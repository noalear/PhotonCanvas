using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

public enum Tool { Select, Rect, Ellipse, Polygon, Star, Line, Pen, Text, Nodes, Pan, Wand, Magnetic, Measure, Trim, Align2, Scissors, Tabs, Start, Eraser, Arc, Centreline }

/// <summary>
/// The design canvas: machine work area in millimetres (+Y down, origin top-left),
/// with zoom/pan, selection, move/scale/rotate, drawing tools and the job preview.
/// </summary>
public sealed partial class CanvasView : Control
{
    // ---- appearance -----------------------------------------------------------
    static IBrush BgBrush = new SolidColorBrush(Color.Parse("#0E1014"));
    static IBrush FieldBrush = new SolidColorBrush(Color.Parse("#161920"));
    static IPen FieldPen = new Pen(new SolidColorBrush(Color.Parse("#3A4150")), 1);
    static IBrush RulerBrush = new SolidColorBrush(Color.Parse("#15181E"));
    static IBrush RulerText = new SolidColorBrush(Color.Parse("#7D8694"));
    static IPen RulerTick = new Pen(new SolidColorBrush(Color.Parse("#3A4150")), 1);
    static readonly Color AccentColor = Color.Parse("#4F8CFF");
    static readonly IPen SelBoxPen = new Pen(new SolidColorBrush(AccentColor), 1, new DashStyle([4, 3], 0));
    static readonly IBrush HandleFill = Brushes.White;
    static readonly IPen HandlePen = new Pen(new SolidColorBrush(AccentColor), 1.5);
    static readonly IBrush WindowBoxFill = new SolidColorBrush(Color.FromArgb(28, 79, 140, 255));
    static readonly IPen WindowBoxPen = new Pen(new SolidColorBrush(AccentColor), 1);
    static readonly IBrush CrossBoxFill = new SolidColorBrush(Color.FromArgb(28, 47, 194, 122));
    static readonly IPen CrossBoxPen = new Pen(new SolidColorBrush(Color.Parse("#2FC27A")), 1, new DashStyle([5, 3], 0));
    static readonly Typeface Face = new("Inter");

    const double RulerSize = 22;
    const double HandleSize = 8;
    const double RotateOffset = 26;
    const double DragThreshold = 3;

    // ---- state ----------------------------------------------------------------
    Document? _doc;
    MachineProfile _machine = MachineProfile.F2Ultra;
    double _scale = 3;            // screen px per mm
    Point _offset = new(60, 60);  // screen position of machine (0, 0)
    bool _fitted;
    readonly ConditionalWeakTable<PathShape, StreamGeometry> _geoms = new();
    // Layers with many shapes (a DXF of thousands of holes) are drawn in chunks of a few hundred
    // shapes each, not shape by shape: a chunk is rebuilt only when one of its shapes changes, and
    // the selected shapes are drawn as one more geometry per layer when the selection changes.
    const int BatchFrom = 300, ChunkSize = 500;
    object? _batchedFor, _batchSelFor;
    readonly Dictionary<Guid, List<(PathShape[] Members, StreamGeometry Geometry)>> _batches = [];
    readonly Dictionary<Guid, List<StreamGeometry>> _batchSel = [];
    readonly ConditionalWeakTable<ImageSource, Avalonia.Media.Imaging.Bitmap> _pictures = new();

    enum Drag { None, Pan, Box, Move, Scale, Rotate, Create, PenHandle, NodeAnchor, NodeHandle, LineGrip }
    Drag _drag;
    Point _pressScreen, _lastScreen;
    Vec2 _pressWorld;
    DocState? _base;
    HashSet<Guid> _dragIds = [];
    Rect2 _dragBounds;
    int _handle;
    bool _editStarted;
    bool _spaceHeld;
    Guid? _hover;
    Guid? _highlight;

    /// <summary>A shape to point out (e.g. the one under the mouse in the layer list); null for none.</summary>
    public Guid? HighlightShape
    {
        get => _highlight;
        set { if (_highlight == value) return; _highlight = value; InvalidateVisual(); }
    }
    Vec2? _cursor;
    Vec2 _createEnd;

    public CanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    public Document? Doc
    {
        get => _doc;
        set
        {
            if (_doc is not null) _doc.Changed -= OnDocChanged;
            _doc = value;
            if (_doc is not null) _doc.Changed += OnDocChanged;
            InvalidateVisual();
        }
    }

    public MachineProfile Machine
    {
        get => _machine;
        set { _machine = value; InvalidateVisual(); }
    }

    Tool _tool;
    public Tool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            if (_tool == Tool.Pen && _pen.Count >= 2) FinishPen(closed: false);
            _pen.Clear();
            if (_tool == Tool.Magnetic && _magAnchors.Count >= 2) FinishMagnetic(closed: false);
            ClearMagnetic();
            _wandPreview = null;
            _wandTicket++;
            _node = null;
            ResetPointTools();
            ResetSnaps();
            ResetConstruct();
            _tool = value;
            if (value is Tool.Wand or Tool.Magnetic) PreparePhoto();
            CancelDrag();
            UpdateCursor(null);
            ToolChanged?.Invoke(value);
            InvalidateVisual();
        }
    }

    (Avalonia.Media.Imaging.Bitmap Image, Rect2 Area)? _closeUp;

    /// <summary>A close-up camera photo, straightened, covering <c>Area</c> of the bed (null hides it).</summary>
    public (Avalonia.Media.Imaging.Bitmap Image, Rect2 Area)? CloseUpImage
    {
        get => _closeUp;
        set
        {
            var old = _closeUp?.Image;
            _closeUp = value;
            if (old is not null && !ReferenceEquals(old, value?.Image)) old.Dispose();
            InvalidateVisual();
        }
    }

    (BuiltJob Job, RunMarker Marker)? _run;

    /// <summary>A running job: what's been marked, what's being marked, and where the laser is (null hides it).</summary>
    public void ShowRun(BuiltJob? job, RunMarker? marker)
    {
        _run = job is not null && marker is not null ? (job, marker) : null;
        InvalidateVisual();
    }

    BuiltJob? _preview;
    /// <summary>When set, the processing order and travel moves are drawn over the design.</summary>
    public BuiltJob? Preview
    {
        get => _preview;
        set { _preview = value; InvalidateVisual(); }
    }

    Avalonia.Media.Imaging.Bitmap? _camera;
    /// <summary>Straightened camera picture covering exactly the work area (0–FieldW × 0–FieldH mm).</summary>
    public Avalonia.Media.Imaging.Bitmap? CameraImage
    {
        get => _camera;
        set { _camera = value; InvalidateVisual(); }
    }

    /// <summary>
    /// Page Y → bed Y (bed = page + this) when the page isn't the bed (the rotary), so bed photos are put where they
    /// belong; null on the flat bed.
    /// </summary>
    public Func<double?>? BedShiftY { get; set; }

    double _cameraOpacity = 0.85;
    public double CameraOpacity
    {
        get => _cameraOpacity;
        set { _cameraOpacity = value; InvalidateVisual(); }
    }

    bool _showGrid = true;
    /// <summary>
    /// Pictures on a layer with a measured tone curve are drawn as they'll come out: corrected and
    /// through the material's response, in its own colours.
    /// </summary>
    public bool PicturesAsBurned
    {
        get => _asBurned;
        set { _asBurned = value; InvalidateVisual(); }
    }
    bool _asBurned;
    readonly Dictionary<(ImageSource, Core.Imaging.ToneAdjust, Core.Imaging.ToneCurve), Avalonia.Media.Imaging.Bitmap> _burned = [];

    /// <summary>A picture as it'll burn (made once per picture, its tone settings and the material's curve).</summary>
    Avalonia.Media.Imaging.Bitmap Burned(ImageShape im, Core.Imaging.ToneCurve curve)
    {
        var key = (im.Source, im.Tone, curve);
        if (_burned.TryGetValue(key, out var have)) return have;
        if (_burned.Count > 24) _burned.Clear();
        var src = im.Source.Pixels;
        var px = new byte[src.Width * src.Height * 4];
        // Tones are worked out once each (pictures have far more pixels than greys).
        var colour = new uint[256];
        for (int g = 0; g < 256; g++) colour[g] = curve.Burned(im.Tone.Apply(g / 255.0), corrected: true);
        uint bare = curve.Burned(1, corrected: true);
        for (int i = 0; i < src.Width * src.Height; i++)
        {
            int k = i * 4;
            int lum = (int)Math.Round(0.299 * src.Rgba[k] + 0.587 * src.Rgba[k + 1] + 0.114 * src.Rgba[k + 2]);
            double a = src.Rgba[k + 3] / 255.0;
            uint c = colour[Math.Clamp(lum, 0, 255)];
            // Transparent parts are bare material.
            px[k] = (byte)Math.Round(((c >> 16) & 0xFF) * a + ((bare >> 16) & 0xFF) * (1 - a));
            px[k + 1] = (byte)Math.Round(((c >> 8) & 0xFF) * a + ((bare >> 8) & 0xFF) * (1 - a));
            px[k + 2] = (byte)Math.Round((c & 0xFF) * a + (bare & 0xFF) * (1 - a));
            px[k + 3] = 255;
        }
        var bmp = CameraImaging.ToAvaloniaRgba(new Core.Imaging.RasterImage(src.Width, src.Height, px));
        _burned[key] = bmp;
        return bmp;
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; InvalidateVisual(); }
    }

    public event Action<Vec2?>? CursorMoved;
    /// <summary>The text tool clicked here (world mm): make a text object.</summary>
    public event Action<Vec2>? TextRequested;
    /// <summary>A text object was double-clicked: edit its words.</summary>
    public event Action<Guid>? TextEditRequested;

    /// <summary>A code (QR, barcode) was double-clicked: edit it.</summary>
    public event Action<Guid>? CodeEditRequested;

    /// <summary>The light theme's work area: a white bed on a grey surround.</summary>
    public static void UseLightColours()
    {
        BgBrush = new SolidColorBrush(Color.Parse("#D3D8DF"));
        FieldBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
        FieldPen = new Pen(new SolidColorBrush(Color.Parse("#98A1AE")), 1);
        RulerBrush = new SolidColorBrush(Color.Parse("#EEF1F4"));
        RulerText = new SolidColorBrush(Color.Parse("#5B6472"));
        RulerTick = new Pen(new SolidColorBrush(Color.Parse("#AEB6C1")), 1);
        GridSettings.Default = new(10, 1, Color.Parse("#D2D8E0"), Color.Parse("#EBEEF2"));
    }

    /// <summary>Picture eraser: brush width (mm).</summary>
    public double EraserMm { get; set; } = 3;

    /// <summary>Point editing: the radius R gives a corner (mm).</summary>
    public double CornerRadiusMm { get; set; } = 2;

    /// <summary>Polygon tool: number of sides.</summary>
    public int PolygonSides { get; set; } = 6;
    /// <summary>Star tool: number of points.</summary>
    public int StarPoints { get; set; } = 5;
    /// <summary>Star tool: inner radius as a fraction of the outer.</summary>
    public double StarInner { get; set; } = 0.5;
    public event Action<Tool>? ToolChanged;
    public event Action<double>? ZoomChanged;

    /// <summary>Screen pixels per millimetre.</summary>
    public double Zoom => _scale;

    void OnDocChanged(bool content) => InvalidateVisual();

    // ---- coordinates ------------------------------------------------------------
    Vec2 ToWorld(Point p) => new((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
    Point ToScreen(Vec2 w) => new(w.X * _scale + _offset.X, w.Y * _scale + _offset.Y);
    Rect ToScreen(Rect2 r) => new(ToScreen(r.TopLeft), ToScreen(r.BottomRight));

    Rect Viewport => new(RulerSize, RulerSize, Math.Max(1, Bounds.Width - RulerSize), Math.Max(1, Bounds.Height - RulerSize));

    public void FitRect(Rect2 r, double marginPx = 40)
    {
        if (r.IsEmpty || Bounds.Width < 10) return;
        var vp = Viewport;
        double w = Math.Max(r.Width, 1), h = Math.Max(r.Height, 1);
        _scale = Math.Clamp(Math.Min((vp.Width - 2 * marginPx) / w, (vp.Height - 2 * marginPx) / h), 0.2, 400);
        var c = r.Center;
        _offset = new(vp.Center.X - c.X * _scale, vp.Center.Y - c.Y * _scale);
        ZoomChanged?.Invoke(_scale);
        InvalidateVisual();
    }

    public void FitField() => FitRect(new Rect2(0, 0, _machine.FieldW, _machine.FieldH));

    /// <summary>Where the close-up camera sees (outlined, so tests read from it can be kept inside); null hides it.</summary>
    public Rect2? CloseUpArea
    {
        get => _closeUpArea;
        set { if (value != _closeUpArea) { _closeUpArea = value; InvalidateVisual(); } }
    }
    Rect2? _closeUpArea;

    /// <summary>The part of the bed on screen now (mm).</summary>
    public Rect2 VisibleArea => new(ToWorld(Viewport.TopLeft).X, ToWorld(Viewport.TopLeft).Y, ToWorld(Viewport.BottomRight).X, ToWorld(Viewport.BottomRight).Y);

    public void FitSelection()
    {
        if (_doc is null) return;
        if (_doc.Selection.Count > 0) FitShapes(_doc.State.Bounds(_doc.Selection)); else FitDesign();
    }

    /// <summary>Zoom to everything in the design, whatever is selected; the whole bed when it's empty.</summary>
    public void FitDesign()
    {
        if (_doc is null) return;
        FitShapes(_doc.State.Bounds(_doc.State.Shapes.Select(s => s.Id)));
    }

    void FitShapes(Rect2 b)
    {
        if (b.IsEmpty) FitField(); else FitRect(b.Inflate(Math.Max(b.Width, b.Height) * 0.05 + 1));
    }

    public void ZoomBy(double factor) => ZoomAt(Viewport.Center, factor);

    void ZoomAt(Point screen, double factor)
    {
        var w = ToWorld(screen);
        _scale = Math.Clamp(_scale * factor, 0.2, 400);
        _offset = new(screen.X - w.X * _scale, screen.Y - w.Y * _scale);
        ZoomChanged?.Invoke(_scale);
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = base.ArrangeOverride(finalSize);
        if (!_fitted && finalSize.Width > 50 && finalSize.Height > 50)
        {
            _fitted = true;
            Dispatcher(() => FitField());
        }
        return s;
    }

    static void Dispatcher(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a);

    // ---- rendering -------------------------------------------------------------
    StreamGeometry GeometryFor(PathShape s) => _geoms.GetValue(s, BuildGeometry);

    static StreamGeometry BuildGeometry(PathShape s) => Combine([s]);

    static StreamGeometry Combine(IEnumerable<PathShape> shapes)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(FillRule.EvenOdd);
        foreach (var s in shapes)
            foreach (var sp in s.World.Subpaths)
            {
                if (sp.Segs.IsEmpty) continue;
                c.BeginFigure(P(sp.Start), true);
                foreach (var seg in sp.Segs)
                {
                    if (seg.Kind == SegKind.Line) c.LineTo(P(seg.To));
                    else c.CubicBezierTo(P(seg.C1), P(seg.C2), P(seg.To));
                }
                c.EndFigure(sp.Closed);
            }
        return g;
    }

    /// <summary>The layers drawn in one piece (those with many shapes), each as one geometry, and their selected shapes likewise.</summary>
    void UpdateBatches(Document doc)
    {
        var state = doc.State;
        if (!ReferenceEquals(_batchedFor, state.Shapes))
        {
            _batchedFor = state.Shapes;
            _batchSelFor = null;
            var old = new Dictionary<Guid, List<(PathShape[] Members, StreamGeometry Geometry)>>(_batches);
            _batches.Clear();
            // Only shapes shown and sent: a hidden one isn't drawn, one with its output off is drawn faded on its own.
            foreach (var g in state.Shapes.OfType<PathShape>().Where(x => x.Sent).GroupBy(x => x.LayerId))
            {
                var shapes = g.ToArray();
                if (shapes.Length < BatchFrom) continue;
                var was = old.GetValueOrDefault(g.Key);
                var chunks = new List<(PathShape[], StreamGeometry)>();
                for (int i = 0, n = 0; i < shapes.Length; i += ChunkSize, n++)
                {
                    var members = shapes[i..Math.Min(shapes.Length, i + ChunkSize)];
                    // Unchanged shapes are the very same objects: such a chunk keeps its geometry.
                    if (was is not null && n < was.Count && was[n].Members.Length == members.Length && was[n].Members.AsSpan().SequenceEqual(members, ReferenceEqualityComparer.Instance))
                        chunks.Add(was[n]);
                    else chunks.Add((members, Combine(members)));
                }
                _batches[g.Key] = chunks;
            }
        }
        if (!ReferenceEquals(_batchSelFor, doc.Selection))
        {
            _batchSelFor = doc.Selection;
            _batchSel.Clear();
            if (doc.Selection.Count > 0 && _batches.Count > 0)
                foreach (var g in state.Shapes.OfType<PathShape>().Where(x => x.Sent && _batches.ContainsKey(x.LayerId) && doc.Selection.Contains(x.Id)).GroupBy(x => x.LayerId))
                    _batchSel[g.Key] = [.. g.Chunk(ChunkSize).Select(Combine)];
        }
    }

    static Point P(Vec2 v) => new(v.X, v.Y);

    static StreamGeometry ClipGeometry(PhotonCanvas.Doc.PathGeometry g)
    {
        var sg = new StreamGeometry();
        using var c = sg.Open();
        c.SetFillRule(FillRule.EvenOdd);
        foreach (var sp in g.Subpaths.Where(s => !s.Segs.IsEmpty))
        {
            c.BeginFigure(P(sp.Start), true);
            foreach (var seg in sp.Segs)
            {
                if (seg.Kind == SegKind.Line) c.LineTo(P(seg.To));
                else c.CubicBezierTo(P(seg.C1), P(seg.C2), P(seg.To));
            }
            c.EndFigure(true);
        }
        return sg;
    }

    static Color LayerColor(Layer l, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color);

    /// <summary>Add-ons' drawings (see <see cref="Addons.ICanvasOverlay"/>), in the order added.</summary>
    public List<Addons.ICanvasOverlay> Overlays { get; } = [];

    void DrawOverlays(DrawingContext ctx, Addons.CanvasLayer layer)
    {
        if (Overlays.Count == 0) return;
        var info = new Addons.CanvasDrawInfo(_scale, ToScreen, Rect2.FromPoints(ToWorld(new Point(0, 0)), ToWorld(new Point(Bounds.Width, Bounds.Height))));
        foreach (var o in Overlays.ToList())
            if (o.Layer == layer) o.Draw(ctx, info);
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, b);
        var field = ToScreen(new Rect2(0, 0, _machine.FieldW, _machine.FieldH));
        ctx.FillRectangle(FieldBrush, field);
        // Photos are of the bed. On the rotary the page is the part unrolled, so they're shown at their true size with the
        // laser line on the row that faces it (not squeezed into the page), and clipped to the page.
        double? bedShift = BedShiftY?.Invoke();
        Rect2 OnPage(Rect2 bed) => bedShift is { } dy ? new Rect2(bed.MinX, bed.MinY - dy, bed.MaxX, bed.MaxY - dy) : bed;
        using (bedShift is null ? default(DrawingContext.PushedState?) : ctx.PushClip(field))
        {
            if (_camera is { } cam)
                using (ctx.PushOpacity(_cameraOpacity))
                    ctx.DrawImage(cam, new Rect(0, 0, cam.PixelSize.Width, cam.PixelSize.Height),
                        bedShift is null ? field : ToScreen(OnPage(new Rect2(0, 0, MachineProfile.F2Ultra.FieldW, MachineProfile.F2Ultra.FieldH))));
            if (_closeUp is { } cu)
            {
                // The close-up photo on top of the overview, where the camera really looks.
                var r = ToScreen(OnPage(cu.Area));
                ctx.DrawImage(cu.Image, new Rect(0, 0, cu.Image.PixelSize.Width, cu.Image.PixelSize.Height), r);
                ctx.DrawRectangle(new Pen(new SolidColorBrush(AccentColor), 1, new DashStyle([4, 3], 0)), r);
            }
        }
        if (_showGrid) DrawGrid(ctx, field);
        if (_closeUpArea is { } seen)
        {
            var r = ToScreen(OnPage(seen));
            var green = Color.Parse("#2FC27A");
            ctx.DrawRectangle(new Pen(new SolidColorBrush(green), 1.5, new DashStyle([6, 4], 0)), r);
            var label = new FormattedText("close-up camera sees this", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, new SolidColorBrush(green));
            ctx.DrawText(label, new Point(r.X + 4, r.Y - label.Height - 2));
        }
        ctx.DrawRectangle(FieldPen, field.Deflate(0.5));

        if (_doc is { } doc)
        {
            DrawSurfaces(ctx, doc);
            var world = Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_offset.X, _offset.Y);
            using (ctx.PushTransform(world))
            {
                DrawOverlays(ctx, Addons.CanvasLayer.UnderShapes);
                DrawShapes(ctx, doc);
                DrawOverlays(ctx, Addons.CanvasLayer.OverShapes);
                if (_preview is not null) DrawPreview(ctx, _preview);
                if (_run is { } run) DrawRun(ctx, run.Job, run.Marker);
                if (_drag == Drag.Create) DrawCreatePreview(ctx);
                if (_tool == Tool.Pen) DrawPen(ctx);
                if (_tool == Tool.Wand) DrawWand(ctx);
                if (_tool == Tool.Magnetic) DrawMagnetic(ctx);
                DrawConstruct(ctx);
            }
            if (_preview is { } order) DrawOrderLegend(ctx, order);
            if (_tool == Tool.Nodes) DrawNodes(ctx);
            else DrawSelection(ctx, doc);
            DrawPointTools(ctx);
            DrawSection(ctx);
            DrawGuidelines(ctx);
            DrawTabs(ctx);
            if (_drag == Drag.Box) DrawBox(ctx);
            if (_drag != Drag.None || PicksPoints) DrawGuides(ctx);
            DrawLineInProgress(ctx);
            DrawSnaps(ctx);
            DrawConstructLabel(ctx);
            DrawOverlays(ctx, Addons.CanvasLayer.Screen);
        }
        DrawRulers(ctx);
    }

    void DrawShapes(DrawingContext ctx, Document doc)
    {
        double px = 1 / _scale;
        var layers = doc.State.Layers.ToDictionary(l => l.Id);
        var selected = doc.Selection;
        var viewWorld = new Rect2(ToWorld(new Point(0, 0)).X, ToWorld(new Point(0, 0)).Y, ToWorld(new Point(Bounds.Width, Bounds.Height)).X, ToWorld(new Point(Bounds.Width, Bounds.Height)).Y);
        UpdateBatches(doc);
        // Pictures first, whatever their layer: every line and shape shows on top of them, so a
        // picture can be lined up with the outlines under it.
        foreach (var shape in doc.State.Shapes)
            if (shape is ImageShape { Visible: true } im && layers.TryGetValue(im.LayerId, out var picLayer) && picLayer.Visible && im.Bounds.Intersects(viewWorld))
                DrawPicture(ctx, im, picLayer, selected.Contains(im.Id), _hover == im.Id || _highlight == im.Id, px);
        foreach (var (id, chunks) in _batches)
        {
            if (!layers.TryGetValue(id, out var layer) || !layer.Visible) continue;
            byte alpha = layer.Output ? (byte)255 : (byte)110;
            IBrush? fill = layer.Mode == LayerMode.Fill ? new SolidColorBrush(LayerColor(layer, (byte)(layer.Output ? 60 : 25))) : null;
            var pen = new Pen(new SolidColorBrush(LayerColor(layer, alpha)), 1.25 * px);
            foreach (var (_, geometry) in chunks) ctx.DrawGeometry(fill, pen, geometry);
            if (_batchSel.TryGetValue(id, out var sel))
            {
                // Solid, not dashed: dashing thousands of outlines takes a second a frame.
                var thick = new Pen(new SolidColorBrush(LayerColor(layer, alpha)), 2.6 * px);
                var core = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 0.9 * px);
                foreach (var g in sel) { ctx.DrawGeometry(null, thick, g); ctx.DrawGeometry(null, core, g); }
            }
        }
        foreach (var shape in doc.State.Shapes)
        {
            if (!shape.Visible || !layers.TryGetValue(shape.LayerId, out var layer) || !layer.Visible) continue;
            if (shape.Output && _batches.ContainsKey(shape.LayerId))
            {
                // Drawn with its layer; only the one under the pointer (or highlighted) on top.
                if ((_hover == shape.Id || _highlight == shape.Id) && shape is PathShape hp && !selected.Contains(shape.Id))
                {
                    var hg = GeometryFor(hp);
                    if (_highlight == shape.Id)
                        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, AccentColor.R, AccentColor.G, AccentColor.B)), 5 * px), hg);
                    ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LayerColor(layer)), 2 * px), hg);
                }
                continue;
            }
            if (shape is not PathShape ps) continue;
            if (!ps.Bounds.Inflate(1).Intersects(viewWorld)) continue;
            var geom = GeometryFor(ps);
            // A guide (its layer's output off, or its own) is drawn faded.
            bool sent = layer.Output && ps.Output;
            byte alpha = sent ? (byte)255 : (byte)110;
            IBrush? fill = layer.Mode == LayerMode.Fill ? new SolidColorBrush(LayerColor(layer, (byte)(sent ? 60 : 25))) : null;
            bool sel = selected.Contains(shape.Id);
            bool hov = _hover == shape.Id && !sel;
            double w = sel ? 2.2 : hov ? 2 : 1.25;
            if (_highlight == shape.Id)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, AccentColor.R, AccentColor.G, AccentColor.B)), 5 * px), geom);
            // The centrelines layer's lines are dash-dot, as on a drawing.
            ctx.DrawGeometry(fill, new Pen(new SolidColorBrush(LayerColor(layer, alpha)), w * px, layer.Centrelines ? CentreDash : null), geom);
            if (sel)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 0.8 * px, new DashStyle([3, 3], 0)), geom);
        }
    }

    void DrawPicture(DrawingContext ctx, ImageShape im, Layer layer, bool sel, bool hov, double px)
    {
        var bmp = _asBurned && layer.MaterialTone is { BareColour: not null, BurnColour: not null } curve
            ? Burned(im, curve)
            : _pictures.GetValue(im.Source, s => CameraImaging.ToAvaloniaRgba(s.Pixels));
        var t = im.Transform;
        using (ctx.PushTransform(new Matrix(t.A, t.B, t.C, t.D, t.E, t.F)))
        // A picture that isn't sent (another piece's share, a reference photo) is clearly dimmer than one that is.
        using (ctx.PushOpacity(layer.Output && im.Output ? 1 : 0.3))
        {
            // A clipped picture shows only what's inside its outline.
            using var clip = im.Clip is { } c0 ? ctx.PushGeometryClip(ClipGeometry(c0)) : default(DrawingContext.PushedState?);
            ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), new Rect(0, 0, im.SizeMm.X, im.SizeMm.Y));
        }
        var c = im.Corners;
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(P(c[0]), false);
            for (int i = 1; i < 4; i++) gc.LineTo(P(c[i]));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LayerColor(layer)), (sel ? 2.2 : hov ? 2 : 1) * px), g);
        if (sel) ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 0.8 * px, new DashStyle([3, 3], 0)), g);
    }

    static readonly Color DoneColor = Color.Parse("#4FAF83"), NowColor = Color.Parse("#E8B04A"), LaserColor = Color.Parse("#FF4D3D");

    /// <summary>Marked paths in green, the one being marked in amber, the laser as a red dot (or a red scan line on fills).</summary>
    void DrawRun(DrawingContext ctx, BuiltJob job, RunMarker m)
    {
        double px = 1 / _scale;
        StreamGeometry Path(BuiltJob.Step st)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(P(st.Points[0]), st.Fill);
            for (int k = 1; k < st.Points.Count; k++) c.LineTo(P(st.Points[k]));
            c.EndFigure(st.Closed);
            return g;
        }
        var donePen = new Pen(new SolidColorBrush(DoneColor), 2 * px);
        var doneFill = new SolidColorBrush(DoneColor, 0.28);
        foreach (int i in m.Done)
        {
            if (i >= job.Steps.Count || job.Steps[i].Points.Count == 0) continue;
            var st = job.Steps[i];
            ctx.DrawGeometry(st.Fill ? doneFill : null, donePen, Path(st));
        }
        var nowPen = new Pen(new SolidColorBrush(NowColor), 2.4 * px);
        foreach (int i in m.Current)
        {
            if (i >= job.Steps.Count || job.Steps[i].Points.Count == 0) continue;
            ctx.DrawGeometry(null, nowPen, Path(job.Steps[i]));
        }
        if (m.Sweep is { } sw)
        {
            ctx.DrawLine(new Pen(new SolidColorBrush(LaserColor, 0.3), 7 * px), P(sw.A), P(sw.B));
            ctx.DrawLine(new Pen(new SolidColorBrush(LaserColor), 1.8 * px), P(sw.A), P(sw.B));
        }
        if (m.Head is { } h)
        {
            var c = P(h);
            ctx.DrawEllipse(new SolidColorBrush(LaserColor, 0.18), null, c, 14 * px, 14 * px);
            ctx.DrawEllipse(new SolidColorBrush(LaserColor, 0.4), null, c, 7 * px, 7 * px);
            ctx.DrawEllipse(new SolidColorBrush(LaserColor), new Pen(Brushes.White, 1.2 * px), c, 3.5 * px, 3.5 * px);
        }
    }

    // ---- processing order preview ---------------------------------------------
    /// <summary>The order gradient, first to last: blue, cyan, green, yellow, orange, red (reads on the dark canvas).</summary>
    static readonly Color[] OrderStops = [Color.Parse("#4F8CFF"), Color.Parse("#22D3EE"), Color.Parse("#4ADE80"), Color.Parse("#FACC15"), Color.Parse("#F97316"), Color.Parse("#EF4444")];
    /// <summary>Paths are drawn in this many colour bands (one geometry each, so big jobs stay quick).</summary>
    const int OrderBands = 48;
    /// <summary>Above this many cuts the travel moves aren't drawn: they'd hide the order in a web of lines.</summary>
    const int MaxTravelSteps = 300;

    static Color OrderColor(double t)
    {
        t = Math.Clamp(t, 0, 1) * (OrderStops.Length - 1);
        int i = Math.Min(OrderStops.Length - 2, (int)t);
        double f = t - i;
        Color a = OrderStops[i], b = OrderStops[i + 1];
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
    }

    BuiltJob? _orderFor;
    readonly List<(Color Color, StreamGeometry Paths)> _orderBands = [];
    StreamGeometry? _orderTravel;
    int _orderPaths;

    /// <summary>
    /// Each path coloured by when it's first cut (later passes of the same path aren't drawn again),
    /// grouped into colour bands; the travel moves only for small jobs.
    /// </summary>
    void BuildOrder(BuiltJob job)
    {
        _orderFor = job;
        _orderBands.Clear();
        var seen = new HashSet<(long, long, long, long, int)>();
        var firsts = new List<BuiltJob.Step>();
        foreach (var st in job.Steps)
        {
            if (st.Points.Count == 0) continue;
            static long R(double v) => (long)Math.Round(v * 1000);
            if (seen.Add((R(st.Points[0].X), R(st.Points[0].Y), R(st.Points[^1].X), R(st.Points[^1].Y), st.Points.Count))) firsts.Add(st);
        }
        _orderPaths = firsts.Count;
        int n = firsts.Count;
        for (int band = 0; band < OrderBands; band++)
        {
            int from = band * n / OrderBands, to = (band + 1) * n / OrderBands;
            if (to <= from) continue;
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = from; i < to; i++)
                {
                    var st = firsts[i];
                    c.BeginFigure(P(st.Points[0]), false);
                    for (int k = 1; k < st.Points.Count; k++) c.LineTo(P(st.Points[k]));
                    c.EndFigure(st.Closed);
                }
            _orderBands.Add((OrderColor((band + 0.5) / OrderBands), g));
        }
        _orderTravel = null;
        if (job.Steps.Count <= MaxTravelSteps)
        {
            _orderTravel = new StreamGeometry();
            using var c = _orderTravel.Open();
            var pos = new Vec2(0, 0);
            foreach (var st in job.Steps.Where(x => x.Points.Count > 0))
            {
                c.BeginFigure(P(pos), false);
                c.LineTo(P(st.Points[0]));
                c.EndFigure(false);
                pos = st.Closed ? st.Points[0] : st.Points[^1];
            }
        }
    }

    void DrawPreview(DrawingContext ctx, BuiltJob job)
    {
        double px = 1 / _scale;
        if (!ReferenceEquals(job, _orderFor)) BuildOrder(job);
        if (_orderTravel is not null)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 160, 170, 185)), 0.8 * px, new DashStyle([2, 3], 0)), _orderTravel);
        foreach (var (color, paths) in _orderBands)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.6 * px), paths);
        ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(160, 245, 165, 36)), px, new DashStyle([6, 4], 0)),
            new Rect(job.Bounds.MinX, job.Bounds.MinY, job.Bounds.Width, job.Bounds.Height));
    }

    /// <summary>The key, at the top of the work area: the gradient with its first and last ends named.</summary>
    void DrawOrderLegend(DrawingContext ctx, BuiltJob job)
    {
        const double BarW = 260, BarH = 10, Pad = 10;
        var title = Text("Processing Order Gradient", 12, Brushes.White);
        var first = Text("First", 11, new SolidColorBrush(OrderStops[0]));
        var last = Text("Last", 11, new SolidColorBrush(OrderStops[^1]));
        // Counted as the job bar counts them; spreading the heat cuts long paths in pieces, said too.
        int paths = job.Paths > 0 ? job.Paths : _orderPaths;
        string about = $"{paths:N0} path{(paths == 1 ? "" : "s")}" +
                       (_orderPaths > paths ? $" in {_orderPaths:N0} pieces" : "") +
                       (job.Steps.Count > _orderPaths ? $", {job.Steps.Count:N0} cuts with the passes" : "") +
                       (_orderTravel is null ? " · travel moves hidden" : " · dashed: travel");
        var sub = Text(about, 10.5, new SolidColorBrush(Color.FromRgb(170, 178, 190)));
        double w = Math.Max(BarW, Math.Max(title.Width, sub.Width)) + 2 * Pad;
        double h = Pad + title.Height + 6 + first.Height + 2 + BarH + 4 + sub.Height + Pad;
        var vp = Viewport;
        var box = new Rect(vp.X + (vp.Width - w) / 2, vp.Y + 10, w, h);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(225, 22, 26, 33)), box, 6);
        ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), box.Deflate(0.5), 6);
        double y = box.Y + Pad;
        ctx.DrawText(title, new Point(box.X + (w - title.Width) / 2, y));
        y += title.Height + 6;
        double barX = box.X + (w - BarW) / 2;
        ctx.DrawText(first, new Point(barX, y));
        ctx.DrawText(last, new Point(barX + BarW - last.Width, y));
        y += first.Height + 2;
        var stops = new GradientStops();
        for (int i = 0; i < OrderStops.Length; i++) stops.Add(new GradientStop(OrderStops[i], (double)i / (OrderStops.Length - 1)));
        var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = stops };
        var bar = new Rect(barX, y, BarW, BarH);
        ctx.FillRectangle(gradient, bar, 3);
        // The ends marked: a tick under each.
        var tick = new Pen(Brushes.White, 1.5);
        ctx.DrawLine(tick, new Point(bar.Left + 1, bar.Top - 2), new Point(bar.Left + 1, bar.Bottom + 2));
        ctx.DrawLine(tick, new Point(bar.Right - 1, bar.Top - 2), new Point(bar.Right - 1, bar.Bottom + 2));
        y += BarH + 4;
        ctx.DrawText(sub, new Point(box.X + (w - sub.Width) / 2, y));
    }

    void DrawCreatePreview(DrawingContext ctx)
    {
        var g = CreateGeometry();
        if (g is null) return;
        var pen = new Pen(new SolidColorBrush(AccentColor), 1.5 / _scale);
        foreach (var f in g.Flatten(0.5 / _scale))
            for (int i = 1; i < f.Points.Count; i++) ctx.DrawLine(pen, P(f.Points[i - 1]), P(f.Points[i]));
    }

    Rect2 SelectionBounds(Document doc) => doc.State.Bounds(doc.Selection);

    void DrawSelection(DrawingContext ctx, Document doc)
    {
        if (doc.Selection.Count == 0 || _drag == Drag.Create) return;
        if (SelectedLines() is { } lines) { DrawLineGrips(ctx, lines); return; }
        var b = SelectionBounds(doc);
        if (b.IsEmpty) return;
        var r = ToScreen(b).Inflate(4);
        ctx.DrawRectangle(SelBoxPen, r);
        foreach (var h in HandlePoints(r))
            ctx.DrawRectangle(HandleFill, HandlePen, new Rect(h.X - HandleSize / 2, h.Y - HandleSize / 2, HandleSize, HandleSize), 1.5, 1.5);
        var top = new Point(r.Center.X, r.Top);
        var rot = new Point(r.Center.X, r.Top - RotateOffset);
        ctx.DrawLine(HandlePen, top, rot);
        ctx.DrawEllipse(HandleFill, HandlePen, rot, 5, 5);

        // Size label under the selection.
        var label = $"{Units.Num(b.Width)} × {Units.Num(b.Height)} {Units.Name}";
        var ft = Text(label, 11, Brushes.White);
        var lr = new Rect(r.Center.X - ft.Width / 2 - 6, r.Bottom + 8, ft.Width + 12, ft.Height + 4);
        ctx.FillRectangle(new SolidColorBrush(AccentColor), lr, 4);
        ctx.DrawText(ft, new Point(lr.X + 6, lr.Y + 2));
    }

    static Point[] HandlePoints(Rect r) =>
    [
        r.TopLeft, new(r.Center.X, r.Top), r.TopRight, new(r.Right, r.Center.Y),
        r.BottomRight, new(r.Center.X, r.Bottom), r.BottomLeft, new(r.Left, r.Center.Y),
    ];

    void DrawBox(DrawingContext ctx)
    {
        var r = new Rect(_pressScreen, _lastScreen).Normalize();
        bool crossing = _lastScreen.X < _pressScreen.X;
        ctx.FillRectangle(crossing ? CrossBoxFill : WindowBoxFill, r);
        ctx.DrawRectangle(crossing ? CrossBoxPen : WindowBoxPen, r);
    }

    void DrawRulers(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.FillRectangle(RulerBrush, new Rect(0, 0, w, RulerSize));
        ctx.FillRectangle(RulerBrush, new Rect(0, 0, RulerSize, h));
        // Ruler steps in the unit shown (millimetres, or inches split in quarters).
        double unit = Units.PerUnit;
        double[] steps = Units.Inches ? [0.125, 0.25, 0.5, 1, 2, 5, 10] : [1, 2, 5, 10, 20, 50, 100];
        double label = steps.FirstOrDefault(s => s * unit * _scale >= 50, steps[^1]);
        double tick = label / (Units.Inches ? 4 : 5);
        // Horizontal
        var x0 = Math.Floor(ToWorld(new Point(RulerSize, 0)).X / unit / tick) * tick;
        for (double x = x0; ToScreen(new Vec2(x * unit, 0)).X < w; x += tick)
        {
            double sx = Math.Round(ToScreen(new Vec2(x * unit, 0)).X) + 0.5;
            if (sx < RulerSize) continue;
            bool major = Math.Abs(x / label - Math.Round(x / label)) < 1e-6;
            ctx.DrawLine(RulerTick, new(sx, major ? 6 : 15), new(sx, RulerSize));
            if (major) ctx.DrawText(Text(Fmt(x), 10, RulerText), new(sx + 3, 2));
        }
        var y0 = Math.Floor(ToWorld(new Point(0, RulerSize)).Y / unit / tick) * tick;
        for (double y = y0; ToScreen(new Vec2(0, y * unit)).Y < h; y += tick)
        {
            double sy = Math.Round(ToScreen(new Vec2(0, y * unit)).Y) + 0.5;
            if (sy < RulerSize) continue;
            bool major = Math.Abs(y / label - Math.Round(y / label)) < 1e-6;
            ctx.DrawLine(RulerTick, new(major ? 6 : 15, sy), new(RulerSize, sy));
            if (major)
            {
                var ft = Text(Fmt(y), 10, RulerText);
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(2, sy - 3)))
                    ctx.DrawText(ft, new(0, 0));
            }
        }
        // Cursor markers
        if (_cursor is { } c)
        {
            var sc = ToScreen(c);
            var pen = new Pen(new SolidColorBrush(AccentColor), 1);
            ctx.DrawLine(pen, new(sc.X, 0), new(sc.X, RulerSize));
            ctx.DrawLine(pen, new(0, sc.Y), new(RulerSize, sc.Y));
        }
        ctx.FillRectangle(RulerBrush, new Rect(0, 0, RulerSize, RulerSize));
        ctx.DrawLine(RulerTick, new(0, RulerSize - 0.5), new(w, RulerSize - 0.5));
        ctx.DrawLine(RulerTick, new(RulerSize - 0.5, 0), new(RulerSize - 0.5, h));
    }

    static string Fmt(double v) => Math.Round(v, 3).ToString("0.##", CultureInfo.InvariantCulture);

    static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);

    // ---- hit testing -----------------------------------------------------------
    Shape? HitShape(Point screen)
    {
        if (_doc is null) return null;
        var w = ToWorld(screen);
        double tol = 5 / _scale;
        var layers = _doc.State.Layers.ToDictionary(l => l.Id);
        // Topmost first, pictures under everything else (as drawn); selected shapes win ties so a
        // selection is easy to drag.
        Shape? hit = null, picture = null;
        foreach (var s in _doc.State.Shapes.Reverse())
        {
            if (!s.Visible || !layers.TryGetValue(s.LayerId, out var l) || !l.Visible || l.Locked || s.Locked) continue;
            if (!s.HitTest(w, tol, insideCounts: l.Mode == LayerMode.Fill)) continue;
            if (_doc.Selection.Contains(s.Id)) return s;
            if (s is ImageShape) picture ??= s;
            else hit ??= s;
        }
        return hit ?? picture;
    }

    /// <summary>0–7 = scale handles (clockwise from top-left), 8 = rotate, -1 = none.</summary>
    int HitHandle(Point screen)
    {
        if (_doc is null || _doc.Selection.Count == 0 || SelectedLines() is not null) return -1;
        var b = SelectionBounds(_doc);
        if (b.IsEmpty) return -1;
        var r = ToScreen(b).Inflate(4);
        var rot = new Point(r.Center.X, r.Top - RotateOffset);
        if (Dist(screen, rot) <= 8) return 8;
        var hs = HandlePoints(r);
        for (int i = 0; i < hs.Length; i++)
            if (Math.Abs(screen.X - hs[i].X) <= HandleSize && Math.Abs(screen.Y - hs[i].Y) <= HandleSize) return i;
        return -1;
    }

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    void UpdateCursor(Point? screen)
    {
        StandardCursorType c = StandardCursorType.Arrow;
        if (_tool == Tool.Pan || _spaceHeld || _drag == Drag.Pan) c = _drag == Drag.Pan ? StandardCursorType.SizeAll : StandardCursorType.Hand;
        else if (_tool is Tool.Rect or Tool.Ellipse or Tool.Polygon or Tool.Star or Tool.Line or Tool.Pen or Tool.Wand or Tool.Magnetic or Tool.Measure or Tool.Align2 or Tool.Arc or Tool.Centreline) c = StandardCursorType.Cross;
        else if (_tool is Tool.Trim or Tool.Scissors or Tool.Tabs or Tool.Start) c = StandardCursorType.Hand;
        else if (_tool == Tool.Eraser) c = StandardCursorType.Cross;
        else if (_tool == Tool.Text) c = StandardCursorType.Ibeam;
        else if (screen is { } gp && _drag == Drag.None && HitLineGrip(gp) is { } grip)
            c = grip.Which == 1 ? StandardCursorType.SizeAll : StandardCursorType.Cross;
        else if (screen is { } p && _drag == Drag.None)
        {
            int h = HitHandle(p);
            c = h switch
            {
                0 or 4 => StandardCursorType.TopLeftCorner,
                2 or 6 => StandardCursorType.TopRightCorner,
                1 or 5 => StandardCursorType.SizeNorthSouth,
                3 or 7 => StandardCursorType.SizeWestEast,
                8 => StandardCursorType.Hand,
                _ => _hover is not null ? StandardCursorType.SizeAll : StandardCursorType.Arrow,
            };
        }
        Cursor = new Cursor(c);
    }

    // ---- input ----------------------------------------------------------------
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_doc is null) return;
        var pt = e.GetCurrentPoint(this);
        var p = pt.Position;
        _pressScreen = _lastScreen = p;
        _pressWorld = ToWorld(p);
        _editStarted = false;
        var mods = e.KeyModifiers;
        _lastMods = mods;

        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && (_spaceHeld || _tool == Tool.Pan)))
        {
            _drag = Drag.Pan;
            e.Pointer.Capture(this);
            UpdateCursor(p);
            return;
        }
        if (pt.Properties.IsRightButtonPressed && LineInProgress)
        {
            // Right-click finishes the lines, as Enter does.
            _lineFrom = null;
            _typed = "";
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (pt.Properties.IsRightButtonPressed && _tool is Tool.Select or Tool.Nodes && _drag == Drag.None)
        {
            // Right-click acts on what's under the mouse: select it (with its group) unless it's already selected.
            if (HitShape(p) is { } under)
            {
                if (!_doc.Selection.Contains(under.Id))
                    _doc.Select(mods.HasFlag(KeyModifiers.Alt) ? [under.Id] : Ops.WithGroups(_doc.State, [under.Id]));
            }
            else _doc.Select([]);
            return; // the menu opens on release (ContextRequested)
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        if (GuidePressed(p)) return;

        if (_tool == Tool.Text)
        {
            TextRequested?.Invoke(_pressWorld);
            return;
        }
        if (_tool == Tool.Pen)
        {
            PenPressed(e);
            return;
        }
        if (_tool == Tool.Wand)
        {
            WandPressed();
            return;
        }
        if (_tool == Tool.Magnetic)
        {
            MagneticPressed(e);
            return;
        }
        if (_tool == Tool.Nodes)
        {
            NodesPressed(e, p);
            return;
        }
        if (_tool == Tool.Measure) { MeasurePressed(); return; }
        if (_tool == Tool.Trim) { TrimPressed(p); return; }
        if (_tool == Tool.Align2) { AlignPressed(); return; }
        if (_tool == Tool.Scissors) { ScissorsPressed(p); return; }
        if (_tool == Tool.Tabs) { TabPressed(p, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); return; }
        if (_tool == Tool.Start) { StartPressed(p, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); return; }
        if (_tool == Tool.Eraser) { if (ErasePressed(p)) e.Pointer.Capture(this); return; }
        if (ConstructMethod is { } method) { ConstructPressed(method); return; }
        if (_tool == Tool.Line && _lineFrom is { } start)
        {
            // Click by click: this click ends the line, and the next one starts here.
            AddLine(start, LineEnd(start, SnapPick(_pressWorld, null, start)));
            InvalidateVisual();
            return;
        }
        if (_tool is Tool.Rect or Tool.Ellipse or Tool.Polygon or Tool.Star or Tool.Line)
        {
            _drag = Drag.Create;
            _pressWorld = SnapPick(_pressWorld);
            _guides.Clear();
            _createEnd = _pressWorld;
            return;
        }

        // Double-click: edit a path's points, or a text's words.
        if (e.ClickCount >= 2 && HitShape(p) is { } dbl)
        {
            if (dbl is TextShape) TextEditRequested?.Invoke(dbl.Id);
            else if (dbl is CodeShape) CodeEditRequested?.Invoke(dbl.Id);
            else if (dbl is PathShape)
            {
                _doc.Select([dbl.Id]);
                Tool = Tool.Nodes;
            }
            return;
        }

        if (_tool == Tool.Select && LineGripPressed(p)) return;
        int handle = HitHandle(p);
        // On one of the selection's own snap points (a corner, an edge's middle), just inside it: a move by
        // that point, not a resize (the handles sit just outside the selection).
        if (handle is >= 0 and not 8 && SelectionBounds(_doc).Contains(_pressWorld) && HitShape(p) is not null)
        {
            var picked = _doc.Selection;
            if (FindObjectSnap(_pressWorld, s => picked.Contains(s.Id), null, GrabKinds) is not null) handle = -1;
        }
        if (handle >= 0)
        {
            _handle = handle;
            _drag = handle == 8 ? Drag.Rotate : Drag.Scale;
            _dragIds = [.. _doc.Selection];
            _dragBounds = SelectionBounds(_doc);
            _base = _doc.State;
            PrepareSnap(_dragIds);
            return;
        }

        var hit = HitShape(p);
        if (hit is not null)
        {
            // A grouped shape brings its whole group, unless Alt picks just that one.
            var unit = mods.HasFlag(KeyModifiers.Alt) ? [hit.Id] : Ops.WithGroups(_doc.State, [hit.Id]);
            if (mods.HasFlag(KeyModifiers.Shift))
            {
                bool remove = _doc.Selection.Contains(hit.Id);
                _doc.Select(remove ? _doc.Selection.Except(unit) : _doc.Selection.Union(unit));
                if (remove) { _drag = Drag.None; return; }
            }
            else if (!_doc.Selection.Contains(hit.Id))
                _doc.Select(unit);
            _drag = Drag.Move;
            _dragIds = [.. _doc.Selection];
            _dragBounds = SelectionBounds(_doc);
            _base = _doc.State;
            PrepareSnap(_dragIds);
            // Picked up by one of its snap points (a corner, a centre…): that point snaps onto other shapes' points.
            var sel = _doc.Selection;
            _grab = FindObjectSnap(_pressWorld, s => sel.Contains(s.Id), null, GrabKinds)?.At;
            return;
        }

        _drag = Drag.Box;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var w = ToWorld(p);
        _cursor = w;
        CursorMoved?.Invoke(w);
        var mods = e.KeyModifiers;
        _lastMods = mods;

        if (_guideDrag is not null) { GuideMoved(p); _lastScreen = p; return; }
        if (_tool == Tool.Eraser) { EraseMoved(w); _lastScreen = p; InvalidateVisual(); return; }
        _snapHit = null;
        switch (_drag)
        {
            case Drag.None:
                // Where a click would land: the snap marker shows before clicking.
                if (PicksPoints && _tool != Tool.Measure)
                {
                    var at = SnapPick(w, null, PickFrom);
                    _pick = at;
                    if (_tool == Tool.Line) _createEnd = at;
                }
                else if (_tool == Tool.Select && _doc is { } d0 && d0.Selection.Count > 0 && SelectedLines() is null)
                {
                    var sel = d0.Selection;
                    _snapHit = FindObjectSnap(w, s => sel.Contains(s.Id), null, GrabKinds);
                }
                if (ConstructMethod is { } method) ConstructMoved(method, w);
                if (_tool == Tool.Pen && _pen.Count > 0) InvalidateVisual();
                if (_tool == Tool.Wand) ScheduleWandPreview(w);
                if (_tool == Tool.Measure) MeasureMoved(w);
                if (_tool == Tool.Trim || _tool == Tool.Scissors) TrimHover(p);
                var hov = _tool is Tool.Select or Tool.Nodes ? HitShape(p)?.Id : null;
                if (hov != _hover) _hover = hov;
                UpdateCursor(p);
                InvalidateVisual();
                break;
            case Drag.Pan:
                _offset += p - _lastScreen;
                InvalidateVisual();
                break;
            case Drag.Box:
                InvalidateVisual();
                break;
            case Drag.Create:
                _createEnd = SnapPick(w, null, _tool == Tool.Line ? _pressWorld : null);
                InvalidateVisual();
                break;
            case Drag.PenHandle:
                PenMoved(w);
                break;
            case Drag.NodeAnchor or Drag.NodeHandle:
                NodesMoved(p, w);
                break;
            case Drag.LineGrip:
                LineGripMoved(p, w);
                break;
            case Drag.Move when _doc is not null && _base is not null:
                if (!StartEditIfMoved(p)) break;
                var d = w - _pressWorld;
                if (mods.HasFlag(KeyModifiers.Shift)) d = Math.Abs(d.X) >= Math.Abs(d.Y) ? new(d.X, 0) : new(0, d.Y);
                bool lockY = mods.HasFlag(KeyModifiers.Shift) && d.Y == 0, lockX = mods.HasFlag(KeyModifiers.Shift) && d.X == 0;
                var moved = new Rect2(_dragBounds.MinX + d.X, _dragBounds.MinY + d.Y, _dragBounds.MaxX + d.X, _dragBounds.MaxY + d.Y);
                var dragged = _dragIds;
                if (_grab is { } grab && !SnapSuspended && FindObjectSnap(grab + d, s => !dragged.Contains(s.Id), null) is { } onto)
                {
                    // The point it's held by lands on another shape's point.
                    d = onto.At - grab;
                    _snapHit = onto;
                    _guides.Clear();
                }
                else d += SnapBox(moved, allowX: !lockX, allowY: !lockY);
                _doc.UpdateEdit(Ops.Move(_base, _dragIds, d.X, d.Y));
                break;
            case Drag.Scale when _doc is not null && _base is not null:
                if (!StartEditIfMoved(p)) break;
                _doc.UpdateEdit(Ops.Transform(_base, _dragIds, ScaleTransform(SnapHandle(w), mods)));
                break;
            case Drag.Rotate when _doc is not null && _base is not null:
                if (!StartEditIfMoved(p)) break;
                var c = _dragBounds.Center;
                double a0 = Math.Atan2(_pressWorld.Y - c.Y, _pressWorld.X - c.X), a1 = Math.Atan2(w.Y - c.Y, w.X - c.X);
                double deg = (a1 - a0) * 180 / Math.PI;
                if (mods.HasFlag(KeyModifiers.Shift)) deg = Math.Round(deg / 15) * 15;
                _doc.UpdateEdit(Ops.Transform(_base, _dragIds, Affine.RotateAbout(c, deg)));
                break;
        }
        _lastScreen = p;
    }

    bool StartEditIfMoved(Point p)
    {
        if (_editStarted) return true;
        if (Dist(p, _pressScreen) < DragThreshold || _doc is null) return false;
        _doc.BeginEdit();
        _editStarted = true;
        return true;
    }

    Affine ScaleTransform(Vec2 w, KeyModifiers mods)
    {
        var b = _dragBounds;
        // Handle positions: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L.
        int hx = _handle switch { 0 or 6 or 7 => -1, 2 or 3 or 4 => 1, _ => 0 };
        int hy = _handle switch { 0 or 1 or 2 => -1, 4 or 5 or 6 => 1, _ => 0 };
        bool fromCenter = mods.HasFlag(KeyModifiers.Alt);
        var anchor = fromCenter ? b.Center : new Vec2(hx < 0 ? b.MaxX : hx > 0 ? b.MinX : b.Center.X, hy < 0 ? b.MaxY : hy > 0 ? b.MinY : b.Center.Y);
        var grab = new Vec2(hx < 0 ? b.MinX : hx > 0 ? b.MaxX : b.Center.X, hy < 0 ? b.MinY : hy > 0 ? b.MaxY : b.Center.Y);
        double sx = 1, sy = 1;
        if (hx != 0 && Math.Abs(grab.X - anchor.X) > 1e-9) sx = (w.X - anchor.X) / (grab.X - anchor.X);
        if (hy != 0 && Math.Abs(grab.Y - anchor.Y) > 1e-9) sy = (w.Y - anchor.Y) / (grab.Y - anchor.Y);
        bool corner = hx != 0 && hy != 0;
        // Corners keep proportions unless Shift is held; edges stretch one axis.
        if (corner && !mods.HasFlag(KeyModifiers.Shift))
        {
            double s = Math.Abs(sx) >= Math.Abs(sy) ? sx : sy;
            sx = sy = s;
        }
        static double Guard(double v) => Math.Abs(v) < 1e-3 ? (v < 0 ? -1e-3 : 1e-3) : v;
        return Affine.ScaleAbout(anchor, Guard(sx), Guard(sy));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        _lastScreen = p;
        var drag = _drag;
        _drag = Drag.None;
        _guides.Clear();
        e.Pointer.Capture(null);
        if (_doc is null) return;
        if (_guideDrag is not null) { GuideReleased(p); return; }
        if (_tool == Tool.Eraser) { EraseReleased(); InvalidateVisual(); return; }
        if (_tool == Tool.Measure) MeasureReleased();
        if (ConstructMethod is { } method) ConstructReleased(method, p);
        switch (drag)
        {
            case Drag.Move when _editStarted: _doc.EndEdit(_dragIds.Count == 1 ? "Move shape" : "Move shapes"); break;
            case Drag.Scale when _editStarted: _doc.EndEdit("Resize"); break;
            case Drag.Rotate when _editStarted: _doc.EndEdit("Rotate"); break;
            case Drag.Box: FinishBox(p, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); break;
            case Drag.Create when _tool == Tool.Line && Dist(p, _pressScreen) < DragThreshold:
                // A click, not a drag: lines click by click from here.
                _lineFrom = _pressWorld;
                _createEnd = _pressWorld;
                break;
            case Drag.Create: FinishCreate(); break;
            case Drag.NodeAnchor or Drag.NodeHandle when _editStarted: _doc.EndEdit("Edit points"); break;
            case Drag.LineGrip when _editStarted: _doc.EndEdit(_grip?.Which == 1 ? "Move line" : "Move line end"); break;
        }
        _editStarted = false;
        _base = null;
        _grab = null;
        _grip = null;
        if (!PicksPoints) _snapHit = null;
        UpdateCursor(p);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag != Drag.None) CancelDrag();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        _hover = null;
        _snapHit = null;
        _wandPreview = null;
        _wandTicket++;
        CursorMoved?.Invoke(null);
        InvalidateVisual();
    }

    /// <summary>Snap the dragged scale handle along the axes it changes.</summary>
    Vec2 SnapHandle(Vec2 w)
    {
        int hx = _handle switch { 0 or 6 or 7 => -1, 2 or 3 or 4 => 1, _ => 0 };
        int hy = _handle switch { 0 or 1 or 2 => -1, 4 or 5 or 6 => 1, _ => 0 };
        return SnapPoint(w, hx != 0, hy != 0);
    }

    void CancelDrag()
    {
        _guides.Clear();
        if (_editStarted) _doc?.CancelEdit();
        _editStarted = false;
        _drag = Drag.None;
        _base = null;
        InvalidateVisual();
    }

    void FinishBox(Point p, bool additive)
    {
        if (_doc is null) return;
        if (Dist(p, _pressScreen) < DragThreshold)
        {
            if (!additive) _doc.Select([]);
            return;
        }
        var r = Rect2.FromPoints(ToWorld(_pressScreen), ToWorld(p));
        bool crossing = p.X < _pressScreen.X;
        // Left→right selects what is fully inside; right→left also what it touches (CAD convention).
        var picked = Ops.WithGroups(_doc.State, _doc.State.Editable()
            .Where(s => r.Contains(s.Bounds) || (crossing && s.Touches(r)))
            .Select(s => s.Id));
        _doc.Select(additive ? _doc.Selection.Union(picked) : picked);
    }

    PathGeometry? CreateGeometry()
    {
        var a = _pressWorld;
        var b = _createEnd;
        var mods = _lastMods;
        if (_tool == Tool.Line)
        {
            if (mods.HasFlag(KeyModifiers.Shift))
            {
                // Snap to 45° steps.
                var d = b - a;
                double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4), len = d.Length;
                b = a + new Vec2(Math.Cos(ang) * len, Math.Sin(ang) * len);
            }
            return a.Dist(b) < 1e-6 ? null : PathGeometry.Line(a, b);
        }
        double w = b.X - a.X, h = b.Y - a.Y;
        if (mods.HasFlag(KeyModifiers.Shift))
        {
            double s = Math.Max(Math.Abs(w), Math.Abs(h));
            w = Math.Sign(w == 0 ? 1 : w) * s;
            h = Math.Sign(h == 0 ? 1 : h) * s;
        }
        Rect2 r = mods.HasFlag(KeyModifiers.Alt)
            ? new Rect2(a.X - Math.Abs(w), a.Y - Math.Abs(h), a.X + Math.Abs(w), a.Y + Math.Abs(h))
            : Rect2.FromPoints(a, a + new Vec2(w, h));
        if (r.Width < 1e-6 || r.Height < 1e-6) return null;
        return _tool switch
        {
            Tool.Ellipse => PathGeometry.Ellipse(r.Center.X, r.Center.Y, r.Width / 2, r.Height / 2),
            Tool.Polygon => RegularShape(r, Math.Clamp(PolygonSides, 3, 64), 1),
            Tool.Star => RegularShape(r, Math.Clamp(StarPoints, 3, 64), Math.Clamp(StarInner, 0.05, 0.95)),
            _ => PathGeometry.Rect(r.MinX, r.MinY, r.Width, r.Height),
        };
    }

    /// <summary>Regular polygon (inner = 1) or star (inner &lt; 1) filling the box, pointing up.</summary>
    static PathGeometry RegularShape(Rect2 box, int corners, double inner)
    {
        var pts = new List<Vec2>();
        int n = inner < 1 ? corners * 2 : corners;
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n;
            double k = inner < 1 && i % 2 == 1 ? inner : 1;
            pts.Add(new Vec2(Math.Cos(a) * k, Math.Sin(a) * k));
        }
        // Scale the unit shape so its own bounds fill the dragged box.
        var b = Rect2.Bounds(pts);
        var fitted = pts.Select(p => new Vec2(box.MinX + (p.X - b.MinX) / b.Width * box.Width, box.MinY + (p.Y - b.MinY) / b.Height * box.Height)).ToList();
        return PathGeometry.Polyline(fitted, true);
    }

    KeyModifiers _lastMods;

    void FinishCreate()
    {
        if (_doc is null) return;
        var g = CreateGeometry();
        var bounds = g?.Bounds();
        if (g is null || bounds is not { } bb || Math.Max(bb.Width, bb.Height) < 0.05) return;
        if (DrawLayer() is not { } lay) return;
        var name = _tool switch { Tool.Rect => "Rectangle", Tool.Ellipse => "Ellipse", Tool.Polygon => "Polygon", Tool.Star => "Star", _ => "Line" };
        // Rectangles, ellipses, polygons and stars stay editable (size, corners, sides).
        PathShape shape = _tool switch
        {
            Tool.Rect => ParamShape.Create(ParamKind.Rectangle, bb.Width, bb.Height, 0, 4, 0.5, lay.Id, Affine.Translate(bb.MinX, bb.MinY)),
            Tool.Ellipse => ParamShape.Create(ParamKind.Ellipse, bb.Width, bb.Height, 0, 0, 0.5, lay.Id, Affine.Translate(bb.MinX, bb.MinY)),
            Tool.Polygon => ParamShape.Create(ParamKind.Polygon, bb.Width, bb.Height, 0, Math.Clamp(PolygonSides, 3, 64), 0.5, lay.Id, Affine.Translate(bb.MinX, bb.MinY)),
            Tool.Star => ParamShape.Create(ParamKind.Star, bb.Width, bb.Height, 0, Math.Clamp(StarPoints, 3, 64), Math.Clamp(StarInner, 0.05, 0.95), lay.Id, Affine.Translate(bb.MinX, bb.MinY)),
            _ => new PathShape { Name = name, LayerId = lay.Id, Geometry = g },
        };
        // A line isn't selected (the Line tool carries on, and handles would be in the way); other shapes are.
        _doc.Commit($"Draw {name.ToLowerInvariant()}", Ops.AddShapes(_doc.State, [shape]), _tool == Tool.Line ? [] : [shape.Id]);
        // The Line tool stays on for the next line; the other shapes go back to Select.
        if (_tool != Tool.Line) Tool = Tool.Select;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _offset += new Vector(e.Delta.Y * 40, 0);
            InvalidateVisual();
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _offset += new Vector(0, e.Delta.Y * 40);
            InvalidateVisual();
        }
        else ZoomAt(p, Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _lastMods = e.KeyModifiers;
        if (_doc is null) return;
        if (e.Key == Key.Space && !_spaceHeld)
        {
            _spaceHeld = true;
            UpdateCursor(null);
            e.Handled = true;
            return;
        }
        if (_drag == Drag.Create || ConstructMethod is not null) { InvalidateVisual(); }
        if (_tool == Tool.Pen && PenKey(e)) { e.Handled = true; return; }
        if (ConstructKey(e)) { e.Handled = true; return; }
        if (_tool == Tool.Line && LineKey(e)) { e.Handled = true; return; }
        if (_tool == Tool.Magnetic && MagneticKey(e)) { e.Handled = true; return; }
        if (_tool == Tool.Nodes && NodesKey(e)) { e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (_drag != Drag.None) CancelDrag();
            else if (_tool != Tool.Select) Tool = Tool.Select;
            else _doc.Select([]);
            e.Handled = true;
            return;
        }
        if (_drag != Drag.None) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 0.1 : 1;
        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };
        if ((dx != 0 || dy != 0) && _doc.Selection.Count > 0)
        {
            _doc.Commit("Nudge", Ops.Move(_doc.State, _doc.Selection, dx, dy));
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _lastMods = e.KeyModifiers;
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            UpdateCursor(null);
        }
        if (_drag == Drag.Create || ConstructMethod is not null) InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _lastMods = e.KeyModifiers;
    }

    /// <summary>World point at the centre of the visible canvas (for pasting).</summary>
    public Vec2 ViewCenter => ToWorld(Viewport.Center);
}
