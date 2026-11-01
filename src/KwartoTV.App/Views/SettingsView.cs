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

    // Rarely used options stay folded away until "More options" is pressed.
    private bool _more;

    private void Build()
    {
        Body.Children.Add(new TextBlock { Text = "Settings", FontSize = 40, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        var st = Tv.Settings;

        // ---- channels
        Body.Children.Add(UiKit.Heading("Channels"));
        foreach (var p in st.Playlists.ToList())
        {
            var pl = p;
            var total = Tv.Library.GetValueOrDefault(pl.Id)?.Count;
            var chosen = pl.IsStream ? 1 : pl.Selected?.Count ?? total ?? 0;
            var chosenList = Tv.Library.TryGetValue(pl.Id, out var lib) ? lib.Where(c => pl.IsStream || pl.Selected is null || pl.Selected.Contains(c.Id)).ToList() : new List<Channel>();
            int deadCount = chosenList.Count(c => Tv.StatusOf(c) == HealthStatus.Dead), okCount = chosenList.Count(c => Tv.StatusOf(c) == HealthStatus.Ok);
            var info = pl.IsStream ? "One stream" : total is null ? "Not downloaded yet" : $"{chosen} of {total} channels chosen";
            if (chosenList.Count > 0) info += $"  ·  {okCount} work, {deadCount} do not";
            var err = Tv.ErrorFor(pl.Id);
            var actions = new List<(string, Action, bool)>();
            if (!pl.IsStream) actions.Add(("Choose channels", () => { if (Tv.Library.ContainsKey(pl.Id)) Shell.ShowChannelPicker(pl.Id); else Shell.ShowToast(err is null ? "This playlist is not downloaded yet. Use Reload channels." : "This playlist did not download. Use Reload channels."); }, false));
            if (_more && !pl.IsStream) actions.Add((pl.ShowAsSpace ? "Own space: On" : "Own space: Off", () => { st.SetSpace(pl.Id, !pl.ShowAsSpace); Tv.SaveSettings(); Refresh(); }, false));
            actions.Add(("Remove", () => Dialogs.Confirm(Shell, $"Remove {pl.Name} and its channels?", "Remove", () => { Tv.RemovePlaylist(pl.Id); Refresh(); }), true));
            Body.Children.Add(ItemRow(pl.Name, err is null ? $"{info}  ·  {pl.Source}" : "⚠ " + err, actions.ToArray()));
        }
        foreach (var err in Tv.PlaylistErrors) Body.Children.Add(new TextBlock { Text = "⚠ " + err, FontSize = 17, Foreground = UiKit.B("Danger"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        Body.Children.Add(Buttons(
            UiKit.ActionButton("Add channels…", Shell.AddChannelsMenu),
            UiKit.ActionButton(Tv.Scan.Running ? $"Checking… {Tv.Scan.Done:N0} of {Tv.Scan.Total:N0}" : "Scan all channels", () => { if (Tv.Scan.Running) Tv.StopScan(); else Tv.StartScan(full: false); Refresh(); }),
            UiKit.ActionButton(Tv.ChannelsLoading ? "Loading…" : "Reload channels", () => _ = Tv.RefreshChannelsAsync())));
        if (_more)
            Body.Children.Add(Buttons(
                UiKit.ActionButton(st.HideUnavailable ? "Channels that do not work: hidden" : "Channels that do not work: shown", () => { st.HideUnavailable = !st.HideUnavailable; Tv.SaveSettings(); Tv.RebuildLineup(); Refresh(); }),
                UiKit.ActionButton(st.MaxChannels > 0 ? $"Most channels: {st.MaxChannels}" : "Most channels: all", () =>
                {
                    st.MaxChannels = st.MaxChannels switch { 100 => 200, 200 => 300, 300 => 0, _ => 100 };
                    Tv.SaveSettings(); Tv.RebuildLineup(); Tv.CheckChosenInBackground(); Refresh();
                }),
                // Numbers stay put like on cable (a hidden channel leaves a gap); this closes the gaps: 1, 2, 3...
                UiKit.ActionButton("Renumber channels", () => Dialogs.Confirm(Shell, "Number the channels 1, 2, 3… again, closing the gaps? Channel numbers you know will change.", "Renumber", () =>
                {
                    Tv.Lineup.Renumber(); Tv.SaveSettings(); Refresh(); Shell.ShowToast("Channels numbered 1, 2, 3… again.");
                }))));

        // ---- phone remote
        Body.Children.Add(UiKit.Heading("Phone remote"));
        var devices = Tv.Pairing.Devices;
        if (devices.Count == 0) Body.Children.Add(UiKit.Dimmed("No phone paired yet.", 18));
        foreach (var d in devices)
        {
            var dev = d;
            Body.Children.Add(ItemRow(dev.Name + (dev.Blocked ? "  (blocked)" : ""), $"Paired {dev.PairedAt.LocalDateTime:d MMM yyyy}",
                (dev.Blocked ? "Unblock" : "Block", () => { Tv.Pairing.SetBlocked(dev.Id, !dev.Blocked); Refresh(); }, false),
                ("Remove", () => Dialogs.Confirm(Shell, $"Remove {dev.Name}? It will need a new code to connect again.", "Remove", () => { Tv.Pairing.Revoke(dev.Id); Refresh(); }), true)));
        }
        Body.Children.Add(Buttons(UiKit.ActionButton("Pair a phone", () => Dialogs.Pair(Shell))));
        if (_more)
        {
            Body.Children.Add(Buttons(UiKit.ActionButton("Fix network access", () => Dialogs.FixNetwork(Shell, Tv.ActivePort > 0 ? Tv.ActivePort : st.Port))));
            var addr = TvServices.LanAddresses().FirstOrDefault();
            Body.Children.Add(UiKit.Dimmed($"{Tv.RemoteStatus}" + (addr is null ? "" : $"  ·  Browser remote: http://{addr}:{Tv.ActivePort}"), 17));
        }

        // ---- lock screen
        Body.Children.Add(UiKit.Heading("Lock screen and screensaver"));
        Body.Children.Add(Buttons(
            UiKit.ActionButton($"Screensaver: {AppSettings.DescribeScreensaver(st.ScreensaverMinutes)}", () =>
            {
                var c = AppSettings.ScreensaverChoices;
                var i = Array.IndexOf(c, st.ScreensaverMinutes);
                st.ScreensaverMinutes = c[(i + 1) % c.Length];
                Tv.SaveSettings(); Refresh();
            }),
            UiKit.ActionButton($"Bible verse: {Verses.Describe(st.VerseEveryMinutes)}", () => { st.VerseEveryMinutes = Verses.Next(st.VerseEveryMinutes); Tv.SaveSettings(); Refresh(); }),
            UiKit.ActionButton(st.SlideshowOn ? "Photo slideshow: On" : "Photo slideshow: Off", () =>
            {
                if (!st.SlideshowOn && string.IsNullOrWhiteSpace(st.SlideshowFolder))
                {
                    var picked = Dialogs.PickFolder();
                    if (picked is null) return;
                    st.SlideshowFolder = picked;
                }
                st.SlideshowOn = !st.SlideshowOn; Tv.SaveSettings(); Refresh();
            })));
        if (st.SlideshowOn)
        {
            var count = Slideshow.Photos(st.SlideshowFolder, 0).Count;
            Body.Children.Add(Buttons(
                UiKit.ActionButton(string.IsNullOrWhiteSpace(st.SlideshowFolder) ? "Choose photo folder" : $"Photos: {Path.GetFileName(st.SlideshowFolder.TrimEnd('\\', '/'))} ({count})", () =>
                {
                    var picked = Dialogs.PickFolder();
                    if (picked is null) return;
                    st.SlideshowFolder = picked; Tv.SaveSettings(); Refresh();
                }),
                UiKit.ActionButton($"Each photo: {st.SlideshowSeconds} s", () =>
                {
                    var i = Array.IndexOf(AppSettings.SlideshowChoices, st.SlideshowSeconds);
                    st.SlideshowSeconds = AppSettings.SlideshowChoices[(i + 1) % AppSettings.SlideshowChoices.Length]; Tv.SaveSettings(); Refresh();
                })));
        }

        // ---- websites and videos
        Body.Children.Add(UiKit.Heading("Websites and videos"));
        foreach (var t in st.WebTiles.ToList())
            Body.Children.Add(ItemRow(t.Name, t.Url, ("Remove", () => { st.RemoveWebTile(t.Id); Tv.SaveSettings(); Refresh(); }, true)));
        foreach (var f in st.LibraryFolders.ToList())
            Body.Children.Add(ItemRow(Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } fn ? fn : f, f, ("Remove", () => { st.LibraryFolders.Remove(f); Tv.SaveSettings(); Refresh(); }, true)));
        Body.Children.Add(Buttons(
            UiKit.ActionButton("Add website", AddWebsite),
            UiKit.ActionButton("Add video folder", () =>
            {
                var path = Dialogs.PickFolder();
                if (path is null) return;
                if (!st.LibraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) st.LibraryFolders.Add(path);
                Tv.SaveSettings(); Refresh();
            })));
        if (_more)
            Body.Children.Add(Buttons(
                UiKit.ActionButton(st.NetflixPreferApp ? "Netflix: the Netflix app when installed" : "Netflix: inside KwartoTV (phone controls it)", () => { st.NetflixPreferApp = !st.NetflixPreferApp; st.NetflixInApp = true; Tv.SaveSettings(); Refresh(); }),
                UiKit.ActionButton($"YouTube picture: {AppSettings.YouTubeRenderModes[Math.Clamp(st.YouTubeRenderMode, 0, 2)]}", () =>
                {
                    st.YouTubeRenderMode = (st.YouTubeRenderMode + 1) % AppSettings.YouTubeRenderModes.Length;
                    Tv.SaveSettings(); Refresh();
                    // YouTube and the music share one browser: it starts again with the new picture setting.
                    Tv.MusicPlayer.Stop(); KwartoTV.App.Services.WebAppWindow.YouTubeRenderMode = st.YouTubeRenderMode;
                    Shell.ShowToast("Applies the next time YouTube opens. Black boxes behind text or icons? Use Text fix, or Safe.");
                })));

        // ---- system
        Body.Children.Add(UiKit.Heading("System"));
        var startup = StartupEnabled();
        var upd = Tv.Updater.Available;
        Body.Children.Add(Buttons(
            UiKit.ActionButton(upd is null ? "Check for updates" : $"Install update {upd.Version}", () => { if (upd is null) _ = Shell.CheckForUpdateAsync(true); else Dialogs.UpdateDialog(Shell, upd, Tv.Updater); }),
            UiKit.ActionButton(startup ? "Start with Windows: On" : "Start with Windows: Off", () => { SetStartup(!startup); Refresh(); }),
            UiKit.ActionButton("Power…", () => Dialogs.PowerMenu(Shell), danger: true)));
        Body.Children.Add(Buttons(UiKit.ActionButton(_more ? "Fewer options ▴" : "More options ▾", () => { _more = !_more; Refresh(); })));
        Body.Children.Add(UiKit.Dimmed($"KwartoTV {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)}" + (_more ? $"  ·  data in {AppPaths.Root}" : ""), 15));
    }

    private static FrameworkElement Buttons(params UIElement[] buttons)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    private static FrameworkElement ItemRow(string title, string? sub, params (string Label, Action Do, bool Danger)[] actions)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 260 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        // One line each, cut with "…" (a long link wrapped into many lines next to the buttons).
        if (sub is not null)
            foreach (var part in SplitSource(sub))
                text.Children.Add(new TextBlock { Text = part, FontSize = 16, Foreground = UiKit.B("Dim"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        g.Children.Add(text);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, act, danger) in actions) btns.Children.Add(UiKit.ActionButton(label, act, danger));
        Grid.SetColumn(btns, 1);
        g.Children.Add(btns);
        return new Border { Background = UiKit.B("Surface"), BorderBrush = UiKit.B("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 14, 14, 2), Margin = new Thickness(0, 10, 0, 0), Child = g };
    }

    /// <summary>"info  ·  info  ·  https://… or C:\…": the link or file on a line of its own.</summary>
    private static IEnumerable<string> SplitSource(string sub)
    {
        var i = sub.LastIndexOf("  ·  ", StringComparison.Ordinal);
        if (i > 0 && sub[(i + 5)..] is var tail && (tail.Contains('/') || tail.Contains('\\'))) return new[] { sub[..i], tail };
        return new[] { sub };
    }

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
