using Clipper2Lib;
using PhotonCanvas.Addon.Bridges;
using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

/// <summary>The quick inside-the-outline check the put-together tests use gives Clipper's answer everywhere.</summary>
public class OutlineGridTests
{
    static void SameEverywhere(PathsD area, int seed)
    {
        var b = Geo.Bounds(area);
        var rnd = new Random(seed);
        // Enough asks to lay the grid, then points all over (and a little beyond), on a fine lattice too
        // so some land on the grid's own lines, and on and a hair from the outline's corners and edges
        // (where Clipper's rounding to 0.01 mm decides).
        var points = new List<(double u, double v)>();
        foreach (var p in area)
            for (int k = 0; k < p.Count; k++)
            {
                var (a, c) = (p[k], p[(k + 1) % p.Count]);
                points.Add((a.x, a.y));
                for (int n = 0; n < 6; n++)
                {
                    double t = rnd.NextDouble();
                    points.Add((a.x + (c.x - a.x) * t + (rnd.NextDouble() - 0.5) * 0.06, a.y + (c.y - a.y) * t + (rnd.NextDouble() - 0.5) * 0.06));
                }
            }
        for (int n = 0; n < 40000; n++)
            points.Add((b.left - 2 + rnd.NextDouble() * (b.right - b.left + 4), b.top - 2 + rnd.NextDouble() * (b.bottom - b.top + 4)));
        for (double u = b.left - 1; u <= b.right + 1; u += 0.25)
            for (double v = b.top - 1; v <= b.bottom + 1; v += 0.25) points.Add((u, v));
        int inside = 0;
        foreach (var (u, v) in points)
        {
            bool slow = OutlineGrid.Slow(area, u, v);
            Assert.True(slow == OutlineGrid.In(area, u, v), $"different at {u}, {v}");
            if (slow) inside++;
        }
        Assert.InRange(inside, 100, points.Count - 100);
    }

    [Fact]
    public void A_worn_textured_piece_with_holes_gets_the_same_answer_as_asking_Clipper()
    {
        var kit = BridgeKits.Make(Defaults.For(BridgeKind.Rope));
        int seed = 1;
        foreach (var part in kit.Parts.Take(6)) SameEverywhere(Geo.Offset(part.Cut, -0.03), seed++);
    }

    [Fact]
    public void A_long_thin_slanting_shape_gets_the_same_answer_as_asking_Clipper()
    {
        var sliver = new PathsD { new PathD { new(0, 0), new(200, 30), new(200, 31.5), new(0, 1.5) } };
        var square = new PathsD { new PathD { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }, new PathD { new(3, 3), new(3, 7), new(7, 7), new(7, 3) } };
        SameEverywhere(sliver, 7);
        SameEverywhere(square, 8);
    }
}
