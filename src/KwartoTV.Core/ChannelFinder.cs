using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>Where a scan of every channel is: how many were checked, and how many work.</summary>
public sealed record ScanProgress(bool Running, int Done, int Total, int Ok, int Dead)
{
    public static readonly ScanProgress Idle = new(false, 0, 0, 0, 0);
}

/// <summary>
/// The channel finder used by the phone: every channel of every playlist is checked from the TV PC, and only the ones
/// that work are listed, by category, so the user picks from channels that really play (and marks favorites).
/// </summary>
public static class ChannelFinder
{
    /// <summary>Most working channels sent to a phone in one message.</summary>
    public const int MaxChannels = 6000;

    /// <summary>Every channel of every playlist once (by address), with the playlist it comes from; the first playlist wins.</summary>
    public static List<(Channel Channel, PlaylistEntry Playlist)> All(IEnumerable<PlaylistEntry> playlists, IReadOnlyDictionary<string, IReadOnlyList<Channel>> loaded)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(Channel, PlaylistEntry)>();
        foreach (var p in playlists)
        {
            if (!loaded.TryGetValue(p.Id, out var chs)) continue;
            foreach (var c in chs) if (seen.Add(c.Url)) list.Add((c, p));
        }
        return list;
    }

    /// <summary>
    /// {"type":"catalog","id":..,"scan":{..},"checked":N,"all":N,"total":N,"channels":[{"i":id,"n":name,"g":group,"s":chosen,"f":favorite}]}.
    /// Only channels that work here, sorted by category (TV order) and name. Short keys keep thousands of rows small.
    /// </summary>
    public static string CatalogMessage(IReadOnlyList<(Channel Channel, PlaylistEntry Playlist)> all, IReadOnlyDictionary<string, ChannelHealth> health,
        Func<string, bool> isChosen, Func<string, bool> isFavorite, ScanProgress scan, string? id = null)
    {
        static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
        int checkedCount = 0;
        var working = new List<Channel>();
        foreach (var (c, _) in all)
        {
            if (!health.TryGetValue(c.Id, out var h) || h.Status == HealthStatus.Unknown) continue;
            checkedCount++;
            if (h.Status == HealthStatus.Ok) working.Add(c);
        }
        var rows = working
            .OrderBy(c => ChannelCurator.CategoryRank(c.Group)).ThenBy(c => c.Group, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxChannels)
            .Select(c => new { i = c.Id, n = Cut(c.Name, 80), g = Cut(c.Group, 40), s = isChosen(c.Id), f = isFavorite(c.Id) })
            .ToList();
        return JsonSerializer.Serialize(new
        {
            type = "catalog", id, scan = ScanJson(scan), @checked = checkedCount, all = all.Count, total = working.Count, channels = rows,
        });
    }

    /// <summary>{"type":"scan","running":true,"done":..,"total":..,"ok":..,"dead":..}</summary>
    public static string ScanMessage(ScanProgress s) => JsonSerializer.Serialize(new { type = "scan", s.Running, s.Done, s.Total, s.Ok, s.Dead }, Web);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static object ScanJson(ScanProgress s) => new { running = s.Running, done = s.Done, total = s.Total, ok = s.Ok, dead = s.Dead };

    /// <summary>Reads "1:value" (on) or "0:value" (off), as sent by the phone for choose/favorite commands.</summary>
    public static bool TryParseToggle(string? arg, out bool on, out string value)
    {
        on = false; value = "";
        if (arg is null || arg.Length < 3 || arg[1] != ':' || arg[0] is not ('0' or '1')) return false;
        on = arg[0] == '1'; value = arg[2..];
        return value.Length > 0 && !value.Any(char.IsControl);
    }

    /// <summary>A channel id (see <see cref="Channel.StableId"/>): letters, digits, - and _, at most 40.</summary>
    public static bool IsChannelId(string s) => s.Length is >= 1 and <= 40 && s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}
