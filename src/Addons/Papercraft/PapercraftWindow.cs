using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.App;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Papercraft;

/// <summary>
/// Fold-up papercraft: open a 3D model, choose its size and how simple, and get it unfolded into
/// flat pieces of card with cut outlines, fold lines (ridges solid, valleys dashed), glue tabs and
/// numbered edges, laid out on the bed.
/// </summary>
public sealed class PapercraftWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    List<StlDepth.Triangle>? _mesh;
    string _name = "";
    PaperNet? _net;
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    readonly PaperNetView _view = new() { MinHeight = 380 };
    readonly TextBlock _file = new() { Text = "No model yet.", TextWrapping = TextWrapping.Wrap };
    readonly NumericUpDown _size = Number(10, 2000, 10, "0.#"), _faces = Number(0, 5000, 50, "0"), _tab = Number(0, 30, 1, "0.#"), _flat = Number(0, 45, 1, "0.#");
    readonly CheckBox _numbers = new() { Content = "Number the edges that glue together", IsChecked = true };
    readonly ComboBox _stock = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly List<MaterialChoice> _stocks;
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _make = new() { Content = "Make the papercraft", Classes = { "primary" }, IsEnabled = false, MinWidth = 170 };

    public PapercraftWindow(Document doc, MachineProfile machine)
    {
        _doc = doc;
        _machine = machine;
        Title = "Fold-up papercraft";
        Width = 1100;
        Height = 720;
        MinWidth = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _size.Value = 120; _faces.Value = 300; _tab.Value = 6; _flat.Value = 2;
        foreach (var n in new[] { _size, _faces, _tab, _flat }) n.ValueChanged += (_, _) => Later();
        (_stocks, int first) = MaterialChoice.For(doc.State, machine.FieldW, machine.FieldH);
        _stock.ItemsSource = _stocks.Select(c => c.Label).ToList();
        _stock.SelectedIndex = first;
        _stock.SelectionChanged += (_, _) => Later();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Unfold(); };

        var open = Button("Open a 3D model…", tip: "STL or OBJ from a 3D printing site or Blender; STEP straight from CAD. Low-poly models fold up best.");
        open.Click += async (_, _) => await Open();
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
                Field("Size (mm)", _size, tip: "The finished model's longest side."),
                Field("Faces at most", _faces, tip: "Make the model simpler until it has no more than this many flat faces (0 = as it is). Fewer faces: fewer, bigger pieces and less folding; more: rounder."),
                Hint("A few hundred faces is plenty to fold by hand. Detailed models from printing sites have tens of thousands."),
                Field("Glue tabs (mm)", _tab, tip: "How deep the glue tabs are. One edge of each pair gets a tab; it's made smaller, or put on the other edge, where it would overlap the piece. 0 = no tabs."),
                Field("Flat below (°)", _flat, tip: "Faces that bend less than this are treated as one flat face: no fold line between them."),
                _numbers,
                Hint("Matching edges carry the same number: glue the tab on one under the edge with the same number on the other."),
                Field("Put on", _stock, tip: "The whole bed, or a sheet you've outlined on the work area (Camera → Outline material)."),
                _summary,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _make } };
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16,
            Children = { Key("Cut", PaperNetView.CutColour, false), Key("Mountain fold (ridge)", PaperNetView.MountainColour, false), Key("Valley fold (dashed)", PaperNetView.ValleyColour, true), Key("Glue tab", PaperNetView.TabColour, false) },
        };
        // The key sits on the white of the preview, so the black and beige read.
        legend.Margin = new(12, 0, 12, 10);
        var paper = new DockPanel();
        DockPanel.SetDock(legend, Dock.Bottom);
        paper.Children.Add(legend);
        paper.Children.Add(_view);
        var left = new Border { CornerRadius = new(8), ClipToBounds = true, Background = Brushes.White, Child = paper };
        var grid = new Grid { ColumnDefinitions = new("*,16,340") };
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

    static Control Key(string text, Color colour, bool dashed) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 6,
        Children =
        {
            new Avalonia.Controls.Shapes.Line { StartPoint = new(0, 7), EndPoint = new(22, 7), Stroke = new SolidColorBrush(colour), StrokeThickness = dashed ? 2 : colour == PaperNetView.TabColour ? 8 : 2, StrokeDashArray = dashed ? [3, 2] : null, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, Foreground = Brushes.DimGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>Show a model already read (and unfold it).</summary>
    public void Load(List<StlDepth.Triangle> mesh, string name)
    {
        _mesh = mesh;
        _name = name;
        _file.Text = $"{name}: {mesh.Count:N0} triangles.";
        Later();
    }

    async Task Open()
    {
        // A plain file picker: the slicer's model browser belongs to that add-on, and this one stands alone.
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "3D model to unfold",
            FileTypeFilter = [new FilePickerFileType("3D models (STL, OBJ, STEP)") { Patterns = ["*.stl", "*.obj", "*.stp", "*.step"] }],
        });
        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null) return;
        _file.Text = $"Reading {Path.GetFileName(path)}…";
        try
        {
            if (Path.GetExtension(path).ToLowerInvariant() is ".stp" or ".step")
            {
                var r = await Task.Run(() => Doc.Import.Step.StepMesh.Load(path));
                Load(StlDepth.FromStep(r), Path.GetFileNameWithoutExtension(path));
                if (r.FailedFaces > 0)
                    _file.Text += $" ⚠ {r.FailedFaces} of {r.Faces} faces couldn't be read and are missing ({r.Problems.FirstOrDefault()}).";
            }
            else Load(await Task.Run(() => StlDepth.Load(path)), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { _file.Text = $"Couldn't read it: {e.Message}"; }
    }

    void Later()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    PapercraftOptions Options()
    {
        // Pieces fit the bed (or the outlined sheet) with a margin.
        var sheet = _stocks[Math.Max(0, _stock.SelectedIndex)].Outline?.Bounds() is { IsEmpty: false } b ? (b.Width, b.Height) : (_machine.FieldW, _machine.FieldH);
        return new PapercraftOptions
        {
            SizeMm = (double)(_size.Value ?? 120),
            MaxFaces = (int)(_faces.Value ?? 300),
            TabMm = (double)(_tab.Value ?? 6),
            FlatDeg = (double)(_flat.Value ?? 2),
            MaxWidthMm = sheet.Item1 - 6,
            MaxHeightMm = sheet.Item2 - 6,
        };
    }

    async Task Unfold()
    {
        if (_mesh is not { } mesh) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var o = Options();
        _make.IsEnabled = false;
        _summary.Text = "Unfolding…";
        try
        {
            var net = await Task.Run(() => Unfolder.Unfold(mesh, o, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _net = net;
            _view.Net = net;
            _summary.Text = Summary(net, o);
            _make.IsEnabled = net.Pieces.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException e) { _net = null; _view.Net = null; _summary.Text = e.Message; }
    }

    /// <summary>What you'll get, in a few sentences.</summary>
    public static string Summary(PaperNet net, PapercraftOptions o)
    {
        string faces = net.Faces < net.SourceTriangles ? $"{net.Faces:N0} faces (simplified from {net.SourceTriangles:N0})" : $"{net.Faces:N0} faces";
        var text = $"{net.ModelX:0} × {net.ModelY:0} × {net.ModelZ:0} mm, {faces}: {net.Pieces.Count} piece{(net.Pieces.Count == 1 ? "" : "s")}, {net.Folds} folds, {net.Joins} edges to glue.";
        if (o.TabMm > 0 && net.MissingTabs > 0)
            text += $" {net.MissingTabs} join{(net.MissingTabs == 1 ? " has" : "s have")} no room for a tab: tape those, or glue a strip behind.";
        if (net.OpenEdges > 0)
            text += $" The model has {net.OpenEdges} open edge{(net.OpenEdges == 1 ? "" : "s")} (holes in its surface): those stay open.";
        if (net.OddEdges > 0)
            text += $" {net.OddEdges} edge{(net.OddEdges == 1 ? " joins" : "s join")} more than two faces: those are cut and left unnumbered.";
        if (net.Pieces.Count > 40)
            text += " That's a lot of pieces: fewer faces makes it quicker to build.";
        return text;
    }

    void Make()
    {
        if (_net is not { } net) return;
        try
        {
            var stock = _stocks[Math.Max(0, _stock.SelectedIndex)];
            var (s, sheets) = PapercraftLayout.Place(_doc.State, net, _machine, _numbers.IsChecked == true, title: string.IsNullOrWhiteSpace(_name) ? "Papercraft" : _name, material: stock.Outline);
            _doc.Commit($"Papercraft of {_name}", s, []);
            _summary.Text = $"Made {net.Pieces.Count} pieces on {sheets} {(stock.Outline is null ? "bed" : "sheet")}{(sheets == 1 ? "" : "s")}" + (sheets > 1 ? " (a page each: the tabs above the work area)." : ".") +
                            $" Set the layers for your card: \"{PapercraftLayout.CutLayer}\" cuts through, \"{PapercraftLayout.MountainLayer}\" and \"{PapercraftLayout.ValleyLayer}\" only score it, \"{PapercraftLayout.NumbersLayer}\" marks lightly.";
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }
}

/// <summary>The unfolded pieces, laid out in rows to look at: cut lines, folds and tabs.</summary>
public sealed class PaperNetView : Control
{
    public static readonly Color CutColour = Color.FromRgb(0x20, 0x20, 0x20);
    public static readonly Color MountainColour = Color.FromRgb(0xD3, 0x2F, 0x2F);
    public static readonly Color ValleyColour = Color.FromRgb(0x1E, 0x63, 0xC9);
    public static readonly Color TabColour = Color.FromRgb(0xE8, 0xDF, 0xC8);

    PaperNet? _net;
    List<(PaperPiece Piece, Vec2 At)> _placed = [];
    Rect2 _all = Rect2.Empty;

    public PaperNet? Net
    {
        get => _net;
        set { _net = value; Arrange(); InvalidateVisual(); }
    }

    /// <summary>Simple shelves: biggest first, rows about as wide as the whole is tall.</summary>
    void Arrange()
    {
        _placed = [];
        _all = Rect2.Empty;
        if (_net is null || _net.Pieces.Count == 0) return;
        const double gap = 4;
        var pieces = _net.Pieces.OrderByDescending(p => p.Bounds.Height).ToList();
        double area = pieces.Sum(p => (p.Bounds.Width + gap) * (p.Bounds.Height + gap));
        double rowWidth = Math.Max(pieces.Max(p => p.Bounds.Width), Math.Sqrt(area * 1.4));
        double x = 0, y = 0, rowH = 0;
        foreach (var p in pieces)
        {
            var b = p.Bounds;
            if (x > 0 && x + b.Width > rowWidth) { x = 0; y += rowH + gap; rowH = 0; }
            _placed.Add((p, new Vec2(x - b.MinX, y - b.MinY)));
            _all = _all.Union(new Rect2(x, y, x + b.Width, y + b.Height));
            x += b.Width + gap;
            rowH = Math.Max(rowH, b.Height);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        if (_placed.Count == 0 || _all.IsEmpty)
        {
            var hint = new FormattedText("Open a 3D model to see it unfolded.", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Gray);
            ctx.DrawText(hint, new Point((bounds.Width - hint.Width) / 2, (bounds.Height - hint.Height) / 2));
            return;
        }
        double pad = 12;
        double k = Math.Min((bounds.Width - 2 * pad) / Math.Max(_all.Width, 1), (bounds.Height - 2 * pad) / Math.Max(_all.Height, 1));
        var origin = new Point(pad + (bounds.Width - 2 * pad - _all.Width * k) / 2, pad + (bounds.Height - 2 * pad - _all.Height * k) / 2);
        Point P(Vec2 v, Vec2 at) => new(origin.X + (v.X + at.X) * k, origin.Y + (v.Y + at.Y) * k);
        var cut = new Pen(new SolidColorBrush(CutColour), 1);
        var faceFill = new SolidColorBrush(Color.FromRgb(0xFB, 0xF8, 0xF1));
        var tabFill = new SolidColorBrush(TabColour);
        var mountain = new Pen(new SolidColorBrush(MountainColour), 1);
        var valley = new Pen(new SolidColorBrush(ValleyColour), 1) { DashStyle = new DashStyle([4, 3], 0) };
        foreach (var (piece, at) in _placed)
        {
            foreach (var t in piece.Tabs)
                ctx.DrawGeometry(tabFill, cut, Polygon(t.Outline.Select(v => P(v, at))));
            foreach (var (a, b, c) in piece.Faces)
                ctx.DrawGeometry(faceFill, null, Polygon([P(a, at), P(b, at), P(c, at)]));
            foreach (var e in piece.Edges) ctx.DrawLine(cut, P(e.A, at), P(e.B, at));
            foreach (var f in piece.Folds) ctx.DrawLine(f.Kind == FoldKind.Mountain ? mountain : valley, P(f.A, at), P(f.B, at));
        }
    }

    static StreamGeometry Polygon(IEnumerable<Point> pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        bool first = true;
        foreach (var p in pts)
        {
            if (first) { c.BeginFigure(p, true); first = false; }
            else c.LineTo(p);
        }
        if (!first) c.EndFigure(true);
        return g;
    }
}
