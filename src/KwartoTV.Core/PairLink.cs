namespace KwartoTV.Core;

/// <summary>The text inside the pairing QR code: kwartotv://pair?host=192.168.1.20&amp;port=8765&amp;pin=123456</summary>
public static class PairLink
{
    public static string Build(string host, int port, string pin) =>
        $"kwartotv://pair?host={Uri.EscapeDataString(host)}&port={port}&pin={Uri.EscapeDataString(pin)}";

    public static bool TryParse(string? text, out string host, out int port, out string pin)
    {
        host = pin = ""; port = 0;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u) || u.Scheme != "kwartotv" || u.Host != "pair") return false;
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        host = q["host"] ?? ""; pin = q["pin"] ?? "";
        return !string.IsNullOrWhiteSpace(host) && int.TryParse(q["port"], out port) && port is > 0 and < 65536
               && pin.Length == 6 && pin.All(char.IsAsciiDigit);
    }
}
