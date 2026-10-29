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
    private readonly VolumeOverlay _volumeRocker = new();
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

        PowerButton.Click += (_, _) => Dialogs.PowerMenu(this);
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
            _ = CheckForUpdateAsync(userAsked: false);
            await _tv.RefreshChannelsAsync();
        };
        Closed += (_, _) => { CloseWeb(); _player?.Close(); _tv.Apps.Close(); _tv.Dispose(); };
    }

    public TvServices Tv => _tv;

    /// <summary>Checks the latest release. On startup it only tells you when something new exists; from Settings it always answers.</summary>
    public async Task CheckForUpdateAsync(bool userAsked)
    {
        var info = await _tv.Updater.CheckAsync();
        if (info is not null)
        {
            if (!DialogOpen) Dialogs.UpdateDialog(this, info, _tv.Updater); else ShowToast($"Update {info.Version} is available (Settings → Updates).");
            _current?.Refresh();
        }
        else if (userAsked) ShowToast(_tv.Updater.LastError ?? "KwartoTV is up to date.");
    }
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

    /// <summary>The page where the user ticks which channels of a playlist go into the lineup.</summary>
    public void ShowChannelPicker(string playlistId)
    {
        CloseDialog(); CloseWeb(); _tv.Apps.Close(); _player?.Hide();
        _currentName = "Picker";
        _current = new ChannelPickerView(this, playlistId);
        Page.Content = _current;
        for (int i = 0; i < _navButtons.Count; i++)
        {
            var on = i == 3;
            ((TextBlock)_navButtons[i].Content).FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            ((TextBlock)_navButtons[i].Content).TextDecorations = on ? TextDecorations.Underline : null;
        }
        Native.BringToFront(Handle);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _current?.FocusFirst());
    }

    /// <summary>Plays a channel from the picker (not yet in the lineup). Back returns to the picker.</summary>
    public void PreviewChannel(Channel ch)
    {
        if (!MpvPlayer.IsAvailable()) { ShowToast("Video engine (libmpv-2.dll) is missing. Reinstall KwartoTV."); return; }
        CloseWeb(); _tv.Apps.Close();
        _player ??= new PlayerWindow(_tv, this);
        _player.PlayPreview(ch);
    }

    /// <summary>Closes the player and stays on the current page (used when previewing from the picker).</summary>
    public void ClosePlayer()
    {
        _player?.Hide();
        _tv.RebuildLineup();
        Native.BringToFront(Handle);
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _current?.FocusFirst());
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

    public void ShowChannelList()
    {
        if (_tv.Lineup.Channels.Count == 0) { ShowToast("No channels yet. Add channels first."); return; }
        if (_player is not { IsVisible: true }) { ShowLiveTv(); Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _player?.OpenGuide()); }
        else _player.ToggleGuide();
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
        if (_tv.Settings.NetflixInApp)
        {
            // Inside KwartoTV, like YouTube. Move the pointer with the phone's Mouse pad (Netflix's website is built for a mouse).
            const string url = "https://www.netflix.com/browse";
            OpenWeb(url, "netflix", ForegroundKind.Netflix, null, "Netflix", () => _tv.Apps.LaunchWeb(new WebTile("netflix", "Netflix", url)));
            return;
        }
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

    /// <summary>The "Add channels" menu: link, file, clipboard, one stream, or the public iptv-org lists.</summary>
    public void AddChannelsMenu() => Dialogs.AddChannels(this);

    public void AddIptvOrgPlaylist() => Dialogs.IptvOrgPicker(this, pick => AddPlaylistSource(pick.Name + " (iptv-org)", pick.Url, false));

    /// <summary>
    /// Adds a playlist link/file (or one stream), downloads it, and opens the channel picker with everything that was found.
    /// If the download fails the user sees why and what to try.
    /// </summary>
    public async void AddPlaylistSource(string name, string source, bool isStream)
    {
        var err = isStream ? _tv.Settings.AddStream(name, source) : _tv.Settings.AddPlaylist(name, source);
        if (err is not null) { ShowToast(err); return; }
        _tv.SaveSettings();
        var entry = _tv.Settings.Playlists.Last();
        ShowToast(isStream ? "Adding stream…" : $"Downloading {entry.Name}… this can take a minute");
        var failure = await _tv.LoadPlaylistAsync(entry);
        if (failure is not null) { Dialogs.LoadFailed(this, entry, failure); return; }
        _tv.RebuildLineup();
        if (isStream) { ShowToast("Stream added. Find it in your channels."); _current?.Refresh(); return; }
        var count = _tv.Library.GetValueOrDefault(entry.Id)?.Count ?? 0;
        ShowChannelPicker(entry.Id);
        ShowToast($"{count} channels found. Choose the ones you want.");
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
    public bool KeyboardOpen => OskHost.Visibility == Visibility.Visible;
    private TextBox? _oskTarget;

    /// <summary>Opens the on-screen keyboard for a text box (press OK on the box).</summary>
    private Action? _oskDone;

    public void ShowKeyboard(TextBox target, Action? onDone = null)
    {
        _oskTarget = target; _oskDone = onDone;
        var osk = new OnScreenKeyboard(new TextBoxTarget(target), () => HideKeyboard(next: true));
        OskHost.Child = osk;
        OskHost.Visibility = Visibility.Visible;
        // Move the dialog to the top so the keyboard does not cover it.
        DialogCard.VerticalAlignment = VerticalAlignment.Top; DialogCard.Margin = new Thickness(0, 24, 0, 0);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, osk.FocusFirstKey);
    }

    public void HideKeyboard(bool next = false)
    {
        if (!KeyboardOpen) return;
        OskHost.Visibility = Visibility.Collapsed; OskHost.Child = null;
        DialogCard.VerticalAlignment = VerticalAlignment.Center; DialogCard.Margin = new Thickness(0);
        var box = _oskTarget; _oskTarget = null;
        var done = _oskDone; _oskDone = null;
        if (box is null) return;
        if (done is not null) { done(); return; }
        box.Focus(); Keyboard.Focus(box);
        if (next) box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    /// <summary>Shows a dialog over the page. [focus] is the element that gets the keyboard first (default: the first control).</summary>
    public void ShowDialog(UIElement content, UIElement? focus = null)
    {
        _focusBeforeDialog = Keyboard.FocusedElement;
        DialogCard.Child = content;
        Overlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (focus is not null) { focus.Focus(); Keyboard.Focus(focus); }
            else content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
    }

    public void CloseDialog()
    {
        if (!DialogOpen) return;
        if (KeyboardOpen) { OskHost.Visibility = Visibility.Collapsed; OskHost.Child = null; DialogCard.VerticalAlignment = VerticalAlignment.Center; DialogCard.Margin = new Thickness(0); _oskTarget = null; }
        Overlay.Visibility = Visibility.Collapsed;
        DialogCard.Child = null;
        if (_focusBeforeDialog is UIElement u && u.IsVisible) Keyboard.Focus(u); else _current?.FocusFirst();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.Escape or Key.BrowserBack)
        {
            if (KeyboardOpen) HideKeyboard();
            else if (DialogOpen) CloseDialog();
            else if (_currentName == "Picker") ShowSettings();
            else if (_currentName != "Home") Navigate("Home");
            e.Handled = true;
            return;
        }
        // PC keyboard shortcuts: F1 Home, F2 Live TV, F3 Library, F4 Settings, F5 YouTube, F6 Netflix, F7 add channels, F8 power menu, F9 iptv-org lists.
        if (!DialogOpen && e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            switch (e.Key) { case Key.F1: GoHome(); break; case Key.F2: ShowLiveTv(); break; case Key.F3: ShowLibrary(); break; case Key.F5: LaunchYouTube(); break; case Key.F6: LaunchNetflix(); break; case Key.F7: AddChannelsMenu(); break; case Key.F9: AddIptvOrgPlaylist(); break; case Key.F8: Dialogs.PowerMenu(this); break; default: ShowSettings(); break; }
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
