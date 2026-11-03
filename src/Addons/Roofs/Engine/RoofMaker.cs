using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// Makes a roof that lifts off a building. It's built on a base plate the size of what it sits on, with
/// a locator under it that drops inside the walls so it can't slide off; gable ends (or a ridge board),
/// trusses and a back wall stand on the base with tabs in slots, and the slopes rest on them with their
/// shingles etched on. A cone is stacked from rings (see <see cref="Rings"/>).
/// </summary>
/// <remarks>
/// In the model X runs along the ridge (0 to the length), Y across it (−width/2 to width/2) and Z up from
/// the top of what the roof sits on. The base plate is Z 0 to T; everything else stands on it.
/// </remarks>
public static class RoofMaker
{
    /// <summary>Gap between the locator and the inside of the walls, all round (mm).</summary>
    public const double ClearMm = 0.3;
    /// <summary>Least material left between a slot and the edge of a piece (mm).</summary>
    public const double BridgeMm = 2.5;
    /// <summary>The locator reaches this far below the tops of the walls (mm).</summary>
    public const double GripMm = 2;

    /// <summary>The roof made to sit on a room of the wall maker's walls (see <see cref="WallsFit.Fit(RoofSpec, WallSpec, WallSpec?)"/>).</summary>
    public static RoofSpec FitToWalls(RoofSpec s, WallSpec longWalls, WallSpec? shortWalls = null) => WallsFit.Fit(s, longWalls, shortWalls);

    public static RoofKit Make(RoofSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<RoofPart>();
        double T = Math.Max(0.3, s.ThicknessMm);
        if (s.Style == RoofStyle.Cone) return Rings.ConeRoof(s, findings);

        double L = s.LengthMm, W = s.WidthMm, pitch = Math.Clamp(s.PitchDeg, 10, 75);
        if (L < 20 || W < 20) { findings.Add(new(Severity.Bad, "The roof is too small: make it at least 20 × 20 mm.")); return new(parts, findings, T); }
        // A hipped roof's ridge runs along the longer side: made that way round, then turned.
        bool turn = s.Style == RoofStyle.Hip && W > L;
        if (turn) (L, W) = (W, L);
        double t = Math.Tan(pitch * Math.PI / 180), c = Math.Cos(pitch * Math.PI / 180), sn = Math.Sin(pitch * Math.PI / 180);
        double e = Math.Max(0, s.EaveMm), fit = Math.Max(0, s.FitMm), z0 = T;
        // The gables (and a lean-to's back wall) stand over the walls: their middle over the walls' middle.
        double x0 = s.InsetMm > 0 ? s.InsetMm - Math.Max(0, s.WallMm) / 2 - T / 2 : BridgeMm;
        x0 = Math.Clamp(x0, BridgeMm, Math.Max(BridgeMm, Math.Min(L, W) / 4));

        var standing = new List<(RoofPart Part, PathsD Tabs)>();
        switch (s.Style)
        {
            case RoofStyle.Gable: Gable(s, L, W, T, t, c, sn, e, z0, x0, parts, standing, findings); break;
            case RoofStyle.LeanTo: LeanTo(s, L, W, T, t, c, sn, e, z0, x0, parts, standing, findings); break;
            default: Hip(s, L, W, T, t, c, sn, e, z0, fit, parts, standing, findings); break;
        }
        if (findings.Any(f => f.Level == Severity.Bad)) return new(parts, findings, T);

        // The base: slots for every tab standing on it.
        var slots = new PathsD();
        foreach (var (part, tabs) in standing)
            foreach (var pose in part.Poses)
                foreach (var tab in tabs)
                {
                    var b = Geo.Bounds([tab]);
                    var pts = new[] { pose.At(b.left, 0, 0), pose.At(b.right, 0, 0), pose.At(b.left, 0, T), pose.At(b.right, 0, T) };
                    double ax = pts.Min(p => p.X) - fit / 2, bx = pts.Max(p => p.X) + fit / 2, ay = pts.Min(p => p.Y) - fit / 2, by = pts.Max(p => p.Y) + fit / 2;
                    slots.Add(Geo.Rect(ax, ay + W / 2, bx, by + W / 2));
                }
        var basePlate = Geo.Subtract([Geo.Rect(0, 0, L, W)], Geo.Union(slots));
        parts.Insert(0, new RoofPart("Base", basePlate, [], [], 1, [Pose.Flat(new V3(0, -W / 2, 0))]));
        parts.InsertRange(1, Locator(s, L, W, T, findings));

        if (turn)
            for (int i = 0; i < parts.Count; i++)
                parts[i] = parts[i] with { Poses = [.. parts[i].Poses.Select(p => p.Turned(90, L / 2, 0).Moved(new V3(W / 2 - L / 2, 0, 0)))] };

        findings.Add(new(Severity.Note, $"The ridge is {W / 2 * t + T:0.#} mm above the base ({W / 2 * t + 2 * T:0.#} mm above what it sits on)."));
        findings.Add(new(Severity.Note, "Glue the gables, trusses and slopes to the base and each other; the whole roof lifts off."));
        return Finish(parts, findings, T, s.Roofs);
    }

    /// <summary>Every copy of every part counted for <paramref name="models"/> of them.</summary>
    internal static RoofKit Finish(List<RoofPart> parts, List<Finding> findings, double T, int models)
    {
        int m = Math.Max(1, models);
        return new([.. parts.Where(p => p.Cut.Count > 0).Select(p => p with { Copies = Math.Max(1, p.Poses.Count) * m })], findings, T);
    }

    /// <summary>The locator: a frame under the base, inside the walls and clear of the columns, deep enough to reach below the walls' tops.</summary>
    static List<RoofPart> Locator(RoofSpec s, double L, double W, double T, List<Finding> findings)
    {
        if (s.InsetMm <= 0) return [];
        double inset = s.InsetMm + ClearMm, corner = s.CornerMm > 0 ? s.CornerMm + ClearMm : 0;
        if (L - 2 * inset < 10 || W - 2 * inset < 10)
        {
            findings.Add(new(Severity.Warn, "The walls leave no room inside for the locator under the roof: it has none, so it can slide."));
            return [];
        }
        var outer = new PathsD { Geo.Rect(inset, inset, L - inset, W - inset) };
        if (corner > inset)
            outer = Geo.Subtract(outer, [Geo.Rect(-1, -1, corner, corner), Geo.Rect(L - corner, -1, L + 1, corner), Geo.Rect(L - corner, W - corner, L + 1, W + 1), Geo.Rect(-1, W - corner, corner, W + 1)]);
        double band = Math.Max(8, Math.Max(0, corner - inset) + 5);
        var hole = Geo.Rect(inset + band, inset + band, L - inset - band, W - inset - band);
        bool holed = L - 2 * (inset + band) >= 15 && W - 2 * (inset + band) >= 15;
        var frame = holed ? Geo.Subtract(outer, [hole]) : outer;
        if (Geo.Pieces(frame) != 1) frame = outer;
        int layers = Math.Max(1, (int)Math.Ceiling((Math.Max(0, s.RestAboveWallsMm) + GripMm) / T - 1e-6));
        var poses = Enumerable.Range(0, layers).Select(i => Pose.Flat(new V3(0, -W / 2, -(i + 1) * T))).ToList();
        findings.Add(new(Severity.Note, layers == 1 ? "Glue the locator under the middle of the base: it drops inside the walls."
            : $"Glue the {layers} locator layers under the middle of the base: they drop inside the walls."));
        return [new RoofPart("Locator", Geo.Tidy(frame), [], [], layers, poses)];
    }

    // ---- the pieces that stand on the base ----

    /// <summary>Tabs along the bottom of a standing piece (v below 0), each <paramref name="width"/> wide at the given u.</summary>
    static PathsD Tabs(IEnumerable<double> us, double width, double T) => [.. us.Select(u => Geo.Rect(u - width / 2, -T, u + width / 2, 0.01))];

    static double TabWidth(double span) => Math.Clamp(span * 0.12, 5, 12);

    /// <summary>The wall maker's texture over a gable's face (y up from its foot), the window cut and framed.</summary>
    static (PathsD Cut, PathsD Etched, List<PathD> Lines) Faced(RoofSpec s, PathsD profile, PathsD tabs, PathsD? window)
    {
        var cut = Geo.Union(profile, tabs);
        var keep = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (window is { Count: > 0 })
        {
            cut = Geo.Subtract(cut, window);
            var frame = Geo.Offset(window, 1.5, round: true);
            keep.AddRange(frame);
            etched.AddRange(Geo.Intersect(Geo.Subtract(frame, Geo.Offset(window, 0.9, round: true)), cut));
        }
        var (x0, _, x1, y1) = Shapes.Box(profile);
        var wall = new WallSpec { Texture = s.GableTexture, Custom = s.GableCustom, SizeA = s.GableA, SizeB = s.GableB, GrooveMm = s.GableGrooveMm, Seed = s.GableSeed };
        var tex = Textures.Make(wall, new Face(x1 - x0, y1, 0, x1 - x0, y1));
        tex = new TextureParts(Geo.Move(tex.Raised, x0, 0), Geo.Move(tex.Etched, x0, 0), Geo.Map(tex.Lines, p => new PointD(p.x + x0, p.y)));
        var region = Geo.Subtract(Geo.Intersect(cut, profile), Geo.Union(keep));
        var (te, tl) = Shingles.Apply(tex, region, s.Etch);
        etched.AddRange(te);
        lines.AddRange(tl);
        return (cut, Geo.Union(etched), lines);
    }

    /// <summary>A window for a gable <paramref name="w"/> wide at its foot and <paramref name="h"/> high, or null if none fits.</summary>
    static PathsD? GableWindowShape(RoofSpec s, PathsD profile, double cx, double h, List<Finding> findings)
    {
        if (s.Window == GableWindow.None) return null;
        double ww = Math.Max(2, s.WindowMm);
        double wh = s.Window switch { GableWindow.Round => ww, GableWindow.Arched => ww * 1.4, GableWindow.Square => ww * 1.2, GableWindow.Angled => ww * 0.8, _ => ww * 1.5 };
        double bottom = Math.Max(3, h * 0.38 - wh / 2);
        var shape = s.Window switch
        {
            GableWindow.Round => OpeningShape.Round,
            GableWindow.Arched => OpeningShape.Arched,
            GableWindow.Pointed => OpeningShape.Pointed,
            GableWindow.Angled => OpeningShape.Chamfered,
            _ => OpeningShape.Rectangle,
        };
        var win = WallKitMaker.Opening(shape, cx - ww / 2, bottom, ww, wh);
        if (Geo.Area(Geo.Subtract(Geo.Offset(win, 3), profile)) > 1e-3)
        {
            findings.Add(new(Severity.Warn, "The gable window doesn't fit in the gable: make it smaller or the roof steeper."));
            return null;
        }
        return win;
    }

    /// <summary>Evenly spaced positions strictly between a and b, about <paramref name="spacing"/> apart (none if spacing is 0).</summary>
    static List<double> Between(double a, double b, double spacing)
    {
        if (spacing <= 0 || b - a < spacing * 0.75) return [];
        int n = Math.Max(1, (int)Math.Ceiling((b - a) / spacing) - 1);
        return [.. Enumerable.Range(1, n).Select(i => a + (b - a) * i / (n + 1))];
    }

    /// <summary>A truss: the profile with the middle cut out, keeping rails round it, if that leaves one sound piece.</summary>
    static PathsD Opened(PathsD profile, PathsD tabs, double rail, double? below = null)
    {
        var whole = Geo.Union(profile, tabs);
        var hole = Geo.Offset(profile, -rail);
        if (below is { } top) hole = Shapes.Below(hole, 0, 1, top);
        hole = Shapes.Below(hole, 0, -1, -rail);
        if (Geo.Area(hole) < 40) return whole;
        var opened = Geo.Subtract(whole, hole);
        return Geo.Pieces(opened) == 1 ? opened : whole;
    }

    static void Gable(RoofSpec s, double L, double W, double T, double t, double c, double sn, double e, double z0, double x0, List<RoofPart> parts, List<(RoofPart, PathsD)> standing, List<Finding> findings)
    {
        double h = W / 2 * t;
        var profile = Shapes.Below(Shapes.Below([Geo.Rect(0, 0, W, h + 1)], -t, 1, 0), t, 1, W * t);
        double tw = TabWidth(W);
        var tabs = Tabs([W * 0.25, W * 0.75], tw, T);
        var window = GableWindowShape(s, profile, W / 2, h, findings);
        var (cut, etched, lines) = Faced(s, profile, tabs, window);
        // Outer faces outwards: the near one faces −X, the far one +X.
        var gable = new RoofPart("Gable end", cut, etched, lines, 2,
        [
            new Pose(new V3(x0 + T, W / 2, z0), -V3.UnitY, V3.UnitZ),
            new Pose(new V3(L - x0 - T, -W / 2, z0), V3.UnitY, V3.UnitZ),
        ]);
        parts.Add(gable);
        standing.Add((gable, tabs));

        var xs = Between(x0 + T, L - x0 - T, s.TrussSpacingMm).Select(x => x - T / 2).ToList();
        if (xs.Count > 0)
        {
            var truss = new RoofPart("Truss", Opened(profile, tabs, Math.Clamp(Math.Min(W, h) * 0.08, 3.5, 6)), [], [], xs.Count,
                [.. xs.Select(x => new Pose(new V3(x, -W / 2, z0), V3.UnitY, V3.UnitZ))]);
            parts.Add(truss);
            standing.Add((truss, tabs));
        }

        // The slopes. Without caps, at the ridge the left one runs on over the right one's end (see the class
        // notes in the help); with caps both stop at the ridge and the cap closes the joint.
        double sin2 = Math.Sin(2 * Math.Atan(t)), tan2 = Math.Tan(2 * Math.Atan(t));
        double startR = s.Caps ? 0 : tan2 > 1e-9 ? T / tan2 : 0, startL = s.Caps ? 0 : -T / Math.Max(1e-6, sin2);
        double S = W / 2 / c + e, lp = L + 2 * s.GableOverhangMm, go = s.GableOverhangMm;
        if (lp < 5 || S - startR < 5) { findings.Add(new(Severity.Bad, "The slopes come out too small: make the roof bigger.")); return; }
        var right = new Pose(new V3(L + go, W / 2 + e * c, z0 - e * sn), -V3.UnitX, new V3(0, -c, sn));
        var left = new Pose(new V3(-go, -W / 2 - e * c, z0 - e * sn), V3.UnitX, new V3(0, c, sn));
        parts.Add(Slope(s, "Slope (right)", lp, S - startR, right));
        parts.Add(Slope(s, "Slope (left)", lp, S - startL, left));
        if (s.Caps)
        {
            // A strip on each slope along the ridge, the left one running on over the right one's edge.
            double phi = 2 * Math.Atan(t);
            PathD Strip() => [new(0, 0), new(lp, 0), new(lp, S), new(0, S)];
            parts.Add(Caps(s, "Ridge cap (right)", Strip(), [new Seam(2, phi, false)], T, [right]));
            parts.Add(Caps(s, "Ridge cap (left)", Strip(), [new Seam(2, phi, true)], T, [left]));
        }
        if (s.TrussSpacingMm > 0 && xs.Count == 0 && L - 2 * x0 > 90) findings.Add(new(Severity.Note, "No trusses: the gables hold the slopes up."));
    }

    static void LeanTo(RoofSpec s, double L, double W, double T, double t, double c, double sn, double e, double z0, double x0, List<RoofPart> parts, List<(RoofPart, PathsD)> standing, List<Finding> findings)
    {
        // It rises from the front edge (−Y) to the back wall, which stands over the back of the building.
        double wl = W - x0, h = wl * t, back = -W / 2 + wl;
        var profile = Shapes.Below([Geo.Rect(0, 0, wl, h + 1)], -t, 1, 0);
        double tw = TabWidth(wl);
        var tabs = Tabs([wl * 0.3, wl * 0.78], tw, T);
        var window = GableWindowShape(s, profile, wl * 0.62, h * 1.3, findings);
        var (cut, etched, lines) = Faced(s, profile, tabs, window);
        // The far side faces +X with u from the front; the near side faces −X, so it's the mirror image.
        static PointD Flip(PointD p, double w) => new(w - p.x, p.y);
        var far = new RoofPart("Side (right)", cut, etched, lines, 1, [new Pose(new V3(L - x0 - T, -W / 2, z0), V3.UnitY, V3.UnitZ)]);
        var near = new RoofPart("Side (left)", Geo.Tidy(Geo.Map(cut, p => Flip(p, wl))), Geo.Tidy(Geo.Map(etched, p => Flip(p, wl))), Geo.Map(lines, p => Flip(p, wl)), 1,
            [new Pose(new V3(x0 + T, back, z0), -V3.UnitY, V3.UnitZ)]);
        parts.Add(far);
        parts.Add(near);
        standing.Add((far, tabs));
        standing.Add((near, Geo.Tidy(Geo.Map(tabs, p => Flip(p, wl)))));

        // The back wall, between the sides, its top just under the slope.
        double bl = L - 2 * (x0 + T), bh = h - T * t;
        if (bl < 10 || bh < 3) { findings.Add(new(Severity.Bad, "The roof is too short or too shallow for its back wall: make it longer or steeper.")); return; }
        var backProfile = new PathsD { Geo.Rect(0, 0, bl, bh) };
        var backTabs = Tabs([bl * 0.2, bl * 0.5, bl * 0.8], TabWidth(bl), T);
        var (bc, be, bln) = Faced(s, backProfile, backTabs, null);
        var backWall = new RoofPart("Back wall", bc, be, bln, 1, [new Pose(new V3(L - x0 - T, back - T, z0), -V3.UnitX, V3.UnitZ)]);
        parts.Add(backWall);
        standing.Add((backWall, backTabs));

        var xs = Between(x0 + T, L - x0 - T, s.TrussSpacingMm).Select(x => x - T / 2).ToList();
        if (xs.Count > 0)
        {
            // Trusses stop short of the back wall.
            var tp = Shapes.Below(profile, 1, 0, wl - T);
            var truss = new RoofPart("Truss", Opened(tp, tabs, Math.Clamp(Math.Min(wl, h) * 0.08, 3.5, 6)), [], [], xs.Count,
                [.. xs.Select(x => new Pose(new V3(x, -W / 2, z0), V3.UnitY, V3.UnitZ))]);
            parts.Add(truss);
            standing.Add((truss, tabs));
        }

        double lp = L + 2 * s.GableOverhangMm, go = s.GableOverhangMm;
        parts.Add(Slope(s, "Slope", lp, 2 * e + wl / c, new Pose(new V3(-go, -W / 2 - e * c, z0 - e * sn), V3.UnitX, new V3(0, c, sn))));
    }

    static void Hip(RoofSpec s, double L, double W, double T, double t, double c, double sn, double e, double z0, double fit, List<RoofPart> parts, List<(RoofPart, PathsD)> standing, List<Finding> findings)
    {
        double h = W / 2 * t, oh = e * c, drop = e * sn, S = W / 2 / c + e;
        // The ridge board along the middle, under where the long slopes meet.
        var board = Shapes.Below(Shapes.Below(Shapes.Below([Geo.Rect(0, 0, L, h + 1)], -t, 1, 0), t, 1, L * t), 0, 1, (W / 2 - T / 2) * t);
        // Trusses across, as many as fit along the ridge (one in the middle of a pyramid).
        double ridge = L - W;
        int n = s.TrussSpacingMm > 0 ? Math.Max(1, (int)Math.Round(ridge / s.TrussSpacingMm) + 1) : 1;
        var xs = Enumerable.Range(0, n).Select(i => W / 2 + ridge * (i + 0.5) / n - T / 2).ToList();
        if (n == 1) xs = [L / 2 - T / 2];
        double slotW = T + fit;
        var trussPoses = new List<Pose>();
        var trussParts = new List<RoofPart>();
        var boardSlots = new PathsD();
        int k = 0;
        foreach (var x in xs)
        {
            double cap = Math.Min(x, L - x - T) * t;
            var tp = Shapes.Below(Shapes.Below(Shapes.Below([Geo.Rect(0, 0, W, h + 1)], -t, 1, 0), t, 1, W * t), 0, 1, cap);
            double top = Math.Min(cap, (W / 2 - T / 2) * t), half = top / 2;
            var tabs = Tabs([W * 0.22, W * 0.78], TabWidth(W) * 0.8, T);
            var slot = Geo.Rect(W / 2 - slotW / 2, half, W / 2 + slotW / 2, h + 2);
            var cut = Geo.Subtract(Opened(tp, tabs, Math.Clamp(Math.Min(W, h) * 0.08, 3.5, 6), half - 3), [slot]);
            boardSlots.Add(Geo.Rect(x - fit / 2, -T - 1, x + T + fit / 2, half));
            var part = new RoofPart(n == 1 ? "Cross board" : $"Truss {++k}", cut, [], [], 1, [new Pose(new V3(x, -W / 2, z0), V3.UnitY, V3.UnitZ)]);
            trussParts.Add(part);
            standing.Add((part, tabs));
        }
        // The ridge board's tabs keep clear of the trusses' slots.
        var cand = Enumerable.Range(0, 7).Select(i => L * (i + 1) / 8.0).Where(u => xs.All(x => Math.Abs(u - (x + T / 2)) > T + 6) && u * t > 4 && (L - u) * t > 4).ToList();
        var boardTabs = Tabs(cand.Count > 3 ? [cand[0], cand[cand.Count / 2], cand[^1]] : cand, TabWidth(L) * 0.7, T);
        var boardCut = Geo.Subtract(Geo.Union(board, boardTabs), boardSlots);
        if (Geo.Pieces(boardCut) != 1) { findings.Add(new(Severity.Bad, "The roof is too small for its ridge board and trusses: make it bigger or steeper.")); return; }
        var ridgeBoard = new RoofPart("Ridge board", boardCut, [], [], 1, [new Pose(new V3(0, T / 2, z0), V3.UnitX, V3.UnitZ)]);
        parts.Add(ridgeBoard);
        standing.Add((ridgeBoard, boardTabs));
        parts.AddRange(trussParts);

        // The slopes: two trapezoids along the sides and a triangle at each end, all reaching out to the eaves.
        double lx = L + 2 * oh, wx = W + 2 * oh;
        PathD trap = [new(0, 0), new(lx, 0), new(oh + L - W / 2, S), new(oh + W / 2, S)];
        PathD tri = [new(0, 0), new(wx, 0), new(wx / 2, S)];
        var trapPart = Slope(s, "Long slope", [trap], new Pose(new V3(L + oh, W / 2 + oh, z0 - drop), -V3.UnitX, new V3(0, -c, sn)));
        var triPart = Slope(s, "End slope", [tri], new Pose(new V3(L + oh, -W / 2 - oh, z0 - drop), V3.UnitY, new V3(-c, 0, sn)));
        parts.Add(trapPart with { Copies = 2, Poses = [.. trapPart.Poses, new Pose(new V3(-oh, -W / 2 - oh, z0 - drop), V3.UnitX, new V3(0, c, sn))] });
        parts.Add(triPart with { Copies = 2, Poses = [.. triPart.Poses, new Pose(new V3(-oh, W / 2 + oh, z0 - drop), -V3.UnitY, new V3(c, 0, sn))] });

        if (s.Caps)
        {
            // Each slope gets one cap piece lying on it along its hips (and the ridge): where two meet over a
            // joint, the long slope's runs on over the end slope's edge, and the front one's over the back one's
            // at the ridge, so the joints close up tight.
            double hipPhi = Math.Acos(Math.Clamp(c * c, -1, 1)), ridgePhi = 2 * Math.Atan(t);
            // Over a point (no ridge), the long slopes' caps meet at the top as if over a ridge with no length.
            var front = trapPart.Poses[0];
            var back = new Pose(new V3(-oh, -W / 2 - oh, z0 - drop), V3.UnitX, new V3(0, c, sn));
            parts.Add(Caps(s, "Long slope cap (front)", trap, [new Seam(1, hipPhi, true), new Seam(2, ridgePhi, true), new Seam(3, hipPhi, true)], T, [front]));
            parts.Add(Caps(s, "Long slope cap (back)", trap, [new Seam(1, hipPhi, true), new Seam(2, ridgePhi, false), new Seam(3, hipPhi, true)], T, [back]));
            parts.Add(Caps(s, "End slope cap", tri, [new Seam(1, hipPhi, false), new Seam(2, hipPhi, false)], T, [.. triPart.Poses, new Pose(new V3(-oh, W / 2 + oh, z0 - drop), -V3.UnitY, new V3(c, 0, sn))]));
        }
        findings.Add(new(Severity.Note, n == 1 ? "The ridge board and the cross board slot together halfway, and stand in slots in the base." : $"The ridge board and the {n} trusses slot together halfway, and stand in slots in the base."));
    }

    // ---- the slopes and caps ----

    static RoofPart Slope(RoofSpec s, string name, double w, double h, Pose pose) => Slope(s, name, [Geo.Rect(0, 0, w, h)], pose);

    /// <summary>A slope: its outline with the shingles etched on, courses running along its bottom edge (the eave).</summary>
    internal static RoofPart Slope(RoofSpec s, string name, PathsD outline, Pose pose)
    {
        var (_, _, x1, y1) = Shapes.Box(outline);
        var tex = Shingles.Make(s.Shingle, s.ShingleA, s.ShingleB, s.ShingleGrooveMm, s.Seed, x1, y1);
        var (etched, lines) = Shingles.Apply(tex, Geo.Tidy(outline), s.Etch);
        return new RoofPart(name, Geo.Tidy(outline), etched, lines, 1, [pose]);
    }

    /// <summary>
    /// A joint along edge <see cref="Edge"/> of a slope's flat outline (from its point Edge to the next) with
    /// the slope beyond it: <see cref="Phi"/> is the angle between the two slopes' faces (between their
    /// normals), and <see cref="Over"/> whether this side's cap runs on over the other's edge.
    /// </summary>
    internal readonly record struct Seam(int Edge, double Phi, bool Over);

    /// <summary>How wide a cap's strip is on each side of a joint (mm).</summary>
    internal static double CapStrip(double T) => Math.Max(3, T + 1.5);

    /// <summary>
    /// The cap piece that lies on a slope along its joints: a strip on top of the slope beside each joint,
    /// running to where the strips on the slopes either side meet over the joint, so the two make a tight
    /// ridge (the one that runs <see cref="Seam.Over"/> covers the other's edge, as the slopes do without
    /// caps). Each strip has the joints between ridge tiles etched across it.
    /// </summary>
    internal static RoofPart Caps(RoofSpec s, string name, PathD slope, IReadOnlyList<Seam> seams, double T, List<Pose> slopePoses)
    {
        int n = slope.Count;
        double w = CapStrip(T), big = 1e4;
        var mid = new PointD(slope.Average(p => p.x), slope.Average(p => p.y));
        // The edge from point i to the next: its direction and its outward normal, or null if it has no length.
        (PointD A, PointD D, PointD Out)? EdgeOf(int i)
        {
            var a = slope[i];
            var b = slope[(i + 1) % n];
            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6 && n >= 4)
            {
                // An edge with no length (a hipped roof's ridge, when it comes to a point): it runs the way
                // the edge across from it does, back the other way.
                var p0 = slope[(i + n - 1) % n];
                var p1 = slope[(i + 2) % n];
                dx = p1.x - p0.x; dy = p1.y - p0.y; len = Math.Sqrt(dx * dx + dy * dy);
            }
            if (len < 1e-6) return null;
            var d = new PointD(dx / len, dy / len);
            var o = new PointD(d.y, -d.x);
            if ((mid.x - a.x) * o.x + (mid.y - a.y) * o.y > 0) o = new PointD(-o.x, -o.y);
            return (a, d, o);
        }
        // Everything on the inside of edge i, moved out by shift.
        PathsD Inside(int i, double shift)
        {
            if (EdgeOf(i) is not { } e) return [Geo.Rect(-big, -big, big, big)];
            var a = new PointD(e.A.x + e.Out.x * shift, e.A.y + e.Out.y * shift);
            PointD At(double along, double inward) => new(a.x + e.D.x * along - e.Out.x * inward, a.y + e.D.y * along - e.Out.y * inward);
            return [[At(-big, 0), At(big, 0), At(big, big), At(-big, big)]];
        }
        // How far each joint's strip reaches past the slope's edge: to where the faces on top of the two
        // slopes meet, then on over the other's edge or back off it.
        // Up to a right angle the one underneath stops where its top meets the other's underside, and the
        // other runs on to cover its end; steeper, the one underneath stops at the joint (its end then stays
        // clear of the other) and the other runs on over it.
        double Reach(Seam j)
        {
            double phi = Math.Clamp(j.Phi, 0.05, Math.PI - 0.05), meet = T * Math.Tan(phi / 2);
            if (phi <= Math.PI / 2) return meet + (j.Over ? T / Math.Sin(phi) : -T / Math.Tan(phi));
            return meet + (j.Over ? T * Math.Sin(phi) : 0);
        }
        var shift = new double[n];
        foreach (var j in seams) shift[j.Edge] = Reach(j);
        PathsD outer = [Geo.Rect(-big, -big, big, big)], inner = [Geo.Rect(-big, -big, big, big)];
        for (int i = 0; i < n; i++)
        {
            bool seam = seams.Any(j => j.Edge == i);
            outer = Geo.Intersect(outer, Inside(i, shift[i]));
            inner = Geo.Intersect(inner, Inside(i, seam ? shift[i] - w : 0));
        }
        var cut = Geo.Tidy(Geo.Subtract(outer, inner));
        // The joints between ridge tiles, across each strip.
        var marks = new PathsD();
        var lines = new List<PathD>();
        double g = Math.Clamp(s.ShingleGrooveMm, 0.3, 1);
        foreach (var j in seams)
        {
            if (EdgeOf(j.Edge) is not { } e) continue;
            var a = new PointD(e.A.x + e.Out.x * shift[j.Edge], e.A.y + e.Out.y * shift[j.Edge]);
            var b0 = slope[(j.Edge + 1) % n];
            double len = (b0.x - e.A.x) * e.D.x + (b0.y - e.A.y) * e.D.y;
            var strip = Geo.Intersect(cut, Geo.Subtract(Inside(j.Edge, shift[j.Edge]), Inside(j.Edge, shift[j.Edge] - w)));
            for (double x = 7; x < len - 2; x += 7)
            {
                PointD P(double along, double inward) => new(a.x + e.D.x * along - e.Out.x * inward, a.y + e.D.y * along - e.Out.y * inward);
                if (s.Etch == EtchStyle.Grooves) marks.AddRange(Geo.Intersect([[P(x - g / 2, -1), P(x + g / 2, -1), P(x + g / 2, w + 1), P(x - g / 2, w + 1)]], strip));
                else lines.AddRange(Geo.ClipLines([[P(x, -1), P(x, w + 1)]], strip));
            }
        }
        PathsD etched = s.Shingle != Shingles.Plain && s.Etch == EtchStyle.Grooves ? Geo.Union(marks) : [];
        if (s.Shingle == Shingles.Plain) lines.Clear();
        var poses = slopePoses.Select(p => p.Moved(p.N * T)).ToList();
        return new RoofPart(name, cut, etched, lines, poses.Count, poses);
    }

    /// <summary>A strip over a ridge or hip, with the joints between ridge tiles etched across it.</summary>
    internal static RoofPart Cap(RoofSpec s, string name, double length, double width, Pose pose)
    {
        var cut = new PathsD { Geo.Rect(0, 0, length, width) };
        var marks = new PathsD();
        double g = Math.Clamp(s.ShingleGrooveMm, 0.3, 1);
        for (double x = 7; x < length - 2; x += 7) marks.Add(Geo.Rect(x - g / 2, -1, x + g / 2, width + 1));
        PathsD etched = [];
        var lines = new List<PathD>();
        if (s.Etch == EtchStyle.Grooves && s.Shingle != Shingles.Plain) etched = Geo.Intersect(Geo.Union(marks), cut);
        else if (s.Shingle != Shingles.Plain) lines.AddRange(Geo.ClipLines(marks.Select(m => new PathD { new((m[0].x + m[1].x) / 2, -1), new((m[0].x + m[1].x) / 2, width + 1) }), cut));
        return new RoofPart(name, cut, etched, lines, 1, [pose]);
    }
}
