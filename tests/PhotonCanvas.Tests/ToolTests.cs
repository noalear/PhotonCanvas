using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class ShapeToolTests
{
    static Rect2 B(PathGeometry g) => Rect2.Bounds(g.Flatten(0.005).SelectMany(f => f.Points).ToList());

    [Fact]
    public void RoundCornersKeepsTheBox()
    {
        var (g, n) = PathTools.RoundCorners(PathGeometry.Rect(0, 0, 20, 10), 2);
        Assert.Equal(4, n);
        Assert.Contains(g.Subpaths[0].Segs, s => s.Kind == SegKind.Cubic);
        var b = B(g);
        Assert.Equal(20, b.Width, 2);
        Assert.Equal(10, b.Height, 2);
        // The corner itself is cut off by about r(1 − 1/√2) diagonally.
        Assert.DoesNotContain(g.Subpaths[0].Flatten(0.01), p => p.Dist(new Vec2(0, 0)) < 0.5);
        // Too big a radius shrinks to fit (half the short side).
        var (big, _) = PathTools.RoundCorners(PathGeometry.Rect(0, 0, 20, 10), 50);
        Assert.Equal(10, B(big).Height, 2);
        // Curves are left alone.
        Assert.Equal(0, PathTools.RoundCorners(PathGeometry.Ellipse(5, 5, 5, 5), 1).Rounded);
    }

    [Fact]
    public void TidyJoinsClosesAndDeduplicates()
    {
        var a = PathGeometry.Polyline([new(0, 0), new(10, 0)], false);
        var b = PathGeometry.Polyline([new(10.02, 0), new(10, 10)], false);
        var c = PathGeometry.Polyline([new(10, 10), new(0, 10), new(0, 0.03)], false);
        var dup = PathGeometry.Rect(30, 30, 5, 5);
        var r = PathTools.Cleanup([a, b, c, dup, PathGeometry.Rect(30, 30, 5, 5)], 0.05);
        Assert.Equal(2, r.Joined);
        Assert.Equal(1, r.Closed);
        Assert.Equal(1, r.Duplicates);
        var joined = Assert.Single(r.Geometries[0].Subpaths);
        Assert.True(joined.Closed);
        Assert.True(r.Geometries[1].IsEmpty && r.Geometries[2].IsEmpty && r.Geometries[4].IsEmpty);
        Assert.False(PathTools.Cleanup([PathGeometry.Rect(0, 0, 5, 5)], 0.05).Changed);
    }

    [Fact]
    public void TrimRemovesThePieceBetweenCrossings()
    {
        var line = PathGeometry.Line(new(0, 5), new(30, 5));
        IReadOnlyList<Vec2>[] cutters = [[new(10, 0), new(10, 10)], [new(20, 0), new(20, 10)]];
        var (result, removed) = PathTools.Trim(line, new Vec2(15, 5.1), cutters)!.Value;
        Assert.Equal(2, result.Subpaths.Length);
        Assert.Equal(10, removed.Last().Dist(removed[0]), 6);
        // The end piece.
        var (end, _) = PathTools.Trim(line, new Vec2(25, 5), cutters)!.Value;
        Assert.Equal(20, B(end).MaxX, 6);
        // Nothing crosses: nothing to trim.
        Assert.Null(PathTools.Trim(line, new Vec2(15, 5), []));
        // A closed square cut by a line through it: the clicked side goes, the rest stays as one line.
        var (sq, _) = PathTools.Trim(PathGeometry.Rect(0, 0, 10, 10), new Vec2(0, 5), [[new(5, -1), new(5, 11)]])!.Value;
        Assert.False(Assert.Single(sq.Subpaths).Closed);
        Assert.Equal(5, B(sq).MinX, 6);
    }

    [Fact]
    public void CirclesAndPointsAlong()
    {
        var c = PathTools.Circle(PathGeometry.Ellipse(10, 10, 3, 3));
        Assert.NotNull(c);
        Assert.Equal(6, c!.Value.Diameter, 2);
        Assert.Null(PathTools.Circle(PathGeometry.Rect(0, 0, 6, 6)));
        Assert.Null(PathTools.Circle(PathGeometry.Ellipse(10, 10, 3, 2)));

        var pts = PathTools.PointsAlong([new(0, 0), new(10, 0)], closed: false, 3);
        Assert.Equal([0.0, 5, 10], pts.Select(p => Math.Round(p.Point.X, 6)));
        var round = PathTools.PointsAlong([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], closed: true, 4);
        Assert.Equal(new Vec2(10, 10), round[2].Point);
        Assert.Equal(90, round[1].AngleDeg, 6);
    }

    [Fact]
    public void WarpAndCopyAlong()
    {
        var sq = PathGeometry.Rect(0, 0, 10, 10);
        Assert.Equal(10, B(PathTools.Warp(sq, p => p)).Width, 2);
        var arched = PathTools.Warp(sq, p => new Vec2(p.X, p.Y - 3 * Math.Sin(Math.PI * p.X / 10)));
        Assert.True(B(arched).MinY < -2.5);

        var s = DocState.New();
        var shape = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(0, 0, 2, 2) };
        s = Ops.AddShapes(s, [shape]);
        var (after, ids) = PathTools.CopyAlong(s, new HashSet<Guid> { shape.Id }, [new(10, 50), new(110, 50)], false, 5, follow: true, removeOriginals: true);
        Assert.Equal(5, ids.Count);
        Assert.Null(after.FindShape(shape.Id));
        Assert.Equal(111, after.Bounds(ids).MaxX, 6); // the last copy is centred on the path's end
    }

    [Fact]
    public void TextFollowsAPath()
    {
        var style = new TextStyle("Arial", 5);
        var flat = TextShape.Create("HELLO", style, Guid.NewGuid(), Affine.Identity);
        var up = flat.OnPath(PathGeometry.Line(new(0, 100), new(0, 0)));
        Assert.NotNull(up.Guide);
        // Along a vertical line the words run upwards: taller than wide.
        Assert.True(up.Bounds.Height > up.Bounds.Width * 2);
        Assert.Null(up.OnPath(null).Guide);
        // Edited words keep following the path, and it's saved.
        var edited = up.Edit("HI THERE", style);
        Assert.NotNull(edited.Guide);
        var s = Ops.AddShapes(DocState.New(), [edited.With(layerId: DocState.New().Layers[0].Id)]);
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.NotNull(back.Shapes.OfType<TextShape>().Single().Guide);
    }

    [Fact]
    public void LockedShapesStayPutAndSave()
    {
        var s = DocState.New();
        var a = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(0, 0, 5, 5) };
        s = Ops.AddShapes(s, [a.WithLocked(true)]);
        Assert.Empty(s.Editable());
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.True(back.Shapes.Single().Locked);
        // Still marked.
        Assert.Single(JobBuilder.Build(back, new JobOptions()).Job.Blocks);
    }

    [Fact]
    public void HistoryStepsBackAndForth()
    {
        var d = new Document();
        var l = d.State.Layers[0].Id;
        for (int i = 0; i < 3; i++)
            d.Commit($"Step {i}", Ops.AddShapes(d.State, [new PathShape { LayerId = l, Geometry = PathGeometry.Rect(i * 10, 0, 5, 5) }]));
        Assert.Equal(["Step 0", "Step 1", "Step 2"], d.UndoLabels);
        d.Step(-2);
        Assert.Single(d.State.Shapes);
        Assert.Equal(["Step 1", "Step 2"], d.RedoLabels);
        d.Step(2);
        Assert.Equal(3, d.State.Shapes.Count);
    }
}

public class PictureStyleTests
{
    [Fact]
    public void ScreensKeepTheTone()
    {
        foreach (var m in new[] { DitherMethod.Halftone, DitherMethod.Newsprint, DitherMethod.LineScreen })
            foreach (var grey in new[] { 0.25f, 0.5f, 0.8f })
            {
                var dots = Dither.Apply(Enumerable.Repeat(grey, 120 * 120).ToArray(), 120, 120, m, new Screen(10, 30));
                double burn = dots.Count(d => d) / (120.0 * 120);
                Assert.InRange(burn, 1 - grey - 0.08, 1 - grey + 0.08);
            }
    }

    [Fact]
    public void SketchFindsOnlyEdges()
    {
        int w = 40, h = 20;
        var img = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) img[y * w + x] = x < 20 ? 0f : 1f;
        var dots = Dither.Apply(img, w, h, DitherMethod.Sketch);
        var cols = Enumerable.Range(0, w * h).Where(i => dots[i]).Select(i => i % w).Distinct().ToList();
        Assert.NotEmpty(cols);
        Assert.All(cols, c => Assert.InRange(c, 18, 21));
    }

    [Fact]
    public void DotWidthShortensLines()
    {
        IReadOnlyList<Vec2> sq = [new(10, 10), new(20, 10), new(20, 20), new(10, 20)];
        var plain = Job.FillLines(new FillBlock([sq], LaserSettings.Blue(30, 400), 10));
        var corrected = Job.FillLines(new FillBlock([sq], LaserSettings.Blue(30, 400), 10) { DotWidthMm = 0.1 });
        Assert.Equal(plain.Count, corrected.Count);
        Assert.All(corrected, s => Assert.Equal(9.9, s.Start.Dist(s.End), 6));
    }
}

public class WorkModeTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    static DocState Design(LayerMode mode, double w = 30, double h = 30)
    {
        var s = DocState.New();
        var layer = Layer.Default(1, mode);
        s = Ops.AddLayer(s, layer);
        return Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(20, 20, w, h) }]);
    }

    [Fact]
    public void RotaryUsesTheUAxis()
    {
        var job = JobBuilder.Build(Design(LayerMode.Cut), new JobOptions { Mode = new WorkMode.Rotary(0.59917, -1, 188.5) });
        var g = job.Motion;
        Assert.Contains("M535U0.59917", g);
        Assert.Contains("\nG100\n", g);
        Assert.Contains("\"keys\":[\"x\",\"u\"],\"rm\":-1", g);
        Assert.Contains("G0Y110.000", g);
        // The middle of the design faces the laser: its top edge is 15 mm round one way.
        Assert.Contains(g.Split('\n'), l => l.StartsWith("G0X20U15") || l.StartsWith("G1X20U15") || l.Contains("U-15") || l.Contains("U15"));
        Assert.Contains("G0 U0\nG101", g);
        Assert.Contains("\"axis\":\"U\"", System.Text.Encoding.UTF8.GetString(job.Xf));
        // A design taller than once round doesn't fit.
        Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(Design(LayerMode.Cut, 30, 200), new JobOptions { Mode = new WorkMode.Rotary(0.59917, -1, 188.5) }));
    }

    [Fact]
    public void ConveyorRunsLinesAcrossTheBelt()
    {
        var job = JobBuilder.Build(Design(LayerMode.Fill, 300, 30), new JobOptions { Mode = new WorkMode.Conveyor(0.74592, 0) });
        Assert.Contains("G100 F9600", job.Motion);
        Assert.Contains("\"keys\":[\"u\",\"y\"]", job.Motion);
        Assert.All(job.Job.Blocks.OfType<FillBlock>(), f => Assert.Equal(90, f.AngleDeg));
        // 300 mm long: fine on the belt, too long for the bed.
        Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(Design(LayerMode.Fill, 300, 30), new JobOptions()));
        Assert.Contains(job.Warnings, w => w.Contains("experimental"));
    }

    [Fact]
    public void FlatOutputIsUnchangedByTheModes()
    {
        var a = JobBuilder.Build(Design(LayerMode.Cut), new JobOptions(), DateTimeOffset.UnixEpoch).Motion;
        var b = JobBuilder.Build(Design(LayerMode.Cut), new JobOptions { Mode = new WorkMode.Flat() }, DateTimeOffset.UnixEpoch).Motion;
        Assert.Equal(a, b);
        Assert.DoesNotContain("G100", a);
    }
}

public class SectionTests
{
    [Fact]
    public void PlanCoversTheDesign()
    {
        var plan = Section.Plan(new Rect2(0, 0, 450, 150), 200, 200, MachineProfile.F2Ultra);
        Assert.Equal(3, plan.Count);
        Assert.Equal(new Vec2(-400, 0), plan[2].MaterialMove);
        // Every section lands in the same place.
        Assert.All(plan, p => Assert.Equal(new Vec2(10, 10), p.Clip.TopLeft + p.Shift));
    }

    [Fact]
    public void EachSectionIsCutAndMovedIntoTheField()
    {
        var s = DocState.New();
        var cut = s.Layers[0];
        var fill = Layer.Default(1, LayerMode.Fill);
        s = Ops.AddLayer(s, fill);
        s = Ops.AddShapes(s,
        [
            new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Line(new(10, 50), new(390, 50)) },
            new PathShape { LayerId = fill.Id, Geometry = PathGeometry.Rect(150, 60, 100, 30) },
        ]);
        var plan = Section.Plan(new Rect2(10, 50, 390, 90), 200, 200, MachineProfile.F2Ultra);
        Assert.Equal(2, plan.Count);
        foreach (var sec in plan)
        {
            var job = JobBuilder.Build(s, new JobOptions { Section = sec });
            foreach (var line in job.Motion.Split('\n').Where(l => l.StartsWith("G1X") || l.StartsWith("G0X")))
            {
                var x = double.Parse(line[3..].Split('Y', 'S')[0], System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(x, 0, 220);
            }
            var fills = job.Job.Blocks.OfType<FillBlock>().Single();
            Assert.NotNull(fills.ClipRect);
            Assert.All(Job.FillLines(fills), l => Assert.InRange(Math.Max(l.Start.X, l.End.X), 0, 210.0001));
        }
        // Without a section the design doesn't fit.
        Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(s, new JobOptions()));
    }
}
