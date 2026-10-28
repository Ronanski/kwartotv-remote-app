using System.Text;

namespace KwartoTV.Core;

public sealed record CatalogResult(IReadOnlyList<Channel> Channels, IReadOnlyList<string> Errors);

/// <summary>Loads every playlist the user added (links and local files) into one channel list.</summary>
public static class ChannelCatalog
{
    public static IReadOnlyList<Channel> LoadFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Playlist file not found.", path);
        if (info.Length > PlaylistLoader.MaxBytes) throw new InvalidDataException("Playlist is too large.");
        return M3uParser.Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static async Task<CatalogResult> LoadAllAsync(IEnumerable<PlaylistEntry> playlists, HttpClient http, CancellationToken ct = default)
    {
        var all = new List<Channel>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in playlists)
        {
            try
            {
                var list = Uri.TryCreate(p.Source, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeFile
                    ? await PlaylistLoader.LoadAsync(p.Source, http, ct)
                    : LoadFile(p.Source);
                foreach (var c in list) if (seen.Add(c.Url)) all.Add(c);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{p.Name}: {ex.Message}"); }
        }
        return new CatalogResult(all, errors);
    }
}
