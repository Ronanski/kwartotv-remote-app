using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>
/// YouTube Music, playing in the background (a browser window kept off screen), controlled from the phone:
/// play/pause, next, previous, search and pick a song. It keeps playing behind the lock screen, which shows the song.
/// Uses its own saved profile ("music"), so a YouTube Premium sign-in there means no ads.
/// </summary>
public sealed class MusicService
{
    /// <summary>Injected into music.youtube.com: reports what is playing, presses the player's buttons, searches.</summary>
    private const string PageScript = @"
(function () {
  if (window.__kwMusic) return; window.__kwMusic = true;
  function post(o) { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) {} }
  function click(sel) { var b = document.querySelector(sel); if (b) { b.click(); return true; } return false; }
  function video() { return document.querySelector('video'); }
  var last = '';
  setInterval(function () {
    // 'Still listening?' must not stop the music behind the lock screen.
    click('ytmusic-you-there-renderer button, ytmusic-you-there-renderer tp-yt-paper-button');
    var v = video(), md = navigator.mediaSession && navigator.mediaSession.metadata;
    var art = md && md.artwork && md.artwork.length ? md.artwork[md.artwork.length - 1].src : '';
    var s = { type: 'now', title: md ? md.title || '' : '', artist: md ? md.artist || '' : '', art: art, playing: !!(v && !v.paused && !v.ended) };
    var k = JSON.stringify(s); if (k !== last) { last = k; post(s); }
  }, 1000);
  function texts(col) { try { return col.musicResponsiveListItemFlexColumnRenderer.text.runs.map(function (r) { return r.text; }).join(''); } catch (e) { return ''; } }
  function walk(o, out) {
    if (!o || typeof o !== 'object' || out.length >= 20) return;
    if (Array.isArray(o)) { for (var i = 0; i < o.length; i++) walk(o[i], out); return; }
    var r = o.musicResponsiveListItemRenderer;
    if (r) {
      var id = r.playlistItemData && r.playlistItemData.videoId;
      if (!id) { try { id = r.overlay.musicItemThumbnailOverlayRenderer.content.musicPlayButtonRenderer.playNavigationEndpoint.watchEndpoint.videoId; } catch (e) {} }
      var cols = r.flexColumns || [];
      var artist = cols.length > 1 ? texts(cols[1]).split(' • ')[0] : '';
      if (id) out.push({ id: id, t: cols.length ? texts(cols[0]) : '', a: artist });
      return;
    }
    for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) walk(o[k], out);
  }
  window.__kw = {
    toggle: function () { if (!click('ytmusic-player-bar #play-pause-button')) { var v = video(); if (v) { if (v.paused) v.play(); else v.pause(); } } },
    pause: function () { var v = video(); if (v && !v.paused) { if (!click('ytmusic-player-bar #play-pause-button')) v.pause(); } },
    next: function () { click('ytmusic-player-bar .next-button'); },
    prev: function () { click('ytmusic-player-bar .previous-button'); },
    search: function (q) {
      try {
        var cfg = window.ytcfg, key = cfg && cfg.get('INNERTUBE_API_KEY'), ctx = cfg && cfg.get('INNERTUBE_CONTEXT');
        // Songs only (the 'Songs' filter of the search page).
        fetch('/youtubei/v1/search?prettyPrint=false' + (key ? '&key=' + encodeURIComponent(key) : ''), {
          method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ context: ctx, query: q, params: 'EgWKAQIIAWoKEAkQBRAKEAMQBA==' })
        }).then(function (r) { return r.json(); })
          .then(function (j) { var out = []; walk(j, out); post({ type: 'results', q: q, items: out }); })
          .catch(function () { post({ type: 'results', q: q, items: [], error: true }); });
      } catch (e) { post({ type: 'results', q: q, items: [], error: true }); }
    }
  };
})();";

    private Window? _window;
    private WebView2? _web;
    private Task? _starting;
    private string? _pendingSearch;
    private MusicState _state = MusicState.Off;

    public MusicState State => _state;
    public bool IsOn => _window is not null;

    /// <summary>The song or the play state changed (on the UI thread).</summary>
    public event Action<MusicState>? Changed;
    /// <summary>Search results for the phones (query, songs, failed).</summary>
    public event Action<string, IReadOnlyList<MusicTrack>, bool>? Results;

    /// <summary>Starts the music player (the YouTube Music home page; nothing plays until a song is picked).</summary>
    public Task OpenAsync(string? url = null) => _starting ??= StartAsync(url ?? Music.Home);

    private async Task StartAsync(string url)
    {
        // A real window (the browser needs one) kept off screen, never in the taskbar, never taking the keyboard.
        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        _window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Width = 640, Height = 360, Left = -20000, Top = -20000, Background = Brushes.Black, Title = "KwartoTV Music", Content = _web,
        };
        _window.Show();
        try
        {
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile("webview-music"), options);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeHttps && u.Scheme != "about") e.Cancel = true;
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PageScript);
            core.WebMessageReceived += (_, e) => OnMessage(e.TryGetWebMessageAsString());
            core.NavigationCompleted += (_, _) =>
            {
                if (_pendingSearch is { } q) { _pendingSearch = null; Search(q); }
            };
            SetState(_state with { On = true });
            AppPaths.LogLine("music: started");
            core.Navigate(url);
        }
        catch (Exception ex)
        {
            AppPaths.LogLine("music: " + ex.Message);
            Stop();
            throw new UserMessageException("Music could not start on the TV (the Edge WebView2 runtime is missing).");
        }
    }

    private void OnMessage(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (Music.ParseNow(text) is { } now)
        {
            if (now == _state) return;
            if (now.Title != _state.Title || now.Playing != _state.Playing)
                AppPaths.LogLine($"music: {(now.Playing ? "playing" : "paused")} {now.Title} - {now.Artist}");
            SetState(now);
            return;
        }
        if (Music.ParseResults(text) is { } r)
        {
            AppPaths.LogLine($"music: search \"{r.Query}\": {r.Tracks.Count} songs{(r.Error ? " (failed)" : "")}");
            Results?.Invoke(r.Query, r.Tracks, r.Error);
        }
    }

    private void SetState(MusicState s) { _state = s; Changed?.Invoke(s); }

    private void Run(string js)
    {
        if (_web?.CoreWebView2 is { } core) _ = core.ExecuteScriptAsync("window.__kw && window.__kw." + js);
    }

    public void PlayPause() { if (_window is null) _ = OpenAsync(); else Run("toggle()"); }
    public void Next() => Run("next()");
    public void Previous() => Run("prev()");

    /// <summary>Pauses the music (a channel or video starts on the TV).</summary>
    public void Pause() { if (_state.Playing) Run("pause()"); }

    /// <summary>Plays one song; YouTube Music continues with similar songs.</summary>
    public async void Play(string id)
    {
        if (!Music.IsTrackId(id)) return;
        AppPaths.LogLine("music: play " + id);
        if (_window is null) { try { await OpenAsync(Music.WatchUrl(id)); } catch (Exception ex) { AppPaths.LogLine("music: " + ex.Message); } return; }
        if (_starting is { } t) { try { await t; } catch { return; } }
        _web?.CoreWebView2?.Navigate(Music.WatchUrl(id));
    }

    /// <summary>Searches songs; the results go to the phones (Results).</summary>
    public async void Search(string query)
    {
        if (!Music.IsQuery(query)) return;
        if (_window is null)
        {
            _pendingSearch = query;
            try { await OpenAsync(); } catch (Exception ex) { AppPaths.LogLine("music: " + ex.Message); Results?.Invoke(query, Array.Empty<MusicTrack>(), true); }
            return;
        }
        if (_web?.CoreWebView2 is null) { _pendingSearch = query; return; }
        Run("search(" + System.Text.Json.JsonSerializer.Serialize(query) + ")");
    }

    /// <summary>Stops the music and closes its browser.</summary>
    public void Stop()
    {
        var w = _window; var web = _web;
        _window = null; _web = null; _starting = null; _pendingSearch = null;
        try { web?.Dispose(); } catch { }
        try { w?.Close(); } catch { }
        if (w is not null) AppPaths.LogLine("music: stopped");
        SetState(MusicState.Off);
    }
}
