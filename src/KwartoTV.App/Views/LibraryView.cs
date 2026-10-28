using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KwartoTV.App.Ui;
using KwartoTV.Core;

namespace KwartoTV.App.Views;

/// <summary>Videos and music from the folders the user added. Playing uses the same libmpv player as Live TV.</summary>
public sealed class LibraryView : PageBase
{
    private IReadOnlyList<LibraryItem>? _items;
    private bool _scanning;

    public LibraryView(MainWindow shell) : base(shell) { }

    public override void Refresh()
    {
        if (_items is null && !_scanning) { _ = ScanAsync(); }
        Rebuild(Build);
    }

    private async Task ScanAsync()
    {
        _scanning = true;
        var folders = Shell.Tv.Settings.LibraryFolders.ToList();
        _items = await Task.Run(() => LibraryScanner.Scan(folders));
        _scanning = false;
        Rebuild(Build);
        if (!IsKeyboardFocusWithin) FocusFirst();
    }

    private void Build()
    {
        Body.Children.Add(new TextBlock { Text = "Library", FontSize = 40, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        var folders = Shell.Tv.Settings.LibraryFolders;
        if (folders.Count == 0)
        {
            Body.Children.Add(UiKit.Dimmed("Add a folder with your own videos or music and it will show up here.", 22));
            Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0), Children = { UiKit.ActionButton("Add a folder", AddFolder) } });
            return;
        }
        if (_items is null) { Body.Children.Add(UiKit.Dimmed("Looking for files…", 22)); return; }
        if (_items.Count == 0) Body.Children.Add(UiKit.Dimmed("No videos or music found in your folders.", 22));

        foreach (var group in _items.GroupBy(i => i.Folder))
        {
            var list = group.ToList();
            Body.Children.Add(UiKit.Row(group.Key, list.Take(60).Select((item, idx) => UiKit.Tile(UiKit.CardW, UiKit.CardH, Card(item), () => Shell.PlayFiles(list, idx), item.Title))));
        }
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0), Children = { UiKit.ActionButton("Add another folder", AddFolder), UiKit.ActionButton("Rescan", () => { _items = null; Refresh(); }) } });
    }

    private static FrameworkElement Card(LibraryItem item)
    {
        var g = new Grid { Background = new LinearGradientBrush(UiKit.Hue(item.Title), UiKit.Hue(item.Path), 45) };
        var ext = System.IO.Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant();
        g.Children.Add(new TextBlock { Text = "▶", FontSize = 52, Opacity = 0.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -14, 0, 0) });
        g.Children.Add(new TextBlock { Text = ext, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 10, 0, 0), Opacity = 0.8 });
        g.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0xCC, 0, 0, 0), 90), Height = 62, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
        g.Children.Add(new TextBlock { Text = item.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 10) });
        return g;
    }

    private void AddFolder()
    {
        var path = Dialogs.PickFolder();
        if (path is null) return;
        if (!Shell.Tv.Settings.LibraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) Shell.Tv.Settings.LibraryFolders.Add(path);
        Shell.Tv.SaveSettings();
        _items = null; Refresh();
    }
}
