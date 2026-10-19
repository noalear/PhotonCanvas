namespace PhotonCanvas.Doc.Import.Step;

/// <summary>A point or direction in 3D.</summary>
public readonly record struct P3(double X, double Y, double Z)
{
    public static P3 operator +(P3 a, P3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static P3 operator -(P3 a) => new(-a.X, -a.Y, -a.Z);
    public static P3 operator *(P3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static P3 operator *(double k, P3 a) => new(a.X * k, a.Y * k, a.Z * k);
    public static P3 operator /(P3 a, double k) => new(a.X / k, a.Y / k, a.Z / k);
    public double Dot(P3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public P3 Cross(P3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public P3 Unit => Length is var l && l > 1e-300 ? this / l : this;
    public static P3 Lerp(P3 a, P3 b, double t) => a + (b - a) * t;
}

/// <summary>A placement: an origin and three perpendicular unit axes.</summary>
public readonly record struct Frame(P3 O, P3 X, P3 Y, P3 Z)
{
    public static readonly Frame Identity = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));

    /// <summary>From an axis and a reference direction (either may be missing), as STEP builds them.</summary>
    public static Frame Of(P3 o, P3? axis, P3? refDir)
    {
        var z = (axis ?? new P3(0, 0, 1)).Unit;
        var x = refDir ?? (Math.Abs(z.X) < 0.9 ? new P3(1, 0, 0) : new P3(0, 1, 0));
        x = (x - z * x.Dot(z)).Unit;
        if (x.Length < 0.5) x = (Math.Abs(z.X) < 0.9 ? new P3(1, 0, 0) : new P3(0, 1, 0)) is var a ? (a - z * a.Dot(z)).Unit : x;
        return new Frame(o, x, z.Cross(x), z);
    }

    /// <summary>Local coordinates to world.</summary>
    public P3 At(double x, double y, double z) => O + X * x + Y * y + Z * z;
    public P3 Apply(P3 p) => At(p.X, p.Y, p.Z);
    public P3 Turn(P3 d) => X * d.X + Y * d.Y + Z * d.Z;
    /// <summary>World to local coordinates.</summary>
    public P3 Local(P3 p) { var d = p - O; return new(d.Dot(X), d.Dot(Y), d.Dot(Z)); }
}

/// <summary>A rigid placement (rotation and move), plus a scale for units.</summary>
public readonly record struct Xform(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22, double Tx, double Ty, double Tz)
{
    public static readonly Xform Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);

    /// <summary>The frame's local coordinates to world.</summary>
    public static Xform From(Frame f) => new(f.X.X, f.Y.X, f.Z.X, f.X.Y, f.Y.Y, f.Z.Y, f.X.Z, f.Y.Z, f.Z.Z, f.O.X, f.O.Y, f.O.Z);

    public P3 Apply(P3 p) => new(M00 * p.X + M01 * p.Y + M02 * p.Z + Tx, M10 * p.X + M11 * p.Y + M12 * p.Z + Ty, M20 * p.X + M21 * p.Y + M22 * p.Z + Tz);

    /// <summary>First this, then <paramref name="b"/>.</summary>
    public Xform Then(Xform b) => new(
        b.M00 * M00 + b.M01 * M10 + b.M02 * M20, b.M00 * M01 + b.M01 * M11 + b.M02 * M21, b.M00 * M02 + b.M01 * M12 + b.M02 * M22,
        b.M10 * M00 + b.M11 * M10 + b.M12 * M20, b.M10 * M01 + b.M11 * M11 + b.M12 * M21, b.M10 * M02 + b.M11 * M12 + b.M12 * M22,
        b.M20 * M00 + b.M21 * M10 + b.M22 * M20, b.M20 * M01 + b.M21 * M11 + b.M22 * M21, b.M20 * M02 + b.M21 * M12 + b.M22 * M22,
        b.M00 * Tx + b.M01 * Ty + b.M02 * Tz + b.Tx, b.M10 * Tx + b.M11 * Ty + b.M12 * Tz + b.Ty, b.M20 * Tx + b.M21 * Ty + b.M22 * Tz + b.Tz);

    /// <summary>The inverse of a rigid placement (rotation transposed).</summary>
    public Xform InverseRigid()
    {
        double tx = -(M00 * Tx + M10 * Ty + M20 * Tz), ty = -(M01 * Tx + M11 * Ty + M21 * Tz), tz = -(M02 * Tx + M12 * Ty + M22 * Tz);
        return new(M00, M10, M20, M01, M11, M21, M02, M12, M22, tx, ty, tz);
    }

    /// <summary>Does it turn the model inside out (a mirror)?</summary>
    public bool Mirrors => M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20) < 0;
}

// ---- curves -------------------------------------------------------------------------------

/// <summary>A curve in 3D with a parameter.</summary>
public abstract class Curve3
{
    public abstract P3 At(double t);
    /// <summary>The parameter of the point on the curve nearest <paramref name="p"/>.</summary>
    public abstract double Project(P3 p);
    /// <summary>Curves that come round on themselves (circles), and how far round that is.</summary>
    public virtual double Period => 0;
    /// <summary>The parameter range, for curves that have one.</summary>
    public virtual (double T0, double T1) Range => (double.NegativeInfinity, double.PositiveInfinity);
    /// <summary>A straight line: two points are enough.</summary>
    public virtual bool Straight => false;

    /// <summary>Nearest parameter by sampling, then Newton steps (for curves with no formula for it).</summary>
    protected double ProjectNumeric(P3 p, double t0, double t1, int samples)
    {
        double best = t0, bestD = double.MaxValue;
        for (int i = 0; i <= samples; i++)
        {
            double t = t0 + (t1 - t0) * i / samples;
            double d = (At(t) - p).Length;
            if (d < bestD) { bestD = d; best = t; }
        }
        double step = (t1 - t0) / samples;
        // Newton on (C(t) − p)·C'(t) = 0, kept in range.
        double h = Math.Max(1e-9, Math.Abs(t1 - t0) * 1e-6);
        for (int k = 0; k < 30; k++)
        {
            var c = At(best);
            var d1 = (At(best + h) - At(best - h)) / (2 * h);
            var d2 = (At(best + h) - c * 2 + At(best - h)) / (h * h);
            var e = c - p;
            double f = e.Dot(d1), df = d1.Dot(d1) + e.Dot(d2);
            if (Math.Abs(df) < 1e-300) break;
            double next = Math.Clamp(best - f / df, Math.Max(t0, best - step), Math.Min(t1, best + step));
            if (Math.Abs(next - best) < 1e-12 * Math.Max(1, Math.Abs(best))) { best = next; break; }
            best = next;
        }
        return best;
    }
}

public sealed class LineCurve(P3 origin, P3 dir) : Curve3
{
    readonly P3 _d = dir.Unit;
    public override P3 At(double t) => origin + _d * t;
    public override double Project(P3 p) => (p - origin).Dot(_d);
    public override bool Straight => true;
}

public sealed class CircleCurve(Frame f, double r) : Curve3
{
    public Frame Frame => f;
    public double Radius => r;
    public override P3 At(double t) => f.O + f.X * (r * Math.Cos(t)) + f.Y * (r * Math.Sin(t));
    public override double Project(P3 p) { var l = f.Local(p); return Math.Atan2(l.Y, l.X); }
    public override double Period => 2 * Math.PI;
}

public sealed class EllipseCurve(Frame f, double a, double b) : Curve3
{
    public override P3 At(double t) => f.O + f.X * (a * Math.Cos(t)) + f.Y * (b * Math.Sin(t));
    public override double Project(P3 p)
    {
        var l = f.Local(p);
        double t = Math.Atan2(l.Y / b, l.X / a);
        // A few Newton steps from the stretched-circle guess.
        for (int k = 0; k < 8; k++)
        {
            double c = Math.Cos(t), s = Math.Sin(t);
            double ex = a * c - l.X, ey = b * s - l.Y;
            double f1 = -ex * a * s + ey * b * c;
            double f2 = a * a * s * s + b * b * c * c - ex * a * c - ey * b * s;
            if (Math.Abs(f2) < 1e-300) break;
            t -= f1 / f2;
        }
        return t;
    }
    public override double Period => 2 * Math.PI;
}

/// <summary>A B-spline curve (NURBS when it has weights).</summary>
public sealed class BSplineCurve : Curve3
{
    readonly int _p;
    readonly P3[] _pts;
    readonly double[]? _w;
    readonly double[] _knots;
    readonly bool _closed;

    public BSplineCurve(int degree, IReadOnlyList<P3> points, IReadOnlyList<double>? weights, IReadOnlyList<double> knots)
    {
        _p = Math.Max(1, degree);
        _pts = [.. points];
        _w = weights is { Count: > 0 } ? [.. weights] : null;
        _knots = [.. knots];
        if (_knots.Length != _pts.Length + _p + 1) throw new FormatException($"B-spline curve: {_pts.Length} points, degree {_p}, but {_knots.Length} knots.");
        _closed = (At(Range.T0) - At(Range.T1)).Length < 1e-7 * Math.Max(1, Size());
    }

    double Size() => _pts.Max(p => Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))));

    public override (double T0, double T1) Range => (_knots[_p], _knots[_knots.Length - 1 - _p]);
    public override double Period => _closed ? Range.T1 - Range.T0 : 0;
    public int Spans => _knots.Skip(_p).Take(_knots.Length - 2 * _p).Distinct().Count() - 1;

    public override P3 At(double t)
    {
        // A closed curve goes round: wrap the parameter rather than stop at the ends.
        if (_closed && Range is var (t0, t1) && t1 > t0 && (t < t0 || t > t1))
        {
            double per = t1 - t0, r = (t - t0) % per;
            t = t0 + (r < 0 ? r + per : r);
        }
        return BSpline.Eval(_p, _knots, _pts, _w, t);
    }

    public override double Project(P3 p)
    {
        var (t0, t1) = Range;
        return ProjectNumeric(p, t0, t1, Math.Max(16, Spans * 8));
    }
}

/// <summary>A curve used only between two of its parameters (TRIMMED_CURVE's basis is enough for edges).</summary>
public sealed class PolylineCurve : Curve3
{
    readonly P3[] _pts;
    public PolylineCurve(IReadOnlyList<P3> pts) { _pts = [.. pts]; }
    public override (double T0, double T1) Range => (0, _pts.Length - 1);
    public override P3 At(double t)
    {
        int i = Math.Clamp((int)Math.Floor(t), 0, _pts.Length - 2);
        return P3.Lerp(_pts[i], _pts[i + 1], t - i);
    }
    public override double Project(P3 p)
    {
        double best = 0, bd = double.MaxValue;
        for (int i = 0; i + 1 < _pts.Length; i++)
        {
            var d = _pts[i + 1] - _pts[i];
            double l2 = d.Dot(d), s = l2 > 0 ? Math.Clamp((p - _pts[i]).Dot(d) / l2, 0, 1) : 0;
            double dist = (P3.Lerp(_pts[i], _pts[i + 1], s) - p).Length;
            if (dist < bd) { bd = dist; best = i + s; }
        }
        return best;
    }
    /// <summary>Its corners between two parameters (so a polyline edge keeps them exactly).</summary>
    public IEnumerable<double> Corners(double t0, double t1)
    {
        for (int i = (int)Math.Ceiling(Math.Min(t0, t1)); i <= Math.Floor(Math.Max(t0, t1)); i++) yield return i;
    }
}

/// <summary>De Boor evaluation, plain or rational, curves and surfaces.</summary>
public static class BSpline
{
    /// <summary>The knot span holding <paramref name="t"/>: the last i with knots[i] ≤ t &lt; knots[i+1] (clamped into range).</summary>
    public static int Span(int p, double[] knots, int n, double t)
    {
        // n = number of control points.
        if (t >= knots[n]) return n - 1;
        if (t <= knots[p]) return p;
        int lo = p, hi = n;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (t < knots[mid]) hi = mid; else lo = mid;
        }
        return lo;
    }

    /// <summary>The p+1 basis functions that are non-zero at t (span k).</summary>
    public static void Basis(int p, double[] knots, int k, double t, Span<double> n)
    {
        Span<double> left = stackalloc double[p + 1], right = stackalloc double[p + 1];
        n[0] = 1;
        for (int j = 1; j <= p; j++)
        {
            left[j] = t - knots[k + 1 - j];
            right[j] = knots[k + j] - t;
            double saved = 0;
            for (int r = 0; r < j; r++)
            {
                double den = right[r + 1] + left[j - r];
                double temp = den == 0 ? 0 : n[r] / den;
                n[r] = saved + right[r + 1] * temp;
                saved = left[j - r] * temp;
            }
            n[j] = saved;
        }
    }

    public static P3 Eval(int p, double[] knots, P3[] pts, double[]? w, double t)
    {
        int n = pts.Length;
        var (t0, t1) = (knots[p], knots[n]);
        t = Math.Clamp(t, t0, t1);
        int k = Span(p, knots, n, t);
        Span<double> b = stackalloc double[p + 1];
        Basis(p, knots, k, t, b);
        double x = 0, y = 0, z = 0, ww = 0;
        for (int j = 0; j <= p; j++)
        {
            int i = k - p + j;
            double c = b[j] * (w?[i] ?? 1);
            x += c * pts[i].X; y += c * pts[i].Y; z += c * pts[i].Z; ww += c;
        }
        return ww == 0 ? new P3(x, y, z) : new P3(x / ww, y / ww, z / ww);
    }

    /// <summary>The full knot vector from the distinct knots and how many times each repeats.</summary>
    public static double[] Expand(IReadOnlyList<double> knots, IReadOnlyList<double> mults)
    {
        var all = new List<double>();
        for (int i = 0; i < knots.Count; i++)
            for (int m = 0; m < (int)Math.Round(mults[i]); m++) all.Add(knots[i]);
        return [.. all];
    }
}

// ---- surfaces -----------------------------------------------------------------------------

/// <summary>
/// A surface with two parameters. <see cref="Uv"/> finds the parameters of a point on it;
/// the parameters' natural normal (∂u × ∂v) is the surface's own normal, as STEP defines it.
/// </summary>
public abstract class Surface3
{
    public abstract P3 At(double u, double v);
    /// <summary>The parameters of the point nearest <paramref name="p"/>; <paramref name="hint"/> is a nearby answer, if known.</summary>
    public abstract (double U, double V) Uv(P3 p, (double U, double V)? hint);
    public virtual double PeriodU => 0;
    public virtual double PeriodV => 0;
    /// <summary>Flat: no points are needed inside a face.</summary>
    public virtual bool Flat => false;
    /// <summary>Straight along v (cylinders, cones, extrusions): no extra points are needed that way.</summary>
    public virtual bool StraightV => false;
    /// <summary>The v of a point where u means nothing (a cone's tip, a sphere's poles), or null.</summary>
    public virtual IReadOnlyList<double> Poles => [];
    /// <summary>Every (u, v) of a point, on surfaces that pass through themselves (a spindle torus): the caller picks the one that runs on from its neighbours.</summary>
    public virtual IReadOnlyList<(double U, double V)> Candidates(P3 p) => [Uv(p, null)];
    public virtual bool SelfCrossing => false;

    public P3 Normal(double u, double v)
    {
        const double h = 1e-6;
        var su = At(u + h, v) - At(u - h, v);
        var sv = At(u, v + h) - At(u, v - h);
        return su.Cross(sv).Unit;
    }
}

public sealed class PlaneSurface(Frame f) : Surface3
{
    public override P3 At(double u, double v) => f.O + f.X * u + f.Y * v;
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint) { var l = f.Local(p); return (l.X, l.Y); }
    public override bool Flat => true;
}

/// <summary>Cylinder: u is the angle (radians) round the axis, v the distance along it.</summary>
public sealed class CylinderSurface(Frame f, double r) : Surface3
{
    public override P3 At(double u, double v) => f.O + f.X * (r * Math.Cos(u)) + f.Y * (r * Math.Sin(u)) + f.Z * v;
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint) { var l = f.Local(p); return (Math.Atan2(l.Y, l.X), l.Z); }
    public override double PeriodU => 2 * Math.PI;
    public override bool StraightV => true;
}

/// <summary>Cone: u the angle, v the distance along the axis; radius r + v·tan(half-angle).</summary>
public sealed class ConeSurface(Frame f, double r, double halfAngle) : Surface3
{
    readonly double _tan = Math.Tan(halfAngle);
    double R(double v) => r + v * _tan;
    public override P3 At(double u, double v) => f.O + f.X * (R(v) * Math.Cos(u)) + f.Y * (R(v) * Math.Sin(u)) + f.Z * v;
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        var l = f.Local(p);
        // Along the slant, not just the axis, so points slightly off the cone land right.
        double c = Math.Cos(Math.Atan(_tan)), s = Math.Sin(Math.Atan(_tan));
        double rho = Math.Sqrt(l.X * l.X + l.Y * l.Y);
        double along = (rho - r) * s + l.Z * c;
        double v = along * c;
        return (Math.Atan2(l.Y, l.X), v);
    }
    public override double PeriodU => 2 * Math.PI;
    public override bool StraightV => true;
    public override IReadOnlyList<double> Poles => Math.Abs(_tan) > 1e-12 ? [-r / _tan] : [];
}

/// <summary>Sphere: u the longitude, v the latitude (−π/2 to π/2).</summary>
public sealed class SphereSurface(Frame f, double r) : Surface3
{
    public override P3 At(double u, double v)
    {
        double cv = Math.Cos(v);
        return f.O + f.X * (r * cv * Math.Cos(u)) + f.Y * (r * cv * Math.Sin(u)) + f.Z * (r * Math.Sin(v));
    }
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        var l = f.Local(p);
        return (Math.Atan2(l.Y, l.X), Math.Atan2(l.Z, Math.Sqrt(l.X * l.X + l.Y * l.Y)));
    }
    public override double PeriodU => 2 * Math.PI;
    public override IReadOnlyList<double> Poles => [-Math.PI / 2, Math.PI / 2];
}

/// <summary>Torus: u the angle round the axis, v the angle round the tube.</summary>
public sealed class TorusSurface(Frame f, double major, double minor) : Surface3
{
    public override P3 At(double u, double v)
    {
        double rr = major + minor * Math.Cos(v);
        return f.O + f.X * (rr * Math.Cos(u)) + f.Y * (rr * Math.Sin(u)) + f.Z * (minor * Math.Sin(v));
    }
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        var l = f.Local(p);
        double u = Math.Atan2(l.Y, l.X);
        double rho = Math.Sqrt(l.X * l.X + l.Y * l.Y);
        return (u, Math.Atan2(l.Z, rho - major));
    }
    public override double PeriodU => 2 * Math.PI;
    public override double PeriodV => 2 * Math.PI;

    /// <summary>A spindle torus (tube wider than the ring) goes through its axis: there, u means nothing.</summary>
    public override IReadOnlyList<double> Poles => major < minor ? [-Math.Acos(-major / minor), Math.Acos(-major / minor)] : [];

    public override bool SelfCrossing => major < minor;

    public override IReadOnlyList<(double U, double V)> Candidates(P3 p)
    {
        var first = Uv(p, null);
        if (major >= minor) return [first];
        // The inner sheet: the same point reached from the opposite side of the axis.
        var l = f.Local(p);
        double rho = Math.Sqrt(l.X * l.X + l.Y * l.Y);
        var second = (first.U + Math.PI, Math.Atan2(l.Z, -rho - major));
        return (At(second.Item1, second.Item2) - p).Length < 1e-6 * (major + minor) ? [first, second] : [first];
    }
}

/// <summary>A curve swept in a straight line: u along the curve, v along the sweep.</summary>
public sealed class ExtrusionSurface(Curve3 curve, P3 dir) : Surface3
{
    readonly P3 _d = dir.Unit;
    public override P3 At(double u, double v) => curve.At(u) + _d * v;
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        // Along the sweep, every point of the curve's shadow: find the curve parameter on the flattened curve.
        var flat = new Flattened(curve, _d);
        double u = flat.Project(p - _d * p.Dot(_d));
        return (u, (p - curve.At(u)).Dot(_d));
    }
    public override double PeriodU => curve.Period;
    public override bool StraightV => true;
    public override bool Flat => curve.Straight;

    /// <summary>The curve squashed flat along the sweep.</summary>
    sealed class Flattened(Curve3 c, P3 d) : Curve3
    {
        public override P3 At(double t) { var q = c.At(t); return q - d * q.Dot(d); }
        public override double Project(P3 p)
        {
            if (c is CircleCurve or EllipseCurve or LineCurve)
            {
                // Close enough to start from the curve's own answer, then polish.
                double t = c.Project(p);
                return Polish(p, t);
            }
            var (t0, t1) = c.Range;
            return ProjectNumeric(p, t0, t1, c is BSplineCurve b ? Math.Max(16, b.Spans * 8) : 64);
        }
        double Polish(P3 p, double t)
        {
            const double h = 1e-6;
            for (int k = 0; k < 10; k++)
            {
                var e = At(t) - p;
                var d1 = (At(t + h) - At(t - h)) / (2 * h);
                double den = d1.Dot(d1);
                if (den < 1e-300) break;
                double dt = e.Dot(d1) / den;
                t -= dt;
                if (Math.Abs(dt) < 1e-12) break;
            }
            return t;
        }
        public override (double T0, double T1) Range => c.Range;
    }
}

/// <summary>A B-spline surface (NURBS when it has weights).</summary>
public sealed class BSplineSurface : Surface3
{
    readonly int _pu, _pv, _nu, _nv;
    readonly P3[] _pts; // row-major: [i * _nv + j], i along u
    readonly double[]? _w;
    readonly double[] _ku, _kv;
    readonly double _periodU, _periodV;

    public BSplineSurface(int du, int dv, IReadOnlyList<IReadOnlyList<P3>> grid, IReadOnlyList<IReadOnlyList<double>>? weights, double[] ku, double[] kv)
    {
        _pu = Math.Max(1, du);
        _pv = Math.Max(1, dv);
        _nu = grid.Count;
        _nv = grid[0].Count;
        _pts = new P3[_nu * _nv];
        for (int i = 0; i < _nu; i++)
            for (int j = 0; j < _nv; j++) _pts[i * _nv + j] = grid[i][j];
        if (weights is { Count: > 0 })
        {
            _w = new double[_nu * _nv];
            for (int i = 0; i < _nu; i++)
                for (int j = 0; j < _nv; j++) _w[i * _nv + j] = weights[i][j];
        }
        _ku = ku;
        _kv = kv;
        if (_ku.Length != _nu + _pu + 1 || _kv.Length != _nv + _pv + 1) throw new FormatException("B-spline surface: knots don't match the control points.");
        // Closed if its two opposite edges meet (then a face may run round the join).
        double size = _pts.Max(p => Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)))) + 1;
        var (u0, u1, v0, v1) = Bounds;
        bool ClosedU() => Enumerable.Range(0, 9).All(k => (At(u0, v0 + (v1 - v0) * k / 8) - At(u1, v0 + (v1 - v0) * k / 8)).Length < 1e-6 * size);
        bool ClosedV() => Enumerable.Range(0, 9).All(k => (At(u0 + (u1 - u0) * k / 8, v0) - At(u0 + (u1 - u0) * k / 8, v1)).Length < 1e-6 * size);
        _periodU = ClosedU() ? u1 - u0 : 0;
        _periodV = ClosedV() ? v1 - v0 : 0;
    }

    public (double U0, double U1, double V0, double V1) Bounds => (_ku[_pu], _ku[_nu], _kv[_pv], _kv[_nv]);
    public override double PeriodU => _periodU;
    public override double PeriodV => _periodV;

    public override P3 At(double u, double v)
    {
        var (u0, u1, v0, v1) = Bounds;
        // Wrapped into range on closed surfaces, clamped on open ones.
        if (_periodU > 0) u = u0 + Mod(u - u0, _periodU); else u = Math.Clamp(u, u0, u1);
        if (_periodV > 0) v = v0 + Mod(v - v0, _periodV); else v = Math.Clamp(v, v0, v1);
        int ku = BSpline.Span(_pu, _ku, _nu, u), kv = BSpline.Span(_pv, _kv, _nv, v);
        Span<double> bu = stackalloc double[_pu + 1], bv = stackalloc double[_pv + 1];
        BSpline.Basis(_pu, _ku, ku, u, bu);
        BSpline.Basis(_pv, _kv, kv, v, bv);
        double x = 0, y = 0, z = 0, w = 0;
        for (int a = 0; a <= _pu; a++)
        {
            int i = ku - _pu + a;
            for (int b = 0; b <= _pv; b++)
            {
                int j = kv - _pv + b;
                double c = bu[a] * bv[b] * (_w?[i * _nv + j] ?? 1);
                var p = _pts[i * _nv + j];
                x += c * p.X; y += c * p.Y; z += c * p.Z; w += c;
            }
        }
        return w == 0 ? new P3(x, y, z) : new P3(x / w, y / w, z / w);
    }

    static double Mod(double a, double m) { double r = a % m; return r < 0 ? r + m : r; }

    (double U, double V)[]? _seeds;
    P3[]? _seedPts;

    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        var (u0, u1, v0, v1) = Bounds;
        if (hint is { } h && (At(h.U, h.V) - p).Length < 1e-9) return h;
        // From the hint first (the last point of an outline is usually close by)…
        (double U, double V)? fromHint = hint is { } hh ? Newton(p, hh) : null;
        double size = Size();
        if (fromHint is { } fh && (At(fh.U, fh.V) - p).Length < 1e-7 * size) return fh;
        // …else from the nearest few of a grid of samples: on a long twisting surface (a screw thread)
        // the nearest sample can be on the wrong turn, and Newton stops in a dip that isn't the point.
        if (_seeds is null)
        {
            int nu = Math.Clamp((_nu - _pu + 1) * 6, 12, 240), nv = Math.Clamp((_nv - _pv + 1) * 6, 12, 240);
            _seeds = new (double, double)[(nu + 1) * (nv + 1)];
            _seedPts = new P3[_seeds.Length];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    var s = (u0 + (u1 - u0) * i / nu, v0 + (v1 - v0) * j / nv);
                    _seeds[i * (nv + 1) + j] = s;
                    _seedPts[i * (nv + 1) + j] = At(s.Item1, s.Item2);
                }
        }
        // The 6 nearest samples.
        Span<int> near = stackalloc int[6];
        Span<double> nd = stackalloc double[6];
        nd.Fill(double.MaxValue);
        near.Fill(0);
        for (int i = 0; i < _seeds.Length; i++)
        {
            var q = _seedPts![i] - p;
            double d = q.Dot(q);
            if (d >= nd[5]) continue;
            int k = 5;
            while (k > 0 && nd[k - 1] > d) { nd[k] = nd[k - 1]; near[k] = near[k - 1]; k--; }
            nd[k] = d;
            near[k] = i;
        }
        (double U, double V) best = fromHint ?? _seeds[near[0]];
        double bestD = (At(best.U, best.V) - p).Length;
        for (int k = 0; k < 6 && nd[k] < double.MaxValue; k++)
        {
            var r = Newton(p, _seeds[near[k]]);
            double d = (At(r.U, r.V) - p).Length;
            if (d < bestD) { bestD = d; best = r; }
            if (bestD < 1e-7 * size) break;
        }
        return best;
    }

    double _size;
    double Size() => _size > 0 ? _size : _size = _pts.Max(q => Math.Max(Math.Abs(q.X), Math.Max(Math.Abs(q.Y), Math.Abs(q.Z)))) + 1;

    (double U, double V) Newton(P3 p, (double U, double V) s)
    {
        var (u0, u1, v0, v1) = Bounds;
        double u = s.U, v = s.V;
        double hu = (u1 - u0) * 1e-6, hv = (v1 - v0) * 1e-6;
        for (int k = 0; k < 40; k++)
        {
            var c = At(u, v);
            var su = (At(u + hu, v) - At(u - hu, v)) / (2 * hu);
            var sv = (At(u, v + hv) - At(u, v - hv)) / (2 * hv);
            var e = c - p;
            // Gauss–Newton: solve [su·su su·sv; su·sv sv·sv] d = −[e·su; e·sv].
            double a = su.Dot(su), b = su.Dot(sv), d = sv.Dot(sv), r1 = -e.Dot(su), r2 = -e.Dot(sv);
            double det = a * d - b * b;
            if (Math.Abs(det) < 1e-300) break;
            double du = (r1 * d - r2 * b) / det, dv = (a * r2 - b * r1) / det;
            double nu = u + du, nv = v + dv;
            if (_periodU == 0) nu = Math.Clamp(nu, u0, u1);
            if (_periodV == 0) nv = Math.Clamp(nv, v0, v1);
            bool done = Math.Abs(nu - u) < 1e-12 * (u1 - u0 + 1) && Math.Abs(nv - v) < 1e-12 * (v1 - v0 + 1);
            u = nu; v = nv;
            if (done) break;
        }
        return (u, v);
    }
}
