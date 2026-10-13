using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using YTool.Core;
using YTool.Doc;
using PathGeometry = YTool.Doc.PathGeometry;

namespace YTool.App;

public enum Tool { Select, Rect, Ellipse, Polygon, Star, Line, Pen, Text, Nodes, Pan }

/// <summary>
/// The design canvas: machine work area in millimetres (+Y down, origin top-left),
/// with zoom/pan, selection, move/scale/rotate, drawing tools and the job preview.
/// </summary>
public sealed partial class CanvasView : Control
{
    // ---- appearance -----------------------------------------------------------
    static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#0E1014"));
    static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#161920"));
    static readonly IPen FieldPen = new Pen(new SolidColorBrush(Color.Parse("#3A4150")), 1);
    static readonly IBrush RulerBrush = new SolidColorBrush(Color.Parse("#15181E"));
    static readonly IBrush RulerText = new SolidColorBrush(Color.Parse("#7D8694"));
    static readonly IPen RulerTick = new Pen(new SolidColorBrush(Color.Parse("#3A4150")), 1);
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
    readonly ConditionalWeakTable<ImageSource, Avalonia.Media.Imaging.Bitmap> _pictures = new();

    enum Drag { None, Pan, Box, Move, Scale, Rotate, Create, PenHandle, NodeAnchor, NodeHandle }
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
            _node = null;
            _tool = value;
            CancelDrag();
            UpdateCursor(null);
            ToolChanged?.Invoke(value);
            InvalidateVisual();
        }
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

    double _cameraOpacity = 0.85;
    public double CameraOpacity
    {
        get => _cameraOpacity;
        set { _cameraOpacity = value; InvalidateVisual(); }
    }

    bool _showGrid = true;
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

    public void FitSelection()
    {
        if (_doc is null) return;
        var b = _doc.Selection.Count > 0 ? _doc.State.Bounds(_doc.Selection) : _doc.State.Bounds(_doc.State.Shapes.Select(s => s.Id));
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

    static StreamGeometry BuildGeometry(PathShape s)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(FillRule.EvenOdd);
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

    static Point P(Vec2 v) => new(v.X, v.Y);

    static Color LayerColor(Layer l, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(l.Color >> 16), (byte)(l.Color >> 8), (byte)l.Color);

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, b);
        var field = ToScreen(new Rect2(0, 0, _machine.FieldW, _machine.FieldH));
        ctx.FillRectangle(FieldBrush, field);
        if (_camera is { } cam)
            using (ctx.PushOpacity(_cameraOpacity))
                ctx.DrawImage(cam, new Rect(0, 0, cam.PixelSize.Width, cam.PixelSize.Height), field);
        if (_showGrid) DrawGrid(ctx, field);
        ctx.DrawRectangle(FieldPen, field.Deflate(0.5));

        if (_doc is { } doc)
        {
            var world = Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_offset.X, _offset.Y);
            using (ctx.PushTransform(world))
            {
                DrawShapes(ctx, doc);
                if (_preview is not null) DrawPreview(ctx, _preview);
                if (_drag == Drag.Create) DrawCreatePreview(ctx);
                if (_tool == Tool.Pen) DrawPen(ctx);
            }
            if (_tool == Tool.Nodes) DrawNodes(ctx);
            else DrawSelection(ctx, doc);
            if (_drag == Drag.Box) DrawBox(ctx);
            if (_drag != Drag.None) DrawGuides(ctx);
        }
        DrawRulers(ctx);
    }

    void DrawShapes(DrawingContext ctx, Document doc)
    {
        double px = 1 / _scale;
        var layers = doc.State.Layers.ToDictionary(l => l.Id);
        var selected = doc.Selection;
        var viewWorld = new Rect2(ToWorld(new Point(0, 0)).X, ToWorld(new Point(0, 0)).Y, ToWorld(new Point(Bounds.Width, Bounds.Height)).X, ToWorld(new Point(Bounds.Width, Bounds.Height)).Y);
        foreach (var shape in doc.State.Shapes)
        {
            if (!layers.TryGetValue(shape.LayerId, out var layer) || !layer.Visible) continue;
            if (shape is ImageShape im)
            {
                if (im.Bounds.Intersects(viewWorld)) DrawPicture(ctx, im, layer, selected.Contains(im.Id), _hover == im.Id, px);
                continue;
            }
            if (shape is not PathShape ps) continue;
            if (!ps.Bounds.Inflate(1).Intersects(viewWorld)) continue;
            var geom = GeometryFor(ps);
            byte alpha = layer.Output ? (byte)255 : (byte)110;
            IBrush? fill = layer.Mode == LayerMode.Fill ? new SolidColorBrush(LayerColor(layer, (byte)(layer.Output ? 60 : 25))) : null;
            bool sel = selected.Contains(shape.Id);
            bool hov = _hover == shape.Id && !sel;
            double w = sel ? 2.2 : hov ? 2 : 1.25;
            ctx.DrawGeometry(fill, new Pen(new SolidColorBrush(LayerColor(layer, alpha)), w * px), geom);
            if (sel)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 0.8 * px, new DashStyle([3, 3], 0)), geom);
        }
    }

    void DrawPicture(DrawingContext ctx, ImageShape im, Layer layer, bool sel, bool hov, double px)
    {
        var bmp = _pictures.GetValue(im.Source, s => CameraImaging.ToAvaloniaRgba(s.Pixels));
        var t = im.Transform;
        using (ctx.PushTransform(new Matrix(t.A, t.B, t.C, t.D, t.E, t.F)))
        using (ctx.PushOpacity(layer.Output ? 1 : 0.5))
            ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), new Rect(0, 0, im.SizeMm.X, im.SizeMm.Y));
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

    void DrawPreview(DrawingContext ctx, BuiltJob job)
    {
        double px = 1 / _scale;
        var travel = new Pen(new SolidColorBrush(Color.FromArgb(150, 160, 170, 185)), 0.8 * px, new DashStyle([2, 3], 0));
        var pos = new Vec2(0, 0);
        int n = job.Steps.Count;
        for (int i = 0; i < n; i++)
        {
            var st = job.Steps[i];
            if (st.Points.Count == 0) continue;
            ctx.DrawLine(travel, P(pos), P(st.Points[0]));
            // Early steps blue, late steps orange: reads the processing order at a glance.
            double t = n <= 1 ? 0 : (double)i / (n - 1);
            var col = Color.FromRgb((byte)(79 + t * (245 - 79)), (byte)(140 + t * (165 - 140)), (byte)(255 + t * (36 - 255)));
            var pen = new Pen(new SolidColorBrush(col), 1.6 * px);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(st.Points[0]), false);
                for (int k = 1; k < st.Points.Count; k++) c.LineTo(P(st.Points[k]));
                c.EndFigure(st.Closed);
            }
            ctx.DrawGeometry(null, pen, g);
            pos = st.Closed ? st.Points[0] : st.Points[^1];
        }
        ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(160, 245, 165, 36)), px, new DashStyle([6, 4], 0)),
            new Rect(job.Bounds.MinX, job.Bounds.MinY, job.Bounds.Width, job.Bounds.Height));
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
        var label = $"{b.Width:0.##} × {b.Height:0.##} mm";
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
        double[] steps = [1, 2, 5, 10, 20, 50, 100];
        double label = steps.FirstOrDefault(s => s * _scale >= 50, 100);
        double tick = label / 5;
        // Horizontal
        var x0 = Math.Floor(ToWorld(new Point(RulerSize, 0)).X / tick) * tick;
        for (double x = x0; ToScreen(new Vec2(x, 0)).X < w; x += tick)
        {
            double sx = Math.Round(ToScreen(new Vec2(x, 0)).X) + 0.5;
            if (sx < RulerSize) continue;
            bool major = Math.Abs(x / label - Math.Round(x / label)) < 1e-6;
            ctx.DrawLine(RulerTick, new(sx, major ? 6 : 15), new(sx, RulerSize));
            if (major) ctx.DrawText(Text(Fmt(x), 10, RulerText), new(sx + 3, 2));
        }
        var y0 = Math.Floor(ToWorld(new Point(0, RulerSize)).Y / tick) * tick;
        for (double y = y0; ToScreen(new Vec2(0, y)).Y < h; y += tick)
        {
            double sy = Math.Round(ToScreen(new Vec2(0, y)).Y) + 0.5;
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
        // Topmost first; selected shapes win ties so a selection is easy to drag.
        Shape? hit = null;
        foreach (var s in _doc.State.Shapes.Reverse())
        {
            if (!layers.TryGetValue(s.LayerId, out var l) || !l.Visible || l.Locked) continue;
            if (!s.HitTest(w, tol, insideCounts: l.Mode == LayerMode.Fill)) continue;
            if (_doc.Selection.Contains(s.Id)) return s;
            hit ??= s;
        }
        return hit;
    }

    /// <summary>0–7 = scale handles (clockwise from top-left), 8 = rotate, -1 = none.</summary>
    int HitHandle(Point screen)
    {
        if (_doc is null || _doc.Selection.Count == 0) return -1;
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
        else if (_tool is Tool.Rect or Tool.Ellipse or Tool.Polygon or Tool.Star or Tool.Line or Tool.Pen) c = StandardCursorType.Cross;
        else if (_tool == Tool.Text) c = StandardCursorType.Ibeam;
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
        if (!pt.Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);

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
        if (_tool == Tool.Nodes)
        {
            NodesPressed(e, p);
            return;
        }
        if (_tool is Tool.Rect or Tool.Ellipse or Tool.Polygon or Tool.Star or Tool.Line)
        {
            _drag = Drag.Create;
            PrepareSnap(new HashSet<Guid>());
            _pressWorld = SnapPoint(_pressWorld);
            _guides.Clear();
            _createEnd = _pressWorld;
            return;
        }

        // Double-click: edit a path's points, or a text's words.
        if (e.ClickCount >= 2 && HitShape(p) is { } dbl)
        {
            if (dbl is TextShape) TextEditRequested?.Invoke(dbl.Id);
            else if (dbl is PathShape)
            {
                _doc.Select([dbl.Id]);
                Tool = Tool.Nodes;
            }
            return;
        }

        int handle = HitHandle(p);
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

        switch (_drag)
        {
            case Drag.None:
                if (_tool == Tool.Pen && _pen.Count > 0) InvalidateVisual();
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
                _createEnd = SnapPoint(w);
                InvalidateVisual();
                break;
            case Drag.PenHandle:
                PenMoved(w);
                break;
            case Drag.NodeAnchor or Drag.NodeHandle:
                NodesMoved(p, w);
                break;
            case Drag.Move when _doc is not null && _base is not null:
                if (!StartEditIfMoved(p)) break;
                var d = w - _pressWorld;
                if (mods.HasFlag(KeyModifiers.Shift)) d = Math.Abs(d.X) >= Math.Abs(d.Y) ? new(d.X, 0) : new(0, d.Y);
                bool lockY = mods.HasFlag(KeyModifiers.Shift) && d.Y == 0, lockX = mods.HasFlag(KeyModifiers.Shift) && d.X == 0;
                var moved = new Rect2(_dragBounds.MinX + d.X, _dragBounds.MinY + d.Y, _dragBounds.MaxX + d.X, _dragBounds.MaxY + d.Y);
                d += SnapBox(moved, allowX: !lockX, allowY: !lockY);
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
        switch (drag)
        {
            case Drag.Move when _editStarted: _doc.EndEdit(_dragIds.Count == 1 ? "Move shape" : "Move shapes"); break;
            case Drag.Scale when _editStarted: _doc.EndEdit("Resize"); break;
            case Drag.Rotate when _editStarted: _doc.EndEdit("Rotate"); break;
            case Drag.Box: FinishBox(p, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); break;
            case Drag.Create: FinishCreate(); break;
            case Drag.NodeAnchor or Drag.NodeHandle when _editStarted: _doc.EndEdit("Edit points"); break;
        }
        _editStarted = false;
        _base = null;
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
        var shape = new PathShape { Name = name, LayerId = lay.Id, Geometry = g };
        _doc.Commit($"Draw {name.ToLowerInvariant()}", Ops.AddShapes(_doc.State, [shape]), [shape.Id]);
        Tool = Tool.Select;
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
        if (_drag == Drag.Create) { InvalidateVisual(); }
        if (_tool == Tool.Pen && PenKey(e)) { e.Handled = true; return; }
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
        if (_drag == Drag.Create) InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _lastMods = e.KeyModifiers;
    }

    /// <summary>World point at the centre of the visible canvas (for pasting).</summary>
    public Vec2 ViewCenter => ToWorld(Viewport.Center);
}
