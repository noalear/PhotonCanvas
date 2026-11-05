using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>What a tree is: a spreading oak, a pine, a dead tree, a palm or a giant mushroom.</summary>
public enum TreeStyle { Oak, Pine, Dead, Palm, Mushroom }

/// <summary>A street stall: a market stall under an awning, an outdoor bar, or a notice board.</summary>
public enum StallStyle { Market, Bar, NoticeBoard }

/// <summary>Street pieces for the city builder: trees, troughs, stages and stalls.</summary>
public static partial class TownWalls
{
    // ---- trees ----

    /// <summary>
    /// A tree: two profiles halved together (one slotted from the top, one from the bottom) standing in a
    /// round base, each drawn on its own so the tree looks full from every side: bark and leaves
    /// etched, gaps cut through the canopy.
    /// </summary>
    public static void Tree(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 90;
        if (W < 16 || D < 16) { m.Bad("The tree is too narrow: make it at least 16 mm across each way."); return; }
        if (H < 30) { m.Bad("The tree is too low: make it at least 30 mm high."); return; }
        var b = new Build(m, LookOf(m.Theme), k);
        double sw = m.SlotW, a = sw / 2 + 2.2, tab = 1.8;
        double ground = T, h = H - ground;
        double ym = Math.Round(h * (it.Tree == TreeStyle.Mushroom ? 0.3 : 0.2), 1);
        // The base, round (a planter for sci-fi), with grass and stones.
        double R = Geo.Clamp(Math.Max(W, D) * 0.3, 9 * k, 17 * k), cx = W / 2, cz = D / 2;
        var bed = new Flat { Name = "Base", Y = ground, Texture = "none" };
        bool planter = m.Theme == TerrainThemes.SciFi;
        bed.Outline.Add(planter ? Build.XZ([.. Fronts.Chamfered(cx - R, -cz - R, cx + R, -cz + R, R * 0.3).Select(p => new V3(p.x, 0, -p.y))]) : Geo.Circle(cx, -cz, R, 0.02));
        Ground(bed, cx, -cz, R, planter, m.Rnd);
        b.Flats.Add(bed);
        foreach (var (name, along, len) in new[] { ("Tree", true, W), ("Tree cross", false, D) })
        {
            double c = len / 2;
            var (outline, holes, etched, lines) = TreeProfile(it.Tree, len, h, k, sw, m.Rnd);
            // The joint: one slotted down from the top, the other up from the bottom.
            var cut = Geo.Subtract(outline, holes);
            cut = Geo.Open(cut, 1.15);
            cut = Geo.Subtract(cut, [along ? Geo.Rect(c - sw / 2, ym, c + sw / 2, h + 5) : Geo.Rect(c - sw / 2, -5, c + sw / 2, ym)]);
            cut = Geo.Open(cut, 1.0);
            var up = along
                ? b.Upright(name, new V3(0, 0, cz), V3.UnitX, len, V3.UnitZ)
                : b.Upright(name, new V3(cx, 0, 0), V3.UnitZ, len, -V3.UnitX);
            up.O += new V3(0, ground, 0);
            up.Plain = true;
            cut = Largest(Geo.Tidy(cut));
            up.Outline.AddRange(cut.Where(Clipper.IsPositive));
            up.Holes.AddRange(cut.Where(q => !Clipper.IsPositive(q)));
            up.Etched.AddRange(etched);
            up.Lines.AddRange(lines);
            // Two tabs down into the base, clear of the other profile's slot.
            foreach (var u in new[] { c - a, c + a })
            {
                up.Outline.Add(Geo.Rect(u - tab / 2, -T, u + tab / 2, 0.02));
                var n = up.N * ((T + m.Fit) / 2);
                V3 p0 = up.At(u - tab / 2 - m.Fit / 2, 0) - new V3(0, ground, 0), p1 = up.At(u + tab / 2 + m.Fit / 2, 0) - new V3(0, ground, 0);
                bed.Holes.Add(Build.XZ(p0 - n, p1 - n, p1 + n, p0 + n));
            }
        }
        b.Finish();
        m.Note("Slide the two halves of the tree together by their slots, then push its four feet into the base.");
    }

    /// <summary>The biggest piece of a tidied shape with its holes, dropping crumbs the slot cut loose.</summary>
    static PathsD Largest(PathsD a)
    {
        var outer = a.Where(Clipper.IsPositive).OrderByDescending(q => Clipper.Area(q)).FirstOrDefault();
        if (outer is null) return a;
        return [outer, .. a.Where(q => !Clipper.IsPositive(q) && Clipper.PointInPolygon(q[0], outer) != PointInPolygonResult.IsOutside)];
    }

    /// <summary>Grass tufts and stones on a base (or soil and a rim for a planter).</summary>
    static void Ground(Flat bed, double cx, double cy, double R, bool planter, Random rnd)
    {
        if (planter)
        {
            bed.Lines.Add(Geo.Loop(Fronts.Chamfered(cx - R + 1.2, cy - R + 1.2, cx + R - 1.2, cy + R - 1.2, R * 0.3 - 0.4)));
            for (int i = 0; i < 14; i++) bed.Etched.Add(Geo.Circle(cx + (rnd.NextDouble() - 0.5) * R * 1.4, cy + (rnd.NextDouble() - 0.5) * R * 1.4, 0.2, 0.01));
            return;
        }
        bed.Lines.Add(Geo.Loop(Geo.Circle(cx, cy, R - 0.8, 0.02)));
        for (int i = 0; i < 9; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2, r = R * (0.45 + 0.4 * rnd.NextDouble());
            double x = cx + Math.Cos(ang) * r, y = cy + Math.Sin(ang) * r;
            if (rnd.NextDouble() < 0.4) bed.Etched.Add(Rugs.Ellipse(x, y, 0.5 + rnd.NextDouble() * 0.5, 0.4 + rnd.NextDouble() * 0.3));
            else
                for (int j = -1; j <= 1; j++) bed.Lines.Add([new(x + j * 0.3, y), new(x + j * 0.55, y + 0.9 + rnd.NextDouble() * 0.4)]);
        }
    }

    /// <summary>One profile of a tree, <paramref name="w"/> across and <paramref name="h"/> high, standing at v = 0.</summary>
    static (PathsD Outline, PathsD Holes, PathsD Etched, List<PathD> Lines) TreeProfile(TreeStyle style, double w, double h, double k, double sw, Random rnd)
    {
        var o = new PathsD();
        var holes = new PathsD();
        var e = new PathsD();
        var l = new List<PathD>();
        double c = w / 2;
        // A trunk from the ground with roots, widths given at heights.
        void Trunk(double g, double top, double topHalf, double rootReach)
        {
            var left = new PathD();
            var right = new PathD();
            int n = 8;
            for (int i = 0; i <= n; i++)
            {
                double t = i / (double)n, y = top * t, half = g + (topHalf - g) * Math.Pow(t, 0.7) + Math.Sin(t * 7 + rnd.NextDouble()) * 0.2 * k;
                left.Add(new PointD(c - half, y));
                right.Add(new PointD(c + half, y));
            }
            right.Reverse();
            o.Add([.. left, .. right]);
            foreach (int s in new[] { -1, 1 })
                o.Add(new PathD { new(c + s * (g - 0.5), 0), new(c + s * (g + rootReach), 0), new(c + s * (g + rootReach), 1.1), new(c + s * (g - 0.5), 3.2 * k) });
            // Bark.
            for (double x = -g + 1.0; x < g - 0.6; x += 1.3 * k)
            {
                var line = new PathD();
                for (double y = 0.6; y < top; y += 1.2 * k)
                {
                    double t = y / top, half = g + (topHalf - g) * Math.Pow(t, 0.7);
                    line.Add(new PointD(c + x / g * half * 0.85 + (rnd.NextDouble() - 0.5) * 0.25, y));
                }
                if (line.Count > 1) l.Add(line);
            }
        }
        // A tapering limb from p to q.
        void Limb(PointD p, PointD q, double w0, double w1)
        {
            double dx = q.x - p.x, dy = q.y - p.y, len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy)), nx = -dy / len, ny = dx / len;
            o.Add(new PathD { new(p.x + nx * w0 / 2, p.y + ny * w0 / 2), new(p.x - nx * w0 / 2, p.y - ny * w0 / 2), new(q.x - nx * w1 / 2, q.y - ny * w1 / 2), new(q.x + nx * w1 / 2, q.y + ny * w1 / 2) });
            o.Add(Geo.Circle(q.x, q.y, w1 / 2 + 0.04, 0.01));
            l.Add([new(p.x + nx * w0 * 0.15, p.y + ny * w0 * 0.15), new((p.x + q.x) / 2 + nx * 0.2, (p.y + q.y) / 2 + ny * 0.2), new(q.x, q.y)]);
        }
        double g0 = Math.Max(sw / 2 + 3.4, 4.4 * k);
        switch (style)
        {
            case TreeStyle.Pine:
            {
                Trunk(g0 * 0.75, h * 0.3, Math.Max(sw / 2 + 1.4, 2.4 * k), 2.6 * k);
                double yb = h * 0.15;
                int tiers = 5;
                for (int i = 0; i < tiers; i++)
                {
                    double t = i / (double)tiers, yi = yb + (h - yb) * t * 0.82, th = (h - yb) * 0.34, wi = (w / 2 - 0.5) * (1 - t * 0.78);
                    var tier = new PathD();
                    int teeth = Math.Max(2, (int)(wi * 2 / (3.4 * k)));
                    double tw = wi * 2 / teeth;
                    for (int j = 0; j < teeth; j++)
                    {
                        double x0 = c - wi + j * tw;
                        tier.Add(new PointD(x0 + 0.01, yi + 1.8 * k));
                        tier.Add(new PointD(x0 + tw * 0.5 - 0.6, yi));
                        tier.Add(new PointD(x0 + tw * 0.5 + 0.6, yi));
                    }
                    tier.Add(new PointD(c + wi, yi + 1.8 * k));
                    double apex = Math.Min(h, yi + th);
                    tier.Add(new PointD(c + 0.8, apex));
                    tier.Add(new PointD(c - 0.8, apex));
                    o.Add(tier);
                    // Needles: strokes down and out along each tier.
                    for (double x = c - wi + 1.4; x < c + wi - 1.4; x += 1.5 * k)
                    {
                        double ty = yi + 1.8 * k + (apex - yi - 1.8 * k) * (1 - Math.Abs(x - c) / wi) * 0.75;
                        if (ty - yi > 2.6 * k) l.Add([new(x, ty - 0.6), new(x + (x < c ? -0.8 : 0.8) * k, yi + 2.2 * k)]);
                    }
                }
                break;
            }
            case TreeStyle.Dead:
            {
                Trunk(g0, h * 0.42, Math.Max(sw / 2 + 1.4, 2.5 * k), 3.4 * k);
                void Branch(PointD p, double ang, double len, double wid, int depth)
                {
                    var q = new PointD(Geo.Clamp(p.x + Math.Cos(ang) * len, 1 + wid, w - 1 - wid), Math.Min(h - 1 - wid, p.y + Math.Sin(ang) * len));
                    double w1 = Math.Max(1.5, wid * 0.68);
                    Limb(p, q, wid, w1);
                    if (depth == 0) return;
                    double spread = 0.35 + rnd.NextDouble() * 0.3;
                    Branch(q, ang + spread, len * 0.7, w1, depth - 1);
                    Branch(q, ang - spread * 0.9, len * 0.65, w1, depth - 1);
                }
                // Each branch starts low in the trunk to one side of the joint, so it stays on its own half.
                double fy = h * 0.42 - 2 * k, side = sw / 2 + 0.5;
                Branch(new(c - side, fy), Math.PI / 2 + 0.55, h * 0.24, 2.6 * k + 0.8, 3);
                Branch(new(c + side, fy), Math.PI / 2 - 0.5, h * 0.26, 2.6 * k + 0.8, 3);
                Branch(new(c - side, fy), Math.PI / 2 + 0.3, h * 0.22, 2.2 * k + 0.8, 2);
                // A hollow knot in the trunk.
                e.Add(Rugs.Ellipse(c + g0 * 0.25, h * 0.22, 0.7 * k, 1.1 * k));
                break;
            }
            case TreeStyle.Palm:
            {
                double top = h * 0.74;
                Trunk(g0 * 0.85, top, Math.Max(sw / 2 + 1.4, 2.4 * k), 2.4 * k);
                for (double y = 2.2 * k; y < top - 1; y += 1.8 * k) l.Add([new(c - g0 * 0.7, y), new(c, y + 0.5 * k), new(c + g0 * 0.7, y)]);
                // Fronds arching out and down from the crown, with notched leaflets.
                var crown = new PointD(c, top);
                foreach (var (dir, rise, reach) in new[] { (-1.0, 0.5, 0.5), (1.0, 0.5, 0.5), (-1.0, 1.0, 0.36), (1.0, 1.0, 0.36), (-0.25, 1.25, 0.14), (0.3, 1.2, 0.16) })
                {
                    var spine = new List<PointD>();
                    for (int i = 0; i <= 10; i++)
                    {
                        double t = i / 10.0, x = c + dir * (w / 2 - 1.2) * reach * 2 * t, y = top + (h - top) * rise * (Math.Sin(t * Math.PI * 0.9)) * 0.9 - (h - top) * 0.5 * t * t;
                        spine.Add(new PointD(Geo.Clamp(x, 1.2, w - 1.2), Math.Min(h - 0.8, y)));
                    }
                    for (int i = 0; i < spine.Count - 1; i++)
                    {
                        double t = i / 10.0;
                        Limb(spine[i], spine[i + 1], 3.0 * k * (1 - t) + 1.3, 3.0 * k * (1 - t - 0.1) + 1.3);
                    }
                }
                foreach (var dx in new[] { -1.4, 0.0, 1.4 }) o.Add(Geo.Circle(c + dx * k, top - 0.6 * k, 1.1 * k + 0.2, 0.02));
                break;
            }
            case TreeStyle.Mushroom:
            {
                double capY = h * 0.56, stemTop = Math.Max(sw / 2 + 1.6, 3.0 * k);
                Trunk(g0 * 1.05, capY + 1, stemTop, 1.6 * k);
                // A skirt on the stem, and the cap: a dome, spotted, with gills under it.
                o.Add(new PathD { new(c - stemTop - 1.6 * k, capY * 0.7), new(c + stemTop + 1.6 * k, capY * 0.7), new(c + stemTop, capY * 0.7 + 1.8 * k), new(c - stemTop, capY * 0.7 + 1.8 * k) });
                double rx = w / 2 - 0.5, ry = h - capY;
                var cap = new PathD();
                for (int i = 0; i <= 40; i++)
                {
                    double t = Math.PI * i / 40;
                    cap.Add(new PointD(c + Math.Cos(t) * rx, capY + 1.6 * k + Math.Sin(t) * (ry - 1.6 * k)));
                }
                cap.Add(new PointD(c - rx, capY + 1.0 * k));
                for (int i = 1; i < 10; i++) cap.Add(new PointD(c - rx + 2 * rx * i / 10, capY + 1.0 * k - Math.Sin(Math.PI * i / 10) * 1.2 * k));
                cap.Add(new PointD(c + rx, capY + 1.0 * k));
                o.Add(cap);
                for (int i = 1; i < 14; i++) { double x = c - rx + 2 * rx * i / 14; l.Add([new(x, capY + 1.2 * k), new(c + (x - c) * 0.6, capY + 0.2)]); }
                for (int i = 0; i < 7; i++)
                {
                    double t = Math.PI * (0.15 + 0.7 * rnd.NextDouble()), rr = 0.55 + 0.3 * rnd.NextDouble();
                    double x = c + Math.Cos(t) * rx * rr, y = capY + 1.6 * k + Math.Sin(t) * (ry - 1.6 * k) * rr;
                    e.Add(Rugs.Ellipse(x, y, (0.6 + rnd.NextDouble() * 0.5) * k, (0.5 + rnd.NextDouble() * 0.3) * k));
                }
                // Little ones at its foot.
                foreach (int s in new[] { -1, 1 })
                {
                    double x = c + s * (g0 + 4 * k);
                    if (x - 3 * k < 0.5 || x + 3 * k > w - 0.5) continue;
                    o.Add(Geo.Rect(Math.Min(c, x), 0, Math.Max(c, x), 1.3));
                    o.Add(Geo.Rect(x - 1.0 * k, 0, x + 1.0 * k, 4 * k));
                    var small = new PathD();
                    for (int i = 0; i <= 16; i++) { double t = Math.PI * i / 16; small.Add(new PointD(x + Math.Cos(t) * 2.8 * k, 4 * k + Math.Sin(t) * 2.4 * k)); }
                    o.Add(small);
                }
                break;
            }
            default:
            {
                // An oak: a stout trunk forking into limbs under a canopy of rounded clumps.
                double fork = h * 0.4;
                Trunk(g0, fork + 1, Math.Max(sw / 2 + 1.5, 2.8 * k), 3.6 * k);
                double cy = h * 0.66, rx = w / 2 - 0.6, ry = h - cy - 0.5;
                // The limbs start low in the trunk to one side of the joint, so each stays on its own half.
                double fy = fork - 2 * k, side = sw / 2 + 0.5;
                Limb(new(c - side, fy), new(c - rx * 0.55, cy - ry * 0.1), 3.0 * k + 0.6, 1.8 * k);
                Limb(new(c + side, fy), new(c + rx * 0.6, cy - ry * 0.05), 3.0 * k + 0.6, 1.8 * k);
                Limb(new(c + side, fy), new(c + rx * 0.1 + side, cy + ry * 0.4), 2.8 * k + 0.6, 1.8 * k);
                // The canopy: an elliptical core edged with clumps of leaves.
                var core = new PathD();
                for (int i = 0; i < 48; i++) { double t = Math.PI * 2 * i / 48; core.Add(new PointD(c + Math.Cos(t) * rx * 0.72, cy + Math.Sin(t) * ry * 0.7)); }
                o.Add(core);
                var lobes = new List<(double X, double Y, double R)>();
                int n = 13;
                for (int i = 0; i < n; i++)
                {
                    double t = -0.35 + (Math.PI + 0.7) * i / (n - 1) + (rnd.NextDouble() - 0.5) * 0.15;
                    double r = Math.Min(rx, ry) * (0.26 + 0.08 * rnd.NextDouble());
                    double x = c + Math.Cos(t) * (rx - r), y = cy + Math.Sin(t) * (ry - r);
                    lobes.Add((x, y, r));
                }
                foreach (var t in new[] { -0.9, -1.5, -2.2 })
                {
                    double r = Math.Min(rx, ry) * 0.24;
                    lobes.Add((c + Math.Cos(t) * (rx - r) * 0.92, cy + Math.Sin(t) * (ry - r) * 0.85, r));
                }
                foreach (var (x, y, r) in lobes)
                {
                    o.Add(Geo.Circle(x, y, r, 0.02));
                    // Shading: an arc in each clump and a few leaves.
                    l.Add([.. Geo.Arc(x, y, r * 0.72, -Math.PI * 0.55, Math.PI * 0.05)]);
                    for (int j = 0; j < 2; j++)
                    {
                        double ax = x + (rnd.NextDouble() - 0.5) * r, ay = y + (rnd.NextDouble() - 0.3) * r * 0.8;
                        l.Add([.. Geo.Arc(ax, ay, 0.6 * k, Math.PI * 1.1, Math.PI * 1.9)]);
                    }
                }
                // Sky through the canopy, kept off the joint and the edges.
                for (int i = 0; i < 9; i++)
                {
                    double t = Math.PI * 2 * rnd.NextDouble(), rr = 0.42 + 0.3 * rnd.NextDouble();
                    double x = c + Math.Cos(t) * rx * rr, y = cy + Math.Sin(t) * ry * rr, r = (1.0 + 0.7 * rnd.NextDouble()) * k;
                    if (Math.Abs(x - c) < sw / 2 + r + 1.6 || y - r < fork + 4 * k) continue;
                    holes.Add(Rugs.Ellipse(x, y, r * 1.3, r));
                }
                break;
            }
        }
        return (Geo.Solid(o), Geo.Solid(holes), e, l);
    }

    // ---- troughs ----

    /// <summary>
    /// A horse trough: four walls on a plinth, water inside on two baffles, a stone (or plank, or plate)
    /// rim glued round the top and a pump at one end (a feed pipe for sci-fi).
    /// </summary>
    public static void Trough(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 9;
        if (D < 4 * T + 3) { m.Bad($"The trough is too narrow: make it at least {4 * T + 3:0.#} mm deep."); return; }
        if (H < 6) { m.Bad("The trough is too low: make it at least 6 mm high."); return; }
        var look = LookOf(m.Theme);
        bool wood = look.Wood || m.Theme == TerrainThemes.Dungeon, metal = look.Top == Top.Armour;
        var b = new Build(m, look with { Face = wood ? "Wood planks (across)" : metal ? "Metal plates" : look.Face, A = wood ? 3 : look.A * 0.5, B = wood ? 30 : look.B * 0.5 }, k * 0.5);
        double pumpL = metal ? 0 : Math.Round(7 * Math.Min(1.4, k), 1), Wt = W - pumpL;
        if (Wt < 4 * T + 8) { m.Bad("The trough is too short: make it longer."); return; }
        double lip = 1.2, x0 = lip, x1 = Wt - lip, z0 = lip, z1 = D - lip, top = H - (b.Glue ? T : 0);
        // The plinth it stands on, out past the walls, with the pump's stand at one end.
        var plinth = new Flat { Name = "Plinth", Y = T, Texture = "none" };
        plinth.Outline.Add(Build.XZRect(0, 0, W, D));
        plinth.Lines.Add(Geo.Loop(Build.XZRect(0.5, 0.5, W - 0.5, D - 0.5)));
        b.Flats.Add(plinth);
        var front = b.Upright("Front", new V3(x0, 0, z1 - T / 2), V3.UnitX, x1 - x0, V3.UnitZ);
        var back = b.Upright("Back", new V3(x0, 0, z0 + T / 2), V3.UnitX, x1 - x0, -V3.UnitZ);
        var left = b.Upright("End", new V3(x0 + T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, -V3.UnitX);
        var right = b.Upright("End", new V3(x1 - T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
        foreach (var f in new[] { front, back, left, right })
        {
            f.Outline.Add(Geo.Rect(0, T, f.Len, top));
            foreach (var (t0, t1) in b.Tabs(0, f.Len, f == left || f == right ? 0.8 : T + 1.2)) b.DownInto(plinth, f, t0, t1);
        }
        // Iron bands on a wooden one.
        if (wood)
            foreach (var f in new[] { front, back })
                foreach (var x in new[] { f.Len * 0.18, f.Len * 0.82 })
                {
                    f.Etched.Add(Geo.Rect(x - 0.5, T, x + 0.5, top));
                    f.Lines.Add(WoodFace.Dot(x, top - 1.2, 0.2));
                    f.Lines.Add(WoodFace.Dot(x, T + 1.2, 0.2));
                }
        // The water, on two baffles standing in the plinth.
        double wy = top - Math.Min(1.6, (top - T) * 0.3);
        var water = new Flat { Name = "Water", Y = wy, Texture = "none" };
        water.Outline.Add(Build.XZRect(x0 + T + 0.02, z0 + T + 0.02, x1 - T - 0.02, z1 - T - 0.02));
        for (int i = 0; i < 5; i++)
        {
            double cx = x0 + T + (x1 - x0 - 2 * T) * (0.15 + 0.7 * m.Rnd.NextDouble()), cz = (z0 + z1) / 2 + (m.Rnd.NextDouble() - 0.5) * (z1 - z0 - 2 * T) * 0.5;
            water.Lines.Add([.. Geo.Arc(cx, -cz, 0.8 + i * 0.25, 0.3, Math.PI - 0.3)]);
        }
        b.Flats.Add(water);
        foreach (var x in new[] { x0 + T + (x1 - x0 - 2 * T) * 0.3, x0 + T + (x1 - x0 - 2 * T) * 0.7 })
        {
            var baffle = b.Upright("Baffle", new V3(x, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
            baffle.Plain = true;
            baffle.Outline.Add(Geo.Rect(0.1, T, baffle.Len - 0.1, wy - T));
            b.DownInto(plinth, baffle, baffle.Len * 0.3, baffle.Len * 0.7);
            b.UpInto(water, baffle, baffle.Len * 0.35, baffle.Len * 0.65);
        }
        if (b.Glue)
        {
            // The rim, glued round the top.
            var rim = new Flat { Name = "Rim", Y = H, Texture = "none" };
            rim.Outline.Add(Build.XZRect(x0 - 0.6, z0 - 0.6, x1 + 0.6, z1 + 0.6));
            rim.Holes.Add(Build.XZRect(x0 + T, z0 + T, x1 - T, z1 - T));
            rim.Lines.Add(Geo.Loop(Build.XZRect(x0 - 0.1, z0 - 0.1, x1 + 0.1, z1 + 0.1)));
            if (!wood && !metal) for (double x = x0 + 4; x < x1 - 2; x += 5) { rim.Lines.Add([new(x, -z0 + 0.6), new(x, -(z0 + T))]); rim.Lines.Add([new(x - 1.5, -(z1 - T)), new(x - 1.5, -z1 - 0.6)]); }
            // A notch for the sci-fi feed pipe coming up through it.
            if (metal) { double zc = z0 + T + (z1 - z0 - 2 * T) / 2; rim.Holes.Add(Build.XZRect(x1 - T - 0.05, zc - 0.8 - m.Fit, x1 + 2, zc + 0.8 + m.Fit)); }
            rim.Glued = true;
            b.Flats.Add(rim);
        }
        if (metal)
        {
            // A feed pipe over the end, with a valve wheel.
            double pc = right.Len / 2;
            right.Outline.Add(Geo.Rect(pc - 0.8, top - 0.02, pc + 0.8, top + 4 * k));
            right.Outline.Add(Geo.Circle(pc, top + 4 * k, 1.5, 0.02));
            right.Lines.Add(Geo.Loop(Geo.Circle(pc, top + 4 * k, 0.8, 0.02)));
        }
        else
        {
            // The pump: a post with its spout over the water and the handle up.
            double px = Wt + pumpL * 0.45, pw = Math.Max(2.4, 2.6 * k * 0.6), ph = H + 9 * Math.Min(1.4, k);
            var pump = b.Upright("Pump", new V3(0, 0, D / 2), V3.UnitX, W, V3.UnitZ);
            pump.Plain = true;
            pump.Outline.Add(Geo.Rect(px - pw / 2, T, px + pw / 2, ph));
            pump.Outline.Add(Geo.Rect(px - pw / 2 - 0.6, ph - 0.01, px + pw / 2, ph + 1.2));
            double sy = ph - 2.4;
            // The spout over the rim.
            double xt = x1 - 3.4;
            pump.Outline.Add(new PathD { new(px - pw / 2 + 0.01, sy), new(xt, sy - 0.4), new(xt, sy - 2.8), new(xt + 1.3, sy - 2.8), new(xt + 1.3, sy - 1.7), new(px - pw / 2 + 0.01, sy - 1.4) });
            // The handle, cranked up and back.
            double hx = Math.Max(px + pw / 2 + 1.2, Math.Min(W - 1.4, px + pw / 2 + 4.2));
            pump.Outline.Add(Geo.Strip(new(px, ph - 0.2), new(hx, ph + 3.6), 1.5));
            pump.Outline.Add(Geo.Circle(hx, ph + 3.6, 0.75, 0.02));
            pump.Lines.Add(WoodFace.Dot(px, ph - 0.4, 0.35));
            pump.Lines.Add([new(px - pw / 2 + 0.4, T + 2), new(px + pw / 2 - 0.4, T + 2)]);
            b.DownInto(plinth, pump, px - pw / 2 + 0.3, px + pw / 2 - 0.3);
        }
        b.Finish();
        m.Note("Push the walls and baffles into the plinth, lay the water on the baffles" + (b.Glue ? ", glue the rim round the top" : "") + (metal ? "." : " and stand the pump in its slot."));
    }

    // ---- the stage ----

    /// <summary>
    /// A players' stage: a planked deck on skirts and joists, steps up the front, a backdrop painted with
    /// a landscape behind a proscenium with a crest, curtains tied back and pennants on the posts.
    /// </summary>
    public static void Stage(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 60;
        double deck = Math.Round(Geo.Clamp(H * 0.2, 6, 16), 1);
        if (W < 30 || D < 20) { m.Bad("The stage is too small: make it at least 30 × 20 mm."); return; }
        if (H < deck + 20) { m.Bad("The stage is too low for its backdrop: make it taller."); return; }
        var look = LookOf(m.Theme);
        bool metal = look.Top == Top.Armour;
        var b = new Build(m, look with { Face = metal ? "Metal plates" : "Wood planks (upright)", A = metal ? 10 : 3, B = metal ? 8 : 30 }, k);
        double stepD = Math.Min(10 * k, D * 0.25), D0 = D - stepD, lip = 1.2;
        // The deck on its skirts.
        var top = new Flat { Name = "Deck", Y = deck, Texture = metal ? "Metal plates" : "Wood planks (across)", A = metal ? 10 : 3.2, B = metal ? 8 : 30, G = 0.4 };
        top.Outline.Add(Build.XZRect(0, 0, W, D0));
        b.Flats.Add(top);
        double x0 = lip, x1 = W - lip, z0 = lip, z1 = D0 - lip;
        var front = b.Upright("Front skirt", new V3(x0, 0, z1 - T / 2), V3.UnitX, x1 - x0, V3.UnitZ);
        var back = b.Upright("Back skirt", new V3(x0, 0, z0 + T / 2), V3.UnitX, x1 - x0, -V3.UnitZ);
        var left = b.Upright("Side skirt", new V3(x0 + T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, -V3.UnitX);
        var right = b.Upright("Side skirt", new V3(x1 - T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
        foreach (var f in new[] { front, back, left, right })
        {
            f.Outline.Add(Geo.Rect(0, 0, f.Len, deck - T));
            foreach (var (t0, t1) in b.Tabs(0, f.Len, f == left || f == right ? 1.2 : T + 1.4)) b.UpInto(top, f, t0, t1);
        }
        // Joists across under the deck.
        int joists = Math.Max(1, (int)((x1 - x0) / (22 * k)));
        for (int i = 1; i <= joists; i++)
        {
            double x = x0 + (x1 - x0) * i / (joists + 1);
            var j = b.Upright("Joist", new V3(x, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
            j.Plain = true;
            j.Outline.Add(Geo.Rect(0.1, 0, j.Len - 0.1, deck - T));
            foreach (var (t0, t1) in b.Tabs(0.1, j.Len - 0.1, 1.5)) b.UpInto(top, j, t0, t1);
        }
        // Panels on the front skirt, with the theme's badge in the middle one.
        int panels = Math.Max(3, (int)Math.Round((x1 - x0) / (12 * k)) | 1);
        double pw = (front.Len - 2) / panels;
        for (int i = 0; i < panels; i++)
        {
            double a = 1 + i * pw + 0.4, c = 1 + (i + 1) * pw - 0.4;
            front.Etched.AddRange(Fronts.Outline(a, 1.0, c, deck - T - 1.0, 0.35));
            if (i == panels / 2) Cabinets.Symbol(Fronts.Look(m), m.Theme, (a + c) / 2, (deck - T) / 2, Math.Min(c - a, deck - T - 2) * 0.32, front.Etched, front.Lines);
        }
        // Steps up the front, in the middle: treads with their tabs through the skirt, each on a riser.
        double sw2 = Math.Min(16 * k, W * 0.3), sx0 = W / 2 - sw2 / 2, sx1 = W / 2 + sw2 / 2;
        int steps = Math.Max(2, (int)Math.Floor(deck / Math.Max(3.4 * k, T + 1.1)));
        double rise = deck / steps, run = (D - z1) / steps;
        Flat? below = null;
        for (int s = 1; s < steps; s++)
        {
            double y = rise * s, zf = D - (s - 1) * run;
            var tr = new Flat { Name = $"Step {s}", Y = y, Texture = top.Texture, A = top.A, B = top.B, G = top.G };
            tr.Outline.Add(Build.XZRect(sx0, z1, sx1, zf));
            b.Flats.Add(tr);
            double ua = Build.UOf(front, new V3(sx0 + 2, 0, 0), V3.UnitX, 0), ub = Build.UOf(front, new V3(sx1 - 2, 0, 0), V3.UnitX, 0);
            b.FlatInto(tr, front, Math.Min(ua, ub), Math.Max(ua, ub));
            var riser = b.Upright($"Riser {s}", new V3(sx0 + 0.6, 0, zf - 1.3 - T / 2), V3.UnitX, sw2 - 1.2, V3.UnitZ);
            riser.Outline.Add(Geo.Rect(0, below?.Y ?? 0, riser.Len, y - T));
            foreach (var (t0, t1) in b.Tabs(0, riser.Len, 1.2)) b.UpInto(tr, riser, t0, t1);
            if (below is not null) foreach (var (t0, t1) in b.Tabs(0, riser.Len, 1.2)) b.DownInto(below, riser, t0, t1);
            below = tr;
        }
        // The backdrop: a painted scene, standing in the deck at the back.
        double bz = z0 + T + 1.4 * k + T / 2, bh = H - deck;
        var drop = b.Upright("Backdrop", new V3(x0 + T + 0.6, 0, bz), V3.UnitX, x1 - x0 - 2 * T - 1.2, V3.UnitZ);
        drop.Plain = true;
        drop.Outline.Add(Geo.Rect(0, deck, drop.Len, H - 4 * k));
        foreach (var (t0, t1) in b.Tabs(0, drop.Len, 2)) b.DownInto(top, drop, t0, t1);
        double px = Math.Max(3.2, 3.2 * k), arch = Math.Min(6 * k, bh * 0.2);
        var pe = new PathsD();
        var pl = new List<PathD>();
        if (metal) Pictures.StarMap(px, deck + 1, drop.Len - px, H - 4 * k - arch, m.Rnd, pe, pl);
        else Pictures.Landscape(px, deck + 1, drop.Len - px, H - 4 * k - arch, m.Rnd, pe, pl);
        drop.Etched.AddRange(pe);
        drop.Lines.AddRange(pl);
        if (b.Glue)
        {
            // The proscenium glued on the front of it: posts, a pelmet with a crest, pennants on top.
            var pro = b.Upright("Proscenium", new V3(x0 + T + 0.6, 0, bz + T), V3.UnitX, drop.Len, V3.UnitZ);
            pro.Glued = pro.Plain = true;
            double L = pro.Len, py = H - 4 * k - arch;
            pro.Outline.Add(Geo.Rect(0, deck + 0.02, px, H - 2 * k));
            pro.Outline.Add(Geo.Rect(L - px, deck + 0.02, L, H - 2 * k));
            var pelmet = new PathD { new(0, H - 4 * k), new(L, H - 4 * k), new(L, py - 0.5) };
            for (int i = 1; i < 24; i++) { double t = i / 24.0; pelmet.Add(new PointD(L - px - (L - 2 * px) * t, py - 0.5 + Math.Sin(t * Math.PI) * arch * 0.9)); }
            pelmet.Add(new PointD(0, py - 0.5));
            pro.Outline.Add(pelmet);
            pro.Outline.Add(Geo.Rect(0, H - 4 * k - 0.01, L, H - 2.6 * k));
            double crest = Math.Min(5 * k, L * 0.12);
            pro.Outline.Add(Shield(L / 2 - crest / 2, H - 2.8 * k - crest * 0.2, L / 2 + crest / 2, H));
            Cabinets.Symbol(Fronts.Look(m), m.Theme, L / 2, H - 2.6 * k - crest * 0.2 + (crest * 1.1) * 0.45, crest * 0.3, pro.Etched, pro.Lines);
            foreach (var x in new[] { px / 2, L - px / 2 })
            {
                pro.Lines.Add([new(x, deck + 1), new(x, H - 4.5 * k)]);
                // A pennant on a staff above each post.
                pro.Outline.Add(Geo.Rect(x - 0.55, H - 2.7 * k, x + 0.55, H));
            }
            // The curtains, tied back to the posts.
            foreach (var (u0, sign) in new[] { (px - 0.01, 1.0), (L - px + 0.01, -1.0) })
            {
                var cur = b.Upright(sign > 0 ? "Curtain" : "Curtain", new V3(x0 + T + 0.6, 0, bz + 2 * T), V3.UnitX, L, V3.UnitZ);
                cur.Glued = cur.Plain = true;
                double cw = Math.Min(9 * k, L * 0.2), tie = deck + (py - deck) * 0.35;
                var shape = new PathD { new(u0, py), new(u0 + sign * cw, py), new(u0 + sign * cw * 0.3, tie + 1.5), new(u0 + sign * cw * 0.55, deck + 0.3), new(u0, deck + 0.3) };
                cur.Outline.Add(shape);
                for (int f = 1; f < 5; f++) cur.Lines.Add([new(u0 + sign * cw * f / 5, py - 0.3), new(u0 + sign * cw * 0.3 * f / 5 + sign * 0.2, tie + 1.6), new(u0 + sign * cw * 0.55 * f / 5, deck + 0.6)]);
                cur.Etched.Add(Geo.Rect(Math.Min(u0, u0 + sign * cw * 0.38), tie, Math.Max(u0, u0 + sign * cw * 0.38), tie + 0.9));
            }
        }
        b.Finish();
        m.Note("Push the skirts and joists up into the deck, the steps' treads through the front skirt and onto the stringers, and the backdrop down into the deck" + (b.Glue ? "; glue the proscenium and curtains on the front of the backdrop." : "."));
    }

    // ---- stalls ----

    /// <summary>
    /// A street stall: two side frames (a tall back post, a front post, the counter's end and a sloping
    /// rafter) joined by the counter top, a low shelf and the front, under a sloping awning (striped for a
    /// market, boarded for a bar) that the rafters' tabs hold. A market stall has crates of produce on its
    /// counter; a bar has barrel ends on its front and a sign on a bracket. A notice board is a board on
    /// two legs under a little roof, with notices pinned on.
    /// </summary>
    public static void Stall(Maker m)
    {
        var it = m.Item;
        if (it.Stall == StallStyle.NoticeBoard) { NoticeBoard(m); return; }
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 40;
        bool bar = it.Stall == StallStyle.Bar;
        if (W < 20 || D < 12) { m.Bad("The stall is too small: make it at least 20 × 12 mm."); return; }
        if (H < 24) { m.Bad("The stall is too low: make it at least 24 mm high."); return; }
        var look = LookOf(m.Theme);
        bool metal = look.Top == Top.Armour;
        var b = new Build(m, look with { Face = metal ? "Metal plates" : "Wood planks (across)", A = metal ? 8 : 2.6, B = metal ? 6 : 30, G = 0.4 }, k);
        double ch = Math.Round(Geo.Clamp(H * 0.36, 10, 18), 1), cd = Math.Round(Math.Min(D * 0.45, 9 * k), 1), pw = Math.Max(2.4, 2.4 * k);
        double side = 1.2;
        double Hb = H - T - 0.5, Hf = Math.Max(ch + 9 * k, H - T - 0.5 - D * 0.45);
        double len = Math.Sqrt(D * D + (Hb - Hf) * (Hb - Hf));
        // The awning reaches far enough past the front posts for the valance to hang clear of them.
        double over = Math.Max(2.2 * k, (T + 0.5) * len / D + 0.4);
        var up = new V3(0, (Hb - Hf) / len, -D / len);              // up the slope, front to back
        var n = new V3(0, D / len, (Hb - Hf) / len);                // the awning's face
        V3 R(double t) => new V3(0, Hf, D) + up * t;
        double rafter = Math.Max(2.2, 2.0 * k);
        // The side frames.
        var frames = new List<Up>();
        foreach (var (x, face) in new[] { (side + T / 2, -V3.UnitX), (W - side - T / 2, V3.UnitX) })
        {
            var f = b.Upright("Side frame", new V3(x, 0, 0), V3.UnitZ, D, face);
            f.Plain = true;
            double U(double z) => Build.UOf(f, new V3(0, 0, z), V3.UnitZ, 0);
            PathD Rect(double z0, double y0, double z1, double y1) => Geo.Rect(Math.Min(U(z0), U(z1)), y0, Math.Max(U(z0), U(z1)), y1);
            // The back post, its top cut to the slope.
            double Top(double z) => Hf + (D - z) * (Hb - Hf) / D + 0.02;
            f.Outline.Add(new PathD { new(U(0), 0), new(U(pw), 0), new(U(pw), Top(pw)), new(U(0), Top(0)) });
            f.Outline.Add(Rect(D - pw, 0, D, Hf + 0.02));
            f.Outline.Add(Rect(D - cd, 0, D, ch));
            // The rafter under the awning, its top edge along the slope.
            V3 a0 = R(0), a1 = R(len), b1 = a1 - n * rafter, b0 = a0 - n * rafter;
            f.Outline.Add([.. new[] { a0, a1, b1, b0 }.Select(q => new PointD(U(q.Z), q.Y))]);
            // A rail low down that carries the shelf.
            double sy = Math.Min(3.4 * k + T, ch * 0.3);
            f.Outline.Add(Rect(0, sy - T - 1.2, D, sy + 1.2));
            f.Lines.Add([new(U(D - cd + 0.6), 0.8), new(U(D - cd + 0.6), ch - 0.8)]);
            frames.Add(f);
            // Tabs up into the awning.
            foreach (var t in new[] { len * 0.25, len * 0.75 })
            {
                V3 p0 = R(t - 1.2) - n * 0.02, p1 = R(t + 1.2) - n * 0.02, p2 = R(t + 1.2) + n * T, p3 = R(t - 1.2) + n * T;
                f.Outline.Add([.. new[] { p0, p1, p2, p3 }.Select(q => new PointD(U(q.Z), q.Y))]);
            }
        }
        double fx0 = side + T, fx1 = W - side - T;
        // The counter top, a low shelf and the front, between the frames.
        var counter = new Flat { Name = "Counter", Y = ch, Texture = metal ? "Metal plates" : "Wood planks (across)", A = metal ? 8 : 2.8, B = metal ? 6 : 30, G = 0.4 };
        counter.Outline.Add(Build.XZRect(fx0, D - cd, fx1, D + 0.8));
        b.Flats.Add(counter);
        var shelf = new Flat { Name = "Shelf", Y = Math.Min(3.4 * k + T, ch * 0.3), Texture = counter.Texture, A = counter.A, B = counter.B, G = 0.4 };
        shelf.Outline.Add(Build.XZRect(fx0, pw + 0.4, fx1, D - cd - 0.3));
        b.Flats.Add(shelf);
        foreach (var f in frames)
        {
            double U(double z) => Build.UOf(f, new V3(0, 0, z), V3.UnitZ, 0);
            b.FlatInto(counter, f, Math.Min(U(D - cd + 1.2), U(D - 1.6)), Math.Max(U(D - cd + 1.2), U(D - 1.6)));
            double sa = U(pw + 1.6), sb = U(D - cd - 1.6);
            if (Math.Abs(sb - sa) > 2) b.FlatInto(shelf, f, Math.Min(sa, sb), Math.Max(sa, sb));
        }
        var front = b.Upright("Counter front", new V3(fx0, 0, D - T / 2 - 0.4), V3.UnitX, fx1 - fx0, V3.UnitZ);
        front.Outline.Add(Geo.Rect(0, 0.6, front.Len, ch - T));
        foreach (var (t0, t1) in b.Tabs(0, front.Len, 1.2)) b.UpInto(counter, front, t0, t1);
        // The awning, held by the rafters' tabs.
        var aw = new Up { Name = bar ? "Roof" : "Awning", U = V3.UnitX, V = up, O = R(0) + n * (T / 2) - up * over, Len = W, Plain = true };
        b.Ups.Add(aw);
        double av1 = len + over + 0.6;
        aw.Outline.Add(Geo.Rect(0, 0, aw.Len, av1));
        foreach (var f in frames)
        {
            double xc = f.O.X - aw.O.X;
            foreach (var t in new[] { len * 0.25, len * 0.75 })
                aw.Holes.Add(Geo.Rect(xc - (T + m.Fit) / 2, over + t - 1.2 - m.Fit / 2, xc + (T + m.Fit) / 2, over + t + 1.2 + m.Fit / 2));
        }
        if (bar || metal)
        {
            // Boards (or plates) down the roof, a ridge batten along the top.
            for (double x = 2.4 * k; x < aw.Len - 1; x += 2.6 * k) aw.Lines.Add([new(x, 0.4), new(x, av1 - 0.4)]);
            aw.Etched.Add(Geo.Rect(0, av1 - 1.2, aw.Len, av1 - 0.6));
        }
        else
        {
            // Stripes down the awning.
            double sw3 = Math.Max(2.4, aw.Len / Math.Max(3, Math.Round(aw.Len / (3.6 * k))));
            for (double x = 0; x < aw.Len; x += 2 * sw3) aw.Etched.Add(Geo.Rect(x, 0, Math.Min(aw.Len, x + sw3), av1));
        }
        if (b.Glue && !bar)
        {
            // A scalloped valance along the front edge, glued under it.
            double zv = D + over * D / len - T / 2 - 0.3;
            var val = b.Upright("Valance", new V3(0, 0, zv), V3.UnitX, aw.Len, V3.UnitZ);
            val.Glued = val.Plain = true;
            // Its top along the awning's underside, at the valance's front face.
            double vy1 = Hf + (D - (zv + T / 2)) * (Hb - Hf) / D - 0.02, vh = Math.Max(2.4, 3 * k);
            int sc = Math.Max(3, (int)Math.Round(aw.Len / (4 * k)));
            var vp = new PathD { new(0, vy1), new(aw.Len, vy1) };
            for (int i = sc; i >= 0; i--)
            {
                double x = aw.Len * i / sc;
                vp.Add(new PointD(x, vy1 - vh));
                if (i > 0) vp.Add(new PointD(x - aw.Len / sc / 2, vy1 - vh * 0.55));
            }
            val.Outline.Add(vp);
            if (!bar && !metal)
            {
                double sw3 = Math.Max(2.4, aw.Len / Math.Max(3, Math.Round(aw.Len / (3.6 * k))));
                for (double x = 0; x < aw.Len; x += 2 * sw3) val.Etched.Add(Geo.Rect(x, vy1 - vh, Math.Min(aw.Len, x + sw3), vy1));
            }
            val.Lines.Add([new(0.3, vy1 - 0.6), new(aw.Len - 0.3, vy1 - 0.6)]);
        }
        if (bar)
        {
            // Barrel ends on the front, hooped, with a tap in each.
            if (b.Glue)
            {
                int barrels = Math.Max(1, (int)((front.Len - 2) / (7.5 * k)));
                double r = Math.Min((front.Len - 2) / barrels / 2 - 0.6, (ch - T) / 2 - 1);
                var ends = b.Upright("Barrel ends", new V3(fx0, 0, D - 0.4 + T / 2), V3.UnitX, front.Len, V3.UnitZ);
                ends.Glued = ends.Plain = true;
                for (int i = 0; i < barrels; i++)
                {
                    double cx = 1 + (front.Len - 2) * (i + 0.5) / barrels, cy = (ch - T) / 2 + 0.3;
                    ends.Outline.Add(Geo.Circle(cx, cy, r, 0.02));
                    ends.Lines.Add(Geo.Loop(Geo.Circle(cx, cy, r - 0.6, 0.02)));
                    ends.Lines.Add(Geo.Loop(Geo.Circle(cx, cy, r * 0.45, 0.02)));
                    for (double yy = cy - r + 1; yy < cy + r - 0.8; yy += 1.4 * k) { double hw = Math.Sqrt(Math.Max(0, r * r - (yy - cy) * (yy - cy))) - 0.7; if (hw > 0.5) ends.Lines.Add([new(cx - hw, yy), new(cx + hw, yy)]); }
                    ends.Etched.Add(Geo.Rect(cx - 0.4, cy - r * 0.35 - 1.2, cx + 0.4, cy - r * 0.35));
                }
                if (ends.Outline.Count > 1)
                {
                    // Joined into one piece by a rail behind them.
                    ends.Outline.Add(Geo.Rect(1, (ch - T) / 2 + 0.3 - 0.8, front.Len - 1, (ch - T) / 2 + 1.1));
                }
            }
            // A sign hanging from a bracket on the front post.
            var f = frames[1];
            double U(double z) => Build.UOf(f, new V3(0, 0, z), V3.UnitZ, 0);
            double by = Hf - 3.4 * k;
            f.Outline.Add(Geo.Rect(Math.Min(U(D - pw), U(D + 6 * k)), by, Math.Max(U(D - pw), U(D + 6 * k)), by + 1.2));
            double sx0 = Math.Min(U(D + 0.6 * k), U(D + 5.6 * k)), sx1 = Math.Max(U(D + 0.6 * k), U(D + 5.6 * k));
            f.Outline.Add(Geo.Rect((sx0 + sx1) / 2 - 0.5, by - 1.4 * k, (sx0 + sx1) / 2 + 0.5, by + 0.02));
            var sign = Shield(sx0, by - 1.4 * k - 5 * k, sx1, by - 1.4 * k + 0.02);
            f.Outline.Add(sign);
            f.Lines.Add(Geo.Loop(Geo.Offset([sign], -0.4).FirstOrDefault() ?? sign));
            Tankard(f, (sx0 + sx1) / 2, by - 1.4 * k - 2.4 * k, 1.3 * k);
        }
        else if (b.Glue)
        {
            // Crates of produce on the counter, and a basket on the shelf.
            double cw = Math.Min(7 * k, (fx1 - fx0) / 3 - 0.6), cdp = Math.Min(cd - 1.4, 5 * k);
            for (int i = 0; i < 3; i++)
            {
                double x0 = fx0 + 0.6 + i * ((fx1 - fx0 - 1.2) / 3), z0 = D - cd + 0.6;
                var crate = new Flat { Name = "Produce", Y = ch + T, Texture = "none", Glued = true };
                crate.Outline.Add(Build.XZRect(x0, z0, x0 + cw, z0 + cdp));
                crate.Lines.Add(Geo.Loop(Build.XZRect(x0 + 0.5, z0 + 0.5, x0 + cw - 0.5, z0 + cdp - 0.5)));
                // Apples, cabbages and loaves in turn.
                for (double fx = x0 + 1.3; fx < x0 + cw - 0.9; fx += 1.3 * k)
                    for (double fz = z0 + 1.3; fz < z0 + cdp - 0.9; fz += 1.3 * k)
                        if (i == 1) crate.Etched.Add(Geo.Circle(fx, -fz, 0.5 * k, 0.01));
                        else if (i == 0) crate.Lines.Add(Geo.Loop(Geo.Circle(fx, -fz, 0.5 * k, 0.01)));
                        else crate.Lines.Add(Geo.Loop(Rugs.Ellipse(fx, -fz, 0.6 * k, 0.4 * k)));
                b.Flats.Add(crate);
            }
        }
        b.Finish();
        m.Note("Push the counter and shelf through the side frames, the front up into the counter, then press the awning onto the rafters' tabs" + (b.Glue ? ", then glue on the " + (bar ? "barrel ends." : "valance and the produce.") : "."));
    }

    /// <summary>A tankard, scored on a sign.</summary>
    static void Tankard(Up f, double cx, double cy, double r)
    {
        f.Lines.Add(Geo.Loop(Geo.Rect(cx - r * 0.6, cy - r, cx + r * 0.5, cy + r * 0.8)));
        f.Lines.Add([.. Geo.Arc(cx + r * 0.5, cy, r * 0.5, -Math.PI / 2, Math.PI / 2)]);
        f.Lines.Add([new(cx - r * 0.7, cy + r * 0.8), new(cx - r * 0.3, cy + r * 1.15), new(cx + r * 0.1, cy + r * 0.85), new(cx + r * 0.6, cy + r * 1.1)]);
    }

    /// <summary>A notice board: a board on two legs, a little gabled roof, notices pinned on and two crossed feet.</summary>
    static void NoticeBoard(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 40;
        if (W < 14 || D < 8 || H < 20) { m.Bad("The notice board is too small: make it at least 14 wide, 8 deep and 20 high."); return; }
        var b = new Build(m, LookOf(m.Theme) with { Face = "Wood planks (upright)", A = 2.6, B = 30, G = 0.4 }, k);
        double lw = Math.Max(m.SlotW + 2.4, 2.6 * k), bh = H * 0.42, by0 = H * 0.32, roof = Math.Min(5 * k, H * 0.15);
        double cz = D / 2, sw = m.SlotW, footH = Math.Min(4 * k, by0 * 0.5);
        var board = b.Upright("Board", new V3(0, 0, cz), V3.UnitX, W, V3.UnitZ);
        board.Outline.Add(Geo.Rect(0.8, 0, 0.8 + lw, H - roof));
        board.Outline.Add(Geo.Rect(W - 0.8 - lw, 0, W - 0.8, H - roof));
        board.Outline.Add(Geo.Rect(0.8, by0, W - 0.8, by0 + bh));
        board.Outline.Add(new PathD { new(0, H - roof - 1.2), new(W, H - roof - 1.2), new(W, H - roof - 0.02), new(W / 2, H), new(0, H - roof - 0.02) });
        board.Outline.Add(Geo.Rect(0.8, H - roof - 1.6, W - 0.8, H - roof + 0.01));
        board.Etched.AddRange(Fronts.Outline(0.8 + lw, by0 + 0.6, W - 0.8 - lw, by0 + bh - 0.6, 0.4));
        // The feet: crossed through the legs from below.
        foreach (var x in new[] { 0.8 + lw / 2, W - 0.8 - lw / 2 })
        {
            board.Holes.Add(Geo.Rect(x - sw / 2, -1, x + sw / 2, footH / 2));
            var foot = b.Upright("Foot", new V3(x, 0, 0), V3.UnitZ, D, V3.UnitX);
            foot.Plain = true;
            foot.Outline.Add(new PathD { new(0, 0), new(D, 0), new(D, 1.2), new(D / 2 + lw / 2, footH), new(D / 2 - lw / 2, footH), new(0, 1.2) });
            foot.Holes.Add(Geo.Rect(D / 2 - sw / 2, footH / 2, D / 2 + sw / 2, footH + 1));
        }
        b.Finish();
        if (b.Glue)
        {
            // The notices, pinned on in a jumble, and the roof's boards over the gable.
            var rnd = m.Rnd;
            double x = 0.8 + lw + 1.0;
            int i = 0;
            while (x < W - 0.8 - lw - 4 * k)
            {
                double nw = (3.6 + rnd.NextDouble() * 2) * k, nh = (4 + rnd.NextDouble() * 3) * k, y = by0 + 1.2 + rnd.NextDouble() * Math.Max(0, bh - nh - 2.4);
                nw = Math.Min(nw, W - 0.8 - lw - 1 - x);
                if (nw < 3) break;
                var note = new PathD { new(x, y), new(x + nw, y + 0.3), new(x + nw - 0.2, y + nh), new(x - 0.1, y + nh - 0.2) };
                var e = new PathsD();
                var l = new List<PathD> { WoodFace.Dot(x + nw / 2, y + nh - 0.8, 0.25) };
                for (double ly = y + nh - 1.8; ly > y + 0.8; ly -= 0.9) l.Add([new(x + 0.6, ly), new(x + 0.6 + (nw - 1.2) * (0.5 + 0.5 * rnd.NextDouble()), ly + 0.05)]);
                if (i == 0) { e.Add(Fronts.Star(x + nw / 2, y + nh * 0.45, nw * 0.25, nw * 0.1, 5)); }
                m.Add($"Notice {i + 1}", [note], [], [new Pose(new V3(0, 0, cz + T), V3.UnitX, V3.UnitY)], grainAlongU: false, face: [], etched: e, lines: l).Glue();
                x += nw + 0.8;
                i++;
            }
            foreach (int s in new[] { -1, 1 })
            {
                double slope = Math.Atan2(roof - 0.02, W / 2), run = Math.Sqrt(W * W / 4 + roof * roof) + 1.2;
                var dir = new V3(s * Math.Cos(slope), -Math.Sin(slope), 0);
                var nrm = new V3(s * Math.Sin(slope), Math.Cos(slope), 0);
                var pose = s > 0
                    ? new Pose(new V3(W / 2, H, cz - D * 0.35) + nrm * (T / 2), V3.UnitZ, dir)
                    : new Pose(new V3(W / 2, H, cz + D * 0.35) + nrm * (T / 2), -V3.UnitZ, dir);
                var shingle = new PathsD { Geo.Rect(0, 0, D * 0.7, run) };
                var sl = new List<PathD>();
                for (double v = 1.4 * k; v < run - 0.4; v += 1.6 * k) sl.Add([new(0.3, v), new(D * 0.7 - 0.3, v)]);
                m.Add("Roof board", shingle, [], [pose], grainAlongU: true, face: [], lines: sl).Glue();
            }
        }
        m.Note("Push the feet up onto the legs" + (b.Glue ? ", glue on the roof boards over the gable and pin the notices on the board." : "."));
    }
}
