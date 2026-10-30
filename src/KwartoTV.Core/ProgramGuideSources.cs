namespace KwartoTV.Core;

/// <summary>Free TV guides (XMLTV) used when the user has not set their own.</summary>
public static class ProgramGuideSources
{
    public static readonly IReadOnlyList<string> Default = new[]
    {
        "https://epg.pw/xmltv/epg_PH.xml.gz",
    };

    /// <summary>A guide is downloaded again after this long.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);
}
