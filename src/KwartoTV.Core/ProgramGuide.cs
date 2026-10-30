using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace KwartoTV.Core;

public sealed record Programme(DateTimeOffset Start, DateTimeOffset Stop, string Title, string? Description);

/// <summary>
/// What is on each channel now and next, from an XMLTV guide (the format free TV guides use).
/// Channels are matched by their playlist guide id (tvg-id), else by name ("GMA Network (1080p)" matches "GMA").
/// </summary>
public sealed class ProgramGuide
{
    public static readonly ProgramGuide Empty = new(new(), new());

    private readonly Dictionary<string, List<Programme>> _byId;      // guide channel id (lower case) → programmes by start
    private readonly Dictionary<string, string> _idByName;           // normalized display name → guide channel id

    private ProgramGuide(Dictionary<string, List<Programme>> byId, Dictionary<string, string> idByName) { _byId = byId; _idByName = idByName; }

    public int ChannelCount => _byId.Count;

    /// <summary>Several guides as one; for a channel in more than one, the earlier guide wins.</summary>
    public static ProgramGuide Merge(IEnumerable<ProgramGuide> guides)
    {
        var byId = new Dictionary<string, List<Programme>>(StringComparer.OrdinalIgnoreCase);
        var idByName = new Dictionary<string, string>();
        foreach (var g in guides)
        {
            foreach (var kv in g._byId) byId.TryAdd(kv.Key, kv.Value);
            foreach (var kv in g._idByName) idByName.TryAdd(kv.Key, kv.Value);
        }
        return new ProgramGuide(byId, idByName);
    }

    /// <summary>Reads an XMLTV guide (plain or gzip), keeping only programmes that overlap [from, to].</summary>
    public static ProgramGuide Parse(Stream input, DateTimeOffset from, DateTimeOffset to)
    {
        var buffered = new BufferedStream(input);
        var magic = new byte[2];
        int got = buffered.Read(magic, 0, 2);
        Stream data = new ConcatStream(magic.AsSpan(0, got).ToArray(), buffered);
        if (got == 2 && magic[0] == 0x1f && magic[1] == 0x8b) data = new GZipStream(data, CompressionMode.Decompress);

        var byId = new Dictionary<string, List<Programme>>(StringComparer.OrdinalIgnoreCase);
        var idByName = new Dictionary<string, string>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };
        using var r = XmlReader.Create(data, settings);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.Name == "channel")
            {
                var id = r.GetAttribute("id");
                if (string.IsNullOrEmpty(id)) continue;
                using var sub = r.ReadSubtree();
                // ReadElementContentAsString moves on by itself, so only call Read() when it was not used.
                sub.Read();
                while (!sub.EOF)
                    if (sub.NodeType == XmlNodeType.Element && sub.Name == "display-name")
                    {
                        var key = NormalizeName(sub.ReadElementContentAsString());
                        if (key.Length > 0) idByName.TryAdd(key, id);
                    }
                    else sub.Read();
            }
            else if (r.Name == "programme")
            {
                var ch = r.GetAttribute("channel");
                if (string.IsNullOrEmpty(ch) || !TryParseTime(r.GetAttribute("start"), out var start) || !TryParseTime(r.GetAttribute("stop"), out var stop)) continue;
                if (stop <= from || start >= to || stop <= start) continue;
                string? title = null, desc = null;
                using (var sub = r.ReadSubtree())
                {
                    sub.Read();
                    while (!sub.EOF)
                        if (sub.NodeType == XmlNodeType.Element && sub.Name == "title" && title is null) title = sub.ReadElementContentAsString().Trim();
                        else if (sub.NodeType == XmlNodeType.Element && sub.Name == "desc" && desc is null) desc = sub.ReadElementContentAsString().Trim();
                        else sub.Read();
                }
                if (string.IsNullOrEmpty(title)) continue;
                if (!byId.TryGetValue(ch, out var list)) byId[ch] = list = new List<Programme>();
                list.Add(new Programme(start, stop, title, string.IsNullOrEmpty(desc) ? null : desc));
            }
        }
        foreach (var list in byId.Values) list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ProgramGuide(byId, idByName);
    }

    /// <summary>The programme on now and the one after it (either may be null when the guide has nothing).</summary>
    public (Programme? Now, Programme? Next) NowNext(Channel ch, DateTimeOffset now)
    {
        var list = Find(ch);
        if (list is null) return (null, null);
        Programme? cur = null, next = null;
        foreach (var p in list)
        {
            if (p.Start <= now && now < p.Stop) cur = p;
            else if (p.Start >= now) { next = p; break; }
        }
        return (cur, next);
    }

    private List<Programme>? Find(Channel ch)
    {
        if (!string.IsNullOrEmpty(ch.TvgId))
        {
            if (_byId.TryGetValue(ch.TvgId, out var exact)) return exact;
            // iptv-org ids carry a feed: "GMA.ph@SD" → "GMA.ph"
            var at = ch.TvgId.IndexOf('@');
            if (at > 0 && _byId.TryGetValue(ch.TvgId[..at], out var feed)) return feed;
        }
        return _idByName.TryGetValue(NormalizeName(ch.Name), out var id) && _byId.TryGetValue(id, out var byName) ? byName : null;
    }

    private static readonly Regex Tags = new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "network", "channel", "tv", "hd", "sd", "fhd", "uhd", "4k", "ph", "philippines", "pinoy", "the" };

    /// <summary>"GMA Network (1080p) [Geo-blocked]" → "gma"; "TV5 HD" → "tv5".</summary>
    public static string NormalizeName(string name)
    {
        var words = Regex.Split(Tags.Replace(name.ToLowerInvariant(), " "), "[^a-z0-9]+").Where(w => w.Length > 0 && !Filler.Contains(w));
        return string.Concat(words);
    }

    /// <summary>XMLTV time: "20261030140000 +0800" (the zone may be missing; then UTC).</summary>
    public static bool TryParseTime(string? s, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s) || s.Length < 14) return false;
        if (!DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        var offset = TimeSpan.Zero;
        var zone = s[14..].Trim();
        if (zone.Length == 5 && (zone[0] == '+' || zone[0] == '-') && int.TryParse(zone.AsSpan(1, 2), out var hh) && int.TryParse(zone.AsSpan(3, 2), out var mm))
            offset = new TimeSpan(hh, mm, 0) * (zone[0] == '-' ? -1 : 1);
        value = new DateTimeOffset(local, offset);
        return true;
    }

    /// <summary>Puts the bytes read to sniff the format back in front of the rest of the stream.</summary>
    private sealed class ConcatStream : Stream
    {
        private readonly byte[] _head; private int _pos; private readonly Stream _rest;
        public ConcatStream(byte[] head, Stream rest) { _head = head; _rest = rest; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos < _head.Length) { int n = Math.Min(count, _head.Length - _pos); Array.Copy(_head, _pos, buffer, offset, n); _pos += n; return n; }
            return _rest.Read(buffer, offset, count);
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
