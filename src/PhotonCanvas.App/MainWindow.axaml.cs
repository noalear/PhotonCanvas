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
    readonly Docking _docking;
    readonly MachineHud _hud;
    readonly Ribbon _ribbon = new();
    CloseUpView? _closeUp;

    /// <summary>The close-up camera on the work area: open or close it.</summary>
    void ToggleCloseUp(bool open)
    {
        if (!open) { _closeUp?.Close(); return; }
        if (_closeUp is null)
        {
            _closeUp = new CloseUpView(_machine);
            _closeUp.Closed += _ribbon.Refresh;
            _closeUp.PlacedOnBed += (img, area) =>
            {
                if (img is null) { Canvas.CloseUpImage = null; return; }
                using var ms = new MemoryStream(img.EncodePng());
                Canvas.CloseUpImage = (new Avalonia.Media.Imaging.Bitmap(ms), area);
            };
            CanvasOverlay.Content = _closeUp;
        }
        _ = Guard(_closeUp.OpenAsync);
        _ribbon.Refresh();
    }

    bool CloseUpOpen => _closeUp?.IsVisible == true;
    List<Shape> _clipboard = [];
    int _pasteCount;
    bool _closeConfirmed;

    /// <summary>A project to open once the window is shown (from the command line).</summary>
    public string? OpenPathOnStartup { get; set; }

    /// <summary>Connect to the machine when the window opens (off for screenshots and tests).</summary>
    public bool ConnectOnOpen { get; init; } = true;

    static readonly FilePickerFileType ProjectType = new("PhotonCanvas project") { Patterns = ["*.pcanvas", "*.ytool"] };
    static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];
    // Every drawing format the importer reads (PostScript and .hpg included), so none is left out of the file window.
    static readonly FilePickerFileType VectorTypes = new("Drawings (SVG, DXF, PDF, AI, EPS, xTool, LightBurn, HPGL)") { Patterns = [.. Importer.Extensions.Select(e => "*" + e)] };
    static readonly FilePickerFileType AnyImportable = new("Everything that can be imported")
    {
        Patterns = [.. Importer.Extensions.Concat(PictureExtensions).Select(e => "*" + e)],
    };

    public MainWindow()
    {
        InitializeComponent();
        Canvas.Doc = _doc;
        _layers = new LayersPanel(_doc);
        _layers.ShapeHovered += id => Canvas.HighlightShape = id;
        _layers.ShapeActions = ShapeActions;
        _layers.Moved += (n, layer) => StatusText.Text = $"{(n == 1 ? "1 shape" : $"{n} shapes")} moved to \"{layer}\". Ctrl+Z puts {(n == 1 ? "it" : "them")} back.";
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
        _machine = new MachinePanel(_settings) { OwnerWindow = this };
        if (_settings.ActiveSetup().Kind == MachineKind.XTool && !WorkModes.Usable(_settings)) _settings.WorkMode = "Flat";
        // The machine in use (and its work mode) sets the work area.
        Canvas.Machine = WorkModes.Area(_settings);
        _job = new JobBar(_doc, _settings, Canvas, _machine);
        _job.JobBuilt += j => _layers.SetTimes(j?.LayerTimes.ToDictionary(t => t.LayerId, t => t.TotalSeconds) ?? new());
        _job.LayerClicked += id => _layers.Activate(id);
        _hud = new MachineHud(_machine, _settings);
        _hud.SettingsRequested += () => _ = Guard(() => ShowSettings("Machine"));
        _hud.CalibrateRequested += () => _ = Guard(_machine.CalibrateAsync);
        _hud.CloseUpRequested += () => ToggleCloseUp(!CloseUpOpen);
        JobHost.Content = _job;
        PagesHost.Content = new PagesBar(_doc, this);
        // The side panels: movable between the sides, floating or folded (View → Panels).
        HookHelpKey();
        _docking = new Docking(this, _settings, MainGrid, LeftDock, RightDock, LeftSplit, RightSplit);
        _docking.Add("layers", "Layers", _layers, new PanelPlace { Side = "Left", Order = 0 });
        _docking.Add("selection", "Selection", _props, new PanelPlace { Side = "Right", Order = 0 });
        _docking.Add("layer", "Layer settings", new ScrollViewer { Content = new ContentControl { Content = _layerEditor, Margin = new(0, 0, 4, 10) } }, new PanelPlace { Side = "Right", Order = 1 });
        _docking.Layout();
        _ribbon.SetBodyTrail(_hud);
        RibbonHost.Content = _ribbon;

        BuildRibbon();
        WireTools();
        ApplyGrid();
        Canvas.CursorMoved += p => CursorText.Text = p is { } v ? $"X {Units.Num(v.X)}   Y {Units.Num(v.Y)} {Units.Name}" : "";
        Canvas.ZoomChanged += z => ZoomText.Text = $"{z / 3.7795 * 100:0}%";
        _doc.Changed += _ => UpdateChrome();
        _doc.Changed += _ => FollowSelectionLayer();
        // While a test read from the close-up camera is in the design, show where that camera sees.
        void ShowCloseUpArea() => Canvas.CloseUpArea =
            ToneTestWindow.InDesign(_doc.State) || LineTestWindow.InDesign(_doc.State) ? _machine.CloseUpSees() : null;
        _doc.Changed += _ => ShowCloseUpArea();
        _machine.StateChanged += ShowCloseUpArea;
        _machine.PhotoChanged += p => Canvas.Photo = p;
        _machine.StateChanged += _ribbon.Refresh;
        // Quick processing: the lid closing starts focus, photo, placing and upload.
        _machine.DeviceEventReceived += e => { if (e.Module == "GAP" && e.Type == "CLOSE") _ = Guard(QuickProcess); };
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
                var welcome = new WelcomeWindow(firstRun: true, _machine.SetUpKeys, () => _machine.HasKeys, _settings);
                await welcome.ShowDialog(this);
                if (!welcome.Accepted)
                {
                    _closeConfirmed = true;
                    Close();
                    return;
                }
                _settings.AcceptedTermsVersion = WelcomeWindow.TermsVersion;
                _settings.EquipmentAsked = true;
                _settings.Save();
                ApplyEquipment();
            }
            if (ConnectOnOpen && !_settings.EquipmentAsked)
            {
                // Once: what attachments there are, so features that need them can stay hidden.
                await new EquipmentWindow(_settings).ShowDialog(this);
                _settings.EquipmentAsked = true;
                _settings.Save();
                ApplyEquipment();
            }
            bool recovered = ConnectOnOpen && await OfferRecovery();
            if (ConnectOnOpen) _ = _machine.AutoConnectAsync();
            // Started with a file after unsaved work was brought back: save the copy, drop it, or keep it open instead.
            if (OpenPathOnStartup is { } p && (!recovered || await ConfirmDiscard())) await OpenPath(p);
            else if (ConnectOnOpen && !recovered) await ReopenLast();
            _autosave.Tick += (_, _) => Autosave();
            if (ConnectOnOpen) _autosave.Start();
            // A newer version published? (Quiet when offline; off in Settings → Updates.)
            if (ConnectOnOpen) _ = Guard(() => UpdateNotice.CheckAtStart(this, _settings));
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
            ? "Draw with the Home tab, or drag a file in (SVG, DXF, PDF, pictures). New material? Laser → Test grid."
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
                Tool.Nodes => "Drag points and handles. Double-click an edge to add a point; Delete removes the selected one; S smooth or sharp; Y symmetric; R rounds the corner; B breaks the path there. Alt-drag a handle to break a smooth point. Esc to finish.",
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
        Canvas.CodeEditRequested += id => _ = Guard(() => EditCode(id));
        _props.CodeEditRequested += id => _ = Guard(() => EditCode(id));
        _props.VariablesRequested += () => _ = Guard(ShowVariables);
        _props.FontsRequested += () => _ = Guard(ShowFonts);
        _job.RunFinished += from =>
        {
            // An etch test just finished: straight to its results.
            if (_doc.State.Layers.Any(l => EtchTest.PassesOf(l) is not null)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => ShowEtchTest(true)));
            // A cut test (or its kerf test) likewise.
            else if (CutTestWindow.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => ShowCutTest(true)));
            else if (ToneTestWindow.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => ShowToneTest(true)));
            else if (LineTestWindow.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => ShowLineTest(true)));
            else if (CloseUpCalibrationWindow.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => ShowCloseUpCalibration(true)));
            else if (Bevel.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => new BevelTestWindow(_doc, _library, _settings, Canvas.Machine, true).ShowDialog(this)));
            else if (LaserAlignWindow.InDesign(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(() => new LaserAlignWindow(_doc, _machine, _settings, true).ShowDialog(this)));
            // A depth round burned: measure it next.
            if (ExactDepthWindow.Waiting(_doc.State)) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Guard(ShowExactDepth));
            // The next serial number and spreadsheet row, and the next turn for layers that turn between runs.
            if (from != _doc.FilePath) return;
            var next = VariableText.AfterRun(_doc.State, DateTime.Now);
            if (ReferenceEquals(next, _doc.State)) return;
            _doc.Commit("Next run: serial number and row", next);
            if (VariableText.Describe(next) is { } marks) StatusText.Text = $"Ready for the next run: {marks}. (Ctrl+Z goes back.)";
        };
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
        Canvas.PicturesAsBurned = _settings.PicturesAsBurned;
        Canvas.SnapToObjects = _settings.SnapToObjects;
        Canvas.SnapToGrid = _settings.SnapToGrid;
        Canvas.SnapDistancePx = _settings.SnapDistancePx;
        Canvas.ObjectSnapOn = _settings.ObjectSnap;
        Canvas.ObjectSnaps = (Doc.Geometry.SnapKinds)_settings.ObjectSnapKinds;
        Canvas.Tracking = _settings.SnapTracking;
        _ribbon.Refresh();
    }

    void ToggleObjectSnaps()
    {
        _settings.ObjectSnap = !_settings.ObjectSnap;
        SaveAndApplyGrid();
        StatusText.Text = _settings.ObjectSnap ? "Object snaps on." : "Object snaps off (F3 turns them back on).";
    }

    /// <summary>The object snap kinds and tracking, ticked on and off (they apply at once).</summary>
    MenuFlyout SnapKindsMenu()
    {
        var menu = new MenuFlyout();
        void Opened()
        {
            menu.Items.Clear();
            foreach (var (kind, name, about) in GridWindow.SnapKindNames)
            {
                var item = new MenuItem { Header = $"{name}: {about}", ToggleType = MenuItemToggleType.CheckBox, IsChecked = (_settings.ObjectSnapKinds & (int)kind) != 0 };
                item.Click += (_, _) =>
                {
                    _settings.ObjectSnapKinds ^= (int)kind;
                    SaveAndApplyGrid();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var track = new MenuItem { Header = "Tracking: pause on a snap point to line up with it", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.SnapTracking };
            track.Click += (_, _) => { _settings.SnapTracking = !_settings.SnapTracking; SaveAndApplyGrid(); };
            menu.Items.Add(track);
        }
        menu.Opening += (_, _) => Opened();
        Opened();
        return menu;
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
        // Clicking the tool that's already on leaves it on, and its button pressed.
        void Use(Tool t) { Canvas.Tool = t; foreach (var (tb, tool) in _toolButtons) tb.IsChecked = tool == t; Canvas.Focus(); }
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
            Content = "File", Background = UiKit.Brush("Bg3"), BorderBrush = UiKit.Brush("Line"), BorderThickness = new(1),
            FontWeight = Avalonia.Media.FontWeight.SemiBold, Padding = new(14, 3), CornerRadius = new(5),
        };
        file.Flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Items =
            {
                AsyncItem("New", NewDoc, "Ctrl+N"),
                AsyncItem("New from template…", NewFromTemplate),
                AsyncItem("Open…", OpenDoc, "Ctrl+O"),
                _recentMenu,
                new Separator(),
                AsyncItem("Save", () => Save(false), "Ctrl+S"),
                AsyncItem("Save as…", () => Save(true), "Ctrl+Shift+S"),
                AsyncItem("Save as template…", SaveAsTemplate),
                AsyncItem("Earlier versions…", ShowVersions),
                new Separator(),
                AsyncItem("Import…", () => ImportAny(byColour: false), "Ctrl+I"),
                AsyncItem("Import, one layer per colour…", () => ImportAny(byColour: true), "Ctrl+Shift+I"),
                AsyncItem("Import picture…", ImportImage, "Ctrl+J"),
                new Separator(),
                AsyncItem("Export job package (.xf)…", () => _job.Export(xf: true)),
                AsyncItem("Export G-code (.gcode)…", () => _job.Export(xf: false)),
                AsyncItem("Export design as SVG…", () => ExportDesign("svg")),
                AsyncItem("Export design as DXF…", () => ExportDesign("dxf")),
                AsyncItem("Export design as PNG…", () => ExportDesign("png")),
                new Separator(),
                AsyncItem("Settings…", () => ShowSettings("Machine")),
                AsyncItem("Keyboard shortcuts…", ShowHotkeys),
                AsyncItem("Fonts…", ShowFonts),
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
                Item("Help guide", () => ShowHelp(), "F1"),
                Item("Tutorials", ShowTutorials),
                new Separator(),
                AsyncItem("Getting started", ShowGettingStarted),
                AsyncItem("Keyboard shortcuts", ShowShortcuts),
                AsyncItem("Safety and disclaimer", ShowSafety),
                new Separator(),
                AsyncItem($"About {AppIdentity.ProductName}", ShowAbout),
            },
        };
        _ribbon.SetTrail(save, _undoButton, _redoButton, new Border { Width = 1, Background = UiKit.Brush("Line"), Margin = new(6, 6) }, gear, help);
        foreach (var (name, c) in new (string, Control)[] { ("File", file), ("Save", save), ("Undo", _undoButton), ("Redo", _redoButton), ("Settings", gear), ("Help", help) })
            _headerButtons[name] = c;
        _ribbon.Track(_undoButton, () => _doc.CanUndo);
        _ribbon.Track(_redoButton, () => _doc.CanRedo);

        // ---- Home: everyday drawing and editing ----
        var home = _ribbon.Tab("Home", "Draw, select, combine and edit");
        var clip = home.Group("Clipboard");
        // Always on: what another program copied (a picture, SVG) can be pasted too.
        clip.Large("Paste", "IcPaste", Paste, "Paste (Ctrl+V): shapes copied here, or a picture or SVG copied in another program");
        clip.Small("Cut", "IcCut", CutSel, "Cut (Ctrl+X)", HasSel);
        clip.Small("Copy", "IcCopy", CopySel, "Copy (Ctrl+C)", HasSel);
        clip.Small("Duplicate", "IcDuplicate", DuplicateSel, "Duplicate, offset a little (Ctrl+D)", HasSel);
        var select = home.Group("Select");
        ToolLarge(select, "Select", "IcCursor", Tool.Select, "Select and move (V). Drag left→right to select inside, right→left to select touching.");
        ToolSmall(select, "Edit points", "IcNodes", Tool.Nodes, "Edit a shape's points and curves (N, or double-click a shape)");
        ToolSmall(select, "Pan", "IcHand", Tool.Pan, "Pan the view (H, or hold Space / middle mouse)");
        select.Small("Delete", "IcTrash", DeleteSel, "Delete (Del)", HasSel);
        select.Small("All", "IcCursor", SelectAll, "Select everything (Ctrl+A)");
        select.Small("Active layer", "IcLayers", SelectActiveLayer, "Select everything on the active layer you can pick (Ctrl+Shift+A)");
        var selectBy = select.Small("Select by…", "IcCircles", () => { }, "Select open lines, shapes the same size, shapes inside another, circles by size…");
        selectBy.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("Circles by size…", () => _ = Guard(() => new CirclesWindow(_doc).ShowDialog(this))),
                Item("Open lines (not closed)", SelectOpen),
                Item("The same size as the selection", SelectSameSize),
                Item("Everything inside the selected shape", SelectInside),
                Item("On the same layers as the selection", () => { var ls = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).Select(s => s.LayerId).ToHashSet(); _doc.Select(_doc.State.Editable().Where(s => ls.Contains(s.LayerId)).Select(s => s.Id)); }),
            },
        };
        select.Small("Invert", "IcExclude", () => _doc.Select(_doc.State.Editable().Select(s => s.Id).Where(id => !_doc.Selection.Contains(id))), "Select what isn't selected, and the other way round");
        var draw = home.Group("Draw");
        ToolLarge(draw, "Text", "IcText", Tool.Text, "Text (T) · click where the text should go");
        ToolLarge(draw, "Pen", "IcPen", Tool.Pen, "Pen (P) · click = corner · drag = curve · click the first point to close · Enter to finish");
        ToolSmall(draw, "Ellipse", "IcEllipse", Tool.Ellipse, "Ellipse (E) · Shift = circle · Alt = from centre");
        ToolSmall(draw, "Rectangle", "IcRect", Tool.Rect, "Rectangle (R) · Shift = square · Alt = from centre");
        ToolSmall(draw, "Polygon", "IcPolygon", Tool.Polygon, "Polygon (O) · Shift = even · Alt = from centre");
        ToolSmall(draw, "Star", "IcStar", Tool.Star, "Star (S) · Shift = even · Alt = from centre");
        ToolSmall(draw, "Line", "IcLine", Tool.Line, "Line (L) · Shift = 45° steps");
        draw.Large("Generators", "IcTestGrid", () => _ = Guard(() => new GeneratorsWindow(_doc, Canvas.Machine).ShowDialog(this)), "Finger-jointed boxes, keychains and tags, living hinges, earrings, and the selection in several sizes");
        draw.Large("Special Shapes", "IcHeart", ShowShapes, "Shape library (I): hearts, stars, badges, frames, living hinges and your own saved shapes");
        ToolSmall(draw, "Pick piece", "IcWand", Tool.Wand, "Click inside a piece on the camera picture to outline it (W)");
        ToolSmall(draw, "Trace edge", "IcLasso", Tool.Magnetic, "Magnetic pen: click along an edge on the camera picture; the line hugs the edge (M)");
        var codes = home.Group("Serial & codes");
        codes.Large("Code", "IcCode", () => _ = Guard(() => EditCode(null)), "QR code, Data Matrix or barcode. It can hold a serial number, the date or a spreadsheet column, new on every run");
        codes.Small("Variables", "IcVariables", () => _ = Guard(ShowVariables), "Serial number and spreadsheet (CSV) that fill in {fields} in text and codes, moving on after each run");
        codes.Small("Number copies", "IcArrayGrid", NumberCopies, "Selected copies (groups or single shapes) each mark the next serial number or spreadsheet row, back to front and left to right; select one to set it back", HasSel);
        var grp = home.Group("Group");
        grp.Large("Group", "IcGroup", GroupSel, "Group: select and move them as one (Ctrl+G)", Many);
        grp.Small("Ungroup", "IcUngroup", UngroupSel, "Ungroup (Ctrl+Shift+G)", () => _doc.State.Shapes.Any(x => _doc.Selection.Contains(x.Id) && x.GroupId is not null));
        grp.Small("Combine", "IcCombine", Combine, "Combine into one compound shape: overlapping parts make holes (Ctrl+K)", Many);
        grp.Small("Break apart", "IcBreak", BreakApart, "Break a compound shape into its pieces (Ctrl+Shift+K)", () => SelectedPaths().Any(p => p.Geometry.Subpaths.Length > 1));
        var boolean = home.Group("Shape builder");
        boolean.Large("Weld", "IcWeld", () => BooleanSel(BoolOp.Union), "Weld: merge overlapping shapes into one outline (Ctrl+Shift+U)", TwoPaths);
        boolean.Small("Subtract", "IcSubtract", () => BooleanSel(BoolOp.Subtract), "Subtract: the bottom shape minus the ones above it (Ctrl+Shift+M)", TwoPaths);
        boolean.Small("Intersect", "IcIntersect", () => BooleanSel(BoolOp.Intersect), "Intersect: keep only where they overlap", TwoPaths);
        boolean.Small("Exclude", "IcExclude", () => BooleanSel(BoolOp.Exclude), "Exclude: remove where they overlap", TwoPaths);
        boolean.Small("Mask with shape", "IcIntersect", () => _ = Guard(MaskSel), "Cut the selected shapes to the inside (or outside) of one of them: lines past its edge and the parts of filled shapes outside it are removed", TwoPaths);
        var outline = home.Group("Outline");
        outline.Large("Offset", "IcOffset", () => _ = Guard(OffsetSel), "Offset: a new outline a set distance outside or inside (Ctrl+Shift+O)", () => SelectedPaths().Count > 0);
        outline.Small("Round corners", "IcRound", () => _ = Guard(RoundCornersSel), "Round the sharp corners of the selected shapes", () => SelectedPaths().Count > 0);
        outline.Small("Text on path", "IcTextPath", TextOnPath, "Select a text and a line or shape: the words follow it (select the text alone to straighten it again)", () => SelectedPaths().Any(p => p is TextShape));
        ToolSmall(outline, "Trim", "IcTrim", Tool.Trim, "Trim: click a piece of a line between where it crosses others to cut it away");
        outline.Small("Simplify", "IcNodes", () => _ = Guard(SimplifySel), "Fewer points: imported and traced shapes followed closely with far fewer lines and curves", () => SelectedPaths().Any(p => p is not (TextShape or CodeShape)));
        outline.Small("Cut shapes", "IcBreak", CutShapesSel, "Cut overlapping shapes into separate pieces where they cross", TwoPaths);
        ToolSmall(outline, "Scissors", "IcCut", Tool.Scissors, "Scissors: click a line or outline to cut it there");
        ToolSmall(outline, "Place tabs", "IcBreak", Tool.Tabs, "Click outlines to put tabs exactly where you want them (Shift+click removes one); used on Cut layers");
        ToolSmall(outline, "Start point", "IcPen", Tool.Start, "Click an outline where its cut should start (Shift+click clears it)");
        var moreOutline = outline.Small("More…", "IcMore", () => { }, "Extend lines, remove overlapping lines, tangent circles, shadows");
        moreOutline.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("Extend lines to meet the next shape", ExtendSel),
                Item("Remove overlapping lines", RemoveOverlapsSel),
                Item("Tangent circles (select three circles)", TangentCirclesSel),
                AsyncItem("Stencil bridges…", StencilSel),
                AsyncItem("Drop shadow…", () => ShadowSel(false)),
                AsyncItem("3D shadow…", () => ShadowSel(true)),
                Item("Clear all guides", () => { if (_doc.State.Guides.Count > 0) _doc.Commit("Clear guides", _doc.State with { Guides = [] }); }),
            },
        };
        outline.Small("Tidy lines", "IcTidy", () => _ = Guard(TidyPaths), "Tidy up imported drawings: join ends that nearly meet, close nearly-closed shapes, delete lines lying on top of each other");
        outline.Small("Text to paths", "IcToPaths", ConvertToPaths, "Turn text into ordinary shapes (Ctrl+Shift+C)", () => _doc.State.Shapes.Any(s => _doc.Selection.Contains(s.Id) && s is TextShape));
        var import = home.Group("Bring in");
        import.Large("Import", "IcImport", () => _ = Guard(() => ImportAny(false)), "Import drawings and pictures: SVG, DXF, PDF, AI, xTool projects, PNG, JPG… (Ctrl+I)");
        import.Small("Picture", "IcImage", () => _ = Guard(ImportImage), "Import a picture onto an Image layer (Ctrl+J)");
        import.Small("By colour", "IcLayers", () => _ = Guard(() => ImportAny(true)), "Import a drawing with one layer per colour (Ctrl+Shift+I)");
        var importMore = import.Small("Import as…", "IcMore", () => { }, "Import a drawing grouped, in separate pieces, or as one picture");
        import.Small("3D to slices", "IcDepth", () => _ = Guard(() => new StackSliceWindow(_doc, Canvas.Machine, _settings, _library).ShowDialog(this)), "Slice a 3D model (STL, OBJ, STEP) into layers of your material's thickness: cut them, numbered, and glue them up into the model");
        importMore.Flyout = new MenuFlyout
        {
            Items =
            {
                AsyncItem("Import, each file grouped…", () => ImportAny(false, ImportHow.Grouped)),
                AsyncItem("Import with every piece separate…", () => ImportAny(false, ImportHow.Pieces)),
                AsyncItem("Import as one picture…", () => ImportAny(false, ImportHow.Picture)),
            },
        };

        // ---- Arrange: position, align, order, repeat ----
        var arrange = _ribbon.Tab("Arrange", "Align, distribute, flip, rotate, order and repeat");
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
        var repeat = arrange.Group("Repeat");
        repeat.Large("Nest", "IcNest", () => _ = Guard(NestSel), "Pack copies or parts onto the work area or a piece, as tightly as they fit (Ctrl+Alt+N)", HasSel);
        repeat.Small("Grid array", "IcArrayGrid", () => _ = Guard(() => ArraySel(circular: false)), "Copies in rows and columns", HasSel);
        repeat.Small("Circular array", "IcArrayCircle", () => _ = Guard(() => ArraySel(circular: true)), "Copies around a centre", HasSel);
        repeat.Small("Along a path", "IcAlongPath", () => _ = Guard(CopyAlongSel), "Copies spaced along a path: select what to copy and the path (the longest selected line is the path)", Many);
        var shapeIt = arrange.Group("Transform");
        shapeIt.Large("Warp", "IcWarp", () => _ = Guard(() => new WarpWindow(_doc).ShowDialog(this)), "Warp the selection: perspective taper, arch, bulge or wave", () => SelectedPaths().Count > 0);
        ToolSmall(shapeIt, "Align 2 points", "IcAlign2", Tool.Align2, "Move and turn the selection so two of its points land on two other points (Shift on the last click also resizes)");
        shapeIt.Small("Lock", "IcLock", () => LockSel(true), "Lock the selected shapes: they stay put and can't be picked until unlocked (still marked)", HasSel);
        shapeIt.Small("Unlock all", "IcUnlock", () => LockSel(false), "Unlock every locked shape", () => _doc.State.Shapes.Any(s => s.Locked));

        // ---- Picture: photos and artwork ----
        var picture = _ribbon.Tab("Picture", "Trace, depthmaps and background removal (lights up when a picture is selected)");
        var pin = picture.Group("Add");
        pin.Large("Import picture", "IcImage", () => _ = Guard(ImportImage), "Import a picture onto an Image layer (Ctrl+J)");
        var pwork = picture.Group("Turn it into");
        pwork.Large("Trace", "IcTrace", () => { if (SelectedPicture() is { } im) _ = Guard(() => Trace(im)); }, "Trace the picture into cut or engrave outlines (Ctrl+T)", HasPicture);
        pwork.Large("Engraving", "IcLine", () => { if (SelectedPicture() is { } im) _ = Guard(() => new EngravingWindow(_doc, im).ShowDialog(this)); }, "The picture as engraving lines, banknote style: lines that swell in the shadows (straight, following the forms, rings or a spiral)", HasPicture);
        _ribbon.ShowWhen(pwork.Large("Colour on metal", "IcPalette", () => { if (SelectedPicture() is { } im) _ = Guard(() => MakeColourPicture(im)); }, "The picture in the colours your plate really made (a measured palette): one layer per colour, MOPA on stainless or titanium", HasPicture), () => _settings.ShowColourOnMetal);
        _ribbon.ShowWhen(pwork.Large("Diffraction", "IcPrism", () => { if (SelectedPicture() is { } im) _ = Guard(() => MakeDiffraction(im)); }, "Turn the picture into tiny diffraction gratings (MOPA on polished stainless): it shimmers with rainbow colours as it tilts", HasPicture), () => _settings.ShowDiffraction);
        pwork.Large("Depthmap", "IcDepth", () => _ = Guard(() => MakeDepthmap(SelectedPicture())), "Make a relief depthmap with DepthCharge (select a picture first, or pick a file)");
        var pclip = picture.Group("Shape");
        pclip.Large("Image mask", "IcIntersect", ClipPicture, "Select a picture and a closed shape: the shape masks the picture, so only the part inside it is shown and engraved", () => SelectedPicture() is not null || _doc.State.Shapes.Any(s => _doc.Selection.Contains(s.Id) && s is ImageShape));
        pclip.Small("Remove mask", "IcExclude", () => { if (SelectedPicture() is { Clip: not null } im) _doc.Commit("Remove image mask", Ops.Replace(_doc.State, im.WithClip(null)), [im.Id]); }, "Take the image mask off: the whole picture is shown and engraved again", () => SelectedPicture() is { Clip: not null });
        var pedit = picture.Group("Edit");
        pedit.Large("Filters", "IcPalette", () => { if (SelectedPicture() is { } im) _ = Guard(() => FilterPicture(im)); }, "Greyscale, saturation, pencil sketch, emboss, comic, black and white, mosaic", HasPicture);
        ToolSmall(pedit, "Eraser", "IcExclude", Tool.Eraser, "Rub out parts of a picture by dragging over them (they turn white and aren't engraved)");
        pedit.Small("Crop", "IcFrame", () => { if (SelectedPicture() is { } im) _ = Guard(() => CropPicture(im)); }, "Cut the picture down to a part of it (free, square, 4:3…)", HasPicture);
        pedit.Small("CMYK split", "IcLayers", () => { if (SelectedPicture() is { } im) SplitCmyk(im); }, "Four pictures, one per ink (cyan, magenta, yellow, black), each on its own layer: for screen printing", HasPicture);
        pedit.Small("Centre-line trace", "IcPen", () => { if (SelectedPicture() is { } im) _ = Guard(() => CentrelineTrace(im)); }, "One line down the middle of each stroke (handwriting, line art), on a Score layer", HasPicture);
        var prelief = picture.Group("Relief");
        prelief.Large("3D model", "IcDepth", () => _ = Guard(AddStl), "A 3D model (STL) as a depth map, on a depth-slices layer");
        prelief.Small("Depth outlines", "IcTrace", () => { if (SelectedPicture() is { } im) _ = Guard(() => new DepthOutlinesWindow(_doc, im).ShowDialog(this)); }, "Find the edges in a depthmap and mark them as lines over the relief (one laser line, a set width, or swelling with the edge), so it looks less washed out", HasPicture);
        prelief.Small("Relief preview", "IcEye", () => { if (SelectedPicture() is { } im) new ReliefPreviewWindow(im).Show(this); }, "See a depth map as the lit relief it will carve", HasPicture);
        pclip.Small("Shapes to picture", "IcImage", () => _ = Guard(ShapesToPicture), "Turn the selected shapes into a picture (filled shapes and lines), e.g. to engrave them with a picture style", () => SelectedPaths().Count > 0);
        var pclean = picture.Group("Clean up");
        pclean.Large("Remove background", "IcRemoveBg", () => { if (SelectedPicture() is { } im) _ = Guard(() => RemoveBackground(im)); }, "Make the backdrop see-through so it isn't engraved", HasPicture);

        // ---- Camera: what the machine sees ----
        var camera = _ribbon.Tab("Camera", "Use the machine's camera: photo, live view, fit designs to pieces, calibration");
        var see = camera.Group("See the bed");
        see.Large("Update photo", "IcCamera", () => _ = Guard(_machine.UpdatePhotoAsync), "Take a new photo of the bed and lay it under your design", () => _machine.IsConnected && _machine.HasCalibration);
        see.LargeToggle("Live view", "IcLive", on => _machine.LiveView = on, "Keep the camera picture updating", () => _machine.IsConnected, () => _machine.LiveView);
        see.LargeToggle("Close-up", "IcZoomIn", ToggleCloseUp, "The close-up camera in the laser head, in a corner of the work area: live or one photo at a time", null, () => CloseUpOpen);
        see.SmallToggle("Show picture", "IcEye", on => { if (_machine.HasCalibration) _machine.CameraOnWorkArea = on; else _ = Guard(async () => { await _machine.CalibrateAsync(); _ribbon.Refresh(); }); }, "Show the camera picture under the design", null, () => _machine.CameraOnWorkArea);
        var fit = camera.Group("Place");
        fit.Large("Fit to piece", "IcFitPiece", () => _ = Guard(FitToCamera), "Snap the selected design onto the real piece the camera sees: moves, turns and resizes it to match (Ctrl+Shift+F)", () => SelectedOutline().Count > 0);
        fit.Large("Cut round prints", "IcCut", () => _ = Guard(CutRoundPrints), "Print-then-cut: the camera finds printed art on paper (stickers, labels) and a cut goes round each one, a margin outside. Select a shape first to look only inside it");
        fit.Large("Line up pieces", "IcFitPiece", ShowLineUp, "For a design split onto pieces: the camera keeps looking and moves the page's design onto its piece and the pieces laid beside it, so they line up. No clicking while you place them");
        fit.Large("Every piece", "IcGrid", () => _ = Guard(PlaceOnEveryPiece), "Photograph the bed, find each piece on it and put a copy of the selected design on every one (with the next serial number or spreadsheet row on each)", () => SelectedOutline().Count > 0);
        var cal = camera.Group("Set up");
        cal.Large("Calibrate", "IcCalibrate", () => _ = Guard(_machine.CalibrateAsync), "Calibrate the camera so its picture lines up with where the laser marks (once per material height)");
        cal.Small("Focus on selection", "IcFocus", () => _ = Guard(FocusByPointer), "Aimed focus: the red pointer is aimed at the middle of the selection (or of the bed), the close-up camera reads the height there from where its dot lands, and the head moves to focus on it. Nothing touches the material; needs the pointer calibrated (Curved surface)");
        cal.Small("Thickness", "IcMeasure", () => _ = Guard(MeasureThickness), "Measure the material's thickness: auto-focus on it and compare with the empty bed (measured once)");
        cal.Small("Align MOPA", "IcCalibrate", () => _ = Guard(() => new LaserAlignWindow(_doc, _machine, _settings, LaserAlignWindow.InDesign(_doc.State)).ShowDialog(this)), "Line the MOPA (fiber) laser up with the blue one: both burn a grid of dots, the close-up camera measures how far apart they land, and MOPA work is moved to match");
        cal.Large("Close-up position", "IcZoomIn", () => _ = Guard(() => ShowCloseUpCalibration(CloseUpCalibrationWindow.InDesign(_doc.State))), "Teach PhotonCanvas exactly where the close-up camera looks (ten small dots, once per focus height): tone and line tests are then read from sharp close-up photos");

        // ---- Laser: materials and the job ----
        var laser = _ribbon.Tab("Laser", "Materials, tests, framing and shipping the job");
        var mat = laser.Group("Materials");
        mat.Large("Library", "IcMaterial", () => _ = Guard(ShowLibrary), "Your material and cutting library: settings that worked, with photos; share and import");
        mat.Large("Test grid", "IcTestGrid", () => _ = Guard(AddMaterialTest), "Add a test grid: power × speed, focus, line spacing or any two settings, to find what works on a material");
        mat.Large("Record results", "IcRecord", () => _ = Guard(RecordTestResults), "Rate the squares of a burned test grid and save the good ones to the library");
        mat.Large("Etch rate", "IcDepth", () => _ = Guard(() => ShowEtchTest(_doc.State.Layers.Any(l => EtchTest.PassesOf(l) is not null))), "How deep one pass goes: circles burned 1, 2, 3 … passes until one goes right through; saved with the material");
        mat.Large("Cut test", "IcTestGrid", () => _ = Guard(() => ShowCutTest(CutTestWindow.InDesign(_doc.State))), "Find the cut: small squares at different speeds and passes, see which drop out; then the kerf and the slot fit at that setting; saved with the material");
        mat.Large("Exact depth", "IcDepth", () => _ = Guard(ShowExactDepth), "Engrave the selected shapes to an exact depth: the red pointer measures the surface, then the depth after each round of passes, and works out the next round");
        mat.Large("Tone test", "IcPalette", () => _ = Guard(() => ShowToneTest(ToneTestWindow.InDesign(_doc.State))), "Photos that come out as they look: grey patches burned with a picture layer's settings, read by the camera, and that layer's pictures corrected for the material");
        mat.Large("Line test", "IcLine", () => _ = Guard(() => ShowLineTest(LineTestWindow.InDesign(_doc.State))), "Hide the rows pictures are burned in: the same grey patch burned with a picture layer's settings and with one change each (spread, direction, closer lines), side by side");
        mat.Small("Inlay", "IcLayers", () => _ = Guard(() => new InlayWindow(_doc, _library, SelectedPaths()).ShowDialog(this)), "The selected shapes as an inlay: a pocket engraved as deep as the veneer (from your etch test) and the piece cut to fit (with your cut test's kerf)", () => SelectedPaths().Count > 0);
        mat.Small("Bevel test", "IcMeasure", () => _ = Guard(() => new BevelTestWindow(_doc, _library, _settings, Canvas.Machine, Bevel.InDesign(_doc.State)).ShowDialog(this)), "How much the galvo's beam slants away from the middle of the field: four bars cut through a board and measured with calipers. Needed for bevelled edges");
        mat.Small("Graphene test", "IcBolt", () => _ = Guard(() => new GrapheneTestWindow(_doc, _library, Canvas.Machine, GrapheneTestWindow.InDesign(_doc.State)).ShowDialog(this)), "Experimental: laser-induced graphene (conductive carbon burned into cork or wood). Strips at different power and speed, measured with a multimeter");
        mat.SmallToggle("Quick on lid close", "IcBolt", on => { _settings.QuickProcessing = on; _settings.Save(); }, "Quick processing: closing the lid focuses on the material, photographs the bed, centres the design on the piece and loads the job. The machine's button still starts it", null, () => _settings.QuickProcessing);
        var colour = laser.Group("Colour on metal", () => _settings.ShowColourOnMetal);
        colour.Large("Colour test", "IcPalette", () => _ = Guard(AddColourTest), "Add a MOPA colour test grid: one setting across, another down, to see which colours your plate really gives");
        colour.Large("Measure colours", "IcEyedropper", () => _ = Guard(MeasureColours), "Read the colours of a burned colour test from a scan or photo, and save them as a palette");
        var job = laser.Group("Job");
        job.Large("Frame", "IcFrame", () => _ = Guard(_job.ToggleFrame), "Trace around the job with a faint dot to see where it lands (click again to stop)", () => _job.CanFrame);
        job.Large("Ship It", "IcSend", () => _ = Guard(() => _job.Send()), "Upload the job; the machine waits for its start button", () => _job.CanSend);
        job.Small("Export .xf", "IcExport", () => _ = Guard(() => _job.Export(xf: true)), "Save the job package the machine runs");
        job.Small("Export G-code", "IcExport", () => _ = Guard(() => _job.Export(xf: false)), "Save the motion G-code as text");
        job.Small("Preview run", "IcPreview", () => { if (_job.CurrentJob is { } bj) new RunPreviewWindow(bj, Canvas).Show(this); }, "Play the job back on the work area, sped up: what's marked when", () => _job.CurrentJob is not null);
        job.SmallToggle("Show order", "IcPreview", on => _job.ShowOrder = on, "Show the processing order on the work area", null, () => _job.ShowOrder);
        job.SmallToggle("Time-lapse", "IcCamera", on => { _settings.TimeLapse = on; _settings.Save(); }, $"Take a camera photo every {_settings.TimeLapseSeconds:0} s while a job runs and make a video of it (needs ffmpeg for the video; otherwise the photos are kept). Saved in Pictures.", null, () => _settings.TimeLapse);
        var work = laser.Group("Work area");
        _ribbon.ShowWhen(work.Large("Work mode", "IcRotary", () => _ = Guard(ShowWorkMode), "Flat bed, rotary attachment or conveyor (experimental), and their settings"), () => _settings.OwnsAttachment);
        work.Large("Curved surface", "IcDepth", () => _ = Guard(() => new CurvedSurfaceWindow(_doc, _machine).ShowDialog(this)), "Mark on a curved piece: measure it with the pointer, or type in a cylinder or slatted drum; the head follows it in height steps (experimental)");
        work.Small("Layer by layer", "IcLayers", () => new LayerByLayerWindow(_doc, _job).Show(this), "Mark one layer at a time with a pause to change the screen, colour or material between them (screen printing)");
        work.Small("Sections", "IcSections", () => _ = Guard(ShowSections), "Mark a design bigger than the work area in sections, moving the material between them", () => _settings.WorkMode == "Flat");
        work.Small("Split onto pieces", "IcBreak", () => _ = Guard(SplitOntoPieces), "Mark a design across several pieces that go together (four boards making one picture): select the design and the pieces' material outlines; each piece gets a page with its share, and Line up pieces fits it to the pieces on the bed", HasSel);
        var machine = laser.Group("Machine");
        _ribbon.ShowWhen(machine.Small("Console", "IcConsole", ShowConsole, "G-code console and macros: send commands straight to the machine (advanced)"), () => _settings.ShowConsole);
        machine.Large("Machine settings", "IcSettings", () => _ = Guard(() => ShowSettings("Machine")), "Machine connection, camera calibration, lights, options and its event log");
        machine.Large("Remote access", "IcPhone", () => new RemoteAccessWindow().Show(this), "Watch and run the laser from your phone: start the remote server, connect the phone with a QR code, choose the designs it can load");

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
        grid.LargeToggle("Object snaps", "IcMagnet", on => { _settings.ObjectSnap = on; SaveAndApplyGrid(); }, "Points you pick land on other shapes' ends, middles, centres, quadrants and crossings, and on the perpendicular or tangent from where a line starts, as in AutoCAD; a marker shows which (F3 turns them off and on; hold Ctrl for a free point)", null, () => _settings.ObjectSnap);
        var kinds = grid.Small("Snap points…", "IcSettings", () => { }, "Which object snaps are on, and tracking");
        kinds.Flyout = SnapKindsMenu();
        grid.Small("Grid settings…", "IcSettings", () => _ = Guard(ShowGridSettings), "Grid spacing and colours, snap distance (Ctrl+')");
        var tools = view.Group("Measure & history");
        ToolLarge(tools, "Measure", "IcMeasure", Tool.Measure, "Measure: drag between two points (snaps to corners; Shift = 45° steps)");
        tools.Large("History", "IcHistory", ShowHistory, "Every step you can undo: click one to go back to it");
        var panels = view.Group("Show");
        panels.Small("Reset panels", "IcLayers", () => _docking.Reset(), "Put the layers, selection and layer settings panels back where they started (each panel's title bar moves, floats or folds it)");
        panels.SmallToggle("Processing order", "IcPreview", on => _job.ShowOrder = on, "Colour the paths by when they run: blue first, through green and yellow, to red last (a key shows at the top of the work area)", null, () => _job.ShowOrder);
        panels.SmallToggle("Pictures as burned", "IcPalette", on => { _settings.PicturesAsBurned = on; _settings.Save(); Canvas.PicturesAsBurned = on; },
            "Show pictures as they'll come out on their layer's material: corrected, in the material's own colours. Needs a tone test on that material (Laser → Tone test).", null, () => _settings.PicturesAsBurned);

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
        var equipment = new EquipmentPanel(_settings);
        equipment.Changed += ApplyEquipment;
        var machines = new MachinesPanel(_settings);
        machines.Changed += ApplyWorkMode;
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
            ("Tools",
            [
                new("Home the Z axis", "Send the head up to find its zero again (if the focus height seems off).", HomeZ),
                new("Job history…", "Every job run from here: when, which design, how long.", () => new JobHistoryWindow().ShowDialog(this)),
                new("G-code console…", "Send commands straight to the machine, and keep macros (advanced).", () => { ShowConsole(); return Task.CompletedTask; }),
                new("Back up settings…", "Save your settings, material library, shapes and templates in one file.", BackupSettings),
                new("Restore settings…", "Bring back a backup (replaces the current settings, library, shapes and templates).", RestoreSettings),
            ]),
            ("Help",
            [
                new("Help guide", "Every feature and setting explained, with pictures. F1 in any window opens its page.", () => { ShowHelp(); return Task.CompletedTask; }),
                new("Tutorials", "Short guided walks through each feature: a card points at each button as you go.", () => { ShowTutorials(); return Task.CompletedTask; }),
                new("Getting started", "A short tour, and the connection setup.", ShowGettingStarted),
                new("Keyboard shortcuts", "Every shortcut on one page.", ShowShortcuts),
                new("Safety and disclaimer", "Please read before running jobs.", ShowSafety),
                new($"About {AppIdentity.ProductName}", "Version, licence, credits.", ShowAbout),
            ]),
        ], [("Machines", machines), ("Equipment", equipment), ("Appearance", new AppearancePanel(_settings, () => { _props.Refresh(); Canvas.InvalidateVisual(); })), ("Updates", UpdateNotice.Panel(_settings, () => this))]);
        await w.ShowDialog(this);
        _ribbon.Refresh();
    }

    // ---- keyboard ------------------------------------------------------------

    bool TextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox or NumericUpDown || (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<NumericUpDown>() is not null;

    List<HotCommand>? _commands;

    /// <summary>Everything that has (or can be given) a keyboard shortcut. Changed ones are in the settings.</summary>
    IReadOnlyList<HotCommand> Commands() => _commands ??=
    [
        new("file.new", "File", "New design", "Ctrl+N", () => _ = Guard(NewDoc), true),
        new("file.open", "File", "Open…", "Ctrl+O", () => _ = Guard(OpenDoc), true),
        new("file.save", "File", "Save", "Ctrl+S", () => _ = Guard(() => Save(false)), true),
        new("file.saveas", "File", "Save as…", "Ctrl+Shift+S", () => _ = Guard(() => Save(true)), true),
        new("file.import", "File", "Import…", "Ctrl+I", () => _ = Guard(() => ImportAny(false)), true),
        new("file.importcolour", "File", "Import, one layer per colour…", "Ctrl+Shift+I", () => _ = Guard(() => ImportAny(true)), true),
        new("file.picture", "File", "Import picture…", "Ctrl+J", () => _ = Guard(ImportImage), true),
        new("help.guide", "File", "Help guide", "F1", () => ShowHelp(), true),
        new("help.tutorials", "File", "Tutorials", "", ShowTutorials, true),
        new("help.keys", "File", "Keyboard shortcuts", "", () => _ = Guard(ShowShortcuts), true),
        new("edit.undo", "Edit", "Undo", "Ctrl+Z", _doc.Undo),
        new("edit.redo", "Edit", "Redo", "Ctrl+Y|Ctrl+Shift+Z", _doc.Redo),
        new("edit.history", "Edit", "Undo history", "Ctrl+H", ShowHistory),
        new("edit.cut", "Edit", "Cut", "Ctrl+X", CutSel),
        new("edit.copy", "Edit", "Copy", "Ctrl+C", CopySel),
        new("edit.paste", "Edit", "Paste", "Ctrl+V", Paste),
        new("edit.duplicate", "Edit", "Duplicate", "Ctrl+D", DuplicateSel),
        new("edit.delete", "Edit", "Delete", "Delete|Back|Shift+Delete|Shift+Back", DeleteSel),
        new("select.all", "Select", "Select all", "Ctrl+A", SelectAll),
        new("select.layer", "Select", "Select the active layer", "Ctrl+Shift+A", SelectActiveLayer),
        new("select.circles", "Select", "Select circles…", "", () => _ = Guard(() => new CirclesWindow(_doc).ShowDialog(this))),
        new("shape.group", "Shapes", "Group", "Ctrl+G", GroupSel),
        new("shape.ungroup", "Shapes", "Ungroup", "Ctrl+Shift+G", UngroupSel),
        new("shape.combine", "Shapes", "Combine", "Ctrl+K", Combine),
        new("shape.break", "Shapes", "Break apart", "Ctrl+Shift+K", BreakApart),
        new("shape.topaths", "Shapes", "Text to paths", "Ctrl+Shift+C", ConvertToPaths),
        new("shape.weld", "Shapes", "Weld", "Ctrl+Shift+U", () => BooleanSel(BoolOp.Union)),
        new("shape.subtract", "Shapes", "Subtract", "Ctrl+Shift+M", () => BooleanSel(BoolOp.Subtract)),
        new("shape.offset", "Shapes", "Offset…", "Ctrl+Shift+O", () => _ = Guard(OffsetSel)),
        new("shape.round", "Shapes", "Round corners…", "", () => _ = Guard(RoundCornersSel)),
        new("shape.textpath", "Shapes", "Text on path", "", TextOnPath),
        new("shape.tidy", "Shapes", "Tidy lines…", "", () => _ = Guard(TidyPaths)),
        new("shape.along", "Shapes", "Copy along a path…", "", () => _ = Guard(CopyAlongSel)),
        new("shape.warp", "Shapes", "Warp…", "", () => { if (SelectedPaths().Count > 0) _ = Guard(() => new WarpWindow(_doc).ShowDialog(this)); }),
        new("shape.material", "Shapes", "Use as material outline", "", MaterialSel),
        new("shape.lock", "Shapes", "Lock", "Ctrl+Alt+L", () => LockSel(true)),
        new("shape.unlock", "Shapes", "Unlock all", "Ctrl+Alt+Shift+L", () => LockSel(false)),
        new("shape.nest", "Shapes", "Nest…", "Ctrl+Alt+N", () => { if (_doc.Selection.Count > 0) _ = Guard(NestSel); }),
        new("camera.fit", "Shapes", "Fit to piece in camera…", "Ctrl+Shift+F", () => { if (SelectedOutline().Count > 0) _ = Guard(FitToCamera); }),
        new("picture.trace", "Shapes", "Trace picture…", "Ctrl+T", () => { if (SelectedPicture() is { } im) _ = Guard(() => Trace(im)); }),
        new("picture.depthoutlines", "Shapes", "Depth outlines…", "", () => { if (SelectedPicture() is { } im) _ = Guard(() => new DepthOutlinesWindow(_doc, im).ShowDialog(this)); }),
        new("layer.add", "Layers", "New layer", "Ctrl+L", _layers.AddLayer),
        new("layer.moveto", "Layers", "Move selection to the active layer", "Ctrl+M", () => _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.ActiveLayer))),
        new("view.zoomin", "View", "Zoom in", "Ctrl+OemPlus|Ctrl+Add", () => Canvas.ZoomBy(1.25)),
        new("view.zoomout", "View", "Zoom out", "Ctrl+OemMinus|Ctrl+Subtract", () => Canvas.ZoomBy(0.8)),
        new("view.fit", "View", "Fit work area", "Home|Ctrl+D0|Ctrl+NumPad0", Canvas.FitField),
        new("view.fitsel", "View", "Fit selection", "F", Canvas.FitSelection),
        new("view.grid", "View", "Grid settings…", "Ctrl+OemQuotes", () => _ = Guard(ShowGridSettings)),
        new("view.osnap", "View", "Object snaps on or off", "F3", ToggleObjectSnaps),
        new("tool.select", "Tools", "Select", "V", () => Canvas.Tool = Tool.Select),
        new("tool.rect", "Tools", "Rectangle", "R", () => Canvas.Tool = Tool.Rect),
        new("tool.ellipse", "Tools", "Ellipse", "E", () => Canvas.Tool = Tool.Ellipse),
        new("tool.line", "Tools", "Line", "L", () => Canvas.Tool = Tool.Line),
        new("tool.polygon", "Tools", "Polygon", "O", () => Canvas.Tool = Tool.Polygon),
        new("tool.star", "Tools", "Star", "S", () => Canvas.Tool = Tool.Star),
        new("tool.pen", "Tools", "Pen", "P", () => Canvas.Tool = Tool.Pen),
        new("tool.text", "Tools", "Text", "T", () => Canvas.Tool = Tool.Text),
        new("tool.nodes", "Tools", "Edit points", "N", () => { if (_doc.Selection.Count == 1) Canvas.Tool = Tool.Nodes; }),
        new("tool.pan", "Tools", "Pan", "H", () => Canvas.Tool = Tool.Pan),
        new("tool.wand", "Tools", "Pick piece (camera)", "W", () => Canvas.Tool = Tool.Wand),
        new("tool.magnetic", "Tools", "Trace edge (camera)", "M", () => Canvas.Tool = Tool.Magnetic),
        new("tool.shapes", "Tools", "Special shapes", "I", ShowShapes),
        new("tool.measure", "Tools", "Measure", "", () => Canvas.Tool = Tool.Measure),
        new("tool.trim", "Tools", "Trim", "", () => Canvas.Tool = Tool.Trim),
        new("tool.align2", "Tools", "Align by two points", "", () => Canvas.Tool = Tool.Align2),
        new("job.frame", "Job", "Frame", "", () => { if (_job.CanFrame) _ = Guard(_job.ToggleFrame); }),
        new("job.send", "Job", "Ship It", "", () => { if (_job.CanSend) _ = Guard(() => _job.Send()); }),
        new("job.console", "Job", "G-code console", "", ShowConsole),
    ];

    readonly Dictionary<string, KeyGesture[]> _gestures = [];

    KeyGesture[] Gestures(string keys)
    {
        if (_gestures.TryGetValue(keys, out var g)) return g;
        var list = new List<KeyGesture>();
        foreach (var part in keys.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { list.Add(KeyGesture.Parse(part)); }
            catch (ArgumentException) { }
        return _gestures[keys] = [.. list];
    }

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Mid-drag (moving, resizing): commands wait until it's let go.
        if (_doc.InEdit) return;
        var m = e.KeyModifiers;
        bool ctrl = m.HasFlag(KeyModifiers.Control), shift = m.HasFlag(KeyModifiers.Shift);
        bool typing = TextInputFocused();
        // A pen with points down, point editing and the magnetic pen use these keys themselves (R rounds a point, it doesn't pick the rectangle).
        bool toolKeys = !ctrl && ((Canvas.PenInProgress || Canvas.Tool is Tool.Nodes) && e.Key is Key.Delete or Key.Back or Key.Enter or Key.Escape or Key.S
            || Canvas.LineInProgress && e.Key is Key.Delete or Key.Back or Key.Enter or Key.Escape
            || Canvas.Tool is Tool.Nodes && e.Key is Key.R or Key.Y or Key.B
            || Canvas.Tool is Tool.Magnetic && e.Key is Key.Delete or Key.Back or Key.Enter or Key.Escape);
        foreach (var c in Commands())
        {
            // File shortcuts work everywhere; the rest leave text boxes their Ctrl+Z, Ctrl+A, Delete, arrows and letters.
            if (typing && !c.AlsoWhileTyping) continue;
            if (toolKeys && !c.AlsoWhileTyping) continue;
            foreach (var g in Gestures(HotkeysWindow.Keys(c, _settings)))
            {
                if (!g.Matches(e)) continue;
                e.Handled = true;
                c.Run();
                return;
            }
        }
        if (typing || toolKeys) return;
        // Ctrl+1…9: move the selection to layer N.
        if (ctrl && !shift && e.Key is >= Key.D1 and <= Key.D9)
        {
            int idx = e.Key - Key.D1;
            if (idx < _doc.State.Layers.Count && _doc.Selection.Count > 0)
            {
                e.Handled = true;
                _doc.Commit($"Move to {_doc.State.Layers[idx].Name}", Ops.MoveToLayer(_doc.State, _doc.Selection, _doc.State.Layers[idx].Id));
            }
        }
    }

    Task ShowHotkeys() => new HotkeysWindow(Commands(), _settings).ShowDialog(this);

    /// <summary>The equipment or shown features changed: the ribbon and the work mode follow.</summary>
    void ApplyEquipment()
    {
        if (!WorkModes.Usable(_settings))
        {
            // A work mode for an attachment that's no longer ticked: back to the flat bed.
            _settings.WorkMode = "Flat";
            _settings.Save();
            StatusText.Text = "Work mode: flat bed (the attachment it used isn't in your equipment).";
        }
        Canvas.Machine = WorkModes.Area(_settings);
        _job.Refresh();
        _ribbon.Refresh();
    }

    // ---- autosave, versions, export, backup --------------------------------------------

    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMinutes(2) };
    static string AutosaveDir => Path.Combine(AppIdentity.DataDir, "autosave");
    static string AutosaveFile => Path.Combine(AutosaveDir, "unsaved" + DocFile.Extension);
    static string AutosaveFrom => Path.Combine(AutosaveDir, "from.txt");

    /// <summary>Every two minutes, unsaved changes are kept aside, in case the program or the computer stops.</summary>
    void Autosave()
    {
        if (!_doc.Dirty) return;
        try
        {
            Directory.CreateDirectory(AutosaveDir);
            DocFile.Save(_doc.State, AutosaveFile);
            File.WriteAllText(AutosaveFrom, _doc.FilePath ?? "");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void ClearAutosave()
    {
        try
        {
            if (File.Exists(AutosaveFile)) File.Delete(AutosaveFile);
            if (File.Exists(AutosaveFrom)) File.Delete(AutosaveFrom);
        }
        catch (IOException) { }
    }

    /// <summary>The design that was open when the program was last closed, at the same view (when it had been saved and is still there).</summary>
    async Task ReopenLast()
    {
        if (_settings.LastOpenFile is not { } last || !File.Exists(last)) return;
        await OpenPath(last);
        if (_doc.FilePath != last) return;
        if (_settings.LastView is [var l, var t, var r, var b] && r > l && b > t)
            // After layout, so the canvas knows its size.
            Dispatcher.UIThread.Post(() => Canvas.FitRect(new Rect2(l, t, r, b), 0), DispatcherPriority.Background);
        StatusText.Text = $"Reopened {Path.GetFileName(last)}, as it was when {AppIdentity.ProductName} was closed.";
    }

    /// <summary>Keep what's open (the saved design and the view) to reopen next time.</summary>
    void RememberOpen()
    {
        _settings.LastOpenFile = _doc.FilePath;
        var v = Canvas.VisibleArea;
        _settings.LastView = v.IsEmpty ? null : [v.MinX, v.MinY, v.MaxX, v.MaxY];
        _settings.Save();
    }

    /// <summary>A design was left unsaved when the program last stopped: offer it back. True when it was brought back.</summary>
    async Task<bool> OfferRecovery()
    {
        if (!File.Exists(AutosaveFile)) return false;
        var from = File.Exists(AutosaveFrom) ? File.ReadAllText(AutosaveFrom).Trim() : "";
        string name = from.Length > 0 ? Path.GetFileName(from) : "an unsaved design";
        var when = File.GetLastWriteTime(AutosaveFile);
        if (await Dialogs.Confirm(this, "Bring back unsaved work?",
                $"{AppIdentity.ProductName} stopped without saving changes to {name}. A copy from {when:g} was kept.\n\nOpen it? (Save it to keep it.)", "Open the copy", "Discard it"))
        {
            try
            {
                _doc.Reset(DocFile.Load(AutosaveFile), from.Length > 0 ? from : null);
                _doc.MarkDirty();
                _layers.Activate(_doc.ActiveLayer);
                Canvas.FitField();
                StatusText.Text = "Unsaved work brought back: save it to keep it.";
                return true;
            }
            catch (Exception e) when (e is IOException or FormatException) { await Dialogs.Message(this, "Couldn't open the copy", e.Message); }
        }
        ClearAutosave();
        return false;
    }

    async Task ShowVersions()
    {
        if (_doc.FilePath is not { } path) { await Dialogs.Message(this, "Earlier versions", "Save the design first: a copy is kept each time you save over it."); return; }
        var w = new VersionsWindow(path);
        await w.ShowDialog(this);
        if (w.Chosen is not { } v || !await ConfirmDiscard()) return;
        _doc.Reset(DocFile.Load(v), null);
        _layers.Activate(_doc.ActiveLayer);
        StatusText.Text = $"Opened the version from {Path.GetFileNameWithoutExtension(v)} as a new design.";
    }

    async Task ExportDesign(string kind)
    {
        var baseName = _doc.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : "design";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export design as {kind.ToUpperInvariant()}",
            DefaultExtension = kind,
            SuggestedFileName = $"{baseName}.{kind}",
            FileTypeChoices = [new FilePickerFileType(kind.ToUpperInvariant()) { Patterns = [$"*.{kind}"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var state = _doc.State;
        switch (kind)
        {
            case "svg": await File.WriteAllTextAsync(path, Doc.Export.DesignExport.Svg(state)); break;
            case "dxf": await File.WriteAllTextAsync(path, Doc.Export.DesignExport.Dxf(state)); break;
            default: await File.WriteAllBytesAsync(path, await Task.Run(() => Doc.Export.DesignExport.Png(state, 300))); break;
        }
        StatusText.Text = $"Exported {Path.GetFileName(path)}.";
    }

    async Task ShapesToPicture()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var input = await Dialogs.Prompt(this, "Shapes to picture", "Resolution (dpi)", "300");
        if (input is null || !double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dpi) || dpi < 25 || dpi > 2540) return;
        var area = _doc.State.Bounds(paths.Select(p => p.Id));
        var png = await Task.Run(() => Doc.Export.DesignExport.ShapesToPng(paths, area, dpi));
        var src = ImageSource.FromEncoded(png);
        var state = _doc.State;
        var layer = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Image && l.Visible && !l.Locked);
        if (layer is null)
        {
            layer = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = "Pictures" };
            state = Ops.AddLayer(state, layer);
        }
        var pic = new ImageShape { Name = "Shapes as a picture", LayerId = layer.Id, Source = src, SizeMm = new Vec2(area.Width, area.Height), Transform = Affine.Translate(area.MinX, area.MinY) };
        _doc.Commit("Shapes to picture", Ops.AddShapes(state, [pic]), [pic.Id]);
    }

    /// <summary>The same picture shape with a new picture (same place and size, unless given).</summary>
    static ImageShape Repicture(ImageShape im, Core.Imaging.RasterImage img, Vec2? size = null, Affine? transform = null) =>
        im.WithPicture(ImageSource.FromEncoded(img.EncodePng()), size, transform);

    async Task FilterPicture(ImageShape im)
    {
        var w = new PictureFilterWindow(im.Source.Pixels);
        await w.ShowDialog(this);
        if (w.Result is { } r) _doc.Commit("Picture filter", Ops.Replace(_doc.State, Repicture(im, r)), [im.Id]);
    }

    async Task CropPicture(ImageShape im)
    {
        var src = im.Source.Pixels;
        var w = new CropWindow(src);
        await w.ShowDialog(this);
        if (w.Result is not { } c) return;
        var cropped = Core.Imaging.PictureTools.Crop(src, c.X, c.Y, c.W, c.H);
        double kx = im.SizeMm.X / src.Width, ky = im.SizeMm.Y / src.Height;
        var size = new Vec2(c.W * kx, c.H * ky);
        var t = Affine.Translate(c.X * kx, c.Y * ky).Then(im.Transform);
        _doc.Commit("Crop picture", Ops.Replace(_doc.State, Repicture(im, cropped, size, t)), [im.Id]);
    }

    void SplitCmyk(ImageShape im)
    {
        // Separated as the picture looks with its tone settings; each plate keeps its clip and sharpening.
        var plates = Core.Imaging.PictureTools.CmykSplit(im.Source.Pixels, im.Tone);
        var plateTone = Core.Imaging.ToneAdjust.None with { Sharpen = im.Tone.Sharpen, SharpenRadiusMm = im.Tone.SharpenRadiusMm };
        var state = _doc.State;
        var made = new List<Shape>();
        uint[] colours = [0x00A3E0, 0xE0007A, 0xF2C800, 0x222222];
        for (int i = 0; i < plates.Length; i++)
        {
            var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = $"{plates[i].Ink} · {im.Name}", Color = colours[i], Dither = Core.Imaging.DitherMethod.Halftone, HalftoneAngleDeg = new[] { 15.0, 75, 0, 45 }[i] };
            state = Ops.AddLayer(state, layer);
            var plate = new ImageShape { Name = $"{plates[i].Ink} plate", LayerId = layer.Id, Source = ImageSource.FromEncoded(plates[i].Plate.EncodePng()), SizeMm = im.SizeMm, Transform = im.Transform, Clip = im.Clip, Tone = plateTone };
            made.Add(plate);
        }
        _doc.Commit("CMYK split", Ops.AddShapes(state, made), made.Select(m => m.Id));
        StatusText.Text = "Four ink layers added (halftone at the usual screen angles). Mark each on its own screen; Laser → Layer by layer can ship them one layer at a time.";
    }

    async Task CentrelineTrace(ImageShape im)
    {
        var src = im.Source.Pixels;
        var tone = im.Tone;
        var (geometry, n) = await Task.Run(() =>
        {
            var ink = new bool[src.Width * src.Height];
            for (int i = 0; i < ink.Length; i++)
            {
                double lum = (0.2126 * src.Rgba[i * 4] + 0.7152 * src.Rgba[i * 4 + 1] + 0.0722 * src.Rgba[i * 4 + 2]) / 255;
                ink[i] = src.Rgba[i * 4 + 3] > 127 && tone.Apply(lum) < 0.5;
            }
            var lines = Doc.Tracing.Centreline.Trace(ink, src.Width, src.Height);
            return (Doc.Tracing.Centreline.ToGeometry(lines, im.PixelToWorld), lines.Count);
        });
        if (n == 0) { StatusText.Text = "No dark strokes found to trace."; return; }
        var state = _doc.State;
        var layer = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Score && l.Visible && !l.Locked);
        if (layer is null)
        {
            layer = Layer.Default(state.Layers.Count, LayerMode.Score) with { Name = "Centre lines" };
            state = Ops.AddLayer(state, layer);
        }
        var shape = new PathShape { Name = $"Centre lines · {im.Name}", LayerId = layer.Id, Geometry = geometry };
        _doc.Commit("Centre-line trace", Ops.AddShapes(state, [shape]), [shape.Id]);
        StatusText.Text = $"Traced {n} line{(n == 1 ? "" : "s")} down the middle of the strokes.";
    }

    async Task AddStl()
    {
        var w = new StlWindow();
        await w.ShowDialog(this);
        if (w.Result is not { } r) return;
        var state = _doc.State;
        var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with
        {
            Name = $"Relief · {r.Name}", ImageMode = ImageEngraveMode.Slices, Dpi = 254, LinesPerCm = 200, AngleStepDeg = 37,
        };
        state = Ops.AddLayer(state, layer);
        var at = new Vec2(Canvas.Machine.FieldW / 2 - r.SizeMm.X / 2, Canvas.Machine.FieldH / 2 - r.SizeMm.Y / 2);
        var pic = new ImageShape { Name = r.Name, LayerId = layer.Id, Source = ImageSource.FromEncoded(r.Depth.EncodePng()), SizeMm = r.SizeMm, Transform = Affine.Translate(at) };
        _doc.Commit("3D model relief", Ops.AddShapes(state, [pic]), [pic.Id]);
        _layers.Activate(layer.Id);
    }

    async Task HomeZ()
    {
        if (!_machine.IsConnected) { StatusText.Text = "Z homing: connect the machine first."; return; }
        if (!await Dialogs.Confirm(this, "Home the Z axis?", "The head moves all the way up to find its zero, then you set the focus again (or auto-focus). Keep hands clear of the head.", "Home it", "Cancel")) return;
        StatusText.Text = await _machine.HomeZAsync() is { } err ? $"Z homing: {err}" : "Z axis homed. Set the focus height again before marking.";
    }

    static readonly string[] BackupItems = ["app.json", "materials", "shapes", "templates"];

    async Task BackupSettings()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Back up settings",
            DefaultExtension = "pcbackup",
            SuggestedFileName = $"{AppIdentity.ProductName} backup {DateTime.Now:yyyy-MM-dd}.pcbackup",
            FileTypeChoices = [new FilePickerFileType("Settings backup") { Patterns = ["*.pcbackup"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _settings.Save();
        await Task.Run(() =>
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            foreach (var item in BackupItems)
            {
                var full = Path.Combine(AppIdentity.DataDir, item);
                if (File.Exists(full)) System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, full, item);
                else if (Directory.Exists(full))
                    foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, f, Path.GetRelativePath(AppIdentity.DataDir, f).Replace('\\', '/'));
            }
        });
        StatusText.Text = $"Backed up to {Path.GetFileName(path)}.";
    }

    async Task RestoreSettings()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore settings",
            FileTypeFilter = [new FilePickerFileType("Settings backup") { Patterns = ["*.pcbackup"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (!await Dialogs.Confirm(this, "Restore settings?", "This replaces your current settings, material library, shapes and templates with the backup's. Restart the program afterwards.", "Restore", "Cancel")) return;
        await Task.Run(() =>
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var root = Path.GetFullPath(AppIdentity.DataDir);
            foreach (var e in zip.Entries)
            {
                if (!BackupItems.Any(i => e.FullName == i || e.FullName.StartsWith(i + "/", StringComparison.Ordinal))) continue;
                var target = Path.GetFullPath(Path.Combine(root, e.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith('/')) continue; // nothing outside the data folder
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                System.IO.Compression.ZipFileExtensions.ExtractToFile(e, target, overwrite: true);
            }
        });
        // The settings in memory would be written over the restored ones on the way out.
        AppSettings.Frozen = true;
        await Dialogs.Message(this, "Restored", "Settings restored. Close and open the program again to use them (changes made before then aren't kept).");
    }

    async Task ShowWorkMode()
    {
        var w = new WorkModeWindow(_settings, _machine);
        if (!await w.ShowDialog<bool>(this)) return;
        ApplyWorkMode();
        StatusText.Text = $"Work mode: {WorkModes.Describe(_settings)}.";
    }

    /// <summary>The canvas shows the mode's work area; the job is built for it.</summary>
    void ApplyWorkMode()
    {
        Canvas.Machine = WorkModes.Area(_settings);
        Canvas.FitField();
        _job.Refresh();
        _ribbon.Refresh();
    }

    SectionsWindow? _sections;

    Task ShowSections()
    {
        if (_sections is { } open) { open.Activate(); return Task.CompletedTask; }
        _sections = new SectionsWindow(_doc, _job);
        _sections.Closed += (_, _) => _sections = null;
        _sections.Show(this);
        return Task.CompletedTask;
    }

    ConsoleWindow? _console;

    void ShowConsole()
    {
        // (Its ribbon button shows only while Settings → Equipment's console box is ticked; Settings → Tools opens it anyway.)
        if (_console is { } open) { open.Activate(); return; }
        _console = new ConsoleWindow(_machine, _settings);
        _console.Closed += (_, _) => _console = null;
        _console.Show(this);
    }

    string TemplatesFolder => Path.Combine(AppIdentity.DataDir, "templates");

    async Task NewFromTemplate()
    {
        if (!await ConfirmDiscard()) return;
        var w = new TemplatesWindow(TemplatesFolder);
        await w.ShowDialog(this);
        if (w.Chosen is not { } path) return;
        DocState state;
        try { state = DocFile.Load(path); }
        catch (Exception ex) when (ex is IOException or FormatException) { await Dialogs.Message(this, "Can't open the template", ex.Message); return; }
        // A new, untitled design: saving asks for a name, so the template stays as it was.
        _doc.Reset(state, null);
        _layers.Activate(_doc.ActiveLayer);
        Canvas.FitField();
        StatusText.Text = $"New design from the template \"{Path.GetFileNameWithoutExtension(path)}\".";
    }

    async Task SaveAsTemplate()
    {
        var suggested = _doc.FilePath is { } fp ? Path.GetFileNameWithoutExtension(fp) : "My template";
        var name = await Dialogs.Prompt(this, "Save as template", "Template name", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;
        var safe = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        Directory.CreateDirectory(TemplatesFolder);
        var path = Path.Combine(TemplatesFolder, safe + DocFile.Extension);
        if (File.Exists(path) && !await Dialogs.Confirm(this, "Replace template?", $"There's already a template called \"{safe}\". Replace it?", "Replace", "Cancel")) return;
        DocFile.Save(_doc.State, path);
        StatusText.Text = $"Saved as the template \"{safe}\": File → New from template starts a design from it.";
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
            Do(shapes.Any(s => s.Locked) ? "Unlock" : "Lock", () => LockSel(!shapes.Any(s => s.Locked), shapes.Select(s => s.Id).ToHashSet())),
            Do("Use as material outline", MaterialSel, paths.Any(p => p is not TextShape && p.Geometry.Subpaths.Any(sp => sp.Closed))),
            DoAsync("Split onto pieces…", SplitOntoPieces, n > 1),
            Sub("Shape operations",
                DoAsync("Offset…", OffsetSel, paths.Count > 0, "Ctrl+Shift+O"),
                DoAsync("Round corners…", RoundCornersSel, paths.Count > 0),
                Do(anyText && paths.Count == 1 && paths[0] is TextShape { Guide: not null } ? "Straighten text" : "Text on path", TextOnPath, anyText),
                DoAsync("Copy along path…", CopyAlongSel, n > 1),
                DoAsync("Warp…", () => new WarpWindow(_doc).ShowDialog(this), paths.Count > 0),
                DoAsync("Tidy lines…", TidyPaths, paths.Count > 0),
                new Separator(),
                Do("Weld (union)", () => BooleanSel(BoolOp.Union), paths.Count > 1),
                Do("Subtract (bottom minus the rest)", () => BooleanSel(BoolOp.Subtract), paths.Count > 1),
                Do("Intersect (keep the overlap)", () => BooleanSel(BoolOp.Intersect), paths.Count > 1),
                DoAsync("Mask with shape…", MaskSel, paths.Count > 1),
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
                SizeMm = im.SizeMm, Tone = im.Tone, Clip = im.Clip, Source = ImageSource.FromEncoded(result.ToPng()),
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
            // Darker = deeper, carved in slices; each slice's lines turned to keep the floor smooth.
            ImageMode = ImageEngraveMode.Slices,
            Dpi = 254,
            LinesPerCm = 200,
            AngleStepDeg = 37,
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
        await new NestWindow(_doc, _doc.Selection, Canvas.Machine).ShowDialog(this);
    }

    void CenterSelInField()
    {
        var b = _doc.State.Bounds(_doc.Selection);
        if (b.IsEmpty) return;
        var m = Canvas.Machine;
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
        // Tell other programs' pictures from ours when pasting.
        if (Clipboard is { } cb) _ = cb.SetTextAsync(ClipMarker);
    }

    void CutSel()
    {
        CopySel();
        if (_doc.Selection.Count > 0) _doc.Commit("Cut", Ops.Delete(_doc.State, _doc.Selection));
    }

    /// <summary>Marker put on the system clipboard when shapes are copied here, so pasting knows they're ours.</summary>
    const string ClipMarker = "PhotonCanvas shapes";

    /// <summary>Paste: a picture or SVG copied in another program, or shapes copied here.</summary>
    void Paste() => _ = Guard(PasteAsync);

    async Task PasteAsync()
    {
        if (Clipboard is { } cb)
        {
            string? text = null;
            try { text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb); } catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            if (text != ClipMarker)
            {
                if (text is { } t && t.TrimStart().StartsWith('<') && t.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    var tmp = Path.Combine(Path.GetTempPath(), $"pasted-{Guid.NewGuid():N}.svg");
                    await File.WriteAllTextAsync(tmp, t);
                    await ImportPaths([tmp], false);
                    return;
                }
                try
                {
                    if (await Avalonia.Input.Platform.ClipboardExtensions.TryGetBitmapAsync(cb) is { } bmp)
                    {
                        var tmp = Path.Combine(Path.GetTempPath(), $"pasted-{Guid.NewGuid():N}.png");
                        bmp.Save(tmp);
                        // A picture: onto a picture layer, as Import picture does.
                        await ImportPictures([tmp]);
                        return;
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException) { }
            }
        }
        PasteInternal();
    }

    void PasteInternal()
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
        VersionsWindow.Keep(path);
        DocFile.Save(_doc.State, path);
        _doc.MarkSaved(path);
        ClearAutosave();
        _settings.AddRecent(path);
        _settings.Save();
        RefreshRecent();
        return true;
    }

    /// <summary>How a drawing comes in: shape by shape, grouped per file, broken into every piece, or drawn into one picture.</summary>
    enum ImportHow { Shapes, Grouped, Pieces, Picture }

    async Task ImportAny(bool byColour, ImportHow how = ImportHow.Shapes)
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
        if (drawings.Count > 0) await ImportPaths(drawings, byColour, how);
    }

    async Task ImportPaths(IReadOnlyList<string> paths, bool byColour, ImportHow how = ImportHow.Shapes)
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

            if (how == ImportHow.Picture)
            {
                // The drawing as one picture (0.1 mm pixels): closed shapes filled, lines drawn.
                var drawn = r.Paths.Select(ip => new PathShape { LayerId = Guid.Empty, Geometry = ip.Geometry.Transform(move) }).ToList();
                var area = drawn.Aggregate(Rect2.Empty, (acc, d) => acc.Union(d.Bounds));
                if (!area.IsEmpty)
                {
                    area = area.Inflate(0.5);
                    var png = await Task.Run(() => Doc.Export.DesignExport.ShapesToPng(drawn, area, 254));
                    var picLayer0 = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Image && l.Visible && !l.Locked);
                    if (picLayer0 is null)
                    {
                        picLayer0 = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = "Pictures" };
                        state = Ops.AddLayer(state, picLayer0, 0);
                    }
                    var whole = new ImageShape { Name = Path.GetFileNameWithoutExtension(path), LayerId = picLayer0.Id, Source = ImageSource.FromEncoded(png), SizeMm = new Vec2(area.Width, area.Height), Transform = Affine.Translate(area.MinX, area.MinY) };
                    state = Ops.AddShapes(state, [whole]);
                    newIds.Add(whole.Id);
                }
                if (r.Images.Count > 0) warnings.Add($"{Path.GetFileName(path)}: its pictures came in on their own, on top of the drawing.");
            }
            var fileIds = new List<Guid>();
            // xTool projects name their colour layers; keep those names.
            bool named = r.LayerNames.Count > 0;
            foreach (var ip in how == ImportHow.Picture ? [] : r.Paths)
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
                var geometry = ip.Geometry.Transform(move);
                // Every piece separate: each outline its own shape (holes too).
                var pieces = how == ImportHow.Pieces && geometry.Subpaths.Length > 1
                    ? geometry.Subpaths.Select(sp => new Doc.PathGeometry([sp])).ToList()
                    : [geometry];
                foreach (var piece in pieces)
                {
                    var shape = new PathShape { Name = ip.Name, LayerId = layerId, Geometry = piece };
                    state = Ops.AddShapes(state, [shape]);
                    newIds.Add(shape.Id);
                    fileIds.Add(shape.Id);
                }
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
                fileIds.Add(pic.Id);
            }
            if (how == ImportHow.Grouped && fileIds.Count > 1) state = Ops.Group(state, fileIds.ToHashSet()).Item1;
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

    // ---- more shape tools ---------------------------------------------------------------

    /// <summary>A plain path in place of <paramref name="p"/>, with this outline in world mm.</summary>
    static PathShape Plain(PathShape p, Doc.PathGeometry world) =>
        new() { Id = p.Id, Name = p.Name, LayerId = p.LayerId, GroupId = p.GroupId, Geometry = world };

    string _lastRadius = "2";

    async Task RoundCornersSel()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var input = await Dialogs.Prompt(this, "Round corners", "Corner radius (mm). Corners whose edges are too short get the biggest radius that fits.", _lastRadius);
        if (input is null || !double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) || r <= 0) return;
        _lastRadius = input;
        var s = _doc.State;
        int total = 0;
        foreach (var p in paths)
        {
            var (g, n) = PathTools.RoundCorners(p.World, r);
            if (n == 0) continue;
            total += n;
            s = Ops.Replace(s, Plain(p, g));
        }
        if (total == 0) { StatusText.Text = "No sharp corners between straight edges to round (curves are left as they are)."; return; }
        _doc.Commit($"Round corners {r:0.##} mm", s);
        StatusText.Text = $"Rounded {total} corner{(total == 1 ? "" : "s")}." + (paths.Any(p => p is TextShape or CodeShape) ? " Text and codes became plain outlines." : "");
    }

    async Task TidyPaths()
    {
        var targets = (_doc.Selection.Count > 0 ? SelectedPaths() : _doc.State.Editable().OfType<PathShape>().ToList())
            .Where(p => p is not (TextShape or CodeShape)).ToList();
        if (targets.Count == 0) { StatusText.Text = "Nothing to tidy: it works on outlines (not pictures, text or codes)."; return; }
        var input = await Dialogs.Prompt(this, "Tidy lines", $"Join ends closer than (mm). Works on {(_doc.Selection.Count > 0 ? "the selection" : "the whole design")}.", "0.05");
        if (input is null || !double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tol) || tol < 0) return;
        var r = PathTools.Cleanup([.. targets.Select(t => t.World)], tol);
        if (!r.Changed) { StatusText.Text = "Nothing to tidy: no ends to join, nothing nearly closed, no duplicates."; return; }
        var s = _doc.State;
        var gone = new HashSet<Guid>();
        for (int i = 0; i < targets.Count; i++)
        {
            if (r.Geometries[i].IsEmpty) gone.Add(targets[i].Id);
            else s = Ops.Replace(s, Plain(targets[i], r.Geometries[i]));
        }
        if (gone.Count > 0) s = Ops.Delete(s, gone);
        _doc.Commit("Tidy lines", s);
        StatusText.Text = $"Joined {r.Joined}, closed {r.Closed}, removed {r.Duplicates} duplicate{(r.Duplicates == 1 ? "" : "s")} and {r.Tiny} zero-length piece{(r.Tiny == 1 ? "" : "s")}.";
    }

    /// <summary>A text and a path selected: the text follows the path. A text alone on a path: straight again.</summary>
    void TextOnPath()
    {
        var sel = SelectedPaths();
        var text = sel.OfType<TextShape>().FirstOrDefault();
        if (text is null) return;
        var guide = sel.Where(p => p is not (TextShape or CodeShape)).OrderByDescending(p => p.Bounds.Width + p.Bounds.Height).FirstOrDefault();
        if (guide is null)
        {
            if (text.Guide is null) { StatusText.Text = "Select the text and the line or shape it should follow."; return; }
            _doc.Commit("Straighten text", Ops.Replace(_doc.State, text.OnPath(null)), [text.Id]);
            return;
        }
        _doc.Commit("Text on path", Ops.Replace(_doc.State, text.OnPath(guide.World)), [text.Id]);
        StatusText.Text = "The text follows the path. Alignment picks the start, middle or end; the path itself is still there (delete it or turn its layer's output off if it shouldn't be marked).";
    }

    /// <summary>A picture and a closed shape selected: the shape becomes the picture's image mask (the shape stays, as a guide).</summary>
    async Task MaskSel()
    {
        var paths = SelectedPaths();
        if (paths.Count < 2 || !paths.Any(p => p.World.Subpaths.Any(sp => sp.Closed))) { StatusText.Text = "Select the shapes to cut and a closed shape to mask them with."; return; }
        await new MaskShapesWindow(_doc, paths).ShowDialog(this);
    }

    void ClipPicture()
    {
        var sel = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList();
        var pic = sel.OfType<ImageShape>().FirstOrDefault();
        var outline = sel.OfType<PathShape>().FirstOrDefault(p => p.World.Subpaths.Any(sp => sp.Closed));
        if (pic is null || outline is null) { StatusText.Text = "Select a picture and the closed shape to mask it with."; return; }
        _doc.Commit("Image mask", Ops.Replace(_doc.State, pic.WithClip(new Doc.PathGeometry(outline.World.Subpaths.Where(sp => sp.Closed)))), [pic.Id]);
        StatusText.Text = "Image mask: only the part inside the shape is shown and engraved. The shape is still there (delete it or turn its layer's output off if it shouldn't be marked).";
    }

    async Task CopyAlongSel()
    {
        var sel = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList();
        var guide = sel.OfType<PathShape>().Where(p => p is not (TextShape or CodeShape) && !p.World.IsEmpty)
            .OrderByDescending(p => p.World.Flatten(0.05).Sum(f => PathTools.CumulativeLengths(f.Points)[^1])).FirstOrDefault();
        if (guide is null || sel.Count < 2) { StatusText.Text = "Select what to copy and the path to copy it along."; return; }
        await new CopyAlongWindow(_doc, guide).ShowDialog(this);
    }

    void ExtendSel()
    {
        var all = _doc.State.Editable().OfType<PathShape>().Where(p => p is not (TextShape or CodeShape)).ToList();
        var targets = (_doc.Selection.Count > 0 ? SelectedPaths() : all).Where(p => p is not (TextShape or CodeShape) && p.HasOpenSubpaths).ToList();
        if (targets.Count == 0) { StatusText.Text = "Select lines with open ends to extend."; return; }
        // Ends run on to anything in the design, not just the selection.
        var others = all.Where(p => !targets.Contains(p)).Select(p => p.World).ToList();
        var (geoms, n) = EditTools.Extend([.. targets.Select(p => p.World), .. others]);
        if (n == 0) { StatusText.Text = "No open end meets anything if carried on straight."; return; }
        var s = _doc.State;
        for (int i = 0; i < targets.Count; i++) s = Ops.Replace(s, Plain(targets[i], geoms[i]));
        _doc.Commit("Extend lines", s);
        StatusText.Text = $"Extended {n} end{(n == 1 ? "" : "s")} to the next line.";
    }

    void RemoveOverlapsSel()
    {
        var paths = (_doc.Selection.Count > 0 ? SelectedPaths() : _doc.State.Editable().OfType<PathShape>().ToList()).Where(p => p is not (TextShape or CodeShape)).ToList();
        if (paths.Count == 0) { StatusText.Text = "Nothing to check: it works on outlines (not pictures, text or codes)."; return; }
        var (geoms, removed) = EditTools.RemoveOverlaps([.. paths.Select(p => p.World)]);
        if (removed < 1e-3) { StatusText.Text = "No lines lie on top of each other."; return; }
        var s = _doc.State;
        for (int i = 0; i < paths.Count; i++)
            s = geoms[i].IsEmpty ? Ops.Delete(s, new HashSet<Guid> { paths[i].Id }) : Ops.Replace(s, Plain(paths[i], geoms[i]));
        _doc.Commit("Remove overlapping lines", s);
        StatusText.Text = $"Removed {removed:0.#} mm of doubled lines: they're cut once now.";
    }

    void TangentCirclesSel()
    {
        var circles = SelectedPaths().Select(p => PathTools.Circle(p.World)).OfType<(Vec2 Centre, double Diameter)>().ToList();
        if (circles.Count != 3) { StatusText.Text = "Select exactly three circles."; return; }
        var found = EditTools.TangentCircles((circles[0].Centre, circles[0].Diameter / 2), (circles[1].Centre, circles[1].Diameter / 2), (circles[2].Centre, circles[2].Diameter / 2));
        if (found.Count == 0) { StatusText.Text = "No circle touches all three."; return; }
        var layer = SelectedPaths()[0].LayerId;
        var made = found.Select(f => (Shape)ParamShape.Create(ParamKind.Ellipse, 2 * f.Radius, 2 * f.Radius, 0, 0, 0.5, layer, Affine.Translate(f.Centre.X - f.Radius, f.Centre.Y - f.Radius), name: "Tangent circle")).ToList();
        _doc.Commit("Tangent circles", Ops.AddShapes(_doc.State, made), made.Select(m => m.Id));
        StatusText.Text = $"{made.Count} circles touch all three (delete the ones you don't want).";
    }

    async Task ShadowSel(bool solid)
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var input = await Dialogs.Prompt(this, solid ? "3D shadow" : "Drop shadow", "Offset X, Y (mm)", "1.5, 1.5");
        if (input is null) return;
        var parts = input.Split(',', ';', ' ').Where(x => x.Length > 0).Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        if (parts.Length < 2 || parts.Any(double.IsNaN)) return;
        var made = new List<Shape>();
        foreach (var p in paths)
        {
            var g = EditTools.Shadow(p.World, parts[0], parts[1], solid ? 24 : 1);
            if (!g.IsEmpty) made.Add(new PathShape { Name = $"Shadow · {p.Name}", LayerId = p.LayerId, Geometry = g });
        }
        if (made.Count == 0) { StatusText.Text = "Shadows need closed shapes."; return; }
        _doc.Commit(solid ? "3D shadow" : "Drop shadow", Ops.AddShapes(_doc.State, made), made.Select(m => m.Id));
        StatusText.Text = "Shadow added on the same layer: move it to another layer for different settings.";
    }

    void SelectOpen() => _doc.Select(_doc.State.Editable().OfType<PathShape>().Where(p => p.World.Subpaths.Any(sp => !sp.Closed && !sp.Segs.IsEmpty)).Select(p => p.Id));

    void SelectSameSize()
    {
        var sel = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).Select(s => s.Bounds).ToList();
        if (sel.Count == 0) return;
        bool Same(Rect2 a, Rect2 b) => Math.Abs(a.Width - b.Width) < 0.05 && Math.Abs(a.Height - b.Height) < 0.05;
        _doc.Select(_doc.State.Editable().Where(s => sel.Any(b => Same(b, s.Bounds))).Select(s => s.Id));
    }

    void SelectInside()
    {
        var outer = SelectedPaths().Where(p => p.World.Subpaths.Any(sp => sp.Closed)).ToList();
        if (outer.Count == 0) return;
        var polys = outer.SelectMany(o => o.World.Flatten(0.05).Where(f => f.Closed).Select(f => f.Points)).ToList();
        bool Inside(Shape s) => s.Bounds is var b && !b.IsEmpty && polys.Any(p => new[] { b.TopLeft, b.BottomRight, new Vec2(b.MinX, b.MaxY), new Vec2(b.MaxX, b.MinY) }.All(c => Geom.Inside(p, c)));
        var ids = outer.Select(o => o.Id).ToHashSet();
        _doc.Select(_doc.State.Editable().Where(s => !ids.Contains(s.Id) && Inside(s)).Select(s => s.Id));
    }

    async Task SimplifySel()
    {
        var paths = SelectedPaths().Where(p => p is not (TextShape or CodeShape)).ToList();
        if (paths.Count == 0) return;
        var input = await Dialogs.Prompt(this, "Simplify", "Stay within (mm) of the original outline", "0.02");
        if (input is null || !double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tol) || tol <= 0) return;
        var s = _doc.State;
        int before = 0, after = 0;
        foreach (var p in paths)
        {
            var (g, b, a) = PathTools.Simplify(p.World, tol);
            before += b;
            after += a;
            if (a < b) s = Ops.Replace(s, Plain(p, g));
        }
        if (after >= before) { StatusText.Text = "Already as simple as it gets at that accuracy."; return; }
        _doc.Commit("Simplify", s);
        StatusText.Text = $"Simplified: {before} pieces → {after}.";
    }

    /// <summary>
    /// The selected closed shapes are the board in the machine: they move to the Material layer
    /// (shown, never sent), and nesting and the model slicer fill them instead of the whole bed.
    /// </summary>
    /// <summary>
    /// The selected design split across the selected material outlines (the pieces it goes over; all
    /// the ones it touches when none are selected): a page each, then Line up pieces to fit them.
    /// </summary>
    async Task SplitOntoPieces()
    {
        var s = _doc.State;
        var sel = _doc.Selection;
        var material = MaterialOutline.Layer(s)?.Id;
        var outlines = MaterialOutline.On(s);
        var pieces = outlines.Where(p => sel.Contains(p.Id)).ToList();
        var art = s.Shapes.Where(x => sel.Contains(x.Id) && x.LayerId != material).Select(x => x.Id).ToHashSet();
        if (pieces.Count == 0 && art.Count > 0)
        {
            var box = s.Bounds(art);
            pieces = [.. outlines.Where(p => p.Bounds.Intersects(box))];
        }
        if (pieces.Count < 2 || art.Count == 0)
        {
            await Dialogs.Message(this, "Split onto pieces",
                "Select the design and the pieces it goes across. Draw each piece the size it really is (a rectangle 200 × 75 mm for a 200 × 75 mm board), " +
                "right-click it → Use as material outline, and lay them out as they'll go together. Then put the design over them, select it all, and split.\n\n" +
                (pieces.Count < 2 ? $"Pieces found: {pieces.Count} (at least 2 are needed)." : "Nothing of the design is selected."));
            return;
        }
        double w = Canvas.Machine.FieldW, h = Canvas.Machine.FieldH;
        StatusText.Text = "Splitting…";
        var result = await Task.Run(() => PieceSplit.Split(s, pieces, art, w, h));
        _doc.Commit("Split onto pieces", result.State, []);
        _layers.Activate(_doc.ActiveLayer);
        Canvas.FitField();
        StatusText.Text = $"Split onto {result.Pieces} pieces: a page each (\"Piece 1 of {result.Pieces}\"…), after the layout. Only each page's piece is sent; the rest shows grey.";
        if (result.Notes.Count > 0) await Dialogs.Message(this, "Split onto pieces", string.Join("\n", result.Notes));
        ShowLineUp();
    }

    LineUpWindow? _lineUp;

    /// <summary>The Line up the pieces window (one at a time).</summary>
    void ShowLineUp()
    {
        if (_lineUp is { IsVisible: true }) { _lineUp.Activate(); return; }
        _lineUp = new LineUpWindow(_doc, _machine, Canvas.Machine);
        _lineUp.Closed += (_, _) => _lineUp = null;
        _lineUp.Show(this);
    }

    void MaterialSel()
    {
        var ids = SelectedPaths().Where(p => p is not (TextShape or CodeShape) && p.Geometry.Subpaths.Any(sp => sp.Closed)).Select(p => p.Id).ToHashSet();
        if (ids.Count == 0) { StatusText.Text = "Select the closed outline of your material (drawn, or picked from the camera)."; return; }
        _doc.Commit("Use as material outline", MaterialOutline.Mark(_doc.State, ids), ids);
        StatusText.Text = $"Material outline{(ids.Count == 1 ? "" : "s")} set: on the Material layer, which is never sent. Nest and 3D to slices now fill {(ids.Count == 1 ? "it" : "them")}.";
    }

    void CutShapesSel()
    {
        var paths = SelectedPaths().Where(p => p is not (TextShape or CodeShape)).ToList();
        if (paths.Count < 2) return;
        var pieces = PathTools.CutShapes([.. paths.Select(p => p.World)]);
        if (pieces.Count == 0) return;
        var layer = paths[0].LayerId;
        var made = pieces.Select((g, i) => (Shape)new PathShape { Name = $"Piece {i + 1}", LayerId = layer, Geometry = g }).ToList();
        var s = Ops.AddShapes(Ops.Delete(_doc.State, paths.Select(p => p.Id).ToHashSet()), made);
        _doc.Commit("Cut shapes", s, made.Select(m => m.Id));
        StatusText.Text = $"Cut into {made.Count} pieces.";
    }

    /// <summary>Lock the selection, or unlock every locked shape; with <paramref name="only"/>, just those shapes (the right-click menu).</summary>
    void LockSel(bool locked, IReadOnlySet<Guid>? only = null)
    {
        var s = _doc.State;
        IReadOnlySet<Guid> ids = only ?? (locked ? _doc.Selection : s.Shapes.Where(x => x.Locked).Select(x => x.Id).ToHashSet());
        if (ids.Count == 0) return;
        var shapes = s.Shapes.Select(x => ids.Contains(x.Id) && x.Locked != locked ? x.WithLocked(locked) : x);
        _doc.Commit(locked ? "Lock" : only is null ? "Unlock all" : "Unlock", s with { Shapes = System.Collections.Immutable.ImmutableList.CreateRange(shapes) }, locked ? [] : ids);
    }

    HistoryWindow? _history;

    void ShowHistory()
    {
        if (_history is { } open) { open.Activate(); return; }
        _history = new HistoryWindow(_doc);
        _history.Closed += (_, _) => _history = null;
        _history.Show(this);
    }

    void ConvertToPaths()
    {
        var state = _doc.State;
        foreach (var t in state.Shapes.OfType<PathShape>().Where(t => t is TextShape or CodeShape or ParamShape && _doc.Selection.Contains(t.Id)).ToList())
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

    /// <summary>
    /// Aimed focus (xTool's fallback): the red pointer aimed at the middle of the selection, the
    /// close-up camera reads the height there from where its dot lands (the pointer comes in at a
    /// slant, so the dot moves with height), and the head moves to focus on it. Nothing fires and
    /// nothing touches the material.
    /// </summary>
    async Task FocusByPointer()
    {
        if (_machine.Client is not { IsConnected: true } client) { await Dialogs.Message(this, "Focus on selection", "Connect the machine first."); return; }
        if (SurfaceProbe.LoadCalibration() is not { } cal)
        {
            await Dialogs.Message(this, "Focus on selection", "The red pointer needs calibrating once first: Laser → Curved surface → Calibrate the pointer (on a flat board).");
            return;
        }
        if (_machine.Distance is not { } now) { await Dialogs.Message(this, "Focus on selection", "Focus once first (auto-focus, or type the height in the machine panel): the pointer reads heights near the focus."); return; }
        var sel = _doc.State.Shapes.Where(x => _doc.Selection.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        var aim = sel.IsEmpty ? new Vec2(Canvas.Machine.FieldW / 2, Canvas.Machine.FieldH / 2) : sel.Center;
        StatusText.Text = $"Reading the height at ({aim.X:0}, {aim.Y:0}) with the pointer…";
        var heights = await new SurfaceProbe(client).MeasureAsync([aim], now, cal, new Progress<string>(t => StatusText.Text = t), CancellationToken.None);
        if (heights[0] is not { } h)
        {
            StatusText.Text = "The pointer's dot couldn't be read there: too far from the current focus, too dark, or shiny. Auto-focus nearer first, or pick another spot.";
            return;
        }
        if (Math.Abs(h - now) < 0.05) { StatusText.Text = $"Already in focus there ({h:0.00} mm)."; return; }
        if (await _machine.MoveFocusAsync(h) is { } error) { StatusText.Text = $"Couldn't move the focus: {error}"; return; }
        StatusText.Text = $"Focused on the {(sel.IsEmpty ? "middle of the bed" : "selection")}: {h:0.00} mm ({h - now:+0.00;-0.00} from before), read with the pointer.";
        AppLog.Write("focus", $"Aimed focus at ({aim.X:0.#}, {aim.Y:0.#}): {now:0.00} → {h:0.00} mm.");
    }

    /// <summary>
    /// The material's thickness: the auto-focus distance on it against the empty bed's (measured once,
    /// on the first go). Auto-focus moves the head, but nothing fires.
    /// </summary>
    async Task MeasureThickness()
    {
        if (!_machine.IsConnected) { await Dialogs.Message(this, "Thickness", "Connect the machine first."); return; }
        if (_settings.BedFocusMm is not { } bed)
        {
            if (!await Dialogs.Confirm(this, "Thickness: the empty bed first", "To measure thickness, PhotonCanvas first needs the empty bed's height, once. Clear the bed under the head (nothing on it), then measure.", "Measure the empty bed", "Cancel")) return;
            StatusText.Text = "Focusing on the empty bed…";
            if (await _machine.AutoFocusNowAsync() is not { } b) { StatusText.Text = "Couldn't focus on the bed."; return; }
            _settings.BedFocusMm = b;
            _settings.Save();
            await Dialogs.Message(this, "Thickness", $"The empty bed is measured ({b:0.00} mm). Now put the material under the head and press Thickness again.");
            return;
        }
        StatusText.Text = "Focusing on the material…";
        if (await _machine.AutoFocusNowAsync() is not { } on) { StatusText.Text = "Couldn't focus on the material."; return; }
        double t = Math.Abs(bed - on);
        StatusText.Text = $"The material is {t:0.00} mm thick (focus {on:0.00} mm, the empty bed {bed:0.00} mm).";
        if (await Dialogs.Confirm(this, "Thickness", $"The material is {t:0.00} mm thick.\n\nIf that's far off (the bed was moved or changed), measure the empty bed again.", "OK", "Measure the empty bed again"))
            return;
        _settings.BedFocusMm = null;
        _settings.Save();
        await MeasureThickness();
    }

    bool _quickRunning;

    /// <summary>
    /// Quick processing (when turned on): the lid closed on an idle machine with a design to run, so
    /// focus on the material, photograph the bed, centre the design on the biggest piece found, and
    /// load the job. Nothing fires until the machine's button is pressed.
    /// </summary>
    async Task QuickProcess()
    {
        if (!_settings.QuickProcessing || _quickRunning || !_machine.IsConnected || _machine.Mode is not ("P_IDLE" or "P_WORK_DONE" or "P_FINISH" or "")) return;
        if (_settings.ActiveSetup().Kind != MachineKind.XTool) return;
        var output = JobBuilder.OutputLayers(_doc.State).SelectMany(l => _doc.State.ShapesFor(l)).ToList();
        if (output.Count == 0) return;
        _quickRunning = true;
        try
        {
            StatusText.Text = "Lid closed: focusing…";
            if (await _machine.AutoFocusNowAsync() is not { } dist) { StatusText.Text = "Quick processing stopped: the focus couldn't be measured."; return; }
            StatusText.Text = "Lid closed: finding the material…";
            const double MmPerPx = 0.4;
            var area = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH);
            using var photo = await _machine.CaptureAreaAsync(area, MmPerPx, dist);
            var raster = CameraImaging.ToRaster(photo);
            var pieces = await Task.Run(() => PhotonCanvas.Core.Camera.PieceFinder.Find(PhotonCanvas.Core.Imaging.BackgroundRemoval.Mask(raster, new PhotonCanvas.Core.Imaging.BackgroundOptions()), raster.Width, raster.Height, area, MmPerPx, 400));
            if (pieces.MaxBy(p => p.AreaMm2) is { } piece)
            {
                var b = output.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, c) => a.Union(c));
                var move = piece.Centre - b.Center;
                if (move.Length > 0.05)
                    _doc.Commit("Quick processing: centred on the material", Ops.Transform(_doc.State, output.Select(x => x.Id).ToHashSet(), Affine.Translate(move.X, move.Y)));
            }
            else StatusText.Text = "Lid closed: no material found in the photo; the design stays where it is.";
            await _job.Send(quick: true);
            StatusText.Text = pieces.Count > 0 ? "Quick processing: focused, centred on the material and loaded. Press the machine's button to start." : StatusText.Text;
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or FormatException or InvalidOperationException)
        {
            StatusText.Text = $"Quick processing stopped: {e.Message}";
        }
        finally { _quickRunning = false; }
    }

    /// <summary>Print-then-cut: cuts round printed art the camera finds (close-up when it covers the area, else the overview).</summary>
    async Task CutRoundPrints()
    {
        if (!_machine.IsConnected) { await Dialogs.Message(this, "Cut round prints", "Connect the machine first: this needs a fresh camera photo."); return; }
        // Where to look: inside the selected shape(s), else the whole bed.
        var sel = _doc.State.Shapes.Where(x => _doc.Selection.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        var within = sel.IsEmpty ? new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH) : sel;
        var text = await Dialogs.Prompt(this, "Cut round prints", "How far outside the printed art the cut goes (mm): a white border round each sticker.", "1");
        if (text is null || !double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double margin) || margin < 0) return;
        StatusText.Text = "Photographing the prints…";
        CameraPhoto? photo = null;
        if (_machine.CloseUpSees() is { } sees && sees.Contains(within.TopLeft) && sees.Contains(within.BottomRight)) photo = await _machine.CloseUpBedPhotoAsync();
        if (photo is null) { await _machine.UpdatePhotoAsync(); photo = Canvas.Photo; }
        if (photo is null) { StatusText.Text = "No camera photo: calibrate the camera first (Camera → Calibrate)."; return; }
        var cuts = await Task.Run(() => PrintCut.Find(photo.Image, photo.Area, photo.MmPerPx, within, new PrintCutOptions { MarginMm = margin }));
        int n = cuts.Subpaths.Length;
        if (n == 0) { StatusText.Text = "No print found there: is it on white paper, in the camera's view and well lit?"; return; }
        var s = _doc.State;
        var layer = s.Layers.FirstOrDefault(l => l.Name == "Print cut") ?? Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = "Print cut" };
        if (s.FindLayer(layer.Id) is null) s = Ops.AddLayer(s, layer);
        var shapes = cuts.Subpaths.Select((sp, i) => (Shape)new PathShape { Name = $"Print {i + 1}", LayerId = layer.Id, Geometry = new PathGeometry([sp]) }).ToList();
        _doc.Commit("Cut round prints", Ops.AddShapes(s, shapes), shapes.Select(x => x.Id));
        StatusText.Text = $"{n} print{(n == 1 ? "" : "s")} found: a cut round each, {margin:0.##} mm out, on the \"Print cut\" layer. Set its power for the paper (a kiss cut for stickers).";
    }

    /// <summary>Stencil bridges: the middles of O, A, R… kept joined to the stencil by a strip across the letter.</summary>
    async Task StencilSel()
    {
        var shapes = SelectedPaths();
        if (shapes.Count == 0) { await Dialogs.Message(this, "Stencil bridges", "Select the letters or shapes to cut as a stencil first."); return; }
        var text = await Dialogs.Prompt(this, "Stencil bridges", "How wide each bridge is (mm). A strip of the sheet this wide is kept across each letter that has a middle (O, A, R, B…), so the middle stays in.", "1");
        if (text is null || !double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) || !(w > 0)) return;
        var s = _doc.State;
        int islands = 0, changed = 0;
        foreach (var sh in shapes)
        {
            var (g, n) = PhotonCanvas.Doc.Geometry.Stencil.Bridge(sh.World, w);
            if (n == 0) continue;
            islands += n;
            changed++;
            s = Ops.Replace(s, new PathShape { Id = sh.Id, Name = sh.Name, LayerId = sh.LayerId, GroupId = sh.GroupId, Geometry = g });
        }
        if (changed == 0) { StatusText.Text = "Nothing to bridge: none of the selected shapes has a middle that would fall out."; return; }
        _doc.Commit("Stencil bridges", s);
        StatusText.Text = $"Bridged {islands} middle{(islands == 1 ? "" : "s")} in {changed} shape{(changed == 1 ? "" : "s")} with {w:0.##} mm strips.";
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

    /// <summary>Everything on the active layer that could be clicked: locked shapes, and a hidden or locked layer's, are left out.</summary>
    void SelectActiveLayer()
    {
        var pickable = _doc.State.Editable().Select(s => s.Id).ToHashSet();
        _doc.Select(_doc.State.ShapesOn(_doc.ActiveLayer).Select(s => s.Id).Where(pickable.Contains));
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        var svgs = files.Where(Importer.CanLoad).ToList();
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
            if (ConnectOnOpen) RememberOpen();
            ClearAutosave();
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

    async Task ShowShortcuts()
    {
        var keys = Commands().Select(c => (c, k: HotkeysWindow.Keys(c, _settings))).Where(x => x.k.Length > 0)
            .GroupBy(x => x.c.Group).Select(g => $"{g.Key}: " + string.Join(" · ", g.Select(x => $"{x.k.Split('|')[0]} {x.c.Name.ToLowerInvariant()}")));
        var text = string.Join("\n", keys.Concat(
        [
            "",
            "Space or middle mouse: pan · wheel: zoom · Shift+wheel: scroll sideways",
            "Arrows: nudge 1 mm (Shift 10 mm, Ctrl 0.1 mm) · Ctrl+1…9: move the selection to layer 1…9",
            "Drag left→right: select inside · right→left: select touching",
            "Corner handles keep proportions (Shift = free) · Alt = from centre",
            "Rotate handle: Shift snaps to 15° · hold Ctrl while dragging to turn snapping off",
            "Esc: cancel / deselect",
        ]));
        if (await Dialogs.Confirm(this, "Keyboard shortcuts", text, "Change shortcuts…", "Close")) await ShowHotkeys();
    }

    async Task ShowFonts()
    {
        var w = new FontWindow(_settings);
        w.FontsChanged += _props.RefreshFonts;
        await w.ShowDialog(this);
    }

    Task ShowGettingStarted() => new WelcomeWindow(firstRun: false, _machine.SetUpKeys, () => _machine.HasKeys).ShowDialog(this);

    Task ShowSafety() => Dialogs.Message(this, "Safety and disclaimer", WelcomeWindow.SafetyText + "\n\n" + WelcomeWindow.DisclaimerText);

    Task ShowAbout() => Dialogs.Message(this, $"About {AppIdentity.ProductName}",
        $"{AppIdentity.ProductName} {Updates.Current} – an offline, no-telemetry laser workspace for the xTool F2 Ultra.\n\n" +
        "Jobs are generated locally in the same G-code dialect as the stock software, verified line for line against real exports.\n\n" +
        "Free software under the GNU General Public License v3 or later (see LICENSE and THIRD-PARTY-NOTICES). No warranty.\n\n" +
        Updates.SourceOffer + "\n\n" +
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

    /// <summary>Each selected copy (a group, or a shape on its own) marks the next number, in reading order.</summary>
    void NumberCopies()
    {
        var picked = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList();
        if (picked.Count == 0) return;
        var units = picked.GroupBy(s => s.GroupId ?? s.Id).Select(g => (Ids: g.Select(x => x.Id).ToHashSet(), Box: _doc.State.Bounds(g.Select(x => x.Id).ToHashSet()))).ToList();
        double row = Math.Max(1, units.Average(u => u.Box.Height) / 2);
        var ordered = units.Count == 1 ? units : [.. units.OrderBy(u => Math.Round(u.Box.Center.Y / row)).ThenBy(u => u.Box.Center.X)];
        var s = _doc.State;
        for (int i = 0; i < ordered.Count; i++)
            s = s with { Shapes = [.. s.Shapes.Select(x => ordered[i].Ids.Contains(x.Id) ? x.WithVarOffset(units.Count == 1 ? 0 : i) : x)] };
        _doc.Commit("Number copies", s);
        StatusText.Text = units.Count == 1
            ? "This copy marks the design's own serial number / row again."
            : $"{units.Count} copies, each marking the next serial number / spreadsheet row (back to front, left to right).";
    }

    Task ShowExactDepth() => new ExactDepthWindow(_doc, _machine, _library, _doc.Selection).ShowDialog(this);

    Task ShowToneTest(bool results) =>
        new ToneTestWindow(_doc, _library, Canvas.Machine, () => Canvas.Photo, _machine.UpdatePhotoAsync, results, _machine.CloseUpBedPhotoAsync, _machine.CloseUpSees, () => _machine.Lights, _machine.SetLights).ShowDialog(this);

    Task ShowCloseUpCalibration(bool find) =>
        new CloseUpCalibrationWindow(_doc, _machine, Canvas.Machine, find).ShowDialog(this);

    Task ShowLineTest(bool results) =>
        new LineTestWindow(_doc, Canvas.Machine, () => Canvas.Photo, _machine.UpdatePhotoAsync, results, _machine.CloseUpBedPhotoAsync, _machine.CloseUpSees, () => _machine.Lights, _machine.SetLights).ShowDialog(this);

    Task ShowCutTest(bool results) =>
        new CutTestWindow(_doc, _library, Canvas.Machine, () => Canvas.Photo, _machine.UpdatePhotoAsync, results).ShowDialog(this);

    Task ShowEtchTest(bool results) =>
        new EtchTestWindow(_doc, _library, Canvas.Machine, () => Canvas.Photo, _machine.UpdatePhotoAsync, results,
            d => _machine.CameraMapFor(d) is { } c ? new EtchTestWindow.ImageMapChoiceInfo(c.Exact, c.Description) : null).ShowDialog(this);

    async Task PlaceOnEveryPiece()
    {
        var outline = SelectedOutline();
        if (outline.Count == 0) { await Dialogs.Message(this, "Every piece", "Select the design to put on each piece first."); return; }
        if (!_machine.IsConnected) { await Dialogs.Message(this, "Every piece", "Connect to the machine first: finding the pieces needs a fresh camera photo."); return; }
        var ids = _doc.Selection;
        var w = new BatchPlaceWindow(_machine, outline, placements =>
        {
            // One copy per piece (each its own group); the copies' fields count up in reading order.
            var originals = _doc.State.Shapes.Where(s => ids.Contains(s.Id)).ToList();
            var made = new List<Shape>();
            var s = _doc.State;
            for (int i = 0; i < placements.Count; i++)
            {
                var copy = Ops.CopyAsUnit(originals, placements[i].Move).Select(c => c.WithVarOffset(i)).ToList();
                s = Ops.AddShapes(s, copy);
                if (copy.Count > 1) s = Ops.Group(s, copy.Select(c => c.Id).ToHashSet()).Item1;
                made.AddRange(copy);
            }
            s = Ops.Delete(s, ids);
            _doc.Commit("Place on every piece", s, made.Select(m => m.Id));
            StatusText.Text = $"A copy on each of {placements.Count} pieces." + (VariableText.Used(_doc.State).Count > 0 ? " Each one marks the next serial number / spreadsheet row." : "");
        });
        await w.ShowDialog(this);
    }

    /// <summary>Colour layers over a picture from a measured palette; the picture stops being sent when it's alone on its layer.</summary>
    async Task MakeColourPicture(ImageShape picture)
    {
        var w = new ColourPictureWindow(picture, _library);
        await w.ShowDialog(this);
        if (w.Result is not { } r) return;
        var (state, added) = Doc.Colour.ColourPicture.Add(_doc.State, picture, r.Palette, r.Options);
        if (added.Count == 0) return;
        bool alone = state.ShapesOn(picture.LayerId).Count() == 1;
        if (alone) state = Ops.UpdateLayers(state, new HashSet<Guid> { picture.LayerId }, l => l with { Output = false });
        _doc.Commit("Colour picture", state, added);
        StatusText.Text = $"Added {added.Count} colour layers." + (alone ? " The picture's own layer is now a guide only (not sent)." : " The picture shares its layer, which is still sent: turn its output off if you only want the colours.");
    }

    /// <summary>Grating layers over a picture; the picture itself stops being sent when it's alone on its layer.</summary>
    async Task MakeDiffraction(ImageShape picture)
    {
        var w = new DiffractionWindow(picture);
        await w.ShowDialog(this);
        if (w.Result is not { } o) return;
        var (state, added) = Doc.Colour.Diffraction.Add(_doc.State, picture, o);
        if (added.Count == 0) return;
        bool alone = state.ShapesOn(picture.LayerId).Count() == 1;
        if (alone) state = Ops.UpdateLayers(state, new HashSet<Guid> { picture.LayerId }, l => l with { Output = false });
        _doc.Commit("Diffraction picture", state, added);
        StatusText.Text = alone
            ? $"Added {added.Count} grating layers. The picture's own layer is now a guide only (not sent)."
            : $"Added {added.Count} grating layers. The picture shares its layer, which is still sent: turn its output off if you only want the gratings.";
    }

    async Task AddColourTest()
    {
        var w = new ColourTestWindow(_settings.TextFont);
        await w.ShowDialog(this);
        if (w.Result is not { } o) return;
        var (state, added) = Doc.Colour.ColourTest.Add(_doc.State, o);
        _doc.Commit("Colour test", state, added);
        Canvas.FitField();
    }

    async Task MeasureColours()
    {
        var w = new MeasureColourWindow(_doc.State, _library, Canvas.Photo, _machine.Distance, _machine);
        await w.ShowDialog(this);
        _layerEditor.RefreshMaterials();
    }

    /// <summary>Make a new code (<paramref name="id"/> null) or change one.</summary>
    async Task EditCode(Guid? id)
    {
        var existing = id is { } i ? _doc.State.FindShape(i) as CodeShape : null;
        if (id is not null && existing is null) return;
        var w = new CodeWindow(existing?.Spec, _doc.State.Vars, () => _ = Guard(ShowVariables));
        await w.ShowDialog(this);
        if (w.Result is not { } spec) return;
        if (existing is not null)
        {
            _doc.Commit("Edit code", Ops.Replace(_doc.State, existing.Edit(spec)), [existing.Id]);
            return;
        }
        // On the active layer if it can take vectors, else a new engrave layer; centred on the work area.
        var state = _doc.State;
        var layer = state.FindLayer(_doc.ActiveLayer) is { Visible: true, Locked: false, Mode: LayerMode.Fill } l ? l : null;
        if (layer is null)
        {
            layer = Layer.Default(state.Layers.Count, LayerMode.Fill) with { Name = "Codes", LinesPerCm = 200 };
            state = Ops.AddLayer(state, layer);
        }
        var code = CodeShape.Create(spec, null, layer.Id, Affine.Identity);
        var b = code.Bounds;
        var at = new Vec2(Canvas.Machine.FieldW / 2, Canvas.Machine.FieldH / 2);
        if (!b.IsEmpty) code = (CodeShape)code.With(transform: Affine.Translate(at.X - b.Center.X, at.Y - b.Center.Y));
        _doc.Commit("Add code", Ops.AddShapes(state, [code]), [code.Id]);
        _layers.Activate(layer.Id);
    }

    async Task ShowVariables()
    {
        var w = new VariablesWindow(_doc.State.Vars);
        await w.ShowDialog(this);
        if (w.Result is not { } v || v == _doc.State.Vars) return;
        _doc.Commit("Variables", _doc.State with { Vars = v });
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
