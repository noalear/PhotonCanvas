using System.Globalization;
using System.Text.RegularExpressions;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Surfaces (Machine → Surfaces): marks drawn on an object's rectangle wrapped round it, focused on it, and slowed where it slopes away.</summary>
public class MarkSurfaceTests
{
    // A 10 mm pen lying left to right, its top 10 mm up, its top line along Y = 100.
    static MarkSurface Pen => new() { Name = "Pen 1", Centre = new Vec2(110, 100), AxisDeg = 0, LengthMm = 80, DiameterMm = 10, TopMm = 10 };

    static DocState WithShapes(MarkSurfaces surfaces, LayerMode mode, params PathGeometry[] shapes)
    {
        var s = DocState.New();
        s = s with { Layers = [s.Layers[0] with { Mode = mode, Laser = LaserSettings.Blue(20, 100), LinesPerCm = 50 }], Surfaces = surfaces };
        return Ops.AddShapes(s, [.. shapes.Select(g => (Shape)new PathShape { LayerId = s.Layers[0].Id, Geometry = g })]);
    }

    static List<double> Zs(string motion) =>
        [.. motion.Split('\n').Where(l => l.StartsWith("G0Z") || l.StartsWith("G1 Z"))
            .Select(l => double.Parse(Regex.Match(l, @"Z(-?[\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture))];

    static List<Vec2> Burns(string motion) =>
        [.. motion.Split('\n').Select(l => Regex.Match(l, @"^G1X(-?[\d.]+)Y(-?[\d.]+)")).Where(m => m.Success)
            .Select(m => new Vec2(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))];

    [Fact]
    public void TheRectangleIsThePensTopUnrolled()
    {
        var z = Pen.Zone();
        // 60° each way, unrolled: 2 × 5 × π/3 mm across.
        Assert.Equal(10 * Math.PI / 3, z.AcrossMm, 9);
        // A point a quarter of the way round (s = R π/4) lands R sin(π/4) from the top line, seen from above.
        var bed = z.ToBed(new Vec2(120, 100 + 5 * Math.PI / 4));
        Assert.Equal(120, bed.X, 9);
        Assert.Equal(100 + 5 * Math.Sin(Math.PI / 4), bed.Y, 9);
        // The surface there is R cos(45°) above the axis; the axis is 5 mm below the 10 mm top.
        Assert.Equal(5 + 5 * Math.Cos(Math.PI / 4), z.HeightAt(bed), 9);
        Assert.Equal(Math.Cos(Math.PI / 4), z.SpeedAt(bed), 9);
        // And back again.
        var wrap = new SurfaceWrap([z]);
        var back = wrap.ToPage(bed)!.Value;
        Assert.Equal(100 + 5 * Math.PI / 4, back.Y, 9);
        // Beside the marks but inside the rectangle, nothing on the canvas lands there.
        Assert.Null(wrap.ToPage(new Vec2(110, 100 + 5.1)));
        Assert.Equal(new Vec2(10, 10), wrap.ToPage(new Vec2(10, 10)));
        // Without wrapping, the design is dropped straight down: the rectangle is what's seen from above.
        var flatDrop = (Pen with { Wrap = false }).Zone();
        Assert.Equal(2 * 5 * Math.Sin(Math.PI / 3), flatDrop.AcrossMm, 9);
        Assert.Equal(103, flatDrop.ToBed(new Vec2(110, 103)).Y, 9);
    }

    [Fact]
    public void ALineRoundThePenIsWrappedAndFollowedInHeight()
    {
        // A score line across the pen, 8 mm of its surface (±0.8 rad round from the top).
        var s = WithShapes(new MarkSurfaces { Items = [Pen] }, LayerMode.Score, PathGeometry.Line(new Vec2(110, 96), new Vec2(110, 104)));
        var built = JobBuilder.Build(s, new JobOptions());
        var lines = built.Motion.Split('\n');
        Assert.Contains("G102", lines);
        var burns = Burns(built.Motion);
        // Seen from above it spans ±R sin(0.8), not ±4.
        Assert.Equal(100 - 5 * Math.Sin(0.8), burns.Min(p => p.Y), 2);
        Assert.Equal(100 + 5 * Math.Sin(0.8), burns.Max(p => p.Y), 2);
        Assert.All(burns, p => Assert.Equal(110, p.X, 3));
        // The head starts on the top (10 mm + the focus's 6) and steps down to where the line ends.
        var zs = Zs(built.Motion);
        Assert.Equal(16, zs.Max(), 1);
        Assert.Equal(6 + 5 + 5 * Math.Cos(0.8), zs.Min(), 0);
        // Slower on the slopes: the pieces near the ends go at less than the layer's 100 mm/s.
        var feeds = lines.Where(l => l.StartsWith("G1X") && l.Contains('F')).Select(l => long.Parse(Regex.Match(l, @"F(\d+)").Groups[1].Value)).ToList();
        Assert.Contains(6000L, feeds.Select(f => Math.Min(6000L, f + 30)));
        Assert.Contains(feeds, f => f < 6000 * Math.Cos(0.7));
        Assert.Contains(built.Warnings, w => w.Contains("Pen 1") && w.Contains("46°"));
    }

    [Fact]
    public void FillLinesAlongThePenStayEvenlySpacedRoundIt()
    {
        // A fill 6 mm round the pen and 20 mm along it: lines along X (the pen's axis), 0.2 mm apart round the surface.
        var s = WithShapes(new MarkSurfaces { Items = [Pen] }, LayerMode.Fill, PathGeometry.Rect(100, 97, 20, 6));
        var built = JobBuilder.Build(s, new JobOptions());
        var ys = Burns(built.Motion).Select(p => Math.Round(p.Y, 3)).Distinct().Order().ToList();
        // The rows land R sin(s / R) from the top line: closer together seen from above further round.
        double first = ys[1] - ys[0], middle = ys.Zip(ys.Skip(1)).Select(p => p.Second - p.First).Where(d => d > 0.05).Max();
        Assert.True(middle > first + 0.005, $"{first} vs {middle}");
        Assert.True(ys[0] > 97 + 0.05);
        // Lines along it aren't cut up: no warning about running round it.
        Assert.DoesNotContain(built.Warnings, w => w.Contains("run round it"));
        // Turned across it: the warning says which angle runs along.
        var across = s with { Layers = [s.Layers[0] with { FillAngleDeg = 90 }] };
        Assert.Contains(JobBuilder.Build(across, new JobOptions()).Warnings, w => w.Contains("run round it") && w.Contains("0°"));
    }

    [Fact]
    public void EverythingElseNeedsAHeight()
    {
        var both = new[] { PathGeometry.Line(new Vec2(110, 98), new Vec2(110, 102)), PathGeometry.Rect(20, 20, 10, 10) };
        var s = WithShapes(new MarkSurfaces { Items = [Pen] }, LayerMode.Score, both);
        var e = Assert.Throws<JobException>(() => JobBuilder.Build(s, new JobOptions()));
        Assert.Contains("Everything else", e.Message);
        // Set: the square off the pen is marked at its height (3 mm + 6).
        var built = JobBuilder.Build(s with { Surfaces = s.Surfaces with { OtherMm = 3 } }, new JobOptions());
        Assert.Contains(9.0, Zs(built.Motion).Select(z => Math.Round(z, 3)));
        // Nothing on a surface: a flat job as before, whatever is set.
        var off = WithShapes(new MarkSurfaces { Items = [Pen] }, LayerMode.Score, PathGeometry.Rect(20, 20, 10, 10));
        Assert.DoesNotContain("G102", JobBuilder.Build(off, new JobOptions()).Motion.Split('\n'));
    }

    [Fact]
    public void AFlatSurfaceIsMarkedAtItsOwnHeight()
    {
        var lid = new MarkSurface { Name = "Lid", Kind = MarkSurfaceKind.Flat, Centre = new Vec2(60, 60), LengthMm = 40, WidthMm = 30, TopMm = 25 };
        var s = WithShapes(new MarkSurfaces { Items = [lid, Pen], OtherMm = 2 }, LayerMode.Score,
            PathGeometry.Rect(50, 55, 20, 10), PathGeometry.Line(new Vec2(110, 98), new Vec2(110, 102)), PathGeometry.Rect(180, 180, 5, 5));
        var built = JobBuilder.Build(s, new JobOptions());
        var zs = Zs(built.Motion).Select(z => Math.Round(z, 2)).ToHashSet();
        Assert.Contains(31.0, zs); // the lid: 25 + 6
        Assert.Contains(8.0, zs);  // everything else: 2 + 6
        Assert.Contains(16.0, zs); // the pen's top
        // The lid's square is where it was drawn.
        Assert.Contains(new Vec2(70, 55), Burns(built.Motion));
    }

    [Fact]
    public void OverlappingSurfacesAreRefused()
    {
        var other = Pen with { Id = Guid.NewGuid(), Name = "Pen 2", Centre = new Vec2(110, 104) };
        var s = WithShapes(new MarkSurfaces { Items = [Pen, other] }, LayerMode.Score, PathGeometry.Line(new Vec2(110, 98), new Vec2(110, 102)));
        var e = Assert.Throws<JobException>(() => JobBuilder.Build(s, new JobOptions()));
        Assert.Contains("overlap", e.Message);
        // 12 mm apart (two 10 mm pens side by side in a jig): fine.
        JobBuilder.Build(s with { Surfaces = s.Surfaces with { Items = [Pen, other with { Centre = new Vec2(110, 112) }] } }, new JobOptions());
    }

    [Fact]
    public void APictureIsLaidOutWhereItLands()
    {
        // A full picture 10 mm square across the pen, one dot every 0.1 mm.
        var bmp = new Bitmap(100, 100);
        Array.Fill(bmp.Bits, true);
        var d = new DotBlock(bmp, new Vec2(105, 95), LaserSettings.Blue(20, 100), 254, 100, 0, "Threshold");
        var wrap = new SurfaceWrap([Pen.Zone()]);
        var on = (DotBlock)wrap.Apply(d);
        // Rows past R sin(1) from the top line (±4.21 mm) get nothing: the picture is squeezed into what's seen from above.
        var rows = Enumerable.Range(0, 100).Where(r => Enumerable.Range(0, 100).Any(c => on.Bitmap[c, r])).ToList();
        Assert.InRange(rows.Count, 82, 86);
        Assert.Single(wrap.Used);
        // Dots further round are held on longer (less light reaches each mm² of a sloping surface).
        var banded = SurfaceBanding.Apply([on], p => wrap.HeightAt(p) ?? 0, 0.2, wrap.SpeedAt).OfType<DotBlock>().ToList();
        Assert.Equal(100, banded.Min(b => b.DotUs));
        Assert.True(banded.Max(b => b.DotUs) >= 180);
        Assert.All(banded, b => Assert.Equal(0, b.DotUs % 10));
    }

    [Fact]
    public void SurfacesAreKeptWithTheDesign()
    {
        var lid = new MarkSurface { Name = "Lid", Kind = MarkSurfaceKind.Flat, Centre = new Vec2(60, 60), AxisDeg = 90, LengthMm = 40, WidthMm = 30, TopMm = 25, ReachDeg = 70, Wrap = false, EvenOut = false };
        var s = DocState.New() with { Surfaces = new MarkSurfaces { Items = [Pen, lid], OtherMm = 3.5, BandMm = 0.1 } };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(s.Surfaces.Items, back.Surfaces.Items);
        Assert.Equal(3.5, back.Surfaces.OtherMm);
        Assert.Equal(0.1, back.Surfaces.BandMm);
        Assert.DoesNotContain("surfaces", DocFile.Serialize(DocState.New()));
    }
}
