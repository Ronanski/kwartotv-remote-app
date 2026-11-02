using System.Text.Json;

namespace KwartoTV.Core;

/// <summary>
/// The lock screen's look, set from the phone's Lock workspace ("lock.set key=value"): the clock style, its color, the
/// brightness, what shows under the clock (date, song, Bible verse), the background (photos, black, midnight), the photo
/// folder and how often the photo changes, and when the screensaver comes by itself. Every value is checked here.
/// </summary>
public static class LockOptions
{
    public static readonly string[] Clocks = { "classic", "big", "corner" };
    /// <summary>The clock colors the phone offers (white, warm, blue, lavender).</summary>
    public static readonly string[] Colors = { "FFFFFF", "FFD8A8", "9DB8FF", "C9B6FF" };
    public static readonly int[] Dims = { 100, 70, 40 };
    public static readonly string[] Backgrounds = { "photos", "black", "midnight" };
    public static readonly int[] PhotoSeconds = { 10, 20, 30, 60, 300 };
    public static readonly int[] SaverMinutes = { 0, 5, 10, 15, 30, 60 };

    /// <summary>
    /// Applies "key=value" to [s]. [folderAllowed] says whether a photo folder may be used (it must be one the TV offered).
    /// False (and nothing changed) when the key or value is not one of the choices.
    /// </summary>
    public static bool TryApply(AppSettings s, string? arg, Func<string, bool>? folderAllowed = null)
    {
        if (string.IsNullOrEmpty(arg)) return false;
        var eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        var key = arg[..eq];
        var value = arg[(eq + 1)..];
        switch (key)
        {
            case "clock" when Array.IndexOf(Clocks, value) >= 0: s.LockClock = value; return true;
            case "color" when Array.IndexOf(Colors, value.ToUpperInvariant()) >= 0: s.LockColor = value.ToUpperInvariant(); return true;
            case "dim" when Int(value) is { } d && Array.IndexOf(Dims, d) >= 0: s.LockDim = d; return true;
            case "date" when Bool(value) is { } b: s.LockDate = b; return true;
            case "song" when Bool(value) is { } b: s.LockSong = b; return true;
            case "verse" when Bool(value) is { } b: s.VerseEveryMinutes = b ? (s.VerseEveryMinutes > 0 ? s.VerseEveryMinutes : 60) : 0; return true;
            case "bg" when Array.IndexOf(Backgrounds, value) >= 0:
                if (value == "photos") s.SlideshowOn = true;
                else { s.SlideshowOn = false; s.LockBackground = value; }
                return true;
            case "every" when Int(value) is { } e && Array.IndexOf(PhotoSeconds, e) >= 0: s.SlideshowSeconds = e; return true;
            case "saver" when Int(value) is { } m && Array.IndexOf(SaverMinutes, m) >= 0: s.ScreensaverMinutes = m; return true;
            case "folder" when value.Length is > 0 and <= 400 && !value.Any(char.IsControl) && folderAllowed?.Invoke(value) == true:
                s.SlideshowFolder = value; return true;
        }
        return false;
    }

    private static int? Int(string v) => int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : null;
    private static bool? Bool(string v) => v switch { "1" or "on" or "true" => true, "0" or "off" or "false" => false, _ => null };

    /// <summary>The background the lock screen shows now: photos only with a folder that has some.</summary>
    public static string Background(AppSettings s) => s.SlideshowOn ? "photos" : (Array.IndexOf(Backgrounds, s.LockBackground) > 0 ? s.LockBackground : "black");

    /// <summary>The phone's "lock.state": every option, whether the TV is locked, the photo folder and its photos, the sleep timer.</summary>
    public static string StateMessage(AppSettings s, bool locked, int photoCount, int sleepMinutesLeft) => JsonSerializer.Serialize(new
    {
        type = "lock.state",
        locked,
        clock = Array.IndexOf(Clocks, s.LockClock) >= 0 ? s.LockClock : "classic",
        color = Array.IndexOf(Colors, s.LockColor) >= 0 ? s.LockColor : "FFFFFF",
        dim = Array.IndexOf(Dims, s.LockDim) >= 0 ? s.LockDim : 100,
        date = s.LockDate,
        song = s.LockSong,
        verse = s.VerseEveryMinutes > 0,
        bg = Background(s),
        folder = s.SlideshowFolder ?? "",
        folderName = FolderName(s.SlideshowFolder),
        photos = photoCount,
        every = s.SlideshowSeconds,
        saver = s.ScreensaverMinutes,
        timer = sleepMinutesLeft,
    });

    public static string FolderName(string? folder) =>
        string.IsNullOrWhiteSpace(folder) ? "" : folder.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault(x => x.Length > 0) ?? folder;

    /// <summary>The folders the phone can pick for the photos ("lock.folders"): [path, name] pairs, at most 40.</summary>
    public static string FoldersMessage(IEnumerable<string> folders) => JsonSerializer.Serialize(new
    {
        type = "lock.folders",
        folders = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).Take(40)
            .Select(f => new { path = f, name = FolderName(f) }).ToArray(),
    });

    /// <summary>One photo for the phone's live preview ("lock.photo"): its place, how many there are, and a small JPEG.</summary>
    public static string PhotoMessage(int index, int count, byte[]? jpeg) => JsonSerializer.Serialize(new
    {
        type = "lock.photo",
        i = index, n = count,
        jpg = jpeg is null ? "" : Convert.ToBase64String(jpeg),
    });
}
