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
        Assert.Equal(["As set", "Not spread", "One way", "400 dpi", "500 dpi"], v.Select(x => x.Label));
        Assert.False(v[1].Settings.SpreadLines);
        Assert.True(v[1].Settings.Bidirectional);
        Assert.False(v[2].Settings.Bidirectional);
        Assert.True(v[2].Settings.SpreadLines);
        // Finer lines put in the same heat: power down by the same share as the lines get closer.
        Assert.Equal(400, v[3].Settings.Dpi);
        Assert.Equal(67.5, v[3].Settings.Laser.Power, 6);
        Assert.Equal(7.5, v[3].Settings.GrayMinPower, 6);
        Assert.Equal(80, v[3].Settings.Laser.Speed);
        // The opposite toggles when the layer starts the other way.
        var other = LineTest.Variants(Pictures() with { SpreadLines = false, Bidirectional = false });
        Assert.Equal(["As set", "Spread out", "Both ways", "400 dpi", "500 dpi"], other.Select(x => x.Label));
    }

    [Fact]
    public void TheTestBuildsWithEachPatchOnItsOwnLayerAndCanBeTakenOut()
    {
        var s = DocState.New();
        var (t, ids) = LineTest.Add(s, Pictures(), 10, new Vec2(20, 20));
        Assert.Equal(5, ids.Count);
        var patches = LineTest.Patches(t);
        Assert.Equal(["As set", "Not spread", "One way", "400 dpi", "500 dpi"], patches.Select(p => LineTest.Label(p.Layer)));
        Assert.Equal("Pictures", LineTest.SourceName(t));
        // Patches side by side, gaps between.
        Assert.True(patches[1].Patch.Bounds.MinX > patches[0].Patch.Bounds.MaxX + 2);

        var job = JobBuilder.Build(t, new JobOptions());
        var dots = job.Job.Blocks.OfType<DotBlock>().ToList();
        Assert.Equal(5, dots.Count);
        Assert.Equal([true, false, true, true, true], dots.Select(d => d.Spread > 1));
        Assert.Equal([false, false, true, false, false], dots.Select(d => d.Unidirectional));
        Assert.Equal([300.0, 300, 300, 400, 500], dots.Select(d => d.Dpi));

        var gone = LineTest.Remove(t);
        Assert.Empty(LineTest.Patches(gone));
        Assert.DoesNotContain(gone.Layers, LineTest.IsTestLayer);
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
