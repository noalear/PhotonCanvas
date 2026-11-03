using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The ornaments that can go on gauges, rulers and templates: drawn here as filled areas in a box
/// from −1 to 1 each way (y up), so they engrave cleanly at any size. Those that point
/// (<see cref="Info.Pointing"/>) are drawn pointing right and turned to point outward at the ends.
/// </summary>
public static class Ornaments
{
    /// <summary>The name for your own design (<see cref="Markings.Art"/>) in the list.</summary>
    public const string ArtName = "Your design";

    public sealed record Info(string Name, string Theme, Func<PathsD> Draw, bool Pointing = false);

    public static readonly IReadOnlyList<Info> All =
    [
        new("Diamond", "Classic", Diamond),
        new("Star", "Classic", () => FacetStar(5, 1, 0.42, 90, 0.07)),
        new("Compass rose", "Classic", Compass),
        new("Laurel", "Classic", Laurel),
        new("Arrow", "Wargame", Arrow, true),
        new("Chevrons", "Wargame", Chevrons, true),
        new("Boot prints", "Wargame", Boots),
        new("Bullseye", "Wargame", Bullseye),
        new("Crossed swords", "Wargame", CrossedSwords),
        new("Fleur-de-lis", "Medieval", FleurDeLis),
        new("Sword", "Medieval", () => Sword(0)),
        new("Shield", "Medieval", Shield),
        new("Crown", "Medieval", Crown),
        new("Skull", "Dungeon", Skull),
        new("Key", "Dungeon", Key, true),
        new("D20", "Dungeon", D20),
        new("Flame", "Dungeon", Flame),
        new("Crystals", "Cave", Crystals),
        new("Bat", "Cave", Bat),
        new("Paw", "Cave", Paw),
        new("Reticle", "Sci-fi", Reticle),
        new("Gear", "Sci-fi", Gear),
        new("Hazard", "Sci-fi", Hazard),
        new("Lightning", "Sci-fi", Lightning),
        new("Sun", "Desert", Sun),
        new("Pyramid", "Desert", Pyramid),
        new("Moon and star", "Desert", Moon),
        new("Gothic cross", "Gothic", GothicCross),
        new("Quatrefoil", "Gothic", Quatrefoil),
        new("Burst", "Gothic", Burst),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(o => o.Name)];

    public static Info? Find(string? name) => All.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    static readonly Dictionary<string, PathsD> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Lock CacheLock = new();

    /// <summary>The ornament's areas in its −1…1 box (cached; a copy is returned).</summary>
    public static PathsD Shape(string name)
    {
        if (Find(name) is not { } info) return [];
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(info.Name, out var s)) Cache[info.Name] = s = Fit(Geo.Tidy(info.Draw()));
            return [.. s.Select(p => new PathD(p))];
        }
    }

    /// <summary>Areas scaled so the bigger side of their box spans −1…1, centred.</summary>
    public static PathsD Fit(PathsD a)
    {
        if (a.Count == 0) return a;
        var b = Geo.Bounds(a);
        double w = b.right - b.left, h = b.bottom - b.top, k = 2 / Math.Max(1e-9, Math.Max(w, h));
        double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
        return Geo.Map(a, q => new PointD((q.x - cx) * k, (q.y - cy) * k));
    }

    /// <summary>
    /// The areas (in a −1…1 box) put at (<paramref name="cx"/>, <paramref name="cy"/>), as big as fits in
    /// <paramref name="w"/> × <paramref name="h"/>, turned <paramref name="deg"/> (anticlockwise) and,
    /// with <paramref name="mirror"/>, flipped left to right first.
    /// </summary>
    public static PathsD Place(PathsD unit, double cx, double cy, double w, double h, double deg = 0, bool mirror = false)
    {
        if (unit.Count == 0 || w <= 0 || h <= 0) return [];
        var b = Geo.Bounds(unit);
        double uw = b.right - b.left, uh = b.bottom - b.top;
        // Turned a quarter, its sides swap.
        bool quarter = Math.Abs(Math.Sin(deg * Math.PI / 180)) > 0.7;
        double k = Math.Min(w / Math.Max(1e-9, quarter ? uh : uw), h / Math.Max(1e-9, quarter ? uw : uh));
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r), mx = (b.left + b.right) / 2, my = (b.top + b.bottom) / 2;
        var o = new PathsD();
        foreach (var p in unit)
        {
            var q = new PathD(p.Count);
            foreach (var pt in p)
            {
                double x = (pt.x - mx) * (mirror ? -1 : 1) * k, y = (pt.y - my) * k;
                q.Add(new PointD(cx + x * c - y * s, cy + x * s + y * c));
            }
            if (mirror) q.Reverse();
            o.Add(q);
        }
        return o;
    }

    // ---- drawing helpers (a −1…1 box, y up) ----

    const double Tol = 0.002;

    static PathD P(params double[] xy)
    {
        var p = new PathD(xy.Length / 2);
        for (int i = 0; i + 1 < xy.Length; i += 2) p.Add(new PointD(xy[i], xy[i + 1]));
        return p;
    }

    static PathD Circle(double x, double y, double r) => Geo.Circle(x, y, r, Tol);

    static PathD Ellipse(double x, double y, double rx, double ry, double deg = 0)
    {
        int n = 96;
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n, ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            p.Add(new PointD(x + ex * c - ey * s, y + ex * s + ey * c));
        }
        return p;
    }

    /// <summary>A leaf: pointed at both ends, <paramref name="len"/> long from (x, y) at <paramref name="deg"/>.</summary>
    static PathD Leaf(double x, double y, double len, double width, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var p = new PathD();
        const int n = 24;
        for (int i = 0; i <= n; i++) { double t = (double)i / n, u = len * t, v = width / 2 * Math.Pow(Math.Sin(Math.PI * t), 0.85) * (1 - 0.25 * t); p.Add(new(x + u * c - v * s, y + u * s + v * c)); }
        for (int i = n - 1; i > 0; i--) { double t = (double)i / n, u = len * t, v = -width / 2 * Math.Pow(Math.Sin(Math.PI * t), 0.85) * (1 - 0.25 * t); p.Add(new(x + u * c - v * s, y + u * s + v * c)); }
        return p;
    }

    static PathD Star(int n, double r, double ri, double startDeg = 90, double cx = 0, double cy = 0)
    {
        var p = new PathD(2 * n);
        for (int i = 0; i < 2 * n; i++)
        {
            double a = (startDeg + i * 180.0 / n) * Math.PI / 180, rr = i % 2 == 0 ? r : ri;
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        return p;
    }

    static PointD Polar(double r, double deg, double cx = 0, double cy = 0) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));

    /// <summary>Solid areas joined (each loop counted as solid, whichever way round it runs).</summary>
    static PathsD U(params PathD[] p) => Geo.Union(new PathsD(p.Select(Solid)));
    static PathsD U(params PathsD[] p) => p.Aggregate(new PathsD(), (a, b) => a.Count == 0 ? Geo.Tidy(b) : Geo.Union(a, Geo.Tidy(b)));
    static PathsD Sub(PathsD a, params PathD[] b) => Geo.Subtract(a, Geo.Union(new PathsD(b.Select(Solid))));
    static PathsD Sub(PathsD a, PathsD b) => Geo.Subtract(a, Geo.Tidy(b));
    static PathsD And(PathsD a, PathsD b) => Geo.Intersect(a, b);

    static PathD Solid(PathD p)
    {
        if (Clipper.Area(p) >= 0) return p;
        var q = new PathD(p);
        q.Reverse();
        return q;
    }

    /// <summary>A line drawn with a pen <paramref name="w"/> wide.</summary>
    static PathsD Stroke(PathD line, double w, bool round = true, bool closed = false) =>
        Geo.Tidy(Clipper.InflatePaths([line], w / 2, round ? JoinType.Round : JoinType.Miter, closed ? EndType.Joined : round ? EndType.Round : EndType.Butt, 3, 4));

    static PathsD Strokes(IEnumerable<PathD> lines, double w, bool round = true) =>
        Geo.Tidy(Clipper.InflatePaths(new PathsD(lines), w / 2, round ? JoinType.Round : JoinType.Miter, round ? EndType.Round : EndType.Butt, 3, 4));

    static PathsD Ring(double x, double y, double r, double w) => Sub(U(Circle(x, y, r + w / 2)), Circle(x, y, r - w / 2));

    /// <summary>The areas and their mirror image (x → −x) joined.</summary>
    static PathsD Mirror(PathsD a)
    {
        var m = new PathsD(a.Select(p => { var q = new PathD(p.Select(v => new PointD(-v.x, v.y))); q.Reverse(); return q; }));
        return Geo.Union(Geo.Tidy(a), Geo.Tidy(m));
    }

    static PathsD Turn(PathsD a, double deg, double dx = 0, double dy = 0) => Geo.Turn(a, deg, dx, dy);
    static PathsD Scale(PathsD a, double k, double dx = 0, double dy = 0) => Geo.Map(a, q => new PointD(q.x * k + dx, q.y * k + dy));
    static PathsD Grow(PathsD a, double by) => Geo.Offset(a, by, true);

    /// <summary>Front over back, with a thin gap round the front so they read as two.</summary>
    static PathsD Over(PathsD back, PathsD front, double gap = 0.06) => Geo.Union(Sub(back, Grow(front, gap)), Geo.Tidy(front));

    /// <summary>A path built from lines and cubic curves.</summary>
    sealed class Pen
    {
        readonly PathD _p = [];
        PointD _at;
        public Pen(double x, double y) { _at = new(x, y); _p.Add(_at); }
        public Pen L(double x, double y) { _at = new(x, y); _p.Add(_at); return this; }
        public Pen C(double x1, double y1, double x2, double y2, double x, double y)
        {
            const int n = 24;
            var a = _at;
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                _p.Add(new PointD(u * u * u * a.x + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x, u * u * u * a.y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y));
            }
            _at = new(x, y);
            return this;
        }
        public PathD Done() => _p;
    }

    // ---- the ornaments ----

    /// <summary>A star with its points cut into facets: an outline, and one half of each point filled.</summary>
    static PathsD FacetStar(int n, double r, double ri, double startDeg, double w)
    {
        var outline = Star(n, r, ri, startDeg);
        var o = Stroke(outline, w, false, true);
        var facets = new PathsD();
        for (int i = 0; i < n; i++)
        {
            double tip = startDeg + i * 360.0 / n, inner = tip + 180.0 / n;
            facets.Add(Solid([new(0, 0), Polar(r, tip), Polar(ri, inner)]));
        }
        return Geo.Union(o, Geo.Intersect(Geo.Union(facets), [outline]));
    }

    static PathsD Diamond()
    {
        var outer = P(1, 0, 0, 0.72, -1, 0, 0, -0.72);
        var o = Sub(U(outer), P(0.8, 0, 0, 0.576, -0.8, 0, 0, -0.576));
        o = Geo.Union(o, U(P(0.55, 0, 0, 0.4, -0.55, 0, 0, -0.4)));
        return Sub(o, P(0.2, 0, 0, 0.144, -0.2, 0, 0, -0.144));
    }

    static PathsD Compass()
    {
        var big = FacetStar(4, 1, 0.2, 90, 0.06);
        var small = U(Star(4, 0.66, 0.2, 45));
        var o = Over(small, big, 0.06);
        o = Over(Ring(0, 0, 0.46, 0.07), o, 0.06);
        return Sub(o, Circle(0, 0, 0.06));
    }

    static PathsD Laurel()
    {
        // A sprig: a gently curved stem with leaves along it, each side, pointing toward the tip.
        var stem = new Pen(-0.95, -0.32).C(-0.4, -0.38, 0.35, -0.12, 0.98, 0.3).Done();
        var o = Stroke(stem, 0.06);
        for (int i = 0; i < 6; i++)
        {
            double t = 0.08 + i * 0.15;
            var (pt, deg) = At(stem, t);
            double len = 0.42 - i * 0.025, wd = 0.2 - i * 0.008;
            o = Geo.Union(o, U(Leaf(pt.x, pt.y, len, wd, deg + 42)));
            if (i < 5) o = Geo.Union(o, U(Leaf(pt.x + 0.07, pt.y - 0.02, len * 0.95, wd, deg - 40)));
        }
        return Geo.Union(o, U(Leaf(0.9, 0.24, 0.3, 0.16, 35)));
    }

    /// <summary>The point a share <paramref name="t"/> of the way along a line, and its direction (degrees).</summary>
    static (PointD P, double Deg) At(PathD line, double t)
    {
        double total = 0;
        for (int i = 1; i < line.Count; i++) total += Dist(line[i - 1], line[i]);
        double want = Math.Clamp(t, 0, 1) * total, run = 0;
        for (int i = 1; i < line.Count; i++)
        {
            double d = Dist(line[i - 1], line[i]);
            if (run + d >= want && d > 0)
            {
                double f = (want - run) / d;
                var a = line[i - 1];
                var b = line[i];
                return (new PointD(a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f), Math.Atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI);
            }
            run += d;
        }
        return (line[^1], 0);
    }

    static double Dist(PointD a, PointD b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    static PathsD Arrow()
    {
        var head = P(1, 0, 0.28, 0.5, 0.42, 0.08, 0.42, -0.08, 0.28, -0.5);
        var shaft = Geo.Rect(-0.78, -0.065, 0.45, 0.065);
        var top = P(-1, 0.42, -0.62, 0.42, -0.4, 0.065, -0.78, 0.065);
        var bottom = P(-1, -0.42, -0.78, -0.065, -0.4, -0.065, -0.62, -0.42);
        var o = U(head, shaft, top, bottom);
        // A nock line between the feathers.
        return Sub(o, Geo.Rect(-0.75, -0.012, -0.5, 0.012));
    }

    static PathsD Chevrons()
    {
        var o = new PathsD();
        foreach (var (x, k) in new[] { (-0.62, 0.7), (-0.05, 0.85), (0.55, 1.0) })
            o = Geo.Union(o, Stroke(P(x - 0.3 * k, 0.62 * k, x + 0.22 * k, 0, x - 0.3 * k, -0.62 * k), 0.22 * k, false));
        return o;
    }

    static PathsD Boots()
    {
        PathsD Print(double x, double y, double deg, bool left)
        {
            double s = left ? -1 : 1;
            var sole = U(Ellipse(0.03 * s, 0.3, 0.27, 0.36, 4 * s), Ellipse(0, -0.02, 0.19, 0.3));
            var heel = U(Ellipse(0, -0.56, 0.2, 0.19));
            var o = Geo.Union(sole, heel);
            // Tread across the sole and heel.
            for (int i = 0; i < 4; i++) o = Sub(o, Geo.Rect(-0.4, 0.08 + i * 0.13, 0.4, 0.115 + i * 0.13));
            o = Sub(o, Geo.Rect(-0.3, -0.58, 0.3, -0.545));
            return Turn(Scale(o, 0.78), deg, x, y);
        }
        return Geo.Union(Print(-0.36, -0.22, 10, true), Print(0.36, 0.3, -10, false));
    }

    static PathsD Bullseye() => Geo.Union(Geo.Union(Ring(0, 0, 0.88, 0.24), Ring(0, 0, 0.48, 0.22)), U(Circle(0, 0, 0.16)));

    static PathsD Sword(double deg)
    {
        var blade = U(P(0, 1, 0.12, 0.8, 0.12, -0.32, -0.12, -0.32, -0.12, 0.8));
        blade = Sub(blade, Geo.Strip(new(0, -0.24), new(0, 0.72), 0.04));
        var guard = U(new Pen(-0.5, -0.33).C(-0.2, -0.38, 0.2, -0.38, 0.5, -0.33).L(0.5, -0.44).C(0.2, -0.49, -0.2, -0.49, -0.5, -0.44).Done(),
            Circle(-0.5, -0.385, 0.075), Circle(0.5, -0.385, 0.075));
        var grip = U(Geo.Rect(-0.07, -0.82, 0.07, -0.44));
        for (int i = 0; i < 3; i++) grip = Sub(grip, Geo.Strip(new(-0.08, -0.74 + i * 0.1), new(0.08, -0.69 + i * 0.1), 0.025));
        var o = U(blade, guard, grip, U(Circle(0, -0.9, 0.11)));
        return deg == 0 ? o : Turn(o, deg);
    }

    static PathsD CrossedSwords()
    {
        var back = Turn(Sword(0), -40);
        var front = Turn(Sword(0), 40);
        return Over(back, front, 0.06);
    }

    static PathsD Shield()
    {
        var outline = new Pen(-0.78, 0.9).C(-0.3, 0.8, 0.3, 0.8, 0.78, 0.9).L(0.78, 0.25).C(0.78, -0.35, 0.42, -0.75, 0, -1).C(-0.42, -0.75, -0.78, -0.35, -0.78, 0.25).Done();
        var s = U(outline);
        var rim = Sub(s, Geo.Offset(s, -0.1));
        var inner = Geo.Offset(s, -0.19);
        // A cross on it, left as wood between the four filled quarters.
        var quarters = Sub(inner, Geo.Rect(-0.08, -1.1, 0.08, 1), Geo.Rect(-1, 0.22, 1, 0.38));
        return Geo.Union(rim, quarters);
    }

    static PathsD Crown()
    {
        var band = Sub(U(Geo.Rect(-0.82, -0.72, 0.82, -0.44)), Circle(-0.45, -0.58, 0.07), P(0, -0.66, 0.08, -0.58, 0, -0.5, -0.08, -0.58), Circle(0.45, -0.58, 0.07));
        var body = U(P(-0.82, -0.38, -0.92, 0.45, -0.46, 0.02, 0, 0.62, 0.46, 0.02, 0.92, 0.45, 0.82, -0.38));
        body = Sub(body, P(-0.62, -0.28, -0.66, 0.08, -0.4, -0.18), P(0.62, -0.28, 0.66, 0.08, 0.4, -0.18), P(-0.16, -0.26, 0, 0.3, 0.16, -0.26));
        return U(band, body, U(Circle(-0.92, 0.55, 0.12), Circle(0, 0.74, 0.13), Circle(0.92, 0.55, 0.12)));
    }

    static PathsD Skull()
    {
        var head = U(Ellipse(0, 0.22, 0.74, 0.7), new Pen(-0.5, -0.1).C(-0.52, -0.4, -0.42, -0.62, -0.3, -0.7).L(0.3, -0.7).C(0.42, -0.62, 0.52, -0.4, 0.5, -0.1).Done());
        // Cheekbones in, below the eyes.
        head = Sub(head, Ellipse(-0.72, -0.42, 0.24, 0.3), Ellipse(0.72, -0.42, 0.24, 0.3));
        head = Sub(head, Ellipse(-0.3, 0.08, 0.21, 0.19, -12), Ellipse(0.3, 0.08, 0.21, 0.19, 12));
        head = Sub(head, new Pen(0, -0.1).C(-0.05, -0.18, -0.12, -0.3, -0.04, -0.33).L(0.04, -0.33).C(0.12, -0.3, 0.05, -0.18, 0, -0.1).Done());
        // Teeth.
        head = Sub(head, Geo.Rect(-0.3, -0.5, 0.3, -0.465));
        foreach (var x in new[] { -0.2, -0.067, 0.067, 0.2 }) head = Sub(head, Geo.Rect(x - 0.018, -0.68, x + 0.018, -0.42));
        return head;
    }

    static PathsD Key()
    {
        var bow = Sub(U(Circle(-0.66, 0.2, 0.25), Circle(-0.66, -0.2, 0.25), Circle(-0.86, 0, 0.25), Circle(-0.62, 0, 0.27)), Circle(-0.7, 0, 0.11));
        var shaft = U(Geo.Rect(-0.44, -0.065, 0.98, 0.065), Geo.Rect(-0.4, -0.13, -0.32, 0.13));
        var bit = Sub(U(Geo.Rect(0.56, -0.42, 0.98, -0.03)), Geo.Rect(0.68, -0.43, 0.76, -0.2), Geo.Rect(0.85, -0.43, 0.9, -0.3));
        return U(bow, shaft, bit);
    }

    static PathsD D20()
    {
        var hex = Enumerable.Range(0, 6).Select(i => Polar(1, 90 + 60 * i)).ToArray();
        var tri = new[] { Polar(0.52, 90), Polar(0.52, 210), Polar(0.52, 330) };
        var lines = new List<PathD> { new(hex) { hex[0] }, new(tri) { tri[0] } };
        // Each corner of the front face to the three nearest corners of the outline.
        int[][] to = [[5, 0, 1], [1, 2, 3], [3, 4, 5]];
        for (int i = 0; i < 3; i++) foreach (var j in to[i]) lines.Add([tri[i], hex[j]]);
        var o = Strokes(lines, 0.075);
        // The front face filled.
        return Geo.Union(o, U(new PathD(tri)));
    }

    static PathsD Flame()
    {
        var outer = new Pen(0, -1).C(0.62, -1, 0.8, -0.42, 0.56, 0.04).C(0.48, 0.26, 0.54, 0.5, 0.42, 0.72).C(0.3, 0.52, 0.22, 0.42, 0.14, 0.5)
            .C(0.16, 0.72, 0.1, 0.88, -0.04, 1).C(-0.16, 0.72, -0.4, 0.58, -0.42, 0.3).C(-0.44, 0.44, -0.52, 0.52, -0.62, 0.56)
            .C(-0.6, 0.3, -0.82, 0.06, -0.72, -0.36).C(-0.64, -0.78, -0.36, -1, 0, -1).Done();
        var inner = new Pen(0, -0.82).C(0.4, -0.82, 0.52, -0.42, 0.34, -0.08).C(0.26, 0.1, 0.24, 0.24, 0.12, 0.4).C(0.06, 0.2, -0.04, 0.12, -0.12, 0.2)
            .C(-0.16, 0.02, -0.4, -0.06, -0.44, -0.3).C(-0.46, -0.6, -0.3, -0.82, 0, -0.82).Done();
        var core = new Pen(0, -0.7).C(0.24, -0.7, 0.3, -0.44, 0.16, -0.22).C(0.1, -0.12, 0.06, -0.04, 0.02, 0.04).C(-0.06, -0.08, -0.22, -0.2, -0.24, -0.38).C(-0.24, -0.58, -0.14, -0.7, 0, -0.7).Done();
        return Geo.Union(Sub(U(outer), inner), U(core));
    }

    static PathsD Crystals()
    {
        PathsD Crystal(double x, double y, double w, double h, double deg)
        {
            var left = U(P(-w, 0, -w, h, 0, h + w * 1.5, 0, 0));
            var right = Sub(U(P(0, 0, 0, h + w * 1.5, w, h, w, 0)), Geo.Offset(U(P(0, 0, 0, h + w * 1.5, w, h, w, 0)), -0.05));
            var c = Geo.Union(left, right);
            return Turn(c, deg, x, y);
        }
        var back = Geo.Union(Crystal(-0.32, -0.72, 0.16, 0.7, 28), Crystal(0.34, -0.72, 0.15, 0.58, -30));
        var o = Over(back, Crystal(0, -0.78, 0.21, 1.15, 0), 0.06);
        var ground = U(Ellipse(-0.3, -0.84, 0.42, 0.13), Ellipse(0.32, -0.86, 0.38, 0.11), Ellipse(0, -0.8, 0.3, 0.12));
        return Over(o, ground, 0.05);
    }

    static PathsD Bat()
    {
        var wing = U(new Pen(0.08, 0.18).C(0.34, 0.42, 0.66, 0.52, 1, 0.5).C(0.92, 0.3, 0.9, 0.12, 0.94, -0.06)
            .C(0.84, 0.12, 0.7, 0.12, 0.6, -0.1).C(0.5, 0.08, 0.38, 0.06, 0.3, -0.2).C(0.22, -0.04, 0.14, -0.04, 0.06, -0.2).Done());
        var body = U(Ellipse(0, -0.02, 0.14, 0.27), Circle(0, 0.3, 0.13), P(-0.12, 0.34, -0.12, 0.56, -0.03, 0.4), P(0.12, 0.34, 0.12, 0.56, 0.03, 0.4));
        var o = Geo.Union(Mirror(wing), body);
        // Bones in the wing, picked out.
        foreach (var (a, b) in new[] { ((0.16, 0.16), (0.9, 0.04)), ((0.16, 0.12), (0.6, -0.02)), ((0.14, 0.08), (0.32, -0.1)) })
        {
            o = Sub(o, Geo.Strip(new(a.Item1, a.Item2), new(b.Item1, b.Item2), 0.025));
            o = Sub(o, Geo.Strip(new(-a.Item1, a.Item2), new(-b.Item1, b.Item2), 0.025));
        }
        return Sub(o, Circle(-0.05, 0.32, 0.025), Circle(0.05, 0.32, 0.025));
    }

    static PathsD Paw()
    {
        var pad = U(Circle(-0.24, -0.44, 0.26), Circle(0.24, -0.44, 0.26), Circle(0, -0.26, 0.34), Circle(0, -0.5, 0.26));
        var toes = U(Ellipse(-0.66, 0.08, 0.17, 0.23, 25), Ellipse(-0.25, 0.5, 0.18, 0.25, 8), Ellipse(0.25, 0.5, 0.18, 0.25, -8), Ellipse(0.66, 0.08, 0.17, 0.23, -25));
        return Geo.Union(pad, toes);
    }

    static PathsD Reticle()
    {
        var o = Geo.Union(Ring(0, 0, 0.64, 0.09), Ring(0, 0, 0.3, 0.05));
        foreach (var d in new[] { 0, 90, 180, 270 }) o = Geo.Union(o, Stroke([Polar(0.4, d), Polar(1, d)], 0.08, false));
        foreach (var d in new[] { 45, 135, 225, 315 }) o = Geo.Union(o, Stroke([Polar(0.6, d), Polar(0.8, d)], 0.05, false));
        return Geo.Union(o, U(Circle(0, 0, 0.07)));
    }

    static PathsD Gear()
    {
        var o = U(Circle(0, 0, 0.72));
        for (int i = 0; i < 10; i++)
        {
            double a = 18 + i * 36;
            o = Geo.Union(o, U(new PathD { Polar(0.66, a - 11), Polar(1, a - 6.5), Polar(1, a + 6.5), Polar(0.66, a + 11) }));
        }
        o = Sub(o, Circle(0, 0, 0.52));
        var hub = U(Circle(0, 0, 0.25));
        for (int i = 0; i < 5; i++) hub = Geo.Union(hub, U(Geo.Strip(new(0, 0), Polar(0.6, 90 + i * 72), 0.13)));
        return Sub(Geo.Union(o, hub), Circle(0, 0, 0.1));
    }

    static PathsD Hazard()
    {
        var o = U(Circle(0, 0, 0.15));
        foreach (var c in new[] { 90.0, 210, 330 })
        {
            var blade = new PathD();
            blade.AddRange(Geo.Arc(0, 0, 0.96, (c - 30) * Math.PI / 180, (c + 30) * Math.PI / 180, Tol));
            blade.AddRange(Geo.Arc(0, 0, 0.27, (c + 30) * Math.PI / 180, (c - 30) * Math.PI / 180, Tol));
            o = Geo.Union(o, U(blade));
        }
        return o;
    }

    static PathsD Lightning() => U(P(0.22, 1, -0.56, -0.1, -0.06, -0.1, -0.3, -1, 0.58, 0.2, 0.07, 0.2));

    static PathsD Sun()
    {
        var o = U(Circle(0, 0, 0.38));
        o = Sub(o, Circle(0, 0, 0.28));
        o = Geo.Union(o, U(Circle(0, 0, 0.2)));
        for (int i = 0; i < 16; i++)
        {
            double a = 90 + i * 22.5, r = i % 2 == 0 ? 1 : 0.74, half = i % 2 == 0 ? 8 : 6;
            o = Geo.Union(o, U(new PathD { Polar(0.47, a - half), Polar(r, a), Polar(0.47, a + half) }));
        }
        return o;
    }

    static PathsD Pyramid()
    {
        PointD apex = new(0, 0.62), l = new(-1, -0.72), m = new(0.28, -0.72), r = new(0.86, -0.72);
        var lit = U(new PathD { apex, l, m });
        var shade = new PathsD();
        for (int i = 0; i < 8; i++) shade.Add(Geo.Rect(-1, -0.72 + i * 0.17, 1, -0.72 + i * 0.17 + 0.085));
        var dark = And(U(new PathD { apex, m, r }), Geo.Union(shade));
        var edge = Stroke(new PathD { apex, m }, 0.04);
        return U(lit, dark, edge, Ring(0.72, 0.62, 0.16, 0.05), U(Circle(0.72, 0.62, 0.08)), U(Geo.Rect(-1, -0.86, 1, -0.79)));
    }

    static PathsD Moon()
    {
        var crescent = Sub(U(Circle(-0.1, 0, 0.9)), Circle(0.3, 0.18, 0.74));
        return Geo.Union(crescent, FacetStar(5, 0.3, 0.13, 90, 0.035).Select(p => new PathD(p.Select(q => new PointD(q.x + 0.42, q.y + 0.08)))).ToPathsD());
    }

    static PathsD GothicCross()
    {
        double cy = 0.3;
        var bars = U(Geo.Rect(-0.1, -0.78, 0.1, 0.88), Geo.Rect(-0.78, cy - 0.1, 0.78, cy + 0.1), P(-0.24, -1, 0.24, -1, 0.1, -0.74, -0.1, -0.74));
        PathsD Trefoil(double x, double y, double deg) => Turn(U(Circle(0.12, 0, 0.12), Circle(0, 0.11, 0.1), Circle(0, -0.11, 0.1)), deg, x, y);
        var ends = U(Trefoil(0, 0.86, 90), Trefoil(-0.78, cy, 180), Trefoil(0.78, cy, 0));
        var ring = Ring(0, cy, 0.38, 0.08);
        var o = U(bars, ends);
        o = Over(ring, o, 0.05);
        return Sub(o, P(0, cy - 0.06, 0.06, cy, 0, cy + 0.06, -0.06, cy));
    }

    static PathsD Quatrefoil()
    {
        var lobes = U(Circle(0.44, 0, 0.5), Circle(-0.44, 0, 0.5), Circle(0, 0.44, 0.5), Circle(0, -0.44, 0.5));
        var o = Sub(lobes, Geo.Offset(lobes, -0.1));
        return Geo.Union(o, FacetStar(4, 0.5, 0.17, 45, 0.04));
    }

    static PathsD Burst()
    {
        double[] rr = [1, 0.82, 0.96, 0.78, 1, 0.86, 0.92, 0.8, 0.98, 0.84, 0.94, 0.8];
        PathD Spikes(double k, double inner)
        {
            var p = new PathD();
            for (int i = 0; i < 24; i++) p.Add(Polar((i % 2 == 0 ? rr[i / 2] : inner) * k, 90 + i * 15));
            return p;
        }
        var o = Sub(U(Spikes(1, 0.52)), Spikes(0.74, 0.4));
        o = Geo.Union(o, U(Spikes(0.5, 0.28)));
        return Sub(o, Circle(0, 0, 0.12));
    }

    static PathsD FleurDeLis()
    {
        // The middle petal, pointed at the top.
        var mid = new Pen(0, 1).C(0.12, 0.82, 0.3, 0.62, 0.3, 0.38).C(0.3, 0.18, 0.16, 0.04, 0.1, -0.08).L(-0.1, -0.08)
            .C(-0.16, 0.04, -0.3, 0.18, -0.3, 0.38).C(-0.3, 0.62, -0.12, 0.82, 0, 1).Done();
        // The right petal: out from the band, up, and curling over and down at its tip.
        var side = new Pen(0.1, -0.08).C(0.18, 0.16, 0.4, 0.26, 0.6, 0.28).C(0.86, 0.3, 1, 0.5, 0.9, 0.7)
            .C(0.82, 0.84, 0.62, 0.82, 0.58, 0.68).C(0.56, 0.6, 0.62, 0.54, 0.7, 0.56).C(0.74, 0.46, 0.66, 0.38, 0.5, 0.34)
            .C(0.34, 0.3, 0.22, 0.14, 0.18, -0.08).Done();
        // Under the band: a small petal each side curling out, and a point in the middle.
        var foot = new Pen(0.06, -0.24).C(0.22, -0.26, 0.42, -0.34, 0.5, -0.5).C(0.56, -0.64, 0.46, -0.78, 0.34, -0.72)
            .C(0.3, -0.7, 0.3, -0.62, 0.36, -0.6).C(0.34, -0.5, 0.2, -0.46, 0.06, -0.44).Done();
        var tail = P(-0.1, -0.26, 0.1, -0.26, 0, -0.62);
        var band = U(Geo.Rect(-0.46, -0.24, 0.46, -0.08));
        band = Geo.Union(band, U(Circle(-0.46, -0.16, 0.08), Circle(0.46, -0.16, 0.08)));
        var o = Geo.Union(Mirror(U(side)), Mirror(U(foot)));
        o = Geo.Union(o, U(mid, tail));
        o = Over(o, band, 0.05);
        // A line down the middle petal.
        return Sub(o, Geo.Strip(new(0, 0.0), new(0, 0.7), 0.035));
    }
}

static class PathsExtensions
{
    public static PathsD ToPathsD(this IEnumerable<PathD> p) => new(p);
}
