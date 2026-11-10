using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Signs;

/// <summary>The small motifs that go either side of the first line, and the rule under it, as outlines (mm).</summary>
public static class Motifs
{
    /// <summary>Names for the list, in the order of <see cref="Decoration"/>.</summary>
    public static readonly IReadOnlyList<string> Names = ["None", "Star", "Heart", "Diamond", "Leaf", "Flower", "Dot"];

    /// <summary>
    /// <paramref name="d"/> about <paramref name="size"/> mm high, centred on <paramref name="at"/>; the
    /// right-hand one is <paramref name="mirrored"/> so a leaf points out on both sides.
    /// </summary>
    public static List<Vec2[]> Make(Decoration d, Vec2 at, double size, bool mirrored)
    {
        double r = size / 2;
        Vec2[] Ring(int n, Func<double, (double X, double Y)> f)
        {
            var o = new Vec2[n];
            for (int i = 0; i < n; i++)
            {
                var (x, y) = f(2 * Math.PI * i / n);
                o[i] = new Vec2(at.X + (mirrored ? -x : x) * r, at.Y + y * r);
            }
            return o;
        }
        switch (d)
        {
            case Decoration.Star:
                return [Ring(10, a =>
                {
                    int i = (int)Math.Round(a / (2 * Math.PI) * 10);
                    double rr = i % 2 == 0 ? 1.0 : 0.42, ang = -Math.PI / 2 + i * Math.PI / 5;
                    return (rr * Math.Cos(ang), rr * Math.Sin(ang) + 0.1);
                })];
            case Decoration.Heart:
                // The classic heart curve, lobes up, scaled to about 2 across and 2 high.
                return [Ring(96, t =>
                {
                    double x = 16 * Math.Pow(Math.Sin(t), 3);
                    double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                    return (x / 16.5, -y / 16.5 - 0.1);
                })];
            case Decoration.Diamond:
                return [Ring(4, a => (0.62 * Math.Sin(a), -Math.Cos(a)))];
            case Decoration.Leaf:
                // A pointed leaf leaning out, its tip toward the outside of the sign.
                {
                    var o = new List<Vec2>();
                    const int N = 40;
                    double tilt = -0.5;
                    for (int i = 0; i <= 2 * N; i++)
                    {
                        double t = i <= N ? i / (double)N : (2 * N - i) / (double)N;
                        double w = 0.42 * Math.Sin(Math.PI * t) * (1 - 0.25 * t);
                        double lx = -1 + 2 * t, ly = (i <= N ? -w : w) + 0.12 * Math.Sin(Math.PI * t);
                        double x = lx * Math.Cos(tilt) - ly * Math.Sin(tilt), y = lx * Math.Sin(tilt) + ly * Math.Cos(tilt);
                        o.Add(new Vec2(at.X + (mirrored ? x : -x) * r, at.Y + y * r));
                    }
                    return [[.. o]];
                }
            case Decoration.Flower:
                return [Ring(144, a =>
                {
                    double rr = 0.45 + 0.55 * Math.Abs(Math.Cos(3 * a));
                    return (rr * Math.Cos(a), rr * Math.Sin(a));
                })];
            case Decoration.Dot:
                return [Ring(48, a => (0.55 * Math.Cos(a), 0.55 * Math.Sin(a)))];
            default:
                return [];
        }
    }

    /// <summary>A rule <paramref name="length"/> mm long, tapering to points, with a diamond in its middle.</summary>
    public static List<Vec2[]> Rule(Vec2 at, double length, double cap)
    {
        double half = length / 2, t = Math.Max(0.12 * cap, 0.3) / 2, dw = 0.32 * cap, dh = 0.32 * cap;
        return
        [
            [
                new(at.X - half, at.Y), new(at.X - dw, at.Y - t), new(at.X - dw * 0.6, at.Y - t), new(at.X, at.Y - dh),
                new(at.X + dw * 0.6, at.Y - t), new(at.X + dw, at.Y - t), new(at.X + half, at.Y),
                new(at.X + dw, at.Y + t), new(at.X + dw * 0.6, at.Y + t), new(at.X, at.Y + dh),
                new(at.X - dw * 0.6, at.Y + t), new(at.X - dw, at.Y + t),
            ],
        ];
    }
}
