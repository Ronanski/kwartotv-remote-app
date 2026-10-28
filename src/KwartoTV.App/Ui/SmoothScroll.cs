using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KwartoTV.App.Ui;

/// <summary>
/// Makes scrolling glide instead of jump when the focus moves (arrow keys, remote). WPF scrolls instantly by default,
/// which feels dead on a TV. Attach to the content panel inside a ScrollViewer (and to row strips inside rows).
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty VerticalProperty = DependencyProperty.RegisterAttached("Vertical", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0d, (d, e) => { if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue); }));

    public static readonly DependencyProperty HorizontalProperty = DependencyProperty.RegisterAttached("Horizontal", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0d, (d, e) => { if (d is ScrollViewer sv) sv.ScrollToHorizontalOffset((double)e.NewValue); }));

    private const int Milliseconds = 260;

    public static void Attach(FrameworkElement scope) => scope.RequestBringIntoView += OnBringIntoView;

    private static void OnBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is not FrameworkElement target || !target.IsVisible) return;
        bool any = false;
        for (DependencyObject? p = VisualTreeHelper.GetParent(target); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is not ScrollViewer sv) continue;
            Rect r;
            try { r = target.TransformToAncestor(sv).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
            catch (InvalidOperationException) { continue; }

            if (sv.ScrollableHeight > 0)
            {
                double h = sv.ViewportHeight, margin = 36;
                if (r.Top < margin || r.Bottom > h - margin)
                {
                    // Keep the focused row a little below the top, like the shelf on a TV home screen.
                    double to = Math.Clamp(sv.VerticalOffset + r.Top - h * 0.2, 0, sv.ScrollableHeight);
                    Animate(sv, VerticalProperty, sv.VerticalOffset, to);
                    any = true;
                }
            }
            if (sv.ScrollableWidth > 0)
            {
                double w = sv.ViewportWidth, margin = 60;
                if (r.Left < margin || r.Right > w - margin)
                {
                    double to = r.Left < margin ? sv.HorizontalOffset + r.Left - margin : sv.HorizontalOffset + r.Right - w + margin;
                    Animate(sv, HorizontalProperty, sv.HorizontalOffset, Math.Clamp(to, 0, sv.ScrollableWidth));
                    any = true;
                }
            }
        }
        if (any) e.Handled = true; // we scrolled; do not let the default (instant) scroll run too
    }

    private static void Animate(ScrollViewer sv, DependencyProperty prop, double from, double to)
    {
        if (Math.Abs(from - to) < 0.5) return;
        sv.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
