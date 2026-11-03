using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Desks. A sloping desk (a lectern, a scribe's writing desk, a map table or a control console) is two
/// shaped sides with a sloping top between them, its tabs in slots along the slope, a shelf low down
/// (or, on a console, a flat desk and a kick panel). A flat desk is a top on two ends joined by a back
/// panel and a drawer rail, with papers on it, or a screen.
/// </summary>
public static class Slants
{
    public static void SlantDesk(Maker m)
    {
        var it = m.Item;
        var style = it.Slant;
        var look = Fronts.Look(m);
        var rnd = m.Rnd;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double Li = W - 2 * T;
        string what = style switch { SlantStyle.Lectern => "lectern", SlantStyle.Console => "console", SlantStyle.MapTable => "map table", _ => "writing desk" };
        if (Li < 4) { m.Bad($"The {what} is too narrow: make it at least {2 * T + 4:0.#} mm wide."); return; }
        if (D < 6) { m.Bad($"The {what} is too shallow: make it at least 6 mm deep."); return; }
        double angle = style switch { SlantStyle.MapTable => 12, SlantStyle.Console => 40, SlantStyle.Lectern => 30, _ => 22 } * Math.PI / 180;
        double ca = Math.Cos(angle), sa = Math.Sin(angle);

        // ---- the side's outline (u = z from the back, v = y), the slope's ends, the flat plates ----
        var side = new PathsD();
        var holes = new PathsD();
        double zF, yF, zB, yB;
        var flats = new List<(string Name, double Y, double Z0, double Z1)>();
        double pw = 4.2, zc = D / 2;
        switch (style)
        {
            case SlantStyle.Lectern:
            {
                zF = D - 0.6; zB = Math.Max(0.6, D - 0.6 - (H * 0.3) / Math.Tan(angle));
                yB = H; yF = H - (zF - zB) * Math.Tan(angle);
                double yP = Math.Max(4, yF - 7);
                if (yF < 8) { m.Bad($"The {what} is too low for its depth: make it taller or shallower."); return; }
                side.Add(WoodFace.RoundRect(0, 0, D, 1.8, 0.7));
                side.Add(Geo.Rect(zc - pw / 2, 0, zc + pw / 2, yP + 0.01));
                side.Add([new(zc - pw / 2, yP), new(zc + pw / 2, yP), new(zF, yF - 5), new(zF, yF), new(zB, yB), new(zB, yB - 5)]);
                // A shelf: a narrow stretcher between the posts.
                flats.Add(("Shelf", Math.Max(2.4, yP * 0.4), zc - pw / 2, zc + pw / 2));
                break;
            }
            case SlantStyle.Console:
            {
                double yd = Geo.Clamp(H * 0.45, 7, H - 6), zs = D * 0.5;
                zF = zs; yF = yd; zB = Math.Max(1.2, zs - (H - yd) / Math.Tan(angle)); yB = yd + (zs - zB) * Math.Tan(angle);
                side.Add([new(0, 0), new(D, 0), new(D, yd), new(zs, yd), new(zB, yB), new(0, yB)]);
                flats.Add(("Desk", yd - 1.0 - T, zs + 0.2, D));
                break;
            }
            default:
            {
                                zF = D; zB = 0;
                yF = H - D * Math.Tan(angle); yB = H;
                if (yF < 6) { m.Bad($"The {what} is too low for its depth: make it taller or shallower."); return; }
                side.Add([new(0, 0), new(D, 0), new(D, yF), new(0, yB)]);
                double ys = Geo.Clamp(yF * 0.3, 2.6, 6), legs = Geo.Clamp(D * 0.14, 1.6, 3);
                holes.Add(WoodFace.RoundRect(legs, -3, D - legs, ys - 1.2, Math.Min(1.2, (D - 2 * legs) / 2)));
                // A shelf for books or rolled maps.
                flats.Add(("Shelf", ys, 0, D));
                // A notch-shaped waist on the sides above the shelf for a lighter look.
                if (yF - 4.5 - T - (ys + T + 1.6) > 3 && D > 8)
                    holes.Add(WoodFace.RoundRect(legs + 0.8, ys + T + 1.6, D - legs - 0.8, Math.Min(yF, yB) - 4.5 - T, 1.0));
                break;
            }
        }
        // ---- the sloping top, between the sides, a sheet's width below their sloping edge ----
        var P0 = new PointD(zF, yF);
        double L = Math.Sqrt((zB - zF) * (zB - zF) + (yB - yF) * (yB - yF));
        var d = new PointD((zB - zF) / L, (yB - yF) / L);          // up the slope (back and up)
        var outN = new PointD(d.y, -d.x);                           // out of the slope (front and up)
        if (outN.y < 0) outN = new PointD(-outN.x, -outN.y);
        double dd = 1.0 + sw / 2, inset = 0.8, Lp = L - 2 * inset;
        if (Lp < 4) { m.Bad($"The {what}'s top is too short: make it deeper or taller."); return; }
        double stw = Geo.Clamp(Lp * 0.2, 1.2, 4);
        double[] slopeTabs = Lp < 8 ? [Lp / 2] : [Lp * 0.25, Lp * 0.75];
        PointD At(double s, double off) => new(P0.x + d.x * s - outN.x * off, P0.y + d.y * s - outN.y * off);
        foreach (var s in slopeTabs)
        {
            var c = At(inset + s, dd);
            double a = (stw + f) / 2, b = sw / 2;
            holes.Add([new(c.x - d.x * a - outN.x * b, c.y - d.y * a - outN.y * b), new(c.x + d.x * a - outN.x * b, c.y + d.y * a - outN.y * b),
                       new(c.x + d.x * a + outN.x * b, c.y + d.y * a + outN.y * b), new(c.x - d.x * a + outN.x * b, c.y - d.y * a + outN.y * b)]);
        }
        // ---- the flat plates' slots ----
        (double Tw, double[] At) Tabs(double depth) => depth < 7 ? (Math.Max(1.2, Math.Min(depth - 2.2, depth * 0.4)), [depth / 2]) : (Geo.Clamp(depth * 0.2, 1.2, 4), [depth * 0.25, depth * 0.75]);
        foreach (var (_, y, z0, z1) in flats)
        {
            var (tw, at) = Tabs(z1 - z0);
            foreach (var c in at) holes.Add(Geo.Rect(z0 + c - (tw + f) / 2, y - f / 2, z0 + c + (tw + f) / 2, y + T + f / 2));
        }
        // ---- a console's kick panel under the desk ----
        double zk = D - 1.0 - T / 2, kTop = flats.Count > 0 && style == SlantStyle.Console ? flats[0].Y : 0;
        var kickTabs = new List<(double Y, double H)>();
        if (style == SlantStyle.Console)
        {
            double h = Math.Min(Math.Min(kTop * 0.4, 4), kTop - 2.3);
            if (h >= 0.8) kickTabs.Add((kTop / 2, h));
            foreach (var (y, h2) in kickTabs) holes.Add(Geo.Rect(zk - sw / 2, y - (h2 + f) / 2, zk + sw / 2, y + (h2 + f) / 2));
        }
        var shape = Geo.Union(side);
        var se = new PathsD();
        var sl = new List<PathD>();
        if (look == FrontLook.Metal && style == SlantStyle.Console)
            for (double y = 1.5; y < Math.Min(5, flats[0].Y - 1); y += 0.8) se.Add(Geo.Rect(D * 0.2, y, D * 0.8, y + 0.3));
        m.Add("Left side", shape, holes, [Pose.Left(T / 2, 0, 0)], grainAlongU: false, etched: se, lines: sl);
        PathsD Mirror(PathsD a) => Geo.Map(a, p => new PointD(D - p.x, p.y));
        m.Add("Right side", Mirror(shape), Mirror(holes), [Pose.Right(W - T / 2, 0, D)], grainAlongU: false, etched: Mirror(se));

        // ---- the sloping top's piece ----
        var top = new PathsD { Geo.Rect(0, 0, Li, Lp) };
        foreach (var s in slopeTabs)
        {
            top.Add(Geo.Rect(-T, s - stw / 2, 0.01, s + stw / 2));
            top.Add(Geo.Rect(Li - 0.01, s - stw / 2, Li + T, s + stw / 2));
        }
        var te = new PathsD();
        var tl = new List<PathD>();
        switch (style)
        {
            case SlantStyle.Console:
                Screens(Li, Lp, rnd, te, tl);
                break;
            case SlantStyle.MapTable:
                MapOn(Li, Lp, rnd, te, tl);
                break;
            default:
                OpenBook(Li / 2, Lp * 0.5, Math.Min(Li * 0.85, 8), Math.Min(Lp * 0.75, 5.5), tl, te);
                // A ledge along the bottom to stop the book sliding.
                te.Add(Geo.Rect(0, 0.2, Li, 0.7));
                break;
        }
        var o = At(inset, dd);
        var pose = new Pose(new V3(T, o.y, o.x), V3.UnitX, new V3(0, d.y, d.x));
        m.Add(style == SlantStyle.Console ? "Screen panel" : "Top", top, [], [pose], grainAlongU: true, face: style is SlantStyle.Console or SlantStyle.MapTable ? [] : null, etched: te, lines: tl);

        // ---- the flat plates ----
        foreach (var (name, y, z0, z1) in flats)
        {
            double depth = z1 - z0;
            var (tw, at) = Tabs(depth);
            var plate = new PathsD { Geo.Rect(0, 0, Li, depth) };
            foreach (var c in at)
            {
                double v = depth - c;
                plate.Add(Geo.Rect(-T, v - tw / 2, 0.01, v + tw / 2));
                plate.Add(Geo.Rect(Li - 0.01, v - tw / 2, Li + T, v + tw / 2));
            }
            var pe = new PathsD();
            var pl = new List<PathD>();
            if (name == "Desk") Buttons(Li, depth, rnd, pe, pl);
            if (name == "Shelf" && style == SlantStyle.MapTable)
                for (double x = 1; x < Li - 1.5; x += 1.6) pl.Add(WoodFace.Dot(x + 0.6, depth / 2, 0.6));
            m.Add(name, plate, [], [Pose.Up(T, y + T / 2, z1)], grainAlongU: true, face: name == "Desk" ? [] : null, etched: pe, lines: pl);
        }
        if (style == SlantStyle.Console && kickTabs.Count > 0)
        {
            var kick = new PathsD { Geo.Rect(0, 0, Li, kTop) };
            foreach (var (y, h) in kickTabs)
            {
                kick.Add(Geo.Rect(-T, y - h / 2, 0.01, y + h / 2));
                kick.Add(Geo.Rect(Li - 0.01, y - h / 2, Li + T, y + h / 2));
            }
            var ke = new PathsD();
            var kl = new List<PathD>();
            Fronts.Panel(FrontLook.Metal, 0.4, 0.4, Li - 0.4, kTop - 0.4, ke, kl);
            for (double y = 1.4; y < kTop - 1.4; y += 0.8) ke.Add(Geo.Rect(Li * 0.25, y, Li * 0.75, y + 0.3));
            m.Add("Kick panel", kick, [], [Pose.Front(T, 0, zk)], grainAlongU: true, face: [], etched: ke, lines: kl);
        }
        m.Note($"Slide the {(flats.Count > 0 ? Lower(flats[0].Name) + " and the " : "")}{(style == SlantStyle.Console ? "screen panel" : "sloping top")} into one side, then push the other side on.");
    }

    static string Lower(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>An open book: two pages with lines of text, a ribbon.</summary>
    static void OpenBook(double cx, double cy, double w, double h, List<PathD> lines, PathsD etched)
    {
        if (w < 3 || h < 2) return;
        foreach (var s in new[] { -1, 1 })
        {
            var page = new PathD { new(cx, cy - h / 2), new(cx + s * w / 2, cy - h / 2 + 0.3), new(cx + s * w / 2, cy + h / 2 + 0.3), new(cx, cy + h / 2) };
            lines.Add(Geo.Loop(page));
            for (double y = cy - h / 2 + 0.8; y < cy + h / 2 - 0.4; y += 0.6) lines.Add([new(cx + s * 0.5, y), new(cx + s * (w / 2 - 0.5), y + 0.1)]);
        }
        etched.Add(Geo.Rect(cx - 0.15, cy - h / 2 - 1.0, cx + 0.15, cy - h / 2));
    }

    /// <summary>A console's screens: two or three, with lines, graphs and a radar.</summary>
    static void Screens(double W, double H, Random rnd, PathsD etched, List<PathD> lines)
    {
        int n = W > 14 ? 3 : W > 7 ? 2 : 1;
        double g = 0.6, sw = (W - (n + 1) * g) / n;
        for (int i = 0; i < n; i++)
        {
            double x0 = g + i * (sw + g), x1 = x0 + sw, y0 = g, y1 = H - g;
            var scr = new PathsD { Fronts.Chamfered(x0, y0, x1, y1, Math.Min(0.6, sw * 0.1)) };
            var shown = new PathsD();
            if (i % 2 == 1 && Math.Min(sw, y1 - y0) > 3)
            {
                // A radar: rings and a sweep.
                double r = Math.Min(sw, y1 - y0) * 0.4, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
                foreach (var k in new[] { 1.0, 0.6 }) shown.AddRange(Fronts.Outline([Geo.Circle(cx, cy, r * k, 0.02)], 0.18));
                shown.Add(Geo.Strip(new PointD(cx, cy), new PointD(cx + r * 0.7, cy + r * 0.7), 0.25));
            }
            else
            {
                for (double y = y1 - 0.9; y > y0 + 0.6; y -= 0.8) shown.Add(Geo.Rect(x0 + 0.5, y - 0.15, x0 + 0.5 + (sw - 1) * (0.4 + 0.6 * rnd.NextDouble()), y + 0.15));
            }
            etched.AddRange(Geo.Subtract(scr, shown));
        }
    }

    /// <summary>Buttons and switches on a console's desk.</summary>
    static void Buttons(double W, double D, Random rnd, PathsD etched, List<PathD> lines)
    {
        for (double y = 1; y < D - 0.8; y += 1.1)
            for (double x = 1; x < W - 0.8; x += 1.1)
            {
                double r = rnd.NextDouble();
                if (r < 0.35) etched.Add(Geo.Rect(x - 0.3, y - 0.25, x + 0.3, y + 0.25));
                else if (r < 0.6) lines.Add(WoodFace.Dot(x, y, 0.25));
            }
    }

    /// <summary>A map on a table: a coast, rivers, a compass rose and a border.</summary>
    static void MapOn(double W, double H, Random rnd, PathsD etched, List<PathD> lines)
    {
        lines.Add(Geo.Loop(Geo.Rect(0.6, 0.6, W - 0.6, H - 0.6)));
        var land = Rugs.Hide(W * 0.45, H * 0.5, W * 0.35, H * 0.38, rnd);
        lines.Add(Geo.Loop(land));
        etched.AddRange(Geo.Subtract([Geo.Rect(0.6, 0.6, W - 0.6, H - 0.6)], Geo.Offset([land], 0.6)).Where(p => Math.Abs(Clipper.Area(p)) > 0.1).Take(0));
        for (int k = 0; k < 2; k++)
        {
            var r = new PathD();
            double x = W * (0.3 + 0.3 * k), y = H * 0.5;
            for (int i = 0; i < 8; i++) { r.Add(new PointD(x, y)); x += (rnd.NextDouble() - 0.3) * 1.5; y += (rnd.NextDouble() - 0.5) * 1.6; }
            lines.AddRange(Geo.ClipLines([r], [land]));
        }
        double cr = Math.Min(W, H) * 0.12;
        if (cr > 0.8) lines.Add(Geo.Loop(Fronts.Star(W * 0.85, H * 0.78, cr, cr * 0.3, 4)));
        for (int i = 0; i < 4; i++) etched.Add(Geo.Circle(W * (0.25 + 0.4 * rnd.NextDouble()), H * (0.3 + 0.4 * rnd.NextDouble()), 0.3, 0.01));
    }

    // ---- flat desks ----

    public static void Desk(Maker m)
    {
        var it = m.Item;
        var look = Fronts.Look(m);
        var rnd = m.Rnd;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double Li = W - 2 * T, Hb = H - T;
        double ah = Geo.Clamp(H * 0.25, 3.6, 4.8), mh = Geo.Clamp(Hb * 0.5, 3.2, Hb - 2);
        if (Li < 8) { m.Bad($"The desk is too narrow: make it at least {2 * T + 8:0.#} mm wide."); return; }
        if (D < 2 * T + 5) { m.Bad($"The desk is too shallow: make it at least {2 * T + 5:0.#} mm deep."); return; }
        if (Hb < ah + 3) { m.Bad($"The desk is too low: make it at least {ah + 3 + T:0.#} mm high."); return; }
        bool metal = look == FrontLook.Metal;
        double zbk = 1.0, zap = D - 1.0 - T;
        // ---- the ends (u = z from the back, v = y), their tabs up into the top ----
        double etw = Geo.Clamp(D * 0.2, 1.2, 4);
        double[] endTabs = D < 9 ? [D / 2] : [D * 0.3, D * 0.7];
        var end = new PathsD { Geo.Rect(0, 0, D, Hb) };
        foreach (var u in endTabs) end.Add(Geo.Rect(u - etw / 2, Hb - 0.01, u + etw / 2, H));
        var eh = new PathsD();
        double legs = Geo.Clamp(D * 0.15, 1.6, 3);
        double under = Hb - Math.Max(ah, mh) - 1.2;
        if (!metal && under > 2) eh.Add(WoodFace.RoundRect(legs, -3, D - legs, under, Math.Min(1.5, (D - 2 * legs) / 2)));
        double ath = ah - 2.3, mth = Math.Min(mh - 2.3, 4);
        eh.Add(Geo.Rect(zap - f / 2, Hb - ah / 2 - (ath + f) / 2, zap + T + f / 2, Hb - ah / 2 + (ath + f) / 2));
        eh.Add(Geo.Rect(zbk - f / 2, Hb - mh / 2 - (mth + f) / 2, zbk + T + f / 2, Hb - mh / 2 + (mth + f) / 2));
        var ee = new PathsD();
        var el = new List<PathD>();
        if (metal && under > 3) Fronts.Panel(FrontLook.Metal, zbk + T + 0.6, 1.0, zap - 0.6, Hb - Math.Max(ah, mh) - 0.8, ee, el);
        m.Add("End", end, eh, [Pose.Left(T / 2, 0, 0), Pose.Left(W - T / 2, 0, 0)], grainAlongU: false, etched: ee, lines: el);
        // ---- the back panel and the drawer rail between them ----
        PathsD Between(double h, double th)
        {
            var p = new PathsD { Geo.Rect(0, 0, Li, h) };
            p.Add(Geo.Rect(-T, h / 2 - th / 2, 0.01, h / 2 + th / 2));
            p.Add(Geo.Rect(Li - 0.01, h / 2 - th / 2, Li + T, h / 2 + th / 2));
            return p;
        }
        m.Add("Back panel", Between(mh, mth), [], [Pose.Front(T, Hb - mh, zbk + T / 2)], grainAlongU: true);
        var de = new PathsD();
        var dl = new List<PathD>();
        int drawers = Li > 22 ? 3 : Li > 12 ? 2 : 1;
        for (int i = 0; i < drawers; i++) Fronts.Drawer(look, Li * i / drawers + 0.3, 0.4, Li * (i + 1) / drawers - 0.3, ah - 0.4, de, dl);
        m.Add("Drawer rail", Between(ah, ath), [], [Pose.Front(T, Hb - ah, zap + T / 2)], grainAlongU: true, face: metal ? [] : null, etched: de, lines: dl);
        // ---- the top, overhanging, with papers or a screen on it ----
        double oh = Geo.Clamp(W * 0.04, 1.0, 2.0), Wt = W + 2 * oh, Dt = D + oh;
        var slots = new PathsD();
        foreach (var x in new[] { T / 2, W - T / 2 })
            foreach (var u in endTabs) slots.Add(Geo.Rect(x + oh - sw / 2, Dt - u - (etw + f) / 2, x + oh + sw / 2, Dt - u + (etw + f) / 2));
        bool screen = it.Contents == ShelfContents.Tech;
        double mw = Math.Min(Wt * 0.45, 12), mHt = Math.Min(9, Math.Max(5, mw * 0.65)), mz = Math.Max(2.5, D * 0.3), mtw = Geo.Clamp(mw * 0.2, 1.2, 2.5);
        if (screen) slots.Add(Geo.Rect(Wt / 2 - (mtw + f) / 2, Dt - mz - sw / 2, Wt / 2 + (mtw + f) / 2, Dt - mz + sw / 2));
        var te = new PathsD();
        var tl = new List<PathD>();
        if (screen)
        {
            // A keyboard in front of the screen.
            double kw = Math.Min(mw * 0.8, Wt - 4);
            tl.Add(Geo.Loop(Geo.Rect(Wt / 2 - kw / 2, oh + 0.8, Wt / 2 + kw / 2, oh + 2.4)));
            for (double x = Wt / 2 - kw / 2 + 0.5; x < Wt / 2 + kw / 2 - 0.3; x += 0.6) tl.Add([new(x, oh + 1.0), new(x, oh + 2.2)]);
        }
        else
        {
            // Papers, a book and an inkwell with a quill.
            tl.Add(Geo.Loop(Geo.Turn([Geo.Rect(-2, -1.4, 2, 1.4)], 8, Wt * 0.35, Dt * 0.55)[0]));
            tl.Add(Geo.Loop(Geo.Turn([Geo.Rect(-1.8, -1.3, 1.8, 1.3)], -6, Wt * 0.42, Dt * 0.5)[0]));
            te.Add(Geo.Circle(Wt * 0.72, Dt * 0.62, 0.6, 0.01));
            tl.Add([new(Wt * 0.72, Dt * 0.62), new(Wt * 0.72 + 2.2, Dt * 0.62 + 1.6)]);
            if (Wt > 14) te.Add(Geo.Turn([Geo.Rect(-1.6, -1.2, 1.6, 1.2)], 20, Wt * 0.15, Dt * 0.6)[0]);
        }
        m.Add("Top", [Geo.Rect(0, 0, Wt, Dt)], slots, [Pose.Up(-oh, H - T / 2, D + oh)], grainAlongU: true, etched: te, lines: tl);
        if (screen)
        {
            var mon = new PathsD { Fronts.Chamfered(0, 1.6, mw, mHt, 0.8), Geo.Rect(mw / 2 - 0.8, 0, mw / 2 + 0.8, 1.7), Geo.Rect(mw / 2 - mtw / 2, -T, mw / 2 + mtw / 2, 0.01) };
            var me = new PathsD();
            var ml = new List<PathD>();
            Screens(mw, mHt - 1.6 - 0.2, rnd, me, ml);
            m.Add("Screen", Geo.Union(mon), [], [Pose.Front(-oh + Wt / 2 - mw / 2, H, mz)], grainAlongU: true, face: [], etched: Geo.Move(me, 0, 1.8), lines: Geo.Map(ml, p => new PointD(p.x, p.y + 1.8)));
        }
        m.Note("Push the back panel and the drawer rail into one end, put the other end on, then press the top down onto the ends' tabs" + (screen ? " and stand the screen in its slot." : "."));
    }
}
