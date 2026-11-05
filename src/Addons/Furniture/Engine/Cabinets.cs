using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Cabinets: two sides, a bottom and shelves whose tabs go through the sides, a back and a front
/// panel set between the sides (their tabs through the sides too), and a top the sides' tabs go up
/// into (two layers for a cornice), with a pediment or an arch standing on it if asked. The front
/// carries the doors, drawers, glass, lattice, lockers, machines or a clock face; open shelves show
/// what stands on them, drawn on the back. A hutch is a cupboard with a worktop and open shelves set
/// back above it.
/// </summary>
public static class Cabinets
{
    /// <summary>The front is this far behind the sides' front edges (mm): the sheet in front of its slots.</summary>
    const double FrontIn = 1.0;
    /// <summary>The back is this far in from the sides' back edges (mm).</summary>
    const double BackIn = 1.0;

    public static void Make(Maker m)
    {
        var it = m.Item;
        if (it.Front == CabinetFront.Clock) { Clocks.Make(m); return; }
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        var front = it.Front;
        var look = Fronts.Look(m);
        bool metal = look == FrontLook.Metal;
        bool hutch = front == CabinetFront.Hutch, open = front == CabinetFront.Open, clock = front == CabinetFront.Clock;
        bool back = it.BackPanel || front is not CabinetFront.Panelled;
        bool hasFront = !open;
        bool seeThrough = front is CabinetFront.Open or CabinetFront.Glass or CabinetFront.Lattice or CabinetFront.Hutch || (front == CabinetFront.Panelled && !back);
        // Feet cut out of the sides and front, unless it's a metal box, a clock's solid base or an altar's.
        bool feet = !metal && front is not (CabinetFront.Clock or CabinetFront.Altar or CabinetFront.Tech or CabinetFront.Locker);
        double kick = feet ? Math.Max(2.5, T + 1.5) : Math.Max(1.5, T * 0.75);
        string what = Name(front);

        // The top: one layer, or two stepped out for a cornice; a pediment or an arch above it.
        var crown = clock ? (it.Crown == Crown.Pediment ? Crown.Pediment : Crown.Arch) : it.Crown;
        int layers = crown == Crown.Cornice ? 2 : 1;
        double hc = crown is Crown.Pediment or Crown.Arch ? Geo.Clamp(W * (clock ? 0.42 : crown == Crown.Arch ? 0.28 : 0.2), clock ? 3.2 : 2.5, 8) : 0;
        // A clock's crown carries finials: a ball on the top and one on each end.
        double finR = clock && hc > 0 ? Geo.Clamp(W * 0.06, 0.55, 0.8) : 0, finH = finR > 0 ? 0.6 + 2 * finR : 0;
        double bodyTop = H - hc - finH - layers * T;
        double Dl = D, Dup = hutch ? Geo.Clamp(D * 0.5, 2 * T + 3.5, D - 2) : D;
        double Hl = hutch ? Geo.Clamp(H * 0.45, kick + 2 * T + 6, bodyTop - T - 7) : 0;

        if (W - 2 * T < 5) { m.Bad($"The {what} is too narrow: make it at least {2 * T + 5:0.#} mm wide."); return; }
        double minD = (back ? BackIn + T : 0) + (hasFront ? FrontIn + T : 0) + 2;
        if (D < minD) { m.Bad($"The {what} is too shallow: make it at least {minD:0.#} mm deep."); return; }
        if (bodyTop - kick - T < 5) { m.Bad($"The {what} is too low: make it at least {kick + T + 5 + layers * T + hc:0.#} mm high."); return; }
        if (hutch && (Dup - (back ? BackIn + T : 0) < 2.5 || bodyTop - Hl < 7)) { m.Bad($"The {what} is too small for a cupboard with shelves over it: make it taller and deeper."); return; }

        // ---- the plates: bottom, worktop (hutch) and shelves, each (y of its bottom face, depth it spans) ----
        double zb = back ? BackIn + T : 0;
        double zfLower = hasFront ? Dl - FrontIn - T : Dl;
        var plates = new List<(double Y, double Z1, bool Worktop)> { (kick, zfLower, false) };
        // A worktop's tabs go only where the side carries on up above it (behind the shelves), so there's sheet over its slots.
        double TabSpan(double z1, bool worktop) => worktop ? Dup - zb : Math.Min(z1, Dl) - zb;
        int shelves = Geo.Clamp(it.Shelves, 0, 30);
        if (hutch)
        {
            plates.Add((Hl - T, Dl, true));
            double gap = (bodyTop - Hl - shelves * T) / (shelves + 1);
            if (gap < 3) { m.Bad($"The shelves are too close together ({Math.Max(0, gap):0.#} mm): make it taller or have fewer shelves."); return; }
            for (int i = 1; i <= shelves; i++) plates.Add((Hl + i * gap + (i - 1) * T, Dup, false));
        }
        else if (seeThrough || front == CabinetFront.Panelled && !back)
        {
            double gap = (bodyTop - kick - T - shelves * T) / (shelves + 1);
            if (gap < 3) { m.Bad($"The shelves are too close together ({Math.Max(0, gap):0.#} mm): make it taller or have fewer shelves."); return; }
            for (int i = 1; i <= shelves; i++) plates.Add((kick + T + i * gap + (i - 1) * T, zfLower, false));
        }
        // The open spaces between plates (and up to the top), for the back's and front's tabs and the contents.
        var gaps = new List<(double Y0, double Y1, double Depth)>();
        for (int i = 0; i < plates.Count; i++)
            gaps.Add((plates[i].Y + T, i + 1 < plates.Count ? plates[i + 1].Y : bodyTop, plates[i].Worktop || hutch && i > 0 ? Dup : Dl));

        // What stands on shelves that can be seen, cut out to stand on them where there's room (drawn on the back otherwise).
        var rows = new Contents.ShelfRow?[gaps.Count];
        for (int i = 0; i < gaps.Count; i++)
        {
            if (!seeThrough || front == CabinetFront.Panelled || hutch && i == 0) continue;
            double depth = Math.Min(plates[i].Z1, Dl) - zb;
            if (depth < T + f + Maker.Bridge) continue;
            rows[i] = Contents.Row(it.Contents, W - 2 * T - 2 * RowEnd, gaps[i].Y1 - gaps[i].Y0 - RowHeadroom, T, m.Rnd);
        }

        (double Tw, double[] At) Tabs(double depth) => depth < 7 ? (Math.Max(1.2, depth * 0.4), [depth / 2]) : (Geo.Clamp(depth * 0.2, 1.2, 4), [depth * 0.25, depth * 0.75]);
        // Tabs on the back's and front's edges, in each gap between plates: none where the gap is too small for sheet round the slot.
        static double TabH(double y0, double y1) => Math.Min(Math.Min((y1 - y0) * 0.5, 5), y1 - y0 - 2.3);
        static double[] TabHeights(double y0, double y1) => TabH(y0, y1) < 0.8 ? [] : y1 - y0 > 18 ? [y0 + (y1 - y0) * 0.25, y0 + (y1 - y0) * 0.75] : [(y0 + y1) / 2];

        // ---- the sides (flat: u = z from the back, v = y) ----
        double Dtop = Dup;
        double sideTw = Geo.Clamp(Dtop * 0.3, 1.2, 5);
        var side = new PathsD();
        if (hutch)
        {
            side.Add(Geo.Rect(0, 0, Dl, Hl));
            side.Add(Geo.Rect(0, Hl - 0.01, Dup, bodyTop));
            // A cove where the shelves step back from the worktop.
            double cove = Math.Min(Dl - Dup, Math.Min(4, (bodyTop - Hl) * 0.3));
            if (cove > 1.2)
                side = Geo.Union(side, Geo.Subtract([Geo.Rect(Dup - 0.01, Hl - 0.01, Dup + cove, Hl + cove)], [Geo.Circle(Dup + cove, Hl + cove, cove, 0.02)]));
        }
        else side.Add(Geo.Rect(0, 0, D, bodyTop));
        side.Add(Geo.Rect(Dtop / 2 - sideTw / 2, bodyTop - 0.01, Dtop / 2 + sideTw / 2, bodyTop + layers * T));
        var sideHoles = new PathsD();
        if (feet) sideHoles.Add(WoodFace.RoundRect(Dl * 0.22, -3, Dl * 0.78, kick - 1.2, Math.Min(1.2, Dl * 0.2)));
        foreach (var (y, z1, wt) in plates)
        {
            var (tw, at) = Tabs(TabSpan(z1, wt));
            foreach (var c in at) sideHoles.Add(Geo.Rect(zb + c - (tw + f) / 2, y - f / 2, zb + c + (tw + f) / 2, y + T + f / 2));
        }
        if (back)
            foreach (var (y0, y1, _) in gaps)
                foreach (var yc in TabHeights(y0, y1))
                {
                    double bh = TabH(y0, y1);
                    sideHoles.Add(Geo.Rect(BackIn - f / 2, yc - (bh + f) / 2, BackIn + T + f / 2, yc + (bh + f) / 2));
                }
        // The front's tabs, between the plates below its top.
        double frontTop = hutch ? Hl - T : bodyTop;
        var frontTabs = new List<(double Y, double H)>();
        if (hasFront)
        {
            var spans = new List<(double, double)>();
            foreach (var (y0, y1, _) in gaps) if (y1 <= frontTop + 1e-6) spans.Add((y0, y1));
            foreach (var (y0, y1) in spans)
                foreach (var yc in TabHeights(y0, y1)) frontTabs.Add((yc, TabH(y0, y1)));
            foreach (var (yc, bh) in frontTabs)
                sideHoles.Add(Geo.Rect(Dl - FrontIn - T - f / 2, yc - (bh + f) / 2, Dl - FrontIn + f / 2, yc + (bh + f) / 2));
        }
        var sideFace = new PathsD { Geo.Rect(0, 0, Dl, bodyTop) };
        var (se, sl) = SideDetail(m, look, Dl, bodyTop, kick, hutch ? Hl : 0, hutch ? Dup : Dl);
        m.Add("Left side", side, sideHoles, [Pose.Left(T / 2, 0, 0)], grainAlongU: false, face: sideFace, etched: se, lines: sl);
        PathsD Mirror(PathsD a) => Geo.Map(a, p => new PointD(Dl - p.x, p.y));
        var (se2, sl2) = SideDetail(m, look, Dl, bodyTop, kick, hutch ? Hl : 0, hutch ? Dup : Dl);
        m.Add("Right side", Mirror(side), Mirror(sideHoles), [Pose.Right(W - T / 2, 0, Dl)], grainAlongU: false, face: Mirror(sideFace), etched: Mirror(se2), lines: Geo.Map(sl2, p => new PointD(Dl - p.x, p.y)));

        // ---- plates (flat: u = x from the left side's inside, v = from the front to the back) ----
        double Li = W - 2 * T;
        for (int pi = 0; pi < plates.Count; pi++)
        {
            var (y, z1, worktop) = plates[pi];
            double depth = Math.Min(z1, Dl) - zb;
            var (tw, at) = Tabs(TabSpan(z1, worktop));
            double lip = worktop ? Geo.Clamp(W * 0.05, 1.2, 2) : 0;
            var plate = new PathsD { Geo.Rect(0, lip, Li, lip + depth) };
            if (lip > 0) plate.Add(Geo.Rect(-T - lip, 0, Li + T + lip, lip + 0.01));
            foreach (var c in at)
            {
                double v = lip + depth - c;
                plate.Add(Geo.Rect(-T, v - tw / 2, 0.01, v + tw / 2));
                plate.Add(Geo.Rect(Li - 0.01, v - tw / 2, Li + T, v + tw / 2));
            }
            string name = worktop ? "Worktop" : Math.Abs(y - kick) < 1e-9 ? "Bottom" : "Shelf";
            m.Add(name, plate, RowNotches(rows[pi], lip + depth, T, f), ribs: RowRibs(m, rows[pi], lip + depth), poses: [Pose.Up(T, y + T / 2, zb + depth + lip)], grainAlongU: true, face: [Geo.Rect(-T - lip, 0, Li + T + lip, lip + depth)]);
        }

        // ---- the back (flat: u = x from the left side's inside, v = y from the bottom plate) ----
        var rnd = m.Rnd;
        if (back)
        {
            double y0 = kick, Hb = bodyTop - kick;
            var bk = new PathsD { Geo.Rect(0, 0, Li, Hb) };
            var etched = new PathsD();
            var lines = new List<PathD>();
            for (int i = 0; i < gaps.Count; i++)
            {
                var (g0, g1, _) = gaps[i];
                foreach (var yc in TabHeights(g0, g1))
                {
                    double bh = TabH(g0, g1), v = yc - y0;
                    bk.Add(Geo.Rect(-T, v - bh / 2, 0.01, v + bh / 2));
                    bk.Add(Geo.Rect(Li - 0.01, v - bh / 2, Li + T, v + bh / 2));
                }
                // What stands on the shelves, where it can be seen.
                bool shows = seeThrough && (!hutch || i > 0);
                if (shows && rows[i] is null) Contents.Fill(it.Contents, 0, Li, g0 - y0, g1 - y0, rnd, etched, lines);
            }
            if (bk.Count == 1) { m.Bad($"The shelves are too close together for the back to lock in: have fewer shelves or make the {what} taller."); return; }
            m.Add("Back", bk, [], [Pose.Front(T, y0, BackIn + T / 2)], grainAlongU: false, face: seeThrough && rows.All(r => r is null) ? [] : [Geo.Rect(0, 0, Li, Hb)], etched: etched, lines: lines);
        }
        else if (!hasFront) m.Note("With neither a back nor a front, glue the shelves in square.");
        AddRows(m, rows, i => plates[i].Y + T, zb, it.Contents);

        // ---- the front (flat: u = x from the left side's inside, v = y) ----
        if (hasFront)
        {
            var fr = new PathsD { Geo.Rect(0, 0, Li, frontTop) };
            foreach (var (yc, bh) in frontTabs)
            {
                fr.Add(Geo.Rect(-T, yc - bh / 2, 0.01, yc + bh / 2));
                fr.Add(Geo.Rect(Li - 0.01, yc - bh / 2, Li + T, yc + bh / 2));
            }
            var holes = new PathsD();
            if (feet) holes.Add(WoodFace.RoundRect(Li * 0.14, -3, Li * 0.86, kick - 1.2, Math.Min(1.4, Li * 0.1)));
            var etched = new PathsD();
            var lines = new List<PathD>();
            var leaves = Relief.Use(m) ? new List<Leaf>() : null;
            Face(m, look, front, Li, kick, frontTop, holes, etched, lines, leaves);
            if (leaves is { Count: > 0 }) Relief.Add(m, look, leaves, new V3(T, 0, Dl - FrontIn), etched, lines);
            m.Add("Front", fr, holes, [Pose.Front(T, 0, Dl - FrontIn - T / 2)], grainAlongU: false, face: metal ? [] : [Geo.Rect(0, 0, Li, frontTop)], etched: etched, lines: lines);
        }

        // ---- the top: one or two layers, over the sides' tabs ----
        double ohs = Geo.Clamp(W * 0.05, 1.1, 2.2), step = Geo.Clamp(W * 0.04, 0.8, 1.6);
        double ohf = ohs;
        var crownTabs = new List<double>();
        // An altar's cross and candlesticks stand on it, their tabs down through slots in the top.
        var standing = front == CabinetFront.Altar && !metal && layers == 1 && hc == 0 ? AltarPieces(m, look, W, Dtop, T) : [];
        double crownTw = Geo.Clamp(W * 0.18, 1.5, 4), zc = Dtop - T / 2 - Math.Max(0, ohf < 1.2 ? 1.2 - ohf : 0);
        if (hc > 0)
        {
            // Two tabs clear of the sides' slots, or one in the middle on a narrow cabinet.
            double inX = Math.Max(W * 0.28, T + 1.3 + crownTw / 2 + f / 2);
            if (W - 2 * inX >= crownTw + 1.3 + f) crownTabs.AddRange([inX, W - inX]);
            else crownTabs.Add(W / 2);
        }
        for (int k = 0; k < layers; k++)
        {
            double o = ohs + k * step, of = ohf + k * step;
            double Wt = W + 2 * o, Dt = Dtop + of;
            var slots = new PathsD();
            foreach (var xs in new[] { T / 2, W - T / 2 })
            {
                double u = xs + o, v = Dt - Dtop / 2;
                slots.Add(Geo.Rect(u - sw / 2, v - (sideTw + f) / 2, u + sw / 2, v + (sideTw + f) / 2));
            }
            foreach (var x in crownTabs) slots.Add(Geo.Rect(x + o - (crownTw + f) / 2, Dt - zc - sw / 2, x + o + (crownTw + f) / 2, Dt - zc + sw / 2));
            if (k == layers - 1)
                foreach (var sp in standing) slots.Add(Geo.Rect(sp.X + o - (sp.TabW + f) / 2, Dt - sp.Z - sw / 2, sp.X + o + (sp.TabW + f) / 2, Dt - sp.Z + sw / 2));
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (front == CabinetFront.Altar && k == layers - 1) Cloth(look, m.Theme, Wt, Dt, ClothW(W, T, Geo.Clamp(it.Columns, 1, 6)), etched, lines);
            m.Add(layers == 1 ? "Top" : k == 0 ? "Top (under)" : "Top", [Geo.Rect(0, 0, Wt, Dt)], slots, [Pose.Up(-o, bodyTop + k * T + T / 2, Dtop + of)],
                grainAlongU: true, etched: etched, lines: lines);
        }

        foreach (var sp in standing)
            m.Add(sp.Name, sp.Shape, [], [Pose.Front(sp.X - sp.W / 2, bodyTop + layers * T, sp.Z)], grainAlongU: false, face: [], lines: sp.Lines);
        if (standing.Count > 0) m.Note("Push the cross and the candlesticks into the slots in the top.");
        if (front == CabinetFront.Altar && !metal && !m.PushFit) AltarCloth(m, look, W, T, bodyTop, kick, Dl);

        // ---- open shelves: a band of tracery glued across the front under the top ----
        if (open && !hutch && !metal && !m.PushFit && m.Theme != TerrainThemes.Cave) Tracery(m, look, W, T, bodyTop, Dl);

        // ---- a pediment or an arch on top, its tabs down through the top ----
        if (hc > 0)
        {
            double Wc = W, hb = Geo.Clamp(hc * 0.3, 1.0, 1.4), yTop = bodyTop + layers * T;
            var shape = new PathsD { Geo.Rect(0, 0, Wc, hb) };
            if (crown == Crown.Pediment) shape.Add([new(0, hb - 0.01), new(Wc, hb - 0.01), new(Wc / 2, hc)]);
            else
            {
                var arc = new PathD { new(0, hb - 0.01) };
                double rise = hc - hb, r = (Wc * Wc / 4 + rise * rise) / (2 * rise), cy = hb + rise - r, a0 = Math.Asin(Geo.Clamp(Wc / 2 / r, -1, 1));
                arc.AddRange(Geo.Arc(Wc / 2, cy, r, Math.PI / 2 - a0, Math.PI / 2 + a0, 0.02).Reverse());
                arc.Add(new PointD(Wc, hb - 0.01));
                shape.Add(Geo.Tidy([arc]).FirstOrDefault() ?? arc);
            }
            foreach (var x in crownTabs) shape.Add(Geo.Rect(x - crownTw / 2, -layers * T, x + crownTw / 2, 0.01));
            if (finR > 0)
            {
                // Finials: on a neck at the top, and on a little block at each end.
                double neck = Math.Max(1.0, finR * 1.3);
                shape.Add(Geo.Rect(Wc / 2 - neck / 2, hc - 0.6, Wc / 2 + neck / 2, hc + 0.7));
                shape.Add(Geo.Circle(Wc / 2, hc + 0.6 + finR, finR, 0.02));
                foreach (var x in new[] { finR + 0.15, Wc - finR - 0.15 })
                {
                    shape.Add(Geo.Rect(x - finR, hb - 0.01, x + finR, hb + 0.7));
                    shape.Add(Geo.Circle(x, hb + 0.6 + finR * 0.85, finR * 0.85, 0.02));
                }
                shape = Geo.Solid(shape);
            }
            var etched = new PathsD();
            var lines = new List<PathD>();
            // A moulding line and something in the middle: a disc, a clock's finial, a star.
            lines.Add([new(0, hb), new(Wc, hb)]);
            double rr = Math.Min((hc - hb) * 0.32, Wc * 0.1);
            if (rr > 0.5)
            {
                double cy = hb + (hc - hb) * 0.38;
                if (look == FrontLook.Carved) lines.Add(Geo.Loop(Fronts.Star(Wc / 2, cy, rr, rr * 0.5, 8)));
                else if (look == FrontLook.Gothic) lines.Add(Geo.Loop(Trefoil(Wc / 2, cy, rr)));
                else { lines.Add(WoodFace.Dot(Wc / 2, cy, rr)); lines.Add(WoodFace.Dot(Wc / 2, cy, rr * 0.45)); }
            }
            m.Add(crown == Crown.Pediment ? "Pediment" : "Arch", shape, [], [Pose.Front(0, yTop, zc)], grainAlongU: true, face: [Geo.Rect(0, 0, Wc, hc)], etched: etched, lines: lines);
        }

        m.Note(Assembly(front, back, hutch, hc > 0, layers));
    }

    /// <summary>
    /// What stands on an altar: a budded cross on a stepped foot in the middle (where the theme's symbol is a cross) and a
    /// candlestick each side, each flat with a tab under it (u from its left edge, v up from the top's surface).
    /// </summary>
    static List<(string Name, double X, double Z, double W, double TabW, PathsD Shape, List<PathD> Lines)> AltarPieces(Maker m, FrontLook look, double W, double D, double T)
    {
        var list = new List<(string, double, double, double, double, PathsD, List<PathD>)>();
        double H = m.Item.HeightMm, z = D * 0.4;
        if (W < 14 || D < 6) return list;
        double tw = 2.0;
        bool cross = m.Theme is TerrainThemes.Medieval or TerrainThemes.Gothic;
        if (cross)
        {
            double ch = Geo.Clamp(H * 0.6, 6, 11), cw = ch * 0.62, bar = Math.Max(1.4, ch * 0.15), fw = Math.Min(cw * 0.7, 5);
            double cx = cw / 2, armY = ch * 0.66, foot = 2.2;
            var c = new PathsD
            {
                Geo.Rect(cx - fw / 2, 0, cx + fw / 2, foot * 0.5),
                Geo.Rect(cx - fw * 0.34, foot * 0.5 - 0.01, cx + fw * 0.34, foot),
                Geo.Rect(cx - bar / 2, foot - 0.01, cx + bar / 2, ch - bar * 0.6),
                Geo.Rect(bar * 0.6, armY - bar / 2, cw - bar * 0.6, armY + bar / 2),
                Geo.Rect(cx - tw / 2, -T, cx + tw / 2, 0.01),
            };
            // Buds at the ends of the arms and the top, and a ring where they cross.
            foreach (var (bx, by) in new[] { (bar * 0.7, armY), (cw - bar * 0.7, armY), (cx, ch - bar * 0.7) }) c.Add(Geo.Circle(bx, by, bar * 0.7, 0.02));
            var lines = new List<PathD> { Geo.Loop(Geo.Circle(cx, armY, bar * 0.95, 0.02)), WoodFace.Dot(cx, armY, bar * 0.3) };

            list.Add(("Altar cross", W / 2, z, cw, tw, Geo.Solid(c), lines));
        }
        // The candlesticks: a foot, a stem with a knob, a drip pan and the candle with its flame.
        double sh = Geo.Clamp(H * 0.45, 6, 9), sw2 = 3.2, sx = sw2 / 2;
        var stick = new PathsD
        {
            WoodFace.RoundRect(0, 0, sw2, 1.2, 0.4),
            Geo.Rect(sx - 0.65, 1.1, sx + 0.65, sh * 0.5),
            Geo.Circle(sx, sh * 0.32, 1.0, 0.02),
            WoodFace.RoundRect(sx - 1.4, sh * 0.5 - 0.01, sx + 1.4, sh * 0.5 + 1.1, 0.3),
            Geo.Rect(sx - 0.65, sh * 0.5 + 1.0, sx + 0.65, sh - 1.7),
            Rugs.Ellipse(sx, sh - 1.05, 0.68, 0.95),
            Geo.Rect(sx - tw / 2, -T, sx + tw / 2, 0.01),
        };
        var sl = new List<PathD> { new() { new(sx - 0.65, sh - 2.1), new(sx + 0.65, sh - 2.1) } };
        double dx = cross ? Math.Min(W * 0.3, W / 2 - 4) : W * 0.25;
        foreach (var x in new[] { W / 2 - dx, W / 2 + dx }) list.Add(("Candlestick", x, z, sw2, tw, Geo.Solid(stick), sl));
        return list;
    }

    /// <summary>The cloth hanging down the altar's front from the top: a cross worked on it and a fringe along its end, glued on.</summary>
    static void AltarCloth(Maker m, FrontLook look, double W, double T, double bodyTop, double kick, double D)
    {
        double cw = ClothW(W, T, Geo.Clamp(m.Item.Columns, 1, 6)), ch = Math.Min((bodyTop - kick) * 0.7, cw * 1.3), fringe = Math.Min(1.2, ch * 0.12);
        if (cw < 4 || ch < 4) return;
        var shape = new PathD { new(0, ch), new(0, fringe) };
        // A fringe of teeth along the bottom.
        int n = Math.Max(3, (int)Math.Round(cw / 1.0));
        for (int i = 0; i < n; i++)
        {
            double a = cw * i / n, b = cw * (i + 1) / n;
            shape.Add(new((a + b) / 2, 0));
            shape.Add(new(b, fringe));
        }
        shape.Add(new(cw, ch));
        var etched = new PathsD();
        var lines = new List<PathD>();
        etched.AddRange(Fronts.Outline(0.5, fringe + 0.5, cw - 0.5, ch - 0.5, 0.3));
        for (double x = 0.6; x < cw - 0.4; x += 0.5) lines.Add([new(x, fringe * 0.6), new(x, fringe + 0.2)]);
        double r = Math.Min(cw * 0.26, (ch - fringe) * 0.24);
        if (m.Theme is TerrainThemes.Medieval or TerrainThemes.Gothic) { if (r > 0.6) lines.Add(Cross(cw / 2, fringe + (ch - fringe) * 0.5, r)); }
        else Symbol(look, m.Theme, cw / 2, fringe + (ch - fringe) * 0.5, r, etched, lines);
        m.Add("Altar cloth", [shape], [], [Pose.Front(W / 2 - cw / 2, bodyTop - ch, D - FrontIn + T / 2)], grainAlongU: false, face: [], etched: etched, lines: lines).Glue();
        m.Note("Glue the cloth down the altar's front from under the top.");
    }

    /// <summary>
    /// A band glued across the front of open shelves under the top: a row of arches (pointed, or round on a carved
    /// look) cut open along its bottom edge between posts over the sides, a moulding scored along it and round each arch.
    /// </summary>
    static void Tracery(Maker m, FrontLook look, double W, double T, double bodyTop, double D)
    {
        double th = Geo.Clamp(m.Item.HeightMm * 0.14, 3, 5.5), band = Math.Max(1.4, th * 0.3), gap = 1.0;
        double x0 = T + 0.3, x1 = W - T - 0.3;
        int n = Math.Max(2, (int)Math.Round((x1 - x0) / 5.0));
        double aw = (x1 - x0 - (n - 1) * gap) / n;
        if (aw < 2.4 || th - band < 1.6) return;
        var top = look == FrontLook.Carved ? BackTop.Arched : BackTop.Pointed;
        var cuts = new PathsD();
        var lines = new List<PathD> { new() { new(0, th - band * 0.45), new(W, th - band * 0.45) } };
        for (int i = 0; i < n; i++)
        {
            double a = x0 + i * (aw + gap), b = a + aw;
            var arch = Shapes.Topped(top, a, -1, b, th - band, Math.Min(aw * 0.5, th - band));
            cuts.Add(arch);
            // A groove round the arch, and a little cusp scored in each spandrel.
            lines.AddRange(Geo.ClipLines([Geo.Loop(Geo.Offset([arch], 0.45).FirstOrDefault() ?? arch)], [Geo.Rect(0, 0.01, W, th)]));
            if (i > 0) lines.Add(WoodFace.Dot(a - gap / 2, th - band - 0.1, Math.Min(0.35, gap * 0.3)));
        }
        var plate = Geo.Subtract([Geo.Rect(0, 0, W, th)], cuts);
        m.Add("Tracery", plate, [], [Pose.Front(0, bodyTop - th, D + T / 2)], grainAlongU: true, face: [], lines: lines).Glue();
        m.Note("Glue the tracery across the front under the top.");
    }

    /// <summary>How far a row of things on a shelf stops short of the sides (mm), and how far below the shelf over it.</summary>
    public const double RowEnd = 0.2, RowHeadroom = 0.5;

    /// <summary>The notches at a shelf's back edge (at v = <paramref name="backV"/>, flat) for a row's tabs; none without a row.</summary>
    public static PathsD RowNotches(Contents.ShelfRow? row, double backV, double T, double f)
    {
        var n = new PathsD();
        if (row is null) return n;
        foreach (var c in row.Tabs) n.Add(Geo.Rect(RowEnd + c - (row.TabW + f) / 2, backV - (T + f), RowEnd + c + (row.TabW + f) / 2, backV + 1));
        return n;
    }

    /// <summary>
    /// Push fit: a grip rib on the front wall of each notch, pointing back, so the row's tab is squeezed against
    /// the cabinet's back (the notch's other side) and holds without glue.
    /// </summary>
    public static PathsD RowRibs(Maker m, Contents.ShelfRow? row, double backV)
    {
        var ribs = new PathsD();
        if (row is null) return ribs;
        foreach (var c in row.Tabs)
            ribs.Add(Maker.Tooth(new PointD(RowEnd + c, backV - (m.T + m.Fit)), new PointD(1, 0), new PointD(0, 1), Math.Min(0.8, (row.TabW + m.Fit) / 3), m.Fit + m.Grip));
        return ribs;
    }

    /// <summary>
    /// The rows of things cut out to stand on the shelves, each with its back to the cabinet's back (at z =
    /// <paramref name="zBack"/>) and its tabs down in its shelf's notches (the shelf's top at <paramref name="shelfTop"/>).
    /// </summary>
    public static void AddRows(Maker m, Contents.ShelfRow?[] rows, Func<int, double> shelfTop, double zBack, ShelfContents what)
    {
        int count = rows.Count(r => r is not null), n = 0;
        if (count == 0) return;
        string name = what switch
        {
            ShelfContents.Bottles => "Bottles",
            ShelfContents.Jars => "Jars",
            ShelfContents.Plates => "Plates",
            ShelfContents.Scrolls => "Scrolls",
            _ => "Books",
        };
        for (int i = 0; i < rows.Length; i++)
            if (rows[i] is { } row)
                m.Add(count > 1 ? $"{name} {++n}" : name, row.Outline, [], [Pose.Front(m.T + RowEnd, shelfTop(i), zBack + m.T / 2)], grainAlongU: true, face: [],
                    etched: row.Etched, lines: row.Lines);
        m.Note($"Stand the {name.ToLowerInvariant()} on {(count > 1 ? "their shelves" : "the shelf")}, their tabs pushed down into the notches at the back" + (m.PushFit ? "." : ", and glue them in place."));
    }

    static string Name(CabinetFront f) => f switch
    {
        CabinetFront.Drawers => "chest of drawers",
        CabinetFront.DrawersOverDoors => "dresser",
        CabinetFront.Glass => "display cabinet",
        CabinetFront.Open => "shelves",
        CabinetFront.Hutch => "dresser",
        CabinetFront.Panelled => "counter",
        CabinetFront.Locker => "locker",
        CabinetFront.Tech => "machine",
        CabinetFront.Clock => "clock",
        CabinetFront.Altar => "altar",
        _ => "cupboard",
    };

    static string Assembly(CabinetFront f, bool back, bool hutch, bool crown, int layers) =>
        "Push the bottom" + (hutch ? ", worktop and shelves" : "") + (back ? ", back" : "") + (f == CabinetFront.Open ? "" : " and front") +
        " into one side, add the other side, then press the top" + (layers > 1 ? " layers" : "") + " down onto the sides' tabs" + (crown ? " and stand the crown in its slots." : ".");

    /// <summary>The detail on a side: a panel on fine furniture, rivets and a seam on metal.</summary>
    static (PathsD, List<PathD>) SideDetail(Maker m, FrontLook look, double D, double H, double kick, double Hl, double Dup)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        double y0 = kick + 1.2, y1 = (Hl > 0 ? Hl : H) - 1.2, x0 = 1.0 + m.T + 0.6, x1 = D - 1.0 - m.T - 0.6;
        if (look is FrontLook.Gothic or FrontLook.Carved && x1 - x0 > 3 && y1 - y0 > 5) etched.AddRange(Fronts.Outline(x0, y0, x1, y1));
        if (look == FrontLook.Metal && x1 - x0 > 2 && y1 - y0 > 3)
        {
            // Vents low down (the panel line and rivets come with the metal detail).
            for (double y = y0 + 1.5; y < y0 + Math.Min(5, (y1 - y0) * 0.3); y += 0.8) etched.Add(Geo.Rect(x0 + 1, y, x1 - 1, y + 0.3));
        }
        _ = Dup;
        return (etched, lines);
    }

    /// <summary>The front's face: doors, drawers and the rest in the area over the kick (y from kick to top).</summary>
    static void Face(Maker m, FrontLook look, CabinetFront front, double W, double kick, double top, PathsD holes, PathsD etched, List<PathD> lines, List<Leaf>? leaves = null)
    {
        var it = m.Item;
        var rnd = m.Rnd;
        double g = 0.5, x0 = g, x1 = W - g, y0 = kick + g * 0.5, y1 = top - g;
        // The kick board under the doors.
        etched.Add(Geo.Rect(-1, kick - Fronts.Groove / 2, W + 1, kick + Fronts.Groove / 2));
        int cols = Geo.Clamp(it.Columns, 1, 6), rows = Geo.Clamp(it.Rows, 1, 12);
        switch (front)
        {
            case CabinetFront.Drawers:
                DrawerGrid(look, x0, y0, x1, y1, rows, cols, etched, lines, leaves);
                break;
            case CabinetFront.DrawersOverDoors:
            case CabinetFront.Hutch:
            {
                double dh = Geo.Clamp((y1 - y0) * 0.24, 2.2, 5);
                DrawerGrid(look, x0, y1 - dh, x1, y1, 1, cols, etched, lines, leaves);
                Doors(look, x0, y0, x1, y1 - dh - g, cols, rnd, etched, lines, leaves: leaves);
                break;
            }
            case CabinetFront.Glass:
            case CabinetFront.Lattice:
            {
                Doors(look, x0, y0, x1, y1, cols, rnd, etched, lines, handles: true, glass: true);
                double dw = (x1 - x0) / cols, frame = Geo.Clamp(dw * 0.16, 1.4, 2.4);
                for (int c = 0; c < cols; c++)
                {
                    double a = x0 + c * dw + frame, b = x0 + (c + 1) * dw - frame, ya = y0 + frame * 1.4, yb = y1 - frame;
                    Func<double, double, double, double, PathD>? topShape = look == FrontLook.Gothic ? Fronts.PointedRect : look == FrontLook.Carved ? Fronts.ArchedRect : null;
                    holes.AddRange(front == CabinetFront.Glass ? Fronts.Panes(a, ya, b, yb, 3.2, 1.0, topShape) : Fronts.Lattice(a, ya, b, yb, 3.4, 1.0));
                }
                break;
            }
            case CabinetFront.Panelled:
            {
                double pw = (x1 - x0) / cols;
                for (int c = 0; c < cols; c++)
                    if (leaves is not null) leaves.Add(new Leaf(x0 + c * pw + 0.2, y0, x0 + (c + 1) * pw - 0.2, y1, LeafKind.Panel, Handle: false));
                    else Fronts.Panel(look, x0 + c * pw + 0.2, y0, x0 + (c + 1) * pw - 0.2, y1, etched, lines);
                break;
            }
            case CabinetFront.Locker:
            {
                double dw = (x1 - x0) / cols;
                for (int c = 0; c < cols; c++)
                {
                    double a = x0 + c * dw + 0.2, b = x0 + (c + 1) * dw - 0.2;
                    Fronts.Door(FrontLook.Metal, a, y0, b, y1, hingeLeft: true, rnd, etched, lines);
                    // Vents top and bottom, cut through where there's room (bars between them a laser can leave), and a name plate.
                    double vw = (b - a) * 0.6, cx = (a + b) / 2;
                    if (Math.Abs(vw - m.SlotW) < 0.15) vw -= 0.3;
                    bool cutVents = b - a >= 3.5 && y1 - y0 >= 16;
                    double vTop = cutVents ? y1 - 1.5 - 2 * 1.6 - 0.6 : y1 - Math.Min(4, (y1 - y0) * 0.15), vBot = cutVents ? y0 + 1.5 : y0 + Math.Min(2.5, (y1 - y0) * 0.1);
                    foreach (var (vy, n) in new[] { (vTop, 3), (vBot, 2) })
                        for (int k = 0; k < n; k++)
                        {
                            if (cutVents) holes.Add(WoodFace.RoundRect(cx - vw / 2, vy + k * 1.6, cx + vw / 2, vy + k * 1.6 + 0.6, 0.29));
                            else etched.Add(Geo.Rect(cx - vw / 2, vy + k * 0.7, cx + vw / 2, vy + k * 0.7 + 0.3));
                        }
                    double ny = cutVents ? vTop - 1.8 : y1 - Math.Min(6, (y1 - y0) * 0.24);
                    if (b - a > 4) lines.Add(Geo.Loop(Geo.Rect(cx - vw / 2, ny, cx + vw / 2, ny + 1.0)));
                }
                break;
            }
            case CabinetFront.Tech:
            {
                double uh = (y1 - y0) / rows;
                for (int r = 0; r < rows; r++)
                {
                    double a = y0 + r * uh + 0.15, b = y0 + (r + 1) * uh - 0.15;
                    lines.Add(Geo.Loop(Fronts.Chamfered(x0, a, x1, b, Math.Min(0.4, uh * 0.15))));
                    Contents.Unit(x0 + 0.3, x1 - 0.3, a + 0.2, b - 0.2, rnd, etched, lines);
                }
                break;
            }
            case CabinetFront.Altar:
            {
                double pw = (x1 - x0) / Math.Max(3, cols | 1);
                int n = (int)Math.Round((x1 - x0) / pw);
                for (int c = 0; c < n; c++)
                {
                    double a = x0 + c * pw + 0.2, b = x0 + (c + 1) * pw - 0.2;
                    if (c == n / 2) { etched.AddRange(Fronts.Outline(a, y0, b, y1)); Symbol(look, m.Theme, (a + b) / 2, (y0 + y1) / 2, Math.Min(b - a, y1 - y0) * 0.36, etched, lines); }
                    else if (leaves is not null) leaves.Add(new Leaf(a, y0, b, y1, LeafKind.Panel, Handle: false));
                    else Fronts.Panel(look, a, y0, b, y1, etched, lines);
                }
                break;
            }
            default:
                Doors(look, x0, y0, x1, y1, cols, rnd, etched, lines, leaves: leaves);
                break;
        }
    }

    /// <summary><paramref name="cols"/> doors side by side, hung from the outer edges (a pair meets in the middle).</summary>
    static void Doors(FrontLook look, double x0, double y0, double x1, double y1, int cols, Random rnd, PathsD etched, List<PathD> lines, bool handles = true, bool glass = false, List<Leaf>? leaves = null)
    {
        double dw = (x1 - x0) / cols;
        for (int c = 0; c < cols; c++)
        {
            double a = x0 + c * dw + 0.15, b = x0 + (c + 1) * dw - 0.15;
            bool hingeLeft = cols == 1 ? true : c < cols / 2.0;
            if (glass)
            {
                // Just the edge and a handle: the panes are cut through.
                etched.AddRange(Fronts.Outline(a, y0, b, y1));
                double hx = hingeLeft ? b - Math.Min(0.8, (b - a) * 0.12) : a + Math.Min(0.8, (b - a) * 0.12);
                if (handles) lines.Add(WoodFace.Dot(hx, (y0 + y1) / 2, 0.3));
            }
            else if (leaves is not null) leaves.Add(new Leaf(a, y0, b, y1, LeafKind.Door, hingeLeft, handles));
            else Fronts.Door(look, a, y0, b, y1, hingeLeft, rnd, etched, lines, handles);
        }
    }

    /// <summary>Rows of drawers, deeper towards the bottom, in columns.</summary>
    static void DrawerGrid(FrontLook look, double x0, double y0, double x1, double y1, int rows, int cols, PathsD etched, List<PathD> lines, List<Leaf>? leaves = null)
    {
        // Each row a little deeper than the one above it.
        var weights = Enumerable.Range(0, rows).Select(i => 1 + 0.12 * (rows - 1 - i)).ToArray();
        double sum = weights.Sum(), y = y0, dw = (x1 - x0) / cols;
        for (int r = 0; r < rows; r++)
        {
            double h = (y1 - y0) * weights[r] / sum;
            for (int c = 0; c < cols; c++)
                if (leaves is not null) leaves.Add(new Leaf(x0 + c * dw + 0.15, y + 0.15, x0 + (c + 1) * dw - 0.15, y + h - 0.15, LeafKind.Drawer));
                else Fronts.Drawer(look, x0 + c * dw + 0.15, y + 0.15, x0 + (c + 1) * dw - 0.15, y + h - 0.15, etched, lines);
            y += h;
        }
    }

    /// <summary>An altar cloth: a band across the top, hanging over the front, with a fringe.</summary>
    /// <summary>How wide an altar's cloth is: inside the middle one of its front's panels (between the carved ones either side).</summary>
    static double ClothW(double W, double T, int cols) => Math.Min(Math.Min(W * 0.35, 9), (W - 2 * T) / Math.Max(3, cols | 1) - 1.2);

    static void Cloth(FrontLook look, string theme, double W, double D, double cw, PathsD etched, List<PathD> lines)
    {
        double x0 = W / 2 - cw / 2;
        etched.AddRange(Fronts.Outline(x0, -1, x0 + cw, D + 1, 0.3));
        for (double x = x0 + 0.6; x < x0 + cw - 0.4; x += 0.5) lines.Add([new(x, 0), new(x, 0.7)]);
        double r = Math.Min(cw * 0.22, D * 0.18);
        if (r > 0.6 && look == FrontLook.Plank && theme is PhotonCanvas.Addon.Terrain.TerrainThemes.Dungeon or PhotonCanvas.Addon.Terrain.TerrainThemes.Cave)
        {
            if (r >= 0.8) Symbol(look, theme, W / 2, D * 0.45, r, etched, lines);
        }
        else if (r > 0.6) lines.Add(Cross(W / 2, D * 0.45, r));
    }

    static PathD Cross(double cx, double cy, double r) =>
        Geo.Loop([new(cx - r * 0.18, cy - r), new(cx + r * 0.18, cy - r), new(cx + r * 0.18, cy + r * 0.3), new(cx + r * 0.6, cy + r * 0.3), new(cx + r * 0.6, cy + r * 0.62),
                  new(cx + r * 0.18, cy + r * 0.62), new(cx + r * 0.18, cy + r), new(cx - r * 0.18, cy + r), new(cx - r * 0.18, cy + r * 0.62), new(cx - r * 0.6, cy + r * 0.62),
                  new(cx - r * 0.6, cy + r * 0.3), new(cx - r * 0.18, cy + r * 0.3)]);

    /// <summary>
    /// A symbol for the middle of an altar, a throne's back or a banner: a sun for the desert, a cog for sci-fi,
    /// a skull in a dungeon, a spiral in a ring of dots (a cave painting) in a cave, a cross otherwise.
    /// </summary>
    public static void Symbol(FrontLook look, string? theme, double cx, double cy, double r, PathsD etched, List<PathD> lines)
    {
        if (r < 0.8) return;
        if (look == FrontLook.Plank && theme == PhotonCanvas.Addon.Terrain.TerrainThemes.Dungeon) { Skull(cx, cy, r, etched, lines); return; }
        if (look == FrontLook.Plank && theme == PhotonCanvas.Addon.Terrain.TerrainThemes.Cave) { CaveSpiral(cx, cy, r, etched, lines); return; }
        switch (look)
        {
            case FrontLook.Carved:
                lines.Add(Geo.Loop(Fronts.Star(cx, cy, r, r * 0.45, 12)));
                lines.Add(WoodFace.Dot(cx, cy, r * 0.32));
                break;
            case FrontLook.Metal:
                lines.Add(Geo.Loop(Cog(cx, cy, r, 10)));
                etched.Add(Geo.Circle(cx, cy, r * 0.3, 0.01));
                break;
            case FrontLook.Gothic:
                lines.Add(WoodFace.Dot(cx, cy, r));
                lines.Add(Cross(cx, cy, r * 0.8));
                break;
            default:
                lines.Add(Cross(cx, cy, r));
                break;
        }
    }

    /// <summary>A skull: the cranium and jaw scored, the eyes and nose etched, the teeth lined.</summary>
    static void Skull(double cx, double cy, double r, PathsD etched, List<PathD> lines)
    {
        var head = Geo.Solid([Geo.Circle(cx, cy + r * 0.18, r * 0.66, 0.01), WoodFace.RoundRect(cx - r * 0.4, cy - r * 0.8, cx + r * 0.4, cy - r * 0.05, r * 0.15)]);
        lines.AddRange(head.Select(Geo.Loop));
        foreach (var s in new[] { -1, 1 }) etched.Add(Rugs.Ellipse(cx + s * r * 0.25, cy + r * 0.08, r * 0.17, r * 0.2));
        etched.Add(new PathD { new(cx - r * 0.09, cy - r * 0.2), new(cx + r * 0.09, cy - r * 0.2), new(cx, cy - r * 0.38) });
        if (r < 1.4) return;
        lines.Add([new(cx - r * 0.3, cy - r * 0.55), new(cx + r * 0.3, cy - r * 0.55)]);
        for (int i = -2; i <= 2; i++) lines.Add([new(cx + i * r * 0.12, cy - r * 0.45), new(cx + i * r * 0.12, cy - r * 0.68)]);
    }

    /// <summary>A spiral inside a ring of dots, like a cave painting.</summary>
    static void CaveSpiral(double cx, double cy, double r, PathsD etched, List<PathD> lines)
    {
        var p = new PathD();
        for (int i = 0; i <= 60; i++)
        {
            double t = i / 60.0, a = t * Math.PI * 5, rr = r * 0.7 * t;
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        lines.Add(p);
        for (int i = 0; i < 10; i++)
        {
            double a = i * Math.PI / 5;
            etched.Add(Geo.Circle(cx + Math.Cos(a) * r * 0.95, cy + Math.Sin(a) * r * 0.95, Math.Max(0.15, r * 0.1), 0.01));
        }
    }

    static PathD Cog(double cx, double cy, double r, int teeth)
    {
        var p = new PathD();
        for (int i = 0; i < teeth * 4; i++)
        {
            double a = 2 * Math.PI * i / (teeth * 4), rr = i % 4 is 0 or 1 ? r : r * 0.78;
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>A trefoil: three overlapping circles (gothic tracery).</summary>
    public static PathD Trefoil(double cx, double cy, double r)
    {
        var lobes = new PathsD();
        for (int i = 0; i < 3; i++)
        {
            double a = Math.PI / 2 + i * 2 * Math.PI / 3;
            lobes.Add(Geo.Circle(cx + Math.Cos(a) * r * 0.45, cy + Math.Sin(a) * r * 0.45, r * 0.55, 0.01));
        }
        return Geo.Union(lobes).OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();
    }
}
