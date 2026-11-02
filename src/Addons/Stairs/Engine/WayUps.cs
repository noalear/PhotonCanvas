using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The size of a way up before it's placed: how wide it is, the opening it needs and how far it
/// reaches back from the opening's far edge (on the plan, mm).
/// </summary>
public sealed record WayPlan(WayUp Kind, string Name, double Width, double HatchLength, double HatchWidth, double Behind, int Steps, double Rise, int LayersPerStep)
{
    /// <summary>Its footprint on the plan once placed (see <see cref="WayUps.Make"/> for what far, near and the centre are).</summary>
    public PathsD Footprint(double far, double near, double cx, double cy) => Kind switch
    {
        WayUp.Spiral => [Geo.Circle(cx, cy, HatchLength / 2 - WayUps.SpiralClearMm)],
        WayUp.Stairs or WayUp.SolidStairs => [Geo.Rect(far - Behind, near + WayUps.SideClearMm, far + FloorKitMaker.TuckMm, near + WayUps.SideClearMm + Width)],
        _ => [Geo.Rect(far - Behind, near + WayUps.SideClearMm, far, near + WayUps.SideClearMm + Width)],
    };
}

/// <summary>
/// Stairs and ladders. Open stairs: two side pieces (stringers) with a slot for each step, the steps'
/// tabs pushed through them. Solid stairs: a stack of layers, each one step shorter than the one
/// under it. Spiral stairs: wedge-shaped steps and round spacers threaded on a pin. A leaning ladder
/// is one piece; a hooked ladder has side rails with a hook over the floor's edge and rungs through them.
/// </summary>
public static class WayUps
{
    /// <summary>Room between a way up and the side of its opening (mm).</summary>
    public const double SideClearMm = 0.5;
    /// <summary>Room round spiral stairs in their round opening (mm).</summary>
    public const double SpiralClearMm = 1.5;
    /// <summary>Material left round the slots in open stairs' stringers (mm).</summary>
    public const double StringerMarginMm = 2;
    /// <summary>A handrail's height above the steps (mm): waist height for a 28 mm figure.</summary>
    public const double RailHeightMm = 15;
    /// <summary>The pin that lines solid stairs' layers up (mm): a cocktail stick.</summary>
    public const double StackPinMm = 2;
    /// <summary>A leaning ladder's rails and rungs (mm, across them).</summary>
    public const double FlatRailMm = 2, FlatRungMm = 1.6;
    /// <summary>A hooked ladder's rungs: their height where they go through the rails, and the shoulder above and below that stops them sliding.</summary>
    public const double RungMm = 1.6, RungShoulderMm = 0.6;
    /// <summary>How far a hooked ladder's hook reaches over the floor, and how thick it is (mm).</summary>
    public const double HookMm = 5, HookThickMm = 2.5;

    static double Going(FloorSpec s) => Math.Clamp(s.GoingMm, 3, 60);
    static double StairW(FloorSpec s) => Math.Clamp(s.StairWidthMm, 5, 300);
    static double LadderW(FloorSpec s) => Math.Clamp(s.LadderWidthMm, 4, 100);
    static double Angle(FloorSpec s) => Math.Clamp(s.LadderAngleDeg, 55, 85) * Math.PI / 180;
    static double HookedRail(KitBuilder k) => Math.Max(5, k.T + k.Fit + 2.4);

    internal static WayPlan Plan(KitBuilder k, double climb)
    {
        var s = k.Spec;
        double T = k.T, G = Going(s), r0 = Math.Clamp(s.RiseMm, 1, 100);
        switch (s.Way)
        {
            case WayUp.Stairs:
            {
                int n = Math.Max(2, (int)Math.Round(climb / r0));
                double run = (n - 1) * G, m = StringerMarginMm, w = StairW(s) + 2 * T;
                return new(s.Way, "stairs", w, run + m + 2 * SideClearMm, w + 2 * SideClearMm, run + m, n, climb / n, 1);
            }
            case WayUp.SolidStairs:
            {
                int per = Math.Max(1, (int)Math.Round(r0 / T));
                int n = Math.Max(2, (int)Math.Round(climb / (per * T)));
                double run = (n - 1) * G, w = StairW(s);
                return new(s.Way, "stairs", w, run + 2 * SideClearMm, w + 2 * SideClearMm, run, n, per * T, per);
            }
            case WayUp.Spiral:
            {
                int per = Math.Max(1, (int)Math.Round(r0 / T));
                int n = Math.Max(2, (int)Math.Round(climb / (per * T)));
                double d = 2 * (Math.Clamp(s.SpiralRadiusMm, 10, 200) + SpiralClearMm);
                return new(s.Way, "spiral stairs", d, d, d, d, n, per * T, per);
            }
            case WayUp.Ladder:
            {
                double a = Angle(s), w = LadderW(s) + 2 * FlatRailMm;
                double back = Math.Cos(a) * climb / Math.Sin(a) + Math.Sin(a) * T + 0.5;
                return new(s.Way, "ladder", w, 14, w + 2 * SideClearMm + 2, back, 0, 0, 0);
            }
            case WayUp.HookedLadder:
            {
                double d = HookedRail(k), w = LadderW(s) + 2 * T;
                return new(s.Way, "ladder", w, d + 9, w + 2 * SideClearMm + 2, d, 0, 0, 0);
            }
            default: return new(s.Way, "", 0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Makes the way up. <paramref name="far"/> is the plan x where it reaches the floor (the opening's far
    /// edge: the floor goes on past it), <paramref name="near"/> the plan y of the opening's near side; spiral
    /// stairs stand on <paramref name="cx"/>, <paramref name="cy"/>. <paramref name="underPlate"/>: a plate
    /// lies past <paramref name="far"/> at the climb's height, so stairs can tuck under it.
    /// </summary>
    internal static (int Steps, double Rise) Make(KitBuilder k, WayPlan plan, double climb, bool underPlate, double far, double near, double cx, double cy)
    {
        switch (plan.Kind)
        {
            case WayUp.Stairs: return Open(k, plan, climb, underPlate, far, near + SideClearMm);
            case WayUp.SolidStairs: return Solid(k, plan, climb, underPlate, far, near + SideClearMm);
            case WayUp.Spiral: return Spiral(k, plan, climb, cx, cy);
            case WayUp.Ladder: Leaning(k, plan, climb, far, near + SideClearMm + 1); return (0, 0);
            case WayUp.HookedLadder: Hooked(k, plan, climb, underPlate, far, near + SideClearMm + 1); return (0, 0);
            default: return (0, 0);
        }
    }

    static int Ways(KitBuilder k) => Math.Max(1, k.Spec.WaysUp);

    // ---- open stairs ----

    /// <summary>
    /// Two stringers (the sides) with a slot for each step; each step is a board whose tabs go through
    /// the slots, resting on their bottoms. Seen side-on, step k's top is k·rise up and it spans
    /// (k−1)·going to k·going from the bottom; the last rise is up onto the floor.
    /// </summary>
    static (int, double) Open(KitBuilder k, WayPlan plan, double climb, bool underPlate, double far, double y0)
    {
        var s = k.Spec;
        double T = k.T, f = k.Fit, G = Going(s), W = StairW(s), m = StringerMarginMm;
        int n = plan.Steps;
        double R = climb / n, run = (n - 1) * G, x0 = far - run;
        double a = Math.Min(1, G / 5);   // shoulders: each step's tab is narrower than the step, so the step sits between the stringers
        if (R < T + f + 0.6) { k.Bad($"The steps are too low for the sheet: make each step at least {T + f + 0.6:0.#} mm high."); return (n, R); }
        if (G - 2 * a < 2) { k.Bad("The steps are too shallow: make each step at least 4 mm deep."); return (n, R); }
        double plateBottom = climb - T;

        // The stringer, side-on: x from the bottom step's front (0), y up from the ground.
        var pts = new List<PointD> { new(-m, 0), new(G + m, 0) };
        var mortises = new PathsD();
        for (int i = 1; i < n; i++)
        {
            double xa = (i - 1) * G, xb = i * G, top = i * R;
            pts.AddRange([new(xa - m, top - T - m), new(xb + m, top - T - m), new(xb + m, top + m), new(xa - m, top + m)]);
            mortises.Add(Geo.Rect(xa + a - f / 2, top - T - f / 2, xb - a + f / 2, top + f / 2));
        }
        if (underPlate)
        {
            double y = plateBottom - TuckClear;
            pts.AddRange([new(run, y), new(run + FloorKitMaker.TuckMm, y), new(run + FloorKitMaker.TuckMm, Math.Max(0, y - 4))]);
        }
        var hull = Hull(pts);
        var stringer = Geo.Intersect([hull], [Geo.Rect(-m - 1, 0, run + FloorKitMaker.TuckMm + 1, climb + RailHeightMm + 5)]);
        if (underPlate) stringer = Geo.Subtract(stringer, [Geo.Rect(run, plateBottom - TuckClear, run + 50, climb + 50)]);
        else stringer = Geo.Subtract(stringer, [Geo.Rect(run + m, -1, run + 50, climb + 50)]);
        var railed = Geo.Union(stringer, Rail(n, R, G));

        // Left and right as you go up (+x): left is the far side (bigger y).
        bool railLeft = s.Rail is Railing.Left or Railing.Both, railRight = s.Rail is Railing.Right or Railing.Both;
        double yNear = y0 + T / 2, yFar = y0 + T + W + T / 2;
        int ways = Ways(k);
        if (railLeft == railRight)
            k.Add(railLeft ? "Stringer with handrail" : "Stringer", railLeft ? railed : stringer, mortises, 2 * ways, [Pose.Upright(x0, yNear, 0), Pose.Upright(x0, yFar, 0)]);
        else
        {
            k.Add("Stringer", stringer, mortises, ways, [Pose.Upright(x0, railLeft ? yNear : yFar, 0)]);
            k.Add("Stringer with handrail", railed, mortises, ways, [Pose.Upright(x0, railLeft ? yFar : yNear, 0)]);
        }

        // A step, flat: u across the stairs (tabs at both ends), v from its back edge to its front.
        var tread = new PathsD { Geo.Rect(T, 0, T + W, G), Geo.Rect(0, a, T, G - a), Geo.Rect(T + W, a, W + 2 * T, G - a) };
        var (te, tl) = s.TreadTexture ? k.Texture([Geo.Rect(T, 0, T + W, G)], false, 3) : ([], []);
        var poses = new List<Pose>();
        for (int i = 1; i < n; i++)
            poses.Add(new Pose(V3.Room(x0 + i * G, y0, i * R - f / 2 - T / 2), -V3.UnitZ, -V3.UnitX));
        k.Add("Step", tread, [], (n - 1) * ways, poses, te, tl);

        k.Note($"{n - 1} steps {R:0.#} mm high and {G:0.#} mm deep{(underPlate ? ", then up onto the floor" : "")}: the stairs run {run + 2 * m:0} mm{(underPlate ? $" and tuck {FloorKitMaker.TuckMm:0} mm under the floor past the opening" : "")}.");
        if (R > 2 * G) k.Warn("The steps are much higher than they are deep: steep, like a ship's ladder. Make them deeper or lower for a gentler stair.");
        return (n, R);
    }

    const double TuckClear = FloorKitMaker.TuckClearMm;

    /// <summary>A handrail on a stringer: a post on each step and a rail along their tops.</summary>
    static PathsD Rail(int n, double R, double G)
    {
        const double post = 1.4, rail = 1.6;
        var parts = new PathsD();
        var tops = new List<PointD>();
        for (int i = 1; i < n; i++)
        {
            double x = (i - 0.5) * G, y = i * R;
            parts.Add(Geo.Rect(x - post / 2, y, x + post / 2, y + RailHeightMm));
            tops.Add(new(x, y + RailHeightMm - rail / 2));
        }
        if (tops.Count >= 2) parts.Add(Textures.Strip(tops[0], tops[^1], rail));
        else if (tops.Count == 1) parts.Add(Geo.Rect(tops[0].x - G / 2, tops[0].y - rail / 2, tops[0].x + G / 2, tops[0].y + rail / 2));
        return Geo.Union(parts);
    }

    /// <summary>The convex hull of points (anticlockwise).</summary>
    static PathD Hull(List<PointD> pts)
    {
        var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList();
        if (p.Count < 3) return [.. p];
        static double Cross(PointD o, PointD a, PointD b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var h = new List<PointD>();
        foreach (var q in p) { while (h.Count >= 2 && Cross(h[^2], h[^1], q) <= 1e-12) h.RemoveAt(h.Count - 1); h.Add(q); }
        int lower = h.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--) { var q = p[i]; while (h.Count >= lower && Cross(h[^2], h[^1], q) <= 1e-12) h.RemoveAt(h.Count - 1); h.Add(q); }
        h.RemoveAt(h.Count - 1);
        return [.. h];
    }

    // ---- solid stairs ----

    /// <summary>
    /// A stack of layers, each as wide as the stairs, from the front of its step to the top; every
    /// step is one or more layers. The top step's region has two holes for pins that line the stack
    /// up (not through the top layer, so they don't show). Layers that are lower than the plate
    /// reach under it, so the stairs can't slide out.
    /// </summary>
    static (int, double) Solid(KitBuilder k, WayPlan plan, double climb, bool underPlate, double far, double y0)
    {
        var s = k.Spec;
        double T = k.T, G = Going(s), W = StairW(s);
        int n = plan.Steps, per = plan.LayersPerStep;
        double R = plan.Rise, run = (n - 1) * G, x0 = far - run, last = climb - (n - 1) * R;
        int layers = (n - 1) * per;
        double plateBottom = climb - T;
        bool pins = G >= StackPinMm + 3 && W >= 8;
        var groups = new Dictionary<string, (PathsD Outline, PathsD Holes, PathsD Etched, List<PathD> Lines, List<Pose> Poses, string Name)>();
        for (int j = 0; j < layers; j++)
        {
            int step = j / per + 1;
            bool topOfStep = j % per == per - 1, topLayer = j == layers - 1;
            bool tuck = underPlate && (j + 1) * T <= plateBottom - TuckClear + 1e-9;
            double start = (step - 1) * G, len = run - start + (tuck ? FloorKitMaker.TuckMm : 0);
            string key = $"{step}|{topOfStep}|{topLayer}|{tuck}";
            if (!groups.TryGetValue(key, out var g))
            {
                var holes = new PathsD();
                if (pins && !topLayer)
                {
                    double px = run - G / 2 - start;
                    holes.Add(Geo.Circle(px, W / 4, (StackPinMm + 0.1) / 2));
                    holes.Add(Geo.Circle(px, 3 * W / 4, (StackPinMm + 0.1) / 2));
                }
                var (e, l) = topOfStep && s.TreadTexture ? k.Texture([Geo.Rect(0, 0, G, W)], true, step) : ([], []);
                string name = topLayer ? "Top step" : topOfStep ? $"Step {step}" : $"Step {step} layer";
                g = ([Geo.Rect(0, 0, len, W)], holes, e, l, [], name);
                groups[key] = g;
            }
            g.Poses.Add(Pose.Flat(x0 + start, y0, j * T + T / 2));
        }
        foreach (var g in groups.Values)
            k.Add(g.Name, g.Outline, g.Holes, g.Poses.Count * Ways(k), g.Poses, g.Etched, g.Lines);
        k.Note($"{n - 1} steps of {per} layer{(per == 1 ? "" : "s")} ({R:0.#} mm) each, {G:0.#} mm deep: {layers} layers in all.");
        if (Math.Abs(last - R) > 0.5) k.Note($"The last step up onto the floor is {last:0.#} mm high (the others {R:0.#} mm): steps are whole layers of the sheet.");
        if (pins) k.Note($"Line the layers up on two {StackPinMm:0} mm pins (cocktail sticks) through the top step's holes, and glue them.");
        if (last < T * 0.5 || last > 2.5 * R) k.Warn($"The last step is {last:0.#} mm: change the step height for an even climb.");
        return (n, R);
    }

    // ---- spiral stairs ----

    /// <summary>
    /// Wedge-shaped steps round a hub, with round spacers between them, threaded on a pin (a bamboo
    /// skewer) standing on a wider base disc; each step turned one step's angle on from the one under it.
    /// </summary>
    static (int, double) Spiral(KitBuilder k, WayPlan plan, double climb, double cx, double cy)
    {
        var s = k.Spec;
        double T = k.T, rad = Math.Clamp(s.SpiralRadiusMm, 10, 200), step = Math.Clamp(s.SpiralStepDeg, 10, 90);
        double pin = Math.Clamp(s.SpiralPinMm, 1, 10), hub = Math.Clamp(s.SpiralHubMm, pin + 3, rad), pinHole = pin + 0.1;
        int n = plan.Steps, per = plan.LayersPerStep;
        double R = plan.Rise, last = climb - (n - 1) * R;
        double half = step / 2 * Math.PI / 180;
        var wedge = new PathD { new(0, 0) };
        wedge.AddRange(Geo.Arc(0, 0, rad, -half, half));
        var holes = new PathsD { Geo.Circle(0, 0, pinHole / 2) };
        var (te, tl) = s.TreadTexture ? k.Texture([wedge], false, 5) : ([], []);
        int dir = s.Clockwise ? -1 : 1;
        var treads = new List<Pose>();
        var spacers = new List<Pose>();
        Pose? baseDisc = null;
        for (int j = 0; j < (n - 1) * per; j++)
        {
            double h = j * T + T / 2;
            if (j % per == per - 1)
            {
                int i = j / per;   // 0 for the first step
                treads.Add(Pose.FlatTurned(cx, cy, h, dir * i * step));
            }
            else if (j == 0) baseDisc = Pose.Flat(cx, cy, h);
            else spacers.Add(Pose.Flat(cx, cy, h));
        }
        int ways = Ways(k);
        k.Add("Spiral step", [Geo.Circle(0, 0, hub / 2), wedge], holes, treads.Count * ways, treads, te, tl);
        if (spacers.Count > 0) k.Add("Spacer", [Geo.Circle(0, 0, hub / 2)], holes, spacers.Count * ways, spacers);
        if (baseDisc is not null) k.Add("Base", [Geo.Circle(0, 0, hub / 2 + 5)], holes, ways, [baseDisc]);
        // The pin, shown (not cut): from the ground to the top step.
        k.Ghosts.Add(new Ghost("Pin", [Geo.Circle(0, 0, pin / 2)], Pose.Flat(cx, cy, (n - 1) * R / 2), (n - 1) * R));
        double turn = (n - 1) * step;
        k.Note($"{n - 1} steps {R:0.#} mm apart, each turned {step:0.#}° on from the one under it ({turn:0}° in all, {(s.Clockwise ? "clockwise" : "anticlockwise")} going up). Thread them{(spacers.Count > 0 ? " and the spacers" : "")} on a {pin:0.#} mm pin (a bamboo skewer) {(n - 1) * R:0} mm long, lining each step's back edge up with the front edge of the one under it.");
        double headroom = R * (360 / step);
        if (turn >= 360 && headroom < 32) k.Warn($"Only {headroom:0} mm between a step and the one a full turn above it: a 28 mm figure won't fit under. Turn each step less, or make the steps higher.");
        if (Math.Abs(last - R) > 0.5) k.Note($"The last step up onto the floor is {last:0.#} mm high (the others {R:0.#} mm).");
        if (Math.Sin(half) * rad * 2 < 6) k.Warn("The steps are very narrow at their outer end: turn each step further or make the stairs wider.");
        return (n, R);
    }

    // ---- ladders ----

    /// <summary>
    /// A leaning ladder in one piece: two rails and the rungs between them. Its underside rests on the
    /// top of the opening's far edge; the rails go on up past the floor for figures to hold.
    /// </summary>
    static void Leaning(KitBuilder k, WayPlan plan, double climb, double far, double y0)
    {
        var s = k.Spec;
        double T = k.T, a = Angle(s), sin = Math.Sin(a), cos = Math.Cos(a), W = LadderW(s), rail = FlatRailMm;
        double vc = climb / sin, ext = s.Floor ? 6 : 4, len = vc + ext, width = W + 2 * rail;
        double spacing = Math.Clamp(s.RungStepMm, 3, 30) / sin;
        var parts = new PathsD { Geo.Rect(0, 0, rail, len), Geo.Rect(rail + W, 0, width, len) };
        int rungs = 0;
        for (double v = spacing; v <= vc - 2; v += spacing, rungs++)
            parts.Add(Geo.Rect(rail - 0.01, v - FlatRungMm / 2, rail + W + 0.01, v + FlatRungMm / 2));
        // u runs along −y from the ladder's far side, v up the slope; its face looks up and back, at the climber.
        var V = new V3(cos, sin, 0);
        var pose = new Pose(new V3(far - cos * vc - sin * T / 2, cos * T / 2, -(y0 + width)), V3.UnitZ, V);
        k.Add("Ladder", parts, [], Ways(k), [pose]);
        k.Note($"A {len:0} mm ladder with {rungs} rungs: lean it at {a * 180 / Math.PI:0}° with its underside on the {(s.Floor && s.Hatch ? "opening's far edge" : s.Floor ? "floor's edge" : "edge it climbs to")}; its foot stands {cos * vc:0} mm out.");
    }

    /// <summary>
    /// A ladder standing straight up against the opening's far edge: two rails, side-on, whose hooks
    /// lie on the floor; rungs through slots in them (each rung's shoulders stop it sliding sideways).
    /// </summary>
    static void Hooked(KitBuilder k, WayPlan plan, double climb, bool underPlate, double far, double y0)
    {
        var s = k.Spec;
        double T = k.T, f = k.Fit, d = HookedRail(k), W = LadderW(s);
        double wallT = FloorKitMaker.WallThickness(s);
        // The rail, side-on: u from its back (x = far − d) to its front against the edge (u = d), v up.
        var rail = new PathsD
        {
            Geo.Rect(0, 0, d, climb + 0.1 + HookThickMm),
            Geo.Rect(0, climb + 0.1, d + (s.Floor ? HookMm : wallT + f + 2), climb + 0.1 + HookThickMm),
        };
        // With no floor it hooks over a wall's top: a finger down the far side.
        if (!s.Floor) rail.Add(Geo.Rect(d + wallT + f, climb - 4, d + wallT + f + 2, climb + 0.1 + HookThickMm));
        var slots = new PathsD();
        var rungPoses = new List<Pose>();
        double step = Math.Clamp(s.RungStepMm, 3, 30);
        for (double h = step; h <= climb - 2.5; h += step)
        {
            slots.Add(Geo.Rect(d / 2 - (T + f) / 2, h - (RungMm + f) / 2, d / 2 + (T + f) / 2, h + (RungMm + f) / 2));
            rungPoses.Add(new Pose(V3.Room(far - d / 2, y0, h), -V3.UnitZ, V3.UnitY));
        }
        int ways = Ways(k);
        k.Add("Ladder rail", rail, slots, 2 * ways, [Pose.Upright(far - d, y0 + T / 2, 0), Pose.Upright(far - d, y0 + T + W + T / 2, 0)]);
        double body = RungMm / 2 + RungShoulderMm;
        var rung = new PathsD { Geo.Rect(T, -body, T + W, body), Geo.Rect(0, -RungMm / 2, T, RungMm / 2), Geo.Rect(T + W, -RungMm / 2, W + 2 * T, RungMm / 2) };
        if (rungPoses.Count == 0) { k.Bad("The ladder is too short for a rung: make it taller or the rungs closer."); return; }
        k.Add("Rung", rung, [], rungPoses.Count * ways, rungPoses);
        k.Note($"Push the {rungPoses.Count} rungs through both rails; the hooks at the top {(s.Floor ? "lie on the floor at the opening's edge" : "hook over the wall's top")}.");
    }
}
