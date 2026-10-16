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
/// The layers, as cards in run order (top runs first). Each card says what the layer does,
/// how hard, how fast and how long it takes; the active layer's card opens up to show its
/// settings (<see cref="Editor"/>), so a layer and its settings are always in one place.
/// Click a card to make it active (click it again to fold its settings), Ctrl/Shift-click
/// to edit several together, drag to reorder, double-click to rename; the arrow lists the
/// layer's shapes. Cards are rebuilt from the document on every change, except the
/// settings, which stay put so typing in them is never interrupted.
/// </summary>
public sealed class LayersPanel : UserControl
{
    readonly StackPanel _rows = new() { Spacing = 2 };
    readonly Document _doc;
    ImmutableHashSet<Guid> _selected = [];
    Guid _anchor;
    Guid? _renaming;

    // shapes under each layer
    readonly HashSet<Guid> _expanded = [];
    Guid? _renamingShape;
    Guid? _shapeAnchor;
    ImmutableHashSet<Guid> _lastSelection = [];
    const int MaxShapeRows = 300;

    /// <summary>The mouse is over a shape's row (null when it leaves): highlight it on the canvas.</summary>
    public event Action<Guid?>? ShapeHovered;

    /// <summary>Common shape tasks (cut, group, array…) for a set of shapes; supplied by the main window.</summary>
    public Func<IReadOnlySet<Guid>, List<Control>>? ShapeActions { get; set; }

    // drag-to-reorder
    Guid? _dragLayer;
    Point _dragStart;
    bool _dragging;
    int _dropIndex = -1;

    // the active layer's settings, docked under its card
    readonly Border _editorHost;
    Control? _editor;
    bool _editorFolded, _wasActive;
    IReadOnlyDictionary<Guid, double> _times = new Dictionary<Guid, double>();

    /// <summary>The settings shown under the active layer's card.</summary>
    public Control? Editor
    {
        get => _editor;
        set { _editor = value; _editorHost.Child = value; Rebuild(); }
    }

    /// <summary>Each layer's estimated time in seconds (from the job), shown on its card.</summary>
    public void SetTimes(IReadOnlyDictionary<Guid, double> seconds)
    {
        _times = seconds;
        if (!_doc.InEdit) Rebuild();
    }

    /// <summary>Layers chosen for editing (always includes the active layer).</summary>
    public ImmutableHashSet<Guid> SelectedLayers => _selected;
    public event Action? SelectionChanged;

    public LayersPanel(Document doc)
    {
        _doc = doc;
        // Not on every mouse move of a drag on the canvas: once it's done is enough.
        _doc.Changed += _ => { if (!_doc.InEdit) Rebuild(); };
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

        _editorHost = new Border
        {
            Background = Res("Bg2"),
            BorderBrush = Res("Accent"),
            BorderThickness = new(1, 0, 1, 1),
            CornerRadius = new(0, 0, 8, 8),
            Padding = new(10, 8, 10, 10),
            Margin = new(0, 0, 0, 6),
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { add, dup, up, down, merge, del } };
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "LAYERS", Classes = { "h" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0) });
        DockPanel.SetDock(bar, Dock.Right);
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Insert(0, bar);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        ToolTip.SetTip(header, "Top layer runs first. Click a layer for its settings; Ctrl/Shift-click to edit several at once; drag to reorder; double-click to rename. The arrow lists a layer's shapes: click one to select it, right-click for more.");
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
        // Bring its card into view (it may have been chosen on the job timeline or the canvas).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _rows.Children.FirstOrDefault(c => c.Tag is Guid g && g == id)?.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
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

        // Shapes selected on the canvas: open their layers so they can be seen in the list.
        bool newSelection = !_doc.Selection.SetEquals(_lastSelection);
        _lastSelection = _doc.Selection;
        if (newSelection)
            foreach (var s in state.Shapes)
                if (_doc.Selection.Contains(s.Id)) _expanded.Add(s.LayerId);

        // Everything but the settings is rebuilt. The settings stay attached (moving them would drop
        // the keyboard focus mid-typing): cards above the active layer go before them, the rest after.
        for (int i = _rows.Children.Count - 1; i >= 0; i--)
            if (_rows.Children[i] != _editorHost) _rows.Children.RemoveAt(i);
        int activeIndex = state.LayerIndex(_doc.ActiveLayer);
        bool showEditor = _editor is not null && activeIndex >= 0 && !_editorFolded && !_dragging;
        if (!_rows.Children.Contains(_editorHost)) _rows.Children.Add(_editorHost);
        _editorHost.IsVisible = showEditor;
        int insertAt = 0;
        var counts = state.Shapes.GroupBy(s => s.LayerId).ToDictionary(g => g.Key, g => g.Count());
        Control? firstSelected = null;
        for (int i = 0; i < state.Layers.Count; i++)
        {
            var l = state.Layers[i];
            int count = counts.GetValueOrDefault(l.Id);
            bool open = showEditor && i == activeIndex;
            var block = new StackPanel { Spacing = 1, Tag = l.Id, Margin = new(0, 0, 0, open ? 0 : 5) };
            block.Children.Add(Row(l, i, count, open));
            var shapes = block;
            if (open)
            {
                // The active card's shapes come after its settings.
                shapes = new StackPanel { Spacing = 1, Margin = new(0, 0, 0, 5) };
                _rows.Children.Insert(insertAt + 1, shapes);
            }
            if (count > 0 && _expanded.Contains(l.Id)) AddShapeRows(shapes, l, ref firstSelected);
            if (i <= activeIndex) _rows.Children.Insert(insertAt++, block);
            else _rows.Children.Add(block);
        }
        if (newSelection && firstSelected is { } target)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => target.BringIntoView());
        if (changed) SelectionChanged?.Invoke();
    }

    Control Row(Layer l, int index, int count, bool open)
    {
        bool active = l.Id == _doc.ActiveLayer;
        bool selected = _selected.Contains(l.Id);
        var colour = Color.FromRgb((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color);
        bool live = l.Output && l.Visible;

        var bar = new Border
        {
            Width = 5, CornerRadius = new(3), Margin = new(0, 0, 6, 0),
            Background = new SolidColorBrush(colour),
            Cursor = new Cursor(StandardCursorType.Hand),
            Opacity = live ? 1 : 0.4,
        };
        ToolTip.SetTip(bar, "Layer colour (display only): click to change");
        bar.PointerPressed += (_, e) => { e.Handled = true; ShowPalette(bar, l); };

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
            var title = new TextBlock { Text = l.Name, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = live ? 1 : 0.6, VerticalAlignment = VerticalAlignment.Center };
            var n = new TextBlock { Text = count == 0 ? "empty" : count == 1 ? "1 shape" : $"{count} shapes", Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
            var g = new Grid { ColumnDefinitions = new("Auto,Auto"), VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions[0].MaxWidth = 150;
            g.Children.Add(title);
            Grid.SetColumn(n, 1);
            g.Children.Add(n);
            name = g;
        }

        var eye = Toggle(l.Visible ? "IcEye" : "IcEyeOff", l.Visible, l.Visible ? "Hide (hidden layers are never sent)" : "Show");
        eye.Click += (_, _) => ToggleOn(l, x => x with { Visible = !x.Visible }, l.Visible ? "Hide layer" : "Show layer");
        var lck = Toggle(l.Locked ? "IcLock" : "IcUnlock", !l.Locked, l.Locked ? "Unlock" : "Lock (can't select or move its shapes)");
        lck.Click += (_, _) => ToggleOn(l, x => x with { Locked = !x.Locked }, l.Locked ? "Unlock layer" : "Lock layer");
        var outp = Toggle("IcBolt", l.Output, l.Output ? "Output on: this layer is sent to the laser" : "Output off: shown as a guide, never sent");
        outp.Click += (_, _) => ToggleOn(l, x => x with { Output = !x.Output }, l.Output ? "Disable layer output" : "Enable layer output");

        bool shapesOpen = _expanded.Contains(l.Id);
        var chevron = new Button
        {
            Classes = { "icon" }, MinWidth = 18, MinHeight = 20, Padding = new(0), Margin = new(0, 0, 2, 0),
            Content = new TextBlock { Text = count == 0 ? "" : shapesOpen ? "▾" : "▸", FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
            IsEnabled = count > 0, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(chevron, shapesOpen ? "Hide this layer's shapes" : "Show this layer's shapes");
        chevron.Click += (_, _) => { if (!_expanded.Remove(l.Id)) _expanded.Add(l.Id); Rebuild(); };

        var top = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto,Auto") };
        top.Children.Add(chevron);
        Grid.SetColumn(name, 1); top.Children.Add(name);
        Grid.SetColumn(outp, 2); top.Children.Add(outp);
        Grid.SetColumn(eye, 3); top.Children.Add(eye);
        Grid.SetColumn(lck, 4); top.Children.Add(lck);

        // What it does, at a glance: mode, material, power/speed/passes, time.
        var (modeText, modeColour) = ModeLook(l.Mode);
        var pill = new Border
        {
            CornerRadius = new(4), Padding = new(5, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse(modeColour), 0.2),
            Child = new TextBlock { Text = modeText, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse(modeColour)), LetterSpacing = 0.5 },
        };
        string light = l.Laser.Light == Core.LightSource.Red ? " · MOPA" : "";
        string passes = l.Laser.Passes > 1 ? $" ×{l.Laser.Passes}" : "";
        string detail = l.Mode == LayerMode.Image ? $"{l.Laser.Power:0.#}% · {l.Dpi:0} dpi{light}" : $"{l.Laser.Power:0.#}% · {l.Laser.Speed:0.#} mm/s{passes}{light}";
        var info = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { pill, new TextBlock { Text = detail, FontSize = 11.5, Foreground = Res("TextMuted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 } },
        };
        var time = new TextBlock
        {
            FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("TextMuted"),
            Text = live && _times.TryGetValue(l.Id, out var t) ? UiKit.Clock(t) : "",
        };
        ToolTip.SetTip(time, "About how long this layer takes");
        var bottom = new DockPanel { Margin = new(20, 3, 4, 0) };
        DockPanel.SetDock(time, Dock.Right);
        bottom.Children.Add(time);
        bottom.Children.Add(info);
        var lines = new StackPanel { Children = { top } };
        if (l.Material is { } mat)
            lines.Children.Add(new TextBlock
            {
                Text = mat.RecipeTitle is { Length: > 0 } rt ? $"{mat.Label} · {rt}" : mat.Label,
                FontSize = 11.5, Margin = new(20, 1, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.85,
            });
        lines.Children.Add(bottom);

        var grid = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new(5, 5, 3, 6) };
        grid.Children.Add(bar);
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);

        bool drop = _dragging && _dropIndex == index;
        var row = new Border
        {
            Background = active ? Res("Bg3") : selected ? new SolidColorBrush(Color.Parse("#20304A")) : Res("Bg2"),
            CornerRadius = open ? new(8, 8, 0, 0) : new(8),
            BorderThickness = drop ? new(1, 3, 1, 1) : new(1),
            BorderBrush = drop || active || selected ? Res("Accent") : Res("Line"),
            Child = grid,
            Opacity = _dragging && _dragLayer == l.Id ? 0.5 : 1,
            Tag = l.Id,
        };
        // Buttons and the colour bar handle their own presses, so this only fires on the card itself.
        row.PointerPressed += (_, e) => OnRowPressed(l, e);
        row.ContextRequested += (_, e) => { e.Handled = true; RowMenu(l).Open(row); };
        if (active && _selected.Count == 1 && _editor is not null)
            ToolTip.SetTip(row, open ? "Click to fold the settings away" : "Click to show this layer's settings");
        return row;
    }

    // ---- shapes under a layer ------------------------------------------------------------

    void AddShapeRows(StackPanel block, Layer l, ref Control? firstSelected)
    {
        var shapes = _doc.State.ShapesOn(l.Id).ToList();
        var emittedGroups = new HashSet<Guid>();
        int shown = 0;
        foreach (var s in shapes)
        {
            if (shown >= MaxShapeRows) break;
            if (s.GroupId is { } g)
            {
                if (!emittedGroups.Add(g)) continue;
                var members = shapes.Where(x => x.GroupId == g).ToList();
                block.Children.Add(GroupRow(members));
                foreach (var m in members)
                {
                    var row = ShapeRow(m, l, indent: 1);
                    if (firstSelected is null && _doc.Selection.Contains(m.Id)) firstSelected = row;
                    block.Children.Add(row);
                    shown++;
                }
                continue;
            }
            var r = ShapeRow(s, l, indent: 0);
            if (firstSelected is null && _doc.Selection.Contains(s.Id)) firstSelected = r;
            block.Children.Add(r);
            shown++;
        }
        if (shapes.Count > shown)
            block.Children.Add(new TextBlock { Classes = { "hint" }, Margin = new(34, 2, 0, 4), Text = $"… and {shapes.Count - shown} more (use Select all shapes on this layer)" });
    }

    /// <summary>What a shape is called in the list.</summary>
    static string Label(Shape s) => s switch
    {
        TextShape t => $"Text “{Trim(t.Text.Replace('\n', ' '), 28)}”",
        ImageShape => string.IsNullOrWhiteSpace(s.Name) ? "Picture" : Trim(s.Name, 34),
        _ => string.IsNullOrWhiteSpace(s.Name) ? "Shape" : Trim(s.Name, 34),
    };

    static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    Control GroupRow(List<Shape> members)
    {
        bool all = members.All(m => _doc.Selection.Contains(m.Id));
        var row = new Border
        {
            Background = all ? new SolidColorBrush(Color.Parse("#20304A")) : Brushes.Transparent,
            CornerRadius = new(4),
            Margin = new(30, 0, 0, 0),
            Padding = new(6, 2),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new PathIcon { Data = (Geometry)Application.Current!.FindResource("IcCopy")!, Width = 11, Height = 11, Opacity = 0.7 },
                    new TextBlock { Text = $"Group of {members.Count}", FontSize = 12, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        ToolTip.SetTip(row, "Click to select the whole group");
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _doc.Select(members.Select(m => m.Id));
            e.Handled = true;
        };
        return row;
    }

    Control ShapeRow(Shape s, Layer l, int indent)
    {
        bool sel = _doc.Selection.Contains(s.Id);
        var b = s.Bounds;
        Control name;
        if (_renamingShape == s.Id)
        {
            var tb = new TextBox { Text = s.Name, MinHeight = 22, Padding = new(4, 1), FontSize = 12 };
            void Done(bool keep)
            {
                if (_renamingShape != s.Id) return;
                _renamingShape = null;
                var text = tb.Text?.Trim() ?? "";
                if (keep && text != s.Name && _doc.State.FindShape(s.Id) is { } cur)
                    _doc.Commit("Rename shape", Ops.Replace(_doc.State, cur.With(name: text)));
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
            name = new TextBlock { Text = Label(s), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        var size = new TextBlock
        {
            Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0),
            Text = b.IsEmpty ? "" : $"{b.Width:0.#} × {b.Height:0.#}",
        };
        var icon = new ShapeThumb(s, Color.FromRgb((byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color), l.Mode == LayerMode.Fill)
        {
            Margin = new(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        grid.Children.Add(icon);
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        Grid.SetColumn(size, 2); grid.Children.Add(size);
        var row = new Border
        {
            Background = sel ? new SolidColorBrush(Color.Parse("#20304A")) : Brushes.Transparent,
            BorderBrush = sel ? Res("Accent") : Brushes.Transparent,
            BorderThickness = new(2, 0, 0, 0),
            CornerRadius = new(4),
            Margin = new(30 + indent * 14, 0, 2, 0),
            Padding = new(4, 2),
            Child = grid,
            Opacity = l.Visible ? 1 : 0.55,
        };
        ToolTip.SetTip(row, b.IsEmpty ? Label(s) : $"{Label(s)}\n{b.Width:0.##} × {b.Height:0.##} mm at X {b.MinX:0.##}, Y {b.MinY:0.##}");
        row.PointerEntered += (_, _) => ShapeHovered?.Invoke(s.Id);
        row.PointerExited += (_, _) => ShapeHovered?.Invoke(null);
        row.PointerPressed += (_, e) => OnShapePressed(s, l, e);
        // Built when opened, so it reflects the selection at that moment.
        row.ContextRequested += (_, e) => { e.Handled = true; ShapeMenu(s).Open(row); };
        return row;
    }

    void OnShapePressed(Shape s, Layer l, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (!_doc.Selection.Contains(s.Id)) _doc.Select([s.Id]);
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        var mods = e.KeyModifiers;
        if (e.ClickCount >= 2 && mods == KeyModifiers.None)
        {
            _renamingShape = s.Id;
            Rebuild();
        }
        else if (mods.HasFlag(KeyModifiers.Control))
        {
            _doc.Select(_doc.Selection.Contains(s.Id) ? _doc.Selection.Remove(s.Id) : _doc.Selection.Add(s.Id));
            _shapeAnchor = s.Id;
        }
        else if (mods.HasFlag(KeyModifiers.Shift) && _shapeAnchor is { } anchor)
        {
            var list = _doc.State.ShapesOn(l.Id).Select(x => x.Id).ToList();
            int a = list.IndexOf(anchor), b = list.IndexOf(s.Id);
            if (a < 0) a = b;
            _doc.Select(list.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1));
        }
        else
        {
            _doc.Select([s.Id]);
            _shapeAnchor = s.Id;
        }
        e.Handled = true;
    }

    ContextMenu ShapeMenu(Shape s)
    {
        MenuItem Item(string header, Action a, bool enabled = true)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            m.Click += (_, _) => a();
            return m;
        }
        // Right-clicking a selected shape acts on the whole selection; otherwise on that shape.
        IReadOnlySet<Guid> targets = _doc.Selection.Contains(s.Id) ? _doc.Selection : new HashSet<Guid> { s.Id };
        if (!_doc.Selection.Contains(s.Id)) _doc.Select([s.Id]);
        var menu = new ContextMenu();
        menu.Items.Add(Item("Rename", () => { _renamingShape = s.Id; Rebuild(); }, targets.Count == 1));
        menu.Items.Add(Item("Select its whole group", () => _doc.Select(_doc.State.Shapes.Where(x => x.GroupId == s.GroupId).Select(x => x.Id)), s.GroupId is not null));
        menu.Items.Add(new Separator());
        foreach (var c in ShapeActions?.Invoke(targets) ?? []) menu.Items.Add(c);
        return menu;
    }

    /// <summary>The label and its colour for a layer's mode (muted tones, for chips).</summary>
    public static (string Text, string Colour) ModeLook(LayerMode m) => m switch
    {
        LayerMode.Cut => ("CUT", "#D97A6C"),
        LayerMode.Score => ("SCORE", "#CFA55A"),
        LayerMode.Fill => ("ENGRAVE", "#7C9FD4"),
        _ => ("PICTURE", "#A592CF"),
    };

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
            // Clicking the active card again folds or unfolds its settings (on release, if it wasn't a drag).
            _wasActive = l.Id == _doc.ActiveLayer && _selected.Count == 1;
            if (!_wasActive) _editorFolded = false;
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
        // Only the layer cards count (not the settings or the active layer's shape list).
        int idx = _doc.State.Layers.Count, li = 0;
        foreach (var child in _rows.Children)
        {
            if (child.Tag is not Guid) continue;
            var b = child.Bounds;
            if (p.Y < b.Y + b.Height / 2) { idx = li; break; }
            li++;
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
        if (id is { } clicked && !dragging && _wasActive && clicked == _doc.ActiveLayer)
        {
            _editorFolded = !_editorFolded;
            Rebuild();
        }
        _wasActive = false;
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
        // The same shape tasks, applied to everything on this layer.
        var onLayer = _doc.State.ShapesOn(l.Id).Select(s => s.Id).ToHashSet();
        var shapesMenu = new MenuItem { Header = $"Its shapes ({onLayer.Count})", IsEnabled = onLayer.Count > 0 };
        foreach (var c in ShapeActions?.Invoke(onLayer) ?? []) shapesMenu.Items.Add(c);
        return new ContextMenu
        {
            Items =
            {
                shapesMenu,
                new Separator(),
                Item("Select all shapes on this layer", () => _doc.Select(_doc.State.ShapesOn(l.Id).Select(s => s.Id))),
                Item("Show all layers' shapes", () => { foreach (var x in _doc.State.Layers) _expanded.Add(x.Id); Rebuild(); }),
                Item("Hide all layers' shapes", () => { _expanded.Clear(); Rebuild(); }),
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
