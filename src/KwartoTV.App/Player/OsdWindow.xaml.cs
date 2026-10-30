using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KwartoTV.App.Services;
using KwartoTV.Core;

namespace KwartoTV.App.Player;

/// <summary>
/// Transparent click-through window drawn above the video. WPF content cannot draw over the mpv child window
/// (the "airspace" limit), so the on-screen display lives in its own owned window instead.
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public OsdWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeOverlay(new WindowInteropHelper(this).Handle);
        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); Banner.Visibility = Visibility.Collapsed; };
        _progressTimer.Tick += (_, _) => { _progressTimer.Stop(); Progress.Visibility = Visibility.Collapsed; };
    }

    public void ShowBanner(Channel ch, int number, bool favorite, string? onNow = null, string? onNext = null)
    {
        BannerNumber.Text = number.ToString();
        BannerName.Text = ch.Name + (favorite ? "  ★" : "");
        BannerGroup.Text = ch.Group;
        BannerNow.Text = onNow is null ? "" : "▶ Now: " + onNow;
        BannerNow.Visibility = onNow is null ? Visibility.Collapsed : Visibility.Visible;
        BannerNext.Text = onNext is null ? "" : "Next: " + onNext;
        BannerNext.Visibility = onNext is null ? Visibility.Collapsed : Visibility.Visible;
        Banner.Visibility = Visibility.Visible;
        _bannerTimer.Stop(); _bannerTimer.Start();
    }

    public void SetDigits(string digits)
    {
        DigitText.Text = digits;
        DigitBox.Visibility = digits.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowMessage(string? text)
    {
        MessageText.Text = text ?? "";
        MessageBox.Visibility = string.IsNullOrEmpty(text) || Guide.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowGuide(IReadOnlyList<(int Number, Channel Channel, bool Favorite, string? OnNow)> rows, int selected, string category, string position)
    {
        GuideTitle.Text = category;
        GuideSub.Text = position;
        GuideRows.Children.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            var (num, ch, fav, onNow) = rows[i];
            var on = i == selected;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // number 0 is the search row: OK on it opens the keyboard
            if (num == 0) ch = ch with { Name = "⌨  Type a channel name or number" };
            var n = new TextBlock { Text = num == 0 ? "🔍" : num.ToString(), FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = ch.Name, FontSize = 24, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            // the programme on now, under the channel name (when the TV guide has this channel)
            var now = new TextBlock { Text = onNow ?? "", FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = onNow is null ? Visibility.Collapsed : Visibility.Visible };
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, now } };
            var star = new TextBlock { Text = fav ? "★" : "", FontSize = 22, Margin = new Thickness(8, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameCol, 1); Grid.SetColumn(star, 2);
            row.Children.Add(n); row.Children.Add(nameCol); row.Children.Add(star);
            var fg = on ? new SolidColorBrush(Color.FromRgb(0x1A, 0x0D, 0x05)) : Brushes.White;
            n.Foreground = on ? fg : new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x6B));
            name.Foreground = fg; now.Foreground = on ? fg : new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB8)); star.Foreground = on ? fg : new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x45));
            GuideRows.Children.Add(new Border
            {
                Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(0, 9, 0, 9),
                Background = on ? new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x45)) : Brushes.Transparent,
            });
        }
        Guide.Visibility = Visibility.Visible;
        MessageBox.Visibility = Visibility.Collapsed;
    }

    public void HideGuide() => Guide.Visibility = Visibility.Collapsed;
    public bool ProgressVisible => Progress.Visibility == Visibility.Visible;
    public bool GuideVisible => Guide.Visibility == Visibility.Visible;

    public void ShowProgress(string title, double position, double duration, bool paused, bool keepVisible)
    {
        ProgressTitle.Text = (paused ? "⏸  " : "") + title;
        double frac = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        FillCol.Width = new GridLength(frac, GridUnitType.Star);
        RestCol.Width = new GridLength(1 - frac, GridUnitType.Star);
        ProgressTime.Text = duration > 0 ? $"{Fmt(position)}  /  {Fmt(duration)}" : Fmt(position);
        Progress.Visibility = Visibility.Visible;
        _progressTimer.Stop();
        if (!keepVisible) _progressTimer.Start();
    }

    public void HideAll()
    {
        Banner.Visibility = DigitBox.Visibility = MessageBox.Visibility = Guide.Visibility = Progress.Visibility = Visibility.Collapsed;
        _bannerTimer.Stop(); _progressTimer.Stop();
    }

    private static string Fmt(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
