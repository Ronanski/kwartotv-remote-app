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
