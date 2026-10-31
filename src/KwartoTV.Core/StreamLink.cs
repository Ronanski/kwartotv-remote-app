using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// A stream address as IPTV lists write it. Many put the headers a server wants right in the link, the way VLC, Kodi and
/// TiviMate read them: "https://host/live.m3u8|User-Agent=Mozilla/5.0&amp;Referer=https://site/". Without splitting that off,
/// the whole text is sent as the address and the server refuses it.
/// </summary>
public sealed record StreamLink(string Url, string? Referrer, string? UserAgent, string? Headers)
{
    /// <summary>Splits "address|Header=value&amp;Header2=value2" (values may be URL-encoded). A plain address comes back as is.</summary>
    public static StreamLink Parse(string raw)
    {
        raw = (raw ?? "").Trim();
        var bar = raw.IndexOf('|');
        if (bar < 0) return new(raw, null, null, null);
        var url = raw[..bar].Trim();
        string? referrer = null, ua = null;
        var extra = new List<string>();
        foreach (var part in raw[(bar + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Decode(part[..eq]).Trim();
            var val = Decode(part[(eq + 1)..]).Trim().Trim('"');
            Add(key, val, ref referrer, ref ua, extra);
        }
        return new(url, referrer, ua, extra.Count == 0 ? null : string.Join("\n", extra));
    }

    /// <summary>#EXTHTTP:{"User-Agent":"...","Referer":"...","Cookie":"..."} (TiviMate and others).</summary>
    public static StreamLink FromJson(string json)
    {
        string? referrer = null, ua = null;
        var extra = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) Add(p.Name, p.Value.GetString() ?? "", ref referrer, ref ua, extra);
        }
        catch (JsonException) { }
        return new("", referrer, ua, extra.Count == 0 ? null : string.Join("\n", extra));
    }

    /// <summary>One header: User-Agent and Referer have their own places, a few others (Origin, Cookie, ...) are passed on.</summary>
    internal static void Add(string key, string val, ref string? referrer, ref string? ua, List<string> extra)
    {
        if (val.Length is 0 or > 1024 || val.Any(char.IsControl) || key.Length is 0 or > 64 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return;
        switch (key.ToLowerInvariant())
        {
            case "user-agent" or "useragent" or "http-user-agent": ua = val; break;
            case "referer" or "referrer" or "http-referrer" or "http-referer": referrer = val; break;
            default:
                if (extra.Count < 8 && !extra.Any(h => h.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))) extra.Add($"{key}: {val}");
                break;
        }
    }

    /// <summary>The extra headers as (name, value) pairs.</summary>
    public static IEnumerable<(string Name, string Value)> Pairs(string? headers)
    {
        if (string.IsNullOrEmpty(headers)) yield break;
        foreach (var line in headers.Split('\n'))
        {
            var c = line.IndexOf(':');
            if (c > 0) yield return (line[..c].Trim(), line[(c + 1)..].Trim());
        }
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); } catch { return s; }
    }
}
