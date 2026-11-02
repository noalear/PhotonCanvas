using Clipper2Lib;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// Stone arch bridges: two side walls with the arches cut out and a parapet above the road, stone
/// etched on their outer faces with a ring of wedge-shaped stones round each arch; the road (one piece,
/// or a ramp up, a level top and a ramp down) whose tabs go through slots in the walls; and walls across
/// inside, at the ends and over each pier, whose tabs go through the side walls too and keep them square.
/// Seen from the side, x runs along the bridge and y up; the side walls stand at z = ± (road / 2 + sheet / 2).
/// </summary>
public static class StoneBridge
{
    /// <summary>A straight run of road seen from the side, from A to B (its top surface), trimmed at bends.</summary>
    sealed record Run(PointD A, PointD B, double Trim0, double Trim1)
    {
        public double Length => Math.Sqrt((B.x - A.x) * (B.x - A.x) + (B.y - A.y) * (B.y - A.y));
        public PointD Dir => new((B.x - A.x) / Length, (B.y - A.y) / Length);
        /// <summary>Square to the run, upwards (the second ramp runs right to left).</summary>
        public PointD Up => Dir.x >= 0 ? new(-Dir.y, Dir.x) : new(Dir.y, -Dir.x);
        public PointD At(double along, double up) => new(A.x + Dir.x * along + Up.x * up, A.y + Dir.y * along + Up.y * up);
    }

    public static void Make(Maker m)
    {
        var s = m.Spec;
        double T = m.T, f = m.Fit, sw = m.SlotW, L = s.LengthMm, W = s.WidthMm, H = s.HeightMm;
        int n = Math.Clamp(s.Arches, 1, 9);
        double A = s.ArchSpanMm, Sp = Math.Max(0, s.SpringMm), ring = Math.Max(0.5, s.RingMm);
        if (W < 6) { m.Bad("Make the road at least 6 mm wide."); return; }
        if (A < 6) { m.Bad("Make the arches at least 6 mm wide."); return; }
        double pier = n > 1 ? (s.PierMm > 0 ? s.PierMm : Math.Max(sw + 2 * Maker.Bridge + 1, A * 0.25)) : 0;
        if (n > 1 && pier < sw + 2 * Maker.Bridge) { m.Bad($"Make the piers at least {sw + 2 * Maker.Bridge:0.#} mm wide: a wall across goes through each."); return; }
        double a = (L - n * A - (n - 1) * pier) / 2;
        double needEnd = sw + 2 * Maker.Bridge + 1.5;
        if (a < needEnd)
        {
            m.Bad($"The arches take up too much of the bridge: make it at least {n * A + (n - 1) * pier + 2 * needEnd:0} mm long, or the arches narrower or fewer.");
            return;
        }
        double rise = s.Arch switch
        {
            ArchShape.Segmental => A / 4,
            ArchShape.Pointed => Math.Sqrt(0.5625 - 0.0625) * A,
            _ => A / 2,
        };
        double crown = Sp + rise, roadBottom = H - T;
        double need = crown + ring + Maker.Bridge + f / 2 + 0.2;
        if (need > roadBottom)
        {
            m.Bad($"The arches are too tall for the road: raise the road to at least {need + T:0.#} mm, or make the arches lower, narrower or segmental.");
            return;
        }

        // The road seen from the side: level, or ramps up to a level top over the arches.
        bool humped = s.Road == RoadShape.Humped && s.EndHeightMm < H - 0.5;
        double He = s.EndHeightMm;
        if (humped && He < T + 4) { m.Bad($"Make the road at least {T + 4:0.#} mm high at the ends."); return; }
        var road = humped ? new List<PointD> { new(0, He), new(a, H), new(L - a, H), new(L, He) } : [new(0, H), new(L, H)];
        double ramp = humped ? Math.Atan2(H - He, a) * 180 / Math.PI : 0;
        if (ramp > 35) m.Warn($"The ramps are steep ({ramp:0}°): raise the ends or lower the road.");
        var runs = new List<Run>();
        for (int i = 0; i + 1 < road.Count; i++)
        {
            double Bend(int at)
            {
                if (at <= 0 || at >= road.Count - 1) return 0;
                double a1 = Math.Atan2(road[at].y - road[at - 1].y, road[at].x - road[at - 1].x), a2 = Math.Atan2(road[at + 1].y - road[at].y, road[at + 1].x - road[at].x);
                // Over a crest the road's undersides would overlap: each run stops short so they just meet underneath.
                return T * Math.Tan(Math.Abs(a2 - a1) / 2);
            }
            // The second ramp runs from the end inwards, so both ramps are the same piece.
            runs.Add(humped && i == 2 ? new Run(road[3], road[2], 0, Bend(2)) : new Run(road[i], road[i + 1], Bend(i), Bend(i + 1)));
        }

        bool parapet = s.Parapet != ParapetTop.None;
        double ph = parapet ? Math.Max(2, s.ParapetMm) : 0;
        var top = road.Select(p => new PointD(p.x, p.y + ph)).ToList();
        var wall = new PathD { new(0, 0), new(L, 0) };
        for (int i = top.Count - 1; i >= 0; i--) wall.Add(top[i]);
        var outline = new PathsD { wall };
        var holes = new PathsD();
        var crenels = new PathsD();
        if (s.Parapet == ParapetTop.Battlements)
        {
            if (humped) m.Note("Battlements need a level road: the parapet has a straight top.");
            else crenels = Crenels(L, H + ph, ph);
        }
        holes.AddRange(crenels);

        // The arches.
        var openings = new PathsD();
        var rings = new PathsD();
        var voussoirs = new PathsD();
        double g = Math.Clamp(s.GrooveMm, 0.2, 1.2);
        for (int i = 0; i < n; i++)
        {
            double x0 = a + i * (A + pier);
            var (open, curve) = Opening(s.Arch, x0, A, Sp);
            openings.Add(open);
            var band = Geo.Intersect(Geo.Subtract(Geo.Offset([open], ring + 0.4, round: true), [open]), [Geo.Rect(x0 - ring - 2, Sp - 0.01, x0 + A + ring + 2, crown + ring + 2)]);
            rings.AddRange(band);
            voussoirs.AddRange(Voussoirs(curve, new PointD(x0 + A / 2, Sp), ring, Math.Clamp(s.SizeB * 0.8, 1.8, 5), g));
        }
        holes.AddRange(openings);

        // Slots for the road's tabs, each in the run's own frame.
        var roadTabs = runs.Select(r => Tabs(r.Trim0, r.Length - r.Trim1)).ToList();
        for (int i = 0; i < runs.Count; i++)
            foreach (var (c, tl) in roadTabs[i])
            {
                var r = runs[i];
                double upTo = parapet ? f / 2 : 3;
                holes.Add([r.At(c - (tl + f) / 2, -T - f / 2), r.At(c + (tl + f) / 2, -T - f / 2), r.At(c + (tl + f) / 2, upTo), r.At(c - (tl + f) / 2, upTo)]);
            }

        // Walls across: near each end, and over each pier between two arches.
        double Underside(double x)
        {
            double y = double.MaxValue;
            foreach (var r in runs)
            {
                double x0 = Math.Min(r.A.x, r.B.x), x1 = Math.Max(r.A.x, r.B.x);
                if (x < x0 - 1e-9 || x > x1 + 1e-9) continue;
                double t = (x - r.A.x) / (r.B.x - r.A.x);
                y = Math.Min(y, r.A.y + (r.B.y - r.A.y) * t - T / Math.Abs(r.Dir.x));
            }
            return y;
        }
        double CrossHeight(double xc) => Math.Min(Underside(Math.Max(0, xc - T / 2)), Underside(Math.Min(L, xc + T / 2))) - 0.05;
        double xe = sw / 2 + 1.2;
        var ends = new List<(double X, bool Left)>();
        if (CrossHeight(xe) >= 4) { ends.Add((xe, true)); ends.Add((L - xe, false)); }
        else m.Note("The ends are too low for walls across them: the road and the arches hold the sides together.");
        var piers = Enumerable.Range(0, n - 1).Select(i => a + A + pier / 2 + i * (A + pier)).Where(x => CrossHeight(x) >= 4).ToList();
        PathsD Cross(double h, List<(double Y, double H)> tabs)
        {
            var o = new PathsD { Geo.Rect(0, 0, W, h) };
            foreach (var (y, th) in tabs)
            {
                o.Add(Geo.Rect(-T, y - th / 2, 0.01, y + th / 2));
                o.Add(Geo.Rect(W - 0.01, y - th / 2, W + T, y + th / 2));
            }
            return o;
        }
        List<(double Y, double H)> CrossTabs(double h)
        {
            double th = Math.Clamp(h * 0.25, 2, 6);
            return h > 14 ? [(h * 0.3, th), (h * 0.72, th)] : [(h * 0.5, th)];
        }
        void CrossSlots(double xc, double h)
        {
            foreach (var (y, th) in CrossTabs(h)) holes.Add(Geo.Rect(xc - sw / 2, y - (th + f) / 2, xc + sw / 2, y + (th + f) / 2));
        }
        double endH = ends.Count > 0 ? CrossHeight(xe) : 0;
        foreach (var (x, _) in ends) CrossSlots(x, endH);
        var pierH = piers.Select(CrossHeight).ToList();
        for (int i = 0; i < piers.Count; i++) CrossSlots(piers[i], pierH[i]);

        // The stone on the side walls' outer faces.
        var cut = Geo.Subtract(Geo.Union(outline), Geo.Union(holes));
        double maxTop = top.Max(p => p.y);
        var face = new Face(L, maxTop, 0, L, H);
        var tex = Textures.Make(s.Texture, s.SizeA, s.SizeB, s.GrooveMm, s.Seed, s.Custom, face);
        // A smooth coping along the top, edged with a groove.
        const double coping = 1.2;
        var body = Geo.Subtract(Geo.Union(outline), crenels);
        var copingBand = Geo.Intersect(Geo.Subtract(body, Geo.Move(body, 0, -coping)), cut);
        var copingStone = Geo.Intersect(copingBand, Geo.Move(body, 0, g - coping));
        var keepOff = Geo.Union(Geo.Union(rings, Geo.Offset(voussoirs, g)), copingBand);
        var (etched, lines) = Stonework(cut, tex, keepOff, Geo.Union(voussoirs, copingStone));
        double zs = W / 2 + T / 2;
        m.Add("Side wall", outline, holes, [Pose.Front(0, 0, zs), Pose.Back(L, 0, -zs)], etched, lines);

        // The road.
        var surface = s.Surface;
        for (int i = 0; i < runs.Count; i++)
        {
            if (humped && i == 2) continue;
            var r = runs[i];
            double u0 = r.Trim0, u1 = r.Length - r.Trim1;
            var o = new PathsD { Geo.Rect(u0, 0, u1, W) };
            foreach (var (c, tl) in roadTabs[i])
            {
                o.Add(Geo.Rect(c - tl / 2, -T, c + tl / 2, 0.01));
                o.Add(Geo.Rect(c - tl / 2, W - 0.01, c + tl / 2, W + T));
            }
            var deck = new PathsD { Geo.Rect(u0, 0, u1, W) };
            var (de, dl) = Surface(surface, deck, u1 - u0, W, u0, s.Seed + i * 13, g);
            var poses = new List<Pose> { RoadPose(r, T, W, reversed: false) };
            if (humped && i == 0) poses.Add(RoadPose(runs[2], T, W, reversed: true));
            string name = !humped ? "Road" : i == 1 ? "Road (top)" : "Road (ramp)";
            m.Add(name, o, [], poses, de, dl);
        }

        // The walls across.
        if (ends.Count > 0)
        {
            var ef = Textures.Make(s.Texture, s.SizeA, s.SizeB, s.GrooveMm, s.Seed + 5, s.Custom, new Face(W, endH, 0, W, endH));
            var (ee, el) = Stonework(new PathsD { Geo.Rect(0, 0, W, endH) }, ef, [], []);
            m.Add("End wall", Cross(endH, CrossTabs(endH)), [], [Pose.Left(xe, 0, -W / 2), Pose.Right(L - xe, 0, W / 2)], ee, el);
        }
        if (piers.Count > 0)
        {
            // Over level piers they're all the same; on a humped road too, as the piers are under its level top.
            m.Add("Pier wall", Cross(pierH.Min(), CrossTabs(pierH.Min())), [], piers.Select(x => Pose.Left(x, 0, -W / 2)));
        }

        m.Note(humped
            ? "Push the walls across into one side wall, then the three road pieces (ramps at the ends), then put the other side wall on all their tabs. A little glue in the slots keeps it."
            : "Push the walls across and the road into one side wall, then put the other side wall on all their tabs. A little glue in the slots keeps it.");
    }

    /// <summary>Tab middles and lengths along a run of road from u0 to u1, about every 16 mm.</summary>
    static List<(double C, double Len)> Tabs(double u0, double u1)
    {
        double len = u1 - u0;
        int count = Math.Max(2, (int)Math.Round(len / 16));
        double tl = Math.Clamp(len / count * 0.4, 2.5, 8);
        return [.. Enumerable.Range(0, count).Select(j => (u0 + len * (j + 0.5) / count, tl))];
    }

    /// <summary>Where a run of road lies: its top on the run, u along it, v across from the front side wall to the back one (or the other way for the second ramp, so it's the same piece as the first).</summary>
    static Pose RoadPose(Run r, double t, double w, bool reversed)
    {
        var u = new V3(r.Dir.x, r.Dir.y, 0);
        var v = reversed ? new V3(0, 0, 1) : new V3(0, 0, -1);
        var n = u.Cross(v);
        return new Pose(new V3(r.A.x, r.A.y, reversed ? -w / 2 : w / 2) - n * (t / 2), u, v);
    }

    /// <summary>One arch's opening (from just under the table up), and its curve from the left springing point over to the right.</summary>
    static (PathD Open, List<PointD> Curve) Opening(ArchShape shape, double x0, double span, double spring)
    {
        double xc = x0 + span / 2;
        var curve = new List<PointD>();
        switch (shape)
        {
            case ArchShape.Segmental:
            {
                double r = span / 4, R = (span * span / 4 + r * r) / (2 * r), cy = spring + r - R;
                double a0 = Math.Atan2(spring - cy, x0 - xc), a1 = Math.Atan2(spring - cy, x0 + span - xc);
                curve.AddRange(Geo.Arc(xc, cy, R, a0, a1, 0.005));
                break;
            }
            case ArchShape.Pointed:
            {
                double R = 0.75 * span, h = Math.Sqrt(R * R - (R - span / 2) * (R - span / 2));
                double cl = x0 + R, cr = x0 + span - R;
                curve.AddRange(Geo.Arc(cl, spring, R, Math.PI, Math.Atan2(h, xc - cl), 0.005));
                curve.AddRange(Geo.Arc(cr, spring, R, Math.Atan2(h, xc - cr), 0, 0.005).Skip(1));
                break;
            }
            default:
                curve.AddRange(Geo.Arc(xc, spring, span / 2, Math.PI, 0, 0.005));
                break;
        }
        var open = new PathD { new(x0, -1) };
        if (spring > 0) open.Add(new PointD(x0, spring));
        open.AddRange(curve);
        if (spring > 0) open.Add(new PointD(x0 + span, spring));
        open.Add(new PointD(x0 + span, -1));
        return (open, curve);
    }

    /// <summary>The wedge-shaped stones round an arch (an odd number, the keystone in the middle standing out a little), shrunk by half the groove.</summary>
    static PathsD Voussoirs(List<PointD> curve, PointD centre, double ring, double stone, double groove)
    {
        var len = new double[curve.Count];
        for (int i = 1; i < curve.Count; i++) len[i] = len[i - 1] + Math.Sqrt((curve[i].x - curve[i - 1].x) * (curve[i].x - curve[i - 1].x) + (curve[i].y - curve[i - 1].y) * (curve[i].y - curve[i - 1].y));
        double total = len[^1];
        int count = Math.Max(3, (int)Math.Round(total / stone));
        if (count % 2 == 0) count++;
        (PointD P, PointD N) At(double sAt)
        {
            int i = Math.Clamp(Array.BinarySearch(len, sAt) is var k && k >= 0 ? k : ~k, 1, curve.Count - 1);
            double t = Math.Clamp((sAt - len[i - 1]) / Math.Max(1e-12, len[i] - len[i - 1]), 0, 1);
            var p = new PointD(curve[i - 1].x + (curve[i].x - curve[i - 1].x) * t, curve[i - 1].y + (curve[i].y - curve[i - 1].y) * t);
            double dx = curve[i].x - curve[i - 1].x, dy = curve[i].y - curve[i - 1].y, l = Math.Max(1e-12, Math.Sqrt(dx * dx + dy * dy));
            var nrm = new PointD(-dy / l, dx / l);
            // Outwards: away from the middle of the arch's foot.
            if ((p.x - centre.x) * nrm.x + (p.y - centre.y) * nrm.y < 0) nrm = new PointD(-nrm.x, -nrm.y);
            return (p, nrm);
        }
        var stones = new PathsD();
        for (int i = 0; i < count; i++)
        {
            var (p0, n0) = At(total * i / count);
            var (p1, n1) = At(total * (i + 1) / count);
            double r = i == count / 2 ? ring + 0.8 : ring;
            var q = new PathD
            {
                new(p0.x - n0.x * 0.5, p0.y - n0.y * 0.5), new(p1.x - n1.x * 0.5, p1.y - n1.y * 0.5),
                new(p1.x + n1.x * r, p1.y + n1.y * r), new(p0.x + n0.x * r, p0.y + n0.y * r),
            };
            stones.AddRange(Geo.Offset(Geo.Tidy([q]), -groove / 2));
        }
        return stones;
    }

    /// <summary>Gaps along the top of a level parapet, the same from both ends.</summary>
    static PathsD Crenels(double length, double topY, double ph)
    {
        double merlon = 5, gap = 4, depth = Math.Min(ph - 1.5, Math.Max(2, ph * 0.45));
        var holes = new PathsD();
        if (depth < 1) return holes;
        int k = (int)Math.Floor((length - merlon) / (merlon + gap));
        if (k < 1) return holes;
        double start = (length - (k * gap + (k + 1) * merlon)) / 2 + merlon;
        for (int i = 0; i < k; i++)
        {
            double x = start + i * (merlon + gap);
            holes.Add(Geo.Rect(x, topY - depth, x + gap, topY + 1));
        }
        return holes;
    }

    /// <summary>
    /// What's etched and scored on a stone face: everything but the raised stones is etched. Inside
    /// <paramref name="keepOff"/> the texture gives way to <paramref name="extra"/> stones (an arch's ring, the coping).
    /// A texture with no stones (plain, corrugated) leaves the face raised.
    /// </summary>
    static (PathsD Etched, List<PathD> Lines) Stonework(PathsD face, TextureParts tex, PathsD keepOff, PathsD extra)
    {
        var open = Geo.Subtract(face, keepOff);
        var raised = tex.Raised.Count > 0 ? Geo.Intersect(Geo.Union(tex.Raised), open) : open;
        raised = Geo.Union(raised, Geo.Intersect(Geo.Union(extra), face));
        var etched = Geo.Subtract(face, raised);
        if (tex.Etched.Count > 0) etched = Geo.Union(etched, Geo.Intersect(Geo.Union(tex.Etched), open));
        return (etched, Geo.ClipLines(tex.Lines, open));
    }

    /// <summary>The road's top: cobbles, flagstones or nothing, over the part between the tabs (u0 along).</summary>
    static (PathsD Etched, List<PathD> Lines) Surface(RoadSurface surface, PathsD deck, double length, double width, double u0, int seed, double groove)
    {
        if (surface == RoadSurface.Plain) return ([], []);
        var face = new Face(length, width, 0, length, width);
        var tex = surface == RoadSurface.Cobbles
            ? Textures.Make("Fieldstone", 3, 2.6, Math.Min(groove, 0.5), seed, null, face)
            : Textures.Make("Stone blocks", 7, 5, Math.Min(groove, 0.5), seed, null, face);
        var moved = new TextureParts(Geo.Move(tex.Raised, u0, 0), Geo.Move(tex.Etched, u0, 0), [.. tex.Lines.Select(l => Geo.Map(l, p => new PointD(p.x + u0, p.y)))]);
        return Stonework(deck, moved, [], []);
    }
}
