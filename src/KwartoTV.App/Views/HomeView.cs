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

    public HomeView(MainWindow shell) : base(shell) { }

    public override void Refresh() => Rebuild(Build);

    private void Build()
    {
        var tv = Shell.Tv;
        var lineup = tv.Lineup;
        var channels = lineup.Channels;
        var numbers = new Dictionary<string, int>();
        for (int i = 0; i < channels.Count; i++) numbers[channels[i].Id] = i + 1;

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
        apps.Add(UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.AppCard("Add website", "+", Color.FromRgb(0x1E, 0x24, 0x30), Color.FromRgb(0x2B, 0x31, 0x40)), Shell.ShowSettings, "Add website"));
        Body.Children.Add(UiKit.Row("Apps", apps));

        // Each playlist the user gave its own space gets its own row right under the apps.
        foreach (var (space, chs) in tv.Spaces())
            if (chs.Count > 0) Body.Children.Add(UiKit.Row("📺  " + space.Name, chs.Take(MaxPerRow).Select(x => Card(x.Channel, x.Number, lineup.IsFavorite(x.Channel.Id)))));

        var favs = lineup.Favorites;
        if (favs.Count > 0) Body.Children.Add(UiKit.Row("My favorites", favs.Take(MaxPerRow).Select(c => Card(c, numbers[c.Id], true))));

        int rows = 0;
        foreach (var g in lineup.Groups)
        {
            if (rows++ >= MaxRows) break;
            var inGroup = lineup.InGroup(g);
            Body.Children.Add(UiKit.Row(inGroup.Count > MaxPerRow ? $"{g}  ·  {inGroup.Count}" : g, inGroup.Take(MaxPerRow).Select(c => Card(c, numbers[c.Id], lineup.IsFavorite(c.Id)))));
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

    private FrameworkElement Card(Channel c, int number, bool fav) =>
        UiKit.Tile(UiKit.CardW, UiKit.CardH, UiKit.ChannelCard(c, number, fav), () => Shell.Tv.Lineup.TuneNumber(number), $"{number} {c.Name}");

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
