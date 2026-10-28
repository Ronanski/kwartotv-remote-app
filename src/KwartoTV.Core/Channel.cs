using System.Security.Cryptography;
using System.Text;

namespace KwartoTV.Core;

public enum StreamKind { Hls, Direct, YouTube }

public sealed record Channel(string Id, string Name, string Url, string? LogoUrl, string Group, StreamKind Kind)
{
    public static StreamKind DetectKind(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("youtube.com") || u.Contains("youtu.be")) return StreamKind.YouTube;
        var path = u.Split('?')[0];
        return path.EndsWith(".m3u8") ? StreamKind.Hls : StreamKind.Direct;
    }

    public static string StableId(string url)
    {
        var h = SHA1.HashData(Encoding.UTF8.GetBytes(url.Trim()));
        return Convert.ToHexString(h, 0, 5).ToLowerInvariant();
    }
}
