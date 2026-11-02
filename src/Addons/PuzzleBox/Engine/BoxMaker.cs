using Clipper2Lib;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>A box worked out: its bodies layer by layer, and what was found while making it.</summary>
public sealed class BoxDesign
{
    public required BoxSpec Spec { get; init; }
    public List<Body> Bodies { get; } = [];
    public List<Finding> Findings { get; } = [];
    /// <summary>Layers of sheet, bottom (0) to top.</summary>
    public int Layers { get; set; }
    public double T { get; init; }
    public double Fit { get; init; }
    /// <summary>The bodies over the top (the lid, or every panel): the box is open when they're all out.</summary>
    public List<int> TopBodies { get; } = [];
    public int? DrawerBody { get; set; }
    /// <summary>How far the drawer has to come out to count as open (mm).</summary>
    public double DrawerOpensMm { get; set; }
    /// <summary>The space inside the box and inside the drawer (width, depth, height), zero if none.</summary>
    public (double W, double D, double H) Inside { get; set; }
    public (double W, double D, double H) DrawerInside { get; set; }
    /// <summary>Filled in by <see cref="BoxMaker.Make"/> when the box can be built.</summary>
    public Solution? Solution { get; set; }
    /// <summary>How the sliding panels are hooked together, front boundary first (empty: no panels, or one).</summary>
    public List<BoxMaker.Hook> Hooks { get; set; } = [];
    /// <summary>Hooks to use instead of picking them (while trying layouts).</summary>
    internal List<BoxMaker.Hook>? ForcedHooks { get; set; }

    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public double HeightMm => Layers * T;

    public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
    public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
    public void Note(string text) => Findings.Add(new(Severity.Note, text));

    public int Add(Body b)
    {
        Bodies.Add(b);
        return Bodies.Count - 1;
    }
}

/// <summary>
/// Builds a puzzle box out of layers of sheet. The box (frame) is every layer that doesn't move, glued
/// in a stack: a bottom, the drawer's layers (open at the front), a floor, rings round the space inside,
/// and the top. The drawer is cut from the same layers, so its front looks like the wall. The sliding
/// panels run in a groove under the top layer and are hooked to each other underneath; the button
/// on the floor carries a bolt that hangs down into the drawer.
/// </summary>
/// <remarks>
/// Plan coordinates: x left to right (0 to the width), y from the front face (0) to the back.
/// Every body is placed as it sits when the box is shut; the solver moves them from there.
/// </remarks>
public static class BoxMaker
{
    /// <summary>The narrowest strip a piece may have (mm).</summary>
    public const double Bridge = 1.5;

    /// <summary>The sizes everything is worked out from.</summary>
    public sealed record Sizes(double T, double G, double W, double D, double Wall, int N,
                               double Shift, double Tongue, double Stop, double HookW, double HookNeck, double HookFoot, double HookToe, double DrawerWall);

    public static Sizes Size(BoxSpec s)
    {
        double T = Math.Clamp(s.ThicknessMm, 0.5, 12), g = Math.Clamp(s.FitMm, 0.02, 1.5);
        double shift = Math.Clamp(T, 2.5, 5);
        double stop = shift + 2 * g;
        double tongue = shift + Math.Max(4, T);
        double hw = Math.Max(4, 1.5 * T), neck = Math.Max(2.5, T), toe = Math.Max(3, T);
        double foot = 2 * stop + Math.Max(2, 0.7 * T);
        double dw = Math.Clamp(s.WallMm * 0.5, Math.Max(3, T), Math.Max(3, s.WallMm));
        int n = Math.Max(1, (int)Math.Round(s.HeightMm / T));
        return new(T, g, s.WidthMm, s.DepthMm, s.WallMm, n, shift, tongue, stop, hw, neck, foot, toe, dw);
    }

    /// <summary>The fewest layers the box needs, given the drawer and the top.</summary>
    public static int MinLayers(BoxSpec s, Sizes z)
    {
        int nd = DrawerLayers(s, z);
        int compStart = s.Drawer ? nd + 2 : 1;
        int rings = s.Drawer && s.Bolt && s.Top != TopKind.Closed ? 2 : 1;
        return compStart + rings + (s.Top == TopKind.Closed ? 1 : 2);
    }

    public static int DrawerLayers(BoxSpec s, Sizes z) =>
        !s.Drawer ? 0 : Math.Max(s.Bolt && s.Top != TopKind.Closed ? 3 : 2, (int)Math.Round(s.DrawerHeightMm / z.T));

    public static BoxDesign Make(BoxSpec s, bool solve = true)
    {
        var z = Size(s);
        var d = new BoxDesign { Spec = s, T = z.T, Fit = z.G };
        double T = z.T, g = z.G, W = z.W, D = z.D, Wt = z.Wall;
        d.Layers = z.N;
        if (W <= 0 || D <= 0 || s.HeightMm <= 0) { d.Bad("Give it a size: width, depth and height must be more than 0."); return d; }
        if (Wt < Math.Max(2 * T, 5)) { d.Bad($"The walls are too thin to glue up from {T:0.##} mm sheet: make them at least {Math.Max(2 * T, 5):0.#} mm."); return d; }
        if (s.Top == TopKind.Closed && !s.Drawer) { d.Bad("A closed box with no drawer has no way in: give it a lid, sliding panels or a drawer."); return d; }
        bool bolt = s.Drawer && s.Bolt && s.Top != TopKind.Closed;
        if (s.Top == TopKind.SlidingPanels)
        {
            double need = z.Tongue + g + Math.Max(3, T);
            if (Wt < need) { d.Bad($"The sliding panels' runners need walls at least {need:0.#} mm thick on this sheet: make the walls thicker."); return d; }
        }
        double minInside = Math.Max(20, 4 * T);
        if (W - 2 * Wt < minInside || D - 2 * Wt < minInside)
        {
            d.Bad($"The box is too small for its walls: make it at least {2 * Wt + minInside:0} mm wide and deep, or the walls thinner.");
            return d;
        }
        int need0 = MinLayers(s, z);
        if (z.N < need0) { d.Bad($"The box needs at least {need0} layers ({need0 * T:0.#} mm) for this top{(s.Drawer ? " and drawer" : "")}: make it taller{(s.Drawer ? " or the drawer lower" : "")}."); return d; }

        int N = z.N;
        int nd = DrawerLayers(s, z);
        int floor = s.Drawer ? nd + 1 : 0;
        int compStart = s.Drawer ? nd + 2 : 1;
        int ringTop = s.Top == TopKind.SlidingPanels ? N - 3 : N - 2;

        var frame = new Body { Name = "Box", Role = BodyRole.Frame };
        d.Add(frame);
        Box2[] Ring(double inset) => [new(0, 0, W, inset), new(0, D - inset, W, D), new(0, 0, inset, D), new(W - inset, 0, W, D)];
        var full = new Box2(0, 0, W, D);

        // ---- the bottom ----
        frame.Add(0, full);
        frame.PieceNames[0] = "Bottom";

        // ---- the drawer and its layers (the box's open at the front) ----
        double dx0 = Wt + g, dx1 = W - Wt - g, dy1 = D - Wt - g, dw = z.DrawerWall;
        int drawer = -1;
        if (s.Drawer)
        {
            if (dx1 - dx0 - 2 * dw < Math.Max(15, 3 * T) || dy1 - 2 * dw < Math.Max(15, 3 * T)) { d.Bad("The drawer is too small inside: make the box bigger or the walls thinner."); return d; }
            for (int l = 1; l <= nd; l++)
            {
                frame.Add(l, new Box2(0, 0, Wt, D), new Box2(W - Wt, 0, W, D), new Box2(Wt, D - Wt, W - Wt, D));
                frame.PieceNames[l] = "Drawer surround";
            }
            var dr = new Body { Name = "Drawer", Role = BodyRole.Drawer };
            dr.Add(1, new Box2(dx0, 0, dx1, dy1));
            dr.PieceNames[1] = "Drawer bottom";
            for (int l = 2; l <= nd; l++)
            {
                dr.Add(l, new Box2(dx0, 0, dx1, dw), new Box2(dx0, dy1 - dw, dx1, dy1), new Box2(dx0, 0, dx0 + dw, dy1), new Box2(dx1 - dw, 0, dx1, dy1));
                dr.PieceNames[l] = "Drawer side";
            }
            if (s.FingerNotch)
            {
                double rn = Math.Clamp(Math.Min((dx1 - dx0) / 5, 11), 5, Math.Max(5, D / 4));
                frame.Cut(0, Geo.Circle(W / 2, 0, rn, 0.02));
            }
            drawer = d.Add(dr);
            d.DrawerBody = drawer;
            d.DrawerInside = (dx1 - dx0 - 2 * dw, dy1 - 2 * dw, (nd - 1) * T);
            d.DrawerOpensMm = Math.Max(10, (dy1 - 2 * dw) * 0.6);
            // The floor between the drawer and the space inside the box.
            frame.Add(floor, full);
            frame.PieceNames[floor] = "Floor";
        }

        // ---- rings round the space inside ----
        for (int l = compStart; l <= ringTop; l++)
        {
            frame.Add(l, Ring(Wt));
            frame.PieceNames[l] = "Wall";
        }

        // ---- the top ----
        switch (s.Top)
        {
            case TopKind.LiftLid:
                {
                    var lid = new Body { Name = "Lid", Role = BodyRole.Lid };
                    lid.Add(N - 1, full);
                    lid.PieceNames[N - 1] = "Lid";
                    double lw = Math.Clamp(Math.Min(W, D) / 10, Math.Max(4, 1.5 * T), Math.Max(4, (Math.Min(W, D) - 2 * Wt) / 3));
                    double a0 = Wt + g, b0 = Wt + g, a1 = W - Wt - g, b1 = D - Wt - g;
                    lid.Add(N - 2, new Box2(a0, b0, a1, b0 + lw), new Box2(a0, b1 - lw, a1, b1), new Box2(a0, b0, a0 + lw, b1), new Box2(a1 - lw, b0, a1, b1));
                    lid.PieceNames[N - 2] = "Lid lip";
                    // A finger notch in the top ring's front, under the lid's edge, to lift it.
                    double rn = Math.Min(9, Wt - Bridge - 0.5);
                    if (rn >= 4) frame.Cut(N - 2, Geo.Circle(W / 2, 0, rn, 0.02));
                    d.TopBodies.Add(d.Add(lid));
                    d.Inside = (W - 2 * Wt, D - 2 * Wt, (N - 1 - compStart) * T);
                    break;
                }
            case TopKind.Closed:
                frame.Add(N - 1, full);
                frame.PieceNames[N - 1] = "Top";
                d.Inside = (0, 0, 0);
                break;
            default:
                if (!Panels(d, frame, s, z, N)) return d;
                d.Inside = (W - 2 * Wt, D - 2 * Wt, (N - 2 - compStart) * T);
                break;
        }

        // ---- the button on the floor, and the bolt under it that keeps the drawer shut ----
        if (bolt && drawer >= 0) Bolt(d, frame, z, drawer, nd, floor, compStart);

        Marks(d, s, z);
        Message(d, s, z, floor, nd);

        d.Note(Size(d, s));
        if (solve) Play(d);
        return d;
    }

    static string Size(BoxDesign d, BoxSpec s)
    {
        string h = Math.Abs(d.HeightMm - s.HeightMm) > 0.01 ? $" ({d.Layers} layers: {d.HeightMm:0.#} mm high)" : $" ({d.Layers} layers)";
        string inside = d.Inside.W > 0 ? $" Inside: {d.Inside.W:0} × {d.Inside.D:0} × {d.Inside.H:0.#} mm." : "";
        string drawer = d.DrawerBody is not null ? $" Drawer inside: {d.DrawerInside.W:0} × {d.DrawerInside.D:0} × {d.DrawerInside.H:0.#} mm." : "";
        return $"{s.WidthMm:0.#} × {s.DepthMm:0.#} mm{h}.{inside}{drawer}";
    }

    // ---------------------------------------------------------------------------------------------
    // Sliding panels

    /// <summary>How a boundary between two panels is hooked: which way the hooks' feet point, and whether the next panel only lets this one sit to one side of it.</summary>
    public sealed record Hook(int Dir, bool Half, double[] X);

    /// <summary>
    /// The panels: each a top piece flush with the top of the box and a runner under it with tongues
    /// that run in the groove under the top layer (so it can't be lifted), shifting sideways a few
    /// millimetres. Each runner has hooks into the next panel's runner, L-shaped, whose feet only come
    /// out of their pockets once the two panels are shifted the right way relative to each other.
    /// </summary>
    static bool Panels(BoxDesign d, Body frame, BoxSpec s, Sizes z, int N)
    {
        double T = z.T, g = z.G, W = z.W, D = z.D, Wt = z.Wall, sh = z.Shift, o = z.Tongue, S = z.Stop;
        int K = Math.Clamp(s.Panels, 1, 8);
        int G = N - 2, C = N - 1;
        double lp = D - Wt - g;
        double depth = lp / K;
        double hookDepth = z.HookNeck + z.HookToe + g;
        double needDepth = Math.Max(10, hookDepth + 4 + g);
        if (K > 1 && depth < needDepth)
        {
            int most = Math.Max(1, (int)Math.Floor(lp / needDepth));
            d.Bad($"{K} panels are too narrow front to back for their hooks: use {most} or fewer, or make the box deeper.");
            return false;
        }

        // The frame: the groove (open at the front) and the top layer over it.
        frame.Add(G, new Box2(0, 0, Wt - o - g, D), new Box2(W - Wt + o + g, 0, W, D), new Box2(0, D - Wt + o + g, W, D));
        frame.PieceNames[G] = "Groove";
        frame.Add(C, new Box2(0, 0, Wt, D), new Box2(W - Wt, 0, W, D), new Box2(0, D - Wt, W, D));
        frame.PieceNames[C] = "Top";

        // Panels as they sit shut: every one to the left.
        double cx0 = Wt + g, cx1 = W - Wt - 3 * g - sh;
        double gx0 = Wt - o - g, gx1 = W - Wt + o - sh - g;
        var ys = Enumerable.Range(0, K + 1).Select(i => i * depth).ToArray();
        var tops = new List<List<Box2>>();
        var runners = new List<List<Box2>>();
        for (int i = 0; i < K; i++)
        {
            double y0 = i == 0 ? 0 : ys[i] + g / 2, y1 = i == K - 1 ? lp : ys[i + 1] - g / 2;
            tops.Add([new Box2(cx0, y0, cx1, y1)]);
            runners.Add([new Box2(gx0, y0, gx1, i == K - 1 ? D - Wt + o : y1)]);
        }

        var hooks = d.ForcedHooks ?? PlanHooks(d, s, z, K, cx0, cx1);
        if (hooks is null) return false;
        for (int i = 0; i < K - 1; i++)
        {
            var hk = hooks[i];
            double B = ys[i + 1];
            var cavities = new List<Box2>();
            foreach (var hx in hk.X)
            {
                var (neck, foot, cav) = HookShape(z, B, hx, hk.Dir, hk.Half);
                runners[i].Add(neck);
                runners[i].Add(foot);
                cavities.AddRange(cav);
            }
            runners[i + 1] = Box2.Minus(runners[i + 1], cavities);
        }

        for (int i = 0; i < K; i++)
        {
            var p = new Body { Name = $"Panel {i + 1}", Role = BodyRole.Panel, Number = i + 1 };
            p.Set(C, tops[i]);
            p.Set(G, runners[i]);
            p.PieceNames[C] = $"Panel {i + 1}";
            p.PieceNames[G] = $"Panel {i + 1} runner";
            // A shallow etched thumb grip in the middle of the top, to push the panel with.
            var t = tops[i][0];
            double rx = Math.Min(9, t.Width / 6), ry = Math.Min(4, t.Height / 4);
            if (ry >= 2) p.Etch(C, [Ellipse((t.X0 + t.X1) / 2, (t.Y0 + t.Y1) / 2, rx, ry)]);
            d.TopBodies.Add(d.Add(p));
        }
        d.Hooks = hooks;
        return true;
    }

    static PathD Ellipse(double cx, double cy, double rx, double ry)
    {
        var p = new PathD();
        for (int k = 0; k < 64; k++)
        {
            double a = 2 * Math.PI * k / 64;
            p.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>
    /// One hook on the runner in front of boundary <paramref name="B"/> at <paramref name="hx"/>: its neck and
    /// foot, and the pocket it needs in the runner behind (for every sideways offset the two runners can
    /// have, or for one side only, and the way out at the offset that frees it).
    /// </summary>
    public static (Box2 Neck, Box2 Foot, List<Box2> Pocket) HookShape(Sizes z, double B, double hx, int dir, bool half)
    {
        double g = z.G, S = z.Stop, w = z.HookW, h = z.HookNeck, e = z.HookToe, f = z.HookFoot;
        var neck = new Box2(hx, B - g / 2 - 0.01, hx + w, B + h);
        // The foot reaches sideways past the neck; the exit is where the runners are offset by ∓S.
        double fx0 = dir > 0 ? hx : hx - f, fx1 = dir > 0 ? hx + w + f : hx + w;
        var foot = new Box2(fx0, B + h, fx1, B + h + e);
        double exit = dir > 0 ? -S : S;
        // Offsets (front runner relative to the back one) the pocket lets the hook have.
        double rmin = half ? (dir > 0 ? -S : 0) : -S, rmax = half ? (dir > 0 ? 0 : S) : S;
        var pocket = new List<Box2>
        {
            // The way out for the whole hook, at the freeing offset.
            new(fx0 + exit - g, B - 1, fx1 + exit + g, B + h + e + g),
            // The neck, at every offset allowed.
            new(hx + rmin - g, B - 1, hx + w + rmax + g, B + h + g),
            // The foot, at every offset allowed.
            new(fx0 + rmin - g, B + h - g, fx1 + rmax + g, B + h + e + g),
        };
        return (neck, foot, pocket);
    }

    /// <summary>
    /// Picks the hooks for every boundary: which way they point and whether they only allow one side,
    /// where they sit; trying layouts (from the seed) and keeping one whose shortest way out suits the difficulty.
    /// </summary>
    static List<Hook>? PlanHooks(BoxDesign d, BoxSpec s, Sizes z, int K, double cx0, double cx1)
    {
        if (K < 2) return [];
        double S = z.Stop, g = z.G;
        double span = z.HookW + z.HookFoot + 2 * S + 2 * g;
        double lo = cx0 + 2 + S, hi = cx1 - 2 - S;
        int per = hi - lo >= 2 * span + 12 ? 2 : hi - lo >= span + 2 ? 1 : 0;
        if (per == 0) { d.Bad($"The panels are too narrow for their hooks: make the box at least {2 * z.Wall + span + 2 * S + 10 + z.Shift:0} mm wide."); return null; }
        int tries = s.Difficulty switch { Difficulty.Easy => 1, Difficulty.Medium => 10, _ => 40 };
        List<Hook>? best = null;
        int bestLen = -1;
        var lengths = new List<(int Len, List<Hook> Hooks)>();
        for (int t = 0; t < tries; t++)
        {
            var rnd = new Random(s.Seed * 7919 + t * 104729 + K * 31);
            var hooks = new List<Hook>();
            for (int i = 0; i < K - 1; i++)
            {
                int dir = rnd.Next(2) == 0 ? 1 : -1;
                bool half = s.Difficulty != Difficulty.Easy && rnd.NextDouble() < (s.Difficulty == Difficulty.Hard ? 0.8 : 0.5);
                var xs = new double[per];
                for (int k = 0; k < per; k++)
                {
                    // Each hook's whole span (foot and pockets) inside its share of the width, a little to one side by chance.
                    double a = lo + (hi - lo) * k / per, b = lo + (hi - lo) * (k + 1) / per;
                    double room = Math.Max(0, b - a - span);
                    double left = a + room * (0.25 + 0.5 * rnd.NextDouble());
                    // The hook's own x is where its neck starts: the span runs from its left-most pocket.
                    xs[k] = dir > 0 ? left + S + g : left + S + g + z.HookFoot;
                }
                hooks.Add(new Hook(dir, half, xs));
            }
            if (tries == 1) return hooks;
            // Try it: build the panels with these hooks and play just the top.
            int len = TryTop(s, z, K, hooks);
            if (len < 0) continue;
            lengths.Add((len, hooks));
        }
        if (lengths.Count == 0) { d.Bad("Couldn't find a way to hook the panels that opens: try another variation."); return null; }
        var sorted = lengths.OrderBy(x => x.Len).ToList();
        (bestLen, best) = s.Difficulty == Difficulty.Hard ? sorted[^1] : sorted[sorted.Count / 2];
        return best;
    }

    /// <summary>The shortest way out of just the panels with these hooks, or −1 if they don't come out.</summary>
    static int TryTop(BoxSpec s, Sizes z, int K, List<Hook> hooks)
    {
        var trial = s with { Drawer = false, Bolt = false, Top = TopKind.SlidingPanels, HeightMm = 4 * z.T, Message = "", MarkPieces = false };
        var d = new BoxDesign { Spec = trial, T = z.T, Fit = z.G, Layers = 4 };
        var frame = new Body { Name = "Box", Role = BodyRole.Frame };
        d.Add(frame);
        d.ForcedHooks = hooks;
        if (!Panels(d, frame, trial, z, 4)) return -1;
        var sol = Solver.Solve(d, 5000);
        return sol.Solved ? sol.Moves.Count : -1;
    }

    // ---------------------------------------------------------------------------------------------
    // The button and bolt

    /// <summary>
    /// A button on the floor inside the box, with a knob, on a bar that slides sideways in a slot in the
    /// floor; under the bar a tooth hangs down into the drawer's top layer just in front of its back.
    /// Shut, the tooth catches the drawer's back; slid across, it lines up with a gap in the drawer's
    /// back and the drawer pulls out past it. The tooth is longer than the slot is wide, so the button
    /// can't be lifted out; the button covers the slot whichever way it's slid.
    /// </summary>
    static void Bolt(BoxDesign d, Body frame, Sizes z, int drawer, int nd, int floor, int compStart)
    {
        double T = z.T, g = z.G, W = z.W, D = z.D, Wt = z.Wall, dw = z.DrawerWall;
        double dx0 = Wt + g, dx1 = W - Wt - g, dy1 = D - Wt - g;
        double tw = Math.Max(4, 1.5 * T), bw = Math.Max(4, 1.5 * T), tl = bw + 2 * g + 3, tv = tw + g + 1.5;
        double ty1 = dy1 - dw - g, ty0 = ty1 - tl, tyc = (ty0 + ty1) / 2;
        // From the right: the button stays inside the box when slid across.
        double nubX1 = W - Wt - 2 * g - 1 - tv;
        double barX1 = nubX1 - tv - g - 1.5, barX0 = barX1 - (tw + 4);
        double tx0 = barX0 + 2, tx1 = barX1 - 2;
        double nubX0 = barX0 - tv - g - 1.5;
        var bar = new Box2(barX0, tyc - bw / 2, barX1, tyc + bw / 2);
        var slot = new Box2(bar.X0 - g, bar.Y0 - g, bar.X1 + tv + g, bar.Y1 + g);
        var notch = new Box2(tx0 + tv, dy1 - dw - 1, tx1 + tv + 2 * g, dy1 + 1);
        double ix0 = dx0 + dw, ix1 = dx1 - dw;
        if (nubX0 < Wt + g + 1 || tx0 < ix0 + 2 || notch.X1 > ix1 - 1)
        {
            d.Warn("The drawer is too narrow for the bolt that locks it: it's left unlocked. Make the box wider to have it.");
            return;
        }
        // The slot through the floor.
        frame.Set(floor, Box2.Minus(frame.Solid[floor], [slot]));
        // The gap in the drawer's back, top layer.
        var dr = d.Bodies[drawer];
        dr.Set(nd, Box2.Minus(dr.Solid[nd], [notch]));

        var b = new Body { Name = "Button", Role = BodyRole.Button };
        b.Add(nd, new Box2(tx0, ty0, tx1, ty1));
        b.PieceNames[nd] = "Bolt tooth";
        b.Add(floor, bar);
        b.PieceNames[floor] = "Bolt";
        var nub = new Box2(nubX0, ty0, nubX1, ty1);
        b.Add(compStart, nub);
        b.PieceNames[compStart] = "Button";
        double kx = (nub.X0 + nub.X1) / 2, kw = Math.Min(8, nub.Width / 3);
        b.Add(compStart + 1, new Box2(kx - kw / 2, nub.Y0 + 1, kx + kw / 2, nub.Y1 - 1));
        b.PieceNames[compStart + 1] = "Button knob";
        b.ReachAfter.AddRange(d.TopBodies);
        d.Add(b);
    }

    // ---------------------------------------------------------------------------------------------
    // Letters and the message

    /// <summary>
    /// Letters on the faces that get glued over, so the layers go together in order: the box's layers A,
    /// B, C… from the bottom (the same letter for layers that are the same), the drawer's D1, D2…, and
    /// the panels' runners 1, 2…. On the back left corner, where the layer above covers it.
    /// </summary>
    static void Marks(BoxDesign d, BoxSpec s, Sizes z)
    {
        if (!s.MarkPieces) return;
        var frame = d.Bodies[0];
        int top = frame.Solid.Keys.Max();
        var letters = FrameLetters(d);
        double corner = (d.Spec.Top == TopKind.SlidingPanels ? z.Wall - z.Tongue - z.G : z.Wall) - 1.6;
        foreach (var (layer, _) in frame.Solid)
        {
            // The top layer shows.
            if (layer == top && d.Spec.Top != TopKind.LiftLid) continue;
            double size = Math.Min(Math.Max(corner, 2.5), 6);
            frame.Etch(layer, Text(letters[layer], s.Font, size / 2 + 0.8, z.D - size / 2 - 0.8, size, size));
        }
        if (d.DrawerBody is int di)
        {
            var dr = d.Bodies[di];
            double dw = z.DrawerWall, x = z.Wall + z.G, y1 = z.D - z.Wall - z.G;
            int dtop = dr.Solid.Keys.Max();
            double size = Math.Min(dw - 1.2, 5);
            foreach (var layer in dr.Solid.Keys.Where(l => l != dtop))
            {
                dr.Etch(layer, Text($"D{layer}", s.Font, x + dw * 0.9, y1 - dw / 2, dw * 1.7, size));
                dr.PieceNames[layer] = $"{dr.PieceName(layer)} (D{layer})";
            }
        }
        foreach (var p in d.Bodies.Where(b => b.Role == BodyRole.Panel))
        {
            // Under the top piece, near its back left corner: clear of the pocket at the front and the tongue at the side.
            int runner = p.Solid.Keys.Min();
            var face = p.Solid[p.Solid.Keys.Max()][0];
            double size = Math.Min(5, face.Height * 0.3);
            p.Etch(runner, Text(p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Font, face.X0 + 1 + size / 2, face.Y1 - 1.5 - size / 2, size, size));
        }
    }

    /// <summary>A letter for each different piece of the box, from the bottom up.</summary>
    public static Dictionary<int, string> FrameLetters(BoxDesign d)
    {
        var frame = d.Bodies[0];
        var byShape = new Dictionary<string, string>();
        var letters = new Dictionary<int, string>();
        foreach (var layer in frame.Solid.Keys)
        {
            string key = ShapeKey(frame, layer);
            if (!byShape.TryGetValue(key, out var letter)) byShape[key] = letter = Letter(byShape.Count);
            letters[layer] = letter;
        }
        return letters;
    }

    static string Letter(int i) => i < 26 ? ((char)('A' + i)).ToString() : $"{(char)('A' + i / 26 - 1)}{(char)('A' + i % 26)}";

    /// <summary>The same for two layers whose pieces are cut the same.</summary>
    public static string ShapeKey(Body b, int layer)
    {
        var outline = b.Outline(layer);
        var sb = new System.Text.StringBuilder();
        foreach (var p in outline.OrderBy(p => p.Count).ThenBy(p => p.Sum(q => q.x + 3 * q.y)))
        {
            sb.Append(p.Count).Append(':');
            foreach (var q in p.OrderBy(q => q.x).ThenBy(q => q.y)) sb.Append($"{q.x:0.###},{q.y:0.###};");
            sb.Append('|');
        }
        return sb.ToString();
    }

    /// <summary>The message on the drawer's bottom, or on the floor of the box (clear of the button's slot).</summary>
    static void Message(BoxDesign d, BoxSpec s, Sizes z, int floor, int nd)
    {
        string text = (s.Message ?? "").Trim();
        if (text.Length == 0) return;
        double Wt = z.Wall, W = z.W, D = z.D, g = z.G;
        if (s.MessageAt == MessagePlace.Drawer && d.DrawerBody is int di)
        {
            double dw = z.DrawerWall, x0 = Wt + g + dw + 3, x1 = W - Wt - g - dw - 3, y0 = dw + 3, y1 = D - Wt - g - dw - 3;
            // Keep it in front of the bolt's tooth.
            // In the middle, in front of the bolt's tooth at the back.
            var shape = Text(text, s.Font, (x0 + x1) / 2, (y0 + y1) / 2 - 2, x1 - x0, Math.Min(14, (y1 - y0) * 0.45));
            d.Bodies[di].Etch(1, shape);
            return;
        }
        if (d.Inside.W <= 0) { d.Warn("There's no space inside the box for the message: put it in the drawer."); return; }
        {
            double x0 = Wt + 3, x1 = W - Wt - 3, y0 = Wt + 3, y1 = D - Wt - 3;
            // In the front half, clear of the button.
            double ym = y0 + (y1 - y0) * 0.45;
            var shape = Text(text, s.Font, (x0 + x1) / 2, (y0 + ym) / 2, x1 - x0, Math.Min(14, ym - y0));
            d.Bodies[0].Etch(floor, shape);
        }
    }

    static PathsD Text(string text, string font, double cx, double cy, double maxW, double maxH) =>
        Labels.Text(text, font, cx, cy, maxW, maxH);

    // ---------------------------------------------------------------------------------------------

    /// <summary>Checks nothing is in the same place as anything else shut, then plays the box.</summary>
    static void Play(BoxDesign d)
    {
        var clash = Solver.Overlaps(d, Pose.Shut(d.Bodies.Count));
        if (clash.Count > 0) { d.Bad("Pieces are in the same place: " + clash[0] + ". Try other sizes."); return; }
        var sol = Solver.Solve(d);
        d.Solution = sol;
        if (!sol.Solved)
        {
            d.Bad(sol.TooMany ? "Too many ways to try to check it opens: use fewer panels." : "It doesn't open: something is stuck. Try other sizes or another variation.");
            return;
        }
        int panels = d.Bodies.Count(b => b.Role == BodyRole.Panel);
        string first = d.Spec.Top switch
        {
            TopKind.SlidingPanels when panels > 1 => $"The panels come out in {(sol.BoxOpenAt)} moves",
            TopKind.SlidingPanels => "The lid slides out",
            TopKind.LiftLid => "The lid lifts off",
            _ => "",
        };
        string drawer = d.DrawerBody is null ? "" :
            d.Bodies.Any(b => b.Role == BodyRole.Button) ? $"the drawer opens {sol.DrawerOpenAt - Math.Max(0, sol.BoxOpenAt)} moves after that (the button inside unlocks it)" :
            d.TopBodies.Count == 0 ? "The drawer pulls out" : "the drawer pulls out any time";
        string sentence = first.Length > 0 && drawer.Length > 0 ? $"{first}; {drawer}." : $"{first}{drawer}.";
        d.Note($"Played through: {sentence} {sol.Moves.Count} move{(sol.Moves.Count == 1 ? "" : "s")} in all, the fewest there are ({sol.States} positions tried).");
    }
}
