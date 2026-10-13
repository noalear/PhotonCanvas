namespace YTool.Core.Camera;

public enum MarkStyle
{
    /// <summary>Solid dot (concentric circles). Easy to spot, centre found by its darkness.</summary>
    Dot,
    /// <summary>Plus-shaped crosshair. Thin, so the centre is easy to judge by eye.</summary>
    Cross,
}

/// <summary>
/// The shape of the calibration marks. <paramref name="SizeMm"/> is the dot
/// diameter, or the full width of a crosshair.
/// </summary>
public sealed record MarkSpec(MarkStyle Style, double SizeMm)
{
    public static readonly MarkSpec Default = new(MarkStyle.Dot, 2);

    /// <summary>How far around a guess to search for the mark (mm).</summary>
    public double SearchRadiusMm => Math.Max(2.5, SizeMm / 2 + 2);
}

/// <summary>
/// The calibration burn: a grid of marks (vector paths, so they land exactly
/// where vector cuts land) plus orientation dots: one inward of the top-left
/// mark, two inward of the top-right mark.
/// </summary>
public static class CalibrationMarks
{
    public const int GridSize = 5;
    public const double Margin = 10;

    /// <summary>Crosshair arms are drawn as this many side-by-side lines, so they are thick enough for the camera.</summary>
    const int CrossStrokes = 3;
    const double CrossStrokeGap = 0.15;

    /// <summary>Mark centres in machine mm, row by row from the top-left.</summary>
    public static IReadOnlyList<Vec2> Grid(MachineProfile m)
    {
        var o = new List<Vec2>();
        double sx = (m.FieldW - 2 * Margin) / (GridSize - 1), sy = (m.FieldH - 2 * Margin) / (GridSize - 1);
        for (int r = 0; r < GridSize; r++)
            for (int c = 0; c < GridSize; c++) o.Add(new(Margin + c * sx, Margin + r * sy));
        return o;
    }

    /// <summary>Indices into <see cref="Grid"/> of the four corners: top-left, top-right, bottom-right, bottom-left.</summary>
    public static readonly int[] Corners = [0, GridSize - 1, GridSize * GridSize - 1, GridSize * (GridSize - 1)];

    public static IReadOnlyList<Vec2> OrientationDots(MachineProfile m) =>
    [
        new(Margin + 12, Margin + 12),                 // top-left: one dot
        new(m.FieldW - Margin - 12, Margin + 12),      // top-right: two dots
        new(m.FieldW - Margin - 18, Margin + 18),
    ];

    /// <summary>Concentric circles that read as a solid dot.</summary>
    public static IEnumerable<Polyline> Dot(Vec2 c, double radius)
    {
        for (double r = radius; r > 0.12; r -= 0.25)
            yield return Polyline.Loop(Geom.Arc(c.X, c.Y, r, r, 0, 360, 0.05));
    }

    /// <summary>A plus sign of width <paramref name="size"/>, each arm a few strokes thick.</summary>
    public static IEnumerable<Polyline> Cross(Vec2 c, double size)
    {
        double h = size / 2;
        for (int k = 0; k < CrossStrokes; k++)
        {
            double o = (k - (CrossStrokes - 1) / 2.0) * CrossStrokeGap;
            yield return Polyline.Open((c.X - h, c.Y + o), (c.X + h, c.Y + o));
            yield return Polyline.Open((c.X + o, c.Y - h), (c.X + o, c.Y + h));
        }
    }

    public static IEnumerable<Polyline> Mark(Vec2 c, MarkSpec spec) =>
        spec.Style == MarkStyle.Cross ? Cross(c, spec.SizeMm) : Dot(c, spec.SizeMm / 2);

    public static Job Build(MachineProfile m, LaserSettings laser, MarkSpec? spec = null)
    {
        spec ??= MarkSpec.Default;
        // Orientation dots stay small and distinct from the grid marks.
        double orient = Math.Clamp(spec.SizeMm / 2, 1, 2) / 2;
        var blocks = new List<Block>();
        foreach (var p in Grid(m))
            blocks.AddRange(Mark(p, spec).Select(pl => (Block)new VectorBlock(pl, laser)));
        foreach (var p in OrientationDots(m))
            blocks.AddRange(Dot(p, orient).Select(pl => (Block)new VectorBlock(pl, laser)));
        return new Job(m, blocks) { PassMode = new PassMode.PerBlock(), Created = DateTimeOffset.Now.ToString("yyyy_MM_dd_HH_mm_ss") };
    }
}
