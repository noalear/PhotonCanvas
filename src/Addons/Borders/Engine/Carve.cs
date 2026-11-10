namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// Small pieces the ornaments are drawn with: signed distances to shapes (negative inside, in mm)
/// and the carved profiles made from them (0 = carved down to the band's floor, 1 = its surface).
/// </summary>
static class Carve
{
    public static double Sat(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    public static double Smooth(double e0, double e1, double x)
    {
        double t = Sat((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>x wrapped into [-0.5, 0.5): the position within a repeat, centred on the motif.</summary>
    public static double Centred(double u) => u - Math.Floor(u + 0.5);

    /// <summary>x wrapped into [0, 1).</summary>
    public static double Frac(double u) => u - Math.Floor(u);

    /// <summary>
    /// A raised shape with rounded-over edges: 0 outside, rising on a quarter circle of radius
    /// <paramref name="r"/> (mm) inside the edge, level beyond. A large radius makes a dome.
    /// </summary>
    public static double Pillow(double d, double r)
    {
        if (d >= 0) return 0;
        double e = -d / Math.Max(1e-6, r);
        if (e >= 1) return 1;
        double k = 1 - e;
        return Math.Sqrt(1 - k * k);
    }

    /// <summary>A soft-edged mask: 1 inside, 0 outside, blended across <paramref name="soft"/> mm at the edge.</summary>
    public static double Mask(double d, double soft) => Sat(0.5 - d / Math.Max(1e-6, soft));

    /// <summary>A rounded ridge along a line <paramref name="d"/> mm away, <paramref name="half"/> mm either side: 1 on the line.</summary>
    public static double Ridge(double d, double half)
    {
        double k = d / Math.Max(1e-6, half);
        return k * k >= 1 ? 0 : Math.Sqrt(1 - k * k);
    }

    /// <summary>A round-bottomed groove <paramref name="half"/> mm either side of a line: 0 on the line, 1 away from it.</summary>
    public static double Groove(double d, double half) => 1 - Ridge(d, half);

    /// <summary>A V groove: 0 on the line, 1 at <paramref name="half"/> mm away.</summary>
    public static double VGroove(double d, double half) => Sat(Math.Abs(d) / Math.Max(1e-6, half));

    // ---- distances ----

    public static double Circle(double x, double y, double r) => Math.Sqrt(x * x + y * y) - r;

    /// <summary>An ellipse's signed distance, close enough near the edge (Quílez's estimate).</summary>
    public static double Ellipse(double x, double y, double a, double b)
    {
        a = Math.Max(a, 1e-6);
        b = Math.Max(b, 1e-6);
        double k0 = Math.Sqrt(x * x / (a * a) + y * y / (b * b));
        double k1 = Math.Sqrt(x * x / (a * a * a * a) + y * y / (b * b * b * b));
        if (k1 < 1e-12) return -Math.Min(a, b);
        return k0 * (k0 - 1) / k1;
    }

    /// <summary>An egg, its broad end up (toward smaller y) and narrower end down; <paramref name="taper"/> 0 = an ellipse.</summary>
    public static double Egg(double x, double y, double a, double b, double taper)
    {
        // Narrow the lower half: the half-width shrinks by up to taper toward the bottom.
        double t = Sat(y / Math.Max(1e-6, b));
        double k = 1 - taper * t * t;
        return Ellipse(x / k, y, a, b) * Math.Min(1, k + 0.15);
    }

    public static double Box(double x, double y, double hw, double hh)
    {
        double dx = Math.Abs(x) - hw, dy = Math.Abs(y) - hh;
        double ox = Math.Max(dx, 0), oy = Math.Max(dy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0);
    }

    public static double RoundBox(double x, double y, double hw, double hh, double r)
    {
        r = Math.Min(r, Math.Min(hw, hh));
        return Box(x, y, hw - r, hh - r) - r;
    }

    /// <summary>Distance from (x, y) to the segment a–b.</summary>
    public static double Segment(double x, double y, double ax, double ay, double bx, double by)
    {
        double px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
        double l = dx * dx + dy * dy;
        double h = l < 1e-12 ? 0 : Sat((px * dx + py * dy) / l);
        double ex = px - dx * h, ey = py - dy * h;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>Distance to a polyline given as x0, y0, x1, y1, …</summary>
    public static double Polyline(double x, double y, ReadOnlySpan<double> pts)
    {
        double best = double.MaxValue;
        for (int i = 2; i + 1 < pts.Length; i += 2)
            best = Math.Min(best, Segment(x, y, pts[i - 2], pts[i - 1], pts[i], pts[i + 1]));
        return best;
    }

    /// <summary>A closed polygon's signed distance (negative inside), points as x0, y0, x1, y1, …</summary>
    public static double Polygon(double x, double y, ReadOnlySpan<double> pts)
    {
        int n = pts.Length / 2;
        double d = double.MaxValue;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = pts[2 * i], yi = pts[2 * i + 1], xj = pts[2 * j], yj = pts[2 * j + 1];
            d = Math.Min(d, Segment(x, y, xj, yj, xi, yi));
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside ? -d : d;
    }

    /// <summary>A pointed leaf (vesica) from (0, y0) to (0, y1), <paramref name="half"/> wide at its widest.</summary>
    public static double Leaf(double x, double y, double y0, double y1, double half)
    {
        // Two circular arcs through both tips: the circle's centre sits off to the side.
        double len = Math.Abs(y1 - y0) / 2, my = (y0 + y1) / 2;
        half = Math.Max(1e-6, Math.Min(half, len * 0.999));
        double r = (len * len + half * half) / (2 * half), c = r - half;
        double py = y - my, px = Math.Abs(x);
        // Inside both circles centred at (−c, 0) and (c, 0)... the lens is the intersection of circles centred at (−c,0)/(+c,0) of radius r.
        double dIn = Math.Sqrt((px + c) * (px + c) + py * py) - r;
        // Near the tips the distance is to the tip point.
        if (Math.Abs(py) > len) return Math.Sqrt(px * px + (Math.Abs(py) - len) * (Math.Abs(py) - len));
        return dIn;
    }

    /// <summary>A heart, point down at (0, <paramref name="h"/>/2), lobes up, about <paramref name="w"/> wide and <paramref name="h"/> tall.</summary>
    public static double Heart(double x, double y, double w, double h)
    {
        // Quílez's exact heart (point at the origin, lobes up to y ≈ 1.1, x within ±0.6), scaled to fit.
        double sx = w / 1.21, sy = h / 1.1, sc = Math.Min(sx, sy);
        double px = Math.Abs(x) / sx, py = (h / 2 - y) / sy;
        double d;
        if (py + px > 1)
        {
            double dx = px - 0.25, dy = py - 0.75;
            d = Math.Sqrt(dx * dx + dy * dy) - Math.Sqrt(2) / 4;
        }
        else
        {
            double a = px * px + (py - 1) * (py - 1);
            double m = 0.5 * Math.Max(px + py, 0);
            double b = (px - m) * (px - m) + (py - m) * (py - m);
            d = Math.Sqrt(Math.Min(a, b)) * Math.Sign(px - py);
        }
        return d * sc;
    }

    /// <summary>A star of <paramref name="points"/> points, outer radius <paramref name="r"/>, inner <paramref name="inner"/>.</summary>
    public static double Star(double x, double y, int points, double r, double inner)
    {
        Span<double> pts = stackalloc double[points * 4];
        for (int i = 0; i < points * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / points, rr = i % 2 == 0 ? r : inner;
            pts[2 * i] = Math.Cos(a) * rr;
            pts[2 * i + 1] = Math.Sin(a) * rr;
        }
        return Polygon(x, y, pts);
    }

    /// <summary>Cheap smooth noise, 0…1, from a lattice of hashed values (for hammered and worn textures).</summary>
    public static double Noise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
    }

    public static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
}
