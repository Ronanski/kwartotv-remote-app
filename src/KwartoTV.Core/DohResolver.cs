using System.Net;
using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// Looks a name up through a public DNS-over-HTTPS service when the provider's own DNS fails or is slow
/// (some home internet providers block or break DNS for sites such as github.io).
/// The service is contacted by its IP address, so no DNS is needed to reach it.
/// </summary>
public static class DohResolver
{
    private static readonly string[] Services =
    {
        "https://1.1.1.1/dns-query?name={0}&type=A",
        "https://8.8.8.8/resolve?name={0}&type=A",
    };

    private static readonly HttpClient Client = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(4) }) { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Reads the JSON answer of a DoH service ({"Answer":[{"type":1,"data":"1.2.3.4"}]}).</summary>
    public static IReadOnlyList<IPAddress> ParseAnswer(string json)
    {
        var list = new List<IPAddress>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Answer", out var answers)) return list;
            foreach (var a in answers.EnumerateArray())
                if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1 && a.TryGetProperty("data", out var d) && IPAddress.TryParse(d.GetString(), out var ip))
                    list.Add(ip);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { list.Clear(); }
        return list;
    }

    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
    {
        foreach (var template in Services)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, string.Format(template, Uri.EscapeDataString(host)));
                req.Headers.Accept.ParseAdd("application/dns-json");
                using var resp = await Client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var ips = ParseAnswer(await resp.Content.ReadAsStringAsync(ct));
                if (ips.Count > 0) return ips;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        return Array.Empty<IPAddress>();
    }
}
