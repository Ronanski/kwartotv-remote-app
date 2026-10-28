using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KwartoTV.App.Views;

public abstract class PageBase : UserControl
{
    protected readonly MainWindow Shell;
    protected readonly ScrollViewer Scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(56, 0, 56, 60) };
    protected readonly StackPanel Body = new();

    protected PageBase(MainWindow shell)
    {
        Shell = shell;
        Scroll.Content = Body;
        Content = Scroll;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Rebuild the page from current data. Called when channels or settings change.</summary>
    public abstract void Refresh();

    public void FocusFirst() => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

    /// <summary>Rebuild but keep the user roughly where they were.</summary>
    protected void Rebuild(Action build)
    {
        var offset = Scroll.VerticalOffset;
        var focusedIndex = FocusIndex();
        Body.Children.Clear();
        build();
        UpdateLayout();
        Scroll.ScrollToVerticalOffset(offset);
        RestoreFocus(focusedIndex);
    }

    private int FocusIndex() => Keyboard.FocusedElement is Button b ? AllButtons().ToList().IndexOf(b) : -1;

    private void RestoreFocus(int index)
    {
        var all = AllButtons().ToList();
        if (all.Count == 0) return;
        if (index >= 0 && index < all.Count) all[index].Focus(); else if (index == -1 && !IsKeyboardFocusWithin) { }
    }

    protected IEnumerable<Button> AllButtons()
    {
        var stack = new Stack<DependencyObject>(); stack.Push(Body);
        var list = new List<Button>();
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            if (o is Button b) list.Add(b);
            var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
            for (int i = n - 1; i >= 0; i--) stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
        }
        return list;
    }
}
