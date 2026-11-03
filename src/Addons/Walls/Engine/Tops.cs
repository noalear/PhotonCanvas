using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>How a wall's top is broken: crumbled along its stones, a deep breach, one end fallen, weathered all along, shattered in jagged cracks, or the first broken top's stone-sized steps.</summary>
public enum RuinStyle { None, Crumbled, Breach, FallenEnd, Weathered, Shattered, Stepped }

/// <summary>Where a broken top is deepest (for a fallen end: which end has fallen).</summary>
public enum RuinPlace { Anywhere, Left, Middle, Right }

/// <summary>The heights of the merlons along battlements.</summary>
public enum MerlonPattern { Even, Alternating, TallEnds }

/// <summary>
/// The tops of walls seen from the front: battlements (their merlons all the same height or not) and the ways
/// a top can be broken, over a straight top or battlements alike. A break that follows the texture's stones
/// takes whole stones away, so the edge runs along the joints like real fallen masonry.
/// </summary>
public static class Tops
{
    /// <summary>The broken tops, in the order the window lists them, with what each looks like.</summary>
    public static readonly IReadOnlyList<(RuinStyle Style, string Name, string About)> Styles =
    [
        (RuinStyle.None, "Not broken", "The top is whole."),
        (RuinStyle.Crumbled, "Crumbled", "Stones have fallen out along the top, most of them in one place, the break following the joints."),
        (RuinStyle.Breach, "Breach", "A deep gap knocked through the top, with steep sides and crumbled shoulders."),
        (RuinStyle.FallenEnd, "Fallen end", "Standing to full height at one end, falling away towards the other."),
        (RuinStyle.Weathered, "Weathered", "Worn down unevenly all along, with a few deeper gaps."),
        (RuinStyle.Shattered, "Shattered", "Jagged cracks rather than stones, for concrete, rock or metal."),
        (RuinStyle.Stepped, "Stepped", "The first broken top: a dip in stone-sized steps."),
    ];

    /// <summary>
    /// Battlements over a wall <paramref name="top"/> high: merlons up to the top (the shorter ones, if they vary,
    /// <see cref="WallSpec.MerlonStepMm"/> lower) and the crenels' floor at <paramref name="floor"/>.
    /// </summary>
    public static Profile Battlements(WallSpec s, double L, double top, double floor, IReadOnlyList<(double X0, double X1)> crenels)
    {
        int n = crenels.Count + 1;
        double step = Math.Clamp(s.MerlonStepMm, 0, Math.Max(0, top - floor - 1));
        bool Tall(int i) => s.Merlons switch
        {
            MerlonPattern.Alternating => i % 2 == 0,
            MerlonPattern.TallEnds => i == 0 || i == n - 1,
            _ => true,
        };
        var steps = new List<(double, double, double)>();
        double x = 0;
        int k = 0;
        foreach (var (c0, c1) in crenels)
        {
            steps.Add((x, c0, Tall(k++) ? top : top - step));
            steps.Add((c0, c1, floor));
            x = c1;
        }
        steps.Add((x, L, Tall(k) ? top : top - step));
        return new Profile(steps).Merged();
    }

    /// <summary>
    /// A top broken the spec's way (<see cref="WallSpec.Breaks"/>), never higher than <paramref name="top"/> and never
    /// lower than <paramref name="floor"/>. The ends, <paramref name="keep"/> mm, stay at least
    /// <paramref name="endFloor"/> high. Each <paramref name="face"/> breaks its own way round the same place, the
    /// whole break scaled by <paramref name="depthScale"/>. <paramref name="stones"/> are the texture's stones on this
    /// face (in the front's x): when they're stone-sized the break takes whole stones away.
    /// </summary>
    public static Profile Break(WallSpec s, Profile top, double L, double keep, double endFloor, double floor, int face, double depthScale, PathsD? stones, out bool shallower)
    {
        shallower = false;
        var style = s.Breaks;
        if (style == RuinStyle.None || top.Segs.Count == 0) return top;
        double H = top.Max;
        double stepX = Math.Clamp(s.SizeA / 2, 1.5, 8), stepY = Math.Clamp(s.SizeB, 1, 8);
        double depth = Math.Clamp(s.RuinDepth, 0.05, 0.8) * s.HeightMm * depthScale;
        keep = Math.Clamp(keep, 0, L / 3);
        endFloor = Math.Min(endFloor, H);
        var shared = new Random(s.Seed * 7919 + 13);
        var rnd = new Random(s.Seed * 7919 + 13 + face * 6007);

        if (style == RuinStyle.Stepped)
        {
            var stepped = Stepped(s, L, H, keep, depth, shared, rnd);
            if (stepped.Min < floor) shallower = true;
            return Profile.Combine(top, stepped, (_, a, b) => Math.Max(Math.Min(a, b), Math.Min(a, floor))).Merged();
        }

        var dip = Shape(style, s, L, keep, depth, stepX, shared);
        // The deepest point has to stay above the floor (with room for a stone's worth of unevenness): any deeper
        // and the whole break is made shallower.
        double deepest = 0;
        for (double x = 0; x <= L; x += 0.5) deepest = Math.Max(deepest, dip(x));
        double room = Math.Max(0, H - floor - 0.6 * stepY), scale = 1;
        if (deepest > room + 1e-6) { scale = room / deepest; shallower = true; }
        double D(double x) => scale * dip(x);

        bool byStones = style != RuinStyle.Shattered && StoneSized(stones, s.HeightMm);
        var pts = byStones
            ? FollowStones(stones!, D, depth * scale, H, L, keep, endFloor, floor, s.GrooveMm, style == RuinStyle.Weathered ? 1.6 : 0.9, rnd)
            : Jagged(style, D, depth * scale, H, L, keep, endFloor, floor, stepX, stepY, rnd);
        var broken = Profile.Line(pts);
        return Profile.Combine(top, broken, (_, a, b) => Math.Max(Math.Min(a, b), Math.Min(a, floor))).Merged();
    }

    /// <summary>How deep the break is along the wall (mm, 0 where it's whole).</summary>
    static Func<double, double> Shape(RuinStyle style, WallSpec s, double L, double keep, double depth, double stepX, Random shared)
    {
        double a = keep, u = Math.Max(1, L - 2 * keep);
        double place = s.RuinAt switch { RuinPlace.Left => 0.22, RuinPlace.Middle => 0.5, RuinPlace.Right => 0.78, _ => 0.3 + 0.4 * shared.NextDouble() };
        double c = a + u * place;
        var noise = Noise(shared, style == RuinStyle.Weathered ? Math.Max(2, 1.2 * stepX) : Math.Max(3, 2.5 * stepX), L);
        static double Bump(double x, double c, double hw) { double t = Math.Abs(x - c) / hw; return t >= 1 ? 0 : Math.Pow(Math.Cos(t * Math.PI / 2), 2); }
        (double C, double Hw, double Amp)[] Extras(int n, double amp0, double amp1, double hw0, double hw1) =>
            [.. Enumerable.Range(0, n).Select(_ => (a + u * shared.NextDouble(), u * (hw0 + (hw1 - hw0) * shared.NextDouble()), depth * (amp0 + (amp1 - amp0) * shared.NextDouble())))];
        double Sum((double C, double Hw, double Amp)[] e, double x) => e.Sum(b => b.Amp * Bump(x, b.C, b.Hw));

        switch (style)
        {
            case RuinStyle.Breach:
            {
                double hw = Math.Max(u * (0.12 + 0.08 * shared.NextDouble()), 2.5 * stepX);
                return x =>
                {
                    double t = Math.Abs(x - c) / hw;
                    double core = t >= 1 ? 0 : Math.Pow(1 - Math.Pow(t, 2.5), 0.7);
                    return Math.Max(0, depth * 1.3 * core + depth * 0.25 * Bump(x, c, hw * 2.2) + depth * 0.06 * noise(x));
                };
            }
            case RuinStyle.FallenEnd:
            {
                bool leftFallen = s.RuinAt switch { RuinPlace.Left => true, RuinPlace.Right => false, _ => shared.NextDouble() < 0.5 };
                return x =>
                {
                    double p = Math.Clamp(leftFallen ? (a + u - x) / u : (x - a) / u, 0, 1);
                    double fall = Math.Pow(Math.Clamp((p - 0.12) / 0.88, 0, 1), 1.15);
                    return Math.Max(0, depth * 1.25 * fall + depth * 0.1 * (noise(x) + 1) / 2 * Math.Min(1, p * 4));
                };
            }
            case RuinStyle.Weathered:
            {
                var notches = Extras(2 + shared.Next(2), 0.25, 0.45, 0.05, 0.1);
                return x => Math.Max(0, depth * 0.3 * (0.5 + 0.5 * noise(x)) + Sum(notches, x));
            }
            default:   // crumbled, shattered
            {
                double hw = u * (0.26 + 0.14 * shared.NextDouble());
                var extras = Extras(2, 0.2, 0.4, 0.05, 0.1);
                return x => Math.Max(0, depth * Bump(x, c, hw) + Sum(extras, x) + depth * 0.1 * (noise(x) + 1) / 2);
            }
        }
    }

    /// <summary>Smooth noise from −1 to 1 along the wall, changing over about <paramref name="wave"/> mm.</summary>
    static Func<double, double> Noise(Random r, double wave, double L)
    {
        int n = (int)Math.Ceiling(L / wave) + 3;
        var v = Enumerable.Range(0, n).Select(_ => r.NextDouble() * 2 - 1).ToArray();
        return x =>
        {
            double f = Math.Clamp(x / wave, 0, n - 2);
            int i = (int)Math.Floor(f);
            double t = (1 - Math.Cos((f - i) * Math.PI)) / 2;
            return v[i] * (1 - t) + v[Math.Min(n - 1, i + 1)] * t;
        };
    }

    /// <summary>Whether the texture's pieces are stones a break can take away one by one: many of them, small and not long and thin like planks.</summary>
    static bool StoneSized(PathsD? stones, double H)
    {
        if (stones is null || stones.Count < 6) return false;
        var sizes = stones.Select(p => Clipper.GetBounds(p)).Select(b => (Big: Math.Max(b.Width, b.Height), Aspect: Math.Max(b.Width, b.Height) / Math.Max(0.1, Math.Min(b.Width, b.Height)))).ToList();
        double big = sizes.Select(z => z.Big).Order().ElementAt(sizes.Count / 2);
        double aspect = sizes.Select(z => z.Aspect).Order().ElementAt(sizes.Count / 2);
        return big <= Math.Max(6, 0.3 * H) && aspect <= 4;
    }

    /// <summary>
    /// The top of what's left when every stone above the break is taken away (each judged by its middle, a little
    /// at random), with the joints between those left closed up and anything left hanging over a gap taken too.
    /// </summary>
    static List<(double X, double Y)> FollowStones(PathsD stones, Func<double, double> D, double depth, double H, double L, double keep, double endFloor, double floor, double groove, double loose, Random rnd)
    {
        var kept = new PathsD();
        foreach (var st in stones)
        {
            var b = Clipper.GetBounds(st);
            if (b.right < -1 || b.left > L + 1 || b.top > H) continue;
            double top = Math.Min(b.bottom, H), cx = Math.Clamp((b.left + b.right) / 2, 0, L), cy = (b.top + top) / 2, h = top - b.top;
            double d = D(cx);
            bool end = cx < keep || cx > L - keep;
            double lim = end ? Math.Max(H - d, endFloor) : H - d;
            if (top <= floor + 0.01 || d < 0.04 * depth || cy + (rnd.NextDouble() - 0.5) * h * loose <= lim) kept.Add(st);
        }
        double g = Math.Clamp(groove, 0.2, 2);
        var k = Geo.Union(Geo.Offset(Geo.Union(kept), g / 2 + 0.05, round: true));
        k = Geo.Offset(Geo.Offset(k, 0.8, round: true), -0.8, round: true);       // the joints between stones left closed up
        k = Geo.Offset(Geo.Offset(k, -0.6, round: true), 0.6, round: true);       // slivers too thin to cut taken away
        k = Geo.Union(k, [Geo.Rect(-1, -2, L + 1, floor)]);

        // The top of what's left, felt for along upright lines across the wall, then again more finely either side
        // of every jump so the stones' ends come out upright.
        var tops = new Dictionary<long, double>();
        static long Key(double x) => (long)Math.Round(x * 1e4);
        void Feel(IEnumerable<double> at)
        {
            var sorted = at.Order().ToArray();
            var lines = sorted.Select(x => new PathD { new(x, -1), new(x, H + 2) }).ToList();
            foreach (var x in sorted) tops[Key(x)] = floor;
            double Nearest(double x)
            {
                int i = Array.BinarySearch(sorted, x);
                if (i >= 0) return sorted[i];
                i = ~i;
                if (i == 0) return sorted[0];
                if (i == sorted.Length) return sorted[^1];
                return x - sorted[i - 1] < sorted[i] - x ? sorted[i - 1] : sorted[i];
            }
            foreach (var seg in Geo.ClipLines(lines, k))
            {
                if (seg.Count < 2) continue;
                double y0 = seg.Min(p => p.y), y1 = seg.Max(p => p.y);
                if (y0 > floor - 0.5 || y1 < floor - 0.5) continue;   // only what stands on the wall below
                long key = Key(Nearest(seg[0].x));
                tops[key] = Math.Max(tops[key], y1);
            }
        }
        var xs = Samples(L, keep, 0.25);
        Feel(xs);
        var fine = new List<double>();
        for (int i = 0; i + 1 < xs.Count; i++)
            if (Math.Abs(tops[Key(xs[i])] - tops[Key(xs[i + 1])]) > 0.4)
                for (int j = 1; j < 10; j++) fine.Add(Math.Round(xs[i] + (xs[i + 1] - xs[i]) * j / 10, 6));
        fine = [.. fine.Where(x => !tops.ContainsKey(Key(x)))];
        if (fine.Count > 0) { Feel(fine); xs = [.. xs.Concat(fine).Order()]; }
        return [.. xs.Select(x => (x, Ends(x, Math.Min(H, tops[Key(x)]), L, keep, endFloor)))];
    }

    /// <summary>A break in a jagged line: short straight cracks at odd angles, rougher where it's deeper.</summary>
    static List<(double X, double Y)> Jagged(RuinStyle style, Func<double, double> D, double depth, double H, double L, double keep, double endFloor, double floor, double stepX, double stepY, Random rnd)
    {
        bool shattered = style == RuinStyle.Shattered;
        double size = Math.Min(stepY, 2 * stepX), amp = shattered ? size * 0.9 : size * 0.5;
        var xs = new List<double> { 0 };
        for (double x = 0; ;)
        {
            x += stepX * (shattered ? 0.4 + 0.7 * rnd.NextDouble() : 0.5 + 0.6 * rnd.NextDouble());
            if (x >= L) break;
            xs.Add(x);
        }
        xs.AddRange(Samples(L, keep, double.MaxValue));
        xs = [.. xs.Select(x => Math.Round(x, 6)).Distinct().Order()];
        return [.. xs.Select(x =>
        {
            double d = D(x);
            double rough = depth <= 0 ? 0 : Math.Min(1, d / (0.15 * depth));
            double y = H - d + (rnd.NextDouble() * 2 - 1) * amp * rough;
            return (x, Ends(x, Math.Clamp(y, floor, H), L, keep, endFloor));
        })];
    }

    /// <summary>The ends of the wall stay at least <paramref name="endFloor"/> high (whole, where they go into a column).</summary>
    static double Ends(double x, double y, double L, double keep, double endFloor) =>
        keep > 0 && (x < keep || x > L - keep) ? Math.Max(y, endFloor) : y;

    /// <summary>Places along the wall every <paramref name="step"/> mm, with the ends of the kept ends either side.</summary>
    static List<double> Samples(double L, double keep, double step)
    {
        var xs = new List<double> { 0, L };
        if (step < L) for (double x = step; x < L; x += step) xs.Add(x);
        if (keep > 0) xs.AddRange([keep - 1e-4, keep + 1e-4, L - keep - 1e-4, L - keep + 1e-4]);
        return [.. xs.Where(x => x >= 0 && x <= L).Select(x => Math.Round(x, 6)).Distinct().Order()];
    }

    /// <summary>The first broken top: a dip in stone-sized steps, deepest somewhere near the middle, the ends whole for <paramref name="keep"/> mm.</summary>
    static Profile Stepped(WallSpec s, double L, double H, double keep, double depth, Random shared, Random rnd)
    {
        double u = L - 2 * keep;
        double centre = keep + u * (0.35 + 0.3 * shared.NextDouble()), half = u * (0.3 + 0.15 * shared.NextDouble());
        double stepX = Math.Clamp(s.SizeA / 2, 1.5, 8), stepY = Math.Clamp(s.SizeB, 1, 8);
        var steps = new List<(double, double, double)>();
        if (keep > 0) steps.Add((0, keep, H));
        for (double x = keep; x < L - keep - 1e-6; x += stepX)
        {
            double x1 = Math.Min(L - keep, x + stepX);
            double t = Math.Clamp(Math.Abs((x + x1) / 2 - centre) / half, 0, 1);
            double dip = depth * Math.Pow(Math.Cos(t * Math.PI / 2), 2) + (rnd.NextDouble() - 0.3) * stepY;
            steps.Add((x, x1, H - Math.Max(0, Math.Round(dip / stepY) * stepY)));
        }
        if (keep > 0) steps.Add((L - keep, L, H));
        return new Profile(steps).Merged();
    }
}
