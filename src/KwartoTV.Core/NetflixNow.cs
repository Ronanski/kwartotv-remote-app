using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// What Netflix plays on the TV, for the phone's Netflix controls: title, episode, position and length (seconds),
/// paused, speed, and whether Netflix asks "Are you still watching?". Also Netflix's episodes, for the phone's picker.
/// The page's reports are checked and cleaned here before they go to the phones.
/// </summary>
public static class NetflixNow
{
    public static readonly string[] Speeds = { "0.5", "0.75", "1", "1.25", "1.5" };
    public static bool IsSpeed(string? s) => s is not null && Array.IndexOf(Speeds, s) >= 0;

    private static string Clean(string? s, int max)
    {
        s = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length > max ? s[..max] : s;
    }

    private static double Num(JsonElement o, string k, double max) =>
        o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d >= 0 && d <= max ? d : 0;

    private static bool Flag(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The page's report {t, e, pos, dur, paused, rate, still} as the phone's message; null when it is not one.</summary>
    public static string? Message(string? pageJson)
    {
        if (string.IsNullOrWhiteSpace(pageJson)) return null;
        try
        {
            using var d = JsonDocument.Parse(pageJson);
            var o = d.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            var dur = Math.Round(Num(o, "dur", 86400));
            var pos = Math.Min(Math.Round(Num(o, "pos", 86400)), dur > 0 ? dur : 86400);
            var rate = Num(o, "rate", 4);
            return JsonSerializer.Serialize(new
            {
                type = "netflix.now",
                title = Clean(Str(o, "t"), 120),
                episode = Clean(Str(o, "e"), 120),
                pos = (int)pos, dur = (int)dur,
                paused = Flag(o, "paused"),
                rate = rate > 0 ? rate : 1,
                still = Flag(o, "still"),
            });
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The page's episodes {show, seasons:[{n, eps:[{id, n, on}]}]} as the phone's message "netflix.episodes"
    /// (ids are Netflix's numbers; at most 30 seasons of 200 episodes). Null when there are none.
    /// </summary>
    public static string? EpisodesMessage(string? pageJson)
    {
        if (string.IsNullOrWhiteSpace(pageJson)) return null;
        try
        {
            using var d = JsonDocument.Parse(pageJson);
            var o = d.RootElement;
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("seasons", out var ss) || ss.ValueKind != JsonValueKind.Array) return null;
            var seasons = new List<object>();
            foreach (var s in ss.EnumerateArray().Take(30))
            {
                if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("eps", out var es) || es.ValueKind != JsonValueKind.Array) continue;
                var eps = new List<object>();
                foreach (var e in es.EnumerateArray().Take(200))
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var id = e.TryGetProperty("id", out var iv) ? (iv.ValueKind == JsonValueKind.Number ? iv.GetRawText() : iv.ValueKind == JsonValueKind.String ? iv.GetString() : null) : null;
                    if (id is not { Length: >= 1 and <= 12 } || !id.All(char.IsAsciiDigit)) continue;
                    eps.Add(new { id, n = Clean(Str(e, "n"), 100) is { Length: > 0 } n ? n : "Episode", on = Flag(e, "on") });
                }
                if (eps.Count > 0) seasons.Add(new { n = Clean(Str(s, "n"), 60) is { Length: > 0 } sn ? sn : "Season " + (seasons.Count + 1), eps });
            }
            if (seasons.Count == 0) return null;
            return JsonSerializer.Serialize(new { type = "netflix.episodes", show = Clean(Str(o, "show"), 120), seasons });
        }
        catch (JsonException) { return null; }
    }
}
