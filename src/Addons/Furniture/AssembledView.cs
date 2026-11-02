using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// One piece of furniture put together, drawn in 3D from above and to one side: every piece at its
/// thickness where it sits, the etched face showing its detail. Drag to turn it round.
/// </summary>
public sealed class AssembledView : Control
{
    /// <summary>A flat face of one piece in the room: its outlines (holes included), facing <see cref="Normal"/>.</summary>
    sealed record Face(List<V3[]> Loops, V3 Normal, double Shade, PathsD? Etched, List<PathD>? Lines, Pose? Pose, double W);

    readonly List<Face> _faces = [];
    V3 _centre;
    double _yaw = 32, _pitch = 24;
    Point? _drag;
    string? _message;

    public AssembledView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(ItemKit? kit, double thickness, string? message = null)
    {
        _faces.Clear();
        _message = message;
        if (kit is not null)
        {
            double T = thickness;
            var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                {
                    V3 At(PointD p, double w) => pose.At(p.x, p.y, w);
                    var n = pose.N;
                    var top = outline.Select(c => c.Select(p => At(p, T / 2)).ToArray()).ToList();
                    var bottom = outline.Select(c => c.Select(p => At(p, -T / 2)).ToArray()).ToList();
                    _faces.Add(new Face(top, n, 1, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, pose, T / 2));
                    _faces.Add(new Face(bottom, -n, 1, null, null, null, 0));
                    foreach (var c in outline)
                        for (int i = 0; i < c.Count; i++)
                        {
                            var a = c[i];
                            var b = c[(i + 1) % c.Count];
                            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                            if (len < 1e-6) continue;
                            // Outward: to the right of the edge, as outlines run anticlockwise and holes clockwise.
                            var side = pose.U * (dy / len) + pose.V * (-dx / len);
                            _faces.Add(new Face([[At(a, T / 2), At(b, T / 2), At(b, -T / 2), At(a, -T / 2)]], side, 0.86, null, null, null, 0));
                        }
                    foreach (var q in top.Concat(bottom).SelectMany(l => l))
                    {
                        lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                        hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                    }
                }
            }
            _centre = (lo + hi) * 0.5;
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
            if (_message is { } m) PiecesView.Message(ctx, Bounds, m);
            return;
        }
        var (r, u, d) = View();
        Point Flat(V3 p) { var q = p - _centre; return new Point(q.Dot(r), -q.Dot(u)); }
        // Fit what's seen into the view.
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
        // Far faces first; faces turned away are left out.
        var seen = _faces.Where(f => f.Normal.Dot(d) > 1e-6)
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
            var wood = Color.FromRgb(0xDD, 0xBE, 0x92);
            var brush = new SolidColorBrush(Color.FromRgb((byte)(wood.R * lit), (byte)(wood.G * lit), (byte)(wood.B * lit)));
            ctx.DrawGeometry(brush, edge, g);
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
