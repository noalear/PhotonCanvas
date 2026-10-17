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

/// <summary>
/// Warp the selected outlines: taper (perspective), arch, bulge or wave, previewed live on the
/// canvas. Text and codes become plain outlines.
/// </summary>
public sealed class WarpWindow : Window
{
    readonly Document _doc;
    readonly DocState _base;
    readonly List<PathShape> _shapes;
    readonly Rect2 _box;
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Perspective (taper)", "Arch", "Bulge or pinch", "Wave" } };
    readonly Slider _a = new() { Minimum = -100, Maximum = 100, Value = 30 }, _b = new() { Minimum = -100, Maximum = 100, Value = 0 };
    readonly NumericUpDown _waves = Number(0.5m, 20, 0.5m, "0.#");
    readonly TextBlock _aLabel = new() { Classes = { "muted" } }, _bLabel = new() { Classes = { "muted" } }, _info = Hint();
    readonly Control _bRow, _wavesRow;
    bool _done;

    public WarpWindow(Document doc)
    {
        _doc = doc;
        _base = doc.State;
        _shapes = [.. doc.State.Shapes.Where(s => doc.Selection.Contains(s.Id)).OfType<PathShape>()];
        _box = _base.Bounds(_shapes.Select(s => s.Id));
        Title = "Warp";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _mode.SelectedIndex = 0;
        _waves.Value = 2;
        _bRow = new StackPanel { Children = { _bLabel, _b } };
        _wavesRow = Field("Waves across", _waves);

        var ok = new Button { Content = "Warp", IsDefault = true, Classes = { "primary" }, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { _done = true; _doc.EndEdit($"Warp ({_mode.SelectedItem})"); Close(); };
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
                    Field("Warp", _mode),
                    new StackPanel { Children = { _aLabel, _a } },
                    _bRow,
                    _wavesRow,
                    _info,
                    Hint("Text and codes become plain outlines. Curves are followed to within 0.02 mm."),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                },
            },
        };
        _mode.SelectionChanged += (_, _) => { Labels(); Preview(); };
        _a.ValueChanged += (_, _) => Preview();
        _b.ValueChanged += (_, _) => Preview();
        _waves.ValueChanged += (_, _) => Preview();
        _doc.BeginEdit();
        Labels();
        Opened += (_, _) => Preview();
    }

    void Labels()
    {
        int m = _mode.SelectedIndex;
        _aLabel.Text = m switch { 0 => "Narrow the top (−: the bottom)", 1 => "Arch up (−: down)", 2 => "Bulge (−: pinch)", _ => "Wave height" };
        _bLabel.Text = "Narrow the left (−: the right)";
        _bRow.IsVisible = m == 0;
        _wavesRow.IsVisible = m == 3;
    }

    /// <summary>The warp as a point mapping over the selection's box.</summary>
    Func<Vec2, Vec2> Map()
    {
        var b = _box;
        double a = _a.Value / 100, k = _b.Value / 100, n = (double)(_waves.Value ?? 2);
        var c = b.Center;
        int mode = _mode.SelectedIndex;
        return p =>
        {
            double u = b.Width > 0 ? (p.X - b.MinX) / b.Width : 0.5, v = b.Height > 0 ? (p.Y - b.MinY) / b.Height : 0.5;
            switch (mode)
            {
                case 0:
                    double sx = 1 - 0.9 * a * (1 - v), sy = 1 - 0.9 * k * (1 - u);
                    return new Vec2(c.X + (p.X - c.X) * sx, c.Y + (p.Y - c.Y) * sy);
                case 1:
                    return new Vec2(p.X, p.Y - a * b.Height * 0.5 * Math.Sin(Math.PI * u));
                case 2:
                    double du = (u - 0.5) * 2, dv = (v - 0.5) * 2, r2 = Math.Min(1, du * du + dv * dv);
                    double f = 1 + 0.5 * a * (1 - r2);
                    return c + (p - c) * f;
                default:
                    return new Vec2(p.X, p.Y + a * b.Height * 0.25 * Math.Sin(2 * Math.PI * n * u));
            }
        };
    }

    void Preview()
    {
        if (_box.IsEmpty) return;
        var map = Map();
        var s = _base;
        foreach (var sh in _shapes)
            s = Ops.Replace(s, new PathShape { Id = sh.Id, Name = sh.Name, LayerId = sh.LayerId, GroupId = sh.GroupId, Geometry = PathTools.Warp(sh.World, map) });
        _doc.UpdateEdit(s);
        _info.Text = $"{_shapes.Count} shape{(_shapes.Count == 1 ? "" : "s")}, {_box.Width:0.#} × {_box.Height:0.#} mm before warping.";
    }
}

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
                    Row(Field("From Ø mm", _min), Field("to", _max)),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, select } },
                },
            },
        };
    }

    void SelectRange(double min, double max) => _doc.Select(_circles.Where(c => c.D >= min - 1e-9 && c.D <= max + 1e-9).Select(c => c.Id));
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
