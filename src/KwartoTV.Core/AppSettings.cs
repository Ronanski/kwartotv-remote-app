using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>A playlist the user added. Source is an https link or a local .m3u/.m3u8 file path.</summary>
public sealed record PlaylistEntry(string Id, string Name, string Source);

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
    /// <summary>Volume of the built-in player, 0-130.</summary>
    public int Volume { get; set; } = 100;

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
        Playlists.Add(new PlaylistEntry(Channel.StableId(source), Clean(name, source), source));
        return null;
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
