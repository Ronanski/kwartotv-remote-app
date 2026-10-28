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

        var cached = JsonFile.LoadOrNew<List<Channel>>(AppPaths.ChannelCache);
        Lineup.Load(cached);
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

    /// <summary>Downloads and parses every playlist the user added. Keeps the old list if nothing could be loaded.</summary>
    public async Task RefreshChannelsAsync()
    {
        if (Settings.Playlists.Count == 0)
        {
            Lineup.Load(Array.Empty<Channel>());
            PlaylistErrors = Array.Empty<string>();
            ChannelsChanged?.Invoke();
            return;
        }
        ChannelsLoading = true; ChannelsChanged?.Invoke();
        try
        {
            var result = await ChannelCatalog.LoadAllAsync(Settings.Playlists, Http);
            PlaylistErrors = result.Errors;
            if (result.Channels.Count > 0 || result.Errors.Count == 0)
            {
                Lineup.Load(result.Channels);
                if (Lineup.Current is null) Lineup.Restore(Settings.Lineup with { Favorites = Lineup.Export().Favorites });
                try { JsonFile.Save(AppPaths.ChannelCache, result.Channels); } catch { }
            }
        }
        catch (Exception ex) { PlaylistErrors = new[] { ex.Message }; }
        finally { ChannelsLoading = false; ChannelsChanged?.Invoke(); }
        // The network may simply be slow to wake up (just started, Wi-Fi joining): try again once and then every few minutes.
        if (PlaylistErrors.Count > 0 && Lineup.Channels.Count == 0 && ++_retries <= 6)
        {
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(_retries == 1 ? 15 : 120)); await RefreshChannelsAsync(); });
        }
        else if (PlaylistErrors.Count == 0) _retries = 0;
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
