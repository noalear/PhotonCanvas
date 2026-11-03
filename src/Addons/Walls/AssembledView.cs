using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// One wall put together, with a column at each end, drawn in 3D from in front, above and to one side:
/// every piece at its thickness where it sits, the etched faces showing their texture. Drag to turn it round.
/// </summary>
public sealed class AssembledView : Control
{
    /// <summary>A flat face of one piece: its outlines (holes included), facing <see cref="Normal"/>.</summary>
    sealed record Face(List<V3[]> Loops, V3 Normal, double Shade, PathsD? Etched, List<PathD>? Lines, Pose? Pose, double W, int Piece, bool Big);

    /// <summary>One piece where it sits: its middle plane and the corners of its box, for working out what's in front of what.</summary>
    sealed record Slab(V3 O, V3 N, V3[] Corners, double Half);

    readonly List<Face> _faces = [];
    readonly List<Slab> _slabs = [];
    V3 _centre;
    double _yaw = 28, _pitch = 22;
    Point? _drag;
    string? _message;

    static readonly Color Wood = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));

    public AssembledView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(WallKit? kit, double thickness, string? message = null)
    {
        _faces.Clear();
        _slabs.Clear();
        _message = message;
        if (kit is not null)
        {
            double T = thickness;
            var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var part in kit.Parts)
            {
                if (part.Poses.Count == 0) continue;
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                var box = Geo.Bounds(outline);
                foreach (var pose in part.Poses)
                {
                    int piece = _slabs.Count;
                    _slabs.Add(new Slab(pose.O, pose.N, [.. from u in new[] { box.left, box.right } from v in new[] { box.top, box.bottom } from w in new[] { -T / 2, T / 2 } select pose.At(u, v, w)], T / 2));
                    V3 At(PointD p, double w) => pose.At(p.x, p.y, w);
                    var n = pose.N;
                    var top = outline.Select(c => c.Select(p => At(p, T / 2)).ToArray()).ToList();
                    var bottom = outline.Select(c => c.Select(p => At(p, -T / 2)).ToArray()).ToList();
                    _faces.Add(new Face(top, n, 1, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, pose, T / 2, piece, true));
                    _faces.Add(new Face(bottom, n * -1, 1, null, null, null, 0, piece, true));
                    foreach (var c in outline)
                        for (int i = 0; i < c.Count; i++)
                        {
                            var a = c[i];
                            var b = c[(i + 1) % c.Count];
                            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                            if (len < 1e-6) continue;
                            // Outward: to the right of the edge, as outlines run anticlockwise and holes clockwise.
                            var side = pose.U * (dy / len) + pose.V * (-dx / len);
                            _faces.Add(new Face([[At(a, T / 2), At(b, T / 2), At(b, -T / 2), At(a, -T / 2)]], side, 0.86, null, null, null, 0, piece, false));
                        }
                    foreach (var q in top.Concat(bottom).SelectMany(l => l))
                    {
                        lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                        hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                    }
                }
            }
            _centre = (lo + hi) * 0.5;
            if (_faces.Count == 0) _message ??= "Nothing to show.";
        }
        InvalidateVisual();
    }

    (V3 Right, V3 Up, V3 Toward) View()
    {
        double y = _yaw * Math.PI / 180, p = _pitch * Math.PI / 180;
        var d = new V3(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p));
        var r = new V3(Math.Cos(y), 0, -Math.Sin(y));
        return (r, d.Cross(r), d);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_faces.Count == 0)
        {
            if (_message is { } m)
            {
                var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
                { MaxTextWidth = Math.Max(50, Bounds.Width - 40), TextAlignment = TextAlignment.Center };
                ctx.DrawText(text, new Point(20, Bounds.Height / 2 - text.Height / 2));
            }
            return;
        }
        var (r, u, d) = View();
        Point Flat(V3 p) { var q = p - _centre; return new Point(q.Dot(r), -q.Dot(u)); }
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in _faces)
            foreach (var l in f.Loops)
                foreach (var q in l)
                {
                    var s = Flat(q);
                    x0 = Math.Min(x0, s.X); x1 = Math.Max(x1, s.X); y0 = Math.Min(y0, s.Y); y1 = Math.Max(y1, s.Y);
                }
        double k = Math.Min((Bounds.Width - 30) / Math.Max(1e-6, x1 - x0), (Bounds.Height - 40) / Math.Max(1e-6, y1 - y0));
        if (!(k > 0)) return;
        double ox = Bounds.Width / 2 - (x0 + x1) / 2 * k, oy = Bounds.Height / 2 - (y0 + y1) / 2 * k;
        Point Screen(V3 p) { var s = Flat(p); return new Point(ox + s.X * k, oy + s.Y * k); }

        var light = new V3(0.35, 0.85, 0.45);
        light *= 1 / light.Length;
        var edge = new Pen(EdgeBrush, 0.6);
        var score = new Pen(ScoreBrush, Math.Clamp(k * 0.05, 0.3, 0.9));
        // Pieces from the back to the front; each piece's edges before its face. Faces turned away are left out.
        var order = Order(d, Screen);
        var rank = new int[_slabs.Count];
        for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
        var seen = _faces.Where(f => f.Normal.Dot(d) > 1e-6)
                         .OrderBy(f => rank[f.Piece]).ThenBy(f => f.Big ? 1 : 0).ThenBy(f => Middle(f.Loops[0]).Dot(d));
        foreach (var f in seen)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
                foreach (var l in f.Loops)
                {
                    c.BeginFigure(Screen(l[0]), true);
                    for (int i = 1; i < l.Length; i++) c.LineTo(Screen(l[i]));
                    c.EndFigure(true);
                }
            }
            double lit = (0.62 + 0.38 * Math.Max(0, f.Normal.Dot(light))) * f.Shade;
            var brush = new SolidColorBrush(Color.FromRgb((byte)(Wood.R * lit), (byte)(Wood.G * lit), (byte)(Wood.B * lit)));
            ctx.DrawGeometry(brush, edge, g);
            if (f.Pose is { } pose)
            {
                Point On(PointD p) => Screen(pose.At(p.x, p.y, f.W));
                if (f.Etched is { } et) ctx.DrawGeometry(Burnt, null, Shape(et, On, true));
                if (f.Lines is { } ln) ctx.DrawGeometry(null, score, Shape(ln, On, false));
            }
        }
        var hint = new FormattedText("Drag to turn it round", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
    }

    /// <summary>
    /// The order to draw the pieces in: a piece wholly behind another's plane (as seen) goes first; pieces
    /// that cross each other's planes go by how far away their middles are.
    /// </summary>
    List<int> Order(V3 d, Func<V3, Point> screen)
    {
        int n = _slabs.Count;
        var depth = _slabs.Select(sl => Middle(sl.Corners).Dot(d)).ToArray();
        var rects = _slabs.Select(sl =>
        {
            var pts = sl.Corners.Select(screen).ToList();
            return new Rect(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
        }).ToArray();
        var after = new List<int>[n];
        var before = new int[n];
        for (int i = 0; i < n; i++) after[i] = [];
        // +1: b is in front of a's plane, −1 behind it, 0 neither (it crosses it, or lies in it).
        static int Side(Slab a, Slab b, V3 d)
        {
            var nrm = a.N.Dot(d) >= 0 ? a.N : a.N * -1;
            double eps = a.Half + 0.05, lo = double.MaxValue, hi = double.MinValue;
            foreach (var c in b.Corners) { double t = (c - a.O).Dot(nrm); lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
            if (lo >= -eps && hi > eps) return 1;
            if (hi <= eps && lo < -eps) return -1;
            return 0;
        }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (!rects[i].Intersects(rects[j])) continue;
                int s = Side(_slabs[i], _slabs[j], d);
                if (s == 0) s = -Side(_slabs[j], _slabs[i], d);
                if (s == 1) { after[i].Add(j); before[j]++; }
                else if (s == -1) { after[j].Add(i); before[i]++; }
            }
        // Kahn's sort, the farthest ready piece first; a loop is broken at its farthest piece.
        var done = new bool[n];
        var order = new List<int>(n);
        while (order.Count < n)
        {
            int pick = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && before[i] == 0 && (pick < 0 || depth[i] < depth[pick])) pick = i;
            if (pick < 0)
                for (int i = 0; i < n; i++)
                    if (!done[i] && (pick < 0 || depth[i] < depth[pick])) pick = i;
            done[pick] = true;
            order.Add(pick);
            foreach (var j in after[pick]) before[j]--;
        }
        return order;
    }

    /// <summary>The middle of a face's box (a face's corners crowd where its outline is detailed, so their average leans that way).</summary>
    V3 Middle(V3[] loop)
    {
        double x0 = loop.Min(q => q.X), x1 = loop.Max(q => q.X), y0 = loop.Min(q => q.Y), y1 = loop.Max(q => q.Y), z0 = loop.Min(q => q.Z), z1 = loop.Max(q => q.Z);
        return new V3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2) - _centre;
    }

    static StreamGeometry Shape(IEnumerable<PathD> paths, Func<PointD, Point> on, bool closed)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var p in paths)
        {
            if (p.Count < 2) continue;
            c.BeginFigure(on(p[0]), closed);
            for (int i = 1; i < p.Count; i++) c.LineTo(on(p[i]));
            c.EndFigure(closed);
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
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, -15, 85);
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
