using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>Which piece of a town wall: a straight run, a corner, a gatehouse or a finished end.</summary>
public enum WallPiece { Straight, OutsideCorner, InsideCorner, Gate, End }

/// <summary>
/// Town walls and towers, built the way real ones are: an outer face that rises into a crenellated
/// parapet, a lower inner face, and a wall walk (one piece, an L round a corner) whose tabs run through
/// the outer face, hidden under a moulded string course with corbels glued on over them. A plinth runs
/// along the foot, arrow loops are cut through the merlons, and the stone, logs, sandstone or armour
/// plate is etched to suit the theme. Pieces line up on a 25.4 mm (1 inch) grid at 28 mm figures: a
/// wall's centre line runs along a grid line with its outside towards −Z, and the parapet's merlons
/// meet halfway across a join, so runs of them read as one wall.
/// </summary>
public static partial class TownWalls
{
    /// <summary>One map square at 28 mm figures (mm).</summary>
    public const double Square = 25.4;
    /// <summary>The deck's overhang past the inner face (mm), so its slots there keep a solid edge.</summary>
    const double Lip = 1.2;

    // ---- the theme's look ----

    enum Top { Merlons, Broken, Logs, Armour, Stepped, Gabled }
    enum Course { Corbels, Arches, Beams, Rail, Band }
    enum Loop { Cross, Slit, Pointed, Port, Keyhole, None }

    sealed record Look(Top Top, Course Course, Loop Loop, string Face, double A, double B, double G, string Deck, double DA, double DB, double DG, bool Wood);

    static Look LookOf(string theme) => theme switch
    {
        TerrainThemes.Dungeon => new(Top.Broken, Course.Corbels, Loop.Slit, ThemeTextures.DungeonBlocks, 11, 6, 0.8, "Tiles", 7, 7, 0.5, false),
        TerrainThemes.Cave => new(Top.Logs, Course.Rail, Loop.None, "Wood planks (upright)", 3.2, 40, 0.45, "Wood planks (across)", 3, 30, 0.4, true),
        TerrainThemes.SciFi => new(Top.Armour, Course.Band, Loop.Port, ThemeTextures.SciFiPanels, 14, 11, 0.5, "Metal plates", 10, 8, 0.4, false),
        TerrainThemes.Desert => new(Top.Stepped, Course.Beams, Loop.Keyhole, ThemeTextures.Sandstone, 13, 6, 0.6, "Tiles", 7, 7, 0.5, false),
        TerrainThemes.Gothic => new(Top.Gabled, Course.Arches, Loop.Pointed, "Stone blocks", 9, 5, 0.7, "Tiles", 6, 6, 0.5, false),
        _ => new(Top.Merlons, Course.Corbels, Loop.Cross, "Stone blocks", 10, 5.5, 0.7, "Tiles", 7, 7, 0.5, false),
    };

    // ---- pieces under construction ----

    /// <summary>An upright piece: flat (u, v) sits at O + U·u + Y·v, its etched face towards U × Y.</summary>
    sealed class Up
    {
        public string Name = "";
        public V3 O, U;
        /// <summary>Up the piece: straight up unless it's a sloping roof or awning.</summary>
        public V3 V = V3.UnitY;
        public double Len;
        public PathsD Outline = [], Holes = [], Etched = [];
        public List<PathD> Lines = [];
        public bool Glued, Plain;
        public V3 N => U.Cross(V);
        public V3 At(double u, double v) => O + U * u + V * v;
    }

    /// <summary>A flat piece lying face up at height Y (its top), drawn in the room's (x, z).</summary>
    sealed class Flat
    {
        public string Name = "";
        public double Y;
        public PathsD Outline = [], Holes = [], Etched = [];
        public List<PathD> Lines = [];
        public string Texture = "Tiles";
        public double A = 7, B = 7, G = 0.5;
        public bool Glued;
    }

    /// <summary>Everything one wall or tower is made of, joined up, then added to the maker.</summary>
    sealed class Build(Maker m, Look look, double k)
    {
        public readonly Maker M = m;
        public readonly Look L = look;
        /// <summary>Sizes of the detail relative to 28 mm figures.</summary>
        public readonly double K = k;
        public readonly List<Up> Ups = [];
        public readonly List<Flat> Flats = [];
        public double T => M.T;
        double F => M.Fit;
        public bool Glue => !M.PushFit;

        /// <summary>
        /// An upright along the line from <paramref name="a"/> (room x, z at its middle plane) for
        /// <paramref name="len"/> mm in direction <paramref name="dir"/>, its face towards <paramref name="facing"/>.
        /// </summary>
        public Up Upright(string name, V3 a, V3 dir, double len, V3 facing)
        {
            bool fwd = dir.Cross(V3.UnitY).Dot(facing) > 0;
            var up = new Up { Name = name, U = fwd ? dir : -dir, O = fwd ? a : a + dir * len, Len = len };
            Ups.Add(up);
            return up;
        }

        /// <summary>u along an upright for a point at distance s from the start of the line it was made on.</summary>
        public static double UOf(Up up, V3 a, V3 dir, double s) => (a + dir * s - up.O).Dot(up.U);

        // ---- joints ----

        /// <summary>A tab on the flat piece through the upright (u0–u1): the upright gets the slot.</summary>
        public void FlatInto(Flat f, Up up, double u0, double u1)
        {
            var n = up.N * (T / 2 + 0.02);
            V3 a = up.At(u0, 0), b = up.At(u1, 0);
            f.Outline.Add(XZ(a - n, b - n, b + n, a + n));
            up.Holes.Add(Geo.Rect(u0 - F / 2, f.Y - T - F / 2, u1 + F / 2, f.Y + F / 2));
        }

        /// <summary>A tab on the upright's top edge (u0–u1) up into the flat piece over it.</summary>
        public void UpInto(Flat f, Up up, double u0, double u1)
        {
            up.Outline.Add(Geo.Rect(u0, f.Y - T - 0.02, u1, f.Y));
            SlotFor(f, up, u0, u1);
        }

        /// <summary>A tab on the upright's bottom edge (u0–u1) down into the flat piece under it.</summary>
        public void DownInto(Flat f, Up up, double u0, double u1)
        {
            up.Outline.Add(Geo.Rect(u0, f.Y - T, u1, f.Y + 0.02));
            SlotFor(f, up, u0, u1);
        }

        void SlotFor(Flat f, Up up, double u0, double u1)
        {
            var n = up.N * ((T + F) / 2);
            V3 a = up.At(u0 - F / 2, 0), b = up.At(u1 + F / 2, 0);
            f.Holes.Add(XZ(a - n, b - n, b + n, a + n));
        }

        /// <summary>A rectangle in the room's (x, z) as a flat piece's (u, v): v = −z.</summary>
        public static PathD XZ(params V3[] p) => [.. p.Select(q => new PointD(q.X, -q.Z))];
        public static PathD XZRect(double x0, double z0, double x1, double z1) => Geo.Rect(x0, -z1, x1, -z0);

        /// <summary>Tabs spread along u0–u1: about one every 14 mm, kept off the ends.</summary>
        public List<(double, double)> Tabs(double u0, double u1, double edge = 2.5)
        {
            double len = u1 - u0 - 2 * edge, tw = Geo.Clamp(len * 0.3, 2.4, 4.5 * K);
            if (len < tw) return len > 2 ? [((u0 + u1) / 2 - 1, (u0 + u1) / 2 + 1)] : [];
            int n = Math.Max(1, (int)Math.Round(len / (14 * K)));
            var list = new List<(double, double)>();
            for (int i = 0; i < n; i++)
            {
                double c = u0 + edge + len * (i + 0.5) / n;
                list.Add((c - tw / 2, c + tw / 2));
            }
            return list;
        }

        // ---- adding it all ----

        public void Finish()
        {
            foreach (var f in Flats)
            {
                if (f.Glued && !Glue) continue;
                var outline = Geo.Solid(f.Outline);
                var b = Geo.Bounds(outline);
                var etched = new PathsD(f.Etched);
                var lines = new List<PathD>(f.Lines);
                Texture(f.Texture, b.left, b.top, b.right - b.left, b.bottom - b.top, f.A, f.B, f.G, etched, lines);
                var part = M.Add(f.Name, outline, Geo.Solid(f.Holes), [Pose.Up(0, f.Y - T / 2, 0)], grainAlongU: true, face: [], etched: etched, lines: lines);
                if (f.Glued) part.Glue();
            }
            foreach (var up in Ups)
            {
                if (up.Glued && !Glue) continue;
                var outline = Geo.Solid(up.Outline);
                var b = Geo.Bounds(outline);
                var etched = new PathsD(up.Etched);
                var lines = new List<PathD>(up.Lines);
                if (!up.Plain) Texture(L.Face, b.left, b.top, b.right - b.left, b.bottom - b.top, L.A, L.B, L.G, etched, lines);
                var part = M.Add(up.Name, outline, Geo.Solid(up.Holes), [new Pose(up.O, up.U, up.V)], grainAlongU: false, face: [], etched: etched, lines: lines);
                if (up.Glued) part.Glue();
            }
        }

        /// <summary>A texture over the box (x, y, w, h), sized to the figures.</summary>
        public void Texture(string name, double x, double y, double w, double h, double a, double b, double g, PathsD etched, List<PathD> lines)
        {
            if (w <= 0 || h <= 0) return;
            var (raised, more, ln) = TextureEngine.Make(name, w + 2, h + 2, 0, w + 2, h + 2, a * K, b * K, g * Math.Sqrt(K), M.Rnd.Next());
            var face = new PathsD { Geo.Rect(0, 0, w + 2, h + 2) };
            var gaps = Geo.Union(Geo.Subtract(face, raised), Geo.Union(more));
            etched.AddRange(Geo.Move(gaps, x - 1, y - 1));
            lines.AddRange(Geo.Map(ln, q => new PointD(q.x + x - 1, q.y + y - 1)));
        }
    }

    // ---- the walls ----

    /// <summary>The town wall piece the item describes.</summary>
    public static void Make(Maker m)
    {
        var it = m.Item;
        double T = m.T, H = it.HeightMm, d = it.DepthMm;
        double k = H / 56;
        if (d < 3 * T + Lip + 6) { m.Bad($"The wall is too thin for a wall walk: make it at least {3 * T + Lip + 6:0.#} mm thick."); return; }
        if (H < 24) { m.Bad("The wall is too low: make it at least 24 mm high."); return; }
        var b = new Build(m, LookOf(m.Theme), k);
        switch (it.Wall)
        {
            case WallPiece.Straight:
                if (it.WidthMm < 10) { m.Bad("The wall is too short: make it at least 10 mm long."); return; }
                Straight(b, it.WidthMm, d, H);
                break;
            case WallPiece.OutsideCorner:
            case WallPiece.InsideCorner:
                if (it.WidthMm < d + 6) { m.Bad($"The corner's arms are too short: make them at least {d + 6:0.#} mm."); return; }
                Corner(b, it.WidthMm, d, H, it.Wall == WallPiece.InsideCorner);
                break;
            case WallPiece.Gate:
                Gate(b, it.WidthMm, d, H);
                break;
            case WallPiece.End:
                End(b, it.WidthMm, d, H);
                break;
        }
        if (m.Findings.Any(f => f.Level == Severity.Bad)) return;
        b.Finish();
        m.Note(m.PushFit
            ? "Push the wall walk's tabs through the outer face, then push the inner face and the end pieces up into the walk."
            : "Put the wall walk's tabs through the outer face, push the inner face and the end pieces up into the walk, then glue the string course over the tab ends and the plinth along the foot.");
    }

    /// <summary>The wall walk's height and the parapet's for a wall <paramref name="H"/> high.</summary>
    static (double Deck, double Crenel) Heights(double H) => (Math.Round(H * 0.66, 1), Math.Round(H * 0.82, 1));

    /// <summary>
    /// One straight run: from <paramref name="a"/> (the outside of the string course, at its start) along
    /// <paramref name="dir"/>, the wall inwards along <paramref name="inward"/>. Extents along the run for each layer.
    /// </summary>
    sealed record Run(V3 A, V3 Dir, V3 In, double Thick, double S0, double S1)
    {
        public double Outer0 { get; init; } = double.NaN;
        public double Outer1 { get; init; } = double.NaN;
        public double Inner0 { get; init; } = double.NaN;
        public double Inner1 { get; init; } = double.NaN;
        public double Strip0 { get; init; } = double.NaN;
        public double Strip1 { get; init; } = double.NaN;
        /// <summary>Merlon width at each end: a full merlon at a corner, half of one where the next piece goes on.</summary>
        public double MerlonStart { get; init; } = -1;
        public double MerlonEnd { get; init; } = -1;
        public bool EndPieceStart { get; init; }
        public bool EndPieceEnd { get; init; }
        public V3 P(double s, double q) => A + Dir * s + In * q;
    }

    /// <summary>The outer face with its parapet, the inner face, the end pieces and the glued courses for one run; the walk is the caller's.</summary>
    static void AddRun(Build b, Run r, Flat walk, double H, string label = "")
    {
        double T = b.T, d = r.Thick, k = b.K;
        var (deckY, crenel) = Heights(H);
        double o0 = double.IsNaN(r.Outer0) ? r.S0 : r.Outer0, o1 = double.IsNaN(r.Outer1) ? r.S1 : r.Outer1;
        double i0 = double.IsNaN(r.Inner0) ? r.S0 : r.Inner0, i1 = double.IsNaN(r.Inner1) ? r.S1 : r.Inner1;
        double g0 = double.IsNaN(r.Strip0) ? r.S0 : r.Strip0, g1 = double.IsNaN(r.Strip1) ? r.S1 : r.Strip1;
        var outward = -r.In;
        // The outer face: up to the merlons' tops.
        var outer = b.Upright("Outer face" + label, r.P(o0, 1.5 * T), r.Dir, o1 - o0, outward);
        double ua = Build.UOf(outer, r.P(o0, 1.5 * T), r.Dir, 0), ub = Build.UOf(outer, r.P(o0, 1.5 * T), r.Dir, o1 - o0);
        double u0 = Math.Min(ua, ub), u1 = Math.Max(ua, ub);
        bool flip = ua > ub;
        double ms = r.MerlonStart < 0 ? -1 : r.MerlonStart, me = r.MerlonEnd < 0 ? -1 : r.MerlonEnd;
        if (flip) (ms, me) = (me, ms);
        outer.Outline.Add(Geo.Rect(u0, 0, u1, crenel));
        Parapet(b, outer, u0, u1, crenel, H, ms, me);
        // The walk's tabs through it.
        foreach (var (t0, t1) in b.Tabs(u0, u1, 3)) b.FlatInto(walk, outer, t0, t1);
        // A blind loop or two low down on longer runs, and a drain under the walk.
        if (b.L.Loop != Loop.None && u1 - u0 > 20 * k)
            for (double c = u0 + (u1 - u0) / 4; c < u1; c += (u1 - u0) / 2)
                LowLoop(b, outer, c, deckY * 0.35, deckY * 0.62);

        // The inner face: up to the walk, its tabs up into it.
        var inner = b.Upright("Inner face" + label, r.P(i0, d - Lip - T / 2), r.Dir, i1 - i0, r.In);
        double ia = Build.UOf(inner, r.P(i0, d - Lip - T / 2), r.Dir, 0), ib = Build.UOf(inner, r.P(i0, d - Lip - T / 2), r.Dir, i1 - i0);
        double v0 = Math.Min(ia, ib), v1 = Math.Max(ia, ib);
        inner.Outline.Add(Geo.Rect(v0, 0, v1, deckY - T));
        foreach (var (t0, t1) in b.Tabs(v0, v1)) b.UpInto(walk, inner, t0, t1);
        // Steps up the inside of a longer run.
        if (i1 - i0 > 30 * k && !b.L.Wood) Steps(inner, v0 + (v1 - v0) * 0.55, deckY - T, k);

        // End pieces under the walk, where the run is open.
        double eIn = 2 * T, eOut = d - Lip - T;
        foreach (var (s, here) in new[] { (r.S0 + 1.3 + T / 2, r.EndPieceStart), (r.S1 - 1.3 - T / 2, r.EndPieceEnd) })
        {
            if (!here) continue;
            var e = b.Upright("End" + label, r.P(s, eIn), r.In, eOut - eIn, r.Dir);
            e.Outline.Add(Geo.Rect(0, 0, eOut - eIn, deckY - T));
            e.Plain = true;
            foreach (var (t0, t1) in b.Tabs(0, eOut - eIn, 1.5)) b.UpInto(walk, e, t0, t1);
        }

        // The glued string course over the tab ends, with its corbels, and the plinth.
        if (!b.Glue) return;
        var course = b.Upright("String course" + label, r.P(g0, T / 2), r.Dir, g1 - g0, outward);
        course.Glued = course.Plain = true;
        double ca = Build.UOf(course, r.P(g0, T / 2), r.Dir, 0), cb = Build.UOf(course, r.P(g0, T / 2), r.Dir, g1 - g0);
        StringCourse(b, course, Math.Min(ca, cb), Math.Max(ca, cb), deckY);
        var plinth = b.Upright("Plinth" + label, r.P(g0, T / 2), r.Dir, g1 - g0, outward);
        plinth.Glued = plinth.Plain = true;
        Plinth(b, plinth, Math.Min(ca, cb), Math.Max(ca, cb));
        // A carved shield hung on each square of stone wall, between the loops.
        if (b.L.Top is Top.Merlons or Top.Gabled or Top.Broken && g1 - g0 >= 18 * k)
        {
            int n = Math.Max(1, (int)Math.Round((g1 - g0) / Square));
            for (int i = 0; i < n; i++)
            {
                double c = g0 + (g1 - g0) * (i + 0.5) / n, sw = 5.6 * k, sh = 6.8 * k, y0 = deckY * 0.42;
                var badge = b.Upright("Shield" + label + (n > 1 ? $" {i + 1}" : ""), r.P(c - sw / 2, T / 2), r.Dir, sw, outward);
                badge.Glued = badge.Plain = true;
                var shield = Shield(0, y0, sw, y0 + sh);
                badge.Outline.Add(shield);
                var be = new PathsD();
                var bl = new List<PathD>();
                Cabinets.Symbol(Fronts.Look(b.M), b.M.Theme, sw / 2, y0 + sh * 0.56, Math.Min(sw, sh) * 0.3, be, bl);
                badge.Etched.AddRange(be);
                badge.Lines.AddRange(bl);
                badge.Lines.Add(Geo.Loop(Geo.Offset([shield], -0.45).FirstOrDefault() ?? shield));
            }
        }
    }

    /// <summary>The merlons (or logs, armour, steps) along the top of a face, from the crenels to <paramref name="top"/>.</summary>
    static void Parapet(Build b, Up face, double u0, double u1, double crenel, double top, double startW, double endW)
    {
        double k = b.K, period = 8.6 * k, mw = Math.Max(2.6, period * 0.55);
        var rnd = b.M.Rnd;
        if (b.L.Top == Top.Logs)
        {
            // A palisade: every log sharpened, a little taller or shorter than its neighbours.
            double lw = 3.2 * k;
            int n = Math.Max(1, (int)Math.Round((u1 - u0) / lw));
            lw = (u1 - u0) / n;
            for (int i = 0; i < n; i++)
            {
                double a = u0 + i * lw, c = a + lw / 2, tip = top - rnd.NextDouble() * 1.4 * k, sh = tip - 2.2 * k, half = Math.Max(0.6, lw * 0.18);
                face.Outline.Add(new PathD { new(a, crenel - 0.01), new(a + lw, crenel - 0.01), new(a + lw, sh), new(c + half, tip), new(c - half, tip), new(a, sh) });
                face.Lines.Add([new(a, crenel + 0.4), new(a, sh - 0.2)]);
            }
            return;
        }
        if (startW < 0) startW = mw / 2;
        if (endW < 0) endW = mw / 2;
        // The merlons: one at each end, and as many between as fit with a crenel between each.
        var spans = new List<(double, double)> { (u0, u0 + startW), (u1 - endW, u1) };
        double from = u0 + startW, to = u1 - endW, gap = period - mw;
        if (to - from > 2 * gap + mw * 0.6)
        {
            int j = Math.Max(0, (int)Math.Round((to - from - gap) / (gap + mw)));
            double g = (to - from - j * mw) / (j + 1);
            while (j > 0 && g < 1.6) { j--; g = (to - from - j * mw) / (j + 1); }
            for (int i = 0; i < j; i++)
            {
                double a = from + g * (i + 1) + mw * i;
                spans.Add((a, a + mw));
            }
        }
        else if (to - from < 1.6)
            spans.Add((from - 0.01, to + 0.01));
        foreach (var (a, c) in spans)
        {
            if (c - a < 0.3) continue;
            bool full = c - a > mw * 0.8;
            face.Outline.Add(Merlon(b.L.Top, a, c, crenel, top, k, rnd, full, a <= u0 + 1e-6, c >= u1 - 1e-6));
            if (full && c - a > 3.4) TopLoop(b, face, (a + c) / 2, crenel, top);
        }
    }

    /// <summary>One merlon's shape (open at the ends of a face that's cut there).</summary>
    static PathD Merlon(Top top, double a, double c, double y0, double y1, double k, Random rnd, bool full, bool atStart, bool atEnd)
    {
        double w = c - a, cx = (a + c) / 2;
        switch (top)
        {
            case Top.Stepped when w >= 2.6:
            {
                // Three steps, narrowing (the half at a join is the half of one).
                double h = (y1 - y0) / 3, s = Math.Max(0.6, w * (full ? 0.18 : 0.36));
                double l0 = atStart && !full ? a : a, r0 = atEnd && !full ? c : c;
                double l1 = atStart && !full ? a : a + s, r1 = atEnd && !full ? c : c - s;
                double l2 = atStart && !full ? a : a + 2 * s, r2 = atEnd && !full ? c : c - 2 * s;
                if (r2 - l2 < 1.2) { l2 = cx - 0.6; r2 = cx + 0.6; }
                return [new(l0, y0 - 0.01), new(r0, y0 - 0.01), new(r0, y0 + h), new(r1, y0 + h), new(r1, y0 + 2 * h), new(r2, y0 + 2 * h), new(r2, y1), new(l2, y1), new(l2, y0 + 2 * h), new(l1, y0 + 2 * h), new(l1, y0 + h), new(l0, y0 + h)];
            }
            case Top.Gabled when w >= 2.6:
            {
                double rise = Math.Min(w * 0.45, (y1 - y0) * 0.4), flat = 0.6;
                if (!full && atStart) return [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1 - rise), new(a + flat, y1), new(a, y1)];
                if (!full && atEnd) return [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1), new(c - flat, y1), new(a, y1 - rise)];
                return [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1 - rise), new(cx + flat, y1), new(cx - flat, y1), new(a, y1 - rise)];
            }
            case Top.Armour when w >= 2.6:
            {
                // Armour blocks with their top corners cut off.
                double ch = Math.Min(1.4 * k, w * 0.3);
                double lt = atStart && !full ? a : a + ch, rt = atEnd && !full ? c : c - ch;
                return [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1 - (rt < c ? ch : 0)), new(rt, y1), new(lt, y1), new(a, y1 - (lt > a ? ch : 0))];
            }
            case Top.Broken when full && rnd.NextDouble() < 0.4:
            {
                // A corner knocked off.
                double cut = Math.Min(w * 0.35, (y1 - y0) * 0.4);
                return rnd.NextDouble() < 0.5
                    ? [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1), new(a + cut, y1), new(a, y1 - cut)]
                    : [new(a, y0 - 0.01), new(c, y0 - 0.01), new(c, y1 - cut), new(c - cut, y1), new(a, y1)];
            }
            default:
                return Geo.Rect(a, y0 - 0.01, c, y1);
        }
    }

    /// <summary>An arrow loop cut through a merlon.</summary>
    static void TopLoop(Build b, Up face, double cx, double y0, double y1)
    {
        double k = b.K, h = (y1 - y0), lo = y0 + h * 0.18, hi = y1 - h * 0.3;
        switch (b.L.Loop)
        {
            case Loop.Cross:
                face.Holes.Add(Geo.Rect(cx - 0.45, lo, cx + 0.45, hi));
                face.Holes.Add(Geo.Rect(cx - 1.1, lo + (hi - lo) * 0.55, cx + 1.1, lo + (hi - lo) * 0.55 + 0.8));
                break;
            case Loop.Slit:
                face.Holes.Add(Geo.Rect(cx - 0.45, lo, cx + 0.45, hi));
                break;
            case Loop.Pointed:
                face.Holes.Add(Fronts.PointedRect(cx - 0.55, lo, cx + 0.55, hi));
                break;
            case Loop.Port:
                face.Holes.Add(Fronts.Chamfered(cx - 1.0 * k, lo + 0.4, cx + 1.0 * k, lo + 0.4 + 1.1 * k, 0.3));
                break;
            case Loop.Keyhole:
                face.Holes.Add(Geo.Rect(cx - 0.45, lo + 1.0, cx + 0.45, Math.Min(hi, y0 + h * 0.55)));
                face.Holes.Add(Geo.Circle(cx, lo + 0.8, 0.65, 0.01));
                break;
        }
    }

    /// <summary>A tall arrow loop low in a wall or tower, its splayed surround etched.</summary>
    static void LowLoop(Build b, Up face, double cx, double y0, double y1)
    {
        double k = b.K;
        switch (b.L.Loop)
        {
            case Loop.Port:
                face.Holes.Add(Fronts.Chamfered(cx - 1.4 * k, y1 - 2 * k, cx + 1.4 * k, y1 - 0.6 * k, 0.35));
                face.Etched.AddRange(Fronts.Outline([Fronts.Chamfered(cx - 2.2 * k, y1 - 2.8 * k, cx + 2.2 * k, y1 + 0.2 * k, 0.6)], 0.4));
                break;
            case Loop.Pointed:
                face.Holes.Add(Fronts.PointedRect(cx - 0.55, y0, cx + 0.55, y1));
                face.Etched.AddRange(Fronts.Outline([Fronts.PointedRect(cx - 1.6 * k, y0 - 0.8 * k, cx + 1.6 * k, y1 + 1.2 * k)], 0.35));
                break;
            case Loop.None:
                break;
            default:
                face.Holes.Add(Geo.Rect(cx - 0.45, y0, cx + 0.45, y1));
                if (b.L.Loop == Loop.Cross) face.Holes.Add(Geo.Rect(cx - 1.2, (y0 + y1) / 2 - 0.4, cx + 1.2, (y0 + y1) / 2 + 0.4));
                face.Etched.AddRange(Fronts.Outline(cx - 1.5 * k, y0 - 0.8 * k, cx + 1.5 * k, y1 + 0.8 * k, 0.35));
                break;
        }
    }

    /// <summary>A flight of steps etched up the inner face to the walk.</summary>
    static void Steps(Up face, double x, double top, double k)
    {
        double sw = 2.4 * k, sh = 2.4 * k;
        int n = (int)(top / sh);
        for (int i = 0; i < n; i++)
        {
            double y = i * sh, x0 = x + (n - 1 - i) * sw * 0.5;
            face.Lines.Add(Geo.Loop(Geo.Rect(x0, y, x0 + sw * 1.4, y + sh)));
        }
    }

    /// <summary>The moulded course over the walk's tab ends, with corbels (or arches, beam ends, a rail or an armour band) under it.</summary>
    static void StringCourse(Build b, Up s, double u0, double u1, double deckY)
    {
        double k = b.K, T = b.T, y0 = deckY - T - 1.1, y1 = deckY + 1.1;
        var rnd = b.M.Rnd;
        switch (b.L.Course)
        {
            case Course.Rail:
            {
                // A lashed log rail.
                s.Outline.Add(WoodFace.RoundRect(u0, y0, u1, y1, Math.Min(0.9, (y1 - y0) / 2)));
                for (double x = u0 + 1; x < u1 - 1; x += 3.2 * k)
                    s.Lines.Add([new(x + rnd.NextDouble() * 0.4, y0 + 0.4), new(x + 0.5, y1 - 0.4)]);
                s.Lines.Add([new(u0 + 0.4, (y0 + y1) / 2), new(u1 - 0.4, (y0 + y1) / 2 + 0.1)]);
                return;
            }
            case Course.Band:
            {
                // An armour band with hazard stripes and running lights.
                s.Outline.Add(Fronts.Chamfered(u0, y0 - 0.6, u1, y1, 0.01));
                s.Outline.Add(new PathD { new(u0, y0 - 0.6), new(u1, y0 - 0.6), new(u1 - 0.01, y0 - 1.6 * k), new(u0 + 0.01, y0 - 1.6 * k) });
                var stripes = new PathsD();
                for (double x = u0 - 4; x < u1 + 4; x += 2.4 * k)
                    stripes.Add(new PathD { new(x, y0 + 0.2), new(x + 1.2 * k, y0 + 0.2), new(x + 1.2 * k + (y1 - y0 - 0.4), y1 - 0.2), new(x + (y1 - y0 - 0.4), y1 - 0.2) });
                s.Etched.AddRange(Geo.Intersect(Geo.Union(stripes), [Geo.Rect(u0 + 0.3, y0 + 0.2, u1 - 0.3, y1 - 0.2)]));
                for (double x = u0 + 3 * k; x < u1 - 1; x += 6 * k) s.Lines.Add(WoodFace.Dot(x, y0 - 1.0 * k, 0.25));
                return;
            }
        }
        s.Outline.Add(Geo.Rect(u0, y0, u1, y1));
        s.Lines.Add([new(u0, y1 - 0.45), new(u1, y1 - 0.45)]);
        double pitch = 4.2 * k, cw = 1.7 * k, ch = 2.4 * k;
        int n = Math.Max(1, (int)Math.Round((u1 - u0) / pitch));
        pitch = (u1 - u0) / n;
        switch (b.L.Course)
        {
            case Course.Arches:
            {
                // A corbel table: little pointed arches between the corbels.
                s.Outline.Add(Geo.Rect(u0, y0 - ch, u1, y0 + 0.01));
                for (int i = 0; i < n; i++)
                {
                    double a = u0 + i * pitch + cw / 2 + 0.5, c = u0 + (i + 1) * pitch - cw / 2 + 0.5;
                    if (a < u0 + 1.0 || c > u1 - 1.0 || c - a < 1.0) continue;
                    s.Holes.Add(Fronts.PointedRect(a, y0 - ch - 0.01, c, y0 - 1.0));
                }
                break;
            }
            case Course.Beams:
            {
                // The ends of the roof beams poking out, ringed.
                for (int i = 0; i < n; i++)
                {
                    double c = u0 + (i + 0.5) * pitch;
                    if (c - cw / 2 < u0 + 0.2 || c + cw / 2 > u1 - 0.2) continue;
                    s.Outline.Add(Geo.Rect(c - cw / 2, y0 - ch * 0.8, c + cw / 2, y0 + 0.01));
                    s.Lines.Add(WoodFace.Dot(c, y0 - ch * 0.45, cw * 0.28));
                }
                break;
            }
            default:
            {
                // Stepped corbels.
                for (int i = 0; i < n; i++)
                {
                    double c = u0 + (i + 0.5) * pitch;
                    if (c - cw / 2 < u0 + 0.2 || c + cw / 2 > u1 - 0.2) continue;
                    s.Outline.Add(Geo.Rect(c - cw / 2, y0 - ch * 0.5, c + cw / 2, y0 + 0.01));
                    s.Outline.Add(Geo.Rect(c - cw * 0.32, y0 - ch, c + cw * 0.32, y0 - ch * 0.5 + 0.01));
                }
                break;
            }
        }
    }

    /// <summary>The plinth along the foot: a chamfered course (a lower rail for a palisade, a skirt of armour for sci-fi).</summary>
    static void Plinth(Build b, Up p, double u0, double u1)
    {
        double k = b.K;
        switch (b.L.Course)
        {
            case Course.Rail:
                p.Outline.Add(WoodFace.RoundRect(u0, 4 * k, u1, 4 * k + 2.2, 0.9));
                p.Lines.Add([new(u0 + 0.4, 4 * k + 1.1), new(u1 - 0.4, 4 * k + 1.2)]);
                return;
            case Course.Band:
                p.Outline.Add(new PathD { new(u0, 0), new(u1, 0), new(u1, 3.4 * k), new(u1 - 0.01, 4.4 * k), new(u0 + 0.01, 4.4 * k), new(u0, 3.4 * k) });
                for (double x = u0 + 2 * k; x < u1 - 3 * k; x += 5 * k)
                    for (int i = 0; i < 3; i++) p.Etched.Add(Geo.Rect(x, 0.8 * k + i * 0.9 * k, x + 2.6 * k, 0.8 * k + i * 0.9 * k + 0.4));
                return;
        }
        double h = 3.6 * k;
        p.Outline.Add(Geo.Rect(u0, 0, u1, h));
        p.Outline.Add(Geo.Rect(u0, h - 0.01, u1, h + 1.2));
        p.Lines.Add([new(u0, h), new(u1, h)]);
        // The odd big footing stone.
        for (double x = u0 + 2; x < u1 - 4; x += 5.5 * k + b.M.Rnd.NextDouble() * 3 * k)
            p.Lines.Add([new(x, 0.3), new(x, h - 0.3)]);
    }

    // ---- the pieces ----

    static void Straight(Build b, double L, double d, double H)
    {
        double T = b.T;
        var walk = WalkFlat(b, H);
        var r = new Run(new V3(0, 0, 0), V3.UnitX, V3.UnitZ, d, 0, L) { EndPieceStart = true, EndPieceEnd = true };
        walk.Outline.Add(Build.XZRect(0, 2 * T, L, d));
        AddRun(b, r, walk, H);
    }

    static void Corner(Build b, double R, double d, double H, bool inside)
    {
        double T = b.T;
        var walk = WalkFlat(b, H);
        var (_, crenel) = Heights(H);
        double full = Math.Max(2.6, 8.6 * b.K * 0.55);
        if (!inside)
        {
            // Arms along +x and +z from the corner, the outside to −z and −x.
            walk.Outline.Add(Build.XZRect(2 * T, 2 * T, R, d));
            walk.Outline.Add(Build.XZRect(2 * T, 2 * T, d, R));
            AddRun(b, new Run(new V3(0, 0, 0), V3.UnitX, V3.UnitZ, d, 0, R)
            {
                Outer0 = T, Inner0 = d - Lip - T, MerlonStart = full, EndPieceEnd = true,
            }, walk, H, " 1");
            AddRun(b, new Run(new V3(0, 0, 0), V3.UnitZ, V3.UnitX, d, 0, R)
            {
                Outer0 = 2 * T, Inner0 = d - Lip, Strip0 = T, MerlonStart = full - T, EndPieceEnd = true,
            }, walk, H, " 2");
        }
        else
        {
            // The outside to +z and +x, round the inside of the corner.
            walk.Outline.Add(Build.XZRect(0, 0, R, d - 2 * T));
            walk.Outline.Add(Build.XZRect(0, 0, d - 2 * T, R));
            AddRun(b, new Run(new V3(0, 0, d), V3.UnitX, -V3.UnitZ, d, 0, R)
            {
                Outer0 = d - 2 * T, Strip0 = d - T, MerlonStart = full, EndPieceEnd = true, Inner0 = Lip,
            }, walk, H, " 1");
            AddRun(b, new Run(new V3(d, 0, 0), V3.UnitZ, -V3.UnitX, d, 0, R)
            {
                Outer0 = d - T, Strip0 = d, MerlonStart = full, EndPieceEnd = true, Inner0 = Lip + T,
            }, walk, H, " 2");
            _ = crenel;
        }
    }

    static Flat WalkFlat(Build b, double H)
    {
        var (deckY, _) = Heights(H);
        var f = new Flat { Name = "Wall walk", Y = deckY, Texture = b.L.Deck, A = b.L.DA, B = b.L.DB, G = b.L.DG };
        b.Flats.Add(f);
        return f;
    }

    // ---- towers: the gatehouse, the turret at a wall's end, the guard tower ----

    /// <summary>Which sides of a box tower get glued courses (not where a wall runs into it).</summary>
    [Flags] enum Sides { None = 0, Back = 1, Front = 2, Left = 4, Right = 8, All = 15 }

    sealed record BoxFaces(Up Back, Up Front, Up Left, Up Right, Flat Deck);

    /// <summary>
    /// A box tower: four faces from x0–x1, z0–z1 (outside of the faces), a fighting deck at
    /// <paramref name="deckY"/> with its tabs through all four, merlons on every face, and the glued
    /// courses and quoins on the <paramref name="dress"/> sides.
    /// </summary>
    static BoxFaces Box(Build b, string name, double x0, double z0, double x1, double z1, double H, double deckY, Sides dress, (double, double)? frontGap = null)
    {
        double T = b.T, k = b.K, crenel = deckY + (H - deckY) * 0.5;
        var back = b.Upright($"{name} back", new V3(x0, 0, z0 + T / 2), V3.UnitX, x1 - x0, -V3.UnitZ);
        var front = b.Upright($"{name} front", new V3(x0, 0, z1 - T / 2), V3.UnitX, x1 - x0, V3.UnitZ);
        var left = b.Upright($"{name} left", new V3(x0 + T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, -V3.UnitX);
        var right = b.Upright($"{name} right", new V3(x1 - T / 2, 0, z0 + T), V3.UnitZ, z1 - z0 - 2 * T, V3.UnitX);
        double full = Math.Max(2.6, 8.6 * k * 0.55);
        foreach (var f in new[] { back, front })
        {
            f.Outline.Add(Geo.Rect(0, 0, f.Len, crenel));
            Parapet(b, f, 0, f.Len, crenel, H, full, full);
        }
        foreach (var f in new[] { left, right })
        {
            f.Outline.Add(Geo.Rect(0, 0, f.Len, crenel));
            Parapet(b, f, 0, f.Len, crenel, H, full - T, full - T);
        }
        var deck = new Flat { Name = $"{name} roof", Y = deckY, Texture = b.L.Deck, A = b.L.DA, B = b.L.DB, G = b.L.DG };
        deck.Outline.Add(Build.XZRect(x0 + T, z0 + T, x1 - T, z1 - T));
        b.Flats.Add(deck);
        foreach (var f in new[] { back, front, left, right })
            foreach (var (t0, t1) in b.Tabs(0, f.Len, f == left || f == right ? 1.6 : T + 1.6))
                b.FlatInto(deck, f, t0, t1);
        // A hatch in the roof.
        double hx = x0 + T + 2.5 * k, hz = z1 - T - 2.5 * k;
        if (x1 - x0 > 16 * k && z1 - z0 > 16 * k)
        {
            deck.Lines.Add(Geo.Loop(Build.XZRect(hx, hz - 4 * k, hx + 4 * k, hz)));
            deck.Lines.Add(WoodFace.Dot(hx + 3.2 * k, -(hz - 2 * k), 0.3));
        }
        if (b.Glue)
        {
            double mid = deckY;
            if (dress.HasFlag(Sides.Back)) Dress(b, $"{name} back", new V3(x0 - T, 0, z0 - T / 2), V3.UnitX, x1 - x0 + 2 * T, -V3.UnitZ, mid, H);
            if (dress.HasFlag(Sides.Front)) Dress(b, $"{name} front", new V3(x0 - T, 0, z1 + T / 2), V3.UnitX, x1 - x0 + 2 * T, V3.UnitZ, mid, H, frontGap);
            if (dress.HasFlag(Sides.Left)) Dress(b, $"{name} left", new V3(x0 - T / 2, 0, z0), V3.UnitZ, z1 - z0, -V3.UnitX, mid, H);
            if (dress.HasFlag(Sides.Right)) Dress(b, $"{name} right", new V3(x1 + T / 2, 0, z0), V3.UnitZ, z1 - z0, V3.UnitX, mid, H);
        }
        return new(back, front, left, right, deck);
    }

    /// <summary>The string course, plinth and corner quoins glued on one side of a tower.</summary>
    static void Dress(Build b, string name, V3 a, V3 dir, double len, V3 facing, double deckY, double H, (double, double)? gap = null)
    {
        var course = b.Upright($"{name} course", a, dir, len, facing);
        course.Glued = course.Plain = true;
        StringCourse(b, course, 0, len, deckY);
        foreach (var (p0, p1, n) in gap is { } g ? new[] { (0.0, g.Item1, " 1"), (g.Item2, len, " 2") } : [(0.0, len, "")])
        {
            var plinth = b.Upright($"{name} plinth{n}", a, dir, len, facing);
            plinth.Glued = plinth.Plain = true;
            Plinth(b, plinth, p0, p1);
        }
        if (b.L.Wood || b.L.Course == Course.Band) return;
        // Quoins up both corners between the plinth and the course: long and short stones in turn.
        double k = b.K, y0 = 3.6 * k + 1.3, y1 = deckY - b.T - 1.1 - 2.6 * k - 0.4, sh = 3.2 * k;
        if (y1 - y0 < 2 * sh) return;
        foreach (bool atStart in new[] { true, false })
        {
            var q = b.Upright($"{name} quoins {(atStart ? 1 : 2)}", a, dir, len, facing);
            q.Glued = q.Plain = true;
            int n = (int)((y1 - y0) / sh);
            for (int i = 0; i < n; i++)
            {
                double w = (i % 2 == 0 ? 4.4 : 2.8) * k, y = y0 + i * sh;
                var r = atStart ? Geo.Rect(0, y, w, y + sh + 0.01) : Geo.Rect(len - w, y, len, y + sh + 0.01);
                q.Outline.Add(r);
                if (i > 0) q.Lines.Add(atStart ? [new(0.2, y), new(Math.Min(w, (i % 2 == 0 ? 2.8 : 4.4) * k) - 0.2, y)] : [new(len - Math.Min(w, (i % 2 == 0 ? 2.8 : 4.4) * k) + 0.2, y), new(len - 0.2, y)]);
            }
            // Only a piece where it doesn't run into the face's own corbels or tabs.
            if (q.Outline.Count == 0) b.Ups.Remove(q);
        }
    }

    /// <summary>A gatehouse: a tower over a gate passage, with its portcullis and the doors swung open.</summary>
    static void Gate(Build b, double L, double D, double H)
    {
        double T = b.T, k = b.K;
        double Hg = Math.Round(H * 1.22, 1), deckY = Math.Round(Hg * 0.78, 1);
        double x0 = T, x1 = L - T, z0 = T, z1 = D - T;
        double aw = Geo.Clamp(Square * 0.86 * k, 10, L - 4 * T - 8), cx = L / 2;
        double spring = Math.Min(deckY - T - 6 - aw / 2, 26 * k), ah = spring + aw / 2;
        if (spring < 8) { b.M.Bad("The gatehouse is too low for its gate: make the wall taller."); return; }
        if (x1 - x0 < aw + 2 * T + 8) { b.M.Bad("The gatehouse is too short for its gate: make it longer."); return; }
        var box = Box(b, "Gatehouse", x0, z0, x1, z1, Hg, deckY, Sides.Back | Sides.Front);
        // The arch through both faces.
        foreach (var f in new[] { box.Back, box.Front })
        {
            double c = Build.UOf(f, new V3(cx, 0, 0), V3.UnitX, 0);
            var arch = Fronts.ArchedRect(c - aw / 2, -1, c + aw / 2, ah);
            if (b.L.Top is Top.Gabled or Top.Logs) arch = Fronts.PointedRect(c - aw / 2, -1, c + aw / 2, ah + aw * 0.12);
            if (b.L.Top == Top.Armour) arch = Fronts.Chamfered(c - aw / 2, -1, c + aw / 2, ah, aw * 0.22);
            f.Holes.Add(arch);
            // The voussoirs round the arch, and a carved shield over it on the outside.
            f.Etched.AddRange(Fronts.Outline(Geo.Offset([arch], 1.6 * k), 0.35));
            if (!b.L.Wood && b.L.Top != Top.Armour)
                for (int i = 1; i < 8; i++)
                {
                    double t = Math.PI * i / 8, r0 = aw / 2, r1 = aw / 2 + 1.6 * k;
                    f.Lines.Add([new(c + Math.Cos(t) * r0, spring + Math.Sin(t) * r0), new(c + Math.Cos(t) * r1, spring + Math.Sin(t) * r1)]);
                }
        }
        // The passage walls, roof and murder holes.
        double pz0 = z0 + T, pz1 = z1 - T;
        foreach (var (x, face) in new[] { (cx - aw / 2 - T / 2, -V3.UnitX), (cx + aw / 2 + T / 2, V3.UnitX) })
        {
            var w = b.Upright("Passage wall", new V3(x, 0, pz0), V3.UnitZ, pz1 - pz0, -face);
            w.Outline.Add(Geo.Rect(0, 0, pz1 - pz0, deckY - T));
            foreach (var (t0, t1) in b.Tabs(0, pz1 - pz0, 1.5)) b.UpInto(box.Deck, w, t0, t1);
            w.Plain = false;
        }
        var vault = new Flat { Name = "Passage roof", Y = ah + T, Texture = "none" };
        vault.Outline.Add(Build.XZRect(cx - aw / 2, pz0 + 0.02, cx + aw / 2, pz1 - 0.02));
        b.Flats.Add(vault);
        foreach (var w in b.Ups.Where(u => u.Name == "Passage wall").ToList())
            foreach (var (t0, t1) in b.Tabs(0, pz1 - pz0, 2)) b.FlatInto(vault, w, t0, t1);

        if (!b.Glue) return;
        // The portcullis, raised, just inside the outer arch: its spiked foot hangs in the arch.
        double ph = Math.Min(8 * k, aw * 0.6), py0 = ah - ph;
        var port = b.Upright("Portcullis", new V3(cx - aw / 2, 0, pz0 + T / 2), V3.UnitX, aw, -V3.UnitZ);
        port.Glued = port.Plain = true;
        double bar = 1.1, cell = Geo.Clamp(aw / 6, 2.4, 3.4);
        port.Outline.Add(Geo.Rect(0, py0, aw, ah - 0.02));
        var grid = new PathsD();
        for (double x = bar; x + cell - bar <= aw - bar + 1e-6; x += cell)
            for (double y = py0 + 1.6; y + cell - bar <= ah - 1.2; y += cell)
                grid.Add(Geo.Rect(x, y, x + cell - bar, y + cell - bar));
        port.Holes.AddRange(grid);
        for (double x = bar / 2; x < aw; x += cell)
            if (x - 0.55 >= 0 && x + 0.55 <= aw) port.Outline.Add(Geo.Rect(x - 0.55, py0 - 1.3, x + 0.55, py0 + 0.01));
        // The doors, open against the passage walls on the town side.
        double dh = spring + aw * 0.2, dw = Math.Min(aw / 2 - 0.3, pz1 - pz0 - 8);
        foreach (var (x, face, flip) in new[] { (cx - aw / 2 + T / 2, V3.UnitX, false), (cx + aw / 2 - T / 2, -V3.UnitX, true) })
        {
            var door = b.Upright("Gate door", new V3(x, 0, pz1 - dw - 0.3), V3.UnitZ, dw, face);
            door.Glued = door.Plain = true;
            door.Outline.Add(Geo.Rect(0, 0.3, dw, dh));
            var e = new PathsD();
            var l = new List<PathD>();
            Fronts.Door(b.L.Top == Top.Armour ? FrontLook.Metal : FrontLook.Plank, 0, 0.3, dw, dh, flip, b.M.Rnd, e, l);
            door.Etched.AddRange(e);
            door.Lines.AddRange(l);
        }
        // A carved shield or badge over the arch, outside.
        var badge = b.Upright("Badge", new V3(cx - 3.5 * k, 0, z0 - T / 2), V3.UnitX, 7 * k, -V3.UnitZ);
        badge.Glued = badge.Plain = true;
        double by = ah + 2.4 * k + 1.6 * k, bw = 7 * k, bh = Math.Min(8 * k, deckY - T - 1.1 - 2.6 * k - by - 0.6);
        if (bh < 3) { b.Ups.Remove(badge); return; }
        var shield = Shield(0, by, bw, by + bh);
        badge.Outline.Add(shield);
        var be = new PathsD();
        var bl = new List<PathD>();
        Cabinets.Symbol(Fronts.Look(b.M), b.M.Theme, bw / 2, by + bh * 0.55, Math.Min(bw, bh) * 0.3, be, bl);
        badge.Etched.AddRange(be);
        badge.Lines.AddRange(bl);
        badge.Lines.Add(Geo.Loop(Geo.Offset([shield], -0.5).FirstOrDefault() ?? shield));
    }

    /// <summary>A heater shield (a hexagonal badge for sci-fi).</summary>
    static PathD Shield(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0, cx = (x0 + x1) / 2;
        var p = new PathD { new(x0, y1), new(x1, y1), new(x1, y0 + h * 0.45) };
        for (int i = 1; i < 8; i++)
        {
            double t = i / 8.0;
            p.Add(new PointD(x1 - (w / 2) * Math.Sin(t * Math.PI / 2), y0 + h * 0.45 * (1 - Math.Sin(t * Math.PI / 2))));
        }
        p.Add(new PointD(cx, y0));
        for (int i = 7; i >= 1; i--)
        {
            double t = i / 8.0;
            p.Add(new PointD(x0 + (w / 2) * Math.Sin(t * Math.PI / 2), y0 + h * 0.45 * (1 - Math.Sin(t * Math.PI / 2))));
        }
        p.Add(new PointD(x0, y0 + h * 0.45));
        return p;
    }

    /// <summary>A wall that ends in a turret, standing out on both sides, with a door onto the walk.</summary>
    static void End(Build b, double L, double D, double H)
    {
        double T = b.T, k = b.K, d = Math.Min(D, Math.Round(19 * k, 1));
        double tw = Math.Min(Math.Round(14 * k, 1), L - 10), Ht = Math.Round(H * 1.18, 1), tDeck = Math.Round(Ht * 0.76, 1);
        if (tw < 8 + 2 * T) { b.M.Bad("The wall's end is too short for its turret: make it longer."); return; }
        double zc = D / 2, wz0 = zc - d / 2;
        var box = Box(b, "Turret", T, T, T + tw, D - T, Ht, tDeck, Sides.Back | Sides.Front | Sides.Left);
        var walk = WalkFlat(b, H);
        double s0 = T + tw;
        walk.Outline.Add(Build.XZRect(s0, wz0 + 2 * T, L, wz0 + d));
        AddRun(b, new Run(new V3(0, 0, wz0), V3.UnitX, V3.UnitZ, d, 0, L) { Outer0 = s0, Inner0 = s0, Strip0 = s0, EndPieceEnd = true, MerlonStart = -1 }, walk, H);
        // The walk's tabs into the turret's side, and a door onto it.
        var side = box.Right;
        var (deckY, _) = Heights(H);
        double ua = Build.UOf(side, new V3(0, 0, wz0 + 2 * T), V3.UnitX, 0), ub = Build.UOf(side, new V3(0, 0, wz0 + d - Lip - T), V3.UnitX, 0);
        double lo = Math.Min(ua, ub), hi = Math.Max(ua, ub);
        double mid = (lo + hi) / 2, tab = Math.Min(3.2, (hi - lo) * 0.3);
        b.FlatInto(walk, side, mid - tab / 2 - (hi - lo) * 0.25, mid + tab / 2 - (hi - lo) * 0.25);
        b.FlatInto(walk, side, mid - tab / 2 + (hi - lo) * 0.25, mid + tab / 2 + (hi - lo) * 0.25);
        double dw = Math.Min(5 * k, (hi - lo) * 0.4);
        if (deckY + 9 * k < tDeck - T - 1)
        {
            var door = Fronts.ArchedRect(mid - dw / 2, deckY + 0.3, mid + dw / 2, deckY + 9 * k);
            side.Etched.AddRange(Fronts.Outline([door], 0.4));
            side.Lines.Add(WoodFace.Dot(mid + dw * 0.3, deckY + 4.5 * k, 0.25));
        }
        // A loop in each face of the turret.
        foreach (var f in new[] { box.Back, box.Front, box.Left })
            if (b.L.Loop != Loop.None) LowLoop(b, f, f.Len / 2, Ht * 0.25, Ht * 0.45);
    }

    // ---- the guard tower ----

    /// <summary>A guard tower: a tall box tower with a door, loops on every side at two levels and a flag (or a mast) on the roof.</summary>
    public static void Tower(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 92;
        if (W < 24 || D < 24) { m.Bad("The tower is too small: make it at least 24 mm each way."); return; }
        if (H < 40) { m.Bad("The tower is too low: make it at least 40 mm high."); return; }
        var b = new Build(m, LookOf(m.Theme), k);
        double deckY = Math.Round(H * 0.84, 1);
        double flen = W - 2 * T, dw = Math.Min(10 * k, flen * 0.3), dh = Math.Min(19 * k, deckY * 0.3), cx = flen / 2;
        var box = Box(b, "Tower", T, T, W - T, D - T, H, deckY, Sides.All, (T + cx - dw / 2 - 0.05, T + cx + dw / 2 + 0.05));
        // The door at the foot of the front.
        var front = box.Front;
        var doorShape = b.L.Top switch
        {
            Top.Gabled or Top.Logs => Fronts.PointedRect(cx - dw / 2, 0.01, cx + dw / 2, dh),
            Top.Armour => Fronts.Chamfered(cx - dw / 2, 0.01, cx + dw / 2, dh, dw * 0.25),
            _ => Fronts.ArchedRect(cx - dw / 2, 0.01, cx + dw / 2, dh),
        };
        front.Etched.AddRange(Fronts.Outline(Geo.Offset([doorShape], 1.4 * k), 0.4));
        if (b.Glue)
        {
            // The door itself, glued on proud of the wall.
            var door = b.Upright("Door", new V3(T, 0, D - T / 2), V3.UnitX, front.Len, V3.UnitZ);
            door.Glued = door.Plain = true;
            door.Outline.Add(doorShape);
            var e = new PathsD();
            var l = new List<PathD>();
            Fronts.Door(b.L.Top == Top.Armour ? FrontLook.Metal : FrontLook.Plank, cx - dw / 2, 0.01, cx + dw / 2, dh, true, m.Rnd, e, l);
            door.Etched.AddRange(e);
            door.Lines.AddRange(l);
        }
        else
        {
            var e = new PathsD();
            var l = new List<PathD>();
            Fronts.Door(b.L.Top == Top.Armour ? FrontLook.Metal : FrontLook.Plank, cx - dw / 2, 0.01, cx + dw / 2, dh, true, m.Rnd, e, l);
            front.Etched.AddRange(e);
            front.Lines.AddRange(l);
        }
        // Loops on every side, two levels (the door's side gets them off to the sides).
        foreach (var f in new[] { box.Back, box.Left, box.Right, box.Front })
        {
            if (b.L.Loop == Loop.None) break;
            double y0 = deckY * 0.38, y1 = deckY * 0.38 + 9 * k, y2 = deckY * 0.68, y3 = deckY * 0.68 + 9 * k;
            if (f == box.Front)
            {
                LowLoop(b, f, f.Len * 0.5, y2, y3);
                continue;
            }
            LowLoop(b, f, f.Len * 0.5, y0, y1);
            LowLoop(b, f, f.Len * 0.5, y2, y3);
        }
        // The flag (or a mast with its dish and lamp) standing in the roof.
        Flagpole(b, box.Deck, W / 2, D / 2, deckY, H);
        b.Finish();
        m.Note(m.PushFit
            ? "Push the roof's tabs through the four walls, back and front first, then stand the flagpole in its slot."
            : "Put the roof's tabs through the four walls, back and front first, then glue on the courses, the plinth, the quoins and the door, and stand the flagpole in its slot.");
    }

    /// <summary>A pole with a banner (a mast with a dish for sci-fi) standing in a slot in a roof.</summary>
    static void Flagpole(Build b, Flat deck, double x, double z, double deckY, double H)
    {
        double k = b.K, ph = Math.Max(18 * k, (H - deckY) + 14 * k), pw = 1.5;
        var pole = b.Upright("Flagpole", new V3(x - 0.75, 0, z), V3.UnitX, 12 * k, V3.UnitZ);
        pole.Plain = true;
        pole.Outline.Add(Geo.Rect(0, deckY, pw, deckY + ph));
        b.DownInto(deck, pole, 0, pw);
        double fy1 = deckY + ph - 1.2, fy0 = fy1 - 7 * k, fx1 = pw + 9 * k;
        if (b.L.Top == Top.Armour)
        {
            // A sensor mast: a dish and a beacon.
            pole.Outline.Add(new PathD { new(pw - 0.01, fy1 - 2.5 * k), new(pw + 4.5 * k, fy1 - 0.5 * k), new(pw + 4.5 * k, fy1 + 0.6 * k), new(pw - 0.01, fy1 - 1.2 * k) });
            pole.Outline.Add(Geo.Circle(pw / 2, deckY + ph + 0.6, 1.1, 0.01));
            pole.Lines.Add(WoodFace.Dot(pw / 2, deckY + ph + 0.6, 0.5));
            return;
        }
        // A swallow-tailed banner (a ragged one for a dungeon or a cave).
        var flag = new PathD { new(pw - 0.01, fy0), new(fx1, fy0), new(fx1, fy0 + 1.1), new(fx1 - 2.6 * k, (fy0 + fy1) / 2), new(fx1, fy1 - 1.1), new(fx1, fy1), new(pw - 0.01, fy1) };
        if (b.L.Top is Top.Broken or Top.Logs)
            flag = [new(pw - 0.01, fy0), new(fx1 - 1.5 * k, fy0 + 0.6 * k), new(fx1 - 1.5 * k, fy0 + 1.8 * k), new(fx1, (fy0 + fy1) / 2 - 0.6), new(fx1, (fy0 + fy1) / 2 + 0.6), new(fx1 - 2.2 * k, fy1 - 1.4 * k), new(fx1 - 0.8 * k, fy1), new(pw - 0.01, fy1)];
        pole.Outline.Add(flag);
        pole.Outline.Add(Geo.Circle(pw / 2, deckY + ph + 0.4, 0.95, 0.01));
        var e = new PathsD();
        var l = new List<PathD>();
        Cabinets.Symbol(Fronts.Look(b.M), b.M.Theme, (pw + fx1 - 2.6 * k) / 2, (fy0 + fy1) / 2, Math.Min(fx1 - pw - 3 * k, fy1 - fy0) * 0.32, e, l);
        pole.Etched.AddRange(e);
        pole.Lines.AddRange(l);
        pole.Lines.Add([new(pw + 0.6, fy0 + 0.5), new(fx1 - 3.0 * k, fy0 + 0.5)]);
    }

    /// <summary>
    /// The ground a town piece covers (mm, x then z), which can be more than its width and depth:
    /// a corner covers its arm both ways.
    /// </summary>
    public static (double W, double D) Footprint(FurnitureItem it) =>
        it.Kind == ItemKind.TownWall && it.Wall is WallPiece.OutsideCorner or WallPiece.InsideCorner ? (it.WidthMm, it.WidthMm) : (it.WidthMm, it.DepthMm);
}
