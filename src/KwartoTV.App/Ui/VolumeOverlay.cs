using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KwartoTV.App.Services;

namespace KwartoTV.App.Ui;

/// <summary>
/// The volume rocker: a vertical bar on the right edge that appears above everything (home, player, YouTube, Netflix)
/// whenever the volume changes, like a TV.
/// </summary>
public sealed class VolumeOverlay : Window
{
    private readonly Border _fill = new() { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x45)), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
    private readonly TextBlock _icon = new() { FontSize = 38, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White };
    private readonly TextBlock _value = new() { FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private const double TrackHeight = 300;

    public VolumeOverlay()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; Topmost = true; IsHitTestVisible = false;
        Width = 130; Height = 520;
        Left = SystemParameters.PrimaryScreenWidth - Width - 36;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

        var track = new Border { Width = 28, Height = TrackHeight, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(4), Child = _fill };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(_icon);
        stack.Children.Add(new Border { Height = 14 });
        stack.Children.Add(track);
        stack.Children.Add(new Border { Height = 14 });
        stack.Children.Add(_value);
        Content = new Border { CornerRadius = new CornerRadius(40), Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x0B, 0x0D, 0x12)), Padding = new Thickness(0, 28, 0, 28), Child = stack, Margin = new Thickness(6) };
        Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlay(new WindowInteropHelper(this).Handle);
        };
        _hide.Tick += (_, _) => { _hide.Stop(); Fade(0, 220, () => Hide()); };
        SystemVolume.Changed += (percent, muted) => Dispatcher.BeginInvoke(() => Show(percent, muted));
    }

    public void Show(int percent, bool muted)
    {
        _icon.Text = muted || percent == 0 ? "🔇" : percent < 34 ? "🔈" : percent < 67 ? "🔉" : "🔊";
        _value.Text = muted ? "Muted" : percent.ToString();
        var frac = muted ? 0 : Math.Clamp(percent / 100.0, 0, 1);
        var target = (TrackHeight - 8) * frac;
        _fill.BeginAnimation(HeightProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (!IsVisible) { Opacity = 0; base.Show(); }
        Fade(1, 120, null);
        _hide.Stop(); _hide.Start();
    }

    private void Fade(double to, int ms, Action? done)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
        if (done is not null) a.Completed += (_, _) => done();
        BeginAnimation(OpacityProperty, a);
    }
}
