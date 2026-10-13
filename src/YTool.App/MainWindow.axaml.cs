using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YTool.Core;
using YTool.Doc;

namespace YTool.App;

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

    static readonly FilePickerFileType ProjectType = new("YTool project") { Patterns = ["*.ytool"] };
    static readonly FilePickerFileType SvgType = new("SVG drawing") { Patterns = ["*.svg"] };

    public MainWindow()
    {
        InitializeComponent();
        Canvas.Doc = _doc;
        _layers = new LayersPanel(_doc);
        _props = new PropertiesPanel(_doc, _layers, _settings);
        _machine = new MachinePanel(_settings);
        _job = new JobPanel(_doc, _settings, Canvas, _machine);
        LayersHost.Content = _layers;
        PropertiesHost.Content = _props;
        JobHost.Content = _job;
        MachineHost.Content = _machine;

        BuildMenu();
        WireTools();
        UndoButton.Click += (_, _) => _doc.Undo();
        RedoButton.Click += (_, _) => _doc.Redo();
        FitButton.Click += (_, _) => Canvas.FitField();
        Canvas.CursorMoved += p => CursorText.Text = p is { } v ? $"X {v.X:0.00}   Y {v.Y:0.00} mm" : "";
        Canvas.ZoomChanged += z => ZoomText.Text = $"{z / 3.7795 * 100:0}%";
        _doc.Changed += _ => UpdateChrome();
        _doc.Changed += _ => FollowSelectionLayer();
        _machine.StatusChanged += s => MachineStatus.Text = s;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        Opened += async (_, _) =>
        {
            Canvas.Focus();
            if (OpenPathOnStartup is { } p) await OpenPath(p);
        };
        Closing += OnClosing;
        UpdateChrome();
    }

    // ---- chrome --------------------------------------------------------------

    void UpdateChrome()
    {
        var name = _doc.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : "Untitled";
        Title = $"{name}{(_doc.Dirty ? " •" : "")} — YTool Studio";
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
            (ToolSelect, Tool.Select), (ToolRect, Tool.Rect), (ToolEllipse, Tool.Ellipse), (ToolLine, Tool.Line), (ToolPan, Tool.Pan),
        };
        foreach (var (b, t) in map) b.Click += (_, _) => { Canvas.Tool = t; Canvas.Focus(); };
        void Sync(Tool t) { foreach (var (b, tool) in map) b.IsChecked = tool == t; }
        Canvas.ToolChanged += Sync;
        Sync(Canvas.Tool);
    }

    // ---- menu ----------------------------------------------------------------

    MenuItem? _undoItem, _redoItem, _recentMenu;
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
                AsyncItem("Import SVG…", () => ImportSvg(byColour: false), "Ctrl+I"),
                AsyncItem("Import SVG, one layer per colour…", () => ImportSvg(byColour: true), "Ctrl+Shift+I"),
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
                Item("Combine into one shape", Combine, "Ctrl+G", () => _doc.Selection.Count > 1),
                Item("Break apart", BreakApart, "Ctrl+Shift+G", HasSel),
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
        var gridItem = new MenuItem { Header = "Show grid", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        gridItem.Click += (_, _) => Canvas.ShowGrid = gridItem.IsChecked;
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
                gridItem,
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
        var help = new MenuItem
        {
            Header = "_Help",
            Items = { AsyncItem("Keyboard shortcuts", ShowShortcuts, "F1"), AsyncItem("About YTool Studio", ShowAbout) },
        };
        MainMenu.ItemsSource = new[] { file, edit, arrange, view, layer, help };
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
            (true, false, Key.I) => () => ImportSvg(false),
            (true, true, Key.I) => () => ImportSvg(true),
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
            (true, false, Key.G) => Combine,
            (true, true, Key.G) => BreakApart,
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
        var copies = _clipboard.Select(s => s.With(
            id: Guid.NewGuid(),
            layerId: layers.Contains(s.LayerId) ? s.LayerId : _doc.ActiveLayer,
            transform: s.Transform.Then(Affine.Translate(off, off)))).ToList();
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
                DefaultExtension = "ytool",
                SuggestedFileName = path is null ? "Untitled.ytool" : Path.GetFileName(path),
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

    async Task ImportSvg(bool byColour)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = byColour ? "Import SVG (one layer per colour)" : "Import SVG",
            AllowMultiple = true,
            FileTypeFilter = [SvgType],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await ImportPaths(paths, byColour);
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
            await Dialogs.Message(this, "Import SVG", "Every layer is hidden or locked. Show or unlock a layer to import into it.");
            return;
        }
        foreach (var path in paths)
        {
            SvgImportResult r;
            try { r = SvgImport.Load(path, new SvgImportOptions { PxPerInch = _settings.SvgPxPerInch }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException)
            {
                warnings.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }
            warnings.AddRange(r.Warnings.Select(w => $"{Path.GetFileName(path)}: {w}"));
            if (r.Paths.Count == 0) continue;

            // Keep the drawing's position when it fits the work area; otherwise centre it.
            var b = r.Bounds;
            var field = new Rect2(0, 0, Canvas.Machine.FieldW, Canvas.Machine.FieldH);
            var move = field.Contains(b) ? Affine.Identity
                : Affine.Translate(field.Center.X - b.Center.X, field.Center.Y - b.Center.Y);
            if (b.Width > field.Width || b.Height > field.Height)
                warnings.Add($"{Path.GetFileName(path)} is {b.Width:0.#} × {b.Height:0.#} mm, larger than the {field.Width:0} × {field.Height:0} mm work area. Scale it down before sending.");

            foreach (var ip in r.Paths)
            {
                Guid layerId;
                if (byColour)
                {
                    var colour = ip.Color ?? 0x000000;
                    var existing = state.Layers.FirstOrDefault(l => l.Name == ColourLayerName(colour));
                    if (existing is null)
                    {
                        existing = Layer.Default(state.Layers.Count) with { Name = ColourLayerName(colour), Color = DisplayColour(colour, state) };
                        state = Ops.AddLayer(state, existing);
                    }
                    layerId = existing.Id;
                }
                else layerId = target!.Value;
                var shape = new PathShape { Name = ip.Name, LayerId = layerId, Geometry = ip.Geometry.Transform(move) };
                state = Ops.AddShapes(state, [shape]);
                newIds.Add(shape.Id);
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
        var svgs = files.Where(f => f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)).ToList();
        var project = files.FirstOrDefault(f => f.EndsWith(DocFile.Extension, StringComparison.OrdinalIgnoreCase));
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
        "V select · R rectangle · E ellipse · L line · H pan",
        "Space or middle mouse: pan · wheel: zoom · Shift+wheel: scroll sideways",
        "Home: fit work area · F: fit selection",
        "Arrows: nudge 1 mm (Shift 10 mm, Ctrl 0.1 mm)",
        "Drag left→right: select inside · right→left: select touching",
        "Corner handles keep proportions (Shift = free) · Alt = from centre",
        "Rotate handle: Shift snaps to 15°",
        "Ctrl+D duplicate · Ctrl+G combine · Ctrl+Shift+G break apart",
        "Ctrl+1…9 move selection to layer 1…9 · Ctrl+M to the active layer",
        "Ctrl+Z / Ctrl+Y undo / redo · Esc cancel / deselect"));

    Task ShowAbout() => Dialogs.Message(this, "About YTool Studio",
        "YTool Studio – an offline, no-telemetry laser workspace for the xTool F2 Ultra.\n\n" +
        "Jobs are generated locally in the same G-code dialect as the stock software, verified line for line against real exports.");
}
