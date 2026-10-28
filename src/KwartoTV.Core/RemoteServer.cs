using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace KwartoTV.Core;

public sealed record PairRequest(string? Pin, string? Name);

/// <summary>
/// LAN remote endpoint: POST /pair (PIN to token) and GET /ws?device=..&amp;token=.. (WebSocket commands).
/// Prefix example: http://127.0.0.1:8765/ (tests) or http://+:8765/ (needs a URL ACL on Windows).
/// </summary>
public sealed class RemoteServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly PairingService _pairing;
    private readonly CommandRouter _router;
    private readonly ChannelLineup _lineup;
    private readonly ConcurrentDictionary<Guid, (string DeviceId, WebSocket Socket)> _clients = new();
    private readonly Func<DateTimeOffset> _clock;
    private Task? _loop;
    private static readonly Lazy<byte[]> RemotePage = new(() =>
    {
        using var s = typeof(RemoteServer).Assembly.GetManifestResourceStream("remote.html")!;
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    });

    /// <summary>Shown to phones in /info (and used as the mDNS instance name by the shell).</summary>
    public string ServerName { get; set; } = "KwartoTV";

    public RemoteServer(string prefix, PairingService pairing, CommandRouter router, ChannelLineup lineup, Func<DateTimeOffset>? clock = null)
    {
        _listener.Prefixes.Add(prefix);
        _pairing = pairing; _router = router; _lineup = lineup;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lineup.Tuned += OnTuned;
        _pairing.Changed += DropRevoked;
    }

    // A revoked or blocked phone is disconnected right away, not on its next command.
    private void DropRevoked()
    {
        foreach (var (id, c) in _clients.ToArray())
        {
            if (!_pairing.IsKnownAndAllowed(c.DeviceId))
            {
                _clients.TryRemove(id, out _);
                _ = Task.Run(async () => { try { await c.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "revoked", CancellationToken.None); } catch { } });
            }
        }
    }

    public void Start() { _listener.Start(); _loop = Task.Run(AcceptLoop); }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleSafe(ctx));
        }
    }

    private async Task HandleSafe(HttpListenerContext ctx)
    {
        try { await Handle(ctx); }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        if (path == "/" && ctx.Request.HttpMethod == "GET") { await HandlePage(ctx); return; }
        if (path == "/info" && ctx.Request.HttpMethod == "GET")
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new { app = "KwartoTV", name = ServerName, version = 1 }, Json));
            ctx.Response.Close(); return;
        }
        if (path == "/pair" && ctx.Request.HttpMethod == "POST") { await HandlePair(ctx); return; }
        if (path == "/ws" && ctx.Request.IsWebSocketRequest) { await HandleSocket(ctx); return; }
        ctx.Response.StatusCode = 404; ctx.Response.Close();
    }

    private async Task HandlePage(HttpListenerContext ctx)
    {
        var page = RemotePage.Value;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self' ws: wss:; frame-ancestors 'none'";
        ctx.Response.ContentLength64 = page.Length;
        await ctx.Response.OutputStream.WriteAsync(page);
        ctx.Response.Close();
    }

    private async Task HandlePair(HttpListenerContext ctx)
    {
        if (ctx.Request.ContentLength64 > 1024) { ctx.Response.StatusCode = 413; ctx.Response.Close(); return; }
        PairRequest? req;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
        { try { req = JsonSerializer.Deserialize<PairRequest>(await sr.ReadToEndAsync(), Json); } catch { req = null; } }

        var result = req is null ? null : _pairing.Complete(req.Pin ?? "", req.Name ?? "", _clock());
        if (result is null) { ctx.Response.StatusCode = 403; ctx.Response.Close(); return; }

        var body = JsonSerializer.SerializeToUtf8Bytes(new { deviceId = result.Value.Device.Id, token = result.Value.Token }, Json);
        ctx.Response.ContentType = "application/json";
        await ctx.Response.OutputStream.WriteAsync(body);
        ctx.Response.Close();
    }

    private async Task HandleSocket(HttpListenerContext ctx)
    {
        var device = ctx.Request.QueryString["device"];
        var token = ctx.Request.QueryString["token"];
        if (!_pairing.Validate(device, token)) { ctx.Response.StatusCode = 401; ctx.Response.Close(); return; }

        var wsCtx = await ctx.AcceptWebSocketAsync(null);
        var ws = wsCtx.WebSocket;
        var id = Guid.NewGuid();
        _clients[id] = (device!, ws);
        await Send(ws, StateMessage(_lineup.Current, _lineup.CurrentNumber));

        var buf = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, _cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) goto done;
                    ms.Write(buf, 0, r.Count);
                    if (ms.Length > 8192) { await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", CancellationToken.None); goto done; }
                } while (!r.EndOfMessage);

                if (!_pairing.Validate(device, token)) { await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "revoked", CancellationToken.None); break; }

                RemoteCommand? cmd;
                try { cmd = JsonSerializer.Deserialize<RemoteCommand>(ms.ToArray(), Json); } catch { cmd = null; }
                var res = cmd is null ? new CommandResult(false, "bad json") : _router.Handle(cmd);
                await Send(ws, JsonSerializer.Serialize(new { type = "ack", id = cmd?.Id, ok = res.Ok, error = res.Error }, Json));
            }
        }
        catch { }
        done:
        _clients.TryRemove(id, out _);
        try { ws.Dispose(); } catch { }
    }

    private static string StateMessage(Channel? ch, int number) =>
        JsonSerializer.Serialize(new { type = "state", number, channel = ch is null ? null : new { ch.Id, ch.Name, ch.Group } }, Json);

    private void OnTuned(Channel ch, int number)
    {
        var msg = StateMessage(ch, number);
        foreach (var c in _clients.Values) _ = Send(c.Socket, msg);
    }

    private static async Task Send(WebSocket ws, string text)
    {
        if (ws.State != WebSocketState.Open) return;
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _lineup.Tuned -= OnTuned;
        _pairing.Changed -= DropRevoked;
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        if (_loop is not null) { try { await _loop; } catch { } }
    }
}
