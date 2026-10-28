using System.Text.Json;

namespace KwartoTV.Core;

public sealed record PlaylistSuggestion(string Name, string Url);

/// <summary>
/// Links to the public iptv-org playlists (https://github.com/iptv-org/iptv). KwartoTV bundles no channels:
/// this only builds the address of a playlist the user picks, which is then downloaded like any other playlist link.
/// </summary>
public static class IptvOrg
{
    public const string Home = "https://github.com/iptv-org/iptv";
    public const string CountriesApi = "https://iptv-org.github.io/api/countries.json";
    public const string LanguagesApi = "https://iptv-org.github.io/api/languages.json";
    private const string Base = "https://iptv-org.github.io/iptv/";

    /// <summary>
    /// The same playlist from other servers, tried when the main address does not answer (some networks cannot reach github.io).
    /// Only iptv-org playlist addresses have mirrors.
    /// </summary>
    public static IReadOnlyList<string> Mirrors(string url)
    {
        if (!url.StartsWith(Base, StringComparison.Ordinal)) return new[] { url };
        var path = url[Base.Length..];
        return new[]
        {
            url,
            $"https://cdn.jsdelivr.net/gh/iptv-org/iptv@gh-pages/{path}",
            $"https://raw.githubusercontent.com/iptv-org/iptv/gh-pages/{path}",
            $"https://fastly.jsdelivr.net/gh/iptv-org/iptv@gh-pages/{path}",
        };
    }

    public static string CountryUrl(string code) => $"{Base}countries/{code.ToLowerInvariant()}.m3u";
    public static string LanguageUrl(string code) => $"{Base}languages/{code.ToLowerInvariant()}.m3u";

    /// <summary>Every channel of the project in one list (large). Pick the channels you want in the channel picker.</summary>
    public static readonly PlaylistSuggestion AllChannels = new("All channels (everything)", Base + "index.m3u");

    public static readonly IReadOnlyList<(string Code, string Name)> BuiltInLanguages = new (string, string)[]
    {
        ("eng", "English"), ("spa", "Spanish"), ("fra", "French"), ("deu", "German"), ("por", "Portuguese"), ("rus", "Russian"),
        ("ara", "Arabic"), ("hin", "Hindi"), ("zho", "Chinese"), ("jpn", "Japanese"), ("kor", "Korean"), ("ind", "Indonesian"),
        ("tha", "Thai"), ("vie", "Vietnamese"), ("tur", "Turkish"), ("ita", "Italian"), ("tgl", "Tagalog"), ("fil", "Filipino"),
    };

    public static IReadOnlyList<PlaylistSuggestion> LanguageSuggestions(IEnumerable<(string Code, string Name)> languages) =>
        languages.Where(l => l.Code.Length is >= 2 and <= 3 && l.Code.All(char.IsAsciiLetter))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new PlaylistSuggestion(l.Name, LanguageUrl(l.Code))).ToList();

    public static string CategoryUrl(string id) => $"{Base}categories/{id.ToLowerInvariant()}.m3u";

    /// <summary>Used when the live list cannot be downloaded.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> BuiltInCountries = new (string, string)[]
    {
        ("AF", "Afghanistan"),
        ("AL", "Albania"),
        ("DZ", "Algeria"),
        ("AR", "Argentina"),
        ("AM", "Armenia"),
        ("AU", "Australia"),
        ("AT", "Austria"),
        ("AZ", "Azerbaijan"),
        ("BH", "Bahrain"),
        ("BD", "Bangladesh"),
        ("BY", "Belarus"),
        ("BE", "Belgium"),
        ("BO", "Bolivia"),
        ("BA", "Bosnia and Herzegovina"),
        ("BR", "Brazil"),
        ("BG", "Bulgaria"),
        ("KH", "Cambodia"),
        ("CM", "Cameroon"),
        ("CA", "Canada"),
        ("CL", "Chile"),
        ("CN", "China"),
        ("CO", "Colombia"),
        ("CR", "Costa Rica"),
        ("HR", "Croatia"),
        ("CU", "Cuba"),
        ("CY", "Cyprus"),
        ("CZ", "Czech Republic"),
        ("DK", "Denmark"),
        ("DO", "Dominican Republic"),
        ("EC", "Ecuador"),
        ("EG", "Egypt"),
        ("SV", "El Salvador"),
        ("EE", "Estonia"),
        ("ET", "Ethiopia"),
        ("FI", "Finland"),
        ("FR", "France"),
        ("GE", "Georgia"),
        ("DE", "Germany"),
        ("GH", "Ghana"),
        ("GR", "Greece"),
        ("GT", "Guatemala"),
        ("HN", "Honduras"),
        ("HK", "Hong Kong"),
        ("HU", "Hungary"),
        ("IS", "Iceland"),
        ("IN", "India"),
        ("ID", "Indonesia"),
        ("IR", "Iran"),
        ("IQ", "Iraq"),
        ("IE", "Ireland"),
        ("IL", "Israel"),
        ("IT", "Italy"),
        ("JM", "Jamaica"),
        ("JP", "Japan"),
        ("JO", "Jordan"),
        ("KZ", "Kazakhstan"),
        ("KE", "Kenya"),
        ("KW", "Kuwait"),
        ("KG", "Kyrgyzstan"),
        ("LA", "Laos"),
        ("LV", "Latvia"),
        ("LB", "Lebanon"),
        ("LY", "Libya"),
        ("LT", "Lithuania"),
        ("LU", "Luxembourg"),
        ("MO", "Macao"),
        ("MY", "Malaysia"),
        ("MV", "Maldives"),
        ("MT", "Malta"),
        ("MX", "Mexico"),
        ("MD", "Moldova"),
        ("MN", "Mongolia"),
        ("ME", "Montenegro"),
        ("MA", "Morocco"),
        ("MM", "Myanmar"),
        ("NP", "Nepal"),
        ("NL", "Netherlands"),
        ("NZ", "New Zealand"),
        ("NI", "Nicaragua"),
        ("NG", "Nigeria"),
        ("MK", "North Macedonia"),
        ("NO", "Norway"),
        ("OM", "Oman"),
        ("PK", "Pakistan"),
        ("PS", "Palestine"),
        ("PA", "Panama"),
        ("PY", "Paraguay"),
        ("PE", "Peru"),
        ("PH", "Philippines"),
        ("PL", "Poland"),
        ("PT", "Portugal"),
        ("PR", "Puerto Rico"),
        ("QA", "Qatar"),
        ("RO", "Romania"),
        ("RU", "Russia"),
        ("SA", "Saudi Arabia"),
        ("SN", "Senegal"),
        ("RS", "Serbia"),
        ("SG", "Singapore"),
        ("SK", "Slovakia"),
        ("SI", "Slovenia"),
        ("SO", "Somalia"),
        ("ZA", "South Africa"),
        ("KR", "South Korea"),
        ("ES", "Spain"),
        ("LK", "Sri Lanka"),
        ("SD", "Sudan"),
        ("SE", "Sweden"),
        ("CH", "Switzerland"),
        ("SY", "Syria"),
        ("TW", "Taiwan"),
        ("TJ", "Tajikistan"),
        ("TZ", "Tanzania"),
        ("TH", "Thailand"),
        ("TN", "Tunisia"),
        ("TR", "Turkey"),
        ("TM", "Turkmenistan"),
        ("UG", "Uganda"),
        ("UA", "Ukraine"),
        ("AE", "United Arab Emirates"),
        ("UK", "United Kingdom"),
        ("US", "United States"),
        ("UY", "Uruguay"),
        ("UZ", "Uzbekistan"),
        ("VE", "Venezuela"),
        ("VN", "Vietnam"),
        ("YE", "Yemen"),
        ("ZW", "Zimbabwe")
    };

    public static readonly IReadOnlyList<string> Categories = new[] { "animation", "auto", "business", "classic", "comedy", "cooking", "culture", "documentary", "education", "entertainment", "family", "general", "interactive", "kids", "legislative", "lifestyle", "movies", "music", "news", "outdoor", "public", "relax", "religious", "science", "series", "shop", "sports", "travel", "weather" };

    public static IReadOnlyList<PlaylistSuggestion> CountrySuggestions(IEnumerable<(string Code, string Name)> countries) =>
        countries.Where(c => c.Code.Length is 2 or 3 && c.Code.All(char.IsAsciiLetter))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PlaylistSuggestion(c.Name, CountryUrl(c.Code))).ToList();

    public static IReadOnlyList<PlaylistSuggestion> CategorySuggestions() =>
        Categories.Select(c => new PlaylistSuggestion(char.ToUpperInvariant(c[0]) + c[1..], CategoryUrl(c))).ToList();

    /// <summary>Reads https://iptv-org.github.io/api/countries.json ([{"name":"Philippines","code":"PH",...}]).</summary>
    public static IReadOnlyList<(string Code, string Name)> ParseCountries(string json)
    {
        var list = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var code = e.TryGetProperty("code", out var c) ? c.GetString() : null;
                var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name)) list.Add((code!, name!));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { list.Clear(); }
        return list;
    }

    /// <summary>Filters suggestions by what the user typed (case-insensitive, any part of the name).</summary>
    public static IReadOnlyList<PlaylistSuggestion> Filter(IEnumerable<PlaylistSuggestion> all, string? text) =>
        string.IsNullOrWhiteSpace(text) ? all.ToList() : all.Where(s => s.Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
}
