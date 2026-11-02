using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.BendHinge;

/// <summary>
/// How a sheet material copes with a living hinge. <see cref="TwistStrain"/> is the shear strain
/// the strips between slits take when they twist without cracking; <see cref="BendStrain"/> is how
/// far the solid sheet stretches on its outside face when it bends by itself. Both are rough,
/// cautious figures for common sheet; <see cref="BendHinge.Calibrate"/> makes one from a hinge you
/// have actually bent.
/// </summary>
public sealed record HingeMaterial(string Name, double TwistStrain, double BendStrain)
{
    public static readonly IReadOnlyList<HingeMaterial> Presets =
    [
        new("Birch plywood", 0.022, 0.006),
        new("Poplar plywood", 0.026, 0.007),
        new("MDF", 0.012, 0.003),
        new("Cast acrylic", 0.015, 0.008),
    ];
}

/// <summary>The settings for a bend-to-shape hinge (mm).</summary>
public sealed record BendHingeSettings
{
    /// <summary>The panel's size across the bend: the slits run this way.</summary>
    public double Height { get; init; } = 60;
    public double Thickness { get; init; } = 3;
    /// <summary>The length of each slit.</summary>
    public double CutLength { get; init; } = 15;
    /// <summary>The uncut material between slits in a line.</summary>
    public double Gap { get; init; } = 3;
    /// <summary>The width the laser takes out of each slit.</summary>
    public double Kerf { get; init; } = 0.15;
    /// <summary>The closest lines of slits are allowed to get.</summary>
    public double MinSpacing { get; init; } = 1;
    /// <summary>The widest lines of slits get before the sheet is left solid.</summary>
    public double MaxSpacing { get; init; } = 6;
    /// <summary>How much tighter than needed every part is made to bend (1 = exactly).</summary>
    public double Safety { get; init; } = 1.5;
    public HingeMaterial Material { get; init; } = HingeMaterial.Presets[0];

    /// <summary>Length of each strip that twists: from a bridge in one line to the next one along.</summary>
    public double TwistLength => Math.Max(0.1, (CutLength - Gap) / 2);

    /// <summary>What's wrong with these settings, or null if they make a hinge.</summary>
    public string? Problem()
    {
        if (!(Thickness > 0)) return "The material needs a thickness.";
        if (!(Height > 0)) return "The panel needs a height.";
        if (!(Gap >= 0.5)) return "Leave at least 0.5 mm between slits.";
        if (CutLength < 2 * Gap + 1) return "Make the slits at least twice the gap long (plus 1 mm), or nothing twists.";
        if (CutLength + Gap > Height) return "The slits and a gap don't fit in the panel's height.";
        if (!(Kerf >= 0)) return "The kerf can't be negative.";
        if (MinSpacing < Kerf + 0.3) return $"The closest lines must leave a strip at least 0.3 mm wide after the kerf ({Kerf + 0.3:0.##} mm or more).";
        if (MaxSpacing < MinSpacing) return "The widest spacing must be at least the closest.";
        if (!(Safety >= 1)) return "The safety factor must be 1 or more.";
        if (!(Material.TwistStrain > 0 && Material.BendStrain > 0)) return "The material's figures must be above 0.";
        return null;
    }
}

/// <summary>The curve the panel is to follow, sampled every <see cref="Step"/> mm along its length.</summary>
public sealed class BendProfile
{
    /// <summary>Points along the curve, <see cref="Step"/> apart, starting where the panel starts.</summary>
    public required IReadOnlyList<Vec2> Points { get; init; }
    /// <summary>How far the direction turns at each point (radians, never negative).</summary>
    public required double[] Turn { get; init; }
    /// <summary>Total turning up to and including each point.</summary>
    public required double[] Cumulative { get; init; }
    public required double Step { get; init; }
    public required bool Closed { get; init; }
    /// <summary>The curve's length: the flat panel's length.</summary>
    public double Length => Closed ? Points.Count * Step : (Points.Count - 1) * Step;

    /// <summary>How far the curve turns between two distances along it (radians).</summary>
    public double TurnBetween(double a, double b)
    {
        if (b <= a) return 0;
        return At(b) - At(a);
    }

    /// <summary>Total turning from the start to <paramref name="s"/> (linear between samples).</summary>
    double At(double s)
    {
        if (s <= 0) return 0;
        double i = s / Step;
        int n = Cumulative.Length;
        if (i >= n - 1) return Cumulative[n - 1];
        int k = (int)Math.Floor(i);
        double before = k == 0 ? 0 : Cumulative[k - 1];
        // The turn at sample k is spread over the step that follows it.
        return before + (Cumulative[k] - before) * Math.Clamp(i - k, 0, 1);
    }

    /// <summary>The point <paramref name="s"/> mm along the curve.</summary>
    public Vec2 PointAt(double s)
    {
        int n = Points.Count;
        if (n == 0) return default;
        double i = s / Step;
        if (Closed) i = ((i % n) + n) % n;
        else i = Math.Clamp(i, 0, n - 1);
        int k = (int)Math.Floor(i);
        int k1 = Closed ? (k + 1) % n : Math.Min(k + 1, n - 1);
        return Vec2.Lerp(Points[k], Points[k1], i - k);
    }

    /// <summary>The unit direction of travel <paramref name="s"/> mm along the curve.</summary>
    public Vec2 DirectionAt(double s)
    {
        var a = PointAt(Math.Max(Closed ? s - Step : 0, s - Step));
        var b = PointAt(Closed ? s + Step : Math.Min(Length, s + Step));
        var d = b - a;
        double l = d.Length;
        return l > 1e-12 ? d / l : new Vec2(1, 0);
    }
}

/// <summary>One stretch of the panel and how it's treated.</summary>
public enum BandKind
{
    /// <summary>Left solid: flat, or bends gently enough that the sheet takes it by itself.</summary>
    Solid,
    /// <summary>Slit, with lines as far apart as this part's bend allows.</summary>
    Hinge,
    /// <summary>Slit as closely as allowed, and still not enough for this part's bend.</summary>
    TooTight,
}

/// <summary>A stretch of the panel, from <see cref="From"/> to <see cref="To"/> mm along it.</summary>
public readonly record struct Band(double From, double To, BandKind Kind, double Radius);

/// <summary>A flat panel that bends into a curve, with its slits, bands and checks.</summary>
public sealed class BendHingeResult
{
    public required BendProfile Profile { get; init; }
    public required BendHingeSettings Settings { get; init; }
    /// <summary>Runs of lines of slits, each line's place in mm along the panel.</summary>
    public required IReadOnlyList<IReadOnlyList<double>> Runs { get; init; }
    /// <summary>Every line of slits, mm along the panel.</summary>
    public IEnumerable<double> Lines => Runs.SelectMany(r => r);
    /// <summary>The slits, as open lines (panel coordinates: x along, y across, top-left at 0, 0).</summary>
    public required PathGeometry Slits { get; init; }
    /// <summary>The panel's outline (a rectangle the curve's length by the height).</summary>
    public required PathGeometry Outline { get; init; }
    /// <summary>The panel in stretches: solid, slit, or slit and still too tight.</summary>
    public required IReadOnlyList<Band> Bands { get; init; }
    /// <summary>The tightest bend in the curve (radius, mm), measured over the closest spacing.</summary>
    public required double TightestRadius { get; init; }
    /// <summary>The tightest bend these slits can take, at the closest spacing (mm).</summary>
    public required double ReachableRadius { get; init; }
    /// <summary>A slit length that would reach the tightest bend, if one fits in the height (else null).</summary>
    public required double? SuggestedCutLength { get; init; }
    public double Length => Profile.Length;
    public bool AllReachable => Bands.All(b => b.Kind != BandKind.TooTight);
}

/// <summary>
/// "Bend to shape": a living hinge whose slits are only where the panel has to bend, and only as
/// close together as each part's bend needs. Straight parts stay solid and stiff; tight corners
/// get close lines of slits.
/// <para>
/// The panel bends by twisting the strips between lines of slits. A strip <c>w</c> wide (less
/// the kerf) in sheet <c>t</c> thick twists by an angle φ over its free length <c>L</c> (from a
/// bridge in one line to the next bridge along, (cut − gap) / 2) with a shear strain of about
/// φ·c / L, where c is the thinner of the strip's two sides. So each strip can turn the panel
/// through at most φ = γ·L / c, γ being the material's twist strain. Lines are placed one after
/// another, each as far from the last as the turning of the curve between them allows. Where
/// the curve is gentle enough for the solid sheet (bend strain ε, radius t / 2ε) there are none.
/// </para>
/// </summary>
public static class BendHinge
{
    /// <summary>Sample spacing along the curve (mm).</summary>
    public const double SampleStep = 0.1;

    /// <summary>
    /// The longest subpath of <paramref name="world"/> as a profile, sampled evenly. A closed
    /// curve is started in its flattest stretch, so the panel's two ends meet where it's solid.
    /// Null if there's no curve at least 5 mm long.
    /// </summary>
    public static BendProfile? Profile(PathGeometry world, double step = SampleStep, double smoothing = 2)
    {
        var flat = world.Flatten(0.001)
            .Select(f => (f.Points, f.Closed, Len: PathLength(f.Points, f.Closed)))
            .Where(f => f.Points.Count >= 2)
            .OrderByDescending(f => f.Len)
            .FirstOrDefault();
        if (flat.Points is null || flat.Len < 5) return null;
        var pts = Resample(flat.Points, flat.Closed, step, out double realStep);
        int n = pts.Count;
        var turn = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!flat.Closed && (i == 0 || i == n - 1)) continue;
            var a = pts[(i - 1 + n) % n];
            var b = pts[i];
            var c = pts[(i + 1) % n];
            var d0 = b - a; var d1 = c - b;
            if (d0.Length < 1e-12 || d1.Length < 1e-12) continue;
            turn[i] = Math.Abs(Math.Atan2(d0.Cross(d1), d0.Dot(d1)));
        }
        // Curves drawn as many short straight lines (flattened, or imported that way) turn in lumps
        // at their corners: spread each over a couple of millimetres, keeping the total.
        turn = Smooth(turn, flat.Closed, Math.Max(0, (int)Math.Round(smoothing / realStep / 2)));
        if (flat.Closed)
        {
            int start = FlattestStart(turn, realStep);
            pts = [.. pts.Skip(start).Concat(pts.Take(start))];
            turn = [.. turn.Skip(start).Concat(turn.Take(start))];
        }
        var cum = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++) { sum += turn[i]; cum[i] = sum; }
        return new BendProfile { Points = pts, Turn = turn, Cumulative = cum, Step = realStep, Closed = flat.Closed };
    }

    /// <summary>A running average over 2·<paramref name="half"/> + 1 samples (the ends of an open curve keep their share).</summary>
    static double[] Smooth(double[] v, bool closed, int half)
    {
        int n = v.Length;
        if (half <= 0 || n < 3) return v;
        var o = new double[n];
        int width = 2 * half + 1;
        for (int i = 0; i < n; i++)
        {
            if (v[i] == 0) continue;
            double share = v[i] / width;
            for (int k = -half; k <= half; k++)
            {
                int j = i + k;
                if (closed) j = ((j % n) + n) % n;
                else j = Math.Clamp(j, 0, n - 1);
                o[j] += share;
            }
        }
        return o;
    }

    static double PathLength(List<Vec2> p, bool closed)
    {
        double l = 0;
        for (int i = 1; i < p.Count; i++) l += p[i].Dist(p[i - 1]);
        if (closed && p.Count > 1) l += p[^1].Dist(p[0]);
        return l;
    }

    /// <summary>Points every <paramref name="step"/> mm along the line (adjusted so they come out even).</summary>
    static List<Vec2> Resample(List<Vec2> src, bool closed, double step, out double realStep)
    {
        var p = new List<Vec2>(src);
        if (closed && p.Count > 1 && p[^1].Dist(p[0]) < 1e-9) p.RemoveAt(p.Count - 1);
        if (closed) p.Add(p[0]);
        double total = PathLength(p, false);
        int count = Math.Max(2, (int)Math.Round(total / step));
        realStep = total / count;
        var o = new List<Vec2>(count + 1);
        int seg = 0;
        double segStart = 0;
        for (int k = 0; k <= count; k++)
        {
            double s = k * realStep;
            while (seg < p.Count - 2 && segStart + p[seg + 1].Dist(p[seg]) < s)
            {
                segStart += p[seg + 1].Dist(p[seg]);
                seg++;
            }
            double len = p[seg + 1].Dist(p[seg]);
            double t = len > 1e-12 ? Math.Clamp((s - segStart) / len, 0, 1) : 0;
            o.Add(Vec2.Lerp(p[seg], p[seg + 1], t));
        }
        // A closed curve's last point is its first again.
        if (closed) o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>The sample in the middle of the longest stretch with the least turning.</summary>
    static int FlattestStart(double[] turn, double step)
    {
        int n = turn.Length;
        // Turning within ±10 mm (or an eighth of the way round) of each sample.
        int half = Math.Max(1, Math.Min(n / 8, (int)Math.Round(10 / step)));
        var near = new double[n];
        double window = 0;
        for (int k = -half; k <= half; k++) window += turn[((k % n) + n) % n];
        for (int i = 0; i < n; i++)
        {
            near[i] = window;
            window -= turn[((i - half) % n + n) % n];
            window += turn[(i + half + 1) % n];
        }
        double best = near.Min();
        double tol = Math.Max(1e-9, best * 0.01 + 1e-6);
        // The longest run (going round) of samples as flat as the flattest, and its middle.
        int bestLen = 0, bestMid = Array.IndexOf(near, best);
        for (int i = 0; i < n; i++)
        {
            if (near[i] > best + tol) continue;
            if (near[(i - 1 + n) % n] <= best + tol && near.Any(v => v > best + tol)) continue;
            int len = 0;
            while (len < n && near[(i + len) % n] <= best + tol) len++;
            if (len > bestLen) { bestLen = len; bestMid = (i + len / 2) % n; }
        }
        return bestMid;
    }

    /// <summary>The thinner side of a strip between lines <paramref name="spacing"/> apart.</summary>
    static double StripSide(BendHingeSettings s, double spacing) => Math.Max(0.05, Math.Min(spacing - s.Kerf, s.Thickness));

    /// <summary>How far one strip between lines <paramref name="spacing"/> apart may turn the panel (radians).</summary>
    public static double StripTurn(BendHingeSettings s, double spacing) =>
        s.Material.TwistStrain * s.TwistLength / (s.Safety * StripSide(s, spacing));

    /// <summary>The tightest radius a regular hinge with lines <paramref name="spacing"/> apart takes (mm).</summary>
    public static double RadiusAt(BendHingeSettings s, double spacing) => spacing / StripTurn(s, spacing);

    /// <summary>The tightest radius the solid sheet takes by itself (mm).</summary>
    public static double SolidRadius(BendHingeSettings s) => s.Safety * s.Thickness / (2 * s.Material.BendStrain);

    /// <summary>
    /// The material figure from a regular hinge (slits <paramref name="cutLength"/> long with
    /// <paramref name="gap"/> between, lines <paramref name="spacing"/> apart) that bent around
    /// <paramref name="radius"/> without cracking. Its bend strain is kept from
    /// <paramref name="like"/>.
    /// </summary>
    public static HingeMaterial Calibrate(HingeMaterial like, double thickness, double kerf, double cutLength, double gap, double spacing, double radius)
    {
        var probe = new BendHingeSettings { Thickness = thickness, Kerf = kerf, CutLength = cutLength, Gap = gap, Safety = 1, Material = like };
        double side = StripSide(probe, spacing);
        double gamma = side * spacing / (Math.Max(0.1, radius) * probe.TwistLength);
        return like with { Name = $"{like.Name} (from your bend)", TwistStrain = gamma };
    }

    /// <summary>Lays out the slits for the panel that bends into <paramref name="profile"/>.</summary>
    public static BendHingeResult Make(BendProfile profile, BendHingeSettings s)
    {
        double L = profile.Length;
        double minW = s.MinSpacing, maxW = s.MaxSpacing;
        double solidCurv = 1 / SolidRadius(s);
        // Gentler than the solid sheet takes by itself, from x on for a strip w wide.
        bool Gentle(double from, double w) => profile.TurnBetween(from, Math.Min(L, from + w)) <= w * solidCurv;
        // Widest spacing from a line at x whose strip still takes the turning in it.
        (double Width, bool Fits) NextWidth(double x)
        {
            if (profile.TurnBetween(x, x + minW) > StripTurn(s, minW)) return (minW, false);
            double lo = minW, hi = maxW;
            if (profile.TurnBetween(x, x + hi) <= StripTurn(s, hi)) return (hi, true);
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) / 2;
                if (profile.TurnBetween(x, x + mid) <= StripTurn(s, mid)) lo = mid; else hi = mid;
            }
            return (lo, true);
        }

        var runs = new List<IReadOnlyList<double>>();
        var bands = new List<Band>();
        void Mark(double from, double to, BandKind kind, double radius)
        {
            if (to - from <= 1e-9) return;
            if (bands.Count > 0 && bands[^1].Kind == kind && Math.Abs(bands[^1].To - from) < 1e-6)
                bands[^1] = bands[^1] with { To = to, Radius = Math.Min(bands[^1].Radius, radius) };
            else bands.Add(new Band(from, to, kind, radius));
        }

        // Keep the ends of the panel clear of slits (the closest spacing, at least 1 mm).
        double edge = Math.Max(1, minW);
        double x = edge;
        double solidFrom = 0;
        double step = Math.Max(0.05, profile.Step);
        while (x <= L - edge)
        {
            if (Gentle(x, minW)) { x += step; continue; }
            // A run of lines, one after another, until the curve gets gentle again.
            Mark(solidFrom, x, BandKind.Solid, double.PositiveInfinity);
            var lines = new List<double> { x };
            runs.Add(lines);
            while (true)
            {
                if (Gentle(x, maxW))
                {
                    // The sheet takes the next strip by itself: the run ends, and that strip is solid.
                    solidFrom = x;
                    x += maxW;
                    break;
                }
                var (w, fits) = NextWidth(x);
                double turned = profile.TurnBetween(x, x + w);
                double radius = turned > 1e-12 ? w / turned : double.PositiveInfinity;
                if (x + w > L - edge)
                {
                    Mark(x, Math.Min(L, x + w), fits ? BandKind.Hinge : BandKind.TooTight, radius);
                    x += w;
                    solidFrom = x;
                    break;
                }
                Mark(x, x + w, fits ? BandKind.Hinge : BandKind.TooTight, radius);
                x += w;
                lines.Add(x);
            }
            solidFrom = Math.Min(L, solidFrom);
        }
        Mark(solidFrom, L, BandKind.Solid, double.PositiveInfinity);

        // The tightest bend, over the closest spacing, for the report.
        double tightest = double.PositiveInfinity;
        for (double a = 0; a + minW <= L + 1e-9; a += step)
        {
            double turned = profile.TurnBetween(a, a + minW);
            if (turned > 1e-12) tightest = Math.Min(tightest, minW / turned);
        }
        double reach = RadiusAt(s, minW);
        double? suggest = null;
        if (tightest < reach)
        {
            // The twist length scales the reach directly.
            double needL = s.TwistLength * reach / tightest;
            double cut = Math.Ceiling((2 * needL + s.Gap) * 2) / 2;
            if (cut + s.Gap <= s.Height) suggest = cut;
        }

        return new BendHingeResult
        {
            Profile = profile,
            Settings = s,
            Runs = runs,
            Slits = SlitLines(runs, s.Height, s.CutLength, s.Gap),
            Outline = PathGeometry.Rect(0, 0, L, s.Height),
            Bands = bands,
            TightestRadius = tightest,
            ReachableRadius = reach,
            SuggestedCutLength = suggest,
        };
    }

    /// <summary>
    /// Slits along each line, every other line shifted half a period and run out through the top
    /// and bottom edges (as the regular living hinge), so the strips between lines are joined by
    /// bridges that alternate from one side to the other. Each run starts with a line that stays
    /// inside the edges.
    /// </summary>
    public static PathGeometry SlitLines(IEnumerable<IReadOnlyList<double>> runs, double height, double cutLength, double gap)
    {
        double period = cutLength + gap;
        var subs = new List<Subpath>();
        foreach (var run in runs)
            for (int c = 0; c < run.Count; c++)
            {
                double x = run[c];
                bool inside = c % 2 == 0;
                double y = inside ? gap / 2 : gap / 2 + period / 2 - period;
                double lo = inside ? gap / 2 : 0, hi = inside ? height - gap / 2 : height;
                for (; y < height; y += period)
                {
                    double a = Math.Max(lo, y), b = Math.Min(hi, y + cutLength);
                    if (b - a >= 0.5) subs.Add(new Subpath(new Vec2(x, a), [Seg.Line(new Vec2(x, b))], false));
                }
            }
        return new PathGeometry(subs);
    }

    /// <summary>
    /// Where to cut a panel longer than <paramref name="maxLength"/> into pieces that fit: in its
    /// solid stretches (at least 3 mm from any slit) where it can, as few pieces as that allows.
    /// <paramref name="throughSlits"/> says a cut had to go through slits.
    /// </summary>
    public static List<double> SplitPoints(BendHingeResult r, double maxLength, out bool throughSlits)
    {
        const double Margin = 3;
        throughSlits = false;
        var o = new List<double>();
        if (!(maxLength > 2 * Margin)) return o;
        double start = 0, L = r.Length;
        while (L - start > maxLength)
        {
            double best = -1;
            foreach (var b in r.Bands.Where(b => b.Kind == BandKind.Solid))
            {
                double lo = Math.Max(b.From + (b.From > 0 ? Margin : 0), start + Margin);
                double hi = Math.Min(b.To - (b.To < L ? Margin : 0), start + maxLength);
                if (hi >= lo) best = Math.Max(best, hi);
            }
            if (best <= start + Margin)
            {
                best = start + maxLength;
                throughSlits = true;
            }
            o.Add(best);
            start = best;
        }
        return o;
    }

    /// <summary>
    /// One piece of a split panel, from <paramref name="from"/> to <paramref name="to"/> mm along
    /// it, moved so it starts at x = 0: its outline, its slits and its line-up marks.
    /// </summary>
    public static (PathGeometry Outline, PathGeometry Slits, PathGeometry Marks) Piece(BendHingeResult r, PathGeometry marks, double from, double to)
    {
        var shift = Affine.Translate(-from, 0);
        bool Inside(Subpath p) => p.Start.X >= from - 1e-9 && p.Start.X < to - 1e-9;
        return (
            PathGeometry.Rect(0, 0, to - from, r.Settings.Height),
            new PathGeometry(r.Slits.Subpaths.Where(Inside)).Transform(shift),
            new PathGeometry(marks.Subpaths.Where(Inside)).Transform(shift));
    }

    /// <summary>
    /// The formers for a closed curve: the curve brought in by half the thickness, so the panel's
    /// middle follows the curve when it's wrapped round them. Empty for an open curve.
    /// </summary>
    public static PathGeometry Former(PathGeometry world, double thickness)
    {
        var closed = new PathGeometry(world.Subpaths.Where(p => p.Closed));
        if (closed.IsEmpty) return PathGeometry.Empty;
        return ShapeGeometry.Offset(closed, -thickness / 2, JoinStyle.Round, EndStyle.Flat);
    }

    /// <summary>
    /// Short line-up marks, at the middle of each slit stretch: on the panel's top edge (panel
    /// coordinates), and on the former's edge where that part of the panel sits (world mm,
    /// pointing in), plus one on the former where the panel's ends meet. Each pair of marks meets
    /// when the panel is wrapped on.
    /// </summary>
    public static (PathGeometry Panel, PathGeometry Former) LineUpMarks(BendHingeResult r, double markLength = 2)
    {
        var onPanel = new List<Subpath>();
        var onFormer = new List<Subpath>();
        var p = r.Profile;
        double half = r.Settings.Thickness / 2;
        double sign = p.Closed ? (SignedArea(p.Points) >= 0 ? 1 : -1) : 1;
        // On a closed curve, the former is also marked where the panel's two ends meet.
        var places = r.Bands.Where(b => b.Kind != BandKind.Solid).Select(b => (b.From + b.To) / 2).ToList();
        foreach (var s in places)
            onPanel.Add(new Subpath(new Vec2(s, 0), [Seg.Line(new Vec2(s, markLength))], false));
        if (!p.Closed) return (new PathGeometry(onPanel), PathGeometry.Empty);
        foreach (var s in places.Prepend(0))
        {
            var d = p.DirectionAt(s);
            // Inward: to the left of travel when the signed area is positive, to the right otherwise.
            var inward = new Vec2(-d.Y, d.X) * sign;
            var at = p.PointAt(s) + inward * half;
            onFormer.Add(new Subpath(at, [Seg.Line(at + inward * markLength)], false));
        }
        return (new PathGeometry(onPanel), new PathGeometry(onFormer));
    }

    static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }
}
