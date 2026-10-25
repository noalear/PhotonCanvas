using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Import;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Stacked slices: open a 3D model, choose which way up, its size and the material's thickness,
/// and get every layer as a part to cut, numbered from the bottom up, with where the next layer
/// sits marked on it. Glue them up in order and the model is back, in wood.
/// </summary>
public sealed class StackSliceWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    List<StlDepth.Triangle>? _mesh;
    string _name = "";
    SliceStack? _stack;
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    readonly StackView _view = new() { MinHeight = 380 };
    readonly ComboBox _look = new() { ItemsSource = new[] { "Solid", "Outlines" }, SelectedIndex = 0, Width = 120 };
    readonly ComboBox _material = new() { ItemsSource = new[] { "Basswood", "Birch plywood", "Walnut", "MDF", "Cardboard", "White acrylic", "Black acrylic" }, SelectedIndex = 0, Width = 150 };
    readonly CheckBox _burnt = new() { Content = "Burnt edges", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    readonly Slider _from = new() { Minimum = 1, Maximum = 1, Value = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider _to = new() { Minimum = 1, Maximum = 1, Value = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly TextBlock _range = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
    readonly TextBlock _file = new() { Text = "No model yet.", TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _up = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Z up (as made)", "Z down", "Y up", "Y down", "X up", "X down" }, SelectedIndex = 0 };
    readonly NumericUpDown _width = Number(1, 2000, 5, "0.#"), _depth = Number(1, 2000, 5, "0.#");
    readonly CheckBox _keepProportions = new() { Content = "Keep proportions", IsChecked = true };
    readonly Button _ownSize = Button("Model's own size", tip: "Back to the size the model was made at (one model unit = 1 mm, as almost every STL and OBJ is; STEP files say their units and are read in millimetres).");
    (double X, double Y, double Z) _natural = (1, 1, 1);
    bool _sizing;
    readonly NumericUpDown _turn = Number(-180, 180, 15, "0"), _height = Number(1, 2000, 5, "0.#"), _thick = Number(0.3m, 50, 0.5m, "0.##"), _dowel = Number(0, 20, 0.5m, "0.#"), _pin = Number(0, 20, 0.5m, "0.#");
    readonly ComboBox _outline = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "Middle of each slice (steps both ways)", "Outer: sand the steps back to the shape", "Inner: nothing sticks out, fill the steps" },
        SelectedIndex = 0,
    };
    readonly CheckBox _numbers = new() { Content = "Number each slice (light mark, 1 at the bottom)", IsChecked = true };
    readonly CheckBox _marks = new() { Content = "Mark where the next slice sits", IsChecked = true };
    readonly CheckBox _joined = new() { Content = "Keep every slice joined", IsChecked = true };
    readonly ComboBox _method = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Stacked layers (solid)", "Egg crate (slotted grid)", "Radial (fins on rings)" }, SelectedIndex = 0 };
    readonly NumericUpDown _sheetsX = Number(1, 60, 1, "0"), _sheetsY = Number(1, 60, 1, "0"), _fins = Number(3, 72, 1, "0"), _rings = Number(1, 20, 1, "0"), _core = Number(0, 50, 0.5m, "0.#"), _fit = Number(-0.5m, 1, 0.05m, "0.##");
    StackPanel _stackedPanel = null!, _eggPanel = null!, _radialPanel = null!;
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _stock = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<MaterialChoice> _stocks;
    readonly Button _make = new() { Content = "Make the slices", Classes = { "primary" }, IsEnabled = false, MinWidth = 150 };

    readonly AppSettings? _settings;
    // From the material library: the cut (thickness, kerf, slot fit from the cut test) and edge shaping (depth a pass from the etch test).
    readonly ComboBox _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<(Doc.Materials.Material M, Doc.Materials.Recipe R)?> _cuts = [];
    readonly TextBlock _cutNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    readonly CheckBox _shape = new() { Content = "Shape the edges: engrave the steps down to the slope" };
    readonly ComboBox _etchWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<(Doc.Materials.Material M, Doc.Materials.Recipe R)> _etches = [];
    readonly NumericUpDown _step = Number(0.02m, 2, 0.01m, "0.0##");
    readonly TextBlock _shapeNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    readonly Doc.Materials.MaterialLibrary? _library;
    // Pins cut from a dowel laid in the machine: a page of its own beside the slices.
    readonly CheckBox _cutPins = new() { Content = "Cut the pins from a dowel too" };
    readonly NumericUpDown _pinDowel = Number(0.5m, 20, 0.5m, "0.##"), _pinDowelLength = Number(10, 2000, 10, "0"), _pinShorter = Number(0, 40, 1, "0"), _pinSpare = Number(0, 100, 5, "0");
    readonly ComboBox _pinCutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly DowelPinPreview _pinPreview = new() { Margin = new(0, 4, 0, 0) };
    readonly TextBlock _pinNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    StackPanel _pinPanel = null!;
    // The assembly instructions: a PDF beside the design (or in the data folder until it's saved).
    readonly CheckBox _booklet = new() { Content = "Make an assembly booklet (PDF)", IsChecked = true };
    readonly ComboBox _bookletView = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Front", "Right side", "Back", "Left side" }, SelectedIndex = 0 };
    readonly Button _openBooklet = Button("Open the booklet", tip: "Opens the assembly booklet just made, in your PDF viewer, to read or print.");
    string? _bookletPath;

    public StackSliceWindow(Document doc, MachineProfile machine, AppSettings? settings = null, Doc.Materials.MaterialLibrary? library = null)
    {
        _settings = settings;
        _library = library;
        _doc = doc;
        _machine = machine;
        Title = "Stacked slices";
        Width = 1100;
        Height = 740;
        MinWidth = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _height.Value = 60; _thick.Value = 3; _dowel.Value = 0; _turn.Value = 0; _pin.Value = 2;
        _sheetsX.Value = 6; _sheetsY.Value = 4; _fins.Value = 12; _rings.Value = 3; _core.Value = 6; _fit.Value = 0.1m;
        foreach (var n in new[] { _sheetsX, _sheetsY, _fins, _rings, _core, _fit }) n.ValueChanged += (_, _) => Later();
        _method.SelectionChanged += (_, _) => { ShowMethod(); Later(); };
        (_stocks, int first) = MaterialChoice.For(doc.State, machine.FieldW, machine.FieldH);
        _stock.ItemsSource = _stocks.Select(c => c.Label).ToList();
        _stock.SelectedIndex = first;
        _stock.SelectionChanged += (_, _) => Later();

        var open = Button("Open a 3D model…", tip: "STL or OBJ from a 3D printing site or Blender; STEP straight from CAD (Fusion, Onshape, SolidWorks, McMaster-Carr parts…)");
        open.Click += async (_, _) => await Open();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Reslice(); };
        foreach (var n in new[] { _thick, _dowel, _pin }) n.ValueChanged += (_, _) => Later();
        _pinDowel.Value = 2; _pinDowelLength.Value = 300; _pinShorter.Value = 15; _pinSpare.Value = 10;
        // The dowel follows the pin holes until it's set apart from them.
        _pin.ValueChanged += (_, e) => { if (e.OldValue is { } was && _pinDowel.Value == was && e.NewValue is > 0) _pinDowel.Value = e.NewValue; };
        foreach (var n in new[] { _pinDowel, _pinDowelLength, _pinShorter, _pinSpare }) n.ValueChanged += (_, _) => Pins();
        _cutPins.IsCheckedChanged += (_, _) => Pins();
        _pinCutWith.SelectionChanged += (_, _) => Pins();
        ToolTip.SetTip(_cutPins, "A page of its own with a straight cut across a dowel every pin length, for laying the dowel on the bed and cutting all the pins in one go. Made with the slices.");
        _outline.SelectionChanged += (_, _) => Later();
        // Turning or standing it another way changes what X, Y and Z are: sizes follow (height kept).
        _up.SelectionChanged += (_, _) => { Natural(); Resize(_height); };
        _turn.ValueChanged += (_, _) => { Natural(); Resize(_height); };
        foreach (var n in new[] { _width, _depth, _height }) n.ValueChanged += (_, _) => Resize(n);
        _keepProportions.IsCheckedChanged += (_, _) => { if (_keepProportions.IsChecked == true) Resize(_height); };
        _ownSize.Click += (_, _) => OwnSize();
        _joined.IsCheckedChanged += (_, _) => Later();
        Library();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _etchWith.SelectionChanged += (_, _) => { if (_etchWith.SelectedIndex >= 0 && _etchWith.SelectedIndex < _etches.Count && _etches[_etchWith.SelectedIndex].R.EtchMmPerPass is { } e) _step.Value = (decimal)e; Later(); };
        _step.ValueChanged += (_, _) => Later();
        _shape.IsCheckedChanged += (_, _) =>
        {
            // Shaping takes material away, so the slices should start out covering the whole shape.
            if (_shape.IsChecked == true && _outline.SelectedIndex != (int)SliceOutline.Outer) _outline.SelectedIndex = (int)SliceOutline.Outer;
            ShowShaping();
            Later();
        };
        ToolTip.SetTip(_shape, "Each slice's top is engraved down in steps, a pass a step, wherever the model slopes away inside the slice, so the stack's steps become the model's slopes. Needs the depth one engrave pass takes off: Laser → Etch rate test.");
        ToolTip.SetTip(_joined, "Where the shape slopes steeply (a trunk, an ear), two slices can miss each other even though the model is joined. This adds the shape where they meet to both, so they overlap enough to glue and pin. Sand it back to the shape.");
        _make.Click += (_, _) => Make();
        _openBooklet.IsVisible = false;
        _booklet.IsCheckedChanged += (_, _) => BookletShown();
        _openBooklet.Click += (_, _) =>
        {
            if (_bookletPath is null || !File.Exists(_bookletPath)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_bookletPath) { UseShellExecute = true })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { _summary.Text += $"\nCouldn't open it: {e.Message}"; }
        };
        ToolTip.SetTip(_booklet, "Instructions for putting the slices together, like a building-set manual: the finished model, every part, then a step per slice from the bottom up, showing the stack so far with the new parts in colour, their labels, and where the pins go. Saved beside your design when you make the slices.");
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                open, _file,
                Field("Stands with", _up),
                Field("Turned (°)", _turn, tip: "Round the up direction: changes how the slices lie on the bed, not the model."),
                Field("Width X (mm)", _width, tip: "Across, left to right, standing the chosen way up."),
                Field("Depth Y (mm)", _depth, tip: "Front to back, standing the chosen way up."),
                Field("Height Z (mm)", _height, tip: "Up: this sets how many slices there are."),
                _keepProportions,
                _ownSize,
                Field("Material (mm)", _thick, tip: "One slice per thickness of material."),
                Field("Build as", _method),
                (_stackedPanel = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        Field("Outline", _outline),
                        _shape,
                        Field("Engrave with", _etchWith, tip: "Engrave settings from your library with a measured depth a pass (Laser → Etch rate test)."),
                        Field("Depth a pass (mm)", _step, tip: "How deep one engrave pass goes: one step of the shaping."),
                        _shapeNote,
                        Hint("A slice is the same shape top and bottom, but the model changes through it: a pyramid comes out stepped. Outer leaves material to sand back to the shape; Inner never sticks out."),
                        _marks, _joined,
                        Field("Pin holes Ø", _pin, tip: "Matching holes wherever a part sits on the one below, for short pins (toothpicks, skewers, brads) that line them up. Placed where the slices around them hide the holes when they can. 0 = none."),
                        Hint("A toothpick is about 2 mm, a bamboo skewer 3 mm. Set the cut layer's kerf so the pins fit snugly; pins need only be twice the material thick."),
                        _cutPins,
                        (_pinPanel = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                Field("Dowel Ø (mm)", _pinDowel, tip: "The dowel's diameter, measured with calipers (bought dowels are often a little under their size). Follows the pin holes until you change it."),
                                Field("Dowel length (mm)", _pinDowelLength, tip: "How long your dowel is. Only as much as fits on the bed is cut in one go."),
                                Field("Pins shorter by (%)", _pinShorter, tip: "A pin goes through two slices; a little shorter means it never stops them closing up, even with a drop of glue in the hole."),
                                Field("Spare pins (%)", _pinSpare, tip: "Extra pins on top of the ones the holes need (rounded up), for the ones that roll away or break."),
                                Field("Cut the dowel with", _pinCutWith, tip: "Cut settings from your library for the dowel's wood. A dowel is as thick as it is wide, so it needs settings that cut that deep."),
                                _pinPreview,
                                _pinNote,
                            },
                        }),
                        Field("Dowels Ø", _dowel, tip: "Two holes through every slice, for long dowels through the whole stack. Only where every slice has room. 0 = none."),
                    },
                }),
                (_eggPanel = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        Hint("Upright sheets across the model both ways, slotted half-way where they cross so they slide together: an open grid of the model's shape. Few parts, little material."),
                        Field("Sheets across X", _sheetsX, tip: "Sheets standing across the model's width, evenly spaced."),
                        Field("Sheets across Y", _sheetsY, tip: "Sheets standing across its depth."),
                    },
                }),
                (_radialPanel = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        Hint("Fins standing round an upright axis through the middle, slotted onto flat rings at a few heights. Best for round things: heads, vases, pumpkins."),
                        Field("Fins", _fins, tip: "Fins round the axis, evenly spaced."),
                        Field("Rings", _rings, tip: "Flat rings at even heights that hold the fins."),
                        Field("Centre hole Ø", _core, tip: "A hole up the middle of the rings (for a dowel); the fins start this far out. 0 = none."),
                    },
                }),
                _numbers,
                _booklet,
                Field("Booklet seen from", _bookletView, tip: "Which side of the model the booklet's pictures look at. Front is the side facing −Y in the file, as almost every 3D program saves it; if your model comes out from behind, choose Back."),
                Field("Cut with", _cutWith, tip: "Cut settings from your library: the material's thickness, and the kerf and slot fit your cut test measured, go on the slices."),
                _cutNote,
                Field("Slot fit (mm)", _fit, tip: "Slots are the material's thickness plus this. Raise it if the sheets won't go together, lower it (even below 0) if they're loose. The cut layer's kerf counts too."),
                Field("Cut from", _stock, tip: "Where the parts go. To use the board actually in the machine, outline it first (draw it, or Pick piece from camera), right-click it: Use as material outline. Further beds get the same outline on new pages."),
                _summary,
                _openBooklet,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _make } };
        var grid = new Grid { ColumnDefinitions = new("*,16,360") };
        _look.SelectionChanged += (_, _) => { _view.Look = _look.SelectedIndex == 1 ? StackLook.Lines : StackLook.Solid; _material.IsEnabled = _look.SelectedIndex == 0; BurntShown(); };
        _material.SelectionChanged += (_, _) => { _view.Material = (StackMaterial)Math.Max(0, _material.SelectedIndex); BurntShown(); };
        _burnt.IsCheckedChanged += (_, _) => _view.Burnt = _burnt.IsChecked == true;
        ToolTip.SetTip(_burnt, "The cut edges as they come off the laser: charred dark, darkest on the top where the beam comes in, with a little smoke beside them. Acrylic doesn't char.");
        _from.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Range(fromMoved: true); };
        _to.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Range(fromMoved: false); };
        var controls = new StackPanel
        {
            Spacing = 8, Margin = new(0, 8, 0, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "View", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, _look, _material, _burnt } },
                new Grid
                {
                    ColumnDefinitions = new("Auto,*,Auto,*,Auto"), ColumnSpacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "From", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } },
                        Col(_from, 1),
                        Col(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, 2),
                        Col(_to, 3),
                        Col(_range, 4),
                    },
                },
            },
        };
        var left = new DockPanel();
        DockPanel.SetDock(controls, Dock.Bottom);
        left.Children.Add(controls);
        left.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _view });
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = side };
        Grid.SetColumn(scroll, 2);
        grid.Children.Add(scroll);
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
        // Now the settings sit in their rows: hide the shaping rows (labels too) unless it's on.
        ShowShaping();
        ShowMethod();
        Pins();
    }

    /// <summary>Show a model already read (and slice it).</summary>
    public void Load(List<StlDepth.Triangle> mesh, string name)
    {
        _mesh = mesh;
        _name = name;
        _file.Text = $"{name}: {mesh.Count:N0} triangles.";
        Natural();
        OwnSize();
    }

    /// <summary>Burnt edges only mean something on solid wood or card.</summary>
    void BurntShown() => _burnt.IsEnabled = _look.SelectedIndex == 0 && _material.SelectedIndex < (int)StackMaterial.WhiteAcrylic;

    /// <summary>Only the settings for the chosen way of building.</summary>
    void ShowMethod()
    {
        _stackedPanel.IsVisible = _method.SelectedIndex == 0;
        _eggPanel.IsVisible = _method.SelectedIndex == 1;
        _radialPanel.IsVisible = _method.SelectedIndex == 2;
        _numbers.Content = _method.SelectedIndex == 0 ? "Number each slice (light mark, 1 at the bottom)" : "Name each sheet and mark what goes in each slot";
        _booklet.IsVisible = _method.SelectedIndex == 0;
        BookletShown();
    }

    /// <summary>The booklet's view only when there'll be a booklet.</summary>
    void BookletShown()
    {
        if (_bookletView.Parent is Control row) row.IsVisible = _method.SelectedIndex == 0 && _booklet.IsChecked == true;
    }

    static Control Col(Control c, int col)
    {
        Grid.SetColumn(c, col);
        return c;
    }

    /// <summary>The slices shown: the two sliders never cross.</summary>
    void Range(bool fromMoved)
    {
        if (_from.Value > _to.Value)
        {
            if (fromMoved) _to.Value = _from.Value; else _from.Value = _to.Value;
        }
        _view.From = (int)_from.Value;
        _view.To = (int)_to.Value;
        int n = (int)_to.Maximum;
        var what = _method.SelectedIndex == 0 ? "slices" : "sheets";
        _range.Text = _from.Value <= 1 && _to.Value >= n ? $"All {n} {what}" : $"{char.ToUpperInvariant(what[0])}{what[1..]} {_from.Value:0}–{_to.Value:0} of {n}";
    }

    /// <summary>The model's own size standing the chosen way up (for keeping proportions).</summary>
    void Natural()
    {
        if (_mesh is not { } mesh) return;
        var n = StackSlicer.NaturalSize(mesh, (SliceUp)Math.Max(0, _up.SelectedIndex), (double)(_turn.Value ?? 0));
        _natural = (Math.Max(1e-9, n.X), Math.Max(1e-9, n.Y), Math.Max(1e-9, n.Z));
    }

    /// <summary>The size the model was made at (its units taken as millimetres), standing the chosen way up.</summary>
    void OwnSize()
    {
        if (_mesh is null) return;
        _sizing = true;
        try
        {
            static decimal Mm(double v) => (decimal)Math.Clamp(Math.Round(v, 1), 1, 2000);
            _width.Value = Mm(_natural.X);
            _depth.Value = Mm(_natural.Y);
            _height.Value = Mm(_natural.Z);
        }
        finally { _sizing = false; }
        Later();
    }

    /// <summary>One size changed: with proportions kept, the other two follow it.</summary>
    void Resize(NumericUpDown changed)
    {
        if (_sizing) return;
        _sizing = true;
        try
        {
            if (_keepProportions.IsChecked == true)
            {
                double k = changed == _width ? (double)(_width.Value ?? 1) / _natural.X
                    : changed == _depth ? (double)(_depth.Value ?? 1) / _natural.Y
                    : (double)(_height.Value ?? 1) / _natural.Z;
                if (changed != _width) _width.Value = (decimal)Math.Round(_natural.X * k, 1);
                if (changed != _depth) _depth.Value = (decimal)Math.Round(_natural.Y * k, 1);
                if (changed != _height) _height.Value = (decimal)Math.Round(_natural.Z * k, 1);
            }
        }
        finally { _sizing = false; }
        Later();
    }

    void Later()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Open()
    {
        string? path;
        if (_settings is not null)
        {
            // Pick by sight: thumbnails of every model in the folder.
            var browser = new ModelBrowserWindow(_settings);
            await browser.ShowDialog(this);
            path = browser.Chosen;
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "3D model to slice",
                FileTypeFilter = [new FilePickerFileType("3D models (STL, OBJ, STEP)") { Patterns = ["*.stl", "*.obj", "*.stp", "*.step"] }],
            });
            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        if (path is null) return;
        _file.Text = $"Reading {Path.GetFileName(path)}…";
        try
        {
            if (Path.GetExtension(path).ToLowerInvariant() is ".stp" or ".step")
            {
                // From CAD: its exact shapes turned into triangles; say if any face couldn't be.
                var r = await Task.Run(() => PhotonCanvas.Doc.Import.Step.StepMesh.Load(path));
                Load(StlDepth.FromStep(r), Path.GetFileNameWithoutExtension(path));
                if (r.FailedFaces > 0)
                    _file.Text += $" ⚠ {r.FailedFaces} of {r.Faces} faces couldn't be read and are missing ({r.Problems.FirstOrDefault()}).";
            }
            else Load(await Task.Run(() => StlDepth.Load(path)), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { _file.Text = $"Couldn't read it: {e.Message}"; }
    }

    SliceOptions Options() => new()
    {
        Up = (SliceUp)Math.Max(0, _up.SelectedIndex),
        TurnDeg = (double)(_turn.Value ?? 0),
        HeightMm = (double)(_height.Value ?? 60),
        WidthMm = (double)(_width.Value ?? 0),
        DepthMm = (double)(_depth.Value ?? 0),
        ThicknessMm = (double)(_thick.Value ?? 3),
        Outline = (SliceOutline)Math.Max(0, _outline.SelectedIndex),
        DowelMm = (double)(_dowel.Value ?? 0),
        PinMm = (double)(_pin.Value ?? 0),
        KeepJoined = _joined.IsChecked == true,
        Method = (SliceMethod)Math.Max(0, _method.SelectedIndex),
        SheetsX = (int)(_sheetsX.Value ?? 6),
        SheetsY = (int)(_sheetsY.Value ?? 4),
        Fins = (int)(_fins.Value ?? 12),
        Rings = (int)(_rings.Value ?? 3),
        CoreMm = (double)(_core.Value ?? 6),
        SlotFitMm = (double)(_fit.Value ?? 0.1m),
        ShapeStepMm = _shape.IsChecked == true && (int)Math.Max(0, _method.SelectedIndex) == (int)SliceMethod.Stacked ? (double)(_step.Value ?? 0) : 0,
    };

    /// <summary>What edge shaping will do, in a sentence or two.</summary>
    static string Shaping(SliceStack stack, SliceOptions o)
    {
        var shaped = stack.Parts.Where(p => p.Shaping.Count > 0).ToList();
        int steps = shaped.Sum(p => p.Shaping.Count), deepest = shaped.Select(p => p.Shaping.Count).DefaultIfEmpty(0).Max();
        int under = stack.Parts.Count(p => p.UndersideMm2 > 5);
        string text = shaped.Count == 0
            ? "\nEdge shaping: no slopes to engrave (the model doesn't narrow going up inside any slice)."
            : $"\nEdge shaping: {steps} engraving step{(steps == 1 ? "" : "s")} on {shaped.Count} part{(shaped.Count == 1 ? "" : "s")}, at most {deepest} deep ({deepest * o.ShapeStepMm:0.##} mm of {o.ThicknessMm:0.##}).";
        return text + (under > 0 ? $" {under} part{(under == 1 ? " has" : "s have")} slopes underneath (the model narrows downward there): sand those." : "");
    }

    /// <summary>The library's cut and engrave settings to offer.</summary>
    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Doc.Materials.Material, Doc.Materials.Recipe)?)(m, r)))];
        _cutWith.ItemsSource = _cuts.Select(c => c is { } x
            ? $"{x.M.Label} · {x.R.Title}" + (x.R.SlotFitMm is { } f ? $" (fit {f:+0.##;-0.##;0} mm)" : "")
            : "As set on the cut layer").ToList();
        _cutWith.SelectedIndex = 0;
        _pinCutWith.ItemsSource = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the pins' cut layer").ToList();
        _pinCutWith.SelectedIndex = 0;
        _etches = [.. mats.SelectMany(m => m.Recipes.Where(r => r.EtchMmPerPass is > 0).Select(r => (m, r)))];
        _etchWith.ItemsSource = _etches.Count == 0 ? ["No etch-rate results yet"] : _etches.Select(x => $"{x.M.Label} · {x.R.Title} ({x.R.EtchMmPerPass:0.###} mm a pass)").ToList();
        _etchWith.SelectedIndex = 0;
        _etchWith.IsEnabled = _etches.Count > 0;
        _step.Value = _etches.Count > 0 && _etches[0].R.EtchMmPerPass is { } e ? (decimal)e : 0.15m;
        ShowShaping();
    }

    void ShowShaping()
    {
        bool on = _shape.IsChecked == true;
        _etchWith.IsVisible = _step.IsVisible = _shapeNote.IsVisible = on;
        // (The fields' labels go with them.)
        if (_etchWith.Parent is Control f1) f1.IsVisible = on;
        if (_step.Parent is Control f2) f2.IsVisible = on;
        _shapeNote.Text = _etches.Count == 0
            ? "Measure the depth a pass first (Laser → Etch rate test, saved to the library); until then the depth here is a guess, and the shaping will be too deep or too shallow."
            : "Engraved before the cut, on its own layer (\"Slices · shaping\", one pass a step). Where the model narrows downward the steps are underneath: those are sanded.";
    }

    /// <summary>The library's cut settings chosen for the dowel, if any.</summary>
    (Doc.Materials.Material M, Doc.Materials.Recipe R)? PinCut() =>
        _pinCutWith.SelectedIndex > 0 && _pinCutWith.SelectedIndex < _cuts.Count ? _cuts[_pinCutWith.SelectedIndex] : null;

    /// <summary>The pins to cut from the dowel for the slices as they are now (null = not asked for, or no pin holes).</summary>
    DowelPinPlan? PinPlan()
    {
        if (_cutPins.IsChecked != true || _stack is not { } stack || (double)(_pin.Value ?? 0) <= 0 || _method.SelectedIndex != (int)SliceMethod.Stacked) return null;
        double kerf = PinCut() is { } c && c.R.Settings.KerfMm > 0 ? c.R.Settings.KerfMm : 0.2;
        return DowelPins.Plan(new DowelPinOptions
        {
            PinsNeeded = stack.Pins.Count,
            SpareShare = (double)(_pinSpare.Value ?? 10) / 100,
            ThicknessMm = (double)(_thick.Value ?? 3),
            ShorterShare = (double)(_pinShorter.Value ?? 15) / 100,
            DowelDiameterMm = (double)(_pinDowel.Value ?? 2),
            DowelLengthMm = (double)(_pinDowelLength.Value ?? 300),
            KerfMm = kerf,
        }, PinRoomMm);
    }

    /// <summary>How much dowel fits along the bed's longer side, inside the margins.</summary>
    double PinRoomMm => Math.Max(_machine.FieldW, _machine.FieldH) - 20;

    /// <summary>The dowel settings shown only when they're wanted, and the preview and what it makes, spelled out.</summary>
    void Pins()
    {
        // (Not until the window's built.)
        if (_pinPanel is null) return;
        bool on = _cutPins.IsChecked == true;
        _pinPanel.IsVisible = on;
        _cutPins.IsVisible = (double)(_pin.Value ?? 0) > 0;
        DowelPinPlan? plan = null;
        try { plan = PinPlan(); } catch (ArgumentException) { }
        double dia = (double)(_pinDowel.Value ?? 2), length = (double)(_pinDowelLength.Value ?? 300);
        _pinPreview.Show(plan, dia, length);
        if (plan is null) { _pinNote.Text = _stack is null ? "Open a model first: the pins needed come from its slices." : ""; return; }
        int needed = _stack!.Pins.Count, spare = plan.Wanted - needed;
        var note = needed == 0
            ? "These slices have no pin holes, so there are no pins to cut."
            : $"{needed} pin{(needed == 1 ? "" : "s")} needed, {spare} spare: {plan.Wanted} to cut, each {plan.PinLengthMm:0.##} mm long (two slices are {2 * (double)(_thick.Value ?? 3):0.##} mm). " +
              $"{plan.CutsAtMm.Count} cuts, one {(plan.CutsAtMm.Count > plan.Made ? "squaring the dowel's end and one " : "")}after each pin, allowing {(PinCut() is { } c && c.R.Settings.KerfMm > 0 ? $"your {c.R.Settings.KerfMm:0.###} mm kerf" : "0.2 mm for the kerf")}.";
        if (plan.Short)
            note += length > PinRoomMm
                ? $"\n⚠ Only {plan.Made} fit along the bed ({PinRoomMm:0} mm of dowel at a time): run the job, slide the dowel along, square its end and run it again."
                : $"\n⚠ The {length:0} mm dowel makes only {plan.Made} of the {plan.Wanted}: cut the rest from another dowel (or fewer spares).";
        if (Math.Abs(dia - (double)(_pin.Value ?? 0)) > 0.05)
            note += $"\n⚠ The dowel is {dia:0.##} mm but the pin holes are {(double)(_pin.Value ?? 0):0.##} mm: they won't fit snugly. Set the pin holes to the dowel's size.";
        _pinNote.Text = note + "\nLay the dowel along the bed's long side, taped down so the pins can't roll, and set the distance to its top. Then move the cuts onto it in the camera picture.";
    }

    /// <summary>Cut settings chosen from the library: the material's thickness and the cut test's slot fit come with them.</summary>
    void CutChosen()
    {
        if (_cutWith.SelectedIndex <= 0 || _cutWith.SelectedIndex >= _cuts.Count || _cuts[_cutWith.SelectedIndex] is not { } c) { _cutNote.Text = ""; return; }
        var notes = new List<string>();
        if (c.M.ThicknessMm is { } t) { _thick.Value = (decimal)t; notes.Add($"material {t:0.##} mm"); }
        if (c.R.Settings.KerfMm > 0) notes.Add($"kerf {c.R.Settings.KerfMm:0.###} mm on the cut layer");
        if (c.R.SlotFitMm is { } fit) { _fit.Value = (decimal)fit; notes.Add($"slot fit {fit:+0.##;-0.##;0} mm from your cut test"); }
        _cutNote.Text = notes.Count > 0 ? $"From {c.M.Label}: {string.Join(", ", notes)}." : "";
        Later();
    }

    async Task Reslice()
    {
        if (_mesh is not { } mesh) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var o = Options();
        _summary.Text = "Slicing…";
        _make.IsEnabled = false;
        try
        {
            var stack = await Task.Run(() => StackSlicer.Slice(mesh, o, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _stack = stack;
            Pins();
            _view.Show(stack, o.ThicknessMm);
            // The range slider spans the new stack (all of it, unless a range was already picked within it).
            bool all = _to.Value >= _to.Maximum && _from.Value <= 1;
            _from.Maximum = _to.Maximum = Math.Max(1, stack.Slices);
            if (all || _to.Value > stack.Slices) _to.Value = stack.Slices;
            if (_from.Value > _to.Value) _from.Value = 1;
            Range(true);
            var biggest = stack.Parts.Select(p => p.Outline.Bounds()).Where(b => !b.IsEmpty).DefaultIfEmpty(Rect2.Empty).MaxBy(b => b.Width * b.Height);
            int parts = stack.Parts.Count;
            // Against the board chosen (its biggest piece), or the bed; a quarter turn is allowed.
            var room = _stocks[Math.Max(0, _stock.SelectedIndex)].Outline is { } stockOutline
                ? stockOutline.Subpaths.Where(sp => sp.Closed).Select(sp => new PhotonCanvas.Doc.PathGeometry([sp]).Bounds()).MaxBy(b => b.Width * b.Height)
                : new Rect2(0, 0, _machine.FieldW, _machine.FieldH);
            double roomLong = Math.Min(Math.Max(room.Width, room.Height), Math.Max(_machine.FieldW, _machine.FieldH)), roomShort = Math.Min(Math.Min(room.Width, room.Height), Math.Min(_machine.FieldW, _machine.FieldH));
            bool fits = stack.Parts.All(p => p.Outline.Bounds() is var b && Math.Max(b.Width, b.Height) + 6 <= roomLong && Math.Min(b.Width, b.Height) + 6 <= roomShort);
            string where = room.Width >= _machine.FieldW && room.Height >= _machine.FieldH ? "the bed" : "the material";
            if (o.Method != SliceMethod.Stacked)
            {
                int slots = stack.Parts.Sum(p => p.Notes.Count) / 2;
                _summary.Text = $"{parts} sheet{(parts == 1 ? "" : "s")} of {o.ThicknessMm:0.##} mm, {stack.ModelWidthMm:0.#} × {stack.ModelDepthMm:0.#} × {stack.StackHeightMm:0.#} mm overall, " +
                                $"slotted together at {slots} crossing{(slots == 1 ? "" : "s")} (slots {o.ThicknessMm + o.SlotFitMm:0.##} mm wide). " +
                                (o.Method == SliceMethod.EggCrate
                                    ? "Each slot is marked with the sheet that goes in it: stand the X sheets up and slide the Y sheets down onto them from the top."
                                    : "Each slot is marked with the part that goes in it: hold the rings at their heights and push each fin in from the side, towards the middle" + (o.CoreMm > 0 ? $", with a {o.CoreMm:0.#} mm dowel up the middle." : ".")) +
                                (stack.LeftOut > 0 ? $" {stack.LeftOut} tiny piece{(stack.LeftOut == 1 ? " was" : "s were")} left out." : "") +
                                (fits ? "" : $"\n⚠ The biggest sheet ({biggest.Width:0} × {biggest.Height:0} mm) doesn't fit on {where}: make the model smaller.");
                _make.IsEnabled = fits && parts > 0;
                return;
            }
            _summary.Text = $"{stack.Slices} slices of {o.ThicknessMm:0.##} mm: the stack stands {stack.StackHeightMm:0.#} mm tall, {stack.ModelWidthMm:0.#} × {stack.ModelDepthMm:0.#} mm across. " +
                            $"{parts} part{(parts == 1 ? "" : "s")} to cut" + (parts > stack.Slices ? " (some slices are in several pieces)" : "") + "." +
                            (stack.LeftOut > 0 ? $" {stack.LeftOut} sliver{(stack.LeftOut == 1 ? " was" : "s were")} too small to cut and left out." : "") +
                            (stack.Joined > 0 ? $" {stack.Joined} steep spot{(stack.Joined == 1 ? " was" : "s were")} widened so the slices there overlap: sand back to the shape." : "") +
                            (stack.Parts.Count(p => p.Floating) is var fl and > 0 ? $"\n⚠ {fl} part{(fl == 1 ? "" : "s")} start in mid-air with nothing under them (red in the preview): separate pieces of the model, to glue on by eye or leave out." : "") +
                            (o.PinMm > 0 ? $" {stack.Pins.Count} pins of {o.PinMm:0.#} mm join the slices, each up to {2 * o.ThicknessMm:0.#} mm long." : "") +
                            (o.DowelMm > 0 ? stack.Dowels.Count == 2 ? $" Two {o.DowelMm:0.#} mm dowel holes through every slice." : " No room for dowel holes in every slice." : "") +
                            (o.ShapeStepMm > 0 ? Shaping(stack, o) : "") +
                            (fits ? "" : $"\n⚠ The biggest slice ({biggest.Width:0} × {biggest.Height:0} mm) doesn't fit on {where}: make the model smaller.");
            _make.IsEnabled = fits && parts > 0;
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e) { _summary.Text = e.Message; }
        catch (Exception e)
        {
            // Never leave the window silently blank: say what went wrong, and keep the details.
            AppLog.Error("slicing", e);
            _summary.Text = $"Couldn't slice this model: {e.Message}";
        }
    }

    void Make()
    {
        if (_stack is not { } stack) return;
        try
        {
            var stock = _stocks[Math.Max(0, _stock.SelectedIndex)];
            // Edge shaping: engraved with the chosen settings, a pass a step.
            Layer? shaping = null;
            if (_shape.IsChecked == true && stack.Parts.Any(p => p.Shaping.Count > 0))
                shaping = _etchWith.SelectedIndex >= 0 && _etchWith.SelectedIndex < _etches.Count
                    ? _etches[_etchWith.SelectedIndex].R.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill))
                    : Layer.Default(0, LayerMode.Fill);
            // The slices go on a page of their own when this one already has something on it (on a bed), then a page a bed.
            int firstPage = _doc.State.PageIndex + (stock.Outline is null && _doc.State.Shapes.Count > 0 ? 1 : 0);
            var (s, sheets) = StackLayout.Place(_doc.State, stack, _machine, _numbers.IsChecked == true, _marks.IsChecked == true, title: _name, material: stock.Outline, shaping: shaping);
            // The cut settings from the library (kerf included) on the cut layer, on every page the slices went on.
            if (_cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex < _cuts.Count && _cuts[_cutWith.SelectedIndex] is { } c)
                s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == "Slices · cut",
                    l => c.R.Settings.ApplyTo(l) with { Material = new Doc.Materials.MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) });
            // The pins from the dowel: a page after the slices' last one, the slices' first page still shown.
            string pinsMade = "";
            if (PinPlan() is { Made: > 0 } plan)
            {
                int slicesShown = s.PageIndex;
                s = DowelPins.Place(Pages.SwitchTo(s, slicesShown + sheets - 1), plan, (double)(_pinDowel.Value ?? 2), (double)(_pinDowelLength.Value ?? 300), _machine, title: $"{_name} pins");
                // The kerf is allowed for in the spacing (lines are cut as drawn), so none on the layer.
                if (PinCut() is { } pc)
                    s = Pages.UpdateLayers(s, s.PageIndex, 1, l => l.Name == DowelPins.CutLayer,
                        l => pc.R.Settings.ApplyTo(l) with { KerfMm = 0, Material = new Doc.Materials.MaterialRef(pc.M.Id, pc.M.Label, pc.R.Id, pc.R.Title) });
                s = Pages.SwitchTo(s, slicesShown);
                pinsMade = $" The {plan.Made} pins are on their own page, \"{_name} pins\" (layer \"{DowelPins.CutLayer}\"): move the cuts onto the dowel in the camera picture.";
            }
            _doc.Commit($"Stacked slices of {_name}", s, []);
            _summary.Text = $"Made {stack.Parts.Count} parts on {sheets} {(stock.Outline is null ? "bed" : "board")}{(sheets == 1 ? "" : "s")}" + (sheets > 1 ? " (a page each: the tabs above the work area" + (stock.Outline is null ? ")." : ", each with the same material outline).") : ".") +
                            " The cut layer is \"Slices · cut\" (set its kerf for snug dowels), the light marks \"Slices · marks\"." + pinsMade;
            if (_booklet.IsChecked == true && stack.Method == SliceMethod.Stacked) _ = Booklet(stack, PinPlan() is { Made: > 0 } bp ? bp.PinLengthMm : null);
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }

    /// <summary>Where the booklet goes: beside the design when it's saved, else in the data folder's Booklets.</summary>
    string BookletPath()
    {
        var name = string.Concat((string.IsNullOrWhiteSpace(_name) ? "Slices" : _name).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dir = _doc.FilePath is { } saved && Path.GetDirectoryName(saved) is { Length: > 0 } d ? d : Path.Combine(AppIdentity.DataDir, "Booklets");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name} assembly.pdf");
    }

    /// <summary>Writes the assembly booklet (in the background: a tall model has many pages) and says where.</summary>
    async Task Booklet(SliceStack stack, double? pinLength)
    {
        var o = new BookletOptions
        {
            Title = string.IsNullOrWhiteSpace(_name) ? "Slices" : _name,
            PinMm = (double)(_pin.Value ?? 0),
            PinLengthMm = pinLength,
            DowelMm = (double)(_dowel.Value ?? 0),
            Outline = (SliceOutline)Math.Max(0, _outline.SelectedIndex),
            View = (BookletView)Math.Max(0, _bookletView.SelectedIndex),
            TurnDeg = (double)(_turn.Value ?? 0),
        };
        try
        {
            var path = BookletPath();
            int pages = await Task.Run(() => AssemblyBooklet.SavePdf(stack, o, path));
            _bookletPath = path;
            _openBooklet.IsVisible = true;
            _summary.Text += $"\nAssembly booklet ({pages} pages): {path}";
        }
        catch (IOException e) { _summary.Text += $"\nCouldn't save the assembly booklet (is it open in a PDF viewer?): {e.Message}"; }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Error("assembly booklet", e);
            _summary.Text += $"\nCouldn't make the assembly booklet: {e.Message}";
        }
    }
}
