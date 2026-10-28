using System.Diagnostics;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>Starts and stops the YouTube, Netflix and website tiles that run outside KwartoTV.</summary>
public sealed class ExternalApps
{
    private readonly IAppLauncher _launcher = new ProcessAppLauncher();
    private Process? _proc;
    private string? _netflixName;

    public ForegroundKind Kind { get; private set; } = ForegroundKind.KwartoTV;
    public bool IsRunning => Kind != ForegroundKind.KwartoTV;

    /// <summary>Raised on a worker thread when the external app was closed by the user.</summary>
    public event Action? Exited;

    public string? LastError { get; private set; }

    public bool LaunchYouTube()
    {
        var spec = AppLaunchPlans.YouTube(AppPaths.Profile("youtube"), File.Exists);
        if (spec is null) return Fail("Install Microsoft Edge or Google Chrome to use YouTube.");
        return Start(spec, ForegroundKind.YouTube);
    }

    public bool LaunchWeb(WebTile tile)
    {
        var spec = AppLaunchPlans.Web(tile.Url, AppPaths.Profile("web-" + tile.Id), File.Exists);
        if (spec is null) return Fail("Install Microsoft Edge or Google Chrome to open websites.");
        return Start(spec, ForegroundKind.Web);
    }

    public bool LaunchNetflix(string? appId)
    {
        _netflixName = "Netflix";
        var spec = AppLaunchPlans.Netflix(appId);
        if (!Start(spec, ForegroundKind.Netflix, trackProcess: false)) return false;
        return true;
    }

    /// <summary>Looks up the real Netflix app id on this PC (it differs between installs).</summary>
    public static string? DiscoverNetflixAppId()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", NetflixLocator.PowerShellArgs)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return null; }
            return NetflixLocator.Find(output);
        }
        catch { return null; }
    }

    private bool Start(LaunchSpec spec, ForegroundKind kind, bool trackProcess = true)
    {
        Close();
        try
        {
            var psi = new ProcessStartInfo(spec.FileName) { UseShellExecute = spec.UseShellExecute };
            foreach (var a in spec.Args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            if (p is null) return Fail("Could not start the app.");
            Kind = kind; LastError = null;
            if (trackProcess)
            {
                _proc = p;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) => { if (ReferenceEquals(_proc, p)) { _proc = null; Kind = ForegroundKind.KwartoTV; Exited?.Invoke(); } };
            }
            return true;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    /// <summary>Closes the running external app (if any) and returns control to KwartoTV.</summary>
    public void Close()
    {
        var p = _proc; _proc = null;
        if (p is not null)
        {
            try { if (!p.HasExited) { p.CloseMainWindow(); if (!p.WaitForExit(1200)) p.Kill(entireProcessTree: true); } } catch { }
        }
        if (Kind == ForegroundKind.Netflix && _netflixName is not null)
        {
            foreach (var n in Process.GetProcessesByName(_netflixName))
            {
                try { n.CloseMainWindow(); if (!n.WaitForExit(1200)) n.Kill(); } catch { }
            }
        }
        Kind = ForegroundKind.KwartoTV;
    }

    private bool Fail(string msg) { LastError = msg; return false; }
}
