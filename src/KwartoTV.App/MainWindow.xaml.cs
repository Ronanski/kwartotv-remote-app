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
    private bool _pageStale;
    private string _currentName = "Home";

    public MainWindow()
    {
        InitializeComponent();
        _tv = new TvServices(_host);
        _host.Attach(this);
        // While the clock lock screen is up, the remote can only unlock it (and use volume and power).
        _tv.Router.IsLocked = () => _locked;

        foreach (var (label, action) in new (string, Action)[] { ("Home", GoHome), ("Live TV", ShowLiveTv), ("Library", ShowLibrary), ("Settings", ShowSettings) })
        {
            var b = new Button { Content = new TextBlock { Text = label }, Style = (Style)FindResource("Pill") };
            b.Click += (_, _) => action();
            _navButtons.Add(b); Nav.Children.Add(b);
        }

        PowerButton.Click += (_, _) => Dialogs.PowerMenu(this);
        // While the player or a web app is on screen it must keep the keyboard (the remote's keys). If the home window
        // behind it gets activated (a click, a notification closing), hand the focus straight back.
        Activated += (_, _) =>
        {
            if (_lock is { } locked) { Dispatcher.BeginInvoke(() => locked.Activate()); return; }
            if (_player is { IsVisible: true } p) Dispatcher.BeginInvoke(() => { p.Activate(); p.Focus(); });
            else if (_web is { IsVisible: true } w) Dispatcher.BeginInvoke(() => w.Activate());
        };
        _clock.Tick += (_, _) => UpdateClock(); UpdateClock(); _clock.Start();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _digits.Tick += (_, _) => _tv.Lineup.Tick(DateTimeOffset.UtcNow);
        _digits.Start();

        _tv.Lineup.Tuned += OnTuned;
        // Redraw the page only while it is on screen; GoHome redraws it when the player or a web app closes.
        _tv.ChannelsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (_current is ChannelPickerView || _player is { IsVisible: true } || _web is { IsVisible: true }) { _pageStale = true; return; }
            _current?.Refresh();
        });
        _tv.Apps.Exited += () => Dispatcher.BeginInvoke(() => { if (_player is not { IsVisible: true }) Native.BringToFront(Handle); });

        Loaded += async (_, _) =>
        {
            Navigate("Home");
            await _tv.StartRemoteAsync();
            RemoteDot.Foreground = _tv.Server is null ? (System.Windows.Media.Brush)FindResource("Danger") : (System.Windows.Media.Brush)FindResource("Accent");
            _current?.Refresh();
            // First run: nothing is paired yet, so show the pairing code straight away (no need to find Settings).
            if (_tv.Server is not null && _tv.Pairing.Devices.Count == 0) Dialogs.Pair(this);
            _updateTimer.Tick += async (_, _) => await AutoUpdateTickAsync();
            _updateTimer.Start();
            _ = AutoUpdateTickAsync();
            _tv.StartGuide();
            await _tv.RefreshChannelsAsync();
        };
        Closed += (_, _) => { CloseWeb(); _player?.Close(); _tv.Apps.Close(); _tv.Dispose(); };
    }

    public TvServices Tv => _tv;

    // ---------- automatic updates
    // Checked every 6 hours (a TV PC stays on for days). A new version installs by itself once nobody has used the TV for
    // 15 minutes and nothing is playing, through the update task the installer made (no Windows permission prompt).

    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _updateToastShown, _updating, _noUpdateTask;
    private static readonly TimeSpan UpdateCheckEvery = TimeSpan.FromHours(6), IdleBeforeUpdate = TimeSpan.FromMinutes(15);

    private async Task AutoUpdateTickAsync()
    {
        if (_updating) return;
        if (DateTime.UtcNow - _lastUpdateCheck > UpdateCheckEvery)
        {
            _lastUpdateCheck = DateTime.UtcNow;
            await _tv.Updater.CheckAsync();
            _current?.Refresh();
        }
        var info = _tv.Updater.Available;
        if (info is null) return;
        bool inUse = _player is { IsVisible: true } || _web is { IsVisible: true } || _tv.Apps.IsRunning || Native.IdleTime < IdleBeforeUpdate;
        if (!inUse && !_noUpdateTask)
        {
            _updating = true;
            AppPaths.LogLine($"update: installing {info.Version} while the TV is not in use");
            // Per-user install: download, close, install silently and open again, with no Windows permission prompt.
            if (Updater.IsPerUserInstall)
            {
                if (await _tv.Updater.InstallAsync(info) is not { } err) { await ShowUpdatingNoticeAsync(info.Version); Application.Current.Shutdown(); return; }
                AppPaths.LogLine("update: " + err);
                _updating = false;
                _lastUpdateCheck = DateTime.UtcNow - UpdateCheckEvery + TimeSpan.FromHours(1);   // try again in an hour
                return;
            }
            if (await Updater.StartUpdateTaskAsync()) { await ShowUpdatingNoticeAsync(info.Version); Application.Current.Shutdown(); return; }
            // An old Program Files install without the update task: install from the banner or Settings (Windows asks once,
            // and that update moves KwartoTV to a per-user install that never asks again).
            _noUpdateTask = true; _updating = false;
        }
        if (!_updateToastShown)
        {
            _updateToastShown = true;
            ShowToast(_noUpdateTask ? $"Update {info.Version} is available (Home or Settings → Updates)." : $"Update {info.Version} is ready. It installs by itself when the TV is not in use.");
        }
    }

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
        AppPaths.LogLine("page: " + name);
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
        _pageStale = false;
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
        if (_pageStale && _current is not ChannelPickerView) { _pageStale = false; _current?.Refresh(); }
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
        // Nothing watched yet: tuning channel 1 opens the player through OnTuned. Opening it here as well started the stream twice.
        if (_tv.Lineup.Current is { } ch) OpenPlayerLive(ch);
        else _tv.Lineup.TuneNumber(1);
    }

    /// <summary>
    /// Plays a channel from a card on screen. If the channel is no longer in the lineup (hidden after a check since the
    /// screen was drawn), says so instead of playing another channel.
    /// </summary>
    public void TuneChannel(Channel ch)
    {
        if (_tv.Lineup.TuneId(ch.Id) is null) { ShowToast($"{ch.Name} is not available right now."); _current?.Refresh(); }
    }

    /// <summary>Starts watching a playlist's own space: its first channel, with the channel list open on that space.</summary>
    public void OpenSpace(string playlistId)
    {
        var space = _tv.Spaces().FirstOrDefault(s => s.Playlist.Id == playlistId);
        if (space.Playlist is null || space.Channels.Count == 0) { ShowToast("No working channels in this space yet. Open Settings → Choose channels."); return; }
        var currentInSpace = space.Channels.Any(c => c.Number == _tv.Lineup.CurrentNumber);
        if (!currentInSpace) _tv.Lineup.TuneNumber(space.Channels[0].Number); else ShowLiveTv();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _player?.OpenGuide("📺 " + space.Playlist.Name));
    }

    /// <summary>A playlist link sent from the phone: added like any link, then the channel picker opens on the TV.</summary>
    public void AddLinkFromPhone(string url)
    {
        CloseWeb(); _player?.Hide(); _tv.Apps.Close();
        Native.BringToFront(Handle);
        AddPlaylistSource("", url, false);
    }

    /// <summary>A YouTube live link in a playlist: opens inside KwartoTV showing only the video. ▲▼ still change channel.</summary>
    public void PlayYouTubeChannel(Channel ch)
    {
        AppPaths.LogLine("youtube channel: " + ch.Name);
        OpenWeb(ch.Url, "youtube", ForegroundKind.YouTube, null, ch.Name, () => _tv.Apps.LaunchWeb(new WebTile(ch.Id, ch.Name, ch.Url)), videoOnly: true);
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
            // Inside KwartoTV, like YouTube, with TV-style remote navigation (TvWebScript). The phone's Mouse pad still works too.
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
        // Always inside KwartoTV (never an outside browser window), with TV-style remote navigation.
        OpenWeb(tile.Url, "web-" + tile.Id, ForegroundKind.Web, null, tile.Name, () =>
        {
            ShowToast($"Could not open {tile.Name}: Microsoft Edge WebView2 is missing. Install it from microsoft.com/edge/webview2 and try again.");
            return true;
        });
    }

    /// <summary>Bilibili (anime, dramas, with English subtitles), inside KwartoTV with TV-style remote navigation.</summary>
    public void LaunchBilibili() => LaunchWeb(new WebTile("bilibili", "Bilibili", AppLaunchPlans.BilibiliUrl));

    /// <summary>Opens a site fullscreen inside KwartoTV. If the embedded browser is not available, uses an external browser window.</summary>
    private void OpenWeb(string url, string profile, ForegroundKind kind, string? userAgent, string name, Func<bool> browserFallback, bool videoOnly = false)
    {
        PrepareExternal();
        _tv.Apps.Close();
        CloseWeb();
        ShowToast($"Opening {name}…");
        var w = new WebAppWindow(url, profile, kind, userAgent, videoOnly) { Owner = this };
        w.HomeRequested += GoHome;
        w.ChannelUp += () => _tv.Lineup.Next();
        w.ChannelDown += () => _tv.Lineup.Previous();
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

    /// <summary>
    /// One press: free channels in English and Filipino plus the Philippines list (as its own space). Everything is
    /// checked from this PC and only the channels that work appear, sorted by category.
    /// </summary>
    public async void AddFreePack()
    {
        int added = 0;
        foreach (var s in IptvOrg.FreePack)
        {
            if (_tv.Settings.Playlists.Any(p => p.Source == s.Url)) continue;
            if (_tv.Settings.AddPlaylist(s.Name, s.Url) is not null) continue;
            var entry = _tv.Settings.Playlists.Last();
            _tv.Settings.SetSelectionAll(entry.Id);
            if (s.Name == "Philippines") _tv.Settings.SetSpace(entry.Id, true);
            added++;
        }
        _tv.SaveSettings();
        if (added == 0) { ShowToast("The free channel pack is already added."); return; }
        ShowToast("Downloading the free channel lists…");
        await _tv.RefreshChannelsAsync();
        ShowToast($"Checking {_tv.ChosenChannels().Count} channels. The ones that work appear on Home as they pass.");
        GoHome();
    }

    /// <summary>
    /// The networks' own YouTube live streams (GMA, Kapamilya, TV5, ...), added only when asked: they are not on around the
    /// clock, so they show in Live TV only while they are live.
    /// </summary>
    public async void AddPinoyLive()
    {
        if (_tv.Settings.Playlists.Any(p => string.Equals(p.Source, AppPaths.PinoyLivePlaylist, StringComparison.OrdinalIgnoreCase))) { ShowToast("Pinoy Live is already added."); return; }
        if (_tv.Settings.AddPlaylist(PinoyLive.Name, AppPaths.PinoyLivePlaylist) is { } err) { ShowToast(err); return; }
        var pinoy = _tv.Settings.Playlists.Last();
        _tv.Settings.SetSelectionAll(pinoy.Id);
        _tv.Settings.SetSpace(pinoy.Id, true);
        _tv.SaveSettings();
        ShowToast("Pinoy Live added. Channels show while they are live.");
        await _tv.RefreshChannelsAsync();
    }

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

    // ---------- navigation check (--nav-audit): can the arrow keys reach every button on every screen?

    /// <summary>Opens each page and the main dialogs and writes, for each, the buttons the arrows cannot reach.</summary>
    public async Task RunNavAuditAsync(string reportPath)
    {
        var lines = new List<string>();
        int problems = 0;
        void Audit(string name, FrameworkElement scope)
        {
            scope.UpdateLayout();
            var missing = SpatialNav.Unreachable(scope, out var total);
            problems += missing.Count;
            lines.Add($"{name}: {total} controls, {missing.Count} unreachable" + (missing.Count > 0 ? ": " + string.Join(" | ", missing.Select(SpatialNav.NameOf)) : ""));
        }
        async Task Settle() { await Task.Delay(1200); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }

        CloseDialog();
        foreach (var page in new[] { "Home", "Settings", "Library" })
        {
            Navigate(page); await Settle();
            Audit("page " + page, (FrameworkElement)Content);
        }
        Navigate("Home"); await Settle();
        foreach (var (name, open) in new (string, Action)[] { ("dialog Power", () => Dialogs.PowerMenu(this)), ("dialog Add channels", () => Dialogs.AddChannels(this)), ("dialog Pair", () => Dialogs.Pair(this)) })
        {
            open(); await Settle();
            Audit(name, DialogCard);
            CloseDialog(); await Settle();
        }
        lines.Add(problems == 0 ? "RESULT: every control can be reached with the arrows" : $"RESULT: {problems} control(s) cannot be reached with the arrows");
        File.WriteAllLines(reportPath, lines);
        foreach (var l in lines) AppPaths.LogLine("nav audit: " + l);
    }

    // ---------- the remote's keys go to KwartoTV

    /// <summary>
    /// Says on the TV that KwartoTV is about to close for an update and opens again by itself, for a few seconds, so
    /// nobody thinks it stopped working. (The helper then shows an "Updating" screen until KwartoTV is back.)
    /// </summary>
    public async Task ShowUpdatingNoticeAsync(string version)
    {
        _player?.Hide(); CloseWeb();
        var panel = new StackPanel { Width = 760 };
        panel.Children.Add(new TextBlock { Text = $"Updating to version {version}", FontSize = 34, FontWeight = FontWeights.Bold });
        panel.Children.Add(UiKit.Dimmed("KwartoTV will close in a moment and open again by itself in about a minute. Please do not turn off the PC.", 20));
        var count = UiKit.Dimmed("", 20);
        count.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(count);
        ShowDialog(panel);
        Native.BringToFront(Handle); Activate();
        for (int s = 5; s > 0; s--) { count.Text = $"Closing in {s}…"; await Task.Delay(1000); }
    }

    public bool EnsureInFront()
    {
        if (_tv.Apps.IsRunning) return false;                                         // an outside app KwartoTV opened has them
        if (Native.ForegroundProcessId() == (uint)Environment.ProcessId) return false; // already ours (any KwartoTV window)
        Window target = _lock is not null ? _lock : _player is { IsVisible: true } p ? p : _web is { IsVisible: true } w ? w : this;
        AppPaths.LogLine("remote: KwartoTV was not in front; bringing it back for the remote");
        Native.BringToFront(new WindowInteropHelper(target).Handle);
        target.Activate();
        if (target == this) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (Keyboard.FocusedElement is null) _current?.FocusFirst(); });
        return true;
    }

    // ---------- lock screen (the remote's "Lock"): a calm clock, and the TV is locked until the phone unlocks it

    private LockScreenWindow? _lock;
    private volatile bool _locked;
    public bool ScreenIsOff => _locked;

    /// <summary>
    /// Shows the clock screensaver and locks the TV: keys, clicks and remote buttons do nothing until Unlock on the
    /// phone (volume, mute and power still work). Sound keeps playing.
    /// </summary>
    public void ScreenOff()
    {
        if (_lock is not null) return;
        AppPaths.LogLine("screen: locked (clock)");
        CloseDialog(); HideKeyboard();
        _locked = true;
        // On the screen KwartoTV is showing on (the player's or a web app's when one is open).
        Window tvWindow = _player is { IsVisible: true } p ? p : _web is { IsVisible: true } w ? w : this;
        var l = new LockScreenWindow(tvWindow);
        _lock = l;
        l.Show();
        l.Activate();
        // The PC must not go to sleep behind the clock (the phone could not reach it to unlock), and the clock stays on screen.
        Native.KeepAwake(true);
    }

    public void ScreenOn()
    {
        var l = _lock; _lock = null; _locked = false;
        Native.KeepAwake(false);
        Native.WakeScreen();
        if (l is null) return;
        try { l.Unlock(); } catch { }
        AppPaths.LogLine("screen: unlocked");
        // Give the keyboard back to what is on screen.
        if (_player is { IsVisible: true } p) { p.Activate(); p.Focus(); }
        else if (_web is { IsVisible: true } w) w.Activate();
        else { Native.BringToFront(Handle); Activate(); }
    }

    public void Finder(string cmd, string? arg)
    {
        ChannelFinder.TryParseToggle(arg, out var on, out var value);
        switch (cmd)
        {
            case "scan.start":
                if (_tv.Settings.Playlists.Count == 0) throw new UserMessageException("Add a playlist on the TV first (Settings → Add channels).");
                _tv.StartScan(full: arg == "full");
                ShowToast("Checking every channel from this PC. Follow it on the phone.");
                break;
            case "scan.stop": _tv.StopScan(); break;
            case "catalog.choose": _tv.Choose(value, on); break;
            case "catalog.choosegroup": _tv.ChooseGroup(value, on); break;
            case "catalog.favorite": _tv.Favorite(value, on); break;
        }
    }

    // ---------- toast and dialogs

    public void ShowToast(string text)
    {
        ToastText.Text = text; Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    public bool DialogOpen => Overlay.Visibility == Visibility.Visible;
    public bool KeyboardOpen => OskHost.Visibility == Visibility.Visible;
    private TextBox? _oskTarget;

    private Action? _oskDone;
    private bool _eatEnterRepeat;   // a held OK that pressed Done must not go on to press the next control

    /// <summary>
    /// Opens the on-screen keyboard for a text box (press OK on the box). [purpose] names the kind of field ("link", ...)
    /// so its recent entries are offered; [title] is shown above the keyboard's preview.
    /// </summary>
    public void ShowKeyboard(TextBox target, Action? onDone = null, string? purpose = null, string? title = null)
    {
        _oskTarget = target; _oskDone = onDone;
        var osk = new OnScreenKeyboard(new TextBoxTarget(target), () => HideKeyboard(next: true), () => HideKeyboard(), purpose, title);
        OskHost.Child = osk;
        OskHost.Visibility = Visibility.Visible;
        // Move the dialog to the top so the keyboard does not cover it.
        DialogCard.VerticalAlignment = VerticalAlignment.Top; DialogCard.Margin = new Thickness(0, 24, 0, 0);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, osk.FocusFirstKey);
    }

    public void HideKeyboard(bool next = false)
    {
        if (!KeyboardOpen) return;
        _eatEnterRepeat = true;
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

    // Only five keys are needed (arrows and OK): on buttons a short OK acts when it is let go, and holding OK for half a
    // second is Back. Text boxes and the on-screen keyboard keep OK as it was (the keyboard repeats keys while OK is held).
    private static readonly TimeSpan HoldForBack = TimeSpan.FromMilliseconds(500);
    private DateTime _okDownAt;
    private bool _okDown, _okHeldDone;

    private void GoBack()
    {
        if (KeyboardOpen) HideKeyboard();
        else if (DialogOpen) CloseDialog();
        else if (_currentName == "Picker") ShowSettings();
        else if (_currentName != "Home") Navigate("Home");
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key != Key.Enter || !_okDown) return;
        _okDown = false;
        e.Handled = true;
        if (_okHeldDone) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase b && b.IsEnabled)
            b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        else
        {
            // OK with nothing selected (focus was lost): select the first control instead of doing nothing.
            FrameworkElement scope = KeyboardOpen ? OskHost : DialogOpen ? DialogCard : (FrameworkElement)Content;
            SpatialNav.Move(scope, Key.Down);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Enter && e.IsRepeat && _eatEnterRepeat) { e.Handled = true; return; }
        if (!e.IsRepeat) _eatEnterRepeat = false;
        // The on-screen keyboard handles the remote itself (wrapping rows, hold OK). Other keys, such as letters typed
        // on a real keyboard, reach it as text; the page shortcuts below stay off while it is open.
        if (KeyboardOpen && OskHost.Child is OnScreenKeyboard osk)
        {
            if (osk.HandleKey(e)) e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && !KeyboardOpen && Keyboard.FocusedElement is not TextBox)
        {
            e.Handled = true;
            if (!e.IsRepeat) { _okDown = true; _okHeldDone = false; _okDownAt = DateTime.UtcNow; }
            else if (_okDown && !_okHeldDone && DateTime.UtcNow - _okDownAt >= HoldForBack) { _okHeldDone = true; GoBack(); }
            return;
        }
        // Arrow keys (the remote's d-pad): our own TV-style focus movement, everywhere on this window.
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.None)
        {
            // In a text box, left/right move the cursor; up/down leave the box.
            if (Keyboard.FocusedElement is TextBox && e.Key is Key.Left or Key.Right) return;
            FrameworkElement scope = KeyboardOpen ? OskHost : DialogOpen ? DialogCard : (FrameworkElement)Content;
            SpatialNav.Move(scope, e.Key);
            e.Handled = true;
            return;
        }
        // OK with nothing selected (focus was lost): select the first control instead of doing nothing.
        if (e.Key == Key.Enter && Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.ButtonBase or TextBox))
        {
            FrameworkElement scope = KeyboardOpen ? OskHost : DialogOpen ? DialogCard : (FrameworkElement)Content;
            SpatialNav.Move(scope, Key.Down);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Escape or Key.BrowserBack)
        {
            GoBack();
            e.Handled = true;
            return;
        }
        // Ctrl+L on a PC keyboard locks the TV with the clock (only the phone unlocks it).
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
        {
            ScreenOff();
            e.Handled = true;
            return;
        }
        // PC keyboard shortcuts: F1 Home, F2 Live TV, F3 Library, F4 Settings, F5 YouTube, F6 Netflix, F7 add channels, F8 power menu, F9 iptv-org lists, F11 Bilibili.
        if (!DialogOpen && e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F11 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            switch (e.Key) { case Key.F1: GoHome(); break; case Key.F2: ShowLiveTv(); break; case Key.F3: ShowLibrary(); break; case Key.F5: LaunchYouTube(); break; case Key.F6: LaunchNetflix(); break; case Key.F7: AddChannelsMenu(); break; case Key.F9: AddIptvOrgPlaylist(); break; case Key.F11: LaunchBilibili(); break; case Key.F8: Dialogs.PowerMenu(this); break; default: ShowSettings(); break; }
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
