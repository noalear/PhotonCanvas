namespace PhotonCanvas.Addon.Stairs;

/// <summary>A point or direction in the room (mm): X along the room's length, Y up, Z towards you (the room's width runs along −Z).</summary>
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

    /// <summary>A point given on the room's plan (x along it, y across it, mm from the first corner column's centre) and its height.</summary>
    public static V3 Room(double x, double y, double h) => new(x, h, -y);
}

/// <summary>
/// Where one flat piece sits once everything is put together: its flat (u, v) coordinates run along
/// <see cref="U"/> and <see cref="V"/> from <see cref="O"/>, and its thickness is centred on that
/// plane, half each way along <see cref="N"/>. The face towards +N is the one etched (it's up on the
/// laser's bed).
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

    /// <summary>Lying flat, face up: u along the room's length, v across it (plan coordinates).</summary>
    public static Pose Flat(double x, double y, double h) => new(V3.Room(x, y, h), V3.UnitX, -V3.UnitZ);

    /// <summary>Upright along the room's length: u along it, v up; its face looks along −y (towards you).</summary>
    public static Pose Upright(double x, double y, double h) => new(V3.Room(x, y, h), V3.UnitX, V3.UnitY);

    /// <summary>Lying flat, face up, turned <paramref name="deg"/> anticlockwise on the plan from <see cref="Flat"/>.</summary>
    public static Pose FlatTurned(double x, double y, double h, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        // u = (c, s) on the plan, v = (−s, c): plan (x, y) is room (X, −Z).
        return new(V3.Room(x, y, h), new V3(c, 0, -s), new V3(-s, 0, -c));
    }
}
