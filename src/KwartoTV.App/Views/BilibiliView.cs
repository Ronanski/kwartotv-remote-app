using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KwartoTV.App.Services;
using KwartoTV.App.Ui;
using KwartoTV.Core;

namespace KwartoTV.App.Views;

/// <summary>
/// Bilibili as a TV screen made by KwartoTV: rows of posters from bilibili.tv's own public lists (recommended, anime),
/// a search that can be typed on the phone, and OK to play (only the video, fullscreen). Arrows and OK, like Live TV.
/// </summary>
public sealed class BilibiliView : PageBase
{
    private static readonly HttpClient Http = MakeHttp();
    private IReadOnlyList<BiliRow>? _rows;
    private IReadOnlyList<BiliItem>? _found;
    private string? _query, _error;
    private bool _loading;
    private string? _lastKey;

    public BilibiliView(MainWindow shell) : base(shell) { }

    private static HttpClient MakeHttp()
    {
        var h = ResilientHttp.Create(TimeSpan.FromSeconds(20));
        h.DefaultRequestHeaders.UserAgent.Clear();
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.tv/");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.bilibili.tv");
        return h;
    }

    public override void Refresh()
    {
        if (_rows is null && !_loading) _ = LoadAsync();
        Rebuild(Build);
    }

    public override void FocusFirstOrLast()
    {
        if (_lastKey is { } key && AllButtons().FirstOrDefault(b => Equals(b.Tag, key)) is { } b) b.Focus();
        else FocusFirst();
    }

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try
        {
            var rec = Http.GetStringAsync(Bilibili.RecommendUrl);
            var anime1 = Http.GetStringAsync(Bilibili.AnimePageUrl(1));
            var anime2 = Http.GetStringAsync(Bilibili.AnimePageUrl(2));
            var rows = new List<BiliRow>();
            static string Head(string s) => s.Length > 300 ? s[..300] : s;
            try
            {
                var json = await rec; var cards = Bilibili.ParseCards(json);
                if (cards.Count > 0) rows.Add(new BiliRow("Recommended", cards)); else AppPaths.LogLine("bilibili: recommend gave nothing: " + Head(json));
            }
            catch (Exception ex) { AppPaths.LogLine("bilibili: recommend: " + ex.Message); }
            foreach (var t in new[] { anime1, anime2 })
                try
                {
                    var json = await t; var mods = Bilibili.ParseModules(json);
                    if (mods.Count > 0) rows.AddRange(mods); else AppPaths.LogLine("bilibili: anime page gave nothing: " + Head(json));
                }
                catch (Exception ex) { AppPaths.LogLine("bilibili: anime: " + ex.Message); }
            _rows = rows.GroupBy(r => r.Title).Select(g => new BiliRow(g.Key, g.SelectMany(r => r.Items).DistinctBy(i => (i.IsSeason, i.Id)).Take(40).ToList())).Take(14).ToList();
            AppPaths.LogLine($"bilibili: {_rows.Count} rows, {_rows.Sum(r => r.Items.Count)} shows");
            if (_rows.Count == 0) _error = "Bilibili did not send its lists. Check the internet, or open the website.";
        }
        catch (Exception ex) { _error = "Bilibili could not be reached: " + ex.Message; AppPaths.LogLine("bilibili: " + ex.Message); _rows = Array.Empty<BiliRow>(); }
        _loading = false;
        if (IsLoaded) Rebuild(Build);
        if (IsLoaded && Window.GetWindow(this) is { IsActive: true }) FocusFirstOrLast();
    }

    private async Task SearchAsync(string query)
    {
        _query = query; _found = null; Rebuild(Build);
        try { _found = Bilibili.ParseSearch(await Http.GetStringAsync(Bilibili.SearchUrl(query))); }
        catch (Exception ex) { _found = Array.Empty<BiliItem>(); AppPaths.LogLine("bilibili: search: " + ex.Message); }
        AppPaths.LogLine($"bilibili: search \"{query}\": {_found.Count}");
        Rebuild(Build);
        if (_found.Count > 0) AllButtons().FirstOrDefault(b => b.Tag is string k && k.StartsWith("found:"))?.Focus();
    }

    private void Search() => Dialogs.Form(Shell, "Search Bilibili", "Type a show or anime name (or type it on your phone).",
        new[] { new Dialogs.Field("Name", _query ?? "", Purpose: "search") }, "Search", v =>
        {
            var q = v[0].Trim();
            if (q.Length is 0 or > 60) return "Type a name.";
            _ = SearchAsync(q);
            return null;
        });

    private void Build()
    {
        var head = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var title = new TextBlock { Text = "Bilibili", FontSize = 40, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(UiKit.ActionButton("🔍  Search", Search));
        buttons.Children.Add(UiKit.ActionButton("Reload", () => { _rows = null; Refresh(); }));
        buttons.Children.Add(UiKit.ActionButton("Website / sign in", Shell.LaunchBilibiliWebsite));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons); head.Children.Add(title);
        Body.Children.Add(head);
        Body.Children.Add(UiKit.Dimmed("Anime and shows with English subtitles. OK plays (only the video, full screen). In the video: OK pause, ← → 10 seconds, Back returns here. Sign in on the phone: Remote tab → Sign in to Bilibili.", 16));

        if (_query is not null)
        {
            if (_found is null) Body.Children.Add(UiKit.Heading($"Searching \"{_query}\"…"));
            else if (_found.Count == 0) Body.Children.Add(UiKit.Heading($"Nothing found for \"{_query}\""));
            else Body.Children.Add(UiKit.Row($"Results for \"{_query}\"", _found.Select(i => Card(i, "found:"))));
        }
        if (_rows is null) { Body.Children.Add(UiKit.Heading("Loading Bilibili…")); return; }
        if (_error is not null) Body.Children.Add(new TextBlock { Text = "⚠ " + _error, FontSize = 18, Foreground = UiKit.B("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
        foreach (var r in _rows) Body.Children.Add(UiKit.Row(r.Title, r.Items.Select(i => Card(i, r.Title + ":"))));
    }

    private const double W = 220, H = 300;

    private Button Card(BiliItem item, string keyPrefix)
    {
        var key = keyPrefix + (item.IsSeason ? "s" : "v") + item.Id;
        var g = new Grid { Background = new LinearGradientBrush(UiKit.Hue(item.Title), UiKit.Hue(item.Title + "b"), 60), ClipToBounds = true };
        var initial = new TextBlock { Text = item.Title[..1].ToUpperInvariant(), FontSize = 72, FontWeight = FontWeights.Bold, Opacity = 0.3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(initial);
        if (item.Cover is { } cover && Uri.TryCreate(cover, UriKind.Absolute, out var u))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = u; bmp.DecodePixelWidth = 320; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                bmp.DownloadCompleted += (_, _) => initial.Visibility = Visibility.Collapsed;
                g.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
            }
            catch { }
        }
        g.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0xE0, 0, 0, 0), 90), Height = 110, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
        g.Children.Add(new TextBlock
        {
            Text = item.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 50, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 12),
        });
        if (item.Badge is { } badge)
            g.Children.Add(new Border
            {
                Background = UiKit.B("Accent"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = badge, FontSize = 14, FontWeight = FontWeights.SemiBold },
            });
        return UiKit.Tile(W, H, g, () => { _lastKey = key; Shell.PlayBilibili(item); }, item.Title, key);
    }
}
