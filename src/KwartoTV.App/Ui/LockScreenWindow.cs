using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KwartoTV.App.Ui;

/// <summary>
/// The lock screen: a calm clock on black (the time inside a ring that sweeps with the seconds, the date, and this
/// month's calendar). Everything drifts slowly so nothing burns into the TV. While it is up the TV is locked: keys,
/// clicks and remote buttons do nothing; only Unlock on the phone closes it (see MainWindow.ScreenOn).
/// </summary>
public sealed class LockScreenWindow : Window
{
    private const double Ring = 540, Stroke = 3;
    private static readonly Color Accent = Color.FromRgb(0xFF, 0x7A, 0x45);
    private static readonly Brush Soft = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));

    private readonly TextBlock _time = new() { FontSize = 168, FontWeight = FontWeights.Thin, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _ampm = new() { FontSize = 30, FontWeight = FontWeights.Light, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -18, 0, 0) };
    private readonly TextBlock _date = new() { FontSize = 28, FontWeight = FontWeights.Light, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
    private readonly System.Windows.Shapes.Path _arc = new() { Stroke = new SolidColorBrush(Color.FromArgb(0xC8, Accent.R, Accent.G, Accent.B)), StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Ellipse _head = new() { Width = 12, Height = 12, Fill = new SolidColorBrush(Accent) };
    private readonly TranslateTransform _headAt = new();
    private readonly StackPanel _calendar = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(90, 0, 0, 0) };
    private readonly TranslateTransform _drift = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _wander = new() { Interval = TimeSpan.FromSeconds(45) };
    private readonly Random _rnd = new();
    private DateTime _calendarDay;
    private string _shownTime = "";
    private bool _allowClose;

    public LockScreenWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Background = Brushes.Black; Cursor = Cursors.None; Title = "KwartoTV locked";
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        // The ring: a faint track that breathes, the arc of the current minute, and a dot at its tip.
        var track = new Ellipse { Width = Ring, Height = Ring, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.08 };
        track.BeginAnimation(OpacityProperty, new DoubleAnimation(0.05, 0.16, TimeSpan.FromSeconds(6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        _head.RenderTransform = _headAt;
        var face = new Canvas { Width = Ring, Height = Ring };
        face.Children.Add(track); face.Children.Add(_arc); face.Children.Add(_head);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(_time); center.Children.Add(_ampm); center.Children.Add(_date);
        var clock = new Grid { Width = Ring, Height = Ring };
        clock.Children.Add(face); clock.Children.Add(center);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransform = _drift };
        row.Children.Add(clock); row.Children.Add(_calendar);

        var hint = new TextBlock
        {
            Text = "🔒  Locked  ·  unlock from KwartoTV Remote on your phone (Power → Unlock)",
            FontSize = 17, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 36), RenderTransform = _drift,
        };
        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(row); root.Children.Add(hint);
        Content = root;

        _tick.Tick += (_, _) => Draw();
        _wander.Tick += (_, _) => Wander();
        Loaded += (_, _) =>
        {
            Draw(); _tick.Start(); _wander.Start();
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(1.2)) { EasingFunction = new QuadraticEase() });
        };
        Closed += (_, _) => { _tick.Stop(); _wander.Stop(); };
        // Locked: nothing on the PC itself gets past this screen.
        PreviewKeyDown += (_, e) => e.Handled = true;
        PreviewMouseDown += (_, e) => e.Handled = true;
        PreviewMouseWheel += (_, e) => e.Handled = true;
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
    }

    /// <summary>Closes the lock screen (only the phone's Unlock calls this).</summary>
    public void Unlock()
    {
        _allowClose = true;
        Close();
    }

    private void Draw()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var time = now.ToString("h:mm", culture);
        if (time != _shownTime)
        {
            _shownTime = time;
            _time.Text = time;
            _ampm.Text = now.ToString("tt", culture).ToLowerInvariant();
            _date.Text = now.ToString("dddd, MMMM d", culture);
        }
        if (now.Date != _calendarDay) { _calendarDay = now.Date; BuildCalendar(now); }

        // The arc follows the seconds smoothly, starting at the top and going clockwise.
        var frac = (now.Second + now.Millisecond / 1000.0) / 60.0;
        double r = (Ring - Stroke) / 2, c = Ring / 2;
        var angle = frac * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
        _arc.Data = new PathGeometry(new[] { figure });
        _headAt.X = end.X - _head.Width / 2; _headAt.Y = end.Y - _head.Height / 2;
    }

    private void BuildCalendar(DateTime today)
    {
        _calendar.Children.Clear();
        var culture = CultureInfo.CurrentCulture;
        _calendar.Children.Add(new TextBlock { Text = today.ToString("MMMM yyyy", culture), FontSize = 26, FontWeight = FontWeights.Light, Foreground = Soft, Margin = new Thickness(6, 0, 0, 14) });
        var grid = new UniformGrid { Columns = 7 };
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)first + i) % 7);
            grid.Children.Add(Cell(culture.DateTimeFormat.GetShortestDayName(dow)[..1], Faint, null, 16));
        }
        var start = new DateTime(today.Year, today.Month, 1);
        int blanks = ((int)start.DayOfWeek - (int)first + 7) % 7;
        for (int i = 0; i < blanks; i++) grid.Children.Add(new Border());
        for (int d = 1; d <= DateTime.DaysInMonth(today.Year, today.Month); d++)
        {
            var isToday = d == today.Day;
            var weekend = new DateTime(today.Year, today.Month, d).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            grid.Children.Add(Cell(d.ToString(culture), isToday ? Brushes.Black : weekend ? Dim : Soft, isToday ? new SolidColorBrush(Accent) : null, 20));
        }
        _calendar.Children.Add(grid);
    }

    private static FrameworkElement Cell(string text, Brush fg, Brush? circle, double size)
    {
        var g = new Grid { Width = 46, Height = 42 };
        if (circle is not null) g.Children.Add(new Ellipse { Width = 36, Height = 36, Fill = circle });
        g.Children.Add(new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg, FontWeight = circle is null ? FontWeights.Light : FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    // A slow drift to a new spot now and then, so nothing stays in one place on the TV.
    private void Wander()
    {
        double maxX = Math.Max(0, (ActualWidth - 1300) / 2), maxY = Math.Max(0, (ActualHeight - Ring) / 2 - 40);
        maxX = Math.Min(maxX, 160); maxY = Math.Min(maxY, 90);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation((_rnd.NextDouble() * 2 - 1) * maxX, TimeSpan.FromSeconds(12)) { EasingFunction = ease });
        _drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation((_rnd.NextDouble() * 2 - 1) * maxY, TimeSpan.FromSeconds(12)) { EasingFunction = ease });
    }
}
