using System.Collections.Immutable;
using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// A paper-strip music box movement: how wide its strips are, how far apart its notes are across them, where
/// the first (lowest) one is from the edge, how big a hole it wants, and its notes from the lowest up.
/// </summary>
public sealed record Movement(string Key, string Name, double WidthMm, double PitchMm, double FirstMm, double HoleMm, ImmutableArray<int> Pitches)
{
    public string Tracks => string.Join(" ", Pitches.Select(Notes.Name));

    /// <summary>
    /// The common hand-crank movements. Sizes are the usual ones for each; makers differ a little, so check
    /// yours with the test strip (every note once, lowest first) before cutting a long tune.
    /// </summary>
    public static readonly ImmutableArray<Movement> All =
    [
        new("30", "30-note (C3 to E6, mostly chromatic)", 70, 2, 6, 2.4,
            [48, 50, 55, 57, 59, 60, 62, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 86, 88]),
        new("15", "15-note (C major, two octaves)", 41, 2, 6.5, 2.4,
            [60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84]),
    ];

    public static Movement? Find(string key) => All.FirstOrDefault(m => m.Key == key);
}

/// <summary>
/// A punched strip for a paper-strip music box: each note of the tune is a hole in its note's lane, a beat's
/// length apart along the strip. Too long for the bed, it's cut in pieces that lock together with a dovetail
/// (and a strip of tape over each join). The check finds notes the box doesn't have, holes too close to sound
/// (or so close the card tears), and neighbouring notes whose holes run together.
/// </summary>
public sealed record StripSpec : ToySpec
{
    /// <summary>Which movement (<see cref="Movement.Key"/>), or "custom" for the sizes and notes below.</summary>
    public string Movement { get; init; } = "30";
    public double WidthMm { get; init; } = 70;
    public double PitchMm { get; init; } = 2;
    public double FirstMm { get; init; } = 6;
    public double HoleMm { get; init; } = 2.4;
    /// <summary>The box's notes from the edge nearest the first lane, as names ("C3 D3 G3 …").</summary>
    public string Tracks { get; init; } = Automata.Movement.All[0].Tracks;
    /// <summary>The tune, as text (<see cref="Melody"/>).</summary>
    public string Tune { get; init; } = Melody.Tunes[0].Text;
    /// <summary>Move every note up (+) or down (−) by this many semitones.</summary>
    public int Transpose { get; init; }
    /// <summary>A note the box hasn't got is played an octave (or two) up or down, where it has that one.</summary>
    public bool Octaves { get; init; } = true;
    /// <summary>How much strip one beat takes (mm): longer is slower for the same turning.</summary>
    public double BeatMm { get; init; } = 8;
    /// <summary>Blank strip before the first note, to feed it in by (mm).</summary>
    public double LeadMm { get; init; } = 30;
    /// <summary>Blank strip after the last note (mm).</summary>
    public double TailMm { get; init; } = 15;
    /// <summary>Lanes run from the other edge (as if the strip were turned over).</summary>
    public bool Mirror { get; init; }
    /// <summary>The longest piece to cut (mm): the bed's length less a margin.</summary>
    public double MaxPieceMm { get; init; } = 200;

    /// <summary>How far the dovetail at a join reaches into the next piece (mm).</summary>
    public const double TabMm = 6;
    /// <summary>Strip shown passing the comb for each turn in the motion view (mm).</summary>
    public const double MmPerTurn = 40;

    public StripSpec WithMovement(Movement m) => this with { Movement = m.Key, WidthMm = m.WidthMm, PitchMm = m.PitchMm, FirstMm = m.FirstMm, HoleMm = m.HoleMm, Tracks = m.Tracks };

    /// <summary>The box's notes, from the first lane.</summary>
    public List<int> Lanes()
    {
        var o = new List<int>();
        foreach (var tok in (Tracks ?? "").Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            o.Add(Notes.Parse(tok) ?? throw new InvalidOperationException($"\"{tok}\" in the box's notes isn't a note (write them like C3 D3 G3)."));
        if (o.Count < 2) throw new InvalidOperationException("Give the box at least two notes.");
        if (o.Distinct().Count() != o.Count) throw new InvalidOperationException("The box's notes have one twice.");
        return o;
    }

    /// <summary>A note's lane (from the first), after moving it by <paramref name="shift"/>: exactly, or an octave or two away when <see cref="Octaves"/>; null when the box can't play it.</summary>
    static (int Lane, bool Moved)? LaneOf(int pitch, int shift, Dictionary<int, int> lanes, bool octaves)
    {
        int p = pitch + shift;
        if (lanes.TryGetValue(p, out int l)) return (l, false);
        if (!octaves) return null;
        foreach (int o in new[] { 12, -12, 24, -24, 36, -36 })
            if (lanes.TryGetValue(p + o, out l)) return (l, true);
        return null;
    }

    /// <summary>The shift (semitones, −24 to +24) that leaves fewest notes unplayed, then fewest moved, then nearest 0.</summary>
    public static int BestTranspose(IReadOnlyList<NoteEvent> notes, IReadOnlyList<int> lanes, bool octaves)
    {
        var map = lanes.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        int best = 0;
        (int Missing, int Moved, int Abs) score = (int.MaxValue, 0, 0);
        for (int s = -24; s <= 24; s++)
        {
            int miss = 0, moved = 0;
            foreach (var n in notes)
            {
                var l = LaneOf(n.Pitch, s, map, octaves);
                if (l is null) miss++;
                else if (l.Value.Moved) moved++;
            }
            var sc = (miss, moved, Math.Abs(s));
            if (sc.CompareTo(score) < 0) { score = sc; best = s; }
        }
        return best;
    }

    /// <summary>A hole: where along the strip (mm), which lane, and the note it plays.</summary>
    public readonly record struct Punch(double X, int Lane, int Pitch);

    /// <summary>The holes for the tune, and what couldn't be played.</summary>
    public (List<Punch> Holes, List<int> Missing, int Moved, Melody.Result Tune) Punches()
    {
        var lanes = Lanes();
        var map = lanes.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var tune = Melody.Parse(Tune);
        var holes = new HashSet<Punch>();
        var missing = new List<int>();
        int moved = 0;
        foreach (var n in tune.Notes)
        {
            var l = LaneOf(n.Pitch, Transpose, map, Octaves);
            if (l is null) { missing.Add(n.Pitch + Transpose); continue; }
            if (l.Value.Moved) moved++;
            holes.Add(new Punch(Math.Round(LeadMm + n.Beat * BeatMm, 3), l.Value.Lane, lanes[l.Value.Lane]));
        }
        return ([.. holes.OrderBy(h => h.X).ThenBy(h => h.Lane)], missing, moved, tune);
    }

    double LaneY(int lane, int lanes) => Mirror ? WidthMm - FirstMm - lane * PitchMm : FirstMm + lane * PitchMm;

    public override Mech Build(CancellationToken ct = default)
    {
        if (!(WidthMm >= 10)) throw new InvalidOperationException("Make the strip at least 10 mm wide.");
        if (!(PitchMm > 0.5)) throw new InvalidOperationException("The notes must be more than 0.5 mm apart across the strip.");
        if (!(BeatMm >= 1)) throw new InvalidOperationException("Give a beat at least 1 mm of strip.");
        if (!(HoleMm >= 0.5)) throw new InvalidOperationException("Make the holes at least 0.5 mm across.");
        var lanes = Lanes();
        double lastLane = FirstMm + (lanes.Count - 1) * PitchMm;
        if (FirstMm - HoleMm / 2 < 1 || lastLane + HoleMm / 2 > WidthMm - 1)
            throw new InvalidOperationException($"{lanes.Count} notes {PitchMm:0.##} mm apart from {FirstMm:0.##} mm in don't fit across a {WidthMm:0.##} mm strip.");
        var (holes, missing, moved, tune) = Punches();
        var mech = new Mech { Title = "Music box strip", Cycle = 2 * Math.PI * Math.Max(1, (LeadMm + tune.Beats * BeatMm + TailMm) / MmPerTurn) };
        foreach (var p in tune.Problems.Take(4)) mech.Findings.Add(new(Severity.Bad, p));
        if (tune.Problems.Count > 4) mech.Findings.Add(new(Severity.Bad, $"…and {tune.Problems.Count - 4} more things in the tune that aren't notes."));
        if (holes.Count == 0) throw new InvalidOperationException(tune.Notes.Count == 0 ? "Write a tune: notes like C5 D5 E5, a space between each." : "None of the tune's notes are on this box: try Fit it to the box.");

        double r = HoleMm / 2;
        double length = Math.Max(holes[^1].X + TailMm, LeadMm + TailMm + 10);
        length = Math.Max(length, LeadMm + tune.Beats * BeatMm + 2);

        // ---- what the check finds ----
        if (missing.Count > 0)
        {
            var names = missing.Distinct().Order().Select(Notes.Name).ToList();
            int best = BestTranspose(tune.Notes, lanes, Octaves);
            string fix = best != Transpose ? $" Moving the tune {(best > Transpose ? "up" : "down")} {Math.Abs(best - Transpose)} semitone{(Math.Abs(best - Transpose) == 1 ? "" : "s")} (Fit it to the box) plays more of them." : Octaves ? "" : " Ticking \"Play missing notes an octave up or down\" may help.";
            mech.Findings.Add(new(Severity.Warn, $"{missing.Count} note{(missing.Count == 1 ? "" : "s")} left out: the box has no {Say.And(names.Take(8))}{(names.Count > 8 ? " and others" : "")}.{fix}"));
        }
        if (moved > 0) mech.Findings.Add(new(Severity.Note, $"{moved} note{(moved == 1 ? " is" : "s are")} played an octave up or down, where the box has no note that low or high."));
        if (missing.Count == 0) mech.Findings.Add(new(Severity.Good, $"Every note of the tune is on the box ({holes.Count} holes)."));

        var tear = new List<Punch>();
        var quick = new List<Punch>();
        foreach (var lane in holes.GroupBy(h => h.Lane))
        {
            var xs = lane.OrderBy(h => h.X).ToList();
            for (int i = 1; i < xs.Count; i++)
            {
                double gap = xs[i].X - xs[i - 1].X;
                if (gap - HoleMm < 0.8) tear.Add(xs[i]);
                else if (gap < 2 * HoleMm + 0.5) quick.Add(xs[i]);
            }
        }
        double minBeat = 0.8 + HoleMm;
        if (tear.Count > 0)
            mech.Findings.Add(new(Severity.Bad, $"{tear.Count} repeated note{(tear.Count == 1 ? "" : "s")} {(tear.Count == 1 ? "is" : "are")} so close the holes run together and the card tears (first: {Notes.Name(tear[0].Pitch)} at {tear[0].X - LeadMm:0} mm). Give a beat more strip (at least {Math.Ceiling(minBeat / Math.Max(1e-3, ShortestRepeat(holes) / BeatMm)):0} mm)."));
        if (quick.Count > 0)
            mech.Findings.Add(new(Severity.Warn, $"{quick.Count} repeated note{(quick.Count == 1 ? " comes" : "s come")} so soon after the last that the tooth may not have sprung back to sound it again (first: {Notes.Name(quick[0].Pitch)}). A longer beat spaces them out."));
        if (tear.Count == 0 && quick.Count == 0) mech.Findings.Add(new(Severity.Good, "No note repeats too soon for its tooth to sound again."));
        var merged = new List<Punch>();
        if (PitchMm < HoleMm + 0.6)
            foreach (var g in holes.GroupBy(h => h.X))
            {
                var ls = g.Select(h => h.Lane).ToHashSet();
                merged.AddRange(g.Where(h => ls.Contains(h.Lane + 1)));
            }
        if (merged.Count > 0)
            mech.Findings.Add(new(Severity.Warn, $"{merged.Count} time{(merged.Count == 1 ? "" : "s")} two neighbouring notes play together, and their holes ({HoleMm:0.##} mm, {PitchMm:0.##} mm apart) run into one."));

        // ---- the strip, and where to cut it into pieces ----
        double W = WidthMm;
        var holePaths = new Paths64(holes.Select(h => Poly.Circle(new Vec2(h.X, LaneY(h.Lane, lanes.Count)), r, 0.003)));
        var cuts = Joins(holes, length, r);
        double nose = Math.Min(15, LeadMm * 0.6);
        var whole = new Paths64
        {
            Poly.Path([new Vec2(0, W * 0.25), new Vec2(nose, 0), new Vec2(length, 0), new Vec2(length, W), new Vec2(nose, W), new Vec2(0, W * 0.75)]),
        };
        whole = Poly.Subtract(whole, holePaths);
        int count = cuts.Count + 1;
        for (int k = 0; k < count; k++)
        {
            ct.ThrowIfCancellationRequested();
            double x0 = k == 0 ? 0 : cuts[k - 1].X, x1 = k == cuts.Count ? length : cuts[k].X;
            var piece = Poly.Intersect(whole, [Poly.Rect(x0, -1, x1, W + 1)]);
            if (k < cuts.Count && cuts[k].Tab) piece = Poly.Union(piece, Poly.Subtract([Dovetail(x1, W, 0)], holePaths));
            if (k > 0 && cuts[k - 1].Tab) piece = Poly.Subtract(piece, [Dovetail(x0, W, 0.05)]);
            var marks = new List<List<Vec2>>();
            double ay = Mirror ? FirstMm / 2 : W - FirstMm / 2;
            double ax = x0 + (k == 0 ? nose + 4 : TabMm + 4);
            if (ax + 14 < x1)
            {
                marks.Add([new Vec2(ax, ay), new Vec2(ax + 12, ay)]);
                marks.Add([new Vec2(ax + 9.5, ay + 1.5), new Vec2(ax + 12, ay), new Vec2(ax + 9.5, ay - 1.5)]);
            }
            mech.Parts.Add(new Part
            {
                Name = count == 1 ? "Strip" : $"Strip {k + 1} of {count}",
                Shape = piece,
                Marks = marks,
                Label = count > 1 && ax + 24 < x1 ? new PartLabel($"{k + 1}", new Vec2(ax + 19, ay), Math.Min(3, FirstMm - 1.5)) : null,
            });
        }
        if (count > 1)
        {
            int taped = cuts.Count(c => !c.Tab);
            mech.Findings.Add(new(Severity.Note, $"Cut in {count} pieces (each up to {MaxPieceMm:0} mm, to fit the bed): {(taped == 0 ? "each join is a dovetail" : taped == cuts.Count ? "the joins are straight, where there was no room for a dovetail between the holes" : $"{cuts.Count - taped} joins are dovetails, {taped} straight")}. Push them together and put clear tape over each join, on both sides."));
        }
        mech.Shopping.Add($"A {(Automata.Movement.Find(Movement)?.Name ?? $"{lanes.Count}-note").Split(" (")[0]} paper-strip music box movement");
        mech.Shopping.Add("Thin card the same thickness as the strips that came with it (usually about 0.25 mm, 160 to 200 gsm)");
        if (count > 1) mech.Shopping.Add("Clear tape for the joins");

        // ---- watching it play: the strip going past the comb ----
        const double CombX = 46, ViewW = 150;
        double Along(double a) => a / (2 * Math.PI) * MmPerTurn - 10;
        var view = new Rect2(-4, -6, ViewW, W + 6);
        var clip = new Rect2(-4, -2, ViewW, W + 2);
        var reading = new Paths64 { Poly.Rect(CombX - 0.6, -4, CombX + 0.6, W + 4) };
        var laneY = Enumerable.Range(0, lanes.Count).Select(i => LaneY(i, lanes.Count)).ToArray();
        var bodies = new List<Body>
        {
            new("Strip", whole, 0xE9DFC8, _ => Pose.Zero) { ShapeAt = a => Poly.Clip(Poly.Move(whole, 0, CombX - Along(a), 0), clip) },
            new("Comb", reading, Tints.Steel, _ => Pose.Zero),
        };
        foreach (var c in cuts)
            bodies.Add(new Body("Join", [Poly.Rect(c.X - 0.3, 0, c.X + 0.3, W)], Tints.Of(1), _ => Pose.Zero)
            {
                ShapeAt = a => Poly.Clip([Poly.Rect(CombX - Along(a) + c.X - 0.3, 0, CombX - Along(a) + c.X + 0.3, W)], clip),
            });
        var bad = tear.Concat(merged).ToHashSet();
        mech.Panels.Add(new PlanarPanel
        {
            Title = "The strip going through the box",
            Bodies = bodies,
            Extent = view,
            Overlay = a =>
            {
                double at = Along(a);
                var o = new List<(List<Vec2>, uint)>();
                foreach (var h in holes)
                {
                    double dx = h.X - at;
                    if (Math.Abs(dx) > r) continue;
                    double y = laneY[h.Lane];
                    o.Add(([new Vec2(CombX - 6, y), new Vec2(CombX + 6, y)], bad.Contains(h) ? Tints.Problem : Tints.Of(2)));
                }
                return o;
            },
        });
        // The strip under the picture: red where holes tear, orange where a repeat comes too soon.
        double total = mech.Cycle;
        foreach (var (list, level) in new[] { (tear.Concat(merged).ToList(), Severity.Bad), (quick, Severity.Warn) })
            foreach (var h in list)
            {
                double a = (h.X + 10) / MmPerTurn * 2 * Math.PI;
                int k = (int)Math.Round(a / total * Mech.StripSamples);
                mech.Mark(k - 1, k + 2, level);
            }

        double beats = tune.Beats;
        mech.Summary = $"A strip {length:0} mm long{(count > 1 ? $" in {count} pieces" : "")} with {holes.Count} holes: {beats:0.#} beats of music on a {lanes.Count}-note box.";
        return mech;
    }

    /// <summary>The shortest gap between two holes in one lane (mm), or the beat's length when nothing repeats.</summary>
    double ShortestRepeat(List<Punch> holes)
    {
        double best = double.PositiveInfinity;
        foreach (var lane in holes.GroupBy(h => h.Lane))
        {
            var xs = lane.Select(h => h.X).Order().ToList();
            for (int i = 1; i < xs.Count; i++) best = Math.Min(best, xs[i] - xs[i - 1]);
        }
        return double.IsFinite(best) ? best : BeatMm;
    }

    /// <summary>A dovetail across the join at <paramref name="x"/>: narrow at the join, wider into the next piece; <paramref name="grow"/> for the notch it fits.</summary>
    static Path64 Dovetail(double x, double w, double grow)
    {
        double m = w / 2, n = Math.Min(4, w * 0.12) + grow, f = Math.Min(6.5, w * 0.18) + grow;
        return Poly.Path([new Vec2(x - 0.01, m - n), new Vec2(x + TabMm + grow, m - f), new Vec2(x + TabMm + grow, m + f), new Vec2(x - 0.01, m + n)]);
    }

    /// <summary>
    /// Where to cut a strip too long for the bed: as far along as a piece may go, at a place clear of holes
    /// (with room for the dovetail into the next piece where there is; a straight join otherwise).
    /// </summary>
    List<(double X, bool Tab)> Joins(List<Punch> holes, double length, double r)
    {
        var o = new List<(double, bool)>();
        double max = Math.Max(40, MaxPieceMm);
        double start = 0;
        var xs = holes.Select(h => h.X).Distinct().Order().ToArray();
        bool Clear(double from, double to) => !xs.Any(x => x + r > from && x - r < to);
        while (length - start > max)
        {
            double x = -1;
            bool tab = false;
            for (double c = start + max - TabMm; c > start + max * 0.5; c -= 0.5)
                if (Clear(c - 1.5, c + TabMm + 1.5)) { x = c; tab = true; break; }
            if (x < 0)
                for (double c = start + max; c > start + max * 0.5; c -= 0.25)
                    if (Clear(c - 1.2, c + 1.2)) { x = c; break; }
            if (x < 0) x = start + max;   // nowhere clear: cut through (the check warns of a note this close to the join)
            o.Add((x, tab));
            start = x;
        }
        return o;
    }
}
