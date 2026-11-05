using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>Bookshelves, and boxes with finger joints: chests and crates.</summary>
public static class Storage
{
    // ---- bookshelf ----

    /// <summary>
    /// Two sides, a bottom shelf and the shelves above it with tabs through the sides, a back panel
    /// with tabs through the sides (books etched on its face), and a top that the sides' tabs go up into.
    /// </summary>
    public static void Bookshelf(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        bool back = it.BackPanel;
        double bi = 1.0, z0 = back ? bi + T : 0, Ds = D - z0, kick = Math.Max(2.5, T + 1.5);
        double oh = Math.Clamp(W * 0.05, 1.2, 2.5);
        int n = Math.Clamp(it.Shelves, 0, 30);
        if (W - 2 * T < 4) { m.Bad($"The bookshelf is too narrow: make it at least {2 * T + 4:0.#} mm wide."); return; }
        if (Ds < 3) { m.Bad($"The bookshelf is too shallow{(back ? " for a back panel" : "")}: make it at least {z0 + 3:0.#} mm deep."); return; }
        double inside = H - T - (kick + T);
        double gap = (inside - n * T) / (n + 1);
        if (gap < 3) { m.Bad($"The shelves are too close together ({Math.Max(0, gap):0.#} mm): make the bookshelf taller or have fewer shelves."); return; }

        // Shelves' bottom faces: the bottom one, then evenly up to the top.
        var shelfY = new List<double> { kick };
        for (int i = 1; i <= n; i++) shelfY.Add(kick + T + i * gap + (i - 1) * T);
        double[] tabAt;
        double tw;
        if (Ds < 7) { tw = Math.Max(1.2, Ds * 0.4); tabAt = [Ds / 2]; }
        else { tw = Math.Clamp(Ds * 0.2, 1.2, 4); tabAt = [Ds * 0.25, Ds * 0.75]; }
        double sideTw = Math.Clamp(D * 0.3, 1.2, 5);
        // The gaps between shelves, for the back's tabs and the books.
        var gaps = new List<(double Y0, double Y1)>();
        for (int i = 0; i < shelfY.Count; i++) gaps.Add((shelfY[i] + T, i + 1 < shelfY.Count ? shelfY[i + 1] : H - T));

        // The books, cut out to stand on the shelves where there's room (drawn on the back otherwise).
        var rows = new Contents.ShelfRow?[gaps.Count];
        if (it.Books && Ds >= T + f + Maker.Bridge)
            for (int i = 0; i < gaps.Count; i++)
                rows[i] = Contents.Row(ShelfContents.Books, W - 2 * T - 2 * Cabinets.RowEnd, gaps[i].Y1 - gaps[i].Y0 - Cabinets.RowHeadroom, T, m.Rnd);

        // Sides.
        var side = new PathsD { Geo.Rect(0, 0, D, H - T), Geo.Rect(D / 2 - sideTw / 2, H - T - 0.01, D / 2 + sideTw / 2, H) };
        var sideHoles = new PathsD { WoodFace.RoundRect(D * 0.25, -3, D * 0.75, kick - 1.2, Math.Min(1, D * 0.25)) };
        foreach (var y in shelfY)
            foreach (var c in tabAt) sideHoles.Add(Geo.Rect(z0 + c - (tw + f) / 2, y - f / 2, z0 + c + (tw + f) / 2, y + T + f / 2));
        if (back)
            foreach (var (y0, y1) in gaps)
            {
                double bh = Math.Min((y1 - y0) * 0.5, 5), yc = (y0 + y1) / 2;
                sideHoles.Add(Geo.Rect(bi - f / 2, yc - (bh + f) / 2, bi + T + f / 2, yc + (bh + f) / 2));
            }
        m.Add("Side", side, sideHoles, [Pose.Left(T / 2, 0, 0), Pose.Left(W - T / 2, 0, 0)], grainAlongU: false, face: [Geo.Rect(0, 0, D, H - T)]);

        // Shelves (seen from above: v runs from the front to the back).
        double Li = W - 2 * T;
        var shelf = new PathsD { Geo.Rect(0, 0, Li, Ds) };
        foreach (var c in tabAt)
        {
            double v = Ds - c;
            shelf.Add(Geo.Rect(-T, v - tw / 2, 0.01, v + tw / 2));
            shelf.Add(Geo.Rect(Li - 0.01, v - tw / 2, Li + T, v + tw / 2));
        }
        // One shelf piece for all of them when none has books standing on it; otherwise each its own, notched for its row.
        if (rows.All(r => r is null)) m.Add("Shelf", shelf, [], shelfY.Select(y => Pose.Up(T, y + T / 2, D)), grainAlongU: true, face: [Geo.Rect(0, 0, Li, Ds)]);
        else
            for (int i = 0; i < shelfY.Count; i++)
                m.Add(i == 0 ? "Bottom shelf" : $"Shelf {i}", shelf, Cabinets.RowNotches(rows[i], Ds, T, f), ribs: Cabinets.RowRibs(m, rows[i], Ds), poses: [Pose.Up(T, shelfY[i] + T / 2, D)], grainAlongU: true, face: [Geo.Rect(0, 0, Li, Ds)]);

        // The back, books on its face between the shelves.
        if (back)
        {
            double y0 = kick, Hbk = H - T - kick;
            var bk = new PathsD { Geo.Rect(0, 0, Li, Hbk) };
            var etched = new PathsD();
            var lines = new List<PathD>();
            foreach (var (g0, g1) in gaps)
            {
                double bh = Math.Min((g1 - g0) * 0.5, 5), yc = (g0 + g1) / 2 - y0;
                bk.Add(Geo.Rect(-T, yc - bh / 2, 0.01, yc + bh / 2));
                bk.Add(Geo.Rect(Li - 0.01, yc - bh / 2, Li + T, yc + bh / 2));
                if (it.Books && rows[gaps.IndexOf((g0, g1))] is null) Books(0, Li, g0 - y0, g1 - y0, m.Rnd, etched, lines);
            }
            m.Add("Back", bk, [], [Pose.Front(T, y0, bi + T / 2)], grainAlongU: false, face: it.Books && rows.All(r => r is null) ? [] : [Geo.Rect(0, 0, Li, Hbk)], etched: etched, lines: lines);
        }
        else m.Note(m.PushFit ? "Without a back panel the shelves hold the sides square only by their slots: add the back for a stiffer bookshelf." : "Without a back panel, glue the shelves in square.");
        Cabinets.AddRows(m, rows, i => shelfY[i] + T, z0, ShelfContents.Books);

        // The top, overhanging at the front and sides.
        double Wt = W + 2 * oh, Dt = D + oh;
        var topSlots = new PathsD();
        foreach (var xs in new[] { T / 2, W - T / 2 })
        {
            double u = xs + oh, v = Dt - D / 2;
            topSlots.Add(Geo.Rect(u - sw / 2, v - (sideTw + f) / 2, u + sw / 2, v + (sideTw + f) / 2));
        }
        m.Add("Top", [Geo.Rect(0, 0, Wt, Dt)], topSlots, [Pose.Up(-oh, H - T / 2, D + oh)], grainAlongU: true);
        m.Note($"{shelfY.Count} shelves, {gap:0.#} mm apart. Put the shelves{(back ? " and the back" : "")} into one side, add the other side, then press the top on.");
    }

    /// <summary>Book spines standing on a shelf: scored outlines and bands, the space over them etched dark.</summary>
    static void Books(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0;
        if (h < 2 || x1 - x0 < 2) return;
        var space = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        var books = new PathsD();
        double x = x0 + 0.3;
        while (x < x1 - 0.5)
        {
            if (rnd.NextDouble() < 0.08) { x += 0.6 + rnd.NextDouble() * 1.5; continue; } // a gap on the shelf
            double w = 0.7 + rnd.NextDouble() * 0.9, bh = h * (0.55 + rnd.NextDouble() * 0.33);
            if (x + w > x1 - 0.2) break;
            var b = Geo.Rect(x, y0, x + w, y0 + bh);
            books.Add(b);
            lines.Add(Geo.Loop(b));
            if (bh > 2.5 && w > 0.9)
            {
                lines.Add([new(x, y0 + bh * 0.82), new(x + w, y0 + bh * 0.82)]);
                lines.Add([new(x, y0 + bh * 0.18), new(x + w, y0 + bh * 0.18)]);
            }
            x += w;
        }
        etched.AddRange(Geo.Subtract(space, Geo.Offset(Geo.Union(books), 0.1)));
    }

    // ---- boxes ----

    /// <summary>One face of a box: its outline (flat), where it sits, and its size along u and v.</summary>
    public sealed record Panel(string Name, PathsD Cut, Pose Pose, double Lu, double Lv)
    {
        /// <summary>Grip ribs on the sides of its fingers (push fit), to add to the cut after it's checked.</summary>
        public PathsD Ribs { get; init; } = [];
    }

    /// <summary>
    /// The panels of a box <paramref name="size"/> (x, y, z) outside, with finger joints along every
    /// edge where two panels meet. Each edge's fingers alternate between its two panels; corners go to
    /// the front and back, then the ends, then the bottom and top. <paramref name="has"/> = left, right,
    /// bottom, top, back, front.
    /// </summary>
    public static List<Panel> Box(double[] size, double T, bool[] has, double rib = 0)
    {
        static int Rank(int axis) => axis == 2 ? 3 : axis == 0 ? 2 : 1;
        bool Has(int axis, int side) => has[axis * 2 + side];
        int Segments(double len)
        {
            double ft = Math.Max(1.5 * T, 2);
            int n = (int)Math.Round(len / ft);
            if (n % 2 == 0) n = Math.Max(1, n - 1);
            // At least three, so every edge interlocks, while the fingers stay wide enough not to break.
            if (n < 3 && len >= 3 * Maker.Bridge * 1.2) n = 3;
            return Math.Max(1, n);
        }
        string[] names = ["Left end", "Right end", "Bottom", "Lid", "Back", "Front"];
        var panels = new List<Panel>();
        for (int a = 0; a < 3; a++)
            for (int s = 0; s < 2; s++)
            {
                if (!Has(a, s)) continue;
                // The face's two axes (b along u, c along v) and how the flat coordinates map from the room's.
                int b, c;
                Pose pose;
                double W = size[0], H = size[1], D = size[2];
                switch (a)
                {
                    case 2:
                        b = 0; c = 1;
                        pose = s == 1 ? Pose.Front(0, 0, D - T / 2) : Pose.Back(W, 0, T / 2);
                        break;
                    case 0:
                        b = 2; c = 1;
                        pose = s == 0 ? Pose.Left(T / 2, 0, 0) : Pose.Right(W - T / 2, 0, D);
                        break;
                    default:
                        b = 0; c = 2;
                        pose = s == 0 ? Pose.Down(0, T / 2, 0) : Pose.Up(0, H - T / 2, D);
                        break;
                }
                var rects = new PathsD();
                var ribs = new PathsD();
                (double, double)[] Zones(double L) => [(0, T), (T, L - T), (L - T, L)];
                var zb = Zones(size[b]);
                var zc = Zones(size[c]);
                for (int ib = 0; ib < 3; ib++)
                    for (int ic = 0; ic < 3; ic++)
                    {
                        var (b0, b1) = zb[ib];
                        var (c0, c1) = zc[ic];
                        if (b1 - b0 <= 1e-9 || c1 - c0 <= 1e-9) continue;
                        bool endB = ib != 1, endC = ic != 1;
                        if (!endB && !endC) { rects.Add(Geo.Rect(b0, c0, b1, c1)); continue; }
                        if (endB && endC)
                        {
                            int owner = a;
                            if (Has(b, ib == 0 ? 0 : 1) && Rank(b) > Rank(owner)) owner = b;
                            if (Has(c, ic == 0 ? 0 : 1) && Rank(c) > Rank(owner)) owner = c;
                            if (owner == a) rects.Add(Geo.Rect(b0, c0, b1, c1));
                            continue;
                        }
                        // An edge: shared with the face across it, if there is one, in alternating fingers.
                        int other = endB ? b : c, otherSide = endB ? (ib == 0 ? 0 : 1) : (ic == 0 ? 0 : 1);
                        if (!Has(other, otherSide)) { rects.Add(Geo.Rect(b0, c0, b1, c1)); continue; }
                        bool even = Rank(a) > Rank(other);
                        double from = endB ? c0 : b0, to = endB ? c1 : b1;
                        int nseg = Segments(to - from);
                        for (int k = 0; k < nseg; k++)
                        {
                            if ((k % 2 == 0) != even) continue;
                            double s0 = from + (to - from) * k / nseg, s1 = from + (to - from) * (k + 1) / nseg;
                            rects.Add(endB ? Geo.Rect(b0, s0, b1, s1) : Geo.Rect(s0, c0, s1, c1));
                            if (rib > 0)
                            {
                                // A rib on each side of the finger, pressing on the fingers either side of it.
                                double bw = Math.Min(0.8, T * 0.45);
                                if (endB)
                                {
                                    double bm = (b0 + b1) / 2;
                                    ribs.Add(Maker.Tooth(new(bm, s1), new(1, 0), new(0, 1), bw, rib));
                                    ribs.Add(Maker.Tooth(new(bm, s0), new(1, 0), new(0, -1), bw, rib));
                                }
                                else
                                {
                                    double cm = (c0 + c1) / 2;
                                    ribs.Add(Maker.Tooth(new(s1, cm), new(0, 1), new(1, 0), bw, rib));
                                    ribs.Add(Maker.Tooth(new(s0, cm), new(0, 1), new(-1, 0), bw, rib));
                                }
                            }
                        }
                    }
                // Room coordinates → the panel's flat ones.
                double Coord(V3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
                V3 Room(double gb, double gc)
                {
                    double[] g = [0, 0, 0];
                    g[b] = gb; g[c] = gc; g[a] = Coord(pose.O, a);
                    return new V3(g[0], g[1], g[2]);
                }
                PointD Flat(PointD p) { var l = pose.Local(Room(p.x, p.y)); return new PointD(l.U, l.V); }
                var flat = Geo.Map(Geo.Union(rects), Flat);
                panels.Add(new Panel(names[a * 2 + s], Geo.Tidy(flat), pose, size[b], size[c]) { Ribs = Geo.Map(ribs, Flat) });
            }
        return panels;
    }

    /// <summary>Iron bands across a panel at <paramref name="at"/> along u, with rivets.</summary>
    static void Bands(double[] at, double Lv, double width, PathsD etched, List<PathD> lines, bool alongV = true, double Lu = 0)
    {
        foreach (var x in at)
        {
            if (alongV)
            {
                etched.Add(Geo.Rect(x - width / 2, -1, x + width / 2, Lv + 1));
                for (double y = 1.2; y < Lv - 0.8; y += 2.2) lines.Add(WoodFace.Dot(x, y, 0.18));
            }
            else
            {
                etched.Add(Geo.Rect(-1, x - width / 2, Lu + 1, x + width / 2));
                for (double u = 1.2; u < Lu - 0.8; u += 2.2) lines.Add(WoodFace.Dot(u, x, 0.18));
            }
        }
    }

    /// <summary>A chest: a box open at the top with planks, iron bands and a lock plate, and a lid that sits on it, located by a rim under it.</summary>
    public static void Chest(Maker m) => BoxItem(m, chest: true);

    /// <summary>A crate: a closed box (or open at the top) with a frame of boards and a brace etched on each face.</summary>
    public static void Crate(Maker m) => BoxItem(m, chest: false);

    static void BoxItem(Maker m, bool chest)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T;
        string what = chest ? "chest" : "crate";
        double min = 3 * T + 2;
        if (W < min || D < min || H < min) { m.Bad($"The {what} is too small for a {T:0.##} mm sheet: make each side at least {min:0.#} mm."); return; }
        bool closed = it.Lid == LidStyle.Flat;
        var panels = Box([W, H, D], T, [true, true, true, closed, true, true], m.PushFit ? m.Grip / 2 : 0);
        double band = Math.Clamp(Math.Min(W, D) * 0.07, 0.6, 1.2);
        foreach (var p in panels)
        {
            var etched = new PathsD();
            var lines = new List<PathD>();
            bool side = p.Name is not ("Bottom" or "Lid");
            if (chest && it.Bands && p.Name != "Bottom")
            {
                double inset = T + band;
                if (p.Lu > 2 * inset + 2) Bands([inset, p.Lu - inset], p.Lv, band, etched, lines);
                if (p.Name == "Front") lines.AddRange(Lock(p.Lu / 2, p.Lv - Math.Min(p.Lv * 0.3, 3.5), Math.Min(p.Lu * 0.16, 2.6)));
            }
            if (!chest && it.Bands && p.Name != "Bottom") Braced(p.Lu, p.Lv, T, etched);
            bool planks = side || p.Name == "Lid";
            m.Add(p.Name, p.Cut, [], [p.Pose], grainAlongU: true, face: [Geo.Rect(0, 0, p.Lu, p.Lv)], planks: planks && (chest || !it.Bands), plankMm: chest ? 2.4 : 3,
                etched: etched, lines: lines, ribs: p.Ribs);
        }
        if (chest && it.Lid is LidStyle.Loose or LidStyle.Domed)
        {
            double lo = Math.Clamp(Math.Min(W, D) * 0.04, 0.4, 1), cl = Math.Max(0.15, m.Fit);
            double Wl = W + 2 * lo, Dl = D + 2 * lo;
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (it.Bands && Wl > 2 * (T + band + lo) + 2) Bands([T + band + lo, Wl - T - band - lo], Dl, band, etched, lines);
            double Wr = W - 2 * T - 2 * cl, Dr = D - 2 * T - 2 * cl;
            // Push fit: two upright strips just inside the front and back, their tabs up through the lid.
            double hs = Math.Min(Math.Clamp(H * 0.25, 1.5, 3), H - 2 * T - 0.5), tw = Math.Clamp(Wr * 0.2, 1.5, 4);
            bool strips = m.PushFit && Wr > 2 * tw + 3 && Dr > 2 * T + 1 && hs >= 1.2;
            var slots = new PathsD();
            double[] zs = [T + cl + T / 2, D - T - cl - T / 2], xs = [T + cl + Wr * 0.25, T + cl + Wr * 0.75];
            if (strips)
                foreach (var z in zs)
                    foreach (var x in xs)
                        slots.Add(Geo.Rect(x + lo - tw / 2 - m.Fit / 2, D + lo - z - m.SlotW / 2, x + lo + tw / 2 + m.Fit / 2, D + lo - z + m.SlotW / 2));
            var dome = it.Lid == LidStyle.Domed ? Dome(m, W, D, H, lo, band) : null;
            if (dome is not null) slots.AddRange(dome);
            m.Add("Lid", [Geo.Rect(0, 0, Wl, Dl)], slots, [Pose.Up(-lo, H + T / 2, D + lo)], grainAlongU: true, planks: true, plankMm: 2.4, etched: etched, lines: lines);
            if (strips)
            {
                var strip = new PathsD { Geo.Rect(0, H - hs, Wr, H) };
                foreach (var x in xs) strip.Add(Geo.Rect(x - T - cl - tw / 2, H - 0.01, x - T - cl + tw / 2, H + T));
                m.Add("Lid rail", strip, [], [.. zs.Select(z => Pose.Front(T + cl, 0, z))], grainAlongU: true);
                m.Note("Push the two lid rails' tabs up into the slots under the lid: they keep the lid in place on the chest.");
            }
            else if (Wr > 2 && Dr > 2)
            {
                m.Add("Lid rim", [Geo.Rect(0, 0, Wr, Dr)], [], [Pose.Up(T + cl, H - T / 2, D - T - cl)], grainAlongU: true);
                m.Note("Glue the rim under the middle of the lid: it keeps the lid in place on the chest.");
            }
        }
        m.Note(closed ? $"Fit the sides together at the corners, the bottom in, and the lid on last: every piece goes in straight, its fingers between the others'." : $"Push the {what}'s sides together at the corners, then the bottom in.");
    }

    /// <summary>
    /// A domed lid on the chest's lid board: two arched ends over the chest's ends and a rail along the front and
    /// the back, their tabs down into the board, and planks laid over them round the curve, banded with iron.
    /// Returns the slots for the board (flat, as on the lid board); null when the chest is too small for a dome.
    /// </summary>
    static PathsD? Dome(Maker m, double W, double D, double H, double lo, double band)
    {
        var it = m.Item;
        double T = m.T, f = m.Fit, sw = m.SlotW;
        double Wl = W + 2 * lo, S = D + 2 * lo, y0 = H + T;
        // The dome stands in from the board's front and back edges (sheet in front of the slots) and its ends in from its sides.
        double inset = Maker.Bridge + 0.1, Sd = S - 2 * inset;
        double hb = Math.Max(1.4, Maker.Bridge + 0.4), rise = Geo.Clamp(Sd * 0.24, 1.5, 5);
        // The planks are glued on, so push fit keeps the flat lid.
        if (m.PushFit || Sd < 2 * T + 2.5 || W < 2 * T + 6) return null;
        // The curve (s from the board's back edge, y up from the board), cut into planks.
        double rho = (Sd * Sd / 4 + rise * rise) / (2 * rise), cy = rise - rho, half = Math.Asin(Geo.Clamp(Sd / 2 / rho, -1, 1));
        int n = Geo.Clamp((int)Math.Round(2 * half / (24 * Math.PI / 180)), 3, 7);
        // Its corners, from the front over to the back, so each plank's outer face points outward.
        var at = new List<PointD>();
        for (int i = 0; i <= n; i++)
        {
            double a = half - 2 * half * i / n;
            at.Add(new PointD(inset + Sd / 2 + rho * Math.Sin(a), hb + cy + rho * Math.Cos(a)));
        }
        // The ends: between the edge rails, up to the planks' inner faces.
        double s0 = inset, s1 = inset + Sd;
        var under = new PathD { new(s1, 0), new(s1, hb) };
        under.AddRange(at.Skip(1).SkipLast(1));
        under.AddRange([new(s0, hb), new(s0, 0)]);
        var end = Geo.Intersect([under], [Geo.Rect(s0 + T, 0, s1 - T, hb + rise + 1)]);
        double etw = Geo.Clamp((Sd - 2 * T) * 0.2, 1.5, 3);
        // Two tabs when there is sheet enough between them, else one in the middle.
        double span = Sd - 2 * T;
        double[] endTabs = span * 0.4 >= etw + f + 1.2 ? [s0 + T + span * 0.3, s1 - T - span * 0.3] : [s0 + T + span / 2];
        var endCut = Geo.Solid([.. end, .. endTabs.Select(u => Geo.Rect(u - etw / 2, -T, u + etw / 2, 0.01))]);
        double ex = Math.Max(T / 2, inset + sw / 2 - lo);
        double[] endX = [ex, W - ex];
        // The edge rails along the front and back, the full length of the lid.
        double rtw = Geo.Clamp(Wl * 0.12, 1.5, 3.5);
        double[] railTabs = [Wl * 0.3, Wl * 0.7];
        var rail = new PathsD { Geo.Rect(0, 0, Wl, hb) };
        foreach (var u in railTabs) rail.Add(Geo.Rect(u - rtw / 2, -T, u + rtw / 2, 0.01));
        // The board's slots (u = x + lo, v = S - s).
        var slots = new PathsD();
        foreach (var x in endX)
            foreach (var u in endTabs) slots.Add(Geo.Rect(x + lo - sw / 2, S - u - (etw + f) / 2, x + lo + sw / 2, S - u + (etw + f) / 2));
        foreach (var v in new[] { S - s1 + T / 2, S - s0 - T / 2 })
            foreach (var u in railTabs) slots.Add(Geo.Rect(u - (rtw + f) / 2, v - sw / 2, u + (rtw + f) / 2, v + sw / 2));
        double[] bandAt = it.Bands && Wl > 2 * (T + band + lo) + 2 ? [T + band + lo, Wl - T - band - lo] : [];
        var re = new PathsD();
        var rl = new List<PathD>();
        if (bandAt.Length > 0) Bands(bandAt, hb, band, re, rl);
        m.Add("Lid end", endCut, [], [.. endX.Select(x => Pose.Left(x, y0, -lo))], grainAlongU: true, planks: true, plankMm: 2.4);
        m.Add("Lid edge", rail, [], [Pose.Front(-lo, y0, -lo + s1 - T / 2), Pose.Back(W + lo, y0, -lo + s0 + T / 2)], grainAlongU: true, face: [Geo.Rect(0, 0, Wl, hb)], etched: re, lines: rl);
        // The planks, their inner faces on the chords of the curve.
        for (int i = 0; i < n; i++)
        {
            var a = at[i];
            var b = at[i + 1];
            double c = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            var v = new V3(0, (b.y - a.y) / c, (b.x - a.x) / c);
            var nrm = V3.UnitX.Cross(v);
            var o = new V3(-lo, y0 + a.y, -lo + a.x) + nrm * (T / 2);
            var pe = new PathsD();
            var pl = new List<PathD>();
            if (bandAt.Length > 0) Bands(bandAt, c, band, pe, pl);
            m.Add($"Lid plank {i + 1}", [Geo.Rect(0, 0, Wl, c)], [], [new Pose(o, V3.UnitX, v)], grainAlongU: true, face: [Geo.Rect(0, 0, Wl, c)], etched: pe, lines: pl);
        }
        m.Note("Domed lid: stand the two ends and the two edge rails in the lid board's slots, then glue the planks over them round the curve, front to back.");
        return slots;
    }

    /// <summary>A lock plate with a keyhole, scored.</summary>
    static IEnumerable<PathD> Lock(double cx, double cy, double w)
    {
        double h = w * 1.25;
        yield return Geo.Loop(WoodFace.RoundRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, w * 0.15));
        yield return WoodFace.Dot(cx, cy + h * 0.1, w * 0.12);
        yield return [new(cx, cy - h * 0.02), new(cx, cy - h * 0.3)];
    }

    /// <summary>A crate's face: boards round the edge and a brace corner to corner, as etched grooves.</summary>
    static void Braced(double Lu, double Lv, double T, PathsD etched)
    {
        double fb = Math.Clamp(Math.Min(Lu, Lv) * 0.16, 1.2, 3), g = WoodFace.GrooveMm;
        double x0 = T * 0.5 + fb, y0 = T * 0.5 + fb, x1 = Lu - T * 0.5 - fb, y1 = Lv - T * 0.5 - fb;
        if (x1 - x0 < 2 || y1 - y0 < 2) return;
        var inner = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        etched.AddRange(Geo.Subtract(Geo.Offset(inner, g / 2), Geo.Offset(inner, -g / 2)));
        // The brace: two grooves, a board's width apart, from corner to corner.
        double ang = Math.Atan2(y1 - y0, x1 - x0);
        foreach (var off in new[] { -fb / 2, fb / 2 })
        {
            double nx = -Math.Sin(ang) * off, ny = Math.Cos(ang) * off;
            var strip = Geo.Strip(new PointD(x0 + nx, y0 + ny), new PointD(x1 + nx, y1 + ny), g);
            etched.AddRange(Geo.Intersect([strip], inner));
        }
    }
}
