using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KwartoTV.App.Ui;

/// <summary>Where the on-screen keyboard sends its letters: a text box in KwartoTV, or a field on a web page.</summary>
public interface ITextTarget
{
    void Insert(string text);
    void Backspace();
    void Clear();
    /// <summary>Enter / submit.</summary>
    void Submit();
}

public sealed class TextBoxTarget : ITextTarget
{
    private readonly TextBox _box;
    public TextBoxTarget(TextBox box) => _box = box;

    public void Insert(string text)
    {
        int at = Math.Clamp(_box.CaretIndex, 0, _box.Text.Length);
        _box.Text = _box.Text.Insert(at, text);
        _box.CaretIndex = at + text.Length;
    }

    public void Backspace()
    {
        int at = Math.Clamp(_box.CaretIndex, 0, _box.Text.Length);
        if (at == 0) return;
        _box.Text = _box.Text.Remove(at - 1, 1);
        _box.CaretIndex = at - 1;
    }

    public void Clear() { _box.Text = ""; }
    public void Submit() { }
}

/// <summary>
/// A TV-style on-screen keyboard. Move with the arrow keys (or the phone's d-pad), press OK to type.
/// Layers: lowercase, shift (one capital), and numbers/symbols.
/// </summary>
public sealed class OnScreenKeyboard : Border
{
    private enum Layer { Lower, Upper, Symbols }

    private readonly ITextTarget _target;
    private readonly Action _done;
    private readonly StackPanel _rows = new();
    private Layer _layer = Layer.Lower;
    private string? _keepFocus;

    public OnScreenKeyboard(ITextTarget target, Action done)
    {
        _target = target; _done = done;
        Background = UiKit.B("Surface");
        BorderBrush = UiKit.B("Line");
        BorderThickness = new Thickness(0, 2, 0, 0);
        Padding = new Thickness(40, 14, 40, 18);
        Child = new Border { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Center, Child = _rows };
        Build();
    }

    private static readonly string[] Lower = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm,." };
    private static readonly string[] Symbols = { "1234567890", "!@#$%^&*()", "_=+[]{};:'", "\"?/\\|<>~`," };

    private void Build()
    {
        var focusedName = _keepFocus;
        _rows.Children.Clear();
        var keys = _layer switch { Layer.Symbols => Symbols, Layer.Upper => Lower.Select(r => r.ToUpperInvariant()).ToArray(), _ => Lower };

        // rows 1-3: character keys; row 1 also has backspace
        for (int r = 0; r < 3; r++)
        {
            var row = Row();
            foreach (var ch in keys[r]) row.Children.Add(CharKey(ch.ToString()));
            if (r == 0) row.Children.Add(Wide("⌫", 1.6, () => _target.Backspace(), repeat: true, name: "backspace"));
            _rows.Children.Add(row);
        }

        // row 4: shift (letters) or ABC (symbols), characters
        var r4 = Row();
        if (_layer == Layer.Symbols) r4.Children.Add(Wide("ABC", 1.6, () => SetLayer(Layer.Lower), name: "layer"));
        else r4.Children.Add(Wide(_layer == Layer.Upper ? "⇩" : "⇧", 1.6, () => SetLayer(_layer == Layer.Upper ? Layer.Lower : Layer.Upper), name: "shift"));
        foreach (var ch in keys[3]) r4.Children.Add(CharKey(ch.ToString()));
        _rows.Children.Add(r4);

        // row 5
        var r5 = Row();
        r5.Children.Add(Wide(_layer == Layer.Symbols ? "abc" : "?123", 1.5, () => SetLayer(_layer == Layer.Symbols ? Layer.Lower : Layer.Symbols), name: "symbols"));
        r5.Children.Add(CharKey("@"));
        r5.Children.Add(CharKey("/"));
        r5.Children.Add(CharKey(":"));
        r5.Children.Add(Wide("space", 3.4, () => _target.Insert(" "), name: "space"));
        r5.Children.Add(Wide(".com", 1.5, () => _target.Insert(".com"), name: "com"));
        r5.Children.Add(Wide("Clear", 1.4, () => _target.Clear(), name: "clear"));
        r5.Children.Add(Wide("Done", 1.5, _done, accent: true, name: "done"));
        _rows.Children.Add(r5);

        if (focusedName is not null) Dispatcher.BeginInvoke(() => FindKey(focusedName)?.Focus());
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

    private Button CharKey(string ch)
    {
        var b = NewKey(ch, 1.0, name: "k" + ch);
        b.Click += (_, _) =>
        {
            _target.Insert(ch);
            if (_layer == Layer.Upper) { _keepFocus = "k" + ch.ToLowerInvariant(); SetLayer(Layer.Lower); }
        };
        return b;
    }

    private Button Wide(string label, double width, Action act, bool accent = false, bool repeat = false, string? name = null)
    {
        var b = NewKey(label, width, name: name);
        if (accent) b.Foreground = UiKit.B("Accent");
        b.Click += (_, _) => act();
        if (repeat) b.PreviewKeyDown += (_, e) => { if (e.IsRepeat && e.Key is Key.Enter or Key.Space) { act(); e.Handled = true; } };
        return b;
    }

    private static Button NewKey(string label, double width, string? name) => new()
    {
        Style = (Style)Application.Current.FindResource("Key"),
        Content = new TextBlock { Text = label, FontSize = label.Length > 2 ? 22 : 28 },
        Width = 64 * width + (width > 1 ? (width - 1) * 6 : 0), Height = 62,
        Tag = name,
    };

    private Button? FindKey(string name) =>
        _rows.Children.OfType<StackPanel>().SelectMany(r => r.Children.OfType<Button>()).FirstOrDefault(b => (string?)b.Tag == name);

    private void SetLayer(Layer l)
    {
        // Remember the focused key so the keyboard does not make the highlight jump when the layout redraws.
        if (_keepFocus is null && Keyboard.FocusedElement is Button { Tag: string t }) _keepFocus = t;
        _layer = l; Build(); _keepFocus = null;
    }

    public void FocusFirstKey() => FindKey("k" + (_layer == Layer.Upper ? "Q" : "q"))?.Focus();
}
