using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Fireplaces, bread ovens and forges, for against a wall (the back at the wall, the front facing out).
/// A front with the opening cut through it and two cheeks behind it stand on a hearth that sticks out
/// into the room, their tabs down through it; a fire back between the cheeks shows through the opening,
/// with logs and flames standing in front of it. A fireplace has a mantel shelf over the front and a
/// chimney breast standing on that; a forge's mantel is its fire bed, under a hood; an oven's front is
/// a dome, with a wood store under the oven door.
/// </summary>
public static class Hearths
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        var style = it.Hearth;
        var look = Fronts.Look(m);
        var rnd = m.Rnd;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        bool oven = style == HearthStyle.Oven, forge = style == HearthStyle.Forge;
        string what = oven ? "oven" : forge ? "forge" : "fireplace";
        double e = 1.2, j = Math.Max(W * 0.18, e + T + 1.6);
        double Hs = oven ? Geo.Clamp(H * 0.62, 8, H - 4) : forge ? Geo.Clamp(H * 0.36, 8, 16) : Geo.Clamp(H * 0.5, 9, 22);
        double Wi = W - 2 * e - 2 * T, Dc = D - T;
        if (Wi < 5 || W - 2 * j < 3) { m.Bad($"The {what} is too narrow: make it at least {2 * j + 5:0.#} mm wide."); return; }
        if (Dc < 1 + T + 4) { m.Bad($"The {what} is too shallow: make it at least {2 * T + 5:0.#} mm deep."); return; }
        if (!oven && H < Hs + T + 5) { m.Bad($"The {what} is too low: make it at least {Hs + T + 5:0.#} mm high."); return; }

        // ---- the front (flat: u = x, v = y over the hearth) ----
        double Hf = (oven ? H : Hs) - T;
        double topV = oven ? Hs - T : Hf;
        var front = new PathsD { oven ? Shapes.Topped(BackTop.Arched, 0, 0, W, Hf, Math.Min(W * 0.35, Hf - (Hs - T) + 0.01)) : Geo.Rect(0, 0, W, Hf) };
        double ftw = Geo.Clamp(j * 0.4, 1.2, 3);
        double[] frontTabs = [j / 2, W - j / 2];
        foreach (var u in frontTabs)
        {
            front.Add(Geo.Rect(u - ftw / 2, -T, u + ftw / 2, 0.01));
            if (!oven) front.Add(Geo.Rect(u - ftw / 2, Hf - 0.01, u + ftw / 2, Hf + T));
        }
        var fHoles = new PathsD();
        double ow = W - 2 * j, oh;
        PathD Opening(double x0, double y0, double x1, double y1)
        {
            double rise = Math.Min((x1 - x0) * 0.3, (y1 - y0) * 0.4);
            return look switch
            {
                FrontLook.Metal => Fronts.Chamfered(x0, y0, x1, y1, Math.Min(1.5, (x1 - x0) * 0.2)),
                FrontLook.Gothic => Shapes.Topped(BackTop.Pointed, x0, y0, x1, y1, Math.Min((x1 - x0) * 0.6, (y1 - y0) * 0.5)),
                FrontLook.Carved => Shapes.Topped(BackTop.Rounded, x0, y0, x1, y1, rise),
                _ => Shapes.Topped(BackTop.Arched, x0, y0, x1, y1, rise * 0.6),
            };
        }
        double oy0;
        if (oven)
        {
            // The oven door up in the dome, the wood store below it.
            oy0 = Math.Max(3.2, (Hs - T) * 0.32);
            // Below the roof's slots, so the dome over it stays whole.
            oh = Math.Min(ow * 0.8, Hs - T - f / 2 - 1.2 - oy0);
            if (oh < 2.5) { m.Bad($"The {what} is too low for its door: make it at least {H + 2.5 - oh:0.#} mm high."); return; }
            fHoles.Add(Shapes.Topped(BackTop.Arched, j, oy0, W - j, oy0 + oh, Math.Min(ow / 2, oh * 0.6)));
            fHoles.Add(Geo.Rect(j + 0.4, -0.01, W - j - 0.4, oy0 - 1.4));
        }
        else
        {
            oy0 = 0;
            oh = forge ? Math.Max(3, Hf * 0.5) : Math.Max(4, Math.Min(Hf - 2.4, Hf * 0.72));
            fHoles.Add(Opening(j, -0.01, W - j, oh));
        }
        var fe = new PathsD();
        var fl = new List<PathD>();
        Masonry(m, look, 0, 0, W, Hf, rnd, fe, fl, oven);
        // A keystone over the opening, and a lintel line on a fireplace.
        if (!oven && look != FrontLook.Metal && oh + 2.2 < Hf)
        {
            double ky = oh + 0.2;
            fe = Geo.Subtract(fe, [Geo.Rect(W / 2 - 1.2, ky, W / 2 + 1.2, ky + 2.0)]);
            fl.Add(Geo.Loop([new(W / 2 - 0.8, ky), new(W / 2 + 0.8, ky), new(W / 2 + 1.1, ky + 1.9), new(W / 2 - 1.1, ky + 1.9)]));
        }
        fe = Geo.Subtract(Geo.Union(fe), Geo.Offset(fHoles, 0.4));
        m.Add("Front", front, fHoles, [Pose.Front(0, T, D - T / 2)], grainAlongU: true, face: [], etched: fe, lines: fl);

        // ---- the cheeks (flat: u = z from the wall, v = y over the hearth) ----
        double Hc = Hs - T, ctw = Geo.Clamp(Dc * 0.2, 1.2, 3);
        double[] cheekTabs = Dc < 9 ? [Dc * 0.6] : [Dc * 0.42, Math.Min(Dc * 0.8, Dc - ctw / 2 - 1.4)];
        var cheek = new PathsD { Geo.Rect(0, 0, Dc, Hc) };
        foreach (var u in cheekTabs)
        {
            cheek.Add(Geo.Rect(u - ctw / 2, -T, u + ctw / 2, 0.01));
            cheek.Add(Geo.Rect(u - ctw / 2, Hc - 0.01, u + ctw / 2, Hc + T));
        }
        // The fire back's tabs: one high, one low.
        double zb = 1.0;
        var backTabs = new List<(double V, double H)>();
        foreach (var (a, b) in new[] { (0.0, Hc / 2), (Hc / 2, Hc) })
        {
            double h = Math.Min(Math.Min((b - a) * 0.5, 4), b - a - 2.3);
            if (h >= 0.8) backTabs.Add(((a + b) / 2, h));
        }
        var cHoles = new PathsD();
        foreach (var (v, h) in backTabs) cHoles.Add(Geo.Rect(zb - f / 2, v - (h + f) / 2, zb + T + f / 2, v + (h + f) / 2));
        m.Add("Cheek", cheek, cHoles, [Pose.Left(e + T / 2, T, 0), Pose.Left(W - e - T / 2, T, 0)], grainAlongU: true, face: []);

        // ---- the fire back between the cheeks: bricks, blackened by soot ----
        var back = new PathsD { Geo.Rect(0, 0, Wi, Hc) };
        foreach (var (v, h) in backTabs)
        {
            back.Add(Geo.Rect(-T, v - h / 2, 0.01, v + h / 2));
            back.Add(Geo.Rect(Wi - 0.01, v - h / 2, Wi + T, v + h / 2));
        }
        var be = new PathsD();
        var bl = new List<PathD>();
        if (look == FrontLook.Metal)
        {
            // A glowing grille in a heater.
            for (double y = 1.2; y < Hc - 1; y += 1.1) be.Add(Geo.Rect(1, y, Wi - 1, y + 0.5));
        }
        else
        {
            Bricks(0, 0, Wi, Hc, 1.1, 2.4, rnd, be);
            be.Add(Rugs.Ellipse(Wi / 2, Hc * 0.35, Wi * 0.32, Hc * 0.45));
        }
        m.Add("Fire back", back, [], [Pose.Front(e + T, T, zb + T / 2)], grainAlongU: true, face: [], etched: Geo.Intersect(Geo.Union(be), [Geo.Rect(0, 0, Wi, Hc)]), lines: bl);

        // ---- logs and flames in the fire ----
        double zl = (zb + T + Dc) / 2, wl = Math.Min(Wi * 0.7, ow * 0.85), hl = Math.Max(3.5, Math.Min((oven ? oy0 - 1.4 : oh) * 0.7, 9));
        bool logs = wl > 4 && hl >= 3;
        double ltw = Geo.Clamp(wl * 0.18, 1.2, 2.5);
        double[] logTabs = [wl * 0.25, wl * 0.75];
        if (logs)
        {
            var (ls, ll, le) = Logs(wl, hl, ltw, logTabs, T, oven, look == FrontLook.Metal);
            m.Add(oven ? "Firewood" : "Logs", ls, [], [Pose.Front(W / 2 - wl / 2, T, zl)], grainAlongU: true, face: [], etched: le, lines: ll);
        }

        // ---- the hearth under it all, sticking out into the room ----
        double ho = oven ? 0.6 : 1.5, hf = oven ? 1.2 : Geo.Clamp(D * 0.35, 2, 6);
        double Lh = W + 2 * ho, Dh = D + hf;
        var hs = new PathsD();
        PointD H2(double x, double z) => new(x + ho, Dh - z);
        void HSlot(double x, double z, double wx, double wz) { var c = H2(x, z); hs.Add(Geo.Rect(c.x - wx / 2, c.y - wz / 2, c.x + wx / 2, c.y + wz / 2)); }
        foreach (var u in frontTabs) HSlot(u, D - T / 2, ftw + f, sw);
        foreach (var x in new[] { e + T / 2, W - e - T / 2 }) foreach (var u in cheekTabs) HSlot(x, u, sw, ctw + f);
        if (logs) foreach (var u in logTabs) HSlot(W / 2 - wl / 2 + u, zl, ltw + f, sw);
        var he = new PathsD();
        var hl2 = new List<PathD>();
        if (look != FrontLook.Metal) Flagstones(Lh, Dh, rnd, he);
        else for (double x = 1; x < Lh - 0.5; x += 1.2) hl2.Add([new(x, 0.6), new(x, Dh - 0.6)]);
        m.Add("Hearth", [Geo.Rect(0, 0, Lh, Dh)], hs, [Pose.Up(-ho, T / 2, Dh)], grainAlongU: true, face: [], etched: he, lines: hl2);

        // ---- the mantel (a forge's fire bed; an oven's roof behind its dome) ----
        double mo = oven ? 0 : forge ? 1.2 : Geo.Clamp(W * 0.05, 1.2, 2.5);
        double x0m = oven ? e : -mo, Lm = oven ? W - 2 * e : W + 2 * mo, Dm = oven ? Dc : D + mo;
        var ms = new PathsD();
        var mantel = new PathsD { Geo.Rect(0, 0, Lm, Dm) };
        PointD M2(double x, double z) => new(x - x0m, Dm - z);
        void MSlot(double x, double z, double wx, double wz) { var c = M2(x, z); ms.Add(Geo.Rect(c.x - wx / 2, c.y - wz / 2, c.x + wx / 2, c.y + wz / 2)); }
        if (!oven) foreach (var u in frontTabs) MSlot(u, D - T / 2, ftw + f, sw);
        foreach (var x in new[] { e + T / 2, W - e - T / 2 }) foreach (var u in cheekTabs) MSlot(x, u, sw, ctw + f);
        double mtw = Geo.Clamp(Lm * 0.15, 1.5, 4);
        if (oven)
        {
            // The roof's tabs go forward into the dome.
            foreach (var u in new[] { Lm * 0.3, Lm * 0.7 }) mantel.Add(Geo.Rect(u - mtw / 2, -T, u + mtw / 2, 0.01));
            foreach (var u in new[] { Lm * 0.3, Lm * 0.7 }) fHoles.Add(Geo.Rect(x0m + u - (mtw + f) / 2, Hs - T - f / 2, x0m + u + (mtw + f) / 2, Hs + f / 2));
        }
        // The chimney breast or the forge's hood stands on it.
        double Hb = H - Hs - T, Wb = forge ? W - 2 * e : W - 2 * Math.Max(e, 1.6), zbr = forge ? Dc * 0.4 : Dc * 0.55, btw = Geo.Clamp(Wb * 0.15, 1.5, 4);
        double[] breastTabs = [Wb * 0.25, Wb * 0.75];
        double xb = (W - Wb) / 2;
        if (!oven) foreach (var u in breastTabs) MSlot(xb + u, zbr, btw + f, sw);
        // A forge's flames stand on its fire bed.
        double wfl = Math.Min(W * 0.45, 8), zfl = (zbr + D) / 2 + 0.3;
        double[] flTabs = [wfl * 0.3, wfl * 0.7];
        double fltw = Geo.Clamp(wfl * 0.15, 1.2, 2);
        if (forge) foreach (var u in flTabs) MSlot(W / 2 - wfl / 2 + u, zfl, fltw + f, sw);
        var me = new PathsD();
        var mlines = new List<PathD>();
        if (forge)
        {
            // Coals heaped on the fire bed.
            for (int i = 0; i < 40; i++)
            {
                double x = Lm * (0.2 + 0.6 * rnd.NextDouble()), y = Dm - zfl + (rnd.NextDouble() - 0.5) * Math.Min(4, (D - zbr) * 0.8);
                mlines.Add(WoodFace.Dot(x, y, 0.3 + 0.2 * rnd.NextDouble()));
            }
            me.Add(Rugs.Ellipse(Lm / 2, Dm - zfl, Lm * 0.3, Math.Min(2.2, (D - zbr) * 0.45)));
            me = Geo.Subtract(me, Geo.Offset(ms, 0.3));
        }
        m.Add(oven ? "Roof" : forge ? "Fire bed" : "Mantel", mantel, ms, [Pose.Up(x0m, Hs + T / 2, Dm)], grainAlongU: true, face: forge || oven ? [] : null, etched: me, lines: mlines);
        if (oven)
        {
            // Re-add the front with its slots for the roof's tabs.
            m.Parts.RemoveAll(p => p.Name == "Front");
            m.Add("Front", front, fHoles, [Pose.Front(0, T, D - T / 2)], grainAlongU: true, face: [], etched: fe, lines: fl);
        }
        else
        {
            var breast = new PathsD();
            if (forge) breast.Add([new(0, 0), new(Wb, 0), new(Wb * 0.72, Hb), new(Wb * 0.28, Hb)]);
            else breast.Add(Geo.Rect(0, 0, Wb, Hb));
            foreach (var u in breastTabs) breast.Add(Geo.Rect(u - btw / 2, -T, u + btw / 2, 0.01));
            var bre = new PathsD();
            var brl = new List<PathD>();
            Masonry(m, look, 0, 0, Wb, Hb, rnd, bre, brl, false);
            if (!forge && Hb > 8 && Wb > 8) Cabinets.Symbol(look, m.Theme, Wb / 2, Hb * 0.45, Math.Min(Wb * 0.18, Hb * 0.2), bre, brl);
            if (forge) for (double y = 1.5; y < Hb - 0.5; y += 2.2) brl.Add([new(0, y), new(Wb, y)]);
            var shapeB = Geo.Union(breast);
            m.Add(forge ? "Hood" : "Chimney breast", shapeB, [], [Pose.Front(xb, Hs + T, zbr)], grainAlongU: true, face: [], etched: Geo.Intersect(Geo.Union(bre), shapeB), lines: Geo.ClipLines(brl, shapeB));
            if (forge)
            {
                var fls = Profiles.Flames(wfl / 2, 0, wfl, Math.Min(4, Hb * 0.3));
                var flp = new PathsD(fls) { Geo.Rect(0.4, 0, wfl - 0.4, 0.8) };
                foreach (var u in flTabs) flp.Add(Geo.Rect(u - fltw / 2, -T, u + fltw / 2, 0.01));
                m.Add("Flames", Geo.Solid(flp), [], [Pose.Front(W / 2 - wfl / 2, Hs + T, zfl)], grainAlongU: true, face: [], etched: Geo.Intersect(Profiles.Flames(wfl / 2, 0, wfl * 0.55, Math.Min(4, Hb * 0.3) * 0.55), fls));
            }
        }
        m.Note("Stand the front, the cheeks (the fire back between them) and the " + (oven ? "firewood" : "logs") + " in the hearth, then press the " +
               (oven ? "roof's tabs into the dome." : forge ? "fire bed on and stand the hood and flames in it." : "mantel on and stand the chimney breast in it."));
    }

    /// <summary>Logs crossed on the floor of the fire with flames rising from them (or a stack of firewood for an oven).</summary>
    static (PathsD, List<PathD>, PathsD) Logs(double w, double h, double tw, double[] tabs, double T, bool stack, bool metal)
    {
        var shape = new PathsD();
        var lines = new List<PathD>();
        var etched = new PathsD();
        if (stack)
        {
            // Log ends in a pile.
            // Log ends in a pile: a solid piece with a scalloped top, each log's end drawn on it.
            double r = Math.Min(1.1, h / 4), topY = r;
            int row = 0;
            double right = 0;
            for (double y = r; y + r <= h; y += r * 1.75, row++)
                for (double x = r + (row % 2) * r; x + r <= w; x += r * 2)
                {
                    shape.Add(Geo.Circle(x, y, r * 1.02));
                    lines.Add(Geo.Loop(Geo.Circle(x, y, r * 0.98)));
                    lines.Add(WoodFace.Dot(x, y, r * 0.45));
                    topY = Math.Max(topY, y);
                    right = Math.Max(right, x + r);
                }
            shape.Add(Geo.Rect(0, 0, Math.Max(right, Math.Min(w, 2 * r)), topY));
        }
        else if (metal)
        {
            // A heater's core: a column of fins.
            shape.Add(Fronts.Chamfered(0, 0, w, h, 0.8));
            for (double y = 0.8; y < h - 0.6; y += 0.9) etched.Add(Geo.Rect(0.6, y, w - 0.6, y + 0.4));
        }
        else
        {
            double lh = Math.Min(1.6, h * 0.3);
            shape.Add(WoodFace.RoundRect(0, 0, w, lh, lh / 2));
            shape.AddRange(Geo.Turn([WoodFace.RoundRect(-w * 0.35, -lh / 2, w * 0.35, lh / 2, lh / 2)], 12, w * 0.45, lh * 1.2));
            shape.AddRange(Geo.Turn([WoodFace.RoundRect(-w * 0.3, -lh / 2, w * 0.3, lh / 2, lh / 2)], -15, w * 0.6, lh * 1.3));
            var fl = Profiles.Flames(w / 2, lh, w * 0.75, h - lh);
            shape.AddRange(fl);
            etched.AddRange(Geo.Intersect(Profiles.Flames(w / 2, lh, w * 0.4, (h - lh) * 0.55), fl));
            lines.Add(WoodFace.Dot(lh * 0.5, lh / 2, lh * 0.3));
            lines.Add(WoodFace.Dot(w - lh * 0.5, lh / 2, lh * 0.3));
        }
        // Nothing below the hearth but the tabs.
        shape = Geo.Intersect(Geo.Solid(shape), [Geo.Rect(-1, 0, w + 1, h + 1)]);
        foreach (var u in tabs) shape.Add(Geo.Rect(u - tw / 2, -T, u + tw / 2, 0.01));
        var u2 = Geo.Union(shape);
        return (u2, Geo.ClipLines(lines, u2), Geo.Intersect(etched, u2));
    }

    /// <summary>The front's stonework: courses of blocks (fine for gothic, big for a dungeon), bricks for an oven, metal panels for sci-fi.</summary>
    static void Masonry(Maker m, FrontLook look, double x0, double y0, double x1, double y1, Random rnd, PathsD etched, List<PathD> lines, bool bricks)
    {
        if (look == FrontLook.Metal)
        {
            double pw = Math.Max(4, (x1 - x0) / Math.Max(1, Math.Round((x1 - x0) / 7)));
            for (double x = x0; x < x1 - 1; x += pw)
                for (double y = y0; y < y1 - 1; y += 6)
                    Fronts.Panel(FrontLook.Metal, x + 0.2, y + 0.2, Math.Min(x1, x + pw) - 0.2, Math.Min(y1, y + 6) - 0.2, etched, lines);
            return;
        }
        var (ch, bl) = bricks ? (1.1, 2.4) : m.Theme switch
        {
            "Dungeon" => (2.6, 4.6),
            "Gothic" => (1.7, 3.6),
            "Cave" => (2.4, 3.2),
            "Desert" => (2.2, 5.0),
            _ => (2.0, 3.6),
        };
        Bricks(x0, y0, x1, y1, ch, bl, rnd, etched);
    }

    /// <summary>Courses of blocks: etched mortar grooves, the joints staggered.</summary>
    public static void Bricks(double x0, double y0, double x1, double y1, double course, double length, Random rnd, PathsD etched)
    {
        double g = WoodFace.GrooveMm;
        var grooves = new PathsD();
        int row = 0;
        for (double y = y0 + course; y < y1 - 0.2; y += course, row++)
            grooves.Add(Geo.Rect(x0 - 1, y - g / 2, x1 + 1, y + g / 2));
        row = 0;
        for (double y = y0; y < y1; y += course, row++)
        {
            double x = x0 + (row % 2 == 0 ? 0 : length / 2) - length * rnd.NextDouble() * 0.15;
            while (x < x1)
            {
                if (x > x0 + 0.3) grooves.Add(Geo.Rect(x - g / 2, y, x + g / 2, Math.Min(y1, y + course)));
                x += length * (0.8 + 0.4 * rnd.NextDouble());
            }
        }
        etched.AddRange(Geo.Intersect(Geo.Union(grooves), [Geo.Rect(x0, y0, x1, y1)]));
    }

    /// <summary>Flagstones on a hearth: big irregular slabs, the gaps between them etched.</summary>
    static void Flagstones(double W, double D, Random rnd, PathsD etched)
    {
        double g = WoodFace.GrooveMm;
        var grooves = new PathsD();
        double x = 0;
        while (x < W)
        {
            double w = 3 + rnd.NextDouble() * 3;
            if (x > 0.3) grooves.Add(Geo.Rect(x - g / 2, 0, x + g / 2, D));
            double y = 0;
            while (y < D)
            {
                double h = 2.5 + rnd.NextDouble() * 3;
                if (y > 0.3) grooves.Add(Geo.Rect(x, y - g / 2, x + w, y + g / 2));
                y += h;
            }
            x += w;
        }
        etched.AddRange(Geo.Intersect(Geo.Union(grooves), [Geo.Rect(0, 0, W, D)]));
    }
}
