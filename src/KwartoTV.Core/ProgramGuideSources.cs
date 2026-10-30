namespace KwartoTV.Core;

/// <summary>Free TV guides (XMLTV) used when the user has not set their own.</summary>
public static class ProgramGuideSources
{
    // Checked with tools/probe/epg.js: open-epg uses the same guide ids as the playlists ("GMA.ph"), epg.pw is matched by name.
    public static readonly IReadOnlyList<string> Default = new[]
    {
        "https://www.open-epg.com/files/philippines3.xml",
        "https://epg.pw/xmltv/epg_PH.xml.gz",
    };

    /// <summary>A guide is downloaded again after this long.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);
}
