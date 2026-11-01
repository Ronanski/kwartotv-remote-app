using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KwartoTV.App.Ui;
using KwartoTV.Core;

namespace KwartoTV.App.Views;

/// <summary>Netflix-style home: a billboard, then rows of apps, favorites and channel groups.</summary>
public sealed class HomeView : PageBase
{
    private const int MaxPerRow = 40, MaxRows = 12;

    private TextBlock? _scanText;
    private Button? _scanButton;

    public HomeView(MainWindow shell) : base(shell)
    {
        shell.Tv.ScanChanged += s => Dispatcher.BeginInvoke(() => ShowScan(s));
    }

    private void ShowScan(ScanProgress s)
    {
        if (_scanText is null || _scanButton is null) return;
        _scanText.Text = s.Running
            ? $"Checking every channel from this PC… {s.Done:N0} of {s.Total:N0}  ·  {s.Ok:N0} work  ·  {s.Dead:N0} do not"
            : s.Total > 0
                ? $"Last scan: {s.Ok:N0} channels work, {s.Dead:N0} do not. Channels that do not work are hidden. Pick working ones on the phone: Channels → Find channels."
                : "Find out which of your channels really play here: the scan checks every channel of every playlist from this PC.";
        if (_scanButton.Content is TextBlock t) t.Text = s.Running ? "■  Stop scan" : s.Total > 0 ? "🔍  Scan again" : "🔍  Scan all channels";
    }

    public override void Refresh() => Rebuild(Build);

    private void Build()
    {
        var tv = Shell.Tv;
        var lineup = tv.Lineup;
        var channels = lineup.Channels;
        var numbers = new Dictionary<string, int>();
        foreach (var (n, c) in lineup.Numbered) numbers[c.Id] = n;

        if (tv.Updater.Available is { } upd)
        {
            var banner = UiKit.ActionButton($"⬆  Update {upd.Version} is available. Install", () => Dialogs.UpdateDialog(Shell, upd, tv.Updater));
            banner.Margin = new Thickness(0, 6, 0, 0);
            Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { banner } });
        }
        Body.Children.Add(Hero(channels.Count, lineup.Current, lineup.CurrentNumber, tv.ChannelsLoading));

        var apps = new List<UIElement>
        {
            UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Live TV", "▶", Color.FromRgb(0x7A, 0x2E, 0x14), Color.FromRgb(0xE8, 0x6A, 0x3A)), Shell.ShowLiveTv, "Live TV"),
            UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("YouTube", "▷", Color.FromRgb(0x5E, 0x10, 0x14), Color.FromRgb(0xC4, 0x1E, 0x24)), Shell.LaunchYouTube, "YouTube"),
            UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Netflix", "N", Color.FromRgb(0x1A, 0x06, 0x08), Color.FromRgb(0x8E, 0x10, 0x18)), Shell.LaunchNetflix, "Netflix"),
            UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Bilibili", "◉", Color.FromRgb(0x0A, 0x3A, 0x5C), Color.FromRgb(0x00, 0xA1, 0xD6), "anime · dramas"), Shell.LaunchBilibili, "Bilibili"),
            UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Library", "▤", Color.FromRgb(0x14, 0x2A, 0x4A), Color.FromRgb(0x2A, 0x5E, 0xA8)), Shell.ShowLibrary, "Library"),
        };
        foreach (var (space, chs) in tv.Spaces())
        {
            var sp = space;
            apps.Insert(1, UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard(sp.Name, "📺", Color.FromRgb(0x0E, 0x2A, 0x5C), Color.FromRgb(0xC8, 0x2A, 0x2A), $"{chs.Count} channels"), () => Shell.OpenSpace(sp.Id), sp.Name));
        }
        foreach (var site in tv.Settings.WebTiles)
        {
            var s = site;
            apps.Add(UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard(s.Name, "◎", UiKit.Hue(s.Name), UiKit.Hue(s.Url), new Uri(s.Url).Host), () => Shell.LaunchWeb(s), s.Name));
        }
        // More apps: the ones installed on this PC show by themselves, plus the ones the user put here.
        foreach (var app in AppCatalog.All.Where(a => tv.InstalledApps.ContainsKey(a.Id) || tv.Settings.HomeApps.Contains(a.Id)))
        {
            var a = app;
            apps.Add(UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard(a.Name, a.Icon, Rgb(a.From), Rgb(a.To), tv.InstalledApps.ContainsKey(a.Id) ? a.Tagline : a.Tagline + " · web"), () => Shell.OpenCatalogApp(a), a.Name));
        }
        apps.Add(UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("More apps", "⊞", Color.FromRgb(0x1E, 0x24, 0x30), Color.FromRgb(0x3A, 0x42, 0x58), "Prime · Disney+ · Viu…"), () => Dialogs.MoreApps(Shell), "More apps"));
        apps.Add(UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Add website", "+", Color.FromRgb(0x1E, 0x24, 0x30), Color.FromRgb(0x2B, 0x31, 0x40)), Shell.ShowSettings, "Add website"));
        Body.Children.Add(UiKit.Row("Apps", apps));

        // The channel scan, right on the TV: which channels really play from this PC.
        if (tv.Settings.Playlists.Count > 0)
        {
            _scanText = UiKit.Dimmed("", 18);
            _scanText.TextWrapping = TextWrapping.Wrap; _scanText.VerticalAlignment = VerticalAlignment.Center; _scanText.Margin = new Thickness(18, 0, 0, 0);
            _scanButton = UiKit.ActionButton("🔍  Scan all channels", () => { if (Shell.Tv.Scan.Running) Shell.Tv.StopScan(); else Shell.Tv.StartScan(full: false); });
            var scanRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8), LastChildFill = true };
            DockPanel.SetDock(_scanButton, Dock.Left);
            scanRow.Children.Add(_scanButton); scanRow.Children.Add(_scanText);
            Body.Children.Add(scanRow);
            ShowScan(tv.Scan);
        }

        // Each playlist the user gave its own space gets its own row right under the apps.
        foreach (var (space, chs) in tv.Spaces())
            if (chs.Count > 0) Body.Children.Add(UiKit.Row("📺  " + space.Name, chs.Take(MaxPerRow).Select(x => Card(x.Channel, x.Number, lineup.IsFavorite(x.Channel.Id)))));

        // The channels watched lately, newest first: one press back to them.
        var recent = lineup.Recent;
        if (recent.Count > 1) Body.Children.Add(UiKit.Row("Recently watched", recent.Select(c => Card(c, numbers.GetValueOrDefault(c.Id), lineup.IsFavorite(c.Id)))));

        var favs = lineup.Favorites;
        if (favs.Count > 0) Body.Children.Add(UiKit.Row("My favorites", favs.Take(MaxPerRow).Select(c => Card(c, numbers.GetValueOrDefault(c.Id), true))));

        int rows = 0;
        foreach (var g in lineup.Groups)
        {
            if (rows++ >= MaxRows) break;
            var inGroup = lineup.InGroup(g);
            Body.Children.Add(UiKit.Row(inGroup.Count > MaxPerRow ? $"{g}  ·  {inGroup.Count}" : g, inGroup.Take(MaxPerRow).Select(c => Card(c, numbers.GetValueOrDefault(c.Id), lineup.IsFavorite(c.Id)))));
        }

        if (channels.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
            if (tv.CheckingCount > 0)
                empty.Children.Add(UiKit.Dimmed($"Checking channels from this PC… {tv.CheckingCount} to go. Channels that work appear here as they pass.", 22));
            else empty.Children.Add(UiKit.Dimmed(tv.ChannelsLoading
                ? "Loading your channels…"
                : tv.Settings.Playlists.Count == 0
                    ? "No channels yet. KwartoTV comes with no channel list: add a playlist link or file (or one stream), then choose the channels you want."
                    : tv.Library.Count > 0 ? "You have not chosen any channels yet. Open Settings and press Choose channels on a playlist."
                    : "Your playlists have not been downloaded. Check them in Settings.", 22));
            if (!tv.ChannelsLoading && tv.PlaylistErrors.Count > 0)
            {
                empty.Children.Add(new TextBlock { Text = "⚠ " + tv.PlaylistErrors[0], FontSize = 18, Foreground = UiKit.B("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                empty.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), Children = { UiKit.ActionButton("Try again", () => _ = tv.RefreshChannelsAsync()) } });
            }
            if (!tv.ChannelsLoading) empty.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0), Children = { UiKit.ActionButton("⭐ Free channel pack", Shell.AddFreePack), UiKit.ActionButton("Add channels", Shell.AddChannelsMenu) } });
            Body.Children.Add(empty);
        }
    }

    private static Color Rgb(uint c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);

    private FrameworkElement Card(Channel c, int number, bool fav) =>
        UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.ChannelCard(c, number, fav), () => Shell.TuneChannel(c), $"{number} {c.Name}", key: "ch:" + c.Id);

    private FrameworkElement Hero(int count, Channel? current, int number, bool loading)
    {
        var box = new Border
        {
            Height = 250, CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(44, 34, 44, 30),
            Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x1A, 0x10), Color.FromRgb(0x14, 0x17, 0x20), 20),
        };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string title, sub;
        if (current is not null) { title = current.Name; sub = $"Channel {number}  ·  {current.Group}"; }
        else if (count > 0) { title = "Watch live TV"; sub = $"{count} channels ready"; }
        else { title = "Welcome to KwartoTV"; sub = loading ? "Loading your channels…" : "Your living room, your playlists."; }
        col.Children.Add(UiKit.Dimmed(current is not null ? "CONTINUE WATCHING" : count > 0 ? "LIVE" : "GET STARTED", 16));
        col.Children.Add(new TextBlock { Text = title, FontSize = 46, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) });
        col.Children.Add(UiKit.Dimmed(sub, 22));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        if (count > 0) buttons.Children.Add(UiKit.ActionButton(current is not null ? "▶  Resume" : "▶  Watch", Shell.ShowLiveTv));
        else buttons.Children.Add(UiKit.ActionButton("Add channels", Shell.AddChannelsMenu));
        buttons.Children.Add(UiKit.ActionButton(Shell.Tv.Pairing.Devices.Count == 0 ? "Pair your phone" : "Pair another phone", () => Dialogs.Pair(Shell)));
        col.Children.Add(buttons);
        box.Child = col;
        return box;
    }
}
