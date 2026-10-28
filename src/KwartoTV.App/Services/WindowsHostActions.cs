using System.Diagnostics;
using System.Globalization;
using System.Windows;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>What the TV shell can do when a remote asks for it. Implemented by MainWindow; always called on the UI thread.</summary>
public interface IShell
{
    ForegroundKind ForegroundApp { get; }
    IntPtr WindowHandle { get; }
    void GoHome();
    void ShowLiveTv();
    void ShowLibrary();
    void ShowSettings();
    void LaunchYouTube();
    void LaunchNetflix();
    /// <summary>Handled by the built-in player? Returns false when nothing is playing in KwartoTV.</summary>
    bool PlayerTogglePause();
    bool PlayerSeek(int seconds);
    bool PlayerSpeed(double speed);
    bool PlayerStop();
    /// <summary>Volume of the built-in player (+/- points). False when nothing is playing in KwartoTV, so system volume keys are used.</summary>
    bool PlayerVolume(int delta);
    bool PlayerSetVolume(int percent);
    bool PlayerMute();
}

/// <summary>Windows implementation of IHostActions: navigation keys, media keys, volume, text, apps and power.</summary>
public sealed class WindowsHostActions : IHostActions
{
    private IShell? _shell;
    public void Attach(IShell shell) => _shell = shell;

    public void Execute(string cmd, string? arg)
    {
        var shell = _shell ?? throw new InvalidOperationException("shell not ready");
        var ui = Application.Current.Dispatcher;

        switch (cmd)
        {
            case "nav.home": ui.Invoke(shell.GoHome); return;
            case "app.livetv": ui.Invoke(shell.ShowLiveTv); return;
            case "app.library": ui.Invoke(shell.ShowLibrary); return;
            case "app.settings": ui.Invoke(shell.ShowSettings); return;
            case "app.youtube": ui.Invoke(shell.LaunchYouTube); return;
            case "app.netflix": ui.Invoke(shell.LaunchNetflix); return;

            case "media.playpause":
                if (ui.Invoke(shell.PlayerTogglePause)) return;
                Native.Press(HostKeyMap.Media(cmd)!); return;
            case "media.stop":
                if (ui.Invoke(shell.PlayerStop)) return;
                Native.Press(HostKeyMap.Media(cmd)!); return;
            case "media.seek":
            {
                var s = int.Parse(arg!, CultureInfo.InvariantCulture);
                if (ui.Invoke(() => shell.PlayerSeek(s))) return;
                Native.Press(HostKeyMap.Seek(s)); return;
            }
            case "media.speed":
                ui.Invoke(() => shell.PlayerSpeed(double.Parse(arg!, CultureInfo.InvariantCulture))); return;
            case "media.volume":
            {
                var v = int.Parse(arg!, CultureInfo.InvariantCulture);
                if (ui.Invoke(() => shell.PlayerSetVolume(v))) return;
                Native.Press(HostKeyMap.VolumeTo(v)); return;
            }
            case "media.volup":
                if (ui.Invoke(() => shell.PlayerVolume(+5))) return;
                break;
            case "media.voldown":
                if (ui.Invoke(() => shell.PlayerVolume(-5))) return;
                break;
            case "media.mute":
                if (ui.Invoke(shell.PlayerMute)) return;
                break;
            case "mouse.move":
                CommandRouter.TryMove(arg, out var mdx, out var mdy);
                Native.MoveMouse(mdx, mdy); return;
            case "mouse.click": Native.MouseClick(false); return;
            case "mouse.rightclick": Native.MouseClick(true); return;
            case "mouse.scroll":
                Native.MouseScroll(-int.Parse(arg!, CultureInfo.InvariantCulture)); return;
            case "key.text":
                Native.TypeText(arg!); return;

            case "power.sleep": Native.Sleep(); return;
            case "power.screenoff": Native.ScreenOff(shell.WindowHandle); return;
            case "power.screenon": Native.WakeScreen(); return;
            case "power.shutdown": Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); return;
            case "power.restart": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); return;
        }

        var keys = HostKeyMap.Navigation(cmd, shell.ForegroundApp) ?? HostKeyMap.Media(cmd);
        if (keys is null) throw new NotSupportedException(cmd);
        Native.Press(keys);
    }
}
