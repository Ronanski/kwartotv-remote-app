using System.Diagnostics;
using System.Windows;
using System.Reflection;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>Checks the project's "latest" release and installs a newer KwartoTV.exe over this one.</summary>
public sealed class Updater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>The build number of the running app (the last part of MAJOR.MINOR.PATCH.BUILD; 0 for a local build).</summary>
    public static int CurrentBuild => Updates.BuildOf(Assembly.GetEntryAssembly()?.GetName().Version);

    /// <summary>The release version of the running app, e.g. 1.1.4.</summary>
    public static string CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

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
        // Older Program Files installs with the update task: it downloads, checks and installs by itself, with no prompt.
        // (Current installs are per user: the installer below needs no permission prompt at all.)
        if (!IsPerUserInstall && await StartUpdateTaskAsync()) return null;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "KwartoTV-update", $"KwartoTV-{info.Build}.exe");
            await Updates.DownloadAsync(info.Exe, path, Http, progress);
            StartInstaller(path, info.Version);
            return null;
        }
        catch (Exception ex) { AppPaths.LogLine("update: " + ex); return "Update failed: " + ex.Message; }
    }

    public const string TaskName = "KwartoTV Update";

    /// <summary>
    /// Installed for this user (in %LocalAppData%), so an update replaces the files with no Windows permission prompt.
    /// Older versions were installed in Program Files.
    /// </summary>
    public static bool IsPerUserInstall =>
        AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
        || !AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

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
    /// Starts a small helper that waits for this app to close, shows an "Updating KwartoTV…" screen on the TV (so nobody
    /// thinks it stopped working), runs the installer silently, opens KwartoTV again as the normal user, and closes its
    /// screen once KwartoTV is back. Running the installer while the app is still open made it quit silently.
    /// </summary>
    public static void StartInstaller(string installer, string? version = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "KwartoTV-update");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "apply-update.ps1");
        File.WriteAllText(script, HelperScript, new System.Text.UTF8Encoding(true));   // with a BOM: Windows PowerShell reads it as UTF-8
        var app = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KwartoTV.exe");
        // The waiting screen goes on the screen KwartoTV is on (the TV), in screen pixels.
        var screen = Application.Current?.Dispatcher.Invoke(() =>
            Application.Current.MainWindow is { } mw ? Native.ScreenOf(new System.Windows.Interop.WindowInteropHelper(mw).Handle) : null);
        var where = screen is { } r ? $" -X {r.X} -Y {r.Y} -W {r.W} -H {r.H}" : "";
        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File \"{script}\" -AppPid {Environment.ProcessId} " +
                   $"-Installer \"{installer}\" -App \"{app}\" -Log \"{Path.Combine(dir, "install.log")}\" -Version \"{version ?? ""}\"{where}";
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        Process.Start(psi);
        AppPaths.LogLine("update: helper started for " + installer);
    }

    private const string HelperScript = """
        param([int]$AppPid, [string]$Installer, [string]$App, [string]$Log, [string]$Version = "", [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0)
        $status = Join-Path (Split-Path $Log) "status.txt"
        function Say($t) { "$(Get-Date -Format s) $t" | Out-File -FilePath $status -Append -Encoding utf8 }

        # The waiting screen: KwartoTV is closed while it updates, so say so on the TV.
        $form = $null
        try {
            Add-Type -Name Dpi -Namespace KwartoTV -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
            [KwartoTV.Dpi]::SetProcessDPIAware() | Out-Null
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $form = New-Object System.Windows.Forms.Form
            $form.FormBorderStyle = 'None'; $form.ShowInTaskbar = $false; $form.TopMost = $true; $form.StartPosition = 'Manual'
            $form.BackColor = [System.Drawing.Color]::FromArgb(14, 16, 22); $form.Text = "Updating KwartoTV"; $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
            if ($W -gt 0 -and $H -gt 0) { $form.Bounds = New-Object System.Drawing.Rectangle $X, $Y, $W, $H } else { $form.WindowState = 'Maximized' }
            function Lbl($text, $size, $style, $color) {
                $l = New-Object System.Windows.Forms.Label
                $l.Text = $text; $l.AutoSize = $true; $l.ForeColor = $color; $l.BackColor = $form.BackColor
                $l.Font = New-Object System.Drawing.Font("Segoe UI", $size, $style)
                $form.Controls.Add($l); return $l
            }
            $accent = [System.Drawing.Color]::FromArgb(255, 122, 69)
            $brand = Lbl "KwartoTV" 40 ([System.Drawing.FontStyle]::Bold) $accent
            $what = Lbl ($(if ($Version) { "Updating to version $Version" } else { "Updating" })) 26 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::White)
            $dots = Lbl "●  ○  ○" 22 ([System.Drawing.FontStyle]::Regular) $accent
            $note = Lbl "KwartoTV opens again by itself in about a minute. Please do not turn off the PC." 15 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(160, 166, 180))
            $place = {
                $cw = $form.ClientSize.Width; $y0 = [int]($form.ClientSize.Height / 2 - 150)
                foreach ($pair in @(@($brand, 0), @($what, 90), @($dots, 160), @($note, 230))) {
                    $pair[0].Left = [int](($cw - $pair[0].Width) / 2); $pair[0].Top = $y0 + $pair[1]
                }
            }
            $form.Add_Shown($place); $form.Add_Resize($place)
        } catch { Say "waiting screen not shown: $($_.Exception.Message)"; $form = $null }

        $state = @{ Phase = 0; Since = Get-Date; Started = Get-Date; Proc = $null; Tick = 0 }
        $step = {
            switch ($state.Phase) {
                0 {
                    # Wait for KwartoTV to close (up to 30 s), then run the installer without a window.
                    if ((Get-Process -Id $AppPid -ErrorAction SilentlyContinue) -and ((Get-Date) - $state.Since).TotalSeconds -lt 30) { return }
                    Get-Process -Id $AppPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                    Say "KwartoTV closed; installing"
                    try { $state.Proc = Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LOG=`"$Log`"" -PassThru; $null = $state.Proc.Handle }
                    catch { Say "installer did not start: $($_.Exception.Message)"; Say "installer finished with code -1"; $state.Phase = 2; $state.Since = Get-Date; return }
                    $state.Phase = 1
                }
                1 {
                    if (-not $state.Proc.HasExited) { return }
                    Say "installer finished with code $($state.Proc.ExitCode)"
                    Start-Sleep -Milliseconds 800
                    $state.Phase = 2; $state.Since = Get-Date
                }
                2 {
                    # Open KwartoTV again (unless the installer already did), then wait until its window is up.
                    if (-not (Get-Process -Name KwartoTV -ErrorAction SilentlyContinue)) {
                        $exe = $App
                        $moved = Join-Path $env:LOCALAPPDATA "Programs\KwartoTV\KwartoTV.exe"
                        if (-not (Test-Path $exe) -and (Test-Path $moved)) { $exe = $moved }
                        Start-Process -FilePath $exe; Say "KwartoTV opened again"
                    } else { Say "KwartoTV opened again" }
                    $state.Phase = 3; $state.Since = Get-Date
                }
                3 {
                    $up = Get-Process -Name KwartoTV -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
                    if ($up -or ((Get-Date) - $state.Since).TotalSeconds -gt 25) { $state.Phase = 4 }
                }
            }
        }

        if ($form) {
            $timer = New-Object System.Windows.Forms.Timer
            $timer.Interval = 400
            $timer.Add_Tick({
                $state.Tick++
                $dots.Text = @("●  ○  ○", "○  ●  ○", "○  ○  ●")[$state.Tick % 3]
                $dots.Left = [int](($form.ClientSize.Width - $dots.Width) / 2)
                & $step
                if ($state.Phase -ge 4 -or ((Get-Date) - $state.Started).TotalMinutes -gt 6) { $timer.Stop(); $form.Close() }
            })
            $timer.Start()
            [void]$form.ShowDialog()
        } else {
            while ($state.Phase -lt 4 -and ((Get-Date) - $state.Started).TotalMinutes -lt 6) { & $step; Start-Sleep -Milliseconds 400 }
        }
        """;
}
