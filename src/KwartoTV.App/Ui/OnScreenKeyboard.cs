using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KwartoTV.App.Services;
using KwartoTV.Core;

namespace KwartoTV.App.Ui;

/// <summary>Where the on-screen keyboard sends its letters: a text box in KwartoTV, or a field on a web page.</summary>
public interface ITextTarget
{
    void Insert(string text);
    void Backspace();
    void Clear();
    /// <summary>Enter / submit.</summary>
    void Submit();
    /// <summary>Moves the cursor [delta] characters (◀ -1, ▶ +1).</summary>
    void MoveCaret(int delta);
    /// <summary>What the field holds now, for the keyboard's preview. A web page reports it a moment after each change.</summary>
    string Text { get; }
    int Caret { get; }
    /// <summary>A password field: the preview shows dots and nothing is remembered.</summary>
    bool Secret { get; }
    /// <summary>The field can be submitted (a web page form or search box), so the keyboard shows a Go key.</summary>
    bool CanSubmit { get; }
    /// <summary>Text or Caret changed.</summary>
    event Action? Changed;
}

public sealed class TextBoxTarget : ITextTarget
{
    private readonly TextBox _box;
    private Action? _changed;
    public TextBoxTarget(TextBox box) => _box = box;

    // Only listens to the box while the keyboard does, so opening the keyboard again does not pile up handlers.
    public event Action? Changed
    {
        add { if (_changed is null) _box.TextChanged += OnBoxChanged; _changed += value; }
        remove { _changed -= value; if (_changed is null) _box.TextChanged -= OnBoxChanged; }
    }

    private void OnBoxChanged(object sender, TextChangedEventArgs e) => _changed?.Invoke();

    public string Text => _box.Text;
    public int Caret => Math.Clamp(_box.CaretIndex, 0, _box.Text.Length);
    public bool Secret => false;
    public bool CanSubmit => false;

    public void Insert(string text)
    {
        int at = Caret;
        _box.Text = _box.Text.Insert(at, text);
        _box.CaretIndex = at + text.Length;
        _changed?.Invoke();
    }

    public void Backspace()
    {
        int at = Caret;
        if (at == 0) return;
        _box.Text = _box.Text.Remove(at - 1, 1);
        _box.CaretIndex = at - 1;
        _changed?.Invoke();
    }

    public void MoveCaret(int delta)
    {
        _box.CaretIndex = Math.Clamp(Caret + delta, 0, _box.Text.Length);
        _changed?.Invoke();
    }

    public void Clear() { _box.Text = ""; _changed?.Invoke(); }
    public void Submit() { }
}

/// <summary>
/// A TV-style on-screen keyboard that works with only ▲ ▼ ◀ ▶ and OK. The arrows wrap round the rows and columns,
/// OK types; holding OK types the key's capital or small symbol, and repeats ⌫ ◀ ▶. Pages: ABC, 123 and #+=.
/// A preview shows the text with its cursor, and recent entries for the same kind of field can be picked again.
/// Escape / Back close it too, for remotes that have them.
/// </summary>
public sealed class OnScreenKeyboard : Border
{
    private enum Layer { Letters, Symbols, More }
    private enum Shift { Off, Once, Caps }

    /// <summary>A key. Its own Enter/Space handling is off: the keyboard handles OK so a held OK can repeat or type the alternate.</summary>
    private sealed class KeyButton : Button
    {
        public string Id { get; init; } = "";
        /// <summary>Character key: what it types (letters in lower case; Shift changes them).</summary>
        public string? Char { get; init; }
        /// <summary>Any other key.</summary>
        public Action? Act { get; set; }
        /// <summary>Holding OK repeats it (⌫ ◀ ▶).</summary>
        public bool Repeats { get; init; }
        public TextBlock Label { get; init; } = null!;

        protected override void OnKeyDown(KeyEventArgs e) { }
        protected override void OnKeyUp(KeyEventArgs e) { }

        /// <summary>The pressed look for a moment, so an OK from the remote is seen on the key.</summary>
        public void Flash()
        {
            IsPressed = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            t.Tick += (_, _) => { t.Stop(); IsPressed = false; };
            t.Start();
        }
    }

    private const double Unit = 70;          // one key plus its margins
    private const int RowUnits = 12;         // every row of keys is this wide
    private const double KeyHeight = 60;

    private readonly ITextTarget _target;
    private readonly Action _done, _close;
    private readonly string? _purpose;
    private readonly StackPanel _keyRows = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _preview = new() { FontSize = 34, TextTrimming = TextTrimming.None, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    private Layer _layer = Layer.Letters;
    private Shift _shift = Shift.Off;
    private double? _prefX;           // the column kept while moving up and down through rows of different lengths
    private KeyButton? _held;         // the key the current OK press started on
    private string? _heldAlt;         // what a held OK turns the just-typed character into
    private List<string> _shownChips = new();

    private static TypingHistory? _history;
    private static TypingHistory History => _history ??= TypingHistory.Load(AppPaths.TypingHistory);

    /// <param name="done">Done (and Go): the text is finished.</param>
    /// <param name="close">✕ Close, Escape or Back: leave the text as it is. Defaults to [done].</param>
    /// <param name="purpose">The kind of field ("link", "web-search", ...): its recent entries are offered. Null remembers nothing.</param>
    /// <param name="title">A short name for the field, shown above the preview.</param>
    public OnScreenKeyboard(ITextTarget target, Action done, Action? close = null, string? purpose = null, string? title = null)
    {
        _target = target; _done = done; _close = close ?? done; _purpose = purpose;
        Background = UiKit.B("Surface");
        BorderBrush = UiKit.B("Line");
        BorderThickness = new Thickness(0, 2, 0, 0);
        Padding = new Thickness(40, 12, 40, 16);

        var head = new DockPanel { Margin = new Thickness(3, 0, 3, 4) };
        var hint = UiKit.Dimmed("Hold OK: capital or small symbol  ·  hold ⌫ ◀ ▶ to repeat", 15);
        hint.TextWrapping = TextWrapping.NoWrap; hint.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(hint, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(new TextBlock { Text = title ?? "", FontSize = 18, Foreground = UiKit.B("Dim"), VerticalAlignment = VerticalAlignment.Bottom });

        var previewBox = new Border
        {
            Background = UiKit.B("Bg"), BorderBrush = UiKit.B("Line"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 4, 18, 6), Margin = new Thickness(3, 0, 3, 10), Height = 64, ClipToBounds = true, Child = _preview,
        };

        var body = new StackPanel { Width = Unit * RowUnits };
        body.Children.Add(head);
        body.Children.Add(previewBox);
        body.Children.Add(_chips);
        body.Children.Add(_keyRows);
        // Shrinks (never grows) to fit small or high-DPI screens, and never covers more than about 60% of the height.
        Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = SystemParameters.PrimaryScreenHeight * 0.6, Child = body };

        Build();
        Refresh();
        Loaded += (_, _) => { _target.Changed -= Refresh; _target.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _target.Changed -= Refresh;
    }

    // ---------- layout

    private const string DigitRow = "1234567890";
    private static readonly string[] LetterRows = { "qwertyuiop", "asdfghjkl-", "zxcvbnm,.@" };
    private static readonly string[] SymbolRows = { "@#$%&*-+()", "!\"':;/?_,." };
    private static readonly string[] MoreRows = { "[]{}<>^|\\`", "=~₱€£¥°•¿¡" };

    private void Build()
    {
        _keyRows.Children.Clear();
        var chars = _layer switch { Layer.Symbols => SymbolRows, Layer.More => MoreRows, _ => LetterRows };

        // Every row is 10 one-wide keys plus a 2-wide action key on the right, so the right column lines up.
        _keyRows.Children.Add(Row(CharKeys(DigitRow), Wide("⌫", 2, "backspace", () => _target.Backspace(), repeats: true)));
        _keyRows.Children.Add(Row(CharKeys(chars[0]), Wide("Clear", 2, "clear", () => _target.Clear())));
        _keyRows.Children.Add(Row(CharKeys(chars[1]), Wide("✓ Done", 2, "done", () => Finish(submit: false), accent: true)));
        var third = _layer == Layer.Letters ? CharKeys(LetterRows[2]) : WebKeys();
        _keyRows.Children.Add(Row(third, Wide("✕ Close", 2, "close", () => _close())));

        // Bottom row: Shift / page keys, .com, space, the cursor, and Go for web pages.
        var bottom = new List<KeyButton>();
        bottom.Add(_layer switch
        {
            Layer.Letters => Wide("⇧", 1.5, "shift", CycleShift),
            Layer.Symbols => Wide("#+=", 1.5, "page2", () => SetLayer(Layer.More)),
            _ => Wide("123", 1.5, "page2", () => SetLayer(Layer.Symbols)),
        });
        bottom.Add(_layer == Layer.Letters ? Wide("&123", 1.5, "page", () => SetLayer(Layer.Symbols)) : Wide("ABC", 1.5, "page", () => SetLayer(Layer.Letters)));
        bottom.Add(Wide(".com", 1.5, "com", () => _target.Insert(".com")));
        double space = RowUnits - 1.5 * 3 - 1.25 * 2 - (_target.CanSubmit ? 2 : 0);
        bottom.Add(Wide("space", space, "space", () => _target.Insert(" ")));
        bottom.Add(Wide("◀", 1.25, "left", () => _target.MoveCaret(-1), repeats: true));
        bottom.Add(Wide("▶", 1.25, "right", () => _target.MoveCaret(1), repeats: true));
        if (_target.CanSubmit) bottom.Add(Wide("⏎ Go", 2, "go", () => Finish(submit: true), accent: true));
        _keyRows.Children.Add(Row(bottom));
        Relabel();
    }

    private static StackPanel Row(IEnumerable<KeyButton> keys, KeyButton? last = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var k in keys) row.Children.Add(k);
        if (last is not null) row.Children.Add(last);
        return row;
    }

    private List<KeyButton> CharKeys(string chars)
    {
        var keys = new List<KeyButton>();
        foreach (var c in chars)
        {
            var ch = c.ToString();
            var alt = OskText.Alternate(ch);
            // Letters show their capital by Shift; other keys show what a held OK types, small in the corner.
            keys.Add(NewKey(ch, 1, "k" + ch, hint: char.IsLetter(c) ? null : alt, character: ch));
        }
        return keys;
    }

    /// <summary>Starters for web addresses and e-mail, on the 123 and #+= pages (.com is on every page, @ on ABC and 123).</summary>
    private List<KeyButton> WebKeys() => new()
    {
        Wide("https://", 2.8, "https", () => _target.Insert("https://")),
        Wide("www.", 2, "www", () => _target.Insert("www.")),
        Wide(".ph", 1.6, "ph", () => _target.Insert(".ph")),
        Wide(".net", 1.8, "net", () => _target.Insert(".net")),
        Wide(".org", 1.8, "org", () => _target.Insert(".org")),
    };

    private KeyButton Wide(string label, double width, string id, Action act, bool accent = false, bool repeats = false)
    {
        var k = NewKey(label, width, id, repeats: repeats);
        k.Act = act;
        if (accent) k.Tag = "accent";
        return k;
    }

    private KeyButton NewKey(string label, double width, string id, string? hint = null, string? character = null, bool repeats = false)
    {
        var text = new TextBlock { Text = label, FontSize = label.Length > 2 ? 22 : 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid();
        content.Children.Add(text);
        var k = new KeyButton
        {
            Style = (Style)Application.Current.FindResource("Key"),
            Width = Unit * width - 6, Height = KeyHeight,
            Id = id, Char = character, Repeats = repeats, Label = text,
        };
        if (hint is not null)
        {
            // The key is a fixed size, so the hint sits in its top-right corner over the main label.
            content.Width = k.Width - 6; content.Height = KeyHeight - 6;
            content.Children.Add(new TextBlock { Text = hint, FontSize = 14, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) });
        }
        k.Content = content;
        k.Click += (_, _) => Press(k);   // a mouse click; OK from the remote goes through HandleKey
        return k;
    }

    /// <summary>Shows letters in the case Shift gives them, and the Shift key's state.</summary>
    private void Relabel()
    {
        foreach (var k in AllKeys())
        {
            if (k.Char is { } ch) k.Label.Text = Typed(ch);
            else if (k.Id == "shift")
            {
                k.Label.Text = _shift == Shift.Caps ? "⇪" : "⇧";
                k.Tag = _shift == Shift.Off ? null : "accent";
            }
        }
    }

    private string Typed(string ch) => _layer == Layer.Letters && _shift != Shift.Off ? ch.ToUpperInvariant() : ch;

    private IEnumerable<KeyButton> AllKeys() =>
        _keyRows.Children.OfType<StackPanel>().SelectMany(r => r.Children.OfType<KeyButton>()).Concat(_chips.Children.OfType<KeyButton>());

    private KeyButton? FindKey(string id) => AllKeys().FirstOrDefault(k => k.Id == id);

    // ---------- what the keys do

    private void Press(KeyButton k)
    {
        k.Flash();
        if (k.Char is { } ch)
        {
            var s = Typed(ch);
            _target.Insert(s);
            _heldAlt = OskText.Alternate(s);
            if (_shift == Shift.Once) { _shift = Shift.Off; Relabel(); }
        }
        else k.Act?.Invoke();
    }

    /// <summary>⇧ once: the next letter is a capital. Twice: caps lock (⇪). Three times: off.</summary>
    private void CycleShift()
    {
        _shift = _shift switch { Shift.Off => Shift.Once, Shift.Once => Shift.Caps, _ => Shift.Off };
        Relabel();
    }

    private void SetLayer(Layer l)
    {
        // Keep the highlight in the same place on the new page instead of making it jump.
        var rows = KeyRows();
        var cur = FocusedKey();
        int r = cur is null ? -1 : rows.FindIndex(row => row.Contains(cur));
        int c = r < 0 ? 0 : rows[r].IndexOf(cur!);
        _layer = l; _shift = Shift.Off;
        Build();
        if (r < 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var now = KeyRows();
            if (r < now.Count && now[r].Count > 0) FocusKey(now[r][Math.Min(c, now[r].Count - 1)]);
        });
    }

    private void Finish(bool submit)
    {
        if (!_target.Secret && _purpose is not null && History.Remember(_purpose, _target.Text))
        {
            try { History.Save(AppPaths.TypingHistory); } catch (Exception ex) { AppPaths.LogLine("typing history: " + ex.Message); }
        }
        if (submit) _target.Submit();
        _done();
    }

    private void PickRecent(string entry)
    {
        _target.Clear();
        _target.Insert(entry);
        if (FindKey("done") is { } d) FocusKey(d);
    }

    // ---------- preview and recent entries

    private void Refresh()
    {
        var text = _target.Text;
        var shown = _target.Secret ? new string('●', text.Length) : text;
        var (before, after) = OskText.Window(shown, _target.Caret, 34);
        _preview.Inlines.Clear();
        _preview.Inlines.Add(new Run(before));
        // The cursor: a solid orange bar, so it cannot be mistaken for a typed "|".
        _preview.Inlines.Add(new InlineUIContainer(new Border { Width = 4, Height = 40, Margin = new Thickness(1, 0, 1, 0), Background = UiKit.B("Accent"), CornerRadius = new CornerRadius(2) }) { BaselineAlignment = BaselineAlignment.Center });
        _preview.Inlines.Add(new Run(after));
        if (text.Length == 0) _preview.Inlines.Add(new Run(" type with the keys below") { Foreground = UiKit.B("Dim"), FontSize = 22 });
        RenderChips();
    }

    private void RenderChips()
    {
        bool any = !_target.Secret && History.For(_purpose).Count > 0;
        var list = any ? History.Suggest(_purpose, _target.Text, 3).ToList() : new List<string>();
        // Keep the row's height while this kind of field has history, so the keys do not jump as suggestions come and go.
        _chips.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _chips.Height = any ? KeyHeight - 4 : double.NaN;
        if (list.SequenceEqual(_shownChips)) return;
        bool hadFocus = FocusedKey() is { } f && _chips.Children.Contains(f);
        _shownChips = list;
        _chips.Children.Clear();
        foreach (var entry in list)
        {
            var e = entry;
            var k = new KeyButton
            {
                Style = (Style)Application.Current.FindResource("Key"),
                Height = KeyHeight - 10, MaxWidth = 270, Id = "recent",
                Label = new TextBlock { Text = "↺  " + e, FontSize = 20, Margin = new Thickness(16, 0, 16, 0) },
                Act = () => PickRecent(e),
            };
            k.Content = k.Label;
            k.Click += (_, _) => Press(k);
            _chips.Children.Add(k);
        }
        if (hadFocus) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusStart());
    }

    // ---------- the remote

    /// <summary>
    /// Handles a key while the keyboard is open. The host window calls this first for every key and stops if it returns true.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up or Key.Down or Key.Left or Key.Right:
                Move(e.Key);
                return true;
            case Key.Tab:
                Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Key.Left : Key.Right);
                return true;
            case Key.Enter:
                OnOk(e.IsRepeat);
                return true;
            case Key.Escape or Key.BrowserBack:
                _close();
                return true;
            case Key.Back:
                _target.Backspace();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Text typed on a real keyboard (or the phone's keyboard tab) goes straight into the field.</summary>
    public bool TypeText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl)) return false;
        _target.Insert(text);
        return true;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (!e.Handled && TypeText(e.Text)) e.Handled = true;
    }

    /// <summary>
    /// OK pressed: type the key. OK held (the key repeats): ⌫ ◀ ▶ repeat, and a character key is swapped once for its
    /// alternate (capital, or the small symbol). Repeats that did not start on this key are ignored, so a held OK
    /// that opened the keyboard does not type.
    /// </summary>
    private void OnOk(bool repeat)
    {
        var k = FocusedKey();
        if (k is null) { FocusStart(); return; }
        if (!repeat) { _held = k; _heldAlt = null; Press(k); return; }
        if (!ReferenceEquals(_held, k)) return;
        if (k.Repeats) { k.Flash(); k.Act?.Invoke(); }
        else if (_heldAlt is { } alt)
        {
            _heldAlt = null;
            _target.Backspace();
            _target.Insert(alt);
            k.Flash();
        }
    }

    private KeyButton? FocusedKey() => Keyboard.FocusedElement is KeyButton k && IsAncestorOf(k) ? k : null;

    private List<List<KeyButton>> KeyRows() =>
        _keyRows.Children.OfType<StackPanel>().Select(r => r.Children.OfType<KeyButton>().ToList()).ToList();

    /// <summary>The rows the arrows move through: the recent entries (when shown), then the keys.</summary>
    private List<List<KeyButton>> NavRows()
    {
        var rows = KeyRows();
        var chips = _chips.Children.OfType<KeyButton>().ToList();
        if (_chips.IsVisible && chips.Count > 0) rows.Insert(0, chips);
        return rows;
    }

    /// <summary>
    /// ◀ ▶ move along the row and wrap round at its ends. ▲ ▼ move to the next row (wrapping from the bottom to the top)
    /// and land on the key under the same spot, so long keys and short rows do not lose the column.
    /// </summary>
    private void Move(Key key)
    {
        var rows = NavRows();
        var cur = FocusedKey();
        int r = cur is null ? -1 : rows.FindIndex(row => row.Contains(cur));
        if (cur is null || r < 0) { FocusStart(); return; }
        if (key is Key.Left or Key.Right)
        {
            var row = rows[r];
            _prefX = null;
            FocusKey(row[OskText.Wrap(row.IndexOf(cur), key == Key.Right ? 1 : -1, row.Count)], keepColumn: false);
            return;
        }
        var span = Span(cur);
        double x = _prefX is double px && px >= span.Left && px <= span.Right ? px : (span.Left + span.Right) / 2;
        var next = rows[OskText.Wrap(r, key == Key.Down ? 1 : -1, rows.Count)];
        int i = OskText.Nearest(next.Select(Span).ToList(), x);
        if (i < 0) return;
        FocusKey(next[i], keepColumn: true);
        _prefX = x;
    }

    private (double Left, double Right) Span(KeyButton k)
    {
        try
        {
            var a = k.TranslatePoint(new Point(0, 0), this);
            var b = k.TranslatePoint(new Point(k.ActualWidth, 0), this);
            return (a.X, b.X);
        }
        catch (InvalidOperationException) { return (0, 0); }
    }

    private void FocusKey(KeyButton k, bool keepColumn = false)
    {
        if (!keepColumn) _prefX = null;
        k.Focus();
        Keyboard.Focus(k);
    }

    /// <summary>Where the highlight starts: on Done when the field already holds text, otherwise on the first letter.</summary>
    private void FocusStart()
    {
        var start = !OskText.IsJustPrefix(_target.Text) ? FindKey("done") : null;
        start ??= FindKey("kq") ?? KeyRows().Skip(1).FirstOrDefault()?.FirstOrDefault();
        if (start is not null) FocusKey(start);
    }

    public void FocusFirstKey() => FocusStart();
}
