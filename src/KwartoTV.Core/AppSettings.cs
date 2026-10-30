using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>A playlist the user added. Source is an https link or a local .m3u/.m3u8 file path.</summary>
/// <summary>
/// A playlist the user added. Source is an https link or a local file path; for a single stream (IsStream) it is the stream address.
/// Selected holds the ids of the channels the user chose from the playlist; null means every channel.
/// </summary>
/// ShowAsSpace gives the playlist its own place: a row and a tile on Home, and a category in the channel list.
public sealed record PlaylistEntry(string Id, string Name, string Source, List<string>? Selected = null, bool IsStream = false, bool ShowAsSpace = false);

public sealed record WebTile(string Id, string Name, string Url);

public sealed class AppSettings
{
    public const int DefaultPort = 8765;

    public List<PlaylistEntry> Playlists { get; set; } = new();
    public List<WebTile> WebTiles { get; set; } = new();
    public List<string> LibraryFolders { get; set; } = new();
    public LineupState Lineup { get; set; } = new(null, null, new List<string>());
    public int Port { get; set; } = DefaultPort;
    public string? NetflixAppId { get; set; }
    /// <summary>Netflix opens inside KwartoTV (website). Turn off to use the Microsoft Store app instead.</summary>
    public bool NetflixInApp { get; set; } = true;
    /// <summary>Channels that were checked and do not work here (blocked, gone, no video) stay out of the lineup.</summary>
    public bool HideUnavailable { get; set; } = true;
    /// <summary>The lineup holds at most this many channels (0 = all); the first playlists come first.</summary>
    public int MaxChannels { get; set; } = 300;
    /// <summary>Free TV guides (XMLTV) for the "on now" line of Live TV; the first one that has a channel wins.</summary>
    public List<string> GuideUrls { get; set; } = new(ProgramGuideSources.Default);

    public static bool IsPlaylistFile(string path) =>
        Path.IsPathRooted(path) && (path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

    public static bool IsHttpsUrl(string url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(u.Host);

    /// <summary>Adds a playlist after validating it. Returns an error message, or null on success.</summary>
    public string? AddPlaylist(string name, string source)
    {
        source = (source ?? "").Trim();
        var okSource = Uri.TryCreate(source, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeFile
            ? PlaylistLoader.IsAllowedSource(u)
            : IsPlaylistFile(source);
        if (!okSource) return "Use an https link or the full path of a .m3u / .m3u8 file.";
        if (Playlists.Any(p => string.Equals(p.Source, source, StringComparison.OrdinalIgnoreCase))) return "That playlist is already added.";
        // Nothing is selected yet: the user chooses the channels (see the channel picker), not the whole list.
        Playlists.Add(new PlaylistEntry(Channel.StableId(source), Clean(name, source), source, new List<string>()));
        return null;
    }

    /// <summary>Adds one stream address (an .m3u8 link, rtmp://, ...) as a channel of its own.</summary>
    public string? AddStream(string name, string url)
    {
        url = (url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !M3uParser.IsStreamUri(u)) return "Use a stream address that starts with http://, https://, rtmp://, rtsp:// or udp://";
        if (Playlists.Any(p => string.Equals(p.Source, url, StringComparison.OrdinalIgnoreCase))) return "That stream is already added.";
        Playlists.Add(new PlaylistEntry(Channel.StableId(url), Clean(name, u.Host), url, null, IsStream: true));
        return null;
    }

    public PlaylistEntry? FindPlaylist(string id) => Playlists.FirstOrDefault(p => p.Id == id);

    public void SetSpace(string playlistId, bool on)
    {
        var i = Playlists.FindIndex(p => p.Id == playlistId);
        if (i >= 0) Playlists[i] = Playlists[i] with { ShowAsSpace = on };
    }

    /// <summary>Every channel of the playlist (also the ones added to it later).</summary>
    public void SetSelectionAll(string playlistId)
    {
        var i = Playlists.FindIndex(p => p.Id == playlistId);
        if (i >= 0) Playlists[i] = Playlists[i] with { Selected = null };
    }

    public void SetSelection(string playlistId, IEnumerable<string> channelIds)
    {
        var i = Playlists.FindIndex(p => p.Id == playlistId);
        if (i >= 0) Playlists[i] = Playlists[i] with { Selected = channelIds.Distinct().ToList() };
    }

    public string? AddWebTile(string name, string url)
    {
        url = (url ?? "").Trim();
        if (!IsHttpsUrl(url)) return "Website must start with https://";
        if (WebTiles.Any(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase))) return "That website is already added.";
        var host = new Uri(url).Host;
        WebTiles.Add(new WebTile(Channel.StableId(url), Clean(name, host), url));
        return null;
    }

    public bool RemovePlaylist(string id) => Playlists.RemoveAll(p => p.Id == id) > 0;
    public bool RemoveWebTile(string id) => WebTiles.RemoveAll(t => t.Id == id) > 0;

    private static string Clean(string name, string fallback)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = fallback;
        return name.Length > 40 ? name[..40] : name;
    }
}

/// <summary>JSON persistence with atomic replace, so a power cut never leaves a half-written file.</summary>
public static class JsonFile
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T LoadOrNew<T>(string path) where T : new()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts) ?? new T() : new T(); }
        catch { return new T(); }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opts));
        File.Move(tmp, path, overwrite: true);
    }
}
