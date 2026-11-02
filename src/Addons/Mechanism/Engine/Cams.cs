using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>What the follower does during one step of the cam's turn.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CamMove>))]
public enum CamMove
{
    /// <summary>Goes up.</summary>
    Rise,
    /// <summary>Stays where it is.</summary>
    Wait,
    /// <summary>Comes down.</summary>
    Fall,
    /// <summary>Falls straight off a step in the cam (a snail cam): a sudden drop, which only works turning one way.</summary>
    Drop,
}

/// <summary>How a rise or fall speeds up and slows down.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CamLaw>))]
public enum CamLaw
{
    /// <summary>Cycloidal: starts and stops without a jolt. The best choice for most moves.</summary>
    Smooth,
    /// <summary>Simple harmonic: the gentlest push, with a small jolt at each end unless the next move carries on.</summary>
    Harmonic,
    /// <summary>3-4-5 polynomial: like smooth, a little quicker in the middle.</summary>
    Polynomial,
    /// <summary>Constant speed: simple, but it knocks at each end.</summary>
    Steady,
}

/// <summary>The end of the follower that rides on the cam.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FollowerFoot>))]
public enum FollowerFoot
{
    /// <summary>A round end: follows dips and sudden drops.</summary>
    Round,
    /// <summary>A flat foot: pushes straight up and wears well, but can't get into dips.</summary>
    Flat,
}

/// <summary>One step of a cam's motion: rise or fall so many mm over so many degrees of its turn, wait, or drop.</summary>
public sealed record CamStep(CamMove Move, double Degrees, double Mm, CamLaw Law = CamLaw.Smooth);

/// <summary>Motions to start from.</summary>
public static class CamPresets
{
    public sealed record Preset(string Name, ImmutableList<CamStep> Steps);

    public static readonly ImmutableArray<Preset> All =
    [
        new("Up and down", [new(CamMove.Rise, 180, 12, CamLaw.Harmonic), new(CamMove.Fall, 180, 12, CamLaw.Harmonic)]),
        new("Up, wait, down, wait", [new(CamMove.Rise, 120, 12), new(CamMove.Wait, 60, 0), new(CamMove.Fall, 120, 12), new(CamMove.Wait, 60, 0)]),
        new("Two bumps", [new(CamMove.Rise, 90, 8, CamLaw.Harmonic), new(CamMove.Fall, 90, 8, CamLaw.Harmonic), new(CamMove.Rise, 90, 8, CamLaw.Harmonic), new(CamMove.Fall, 90, 8, CamLaw.Harmonic)]),
        new("Three bumps", [new(CamMove.Rise, 60, 6, CamLaw.Harmonic), new(CamMove.Fall, 60, 6, CamLaw.Harmonic), new(CamMove.Rise, 60, 6, CamLaw.Harmonic), new(CamMove.Fall, 60, 6, CamLaw.Harmonic), new(CamMove.Rise, 60, 6, CamLaw.Harmonic), new(CamMove.Fall, 60, 6, CamLaw.Harmonic)]),
        new("Quick up, slow down", [new(CamMove.Rise, 70, 12), new(CamMove.Wait, 40, 0), new(CamMove.Fall, 190, 12), new(CamMove.Wait, 60, 0)]),
        new("Wait, then hop", [new(CamMove.Wait, 200, 0), new(CamMove.Rise, 60, 10), new(CamMove.Fall, 60, 10), new(CamMove.Wait, 40, 0)]),
        new("Slow rise, sudden drop", [new(CamMove.Rise, 300, 12), new(CamMove.Wait, 30, 0), new(CamMove.Drop, 0, 12), new(CamMove.Wait, 30, 0)]),
    ];

    public static ImmutableList<CamStep> Default => All[1].Steps;

    /// <summary>The preset these steps are, or null when they've been changed.</summary>
    public static Preset? Find(IReadOnlyList<CamStep> steps) => All.FirstOrDefault(p => p.Steps.SequenceEqual(steps));
}

/// <summary>A cam's steps laid out round one turn: the follower's height, and how fast it changes, at any angle.</summary>
public sealed class CamProgram
{
    /// <summary>One step, placed: from which angle (radians), for how long, from what height, changing by how much.</summary>
    public sealed record Segment(CamMove Move, double From, double Span, double H0, double Dh, CamLaw Law)
    {
        public double To => From + Span;
        /// <summary>A change of height all at once (a drop, or a jump up that can't be done).</summary>
        public bool Sudden => Span <= 0;
    }

    public IReadOnlyList<Segment> Segments { get; }
    /// <summary>What the steps' degrees added up to (they're stretched or squeezed to fill one turn).</summary>
    public double TotalDegrees { get; }
    /// <summary>How much higher the follower ends the turn than it started: above 0 it drops back at the end; below 0 it would have to jump up.</summary>
    public double EndJump { get; }
    /// <summary>The highest it goes, above its lowest (mm).</summary>
    public double Lift { get; }

    public CamProgram(IEnumerable<CamStep> steps)
    {
        var list = steps.Where(s => s.Move == CamMove.Drop ? s.Mm > 0 : s.Degrees > 0).ToList();
        double total = list.Where(s => s.Move != CamMove.Drop).Sum(s => s.Degrees);
        if (!(total > 0)) throw new InvalidOperationException("Give the cam's steps some of the turn (degrees).");
        TotalDegrees = total;
        double k = 2 * Math.PI / total;
        var segs = new List<Segment>();
        double at = 0, h = 0;
        foreach (var s in list)
        {
            double mm = Math.Max(0, s.Mm);
            double span = s.Move == CamMove.Drop ? 0 : s.Degrees * k;
            double dh = s.Move switch { CamMove.Rise => mm, CamMove.Fall or CamMove.Drop => -mm, _ => 0 };
            segs.Add(new(s.Move, at, span, h, dh, s.Law));
            at += span;
            h += dh;
        }
        EndJump = h;
        if (Math.Abs(h) > 1e-9) segs.Add(new(CamMove.Drop, 2 * Math.PI, 0, h, -h, CamLaw.Smooth));
        double lo = segs.Min(s => Math.Min(s.H0, s.H0 + s.Dh));
        Segments = [.. segs.Select(s => s with { H0 = s.H0 - lo })];
        Lift = Segments.Max(s => Math.Max(s.H0, s.H0 + s.Dh));
    }

    /// <summary>The height (mm), its rate (mm per radian) and how that changes (mm per radian²) at an angle of the turn.</summary>
    public (double S, double V, double A) At(double phi)
    {
        phi -= 2 * Math.PI * Math.Floor(phi / (2 * Math.PI));
        Segment? last = null;
        foreach (var s in Segments)
        {
            if (s.Sudden) continue;
            last = s;
            if (phi < s.To) return Eval(s, phi);
        }
        return Eval(last!, phi);
    }

    /// <summary>The step the turn is in at an angle (never a sudden one).</summary>
    public Segment SegmentAt(double phi)
    {
        phi -= 2 * Math.PI * Math.Floor(phi / (2 * Math.PI));
        Segment? last = null;
        foreach (var s in Segments)
        {
            if (s.Sudden) continue;
            last = s;
            if (phi < s.To) return s;
        }
        return last!;
    }

    static (double S, double V, double A) Eval(Segment s, double phi)
    {
        if (s.Dh == 0) return (s.H0, 0, 0);
        double t = Math.Clamp((phi - s.From) / s.Span, 0, 1);
        var (y, v, a) = Law(s.Law, t);
        return (s.H0 + s.Dh * y, s.Dh * v / s.Span, s.Dh * a / (s.Span * s.Span));
    }

    /// <summary>A law's height (0 to 1) and its first and second rates, at <paramref name="t"/> (0 to 1) of the way through the move.</summary>
    public static (double Y, double V, double A) Law(CamLaw law, double t)
    {
        const double Tau = 2 * Math.PI;
        return law switch
        {
            CamLaw.Smooth => (t - Math.Sin(Tau * t) / Tau, 1 - Math.Cos(Tau * t), Tau * Math.Sin(Tau * t)),
            CamLaw.Harmonic => ((1 - Math.Cos(Math.PI * t)) / 2, Math.PI / 2 * Math.Sin(Math.PI * t), Math.PI * Math.PI / 2 * Math.Cos(Math.PI * t)),
            CamLaw.Polynomial => (t * t * t * (10 - 15 * t + 6 * t * t), 30 * t * t * (1 - 2 * t + t * t), 60 * t * (1 - 3 * t + 2 * t * t)),
            _ => (t, 1, 0),
        };
    }

    /// <summary>Angles round the turn with the height at each, close enough to draw the cam from: every step's ends included, and both heights at a sudden change.</summary>
    public List<(double Phi, double S)> Trace(double stepRad)
    {
        var o = new List<(double, double)>();
        foreach (var s in Segments)
        {
            if (s.Sudden)
            {
                o.Add((s.From, s.H0));
                o.Add((s.From, s.H0 + s.Dh));
                continue;
            }
            int n = Math.Max(2, (int)Math.Ceiling(s.Span / stepRad));
            for (int i = 0; i <= n; i++)
            {
                double phi = s.From + s.Span * i / n;
                o.Add((phi, Eval(s, phi).S));
            }
        }
        return o;
    }
}

/// <summary>
/// A cam cut for its steps and follower, and what the follower really does riding on the cut shape: a round
/// foot can't get into a dip narrower than itself or reach the top of a bump sharper than itself, and a flat
/// foot can't get into any dip, so the check runs the follower on the shape, not on the plan.
/// </summary>
public sealed class CamDesign
{
    /// <summary>How many points round the turn the follower is checked at.</summary>
    public const int Samples = 720;
    /// <summary>How thick a flat foot is, up from its face (mm).</summary>
    public const double FlatFootH = 5;
    /// <summary>How far the follower may be off the plan before it counts (mm).</summary>
    public const double Tolerance = 0.15;

    public CamProgram Program { get; }
    public FollowerFoot Foot { get; }
    /// <summary>The smallest radius the follower rides on: the cam's radius where the lift is 0 (mm).</summary>
    public double BaseR { get; }
    /// <summary>The round foot's radius, or half the flat foot's width (mm).</summary>
    public double FootR { get; }
    /// <summary>+1 when the cam turns anticlockwise as drawn, −1 clockwise.</summary>
    public int Dir { get; }
    /// <summary>The cam's edge, in its own axes (no holes).</summary>
    public Paths64 Outline { get; }
    /// <summary>Where the follower really sits (a round foot's centre, or a flat foot's face) above the shaft, at each of <see cref="Samples"/> points of the cam's turn.</summary>
    public double[] Height { get; }
    /// <summary>The cam's highest and lowest points above the shaft at each sample (for drawing it edge-on).</summary>
    public double[] Top { get; }
    public double[] Bottom { get; }
    public double MaxRadius { get; }
    public double MinRadius { get; }
    /// <summary>Samples where the check found trouble (filled in by <see cref="Check"/>).</summary>
    public bool[] Trouble { get; } = new bool[Samples];

    /// <summary>Where the follower sits when its lift is 0.</summary>
    public double Rest => Foot == FollowerFoot.Round ? BaseR + FootR : BaseR;
    /// <summary>How far the foot reaches up from where it sits (the top of a round foot, or of a flat foot's plate).</summary>
    public double FootTop => Foot == FollowerFoot.Round ? FootR : FlatFootH;
    /// <summary>How far the foot reaches down from where it sits.</summary>
    public double FootBottom => Foot == FollowerFoot.Round ? FootR : 0;

    public CamDesign(IEnumerable<CamStep> steps, double baseR, FollowerFoot foot, double footMm, int dir, CancellationToken ct = default)
    {
        Program = new CamProgram(steps);
        if (!(baseR >= 5)) throw new InvalidOperationException("Give the cam a base radius of at least 5 mm.");
        Foot = foot;
        BaseR = baseR;
        Dir = dir >= 0 ? 1 : -1;
        FootR = Math.Max(foot == FollowerFoot.Round ? 2 : 3, footMm / 2);
        var trace = Program.Trace(0.25 * Math.PI / 180);
        ct.ThrowIfCancellationRequested();
        Outline = foot == FollowerFoot.Round ? RoundOutline(trace) : FlatOutline(trace);
        ct.ThrowIfCancellationRequested();

        // Run the follower on the cut shape: at each angle, how low can it sit without going into the cam?
        var pts = Densify(Outline, 0.2);
        MaxRadius = pts.Max(v => v.Length);
        MinRadius = pts.Min(v => v.Length);
        Height = new double[Samples];
        Top = new double[Samples];
        Bottom = new double[Samples];
        double r = FootR, r2 = r * r;
        for (int k = 0; k < Samples; k++)
        {
            double al = Math.PI / 2 - Dir * (2 * Math.PI * k / Samples);
            double ux = Math.Cos(al), uy = Math.Sin(al); // up the follower, in the cam's axes
            double wx = -uy, wy = ux;                     // across it
            double best = double.NegativeInfinity, hi = double.NegativeInfinity, lo = double.PositiveInfinity;
            foreach (var v in pts)
            {
                double along = v.X * ux + v.Y * uy;
                if (along > hi) hi = along;
                if (along < lo) lo = along;
                double across = v.X * wx + v.Y * wy;
                if (Math.Abs(across) >= r) continue;
                double h = foot == FollowerFoot.Round ? along + Math.Sqrt(r2 - across * across) : along;
                if (h > best) best = h;
            }
            Height[k] = double.IsFinite(best) ? best : Rest;
            Top[k] = hi;
            Bottom[k] = lo;
        }
    }

    /// <summary>A round foot's centre runs round the pitch curve; the cam is that curve brought in by the foot's radius.</summary>
    Paths64 RoundOutline(List<(double Phi, double S)> trace)
    {
        var pitch = Poly.Path(trace.Select(p => Shapes.Polar(Rest + p.S, Math.PI / 2 - Dir * p.Phi)));
        if (!Clipper.IsPositive(pitch)) pitch.Reverse();
        var cam = Poly.Grow(Poly.Union([pitch]), -FootR);
        var piece = cam.Where(p => Clipper.IsPositive(p) && Clipper.PointInPolygon(new Point64(0, 0), p) == PointInPolygonResult.IsInside)
            .OrderByDescending(p => Math.Abs(Clipper.Area(p))).FirstOrDefault();
        if (piece is null) throw new InvalidOperationException("The foot is too big for this cam: make the foot smaller or the base radius bigger.");
        return [piece];
    }

    /// <summary>
    /// A flat foot's face is a line at each angle; the cam is everything inside all of them (always a convex
    /// shape): found as the dual of the convex hull of the lines' poles.
    /// </summary>
    Paths64 FlatOutline(List<(double Phi, double S)> trace)
    {
        var dual = trace.Select(p => Shapes.Polar(1 / (BaseR + p.S), Math.PI / 2 - Dir * p.Phi)).ToList();
        var hull = Hull(dual);
        var pts = new List<Vec2>(hull.Count);
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            double det = a.X * b.Y - a.Y * b.X;
            if (Math.Abs(det) < 1e-15) continue;
            pts.Add(new((b.Y - a.Y) / det, (a.X - b.X) / det));
        }
        var path = Poly.Path(pts);
        if (!Clipper.IsPositive(path)) path.Reverse();
        return Poly.Union([path]);
    }

    /// <summary>The convex hull, anticlockwise (Andrew's monotone chain).</summary>
    static List<Vec2> Hull(List<Vec2> points)
    {
        var p = points.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        if (p.Count < 3) return p;
        var h = new Vec2[2 * p.Count];
        int k = 0;
        for (int i = 0; i < p.Count; i++)
        {
            while (k >= 2 && (h[k - 1] - h[k - 2]).Cross(p[i] - h[k - 2]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Count - 2, lo = k + 1; i >= 0; i--)
        {
            while (k >= lo && (h[k - 1] - h[k - 2]).Cross(p[i] - h[k - 2]) <= 0) k--;
            h[k++] = p[i];
        }
        return [.. h.Take(k - 1)];
    }

    static List<Vec2> Densify(Paths64 paths, double step)
    {
        var o = new List<Vec2>();
        foreach (var path in paths)
            for (int i = 0; i < path.Count; i++)
            {
                var a = Poly.V(path[i]);
                var b = Poly.V(path[(i + 1) % path.Count]);
                int n = Math.Max(1, (int)Math.Ceiling(a.Dist(b) / step));
                for (int j = 0; j < n; j++) o.Add(Vec2.Lerp(a, b, (double)j / n));
            }
        return o;
    }

    static double Lerp(double[] a, double phi)
    {
        double x = phi / (2 * Math.PI) * a.Length;
        x -= a.Length * Math.Floor(x / a.Length);
        int i = Math.Min(a.Length - 1, (int)x);
        double t = x - i;
        return a[i] * (1 - t) + a[(i + 1) % a.Length] * t;
    }

    /// <summary>Sample index nearest an angle of the cam's turn.</summary>
    public static int Index(double phi)
    {
        int k = (int)Math.Round(phi / (2 * Math.PI) * Samples) % Samples;
        return k < 0 ? k + Samples : k;
    }

    /// <summary>Where the follower really sits above the shaft at an angle of the cam's turn.</summary>
    public double HeightAt(double phi) => Lerp(Height, phi);
    /// <summary>How high the follower really is (above its lowest planned place) at an angle of the cam's turn.</summary>
    public double LiftAt(double phi) => Lerp(Height, phi) - Rest;
    public double TopAt(double phi) => Lerp(Top, phi);
    public double BottomAt(double phi) => Lerp(Bottom, phi);
    /// <summary>The cam turned to an angle of its turn (radians, as drawn).</summary>
    public double TurnAt(double phi) => Dir * phi;

    /// <summary>
    /// A follower for this cam: its foot (origin at a round foot's centre, or the middle of a flat foot's face)
    /// and a bar <paramref name="barW"/> wide straight up from it to <paramref name="topY"/>.
    /// </summary>
    public Paths64 FollowerShape(double barW, double topY)
    {
        var parts = new Paths64();
        if (Foot == FollowerFoot.Round)
        {
            parts.Add(Poly.Circle(default, FootR, 0.003));
            double neck = Math.Min(barW, 1.6 * FootR);
            parts.Add(Poly.Rect(-neck / 2, 0, neck / 2, FootR + 3));
            parts.Add(Poly.Rect(-barW / 2, FootR + 2, barW / 2, topY));
        }
        else
        {
            parts.Add(Shapes.RoundRect(-FootR, 0, FootR, FlatFootH, 1));
            parts.Add(Poly.Rect(-barW / 2, FlatFootH - 0.5, barW / 2, topY));
        }
        return Poly.Union(parts);
    }

    /// <summary>
    /// Adds what the check found about this cam to the mechanism, marks the strip, fills in <see cref="Trouble"/>,
    /// and returns how fast it can turn before the follower flies off it (turns a minute).
    /// </summary>
    /// <param name="who">How to name it at the start of a sentence ("The cam", "Cam 2").</param>
    /// <param name="start">How far into its steps the cam is when the input is at 0 (radians).</param>
    /// <param name="holeR">The radius of the hole for its shaft.</param>
    public double Check(Mechanism mech, string who, double start, double holeR, Paths64? cut = null, Paths64? follower = null)
    {
        var p = Program;
        var plan = new (double S, double V, double A)[Samples];
        var dev = new double[Samples];
        for (int k = 0; k < Samples; k++)
        {
            plan[k] = p.At(2 * Math.PI * k / Samples);
            dev[k] = Height[k] - (Rest + plan[k].S);
        }
        int K(double input) => Index(input + start);
        double InputDeg(int k) { double d = (k * 360.0 / Samples - start * 180 / Math.PI) % 360; return d < 0 ? d + 360 : d; }

        if (Math.Abs(p.TotalDegrees - 360) > 0.5)
            mech.Findings.Add(new(Severity.Note, $"{who}: the steps add up to {p.TotalDegrees:0.#}°, so they've been {(p.TotalDegrees > 360 ? "squeezed" : "stretched")} to fill one turn."));
        if (p.EndJump > 1e-6)
            mech.Findings.Add(new(Severity.Note, $"{who}: the follower ends the turn {p.EndJump:0.#} mm higher than it started, so it drops back at the end."));

        // Sudden changes: a drop takes a few degrees (the foot rolls off the edge); a jump up jams.
        var skip = new bool[Samples];
        var jams = new bool[Samples];
        foreach (var s in p.Segments.Where(s => s.Sudden))
        {
            int k0 = Index(s.From);
            int back = 0, fwd = 0;
            while (back < 120 && Math.Abs(dev[(k0 - back - 1 + Samples) % Samples]) > Tolerance) back++;
            while (fwd < 240 && Math.Abs(dev[(k0 + fwd) % Samples]) > Tolerance) fwd++;
            for (int i = -back; i < fwd; i++) skip[(k0 + i + Samples) % Samples] = true;
            double deg = (back + fwd) * 360.0 / Samples;
            if (s.Dh > 0)
            {
                mech.Findings.Add(new(Severity.Bad, $"{who}: the steps go down {s.Dh:0.#} mm more than they go up, so the follower would have to jump up at the end of the turn, and the cam would jam against it. Make the rises and falls add up."));
                for (int i = -Math.Max(2, back); i < Math.Max(3, fwd); i++) jams[(k0 + i + Samples) % Samples] = Trouble[(k0 + i + Samples) % Samples] = true;
            }
            else if (Foot == FollowerFoot.Flat && deg > 25)
                mech.Findings.Add(new(Severity.Warn, $"{who}: a flat foot can't fall off a step, so instead of dropping it slides down {-s.Dh:0.#} mm over about {deg:0}° of the turn. A round foot drops quicker."));
            else
                mech.Findings.Add(new(Severity.Note, $"{who}: the follower drops {-s.Dh:0.#} mm in about {Math.Max(1, deg):0}° of the turn. Turn it only the way the arrow shows: backwards, the step in the cam catches the follower."));
        }
        mech.MarkWhere(a => skip[K(a)], Severity.Note);
        mech.MarkWhere(a => jams[K(a)], Severity.Bad);

        // Anywhere else the follower is off the plan, the cut shape can't make the motion.
        bool Off(int i) => !skip[i % Samples] && Math.Abs(dev[i % Samples]) > Tolerance;
        var runs = new List<(int From, int Len, double Worst)>();
        int calm = Enumerable.Range(0, Samples).FirstOrDefault(i => !Off(i), -1);
        for (int j = 1; j <= Samples; j++)
        {
            // Each run of samples off the plan, found from its first sample (going round from one that's on it).
            int i = calm < 0 ? 0 : (calm + j) % Samples;
            if (calm >= 0 && (!Off(i) || Off(i + Samples - 1))) continue;
            int len = 0;
            double worst = 0;
            while (len < Samples && Off(i + len)) { double v = dev[(i + len) % Samples]; if (Math.Abs(v) > Math.Abs(worst)) worst = v; len++; }
            runs.Add((i, len, worst));
            if (calm < 0) break;
        }
        foreach (var (from, len, worst) in runs.OrderByDescending(r => Math.Abs(r.Worst)).Take(3))
        {
            var level = Math.Abs(worst) > 0.5 ? Severity.Bad : Severity.Warn;
            string where = len >= Samples - 1 ? "all the way round" : $"from {InputDeg(from):0}° to {InputDeg(from + len):0}° of the turn";
            string why = worst > 0
                ? Foot == FollowerFoot.Round
                    ? "it can't get down into a dip narrower than its foot. Use a smaller foot, or spread the moves over more of the turn"
                    : "a flat foot can't get down into a dip. Use a round foot, or gentler moves"
                : "it can't reach the top of a bump that's sharper than its foot. Use a smaller foot or a bigger base radius, or spread the move over more of the turn";
            mech.Findings.Add(new(level, $"{who}: the cut shape can't make the motion {where}: the follower is up to {Math.Abs(worst):0.##} mm {(worst > 0 ? "too high" : "too low")}, because {why}."));
            for (int i = 0; i < len; i++) Trouble[(from + i) % Samples] |= level == Severity.Bad;
            int f0 = from, l0 = len;
            mech.MarkWhere(a => { int k = K(a); int off = (k - f0 + Samples) % Samples; return off < l0; }, level);
        }
        if (runs.Count == 0)
            mech.Findings.Add(new(Severity.Good, $"{who}: riding on the cut shape, the follower does what the steps say, to within {Tolerance} mm all the way round."));

        // How hard the cam pushes sideways: a round foot is pushed along the line through its centre and the
        // point where it touches; past about 30° from straight up it starts to stick in its guide.
        if (Foot == FollowerFoot.Round)
        {
            double worst = 0;
            var steep = new bool[Samples];
            for (int k = 0; k < Samples; k++)
            {
                if (skip[k] || plan[k].V <= 1e-9) continue;
                double beta = Math.Round(Math.Atan(plan[k].V / (Rest + plan[k].S)) * 180 / Math.PI);
                worst = Math.Max(worst, beta);
                steep[k] = beta > 30;
            }
            if (worst > 45) mech.Findings.Add(new(Severity.Bad, $"{who} pushes the follower {worst:0}° off straight up on its steepest rise: it will jam. Make the base radius bigger, or spread the rise over more of the turn."));
            else if (worst > 30) mech.Findings.Add(new(Severity.Warn, $"{who} pushes the follower {worst:0}° off straight up on its steepest rise (under 30° runs smoothly); it may stick. A bigger base radius or a longer rise eases it."));
            else if (worst > 0) mech.Findings.Add(new(Severity.Good, $"{who} pushes at most {worst:0}° off straight up (under 30° runs smoothly)."));
            if (worst > 30) mech.MarkWhere(a => steep[K(a)], worst > 45 ? Severity.Bad : Severity.Warn);
            if (worst > 45) for (int k = 0; k < Samples; k++) Trouble[k] |= steep[k];
        }
        else
        {
            // A flat foot is touched off its middle by as much as the lift is changing (mm per radian).
            double reach = 0;
            for (int k = 0; k < Samples; k++) if (!skip[k]) reach = Math.Max(reach, Math.Abs(plan[k].V));
            if (reach > FootR - 1.5)
            {
                mech.Findings.Add(new(Severity.Bad, $"{who} touches the foot up to {reach:0.#} mm from its middle, past its edge: make the foot at least {Math.Ceiling(2 * (reach + 2)):0} mm wide."));
                var past = new bool[Samples];
                for (int k = 0; k < Samples; k++) past[k] = !skip[k] && Math.Abs(plan[k].V) > FootR - 1.5;
                mech.MarkWhere(a => past[K(a)], Severity.Bad);
                for (int k = 0; k < Samples; k++) Trouble[k] |= past[k];
            }
            else mech.Findings.Add(new(Severity.Good, $"{who} touches the flat foot within {reach:0.#} mm of its middle; the foot is {2 * FootR:0.#} mm wide."));
        }

        // The cam hitting the follower anywhere but its foot.
        if (cut is not null && follower is not null)
        {
            var hit = new bool[72];
            for (int j = 0; j < hit.Length; j++)
            {
                int k = j * Samples / hit.Length;
                double phi = 2 * Math.PI * k / Samples;
                var at = new Vec2(0, Height[k]);
                double reach = Math.Max(FootR, 6) + 3;
                var near = new Rect2(-reach, Height[k] - FootBottom - 2, reach, Height[k] + FootTop + 15);
                var c = Poly.Clip(Poly.Move(cut, TurnAt(phi)), near);
                var f = Poly.Clip(Poly.Move(follower, new Pose(at + new Vec2(0, 0.01), 0)), near);
                hit[j] = Poly.Overlap(c, f) > 0.05;
            }
            if (hit.Any(h => h))
            {
                mech.Findings.Add(new(Severity.Bad, $"{who} runs into the side of its follower. Use a bigger foot or gentler moves."));
                mech.MarkWhere(a => hit[K(a) * hit.Length / Samples], Severity.Bad);
                for (int k = 0; k < Samples; k++) Trouble[k] |= hit[k * hit.Length / Samples];
            }
        }

        if (p.Segments.Any(s => !s.Sudden && s.Dh != 0 && s.Law == CamLaw.Steady))
            mech.Findings.Add(new(Severity.Note, $"{who}: steady-speed moves knock at each end; Smooth runs quieter."));
        if (MinRadius - holeR < 3)
            mech.Findings.Add(new(Severity.Bad, $"{who} has almost no wood round its shaft hole: make its base radius bigger."));

        // Gravity holds the follower on: it leaves the cam wherever the cam pulls away faster than it can fall.
        double pull = 0;
        for (int k = 0; k < Samples; k++)
        {
            if (skip[k]) continue;
            var seg = p.SegmentAt(2 * Math.PI * k / Samples);
            if (seg.Law == CamLaw.Steady) continue;
            pull = Math.Max(pull, -plan[k].A);
        }
        return pull <= 1e-9 ? double.PositiveInfinity : Math.Sqrt(9810 / pull) * 60 / (2 * Math.PI);
    }

    /// <summary>The finding for how fast the followers can be turned before they fly off their cams.</summary>
    public static Finding SpeedFinding(double rpm, bool several)
    {
        string them = several ? "The followers stay on their cams" : "The follower stays on the cam";
        if (double.IsPositiveInfinity(rpm)) return new(Severity.Good, $"{them} at any speed you can turn it by hand.");
        if (rpm < 60) return new(Severity.Warn, $"{them} only up to about {rpm:0} turns a minute; any faster and {(several ? "they fly off and bounce" : "it flies off and bounces")}. Turn it gently, or spread the moves out.");
        return new(Severity.Good, $"{them} up to about {Math.Floor(rpm / 10) * 10:0} turns a minute (by hand you turn about 60).");
    }
}

/// <summary>
/// A cam and follower on a backboard: turning the cam pushes the follower up and lets it down again, step by
/// step. The follower slides on two pins in the backboard through a slot.
/// </summary>
public sealed record CamSpec : MechSpec
{
    public ImmutableList<CamStep> Steps { get; init; } = CamPresets.Default;
    /// <summary>The cam's radius where the follower is lowest (mm).</summary>
    public double BaseRadiusMm { get; init; } = 25;
    public FollowerFoot Foot { get; init; } = FollowerFoot.Round;
    /// <summary>The foot's size: a round foot's diameter, or a flat foot's width (mm).</summary>
    public double FootMm { get; init; } = 12;
    /// <summary>Turns clockwise (looking at the front of the cam) instead of anticlockwise.</summary>
    public bool Clockwise { get; init; }
    public Axle Axle { get; init; } = new();
    /// <summary>A hole near the middle of the cam for a peg to turn it by.</summary>
    public bool Handle { get; init; } = true;
    /// <summary>Where the handle hole goes and what peg it takes.</summary>
    public Crank Crank { get; init; } = new();
    /// <summary>The cam's hole for its shaft; null = one that grips the shaft (which turns in the backboard).</summary>
    public Hole? CamHole { get; init; }

    public override Mechanism Build(CancellationToken ct = default)
    {
        int dir = Clockwise ? -1 : 1;
        var d = new CamDesign(Steps, BaseRadiusMm, Foot, FootMm, dir, ct);
        double t = ThicknessMm;
        var mech = new Mechanism { Title = "Cam and follower" };
        string mat = Material;
        var bore = new Bore(CamHole, Axle, false, mat);
        double holeR = bore.R;
        var peg = Crank.PegHole(Axle);
        double pegR = peg.OuterR(mat);

        // ---- the cam: its edge, a hole that grips the shaft, a peg hole to turn it by, an arrow for the way it turns ----
        double room = d.MinRadius - holeR;
        var holes = new Paths64 { bore.Cut(default) };
        Vec2? handle = null;
        if (Handle)
        {
            var (at, why) = Crank.Place(holeR, d.MinRadius - 3, pegR, "The cam");
            if (why is not null && Crank.RadiusMm > 0) mech.Findings.Add(new(Severity.Warn, why));
            handle = at;
            if (at is { } a) holes.Add(peg.Cut(a, mat));
        }
        if (bore.Turns)
            mech.Findings.Add(new(Severity.Bad, "The cam's hole lets it turn on its shaft, so turning the shaft won't turn it. Use a press or snug fit, or a D or square hole."));
        var cam = Poly.Subtract(d.Outline, holes);
        double arrowR = holeR + room * 0.5;
        var arrow = room > 6 ? Shapes.TurnArrow(default, arrowR, Math.PI / 2 - dir * 0.6, Math.PI / 2 + dir * 0.6) : [];
        mech.Parts.Add(new Part
        {
            Name = "Cam",
            Shape = cam,
            Marks = arrow,
            Label = room > 7 ? new PartLabel("Cam", new Vec2(0, -(holeR + room * 0.5)), Math.Clamp(room * 0.25, 2.5, 4.5)) : null,
        });

        // ---- the follower: its foot, a bar up from it with a slot that rides on two pins ----
        double pinR = Axle.SizeMm / 2;
        double slotW = (Axle.Square ? Axle.SizeMm : Axle.SizeMm) + 0.3;
        double barW = Math.Max(d.Foot == FollowerFoot.Round ? Math.Clamp(1.6 * d.FootR, 6, 10) : 10, slotW + 5);
        double hMin = d.Height.Min(), hMax = d.Height.Max();
        double spacing = Math.Max(20, 3 * Axle.SizeMm);
        double pin1 = Math.Max(d.MaxRadius + pinR + 2, hMax + d.FootTop + 3.2 + slotW / 2);
        double pin2 = pin1 + spacing;
        double slot0 = pin1 - hMax - slotW / 2 - 0.2, slot1 = pin2 - hMin + slotW / 2 + 0.2;
        double barTop = slot1 + 5;
        var followerSolid = d.FollowerShape(barW, barTop);
        var follower = Poly.Subtract(followerSolid, [Shapes.RoundRect(-slotW / 2, slot0, slotW / 2, slot1, slotW / 2)]);
        mech.Parts.Add(new Part { Name = "Follower", Shape = follower });

        // ---- the backboard: holds the shaft (it turns in it) and the two pins ----
        double halfW = Math.Max(d.MaxRadius, d.FootR + 2) + 6;
        var box = new Rect2(-halfW, -d.MaxRadius - 6, halfW, hMax + barTop + 4);
        var corners = new[] { new Vec2(box.MinX + 5, box.MinY + 5), new Vec2(box.MaxX - 5, box.MinY + 5), new Vec2(box.MaxX - 5, box.MaxY - 5), new Vec2(box.MinX + 5, box.MaxY - 5) };
        var boardHoles = new Paths64 { bore.Frame(default, true), Axle.HeldHole(new Vec2(0, pin1)), Axle.HeldHole(new Vec2(0, pin2)) };
        foreach (var c in corners)
            if (c.Length > d.MaxRadius + 4 && (Math.Abs(c.X) > barW / 2 + 4)) boardHoles.Add(Poly.Circle(c, 1.75, 0.003));
        var board = Poly.Subtract([Shapes.RoundRect(box.MinX, box.MinY, box.MaxX, box.MaxY, 5)], boardHoles);
        mech.Parts.Add(new Part { Name = "Backboard", Shape = board, Label = new PartLabel("Backboard", new Vec2(0, box.MinY + 4), 3) });
        mech.Shopping.Add($"A shaft: {bore.ShaftWords} dowel or rod, about {Math.Ceiling(2 * t + 12):0} mm long ({(bore.Hole?.Fit == HoleFit.Press ? "pressed into the cam" : "glue it into the cam")}; it turns in the backboard)");
        mech.Shopping.Add($"Two pins for the follower's slot: {Axle.Words} dowel, about {Math.Ceiling(2 * t + 4):0} mm long, glued into the backboard");
        if (handle is not null) mech.Shopping.Add($"A handle peg: {peg.Shaft.Words} dowel, about 25 mm long");
        var drawn = new List<string>();
        if (bore.Chosen) drawn.Add($"the cam's shaft hole {bore.Words}");
        if (handle is not null && Crank.Hole is not null) drawn.Add($"its handle hole {peg.Words(mat)}");
        if (drawn.Count > 0) mech.Findings.Add(new(Severity.Note, $"Holes drawn for {MaterialWords.Of(mat)}: {string.Join("; ", drawn)}."));

        // ---- how it moves ----
        var bodies = new List<Body>
        {
            new("Backboard", board, Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Cam", cam, Tints.Of(0), a => new Pose(default, d.TurnAt(a))) { IsInput = true, Lines = arrow },
            new("Follower", follower, Tints.Of(1), a => new Pose(new Vec2(0, d.HeightAt(a)), 0)),
            new("Shaft", [bore.ShaftOutline()], Tints.Steel, a => new Pose(default, d.TurnAt(a))),
            new("Pin", [Poly.Circle(default, pinR, 0.01)], Tints.Steel, _ => new Pose(new Vec2(0, pin1), 0)),
            new("Pin", [Poly.Circle(default, pinR, 0.01)], Tints.Steel, _ => new Pose(new Vec2(0, pin2), 0)),
        };
        if (handle is { } h) bodies.Add(new Body("Handle", [peg.Shaft.Outline(default)], Tints.Steel, a => new Pose(new Pose(default, d.TurnAt(a)).Apply(h), d.TurnAt(a))));

        // ---- the motion check ----
        double rpm = d.Check(mech, "The cam", 0, holeR, cam, followerSolid);
        mech.Findings.Add(CamDesign.SpeedFinding(rpm, false));
        mech.Clashing = a => d.Trouble[CamDesign.Index(a)] ? ["Cam", "Follower"] : [];
        var preset = CamPresets.Find(Steps);
        mech.Summary = $"One turn of the cam lifts the follower {d.Program.Lift:0.#} mm{(preset is null ? "" : $" ({preset.Name.ToLowerInvariant()})")} and lets it down again.";

        mech.Panels.Add(new PlanarPanel
        {
            Title = "Looking at the front of the cam",
            Bodies = bodies,
            Extent = box.Inflate(2),
            InputPivot = default,
            InputSense = dir,
            Overlay = a =>
            {
                double planned = d.Rest + d.Program.At(a).S;
                double actual = d.HeightAt(a);
                return Math.Abs(planned - actual) > CamDesign.Tolerance
                    ? [([new Vec2(-d.FootR - 4, planned), new Vec2(d.FootR + 4, planned)], Tints.Problem)]
                    : [];
            },
        });
        mech.Panels.Add(LiftChart([("Planned", 0x8A8F98u, (Func<double, double>)(a => d.Program.At(a).S), true), ("On the cut cam", Tints.Of(1), d.LiftAt, false)]));
        return mech;
    }

    /// <summary>A chart of how high followers are over one turn.</summary>
    internal static ChartPanel LiftChart(IEnumerable<(string Name, uint Colour, Func<double, double> Lift, bool Dashed)> lines, string title = "How high the follower is") => new()
    {
        Title = title,
        Unit = "mm",
        Weight = 0.55,
        Series = [.. lines.Select(l => new Series(l.Name, l.Colour, [.. Enumerable.Range(0, 360).Select(i => l.Lift(2 * Math.PI * i / 360))]) { Dashed = l.Dashed })],
    };
}
