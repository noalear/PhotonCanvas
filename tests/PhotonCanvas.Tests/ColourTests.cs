using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using Xunit;

namespace PhotonCanvas.Tests;

public class ColourTests
{
    [Fact]
    public void SrgbToLabMatchesReferenceValues()
    {
        var white = ColourScience.SrgbToLab(255, 255, 255);
        Assert.Equal(100, white.L, 0.01);
        Assert.Equal(0, white.A, 0.01);
        Assert.Equal(0, white.B, 0.01);
        var red = ColourScience.SrgbToLab(255, 0, 0);
        Assert.Equal(53.24, red.L, 0.05);
        Assert.Equal(80.09, red.A, 0.1);
        Assert.Equal(67.20, red.B, 0.1);
        // And back.
        Assert.Equal(((byte)255, (byte)0, (byte)0), ColourScience.LabToSrgb(red));
        var teal = ColourScience.SrgbToLab(40, 140, 150);
        Assert.Equal(((byte)40, (byte)140, (byte)150), ColourScience.LabToSrgb(teal));
    }

    [Fact]
    public void DeltaE2000MatchesSharmasTestData()
    {
        // Sharma, Wu & Dalal (2005), pairs 1, 7 and 17.
        Assert.Equal(2.0425, ColourScience.DeltaE2000(new(50, 2.6772, -79.7751), new(50, 0, -82.7485)), 3);
        Assert.Equal(2.3669, ColourScience.DeltaE2000(new(50, 0, 0), new(50, -1, 2)), 3);
        Assert.Equal(27.1492, ColourScience.DeltaE2000(new(50, 2.5, 0), new(73, 25, -18)), 3);
        Assert.Equal(0, ColourScience.DeltaE2000(new(60, 10, 10), new(60, 10, 10)), 9);
    }

    [Fact]
    public void ColourTestGridRunsFromLeastToMostEnergyWithSettingsTheSourceCanDo()
    {
        var o = new ColourTestOptions
        {
            Across = ColourParam.Speed, AcrossMin = 400, AcrossMax = 1600, AcrossSteps = 4,
            Down = ColourParam.PulseWidthNs, DownMin = 2, DownMax = 500, DownSteps = 4,
            FrequencyKhz = 3500,
        };
        var (s, added) = ColourTest.Add(DocState.New(), o);
        var cells = ColourTest.Cells(s);
        Assert.Equal(16, cells.Count);
        Assert.All(cells, c =>
        {
            Assert.Equal(LightSource.Red, c.Layer.Laser.Light);
            Assert.Equal(LayerMode.Fill, c.Layer.Mode);
            var lim = MachineProfile.F2Ultra.MopaLimitFor(c.Layer.Laser.PulseWidthNs);
            Assert.NotNull(lim); // a pulse width the source really has
            Assert.True(c.Layer.Laser.FrequencyKhz <= lim!.Value.MaxKhz);
        });
        // Rows are the pulse widths, columns the speeds.
        Assert.Equal(1600, cells.Single(c => c.Row == 0 && c.Col == 3).Layer.Laser.Speed);
        Assert.Equal(2, cells.Single(c => c.Row == 0 && c.Col == 0).Layer.Laser.PulseWidthNs);
        Assert.Equal(500, cells.Single(c => c.Row == 3 && c.Col == 0).Layer.Laser.PulseWidthNs);
        // Run order (layer order) never goes down in energy.
        var order = s.Layers.Where(l => l.Name.StartsWith(ColourTest.LayerPrefix)).Select(l => ColourTest.Energy(l.Laser, l.LinesPerCm)).ToList();
        Assert.Equal(order.Order(), order);
        Assert.Contains(s.Layers, l => l.Name == ColourTest.LabelLayerName);
        var b = ColourTest.Bounds(DocState.New(), o);
        Assert.True(b.MaxX < 220 && b.MaxY < 220);
        JobBuilder.Build(s, new JobOptions()); // builds without complaint
    }

    [Fact]
    public void MeasuresEachSquareFromAPictureOfTheBurnedTest()
    {
        var o = new ColourTestOptions { AcrossSteps = 3, DownSteps = 2, CellMm = 6, GapMm = 3 };
        var (s, _) = ColourTest.Add(DocState.New(), o);
        var cells = ColourTest.Cells(s);
        Assert.Equal(6, cells.Count);

        // A fake scan: 8 px per mm, slightly rotated and shifted; each square painted its own colour.
        var mmToPx = Homography.Fit([.. new[] { new Vec2(0, 0), new(100, 0), new(100, 100), new(0, 100) }
            .Select(p => (p, new Vec2(30 + p.X * 8 - p.Y * 0.4, 20 + p.Y * 8 + p.X * 0.4)))])!.Value;
        int w = 900, h = 700;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = 190; rgba[i * 4 + 1] = 190; rgba[i * 4 + 2] = 195; rgba[i * 4 + 3] = 255; }
        (byte, byte, byte)[] colours = [(200, 160, 60), (120, 70, 40), (110, 50, 130), (40, 70, 170), (80, 150, 200), (20, 20, 25)];
        var pxToMm = Homography.Fit([.. new[] { new Vec2(0, 0), new(100, 0), new(100, 100), new(0, 100) }
            .Select(p => (mmToPx.Apply(p), p))])!.Value;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = pxToMm.Apply(new Vec2(x + 0.5, y + 0.5));
                int k = cells.ToList().FindIndex(c => c.Bounds.Contains(mm));
                if (k < 0) continue;
                var (r, g, b) = colours[k];
                int i = (y * w + x) * 4;
                // The last-but-one square is blotchy: its left half came out much lighter.
                bool blotch = k == 4 && mm.X < cells[k].Bounds.Center.X;
                rgba[i] = (byte)(blotch ? 230 : r); rgba[i + 1] = (byte)(blotch ? 230 : g); rgba[i + 2] = (byte)(blotch ? 230 : b);
            }
        var img = new RasterImage(w, h, rgba);
        var measured = ColourMeasure.Measure(img, mmToPx, cells);
        Assert.Equal(6, measured.Count);
        for (int k = 0; k < 6; k++)
        {
            if (k == 4) continue;
            var (r, g, b) = colours[k];
            Assert.True(ColourScience.DeltaE2000(measured[k].Colour, ColourScience.SrgbToLab(r, g, b)) < 0.5, $"square {k}");
            Assert.True(measured[k].Spread < 0.5);
            Assert.True(measured[k].Use);
        }
        Assert.True(measured[4].Spread > ColourMeasure.BlotchySpread);
        Assert.False(measured[4].Use);
        Assert.Equal(cells[2].Layer.Laser.Speed, measured[2].Settings.Laser.Speed);

        // Distinct colours: the five even squares are all clearly different.
        Assert.Equal(5, ColourMeasure.Distinct(measured).Count);
        // A near-duplicate isn't counted twice.
        var dup = measured[0] with { Row = 9, L = measured[0].L + 0.5 };
        Assert.Equal(5, ColourMeasure.Distinct([.. measured, dup]).Count);
    }

    [Fact]
    public void WhiteBalanceMakesTheReferenceNeutral()
    {
        // A warm-lit grey card: R high, B low.
        int w = 20, h = 20;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = 200; rgba[i * 4 + 1] = 180; rgba[i * 4 + 2] = 150; rgba[i * 4 + 3] = 255; }
        var gains = ColourMeasure.WhiteBalance(new RasterImage(w, h, rgba), [new(2, 2), new(18, 2), new(18, 18), new(2, 18)]);
        Assert.NotNull(gains);
        var (gr, gg, gb) = gains!.Value;
        double r = ColourScience.ToLinear(200 / 255.0) * gr, g = ColourScience.ToLinear(180 / 255.0) * gg, b = ColourScience.ToLinear(150 / 255.0) * gb;
        var lab = ColourScience.LinearRgbToLab(r, g, b);
        Assert.True(lab.Chroma < 0.5);
    }
}

public class MopaLimitTests
{
    static MopaLimit At(int ns) => MachineProfile.F2Ultra.MopaLimitFor(ns)!.Value;

    [Fact]
    public void OnReflectiveMaterialLongPulsesSkipTheRangeTheSourceIsProtectedFrom()
    {
        // 200 ns: full power from 65 kHz; 48–64 kHz kept out of on reflective work (as the stock software does).
        var l = At(200);
        Assert.True(l.InGap(48));
        Assert.True(l.InGap(64));
        Assert.False(l.InGap(47));
        Assert.False(l.InGap(65));
        Assert.Equal(65, l.Safe(50, reflective: true));
        Assert.Equal(50, l.Safe(50)); // everywhere else the whole range is there
        Assert.Equal(47, l.Safe(47, reflective: true));
        Assert.Equal(1000, l.Safe(5000)); // and never above its maximum
        Assert.Equal(1, l.Safe(0));
        // Short pulses have no such range.
        Assert.Null(At(6).GapFromKhz);
        Assert.Equal(300, At(6).Safe(300));

        var ex = Assert.Throws<JobException>(() => LaserSettings.Red(50, 500, 200, 55).Validate(MachineProfile.F2Ultra, reflective: true));
        Assert.Contains("48–64 kHz", ex.Message);
        LaserSettings.Red(50, 500, 200, 55).Validate(MachineProfile.F2Ultra);
        LaserSettings.Red(50, 500, 200, 65).Validate(MachineProfile.F2Ultra);
        LaserSettings.Red(50, 500, 200, 40).Validate(MachineProfile.F2Ultra);
    }

    [Fact]
    public void BelowFullPowerFrequencyTheSourceGivesLess()
    {
        var l = At(6); // full power from 850 kHz
        Assert.Equal(1, l.PowerAvailable(850));
        Assert.Equal(1, l.PowerAvailable(2000));
        Assert.Equal(300 / 850.0, l.PowerAvailable(300), 9);
    }

    [Fact]
    public void ImportedSettingsAreBroughtWithinTheSourcesLimits()
    {
        var s = new PhotonCanvas.Doc.Materials.ProcessSettings
        {
            Laser = LaserSettings.Red(50, 500, 190, 5000), // not a real pulse width, and above its maximum
            LinesPerCm = 99999,
        }.Clamped(MachineProfile.F2Ultra);
        Assert.Equal(200, s.Laser.PulseWidthNs);
        Assert.Equal(1000, s.Laser.FrequencyKhz);
        Assert.Equal(FillBlock.MaxLinesPerCm, s.LinesPerCm);
    }

    [Fact]
    public void FillsDownToOneMicronBuild()
    {
        var s = DocState.New();
        var layer = new Layer { Name = "Colour", Mode = LayerMode.Fill, Laser = LaserSettings.Red(20, 800, 6, 500), LinesPerCm = 10000 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 2, 2) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains("G1", job.Motion);
        Assert.Contains("Y10.001S0", job.Motion); // lines 0.001 mm apart
        Assert.Contains("Y10.002S0", job.Motion);
        var tooFine = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { LinesPerCm = 10001 });
        Assert.Throws<JobException>(() => JobBuilder.Build(tooFine, new JobOptions()));
    }
}

public class DiffractionTests
{
    /// <summary>A picture of solid blocks: each (x0, y0, x1, y1, r, g, b) in pixels.</summary>
    static RasterImage Blocks(int w, int h, params (int X0, int Y0, int X1, int Y1, byte R, byte G, byte B)[] blocks)
    {
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255; rgba[i * 4 + 3] = 255; }
        foreach (var (x0, y0, x1, y1, r, g, b) in blocks)
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * w + x) * 4;
                    rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b;
                }
        return new RasterImage(w, h, rgba);
    }

    [Fact]
    public void PitchIsSpeedOverFrequency()
    {
        Assert.Equal(1.0, Diffraction.PitchUm(300, 300), 9);
        Assert.Equal(640, Diffraction.Wavelength(0), 1);
        Assert.Equal(530, Diffraction.Wavelength(120), 1);
        Assert.Equal(460, Diffraction.Wavelength(240), 1);
    }

    [Fact]
    public void ShimmerSharesBrightnessOutOverDirectionsAndLeavesWhiteBare()
    {
        // 40 × 20 px picture shown 20 × 10 mm, 1 mm pixels: black left quarter, mid-grey next, white the rest.
        var img = Blocks(40, 20, (0, 0, 10, 20, 0, 0, 0), (10, 0, 20, 20, 119, 119, 119));
        var map = Diffraction.Map(img, new Vec2(20, 10), new DiffractionOptions { PixelMm = 1, AngleSteps = 4 });
        Assert.Equal(20, map.Cols);
        Assert.Equal(10, map.Rows);
        Assert.Equal(2, map.Buckets.Count);
        var black = map.Buckets.Single(b => b.AngleDeg == 0);
        Assert.Equal(50, black.Pixels);
        Assert.Equal(90, map.Buckets.Single(b => b != black).AngleDeg); // L ≈ 50 → step 2 of 4
        Assert.Equal(100, map.Cells.Count(c => c < 0)); // the white half is left bare
        Assert.All(map.Buckets, b => Assert.Equal(1.0, b.PitchUm, 2));
    }

    [Fact]
    public void ColourModeTurnsHueIntoPitchWithinTheSourcesLimits()
    {
        var img = Blocks(30, 10, (0, 0, 10, 10, 220, 30, 30), (10, 0, 20, 10, 40, 60, 220), (20, 0, 30, 10, 128, 128, 128));
        var o = new DiffractionOptions { Mode = DiffractionMode.Colour, PixelMm = 1, ColourSteps = 12 };
        var map = Diffraction.Map(img, new Vec2(30, 10), o);
        Assert.Equal(2, map.Buckets.Count); // grey isn't a grating colour
        var red = map.Buckets.MaxBy(b => b.PitchUm)!;
        var blue = map.Buckets.MinBy(b => b.PitchUm)!;
        Assert.InRange(red.PitchUm, 1.15, 1.25);   // ≈ 640/530 µm
        Assert.InRange(blue.PitchUm, 0.82, 0.92);  // ≈ 460/530 µm
        var lim = MachineProfile.F2Ultra.MopaLimitFor(60)!.Value;
        Assert.All(map.Buckets, b => Assert.Equal(lim.Safe(b.FrequencyKhz), b.FrequencyKhz));
    }

    [Fact]
    public void AddsOneFillLayerPerBucketOverThePictureAndBuilds()
    {
        var img = Blocks(40, 20, (0, 0, 10, 20, 0, 0, 0), (10, 0, 20, 20, 119, 119, 119));
        var src = ImageSource.FromEncoded(img.ToPng());
        var s = DocState.New();
        var picLayer = Layer.Default(1, LayerMode.Image);
        s = Ops.AddLayer(s, picLayer);
        var pic = new ImageShape { Name = "pic", LayerId = picLayer.Id, Source = src, SizeMm = new Vec2(20, 10), Transform = Affine.Translate(50, 60) };
        s = Ops.AddShapes(s, [pic]);
        var (after, added) = Diffraction.Add(s, pic, new DiffractionOptions { PixelMm = 1, AngleSteps = 4 });
        Assert.Equal(2, added.Count);
        var layers = after.Layers.Where(l => l.Name.StartsWith(Diffraction.LayerPrefix)).ToList();
        Assert.Equal(2, layers.Count);
        Assert.Contains(layers, l => l.FillAngleDeg == 90);
        Assert.All(layers, l => Assert.Equal(LightSource.Red, l.Laser.Light));
        // Each bucket's pixels merged into one strip (10 columns × 10 rows each).
        foreach (var id in added)
        {
            var shape = (PathShape)after.FindShape(id)!;
            Assert.Single(shape.Geometry.Subpaths);
            var b = shape.Bounds;
            Assert.True(b.MinX >= 50 - 1e-9 && b.MaxX <= 70 + 1e-9 && b.MinY >= 60 - 1e-9 && b.MaxY <= 70 + 1e-9);
        }
        var job = JobBuilder.Build(Ops.UpdateLayers(after, new HashSet<Guid> { picLayer.Id }, l => l with { Output = false }), new JobOptions());
        Assert.Equal(2, job.LayerTimes.Count);
    }
}

public class ReflectiveTests
{
    [Fact]
    public void ReflectiveLayersKeepOutOfTheProtectedRangeAndGetAReminder()
    {
        var s = DocState.New();
        var layer = new Layer { Name = "Brass tag", Mode = LayerMode.Fill, Laser = LaserSettings.Red(50, 500, 200, 55), LinesPerCm = 100 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 5, 5) }]);
        var plain = JobBuilder.Build(s, new JobOptions());
        Assert.DoesNotContain(plain.Warnings, w => w.Contains("reflective"));
        var shiny = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { Reflective = true });
        Assert.Contains("48–64 kHz", Assert.Throws<JobException>(() => JobBuilder.Build(shiny, new JobOptions())).Message);
        var ok = Ops.UpdateLayers(shiny, new HashSet<Guid> { layer.Id }, l => l with { Laser = l.Laser with { FrequencyKhz = 65 } });
        Assert.Contains(JobBuilder.Build(ok, new JobOptions()).Warnings, w => w.Contains("black marking spray"));
    }

    [Fact]
    public void NamesThatSoundReflectiveAreGuessed()
    {
        Assert.True(PhotonCanvas.Doc.Materials.Material.LooksReflective("Polished brass 1 mm"));
        Assert.True(PhotonCanvas.Doc.Materials.Material.LooksReflective("Aluminium sheet"));
        Assert.False(PhotonCanvas.Doc.Materials.Material.LooksReflective("Basswood"));
        Assert.False(PhotonCanvas.Doc.Materials.Material.LooksReflective("304 stainless, brushed"));
    }
}

public class RotateTests
{
    [Fact]
    public void QuarterTurnClockwisePutsTheLeftSideOnTop()
    {
        // 40 × 20: left half red, right half blue.
        int w = 40, h = 20;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                rgba[i] = (byte)(x < 20 ? 255 : 0); rgba[i + 2] = (byte)(x < 20 ? 0 : 255); rgba[i + 3] = 255;
            }
        var r = new RasterImage(w, h, rgba).Rotated(90);
        Assert.Equal(20, r.Width);
        Assert.Equal(40, r.Height);
        int Px(int x, int y) => (y * r.Width + x) * 4;
        Assert.True(r.Rgba[Px(10, 5)] > 200 && r.Rgba[Px(10, 5) + 2] < 50);   // top: red
        Assert.True(r.Rgba[Px(10, 35) + 2] > 200 && r.Rgba[Px(10, 35)] < 50); // bottom: blue
        // A slanted turn grows the canvas and leaves the corners clear.
        var s = new RasterImage(w, h, rgba).Rotated(30);
        Assert.True(s.Width > w && s.Height > h);
        Assert.True(s.Rgba[3] < 10);
    }
}

public class GratingTests
{
    static RasterImage Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = 255; }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void GifHasEveryFrameInItsColours()
    {
        // Three frames, the middle one a gradient (more than 256 colours, so it must be quantized).
        var grad = new byte[64 * 48 * 4];
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++)
            {
                int i = (y * 64 + x) * 4;
                grad[i] = (byte)(x * 4); grad[i + 1] = (byte)(y * 5); grad[i + 2] = 128; grad[i + 3] = 255;
            }
        var frames = new[] { Solid(64, 48, 200, 30, 40), new RasterImage(64, 48, grad), Solid(64, 48, 20, 90, 220) };
        var gif = GifEncoder.Encode(frames, 80);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        using var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(gif));
        Assert.NotNull(codec);
        Assert.Equal(3, codec.FrameCount);
        Assert.Equal(800, codec.FrameInfo[0].Duration);
        var info = new SkiaSharp.SKImageInfo(64, 48, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        using var bmp = new SkiaSharp.SKBitmap(info);
        for (int f = 0; f < 3; f++)
        {
            Assert.Equal(SkiaSharp.SKCodecResult.Success, codec.GetPixels(info, bmp.GetPixels(), new SkiaSharp.SKCodecOptions(f)));
            var want = frames[f].Rgba;
            var got = bmp.Bytes;
            foreach (int p in new[] { 0, 64 * 24 + 32, 64 * 48 - 1 })
                for (int ch = 0; ch < 3; ch++)
                    Assert.InRange(got[p * 4 + ch] - want[p * 4 + ch], -12, 12);
        }
    }

    [Fact]
    public void StraightensAPerspectivePhotoOfTheGrid()
    {
        // A photo taken at an angle: the 0–10 mm square appears as a trapezoid.
        var mmToPx = Homography.Fit([(new Vec2(0, 0), new Vec2(20, 30)), (new Vec2(10, 0), new Vec2(180, 20)), (new Vec2(10, 10), new Vec2(190, 170)), (new Vec2(0, 10), new Vec2(10, 160))])!.Value;
        var pxToMm = Homography.Fit([(new Vec2(20, 30), new Vec2(0, 0)), (new Vec2(180, 20), new Vec2(10, 0)), (new Vec2(190, 170), new Vec2(10, 10)), (new Vec2(10, 160), new Vec2(0, 10))])!.Value;
        int w = 200, h = 200;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = pxToMm.Apply(new Vec2(x + 0.5, y + 0.5));
                int i = (y * w + x) * 4;
                bool left = mm.X < 5;
                px[i] = (byte)(left ? 220 : 30); px[i + 2] = (byte)(left ? 30 : 220); px[i + 3] = 255;
            }
        var flat = GratingMeasure.Straighten(new RasterImage(w, h, px), mmToPx, new Rect2(0, 0, 10, 10), 10);
        Assert.Equal(100, flat.Width);
        Assert.True(flat.Rgba[(50 * 100 + 20) * 4] > 200);     // left half red, straight
        Assert.True(flat.Rgba[(50 * 100 + 80) * 4 + 2] > 200); // right half blue
        Assert.True(flat.Rgba[(5 * 100 + 20) * 4] > 200);      // even near the top edge
    }

    [Fact]
    public void ScoresSquaresByTheirBestViewAndSaysWhereTheyLightUp()
    {
        ColourSwatch S(int r, int c, double chroma) => new() { Row = r, Col = c, L = 60, A = chroma, B = 0 };
        var views = new[]
        {
            new GratingViewResult { View = GratingView.Straight, Swatches = [S(0, 0, 5), S(0, 1, 3)] },
            new GratingViewResult { View = GratingView.Top, Swatches = [S(0, 0, 40), S(0, 1, 8)] },
            new GratingViewResult { View = GratingView.Bottom, Swatches = [S(0, 0, 30), S(0, 1, 20)] },
        };
        var scores = GratingMeasure.Score(views);
        Assert.Equal((0, 0), (scores[0].Row, scores[0].Col));
        Assert.Equal(40, scores[0].Vividness, 6);
        Assert.Equal([GratingView.Top, GratingView.Bottom], scores[0].LightsUpAt);
        Assert.Equal([GratingView.Bottom], scores[1].LightsUpAt);
    }

    [Fact]
    public void GratingSetsTravelWithTheirPictures()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-gratings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = new PhotonCanvas.Doc.Materials.MaterialLibrary(Path.Combine(dir, "a"));
            var id = Guid.NewGuid();
            var frame = src.SaveFile($"{id:N}-straight.png", Solid(8, 8, 10, 20, 30).EncodePng());
            var gif = src.SaveFile($"{id:N}.gif", GifEncoder.Encode([Solid(8, 8, 10, 20, 30)], 50));
            var set = new GratingSet { Id = id, Name = "Gratings 1", Views = [new GratingViewResult { View = GratingView.Straight, Frame = frame }], Gif = gif };
            var m = new PhotonCanvas.Doc.Materials.Material { Name = "Mirror stainless", Gratings = [set], Palettes = [new ColourPalette { Name = "p" }] };
            src.Upsert(m);
            using var file = new MemoryStream();
            src.Export(file, src.Materials, "me");
            file.Position = 0;
            var dst = new PhotonCanvas.Doc.Materials.MaterialLibrary(Path.Combine(dir, "b"));
            dst.Import(file, "x");
            var got = dst.Materials.Single();
            Assert.Single(got.Palettes);
            var g = got.Gratings.Single();
            Assert.NotNull(dst.PhotoPath(g.Gif));
            Assert.NotNull(dst.PhotoPath(g.Views[0].Frame));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

public class ColourPictureTests
{
    static ColourSwatch Swatch(int col, byte r, byte g, byte b, double power) =>
        new ColourSwatch { Row = 0, Col = col, Settings = new PhotonCanvas.Doc.Materials.ProcessSettings { Mode = LayerMode.Fill, Laser = LaserSettings.Red(power, 800, 6, 500), LinesPerCm = 5000 } }
            with { L = ColourScience.SrgbToLab(r, g, b).L, A = ColourScience.SrgbToLab(r, g, b).A, B = ColourScience.SrgbToLab(r, g, b).B };

    static ColourPalette Palette() => new()
    {
        Swatches = [Swatch(0, 200, 160, 60, 40), Swatch(1, 40, 70, 170, 25)],
        BareL = 70, BareA = 0, BareB = 0,
    };

    static RasterImage Picture(params (byte R, byte G, byte B, byte A)[] columns)
    {
        // Columns of solid colour, 20 px wide each, 20 px tall.
        int w = 20 * columns.Length, h = 20;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = columns[x / 20];
                int i = (y * w + x) * 4;
                px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void ExactPaletteColoursMapStraightOntoTheirSettings()
    {
        var o = new ColourPictureOptions { PixelMm = 1, FitLightness = false, Dither = false };
        var inks = ColourPicture.Inks(Palette(), o);
        Assert.Equal(3, inks.Count);
        Assert.True(inks[0].IsBare);
        var bare = ColourScience.LabToSrgb(new Lab(70, 0, 0));
        var map = ColourPicture.Map(Picture((200, 160, 60, 255), (40, 70, 170, 255), (bare.R, bare.G, bare.B, 255), (0, 0, 0, 0)), new Vec2(8, 2), Palette(), o);
        Assert.Equal(8, map.Cols);
        int Cell(int c) => map.Cells[c];
        Assert.Equal(1, Cell(0)); // gold
        Assert.Equal(2, Cell(3)); // blue
        Assert.Equal(0, Cell(5)); // bare metal
        Assert.Equal(-1, Cell(7)); // transparent: outside the picture
    }

    [Fact]
    public void ShadesInBetweenAreMixedWithDots()
    {
        // A colour halfway between the gold and the blue: dithering uses both.
        var o = new ColourPictureOptions { PixelMm = 0.25, FitLightness = false, Dither = true, UseBare = false };
        var mid = ColourScience.LabToSrgb(new Lab(
            (ColourScience.SrgbToLab(200, 160, 60).L + ColourScience.SrgbToLab(40, 70, 170).L) / 2,
            (ColourScience.SrgbToLab(200, 160, 60).A + ColourScience.SrgbToLab(40, 70, 170).A) / 2,
            (ColourScience.SrgbToLab(200, 160, 60).B + ColourScience.SrgbToLab(40, 70, 170).B) / 2));
        var map = ColourPicture.Map(Picture((mid.R, mid.G, mid.B, 255)), new Vec2(10, 10), Palette(), o);
        double gold = map.Counts[0], blue = map.Counts[1];
        Assert.InRange(gold / (gold + blue), 0.3, 0.7);
    }

    [Fact]
    public void OneLayerPerColourUsedLeastEnergyFirst()
    {
        var img = Picture((200, 160, 60, 255), (40, 70, 170, 255));
        var src = ImageSource.FromEncoded(img.ToPng());
        var s = DocState.New();
        var picLayer = Layer.Default(1, LayerMode.Image);
        s = Ops.AddLayer(s, picLayer);
        var pic = new ImageShape { Name = "pic", LayerId = picLayer.Id, Source = src, SizeMm = new Vec2(20, 10), Transform = Affine.Translate(30, 30) };
        s = Ops.AddShapes(s, [pic]);
        var (after, added) = ColourPicture.Add(s, pic, Palette(), new ColourPictureOptions { PixelMm = 1, Dither = false, FitLightness = false });
        Assert.Equal(2, added.Count);
        var layers = after.Layers.Where(l => l.Name.StartsWith(ColourPicture.LayerPrefix)).ToList();
        Assert.Equal([25.0, 40.0], layers.Select(l => l.Laser.Power)); // 25% (blue) has less energy: first
        Assert.All(layers, l => { Assert.Equal(LightSource.Red, l.Laser.Light); Assert.Equal(5000, l.LinesPerCm); });
        var job = JobBuilder.Build(Ops.UpdateLayers(after, new HashSet<Guid> { picLayer.Id }, l => l with { Output = false }), new JobOptions());
        Assert.Equal(2, job.LayerTimes.Count);
    }

    [Fact]
    public void BareMetalIsReadFromTheGapsBetweenSquares()
    {
        var o = new ColourTestOptions { AcrossSteps = 3, DownSteps = 2, CellMm = 6, GapMm = 3 };
        var (s, _) = ColourTest.Add(DocState.New(), o);
        var cells = ColourTest.Cells(s);
        var mmToPx = Homography.Fit([.. new[] { new Vec2(0, 0), new(100, 0), new(100, 100), new(0, 100) }.Select(p => (p, new Vec2(p.X * 8, p.Y * 8)))])!.Value;
        int w = 800, h = 600;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = new Vec2((x + 0.5) / 8, (y + 0.5) / 8);
                bool inSquare = cells.Any(c => c.Bounds.Contains(mm));
                int i = (y * w + x) * 4;
                px[i] = (byte)(inSquare ? 40 : 150); px[i + 1] = (byte)(inSquare ? 40 : 152); px[i + 2] = (byte)(inSquare ? 160 : 158); px[i + 3] = 255;
            }
        var bare = ColourMeasure.MeasureBare(new RasterImage(w, h, px), mmToPx, cells);
        Assert.NotNull(bare);
        Assert.True(ColourScience.DeltaE2000(bare!.Value, ColourScience.SrgbToLab(150, 152, 158)) < 1);
    }
}
