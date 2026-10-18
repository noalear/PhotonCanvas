using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using Xunit;

namespace PhotonCanvas.Tests;

public class PointerProbeTests
{
    static RasterImage Photo(Vec2? dot, int seed)
    {
        int w = 160, h = 120;
        var px = new byte[w * h * 4];
        var rnd = new Random(seed);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                // A shiny, patterned surface: stripes and a bright patch, plus noise.
                byte v = (byte)Math.Clamp(90 + 60 * ((x / 8) % 2) + (x > 100 && y < 40 ? 90 : 0) + rnd.Next(-6, 7), 0, 255);
                px[k] = px[k + 1] = px[k + 2] = v;
                px[k + 3] = 255;
                if (dot is { } d)
                {
                    double r2 = (x + 0.5 - d.X) * (x + 0.5 - d.X) + (y + 0.5 - d.Y) * (y + 0.5 - d.Y);
                    double glow = 200 * Math.Exp(-r2 / 8);
                    px[k] = (byte)Math.Clamp(v + glow, 0, 255);
                    px[k + 1] = (byte)Math.Clamp(v + glow * 0.2, 0, 255);
                }
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void TheDotIsFoundOnABusySurface()
    {
        var found = DotFinder.Find(Photo(new Vec2(60.3, 70.6), 1), Photo(null, 2));
        Assert.NotNull(found);
        Assert.True(found!.Value.Centre.Dist(new Vec2(60.3, 70.6)) < 0.5);
        Assert.Null(DotFinder.Find(Photo(null, 3), Photo(null, 4)));
    }

    [Fact]
    public void DepthsComeFromTheCalibratedTracks()
    {
        // The dot moves 3 px right for every mm below focus, plus a little with the aim.
        Vec2 Seen(Vec2 aim, double depth) => new(80 + aim.X * 0.2 + depth * 3, 60 + aim.Y * 0.1);
        var aims = new List<Vec2>();
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) aims.Add(new Vec2(90 + 20 * i, 90 + 20 * j));
        var cal = new PointerCalibration(PointerCalibration.DefaultDepths, aims,
            [.. aims.Select(a => (IReadOnlyList<Vec2?>)[.. PointerCalibration.DefaultDepths.Select(d => (Vec2?)Seen(a, d))])]);
        var aim = new Vec2(103, 117);
        Assert.Equal(2.7, cal.Depth(aim, Seen(aim, 2.7))!.Value, 1);
        Assert.Equal(-5.2, cal.Depth(aim, Seen(aim, -5.2))!.Value, 1);
        // Seen nowhere near the track: not trusted.
        Assert.Null(cal.Depth(aim, new Vec2(10, 10)));
    }

    [Fact]
    public void ABadPointStandsOut()
    {
        var pts = new List<SurfacePoint>();
        for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) pts.Add(new SurfacePoint(new Vec2(i * 10, j * 10), 20 + i * 0.3));
        pts[14] = pts[14] with { Height = pts[14].Height + 3 };
        Assert.Equal([14], SurfaceCheck.Outliers(pts));
    }
}
