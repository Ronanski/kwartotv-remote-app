using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>
/// Music from the phone, played on the TV through its YouTube app (a browser kept off screen with the same saved
/// profile as YouTube on the TV): signed in to YouTube there means signed in here (Premium: no ads). Songs are found
/// with YouTube Music's own search; KwartoTV keeps the queue (YouTube Music's "up next" radio) for next / previous and
/// goes on by itself at the end of a song. It plays on behind the lock screen, which shows the song.
/// </summary>
public sealed class MusicService
{
    /// <summary>The window is kept off screen: the browser must not treat it as hidden (it would hold the music back).</summary>
    public const string BackgroundArgs = "--disable-background-media-suspend --disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding --disable-background-timer-throttling --disable-features=CalculateNativeWinOcclusion";

    /// <summary>The switches of the YouTube profile (shared with YouTube on the TV, so both must use the same ones).</summary>
    public static string YouTubeArgs(int renderMode) =>
        ("--autoplay-policy=no-user-gesture-required " + BackgroundArgs + " " + AppSettings.YouTubeBrowserArgs(renderMode)).Trim();

    /// <summary>Injected into YouTube's TV page: reports play state and the end of a song, and plays/pauses.</summary>
    private const string PageScript = @"
(function () {
  if (window.__kwMusic) return; window.__kwMusic = true;
  function post(o) { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) {} }
  function video() { return document.querySelector('video'); }
  var last = '', endedFor = '';
  setInterval(function () {
    var v = video();
    var s = { type: 'state', playing: !!(v && !v.paused && !v.ended && v.readyState > 2), ready: v ? v.readyState : -1 };
    var k = JSON.stringify(s); if (k !== last) { last = k; post(s); }
    // A song page that does not start by itself is started (a pause from the phone is respected).
    if (v && v.paused && !v.ended && !window.__kwPaused && v.readyState > 1) v.play().catch(function () {});
    // The end of the song: KwartoTV plays the next one of its queue.
    if (v && v.duration > 0 && (v.ended || v.currentTime >= v.duration - 0.6) && endedFor !== location.hash) { endedFor = location.hash; post({ type: 'ended' }); }
  }, 1000);
  window.__kw = {
    toggle: function () { var v = video(); if (!v) return; if (v.paused) { window.__kwPaused = false; v.play(); } else { window.__kwPaused = true; v.pause(); } },
    pause: function () { var v = video(); if (v && !v.paused) { window.__kwPaused = true; v.pause(); } },
    restart: function () { var v = video(); if (v) v.currentTime = 0; }
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
    private readonly List<MusicTrack> _queue = new();
    private int _index = -1;
    private bool _playing;

    public MusicState State => _state;
    public bool IsOn => _window is not null;

    /// <summary>The song or the play state changed (on the UI thread).</summary>
    public event Action<MusicState>? Changed;
    /// <summary>Search results for the phones (query, songs, failed).</summary>
    public event Action<string, IReadOnlyList<MusicTrack>, bool>? Results;

    private MusicTrack? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

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
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile("webview-youtube"), new CoreWebView2EnvironmentOptions(YouTubeArgs(WebAppWindow.YouTubeRenderMode)));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.UserAgent = WebAppWindow.TvUserAgent;   // YouTube's TV app, with its sign-in
            core.NewWindowRequested += (_, e) => e.Handled = true;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebAppWindow.NoForcedColorsScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PageScript);
            core.WebMessageReceived += (_, e) => OnMessage(e.TryGetWebMessageAsString());
            _state = _state with { On = true };
            AppPaths.LogLine("music: started (YouTube on the TV)");
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
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(text);
            var type = d.RootElement.GetProperty("type").GetString();
            if (type == "state")
            {
                _playing = d.RootElement.TryGetProperty("playing", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.True;
                Publish();
            }
            else if (type == "ended") { AppPaths.LogLine("music: song ended"); _ = NextAsync(); }
        }
        catch { }
    }

    private void Publish()
    {
        var c = Current;
        var s = new MusicState(IsOn, _playing, c?.Title ?? "", c?.Artist ?? "", null);
        if (s == _state) return;
        if (s.Title != _state.Title || s.Playing != _state.Playing) AppPaths.LogLine($"music: {(s.Playing ? "playing" : "paused")} {s.Title} - {s.Artist}");
        SetState(s);
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
        var json = await Post(YouTubeMusicApi.SearchUrl, YouTubeMusicApi.SearchBody(query));
        IReadOnlyList<MusicTrack> tracks = json is null ? Array.Empty<MusicTrack>() : YouTubeMusicApi.ParseSearch(json);
        AppPaths.LogLine($"music: search \"{query}\": {tracks.Count} songs{(json is null ? " (failed)" : "")}");
        Results?.Invoke(query, tracks, json is null);
    }

    /// <summary>Plays one song; the queue goes on with YouTube Music's radio for it.</summary>
    public async void Play(string id)
    {
        if (!Music.IsTrackId(id)) return;
        AppPaths.LogLine("music: play " + id);
        var json = await Post(YouTubeMusicApi.NextUrl, YouTubeMusicApi.NextBody(id));
        IReadOnlyList<MusicTrack> radio = json is null ? Array.Empty<MusicTrack>() : YouTubeMusicApi.ParseNext(json);
        _queue.Clear();
        _queue.Add(radio.FirstOrDefault(t => t.Id == id) ?? new MusicTrack(id, "", ""));
        _queue.AddRange(radio.Where(t => _queue.All(q => q.Id != t.Id)));
        _index = 0;
        AppPaths.LogLine($"music: queue of {_queue.Count}");
        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        if (Current is not { } c) return;
        try { await OpenAsync(); } catch (Exception ex) { AppPaths.LogLine("music: " + ex.Message); return; }
        _playing = false; Publish();
        Run("pause()");
        if (_web?.CoreWebView2 is { } core)
        {
            await core.ExecuteScriptAsync("window.__kwPaused = false;");
            core.Navigate(YouTubeMusicApi.TvWatchUrl(c.Id));
        }
    }

    public void PlayPause()
    {
        if (Current is null) return;
        if (_window is null) { _ = LoadCurrentAsync(); return; }
        Run("toggle()");
    }

    public void Next() => _ = NextAsync();

    private async Task NextAsync()
    {
        if (Current is not { } c) return;
        if (_index + 1 >= _queue.Count)
        {
            // End of the queue: more of the same radio, starting from this song.
            var json = await Post(YouTubeMusicApi.NextUrl, YouTubeMusicApi.NextBody(c.Id));
            IReadOnlyList<MusicTrack> more = json is null ? Array.Empty<MusicTrack>() : YouTubeMusicApi.ParseNext(json);
            _queue.AddRange(more.Where(t => _queue.All(q => q.Id != t.Id)));
            if (_index + 1 >= _queue.Count) return;
        }
        _index++;
        await LoadCurrentAsync();
    }

    public void Previous()
    {
        if (Current is null) return;
        if (_index == 0) { Run("restart()"); return; }
        _index--;
        _ = LoadCurrentAsync();
    }

    /// <summary>Pauses the music (a channel or video starts on the TV).</summary>
    public void Pause() { if (_state.Playing) Run("pause()"); }

    /// <summary>Stops the music and closes its browser.</summary>
    public void Stop()
    {
        var w = _window; var web = _web;
        _window = null; _web = null; _starting = null; _playing = false;
        try { web?.Dispose(); } catch { }
        try { w?.Close(); } catch { }
        if (w is not null) AppPaths.LogLine("music: stopped");
        SetState(MusicState.Off);
    }
}
