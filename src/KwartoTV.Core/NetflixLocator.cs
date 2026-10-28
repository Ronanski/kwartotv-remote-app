using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// Finds the Netflix Store app id from the output of:
/// Get-StartApps | Where-Object Name -like '*Netflix*' | ConvertTo-Json -Compress
/// (PowerShell prints one object for one match and an array for several.)
/// </summary>
public static class NetflixLocator
{
    public const string PowerShellArgs =
        "-NoProfile -NonInteractive -Command \"Get-StartApps | Where-Object Name -like '*Netflix*' | ConvertTo-Json -Compress\"";

    public static string? Find(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
            foreach (var e in items)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
                var id = e.TryGetProperty("AppID", out var a) ? a.GetString() : null;
                if (id is null || name is null || !name.Contains("Netflix", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Contains('!') && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '!' or '-')) return id;
            }
        }
        catch (JsonException) { }
        return null;
    }
}
