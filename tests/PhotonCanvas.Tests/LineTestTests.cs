using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class LineTestTests
{
    static Layer Pictures() => Layer.Default(1, LayerMode.Image) with
    {
        Name = "Pictures", ImageMode = ImageEngraveMode.Grayscale, Dpi = 300, GrayMinPower = 10, SpreadLines = true, Bidirectional = true,
        Laser = LaserSettings.Blue(90, 80),
    };

    [Fact]
    public void EachPatchChangesOneThing()
    {
        var v = LineTest.Variants(Pictures());
        Assert.Equal(["As set", "Not spread", "Scattered", "One way", "400 dpi", "500 dpi", "2 passes turned", "3 passes turned"], v.Select(x => x.Label));
        Assert.False(v[1].Settings.SpreadLines);
        Assert.True(v[1].Settings.Bidirectional);
        // Scattered: still spread, in no set order.
        Assert.True(v[2].Settings.SpreadLines && v[2].Settings.SpreadScatter);
        Assert.False(v[3].Settings.Bidirectional);
        Assert.True(v[3].Settings.SpreadLines);
        // Finer lines put in the same heat: power down by the same share as the lines get closer.
        Assert.Equal(400, v[4].Settings.Dpi);
        Assert.Equal(67.5, v[4].Settings.Laser.Power, 6);
        Assert.Equal(7.5, v[4].Settings.GrayMinPower, 6);
        Assert.Equal(80, v[4].Settings.Laser.Speed);
        // The opposite toggles when the layer starts the other way.
        var other = LineTest.Variants(Pictures() with { SpreadLines = false, Bidirectional = false });
        Assert.Equal(["As set", "Spread out", "Both ways", "400 dpi", "500 dpi", "2 passes turned", "3 passes turned"], other.Select(x => x.Label));
        // Turned passes: the power cut by the square root (weak passes mark less), each pass 180° ÷ passes round from the last.
        Assert.Equal((3, 52.0, 60.0), (v[7].Settings.Laser.Passes, v[7].Settings.Laser.Power, v[7].Settings.AngleStepDeg));
        Assert.Equal((2, 63.6, 90.0, 7.1), (v[6].Settings.Laser.Passes, v[6].Settings.Laser.Power, v[6].Settings.AngleStepDeg, v[6].Settings.GrayMinPower));
    }

    [Fact]
    public void TheTestBuildsWithEachPatchOnItsOwnLayerAndCanBeTakenOut()
    {
        var s = DocState.New();
        var (t, ids) = LineTest.Add(s, Pictures(), 10, new Vec2(20, 20));
        Assert.Equal(8, ids.Count);
        var patches = LineTest.Patches(t);
        Assert.Equal(["As set", "Not spread", "Scattered", "One way", "400 dpi", "500 dpi", "2 passes turned", "3 passes turned"], patches.Select(p => LineTest.Label(p.Layer)));
        Assert.Equal("Pictures", LineTest.SourceName(t));
        // Patches side by side, gaps between.
        Assert.True(patches[1].Patch.Bounds.MinX > patches[0].Patch.Bounds.MaxX + 2);

        var job = JobBuilder.Build(t, new JobOptions());
        var dots = job.Job.Blocks.OfType<DotBlock>().ToList();
        Assert.Equal(8, dots.Count);
        Assert.Equal([true, false, true, true, true, true, true, true], dots.Select(d => d.Spread > 1));
        Assert.Equal([false, false, true, false, false, false, false, false], dots.Select(d => d.Scatter));
        Assert.Equal([false, false, false, true, false, false, false, false], dots.Select(d => d.Unidirectional));
        Assert.Equal([300.0, 300, 300, 300, 400, 500, 300, 300], dots.Select(d => d.Dpi));
        // The turned patches: one layout per pass, at 0°, 60° and 120°.
        Assert.Equal([0.0, 60, 120], dots[7].PassBlocks!.Select(d => d.AngleDeg));
        Assert.Equal([0.0, 90], dots[6].PassBlocks!.Select(d => d.AngleDeg));

        var gone = LineTest.Remove(t);
        Assert.Empty(LineTest.Patches(gone));
        Assert.DoesNotContain(gone.Layers, LineTest.IsTestLayer);
    }

    [Fact]
    public void PatchesCanBeAPieceOfThePictureAtItsRealSize()
    {
        // 1448 pixels across 58.7 mm (the skull picture): a 10 mm piece is about 247 pixels of it.
        int w = 1448, h = 1086;
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = (byte)(i % w * 255 / w); px[i * 4 + 3] = 255; }
        var pic = new ImageShape { LayerId = Guid.NewGuid(), Source = ImageSource.FromEncoded(new RasterImage(w, h, px).EncodePng()), SizeMm = new Vec2(100, 75), Transform = Affine.Scale(0.5872, 0.5872), Tone = ToneAdjust.None with { Contrast = 0.2 } };
        var piece = LineTest.PieceOf(pic, 10);
        Assert.Equal(10, piece.SizeMm.X, 1);
        Assert.Equal(0.2, piece.Tone.Contrast, 6);
        var (s, _) = LineTest.Add(DocState.New(), Pictures(), 10, new Vec2(20, 20), piece);
        var patch = LineTest.Patches(s)[0].Patch;
        Assert.InRange(patch.Source.Width, 245, 249);
        // The middle of the picture: a mid grey in the middle of the piece.
        Assert.InRange(patch.Source.SampleLuminance(patch.Source.Width / 2.0, patch.Source.Height / 2.0), 0.45, 0.55);
    }

    [Fact]
    public void TheTestFitsWhereTheCloseUpSeesInRows()
    {
        var fit = LineTest.Fit(8, 10, new Vec2(30, 22))!.Value;
        var need = LineTest.Size(8, fit.Size, fit.Columns);
        Assert.True(need.X <= 30 && need.Y <= 22, $"{need} at {fit}");
        var (s, _) = LineTest.Add(DocState.New(), Pictures(), fit.Size, new Vec2(100, 80), columns: fit.Columns);
        var patches = LineTest.Patches(s);
        // Still in the order made, row by row.
        Assert.Equal(["As set", "Not spread", "Scattered", "One way", "400 dpi", "500 dpi", "2 passes turned", "3 passes turned"], patches.Select(p => LineTest.Label(p.Layer)));
        Assert.True(patches[fit.Columns].Patch.Bounds.MinY > patches[0].Patch.Bounds.MaxY);
        var all = patches.Select(p => p.Patch.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        Assert.True(all.Width <= 30.01 && all.Height <= 22.01);
        Assert.Null(LineTest.Fit(8, 10, new Vec2(8, 8)));
    }

    [Fact]
    public void ScatteredRowsAreEveryRowOnceAndNeverBesideTheLast()
    {
        var o = Job.Scattered(400, 6);
        Assert.Equal(Enumerable.Range(0, 400), o.Order());
        // Each next row at least the stride away from the last (all but a few at the very end).
        int near = o.Zip(o.Skip(1)).Count(p => Math.Abs(p.First - p.Second) < 6);
        Assert.True(near <= 3, $"{near} close pairs");
        // No repeating pattern: unlike the regular spread, the steps between rows vary.
        Assert.True(o.Zip(o.Skip(1)).Select(p => p.Second - p.First).Distinct().Count() > 100);
        Assert.Equal(o, Job.Scattered(400, 6)); // the same every time
    }

    [Fact]
    public void APatchsSettingsGoOnTheLayer()
    {
        var target = Pictures() with { Dither = DitherMethod.Jarvis };
        var tested = LineTest.Variants(target).Single(v => v.Label == "400 dpi").Settings with { Bidirectional = false };
        var used = LineTest.UseOn(target, tested);
        Assert.Equal((400.0, 67.5, false), (used.Dpi, used.Laser.Power, used.Bidirectional));
        Assert.Equal(target.Id, used.Id);
        Assert.Equal(target.Name, used.Name);
        Assert.Equal(DitherMethod.Jarvis, used.Dither);
    }

    [Fact]
    public void OneWayRowsAllRunTheSameDirection()
    {
        var bitmap = new Bitmap(3, 2);
        for (int i = 0; i < bitmap.Bits.Length; i++) bitmap.Bits[i] = true;
        var block = new DotBlock(bitmap, new Vec2(10, 10), LaserSettings.Blue(50, 100), 254, 200, 10, "grayscale");
        string Lines(DotBlock b) => new Job(MachineProfile.F2Ultra, [b]) { Created = "x" }.Render();
        // Both ways: the second row starts at its right end, straight below where the first ended.
        Assert.Contains("G25Y10.100", Lines(block));
        // One way: it starts at the left again.
        Assert.Contains("G25X10.000Y10.100", Lines(block with { Unidirectional = true }));
        Assert.DoesNotContain("G25Y10.100", Lines(block with { Unidirectional = true }));
    }
}
