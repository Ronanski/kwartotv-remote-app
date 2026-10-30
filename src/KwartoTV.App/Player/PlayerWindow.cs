using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KwartoTV.App.Services;
using KwartoTV.Core;

namespace KwartoTV.App.Player;

/// <summary>Fullscreen libmpv player for Live TV (cable-style controls) and Library files.</summary>
public sealed class PlayerWindow : Window
{
    private enum Mode { Live, Files, Preview }

    // Some stream servers refuse one identity and accept another, so a failed channel is retried with the next one.
    private static readonly string?[] UserAgents = { null, "VLC/3.0.20 LibVLC/3.0.20", "libmpv" };
    private Channel? _channel;
    private int _attempt;
    private string? _problem;
    private DateTime _streamStarted;
    private bool _startedLogged;
    private bool _healthMarked;

    private readonly TvServices _tv;
    private readonly MainWindow _main;
    private readonly MpvHost _host = new();
    private readonly OsdWindow _osd = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private MpvPlayer? _mpv;
    private Action? _afterReady;
    private int _initTries;

    private Mode _mode;
    private DateTime _loadedAt;
    private bool _failed;
    private bool _guideOpen;
    private int _guideIndex;

    private IReadOnlyList<LibraryItem> _files = Array.Empty<LibraryItem>();
    private int _fileIndex;

    public PlayerWindow(TvServices tv, MainWindow main)
    {
        _tv = tv; _main = main;
        Owner = main;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        Background = Brushes.Black; Cursor = Cursors.None; ShowInTaskbar = false; Title = "KwartoTV Player";
        Content = _host;
        _poll.Tick += (_, _) => Poll();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _osd.Owner ??= this; SyncOsd(); _osd.Show(); _poll.Start(); }
            else { _poll.Stop(); _osd.Hide(); _osd.HideAll(); _mpv?.Stop(); _guideOpen = false; }
        };
        SizeChanged += (_, _) => SyncOsd();
        LocationChanged += (_, _) => SyncOsd();
        Closed += (_, _) => { _poll.Stop(); _mpv?.Dispose(); _osd.Close(); };
    }

    private void SyncOsd()
    {
        _osd.Left = 0; _osd.Top = 0;
        _osd.Width = SystemParameters.PrimaryScreenWidth; _osd.Height = SystemParameters.PrimaryScreenHeight;
    }

    // ---------- starting playback

    private void WhenReady(Action act)
    {
        if (_mpv is not null) { act(); return; }
        _afterReady = act;
        TryInit();
    }

    private void TryInit()
    {
        if (_mpv is not null) return;
        if (_host.Handle == IntPtr.Zero)
        {
            if (++_initTries > 60) { _osd.ShowMessage("Could not start the video engine."); return; }
            Dispatcher.BeginInvoke(DispatcherPriority.Background, async () => { await Task.Delay(50); TryInit(); });
            return;
        }
        try { _mpv = MpvPlayer.Create(_host.Handle); }
        catch (Exception ex) { AppPaths.LogLine("mpv: " + ex); _osd.ShowMessage("Could not start the video engine (libmpv)."); return; }
        var a = _afterReady; _afterReady = null; a?.Invoke();
    }

    private void Present()
    {
        if (!IsVisible) Show();
        Activate(); Focus();
        Native.BringToFront(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    public void PlayLive(Channel ch, int number)
    {
        if (ch.Kind == StreamKind.YouTube)
        {
            Hide();
            _main.PlayYouTubeChannel(ch);
            return;
        }
        _mode = Mode.Live; _failed = false; CloseGuide();
        Present();
        WhenReady(() =>
        {
            StartStream(ch, 0);
            ShowLiveBanner(ch, number);
        });
    }

    /// <summary>Plays a channel that is not in the lineup yet, so the user can check it before choosing it. Back returns to the picker.</summary>
    public void PlayPreview(Channel ch)
    {
        _mode = Mode.Preview; _failed = false; CloseGuide();
        Present();
        WhenReady(() =>
        {
            StartStream(ch, 0);
            _osd.ShowBanner(ch, 0, false);
        });
    }

    private void StartStream(Channel ch, int attempt)
    {
        _channel = ch; _attempt = attempt; _problem = null; _failed = false; _startedLogged = false;
        if (attempt == 0) _healthMarked = false;
        _osd.ShowMessage(attempt == 0 ? null : "Trying another way to connect…");
        var ua = ch.UserAgent ?? UserAgents[Math.Min(attempt, UserAgents.Length - 1)];
        _mpv!.SetHeaders(ch.Referrer, ua);
        _mpv.SetSubtitles(false);
        _mpv.Load(ch.Url); _mpv.Paused = false;
        _loadedAt = _streamStarted = DateTime.UtcNow;
        AppPaths.LogLine($"player: opening {ch.Name} (attempt {attempt + 1}) {ch.Url}");
    }

    public void PlayFiles(IReadOnlyList<LibraryItem> items, int index)
    {
        _mode = Mode.Files; _files = items; _fileIndex = index; _failed = false;
        Present();
        WhenReady(() => LoadFile());
    }

    private void LoadFile()
    {
        var item = _files[_fileIndex];
        _osd.ShowMessage(null);
        _mpv!.SetHeaders(null, null);
        _mpv.SetSubtitles(true);
        _mpv.Load(item.Path); _mpv.Paused = false;
        _loadedAt = DateTime.UtcNow; _failed = false;
        _osd.ShowProgress(item.Title, 0, 0, false, false);
    }

    // ---------- controls used by the remote (through MainWindow)

    public bool TogglePause()
    {
        if (_mpv is null) return false;
        _mpv.Paused = !_mpv.Paused;
        ShowStatus();
        return true;
    }

    public bool Seek(int seconds)
    {
        if (_mpv is null || _mode != Mode.Files) return false;
        _mpv.Seek(seconds); ShowStatus();
        return true;
    }

    public bool SetSpeed(double speed) { _mpv?.SetSpeed(speed); return _mpv is not null; }

    private void ShowStatus()
    {
        if (_mpv is null) return;
        if (_mode == Mode.Files) _osd.ShowProgress(_files[_fileIndex].Title, _mpv.Position, _mpv.Duration, _mpv.Paused, _mpv.Paused);
        else if (_mpv.Paused) _osd.ShowMessage("⏸  Paused"); else _osd.ShowMessage(null);
    }

    // ---------- timers

    private void Poll()
    {
        if (_mpv is null) return;
        _osd.SetDigits(_tv.Lineup.DigitBuffer);

        // Like cable: when a new programme starts on the channel being watched, say what it is.
        if (_mode == Mode.Live && !_guideOpen && _tv.Lineup.Current is { } watching)
        {
            var onNow = _tv.OnAir(watching).Now;
            if (onNow != _lastOnNow) { if (_lastOnNow is not null && onNow is not null) ShowLiveBanner(watching, _tv.Lineup.CurrentNumber); _lastOnNow = onNow; }
        }

        if (_mode != Mode.Files && _channel is not null)
        {
            var ev = _mpv.TakeEvents();
            if (ev.Loaded && !_startedLogged)
            {
                _startedLogged = true;
                AppPaths.LogLine($"player: started {_channel.Name} after {(DateTime.UtcNow - _streamStarted).TotalSeconds:0.0}s (attempt {_attempt + 1})");
            }
            if (ev.Problem is not null)
            {
                _problem = ev.Problem;
                if (_attempt < UserAgents.Length - 1) { StartStream(_channel, _attempt + 1); return; }
            }
            // Opened but nothing moves (no picture or sound) after a while: treat it as not working.
            var since = DateTime.UtcNow - _streamStarted;
            if (_startedLogged && !_healthMarked && since > TimeSpan.FromSeconds(4) && _mpv.Position > 1.5)
            { _healthMarked = true; _tv.MarkHealth(_channel, HealthStatus.Ok, null); }
            if (_startedLogged && !_healthMarked && !_failed && since > TimeSpan.FromSeconds(18) && _mpv.Position < 0.5 && !_mpv.Paused)
            {
                _problem = "no picture or sound";
                _failed = true; _healthMarked = true;
                _tv.MarkHealth(_channel, HealthStatus.Dead, _problem);
                _osd.ShowMessage(_mode == Mode.Preview ? "Can't play this stream.\nNo picture or sound.\nPress Back to return." : "Can't play this channel.\nNo picture or sound. It is hidden from your list now.\nPress ▲ or ▼ for another one.");
                return;
            }
        }

        var age = DateTime.UtcNow - _loadedAt;
        if (!_failed && age > TimeSpan.FromSeconds(2) && _mpv.Idle)
        {
            if (_mode == Mode.Files)
            {
                // Finished (or failed): move on to the next file, or leave.
                if (_fileIndex + 1 < _files.Count) { _fileIndex++; LoadFile(); }
                else _main.GoHome();
                return;
            }
            _failed = true;
            if (_channel is not null && !_healthMarked) { _healthMarked = true; _tv.MarkHealth(_channel, HealthStatus.Dead, _problem ?? "cannot be opened"); }
            // "loading failed" alone says little: the usual causes are an offline address, a country block, or a login requirement.
            var why = string.IsNullOrEmpty(_problem) ? "" : "\n" + (_problem.StartsWith("loading failed") ? _problem + "\nThe stream may be offline, blocked in your country, or need a login." : _problem);
            _osd.ShowMessage(_mode == Mode.Preview
                ? "Can't play this stream." + why + "\nPress Back to return."
                : "Can't play this channel." + why + "\nIt is hidden from your list now. Press ▲ or ▼ for another one.");
            return;
        }
        if (_failed && !_mpv.Idle) { _failed = false; _osd.ShowMessage(null); }

        if (!_failed)
        {
            if (_mpv.Buffering) _osd.ShowMessage("Buffering…");
            else if (!_mpv.Paused) _osd.ShowMessage(null);
        }
        if (_mode == Mode.Files && _osd.ProgressVisible && !_failed) ShowStatus();
    }

    private string? _lastOnNow;

    private void ShowLiveBanner(Channel ch, int number)
    {
        var (now, next) = _tv.OnAir(ch);
        _lastOnNow = now;
        _osd.ShowBanner(ch, number, _tv.Lineup.IsFavorite(ch.Id), now, next);
    }

    // ---------- keys

    // Only five keys are needed (arrows and OK): a short OK acts when it is let go, holding OK for half a second is Back.
    private static readonly TimeSpan HoldForBack = TimeSpan.FromMilliseconds(500);
    private DateTime _okDownAt;
    private bool _okDown, _okHeldDone;

    private bool Back() => _mode switch { Mode.Live => LiveKey(Key.Escape), Mode.Preview => PreviewKey(Key.Escape), _ => FileKey(Key.Escape) };

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key != Key.Enter || !_okDown) return;
        _okDown = false;
        e.Handled = true;
        if (_okHeldDone) return;
        AppPaths.LogLine($"player key: Enter (guide {(_guideOpen ? "open" : "closed")})");
        _ = _mode switch { Mode.Live => LiveKey(Key.Enter), Mode.Preview => PreviewKey(Key.Enter), _ => FileKey(Key.Enter) };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat) { _okDown = true; _okHeldDone = false; _okDownAt = DateTime.UtcNow; }
            else if (_okDown && !_okHeldDone && DateTime.UtcNow - _okDownAt >= HoldForBack) { _okHeldDone = true; Back(); }
            return;
        }
        if (key is Key.Up or Key.Down) AppPaths.LogLine($"player key: {key} (guide {(_guideOpen ? "open" : "closed")})");
        if (key is Key.OemPlus or Key.Add) { SystemVolume.Change(+5); e.Handled = true; return; }
        if (key is Key.OemMinus or Key.Subtract) { SystemVolume.Change(-5); e.Handled = true; return; }
        if (key is Key.M) { SystemVolume.ToggleMute(); e.Handled = true; return; }
        e.Handled = _mode switch { Mode.Live => LiveKey(key), Mode.Preview => PreviewKey(key), _ => FileKey(key) };
    }

    private bool LiveKey(Key key)
    {
        var lineup = _tv.Lineup;
        if (key >= Key.D0 && key <= Key.D9) { lineup.PressDigit(key - Key.D0, DateTimeOffset.UtcNow); return true; }
        if (key >= Key.NumPad0 && key <= Key.NumPad9) { lineup.PressDigit(key - Key.NumPad0, DateTimeOffset.UtcNow); return true; }

        if (_guideOpen)
        {
            switch (key)
            {
                case Key.Up: MoveGuide(-1); return true;
                case Key.Down: MoveGuide(1); return true;
                case Key.PageUp: MoveGuide(-8); return true;
                case Key.PageDown: MoveGuide(8); return true;
                case Key.Enter: if (_guideIndex < _guideList.Count) lineup.TuneNumber(_guideList[_guideIndex].Number); return true;
                case Key.Left: SwitchGroup(-1); return true;
                case Key.Right: SwitchGroup(+1); return true;
                case Key.F: ToggleGuideFavorite(); return true;
                case Key.Escape or Key.BrowserBack or Key.G: CloseGuide(); return true;
            }
            return true;
        }

        switch (key)
        {
            case Key.Up or Key.MediaNextTrack or Key.PageUp: lineup.Next(); return true;
            case Key.Down or Key.MediaPreviousTrack or Key.PageDown: lineup.Previous(); return true;
            case Key.Back or Key.L or Key.Tab: lineup.Last(); return true;
            case Key.Enter or Key.Left or Key.G or Key.Right: OpenGuide(); return true;
            case Key.Space or Key.MediaPlayPause or Key.P: TogglePause(); return true;
            case Key.F:
                if (lineup.Current is { } cur) { lineup.ToggleFavorite(cur.Id); ShowLiveBanner(cur, lineup.CurrentNumber); }
                return true;
            case Key.I when lineup.Current is { } c2: ShowLiveBanner(c2, lineup.CurrentNumber); return true;
            case Key.Escape or Key.BrowserBack or Key.MediaStop: _main.GoHome(); return true;
        }
        return false;
    }

    private bool PreviewKey(Key key)
    {
        switch (key)
        {
            case Key.Enter or Key.Space or Key.MediaPlayPause or Key.P: TogglePause(); return true;
            case Key.Escape or Key.BrowserBack or Key.MediaStop or Key.Back: _main.ClosePlayer(); return true;
        }
        return true;
    }

    private bool FileKey(Key key)
    {
        switch (key)
        {
            case Key.Left: Seek(-10); return true;
            case Key.Right: Seek(10); return true;
            case Key.Up: Seek(60); return true;
            case Key.Down: Seek(-60); return true;
            case Key.Enter or Key.Space or Key.MediaPlayPause or Key.P: TogglePause(); return true;
            case Key.MediaNextTrack or Key.PageDown when _fileIndex + 1 < _files.Count: _fileIndex++; LoadFile(); return true;
            case Key.MediaPreviousTrack or Key.PageUp when _fileIndex > 0: _fileIndex--; LoadFile(); return true;
            case Key.I: ShowStatus(); return true;
            case Key.Escape or Key.BrowserBack or Key.MediaStop: _main.GoHome(); return true;
        }
        return false;
    }

    // ---------- channel list (the guide): grouped by category, switch with left/right

    private List<string> _guideGroups = new();
    private Dictionary<string, HashSet<string>> _spaceMembers = new();
    private int _guideGroup;
    private List<(int Number, Channel Channel)> _guideList = new();

    private const string AllGroup = "All channels", FavoritesGroup = "★ Favorites";

    /// <summary>Opens (or closes) the channel list. Used by the OK key and by the phone's "Channel list" button.</summary>
    public void ToggleGuide() { if (_guideOpen) CloseGuide(); else OpenGuide(); }

    public void OpenGuide() => OpenGuide(null);

    /// <summary>Opens the channel list, optionally on a given category (for example a playlist's own space).</summary>
    public void OpenGuide(string? category)
    {
        if (_mode != Mode.Live || _tv.Lineup.Channels.Count == 0) return;
        _tv.RebuildLineup();   // channels found not to work since the last time drop out of the list
        _guideGroups = new List<string> { AllGroup };
        if (_tv.Lineup.Favorites.Count > 0) _guideGroups.Add(FavoritesGroup);
        _spaceMembers = _tv.Spaces().ToDictionary(s => "📺 " + s.Playlist.Name, s => s.Channels.Select(c => c.Channel.Id).ToHashSet());
        _guideGroups.AddRange(_spaceMembers.Keys);
        _guideGroups.AddRange(_tv.Lineup.Groups);
        // Start in the category of the channel being watched, so "where am I" is obvious.
        var cur = _tv.Lineup.Current;
        _guideGroup = category is not null && _guideGroups.IndexOf(category) is >= 0 and var gi ? gi : 0;
        _guideOpen = true;
        AppPaths.LogLine($"guide: open ({_guideGroups.Count} categories)");
        BuildGuideList();
        _guideIndex = Math.Max(0, _guideList.FindIndex(x => x.Number == _tv.Lineup.CurrentNumber));
        DrawGuide();
    }

    private void CloseGuide() { _guideOpen = false; _osd.HideGuide(); }

    private void BuildGuideList()
    {
        var all = _tv.Lineup.Channels;
        var name = _guideGroups[_guideGroup];
        var list = new List<(int, Channel)>();
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (name == AllGroup || (name == FavoritesGroup && _tv.Lineup.IsFavorite(c.Id))
                || (_spaceMembers.TryGetValue(name, out var members) ? members.Contains(c.Id) : c.Group == name)) list.Add((i + 1, c));
        }
        _guideList = list;
    }

    private void MoveGuide(int delta)
    {
        int n = _guideList.Count;
        if (n == 0) return;
        _guideIndex = Math.Abs(delta) == 1 ? (_guideIndex + delta + n) % n : Math.Clamp(_guideIndex + delta, 0, n - 1);
        DrawGuide();
    }

    private void SwitchGroup(int delta)
    {
        if (_guideGroups.Count == 0) return;
        _guideGroup = (_guideGroup + delta + _guideGroups.Count) % _guideGroups.Count;
        BuildGuideList();
        _guideIndex = Math.Max(0, _guideList.FindIndex(x => x.Number == _tv.Lineup.CurrentNumber));
        DrawGuide();
    }

    private void DrawGuide()
    {
        const int rows = 11;
        int n = _guideList.Count;
        int start = Math.Clamp(_guideIndex - rows / 2, 0, Math.Max(0, n - rows));
        var slice = new List<(int, Channel, bool, string?)>();
        for (int i = start; i < Math.Min(n, start + rows); i++)
        {
            var ch = _guideList[i].Channel;
            slice.Add((_guideList[i].Number, ch, _tv.Lineup.IsFavorite(ch.Id), _tv.OnAir(ch).Now));
        }
        var title = _guideGroups[_guideGroup];
        _osd.ShowGuide(slice, _guideIndex - start, n == 0 ? title + "  (empty)" : title,
            $"{(n == 0 ? 0 : _guideIndex + 1)} of {n}   ·   category {_guideGroup + 1}/{_guideGroups.Count}");
    }

    private void ToggleGuideFavorite()
    {
        if (_guideIndex < _guideList.Count) { _tv.Lineup.ToggleFavorite(_guideList[_guideIndex].Channel.Id); DrawGuide(); }
    }
}
