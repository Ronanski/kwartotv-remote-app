using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KwartoTV.App.Ui;

/// <summary>
/// TV-style arrow-key navigation: the focus moves to the nearest control in the direction pressed, row by row,
/// the way a TV home screen works. WPF's own arrow-key handling gets stuck in scrolling rows and between panels,
/// so the remote could not reach parts of the screen.
/// </summary>
public static class SpatialNav
{
    /// <summary>Moves the focus inside [scope]. Returns false when nothing lies in that direction.</summary>
    public static bool Move(FrameworkElement scope, Key key)
    {
        var target = Target(scope, Keyboard.FocusedElement as FrameworkElement, key);
        return target is not null && target.Focus();
    }

    /// <summary>The control an arrow key moves to from [current] (null or outside: the top-left control), or null.</summary>
    public static FrameworkElement? Target(FrameworkElement scope, FrameworkElement? current, Key key)
    {
        var all = Focusables(scope).ToList();
        if (all.Count == 0) return null;
        if (current is null || !all.Contains(current)) return First(scope, all);
        var from = RectOf(current, scope);
        if (from is null) return null;
        var c = from.Value;

        var scored = new List<(FrameworkElement El, double Primary, double Secondary)>();
        foreach (var el in all)
        {
            if (ReferenceEquals(el, current)) continue;
            var rr = RectOf(el, scope);
            if (rr is null) continue;
            var r = rr.Value;
            double primary, secondary; bool inDirection;
            switch (key)
            {
                case Key.Right: inDirection = r.Left >= c.Right - 4 || (CenterX(r) > CenterX(c) + 4 && r.Left > c.Left + 4); primary = Math.Max(0, r.Left - c.Right); secondary = AxisGap(r.Top, r.Bottom, c.Top, c.Bottom, CenterY(r), CenterY(c)); break;
                case Key.Left: inDirection = r.Right <= c.Left + 4 || (CenterX(r) < CenterX(c) - 4 && r.Right < c.Right - 4); primary = Math.Max(0, c.Left - r.Right); secondary = AxisGap(r.Top, r.Bottom, c.Top, c.Bottom, CenterY(r), CenterY(c)); break;
                case Key.Down: inDirection = r.Top >= c.Bottom - 4 || (CenterY(r) > CenterY(c) + 4 && r.Top > c.Top + 4); primary = Math.Max(0, r.Top - c.Bottom); secondary = AxisGap(r.Left, r.Right, c.Left, c.Right, CenterX(r), CenterX(c)); break;
                case Key.Up: inDirection = r.Bottom <= c.Top + 4 || (CenterY(r) < CenterY(c) - 4 && r.Bottom < c.Bottom - 4); primary = Math.Max(0, c.Top - r.Bottom); secondary = AxisGap(r.Left, r.Right, c.Left, c.Right, CenterX(r), CenterX(c)); break;
                default: return null;
            }
            if (inDirection) scored.Add((el, primary, secondary));
        }
        if (scored.Count == 0) return null;
        // Left/Right stays on the same row when anything is there (the top bar's Power button sits far right, past the
        // cards below; it must be reachable with ▶ from the tabs, not lost to a nearer card one row down).
        if (key is Key.Left or Key.Right)
        {
            var sameRow = scored.Where(s => RectOf(s.El, scope) is { } rr && rr.Top < c.Bottom - 2 && c.Top < rr.Bottom - 2).ToList();
            if (sameRow.Count > 0) scored = sameRow;
        }
        // The nearest row/column first (like a TV), then the control most in line with the current one.
        double nearest = scored.Min(s => s.Primary);
        return scored.Where(s => s.Primary <= nearest + 40).OrderBy(s => s.Secondary).ThenBy(s => s.Primary).First().El;
    }

    private static FrameworkElement? First(FrameworkElement scope, List<FrameworkElement> all) =>
        all.Select(e => (e, r: RectOf(e, scope))).Where(x => x.r.HasValue)
           .OrderBy(x => Math.Round(x.r!.Value.Top / 20)).ThenBy(x => x.r!.Value.Left).Select(x => x.e).FirstOrDefault();

    /// <summary>
    /// The controls in [scope] that the arrow keys can never reach from the first control (the remote could not use
    /// them). Used by the navigation check (--nav-audit).
    /// </summary>
    public static IReadOnlyList<FrameworkElement> Unreachable(FrameworkElement scope, out int total)
    {
        var all = Focusables(scope).ToList();
        total = all.Count;
        if (all.Count == 0) return Array.Empty<FrameworkElement>();
        var seen = new HashSet<FrameworkElement>();
        var queue = new Queue<FrameworkElement>();
        if (First(scope, all) is { } start) { seen.Add(start); queue.Enqueue(start); }
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var key in new[] { Key.Up, Key.Down, Key.Left, Key.Right })
                if (Target(scope, at, key) is { } next && seen.Add(next)) queue.Enqueue(next);
        }
        return all.Where(e => !seen.Contains(e)).ToList();
    }

    /// <summary>A readable name for a control (its label), for the navigation check's report.</summary>
    public static string NameOf(FrameworkElement e)
    {
        var n = System.Windows.Automation.AutomationProperties.GetName(e);
        if (!string.IsNullOrWhiteSpace(n)) return n;
        if (e is ContentControl { Content: string s }) return s;
        var texts = new List<string>();
        var stack = new Stack<DependencyObject>(); stack.Push(e);
        while (stack.Count > 0 && texts.Count < 3)
        {
            var o = stack.Pop();
            if (o is TextBlock { Text: { Length: > 0 } t }) texts.Add(t);
            for (int i = VisualTreeHelper.GetChildrenCount(o) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(o, i));
        }
        return texts.Count > 0 ? string.Join(" ", texts) : e.GetType().Name;
    }

    private static double CenterX(Rect r) => r.Left + r.Width / 2;
    private static double CenterY(Rect r) => r.Top + r.Height / 2;

    /// <summary>0 when the two ranges overlap (same row/column), otherwise how far apart their centres are.</summary>
    private static double AxisGap(double a1, double a2, double b1, double b2, double ca, double cb) =>
        a1 < b2 && b1 < a2 ? Math.Abs(ca - cb) * 0.25 : Math.Abs(ca - cb);

    private static Rect? RectOf(FrameworkElement e, Visual scope)
    {
        try { return e.TransformToAncestor(scope).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Visible, enabled buttons and text boxes under [root].</summary>
    public static IEnumerable<FrameworkElement> Focusables(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            if (o is UIElement { IsVisible: false }) continue;
            if (o is FrameworkElement fe && fe.Focusable && fe.IsEnabled && fe.ActualWidth > 0 && fe is System.Windows.Controls.Primitives.ButtonBase or TextBox)
            {
                yield return fe;
                if (fe is TextBox) continue;   // its insides are not separate stops
            }
            int n = VisualTreeHelper.GetChildrenCount(o);
            for (int i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(o, i));
        }
    }
}
