using System.Text.RegularExpressions;

namespace KwartoTV.Core;

/// <summary>
/// Keeps a big public list (thousands of channels from everywhere) to a watchable lineup: the best few channels of
/// each useful category, the ones that work first. Small lists (a country, a language) are kept whole.
/// </summary>
public static class ChannelCurator
{
    /// <summary>Lists with more channels than this are curated, unless the user picked their channels by hand.</summary>
    public const int LargeList = 400;
    public const int PerCategory = 20;

    /// <summary>Categories that are mostly noise on a living-room TV (shopping, parliaments, weather loops, ...).</summary>
    private static readonly HashSet<string> SkipCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auto", "Business", "Culture", "Education", "General", "International", "Legislative", "Other", "Relax", "Religious", "Shop", "Weather",
    };

    /// <summary>Categories shown first in the channel list and on Home (the rest follow alphabetically).</summary>
    public static readonly IReadOnlyList<string> CategoryOrder = new[]
    {
        "Local TV", "News", "Movies", "Series", "Entertainment", "Kids", "Animation", "Sports", "Music", "Documentary", "Lifestyle", "Cooking", "Travel",
    };

    private static readonly Regex Resolution = new(@"\((\d{3,4})p\)", RegexOptions.Compiled);

    public static bool IsLarge(int count) => count > LargeList;

    /// <summary>iptv-org marks channels that only work in their own country, or are not on all day.</summary>
    public static bool IsPartTime(string name) => name.Contains("[Not 24/7]", StringComparison.OrdinalIgnoreCase);
    public static bool IsGeoBlocked(string name) => name.Contains("[Geo-blocked]", StringComparison.OrdinalIgnoreCase);

    /// <summary>The best <see cref="PerCategory"/> channels of each useful category, grouped by category.</summary>
    public static IReadOnlyList<Channel> Best(IEnumerable<Channel> channels, IReadOnlyDictionary<string, ChannelHealth>? health = null) =>
        channels
            .Where(c => !SkipCategories.Contains(c.Group) && !IsPartTime(c.Name) && !IsGeoBlocked(c.Name))
            .OrderBy(c => health is not null && health.TryGetValue(c.Id, out var h) && h.Status == HealthStatus.Ok ? 0 : 1)
            .ThenByDescending(c => Resolution.Match(c.Name) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0)
            .ThenBy(c => string.IsNullOrEmpty(c.LogoUrl) ? 1 : 0)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => CategoryRank(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Take(PerCategory).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public static int CategoryRank(string group)
    {
        for (int i = 0; i < CategoryOrder.Count; i++) if (string.Equals(CategoryOrder[i], group, StringComparison.OrdinalIgnoreCase)) return i;
        return CategoryOrder.Count;
    }
}
