namespace KwartoTV.Core;

public sealed record LibraryItem(string Path, string Title, string Folder, long Bytes);

/// <summary>Lists playable video/audio files in the folders the user added to the Library.</summary>
public static class LibraryScanner
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".webm", ".ts", ".wmv", ".mpg", ".mpeg", ".mp3", ".flac", ".m4a", ".ogg", ".wav",
    };

    public static IReadOnlyList<LibraryItem> Scan(IEnumerable<string> folders, int maxFiles = 2000)
    {
        var result = new List<LibraryItem>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.EnumerateFiles(folder, "*", opts))
            {
                if (!Extensions.Contains(Path.GetExtension(f))) continue;
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }
                result.Add(new LibraryItem(f, Path.GetFileNameWithoutExtension(f), new DirectoryInfo(folder).Name, size));
                if (result.Count >= maxFiles) return Sorted(result);
            }
        }
        return Sorted(result);
    }

    private static List<LibraryItem> Sorted(List<LibraryItem> l) =>
        l.OrderBy(i => i.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();
}
