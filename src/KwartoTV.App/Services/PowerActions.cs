using System.Diagnostics;

namespace KwartoTV.App.Services;

/// <summary>Whole-PC power actions (not just this app). Used by the remote and by the Power menu on screen.</summary>
public static class PowerActions
{
    public static void Sleep() => Native.Sleep();

    public static void Restart() => Run("/r /t 0");

    public static void Shutdown() => Run("/s /t 0");

    private static void Run(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
}
