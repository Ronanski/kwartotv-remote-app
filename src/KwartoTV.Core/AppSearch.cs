namespace KwartoTV.Core;

/// <summary>
/// A search said (voice) or typed on the phone for one of the TV's apps: "app.search" with "app|words",
/// app = netflix, youtube, bilibili, music or channels.
/// </summary>
public static class AppSearch
{
    public static readonly string[] Apps = { "netflix", "youtube", "bilibili", "music", "channels" };

    public static bool TryParse(string? arg, out string app, out string query)
    {
        app = ""; query = "";
        var bar = arg?.IndexOf('|') ?? -1;
        if (bar <= 0) return false;
        var a = arg![..bar];
        var q = arg[(bar + 1)..].Trim();
        if (Array.IndexOf(Apps, a) < 0 || !Music.IsQuery(q)) return false;
        app = a; query = q;
        return true;
    }
}
