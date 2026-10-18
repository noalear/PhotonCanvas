using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;

namespace PhotonCanvas.App;

/// <summary>
/// A curved surface in 3D: a mesh over the area, the measured points (red when they don't agree
/// with their neighbours), and the design's outline lying on it. Drag to turn it. Heights are drawn
/// taller than life so small curves show; the factor is written in the corner.
/// </summary>
public sealed class SurfaceView : Control
{
    Surface? _surface;
    Rect2 _area = new(0, 0, 220, 220);
    IReadOnlyList<SurfacePoint> _points = [];
    IReadOnlySet<int> _suspect = new HashSet<int>();
    IReadOnlyList<IReadOnlyList<Vec2>> _design = [];
    double _yaw = -35, _pitch = 35;
    Point? _drag;

    public void Show(Surface? surface, Rect2 area, IReadOnlyList<SurfacePoint> points, IReadOnlySet<int> suspect, IReadOnlyList<IReadOnlyList<Vec2>> design)
    {
        _surface = surface;
        _area = area.IsEmpty ? new Rect2(0, 0, 220, 220) : area;
        _points = points;
        _suspect = suspect;
        _design = design;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = e.GetPosition(this); e.Pointer.Capture(this); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = null; e.Pointer.Capture(null); }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is not { } from) return;
        var p = e.GetPosition(this);
        _yaw += (p.X - from.X) * 0.5;
        _pitch = Math.Clamp(_pitch + (p.Y - from.Y) * 0.4, 5, 89);
        _drag = p;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(b.Size));
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
        double scale = Math.Min(b.Width, b.Height) * 0.75 / span;
        Point P(Vec2 xy, double z)
        {
            double x = xy.X - _area.Center.X, y = xy.Y - _area.Center.Y, zz = (z - mid) * stretch;
            double rx = x * Math.Cos(yaw) - y * Math.Sin(yaw), ry = x * Math.Sin(yaw) + y * Math.Cos(yaw);
            // Looking down at the pitch angle: bed depth shortens, height rises.
            double sx = rx, sy = ry * Math.Sin(pitch) - zz * Math.Cos(pitch);
            return new Point(b.Width / 2 + sx * scale, b.Height / 2 + sy * scale);
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
        var label = new FormattedText($"Heights shown ×{stretch:0.#} · {lo:0.0}–{hi:0.0} mm · drag to turn", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(label, new Point(8, b.Height - 20));

        Vec2 At(double u, double v) => new(_area.MinX + _area.Width * u, _area.MinY + _area.Height * v);
    }
}
