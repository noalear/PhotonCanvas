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

    readonly StackView _view = new() { MinHeight = 420 };
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
                _numbers, _marks,
                Field("Pin holes Ø", _pin, tip: "Matching holes wherever a part sits on the one below, for short pins (toothpicks, skewers, brads) that line them up. Placed where the slices around them hide the holes when they can. 0 = none."),
                Hint("A toothpick is about 2 mm, a bamboo skewer 3 mm. Set the cut layer's kerf so the pins fit snugly; pins need only be twice the material thick."),
                Field("Dowels Ø", _dowel, tip: "Two holes through every slice, for long dowels through the whole stack. Only where every slice has room. 0 = none."),
                _summary,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _make } };
        var grid = new Grid { ColumnDefinitions = new("*,16,360") };
        grid.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _view });
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
            var biggest = stack.Parts.Select(p => p.Outline.Bounds()).Where(b => !b.IsEmpty).DefaultIfEmpty(Rect2.Empty).MaxBy(b => b.Width * b.Height);
            int parts = stack.Parts.Count;
            bool fits = stack.Parts.All(p => p.Outline.Bounds() is var b && b.Width + 6 <= _machine.FieldW && b.Height + 6 <= _machine.FieldH);
            _summary.Text = $"{stack.Slices} slices of {o.ThicknessMm:0.##} mm: the stack stands {stack.StackHeightMm:0.#} mm tall, {stack.ModelWidthMm:0.#} × {stack.ModelDepthMm:0.#} mm across. " +
                            $"{parts} part{(parts == 1 ? "" : "s")} to cut" + (parts > stack.Slices ? " (some slices are in several pieces)" : "") + "." +
                            (stack.LeftOut > 0 ? $" {stack.LeftOut} sliver{(stack.LeftOut == 1 ? " was" : "s were")} too small to cut and left out." : "") +
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

/// <summary>The slices stacked up in 3D, as they'll be glued. Drag to turn.</summary>
public sealed class StackView : Control
{
    SliceStack? _stack;
    double _t = 3, _yaw = -30, _pitch = 25;
    Point? _drag;

    public void Show(SliceStack stack, double thickness)
    {
        _stack = stack;
        _t = thickness;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = e.GetPosition(this); e.Pointer.Capture(this); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = null; e.Pointer.Capture(null); }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is not { } from) return;
        var p = e.GetPosition(this);
        _yaw += (p.X - from.X) * 0.5;
        _pitch = Math.Clamp(_pitch + (p.Y - from.Y) * 0.4, -10, 89);
        _drag = p;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(b.Size));
        if (_stack is not { } st)
        {
            var hint = new FormattedText("Open a 3D model to see it in slices.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, UiKit.Brush("TextMuted"));
            ctx.DrawText(hint, new Point((b.Width - hint.Width) / 2, b.Height / 2 - 8));
            return;
        }
        double cx = st.ModelWidthMm / 2, cy = st.ModelDepthMm / 2, cz = st.StackHeightMm / 2;
        double span = Math.Max(Math.Max(st.ModelWidthMm, st.ModelDepthMm), st.StackHeightMm);
        double scale = Math.Min(b.Width, b.Height) * 0.8 / Math.Max(1, span);
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        Point P(Vec2 xy, double z)
        {
            double x = xy.X - cx, y = xy.Y - cy, zz = z - cz;
            double rx = x * Math.Cos(yaw) - y * Math.Sin(yaw), ry = x * Math.Sin(yaw) + y * Math.Cos(yaw);
            return new Point(b.Width / 2 + rx * scale, b.Height / 2 + (ry * Math.Sin(pitch) - zz * Math.Cos(pitch)) * scale);
        }
        // Bottom to top, each slice's top and bottom edge, alternating shades like plywood.
        for (int i = 0; i < st.Layers.Count; i++)
        {
            var loops = st.Layers[i].Flatten(0.2).Where(f => f.Closed).Select(f => f.Points).ToList();
            var shade = i % 2 == 0 ? Color.Parse("#C9A27A") : Color.Parse("#A9825C");
            var pen = new Pen(new SolidColorBrush(shade), 1.2);
            var faint = new Pen(new SolidColorBrush(Color.FromArgb(90, shade.R, shade.G, shade.B)), 1);
            double z0 = i * _t, z1 = (i + 1) * _t;
            foreach (var l in loops)
            {
                for (int k = 0; k < l.Count; k++)
                {
                    var a = l[k];
                    var c = l[(k + 1) % l.Count];
                    ctx.DrawLine(pen, P(a, z1), P(c, z1));
                    ctx.DrawLine(faint, P(a, z0), P(c, z0));
                }
                // A few uprights so the thickness shows.
                int step = Math.Max(1, l.Count / 12);
                for (int k = 0; k < l.Count; k += step) ctx.DrawLine(faint, P(l[k], z0), P(l[k], z1));
            }
        }
        var label = new FormattedText($"{st.Slices} slices · drag to turn", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(label, new Point(8, b.Height - 20));
    }
}
