using System.Net;
using System.Text;

namespace KwartoTV.Core;

public enum HealthStatus { Unknown, Ok, Dead }

public sealed record ChannelHealth(HealthStatus Status, string? Reason, DateTimeOffset CheckedAt);

/// <summary>
/// Checks, from this PC and its internet connection, whether a channel can really be watched: the address answers,
/// it is a stream (not a web or login page), and for HLS the playlist leads to a video segment that downloads.
/// Country blocks show up as 403/451 here because the check runs on the same connection the TV uses.
/// </summary>
public static class StreamProbe
{
    private const int MaxText = 512 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<ChannelHealth> CheckAsync(Channel ch, HttpClient http, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(ch.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new(HealthStatus.Unknown, "cannot be checked before playing (" + (uri?.Scheme ?? "bad address") + ")", now);
        if (ch.Kind == StreamKind.YouTube) return new(HealthStatus.Unknown, "YouTube: opens in the YouTube player", now);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            var reason = await Follow(uri, ch, http, 0, limit.Token);
            return reason is null ? new(HealthStatus.Ok, null, now) : new(HealthStatus.Dead, reason, now);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(HealthStatus.Dead, $"no answer in {(int)Timeout.TotalSeconds} seconds", now); }
        catch (HttpRequestException ex) { return new(HealthStatus.Dead, ex.StatusCode is { } s ? Explain(s) : "cannot connect", now); }
        catch (Exception ex) { return new(HealthStatus.Dead, ex.Message, now); }
    }

    public static string Explain(HttpStatusCode s) => (int)s switch
    {
        401 or 403 => $"blocked or needs an account (HTTP {(int)s})",
        451 => "not available in your country (HTTP 451)",
        404 or 410 => $"no longer exists (HTTP {(int)s})",
        429 => "server is refusing too many viewers (HTTP 429)",
        >= 500 => $"server error (HTTP {(int)s})",
        _ => $"HTTP {(int)s}",
    };

    /// <summary>Null when the address leads to playable video; otherwise why not.</summary>
    private static async Task<string?> Follow(Uri uri, Channel ch, HttpClient http, int depth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        if (ch.UserAgent is { } ua) req.Headers.TryAddWithoutValidation("User-Agent", ua);
        else req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        if (ch.Referrer is { } rf) req.Headers.TryAddWithoutValidation("Referer", rf);
        if (depth > 0 && !LooksLikePlaylistPath(uri)) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 65535);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return Explain(resp.StatusCode);

        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[MaxText];
        int got = 0, n;
        while (got < buf.Length && (n = await stream.ReadAsync(buf.AsMemory(got), ct)) > 0) got += n;
        if (got == 0) return "the stream sent nothing";

        var head = Encoding.UTF8.GetString(buf, 0, Math.Min(got, 2048)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        // DASH (.mpd) is XML, not a web page. Protected (DRM) DASH cannot be played by KwartoTV.
        if (type.Contains("dash", StringComparison.OrdinalIgnoreCase) || head.Contains("<MPD", StringComparison.Ordinal))
        {
            var xml = Encoding.UTF8.GetString(buf, 0, got);
            return xml.Contains("ContentProtection", StringComparison.Ordinal) ? "protected (DRM), cannot be played here" : null;
        }
        if (type.Contains("html", StringComparison.OrdinalIgnoreCase) || head.StartsWith('<'))
            return "not a stream (a web page, often a sign-up or 'not available' page)";
        if (!head.StartsWith("#EXTM3U", StringComparison.Ordinal)) return null; // video bytes (TS, MP4, ...)
        if (depth >= 3) return null;

        var text = Encoding.UTF8.GetString(buf, 0, got);
        var next = NextUri(text, uri);
        if (next is null) return "the stream playlist is empty (nothing on air)";
        return await Follow(next, ch, http, depth + 1, ct);
    }

    private static bool LooksLikePlaylistPath(Uri u) => u.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first variant of a master playlist, or the first segment of a media playlist.</summary>
    public static Uri? NextUri(string playlist, Uri baseUri)
    {
        foreach (var raw in playlist.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (Uri.TryCreate(baseUri, line, out var u) && u.Scheme is "http" or "https") return u;
        }
        return null;
    }
}
