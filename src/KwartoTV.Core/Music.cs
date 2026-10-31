using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>One song found by a search on the phone.</summary>
public sealed record MusicTrack(string Id, string Title, string Artist);

/// <summary>What the music player on the TV is doing (shown on the lock screen and on the phone).</summary>
public sealed record MusicState(bool On, bool Playing, string Title, string Artist, string? Art)
{
    public static readonly MusicState Off = new(false, false, "", "", null);
}

/// <summary>
/// YouTube Music on the TV, controlled from the phone: it keeps playing behind the lock screen, which shows the song.
/// Commands: music.open, music.playpause, music.next, music.prev, music.stop, music.search (text), music.play (song id).
/// </summary>
public static class Music
{
    public const string Home = "https://music.youtube.com/";
    public const int MaxQuery = 64;
    public const int MaxResults = 20;

    /// <summary>The page that plays one song (YouTube Music continues with similar songs after it).</summary>
    public static string WatchUrl(string id) => Home + "watch?v=" + id;

    /// <summary>A YouTube video id: 11 letters, digits, - or _.</summary>
    public static bool IsTrackId(string? s) => s is { Length: 11 } && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool IsQuery(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= MaxQuery && !s.Any(char.IsControl);

    private static string Clean(string? s, int max)
    {
        s = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>{"type":"now","title":..,"artist":..,"art":..,"playing":bool} from the page; null when it is not that.</summary>
    public static MusicState? ParseNow(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("type", out var t) || t.GetString() != "now") return null;
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var art = S("art");
            return new MusicState(true, r.TryGetProperty("playing", out var p) && p.ValueKind == JsonValueKind.True, Clean(S("title"), 120), Clean(S("artist"), 120),
                Uri.TryCreate(art, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? art : null);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>{"type":"results","q":..,"items":[{"id","t","a"}]} from the page: the songs (valid ids only), or null.</summary>
    public static (string Query, IReadOnlyList<MusicTrack> Tracks, bool Error)? ParseResults(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("type", out var t) || t.GetString() != "results") return null;
            var q = r.TryGetProperty("q", out var qv) && qv.ValueKind == JsonValueKind.String ? Clean(qv.GetString(), MaxQuery) : "";
            var list = new List<MusicTrack>();
            var seen = new HashSet<string>();
            if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var i in items.EnumerateArray())
                {
                    if (list.Count >= MaxResults || i.ValueKind != JsonValueKind.Object) break;
                    string S(string k) => i.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    var id = S("id");
                    if (!IsTrackId(id) || !seen.Add(id)) continue;
                    var title = Clean(S("t"), 120);
                    if (title.Length == 0) continue;
                    list.Add(new MusicTrack(id, title, Clean(S("a"), 120)));
                }
            return (q, list, r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True);
        }
        catch (JsonException) { return null; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>{"type":"music","on":..,"playing":..,"title":..,"artist":..,"art":..} for the phones.</summary>
    public static string StateMessage(MusicState s) =>
        JsonSerializer.Serialize(new { type = "music", on = s.On, playing = s.Playing, title = s.Title, artist = s.Artist, art = s.Art }, Json);

    /// <summary>{"type":"music.results","q":..,"error":..,"items":[{"id","title","artist"}]} for the phones.</summary>
    public static string ResultsMessage(string query, IReadOnlyList<MusicTrack> tracks, bool error = false) =>
        JsonSerializer.Serialize(new { type = "music.results", q = query, error, items = tracks.Select(t => new { id = t.Id, title = t.Title, artist = t.Artist }) }, Json);
}
