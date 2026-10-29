namespace KwartoTV.Core;

/// <summary>
/// Guesses a category from a channel's name when the playlist has none (many lists put every channel in one
/// group), so the channel list can still be browsed by category.
/// </summary>
public static class AutoCategory
{
    private static readonly (string Category, string[] Words)[] Rules =
    {
        ("Documentary", new[] { "discovery", "animal planet", "nat geo", "national geographic", "documentar", "history", "travel", "outdoor", "nature", "wild" }),
        ("Kids", new[] { "kids", "kid ", "junior", " jr", "disney", "nick", "cartoon", "animation", "animated", "anime", "xilam", "baby", "toon", "mr bean", "mrbean", "japanim", "retro crush" }),
        ("International", new[] { "monde", "tve ", "internacional", "zarok", "nhk world", "kbs world", "rai ", "deutsche welle" }),
        ("News", new[] { "news", "cnn", "bbc", "al jazeera", "aljazeera", "cgtn", "bloomberg", "finance", "abante", "bilyonaryo", "bilyonario", "cltv", "euronews", "france 24", " dw ", "nhk", "arirang", " anc " }),
        ("Sports", new[] { "sport", "nba", "bein", "espn", "fight", "golf", "racing", "football", "pba", "uaap" }),
        ("Movies", new[] { "movie", "cinema", "cine", "film", "thriller", "action", "rakuten", "hbo", "sphere" }),
        ("Religious", new[] { "ewtn", " inc ", "inctv", "inc tv", "3abn", "maria", "church", "gospel", " god", "catholic", "christ", "hope channel", "light tv" }),
        ("Music", new[] { "music", "mtv", "blues", "hits", "radio", "myx" }),
        ("Local TV", new[] { "gma", "kapamilya", "abs-cbn", "abs cbn", "tv5", "ptv", "a2z", "all tv", "net 25", "net25", "ibc", "gtv", "tfc", "mindanow" }),
        ("Entertainment", new[] { "idol", "baywatch", "fanpop", "geek", "style", "comedy", "reality" }),
    };

    public static string? Guess(string name)
    {
        var n = " " + name.ToLowerInvariant().Replace('(', ' ').Replace(')', ' ') + " ";
        foreach (var (category, words) in Rules)
            if (words.Any(w => n.Contains(w, StringComparison.Ordinal))) return category;
        return null;
    }
}
