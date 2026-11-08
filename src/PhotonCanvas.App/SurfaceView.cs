using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Core;

namespace PhotonCanvas.App;

/// <summary>
/// A curved surface in 3D: a mesh over the area, the measured points (red when they don't agree
/// with their neighbours), and the design's outline lying on it. Drag to turn it, scroll to zoom, middle-drag
/// or the arrow keys to move it (<see cref="View3D.ViewNavigator"/>). Heights are drawn
/// taller than life so small curves show; the factor is written in the corner. Before anything is
/// measured, <see cref="Plan"/> is drawn instead: the bed from above, with the area to measure.
/// </summary>
public sealed class SurfaceView : Control, View3D.INavigable
{
    /// <summary>What measuring will do, drawn from above while there's no surface yet.</summary>
    public sealed record SurfacePlan(Rect2 Bed, Rect2? Calibrated, Rect2 Area, IReadOnlyList<Vec2> Aims);

    /// <summary>Shown (from above) when there's no surface to draw.</summary>
    public SurfacePlan? Plan { get; set; }

    Surface? _surface;
    Rect2 _area = new(0, 0, 220, 220);
    IReadOnlyList<SurfacePoint> _points = [];
    IReadOnlySet<int> _suspect = new HashSet<int>();
    IReadOnlyList<IReadOnlyList<Vec2>> _design = [];
    double _yaw = -35, _pitch = 35;
    // Zoom (1 = the whole area fits) and how far the view's been moved (screen units).
    double _zoom = 1;
    Vector _offset;

    public SurfaceView()
    {
        var nav = new View3D.ViewNavigator(this, this);
        VisualChildren.Add(nav.Buttons);
        LogicalChildren.Add(nav.Buttons);
    }

    public void Show(Surface? surface, Rect2 area, IReadOnlyList<SurfacePoint> points, IReadOnlySet<int> suspect, IReadOnlyList<IReadOnlyList<Vec2>> design)
    {
        _surface = surface;
        _area = area.IsEmpty ? new Rect2(0, 0, 220, 220) : area;
        _points = points;
        _suspect = suspect;
        _design = design;
        InvalidateVisual();
    }

    public void Turn(double dx, double dy)
    {
        _yaw += dx * 0.5;
        _pitch = Math.Clamp(_pitch + dy * 0.4, 5, 89);
        InvalidateVisual();
    }

    public void Move(Vector by)
    {
        _offset += by;
        InvalidateVisual();
    }

    public void Zoom(double factor, Point? at)
    {
        double zoom = Math.Clamp(_zoom * factor, 0.2, 50), k = zoom / _zoom;
        // What's under the pointer stays there.
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2) + _offset;
        if (at is { } p) _offset += (p - c) * (1 - k);
        _zoom = zoom;
        InvalidateVisual();
    }

    public void Fit()
    {
        _zoom = 1;
        _offset = default;
        InvalidateVisual();
    }

    public void Reset()
    {
        _yaw = -35;
        _pitch = 35;
        Fit();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(b.Size));
        if (_surface is null && Plan is { } plan)
        {
            DrawPlan(ctx, b, plan);
            return;
        }
        if (_surface is null)
        {
            var hint = new FormattedText("Measure or type in a shape to see it here.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, UiKit.Brush("TextMuted"));
            ctx.DrawText(hint, new Point((b.Width - hint.Width) / 2, b.Height / 2 - 8));
            return;
        }
        // Heights over the area, and how much to stretch them so the shape reads.
        const int N = 28;
        var h = new double[N + 1, N + 1];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
            {
                h[i, j] = _surface.HeightAt(At(i / (double)N, j / (double)N));
                lo = Math.Min(lo, h[i, j]); hi = Math.Max(hi, h[i, j]);
            }
        double span = Math.Max(_area.Width, _area.Height), relief = Math.Max(0.01, hi - lo);
        double stretch = Math.Clamp(span * 0.25 / relief, 1, 50);
        double mid = (lo + hi) / 2;
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        double scale = Math.Min(b.Width, b.Height) * 0.75 / span * _zoom;
        double cx = b.Width / 2 + _offset.X, cy = b.Height / 2 + _offset.Y;
        Point P(Vec2 xy, double z)
        {
            double x = xy.X - _area.Center.X, y = xy.Y - _area.Center.Y, zz = (z - mid) * stretch;
            double rx = x * Math.Cos(yaw) - y * Math.Sin(yaw), ry = x * Math.Sin(yaw) + y * Math.Cos(yaw);
            // Looking down at the pitch angle: bed depth shortens, height rises.
            double sx = rx, sy = ry * Math.Sin(pitch) - zz * Math.Cos(pitch);
            return new Point(cx + sx * scale, cy + sy * scale);
        }
        var mesh = new Pen(new SolidColorBrush(Color.FromArgb(150, 127, 176, 255)), 1);
        for (int i = 0; i <= N; i++)
            for (int j = 0; j < N; j++)
            {
                ctx.DrawLine(mesh, P(At(i / (double)N, j / (double)N), h[i, j]), P(At(i / (double)N, (j + 1) / (double)N), h[i, j + 1]));
                ctx.DrawLine(mesh, P(At(j / (double)N, i / (double)N), h[j, i]), P(At((j + 1) / (double)N, i / (double)N), h[j + 1, i]));
            }
        // The design, lying on the surface.
        var ink = new Pen(new SolidColorBrush(Color.Parse("#FFB547")), 1.6);
        foreach (var path in _design)
            for (int k = 1; k < path.Count; k++)
                ctx.DrawLine(ink, P(path[k - 1], _surface.HeightAt(path[k - 1])), P(path[k], _surface.HeightAt(path[k])));
        // The points.
        for (int i = 0; i < _points.Count; i++)
        {
            var bad = _suspect.Contains(i);
            ctx.DrawEllipse(bad ? new SolidColorBrush(Color.Parse("#E5484D")) : Brushes.White, null, P(_points[i].At, _points[i].Height), bad ? 4 : 2.5, bad ? 4 : 2.5);
        }
        var label = new FormattedText($"Heights shown ×{stretch:0.#} · {lo:0.0}–{hi:0.0} mm · drag to turn · scroll to zoom", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(label, new Point(8, b.Height - 20));

        Vec2 At(double u, double v) => new(_area.MinX + _area.Width * u, _area.MinY + _area.Height * v);
    }

    /// <summary>The bed from above: where the pointer is calibrated, the area to measure with a dot per spot, and the design.</summary>
    void DrawPlan(DrawingContext ctx, Rect b, SurfacePlan plan)
    {
        var see = plan.Bed.Union(plan.Area).Inflate(8);
        const double top = 34, bottom = 52;
        double scale = Math.Min((b.Width - 24) / see.Width, (b.Height - top - bottom) / see.Height);
        if (scale <= 0) return;
        double ox = (b.Width - see.Width * scale) / 2 - see.MinX * scale, oy = top + (b.Height - top - bottom - see.Height * scale) / 2 - see.MinY * scale;
        Point P(Vec2 v) => new(ox + v.X * scale, oy + v.Y * scale);
        Rect R(Rect2 r) => new(P(r.TopLeft), P(r.BottomRight));
        var muted = UiKit.Brush("TextMuted");
        FormattedText Text(string t, double size, IBrush brush) =>
            new(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush);

        ctx.DrawText(Text("Seen from above: what Measure will do", 13, UiKit.Brush("TextMain")), new Point(12, 10));
        // The bed, with a line every 20 mm.
        ctx.FillRectangle(UiKit.Brush("Bg1"), R(plan.Bed));
        var faint = new Pen(UiKit.Brush("Line"), 1);
        for (double x = plan.Bed.MinX + 20; x < plan.Bed.MaxX; x += 20) ctx.DrawLine(faint, P(new Vec2(x, plan.Bed.MinY)), P(new Vec2(x, plan.Bed.MaxY)));
        for (double y = plan.Bed.MinY + 20; y < plan.Bed.MaxY; y += 20) ctx.DrawLine(faint, P(new Vec2(plan.Bed.MinX, y)), P(new Vec2(plan.Bed.MaxX, y)));
        ctx.DrawRectangle(null, new Pen(muted, 1.2), R(plan.Bed));
        var corner = Text("0, 0", 10, muted);
        ctx.DrawText(corner, P(plan.Bed.TopLeft) + new Point(3, 2));
        var far = Text($"{plan.Bed.MaxX:0}, {plan.Bed.MaxY:0}", 10, muted);
        ctx.DrawText(far, P(plan.Bed.BottomRight) - new Point(far.Width + 3, far.Height + 2));

        var ok = Color.Parse("#30A46C");
        if (plan.Calibrated is { } cal)
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, ok.R, ok.G, ok.B)), new Pen(new SolidColorBrush(ok), 1.4, new DashStyle([4, 3], 0)), R(cal));
        }
        foreach (var path in _design)
            for (int k = 1; k < path.Count; k++)
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FFB547")), 1.4), P(path[k - 1]), P(path[k]));

        bool offBed = !plan.Bed.Contains(plan.Area);
        var areaColour = offBed ? Color.Parse("#E5484D") : Color.Parse("#7FB0FF");
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, areaColour.R, areaColour.G, areaColour.B)), new Pen(new SolidColorBrush(areaColour), 1.8), R(plan.Area));
        foreach (var a in plan.Aims)
            ctx.DrawEllipse(plan.Bed.Contains(a) ? Brushes.White : new SolidColorBrush(Color.Parse("#E5484D")), null, P(a), 2.4, 2.4);

        var legend = Text(
            (offBed ? "Red: the area runs off the bed, so it can't be measured. " : "Blue: the area to measure, a dot for each spot the pointer is aimed at. ") +
            (plan.Calibrated is null ? "" : "Green: where the pointer is calibrated (the board goes under it). ") +
            "Orange: your design. Grid lines every 20 mm.", 11, muted);
        legend.MaxTextWidth = Math.Max(120, b.Width - 24 - 190); // clear of the zoom buttons
        ctx.DrawText(legend, new Point(12, b.Height - bottom + 6));
    }
}
