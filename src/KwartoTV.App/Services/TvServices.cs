using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>Everything the TV shell shares: settings, channel lineup, pairing, remote server and discovery.</summary>
public sealed class TvServices : IDisposable
{
    private static readonly HttpClient Http = ResilientHttp.Create(TimeSpan.FromMinutes(4));

    public AppSettings Settings { get; private set; }
    public ChannelLineup Lineup { get; } = new();
    public PairingService Pairing { get; } = new();
    public ExternalApps Apps { get; } = new();
    /// <summary>YouTube Music in the background, controlled from the phone (plays on behind the lock screen).</summary>
    public MusicService MusicPlayer { get; } = new();

    /// <summary>Catalog apps installed on this PC (catalog id → Start menu id), found at start.</summary>
    public IReadOnlyDictionary<string, string> InstalledApps { get; private set; } = new Dictionary<string, string>();

    /// <summary>Looks again which catalog apps are installed; the Home screen redraws when that changed.</summary>
    public async Task RefreshInstalledAppsAsync()
    {
        var found = await Task.Run(ExternalApps.DiscoverCatalogApps);
        var changed = !found.OrderBy(k => k.Key).SequenceEqual(InstalledApps.OrderBy(k => k.Key));
        InstalledApps = found;
        if (found.Count > 0) AppPaths.LogLine("apps: installed " + string.Join(", ", found.Keys));
        if (changed) ChannelsChanged?.Invoke();
    }
    public Updater Updater { get; } = new();
    public CommandRouter Router { get; private set; } = null!;
    public RemoteServer? Server { get; private set; }
    public MdnsResponder? Mdns { get; private set; }
    public string RemoteStatus { get; private set; } = "Starting…";
    public IReadOnlyList<string> PlaylistErrors { get; private set; } = Array.Empty<string>();
    public bool ChannelsLoading { get; private set; }
    private int _retries;

    public event Action? ChannelsChanged;

    // ---------- TV guide: what is on now and next

    /// <summary>The merged TV guide (empty until the first download, or when no guide covers the channels).</summary>
    public ProgramGuide Guide { get; private set; } = ProgramGuide.Empty;
    public event Action? GuideChanged;

    /// <summary>Loads the guides from the cache, downloads the ones that are old, and does it again every few hours.</summary>
    public void StartGuide() => _ = Task.Run(async () =>
    {
        while (true)
        {
            await LoadGuideAsync();
            // every 20 minutes: which YouTube live channels are on air (the rest is checked once a day)
            for (int i = 0; i < 3; i++) { CheckChosenInBackground(); await Task.Delay(LiveRecheckAfter); }
        }
    });

    private async Task LoadGuideAsync()
    {
        var now = DateTimeOffset.Now;
        var guides = new List<ProgramGuide>();
        var urls = Settings.GuideUrls.ToList();
        for (int i = 0; i < urls.Count; i++)
        {
            var file = AppPaths.GuideCache(i);
            try
            {
                if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > ProgramGuideSources.RefreshAfter)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    var tmp = file + ".tmp";
                    using (var resp = await Http.GetAsync(urls[i], HttpCompletionOption.ResponseHeadersRead))
                    {
                        resp.EnsureSuccessStatusCode();
                        await using var src = await resp.Content.ReadAsStreamAsync();
                        await using var dst = File.Create(tmp);
                        await src.CopyToAsync(dst);
                    }
                    File.Move(tmp, file, overwrite: true);
                }
                await using var read = File.OpenRead(file);
                var g = ProgramGuide.Parse(read, now.AddHours(-3), now.AddHours(24));
                guides.Add(g);
                AppPaths.LogLine($"guide: {urls[i]}: {g.ChannelCount} channels");
            }
            catch (Exception ex) { AppPaths.LogLine($"guide: {urls[i]}: {ex.Message}"); }
        }
        if (guides.Count == 0) return;
        Guide = ProgramGuide.Merge(guides);
        GuideChanged?.Invoke();
    }

    /// <summary>"Eat Bulaga  ·  12:00–1:00 PM" for a channel, or null when the guide has nothing for it.</summary>
    public (string? Now, string? Next) OnAir(Channel ch)
    {
        var (now, next) = Guide.NowNext(ch, DateTimeOffset.Now);
        static string Fmt(Programme p) => $"{p.Title}  ·  {p.Start.ToLocalTime():h:mm}–{p.Stop.ToLocalTime():h:mm tt}";
        return (now is null ? null : Fmt(now), next is null ? null : Fmt(next));
    }

    public TvServices(IHostActions host)
    {
        AppPaths.Ensure();
        Settings = JsonFile.LoadOrNew<AppSettings>(AppPaths.Settings);
        try { if (File.Exists(AppPaths.Devices)) Pairing.Import(File.ReadAllText(AppPaths.Devices)); } catch (Exception ex) { AppPaths.LogLine("devices: " + ex.Message); }
        Pairing.Changed += SaveDevices;

        // The Pinoy Live pack is written fresh each start, so app updates bring its newest channel list.
        try { Directory.CreateDirectory(AppPaths.PastedPlaylists); File.WriteAllText(AppPaths.PinoyLivePlaylist, PinoyLive.Playlist()); } catch (Exception ex) { AppPaths.LogLine("pinoy live: " + ex.Message); }

        // Start with what was loaded last time, so the channels are there even before (or without) the internet.
        foreach (var kv in JsonFile.LoadOrNew<Dictionary<string, List<Channel>>>(AppPaths.PlaylistCache)) Library[kv.Key] = kv.Value;
        foreach (var kv in JsonFile.LoadOrNew<Dictionary<string, ChannelHealth>>(AppPaths.Health)) Health[kv.Key] = kv.Value;
        // Channel numbers are kept for good (like cable): restore them before the first load.
        Lineup.SetNumbers(Settings.Lineup.Numbers);
        Lineup.Reserve(ChosenChannels());
        Lineup.Load(ChannelCatalog.BuildLineup(Settings.Playlists, Library, Settings.HideUnavailable ? Health : null, onlyVerified: Settings.HideUnavailable, maxChannels: Settings.MaxChannels));
        Lineup.Restore(Settings.Lineup);
        // New channels get numbers when the lineup changes: save them.
        Lineup.Changed += SaveSettingsSoon;
        Lineup.Renumbered += () => Lineup.Reserve(ChosenChannels());
        Lineup.Tuned += (_, _) => SaveSettingsSoon();
        Lineup.FavoritesChanged += SaveSettingsSoon;
        Router = new CommandRouter(Lineup, host);
        // A new guide changes the "on now" lines phones show in their channel list.
        GuideChanged += () => Server?.NotifyLineupChanged();
        // Stars set on the TV (or another phone) show on every phone's lists.
        Lineup.FavoritesChanged += () => { Server?.NotifyLineupChanged(); Server?.NotifyCatalogChanged(); };
    }

    private int _saveQueued;
    public void SaveSettingsSoon()
    {
        if (Interlocked.Exchange(ref _saveQueued, 1) == 1) return;
        _ = Task.Run(async () => { await Task.Delay(800); Interlocked.Exchange(ref _saveQueued, 0); SaveSettings(); });
    }

    public void SaveSettings()
    {
        try { Settings.Lineup = Lineup.Export(); JsonFile.Save(AppPaths.Settings, Settings); }
        catch (Exception ex) { AppPaths.LogLine("save settings: " + ex.Message); }
    }

    private void SaveDevices()
    {
        try { AppPaths.Ensure(); File.WriteAllText(AppPaths.Devices, Pairing.Export()); } catch (Exception ex) { AppPaths.LogLine("save devices: " + ex.Message); }
    }

    // ---------- playlists and the channels the user chose

    /// <summary>Every channel of every playlist (what the channel picker shows). The lineup holds only the selected ones.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<Channel>> Library { get; } = new();
    // Written by downloads (which can finish on a background thread) and read by the UI: thread-safe.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _errors = new();

    /// <summary>Whether each channel works on this PC (checked here, so country blocks are seen as they are for this connection).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, ChannelHealth> Health { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _checking = new();
    private static readonly HttpClient ProbeHttp = ResilientHttp.Create(TimeSpan.FromSeconds(30));
    /// <summary>How often the background check runs (YouTube live links and channels that did not work get rechecked).</summary>
    public static readonly TimeSpan LiveRecheckAfter = HealthPolicy.RecheckLive;

    public HealthStatus StatusOf(Channel c) => Health.TryGetValue(c.Id, out var h) ? h.Status : HealthStatus.Unknown;

    /// <summary>Remembers what happened when a channel was played (the player knows best) and saves it.</summary>
    public void MarkHealth(Channel c, HealthStatus status, string? reason)
    {
        // With no network every channel fails: that says nothing about the channel, so do not hide it.
        if (status == HealthStatus.Dead && !IsOnline())
        {
            AppPaths.LogLine($"health: {c.Name} failed while offline; not marked");
            return;
        }
        Health[c.Id] = new ChannelHealth(status, reason, DateTimeOffset.UtcNow, FromPlayer: true);
        SaveHealthSoon();
    }

    /// <summary>False when this PC has no working network connection at all.</summary>
    public static bool IsOnline()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); } catch { return true; }
    }

    private int _healthQueued;
    private void SaveHealthSoon()
    {
        if (Interlocked.Exchange(ref _healthQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500); Interlocked.Exchange(ref _healthQueued, 0);
            try { JsonFile.Save(AppPaths.Health, Health.ToDictionary(k => k.Key, k => k.Value)); } catch (Exception ex) { AppPaths.LogLine("health save: " + ex.Message); }
        });
    }

    /// <summary>
    /// Checks channels from this PC, 8 at a time. Channels checked within the last day are skipped unless [force].
    /// The lineup is rebuilt at the end so the ones that do not work disappear.
    /// </summary>
    public async Task<(int Ok, int Dead)> CheckChannelsAsync(IEnumerable<Channel> channels, bool force = false, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        // Offline: every check would fail and hide channels that are fine.
        if (!IsOnline()) return (0, 0);
        var now = DateTimeOffset.UtcNow;
        var todo = channels.Where(c => force || HealthPolicy.NeedsCheck(c, Health.GetValueOrDefault(c.Id), now))
                           .Where(c => _checking.TryAdd(c.Id, 0)).ToList();
        int done = 0, ok = 0, dead = 0, network = 0;
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, ChannelHealth>();
        Interlocked.Add(ref _checkingCount, todo.Count);
        using var gate = new SemaphoreSlim(todo.Count > 1000 ? 32 : todo.Count > 200 ? 16 : 8);
        var tasks = todo.Select(async c =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var h = await StreamProbe.CheckAsync(c, ProbeHttp, DateTimeOffset.UtcNow, ct);
                Health[c.Id] = h; results[c.Id] = h;
                if (h.Status == HealthStatus.Dead)
                {
                    Interlocked.Increment(ref dead);
                    if (h.Reason is "cannot connect" || h.Reason?.StartsWith("no answer") == true) Interlocked.Increment(ref network);
                    if (todo.Count <= 300) AppPaths.LogLine($"health: {c.Name}: dead ({h.Reason})");
                }
                else if (h.Status == HealthStatus.Ok)
                {
                    Interlocked.Increment(ref ok);
                    // Working channels appear as they pass (at most every 2 seconds), not only when the slowest check ends.
                    var last = Interlocked.Read(ref _lastLiveRebuild);
                    var tick = Environment.TickCount64;
                    if (tick - last > 2000 && Interlocked.CompareExchange(ref _lastLiveRebuild, tick, last) == last) RebuildLineup();
                }
            }
            finally
            {
                gate.Release(); _checking.TryRemove(c.Id, out _);
                Interlocked.Decrement(ref _checkingCount);
                var d = Interlocked.Increment(ref done);
                progress?.Report((d, todo.Count));
                // Long checks: show the channels found so far, every 100.
                if (d % 100 == 0) RebuildLineup();
            }
        });
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
        // If nearly everything failed to even connect, the internet was down, not the channels: forget these results.
        if (todo.Count >= 10 && network >= todo.Count * 0.9)
        {
            foreach (var id in results.Keys) Health.TryRemove(id, out _);
            AppPaths.LogLine($"health: {network} of {todo.Count} could not connect; looks like the internet is down, results not kept");
            return (0, 0);
        }
        SaveHealthSoon();
        if (todo.Count > 0) { AppPaths.LogLine($"health: checked {todo.Count} channels: {ok} work, {dead} do not"); RebuildLineup(); }
        return (ok, dead);
    }

    /// <summary>Playlists shown as their own space, with the lineup numbers of their channels (only the ones that work).</summary>
    public IReadOnlyList<(PlaylistEntry Playlist, IReadOnlyList<(int Number, Channel Channel)> Channels)> Spaces()
    {
        var number = new Dictionary<string, int>();
        foreach (var (n, c) in Lineup.Numbered) number[c.Id] = n;
        var result = new List<(PlaylistEntry, IReadOnlyList<(int, Channel)>)>();
        foreach (var p in Settings.Playlists.Where(p => p.ShowAsSpace))
        {
            var mine = Library.GetValueOrDefault(p.Id) ?? Array.Empty<Channel>();
            var list = mine.Where(c => number.ContainsKey(c.Id)).Select(c => (number[c.Id], c)).ToList();
            result.Add((p, list));
        }
        return result;
    }

    private int _checkingCount;
    /// <summary>How many channels are being checked right now (shown on Home while the first check runs).</summary>
    public int CheckingCount => Volatile.Read(ref _checkingCount);
    private long _lastLiveRebuild;

    /// <summary>The channels the user chose (what the lineup is made from), before hiding the ones that do not work.</summary>
    public IReadOnlyList<Channel> ChosenChannels() => ChannelCatalog.BuildLineup(Settings.Playlists, Library);

    /// <summary>
    /// Checks, in the background, the channels that would be in the lineup and have not been checked lately.
    /// Channels found not to work make room for the next ones, which are checked in turn until the lineup is full.
    /// </summary>
    public void CheckChosenInBackground() => _ = Task.Run(async () =>
    {
        for (int round = 0; round < 10; round++)
        {
            // YouTube live links that are off air now stay in the check, so they come back when they go live again.
            var pool = ChannelCatalog.BuildLineup(Settings.Playlists, Library, Settings.HideUnavailable ? Health : null, maxChannels: Settings.MaxChannels)
                .Concat(ChosenChannels().Where(c => c.Kind == StreamKind.YouTube)).DistinctBy(c => c.Id).ToList();
            var (_, dead) = await CheckChannelsAsync(pool);
            if (dead == 0 || !Settings.HideUnavailable) break;
        }
    });

    public string? ErrorFor(string playlistId) => _errors.GetValueOrDefault(playlistId);

    /// <summary>The numbered lineup again, from the playlists and the user's selection. No download.</summary>
    public void RebuildLineup()
    {
        var next = ChannelCatalog.BuildLineup(Settings.Playlists, Library, Settings.HideUnavailable ? Health : null, onlyVerified: Settings.HideUnavailable, maxChannels: Settings.MaxChannels);
        // Nothing changed: do nothing (a rebuild redraws the home screen, which is slow with many channels).
        // Numbers follow the playlists: every chosen channel gets its number now, also the ones hidden for the moment.
        Lineup.Reserve(ChosenChannels());
        if (next.Select(c => c.Id).SequenceEqual(Lineup.Channels.Select(c => c.Id))) return;
        Lineup.Load(next);
        if (Lineup.Current is null && Lineup.Channels.Count > 0) Lineup.Restore(Settings.Lineup with { Favorites = Lineup.Export().Favorites, Recent = Lineup.Export().Recent });
        ChannelsChanged?.Invoke();
    }

    private void SaveLibraryCache()
    {
        try { JsonFile.Save(AppPaths.PlaylistCache, Library.ToDictionary(k => k.Key, k => k.Value.ToList())); } catch (Exception ex) { AppPaths.LogLine("cache: " + ex.Message); }
    }

    /// <summary>
    /// Downloads one playlist and keeps all its channels for the channel picker. Returns an error text, or null on success.
    /// Tries the direct download, then public DNS, then mirrors, then the system's own web client (the same settings as the browser).
    /// </summary>
    public async Task<string?> LoadPlaylistAsync(PlaylistEntry p)
    {
        try
        {
            var list = await ChannelCatalog.LoadOneAsync(p, Http, SystemDownload);
            Library[p.Id] = list;
            _errors.TryRemove(p.Id, out _);
            SaveLibraryCache();
            AppPaths.LogLine($"playlist: {p.Name}: {list.Count} channels");
            return null;
        }
        catch (Exception ex)
        {
            _errors[p.Id] = ex.Message;
            AppPaths.LogLine($"playlist: {p.Name}: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>Reloads every playlist. A playlist that fails keeps its old channels.</summary>
    public async Task RefreshChannelsAsync()
    {
        if (Settings.Playlists.Count == 0)
        {
            Library.Clear(); _errors.Clear(); PlaylistErrors = Array.Empty<string>();
            RebuildLineup();
            return;
        }
        ChannelsLoading = true; ChannelsChanged?.Invoke();
        try
        {
            foreach (var p in Settings.Playlists.ToList()) await LoadPlaylistAsync(p);
            PlaylistErrors = Settings.Playlists.Where(p => _errors.ContainsKey(p.Id)).Select(p => $"{p.Name}: {_errors[p.Id]}").ToList();
        }
        finally { ChannelsLoading = false; }
        RebuildLineup();
        CheckChosenInBackground();
        // The network may simply be slow to wake up (just started, Wi-Fi joining): try again once and then every few minutes.
        if (PlaylistErrors.Count > 0 && Library.Count < Settings.Playlists.Count && ++_retries <= 6)
        {
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(_retries == 1 ? 15 : 120)); await RefreshChannelsAsync(); });
        }
        else if (PlaylistErrors.Count == 0) _retries = 0;
    }

    public void RemovePlaylist(string id)
    {
        Settings.RemovePlaylist(id);
        Library.TryRemove(id, out _); _errors.TryRemove(id, out _);
        SaveSettings(); SaveLibraryCache(); RebuildLineup();
    }

    /// <summary>Same download as the browser would do (Windows proxy settings, certificates): used when our own download fails.</summary>
    private static async Task<string?> SystemDownload(string url, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; $r=Invoke-WebRequest -UseBasicParsing -Uri $env:KW_URL -TimeoutSec 60; if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { $r.Content }\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            psi.Environment["KW_URL"] = url;
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var read = proc.StandardOutput.ReadToEndAsync(ct);
            if (!await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(75), ct)).ContinueWith(x => x.Result == read)) { try { proc.Kill(); } catch { } return null; }
            return await read;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppPaths.LogLine("system download: " + ex.Message); return null; }
    }

    // ---------- channel finder (the phone): check every channel, list the ones that work, pick and star them

    /// <summary>The running (or last) scan of every channel.</summary>
    public ScanProgress Scan { get => _scan; private set { _scan = value; ScanChanged?.Invoke(value); } }
    private ScanProgress _scan = ScanProgress.Idle;
    /// <summary>The scan moved on (any thread).</summary>
    public event Action<ScanProgress>? ScanChanged;
    private CancellationTokenSource? _scanCts;

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public InlineProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }

    private static T OnUi<T>(Func<T> f)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        return d is null || d.CheckAccess() ? f() : d.Invoke(f);
    }

    /// <summary>
    /// Checks every channel of every playlist from this PC (thousands are fine: up to 32 at a time, results of the last
    /// day are reused unless [full]). Phones with the finder open see the progress and the working channels as they pass.
    /// </summary>
    public void StartScan(bool full)
    {
        if (Scan.Running) return;
        var all = ChannelFinder.All(Settings.Playlists, Library).Select(x => x.Channel).ToList();
        var cts = _scanCts = new CancellationTokenSource();
        Scan = new ScanProgress(true, 0, all.Count, 0, 0);
        Server?.NotifyScan(Scan);
        AppPaths.LogLine($"scan: {all.Count} channels{(full ? " (full)" : "")}");
        _ = Task.Run(async () =>
        {
            var lastSent = DateTime.MinValue;
            var gate = new object();
            ScanProgress Count(bool running, int done)
            {
                int ok = 0, dead = 0;
                foreach (var c in all)
                    if (Health.TryGetValue(c.Id, out var h)) { if (h.Status == HealthStatus.Ok) ok++; else if (h.Status == HealthStatus.Dead) dead++; }
                return new ScanProgress(running, Math.Min(done, all.Count), all.Count, ok, dead);
            }
            var progress = new InlineProgress<(int Done, int Total)>(p =>
            {
                lock (gate)
                {
                    if (DateTime.UtcNow - lastSent < TimeSpan.FromMilliseconds(700) && p.Done < p.Total) return;
                    lastSent = DateTime.UtcNow;
                }
                // Channels checked lately were skipped, so they count as done from the start.
                Scan = Count(true, all.Count - p.Total + p.Done);
                Server?.NotifyScan(Scan);
                Server?.NotifyCatalogChanged();
            });
            try { await CheckChannelsAsync(all, force: full, progress, cts.Token); }
            catch (Exception ex) { AppPaths.LogLine("scan: " + ex.Message); }
            Scan = Count(false, cts.IsCancellationRequested ? Scan.Done : all.Count);
            AppPaths.LogLine($"scan: done, {Scan.Ok} work, {Scan.Dead} do not");
            Server?.NotifyScan(Scan);
            OnUi(() => { RebuildLineup(); return 0; });
            Server?.NotifyCatalogChanged();
        });
    }

    public void StopScan() => _scanCts?.Cancel();

    /// <summary>The finder's list for a phone: only channels that work here, with chosen and favorite flags. Any thread.</summary>
    public string CatalogJson(string? requestId)
    {
        var playlists = OnUi(() => Settings.Playlists.ToList());
        var all = ChannelFinder.All(playlists, Library);
        var chosen = new HashSet<string>(Lineup.Channels.Select(c => c.Id));
        foreach (var p in playlists) if (p.Selected is not null) chosen.UnionWith(p.Selected);
        return ChannelFinder.CatalogMessage(all, Health, chosen.Contains, Lineup.IsFavorite, Scan, requestId);
    }

    /// <summary>Adds or removes channels (by id) to the lineup, from the playlist each comes from. UI thread.</summary>
    private bool ChooseMany(IEnumerable<(Channel Channel, PlaylistEntry Playlist)> picks, bool on)
    {
        var inLineup = Lineup.Channels.Select(c => c.Id).ToHashSet();
        var current = new Dictionary<string, List<string>>();
        bool changed = false;
        foreach (var (c, p) in picks)
        {
            if (!current.TryGetValue(p.Id, out var cur))
                current[p.Id] = cur = (Library.GetValueOrDefault(p.Id) ?? Array.Empty<Channel>()).Where(x => inLineup.Contains(x.Id)).Select(x => x.Id).ToList();
            changed |= Settings.SetChosen(p.Id, c.Id, on, cur);
        }
        if (changed) { SaveSettings(); RebuildLineup(); }
        Server?.NotifyCatalogChanged();
        return changed;
    }

    public void Choose(string channelId, bool on) =>
        ChooseMany(ChannelFinder.All(Settings.Playlists, Library).Where(x => x.Channel.Id == channelId).Take(1), on);

    /// <summary>Every working channel of a category at once.</summary>
    public void ChooseGroup(string group, bool on) =>
        ChooseMany(ChannelFinder.All(Settings.Playlists, Library).Where(x => x.Channel.Group == group && StatusOf(x.Channel) == HealthStatus.Ok), on);

    /// <summary>A star from the phone: a favorite is also put in the lineup, so it shows under Favorites on the TV.</summary>
    public void Favorite(string channelId, bool on)
    {
        if (on) Choose(channelId, true);
        if (!Lineup.SetFavorite(channelId, on)) Server?.NotifyCatalogChanged();
    }

    /// <summary>The port the remote server is actually listening on (may differ from the preferred one).</summary>
    public int ActivePort { get; private set; }

    /// <summary>
    /// Starts the LAN remote server and mDNS advertising. Never throws; RemoteStatus says what happened.
    /// If the port is taken or reserved by Windows (Hyper-V/WSL), the next free port from the list is used.
    /// </summary>
    public Task StartRemoteAsync()
    {
        var name = $"KwartoTV on {Environment.MachineName}";
        var failures = new List<string>();
        foreach (var port in RemoteServer.PortsToTry(Settings.Port))
        {
            var server = new RemoteServer($"http://+:{port}/", Pairing, Router, Lineup) { ServerName = name, OnAir = ch => OnAir(ch).Now, Catalog = CatalogJson, PendingText = () => OnUi(() => KwartoTV.App.Ui.TextBridge.Pending),
                MusicNow = () => MusicPlayer.State.On ? Music.StateMessage(MusicPlayer.State) : null };
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                _ = server.DisposeAsync();
                failures.Add($"{port}: " + (ex is SocketException se ? se.SocketErrorCode.ToString() : ex.Message));
                continue;
            }
            Server = server;
            ActivePort = port;
            KwartoTV.App.Ui.TextBridge.Changed += msg => Server?.NotifyText(msg);
            RemoteStatus = $"Remote is on (port {port})";
            AppPaths.LogLine($"remote: listening on port {port}" + (failures.Count > 0 ? " (not free: " + string.Join(", ", failures) + ")" : ""));
            if (Settings.Port != port) { Settings.Port = port; SaveSettings(); }
            break;
        }
        if (Server is null)
        {
            RemoteStatus = "Remote is OFF: no free port (" + string.Join(", ", failures) + ").";
            AppPaths.LogLine("remote: " + RemoteStatus);
            return Task.CompletedTask;
        }
        Mdns = new MdnsResponder(name, Environment.MachineName.ToLowerInvariant(), ActivePort);
        if (!Mdns.Start()) { AppPaths.LogLine("mdns: " + Mdns.Error); }
        else AppPaths.LogLine("mdns: advertising " + string.Join(", ", MdnsResponder.LocalAddresses()));
        return Task.CompletedTask;
    }

    /// <summary>LAN addresses a phone could use, best guess first (real adapter with a gateway, virtual adapters last).</summary>
    public static IReadOnlyList<string> LanAddresses() => MdnsResponder.LocalAddresses().Select(a => a.ToString()).ToList();

    public void Dispose()
    {
        SaveSettings();
        Mdns?.Dispose();
        if (Server is not null) Server.DisposeAsync().AsTask().Wait(2000);
    }
}
