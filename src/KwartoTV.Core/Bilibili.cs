using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>A show (anime/drama season) or a video on Bilibili, as a card on the TV.</summary>
public sealed record BiliItem(bool IsSeason, string Id, string Title, string? Cover, string? Badge)
{
    /// <summary>The page that plays it (the in-app player shows only the video).</summary>
    public string PlayUrl => IsSeason ? $"https://www.bilibili.tv/en/play/{Id}" : $"https://www.bilibili.tv/en/video/{Id}";
}

/// <summary>A row of cards ("For you", "Trending anime" ...).</summary>
public sealed record BiliRow(string Title, IReadOnlyList<BiliItem> Items);

/// <summary>
/// Bilibili (bilibili.tv) as a TV screen: the same public lists its website shows (no sign-in needed), read as JSON
/// and drawn by KwartoTV as rows of posters; playing opens the episode in the in-app player, video only.
/// </summary>
public static class Bilibili
{
    private const string Api = "https://api.bilibili.tv/intl/gateway/web/v2/";
    private const string Common = "s_locale=en_US&platform=web";

    public static string AnimePageUrl(int page) => $"{Api}anime/page?{Common}&page_type=4&pn={page}";
    public static string RecommendUrl => $"{Api}home/recommend?{Common}&pn=1&ps=40";
    public static string SearchUrl(string query) => $"{Api}search_v2?{Common}&keyword={Uri.EscapeDataString(query)}&highlight=0&pn=1&ps=30&qid=&sort=0&duration_type=0";

    public static bool IsId(string? s) => s is { Length: > 0 and <= 24 } && s.All(char.IsAsciiDigit);

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;

    private static string Clean(string? s, int max = 120)
    {
        s = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length <= max ? s : s[..max];
    }

    private static string? Cover(JsonElement e)
    {
        var c = Str(e, "cover") ?? Str(e, "horizontal_cover") ?? Str(e, "vertical_cover");
        return Uri.TryCreate(c, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? c : null;
    }

    /// <summary>A card from any Bilibili list: a season (season_id) or a video (aid). Null when it is neither.</summary>
    private static BiliItem? Item(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var title = Clean(Str(e, "title"));
        if (title.Length == 0) return null;
        var badge = e.TryGetProperty("corner_mark", out var cm) && cm.ValueKind == JsonValueKind.Object ? Clean(Str(cm, "text"), 24) : null;
        if (string.IsNullOrEmpty(badge)) badge = null;
        var season = Str(e, "season_id");
        if (IsId(season)) return new BiliItem(true, season!, title, Cover(e), badge);
        var aid = Str(e, "aid");
        if (IsId(aid)) return new BiliItem(false, aid!, title, Cover(e), badge);
        return null;
    }

    private static JsonElement? Data(string json, JsonDocument doc)
    {
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("code", out var code) || code.GetRawText() != "0") return null;
        return r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : null;
    }

    /// <summary>The anime page: {"data":{"modules":[{"header":{"title":..},"cards":[..]}]}} → rows (empty ones left out).</summary>
    public static IReadOnlyList<BiliRow> ParseModules(string json)
    {
        var rows = new List<BiliRow>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (Data(json, doc) is not { } d || !d.TryGetProperty("modules", out var mods) || mods.ValueKind != JsonValueKind.Array) return rows;
            foreach (var m in mods.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                var title = m.TryGetProperty("header", out var h) && h.ValueKind == JsonValueKind.Object ? Clean(Str(h, "title"), 60) : "";
                var list = m.TryGetProperty("cards", out var c) ? c : m.TryGetProperty("items", out var it) ? it : default;
                if (list.ValueKind != JsonValueKind.Array) continue;
                var items = list.EnumerateArray().Select(Item).OfType<BiliItem>().DistinctBy(i => (i.IsSeason, i.Id)).Take(40).ToList();
                if (items.Count > 0) rows.Add(new BiliRow(title.Length > 0 ? title : "More", items));
            }
        }
        catch (JsonException) { }
        return rows;
    }

    /// <summary>Home recommendations: {"data":{"cards":[..]}} → cards.</summary>
    public static IReadOnlyList<BiliItem> ParseCards(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (Data(json, doc) is not { } d || !d.TryGetProperty("cards", out var c) || c.ValueKind != JsonValueKind.Array) return Array.Empty<BiliItem>();
            return c.EnumerateArray().Select(Item).OfType<BiliItem>().DistinctBy(i => (i.IsSeason, i.Id)).Take(40).ToList();
        }
        catch (JsonException) { return Array.Empty<BiliItem>(); }
    }

    /// <summary>Search: modules of items; shows (seasons) first, then videos.</summary>
    public static IReadOnlyList<BiliItem> ParseSearch(string json) =>
        ParseModules(json).SelectMany(r => r.Items).DistinctBy(i => (i.IsSeason, i.Id)).OrderByDescending(i => i.IsSeason).Take(40).ToList();
}
