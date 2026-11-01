using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KwartoTV.App.Services;
using KwartoTV.App.Ui;
using KwartoTV.Core;

namespace KwartoTV.App.Views;

/// <summary>
/// YouTube Music as a TV screen: what is playing with its buttons, a search, and rows of songs with their album art
/// (YouTube Music's own Quick picks, charts and more). OK plays a song; the music goes on behind other screens and the
/// lock screen, and the phone controls it too.
/// </summary>
public sealed class MusicView : PageBase
{
    private IReadOnlyList<MusicRow>? _rows;
    private IReadOnlyList<MusicTrack>? _found;
    private string? _query;
    private bool _loading;
    // Made again on every redraw: a WPF element can sit in only one panel at a time.
    private TextBlock _nowTitle = new(), _nowArtist = new();
    private Button? _playButton;

    public MusicView(MainWindow shell) : base(shell)
    {
        shell.Tv.MusicPlayer.Changed += s => Dispatcher.BeginInvoke(() => ShowNow(s));
    }

    private void ShowNow(MusicState s)
    {
        _nowTitle.Text = s.On && s.Title.Length > 0 ? s.Title : "Nothing playing";
        _nowArtist.Text = s.On && s.Title.Length > 0 ? (s.Playing ? "" : "Paused · ") + s.Artist : "Pick a song below, or search. The phone's Music tab controls it too.";
        if (_playButton?.Content is TextBlock t) t.Text = s.Playing ? "❚❚  Pause" : "▶  Play";
    }

    public override void Refresh()
    {
        if (_rows is null && !_loading) _ = LoadAsync();
        Rebuild(Build);
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _rows = await MusicService.HomeAsync();
        _loading = false;
        if (IsLoaded) { Rebuild(Build); if (Window.GetWindow(this) is { IsActive: true }) FocusFirst(); }
    }

    private async Task SearchAsync(string q)
    {
        _query = q; _found = null; Rebuild(Build);
        _found = (await MusicService.SearchAsync(q)).Tracks;
        Rebuild(Build);
        AllButtons().FirstOrDefault(b => b.Tag is string k && k.StartsWith("found:"))?.Focus();
    }

    private void Search() => Dialogs.Form(Shell, "Search YouTube Music", "A song or an artist (or type it on your phone).",
        new[] { new Dialogs.Field("Song or artist", _query ?? "", Purpose: "search") }, "Search", v =>
        {
            var q = v[0].Trim();
            if (!Music.IsQuery(q)) return "Type a song or an artist.";
            _ = SearchAsync(q);
            return null;
        });

    private void Build()
    {
        var m = Shell.Tv.MusicPlayer;
        var head = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var title = new TextBlock { Text = "YouTube Music", FontSize = 40, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        top.Children.Add(UiKit.ActionButton("🔍  Search", Search));
        if (!Shell.Tv.Settings.YouTubeMusicSignedIn) top.Children.Add(UiKit.ActionButton("Sign in (no ads)", () => Shell.OpenYouTubeMusic(signIn: true)));
        DockPanel.SetDock(top, Dock.Right);
        head.Children.Add(top); head.Children.Add(title);
        Body.Children.Add(head);

        // Now playing, with its buttons.
        _nowTitle = new TextBlock { FontSize = 30, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _nowArtist = UiKit.Dimmed("", 18);
        var card = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(24, 18, 24, 18), Margin = new Thickness(0, 10, 0, 4), Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)) };
        var info = new StackPanel();
        info.Children.Add(_nowTitle); info.Children.Add(_nowArtist);
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        keys.Children.Add(UiKit.ActionButton("⏮  Previous", m.Previous));
        _playButton = UiKit.ActionButton("▶  Play", m.PlayPause);
        keys.Children.Add(_playButton);
        keys.Children.Add(UiKit.ActionButton("Next  ⏭", m.Next));
        keys.Children.Add(UiKit.ActionButton("■  Stop", m.Stop));
        info.Children.Add(keys);
        card.Child = info;
        Body.Children.Add(card);
        ShowNow(m.State);

        if (_query is not null)
        {
            if (_found is null) Body.Children.Add(UiKit.Heading($"Searching \"{_query}\"…"));
            else if (_found.Count == 0) Body.Children.Add(UiKit.Heading($"No songs found for \"{_query}\""));
            else Body.Children.Add(UiKit.Row($"Results for \"{_query}\"", _found.Select(t => Card(t, "found:"))));
        }
        if (_rows is null) { Body.Children.Add(UiKit.Heading("Loading YouTube Music…")); return; }
        if (_rows.Count == 0) Body.Children.Add(new TextBlock { Text = "⚠ YouTube Music did not answer. Check the internet, then come back here.", FontSize = 18, Foreground = UiKit.B("Danger"), Margin = new Thickness(0, 20, 0, 0) });
        foreach (var r in _rows) Body.Children.Add(UiKit.Row(r.Title, r.Tracks.Select(t => Card(t, r.Title + ":"))));
    }

    private const double W = 210, H = 260;

    private Button Card(MusicTrack t, string keyPrefix)
    {
        var g = new Grid { Background = new LinearGradientBrush(UiKit.Hue(t.Title), UiKit.Hue(t.Artist + "m"), 60), ClipToBounds = true };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(W) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var note = new TextBlock { Text = "♪", FontSize = 64, Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(note);
        if (t.Art is { } art && Uri.TryCreate(art, UriKind.Absolute, out var u))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = u; bmp.DecodePixelWidth = 260; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                bmp.DownloadCompleted += (_, _) => note.Visibility = Visibility.Collapsed;
                g.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
            }
            catch { }
        }
        var text = new StackPanel { Margin = new Thickness(12, 6, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = t.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = t.Artist, FontSize = 14, Foreground = UiKit.B("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        var textBg = new Border { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x14, 0x1A)), Child = text };
        Grid.SetRow(textBg, 1); g.Children.Add(textBg);
        return UiKit.Tile(W, H, g, () => Shell.Tv.MusicPlayer.Play(t.Id), t.Title, keyPrefix + t.Id);
    }
}
