using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>An etch-rate test: one setting, a row of circles each burned a different number of passes.</summary>
public sealed record EtchTestOptions
{
    public LaserSettings Laser { get; init; } = LaserSettings.Blue(60, 200);
    public double LinesPerCm { get; init; } = 100;
    /// <summary>Turn the fill lines this much each pass (keeps a deep floor even; 0 = same every pass).</summary>
    public double TurnPerPassDeg { get; init; } = 30;
    /// <summary>Passes on the first circle, and how many more on each next one.</summary>
    public int FirstPasses { get; init; } = 1;
    public int StepPasses { get; init; } = 1;
    public int Circles { get; init; } = 20;
    public double DiameterMm { get; init; } = 5;
    public double GapMm { get; init; } = 2.5;
    public int PerRow { get; init; } = 5;
    public double LabelMm { get; init; } = 1.8;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the test on the bed.</summary>
    public Vec2 Origin { get; init; } = new(90, 90);

    /// <summary>The number of passes on each circle, in order.</summary>
    public IReadOnlyList<int> Ladder => [.. Enumerable.Range(0, Math.Max(1, Circles)).Select(i => Math.Max(1, FirstPasses) + i * Math.Max(1, StepPasses))];
}

/// <summary>A depth-per-pass result: the best estimate and the range it must lie in.</summary>
public sealed record EtchRate(double MmPerPass, double LowMm, double HighMm, string Basis);

/// <summary>Depths read at several pass counts: how fast it cuts at first, on average, and how much it slows going down.</summary>
public sealed record EtchCurve(double FirstPassMm, double AverageMm, double SlowdownPercent, double RmsMm, Func<int, double> DepthAfter);

/// <summary>
/// The etch-rate test. Every circle has its own layer with its own pass count, so on a job run
/// round by round all circles are burned a pass at a time and cool in between. The first circle
/// that goes right through gives the material's thickness ÷ passes; depths measured on the
/// shallower ones show whether it slows down with depth (it usually does: the floor drops out of
/// focus and smoke and char build up).
/// </summary>
public static class EtchTest
{
    public const string LayerPrefix = "Etch test · ";

    public static string LayerName(int passes) => $"{LayerPrefix}{passes} pass{(passes == 1 ? "" : "es")}";

    /// <summary>Passes of an etch-test layer, from its name; null if it isn't one.</summary>
    public static int? PassesOf(Layer l) =>
        l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal) && int.TryParse(l.Name[LayerPrefix.Length..].Split(' ')[0], out var n) ? n : null;

    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, EtchTestOptions o)
    {
        var ladder = o.Ladder;
        double pitch = o.DiameterMm + o.GapMm, r = o.DiameterMm / 2;
        var shapes = new List<Shape>();
        int index = s.Layers.Count;
        var labels = Layer.Default(index + ladder.Count, LayerMode.Score) with
        {
            Name = "Etch test labels",
            Laser = o.Laser with { Power = Math.Clamp(o.Laser.Power * 0.4, 3, 30), Speed = Math.Max(o.Laser.Speed, 300), Passes = 1 },
        };
        for (int i = 0; i < ladder.Count; i++)
        {
            int row = i / Math.Max(1, o.PerRow), col = i % Math.Max(1, o.PerRow);
            double cx = o.Origin.X + col * pitch + r, cy = o.Origin.Y + row * (pitch + o.LabelMm + 1) + r;
            var layer = Layer.Default(index + i, LayerMode.Fill) with
            {
                Name = LayerName(ladder[i]),
                Laser = o.Laser with { Passes = ladder[i] },
                LinesPerCm = o.LinesPerCm,
                AngleStepDeg = o.TurnPerPassDeg,
            };
            s = Ops.AddLayer(s, layer);
            shapes.Add(new PathShape { Name = $"{ladder[i]} passes", LayerId = layer.Id, Geometry = PathGeometry.Ellipse(cx, cy, r, r) });
        }
        s = Ops.AddLayer(s, labels);
        for (int i = 0; i < ladder.Count; i++)
        {
            int row = i / Math.Max(1, o.PerRow), col = i % Math.Max(1, o.PerRow);
            double cx = o.Origin.X + col * pitch + r, cy = o.Origin.Y + row * (pitch + o.LabelMm + 1) + o.DiameterMm + 0.8 + o.LabelMm / 2;
            var t = TextShape.Create($"{ladder[i]}", new TextStyle(o.Font, o.LabelMm), labels.Id, Affine.Identity);
            var b = t.Bounds;
            shapes.Add(b.IsEmpty ? t : t.With(transform: Affine.Translate(cx - b.Center.X, cy - b.Center.Y)));
        }
        return (Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToList());
    }

    /// <summary>
    /// From the burn-through: <paramref name="throughAt"/> passes went right through, the circle
    /// before it (<paramref name="notThroughAt"/> passes) didn't. The true rate is between the two.
    /// </summary>
    public static EtchRate FromBreakthrough(double thicknessMm, int throughAt, int? notThroughAt)
    {
        double high = thicknessMm / Math.Max(1, notThroughAt ?? 0) is var h && notThroughAt is > 0 ? h : double.PositiveInfinity;
        double low = thicknessMm / Math.Max(1, throughAt);
        // Through after n passes but not n−k: somewhere between; the middle of the pass counts is the fair guess.
        double best = notThroughAt is > 0 ? thicknessMm / ((throughAt + notThroughAt.Value) / 2.0) : low;
        return new EtchRate(best, low, double.IsInfinity(high) ? best : high,
            notThroughAt is > 0 ? $"through at {throughAt} passes, not at {notThroughAt}" : $"through at {throughAt} passes (the first circle)");
    }

    /// <summary>Nothing went through: the rate is below thickness ÷ most passes.</summary>
    public static EtchRate NoneThrough(double thicknessMm, int mostPasses) =>
        new(double.NaN, 0, thicknessMm / Math.Max(1, mostPasses), $"not through after {mostPasses} passes");

    /// <summary>
    /// Depths measured after different numbers of passes, fitted as depth = a·n − b·n² (through
    /// zero). With fewer than three readings, a straight line.
    /// </summary>
    public static EtchCurve FromDepths(IReadOnlyList<(int Passes, double DepthMm)> readings)
    {
        var pts = readings.Where(r => r.Passes > 0 && r.DepthMm >= 0).ToList();
        if (pts.Count == 0) throw new ArgumentException("No depth readings.", nameof(readings));
        double a, b = 0;
        if (pts.Count >= 3)
        {
            // Least squares for d = a n + c n² (c should come out negative: slowing down).
            double s11 = pts.Sum(p => (double)p.Passes * p.Passes), s12 = pts.Sum(p => Math.Pow(p.Passes, 3)), s22 = pts.Sum(p => Math.Pow(p.Passes, 4));
            double t1 = pts.Sum(p => p.Passes * p.DepthMm), t2 = pts.Sum(p => (double)p.Passes * p.Passes * p.DepthMm);
            double det = s11 * s22 - s12 * s12;
            if (Math.Abs(det) > 1e-12)
            {
                a = (t1 * s22 - t2 * s12) / det;
                double c = (s11 * t2 - s12 * t1) / det;
                b = -c;
            }
            else a = t1 / s11;
        }
        else a = pts.Sum(p => p.Passes * p.DepthMm) / pts.Sum(p => (double)p.Passes * p.Passes);
        double A = a, B = b;
        double Depth(int n) => A * n - B * n * n;
        int last = pts.Max(p => p.Passes);
        double rateFirst = A - B, rateLast = A - B * (2 * last - 1);
        double rms = Math.Sqrt(pts.Average(p => Math.Pow(p.DepthMm - Depth(p.Passes), 2)));
        return new EtchCurve(rateFirst, Depth(last) / last, rateFirst > 0 ? (1 - rateLast / rateFirst) * 100 : 0, rms, Depth);
    }
}
