using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>Pieces for a town square: the gallows and the fountain.</summary>
public static partial class TownWalls
{
    // ---- the gallows ----

    /// <summary>
    /// A gallows: a planked platform on braced legs (skirts with a sill, legs and a diagonal brace in each
    /// bay, joists under the planks), steps up the front, railings round three sides, a trapdoor etched under
    /// the drop, and the frame standing in the platform: two posts, a beam with knee braces, and a noose
    /// on its rope. The dungeon and gothic ones also hang an iron gibbet cage from the beam on a chain.
    /// </summary>
    public static void Gallows(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 60;
        if (W < 30 || D < 22) { m.Bad("The gallows is too small: make it at least 30 × 22 mm."); return; }
        double deck = Math.Round(Geo.Clamp(H * 0.22, 8, 16), 1);
        if (H < deck + 32) { m.Bad($"The gallows is too low for its frame: make it at least {deck + 32:0.#} mm high."); return; }
        var look = LookOf(m.Theme);
        bool metal = look.Top == Top.Armour, cage = m.Theme is TerrainThemes.Dungeon or TerrainThemes.Gothic;
        var b = new Build(m, look with { Face = metal ? "Metal plates" : "Wood planks (upright)", A = metal ? 8 : 2.6, B = metal ? 6 : 30, G = 0.4 }, k);
        double stepD = Math.Round(Math.Min(9 * k, D * 0.28), 1), D0 = D - stepD, lip = 1.2;
        // The platform on its skirts.
        var top = new Flat { Name = "Platform", Y = deck, Texture = metal ? "Metal plates" : "Wood planks (across)", A = metal ? 10 : 3.2, B = metal ? 8 : 30, G = 0.4 };
        top.Outline.Add(Build.XZRect(0, 0, W, D0));
        b.Flats.Add(top);
        double x0 = lip, x1 = W - lip, z0 = lip, z1 = D0 - lip;
        // Where the railings stand: a slot's width and a bit in from the skirts.
        double gap = T + 1.4, xl = x0 + T + gap + T / 2, xr = x1 - T - gap - T / 2, zb = z0 + T + gap + T / 2, zf = z1 - T - gap - T / 2;
        // The steps come up at the front, just inside the left railing.
        double sw2 = Math.Round(Math.Min(12 * k, (x1 - x0) * 0.32), 1), sx0 = xl + T / 2 + 1.0, sx1 = sx0 + sw2;
        var front = b.Upright("Front skirt", new V3(x0, 0, z1 - T / 2), V3.UnitX, x1 - x0, V3.UnitZ);
        var back = b.Upright("Back skirt", new V3(x0, 0, z0 + T / 2), V3.UnitX, x1 - x0, -V3.UnitZ);
        var left = b.Upright("Side skirt", new V3(x0 + T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, -V3.UnitX);
        var right = b.Upright("Side skirt", new V3(x1 - T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
        double lw = Math.Max(2.2, 2.4 * k), sill = Math.Max(1.2, 1.2 * k), yt = deck - T - Math.Max(1.6, 1.8 * k), bw = Math.Max(1.4, 1.5 * k);
        foreach (var f in new[] { front, back, left, right })
        {
            f.Outline.Add(Geo.Rect(0, 0, f.Len, deck - T));
            foreach (var (t0, t1) in b.Tabs(0, f.Len, f == left || f == right ? 1.2 : T + 1.4)) b.UpInto(top, f, t0, t1);
            // Legs, a sill along the ground and a diagonal brace across each bay (boarded behind the steps).
            if (yt - sill < 3) continue;
            int bays = Math.Max(1, (int)Math.Round((f.Len - lw) / (13 * k)));
            double pitch = (f.Len - lw) / bays;
            for (int i = 0; i < bays; i++)
            {
                double a = i * pitch + lw, c = (i + 1) * pitch;
                if (c - a < 3) continue;
                if (f == front && a < sx1 - x0 + 1 && c > sx0 - x0 - 1) continue;
                var bay = new PathsD { Geo.Rect(a, sill, c, yt) };
                // Run on past the corners, so no sliver of the bay is left beside its ends.
                double dx = c - a, dy = yt - sill, dl = Math.Sqrt(dx * dx + dy * dy), ex = dx / dl * bw, ey = dy / dl * bw;
                var brace = i % 2 == 0 ? Geo.Strip(new(a - ex, sill - ey), new(c + ex, yt + ey), bw) : Geo.Strip(new(a - ex, yt + ey), new(c + ex, sill - ey), bw);
                f.Holes.AddRange(Geo.Subtract(bay, [brace]));
                f.Lines.Add(WoodFace.Dot((a + c) / 2, (sill + yt) / 2, 0.22));
            }
        }
        // The railings: round the sides and the back, and along the front either side of the steps.
        // Room between the back and front railings for the frame's slots (and the trapdoor).
        double need = (T + F + 1.2) / 0.42;
        if (zf - zb < need) { m.Bad($"The gallows is too shallow for its railings and frame: make it at least {Math.Ceiling(D + need - (zf - zb)):0} mm deep, or the sheet thinner."); return; }
        double rh = Math.Round(Math.Max(6, 8 * k), 1), rp = Math.Max(1.6, 1.8 * k);
        void Rail(V3 a, V3 dir, double len, V3 facing, bool tabStart, bool tabEnd)
        {
            var r = b.Upright("Railing", a, dir, len, facing);
            r.Plain = true;
            double y0 = deck, y1 = deck + rh, mid = deck + rh * 0.5;
            r.Outline.Add(Geo.Rect(0, y0, r.Len, y1));
            int n = Math.Max(2, (int)Math.Round(r.Len / (10 * k)));
            var posts = Enumerable.Range(0, n + 1).Select(i => i * (r.Len - rp) / n).ToList();
            for (int i = 0; i < n; i++)
            {
                double a0 = posts[i] + rp, a1 = posts[i + 1];
                if (a1 - a0 < 1.2) continue;
                r.Holes.Add(Geo.Rect(a0, y0 - 1, a1, mid - 0.55));
                r.Holes.Add(Geo.Rect(a0, mid + 0.55, a1, y1 - 1.3));
                // Pegs where the rails meet the posts.
                r.Lines.Add(WoodFace.Dot(a0 - rp / 2, y1 - 0.65, 0.18));
            }
            for (int i = 0; i <= n; i++)
            {
                double p = posts[i];
                // A cap on each post.
                r.Outline.Add(Geo.Rect(p, y1 - 0.02, p + rp, y1 + 1.0));
                r.Lines.Add([new(p + rp / 2, y0 + 0.4), new(p + rp / 2, y1 - 1.6)]);
                bool tab = (i > 0 && i < n) || (i == 0 && tabStart) || (i == n && tabEnd);
                if (tab) b.DownInto(top, r, p + 0.2, p + rp - 0.2);
            }
            r.Lines.Add([new(0.2, y1 - 0.65), new(r.Len - 0.2, y1 - 0.65)]);
        }
        Rail(new V3(xl + T / 2 + 0.02, 0, zb), V3.UnitX, xr - xl - T - 0.04, -V3.UnitZ, false, false);
        Rail(new V3(xl, 0, zb - T / 2), V3.UnitZ, zf - zb + T, -V3.UnitX, true, true);
        Rail(new V3(xr, 0, zb - T / 2), V3.UnitZ, zf - zb + T, V3.UnitX, true, true);
        if (xr - T / 2 - (sx1 + 1) > 8) Rail(new V3(sx1 + 1, 0, zf), V3.UnitX, xr - T / 2 - 0.02 - (sx1 + 1), V3.UnitZ, true, false);
        if (sx0 - 1 - (xl + T / 2) > 6) Rail(new V3(xl + T / 2 + 0.02, 0, zf), V3.UnitX, sx0 - 1 - (xl + T / 2 + 0.02), V3.UnitZ, false, true);
        // The frame: two posts and a beam with knee braces, standing in the platform.
        double zc = Math.Round(zb + (zf - zb) * 0.42, 1), pw = Math.Max(2.6, 2.8 * k), bh = Math.Max(2.6, 2.8 * k);
        double xa = xl + T / 2 + 1.6, xb = xr - T / 2 - 1.6;
        var frame = b.Upright("Gallows frame", new V3(xa, 0, zc), V3.UnitX, xb - xa, V3.UnitZ);
        frame.Plain = !metal;
        double L = frame.Len, yb = H - bh, ov = Math.Min(3 * k, 4);
        if (L < 2 * pw + 12) { m.Bad("The gallows is too narrow for its frame: make it wider."); return; }
        foreach (var p in new[] { 0.0, L - pw })
        {
            frame.Outline.Add(Geo.Rect(p, deck, p + pw, yb + 0.02));
            // A block at the foot of each post.
            frame.Outline.Add(Geo.Rect(p - 0.7, deck, p + pw + 0.7, deck + Math.Max(1.6, 1.8 * k)));
            frame.Lines.Add([new(p + pw * 0.35, deck + 2.4), new(p + pw * 0.35, yb - 0.6)]);
            frame.Lines.Add([new(p + pw * 0.7, deck + 3.4), new(p + pw * 0.7, yb - 1.4)]);
            b.DownInto(top, frame, p + 0.3, p + pw - 0.3);
        }
        // The beam, its ends chamfered underneath.
        frame.Outline.Add(new PathD { new(-ov, yb + bh * 0.45), new(-ov + bh * 0.5, yb), new(L + ov - bh * 0.5, yb), new(L + ov, yb + bh * 0.45), new(L + ov, H), new(-ov, H) });
        frame.Lines.Add([new(-ov + 0.5, yb + bh * 0.62), new(L + ov - 0.5, yb + bh * 0.62)]);
        double kb = Math.Min(6 * k, (L - 2 * pw) * 0.24), kw = Math.Max(1.4, 1.5 * k);
        frame.Outline.Add(Geo.Strip(new(pw - 0.4, yb - kb), new(pw + kb, yb + 0.4), kw));
        frame.Outline.Add(Geo.Strip(new(L - pw + 0.4, yb - kb), new(L - pw - kb, yb + 0.4), kw));
        foreach (var (u, y) in new[] { (pw / 2, yb + bh / 2), (L - pw / 2, yb + bh / 2), (pw + kb * 0.3, yb - kb * 0.3 + 0.2), (L - pw - kb * 0.3, yb - kb * 0.3 + 0.2) })
            frame.Lines.Add(WoodFace.Dot(u, y, 0.25));
        // The noose: a rope over the beam, the coils of the knot, and the loop.
        double un = cage ? L * 0.38 : L / 2, rl = Math.Max(2.0, 2.2 * k), rlIn = rl - 1.2;
        double yl = Math.Max(deck + rh + rl + 1.5, deck + 24 * k), kh = Math.Max(2.6, 3.2 * k), ykt = yl + rl - 0.4 + kh;
        frame.Outline.Add(Geo.Rect(un - 0.6, ykt - 0.02, un + 0.6, yb + 0.02));
        frame.Outline.Add(WoodFace.RoundRect(un - 1.0, yl + rl - 0.6, un + 1.0, ykt, 0.3));
        frame.Outline.Add(Geo.Circle(un, yl, rl, 0.02));
        frame.Holes.Add(Geo.Circle(un, yl, rlIn, 0.02));
        for (double y = yl + rl; y < ykt - 0.3; y += 0.55) frame.Lines.Add([new(un - 0.9, y), new(un + 0.9, y + 0.2)]);
        frame.Lines.Add([new(un - 0.6, yb), new(un - 0.6, H)]);
        frame.Lines.Add([new(un + 0.6, yb), new(un + 0.6, H)]);
        frame.Lines.Add([new(un - 0.6, H - 0.6), new(un + 0.6, yb + 0.6)]);
        if (cage)
        {
            // An iron gibbet cage on a chain: bars, bands and a domed top with a ring.
            double uc = L * 0.62, cw = Math.Max(5.6, 6.4 * k), ch = Math.Max(8, 10 * k), yct = yb - Math.Max(4, 5 * k), ycb = yct - ch;
            if (ycb > deck + rh + 1 && uc + cw / 2 < L - pw - 0.8)
            {
                frame.Outline.Add(Geo.Rect(uc - 0.6, yct - 0.02, uc + 0.6, yb + 0.02));
                for (double y = yct + 0.4; y < yb - 0.8; y += 1.4) frame.Lines.Add(Geo.Loop(Rugs.Ellipse(uc, y + 0.5, 0.35, 0.6)));
                double dome = cw * 0.4;
                var body = new PathD { new(uc - cw / 2, ycb + 0.6), new(uc - cw / 2 + 0.6, ycb), new(uc + cw / 2 - 0.6, ycb), new(uc + cw / 2, ycb + 0.6), new(uc + cw / 2, yct - dome) };
                for (int i = 1; i < 12; i++) { double t = i / 12.0 * Math.PI; body.Add(new PointD(uc + Math.Cos(t) * cw / 2, yct - dome + Math.Sin(t) * dome)); }
                body.Add(new PointD(uc - cw / 2, yct - dome));
                frame.Outline.Add(body);
                frame.Holes.AddRange(Fronts.Bars(uc - cw / 2 + 1.1, ycb + 1.2, uc + cw / 2 - 1.1, yct - dome - 0.2, 1.0, 1.1, frame.Lines));
                frame.Lines.Add([new(uc, yct - dome + 0.6), new(uc, yct - 0.6)]);
                frame.Lines.Add([.. Geo.Arc(uc, yct - dome, dome * 0.6, 0.3, Math.PI - 0.3)]);
            }
        }
        // The trapdoor under the drop, in two leaves, with its hinges and a ring.
        double tx = xa + un, tw = Math.Min(4.5 * k, un - pw - 1.5), td = Math.Min(4 * k, (zf - zb) / 2 - T);
        if (tw > 2 && td > 2)
        {
            top.Lines.Add(Geo.Loop(Build.XZRect(tx - tw, zc - td, tx + tw, zc + td)));
            top.Lines.Add([new(tx, -(zc - td)), new(tx, -(zc + td))]);
            // Strap hinges at the outer edge of each leaf.
            foreach (var s in new[] { -1.0, 1.0 })
                foreach (var z in new[] { zc - td * 0.55, zc + td * 0.55 })
                    top.Etched.Add(Build.XZRect(Math.Min(tx + s * tw, tx + s * (tw - 1.8)), z - 0.35, Math.Max(tx + s * tw, tx + s * (tw - 1.8)), z + 0.35));
            top.Lines.Add(Geo.Loop(Geo.Circle(tx - tw * 0.4, -(zc + td * 0.6), 0.45, 0.02)));
        }
        // Joists across under the platform, clear of the frame's posts.
        double jEdge = zb - (z0 + T) + T / 2 + F + 1.2;
        int joists = Math.Max(1, (int)((x1 - x0) / (14 * k)));
        for (int i = 1; i <= joists; i++)
        {
            double x = x0 + (x1 - x0) * i / (joists + 1);
            if (Math.Abs(x - (xa + pw / 2)) < pw / 2 + T + 1.5 || Math.Abs(x - (xb - pw / 2)) < pw / 2 + T + 1.5) continue;
            var j = b.Upright("Joist", new V3(x, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
            j.Plain = true;
            j.Outline.Add(Geo.Rect(0.1, 0, j.Len - 0.1, deck - T));
            foreach (var (t0, t1) in b.Tabs(0.1, j.Len - 0.1, jEdge)) b.UpInto(top, j, t0, t1);
        }
        // The steps: treads with their tabs through the front skirt, each on a riser.
        int steps = Math.Max(2, (int)Math.Floor(deck / Math.Max(3.4 * k, T + 1.1)));
        double rise = deck / steps, run = (D - z1) / steps;
        Flat? below = null;
        for (int s = 1; s < steps; s++)
        {
            double y = rise * s, zfr = D - (s - 1) * run;
            var tr = new Flat { Name = $"Step {s}", Y = y, Texture = top.Texture, A = top.A, B = top.B, G = top.G };
            tr.Outline.Add(Build.XZRect(sx0, z1, sx1, zfr));
            b.Flats.Add(tr);
            double ua = Build.UOf(front, new V3(sx0 + 2, 0, 0), V3.UnitX, 0), ub = Build.UOf(front, new V3(sx1 - 2, 0, 0), V3.UnitX, 0);
            b.FlatInto(tr, front, Math.Min(ua, ub), Math.Max(ua, ub));
            var riser = b.Upright($"Riser {s}", new V3(sx0 + 0.6, 0, zfr - 1.3 - T / 2), V3.UnitX, sw2 - 1.2, V3.UnitZ);
            riser.Outline.Add(Geo.Rect(0, below?.Y ?? 0, riser.Len, y - T));
            foreach (var (t0, t1) in b.Tabs(0, riser.Len, 1.2)) b.UpInto(tr, riser, t0, t1);
            if (below is not null) foreach (var (t0, t1) in b.Tabs(0, riser.Len, 1.2)) b.DownInto(below, riser, t0, t1);
            below = tr;
        }
        b.Finish();
        m.Note("Push the skirts and joists up into the platform and the steps' treads through the front skirt onto their risers, then stand the railings and the gallows frame in their slots.");
    }

    // ---- the fountain ----

    /// <summary>A regular octagon round (cx, cz) with the given apothem, flat sides facing ±x and ±z, in a flat piece's (x, −z).</summary>
    static PathD Octagon(double cx, double cz, double apothem)
    {
        double r = apothem / Math.Cos(Math.PI / 8);
        return [.. Enumerable.Range(0, 8).Select(i => { double a = Math.PI / 8 + i * Math.PI / 4; return new PointD(cx + Math.Cos(a) * r, -(cz + Math.Sin(a) * r)); })];
    }

    /// <summary>A turned shape: half-widths at heights (bottom to top), mirrored about u = c.</summary>
    static PathD Lathe(double c, IEnumerable<(double Half, double Y)> pts)
    {
        var list = pts.ToList();
        return [.. list.Select(p => new PointD(c + p.Half, p.Y)), .. Enumerable.Reverse(list).Select(p => new PointD(c - p.Half, p.Y))];
    }

    /// <summary>
    /// A tiered fountain: an eight-sided basin (eight carved panels with tabs into the plinth and the coping
    /// round the top) holding a floor of water, and in it a column of three stages, each two profiles
    /// halved together and turned 45° from the stage under it, standing on the round bowl below and holding
    /// up the bowl above, water falling from spouts under each bowl, a finial on top.
    /// </summary>
    public static void Fountain(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, S = Math.Min(it.WidthMm, it.DepthMm), H = it.HeightMm, k = H / 46;
        if (H < 26) { m.Bad("The fountain is too low: make it at least 26 mm high."); return; }
        var look = LookOf(m.Theme);
        bool metal = look.Top == Top.Armour;
        var face = m.Theme == TerrainThemes.Cave ? look with { Face = "Stone blocks", A = 6, B = 4, G = 0.9 } : look;
        var b = new Build(m, face, k);
        double cx = it.WidthMm / 2, cz = it.DepthMm / 2, R = S / 2, sw = T + F;
        double lipP = Math.Max(2.0, 2.2 * k), ap = R - lipP - T / 2, inner = ap - T / 2;
        // The joints in the column: tabs near the middle (r0) under a stage, further out (rs) over it, so
        // stages turned 45° to each other never put two slots side by side.
        double r0 = Math.Max(sw / 2 + 1.2, (sw + 1.1) / Math.Sqrt(2)), rs = (sw + 1.1) / Math.Sin(Math.PI / 4), tab = 1.8;
        double hs = sw / 2 + 1.3, r1 = Math.Round(inner * 0.58, 1), r2 = Math.Ceiling(Math.Max(r1 * 0.55, rs + tab + 1.0) * 10) / 10;
        if (r1 < r2 + 3.0 + F || r2 < rs + tab + 1) { m.Bad($"The fountain is too small for its bowls: make it at least {Math.Ceiling(2 * ((rs + tab + 1 + 3.0 + F) / 0.58 + T / 2 + T / 2 + lipP)):0} mm across."); return; }
        double hb = Math.Round(Math.Max(Geo.Clamp(H * 0.2, 7, 12), 3 * T + 3.4), 1);
        double wy = hb - T - 1.2, y1 = Math.Round(H * 0.5, 1), y2 = Math.Round(H * 0.72, 1);
        if (y1 - T - wy < 9 || y2 - T - y1 < 6 || H - y2 < 9) { m.Bad("The fountain is too low for its tiers: make it taller."); return; }
        // The plinth, with its paving.
        var plinth = new Flat { Name = "Plinth", Y = T, Texture = "none" };
        plinth.Outline.Add(Octagon(cx, cz, R));
        plinth.Lines.Add(Geo.Loop(Octagon(cx, cz, R - 0.6)));
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI / 8 + i * Math.PI / 4, ca = Math.Cos(a) / Math.Cos(Math.PI / 8), sa = Math.Sin(a) / Math.Cos(Math.PI / 8);
            plinth.Lines.Add([new(cx + ca * (R - lipP + 1.2), -(cz + sa * (R - lipP + 1.2))), new(cx + ca * R, -(cz + sa * R))]);
        }
        b.Flats.Add(plinth);
        // The coping round the top of the basin.
        var rim = new Flat { Name = "Coping", Y = hb, Texture = "none" };
        rim.Outline.Add(Octagon(cx, cz, ap + T / 2 + 1.2));
        rim.Holes.Add(Octagon(cx, cz, ap - T / 2 - 1.2));
        rim.Lines.Add(Geo.Loop(Octagon(cx, cz, ap + T / 2 + 0.6)));
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI / 8 + i * Math.PI / 4, ca = Math.Cos(a) / Math.Cos(Math.PI / 8), sa = Math.Sin(a) / Math.Cos(Math.PI / 8);
            double ri = ap - T / 2 - 1.2, ro = ap + T / 2 + 1.2;
            rim.Lines.Add([new(cx + ca * ri, -(cz + sa * ri)), new(cx + ca * ro, -(cz + sa * ro))]);
        }
        b.Flats.Add(rim);
        // The water in the basin.
        var water = new Flat { Name = "Water", Y = wy, Texture = "none" };
        water.Outline.Add(Octagon(cx, cz, inner - 0.02));
        b.Flats.Add(water);
        // The eight sides of the basin.
        double L = 2 * (ap - T / 2) * Math.Tan(Math.PI / 8) - 0.1;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            var n = new V3(Math.Cos(a), 0, Math.Sin(a));
            var dir = new V3(-Math.Sin(a), 0, Math.Cos(a));
            var mid = new V3(cx, 0, cz) + n * ap;
            var side = b.Upright("Basin side", mid - dir * (L / 2), dir, L, n);
            side.Outline.Add(Geo.Rect(0, T, L, hb - T));
            b.DownInto(plinth, side, L * 0.3, L * 0.7);
            b.UpInto(rim, side, L * 0.3, L * 0.7);
            if (i % 2 == 0) b.FlatInto(water, side, L * 0.3, L * 0.7);
            // A sunk panel on each, the theme's symbol on every other one.
            if (hb - 2 * T > 3.4 && L > 6)
            {
                side.Etched.AddRange(Fronts.Outline(1.0, T + 0.9, L - 1.0, hb - T - 0.9, 0.35));
                if (i % 2 == 1) Cabinets.Symbol(Fronts.Look(m), m.Theme, L / 2, (hb) / 2, Math.Min(L, hb - 2 * T) * 0.22, side.Etched, side.Lines);
            }
        }
        // The column, a stage at a time.
        Up[] Stage(string name, double ang, double y0, double y1t, double half, Func<bool, PathsD> profile, double ym)
        {
            var ups = new Up[2];
            for (int p = 0; p < 2; p++)
            {
                double a = ang + p * Math.PI / 2;
                var dir = new V3(Math.Cos(a), 0, Math.Sin(a));
                var facing = new V3(-dir.Z, 0, dir.X);
                var up = b.Upright(p == 0 ? name : name + " (crossing)", new V3(cx, 0, cz) - dir * half, dir, 2 * half, facing);
                up.Plain = !metal;
                up.Outline.AddRange(profile(p == 0));
                // Halved together: the first slotted down from the top, the second up from the bottom.
                up.Holes.Add(p == 0 ? Geo.Rect(half - sw / 2, ym, half + sw / 2, H + 5) : Geo.Rect(half - sw / 2, y0 - T - 5, half + sw / 2, ym));
                ups[p] = up;
            }
            return ups;
        }
        void Turned(Up up, double c, double y0, double y1t, double r)
        {
            // Fluting up the bowl and lines round the knops.
            for (int i = -2; i <= 2; i++) up.Lines.Add([new(c + i * hs * 0.35, y0 + 0.3), new(c + i * (r - 1.2) / 2.2, y1t - 1.0)]);
        }
        // Stage one: a pedestal under the water, a stem with a collar and a knop, the big bowl.
        var disc1 = new Flat { Name = "Lower bowl", Y = y1, Texture = "none" };
        disc1.Outline.Add(Geo.Circle(cx, -cz, r1 + 0.6, 0.02));
        b.Flats.Add(disc1);
        double vA = y1 - T, bowlA = Math.Min(5 * k, (vA - wy) * 0.4), ybA = vA - bowlA, ykA = (wy + 1.6 + ybA) / 2, ymA = (wy + 1.6 + ykA - 1.3) / 2;
        double pb = r0 + tab + 0.6, yE = wy - T - 0.1;
        if (yE < T + 1.2) { m.Bad("The basin is too shallow for the sheet: make the fountain taller or the sheet thinner."); return; }
        double halfA = r1 + 2.3;
        var stageA = Stage("Lower column", 0, T, vA, halfA, _ => [Lathe(halfA, BowlPoints(T, yE, pb, hs, wy, ykA, ybA, vA, r1))], ymA);
        foreach (var up in stageA)
        {
            double c = halfA;
            foreach (var s in new[] { -1.0, 1.0 })
            {
                b.DownInto(plinth, up, c + s * r0 - (s < 0 ? tab : 0), c + s * r0 + (s > 0 ? tab : 0));
                b.UpInto(disc1, up, c + s * rs - (s < 0 ? tab : 0), c + s * rs + (s > 0 ? tab : 0));
                Spout(b, up, c, s, r1, vA, wy, water);
            }
            water.Holes.Add(Build.XZ(up.At(c - hs - F / 2, 0) - up.N * (sw / 2), up.At(c + hs + F / 2, 0) - up.N * (sw / 2), up.At(c + hs + F / 2, 0) + up.N * (sw / 2), up.At(c - hs - F / 2, 0) + up.N * (sw / 2)));
            Turned(up, c, ybA, vA, r1);
            up.Lines.Add([new(c - hs - 0.6, wy + 0.95), new(c + hs + 0.6, wy + 0.95)]);
        }
        // Stage two, turned 45°: a socle on the lower bowl, a stem, the small bowl.
        var disc2 = new Flat { Name = "Upper bowl", Y = y2, Texture = "none" };
        disc2.Outline.Add(Geo.Circle(cx, -cz, r2 + 0.6, 0.02));
        b.Flats.Add(disc2);
        double vB = y2 - T, bowlB = Math.Min(3.4 * k, (vB - y1) * 0.42), ybB = vB - bowlB, pbB = r0 + tab + 0.6, yEB = y1 + Math.Max(2.0, 1.6 * k);
        double halfB = r2 + 2.3, ymB = (yEB + 0.8 + ybB) / 2;
        var stageB = Stage("Upper column", Math.PI / 4, y1, vB, halfB, _ => [Lathe(halfB, SoclePoints(y1, yEB, pbB, hs, ybB, vB, r2))], ymB);
        foreach (var up in stageB)
        {
            double c = halfB;
            foreach (var s in new[] { -1.0, 1.0 })
            {
                b.DownInto(disc1, up, c + s * r0 - (s < 0 ? tab : 0), c + s * r0 + (s > 0 ? tab : 0));
                b.UpInto(disc2, up, c + s * rs - (s < 0 ? tab : 0), c + s * rs + (s > 0 ? tab : 0));
                Spout(b, up, c, s, r2, vB, y1, disc1);
            }
            Turned(up, c, ybB, vB, r2);
        }
        // Stage three: the finial, turned back square.
        double halfC = r0 + tab + 0.6, rb = Math.Max(sw / 2 + 1.3, 2.3 * k), yEC = y2 + Math.Max(2.0, 1.6 * k), ybc = yEC + 1.4 * k + rb * 0.9, ymC = (yEC + ybc - rb * 0.7) / 2;
        if (ybc + rb > H - 1.5) { ybc = H - rb - 1.5; ymC = (yEC + ybc - rb * 0.7) / 2; }
        var stageC = Stage("Finial", 0, y2, H, halfC, first => Finial(m.Theme, halfC, y2, yEC, halfC, hs, rb, ybc, H, first), ymC);
        foreach (var up in stageC)
            foreach (var s in new[] { -1.0, 1.0 })
                b.DownInto(disc2, up, halfC + s * r0 - (s < 0 ? tab : 0), halfC + s * r0 + (s > 0 ? tab : 0));
        // Ripples on the water and in the bowls.
        foreach (var (f, r, rr) in new[] { (water, inner - 1.5, r1 + 1.5), (disc1, r1 - 1.0, r2 + 1.5), (disc2, r2 - 1.0, hs + 1.0) })
        {
            f.Lines.Add(Geo.Loop(Geo.Circle(cx, -cz, r, 0.02)));
            for (double q = rr + 0.8; q < r - 0.8; q += 1.6)
            {
                double a0 = m.Rnd.NextDouble() * Math.PI * 2;
                f.Lines.Add([.. Geo.Arc(cx, -cz, q, a0, a0 + 1.2 + m.Rnd.NextDouble())]);
                f.Lines.Add([.. Geo.Arc(cx, -cz, q, a0 + Math.PI, a0 + Math.PI + 0.8 + m.Rnd.NextDouble())]);
            }
        }
        if (b.Glue)
        {
            // Lips glued round the two bowls.
            foreach (var (f, r, name) in new[] { (disc1, r1, "Lower bowl lip"), (disc2, r2, "Upper bowl lip") })
            {
                var lipRing = new Flat { Name = name, Y = f.Y + T, Texture = "none", Glued = true };
                lipRing.Outline.Add(Geo.Circle(cx, -cz, r + 0.6, 0.02));
                lipRing.Holes.Add(Geo.Circle(cx, -cz, r - 0.6, 0.02));
                for (int i = 0; i < 24; i++)
                {
                    double a = i * Math.PI / 12;
                    lipRing.Lines.Add([new(cx + Math.Cos(a) * (r - 0.5), -cz + Math.Sin(a) * (r - 0.5)), new(cx + Math.Cos(a) * (r + 0.5), -cz + Math.Sin(a) * (r + 0.5))]);
                }
                b.Flats.Add(lipRing);
            }
        }
        b.Finish();
        m.Note("Push the basin's sides into the plinth with the water's tabs through every other one, and the coping on top. Slide each stage of the column together by its slots: the lower one into the plinth through the water, the lower bowl onto it, then the upper stage, the upper bowl and the finial" + (b.Glue ? "; glue the lips round the bowls." : "."));
    }

    /// <summary>The lower stage: a pedestal (hidden under the water), a stem with a collar and a knop, and a bowl.</summary>
    static IEnumerable<(double, double)> BowlPoints(double y0, double yE, double pb, double hs, double wy, double yk, double yb, double top, double r)
    {
        yield return (pb, y0);
        yield return (pb, y0 + Math.Min(1.1, (yE - y0) * 0.45));
        yield return (hs + 0.5, yE - 0.4);
        yield return (hs, yE);
        yield return (hs, wy + 0.3);
        yield return (hs + 0.6, wy + 0.4);
        yield return (hs + 0.6, wy + 1.5);
        yield return (hs, wy + 1.6);
        yield return (hs, yk - 1.3);
        yield return (hs + 0.8, yk - 0.6);
        yield return (hs + 0.8, yk + 0.6);
        yield return (hs, yk + 1.3);
        foreach (var q in Bowl(hs, yb, r, top - 0.8)) yield return q;
        yield return (r, top);
    }

    /// <summary>The underside of a bowl, from the stem at <paramref name="yb"/> curving out to its rim.</summary>
    static IEnumerable<(double, double)> Bowl(double hs, double yb, double r, double yr)
    {
        for (int i = 0; i <= 10; i++)
        {
            double f = i / 10.0 * Math.PI / 2;
            yield return (hs + (r - hs) * Math.Sin(f), yr - (yr - yb) * Math.Cos(f));
        }
    }

    /// <summary>A spout under a bowl's rim with a stream of water falling from it into the water below.</summary>
    static void Spout(Build b, Up up, double c, double s, double r, double top, double below, Flat into)
    {
        double a = c + s * (r + 0.9), e = c + s * (r + 2.1);
        up.Outline.Add(Geo.Rect(Math.Min(c + s * r, e), top - 1.4, Math.Max(c + s * r, e), top));
        // The stream rippling as it falls, the same width all the way down.
        double lo = Math.Min(a, e), hi = Math.Max(a, e);
        var left = new PathD();
        var right = new PathD();
        for (double y = below; ; y = Math.Min(top - 1.3, y + 0.25))
        {
            double w = y < below + 0.5 || y > top - 1.8 ? 0 : 0.28 * Math.Sin((y - below) * 2.6);
            left.Add(new PointD(lo + w, y));
            right.Add(new PointD(hi + w, y));
            if (y >= top - 1.3) break;
        }
        right.Reverse();
        up.Outline.Add([.. left, .. right]);
        for (double y = below + 1.0; y < top - 2.2; y += 1.2) up.Lines.Add([new(lo + 0.35 + 0.28 * Math.Sin((y - below) * 2.6), y), new(lo + 0.35 + 0.28 * Math.Sin((y + 0.5 - below) * 2.6), y + 0.5)]);
        b.DownInto(into, up, Math.Min(a, e), Math.Max(a, e));
    }

    /// <summary>The upper stage: a socle on the lower bowl, a stem (with a knop if there's room) and a smaller bowl.</summary>
    static IEnumerable<(double, double)> SoclePoints(double y0, double yE, double pb, double hs, double yb, double top, double r)
    {
        yield return (pb, y0);
        yield return (pb, y0 + 1.1);
        yield return (hs + 0.5, yE);
        yield return (hs, yE + 0.4);
        if (yb - yE > 4.4)
        {
            double mid = (yE + 0.4 + yb) / 2;
            yield return (hs, mid - 1.2);
            yield return (hs + 0.7, mid - 0.6);
            yield return (hs + 0.7, mid + 0.6);
            yield return (hs, mid + 1.2);
        }
        foreach (var q in Bowl(hs, yb, r, top - 0.7)) yield return q;
        yield return (r, top);
    }

    /// <summary>
    /// The finial: a socle, a neck and a ball, and (on the piece slotted from below, which keeps its top
    /// whole) a pine-cone spire, an onion dome in the desert, a second stone in a cave, an antenna for sci-fi.
    /// </summary>
    static PathsD Finial(string theme, double c, double y0, double yE, double pb, double hs, double rb, double ybc, double H, bool first)
    {
        var o = new PathsD
        {
            Lathe(c, [(pb, y0), (pb, y0 + 1.1), (hs + 0.5, yE), (hs, yE + 0.3), (hs, ybc)]),
            Geo.Circle(c, ybc, rb, 0.02),
        };
        if (first) return o;
        double bt = ybc + rb;
        switch (theme)
        {
            case TerrainThemes.Desert:
            {
                double rO = Math.Max(1.6, rb * 0.85), h = H - 1.6 - bt;
                o.Add(Lathe(c, [(0.7, ybc), (0.7, bt + 0.2), (rO, bt + h * 0.35), (rO * 0.75, bt + h * 0.62), (0.7, bt + h), (0.7, H - 1.0)]));
                o.Add(Geo.Circle(c, H - 0.8, 0.8, 0.02));
                break;
            }
            case TerrainThemes.Cave:
            {
                double r2 = Math.Min(rb * 0.75, (H - bt) / 2);
                o.Add(Lathe(c, [(1.0, ybc), (1.0, H - r2)]));
                o.Add(Geo.Circle(c, H - r2, r2, 0.02));
                break;
            }
            case TerrainThemes.SciFi:
                o.Add(Lathe(c, [(0.65, ybc), (0.65, H - 1.4)]));
                o.Add(Geo.Rect(c - 1.6, H - 3.0, c + 1.6, H - 1.8));
                o.Add(Geo.Circle(c, H - 0.8, 0.8, 0.02));
                break;
            default:
            {
                double w0 = theme == TerrainThemes.Gothic ? rb * 0.5 : rb * 0.72;
                o.Add(Lathe(c, [(w0, ybc), (w0, bt + 0.4), (w0 * 0.9, bt + (H - bt) * 0.3), (0.6, H - 1.3)]));
                o.Add(Geo.Circle(c, H - 0.75, 0.75, 0.02));
                break;
            }
        }
        return o;
    }
}
