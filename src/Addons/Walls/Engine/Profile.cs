using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// A wall's top seen from the front, left to right (mm, y up): straight stretches, each level or sloping.
/// A straight top is one stretch; battlements and broken tops are many.
/// </summary>
public sealed class Profile
{
    /// <summary>A stretch of the top from x0 to x1, at y0 at its left and y1 at its right.</summary>
    public readonly record struct Seg(double X0, double X1, double Y0, double Y1)
    {
        public double At(double x) => X1 - X0 < 1e-12 ? Math.Min(Y0, Y1) : Y0 + (Y1 - Y0) * Math.Clamp((x - X0) / (X1 - X0), 0, 1);
    }

    public IReadOnlyList<Seg> Segs { get; }

    public Profile(IEnumerable<Seg> segs) => Segs = [.. segs.Where(s => s.X1 - s.X0 > 1e-9).OrderBy(s => s.X0)];

    /// <summary>Level stretches.</summary>
    public Profile(IEnumerable<(double X0, double X1, double Y)> steps) : this(steps.Select(s => new Seg(s.X0, s.X1, s.Y, s.Y))) { }

    public static Profile Level(double x0, double x1, double y) => new([new Seg(x0, x1, y, y)]);

    /// <summary>Through these points, left to right.</summary>
    public static Profile Line(IReadOnlyList<(double X, double Y)> points)
    {
        var segs = new List<Seg>();
        for (int i = 0; i + 1 < points.Count; i++) segs.Add(new(points[i].X, points[i + 1].X, points[i].Y, points[i + 1].Y));
        return new Profile(segs);
    }

    public double Min => Segs.Count == 0 ? 0 : Segs.Min(s => Math.Min(s.Y0, s.Y1));
    public double Max => Segs.Count == 0 ? 0 : Segs.Max(s => Math.Max(s.Y0, s.Y1));

    /// <summary>The height at x (the lower of two stretches where they meet).</summary>
    public double At(double x)
    {
        double best = double.NaN;
        foreach (var s in Segs)
            if (x >= s.X0 - 1e-9 && x <= s.X1 + 1e-9) best = double.IsNaN(best) ? s.At(x) : Math.Min(best, s.At(x));
        return double.IsNaN(best) ? 0 : best;
    }

    /// <summary>The lowest point over a stretch of the wall.</summary>
    public double Lowest(double x0, double x1)
    {
        double best = double.MaxValue;
        foreach (var s in Segs)
            if (s.X1 > x0 - 1e-9 && s.X0 < x1 + 1e-9)
                best = Math.Min(best, Math.Min(s.At(Math.Max(x0, s.X0)), s.At(Math.Min(x1, s.X1))));
        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>The area under the profile, down to <paramref name="y0"/>.</summary>
    public PathsD Under(double y0)
    {
        if (Segs.Count == 0) return [];
        double lo = Math.Min(y0, Min) - 1, hi = Max + 1;
        var polys = new PathsD();
        // One outline per run of joined stretches (they normally all join).
        var run = new PathD();
        void Close()
        {
            if (run.Count == 0) return;
            run.Add(new PointD(run[^1].x, lo));
            run.Add(new PointD(run[0].x, lo));
            polys.Add(run);
            run = [];
        }
        double lastX = double.NaN;
        foreach (var s in Segs)
        {
            if (run.Count > 0 && Math.Abs(s.X0 - lastX) > 1e-6) Close();
            run.Add(new PointD(s.X0, s.Y0));
            run.Add(new PointD(s.X1, s.Y1));
            lastX = s.X1;
        }
        Close();
        var b = Bounds;
        return Geo.Intersect(Geo.Union(polys), [Geo.Rect(b.X0, y0, b.X1, hi)]);
    }

    (double X0, double X1) Bounds => (Segs[0].X0, Segs.Max(s => s.X1));

    /// <summary>Every place a stretch starts or ends.</summary>
    public IEnumerable<double> Breaks => Segs.SelectMany(s => new[] { s.X0, s.X1 });

    /// <summary>
    /// Two profiles combined over the places either changes: <paramref name="f"/> is given the middle of each
    /// piece (for anything that goes by place) and the two heights at each of its ends. Where the two cross, the
    /// piece is split there, so taking the lower of the two is exact.
    /// </summary>
    public static Profile Combine(Profile a, Profile b, Func<double, double, double, double> f)
    {
        var xs = a.Breaks.Concat(b.Breaks).Select(x => Math.Round(x, 6)).Distinct().Order().ToList();
        var segs = new List<Seg>();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            double x0 = xs[i], x1 = xs[i + 1], m = (x0 + x1) / 2;
            if (x1 - x0 < 1e-9) continue;
            var sa = a.SegAt(m); var sb = b.SegAt(m);
            double a0 = sa?.At(x0) ?? 0, a1 = sa?.At(x1) ?? 0, b0 = sb?.At(x0) ?? 0, b1 = sb?.At(x1) ?? 0;
            double d0 = a0 - b0, d1 = a1 - b1;
            if (d0 * d1 < -1e-12)
            {
                double xc = x0 + (x1 - x0) * d0 / (d0 - d1), yc = a0 + (a1 - a0) * (xc - x0) / (x1 - x0);
                double m0 = (x0 + xc) / 2, m1 = (xc + x1) / 2;
                segs.Add(new(x0, xc, f(m0, a0, b0), f(m0, yc, yc)));
                segs.Add(new(xc, x1, f(m1, yc, yc), f(m1, a1, b1)));
            }
            else segs.Add(new(x0, x1, f(m, a0, b0), f(m, a1, b1)));
        }
        return new Profile(segs);
    }

    Seg? SegAt(double x)
    {
        foreach (var s in Segs) if (x >= s.X0 - 1e-9 && x <= s.X1 + 1e-9) return s;
        return null;
    }

    /// <summary>Every height changed by <paramref name="f"/> (given the place and the height).</summary>
    public Profile Map(Func<double, double, double> f) => new(Segs.Select(s => new Seg(s.X0, s.X1, f(s.X0, s.Y0), f(s.X1, s.Y1))));

    /// <summary>Neighbouring stretches that carry straight on joined.</summary>
    public Profile Merged()
    {
        var o = new List<Seg>();
        foreach (var s in Segs)
        {
            if (o.Count > 0)
            {
                var p = o[^1];
                if (Math.Abs(p.X1 - s.X0) < 1e-6 && Math.Abs(p.Y1 - s.Y0) < 1e-6)
                {
                    double slopeP = (p.Y1 - p.Y0) / (p.X1 - p.X0), slopeS = (s.Y1 - s.Y0) / (s.X1 - s.X0);
                    if (Math.Abs(slopeP - slopeS) < 1e-6) { o[^1] = new(p.X0, s.X1, p.Y0, s.Y1); continue; }
                }
            }
            o.Add(s);
        }
        return new Profile(o);
    }
}
