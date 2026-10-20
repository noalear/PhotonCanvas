using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PictureAngleTests
{
    /// <summary>A 20 × 20 mm picture at (20, 20): left half black, right half white.</summary>
    static ImageShape HalfBlack(Guid layer)
    {
        int n = 100;
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int k = (y * n + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = (byte)(x < n / 2 ? 0 : 255);
                px[k + 3] = 255;
            }
        return new ImageShape { LayerId = layer, Source = ImageSource.FromEncoded(new RasterImage(n, n, px).EncodePng()), SizeMm = new Vec2(20, 20), Transform = Affine.Translate(20, 20) };
    }

    static Layer Pictures() => Layer.Default(1, LayerMode.Image) with { ImageMode = ImageEngraveMode.Grayscale, Dpi = 127, Laser = LaserSettings.Blue(40, 100) };

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(135)]
    public void TurnedRowsBurnThePictureWhereItIs(double angle)
    {
        var pic = HalfBlack(Guid.NewGuid());
        var (block, area) = JobBuilder.Rasterize(pic, Pictures(), angle);
        Assert.NotNull(block);
        Assert.Equal(angle, block!.AngleDeg);
        var burned = new List<Vec2>();
        for (int r = 0; r < block.Bitmap.Height; r++)
            for (int c = 0; c < block.Bitmap.Width; c++)
                if (block.Bitmap[c, r]) burned.Add(block.At(c, r));
        // Only the black half, and all of it (about 10 × 20 mm at 0.2 mm apart).
        Assert.All(burned, p => Assert.InRange(p.X, 19.8, 30.2));
        Assert.All(burned, p => Assert.InRange(p.Y, 19.8, 40.2));
        Assert.InRange(burned.Count, 50 * 100 * 0.93, 50 * 100 * 1.07);
        Assert.True(area.Contains(new Vec2(25, 30)));
    }

    [Fact]
    public void TurnedRowsAreWrittenDotByDotWithBothCoordinates()
    {
        var (block, _) = JobBuilder.Rasterize(HalfBlack(Guid.NewGuid()), Pictures(), 45);
        var g = new Job(MachineProfile.F2Ultra, [block!]) { Created = "x" }.Render();
        var dots = g.Split('\n').Where(l => l.StartsWith("G25X", StringComparison.Ordinal)).ToList();
        Assert.True(dots.Count > 4000);
        Assert.All(dots, d => Assert.Contains("Y", d));
        // Straight rows still write only X along a row.
        var (straight, _) = JobBuilder.Rasterize(HalfBlack(Guid.NewGuid()), Pictures());
        var gs = new Job(MachineProfile.F2Ultra, [straight!]) { Created = "x" }.Render();
        Assert.Contains(gs.Split('\n'), l => l.StartsWith("G25X", StringComparison.Ordinal) && !l.Contains('Y'));
    }

    [Fact]
    public void EachPassRunsItsOwnAngle()
    {
        var s = DocState.New();
        var layer = Pictures() with { Laser = LaserSettings.Blue(15, 100).WithPasses(3), AngleStepDeg = 60 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [HalfBlack(layer.Id)]);
        var job = JobBuilder.Build(s, new JobOptions());
        var block = Assert.Single(job.Job.Blocks.OfType<DotBlock>());
        Assert.Equal([0.0, 60, 120], block.PassBlocks!.Select(b => b.AngleDeg));
        Assert.Equal(0, block.ForPass(1).AngleDeg);
        Assert.Equal(120, block.ForPass(3).AngleDeg);
        // Written out pass by pass (three bitmap blocks), not one block looped three times.
        Assert.Equal(3, job.Motion.Split('\n').Count(l => l.Contains("BITMAP HEAD")));

        // Crosshatch: a second block turned by the crosshatch angle.
        var crossed = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { AngleStepDeg = 0, Crosshatch = true, CrossAngleDeg = 90, Laser = l.Laser.WithPasses(1) });
        Assert.Equal([0.0, 90], JobBuilder.Build(crossed, new JobOptions()).Job.Blocks.OfType<DotBlock>().Select(d => d.AngleDeg));
    }

    [Fact]
    public void OffTheFlatBedRowsStayStraight()
    {
        var s = DocState.New();
        var layer = Pictures() with { Laser = LaserSettings.Blue(15, 100).WithPasses(2), AngleStepDeg = 90 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [HalfBlack(layer.Id)]);
        var job = JobBuilder.Build(s, new JobOptions { Mode = new WorkMode.Rotary(1, 1, 100) });
        var block = Assert.Single(job.Job.Blocks.OfType<DotBlock>());
        Assert.Equal(0, block.AngleDeg);
        Assert.Null(block.PassBlocks);
        Assert.Contains(job.Warnings, w => w.Contains("only work on the flat bed"));
    }

    [Fact]
    public void ASectionKeepsOnlyTheTurnedDotsInsideIt()
    {
        var (block, _) = JobBuilder.Rasterize(HalfBlack(Guid.NewGuid()), Pictures(), 30);
        var clip = new Rect2(20, 20, 25, 40);
        var cut = Sections.Cut(block!, clip, new Vec2(1, 0)).Cast<DotBlock>().Single();
        for (int r = 0; r < cut.Bitmap.Height; r++)
            for (int c = 0; c < cut.Bitmap.Width; c++)
                if (cut.Bitmap[c, r]) Assert.InRange(cut.At(c, r).X, 21 - 1e-6, 26 + 1e-6);
    }
}
