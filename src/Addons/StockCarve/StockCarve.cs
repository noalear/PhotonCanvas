using PhotonCanvas.Addon.RotaryFinish;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Addon.StockCarve;

// The rotary's frame, the one "Scan the part" uses: X along the axis (the design's X, mm from the left of
// the field), the axis at Y = Z = 0, and Z straight up at the laser when the job starts. A point θ degrees
// round sits at (x, −r sin θ, r cos θ): turning the part θ the way a design runs down the page brings it to
// the top, so on the design it is θ/360 of the circumference past the start line.

/// <summary>
/// The stock as it is in the rotary: how far out its surface is (mm from the axis) at each angle round and each
/// X along. Made from a scan of the part, or from a 3D model of it in the rotary's frame.
/// </summary>
public sealed class StockMap
{
    /// <summary>The angles measured (degrees, 0–360, rising).</summary>
    public double[] AnglesDeg { get; }
    /// <summary>The places along the axis measured (design X, mm, rising).</summary>
    public double[] Xs { get; }
    /// <summary>The radius at [angle, x] (mm from the axis); NaN = not known there.</summary>
    public double[,] R { get; }
    /// <summary>The angles go all the way round (the last one joins up with the first).</summary>
    public bool WholeTurn { get; }

    public StockMap(double[] anglesDeg, double[] xs, double[,] r, bool wholeTurn)
    {
        if (anglesDeg.Length == 0 || xs.Length == 0) throw new ArgumentException("The stock has no measurements.");
        if (r.GetLength(0) != anglesDeg.Length || r.GetLength(1) != xs.Length) throw new ArgumentException("The stock's measurements don't match its angles and places.");
        AnglesDeg = anglesDeg;
        Xs = xs;
        R = r;
        WholeTurn = wholeTurn;
    }

    public double MinX => Xs[0];
    public double MaxX => Xs[^1];
    public double LengthMm => MaxX - MinX;

    /// <summary>The furthest out it goes anywhere (mm).</summary>
    public double MaxRadius
    {
        get
        {
            double m = 0;
            foreach (var v in R) if (v > m) m = v;
            return m;
        }
    }

    /// <summary>
    /// From a scan's radii (<c>RotaryScan.Radii(axisHeight)</c>: [angle][x], null = not read), its angles in degrees
    /// (any, wrapped to 0–360 and sorted) and its places along the axis.
    /// </summary>
    public static StockMap FromRadii(IReadOnlyList<double> anglesDeg, IReadOnlyList<double> xs, IReadOnlyList<IReadOnlyList<double?>> radii, bool wholeTurn)
    {
        var order = Enumerable.Range(0, anglesDeg.Count).Select(i => (A: Wrap360(anglesDeg[i]), I: i)).OrderBy(t => t.A).ToList();
        var xOrder = Enumerable.Range(0, xs.Count).OrderBy(i => xs[i]).ToList();
        var r = new double[order.Count, xOrder.Count];
        for (int a = 0; a < order.Count; a++)
            for (int k = 0; k < xOrder.Count; k++)
            {
                var row = order[a].I < radii.Count ? radii[order[a].I] : null;
                int xi = xOrder[k];
                r[a, k] = row is not null && xi < row.Count && row[xi] is { } v && v >= 0 ? v : double.NaN;
            }
        return new StockMap([.. order.Select(t => t.A)], [.. xOrder.Select(i => xs[i])], r, wholeTurn);
    }

    /// <summary>
    /// From a 3D model of the stock in the rotary's frame (as "Scan the part" saves it): its outside, every
    /// <paramref name="stepDeg"/> degrees round and every <paramref name="stepMm"/> along.
    /// </summary>
    public static StockMap FromMesh(IReadOnlyList<Tri> mesh, double stepDeg = 2, double stepMm = 0.5)
    {
        if (mesh.Count == 0) throw new ArgumentException("The stock model is empty.");
        double x0 = mesh.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), x1 = mesh.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
        if (!(x1 - x0 > 0.01)) throw new ArgumentException("The stock model has no length along the axis (X).");
        int nA = Math.Max(16, (int)Math.Round(360 / Math.Max(0.25, stepDeg)));
        int nX = Math.Max(2, (int)Math.Ceiling((x1 - x0) / Math.Max(0.05, stepMm)) + 1);
        var angles = Enumerable.Range(0, nA).Select(i => i * 360.0 / nA).ToArray();
        // Sections just inside the ends, so the end faces themselves aren't cut edge-on.
        double inset = Math.Min(0.02, (x1 - x0) / 100);
        var xs = Enumerable.Range(0, nX).Select(i => x0 + (x1 - x0) * i / (nX - 1)).ToArray();
        var axial = Axial(mesh);
        var r = new double[nA, nX];
        var col = new float[nA];
        for (int k = 0; k < nX; k++)
        {
            Outermost(axial, Math.Clamp(xs[k], x0 + inset, x1 - inset), angles, col);
            for (int a = 0; a < nA; a++) r[a, k] = col[a] > 0 ? col[a] : double.NaN;
        }
        return new StockMap(angles, xs, r, true);
    }

    /// <summary>
    /// How far out the stock is at <paramref name="thetaDeg"/> round and <paramref name="x"/> along (mm): 0 past
    /// its ends (there's nothing there), NaN at an angle that wasn't measured.
    /// </summary>
    public double RadiusAt(double thetaDeg, double x)
    {
        if (x < Xs[0] - 1e-9 || x > Xs[^1] + 1e-9) return 0;
        int nX = Xs.Length;
        int k = nX == 1 ? 0 : Math.Clamp(Upper(Xs, x) - 1, 0, nX - 2);
        double fx = nX == 1 ? 0 : Math.Clamp((x - Xs[k]) / Math.Max(1e-12, Xs[k + 1] - Xs[k]), 0, 1);
        int k1 = nX == 1 ? 0 : k + 1;
        if (!AngleCell(Wrap360(thetaDeg), out int a0, out int a1, out double fa)) return double.NaN;
        double r00 = R[a0, k], r01 = R[a0, k1], r10 = R[a1, k], r11 = R[a1, k1];
        double v0 = Mix(r00, r01, fx), v1 = Mix(r10, r11, fx);
        return Mix(v0, v1, fa);
    }

    /// <summary>Between two values, ignoring one that isn't known.</summary>
    static double Mix(double a, double b, double f) => double.IsNaN(a) ? (f > 0.5 || double.IsNaN(b) ? b : double.NaN) : double.IsNaN(b) ? (f < 0.5 ? a : double.NaN) : a + (b - a) * f;

    bool AngleCell(double t, out int a0, out int a1, out double f)
    {
        var A = AnglesDeg;
        int n = A.Length;
        if (n == 1) { a0 = a1 = 0; f = 0; return Math.Abs(Diff(t, A[0])) < 1; }
        int up = Upper(A, t);
        if (up > 0 && up < n)
        {
            a0 = up - 1; a1 = up;
            double span = A[a1] - A[a0];
            f = (t - A[a0]) / Math.Max(1e-12, span);
            // A gap much wider than the steps elsewhere wasn't measured (a scan of part of a turn).
            return WholeTurn || span <= 2.5 * MeanStep;
        }
        // Past the last angle or before the first: across the seam.
        a0 = n - 1; a1 = 0;
        double gap = A[0] + 360 - A[n - 1];
        double from = t >= A[n - 1] ? t - A[n - 1] : t + 360 - A[n - 1];
        f = from / Math.Max(1e-12, gap);
        if (WholeTurn || gap <= 2.5 * MeanStep) return true;
        // Not round the seam: only right at the ends.
        if (from <= 1e-6) { f = 0; return true; }
        if (gap - from <= 1e-6) { f = 1; return true; }
        return false;
    }

    double MeanStep => AnglesDeg.Length > 1 ? (AnglesDeg[^1] - AnglesDeg[0]) / (AnglesDeg.Length - 1) : 360;

    /// <summary>The first index whose value is above <paramref name="v"/>.</summary>
    static int Upper(double[] a, double v)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int m = (lo + hi) / 2;
            if (a[m] <= v) lo = m + 1; else hi = m;
        }
        return lo;
    }

    internal static double Wrap360(double d)
    {
        d %= 360;
        return d < 0 ? d + 360 : d;
    }

    static double Diff(double a, double b) => ((a - b) % 360 + 540) % 360 - 180;

    /// <summary>The model turned so the rotary's axis (X) is the slicer's Z: (y, z, x).</summary>
    internal static List<Tri> Axial(IReadOnlyList<Tri> mesh) =>
        [.. mesh.Select(t => new Tri(new V3(t.A.Y, t.A.Z, t.A.X), new V3(t.B.Y, t.B.Z, t.B.X), new V3(t.C.Y, t.C.Z, t.C.X)))];

    /// <summary>
    /// The outside of a model (<see cref="Axial"/>) where it crosses <paramref name="x"/>: at each angle, how far out
    /// the outermost surface is along the ray from the axis (0 where there's none).
    /// </summary>
    internal static void Outermost(List<Tri> axial, double x, double[] anglesDeg, float[] into)
    {
        Array.Clear(into);
        var section = StackSlicer.Section(axial, x);
        if (section.IsEmpty) return;
        Rays(StackSlicer.Loops(section), anglesDeg, into);
    }

    /// <summary>Along each angle's ray from the axis, the furthest edge of <paramref name="loops"/> (in the plane (Y, Z)) crossed.</summary>
    internal static void Rays(List<List<Vec2>> loops, double[] anglesDeg, float[] into)
    {
        int n = anglesDeg.Length;
        bool even = n > 1 && Enumerable.Range(1, n - 1).All(i => Math.Abs(anglesDeg[i] - anglesDeg[0] - i * 360.0 / n) < 1e-6);
        foreach (var loop in loops)
            for (int e = 0; e < loop.Count; e++)
            {
                var p = loop[e];
                var q = loop[(e + 1) % loop.Count];
                if (p.Length < 1e-9 && q.Length < 1e-9) continue;
                // The angle of (y, z) = (−r sin θ, r cos θ) is θ = atan2(−y, z).
                double ap = Math.Atan2(-p.X, p.Y) * 180 / Math.PI, aq = Math.Atan2(-q.X, q.Y) * 180 / Math.PI;
                double d = Diff(aq, ap);
                double lo = Math.Min(ap, ap + d), hi = Math.Max(ap, ap + d);
                if (even)
                {
                    double step = 360.0 / n, a0 = anglesDeg[0];
                    for (int jj = (int)Math.Ceiling((lo - a0) / step); jj <= (int)Math.Floor((hi - a0) / step); jj++)
                        Hit(((jj % n) + n) % n);
                }
                else
                    for (int j = 0; j < n; j++)
                    {
                        double t = anglesDeg[j];
                        double off = Diff(t, lo);
                        if (off < 0) off += 360;
                        if (off <= hi - lo + 1e-9) Hit(j);
                    }

                void Hit(int j)
                {
                    double t = anglesDeg[j] * Math.PI / 180;
                    double dy = -Math.Sin(t), dz = Math.Cos(t);
                    double ex = q.X - p.X, ey = q.Y - p.Y;
                    double den = dy * ey - dz * ex;
                    if (Math.Abs(den) < 1e-12) return;
                    double s = (p.X * ey - p.Y * ex) / den;
                    double u = (p.X * dz - p.Y * dy) / den;
                    if (s <= 0 || u < -1e-9 || u > 1 + 1e-9) return;
                    if (s > into[j]) into[j] = (float)s;
                }
            }
    }
}

/// <summary>
/// Where the model goes in the stock: turned about its own middle (X, then Y, then Z, in its own axes), made
/// bigger or smaller, turned round the rotary's axis, and moved. Its middle is the middle of its box.
/// </summary>
public sealed record Placement
{
    public double TurnXDeg { get; init; }
    public double TurnYDeg { get; init; }
    public double TurnZDeg { get; init; }
    /// <summary>Its size (1 = as modelled).</summary>
    public double Scale { get; init; } = 1;
    /// <summary>Turned round the rotary's axis (degrees, the way a design runs down the page).</summary>
    public double SpinDeg { get; init; }
    /// <summary>Where its middle is along the axis (design X, mm).</summary>
    public double AlongMm { get; init; }
    /// <summary>How far its middle is off the axis: towards the rotary's −Y (the side 90° round), and up (towards the laser at the start).</summary>
    public double SideMm { get; init; }
    public double UpMm { get; init; }
}

/// <summary>A model, ready to be placed: its triangles about its middle, turned by the placement's own turns.</summary>
public sealed class TargetModel
{
    public IReadOnlyList<Tri> Mesh { get; }
    /// <summary>The middle of its box, as modelled.</summary>
    public V3 Middle { get; }
    /// <summary>Its size as modelled (mm).</summary>
    public V3 Size { get; }

    public TargetModel(IReadOnlyList<Tri> mesh)
    {
        if (mesh.Count == 0) throw new ArgumentException("The model has no triangles.");
        Mesh = mesh;
        var pts = mesh.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y), z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z);
        Middle = new V3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        Size = new V3(x1 - x0, y1 - y0, z1 - z0);
    }

    /// <summary>A starting placement: its longest side along the axis, in the middle of the stock, as modelled in size.</summary>
    public Placement StartIn(StockMap? stock)
    {
        // Model X already runs along the axis; a model longest up (Z) or front to back (Y) is laid along it.
        var p = new Placement();
        if (Size.Z >= Size.X && Size.Z >= Size.Y) p = p with { TurnYDeg = 90 };
        else if (Size.Y > Size.X) p = p with { TurnZDeg = 90 };
        return p with { AlongMm = stock is null ? 0 : (stock.MinX + stock.MaxX) / 2 };
    }

    /// <summary>The model's own turn (as a 3 × 3 matrix, rows), X then Y then Z.</summary>
    internal static double[] OwnTurn(Placement p)
    {
        double ax = p.TurnXDeg * Math.PI / 180, ay = p.TurnYDeg * Math.PI / 180, az = p.TurnZDeg * Math.PI / 180;
        double cx = Math.Cos(ax), sx = Math.Sin(ax), cy = Math.Cos(ay), sy = Math.Sin(ay), cz = Math.Cos(az), sz = Math.Sin(az);
        // Rz · Ry · Rx
        return
        [
            cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx,
            sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx,
            -sy, cy * sx, cy * cx,
        ];
    }

    /// <summary>A point of the model, about its middle and turned by <paramref name="m"/> (<see cref="OwnTurn"/>).</summary>
    internal V3 Turned(V3 v, double[] m)
    {
        double x = v.X - Middle.X, y = v.Y - Middle.Y, z = v.Z - Middle.Z;
        return new V3(m[0] * x + m[1] * y + m[2] * z, m[3] * x + m[4] * y + m[5] * z, m[6] * x + m[7] * y + m[8] * z);
    }

    /// <summary>A turned point (<see cref="Turned"/>) placed in the rotary's frame.</summary>
    internal static V3 Place(V3 q, double scale, double spinRad, double along, double side, double up)
    {
        double y = q.Y * scale, z = q.Z * scale;
        double c = Math.Cos(spinRad), s = Math.Sin(spinRad);
        return new V3(q.X * scale + along, y * c - z * s - side, y * s + z * c + up);
    }

    /// <summary>The model where the placement puts it, in the rotary's frame.</summary>
    public List<Tri> Placed(Placement p)
    {
        var m = OwnTurn(p);
        double spin = p.SpinDeg * Math.PI / 180;
        V3 P(V3 v) => Place(Turned(v, m), p.Scale, spin, p.AlongMm, p.SideMm, p.UpMm);
        return [.. Mesh.Select(t => new Tri(P(t.A), P(t.B), P(t.C)))];
    }
}

/// <summary>How the placed model compares with the stock.</summary>
/// <param name="StickOut">For each triangle of the placed model, how far it sticks out of the stock (mm, the most at its corners and middle); 0 or less = inside; NaN = where the stock wasn't measured.</param>
/// <param name="WorstMm">The most it sticks out anywhere (≤ 0: it fits, with that much to spare at the tightest spot).</param>
/// <param name="OutsideMm2">The model's surface that sticks out (mm²).</param>
/// <param name="AreaMm2">The model's whole surface (mm²).</param>
/// <param name="PastEnds">Some of it lies past the ends of the stock.</param>
public sealed record FitReport(float[] StickOut, double WorstMm, double OutsideMm2, double AreaMm2, bool PastEnds)
{
    public bool Fits => WorstMm <= 1e-6;
}

/// <summary>Lining a model up in the stock, and carving the stock down to it on the rotary.</summary>
public static class StockCarve
{
    /// <summary>The key a page's shapes carry (the rotary finish note is on them too, for its job check).</summary>
    public const string NoteKey = "stock-carve";

    /// <summary>The ray angle (degrees) of a point in the rotary's frame.</summary>
    internal static double AngleOf(double y, double z) => StockMap.Wrap360(Math.Atan2(-y, z) * 180 / Math.PI);

    /// <summary>How far a point sticks out of the stock (mm; NaN where the stock wasn't measured).</summary>
    internal static double StickOut(StockMap stock, V3 p)
    {
        double r = Math.Sqrt(p.Y * p.Y + p.Z * p.Z);
        // Past an end: how far past it (and, if it's wider than the stock there too, how far out from that edge).
        double past = p.X < stock.MinX ? stock.MinX - p.X : p.X > stock.MaxX ? p.X - stock.MaxX : 0;
        double R = stock.RadiusAt(AngleOf(p.Y, p.Z), Math.Clamp(p.X, stock.MinX, stock.MaxX));
        if (double.IsNaN(R)) return double.NaN;
        if (past <= 0) return r - R;
        return r > R ? Math.Sqrt(past * past + (r - R) * (r - R)) : past;
    }

    /// <summary>The placed model (<see cref="TargetModel.Placed"/>) checked against the stock.</summary>
    public static FitReport Compare(StockMap stock, IReadOnlyList<Tri> placed)
    {
        var o = new float[placed.Count];
        double worst = double.NegativeInfinity, outside = 0, area = 0;
        bool past = false;
        for (int i = 0; i < placed.Count; i++)
        {
            var t = placed[i];
            var mid = new V3((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3, (t.A.Z + t.B.Z + t.C.Z) / 3);
            double v = double.NaN;
            foreach (var p in new[] { t.A, t.B, t.C, mid })
            {
                if (p.X < stock.MinX - 1e-6 || p.X > stock.MaxX + 1e-6) past = true;
                double s = StickOut(stock, p);
                if (!double.IsNaN(s) && (double.IsNaN(v) || s > v)) v = s;
            }
            o[i] = (float)v;
            double a = TriArea(t);
            area += a;
            if (double.IsNaN(v)) continue;
            worst = Math.Max(worst, v);
            if (v > 0) outside += a;
        }
        return new FitReport(o, double.IsNegativeInfinity(worst) ? double.NaN : worst, outside, area, past);
    }

    static double TriArea(Tri t)
    {
        double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        return Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2;
    }

    /// <summary>
    /// Points over the model's surface for fitting (its corners, and more on big triangles), turned by its own turn,
    /// at most <paramref name="max"/> of them.
    /// </summary>
    internal static V3[] Samples(TargetModel model, Placement p, int max = 4000)
    {
        var m = TargetModel.OwnTurn(p);
        var pts = new List<V3>();
        var seen = new HashSet<(long, long, long)>();
        double total = model.Mesh.Sum(TriArea);
        double spacing = Math.Sqrt(Math.Max(1e-6, total) / Math.Max(1, max / 2));
        foreach (var t in model.Mesh)
        {
            foreach (var v in new[] { t.A, t.B, t.C })
                if (seen.Add(((long)Math.Round(v.X * 100), (long)Math.Round(v.Y * 100), (long)Math.Round(v.Z * 100)))) pts.Add(v);
            // Big triangles: a grid of points over them, so a flat face can't bulge out between its corners unseen.
            double longest = Math.Max(Dist(t.A, t.B), Math.Max(Dist(t.B, t.C), Dist(t.C, t.A)));
            int n = Math.Min(12, (int)(longest / spacing));
            for (int i = 1; i < n; i++)
                for (int j = 1; i + j < n; j++)
                {
                    double a = (double)i / n, b = (double)j / n, c = 1 - a - b;
                    pts.Add(new V3(t.A.X * c + t.B.X * a + t.C.X * b, t.A.Y * c + t.B.Y * a + t.C.Y * b, t.A.Z * c + t.B.Z * a + t.C.Z * b));
                }
        }
        if (pts.Count > max)
        {
            // An even thinning (the same every time).
            double every = (double)pts.Count / max;
            pts = [.. Enumerable.Range(0, max).Select(i => pts[(int)(i * every)])];
        }
        return [.. pts.Select(v => model.Turned(v, m))];
    }

    static double Dist(V3 a, V3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>
    /// The best place for the model in the stock: turned round the axis and moved (along it, and off it) so it
    /// sticks out as little as possible, with the room to spare as even as it can be. With
    /// <paramref name="keepSize"/> off it's also made as big as fits. <paramref name="spareMm"/> is room to leave
    /// between the model and the stock's surface everywhere. The model's own turns are kept.
    /// </summary>
    public static Placement BestFit(StockMap stock, TargetModel model, Placement start, bool keepSize = true, double spareMm = 0, CancellationToken ct = default)
    {
        var pts = Samples(model, start);
        if (pts.Length == 0) return start;
        double reach = Math.Max(1, Math.Max(stock.MaxRadius, stock.LengthMm / 2));

        // How far the model sticks out at its worst (smoothly, so the search can feel its way), + mm past the spare room.
        double Worst(double scale, double spinDeg, double along, double side, double up, double tau)
        {
            double spin = spinDeg * Math.PI / 180;
            double max = double.NegativeInfinity;
            var vals = new double[pts.Length];
            int n = 0;
            foreach (var q in pts)
            {
                var p = TargetModel.Place(q, scale, spin, along, side, up);
                double s = StickOut(stock, p);
                if (double.IsNaN(s)) continue;
                s += spareMm;
                vals[n++] = s;
                if (s > max) max = s;
            }
            if (n == 0) return 0;
            if (tau <= 0) return max;
            double sum = 0, outside = 0;
            for (int i = 0; i < n; i++)
            {
                sum += Math.Exp((vals[i] - max) / tau);
                if (vals[i] > 0) outside += vals[i] * vals[i];
            }
            // The worst spot, and how much sticks out over all: where some can't be helped (the model longer
            // than the stock), the rest is still brought in.
            return max + tau * Math.Log(sum) + 2 * Math.Sqrt(outside / n);
        }

        Placement Fit(double scale, Placement basis, out double worst, bool everyWay = true)
        {
            // Several starting turns round the axis, as the stock may only fit the model one way round.
            var best = basis;
            double bestF = double.PositiveInfinity;
            var starts = new List<double> { basis.SpinDeg };
            if (everyWay) for (int k = 1; k < 8; k++) starts.Add(basis.SpinDeg + k * 45);
            foreach (var spin0 in starts)
            {
                ct.ThrowIfCancellationRequested();
                double[] x0 = [spin0, basis.AlongMm, basis.SideMm, basis.UpMm];
                double[] steps = [20, Math.Max(1, reach / 6), Math.Max(0.5, reach / 10), Math.Max(0.5, reach / 10)];
                double F(double[] x) => Worst(scale, x[0], x[1], x[2], x[3], 0.25);
                var x = NelderMead(F, x0, steps, 250, ct);
                // Then sharper, from there.
                double G(double[] v) => Worst(scale, v[0], v[1], v[2], v[3], 0.03);
                x = NelderMead(G, x, [3, 0.5, 0.3, 0.3], 200, ct);
                double f = Worst(scale, x[0], x[1], x[2], x[3], 0);
                if (f < bestF - 1e-6)
                {
                    bestF = f;
                    best = basis with { SpinDeg = Wrap180(x[0]), AlongMm = x[1], SideMm = x[2], UpMm = x[3], Scale = scale };
                }
            }
            worst = bestF;
            return best;
        }

        Placement Final(Placement p) => Centred(p, Worst(p.Scale, p.SpinDeg, p.AlongMm, p.SideMm, p.UpMm, 0));

        // Along the axis the room is often the same over a stretch (a model shorter than the stock): the middle of it.
        Placement Centred(Placement p, double w0)
        {
            const double Tol = 0.02, Step = 0.25;
            bool Ok(double along) => Worst(p.Scale, p.SpinDeg, along, p.SideMm, p.UpMm, 0) <= w0 + Tol;
            double lo = p.AlongMm, hi = p.AlongMm, limit = 2 * reach + stock.LengthMm;
            while (hi - p.AlongMm < limit && Ok(hi + Step)) hi += Step;
            while (p.AlongMm - lo < limit && Ok(lo - Step)) lo -= Step;
            double mid = (lo + hi) / 2;
            return Ok(mid) ? p with { AlongMm = mid } : p;
        }

        if (keepSize) return Final(Fit(start.Scale, start, out _));

        // As big as fits: halve the gap between too big and small enough.
        double lo = 0, hi = start.Scale;
        Placement good = start with { Scale = 0 };
        var at = Fit(hi, start, out double w);
        if (w <= 0)
        {
            good = at;
            lo = hi;
            // Grow until it doesn't fit.
            for (int i = 0; i < 12; i++)
            {
                hi = lo * 1.5;
                at = Fit(hi, good, out w, everyWay: false);
                if (w > 0) break;
                good = at;
                lo = hi;
            }
            if (w <= 0) return Final(good);
        }
        else
        {
            // Shrink until it fits.
            for (int i = 0; i < 12 && w > 0; i++)
            {
                hi = hi / 1.5;
                at = Fit(hi, at, out w, everyWay: false);
                if (w <= 0) { good = at; lo = hi; hi = lo * 1.5; break; }
            }
            if (w > 0) return Final(at);
        }
        for (int i = 0; i < 10 && hi - lo > lo * 0.002; i++)
        {
            double mid = (lo + hi) / 2;
            var p = Fit(mid, good, out double wm, everyWay: false);
            if (wm <= 0) { good = p; lo = mid; }
            else hi = mid;
        }
        return Final(good);
    }

    static double Wrap180(double d) => ((d % 360) + 540) % 360 - 180;

    /// <summary>The downhill simplex search: the x near <paramref name="x0"/> where <paramref name="f"/> is least.</summary>
    internal static double[] NelderMead(Func<double[], double> f, double[] x0, double[] steps, int iterations, CancellationToken ct = default)
    {
        int n = x0.Length;
        var pts = new double[n + 1][];
        var val = new double[n + 1];
        pts[0] = (double[])x0.Clone();
        for (int i = 0; i < n; i++)
        {
            pts[i + 1] = (double[])x0.Clone();
            pts[i + 1][i] += steps[i];
        }
        for (int i = 0; i <= n; i++) val[i] = f(pts[i]);
        for (int it = 0; it < iterations; it++)
        {
            if ((it & 31) == 0) ct.ThrowIfCancellationRequested();
            var order = Enumerable.Range(0, n + 1).OrderBy(i => val[i]).ToArray();
            pts = [.. order.Select(i => pts[i])];
            val = [.. order.Select(i => val[i])];
            if (Math.Abs(val[n] - val[0]) < 1e-7 && it > 10) break;
            var c = new double[n];
            for (int i = 0; i < n; i++) for (int d = 0; d < n; d++) c[d] += pts[i][d] / n;
            double[] Along(double k) => [.. Enumerable.Range(0, n).Select(d => c[d] + k * (pts[n][d] - c[d]))];
            var r = Along(-1);
            double fr = f(r);
            if (fr < val[0])
            {
                var e = Along(-2);
                double fe = f(e);
                if (fe < fr) { pts[n] = e; val[n] = fe; } else { pts[n] = r; val[n] = fr; }
            }
            else if (fr < val[n - 1]) { pts[n] = r; val[n] = fr; }
            else
            {
                var k = fr < val[n] ? Along(-0.5) : Along(0.5);
                double fk = f(k);
                if (fk < Math.Min(fr, val[n])) { pts[n] = k; val[n] = fk; }
                else
                    for (int i = 1; i <= n; i++)
                    {
                        for (int d = 0; d < n; d++) pts[i][d] = pts[0][d] + 0.5 * (pts[i][d] - pts[0][d]);
                        val[i] = f(pts[i]);
                    }
            }
        }
        int best = Enumerable.Range(0, n + 1).MinBy(i => val[i]);
        return pts[best];
    }

    /// <summary>
    /// The engraving that carves the stock down to the placed model, turning in the rotary: like a glued stack's
    /// finish (<see cref="RotaryFinish.RotaryFinish.Carve"/>), mapped by angle round the axis and X along it, the passes
    /// split into focus heights.
    /// </summary>
    public static RotaryPlan Plan(StockMap stock, IReadOnlyList<Tri> placed, CarveOptions o, CancellationToken ct = default)
    {
        if (!(o.PassDepthMm > 0)) throw new ArgumentException("The depth a pass must be more than zero.");
        if (!(o.FocusBandMm > 0)) throw new ArgumentException("The focus band must be more than zero.");
        if (placed.Count == 0) throw new ArgumentException("There's no model to carve to.");
        double mx0 = placed.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), mx1 = placed.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
        // Only between the model's ends (the rest of the stock, with the end in the chuck, is left), unless asked.
        double x0 = o.PastEnds ? stock.MinX : Math.Max(stock.MinX, mx0), x1 = o.PastEnds ? stock.MaxX : Math.Min(stock.MaxX, mx1);
        if (!(x1 - x0 > 0.05)) throw new ArgumentException("The model isn't over the stock: move it along the axis.");
        double rMax = stock.MaxRadius;
        if (!(rMax > 0)) throw new ArgumentException("The stock has no size.");
        double dia = Math.Min(Math.Max(1, Math.Ceiling(2 * rMax - 0.05)), Math.Floor(WorkModes.MaxCircumferenceMm / Math.PI));
        double circ = Math.PI * dia, length = x1 - x0;
        double cell = Math.Max(0.05, o.DetailMm);
        cell = Math.Max(cell, Math.Sqrt(circ * length / 1_500_000));
        int nT = Math.Max(16, (int)Math.Ceiling(circ / cell)), nZ = Math.Max(1, (int)Math.Ceiling(length / cell));
        double dz = length / nZ;
        // Row j is θ = (j + ½)/nT of a turn, less half a turn: the middle row faces the laser at the start.
        var angles = Enumerable.Range(0, nT).Select(j => StockMap.Wrap360((j + 0.5) * 360.0 / nT - 180)).ToArray();
        var axial = StockMap.Axial(placed);
        var stockG = new float[nZ * nT];
        var floor = new float[nZ * nT];
        var model = new float[nT];
        double focusR = 0, focusX = (x0 + x1) / 2;
        int j0 = nT / 2;
        for (int k = 0; k < nZ; k++)
        {
            ct.ThrowIfCancellationRequested();
            double x = x0 + (k + 0.5) * dz;
            bool inModel = x >= mx0 && x <= mx1;
            if (inModel) StockMap.Outermost(axial, x, angles, model);
            else Array.Clear(model);
            for (int j = 0; j < nT; j++)
            {
                double R = stock.RadiusAt(angles[j], x);
                if (double.IsNaN(R) || !(R > 0)) continue;
                stockG[j * nZ + k] = (float)R;
                // Past the model's ends (when carving there at all), down to the core kept.
                double target = (model[j] > 0 ? model[j] + o.LeaveMm : 0);
                floor[j * nZ + k] = (float)Math.Max(target, o.CoreMm);
                if ((j == j0 || j == j0 - 1) && R > focusR) { focusR = R; focusX = x; }
            }
        }
        if (focusR <= 0) focusR = rMax;
        var plan = RotaryFinish.RotaryFinish.Carve(new RotaryGrid(nT, nZ, stockG, floor), new RotaryCarve(o.PassDepthMm, o.FocusBandMm, o.MaxDepthMm), rMax, focusR, dia, length, x0, ct);
        return plan with { FocusX = focusX };
    }

    /// <summary>What the plan means, in a few lines.</summary>
    public static string Describe(RotaryPlan p, CarveOptions o)
    {
        var s = $"Set Laser → Work mode → Rotary to Object Ø {p.DiameterMm:0} mm (the stock swings {p.WidestMm:0.#} mm across). " +
                $"Focus on the top of the part at X {p.FocusX:0.#} mm before starting, with the part turned back to where the scan started.";
        if (p.Bands.Count == 0) return s + " Nothing needs taking off: the stock is already the model's shape there.";
        s += $" Up to {p.DeepestMm:0.##} mm comes off, in up to {p.MostPasses} pass{(p.MostPasses == 1 ? "" : "es")}, at {p.Bands.Count} focus height{(p.Bands.Count == 1 ? "" : "s")} {o.FocusBandMm:0.##} mm apart.";
        if (p.TooDeepMm2 > 1) s += $" {p.TooDeepMm2 / 100:0.#} cm² needs more than {o.MaxDepthMm:0.#} mm off: that's left (sand it, or raise Deepest).";
        return s;
    }

    /// <summary>The engraving as a page of its own: a layer per focus height (the rotary finish's page, with this add-on's note too).</summary>
    public static DesignPage Page(RotaryPlan plan, Layer engrave, string name)
    {
        var page = RotaryPage.Make(plan, engrave, name);
        return page with { Shapes = [.. page.Shapes.Select(s => s.WithAddonData(NoteKey, "1"))] };
    }
}

/// <summary>How the stock is carved down to the model.</summary>
public sealed record CarveOptions
{
    /// <summary>How deep one engrave pass goes on a flat piece (mm), from the etch rate test.</summary>
    public double PassDepthMm { get; init; } = 0.1;
    /// <summary>How far the surface may be from the focus (mm): each band this deep gets its own focus height.</summary>
    public double FocusBandMm { get; init; } = 1;
    /// <summary>Never engraves deeper than this (mm).</summary>
    public double MaxDepthMm { get; init; } = 5;
    /// <summary>Left on over the model (mm), to sand off.</summary>
    public double LeaveMm { get; init; }
    /// <summary>Never carved closer to the axis than this (mm), so the part stays strong in the chuck.</summary>
    public double CoreMm { get; init; } = 2;
    /// <summary>Carve the stock past the model's ends too (down to the core): off leaves it as it is there.</summary>
    public bool PastEnds { get; init; }
    /// <summary>The size of the map (mm): smaller follows the shape closer, slower to make.</summary>
    public double DetailMm { get; init; } = 0.25;
}
