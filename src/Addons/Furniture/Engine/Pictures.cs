using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Pictures etched and scored on flat pieces (flat coordinates, mm, y up): a portrait or a landscape in a
/// carved frame, a map of a coast with mountains, forests, a river, towns, a road and a compass rose, a
/// star chart, and a damask's ogee lattice.
/// </summary>
public static class Pictures
{
    /// <summary>A carved frame round x0..x1, y0..y1 (its outside), <paramref name="fr"/> wide: two grooves and a block at each corner.</summary>
    public static void Frame(double x0, double y0, double x1, double y1, double fr, PathsD etched, List<PathD> lines)
    {
        etched.AddRange(Fronts.Outline(x0 + fr * 0.3, y0 + fr * 0.3, x1 - fr * 0.3, y1 - fr * 0.3, Math.Min(0.35, fr * 0.2)));
        lines.Add(Geo.Loop(Geo.Rect(x0 + fr * 0.62, y0 + fr * 0.62, x1 - fr * 0.62, y1 - fr * 0.62)));
        lines.Add(Geo.Loop(Geo.Rect(x0 + fr, y0 + fr, x1 - fr, y1 - fr)));
        if (fr < 1.2) return;
        double c = fr * 0.36;
        foreach (var (cx, cy) in new[] { (x0 + fr / 2, y0 + fr / 2), (x1 - fr / 2, y0 + fr / 2), (x0 + fr / 2, y1 - fr / 2), (x1 - fr / 2, y1 - fr / 2) })
        {
            lines.Add(Geo.Loop(Geo.Rect(cx - c, cy - c, cx + c, cy + c)));
            etched.Add(Geo.Circle(cx, cy, c * 0.45, 0.01));
        }
    }

    /// <summary>A head and shoulders, etched dark against a light ground, with the hair, the collar and a chain scored on it.</summary>
    public static void Portrait(double x0, double y0, double x1, double y1, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 3 || h < 4) return;
        double cx = x0 + w / 2, hr = Math.Min(w * 0.17, h * 0.13), hy = y0 + h * 0.62;
        // The head (a little taller than wide), the neck, and the shoulders curving down to the frame.
        var head = Rugs.Ellipse(cx, hy, hr, hr * 1.25);
        var neck = Geo.Rect(cx - hr * 0.42, hy - hr * 2.1, cx + hr * 0.42, hy - hr * 0.6);
        var body = new PathD();
        double sw = Math.Min(w * 0.4, hr * 2.7), top = hy - hr * 1.8;
        for (int i = 0; i <= 24; i++)
        {
            double t = i / 24.0, a = Math.PI * t;
            body.Add(new PointD(cx - sw * Math.Cos(a), top - (top - y0) * 0.15 + (top - y0) * 0.12 * Math.Sin(a) - (1 - Math.Sin(a)) * (top - y0) * 0.55));
        }
        body.Add(new PointD(cx + sw, y0));
        body.Add(new PointD(cx - sw, y0));
        var bust = Geo.Solid([head, neck, body]);
        etched.AddRange(Geo.Intersect(bust, [Geo.Rect(x0, y0, x1, y1)]));
        // Hair framing the face, a collar and a chain across the chest: lines left light on the dark.
        lines.Add([.. Geo.Arc(cx, hy, hr * 1.12, Math.PI * 0.05, Math.PI * 0.95, 0.02)]);
        lines.Add([new(cx - hr * 1.2, top - hr * 0.1), new(cx, top - hr * 0.9), new(cx + hr * 1.2, top - hr * 0.1)]);
        lines.Add([.. Geo.Arc(cx, top + hr * 0.4, hr * 1.7, Math.PI * 1.25, Math.PI * 1.75, 0.02)]);
    }

    /// <summary>
    /// A landscape: a sky with a sun and birds, far hills, a castle on a near hill, a tree in front, the
    /// near ground hatched.
    /// </summary>
    public static void Landscape(double x0, double y0, double x1, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 3 || h < 3) return;
        var box = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        double ph = rnd.NextDouble() * 3;
        PathD Ridge(double baseY, double amp, double freq, double phase)
        {
            var p = new PathD();
            for (int i = 0; i <= 30; i++)
            {
                double t = i / 30.0;
                p.Add(new PointD(x0 + w * t, y0 + h * (baseY + amp * Math.Sin(t * freq + phase) + amp * 0.4 * Math.Sin(t * freq * 2.3 + phase * 1.7))));
            }
            return p;
        }
        // Far hills: a line; the near hill etched dark, its top a ridge.
        lines.AddRange(Geo.ClipLines([Ridge(0.52, 0.06, 4, ph)], box));
        var near = Ridge(0.3, 0.09, 3, ph + 1);
        var ground = Geo.Intersect([new PathD(near) { new(x1, y0), new(x0, y0) }], box);
        // A castle on the near hill (a keep with battlements and a tower with a spire), left light with its outline scored.
        double kx = x0 + w * 0.62, kw = Math.Max(1.2, w * 0.14), kh = Math.Max(1.4, h * 0.2);
        double ky = near.Where(p => Math.Abs(p.x - kx) < kw).Min(p => p.y) - 0.3;
        var castle = new PathsD { Geo.Rect(kx - kw / 2, ky - 0.6, kx + kw / 2, ky + kh) };
        double cren = kw / 5;
        for (int i = 0; i < 5; i += 2) castle.Add(Geo.Rect(kx - kw / 2 + i * cren, ky + kh - 0.01, kx - kw / 2 + (i + 1) * cren, ky + kh + cren));
        castle.Add(Geo.Rect(kx + kw / 2 - 0.01, ky - 0.6, kx + kw / 2 + kw * 0.4, ky + kh * 1.35));
        castle.Add(new PathD { new(kx + kw / 2 - 0.1, ky + kh * 1.35), new(kx + kw / 2 + kw * 0.5, ky + kh * 1.35), new(kx + kw / 2 + kw * 0.2, ky + kh * 1.75) });
        castle = Geo.Intersect(Geo.Union(castle), box);
        etched.AddRange(Geo.Subtract(ground, Geo.Offset(castle, 0.25)));
        lines.AddRange(castle.Select(Geo.Loop));
        lines.AddRange(Geo.ClipLines([Geo.Loop(Fronts.ArchedRect(kx - kw * 0.15, ky - 0.6, kx + kw * 0.15, ky + kh * 0.45))], box));
        // A tree in front on the left: a trunk and a crown of three clumps, etched.
        double tx = x0 + w * 0.24, tr = Math.Min(w, h) * 0.13, ty = y0 + h * 0.18;
        if (tr > 0.6)
            etched.AddRange(Geo.Intersect(Geo.Union([
                Geo.Rect(tx - tr * 0.18, y0, tx + tr * 0.18, ty + tr * 1.2),
                Geo.Circle(tx, ty + tr * 1.6, tr, 0.01), Geo.Circle(tx - tr * 0.6, ty + tr * 1.2, tr * 0.7, 0.01), Geo.Circle(tx + tr * 0.6, ty + tr * 1.25, tr * 0.7, 0.01)]), box));
        // The sun with rays, and a couple of birds.
        double sx = x0 + w * 0.2, sy = y0 + h * 0.8, sr = Math.Min(w, h) * 0.07;
        if (sr > 0.35)
        {
            lines.Add(Geo.Loop(Geo.Circle(sx, sy, sr, 0.01)));
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                lines.Add([new(sx + Math.Cos(a) * sr * 1.4, sy + Math.Sin(a) * sr * 1.4), new(sx + Math.Cos(a) * sr * 2, sy + Math.Sin(a) * sr * 2)]);
            }
        }
        foreach (var (bx, by) in new[] { (0.55, 0.8), (0.66, 0.74) })
        {
            double s = Math.Min(w, h) * 0.04;
            lines.Add([new(x0 + w * bx - s, y0 + h * by + s * 0.5), new(x0 + w * bx, y0 + h * by), new(x0 + w * bx + s, y0 + h * by + s * 0.5)]);
        }
    }

    /// <summary>
    /// A map in x0..x1, y0..y1: an island's coast with the sea hatched in waves round it, mountains, a forest,
    /// a river to the sea, towns joined by a dotted road, and a compass rose out at sea.
    /// </summary>
    public static void Map(double x0, double y0, double x1, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 4 || h < 3) return;
        double cx = x0 + w * 0.44, cy = y0 + h * 0.5;
        var land = Coast(cx, cy, w * 0.33, h * 0.37, rnd);
        lines.Add(Geo.Loop(land));
        var landA = new PathsD { land };
        // The sea: short wave strokes round the coast, out to the edge.
        var seaBand = Geo.Subtract(Geo.Offset(landA, Math.Min(w, h) * 0.12), Geo.Offset(landA, 0.35));
        // A second line just off the coast, as old maps have.
        lines.AddRange(Geo.ClipLines(Geo.Offset(landA, 0.5).Select(Geo.Loop), [Geo.Rect(x0, y0, x1, y1)]));
        for (int i = 0; i < (int)(w * h / 9); i++)
        {
            double x = x0 + w * rnd.NextDouble(), y = y0 + h * rnd.NextDouble();
            if (Inside(seaBand, x, y) || Inside(landA, x, y)) continue;
            var wave = new PathD();
            for (int k = 0; k <= 6; k++) wave.Add(new PointD(x + k * 0.2, y + 0.12 * Math.Sin(k * Math.PI / 3)));
            lines.AddRange(Geo.ClipLines([wave], [Geo.Rect(x0, y0, x1, y1)]));
        }
        var inland = Geo.Offset(landA, -Math.Min(w, h) * 0.06);
        // Mountains: a range of peaks, each with its shaded side etched.
        double mx = cx + w * 0.06, my = cy + h * 0.1, ms = Geo.Clamp(Math.Min(w, h) * 0.08, 0.6, 1.6);
        var peaks = new List<PointD>();
        for (int i = 0; i < 5; i++)
        {
            double px = mx + (i - 2) * ms * 1.1 + (rnd.NextDouble() - 0.5) * ms * 0.3, py = my + Math.Abs(i - 2) * -ms * 0.25;
            if (!Inside(inland, px, py) || !Inside(inland, px, py + ms * 1.2)) continue;
            peaks.Add(new PointD(px, py));
            double s = ms * (0.8 + 0.3 * rnd.NextDouble());
            lines.Add([new(px - s * 0.7, py), new(px, py + s * 1.1), new(px + s * 0.7, py)]);
            etched.Add(new PathD { new(px, py + s * 1.1), new(px + s * 0.7, py), new(px + s * 0.15, py) });
        }
        // A forest: little round trees in a clump.
        double fx = cx - w * 0.13, fy = cy - h * 0.12, fr = Geo.Clamp(Math.Min(w, h) * 0.025, 0.25, 0.5);
        for (int i = 0; i < 9; i++)
        {
            double tx = fx + (rnd.NextDouble() - 0.5) * w * 0.14, ty = fy + (rnd.NextDouble() - 0.5) * h * 0.14;
            if (!Inside(inland, tx, ty)) continue;
            lines.Add(Geo.Loop(Geo.Circle(tx, ty + fr, fr, 0.01)));
            lines.Add([new(tx, ty), new(tx, ty - fr * 0.6)]);
        }
        // A river from the mountains down to the coast.
        var river = new PathD();
        double rx = mx - ms * 0.5, ry = my - ms * 0.2;
        for (int i = 0; i < 40 && Inside(landA, rx, ry); i++)
        {
            river.Add(new PointD(rx, ry));
            rx += 0.35 + (rnd.NextDouble() - 0.4) * 0.2;
            ry -= 0.3 + 0.25 * Math.Sin(i * 0.8);
        }
        river.Add(new PointD(rx, ry));
        if (river.Count > 3) etched.AddRange(Geo.Intersect(Thick(river, 0.3), landA));
        // Towns (a ring with a dot) and the road between them, dotted.
        var towns = new[] { new PointD(cx - w * 0.2, cy + h * 0.12), new PointD(cx + w * 0.02, cy - h * 0.2), new PointD(cx + w * 0.2, cy - h * 0.05) };
        var road = new PathD();
        foreach (var t in towns.Where(t => Inside(inland, t.x, t.y)))
        {
            lines.Add(Geo.Loop(Geo.Circle(t.x, t.y, 0.4, 0.01)));
            etched.Add(Geo.Circle(t.x, t.y, 0.18, 0.01));
            road.Add(t);
        }
        for (int i = 0; i + 1 < road.Count; i++)
        {
            var a = road[i];
            var b = road[i + 1];
            double len = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            for (double s = 0.7; s < len - 0.6; s += 0.6)
            {
                double t = s / len;
                lines.Add([new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t), new(a.x + (b.x - a.x) * (t + 0.3 / len), a.y + (b.y - a.y) * (t + 0.3 / len))]);
            }
        }
        // A compass rose out at sea, in the corner away from the land.
        double cr = Math.Min(w, h) * 0.13;
        if (cr > 0.8)
        {
            double qx = x1 - cr * 1.3, qy = y1 - cr * 1.3;
            var star = Fronts.Star(qx, qy, cr, cr * 0.22, 4);
            lines.Add(Geo.Loop(star));
            lines.Add(Geo.Loop(new PathD(Fronts.Star(qx, qy, cr * 0.6, cr * 0.18, 4).Select(p => Rot(p, qx, qy, Math.PI / 4)))));
            lines.Add(Geo.Loop(Geo.Circle(qx, qy, cr * 0.75, 0.01)));
            etched.Add(new PathD { new(qx, qy), new(qx + cr * 0.22 * 0.7071, qy + cr * 0.22 * 0.7071), new(qx, qy + cr) });
        }
    }

    /// <summary>A star chart: a sun, planets on their orbits, scattered stars, a grid and a dotted course.</summary>
    public static void StarMap(double x0, double y0, double x1, double y1, Random rnd, PathsD into, List<PathD> lines)
    {
        var etched = new PathsD();
        double w = x1 - x0, h = y1 - y0;
        if (w < 4 || h < 3) return;
        var box = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        // A faint grid.
        double g = Geo.Clamp(Math.Min(w, h) / 4, 2, 5);
        var grid = new List<PathD>();
        for (double x = x0 + g; x < x1 - 0.2; x += g) grid.Add([new(x, y0), new(x, y1)]);
        for (double y = y0 + g; y < y1 - 0.2; y += g) grid.Add([new(x0, y), new(x1, y)]);
        double cx = x0 + w * 0.42, cy = y0 + h * 0.52;
        // Orbits (ellipses, tilted a little) with a planet on each.
        int n = 4;
        var planets = new List<PointD>();
        for (int i = 1; i <= n; i++)
        {
            double rx = w * 0.1 * i, ry = h * 0.1 * i, a = rnd.NextDouble() * Math.PI * 2;
            lines.AddRange(Geo.ClipLines([Geo.Loop(Rugs.Ellipse(cx, cy, rx, ry))], box));
            var p = new PointD(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry);
            if (p.x > x0 + 0.6 && p.x < x1 - 0.6 && p.y > y0 + 0.6 && p.y < y1 - 0.6)
            {
                double pr = 0.25 + 0.12 * i;
                etched.Add(Geo.Circle(p.x, p.y, pr, 0.01));
                if (i == 3) lines.Add(Geo.Loop(Rugs.Ellipse(p.x, p.y, pr * 1.9, pr * 0.6)));
                planets.Add(p);
            }
        }
        // The sun, rayed.
        double sr = Math.Min(w, h) * 0.05;
        etched.Add(Geo.Circle(cx, cy, Math.Max(0.4, sr), 0.01));
        lines.Add(Geo.Loop(Fronts.Star(cx, cy, sr * 2.2, sr * 1.1, 8)));
        // Stars: little crosses and dots, clear of the orbits' middle.
        for (int i = 0; i < (int)(w * h / 5); i++)
        {
            double x = x0 + w * rnd.NextDouble(), y = y0 + h * rnd.NextDouble();
            if (rnd.NextDouble() < 0.3)
            {
                double s = 0.25 + rnd.NextDouble() * 0.2;
                lines.Add([new(x - s, y), new(x + s, y)]);
                lines.Add([new(x, y - s), new(x, y + s)]);
            }
            else etched.Add(Geo.Circle(x, y, 0.1, 0.01));
        }
        // A dotted course out to the edge, from the outer planet.
        if (planets.Count > 0)
        {
            var a = planets[^1];
            var b = new PointD(x1 - w * 0.08, y0 + h * 0.12);
            double len = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            for (double s = 0.6; s < len; s += 0.5) etched.Add(Geo.Circle(a.x + (b.x - a.x) * s / len, a.y + (b.y - a.y) * s / len, 0.09, 0.01));
            lines.Add(Geo.Loop(Geo.Rect(b.x - 0.4, b.y - 0.4, b.x + 0.4, b.y + 0.4)));
        }
        lines.AddRange(Geo.ClipLines(grid, box));
        into.AddRange(Geo.Intersect(Geo.Union(etched), box));
    }

    /// <summary>Damask: an ogee lattice scored over x0..x1, y0..y1, a lily etched in each cell, smaller rosettes where the cells meet.</summary>
    public static void Damask(double x0, double y0, double x1, double y1, double cell, PathsD into, List<PathD> lines)
    {
        var etched = new PathsD();
        double w = x1 - x0, h = y1 - y0;
        if (w < cell || h < cell) return;
        var box = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        double a = cell / 2, b = cell * 0.75;
        int nx = (int)Math.Ceiling(w / cell) + 1, ny = (int)Math.Ceiling(h / b) + 1;
        double ox = x0 + (w - (nx - 1) * cell) / 2, oy = y0 + (h - (ny - 1) * b) / 2;
        var shapes = new List<PathD>();
        for (int j = -1; j <= ny; j++)
            for (int i = -1; i <= nx; i++)
            {
                double cx = ox + i * cell + (j % 2 == 0 ? 0 : cell / 2), cy = oy + j * b;
                shapes.Add(Geo.Loop(Ogee(cx, cy, a, b)));
                if (cx > x0 + a * 0.6 && cx < x1 - a * 0.6 && cy > y0 + b * 0.5 && cy < y1 - b * 0.5) Lily(cx, cy, a * 0.55, etched, lines);
            }
        lines.AddRange(Geo.ClipLines(shapes, box));
        into.AddRange(Geo.Intersect(Geo.Union(etched), box));
    }

    /// <summary>An ogee: pointed top and bottom, the sides in S-curves, bulging out to <paramref name="a"/> at the middle.</summary>
    static PathD Ogee(double cx, double cy, double a, double b)
    {
        var p = new PathD();
        const int n = 20;
        double Side(double u) => (Math.Sin(Math.PI * u) - 0.25 * Math.Sin(3 * Math.PI * u)) / 1.25;
        for (int i = 0; i <= n; i++) { double u = (double)i / n; p.Add(new PointD(cx + a * Side(u), cy - b + 2 * b * u)); }
        for (int i = n - 1; i > 0; i--) { double u = (double)i / n; p.Add(new PointD(cx - a * Side(u), cy - b + 2 * b * u)); }
        return p;
    }

    /// <summary>A stylised lily: three petals over a band, etched.</summary>
    static void Lily(double cx, double cy, double r, PathsD etched, List<PathD> lines)
    {
        if (r < 0.5) return;
        etched.Add(Rugs.Ellipse(cx, cy + r * 0.35, r * 0.22, r * 0.6));
        foreach (var s in new[] { -1, 1 })
        {
            var petal = new PathD();
            for (int i = 0; i <= 10; i++)
            {
                double t = i / 10.0;
                petal.Add(new PointD(cx + s * (r * 0.15 + r * 0.7 * Math.Sin(t * Math.PI * 0.8)), cy - r * 0.05 + r * 0.85 * t - r * 0.5 * t * t));
            }
            for (int i = 10; i >= 0; i--)
            {
                double t = i / 10.0;
                petal.Add(new PointD(cx + s * (r * 0.12 + r * 0.45 * Math.Sin(t * Math.PI * 0.8)), cy - r * 0.05 + r * 0.55 * t - r * 0.3 * t * t));
            }
            etched.AddRange(Geo.Solid([petal]));
        }
        etched.Add(Geo.Rect(cx - r * 0.5, cy - r * 0.3, cx + r * 0.5, cy - r * 0.12));
        lines.Add([new(cx, cy - r * 0.3), new(cx, cy - r * 0.8)]);
    }

    /// <summary>An island's coast: a wobbly oval with bays and headlands.</summary>
    static PathD Coast(double cx, double cy, double rx, double ry, Random rnd)
    {
        var p = new PathD();
        const int n = 90;
        double p1 = rnd.NextDouble() * 6, p2 = rnd.NextDouble() * 6, p3 = rnd.NextDouble() * 6;
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            double r = 1 + 0.16 * Math.Sin(a * 3 + p1) + 0.09 * Math.Sin(a * 5 + p2) + 0.05 * Math.Sin(a * 11 + p3);
            p.Add(new PointD(cx + Math.Cos(a) * rx * r, cy + Math.Sin(a) * ry * r));
        }
        return p;
    }

    static PointD Rot(PointD p, double cx, double cy, double a) =>
        new(cx + (p.x - cx) * Math.Cos(a) - (p.y - cy) * Math.Sin(a), cy + (p.x - cx) * Math.Sin(a) + (p.y - cy) * Math.Cos(a));

    static bool Inside(PathsD region, double x, double y)
    {
        int inside = 0;
        foreach (var path in region)
            if (Clipper.PointInPolygon(new PointD(x, y), path) != PointInPolygonResult.IsOutside) inside++;
        return inside % 2 == 1;
    }

    static PathsD Thick(PathD line, double w)
    {
        var all = new PathsD();
        for (int i = 0; i + 1 < line.Count; i++) all.Add(Geo.Strip(line[i], line[i + 1], w));
        return Geo.Union(all);
    }
}
