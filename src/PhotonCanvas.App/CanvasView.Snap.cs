using Avalonia;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>Grid spacing (real millimetres) and colours.</summary>
public sealed record GridSettings(double MajorMm, double MinorMm, Color MajorColor, Color MinorColor)
{
    /// <summary>The usual grid (lighter in the light theme).</summary>
    public static GridSettings Default { get; set; } = new(10, 1, Color.Parse("#2A303B"), Color.Parse("#1D2129"));
}

/// <summary>
/// Smart snapping: while dragging, edges and centres line up with other
/// shapes' edges and centres and with the work area's edges and centrelines;
/// otherwise (if enabled) with the grid. Hold Ctrl to drag freely.
/// </summary>
public sealed partial class CanvasView
{
    GridSettings _grid = GridSettings.Default;
    public GridSettings Grid
    {
        get => _grid;
        set { _grid = value; InvalidateVisual(); }
    }

    /// <summary>Snap to other shapes' edges and centres (the lines are worked out again when it changes).</summary>
    public bool SnapToObjects { get => _snapToObjects; set { _snapToObjects = value; _preparedState = null; } }
    bool _snapToObjects = true;
    public bool SnapToGrid { get; set; }
    /// <summary>How close (screen pixels) something must be to snap.</summary>
    public double SnapDistancePx { get; set; } = 8;

    /// <summary>A line something can snap to: its position, and the span it covers on the other axis (for drawing the guide).</summary>
    readonly record struct SnapLine(double At, double Lo, double Hi);
    readonly record struct Guide(bool Vertical, double At, double Lo, double Hi);

    readonly List<SnapLine> _snapX = [], _snapY = [];
    readonly List<Guide> _guides = [];
    static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.Parse("#FF4FB0")), 1);

    bool SnapSuspended => _lastMods.HasFlag(Avalonia.Input.KeyModifiers.Control);

    /// <summary>Collect what can be snapped to, leaving out the shapes being moved.</summary>
    void PrepareSnap(IReadOnlySet<Guid> exclude)
    {
        _preparedState = null; // the lists below are rebuilt: what was worked out for picking points is gone
        _snapX.Clear();
        _snapY.Clear();
        _guides.Clear();
        if (_doc is null || !SnapToObjects) return;
        var visible = _doc.State.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet();
        foreach (var s in _doc.State.Shapes)
        {
            if (exclude.Contains(s.Id) || !visible.Contains(s.LayerId)) continue;
            var b = s.Bounds;
            if (b.IsEmpty) continue;
            foreach (var x in new[] { b.MinX, b.Center.X, b.MaxX }) _snapX.Add(new(x, b.MinY, b.MaxY));
            foreach (var y in new[] { b.MinY, b.Center.Y, b.MaxY }) _snapY.Add(new(y, b.MinX, b.MaxX));
        }
        double w = _machine.FieldW, h = _machine.FieldH;
        foreach (var g in _doc.State.Guides)
        {
            if (g.Vertical) _snapX.Add(new(g.At, 0, h));
            else _snapY.Add(new(g.At, 0, w));
        }
        foreach (var x in new[] { 0, w / 2, w }) _snapX.Add(new(x, 0, h));
        foreach (var y in new[] { 0, h / 2, h }) _snapY.Add(new(y, 0, w));
    }

    double SnapTol => SnapDistancePx / _scale;

    /// <summary>Best object line for any of the candidate positions: (correction, line), or null.</summary>
    (double Delta, SnapLine Line)? Nearest(List<SnapLine> lines, ReadOnlySpan<double> candidates)
    {
        (double, SnapLine)? best = null;
        double bestAbs = SnapTol;
        foreach (var c in candidates)
            foreach (var l in lines)
            {
                double d = l.At - c;
                if (Math.Abs(d) <= bestAbs)
                {
                    bestAbs = Math.Abs(d);
                    best = (d, l);
                }
            }
        return best;
    }

    /// <summary>Correction that puts the nearest candidate on a grid line.</summary>
    double GridDelta(ReadOnlySpan<double> candidates)
    {
        double step = _grid.MinorMm > 0 ? _grid.MinorMm : _grid.MajorMm;
        if (!(step > 0)) return 0;
        double best = double.PositiveInfinity;
        foreach (var c in candidates)
        {
            double d = Math.Round(c / step) * step - c;
            if (Math.Abs(d) < Math.Abs(best)) best = d;
        }
        return double.IsFinite(best) ? best : 0;
    }

    /// <summary>How far to shift a moving box so its edges or centre line up (and the guides to draw).</summary>
    Vec2 SnapBox(Rect2 box, bool allowX = true, bool allowY = true)
    {
        _guides.Clear();
        if (SnapSuspended || box.IsEmpty) return default;
        double dx = 0, dy = 0;
        if (allowX)
        {
            ReadOnlySpan<double> xs = [box.MinX, box.Center.X, box.MaxX];
            if (Nearest(_snapX, xs) is { } hit)
            {
                dx = hit.Delta;
                _guides.Add(new(true, hit.Line.At, Math.Min(hit.Line.Lo, box.MinY), Math.Max(hit.Line.Hi, box.MaxY)));
            }
            else if (SnapToGrid) dx = GridDelta(xs);
        }
        if (allowY)
        {
            ReadOnlySpan<double> ys = [box.MinY, box.Center.Y, box.MaxY];
            if (Nearest(_snapY, ys) is { } hit)
            {
                dy = hit.Delta;
                _guides.Add(new(false, hit.Line.At, Math.Min(hit.Line.Lo, box.MinX + dx), Math.Max(hit.Line.Hi, box.MaxX + dx)));
            }
            else if (SnapToGrid) dy = GridDelta(ys);
        }
        return new(dx, dy);
    }

    /// <summary>A dragged point (corner, handle, pen or path point), snapped on the axes allowed.</summary>
    Vec2 SnapPoint(Vec2 p, bool allowX = true, bool allowY = true)
    {
        var d = SnapBox(new Rect2(p.X, p.Y, p.X, p.Y), allowX, allowY);
        return p + d;
    }

    void DrawGuides(DrawingContext ctx)
    {
        foreach (var g in _guides)
        {
            if (g.Vertical)
            {
                double x = Math.Round(ToScreen(new Vec2(g.At, 0)).X) + 0.5;
                ctx.DrawLine(GuidePen, new Point(x, ToScreen(new Vec2(0, g.Lo)).Y - 12), new Point(x, ToScreen(new Vec2(0, g.Hi)).Y + 12));
            }
            else
            {
                double y = Math.Round(ToScreen(new Vec2(0, g.At)).Y) + 0.5;
                ctx.DrawLine(GuidePen, new Point(ToScreen(new Vec2(g.Lo, 0)).X - 12, y), new Point(ToScreen(new Vec2(g.Hi, 0)).X + 12, y));
            }
        }
    }

    // ---- grid drawing -------------------------------------------------------------------

    void DrawGrid(DrawingContext ctx, Rect field)
    {
        var clip = field.Intersect(new Rect(Bounds.Size));
        if (clip.Width <= 0 || clip.Height <= 0) return;
        using var _ = ctx.PushClip(clip);
        // Over a camera picture the grid is drawn fainter so the photo stays visible.
        double fade = _camera is not null ? 0.6 : 1;
        IPen Pen(Color c) => new Pen(new SolidColorBrush(Color.FromArgb((byte)(c.A * fade), c.R, c.G, c.B)), 1);
        void Lines(double step, IPen pen)
        {
            // Too dense to see (or to draw quickly): leave it out at this zoom.
            if (!(step > 0) || step * _scale < 4) return;
            var tl = ToWorld(clip.TopLeft);
            var br = ToWorld(clip.BottomRight);
            double x0 = Math.Max(0, Math.Floor(tl.X / step) * step), x1 = Math.Min(_machine.FieldW, br.X);
            double y0 = Math.Max(0, Math.Floor(tl.Y / step) * step), y1 = Math.Min(_machine.FieldH, br.Y);
            for (long i = (long)Math.Round(x0 / step); i * step <= x1 + 1e-9; i++)
            {
                double sx = Math.Round(ToScreen(new Vec2(i * step, 0)).X) + 0.5;
                ctx.DrawLine(pen, new(sx, clip.Top), new(sx, clip.Bottom));
            }
            for (long i = (long)Math.Round(y0 / step); i * step <= y1 + 1e-9; i++)
            {
                double sy = Math.Round(ToScreen(new Vec2(0, i * step)).Y) + 0.5;
                ctx.DrawLine(pen, new(clip.Left, sy), new(clip.Right, sy));
            }
        }
        Lines(_grid.MinorMm, Pen(_grid.MinorColor));
        Lines(_grid.MajorMm, Pen(_grid.MajorColor));
    }
}
