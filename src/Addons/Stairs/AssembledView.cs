using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The floor and the way up put together in the room, drawn in 3D from above and to one side: every
/// piece at its thickness where it sits, the etched face showing its detail, and the walls and their
/// columns faint behind. Drag to turn it round; the floor can be lifted off to see under it.
/// </summary>
public sealed class AssembledView : Control
{
    /// <summary>A flat face: its outlines (holes included), facing <see cref="Normal"/>; ghosts are the walls, drawn faint.</summary>
    sealed record Face(List<V3[]> Loops, V3 Normal, double Shade, PathsD? Etched, List<PathD>? Lines, Pose? Pose, double W, bool Ghost, bool Floor, bool Tile = false);

    readonly List<Face> _faces = [];
    V3 _centre;
    double _yaw = 215, _pitch = 34;
    Point? _drag;
    string? _message;
    bool _liftFloor;

    static readonly Color WoodColour = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly Color GhostColour = Color.FromRgb(0xB8, 0xB8, 0xC0);

    public AssembledView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Leave the floor out of the picture, to see what's under it.</summary>
    public bool LiftFloor
    {
        get => _liftFloor;
        set { _liftFloor = value; InvalidateVisual(); }
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(FloorKit? kit, double thickness, string? message = null)
    {
        _faces.Clear();
        _message = message;
        if (kit is not null)
        {
            var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
            void Solid(PathsD cut, Pose pose, double T, PathsD? etched, List<PathD>? lines, bool ghost, bool floor)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(cut, 0.02));
                V3 At(PointD p, double w) => pose.At(p.x, p.y, w);
                var n = pose.N;
                // Big faces go in tiles, so drawing the far ones first puts each part of them in the right order.
                var tiles = Tiles(outline, etched, lines).ToList();
                foreach (var (tile, te, tl) in tiles)
                {
                    _faces.Add(new Face(tile.Select(c => c.Select(p => At(p, T / 2)).ToArray()).ToList(), n, 1, te, tl, pose, T / 2, ghost, floor, tiles.Count > 1));
                    _faces.Add(new Face(tile.Select(c => c.Select(p => At(p, -T / 2)).ToArray()).ToList(), -n, 1, null, null, null, 0, ghost, floor, tiles.Count > 1));
                }
                foreach (var c in outline)
                    for (int i = 0; i < c.Count; i++)
                    {
                        var a = c[i];
                        var b = c[(i + 1) % c.Count];
                        double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                        if (len < 1e-6) continue;
                        // Outward: to the right of the edge, as outlines run anticlockwise and holes clockwise.
                        var side = pose.U * (dy / len) + pose.V * (-dx / len);
                        int bits = Math.Max(1, (int)Math.Ceiling(len / TileMm));
                        for (int j = 0; j < bits; j++)
                        {
                            var p0 = new PointD(a.x + dx * j / bits, a.y + dy * j / bits);
                            var p1 = new PointD(a.x + dx * (j + 1) / bits, a.y + dy * (j + 1) / bits);
                            _faces.Add(new Face([[At(p0, T / 2), At(p1, T / 2), At(p1, -T / 2), At(p0, -T / 2)]], side, 0.86, null, null, null, 0, ghost, floor));
                        }
                    }
                foreach (var c in outline)
                    foreach (var p in c)
                        foreach (var q in new[] { At(p, T / 2), At(p, -T / 2) })
                        {
                            lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                            hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                        }
            }
            foreach (var g in kit.Ghosts) Solid(g.Outline, g.Pose, g.Thick, null, null, true, false);
            foreach (var part in kit.Parts)
                foreach (var pose in part.Poses)
                    Solid(part.Cut, pose, thickness, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, false, part.Name.StartsWith("Floor"));
            _centre = (lo + hi) * 0.5;
        }
        InvalidateVisual();
    }

    /// <summary>The largest a tile of a face is (mm).</summary>
    const double TileMm = 12;

    /// <summary>A face cut in tiles no bigger than <see cref="TileMm"/>, each with its share of the etching and lines.</summary>
    static IEnumerable<(PathsD Tile, PathsD? Etched, List<PathD>? Lines)> Tiles(PathsD outline, PathsD? etched, List<PathD>? lines)
    {
        var b = Geo.Bounds(outline);
        double w = b.right - b.left, h = b.bottom - b.top;
        int nx = Math.Max(1, (int)Math.Ceiling(w / TileMm)), ny = Math.Max(1, (int)Math.Ceiling(h / TileMm));
        if (nx * ny == 1) { yield return (outline, etched, lines); yield break; }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var cell = new PathsD { Geo.Rect(b.left + w * i / nx, b.top + h * j / ny, b.left + w * (i + 1) / nx, b.top + h * (j + 1) / ny) };
                var tile = Geo.Intersect(outline, cell);
                if (tile.Count == 0) continue;
                var te = etched is null ? null : Geo.Intersect(etched, cell);
                var tl = lines is null ? null : Geo.ClipLines(lines, cell);
                yield return (tile, te is { Count: > 0 } ? te : null, tl is { Count: > 0 } ? tl : null);
            }
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
            if (_message is { } m) PiecesView.Message(ctx, Bounds, m);
            return;
        }
        var (r, u, d) = View();
        Point Flat(V3 p) { var q = p - _centre; return new Point(q.Dot(r), -q.Dot(u)); }
        // Fit everything into the view (the same with the floor lifted off, so the picture doesn't jump).
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
        var edge = new Pen(PiecesView.EdgeBrush, 0.7);
        var ghostEdge = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x60, 0x60, 0x70)), 0.6);
        // Far faces first; faces turned away are left out.
        var seen = _faces.Where(f => f.Normal.Dot(d) > 1e-6 && !(f.Floor && _liftFloor))
                         .Select(f => (Face: f, Depth: f.Loops[0].Average(q => (q - _centre).Dot(d))))
                         .OrderBy(x => x.Depth);
        foreach (var (f, _) in seen)
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
            var col = f.Ghost ? GhostColour : WoodColour;
            var brush = new SolidColorBrush(Color.FromArgb(f.Ghost ? (byte)0x80 : (byte)0xFF, (byte)(col.R * lit), (byte)(col.G * lit), (byte)(col.B * lit)));
            // Tiles of one face are edged in their own colour, so the joins between them don't show.
            ctx.DrawGeometry(brush, f.Tile ? (f.Ghost ? null : new Pen(brush, 0.8)) : f.Ghost ? ghostEdge : edge, g);
            if (f.Pose is { } pose)
            {
                Point On(PointD p) => Screen(pose.At(p.x, p.y, f.W));
                if (f.Etched is { } et) ctx.DrawGeometry(PiecesView.Burnt, null, Shape(et, On, true));
                if (f.Lines is { } ln) ctx.DrawGeometry(null, new Pen(PiecesView.ScoreBrush, Math.Clamp(k * 0.05, 0.3, 0.9)), Shape(ln, On, false));
            }
        }
        var hint = new FormattedText("Drag to turn it round", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
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
