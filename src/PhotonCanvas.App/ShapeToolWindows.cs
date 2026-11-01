using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>Copies of the selection spaced along a path, previewed live.</summary>
public sealed class CopyAlongWindow : Window
{
    readonly Document _doc;
    readonly DocState _base;
    readonly HashSet<Guid> _ids;
    readonly List<Vec2> _path;
    readonly bool _closed;
    readonly NumericUpDown _count = Number(1, 5000, 1, "0");
    readonly CheckBox _follow = new() { Content = "Turn each copy to follow the path", IsChecked = true };
    readonly CheckBox _remove = new() { Content = "Remove the original", IsChecked = true };
    readonly TextBlock _info = Hint();
    List<Guid> _made = [];
    bool _done;

    /// <param name="guide">The path to copy along (its own shape is not copied).</param>
    public CopyAlongWindow(Document doc, PathShape guide)
    {
        _doc = doc;
        _base = doc.State;
        _ids = [.. doc.Selection.Where(id => id != guide.Id)];
        var sub = guide.World.Subpaths.First(s => !s.Segs.IsEmpty);
        _path = sub.Flatten(0.01);
        _closed = sub.Closed;
        Title = "Copy along a path";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _count.Value = 8;
        var ok = new Button { Content = "Make copies", IsDefault = true, Classes = { "primary" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { _done = true; _doc.EndEdit(Title); _doc.Select(_made); Close(); };
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => { if (!_done) _doc.CancelEdit(); };
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Field("Copies", _count, tip: _closed ? "Spread evenly all the way round." : "From one end of the path to the other."),
                    _follow,
                    _remove,
                    _info,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                },
            },
        };
        _count.ValueChanged += (_, _) => Preview();
        _follow.IsCheckedChanged += (_, _) => Preview();
        _remove.IsCheckedChanged += (_, _) => Preview();
        _doc.BeginEdit();
        Opened += (_, _) => Preview();
    }

    void Preview()
    {
        var (s, made) = PathTools.CopyAlong(_base, _ids, _path, _closed, (int)(_count.Value ?? 8), _follow.IsChecked == true, _remove.IsChecked == true);
        _made = made;
        _doc.UpdateEdit(s);
        double len = PathTools.CumulativeLengths(_closed ? [.. _path, _path[0]] : _path)[^1];
        _info.Text = $"Path {len:0.#} mm long ({(_closed ? "closed" : "open")}): a copy every {len / Math.Max(1, _closed ? (double)(_count.Value ?? 8) : (double)(_count.Value ?? 8) - 1):0.##} mm.";
    }
}

/// <summary>Find the circles in the design by size and select them (holes for pins, magnets…).</summary>
public sealed class CirclesWindow : Window
{
    readonly Document _doc;
    readonly List<(Guid Id, double D)> _circles;
    readonly NumericUpDown _min = Number(0, 1000, 0.1m, "0.###"), _max = Number(0, 1000, 0.1m, "0.###");
    readonly StackPanel _sizes = new() { Spacing = 4 };
    readonly TextBlock _info = Hint();

    public CirclesWindow(Document doc)
    {
        _doc = doc;
        _circles = [.. doc.State.Editable().OfType<PathShape>().Where(s => s is not (TextShape or CodeShape))
            .Select(s => (s.Id, PathTools.Circle(s.World)?.Diameter)).Where(x => x.Item2 is not null).Select(x => (x.Id, x.Item2!.Value))];
        Title = "Select circles";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var select = new Button { Content = "Select", IsDefault = true, Classes = { "primary" }, MinWidth = 100 };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        select.Click += (_, _) => { SelectRange((double)(_min.Value ?? 0), (double)(_max.Value ?? 0)); Close(); };
        close.Click += (_, _) => Close();
        if (_circles.Count > 0)
        {
            _min.Value = (decimal)Math.Round(_circles.Min(c => c.D) - 0.005, 3);
            _max.Value = (decimal)Math.Round(_circles.Max(c => c.D) + 0.005, 3);
        }
        foreach (var g in _circles.GroupBy(c => Math.Round(c.D, 2)).OrderBy(g => g.Key))
        {
            var b = new Button { Content = $"{g.Count()} × Ø {g.Key:0.##} mm", HorizontalAlignment = HorizontalAlignment.Stretch };
            double d = g.Key;
            b.Click += (_, _) => { SelectRange(d - 0.005, d + 0.005); Close(); };
            _sizes.Children.Add(b);
        }
        _info.Text = _circles.Count == 0 ? "There are no circles in the design (on unlocked, visible layers)." : $"{_circles.Count} circles. Click a size to select those, or set a range.";
        // The range: both boxes full width so their numbers show, and what Select will pick spelled out.
        var range = new Grid { ColumnDefinitions = new("*,Auto,*"), ColumnSpacing = 8 };
        var to = new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        Grid.SetColumn(to, 1);
        Grid.SetColumn(_max, 2);
        range.Children.Add(_min);
        range.Children.Add(to);
        range.Children.Add(_max);
        var picks = Hint();
        void Picks()
        {
            int n = InRange((double)(_min.Value ?? 0), (double)(_max.Value ?? 0)).Count();
            picks.Text = $"Select picks {n} circle{(n == 1 ? "" : "s")} from Ø {_min.Value ?? 0:0.###} to {_max.Value ?? 0:0.###} mm.";
            select.IsEnabled = n > 0;
        }
        _min.ValueChanged += (_, _) => Picks();
        _max.ValueChanged += (_, _) => Picks();
        Picks();
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _info,
                    new ScrollViewer { MaxHeight = 280, Content = _sizes },
                    new StackPanel { Spacing = 4, Children = { Muted("Or every circle across (mm)"), range, picks } },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, select } },
                },
            },
        };
    }

    IEnumerable<Guid> InRange(double min, double max) => _circles.Where(c => c.D >= min - 1e-9 && c.D <= max + 1e-9).Select(c => c.Id);

    void SelectRange(double min, double max) => _doc.Select(InRange(min, max));
}

/// <summary>Every step of the undo history: click one to go back (or forward) to it. The canvas shows it straight away.</summary>
public sealed class HistoryWindow : Window
{
    readonly Document _doc;
    readonly ListBox _list = new();
    bool _filling;

    public HistoryWindow(Document doc)
    {
        _doc = doc;
        Title = "Undo history";
        Width = 340;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = "Click a step to go back to it; later steps stay below until you change something.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Margin = new(0, 0, 0, 8), [DockPanel.DockProperty] = Dock.Top },
                _list,
            },
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_filling || _list.SelectedIndex < 0) return;
            _doc.Step(_list.SelectedIndex - _doc.UndoLabels.Count);
        };
        _doc.Changed += Fill;
        Closed += (_, _) => _doc.Changed -= Fill;
        Fill(true);
    }

    void Fill(bool _)
    {
        _filling = true;
        try
        {
            var undo = _doc.UndoLabels;
            var redo = _doc.RedoLabels;
            var items = new List<Control> { Item("Start", false) };
            items.AddRange(undo.Select(l => Item(l, false)));
            items.AddRange(redo.Select(l => Item(l, true)));
            _list.ItemsSource = items;
            _list.SelectedIndex = undo.Count;
        }
        finally { _filling = false; }
    }

    static Control Item(string label, bool undone) => new TextBlock { Text = label, Opacity = undone ? 0.45 : 1, FontStyle = undone ? FontStyle.Italic : FontStyle.Normal };
}
