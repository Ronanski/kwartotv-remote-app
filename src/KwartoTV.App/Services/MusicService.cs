using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>
/// Music from the phone, played on the TV by YouTube Music's own player (a browser kept off screen with its own saved
/// profile; signed in there once = signed in for good, Premium: no ads). Songs are found with YouTube Music's search
/// (asked for directly); a picked song plays with its radio, so YouTube Music goes on by itself, and next / previous /
/// play-pause press its player's buttons. It plays on behind the lock screen, which shows the song.
/// </summary>
public sealed class MusicService
{
    /// <summary>The window is kept off screen: the browser must not treat it as hidden (it would hold the music back).</summary>
    public const string BackgroundArgs = "--disable-background-media-suspend --disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding --disable-background-timer-throttling --disable-features=CalculateNativeWinOcclusion";

    /// <summary>The switches of the YouTube profile (shared with YouTube on the TV).</summary>
    public static string YouTubeArgs(int renderMode) =>
        ("--autoplay-policy=no-user-gesture-required " + BackgroundArgs + " " + AppSettings.YouTubeBrowserArgs(renderMode)).Trim();

    /// <summary>The switches of the music profile (shared with the YouTube Music app on the TV screen).</summary>
    public const string MusicArgs = "--autoplay-policy=no-user-gesture-required " + BackgroundArgs;

    /// <summary>Injected into music.youtube.com: reports what is playing and presses the player's buttons.</summary>
    private const string PageScript = @"
(function () {
  if (window.__kwMusic) return; window.__kwMusic = true;
  function post(o) { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) {} }
  function click(sel) { var b = document.querySelector(sel); if (b) { b.click(); return true; } return false; }
  function video() { return document.querySelector('video'); }
  function textOf(sel) { var e = document.querySelector(sel); return e ? (e.innerText || e.textContent || '').trim() : ''; }
  var last = '', ticks = 0;
  setInterval(function () {
    // 'Still listening?' must not stop the music behind the lock screen.
    click('ytmusic-you-there-renderer button, ytmusic-you-there-renderer tp-yt-paper-button');
    var v = video(), md = navigator.mediaSession && navigator.mediaSession.metadata;
    var art = md && md.artwork && md.artwork.length ? md.artwork[md.artwork.length - 1].src : '';
    var barTitle = textOf('ytmusic-player-bar .title'), barBy = textOf('ytmusic-player-bar .byline').split(' \u2022 ')[0];
    var s = { type: 'now', title: (md && md.title) || barTitle, artist: (md && md.artist) || barBy, art: art, playing: !!(v && !v.paused && !v.ended && v.readyState > 2) };
    var k = JSON.stringify(s); if (k !== last) { last = k; post(s); }
    // A song page that does not start by itself is started (once per song; a pause from the phone is respected).
    if (v && v.paused && !v.ended && location.pathname === '/watch' && window.__kwStarted !== location.search && !window.__kwPaused) {
      window.__kwStarted = location.search; v.play().catch(function (e) { post({ type: 'diag', text: 'play() refused: ' + e }); });
    }
    if (++ticks % 10 === 0 && ticks <= 60) post({ type: 'diag', text: location.pathname + location.search.slice(0, 40) + ' video=' + (v ? (v.paused ? 'paused' : 'playing') + ' ready=' + v.readyState : 'none') + ' bar=' + barTitle + ' err=' + (textOf('yt-playability-error-supported-renderers') || textOf('tp-yt-paper-dialog') || '-').slice(0, 120) });
  }, 1000);
  window.__kw = {
    toggle: function () { var v = video(); window.__kwPaused = !!(v && !v.paused); if (!click('ytmusic-player-bar #play-pause-button')) { if (v) { if (v.paused) v.play(); else v.pause(); } } },
    pause: function () { var v = video(); if (v && !v.paused) { window.__kwPaused = true; if (!click('ytmusic-player-bar #play-pause-button')) v.pause(); } },
    next: function () { window.__kwPaused = false; click('ytmusic-player-bar .next-button'); },
    prev: function () { window.__kwPaused = false; click('ytmusic-player-bar .previous-button'); }
  };
})();";

    private static readonly HttpClient Http = MakeHttp();
    private static HttpClient MakeHttp()
    {
        var h = ResilientHttp.Create(TimeSpan.FromSeconds(20));
        h.DefaultRequestHeaders.UserAgent.Clear();
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.youtube.com/");
        return h;
    }

    private Window? _window;
    private WebView2? _web;
    private Task? _starting;
    private MusicState _state = MusicState.Off;
    private string? _lastId;
    private int _diags;

    public MusicState State => _state;
    public bool IsOn => _window is not null;

    /// <summary>The song or the play state changed (on the UI thread).</summary>
    public event Action<MusicState>? Changed;
    /// <summary>Search results for the phones (query, songs, failed).</summary>
    public event Action<string, IReadOnlyList<MusicTrack>, bool>? Results;

    /// <summary>Starts the player (nothing plays until a song is picked).</summary>
    public Task OpenAsync() => _starting ??= StartAsync();

    private async Task StartAsync()
    {
        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        _window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Width = 640, Height = 360, Left = -20000, Top = -20000, Background = Brushes.Black, Title = "KwartoTV Music", Content = _web,
        };
        _window.Show();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile("webview-music"), new CoreWebView2EnvironmentOptions(MusicArgs));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme != Uri.UriSchemeHttps && u.Scheme != "about") e.Cancel = true;
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebAppWindow.NoForcedColorsScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PageScript);
            core.WebMessageReceived += (_, e) => OnMessage(e.TryGetWebMessageAsString());
            _state = _state with { On = true };
            AppPaths.LogLine("music: started");
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
        if (text.Contains("\"diag\"") && _diags++ < 30)
        {
            try { AppPaths.LogLine("music page: " + System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("text").GetString()); } catch { }
        }
    }

    private void SetState(MusicState s) { _state = s; Changed?.Invoke(s); }

    private void Run(string js)
    {
        if (_web?.CoreWebView2 is { } core) _ = core.ExecuteScriptAsync("window.__kw && window.__kw." + js);
    }

    private static async Task<string?> Post(string url, string body)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var r = await Http.PostAsync(url, content);
            return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync() : null;
        }
        catch (Exception ex) { AppPaths.LogLine("music: " + ex.Message); return null; }
    }

    /// <summary>Searches songs with YouTube Music's own search; the results go to the phones (Results).</summary>
    public async void Search(string query)
    {
        if (!Music.IsQuery(query)) return;
        var (tracks, failed) = await SearchAsync(query);
        Results?.Invoke(query, tracks, failed);
    }

    /// <summary>YouTube Music's song search (for the phone and the TV's music screen).</summary>
    public static async Task<(IReadOnlyList<MusicTrack> Tracks, bool Failed)> SearchAsync(string query)
    {
        var json = await Post(YouTubeMusicApi.SearchUrl, YouTubeMusicApi.SearchBody(query));
        IReadOnlyList<MusicTrack> tracks = json is null ? Array.Empty<MusicTrack>() : YouTubeMusicApi.ParseSearch(json);
        AppPaths.LogLine($"music: search \"{query}\": {tracks.Count} songs{(json is null ? " (failed)" : "")}");
        return (tracks, json is null);
    }

    /// <summary>The rows of YouTube Music's home (Quick picks, charts...), for the TV's music screen and the phone.</summary>
    public static async Task<IReadOnlyList<MusicRow>> HomeAsync()
    {
        var json = await Post(YouTubeMusicApi.BrowseUrl, YouTubeMusicApi.HomeBody());
        var rows = json is null ? Array.Empty<MusicRow>() : YouTubeMusicApi.ParseHome(json);
        AppPaths.LogLine($"music: home {rows.Count} rows");
        return rows;
    }

    /// <summary>Plays one song with its radio (YouTube Music goes on with similar songs by itself).</summary>
    public async void Play(string id)
    {
        if (!Music.IsTrackId(id)) return;
        _lastId = id;
        AppPaths.LogLine("music: play " + id);
        try { await OpenAsync(); } catch (Exception ex) { AppPaths.LogLine("music: " + ex.Message); return; }
        if (_web?.CoreWebView2 is { } core)
        {
            await core.ExecuteScriptAsync("window.__kwPaused = false;");
            core.Navigate(Music.WatchUrl(id) + "&list=RDAMVM" + id);
        }
    }

    public void PlayPause()
    {
        if (_window is null) { if (_lastId is { } id) Play(id); return; }
        Run("toggle()");
    }

    public void Next() => Run("next()");
    public void Previous() => Run("prev()");

    /// <summary>Pauses the music (a channel or video starts on the TV).</summary>
    public void Pause() { if (_state.Playing) Run("pause()"); }

    /// <summary>Stops the music and closes its browser.</summary>
    public void Stop()
    {
        var w = _window; var web = _web;
        _window = null; _web = null; _starting = null;
        try { web?.Dispose(); } catch { }
        try { w?.Close(); } catch { }
        if (w is not null) AppPaths.LogLine("music: stopped");
        SetState(MusicState.Off);
    }
}
