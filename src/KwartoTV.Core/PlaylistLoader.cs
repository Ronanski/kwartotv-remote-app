using System.Net;
using System.Text;

namespace KwartoTV.Core;

/// <summary>Downloads an M3U playlist the user added in Settings (for example a country list) and parses it.</summary>
public static class PlaylistLoader
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static bool IsAllowedSource(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip));

    public static async Task<IReadOnlyList<Channel>> LoadAsync(string url, HttpClient http, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || !IsAllowedSource(uri))
            throw new ArgumentException("Playlist link must be a valid https URL.");

        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is > MaxBytes)
            throw new InvalidDataException("Playlist is too large.");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxBytes) throw new InvalidDataException("Playlist is too large.");
        }
        return M3uParser.Parse(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
