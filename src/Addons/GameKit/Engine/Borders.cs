using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// Borders round a marked face: a band a little in from the edge, filled with a repeating motif
/// (rope, Greek key, rivets…) bent to follow the edge, straight sides and curves alike. Each side
/// between two corners holds a whole number of repeats, so the pattern meets itself neatly, and
/// the corners get a piece of their own. Fine lines come back as lines to score; the rest as areas
/// to engrave.
/// </summary>
public static class Borders
{
    public sealed record Info(BorderStyle Style, string Name, string Theme);

    public static readonly IReadOnlyList<Info> All =
    [
        new(BorderStyle.None, "None", ""),
        new(BorderStyle.Line, "Line", "Classic"),
        new(BorderStyle.Double, "Double line", "Classic"),
        new(BorderStyle.Dashed, "Dashed", "Wargame"),
        new(BorderStyle.Beads, "Beads", "Classic"),
        new(BorderStyle.Rope, "Rope", "Medieval"),
        new(BorderStyle.Zigzag, "Zigzag", "Wargame"),
        new(BorderStyle.Wave, "Wave", "Classic"),
        new(BorderStyle.GreekKey, "Greek key", "Desert"),
        new(BorderStyle.Vine, "Vine", "Woodland"),
        new(BorderStyle.Rivets, "Rivets", "Dungeon"),
        new(BorderStyle.Hazard, "Hazard stripes", "Sci-fi"),
        new(BorderStyle.Circuit, "Circuit", "Sci-fi"),
        new(BorderStyle.Stones, "Stones", "Cave"),
        new(BorderStyle.Arches, "Gothic arches", "Gothic"),
        new(BorderStyle.Art, "Your design", ""),
    ];

    public static string Name(BorderStyle s) => All.FirstOrDefault(b => b.Style == s)?.Name ?? s.ToString();

    /// <summary>
    /// The border for <paramref name="region"/> (areas, y up): a band <paramref name="band"/> wide whose outer
    /// edge is <paramref name="inset"/> in from the region's outline. <paramref name="art"/> is the motif for
    /// <see cref="BorderStyle.Art"/>.
    /// </summary>
    public static (PathsD Areas, List<PathD> Lines) Make(PathsD region, double inset, double band, BorderStyle style, MarkArt? art = null, int seed = 1)
    {
        var areas = new PathsD();
        var lines = new List<PathD>();
        if (style == BorderStyle.None || region.Count == 0 || band < 0.3) return (areas, lines);
        var centre = Geo.Offset(region, -(inset + band / 2));
        var loops = Geo.Tidy(centre).Where(p => Clipper.Area(p) > band * band).ToList();
        var rnd = new Random(seed * 7907 + (int)style * 131);
        var artShape = style == BorderStyle.Art && art is not null ? MarkArts.Shape(art) : null;
        foreach (var loop in loops)
        {
            var pts = Clean(loop, band * 0.02);
            if (pts.Count < 3) continue;
            switch (style)
            {
                case BorderStyle.Line:
                    lines.Add(Geo.Loop(pts));
                    continue;
                case BorderStyle.Double:
                    foreach (var d in new[] { band * 0.3, -band * 0.3 })
                        lines.AddRange(Geo.Tidy(Geo.Offset([pts], d)).Select(Geo.Loop));
                    continue;
                case BorderStyle.Rivets:
                case BorderStyle.Hazard:
                    // Edge lines, both sides of the band.
                    double e = style == BorderStyle.Rivets ? band * 0.42 : band * 0.5;
                    foreach (var d in new[] { e, -e })
                        lines.AddRange(Geo.Tidy(Geo.Offset([pts], d)).Select(Geo.Loop));
                    break;
            }
            foreach (var run in Runs(pts))
            {
                var (a, l) = Motif(run, band, style, artShape, rnd);
                areas.AddRange(a);
                lines.AddRange(l);
            }
        }
        // Kept inside the face, a hair in from its edge.
        var keep = Geo.Offset(region, -Math.Max(0.2, inset * 0.4));
        return (Geo.Intersect(Geo.Union(areas), keep), Geo.ClipLines(lines, keep));
    }

    /// <summary>The loop without points closer than <paramref name="min"/> to the one before.</summary>
    static PathD Clean(PathD p, double min)
    {
        var o = new PathD();
        foreach (var q in p)
            if (o.Count == 0 || Dist(o[^1], q) > min) o.Add(q);
        while (o.Count > 2 && Dist(o[0], o[^1]) <= min) o.RemoveAt(o.Count - 1);
        return o;
    }

    static double Dist(PointD a, PointD b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    /// <summary>A stretch of the loop between two corners (or the whole loop, with none): its points, and whether it goes all round.</summary>
    sealed class Run
    {
        public required List<PointD> Pts { get; init; }
        public required bool Closed { get; init; }
        /// <summary>The corner at its start (and so at the end of the one before it).</summary>
        public PointD? Corner { get; init; }
        public double[] Cum { get; private set; } = [];
        public double Length => Cum.Length == 0 ? 0 : Cum[^1];

        public void Measure()
        {
            var c = new double[Pts.Count];
            for (int i = 1; i < Pts.Count; i++) c[i] = c[i - 1] + Dist(Pts[i - 1], Pts[i]);
            Cum = c;
        }

        public PointD At(double s)
        {
            if (Closed) s = ((s % Length) + Length) % Length;
            s = Math.Clamp(s, 0, Length);
            int i = Array.BinarySearch(Cum, s);
            if (i >= 0) return Pts[i];
            i = ~i;
            if (i <= 0) return Pts[0];
            if (i >= Pts.Count) return Pts[^1];
            double f = (s - Cum[i - 1]) / Math.Max(1e-12, Cum[i] - Cum[i - 1]);
            return new PointD(Pts[i - 1].x + (Pts[i].x - Pts[i - 1].x) * f, Pts[i - 1].y + (Pts[i].y - Pts[i - 1].y) * f);
        }

        /// <summary>The unit normal to the left (inward on an anticlockwise loop).</summary>
        public PointD Normal(double s, double h)
        {
            double a = Closed ? s - h : Math.Max(0, s - h), b = Closed ? s + h : Math.Min(Length, s + h);
            var p = At(a);
            var q = At(b);
            double dx = q.x - p.x, dy = q.y - p.y, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-12 ? new PointD(0, 1) : new PointD(-dy / len, dx / len);
        }
    }

    /// <summary>The loop cut at its corners (turns sharper than about 25°).</summary>
    static List<Run> Runs(PathD loop)
    {
        int n = loop.Count;
        var corner = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var a = loop[(i + n - 1) % n];
            var b = loop[i];
            var c = loop[(i + 1) % n];
            double t1 = Math.Atan2(b.y - a.y, b.x - a.x), t2 = Math.Atan2(c.y - b.y, c.x - b.x);
            double turn = Math.Abs(Math.IEEERemainder(t2 - t1, 2 * Math.PI));
            corner[i] = turn > 25 * Math.PI / 180;
        }
        var runs = new List<Run>();
        int first = Array.IndexOf(corner, true);
        if (first < 0)
        {
            var r = new Run { Pts = [.. loop, loop[0]], Closed = true };
            r.Measure();
            runs.Add(r);
            return runs;
        }
        int start = first;
        do
        {
            var pts = new List<PointD> { loop[start] };
            int j = (start + 1) % n;
            while (true)
            {
                pts.Add(loop[j]);
                if (corner[j]) break;
                j = (j + 1) % n;
            }
            var r = new Run { Pts = pts, Closed = false, Corner = loop[start] };
            r.Measure();
            runs.Add(r);
            start = j;
        } while (start != first);
        return runs;
    }

    /// <summary>
    /// A motif repeated along a run. Shapes are made in the run's own coordinates (x along it, y across,
    /// left positive) and bent onto it.
    /// </summary>
    static (PathsD Areas, List<PathD> Lines) Motif(Run run, double b, BorderStyle style, PathsD? art, Random rnd)
    {
        var areas = new PathsD();
        var lines = new List<PathD>();
        var strokes = new List<(PathD Line, double W)>();
        double L = run.Length;
        if (L < b * 0.3) return (areas, lines);
        double h = Math.Min(b * 0.3, L / 4);
        PointD Map(double x, double y)
        {
            var p = run.At(x);
            var nrm = run.Normal(x, h);
            return new PointD(p.x + nrm.x * y, p.y + nrm.y * y);
        }
        PathD Bend(IEnumerable<PointD> local)
        {
            // Points no further apart than a quarter of the band, so curves bend smoothly.
            var src = local.ToList();
            var o = new PathD();
            for (int i = 0; i < src.Count; i++)
            {
                var p = src[i];
                o.Add(Map(p.x, p.y));
                if (i + 1 >= src.Count) break;
                var q = src[i + 1];
                int steps = (int)Math.Ceiling(Dist(p, q) / Math.Max(0.1, b * 0.25));
                for (int k = 1; k < steps; k++) o.Add(Map(p.x + (q.x - p.x) * k / steps, p.y + (q.y - p.y) * k / steps));
            }
            return o;
        }
        void Area(IEnumerable<PointD> local) { var p = Bend(local); if (p.Count >= 3) areas.Add(Clipper.Area(p) < 0 ? Reversed(p) : p); }
        void Stroke(IEnumerable<PointD> local, double w) => strokes.Add((Bend(local), w));
        void Corner(Func<double, double, double, PathD> make)
        {
            if (run.Corner is not { } c) return;
            // Turned to the run's direction as it leaves the corner.
            var nrm = run.Normal(Math.Min(L, b * 0.2), b * 0.1);
            double deg = Math.Atan2(-nrm.x, nrm.y) * 180 / Math.PI;
            var shape = make(c.x, c.y, deg);
            if (shape.Count >= 3) areas.Add(Clipper.Area(shape) < 0 ? Reversed(shape) : shape);
        }

        // Each style: how long one repeat is, how much room is left at the corners, then the repeats.
        bool corners = run.Corner is not null;
        double gap = corners ? b * 0.55 : 0;
        double x0 = gap, x1 = L - gap, span = x1 - x0;
        int Count(double tile) => Math.Max(1, (int)Math.Round(span / tile));
        switch (style)
        {
            case BorderStyle.Dashed:
            {
                // Dashes from corner to corner, so each corner is an L of two dashes meeting.
                double dash = b * 1.6, space = b * 0.9;
                int n = Math.Max(1, (int)Math.Round((L + space) / (dash + space)));
                double k = L / (n * dash + (n - 1) * space);
                if (run.Closed) k = L / (n * (dash + space));
                for (int i = 0; i < n; i++)
                {
                    double s = i * (dash + space) * k;
                    lines.Add(Bend([new(s, 0), new(s + dash * k, 0)]));
                }
                break;
            }
            case BorderStyle.Beads:
            {
                if (span <= 0) break;
                int n = Count(b * 0.95);
                double t = span / n;
                for (int i = 0; i < n; i++) areas.Add(Geo.Circle(Map(x0 + (i + 0.5) * t, 0).x, Map(x0 + (i + 0.5) * t, 0).y, b * 0.28, 0.01));
                Corner((x, y, _) => Geo.Circle(x, y, b * 0.4, 0.01));
                break;
            }
            case BorderStyle.Rope:
            {
                if (span <= 0) break;
                int n = Count(b * 0.62);
                double t = span / n;
                for (int i = 0; i < n; i++)
                {
                    double x = x0 + i * t;
                    Area(Lens(new(x - t * 0.35, -b * 0.46), new(x + t * 1.35, b * 0.46), b * 0.3));
                }
                Corner((x, y, _) => Geo.Circle(x, y, b * 0.42, 0.01));
                break;
            }
            case BorderStyle.Zigzag:
            {
                if (span <= 0) break;
                int n = Count(b * 1.1);
                double t = span / n;
                var z = new List<PointD> { new(x0, 0) };
                for (int i = 0; i < n; i++) { z.Add(new(x0 + (i + 0.25) * t, b * 0.36)); z.Add(new(x0 + (i + 0.75) * t, -b * 0.36)); }
                z.Add(new(x1, 0));
                Stroke(z, b * 0.16);
                Corner((x, y, deg) => Square(x, y, b * 0.5, deg + 45));
                break;
            }
            case BorderStyle.Wave:
            {
                if (span <= 0) break;
                int n = Count(b * 1.7);
                double t = span / n;
                var w = new List<PointD>();
                int steps = n * 24;
                for (int i = 0; i <= steps; i++) { double x = x0 + span * i / steps; w.Add(new(x, b * 0.34 * Math.Sin(2 * Math.PI * (x - x0) / t))); }
                Stroke(w, b * 0.16);
                Corner((x, y, _) => Geo.Circle(x, y, b * 0.3, 0.01));
                break;
            }
            case BorderStyle.GreekKey:
            {
                if (span <= 0) break;
                double g = b / 4.6;
                int n = Count(g * 5);
                double t = span / n, k = t / (g * 5);
                var keyLines = new List<PathD>();
                Stroke([new(x0 - (corners ? gap * 0.6 : 0), -2 * g), new(x1 + (corners ? gap * 0.6 : 0), -2 * g)], g * 0.55);
                for (int i = 0; i < n; i++)
                {
                    double x = x0 + i * t + 0.75 * g * k;
                    Stroke([new(x, -2 * g), new(x, 2 * g), new(x + 3.5 * g * k, 2 * g), new(x + 3.5 * g * k, -0.5 * g), new(x + 1.5 * g * k, -0.5 * g), new(x + 1.5 * g * k, 0.8 * g)], g * 0.55);
                }
                Corner((x, y, deg) => Square(x, y, b * 0.5, deg));
                break;
            }
            case BorderStyle.Vine:
            {
                if (span <= 0) break;
                int n = Count(b * 2);
                double t = span / n;
                var stem = new List<PointD>();
                int steps = n * 24;
                for (int i = 0; i <= steps; i++) { double x = x0 + span * i / steps; stem.Add(new(x, b * 0.2 * Math.Sin(2 * Math.PI * (x - x0) / t))); }
                Stroke(stem, b * 0.09);
                for (int i = 0; i < n; i++)
                    for (int side = 0; side < 2; side++)
                    {
                        // A leaf off each crest and trough, leaning forward.
                        double x = x0 + (i + 0.25 + side * 0.5) * t, y = side == 0 ? b * 0.2 : -b * 0.2;
                        double dir = side == 0 ? 1 : -1;
                        Area(Lens(new(x - t * 0.02, y * 0.9), new(x + t * 0.3, y + dir * b * 0.28), b * 0.22));
                        // A berry under each leaf.
                        var berry = Map(x - t * 0.12, -y * 0.2);
                        areas.Add(Geo.Circle(berry.x, berry.y, b * 0.07, 0.005));
                    }
                Corner((x, y, _) => Flower(x, y, b * 0.42));
                break;
            }
            case BorderStyle.Rivets:
            {
                if (span <= 0) break;
                int n = Count(b * 1.8);
                double t = span / n;
                for (int i = 0; i < n; i++) { var p = Map(x0 + (i + 0.5) * t, 0); areas.Add(Geo.Circle(p.x, p.y, b * 0.17, 0.01)); }
                Corner((x, y, _) => Geo.Circle(x, y, b * 0.24, 0.01));
                break;
            }
            case BorderStyle.Hazard:
            {
                double ex = corners ? b * 0.5 : 0;
                double sp = L - 2 * ex;
                if (sp <= 0) break;
                int n = Math.Max(1, (int)Math.Round(sp / b));
                double t = sp / n;
                for (int i = 0; i < n; i++)
                {
                    double x = ex + i * t;
                    Area([new(x, -b * 0.5), new(x + t * 0.5, -b * 0.5), new(x + t, b * 0.5), new(x + t * 0.5, b * 0.5)]);
                }
                Corner((x, y, deg) => Square(x, y, b, deg));
                break;
            }
            case BorderStyle.Circuit:
            {
                if (span <= 0) break;
                // A trace that steps between two levels, with branches ending in pads.
                double lo = -b * 0.18, hi = b * 0.24, w = b * 0.1;
                var trace = new List<PointD> { new(x0, lo) };
                double x = x0, y = lo;
                bool up = false;
                while (true)
                {
                    double runLen = b * (1.2 + rnd.NextDouble() * 1.8), jog = Math.Abs(hi - lo);
                    if (x + runLen + jog > x1 - b * 0.3) break;
                    x += runLen;
                    trace.Add(new(x, y));
                    double ny = up ? lo : hi;
                    x += jog;
                    trace.Add(new(x, ny));
                    // A branch off the corner, to a pad.
                    if (rnd.NextDouble() < 0.6)
                    {
                        double by = up ? b * 0.38 : -b * 0.4, bx = x - jog / 2 + b * 0.5;
                        if (bx < x1 - b * 0.3)
                        {
                            var from = new PointD(x - jog / 2, (y + ny) / 2);
                            Stroke([from, new(from.x + Math.Abs(by - from.y) * 0.7, by)], w * 0.8);
                            Pad(new(from.x + Math.Abs(by - from.y) * 0.7, by), b * 0.15);
                        }
                    }
                    y = ny;
                    up = !up;
                }
                trace.Add(new(x1, y));
                Stroke(trace, w);
                Pad(new(x0, lo), b * 0.17);
                Pad(new(x1, y), b * 0.17);
                Corner((cx, cy, deg) => Square(cx, cy, b * 0.42, deg));
                break;

                void Pad(PointD at, double r)
                {
                    var c = Map(at.x, at.y);
                    areas.AddRange(Geo.Subtract([Geo.Circle(c.x, c.y, r, 0.01)], [Geo.Circle(c.x, c.y, r * 0.45, 0.01)]));
                }
            }
            case BorderStyle.Stones:
            {
                double ex = corners ? b * 0.45 : 0;
                double sp = L - 2 * ex;
                if (sp <= 0) break;
                var cuts = new List<double> { 0 };
                while (cuts[^1] < sp) cuts.Add(cuts[^1] + b * (0.9 + rnd.NextDouble() * 0.9));
                // The last stone takes up what's left.
                double k = sp / cuts[^1];
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    double a = ex + cuts[i] * k + b * 0.08, c = ex + cuts[i + 1] * k - b * 0.08;
                    if (c - a < b * 0.3) continue;
                    double J() => (rnd.NextDouble() - 0.5) * b * 0.14;
                    Area([new(a + J(), -b * 0.42 + J()), new((a + c) / 2 + J(), -b * 0.46 + J()), new(c + J(), -b * 0.42 + J()), new(c + J() * 0.6, J()), new(c + J(), b * 0.42 + J()), new((a + c) / 2 + J(), b * 0.46 + J()), new(a + J(), b * 0.42 + J()), new(a + J() * 0.6, J())]);
                }
                Corner((x, y, deg) => Square(x, y, b * 0.78, deg + 8));
                break;
            }
            case BorderStyle.Arches:
            {
                if (span <= 0) break;
                int n = Count(b * 1.15);
                double t = span / n, w = b * 0.1, foot = -b * 0.44, spring = -b * 0.02, top = b * 0.42;
                Stroke([new(x0 - (corners ? gap * 0.7 : 0), foot), new(x1 + (corners ? gap * 0.7 : 0), foot)], w);
                for (int i = 0; i < n; i++)
                {
                    double a = x0 + i * t + t * 0.12, c = x0 + (i + 1) * t - t * 0.12, m = (a + c) / 2;
                    var arch = new List<PointD> { new(a, foot), new(a, spring) };
                    for (int j = 1; j <= 12; j++) { double f = j / 12.0; arch.Add(Bez(new(a, spring), new(a, spring + (top - spring) * 0.62), new(m, top), f)); }
                    for (int j = 1; j <= 12; j++) { double f = j / 12.0; arch.Add(Bez(new(m, top), new(c, spring + (top - spring) * 0.62), new(c, spring), f)); }
                    arch.Add(new(c, foot));
                    Stroke(arch, w);
                    if (i + 1 < n) { var p = Map(x0 + (i + 1) * t, top * 0.72); areas.Add(Geo.Circle(p.x, p.y, b * 0.08, 0.01)); }
                }
                Corner((x, y, deg) => Square(x, y, b * 0.42, deg + 45));
                break;
            }
            case BorderStyle.Art:
            {
                if (art is null || art.Count == 0 || span <= 0) break;
                var bb = Geo.Bounds(art);
                double aw = bb.right - bb.left, ah = bb.bottom - bb.top;
                if (aw <= 0 || ah <= 0) break;
                double scale = b * 0.9 / ah, tileW = aw * scale + b * 0.25;
                int n = Count(tileW);
                double t = span / n;
                for (int i = 0; i < n; i++)
                {
                    double cx = x0 + (i + 0.5) * t;
                    // Each copy tidied even-odd on its own, so holes in the design stay holes.
                    var copy = new PathsD(art.Select(p => Bend(p.Select(q => new PointD(cx + (q.x - (bb.left + bb.right) / 2) * scale, (q.y - (bb.top + bb.bottom) / 2) * scale)))));
                    areas.AddRange(Geo.Tidy(copy));
                }
                break;
            }
        }
        foreach (var (line, w) in strokes)
            areas.AddRange(Clipper.InflatePaths([line], w / 2, JoinType.Miter, EndType.Butt, 3, 4));
        return (areas, lines);
    }

    static PointD Bez(PointD a, PointD c, PointD b, double t) => new((1 - t) * (1 - t) * a.x + 2 * (1 - t) * t * c.x + t * t * b.x, (1 - t) * (1 - t) * a.y + 2 * (1 - t) * t * c.y + t * t * b.y);

    static PathD Reversed(PathD p) { var q = new PathD(p); q.Reverse(); return q; }

    /// <summary>A lens (pointed both ends) from <paramref name="a"/> to <paramref name="b"/>, <paramref name="w"/> wide at its middle.</summary>
    static List<PointD> Lens(PointD a, PointD b, double w)
    {
        double dx = b.x - a.x, dy = b.y - a.y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy)), nx = -dy / len, ny = dx / len;
        var o = new List<PointD>();
        const int n = 12;
        for (int i = 0; i <= n; i++) { double t = (double)i / n, v = w / 2 * Math.Sin(Math.PI * t); o.Add(new(a.x + dx * t + nx * v, a.y + dy * t + ny * v)); }
        for (int i = n - 1; i > 0; i--) { double t = (double)i / n, v = -w / 2 * Math.Sin(Math.PI * t); o.Add(new(a.x + dx * t + nx * v, a.y + dy * t + ny * v)); }
        return o;
    }

    static PathD Square(double x, double y, double size, double deg)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r), h = size / 2;
        return [.. new[] { (-h, -h), (h, -h), (h, h), (-h, h) }.Select(p => new PointD(x + p.Item1 * c - p.Item2 * s, y + p.Item1 * s + p.Item2 * c))];
    }

    /// <summary>A small five-petalled flower (one outline).</summary>
    static PathD Flower(double x, double y, double r)
    {
        var petals = new PathsD { Geo.Circle(x, y, r * 0.35, 0.005) };
        for (int i = 0; i < 5; i++)
        {
            double a = Math.PI / 2 + i * 2 * Math.PI / 5;
            petals.Add(Geo.Circle(x + Math.Cos(a) * r * 0.55, y + Math.Sin(a) * r * 0.55, r * 0.42, 0.005));
        }
        var u = Geo.Union(petals);
        return u.Count > 0 ? u.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First() : [];
    }
}
