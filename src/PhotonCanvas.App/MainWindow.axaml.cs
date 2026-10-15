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
    readonly LayerEditor _layerEditor;
    readonly PropertiesPanel _props;
    readonly JobBar _job;
    readonly MachinePanel _machine;
    readonly MachineHud _hud;
    readonly Ribbon _ribbon = new();
    List<Shape> _clipboard = [];
    int _pasteCount;
    bool _closeConfirmed;

    /// <summary>A project to open once the window is shown (from the command line).</summary>
    public string? OpenPathOnStartup { get; set; }

    /// <summary>Connect to the machine when the window opens (off for screenshots and tests).</summary>
    public bool ConnectOnOpen { get; init; } = true;

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
        _layers.ShapeHovered += id => Canvas.HighlightShape = id;
        _layers.ShapeActions = ShapeActions;
        _props = new PropertiesPanel(_doc, _layers, _settings);
        _props.TraceRequested += im => _ = Guard(() => Trace(im));
        _props.FitToCameraRequested += () => _ = Guard(FitToCamera);
        _props.DepthmapRequested += im => _ = Guard(() => MakeDepthmap(im));
        _props.RemoveBackgroundRequested += im => _ = Guard(() => RemoveBackground(im));
        _library = Doc.Materials.MaterialLibrary.Load(Path.Combine(AppIdentity.DataDir, "materials"));
        if (!_settings.PresetsAdopted)
        {
            // Saved presets move into the material library once.
            if (_library.AdoptPresets(_settings.Presets.Select(p => (p.Name, p.Mode, p.Laser, p.LinesPerCm))) > 0) _library.Save();
            _settings.PresetsAdopted = true;
            _settings.Save();
        }
        _layerEditor = new LayerEditor(_doc, _layers, _settings) { Library = _library };
        _layerEditor.SaveToLibraryRequested += l => _ = Guard(() => SaveToLibrary(l));
        _layerEditor.LibraryRequested += () => _ = Guard(ShowLibrary);
        _layers.Editor = _layerEditor;
        _machine = new MachinePanel(_settings) { OwnerWindow = this };
        _job = new JobBar(_doc, _settings, Canvas, _machine);
        _job.JobBuilt += j => _layers.SetTimes(j?.LayerTimes.ToDictionary(t => t.LayerId, t => t.TotalSeconds) ?? new());
        _job.LayerClicked += id => _layers.Activate(id);
        _hud = new MachineHud(_machine, _settings);
        _hud.SettingsRequested += () => _ = Guard(() => ShowSettings("Machine"));
        _hud.CalibrateRequested += () => _ = Guard(_machine.CalibrateAsync);
        _hud.FoldChanged += () => _ribbon.Refresh();
        _hud.FoldChanged += () => _ribbon.Refresh();
        LayersHost.Content = _layers;
        InspectorHost.Content = _props;
        JobHost.Content = _job;
        HudHost.Content = _hud;
        RibbonHost.Content = _ribbon;

        BuildRibbon();
        WireTools();
        ApplyGrid();
        Canvas.CursorMoved += p => CursorText.Text = p is { } v ? $"X {v.X:0.00}   Y {v.Y:0.00} mm" : "";
        Canvas.ZoomChanged += z => ZoomText.Text = $"{z / 3.7795 * 100:0}%";
        _doc.Changed += _ => UpdateChrome();
        _doc.Changed += _ => FollowSelectionLayer();
        _machine.PhotoChanged += p => Canvas.Photo = p;
        _machine.StateChanged += _ribbon.Refresh;
        _machine.FramingChanged += _ribbon.Refresh;
        Canvas.Hint += h => StatusText.Text = h;
        Canvas.CameraOpacity = _machine.OverlayOpacity;
        _machine.OverlayOpacityChanged += o => Canvas.CameraOpacity = o;
        _machine.OverlayChanged += b =>
        {
            var old = Canvas.CameraImage;
            Canvas.CameraImage = b;
            if (!ReferenceEquals(old, b)) old?.Dispose();
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Canvas.ContextRequested += (_, e) => { e.Handled = true; OpenCanvasMenu(); };
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        Opened += async (_, _) =>
        {
            Canvas.Focus();
            if (ConnectOnOpen && _settings.AcceptedTermsVersion < WelcomeWindow.TermsVersion)
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
            if (ConnectOnOpen) _ = _machine.AutoConnectAsync();
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
        if (_doc.InEdit) return;
        int n = _doc.State.Shapes.Count, sel = _doc.Selection.Count;
        StatusText.Text = sel > 0 ? $"{sel} of {n} shapes selected" : n == 0
            ? "Draw with the Home tab, or drag a file in (SVG, DXF, PDF, pictures). New material? Laser → Material test."
            : $"{n} shapes · {_doc.State.Layers.Count} layers";
        _ribbon.Refresh();
        _ribbon.Highlight("Picture", SelectedPicture() is not null);
        if (_undoButton is not null)
        {
            ToolTip.SetTip(_undoButton, _doc.UndoLabel is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)");
            ToolTip.SetTip(_redoButton!, _doc.RedoLabel is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)");
        }
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

    readonly List<(ToggleButton Button, Tool Tool)> _toolButtons = [];

    void WireTools()
    {
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
                Tool.Text => "Click where the text should start, then type in the Text panel on the right.",
                Tool.Wand => "Click inside a piece on the camera picture to outline it. The sliders refine the last pick.",
                Tool.Magnetic => "Click along an edge on the camera picture. Click the first point to close; Enter or double-click to finish; Backspace undoes a point.",
                Tool.Nodes => "Drag points and handles. Double-click an edge to add a point; Delete removes the selected one; S makes it smooth or sharp. Alt-drag a handle to break a smooth point. Esc to finish.",
                _ => "",
            };
            WandPanel.IsVisible = t is Tool.Wand or Tool.Magnetic;
            WandTolGroup.IsVisible = WandIgnoreGroup.IsVisible = t == Tool.Wand;
            ToolOptions.IsVisible = poly || ToolHint.Text.Length > 0;
            Canvas.Focus();
        }
        Canvas.ToolChanged += Options;
        WireWand();
        Canvas.TextRequested += at => CreateText(at);
        Canvas.TextEditRequested += id => { _doc.Select([id]); _props.FocusText(); };
        void Sync(Tool t) { foreach (var (b, tool) in _toolButtons) b.IsChecked = tool == t; }
        Canvas.ToolChanged += Sync;
        Sync(Canvas.Tool);
    }

    /// <summary>Magic wand / magnetic pen sliders: remembered, and applied live.</summary>
    void WireWand()
    {
        WandTolerance.Value = _settings.WandTolerance;
        WandSmoothness.Value = _settings.WandSmoothness;
        WandCorners.Value = _settings.WandCorners;
        WandIgnore.Value = _settings.WandIgnoreMm2;
        void Apply(bool save)
        {
            WandToleranceText.Text = $"{WandTolerance.Value:0}";
            WandSmoothnessText.Text = $"{WandSmoothness.Value:0.#}";
            WandCornersText.Text = $"{WandCorners.Value:0}°";
            WandIgnoreText.Text = $"{WandIgnore.Value:0.#} mm²";
            Canvas.WandOptions = new Doc.Tracing.PickOptions
            {
                Tolerance = WandTolerance.Value,
                Smoothness = WandSmoothness.Value,
                CornerAngle = WandCorners.Value,
                IgnoreBelowMm2 = WandIgnore.Value,
            };
            if (!save) return;
            _settings.WandTolerance = WandTolerance.Value;
            _settings.WandSmoothness = WandSmoothness.Value;
            _settings.WandCorners = WandCorners.Value;
            _settings.WandIgnoreMm2 = WandIgnore.Value;
            _settings.Save();
        }
        foreach (var s in new[] { WandTolerance, WandSmoothness, WandCorners, WandIgnore })
            s.ValueChanged += (_, _) => Apply(save: true);
        Apply(save: false);
    }

    // ---- ribbon ----------------------------------------------------------------

    Button? _undoButton, _redoButton;
    MenuItem? _recentMenu;

    /// <summary>Push the grid and snap settings to the canvas (the ribbon's toggles read them back).</summary>
    void ApplyGrid()
    {
        Canvas.Grid = GridWindow.From(_settings);
        Canvas.ShowGrid = _settings.ShowGrid;
        Canvas.SnapToObjects = _settings.SnapToObjects;
        Canvas.SnapToGrid = _settings.SnapToGrid;
        Canvas.SnapDistancePx = _settings.SnapDistancePx;
        _ribbon.Refresh();
    }

    void SaveAndApplyGrid()
    {
        _settings.Save();
        ApplyGrid();
    }

    Task ShowGridSettings() => new GridWindow(_settings, ApplyGrid).ShowDialog(this);

    MenuItem Item(string header, Action action, string? gesture = null)
    {
        var m = new MenuItem { Header = header };
        if (gesture is not null) m.InputGesture = KeyGesture.Parse(gesture);
        m.Click += (_, _) => action();
        return m;
    }

    MenuItem AsyncItem(string header, Func<Task> action, string? gesture = null) => Item(header, () => _ = Guard(action), gesture);

    static Button HeaderIcon(string icon, string tip)
    {
        var b = new Button { Classes = { "icon" }, Content = new PathIcon { Data = UiKit.Icon(icon) } };
        ToolTip.SetTip(b, tip);
        return b;
    }

    void BuildRibbon()
    {
        bool HasSel() => _doc.Selection.Count > 0;
        bool Many() => _doc.Selection.Count > 1;
        bool TwoPaths() => SelectedPaths().Count > 1;
        bool HasPicture() => SelectedPicture() is not null;
        void Use(Tool t) { Canvas.Tool = t; Canvas.Focus(); }
        ToggleButton ToolLarge(RibbonGroup g, string label, string icon, Tool t, string tip)
        {
            var b = g.LargeToggle(label, icon, _ => Use(t), tip);
            _toolButtons.Add((b, t));
            return b;
        }
        ToggleButton ToolSmall(RibbonGroup g, string label, string icon, Tool t, string tip)
        {
            var b = g.SmallToggle(label, icon, _ => Use(t), tip);
            _toolButtons.Add((b, t));
            return b;
        }

        // ---- File (the button left of the tabs) ----
        _recentMenu = new MenuItem { Header = "Open recent" };
        var file = new Button
        {
            Content = "File", Background = UiKit.Brush("Accent"), Foreground = Avalonia.Media.Brushes.White,
            FontWeight = Avalonia.Media.FontWeight.SemiBold, Padding = new(14, 4), CornerRadius = new(5),
        };
        file.Flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
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
                AsyncItem("Settings…", () => ShowSettings("Machine")),
                Item("Exit", Close),
            },
        };
        _ribbon.SetLead(file);

        _undoButton = HeaderIcon("IcUndo", "Undo (Ctrl+Z)");
        _undoButton.Click += (_, _) => _doc.Undo();
        _redoButton = HeaderIcon("IcRedo", "Redo (Ctrl+Y)");
        _redoButton.Click += (_, _) => _doc.Redo();
        var save = HeaderIcon("IcSave", "Save (Ctrl+S)");
        save.Click += (_, _) => _ = Guard(() => Save(false));
        var gear = HeaderIcon("IcSettings", "Settings: machine connection, camera calibration, grid, libraries");
        gear.Click += (_, _) => _ = Guard(() => ShowSettings("Machine"));
        var help = HeaderIcon("IcHelp", "Help");
        help.Flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Items =
            {
                AsyncItem("Getting started", ShowGettingStarted),
                AsyncItem("Keyboard shortcuts", ShowShortcuts, "F1"),
                AsyncItem("Safety and disclaimer", ShowSafety),
                new Separator(),
                AsyncItem($"About {AppIdentity.ProductName}", ShowAbout),
            },
        };
        _ribbon.SetTrail(save, _undoButton, _redoButton, new Border { Width = 1, Background = UiKit.Brush("Line"), Margin = new(6, 6) }, gear, help);
        _ribbon.Track(_undoButton, () => _doc.CanUndo);
        _ribbon.Track(_redoButton, () => _doc.CanRedo);

        // ---- Home: everyday drawing and editing ----
        var home = _ribbon.Tab("Home", "Draw, select and edit");
        var clip = home.Group("Clipboard");
        clip.Large("Paste", "IcPaste", Paste, "Paste (Ctrl+V)", () => _clipboard.Count > 0);
        clip.Small("Cut", "IcCut", CutSel, "Cut (Ctrl+X)", HasSel);
        clip.Small("Copy", "IcCopy", CopySel, "Copy (Ctrl+C)", HasSel);
        clip.Small("Duplicate", "IcDuplicate", DuplicateSel, "Duplicate, offset a little (Ctrl+D)", HasSel);
        var select = home.Group("Select");
        ToolLarge(select, "Select", "IcCursor", Tool.Select, "Select and move (V). Drag left→right to select inside, right→left to select touching.");
        ToolSmall(select, "Edit points", "IcNodes", Tool.Nodes, "Edit a shape's points and curves (N, or double-click a shape)");
        ToolSmall(select, "Pan", "IcHand", Tool.Pan, "Pan the view (H, or hold Space / middle mouse)");
        select.Small("Delete", "IcTrash", DeleteSel, "Delete (Del)", HasSel);
        var draw = home.Group("Draw");
        ToolLarge(draw, "Rectangle", "IcRect", Tool.Rect, "Rectangle (R) · Shift = square · Alt = from centre");
        ToolLarge(draw, "Ellipse", "IcEllipse", Tool.Ellipse, "Ellipse (E) · Shift = circle · Alt = from centre");
        ToolLarge(draw, "Pen", "IcPen", Tool.Pen, "Pen (P) · click = corner · drag = curve · click the first point to close · Enter to finish");
        ToolLarge(draw, "Text", "IcText", Tool.Text, "Text (T) · click where the text should go");
        ToolSmall(draw, "Polygon", "IcPolygon", Tool.Polygon, "Polygon (O) · Shift = even · Alt = from centre");
        ToolSmall(draw, "Star", "IcStar", Tool.Star, "Star (S) · Shift = even · Alt = from centre");
        ToolSmall(draw, "Line", "IcLine", Tool.Line, "Line (L) · Shift = 45° steps");
        draw.Large("Shapes", "IcHeart", ShowShapes, "Shape library (I): hearts, stars, badges, frames, living hinges and your own saved shapes");
        var import = home.Group("Bring in");
        import.Large("Import", "IcImport", () => _ = Guard(() => ImportAny(false)), "Import drawings and pictures: SVG, DXF, PDF, AI, xTool projects, PNG, JPG… (Ctrl+I)");
        import.Small("Picture", "IcImage", () => _ = Guard(ImportImage), "Import a picture onto an Image layer (Ctrl+J)");
        import.Small("By colour", "IcLayers", () => _ = Guard(() => ImportAny(true)), "Import a drawing with one layer per colour (Ctrl+Shift+I)");
        var lay = home.Group("Layers");
        lay.Large("New layer", "IcPlus", _layers.AddLayer, "Add a layer below the active one (Ctrl+L)");
        lay.Small("Move here", "IcDown", () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.ActiveLayer)), "Move the selected shapes to the active layer (Ctrl+M)", HasSel);

        // ---- Shape: combining and changing shapes ----
        var shape = _ribbon.Tab("Shape", "Group, combine, weld, offset and repeat shapes");
        var grp = shape.Group("Group");
        grp.Large("Group", "IcGroup", GroupSel, "Group: select and move them as one (Ctrl+G)", Many);
        grp.Small("Ungroup", "IcUngroup", UngroupSel, "Ungroup (Ctrl+Shift+G)", HasSel);
        grp.Small("Combine", "IcCombine", Combine, "Combine into one compound shape: overlapping parts make holes (Ctrl+K)", Many);
        grp.Small("Break apart", "IcBreak", BreakApart, "Break a compound shape into its pieces (Ctrl+Shift+K)", HasSel);
        var boolean = shape.Group("Shape builder");
        boolean.Large("Weld", "IcWeld", () => BooleanSel(BoolOp.Union), "Weld: merge overlapping shapes into one outline (Ctrl+Shift+U)", TwoPaths);
        boolean.Small("Subtract", "IcSubtract", () => BooleanSel(BoolOp.Subtract), "Subtract: the bottom shape minus the ones above it (Ctrl+Shift+M)", TwoPaths);
        boolean.Small("Intersect", "IcIntersect", () => BooleanSel(BoolOp.Intersect), "Intersect: keep only where they overlap", TwoPaths);
        boolean.Small("Exclude", "IcExclude", () => BooleanSel(BoolOp.Exclude), "Exclude: remove where they overlap", TwoPaths);
        var outline = shape.Group("Outline");
        outline.Large("Offset", "IcOffset", () => _ = Guard(OffsetSel), "Offset: a new outline a set distance outside or inside (Ctrl+Shift+O)", () => SelectedPaths().Count > 0);
        outline.Small("Text to paths", "IcToPaths", ConvertToPaths, "Turn text into ordinary shapes (Ctrl+Shift+C)", () => _doc.State.Shapes.Any(s => _doc.Selection.Contains(s.Id) && s is TextShape));
        var repeat = shape.Group("Repeat");
        repeat.Large("Nest", "IcNest", () => _ = Guard(NestSel), "Pack copies or parts onto the work area or a piece, as tightly as they fit (Ctrl+Alt+N)", HasSel);
        repeat.Small("Grid array", "IcArrayGrid", () => _ = Guard(() => ArraySel(circular: false)), "Copies in rows and columns", HasSel);
        repeat.Small("Circular array", "IcArrayCircle", () => _ = Guard(() => ArraySel(circular: true)), "Copies around a centre", HasSel);
        var lib = shape.Group("Library");
        lib.Large("Shapes", "IcHeart", ShowShapes, "Shape library (I); save your own shapes there too");

        // ---- Arrange: position, align, order ----
        var arrange = _ribbon.Tab("Arrange", "Align, distribute, flip, rotate and order");
        var align = arrange.Group("Align");
        align.Icons(3,
            ("IcAlignLeft", "Align left edges", () => AlignSel(Align.Left), Many),
            ("IcAlignCenterH", "Align centres horizontally", () => AlignSel(Align.CenterX), Many),
            ("IcAlignRight", "Align right edges", () => AlignSel(Align.Right), Many),
            ("IcAlignTop", "Align top edges", () => AlignSel(Align.Top), Many),
            ("IcAlignMiddle", "Align centres vertically", () => AlignSel(Align.CenterY), Many),
            ("IcAlignBottom", "Align bottom edges", () => AlignSel(Align.Bottom), Many));
        align.Small("Spread across", "IcDistH", () => DistributeSel(true), "Distribute horizontally: equal gaps (3 or more shapes)", () => _doc.Selection.Count > 2);
        align.Small("Spread down", "IcDistV", () => DistributeSel(false), "Distribute vertically: equal gaps (3 or more shapes)", () => _doc.Selection.Count > 2);
        align.Small("Centre on bed", "IcFit", CenterSelInField, "Move the selection to the middle of the work area", HasSel);
        var turn = arrange.Group("Flip & rotate");
        turn.Small("Flip across", "IcFlipH", () => FlipSel(true), "Flip horizontally (mirror left–right)", HasSel);
        turn.Small("Flip over", "IcFlipV", () => FlipSel(false), "Flip vertically (mirror top–bottom)", HasSel);
        turn.Break();
        turn.Small("Turn right", "IcRotateCw", () => RotateSel(90), "Rotate 90° clockwise", HasSel);
        turn.Small("Turn left", "IcRotateCcw", () => RotateSel(-90), "Rotate 90° counter-clockwise", HasSel);
        var order = arrange.Group("Order");
        order.Small("To front", "IcFront", () => _doc.Commit("Bring to front", Ops.BringToFront(_doc.State, _doc.Selection)), "Bring to front (drawn last on its layer)", HasSel);
        order.Small("To back", "IcBack", () => _doc.Commit("Send to back", Ops.SendToBack(_doc.State, _doc.Selection)), "Send to back", HasSel);
        var selectAll = arrange.Group("Select");
        selectAll.Small("All", "IcCursor", SelectAll, "Select everything (Ctrl+A)");
        selectAll.Small("Active layer", "IcLayers", () => _doc.Select(_doc.State.ShapesOn(_doc.ActiveLayer).Select(s => s.Id)), "Select everything on the active layer (Ctrl+Shift+A)");
        selectAll.Small("Invert", "IcExclude", () => _doc.Select(_doc.State.Editable().Select(s => s.Id).Where(id => !_doc.Selection.Contains(id))), "Select what isn't selected, and the other way round");

        // ---- Picture: photos and artwork ----
        var picture = _ribbon.Tab("Picture", "Trace, depthmaps and background removal (lights up when a picture is selected)");
        var pin = picture.Group("Add");
        pin.Large("Import picture", "IcImage", () => _ = Guard(ImportImage), "Import a picture onto an Image layer (Ctrl+J)");
        var pwork = picture.Group("Turn it into");
        pwork.Large("Trace", "IcTrace", () => { if (SelectedPicture() is { } im) _ = Guard(() => Trace(im)); }, "Trace the picture into cut or engrave outlines (Ctrl+T)", HasPicture);
        pwork.Large("Depthmap", "IcDepth", () => _ = Guard(() => MakeDepthmap(SelectedPicture())), "Make a relief depthmap with DepthCharge (select a picture first, or pick a file)");
        var pclean = picture.Group("Clean up");
        pclean.Large("Remove background", "IcRemoveBg", () => { if (SelectedPicture() is { } im) _ = Guard(() => RemoveBackground(im)); }, "Make the backdrop see-through so it isn't engraved", HasPicture);

        // ---- Camera: draw from what the machine sees ----
        var camera = _ribbon.Tab("Camera", "Use the machine's camera: photo, live view, pick pieces, fit designs to them");
        var see = camera.Group("See the bed");
        see.Large("Update photo", "IcCamera", () => _ = Guard(_machine.UpdatePhotoAsync), "Take a new photo of the bed and lay it under your design", () => _machine.IsConnected && _machine.HasCalibration);
        see.LargeToggle("Live view", "IcLive", on => _machine.LiveView = on, "Keep the camera picture updating", () => _machine.IsConnected, () => _machine.LiveView);
        see.SmallToggle("Show picture", "IcEye", on => { if (_machine.HasCalibration) _machine.CameraOnWorkArea = on; else _ = Guard(_machine.CalibrateAsync); }, "Show the camera picture under the design", null, () => _machine.CameraOnWorkArea);
        var pick = camera.Group("Draw from the camera");
        ToolLarge(pick, "Pick piece", "IcWand", Tool.Wand, "Click inside a piece on the camera picture to outline it (W)");
        ToolLarge(pick, "Trace edge", "IcLasso", Tool.Magnetic, "Magnetic pen: click along an edge on the camera picture; the line hugs the edge (M)");
        var fit = camera.Group("Place");
        fit.Large("Fit to piece", "IcFitPiece", () => _ = Guard(FitToCamera), "Snap the selected design onto the real piece the camera sees: moves, turns and resizes it to match (Ctrl+Shift+F)", () => SelectedOutline().Count > 0);
        var cal = camera.Group("Set up");
        cal.Large("Calibrate", "IcCalibrate", () => _ = Guard(_machine.CalibrateAsync), "Calibrate the camera so its picture lines up with where the laser marks (once per material height)");

        // ---- Laser: materials and the job ----
        var laser = _ribbon.Tab("Laser", "Materials, tests, framing and sending");
        var mat = laser.Group("Materials");
        mat.Large("Library", "IcMaterial", () => _ = Guard(ShowLibrary), "Your material and cutting library: settings that worked, with photos; share and import");
        mat.Large("Test grid", "IcTestGrid", () => _ = Guard(AddMaterialTest), "Add a power × speed test grid to find the right settings for a new material");
        mat.Large("Record results", "IcRecord", () => _ = Guard(RecordTestResults), "Rate the squares of a burned test grid and save the good ones to the library");
        var job = laser.Group("Job");
        job.Large("Frame", "IcFrame", () => _ = Guard(_job.ToggleFrame), "Trace around the job with a faint dot to see where it lands (click again to stop)", () => _job.CanFrame);
        job.Large("Send", "IcSend", () => _ = Guard(_job.Send), "Upload the job; the machine waits for its start button", () => _job.CanSend);
        job.Small("Export .xf", "IcExport", () => _ = Guard(() => _job.Export(xf: true)), "Save the job package the machine runs");
        job.Small("Export G-code", "IcExport", () => _ = Guard(() => _job.Export(xf: false)), "Save the motion G-code as text");
        job.SmallToggle("Show order", "IcPreview", on => _job.ShowOrder = on, "Show the processing order on the work area", null, () => _job.ShowOrder);
        var head = laser.Group("Head");
        head.Large("Auto-focus", "IcFocus", () => _ = Guard(async () => await _machine.AutoFocusNowAsync()), "Measure the material and move to focus (the head moves)", () => _machine.IsConnected && !_machine.IsBusyFocusing);
        head.Large("Machine", "IcSettings", () => _ = Guard(() => ShowSettings("Machine")), "Machine connection and options");

        // ---- View ----
        var view = _ribbon.Tab("View", "Zoom, grid and snapping");
        var zoom = view.Group("Zoom");
        zoom.Large("Fit bed", "IcFit", Canvas.FitField, "Show the whole work area (Home)");
        zoom.Small("Fit selection", "IcFit", Canvas.FitSelection, "Zoom to the selection, or the design (F)");
        zoom.Small("Zoom in", "IcZoomIn", () => Canvas.ZoomBy(1.25), "Zoom in (Ctrl +)");
        zoom.Small("Zoom out", "IcZoomOut", () => Canvas.ZoomBy(0.8), "Zoom out (Ctrl −)");
        var grid = view.Group("Grid & snapping");
        grid.LargeToggle("Snap to shapes", "IcMagnet", on => { _settings.SnapToObjects = on; SaveAndApplyGrid(); }, "Snap to shapes and the work area: edges, centres and centrelines (hold Ctrl while dragging to move freely)", null, () => _settings.SnapToObjects);
        grid.SmallToggle("Show grid", "IcGrid", on => { _settings.ShowGrid = on; SaveAndApplyGrid(); }, "Show the grid", null, () => _settings.ShowGrid);
        grid.SmallToggle("Snap to grid", "IcGrid", on => { _settings.SnapToGrid = on; SaveAndApplyGrid(); }, "Snap to the grid's minor lines", null, () => _settings.SnapToGrid);
        grid.Small("Grid settings…", "IcSettings", () => _ = Guard(ShowGridSettings), "Grid spacing and colours, snap distance (Ctrl+')");
        var panels = view.Group("Show");
        panels.SmallToggle("Machine controls", "IcPlug", on => { _settings.MachineHudFolded = !on; _settings.Save(); _hud.Refold(); }, "The camera, light and focus controls on the work area", null, () => !_settings.MachineHudFolded);
        panels.SmallToggle("Processing order", "IcPreview", on => _job.ShowOrder = on, "Number the paths in the order they run", null, () => _job.ShowOrder);

        _ribbon.Select(_settings.RibbonTab);
        _ribbon.TabSelected += t => { _settings.RibbonTab = t; _settings.Save(); };
        RefreshRecent();
        _ribbon.Refresh();
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

    /// <summary>The settings window, opened at a page; the machine panel moves into it while it's open.</summary>
    async Task ShowSettings(string page)
    {
        var w = new SettingsWindow(_machine, page,
        [
            ("Workspace",
            [
                new("Grid & snapping…", "Grid spacing and colours, what snaps and how strongly.", ShowGridSettings),
                new("Shape library…", "Built-in shapes and the ones you saved.", () => { ShowShapes(); return Task.CompletedTask; }),
            ]),
            ("Materials",
            [
                new("Material library…", "Settings that worked for each material, with photos. Share them as .pcmat files, or import other people's.", ShowLibrary),
                new("Material test grid…", "Add a power × speed grid to the design to dial in a new material.", AddMaterialTest),
            ]),
            ("Connection",
            [
                new("Connection keys…", "The one-time setup that lets this program talk to the machine.", async () => await _machine.SetUpKeys()),
                new("Calibrate camera…", "Line the camera picture up with where the laser marks (once per material height).", _machine.CalibrateAsync),
            ]),
            ("Help",
            [
                new("Getting started", "A short tour, and the connection setup.", ShowGettingStarted),
                new("Keyboard shortcuts", "Every shortcut on one page.", ShowShortcuts),
                new("Safety and disclaimer", "Please read before running jobs.", ShowSafety),
                new($"About {AppIdentity.ProductName}", "Version, licence, credits.", ShowAbout),
            ]),
        ]);
        await w.ShowDialog(this);
        _ribbon.Refresh();
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
            (false, false, Key.W) => () => Canvas.Tool = Tool.Wand,
            (false, false, Key.I) => ShowShapes,
            (false, false, Key.M) => () => Canvas.Tool = Tool.Magnetic,
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

    // ---- right-click menus ----------------------------------------------------

    /// <summary>
    /// Common tasks for a set of shapes. Each action selects <paramref name="ids"/> first, so the
    /// same menu works for a right-clicked shape, the selection, or everything on a layer.
    /// </summary>
    List<Control> ShapeActions(IReadOnlySet<Guid> ids)
    {
        var shapes = _doc.State.Shapes.Where(s => ids.Contains(s.Id)).ToList();
        int n = shapes.Count;
        var paths = shapes.OfType<PathShape>().ToList();
        bool anyGrouped = shapes.Any(s => s.GroupId is not null);
        bool anyText = shapes.Any(s => s is TextShape);
        bool compound = paths.Any(p => p.Geometry.Subpaths.Length > 1);
        var picture = shapes.OfType<ImageShape>().FirstOrDefault();
        string what = n == 1 ? "it" : $"these {n}";

        MenuItem Do(string header, Action action, bool enabled = true, string? gesture = null)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled && n > 0 };
            if (gesture is not null) m.InputGesture = KeyGesture.Parse(gesture);
            m.Click += (_, _) => { _doc.Select(ids); action(); };
            return m;
        }
        MenuItem DoAsync(string header, Func<Task> action, bool enabled = true, string? gesture = null) =>
            Do(header, () => _ = Guard(action), enabled, gesture);
        MenuItem Sub(string header, params Control[] items)
        {
            var m = new MenuItem { Header = header, IsEnabled = n > 0 };
            foreach (var i in items) m.Items.Add(i);
            return m;
        }

        var toLayer = Sub("Move to layer");
        foreach (var layer in _doc.State.Layers)
        {
            var target = layer;
            toLayer.Items.Add(Do(target.Name, () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, ids, target.Id)),
                shapes.Any(s => s.LayerId != target.Id)));
        }

        return
        [
            Do("Cut", CutSel, gesture: "Ctrl+X"),
            Do("Copy", CopySel, gesture: "Ctrl+C"),
            new MenuItem { Header = "Paste", IsEnabled = _clipboard.Count > 0, InputGesture = KeyGesture.Parse("Ctrl+V"), Command = null }.With(m => m.Click += (_, _) => Paste()),
            Do("Duplicate", DuplicateSel, gesture: "Ctrl+D"),
            Do($"Delete {what}", DeleteSel, gesture: "Delete"),
            new Separator(),
            Do("Group", GroupSel, n > 1, "Ctrl+G"),
            Do("Ungroup", UngroupSel, anyGrouped, "Ctrl+Shift+G"),
            Do("Combine into one shape (compound)", Combine, paths.Count > 1, "Ctrl+K"),
            Do("Break apart", BreakApart, compound, "Ctrl+Shift+K"),
            Do("Convert text to paths", ConvertToPaths, anyText, "Ctrl+Shift+C"),
            Sub("Shape operations",
                DoAsync("Offset…", OffsetSel, paths.Count > 0, "Ctrl+Shift+O"),
                new Separator(),
                Do("Weld (union)", () => BooleanSel(BoolOp.Union), paths.Count > 1),
                Do("Subtract (bottom minus the rest)", () => BooleanSel(BoolOp.Subtract), paths.Count > 1),
                Do("Intersect (keep the overlap)", () => BooleanSel(BoolOp.Intersect), paths.Count > 1),
                Do("Exclude (remove the overlap)", () => BooleanSel(BoolOp.Exclude), paths.Count > 1)),
            Sub("Array",
                DoAsync("Nest on material…", NestSel),
                DoAsync("Grid array…", () => ArraySel(circular: false)),
                DoAsync("Circular array…", () => ArraySel(circular: true))),
            Sub("Align and distribute",
                Do("Align left", () => AlignSel(Align.Left), n > 1),
                Do("Align centres horizontally", () => AlignSel(Align.CenterX), n > 1),
                Do("Align right", () => AlignSel(Align.Right), n > 1),
                Do("Align top", () => AlignSel(Align.Top), n > 1),
                Do("Align centres vertically", () => AlignSel(Align.CenterY), n > 1),
                Do("Align bottom", () => AlignSel(Align.Bottom), n > 1),
                new Separator(),
                Do("Distribute horizontally", () => DistributeSel(true), n > 2),
                Do("Distribute vertically", () => DistributeSel(false), n > 2),
                new Separator(),
                Do("Centre in work area", CenterSelInField)),
            Sub("Flip and rotate",
                Do("Flip horizontally", () => FlipSel(true)),
                Do("Flip vertically", () => FlipSel(false)),
                Do("Rotate 90° clockwise", () => RotateSel(90)),
                Do("Rotate 90° counter-clockwise", () => RotateSel(-90))),
            Sub("Order",
                Do("Bring to front", () => _doc.Commit("Bring to front", Ops.BringToFront(_doc.State, _doc.Selection))),
                Do("Send to back", () => _doc.Commit("Send to back", Ops.SendToBack(_doc.State, _doc.Selection)))),
            toLayer,
            new Separator(),
            DoAsync("Fit to piece in camera…", FitToCamera, paths.Count > 0, "Ctrl+Shift+F"),
            DoAsync("Trace picture…", () => picture is not null ? Trace(picture) : Task.CompletedTask, picture is not null, "Ctrl+T"),
            DoAsync("Depthmap with DepthCharge…", () => MakeDepthmap(picture), picture is not null),
            DoAsync("Remove picture background…", () => picture is not null ? RemoveBackground(picture) : Task.CompletedTask, picture is not null),
            Do("Zoom to " + what, Canvas.FitSelection, gesture: "F"),
            DoAsync("Rename…", RenameSel, n == 1),
            Do("Save to My shapes…", ShowShapes, paths.Count > 0),
        ];
    }

    readonly DepthChargeService _depthCharge = new();

    async Task RemoveBackground(ImageShape im)
    {
        var w = new BackgroundWindow(im.Source.Pixels, result =>
        {
            var replaced = new ImageShape
            {
                Id = im.Id, Name = im.Name, LayerId = im.LayerId, GroupId = im.GroupId, Transform = im.Transform,
                SizeMm = im.SizeMm, Tone = im.Tone, Source = ImageSource.FromEncoded(result.ToPng()),
            };
            _doc.Commit("Remove background", Ops.Replace(_doc.State, replaced), [im.Id]);
        });
        await w.ShowDialog(this);
    }
    readonly Doc.Materials.MaterialLibrary _library;

    async Task ShowLibrary()
    {
        var w = new MaterialLibraryWindow(_library, _settings, (m, r) => _layerEditor.ApplyRecipe(m, r));
        w.LibraryChanged += _layerEditor.RefreshMaterials;
        await w.ShowDialog(this);
        _layerEditor.RefreshMaterials();
    }

    async Task SaveToLibrary(Layer layer)
    {
        // A photo of this layer's work, if the camera picture covers it.
        byte[]? photo = null;
        var b = _doc.State.Bounds(_doc.State.ShapesOn(layer.Id).Select(s => s.Id).ToHashSet());
        if (Canvas.Photo is { } p && !b.IsEmpty)
            photo = Doc.Materials.MaterialLibrary.CropPhoto(p.Image, p.Area, p.MmPerPx, b.Inflate(2));
        var w = new SaveRecipeWindow(_library, layer, _machine.Distance, photo);
        await w.ShowDialog(this);
        if (w.Result is { } saved)
        {
            _doc.Commit("Material", Ops.UpdateLayers(_doc.State, new HashSet<Guid> { layer.Id },
                l => l with { Material = new Doc.Materials.MaterialRef(saved.Material.Id, saved.Material.Label, saved.Recipe.Id, saved.Recipe.Title) }));
            _layerEditor.RefreshMaterials();
        }
    }

    async Task RecordTestResults()
    {
        var w = new RecordTestWindow(_doc.State, _library, Canvas.Photo, _machine.UpdatePhotoAsync, () => Canvas.Photo, _machine.Distance);
        await w.ShowDialog(this);
        _layerEditor.RefreshMaterials();
    }

    /// <summary>
    /// Depthmap of a picture (or a picture file) via DepthCharge. The result goes over the
    /// original, same place and size, on its own greyscale Image layer.
    /// </summary>
    async Task MakeDepthmap(ImageShape? picture)
    {
        byte[] bytes;
        string name;
        if (picture is not null)
        {
            bytes = picture.Source.Encoded;
            name = string.IsNullOrWhiteSpace(picture.Name) ? "Picture" : picture.Name;
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Picture to turn into a depthmap",
                FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = [.. PictureExtensions.Select(e => "*" + e)] }],
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            bytes = await File.ReadAllBytesAsync(path);
            name = Path.GetFileNameWithoutExtension(path);
        }
        var w = new DepthChargeWindow(_depthCharge, _settings, bytes, name, (png, label) => AddDepthmap(png, label, picture));
        await w.ShowDialog(this);
    }

    void AddDepthmap(byte[] png, string label, ImageShape? over)
    {
        var src = ImageSource.FromEncoded(png);
        var state = _doc.State;
        var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with
        {
            Name = $"Relief · {label}",
            ImageMode = ImageEngraveMode.Grayscale, // darker = deeper: power follows the depth
        };
        state = Ops.AddLayer(state, layer, 0);
        ImageShape shape;
        if (over is not null)
        {
            // Same place and size as the original; a cropped depthmap fits inside it, centred.
            double s = Math.Min(over.SizeMm.X / src.Width, over.SizeMm.Y / src.Height);
            var size = new Vec2(src.Width * s, src.Height * s);
            var inset = (over.SizeMm - size) / 2;
            shape = new ImageShape { Name = label, LayerId = layer.Id, Source = src, SizeMm = size, Transform = Affine.Translate(inset).Then(over.Transform) };
        }
        else
        {
            var size = ImageShape.DefaultSize(src, 100);
            var at = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH).Center - size / 2;
            shape = new ImageShape { Name = label, LayerId = layer.Id, Source = src, SizeMm = size, Transform = Affine.Translate(at) };
        }
        _doc.Commit("Add depthmap", Ops.AddShapes(state, [shape]), [shape.Id]);
        _layers.Activate(layer.Id);
    }

    ShapeLibraryWindow? _shapesWindow;
    string ShapesFolder => Path.Combine(AppIdentity.DataDir, "shapes");

    /// <summary>The shape library (one window, brought forward if already open).</summary>
    void ShowShapes()
    {
        if (_shapesWindow is { } open) { open.Activate(); return; }
        var w = new ShapeLibraryWindow(ShapesFolder, InsertLibraryShape,
            () => _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).OfType<PathShape>().ToList());
        w.Closed += (_, _) => _shapesWindow = null;
        _shapesWindow = w;
        w.Show(this);
    }

    /// <summary>Add a library shape in the middle of the work area (40 mm across unless it has a real size), on the drawing layer.</summary>
    void InsertLibraryShape(LibraryShape item)
    {
        var layer = _doc.State.FindLayer(_doc.ActiveLayer) is { Visible: true, Locked: false } l && l.Mode != LayerMode.Image ? l
            : _doc.State.Layers.FirstOrDefault(x => x.Visible && !x.Locked && x.Mode != LayerMode.Image);
        if (layer is null) return;
        var g = item.Geometry;
        if (!item.RealSize) g = ShapeLibrary.Fit(g, 40);
        var b = g.Bounds(0.01);
        var field = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH);
        // Stagger repeated adds so they don't stack exactly.
        double nudge = 6 * (_insertCount++ % 6);
        var shape = new PathShape { Name = item.Name, LayerId = layer.Id, Geometry = g, Transform = Affine.Translate(field.Center - b.Center + new Vec2(nudge, nudge)) };
        _doc.Commit($"Add {item.Name}", Ops.AddShapes(_doc.State, [shape]), [shape.Id]);
    }

    int _insertCount;

    async Task NestSel()
    {
        if (_doc.Selection.Count == 0) return;
        await new NestWindow(_doc, _doc.Selection).ShowDialog(this);
    }

    void CenterSelInField()
    {
        var b = _doc.State.Bounds(_doc.Selection);
        if (b.IsEmpty) return;
        var m = MachineProfile.F2Ultra;
        _doc.Commit("Centre in work area", Ops.Move(_doc.State, _doc.Selection, m.FieldW / 2 - b.Center.X, m.FieldH / 2 - b.Center.Y));
    }

    async Task RenameSel()
    {
        if (_doc.Selection.Count != 1 || _doc.State.FindShape(_doc.Selection.First()) is not { } s) return;
        if (await Dialogs.Prompt(this, "Rename", "Name:", s.Name) is { } name && name.Trim() != s.Name)
            _doc.Commit("Rename shape", Ops.Replace(_doc.State, s.With(name: name.Trim())));
    }

    /// <summary>Right-click on the canvas: tasks for what was clicked (or the selection), or general ones on empty space.</summary>
    void OpenCanvasMenu()
    {
        var menu = new ContextMenu();
        if (_doc.Selection.Count > 0)
            foreach (var c in ShapeActions(_doc.Selection)) menu.Items.Add(c);
        else
        {
            var paste = new MenuItem { Header = "Paste", IsEnabled = _clipboard.Count > 0, InputGesture = KeyGesture.Parse("Ctrl+V") };
            paste.Click += (_, _) => Paste();
            var all = new MenuItem { Header = "Select all", InputGesture = KeyGesture.Parse("Ctrl+A") };
            all.Click += (_, _) => SelectAll();
            var fit = new MenuItem { Header = "Fit work area", InputGesture = KeyGesture.Parse("Home") };
            fit.Click += (_, _) => Canvas.FitField();
            var grid = new MenuItem { Header = "Grid & snapping…" };
            grid.Click += (_, _) => _ = Guard(ShowGridSettings);
            menu.Items.Add(paste);
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
            menu.Items.Add(fit);
            menu.Items.Add(grid);
        }
        menu.Open(Canvas);
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
            _depthCharge.Dispose(); // stops DepthCharge only if this program started it
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

    /// <summary>Outlines (world mm) of the selected vector shapes; pictures don't count.</summary>
    List<IReadOnlyList<Vec2>> SelectedOutline() =>
        _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id) && s is not ImageShape).OfType<PathShape>()
            .SelectMany(s => s.World.Flatten(0.05))
            .Where(f => f.Points.Count > 1)
            .Select(f => (IReadOnlyList<Vec2>)(f.Closed ? [.. f.Points, f.Points[0]] : f.Points))
            .ToList();

    async Task FitToCamera()
    {
        var outline = SelectedOutline();
        if (outline.Count == 0)
        {
            await Dialogs.Message(this, "Fit to piece in camera", "Select the design (text or shapes) you want to snap onto the piece first.");
            return;
        }
        if (!_machine.IsConnected)
        {
            await Dialogs.Message(this, "Fit to piece in camera", "Connect to the machine first: the fit needs a fresh camera photo.");
            return;
        }
        var ids = _doc.Selection;
        var w = new FitToCameraWindow(_machine, outline, fit =>
        {
            var move = Affine.ScaleAbout(fit.Center, fit.Scale, fit.Scale)
                .Then(Affine.RotateAbout(fit.Center, fit.AngleDeg))
                .Then(Affine.Translate(fit.Offset));
            _doc.Commit("Fit to camera", Ops.Transform(_doc.State, ids, move), ids);
        });
        await w.ShowDialog(this);
    }

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
