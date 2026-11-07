namespace PhotonCanvas.Core;

/// <summary>The kinds of standard stock a part in the rotary can be.</summary>
public enum StockKind
{
    /// <summary>Whatever shape the scan measured.</summary>
    Scanned,
    Round,
    Square,
    Rectangle,
    /// <summary>A hexagon bar; its size is across the flats.</summary>
    Hexagon,
}

/// <summary>
/// A standard stock shape seen end-on, centred on its own middle: a round rod <see cref="WidthMm"/> across, a square
/// or rectangular bar <see cref="WidthMm"/> × <see cref="HeightMm"/>, or a hexagon bar <see cref="WidthMm"/> across the flats.
/// </summary>
public sealed record StockShape(StockKind Kind, double WidthMm, double HeightMm = 0)
{
    public bool IsStandard => Kind != StockKind.Scanned && WidthMm > 0;

    double H => Kind == StockKind.Rectangle && HeightMm > 0 ? HeightMm : WidthMm;

    /// <summary>"2" × 2" square bar" or "25.4 mm round rod", in the units given.</summary>
    public string Describe(bool inches)
    {
        string U(double mm) => inches ? $"{mm / 25.4:0.###}\"" : $"{mm:0.##} mm";
        return Kind switch
        {
            StockKind.Round => $"{U(WidthMm)} round rod",
            StockKind.Square => $"{U(WidthMm)} × {U(WidthMm)} square bar",
            StockKind.Rectangle => $"{U(WidthMm)} × {U(H)} bar",
            StockKind.Hexagon => $"{U(WidthMm)} hexagon bar (across the flats)",
            _ => "as scanned",
        };
    }

    /// <summary>Its turns repeat every this many degrees (360 for none; 0 for a round rod, whose turn can't be seen).</summary>
    public double PeriodDeg => Kind switch
    {
        StockKind.Square => 90,
        StockKind.Rectangle => Math.Abs(H - WidthMm) < 1e-9 ? 90 : 180,
        StockKind.Hexagon => 60,
        StockKind.Round => 0,
        _ => 360,
    };

    /// <summary>The average distance from its middle to its outline (a first guess of its size).</summary>
    public double MeanRadius => Kind switch
    {
        StockKind.Round => WidthMm / 2,
        StockKind.Hexagon => WidthMm / 2 * 1.05,
        _ => (WidthMm + H) / 4 * 1.12,
    };

    /// <summary>The outline's corners (end-on, its middle at 0,0), or null for a round rod.</summary>
    public List<Vec2>? Corners()
    {
        switch (Kind)
        {
            case StockKind.Square:
            case StockKind.Rectangle:
                double w = WidthMm / 2, h = H / 2;
                return [new(-w, -h), new(w, -h), new(w, h), new(-w, h)];
            case StockKind.Hexagon:
                double r = WidthMm / 2 / Math.Cos(Math.PI / 6);
                return [.. Enumerable.Range(0, 6).Select(i => new Vec2(r * Math.Cos(i * Math.PI / 3), r * Math.Sin(i * Math.PI / 3)))];
            default:
                return null;
        }
    }

    /// <summary>
    /// How far from the rotary's axis the surface is, along the ray at <paramref name="deg"/> (the turn that brings that
    /// ray to the top), for this shape turned <paramref name="turnDeg"/> in the chuck with its middle at
    /// <paramref name="centre"/> (end-on, +Y the top at the start, +X where the ray at 90° points). Null if the ray misses it.
    /// </summary>
    public double? RadiusAt(double deg, double turnDeg, Vec2 centre)
    {
        double b = deg * Math.PI / 180;
        var dir = new Vec2(Math.Sin(b), Math.Cos(b));
        if (Kind == StockKind.Round)
        {
            // |t·dir − c|² = R²: the far crossing.
            double R = WidthMm / 2, bq = dir.Dot(centre), cq = centre.Dot(centre) - R * R, disc = bq * bq - cq;
            if (disc < 0) return null;
            double t = bq + Math.Sqrt(disc);
            return t >= 0 ? t : null;
        }
        if (Corners() is not { } corners) return null;
        double c = Math.Cos(turnDeg * Math.PI / 180), s = Math.Sin(turnDeg * Math.PI / 180);
        var pts = corners.Select(p => new Vec2(p.X * c - p.Y * s + centre.X, p.X * s + p.Y * c + centre.Y)).ToList();
        double? best = null;
        for (int i = 0; i < pts.Count; i++)
        {
            Vec2 a = pts[i], e = pts[(i + 1) % pts.Count] - a;
            // t·dir = a + u·e
            double den = dir.X * -e.Y - dir.Y * -e.X;
            if (Math.Abs(den) < 1e-12) continue;
            double t = (a.X * -e.Y - a.Y * -e.X) / den;
            double u = (dir.X * a.Y - dir.Y * a.X) / den;
            if (u < -1e-9 || u > 1 + 1e-9 || t < 0) continue;
            if (best is null || t > best) best = t;
        }
        return best;
    }
}

/// <summary>How a standard stock shape sits in the rotary, found from a scan (<see cref="StockFitter.Fit"/>).</summary>
/// <param name="AxisHeightMm">The rotary's axis above the bed (given, or found).</param>
/// <param name="Centre">Where the stock's middle is, end-on, from the axis (mm; +Y the top at the start of the scan).</param>
/// <param name="TurnDeg">How far the shape is turned in the chuck (degrees, within one period of the shape).</param>
/// <param name="RmsMm">How far the readings sit from the fitted shape (mm): big when the stock isn't that shape.</param>
public sealed record StockFit(StockShape Shape, double AxisHeightMm, Vec2 Centre, double TurnDeg, double RmsMm, int Readings)
{
    /// <summary>How far the stock's middle is off the axis (mm).</summary>
    public double OffAxisMm => Centre.Length;

    /// <summary>The turn (the way a design runs down the page) that brings a flat face of the shape to the top, the shorter way.</summary>
    public double FlatUpDeg()
    {
        if (Shape.Kind == StockKind.Round) return 0;
        // The flat whose outward normal points closest to the top: the ray to the middle of that face.
        var corners = Shape.Corners()!;
        double c = Math.Cos(TurnDeg * Math.PI / 180), s = Math.Sin(TurnDeg * Math.PI / 180);
        double best = double.MaxValue, bestDeg = 0;
        for (int i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            var n = new Vec2(-(b - a).Y, (b - a).X);
            if (n.Dot((a + b) * 0.5) < 0) n = n * -1; // outward
            var rn = new Vec2(n.X * c - n.Y * s, n.X * s + n.Y * c);
            // A ray at deg points along (sin, cos): this face's normal points along that ray at deg = atan2(x, y).
            double deg = Math.Atan2(rn.X, rn.Y) * 180 / Math.PI;
            double turn = ((deg % 360) + 540) % 360 - 180;
            if (Math.Abs(turn) < Math.Abs(best)) { best = turn; bestDeg = turn; }
        }
        return bestDeg;
    }

    /// <summary>
    /// The ideal stock as a scan, so it is used just like a measured one: the fitted shape's top height every
    /// <paramref name="stepDeg"/>° over a whole turn, at the places along the axis <paramref name="xs"/>.
    /// </summary>
    public RotaryScan ToScan(IReadOnlyList<double> xs, double lineY, double stepDeg = 2, DateTimeOffset? when = null, bool inches = false)
    {
        int n = Math.Max(8, (int)Math.Round(360 / Math.Max(0.25, stepDeg)));
        var angles = Enumerable.Range(0, n).Select(i => Math.Round(i * 360.0 / n, 6)).ToArray();
        var row = angles.Select(a => Shape.RadiusAt(a, TurnDeg, Centre) is { } r ? AxisHeightMm + r : (double?)null).ToArray();
        return new RotaryScan
        {
            AnglesDeg = angles,
            Xs = [.. xs],
            Heights = [.. angles.Select((_, i) => xs.Select(_ => row[i]).ToArray())],
            LineY = lineY,
            Method = "standard",
            Stock = Shape.Describe(inches),
            When = when ?? DateTimeOffset.Now,
        };
    }
}

/// <summary>Fitting a standard stock shape to a scan: where it sits off the axis, how it's turned, and (if not given) the axis height.</summary>
public static class StockFitter
{
    /// <summary>
    /// The best fit of <paramref name="shape"/> to every reading of <paramref name="scan"/> (one shape all along the part),
    /// with the axis at <paramref name="axisHeightMm"/>, or found too when that's null. Null with too few readings.
    /// </summary>
    public static StockFit? Fit(RotaryScan scan, StockShape shape, double? axisHeightMm)
    {
        if (!shape.IsStandard) return null;
        var pts = new List<(double Deg, double H)>();
        for (int x = 0; x < scan.Xs.Length; x++) pts.AddRange(scan.Column(x));
        int need = axisHeightMm is null ? 4 : 3;
        if (pts.Select(p => Math.Round(p.Deg, 3)).Distinct().Count() < need) return null;

        double c0 = axisHeightMm ?? pts.Average(p => p.H) - shape.MeanRadius;
        double period = shape.PeriodDeg;
        double scale = Math.Max(1, shape.MeanRadius);
        double Cost(double[] v)
        {
            double c = axisHeightMm ?? v[3];
            var centre = new Vec2(v[0], v[1]);
            double sum = 0;
            foreach (var (deg, h) in pts)
            {
                double? r = shape.RadiusAt(deg, v[2], centre);
                double e = r is { } rr ? c + rr - h : 4 * scale; // a ray that misses the shape: far off
                sum += e * e;
            }
            return sum;
        }
        double[]? best = null;
        double bestCost = double.MaxValue;
        int starts = period <= 0 ? 1 : 12;
        for (int k = 0; k < starts; k++)
        {
            double turn = period <= 0 ? 0 : period * k / starts;
            double[] v0 = axisHeightMm is null ? [0, 0, turn, c0] : [0, 0, turn];
            double[] step = axisHeightMm is null ? [scale * 0.1, scale * 0.1, Math.Max(1, period / starts), scale * 0.1] : [scale * 0.1, scale * 0.1, Math.Max(1, period / starts)];
            var v = NelderMead(Cost, v0, step, 1500);
            double cost = Cost(v);
            if (cost < bestCost) { bestCost = cost; best = v; }
        }
        if (best is null) return null;
        double tdeg = period <= 0 ? 0 : ((best[2] % period) + period) % period;
        return new StockFit(shape, axisHeightMm ?? best[3], new Vec2(best[0], best[1]), tdeg, Math.Sqrt(bestCost / pts.Count), pts.Count);
    }

    /// <summary>Downhill simplex minimisation (small problems).</summary>
    static double[] NelderMead(Func<double[], double> f, double[] x0, double[] step, int iterations)
    {
        int n = x0.Length;
        var simplex = new double[n + 1][];
        var val = new double[n + 1];
        simplex[0] = (double[])x0.Clone();
        for (int i = 0; i < n; i++)
        {
            simplex[i + 1] = (double[])x0.Clone();
            simplex[i + 1][i] += step[i];
        }
        for (int i = 0; i <= n; i++) val[i] = f(simplex[i]);
        for (int it = 0; it < iterations; it++)
        {
            var order = Enumerable.Range(0, n + 1).OrderBy(i => val[i]).ToArray();
            simplex = [.. order.Select(i => simplex[i])];
            val = [.. order.Select(i => val[i])];
            if (Math.Abs(val[n] - val[0]) < 1e-12 * (1 + Math.Abs(val[0])) && it > 50) break;
            var centroid = new double[n];
            for (int i = 0; i < n; i++)
                for (int d = 0; d < n; d++) centroid[d] += simplex[i][d] / n;
            double[] Along(double k) => [.. Enumerable.Range(0, n).Select(d => centroid[d] + k * (simplex[n][d] - centroid[d]))];
            var r = Along(-1);
            double fr = f(r);
            if (fr < val[0])
            {
                var e = Along(-2);
                double fe = f(e);
                (simplex[n], val[n]) = fe < fr ? (e, fe) : (r, fr);
            }
            else if (fr < val[n - 1]) (simplex[n], val[n]) = (r, fr);
            else
            {
                var ct = fr < val[n] ? Along(-0.5) : Along(0.5);
                double fc = f(ct);
                if (fc < Math.Min(fr, val[n])) (simplex[n], val[n]) = (ct, fc);
                else
                    for (int i = 1; i <= n; i++)
                    {
                        for (int d = 0; d < n; d++) simplex[i][d] = simplex[0][d] + 0.5 * (simplex[i][d] - simplex[0][d]);
                        val[i] = f(simplex[i]);
                    }
            }
        }
        int bi = Enumerable.Range(0, n + 1).MinBy(i => val[i]);
        return simplex[bi];
    }
}
