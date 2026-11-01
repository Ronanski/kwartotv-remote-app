using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// One app of the catalog. [StoreNames] are the names Windows shows for its own app (Start menu); when one is installed,
/// KwartoTV opens that real app. Otherwise [WebUrl] opens inside KwartoTV with remote navigation.
/// </summary>
public sealed record CatalogApp(string Id, string Name, string Icon, string Tagline, string[] StoreNames, string WebUrl, uint From, uint To);

/// <summary>More apps for the Home screen (one press from the TV or the phone).</summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<CatalogApp> All = new CatalogApp[]
    {
        // Opens inside KwartoTV with the same saved sign-in as the music the phone plays (Premium: no ads).
        new("ytmusic", "YouTube Music", "♪", "music · sign in for no ads", Array.Empty<string>(), "https://music.youtube.com/", 0x2A0505, 0xE01E1E),
        new("primevideo", "Prime Video", "▶", "movies · series", new[] { "Prime Video for Windows", "Amazon Prime Video for Windows", "Prime Video" }, "https://www.primevideo.com", 0x0F171E, 0x1A98FF),
        new("disneyplus", "Disney+", "✦", "Disney · Marvel · Star Wars", new[] { "Disney+" }, "https://www.disneyplus.com", 0x0B1640, 0x1F80E0),
        new("hbomax", "HBO Max", "◆", "HBO · Warner", new[] { "HBO Max", "Max" }, "https://play.hbomax.com", 0x1A0B3D, 0x5822B4),
        new("viu", "Viu", "V", "K-dramas · Asian shows", new[] { "Viu" }, "https://www.viu.com/ott/ph/", 0x2A2300, 0xF5B800),
        new("iwanttfc", "iWantTFC", "i", "Kapamilya shows", new[] { "iWantTFC" }, "https://www.iwanttfc.com", 0x2A0A10, 0xD0213A),
        new("crunchyroll", "Crunchyroll", "◐", "anime", new[] { "Crunchyroll" }, "https://www.crunchyroll.com", 0x3A1800, 0xF47521),
        new("spotify", "Spotify", "♫", "music · podcasts", new[] { "Spotify", "Spotify Music" }, "https://open.spotify.com", 0x07140B, 0x1DB954),
        new("twitch", "Twitch", "◧", "live streams", new[] { "Twitch" }, "https://www.twitch.tv", 0x1B0F33, 0x9146FF),
        new("appletv", "Apple TV", "◉", "Apple TV+", new[] { "Apple TV" }, "https://tv.apple.com", 0x111111, 0x555555),
        new("plex", "Plex", "›", "your media server", new[] { "Plex", "Plex HTPC" }, "https://app.plex.tv", 0x1F1A0A, 0xE5A00D),
    };

    public static CatalogApp? Find(string? id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>Lists the Start menu apps (name and id) of this PC as JSON.</summary>
    public const string StartAppsArgs = "-NoProfile -NonInteractive -Command \"Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress\"";

    /// <summary>A Start menu app id that is safe to hand to explorer.exe shell:AppsFolder\…</summary>
    public static bool IsSafeAppId(string id) =>
        id.Length is > 2 and < 260 && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '!' or '-' or '\\' or '{' or '}' or ' ' or ':');

    /// <summary>For each catalog app installed on this PC, its Start menu id (from Get-StartApps).</summary>
    public static IReadOnlyDictionary<string, string> Installed(string? startAppsJson)
    {
        var found = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(startAppsJson)) return found;
        try
        {
            using var doc = JsonDocument.Parse(startAppsJson);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
            foreach (var e in items)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = e.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
                var id = e.TryGetProperty("AppID", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (name is null || id is null || !IsSafeAppId(id)) continue;
                foreach (var app in All)
                    if (!found.ContainsKey(app.Id) && app.StoreNames.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                        found[app.Id] = id;
            }
        }
        catch (JsonException) { }
        return found;
    }

    /// <summary>Opens a Start menu app (a Store app or a desktop app) by its id.</summary>
    public static LaunchSpec StoreApp(string appId) => new("explorer.exe", new[] { $@"shell:AppsFolder\{appId}" });
}
