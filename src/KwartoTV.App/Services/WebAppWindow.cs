using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KwartoTV.App.Ui;
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
    private OskWindow? _osk;
    private WebTextTarget? _text;   // the field the keyboard types into; the page reports its text here
    private int _focusLogs;

    /// <summary>The embedded browser could not start (runtime missing). The caller falls back to an external browser.</summary>
    public event Action<string>? Failed;
    /// <summary>User pressed F1 inside the page.</summary>
    public event Action? HomeRequested;
    /// <summary>▲ / ▼ while a YouTube channel from the lineup is showing (video-only mode).</summary>
    public event Action? ChannelUp, ChannelDown;
    private readonly bool _videoOnly;

    /// <summary>Shows only the video of a YouTube watch/live page, fullscreen, and turns ▲▼ into channel up/down.</summary>
    private const string VideoOnlyScript = @"
(function () {
  if (window.__kwVideo) return; window.__kwVideo = true;
  var css = 'html,body{overflow:hidden!important;background:#000!important}' +
    '#masthead-container,#secondary,#below,#related,#comments,ytd-watch-metadata,#chat,#chat-container,tp-yt-app-drawer,ytd-mini-guide-renderer{display:none!important}' +
    '#player-full-bleed-container,#player-container-outer,#player-container-inner,#player-container,#player,ytd-player,#ytd-player,#movie_player{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;z-index:99999!important;background:#000!important}' +
    'video.html5-main-video{width:100vw!important;height:100vh!important;left:0!important;top:0!important;object-fit:contain!important}';
  // YouTube's security policy blocks added <style> tags, so use a constructed style sheet (not covered by that policy).
  var sheet = null;
  function add() {
    try {
      if (!sheet) { sheet = new CSSStyleSheet(); sheet.replaceSync(css); }
      if (document.adoptedStyleSheets.indexOf(sheet) < 0) document.adoptedStyleSheets = document.adoptedStyleSheets.concat([sheet]);
    } catch (x) {
      if (document.getElementById('kw-video')) return; var s = document.createElement('style'); s.id = 'kw-video'; s.textContent = css; (document.head || document.documentElement).appendChild(s);
    }
  }
  add(); document.addEventListener('DOMContentLoaded', add); window.addEventListener('yt-navigate-finish', add);
  setInterval(function () { add(); var v = document.querySelector('video'); if (v && v.paused && !v.ended && !window.__kwPausedByUser) { v.play().catch(function(){}); } }, 2000);
  window.addEventListener('keydown', function (e) {
    if (e.key === 'ArrowUp' || e.key === 'PageUp') { e.preventDefault(); e.stopPropagation(); window.chrome.webview.postMessage('chup'); }
    else if (e.key === 'ArrowDown' || e.key === 'PageDown') { e.preventDefault(); e.stopPropagation(); window.chrome.webview.postMessage('chdown'); }
    else if (e.key === 'Escape' || e.key === 'BrowserBack') { e.preventDefault(); window.chrome.webview.postMessage('home'); }
    else if (e.key === ' ' || e.key === 'Enter' || e.key === 'MediaPlayPause') { var v = document.querySelector('video'); if (v) { e.preventDefault(); if (v.paused) { window.__kwPausedByUser = false; v.play(); } else { window.__kwPausedByUser = true; v.pause(); } } }
  }, true);
})();";

    public WebAppWindow(string url, string profileName, ForegroundKind kind, string? userAgent = null, bool videoOnly = false)
    {
        _url = url; _profile = profileName; Kind = kind; _userAgent = userAgent; _videoOnly = videoOnly;
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
            // Pages get the TV scripts and can talk to the app, so links stay on https too.
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeHttps && u.Scheme != "about") e.Cancel = true;
            };
            // F1 inside the page returns to the KwartoTV home screen.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.addEventListener('keydown', e => { if (e.key === 'F1') { e.preventDefault(); window.chrome.webview.postMessage('home'); } }, true);");
            // The on-screen keyboard for text fields. YouTube TV has its own keyboard, so skip it there.
            if (Kind != ForegroundKind.YouTube) await core.AddScriptToExecuteOnDocumentCreatedAsync(WebTextTarget.PageScript);
            if (_videoOnly) await core.AddScriptToExecuteOnDocumentCreatedAsync(VideoOnlyScript);
            // Netflix and other websites are made for a mouse: add TV-style remote navigation and make them bigger for the couch.
            if (Kind is ForegroundKind.Netflix or ForegroundKind.Web)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(TvWebScript.Script);
                _web.ZoomFactor = 1.25;
            }
            core.WebMessageReceived += (_, e) =>
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg == "home") Dispatcher.BeginInvoke(() => HomeRequested?.Invoke());
                else if (msg == "chup") Dispatcher.BeginInvoke(() => ChannelUp?.Invoke());
                else if (msg == "chdown") Dispatcher.BeginInvoke(() => ChannelDown?.Invoke());
                else if (msg?.StartsWith("tvfocus:") == true && _focusLogs++ < 40) AppPaths.LogLine("web highlight: " + msg[8..]);
                else if (msg?.StartsWith("textfocus") == true && Kind != ForegroundKind.YouTube)
                {
                    var kind = msg.Length > 10 ? msg[10..] : "";
                    Dispatcher.BeginInvoke(() => ShowKeyboard(kind));
                }
                else if (msg?.StartsWith("textstate:") == true)
                {
                    var state = msg[10..];
                    Dispatcher.BeginInvoke(() => _text?.Update(state));
                }
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

    /// <summary>Opens the on-screen keyboard for the page's focused field. [kind] is what the page says the field is.</summary>
    private void ShowKeyboard(string kind)
    {
        if (_osk is not null || !IsVisible) return;
        var text = new WebTextTarget(_web, kind);
        // Recent entries are kept per kind of field (web addresses share them with KwartoTV's own link boxes); never for passwords.
        string? purpose = kind switch { "password" => null, "search" => "web-search", "email" => "web-email", "url" => "link", _ => "web-text" };
        string? title = kind switch { "password" => "Password", "search" => "Search", "email" => "E-mail or username", "url" => "Web address", _ => null };
        var osk = new OskWindow(text, purpose, title) { Owner = this };
        _osk = osk; _text = text;
        osk.Closing_ += () => { _osk = null; _text = null; Dispatcher.BeginInvoke(() => { Activate(); _web.Focus(); }); };
        osk.Closed += (_, _) => { _osk = null; _text = null; };
        osk.Show();
        text.Refresh();
    }

    /// <summary>Give the page keyboard focus again after the window is activated.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_osk is null && _web.CoreWebView2 is not null) _web.Focus();
    }
}
