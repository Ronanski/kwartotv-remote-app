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
    public Updater Updater { get; } = new();
    public CommandRouter Router { get; private set; } = null!;
    public RemoteServer? Server { get; private set; }
    public MdnsResponder? Mdns { get; private set; }
    public string RemoteStatus { get; private set; } = "Starting…";
    public IReadOnlyList<string> PlaylistErrors { get; private set; } = Array.Empty<string>();
    public bool ChannelsLoading { get; private set; }
    private int _retries;

    public event Action? ChannelsChanged;

    public TvServices(IHostActions host)
    {
        AppPaths.Ensure();
        Settings = JsonFile.LoadOrNew<AppSettings>(AppPaths.Settings);
        try { if (File.Exists(AppPaths.Devices)) Pairing.Import(File.ReadAllText(AppPaths.Devices)); } catch (Exception ex) { AppPaths.LogLine("devices: " + ex.Message); }
        Pairing.Changed += SaveDevices;

        // Start with what was loaded last time, so the channels are there even before (or without) the internet.
        foreach (var kv in JsonFile.LoadOrNew<Dictionary<string, List<Channel>>>(AppPaths.PlaylistCache)) Library[kv.Key] = kv.Value;
        foreach (var kv in JsonFile.LoadOrNew<Dictionary<string, ChannelHealth>>(AppPaths.Health)) Health[kv.Key] = kv.Value;
        Lineup.Load(ChannelCatalog.BuildLineup(Settings.Playlists, Library, Settings.HideUnavailable ? Health : null, onlyVerified: Settings.HideUnavailable));
        Lineup.Restore(Settings.Lineup);
        Lineup.Tuned += (_, _) => SaveSettingsSoon();
        Lineup.FavoritesChanged += SaveSettingsSoon;
        Router = new CommandRouter(Lineup, host);
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
    public Dictionary<string, IReadOnlyList<Channel>> Library { get; } = new();
    private readonly Dictionary<string, string> _errors = new();

    /// <summary>Whether each channel works on this PC (checked here, so country blocks are seen as they are for this connection).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, ChannelHealth> Health { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _checking = new();
    private static readonly HttpClient ProbeHttp = ResilientHttp.Create(TimeSpan.FromSeconds(30));
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(24);

    public HealthStatus StatusOf(Channel c) => Health.TryGetValue(c.Id, out var h) ? h.Status : HealthStatus.Unknown;

    /// <summary>Remembers what happened when a channel was played (the player knows best) and saves it.</summary>
    public void MarkHealth(Channel c, HealthStatus status, string? reason)
    {
        Health[c.Id] = new ChannelHealth(status, reason, DateTimeOffset.UtcNow);
        SaveHealthSoon();
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
        var now = DateTimeOffset.UtcNow;
        var todo = channels.Where(c => force || !Health.TryGetValue(c.Id, out var h) || now - h.CheckedAt > RecheckAfter)
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
                else if (h.Status == HealthStatus.Ok) Interlocked.Increment(ref ok);
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
        var lineup = Lineup.Channels;
        var number = new Dictionary<string, int>();
        for (int i = 0; i < lineup.Count; i++) number[lineup[i].Id] = i + 1;
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

    /// <summary>The channels the user chose (what the lineup is made from), before hiding the ones that do not work.</summary>
    public IReadOnlyList<Channel> ChosenChannels() => ChannelCatalog.BuildLineup(Settings.Playlists, Library);

    /// <summary>Checks the chosen channels that have not been checked lately, in the background.</summary>
    public void CheckChosenInBackground() => _ = Task.Run(() => CheckChannelsAsync(ChosenChannels()));

    public string? ErrorFor(string playlistId) => _errors.GetValueOrDefault(playlistId);

    /// <summary>The numbered lineup again, from the playlists and the user's selection. No download.</summary>
    public void RebuildLineup()
    {
        var next = ChannelCatalog.BuildLineup(Settings.Playlists, Library, Settings.HideUnavailable ? Health : null, onlyVerified: Settings.HideUnavailable);
        // Nothing changed: do nothing (a rebuild redraws the home screen, which is slow with many channels).
        if (next.Select(c => c.Id).SequenceEqual(Lineup.Channels.Select(c => c.Id))) return;
        Lineup.Load(next);
        if (Lineup.Current is null && Lineup.Channels.Count > 0) Lineup.Restore(Settings.Lineup with { Favorites = Lineup.Export().Favorites });
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
            _errors.Remove(p.Id);
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
        Library.Remove(id); _errors.Remove(id);
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
            var server = new RemoteServer($"http://+:{port}/", Pairing, Router, Lineup) { ServerName = name };
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
