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
    /// Downloads and checks the installer, then hands it to <see cref="StartInstaller"/>.
    /// Returns an error text, or null when the update has started (the caller then closes the app).
    /// </summary>
    public async Task<string?> InstallAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        // Installed with the update task: it downloads, checks and installs by itself, with no permission prompt.
        if (await StartUpdateTaskAsync()) return null;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "KwartoTV-update", $"KwartoTV-{info.Build}.exe");
            await Updates.DownloadAsync(info.Exe, path, Http, progress);
            StartInstaller(path);
            return null;
        }
        catch (Exception ex) { AppPaths.LogLine("update: " + ex); return "Update failed: " + ex.Message; }
    }

    public const string TaskName = "KwartoTV Update";

    /// <summary>
    /// Starts the "KwartoTV Update" scheduled task that the installer made (see installer/update-task.ps1), when it exists for
    /// the user running KwartoTV. Returns false when there is no such task, so the caller falls back to the permission prompt.
    /// </summary>
    public static async Task<bool> StartUpdateTaskAsync()
    {
        try
        {
            var (code, csv) = await SchTasks($"/Query /TN \"{TaskName}\" /V /FO CSV /NH");
            // "Run As User" is one of the columns; a task made for another account would never run in this session.
            if (code != 0 || !csv.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)) { AppPaths.LogLine("update: no update task for this user"); return false; }
            var (run, _) = await SchTasks($"/Run /TN \"{TaskName}\"");
            AppPaths.LogLine($"update: update task started ({run})");
            return run == 0;
        }
        catch (Exception ex) { AppPaths.LogLine("update task: " + ex.Message); return false; }
    }

    private static async Task<(int Code, string Output)> SchTasks(string args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, output);
    }

    /// <summary>
    /// Starts a small hidden helper that waits for this app to close, runs the installer silently
    /// (Windows asks permission once), and then opens KwartoTV again as the normal user, whether the
    /// install worked or not. Running the installer while the app is still open made it quit silently.
    /// </summary>
    public static void StartInstaller(string installer)
    {
        var dir = Path.Combine(Path.GetTempPath(), "KwartoTV-update");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "apply-update.ps1");
        File.WriteAllText(script, HelperScript);
        var app = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KwartoTV.exe");
        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -AppPid {Environment.ProcessId} " +
                   $"-Installer \"{installer}\" -App \"{app}\" -Log \"{Path.Combine(dir, "install.log")}\"";
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        Process.Start(psi);
        AppPaths.LogLine("update: helper started for " + installer);
    }

    private const string HelperScript = """
        param([int]$AppPid, [string]$Installer, [string]$App, [string]$Log)
        $status = Join-Path (Split-Path $Log) "status.txt"
        function Say($t) { "$(Get-Date -Format s) $t" | Out-File -FilePath $status -Append -Encoding utf8 }
        Say "waiting for KwartoTV ($AppPid) to close"
        try { Wait-Process -Id $AppPid -Timeout 30 -ErrorAction Stop } catch { }
        Get-Process -Id $AppPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        $code = -1
        try {
            $p = Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LOG=`"$Log`"" -PassThru -Wait
            $code = $p.ExitCode
        } catch { Say "installer did not start: $($_.Exception.Message)" }
        Say "installer finished with code $code"
        Start-Sleep -Seconds 1
        if (-not (Get-Process -Name KwartoTV -ErrorAction SilentlyContinue)) { Start-Process -FilePath $App; Say "KwartoTV opened again" }
        """;
}
