using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// The roof or tower put together, drawn in 3D from above and to one side: every piece at its
/// thickness where it sits, the etched face showing its shingles or stonework. Drag to turn it round;
/// the wheel lifts the roof off (pulls the pieces apart upwards) to show inside.
/// </summary>
public sealed class ModelView : Control
{
    /// <summary>One copy of a piece in the model: its outline loops on both faces, and what's etched and scored on its top face.</summary>
    sealed record Slab(V3[][] Bottom, V3[][] Top, V3 Normal, V3 Centre, PathsD? Etched, List<PathD>? Lines, Pose Pose, double T, double Lift);

    readonly List<Slab> _slabs = [];
    V3 _centre;
    double _yaw = 35, _pitch = 28, _apart;
    Point? _drag;
    string? _message;

    static readonly Color Wood = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));
    static readonly IBrush[] Shades = [.. Enumerable.Range(0, 12).Select(i =>
    {
        double k = 0.45 + 0.55 * i / 11;
        return (IBrush)new SolidColorBrush(Color.FromRgb((byte)(Wood.R * k), (byte)(Wood.G * k), (byte)(Wood.B * k)));
    })];

    public ModelView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(RoofKit? kit, string? message = null)
    {
        _slabs.Clear();
        _message = message;
        if (kit is not null)
        {
            double T = kit.ThicknessMm;
            var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.12));
                foreach (var pose in part.Poses)
                {
                    var bottom = outline.Select(l => l.Select(p => pose.At(p.x, p.y, 0)).ToArray()).ToArray();
                    var top = outline.Select(l => l.Select(p => pose.At(p.x, p.y, T)).ToArray()).ToArray();
                    var all = top.SelectMany(l => l).Concat(bottom.SelectMany(l => l)).ToList();
                    if (all.Count == 0) continue;
                    // The middle of its box (not the average of its points: a ring with a gap would lean away from the gap).
                    var c = new V3((all.Min(q => q.X) + all.Max(q => q.X)) / 2, (all.Min(q => q.Y) + all.Max(q => q.Y)) / 2, (all.Min(q => q.Z) + all.Max(q => q.Z)) / 2);
                    foreach (var q in all)
                    {
                        lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                        hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                    }
                    _slabs.Add(new Slab(bottom, top, pose.N, c, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, pose, T, 0));
                }
            }
            _centre = (lo + hi) * 0.5;
            // Pieces are pulled apart upwards in order of height: the roof's slopes and caps come off first.
            double zSpan = Math.Max(1, hi.Z - lo.Z);
            for (int i = 0; i < _slabs.Count; i++) _slabs[i] = _slabs[i] with { Lift = (_slabs[i].Centre.Z - lo.Z) / zSpan };
        }
        InvalidateVisual();
    }

    (V3 Right, V3 Up, V3 Toward) View()
    {
        // Z is up in the model; the viewer looks from the −Y side, turned by the yaw.
        double y = _yaw * Math.PI / 180, p = _pitch * Math.PI / 180;
        var toward = new V3(Math.Sin(y) * Math.Cos(p), -Math.Cos(y) * Math.Cos(p), Math.Sin(p));
        var right = new V3(Math.Cos(y), Math.Sin(y), 0);
        return (right, toward.Cross(right), toward);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_slabs.Count == 0)
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
        double spread = _apart * 40;
        V3 Lifted(V3 p, Slab s) => p + new V3(0, 0, s.Lift * spread);
        Point Flat(V3 p) { var q = p - _centre; return new Point(q.Dot(r), -q.Dot(u)); }
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var s in _slabs)
            foreach (var l in s.Top)
                foreach (var q in l)
                {
                    var f = Flat(Lifted(q, s));
                    x0 = Math.Min(x0, f.X); x1 = Math.Max(x1, f.X); y0 = Math.Min(y0, f.Y); y1 = Math.Max(y1, f.Y);
                }
        double k = Math.Min((Bounds.Width - 30) / Math.Max(1e-6, x1 - x0), (Bounds.Height - 40) / Math.Max(1e-6, y1 - y0));
        if (!(k > 0)) return;
        double ox = Bounds.Width / 2 - (x0 + x1) / 2 * k, oy = Bounds.Height / 2 - (y0 + y1) / 2 * k;
        var light = new V3(-0.35, -0.45, 0.85).Unit;
        var edge = new Pen(Edge, 0.6);
        var score = new Pen(ScoreBrush, Math.Clamp(k * 0.05, 0.3, 0.9));

        foreach (var s in _slabs.OrderBy(s => Lifted(s.Centre, s).Dot(d)))
        {
            Point Screen(V3 p) { var f = Flat(Lifted(p, s)); return new Point(ox + f.X * k, oy + f.Y * k); }
            bool topSeen = s.Normal.Dot(d) > 0;
            var face = topSeen ? s.Top : s.Bottom;
            // The edges facing the viewer, shaded by how they face the light.
            var sides = new StreamGeometry[Shades.Length];
            var openSides = new StreamGeometryContext?[Shades.Length];
            for (int li = 0; li < s.Top.Length; li++)
            {
                var a = s.Bottom[li];
                var b = s.Top[li];
                for (int i = 0; i < a.Length; i++)
                {
                    int j = (i + 1) % a.Length;
                    var along = a[j] - a[i];
                    if (along.Length < 1e-9) continue;
                    var outward = along.Cross(s.Normal).Unit;
                    if (outward.Dot(d) <= 0) continue;
                    int shade = Math.Clamp((int)Math.Round((0.25 + 0.75 * Math.Max(0, outward.Dot(light))) * (Shades.Length - 1) * 0.85), 0, Shades.Length - 1);
                    if (openSides[shade] is not { } c)
                    {
                        sides[shade] = new StreamGeometry();
                        c = sides[shade].Open();
                        openSides[shade] = c;
                    }
                    c.BeginFigure(Screen(a[i]), true);
                    c.LineTo(Screen(a[j]));
                    c.LineTo(Screen(b[j]));
                    c.LineTo(Screen(b[i]));
                    c.EndFigure(true);
                }
            }
            for (int i = 0; i < Shades.Length; i++)
            {
                if (openSides[i] is not { } c) continue;
                c.Dispose();
                ctx.DrawGeometry(Shades[i], null, sides[i]);
            }
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
                foreach (var l in face)
                {
                    if (l.Length < 3) continue;
                    c.BeginFigure(Screen(l[0]), true);
                    for (int i = 1; i < l.Length; i++) c.LineTo(Screen(l[i]));
                    c.EndFigure(true);
                }
            }
            var n = topSeen ? s.Normal : -s.Normal;
            int lit = Math.Clamp((int)Math.Round((0.45 + 0.55 * Math.Max(0, n.Dot(light))) * (Shades.Length - 1)), 0, Shades.Length - 1);
            ctx.DrawGeometry(Shades[lit], edge, g);
            if (topSeen)
            {
                Point On(PointD p) => Screen(s.Pose.At(p.x, p.y, s.T));
                if (s.Etched is { } et) ctx.DrawGeometry(Burnt, null, Paths(et, On, true));
                if (s.Lines is { } ln) ctx.DrawGeometry(null, score, Paths(ln, On, false));
            }
        }
        var hint = new FormattedText("Drag to turn it round · scroll to lift the pieces apart", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
    }

    static StreamGeometry Paths(IEnumerable<PathD> paths, Func<PointD, Point> on, bool closed)
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
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, 5, 85);
        _drag = at;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _apart = Math.Clamp(_apart + e.Delta.Y * 0.15, 0, 3);
        InvalidateVisual();
        e.Handled = true;
    }
}
