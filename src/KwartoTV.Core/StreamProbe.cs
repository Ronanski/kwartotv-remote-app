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
        if (ch.Kind == StreamKind.YouTube) return IsYouTubeLiveLink(uri) ? await CheckYouTubeLiveAsync(uri, http, now, ct) : new(HealthStatus.Unknown, "YouTube: opens in the YouTube player", now);
        // A busy server or a slow moment should not hide a good channel: transient failures get one more try.
        string? reason = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(Timeout);
                reason = await Follow(uri, ch, http, 0, false, limit.Token);
                if (reason is null) return new(HealthStatus.Ok, null, now);
                if (!IsTransient(reason)) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { reason = $"no answer in {(int)Timeout.TotalSeconds} seconds"; }
            catch (HttpRequestException ex) { reason = ex.StatusCode is { } s ? Explain(s) : "cannot connect"; if (ex.StatusCode is not null && !IsTransient(reason)) break; }
            catch (Exception ex) { reason = ex.Message; break; }
        }
        return new(HealthStatus.Dead, reason, now);
    }

    /// <summary>A channel's live link ("https://www.youtube.com/@name/live"): it plays only while the channel is live.</summary>
    public static bool IsYouTubeLiveLink(Uri uri) =>
        uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.TrimEnd('/').EndsWith("/live", StringComparison.OrdinalIgnoreCase);

    /// <summary>The page of a live link says whether the channel is live right now.</summary>
    public static bool YouTubePageIsLive(string html) =>
        html.Contains("\"isLiveNow\":true", StringComparison.Ordinal) || html.Contains("\"isLive\":true", StringComparison.Ordinal);

    public const string NotLiveNow = "not live right now";

    private static async Task<ChannelHealth> CheckYouTubeLiveAsync(Uri uri, HttpClient http, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            using var resp = await http.SendAsync(req, limit.Token);
            if (!resp.IsSuccessStatusCode) return new(HealthStatus.Dead, Explain(resp.StatusCode), now);
            var html = await resp.Content.ReadAsStringAsync(limit.Token);
            return YouTubePageIsLive(html) ? new(HealthStatus.Ok, null, now) : new(HealthStatus.Dead, NotLiveNow, now);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(HealthStatus.Dead, "no answer in 20 seconds", now); }
        catch (HttpRequestException ex) { return new(HealthStatus.Dead, ex.StatusCode is { } s ? Explain(s) : "cannot connect", now); }
    }

    private static bool IsTransient(string reason) =>
        reason.StartsWith("no answer", StringComparison.Ordinal) || reason == "cannot connect" || reason.Contains("HTTP 429") || reason.StartsWith("server error", StringComparison.Ordinal);

    public static string Explain(HttpStatusCode s) => (int)s switch
    {
        401 or 403 => $"blocked or needs an account (HTTP {(int)s})",
        451 => "not available in your country (HTTP 451)",
        404 or 410 => $"no longer exists (HTTP {(int)s})",
        429 => "server is refusing too many viewers (HTTP 429)",
        >= 500 => $"server error (HTTP {(int)s})",
        _ => $"HTTP {(int)s}",
    };

    private sealed record Response(int Status, string Type, byte[] Body, int Length);

    private static async Task<Response> Fetch(Uri uri, Channel ch, HttpClient http, bool range, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("User-Agent", ch.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        if (ch.Referrer is { } rf) req.Headers.TryAddWithoutValidation("Referer", rf);
        if (range) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 65535);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!resp.IsSuccessStatusCode) return new((int)resp.StatusCode, type, Array.Empty<byte>(), 0);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[MaxText];
        int got = 0, n;
        while (got < buf.Length && (n = await stream.ReadAsync(buf.AsMemory(got), ct)) > 0) got += n;
        return new((int)resp.StatusCode, type, buf, got);
    }

    /// <summary>
    /// Null when the address leads to real video; otherwise why not. Strict in the way the iPTV-Scanner project is:
    /// a server that answers is not enough, the final bytes must look like media (or be an encrypted HLS segment).
    /// </summary>
    private static async Task<string?> Follow(Uri uri, Channel ch, HttpClient http, int depth, bool encrypted, CancellationToken ct)
    {
        bool range = depth > 0 && !LooksLikePlaylistPath(uri);
        var r = await Fetch(uri, ch, http, range, ct);
        if (range && r.Status is 400 or 405 or 416 or 501) r = await Fetch(uri, ch, http, false, ct);   // some servers refuse byte ranges
        if (r.Status is < 200 or >= 300) return Explain((HttpStatusCode)r.Status);
        if (r.Length == 0) return "the stream sent nothing";

        var buf = r.Body; int got = r.Length; var type = r.Type;
        var head = Encoding.UTF8.GetString(buf, 0, Math.Min(got, 2048)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');

        // DASH (.mpd) is XML, not a web page. Protected (DRM) DASH cannot be played by KwartoTV.
        if (type.Contains("dash", StringComparison.OrdinalIgnoreCase) || head.Contains("<MPD", StringComparison.Ordinal))
            return Encoding.UTF8.GetString(buf, 0, got).Contains("ContentProtection", StringComparison.Ordinal) ? "protected (DRM), cannot be played here" : null;

        if (head.StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            if (depth >= 4) return null;
            var text = Encoding.UTF8.GetString(buf, 0, got);
            var drm = ProtectionOf(text);
            if (drm == Protection.Drm) return "protected (DRM), cannot be played here";
            var next = NextUri(text, uri);
            if (next is null) return "the stream playlist is empty (nothing on air)";
            return await Follow(next, ch, http, depth + 1, encrypted || drm == Protection.Aes128, ct);
        }

        if (LooksLikeMedia(buf, got)) return null;
        if (type.Contains("html", StringComparison.OrdinalIgnoreCase) || head.StartsWith('<'))
            return "not a stream (a web page, often a sign-up or 'not available' page)";
        if (type.Contains("json", StringComparison.OrdinalIgnoreCase) || head.StartsWith('{') || head.StartsWith('['))
            return "the server answered with an error message instead of video";
        // AES-128 HLS segments are encrypted, so their bytes look random: that is expected.
        if (encrypted) return null;
        if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return null;
        return "the server answers, but what it sends does not look like video";
    }

    private enum Protection { None, Aes128, Drm }

    /// <summary>#EXT-X-KEY: plain AES-128 can be played; SAMPLE-AES with a DRM key system (Widevine, PlayReady, FairPlay) cannot.</summary>
    private static Protection ProtectionOf(string playlist)
    {
        var result = Protection.None;
        foreach (var raw in playlist.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal) && !line.StartsWith("#EXT-X-SESSION-KEY:", StringComparison.Ordinal)) continue;
            if (line.Contains("METHOD=NONE", StringComparison.Ordinal)) continue;
            bool identity = !line.Contains("KEYFORMAT=", StringComparison.Ordinal) || line.Contains("KEYFORMAT=\"identity\"", StringComparison.Ordinal);
            if (line.Contains("METHOD=AES-128", StringComparison.Ordinal) && identity) result = Protection.Aes128;
            else return Protection.Drm;
        }
        return result;
    }

    /// <summary>Known media container signatures: MPEG-TS, MP4/fMP4, WebM/MKV, FLV, Ogg, ID3/MP3, AAC (ADTS).</summary>
    public static bool LooksLikeMedia(byte[] b, int n)
    {
        if (n >= 1 && b[0] == 0x47 && (n < 189 || b[188] == 0x47)) return true;
        for (int i = 1; i < Math.Min(188, n - 188); i++) if (b[i] == 0x47 && b[i + 188] == 0x47) return true;   // TS with leading bytes
        if (n >= 8)
        {
            var box = Encoding.ASCII.GetString(b, 4, 4);
            if (box is "ftyp" or "styp" or "moof" or "sidx" or "moov" or "mdat" or "free" or "emsg" or "prft") return true;
        }
        if (n >= 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return true;
        if (n >= 3 && ((b[0] == 'F' && b[1] == 'L' && b[2] == 'V') || (b[0] == 'I' && b[1] == 'D' && b[2] == '3'))) return true;
        if (n >= 4 && b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S') return true;
        if (n >= 2 && b[0] == 0xFF && (b[1] & 0xF6) == 0xF0) return true;   // AAC ADTS
        if (n >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return true;   // MPEG audio frame
        return false;
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
