using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// 2-D affine transform in SVG order: x' = A·x + C·y + E, y' = B·x + D·y + F.
/// </summary>
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translate(double dx, double dy) => new(1, 0, 0, 1, dx, dy);
    public static Affine Translate(Vec2 d) => Translate(d.X, d.Y);
    public static Affine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Affine ScaleAbout(Vec2 p, double sx, double sy) =>
        Translate(-p.X, -p.Y).Then(Scale(sx, sy)).Then(Translate(p.X, p.Y));

    /// <summary>Rotation by <paramref name="degrees"/> (clockwise on screen, since +Y is down).</summary>
    public static Affine Rotate(double degrees)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new(c, s, -s, c, 0, 0);
    }

    public static Affine RotateAbout(Vec2 p, double degrees) =>
        Translate(-p.X, -p.Y).Then(Rotate(degrees)).Then(Translate(p.X, p.Y));

    public static Affine SkewX(double degrees) => new(1, 0, Math.Tan(degrees * Math.PI / 180), 1, 0, 0);
    public static Affine SkewY(double degrees) => new(1, Math.Tan(degrees * Math.PI / 180), 0, 1, 0, 0);

    /// <summary>Apply this transform first, then <paramref name="next"/>.</summary>
    public Affine Then(Affine next) => new(
        next.A * A + next.C * B,
        next.B * A + next.D * B,
        next.A * C + next.C * D,
        next.B * C + next.D * D,
        next.A * E + next.C * F + next.E,
        next.B * E + next.D * F + next.F);

    public Vec2 Apply(Vec2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    /// <summary>Apply to a direction (no translation).</summary>
    public Vec2 ApplyVector(Vec2 v) => new(A * v.X + C * v.Y, B * v.X + D * v.Y);

    public double Determinant => A * D - B * C;

    public bool IsInvertible => Math.Abs(Determinant) > 1e-12 && double.IsFinite(Determinant);

    public Affine Invert()
    {
        double det = Determinant;
        if (!IsInvertible) throw new InvalidOperationException("transform is not invertible");
        return new(D / det, -B / det, -C / det, A / det, (C * F - D * E) / det, (B * E - A * F) / det);
    }

    /// <summary>Uniform scale factor (geometric mean of the axis scales).</summary>
    public double MeanScale => Math.Sqrt(Math.Abs(Determinant));

    public bool IsIdentity => this == Identity;

    /// <summary>Rotation of the local X axis, degrees clockwise on screen (−180 … 180].</summary>
    public double RotationDeg => Math.Atan2(B, A) * 180 / Math.PI;

    /// <summary>Length of the local X axis (how much the shape is stretched horizontally in its own frame).</summary>
    public double ScaleX => Math.Sqrt(A * A + B * B);

    /// <summary>Local Y scale; negative when the shape is mirrored.</summary>
    public double ScaleY => ScaleX < 1e-12 ? 0 : Determinant / ScaleX;
}
