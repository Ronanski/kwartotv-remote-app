using System.Windows;
using KwartoTV.App.Services;

namespace KwartoTV.App;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();
        _single = new Mutex(true, "KwartoTV.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) =>
        {
            AppPaths.LogLine("UI error: " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppPaths.LogLine("Fatal: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { AppPaths.LogLine("Task error: " + ex.Exception); ex.SetObserved(); };

        new MainWindow().Show();

        // Test hook used by CI: checks that the arrow keys reach every control on every screen, then quits.
        var audit = Array.IndexOf(e.Args, "--nav-audit");
        if (audit >= 0 && audit + 1 < e.Args.Length)
        {
            var report = Path.GetFullPath(e.Args[audit + 1]);
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(8000);
                // Not Application.MainWindow: that is the first window made (the volume overlay), not the home window.
                try
                {
                    if (Windows.OfType<global::KwartoTV.App.MainWindow>().FirstOrDefault() is { } mw) await mw.RunNavAuditAsync(report);
                    else File.WriteAllText(report, "RESULT: audit failed: no home window");
                }
                catch (Exception ex) { File.WriteAllText(report, "RESULT: audit failed: " + ex); }
                Shutdown();
            });
        }

        // Test hook used by CI: runs the same update path the Update dialog uses, with a local installer.
        var i = Array.IndexOf(e.Args, "--apply-update");
        if (i >= 0 && i + 1 < e.Args.Length && File.Exists(e.Args[i + 1]))
        {
            var installer = Path.GetFullPath(e.Args[i + 1]);
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(6000);
                Updater.StartInstaller(installer);
                Shutdown();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
