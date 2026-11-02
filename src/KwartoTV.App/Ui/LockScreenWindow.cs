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
/// The lock screen, over the KwartoTV window only: a calm clock on black (the time inside a ring that sweeps with the seconds, the date, and this
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
    // The look chosen on the phone (Lock workspace): see ApplySettings.
    private readonly Canvas _face = new() { Width = Ring, Height = Ring };
    private readonly Grid _clock = new() { Width = Ring, Height = Ring };
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Viewbox _fit = new() { StretchDirection = StretchDirection.DownOnly };
    private readonly Grid _root = new() { Background = Brushes.Black, ClipToBounds = true };
    private readonly Grid _photoLayer = new();
    private readonly Border _veil = new();
    private readonly Border _dimLayer = new() { Background = Brushes.Black, IsHitTestVisible = false, Opacity = 0 };
    private readonly StackPanel _center = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private KwartoTV.Core.MusicState? _music;
    private string _slideFolder = "";
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

    /// <param name="app">The KwartoTV window on screen (Home, the player or a web app). The lock screen covers exactly that
    /// window and stays on top of it only: other programs and other screens are left alone.</param>
    private readonly KwartoTV.Core.AppSettings _settings;
    private readonly TextBlock _verse = new() { FontSize = 24, FontStyle = FontStyles.Italic, FontWeight = FontWeights.Light, Foreground = Soft, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 34 };
    private readonly TextBlock _verseRef = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Accent), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _verseBox = new() { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(60, 18, 60, 0) };
    private string? _shownVerse;
    // Photo slideshow: two layers that cross-fade, under a dark veil so the clock stays easy to read.
    private readonly Image _photoA = new() { Stretch = Stretch.UniformToFill, Opacity = 0 }, _photoB = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private bool _aOnTop;
    private IReadOnlyList<string> _photos = Array.Empty<string>();
    private int _photoIndex;
    private readonly DispatcherTimer _slides = new();
    // The song playing (YouTube Music from the phone), under the verse.
    private readonly TextBlock _songTitle = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Soft, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 };
    private readonly TextBlock _songArtist = new() { FontSize = 17, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 };
    private readonly TextBlock _songIcon = new() { FontSize = 30, Foreground = new SolidColorBrush(Accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
    private readonly Border _song = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(22, 12, 26, 12), CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)) };

    // The music buttons under the song: the phone's ⏮ ⏯ ⏭ (Remote, Media or Music tab) and the PC's media keys work while locked.
    private readonly TextBlock _playKey = new() { Text = "▶", FontSize = 26, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A media key pressed on the PC while locked: "music.playpause", "music.next" or "music.prev".</summary>
    public event Action<string>? MusicKey;

    private static FrameworkElement Round(UIElement content, Brush fill, double size) =>
        new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, Margin = new Thickness(10, 0, 10, 0), Child = content };

    /// <summary>Shows the song playing (or hides the line when the music is off).</summary>
    public void SetMusic(KwartoTV.Core.MusicState s)
    {
        _music = s;
        if (!s.On || s.Title.Length == 0 || !_settings.LockSong) { _song.Visibility = Visibility.Collapsed; return; }
        _songTitle.Text = s.Title;
        _songArtist.Text = s.Artist;
        _songArtist.Visibility = s.Artist.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _songIcon.Text = s.Playing ? "♫" : "❚❚";
        _playKey.Text = s.Playing ? "❚❚" : "▶";
        _song.Opacity = s.Playing ? 1 : 0.6;
        _song.Visibility = Visibility.Visible;
    }

    /// <summary>Screensaver (not locked): any key, click or remote button closes it.</summary>
    public bool IsScreensaver { get; }

    /// <summary>The screensaver was closed by a key or a click.</summary>
    public event Action? Dismissed;

    public LockScreenWindow(Window app, KwartoTV.Core.AppSettings settings, bool screensaver = false)
    {
        _settings = settings;
        IsScreensaver = screensaver;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Owner = app;
        Background = Brushes.Black; Cursor = Cursors.None; Title = "KwartoTV locked";
        // Exactly over the KwartoTV window (in screen pixels, as Windows reports it), and again whenever it moves or resizes.
        var appHandle = new System.Windows.Interop.WindowInteropHelper(app).Handle;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 640; Height = 480;
        void Cover() => Services.Native.CoverWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, appHandle);
        SourceInitialized += (_, _) => Cover();
        EventHandler follow = (_, _) => Cover();
        SizeChangedEventHandler followSize = (_, _) => Cover();
        app.LocationChanged += follow; app.SizeChanged += followSize; app.StateChanged += follow;
        Closed += (_, _) => { app.LocationChanged -= follow; app.SizeChanged -= followSize; app.StateChanged -= follow; };
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        // The ring: a faint track that breathes, the arc of the current minute, and a dot at its tip.
        var track = new Ellipse { Width = Ring, Height = Ring, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.08 };
        track.BeginAnimation(OpacityProperty, new DoubleAnimation(0.05, 0.16, TimeSpan.FromSeconds(6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        _head.RenderTransform = _headAt;
        var face = _face;
        face.Children.Add(track); face.Children.Add(_arc); face.Children.Add(_head);
        var center = _center;
        center.Children.Add(_time); center.Children.Add(_ampm); center.Children.Add(_date);
        var clock = _clock;
        clock.Children.Add(face); clock.Children.Add(center);

        var row = _row;
        row.HorizontalAlignment = HorizontalAlignment.Center; row.VerticalAlignment = VerticalAlignment.Center; row.RenderTransform = _drift;
        row.Children.Add(clock); row.Children.Add(_calendar);

        var hint = new TextBlock
        {
            Text = screensaver ? "Press any button to go back" : "🔒  Locked  ·  unlock: Lock tab of KwartoTV Remote on your phone  ·  music: Music tab on the phone",
            FontSize = 17, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 36), RenderTransform = _drift,
        };
        _verseBox.Children.Add(_verse); _verseBox.Children.Add(_verseRef);
        var songText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        songText.Children.Add(_songTitle); songText.Children.Add(_songArtist);
        var songRow = new StackPanel { Orientation = Orientation.Horizontal };
        songRow.Children.Add(_songIcon); songRow.Children.Add(songText);
        var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        TextBlock Glyph(string t) => new() { Text = t, FontSize = 22, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dimFill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        keys.Children.Add(Round(Glyph("⏮"), dimFill, 52));
        keys.Children.Add(Round(_playKey, new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)), 62));
        keys.Children.Add(Round(Glyph("⏭"), dimFill, 52));
        var songAll = new StackPanel();
        songAll.Children.Add(songRow); songAll.Children.Add(keys);
        songAll.Children.Add(new TextBlock { Text = "Phone: ⏮ ⏯ ⏭ on the Remote, Media or Music tab", FontSize = 13, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        _song.Child = songAll;
        _verseBox.Children.Add(_song);
        _verseBox.RenderTransform = _drift;
        var root = _root;
        // The photos have their own layer, so their cross-fade order never covers the clock.
        var photos = _photoLayer;
        photos.Children.Add(_photoA); photos.Children.Add(_photoB);
        root.Children.Add(photos);
        _veil.Background = new LinearGradientBrush(Color.FromArgb(0x99, 0, 0, 0), Color.FromArgb(0xD9, 0, 0, 0), 90);
        root.Children.Add(_veil);
        // Clock and calendar on top (shrunk to fit a small window), the verse under them, the hint at the bottom: they
        // never overlap, whatever the window size.
        var stack = new Grid { Margin = new Thickness(0, 30, 0, 70) };
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fit = _fit; fit.Child = row; fit.Margin = new Thickness(40, 0, 40, 0);
        stack.Children.Add(fit);
        Grid.SetRow(_verseBox, 1); stack.Children.Add(_verseBox);
        root.Children.Add(stack); root.Children.Add(hint);
        root.Children.Add(_dimLayer);
        Content = root;
        _slides.Tick += (_, _) => NextPhoto();
        ApplySettings();

        _tick.Tick += (_, _) => Draw();
        _wander.Tick += (_, _) => Wander();
        Loaded += (_, _) =>
        {
            Draw(); _tick.Start(); _wander.Start();
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(1.2)) { EasingFunction = new QuadraticEase() });
        };
        Closed += (_, _) => { _tick.Stop(); _wander.Stop(); _slides.Stop(); };
        // Locked: nothing on the PC itself gets past this screen.
        PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (IsScreensaver && e.Key is not (Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack)) { Dismiss(); return; }
            // Only the PC's media keys get through, to the music.
            var cmd = e.Key switch { Key.MediaPlayPause => "music.playpause", Key.MediaNextTrack => "music.next", Key.MediaPreviousTrack => "music.prev", _ => null };
            if (cmd is not null) MusicKey?.Invoke(cmd);
        };
        PreviewMouseDown += (_, e) => { e.Handled = true; if (IsScreensaver) Dismiss(); };
        PreviewMouseWheel += (_, e) => e.Handled = true;
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
    }

    /// <summary>Closes the screensaver (a key, a click or a remote button).</summary>
    public void Dismiss()
    {
        if (!IsScreensaver || _allowClose) return;
        _allowClose = true;
        Close();
        Dismissed?.Invoke();
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
        var verse = KwartoTV.Core.Verses.For(now, _settings.VerseEveryMinutes);
        if (verse?.Reference != _shownVerse)
        {
            _shownVerse = verse?.Reference;
            _verseBox.Visibility = verse is null ? Visibility.Collapsed : Visibility.Visible;
            if (verse is not null)
            {
                _verse.Text = "“" + verse.Text + "”"; _verseRef.Text = verse.Reference;
                _verseBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5)));
            }
        }

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

    /// <summary>
    /// The look chosen on the phone: the clock (classic ring and calendar, a big time, or a small one in the corner), its
    /// color, the brightness, the date and song lines, and the background (photos, black or midnight blue). Called again
    /// when the phone changes an option, so the TV shows the change right away.
    /// </summary>
    public void ApplySettings()
    {
        var st = _settings;
        var color = Color.FromRgb(0xFF, 0xFF, 0xFF);
        if (KwartoTV.Core.LockOptions.Colors.Contains(st.LockColor))
            color = (Color)ColorConverter.ConvertFromString("#" + st.LockColor);
        _time.Foreground = new SolidColorBrush(Color.FromArgb(0xEE, color.R, color.G, color.B));
        _ampm.Foreground = _date.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
        _date.Visibility = st.LockDate ? Visibility.Visible : Visibility.Collapsed;
        switch (st.LockClock)
        {
            case "big":
                _face.Visibility = Visibility.Collapsed; _calendar.Visibility = Visibility.Collapsed;
                _clock.Width = double.NaN; _clock.Height = double.NaN;
                _time.FontSize = 240; _time.FontWeight = FontWeights.ExtraLight;
                _row.HorizontalAlignment = HorizontalAlignment.Center; _row.VerticalAlignment = VerticalAlignment.Center;
                _fit.HorizontalAlignment = HorizontalAlignment.Stretch; _fit.VerticalAlignment = VerticalAlignment.Stretch;
                _center.HorizontalAlignment = HorizontalAlignment.Center;
                break;
            case "corner":
                _face.Visibility = Visibility.Collapsed; _calendar.Visibility = Visibility.Collapsed;
                _clock.Width = double.NaN; _clock.Height = double.NaN;
                _time.FontSize = 110; _time.FontWeight = FontWeights.Light;
                _fit.HorizontalAlignment = HorizontalAlignment.Left; _fit.VerticalAlignment = VerticalAlignment.Bottom;
                _center.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            default:
                _face.Visibility = Visibility.Visible; _calendar.Visibility = Visibility.Visible;
                _clock.Width = Ring; _clock.Height = Ring;
                _time.FontSize = 168; _time.FontWeight = FontWeights.Thin;
                _fit.HorizontalAlignment = HorizontalAlignment.Stretch; _fit.VerticalAlignment = VerticalAlignment.Stretch;
                _center.HorizontalAlignment = HorizontalAlignment.Center;
                break;
        }
        foreach (var t in new[] { _time, _ampm, _date })
            t.HorizontalAlignment = st.LockClock == "corner" ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _dimLayer.Opacity = st.LockDim switch { 70 => 0.3, 40 => 0.6, _ => 0 };
        var photos = st.SlideshowOn && !string.IsNullOrWhiteSpace(st.SlideshowFolder);
        _root.Background = !photos && st.LockBackground == "midnight" ? new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x30)) : Brushes.Black;
        _photoLayer.Visibility = _veil.Visibility = photos ? Visibility.Visible : Visibility.Collapsed;
        if (_music is { } m) SetMusic(m);
        _shownVerse = "\u0000"; // shows (or hides) the verse again on the next tick
        if (!photos) { _slides.Stop(); _slideFolder = ""; return; }
        _slides.Interval = TimeSpan.FromSeconds(Math.Max(5, st.SlideshowSeconds));
        if (_slideFolder == st.SlideshowFolder) return;
        _slideFolder = st.SlideshowFolder!;
        _slides.Stop();
        var folder = _slideFolder;
        var seed = Environment.TickCount;
        _ = Task.Run(() => KwartoTV.Core.Slideshow.Photos(folder, seed)).ContinueWith(t =>
        {
            if (t.IsFaulted || folder != _slideFolder) return;
            _photos = t.Result; _photoIndex = 0;
            if (_photos.Count == 0) return;
            NextPhoto(); _slides.Start();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void NextPhoto()
    {
        if (_photos.Count == 0) return;
        var path = _photos[_photoIndex++ % _photos.Count];
        var width = (int)Math.Min(2560, Math.Max(800, ActualWidth * 1.5));
        // Decoded off the UI thread at screen size (a 24 MP photo would be slow and use a lot of memory).
        _ = Task.Run(() =>
        {
            try
            {
                var b = new System.Windows.Media.Imaging.BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(path); b.DecodePixelWidth = width;
                b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                b.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                b.EndInit(); b.Freeze();
                return (ImageSource?)b;
            }
            catch { return null; }
        }).ContinueWith(t =>
        {
            if (t.Result is not { } img) return;
            var (show, hide) = _aOnTop ? (_photoB, _photoA) : (_photoA, _photoB);
            _aOnTop = !_aOnTop;
            show.Source = img;
            Panel.SetZIndex(show, 1); Panel.SetZIndex(hide, 0);
            show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2)) { EasingFunction = new SineEase() });
            hide.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(2.5)));
        }, TaskScheduler.FromCurrentSynchronizationContext());
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
