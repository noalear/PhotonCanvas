namespace PhotonCanvas.Addon.Roofs;

/// <summary>A point or direction in the model (mm): X along the roof's length, Y across it, Z up.</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static readonly V3 UnitX = new(1, 0, 0), UnitY = new(0, 1, 0), UnitZ = new(0, 0, 1);
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public V3 Unit => this * (1 / Math.Max(1e-12, Length));
}

/// <summary>
/// Where one copy of a piece sits in the model: the piece's flat point (u, v) goes to O + U·u + V·v,
/// and the sheet's thickness runs along <see cref="N"/> (U × V) from w = 0 to w = T. The face that's up
/// on the laser's bed (the one etched) is the w = T face.
/// </summary>
public sealed record Pose(V3 O, V3 U, V3 V)
{
    public V3 N => U.Cross(V);
    public V3 At(double u, double v, double w = 0) => O + U * u + V * v + N * w;

    /// <summary>Lying flat, etched face up, its flat (0, 0) at <paramref name="o"/>.</summary>
    public static Pose Flat(V3 o) => new(o, V3.UnitX, V3.UnitY);

    /// <summary>The same pose turned about the upright line through (cx, cy) by <paramref name="deg"/> degrees.</summary>
    public Pose Turned(double deg, double cx = 0, double cy = 0)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        V3 Dir(V3 d) => new(d.X * c - d.Y * s, d.X * s + d.Y * c, d.Z);
        V3 Pt(V3 p) => Dir(new V3(p.X - cx, p.Y - cy, p.Z)) + new V3(cx, cy, 0);
        return new Pose(Pt(O), Dir(U), Dir(V));
    }

    public Pose Moved(V3 by) => this with { O = O + by };
}
