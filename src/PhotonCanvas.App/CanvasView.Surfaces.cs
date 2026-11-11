using System.Globalization;
using Avalonia;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

// Surfaces (Machine → Surfaces): each object's rectangle on the canvas, under the design.
public sealed partial class CanvasView
{
    static readonly Color SurfaceInk = Color.Parse("#B98CFF");
    static readonly IBrush SurfaceFill = new SolidColorBrush(Color.FromArgb(28, 185, 140, 255));
    static readonly IBrush SurfaceSteep = new SolidColorBrush(Color.FromArgb(22, 185, 140, 255));
    static readonly IPen SurfacePen = new Pen(new SolidColorBrush(SurfaceInk), 1.2, new DashStyle([6, 3], 0));
    static readonly IPen SurfaceTopPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 185, 140, 255)), 1, new DashStyle([2, 3], 0));
    static readonly IBrush SurfaceText = new SolidColorBrush(SurfaceInk);

    /// <summary>
    /// The surfaces' rectangles: anything drawn inside one is marked on that object. A round object's has its top
    /// line dotted down the middle, and the part more than 45° round from the top (where marks start to fade) shaded.
    /// Drawn in screen space so lines and labels stay the same size at any zoom; never throws.
    /// </summary>
    void DrawSurfaces(DrawingContext ctx, Document doc)
    {
        var items = doc.State.Surfaces.Items;
        if (items.IsEmpty) return;
        foreach (var m in items)
        {
            WrapZone z;
            try { z = m.Zone(); }
            catch (ArgumentException) { continue; }
            var corners = z.Outline();
            if (!corners.All(c => c.IsFinite)) continue;
            var outline = SurfacePoly(corners);
            ctx.DrawGeometry(SurfaceFill, null, outline);
            if (m.Round && m.Wrap && m.ReachDeg > 45)
            {
                // Past 45° round: two bands along the long edges.
                double half = z.AcrossMm / 2, at45 = m.RadiusMm * Math.PI / 4;
                foreach (int side in new[] { -1, 1 })
                    ctx.DrawGeometry(SurfaceSteep, null, SurfacePoly([z.World(-z.LengthMm / 2, side * at45), z.World(z.LengthMm / 2, side * at45), z.World(z.LengthMm / 2, side * half), z.World(-z.LengthMm / 2, side * half)]));
            }
            if (m.Round) ctx.DrawLine(SurfaceTopPen, ToScreen(z.World(-z.LengthMm / 2, 0)), ToScreen(z.World(z.LengthMm / 2, 0)));
            ctx.DrawGeometry(null, SurfacePen, outline);
            var label = new FormattedText(m.Round ? $"{m.Name} · Ø {m.DiameterMm:0.##}" : $"{m.Name} · {m.TopMm:0.##} mm up",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, SurfaceText);
            ctx.DrawText(label, SurfacesPlanView.LabelAt(ToScreen(z.World(-z.LengthMm / 2, 0)), ToScreen(z.World(z.LengthMm / 2, 0)), label));
        }
    }

    StreamGeometry SurfacePoly(IReadOnlyList<Vec2> pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(ToScreen(pts[0]), true);
        for (int i = 1; i < pts.Count; i++) c.LineTo(ToScreen(pts[i]));
        c.EndFigure(true);
        return g;
    }
}
