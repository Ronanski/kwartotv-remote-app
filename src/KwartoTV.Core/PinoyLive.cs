namespace KwartoTV.Core;

/// <summary>
/// The Philippine networks' own free live streams on their official YouTube channels (checked with
/// tools/probe/ph-youtube-live.js). They play as fullscreen channels in Live TV; ▲▼ still change channel.
/// </summary>
public static class PinoyLive
{
    public const string Name = "Pinoy Live (YouTube)";
    public const string FileName = "pinoy-live.m3u";

    /// <summary>
    /// Name, category, YouTube handle, and the TV-guide id when the stream follows a TV channel's schedule.
    /// Each one is an official channel (checked by tools/probe/ph-youtube-live.js). Many are live only part of the day;
    /// the app checks every 20 minutes and shows only the ones on air.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Group, string Handle, string? GuideId)> Channels = new (string, string, string, string?)[]
    {
        // TV networks
        ("Kapamilya Online Live", "Local TV", "ABSCBNEntertainment", "Kapamilya.Channel.ph"),
        ("TV5", "Local TV", "TV5Philippines", null),
        ("GMA Network Live", "Local TV", "gmanetwork", null),
        ("GTV", "Local TV", "gtvph", null),
        ("ALLTV", "Local TV", "AllTVPH", null),
        ("NET25", "Local TV", "NET25TV", null),
        ("PTV", "Local TV", "ptvph", null),
        ("Jeepney TV", "Local TV", "JeepneyTV", null),
        ("Cinema One", "Movies", "CinemaOnePH", null),
        ("Knowledge Channel", "Kids", "KnowledgeChannelOrg", null),
        ("MYX", "Music", "myxph", null),
        // news
        ("GMA News Live", "News", "gmanews", null),
        ("ABS-CBN News", "News", "ABSCBNNews", null),
        ("ANC 24/7", "News", "ANCalerts", null),
        ("One News", "News", "onenewsph", null),
        ("UNTV", "News", "UNTVNewsandRescue", null),
        ("CLTV36", "News", "CLTV36", null),
        ("Bilyonaryo News", "News", "BilyonaryoChannel", null),
        ("Abante TV", "News", "AbanteTV", null),
        ("SMNI", "News", "SMNI", null),
        ("Rappler", "News", "Rappler", null),
        ("INQUIRER.net", "News", "inquirerdotnet", null),
        ("BusinessWorld TV", "News", "BusinessWorldTV", null),
        // radio on TV
        ("DZRH", "News", "DZRH", null),
        ("Radyo Pilipinas", "News", "radyopilipinas", null),
        ("Bombo Radyo", "News", "BomboRadyoPhilippines", null),
        ("DWIZ", "News", "DWIZ882", null),
        ("Brigada News", "News", "brigadanews", null),
        // government
        ("RTVM (Malacañang)", "News", "RTVMalacanang", null),
        ("Senate of the Philippines", "News", "SenateofthePhilippines", null),
        ("House of Representatives", "News", "HouseofRepresentativesPH", null),
        // sports
        ("One Sports+", "Sports", "OneSportsPH", null),
        ("PBA", "Sports", "PBAOfficial", null),
        ("UAAP", "Sports", "UAAPsports", null),
        ("NCAA Philippines", "Sports", "NCAAPhilippines", null),
        ("Solar Sports", "Sports", "SolarSportsPH", null),
        // faith
        ("INC TV", "Religious", "INCTV", null),
        ("Light TV", "Religious", "LightTVPH", null),
    };

    public static string LiveUrl(string handle) => $"https://www.youtube.com/@{handle}/live";

    /// <summary>The pack as an M3U playlist (written to the app's data folder and added like any playlist).</summary>
    public static string Playlist() =>
        "#EXTM3U\n" + string.Concat(Channels.Select(c =>
            $"#EXTINF:-1{(c.GuideId is null ? "" : $" tvg-id=\"{c.GuideId}\"")} group-title=\"{c.Group}\",{c.Name}\n{LiveUrl(c.Handle)}\n"));
}
