namespace KwartoTV.Core;

/// <summary>
/// The last few things typed with the on-screen keyboard, per kind of field ("link", "web-search", ...),
/// so they can be picked again with one press. Newest first. Passwords are never stored.
/// </summary>
public sealed class TypingHistory
{
    public const int MaxPerPurpose = 8;
    public const int MaxLength = 300;

    public Dictionary<string, List<string>> Entries { get; set; } = new();

    public IReadOnlyList<string> For(string? purpose) =>
        purpose is not null && Entries.TryGetValue(purpose, out var list) ? list : Array.Empty<string>();

    /// <summary>Adds [text] to the front of the list for [purpose]. Returns false when nothing changed (empty, too long, only a prefix).</summary>
    public bool Remember(string? purpose, string? text)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(purpose) || text.Length == 0 || text.Length > MaxLength || OskText.IsJustPrefix(text)) return false;
        if (!Entries.TryGetValue(purpose, out var list)) Entries[purpose] = list = new List<string>();
        if (list.Count > 0 && list[0] == text) return false;
        list.RemoveAll(e => e == text);
        list.Insert(0, text);
        if (list.Count > MaxPerPurpose) list.RemoveRange(MaxPerPurpose, list.Count - MaxPerPurpose);
        return true;
    }

    /// <summary>The entries worth offering while [typed] is in the field: those that continue it (all of them when it is empty).</summary>
    public IReadOnlyList<string> Suggest(string? purpose, string? typed, int max = 5)
    {
        var t = (typed ?? "").Trim();
        if (OskText.IsJustPrefix(t)) t = "";
        return For(purpose).Where(e => t.Length == 0 || (e.StartsWith(t, StringComparison.OrdinalIgnoreCase) && e.Length > t.Length)).Take(max).ToList();
    }

    public static TypingHistory Load(string path) => JsonFile.LoadOrNew<TypingHistory>(path);
    public void Save(string path) => JsonFile.Save(path, this);
}

/// <summary>Small text rules used by the on-screen keyboard (kept here so they can be tested without a screen).</summary>
public static class OskText
{
    private static readonly string[] Prefixes = { "https://", "http://", "www.", "https://www.", "http://www." };

    /// <summary>True for an empty field or one that only holds a starter such as "https://".</summary>
    public static bool IsJustPrefix(string? text)
    {
        var t = (text ?? "").Trim();
        return t.Length == 0 || Prefixes.Any(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase));
    }

    private const string Digits = "1234567890", DigitAlts = "!@#$%^&*()";

    /// <summary>What holding OK on a key types instead: the other case of a letter, or the symbol printed on the key.</summary>
    public static string? Alternate(string key)
    {
        if (key.Length != 1) return null;
        char c = key[0];
        if (char.IsLetter(c))
        {
            var other = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
            return other == c ? null : other.ToString();
        }
        int d = Digits.IndexOf(c);
        if (d >= 0) return DigitAlts[d].ToString();
        return c switch { '-' => "_", ',' => ";", '.' => ":", '@' => "/", _ => null };
    }

    /// <summary>
    /// The key to land on in the next row: the one under [x] (a key's span is Left..Right), else the nearest one.
    /// Returns -1 for an empty row.
    /// </summary>
    public static int Nearest(IReadOnlyList<(double Left, double Right)> keys, double x)
    {
        int best = -1; double bestGap = double.MaxValue;
        for (int i = 0; i < keys.Count; i++)
        {
            var (l, r) = keys[i];
            double gap = x < l ? l - x : x > r ? x - r : 0;
            if (gap < bestGap) { best = i; bestGap = gap; }
        }
        return best;
    }

    /// <summary>[index] moved by [delta] inside 0..count-1, wrapping round at both ends.</summary>
    public static int Wrap(int index, int delta, int count) => count <= 0 ? 0 : ((index + delta) % count + count) % count;

    /// <summary>
    /// The part of [text] shown in the keyboard's preview: at most [width] characters around the caret,
    /// with "…" where text is cut off. Returns the text before and after the caret.
    /// </summary>
    public static (string Before, string After) Window(string text, int caret, int width = 46)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (text.Length <= width) return (text[..caret], text[caret..]);
        int start = Math.Clamp(caret - width * 3 / 4, 0, text.Length - width);
        int end = start + width;
        var before = text[start..caret];
        var after = text[caret..end];
        if (start > 0) before = "…" + before;
        if (end < text.Length) after += "…";
        return (before, after);
    }
}
