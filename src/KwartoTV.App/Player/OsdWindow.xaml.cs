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
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); VolumeBox.Visibility = Visibility.Collapsed; };
        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); Banner.Visibility = Visibility.Collapsed; };
        _progressTimer.Tick += (_, _) => { _progressTimer.Stop(); Progress.Visibility = Visibility.Collapsed; };
    }

    private readonly DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };

    /// <summary>Volume bar at the top. Volume goes up to 130 (percent of the original level).</summary>
    public void ShowVolume(int volume, bool muted)
    {
        VolumeText.Text = muted ? "🔇  Muted" : $"🔊  {volume}%";
        double frac = muted ? 0 : Math.Clamp(volume / 130.0, 0, 1);
        VolFillCol.Width = new GridLength(frac, GridUnitType.Star);
        VolRestCol.Width = new GridLength(1 - frac, GridUnitType.Star);
        VolumeBox.Visibility = Visibility.Visible;
        _volumeTimer.Stop(); _volumeTimer.Start();
    }

    public void ShowBanner(Channel ch, int number, bool favorite)
    {
        BannerNumber.Text = number.ToString();
        BannerName.Text = ch.Name + (favorite ? "  ★" : "");
        BannerGroup.Text = ch.Group;
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

    public void ShowGuide(IReadOnlyList<(int Number, Channel Channel, bool Favorite)> rows, int selected)
    {
        GuideRows.Children.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            var (num, ch, fav) = rows[i];
            var on = i == selected;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = num.ToString(), FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = ch.Name, FontSize = 24, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var star = new TextBlock { Text = fav ? "★" : "", FontSize = 22, Margin = new Thickness(8, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); Grid.SetColumn(star, 2);
            row.Children.Add(n); row.Children.Add(name); row.Children.Add(star);
            var fg = on ? new SolidColorBrush(Color.FromRgb(0x1A, 0x0D, 0x05)) : Brushes.White;
            n.Foreground = on ? fg : new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x6B));
            name.Foreground = fg; star.Foreground = on ? fg : new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x45));
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
        Banner.Visibility = DigitBox.Visibility = MessageBox.Visibility = Guide.Visibility = Progress.Visibility = VolumeBox.Visibility = Visibility.Collapsed;
        _bannerTimer.Stop(); _progressTimer.Stop(); _volumeTimer.Stop();
    }

    private static string Fmt(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
