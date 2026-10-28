using System.Windows;
using System.Windows.Controls;
using KwartoTV.App.Ui;
using KwartoTV.Core;

namespace KwartoTV.App.Views;

/// <summary>
/// Shows every channel found in one playlist so the user can choose which ones go into the lineup.
/// OK on a row ticks it; the ▶ button plays it to check it works; Done saves.
/// </summary>
public sealed class ChannelPickerView : PageBase
{
    private const int PageSize = 50;

    private readonly string _playlistId;
    private readonly PlaylistEntry _playlist;
    private readonly IReadOnlyList<Channel> _all;
    private readonly HashSet<string> _selected;
    private readonly List<string> _groups;
    private string _query = "";
    private int _group = -1;   // -1 = all groups
    private int _limit = PageSize;
    private StackPanel _list = new();
    private TextBlock _count = new();

    public ChannelPickerView(MainWindow shell, string playlistId) : base(shell)
    {
        _playlistId = playlistId;
        _playlist = shell.Tv.Settings.FindPlaylist(playlistId) ?? new PlaylistEntry(playlistId, "Playlist", "");
        _all = shell.Tv.Library.GetValueOrDefault(playlistId) ?? Array.Empty<Channel>();
        _selected = _playlist.Selected is null ? _all.Select(c => c.Id).ToHashSet() : _playlist.Selected.ToHashSet();
        _groups = _all.Select(c => c.Group).Distinct().OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        Unloaded += (_, _) => Commit();
    }

    public override void Refresh() => Rebuild(Build);

    /// <summary>Saves the choice and rebuilds the numbered lineup. Safe to call more than once.</summary>
    private void Commit()
    {
        var ordered = _all.Where(c => _selected.Contains(c.Id)).Select(c => c.Id).ToList();
        Shell.Tv.Settings.SetSelection(_playlistId, ordered);
        Shell.Tv.SaveSettings();
        Shell.Tv.RebuildLineup();
    }

    private IEnumerable<Channel> Matches() =>
        _all.Where(c => (_group < 0 || c.Group == _groups[_group]) &&
                        (_query.Length == 0 || c.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)));

    private void Build()
    {
        Body.Children.Add(new TextBlock { Text = "Choose channels", FontSize = 40, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        Body.Children.Add(UiKit.Dimmed($"{_playlist.Name}  ·  {_all.Count} channels found. Press OK on a channel to add or remove it. ▶ plays it so you can test it.", 18));
        _count = new TextBlock { FontSize = 20, Foreground = UiKit.B("Accent"), Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(_count);

        if (_all.Count == 0)
        {
            var err = Shell.Tv.ErrorFor(_playlistId);
            Body.Children.Add(UiKit.Dimmed(err is null ? "No channels were found in this playlist." : "⚠ " + err, 20));
            Body.Children.Add(Row(UiKit.ActionButton("Reload", async () => { await Shell.Tv.LoadPlaylistAsync(_playlist); Shell.ShowChannelPicker(_playlistId); }),
                                  UiKit.ActionButton("Back", Shell.ShowSettings)));
            return;
        }

        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
        controls.Children.Add(UiKit.ActionButton(_query.Length == 0 ? "🔍  Search" : $"🔍  {_query}", OpenSearch));
        controls.Children.Add(UiKit.ActionButton("Group: " + (_group < 0 ? "All" : _groups[_group]), () => { _group = _group + 1 >= _groups.Count ? -1 : _group + 1; _limit = PageSize; Refresh(); }));
        controls.Children.Add(UiKit.ActionButton("Select all shown", () => { foreach (var c in Matches()) _selected.Add(c.Id); RenderList(); UpdateCount(); }));
        controls.Children.Add(UiKit.ActionButton("Clear shown", () => { foreach (var c in Matches()) _selected.Remove(c.Id); RenderList(); UpdateCount(); }));
        controls.Children.Add(UiKit.ActionButton("Done", () => { Commit(); Shell.ShowToast($"{_selected.Count} channels in your lineup"); Shell.ShowSettings(); }));
        Body.Children.Add(controls);

        _list = new StackPanel();
        Body.Children.Add(_list);
        RenderList();
        UpdateCount();
    }

    private void UpdateCount() => _count.Text = $"{_selected.Count} selected of {_all.Count}";

    private void OpenSearch()
    {
        var box = new TextBox { Text = _query };
        box.TextChanged += (_, _) => { _query = box.Text.Trim(); _limit = PageSize; RenderList(); };
        Shell.ShowKeyboard(box, onDone: () => { Refresh(); });
    }

    private void RenderList()
    {
        _list.Children.Clear();
        var matches = Matches().ToList();
        foreach (var ch in matches.Take(_limit)) _list.Children.Add(MakeRow(ch));
        if (matches.Count > _limit)
        {
            var more = UiKit.ActionButton($"Show more ({matches.Count - _limit} more)", () => { _limit += PageSize; RenderList(); });
            more.Margin = new Thickness(0, 10, 0, 0);
            _list.Children.Add(more);
        }
        else if (matches.Count == 0) _list.Children.Add(UiKit.Dimmed("Nothing matches. Change the search or the group.", 20));
    }

    private FrameworkElement MakeRow(Channel ch)
    {
        var check = new TextBlock { FontSize = 26, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"), Width = 44, VerticalAlignment = VerticalAlignment.Center };
        void Paint() { check.Text = _selected.Contains(ch.Id) ? "☑" : "☐"; check.Foreground = _selected.Contains(ch.Id) ? UiKit.B("Accent") : UiKit.B("Dim"); }
        Paint();

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = ch.Name, FontSize = 22, FontWeight = FontWeights.SemiBold });
        label.Children.Add(new TextBlock { Text = ch.Group, FontSize = 14, Foreground = UiKit.B("Dim") });
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(check, Dock.Left);
        content.Children.Add(check); content.Children.Add(label);

        var main = new Button { Content = content, Style = (Style)Application.Current.FindResource("Pill"), Tag = "action", HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 8, 14, 8) };
        main.Click += (_, _) => { if (!_selected.Remove(ch.Id)) _selected.Add(ch.Id); Paint(); UpdateCount(); };

        var test = new Button { Content = new TextBlock { Text = "▶", FontSize = 24 }, Style = (Style)Application.Current.FindResource("Pill"), Tag = "action", Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0) };
        test.Click += (_, _) => { Commit(); Shell.PreviewChannel(ch); };
        System.Windows.Automation.AutomationProperties.SetName(test, "Play " + ch.Name);

        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
        DockPanel.SetDock(test, Dock.Right);
        row.Children.Add(test); row.Children.Add(main);
        return row;
    }

    private static FrameworkElement Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }
}
