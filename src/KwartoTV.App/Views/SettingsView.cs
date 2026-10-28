using System.Windows;
using System.Windows.Controls;
using KwartoTV.App.Services;
using KwartoTV.App.Ui;
using KwartoTV.Core;
using Microsoft.Win32;

namespace KwartoTV.App.Views;

public sealed class SettingsView : PageBase
{
    public SettingsView(MainWindow shell) : base(shell) { }

    public override void Refresh() => Rebuild(Build);

    private TvServices Tv => Shell.Tv;

    private void Build()
    {
        Body.Children.Add(new TextBlock { Text = "Settings", FontSize = 40, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });

        // ---- playlists
        Body.Children.Add(UiKit.Heading("Playlists"));
        Body.Children.Add(UiKit.Dimmed("KwartoTV has no built-in channels. Add .m3u / .m3u8 links or files you are allowed to use.", 18));
        foreach (var p in Tv.Settings.Playlists.ToList())
            Body.Children.Add(ItemRow($"{p.Name}", p.Source, ("Remove", () => { Tv.Settings.RemovePlaylist(p.Id); Tv.SaveSettings(); _ = Tv.RefreshChannelsAsync(); Refresh(); }, true)));
        foreach (var err in Tv.PlaylistErrors) Body.Children.Add(new TextBlock { Text = "⚠ " + err, FontSize = 17, Foreground = UiKit.B("Danger"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        Body.Children.Add(Buttons(
            UiKit.ActionButton("Add from iptv-org", Shell.AddIptvOrgPlaylist),
            UiKit.ActionButton("Add playlist link/file", AddPlaylist),
            UiKit.ActionButton(Tv.ChannelsLoading ? "Loading…" : $"Reload channels ({Tv.Lineup.Channels.Count})", () => _ = Tv.RefreshChannelsAsync())));

        // ---- website tiles
        Body.Children.Add(UiKit.Heading("Website tiles"));
        foreach (var t in Tv.Settings.WebTiles.ToList())
            Body.Children.Add(ItemRow(t.Name, t.Url, ("Remove", () => { Tv.Settings.RemoveWebTile(t.Id); Tv.SaveSettings(); Refresh(); }, true)));
        Body.Children.Add(Buttons(UiKit.ActionButton("Add website tile", AddWebsite)));

        // ---- paired devices
        Body.Children.Add(UiKit.Heading("Paired devices"));
        var devices = Tv.Pairing.Devices;
        if (devices.Count == 0) Body.Children.Add(UiKit.Dimmed("No phones paired yet.", 18));
        foreach (var d in devices)
        {
            var dev = d;
            Body.Children.Add(ItemRow(dev.Name + (dev.Blocked ? "  (blocked)" : ""), $"Paired {dev.PairedAt.LocalDateTime:d MMM yyyy, h:mm tt}",
                (dev.Blocked ? "Unblock" : "Block", () => { Tv.Pairing.SetBlocked(dev.Id, !dev.Blocked); Refresh(); }, false),
                ("Revoke", () => Dialogs.Confirm(Shell, $"Remove {dev.Name}? It will need a new code to connect again.", "Revoke", () => { Tv.Pairing.Revoke(dev.Id); Refresh(); }), true)));
        }
        Body.Children.Add(Buttons(UiKit.ActionButton("Pair new device", () => Dialogs.Pair(Shell)), UiKit.ActionButton("Fix network access", () => Dialogs.FixNetwork(Shell, Tv.ActivePort > 0 ? Tv.ActivePort : Tv.Settings.Port))));
        var addr = TvServices.LanAddresses().FirstOrDefault();
        Body.Children.Add(UiKit.Dimmed($"{Tv.RemoteStatus}" + (addr is null ? "" : $"  ·  Browser remote: http://{addr}:{Tv.ActivePort}"), 17));

        // ---- library
        Body.Children.Add(UiKit.Heading("Library folders"));
        foreach (var f in Tv.Settings.LibraryFolders.ToList())
            Body.Children.Add(ItemRow(f, null, ("Remove", () => { Tv.Settings.LibraryFolders.Remove(f); Tv.SaveSettings(); Refresh(); }, true)));
        Body.Children.Add(Buttons(UiKit.ActionButton("Add folder", () =>
        {
            var path = Dialogs.PickFolder();
            if (path is null) return;
            if (!Tv.Settings.LibraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) Tv.Settings.LibraryFolders.Add(path);
            Tv.SaveSettings(); Refresh();
        })));

        // ---- system
        Body.Children.Add(UiKit.Heading("System"));
        var startup = StartupEnabled();
        Body.Children.Add(Buttons(UiKit.ActionButton(Tv.Settings.NetflixInApp ? "Netflix opens: inside KwartoTV" : "Netflix opens: Microsoft Store app", () => { Tv.Settings.NetflixInApp = !Tv.Settings.NetflixInApp; Tv.SaveSettings(); Refresh(); })));
        Body.Children.Add(Buttons(
            UiKit.ActionButton(startup ? "Start with Windows: On" : "Start with Windows: Off", () => { SetStartup(!startup); Refresh(); }),
            UiKit.ActionButton("Exit KwartoTV", () => Dialogs.Confirm(Shell, "Close KwartoTV?", "Exit", () => Application.Current.Shutdown()), danger: true)));
        Body.Children.Add(UiKit.Dimmed($"KwartoTV {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)}  ·  data in {AppPaths.Root}", 15));
    }

    private static FrameworkElement Buttons(params UIElement[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    private static FrameworkElement ItemRow(string title, string? sub, params (string Label, Action Do, bool Danger)[] actions)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        if (sub is not null) text.Children.Add(UiKit.Dimmed(sub, 16));
        g.Children.Add(text);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, act, danger) in actions) btns.Children.Add(UiKit.ActionButton(label, act, danger));
        Grid.SetColumn(btns, 1);
        g.Children.Add(btns);
        return new Border { Background = UiKit.B("Surface"), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 10, 12, 10), Child = g };
    }

    private void AddPlaylist() => Dialogs.Form(Shell, "Add playlist",
        "Paste an https link to an .m3u / .m3u8 list, or choose a file on this PC.",
        new[] { new Dialogs.Field("Name (optional)"), new Dialogs.Field("Link or file", "https://", Dialogs.PickPlaylistFile) },
        "Add", v =>
        {
            var err = Tv.Settings.AddPlaylist(v[0], v[1]);
            if (err is not null) return err;
            Tv.SaveSettings();
            _ = Tv.RefreshChannelsAsync();
            Shell.ShowToast("Playlist added. Loading channels…");
            Refresh();
            return null;
        });

    private void AddWebsite() => Dialogs.Form(Shell, "Add website tile",
        "The site opens fullscreen in its own window. Good for any https site that works with a keyboard.",
        new[] { new Dialogs.Field("Name"), new Dialogs.Field("Address", "https://") },
        "Add", v =>
        {
            var err = Tv.Settings.AddWebTile(v[0], v[1]);
            if (err is not null) return err;
            Tv.SaveSettings(); Refresh();
            return null;
        });

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool StartupEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("KwartoTV") is string; } catch { return false; }
    }

    private static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("KwartoTV", $"\"{Environment.ProcessPath}\""); else k.DeleteValue("KwartoTV", false);
        }
        catch (Exception ex) { AppPaths.LogLine("startup: " + ex.Message); }
    }
}
