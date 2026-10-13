using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// The layer list. Click to make a layer active, Ctrl/Shift-click to select
/// several for bulk editing, drag to reorder (top runs first), double-click
/// to rename. Rows are rebuilt from the document on every change, so the
/// list can never drift out of sync with it.
/// </summary>
public sealed class LayersPanel : UserControl
{
    readonly StackPanel _rows = new() { Spacing = 2 };
    readonly Document _doc;
    ImmutableHashSet<Guid> _selected = [];
    Guid _anchor;
    Guid? _renaming;

    // drag-to-reorder
    Guid? _dragLayer;
    Point _dragStart;
    bool _dragging;
    int _dropIndex = -1;

    /// <summary>Layers chosen for editing (always includes the active layer).</summary>
    public ImmutableHashSet<Guid> SelectedLayers => _selected;
    public event Action? SelectionChanged;

    public LayersPanel(Document doc)
    {
        _doc = doc;
        _doc.Changed += _ => Rebuild();
        _rows.PointerMoved += (_, e) => OnRowMoved(e);
        _rows.PointerReleased += (_, e) => OnRowReleased(e);
        _rows.PointerCaptureLost += (_, _) => { if (_dragging) { _dragLayer = null; _dragging = false; _dropIndex = -1; Rebuild(); } };
        var add = IconButton("IcPlus", "Add layer");
        add.Click += (_, _) => AddLayer();
        var dup = IconButton("IcCopy", "Duplicate layer with its shapes");
        dup.Click += (_, _) => DuplicateLayer();
        var up = IconButton("IcUp", "Move up (runs earlier)");
        up.Click += (_, _) => MoveSelected(-1);
        var down = IconButton("IcDown", "Move down (runs later)");
        down.Click += (_, _) => MoveSelected(1);
        var merge = IconButton("IcMerge", "Merge selected layers into the top one");
        merge.Click += (_, _) => Merge();
        var del = IconButton("IcTrash", "Delete selected layers (shapes move to the layer above)");
        del.Click += (_, _) => DeleteSelected();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { add, dup, up, down, merge, del } };
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "LAYERS", Classes = { "h" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0) });
        DockPanel.SetDock(bar, Dock.Right);
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Insert(0, bar);

        var hint = new TextBlock
        {
            Classes = { "hint" },
            Text = "Top layer runs first. Ctrl/Shift-click to edit several at once. Ctrl+1…9 moves selected shapes to a layer.",
            Margin = new(0, 8, 0, 0),
        };
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(hint);
        root.Children.Add(scroll);
        Content = root;
        _selected = [_doc.ActiveLayer];
        _anchor = _doc.ActiveLayer;
        Rebuild();
    }

    static Button IconButton(string icon, string tip)
    {
        var b = new Button { Classes = { "icon" }, Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource(icon)! } };
        ToolTip.SetTip(b, tip);
        return b;
    }

    void SetSelection(IEnumerable<Guid> ids, Guid active)
    {
        _selected = ids.ToImmutableHashSet();
        _doc.SetActiveLayer(active);
        Rebuild();
        SelectionChanged?.Invoke();
    }

    /// <summary>Make one layer active and the only selected one.</summary>
    public void Activate(Guid id)
    {
        _anchor = id;
        SetSelection([id], id);
    }

    void Rebuild()
    {
        var state = _doc.State;
        // Drop selections of deleted layers; never leave nothing selected.
        var valid = state.Layers.Select(l => l.Id).ToHashSet();
        var sel = _selected.Where(valid.Contains).ToImmutableHashSet();
        if (!sel.Contains(_doc.ActiveLayer) && valid.Contains(_doc.ActiveLayer)) sel = sel.Add(_doc.ActiveLayer);
        bool changed = !sel.SetEquals(_selected);
        _selected = sel;

        _rows.Children.Clear();
        var counts = state.Shapes.GroupBy(s => s.LayerId).ToDictionary(g => g.Key, g => g.Count());
        for (int i = 0; i < state.Layers.Count; i++)
            _rows.Children.Add(Row(state.Layers[i], i, counts.GetValueOrDefault(state.Layers[i].Id)));
        if (changed) SelectionChanged?.Invoke();
    }

    Control Row(Layer l, int index, int count)
    {
        bool active = l.Id == _doc.ActiveLayer;
        bool selected = _selected.Contains(l.Id);
        var bg = active ? Res("Bg3") : selected ? new SolidColorBrush(Color.Parse("#20304A")) : Brushes.Transparent;

        var swatch = new Border
        {
            Width = 14, Height = 14, CornerRadius = new(3),
            Background = new SolidColorBrush(Color.FromRgb((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(swatch, "Layer colour (display only)");
        swatch.PointerPressed += (_, e) => { e.Handled = true; ShowPalette(swatch, l); };

        Control name;
        if (_renaming == l.Id)
        {
            var tb = new TextBox { Text = l.Name, MinHeight = 24, Padding = new(4, 2), VerticalAlignment = VerticalAlignment.Center };
            void Done(bool keep)
            {
                if (_renaming != l.Id) return;
                _renaming = null;
                var text = tb.Text?.Trim();
                if (keep && !string.IsNullOrEmpty(text) && text != l.Name)
                    _doc.Commit("Rename layer", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { l.Id }, x => x with { Name = text }));
                else Rebuild();
            }
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Done(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Done(false); e.Handled = true; }
            };
            tb.LostFocus += (_, _) => Done(true);
            tb.AttachedToVisualTree += (_, _) => { tb.Focus(); tb.SelectAll(); };
            name = tb;
        }
        else
        {
            name = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = l.Name, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                        Opacity = l.Output && l.Visible ? 1 : 0.55 },
                    new TextBlock { Classes = { "hint" }, Text = Summary(l, count) },
                },
            };
        }

        var eye = Toggle(l.Visible ? "IcEye" : "IcEyeOff", l.Visible, l.Visible ? "Hide (hidden layers are never sent)" : "Show");
        eye.Click += (_, _) => ToggleOn(l, x => x with { Visible = !x.Visible }, l.Visible ? "Hide layer" : "Show layer");
        var lck = Toggle(l.Locked ? "IcLock" : "IcUnlock", !l.Locked, l.Locked ? "Unlock" : "Lock (can't select or move its shapes)");
        lck.Click += (_, _) => ToggleOn(l, x => x with { Locked = !x.Locked }, l.Locked ? "Unlock layer" : "Lock layer");
        var outp = Toggle("IcBolt", l.Output, l.Output ? "Output on: this layer is sent to the laser" : "Output off: shown as a guide, never sent");
        outp.Click += (_, _) => ToggleOn(l, x => x with { Output = !x.Output }, l.Output ? "Disable layer output" : "Enable layer output");

        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto,Auto"), Margin = new(8, 4, 4, 4) };
        grid.Children.Add(swatch);
        Grid.SetColumn(name, 1);
        name.Margin = new(10, 0, 4, 0);
        grid.Children.Add(name);
        Grid.SetColumn(outp, 2); grid.Children.Add(outp);
        Grid.SetColumn(eye, 3); grid.Children.Add(eye);
        Grid.SetColumn(lck, 4); grid.Children.Add(lck);

        var row = new Border
        {
            Background = bg,
            CornerRadius = new(6),
            Child = grid,
            BorderThickness = new(0, 2, 0, 0),
            BorderBrush = _dragging && _dropIndex == index ? Res("Accent") : Brushes.Transparent,
            Opacity = _dragging && _dragLayer == l.Id ? 0.5 : 1,
            Tag = l.Id,
        };
        // Buttons and the swatch handle their own presses, so this only fires on the row itself.
        row.PointerPressed += (_, e) => OnRowPressed(l, e);
        row.ContextMenu = RowMenu(l);
        return row;
    }

    static string Summary(Layer l, int count)
    {
        string mode = l.Mode switch { LayerMode.Cut => "Cut", LayerMode.Score => "Score", LayerMode.Fill => "Engrave", _ => $"Picture {l.Dpi:0} dpi" };
        string light = l.Laser.Light == Core.LightSource.Red ? "fiber" : "diode";
        string passes = l.Laser.Passes > 1 ? $" ×{l.Laser.Passes}" : "";
        string shapes = count == 1 ? "1 shape" : $"{count} shapes";
        return $"{mode} · {l.Laser.Power:0.#}% · {l.Laser.Speed:0.#} mm/s{passes} · {light} · {shapes}";
    }

    static IBrush Res(string key) => (IBrush)Application.Current!.FindResource(key)!;

    static Button Toggle(string icon, bool on, string tip)
    {
        var b = new Button
        {
            Classes = { "icon" },
            MinWidth = 26, MinHeight = 26, Padding = new(4),
            Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource(icon)!, Width = 14, Height = 14 },
            Opacity = on ? 0.95 : 0.4,
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>Toggle on the clicked layer, or on every selected layer when it is part of the selection.</summary>
    void ToggleOn(Layer l, Func<Layer, Layer> edit, string label)
    {
        var ids = _selected.Contains(l.Id) ? _selected : [l.Id];
        // Bulk toggles set every layer to the clicked layer's new state rather than flipping each.
        var target = edit(l);
        _doc.Commit(label, Ops.UpdateLayers(_doc.State, ids, x => x with { Visible = target.Visible == l.Visible ? x.Visible : target.Visible, Locked = target.Locked == l.Locked ? x.Locked : target.Locked, Output = target.Output == l.Output ? x.Output : target.Output }));
    }

    void OnRowPressed(Layer l, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (!_selected.Contains(l.Id)) Activate(l.Id);
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        var mods = e.KeyModifiers;
        if (mods.HasFlag(KeyModifiers.Control))
        {
            var next = _selected.Contains(l.Id) && _selected.Count > 1 ? _selected.Remove(l.Id) : _selected.Add(l.Id);
            _anchor = l.Id;
            SetSelection(next, next.Contains(l.Id) ? l.Id : next.First());
        }
        else if (mods.HasFlag(KeyModifiers.Shift))
        {
            var layers = _doc.State.Layers;
            int a = layers.FindIndex(x => x.Id == _anchor), b = layers.FindIndex(x => x.Id == l.Id);
            if (a < 0) a = b;
            var range = layers.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).Select(x => x.Id);
            SetSelection(range, l.Id);
        }
        else if (e.ClickCount >= 2)
        {
            _renaming = l.Id;
            Rebuild();
        }
        else
        {
            if (!_selected.Contains(l.Id) || _selected.Count == 1) Activate(l.Id);
            else _doc.SetActiveLayer(l.Id);
            // Capture on the container: rows are rebuilt on every change and would lose it.
            _dragLayer = l.Id;
            _dragStart = e.GetPosition(_rows);
            _dragging = false;
            e.Pointer.Capture(_rows);
        }
        e.Handled = true;
    }

    void OnRowMoved(PointerEventArgs e)
    {
        if (_dragLayer is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(_rows);
        if (!_dragging && Math.Abs(p.Y - _dragStart.Y) < 6) return;
        _dragging = true;
        int idx = _rows.Children.Count;
        for (int i = 0; i < _rows.Children.Count; i++)
        {
            var b = _rows.Children[i].Bounds;
            if (p.Y < b.Y + b.Height / 2) { idx = i; break; }
        }
        if (idx != _dropIndex)
        {
            _dropIndex = idx;
            Rebuild();
        }
    }

    void OnRowReleased(PointerReleasedEventArgs e)
    {
        var id = _dragLayer;
        bool dragging = _dragging;
        int drop = _dropIndex;
        _dragLayer = null;
        _dragging = false;
        _dropIndex = -1;
        e.Pointer.Capture(null);
        if (id is not { } lid || !dragging) return;
        int from = _doc.State.LayerIndex(lid);
        int to = drop > from ? drop - 1 : drop;
        if (to != from && from >= 0) _doc.Commit("Reorder layers", Ops.MoveLayer(_doc.State, lid, to));
        else Rebuild();
    }

    ContextMenu RowMenu(Layer l)
    {
        MenuItem Item(string header, Action a, bool enabled = true)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            m.Click += (_, _) => a();
            return m;
        }
        var sel = _doc.Selection;
        return new ContextMenu
        {
            Items =
            {
                Item("Select all shapes on this layer", () => _doc.Select(_doc.State.ShapesOn(l.Id).Select(s => s.Id))),
                Item($"Move selected shapes here ({sel.Count})", () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, sel, l.Id)), sel.Count > 0),
                new Separator(),
                Item("Rename", () => { _renaming = l.Id; Rebuild(); }),
                Item("Duplicate with shapes", DuplicateLayer),
                Item("Show only this layer", () => _doc.Commit("Solo layer", Ops.UpdateLayers(_doc.State, _doc.State.Layers.Select(x => x.Id).ToHashSet(), x => x with { Visible = x.Id == l.Id }))),
                Item("Show all layers", () => _doc.Commit("Show all layers", Ops.UpdateLayers(_doc.State, _doc.State.Layers.Select(x => x.Id).ToHashSet(), x => x with { Visible = true }))),
                new Separator(),
                Item("Delete layer and its shapes", () => _doc.Commit("Delete layer", Ops.DeleteLayers(_doc.State, new HashSet<Guid> { l.Id }, null))),
            },
        };
    }

    void ShowPalette(Control anchor, Layer l)
    {
        var wrap = new WrapPanel { Width = 6 * 30 };
        foreach (var c in Layer.Palette)
        {
            var b = new Button
            {
                Width = 24, Height = 24, Margin = new(3), Padding = new(0), CornerRadius = new(4),
                Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)),
                BorderThickness = new(c == l.Color ? 2 : 0), BorderBrush = Brushes.White,
            };
            b.Click += (_, _) =>
            {
                _doc.Commit("Layer colour", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { l.Id }, x => x with { Color = c }));
                FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            };
            wrap.Children.Add(b);
        }
        var fly = new Flyout { Content = wrap, Placement = PlacementMode.Right };
        FlyoutBase.SetAttachedFlyout(anchor, fly);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    public void AddLayer()
    {
        var state = _doc.State;
        int i = state.Layers.Count;
        var used = state.Layers.Select(l => l.Color).ToHashSet();
        var layer = Layer.Default(i) with { Color = Layer.Palette.FirstOrDefault(c => !used.Contains(c), Layer.Palette[i % Layer.Palette.Count]) };
        int at = state.LayerIndex(_doc.ActiveLayer) + 1;
        _doc.Commit("Add layer", Ops.AddLayer(state, layer, at));
        Activate(layer.Id);
    }

    void DuplicateLayer()
    {
        var (s, id) = Ops.DuplicateLayer(_doc.State, _doc.ActiveLayer);
        if (id == Guid.Empty) return;
        _doc.Commit("Duplicate layer", s);
        Activate(id);
    }

    void MoveSelected(int dir)
    {
        var layers = _doc.State.Layers;
        var idx = layers.Select((l, i) => (l, i)).Where(t => _selected.Contains(t.l.Id)).Select(t => t.i).ToList();
        if (idx.Count == 0) return;
        if (dir < 0 && idx.Min() == 0 || dir > 0 && idx.Max() == layers.Count - 1) return;
        var s = _doc.State;
        foreach (var i in dir < 0 ? idx : Enumerable.Reverse(idx))
            s = Ops.MoveLayer(s, layers[i].Id, i + dir);
        _doc.Commit(dir < 0 ? "Move layer up" : "Move layer down", s);
    }

    void Merge()
    {
        if (_selected.Count < 2) return;
        var order = _doc.State.Layers.Where(l => _selected.Contains(l.Id)).Select(l => l.Id).ToList();
        _doc.Commit("Merge layers", Ops.MergeLayers(_doc.State, order));
        Activate(order[0]);
    }

    void DeleteSelected()
    {
        var layers = _doc.State.Layers;
        if (_selected.Count >= layers.Count)
        {
            // Deleting every layer: keep the shapes on a fresh default layer instead.
            _doc.Commit("Delete layers", Ops.DeleteLayers(_doc.State, _selected, null));
            return;
        }
        int first = layers.FindIndex(l => _selected.Contains(l.Id));
        var survivor = layers.Take(first).LastOrDefault(l => !_selected.Contains(l.Id))
            ?? layers.First(l => !_selected.Contains(l.Id));
        _doc.Commit("Delete layers", Ops.DeleteLayers(_doc.State, _selected, survivor.Id));
        Activate(survivor.Id);
    }
}
