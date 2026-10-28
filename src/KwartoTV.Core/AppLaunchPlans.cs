using System.Diagnostics;

namespace KwartoTV.Core;

public sealed record LaunchSpec(string FileName, IReadOnlyList<string> Args, bool UseShellExecute = false);

public interface IAppLauncher
{
    bool Launch(LaunchSpec spec);
}

/// <summary>Builds launch commands for the TV tiles. Pure logic, so it can be tested on any OS.</summary>
public static class AppLaunchPlans
{
    public const string YouTubeTvUrl = "https://www.youtube.com/tv";

    // Netflix from the Microsoft Store. This is only the fallback: the shell looks the real id up on the PC
    // (Get-StartApps | Where Name -like "*Netflix*", see NetflixLocator) and prefers that.
    public const string NetflixAppUserModelId = "4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix.App";

    private static readonly string[] BrowserCandidates =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    };

    public static string? FindBrowser(Func<string, bool> fileExists) => BrowserCandidates.FirstOrDefault(fileExists);

    /// <summary>
    /// Fullscreen app window with its own browser profile, so the YouTube Premium sign-in is kept
    /// and never mixes with the normal browser.
    /// </summary>
    public static LaunchSpec? Web(string url, string profileDir, Func<string, bool> fileExists)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
        var exe = FindBrowser(fileExists);
        if (exe is null) return null;
        return new LaunchSpec(exe, new[]
        {
            $"--app={u.AbsoluteUri}",
            "--start-fullscreen",
            $"--user-data-dir={profileDir}",
            "--no-first-run",
            "--no-default-browser-check",
        });
    }

    public static LaunchSpec? YouTube(string profileDir, Func<string, bool> fileExists) =>
        Web(YouTubeTvUrl, profileDir, fileExists);

    public static LaunchSpec Netflix(string? appUserModelId = null) =>
        new("explorer.exe", new[] { $@"shell:AppsFolder\{(string.IsNullOrWhiteSpace(appUserModelId) ? NetflixAppUserModelId : appUserModelId)}" });
}

public sealed class ProcessAppLauncher : IAppLauncher
{
    public bool Launch(LaunchSpec spec)
    {
        try
        {
            var psi = new ProcessStartInfo(spec.FileName) { UseShellExecute = spec.UseShellExecute };
            foreach (var a in spec.Args) psi.ArgumentList.Add(a);
            return Process.Start(psi) is not null;
        }
        catch { return false; }
    }
}
