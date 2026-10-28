using System.Net;
using System.Text.RegularExpressions;

namespace KwartoTV.Core;

/// <summary>
/// Decides which of the PC's addresses to show and advertise. A PC with VMware, Hyper-V, WSL or a VPN has several,
/// and only the real Wi-Fi/Ethernet one (the one with a gateway) can be reached from a phone.
/// </summary>
public static class LanAddressPicker
{
    public sealed record Candidate(string Name, string Description, IPAddress Address, bool HasGateway);

    private static readonly Regex Virtual = new("vmware|virtualbox|vbox|hyper-v|vethernet|wsl|docker|tailscale|zerotier|hamachi|vpn|loopback|bluetooth|npcap|teredo|isatap", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsVirtual(Candidate c) => Virtual.IsMatch(c.Name) || Virtual.IsMatch(c.Description);

    public static IReadOnlyList<IPAddress> Rank(IEnumerable<Candidate> all)
    {
        var usable = all.Where(c => !c.Address.ToString().StartsWith("169.254.") && !IPAddress.IsLoopback(c.Address)).ToList();
        return usable
            .OrderBy(c => IsVirtual(c) ? 1 : 0)
            .ThenBy(c => c.HasGateway ? 0 : 1)
            .ThenBy(c => IsPrivate(c.Address) ? 0 : 1)
            .Select(c => c.Address).Distinct().ToList();
    }

    public static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
