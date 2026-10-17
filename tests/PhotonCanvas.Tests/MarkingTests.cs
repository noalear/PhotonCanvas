using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Turning per pass, focus offsets, depth slices and offset fill.</summary>
public class PassTurnAndFocusTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;
    static readonly IReadOnlyList<Vec2> Square = [new(10, 10), new(20, 10), new(20, 20), new(10, 20)];

    [Fact]
    public void PassesTurnWhenWrittenOut()
    {
        var b = new FillBlock([Square], LaserSettings.Blue(30, 400) with { Passes = 3 }, 20) { AngleStepDeg = 45 };
        var g = new Job(M, [b]) { Created = "x" }.Render();
        // No device loop (it would repeat one pattern), three bodies instead.
        Assert.DoesNotContain("G104", g);
        Assert.Equal(3, g.Split('\n').Count(l => l.StartsWith("# blockConfig")));
        Assert.Equal(90, b.ForPass(3).AngleDeg);
        Assert.Equal(0, b.ForPass(3).AngleStepDeg);
        // Whole-job rounds: round r is turned (r − 1) steps.
        var rounds = new Job(M, [b]) { Created = "x", PassMode = new PassMode.Rounds(0) }.Render();
        Assert.Equal(g.Split('\n').Count(l => l.StartsWith("G1")), rounds.Split('\n').Count(l => l.StartsWith("G1")));
    }

    [Fact]
    public void FocusOffsetMovesTheHeadOnceAndBack()
    {
        var a = new FillBlock([Square], LaserSettings.Blue(30, 400), 20) { FocusOffsetMm = 1.5 };
        var b = a with { Polygons = [[new(30, 10), new(40, 10), new(40, 20), new(30, 20)]] };
        var g = new Job(M, [a, b]) { Created = "x" }.Render();
        Assert.Equal(1, g.Split('\n').Count(l => l == "G0Z-1.5F600"));
        Assert.Equal(1, g.Split('\n').Count(l => l == "G0Z1.5F600"));
        Assert.True(g.IndexOf("G0Z-1.5F600", StringComparison.Ordinal) < g.IndexOf("BITMAP HEAD", StringComparison.Ordinal));
        Assert.True(g.IndexOf("G0Z1.5F600", StringComparison.Ordinal) < g.IndexOf("# END", StringComparison.Ordinal));
        Assert.Throws<JobException>(() => new Job(M, [a with { FocusOffsetMm = 11 }]).Render());
        // No offset, no head moves.
        Assert.DoesNotContain("G102", new Job(M, [a with { FocusOffsetMm = 0 }]).Render());
    }

    [Fact]
    public void LayerSettingsReachTheBlocks()
    {
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Fill) with { FillAngleDeg = 10, AngleStepDeg = 20, TurnEachRun = true, RunCount = 2, FocusOffsetMm = -1 };
        layer = layer with { Laser = layer.Laser with { Passes = 3 } };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) }]);
        var f = Assert.IsType<FillBlock>(Assert.Single(JobBuilder.Build(s, new JobOptions()).Job.Blocks));
        // Two runs of three passes already turned 6 × 20°.
        Assert.Equal(10 + 120, f.AngleDeg);
        Assert.Equal(20, f.AngleStepDeg);
        Assert.Equal(-1, f.FocusOffsetMm);
    }
}

public class MaskFillTests
{
    static FillMask Mask(int w, int h, double cell, params (int C, int R)[] on)
    {
        var cells = new bool[w * h];
        foreach (var (c, r) in on) cells[r * w + c] = true;
        return new FillMask(new Vec2(10, 10), cell, w, h, cells);
    }

    [Fact]
    public void LinesCoverExactlyTheFilledCells()
    {
        // Cells (1,1) and (2,1) of 1 mm: x 11–13, y 11–12.
        var m = Mask(4, 4, 1, (1, 1), (2, 1));
        var segs = Job.FillLines(new FillBlock([], LaserSettings.Blue(30, 400), 100) { Mask = m });
        Assert.NotEmpty(segs);
        Assert.All(segs, s =>
        {
            Assert.InRange(s.Start.Y, 11, 12);
            Assert.Equal(11, Math.Min(s.Start.X, s.End.X), 6);
            Assert.Equal(13, Math.Max(s.Start.X, s.End.X), 6);
        });
        Assert.Equal(10, segs.Count); // 1 mm tall at 0.1 mm apart
    }

    [Fact]
    public void AngledLinesStayOnFilledCells()
    {
        var on = new List<(int, int)>();
        for (int r = 0; r < 10; r++)
            for (int c = 0; c < 10; c++)
                if ((c - 5) * (c - 5) + (r - 5) * (r - 5) < 16) on.Add((c, r));
        var m = Mask(10, 10, 0.5, [.. on]);
        var segs = Job.FillLines(new FillBlock([], LaserSettings.Blue(30, 400), 50) { Mask = m, AngleDeg = 37 });
        Assert.NotEmpty(segs);
        foreach (var s in segs)
            for (double t = 0.02; t < 1; t += 0.1)
            {
                var p = s.Start + (s.End - s.Start) * t;
                int c = (int)Math.Floor((p.X - 10) / 0.5), r = (int)Math.Floor((p.Y - 10) / 0.5);
                Assert.Contains((c, r), on);
            }
        Assert.Empty(Job.FillLines(new FillBlock([], LaserSettings.Blue(30, 400), 50) { Mask = Mask(3, 3, 1) }));
    }

    [Fact]
    public void SharedLevelsMakeNestedSlices()
    {
        byte[] levels = [0, 1, 2, 3];
        var masks = Enumerable.Range(1, 3).Select(k => new FillMask(new Vec2(0, 0), 1, 4, 1, levels, k)).ToList();
        Assert.Equal([3, 2, 1], masks.Select(m => m.Count()));
        Assert.Equal(new Rect2(3, 0, 4, 1), masks[2].FilledBounds());
    }
}

public class DepthSliceTests
{
    /// <summary>A left-to-right ramp, white to black: deeper to the right.</summary>
    static ImageShape Ramp(Guid layer)
    {
        int w = 64, h = 8;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte v = (byte)(255 - x * 255 / (w - 1));
                rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        var src = ImageSource.FromEncoded(new RasterImage(w, h, rgba).EncodePng());
        return new ImageShape { LayerId = layer, Source = src, SizeMm = new Vec2(32, 4), Transform = Affine.Translate(20, 20) };
    }

    static (DocState, Layer) Doc(Func<Layer, Layer> edit)
    {
        var s = DocState.New();
        var layer = edit(Layer.Default(1, LayerMode.Image) with { ImageMode = ImageEngraveMode.Slices, Dpi = 127, LinesPerCm = 50, AngleStepDeg = 30 });
        s = Ops.AddLayer(s, layer);
        return (Ops.AddShapes(s, [Ramp(layer.Id)]), layer);
    }

    [Fact]
    public void OneFillPerSliceDeeperIsSmaller()
    {
        var (s, _) = Doc(l => l with { Slices = l.Slices with { Count = 4 } });
        var job = JobBuilder.Build(s, new JobOptions());
        var fills = job.Job.Blocks.Cast<FillBlock>().ToList();
        Assert.Equal(4, fills.Count);
        Assert.All(fills, f => Assert.NotNull(f.Mask));
        var areas = fills.Select(f => f.Mask!.Count()).ToList();
        Assert.True(areas.Zip(areas.Skip(1)).All(p => p.First > p.Second));
        Assert.Equal([0.0, 30, 60, 90], fills.Select(f => f.AngleDeg));
        Assert.All(fills, f => Assert.Equal(120, f.AngleStepDeg));
        // The deepest slice is at the dark (right) end.
        Assert.True(fills[^1].Mask!.FilledBounds().MinX > 40);
        Assert.Equal(4, job.Steps.Count);
        Assert.Contains("G1", job.Motion);
    }

    [Fact]
    public void CleanupAndFocusFollow()
    {
        var (s, _) = Doc(l => l with
        {
            Laser = LaserSettings.Blue(80, 300),
            Slices = l.Slices with { Count = 4, CleanupEvery = 2, DropMm = 0.1, DropEvery = 2, CleanupLaser = l.Slices.CleanupLaser with { Power = 15, Speed = 800 } },
        });
        var fills = JobBuilder.Build(s, new JobOptions()).Job.Blocks.Cast<FillBlock>().ToList();
        Assert.Equal(6, fills.Count); // 4 slices + 2 cleanups
        var cleanups = fills.Where(f => f.Laser.Power == 15).ToList();
        Assert.Equal(2, cleanups.Count);
        Assert.All(cleanups, c => Assert.Equal(LightSource.Blue, c.Laser.Light));
        // Whole-picture cleanup uses the first (largest) slice's cells.
        Assert.Equal(fills[0].Mask!.Count(), cleanups[0].Mask!.Count());
        Assert.Equal([0.0, 0, 0, 0.1, 0.1, 0.1], fills.Select(f => Math.Round(f.FocusOffsetMm, 6)));
    }

    [Fact]
    public void NegativeAndLimits()
    {
        var (s, _) = Doc(l => l with { Slices = l.Slices with { Count = 4, Negative = true } });
        var fills = JobBuilder.Build(s, new JobOptions()).Job.Blocks.Cast<FillBlock>().ToList();
        Assert.True(fills[^1].Mask!.FilledBounds().MaxX < 32); // light (left) is deep now
        var (bad, _) = Doc(l => l with { Slices = l.Slices with { Count = 100, DropMm = 1, DropEvery = 1 } });
        Assert.Throws<JobException>(() => JobBuilder.Build(bad, new JobOptions()));
    }
}

public class OffsetFillTests
{
    [Fact]
    public void RingsStepInward()
    {
        var pos = new Vec2(0, 0);
        var loops = JobBuilder.OffsetLoops(PathGeometry.Rect(10, 10, 10, 10), 1, 0.01, ref pos);
        Assert.Equal(5, loops.Count); // 9, 7, 5, 3 and 1 mm squares
        var sizes = loops.Select(l => Rect2.Bounds(l).Width).ToList();
        Assert.Equal([9.0, 7, 5, 3, 1], sizes.Select(v => Math.Round(v, 2)));
    }

    [Fact]
    public void OffsetLayerMakesOneLoopsBlock()
    {
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Fill) with { FillStyle = FillStyle.Offset, LinesPerCm = 10 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        var block = Assert.IsType<LoopsBlock>(Assert.Single(job.Job.Blocks));
        Assert.Equal(5, block.Loops.Count);
        Assert.Contains("BITMAP HEAD", job.Motion);
        Assert.True(job.EstimateS > 0);
        // Laser off between loops, on along them.
        Assert.Contains(job.Motion.Split('\n'), l => l.StartsWith("G1X") && l.EndsWith("S0"));
    }
}

public class VariableTests
{
    static readonly DateTime Now = new(2026, 10, 16, 14, 5, 0);

    static VariableData Vars() => new()
    {
        Serial = 42,
        CsvName = "people.csv",
        CsvText = "Name;City\n\"Smith, Jo\";Leeds\nAda;\"Lon\"\"don\"\n",
    };

    [Fact]
    public void FieldsFillIn()
    {
        var v = Vars();
        Assert.Equal("Part 0042", VariableText.Resolve("Part {serial:0000}", v, Now));
        Assert.Equal("2026-10-16 14:05", VariableText.Resolve("{date} {time}", v, Now));
        Assert.Equal("Smith, Jo from Leeds (row 1)", VariableText.Resolve("{Name} from {city} (row {row})", v, Now));
        Assert.Equal("{plain} {nope}", VariableText.Resolve("{{plain}} {nope}", v, Now));
        Assert.Equal("Lon\"don", VariableText.Resolve("{City}", v with { CsvRow = 1 }, Now));
        Assert.False(VariableText.HasFields("no fields"));
        Assert.True(VariableText.HasFields("a {serial}"));
    }

    [Fact]
    public void CsvReading()
    {
        var t = Csv.Parse("a,b\n1,\"x,y\"\n\n2,3");
        Assert.Equal(["a", "b"], t.Headers.ToArray());
        Assert.Equal(2, t.Rows.Length);
        Assert.Equal("x,y", t.Cell(0, 1));
        Assert.Equal(1, t.Column(" B "));
        Assert.Equal(Csv.Table.Empty, Csv.Parse(""));
    }

    static DocState WithText(string text, VariableData v)
    {
        var s = DocState.New() with { Vars = v };
        var layer = Layer.Default(1, LayerMode.Fill);
        s = Ops.AddLayer(s, layer);
        return Ops.AddShapes(s, [TextShape.Create(text, new TextStyle(), layer.Id, Affine.Translate(20, 20))]);
    }

    [Fact]
    public void RefreshAndAfterRun()
    {
        var s = VariableText.Refresh(WithText("No. {serial} {Name}", Vars()), Now);
        var t = s.Shapes.OfType<TextShape>().Single();
        Assert.Equal("No. 42 Smith, Jo", t.Shown);
        Assert.Same(s, VariableText.Refresh(s, Now));
        var next = VariableText.AfterRun(s, Now);
        Assert.Equal(43, next.Vars.Serial);
        Assert.Equal(1, next.Vars.CsvRow);
        Assert.Equal("No. 43 Ada", next.Shapes.OfType<TextShape>().Single().Shown);
        // Advancing off: nothing moves.
        var still = VariableText.AfterRun(s with { Vars = s.Vars with { AdvanceAfterRun = false } }, Now);
        Assert.Equal(42, still.Vars.Serial);
    }

    [Fact]
    public void ProblemsStopTheJob()
    {
        var done = VariableText.Refresh(WithText("{Name}", Vars() with { CsvRow = 2 }), Now);
        Assert.Contains("have been marked", Assert.Single(VariableText.Problems(done)));
        Assert.Throws<JobException>(() => JobBuilder.Build(done, new JobOptions()));
        var past = WithText("{serial}", Vars() with { SerialEnd = 40 });
        Assert.Contains("past the last", Assert.Single(VariableText.Problems(past)));
        var unknown = WithText("{Colour}", Vars());
        Assert.Contains("no heading", Assert.Single(VariableText.Problems(unknown)));
        Assert.Empty(VariableText.Problems(WithText("{serial} {Name}", Vars())));
    }

    [Fact]
    public void LayersCountRunsForTurning()
    {
        var s = WithText("x", new VariableData());
        var id = s.Layers[1].Id;
        s = Ops.UpdateLayers(s, new HashSet<Guid> { id }, l => l with { TurnEachRun = true });
        Assert.Equal(1, VariableText.AfterRun(s, Now).FindLayer(id)!.RunCount);
    }
}

public class CodeTests
{
    [Fact]
    public void QrAndBarcodes()
    {
        var (cols, rows, dark) = CodeShape.Encode(CodeKind.QR, "PhotonCanvas 0042");
        Assert.Equal(cols, rows);
        Assert.True(cols >= 21);
        Assert.Contains(true, dark);
        var (bc, br, _) = CodeShape.Encode(CodeKind.Code128, "ABC-123");
        Assert.Equal(1, br);
        Assert.True(bc > 30);
        var (_, dmRows, _) = CodeShape.Encode(CodeKind.DataMatrix, "1234");
        Assert.True(dmRows >= 10);

        var (g, err) = CodeShape.Build(new CodeSpec(CodeKind.QR, "x", ModuleMm: 0.5), "hello");
        Assert.Null(err);
        var b = Rect2.Bounds(g.Flatten(0.01).SelectMany(f => f.Points).ToList());
        Assert.Equal(cols == 21 ? 10.5 : b.Width, b.Width, 6);
        var (_, bad) = CodeShape.Build(new CodeSpec(CodeKind.Ean13), "12ab");
        Assert.Contains("12 digits", bad);
        // Inverted: the margin is marked, so the code is bigger by it.
        var (inv, _) = CodeShape.Build(new CodeSpec(CodeKind.QR, "x", ModuleMm: 0.5, Invert: true, QuietModules: 2), "hello");
        Assert.Equal(b.Width + 2, Rect2.Bounds(inv.Flatten(0.01).SelectMany(f => f.Points).ToList()).Width, 6);
    }

    [Fact]
    public void CodesFollowFieldsAndSave()
    {
        var s = DocState.New() with { Vars = new VariableData { Serial = 7 } };
        var layer = Layer.Default(1, LayerMode.Fill) with
        {
            FillStyle = FillStyle.Offset, AngleStepDeg = 37, TurnEachRun = true, RunCount = 3, FocusOffsetMm = 0.5,
            Slices = new DepthSlices { Count = 50, Negative = true, DropMm = 0.02, DropEvery = 5, CleanupEvery = 10, CleanupWhole = false },
        };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [CodeShape.Create(new CodeSpec(CodeKind.QR, "SN{serial:000}"), null, layer.Id, Affine.Translate(5, 5))]);
        s = VariableText.Refresh(s, DateTime.Now);
        var code = s.Shapes.OfType<CodeShape>().Single();
        Assert.Equal("SN007", code.Shown);
        Assert.False(code.Geometry.IsEmpty);

        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(7, back.Vars.Serial);
        var l2 = back.Layers.Single(l => l.Id == layer.Id);
        Assert.Equal(layer with { Material = null }, l2 with { Material = null });
        var c2 = back.Shapes.OfType<CodeShape>().Single();
        Assert.Equal(code.Spec, c2.Spec);
        Assert.Equal("SN007", VariableText.Refresh(back, DateTime.Now).Shapes.OfType<CodeShape>().Single().Shown);
    }
}

public class TestGridTests
{
    [Fact]
    public void FocusGridSetsOffsets()
    {
        var o = MaterialTestOptions.FocusTest(LightSource.Blue);
        var (s, _) = MaterialTest.Add(DocState.New(), o);
        var squares = s.Layers.Where(l => l.Name.StartsWith("Test ") && l.Name != "Test labels").ToList();
        Assert.Equal(13 * 2, squares.Count);
        Assert.Equal(-3, squares.Min(l => l.FocusOffsetMm));
        Assert.Equal(3, squares.Max(l => l.FocusOffsetMm));
        Assert.Contains(s.Shapes.OfType<TextShape>(), t => t.Text == "+0.5");
    }

    [Fact]
    public void IntervalGridSetsLineDensity()
    {
        var (s, _) = MaterialTest.Add(DocState.New(), MaterialTestOptions.IntervalTest(LightSource.Red));
        var squares = s.Layers.Where(l => l.Name.StartsWith("Test ") && l.Name != "Test labels").ToList();
        Assert.Contains(squares, l => Math.Abs(l.LinesPerCm - 1000) < 1e-6);
        Assert.Contains(squares, l => Math.Abs(l.LinesPerCm - 100) < 1e-6);
        Assert.All(squares, l => Assert.Equal(LightSource.Red, l.Laser.Light));
        Assert.Equal([0.01, 0.02, 0.03], MaterialTest.Steps(0.01, 0.1, 10).Take(3));
    }
}
