using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Import;

namespace PhotonCanvas.App;

public partial class MainWindow : Window
{
    readonly Document _doc = new();
    readonly AppSettings _settings = AppSettings.Load();
    readonly LayersPanel _layers;
    readonly PropertiesPanel _props;
    readonly JobPanel _job;
    readonly MachinePanel _machine;
    List<Shape> _clipboard = [];
    int _pasteCount;
    bool _closeConfirmed;

    /// <summary>A project to open once the window is shown (from the command line).</summary>
    public string? OpenPathOnStartup { get; set; }

    static readonly FilePickerFileType ProjectType = new("PhotonCanvas project") { Patterns = ["*.pcanvas", "*.ytool"] };
    static readonly FilePickerFileType VectorTypes = new("Drawings (SVG, DXF, PDF, AI, xTool)") { Patterns = ["*.svg", "*.dxf", "*.pdf", "*.ai", "*.xs", "*.xcs"] };
    static readonly FilePickerFileType AnyImportable = new("Everything that can be imported")
    {
        Patterns = ["*.svg", "*.dxf", "*.pdf", "*.ai", "*.xs", "*.xcs", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tif", "*.tiff"],
    };

    public MainWindow()
    {
        InitializeComponent();
        Canvas.Doc = _doc;
        _layers = new LayersPanel(_doc);
        _props = new PropertiesPanel(_doc, _layers, _settings);
        _props.TraceRequested += im => _ = Guard(() => Trace(im));
        _machine = new MachinePanel(_settings);
        _job = new JobPanel(_doc, _settings, Canvas, _machine);
        LayersHost.Content = _layers;
        PropertiesHost.Content = _props;
        JobHost.Content = _job;
        MachineHost.Content = _machine;

        BuildMenu();
        WireTools();
        ApplyGrid();
        SnapObjectsButton.Click += (_, _) => { _settings.SnapToObjects = SnapObjectsButton.IsChecked == true; SaveAndApplyGrid(); };
        SnapGridButton.Click += (_, _) => { _settings.SnapToGrid = SnapGridButton.IsChecked == true; SaveAndApplyGrid(); };
        SnapGridButton.ContextRequested += (_, e) => { e.Handled = true; _ = Guard(ShowGridSettings); };
        UndoButton.Click += (_, _) => _doc.Undo();
        RedoButton.Click += (_, _) => _doc.Redo();
        FitButton.Click += (_, _) => Canvas.FitField();
        Canvas.CursorMoved += p => CursorText.Text = p is { } v ? $"X {v.X:0.00}   Y {v.Y:0.00} mm" : "";
        Canvas.ZoomChanged += z => ZoomText.Text = $"{z / 3.7795 * 100:0}%";
        _doc.Changed += _ => UpdateChrome();
        _doc.Changed += _ => FollowSelectionLayer();
        _machine.StatusChanged += s => MachineStatus.Text = s;
        Canvas.CameraOpacity = _machine.OverlayOpacity;
        _machine.OverlayOpacityChanged += o => Canvas.CameraOpacity = o;
        _machine.OverlayChanged += b =>
        {
            var old = Canvas.CameraImage;
            Canvas.CameraImage = b;
            if (!ReferenceEquals(old, b)) old?.Dispose();
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        Opened += async (_, _) =>
        {
            Canvas.Focus();
            if (_settings.AcceptedTermsVersion < WelcomeWindow.TermsVersion)
            {
                var welcome = new WelcomeWindow(firstRun: true, _machine.SetUpKeys, () => _machine.HasKeys);
                await welcome.ShowDialog(this);
                if (!welcome.Accepted)
                {
                    _closeConfirmed = true;
                    Close();
                    return;
                }
                _settings.AcceptedTermsVersion = WelcomeWindow.TermsVersion;
                _settings.Save();
            }
            _ = _machine.AutoConnectAsync();
            if (OpenPathOnStartup is { } p) await OpenPath(p);
        };
        Closing += OnClosing;
        UpdateChrome();
    }

    // ---- chrome --------------------------------------------------------------

    void UpdateChrome()
    {
        var name = _doc.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : "Untitled";
        Title = $"{name}{(_doc.Dirty ? " •" : "")} — {AppIdentity.ProductName}";
        EmptyHint.IsVisible = _doc.State.Shapes.Count == 0;
        UndoButton.IsEnabled = _doc.CanUndo;
        RedoButton.IsEnabled = _doc.CanRedo;
        ToolTip.SetTip(UndoButton, _doc.UndoLabel is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)");
        ToolTip.SetTip(RedoButton, _doc.RedoLabel is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)");
        int n = _doc.State.Shapes.Count, sel = _doc.Selection.Count;
        StatusText.Text = sel > 0 ? $"{sel} of {n} shapes selected" : $"{n} shapes · {_doc.State.Layers.Count} layers";
        RefreshMenuState();
    }

    System.Collections.Immutable.ImmutableHashSet<Guid> _lastSelection = [];

    /// <summary>Selecting shapes on one layer brings that layer's settings up.</summary>
    void FollowSelectionLayer()
    {
        if (_doc.InEdit || _doc.Selection.SetEquals(_lastSelection)) return;
        _lastSelection = _doc.Selection;
        var layers = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).Select(s => s.LayerId).Distinct().ToList();
        if (layers.Count == 1 && layers[0] != _doc.ActiveLayer) _layers.Activate(layers[0]);
    }

    void WireTools()
    {
        var map = new (ToggleButton Button, Tool Tool)[]
        {
            (ToolSelect, Tool.Select), (ToolRect, Tool.Rect), (ToolEllipse, Tool.Ellipse), (ToolPolygon, Tool.Polygon), (ToolStar, Tool.Star),
            (ToolLine, Tool.Line), (ToolPen, Tool.Pen), (ToolText, Tool.Text), (ToolNodes, Tool.Nodes), (ToolPan, Tool.Pan),
        };
        Canvas.PolygonSides = _settings.PolygonSides;
        Canvas.StarPoints = _settings.StarPoints;
        Canvas.StarInner = _settings.StarInnerPercent / 100;
        ToolCorners.ValueChanged += (_, _) =>
        {
            if (ToolCorners.Value is not { } v) return;
            if (Canvas.Tool == Tool.Star) Canvas.StarPoints = _settings.StarPoints = (int)v;
            else Canvas.PolygonSides = _settings.PolygonSides = (int)v;
            _settings.Save();
        };
        ToolInner.ValueChanged += (_, _) =>
        {
            if (ToolInner.Value is not { } v) return;
            _settings.StarInnerPercent = (double)v;
            Canvas.StarInner = (double)v / 100;
            _settings.Save();
        };
        void Options(Tool t)
        {
            bool poly = t is Tool.Polygon or Tool.Star;
            ToolCorners.IsVisible = poly;
            ToolInner.IsVisible = ToolInnerLabel.IsVisible = t == Tool.Star;
            ToolOptionsLabel.Text = t switch { Tool.Polygon => "Sides", Tool.Star => "Points", _ => "" };
            ToolOptionsLabel.IsVisible = poly;
            if (t == Tool.Polygon) ToolCorners.Value = Canvas.PolygonSides;
            if (t == Tool.Star) { ToolCorners.Value = Canvas.StarPoints; ToolInner.Value = (decimal)(Canvas.StarInner * 100); }
            ToolHint.Text = t switch
            {
                Tool.Pen => "Click for corners, drag for curves. Click the first point to close; Enter or double-click to finish an open line; Backspace removes the last point.",
                Tool.Text => "Click where the text should start, then type in the Text panel.",
                Tool.Nodes => "Drag points and handles. Double-click an edge to add a point; Delete removes the selected one; S makes it smooth or sharp. Alt-drag a handle to break a smooth point. Esc to finish.",
                _ => "",
            };
            ToolOptions.IsVisible = poly || ToolHint.Text.Length > 0;
            Canvas.Focus();
        }
        Canvas.ToolChanged += Options;
        Canvas.TextRequested += at => CreateText(at);
        Canvas.TextEditRequested += id => { _doc.Select([id]); RightTabs.SelectedIndex = 0; _props.FocusText(); };
        foreach (var (b, t) in map) b.Click += (_, _) => { Canvas.Tool = t; Canvas.Focus(); };
        void Sync(Tool t) { foreach (var (b, tool) in map) b.IsChecked = tool == t; }
        Canvas.ToolChanged += Sync;
        Sync(Canvas.Tool);
    }

    // ---- menu ----------------------------------------------------------------

    MenuItem? _undoItem, _redoItem, _recentMenu;
    MenuItem _showGridItem = null!, _snapObjectsItem = null!, _snapGridItem = null!;

    /// <summary>Push the grid and snap settings to the canvas and every toggle that shows them.</summary>
    void ApplyGrid()
    {
        Canvas.Grid = GridWindow.From(_settings);
        Canvas.ShowGrid = _settings.ShowGrid;
        Canvas.SnapToObjects = _settings.SnapToObjects;
        Canvas.SnapToGrid = _settings.SnapToGrid;
        Canvas.SnapDistancePx = _settings.SnapDistancePx;
        _showGridItem.IsChecked = _settings.ShowGrid;
        _snapObjectsItem.IsChecked = _settings.SnapToObjects;
        _snapGridItem.IsChecked = _settings.SnapToGrid;
        SnapObjectsButton.IsChecked = _settings.SnapToObjects;
        SnapGridButton.IsChecked = _settings.SnapToGrid;
    }

    void SaveAndApplyGrid()
    {
        _settings.Save();
        ApplyGrid();
    }

    Task ShowGridSettings() => new GridWindow(_settings, ApplyGrid).ShowDialog(this);
    readonly List<(MenuItem Item, Func<bool> Enabled)> _conditional = [];

    MenuItem Item(string header, Action action, string? gesture = null, Func<bool>? enabled = null)
    {
        var m = new MenuItem { Header = header };
        if (gesture is not null) m.InputGesture = KeyGesture.Parse(gesture);
        m.Click += (_, _) => action();
        if (enabled is not null) _conditional.Add((m, enabled));
        return m;
    }

    MenuItem AsyncItem(string header, Func<Task> action, string? gesture = null, Func<bool>? enabled = null) =>
        Item(header, () => _ = Guard(action), gesture, enabled);

    void BuildMenu()
    {
        bool HasSel() => _doc.Selection.Count > 0;
        _recentMenu = new MenuItem { Header = "Open recent" };
        var file = new MenuItem
        {
            Header = "_File",
            Items =
            {
                AsyncItem("New", NewDoc, "Ctrl+N"),
                AsyncItem("Open…", OpenDoc, "Ctrl+O"),
                _recentMenu,
                new Separator(),
                AsyncItem("Save", () => Save(false), "Ctrl+S"),
                AsyncItem("Save as…", () => Save(true), "Ctrl+Shift+S"),
                new Separator(),
                AsyncItem("Import…", () => ImportAny(byColour: false), "Ctrl+I"),
                AsyncItem("Import, one layer per colour…", () => ImportAny(byColour: true), "Ctrl+Shift+I"),
                AsyncItem("Import picture…", ImportImage, "Ctrl+J"),
                new Separator(),
                AsyncItem("Export job package (.xf)…", () => _job.Export(xf: true)),
                AsyncItem("Export G-code (.gcode)…", () => _job.Export(xf: false)),
                new Separator(),
                Item("Exit", Close),
            },
        };
        _undoItem = Item("Undo", _doc.Undo, "Ctrl+Z", () => _doc.CanUndo);
        _redoItem = Item("Redo", _doc.Redo, "Ctrl+Y", () => _doc.CanRedo);
        var edit = new MenuItem
        {
            Header = "_Edit",
            Items =
            {
                _undoItem, _redoItem,
                new Separator(),
                Item("Cut", CutSel, "Ctrl+X", HasSel),
                Item("Copy", CopySel, "Ctrl+C", HasSel),
                Item("Paste", Paste, "Ctrl+V", () => _clipboard.Count > 0),
                Item("Duplicate", DuplicateSel, "Ctrl+D", HasSel),
                Item("Delete", DeleteSel, "Delete", HasSel),
                new Separator(),
                Item("Select all", SelectAll, "Ctrl+A"),
                Item("Select all on active layer", () => _doc.Select(_doc.State.ShapesOn(_doc.ActiveLayer).Select(s => s.Id)), "Ctrl+Shift+A"),
                Item("Invert selection", () => _doc.Select(_doc.State.Editable().Select(s => s.Id).Where(id => !_doc.Selection.Contains(id)))),
            },
        };
        var arrange = new MenuItem
        {
            Header = "_Arrange",
            Items =
            {
                Item("Group", GroupSel, "Ctrl+G", () => _doc.Selection.Count > 1),
                Item("Ungroup", UngroupSel, "Ctrl+Shift+G", HasSel),
                Item("Combine into one shape", Combine, "Ctrl+K", () => _doc.Selection.Count > 1),
                Item("Break apart", BreakApart, "Ctrl+Shift+K", HasSel),
                Item("Convert to paths", ConvertToPaths, "Ctrl+Shift+C", () => _doc.State.Shapes.Any(s => _doc.Selection.Contains(s.Id) && s is TextShape)),
                new Separator(),
                AsyncItem("Offset…", OffsetSel, "Ctrl+Shift+O", () => SelectedPaths().Count > 0),
                Item("Weld (union)", () => BooleanSel(BoolOp.Union), "Ctrl+Shift+U", () => SelectedPaths().Count > 1),
                Item("Subtract (bottom minus the rest)", () => BooleanSel(BoolOp.Subtract), "Ctrl+Shift+M", () => SelectedPaths().Count > 1),
                Item("Intersect (keep the overlap)", () => BooleanSel(BoolOp.Intersect), null, () => SelectedPaths().Count > 1),
                Item("Exclude (remove the overlap)", () => BooleanSel(BoolOp.Exclude), null, () => SelectedPaths().Count > 1),
                new Separator(),
                AsyncItem("Grid array…", () => ArraySel(circular: false), null, HasSel),
                AsyncItem("Circular array…", () => ArraySel(circular: true), null, HasSel),
                Item("Distribute horizontally (equal gaps)", () => DistributeSel(true), null, () => _doc.Selection.Count > 2),
                Item("Distribute vertically (equal gaps)", () => DistributeSel(false), null, () => _doc.Selection.Count > 2),
                AsyncItem("Trace picture…", () => SelectedPicture() is { } im ? Trace(im) : Task.CompletedTask, "Ctrl+T", () => SelectedPicture() is not null),
                new Separator(),
                Item("Align left", () => AlignSel(Align.Left), null, () => _doc.Selection.Count > 1),
                Item("Align centres horizontally", () => AlignSel(Align.CenterX), null, () => _doc.Selection.Count > 1),
                Item("Align right", () => AlignSel(Align.Right), null, () => _doc.Selection.Count > 1),
                Item("Align top", () => AlignSel(Align.Top), null, () => _doc.Selection.Count > 1),
                Item("Align centres vertically", () => AlignSel(Align.CenterY), null, () => _doc.Selection.Count > 1),
                Item("Align bottom", () => AlignSel(Align.Bottom), null, () => _doc.Selection.Count > 1),
                new Separator(),
                Item("Flip horizontally", () => FlipSel(true), null, HasSel),
                Item("Flip vertically", () => FlipSel(false), null, HasSel),
                Item("Rotate 90° clockwise", () => RotateSel(90), null, HasSel),
                Item("Rotate 90° counter-clockwise", () => RotateSel(-90), null, HasSel),
                new Separator(),
                Item("Bring to front", () => _doc.Commit("Bring to front", Ops.BringToFront(_doc.State, _doc.Selection)), null, HasSel),
                Item("Send to back", () => _doc.Commit("Send to back", Ops.SendToBack(_doc.State, _doc.Selection)), null, HasSel),
            },
        };
        _showGridItem = new MenuItem { Header = "Show grid", ToggleType = MenuItemToggleType.CheckBox };
        _showGridItem.Click += (_, _) => { _settings.ShowGrid = _showGridItem.IsChecked; SaveAndApplyGrid(); };
        _snapObjectsItem = new MenuItem { Header = "Snap to shapes & centrelines", ToggleType = MenuItemToggleType.CheckBox };
        _snapObjectsItem.Click += (_, _) => { _settings.SnapToObjects = _snapObjectsItem.IsChecked; SaveAndApplyGrid(); };
        _snapGridItem = new MenuItem { Header = "Snap to grid", ToggleType = MenuItemToggleType.CheckBox };
        _snapGridItem.Click += (_, _) => { _settings.SnapToGrid = _snapGridItem.IsChecked; SaveAndApplyGrid(); };
        var view = new MenuItem
        {
            Header = "_View",
            Items =
            {
                Item("Fit work area", Canvas.FitField, "Home"),
                Item("Fit selection / design", Canvas.FitSelection, "F"),
                Item("Zoom in", () => Canvas.ZoomBy(1.25), "Ctrl+OemPlus"),
                Item("Zoom out", () => Canvas.ZoomBy(0.8), "Ctrl+OemMinus"),
                new Separator(),
                _showGridItem,
                _snapObjectsItem,
                _snapGridItem,
                AsyncItem("Grid & snapping…", ShowGridSettings, "Ctrl+OemQuotes"),
            },
        };
        var layer = new MenuItem
        {
            Header = "_Layer",
            Items =
            {
                Item("New layer", _layers.AddLayer, "Ctrl+L"),
                Item("Move selection to active layer", () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.ActiveLayer)), "Ctrl+M", HasSel),
            },
        };
        var tools = new MenuItem
        {
            Header = "_Tools",
            Items =
            {
                AsyncItem("Material test…", AddMaterialTest),
                AsyncItem("Trace picture…", () => SelectedPicture() is { } im ? Trace(im) : Task.CompletedTask, null, () => SelectedPicture() is not null),
                new Separator(),
                AsyncItem("Connection keys…", async () => await _machine.SetUpKeys()),
                AsyncItem("Grid & snapping…", ShowGridSettings),
            },
        };
        var help = new MenuItem
        {
            Header = "_Help",
            Items =
            {
                AsyncItem("Getting started", ShowGettingStarted),
                AsyncItem("Keyboard shortcuts", ShowShortcuts, "F1"),
                AsyncItem("Safety and disclaimer", ShowSafety),
                new Separator(),
                AsyncItem($"About {AppIdentity.ProductName}", ShowAbout),
            },
        };
        MainMenu.ItemsSource = new[] { file, edit, arrange, view, layer, tools, help };
        MainMenu.Opened += (_, _) => RefreshMenuState();
        RefreshRecent();
    }

    void RefreshMenuState()
    {
        foreach (var (item, enabled) in _conditional) item.IsEnabled = enabled();
        if (_undoItem is not null) _undoItem.Header = _doc.UndoLabel is { } u ? $"Undo {u}" : "Undo";
        if (_redoItem is not null) _redoItem.Header = _doc.RedoLabel is { } r ? $"Redo {r}" : "Redo";
    }

    void RefreshRecent()
    {
        if (_recentMenu is null) return;
        var items = _settings.RecentFiles.Where(File.Exists).Select(p =>
        {
            var m = new MenuItem { Header = p };
            m.Click += (_, _) => _ = Guard(async () => { if (await ConfirmDiscard()) await OpenPath(p); });
            return (object)m;
        }).ToList();
        _recentMenu.ItemsSource = items.Count > 0 ? items : [new MenuItem { Header = "(none)", IsEnabled = false }];
    }

    // ---- keyboard ------------------------------------------------------------

    bool TextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox or NumericUpDown || (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<NumericUpDown>() is not null;

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var m = e.KeyModifiers;
        bool ctrl = m.HasFlag(KeyModifiers.Control), shift = m.HasFlag(KeyModifiers.Shift);
        bool typing = TextInputFocused();

        // File shortcuts work everywhere.
        Func<Task>? fileAction = (ctrl, shift, e.Key) switch
        {
            (true, false, Key.S) => () => Save(false),
            (true, true, Key.S) => () => Save(true),
            (true, false, Key.O) => OpenDoc,
            (true, false, Key.N) => NewDoc,
            (true, false, Key.I) => () => ImportAny(false),
            (true, true, Key.I) => () => ImportAny(true),
            (true, false, Key.J) => ImportImage,
            (false, false, Key.F1) => ShowShortcuts,
            _ => null,
        };
        if (fileAction is not null)
        {
            e.Handled = true;
            _ = Guard(fileAction);
            return;
        }
        if (typing) return; // let text boxes keep Ctrl+Z, Ctrl+A, Delete, arrows, letters
        // Pen and point editing use these keys themselves.
        if (Canvas.Tool is Tool.Pen or Tool.Nodes && !ctrl && e.Key is Key.Delete or Key.Back or Key.Enter or Key.Escape or Key.S) return;

        Action? action = (ctrl, shift, e.Key) switch
        {
            (true, false, Key.Z) => _doc.Undo,
            (true, true, Key.Z) => _doc.Redo,
            (true, false, Key.Y) => _doc.Redo,
            (true, false, Key.X) => CutSel,
            (true, false, Key.C) => CopySel,
            (true, false, Key.V) => Paste,
            (true, false, Key.D) => DuplicateSel,
            (true, false, Key.A) => SelectAll,
            (true, true, Key.A) => () => _doc.Select(_doc.State.ShapesOn(_doc.ActiveLayer).Select(s => s.Id)),
            (true, false, Key.G) => GroupSel,
            (true, true, Key.G) => UngroupSel,
            (true, false, Key.K) => Combine,
            (true, true, Key.K) => BreakApart,
            (true, true, Key.C) => ConvertToPaths,
            (true, true, Key.U) => () => BooleanSel(BoolOp.Union),
            (true, true, Key.M) => () => BooleanSel(BoolOp.Subtract),
            (true, true, Key.O) => () => { _ = Guard(OffsetSel); },
            (true, false, Key.T) => () => { if (SelectedPicture() is { } im) _ = Guard(() => Trace(im)); },
            (true, false, Key.L) => _layers.AddLayer,
            (true, false, Key.M) => () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.ActiveLayer)),
            (true, false, Key.OemPlus or Key.Add) => () => Canvas.ZoomBy(1.25),
            (true, false, Key.OemMinus or Key.Subtract) => () => Canvas.ZoomBy(0.8),
            (true, false, Key.D0 or Key.NumPad0) => Canvas.FitField,
            (false, _, Key.Delete or Key.Back) => DeleteSel,
            (false, false, Key.Home) => Canvas.FitField,
            (false, false, Key.F) => Canvas.FitSelection,
            (false, false, Key.V) => () => Canvas.Tool = Tool.Select,
            (false, false, Key.R) => () => Canvas.Tool = Tool.Rect,
            (false, false, Key.E) => () => Canvas.Tool = Tool.Ellipse,
            (false, false, Key.L) => () => Canvas.Tool = Tool.Line,
            (false, false, Key.O) => () => Canvas.Tool = Tool.Polygon,
            (false, false, Key.S) => () => Canvas.Tool = Tool.Star,
            (false, false, Key.P) => () => Canvas.Tool = Tool.Pen,
            (false, false, Key.T) => () => Canvas.Tool = Tool.Text,
            (false, false, Key.N) => () => { if (_doc.Selection.Count == 1) Canvas.Tool = Tool.Nodes; },
            (false, false, Key.H) => () => Canvas.Tool = Tool.Pan,
            _ => null,
        };
        // Ctrl+1…9: move the selection to layer N.
        if (action is null && ctrl && !shift && e.Key is >= Key.D1 and <= Key.D9)
        {
            int idx = e.Key - Key.D1;
            if (idx < _doc.State.Layers.Count && _doc.Selection.Count > 0)
                action = () => _doc.Commit($"Move to {_doc.State.Layers[idx].Name}", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.State.Layers[idx].Id));
        }
        if (action is null) return;
        e.Handled = true;
        action();
    }

    // ---- edit commands --------------------------------------------------------

    void SelectAll() => _doc.Select(_doc.State.Editable().Select(s => s.Id));

    void DeleteSel()
    {
        if (_doc.Selection.Count == 0) return;
        _doc.Commit(_doc.Selection.Count == 1 ? "Delete shape" : "Delete shapes", Ops.Delete(_doc.State, _doc.Selection));
    }

    void CopySel()
    {
        if (_doc.Selection.Count == 0) return;
        _clipboard = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList();
        _pasteCount = 0;
    }

    void CutSel()
    {
        CopySel();
        if (_doc.Selection.Count > 0) _doc.Commit("Cut", Ops.Delete(_doc.State, _doc.Selection));
    }

    void Paste()
    {
        if (_clipboard.Count == 0) return;
        _pasteCount++;
        double off = 5 * _pasteCount;
        var layers = _doc.State.Layers.Select(l => l.Id).ToHashSet();
        // Pasted groups become new groups, not members of the originals' group.
        var copies = Ops.CopyAsUnit(_clipboard, Affine.Translate(off, off))
            .Select(s => layers.Contains(s.LayerId) ? s : s.With(layerId: _doc.ActiveLayer)).ToList();
        _doc.Commit("Paste", Ops.AddShapes(_doc.State, copies), copies.Select(c => c.Id));
    }

    void DuplicateSel()
    {
        if (_doc.Selection.Count == 0) return;
        var (s, ids) = Ops.Duplicate(_doc.State, _doc.Selection, 5, 5);
        _doc.Commit("Duplicate", s, ids);
    }

    void Combine()
    {
        var (s, id) = Ops.Combine(_doc.State, _doc.Selection);
        if (id is { } i) _doc.Commit("Combine", s, [i]);
    }

    void BreakApart()
    {
        var (s, ids) = Ops.BreakApart(_doc.State, _doc.Selection);
        if (ids.Count > 0) _doc.Commit("Break apart", s, ids);
    }

    void AlignSel(Align how)
    {
        if (_doc.Selection.Count < 2) return;
        _doc.Commit("Align", Ops.AlignTo(_doc.State, _doc.Selection, how, _doc.State.Bounds(_doc.Selection)));
    }

    void FlipSel(bool horizontal)
    {
        var c = _doc.State.Bounds(_doc.Selection).Center;
        _doc.Commit(horizontal ? "Flip horizontally" : "Flip vertically",
            Ops.Transform(_doc.State, _doc.Selection, Affine.ScaleAbout(c, horizontal ? -1 : 1, horizontal ? 1 : -1)));
    }

    void RotateSel(double deg)
    {
        var c = _doc.State.Bounds(_doc.Selection).Center;
        _doc.Commit("Rotate", Ops.Transform(_doc.State, _doc.Selection, Affine.RotateAbout(c, deg)));
    }

    // ---- files ----------------------------------------------------------------

    /// <summary>Run a UI action and report failures in a dialog instead of crashing.</summary>
    async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or JobException or InvalidOperationException or System.Xml.XmlException)
        {
            await Dialogs.Message(this, "Something went wrong", e.Message);
        }
    }

    async Task<bool> ConfirmDiscard()
    {
        if (!_doc.Dirty) return true;
        var name = _doc.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : "Untitled";
        var choice = await Dialogs.SaveChanges(this, name);
        return choice switch
        {
            true => await Save(false),
            false => true,
            null => false,
        };
    }

    async Task NewDoc()
    {
        if (!await ConfirmDiscard()) return;
        _doc.Reset(DocState.New(), null);
        _layers.Activate(_doc.ActiveLayer);
        Canvas.FitField();
    }

    async Task OpenDoc()
    {
        if (!await ConfirmDiscard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = [ProjectType],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await OpenPath(path);
    }

    async Task OpenPath(string path)
    {
        try
        {
            var state = DocFile.Load(path);
            _doc.Reset(state, path);
            _layers.Activate(_doc.ActiveLayer);
            _settings.AddRecent(path);
            _settings.Save();
            RefreshRecent();
            Canvas.FitSelection();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            await Dialogs.Message(this, "Couldn't open project", e.Message);
        }
    }

    async Task<bool> Save(bool saveAs)
    {
        var path = _doc.FilePath;
        if (saveAs || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save project",
                DefaultExtension = "pcanvas",
                SuggestedFileName = path is null ? "Untitled" + DocFile.Extension : Path.GetFileNameWithoutExtension(path) + DocFile.Extension,
                FileTypeChoices = [ProjectType],
            });
            path = file?.TryGetLocalPath();
            if (path is null) return false;
        }
        DocFile.Save(_doc.State, path);
        _doc.MarkSaved(path);
        _settings.AddRecent(path);
        _settings.Save();
        RefreshRecent();
        return true;
    }

    async Task ImportAny(bool byColour)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = byColour ? "Import (one layer per colour)" : "Import",
            AllowMultiple = true,
            FileTypeFilter = [AnyImportable, VectorTypes],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        var pictures = paths.Where(IsPicture).ToList();
        var drawings = paths.Where(p => !IsPicture(p)).ToList();
        if (pictures.Count > 0) await ImportPictures(pictures);
        if (drawings.Count > 0) await ImportPaths(drawings, byColour);
    }

    async Task ImportPaths(IReadOnlyList<string> paths, bool byColour)
    {
        var state = _doc.State;
        var newIds = new List<Guid>();
        var warnings = new List<string>();
        var target = state.FindLayer(_doc.ActiveLayer) is { Visible: true, Locked: false } a ? a.Id
            : state.Layers.FirstOrDefault(l => l.Visible && !l.Locked)?.Id;
        if (target is null && !byColour)
        {
            await Dialogs.Message(this, "Import", "Every layer is hidden or locked. Show or unlock a layer to import into it.");
            return;
        }
        foreach (var path in paths)
        {
            ImportResult r;
            try
            {
                var px = _settings.SvgPxPerInch;
                r = await Task.Run(() => Importer.Load(path, px));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or System.Xml.XmlException)
            {
                warnings.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }
            warnings.AddRange(r.Warnings.Select(w => $"{Path.GetFileName(path)}: {w}"));
            if (r.Paths.Count == 0 && r.Images.Count == 0) continue;

            // Keep the drawing's position when it fits the work area; otherwise centre it.
            var b = r.Bounds;
            var field = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH);
            var move = field.Contains(b) ? Affine.Identity
                : Affine.Translate(field.Center.X - b.Center.X, field.Center.Y - b.Center.Y);
            if (b.Width > field.Width || b.Height > field.Height)
                warnings.Add($"{Path.GetFileName(path)} is {b.Width:0.#} × {b.Height:0.#} mm, larger than the {field.Width:0} × {field.Height:0} mm work area. Scale it down before sending.");

            // xTool projects name their colour layers; keep those names.
            bool named = r.LayerNames.Count > 0;
            foreach (var ip in r.Paths)
            {
                Guid layerId;
                if (byColour || named)
                {
                    var colour = ip.Color ?? 0x000000;
                    var layerName = r.LayerNames.TryGetValue(colour, out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : ColourLayerName(colour);
                    var existing = state.Layers.FirstOrDefault(l => l.Name == layerName && l.Mode != LayerMode.Image);
                    if (existing is null)
                    {
                        existing = Layer.Default(state.Layers.Count) with { Name = layerName, Color = DisplayColour(colour, state) };
                        state = Ops.AddLayer(state, existing);
                    }
                    layerId = existing.Id;
                }
                else layerId = target!.Value;
                var shape = new PathShape { Name = ip.Name, LayerId = layerId, Geometry = ip.Geometry.Transform(move) };
                state = Ops.AddShapes(state, [shape]);
                newIds.Add(shape.Id);
            }
            foreach (var im in r.Images)
            {
                ImageSource src;
                try { src = ImageSource.FromEncoded(im.Encoded); }
                catch (FormatException) { warnings.Add($"{Path.GetFileName(path)}: a picture in the file couldn't be read."); continue; }
                var picLayer = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Image && l.Visible && !l.Locked);
                if (picLayer is null)
                {
                    picLayer = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = "Pictures" };
                    state = Ops.AddLayer(state, picLayer, 0);
                }
                var pic = new ImageShape { Name = im.Name, LayerId = picLayer.Id, Source = src, SizeMm = im.SizeMm, Transform = im.Transform.Then(move) };
                state = Ops.AddShapes(state, [pic]);
                newIds.Add(pic.Id);
            }
        }
        if (newIds.Count > 0)
        {
            _doc.Commit(paths.Count == 1 ? $"Import {Path.GetFileName(paths[0])}" : $"Import {paths.Count} files", state, newIds);
            Canvas.FitSelection();
        }
        if (warnings.Count > 0)
            await Dialogs.Message(this, "Import notes", string.Join("\n\n", warnings.Distinct().Take(12)));
    }

    static string ColourLayerName(uint c) => $"Colour #{c:X6}";

    // ---- text, groups, shape operations ----------------------------------------------

    void CreateText(Vec2 at)
    {
        var layer = _doc.State.FindLayer(_doc.ActiveLayer) is { Visible: true, Locked: false } l && l.Mode != LayerMode.Image ? l
            : _doc.State.Layers.FirstOrDefault(x => x.Visible && !x.Locked && x.Mode != LayerMode.Image);
        if (layer is null) return;
        var style = new TextStyle(_settings.TextFont, _settings.TextSizeMm);
        var text = TextShape.Create("Text", style, layer.Id, Affine.Identity);
        // Place the text's top-left where the user clicked.
        var b = text.Bounds;
        var placed = (TextShape)text.With(transform: Affine.Translate(at.X - (b.IsEmpty ? 0 : b.MinX), at.Y - (b.IsEmpty ? 0 : b.MinY)));
        _doc.Commit("Add text", Ops.AddShapes(_doc.State, [placed]), [placed.Id]);
        Canvas.Tool = Tool.Select;
        RightTabs.SelectedIndex = 0;
        _props.FocusText(selectAll: true);
    }

    void GroupSel()
    {
        var (s, g) = Ops.Group(_doc.State, _doc.Selection);
        if (g != Guid.Empty) _doc.Commit("Group", s, _doc.Selection);
    }

    void UngroupSel() => _doc.Commit("Ungroup", Ops.Ungroup(_doc.State, _doc.Selection));

    void ConvertToPaths()
    {
        var state = _doc.State;
        foreach (var t in state.Shapes.OfType<TextShape>().Where(t => _doc.Selection.Contains(t.Id)).ToList())
            state = Ops.Replace(state, new PathShape { Id = t.Id, Name = t.Name, LayerId = t.LayerId, GroupId = t.GroupId, Transform = t.Transform, Geometry = t.Geometry });
        _doc.Commit("Convert to paths", state);
    }

    /// <summary>Selected vector shapes (text included), in drawing order.</summary>
    List<PathShape> SelectedPaths() => _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).OfType<PathShape>().ToList();

    void BooleanSel(BoolOp op)
    {
        var parts = SelectedPaths();
        if (parts.Count < 2) return;
        // The bottom shape is the base: Subtract takes everything above away from it.
        var result = op switch
        {
            BoolOp.Union => ShapeGeometry.Union(parts.Select(p => p.World)),
            BoolOp.Subtract => ShapeGeometry.Boolean(parts[0].World, ShapeGeometry.Union(parts.Skip(1).Select(p => p.World)), BoolOp.Subtract),
            _ => parts.Skip(1).Aggregate(parts[0].World, (acc, p) => ShapeGeometry.Boolean(acc, p.World, op)),
        };
        if (result.IsEmpty)
        {
            _ = Dialogs.Message(this, "Nothing left", op == BoolOp.Intersect ? "The shapes don't overlap, so there's nothing in common." : "The operation removed everything.");
            return;
        }
        string label = op switch { BoolOp.Union => "Weld", BoolOp.Subtract => "Subtract", BoolOp.Intersect => "Intersect", _ => "Exclude" };
        var shape = new PathShape { Name = label, LayerId = parts[0].LayerId, Geometry = result };
        var state = Ops.Delete(_doc.State, parts.Select(p => p.Id).ToHashSet());
        _doc.Commit(label, Ops.AddShapes(state, [shape]), [shape.Id]);
    }

    async Task OffsetSel()
    {
        if (SelectedPaths().Count == 0) return;
        await new OffsetWindow(_doc, _settings).ShowDialog(this);
    }

    async Task ArraySel(bool circular)
    {
        if (_doc.Selection.Count == 0) return;
        await new ArrayWindow(_doc, circular, Canvas.Machine).ShowDialog(this);
    }

    void DistributeSel(bool horizontal) =>
        _doc.Commit(horizontal ? "Distribute horizontally" : "Distribute vertically", Ops.Distribute(_doc.State, _doc.Selection, horizontal));

    // ---- pictures ------------------------------------------------------------------------

    static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];
    static bool IsPicture(string path) => PictureExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    ImageShape? SelectedPicture() =>
        _doc.Selection.Count == 1 && _doc.State.FindShape(_doc.Selection.First()) is ImageShape im ? im : null;

    async Task ImportImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import picture",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = [.. PictureExtensions.Select(e => "*" + e)] }],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await ImportPictures(paths);
    }

    /// <summary>Pictures go onto an Image layer (made if needed), centred in the work area, up to 100 mm across.</summary>
    async Task ImportPictures(IReadOnlyList<string> paths)
    {
        var state = _doc.State;
        var layer = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Image && l.Visible && !l.Locked);
        if (layer is null)
        {
            layer = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = "Pictures" };
            state = Ops.AddLayer(state, layer, 0);
        }
        var ids = new List<Guid>();
        var problems = new List<string>();
        var field = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH);
        double offset = 0;
        foreach (var path in paths)
        {
            ImageSource src;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                src = await Task.Run(() => ImageSource.FromEncoded(bytes));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                problems.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }
            var size = ImageShape.DefaultSize(src, 100);
            var at = field.Center - size / 2 + new Vec2(offset, offset);
            offset += 5;
            var shape = new ImageShape
            {
                Name = Path.GetFileNameWithoutExtension(path),
                LayerId = layer.Id,
                Source = src,
                SizeMm = size,
                Transform = Affine.Translate(at),
            };
            state = Ops.AddShapes(state, [shape]);
            ids.Add(shape.Id);
        }
        if (ids.Count > 0)
        {
            _doc.Commit(ids.Count == 1 ? "Import picture" : $"Import {ids.Count} pictures", state, ids);
            Canvas.FitSelection();
        }
        if (problems.Count > 0) await Dialogs.Message(this, "Some pictures couldn't be imported", string.Join("\n", problems));
    }

    async Task Trace(ImageShape im)
    {
        var w = new TraceWindow(im, _doc.State.Layers, _doc.ActiveLayer);
        await w.ShowDialog(this);
        if (w.Choice is not { } choice) return;

        var state = _doc.State;
        var ids = new List<Guid>();
        var toWorld = im.PixelToWorld;
        Layer NewLayer(string name, uint? colour)
        {
            var l = Layer.Default(state.Layers.Count, choice.NewLayerMode) with { Name = name };
            if (colour is { } c) l = l with { Color = DisplayColour(c, state) };
            state = Ops.AddLayer(state, l);
            return l;
        }
        Guid? bwLayer = choice.TargetLayer;
        foreach (var part in choice.Parts)
        {
            Guid layerId;
            if (part.Rgb is { } rgb)
            {
                var name = ColourLayerName(rgb);
                layerId = (state.Layers.FirstOrDefault(l => l.Name == name && l.Mode != LayerMode.Image) ?? NewLayer(name, rgb)).Id;
            }
            else layerId = bwLayer ??= NewLayer($"Traced {im.Name}".Trim(), null).Id;
            var shape = new PathShape { Name = $"Traced {im.Name}".Trim(), LayerId = layerId, Geometry = part.Geometry.Transform(toWorld) };
            state = Ops.AddShapes(state, [shape]);
            ids.Add(shape.Id);
        }
        if (choice.RemovePicture) state = Ops.Delete(state, new HashSet<Guid> { im.Id });
        _doc.Commit("Trace picture", state, ids);
    }

    /// <summary>The SVG colour, unless it would vanish on the dark canvas.</summary>
    static uint DisplayColour(uint c, DocState s)
    {
        double lum = (0.2126 * (c >> 16 & 0xFF) + 0.7152 * (c >> 8 & 0xFF) + 0.0722 * (c & 0xFF)) / 255;
        if (lum > 0.25) return c;
        var used = s.Layers.Select(l => l.Color).ToHashSet();
        return Layer.Palette.FirstOrDefault(p => !used.Contains(p), Layer.Palette[s.Layers.Count % Layer.Palette.Count]);
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        string[] drawingTypes = [".svg", ".dxf", ".pdf", ".ai", ".xs", ".xcs"];
        var svgs = files.Where(f => drawingTypes.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
        var pictures = files.Where(IsPicture).ToList();
        var project = files.FirstOrDefault(f => f.EndsWith(DocFile.Extension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(DocFile.LegacyExtension, StringComparison.OrdinalIgnoreCase));
        if (pictures.Count > 0) _ = Guard(() => ImportPictures(pictures));
        if (svgs.Count > 0) _ = Guard(() => ImportPaths(svgs, e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
        else if (project is not null) _ = Guard(async () => { if (await ConfirmDiscard()) await OpenPath(project); });
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !_doc.Dirty)
        {
            _machine.Shutdown();
            return;
        }
        e.Cancel = true;
        if (await ConfirmDiscard())
        {
            _closeConfirmed = true;
            Dispatcher.UIThread.Post(Close);
        }
    }

    Task ShowShortcuts() => Dialogs.Message(this, "Keyboard shortcuts", string.Join("\n",
        "V select · R rectangle · E ellipse · O polygon · S star · L line · P pen · T text · N edit points · H pan",
        "Space or middle mouse: pan · wheel: zoom · Shift+wheel: scroll sideways",
        "Home: fit work area · F: fit selection",
        "Arrows: nudge 1 mm (Shift 10 mm, Ctrl 0.1 mm)",
        "Drag left→right: select inside · right→left: select touching",
        "Corner handles keep proportions (Shift = free) · Alt = from centre",
        "Rotate handle: Shift snaps to 15° · hold Ctrl while dragging to turn snapping off",
        "Ctrl+D duplicate · Ctrl+G group · Ctrl+Shift+G ungroup · Ctrl+K combine · Ctrl+Shift+K break apart",
        "Ctrl+1…9 move selection to layer 1…9 · Ctrl+M to the active layer",
        "Ctrl+Z / Ctrl+Y undo / redo · Esc cancel / deselect"));

    Task ShowGettingStarted() => new WelcomeWindow(firstRun: false, _machine.SetUpKeys, () => _machine.HasKeys).ShowDialog(this);

    Task ShowSafety() => Dialogs.Message(this, "Safety and disclaimer", WelcomeWindow.SafetyText + "\n\n" + WelcomeWindow.DisclaimerText);

    Task ShowAbout() => Dialogs.Message(this, $"About {AppIdentity.ProductName}",
        $"{AppIdentity.ProductName} – an offline, no-telemetry laser workspace for the xTool F2 Ultra.\n\n" +
        "Jobs are generated locally in the same G-code dialect as the stock software, verified line for line against real exports.\n\n" +
        "Free software under the GNU General Public License v3 (see LICENSE and THIRD-PARTY-NOTICES). No warranty. " +
        "Not affiliated with, endorsed by or supported by xTool or Makeblock. \"xTool\" is their trademark, used only to say which machines this works with.");

    async Task AddMaterialTest()
    {
        var w = new MaterialTestWindow(_doc.State, _settings.TextFont);
        await w.ShowDialog(this);
        if (w.Result is not { } o) return;
        var (state, added) = MaterialTest.Add(_doc.State, o);
        _doc.Commit("Material test", state, added);
        Canvas.FitField();
    }
}
