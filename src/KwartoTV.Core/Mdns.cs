using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace KwartoTV.Core;

/// <summary>Minimal DNS-SD/mDNS records so phones can find the TV as "_kwartotv._tcp" without typing an IP.</summary>
public static class MdnsPacket
{
    public const string ServiceType = "_kwartotv._tcp";
    public const string MetaQuery = "_services._dns-sd._udp.local";
    private const ushort TypeA = 1, TypePtr = 12, TypeTxt = 16, TypeSrv = 33, ClassIn = 1, CacheFlush = 0x8000;

    public static string SanitizeLabel(string text, string fallback)
    {
        var s = new string((text ?? "").Where(c => !char.IsControl(c) && c != '.').ToArray()).Trim();
        if (s.Length == 0) s = fallback;
        while (Encoding.UTF8.GetByteCount(s) > 60) s = s[..^1];
        return s;
    }

    /// <summary>Full answer: PTR for the service, plus SRV, TXT and A records so the phone needs no second lookup.</summary>
    public static byte[] BuildResponse(string instance, string hostLabel, IReadOnlyList<IPAddress> ips, int port, uint ttl = 120)
    {
        var v4 = ips.Where(i => i.AddressFamily == AddressFamily.InterNetwork).ToList();
        var svc = $"{ServiceType}.local";
        var inst = $"{instance}.{svc}";
        var host = $"{hostLabel}.local";
        var w = new Writer();
        w.U16(0); w.U16(0x8400); w.U16(0); w.U16(1); w.U16(0); w.U16((ushort)(2 + v4.Count));

        w.Name(svc); w.U16(TypePtr); w.U16(ClassIn); w.U32(ttl == 0 ? 0u : 4500u);
        w.RData(() => w.Name(inst));

        w.Name(inst); w.U16(TypeSrv); w.U16(ClassIn | CacheFlush); w.U32(ttl);
        w.RData(() => { w.U16(0); w.U16(0); w.U16((ushort)port); w.Name(host); });

        w.Name(inst); w.U16(TypeTxt); w.U16(ClassIn | CacheFlush); w.U32(ttl);
        w.RData(() => { var t = Encoding.ASCII.GetBytes("v=1"); w.Byte((byte)t.Length); w.Bytes(t); });

        foreach (var ip in v4)
        {
            w.Name(host); w.U16(TypeA); w.U16(ClassIn | CacheFlush); w.U32(ttl);
            w.RData(() => w.Bytes(ip.GetAddressBytes()));
        }
        return w.ToArray();
    }

    /// <summary>Answer to the DNS-SD "what services exist?" query.</summary>
    public static byte[] BuildMetaResponse()
    {
        var w = new Writer();
        w.U16(0); w.U16(0x8400); w.U16(0); w.U16(1); w.U16(0); w.U16(0);
        w.Name(MetaQuery); w.U16(TypePtr); w.U16(ClassIn); w.U32(4500u);
        w.RData(() => w.Name($"{ServiceType}.local"));
        return w.ToArray();
    }

    /// <summary>Lower-case names asked in a query packet (empty for responses or garbage).</summary>
    public static IReadOnlyList<string> ParseQuestions(byte[] d)
    {
        var names = new List<string>();
        try
        {
            if (d.Length < 12 || (d[2] & 0x80) != 0) return names;
            int qd = d[4] << 8 | d[5], pos = 12;
            for (int i = 0; i < qd; i++)
            {
                names.Add(ReadName(d, ref pos).ToLowerInvariant());
                pos += 4;
            }
        }
        catch (Exception) { names.Clear(); }
        return names;
    }

    /// <summary>Records in a response packet as (name, type). Used by tests and for sanity checks.</summary>
    public static IReadOnlyList<(string Name, int Type, byte[] Data)> ParseRecords(byte[] d)
    {
        var list = new List<(string, int, byte[])>();
        int qd = d[4] << 8 | d[5], an = d[6] << 8 | d[7], ns = d[8] << 8 | d[9], ar = d[10] << 8 | d[11], pos = 12;
        for (int i = 0; i < qd; i++) { ReadName(d, ref pos); pos += 4; }
        for (int i = 0; i < an + ns + ar; i++)
        {
            var name = ReadName(d, ref pos);
            int type = d[pos] << 8 | d[pos + 1];
            int len = d[pos + 8] << 8 | d[pos + 9];
            pos += 10;
            list.Add((name, type, d[pos..(pos + len)]));
            pos += len;
        }
        return list;
    }

    public static string ReadName(byte[] d, ref int pos)
    {
        var sb = new StringBuilder();
        int p = pos, jumps = 0; bool jumped = false;
        while (true)
        {
            int len = d[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                if (++jumps > 10) throw new InvalidDataException("pointer loop");
                int target = (len & 0x3F) << 8 | d[p + 1];
                if (!jumped) pos = p + 2;
                jumped = true; p = target; continue;
            }
            sb.Append(Encoding.UTF8.GetString(d, p + 1, len)).Append('.');
            p += 1 + len;
        }
        if (!jumped) pos = p;
        return sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : "";
    }

    private sealed class Writer
    {
        private readonly MemoryStream _ms = new();
        public void Byte(byte b) => _ms.WriteByte(b);
        public void Bytes(byte[] b) => _ms.Write(b);
        public void U16(ushort v) { Byte((byte)(v >> 8)); Byte((byte)v); }
        public void U32(uint v) { U16((ushort)(v >> 16)); U16((ushort)v); }
        public void Name(string n)
        {
            foreach (var label in n.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var b = Encoding.UTF8.GetBytes(label);
                Byte((byte)b.Length); Bytes(b);
            }
            Byte(0);
        }
        public void RData(Action body)
        {
            var lenPos = _ms.Position; U16(0);
            var start = _ms.Position; body();
            var end = _ms.Position; _ms.Position = lenPos;
            U16((ushort)(end - start)); _ms.Position = end;
        }
        public byte[] ToArray() => _ms.ToArray();
    }
}

/// <summary>Advertises the remote server on the LAN and answers mDNS queries for it.</summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private readonly string _instance, _host;
    private readonly int _port;
    private readonly Func<IReadOnlyList<IPAddress>> _addresses;
    private readonly CancellationTokenSource _cts = new();
    private Socket? _socket;

    public MdnsResponder(string instanceName, string hostLabel, int port, Func<IReadOnlyList<IPAddress>>? addresses = null)
    {
        _instance = MdnsPacket.SanitizeLabel(instanceName, "KwartoTV");
        _host = MdnsPacket.SanitizeLabel(hostLabel, "kwartotv").Replace(' ', '-');
        _port = port;
        _addresses = addresses ?? LocalAddresses;
    }

    public static IReadOnlyList<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.Supports(NetworkInterfaceComponent.IPv4))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
            .ToList();

    /// <summary>Reply to a query packet, or null when the packet is not about this service.</summary>
    public byte[]? Respond(byte[] query)
    {
        foreach (var q in MdnsPacket.ParseQuestions(query))
        {
            if (q == MdnsPacket.MetaQuery) return MdnsPacket.BuildMetaResponse();
            if (q == $"{MdnsPacket.ServiceType}.local" || q == $"{_instance}.{MdnsPacket.ServiceType}.local".ToLowerInvariant() || q == $"{_host}.local".ToLowerInvariant())
                return MdnsPacket.BuildResponse(_instance, _host, _addresses(), _port);
        }
        return null;
    }

    public bool Start()
    {
        try
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            foreach (var ip in _addresses())
            {
                try { s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, ip)); } catch { }
            }
            s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            _socket = s;
            _ = Task.Run(ReceiveLoop);
            _ = Task.Run(AnnounceLoop);
            return true;
        }
        catch { return false; }
    }

    private async Task ReceiveLoop()
    {
        var buf = new byte[9000];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!_cts.IsCancellationRequested && _socket is { } s)
        {
            try
            {
                var r = await s.ReceiveFromAsync(buf, SocketFlags.None, any, _cts.Token);
                var reply = Respond(buf[..r.ReceivedBytes]);
                if (reply is not null) SendMulticast(reply);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { await Task.Delay(500); }
        }
    }

    private async Task AnnounceLoop()
    {
        try
        {
            for (int i = 0; !_cts.IsCancellationRequested; i++)
            {
                SendMulticast(MdnsPacket.BuildResponse(_instance, _host, _addresses(), _port));
                await Task.Delay(i < 3 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(60), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SendMulticast(byte[] packet)
    {
        if (_socket is not { } s) return;
        var dest = new IPEndPoint(Group, MdnsPort);
        foreach (var ip in _addresses())
        {
            try
            {
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
                s.SendTo(packet, dest);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try { SendMulticast(MdnsPacket.BuildResponse(_instance, _host, _addresses(), _port, ttl: 0)); } catch { }
        _cts.Cancel();
        try { _socket?.Dispose(); } catch { }
    }
}
