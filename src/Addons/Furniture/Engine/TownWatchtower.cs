using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

public static partial class TownWalls
{
    // ---- the watchtower ----

    /// <summary>
    /// A square wooden watchtower: four leg frames (legs, girts and cross braces, a ladder up the front
    /// one) tied by a brace ring halfway up and carrying a planked platform with a trapdoor; on it a
    /// boarded parapet all round, gable ends with corner posts and a tie beam (an alarm bell hanging from
    /// the front one, a searchlight in sci-fi), and a gabled roof of two slopes held by tabs from the
    /// gables, shingled, thatched, slated or plated to suit the theme, with a ridge board glued along the top.
    /// </summary>
    static void Watchtower(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 88;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool glue = !m.PushFit, metal = theme == TerrainThemes.SciFi;
        if (W < 26 || D < 26) { m.Bad("The watchtower is too small: make it at least 26 × 26 mm."); return; }
        if (H < 60) { m.Bad("The watchtower is too low: make it at least 60 mm high."); return; }
        var look = LookOf(theme) with { Face = metal ? "Metal plates" : "Wood planks (upright)", A = metal ? 8 : 2.6, B = metal ? 6 : 30, G = 0.4 };
        var b = new Build(m, look, k);
        double lp = 1.4, x0 = Math.Max(Math.Round(3.0 * k, 1), Math.Round(lp + T + 1.4, 1)), yd = Math.Round(H * 0.5, 1), pw = Math.Max(2.6, 2.8 * k), gh = Math.Max(1.6, 1.8 * k), bw = Math.Max(1.3, 1.5 * k);
        double yMid = Math.Round(yd * 0.48, 1), g0 = Math.Round(4.0 * k, 1);
        // The platform, overhanging the legs all round.
        var deck = new Flat { Name = "Platform", Y = yd, Texture = metal ? "Metal plates" : "Wood planks (across)", A = metal ? 10 : 3.0, B = metal ? 8 : 30, G = 0.4 };
        deck.Outline.Add(Build.XZRect(0, 0, W, D));
        b.Flats.Add(deck);
        // The brace ring halfway up, inside the legs.
        double ri = x0 + T + 0.02, rb = Math.Max(3.2, 3.5 * k);
        var ring = new Flat { Name = "Brace ring", Y = yMid, Texture = deck.Texture, A = deck.A, B = deck.B, G = 0.4 };
        ring.Outline.Add(Build.XZRect(ri, ri, W - ri, D - ri));
        ring.Holes.Add(Build.XZRect(ri + rb, ri + rb, W - ri - rb, D - ri - rb));
        b.Flats.Add(ring);
        // The four leg frames: the front and back across, the sides between them.
        double lw = Math.Max(6.0, 6.5 * k), rw = Math.Max(1.3, 1.4 * k), rp = Math.Max(3.2, 3.6 * k);
        var frames = new[]
        {
            (b.Upright("Leg frame (front)", new V3(x0, 0, D - x0 - T / 2), V3.UnitX, W - 2 * x0, V3.UnitZ), 0),
            (b.Upright("Leg frame (back)", new V3(x0, 0, x0 + T / 2), V3.UnitX, W - 2 * x0, -V3.UnitZ), 1),
            (b.Upright("Leg frame (side)", new V3(x0 + T / 2, 0, x0 + T), V3.UnitZ, D - 2 * x0 - 2 * T, -V3.UnitX), 2),
            (b.Upright("Leg frame (side)", new V3(W - x0 - T / 2, 0, x0 + T), V3.UnitZ, D - 2 * x0 - 2 * T, V3.UnitX), 3),
        };
        foreach (var (f, i) in frames)
        {
            double L = f.Len, p = i < 2 ? pw : Math.Max(1.4, pw - T), yt = yd - T;
            f.Outline.Add(Geo.Rect(0, 0, L, yt));
            // The girts: one low down, one through which the brace ring passes, one under the platform.
            var bands = new List<(double Y0, double Y1)> { (g0, g0 + gh), (yMid - T - 1.2, yMid + 1.2), (yt - gh, yt) };
            var cuts = new PathsD();
            // Below the low girt, open between the legs.
            cuts.Add(Geo.Rect(p, -1, L - p, g0));
            // The bays, each crossed by braces (one way only beside the ladder).
            var spans = i == 0
                ? new[] { (p, L / 2 - lw / 2, 1), (L / 2 + lw / 2, L - p, -1) }
                : new[] { (p, L - p, 0) };
            for (int t = 0; t < 2; t++)
            {
                double y0 = bands[t].Y1, y1 = bands[t + 1].Y0;
                foreach (var (a0, a1, dir) in spans)
                {
                    if (a1 - a0 < 3 || y1 - y0 < 3) continue;
                    var bay = new PathsD { Geo.Rect(a0, y0, a1, y1) };
                    double dx = a1 - a0, dy = y1 - y0, dl = Math.Sqrt(dx * dx + dy * dy), ex = dx / dl * bw, ey = dy / dl * bw;
                    var up = Geo.Strip(new(a0 - ex, y0 - ey), new(a1 + ex, y1 + ey), bw);
                    var down = Geo.Strip(new(a0 - ex, y1 + ey), new(a1 + ex, y0 - ey), bw);
                    var braces = dir == 0 ? new PathsD { up, down } : new PathsD { dir > 0 ? up : down };
                    cuts.AddRange(Geo.Subtract(bay, Geo.Union(braces)));
                    f.Lines.Add(WoodFace.Dot((a0 + a1) / 2, (y0 + y1) / 2, 0.22));
                }
            }
            if (i == 0)
            {
                // The ladder up the middle of the front: two rails, rungs between them.
                double l0 = L / 2 - lw / 2 + rw, l1 = L / 2 + lw / 2 - rw;
                var gaps = new PathsD { Geo.Rect(l0, -1, l1, yt - gh) };
                var solid = new PathsD();
                for (double y = 2.6 * k; y < yt - gh - 1.6; y += rp) solid.Add(Geo.Rect(l0 - 0.1, y, l1 + 0.1, y + 1.2));
                foreach (var (y0, y1) in bands) solid.Add(Geo.Rect(l0 - 0.1, y0, l1 + 0.1, y1));
                cuts.AddRange(Geo.Subtract(gaps, Geo.Union(solid)));
                f.Lines.Add([new(l0 - rw / 2, 0.4), new(l0 - rw / 2, yt - 0.4)]);
                f.Lines.Add([new(l1 + rw / 2, 0.4), new(l1 + rw / 2, yt - 0.4)]);
            }
            f.Holes.AddRange(cuts);
            // Pegs where the girts cross the legs.
            foreach (var (y0, y1) in bands)
                foreach (var u in new[] { p / 2, L - p / 2 }) f.Lines.Add(WoodFace.Dot(u, (y0 + y1) / 2, 0.22));
            foreach (var (t0, t1) in b.Tabs(0, L, i < 2 ? T + 1.4 : 1.4)) b.UpInto(deck, f, t0, t1);
            // The brace ring's tabs through the girt.
            double tw = Math.Max(2.4, 3.0 * k);
            var ringTabs = i < 2 ? new[] { (p + 1.6, p + 1.6 + tw), (L - p - 1.6 - tw, L - p - 1.6) } : new[] { (L / 2 - tw / 2, L / 2 + tw / 2) };
            foreach (var (t0, t1) in ringTabs) b.FlatInto(ring, f, t0, t1);
        }
        // The trapdoor in the platform behind the ladder.
        {
            double zf = D - x0 - T - 1.2, zb = zf - Math.Max(5.0, 6.0 * k), xa = W / 2 - lw / 2, xb = W / 2 + lw / 2;
            deck.Lines.Add(Geo.Loop(Build.XZRect(xa, zb, xb, zf)));
            foreach (var x in new[] { xa + 1.2, xb - 2.6 }) deck.Etched.Add(Build.XZRect(x, zb + 0.3, x + 1.4, zb + 0.9));
            deck.Lines.Add(Geo.Loop(Geo.Circle(W / 2, -(zf - 1.2), 0.5, 0.02)));
        }
        // On the platform: the gable ends front and back, the parapets along the sides between them.
        double G = W - 2 * lp, rh = Math.Round(12 * k, 1), pw2 = Math.Max(2.4, 2.6 * k), tb = Math.Max(1.8, 2.0 * k);
        double ye = yd + Math.Round(29 * k, 1), half = W / 2 - lp;
        double yr0 = H - 1.25 * T - 0.6, pitch = Math.Atan2(yr0 - ye, half), ca = Math.Cos(pitch), sa = Math.Sin(pitch);
        double yr = ye + half * Math.Tan(pitch), rlen = half / ca;
        if (yr - ye < 6) { m.Bad("The watchtower is too low for its roof: make it taller."); return; }
        var gables = new[]
        {
            (b.Upright("Gable (front)", new V3(lp, 0, D - lp - T / 2), V3.UnitX, G, V3.UnitZ), true),
            (b.Upright("Gable (back)", new V3(lp, 0, lp + T / 2), V3.UnitX, G, -V3.UnitZ), false),
        };
        foreach (var (g, front) in gables)
        {
            g.Outline.Add(Geo.Rect(0, yd, G, yd + rh));
            g.Outline.Add(Geo.Rect(0, yd, pw2, ye));
            g.Outline.Add(Geo.Rect(G - pw2, yd, G, ye));
            g.Outline.Add(Geo.Rect(0, ye - tb, G, ye + 0.02));
            g.Outline.Add(new PathD { new(0, ye), new(G, ye), new(G / 2, yr) });
            g.Lines.Add([new(0.3, yd + rh - 0.7), new(G - 0.3, yd + rh - 0.7)]);
            // A king post and struts scored on the gable, a vent through the back one.
            g.Lines.Add([new(G / 2, ye + 0.4), new(G / 2, yr - 1.2)]);
            g.Lines.Add([new(G / 2, ye + 0.6), new(G * 0.28, ye + (yr - ye) * 0.44)]);
            g.Lines.Add([new(G / 2, ye + 0.6), new(G * 0.72, ye + (yr - ye) * 0.44)]);
            foreach (var (t0, t1) in b.Tabs(0, G, T + 1.4)) b.DownInto(deck, g, t0, t1);
            if (front)
            {
                // The theme's symbol on the parapet, a bell (or a searchlight) hanging from the tie beam.
                Cabinets.Symbol(Fronts.Look(m), theme, G / 2, yd + rh / 2, Math.Min(rh * 0.3, 3 * k), g.Etched, g.Lines);
                double yt = ye - tb, c = G / 2;
                if (metal)
                {
                    g.Outline.Add(Geo.Rect(c - 0.6, yt - 1.4, c + 0.6, yt + 0.02));
                    g.Outline.Add(Fronts.Chamfered(c - 2.4 * k, yt - 1.4 - 3.4 * k, c + 2.4 * k, yt - 1.2, 0.6));
                    g.Etched.Add(Geo.Rect(c - 1.8 * k, yt - 1.4 - 3.0 * k, c + 1.8 * k, yt - 1.4 - 1.6 * k));
                }
                else
                {
                    double bt = yt - 1.2, bs = Math.Max(1.0, k);
                    g.Outline.Add(Geo.Rect(c - 1.6 * bs, bt - 0.02, c + 1.6 * bs, yt + 0.02));
                    g.Outline.Add(Lathe(c, [(2.7 * bs, bt - 5.0 * bs), (2.7 * bs, bt - 4.4 * bs), (1.9 * bs, bt - 3.0 * bs), (1.6 * bs, bt - 1.2 * bs), (1.2 * bs, bt)]));
                    g.Outline.Add(Geo.Circle(c, bt - 5.1 * bs, 0.75, 0.02));
                    g.Lines.Add([new(c - 2.2 * bs, bt - 4.2 * bs), new(c + 2.2 * bs, bt - 4.2 * bs)]);
                    g.Lines.Add([new(c - 1.6 * bs, bt - 1.6 * bs), new(c + 1.6 * bs, bt - 1.6 * bs)]);
                }
            }
            else g.Holes.Add(Geo.Circle(G / 2, ye + (yr - ye) * 0.38, Math.Min(1.6 * k, (yr - ye) * 0.15), 0.02));
        }
        var sideP = new[]
        {
            b.Upright("Parapet", new V3(lp + T / 2, 0, lp + T), V3.UnitZ, D - 2 * lp - 2 * T, -V3.UnitX),
            b.Upright("Parapet", new V3(W - lp - T / 2, 0, lp + T), V3.UnitZ, D - 2 * lp - 2 * T, V3.UnitX),
        };
        foreach (var p in sideP)
        {
            p.Outline.Add(Geo.Rect(0, yd, p.Len, yd + rh));
            p.Lines.Add([new(0.3, yd + rh - 0.7), new(p.Len - 0.3, yd + rh - 0.7)]);
            if (metal) p.Etched.AddRange(Hazard(0.3, yd + rh - 2.2, p.Len - 0.3, yd + rh - 1.2, 0.8));
            foreach (var (t0, t1) in b.Tabs(0, p.Len, 1.2)) b.DownInto(deck, p, t0, t1);
        }
        // The roof: two slopes on the gables' rafters, overhanging at the eaves and the ends.
        double over = Math.Max(2.4, 2.6 * k), ovz = Math.Max(2.0, 2.2 * k), tEnd = rlen - (T / 2 + 0.05) / ca, len = D - 2 * lp + 2 * ovz;
        string roof = theme switch
        {
            TerrainThemes.SciFi => "plates",
            TerrainThemes.Cave or TerrainThemes.Desert => "thatch",
            TerrainThemes.Dungeon => "planks",
            TerrainThemes.Gothic => "slates",
            _ => "shingles",
        };
        foreach (var left in new[] { true, false })
        {
            var V = left ? new V3(ca, sa, 0) : new V3(-ca, sa, 0);
            var N = left ? new V3(-sa, ca, 0) : new V3(sa, ca, 0);
            var U = left ? V3.UnitZ : -V3.UnitZ;
            var eave = new V3(left ? lp : W - lp, ye, left ? lp - ovz : D - lp + ovz);
            var s = new Up { Name = "Roof", U = U, V = V, O = eave + N * (T / 2) - V * over, Len = len, Plain = true };
            b.Ups.Add(s);
            double vEnd = over + tEnd;
            s.Outline.Add(Geo.Rect(0, 0, len, vEnd));
            // Tabs up from each gable's rafter into the slope.
            foreach (var (g, _) in gables)
            {
                double zg = g.O.Z, us = (new V3(0, 0, zg) - new V3(0, 0, s.O.Z)).Dot(U);
                foreach (var t in new[] { rlen * 0.3, rlen * 0.72 })
                {
                    V3 R(double q) => new V3(left ? lp + q * ca : W - lp - q * ca, ye + q * sa, zg);
                    V3 p0 = R(t - 1.2) - N * 0.02, p1 = R(t + 1.2) - N * 0.02, p2 = R(t + 1.2) + N * T, p3 = R(t - 1.2) + N * T;
                    g.Outline.Add([.. new[] { p0, p1, p2, p3 }.Select(q => new PointD((q - g.O).Dot(g.U), q.Y))]);
                    s.Holes.Add(Geo.Rect(us - (T + F) / 2, over + t - 1.2 - F / 2, us + (T + F) / 2, over + t + 1.2 + F / 2));
                }
            }
            switch (roof)
            {
                case "plates":
                    for (double u = 1.0; u < len - 0.5; u += 1.0) s.Lines.Add([new(u, 0.3), new(u, vEnd - 0.3)]);
                    s.Etched.AddRange(Hazard(0.3, 0.3, len - 0.3, 1.3, 0.9));
                    break;
                case "thatch":
                    for (double u = 0.5; u < len - 0.3; u += 0.7)
                    {
                        double v0 = 0.3 + m.Rnd.NextDouble() * 1.2;
                        s.Lines.Add([new(u, v0), new(u + (m.Rnd.NextDouble() - 0.5) * 0.4, Math.Min(vEnd - 0.3, v0 + 2.5 + m.Rnd.NextDouble() * (vEnd * 0.5)))]);
                    }
                    for (double v = 3.2 * k; v < vEnd - 1; v += 3.2 * k) s.Lines.Add([new(0.3, v), new(len - 0.3, v)]);
                    break;
                case "planks":
                    for (double u = 2.6 * k; u < len - 1; u += 2.6 * k) s.Lines.Add([new(u, 0.3), new(u, vEnd - 0.3)]);
                    for (double u = 1.3 * k; u < len - 1; u += 2.6 * k) s.Lines.Add(WoodFace.Dot(u, vEnd * 0.5, 0.2));
                    break;
                default:
                {
                    // Rows of shingles (smaller slates in gothic), every other row staggered.
                    double row = roof == "slates" ? 1.6 * k : 2.2 * k, wide = roof == "slates" ? 1.8 * k : 2.6 * k;
                    int r = 0;
                    for (double v = row; v < vEnd - 0.4; v += row, r++)
                    {
                        s.Lines.Add([new(0.2, v), new(len - 0.2, v)]);
                        for (double u = (r % 2) * wide / 2 + wide; u < len - 0.4; u += wide) s.Lines.Add([new(u, v - row + 0.2), new(u, v - 0.1)]);
                    }
                    break;
                }
            }
        }
        if (glue)
        {
            // The ridge board along the top (with spikes at its ends in the gothic and dungeon themes).
            var ridge = b.Upright("Ridge board", new V3(W / 2, 0, lp - ovz), V3.UnitZ, len, V3.UnitX);
            ridge.Glued = true;
            double rt = yr + T / ca + 0.8;
            ridge.Outline.Add(Geo.Rect(0, yr, len, rt));
            if (theme is TerrainThemes.Gothic or TerrainThemes.Dungeon)
                foreach (var u in new[] { 0.0, len - 1.8 * k })
                    ridge.Outline.Add(new PathD { new(u, rt - 0.02), new(u + 1.8 * k, rt - 0.02), new(u + 0.9 * k, rt + 2.4 * k) });
        }
        b.Finish();
        m.Note("Push the leg frames together through the brace ring and up into the platform, the gables and parapets down into it, then press the roof slopes onto the gables' tabs" + (glue ? " and glue the ridge board along the top." : "."));
    }
}
