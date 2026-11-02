using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Tracing;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>What the user chose in the trace window.</summary>
/// <param name="Parts">Outlines in the image's pixel coordinates (one per colour in colour mode).</param>
/// <param name="TargetLayer">Layer for black &amp; white output (null = new layer).</param>
/// <param name="NewLayerMode">Mode for any layers created.</param>
/// <param name="RemovePicture">Delete the source picture afterwards.</param>
public sealed record TraceChoice(IReadOnlyList<TracePart> Parts, Guid? TargetLayer, LayerMode NewLayerMode, bool RemovePicture);

/// <summary>
/// Picture → vector outlines, with a live preview. Black &amp; white mode
/// traces everything darker than a threshold; colour mode groups similar
/// colours (tolerance) and traces each group onto its own layer.
/// </summary>
public sealed class TraceWindow : Window
{
    readonly ImageShape _image;
    readonly TracePreview _preview = new();
    readonly RadioButton _bw = new() { Content = "Black & white", GroupName = "mode", IsChecked = true };
    readonly RadioButton _colour = new() { Content = "Colours", GroupName = "mode" };
    readonly RadioButton _pick = new() { Content = "Pick colour (click the picture)", GroupName = "mode" };
    readonly Slider _pickTol = new() { Minimum = 0, Maximum = 100, Value = 15 };
    readonly CheckBox _connected = new() { Content = "Only the area connected to where I clicked", IsChecked = true };
    readonly Border _pickSwatch = new() { Width = 28, Height = 28, CornerRadius = new(4), BorderBrush = Brushes.Gray, BorderThickness = new(1), Background = Brushes.Transparent };
    readonly TextBlock _pickText = Hint("Click the picture to choose a colour."), _pickTolText = Hint();
    (int X, int Y, uint Rgb)? _picked;
    readonly Slider _threshold = new() { Minimum = 1, Maximum = 254, Value = 128 };
    readonly CheckBox _invert = new() { Content = "Trace the light parts instead" };
    readonly Slider _tolerance = new() { Minimum = 1, Maximum = 100, Value = 25 };
    readonly Slider _maxColours = new() { Minimum = 2, Maximum = 16, Value = 8, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly StackPanel _swatches = new() { Spacing = 4 };
    readonly List<TextBlock> _swatchInfo = [];
    readonly Slider _smooth = new() { Minimum = 0, Maximum = 10, Value = 2 };
    readonly Slider _corners = new() { Minimum = 0, Maximum = 180, Value = 40 };
    readonly Slider _specks = new() { Minimum = 0, Maximum = 400, Value = 12 };
    readonly CheckBox _showPicture = new() { Content = "Show picture underneath", IsChecked = true };
    readonly ComboBox _target = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _newMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Cut", "Score", "Engrave (fill)" }, SelectedIndex = 0 };
    readonly CheckBox _removePicture = new() { Content = "Remove the picture after tracing" };
    readonly TextBlock _stats = Hint();
    readonly TextBlock _thresholdText = Hint(), _toleranceText = Hint(), _smoothText = Hint(), _cornersText = Hint(), _specksText = Hint(), _maxText = Hint();
    readonly Control _bwPanel, _colourPanel, _pickPanel, _targetRow;
    readonly Button _ok = new() { Content = "Add to design", Classes = { "primary" }, IsEnabled = false, MinWidth = 140 };
    readonly IReadOnlyList<Layer> _layers;

    ImageTrace.Working? _working;
    List<PaletteEntry> _palette = [];
    int[]? _labels;
    readonly HashSet<int> _include = [];
    TraceResult? _result;
    int _version;
    int _ticket;
    bool _paletteDirty = true;

    public TraceChoice? Choice { get; private set; }

    public TraceWindow(ImageShape image, IReadOnlyList<Layer> layers, Guid activeLayer)
    {
        _image = image;
        _layers = layers.Where(l => l.Mode != LayerMode.Image).ToList();
        Title = $"Trace picture — {(string.IsNullOrEmpty(image.Name) ? "image" : image.Name)}";
        Width = 1300;
        Height = 860;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg0");

        var targets = _layers.Select(l => l.Name).Append("New layer").ToList();
        _target.ItemsSource = targets;
        int active = _layers.ToList().FindIndex(l => l.Id == activeLayer);
        _target.SelectedIndex = active >= 0 ? active : targets.Count - 1;

        _bwPanel = new StackPanel
        {
            Spacing = 6,
            Children = { Slide("Threshold", _threshold, _thresholdText), _invert },
        };
        _colourPanel = new StackPanel
        {
            Spacing = 6,
            IsVisible = false,
            Children =
            {
                Slide("Colour tolerance", _tolerance, _toleranceText),
                Slide("Most colours", _maxColours, _maxText),
                Hint("Ticked colours are traced, each onto its own layer. Unticked colours are left out (the background starts unticked)."),
                _swatches,
            },
        };
        _pickPanel = new StackPanel
        {
            Spacing = 6,
            IsVisible = false,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _pickSwatch, _pickText } },
                Slide("Tolerance", _pickTol, _pickTolText),
                Hint("Low: only colours very close to the one you clicked. High: the selection spreads to similar colours."),
                _connected,
            },
        };
        _targetRow = Field("Put on layer", _target);
        _pickTol.ValueChanged += (_, _) => Schedule(false);
        _connected.IsCheckedChanged += (_, _) => Schedule(false);
        _pick.IsCheckedChanged += (_, _) => { _preview.FaintPicture = _colour.IsChecked == true || _pick.IsChecked == true; ModeChanged(); };
        _preview.Clicked += OnPreviewClick;

        foreach (var s in new[] { _threshold, _smooth, _corners, _specks }) s.ValueChanged += (_, _) => Schedule(false);
        foreach (var s in new[] { _tolerance, _maxColours }) s.ValueChanged += (_, _) => Schedule(true);
        _invert.IsCheckedChanged += (_, _) => Schedule(false);
        _bw.IsCheckedChanged += (_, _) => ModeChanged();
        _showPicture.IsCheckedChanged += (_, _) => _preview.ShowPicture = _showPicture.IsChecked == true;
        _colour.IsCheckedChanged += (_, _) => _preview.FaintPicture = _colour.IsChecked == true || _pick.IsChecked == true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        _ok.Click += (_, _) => Accept();

        var side = new StackPanel
        {
            Children =
            {
                Card("Mode", new StackPanel { Spacing = 4, Children = { _bw, _colour, _pick } }, _bwPanel, _colourPanel, _pickPanel),
                Card("Shape",
                    Slide("Smoothness", _smooth, _smoothText),
                    Slide("Corners", _corners, _cornersText),
                    Slide("Ignore specks", _specks, _specksText),
                    _stats),
                Card("Result", _targetRow, Field("New layers", _newMode, tip: "What any new layers do. You can change it later."), _removePicture, _showPicture),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _ok } },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("*,360") };
        grid.Children.Add(new Border { Child = _preview, Margin = new(12) });
        var scroll = new ScrollViewer { Content = side, Padding = new(0, 12, 12, 12) };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        UpdateLabels();
        Opened += async (_, _) => await Prepare();
    }

    public static Control Slide(string label, Slider s, TextBlock value)
    {
        var head = new DockPanel();
        value.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(new TextBlock { Text = label, Classes = { "muted" } });
        return new StackPanel { Children = { head, s } };
    }

    void UpdateLabels()
    {
        _thresholdText.Text = $"{_threshold.Value:0}";
        _toleranceText.Text = $"{_tolerance.Value:0}";
        _maxText.Text = $"{_maxColours.Value:0}";
        _smoothText.Text = _smooth.Value < 0.5 ? "exact" : $"{_smooth.Value:0.#}";
        _cornersText.Text = _corners.Value < 1 ? "straight lines only" : _corners.Value > 179 ? "all rounded" : $"sharper than {_corners.Value:0}° stay sharp";
        _specksText.Text = _specks.Value < 1 ? "keep everything" : $"under {_specks.Value:0} px";
        _pickTolText.Text = $"{_pickTol.Value:0}";
    }

    async Task Prepare()
    {
        _stats.Text = "Reading picture…";
        _working = await Task.Run(() => ImageTrace.Prepare(_image.Source.Pixels));
        _preview.Picture = CameraImaging.ToAvaloniaRgba(_working.Image);
        _preview.PixelScale = _working.Scale;
        Schedule(true);
    }

    void ModeChanged()
    {
        bool bw = _bw.IsChecked == true, pick = _pick.IsChecked == true, colour = _colour.IsChecked == true;
        _bwPanel.IsVisible = bw;
        _colourPanel.IsVisible = colour;
        _pickPanel.IsVisible = pick;
        _targetRow.IsVisible = !colour;
        _preview.Cursor = new Cursor(pick ? StandardCursorType.Cross : StandardCursorType.Arrow);
        Schedule(colour);
    }

    void OnPreviewClick(Vec2 px)
    {
        if (_working is not { } w) return;
        int x = (int)Math.Floor(px.X), y = (int)Math.Floor(px.Y);
        if (x < 0 || y < 0 || x >= w.Image.Width || y >= w.Image.Height) return;
        if (_pick.IsChecked != true) _pick.IsChecked = true;
        var rgb = ImageTrace.SampleColour(w, x, y);
        _picked = (x, y, rgb);
        _pickSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        _pickText.Text = $"#{rgb:X6} picked. Adjust the tolerance to grow or shrink the selection.";
        Schedule(false);
    }

    void Schedule(bool paletteChanged)
    {
        _paletteDirty |= paletteChanged;
        UpdateLabels();
        _ = Debounced(++_ticket);
    }

    /// <summary>Retrace once the controls have been still for a moment (dragging a slider retraces once, not per pixel).</summary>
    async Task Debounced(int ticket)
    {
        await Task.Delay(180);
        if (ticket == _ticket) await Retrace();
    }

    TraceOptions Options => new()
    {
        Threshold = _threshold.Value / 255,
        Invert = _invert.IsChecked == true,
        Smoothness = _smooth.Value,
        CornerAngle = _corners.Value,
        MinAreaPx = (int)Math.Round(_specks.Value / Math.Max(1, (_working?.Scale ?? 1) * (_working?.Scale ?? 1))),
    };

    async Task Retrace()
    {
        if (_working is not { } w) return;
        int v = ++_version;
        var o = Options;
        bool bw = _bw.IsChecked == true, pick = _pick.IsChecked == true;
        if (pick && _picked is null)
        {
            _result = null;
            _preview.Result = null;
            _stats.Text = "Click the picture to choose a colour.";
            _ok.IsEnabled = false;
            return;
        }
        var picked = _picked;
        double pickTol = _pickTol.Value;
        bool connected = _connected.IsChecked == true;
        _stats.Text = "Tracing…";
        try
        {
            if (!bw && !pick && (_paletteDirty || _labels is null))
            {
                double tol = _tolerance.Value;
                int max = (int)_maxColours.Value;
                var (palette, labels) = await Task.Run(() => ImageTrace.Palette(w, tol, max));
                if (v != _version) return;
                _palette = palette;
                _labels = labels;
                _paletteDirty = false;
                _include.Clear();
                for (int i = 0; i < palette.Count; i++) if (!palette[i].IsBackground) _include.Add(i);
                BuildSwatches();
            }
            var include = _include.ToHashSet();
            var labelsNow = _labels;
            var paletteNow = _palette;
            var result = await Task.Run(() => bw ? ImageTrace.BlackAndWhite(w, o)
                : pick ? ImageTrace.PickColour(w, picked!.Value.Rgb, picked.Value.X, picked.Value.Y, pickTol, connected, o)
                : ImageTrace.ByColour(w, paletteNow, labelsNow!, include, o));
            if (v != _version) return;
            _result = result;
            _preview.Result = result;
            UpdateSwatchInfo(result);
            _stats.Text = result.Paths == 0 ? (pick ? "Nothing selected: raise the tolerance or click another spot." : "Nothing traced: try another threshold, or tick more colours.")
                : $"{result.Paths} outline{(result.Paths == 1 ? "" : "s")}, {result.Nodes} segments.";
            _ok.IsEnabled = result.Paths > 0;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            AppLog.Error("trace", e);
            _stats.Text = $"Tracing failed: {e.Message}";
        }
    }

    /// <summary>After each trace, say per colour what happened to it.</summary>
    void UpdateSwatchInfo(TraceResult result)
    {
        for (int k = 0; k < _swatchInfo.Count && k < _palette.Count; k++)
        {
            var p = _palette[k];
            string share = $"{100.0 * p.Pixels / Math.Max(1, _labels?.Length ?? 1):0.#}% of picture";
            if (!_include.Contains(k))
            {
                _swatchInfo[k].Text = $"{(p.IsBackground ? "background · " : "")}{share} · left out";
                continue;
            }
            int outlines = result.Parts.Where(x => x.Rgb == p.Rgb).Sum(x => x.Geometry.Subpaths.Length);
            _swatchInfo[k].Text = $"{share} · {outlines} outline{(outlines == 1 ? "" : "s")} → own layer";
        }
    }

    void BuildSwatches()
    {
        _swatches.Children.Clear();
        _swatchInfo.Clear();
        for (int i = 0; i < _palette.Count; i++)
        {
            int k = i;
            var p = _palette[i];
            var info = new TextBlock { Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _swatchInfo.Add(info);
            var box = new CheckBox
            {
                IsChecked = _include.Contains(k),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Border { Width = 22, Height = 22, CornerRadius = new(4), BorderBrush = Brushes.Gray, BorderThickness = new(1),
                            Background = new SolidColorBrush(Color.FromRgb((byte)(p.Rgb >> 16), (byte)(p.Rgb >> 8), (byte)p.Rgb)) },
                        info,
                    },
                },
            };
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) _include.Add(k); else _include.Remove(k);
                Schedule(false);
            };
            _swatches.Children.Add(box);
        }
    }

    void Accept()
    {
        if (_result is null || _result.Paths == 0) return;
        bool oneLayer = _colour.IsChecked != true;
        Guid? target = oneLayer && _target.SelectedIndex >= 0 && _target.SelectedIndex < _layers.Count ? _layers[_target.SelectedIndex].Id : null;
        // A single picked colour goes to the chosen layer, like black & white.
        var parts = oneLayer ? _result.Parts.Select(p => p with { Rgb = null }).ToList() : _result.Parts;
        Choice = new TraceChoice(parts, target, (LayerMode)Math.Max(0, _newMode.SelectedIndex), _removePicture.IsChecked == true);
        Close();
    }
}

/// <summary>The picture with the traced outlines drawn over it; wheel zooms, drag pans.</summary>
public sealed class TracePreview : Control
{
    Bitmap? _picture;
    TraceResult? _result;
    bool _showPicture = true;
    double _zoom = 1;
    Point _offset;
    bool _fitted;
    Point? _pan;

    public Bitmap? Picture { get => _picture; set { _picture = value; _fitted = false; InvalidateArrange(); InvalidateVisual(); } }
    /// <summary>Original pixels per preview pixel (traced outlines are in original pixels).</summary>
    public double PixelScale { get; set; } = 1;
    public TraceResult? Result { get => _result; set { _result = value; InvalidateVisual(); } }
    public bool ShowPicture { get => _showPicture; set { _showPicture = value; InvalidateVisual(); } }
    bool _faint;
    /// <summary>Show the picture very faintly, so the traced colours stand out (colour modes).</summary>
    public bool FaintPicture { get => _faint; set { _faint = value; InvalidateVisual(); } }
    bool _marks;
    /// <summary>Draw the result as the marks it makes (filled dark, lines as thin strokes) instead of traced outlines.</summary>
    public bool Marks { get => _marks; set { _marks = value; InvalidateVisual(); } }

    /// <summary>A click (not a drag) on the picture, in preview-bitmap pixels.</summary>
    public event Action<Vec2>? Clicked;
    Point _press;

    public TracePreview() => ClipToBounds = true;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = base.ArrangeOverride(finalSize);
        if (!_fitted && _picture is not null && finalSize.Width > 10)
        {
            var s = _picture.PixelSize;
            _zoom = Math.Min(finalSize.Width / s.Width, finalSize.Height / s.Height) * 0.95;
            _offset = new((finalSize.Width - s.Width * _zoom) / 2, (finalSize.Height - s.Height * _zoom) / 2);
            _fitted = true;
        }
        return r;
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (_picture is null)
        {
            var t = new FormattedText("Reading picture…", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gray);
            ctx.DrawText(t, new Point((Bounds.Width - t.Width) / 2, (Bounds.Height - t.Height) / 2));
            return;
        }
        var s = _picture.PixelSize;
        var view = Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y);
        using (ctx.PushTransform(view))
        {
            if (_showPicture)
                using (ctx.PushOpacity(_faint ? 0.12 : 0.35))
                    ctx.DrawImage(_picture, new Rect(0, 0, s.Width, s.Height), new Rect(0, 0, s.Width, s.Height));
            if (_result is null) return;
            // Outlines are in original pixels; the preview bitmap may be smaller.
            using (ctx.PushTransform(Matrix.CreateScale(1 / PixelScale, 1 / PixelScale)))
            {
                double px = PixelScale / _zoom;
                if (_marks)
                {
                    var ink = new SolidColorBrush(Color.Parse("#3B2616"));
                    foreach (var part in _result.Parts)
                        ctx.DrawGeometry(ink, new Pen(ink, 1.4 * px), ToAvalonia(part.Geometry, FillRule.NonZero));
                    return;
                }
                foreach (var part in _result.Parts)
                {
                    var c = part.Rgb is { } rgb ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : Color.Parse("#111827");
                    var g = ToAvalonia(part.Geometry);
                    ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B)), new Pen(new SolidColorBrush(Color.Parse("#E5484D")), 1.2 * px), g);
                }
            }
        }
    }

    static StreamGeometry ToAvalonia(PathGeometry pg, FillRule rule = FillRule.EvenOdd)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(rule);
        foreach (var sp in pg.Subpaths)
        {
            c.BeginFigure(new Point(sp.Start.X, sp.Start.Y), sp.Closed);
            foreach (var seg in sp.Segs)
            {
                if (seg.Kind == SegKind.Line) c.LineTo(new Point(seg.To.X, seg.To.Y));
                else c.CubicBezierTo(new Point(seg.C1.X, seg.C1.Y), new Point(seg.C2.X, seg.C2.Y), new Point(seg.To.X, seg.To.Y));
            }
            c.EndFigure(sp.Closed);
        }
        return g;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        double old = _zoom;
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 0.05, 40);
        _offset = new(p.X - (p.X - _offset.X) * _zoom / old, p.Y - (p.Y - _offset.Y) * _zoom / old);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pan = _press = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pan is not { } from) return;
        var p = e.GetPosition(this);
        _offset += p - from;
        _pan = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pan = null;
        e.Pointer.Capture(null);
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _press.X) < 4 && Math.Abs(p.Y - _press.Y) < 4 && e.InitialPressMouseButton == MouseButton.Left)
            Clicked?.Invoke(new Vec2((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom));
    }
}
