using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// Lining the MOPA (fiber) laser up with the blue one: each burns a grid of dots, the MOPA's a set
/// distance below the blue's; the close-up camera (whose position on the bed is known) finds every
/// dot, and the two grids' errors are compared. The difference, MOPA less blue, is how far the MOPA
/// lands off; moving MOPA work back by it lines the two up.
/// </summary>
public static class LaserAlign
{
    public const string BlueLayer = "Align · blue", MopaLayer = "Align · MOPA";
    /// <summary>The MOPA's grid sits this far below the blue's (mm).</summary>
    public const double Apart = 18;

    /// <summary>Where each grid's dots go: 3 × 3, <paramref name="spacing"/> apart, centred at <paramref name="centre"/>.</summary>
    public static IReadOnlyList<Vec2> Grid(Vec2 centre, double spacing) =>
        [.. from r in Enumerable.Range(-1, 3) from c in Enumerable.Range(-1, 3) select new Vec2(centre.X + c * spacing, centre.Y + r * spacing)];

    /// <summary>Add both grids (the MOPA's <see cref="Apart"/> below the blue's), each on its own fill layer.</summary>
    public static DocState Add(DocState s, Vec2 blueCentre, double spacing, double dotMm, LaserSettings blue, LaserSettings mopa)
    {
        s = Remove(s);
        var bl = Layer.Default(s.Layers.Count, LayerMode.Fill) with { Name = BlueLayer, Laser = blue };
        s = Ops.AddLayer(s, bl);
        var ml = Layer.Default(s.Layers.Count, LayerMode.Fill) with { Name = MopaLayer, Laser = mopa };
        s = Ops.AddLayer(s, ml);
        var dots = Grid(blueCentre, spacing).Select((p, i) => (Shape)new PathShape { Name = $"Blue dot {i + 1}", LayerId = bl.Id, Geometry = PathGeometry.Ellipse(p.X, p.Y, dotMm / 2, dotMm / 2) })
            .Concat(Grid(blueCentre + new Vec2(0, Apart), spacing).Select((p, i) => (Shape)new PathShape { Name = $"MOPA dot {i + 1}", LayerId = ml.Id, Geometry = PathGeometry.Ellipse(p.X, p.Y, dotMm / 2, dotMm / 2) }));
        return Ops.AddShapes(s, [.. dots]);
    }

    public static DocState Remove(DocState s)
    {
        var ids = s.Layers.Where(l => l.Name is BlueLayer or MopaLayer).Select(l => l.Id).ToHashSet();
        return ids.Count == 0 ? s : Ops.DeleteLayers(s, ids, null);
    }

    /// <summary>The two grids' dot centres as they are in the design (they may have been moved), blue then MOPA; null when there aren't both.</summary>
    public static (IReadOnlyList<Vec2> Blue, IReadOnlyList<Vec2> Mopa)? Marks(DocState s)
    {
        IReadOnlyList<Vec2>? Of(string layer) => s.Layers.FirstOrDefault(l => l.Name == layer) is { } l ? [.. s.ShapesFor(l).Select(x => x.Bounds.Center)] : null;
        return Of(BlueLayer) is { Count: > 0 } b && Of(MopaLayer) is { Count: > 0 } m ? (b, m) : null;
    }

    /// <summary>What the photo showed: how far each laser's dots landed off where they were drawn (mm, mean), how many were found, and MOPA less blue.</summary>
    public sealed record Result(Vec2 BlueOff, Vec2 MopaOff, int BlueFound, int MopaFound, double SpreadMm)
    {
        /// <summary>How far the MOPA lands from where the blue does: move MOPA work back by this.</summary>
        public Vec2 MopaFromBlue => MopaOff - BlueOff;
    }

    /// <summary>
    /// Find the dots in a close-up photo: each where the camera's saved position says it should be,
    /// within <paramref name="searchMm"/>. Null when fewer than five of either grid are found.
    /// </summary>
    public static Result? Measure(RasterImage photo, Homography mmToCamera, IReadOnlyList<Vec2> blue, IReadOnlyList<Vec2> mopa, double searchMm = 2)
    {
        var spots = CloseUpMarks.FindSpots(photo, 0.86);
        var probe = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) };
        if (Homography.Fit([.. probe.Select(mm => (mmToCamera.Apply(mm), mm))]) is not { } toBed) return null;
        var onBed = spots.Select(sp => toBed.Apply(sp.At)).ToList();
        (Vec2 Off, int Found, List<double> Errors) Grid(IReadOnlyList<Vec2> marks)
        {
            var offs = new List<Vec2>();
            foreach (var m in marks)
            {
                var near = onBed.Where(p => p.Dist(m) <= searchMm).OrderBy(p => p.Dist(m)).FirstOrDefault(new Vec2(double.NaN, double.NaN));
                if (near.IsFinite) offs.Add(near - m);
            }
            var mean = offs.Count == 0 ? new Vec2(0, 0) : new Vec2(offs.Average(o => o.X), offs.Average(o => o.Y));
            return (mean, offs.Count, [.. offs.Select(o => o.Dist(mean))]);
        }
        var b = Grid(blue);
        var m = Grid(mopa);
        if (b.Found < 5 || m.Found < 5) return null;
        return new Result(b.Off, m.Off, b.Found, m.Found, b.Errors.Concat(m.Errors).Max());
    }
}
