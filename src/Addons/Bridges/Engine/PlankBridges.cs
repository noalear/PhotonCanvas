using Clipper2Lib;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// Plank bridges and rope bridges: two sides (beams, or ropes between posts) with a notch in their top for
/// each plank, and the planks dropped into the notches. Seen from the side, x runs along the bridge and y
/// up; the sides stand at z = ± (walkway / 2 + sheet / 2), the planks lie across them.
/// </summary>
public static class PlankBridges
{
    /// <summary>How far planks reach past the sides (mm).</summary>
    const double Overhang = 1.5;

    public static void Plank(Maker m)
    {
        var s = m.Spec;
        double T = m.T, f = m.Fit, L = s.LengthMm, W = s.WidthMm;
        bool table = s.Ends == EndsOn.Table;
        double beam = s.BeamMm;
        if (beam < T + 1.2) { m.Bad($"The beams must be at least {T + 1.2:0.#} mm deep: the planks sit in notches as deep as the sheet ({T:0.##} mm)."); return; }
        if (table && s.HeightMm < beam) { m.Bad($"The deck can't be lower than the beams are deep: make the height at least {beam:0.#} mm, or the beams shallower."); return; }
        if (W < 4) { m.Bad("Make the walkway at least 4 mm wide."); return; }
        double Hd = table ? s.HeightMm : beam;
        double rise = Math.Max(0, s.RiseMm);
        if (rise > L * 0.3) { m.Bad($"The arch is too high for its length: make it at most {L * 0.3:0.#} mm."); return; }
        var line = DeckLine.Arc(Hd, rise, 0, L);
        double pw = s.PlankMm, gap = s.GapMm, k = Planks.Keeper(pw), nw = pw - 2 * k;
        if (pw < 2) { m.Bad("Make the planks at least 2 mm wide."); return; }
        double tooth = pw + gap - (nw + f);
        if (tooth < Maker.Bridge + 0.1) { m.Bad($"The planks are too close together for the notches: make the gap at least {Maker.Bridge + 0.1 - 2 * k + f:0.##} mm."); return; }

        bool rail = s.Railing;
        double railT = Math.Clamp(T * 0.8, 1.4, 2.5), railH = s.RailMm, ep = 2.0;
        if (rail && railH - railT < T + 1.5) { m.Bad($"The railing is too low to pass the planks under it: make it at least {T + 1.5 + railT:0.#} mm high."); return; }
        double sa = rail ? ep + 0.4 : 0.8;
        var st = Planks.Lay(line, sa, line.Length - sa, pw, gap);
        if (st.Count < 3) { m.Bad("The bridge is too short for its planks: make it longer or the planks narrower."); return; }
        var teeth = Planks.Teeth(line, st);
        var posts = rail ? Planks.PostTeeth(st.Count, s.PostEvery) : [];

        var outline = new PathsD { line.Band(-beam, 0, 0, L) };
        var holes = new PathsD();
        var lines = new List<PathD>();
        var grain = s.Grain && !s.Metal;

        // Legs: one at each end (when the deck is above the table) and any under the middle, each braced to the beam.
        var legs = new List<(double X, double Free, bool End)>();
        double legW = s.LegMm;
        if (table)
        {
            double endFree = Hd - beam;
            bool spreaders = endFree >= 6 || s.Legs > 0;
            if (spreaders) legW = Math.Max(legW, m.SlotW + 2 * Maker.Bridge + 0.2);
            legW = Math.Clamp(legW, 1.5, L / 4);
            if (endFree > 0.05)
            {
                legs.Add((legW / 2, endFree, true));
                legs.Add((L - legW / 2, endFree, true));
            }
            for (int j = 1; j <= Math.Max(0, s.Legs); j++)
            {
                double x = L * j / (s.Legs + 1.0);
                legs.Add((x, line.Y(x) - beam, false));
            }
        }
        else if (s.Legs > 0) m.Note("Legs under the middle are only made when the bridge stands on the table.");
        foreach (var leg in legs)
        {
            double x0 = leg.X - legW / 2, x1 = leg.X + legW / 2;
            outline.Add(line.FromFloor(x0, x1, 0, -beam + 0.5));
            if (grain) lines.AddRange(Planks.UprightGrain(x0, x1, 0, line.Y(leg.X) - beam, m.Rnd));
            // Knee braces from the leg up to the beam.
            double kb = Math.Min(6, leg.Free * 0.45);
            if (kb >= 2.5)
                foreach (int side in leg.End ? new[] { leg.X < L / 2 ? 1 : -1 } : new[] { -1, 1 })
                {
                    double xs = side > 0 ? x1 : x0, xe = xs + side * kb;
                    outline.Add(Geo.Strip(new PointD(xs - side * 0.3, line.Y(xs) - beam - kb), new PointD(xe, line.Y(xe) - beam + 0.3), 1.4));
                }
        }
        // Spreaders between the legs, through slots in them, low down: they keep the legs apart and square.
        var spreads = legs.Where(l => l.Free >= 6).ToList();
        if (spreads.Count > 0)
        {
            double minFree = spreads.Min(l => l.Free);
            double th = Math.Clamp(minFree * 0.22, 1.5, 3), rh = th + 1.2;
            var poses = new List<Pose>();
            foreach (var leg in spreads)
            {
                double ys = Math.Max(rh / 2 + 1, leg.Free * 0.4);
                holes.Add(Geo.Rect(leg.X - m.SlotW / 2, ys - (th + f) / 2, leg.X + m.SlotW / 2, ys + (th + f) / 2));
                poses.Add(Pose.Left(leg.X, ys - rh / 2, -W / 2));
            }
            var spreader = new PathsD
            {
                Geo.Rect(0, 0, W, rh),
                Geo.Rect(-T, (rh - th) / 2, 0.01, (rh + th) / 2),
                Geo.Rect(W - 0.01, (rh - th) / 2, W + T, (rh + th) / 2),
            };
            m.Add("Spreader", spreader, [], poses, lines: grain ? Grain(0, W, 0, rh, m.Rnd) : null);
        }

        // The railing: a post at each end and every few planks, and a rail along the top.
        if (rail)
        {
            outline.Add(line.Band(railH - railT, railH, 0, L));
            outline.Add(line.Upright(0, ep, -beam / 2, railH - railT / 2));
            outline.Add(line.Upright(L - ep, L, -beam / 2, railH - railT / 2));
            if (grain)
            {
                lines.AddRange(Planks.UprightGrain(0, ep, line.Y(0), line.Y(0) + railH - railT, m.Rnd));
                lines.AddRange(Planks.UprightGrain(L - ep, L, line.Y(L), line.Y(L) + railH - railT, m.Rnd));
                for (double o = railH - railT + 0.45; o < railH - 0.2; o += 0.9) lines.Add(line.Parallel(o, 0, L, m.Rnd, 0.06));
            }
            foreach (int t in posts)
            {
                var tt = teeth[t];
                double w = Math.Max(0.8, tooth * Math.Cos(tt.Angle) - 0.35), x0 = tt.P.x - w / 2, x1 = tt.P.x + w / 2;
                outline.Add(line.Upright(x0, x1, -beam / 2, railH - railT / 2));
                if (grain) lines.AddRange(Planks.UprightGrain(x0, x1, tt.P.y, tt.P.y + railH - railT, m.Rnd));
            }
        }
        if (grain) for (double o = -beam + 0.45; o < -0.2; o += 0.9) lines.Add(line.Parallel(o, 0, L, m.Rnd));
        if (s.Metal)
        {
            lines.AddRange(Rivets(line, -beam / 2, 0, L));
            if (rail) lines.AddRange(Rivets(line, railH - railT / 2, 0, L));
        }
        foreach (var p in st) holes.Add(Planks.Notch(p, nw + f, T));

        double zs = W / 2 + T / 2;
        string sideName = rail ? "Side (beam and railing)" : "Side beam";
        var sidePart = m.Add(sideName, outline, holes, [Pose.Front(0, 0, zs), Pose.Back(L, 0, -zs)], lines: lines);
        if (table) KeepAboveTable(m, sidePart);
        AddPlanks(m, st, [], pw, W, s.Rough);

        m.Note(rail
            ? "Stand the two sides up, slide each plank through the railing of both and drop it into its notches: the narrow necks stop it sliding across. A dot of glue holds it."
            : "Stand the two sides up and drop each plank into its notches: the narrow necks stop it sliding across. A dot of glue holds it.");
        if (rail && posts.Count == 0 && st.Count > 6) m.Note("There are only posts at the ends: make the posts more often if the rail should have more.");
    }

    public static void Rope(Maker m)
    {
        var s = m.Spec;
        double T = m.T, f = m.Fit, L = s.LengthMm, W = s.WidthMm;
        bool table = s.Ends == EndsOn.Table;
        double ropeD = T + 1.6, ropeR = Math.Clamp(T * 0.7, 1.2, 2), P = s.PostMm;
        if (W < 4) { m.Bad("Make the walkway at least 4 mm wide."); return; }
        if (P < 2) { m.Bad("Make the posts at least 2 mm thick."); return; }
        if (L < 2 * P + 20) { m.Bad($"Make the bridge at least {2 * P + 20:0} mm long."); return; }
        double Hd = table ? s.HeightMm : ropeD, sag = Math.Max(0, s.RiseMm);
        if (table && Hd - sag < ropeD + 1) { m.Bad($"It sags onto the table: make the height at least {sag + ropeD + 1:0.#} mm, or the sag less."); return; }
        if (sag > (L - 2 * P) * 0.3) { m.Bad($"It sags too much for its length: make the sag at most {(L - 2 * P) * 0.3:0.#} mm."); return; }
        double xa = P, xb = L - P;
        var line = DeckLine.Sag(Hd, sag, xa, xb);
        double pw = s.PlankMm, gap = s.GapMm, k = Planks.Keeper(pw), nw = pw - 2 * k;
        if (pw < 2) { m.Bad("Make the planks at least 2 mm wide."); return; }
        double tooth = pw + gap - (nw + f);
        if (tooth < Maker.Bridge + 0.1) { m.Bad($"The planks are too close together for the notches: make the gap at least {Maker.Bridge + 0.1 - 2 * k + f:0.##} mm."); return; }
        bool rail = s.Railing;
        double railH = s.RailMm;
        if (rail && railH - ropeR < T + 1.5) { m.Bad($"The hand rope is too low to pass the planks under it: make it at least {T + 1.5 + ropeR:0.#} mm high."); return; }
        var st = Planks.Lay(line, 0.6, line.Length - 0.6, pw, gap);
        if (st.Count < 4) { m.Bad("The bridge is too short for its planks: make it longer or the planks narrower."); return; }
        var teeth = Planks.Teeth(line, st);

        var outline = new PathsD();
        var holes = new PathsD();
        var lines = new List<PathD>();
        bool grain = s.Grain && !s.Metal;
        // The posts, with a point on top.
        double top = Hd + (rail ? railH : 3) + 2.5;
        foreach (var (x0, x1) in new[] { (0.0, P), (L - P, L) })
        {
            outline.Add(new PathD { new(x0, 0), new(x1, 0), new(x1, top), new((x0 + x1) / 2, top + P * 0.5), new(x0, top) });
            if (grain) lines.AddRange(Planks.UprightGrain(x0, x1, 0, top, m.Rnd));
        }
        // The rope under the planks, and the hand rope, both into the posts.
        outline.Add(line.Band(-ropeD, 0, xa - 0.6, xb + 0.6));
        if (grain) lines.AddRange(Twist(line, -ropeD, 0, xa, xb));
        if (rail)
        {
            outline.Add(line.Band(railH - ropeR, railH, xa - 0.6, xb + 0.6));
            if (grain) lines.AddRange(Twist(line, railH - ropeR, railH, xa, xb));
            // Ropes down from the hand rope to the deck every few planks.
            foreach (int t in Planks.PostTeeth(st.Count, s.PostEvery))
            {
                var tt = teeth[t];
                double w = Math.Clamp(tooth * Math.Cos(tt.Angle) - 0.35, 0.8, 1.2), x0 = tt.P.x - w / 2, x1 = tt.P.x + w / 2;
                outline.Add(line.Upright(x0, x1, -ropeD / 2, railH - ropeR / 2));
                if (grain)
                    for (double y = tt.P.y + 0.4; y < tt.P.y + railH - ropeR; y += 0.9)
                        lines.Add([new(x0 - 0.2, y), new(x1 + 0.2, y + 0.5)]);
            }
        }
        foreach (var p in st) holes.Add(Planks.Notch(p, nw + f, T));
        double zs = W / 2 + T / 2;
        m.Add("Side (ropes and posts)", outline, holes, [Pose.Front(0, 0, zs), Pose.Back(L, 0, -zs)], lines: lines);

        // Some planks left out, never the first two or last two at either end.
        var missing = new HashSet<int>();
        int want = Math.Clamp(s.MissingPlanks, 0, Math.Max(0, (st.Count - 4) / 3));
        for (int tries = 0; missing.Count < want && tries < 200; tries++)
        {
            int i = 2 + m.Rnd.Next(Math.Max(1, st.Count - 4));
            if (!missing.Contains(i - 1) && !missing.Contains(i + 1)) missing.Add(i);
        }
        if (s.MissingPlanks > want) m.Note($"Only {want} plank{(want == 1 ? "" : "s")} left out: more would leave too few to walk on.");
        AddPlanks(m, st, missing, pw, W, s.Rough);

        double low = Hd - sag - ropeD;
        m.Note("Stand the two sides up, slide each plank under the hand ropes and drop it into its notches: the narrow necks stop it sliding across. A dot of glue holds it.");
        if (!table)
            m.Note($"The posts stand on the banks and the middle hangs {-low:0.#} mm below them: leave a gap at least that deep under it.");
    }

    /// <summary>The planks, each in its notches; rough planks come in three slightly different shapes.</summary>
    static void AddPlanks(Maker m, List<Station> st, HashSet<int> missing, double pw, double W, bool rough)
    {
        double T = m.T, f = m.Fit, k = Planks.Keeper(pw), zs = W / 2 + T / 2;
        int variants = rough ? Math.Min(3, st.Count) : 1;
        var ends = Enumerable.Range(0, variants).Select(_ => rough
            ? (E0: Overhang + m.Rnd.NextDouble() * 1.2, E1: Overhang + m.Rnd.NextDouble() * 1.2, S0: (m.Rnd.NextDouble() - 0.5) * 1.0, S1: (m.Rnd.NextDouble() - 0.5) * 1.0)
            : (E0: Overhang, E1: Overhang, S0: 0.0, S1: 0.0)).ToList();
        var poses = Enumerable.Range(0, variants).Select(_ => new List<Pose>()).ToList();
        for (int i = 0; i < st.Count; i++)
        {
            if (missing.Contains(i)) continue;
            int v = i % variants;
            poses[v].Add(Planks.Lying(st[i], T, W / 2 + T + ends[v].E0));
        }
        for (int v = 0; v < variants; v++)
        {
            if (poses[v].Count == 0) continue;
            var (e0, e1, s0, s1) = ends[v];
            double len = W + 2 * T + e0 + e1, front = W / 2 + T + e0;
            var (outline, holes) = Planks.Shape(pw, len, k, [front - zs, front + zs], T + f, s0, s1);
            m.Add(variants > 1 ? $"Plank {(char)('A' + v)}" : "Plank", outline, holes, poses[v], lines: m.Spec.Metal ? Planks.Grating(pw, len) : m.Spec.Grain ? Planks.Grain(pw, len, m.Rnd) : null);
        }
    }

    /// <summary>A rope's twist: short slanting strokes across the band between lo and hi above the line.</summary>
    static List<PathD> Twist(DeckLine line, double lo, double hi, double xa, double xb)
    {
        var lines = new List<PathD>();
        double lean = (hi - lo) * 0.45;
        for (double x = xa; x <= xb; x += 0.9)
            lines.Add([new(x, line.Y(x) + lo - 0.2), new(x + lean, line.Y(x + lean) + hi + 0.2)]);
        return lines;
    }

    /// <summary>Rivets along a band <paramref name="o"/> above the deck line, about 3 mm apart.</summary>
    static List<PathD> Rivets(DeckLine line, double o, double xa, double xb)
    {
        var lines = new List<PathD>();
        int n = Math.Max(1, (int)Math.Round((xb - xa - 2) / 3));
        for (int i = 0; i <= n; i++)
        {
            double x = xa + 1 + (xb - xa - 2) * i / n;
            lines.Add(Geo.Loop(Geo.Circle(x, line.Y(x) + o, 0.3)));
        }
        return lines;
    }

    /// <summary>Grain along a straight piece (x along it).</summary>
    static List<PathD> Grain(double x0, double x1, double y0, double y1, Random rnd)
    {
        var line = DeckLine.Level(0, x0, x1);
        var lines = new List<PathD>();
        for (double y = y0 + 0.45; y < y1 - 0.2; y += 0.9) lines.Add(line.Parallel(y, x0 - 2, x1 + 2, rnd, 0.06));
        return lines;
    }

    /// <summary>A piece standing on the table that dips below it would rock: say so.</summary>
    static void KeepAboveTable(Maker m, Part p)
    {
        if (Geo.Bounds(p.Cut).top < -0.01) m.Warn("The sides reach below the table: make the deck higher.");
    }
}
