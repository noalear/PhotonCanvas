using SkiaSharp;
using YTool.Core;
using YTool.Core.Camera;

namespace YTool.Tests;

public class CameraTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    /// <summary>A plausible overview camera: perspective plus barrel distortion, normalised image coords.</summary>
    sealed class FakeCamera(double k1 = -0.08) : IImageMap
    {
        static readonly Homography H = new(0.0031, 0.0002, 0.33, -0.0001, 0.0040, 0.08, 0.00012, 0.00030);

        public Vec2 ToImage(Vec2 mm)
        {
            var p = H.Apply(mm);
            var d = p - new Vec2(0.5, 0.5);
            double r2 = d.X * d.X + d.Y * d.Y;
            return new Vec2(0.5, 0.5) + d * (1 + k1 * r2);
        }
    }

    static List<CalibrationPoint> Points(IImageMap cam, double noise = 0, int seed = 1)
    {
        var rng = new Random(seed);
        return CalibrationMarks.Grid(M).Select(mm => new CalibrationPoint(mm,
            cam.ToImage(mm) + new Vec2((rng.NextDouble() - 0.5) * noise, (rng.NextDouble() - 0.5) * noise))).ToList();
    }

    [Fact]
    public void HomographyRecoversPerspectiveExactly()
    {
        var truth = new Homography(1.2, 0.1, 30, -0.05, 0.9, 12, 0.0004, -0.0002);
        var pairs = CalibrationMarks.Grid(M).Select(p => (p, truth.Apply(p))).ToList();
        var fit = Homography.Fit(pairs)!.Value;
        foreach (var (p, q) in pairs) Assert.True(fit.Apply(p).Dist(q) < 1e-6);
        Assert.Null(Homography.Fit([(new(0, 0), new(0, 0)), (new(1, 1), new(1, 1)), (new(2, 2), new(2, 2)), (new(3, 3), new(3, 3))]));
    }

    [Fact]
    public void CalibrationAbsorbsLensDistortion()
    {
        var cam = new FakeCamera();
        var cal = CameraCalibration.Fit(Points(cam), 21);
        Assert.True(cal.RmsMm < 0.05, $"rms {cal.RmsMm}");
        // Between the marks and out to the field edge, the map stays accurate.
        double worst = 0;
        for (double x = 0; x <= 220; x += 11)
            for (double y = 0; y <= 220; y += 11)
            {
                var err = cal.ToImage(new(x, y)) - cam.ToImage(new(x, y));
                // ~0.004 normalised units per mm in this fake camera.
                worst = Math.Max(worst, err.Length / 0.004);
            }
        Assert.True(worst < 0.3, $"worst {worst:0.###} mm");
    }

    [Fact]
    public void ReportsABadlyClickedMark()
    {
        var pts = Points(new FakeCamera());
        pts[12] = pts[12] with { Image = pts[12].Image + new Vec2(0.02, 0) }; // ≈5 mm off
        var cal = CameraCalibration.Fit(pts, 21);
        Assert.Equal(12, cal.ErrorsMm.Select((e, i) => (e, i)).MaxBy(t => t.e).i);
        Assert.True(cal.MaxMm > 1);
    }

    [Fact]
    public void FourPointsGiveAPerspectiveOnlyFit()
    {
        var cam = new FakeCamera(0);
        var grid = CalibrationMarks.Grid(M);
        var pts = CalibrationMarks.Corners.Select(i => new CalibrationPoint(grid[i], cam.ToImage(grid[i]))).ToList();
        var cal = CameraCalibration.Fit(pts, 10);
        Assert.True(cal.ToImage(new(110, 110)).Dist(cam.ToImage(new(110, 110))) < 1e-6);
        Assert.Throws<CalibrationException>(() => CameraCalibration.Fit(pts.Take(3).ToList(), 10));
    }

    [Fact]
    public void ChoosesOrBlendsByDistance()
    {
        var a = CameraCalibration.Fit(Points(new FakeCamera()), 10);
        var b = CameraCalibration.Fit(Points(new FakeCamera(-0.12)), 30);
        Assert.Null(CalibrationSet.For([], 10));
        var exact = CalibrationSet.For([a, b], 10.1)!;
        Assert.Same(a, exact.Map);
        var mid = CalibrationSet.For([a, b], 20)!;
        var p = new Vec2(200, 200);
        Assert.True(mid.Map.ToImage(p).Dist(Vec2.Lerp(a.ToImage(p), b.ToImage(p), 0.5)) < 1e-12);
        var far = CalibrationSet.For([a, b], 40)!;
        Assert.Same(b, far.Map);
        Assert.False(far.Exact);
    }

    [Fact]
    public void FindsADarkDotNearItsGuessAndIgnoresBlankAreas()
    {
        int w = 200, h = 160;
        var px = new byte[w * h];
        var rng = new Random(3);
        var centre = new Vec2(83.4, 71.7);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = new Vec2(x, y).Dist(centre);
                double v = 200 + rng.Next(-12, 12) - (d < 9 ? 140 * Math.Clamp(9.5 - d, 0, 1) : 0);
                px[y * w + x] = (byte)Math.Clamp(v, 0, 255);
            }
        var img = new GrayImage(w, h, px);
        var found = MarkFinder.Find(img, centre + new Vec2(6, -5), 18);
        Assert.NotNull(found);
        Assert.True(found!.Value.Dist(centre) < 0.4, $"off by {found.Value.Dist(centre):0.###}");
        Assert.Null(MarkFinder.Find(img, new(170, 20), 15));
    }

    [Fact]
    public void WarpPutsMachinePointsWhereTheCameraSawThem()
    {
        // Source: a white image with a black pixel block where the camera sees machine (50, 150).
        var cam = new FakeCamera();
        var src = new SKBitmap(new SKImageInfo(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul));
        src.Erase(SKColors.White);
        var target = cam.ToImage(new(50, 150));
        int tx = (int)(target.X * 800), ty = (int)(target.Y * 600);
        for (int y = ty - 3; y <= ty + 3; y++)
            for (int x = tx - 3; x <= tx + 3; x++) src.SetPixel(x, y, SKColors.Black);
        using var dst = CameraWarp.Warp(src, cam, new Rect2(0, 0, 220, 220), 0.5);
        Assert.Equal(440, dst.Width);
        var c = dst.GetPixel(100, 300); // (50, 150) mm at 0.5 mm/px
        Assert.True(c.Red < 80, $"expected dark at the warped mark, got {c}");
        Assert.True(dst.GetPixel(300, 100).Red > 200);
    }

    [Fact]
    public void CalibrationJobIsValidAndCoversTheField()
    {
        var big = CalibrationMarks.Build(M, LaserSettings.Blue(30, 150), new MarkSpec(MarkStyle.Dot, 4)).Render();
        Assert.Contains("G0X12Y10", big); // outer ring of the top-left 4 mm dot starts at (10 + 2, 10)
        var small = CalibrationMarks.Build(M, LaserSettings.Blue(30, 150), new MarkSpec(MarkStyle.Dot, 1)).Render();
        Assert.Contains("G0X10.5Y10", small);
        var cross = CalibrationMarks.Build(M, LaserSettings.Blue(30, 150), new MarkSpec(MarkStyle.Cross, 4)).Render();
        Assert.Contains("G0X8Y10", cross);      // middle stroke of the horizontal arm
        Assert.Contains("G0X10Y8", cross);      // middle stroke of the vertical arm
        Assert.Contains("G0X8Y9.85", cross);    // a neighbouring stroke, 0.15 mm off
        Assert.Equal(25, CalibrationMarks.Grid(M).Count);
        Assert.Equal(new Vec2(210, 210), CalibrationMarks.Grid(M)[CalibrationMarks.Corners[2]]);
    }

    /// <summary>Render marks as the camera would see them: ~6.5 px/mm, blurred, noisy wood.</summary>
    static (GrayImage Img, Vec2 Centre) Photo(MarkSpec spec, Vec2 centrePx)
    {
        const double PxPerMm = 6.5;
        int w = 160, h = 160;
        var px = new byte[w * h];
        var rng = new Random(9);
        var lines = CalibrationMarks.Mark(new Vec2(0, 0), spec).ToList();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = new Vec2((x - centrePx.X) / PxPerMm, (y - centrePx.Y) / PxPerMm);
                // Burn width ~0.2 mm, softened by the lens.
                double d = lines.Min(l => DistToPath(mm, l));
                double ink = Math.Clamp(1 - (d - 0.1) / 0.12, 0, 1);
                px[y * w + x] = (byte)Math.Clamp(205 + rng.Next(-14, 14) - 130 * ink, 0, 255);
            }
        return (new GrayImage(w, h, px), centrePx);
    }

    static double DistToPath(Vec2 p, Polyline l)
    {
        var e = l.EmittedPoints();
        double best = double.PositiveInfinity;
        for (int i = 1; i < e.Count; i++) best = Math.Min(best, Geom.SegmentDistance(p, e[i - 1], e[i]));
        return best;
    }

    [Theory]
    [InlineData(MarkStyle.Dot, 4)]
    [InlineData(MarkStyle.Dot, 2)]
    [InlineData(MarkStyle.Dot, 1)]
    [InlineData(MarkStyle.Cross, 4)]
    [InlineData(MarkStyle.Cross, 2)]
    public void FindsEveryMarkStyleFromASloppyClick(MarkStyle style, double size)
    {
        var spec = new MarkSpec(style, size);
        var (img, centre) = Photo(spec, new Vec2(80.3, 77.6));
        // A click 1.5 mm off, searched over the radius the app uses.
        var found = MarkFinder.Find(img, centre + new Vec2(1.5 * 6.5 * 0.7, -1.5 * 6.5 * 0.7), spec.SearchRadiusMm * 6.5);
        Assert.NotNull(found);
        Assert.True(found!.Value.Dist(centre) < 0.5, $"{style} {size} mm: off by {found.Value.Dist(centre):0.##} px");
    }

    [Fact]
    public void StoreRoundTripsAndReplacesSameDistance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ytool-test-" + Guid.NewGuid());
        var path = Path.Combine(dir, "camera.json");
        try
        {
            var a = CameraCalibration.Fit(Points(new FakeCamera()), 21);
            var b = CameraCalibration.Fit(Points(new FakeCamera(-0.1)), 21.1);
            var list = CalibrationStore.With(CalibrationStore.With([], a), b);
            Assert.Single(list);
            CalibrationStore.Save(path, list);
            var back = CalibrationStore.Load(path);
            Assert.Single(back);
            Assert.True(back[0].ToImage(new(42, 99)).Dist(b.ToImage(new(42, 99))) < 1e-9);
            Assert.Empty(CalibrationStore.Load(Path.Combine(dir, "missing.json")));
            File.WriteAllText(path, "{ not json");
            Assert.Empty(CalibrationStore.Load(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
