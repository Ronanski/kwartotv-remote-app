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
        var all = Focusables(scope).ToList();
        if (all.Count == 0) return false;
        var current = Keyboard.FocusedElement as FrameworkElement;
        if (current is null || !all.Contains(current))
        {
            // Nothing (or something outside this area) has the focus: start at the top-left control.
            var first = all.Select(e => (e, r: RectOf(e, scope))).Where(x => x.r.HasValue)
                           .OrderBy(x => Math.Round(x.r!.Value.Top / 20)).ThenBy(x => x.r!.Value.Left).Select(x => x.e).FirstOrDefault();
            return first is not null && first.Focus();
        }
        var from = RectOf(current, scope);
        if (from is null) return false;
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
                default: return false;
            }
            if (inDirection) scored.Add((el, primary, secondary));
        }
        if (scored.Count == 0) return false;
        // The nearest row/column first (like a TV), then the control most in line with the current one.
        double nearest = scored.Min(s => s.Primary);
        var best = scored.Where(s => s.Primary <= nearest + 40).OrderBy(s => s.Secondary).ThenBy(s => s.Primary).First().El;
        return best.Focus();
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
