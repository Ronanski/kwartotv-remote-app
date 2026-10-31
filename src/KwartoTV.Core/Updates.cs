using System.Security.Cryptography;
using System.Text.Json;

namespace KwartoTV.Core;

public sealed record UpdateFile(string Url, string Sha256, int VersionCode = 0);

/// <summary>What the "latest" release says is the newest build of the Windows installer and the Android remote.</summary>
public sealed record UpdateInfo(int Build, string Version, UpdateFile Exe, UpdateFile Apk, string? Notes);

public static class Updates
{
    /// <summary>Update manifest attached to every release; GitHub sends this address to the newest release (see .github/workflows/build.yml).</summary>
    public const string ManifestUrl = "https://github.com/Ronanski/kwartotv-remote-app/releases/latest/download/KwartoTV-update.json";

    /// <summary>Files are only ever downloaded from this project's own releases.</summary>
    public const string AllowedPrefix = "https://github.com/Ronanski/kwartotv-remote-app/releases/download/";

    // Checked on the parsed address, the one actually downloaded: "../" in the text would otherwise lead out of this project.
    public static bool IsAllowedUrl(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.AbsoluteUri.StartsWith(AllowedPrefix, StringComparison.Ordinal);

    public static UpdateInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var info = new UpdateInfo(
                r.GetProperty("build").GetInt32(),
                r.GetProperty("version").GetString() ?? "",
                File(r.GetProperty("exe")), File(r.GetProperty("apk")),
                r.TryGetProperty("notes", out var n) ? n.GetString() : null);
            return info.Build > 0 && IsAllowedUrl(info.Exe.Url) && IsAllowedUrl(info.Apk.Url) && IsSha256(info.Exe.Sha256) && IsSha256(info.Apk.Sha256) ? info : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }

        static UpdateFile File(JsonElement e) => new(
            e.GetProperty("url").GetString() ?? "", (e.GetProperty("sha256").GetString() ?? "").ToLowerInvariant(),
            e.TryGetProperty("versionCode", out var vc) ? vc.GetInt32() : 0);
    }

    private static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    /// <summary>True when the release's build number is higher than the running app's.</summary>
    public static bool IsNewer(UpdateInfo info, int currentBuild) => info.Build > currentBuild;

    /// <summary>
    /// The build number inside a file version. Releases are MAJOR.MINOR.PATCH.BUILD (BUILD only ever goes up);
    /// builds before that were 1.0.BUILD.
    /// </summary>
    public static int BuildOf(Version? v) => v is null ? 0 : v.Revision > 0 ? v.Revision : Math.Max(v.Build, 0);

    public static string Sha256Of(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>Downloads a release file, checks its SHA-256 and returns the local path. Throws on a mismatch.</summary>
    public static async Task<string> DownloadAsync(UpdateFile file, string destination, HttpClient http, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsAllowedUrl(file.Url)) throw new InvalidOperationException("Update address is not allowed.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var tmp = destination + ".part";
        using (var resp = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (!string.Equals(Sha256Of(tmp), file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tmp);
            throw new InvalidDataException("The downloaded update is damaged (checksum does not match).");
        }
        File.Move(tmp, destination, overwrite: true);
        return destination;
    }
}
