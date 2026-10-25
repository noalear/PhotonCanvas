using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Tracing;
using Xunit;

namespace PhotonCanvas.Tests;

public class DepthOutlinesTests
{
    /// <summary>200 × 100 px at 0.1 mm a pixel: flat and deep, with a raised 8 × 4 mm block in the middle (a tall step) and a soft dome on the right.</summary>
    static (float[] Height, int W, int H) Plateau()
    {
        int w = 200, h = 100;
        var z = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = 0.1f;
                if (x >= 30 && x < 110 && y >= 30 && y < 70) v = 0.9f;
                double d = Math.Sqrt((x - 160) * (x - 160) + (y - 50) * (y - 50));
                if (d < 20) v = Math.Max(v, (float)(0.1 + 0.15 * Math.Cos(d / 20 * Math.PI / 2))); // gentle, no sharp edge
                z[y * w + x] = v;
            }
        return (z, w, h);
    }

    static readonly Vec2 Px = new(0.1, 0.1);

    [Fact]
    public void FindsTheStepRoundARaisedBlock()
    {
        var (z, w, h) = Plateau();
        var edges = DepthOutlines.Find(z, w, h, Px, new DepthOutlineOptions { Amount = 20, MinLengthMm = 1 });
        Assert.NotEmpty(edges.Lines);
        var pts = edges.Lines.SelectMany(l => l).ToList();
        // Every point lies close to the block's outline (3–11 mm across, 3–7 mm down): the dome is too soft to count.
        foreach (var p in pts)
        {
            double off = Math.Min(Math.Min(Math.Abs(p.X - 3), Math.Abs(p.X - 11)), Math.Min(Math.Abs(p.Y - 3), Math.Abs(p.Y - 7)));
            Assert.True(off < 0.5 && p.X < 11.6, $"{p} is off the block's edge");
        }
        // And it goes all the way round: about 24 mm of outline.
        Assert.InRange(edges.LengthMm, 18, 30);
    }

    [Fact]
    public void LaserLinesAreOpenAndRibbonsAreFilledAtTheirWidth()
    {
        var (z, w, h) = Plateau();
        var o = new DepthOutlineOptions { Amount = 20, MinLengthMm = 1, TaperEnds = false };
        var edges = DepthOutlines.Find(z, w, h, Px, o);
        var lines = DepthOutlines.Draw(edges, o with { Width = OutlineWidth.LaserLine });
        Assert.All(lines.Subpaths, s => Assert.False(s.Closed));

        var ribbon = DepthOutlines.Draw(edges, o with { Width = OutlineWidth.Fixed, WidthMm = 0.4 });
        Assert.All(ribbon.Subpaths, s => Assert.True(s.Closed));
        double area = ribbon.Flatten(0.01).Sum(f => Geom.SignedArea(f.Points));
        // Area ≈ length × width (the outer ring minus the hole inside it).
        Assert.InRange(Math.Abs(area) / (edges.LengthMm * 0.4), 0.8, 1.25);
    }

    [Fact]
    public void VaryingLinesStayBetweenThinnestAndWidest()
    {
        var (z, w, h) = Plateau();
        var o = new DepthOutlineOptions { Amount = 20, MinLengthMm = 1, TaperEnds = false, Width = OutlineWidth.Varying, MinWidthMm = 0.1, MaxWidthMm = 0.6 };
        var edges = DepthOutlines.Find(z, w, h, Px, o);
        Assert.All(edges.Weights.SelectMany(x => x), v => Assert.InRange(v, 0, 1));
        double area = Math.Abs(DepthOutlines.Draw(edges, o).Flatten(0.01).Sum(f => Geom.SignedArea(f.Points)));
        Assert.InRange(area / edges.LengthMm, 0.08, 0.7);
    }

    [Fact]
    public void AFlatPictureHasNoEdges()
    {
        var z = Enumerable.Repeat(0.5f, 50 * 50).ToArray();
        var edges = DepthOutlines.Find(z, 50, 50, Px, new DepthOutlineOptions());
        Assert.Empty(edges.Lines);
        Assert.True(DepthOutlines.Draw(edges, new DepthOutlineOptions()).IsEmpty);
    }
}
