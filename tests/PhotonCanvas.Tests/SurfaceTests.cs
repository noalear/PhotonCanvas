using System.Globalization;
using PhotonCanvas.Core;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Curved surfaces: fitting the measured heights, and marking in height bands as xTool Studio does.</summary>
public class SurfaceTests
{
    static List<SurfacePoint> Grid(Func<double, double, double> h, double x0 = 90, double y0 = 90, double size = 40, int n = 7) =>
        [.. from i in Enumerable.Range(0, n) from j in Enumerable.Range(0, n)
            let x = x0 + size * i / (n - 1) let y = y0 + size * j / (n - 1) select new SurfacePoint(new Vec2(x, y), h(x, y))];

    [Fact]
    public void ShapesAreRecoveredFromTheirPoints()
    {
        var plane = Surface.Fit(Grid((x, y) => 10 + 0.05 * x - 0.02 * y), SurfaceShape.Plane);
        Assert.Equal(10 + 0.05 * 100 - 0.02 * 120, plane.HeightAt(new Vec2(100, 120)), 6);

        // A 60 mm tube lying along Y, its axis at x = 110, top 40 mm above the bed.
        double Tube(double x, double y) => 10 + Math.Sqrt(Math.Max(0, 900 - (x - 110) * (x - 110)));
        var cyl = Surface.Fit(Grid(Tube), SurfaceShape.Cylinder);
        Assert.Equal(30, cyl.Radius, 1);
        Assert.Equal(40, cyl.HeightAt(new Vec2(110, 95)), 1);
        Assert.True(cyl.RmsMm < 0.05);

        // Freeform goes through the points (no smoothing) and is close in between.
        var free = Surface.Fit(Grid(Tube), SurfaceShape.Freeform, smoothing: 0);
        Assert.Equal(Tube(90, 90), free.HeightAt(new Vec2(90, 90)), 4);
        Assert.Equal(Tube(113, 101), free.HeightAt(new Vec2(113, 101)), 0);

        // Twelve flat slats round the same axis: the prism follows the flats where a cylinder can't.
        var slats = Surface.FromCylinder(90, -110, 40, 60, sides: 12);
        var pts = Grid((x, y) => slats.HeightAt(new Vec2(x, y)), n: 11);
        var prism = Surface.Fit(pts, SurfaceShape.Prism, sides: 12);
        var round = Surface.Fit(pts, SurfaceShape.Cylinder);
        Assert.True(prism.RmsMm < round.RmsMm);
        Assert.True(prism.RmsMm < 0.1);
    }

    static Job SlopedJob(Func<Vec2, double> height)
    {
        var square = Polyline.Loop((95, 95), (125, 95), (125, 125), (95, 125));
        return new Job(MachineProfile.F2Ultra, [new VectorBlock(square, LaserSettings.Blue(10, 20))]) { SurfaceHeight = height, Created = "test" };
    }

    [Fact]
    public void LinesAreMarkedOneHeightBandAtATime()
    {
        // 115 mm high on the left, falling 5 mm across: like the slatted drum, roughly.
        double H(Vec2 p) => 115 - (p.X - 95) / 30 * 5;
        var gc = SlopedJob(H).Render().Split('\n');
        int head = Array.IndexOf(gc, "G0 X0 Y0");
        Assert.Equal("G102", gc[head + 1]);
        int tail = Array.FindIndex(gc, l => l.EndsWith(" TAIL") && l.StartsWith("# GS004"));
        Assert.Equal("G103", gc[Array.FindLastIndex(gc, l => l.StartsWith("# ") && l.EndsWith("TAIL")) + 1]);
        var zs = gc.Where(l => l.StartsWith("G0Z")).Select(l => double.Parse(l[3..l.IndexOf('F')], CultureInfo.InvariantCulture)).ToList();
        // Highest first, 0.2 mm apart, from the top of the surface (+6, the focus's rule).
        Assert.Equal(121, zs[0], 3);
        Assert.All(zs.Zip(zs.Skip(1)), p => Assert.Equal(0.2, p.First - p.Second, 3));
        Assert.Equal(26, zs.Count);
        // Every mark is at its band's height (within half a band and one sample's slope).
        double z = 0;
        foreach (var l in gc)
        {
            if (l.StartsWith("G0Z")) z = double.Parse(l[3..l.IndexOf('F')], CultureInfo.InvariantCulture);
            if (!l.StartsWith("G1X") && !l.StartsWith("G1Y")) continue;
            var m = System.Text.RegularExpressions.Regex.Match(l, @"X([\d.]+)");
            if (!m.Success) continue;
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.InRange(SurfaceBanding.HeadZFor(115 - (x - 95) / 30 * 5) - z, -0.2, 0.2);
        }
        _ = tail;
    }

    [Fact]
    public void FillsAndPicturesAreBandedToo()
    {
        double H(Vec2 p) => 20 + (p.Y - 95) * 0.1;
        var fill = new FillBlock([[new(95, 95), new(125, 95), new(125, 125), new(95, 125)]], LaserSettings.Blue(10, 400), 50);
        var job = new Job(MachineProfile.F2Ultra, [fill]) { SurfaceHeight = H };
        var gc = job.Render();
        var bandLines = gc.Split('\n').Where(l => l.StartsWith("G1 Z")).ToList();
        Assert.Equal(16, bandLines.Count); // 3 mm of height, 0.2 mm bands
        Assert.Equal(29, double.Parse(bandLines[0].Split(' ')[1][1..], CultureInfo.InvariantCulture), 1);
        // Flat jobs are unchanged.
        Assert.DoesNotContain("G102", new Job(MachineProfile.F2Ultra, [fill]).Render().Split('\n').Take(30));
    }

    [Fact]
    public void TheJobLooksLikeXtoolStudios()
    {
        // The curved outline job exported from xTool Studio (kept out of the repo): same head and tail around the bands.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "curve", "outline.gc");
        if (!File.Exists(path)) return;
        var theirs = File.ReadAllLines(path);
        var ours = SlopedJob(p => 112).Render().Split('\n');
        static string[] Around(string[] g, string from, string to)
        {
            int a = Array.FindIndex(g, l => l.StartsWith(from)), b = Array.FindIndex(g, a + 1, l => l.StartsWith(to));
            return g[a..(b + 1)];
        }
        Assert.Equal(Around(theirs, "# GS004-4 HEAD", "G102"), Around(ours, "# GS004-4 HEAD", "G102"));
        Assert.Equal(theirs.SkipWhile(l => l != "# GS004-4 VECTOR TAIL").Select(l => l.Trim()).Where(l => l.Length > 0),
            ours.SkipWhile(l => l != "# GS004-4 VECTOR TAIL").Select(l => l.Trim()).Where(l => l.Length > 0));
        // Inside the block: absolute Z moves at 600, jumps at full speed, the power and speed on the first mark.
        Assert.Contains(ours, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^G0Z118(\.0+)?F600$"));
        Assert.Contains(ours, l => l.StartsWith("G0X") && l.EndsWith("F180000"));
        Assert.Contains(ours, l => l.EndsWith("S100F1200"));
    }
}
