using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Surfaces: the objects on the bed that marks go onto (pens, dowels, bottles on their side, raised flat pieces).
/// Each is a rectangle on the canvas; anything drawn inside it is marked on that object: wrapped round a round
/// one, focused on its top, and slowed where it slopes away. The head never moves from this window.
/// </summary>
public sealed class SurfacesWindow : Window
{
    readonly Document _doc;
    readonly MachinePanel _machine;
    MarkSurfaces _set;
    int _chosen = -1;
    bool _loading;

    readonly ListBox _list = new() { MinHeight = 90, MaxHeight = 170 };
    readonly SurfacesPlanView _plan = new() { MinHeight = 420 };
    readonly TextBox _name = new();
    readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Round (pen, dowel, bottle on its side)", "Flat, at its own height (lid, block)" } };
    readonly ComboBox _axis = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Left to right (along X)", "Front to back (along Y)" } };
    readonly NumericUpDown _cx = Number(-100, 320, 0.5m, "0.##"), _cy = Number(-100, 320, 0.5m, "0.##");
    readonly NumericUpDown _length = Number(1, 400, 1, "0.#");
    readonly NumericUpDown _dia = Number((decimal)MarkSurface.MinDiameterMm, (decimal)MarkSurface.MaxDiameterMm, 0.1m, "0.##");
    readonly NumericUpDown _width = Number(1, 400, 1, "0.#");
    readonly NumericUpDown _top = Number(0, (decimal)MarkSurface.MaxTopMm, 0.1m, "0.##");
    readonly NumericUpDown _reach = Number(5, (decimal)MarkSurface.MaxReachDeg, 5, "0");
    readonly CheckBox _wrap = new() { Content = "Wrap the design round it" };
    readonly CheckBox _even = new() { Content = "Even out the slopes (slow down where it curves away)" };
    readonly NumericUpDown _other = Number(0, (decimal)MarkSurface.MaxTopMm, 0.1m, "0.##");
    readonly NumericUpDown _band = Number(0.05m, 1, 0.05m, "0.##");
    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Control _editor, _roundOnly, _flatOnly;

    public SurfacesWindow(Document doc, MachinePanel machine)
    {
        _doc = doc;
        _machine = machine;
        _set = doc.State.Surfaces;
        Title = "Surfaces";
        Width = 1120;
        Height = 780;
        MinWidth = 880;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _other.Watermark = "not set";

        var addRound = Button("Add a round object", "IcPlus", "A pen, dowel, tube or bottle lying on its side. Placed on the selection, if anything is selected.");
        addRound.Click += (_, _) => Add(MarkSurfaceKind.Round);
        var addFlat = Button("Add a flat piece", "IcPlus", "A flat piece at its own height: a box lid, a block in a jig. Placed on the selection, if anything is selected.");
        addFlat.Click += (_, _) => Add(MarkSurfaceKind.Flat);
        var copy = Button("Copy", "IcDuplicate", "Another one the same, beside it (the next pen in a row).");
        copy.Click += (_, _) => Copy();
        var remove = Button("Remove", "IcTrash");
        remove.Click += (_, _) => Remove();
        var onSelection = Button("Centre it on the selection", tip: "Moves this surface so its middle is the middle of the selected shapes (or of the whole design).");
        onSelection.Click += (_, _) =>
        {
            var d = DesignBounds();
            if (d.IsEmpty) { _status.Text = "There's nothing on the canvas to centre it on."; return; }
            _cx.Value = (decimal)Math.Round(d.Center.X, 2);
            _cy.Value = (decimal)Math.Round(d.Center.Y, 2);
        };
        var topIsDia = Button("Lying on the bed: top = diameter", tip: "A round object lying straight on the bed: its top is its diameter above the bed.");
        topIsDia.Click += (_, _) => _top.Value = _dia.Value;
        var topFromFocus = Button("Use the focus height now", tip: "The focus height the machine has now (auto-focus on the object's top first, in the machine panel). The head doesn't move.");
        topFromFocus.Click += (_, _) =>
        {
            if (_machine.Distance is { } d) { _top.Value = (decimal)Math.Round(d, 2); _status.Text = $"Top set to the focus height, {d:0.##} mm."; }
            else _status.Text = _machine.IsConnected ? "The machine isn't focused yet: auto-focus on the object's top first." : "Not connected to the machine: type the height in.";
        };
        var otherFromFocus = Button("Use the focus height now", tip: "The focus height the machine has now. The head doesn't move.");
        otherFromFocus.Click += (_, _) =>
        {
            if (_machine.Distance is { } d) _other.Value = (decimal)Math.Round(d, 2);
            else _status.Text = _machine.IsConnected ? "The machine isn't focused yet." : "Not connected to the machine: type the height in.";
        };
        var clearOther = Button("Not set", tip: "Leave it unset: a job with marks off the surfaces then stops and asks.");
        clearOther.Click += (_, _) => _other.Value = null;

        _roundOnly = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Diameter (mm)", _dia, tip: "Its diameter where the marks go: measure it there with calipers (a pen tapers)."),
                Field("Reach round (°)", _reach, Hint("How far round from the top, each way, marks may go. Past about 60° the surface turns away and marks fade."),
                    tip: "Sets how wide its rectangle on the canvas is: that much of the surface each side of the top, unrolled."),
                _wrap,
                _even,
            },
        };
        _flatOnly = Field("Width (mm)", _width, tip: "How wide the flat piece is, across.");
        _editor = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Name", _name),
                Field("What it is", _kind),
                Field("Lies", _axis, tip: "Which way its length runs on the bed."),
                Field("Middle X (mm)", _cx, tip: "Where the middle of its rectangle is, from the left of the bed. On a round object, a point on its top line."),
                Field("Middle Y (mm)", _cy, tip: "Where the middle of its rectangle is, from the top (back) of the bed."),
                onSelection,
                Field("Length (mm)", _length, tip: "How much of its length takes marks."),
                _roundOnly,
                _flatOnly,
                Field("Top height (mm)", _top, tip: "How high its top is above the bed, as you'd focus on it."),
                Row(topIsDia, topFromFocus),
                _info,
            },
        };

        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _chosen = _list.SelectedIndex;
            Load();
        };
        foreach (var c in new[] { _kind, _axis }) c.SelectionChanged += (_, _) => Changed();
        foreach (var n in new[] { _cx, _cy, _length, _dia, _width, _top, _reach }) n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _wrap, _even }) c.IsCheckedChanged += (_, _) => Changed();
        _name.TextChanged += (_, _) => Changed();
        _other.ValueChanged += (_, _) => { _set = _set with { OtherMm = _other.Value is { } v ? (double)v : null }; Refresh(); };
        _band.ValueChanged += (_, _) => { _set = _set with { BandMm = (double)(_band.Value ?? 0.2m) }; Refresh(); };

        var apply = new Button { Content = "Apply", Classes = { "primary" }, MinWidth = 110 };
        apply.Click += (_, _) =>
        {
            if (_set != _doc.State.Surfaces) _doc.Commit("Surfaces", _doc.State with { Surfaces = _set });
            Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Hint("A surface is an object on the bed that marks go onto. Each one is a rectangle on the canvas (purple): anything you draw inside it is marked on that object. " +
                     "On a round object the rectangle is its top unrolled, so a 10 mm logo comes out 10 mm round the pen. The job focuses on each one and, on round ones, wraps the marks round them and slows down where they curve away."),
                _list,
                Row(addRound, addFlat),
                Row(copy, remove),
                Explained("This surface", "Measure it with calipers. The left side shows where the marks will land, seen from above.", _editor),
                Explained("Everything else", "Marks that aren't on any surface are focused at this height (the material under them). Leave it unset if everything is on a surface.",
                    Field("Everything else is at (mm)", _other), Row(otherFromFocus, clearOther)),
                Field("Height steps (mm)", _band, tip: "How far the head moves between heights on a round object. xTool Studio uses 0.2 mm; smaller follows it more closely and takes longer."),
                _status,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, apply } };
        var grid = new Grid { ColumnDefinitions = new("*,16,380") };
        grid.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _plan });
        var sideScroll = new ScrollViewer { Content = side };
        Grid.SetColumn(sideScroll, 2);
        grid.Children.Add(sideScroll);
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;

        _other.Value = _set.OtherMm is { } o ? (decimal)o : null;
        _band.Value = (decimal)_set.BandMm;
        _chosen = _set.Items.IsEmpty ? -1 : 0;
        FillList();
        Load();
    }

    /// <summary>The selection (or the whole design) on the canvas.</summary>
    Rect2 DesignBounds()
    {
        IReadOnlySet<Guid> ids = _doc.Selection.Count > 0 ? _doc.Selection : _doc.State.Shapes.Select(s => s.Id).ToHashSet();
        return _doc.State.Bounds(ids);
    }

    void Add(MarkSurfaceKind kind)
    {
        var sel = _doc.Selection.Count > 0 ? _doc.State.Bounds(_doc.Selection) : Rect2.Empty;
        var last = _chosen >= 0 && _chosen < _set.Items.Count ? _set.Items[_chosen] : null;
        var item = new MarkSurface
        {
            Name = _set.NextName(kind == MarkSurfaceKind.Round ? "Pen" : "Piece"),
            Kind = kind,
            Centre = sel.IsEmpty ? new Vec2(110, 110) : sel.Center,
            // Along the selection's longer side; else as the last one lies.
            AxisDeg = !sel.IsEmpty ? (sel.Height > sel.Width ? 90 : 0) : last?.AxisDeg ?? 0,
            LengthMm = !sel.IsEmpty ? Math.Round(Math.Max(sel.Width, sel.Height) + 10) : kind == MarkSurfaceKind.Round ? 60 : 40,
            DiameterMm = last?.Round == true ? last.DiameterMm : 10,
            TopMm = last?.Kind == kind ? last.TopMm : 10,
            WidthMm = !sel.IsEmpty ? Math.Round(Math.Min(sel.Width, sel.Height) + 10) : 30,
        };
        _set = _set with { Items = _set.Items.Add(item) };
        _chosen = _set.Items.Count - 1;
        FillList();
        Load();
        _status.Text = kind == MarkSurfaceKind.Round
            ? $"Added {item.Name}. Set its diameter and top height, then put what goes on it inside its rectangle on the canvas."
            : $"Added {item.Name}. Set its size and top height, then put what goes on it inside its rectangle on the canvas.";
    }

    void Copy()
    {
        if (_chosen < 0 || _chosen >= _set.Items.Count) { _status.Text = "Choose a surface to copy first."; return; }
        var was = _set.Items[_chosen];
        // Beside it, across its length, with a 2 mm gap.
        double across = was.Round ? Math.Max(was.DiameterMm, was.AcrossMm) + 2 : was.WidthMm + 2;
        double t = was.AxisDeg * Math.PI / 180;
        var item = was with { Id = Guid.NewGuid(), Name = _set.NextName(was.Round ? "Pen" : "Piece"), Centre = was.Centre + new Vec2(-Math.Sin(t), Math.Cos(t)) * across };
        _set = _set with { Items = _set.Items.Insert(_chosen + 1, item) };
        _chosen++;
        FillList();
        Load();
        _status.Text = $"Copied as {item.Name}, beside it. Copy the marks across too (Edit → Duplicate, or a grid array at the same spacing).";
    }

    void Remove()
    {
        if (_chosen < 0 || _chosen >= _set.Items.Count) return;
        var gone = _set.Items[_chosen];
        _set = _set with { Items = _set.Items.RemoveAt(_chosen) };
        _chosen = Math.Min(_chosen, _set.Items.Count - 1);
        FillList();
        Load();
        _status.Text = $"Removed {gone.Name}. What was drawn on it is marked flat, at the height of everything else.";
    }

    void FillList()
    {
        _loading = true;
        _list.ItemsSource = _set.Items.Select(s => $"{s.Name}: {s.Summary}").ToList();
        _list.SelectedIndex = _chosen;
        _loading = false;
    }

    /// <summary>The chosen surface's settings into the boxes.</summary>
    void Load()
    {
        _loading = true;
        bool any = _chosen >= 0 && _chosen < _set.Items.Count;
        _editor.IsEnabled = any;
        if (any)
        {
            var m = _set.Items[_chosen];
            _name.Text = m.Name;
            _kind.SelectedIndex = m.Round ? 0 : 1;
            _axis.SelectedIndex = Math.Abs(Math.Sin(m.AxisDeg * Math.PI / 180)) > 0.5 ? 1 : 0;
            _cx.Value = (decimal)m.Centre.X;
            _cy.Value = (decimal)m.Centre.Y;
            _length.Value = (decimal)m.LengthMm;
            _dia.Value = (decimal)m.DiameterMm;
            _width.Value = (decimal)m.WidthMm;
            _top.Value = (decimal)m.TopMm;
            _reach.Value = (decimal)m.ReachDeg;
            _wrap.IsChecked = m.Wrap;
            _even.IsChecked = m.EvenOut;
        }
        _loading = false;
        Refresh();
    }

    /// <summary>The boxes into the chosen surface.</summary>
    void Changed()
    {
        if (_loading || _chosen < 0 || _chosen >= _set.Items.Count) { Refresh(); return; }
        var m = _set.Items[_chosen];
        m = m with
        {
            Name = string.IsNullOrWhiteSpace(_name.Text) ? m.Name : _name.Text.Trim(),
            Kind = _kind.SelectedIndex == 1 ? MarkSurfaceKind.Flat : MarkSurfaceKind.Round,
            AxisDeg = _axis.SelectedIndex == 1 ? 90 : 0,
            Centre = new Vec2((double)(_cx.Value ?? 110), (double)(_cy.Value ?? 110)),
            LengthMm = (double)(_length.Value ?? 60),
            DiameterMm = (double)(_dia.Value ?? 10),
            WidthMm = (double)(_width.Value ?? 30),
            TopMm = (double)(_top.Value ?? 10),
            ReachDeg = (double)(_reach.Value ?? (decimal)MarkSurface.DefaultReachDeg),
            Wrap = _wrap.IsChecked == true,
            EvenOut = _even.IsChecked == true,
        };
        _set = _set with { Items = _set.Items.SetItem(_chosen, m) };
        // The list's line for it, without losing the choice.
        if (_list.ItemsSource is List<string> lines && _chosen < lines.Count)
        {
            var now = $"{m.Name}: {m.Summary}";
            if (lines[_chosen] != now) FillList();
        }
        Refresh();
    }

    void Refresh()
    {
        bool any = _chosen >= 0 && _chosen < _set.Items.Count;
        var m = any ? _set.Items[_chosen] : null;
        _roundOnly.IsVisible = m?.Round ?? true;
        _flatOnly.IsVisible = m is { Round: false };
        _info.Text = m is null ? "Add a surface to start."
            : m.Round
                ? $"Its rectangle on the canvas is {m.AcrossMm:0.#} mm across: {m.ReachDeg:0}° each way round from the top, " + (m.Wrap ? "unrolled" : "as seen from above") +
                  $". Seen from above, its marks land in a strip {m.Zone().HalfFootprint * 2:0.#} mm wide along the top line. The head steps from {m.TopMm:0.##} mm down to {m.Zone().HeightAt(m.Zone().World(0, m.Zone().HalfFootprint)):0.##} mm." +
                  (m.TopMm < m.DiameterMm - 0.01 ? " ⚠ Its top is lower than its diameter: is it sunk into the bed or a jig? Check the top height." : "")
                : $"Marks inside its rectangle are focused {m.TopMm:0.##} mm up.";
        _info.Foreground = Brush(m is { Round: true } && m.TopMm < m.DiameterMm - 0.01 ? "Warn" : "TextMuted");
        var wrap = _set.Wrap();
        var design = _doc.State.Shapes.OfType<PathShape>().Where(p => p.Sent).SelectMany(p => p.Flat)
            .Select(f => (IReadOnlyList<Vec2>)(f.Closed ? [.. f.Points, f.Points[0]] : f.Points)).ToList();
        _plan.Show(_set.Items, _chosen, design);
        if (wrap.Overlap() is { } both)
        {
            _status.Text = $"⚠ \"{both.A.Name}\" and \"{both.B.Name}\" overlap on the canvas: a job won't start until they're apart.";
            _status.Foreground = Brush("Warn");
        }
        else if (_status.Foreground == Brush("Warn"))
        {
            _status.Text = "";
            _status.Foreground = Brush("TextMuted");
        }
    }
}
