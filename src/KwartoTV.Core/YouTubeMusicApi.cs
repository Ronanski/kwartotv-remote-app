using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// YouTube Music's own search and "up next" (a radio of similar songs), asked for directly (no page needed). The TV
/// plays the songs through its YouTube app, with that app's sign-in (Premium: no ads), and keeps the queue itself.
/// </summary>
public static class YouTubeMusicApi
{
    public const string SearchUrl = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";
    public const string NextUrl = "https://music.youtube.com/youtubei/v1/next?prettyPrint=false";

    private static object Context => new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20241023.01.00", hl = "en", gl = "PH" } };

    /// <summary>Songs only (the "Songs" filter of YouTube Music's search).</summary>
    public static string SearchBody(string query) => JsonSerializer.Serialize(new { context = Context, query, @params = "EgWKAQIIAWoKEAkQBRAKEAMQBA==" });

    /// <summary>The radio that starts with this song.</summary>
    public static string NextBody(string videoId) => JsonSerializer.Serialize(new { context = Context, videoId, playlistId = "RDAMVM" + videoId, isAudioOnly = true });

    /// <summary>The page of YouTube on the TV (its TV app) that plays one song.</summary>
    public static string TvWatchUrl(string videoId) => "https://www.youtube.com/tv#/watch?v=" + videoId;

    private static string Runs(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return "";
        return string.Concat(runs.EnumerateArray().Select(r => r.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : ""));
    }

    private static string FirstRun(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return "";
        foreach (var r in runs.EnumerateArray())
            if (r.TryGetProperty("text", out var t) && t.GetString() is { Length: > 0 } s && s.Trim() is { Length: > 0 } x && x != "•") return x;
        return "";
    }

    private static JsonElement Path(JsonElement e, params string[] keys)
    {
        foreach (var k in keys) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out e)) return default; }
        return e;
    }

    private static void Walk(JsonElement e, string key, List<JsonElement> found, int max)
    {
        if (found.Count >= max) return;
        if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Walk(x, key, found, max); return; }
        if (e.ValueKind != JsonValueKind.Object) return;
        if (e.TryGetProperty(key, out var hit)) { found.Add(hit); return; }
        foreach (var p in e.EnumerateObject()) Walk(p.Value, key, found, max);
    }

    private static MusicTrack? Track(string? id, string title, string artist)
    {
        title = Clean(title); artist = Clean(artist);
        return Music.IsTrackId(id) && title.Length > 0 ? new MusicTrack(id!, title, artist) : null;
    }

    private static string Clean(string s)
    {
        s = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length <= 120 ? s : s[..120];
    }

    /// <summary>The songs of a search answer (musicResponsiveListItemRenderer rows), unique, at most 20.</summary>
    public static IReadOnlyList<MusicTrack> ParseSearch(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var rows = new List<JsonElement>(); Walk(d.RootElement, "musicResponsiveListItemRenderer", rows, 60);
            var list = new List<MusicTrack>();
            foreach (var r in rows)
            {
                var id = Path(r, "playlistItemData", "videoId").ValueKind == JsonValueKind.String ? Path(r, "playlistItemData", "videoId").GetString()
                    : Path(r, "overlay", "musicItemThumbnailOverlayRenderer", "content", "musicPlayButtonRenderer", "playNavigationEndpoint", "watchEndpoint", "videoId") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
                var cols = r.TryGetProperty("flexColumns", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().ToList() : new();
                string Col(int i) => i < cols.Count ? Runs(Path(cols[i], "musicResponsiveListItemFlexColumnRenderer", "text")) : "";
                var artist = Col(1).Split(" • ")[0];
                if (Track(id, Col(0), artist) is { } t && list.All(x => x.Id != t.Id)) list.Add(t);
                if (list.Count >= Music.MaxResults) break;
            }
            return list;
        }
        catch (JsonException) { return Array.Empty<MusicTrack>(); }
    }

    /// <summary>The songs of an "up next" answer (playlistPanelVideoRenderer rows), in order, unique, at most 50.</summary>
    public static IReadOnlyList<MusicTrack> ParseNext(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var rows = new List<JsonElement>(); Walk(d.RootElement, "playlistPanelVideoRenderer", rows, 80);
            var list = new List<MusicTrack>();
            foreach (var r in rows)
            {
                var id = r.TryGetProperty("videoId", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var artist = FirstRun(Path(r, "shortBylineText"));
                if (artist.Length == 0) artist = FirstRun(Path(r, "longBylineText"));
                if (Track(id, Runs(Path(r, "title")), artist) is { } t && list.All(x => x.Id != t.Id)) list.Add(t);
                if (list.Count >= 50) break;
            }
            return list;
        }
        catch (JsonException) { return Array.Empty<MusicTrack>(); }
    }
}
