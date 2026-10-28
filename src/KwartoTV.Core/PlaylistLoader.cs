using System.Net;
using System.Text;

namespace KwartoTV.Core;

/// <summary>Downloads an M3U playlist the user added in Settings (for example a country list) and parses it.</summary>
public static class PlaylistLoader
{
    public const long MaxBytes = 20 * 1024 * 1024;
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(40);

    public static bool IsAllowedSource(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip));

    /// <summary>
    /// Downloads and parses a playlist. Tries the address, then its mirrors (see IptvOrg.Mirrors), each up to [attemptTimeout].
    /// The error lists what happened at every address, so the user can see whether it is DNS, a timeout, or the server.
    /// </summary>
    public static Task<IReadOnlyList<Channel>> LoadAsync(string url, HttpClient http, CancellationToken ct = default) =>
        LoadAsync(url, http, IptvOrg.Mirrors, AttemptTimeout, ct);

    public static async Task<IReadOnlyList<Channel>> LoadAsync(string url, HttpClient http, Func<string, IReadOnlyList<string>> mirrors, TimeSpan attemptTimeout, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || !IsAllowedSource(uri))
            throw new ArgumentException("Playlist link must be a valid https URL.");

        var problems = new List<string>();
        foreach (var candidate in mirrors(uri.AbsoluteUri))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var cu) || !IsAllowedSource(cu)) continue;
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(attemptTimeout);
                var list = await FetchAsync(cu, http, limit.Token);
                if (list.Count == 0) { problems.Add($"{cu.Host}: the list is empty"); continue; }
                return list;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { problems.Add($"{cu.Host}: no answer in {(attemptTimeout.TotalSeconds >= 1 ? $"{(int)attemptTimeout.TotalSeconds} seconds" : $"{(int)attemptTimeout.TotalMilliseconds} ms")}"); }
            catch (HttpRequestException ex) when (ex.StatusCode is not null) { problems.Add($"{cu.Host}: server said {(int)ex.StatusCode}"); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException) { problems.Add($"{cu.Host}: {ex.Message}"); }
        }
        if (problems.Count == 0) throw new ArgumentException("Playlist link must be a valid https URL.");
        // A single 404 from the only address keeps the original exception type that callers (and tests) expect.
        if (problems.Count == 1 && problems[0].Contains("server said 404")) throw new HttpRequestException(problems[0], null, HttpStatusCode.NotFound);
        throw new HttpRequestException("Could not download the playlist. " + string.Join("; ", problems) + ". Check the internet connection; if it works in a browser but not here, try Reload channels.");
    }

    private static async Task<IReadOnlyList<Channel>> FetchAsync(Uri uri, HttpClient http, CancellationToken ct)
    {
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
