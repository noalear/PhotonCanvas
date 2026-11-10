using static PhotonCanvas.Addon.Borders.Carve;

namespace PhotonCanvas.Addon.Borders;

/// <summary>Where a point is within one repeat of an ornament, and the band's size.</summary>
/// <param name="P">One repeat's length along the band (mm).</param>
/// <param name="W">The band's width across (mm).</param>
/// <param name="Count">Strands, lines or rows, for the ornaments that have several.</param>
public readonly record struct OrnamentScale(double P, double W, int Count);

/// <summary>What an ornament is called, what it's like, and how long one repeat is for its width.</summary>
public sealed record OrnamentInfo(Ornament Kind, string Name, string About, double Aspect, bool UsesCount = false);

/// <summary>
/// The ornaments, each drawn as a height across one repeat: 1 is the band's surface, 0 is carved
/// down by the band's carve depth. Every one is its own original drawing, made from simple shapes
/// (domes, rounded ridges, grooves) so that it carves cleanly in slices. A repeat is centred on its
/// motif (u = 0) and most motifs are symmetric about it, so mitred corners mirror neatly.
/// </summary>
public static class Ornaments
{
    public static readonly IReadOnlyList<OrnamentInfo> All =
    [
        new(Ornament.None, "None", "Plain: just the profile.", 1),
        new(Ornament.EggAndDart, "Egg and dart", "Eggs in cups with arrow-head darts between: the classic carving on an ovolo.", 0.9),
        new(Ornament.Dentil, "Dentils", "A row of square blocks like teeth, under a cornice or along a crown moulding.", 0.75),
        new(Ornament.BeadAndReel, "Bead and reel", "Long beads with pairs of thin discs between, on an astragal.", 1.5),
        new(Ornament.Pearls, "Pearls", "A string of round beads: pearling, beading.", 1.0),
        new(Ornament.Rope, "Rope", "Twisted strands, as on a rope or cable moulding.", 0.9, true),
        new(Ornament.BarleyTwist, "Barley twist", "Two deep strands twisting round, as on a barley-twist leg.", 1.6),
        new(Ornament.GreekKey, "Greek key", "Square spirals hooked off a running line: the key pattern.", 8.0 / 7.0),
        new(Ornament.Fret, "Meander", "One line folding back on itself: the simple Greek fret.", 1.6),
        new(Ornament.WaveScroll, "Wave scroll", "A wavy stem curling off into spirals above and below it: a running scroll.", 1.5),
        new(Ornament.Vitruvian, "Vitruvian scroll", "Waves rising from a base and breaking forward into spirals: the running dog.", 1.3),
        new(Ornament.Guilloche, "Guilloche", "Two strands weaving over and under round a row of studs.", 2.0),
        new(Ornament.Plait, "Plait", "Strands woven over and under, as Celtic knotwork does (2 to 5 strands).", 1.5, true),
        new(Ornament.Acanthus, "Acanthus", "Upright lobed acanthus leaves with a smaller leaf between.", 0.8),
        new(Ornament.LeafAndDart, "Leaf and dart", "Heart-shaped leaves pointing down with darts between: the Lesbian leaf on a reverse ogee.", 0.95),
        new(Ornament.WaterLeaf, "Water leaf", "Long pointed leaves with a raised rib, alternating with narrow ones: tongue and dart.", 0.7),
        new(Ornament.Palmette, "Palmette", "Fans of petals on a pair of scrolls: the anthemion.", 1.1),
        new(Ornament.Laurel, "Laurel", "Overlapping bay leaves with berries, as on a laurel garland.", 0.5),
        new(Ornament.Rosettes, "Rosettes", "Round flowers (paterae) with pearls between: for friezes and corner blocks.", 1.4),
        new(Ornament.Shells, "Shells", "Scallop shells with ribbed fans.", 1.1),
        new(Ornament.Chevron, "Chevron", "A bold zigzag, Norman or Art Deco.", 1.0),
        new(Ornament.Herringbone, "Herringbone", "Two rows of slanting bars leaning opposite ways.", 0.7),
        new(Ornament.Basketweave, "Basketweave", "Strands woven in a checkerboard, three at a time.", 1.0),
        new(Ornament.Billet, "Billet", "Short round rods in two staggered rows: Romanesque.", 1.0),
        new(Ornament.Dogtooth, "Dogtooth", "Four-pointed pyramids in a row: Early English Gothic.", 1.0),
        new(Ornament.Nailhead, "Nailhead", "Small square pyramids in a row, like nail heads.", 1.0),
        new(Ornament.Gadroon, "Gadroons", "Rounded lobes side by side across the band, as on a table or tray edge.", 0.45),
        new(Ornament.TwistedGadroon, "Twisted gadroons", "Gadroons swept into slanting S curves.", 0.45),
        new(Ornament.FlutesAcross, "Fluted frieze", "Short hollow flutes side by side across the band.", 0.4),
        new(Ornament.Arcade, "Interlaced arches", "Round arches on columns overlapping into pointed ones: a Romanesque arcade.", 1.0),
        new(Ornament.FishScale, "Fish scale", "Overlapping round scales in rows, each row lying over the next (imbrication).", 1.0),
        new(Ornament.IvyVine, "Ivy vine", "A winding stem with ivy leaves and curling tendrils.", 2.0),
        new(Ornament.Lozenge, "Lozenges", "Faceted diamonds on a bar.", 1.3),
        new(Ornament.Bamboo, "Bamboo", "A bamboo cane with its rings.", 4.0),
        new(Ornament.RibbonAndStick, "Ribbon and stick", "A ribbon wound round a rod.", 1.2),
        new(Ornament.Quatrefoil, "Quatrefoils", "Four-lobed Gothic openings with a boss in each.", 1.0),
        new(Ornament.Hearts, "Hearts", "A row of hearts: folk and country.", 1.0),
        new(Ornament.Stars, "Stars", "A row of faceted five-pointed stars.", 1.0),
        new(Ornament.Ripple, "Ripple", "Fine wavy ridges across the band: a Dutch ripple moulding.", 0.16),
        new(Ornament.Waves, "Waves", "Wavy lines running along the band.", 1.6, true),
        new(Ornament.Hammered, "Hammered", "A hand-hammered texture of shallow, overlapping dimples.", 6.0),
    ];

    static readonly Dictionary<Ornament, OrnamentInfo> ByKind = All.ToDictionary(o => o.Kind);

    public static OrnamentInfo Info(Ornament o) => ByKind.TryGetValue(o, out var i) ? i : All[0];

    /// <summary>
    /// The height of ornament <paramref name="o"/> at <paramref name="u"/> repeats along the band and
    /// <paramref name="v"/> across it (0 = its outer side, 1 = its inner side): 1 = surface, 0 = carved.
    /// </summary>
    public static double Height(Ornament o, double u, double v, in OrnamentScale s)
    {
        double f = Centred(u), x = f * s.P, y = v * s.W;
        // Kept to 0…1: a few shapes overshoot a hair where their pieces overlap.
        return Sat(o switch
        {
            Ornament.None => 1,
            Ornament.EggAndDart => EggAndDart(x, y, s),
            Ornament.Dentil => Dentil(x, y, s),
            Ornament.BeadAndReel => BeadAndReel(x, y, s),
            Ornament.Pearls => Pillow(Circle(x, y - s.W / 2, 0.45 * Math.Min(s.P, s.W)), 0.45 * Math.Min(s.P, s.W)),
            Ornament.Rope => Rope(u, v, s, Math.Clamp(s.Count, 2, 6) / 3.0 * 0.8, 0.15, 0.7),
            Ornament.BarleyTwist => Rope(u, v, s, 1.25, 0.15, 0.35),
            Ornament.GreekKey => Key(u, v, s, GreekKeyPath, 8, 7),
            Ornament.Fret => Key(u, v, s, FretPath, 8, 5),
            Ornament.WaveScroll => RunningScroll(u, v, s),
            Ornament.Vitruvian => Vitruvian(u, v, s),
            Ornament.Guilloche => Guilloche(u, v, s),
            Ornament.Plait => Plait(u, v, s),
            Ornament.Acanthus => Acanthus(x, y, s),
            Ornament.LeafAndDart => LeafAndDart(x, y, s),
            Ornament.WaterLeaf => WaterLeaf(x, y, s),
            Ornament.Palmette => Palmette(x, y, s),
            Ornament.Laurel => Laurel(u, v, s),
            Ornament.Rosettes => Rosettes(x, y, s),
            Ornament.Shells => Shell(x, y, s),
            Ornament.Chevron => Chevron(u, v, s),
            Ornament.Herringbone => Herringbone(u, v, s),
            Ornament.Basketweave => Basketweave(u, v, s),
            Ornament.Billet => Billet(u, v, s),
            Ornament.Dogtooth => Dogtooth(x, y, s),
            Ornament.Nailhead => Nailhead(x, y, s),
            Ornament.Gadroon => Gadroon(x, y, s),
            Ornament.TwistedGadroon => Gadroon(Centred(u - 0.55 * Math.Sin(Math.PI * (v - 0.5))) * s.P, y, s),
            Ornament.FlutesAcross => FlutesAcross(x, y, s),
            Ornament.Arcade => Arcade(u, v, s),
            Ornament.FishScale => FishScale(u, v, s),
            Ornament.IvyVine => IvyVine(u, v, s),
            Ornament.Lozenge => Lozenge(x, y, s),
            Ornament.Bamboo => Bamboo(x, y, s),
            Ornament.RibbonAndStick => RibbonAndStick(u, v, s),
            Ornament.Quatrefoil => Quatrefoil(x, y, s),
            Ornament.Hearts => Hearts(x, y, s),
            Ornament.Stars => Stars(x, y, s),
            Ornament.Ripple => Ripple(u, v),
            Ornament.Waves => Waves(u, v, s),
            Ornament.Hammered => Hammered(u, v, s),
            _ => 1,
        });
    }

    // ---- the classical mouldings' ornaments ----

    static double EggAndDart(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double top = 0.1 * W;                       // a fillet along the top, which the cups hang from
        double ey = 0.52 * W, a = 0.27 * P, b = 0.36 * W;
        double egg = Egg(x, y - ey, a, b, 0.4);
        double h = 0.25 + 0.75 * Pillow(egg, 0.75 * Math.Min(a, b));
        h = egg < 0 ? h : 0;
        // The cup round each egg: a rounded rim, open at the top where it meets the fillet.
        double gap = 0.035 * P, rim = 0.06 * P;
        double mid = Egg(x, y - ey, a + gap + rim / 2, b + gap + rim / 2, 0.4);
        double cup = 0.8 * Ridge(mid, rim / 2) * Smooth(ey - 0.95 * b, ey - 0.4 * b, y);
        // The upper part of the cup flares into the fillet.
        if (y < ey && Math.Abs(mid) < rim / 2 + gap) cup = Math.Max(cup, 0.8 * Ridge(mid, rim / 2));
        h = Math.Max(h, cup);
        // The dart: an arrow head on a shaft, between eggs.
        double dx = Math.Abs(x) - P / 2;
        double shaft = Pillow(Box(dx, y - 0.45 * W, 0.035 * P, 0.33 * W), 0.03 * P) * 0.75;
        Span<double> head = [-0.11 * P, 0.62 * W, 0.11 * P, 0.62 * W, 0, 0.95 * W];
        double arrow = Pillow(Polygon(dx, y, head), 0.05 * P) * 0.85;
        h = Math.Max(h, Math.Max(shaft, arrow));
        // The fillet.
        h = Math.Max(h, Mask(y - top, 0.02 * W));
        return h;
    }

    static double Dentil(double x, double y, in OrnamentScale s)
    {
        double r = Math.Min(0.08 * s.P, 0.06 * s.W);
        return Pillow(Box(x, y - 0.5 * s.W, 0.31 * s.P, 0.5 * s.W + r), r);
    }

    static double BeadAndReel(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W, yc = y - W / 2;
        double bead = Pillow(Ellipse(x, yc, 0.33 * P, 0.44 * W), 0.44 * W);
        double rx = Math.Abs(Math.Abs(x) - P / 2);
        double reel = Math.Max(Pillow(Ellipse(rx - 0.065 * P, yc, 0.04 * P, 0.4 * W), 0.04 * P),
                               Pillow(Ellipse(rx + 0.065 * P, yc, 0.04 * P, 0.4 * W), 0.04 * P)) * (0.55 + 0.35 * Ridge(yc, 0.42 * W));
        return Math.Max(bead, reel);
    }

    /// <summary>
    /// Strands twisting round a rod, seen from the front: their edges are helices, so they slant
    /// across the rope and curve round at its sides. <paramref name="slant"/> = repeats per half turn.
    /// </summary>
    static double Rope(double u, double v, in OrnamentScale s, double slant, double groove, double sharp)
    {
        double c = 2 * v - 1;
        if (Math.Abs(c) >= 1) return 0;
        double round = Math.Sqrt(1 - c * c);
        double theta = Math.Asin(c);
        double phi = u + slant * theta / Math.PI;
        double lobe = Math.Pow(Math.Sin(Math.PI * Frac(phi)), sharp);
        return Math.Pow(round, 0.7) * (groove + (1 - groove) * lobe);
    }

    // The key patterns' lines, in grid squares (x along, y across, line centres), one repeat each.
    static readonly double[] GreekKeyPath = [4.5, 2.5, 2.5, 2.5, 2.5, 4.5, 6.5, 4.5, 6.5, 0.5, 0.5, 0.5, 0.5, 6.5, 8.5, 6.5];
    static readonly double[] FretPath = [0.5, 4.5, 0.5, 0.5, 6.5, 0.5, 6.5, 2.5, 2.5, 2.5, 2.5, 4.5, 8.5, 4.5];

    /// <summary>A key pattern: a raised square line on a grid <paramref name="gw"/> squares long and <paramref name="gh"/> high.</summary>
    static double Key(double u, double v, in OrnamentScale s, double[] path, int gw, int gh)
    {
        double ux = s.P / gw, uy = s.W / gh;
        double gx = Frac(u) * gw, gy = v * gh;
        double best = double.MaxValue;
        Span<double> pts = stackalloc double[path.Length];
        for (int shift = -1; shift <= 0; shift++)
        {
            for (int i = 0; i < path.Length; i += 2)
            {
                pts[i] = (path[i] + shift * gw) * ux;
                pts[i + 1] = path[i + 1] * uy;
            }
            best = Math.Min(best, Polyline(gx * ux, gy * uy, pts));
        }
        double half = 0.5 * Math.Min(ux, uy);
        return Pillow(best - half, 0.45 * half);
    }

    /// <summary>
    /// The Vitruvian scroll (running dog), two-tone as it's carved: the "water" is a base along the
    /// inner side and, rising from it, a tongue that climbs the wave's long back, goes over the crest
    /// and curls forward into a tightening spiral. The carved background is the other tongue, curling
    /// in from the trough under the crest. The water stands up with rounded edges.
    /// </summary>
    static double Vitruvian(double u, double v, in OrnamentScale s)
    {
        double P = s.P, H = s.W;
        double x = Centred(u) * P, y = v * H;
        double curls = double.MaxValue;
        for (int shift = -1; shift <= 1; shift++)
            curls = Math.Min(curls, WaveCurl(x - shift * P, y, H));
        // Blended into the base, so each wave sweeps up out of it and the troughs are rounded.
        double water = SoftMin(WaveBase * H - y, curls, 0.14 * H);
        return Pillow(water, 0.05 * H);
    }

    const double WaveBase = 0.8;

    /// <summary>
    /// The curling tongue of one wave (its spiral centred just right of x = 0), a signed distance roughly
    /// in mm: it rises thick out of the base on the left, climbs over the crest and curls in, thinning.
    /// </summary>
    static double WaveCurl(double x, double y, double H)
    {
        const double Cx = 0.12, Cy = 0.45, Lean = 0.12, Back = 1.7;
        const double R0 = 0.41, Gap = 0.24, Turns = 1.35, Start = 0.6 * Math.PI, Flare = 0.2;
        const double End = Start + Turns * 2 * Math.PI;
        // Into the spiral's own frame: leaning forward at the crest, its back drawn out long to the left.
        double ly = y / H - Cy, lx = x / H - Cx + Lean * ly;
        if (lx < 0) lx /= 1 + (Back - 1) * Smooth(0, 0.45, -lx);
        double r = Math.Sqrt(lx * lx + ly * ly), a = Math.Atan2(ly, lx);
        double best = double.MaxValue;
        // Each turn of the arm that passes this way: its middle runs in by Gap a turn, and it flares
        // out and thickens where it rises out of the base.
        for (int k = (int)Math.Ceiling((Start - a) / (2 * Math.PI)); ; k++)
        {
            double phi = a + 2 * Math.PI * k;
            if (phi > End) break;
            double t = (phi - Start) / (End - Start), f = Sat(1 - (phi - Start) / (Math.PI / 2));
            double rc = R0 - Gap * (phi - Start) / (2 * Math.PI) + Flare * f * f;
            double half = Gap * (0.27 - 0.08 * t) + 0.04 * f;
            best = Math.Min(best, Math.Abs(r - rc) - half);
        }
        // A round end in the middle of the curl.
        double re = R0 - Gap * Turns, ex = lx - re * Math.Cos(End), ey = ly - re * Math.Sin(End);
        best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey) - Gap * 0.19);
        return best * H;
    }

    /// <summary>
    /// A running scroll: a stem waving along the middle of the band, and in each hollow of the wave a
    /// tendril peeling off it and curling into a spiral, above and below in turn, ending in a small boss.
    /// </summary>
    static double RunningScroll(double u, double v, in OrnamentScale s)
    {
        double P = s.P, H = s.W;
        double f = Frac(u), y = v / 1.0;
        const double A = 0.15, Half = 0.045;
        // The stem, a sine wave (its distance across, allowing for its slope).
        double slope = A * 2 * Math.PI * Math.Cos(2 * Math.PI * f) * H / P;
        double stem = (Math.Abs(y - (0.5 + A * Math.Sin(2 * Math.PI * f))) / Math.Sqrt(1 + slope * slope) - Half) * H;
        // The tendrils: the one above the stem's low point, and the same turned round under its high point.
        double curls = double.MaxValue;
        for (int shift = -1; shift <= 1; shift++)
        {
            double x = (f + shift) * P / H;
            curls = Math.Min(curls, Tendril(x - 0.25 * P / H, y - (0.5 + A)));
            curls = Math.Min(curls, Tendril(0.75 * P / H - x, 0.5 - A - y));
        }
        double d = Math.Min(stem, curls * H);
        return Pillow(d, 0.05 * H);
    }

    /// <summary>
    /// One tendril of the running scroll, in band widths, from the stem's low point at (0, 0): it leaves
    /// the stem going forward, rises and curls back over into a tightening spiral. A signed distance.
    /// </summary>
    static double Tendril(double x, double y)
    {
        const double R0 = 0.32, Gap = 0.13, Turns = 1.6, Start = Math.PI / 2;
        const double End = Start - Turns * 2 * Math.PI;
        // Its spiral is centred R0 above where it leaves the stem, turning anticlockwise as seen.
        double lx = x, ly = y + R0;
        double r = Math.Sqrt(lx * lx + ly * ly), a = Math.Atan2(ly, lx);
        double best = double.MaxValue;
        for (int k = (int)Math.Floor((Start - a) / (2 * Math.PI)); ; k--)
        {
            double phi = a + 2 * Math.PI * k;
            if (phi > Start) continue;
            if (phi < End) break;
            double t = (Start - phi) / (Start - End);
            double rc = R0 - Gap * (Start - phi) / (2 * Math.PI), half = 0.045 - 0.012 * t;
            best = Math.Min(best, Math.Abs(r - rc) - half);
        }
        // A small round boss where it ends.
        double re = R0 - Gap * Turns, ex = lx - re * Math.Cos(End), ey = ly - re * Math.Sin(End);
        return Math.Min(best, Math.Sqrt(ex * ex + ey * ey) - 0.06);
    }

    /// <summary>
    /// Strands weaving over and under. Strand i follows y = 0.5 + a·sin(2π(u + i/n)) (or, for the
    /// guilloche's two, ±a·cos(2πu)); where strands cross, the one heading toward the inner side goes
    /// over, so the crossings alternate. Strands lie higher where they pass over, lower where under.
    /// </summary>
    static double Strands(double u, double v, in OrnamentScale s, int n, bool guilloche, double a, double half)
    {
        double best = 0, bestZ = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            double yi, dy;
            if (guilloche)
            {
                double k = i == 0 ? 1 : -1, w = 2 * Math.PI * u;
                yi = 0.5 + k * a * Math.Cos(w);
                dy = -k * a * 2 * Math.PI * Math.Sin(w);
            }
            else
            {
                double w = 2 * Math.PI * (u + i / (double)n);
                yi = 0.5 + a * Math.Sin(w);
                dy = a * 2 * Math.PI * Math.Cos(w);
            }
            // Distance across the strand: the gap across times the cosine of its slope.
            double slope = dy * s.W / s.P;
            double d = Math.Abs(v - yi) * s.W / Math.Sqrt(1 + slope * slope);
            double r = Ridge(d, half);
            if (r <= 0) continue;
            double z = dy / (Math.Abs(dy) + 0.6 * a * 2 * Math.PI);
            if (z <= bestZ) continue;
            bestZ = z;
            best = (0.72 + 0.28 * z) * (0.5 + 0.5 * r);
        }
        return best;
    }

    static double Guilloche(double u, double v, in OrnamentScale s)
    {
        double h = Strands(u, v, s, 2, true, 0.3, 0.11 * s.W);
        // A domed stud in each eye.
        double x = Centred(u * 2) * s.P / 2, y = (v - 0.5) * s.W;
        double stud = 0.8 * Pillow(Circle(x, y, 0.16 * s.W), 0.16 * s.W);
        return Math.Max(h, stud);
    }

    static double Plait(double u, double v, in OrnamentScale s)
    {
        int n = Math.Clamp(s.Count, 2, 5);
        return Strands(u, v, s, n, false, 0.36, (0.32 / n + 0.02) * s.W);
    }

    // ---- leaves ----

    /// <summary>
    /// An acanthus leaf standing up from the band's inner side, its tip curling over, its edge cut into
    /// lobes, a raised rib up the middle between two channels, and veins out to the lobes.
    /// </summary>
    static double AcanthusLeaf(double x, double y, double width, double height, double baseY, double lean)
    {
        double t = (baseY - y) / height;                    // 0 at the base, 1 at the tip
        if (t < -0.02 || t > 1.05) return 0;
        double tc = Math.Clamp(t, 0, 1);
        double xc = lean * width * tc * tc * tc;            // the tip leans over
        double env = Math.Sin(Math.PI * Math.Pow(tc, 0.75)) * (1 - 0.15 * tc);
        double lobes = 0.74 + 0.26 * Math.Pow(Math.Abs(Math.Sin(Math.PI * 3.5 * tc + 0.4)), 0.6);
        double w = width * env * lobes;
        if (w <= 1e-6) return 0;
        double dx = x - xc;
        double k = Math.Abs(dx) / w;
        if (k >= 1) return 0;
        double dome = Math.Sqrt(1 - k * k);
        double h = 0.35 + 0.65 * Math.Pow(dome, 0.6);
        // Channels either side of the rib, and the veins running up and out to each lobe.
        double ch = 1 - 0.3 * Ridge(Math.Abs(dx) - 0.16 * width * env, 0.07 * width);
        double vein = Frac(3.5 * tc + 0.4 - 0.55 * k);
        double vg = 1 - 0.22 * Ridge(Math.Min(vein, 1 - vein), 0.08) * Smooth(0.15, 0.35, k);
        // Deep "eyes" where the lobes part.
        double notch = Frac(3.5 * tc + 0.4 + 0.5);
        double eye = 1 - 0.45 * Ridge(Math.Min(notch, 1 - notch), 0.1) * Smooth(0.6, 0.9, k);
        return h * ch * vg * eye;
    }

    static double Acanthus(double x, double y, in OrnamentScale s)
    {
        double big = AcanthusLeaf(x, y, 0.46 * s.P, 0.92 * s.W, 0.98 * s.W, 0.25);
        double small = 0.65 * AcanthusLeaf(Math.Abs(x) - s.P / 2, y, 0.28 * s.P, 0.6 * s.W, 0.98 * s.W, 0);
        return Math.Max(big, small);
    }

    static double LeafAndDart(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double cy = 0.5 * W, hw = 0.82 * P, hh = 0.88 * W;
        // The leaf's outline is a raised rim; inside it a smaller leaf with a groove down its middle.
        double outer = Heart(x, y - cy, hw, hh);
        double inner = Heart(x, y - cy - 0.02 * W, hw * 0.78, hh * 0.78);
        double rim = 0.8 * Ridge(Heart(x, y - cy, hw * 0.92, hh * 0.92), 0.05 * P);
        double leaf = Pillow(inner, 0.12 * P) * (0.75 + 0.25 * Ridge(x, 0.4 * P)) * (1 - 0.35 * Ridge(x, 0.03 * P) * Smooth(0.15 * W, 0.35 * W, y));
        double h = Math.Max(rim * Mask(outer, 0.01 * P), leaf);
        // Darts between the leaves.
        double dx = Math.Abs(x) - P / 2;
        double dart = 0.75 * Pillow(Leaf(dx, y, 0.18 * W, 0.98 * W, 0.07 * P), 0.05 * P);
        return Math.Max(h, dart);
    }

    static double WaterLeaf(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double leaf = Leaf(x, y, 0.04 * W, 0.97 * W, 0.36 * P);
        double h = Pillow(leaf, 0.3 * P) * 0.75;
        double rib = Ridge(x, 0.06 * P) * Mask(leaf, 0.01 * P);
        h = Math.Max(h, 0.6 + 0.4 * rib) * Mask(leaf, 0.01 * P);
        double dart = 0.7 * Pillow(Leaf(Math.Abs(x) - P / 2, y, 0.25 * W, 0.97 * W, 0.08 * P), 0.06 * P);
        return Math.Max(h, dart);
    }

    static double Palmette(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double bx = 0, by = 0.86 * W;
        double h = 0;
        // Seven petals fanning up from the base.
        for (int k = -3; k <= 3; k++)
        {
            double ang = k * 0.36;
            double len = 0.78 * W * (1 - 0.1 * Math.Abs(k)) * (Math.Abs(k) == 3 ? 0.8 : 1);
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            // Into the petal's frame: along it is up, turned by ang.
            double px = x - bx, py = y - by;
            double lx = px * ca + py * sa, ly = -px * sa + py * ca;
            double petal = Leaf(lx, ly, -len, -0.08 * W, 0.075 * P + 0.02 * W);
            h = Math.Max(h, Pillow(petal, 0.06 * P + 0.03 * W) * (0.85 + 0.15 * (1 - Math.Abs(k) / 3.0)));
        }
        // The heart at the base, and two scrolls curling out either side of it.
        h = Math.Max(h, Pillow(Circle(x - bx, y - by, 0.09 * W), 0.09 * W));
        double sx = Math.Abs(x) - 0.27 * P, sy = y - 0.85 * W;
        double sr = Math.Sqrt(sx * sx + sy * sy), sang = Math.Atan2(sy, sx);
        double spiral = sr - 0.012 * W * (sang + Math.PI) * 1.3;
        double scroll = 0.8 * Ridge(Centred(spiral / (0.06 * W)) * 0.06 * W, 0.022 * W) * Mask(sr - 0.11 * W, 0.01 * W);
        h = Math.Max(h, scroll);
        // A stem joining the scrolls along the bottom.
        h = Math.Max(h, 0.7 * Ridge(y - 0.95 * W, 0.03 * W));
        return h;
    }

    static double Laurel(double u, double v, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double y = (v - 0.5) * W;
        double best = 0, bestLayer = double.MinValue;
        for (int k = -2; k <= 1; k++)
        {
            double x = (Frac(u) - k) * P;               // from the leaf pair's base
            for (int side = -1; side <= 1; side += 2)
            {
                double ang = side * 0.45;
                double ca = Math.Cos(ang), sa = Math.Sin(ang);
                double lx = x * ca + y * sa, ly = -x * sa + y * ca;
                double len = Math.Min(1.4 * P, 0.74 * s.W), half = 0.13 * W;
                double leaf = Leaf(ly, lx, 0.0, len, half);
                if (leaf >= 0) continue;
                double layer = k * 2 + (side > 0 ? 1 : 0);
                if (layer <= bestLayer) continue;
                double dome = Pillow(leaf, half);
                double rib = 1 - 0.25 * Ridge(ly, 0.025 * W + 0.01 * P) * Smooth(0.05, 0.2, lx / len);
                best = (0.55 + 0.45 * dome) * rib;
                bestLayer = layer;
            }
        }
        // Berries tucked between the pairs.
        double bx = Centred(u - 0.5) * P;
        double berry = Pillow(Circle(bx, y, 0.09 * W), 0.09 * W);
        return Math.Max(best, berry);
    }

    /// <summary>A round flower: petals round a domed centre, a groove down each petal.</summary>
    public static double Rosette(double x, double y, double r, int petals = 8)
    {
        double d = Math.Sqrt(x * x + y * y);
        if (d > r * 1.05) return 0;
        double a = Math.Atan2(y, x);
        double edge = r * (0.78 + 0.22 * Math.Pow(Math.Abs(Math.Cos(petals * a / 2)), 1.5));
        double petal = Pillow(d - edge, 0.45 * r);
        // Each petal rounded across, highest along its middle.
        double pv = 0.75 + 0.25 * Math.Pow(Math.Abs(Math.Cos(petals * a / 2)), 0.8);
        double h = 0.25 + 0.6 * petal * pv;
        h = petal > 0 ? h : 0;
        // The second ring of smaller petals and the button in the middle.
        double inner = r * 0.48 * (0.8 + 0.2 * Math.Abs(Math.Cos(petals * (a + Math.PI / petals) / 2)));
        h = Math.Max(h, (0.55 + 0.35 * Pillow(d - inner, 0.3 * r)) * Mask(d - inner, 0.01 * r));
        h = Math.Max(h, Pillow(d - 0.2 * r, 0.2 * r));
        return h;
    }

    static double Rosettes(double x, double y, in OrnamentScale s)
    {
        double r = 0.44 * Math.Min(s.W, s.P * 0.75);
        double rose = Rosette(x, y - s.W / 2, r);
        double pearl = Pillow(Circle(Math.Abs(x) - s.P / 2, y - s.W / 2, 0.11 * s.W), 0.11 * s.W);
        return Math.Max(rose, pearl);
    }

    static double Shell(double x, double y, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double hx = x, hy = y - 0.9 * W;
        double r = Math.Sqrt(hx * hx + hy * hy), a = Math.Atan2(hx, -hy);   // 0 straight up
        double R = Math.Min(0.8 * W, 0.5 * P);
        if (Math.Abs(a) > 1.45) return Math.Max(0, Ears(hx, hy, s));
        const int ribs = 11;
        double rib = Math.Abs(Math.Cos(ribs * a / 2 * 2));
        double edge = R * (0.93 + 0.07 * rib);
        double fan = Pillow(r - edge, 0.5 * R);
        double h = fan * (0.72 + 0.28 * Math.Pow(rib, 0.6) * Smooth(0.15 * R, 0.4 * R, r));
        return Math.Max(fan > 0 ? 0.2 + 0.8 * h : 0, Ears(hx, hy, s));

        static double Ears(double hx, double hy, in OrnamentScale s) =>
            0.7 * Pillow(Box(hx, hy + 0.02 * s.W, 0.17 * s.P, 0.06 * s.W), 0.04 * s.W);
    }

    // ---- geometric ----

    static double Chevron(double u, double v, in OrnamentScale s)
    {
        double a = 0.3;
        double tri = 4 * Math.Abs(Frac(u + 0.25) - 0.5) - 1;     // -1…1, peaks at u = 0
        double slope = 4 * a * s.W / s.P;
        double d = Math.Abs(v - 0.5 - a * tri) * s.W / Math.Sqrt(1 + slope * slope);
        double half = 0.13 * s.W;
        return Pillow(d - half, 0.6 * half);
    }

    static double Herringbone(double u, double v, in OrnamentScale s)
    {
        int row = v < 0.5 ? 0 : 1;
        double rv = v * 2 - row;
        double rowH = s.W / 2;
        double lean = row == 0 ? 1 : -1;
        // Bars at 45°: along the bar, x - lean·y is constant.
        double x = u * s.P, y = rv * rowH;
        double along = x - lean * (y - rowH / 2);
        double pitch = s.P / 2;
        double d = Math.Abs(Centred(along / pitch)) * pitch * Math.Sqrt(0.5);
        double bar = Pillow(d - pitch * 0.25, pitch * 0.12);
        double ends = Mask(Math.Abs(rv - 0.5) * rowH - rowH * 0.44, 0.03 * rowH);
        return bar * ends;
    }

    static double Basketweave(double u, double v, in OrnamentScale s)
    {
        int col = (int)Math.Floor(Frac(u) * 2), row = v < 0.5 ? 0 : 1;
        double cu = Frac(u) * 2 - col, cv = v * 2 - row;
        bool along = (col + row) % 2 == 0;
        double a = along ? cu : cv, across = along ? cv : cu;
        // Three strands across the cell; each dips at its ends where it passes under.
        double k = Frac(across * 3);
        double strand = Ridge(k - 0.5, 0.48);
        double dip = 0.65 + 0.35 * Math.Sin(Math.PI * a);
        return strand * dip;
    }

    static double Billet(double u, double v, in OrnamentScale s)
    {
        int row = v < 0.5 ? 0 : 1;
        double rv = v * 2 - row;
        double x = Centred(u + row * 0.5) * s.P;
        double rowH = s.W / 2;
        double y = (rv - 0.5) * rowH;
        double rod = Pillow(RoundBox(x, y, 0.27 * s.P, 0.38 * rowH, 0.38 * rowH), 0.38 * rowH);
        return rod;
    }

    static double Dogtooth(double x, double y, in OrnamentScale s)
    {
        double R = 0.48 * Math.Min(s.P, s.W);
        double yc = y - s.W / 2;
        double r = Math.Sqrt(x * x + yc * yc), a = Math.Atan2(yc, x);
        double reach = R * (0.45 + 0.55 * Math.Pow(Math.Abs(Math.Cos(2 * a)), 2.2));
        if (r >= reach) return 0;
        // A pyramid whose faces are hollowed between the four points.
        double h = 1 - r / reach;
        return Math.Pow(h, 0.8);
    }

    static double Nailhead(double x, double y, in OrnamentScale s)
    {
        double R = 0.42 * Math.Min(s.P, s.W);
        double m = Math.Max(Math.Abs(x), Math.Abs(y - s.W / 2));
        return m >= R ? 0 : 1 - m / R;
    }

    static double Gadroon(double x, double y, in OrnamentScale s)
    {
        double hw = 0.46 * s.P;
        return Pillow(RoundBox(x, y - s.W / 2, hw, 0.47 * s.W, hw), hw);
    }

    static double FlutesAcross(double x, double y, in OrnamentScale s)
    {
        double hw = 0.36 * s.P;
        return 1 - Pillow(RoundBox(x, y - s.W / 2, hw, 0.42 * s.W, hw), hw);
    }

    static double Arcade(double u, double v, in OrnamentScale s)
    {
        double y = v * s.W;
        double cy = 0.6 * s.W, r = Math.Min(0.5 * s.P, 0.5 * s.W);
        double half = 0.05 * s.W + 0.015 * s.P;
        double best = double.MaxValue;
        // Round arches on columns, springing every half repeat and spanning a whole one, so they cross into pointed arches.
        double a0 = Math.Floor(u * 2);
        for (double k = a0 - 2; k <= a0 + 2; k++)
        {
            double dx = u * s.P - k * s.P / 2, dy = y - cy;
            double d = dy < 0 ? Math.Abs(Math.Sqrt(dx * dx + dy * dy) - r) : Math.Abs(Math.Abs(dx) - r);
            best = Math.Min(best, d);
        }
        return Pillow(best - half, 0.6 * half);
    }

    /// <summary>
    /// Fish scales (imbrication): rows of round-ended scales, each row lying over the one below it like
    /// shingles. Each scale rises from where it comes out from under the row above to its own rounded
    /// edge, which stands proud of the scales below.
    /// </summary>
    static double FishScale(double u, double v, in OrnamentScale s)
    {
        double R = s.P / 2, y = v * s.W;
        int rows = Math.Max(2, (int)Math.Round(s.W / (0.6 * R)));
        double rowH = s.W / rows;
        int first = (int)Math.Floor((y - R) / rowH);
        // The highest row whose scale covers this point is on top.
        for (int row = first; row <= first + 3 + (int)Math.Ceiling(2 * R / rowH); row++)
        {
            double cy = (row + 0.5) * rowH - 0.35 * R;
            double x = Centred(u + (((row % 2) + 2) % 2 == 0 ? 0 : 0.5)) * s.P;
            double dy = y - cy, d = Math.Sqrt(x * x + dy * dy);
            if (d >= R) continue;
            // Low where it comes out from under the row above, highest just inside its edge, domed across.
            double rise = Sat((dy - (R - 2 * rowH)) / (2 * rowH));
            double lip = Pillow(d - R, 0.12 * R);
            double across = 1 - 0.15 * (x / R) * (x / R);
            return (0.12 + 0.88 * Math.Pow(rise, 1.3)) * Lerp(0.8, 1, lip) * across;
        }
        return 0.12;
    }

    static double IvyVine(double u, double v, in OrnamentScale s)
    {
        double P = s.P, W = s.W;
        double x = Frac(u) * P, y = v * W;
        double best = 0;
        for (int shift = -1; shift <= 1; shift++)
        {
            double xx = x - shift * P;
            double w = 2 * Math.PI * xx / P;
            double sy = 0.5 * W + 0.18 * W * Math.Sin(w);
            double slope = 0.18 * W * 2 * Math.PI / P * Math.Cos(w);
            double d = Math.Abs(y - sy) / Math.Sqrt(1 + slope * slope);
            best = Math.Max(best, 0.7 * Ridge(d, 0.045 * W + 0.01 * P));
            // A leaf above the stem where it peaks, one below where it dips: points outward.
            for (int side = 0; side < 2; side++)
            {
                double lx = xx - (side == 0 ? 0.25 : 0.75) * P;
                double ly = y - (side == 0 ? 0.31 * W : 0.69 * W);
                double flip = side == 0 ? 1 : -1;
                double heart = Heart(lx, -flip * ly, 0.44 * W, 0.4 * W);
                double leaf = Pillow(heart, 0.12 * W);
                double rib = 1 - 0.25 * Ridge(lx, 0.02 * W) * Mask(heart, 0.01 * W);
                best = Math.Max(best, leaf * rib);
                // Its stalk, from the notch between its lobes to the stem.
                double notchY = (side == 0 ? 0.31 : 0.69) * W + flip * 0.16 * W;
                double stemY = 0.5 * W + 0.18 * W * Math.Sin(2 * Math.PI * (side == 0 ? 0.25 : 0.75));
                best = Math.Max(best, 0.6 * Ridge(Segment(lx, y, 0, notchY, 0, stemY), 0.025 * W));
            }
            // Tendrils: little curls between the leaves.
            for (int side = 0; side < 2; side++)
            {
                double tx = xx - (side == 0 ? 0.0 : 0.5) * P, ty = y - (side == 0 ? 0.73 * W : 0.27 * W);
                double r = Math.Sqrt(tx * tx + ty * ty), a = Math.Atan2(ty, tx) * (side == 0 ? 1 : -1);
                double spiral = r - 0.018 * W * (a + Math.PI);
                double curl = 0.55 * Ridge(Centred(spiral / (0.05 * W)) * 0.05 * W, 0.015 * W) * Mask(r - 0.11 * W, 0.01 * W);
                best = Math.Max(best, curl);
            }
        }
        return best;
    }

    static double Lozenge(double x, double y, in OrnamentScale s)
    {
        double yc = y - s.W / 2;
        double hx = 0.4 * s.P, hy = 0.44 * s.W;
        double k = Math.Abs(x) / hx + Math.Abs(yc) / hy;
        double facet = k >= 1 ? 0 : 0.35 + 0.65 * Math.Min(1, (1 - k) * 2.2);
        // The centre is a small flat table, as on a cut stone.
        if (k < 0.3) facet = 1;
        double bar = 0.6 * Pillow(Math.Abs(yc) - 0.07 * s.W, 0.04 * s.W);
        return Math.Max(facet, bar);
    }

    static double Bamboo(double x, double y, in OrnamentScale s)
    {
        double c = 2 * y / s.W - 1;
        if (Math.Abs(c) >= 1) return 0;
        double round = Math.Pow(1 - c * c, 0.35);
        double ax = Math.Abs(x);
        double node = Ridge(ax, 0.035 * s.W);
        double swell = 0.88 + 0.12 * Smooth(0.5 * s.P, 0.05 * s.P, ax);
        double h = Math.Max(swell, 0.95 + 0.05 * node) - 0.15 * Ridge(ax - 0.06 * s.W, 0.03 * s.W);
        return round * h;
    }

    static double RibbonAndStick(double u, double v, in OrnamentScale s)
    {
        double c = 2 * v - 1;
        if (Math.Abs(c) >= 1) return 0;
        double round = Math.Sqrt(1 - c * c);
        double theta = Math.Asin(c);
        double phi = Frac(u + 0.35 * theta / Math.PI);
        double ribbon = Smooth(0.0, 0.06, phi) * Smooth(0.6, 0.54, phi);
        double rod = 0.62 * Math.Pow(round, 0.5);
        double band = (0.75 + 0.25 * Math.Sin(Math.PI * phi / 0.6)) * Math.Pow(round, 0.3);
        return Math.Max(rod, ribbon * band);
    }

    static double Quatrefoil(double x, double y, in OrnamentScale s)
    {
        double yc = y - s.W / 2;
        double R = 0.44 * Math.Min(s.P, s.W);
        double a = 0.42 * R, rl = 0.5 * R;
        double d = Math.Min(Math.Min(Circle(x - a, yc, rl), Circle(x + a, yc, rl)), Math.Min(Circle(x, yc - a, rl), Circle(x, yc + a, rl)));
        d = Math.Min(d, Box(x, yc, a, a));
        double sunk = 1 - 0.8 * Pillow(d, 0.12 * R);
        double boss = Pillow(Circle(x, yc, 0.24 * R), 0.24 * R);
        return Math.Max(sunk, boss);
    }

    static double Hearts(double x, double y, in OrnamentScale s)
    {
        double size = 0.85 * Math.Min(s.P, s.W);
        double d = Heart(x, y - s.W / 2, size, size * 0.95);
        return Pillow(d, 0.3 * size);
    }

    static double Stars(double x, double y, in OrnamentScale s)
    {
        double R = 0.47 * Math.Min(s.P, s.W);
        double yc = y - s.W / 2 + 0.05 * R;
        double d = Star(x, yc, 5, R, 0.45 * R);
        if (d >= 0) return 0;
        // Faceted: a ridge from the centre to every point and every notch.
        double r = Math.Sqrt(x * x + yc * yc), a = Math.Atan2(yc, x) + Math.PI / 2;
        double k = Frac(a * 5 / (2 * Math.PI));
        double reach = Carve.Lerp(R, 0.45 * R, Math.Abs(k - 0.5) * 2);
        return Math.Max(0.15, 1 - r / Math.Max(1e-6, reach) * 0.85) * Mask(d, 0.02 * R);
    }

    static double Ripple(double u, double v)
    {
        double phi = u + 0.22 * Math.Sin(2 * Math.PI * v * 1.2);
        return 0.5 + 0.5 * Math.Cos(2 * Math.PI * phi);
    }

    static double Waves(double u, double v, in OrnamentScale s)
    {
        int n = Math.Clamp(s.Count, 1, 8);
        double a = 0.28 / n, best = 0;
        for (int k = 0; k < n; k++)
        {
            double w = 2 * Math.PI * u;
            double yc = (k + 0.5) / n + a * Math.Sin(w);
            double slope = a * 2 * Math.PI * Math.Cos(w) * s.W / s.P;
            double d = Math.Abs(v - yc) * s.W / Math.Sqrt(1 + slope * slope);
            best = Math.Max(best, Ridge(d, 0.3 / n * s.W));
        }
        return best;
    }

    /// <summary>
    /// Hand-hammered: shallow round hammer marks of different sizes and depths, overlapping so they meet
    /// in soft ridges. They're scattered over a long repeat (several band widths) that wraps round, so
    /// sides, mitres and loops join without a seam and the repeat is hard to see.
    /// </summary>
    static double Hammered(double u, double v, in OrnamentScale s)
    {
        const int Rows = 6;
        int cols = Math.Max(3, (int)Math.Round(Rows * s.P / s.W));
        double cw = s.P / cols, ch = s.W / Rows;
        double gx = Frac(u) * cols, gy = v * Rows;
        int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
        double deepest = 0;
        for (int j = -2; j <= 2; j++)
            for (int i = -3; i <= 2; i++)
            {
                int cx = ix + i, cy = iy + j, wx = ((cx % cols) + cols) % cols;
                // Each cell's mark: anywhere in its cell (rows staggered), its own size and depth.
                double px = cx + 0.5 * (cy & 1) + Hash(wx, cy, 7), py = cy + Hash(wx, cy, 13);
                double r = 1.0 + 0.6 * Hash(wx, cy, 29), depth = 0.7 + 0.3 * Hash(wx, cy, 41);
                double dx = (gx - px) * cw, dy = (gy - py) * ch, rr = r * Math.Min(cw, ch);
                double k = (dx * dx + dy * dy) / (rr * rr);
                // Added as a soft maximum, so where marks meet the ridge between them is rounded.
                if (k < 1) { double c = depth * (1 - k); deepest += c * c * c * c; }
            }
        return 1 - Math.Min(1, Math.Pow(deepest, 0.25));
    }
}
