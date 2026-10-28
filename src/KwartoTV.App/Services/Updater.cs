using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>Checks the project's "latest" release and installs a newer KwartoTV.exe over this one.</summary>
public sealed class Updater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>The build number of the running app (1.0.N: N is the CI build number; 0 for a local build).</summary>
    public static int CurrentBuild => Assembly.GetEntryAssembly()?.GetName().Version?.Build ?? 0;

    public UpdateInfo? Available { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Returns the update when a newer build exists, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var json = await Http.GetStringAsync(Updates.ManifestUrl, cts.Token);
            var info = Updates.Parse(json);
            LastError = info is null ? "The update information could not be read." : null;
            Available = info is not null && Updates.IsNewer(info, CurrentBuild) ? info : null;
        }
        catch (Exception ex) { LastError = "Could not check for updates: " + ex.Message; Available = null; }
        return Available;
    }

    /// <summary>
    /// Downloads and checks the installer, then starts it silently (Windows asks permission once).
    /// The installer closes this app, installs, and starts the new version. Returns an error text, or null when started.
    /// </summary>
    public async Task<string?> InstallAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "KwartoTV-update", $"KwartoTV-{info.Build}.exe");
            await Updates.DownloadAsync(info.Exe, path, Http, progress);
            var psi = new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1")
            { UseShellExecute = true, Verb = "runas" };
            Process.Start(psi);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Update cancelled: Windows permission was declined."; }
        catch (Exception ex) { AppPaths.LogLine("update: " + ex); return "Update failed: " + ex.Message; }
    }
}
