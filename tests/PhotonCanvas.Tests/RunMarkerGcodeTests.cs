using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The "laser is here" marker against the G-code the machine gets (Kyle, 2026-11-18: behind on cuts, nowhere
/// near on multi-pass heat cuts, a different way of engraving than the laser's on fills).
/// </summary>
public class RunMarkerGcodeTests
{
    static PathGeometry Rect(double x, double y, double w, double h) => PathGeometry.Rect(x, y, w, h);
    static PathShape Rect(Guid layer, double x, double y, double w, double h) => new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, w, h) };

    /// <summary>A design of one layer and these outlines on it.</summary>
    static DocState One(Layer layer, params PathGeometry[] shapes)
    {
        var s = DocState.New();
        layer = layer with { Id = s.Layers[0].Id };
        s = s with { Layers = [layer] };
        return Ops.AddShapes(s, [.. shapes.Select(g => new PathShape { LayerId = layer.Id, Geometry = g })]);
    }

    /// <summary>Every lit position, in order: from the motion file itself, and from the marker's block-by-block moves along the timeline.</summary>
    static (List<Vec2> File, List<Vec2> Marker) LitPositions(BuiltJob job)
    {
        var whole = MotionTrace.Read(job.Motion.Split('\n'), job.Job.Mode, job.Job.Machine.FieldW, default);
        var file = Enumerable.Range(0, whole.Count).Where(whole.Burning).Select(whole.To).ToList();
        var marker = new List<Vec2>();
        foreach (var span in job.Timeline.Where(s => s.Block >= 0))
            for (int p = span.Pass; p < span.Pass + span.Passes; p++)
            {
                var tr = job.Job.BlockTrace(span.Block, p, default);
                marker.AddRange(Enumerable.Range(0, tr.Count).Where(tr.Burning).Select(tr.To));
            }
        return (file, marker);
    }

    static void SameMoves(BuiltJob job)
    {
        var (file, marker) = LitPositions(job);
        Assert.NotEmpty(file);
        Assert.Equal(file.Count, marker.Count);
        for (int i = 0; i < file.Count; i++)
            Assert.True(file[i].Dist(marker[i]) < 1e-3, $"move {i}: file {file[i]}, marker {marker[i]}");
    }

    [Fact]
    public void TheMarkerRunsTheMotionFilesOwnMovesInOrder()
    {
        // Cuts over several passes, round by round.
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Blue(70, 20).WithPasses(3) };
        SameMoves(JobBuilder.Build(One(cut, Rect(10, 10, 20, 10), Rect(50, 10, 10, 10)), new JobOptions { WholeJobPasses = true, PassDelayS = 2 }));
        // Fills: spread out, one way, crosshatched, overscan, turned each pass.
        var fill = Layer.Default(0, LayerMode.Fill) with
        {
            Laser = LaserSettings.Blue(30, 500).WithPasses(2), LinesPerCm = 50, SpreadLines = true, Bidirectional = false,
            Crosshatch = true, OverscanMm = 1, AngleStepDeg = 30,
        };
        SameMoves(JobBuilder.Build(One(fill, PathGeometry.Ellipse(40, 40, 10, 6)), new JobOptions { WholeJobPasses = true }));
        SameMoves(JobBuilder.Build(One(fill, PathGeometry.Ellipse(40, 40, 10, 6)), new JobOptions { WholeJobPasses = false }));
        // Offset loops, wobble, dot mode, step-down.
        SameMoves(JobBuilder.Build(One(fill with { FillStyle = FillStyle.Offset, LinesPerCm = 10 }, Rect(10, 10, 15, 8)), new JobOptions()));
        SameMoves(JobBuilder.Build(One(cut with { Wobble = true, WobbleDiameterMm = 0.5, WobbleSpacingMm = 0.2 }, Rect(10, 10, 5, 5)), new JobOptions()));
        SameMoves(JobBuilder.Build(One(cut with { DotModeMm = 0.5 }, Rect(10, 10, 5, 5)), new JobOptions()));
        SameMoves(JobBuilder.Build(One(cut with { StepDownMm = 0.2 }, Rect(10, 10, 5, 5), Rect(30, 10, 5, 5)), new JobOptions { WholeJobPasses = false }));
    }

    [Fact]
    public void AWaitAfterAPathIsSpentAtItsEndNotCrawlingAlongIt()
    {
        // 10 mm/s round a 40 mm square: 4 s of cutting, then 20 s of cooling.
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Blue(70, 10) };
        var job = JobBuilder.Build(One(cut, Rect(10, 10, 10, 10), Rect(100, 10, 10, 10)), new JobOptions { WholeJobPasses = true, PathPauseS = 20 });
        var first = job.Timeline[0];
        Assert.Equal(20, first.WaitS, 6);
        double cutting = first.PassSeconds - first.WaitS;
        Assert.InRange(cutting, 3.9, 4.2);

        // Halfway through the cutting (2 s), the dot is halfway round: the far corner.
        var half = RunTracker.At(job, first.Start + cutting / 2, wait: true);
        var start = job.Steps[half.Current[0]].Points[0];
        Assert.True(half.Exact && half.LaserOn);
        Assert.True(half.Head!.Value.Dist(start) > 13, $"{half.Head} from {start}");
        // Before, it took the wait in too: 12 s in it would have been only a sixth of the way round.
        Assert.NotEmpty(half.Trail);

        // During the wait: the square is marked, the laser off where it finished, nothing being marked.
        var cooling = RunTracker.At(job, first.Start + cutting + 10, wait: true);
        Assert.True(cooling.Pausing);
        Assert.False(cooling.LaserOn);
        Assert.Empty(cooling.Current);
        Assert.Contains(half.Current[0], cooling.Done);
        Assert.True(cooling.Head!.Value.Dist(start) < 0.01);
    }

    [Fact]
    public void HeatOrderedPassesFollowTheirOwnCuts()
    {
        var s = DocState.New();
        var cut = s.Layers[0] with { Laser = LaserSettings.Red(60, 400, 45, 132).WithPasses(4), Order = PathOrder.Heat, HeatThicknessMm = 0.1, HeatLimitC = 20 };
        s = s with { Layers = [cut] };
        var shapes = new List<Shape>();
        for (int i = 0; i < 4; i++) shapes.Add(Rect(cut.Id, 10 + i * 12, 10, 10, 10));
        s = Ops.AddShapes(s, shapes);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
        Assert.Contains(job.Timeline, t => t.WaitS > 0);
        SameMoves(job);
        // Mid-cut of every piece, the dot is on that piece's own outline.
        foreach (var span in job.Timeline.Where(t => !t.IsPause))
        {
            var m = RunTracker.At(job, span.Start + (span.PassSeconds - span.WaitS) / 2, wait: true);
            var step = job.Steps[span.FirstStep];
            Assert.Equal([span.FirstStep], m.Current);
            Assert.True(DistToPath(m.Head!.Value, step) < 0.01, $"{m.Head} off its cut");
        }
    }

    [Fact]
    public void AFillsScanLineIsTheLineTheMachineIsOn()
    {
        // Lines spread out: the second line burned isn't next to the first.
        var fill = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(30, 100), LinesPerCm = 10, SpreadLines = true, Crosshatch = false, OverscanMm = 0 };
        var job = JobBuilder.Build(One(fill, Rect(10, 10, 20, 10)), new JobOptions());
        var block = (FillBlock)job.Job.Blocks[0];
        var lines = Job.FillLines(block);
        var span = job.Timeline[0];
        var trace = job.Job.BlockTrace(0, 1, default);
        // Sample the middle of each lit move: the scan line is the fill line at that point of the run.
        var seen = new List<double>();
        for (int i = 0; i < trace.Count; i++)
        {
            if (!trace.Burning(i)) continue;
            double tt = (trace.StartOf(i) + (i + 1 < trace.Count ? trace.StartOf(i + 1) : trace.Seconds)) / 2;
            var m = RunTracker.At(job, span.Start + tt / trace.Seconds * span.PassSeconds, wait: true);
            Assert.True(m.LaserOn);
            var (a, b) = m.Sweep!.Value;
            Assert.Equal(a.Y, b.Y, 6);
            Assert.Equal(20, Math.Abs(a.X - b.X), 1);
            if (seen.Count == 0 || Math.Abs(seen[^1] - a.Y) > 1e-6) seen.Add(a.Y);
        }
        Assert.Equal(lines.Select(l => Math.Round(l.Start.Y, 3)), seen.Select(y => Math.Round(y, 3)));
        Assert.True(Math.Abs(seen[1] - seen[0]) > 1.5, "spread: the second line is well away from the first");
    }

    [Fact]
    public void BothWaysAndOneWayFillsRunTheirLinesThatWay()
    {
        foreach (bool both in new[] { true, false })
        {
            var fill = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(30, 100), LinesPerCm = 10, Bidirectional = both, Crosshatch = false };
            var job = JobBuilder.Build(One(fill, Rect(10, 10, 20, 5)), new JobOptions());
            var tr = job.Job.BlockTrace(0, 1, default);
            var dirs = Enumerable.Range(0, tr.Count).Where(tr.Burning).Select(i => Math.Sign(tr.To(i).X - tr.From(i).X)).ToList();
            Assert.True(dirs.Count >= 4);
            if (both) Assert.Contains(-1, dirs); else Assert.DoesNotContain(-1, dirs);
        }
    }

    [Fact]
    public void OffsetFillsGoRoundNotAcross()
    {
        var fill = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(30, 100), FillStyle = FillStyle.Offset, LinesPerCm = 10 };
        var job = JobBuilder.Build(One(fill, Rect(10, 10, 20, 20)), new JobOptions());
        var span = job.Timeline[0];
        var m = RunTracker.At(job, span.Start + span.PassSeconds * 0.1, wait: true);
        Assert.True(m.Exact);
        Assert.Null(m.Sweep); // no line across it
        // Early on it's going round the outside, near the edge, not across the middle.
        var h = m.Head!.Value;
        double edge = Math.Min(Math.Min(h.X - 10, 30 - h.X), Math.Min(h.Y - 10, 30 - h.Y));
        Assert.InRange(edge, -0.01, 2);
    }

    [Fact]
    public void TheMopasWorkIsShownWhereItWasDrawn()
    {
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Red(60, 50, 45, 132) };
        var job = JobBuilder.Build(One(cut, Rect(10, 10, 10, 10)), new JobOptions { MopaOffset = new Vec2(0.5, -0.3) });
        Assert.Equal(new Vec2(0.5, -0.3), job.MopaShift);
        var span = job.Timeline[0];
        var m = RunTracker.At(job, span.Start + span.PassSeconds * 0.3, wait: true);
        Assert.True(DistToPath(m.Head!.Value, job.Steps[0]) < 0.01, $"{m.Head}");
    }

    [Fact]
    public void ReadsAttachmentMovesBackToTheDesign()
    {
        var rot = new WorkMode.Rotary(0.6, -1, 100, OriginY: 50);
        var tr = MotionTrace.Read(["G0X10.000U5.000", "G1X20.000U-5.000S500F600", "G4P1000"], rot, 220, new Vec2(0, 50));
        Assert.Equal(new Vec2(10, 45), tr.To(0));
        Assert.Equal(new Vec2(20, 55), tr.To(1));
        Assert.True(tr.Burning(1));
        Assert.Equal(Math.Sqrt(200) / 10, tr.StartOf(2) - tr.StartOf(1), 0.01); // 14.1 mm at 10 mm/s (F600)
        Assert.Equal(tr.StartOf(2) + 1, tr.Seconds, 6);       // the second's pause
        var conv = new WorkMode.Conveyor(1, 0);
        var c = MotionTrace.Read(["G0U-100.000Y30.000", "G25X110.000Y40.000"], conv, 220, default);
        Assert.Equal(new Vec2(10, 30), c.To(0));
        Assert.Equal(new Vec2(10, 40), c.To(1)); // the galvo's X is its parked line, not a design X
    }

    static double DistToPath(Vec2 p, BuiltJob.Step st)
    {
        var pts = st.Closed ? [.. st.Points, st.Points[0]] : st.Points;
        double best = double.MaxValue;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var d = pts[i] - a;
            double l2 = d.Dot(d);
            double t = l2 > 0 ? Math.Clamp((p - a).Dot(d) / l2, 0, 1) : 0;
            best = Math.Min(best, p.Dist(a + d * t));
        }
        return best;
    }
}
