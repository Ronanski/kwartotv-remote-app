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
    private enum Mode { Live, Files }

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
            _main.LaunchWeb(new WebTile(ch.Id, ch.Name, ch.Url.StartsWith("https") ? ch.Url : "https://www.youtube.com/"));
            return;
        }
        _mode = Mode.Live; _failed = false; CloseGuide();
        Present();
        WhenReady(() =>
        {
            _osd.ShowMessage(null);
            _mpv!.SetHeaders(ch.Referrer, ch.UserAgent);
            _mpv.Load(ch.Url); _mpv.Paused = false;
            _loadedAt = DateTime.UtcNow;
            _osd.ShowBanner(ch, number, _tv.Lineup.IsFavorite(ch.Id));
        });
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
        if (_mpv is null || _mode == Mode.Live) return false;
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
            _osd.ShowMessage("Can't play this channel.\nPress ▲ or ▼ for another one.");
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

    // ---------- keys

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.OemPlus or Key.Add) { SystemVolume.Change(+5); e.Handled = true; return; }
        if (key is Key.OemMinus or Key.Subtract) { SystemVolume.Change(-5); e.Handled = true; return; }
        if (key is Key.M) { SystemVolume.ToggleMute(); e.Handled = true; return; }
        e.Handled = _mode == Mode.Live ? LiveKey(key) : FileKey(key);
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
                case Key.Enter or Key.Right: lineup.TuneNumber(_guideIndex + 1); return true;
                case Key.F: ToggleGuideFavorite(); return true;
                case Key.Escape or Key.BrowserBack or Key.Left: CloseGuide(); return true;
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
                if (lineup.Current is { } cur) { lineup.ToggleFavorite(cur.Id); _osd.ShowBanner(cur, lineup.CurrentNumber, lineup.IsFavorite(cur.Id)); }
                return true;
            case Key.I when lineup.Current is { } c2: _osd.ShowBanner(c2, lineup.CurrentNumber, lineup.IsFavorite(c2.Id)); return true;
            case Key.Escape or Key.BrowserBack or Key.MediaStop: _main.GoHome(); return true;
        }
        return false;
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

    // ---------- mini guide

    private void OpenGuide()
    {
        if (_tv.Lineup.Channels.Count == 0) return;
        _guideOpen = true;
        _guideIndex = Math.Max(0, _tv.Lineup.CurrentNumber - 1);
        DrawGuide();
    }

    private void CloseGuide() { _guideOpen = false; _osd.HideGuide(); }

    private void MoveGuide(int delta)
    {
        int n = _tv.Lineup.Channels.Count;
        if (n == 0) return;
        _guideIndex = Math.Abs(delta) == 1 ? (_guideIndex + delta + n) % n : Math.Clamp(_guideIndex + delta, 0, n - 1);
        DrawGuide();
    }

    private void DrawGuide()
    {
        var all = _tv.Lineup.Channels;
        const int rows = 11;
        int start = Math.Clamp(_guideIndex - rows / 2, 0, Math.Max(0, all.Count - rows));
        var slice = new List<(int, Channel, bool)>();
        for (int i = start; i < Math.Min(all.Count, start + rows); i++) slice.Add((i + 1, all[i], _tv.Lineup.IsFavorite(all[i].Id)));
        _osd.ShowGuide(slice, _guideIndex - start);
    }

    private void ToggleGuideFavorite()
    {
        var all = _tv.Lineup.Channels;
        if (_guideIndex < all.Count) { _tv.Lineup.ToggleFavorite(all[_guideIndex].Id); DrawGuide(); }
    }
}
