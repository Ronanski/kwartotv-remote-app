using System.Text.RegularExpressions;

namespace KwartoTV.Core;

/// <summary>Parses M3U/M3U8 playlists the user supplies. Contains no built-in channel sources.</summary>
public static class M3uParser
{
    private static readonly Regex Attr = new("([A-Za-z0-9_-]+)=\"([^\"]*)\"", RegexOptions.Compiled);

    public static IReadOnlyList<Channel> Parse(string text)
    {
        var result = new List<Channel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? name = null, logo = null, group = null;
        bool pending = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':');
                var body = colon >= 0 ? line[(colon + 1)..] : "";
                int lastQuote = body.LastIndexOf('"');
                int comma = body.IndexOf(',', lastQuote < 0 ? 0 : lastQuote);
                var attrText = comma >= 0 ? body[..comma] : body;
                var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Attr.Matches(attrText)) attrs[m.Groups[1].Value] = m.Groups[2].Value;
                name = comma >= 0 ? body[(comma + 1)..].Trim() : "";
                if (name.Length == 0) name = attrs.GetValueOrDefault("tvg-name") ?? "";
                logo = attrs.GetValueOrDefault("tvg-logo");
                group = attrs.GetValueOrDefault("group-title");
                pending = true;
                continue;
            }

            if (line.StartsWith('#')) continue;

            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { pending = false; continue; }
            if (!seen.Add(line)) { pending = false; continue; }

            var finalName = pending && !string.IsNullOrWhiteSpace(name)
                ? name!
                : (uri.Segments.LastOrDefault()?.Trim('/') is { Length: > 0 } seg ? seg : uri.Host);
            result.Add(new Channel(
                Channel.StableId(line), finalName, line,
                string.IsNullOrWhiteSpace(logo) ? null : logo,
                string.IsNullOrWhiteSpace(group) ? "Other" : group!,
                Channel.DetectKind(line)));
            name = logo = group = null; pending = false;
        }
        return result;
    }
}
