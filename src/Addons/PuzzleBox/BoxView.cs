using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>
/// The box put together, in 3D from above and to one side, at any step of opening it: every layer's
/// pieces at the sheet's thickness, the body that has just moved picked out, and one just taken out
/// shown beside the box (lifted off, or slid out in front). Drawn a layer at a time from the bottom:
/// seen from above, a higher layer is always in front of a lower one where they overlap. Drag to
/// turn it round; "see inside" makes the box's own layers and the tops over the hidden parts faint.
/// </summary>
public sealed class BoxView : Control
{
    readonly record struct P3(double X, double Y, double Z)
    {
        public static P3 operator +(P3 a, P3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static P3 operator *(P3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public double Dot(P3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public P3 Cross(P3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    }

    sealed record Piece(int Body, int Layer, PathsD Outline, PathsD? Etched);

    BoxDesign? _design;
    readonly List<Piece> _pieces = [];
    readonly Dictionary<int, Box2> _extent = [];
    Pose? _pose;
    int _moved = -1;
    double _yaw = 28, _pitch = 30;
    Point? _drag;
    string? _message;
    bool _inside;

    static readonly Color Highlight = Color.FromRgb(0xF0, 0xB8, 0x5C);

    public BoxView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Faint layers for the box itself, so the drawer, bolt and hooks inside show.</summary>
    public bool SeeInside
    {
        get => _inside;
        set { _inside = value; InvalidateVisual(); }
    }

    /// <summary>Shows a box, shut (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BoxDesign? d, string? message = null)
    {
        _design = d;
        _message = message;
        _pieces.Clear();
        _extent.Clear();
        _pose = null;
        _moved = -1;
        if (d is not null)
            for (int b = 0; b < d.Bodies.Count; b++)
            {
                var body = d.Bodies[b];
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var layer in body.Solid.Keys)
                {
                    var outline = body.Outline(layer);
                    if (outline.Count == 0) continue;
                    var etched = body.Etched.TryGetValue(layer, out var e) ? Geo.Intersect(Geo.Union(e), outline) : null;
                    _pieces.Add(new Piece(b, layer, outline, etched is { Count: > 0 } ? etched : null));
                    var bb = Geo.Bounds(outline);
                    x0 = Math.Min(x0, bb.left); x1 = Math.Max(x1, bb.right); y0 = Math.Min(y0, bb.top); y1 = Math.Max(y1, bb.bottom);
                }
                if (x1 > x0) _extent[b] = new Box2(x0, y0, x1, y1);
            }
        InvalidateVisual();
    }

    /// <summary>Shows the box in a pose along the way open; <paramref name="moved"/> is the body the last move moved (or took out).</summary>
    public void Step(Pose? pose, int moved)
    {
        _pose = pose;
        _moved = moved;
        InvalidateVisual();
    }

    /// <summary>
    /// Where a body is drawn in a pose: its offset on the plan and how many layers up. Bodies out of the
    /// box are hidden, except the one just taken out, which is shown clear of it.
    /// </summary>
    (bool Shown, double Dx, double Dy, int Up) Place(BoxDesign d, Pose? p, int b, int moved)
    {
        if (p is null) return (true, 0, 0, 0);
        if (!p.Out[b]) return (true, p.Dx[b], p.Dy[b], 0);
        if (b != moved || !_extent.TryGetValue(b, out var e)) return (false, 0, 0, 0);
        return d.Bodies[b].Role switch
        {
            BodyRole.Lid => (true, p.Dx[b], p.Dy[b], (int)Math.Ceiling(Math.Max(12, d.HeightMm * 0.35) / d.T)),
            _ => (true, p.Dx[b], p.Dy[b] - (e.Y1 + 8), 0),
        };
    }

    (P3 Right, P3 Up, P3 Toward) View()
    {
        double y = _yaw * Math.PI / 180, p = _pitch * Math.PI / 180;
        var d = new P3(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p));
        var r = new P3(Math.Cos(y), 0, -Math.Sin(y));
        return (r, d.Cross(r), d);
    }

    /// <summary>A point on the plan at a height, in the room: x across, up, towards the front (y runs to the back).</summary>
    static P3 Room(double x, double y, double h) => new(x, h, -y);

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        var d = _design;
        if (d is null || _pieces.Count == 0)
        {
            if (_message is { } m) PiecesView.Message(ctx, Bounds, m);
            return;
        }
        var (r, u, toward) = View();
        double T = d.T;
        var centre = Room(d.Spec.WidthMm / 2, d.Spec.DepthMm / 2, d.HeightMm / 2);
        Point Flat(P3 q) { var v = q - centre; return new Point(v.Dot(r), -v.Dot(u)); }

        // A scale that holds for every step of opening, so the box doesn't jump about.
        double sx0 = double.MaxValue, sy0 = double.MaxValue, sx1 = double.MinValue, sy1 = double.MinValue;
        var poses = d.Solution?.Poses ?? [];
        var moves = d.Solution?.Moves ?? [];
        void Take(Pose? p, int moved)
        {
            foreach (var (b, e) in _extent)
            {
                var (shown, dx, dy, up) = Place(d, p, b, moved);
                if (!shown) continue;
                var layers = d.Bodies[b].Solid.Keys;
                double h0 = (layers.Min() + up) * T, h1 = (layers.Max() + 1 + up) * T;
                foreach (var x in new[] { e.X0 + dx, e.X1 + dx })
                    foreach (var y in new[] { e.Y0 + dy, e.Y1 + dy })
                        foreach (var h in new[] { h0, h1 })
                        {
                            var s = Flat(Room(x, y, h));
                            sx0 = Math.Min(sx0, s.X); sx1 = Math.Max(sx1, s.X); sy0 = Math.Min(sy0, s.Y); sy1 = Math.Max(sy1, s.Y);
                        }
            }
        }
        Take(null, -1);
        for (int i = 1; i < poses.Count; i++) Take(poses[i], moves[i - 1].Body);
        double k = Math.Min((Bounds.Width - 30) / Math.Max(1e-6, sx1 - sx0), (Bounds.Height - 40) / Math.Max(1e-6, sy1 - sy0));
        if (!(k > 0)) return;
        double ox = Bounds.Width / 2 - (sx0 + sx1) / 2 * k, oy = Bounds.Height / 2 - (sy0 + sy1) / 2 * k - 6;
        Point Screen(P3 q) { var s = Flat(q); return new Point(ox + s.X * k, oy + s.Y * k); }

        var light = new P3(0.35, 0.85, 0.45);
        light *= 1 / Math.Sqrt(light.Dot(light));
        var edge = new Pen(PiecesView.EdgeBrush, 0.6);
        var faintEdge = new Pen(new SolidColorBrush(Color.FromArgb(70, 0x4A, 0x33, 0x1E)), 0.6);

        // Every piece shown, by the layer it's drawn in.
        var byLayer = new SortedDictionary<int, List<(Piece Piece, double Dx, double Dy)>>();
        foreach (var pc in _pieces)
        {
            var (shown, dx, dy, up) = Place(d, _pose, pc.Body, _moved);
            if (!shown) continue;
            int at = pc.Layer + up;
            if (!byLayer.TryGetValue(at, out var list)) byLayer[at] = list = [];
            list.Add((pc, dx, dy));
        }

        // Seeing inside, the box's own layers and the tops over the hidden parts (the lid, the panels' tops) are faint.
        bool Faint(int body, int layer) => _inside && (d.Bodies[body].Role == BodyRole.Frame
            || d.Bodies[body].Role is BodyRole.Panel or BodyRole.Lid && layer == d.Bodies[body].Solid.Keys.Max());
        IBrush Fill(int body, int layer, double lit)
        {
            var c = body == _moved ? Highlight : PiecesView.WoodColour;
            byte a = Faint(body, layer) ? (byte)45 : (byte)255;
            return new SolidColorBrush(Color.FromArgb(a, (byte)Math.Min(255, c.R * lit), (byte)Math.Min(255, c.G * lit), (byte)Math.Min(255, c.B * lit)));
        }
        Pen EdgeOf(int body, int layer) => Faint(body, layer) ? faintEdge : edge;

        foreach (var (layer, list) in byLayer)
        {
            double h0 = layer * T, h1 = h0 + T;
            // The sides first, far ones first; faces turned away left out.
            var sides = new List<(double Depth, Point[] Quad, int Body, int Layer, double Lit)>();
            foreach (var (pc, dx, dy) in list)
                foreach (var loop in pc.Outline)
                    for (int i = 0; i < loop.Count; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % loop.Count];
                        double ex = b.x - a.x, ey = b.y - a.y, len = Math.Sqrt(ex * ex + ey * ey);
                        if (len < 1e-6) continue;
                        // Outward on the plan is to the right of the edge (outlines anticlockwise, holes clockwise).
                        var n = new P3(ey / len, 0, ex / len);
                        if (n.Dot(toward) <= 1e-6) continue;
                        P3 A0 = Room(a.x + dx, a.y + dy, h0), B0 = Room(b.x + dx, b.y + dy, h0), B1 = Room(b.x + dx, b.y + dy, h1), A1 = Room(a.x + dx, a.y + dy, h1);
                        double depth = ((A0 + B1) * 0.5 - centre).Dot(toward);
                        double lit = (0.62 + 0.38 * Math.Max(0, n.Dot(light))) * 0.86;
                        sides.Add((depth, [Screen(A0), Screen(B0), Screen(B1), Screen(A1)], pc.Body, pc.Layer, lit));
                    }
            foreach (var (_, quad, body, pl, lit) in sides.OrderBy(s => s.Depth))
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(quad[0], true);
                    for (int i = 1; i < 4; i++) c.LineTo(quad[i]);
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(Fill(body, pl, lit), EdgeOf(body, pl), g);
            }
            // Then the tops, and what's etched on them.
            double topLit = 0.62 + 0.38 * Math.Max(0, light.Y);
            foreach (var (pc, dx, dy) in list)
            {
                Point On(PointD q) => Screen(Room(q.x + dx, q.y + dy, h1));
                ctx.DrawGeometry(Fill(pc.Body, pc.Layer, topLit), EdgeOf(pc.Body, pc.Layer), Shape(pc.Outline, On));
                if (pc.Etched is { } et && !Faint(pc.Body, pc.Layer)) ctx.DrawGeometry(PiecesView.Burnt, null, Shape(et, On));
            }
        }
        var hint = new FormattedText("Drag to turn it round", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
    }

    static StreamGeometry Shape(IEnumerable<PathD> paths, Func<PointD, Point> on)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var p in paths)
        {
            if (p.Count < 3) continue;
            c.BeginFigure(on(p[0]), true);
            for (int i = 1; i < p.Count; i++) c.LineTo(on(p[i]));
            c.EndFigure(true);
        }
        return g;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } from) return;
        var at = e.GetPosition(this);
        _yaw = (_yaw - (at.X - from.X) * 0.5) % 360;
        // Always from above: that's what makes drawing a layer at a time right.
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, 8, 85);
        _drag = at;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }
}
