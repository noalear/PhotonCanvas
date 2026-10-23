using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Defects found in the October 2026 review, each pinned by the case that showed it.</summary>
public class ReviewFindingsTests
{
    static PathShape Sq(Guid layer, double x) => new() { LayerId = layer, Geometry = PathGeometry.Rect(x, 10, 10, 10) };

    [Fact]
    public void HighlyReflectiveIsSaved()
    {
        var s = DocState.New();
        s = s with { Layers = [s.Layers[0] with { Laser = LaserSettings.Red(50, 500, 200, 55), Reflective = true }] };
        var back = DocFile.Deserialize(DocFile.Serialize(Ops.AddShapes(s, [Sq(s.Layers[0].Id, 10)])));
        Assert.True(back.Layers[0].Reflective);
        // So the job still refuses the frequencies that could reflect back into the MOPA.
        Assert.Throws<JobException>(() => JobBuilder.Build(back, new JobOptions()));
    }

    /// <summary>Layers A, "A fill" (a sub-layer of A) and B, a square on A and one on B.</summary>
    static (DocState S, Layer A, Layer Fill, Layer B) WithSubLayer()
    {
        var a = Layer.Default(0) with { Name = "A" };
        var fill = Layer.Default(1, LayerMode.Fill) with { Name = "A fill", ParentId = a.Id };
        var b = Layer.Default(2) with { Name = "B" };
        var s = new DocState([a, fill, b], []);
        return (Ops.AddShapes(s, [Sq(a.Id, 10), Sq(b.Id, 50)]), a, fill, b);
    }

    [Fact]
    public void DeletedLayersShapesNeverLandOnASubLayer()
    {
        var (s, a, fill, b) = WithSubLayer();
        // The layer above B is the sub-layer: B's square goes to its parent, and is still marked.
        var r = Ops.DeleteLayers(s, new HashSet<Guid> { b.Id }, fill.Id);
        Assert.All(r.Shapes, x => Assert.Equal(a.Id, x.LayerId));
        Assert.Equal(2, JobBuilder.Build(r, new JobOptions()).Job.Blocks.Count(x => x is VectorBlock));

        // Deleting A takes its sub-layer too; A's square goes to the layer left, not away.
        r = Ops.DeleteLayers(s, new HashSet<Guid> { a.Id }, fill.Id);
        Assert.Equal(2, r.Shapes.Count);
        Assert.All(r.Shapes, x => Assert.Equal(b.Id, x.LayerId));
    }

    [Fact]
    public void MergingKeepsALayerThatHoldsShapes()
    {
        var (s, a, fill, _) = WithSubLayer();
        // The sub-layer dragged above its parent, then both merged.
        s = Ops.MoveLayer(s, fill.Id, 0);
        var r = Ops.MergeLayers(s, [fill.Id, a.Id]);
        Assert.Equal(2, r.Shapes.Count);
        Assert.Contains(r.Layers, l => l.Id == a.Id);
        Assert.Contains(r.Shapes, x => x.LayerId == a.Id);
    }

    static double Length(PathGeometry g) => g.Flatten(0.01).Sum(f =>
    {
        double l = 0;
        for (int i = 1; i < f.Points.Count; i++) l += f.Points[i].Dist(f.Points[i - 1]);
        return l + (f.Closed && f.Points.Count > 1 ? f.Points[^1].Dist(f.Points[0]) : 0);
    });

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void SingleLineTextIsOneWholeStroke(double size)
    {
        // An O: one line round the middle of the ring, between the outline's outer and inner edge.
        var outline = TextLayout.Build("O", new TextStyle("Arial", size));
        var single = TextLayout.Build("O", new TextStyle("Arial", size, SingleLine: true));
        var rings = outline.Subpaths.Select(sp => Length(new PathGeometry([sp]))).OrderBy(x => x).ToList();
        double middle = (rings[0] + rings[^1]) / 2;
        Assert.InRange(single.Subpaths.Count(), 1, 2);
        Assert.InRange(Length(single), middle * 0.9, middle * 1.08);
    }

    [Fact]
    public void DeepestSliceFirstStartsOnTheSurface()
    {
        // A grey ramp, so every one of the 16 slices has something in it.
        var px = new byte[16 * 4];
        for (int i = 0; i < 16; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = (byte)(255 - i * 16); px[i * 4 + 3] = 255; }
        var layer = Layer.Default(0, LayerMode.Image) with
        {
            ImageMode = ImageEngraveMode.Slices, Dpi = 50,
            Slices = new DepthSlices { Count = 16, DropMm = 0.2, DropEvery = 4, CleanupEvery = 4, DeepestFirst = true },
        };
        var pic = new ImageShape { LayerId = layer.Id, Source = ImageSource.FromEncoded(new Core.Imaging.RasterImage(16, 1, px).ToPng()), SizeMm = new Vec2(64, 8), Transform = Affine.Translate(20, 20) };
        var job = JobBuilder.Build(Ops.AddShapes(new DocState([layer], []), [pic]), new JobOptions()).Job;
        var slices = job.Blocks.OfType<FillBlock>().Where(b => b.Laser == layer.Laser).ToList();
        // Nothing is cut yet when the first slice runs: the surface focus. Then down 0.2 mm every 4 slices.
        Assert.Equal(0, slices[0].FocusOffsetMm, 9);
        Assert.All(slices.Zip(slices.Skip(1)), p => Assert.True(p.Second.FocusOffsetMm >= p.First.FocusOffsetMm));
        Assert.Equal(0.2 * 3, slices[^1].FocusOffsetMm, 9);
        // A cleanup after every 4 slices run, not after the slice numbered 4.
        int i4 = job.Blocks.ToList().FindIndex(b => b is FillBlock f && f.Laser != layer.Laser);
        Assert.Equal(4, job.Blocks.Take(i4).Count(b => b is FillBlock f && f.Laser == layer.Laser));
    }

    [Fact]
    public void ACurvedSurfaceFillBurnsAtItsSpeed()
    {
        // The head's Z move between bands is at F600 (10 mm/s); F is modal, so the burn has to set it back.
        var square = new Vec2[] { new(95, 95), new(125, 95), new(125, 125), new(95, 125) };
        var g = new Job(MachineProfile.F2Ultra, [new FillBlock([square], LaserSettings.Blue(10, 400), 50)]) { SurfaceHeight = p => 20 + (p.Y - 95) * 0.1, Created = "x" }.Render();
        var lines = g.Split('\n').Select(l => l.Trim()).ToList();
        string feed = "F24000";
        string? now = null;
        int burns = 0;
        foreach (var l in lines)
        {
            if (System.Text.RegularExpressions.Regex.Match(l, @"F(\d+)") is { Success: true } m && l.StartsWith('G')) now = "F" + m.Groups[1].Value;
            if (l.StartsWith("G1X") && !l.EndsWith("S0")) { burns++; Assert.Equal(feed, now); }
        }
        Assert.True(burns > 100);
    }

    [Fact]
    public void OverscanIsCheckedOnEveryTurnedPass()
    {
        // Lines across fit with their run-ups; turned 90° on pass 2, the run-ups go above the field.
        var area = new Vec2[] { new(5, 0.5), new(50, 0.5), new(50, 30), new(5, 30) };
        var fill = new FillBlock([area], LaserSettings.Blue(10, 400).WithPasses(2), 20) { OverscanMm = 2, AngleStepDeg = 90 };
        Assert.Throws<OutsideWorkAreaException>(() => new Job(MachineProfile.F2Ultra, [fill]).Validate());
    }

    static readonly Vec2[] Box = [new(60, 60), new(100, 60), new(100, 100), new(60, 100)];

    [Fact]
    public void StepDownCountsTheBlocksOwnPassesAndTheFocusOffset()
    {
        var m = MachineProfile.F2Ultra;
        // 3 passes of 2 mm (4 mm in all) beside a layer of 8 passes, round by round: fine.
        var cut = new VectorBlock(Polyline.Loop(Box), LaserSettings.Blue(80, 10).WithPasses(3)) { Drop = new StepDown(0, 2) };
        var score = new VectorBlock(Polyline.Loop(Box), LaserSettings.Blue(20, 200).WithPasses(8));
        new Job(m, [cut, score]) { PassMode = new PassMode.Rounds(0) }.Validate();
        // An 8 mm focus offset and 4 mm of steps would lower the head 12 mm: refused.
        Assert.Throws<JobException>(() => new Job(m, [cut with { FocusOffsetMm = 8 }]).Validate());
    }

    [Fact]
    public void ACurvedSurfaceKeepsThePauseAfterEachPath()
    {
        var a = new VectorBlock(Polyline.Loop(Box), LaserSettings.Blue(80, 10)) { PauseAfterS = 5 };
        var b = new VectorBlock(Polyline.Loop([.. Box.Select(p => p + new Vec2(50, 0))]), LaserSettings.Blue(80, 10)) { PauseAfterS = 5 };
        var g = new Job(MachineProfile.F2Ultra, [a, b]) { SurfaceHeight = p => 20 + p.X * 0.01, Created = "x" }.Render();
        Assert.Equal(2, g.Split('\n').Count(l => l.Trim() == "G4P5000"));
    }

    [Fact]
    public void ASectionedPathPausesOnceAfterItsLastPiece()
    {
        // A zigzag in and out of the section: several pieces, one pause.
        var zig = Polyline.Open(new Vec2[] { new(0, 10), new(30, 10), new(30, 50), new(0, 50), new(0, 90), new(30, 90) });
        var pieces = Sections.Cut(new VectorBlock(zig, LaserSettings.Blue(80, 10)) { PauseAfterS = 5 }, new Rect2(10, 0, 40, 100), new Vec2(0, 0)).ToList();
        Assert.True(pieces.Count > 1);
        Assert.Equal(5, pieces.Sum(p => p.PauseAfterS));
        Assert.Equal(5, pieces[^1].PauseAfterS);
    }

    [Fact]
    public void GrblRunUpsStayOnTheBed()
    {
        var m = MachineSetup.FullSpectrumHobby;
        var bmp = new Bitmap(4, 2);
        for (int c = 0; c < 4; c++) { bmp[c, 0] = true; bmp[c, 1] = true; }
        var dots = new DotBlock(bmp, new Vec2(0.1, 0.1), LaserSettings.Blue(40, 200), 254, 200, 1, "Jarvis");
        var fill = new FillBlock([[new(0.5, 0.5), new(10, 0.5), new(10, 5), new(0.5, 5)]], LaserSettings.Blue(40, 200), 20) { OverscanMm = 3 };
        foreach (var line in GrblWriter.Write(new Job(m.Profile, [dots, fill]), m).Split('\n').Where(l => l.StartsWith("G0") || l.StartsWith("G1")))
            foreach (System.Text.RegularExpressions.Match c in System.Text.RegularExpressions.Regex.Matches(line, @"([XY])(-?[\d.]+)"))
            {
                double v = double.Parse(c.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(v, -1e-6, (c.Groups[1].Value == "X" ? m.BedW : m.BedH) + 1e-6);
            }
    }

    [Fact]
    public void AChosenStartWinsOverStartNearestWithKerf()
    {
        var cut = Layer.Default(0) with { KerfMm = 0.2, StartNearest = true };
        var sq = new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }.WithStart(new Vec2(20, 30));
        var job = JobBuilder.Build(Ops.AddShapes(new DocState([cut], []), [sq]), new JobOptions()).Job;
        var first = job.Blocks.OfType<VectorBlock>().Single().Path.Points[0];
        // The kerf moves the outline 0.1 mm out: the start stays at the chosen point, just outside it.
        Assert.True(first.Dist(new Vec2(20, 30.1)) < 0.02, $"started at {first}");
    }

    [Fact]
    public void ABrokenFieldOnALayerThatIsntSentDoesntStopTheJob()
    {
        var cut = Layer.Default(0);
        var off = Layer.Default(1) with { Name = "Notes", Output = false };
        var note = TextShape.Create("{nosuchfield}", new TextStyle("Arial", 5), off.Id, Affine.Translate(50, 50));
        var s = Ops.AddShapes(new DocState([cut, off], []), [Sq(cut.Id, 10), note]);
        Assert.Single(JobBuilder.Build(s, new JobOptions()).Job.Blocks);
        // Sent, it does.
        Assert.Throws<JobException>(() => JobBuilder.Build(s with { Layers = [cut, off with { Output = true }] }, new JobOptions()));
    }

    [Fact]
    public void SmallerLayerFindings()
    {
        // Dot mode round a closed 40 mm square at 1 mm: 160 dots, the corner where it starts fired once.
        var dots = JobBuilder.DotsAlong(Polyline.Loop(Box).EmittedPoints(), 1);
        Assert.Equal(160, dots.Count);
        Assert.True(dots[0].Dist(dots[^1]) > 0.5);

        // Turning from run to run: a picture in dots turns by its passes, not by a slice count it doesn't use.
        var pic = Layer.Default(0, LayerMode.Image) with { TurnEachRun = true, RunCount = 1, AngleStepDeg = 10, Laser = LaserSettings.Blue(30, 80).WithPasses(2) };
        Assert.Equal(20, pic.RunTurnDeg, 9);

        // Copied settings keep "Keep turning from run to run".
        Assert.True(Doc.Materials.ProcessSettings.From(pic).ApplyTo(Layer.Default(1, LayerMode.Image)).TurnEachRun);

        // Tone curves are equal when their points are, wherever the list came from.
        var a = new Core.Imaging.ToneCurve([(0.0, 0.0), (0.5, 0.7), (1.0, 1.0)]);
        var b = new Core.Imaging.ToneCurve([.. a.Points]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        // A scored line's perforation is checked like a cut's.
        var score = Layer.Default(0, LayerMode.Score) with { PerforateMm = 0.001, PerforateGapMm = 0 };
        Assert.Throws<JobException>(() => JobBuilder.Build(Ops.AddShapes(new DocState([score], []), [Sq(score.Id, 10)]), new JobOptions()));
    }

    [Fact]
    public void AShapeSavedOnASubLayerLoadsOnItsParent()
    {
        var (s, a, fill, _) = WithSubLayer();
        s = s with { Shapes = [.. s.Shapes.Select(x => x.LayerId == a.Id ? x.With(layerId: fill.Id) : x)] };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.DoesNotContain(back.Shapes, x => x.LayerId == fill.Id);
        Assert.Contains(back.Shapes, x => x.LayerId == a.Id);
    }
}
