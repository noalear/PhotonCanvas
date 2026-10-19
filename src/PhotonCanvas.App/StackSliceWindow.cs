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

    public StackSliceWindow(Document doc, MachineProfile machine, AppSettings? settings = null)
    {
        _settings = settings;
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
        _outline.SelectionChanged += (_, _) => Later();
        // Turning or standing it another way changes what X, Y and Z are: sizes follow (height kept).
        _up.SelectionChanged += (_, _) => { Natural(); Resize(_height); };
        _turn.ValueChanged += (_, _) => { Natural(); Resize(_height); };
        foreach (var n in new[] { _width, _depth, _height }) n.ValueChanged += (_, _) => Resize(n);
        _keepProportions.IsCheckedChanged += (_, _) => { if (_keepProportions.IsChecked == true) Resize(_height); };
        _ownSize.Click += (_, _) => OwnSize();
        _joined.IsCheckedChanged += (_, _) => Later();
        ToolTip.SetTip(_joined, "Where the shape slopes steeply (a trunk, an ear), two slices can miss each other even though the model is joined. This adds the shape where they meet to both, so they overlap enough to glue and pin. Sand it back to the shape.");
        _make.Click += (_, _) => Make();
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
                        Hint("A slice is the same shape top and bottom, but the model changes through it: a pyramid comes out stepped. Outer leaves material to sand back to the shape; Inner never sticks out."),
                        _marks, _joined,
                        Field("Pin holes Ø", _pin, tip: "Matching holes wherever a part sits on the one below, for short pins (toothpicks, skewers, brads) that line them up. Placed where the slices around them hide the holes when they can. 0 = none."),
                        Hint("A toothpick is about 2 mm, a bamboo skewer 3 mm. Set the cut layer's kerf so the pins fit snugly; pins need only be twice the material thick."),
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
                Field("Slot fit (mm)", _fit, tip: "Slots are the material's thickness plus this. Raise it if the sheets won't go together, lower it (even below 0) if they're loose. The cut layer's kerf counts too."),
                Field("Cut from", _stock, tip: "Where the parts go. To use the board actually in the machine, outline it first (draw it, or Pick piece from camera), right-click it: Use as material outline. Further beds get the same outline on new pages."),
                _summary,
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
        ShowMethod();
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
    };

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
                                    ? "Each slot is marked with the sheet that goes in it: X sheets slide down from the top onto the Y sheets."
                                    : "Each slot is marked with the part that goes in it: slide the rings down over the fins from the top, smallest first" + (o.CoreMm > 0 ? $", with a {o.CoreMm:0.#} mm dowel up the middle." : ".")) +
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
                            (o.PinMm > 0 ? $" {stack.Pins.Count} pins of {o.PinMm:0.#} mm join the slices, each {2 * o.ThicknessMm:0.#} mm long." : "") +
                            (o.DowelMm > 0 ? stack.Dowels.Count == 2 ? $" Two {o.DowelMm:0.#} mm dowel holes through every slice." : " No room for dowel holes in every slice." : "") +
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
            var (s, sheets) = StackLayout.Place(_doc.State, stack, _machine, _numbers.IsChecked == true, _marks.IsChecked == true, title: _name, material: stock.Outline);
            _doc.Commit($"Stacked slices of {_name}", s, []);
            _summary.Text = $"Made {stack.Parts.Count} parts on {sheets} {(stock.Outline is null ? "bed" : "board")}{(sheets == 1 ? "" : "s")}" + (sheets > 1 ? " (a page each: the tabs above the work area" + (stock.Outline is null ? ")." : ", each with the same material outline).") : ".") +
                            " The cut layer is \"Slices · cut\" (set its kerf for snug dowels), the light marks \"Slices · marks\".";
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }
}
