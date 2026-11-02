using Clipper2Lib;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>One piece to cut (y up, mm): its outline and holes, what's etched on it as areas, and lines scored on it.</summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies);

/// <summary>Pieces shown together as they go once built: a pile's layers from above, a wall on its base.</summary>
public sealed record View(string Name, List<Part> Layers);

/// <summary>Everything a set of ruins is cut from, with what was found while making it.</summary>
public sealed record RuinKit(List<Part> Parts, List<View> Views, List<Finding> Findings)
{
    public Part? Front => Parts.FirstOrDefault(p => p.Name == "Broken wall");
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
}

/// <summary>
/// Makes a ruin: a broken wall section in the wall maker's textures (broken along the mortar, brick by
/// brick, where the texture has bricks or stones), with holes, knocked-out stones and cracks, on a base
/// of its own or with whole ends for the wall maker's columns; and the rubble, fallen stones, chunks of
/// wall and broken pillars to go round it.
/// </summary>
public static class RuinKitMaker
{
    /// <summary>Gap left between the end of a wall and the bottom of a column's slot (mm), as in the wall maker.</summary>
    public const double EndClearMm = 0.25;
    /// <summary>The narrowest strip of a broken edge that's safe to cut (mm).</summary>
    public const double Bridge = 1.2;

    public static double WallThickness(RuinSpec s) => s.ThicknessMm * (s.TwoSided ? 2 : 1);

    public static RuinKit Make(RuinSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        var views = new List<View>();
        if (s.ThicknessMm < 0.3) { findings.Add(new(Severity.Bad, "The sheet is too thin: make it at least 0.3 mm.")); return new(parts, views, findings); }
        if (s.Wall) BrokenWall.Make(s, parts, views, findings);
        Rubble.Make(s, parts, views, findings);
        Pillars.Make(s, parts, views, findings);
        if (parts.Count == 0 && !findings.Any(f => f.Level == Severity.Bad))
            findings.Add(new(Severity.Bad, "Nothing to make: tick Make a broken wall, or ask for rubble piles, stones, chunks or pillars."));
        return new(parts, views, findings);
    }
}

/// <summary>The broken wall section, its back panel, and the base it can stand on.</summary>
public static class BrokenWall
{
    public static void Make(RuinSpec s, List<Part> parts, List<View> views, List<Finding> findings)
    {
        double L = s.LengthMm, H = s.HeightMm, T = s.ThicknessMm, wallT = RuinKitMaker.WallThickness(s), fit = Math.Max(0, s.FitMm);
        double a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        double ez = s.SlotDepthMm + RuinKitMaker.EndClearMm;
        double ezL = s.LeftEnd == EndKind.Whole ? ez : 0, ezR = s.RightEnd == EndKind.Whole ? ez : 0;
        if (L < 10 || L < ezL + ezR + 8) { findings.Add(new(Severity.Bad, $"The wall is too short ({L:0.#} mm): make it longer.")); return; }
        if (H < 6) { findings.Add(new(Severity.Bad, "The wall is too low: make it at least 6 mm high.")); return; }

        var rnd = new Random(s.Seed * 7919 + 101);
        var prof = Profile(s, L, H, ezL, ezR, rnd);
        double P(double x) => prof.At(x);

        var tex = s.TextureSpec;
        var grid = TextureGrid.Wall(tex, L, H, ezL, L - ezR, H);
        bool cellular = Rough.Cellular(s.Texture) && grid.Cells.Count > 0;
        double rowH = Textures.Preset(s.Texture)?.SizeBLabel is null ? a : b;
        var panel = new PathsD { Geo.Rect(0, 0, L, H) };
        var ends = new PathsD();
        if (ezL > 0) ends.Add(Geo.Rect(0, 0, ezL, H));
        if (ezR > 0) ends.Add(Geo.Rect(L - ezR, 0, L, H));

        // The outline: whole bricks or stones under the break (and the bottom course always), or a stepped or torn line.
        PathsD cut;
        var kept = new List<Cell>();
        bool Bottom(Cell c) => c.Middle.y < rowH * 0.9;
        if (cellular)
        {
            foreach (var c in grid.Cells)
            {
                double x = Math.Clamp(c.Middle.x, 0, L), top = P(x);
                // Where it's whole, the top course is kept even if it's cut short by the top of the wall.
                if (top >= H - 0.01) top = H + rowH;
                if (!(c.Middle.y < top || Bottom(c))) continue;
                if (c.Middle.x < -a || c.Middle.x > L + a) continue;
                // A broken end crumbles: bricks at the very end go missing, more of them higher up.
                bool atLeft = s.LeftEnd == EndKind.Broken && c.Middle.x < Math.Min(a * 0.6, L * 0.1);
                bool atRight = s.RightEnd == EndKind.Broken && c.Middle.x > L - Math.Min(a * 0.6, L * 0.1);
                if ((atLeft || atRight) && !Bottom(c) && rnd.NextDouble() < 0.25 + 0.55 * c.Middle.y / H) continue;
                kept.Add(c);
            }
            cut = Geo.Intersect(Geo.Union(new PathsD(kept.SelectMany(c => c.Shape))), panel);
        }
        else
        {
            var top = s.Texture == "Wood planks (upright)" ? Splintered(prof, L, H, a, b, rnd)
                : Rough.Wooden(s.Texture) || s.Texture == "Corrugated metal" ? Torn(prof, L, a, rnd)
                : Stepped(prof, L, a, b, rnd);
            var poly = new PathD { new(0, 0), new(L, 0) };
            for (int i = top.Count - 1; i >= 0; i--) poly.Add(top[i]);
            cut = Geo.Intersect(Geo.Tidy([poly]), panel);
        }
        // Stones that only touch at a corner are joined (the gap filled), anything thin enough to snap goes.
        if (cellular) cut = Geo.Intersect(Rough.Close(cut, Math.Max(grid.Groove * 2, Math.Min(a, b) * 0.2)), panel);
        cut = Rough.Largest(Rough.Open(Rough.FillHoles(Geo.Union(cut, ends)), RuinKitMaker.Bridge));

        // Holes knocked through, cell by cell (or a ragged hole), and undone if they'd cut a piece off.
        int breaches = 0;
        var holes = new PathsD();
        for (int k = 0; k < Math.Max(0, s.Breaches); k++)
        {
            double r = Math.Clamp(s.BreachMm, 3, Math.Min(L, H)) / 2;
            double from = ezL + r + 2, to = L - ezR - r - 2;
            if (to <= from) { findings.Add(new(Severity.Warn, "The wall is too short for a hole that size.")); break; }
            bool placed = false;
            for (int tries = 0; tries < 12 && !placed; tries++)
            {
                double cx = from + (to - from) * rnd.NextDouble();
                double under = Enumerable.Range(0, 9).Min(i => P(cx - r * 1.3 + r * 2.6 * i / 8));
                double y0 = Math.Max(rowH * 1.2 + r * 0.8, H * 0.18), y1 = under - r - Math.Max(2, rowH * 1.5);
                if (y1 < y0) continue;
                double cy = y0 + (y1 - y0) * rnd.NextDouble();
                var blob = new PathsD { Rough.Lumpy(Rough.Ellipse(cx, cy, r * 1.1, r * 0.9), r * 0.22, r * 1.4, rnd) };
                PathsD hole = cellular
                    ? Geo.Union(new PathsD(kept.Where(c => !Bottom(c) && Rough.Inside(blob, c.Middle)).SelectMany(c => c.Shape)))
                    : blob;
                if (Geo.Area(hole) < 1) continue;
                if (holes.Count > 0 && Geo.Area(Geo.Intersect(Geo.Offset(hole, a), holes)) > 1e-3) continue;   // apart from the others
                var tried = Geo.Subtract(cut, hole);
                // A hole right through, not a notch broken out of the top, and nothing left thin enough to snap.
                if (Geo.Pieces(tried) != 1 || Geo.Area(Rough.FillHoles(tried)) < Geo.Area(Rough.FillHoles(cut)) - 0.5) continue;
                if (Rough.Thin(tried, RuinKitMaker.Bridge) > Rough.Thin(cut, RuinKitMaker.Bridge) + 0.5) continue;
                cut = tried;
                holes.AddRange(hole);
                placed = true;
                breaches++;
            }
        }
        if (breaches < s.Breaches)
            findings.Add(new(Severity.Warn, breaches == 0 ? "No hole fits below the break: make the wall taller or the hole smaller, or try another Variation." : $"Only {breaches} of the {s.Breaches} holes fit below the break."));

        // Single bricks or stones knocked out: whole cells, clear of the edges and of each other.
        int knocked = 0;
        if (s.KnockedOut > 0)
        {
            if (!cellular) findings.Add(new(Severity.Note, "Knocked-out stones need a texture of bricks, stones, tiles or plates."));
            else
            {
                var taken = new PathsD();
                foreach (var c in kept.Where(c => !Bottom(c) && c.Middle.x > ezL + a * 0.6 && c.Middle.x < L - ezR - a * 0.6).OrderBy(_ => rnd.Next()))
                {
                    if (knocked >= s.KnockedOut) break;
                    var round = Geo.Offset(c.Shape, Math.Max(RuinKitMaker.Bridge, grid.Groove));
                    if (Geo.Area(Geo.Subtract(round, cut)) > 1e-3) continue;          // too near an edge or a hole
                    if (taken.Count > 0 && Geo.Area(Geo.Intersect(round, taken)) > 1e-3) continue;
                    var tried = Geo.Subtract(cut, c.Shape);
                    if (Geo.Pieces(tried) != 1) continue;
                    cut = tried;
                    taken.AddRange(round);
                    knocked++;
                }
                if (knocked < s.KnockedOut) findings.Add(new(Severity.Note, $"{knocked} of the {s.KnockedOut} knocked-out stones fit clear of the edges."));
            }
        }

        // The texture, kept off whole ends (inside the columns' slots) and the tabs.
        var face = Geo.Intersect(cut, [Geo.Rect(0, 0, L, H + 1)]);
        var region = s.PlainEnds && ends.Count > 0 ? Geo.Subtract(face, ends) : face;
        var (etched, lines) = grid.In(region, s.Etch);

        // Cracks: scored down from low points of the break.
        for (int k = 0; k < Math.Max(0, s.Cracks); k++)
        {
            double from = ezL + L * 0.08, to = L - ezR - L * 0.08;
            if (to <= from) break;
            // Lean towards where it's broken deepest.
            double best = from, bestY = double.MaxValue;
            for (int t = 0; t < 4; t++)
            {
                double x = from + (to - from) * rnd.NextDouble();
                if (P(x) < bestY) { bestY = P(x); best = x; }
            }
            foreach (var line in Crack(best, bestY + 1, H * (0.2 + 0.25 * rnd.NextDouble()), rnd)) lines.AddRange(Geo.ClipLines([line], region));
        }

        // A base the wall's tabs slot into (only for a wall that stands on its own).
        var tabs = new PathsD();
        var tabXs = new List<double>();
        double tabW = Math.Clamp(L * 0.15, 4, 15);
        bool baseOk = s.Base && s.LeftEnd == EndKind.Broken && s.RightEnd == EndKind.Broken;
        if (s.Base && !baseOk)
            findings.Add(new(Severity.Warn, "A base is only made for a wall with both ends broken: a wall with a whole end stands in the wall maker's columns."));
        if (baseOk)
        {
            tabXs = L >= 40 ? [L * 0.25, L * 0.75] : [L / 2];
            foreach (var x in tabXs) tabs.Add(Geo.Rect(x - tabW / 2, -T, x + tabW / 2, 0.01));
            cut = Geo.Union(cut, tabs);
        }

        int count = Math.Max(1, s.Walls);
        var front = new Part("Broken wall", cut, etched, lines, count);
        parts.Add(front);
        if (s.TwoSided)
        {
            // Cut as a mirror image: turned over, it lies exactly on the back of the front panel.
            PointD Mirror(PointD p) => new(L - p.x, p.y);
            parts.Add(new Part("Broken wall back", Geo.Tidy(Geo.Map(cut, Mirror)), Geo.Tidy(Geo.Map(etched, Mirror)), Geo.Map(lines, Mirror), count));
        }

        double low = Enumerable.Range(0, 201).Select(i => L * i / 200.0).Where(x => x > ezL && x < L - ezR).Select(P).DefaultIfEmpty(H).Min();
        findings.Add(new(Severity.Note, $"The wall is {H:0.#} mm high where it's whole and about {Math.Max(0, low):0.#} mm at its lowest."));
        if (ezL > 0 || ezR > 0)
            findings.Add(new(Severity.Note, $"Its whole end{(ezL > 0 && ezR > 0 ? "s slide" : " slides")} into the wall maker's columns ({s.SlotDepthMm:0.#} mm slots, for a {wallT:0.#} mm wall); make those in Walls with the same height and sheet."));
        if (s.TwoSided) findings.Add(new(Severity.Note, $"Two panels glued back to back make a {wallT:0.#} mm wall."));

        if (baseOk) Base(s, L, H, wallT, fit, tabW, tabXs, prof, parts, views, findings, count, front);
        else views.Add(new View("Broken wall", [front]));
    }

    /// <summary>Where the top of the wall is along it, sampled every half millimetre.</summary>
    public sealed class Line(double[] y, double step)
    {
        public double At(double x)
        {
            double t = Math.Clamp(x / step, 0, y.Length - 1.0001);
            int i = (int)t;
            return y[i] + (y[i + 1] - y[i]) * (t - i);
        }

        /// <summary>Where it's lowest (x, mm).</summary>
        public double LowestX(double from, double to)
        {
            double best = from, by = double.MaxValue;
            for (int i = 0; i < y.Length; i++)
            {
                double x = i * step;
                if (x < from || x > to) continue;
                if (y[i] < by) { by = y[i]; best = x; }
            }
            return best;
        }
    }

    /// <summary>The top of the broken wall: the break's shape, a wobble, and the rise to full height at whole ends.</summary>
    public static Line Profile(RuinSpec s, double L, double H, double ezL, double ezR, Random rnd)
    {
        const double step = 0.5;
        int n = (int)Math.Ceiling(L / step) + 2;
        var y = new double[n];
        double depth = Math.Clamp(s.BreakDepth, 0.05, 0.9) * H;
        double c = L * (0.35 + 0.3 * rnd.NextDouble()), half = L * (0.3 + 0.15 * rnd.NextDouble());
        double c1 = L * (0.22 + 0.1 * rnd.NextDouble()), c2 = L * (0.68 + 0.1 * rnd.NextDouble()), h2 = 0.6 + 0.4 * rnd.NextDouble();
        var wob = new Rough.Wobble(rnd, Math.Max(10, L / 4));
        static double Dip(double x, double c, double half) => Math.Pow(Math.Cos(Math.PI / 2 * Math.Min(1, Math.Abs(x - c) / Math.Max(1, half))), 2);
        double ramp = Math.Clamp(L * 0.12, 4, 14);
        double floor = Math.Max(2, H * 0.08);
        for (int i = 0; i < n; i++)
        {
            double x = Math.Min(L, i * step);
            double d = s.Break switch
            {
                BreakShape.FallsLeft => Math.Pow(1 - x / L, 1.3),
                BreakShape.FallsRight => Math.Pow(x / L, 1.3),
                BreakShape.TwoGaps => Math.Max(Dip(x, c1, L * 0.17), h2 * Dip(x, c2, L * 0.17)),
                BreakShape.Stump => 0.88,
                _ => Dip(x, c, half),
            };
            d = Math.Clamp(d + 0.16 * wob.At(x) - 0.04, 0, 1);
            double top = H - depth * d;
            // Whole ends rise to full height, so they're straight where they go into the columns.
            if (ezL > 0) top = Raise(top, (x - ezL) / ramp);
            if (ezR > 0) top = Raise(top, (L - ezR - x) / ramp);
            y[i] = Math.Clamp(top, floor, H);
        }
        return new Line(y, step);

        double Raise(double top, double t)
        {
            t = Math.Clamp(t, 0, 1);
            double w = t * t * (3 - 2 * t);
            return H - (H - top) * w;
        }
    }

    /// <summary>A stepped top (like fallen stones), for textures without separate stones to follow.</summary>
    static List<PointD> Stepped(Line prof, double L, double a, double b, Random rnd)
    {
        double stepX = Math.Clamp(a / 2, 1.5, 8), stepY = Math.Clamp(b, 1, 8);
        var pts = new List<PointD>();
        for (double x = 0; x < L - 1e-6; x += stepX)
        {
            double x1 = Math.Min(L, x + stepX);
            double y = prof.At((x + x1) / 2);
            // Down to a whole step, now and then one more.
            double top = Math.Max(1, Math.Min(y, Math.Floor(y / stepY + rnd.NextDouble() * 0.6) * stepY));
            pts.Add(new(x, top));
            pts.Add(new(x1, top));
        }
        return pts;
    }

    /// <summary>Upright boards each snapped off at its own height, to a splintered point.</summary>
    static List<PointD> Splintered(Line prof, double L, double H, double a, double b, Random rnd)
    {
        var pts = new List<PointD>();
        for (double x0 = 0; x0 < L - 1e-6; x0 += a)
        {
            double x1 = Math.Min(L, x0 + a), w = x1 - x0;
            double top = prof.At((x0 + x1) / 2) + (rnd.NextDouble() - 0.5) * Math.Min(b * 0.3, H * 0.15);
            double sp = Math.Clamp(w * 0.9, 0.6, 4);
            double Y(double y) => Math.Clamp(y, 1, H);
            pts.Add(new(x0, Y(top - sp * (0.3 + 0.7 * rnd.NextDouble()))));
            pts.Add(new(x0 + w * (0.25 + 0.5 * rnd.NextDouble()), Y(top + sp * 0.4)));
            pts.Add(new(x1, Y(top - sp * (0.3 + 0.7 * rnd.NextDouble()))));
        }
        return pts;
    }

    /// <summary>A torn, splintered top (wood, sheet metal): a zigzag about the break.</summary>
    static List<PointD> Torn(Line prof, double L, double a, Random rnd)
    {
        double step = Math.Clamp(a / 2.5, 0.8, 3), amp = Math.Clamp(a * 0.5, 0.6, 3);
        var pts = new List<PointD>();
        int k = 0;
        for (double x = 0; x <= L + 1e-6; x += step, k++)
        {
            double xx = Math.Min(L, x);
            double y = prof.At(xx) + (k % 2 == 0 ? -1 : 0.4) * amp * (0.4 + 0.6 * rnd.NextDouble());
            pts.Add(new(xx, Math.Max(1, y)));
        }
        if (pts[^1].x < L) pts.Add(new(L, Math.Max(1, prof.At(L))));
        return pts;
    }

    /// <summary>A crack: a wandering line down from (x, y), with a branch.</summary>
    static List<PathD> Crack(double x, double y, double length, Random rnd)
    {
        var main = new PathD { new(x, y) };
        double dx = (rnd.NextDouble() - 0.5) * 0.8, lean = (rnd.NextDouble() - 0.5) * 0.6;
        var branch = new PathD();
        int n = Math.Max(3, (int)(length / 1.1));
        int at = n / 3 + rnd.Next(Math.Max(1, n / 3));
        for (int i = 1; i <= n; i++)
        {
            dx = Math.Clamp(dx * 0.4 + (rnd.NextDouble() - 0.5) * 1.4 + lean * 0.3, -1.1, 1.1);
            x += dx;
            y -= 1.1;
            main.Add(new(x, y));
            if (i == at)
            {
                double bx = x, by = y, side = rnd.NextDouble() < 0.5 ? -1 : 1;
                branch.Add(new(bx, by));
                for (int j = 0; j < n / 3 + 1; j++)
                {
                    bx += side * (0.7 + 0.5 * rnd.NextDouble());
                    by -= 0.6 + 0.6 * rnd.NextDouble();
                    branch.Add(new(bx, by));
                }
            }
        }
        return branch.Count >= 2 ? [main, branch] : [main];
    }

    /// <summary>
    /// The base: a lumpy plate seen from above (x along the wall, y across it, the wall's middle on y = 0)
    /// with slots for the wall's tabs, rubble heaped against the wall where it fell, and stones scored round about.
    /// </summary>
    static void Base(RuinSpec s, double L, double H, double wallT, double fit, double tabW, List<double> tabXs, Line prof,
                     List<Part> parts, List<View> views, List<Finding> findings, int count, Part front)
    {
        var rnd = new Random(s.Seed * 7919 + 977);
        double m = Math.Clamp(s.BaseMm, 6, 80), half = wallT / 2;
        double r = m * 0.5;
        var rect = Geo.Rect(-m * 0.35 + r, -(m + half) + r, L + m * 0.35 - r, m + half - r);
        var rounded = Geo.Offset([rect], r, round: true);
        var outline = Geo.Tidy([Rough.Lumpy(rounded.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First(), m * 0.12, Math.Max(8, m * 1.6), rnd)]);
        outline = Rough.Largest(Rough.FillHoles(Geo.Union(outline, [Geo.Rect(-2, -(half + 2), L + 2, half + 2)])));
        var slots = new PathsD();
        foreach (var x in tabXs) slots.Add(Geo.Rect(x - (tabW + fit) / 2, -(wallT + fit) / 2, x + (tabW + fit) / 2, (wallT + fit) / 2));
        var cut = Geo.Subtract(outline, slots);
        var wallStrip = new PathsD { Geo.Rect(-1, -half - 0.3, L + 1, half + 0.3) };

        // Heaps against the wall: in front where it's broken deepest, a smaller one behind.
        var heaps = new List<List<Part>>();
        var within = Geo.Offset(outline, -1);
        double heapH = Math.Min(Math.Max(s.ThicknessMm, s.PileHeightMm), H * 0.5);
        if (s.FootRubble)
        {
            double lowX = prof.LowestX(0, L);
            double w = Math.Clamp(L * 0.55, 16, L + m * 0.5), cxF = Math.Clamp(lowX, w * 0.3, L - w * 0.3);
            double cxB = Math.Clamp(L - lowX + (rnd.NextDouble() - 0.5) * L * 0.3, w * 0.25, L - w * 0.25);
            foreach (var (cx, sign, scale, label) in new[] { (cxF, 1.0, 1.0, "Rubble in front"), (cxB, -1.0, 0.7, "Rubble behind") })
            {
                // Built in the heap's own place: its straight back against the wall's face.
                PointD To(PointD p) => new(p.x + cx, sign * (p.y + half + 0.2));
                PointD From(PointD p) => new(p.x - cx, sign * p.y - half - 0.2);
                var local = Geo.Map(within, From);
                var layers = Rubble.Pile(s, label, PileShape.AgainstWall, w * scale, (m - 1.5) * Math.Sqrt(scale), heapH * scale, rnd, Geo.Tidy(local), findings);
                if (layers.Count == 0) continue;
                parts.AddRange(layers.Select(l => l with { Copies = count }));
                heaps.Add([.. layers.Select(l => l with { Cut = Geo.Tidy(Geo.Map(l.Cut, To)), Etched = Geo.Tidy(Geo.Map(l.Etched, To)), Lines = Geo.Map(l.Lines, To) })]);
            }
        }

        // Stones scored on the ground round about.
        var tex = s.TextureOf(s.Seed + 5);
        var grid = TextureGrid.Turned(tex, Geo.Bounds(outline), rnd.NextDouble() * 180, rnd.NextDouble() * 10, rnd.NextDouble() * 10);
        var clear = Geo.Union(new PathsD(wallStrip.Concat(slots).Concat(heaps.Where(h => h.Count > 0).SelectMany(h => h[0].Cut))));
        var ground = Geo.Subtract(Geo.Offset(outline, -1.2), Geo.Offset(clear, 1));
        var lines = new List<PathD>();
        foreach (var c in grid.Cells)
        {
            if (rnd.NextDouble() > 0.45) continue;
            var stone = Geo.Offset(c.Shape, -grid.Groove);
            if (stone.Count == 0 || Geo.Area(Geo.Subtract(stone, ground)) > 1e-3) continue;
            lines.AddRange(stone.Select(Geo.Loop));
        }
        var basePart = new Part("Base", cut, [], lines, count);
        parts.Add(basePart);
        if (heaps.Count > 0)
            findings.Add(new(Severity.Note, $"The rubble against the wall is {string.Join(" and ", heaps.Select(h => h.Count))} layers: glue layer 1 on the base against the wall, then each one on the marks on the one before."));
        findings.Add(new(Severity.Note, $"The wall's {tabXs.Count} tab{(tabXs.Count == 1 ? "" : "s")} slot into the base; the base reaches {m:0.#} mm out each side."));

        // As built, from above: the base, the wall's top edge, the heaps.
        var wallTop = new Part("Wall", [Geo.Rect(0, -half, L, half)], [], [], 1);
        views.Add(new View("Broken wall", [front]));
        views.Add(new View("On its base, from above", [basePart, wallTop, .. heaps.SelectMany(h => h)]));
    }
}
