using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>What a texture's pieces are made of: it decides how they wear.</summary>
internal enum Stuff { Stone, SoftStone, Brick, Tile, Wood, Metal, Plaster }

/// <summary>
/// How worn a texture is and how fine its wear: <paramref name="Amount"/> from 0 (new and crisp) to 1
/// (ancient and battered), 0.5 being the look each texture was designed for; <paramref name="Detail"/>
/// the size of the smallest marks (pits, nicks, grain) in mm; <paramref name="LinesOnly"/> when the
/// texture is scored as outlines rather than etched, so nothing is added that only makes sense filled.
/// </summary>
internal readonly record struct Wear(double Amount, double Detail, bool LinesOnly = false)
{
    public static Wear Normal(double detail) => new(0.5, detail);
}

/// <summary>
/// Ages a texture's pieces so no two look alike and the face reads as a real wall rather than a
/// drawing of one: edges nibbled so the gaps between pieces vary, corners knocked off, bites out of the
/// edges, pits and weathered patches etched into faces (the etch's own variety: some pieces stay smooth,
/// some are pitted all over), cracks scored across them (and the worst etched open), scratches and
/// rust on metal, splits and knots in wood. How much happens also wanders slowly over the face, so one
/// end of a wall can be more weathered than the other, as real ones are.
/// </summary>
internal sealed class Weathering
{
    readonly Noise _noise;
    readonly Random _rnd;
    readonly Wear _w;
    readonly double _d;

    public Weathering(int seed, Wear wear)
    {
        _noise = new Noise(seed);
        _rnd = new Random(seed * 31 + 7);
        _w = wear with { Amount = Math.Clamp(wear.Amount, 0, 1) };
        _d = Math.Clamp(wear.Detail, 0.2, 5);
    }

    public double Detail => _d;
    public Noise Noise => _noise;
    public Random Random => _rnd;

    /// <summary>How worn the face is here: the overall amount, swayed by a slow wander over the face and a little per piece.</summary>
    double LocalWear(double x, double y)
    {
        double sway = _noise.Fbm(x + 500, y + 500, 45, 2);
        double k = _w.Amount * 2 * (1 + 0.55 * sway) * (0.65 + 0.7 * _rnd.NextDouble());
        return Math.Clamp(k, 0, 2.2);
    }

    int Count(double expected)
    {
        if (expected <= 0) return 0;
        int n = (int)Math.Floor(expected);
        if (_rnd.NextDouble() < expected - n) n++;
        return n;
    }

    /// <summary>
    /// One piece worn: its new outline (or outlines, if a bite splits it, which is rare), with any pits,
    /// patches and wide cracks added to <paramref name="etched"/> and scratches, grain and fine cracks to
    /// <paramref name="lines"/>.
    /// </summary>
    public PathsD Piece(PathD piece, Stuff stuff, PathsD etched, List<PathD> lines)
    {
        if (piece.Count < 3 || _w.Amount <= 0.001) return [piece];
        var bb = Poly.Bounds(piece);
        double w = bb.right - bb.left, h = bb.bottom - bb.top, s = Math.Min(w, h);
        if (s < 0.6) return [piece];
        double cx = (bb.left + bb.right) / 2, cy = (bb.top + bb.bottom) / 2;
        double k = LocalWear(cx, cy);
        double d = _d;
        double shift = _rnd.NextDouble() * 1000;

        // 1. The edges nibbled (not metal: it's cut clean).
        double depth = stuff switch
        {
            Stuff.Stone => Math.Min(s * 0.14, 0.1 + d * 0.45 * k),
            Stuff.SoftStone => Math.Min(s * 0.18, 0.15 + d * 0.8 * k),
            Stuff.Brick => Math.Min(s * 0.1, 0.06 + d * 0.3 * k),
            Stuff.Tile => Math.Min(s * 0.06, d * 0.12 * k),
            Stuff.Wood => Math.Min(s * 0.08, d * 0.18 * k),
            Stuff.Plaster => Math.Min(s * 0.1, d * 0.3 * k),
            _ => 0,
        };
        double wave = stuff == Stuff.SoftStone ? d * 3.5 : d * 1.6;
        var shape = new PathsD { depth > 0.02 ? Poly.Roughen(piece, _noise, depth, wave, Math.Max(0.15, d * 0.35), shift) : piece };

        // 2. Corners knocked off and bites out of the edges.
        var bites = new PathsD();
        double cornerChance = stuff switch
        {
            Stuff.Stone => 0.14 * k, Stuff.SoftStone => 0.1 * k, Stuff.Brick => 0.2 * k, Stuff.Tile => 0.1 * k,
            Stuff.Wood => 0.06 * k, Stuff.Metal => 0.04 * k, _ => 0.08 * k,
        };
        bool soft = stuff is Stuff.SoftStone or Stuff.Plaster;
        foreach (var c in Corners(piece))
        {
            if (_rnd.NextDouble() > cornerChance) continue;
            double size = s * (0.1 + 0.2 * _rnd.NextDouble()) * (stuff == Stuff.Metal ? 0.4 : 1);
            bites.Add(soft
                ? Poly.Blob(c.x, c.y, size, size * (0.6 + 0.6 * _rnd.NextDouble()), 1, _rnd, _rnd.NextDouble() * Math.PI)
                : Poly.Shard(c.x, c.y, size, size * (0.6 + 0.6 * _rnd.NextDouble()), _rnd, _rnd.NextDouble() * Math.PI));
        }
        double perimeter = 2 * (w + h);
        double biteRate = stuff switch { Stuff.Stone => 0.35, Stuff.SoftStone => 0.4, Stuff.Brick => 0.18, Stuff.Plaster => 0.25, _ => 0 };
        int nBites = Math.Min(4, Count(perimeter / Math.Max(4, s * 2.5) * biteRate * k * k * 0.5));
        for (int i = 0; i < nBites; i++)
        {
            var (p, along) = OnEdgeDir(piece);
            // A bite is longer along the edge than it is deep.
            double r = s * (0.06 + 0.14 * _rnd.NextDouble());
            bites.Add(soft
                ? Poly.Blob(p.x, p.y, r * (1.2 + _rnd.NextDouble()), r, 0.9, _rnd, along)
                : Poly.Shard(p.x, p.y, r * (1.4 + _rnd.NextDouble()), r, _rnd, along));
        }
        if (bites.Count > 0)
        {
            var bitten = Poly.Subtract(shape, bites);
            // Keep the piece whole: if a bite would take most of it, leave it be.
            if (Poly.Area(bitten) > Poly.Area(shape) * 0.55) shape = [.. bitten.Where(p => Math.Abs(Clipper.Area(p)) > 0.05)];
        }
        if (shape.Count == 0) return [];

        // 3. Marks on the face.
        if (!_w.LinesOnly) Faces(shape, stuff, k, bb, etched);
        Cracks(shape, stuff, k, s, etched, lines);
        if (stuff == Stuff.Metal) Scratches(shape, k, bb, lines);
        return shape;
    }

    /// <summary>
    /// Marks on a face made of several pieces drawn exactly (a vent's frame and slats, a pipe run): pits,
    /// patches, scratches and cracks, without changing any outline.
    /// </summary>
    public void Surface(PathsD area, Stuff stuff, PathsD etched, List<PathD> lines)
    {
        if (area.Count == 0 || _w.Amount <= 0.001) return;
        var bb = Poly.Bounds(area);
        double s = Math.Min(bb.right - bb.left, bb.bottom - bb.top);
        if (s < 0.6) return;
        double k = LocalWear((bb.left + bb.right) / 2, (bb.top + bb.bottom) / 2);
        if (!_w.LinesOnly) Faces(area, stuff, k, bb, etched);
        Cracks(area, stuff, k, s, etched, lines);
        if (stuff == Stuff.Metal) Scratches(area, k, bb, lines);
    }

    /// <summary>Pits and weathered patches etched into the piece's face.</summary>
    void Faces(PathsD shape, Stuff stuff, double k, RectD bb, PathsD etched)
    {
        double d = _d, area = Poly.Area(shape);
        var inside = Poly.Offset(shape, -Math.Max(0.2, d * 0.25));
        if (inside.Count == 0) return;
        var marks = new PathsD();

        // Pits: only some pieces are pitted, and then in a cluster or two, mostly tiny with the odd big one.
        double pitRate = stuff switch
        {
            Stuff.Stone => 0.3, Stuff.SoftStone => 0.5, Stuff.Brick => 0.22, Stuff.Plaster => 0.3, Stuff.Metal => 0.2, Stuff.Wood => 0.05, _ => 0.08,
        };
        double xr = bb.right - bb.left, yr = bb.bottom - bb.top;
        if (_rnd.NextDouble() < pitRate * k)
        {
            int clusters = 1 + (_rnd.NextDouble() < 0.35 ? 1 : 0);
            for (int c = 0; c < clusters; c++)
            {
                double cx = bb.left + xr * (0.15 + 0.7 * _rnd.NextDouble()), cy = bb.top + yr * (0.15 + 0.7 * _rnd.NextDouble());
                double spread = Math.Min(Math.Min(xr, yr) * 0.45, d * (2 + 3 * _rnd.NextDouble()));
                int n = Math.Min(16, 2 + Count(area / (d * d) * 0.02 * k + 4 * _rnd.NextDouble()));
                for (int i = 0; i < n; i++)
                {
                    // Gaussian-ish scatter round the cluster's middle.
                    double rr = spread * Math.Sqrt(-2 * Math.Log(Math.Max(1e-6, _rnd.NextDouble()))) * 0.5, aa = _rnd.NextDouble() * Math.PI * 2;
                    double u = _rnd.NextDouble();
                    double r = Math.Max(0.15, d * (0.16 + 0.5 * u * u * u) * (stuff == Stuff.Metal ? 0.7 : 1));
                    marks.Add(Poly.Blob(cx + rr * Math.Cos(aa), cy + rr * Math.Sin(aa), r * (0.8 + 0.5 * _rnd.NextDouble()), r, 0.8, _rnd, _rnd.NextDouble() * Math.PI));
                }
            }
        }
        // And now and then a lone pit anywhere.
        if (_rnd.NextDouble() < 0.25 * k && stuff != Stuff.Wood)
        {
            double r = Math.Max(0.15, d * (0.15 + 0.2 * _rnd.NextDouble()));
            marks.Add(Poly.Blob(bb.left + xr * _rnd.NextDouble(), bb.top + yr * _rnd.NextDouble(), r, r * 0.8, 0.6, _rnd));
        }

        // Weathered patches: where fine noise rises above a level that falls the more worn the piece is.
        double patchFrom = stuff switch
        {
            Stuff.Stone => 1.15, Stuff.SoftStone => 0.8, Stuff.Brick => 1.4, Stuff.Plaster => 1.0, Stuff.Metal => 1.3, _ => 9,
        };
        if (k > patchFrom)
        {
            double level = 0.62 - 0.32 * Math.Min(1, (k - patchFrom) / 1.2);
            double size = d * (stuff == Stuff.Metal ? 2.2 : 3.2);
            double o = _rnd.NextDouble() * 1000;
            var patch = Poly.Contours(bb.left, bb.top, bb.right, bb.bottom, Math.Max(0.12, d * 0.3),
                (x, y) => _noise.Fbm(x + o, y - o, size, 3), level);
            // Crumbs too small to burn are dropped (shrunk away and grown back).
            double crumb = Math.Max(0.06, d * 0.12);
            marks.AddRange(Poly.Offset(Poly.Offset(patch, -crumb), crumb));
        }
        if (marks.Count == 0) return;
        etched.AddRange(Poly.Intersect(Poly.Union(marks), inside));
    }

    /// <summary>Cracks: a wandering line from an edge into the piece, sometimes forking; the worst are etched open as well as scored.</summary>
    void Cracks(PathsD shape, Stuff stuff, double k, double s, PathsD etched, List<PathD> lines)
    {
        double chance = stuff switch
        {
            Stuff.Stone => 0.12, Stuff.SoftStone => 0.08, Stuff.Brick => 0.05, Stuff.Tile => 0.07, Stuff.Plaster => 0.25, Stuff.Wood => 0.12, _ => 0,
        } * k;
        if (s < _d * 3 || _rnd.NextDouble() > chance) return;
        var outline = shape.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();
        var bb = Poly.Bounds(outline);
        var start = OnEdge(outline);
        var mid = new PointD((bb.left + bb.right) / 2, (bb.top + bb.bottom) / 2);
        double dir = stuff == Stuff.Wood
            ? (bb.right - bb.left > bb.bottom - bb.top ? (start.x < mid.x ? 0 : Math.PI) : (start.y < mid.y ? Math.PI / 2 : -Math.PI / 2))
            : Math.Atan2(mid.y - start.y, mid.x - start.x) + (_rnd.NextDouble() - 0.5) * 1.2;
        double length = (stuff == Stuff.Wood ? Math.Max(bb.right - bb.left, bb.bottom - bb.top) * 0.35 : s * 0.9) * (0.5 + 0.7 * _rnd.NextDouble());
        var main = Walk(start, dir, length, stuff == Stuff.Wood ? 0.15 : 0.7);
        var found = new List<PathD> { main };
        if (stuff != Stuff.Wood && main.Count > 3 && _rnd.NextDouble() < 0.45)
        {
            var at = main[main.Count / 2];
            found.Add(Walk(at, dir + (_rnd.NextDouble() < 0.5 ? -1 : 1) * (0.5 + 0.5 * _rnd.NextDouble()), length * 0.45, 0.8));
        }
        lines.AddRange(Poly.ClipLines(found, shape));
        if (!_w.LinesOnly && k > 1.1 && stuff != Stuff.Tile && main.Count >= 2)
            etched.AddRange(Poly.Intersect([Poly.Taper(main, Math.Max(0.25, _d * 0.45), 0.02)], shape));
    }

    PathD Walk(PointD from, double dir, double length, double wander)
    {
        var p = new PathD { from };
        double step = Math.Max(0.25, _d * 0.7), run = 0;
        var at = from;
        while (run < length)
        {
            dir += (_rnd.NextDouble() - 0.5) * wander;
            at = new PointD(at.x + Math.Cos(dir) * step, at.y + Math.Sin(dir) * step);
            p.Add(at);
            run += step;
        }
        return p;
    }

    /// <summary>Scratches on metal: short, nearly straight, mostly one way, in a few clusters.</summary>
    void Scratches(PathsD shape, double k, RectD bb, List<PathD> lines)
    {
        double area = Poly.Area(shape);
        int n = Math.Min(8, Count(area / 140 * k));
        if (n == 0) return;
        var found = new List<PathD>();
        double main = _rnd.NextDouble() * Math.PI;
        for (int i = 0; i < n; i++)
        {
            double x = bb.left + (bb.right - bb.left) * _rnd.NextDouble(), y = bb.top + (bb.bottom - bb.top) * _rnd.NextDouble();
            double a = main + (_rnd.NextDouble() - 0.5) * 0.6, l = _d * (2 + 6 * _rnd.NextDouble());
            double bend = (_rnd.NextDouble() - 0.5) * 0.15;
            var p = new PathD();
            for (int j = 0; j <= 4; j++)
            {
                double t = j / 4.0 - 0.5, aa = a + bend * t;
                p.Add(new PointD(x + Math.Cos(aa) * l * t, y + Math.Sin(aa) * l * t));
            }
            found.Add(p);
        }
        lines.AddRange(Poly.ClipLines(found, Poly.Offset(shape, -0.2)));
    }

    /// <summary>The outline's corners (where it turns more than 30°).</summary>
    static IEnumerable<PointD> Corners(PathD p)
    {
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[(i - 1 + p.Count) % p.Count];
            var b = p[i];
            var c = p[(i + 1) % p.Count];
            double ax = b.x - a.x, ay = b.y - a.y, bx = c.x - b.x, by = c.y - b.y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-6 || lb < 1e-6) continue;
            double cos = (ax * bx + ay * by) / (la * lb);
            if (cos < 0.87) yield return b;
        }
    }

    /// <summary>A point somewhere on the outline, chosen evenly along its length.</summary>
    PointD OnEdge(PathD p) => OnEdgeDir(p).At;

    /// <summary>A point somewhere on the outline, chosen evenly along its length, and the direction of the edge there (radians).</summary>
    (PointD At, double Along) OnEdgeDir(PathD p)
    {
        double total = 0;
        for (int i = 0; i < p.Count; i++) total += Poly.Dist(p[i], p[(i + 1) % p.Count]);
        double t = _rnd.NextDouble() * total;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            double l = Poly.Dist(a, b);
            if (t <= l && l > 0) return (new PointD(a.x + (b.x - a.x) * t / l, a.y + (b.y - a.y) * t / l), Math.Atan2(b.y - a.y, b.x - a.x));
            t -= l;
        }
        return (p[0], 0);
    }
}
