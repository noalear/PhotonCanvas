namespace PhotonCanvas.Core;

/// <summary>One measured (or typed) height of the material's top: mm above the bed, as the focus uses it.</summary>
public readonly record struct SurfacePoint(Vec2 At, double Height);

/// <summary>How the measured points become a surface.</summary>
public enum SurfaceShape
{
    /// <summary>Any shape: a smooth sheet through (or near, with smoothing) every point.</summary>
    Freeform,
    /// <summary>A round object lying on its side (a bottle, a tube): the best cylinder through the points.</summary>
    Cylinder,
    /// <summary>A cylinder made of flat slats (a barrel, a slatted tube): a many-sided prism, each face flat.</summary>
    Prism,
    /// <summary>A flat piece that sits at a slant.</summary>
    Plane,
}

/// <summary>
/// The top of a curved piece as a height field: points measured on it (or a shape typed in) fitted
/// to a surface. Heights are mm above the bed, the same as the focus's material height.
/// </summary>
public sealed class Surface
{
    public SurfaceShape Shape { get; }
    public IReadOnlyList<SurfacePoint> Points { get; }
    /// <summary>Freeform only: 0 = through every point exactly … 1 = very smooth (rides over bad points).</summary>
    public double Smoothing { get; }
    /// <summary>Prism only: how many flat faces round the whole object.</summary>
    public int Sides { get; }

    // Freeform (thin-plate spline).
    readonly double[]? _w;
    readonly double _a0, _ax, _ay;
    readonly Vec2 _centre;
    readonly double _scale = 1;
    // Cylinder and prism: axis at angle Theta through the bed, cross-axis position Uc, centre height Zc, radius R.
    public double AxisDeg { get; }
    public double AxisOffset { get; }
    public double CentreHeight { get; }
    public double Radius { get; }
    /// <summary>Prism: turn of the faces about the axis (radians).</summary>
    public double FacePhase { get; }

    /// <summary>How far the points are from the surface, typically (mm).</summary>
    public double RmsMm { get; }
    /// <summary>Each point's distance from the surface (height − surface), in order.</summary>
    public IReadOnlyList<double> Residuals { get; }

    Surface(SurfaceShape shape, IReadOnlyList<SurfacePoint> points, double smoothing, int sides,
        double[]? w, double a0, double ax, double ay, Vec2 centre, double scale,
        double axisDeg, double axisOffset, double zc, double r, double phase)
    {
        Shape = shape;
        Points = points;
        Smoothing = smoothing;
        Sides = sides;
        _w = w; _a0 = a0; _ax = ax; _ay = ay; _centre = centre; _scale = scale;
        AxisDeg = axisDeg; AxisOffset = axisOffset; CentreHeight = zc; Radius = r; FacePhase = phase;
        var res = points.Select(p => p.Height - HeightAt(p.At)).ToList();
        Residuals = res;
        RmsMm = res.Count == 0 ? 0 : Math.Sqrt(res.Average(e => e * e));
    }

    /// <summary>A cylinder (or prism, measured across its flats) given directly: its axis on the bed, the height of its top, its diameter.</summary>
    public static Surface FromCylinder(double axisDeg, double axisOffset, double topHeight, double diameter, int sides = 0)
    {
        double r = diameter / 2;
        return sides >= 3
            ? new(SurfaceShape.Prism, [], 0, sides, null, 0, 0, 0, default, 1, axisDeg, axisOffset, topHeight - r / Math.Cos(Math.PI / sides) * Math.Cos(Math.PI / sides), r / Math.Cos(Math.PI / sides), Math.PI / sides)
            : new(SurfaceShape.Cylinder, [], 0, 0, null, 0, 0, 0, default, 1, axisDeg, axisOffset, topHeight - r, r, 0);
    }

    /// <summary>Fit the points to a surface of the given shape. Needs 3 points (6 for a cylinder or prism).</summary>
    public static Surface Fit(IReadOnlyList<SurfacePoint> points, SurfaceShape shape, double smoothing = 0.3, int sides = 12)
    {
        if (points.Count < 3) throw new ArgumentException("At least three points are needed.", nameof(points));
        return shape switch
        {
            SurfaceShape.Plane => FitPlane(points),
            SurfaceShape.Cylinder or SurfaceShape.Prism when points.Count >= 6 => FitCylinder(points, shape, Math.Max(3, sides)),
            SurfaceShape.Cylinder or SurfaceShape.Prism => throw new ArgumentException("A cylinder needs at least six points.", nameof(points)),
            _ => FitFreeform(points, Math.Clamp(smoothing, 0, 1)),
        };
    }

    /// <summary>The material's height here (mm above the bed).</summary>
    public double HeightAt(Vec2 p)
    {
        switch (Shape)
        {
            case SurfaceShape.Freeform when _w is { } w:
            {
                var q = (p - _centre) / _scale;
                double h = _a0 + _ax * q.X + _ay * q.Y;
                for (int i = 0; i < Points.Count; i++) h += w[i] * Kernel(q.Dist((Points[i].At - _centre) / _scale));
                return h;
            }
            case SurfaceShape.Cylinder or SurfaceShape.Prism:
            {
                double t = AxisDeg * Math.PI / 180;
                double u = -p.X * Math.Sin(t) + p.Y * Math.Cos(t) - AxisOffset;
                if (Shape == SurfaceShape.Cylinder)
                    return Math.Abs(u) >= Radius ? CentreHeight : CentreHeight + Math.Sqrt(Radius * Radius - u * u);
                return CentreHeight + PrismTop(u, Radius, Sides, FacePhase);
            }
            default:
                return _a0 + _ax * p.X + _ay * p.Y;
        }
    }

    /// <summary>The highest point of a regular prism's cross-section above horizontal offset <paramref name="u"/> from its axis.</summary>
    static double PrismTop(double u, double r, int n, double phase)
    {
        double best = 0;
        bool any = false;
        for (int k = 0; k < n; k++)
        {
            double a0 = phase + 2 * Math.PI * k / n, a1 = phase + 2 * Math.PI * (k + 1) / n;
            double u0 = r * Math.Sin(a0), z0 = r * Math.Cos(a0), u1 = r * Math.Sin(a1), z1 = r * Math.Cos(a1);
            if ((u < Math.Min(u0, u1)) || (u > Math.Max(u0, u1)) || Math.Abs(u1 - u0) < 1e-12) continue;
            double z = z0 + (z1 - z0) * (u - u0) / (u1 - u0);
            if (!any || z > best) { best = z; any = true; }
        }
        return any ? best : 0;
    }

    static double Kernel(double r) => r < 1e-12 ? 0 : r * r * Math.Log(r);

    static Surface FitPlane(IReadOnlyList<SurfacePoint> pts)
    {
        // Least squares h = a + b x + c y.
        var m = new double[3, 3];
        var v = new double[3];
        foreach (var p in pts)
        {
            double[] row = [1, p.At.X, p.At.Y];
            for (int i = 0; i < 3; i++)
            {
                v[i] += row[i] * p.Height;
                for (int j = 0; j < 3; j++) m[i, j] += row[i] * row[j];
            }
        }
        var s = Solve(m, v) ?? [pts.Average(p => p.Height), 0, 0];
        return new(SurfaceShape.Plane, pts, 0, 0, null, s[0], s[1], s[2], default, 1, 0, 0, 0, 0, 0);
    }

    static Surface FitFreeform(IReadOnlyList<SurfacePoint> pts, double smoothing)
    {
        int n = pts.Count;
        var centre = new Vec2(pts.Average(p => p.At.X), pts.Average(p => p.At.Y));
        double scale = Math.Max(1, pts.Max(p => p.At.Dist(centre)));
        var q = pts.Select(p => (p.At - centre) / scale).ToArray();
        // Smoothing as a share of the kernel's typical size: 0 interpolates, 1 is nearly a plane.
        double lambda = smoothing <= 0 ? 0 : Math.Pow(10, -4 + 4 * smoothing);
        int size = n + 3;
        var m = new double[size, size];
        var v = new double[size];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = Kernel(q[i].Dist(q[j]));
            m[i, i] += lambda * n;
            m[i, n] = m[n, i] = 1;
            m[i, n + 1] = m[n + 1, i] = q[i].X;
            m[i, n + 2] = m[n + 2, i] = q[i].Y;
            v[i] = pts[i].Height;
        }
        var s = Solve(m, v);
        if (s is null) return FitPlane(pts);
        return new(SurfaceShape.Freeform, pts, smoothing, 0, s[..n], s[n], s[n + 1], s[n + 2], centre, scale, 0, 0, 0, 0, 0);
    }

    static Surface FitCylinder(IReadOnlyList<SurfacePoint> pts, SurfaceShape shape, int sides)
    {
        // For each axis direction, the cross-section is a circle in (u, h): fitted directly (algebraic fit).
        (double Err, double Deg, double Uc, double Zc, double R) best = (double.PositiveInfinity, 0, 0, 0, 0);
        void Try(double deg)
        {
            double t = deg * Math.PI / 180, sn = Math.Sin(t), cs = Math.Cos(t);
            var m = new double[3, 3];
            var v = new double[3];
            foreach (var p in pts)
            {
                double u = -p.At.X * sn + p.At.Y * cs, h = p.Height;
                double[] row = [u, h, 1];
                double rhs = -(u * u + h * h);
                for (int i = 0; i < 3; i++)
                {
                    v[i] += row[i] * rhs;
                    for (int j = 0; j < 3; j++) m[i, j] += row[i] * row[j];
                }
            }
            if (Solve(m, v) is not { } s) return;
            double uc = -s[0] / 2, zc = -s[1] / 2, r2 = uc * uc + zc * zc - s[2];
            if (!(r2 > 0)) return;
            double r = Math.Sqrt(r2), err = 0;
            foreach (var p in pts)
            {
                double u = -p.At.X * sn + p.At.Y * cs - uc;
                double h = Math.Abs(u) >= r ? zc : zc + Math.Sqrt(r2 - u * u);
                err += (p.Height - h) * (p.Height - h);
            }
            if (err < best.Err) best = (err, deg, uc, zc, r);
        }
        for (double d = 0; d < 180; d += 2) Try(d);
        double coarse = best.Deg;
        for (double d = coarse - 2; d <= coarse + 2; d += 0.1) Try(d);
        if (double.IsInfinity(best.Err)) return FitFreeform(pts, 0.3);
        if (shape == SurfaceShape.Cylinder)
            return new(SurfaceShape.Cylinder, pts, 0, 0, null, 0, 0, 0, default, 1, best.Deg, best.Uc, best.Zc, best.R, 0);
        // A prism round the same axis: the turn of its faces and its size, by search.
        double bestErr = double.PositiveInfinity, bestPhase = 0, bestR = best.R, bestZc = best.Zc;
        double t2 = best.Deg * Math.PI / 180;
        var cross = pts.Select(p => (U: -p.At.X * Math.Sin(t2) + p.At.Y * Math.Cos(t2) - best.Uc, p.Height)).ToList();
        double step = 2 * Math.PI / sides;
        for (double rs = 0.97; rs <= 1.08; rs += 0.005)
        {
            double r = best.R * rs;
            for (double ph = 0; ph < step; ph += step / 90)
            {
                // The best centre height for this size and turn is just the mean offset.
                double off = cross.Average(c => c.Height - PrismTop(c.U, r, sides, ph));
                double err = cross.Sum(c => Math.Pow(c.Height - off - PrismTop(c.U, r, sides, ph), 2));
                if (err < bestErr) { bestErr = err; bestPhase = ph; bestR = r; bestZc = off; }
            }
        }
        return new(SurfaceShape.Prism, pts, 0, sides, null, 0, 0, 0, default, 1, best.Deg, best.Uc, bestZc, bestR, bestPhase);
    }

    /// <summary>Gaussian elimination with partial pivoting; null if singular.</summary>
    static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
            if (Math.Abs(m[piv, c]) < 1e-12) return null;
            if (piv != c)
            {
                for (int k = 0; k < n; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]);
                (x[c], x[piv]) = (x[piv], x[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                if (f == 0) continue;
                for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
        }
        return x;
    }
}
