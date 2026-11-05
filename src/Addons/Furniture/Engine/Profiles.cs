using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Things cut as one flat shape seen from the front, standing on feet that cross them (halving joints):
/// candle stands and lamps, armour stands, training dummies and coat stands, and one-panel screens. Also
/// folding screens (separate leaves on working hinges, standing in a zig-zag), a weapon rack (weapons standing through a holder between two ends) and stocks (a board between two posts).
/// </summary>
public static class Profiles
{
    /// <summary>A line of the given width with round ends and joins.</summary>
    static PathsD Bar(PathD line, double w) => Clipper.InflatePaths([line], w / 2, JoinType.Round, EndType.Round, 2, 4);

    static PathsD Bar(double x0, double y0, double x1, double y1, double w) => Bar([new(x0, y0), new(x1, y1)], w);

    /// <summary>
    /// A shape that stands on feet: the shape (flat, u = x, v = y, its bottom at 0) stands at the middle of
    /// the depth, and a foot crosses it at each of <paramref name="feetAt"/>, the two slotted halfway into
    /// each other. Each foot is <paramref name="depth"/> long and <paramref name="footH"/> high.
    /// </summary>
    static void OnFeet(Maker m, string name, PathsD shape, PathsD holes, PathsD etched, List<PathD> lines, double[] feetAt, double depth, double footH, bool grainAlongU = false, PathsD? face = null)
    {
        double sw = m.SlotW, T = m.T;
        var cuts = new PathsD(holes);
        foreach (var x in feetAt) cuts.Add(Geo.Rect(x - sw / 2, -1, x + sw / 2, footH / 2));
        m.Add(name, shape, cuts, [Pose.Front(0, 0, depth / 2)], grainAlongU, face: face, etched: etched, lines: lines);
        var foot = new PathsD { WoodFace.RoundRect(0, -2, depth, footH, Math.Min(footH * 0.45, 1.2)) };
        foot = Geo.Intersect(foot, [Geo.Rect(0, 0, depth, footH)]);
        var fh = new PathsD { Geo.Rect(depth / 2 - sw / 2, footH / 2, depth / 2 + sw / 2, footH + 1) };
        m.Add("Foot", foot, fh, [.. feetAt.Select(x => Pose.Left(x, 0, 0))], grainAlongU: true);
        _ = T;
    }

    // ---- candle stands and lamps ----

    public static void Candlestand(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW;
        double footH = Math.Max(2.4, 2 * 1.0 + 0.2), xc = W / 2, stem = 1.3;
        double footW = Math.Max(D, sw + 4);
        if (H < 10 || W < 4) { m.Bad("Make the stand at least 10 mm high and 4 mm wide."); return; }
        var shape = new PathsD
        {
            WoodFace.RoundRect(xc - footW / 2, 0, xc + footW / 2, footH, Math.Min(1, footH * 0.4)),
            // A knop where the stem meets the foot.
            Rugs.Ellipse(xc, footH + 0.6, 1.4, 1.0),
        };
        var etched = new PathsD();
        var lines = new List<PathD>();
        switch (it.Candle)
        {
            case CandleStyle.Lamp:
            {
                double top = H - 3.5;
                shape.AddRange(Bar(xc, footH, xc, top, stem));
                var head = Fronts.Chamfered(xc - W / 2 + 0.3, top - 0.5, xc + W / 2 - 0.3, H, 0.8);
                shape.Add(head);
                etched.Add(Geo.Rect(xc - W / 2 + 1.2, H - 2.6, xc + W / 2 - 1.2, H - 1.6));
                for (double y = footH + 2; y < top - 1; y += 2) lines.Add([new(xc - stem / 2, y), new(xc + stem / 2, y)]);
                break;
            }
            case CandleStyle.Torch:
            {
                double bowl = Math.Min(W * 0.8, 6), top = H - 4.5;
                shape.AddRange(Bar(xc, footH, xc, top, stem * 1.2));
                shape.Add([new(xc - bowl / 2, top + 1.4), new(xc + bowl / 2, top + 1.4), new(xc + bowl * 0.3, top - 0.3), new(xc - bowl * 0.3, top - 0.3)]);
                shape.AddRange(Flames(xc, top + 1.3, bowl * 0.8, H - top - 1.3));
                lines.Add([new(xc - bowl * 0.45, top + 0.8), new(xc + bowl * 0.45, top + 0.8)]);
                etched.AddRange(Geo.Intersect(Flames(xc, top + 1.3, bowl * 0.45, (H - top - 1.3) * 0.6), Flames(xc, top + 1.3, bowl * 0.8, H - top - 1.3)));
                break;
            }
            default:
            {
                // A stem with knops, arms curving out and up to drip pans, a taper in each with its flame: three
                // candles, or five on a wide one, the middle one standing highest.
                double flameH = Geo.Clamp(H * 0.075, 1.5, 2.2), taper = Geo.Clamp(H * 0.11, 2.2, 3.4);
                double topY = H - flameH - taper;                  // the middle pan's top
                int each = W >= 12 ? 2 : 1;
                double step = (W / 2 - 1.4) / each, pan = Math.Min(1.25, step * 0.45 + 0.15), sideY = topY - Math.Min(1.4, H * 0.05);
                double armY = sideY - Math.Min(4.5, H * 0.2);
                shape.AddRange(Bar(xc, footH, xc, topY, stem));
                // Knops: one low on the stem, one where the arms spring from it.
                shape.Add(Rugs.Ellipse(xc, footH + (armY - footH) * 0.45, 1.25, 0.8));
                shape.Add(Rugs.Ellipse(xc, armY - 0.2, 1.15, 0.75));
                var cups = new List<(double X, double Y)> { (xc, topY) };
                for (int k = 1; k <= each; k++)
                {
                    double reach = step * k, y0 = armY - (k - 1) * 1.4;
                    foreach (var s in new[] { -1, 1 })
                    {
                        var arm = new PathD();
                        for (int i = 0; i <= 14; i++)
                        {
                            double t = i / 14.0;
                            arm.Add(new PointD(xc + s * reach * Math.Sin(t * Math.PI / 2), y0 + (sideY - 0.5 - y0) * (1 - Math.Cos(t * Math.PI / 2))));
                        }
                        shape.AddRange(Bar(arm, 1.05));
                        cups.Add((xc + s * reach, sideY));
                    }
                }
                foreach (var (x, y) in cups)
                {
                    // A cup under a wide, thin drip pan; the taper; its flame.
                    shape.Add(new PathD { new(x - 0.6, y - 1.3), new(x + 0.6, y - 1.3), new(x + 0.9, y - 0.5), new(x - 0.9, y - 0.5) });
                    shape.Add(WoodFace.RoundRect(x - pan, y - 0.6, x + pan, y + 0.35, 0.3));
                    shape.Add(Geo.Rect(x - 0.55, y, x + 0.55, y + taper));
                    var flame = Flames(x, y + taper - 0.5, 1.35, flameH + 0.5);
                    shape.AddRange(flame);
                    // The flame burnt dark, so it shows against the taper.
                    etched.AddRange(Geo.Intersect(Geo.Offset(flame, -0.18), [Geo.Rect(x - 2, y + taper + 0.05, x + 2, H + 1)]));
                    lines.Add([new(x - pan + 0.2, y - 0.12), new(x + pan - 0.2, y - 0.12)]);
                    // Wax run down the taper, and where the flame meets it.
                    lines.Add([new(x - 0.2, y + taper - 0.2), new(x - 0.2, y + taper * 0.55)]);
                    lines.Add([new(x - 0.5, y + taper - 0.05), new(x + 0.5, y + taper - 0.05)]);
                }
                lines.Add([new(xc - 0.5, footH + (armY - footH) * 0.45), new(xc + 0.5, footH + (armY - footH) * 0.45)]);
                break;
            }
        }
        OnFeet(m, it.Candle switch { CandleStyle.Lamp => "Lamp", CandleStyle.Torch => "Torch stand", _ => "Candelabra" }, Geo.Solid(shape), [], etched, lines, [xc], footW, footH);
        m.Note("Slide the foot onto the bottom of the stand, crossing it.");
    }

    /// <summary>A flame (one tongue, or three on a wide one) standing at x, y.</summary>
    public static PathsD Flames(double x, double y, double w, double h)
    {
        var all = new PathsD();
        int n = w >= 2.5 ? 3 : 1;
        for (int i = 0; i < n; i++)
        {
            double cx = x + (n == 1 ? 0 : (i - 1) * w * 0.28), hh = h * (n == 1 || i == 1 ? 1 : 0.72), ww = n == 1 ? w : w * 0.5;
            double r = Math.Min(ww / 2, hh * 0.35);
            // A round bottom sitting on y, a tongue up to a flicked tip.
            all.Add(Rugs.Ellipse(cx, y + r, ww / 2, r));
            all.Add(new PathD { new(cx - ww / 2, y + r), new(cx - ww * 0.25, y + r + (hh - r) * 0.55), new(cx + ww * 0.1, y + hh), new(cx + ww * 0.3, y + r + (hh - r) * 0.45), new(cx + ww / 2, y + r) });
        }
        // Each shape the same way round, so overlaps add up rather than cancel.
        // Tips rounded off to what the laser can leave standing.
        var u = Geo.Solid(all);
        var soft = Geo.Offset(Geo.Offset(u, -0.45, round: true), 0.45, round: true);
        return soft.Count > 0 ? soft : u;
    }

    // ---- armour stands, dummies and coat stands ----

    public static void Stand(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW;
        double footH = 2.6, xc = W / 2, post = 1.6;
        double footW = Math.Max(Math.Min(W, D * 1.2), sw + 4);
        if (H < 14 || W < 6) { m.Bad("Make the stand at least 14 mm high and 6 mm wide."); return; }
        var shape = new PathsD { WoodFace.RoundRect(xc - footW / 2, 0, xc + footW / 2, footH, 1.0) };
        var holes = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        var look = Fronts.Look(m);
        string name;
        switch (it.Stand)
        {
            case StandStyle.Dummy:
            {
                name = "Training dummy";
                double shoulder = H * 0.66, body = H * 0.45;
                shape.AddRange(Bar(xc, footH, xc, H - 3, post * 1.3));
                shape.Add(WoodFace.RoundRect(xc - W / 2, shoulder - 0.7, xc + W / 2, shoulder + 0.7, 0.6));
                var sack = Rugs.Ellipse(xc, body, W * 0.3, H * 0.17);
                shape.Add(sack);
                shape.Add(Geo.Circle(xc, H - 2.1, 2.1));
                // Ropes round the sack, a target on it, a face scratched on the head.
                foreach (var f in new[] { -0.55, 0.0, 0.55 }) lines.Add([new(xc - W * 0.3, body + f * H * 0.17), new(xc + W * 0.3, body + f * H * 0.17 + 0.3)]);
                lines.Add(WoodFace.Dot(xc, body, Math.Min(W * 0.18, 2)));
                etched.Add(Geo.Circle(xc, body, Math.Min(W * 0.07, 0.8), 0.01));
                lines.Add([new(xc - 0.8, H - 1.6), new(xc + 0.8, H - 2.6)]);
                lines.Add([new(xc + 0.8, H - 1.6), new(xc - 0.8, H - 2.6)]);
                lines = Geo.ClipLines(lines, Geo.Solid(shape));
                break;
            }
            case StandStyle.Coats:
            {
                name = "Coat stand";
                shape.AddRange(Bar(xc, footH, xc, H - 1.2, post));
                shape.Add(Geo.Circle(xc, H - 1.0, 1.0));
                foreach (var s in new[] { -1, 1 })
                {
                    shape.AddRange(Bar([new(xc, H - 4), new(xc + s * W * 0.25, H - 3.2), new(xc + s * W * 0.32, H - 1.8)], 1.0));
                    shape.Add(Geo.Circle(xc + s * W * 0.32, H - 1.8, 0.65));
                }
                // A cloak hanging from the right hook.
                double cx = xc + W * 0.3, cw = Math.Min(W * 0.36, 5);
                var cloak = new PathD { new(cx - 0.6, H - 2.2), new(cx + 0.6, H - 2.2), new(cx + cw / 2, H * 0.3), new(cx - cw / 2 + 0.5, H * 0.28) };
                shape.Add(cloak);
                for (double f = -0.3; f <= 0.31; f += 0.3) lines.Add([new(cx + f * 0.6, H - 2.6), new(cx + f * cw, H * 0.31)]);
                break;
            }
            default:
            {
                name = "Armour stand";
                // A post up to the hips, then a suit of armour: tassets, breastplate, pauldrons, gorget and helm.
                double hip = H * 0.38, chest = H * 0.62, shoulder = H * 0.74, neck = H * 0.8, sh = W / 2;
                shape.AddRange(Bar(xc, footH, xc, hip + 1, post * 1.4));
                // Tassets (skirt of plates), wider at the bottom.
                var skirt = new PathD { new(xc - sh * 0.62, hip - H * 0.1), new(xc + sh * 0.62, hip - H * 0.1), new(xc + sh * 0.5, hip + 0.5), new(xc - sh * 0.5, hip + 0.5) };
                shape.Add(skirt);
                // The breastplate: waist to chest, then in to the neck.
                var torso = new PathD
                {
                    new(xc - sh * 0.48, hip), new(xc + sh * 0.48, hip), new(xc + sh * 0.6, chest), new(xc + sh * 0.7, shoulder - 0.5),
                    new(xc + sh * 0.3, neck), new(xc - sh * 0.3, neck), new(xc - sh * 0.7, shoulder - 0.5), new(xc - sh * 0.6, chest),
                };
                shape.Add(torso);
                // Pauldrons: rounded caps over the shoulders.
                foreach (var s in new[] { -1, 1 }) shape.Add(Rugs.Ellipse(xc + s * sh * 0.72, shoulder - 0.6, sh * 0.32, H * 0.06));
                // The helm on its gorget.
                double hr = Math.Min(W * 0.17, H * 0.085);
                shape.Add(Geo.Rect(xc - hr * 0.6, neck - 0.3, xc + hr * 0.6, neck + 1.0));
                var helm = Fronts.ArchedRect(xc - hr, neck + 0.8, xc + hr, Math.Min(H, neck + 0.8 + hr * 2.4));
                shape.Add(helm);
                etched.Add(Geo.Rect(xc - hr * 0.75, neck + 0.8 + hr * 1.2, xc + hr * 0.75, neck + 0.8 + hr * 1.2 + 0.35));
                // Plate lines: the ridge, the belt, the tassets' lames, the pauldrons' rims.
                lines.Add([new(xc, hip + 0.4), new(xc, neck - 0.4)]);
                etched.Add(Geo.Rect(xc - sh * 0.5, hip + 0.1, xc + sh * 0.5, hip + 0.6));
                for (double y = hip - H * 0.1 + 1.0; y < hip; y += 1.0) lines.Add([new(xc - sh * 0.6, y), new(xc + sh * 0.6, y)]);
                foreach (var s in new[] { -1, 1 }) lines.Add([new(xc + s * sh * 0.45, shoulder - 0.9), new(xc + s * sh * 1.0, shoulder - 1.2)]);
                if (look == FrontLook.Metal) etched.Add(Geo.Circle(xc, chest, Math.Min(1.2, sh * 0.2), 0.01));
                else lines.Add(Geo.Loop(Cabinets.Trefoil(xc, chest - 0.5, Math.Min(1.6, sh * 0.3))));
                lines = Geo.ClipLines(lines, Geo.Solid(shape));
                break;
            }
        }
        OnFeet(m, name, Geo.Solid(shape), holes, etched, lines, [xc], footW, footH, face: []);
        m.Note("Slide the foot onto the bottom of the stand, crossing it.");
    }

    // ---- standing screens ----

    /// <summary>
    /// A standing screen. One panel stands on two feet that cross it; two or more are separate leaves
    /// joined by working hinges, standing on their own in a zig-zag (see <see cref="HingedScreen"/>).
    /// </summary>
    public static void Screen(Maker m)
    {
        var it = m.Item;
        if (it.WidthMm < 10 || it.HeightMm < 10) { m.Bad("Make the screen at least 10 mm wide and 10 mm high."); return; }
        if (Geo.Clamp(it.Columns, 1, 6) >= 2) { HingedScreen(m); return; }
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW;
        double footH = 2.6, rail = footH + 1.2, stile = 1.2;
        double lw = W - 2 * stile;
        if (lw < 3) { m.Bad("The screen's panel is too narrow: make it wider."); return; }
        var look = Fronts.Look(m);
        var style = it.ScreenStyle;
        double x0 = stile / 2, x1 = W - stile / 2, rise = Math.Min((x1 - x0) * 0.45, H * 0.25);
        var outline = new PathsD { Geo.Rect(0, 0, W, rail + 0.5), LeafShape(style, x0, x1, H, rise) };
        var holes = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        etched.AddRange(Fronts.Outline(x0 + 0.1, 0.1, x1 - 0.1, H, 0.2));
        LeafFace(style, look, x0 + stile, x1 - stile, rail + 0.6, LeafTop(style, H, rise, stile), H, holes, etched, lines);
        OnFeet(m, "Screen", Geo.Solid(outline), holes, etched, lines, [W * 0.2, W * 0.8], Math.Max(D, sw + 4), footH, face: style == ScreenStyle.Barrier ? [] : null);
        m.Note("Slide the two feet onto the screen's bottom edge, crossing it.");
    }

    /// <summary>One leaf's outline, x0..x1 wide and <paramref name="H"/> high, its top shaped for the style.</summary>
    static PathD LeafShape(ScreenStyle style, double x0, double x1, double H, double rise) => style switch
    {
        ScreenStyle.Barrier => Fronts.Chamfered(x0, 0, x1, H, 1.2),
        ScreenStyle.Lattice => Shapes.Topped(BackTop.Arched, x0, 0, x1, H, rise),
        ScreenStyle.Tracery => Shapes.Topped(BackTop.Pointed, x0, 0, x1, H, rise),
        _ => Shapes.Topped(BackTop.Rounded, x0, 0, x1, H, rise),
    };

    /// <summary>Where a leaf's straight sides end and its shaped top begins.</summary>
    static double LeafShoulder(ScreenStyle style, double H, double rise) => style == ScreenStyle.Barrier ? H - 1.2 : H - rise;

    /// <summary>The top of the area inside a leaf's frame (its lattice, window or panel).</summary>
    static double LeafTop(ScreenStyle style, double H, double rise, double stile) => H - stile - (style == ScreenStyle.Barrier ? 0.6 : rise * 0.6);

    /// <summary>What's inside one leaf's frame a..b, ya..yb: a lattice or tracery cut through it, a barrier's chevrons and grille, or a plain panel.</summary>
    static void LeafFace(ScreenStyle style, FrontLook look, double a, double b, double ya, double yb, double H, PathsD holes, PathsD etched, List<PathD> lines)
    {
        if (b - a < 1.5 || yb - ya < 2) return;
        switch (style)
        {
            case ScreenStyle.Lattice:
                holes.AddRange(Fronts.Lattice(a, ya, b, yb, 3.0, 1.0));
                break;
            case ScreenStyle.Tracery:
            {
                double wy = yb - (b - a) * 0.9;
                if (wy - ya < 2) wy = ya + 2;
                holes.Add(Fronts.PointedRect(a + 0.2, ya, b - 0.2, wy));
                double r = (b - a) * 0.36;
                // The trefoil's cusps rounded, so no sharp sliver of sheet points into it.
                if (r > 0.9 && wy + 1.4 + r * 2 < H - 1.2)
                    holes.AddRange(Geo.Offset(Geo.Offset([Cabinets.Trefoil((a + b) / 2, wy + 1.3 + r, r)], 0.45, round: true), -0.45, round: true));
                break;
            }
            case ScreenStyle.Barrier:
            {
                // Chevrons, a light strip along the top and a grille of slots.
                var chev = new PathsD();
                for (double y = ya; y < ya + (yb - ya) * 0.45; y += 1.6)
                    chev.AddRange(Bar([new(a, y), new((a + b) / 2, y + 0.9), new(b, y)], 0.6));
                etched.AddRange(Geo.Intersect(Geo.Union(chev), [Geo.Rect(a, ya, b, yb)]));
                for (double y = ya + (yb - ya) * 0.55; y + 0.6 < yb - 1.6; y += 1.6) holes.Add(Geo.Rect(a + 0.4, y, b - 0.4, y + 0.6));
                etched.Add(Geo.Rect(a, yb - 0.9, b, yb));
                break;
            }
            default:
                Fronts.Panel(look, a, ya, b, yb, etched, lines);
                break;
        }
    }

    /// <summary>How a hinged screen's hinges are sized for a sheet and a fit (all mm).</summary>
    public readonly record struct ScreenHinge(double T, double Fit)
    {
        /// <summary>The pin's width: a strip of the sheet, about square, never narrower than a bridge.</summary>
        public double PinW => Math.Max(Maker.Bridge * 1.2, T * 0.75);
        /// <summary>Half the pin's diagonal: how far its corners reach from the hinge's axis as it turns.</summary>
        public double PinReach => Math.Sqrt(PinW * PinW + T * T) / 2;
        /// <summary>The round hole in a knuckle the pin turns in.</summary>
        public double HoleR => PinReach + Math.Max(0, Fit) / 2 + 0.12;
        /// <summary>A knuckle's round end, a bridge and a bit round the hole.</summary>
        public double KnuckleR => HoleR + Maker.Bridge + 0.15;
        /// <summary>How far each leaf's end stands back from the axis, clear of the pin's corners.</summary>
        public double Gap => PinReach + 0.35;
        /// <summary>Space left round the other leaf's knuckles.</summary>
        public const double Clear = 0.25;
        /// <summary>How deep the notch is that the other leaf's knuckle turns in.</summary>
        public double ClearDepth => KnuckleR + Clear - Gap;
        /// <summary>How far a knuckle goes into its own leaf's notch.</summary>
        public double Grip => Math.Max(1.3, ClearDepth);
        /// <summary>The leaf's frame at a hinged end: wide enough to take the notches and keep a bridge inside them.</summary>
        public double Side => Grip + Maker.Bridge;
        /// <summary>Between two knuckles on one hinge, one above the other.</summary>
        public double Stack => Maker.Bridge + Clear + Math.Max(0, Fit) + 0.1;
        /// <summary>The bottom knuckle's height off the floor.</summary>
        public double Low => Maker.Bridge + Clear + 0.15;
    }

    /// <summary>The fold between leaves that the most and least zig-zag allow (degrees off straight, each way).</summary>
    public const double MinFoldDeg = 12, MaxFoldDeg = 40;

    /// <summary>
    /// The leaves' width and how far each turns off straight (radians) for a screen <paramref name="W"/>
    /// wide and <paramref name="Dz"/> deep (its leaves' middles, front to back) in <paramref name="n"/>
    /// leaves with <paramref name="gap"/> from each leaf's end to its hinge's axis.
    /// </summary>
    public static (double Leaf, double Turn) ZigZag(int n, double W, double Dz, double gap)
    {
        (double Leaf, double Depth) At(double b)
        {
            double c = Math.Cos(b), s = Math.Sin(b);
            if (n == 2)
            {
                double leaf = W / (2 * c) - gap;
                return (leaf, (leaf + gap) * s);
            }
            double l = (W / c - 2 * (n - 1) * gap) / n;
            return (l, (l + 2 * gap) * s);
        }
        double lo = MinFoldDeg * Math.PI / 180, hi = MaxFoldDeg * Math.PI / 180;
        if (At(lo).Depth >= Dz) return (At(lo).Leaf, lo);
        if (At(hi).Depth <= Dz) return (At(hi).Leaf, hi);
        for (int i = 0; i < 50; i++)
        {
            double mid = (lo + hi) / 2;
            if (At(mid).Depth < Dz) lo = mid; else hi = mid;
        }
        return (At((lo + hi) / 2).Leaf, (lo + hi) / 2);
    }

    /// <summary>
    /// A folding screen: two to six separate leaves that stand in a zig-zag, joined by working hinges.
    /// At each hinge four knuckles (small flat plates with a hole at the hinge's axis) stand out of the
    /// two leaves' ends, two from each, taking turns up the hinge: each sits in a notch in its own leaf,
    /// and the other leaf is notched round it so it can turn. A pin cut from the sheet drops through all
    /// four to the floor; the top knuckle's hole is square, so it holds the pin, and the other leaf turns
    /// round it. <see cref="FurnitureItem.WidthMm"/> and <see cref="FurnitureItem.DepthMm"/> are the screen
    /// as it stands: they set how wide the leaves are and how far they fold.
    /// </summary>
    static void HingedScreen(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        int n = Geo.Clamp(it.Columns, 2, 6);
        var hg = new ScreenHinge(T, f);
        double gap = hg.Gap, side = hg.Side, kr = hg.KnuckleR, c = ScreenHinge.Clear;
        // The depth asked for is to the knuckles' round ends, which stand out round the hinges at the front and back.
        var (lw, turn) = ZigZag(n, W, Math.Max(0, D - 2 * kr), gap);
        if (lw - 2 * side < 1.5) { m.Bad($"The screen's leaves are too narrow for their hinges: make it at least {MinWidth(n, gap, side + side + 1.5):0.#} mm wide, or have fewer panels."); return; }
        var style = it.ScreenStyle;
        var look = Fronts.Look(m);
        double rise = Math.Min(lw * 0.45, H * 0.25), shoulder = LeafShoulder(style, H, rise);
        // The knuckles, bottom to top: the left leaf's, the right leaf's; then higher up the right leaf's and the left leaf's.
        double h1 = hg.Low, h2 = h1 + T + hg.Stack;
        double h4 = shoulder - Maker.Bridge - c - T, h3 = h4 - T - hg.Stack;
        double between = (h3 - c) - (h2 + T + c);
        if (between < Maker.Bridge + 0.5)
        {
            m.Bad($"The screen is too low for its hinges: make it at least {H + Maker.Bridge + 0.5 - between:0.#} mm high.");
            return;
        }
        double[] leftOwn = [h1, h4], rightOwn = [h2, h3];

        // ---- the leaves ----
        // Each leaf's end: notches its own knuckles sit in, and deeper-set ones the other leaf's turn in.
        PathsD EndNotches(double end, int dir, double[] own, double[] other)
        {
            var cuts = new PathsD();
            foreach (var h in own) cuts.Add(dir > 0 ? Geo.Rect(end - hg.Grip, h, end + 1, h + sw) : Geo.Rect(end - 1, h, end + hg.Grip, h + sw));
            foreach (var h in other)
                cuts.Add(dir > 0 ? Geo.Rect(end - hg.ClearDepth, h - c, end + 1, h + T + c) : Geo.Rect(end - 1, h - c, end + hg.ClearDepth, h + T + c));
            return cuts;
        }
        double ya = 3.2, yb = LeafTop(style, H, rise, 1.2);
        (PathsD Shape, PathsD Holes, PathsD Etched, List<PathD> Lines) Leaf(bool hingeLeft, bool hingeRight)
        {
            var holes = new PathsD();
            var etched = new PathsD();
            var lines = new List<PathD>();
            // The same frame at both sides, hinged or not, so every leaf looks alike.
            LeafFace(style, look, side, lw - side, ya, yb, H, holes, etched, lines);
            // A leaf's left end is the right-hand leaf of the hinge to its left, and the other way round.
            if (hingeLeft) holes.AddRange(EndNotches(0, -1, rightOwn, leftOwn));
            if (hingeRight) holes.AddRange(EndNotches(lw, 1, leftOwn, rightOwn));
            return ([LeafShape(style, 0, lw, H, rise)], holes, etched, lines);
        }

        // Where each leaf stands: turned alternately back and forward by the fold, end to end along the hinges.
        double cs = Math.Cos(turn), sn = Math.Sin(turn);
        var dirs = Enumerable.Range(0, n).Select(i => new V3(cs, 0, i % 2 == 0 ? -sn : sn)).ToArray();
        var starts = new V3[n];
        var axes = new V3[n - 1];
        var at = new V3(0, 0, 0);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) at += dirs[i] * gap;
            starts[i] = at;
            at += dirs[i] * lw;
            if (i < n - 1) { at += dirs[i] * gap; axes[i] = at; }
        }
        // Moved so the screen stands from x = 0 and z = 0 back to front.
        double zMin = starts.Min(p => p.Z);
        for (int i = 0; i < n; i++) zMin = Math.Min(zMin, (starts[i] + dirs[i] * lw).Z);
        var shift = new V3(0, 0, -zMin + T / 2);
        Pose LeafPose(int i) => new(starts[i] + shift, dirs[i], V3.UnitY);
        bool face = style != ScreenStyle.Barrier;
        void AddLeaf(string name, bool l, bool r, IEnumerable<int> which)
        {
            var (shape, holes, etched, lines) = Leaf(l, r);
            m.Add(name, shape, holes, which.Select(LeafPose), grainAlongU: false, face: face ? null : [], etched: etched, lines: lines);
        }
        AddLeaf("Left leaf", false, true, [0]);
        if (n > 2) AddLeaf("Leaf", true, true, Enumerable.Range(1, n - 2));
        AddLeaf("Right leaf", true, false, [n - 1]);

        // ---- the knuckles and pins ----
        // A knuckle, flat: its round end on the axis at (0, 0), its arm back along −u into its own leaf's notch.
        double arm = gap + hg.Grip;
        var knuckle = new PathsD { Geo.Rect(-arm, -kr, 0, kr), Geo.Circle(0, 0, kr, 0.01) };
        double pw = hg.PinW;
        // A knuckle lies flat with its u along the arm's direction away from its leaf; it's centred on its height.
        static Pose Flat(V3 o, V3 u) => new(o, u, V3.UnitY.Cross(u));
        var round = new List<Pose>();
        var top = new List<Pose>();
        var pins = new List<Pose>();
        for (int i = 0; i < n - 1; i++)
        {
            var axis = axes[i] + shift;
            V3 L = dirs[i], R = -dirs[i + 1];
            round.Add(Flat(axis + V3.UnitY * (h1 + T / 2), L));
            round.Add(Flat(axis + V3.UnitY * (h2 + T / 2), R));
            round.Add(Flat(axis + V3.UnitY * (h3 + T / 2), R));
            top.Add(Flat(axis + V3.UnitY * (h4 + T / 2), L));
            // The pin's flat side along the left leaf, as the top knuckle's square hole holds it.
            pins.Add(new Pose(axis, L, V3.UnitY));
        }
        m.Add("Knuckle", knuckle, [Geo.Circle(0, 0, hg.HoleR, 0.01)], round, grainAlongU: true, face: []);
        m.Add("Top knuckle", knuckle, [Geo.Rect(-(pw + f) / 2, -sw / 2, (pw + f) / 2, sw / 2)], top, grainAlongU: true, face: []);
        double pinTop = h4 + T + 1.2;
        m.Add("Hinge pin", [Shapes.Topped(BackTop.Rounded, -pw / 2, 0, pw / 2, pinTop, pw / 2)], [], pins, grainAlongU: false, face: []);

        double fold = 180 - 2 * turn * 180 / Math.PI;
        m.Note(m.PushFit
            ? $"Push each knuckle into its notch in the leaf's end, round end out. Line up each hinge's four holes, push a pin down through them from the top until it stands on the floor: the top knuckle grips it and the other leaf turns on it."
            : $"Glue each knuckle into its notch in the leaf's end, round end out, the square-holed one at the top. Line up each hinge's four holes and drop a pin down through them to the floor; a dab of glue in the top knuckle's square hole holds it, and the other leaf turns on it.");
        m.Note($"Stand the leaves in a zig-zag, about {fold:0}° between each pair.");
    }

    /// <summary>How wide a screen of <paramref name="n"/> leaves must be for each leaf to be at least <paramref name="leaf"/> wide, folded as little as it goes.</summary>
    static double MinWidth(int n, double gap, double leaf)
    {
        double c = Math.Cos(MinFoldDeg * Math.PI / 180);
        return n == 2 ? 2 * (leaf + gap) * c : (n * leaf + 2 * (n - 1) * gap) * c;
    }

    // ---- weapon racks ----

    public static void WeaponRack(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        int n = Geo.Clamp(it.Rows, 1, 12);
        double Li = W - 2 * T, hh = Geo.Clamp(H * 0.4, 6, 14), shaft = 1.2;
        if (Li / n < 2.4) { m.Bad($"Too many weapons for the width: make the rack at least {2 * T + n * 2.4:0.#} mm wide."); return; }
        // Two rows, front and back, so the weapons' heads can pass each other.
        double rowGap = T + 0.6, needD = rowGap + sw + 2.4;
        if (D < needD) { m.Bad($"The rack is too shallow: make it at least {needD:0.#} mm deep."); return; }
        if (H < hh + 8) { m.Bad($"The rack is too low: make it at least {hh + 8:0.#} mm high."); return; }
        bool guns = Fronts.Look(m) == FrontLook.Metal;
        double zc = D / 2;
        // The ends: a foot and an upright, their tabs down through the base.
        double endH = hh + T + 3, tw = Geo.Clamp(D * 0.2, 1.2, 3);
        double[] baseTabs = [D * 0.2, D * 0.8];
        var end = new PathsD { Shapes.Topped(BackTop.Rounded, 0, T, D, endH, Math.Min(2, D * 0.25)) };
        foreach (var u in baseTabs) end.Add(Geo.Rect(u - tw / 2, 0, u + tw / 2, T + 0.01));
        double htw = Geo.Clamp(D * 0.2, 1.2, 3);
        double[] holdAt = [D * 0.28, D * 0.72];
        var endHoles = new PathsD { WoodFace.RoundRect(D * 0.38, T - 1, D * 0.62, T + Math.Min(3, hh * 0.4), 0.8) };
        foreach (var u in holdAt) endHoles.Add(Geo.Rect(u - (htw + f) / 2, hh - f / 2, u + (htw + f) / 2, hh + T + f / 2));
        m.Add("End", end, endHoles, [Pose.Left(T / 2, 0, 0), Pose.Left(W - T / 2, 0, 0)], grainAlongU: false);
        // The holder between the ends, with a hole for each weapon.
        var holder = new PathsD { Geo.Rect(0, 0, Li, D) };
        foreach (var u in holdAt)
        {
            holder.Add(Geo.Rect(-T, D - u - htw / 2, 0.01, D - u + htw / 2));
            holder.Add(Geo.Rect(Li - 0.01, D - u - htw / 2, Li + T, D - u + htw / 2));
        }
        var widths = new double[n];
        for (int i = 0; i < n; i++) widths[i] = guns ? 1.4 : (i % 4 == 1 ? 1.5 : shaft);
        // Keep the end holes 1.3 mm from the ends of the holder; alternate weapons go in the back row.
        double edge = 1.3 + 0.8 + f / 2;
        var xs = Enumerable.Range(0, n).Select(i => n == 1 ? Li / 2 : edge + (Li - 2 * edge) * i / (n - 1)).ToArray();
        var zs = Enumerable.Range(0, n).Select(i => n == 1 ? zc : zc + (i % 2 == 0 ? rowGap / 2 : -rowGap / 2)).ToArray();
        var hHoles = new PathsD();
        for (int i = 0; i < n; i++) hHoles.Add(Geo.Rect(xs[i] - (widths[i] + f) / 2, D - zs[i] - sw / 2, xs[i] + (widths[i] + f) / 2, D - zs[i] + sw / 2));
        m.Add("Holder", holder, hHoles, [Pose.Up(T, hh + T / 2, D)], grainAlongU: true);
        // The base under it all.
        double o = 1.2;
        var bSlots = new PathsD();
        foreach (var xe in new[] { T / 2, W - T / 2 })
            foreach (var u in baseTabs) bSlots.Add(Geo.Rect(o + xe - sw / 2, o + D - u - (tw + f) / 2, o + xe + sw / 2, o + D - u + (tw + f) / 2));
        for (int i = 0; i < n; i++) bSlots.Add(Geo.Rect(o + T + xs[i] - (widths[i] + f) / 2, o + D - zs[i] - sw / 2, o + T + xs[i] + (widths[i] + f) / 2, o + D - zs[i] + sw / 2));
        m.Add("Base", [WoodFace.RoundRect(0, 0, W + 2 * o, D + 2 * o, 0.8)], bSlots, [Pose.Up(-o, T / 2, D + o)], grainAlongU: true);
        // The weapons, each standing through the holder with a tab down into the base.
        for (int i = 0; i < n; i++)
        {
            double w = widths[i], top = H - (i % 3) * H * 0.08;
            var (shape, ln, nm) = guns ? Rifle(w, T, hh, top) : (i % 4) switch
            {
                1 => SwordUp(w, T, hh, Math.Min(top, hh + T + 7)),
                2 => Axe(w, T, top * 0.85),
                3 => Halberd(w, T, top),
                _ => Spear(w, T, top),
            };
            // A head that would poke past the end of the rack faces the other way.
            var b = Clipper.GetBounds(shape);
            if (xs[i] + b.right > Li - 0.3)
            {
                shape = [.. shape.Select(q => Clipper.ReversePath([.. q.Select(pt => new PointD(-pt.x, pt.y))]))];
                ln = [.. ln.Select(q => new PathD(q.Select(pt => new PointD(-pt.x, pt.y))))];
            }
            m.Add(nm, shape, [], [Pose.Front(T + xs[i], 0, zs[i])], grainAlongU: false, face: [], lines: ln);
        }
        m.Note("Push the ends into the base, the holder between them, then drop each weapon through its hole into the base.");
    }

    // Weapons: flat shapes standing on their tab (u = x across the middle of the shaft, v = y from the floor).
    static (PathsD, List<PathD>, string) Spear(double w, double T, double top)
    {
        var s = new PathsD { Geo.Rect(-w / 2, 0, w / 2, top - 3) };
        s.Add([new(-1.0, top - 3.2), new(1.0, top - 3.2), new(0, top)]);
        return (Geo.Solid(s), [[new(-w / 2, top - 3.6), new(w / 2, top - 3.6)]], "Spear");
    }

    static (PathsD, List<PathD>, string) Halberd(double w, double T, double top)
    {
        var s = new PathsD { Geo.Rect(-w / 2, 0, w / 2, top - 2.5), new PathD { new(-1.0, top - 2.8), new(1.0, top - 2.8), new(0, top) } };
        // An axe blade one side, a hook the other.
        s.Add([new(w / 2 - 0.1, top - 6), new(3.2, top - 6.8), new(3.0, top - 3.2), new(w / 2 - 0.1, top - 3.8)]);
        s.Add([new(-w / 2 + 0.1, top - 5.5), new(-2.2, top - 4.5), new(-w / 2 + 0.1, top - 4.2)]);
        return (Geo.Solid(s), [], "Halberd");
    }

    static (PathsD, List<PathD>, string) Axe(double w, double T, double top)
    {
        var s = new PathsD { Geo.Rect(-w / 2, 0, w / 2, top) };
        var head = new PathD { new(w / 2 - 0.1, top - 3.6), new(2.4, top - 4.6), new(3.0, top - 2.4), new(2.4, top - 0.2), new(w / 2 - 0.1, top - 1.2) };
        s.Add(head);
        return (Geo.Solid(s), [[new(2.6, top - 4.2), new(2.9, top - 0.6)]], "Axe");
    }

    /// <summary>A sword point-down through the holder, its crossguard resting above it.</summary>
    static (PathsD, List<PathD>, string) SwordUp(double w, double T, double hh, double top)
    {
        double guard = hh + T + 0.4;
        var s = new PathsD
        {
            Geo.Rect(-w / 2, 0, w / 2, guard),
            Geo.Rect(-1.8, guard - 0.01, 1.8, guard + 0.9),
            Geo.Rect(-0.55, guard + 0.89, 0.55, top - 1.0),
            Geo.Circle(0, top - 0.75, 0.75),
        };
        return (Geo.Solid(s), [[new(0, T + 0.5), new(0, guard - 0.3)]], "Sword");
    }

    static (PathsD, List<PathD>, string) Rifle(double w, double T, double hh, double top)
    {
        // Barrel up: a butt on the floor (narrowed to the tab), the body with a magazine, the barrel.
        double body0 = hh + T + 0.6, body1 = body0 + Math.Min(6, (top - body0) * 0.45);
        var s = new PathsD
        {
            Geo.Rect(-w / 2, 0, w / 2, body0 + 0.1),
            Fronts.Chamfered(-1.2, body0, 1.2, body1, 0.5),
            Geo.Rect(1.1, body0 + 1.0, 2.4, body0 + 2.6),
            Geo.Rect(-0.5, body1 - 0.01, 0.5, top),
        };
        return (Geo.Solid(s), [[new(-1.2, body0 + 1.0), new(1.2, body0 + 1.0)], WoodFace.Dot(0, body1 - 1.2, 0.3)], "Rifle");
    }

    // ---- stocks ----

    public static void Stocks(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double pw = Math.Max(4.4, sw + 2.3), Li = W - 2 * T, footH = 2.4;
        double bh = Geo.Clamp(H * 0.24, 5, 8), yb = H - bh - 2.5;
        double rh = 2.6, yr = footH + 2.5;
        if (Li < 14) { m.Bad($"The stocks are too narrow: make them at least {2 * T + 14:0.#} mm wide."); return; }
        if (D < pw + 2) { m.Bad($"The stocks are too shallow: make them at least {pw + 2:0.#} mm deep."); return; }
        if (yb < yr + rh + 4) { m.Bad("The stocks are too low: make them at least 20 mm high."); return; }
        double zc = D / 2;
        // The posts: a foot along the depth with braces, the post up the middle.
        var post = new PathsD
        {
            WoodFace.RoundRect(0, 0, D, footH, 0.8),
            Shapes.Topped(BackTop.Rounded, zc - pw / 2, 0, zc + pw / 2, H, pw / 2),
            new PathD { new(zc - pw / 2 - 2.5, footH - 0.01), new(zc - pw / 2 + 0.01, footH - 0.01), new(zc - pw / 2 + 0.01, footH + 2.5) },
            new PathD { new(zc + pw / 2 + 2.5, footH - 0.01), new(zc + pw / 2 - 0.01, footH - 0.01), new(zc + pw / 2 - 0.01, footH + 2.5) },
        };
        double bth = bh - 2.4, rth = rh - 1.2;
        var ph = new PathsD
        {
            Geo.Rect(zc - sw / 2, yb + bh / 2 - (bth + f) / 2, zc + sw / 2, yb + bh / 2 + (bth + f) / 2),
            Geo.Rect(zc - sw / 2, yr + rh / 2 - (rth + f) / 2, zc + sw / 2, yr + rh / 2 + (rth + f) / 2),
        };
        m.Add("Post", Geo.Solid(post), ph, [Pose.Left(T / 2, 0, 0), Pose.Left(W - T / 2, 0, 0)], grainAlongU: false);
        // The board: holes for the head and hands, split along their middles, hinged on one end and locked on the other.
        var board = new PathsD { Geo.Rect(0, 0, Li, bh), Geo.Rect(-T, bh / 2 - bth / 2, 0.01, bh / 2 + bth / 2), Geo.Rect(Li - 0.01, bh / 2 - bth / 2, Li + T, bh / 2 + bth / 2) };
        double rHead = Math.Min(1.8, bh * 0.3), rHand = Math.Min(1.0, bh * 0.18);
        var holes = new PathsD { Geo.Circle(Li / 2, bh / 2, rHead), Geo.Circle(Li * 0.2, bh / 2, rHand), Geo.Circle(Li * 0.8, bh / 2, rHand) };
        var lines = new List<PathD> { new() { new(0, bh / 2), new(Li, bh / 2) } };
        var etched = new PathsD { Geo.Rect(0.6, bh / 2 + 0.6, 2.0, bh - 0.6), Geo.Rect(0.6, 0.6, 2.0, bh / 2 - 0.6) };
        lines.Add(Geo.Loop(Geo.Rect(Li - 2.2, bh / 2 - 1.2, Li - 0.8, bh / 2 + 1.2)));
        lines = Geo.ClipLines(lines, Geo.Subtract([Geo.Rect(0, 0, Li, bh)], holes));
        m.Add("Board", board, holes, [Pose.Front(T, yb, zc)], grainAlongU: true, etched: etched, lines: lines);
        var railP = new PathsD { Geo.Rect(0, 0, Li, rh), Geo.Rect(-T, rh / 2 - rth / 2, 0.01, rh / 2 + rth / 2), Geo.Rect(Li - 0.01, rh / 2 - rth / 2, Li + T, rh / 2 + rth / 2) };
        m.Add("Rail", railP, [], [Pose.Front(T, yr, zc)], grainAlongU: true);
        m.Note("Push the board and the rail into one post, then put the other post on.");
    }
}
