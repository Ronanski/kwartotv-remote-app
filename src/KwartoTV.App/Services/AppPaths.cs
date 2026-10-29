namespace KwartoTV.App.Services;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KwartoTV");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Devices => Path.Combine(Root, "devices.json");
    public static string Health => Path.Combine(Root, "health.json");
    public static string PlaylistCache => Path.Combine(Root, "playlists.cache.json");
    public static string PastedPlaylists => Path.Combine(Root, "playlists");
    public static string Log => Path.Combine(Root, "log.txt");
    public static string Profile(string name) => Path.Combine(Root, "profiles", name);

    public static void Ensure() => Directory.CreateDirectory(Root);

    public static void LogLine(string text)
    {
        try { File.AppendAllText(Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}"); } catch { }
    }
}
