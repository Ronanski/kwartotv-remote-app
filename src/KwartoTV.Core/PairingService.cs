using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KwartoTV.Core;

public sealed record PairedDevice(string Id, string Name, string TokenHash, DateTimeOffset PairedAt, bool Blocked);

/// <summary>PIN pairing with per-device tokens. Only token hashes are stored.</summary>
public sealed class PairingService
{
    public static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(2);
    private const int MaxAttempts = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, PairedDevice> _devices = new();
    private string? _pin;
    private DateTimeOffset _pinExpiry;
    private int _attempts;

    /// <summary>Raised after a device is added, revoked, blocked or unblocked, so the shell can persist the list.</summary>
    public event Action? Changed;

    public string StartPairing(DateTimeOffset now)
    {
        lock (_gate)
        {
            _pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _pinExpiry = now + PinLifetime;
            _attempts = 0;
            return _pin;
        }
    }

    public (PairedDevice Device, string Token)? Complete(string pin, string deviceName, DateTimeOffset now)
    {
        (PairedDevice Device, string Token)? result;
        lock (_gate)
        {
            if (_pin is null || now > _pinExpiry) { _pin = null; return null; }
            if (++_attempts > MaxAttempts) { _pin = null; return null; }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin ?? ""), Encoding.UTF8.GetBytes(_pin)))
                return null;

            _pin = null;
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var name = string.IsNullOrWhiteSpace(deviceName) ? "Remote" : deviceName.Trim();
            if (name.Length > 40) name = name[..40];
            var dev = new PairedDevice(Guid.NewGuid().ToString("N")[..8], name, Hash(token), now, false);
            _devices[dev.Id] = dev;
            result = (dev, token);
        }
        Changed?.Invoke();
        return result;
    }

    public bool Validate(string? deviceId, string? token)
    {
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(token)) return false;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d) || d.Blocked) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(token)), Encoding.UTF8.GetBytes(d.TokenHash));
        }
    }

    public bool IsKnownAndAllowed(string id) { lock (_gate) return _devices.TryGetValue(id, out var d) && !d.Blocked; }

    public IReadOnlyList<PairedDevice> Devices { get { lock (_gate) return _devices.Values.OrderBy(d => d.PairedAt).ToList(); } }
    public bool Revoke(string id)
    {
        bool removed;
        lock (_gate) removed = _devices.Remove(id);
        if (removed) Changed?.Invoke();
        return removed;
    }

    public bool SetBlocked(string id, bool blocked)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(id, out var d)) return false;
            _devices[id] = d with { Blocked = blocked };
        }
        Changed?.Invoke();
        return true;
    }

    public string Export() { lock (_gate) return JsonSerializer.Serialize(_devices.Values.ToList()); }

    public void Import(string json)
    {
        var list = JsonSerializer.Deserialize<List<PairedDevice>>(json) ?? new();
        lock (_gate) { _devices.Clear(); foreach (var d in list) _devices[d.Id] = d; }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
