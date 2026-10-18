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
    readonly Slider _from = new() { Minimum = 1, Maximum = 1, Value = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider _to = new() { Minimum = 1, Maximum = 1, Value = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly TextBlock _range = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
    readonly TextBlock _file = new() { Text = "No model yet.", TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _up = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Z up (as made)", "Z down", "Y up", "Y down", "X up", "X down" }, SelectedIndex = 0 };
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
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _make = new() { Content = "Make the slices", Classes = { "primary" }, IsEnabled = false, MinWidth = 150 };

    public StackSliceWindow(Document doc, MachineProfile machine)
    {
        _doc = doc;
        _machine = machine;
        Title = "Stacked slices";
        Width = 1100;
        Height = 740;
        MinWidth = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _height.Value = 60; _thick.Value = 3; _dowel.Value = 0; _turn.Value = 0; _pin.Value = 2;

        var open = Button("Open a 3D model…", tip: "STL or OBJ, from a 3D printing site, Fusion, Blender…");
        open.Click += async (_, _) => await Open();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Reslice(); };
        foreach (var n in new[] { _turn, _height, _thick, _dowel, _pin }) n.ValueChanged += (_, _) => Later();
        foreach (var c in new[] { _up, _outline }) c.SelectionChanged += (_, _) => Later();
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
                Field("Height (mm)", _height, tip: "The model's height standing this way up; the other sizes follow."),
                Field("Material (mm)", _thick, tip: "One slice per thickness of material."),
                Field("Outline", _outline),
                Hint("A slice is the same shape top and bottom, but the model changes through it: a pyramid comes out stepped. Outer leaves material to sand back to the shape; Inner never sticks out."),
                _numbers, _marks, _joined,
                Field("Pin holes Ø", _pin, tip: "Matching holes wherever a part sits on the one below, for short pins (toothpicks, skewers, brads) that line them up. Placed where the slices around them hide the holes when they can. 0 = none."),
                Hint("A toothpick is about 2 mm, a bamboo skewer 3 mm. Set the cut layer's kerf so the pins fit snugly; pins need only be twice the material thick."),
                Field("Dowels Ø", _dowel, tip: "Two holes through every slice, for long dowels through the whole stack. Only where every slice has room. 0 = none."),
                _summary,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _make } };
        var grid = new Grid { ColumnDefinitions = new("*,16,360") };
        _look.SelectionChanged += (_, _) => { _view.Look = _look.SelectedIndex == 1 ? StackLook.Lines : StackLook.Solid; _material.IsEnabled = _look.SelectedIndex == 0; };
        _material.SelectionChanged += (_, _) => _view.Material = (StackMaterial)Math.Max(0, _material.SelectedIndex);
        _from.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Range(fromMoved: true); };
        _to.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Range(fromMoved: false); };
        var controls = new StackPanel
        {
            Spacing = 8, Margin = new(0, 8, 0, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "View", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, _look, _material } },
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
    }

    /// <summary>Show a model already read (and slice it).</summary>
    public void Load(List<StlDepth.Triangle> mesh, string name)
    {
        _mesh = mesh;
        _name = name;
        _file.Text = $"{name}: {mesh.Count:N0} triangles.";
        _ = Reslice();
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
        _range.Text = _from.Value <= 1 && _to.Value >= n ? $"All {n} slices" : $"Slices {_from.Value:0}–{_to.Value:0} of {n}";
    }

    void Later()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "3D model to slice",
            FileTypeFilter = [new FilePickerFileType("3D models (STL, OBJ)") { Patterns = ["*.stl", "*.obj"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try { Load(await Task.Run(() => StlDepth.Load(path)), Path.GetFileNameWithoutExtension(path)); }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { _file.Text = $"Couldn't read it: {e.Message}"; }
    }

    SliceOptions Options() => new()
    {
        Up = (SliceUp)Math.Max(0, _up.SelectedIndex),
        TurnDeg = (double)(_turn.Value ?? 0),
        HeightMm = (double)(_height.Value ?? 60),
        ThicknessMm = (double)(_thick.Value ?? 3),
        Outline = (SliceOutline)Math.Max(0, _outline.SelectedIndex),
        DowelMm = (double)(_dowel.Value ?? 0),
        PinMm = (double)(_pin.Value ?? 0),
        KeepJoined = _joined.IsChecked == true,
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
            bool fits = stack.Parts.All(p => p.Outline.Bounds() is var b && b.Width + 6 <= _machine.FieldW && b.Height + 6 <= _machine.FieldH);
            _summary.Text = $"{stack.Slices} slices of {o.ThicknessMm:0.##} mm: the stack stands {stack.StackHeightMm:0.#} mm tall, {stack.ModelWidthMm:0.#} × {stack.ModelDepthMm:0.#} mm across. " +
                            $"{parts} part{(parts == 1 ? "" : "s")} to cut" + (parts > stack.Slices ? " (some slices are in several pieces)" : "") + "." +
                            (stack.LeftOut > 0 ? $" {stack.LeftOut} sliver{(stack.LeftOut == 1 ? " was" : "s were")} too small to cut and left out." : "") +
                            (stack.Joined > 0 ? $" {stack.Joined} steep spot{(stack.Joined == 1 ? " was" : "s were")} widened so the slices there overlap: sand back to the shape." : "") +
                            (stack.Parts.Count(p => p.Floating) is var fl and > 0 ? $"\n⚠ {fl} part{(fl == 1 ? "" : "s")} start in mid-air with nothing under them (red in the preview): separate pieces of the model, to glue on by eye or leave out." : "") +
                            (o.PinMm > 0 ? $" {stack.Pins.Count} pins of {o.PinMm:0.#} mm join the slices, each {2 * o.ThicknessMm:0.#} mm long." : "") +
                            (o.DowelMm > 0 ? stack.Dowels.Count == 2 ? $" Two {o.DowelMm:0.#} mm dowel holes through every slice." : " No room for dowel holes in every slice." : "") +
                            (fits ? "" : $"\n⚠ The biggest slice ({biggest.Width:0} × {biggest.Height:0} mm) doesn't fit on the bed: make the model smaller.");
            _make.IsEnabled = fits && parts > 0;
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e) { _summary.Text = e.Message; }
    }

    void Make()
    {
        if (_stack is not { } stack) return;
        try
        {
            var (s, sheets) = StackLayout.Place(_doc.State, stack, _machine, _numbers.IsChecked == true, _marks.IsChecked == true);
            _doc.Commit($"Stacked slices of {_name}", s, []);
            _summary.Text = $"Made {stack.Parts.Count} parts on {sheets} bed{(sheets == 1 ? "" : "s")}" + (sheets > 1 ? " (a page each: the tabs above the work area)." : ".") +
                            " The cut layer is \"Slices · cut\" (set its kerf for snug dowels), the light marks \"Slices · marks\".";
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }
}
