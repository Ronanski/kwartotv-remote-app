using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KwartoTV.Core;

namespace KwartoTV.App.Ui;

/// <summary>Builds the cards and rows used on the Home, Library and Settings screens.</summary>
public static class UiKit
{
    public const double CardW = 260, CardH = 146;

    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public static Button Tile(double w, double h, UIElement content, Action onClick, string name)
    {
        var b = new Button { Width = w, Height = h, Content = content, Style = (Style)Application.Current.FindResource("Tile"), RenderTransform = new ScaleTransform(1, 1) };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8),
    };

    public static TextBlock Dimmed(string text, double size = 18) =>
        new() { Text = text, FontSize = size, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap };

    /// <summary>A heading plus one horizontal strip of cards. Left/Right moves inside it, Up/Down moves between strips.</summary>
    public static StackPanel Row(string title, IEnumerable<UIElement> tiles)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        SmoothScroll.Attach(strip);
        foreach (var t in tiles) strip.Children.Add(t);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = strip,
            Padding = new Thickness(14, 12, 14, 16),
            Margin = new Thickness(-14, -6, -14, 0),
            Focusable = false,
        };
        var panel = new StackPanel();
        panel.Children.Add(Heading(title));
        panel.Children.Add(scroll);
        return panel;
    }

    public static Color Hue(string seed)
    {
        int h = 17;
        foreach (var c in seed) h = h * 31 + c;
        var rnd = new Random(h);
        double hue = rnd.NextDouble() * 360;
        return FromHsl(hue, 0.45, 0.28);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public static FrameworkElement ChannelCard(Channel ch, int number, bool favorite)
    {
        var g = new Grid { Background = new LinearGradientBrush(Hue(ch.Name), Hue(ch.Name + "x"), 45) };
        var name = new TextBlock
        {
            Text = ch.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(14, 0, 14, 10), TextWrapping = TextWrapping.NoWrap,
        };
        var initial = new TextBlock
        {
            Text = ch.Name.Length > 0 ? ch.Name[..1].ToUpperInvariant() : "?", FontSize = 64, FontWeight = FontWeights.Bold,
            Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -20, 0, 0),
        };
        g.Children.Add(initial);
        if (ch.LogoUrl is { } logo && Uri.TryCreate(logo, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            var img = new Image { Margin = new Thickness(36, 18, 36, 46), Stretch = Stretch.Uniform };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = u; bmp.DecodePixelWidth = 260; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                bmp.DownloadCompleted += (_, _) => initial.Visibility = Visibility.Collapsed;
                img.Source = bmp;
            }
            catch { }
            g.Children.Add(img);
        }
        g.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 2, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10),
            Child = new TextBlock { Text = number.ToString(), FontSize = 18, FontWeight = FontWeights.Bold },
        });
        if (favorite)
            g.Children.Add(new TextBlock { Text = "★", FontSize = 22, Foreground = B("Accent"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 0) });
        g.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0xCC, 0, 0, 0), 90), Height = 54, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
        g.Children.Add(name);
        return g;
    }

    public static FrameworkElement AppCard(string title, string glyph, Color from, Color to, string? caption = null)
    {
        var g = new Grid { Background = new LinearGradientBrush(from, to, 60) };
        g.Children.Add(new TextBlock { Text = glyph, FontSize = 58, FontFamily = new FontFamily("Segoe UI Symbol"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -16, 0, 0) });
        g.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 8, 12) });
        if (caption is not null)
            g.Children.Add(new TextBlock { Text = caption, FontSize = 14, Foreground = B("Dim"), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        return g;
    }

    public static Button ActionButton(string text, Action onClick, bool danger = false)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontSize = 20 }, Style = (Style)Application.Current.FindResource("Pill"), Margin = new Thickness(0, 0, 10, 0), Tag = danger ? "danger" : "action" };
        b.Click += (_, _) => onClick();
        return b;
    }
}
