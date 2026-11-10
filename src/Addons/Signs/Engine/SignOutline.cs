using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// A sign's outline: straight lines and circular arcs, clockwise on screen (y down), all convex. It
/// answers how far a point is in from the edge, and where along the edge the nearest point is, so the
/// border's bands follow the edge and its patterns run along it. Inside a convex outline the nearest
/// edge point of two straight sides changes over on the corner's diagonal, so square corners come out
/// mitred. The edge is cut into runs at sharp corners; each run has its own whole number of repeats.
/// A run that goes all the way round (no sharp corners) starts at the top in the middle.
/// </summary>
public sealed class SignOutline
{
    /// <summary>One piece of the edge: a line from <see cref="A"/> to <see cref="B"/>, or an arc round
    /// <see cref="C"/> from angle <see cref="A0"/> turning <see cref="Sweep"/> (radians, clockwise on screen).</summary>
    sealed record Piece(bool Arc, Vec2 A, Vec2 B, Vec2 C, double R, double A0, double Sweep, int Run, double S0)
    {
        public double Length => Arc ? R * Sweep : A.Dist(B);
    }

    /// <summary>A stretch of the edge between two sharp corners, or all the way round.</summary>
    public sealed record Run(string Name, double Length, bool Loop);

    readonly Piece[] _pieces;
    readonly Vec2[] _poly;

    public double W { get; }
    public double H { get; }
    public IReadOnlyList<Run> Runs { get; }
    /// <summary>The outline as a closed polygon (arcs in steps of about 2°), clockwise.</summary>
    public IReadOnlyList<Vec2> Polygon => _poly;

    SignOutline(double w, double h, List<(Piece P, string RunName)> pieces, bool loop)
    {
        W = w;
        H = h;
        // Number the runs in order and measure along each.
        var runs = new List<Run>();
        var list = new List<Piece>();
        string? current = null;
        double s = 0;
        int run = -1;
        foreach (var (p, name) in pieces)
        {
            if (p.Length < 1e-9) continue;
            if (name != current)
            {
                if (run >= 0) runs[run] = runs[run] with { Length = s };
                runs.Add(new Run(name, 0, loop));
                run++;
                current = name;
                s = 0;
            }
            list.Add(p with { Run = run, S0 = s });
            s += p.Length;
        }
        if (run >= 0) runs[run] = runs[run] with { Length = s };
        Runs = runs;
        _pieces = [.. list];
        var poly = new List<Vec2>();
        foreach (var p in _pieces)
        {
            if (!p.Arc) { poly.Add(p.A); continue; }
            int n = Math.Max(2, (int)Math.Ceiling(p.Sweep / (2 * Math.PI / 180)));
            for (int i = 0; i < n; i++) poly.Add(At(p.C, p.R, p.A0 + p.Sweep * i / n));
        }
        _poly = [.. poly];
    }

    static Vec2 At(Vec2 c, double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));

    static (Piece, string) L(Vec2 a, Vec2 b, string run) => (new Piece(false, a, b, default, 0, 0, 0, 0, 0), run);

    static (Piece, string) A(Vec2 c, double r, double fromDeg, double toDeg, string run)
    {
        double a0 = fromDeg * Math.PI / 180, sweep = (toDeg - fromDeg) * Math.PI / 180;
        while (sweep < 0) sweep += 2 * Math.PI;
        return (new Piece(true, At(c, r, a0), At(c, r, a0 + sweep), c, r, a0, sweep, 0, 0), run);
    }

    /// <summary>The outline for <paramref name="spec"/>'s shape and size.</summary>
    public static SignOutline For(SignSpec spec)
    {
        var (w, h) = spec.Size();
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        double c = Math.Max(0, spec.CornerMm);
        switch (spec.Shape)
        {
            case SignShape.Rectangle:
                return Rect(w, h);
            case SignShape.CutCorners:
                {
                    c = Math.Min(c, Math.Min(w, h) / 2 - 0.5);
                    if (c < 0.5) return Rect(w, h);
                    return new(w, h,
                    [
                        L(new(c, 0), new(w - c, 0), "Top"), L(new(w - c, 0), new(w, c), "Top right corner"),
                        L(new(w, c), new(w, h - c), "Right"), L(new(w, h - c), new(w - c, h), "Bottom right corner"),
                        L(new(w - c, h), new(c, h), "Bottom"), L(new(c, h), new(0, h - c), "Bottom left corner"),
                        L(new(0, h - c), new(0, c), "Left"), L(new(0, c), new(c, 0), "Top left corner"),
                    ], false);
                }
            case SignShape.Rounded:
                return RoundRect(w, h, Math.Min(c, Math.Min(w, h) / 2));
            case SignShape.RoundEnds:
                return RoundRect(w, h, Math.Min(w, h) / 2);
            case SignShape.Circle:
            case SignShape.Oval:
                return Oval(w, h);
            case SignShape.Arched:
                return Arched(w, h, spec.ArchRiseMm, c);
            default:
                return Rect(w, h);
        }
    }

    static SignOutline Rect(double w, double h) => new(w, h,
    [
        L(new(0, 0), new(w, 0), "Top"), L(new(w, 0), new(w, h), "Right"),
        L(new(w, h), new(0, h), "Bottom"), L(new(0, h), new(0, 0), "Left"),
    ], false);

    static SignOutline RoundRect(double w, double h, double r)
    {
        if (r < 0.25) return Rect(w, h);
        const string All = "All round";
        return new(w, h,
        [
            L(new(w / 2, 0), new(w - r, 0), All), A(new(w - r, r), r, -90, 0, All),
            L(new(w, r), new(w, h - r), All), A(new(w - r, h - r), r, 0, 90, All),
            L(new(w - r, h), new(r, h), All), A(new(r, h - r), r, 90, 180, All),
            L(new(0, h - r), new(0, r), All), A(new(r, r), r, 180, 270, All),
            L(new(r, 0), new(w / 2, 0), All),
        ], true);
    }

    /// <summary>
    /// An oval of four arcs, as a sign maker sets one out: two flatter arcs along the long sides and
    /// two tighter ones at the ends, meeting smoothly. Equal width and height make a circle.
    /// </summary>
    static SignOutline Oval(double w, double h)
    {
        double a = w / 2, b = h / 2;
        const string All = "All round";
        if (Math.Abs(a - b) < 0.25)
        {
            double r = Math.Min(a, b);
            return new(2 * r, 2 * r, [A(new(r, r), r, -90, 270, All)], true);
        }
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        // The end arcs' radius: half way between the true ellipse's end curvature and its half width.
        double r1 = 0.5 * (lo * lo / hi + lo);
        double r2 = ((hi - r1) * (hi - r1) + lo * lo - r1 * r1) / (2 * (lo - r1));
        bool wide = a >= b;
        double rt = wide ? r2 : r1, rs = wide ? r1 : r2;   // top/bottom arcs, side arcs
        Vec2 ct = new(a, rt), cr = new(w - rs, b), cb = new(a, h - rt), cl = new(rs, b);
        // Where the arcs meet: on the line from the big arc's centre through the small one's.
        double phi = wide ? Math.Atan2(b - rt, a - rs) : Math.Atan2(rt - b, rs - a);
        double p = phi * 180 / Math.PI;
        return new(w, h,
        [
            A(ct, rt, -90, p, All), A(cr, rs, p, -p, All), A(cb, rt, -p, 180 + p, All),
            A(cl, rs, 180 + p, 180 - p, All), A(ct, rt, 180 - p, 270, All),
        ], true);
    }

    /// <summary>
    /// Straight sides and bottom; over the top an arch with rounded shoulders of radius
    /// <paramref name="shoulder"/>, rising <paramref name="rise"/> above where the sides start.
    /// </summary>
    static SignOutline Arched(double w, double h, double rise, double shoulder)
    {
        double r = Math.Clamp(shoulder, 1, w / 4);
        double hh = Math.Clamp(rise, r + 1, Math.Min(w / 2, h - 5));
        if (hh <= r + 0.5) return RoundRect(w, h, Math.Min(r, h / 2));
        // The arch's radius, touching both shoulders and the top.
        double big = ((w / 2 - r) * (w / 2 - r) + hh * hh - r * r) / (2 * (hh - r));
        Vec2 cBig = new(w / 2, big), cLeft = new(r, hh), cRight = new(w - r, hh);
        double alpha = Math.Atan2(cLeft.Y - cBig.Y, cLeft.X - cBig.X) * 180 / Math.PI;   // pointing up and left
        double beta = -180 - alpha;                                                       // its mirror
        const string Top = "Top and sides";
        return new(w, h,
        [
            L(new(0, h), new(0, hh), Top), A(cLeft, r, 180, alpha, Top), A(cBig, big, alpha, beta, Top),
            A(cRight, r, beta, 0, Top), L(new(w, hh), new(w, h), Top),
            L(new(w, h), new(0, h), "Bottom"),
        ], false);
    }

    // ---- distances ----

    /// <summary>
    /// How far (<paramref name="x"/>, <paramref name="y"/>) is in from the edge (mm; negative outside),
    /// which run its nearest edge point is on, and how far along that run.
    /// </summary>
    public double Inset(double x, double y, out int run, out double along)
    {
        double best = double.MaxValue;
        bool inside = false;
        run = 0;
        along = 0;
        foreach (var p in _pieces)
        {
            double d, s;
            bool inner;
            if (!p.Arc)
            {
                double dx = p.B.X - p.A.X, dy = p.B.Y - p.A.Y, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Clamp(((x - p.A.X) * dx + (y - p.A.Y) * dy) / l2, 0, 1);
                double ex = p.A.X + dx * t - x, ey = p.A.Y + dy * t - y;
                d = Math.Sqrt(ex * ex + ey * ey);
                s = t * Math.Sqrt(l2);
                // Clockwise on screen (y down): the inside is on the right of the way along.
                inner = dx * (y - p.A.Y) - dy * (x - p.A.X) > 0;
            }
            else
            {
                double vx = x - p.C.X, vy = y - p.C.Y, rho = Math.Sqrt(vx * vx + vy * vy);
                double rel = Math.Atan2(vy, vx) - p.A0;
                rel -= 2 * Math.PI * Math.Floor(rel / (2 * Math.PI));
                if (rel <= p.Sweep) { d = Math.Abs(p.R - rho); s = p.R * rel; }
                else
                {
                    double da = p.A.Dist(new Vec2(x, y)), db = p.B.Dist(new Vec2(x, y));
                    if (da <= db) { d = da; s = 0; } else { d = db; s = p.R * p.Sweep; }
                }
                inner = rho < p.R;
            }
            // The outline is convex, so the nearest piece's side says whether the point is inside.
            if (d < best) { best = d; run = p.Run; along = p.S0 + s; inside = inner; }
        }
        return inside ? best : -best;
    }

    /// <summary>How far in from the edge a point is (mm; negative outside).</summary>
    public double Inset(double x, double y) => Inset(x, y, out _, out _);

    /// <summary>Is the point inside the outline.</summary>
    public bool Inside(double x, double y) => Inset(x, y) > 0;

    /// <summary>
    /// The first point from <paramref name="from"/> toward <paramref name="to"/> at least
    /// <paramref name="inset"/> mm in from the edge (null if there's none).
    /// </summary>
    public Vec2? FirstInset(Vec2 from, Vec2 to, double inset)
    {
        double len = from.Dist(to);
        int steps = Math.Max(10, (int)Math.Ceiling(len / 0.1));
        for (int i = 0; i <= steps; i++)
        {
            var p = from + (to - from) * (i / (double)steps);
            if (Inset(p.X, p.Y) >= inset) return p;
        }
        return null;
    }
}
