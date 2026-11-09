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

    /// <summary>A photo of the bare surface with, optionally, a blue line <paramref name="len"/> px long running along <paramref name="dir"/>.</summary>
    static RasterImage LinePhoto(Vec2? mid, Vec2 dir, double len, int seed, double brighten = 0)
    {
        int w = 160, h = 120;
        var px = new byte[w * h * 4];
        var rnd = new Random(seed);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                byte v = (byte)Math.Clamp(90 + 60 * ((x / 8) % 2) + brighten + rnd.Next(-6, 7), 0, 255);
                px[k] = px[k + 1] = px[k + 2] = v;
                px[k + 3] = 255;
                if (mid is { } c)
                {
                    var p = new Vec2(x + 0.5, y + 0.5) - c;
                    double along = p.Dot(dir), across = p.Cross(dir);
                    double outside = Math.Max(0, Math.Abs(along) - len / 2);
                    double glow = 140 * Math.Exp(-(across * across + outside * outside) / 3);
                    px[k + 2] = (byte)Math.Clamp(v + glow, 0, 255);      // blue
                    px[k + 1] = (byte)Math.Clamp(v + glow * 0.3, 0, 255);
                }
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void ABlueLineIsFoundWholeWithWhichWayItRuns()
    {
        var dir = new Vec2(Math.Cos(0.3), Math.Sin(0.3));
        // The camera's exposure brightened the whole photo a little too.
        var spot = DotFinder.Find(LinePhoto(new Vec2(70.2, 55.7), dir, 30, 1, brighten: 8), LinePhoto(null, dir, 0, 2));
        Assert.NotNull(spot);
        var s = spot!.Value;
        Assert.True(s.Centre.Dist(new Vec2(70.2, 55.7)) < 1, $"centre {s.Centre}");
        Assert.True(Math.Abs(s.Along.Dot(dir)) > 0.99, $"along {s.Along}");
        Assert.True(s.IsLine, $"elongation {s.Elongation}");
        Assert.InRange(s.LengthPx, 22, 45);
        // A round dot isn't a line.
        Assert.False(DotFinder.Find(Photo(new Vec2(60.3, 70.6), 1), Photo(null, 2))!.Value.IsLine);
    }

    [Fact]
    public void ALineIsReadWhereItCrossesTheTrack()
    {
        // The spot moves 3 px right for every mm below focus; the line runs up and down the picture.
        Vec2 Seen(Vec2 aim, double depth) => new(80 + aim.X * 0.2 + depth * 3, 60 + aim.Y * 0.1);
        var aims = new List<Vec2>();
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) aims.Add(new Vec2(90 + 20 * i, 90 + 20 * j));
        var cal = new PointerCalibration(PointerCalibration.DefaultDepths, aims,
            [.. aims.Select(a => (IReadOnlyList<Vec2?>)[.. PointerCalibration.DefaultDepths.Select(d => (Vec2?)Seen(a, d))])]);
        var aim = new Vec2(103, 117);
        // Only part of the line shows, so its middle is 12 px up the line from the track: still read right.
        var line = new PointerSpot(Seen(aim, 2.7) + new Vec2(0, -12), 200, new Vec2(0, 1), 6, 30);
        Assert.Equal(2.7, cal.Depth(aim, line)!.Value, 1);
        // Read by its middle alone, that spot is too far off the track to trust.
        Assert.Null(cal.Depth(aim, line.Centre));
        // A line lying along the track can't tell by crossing: its middle is used.
        var lengthwise = new PointerSpot(Seen(aim, -3.1), 200, new Vec2(1, 0), 6, 30);
        Assert.Equal(-3.1, cal.Depth(aim, lengthwise)!.Value, 1);
        // A crossing far beyond the line's ends isn't trusted.
        Assert.Null(cal.Depth(aim, new PointerSpot(Seen(aim, 1) + new Vec2(0, -60), 200, new Vec2(0, 1), 6, 20)));
        // Nor a long smear whose middle is far from the track, however long it is.
        Assert.Null(cal.Depth(aim, new PointerSpot(Seen(aim, 1) + new Vec2(0, -60), 200, new Vec2(0, 1), 20, 300)));
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
        // Past the ends of the track (further from the focus than calibrated): not read as the end depth.
        Assert.Equal(8, cal.Depth(aim, Seen(aim, 8.2))!.Value, 1);
        Assert.Null(cal.Depth(aim, Seen(aim, 8.6)));
        Assert.Null(cal.Depth(aim, Seen(aim, -9)));
        // Calibrated over X/Y 90–130: read up to 5 mm outside, not beyond (no guessing 80 mm out).
        Assert.True(cal.Covers(new Vec2(134, 110)));
        Assert.False(cal.Covers(new Vec2(140, 110)));
        var far = new Vec2(207.8, 110);
        Assert.Null(cal.Depth(far, Seen(far, 1)));
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
