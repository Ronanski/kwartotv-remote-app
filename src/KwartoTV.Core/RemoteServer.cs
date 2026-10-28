using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KwartoTV.Core;

public sealed record PairRequest(string? Pin, string? Name);

/// <summary>
/// LAN remote endpoint: GET / (browser remote), GET /info, POST /pair (PIN to token) and
/// GET /ws?device=..&amp;token=.. (WebSocket commands).
/// Built on a plain TcpListener, not HttpListener: on Windows HttpListener goes through http.sys and needs a URL
/// reservation for normal (non-admin) users, which silently kept the server off on real PCs.
/// Prefix example: http://127.0.0.1:8765/ (tests) or http://+:8765/ (all network cards).
/// </summary>
public sealed class RemoteServer : IAsyncDisposable
{
    private const int MaxHeadBytes = 8192, MaxBodyBytes = 1024;
    private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<byte[]> RemotePage = new(() =>
    {
        using var s = typeof(RemoteServer).Assembly.GetManifestResourceStream("remote.html")!;
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    });

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly PairingService _pairing;
    private readonly CommandRouter _router;
    private readonly ChannelLineup _lineup;
    private readonly ConcurrentDictionary<Guid, (string DeviceId, WebSocket Socket)> _clients = new();
    private readonly Func<DateTimeOffset> _clock;
    private Task? _loop;

    /// <summary>Shown to phones in /info (and used as the mDNS instance name by the shell).</summary>
    public string ServerName { get; set; } = "KwartoTV";

    public int Port { get; }

    public RemoteServer(string prefix, PairingService pairing, CommandRouter router, ChannelLineup lineup, Func<DateTimeOffset>? clock = null)
    {
        (var ip, Port) = ParsePrefix(prefix);
        _listener = new TcpListener(ip, Port);
        _pairing = pairing; _router = router; _lineup = lineup;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lineup.Tuned += OnTuned;
        _pairing.Changed += DropRevoked;
    }

    /// <summary>"http://+:8765/" or "http://*:8765/" listen on every card; "http://127.0.0.1:8765/" only on that address.</summary>
    public static (IPAddress Ip, int Port) ParsePrefix(string prefix)
    {
        var p = prefix.Trim();
        var i = p.IndexOf("://", StringComparison.Ordinal);
        if (i >= 0) p = p[(i + 3)..];
        p = p.TrimEnd('/');
        var colon = p.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(p[(colon + 1)..], out var port) || port is <= 0 or > 65535)
            throw new ArgumentException("Prefix needs a port, e.g. http://+:8765/", nameof(prefix));
        var host = p[..colon];
        var ip = host is "+" or "*" or "0.0.0.0" ? IPAddress.Any
            : host == "localhost" ? IPAddress.Loopback
            : IPAddress.Parse(host);
        return (ip, port);
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

    /// <summary>Starts listening. Throws SocketException when the port is already taken.</summary>
    public void Start()
    {
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, OperatingSystem.IsWindows());
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Task.Run(() => HandleSafe(client));
        }
    }

    private async Task HandleSafe(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await Handle(client.GetStream());
            }
            catch { }
        }
    }

    // ---------- minimal HTTP/1.1

    private sealed record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, byte[] Leftover);

    private async Task Handle(NetworkStream stream)
    {
        Request? req;
        using (var headTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
        {
            headTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            req = await ReadHead(stream, headTimeout.Token);
        }
        if (req is null) { await Respond(stream, 400, "text/plain", "bad request"u8.ToArray()); return; }

        if (req.Path == "/ws" && req.Method == "GET" && req.Headers.TryGetValue("upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        { await HandleSocket(stream, req); return; }
        if (req.Path == "/" && req.Method == "GET") { await HandlePage(stream); return; }
        if (req.Path == "/info" && req.Method == "GET")
        {
            await Respond(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { app = "KwartoTV", name = ServerName, version = 1 }, Json));
            return;
        }
        if (req.Path == "/pair" && req.Method == "POST") { await HandlePair(stream, req); return; }
        await Respond(stream, 404, "text/plain", "not found"u8.ToArray());
    }

    private static async Task<Request?> ReadHead(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[MaxHeadBytes];
        int len = 0, end = -1;
        while (end < 0)
        {
            if (len == buf.Length) return null;
            int n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return null;
            len += n;
            end = IndexOf(buf, len, "\r\n\r\n"u8);
        }
        var head = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var first = head[0].Split(' ');
        if (first.Length < 3) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in head.Skip(1))
        {
            var c = line.IndexOf(':');
            if (c > 0) headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }
        var target = first[1];
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q >= 0)
        {
            var parsed = System.Web.HttpUtility.ParseQueryString(target[(q + 1)..]);
            foreach (string? k in parsed.AllKeys) if (k is not null && parsed[k] is { } v) query[k] = v;
        }
        return new Request(first[0].ToUpperInvariant(), path, query, headers, buf[(end + 4)..len]);
    }

    private static int IndexOf(byte[] buf, int len, ReadOnlySpan<byte> pattern) => buf.AsSpan(0, len).IndexOf(pattern);

    /// <summary>Reads a small request body: Content-Length or chunked, capped at MaxBodyBytes.</summary>
    private static async Task<byte[]?> ReadBody(NetworkStream stream, Request req, CancellationToken ct)
    {
        var data = new MemoryStream();
        data.Write(req.Leftover);
        async Task<bool> Fill(long atLeast)
        {
            var b = new byte[512];
            while (data.Length < atLeast)
            {
                if (data.Length > MaxBodyBytes + 64) return false;
                int n = await stream.ReadAsync(b, ct);
                if (n == 0) return false;
                data.Write(b, 0, n);
            }
            return true;
        }

        if (req.Headers.TryGetValue("content-length", out var cl))
        {
            if (!long.TryParse(cl, out var n) || n < 0 || n > MaxBodyBytes) return null;
            if (!await Fill(n)) return null;
            return data.ToArray()[..(int)n];
        }
        if (req.Headers.TryGetValue("transfer-encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            while (true)
            {
                switch (DecodeChunked(data.ToArray(), out var body))
                {
                    case 1: return body;
                    case -1: return null;
                }
                if (!await Fill(data.Length + 1)) return null;
            }
        }
        return Array.Empty<byte>();
    }

    /// <summary>1 = complete body decoded, 0 = need more bytes, -1 = invalid or too big.</summary>
    public static int DecodeChunked(byte[] d, out byte[] body)
    {
        body = Array.Empty<byte>();
        var outp = new MemoryStream();
        int pos = 0;
        while (true)
        {
            int lineEnd = d.AsSpan(pos).IndexOf("\r\n"u8);
            if (lineEnd < 0) return d.Length - pos > 16 ? -1 : 0;
            var sizeText = Encoding.ASCII.GetString(d, pos, lineEnd).Split(';')[0].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0) return -1;
            pos += lineEnd + 2;
            if (size == 0) { body = outp.ToArray(); return 1; }
            if (outp.Length + size > MaxBodyBytes) return -1;
            if (d.Length < pos + size + 2) return 0;
            outp.Write(d, pos, size);
            pos += size + 2;
        }
    }

    private static async Task Respond(NetworkStream stream, int status, string contentType, byte[] body, IEnumerable<string>? extraHeaders = null)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" };
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        sb.Append($"Content-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n");
        if (extraHeaders is not null) foreach (var h in extraHeaders) sb.Append(h).Append("\r\n");
        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        if (body.Length > 0) await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static Task HandlePage(NetworkStream stream) => Respond(stream, 200, "text/html; charset=utf-8", RemotePage.Value, new[]
    {
        "Cache-Control: no-store",
        "X-Content-Type-Options: nosniff",
        "Content-Security-Policy: default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self' ws: wss:; frame-ancestors 'none'",
    });

    private async Task HandlePair(NetworkStream stream, Request req)
    {
        if (req.Headers.TryGetValue("expect", out var ex) && ex.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray());

        byte[]? raw;
        using (var t = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
        {
            t.CancelAfter(TimeSpan.FromSeconds(10));
            raw = await ReadBody(stream, req, t.Token);
        }
        if (raw is null) { await Respond(stream, 413, "text/plain", "too big"u8.ToArray()); return; }

        PairRequest? pr;
        try { pr = JsonSerializer.Deserialize<PairRequest>(raw, Json); } catch { pr = null; }
        var result = pr is null ? null : _pairing.Complete(pr.Pin ?? "", pr.Name ?? "", _clock());
        if (result is null) { await Respond(stream, 403, "text/plain", "forbidden"u8.ToArray()); return; }

        await Respond(stream, 200, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new { deviceId = result.Value.Device.Id, token = result.Value.Token }, Json));
    }

    private async Task HandleSocket(NetworkStream stream, Request req)
    {
        req.Query.TryGetValue("device", out var device);
        req.Query.TryGetValue("token", out var token);
        if (!_pairing.Validate(device, token)) { await Respond(stream, 401, "text/plain", "unauthorized"u8.ToArray()); return; }
        if (!req.Headers.TryGetValue("sec-websocket-key", out var key) || key.Length == 0) { await Respond(stream, 400, "text/plain", "bad request"u8.ToArray()); return; }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WsGuid)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n"));

        using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
        var id = Guid.NewGuid();
        _clients[id] = (device!, ws);
        await Send(ws, StateMessage(_lineup.Current, _lineup.CurrentNumber));

        var buf = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf.AsMemory(), _cts.Token);
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
        if (ws.State == WebSocketState.CloseReceived)
        {
            try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
        }
    }

    private static string StateMessage(Channel? ch, int number) =>
        JsonSerializer.Serialize(new { type = "state", number, channel = ch is null ? null : new { ch.Id, ch.Name, ch.Group } }, Json);

    private void OnTuned(Channel ch, int number)
    {
        var msg = StateMessage(ch, number);
        foreach (var c in _clients.Values) _ = Send(c.Socket, msg);
    }

    private static readonly SemaphoreSlim SendGate = new(1, 1);

    private static async Task Send(WebSocket ws, string text)
    {
        if (ws.State != WebSocketState.Open) return;
        // WebSocket allows one send at a time; acks and channel broadcasts can overlap.
        await SendGate.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
        finally { SendGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _lineup.Tuned -= OnTuned;
        _pairing.Changed -= DropRevoked;
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        foreach (var c in _clients.Values) { try { c.Socket.Abort(); } catch { } }
        if (_loop is not null) { try { await _loop; } catch { } }
    }
}
