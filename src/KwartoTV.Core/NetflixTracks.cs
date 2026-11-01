using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// Netflix's Audio & Subtitles, for the phone (like the menu the Netflix TV app opens during a video): the lists the
/// page reports are checked and passed on as {"type":"netflix.tracks","audio":[{i,n,on}],"subs":[...]}; a pick comes
/// back as netflix.track "a:2" (audio 2) or "s:0" (subtitles 0).
/// </summary>
public static class NetflixTracks
{
    public sealed record Track(int I, string N, bool On);

    /// <summary>A pick from the phone: "a:&lt;n&gt;" or "s:&lt;n&gt;" (n 0..99).</summary>
    public static bool TryParsePick(string? arg, out char kind, out int index)
    {
        kind = ' '; index = -1;
        if (arg is not { Length: >= 3 and <= 4 } || arg[1] != ':' || arg[0] is not ('a' or 's')) return false;
        if (!int.TryParse(arg.AsSpan(2), System.Globalization.NumberStyles.None, null, out index) || index > 99) { index = -1; return false; }
        kind = arg[0];
        return true;
    }

    /// <summary>The page's answer (JSON) as the phone's message; null when it has no lists.</summary>
    public static string? Message(string? pageJson)
    {
        if (string.IsNullOrWhiteSpace(pageJson)) return null;
        try
        {
            using var d = JsonDocument.Parse(pageJson);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return null;
            var audio = Read(d.RootElement, "audio"); var subs = Read(d.RootElement, "subs");
            if (audio.Count == 0 && subs.Count == 0) return null;
            return JsonSerializer.Serialize(new
            {
                type = "netflix.tracks",
                audio = audio.Select(t => new { i = t.I, n = t.N, on = t.On }),
                subs = subs.Select(t => new { i = t.I, n = t.N, on = t.On }),
            });
        }
        catch (JsonException) { return null; }
    }

    private static List<Track> Read(JsonElement root, string key)
    {
        var list = new List<Track>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (list.Count >= 40 || e.ValueKind != JsonValueKind.Object) break;
            if (!e.TryGetProperty("i", out var i) || i.ValueKind != JsonValueKind.Number || !i.TryGetInt32(out var ix) || ix < 0 || ix > 99) continue;
            var n = e.TryGetProperty("n", out var ne) && ne.ValueKind == JsonValueKind.String ? ne.GetString() ?? "" : "";
            n = new string(n.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (n.Length == 0) n = "Track " + (ix + 1);
            if (n.Length > 60) n = n[..60];
            var on = e.TryGetProperty("on", out var oe) && oe.ValueKind == JsonValueKind.True;
            list.Add(new Track(ix, n, on));
        }
        return list;
    }
}
