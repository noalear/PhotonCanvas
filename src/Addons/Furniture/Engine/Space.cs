namespace PhotonCanvas.Addon.Furniture;

/// <summary>A point or direction in the room the furniture stands in (mm): X to the right, Y up, Z towards you.</summary>
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
}

/// <summary>
/// Where one flat piece sits once the furniture is put together: its flat (u, v) coordinates run
/// along <see cref="U"/> and <see cref="V"/> from <see cref="O"/>, and its thickness is centred on
/// that plane, half each way along <see cref="N"/>. The face towards +N is the one etched (it's up
/// on the laser's bed).
/// </summary>
public sealed record Pose(V3 O, V3 U, V3 V)
{
    public V3 N => U.Cross(V);
    public V3 At(double u, double v, double w = 0) => O + U * u + V * v + N * w;

    /// <summary>The flat coordinates of a point in the room, and how far it is off the piece's middle plane.</summary>
    public (double U, double V, double W) Local(V3 p)
    {
        var d = p - O;
        return (d.Dot(U), d.Dot(V), d.Dot(N));
    }

    /// <summary>Upright, facing you (+Z): u to the right, v up.</summary>
    public static Pose Front(double x, double y, double z) => new(new(x, y, z), V3.UnitX, V3.UnitY);
    /// <summary>Upright, facing away (−Z): u to the left, v up.</summary>
    public static Pose Back(double x, double y, double z) => new(new(x, y, z), -V3.UnitX, V3.UnitY);
    /// <summary>Upright, facing left (−X): u towards you, v up.</summary>
    public static Pose Left(double x, double y, double z) => new(new(x, y, z), V3.UnitZ, V3.UnitY);
    /// <summary>Upright, facing right (+X): u away from you, v up.</summary>
    public static Pose Right(double x, double y, double z) => new(new(x, y, z), -V3.UnitZ, V3.UnitY);
    /// <summary>Lying flat, face up (+Y): u to the right, v away from you.</summary>
    public static Pose Up(double x, double y, double z) => new(new(x, y, z), V3.UnitX, -V3.UnitZ);
    /// <summary>Lying flat, face down (−Y): u to the right, v towards you.</summary>
    public static Pose Down(double x, double y, double z) => new(new(x, y, z), V3.UnitX, V3.UnitZ);
}
