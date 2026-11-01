using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.App;
using PhotonCanvas.Doc.Tracing;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.DepthOutlines;

/// <summary>
/// Outlines from a depthmap: finds where the surface steps (a face against the leaves, a leaf over
/// a leaf) and turns those edges into lines to mark over the relief, so the engraving keeps its
/// drawing. The lines are one laser line wide, a set width, or swell with the edge. Live preview.
/// </summary>
public sealed class DepthOutlinesWindow : Window
{
    readonly Document _doc;
    readonly ImageShape _picture;
    readonly TracePreview _preview = new() { Marks = true };
    readonly Slider _detail = new() { Minimum = 0.1, Maximum = 2, Value = 0.3 };
    readonly Slider _amount = new() { Minimum = 1, Maximum = 60, Value = 18 };
    readonly Slider _shortest = new() { Minimum = 0, Maximum = 20, Value = 3 };
    readonly Slider _smooth = new() { Minimum = 0, Maximum = 2, Value = 0.5 };
    readonly TextBlock _detailText = Hint(), _amountText = Hint(), _shortestText = Hint(), _smoothText = Hint();
    readonly RadioButton _laserLine = new() { Content = "One laser line (scored)", GroupName = "width" };
    readonly RadioButton _fixed = new() { Content = "All the same width", GroupName = "width" };
    readonly RadioButton _varying = new() { Content = "Swelling and thinning", GroupName = "width", IsChecked = true };
    readonly NumericUpDown _width = Number(0.05m, 3, 0.05m, "0.0#"), _thin = Number(0.05m, 3, 0.05m, "0.0#"), _wide = Number(0.05m, 3, 0.05m, "0.0#");
    readonly ComboBox _follows = new() { ItemsSource = new[] { "the step is steepest", "the surface is highest", "the surface is deepest" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox _taper = new() { Content = "Taper the ends to a point", IsChecked = true };
    readonly CheckBox _showPicture = new() { Content = "Show the depthmap underneath", IsChecked = true };
    readonly TextBlock _stats = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly Button _ok = new() { Content = "Add to design", Classes = { "primary" }, IsEnabled = false, MinWidth = 140 };
    readonly Control _fixedRow, _varyingRows, _taperRow;

    ImageTrace.Working? _working;
    DepthEdges? _edges;
    (double, double, double, double, WidthFollows)? _edgesFor;
    PathGeometry _marks = PathGeometry.Empty;
    int _ticket, _version;

    public DepthOutlinesWindow(Document doc, ImageShape picture)
    {
        _doc = doc;
        _picture = picture;
        Title = $"Depth outlines — {(string.IsNullOrEmpty(picture.Name) ? "depthmap" : picture.Name)}";
        Width = 1300;
        Height = 860;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg0");
        _width.Value = 0.3m; _thin.Value = 0.12m; _wide.Value = 0.6m;

        foreach (var s in new[] { _detail, _amount, _shortest, _smooth }) s.ValueChanged += (_, _) => Schedule();
        foreach (var n in new[] { _width, _thin, _wide }) n.ValueChanged += (_, _) => Schedule();
        foreach (var r in new[] { _laserLine, _fixed, _varying }) r.IsCheckedChanged += (_, _) => { Rows(); Schedule(); };
        _follows.SelectionChanged += (_, _) => Schedule();
        _taper.IsCheckedChanged += (_, _) => Schedule();
        _showPicture.IsCheckedChanged += (_, _) => _preview.ShowPicture = _showPicture.IsChecked == true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        _ok.Click += (_, _) => Add();

        _fixedRow = Field("Width (mm)", _width, tip: "How wide every line is. The beam itself is about 0.1 mm, so lines wider than that are filled in.");
        _varyingRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Thinnest (mm)", _thin, tip: "The width where the edge is weakest."),
                Field("Widest (mm)", _wide, tip: "The width where the edge is strongest."),
                Field("Wider where", _follows, tip: "Steepest: tall steps get bold lines. Highest: the outlines of raised parts are bold. Deepest: the lines in the crevices are bold, like shadows."),
            },
        };
        _taperRow = _taper;

        var side = new StackPanel
        {
            Children =
            {
                new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 0, 0, 8),
                    Text = "Finds where the depthmap's surface steps or bends sharply and turns those edges into lines. Marked over the relief, they bring back the outlines a soft relief loses." },
                Card("Find the edges",
                    TraceWindow.Slide("Ignore detail under", _detail, _detailText),
                    TraceWindow.Slide("How many edges", _amount, _amountText),
                    TraceWindow.Slide("Shortest line", _shortest, _shortestText),
                    TraceWindow.Slide("Smooth the lines", _smooth, _smoothText)),
                Card("Line width",
                    new StackPanel { Spacing = 4, Children = { _laserLine, _fixed, _varying } },
                    _fixedRow, _varyingRows, _taperRow),
                Card("Result", _stats, _showPicture,
                    Hint("The lines go on a new layer of their own, after the others, so they're marked over the engraving. Score layer for one laser line, engrave (fill) layer otherwise.")),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _ok } },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("*,360") };
        grid.Children.Add(new Border { Child = _preview, Margin = new(12) });
        var scroll = new ScrollViewer { Content = side, Padding = new(0, 12, 12, 12) };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        Rows();
        UpdateLabels();
        Opened += async (_, _) => await Prepare();
    }

    void Rows()
    {
        _fixedRow.IsVisible = _fixed.IsChecked == true;
        _varyingRows.IsVisible = _varying.IsChecked == true;
        _taperRow.IsVisible = _laserLine.IsChecked != true;
    }

    void UpdateLabels()
    {
        _detailText.Text = $"{_detail.Value:0.00} mm";
        _amountText.Text = $"strongest {_amount.Value:0}%";
        _shortestText.Text = _shortest.Value < 0.05 ? "keep every line" : $"{_shortest.Value:0.#} mm";
        _smoothText.Text = _smooth.Value < 0.05 ? "as found" : $"over {_smooth.Value:0.0#} mm";
    }

    DepthOutlineOptions Options() => new()
    {
        DetailMm = _detail.Value,
        Amount = _amount.Value,
        MinLengthMm = _shortest.Value,
        SmoothMm = _smooth.Value,
        Width = _laserLine.IsChecked == true ? OutlineWidth.LaserLine : _fixed.IsChecked == true ? OutlineWidth.Fixed : OutlineWidth.Varying,
        WidthMm = (double)(_width.Value ?? 0.3m),
        MinWidthMm = (double)(_thin.Value ?? 0.12m),
        MaxWidthMm = (double)(_wide.Value ?? 0.6m),
        Follows = (WidthFollows)Math.Max(0, _follows.SelectedIndex),
        TaperEnds = _taper.IsChecked == true,
    };

    async Task Prepare()
    {
        _stats.Text = "Reading the depthmap…";
        _working = await Task.Run(() => ImageTrace.Prepare(_picture.Source.Pixels));
        _preview.Picture = CameraImaging.ToAvaloniaRgba(_working.Image);
        _preview.PixelScale = _working.Scale;
        Schedule();
    }

    void Schedule()
    {
        UpdateLabels();
        _ = Debounced(++_ticket);
    }

    /// <summary>Redraw once the controls have been still for a moment.</summary>
    async Task Debounced(int ticket)
    {
        await Task.Delay(180);
        if (ticket == _ticket) await Redraw();
    }

    async Task Redraw()
    {
        if (_working is not { } w) return;
        int v = ++_version;
        var o = Options();
        var src = _picture.Source;
        // Picture mm per working pixel.
        var mmPerPx = new Vec2(_picture.SizeMm.X / w.Image.Width, _picture.SizeMm.Y / w.Image.Height);
        var key = (o.DetailMm, o.Amount, o.MinLengthMm, o.SmoothMm, o.Follows);
        _stats.Text = "Finding edges…";
        try
        {
            var edges = _edges;
            if (edges is null || _edgesFor != key)
            {
                edges = await Task.Run(() => DepthOutlines.Find(w.Brightness, w.Image.Width, w.Image.Height, mmPerPx, o));
                if (v != _version) return;
                (_edges, _edgesFor) = (edges, key);
            }
            var marks = await Task.Run(() => DepthOutlines.Draw(edges, o));
            if (v != _version) return;
            _marks = marks;
            // The preview wants the source picture's pixels.
            var toPx = Affine.Scale(src.Width / _picture.SizeMm.X, src.Height / _picture.SizeMm.Y);
            _preview.Result = new TraceResult([new TracePart(null, marks.Transform(toPx))]);
            int n = edges.Lines.Count;
            string width = o.Width switch
            {
                OutlineWidth.LaserLine => "one laser line wide",
                OutlineWidth.Fixed => $"{o.WidthMm:0.0#} mm wide",
                _ => $"{Math.Min(o.MinWidthMm, o.MaxWidthMm):0.0#} to {Math.Max(o.MinWidthMm, o.MaxWidthMm):0.0#} mm wide",
            };
            _stats.Text = n == 0 ? "No edges found: let more edges through, or ignore less detail."
                : $"{n:N0} line{(n == 1 ? "" : "s")}, {edges.LengthMm / 1000:0.0#} m in all, {width}.";
            _ok.IsEnabled = n > 0;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            AppLog.Error("depth outlines", e);
            _stats.Text = $"Finding the edges failed: {e.Message}";
        }
    }

    void Add()
    {
        if (_marks.IsEmpty) return;
        bool line = _laserLine.IsChecked == true;
        var s = _doc.State;
        string name = string.IsNullOrWhiteSpace(_picture.Name) ? "depthmap" : _picture.Name;
        var layer = Layer.Default(s.Layers.Count, line ? LayerMode.Score : LayerMode.Fill) with { Name = $"Outlines · {name}" };
        s = Ops.AddLayer(s, layer);
        var shape = new PathShape { Name = $"Depth outlines · {name}", LayerId = layer.Id, Geometry = _marks.Transform(_picture.Transform) };
        _doc.Commit("Depth outlines", Ops.AddShapes(s, [shape]), [shape.Id]);
        Close();
    }
}
