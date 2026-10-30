namespace KwartoTV.Core;

/// <summary>
/// The Philippine networks' own free live streams on their official YouTube channels (checked with
/// tools/probe/ph-youtube-live.js). They play as fullscreen channels in Live TV; ▲▼ still change channel.
/// </summary>
public static class PinoyLive
{
    public const string Name = "Pinoy Live (YouTube)";
    public const string FileName = "pinoy-live.m3u";

    /// <summary>Name, category, YouTube handle, and the TV-guide id when the stream follows a TV channel's schedule.</summary>
    public static readonly IReadOnlyList<(string Name, string Group, string Handle, string? GuideId)> Channels = new (string, string, string, string?)[]
    {
        ("Kapamilya Online Live", "Local TV", "ABSCBNEntertainment", "Kapamilya.Channel.ph"),
        ("TV5", "Local TV", "TV5Philippines", null),
        ("GMA Network Live", "Local TV", "gmanetwork", null),
        ("GMA News Live", "News", "gmanews", null),
        ("NET25", "Local TV", "NET25TV", null),
        ("UNTV", "News", "UNTVNewsandRescue", null),
    };

    public static string LiveUrl(string handle) => $"https://www.youtube.com/@{handle}/live";

    /// <summary>The pack as an M3U playlist (written to the app's data folder and added like any playlist).</summary>
    public static string Playlist() =>
        "#EXTM3U\n" + string.Concat(Channels.Select(c =>
            $"#EXTINF:-1{(c.GuideId is null ? "" : $" tvg-id=\"{c.GuideId}\"")} group-title=\"{c.Group}\",{c.Name}\n{LiveUrl(c.Handle)}\n"));
}
