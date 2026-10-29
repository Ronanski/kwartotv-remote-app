using System.Text;

namespace KwartoTV.Core;

/// <summary>Loads the playlists the user added and builds the lineup from the channels the user selected.</summary>
public static class ChannelCatalog
{
    public static IReadOnlyList<Channel> LoadFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Playlist file not found.", path);
        if (info.Length > PlaylistLoader.MaxBytes) throw new InvalidDataException("Playlist is too large.");
        var text = File.ReadAllText(path, Encoding.UTF8);
        return PlaylistLoader.Interpret(text, new Uri(Path.GetFullPath(path)));
    }

    /// <summary>All channels of one playlist (a link, a file, or a single stream). Throws with a readable message on failure.</summary>
    public static async Task<IReadOnlyList<Channel>> LoadOneAsync(PlaylistEntry p, HttpClient http, PlaylistLoader.FallbackDownload? fallback = null, CancellationToken ct = default)
    {
        if (p.IsStream)
        {
            if (!Uri.TryCreate(p.Source, UriKind.Absolute, out var s) || !M3uParser.IsStreamUri(s)) throw new ArgumentException("Not a stream address.");
            return new[] { new Channel(Channel.StableId(p.Source), p.Name, p.Source, null, "Streams", Channel.DetectKind(p.Source)) };
        }
        if (Uri.TryCreate(p.Source, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeFile)
            return await PlaylistLoader.LoadAsync(p.Source, http, IptvOrg.Mirrors, PlaylistLoader.AttemptTimeout, ct, fallback);
        return LoadFile(p.Source);
    }

    /// <summary>The numbered lineup: only the channels the user selected, playlist by playlist, without duplicate addresses.</summary>
    public static IReadOnlyList<Channel> BuildLineup(IEnumerable<PlaylistEntry> playlists, IReadOnlyDictionary<string, IReadOnlyList<Channel>> loaded,
        IReadOnlyDictionary<string, ChannelHealth>? health = null)
    {
        var result = new List<Channel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in playlists)
        {
            if (!loaded.TryGetValue(p.Id, out var all)) continue;
            var chosen = p.Selected is null ? null : new HashSet<string>(p.Selected);
            foreach (var c in all)
                if ((chosen is null || chosen.Contains(c.Id)) && !IsDead(c, health) && seen.Add(c.Url)) result.Add(c);
        }
        return result;
    }

    /// <summary>Channels found not to work on this PC are left out (when the user keeps unavailable channels hidden).</summary>
    public static bool IsDead(Channel c, IReadOnlyDictionary<string, ChannelHealth>? health) =>
        health is not null && health.TryGetValue(c.Id, out var h) && h.Status == HealthStatus.Dead;
}
