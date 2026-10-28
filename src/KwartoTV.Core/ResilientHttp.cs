using System.Net;
using System.Net.Sockets;

namespace KwartoTV.Core;

/// <summary>
/// An HttpClient that copes with home networks where IPv6 or one DNS answer hangs: it tries IPv4 addresses first,
/// gives each address a short connect time, and falls back to the next one instead of waiting for a long timeout.
/// </summary>
public static class ResilientHttp
{
    public static readonly TimeSpan ConnectTime = TimeSpan.FromSeconds(8);

    public static HttpClient Create(TimeSpan? overall = null)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            // Follow the proxy settings Windows uses for the browser (companies, schools, some ISPs).
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        var http = new HttpClient(handler) { Timeout = overall ?? TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KwartoTV/1.0 (+https://github.com/Ronanski/game-dev-era)");
        return http;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var addresses = new List<IPAddress>();
        string? dnsProblem = null;
        if (IPAddress.TryParse(host, out var literal)) addresses.Add(literal);
        else
        {
            // The provider's DNS first, but not for long: if it fails or hangs, ask a public DNS-over-HTTPS service instead.
            try
            {
                using var dnsLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                dnsLimit.CancelAfter(TimeSpan.FromSeconds(4));
                addresses.AddRange(await Dns.GetHostAddressesAsync(host, dnsLimit.Token));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { dnsProblem = ex is OperationCanceledException ? "DNS did not answer in 4 seconds" : ex.Message; }
            if (addresses.Count == 0) addresses.AddRange(await DohResolver.ResolveAsync(host, ct));
            if (addresses.Count == 0) throw new HttpRequestException($"Could not find {host}: {dnsProblem ?? "no address"}. Check the internet connection or change the DNS to 1.1.1.1 or 8.8.8.8.");
        }

        Exception? last = null;
        foreach (var a in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(a.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(ConnectTime);
                await socket.ConnectAsync(new IPEndPoint(a, ctx.DnsEndPoint.Port), limit.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex; socket.Dispose();
            }
        }
        throw new HttpRequestException($"Could not connect to {host}:{ctx.DnsEndPoint.Port} ({addresses.Count} address(es) tried): {last?.Message ?? "no address"}", last);
    }
}
