using System.Collections.Immutable;
using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// A metal a comb can be cut from: how stiff it is (Young's modulus, GPa) and how heavy (kg/m³), which set
/// its tines' notes, and whether it still needs hardening once it's cut.
/// </summary>
public sealed record CombMetal(string Key, string Name, double ModulusGPa, double Density, bool NeedsHardening, string Buy)
{
    /// <summary>
    /// The usual ones. Hardness hardly changes the stiffness (so the note), but a soft tine takes a set when
    /// it's plucked and sounds dull: spring-hard steel is what real combs are made of.
    /// </summary>
    public static readonly ImmutableArray<CombMetal> All =
    [
        new("1095", "Blue-tempered 1095 spring steel (already hardened)", 205, 7850, false, "Blue-tempered 1095 spring steel shim"),
        new("annealed", "Annealed 1075 or 1095 (harden it after cutting)", 205, 7850, true, "Annealed 1075 or 1095 spring steel sheet"),
        new("301", "301 stainless, full hard", 193, 7900, false, "Full-hard 301 stainless spring steel shim"),
        new("bronze", "Phosphor bronze, spring temper", 110, 8860, false, "Spring-temper phosphor bronze sheet"),
        new("brass", "Brass, half hard", 100, 8530, false, "Half-hard brass shim"),
    ];

    public static CombMetal Find(string key) => All.FirstOrDefault(m => m.Key == key) ?? All[0];
}

/// <summary>
/// A music box comb to cut from thin spring steel: a tine for each note, as far apart as the lanes of the
/// maker's punched strips, so the strips and the comb line up. Each tine is a cantilever, clamped at its
/// root and free at its tip, ringing in its first bending mode:
/// <code>f = (1.8751² / 2π) · (t / L²) · √(E / 12ρ)</code>
/// so for one thickness <c>t</c> its length is <c>L = √(k·t / f)</c>. The width cancels out of that, so
/// it only sets how loud the tine is and how hard it is to pluck: here it's the lane spacing less a gap.
/// The tips line up (where the strip plucks them) and the roots step, as on a real comb; two thick clamp
/// bars with the same stepped edge hold each tine at its root, bolted through the comb's base.
/// It's ballpark: a clamp is never perfectly stiff, so the notes come out close, usually a little flat,
/// and are finished by filing (as real combs are tuned by grinding).
/// </summary>
public sealed record CombSpec : ToySpec
{
    public CombSpec() => ThicknessMm = 0.508;

    /// <summary>Which movement's strips it plays (<see cref="Movement.Key"/>), or "custom" for the sizes and notes below.</summary>
    public string Movement { get; init; } = "15";
    public double WidthMm { get; init; } = 41;
    public double PitchMm { get; init; } = 2;
    public double FirstMm { get; init; } = 6.5;
    /// <summary>The notes from the first lane, as names ("C4 D4 E4 …").</summary>
    public string Tracks { get; init; } = Automata.Movement.All[1].Tracks;
    /// <summary>Tines from the other edge (as the strip's "Lanes from the other edge").</summary>
    public bool Mirror { get; init; }
    /// <summary>Make it sound this many octaves above (+) or below (−) the lanes' names.</summary>
    public int Octave { get; init; }

    /// <summary>Which metal (<see cref="CombMetal.Key"/>).</summary>
    public string Metal { get; init; } = "1095";
    /// <summary>The slot between neighbouring tines (mm): each tine is the lane spacing less this wide.</summary>
    public double GapMm { get; init; } = 0.4;
    /// <summary>Each tine this much longer than its note wants (mm), so it comes out flat and is tuned up by filing its tip.</summary>
    public double ExtraMm { get; init; }
    /// <summary>The tips in a line (as on a real comb, where the strip plucks them); otherwise the roots are, and any straight bar clamps it.</summary>
    public bool AlignTips { get; init; } = true;
    /// <summary>The solid base behind the longest tine's root, where the bolts go (mm).</summary>
    public double BaseMm { get; init; } = 12;
    /// <summary>Base past the outside tines, each side (mm).</summary>
    public double WingMm { get; init; } = 8;
    /// <summary>The clamp bars' thickness (mm): thick, stiff metal, or the notes go flat and dull.</summary>
    public double ClampMm { get; init; } = 3;
    /// <summary>The bolts through the clamp bars and the base (metric size, mm).</summary>
    public double BoltMm { get; init; } = 3;

    /// <summary>The first bending mode of a cantilever: β₁ = 1.8751.</summary>
    const double Beta1 = 1.87510407;
    /// <summary>Concert pitch: A4 (MIDI 69).</summary>
    public const double A4 = 440;

    /// <summary>Spring steel shim sizes as sold (inches: 0.010", 0.012", 0.015", 0.020", 0.025", 0.032", 0.040"), in mm.</summary>
    public static readonly ImmutableArray<double> Stock = [0.254, 0.305, 0.381, 0.508, 0.635, 0.813, 1.016];
    /// <summary>What <see cref="Suggest"/> aims for: the bass tine no longer than this (mm)…</summary>
    public const double LongestWanted = 45;
    /// <summary>…and the treble tine no shorter than this (mm).</summary>
    public const double ShortestWanted = 10;

    public CombSpec WithMovement(Movement m) => this with { Movement = m.Key, WidthMm = m.WidthMm, PitchMm = m.PitchMm, FirstMm = m.FirstMm, Tracks = m.Tracks };

    public CombMetal MetalUsed => CombMetal.Find(Metal);

    public static double Hz(int pitch) => A4 * Math.Pow(2, (pitch - 69) / 12.0);

    /// <summary>k in f = k·t/L² for this metal (t and L in mm, f in Hz).</summary>
    public static double Stiffness(CombMetal m) => Beta1 * Beta1 / (2 * Math.PI) * Math.Sqrt(m.ModulusGPa * 1e9 / (12 * m.Density)) * 1000;

    /// <summary>How long a tine <paramref name="thickMm"/> thick must be to ring at <paramref name="hz"/> (mm, tip to clamp).</summary>
    public static double LengthFor(double hz, double thickMm, CombMetal m) => Math.Sqrt(Stiffness(m) * thickMm / hz);

    /// <summary>The note a tine rings at (Hz).</summary>
    public static double FrequencyOf(double lengthMm, double thickMm, CombMetal m) => Stiffness(m) * thickMm / (lengthMm * lengthMm);

    /// <summary>The comb's notes, from the first lane (as the strip's).</summary>
    public List<int> Lanes() => new StripSpec { Tracks = Tracks }.Lanes();

    public double TineWidthMm => PitchMm - GapMm;

    /// <summary>One tine: its lane, the note it plays, where its middle is across the strip, and how long it is.</summary>
    public readonly record struct Tine(int Lane, int Pitch, double Hz, double X, double LengthMm);

    public List<Tine> Tines()
    {
        var lanes = Lanes();
        var metal = MetalUsed;
        var o = new List<Tine>();
        for (int i = 0; i < lanes.Count; i++)
        {
            int p = lanes[i] + 12 * Octave;
            double hz = Hz(p);
            double x = Mirror ? WidthMm - FirstMm - i * PitchMm : FirstMm + i * PitchMm;
            o.Add(new Tine(i, p, hz, x, LengthFor(hz, ThicknessMm, metal) + ExtraMm));
        }
        return o;
    }

    /// <summary>
    /// The shim size to cut it from: the thickest that keeps the bass tine within <see cref="LongestWanted"/>,
    /// the treble one at least <see cref="ShortestWanted"/> and the tines at least twice as wide as they're
    /// thick (thicker is louder); failing that, the thinnest that keeps the treble tines long enough.
    /// </summary>
    public double Suggest()
    {
        var hz = Lanes().Select(p => Hz(p + 12 * Octave)).ToList();
        double lo = hz.Min(), hi = hz.Max(), w = TineWidthMm;
        var metal = MetalUsed;
        bool Short(double t) => LengthFor(hi, t, metal) >= ShortestWanted;
        foreach (double t in Stock.Reverse())
            if (LengthFor(lo, t, metal) <= LongestWanted && Short(t) && t <= w / 2 + 1e-9) return t;
        foreach (double t in Stock)
            if (Short(t) && t <= w / 2 + 1e-9) return t;
        return Stock[0];
    }

    public override Mech Build(CancellationToken ct = default)
    {
        if (!(ThicknessMm >= 0.1 && ThicknessMm <= 3)) throw new InvalidOperationException("Make the comb 0.1 to 3 mm thick.");
        if (!(PitchMm >= 0.8)) throw new InvalidOperationException("The tines must be at least 0.8 mm apart.");
        if (!(GapMm >= 0.1)) throw new InvalidOperationException("Leave a gap of at least 0.1 mm between the tines.");
        if (TineWidthMm < 0.3) throw new InvalidOperationException($"With {PitchMm:0.##} mm between lanes, a {GapMm:0.##} mm gap leaves tines only {TineWidthMm:0.##} mm wide: make the gap smaller.");
        if (!(BoltMm >= 1.5 && BoltMm <= 8)) throw new InvalidOperationException("Use bolts from M1.5 to M8.");
        if (!(BaseMm >= BoltMm + 5)) throw new InvalidOperationException($"Give the base at least {BoltMm + 5:0.#} mm behind the longest tine, for the bolts.");
        if (!(ClampMm >= 1)) throw new InvalidOperationException("Make the clamp bars at least 1 mm thick (3 mm or more holds the notes best).");
        if (!(WingMm >= 0)) throw new InvalidOperationException("The base can't be narrower than the tines.");
        if (!(ExtraMm >= 0 && ExtraMm <= 5)) throw new InvalidOperationException("Keep the tuning allowance between 0 and 5 mm.");
        var metal = MetalUsed;
        var tines = Tines();
        int n = tines.Count;
        double t = ThicknessMm, w = TineWidthMm, p = PitchMm, g = GapMm;
        var byX = tines.OrderBy(x => x.X).ToList();
        double lMax = tines.Max(x => x.LengthMm), lMin = tines.Min(x => x.LengthMm);
        // Tips on y = 0 and roots at their length, or roots on one line and tips stepped.
        double Tip(Tine x) => AlignTips ? 0 : lMax - x.LengthMm;
        double Root(Tine x) => AlignTips ? x.LengthMm : lMax;
        double back = lMax + BaseMm;
        double x0 = byX[0].X - p / 2 - WingMm, x1 = byX[^1].X + p / 2 + WingMm;

        // ---- the comb: a plate with a slot between each two tines, down to the deeper root of the two ----
        var comb = new Paths64 { Poly.Rect(x0, 0, x1, back) };
        var cuts = new Paths64();
        for (int i = 0; i + 1 < byX.Count; i++)
        {
            double a = byX[i].X + w / 2, b = byX[i + 1].X - w / 2;
            double deep = Math.Max(Root(byX[i]), Root(byX[i + 1]));
            cuts.Add(Poly.Rect(a, -1, b, deep));
            cuts.Add(Poly.Circle(new Vec2((a + b) / 2, deep), (b - a) / 2, 0.002));
        }
        cuts.Add(Poly.Rect(x0 - 1, -1, byX[0].X - w / 2, Root(byX[0])));
        cuts.Add(Poly.Rect(byX[^1].X + w / 2, -1, x1 + 1, Root(byX[^1])));
        foreach (var x in tines)
            if (Tip(x) > 0.01) cuts.Add(Poly.Rect(x.X - w / 2 - 0.01, -1, x.X + w / 2 + 0.01, Tip(x)));
        double boltR = BoltMm / 2 + 0.15, boltY = lMax + BaseMm / 2;
        double edge = Math.Max(4, 1.6 * BoltMm);
        int bolts = Math.Max(2, (int)Math.Ceiling((x1 - x0 - 2 * edge) / 25) + 1);
        var holes = new Paths64(Enumerable.Range(0, bolts).Select(k => Poly.Circle(new Vec2(x0 + edge + k * (x1 - x0 - 2 * edge) / (bolts - 1), boltY), boltR, 0.002)));
        comb = Poly.Subtract(comb, Poly.Union(cuts));
        comb = Poly.Subtract(comb, holes);

        // ---- the clamp bars: the base, with a step at each tine's root ----
        var clamp = new Paths64 { Poly.Rect(x0, lMax, x1, back) };
        foreach (var x in tines) clamp.Add(Poly.Rect(x.X - p / 2, Root(x), x.X + p / 2, back));
        clamp.Add(Poly.Rect(x0, Root(byX[0]), byX[0].X - p / 2 + 0.001, back));
        clamp.Add(Poly.Rect(byX[^1].X + p / 2 - 0.001, Root(byX[^1]), x1, back));
        clamp = Poly.Subtract(Poly.Union(clamp), holes);
        double Front(double x)
        {
            if (x < byX[0].X - p / 2) return Root(byX[0]);
            if (x > byX[^1].X + p / 2) return Root(byX[^1]);
            return Root(byX.MinBy(tn => Math.Abs(tn.X - x)));
        }
        double h = Math.Min(1.6, 0.8 * p);
        var labels = tines.Select(x =>
        {
            string name = Notes.Name(x.Pitch);
            return new PartLabel(name, new Vec2(x.X, Root(x) + 0.8 + 0.36 * h * name.Length), h, 90);
        }).ToList();
        var marks = new List<List<Vec2>>();
        foreach (double ex in new[] { 0.0, WidthMm })
            if (ex > x0 + 0.5 && ex < x1 - 0.5 && byX.All(tn => Math.Abs(tn.X - ex) > p / 2 + 0.3))
                marks.Add([new Vec2(ex, Front(ex) + 0.6), new Vec2(ex, Math.Min(back - 1, Front(ex) + 5))]);

        var m = new Mech { Title = "Music box comb", Cycle = 2 * Math.PI * n };
        m.Parts.Add(new Part { Name = "Comb", Shape = comb });
        m.Parts.Add(new Part
        {
            Name = "Clamp bar",
            Shape = clamp,
            Copies = 2,
            Stock = $"{ClampMm:0.#} mm steel",
            Labels = labels,
            Marks = marks,
        });

        // ---- what the check finds ----
        var lo = tines.MinBy(x => x.Hz);
        var hi = tines.MaxBy(x => x.Hz);
        string movement = Automata.Movement.Find(Movement)?.Name.Split(" (")[0] ?? $"{n}-note";
        m.Findings.Add(new(Severity.Good, $"Tines {p:0.##} mm apart, the {(Mirror ? "lowest" : "first")} {FirstMm:0.##} mm from the strip's {(Mirror ? "far " : "")}edge: the lanes of the {movement} strips this maker punches, so they line up."));
        m.Findings.Add(new(Severity.Note, "Each tine, tip to clamp: " + string.Join(" · ", tines.Select(x => $"{Notes.Name(x.Pitch)} {x.Hz:0.#} Hz {x.LengthMm:0.0} mm")) + "."));
        double k1 = Beta1 * Beta1 / (2 * Math.PI * Math.Sqrt(12));
        m.Findings.Add(new(Severity.Note, $"Ballpark: worked out as a cantilever, f = {k1:0.###} · t/L² · √(E/ρ), with E = {metal.ModulusGPa:0} GPa and ρ = {metal.Density:0} kg/m³. The width ({w:0.##} mm) doesn't change the note, only how loud it is. Expect each note within about a semitone, most likely a little flat, since no clamp is perfectly stiff."));
        m.Findings.Add(new(Severity.Note, "Tune each tine once it's clamped, with a tuner app: file a little off its tip to raise it (1% shorter is about a third of a semitone higher), or thin it near the root to lower it. Keep it cool: dip it in water rather than let it go blue."));
        if (ExtraMm > 0)
        {
            double cents = 1200 * Math.Log2(FrequencyOf(lMin - ExtraMm, t, metal) / FrequencyOf(lMin, t, metal));
            m.Findings.Add(new(Severity.Note, $"Each tine is {ExtraMm:0.##} mm long so it comes out flat (the shortest about {cents:0} cents): tune them all up by filing the tips."));
        }
        var tooShort = tines.Where(x => x.LengthMm < Math.Max(ShortestWanted, 12 * t)).ToList();
        if (tooShort.Count > 0)
            m.Findings.Add(new(Severity.Warn, $"{Say.And(tooShort.Take(6).Select(x => Notes.Name(x.Pitch)))}{(tooShort.Count > 6 ? " and others" : "")} {(tooShort.Count == 1 ? "is" : "are")} short and stubby ({tooShort.Min(x => x.LengthMm):0.0} mm in {t:0.###} mm stock): hard to pluck and to tune, and likely to come out flat. Thinner stock (Suggest) or the comb an octave down lengthens them."));
        var tooLong = tines.Where(x => x.LengthMm > 60).ToList();
        if (tooLong.Count > 0)
            m.Findings.Add(new(Severity.Warn, $"{Say.And(tooLong.Take(6).Select(x => Notes.Name(x.Pitch)))}{(tooLong.Count > 6 ? " and others" : "")} {(tooLong.Count == 1 ? "is" : "are")} long ({lMax:0} mm): long tines ring quietly and can buzz. Thicker stock or the comb an octave up shortens them (real combs weight their bass tines instead)."));
        if (w < 1.5 * t)
            m.Findings.Add(new(Severity.Warn, $"The tines are only {w / t:0.0} times as wide as they're thick, so they'll wobble sideways as much as up and down. Use thinner stock or a smaller gap."));
        if (g < 0.25)
            m.Findings.Add(new(Severity.Warn, $"A {g:0.##} mm gap is tight: ringing tines may touch, and the slivers cut out of the slots may not drop free."));
        if (metal.NeedsHardening)
            m.Findings.Add(new(Severity.Warn, "Annealed steel is soft: cut it as it is, then harden and temper it (see the help), or the tines bend when they're plucked and stay bent. Hardening doesn't change the notes much."));
        else if (metal.Key is "1095" or "301")
            m.Findings.Add(new(Severity.Good, $"{metal.Name.Split(" (")[0]} is already spring-hard, so it needs no heat treatment: the laser only heats a hair's width along each edge."));
        else
            m.Findings.Add(new(Severity.Note, $"{metal.Name.Split(',')[0]} rings more softly and dies away sooner than spring steel, but it's easy to cut and file."));
        if (!AlignTips)
            m.Findings.Add(new(Severity.Note, "The roots line up, so a plain straight bar clamps it, but the tips are stepped: whatever plucks them must reach each one."));
        double clampTotal = 2 * ClampMm + t;
        int boltLen = new[] { 6, 8, 10, 12, 16, 20, 25, 30 }.FirstOrDefault(L => L >= clampTotal + BoltMm + 2, 30);

        m.Shopping.Add($"{metal.Buy}, {t:0.###} mm ({t / 25.4:0.000}\") thick, at least {x1 - x0 + 10:0} × {back + 10:0} mm");
        m.Shopping.Add($"{ClampMm:0.#} mm steel plate (or aluminium) for the two clamp bars: they're on a page of their own, to cut with that plate's settings");
        m.Shopping.Add($"{bolts} M{BoltMm:0.#} bolts {boltLen} mm long, with nuts");
        m.Shopping.Add("A tuner app, and a fine file or a grinder with a fine wheel to tune the tines");

        // ---- watching it play: the comb from above, and the tine playing from the side ----
        var top = new List<Body>
        {
            new("Comb", comb, Tints.Steel, _ => Pose.Zero),
            new("Clamp bar", clamp, Tints.Frame, _ => Pose.Zero),
        };
        int Playing(double a) => (int)Math.Clamp(Math.Floor(a / (2 * Math.PI)), 0, n - 1);
        double Phase(double a) => a / (2 * Math.PI) - Math.Floor(a / (2 * Math.PI));
        m.Panels.Add(new PlanarPanel
        {
            Title = "The comb from above (each tine plays in turn, lowest first)",
            Weight = 2,
            Bodies = top,
            Extent = new Rect2(x0 - 3, -4, x1 + 3, back + 3),
            Overlay = a =>
            {
                var x = tines[Playing(a)];
                double y0 = Tip(x), y1 = Root(x), l = x.X - w / 2 - 0.25, r = x.X + w / 2 + 0.25;
                return [([new Vec2(l, y1), new Vec2(l, y0 - 0.25), new Vec2(r, y0 - 0.25), new Vec2(r, y1)], Tints.Of(2))];
            },
        });
        // Side view: tip at x = 0, root at x = L; the tine pushed down by a pin, then let go to ring and die away.
        double Shape(double xi)
        {
            // The first mode's shape (0 at the root, 2 at the tip).
            double b = Beta1 * xi, sigma = 0.734095514;
            return Math.Cosh(b) - Math.Cos(b) - sigma * (Math.Sinh(b) - Math.Sin(b));
        }
        double Lift(double a, double len)
        {
            double ph = Phase(a), amp = 0.07 * len;
            if (ph < 0.15) return -amp * ph / 0.15;
            double s = ph - 0.15;
            return -amp * Math.Exp(-3.5 * s) * Math.Cos(2 * Math.PI * 7 * s);
        }
        Paths64 Bar(double a, bool upper)
        {
            double len = tines[Playing(a)].LengthMm;
            return upper ? [Poly.Rect(len, t / 2, len + BaseMm, t / 2 + ClampMm)] : [Poly.Rect(len, -t / 2 - ClampMm, len + BaseMm, -t / 2)];
        }
        var side = new List<Body>
        {
            new("Clamp bar", [Poly.Rect(0, t / 2, BaseMm, t / 2 + ClampMm)], Tints.Frame, _ => Pose.Zero) { ShapeAt = a => Bar(a, true) },
            new("Clamp bar", [Poly.Rect(0, -t / 2 - ClampMm, BaseMm, -t / 2)], Tints.Frame, _ => Pose.Zero) { ShapeAt = a => Bar(a, false) },
            new("Tine", [Poly.Rect(0, -t / 2, lMax + BaseMm, t / 2)], Tints.Steel, _ => Pose.Zero)
            {
                ShapeAt = a =>
                {
                    double len = tines[Playing(a)].LengthMm, tipY = Lift(a, len);
                    const int N = 40;
                    var upper = new List<Vec2>();
                    var lower = new List<Vec2>();
                    for (int i = 0; i <= N; i++)
                    {
                        double xi = (double)i / N;   // 0 at the root, 1 at the tip
                        double y = tipY * Shape(xi) / 2;
                        upper.Add(new Vec2(len * (1 - xi), y + t / 2));
                        lower.Add(new Vec2(len * (1 - xi), y - t / 2));
                    }
                    lower.Reverse();
                    var pts = new List<Vec2> { new(len + BaseMm, t / 2) };
                    pts.AddRange(upper);
                    pts.AddRange(lower);
                    pts.Add(new Vec2(len + BaseMm, -t / 2));
                    return [Poly.Path(pts)];
                },
            },
        };
        double reach = 0.07 * lMax + ClampMm + t + 2;
        m.Panels.Add(new PlanarPanel
        {
            Title = "The tine playing, from the side (its swing much enlarged)",
            Bodies = side,
            Extent = new Rect2(-3, -reach, lMax + BaseMm + 3, reach),
        });
        foreach (var x in tooShort.Concat(tooLong))
        {
            int k = tines.IndexOf(x);
            m.Mark(k * Mech.StripSamples / n, (k + 1) * Mech.StripSamples / n, Severity.Warn);
        }

        m.Summary = $"A {n}-tine comb in {t:0.###} mm {metal.Name.Split(" (")[0].Split(',')[0].ToLowerInvariant()}: tines {w:0.##} mm wide, {lMin:0.0} to {lMax:0.0} mm long, {Notes.Name(lo.Pitch)} ({lo.Hz:0} Hz) to {Notes.Name(hi.Pitch)} ({hi.Hz:0} Hz).";
        return m;
    }
}
