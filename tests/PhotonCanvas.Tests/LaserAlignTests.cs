using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class LaserAlignTests
{
    [Fact]
    public void HowFarTheMopaLandsFromTheBlueIsMeasured()
    {
        // The close-up camera: 25 px/mm, turned 30°, round (110, 110).
        const int W = 1400, H = 1100;
        double a = 30 * Math.PI / 180, k = 25;
        var c = new Vec2(110, 112);
        Vec2 Map(Vec2 mm) { var d = mm - c; return new Vec2(W / 2.0 + k * (Math.Cos(a) * d.X - Math.Sin(a) * d.Y), H / 2.0 + k * (Math.Sin(a) * d.X + Math.Cos(a) * d.Y)); }
        var probe = new[] { new Vec2(100, 100), new Vec2(120, 100), new Vec2(120, 120), new Vec2(100, 120) };
        var toCamera = Homography.Fit([.. probe.Select(p => (p, Map(p)))])!.Value;
        var toBed = Homography.Fit([.. probe.Select(p => (Map(p), p))])!.Value;
        var blue = LaserAlign.Grid(new Vec2(110, 103), 6);
        var mopa = LaserAlign.Grid(new Vec2(110, 103 + LaserAlign.Apart), 6);
        // Where they really burned: blue a hair off, the MOPA 0.30 right and 0.15 down more.
        var blueOff = new Vec2(0.05, -0.02);
        var mopaOff = new Vec2(0.35, 0.13);
        var burned = blue.Select(p => p + blueOff).Concat(mopa.Select(p => p + mopaOff)).ToList();
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var mm = toBed.Apply(new Vec2(x + 0.5, y + 0.5));
                double light = burned.Any(d => d.Dist(mm) < 0.6) ? 0.25 : 0.8;
                int i = (y * W + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(light * 255); px[i + 3] = 255;
            }
        var r = LaserAlign.Measure(new RasterImage(W, H, px), toCamera, blue, mopa)!;
        Assert.Equal((9, 9), (r.BlueFound, r.MopaFound));
        Assert.Equal(0.30, r.MopaFromBlue.X, 1);
        Assert.Equal(0.15, r.MopaFromBlue.Y, 1);
        Assert.True(r.SpreadMm < 0.1);
    }

    [Fact]
    public void JobsMoveOnlyTheMopasWorkBack()
    {
        var s = DocState.New();
        var blueLayer = s.Layers[0] with { Laser = LaserSettings.Blue(50, 20) };
        var redLayer = Layer.Default(1, LayerMode.Cut) with { Laser = LaserSettings.Red(50, 500, 200, 60) };
        s = Ops.UpdateLayers(s, new HashSet<Guid> { blueLayer.Id }, _ => blueLayer);
        s = Ops.AddLayer(s, redLayer);
        s = Ops.AddShapes(s, [
            new PathShape { LayerId = blueLayer.Id, Geometry = PathGeometry.Rect(50, 50, 10, 10) },
            new PathShape { LayerId = redLayer.Id, Geometry = PathGeometry.Rect(100, 100, 10, 10) },
        ]);
        var job = JobBuilder.Build(s, new JobOptions { MopaOffset = new Vec2(0.3, 0.15) }).Job;
        var blueStart = job.Blocks.OfType<VectorBlock>().Single(b => b.Laser.Light == LightSource.Blue).Path.Points.Min(p => p.X);
        var redStart = job.Blocks.OfType<VectorBlock>().Single(b => b.Laser.Light == LightSource.Red).Path.Points.Min(p => p.X);
        Assert.Equal(50, blueStart, 6);
        Assert.Equal(99.7, redStart, 6);
    }
}
