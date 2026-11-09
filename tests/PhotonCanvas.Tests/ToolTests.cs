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
    public void RotaryLineIsOnTheFieldNotTheUnrolledObject()
    {
        // The app builds rotary jobs on the unrolled work area (220 × once round): a 25 mm dowel is 78.5 mm
        // round, but the laser line still belongs at the middle of the 220 mm field, not 39 mm from the back.
        var area = M with { FieldH = 78.54 };
        var mode = new WorkMode.Rotary(128 / 78.54, 1, 78.54);
        var g = JobBuilder.Build(Design(LayerMode.Cut, 30, 20), new JobOptions { Machine = area, Mode = mode }).Motion.Split('\n');
        Assert.Contains("G0Y110.000", g);
        Assert.DoesNotContain("G0Y39.270", g);
        // The galvo goes to the line before U mode is switched on, as well as after.
        int park = Array.IndexOf(g, "G0Y110.000"), u = Array.IndexOf(g, "G100");
        Assert.True(park >= 0 && u > park, "the line comes before G100");
        // A rotary that sits elsewhere: the line is the user's.
        var moved = JobBuilder.Build(Design(LayerMode.Cut, 30, 20), new JobOptions { Machine = area, Mode = mode with { LineY = 62.5 } }).Motion;
        Assert.Contains("G0Y62.500", moved);
        Assert.DoesNotContain("G0Y110.000", moved);
        // Framing traces the same line.
        var frame = JobBuilder.Build(Design(LayerMode.Cut, 30, 20), new JobOptions { Machine = area, Mode = mode with { LineY = 62.5 } });
        Assert.Contains("Y62", frame.Border);
    }

    [Fact]
    public void ConveyorParksTheGalvoInsideTheField()
    {
        var area = M with { FieldW = 610 };
        var g = JobBuilder.Build(Design(LayerMode.Fill, 300, 30), new JobOptions { Machine = area, Mode = new WorkMode.Conveyor(0.74592, 0) }).Motion;
        Assert.Contains("G0X110.000", g);
        Assert.DoesNotContain("G0X305.000", g);
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

public class RotaryReturnTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    static DocState Design(LayerMode mode)
    {
        var s = DocState.New();
        var layer = Layer.Default(1, mode);
        s = Ops.AddLayer(s, layer);
        return Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(20, 20, 30, 30) }]);
    }

    static IEnumerable<long> Feeds(string line)
    {
        int i = line.IndexOf('F');
        if (i < 0) yield break;
        int j = i + 1;
        while (j < line.Length && char.IsDigit(line[j])) j++;
        if (j > i + 1) yield return long.Parse(line[(i + 1)..j]);
    }

    [Fact]
    public void RotaryNeverSpinsAtTheMachinesRapid()
    {
        // Kyle's rod (2026-11-08): a cut's rapids at F180000 spun a direct-drive chuck dozens of turns a second.
        double circ = Math.PI * 32.7;
        var rot = new WorkMode.Rotary(32 / circ, 1, circ);
        long cap = Job.RotaryFeedCap(rot);
        Assert.Equal((long)Math.Floor(10 * circ * 60), cap); // 10 turns a second
        var s = DocState.New();
        var cut = Layer.Default(1, LayerMode.Cut) with { Laser = LaserSettings.Blue(90, 1500) };
        var fill = Layer.Default(2, LayerMode.Fill) with { Laser = LaserSettings.Blue(30, 1500) };
        s = Ops.AddLayer(Ops.AddLayer(s, cut), fill);
        s = Ops.AddShapes(s, [
            new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(20, 10, 30, 30) },
            new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(80, 60, 10, 10) },
            new PathShape { LayerId = fill.Id, Geometry = PathGeometry.Rect(120, 20, 20, 20) }]);
        var job = JobBuilder.Build(s, new JobOptions { Machine = M with { FieldH = Math.Round(circ, 2) }, Mode = rot });
        var g = job.Motion.Split('\n');
        int on = Array.IndexOf(g, "G100"), off = Array.LastIndexOf(g, "G101");
        Assert.True(on > 0 && off > on);
        // The rapid is held down straight after U goes on.
        Assert.Equal($"G0F{cap}", g[on + 1]);
        // While U is on, no feed of any move is past the cap.
        foreach (var line in g[on..off].Where(l => l.StartsWith("G0") || l.StartsWith("G1")))
            foreach (var f in Feeds(line)) Assert.True(f <= cap, $"{line} is faster than {cap}");
        Assert.Contains(job.Warnings, w => w.Contains("10 turns a second"));
        // A flat job keeps the machine's rapid and the layer's speed.
        var flat = JobBuilder.Build(s, new JobOptions()).Motion;
        Assert.Contains("F90000", flat);
        Assert.DoesNotContain($"G0F{cap}", flat);
        // A 40 mm/s cut is well under the limit and keeps its own speed.
        var slow = Ops.UpdateLayers(s, new HashSet<Guid> { cut.Id, fill.Id }, l => l with { Laser = l.Laser with { Speed = 40 } });
        var slowJob = JobBuilder.Build(slow, new JobOptions { Machine = M with { FieldH = Math.Round(circ, 2) }, Mode = rot });
        Assert.Contains("F2400", slowJob.Motion);
        Assert.DoesNotContain(slowJob.Warnings, w => w.Contains("10 turns a second"));
    }

    static List<double> Us(string motion) =>
        [.. motion.Split('\n').Where(l => l.StartsWith("G1") || l.StartsWith("G0X"))
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"U(-?[0-9.]+)"))
            .Where(m => m.Success).Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];

    [Fact]
    public void ALineAllTheWayRoundCarriesOnPastItsStart()
    {
        // Kyle (2026-11-09): burning through a rod with one line round it left a short stretch at the seam.
        double circ = Math.PI * 32.7;
        var rot = new WorkMode.Rotary(32 / circ, 1, circ);
        var machine = M with { FieldH = Math.Round(circ, 2) };
        var s = DocState.New();
        var cut = Layer.Default(1, LayerMode.Cut) with { Laser = LaserSettings.Blue(90, 10) };
        s = Ops.AddLayer(s, cut);
        double h = Math.Round(circ, 2);
        s = Ops.AddShapes(s, [new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Line(new(50, 0), new(50, h)) }]);
        var job = JobBuilder.Build(s, new JobOptions { Machine = machine, Mode = rot });
        var u = Us(job.Motion);
        // Once round, then 2 mm more over where it started.
        Assert.Equal(h + RotarySeam.OverlapMm, u.Max() - u.Min(), 2);
        // Where it starts on the object is unchanged (the design's middle faces the laser).
        Assert.Equal(h / 2, job.Job.Mode is WorkMode.Rotary r ? r.OriginY : 0, 2);
        // The cut layer's overcut, when longer, is used instead.
        var longer = Ops.UpdateLayers(s, new HashSet<Guid> { cut.Id }, l => l with { OvercutMm = 5 });
        var u5 = Us(JobBuilder.Build(longer, new JobOptions { Machine = machine, Mode = rot }).Motion);
        Assert.Equal(h + 5, u5.Max() - u5.Min(), 2);
        // A line a little short of round still closes: it carries on to the start and past it.
        var shortOne = Ops.AddShapes(DocState.New() is var d ? Ops.AddLayer(d, cut) : d,
            [new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Line(new(50, 0.5), new(50, h)) }]);
        var us = Us(JobBuilder.Build(shortOne, new JobOptions { Machine = machine, Mode = rot }).Motion);
        Assert.Equal(h + RotarySeam.OverlapMm, us.Max() - us.Min(), 2);
        // A line part way round, and the same line on the flat bed, are left as drawn.
        var half = Ops.AddShapes(Ops.AddLayer(DocState.New(), cut), [new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Line(new(50, 10), new(50, 60)) }]);
        var uh = Us(JobBuilder.Build(half, new JobOptions { Machine = machine, Mode = rot }).Motion);
        Assert.Equal(50, uh.Max() - uh.Min(), 2);
        Assert.DoesNotContain(JobBuilder.Build(s, new JobOptions()).Motion.Split('\n'), l => l.Contains($"Y{h + RotarySeam.OverlapMm:0.###}"));
    }

    [Fact]
    public void CarryingOnFollowsThePathsOwnStart()
    {
        // A wavy band round the object: the carry-on retraces its first stretch, once round on.
        var pts = new List<Vec2> { new(10, 0), new(11, 1), new(12, 2), new(10, 100) };
        Assert.True(RotarySeam.GoesRound(pts, 100));
        Assert.False(RotarySeam.GoesRound(pts, 120));
        var o = RotarySeam.CarryOn(pts, 100, Math.Sqrt(2) * 1.5);
        // It ends right over its start, so it goes straight on along its first stretch.
        Assert.Equal(new Vec2(10, 100), o[3]);
        Assert.Equal(new Vec2(11, 101), o[4]);
        Assert.Equal(11.5, o[^1].X, 6);
        Assert.Equal(101.5, o[^1].Y, 6);
        // Drawn the other way (down the page), it carries on downwards.
        var down = RotarySeam.CarryOn([new(10, 100), new(10, 0)], 100, 2);
        Assert.Equal(-2, down[^1].Y, 6);
    }

    [Fact]
    public void MaterialOutlinesAreNeverSentEvenThroughASubLayer()
    {
        // Kyle (2026-11-08) wondered if a material outline got into a rotary job. The material layer is
        // never sent; a sub-layer of it would go over its shapes again, so that isn't sent either.
        double circ = Math.PI * 32.7;
        var rot = new WorkMode.Rotary(32 / circ, 1, circ);
        var s = DocState.New();
        var cut = Layer.Default(1, LayerMode.Cut);
        s = Ops.AddLayer(s, cut);
        var outline = new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(5, 5, 200, 100) };
        var line = new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(40, 30, 10, 10) };
        s = Ops.AddShapes(s, [outline, line]);
        s = MaterialOutline.Mark(s, new HashSet<Guid> { outline.Id });
        var material = MaterialOutline.Layer(s)!;
        var under = Layer.Default(5, LayerMode.Cut) with { ParentId = material.Id, Output = true };
        s = Ops.AddLayer(s, under);
        Assert.True(MaterialOutline.IsUnder(s, under));
        Assert.False(MaterialOutline.IsUnder(s, material));
        Assert.DoesNotContain(JobBuilder.OutputLayers(s), l => l.Id == material.Id || l.Id == under.Id);
        var job = JobBuilder.Build(s, new JobOptions { Machine = M with { FieldH = Math.Round(circ, 2) }, Mode = rot });
        // Only the small square: nothing out at the outline's far corner (x 205).
        Assert.DoesNotContain("X205", job.Motion);
        Assert.True(job.Bounds.MaxX <= 50.5 && job.Bounds.MinX >= 39.5, $"{job.Bounds}");
    }

    [Fact]
    public void RotaryZMovesHoldTheRapidDownAgain()
    {
        var rot = new WorkMode.Rotary(1, 1, 100);
        var s = DocState.New();
        var cut = Layer.Default(1, LayerMode.Cut) with { FocusOffsetMm = 2 };
        s = Ops.AddLayer(s, cut);
        s = Ops.AddShapes(s, [new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(20, 20, 30, 30) }]);
        var g = JobBuilder.Build(s, new JobOptions { Machine = M with { FieldH = 100 }, Mode = rot }).Motion.Split('\n');
        long cap = Job.RotaryFeedCap(rot);
        // After each Z move the machine's rapid comes back; U on again, it's held down again before any move.
        for (int i = 0; i < g.Length; i++)
            if (g[i] == "G100") Assert.Equal($"G0F{cap}", g[i + 1]);
        Assert.True(g.Count(l => l == "G100") >= 2, "the focus offset makes a Z move");
    }

    [Fact]
    public void TurningJobOnlyTurnsTheRotaryWithTheLaserOff()
    {
        var job = new Job(M, []) { Mode = new WorkMode.Rotary(128 / 78.54, 1, 78.54, LineY: 90) };
        var g = job.TurnRotary(-12.5).Split('\n');
        Assert.Contains(g, l => l.StartsWith("M535U1.6297"));
        Assert.Contains("G100", g);
        Assert.Contains("G0U-12.500", g);
        Assert.Contains("G0Y90.000", g);
        Assert.Contains("G101", g);
        // No tail turn back: that would undo the turn.
        Assert.DoesNotContain("G0 U0", g);
        // Nothing that could fire: no G1 moves with power, no M3.
        Assert.DoesNotContain(g, l => l.StartsWith("G1") && l.Contains('S') && !l.Contains("S0"));
        Assert.DoesNotContain(g, l => l.StartsWith("M3"));
        // The tail after the turn.
        Assert.True(Array.IndexOf(g, "G0U-12.500") < Array.IndexOf(g, "G101"));
    }

    [Fact]
    public void TurningNeedsTheRotaryAndASaneDistance()
    {
        Assert.Throws<JobException>(() => new Job(M, []).TurnRotary(10));
        var rot = new Job(M, []) { Mode = new WorkMode.Rotary(1, 1, 100) };
        Assert.Throws<JobException>(() => rot.TurnRotary(double.NaN));
        Assert.Throws<JobException>(() => rot.TurnRotary(Job.MaxTurnMm + 1));
    }

    [Fact]
    public void TurnJobPackagesAsARotaryJob()
    {
        var xf = RotaryReturn.TurnJob(new WorkMode.Rotary(1.5, 1, 100), 25);
        var files = XfPackage.Read(xf);
        Assert.Contains(files, f => System.Text.Encoding.UTF8.GetString(f.Data).Contains("G0U25.000"));
        Assert.Contains(files, f => System.Text.Encoding.UTF8.GetString(f.Data).Contains("LASER_CYLINDER"));
    }

    [Fact]
    public void StoppedJobPositionFollowsTheRunMarker()
    {
        var area = M with { FieldH = 188.5 };
        var job = JobBuilder.Build(Design(LayerMode.Cut), new JobOptions { Machine = area, Mode = new WorkMode.Rotary(0.59917, -1, 188.5) });
        var rot = (WorkMode.Rotary)job.Job.Mode;
        Assert.Equal(35, rot.OriginY, 6); // the middle of the design faces the laser at the start
        // Not started marking, or finished (the end turns it back): nothing left over.
        Assert.Equal(0, RotaryReturn.UAt(job, 0));
        Assert.Equal(0, RotaryReturn.UAt(job, job.Timeline[^1].End + 1));
        // Part way round the square: where the marker is, round the object from the start, with the direction.
        double mid = job.Timeline[0].Start + job.Timeline[0].PassSeconds * 0.4;
        var head = RunTracker.At(job, mid).Head!.Value;
        double u = RotaryReturn.UAt(job, mid)!.Value;
        Assert.Equal(-(head.Y - 35), u, 6);
        Assert.InRange(Math.Abs(u), 0, 15.0001);
        // The machine's own progress gives the same answer at the same point.
        Assert.Equal(u, RotaryReturn.UAtFraction(job, mid / job.Timeline[^1].End)!.Value, 6);
        // A flat job has no rotary to turn back.
        Assert.Null(RotaryReturn.UAt(JobBuilder.Build(Design(LayerMode.Cut), new JobOptions()), mid));
    }

    [Fact]
    public void TurningStraightAwayIsScaledFromTheMotorUnits()
    {
        // Until checked: 1 ÷ the conveyor factor (the machine scales the feed by it).
        var s = new AppSettings { ConveyorUMultiple = 0.74592 };
        Assert.Equal(1 / 0.74592, RotaryReturn.NowScale(s), 9);
        // Kyle's chuck: 32 units a turn. A quarter turn is 8 motor units; reversed, the other way.
        var chuck = new WorkMode.Rotary(32 / 100.0, 1, 100);
        Assert.Equal(32, RotaryReturn.UnitsPerTurn(chuck, 100), 9);
        Assert.Equal(8, RotaryReturn.UnitsFor(90, chuck, 100), 9);
        Assert.Equal(-8, RotaryReturn.UnitsFor(90, chuck with { Direction = -1 }, 100), 9);
        // 5 of the feed's units at the assumed scale: about 42° on that chuck (Kyle saw 40–50°).
        Assert.InRange(5 / RotaryReturn.NowScale(s) / 32 * 360, 40, 45);
        // A one-turn check that made 1.25 turns: the scale comes down to match.
        Assert.Equal(RotaryReturn.NowScale(s) / 1.25, RotaryReturn.CorrectedNowScale(s, 1, 1.25), 9);
        s.RotaryNowScale = 1.1;
        Assert.Equal(1.1, RotaryReturn.NowScale(s));
        // Turning back what a stopped job left: the left-over U × that job's scale, the other way.
        s.RotaryLeftU = 10; s.RotaryLeftScale = 0.32;
        Assert.Equal(-3.2, RotaryReturn.UnitsBack(s), 9);
        s.RotaryLeftU = null;
        Assert.Equal(0, RotaryReturn.UnitsBack(s));
    }

    [Fact]
    public void LeftOverTurnsAddUpInTheFirstJobsScale()
    {
        var s = new AppSettings();
        RotaryReturn.NoteLeft(s, 10, new WorkMode.Rotary(2, 1, 100));
        Assert.Equal(10, s.RotaryLeftU);
        Assert.Equal(2, s.RotaryLeftScale);
        // A second stop with a scale twice as big turns twice as far per mm.
        RotaryReturn.NoteLeft(s, 5, new WorkMode.Rotary(4, 1, 100));
        Assert.Equal(20, s.RotaryLeftU!.Value, 6);
        // Back to the start: nothing left.
        RotaryReturn.NoteLeft(s, -10, new WorkMode.Rotary(4, 1, 100));
        Assert.Null(s.RotaryLeftU);
        Assert.Equal(90, RotaryReturn.Degrees(25, 100), 6);
        Assert.Equal(-25, RotaryReturn.UFor(90, new WorkMode.Rotary(1, -1, 100), 100), 6);
        // The return uses the stopped job's scale.
        s.RotaryLeftU = 7; s.RotaryLeftScale = 3; s.RotaryLeftCircumferenceMm = 120;
        var back = RotaryReturn.ReturnMode(s);
        Assert.Equal(3, back.UMultiple);
        Assert.Equal(120, back.CircumferenceMm);
    }
}
