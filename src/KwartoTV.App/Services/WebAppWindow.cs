using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>
/// A website shown fullscreen inside KwartoTV (Edge WebView2), with its own saved login.
/// YouTube uses a TV browser identity, so it shows the TV layout and the "sign in with your phone" code
/// and appears as a cast target in the YouTube phone app.
/// </summary>
public sealed class WebAppWindow : Window
{
    public const string TvUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; Xbox; Xbox One) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edge/120.0.0.0";

    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Black };
    private readonly string _url, _profile;
    private readonly string? _userAgent;

    public ForegroundKind Kind { get; }

    /// <summary>The embedded browser could not start (runtime missing). The caller falls back to an external browser.</summary>
    public event Action<string>? Failed;
    /// <summary>User pressed F1 inside the page.</summary>
    public event Action? HomeRequested;

    public WebAppWindow(string url, string profileName, ForegroundKind kind, string? userAgent = null)
    {
        _url = url; _profile = profileName; Kind = kind; _userAgent = userAgent;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        Background = Brushes.Black; ShowInTaskbar = false; Title = "KwartoTV Web";
        Content = _web;
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => { try { _web.Dispose(); } catch { } };
    }

    private async Task StartAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile("webview-" + _profile), options);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            if (_userAgent is not null) core.Settings.UserAgent = _userAgent;
            // Pop-ups open in the same window, and only over https.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps) core.Navigate(e.Uri);
            };
            // F1 inside the page returns to the KwartoTV home screen.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.addEventListener('keydown', e => { if (e.key === 'F1') { e.preventDefault(); window.chrome.webview.postMessage('home'); } }, true);");
            core.WebMessageReceived += (_, e) =>
            {
                if (e.TryGetWebMessageAsString() == "home") Dispatcher.BeginInvoke(() => HomeRequested?.Invoke());
            };
            core.Navigate(_url);
            _web.Focus();
        }
        catch (Exception ex)
        {
            AppPaths.LogLine("webview: " + ex.Message);
            Failed?.Invoke(ex.Message);
        }
    }

    /// <summary>Give the page keyboard focus again after the window is activated.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_web.CoreWebView2 is not null) _web.Focus();
    }
}
