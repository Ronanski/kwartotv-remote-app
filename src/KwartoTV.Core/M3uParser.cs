using System.Text.RegularExpressions;

namespace KwartoTV.Core;

/// <summary>Parses M3U/M3U8 playlists the user supplies. Contains no built-in channel sources.</summary>
public static class M3uParser
{
    private static readonly Regex Attr = new("([A-Za-z0-9_-]+)=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Schemes a stream address may use. Everything else (file:, javascript:, ...) is dropped.</summary>
    public static bool IsStreamUri(Uri uri) =>
        uri.Scheme is "http" or "https" or "rtmp" or "rtmps" or "rtsp" or "udp" or "rtp";

    /// <summary>True when the text is itself one HLS stream (a master or media playlist), not a list of channels.</summary>
    public static bool LooksLikeSingleStream(string text) =>
        text.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal) || text.Contains("#EXT-X-TARGETDURATION", StringComparison.Ordinal);

    public static IReadOnlyList<Channel> Parse(string text, Uri? baseUri = null)
    {
        var result = new List<Channel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? name = null, logo = null, group = null, referrer = null, userAgent = null;
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

            // #EXTVLCOPT:http-referrer=... and http-user-agent=... apply to the next stream (many public lists need them).
            if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
            {
                var opt = line["#EXTVLCOPT:".Length..];
                var eq = opt.IndexOf('=');
                if (eq > 0)
                {
                    var key = opt[..eq].Trim().ToLowerInvariant(); var val = opt[(eq + 1)..].Trim().Trim('"');
                    if (val.Length is > 0 and < 512 && !val.Any(char.IsControl))
                    {
                        if (key is "http-referrer" or "http-referer") referrer = val;
                        else if (key == "http-user-agent") userAgent = val;
                    }
                }
                continue;
            }

            if (line.StartsWith('#')) continue;

            Uri? uri;
            // "/path" counts as an absolute file path on Linux/macOS, so treat a leading slash as relative to the playlist link first.
            bool rootRelative = baseUri is not null && line.StartsWith('/') && !line.StartsWith("//");
            if (rootRelative) uri = null;
            if (rootRelative || (!Uri.TryCreate(line, UriKind.Absolute, out uri) && baseUri is not null && !line.Contains(':')))
            {
                // a relative address in a playlist that came from a link
                if (Uri.TryCreate(baseUri, line, out uri) && uri.IsAbsoluteUri) line = uri.AbsoluteUri;
            }
            if (uri is null || !uri.IsAbsoluteUri || !IsStreamUri(uri))
            { pending = false; referrer = userAgent = null; continue; }
            if (!seen.Add(line)) { pending = false; referrer = userAgent = null; continue; }

            var finalName = pending && !string.IsNullOrWhiteSpace(name)
                ? name!
                : (uri.Segments.LastOrDefault()?.Trim('/') is { Length: > 0 } seg ? seg : uri.Host);
            result.Add(new Channel(
                Channel.StableId(line), finalName, line,
                string.IsNullOrWhiteSpace(logo) ? null : logo,
                string.IsNullOrWhiteSpace(group) ? AutoCategory.Guess(finalName) ?? "Other" : group!,
                Channel.DetectKind(line), referrer, userAgent));
            name = logo = group = referrer = userAgent = null; pending = false;
        }
        return result;
    }
}
