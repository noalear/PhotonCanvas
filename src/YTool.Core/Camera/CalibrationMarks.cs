namespace YTool.Core.Camera;

/// <summary>
/// The calibration burn: a grid of small solid dots (concentric vector
/// circles, so they land exactly where vector cuts land) plus orientation
/// markers: one extra dot inward of the top-left mark, two inward of the
/// top-right mark.
/// </summary>
public static class CalibrationMarks
{
    public const int GridSize = 5;
    public const double Margin = 10;
    public const double DotRadius = 2;

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
        for (double r = radius; r > 0.15; r -= 0.25)
            yield return Polyline.Loop(Geom.Arc(c.X, c.Y, r, r, 0, 360, 0.05));
    }

    public static Job Build(MachineProfile m, LaserSettings laser)
    {
        var blocks = new List<Block>();
        foreach (var p in Grid(m))
            blocks.AddRange(Dot(p, DotRadius).Select(pl => (Block)new VectorBlock(pl, laser)));
        foreach (var p in OrientationDots(m))
            blocks.AddRange(Dot(p, 1).Select(pl => (Block)new VectorBlock(pl, laser)));
        return new Job(m, blocks) { PassMode = new PassMode.PerBlock(), Created = DateTimeOffset.Now.ToString("yyyy_MM_dd_HH_mm_ss") };
    }
}
