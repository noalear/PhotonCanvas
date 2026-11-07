using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Addon.StockCarve;

// Profile cuts: taking the bulk of the waste off before carving, the way a bandsaw cuts a model's front and side
// profiles out of a block. The part is held still in the rotary, turned to a view, and the laser cuts straight down
// through it round the model's outline as seen from above (its silhouette). Everything outside the outline drops away.
// Then the part turns to the next view: its top and side (0° and 90°), then the 45s, then the 22.5s. What's left is the
// model's shape seen every way round, a little oversize, ready for the carving to finish.
//
// Views. Turning the part θ (the way a design runs down the page) brings what was θ round to the top; a point
// (x, y, z) of the rotary's frame is then at (x, y cos θ + z sin θ, z cos θ − y sin θ). Seen from above, a point is at
// x along the axis and h = y cos θ + z sin θ across it; on the bed that's design X = x, design Y = the laser line − h
// (the rotary's +Y is toward the back of the machine). Turned half a turn more, the same outline is seen mirrored, so
// a view can be cut from both sides, each half as deep, the two cuts meeting at the middle.

/// <summary>How the profile cuts are made.</summary>
public sealed record ProfileOptions
{
    /// <summary>How many views round half a turn: 2 (top and side), 4 (and the 45s) or 8 (and the 22.5s).</summary>
    public int Directions { get; init; } = 8;
    /// <summary>Cut each view from above and from below (turned half a turn), each cut going just past the middle: half as deep.</summary>
    public bool BothSides { get; init; } = true;
    /// <summary>The first view (degrees round from where the scan started): usually the model's top.</summary>
    public double StartDeg { get; init; }
    /// <summary>Left on outside the model's outline (mm), for the carving to take off.</summary>
    public double LeaveMm { get; init; } = 0.5;
    /// <summary>Half the width of the bar kept from the chuck to the model (mm), so the part stays held.</summary>
    public double CoreMm { get; init; } = 4;
    /// <summary>The chuck holds the stock's left end (low X); off: its right end.</summary>
    public bool ChuckOnLeft { get; init; } = true;
    /// <summary>Keep a bar to the other end too (a tailstock or support holds it there).</summary>
    public bool CoreToBothEnds { get; init; }
    /// <summary>How wide the cut is (mm): lines side by side, out into the waste, so the beam can reach the bottom of a deep cut.</summary>
    public double TrenchMm { get; init; } = 1.2;
    /// <summary>How far apart those lines are (mm).</summary>
    public double LineGapMm { get; init; } = 0.15;
    /// <summary>How far the focus moves down between focus heights (mm).</summary>
    public double FocusStepMm { get; init; } = 2;
    /// <summary>How deep one pass of the cut settings goes (mm): the passes at each focus height are the step ÷ this.</summary>
    public double PassDepthMm { get; init; } = 0.25;
    /// <summary>Cutting from both sides: how far past the middle each cut goes (mm), so they meet.</summary>
    public double OverlapMm { get; init; } = 1;

    /// <summary>The passes at each focus height.</summary>
    public int PassesPerStep => Math.Clamp((int)Math.Ceiling(FocusStepMm / Math.Max(0.01, PassDepthMm) - 1e-9), 1, 200);
}

/// <summary>One cut: the part turned to <paramref name="ViewDeg"/> (degrees from where the scan started), seen from above.</summary>
/// <param name="View">Which view it is (0 = the first).</param>
/// <param name="Side">0: the first side of the view; 1: the other side (half a turn on).</param>
/// <param name="EndsView">After this cut the view should be through: check it.</param>
public sealed record ProfileStep(int Index, int View, double ViewDeg, int Side, bool EndsView)
{
    /// <summary>"90°, from below" for the window.</summary>
    public string Label(ProfileOptions o, int views)
    {
        string name = $"{Fmt(ViewDeg)}°";
        return o.BothSides ? $"View {View + 1} of {views}: {name} ({(Side == 0 ? "first side" : "other side")})" : $"View {View + 1} of {views}: {name}";
    }

    static string Fmt(double d) => ((d % 360 + 360) % 360).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One focus height of a cut: the lines (design mm) cut with the focus moved down <paramref name="FocusOffsetMm"/>.</summary>
public sealed record ProfileBand(int Index, double FocusOffsetMm, PathGeometry Lines, double LengthMm);

/// <summary>A cut, worked out.</summary>
/// <param name="Keep">What's kept, seen from above (x along the axis, h across it): the model's outline (grown by the leave) and the bar to the chuck.</param>
/// <param name="Shadow">Where there's material, seen from above (x, h).</param>
/// <param name="Bands">The focus heights, top first (empty: there's nothing to cut).</param>
/// <param name="FocusX">Focus on the part's top here (design X and Y, Y usually the laser line) before the cut.</param>
/// <param name="TopMm">How far that top is above the axis (mm).</param>
/// <param name="DeepestMm">How far below that the cut goes (mm).</param>
public sealed record ProfilePlan(ProfileStep Step, PathGeometry Keep, PathGeometry Shadow, IReadOnlyList<ProfileBand> Bands, double FocusX, double FocusY, double TopMm, double DeepestMm, int PassesPerStep)
{
    public double LengthMm => Bands.Sum(b => b.LengthMm);

    /// <summary>"X 110 mm on the laser line", or with its Y when it's off the line.</summary>
    public string FocusAt(double lineY) => Math.Abs(FocusY - lineY) < 0.05 ? $"X {FocusX:0.#} mm on the laser line" : $"X {FocusX:0.#} mm, Y {FocusY:0.#} mm";
}

/// <summary>
/// What's left of the part as the cuts go: how far out the material reaches (mm from the axis) every few degrees round
/// and every so often along the axis. Each view's cut leaves only what's inside its outline, seen that way.
/// </summary>
public sealed class Remaining
{
    /// <summary>The places along the axis (design X, mm, rising, evenly spaced).</summary>
    public double[] Xs { get; }
    /// <summary>How many angles round (evenly spaced, from 0°).</summary>
    public int NA { get; }
    /// <summary>The reach at [angle, x] (mm; 0 = nothing).</summary>
    public double[,] R { get; }

    public Remaining(double[] xs, int nA, double[,] r)
    {
        if (xs.Length < 1 || nA < 8 || r.GetLength(0) != nA || r.GetLength(1) != xs.Length) throw new ArgumentException("Bad map of what's left.");
        Xs = xs;
        NA = nA;
        R = r;
    }

    public double StepDeg => 360.0 / NA;
    double Dx => Xs.Length > 1 ? (Xs[^1] - Xs[0]) / (Xs.Length - 1) : 1;
    public double MinX => Xs[0];
    public double MaxX => Xs[^1];

    /// <summary>The stock, every <paramref name="stepDeg"/> round and <paramref name="stepMm"/> along. It must have been measured all the way round.</summary>
    public static Remaining From(StockMap stock, double stepDeg = 2, double stepMm = 0.5)
    {
        if (!stock.WholeTurn) throw new ArgumentException("Profile cuts need the stock all the way round: scan a whole turn, or pick the standard bar it is.");
        int nA = Math.Max(16, (int)Math.Round(360 / Math.Max(0.5, stepDeg)));
        int nX = Math.Max(2, (int)Math.Ceiling(stock.LengthMm / Math.Max(0.1, stepMm)) + 1);
        var xs = Enumerable.Range(0, nX).Select(i => stock.MinX + stock.LengthMm * i / (nX - 1)).ToArray();
        var r = new double[nA, nX];
        for (int a = 0; a < nA; a++)
            for (int k = 0; k < nX; k++)
            {
                double v = stock.RadiusAt(a * 360.0 / nA, xs[k]);
                r[a, k] = double.IsFinite(v) && v > 0 ? v : 0;
            }
        return new Remaining(xs, nA, r);
    }

    /// <summary>The column nearest <paramref name="x"/> (−1 past the ends).</summary>
    public int Column(double x)
    {
        double dx = Dx;
        if (x < Xs[0] - dx / 2 - 1e-9 || x > Xs[^1] + dx / 2 + 1e-9) return -1;
        return Math.Clamp((int)Math.Round((x - Xs[0]) / dx), 0, Xs.Length - 1);
    }

    /// <summary>The cross-section at column <paramref name="k"/> seen in view <paramref name="viewDeg"/>: points (h across, z up).</summary>
    public Vec2[] Section(int k, double viewDeg)
    {
        var pts = new Vec2[NA];
        for (int a = 0; a < NA; a++)
        {
            double t = (a * StepDeg - viewDeg) * Math.PI / 180, r = R[a, k];
            pts[a] = new Vec2(-r * Math.Sin(t), r * Math.Cos(t));
        }
        return pts;
    }

    /// <summary>The top and bottom of the material on the vertical line at (x, h) in a view (mm above the axis); null where there's none.</summary>
    public (double Top, double Bottom)? TopBottom(double x, double h, double viewDeg)
    {
        int k = Column(x);
        if (k < 0) return null;
        var s = Section(k, viewDeg);
        double top = double.NegativeInfinity, bottom = double.PositiveInfinity;
        for (int i = 0; i < s.Length; i++)
        {
            var p = s[i];
            var q = s[(i + 1) % s.Length];
            if ((p.X > h) == (q.X > h) && p.X != h) continue;
            double z = Math.Abs(q.X - p.X) < 1e-12 ? Math.Max(p.Y, q.Y) : p.Y + (q.Y - p.Y) * (h - p.X) / (q.X - p.X);
            double z2 = Math.Abs(q.X - p.X) < 1e-12 ? Math.Min(p.Y, q.Y) : z;
            top = Math.Max(top, z);
            bottom = Math.Min(bottom, z2);
        }
        return top > bottom + 1e-6 ? (top, bottom) : null;
    }

    /// <summary>Where there's material seen from above in a view (x along, h across), grown by <paramref name="growMm"/>.</summary>
    public PathGeometry Shadow(double viewDeg, double growMm = 0)
    {
        double dx = Dx;
        var rects = new Paths64();
        for (int k = 0; k < Xs.Length; k++)
        {
            var s = Section(k, viewDeg);
            double lo = s.Min(p => p.X), hi = s.Max(p => p.X);
            if (hi - lo < 1e-3) continue;
            rects.Add(Rect64(Xs[k] - dx / 2 - 0.01, lo, Xs[k] + dx / 2 + 0.01, hi));
        }
        if (rects.Count == 0) return PathGeometry.Empty;
        var g = ProfileCut.ToGeometry(Clipper.Union(rects, FillRule.NonZero));
        return growMm != 0 ? ShapeGeometry.Offset(g, growMm, JoinStyle.Round, EndStyle.Round) : g;
    }

    static Path64 Rect64(double x0, double y0, double x1, double y1) =>
    [
        ProfileCut.P(x0, y0), ProfileCut.P(x1, y0), ProfileCut.P(x1, y1), ProfileCut.P(x0, y1),
    ];

    /// <summary>What's left once a view is cut through round <paramref name="keep"/> (x, h): what's outside it, seen that way, is gone.</summary>
    public Remaining After(PathGeometry keep, double viewDeg)
    {
        var loops = keep.IsEmpty ? [] : StackSlicer.Loops(keep);
        var r = (double[,])R.Clone();
        for (int k = 0; k < Xs.Length; k++)
        {
            var spans = ProfileCut.Spans(loops, Xs[k]);
            // The furthest the kept outline reaches each way across, and whether it covers the axis.
            double neg = 0, pos = 0;
            bool onAxis = false;
            foreach (var (lo, hi) in spans)
            {
                if (lo < 0) neg = Math.Max(neg, -lo);
                if (hi > 0) pos = Math.Max(pos, hi);
                if (lo <= 0 && hi >= 0) onAxis = true;
            }
            for (int a = 0; a < NA; a++)
            {
                if (r[a, k] <= 0) continue;
                // Along the ray at this angle, h = −r sin(φ − θ).
                double s = Math.Sin((a * StepDeg - viewDeg) * Math.PI / 180);
                double reach;
                if (Math.Abs(s) < 1e-6) reach = onAxis ? double.PositiveInfinity : 0;
                else reach = (s > 0 ? neg : pos) / Math.Abs(s);
                // A ray that starts outside the outline but crosses it further out keeps all of that (it can't be told apart here): erring on the side of more left.
                r[a, k] = Math.Min(r[a, k], reach);
            }
        }
        return new Remaining(Xs, NA, r);
    }

    /// <summary>What's left as a stock map, for the carving to start from.</summary>
    public StockMap ToStock()
    {
        var angles = Enumerable.Range(0, NA).Select(a => a * StepDeg).ToArray();
        var r = new double[NA, Xs.Length];
        for (int a = 0; a < NA; a++)
            for (int k = 0; k < Xs.Length; k++) r[a, k] = R[a, k];
        return new StockMap(angles, (double[])Xs.Clone(), r, true);
    }

    /// <summary>How much material is left (mm³).</summary>
    public double VolumeMm3()
    {
        double dx = Dx, dt = StepDeg * Math.PI / 180, v = 0;
        for (int k = 0; k < Xs.Length; k++)
        {
            double w = Xs.Length == 1 ? 1 : k == 0 || k == Xs.Length - 1 ? dx / 2 : dx;
            for (int a = 0; a < NA; a++) v += R[a, k] * R[a, k] / 2 * dt * w;
        }
        return v;
    }
}

/// <summary>Working out the profile cuts.</summary>
public static class ProfileCut
{
    /// <summary>The key a cut's shapes carry.</summary>
    public const string NoteKey = "stock-carve.profile";

    const double Scale = 1000;

    internal static Point64 P(double x, double y) => new((long)Math.Round(x * Scale), (long)Math.Round(y * Scale));

    internal static PathGeometry ToGeometry(Paths64 paths)
    {
        var subs = new List<Subpath>();
        foreach (var p in paths)
        {
            if (p.Count < 3) continue;
            var pts = p.Select(q => new Vec2(q.X / Scale, q.Y / Scale)).ToList();
            subs.AddRange(PathGeometry.Polyline(pts, true).Subpaths);
        }
        return new PathGeometry(subs);
    }

    /// <summary>The views in the order they're cut: 0° and 90° (top and side), then the 45s, then the 22.5s, each from <see cref="ProfileOptions.StartDeg"/>.</summary>
    public static double[] Views(ProfileOptions o)
    {
        double[] order = o.Directions switch
        {
            <= 1 => [0],
            2 or 3 => [0, 90],
            >= 4 and < 8 => [0, 90, 45, 135],
            _ => [0, 90, 45, 135, 22.5, 67.5, 112.5, 157.5],
        };
        return [.. order.Select(d => Wrap360(d + o.StartDeg))];
    }

    /// <summary>
    /// The cuts in order. Cutting from both sides, each view is cut twice, half a turn apart; the side cut first is
    /// whichever is the shorter turn from the cut before (the first one from <paramref name="fromDeg"/>).
    /// </summary>
    public static List<ProfileStep> Steps(ProfileOptions o, double fromDeg = 0)
    {
        var views = Views(o);
        var steps = new List<ProfileStep>();
        double at = fromDeg;
        for (int v = 0; v < views.Length; v++)
        {
            double a = views[v];
            if (!o.BothSides)
            {
                steps.Add(new ProfileStep(steps.Count, v, a, 0, true));
                at = a;
                continue;
            }
            double b = Wrap360(a + 180);
            if (Math.Abs(Wrap180(b - at)) < Math.Abs(Wrap180(a - at)) - 1e-9) (a, b) = (b, a);
            steps.Add(new ProfileStep(steps.Count, v, a, 0, false));
            steps.Add(new ProfileStep(steps.Count, v, b, 1, true));
            at = b;
        }
        return steps;
    }

    /// <summary>The shortest turn from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (degrees, −180 to 180; + the way a design runs down the page).</summary>
    public static double TurnBetween(double fromDeg, double toDeg) => Wrap180(toDeg - fromDeg);

    internal static double Wrap360(double d) => ((d % 360) + 360) % 360;
    internal static double Wrap180(double d) => ((d % 360) + 540) % 360 - 180;

    /// <summary>The model seen from above in a view (x along the axis, h across it): everything it covers.</summary>
    public static PathGeometry Silhouette(IReadOnlyList<Tri> placed, double viewDeg)
    {
        double t = viewDeg * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t);
        var tris = new Paths64(placed.Count);
        foreach (var tri in placed)
        {
            var a = P(tri.A.X, tri.A.Y * c + tri.A.Z * s);
            var b = P(tri.B.X, tri.B.Y * c + tri.B.Z * s);
            var d = P(tri.C.X, tri.C.Y * c + tri.C.Z * s);
            long cross = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
            if (cross == 0) continue;
            // All one way round, so overlapping triangles add up (never cancel) when they're joined.
            tris.Add(cross > 0 ? [a, b, d] : [a, d, b]);
        }
        if (tris.Count == 0) return PathGeometry.Empty;
        return ToGeometry(Clipper.Union(tris, FillRule.NonZero));
    }

    /// <summary>
    /// What a view keeps (x, h): the model's outline grown by the leave, and the bar from the chuck to the model's
    /// middle (to both ends if asked) so it stays held.
    /// </summary>
    public static PathGeometry Keep(IReadOnlyList<Tri> placed, double viewDeg, ProfileOptions o, double stockMinX, double stockMaxX)
    {
        var sil = Silhouette(placed, viewDeg);
        if (sil.IsEmpty) return sil;
        var model = o.LeaveMm > 0 ? ShapeGeometry.Offset(sil, o.LeaveMm, JoinStyle.Round, EndStyle.Round) : sil;
        double mx0 = placed.Min(tr => Math.Min(tr.A.X, Math.Min(tr.B.X, tr.C.X))), mx1 = placed.Max(tr => Math.Max(tr.A.X, Math.Max(tr.B.X, tr.C.X)));
        double mid = (mx0 + mx1) / 2, core = Math.Max(0.5, o.CoreMm);
        var bars = new List<PathGeometry> { model };
        if (o.ChuckOnLeft || o.CoreToBothEnds) bars.Add(PathGeometry.Rect(stockMinX - 5, -core, mid - (stockMinX - 5), 2 * core));
        if (!o.ChuckOnLeft || o.CoreToBothEnds) bars.Add(PathGeometry.Rect(mid, -core, stockMaxX + 5 - mid, 2 * core));
        return ShapeGeometry.Union(bars);
    }

    /// <summary>Where the closed <paramref name="loops"/> (x, h) cross the line at <paramref name="x"/>: the h spans inside them (even-odd).</summary>
    public static List<(double Lo, double Hi)> Spans(List<List<Vec2>> loops, double x)
    {
        var hs = new List<double>();
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                var q = loop[(i + 1) % loop.Count];
                if ((p.X > x) == (q.X > x)) continue;
                hs.Add(p.Y + (q.Y - p.Y) * (x - p.X) / (q.X - p.X));
            }
        hs.Sort();
        var spans = new List<(double, double)>();
        for (int i = 0; i + 1 < hs.Count; i += 2) spans.Add((hs[i], hs[i + 1]));
        return spans;
    }

    /// <summary>
    /// The cut for one step: lines round the outline (a trench of them, out into the waste), only where there's material
    /// under them, split into focus heights from the top of the part down to where the cut has to reach. Focus on the
    /// part's top at <paramref name="focusX"/> on the laser line (where the machine's auto-focus measures) if there's
    /// material there, else at the highest point along the axis. <paramref name="lineY"/> is the laser line on the bed
    /// (design Y over the axis).
    /// </summary>
    public static ProfilePlan Plan(Remaining rem, IReadOnlyList<Tri> placed, ProfileStep step, ProfileOptions o, double lineY, double focusX, CancellationToken ct = default)
    {
        if (!(o.FocusStepMm > 0)) throw new ArgumentException("The focus step must be more than zero.");
        if (!(o.LineGapMm > 0)) throw new ArgumentException("The gap between the lines must be more than zero.");
        if (placed.Count == 0) throw new ArgumentException("There's no model to cut round.");
        double view = step.ViewDeg;
        var keep = Keep(placed, view, o, rem.MinX, rem.MaxX);
        if (keep.IsEmpty) throw new ArgumentException("The model has no outline in this view.");
        var shadow = rem.Shadow(view);
        ct.ThrowIfCancellationRequested();

        // The trench: lines round the kept outline, each a gap further out, where there's material under them.
        int n = Math.Clamp((int)Math.Round(Math.Max(o.TrenchMm, o.LineGapMm) / o.LineGapMm), 1, 60);
        var region = rem.Shadow(view, 1);
        var runs = new List<List<(Vec2 P, double Top, double Target)>>();
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ring = ShapeGeometry.Offset(keep, (i + 0.5) * o.LineGapMm, JoinStyle.Round, EndStyle.Round);
            var lines = ShapeGeometry.ClipLines(ring, region);
            foreach (var f in lines.Flatten(0.02))
            {
                var pts = Dense(f.Points, f.Closed, 0.5);
                List<(Vec2, double, double)>? run = null;
                foreach (var p in pts)
                {
                    if (rem.TopBottom(p.X, p.Y, view) is not { } tb)
                    {
                        if (run is { Count: >= 2 }) runs.Add(run);
                        run = null;
                        continue;
                    }
                    // Down to just past the middle (the other side meets it), or right through where it's all above that.
                    double target = o.BothSides && tb.Bottom < -o.OverlapMm ? -o.OverlapMm : tb.Bottom - 0.5;
                    (run ??= []).Add((p, tb.Top, target));
                }
                if (run is { Count: >= 2 }) runs.Add(run);
            }
        }

        // Where to focus: the top of the part on the laser line (over the axis). The highest spot along it, or where the
        // machine's auto-focus measures (the field's middle) when that's nearly as high. Nothing on the line: the highest
        // spot the cut goes through.
        double fx = focusX, fh = 0, zRef;
        double lineTop = double.NegativeInfinity, lineX = focusX;
        foreach (var x in rem.Xs)
            if (rem.TopBottom(x, 0, view) is { } tb && tb.Top > lineTop) { lineTop = tb.Top; lineX = x; }
        if (double.IsFinite(lineTop))
        {
            var mid = rem.TopBottom(focusX, 0, view);
            if (mid is { } m && m.Top >= lineTop - o.FocusStepMm / 2) zRef = m.Top;
            else { fx = lineX; zRef = lineTop; }
        }
        else if (runs.Count > 0)
        {
            var top = runs.SelectMany(r => r).MaxBy(p => p.Top);
            (fx, fh, zRef) = (top.P.X, top.P.Y, top.Top);
        }
        else zRef = 0;

        // Focus heights: band j has the focus (j + ½) steps below the top where it was set.
        var bands = new List<ProfileBand>();
        double highest = runs.SelectMany(r => r).Select(p => p.Top).DefaultIfEmpty(zRef).Max();
        double lowest = runs.SelectMany(r => r).Select(p => p.Target).DefaultIfEmpty(zRef).Min();
        double fstep = o.FocusStepMm;
        int j0 = (int)Math.Floor((zRef - highest) / fstep), j1 = (int)Math.Ceiling((zRef - lowest) / fstep) - 1;
        for (int j = j0; j <= j1 && runs.Count > 0; j++)
        {
            ct.ThrowIfCancellationRequested();
            double zTop = zRef - j * fstep, zBottom = zTop - fstep;
            var subs = new List<Subpath>();
            double len = 0;
            foreach (var run in runs)
            {
                List<Vec2>? piece = null;
                foreach (var (p, top, target) in run)
                {
                    // Material reaches into the band here, and the cut has to go down into it.
                    bool cut = top > zBottom + 1e-6 && target < zTop - 1e-6;
                    if (cut) (piece ??= []).Add(new Vec2(p.X, lineY - p.Y));
                    else { Flush(); }
                }
                Flush();

                void Flush()
                {
                    if (piece is { Count: >= 2 })
                    {
                        double l = 0;
                        for (int i = 1; i < piece.Count; i++) l += piece[i].Dist(piece[i - 1]);
                        if (l >= 0.3)
                        {
                            subs.AddRange(PathGeometry.Polyline(piece, false).Subpaths);
                            len += l;
                        }
                    }
                    piece = null;
                }
            }
            if (subs.Count > 0) bands.Add(new ProfileBand(bands.Count, (j + 0.5) * fstep, new PathGeometry(subs), len));
        }
        double deepest = bands.Count == 0 ? 0 : bands[^1].FocusOffsetMm + fstep / 2;
        return new ProfilePlan(step, keep, shadow, bands, fx, lineY - fh, zRef, deepest, o.PassesPerStep);
    }

    /// <summary>Points no more than <paramref name="most"/> mm apart along a line.</summary>
    static List<Vec2> Dense(List<Vec2> pts, bool closed, double most)
    {
        var o = new List<Vec2>();
        int n = pts.Count + (closed ? 1 : 0);
        for (int i = 0; i < n; i++)
        {
            var p = pts[i % pts.Count];
            if (o.Count > 0)
            {
                var q = o[^1];
                double d = p.Dist(q);
                int k = (int)Math.Ceiling(d / most);
                for (int m = 1; m < k; m++) o.Add(new Vec2(q.X + (p.X - q.X) * m / k, q.Y + (p.Y - q.Y) * m / k));
            }
            o.Add(p);
        }
        return o;
    }

    /// <summary>
    /// The job for a cut (or for some of its focus heights): a cut layer a focus height, with the cut settings
    /// (<paramref name="cut"/>, its passes set to the passes a height). The part is held still in the rotary and
    /// it's cut on the flat.
    /// </summary>
    public static DocState Job(ProfilePlan plan, Layer cut, IEnumerable<int>? onlyBands = null)
    {
        var want = onlyBands?.ToHashSet();
        var layers = new List<Layer>();
        var shapes = new List<Shape>();
        string note = FormattableString.Invariant($"view={plan.Step.ViewDeg:0.###}");
        foreach (var b in plan.Bands)
        {
            if (want is not null && !want.Contains(b.Index)) continue;
            var layer = cut with
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                Name = FormattableString.Invariant($"Profile {plan.Step.ViewDeg:0.#}° · focus {b.FocusOffsetMm:+0.0#;-0.0#;0.0} mm"),
                Color = Layer.Palette[b.Index % Layer.Palette.Count],
                Mode = LayerMode.Cut,
                Laser = cut.Laser with { Passes = plan.PassesPerStep },
                FocusOffsetMm = b.FocusOffsetMm,
                StepDownMm = 0, StepDownFirstMm = 0, TabCount = 0, KerfMm = 0,
                Output = true, Visible = true,
            };
            layers.Add(layer);
            shapes.Add(new PathShape { Name = $"Cut round the profile at {b.FocusOffsetMm:0.#} mm down", LayerId = layer.Id, Geometry = b.Lines }.WithAddonData(NoteKey, note));
        }
        return DocState.New() with { Layers = [.. layers], Shapes = [.. shapes] };
    }

    /// <summary>The deepest focus heights of a cut (the last <paramref name="count"/>), for more passes where it hasn't gone through.</summary>
    public static IEnumerable<int> Deepest(ProfilePlan plan, int count) => plan.Bands.Skip(Math.Max(0, plan.Bands.Count - Math.Max(1, count))).Select(b => b.Index);

    /// <summary>What a cut means, in a few lines.</summary>
    public static string Describe(ProfilePlan p, ProfileOptions o, double lineY)
    {
        if (p.Bands.Count == 0) return "Nothing to cut in this view: there's no material outside the model's outline any more.";
        int passes = p.PassesPerStep * p.Bands.Count;
        return $"Focus on the top of the part at {p.FocusAt(lineY)} ({p.TopMm:0.#} mm above the axis). " +
               $"The cut goes {p.DeepestMm:0.#} mm down from there in {p.Bands.Count} focus height{(p.Bands.Count == 1 ? "" : "s")} {o.FocusStepMm:0.#} mm apart, " +
               $"{p.PassesPerStep} pass{(p.PassesPerStep == 1 ? "" : "es")} at each ({passes} in all, {p.LengthMm / 1000:0.##} m of line each pass round).";
    }
}

/// <summary>Is a cut through? Two photos of the part from above, before the cut and after it, compared where the waste was.</summary>
public static class ThroughCheck
{
    public const byte Ignore = 0, Kept = 1, Waste = 2;

    /// <summary>What each pixel of a photo of <paramref name="area"/> (design mm, <paramref name="mmPerPx"/> a pixel) shows: kept, waste (away from the cut), or neither.</summary>
    public static byte[] Zones(ProfilePlan plan, ProfileOptions o, double lineY, Rect2 area, double mmPerPx, out int w, out int h)
    {
        int ww = Math.Max(1, (int)Math.Round(area.Width / mmPerPx)), hh = Math.Max(1, (int)Math.Round(area.Height / mmPerPx));
        // Clear of the burnt edges either side of the cut.
        const double Margin = 1.5;
        var kept = ShapeGeometry.Boolean(ShapeGeometry.Offset(plan.Keep, -Margin, JoinStyle.Round, EndStyle.Round), plan.Shadow, BoolOp.Intersect);
        var waste = ShapeGeometry.Boolean(plan.Shadow, ShapeGeometry.Offset(plan.Keep, o.TrenchMm + Margin, JoinStyle.Round, EndStyle.Round), BoolOp.Subtract);
        var zones = new byte[ww * hh];
        foreach (var (g, value) in new[] { (kept, Kept), (waste, Waste) })
        {
            var bits = Raster(ToBed(g, lineY), area, mmPerPx, ww, hh);
            for (int i = 0; i < zones.Length; i++) if (bits[i]) zones[i] = value;
        }
        w = ww;
        h = hh;
        return zones;
    }

    /// <summary>Outlines in (x, h) as loops on the bed (design X, Y).</summary>
    static List<List<Vec2>> ToBed(PathGeometry g, double lineY) =>
        g.IsEmpty ? [] : [.. StackSlicer.Loops(g).Select(l => l.Select(p => new Vec2(p.X, lineY - p.Y)).ToList())];

    /// <summary>The pixels inside the loops (even-odd), row by row.</summary>
    public static bool[] Raster(List<List<Vec2>> loops, Rect2 area, double mmPerPx, int w, int h)
    {
        var o = new bool[w * h];
        var xs = new List<double>();
        for (int row = 0; row < h; row++)
        {
            double y = area.MinY + (row + 0.5) * mmPerPx;
            xs.Clear();
            foreach (var loop in loops)
                for (int i = 0; i < loop.Count; i++)
                {
                    var p = loop[i];
                    var q = loop[(i + 1) % loop.Count];
                    if ((p.Y > y) == (q.Y > y)) continue;
                    xs.Add(p.X + (q.X - p.X) * (y - p.Y) / (q.Y - p.Y));
                }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int c0 = Math.Max(0, (int)Math.Ceiling((xs[i] - area.MinX) / mmPerPx - 0.5));
                int c1 = Math.Min(w - 1, (int)Math.Floor((xs[i + 1] - area.MinX) / mmPerPx - 0.5));
                for (int c = c0; c <= c1; c++) o[row * w + c] = true;
            }
        }
        return o;
    }

    /// <summary>
    /// Compares two grey photos (0–255, the same size) zone by zone. The kept part should look the same; where the
    /// waste was, it has dropped away if the cut is through, so it looks different. Brightness changes between the
    /// photos are evened out first (from the kept part).
    /// </summary>
    public static ThroughResult Judge(byte[] before, byte[] after, byte[] zones, int w, int h)
    {
        if (before.Length != w * h || after.Length != w * h || zones.Length != w * h) throw new ArgumentException("The photos and zones must be the same size.");
        var b = Blur(before, w, h);
        var a = Blur(after, w, h);
        double sumB = 0, sumA = 0;
        int nKept = 0, nWaste = 0;
        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i] == Kept) { sumB += b[i]; sumA += a[i]; nKept++; }
            else if (zones[i] == Waste) nWaste++;
        }
        double gain = nKept > 0 && sumA > 1 ? sumB / sumA : 1;
        gain = Math.Clamp(gain, 0.5, 2);
        const double Changed = 28;
        int keptChanged = 0, wasteChanged = 0;
        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i] == Ignore) continue;
            bool diff = Math.Abs(b[i] - a[i] * gain) > Changed;
            if (!diff) continue;
            if (zones[i] == Kept) keptChanged++; else wasteChanged++;
        }
        double kf = nKept == 0 ? 0 : (double)keptChanged / nKept, wf = nWaste == 0 ? 0 : (double)wasteChanged / nWaste;
        ThroughVerdict v = nWaste < 20 ? ThroughVerdict.CantTell
            : wf >= 0.5 && wf - kf >= 0.3 ? ThroughVerdict.Through
            : wf >= 0.15 && wf - kf >= 0.1 ? ThroughVerdict.Partly
            : ThroughVerdict.NotThrough;
        return new ThroughResult(v, wf, kf, nWaste, nKept);
    }

    static float[] Blur(byte[] g, int w, int h)
    {
        var o = new float[g.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        s += g[yy * w + xx];
                        n++;
                    }
                }
                o[y * w + x] = (float)s / n;
            }
        return o;
    }

    /// <summary>A photo as grey levels (from RGBA bytes).</summary>
    public static byte[] Grey(byte[] rgba, int w, int h)
    {
        var o = new byte[w * h];
        for (int i = 0; i < o.Length; i++) o[i] = (byte)Math.Clamp((int)Math.Round(0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]), 0, 255);
        return o;
    }
}

public enum ThroughVerdict { Through, Partly, NotThrough, CantTell }

/// <param name="WasteChanged">How much of where the waste was looks different now (0–1).</param>
/// <param name="KeptChanged">How much of the kept part looks different (0–1): high means the photos can't be compared well.</param>
public sealed record ThroughResult(ThroughVerdict Verdict, double WasteChanged, double KeptChanged, int WastePixels, int KeptPixels)
{
    public string Text => Verdict switch
    {
        ThroughVerdict.Through => $"Looks through: {WasteChanged:0%} of where the waste was has changed (the kept part {KeptChanged:0%}). The waste has dropped away.",
        ThroughVerdict.Partly => $"Partly through: {WasteChanged:0%} of where the waste was has changed (the kept part {KeptChanged:0%}). Some waste may still be hanging on.",
        ThroughVerdict.NotThrough => $"Not through yet: only {WasteChanged:0%} of where the waste was has changed. Give it more passes.",
        _ => "Can't tell from the photos (there's hardly any waste in view). Look at the part.",
    };
}
