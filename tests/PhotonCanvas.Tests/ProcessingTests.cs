using SkiaSharp;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Tests;

public class ShapeGeometryTests
{
    /// <summary>Net filled area (outer outlines and holes have opposite orientation).</summary>
    static double Area(PathGeometry g) =>
        Math.Abs(g.Flatten(0.001).Where(f => f.Closed).Sum(f => Geom.SignedArea(f.Points)));

    static void Near(double expected, double actual, double eps) => Assert.InRange(actual, expected - eps, expected + eps);

    static double DistToRect(Vec2 p, double x, double y, double w, double h)
    {
        Vec2 a = new(x, y), b = new(x + w, y), c = new(x + w, y + h), d = new(x, y + h);
        return new[] { (a, b), (b, c), (c, d), (d, a) }.Min(e => Geom.SegmentDistance(p, e.Item1, e.Item2));
    }

    [Fact]
    public void SharpOutwardOffsetKeepsSquareCorners()
    {
        var g = ShapeGeometry.Offset(PathGeometry.Rect(10, 10, 10, 10), 1, JoinStyle.Sharp, EndStyle.Flat);
        var sub = Assert.Single(g.Subpaths);
        Assert.True(sub.Closed);
        Assert.All(sub.Segs, s => Assert.Equal(SegKind.Line, s.Kind));
        Assert.Equal(4, sub.Segs.Length);
        var b = g.Bounds();
        Assert.Equal(new Rect2(9, 9, 21, 21), b);
        Assert.Contains(new Vec2(9, 9), g.Flatten(0.01)[0].Points);
        Assert.Equal(144, Area(g), 3);
    }

    [Fact]
    public void RoundOffsetFollowsTheTrueArcWithFewCurves()
    {
        const double tol = 0.01;
        var g = ShapeGeometry.Offset(PathGeometry.Rect(10, 10, 10, 10), 1, JoinStyle.Round, EndStyle.Flat, tolerance: tol);
        var sub = Assert.Single(g.Subpaths);
        Assert.InRange(sub.Segs.Length, 8, 24);
        Assert.Equal(4, sub.Segs.Count(s => s.Kind == SegKind.Line));
        Assert.All(g.Flatten(0.0005)[0].Points, p => Assert.InRange(DistToRect(p, 10, 10, 10, 10), 1 - tol, 1 + tol));
        Near(100 + 40 + Math.PI, Area(g), 0.05);
    }

    [Fact]
    public void CircleOffsetStaysACircle()
    {
        var g = ShapeGeometry.Offset(PathGeometry.Ellipse(50, 50, 10, 10), 1, JoinStyle.Round, EndStyle.Flat);
        var sub = Assert.Single(g.Subpaths);
        Assert.InRange(sub.Segs.Length, 2, 16);
        Assert.All(g.Flatten(0.0005)[0].Points, p => Assert.InRange(p.Dist(new(50, 50)), 10.985, 11.015));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    public void RoundOffsetsOfCurvedAndConcaveShapesStayWithinTolerance(double tol)
    {
        var star = new List<Vec2>();
        for (int i = 0; i < 10; i++)
        {
            double a = i * Math.PI / 5, r = i % 2 == 0 ? 20 : 8;
            star.Add(new(50 + r * Math.Cos(a), 50 + r * Math.Sin(a)));
        }
        (PathGeometry Shape, double D)[] cases =
        [
            (PathGeometry.Polyline(star, true), 1.5),
            (PathGeometry.Ellipse(50, 50, 30, 10), 1.5),
            (PathGeometry.Ellipse(50, 50, 30, 10), -2),
            (PathGeometry.RoundRect(10, 10, 40, 20, 5, 5), 0.5),
        ];
        foreach (var (shape, d) in cases)
        {
            var edges = shape.Flatten(0.0002)[0].Points;
            var g = ShapeGeometry.Offset(shape, d, JoinStyle.Round, EndStyle.Flat, tolerance: tol);
            var sub = Assert.Single(g.Subpaths);
            Assert.InRange(sub.Segs.Length, 1, 60);
            foreach (var p in g.Flatten(tol / 10)[0].Points)
            {
                double dist = Enumerable.Range(1, edges.Count - 1).Min(i => Geom.SegmentDistance(p, edges[i - 1], edges[i]));
                Assert.InRange(dist, Math.Abs(d) - tol, Math.Abs(d) + tol);
            }
        }
    }

    [Fact]
    public void InwardOffsetShrinksAndHolesGrow()
    {
        var inner = ShapeGeometry.Offset(PathGeometry.Rect(10, 10, 10, 10), -1, JoinStyle.Sharp, EndStyle.Flat);
        Assert.Equal(new Rect2(11, 11, 19, 19), inner.Bounds());

        // Outer and hole drawn in the same direction: the even-odd rule still makes it a hole.
        var frame = new PathGeometry(PathGeometry.Rect(0, 0, 20, 20).Subpaths.AddRange(PathGeometry.Rect(5, 5, 10, 10).Subpaths));
        var grown = ShapeGeometry.Offset(frame, 1, JoinStyle.Sharp, EndStyle.Flat);
        Assert.Equal(2, grown.Subpaths.Length);
        Assert.Equal(22 * 22 - 8 * 8, Area(grown), 3);
        var shrunk = ShapeGeometry.Offset(frame, -1, JoinStyle.Sharp, EndStyle.Flat);
        Assert.Equal(18 * 18 - 12 * 12, Area(shrunk), 3);
        // Shrinking past the wall thickness leaves nothing.
        Assert.True(ShapeGeometry.Offset(frame, -3, JoinStyle.Sharp, EndStyle.Flat).IsEmpty);
    }

    [Fact]
    public void OpenLinesBecomeOutlinesWithCaps()
    {
        var line = PathGeometry.Line(new(10, 10), new(30, 10));
        Assert.Equal(80, Area(ShapeGeometry.Offset(line, 2, JoinStyle.Round, EndStyle.Flat)), 3);
        Assert.Equal(96, Area(ShapeGeometry.Offset(line, -2, JoinStyle.Round, EndStyle.Square)), 3);
        var round = ShapeGeometry.Offset(line, 2, JoinStyle.Round, EndStyle.Round);
        Near(80 + 4 * Math.PI, Area(round), 0.05);
        Assert.True(Assert.Single(round.Subpaths).Closed);
        var b = round.Bounds(0.001);
        Near(8, b.MinX, 0.01);
        Near(8, b.MinY, 0.01);
        Near(32, b.MaxX, 0.01);
        Near(12, b.MaxY, 0.01);
    }

    [Fact]
    public void BooleansOfTwoOverlappingSquares()
    {
        var a = PathGeometry.Rect(0, 0, 10, 10);
        var b = PathGeometry.Rect(5, 5, 10, 10);
        Assert.Equal(175, Area(ShapeGeometry.Boolean(a, b, BoolOp.Union)), 3);
        Assert.Equal(75, Area(ShapeGeometry.Boolean(a, b, BoolOp.Subtract)), 3);
        Assert.Equal(25, Area(ShapeGeometry.Boolean(a, b, BoolOp.Intersect)), 3);
        Assert.Equal(150, Area(ShapeGeometry.Boolean(a, b, BoolOp.Exclude)), 3);
        Assert.Equal(new Rect2(5, 5, 10, 10), ShapeGeometry.Boolean(a, b, BoolOp.Intersect).Bounds());
        // Open subpaths are ignored.
        Assert.True(ShapeGeometry.Boolean(PathGeometry.Line(new(0, 0), new(5, 5)), b, BoolOp.Intersect).IsEmpty);
    }

    [Fact]
    public void WeldingManyPartsGivesOneOutline()
    {
        var parts = Enumerable.Range(0, 10).Select(i => PathGeometry.Rect(i, 0, 2, 2)).ToList();
        var g = ShapeGeometry.Union(parts);
        var sub = Assert.Single(g.Subpaths);
        Assert.Equal(4, sub.Segs.Length);
        Assert.Equal(22, Area(g), 3);
        // A part's own hole stays open unless another part covers it.
        var ring = new PathGeometry(PathGeometry.Rect(0, 0, 20, 20).Subpaths.AddRange(PathGeometry.Rect(5, 5, 10, 10).Subpaths));
        Assert.Equal(300, Area(ShapeGeometry.Union([ring])), 3);
        Assert.Equal(400, Area(ShapeGeometry.Union([ring, PathGeometry.Rect(4, 4, 12, 12)])), 3);
    }

    [Fact]
    public void DegenerateInputGivesEmptyResults()
    {
        Assert.True(ShapeGeometry.Offset(PathGeometry.Empty, 1, JoinStyle.Round, EndStyle.Round).IsEmpty);
        Assert.True(ShapeGeometry.Offset(PathGeometry.Rect(0, 0, 5, 5), double.NaN, JoinStyle.Round, EndStyle.Round).IsEmpty);
        Assert.True(ShapeGeometry.Offset(PathGeometry.Rect(0, 0, 0, 5), 0, JoinStyle.Sharp, EndStyle.Flat).IsEmpty);
        Assert.True(ShapeGeometry.Offset(PathGeometry.Polyline([new(double.NaN, 0), new(1, 1)], true), 1, JoinStyle.Sharp, EndStyle.Flat).IsEmpty);
        Assert.True(ShapeGeometry.Boolean(PathGeometry.Empty, PathGeometry.Empty, BoolOp.Union).IsEmpty);
        Assert.True(ShapeGeometry.Union([]).IsEmpty);
        Assert.False(ShapeGeometry.Offset(PathGeometry.Rect(0, 0, 5, 5), 1, JoinStyle.Round, EndStyle.Round, miterLimit: double.NaN, tolerance: -1).IsEmpty);
    }
}

public class CutExtrasTests
{
    static PathShape Sq(Guid layer, double x, double y, double s) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    static (DocState S, Layer L) Doc(Func<Layer, Layer> setup, params Func<Guid, Shape>[] shapes)
    {
        var s = DocState.New();
        var l = setup(s.Layers[0]);
        s = s with { Layers = [l] };
        s = Ops.AddShapes(s, shapes.Select(f => f(l.Id)).ToList());
        return (s, l);
    }

    static List<VectorBlock> Vectors(BuiltJob j) => j.Job.Blocks.OfType<VectorBlock>().ToList();

    [Fact]
    public void KerfMovesOutlinesOutAndHolesIn()
    {
        var (s, _) = Doc(l => l with { KerfMm = 0.2 }, id => Sq(id, 10, 10, 50), id => Sq(id, 30, 30, 10));
        var job = JobBuilder.Build(s, new JobOptions());
        var boxes = Vectors(job).Select(v => v.Path.Bounds).ToList();
        Assert.Equal(2, boxes.Count);
        // The hole is cut first (inside its part), shrunk by half the kerf.
        Assert.Equal(30.1, boxes[0].MinX, 6);
        Assert.Equal(39.9, boxes[0].MaxX, 6);
        Assert.Equal(9.9, boxes[1].MinX, 6);
        Assert.Equal(60.1, boxes[1].MaxY, 6);
        Assert.Empty(job.Warnings);

        var (open, _) = Doc(l => l with { KerfMm = 0.2 }, id => new PathShape { LayerId = id, Geometry = PathGeometry.Line(new(10, 10), new(20, 10)) });
        Assert.Contains(JobBuilder.Build(open, new()).Warnings, w => w.Contains("open path"));
    }

    [Fact]
    public void ScoreLayersGetKerfButNoTabs()
    {
        var (s, _) = Doc(l => l with { Mode = LayerMode.Score, KerfMm = 0.2, TabCount = 4 }, id => Sq(id, 10, 10, 20));
        var v = Assert.Single(Vectors(JobBuilder.Build(s, new())));
        Assert.True(v.Path.Closed);
        Assert.Equal(9.9, v.Path.Bounds.MinX, 6);
    }

    [Fact]
    public void TabsLeaveEvenGapsOfTheRightWidth()
    {
        var (s, _) = Doc(l => l with { TabCount = 4, TabWidthMm = 1 }, id => Sq(id, 10, 10, 20));
        var job = JobBuilder.Build(s, new JobOptions());
        var pieces = Vectors(job);
        Assert.Equal(4, pieces.Count);
        Assert.All(pieces, p => Assert.False(p.Path.Closed));
        Assert.Equal(76, pieces.Sum(p => p.Path.Length), 6);
        for (int k = 0; k < 4; k++)
        {
            var end = pieces[k].Path.Points[^1];
            var next = pieces[(k + 1) % 4].Path.Points[0];
            Assert.Equal(1, end.Dist(next), 6);
        }
        // Gaps sit mid-side, half a spacing (10 mm) from the start corner and away from corners.
        Assert.Equal(new Vec2(20.5, 10), pieces[0].Path.Points[0]);
        Assert.Equal(4, job.Steps.Count);
    }

    [Fact]
    public void TabsAreMovedOffCorners()
    {
        // A 10×10 square with 2 tabs: the second centre would land on the corner at (20, 20).
        var (s, _) = Doc(l => l with { TabCount = 2, TabWidthMm = 1 }, id => new PathShape
        {
            LayerId = id,
            Geometry = PathGeometry.Polyline([new(20, 20), new(10, 20), new(10, 10), new(20, 10)], true),
        });
        var pieces = Vectors(JobBuilder.Build(s, new()));
        Assert.Equal(2, pieces.Count);
        var corners = new Vec2[] { new(20, 20), new(10, 20), new(10, 10), new(20, 10) };
        foreach (var p in pieces)
            Assert.DoesNotContain(corners, c => c.Dist(p.Path.Points[0]) < 0.5 || c.Dist(p.Path.Points[^1]) < 0.5);
    }

    [Fact]
    public void ShortOutlinesAreCutWithoutTabs()
    {
        var (s, _) = Doc(l => l with { TabCount = 4, TabWidthMm = 1 }, id => Sq(id, 10, 10, 2), id => Sq(id, 50, 50, 20));
        var job = JobBuilder.Build(s, new());
        Assert.Contains(job.Warnings, w => w.Contains("too short"));
        Assert.Single(Vectors(job), v => v.Path.Closed);
        Assert.Equal(5, Vectors(job).Count);
    }

    [Fact]
    public void OvercutContinuesPastTheStart()
    {
        var (s, _) = Doc(l => l with { OvercutMm = 2 }, id => Sq(id, 10, 10, 20));
        var v = Assert.Single(Vectors(JobBuilder.Build(s, new())));
        Assert.False(v.Path.Closed);
        Assert.Equal(82, v.Path.Length, 6);
        Assert.Equal(new Vec2(10, 10), v.Path.Points[0]);
        Assert.Equal(new Vec2(12, 10), v.Path.Points[^1]);

        // Tabs win over overcut.
        var (t, _) = Doc(l => l with { OvercutMm = 2, TabCount = 2 }, id => Sq(id, 10, 10, 20));
        var tabbed = JobBuilder.Build(t, new());
        Assert.Equal(80 - 2 * 0.5, Vectors(tabbed).Sum(p => p.Path.Length), 6);
        Assert.Contains(tabbed.Warnings, w => w.Contains("no lead-in or overcut"));
    }

    [Fact]
    public void LeadInComesFromOutsideForPartsAndInsideForHoles()
    {
        Vec2[] outer = [new(10, 10), new(60, 10), new(60, 60), new(10, 60)];
        Vec2[] hole = [new(30, 30), new(40, 30), new(40, 40), new(30, 40)];
        // The hole is drawn the other way round, which must not matter.
        var (s, _) = Doc(l => l with { LeadInMm = 2 },
            id => new PathShape { LayerId = id, Geometry = PathGeometry.Polyline(outer, true) },
            id => new PathShape { LayerId = id, Geometry = PathGeometry.Polyline(hole.Reverse().ToArray(), true) });
        var v = Vectors(JobBuilder.Build(s, new()));
        Assert.Equal(2, v.Count);

        var holeCut = v[0].Path;
        Assert.Equal(2, holeCut.Points[0].Dist(holeCut.Points[1]), 6);
        Assert.Equal(new Vec2(30, 40), holeCut.Points[1]);
        Assert.True(Geom.Inside(hole, holeCut.Points[0]));

        var partCut = v[1].Path;
        Assert.Equal(new Vec2(10, 10), partCut.Points[1]);
        Assert.Equal(2, partCut.Points[0].Dist(partCut.Points[1]), 6);
        Assert.False(Geom.Inside(outer, partCut.Points[0]));
        // Square corner: the entry comes in along the diagonal.
        Assert.Equal(partCut.Points[0].X, partCut.Points[0].Y, 9);
        // The whole loop is still cut and closed.
        Assert.Equal(new Vec2(10, 10), partCut.Points[^1]);
        Assert.Equal(200 + 2, partCut.Length, 6);
    }

    [Fact]
    public void ExtrasAreValidatedWithReadableMessages()
    {
        var (s, _) = Doc(l => l with { TabCount = 2, TabWidthMm = 20 }, id => Sq(id, 10, 10, 20));
        var e = Assert.Throws<JobException>(() => JobBuilder.Build(s, new()));
        Assert.Equal("Layer \"Cut\": Tab width must be between 0.1 and 10 mm (got 20 mm)", e.Message);
        var (k, _) = Doc(l => l with { KerfMm = 5 }, id => Sq(id, 10, 10, 20));
        Assert.Contains("Kerf must be between", Assert.Throws<JobException>(() => JobBuilder.Build(k, new())).Message);
        // Unused settings are not checked.
        var (u, _) = Doc(l => l with { TabWidthMm = 20 }, id => Sq(id, 10, 10, 20));
        JobBuilder.Build(u, new());
    }

    [Fact]
    public void DefaultsLeaveCutsUntouched()
    {
        var (s, _) = Doc(l => l, id => Sq(id, 10, 10, 20));
        var v = Assert.Single(Vectors(JobBuilder.Build(s, new())));
        Assert.True(v.Path.Closed);
        Assert.Equal(4, v.Path.Points.Count);
    }
}

public class FillExtrasTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;
    static readonly Vec2[] Square = [new(100, 100), new(120, 100), new(120, 120), new(100, 120)];
    static readonly LaserSettings Laser = LaserSettings.Blue(90, 1250);

    static string Render(FillBlock b) => new Job(M, [b]) { Created = "x" }.Render();

    static List<string> Lines(string g, Func<string, bool> pick) => g.Split('\n').Where(pick).ToList();

    [Fact]
    public void EstimateCountsWhatTheMachineWasTimedDoing()
    {
        // A fill: each line piece costs its burn at the speed, a jump to the next and a little more.
        var one = new FillBlock([Square], LaserSettings.Blue(60, 500), 100);
        var apart = new FillBlock([Square, [.. Square.Select(p => p + new Vec2(60, 0))]], LaserSettings.Blue(60, 500), 100);
        double a = Job.BlockSeconds(one), b = Job.BlockSeconds(apart);
        int pieces = Job.FillLines(apart).Count;
        Assert.Equal(400, pieces);
        // Twice the burning, 200 more pieces, and 200 jumps of about 40 mm at 3000 mm/s (not at the 500 mm/s burn).
        Assert.InRange(b - 2 * a, 200 * 40 / 3000.0 * 0.9, 200 * 40 / 3000.0 * 1.2);

        // A picture: each dot costs its duration plus a step, whatever the speed; blanks are jumped.
        var bmp = new Bitmap(100, 10);
        for (int r = 0; r < 10; r++) { bmp[0, r] = true; bmp[99, r] = true; }
        var dots = new DotBlock(bmp, new(20, 20), LaserSettings.Blue(30, 80), 254, 200, 1, "grayscale");
        Assert.Equal(Job.BlockSeconds(dots), Job.BlockSeconds(dots with { Laser = LaserSettings.Blue(30, 400) }), 9);
        Assert.InRange(Job.BlockSeconds(dots), 20 * 0.00061, 20 * 0.00061 + 10 * 9.8 / 3000 + 1e-6);

        // Every job takes a moment to start.
        Assert.Equal(Job.StartS + Job.BlockSeconds(dots), new Job(M, [dots]).EstimateSeconds(), 9);
    }

    [Fact]
    public void ZeroAngleIsUnchanged()
    {
        var plain = new FillBlock([Square], Laser, 100);
        Assert.Equal(Render(plain), Render(plain with { AngleDeg = 0, OverscanMm = 0 }));
        Assert.Equal(Render(plain), Render(plain with { AngleDeg = 180 }));
        Assert.Equal(Render(plain), Render(plain with { AngleDeg = -360 }));
    }

    [Fact]
    public void NinetyDegreesGivesVerticalLines()
    {
        var b = new FillBlock([Square], Laser, 100) { AngleDeg = 90 };
        var segs = Job.FillLines(b);
        Assert.Equal(200, segs.Count);
        Assert.All(segs, s => Assert.Equal(s.Start.X, s.End.X, 9));
        Assert.All(segs, s => Assert.Equal(20, s.Start.Dist(s.End), 6));
        var g = Render(b);
        var burns = Lines(g, l => l.EndsWith("S900") && l.StartsWith("G1X"));
        Assert.Equal(200, burns.Count);
        Assert.All(burns, l => Assert.Contains("Y", l));
        Assert.Equal("G1X120.000Y120.000S900", burns[0]);
        Assert.Equal("G1X119.900Y100.000S900", burns[1]);
        Assert.Contains(g.Split('\n'), l => l.StartsWith("G1X") && l.Contains('Y') && l.EndsWith("S0"));
    }

    [Fact]
    public void AngledLinesStayInsideTheShape()
    {
        var b = new FillBlock([Square], Laser, 20) { AngleDeg = 30 };
        var segs = Job.FillLines(b);
        Assert.NotEmpty(segs);
        var dir = new Vec2(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6));
        foreach (var s in segs)
        {
            Assert.Equal(1, Math.Abs((s.End - s.Start).Dot(dir)) / s.Start.Dist(s.End), 6);
            foreach (var p in new[] { s.Start, s.End })
                Assert.True(p.X > 100 - 1e-6 && p.X < 120 + 1e-6 && p.Y > 100 - 1e-6 && p.Y < 120 + 1e-6);
        }
        // Burned length equals the area divided by the line spacing (about).
        Assert.InRange(segs.Sum(s => s.Start.Dist(s.End)), 760, 840);
    }

    [Fact]
    public void OverscanAddsLaserOffRunsWithoutChangingBurns()
    {
        var plain = new FillBlock([Square], Laser, 100);
        var over = plain with { OverscanMm = 1 };
        string a = Render(plain), b = Render(over);
        bool Burn(string l) => l.EndsWith("S900");
        Assert.Equal(Lines(a, Burn), Lines(b, Burn));
        Assert.Contains("\"overDist\":1,", b);
        Assert.Contains("G0X99.000Y100.000", b);
        Assert.Contains("G1X100.000S0", b);
        Assert.Contains("G1X121.000S0", b);
        Assert.Contains("G1Y100.100S0", b);
        Assert.Equal(Lines(a, l => l.EndsWith("S0")).Count + 2 * 200, Lines(b, l => l.EndsWith("S0")).Count);
        Assert.True(new Job(M, [over]).EstimateSeconds() > new Job(M, [plain]).EstimateSeconds());

        var angled = plain with { AngleDeg = 45 };
        Assert.Equal(Lines(Render(angled), Burn), Lines(Render(angled with { OverscanMm = 1 }), Burn));
        Assert.Throws<OutsideWorkAreaException>(() => Render(new FillBlock([[new(0, 10), new(10, 10), new(10, 20), new(0, 20)]], Laser, 10) { OverscanMm = 1 }));
    }

    [Fact]
    public void CrosshatchAddsASecondFillAtNinetyDegrees()
    {
        var s = DocState.New();
        var l = Layer.Default(0, LayerMode.Fill) with { Crosshatch = true, FillAngleDeg = 10, LinesPerCm = 20 };
        s = s with { Layers = [l] };
        s = Ops.AddShapes(s, [new PathShape { LayerId = l.Id, Geometry = PathGeometry.Rect(50, 50, 20, 20) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        var fills = job.Job.Blocks.OfType<FillBlock>().ToList();
        Assert.Equal(2, fills.Count);
        Assert.Equal(10, fills[0].AngleDeg);
        Assert.Equal(100, fills[1].AngleDeg);
        int single = Job.FillLines(fills[0]).Count;
        Assert.InRange(Job.FillLines(fills[1]).Count, single - 2, single + 2);
        Assert.Single(job.Steps);
    }
}

public class GrayscaleTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    [Fact]
    public void PowerIsWrittenOnlyWhenItChanges()
    {
        var bmp = new Bitmap(5, 2);
        for (int i = 0; i < bmp.Bits.Length; i++) bmp.Bits[i] = true;
        bmp[3, 1] = false;
        byte[] levels = [255, 255, 0, 128, 128, 128, 128, 255, 9, 9];
        var block = new DotBlock(bmp, new(10, 10), LaserSettings.Blue(90, 80), 254, 200, 10, "grayscale") { Levels = levels };
        var g = new Job(M, [block]).Render();
        Assert.Contains("\"power\":[100,900],\"bitmapMode\":\"grayscale\"", g);
        var dots = g.Split('\n').Where(l => l.StartsWith("G25X") || l.StartsWith("G25Y")).ToList();
        Assert.Equal(
        [
            "G25X10.000Y10.000S900", "G25X10.100", "G25X10.200S100", "G25X10.300S502", "G25X10.400",
            // Row 2 runs right to left; pixel 3 is white and skipped.
            "G25Y10.100S128", "G25X10.200S900", "G25X10.100S502", "G25X10.000",
        ], dots);
    }

    [Fact]
    public void DitheredOutputIgnoresLevelsWhenAbsent()
    {
        var bmp = new Bitmap(2, 1);
        bmp[0, 0] = bmp[1, 0] = true;
        var g = new Job(M, [new DotBlock(bmp, new(10, 10), LaserSettings.Blue(90, 80), 254, 200, 1, "Jarvis")]).Render();
        Assert.Contains("G25X10.000Y10.000S900", g);
        Assert.Contains("G25X10.100\n", g);
        Assert.Throws<JobException>(() => new Job(M, [new DotBlock(bmp, new(10, 10), LaserSettings.Blue(90, 80), 254, 200, 1, "x") { Levels = [1] }]).Render());
    }

    static byte[] Png(int w, int h, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas);
        using var img = surface.Snapshot();
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void GrayscaleImagesBurnDarkerAtHigherPower()
    {
        var s = DocState.New();
        var layer = Layer.Default(0, LayerMode.Image) with { Dpi = 254, ImageMode = ImageEngraveMode.Grayscale, GrayMinPower = 20 };
        s = s with { Layers = [layer] };
        var src = ImageSource.FromEncoded(Png(100, 100, c =>
        {
            c.DrawRect(0, 0, 50, 100, new SKPaint { Color = SKColors.Black });
            c.DrawRect(50, 0, 25, 100, new SKPaint { Color = new SKColor(128, 128, 128) });
        }));
        s = Ops.AddShapes(s, [new ImageShape { LayerId = layer.Id, Source = src, SizeMm = new(10, 10), Transform = Affine.Translate(50, 60) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        var dot = Assert.IsType<DotBlock>(Assert.Single(job.Job.Blocks));
        Assert.NotNull(dot.Levels);
        Assert.Equal("grayscale", dot.Mode);
        Assert.Equal(20, dot.MinPower);
        Assert.True(dot.Bitmap[10, 50] && dot.Bitmap[60, 50]);
        Assert.False(dot.Bitmap[90, 50]); // white: no dot
        Assert.Equal(900, dot.LevelSValue(dot.Levels![50 * 100 + 10]));
        Assert.InRange(dot.LevelSValue(dot.Levels[50 * 100 + 60]), 250, 850);
        Assert.Contains("\"power\":[200,900],\"bitmapMode\":\"grayscale\"", job.Motion);

        var bad = s with { Layers = [layer with { GrayMinPower = 95 }] };
        Assert.Contains("lightest grey", Assert.Throws<JobException>(() => JobBuilder.Build(bad, new())).Message);
    }
}

public class NestingTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    static (DocState S, List<Guid> Ids) Parts(params PathGeometry[] geos)
    {
        var s = DocState.New();
        var shapes = geos.Select(g => (Shape)new PathShape { LayerId = s.Layers[0].Id, Geometry = g }).ToList();
        return (Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToList());
    }

    static void NoOverlaps(IEnumerable<Rect2> boxes, double gap)
    {
        var list = boxes.ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i]; var b = list[j];
                bool apart = a.MaxX + gap <= b.MinX + 0.01 || b.MaxX + gap <= a.MinX + 0.01 || a.MaxY + gap <= b.MinY + 0.01 || b.MaxY + gap <= a.MinY + 0.01;
                Assert.True(apart, $"{a} and {b} are closer than {gap} mm");
            }
    }

    [Fact]
    public void PacksPartsWithGapsInsideTheSheet()
    {
        var (s, ids) = Parts(Enumerable.Range(0, 6).Select(i => PathGeometry.Rect(150 + i, 150, 40, 30)).ToArray());
        var sheet = PathGeometry.Rect(0, 0, 100, 100);
        var (after, report) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = sheet, SpacingMm = 2, MarginMm = 2, RotationStepDeg = 90 }, M);
        Assert.Equal((6, 0), (report.Placed, report.DidNotFit));
        var boxes = ids.Select(id => after.FindShape(id)!.Bounds).ToList();
        Assert.All(boxes, b => Assert.True(b.MinX >= 2 - 0.01 && b.MinY >= 2 - 0.01 && b.MaxX <= 98.01 && b.MaxY <= 98.01, $"{b} outside the margin"));
        NoOverlaps(boxes, 2);
    }

    [Fact]
    public void StartsInTheMiddleAndWorksOutward()
    {
        var (s, ids) = Parts(Enumerable.Range(0, 4).Select(i => PathGeometry.Rect(300 + i, 300, 20, 20)).ToArray());
        var sheet = PathGeometry.Rect(0, 0, 200, 200);
        var (after, report) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = sheet, SpacingMm = 2, MarginMm = 2 }, M);
        Assert.Equal((4, 0), (report.Placed, report.DidNotFit));
        var boxes = ids.Select(id => after.FindShape(id)!.Bounds).ToList();
        NoOverlaps(boxes, 2);
        // Gathered around the middle, not along an edge.
        Assert.All(boxes, b => Assert.True(b.Center.Dist(new Vec2(100, 100)) < 40, $"{b} is far from the middle"));
        // From the corner they go along the top instead.
        var (corner, _) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = sheet, SpacingMm = 2, MarginMm = 2, FromCenter = false }, M);
        Assert.All(ids, id => Assert.True(corner.FindShape(id)!.Bounds.MinY < 5));
    }

    [Fact]
    public void RotatesALongPartToFit()
    {
        var (s, ids) = Parts(PathGeometry.Rect(0, 0, 90, 10));
        var tall = PathGeometry.Rect(0, 0, 40, 120);
        Assert.Equal(1, Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = tall, RotationStepDeg = 0 }, M).Report.DidNotFit);
        var (after, report) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = tall, RotationStepDeg = 90 }, M);
        Assert.Equal(1, report.Placed);
        var b = after.FindShape(ids[0])!.Bounds;
        Assert.True(b.Height > 80 && b.Width < 15, $"{b}");
    }

    [Fact]
    public void AvoidsShapesAlreadyOnTheSheetAndKeepsGroupsTogether()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        var obstacle = new PathShape { LayerId = l, Geometry = PathGeometry.Rect(0, 0, 60, 100) };
        var g = Guid.NewGuid();
        var a = new PathShape { LayerId = l, GroupId = g, Geometry = PathGeometry.Rect(150, 150, 20, 20) };
        var b = new PathShape { LayerId = l, GroupId = g, Geometry = PathGeometry.Ellipse(185, 160, 5, 5) };
        s = Ops.AddShapes(s, [obstacle, a, b]);
        var (after, report) = Nesting.Nest(s, new HashSet<Guid> { a.Id, b.Id }, new NestOptions { Sheet = PathGeometry.Rect(0, 0, 100, 100), SpacingMm = 2 }, M);
        Assert.Equal(1, report.Placed); // one group = one part
        var na = after.FindShape(a.Id)!.Bounds; var nb = after.FindShape(b.Id)!.Bounds;
        Assert.True(na.MinX >= 62 - 0.01, $"{na} overlaps the obstacle");
        Assert.Equal(25, nb.Center.Dist(na.Center), 1); // still 25 mm apart, as grouped (it may have been turned)
        Assert.Equal(after.FindShape(obstacle.Id)!.Bounds, obstacle.Bounds); // obstacle untouched
    }

    [Fact]
    public void FillsTheSheetWithCopies()
    {
        var (s, ids) = Parts(PathGeometry.Rect(150, 150, 20, 20));
        var (after, report) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = PathGeometry.Rect(0, 0, 100, 100), SpacingMm = 2, MarginMm = 2, FillSheet = true, RotationStepDeg = 0 }, M);
        Assert.Equal(16, report.Placed); // 4 × 4 with 2 mm gaps in 96 × 96
        Assert.Equal(15, report.Added.Count);
        NoOverlaps(after.Shapes.Select(x => x.Bounds), 2);
        var (_, three) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = PathGeometry.Rect(0, 0, 100, 100), Copies = 3 }, M);
        Assert.Equal((3, 2), (three.Placed, three.Added.Count));
    }

    [Fact]
    public void ReportsPartsThatDontFitAndUsesRoundSheets()
    {
        var (s, ids) = Parts(PathGeometry.Rect(0, 0, 150, 150), PathGeometry.Rect(0, 0, 20, 20));
        var circle = PathGeometry.Ellipse(110, 110, 50, 50);
        var (after, report) = Nesting.Nest(s, ids.ToHashSet(), new NestOptions { Sheet = circle, MarginMm = 1 }, M);
        Assert.Equal((1, 1), (report.Placed, report.DidNotFit));
        Assert.Equal(ids[0], report.Unplaced.Single());
        // The small square sits inside the circle.
        var b = after.FindShape(ids[1])!.Bounds;
        foreach (var corner in new[] { new Vec2(b.MinX, b.MinY), new Vec2(b.MaxX, b.MinY), new Vec2(b.MinX, b.MaxY), new Vec2(b.MaxX, b.MaxY) })
            Assert.True(corner.Dist(new Vec2(110, 110)) <= 50, $"{corner} is outside the circle");
    }
}
