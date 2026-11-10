namespace PhotonCanvas.Addon.Boxes;

/// <summary>A point or direction in the room the box stands in (mm): X to the right, Y up, Z towards you (the front).</summary>
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
    public V3 Norm() { double l = Length; return l > 0 ? this * (1 / l) : this; }
    public double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
    public static V3 Axis(int axis, double v) => axis == 0 ? new(v, 0, 0) : axis == 1 ? new(0, v, 0) : new(0, 0, v);
}

/// <summary>
/// Where one flat piece sits once the box is put together: its flat (u, v) coordinates run along
/// <see cref="U"/> and <see cref="V"/> from <see cref="O"/>, and its thickness is centred on that
/// plane, half each way along <see cref="N"/>. The face towards +N is the outside of the box (the
/// one up on the laser's bed, where a carving goes).
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

    /// <summary>The same piece turned <paramref name="deg"/> about the line through <paramref name="through"/> along <paramref name="axis"/> (right-handed).</summary>
    public Pose Turned(V3 through, V3 axis, double deg)
    {
        var k = axis.Norm();
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        V3 R(V3 v) => v * c + k.Cross(v) * s + k * (k.Dot(v) * (1 - c));
        return new Pose(through + R(O - through), R(U), R(V));
    }

    public Pose Moved(V3 by) => this with { O = O + by };

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

/// <summary>An upright box in the room, sides along the axes.</summary>
public readonly record struct Box3(V3 Lo, V3 Hi)
{
    public static Box3 Of(IEnumerable<V3> points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var p in points)
        {
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
        }
        return new(new(x0, y0, z0), new(x1, y1, z1));
    }

    public double Size(int axis) => Hi[axis] - Lo[axis];

    /// <summary>Has room in it (more than a hair along every axis).</summary>
    public bool Solid => Size(0) > 1e-6 && Size(1) > 1e-6 && Size(2) > 1e-6;

    public Box3 Intersect(Box3 b) => new(
        new(Math.Max(Lo.X, b.Lo.X), Math.Max(Lo.Y, b.Lo.Y), Math.Max(Lo.Z, b.Lo.Z)),
        new(Math.Min(Hi.X, b.Hi.X), Math.Min(Hi.Y, b.Hi.Y), Math.Min(Hi.Z, b.Hi.Z)));

    /// <summary>The part between <paramref name="lo"/> and <paramref name="hi"/> along one axis.</summary>
    public Box3 Slice(int axis, double lo, double hi) => new(Set(Lo, axis, lo), Set(Hi, axis, hi));

    /// <summary>Grown by <paramref name="by"/> each way along each axis (shrunk when negative).</summary>
    public Box3 Grown(V3 by) => new(Lo - by, Hi + by);

    public IEnumerable<V3> Corners()
    {
        for (int i = 0; i < 8; i++)
            yield return new((i & 1) == 0 ? Lo.X : Hi.X, (i & 2) == 0 ? Lo.Y : Hi.Y, (i & 4) == 0 ? Lo.Z : Hi.Z);
    }

    static V3 Set(V3 v, int axis, double value) => axis == 0 ? v with { X = value } : axis == 1 ? v with { Y = value } : v with { Z = value };
}
