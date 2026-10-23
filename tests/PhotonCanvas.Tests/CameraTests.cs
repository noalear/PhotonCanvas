using SkiaSharp;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Tracing;

namespace PhotonCanvas.Tests;

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
        // Beyond the range the trend continues (linear extrapolation).
        var far = CalibrationSet.For([a, b], 40)!;
        Assert.True(far.Map.ToImage(p).Dist(Vec2.Lerp(a.ToImage(p), b.ToImage(p), 1.5)) < 1e-12);
        Assert.False(far.Exact);
        Assert.True(CalibrationSet.For([a, b], 33)!.Exact); // within the trusted margin
        Assert.False(CalibrationSet.For([a], 20)!.Exact);
    }

    [Fact]
    public void ThreeHeightsFollowACurvedTrendExactly()
    {
        // A camera whose picture position moves quadratically with height.
        IImageMap Cam(double h) => new FakeCamera(-0.08 - 0.0004 * h * h);
        var cals = new[] { 5.0, 20, 40 }.Select(h => CameraCalibration.Fit(Points(Cam(h)), h)).ToList();
        var mid = CalibrationSet.For(cals, 12)!;
        Assert.True(mid.Exact);
        // Quadratic data → the 3-point fit reproduces the calibration fits' trend at any height in range.
        foreach (var mm in new Vec2[] { new(10, 10), new(110, 110), new(210, 200) })
        {
            var expect = Quadratic(cals, 12, mm);
            Assert.True(mid.Map.ToImage(mm).Dist(expect) < 1e-9);
        }
    }

    static Vec2 Quadratic(IReadOnlyList<CameraCalibration> c, double d, Vec2 mm)
    {
        // Lagrange through the three calibrations (same as a least-squares quadratic for 3 points).
        double d0 = c[0].DistanceMm, d1 = c[1].DistanceMm, d2 = c[2].DistanceMm;
        double l0 = (d - d1) * (d - d2) / ((d0 - d1) * (d0 - d2));
        double l1 = (d - d0) * (d - d2) / ((d1 - d0) * (d1 - d2));
        double l2 = (d - d0) * (d - d1) / ((d2 - d0) * (d2 - d1));
        return c[0].ToImage(mm) * l0 + c[1].ToImage(mm) * l1 + c[2].ToImage(mm) * l2;
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
        var dir = Path.Combine(Path.GetTempPath(), "photoncanvas-test-" + Guid.NewGuid());
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

public class ShapeFitTests
{
    // An "F": not symmetric, so a wrong turn or flip can't score well.
    static readonly Vec2[] F =
    [
        new(80, 60), new(112, 60), new(112, 68), new(90, 68), new(90, 76), new(106, 76),
        new(106, 84), new(90, 84), new(90, 100), new(80, 100), new(80, 60),
    ];

    /// <summary>A straightened "photo": dark noisy bed with slats, and the light piece placed by <paramref name="truth"/>.</summary>
    static (GrayImage Img, Rect2 Area, double MmPerPx) Photo(FitResult truth, int seed = 1)
    {
        var area = new Rect2(40, 30, 160, 130);
        const double mmPerPx = 0.15;
        int w = (int)Math.Round(area.Width / mmPerPx), h = (int)Math.Round(area.Height / mmPerPx);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(70, 70, 70));
            using var slat = new SKPaint { Color = new SKColor(35, 35, 35), IsAntialias = true };
            for (double x = area.MinX; x < area.MaxX; x += 7)
                c.DrawRect((float)((x - area.MinX) / mmPerPx), 0, (float)(1 / mmPerPx), h, slat);
            using var path = new SKPath();
            for (int i = 0; i < F.Length; i++)
            {
                var p = truth.Apply(F[i]);
                float px = (float)((p.X - area.MinX) / mmPerPx), py = (float)((p.Y - area.MinY) / mmPerPx);
                if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
            }
            path.Close();
            using var wood = new SKPaint { Color = new SKColor(200, 175, 140), IsAntialias = true };
            c.DrawPath(path, wood);
        }
        var g = CameraWarp.ToGray(bmp);
        var rng = new Random(seed);
        for (int i = 0; i < g.Pixels.Length; i++) g.Pixels[i] = (byte)Math.Clamp(g.Pixels[i] + rng.Next(-12, 13), 0, 255);
        return (g, area, mmPerPx);
    }

    static Vec2 Center => new(96, 80);

    [Fact]
    public void FindsAPieceThatWasMovedTurnedAndResized()
    {
        var truth = new FitResult(Center, new Vec2(5.5, -3.2), 12, 1.06, 1, 0);
        var (img, area, mpp) = Photo(truth);
        var fit = ShapeFit.Fit(img, area, mpp, [F], new FitOptions { MaxRotationDeg = 30, MaxScaleChange = 0.15, SearchMm = 20 });
        Assert.InRange(fit.AngleDeg, 11.5, 12.5);
        Assert.InRange(fit.Scale, 1.05, 1.07);
        // Every corner of the outline lands within 0.25 mm of the real piece's corner.
        Assert.All(F, p => Assert.True(fit.Apply(p).Dist(truth.Apply(p)) < 0.25, $"{p}: {fit.Apply(p)} vs {truth.Apply(p)}"));
        Assert.True(fit.OnEdge > 0.8, $"on edge {fit.OnEdge:P0}");
    }

    [Fact]
    public void AnyAngleFindsAPieceTurnedRoundWithoutResizing()
    {
        var truth = new FitResult(Center, new Vec2(-4, 6), 137, 1, 1, 0);
        var (img, area, mpp) = Photo(truth, seed: 2);
        var fit = ShapeFit.Fit(img, area, mpp, [F], new FitOptions { MaxRotationDeg = 180, MaxScaleChange = 0, SearchMm = 20 });
        Assert.InRange(fit.AngleDeg, 136, 138);
        Assert.Equal(1, fit.Scale, 3);
        Assert.All(F, p => Assert.True(fit.Apply(p).Dist(truth.Apply(p)) < 0.3));
    }

    /// <summary>
    /// Split onto pieces: board 2 lies in the work area, board 1 (done) butted against its left edge,
    /// mostly outside the camera's view. Both are 2.5 mm off and turned 0.8° from where the design has
    /// them; the fit uses all of board 2 and the strip of board 1 it can see.
    /// </summary>
    [Fact]
    public void LinesUpABoardWithTheOneBesideIt()
    {
        var field = new Rect2(0, 0, 220, 140);
        const double mpp = 0.2;
        IReadOnlyList<Vec2> Board(double x, double y) => [new(x, y), new(x + 200, y), new(x + 200, y + 75), new(x, y + 75)];
        var two = Board(15, 30);
        var one = Board(-185, 30);
        var truth = new FitResult(new Vec2(115, 67.5), new Vec2(2.5, -1.6), 0.8, 1, 1, 0);
        int w = (int)Math.Round(field.Width / mpp), h = (int)Math.Round(field.Height / mpp);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(60, 60, 60));
            using var wood = new SKPaint { Color = new SKColor(205, 180, 140), IsAntialias = true };
            using var seam = new SKPaint { Color = new SKColor(120, 105, 80), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(0.3 / mpp) };
            foreach (var board in new[] { one, two })
            {
                using var path = new SKPath();
                for (int i = 0; i < board.Count; i++)
                {
                    var p = truth.Apply(board[i]);
                    float px = (float)(p.X / mpp), py = (float)(p.Y / mpp);
                    if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
                }
                path.Close();
                c.DrawPath(path, wood);
                c.DrawPath(path, seam);
            }
        }
        var img = CameraWarp.ToGray(bmp);
        var seen = ShapeFit.InView([two, one], field.Inflate(-2));
        // Board 1: only the strip inside the view (its right end), as open runs.
        Assert.All(seen.Skip(1), run => Assert.All(run, p => Assert.InRange(p.X, 1.999, 218.001)));
        var fit = ShapeFit.Fit(img, field, mpp, seen, new FitOptions { MaxRotationDeg = 20, MaxScaleChange = 0, SearchMm = 30 });
        Assert.True(fit.OnEdge > 0.8, $"on edge {fit.OnEdge:P0}");
        Assert.All(two, p => Assert.True(fit.Apply(p).Dist(truth.Apply(p)) < 0.3, $"{p}: {fit.Apply(p)} vs {truth.Apply(p)}"));
    }

    [Fact]
    public void ReportsAPoorMatchWhenThePieceIsntThere()
    {
        var (img, area, mpp) = Photo(new FitResult(Center, new Vec2(0, 0), 0, 1, 1, 0));
        // A circle outline doesn't match the F's straight edges.
        var circle = Enumerable.Range(0, 73).Select(i => new Vec2(96 + 18 * Math.Cos(i * Math.PI / 36), 80 + 18 * Math.Sin(i * Math.PI / 36))).ToList();
        var fit = ShapeFit.Fit(img, area, mpp, [circle], new FitOptions { MaxRotationDeg = 0, MaxScaleChange = 0 });
        Assert.True(fit.OnEdge < 0.5, $"on edge {fit.OnEdge:P0}");
    }

    [Fact]
    public void DistanceFieldMatchesBruteForce()
    {
        int w = 23, h = 17;
        var rng = new Random(5);
        var edge = new bool[w * h];
        for (int i = 0; i < 12; i++) edge[rng.Next(w * h)] = true;
        var df = DistanceField.Build(edge, w, h);
        var area = new Rect2(0, 0, w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double best = double.MaxValue;
                for (int j = 0; j < w * h; j++)
                    if (edge[j]) best = Math.Min(best, Math.Sqrt(Math.Pow(j % w - x, 2) + Math.Pow(j / w - y, 2)));
                Assert.Equal(best, df.DistanceMm(x + 0.5, y + 0.5, area, 1), 4);
                int n = df.NearestEdge(x + 0.5, y + 0.5, area, 1);
                Assert.True(edge[n]);
                Assert.Equal(best, Math.Sqrt(Math.Pow(n % w - x, 2) + Math.Pow(n / w - y, 2)), 4);
            }
    }
}

public class CameraDrawingTests
{
    static readonly Rect2 Area = new(0, 0, 100, 80);
    const double Mpp = 0.2;

    /// <summary>A straightened "photo": dark noisy bed, with whatever <paramref name="draw"/> paints (in mm).</summary>
    static SKBitmap Photo(Action<SKCanvas> draw, int seed = 3)
    {
        int w = (int)(Area.Width / Mpp), h = (int)(Area.Height / Mpp);
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(60, 62, 66));
            c.Scale((float)(1 / Mpp));
            draw(c);
        }
        var rng = new Random(seed);
        var px = bmp.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            int n = rng.Next(-10, 11);
            var p = px[i];
            px[i] = new SKColor((byte)Math.Clamp(p.Red + n, 0, 255), (byte)Math.Clamp(p.Green + n, 0, 255), (byte)Math.Clamp(p.Blue + n, 0, 255), 255);
        }
        bmp.Pixels = px;
        return bmp;
    }

    static readonly SKPaint Wood = new() { Color = new SKColor(205, 178, 140), IsAntialias = true };

    [Fact]
    public void MagneticPathHugsTheEdge()
    {
        using var bmp = Photo(c => c.DrawRect(20, 20, 50, 30, Wood)); // top edge at y = 20
        var wire = LiveWire.Build(CameraWarp.ToGray(bmp), Area, Mpp);
        // A click just off the edge snaps onto the edge, close by.
        var a = wire.Snap(new Vec2(20.6, 19.3), 1.5);
        Assert.True(Math.Abs(a.Y - 20) < 0.4 && a.Dist(new Vec2(20.6, 19.3)) < 1.2, $"{a}");
        var tree = wire.Grow(a, 60);
        // Cursor near the far corner: the path follows the top edge, not a wandering line.
        var path = tree.PathTo(wire.Snap(new Vec2(69.5, 20.7), 1.5))!;
        Assert.NotNull(path);
        Assert.All(path, p => Assert.True(Math.Abs(p.Y - 20) < 0.5 || Math.Abs(p.X - 70) < 0.5, $"{p} is off the top edge"));
        // To the opposite corner it goes round the edges rather than across the middle.
        var round = tree.PathTo(new Vec2(70, 50))!;
        Assert.All(round, p => Assert.True(
            Math.Abs(p.Y - 20) < 0.6 || Math.Abs(p.Y - 50) < 0.6 || Math.Abs(p.X - 20) < 0.6 || Math.Abs(p.X - 70) < 0.6, $"{p} is inside the piece"));
        Assert.True(LiveWire.Simplify(path, 0.25).Count <= 4, $"{LiveWire.Simplify(path, 0.25).Count} points");
    }

    static RasterImage ToRaster(SKBitmap bmp)
    {
        var rgba = new byte[bmp.Width * bmp.Height * 4];
        var px = bmp.Pixels;
        for (int i = 0; i < px.Length; i++) { rgba[i * 4] = px[i].Red; rgba[i * 4 + 1] = px[i].Green; rgba[i * 4 + 2] = px[i].Blue; rgba[i * 4 + 3] = 255; }
        return new RasterImage(bmp.Width, bmp.Height, rgba);
    }

    [Fact]
    public void WandOutlinesAPieceWithItsHole()
    {
        // An "O": a 40 mm ring with a 20 mm hole, plus a small dark speck (≈1 mm²) on the wood.
        using var bmp = Photo(c =>
        {
            using var ring = new SKPath { FillType = SKPathFillType.EvenOdd };
            ring.AddCircle(50, 40, 20);
            ring.AddCircle(50, 40, 10);
            c.DrawPath(ring, Wood);
            using var speck = new SKPaint { Color = new SKColor(70, 50, 40) };
            c.DrawRect(64, 39.5f, 1, 1, speck);
        });
        var w = CameraPick.Prepare(ToRaster(bmp));
        var click = new Vec2(50, 25); // on the ring
        var g = CameraPick.Outline(w, Area, Mpp, click, new PickOptions());
        Assert.Equal(2, g.Subpaths.Length); // outside + hole; the speck is ignored
        var b = g.Bounds(0.05);
        Assert.InRange(b.Width, 39.4, 40.6);
        Assert.InRange(b.Center.X, 49.7, 50.3);
        Assert.InRange(b.Center.Y, 39.7, 40.3);
        // Keep specks: the speck becomes a third outline.
        var detailed = CameraPick.Outline(w, Area, Mpp, click, new PickOptions { IgnoreBelowMm2 = 0.2, Smoothness = 0 });
        Assert.Equal(3, detailed.Subpaths.Length);
        // Clicking the bed picks the bed, not the ring.
        Assert.True(CameraPick.Outline(w, Area, Mpp, new Vec2(5, 5), new PickOptions()).Bounds(0.05).Width > 90);
    }

    [Fact]
    public void SmoothnessSimplifiesTheOutline()
    {
        using var bmp = Photo(c => c.DrawCircle(50, 40, 20, Wood));
        var w = CameraPick.Prepare(ToRaster(bmp));
        int Segs(double smooth) => CameraPick.Outline(w, Area, Mpp, new Vec2(50, 40), new PickOptions { Smoothness = smooth, CornerAngle = 40 }).Subpaths.Sum(s => s.Segs.Length);
        Assert.True(Segs(8) < Segs(0), $"{Segs(8)} vs {Segs(0)}");
    }

    [Fact]
    public void ClosedMagneticLoopBecomesASmoothShape()
    {
        var loop = new List<Vec2>();
        for (int i = 0; i <= 200; i++) loop.Add(new Vec2(50 + 15 * Math.Cos(i * Math.PI / 100), 40 + 15 * Math.Sin(i * Math.PI / 100)));
        var g = CameraPick.FitLoop(loop, Area, Mpp);
        Assert.True(g.Subpaths[0].Closed);
        Assert.InRange(g.Bounds(0.05).Width, 29.6, 30.4);
        Assert.True(g.Subpaths[0].Segs.Length < 20);
    }
}
