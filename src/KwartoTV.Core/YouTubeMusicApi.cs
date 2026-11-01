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

    public const string BrowseUrl = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";

    /// <summary>YouTube Music's home (Quick picks, charts, moods) as a visitor.</summary>
    public static string HomeBody() => JsonSerializer.Serialize(new { context = Context, browseId = "FEmusic_home" });

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

    /// <summary>The biggest https picture of a thumbnail list (album art).</summary>
    private static string? Art(JsonElement thumbs)
    {
        if (thumbs.ValueKind != JsonValueKind.Array) return null;
        var url = thumbs.EnumerateArray().Select(t => t.TryGetProperty("url", out var u) ? u.GetString() : null).LastOrDefault(u => u is not null && u.StartsWith("https://"));
        return url;
    }

    private static MusicTrack? FromListItem(JsonElement r)
    {
        var id = Path(r, "playlistItemData", "videoId").ValueKind == JsonValueKind.String ? Path(r, "playlistItemData", "videoId").GetString()
            : Path(r, "overlay", "musicItemThumbnailOverlayRenderer", "content", "musicPlayButtonRenderer", "playNavigationEndpoint", "watchEndpoint", "videoId") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
        var cols = r.TryGetProperty("flexColumns", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().ToList() : new();
        string Col(int i) => i < cols.Count ? Runs(Path(cols[i], "musicResponsiveListItemFlexColumnRenderer", "text")) : "";
        var t = Track(id, Col(0), Col(1).Split(" • ")[0]);
        return t is null ? null : t with { Art = Art(Path(r, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")) };
    }

    private static MusicTrack? FromTwoRow(JsonElement r)
    {
        var id = Path(r, "navigationEndpoint", "watchEndpoint", "videoId") is { ValueKind: JsonValueKind.String } a ? a.GetString()
            : Path(r, "thumbnailOverlay", "musicItemThumbnailOverlayRenderer", "content", "musicPlayButtonRenderer", "playNavigationEndpoint", "watchEndpoint", "videoId") is { ValueKind: JsonValueKind.String } b ? b.GetString() : null;
        var t = Track(id, Runs(Path(r, "title")), FirstRun(Path(r, "subtitle")));
        return t is null ? null : t with { Art = Art(Path(r, "thumbnailRenderer", "musicThumbnailRenderer", "thumbnail", "thumbnails")) };
    }

    /// <summary>The home rows: each carousel with its title and its playable songs (albums and playlists left out).</summary>
    public static IReadOnlyList<MusicRow> ParseHome(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var shelves = new List<JsonElement>(); Walk(d.RootElement, "musicCarouselShelfRenderer", shelves, 20);
            var rows = new List<MusicRow>();
            foreach (var s in shelves)
            {
                var title = Clean(Runs(Path(s, "header", "musicCarouselShelfBasicHeaderRenderer", "title")));
                if (!s.TryGetProperty("contents", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                var tracks = new List<MusicTrack>();
                foreach (var i in items.EnumerateArray())
                {
                    MusicTrack? t = i.TryGetProperty("musicResponsiveListItemRenderer", out var li) ? FromListItem(li)
                        : i.TryGetProperty("musicTwoRowItemRenderer", out var tr) ? FromTwoRow(tr) : null;
                    if (t is not null && tracks.All(x => x.Id != t.Id)) tracks.Add(t);
                }
                if (tracks.Count > 0) rows.Add(new MusicRow(title.Length > 0 ? title : "Music", tracks.Take(30).ToList()));
            }
            return rows;
        }
        catch (JsonException) { return Array.Empty<MusicRow>(); }
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
                if (FromListItem(r) is { } t && list.All(x => x.Id != t.Id)) list.Add(t);
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
