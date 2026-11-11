using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// The bed from above, as the Surfaces window shows it: each object's rectangle on the canvas (dashed), where its
/// marks really land seen from above (filled), and the design twice: faint as drawn, and bright where it will be
/// marked (squeezed towards a round object's top line, as it wraps round it). A cross-section of the chosen
/// object is drawn in the corner. Never throws while drawing.
/// </summary>
public sealed class SurfacesPlanView : Control
{
    IReadOnlyList<MarkSurface> _items = [];
    IReadOnlyList<IReadOnlyList<Vec2>> _design = [];
    int _chosen = -1;

    static readonly Rect2 Bed = new(0, 0, MachineProfile.F2Ultra.FieldW, MachineProfile.F2Ultra.FieldH);
    static readonly Color Ink = Color.Parse("#B98CFF");
    static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(70, 200, 200, 210));
    static readonly IBrush Mark = new SolidColorBrush(Color.Parse("#FF9F43"));

    public void Show(IReadOnlyList<MarkSurface> items, int chosen, IReadOnlyList<IReadOnlyList<Vec2>> design)
    {
        _items = items;
        _chosen = chosen;
        _design = design;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(UiKit.Brush("Bg0"), b);
        // Zoomed in on the surfaces (marks a few mm round a pen are too small to see on the whole bed).
        var view = Bed;
        if (_items.Count > 0)
        {
            var r = _items.Select(m => m.Zone().Bounds).Aggregate((x, y) => x.Union(y));
            double margin = Math.Max(8, 0.15 * Math.Max(r.Width, r.Height));
            r = r.Inflate(margin);
            if (r.Width < 60) r = r.Inflate((60 - r.Width) / 2);
            if (r.IsEmpty || !double.IsFinite(r.Width)) r = Bed;
            view = r;
        }
        double pad = 14, scale = Math.Min((b.Width - 2 * pad) / view.Width, (b.Height - 2 * pad) / view.Height);
        if (!(scale > 0) || !double.IsFinite(scale)) return;
        var origin = new Point((b.Width - view.Width * scale) / 2 - view.MinX * scale, (b.Height - view.Height * scale) / 2 - view.MinY * scale);
        Point P(Vec2 v) => new(origin.X + v.X * scale, origin.Y + v.Y * scale);
        using var clip = ctx.PushClip(b);
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(P(Bed.TopLeft), P(Bed.BottomRight)));
        var grid = new Pen(UiKit.Brush("Line"), 0.5);
        double step = view.Width > 120 ? 20 : view.Width > 50 ? 10 : 5;
        for (double g = step; g < Bed.Width; g += step)
        {
            ctx.DrawLine(grid, P(new Vec2(g, 0)), P(new Vec2(g, Bed.Height)));
            ctx.DrawLine(grid, P(new Vec2(0, g)), P(new Vec2(Bed.Width, g)));
        }
        var scaleText = new FormattedText($"Grid every {step:0} mm, seen from above", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, UiKit.Brush("TextMuted"));
        ctx.DrawText(scaleText, new Point(8, 6));
        StreamGeometry Shape(IReadOnlyList<Vec2> pts, bool closed)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(P(pts[0]), closed);
            for (int i = 1; i < pts.Count; i++) c.LineTo(P(pts[i]));
            c.EndFigure(closed);
            return g;
        }
        var zones = new List<WrapZone>();
        for (int i = 0; i < _items.Count; i++)
        {
            var z = _items[i].Zone();
            zones.Add(z);
            bool chosen = i == _chosen;
            var ink = new SolidColorBrush(Color.FromArgb(chosen ? (byte)255 : (byte)170, Ink.R, Ink.G, Ink.B));
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(chosen ? (byte)70 : (byte)40, Ink.R, Ink.G, Ink.B)), null, Shape(z.Footprint(), true));
            ctx.DrawGeometry(null, new Pen(ink, chosen ? 1.6 : 1, new DashStyle([5, 3], 0)), Shape(z.Outline(), true));
            var label = new FormattedText(_items[i].Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, ink);
            ctx.DrawText(label, LabelAt(P(z.World(-z.LengthMm / 2, 0)), P(z.World(z.LengthMm / 2, 0)), label));
        }
        // The design as drawn (faint), then where it lands (bright).
        var faint = new Pen(Faint, 1);
        var bright = new Pen(Mark, 1.2);
        var wrap = new SurfaceWrap(zones);
        foreach (var path in _design.Where(p => p.Count >= 2))
        {
            ctx.DrawGeometry(null, faint, Shape(path, false));
            if (zones.Count > 0) ctx.DrawGeometry(null, bright, Shape(wrap.MapPath(path), false));
        }
        if (_chosen >= 0 && _chosen < _items.Count && _items[_chosen].Round) DrawSection(ctx, _items[_chosen], b);
    }

    /// <summary>
    /// Where a surface's name goes: just before the start of its top line (to the left of one lying left to right,
    /// above one lying front to back), so the names of objects side by side don't land on each other.
    /// </summary>
    internal static Point LabelAt(Point start, Point end, FormattedText label)
    {
        bool acrossX = Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y);
        var first = acrossX ? (start.X <= end.X ? start : end) : (start.Y <= end.Y ? start : end);
        return acrossX ? new Point(first.X - label.Width - 6, first.Y - label.Height / 2) : new Point(first.X - label.Width / 2, first.Y - label.Height - 4);
    }

    /// <summary>The chosen round object end-on (bottom right): its circle on the bed, the arc marks may cover, and the beam coming down.</summary>
    static void DrawSection(DrawingContext ctx, MarkSurface m, Rect b)
    {
        double size = 120;
        var box = new Rect(b.Right - size - 10, b.Bottom - size - 28, size, size + 18);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(225, 20, 22, 28)), box, 6);
        double r = 34;
        var c = new Point(box.Center.X, box.Y + 18 + size / 2 + 6);
        var line = new Pen(UiKit.Brush("TextMuted"), 1);
        ctx.DrawLine(line, new Point(box.X + 8, c.Y + r), new Point(box.Right - 8, c.Y + r));
        ctx.DrawEllipse(null, line, c, r, r);
        double reach = Math.Clamp(m.ReachDeg, 1, MarkSurface.MaxReachDeg) * Math.PI / 180;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            const int n = 32;
            for (int i = 0; i <= n; i++)
            {
                double a = -reach + 2 * reach * i / n;
                var p = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
                if (i == 0) g.BeginFigure(p, false);
                else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Ink), 3), arc);
        var beam = new Pen(new SolidColorBrush(Color.FromArgb(160, 61, 214, 245)), 1, new DashStyle([3, 3], 0));
        foreach (double a in new[] { -reach, 0, reach })
        {
            double x = c.X + r * Math.Sin(a), y = c.Y - r * Math.Cos(a);
            ctx.DrawLine(beam, new Point(x, box.Y + 18), new Point(x, y));
        }
        var t = new FormattedText($"End on · Ø {m.DiameterMm:0.##} mm · ±{m.ReachDeg:0}°", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, UiKit.Brush("TextMuted"));
        ctx.DrawText(t, new Point(box.X + 6, box.Y + 3));
    }
}
