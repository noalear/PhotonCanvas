using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// The galvo's slanted beam. The mirrors steer the beam from above the middle of the field (the
/// pivot); away from the middle it comes down at a slant, so a cut's wall isn't upright. A straight
/// cut's wall is the plane through the pivot and the cut line: its slope is the same all along the
/// line, atan(distance of the line from the middle ÷ pivot height), and the part on the middle's
/// side comes out wider at the bottom (an inward-sloping edge seen from above). So a chosen bevel is
/// a chosen distance from the middle. The two mirrors sit at different heights, so slopes across
/// (edges running down the field) and down (edges running across) each have their own pivot.
/// </summary>
public static class Bevel
{
    /// <summary>The slope (degrees from upright) of a straight cut <paramref name="distanceMm"/> from the field's middle.</summary>
    public static double AngleAt(double distanceMm, double pivotMm) => Math.Atan(Math.Abs(distanceMm) / pivotMm) * 180 / Math.PI;

    /// <summary>How far from the field's middle a straight cut must lie for a slope of <paramref name="angleDeg"/>.</summary>
    public static double DistanceFor(double angleDeg, double pivotMm) => pivotMm * Math.Tan(angleDeg * Math.PI / 180);

    /// <summary>The steepest slope an edge can get: one at the side of the field, 5 mm in.</summary>
    public static double MaxAngle(MachineProfile m, double pivotMm) => AngleAt(Math.Min(m.FieldW, m.FieldH) / 2 - 5, pivotMm);

    /// <summary>
    /// The pivot height from a long and a short bar cut along the same direction, each measured with
    /// calipers: the bar itself (outside jaws: its bottom face, the widest) and the hole it left
    /// (inside jaws from the top: its top opening, the narrowest).
    /// </summary>
    /// <remarks>
    /// Seen from the pivot, the bottom face is the top face scaled by (H + t) ÷ H, wherever the bar
    /// lies: a bar L long is L·t/H longer at the bottom. bar − hole = L·t/H minus the kerf at the
    /// top and at the bottom, the same for both bars, so the difference between the bars leaves
    /// (L1 − L2)·t/H: H = t·(L1 − L2) ÷ ((bar1 − hole1) − (bar2 − hole2)).
    /// </remarks>
    public static double? PivotFromBars(double thicknessMm, double longMm, double longBar, double longHole, double shortMm, double shortBar, double shortHole)
    {
        double diff = (longBar - longHole) - (shortBar - shortHole);
        if (!(diff > 0.02) || !(thicknessMm > 0) || !(longMm > shortMm)) return null; // too small to tell from the calipers' play
        return thicknessMm * (longMm - shortMm) / diff;
    }

    public const string TestLayer = "Bevel test";

    public static bool InDesign(DocState s) => s.Layers.Any(l => l.Name == TestLayer);

    /// <summary>The bars' lengths for a field (mm): as long as fits beside each other, and a short one.</summary>
    public static (double Long, double Short) BarLengths(MachineProfile m)
    {
        double f = Math.Min(m.FieldW, m.FieldH);
        double longMm = Math.Round(f - 70);
        return (longMm, Math.Round(Math.Min(40, longMm / 3)));
    }

    /// <summary>
    /// The bar test, cut with <paramref name="cut"/>'s settings: a long and a short bar 12 mm wide
    /// running across (their ends slope across: the pivot for edges running down) and a pair running
    /// down, laid in an L so none cross. Where a bar lies doesn't change the reading, only its length.
    /// </summary>
    public static DocState AddTest(DocState s, MachineProfile m, Layer cut)
    {
        var layer = cut with { Id = Guid.NewGuid(), Name = TestLayer, Mode = LayerMode.Cut, ParentId = null };
        s = Ops.AddLayer(s, layer);
        double f = Math.Min(m.FieldW, m.FieldH);
        var (longMm, shortMm) = BarLengths(m);
        const double W = 12, Edge = 10;
        Shape Bar(string name, double x, double y, double w, double h) => new PathShape { Name = name, LayerId = layer.Id, Geometry = PathGeometry.Rect(x, y, w, h) };
        double outer = f - Edge - W, inner = outer - 8 - W;
        return Ops.AddShapes(s,
        [
            Bar($"Long bar across ({longMm:0} mm)", Edge, outer, longMm, W),
            Bar($"Short bar across ({shortMm:0} mm)", f / 2 - shortMm / 2, inner, shortMm, W),
            Bar($"Long bar down ({longMm:0} mm)", outer, Edge, W, longMm),
            Bar($"Short bar down ({shortMm:0} mm)", inner, f / 2 - shortMm / 2, W, shortMm),
        ]);
    }
}
