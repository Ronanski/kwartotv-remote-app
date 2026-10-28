using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KwartoTV.App.Player;
using KwartoTV.App.Services;
using KwartoTV.App.Ui;
using KwartoTV.App.Views;
using KwartoTV.Core;

namespace KwartoTV.App;

public partial class MainWindow : Window, IShell
{
    private readonly WindowsHostActions _host = new();
    private readonly TvServices _tv;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _digits = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<Button> _navButtons = new();
    private PlayerWindow? _player;
    private WebAppWindow? _web;
    private IInputElement? _focusBeforeDialog;

    private PageBase? _current;
    private string _currentName = "Home";

    public MainWindow()
    {
        InitializeComponent();
        _tv = new TvServices(_host);
        _host.Attach(this);

        foreach (var (label, action) in new (string, Action)[] { ("Home", GoHome), ("Live TV", ShowLiveTv), ("Library", ShowLibrary), ("Settings", ShowSettings) })
        {
            var b = new Button { Content = new TextBlock { Text = label }, Style = (Style)FindResource("Pill") };
            b.Click += (_, _) => action();
            _navButtons.Add(b); Nav.Children.Add(b);
        }

        _clock.Tick += (_, _) => UpdateClock(); UpdateClock(); _clock.Start();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _digits.Tick += (_, _) => _tv.Lineup.Tick(DateTimeOffset.UtcNow);
        _digits.Start();

        _tv.Lineup.Tuned += OnTuned;
        _tv.ChannelsChanged += () => Dispatcher.BeginInvoke(() => _current?.Refresh());
        _tv.Apps.Exited += () => Dispatcher.BeginInvoke(() => { if (_player is not { IsVisible: true }) Native.BringToFront(Handle); });

        Loaded += async (_, _) =>
        {
            Navigate("Home");
            await _tv.StartRemoteAsync();
            RemoteDot.Foreground = _tv.Server is null ? (System.Windows.Media.Brush)FindResource("Danger") : (System.Windows.Media.Brush)FindResource("Accent");
            _current?.Refresh();
            // First run: nothing is paired yet, so show the pairing code straight away (no need to find Settings).
            if (_tv.Server is not null && _tv.Pairing.Devices.Count == 0) Dialogs.Pair(this);
            await _tv.RefreshChannelsAsync();
        };
        Closed += (_, _) => { CloseWeb(); _player?.Close(); _tv.Apps.Close(); _tv.Dispose(); };
    }

    public TvServices Tv => _tv;
    public IntPtr Handle => new WindowInteropHelper(this).Handle;
    IntPtr IShell.WindowHandle => Handle;
    public ForegroundKind ForegroundApp =>
        _player is { IsVisible: true } ? ForegroundKind.KwartoTV
        : _web is { IsVisible: true } ? _web.Kind
        : _tv.Apps.IsRunning ? _tv.Apps.Kind : ForegroundKind.KwartoTV;

    private void UpdateClock() => Clock.Text = DateTime.Now.ToString("h:mm tt").ToLowerInvariant();

    // ---------- navigation

    private void Navigate(string name)
    {
        _currentName = name;
        _current = name switch
        {
            "Library" => new LibraryView(this),
            "Settings" => new SettingsView(this),
            _ => new HomeView(this),
        };
        Page.Content = _current;
        for (int i = 0; i < _navButtons.Count; i++)
        {
            var on = i == 0 ? name == "Home" : (i == 2 && name == "Library") || (i == 3 && name == "Settings");
            ((TextBlock)_navButtons[i].Content).FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            ((TextBlock)_navButtons[i].Content).TextDecorations = on ? TextDecorations.Underline : null;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _current?.FocusFirst());
    }

    public void GoHome()
    {
        CloseDialog();
        _player?.Hide();
        CloseWeb();
        _tv.Apps.Close();
        if (_currentName != "Home") Navigate("Home"); else _current?.Refresh();
        Native.BringToFront(Handle);
        Activate();
    }

    public void ShowLibrary() { CloseWeb(); _tv.Apps.Close(); _player?.Hide(); Navigate("Library"); Native.BringToFront(Handle); }
    public void ShowSettings() { CloseWeb(); _tv.Apps.Close(); _player?.Hide(); Navigate("Settings"); Native.BringToFront(Handle); }

    public void ShowLiveTv()
    {
        if (_tv.Lineup.Channels.Count == 0) { ShowToast("No channels yet. Add a playlist in Settings."); ShowSettings(); return; }
        CloseWeb(); _tv.Apps.Close();
        var ch = _tv.Lineup.Current ?? _tv.Lineup.TuneNumber(1);
        if (ch is null) return;
        if (_tv.Lineup.Current is not null) OpenPlayerLive(ch);
    }

    private void OnTuned(Channel ch, int number) => Dispatcher.BeginInvoke(() => { CloseWeb(); _tv.Apps.Close(); OpenPlayerLive(ch); });

    private void OpenPlayerLive(Channel ch)
    {
        if (!MpvPlayer.IsAvailable()) { ShowToast("Video engine (libmpv-2.dll) is missing. Reinstall KwartoTV."); return; }
        _player ??= new PlayerWindow(_tv, this);
        _player.PlayLive(ch, _tv.Lineup.CurrentNumber);
    }

    public void PlayFiles(IReadOnlyList<LibraryItem> items, int index)
    {
        if (!MpvPlayer.IsAvailable()) { ShowToast("Video engine (libmpv-2.dll) is missing. Reinstall KwartoTV."); return; }
        CloseWeb(); _tv.Apps.Close();
        _player ??= new PlayerWindow(_tv, this);
        _player.PlayFiles(items, index);
    }

    public void LaunchYouTube()
    {
        // In-app, with a TV browser identity so the sign-in is the phone code flow and the phone's cast button works.
        OpenWeb(AppLaunchPlans.YouTubeTvUrl, "youtube", ForegroundKind.YouTube, WebAppWindow.TvUserAgent, "YouTube",
            () => _tv.Apps.LaunchYouTube());
    }

    public async void LaunchNetflix()
    {
        PrepareExternal();
        ShowToast("Opening Netflix…");
        if (_tv.Settings.NetflixAppId is null)
        {
            _tv.Settings.NetflixAppId = await Task.Run(ExternalApps.DiscoverNetflixAppId);
            if (_tv.Settings.NetflixAppId is not null) _tv.SaveSettings();
        }
        if (_tv.Settings.NetflixAppId is null)
        {
            // No Store app on this PC: open the website in its own fullscreen window instead.
            OpenWeb("https://www.netflix.com/browse", "netflix", ForegroundKind.Netflix, null, "Netflix",
                () => _tv.Apps.LaunchWeb(new WebTile("netflix", "Netflix", "https://www.netflix.com/browse")));
            ShowToast("Netflix app not found, opening netflix.com instead.");
            return;
        }
        if (!_tv.Apps.LaunchNetflix(_tv.Settings.NetflixAppId)) ShowToast(_tv.Apps.LastError ?? "Could not open Netflix.");
    }

    public void LaunchWeb(WebTile tile)
    {
        OpenWeb(tile.Url, "web-" + tile.Id, ForegroundKind.Web, null, tile.Name, () => _tv.Apps.LaunchWeb(tile));
    }

    /// <summary>Opens a site fullscreen inside KwartoTV. If the embedded browser is not available, uses an external browser window.</summary>
    private void OpenWeb(string url, string profile, ForegroundKind kind, string? userAgent, string name, Func<bool> browserFallback)
    {
        PrepareExternal();
        _tv.Apps.Close();
        CloseWeb();
        ShowToast($"Opening {name}…");
        var w = new WebAppWindow(url, profile, kind, userAgent) { Owner = this };
        w.HomeRequested += GoHome;
        w.Failed += _ => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_web, w)) return;
            CloseWeb();
            if (!browserFallback()) ShowToast(_tv.Apps.LastError ?? $"Could not open {name}.");
        });
        _web = w;
        w.Show();
        Native.BringToFront(new WindowInteropHelper(w).Handle);
    }

    private void CloseWeb()
    {
        var w = _web; _web = null;
        if (w is null) return;
        try { w.Close(); } catch { }
    }

    private void PrepareExternal() { CloseDialog(); _player?.Hide(); }

    // ---------- in-app player controls used by the remote

    public bool PlayerTogglePause() => _player is { IsVisible: true } p && p.TogglePause();
    public bool PlayerSeek(int seconds) => _player is { IsVisible: true } p && p.Seek(seconds);
    public bool PlayerSpeed(double speed) => _player is { IsVisible: true } p && p.SetSpeed(speed);
    public bool PlayerStop() { if (_player is not { IsVisible: true } p) return false; GoHome(); return true; }

    // ---------- toast and dialogs

    public void ShowToast(string text)
    {
        ToastText.Text = text; Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    public bool DialogOpen => Overlay.Visibility == Visibility.Visible;

    public void ShowDialog(UIElement content)
    {
        _focusBeforeDialog = Keyboard.FocusedElement;
        DialogCard.Child = content;
        Overlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    public void CloseDialog()
    {
        if (!DialogOpen) return;
        Overlay.Visibility = Visibility.Collapsed;
        DialogCard.Child = null;
        if (_focusBeforeDialog is UIElement u && u.IsVisible) Keyboard.Focus(u); else _current?.FocusFirst();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.Escape or Key.BrowserBack)
        {
            if (DialogOpen) CloseDialog();
            else if (_currentName != "Home") Navigate("Home");
            e.Handled = true;
            return;
        }
        // PC keyboard shortcuts: F1 Home, F2 Live TV, F3 Library, F4 Settings, F5 YouTube.
        if (!DialogOpen && e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            switch (e.Key) { case Key.F1: GoHome(); break; case Key.F2: ShowLiveTv(); break; case Key.F3: ShowLibrary(); break; case Key.F5: LaunchYouTube(); break; default: ShowSettings(); break; }
            e.Handled = true;
            return;
        }
        // Typing a channel number anywhere (not in a text box) switches to that channel.
        if (!DialogOpen && Keyboard.FocusedElement is not TextBox && TryDigit(e.Key, out var d))
        {
            _tv.Lineup.PressDigit(d, DateTimeOffset.UtcNow);
            e.Handled = true;
        }
    }

    private static bool TryDigit(Key k, out int d)
    {
        d = k is >= Key.D0 and <= Key.D9 ? k - Key.D0 : k is >= Key.NumPad0 and <= Key.NumPad9 ? k - Key.NumPad0 : -1;
        return d >= 0;
    }
}
