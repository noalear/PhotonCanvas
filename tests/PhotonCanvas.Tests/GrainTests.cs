using System.Collections.Immutable;
using System.Text.RegularExpressions;
using PhotonCanvas.Addon.Grain;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using SkiaSharp;

namespace PhotonCanvas.Tests;

/// <summary>Grain-aware engraving: the power field in the job writer, and the add-on's map, test and placement.</summary>
public class GrainTests
{
    // ---- the power field (Core) and the job builder's hook ----

    static PowerField Halves(double left, double right, double cell = 1) =>
        // 20 × 10 mm from (0, 0): the left half at one factor, the right at another.
        new(new Vec2(0, 0), cell, (int)(20 / cell), (int)(10 / cell),
            [.. Enumerable.Range(0, (int)(20 / cell) * (int)(10 / cell)).Select(i => (float)((i % (int)(20 / cell)) * cell < 10 ? left : right))]);

    [Fact]
    public void PowerFieldBlendsCellsAndIsFullPowerOutsideOrUnknown()
    {
        var f = Halves(0.5, 1);
        Assert.Equal(0.5, f.At(new Vec2(3, 5)), 6);
        Assert.Equal(1, f.At(new Vec2(15, 5)), 6);
        Assert.Equal(1, f.At(new Vec2(-5, 5)), 6);
        Assert.Equal(1, f.At(new Vec2(50, 50)), 6);
        // Across the edge it changes smoothly.
        Assert.InRange(f.At(new Vec2(10, 5)), 0.7, 0.8);
        // Unknown cells count as full power; factors are clamped to 0–1.
        var u = new PowerField(new Vec2(0, 0), 1, 2, 1, [float.NaN, 3f]);
        Assert.Equal(1, u.At(new Vec2(0.5, 0.5)), 6);
        Assert.Equal(1, u.Cell(1, 0));
        Assert.Equal(0.5f, Halves(0.5, 1).Shifted(new Vec2(100, 0)).Lowest);
        Assert.Equal(0.5, Halves(0.5, 1).Shifted(new Vec2(100, 0)).At(new Vec2(103, 5)), 6);
    }

    [Fact]
    public void PowerFieldSplitsALineWherePowerChanges()
    {
        var f = Halves(0.5, 1);
        var pieces = f.Along(new Vec2(1, 5), new Vec2(19, 5));
        Assert.True(pieces.Count is >= 2 and <= 4, $"{pieces.Count} pieces");
        Assert.Equal(0.5, pieces[0].Factor, 6);
        Assert.Equal(1, pieces[^1].Factor, 6);
        Assert.Equal(new Vec2(19, 5), pieces[^1].End);
        // In order along the line.
        for (int i = 1; i < pieces.Count; i++) Assert.True(pieces[i].End.X > pieces[i - 1].End.X);
        // Even power: one piece.
        Assert.Single(Halves(0.8, 0.8).Along(new Vec2(1, 5), new Vec2(19, 5)));
        // Two fields multiply.
        var both = PowerField.Combine(Halves(0.5, 1), Halves(0.8, 0.8))!;
        Assert.Equal(0.4, both.At(new Vec2(3, 5)), 3);
        Assert.Null(PowerField.Combine(null, null));
    }

    static DocState FillSquare(out Layer layer, double power = 50)
    {
        layer = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(power, 400), LinesPerCm = 10 };
        var sq = new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(0, 0, 20, 10) };
        return Ops.AddShapes(DocState.New() with { Layers = [layer] }, [sq]);
    }

    static List<long> BurnS(string motion) =>
        [.. Regex.Matches(motion, @"^G1[^\n]*S(\d+)", RegexOptions.Multiline).Select(m => long.Parse(m.Groups[1].Value)).Where(v => v > 0)];

    [Fact]
    public void NoFieldKeepsTheGcodeExactlyAsBefore()
    {
        var s = FillSquare(out var layer);
        var plain = JobBuilder.Build(s, new JobOptions()).Motion;
        // A field that lowers nothing changes nothing.
        var none = JobBuilder.Build(s with { LayerPower = ImmutableDictionary<Guid, PowerField>.Empty.Add(layer.Id, Halves(1, 1)) }, new JobOptions()).Motion;
        Assert.Equal(plain, none);
        // And only S500 (50 %) is ever burned.
        Assert.All(BurnS(plain), v => Assert.Equal(500, v));
    }

    [Fact]
    public void AFillBurnsEachLineInPiecesAtTheFieldsPower()
    {
        var s = FillSquare(out var layer);
        var plain = JobBuilder.Build(s, new JobOptions());
        var job = JobBuilder.Build(s with { LayerPower = ImmutableDictionary<Guid, PowerField>.Empty.Add(layer.Id, Halves(0.5, 1)) }, new JobOptions());
        var burns = BurnS(job.Motion);
        Assert.Contains(250L, burns); // half of 50 % on the left
        Assert.Contains(500L, burns); // the layer's own power on the right
        Assert.All(burns, v => Assert.InRange(v, 0, 500)); // never more than the layer's power
        // The same path and the same time: only the power changes.
        Assert.Equal(plain.EstimateS, job.EstimateS, 3);
        Assert.Equal(plain.Bounds, job.Bounds);
    }

    [Fact]
    public void LayersThatCantFollowAFieldRunAtTheirOwnPowerWithAWarning()
    {
        var s = FillSquare(out var layer);
        var plain = JobBuilder.Build(s, new JobOptions()).Motion;
        var offset = s with { Layers = [layer with { FillStyle = FillStyle.Offset }], LayerPower = ImmutableDictionary<Guid, PowerField>.Empty.Add(layer.Id, Halves(0.5, 1)) };
        var job = JobBuilder.Build(offset, new JobOptions());
        Assert.Contains(job.Warnings, w => w.Contains("runs at its own power everywhere"));
        Assert.Equal(JobBuilder.Build(offset with { LayerPower = ImmutableDictionary<Guid, PowerField>.Empty }, new JobOptions()).Motion, job.Motion);
        Assert.NotEqual(plain, job.Motion);
    }

    static byte[] Png(int w, int h, Action<SKCanvas> draw)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.White); draw(c); }
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void APicturesGreysAreScaledByTheField()
    {
        var layer = Layer.Default(0, LayerMode.Image) with { Dpi = 254, ImageMode = ImageEngraveMode.Grayscale, GrayMinPower = 0 };
        var src = ImageSource.FromEncoded(Png(200, 100, c => c.Clear(SKColors.Black)));
        var img = new ImageShape { LayerId = layer.Id, Source = src, SizeMm = new(20, 10) };
        var s = Ops.AddShapes(DocState.New() with { Layers = [layer] }, [img]);
        var plain = Assert.IsType<DotBlock>(Assert.Single(JobBuilder.Build(s, new JobOptions()).Job.Blocks));
        var half = Assert.IsType<DotBlock>(Assert.Single(JobBuilder.Build(s with { LayerPower = ImmutableDictionary<Guid, PowerField>.Empty.Add(layer.Id, Halves(0.5, 1)) }, new JobOptions()).Job.Blocks));
        int w = plain.Bitmap.Width;
        Assert.Equal(255, plain.Levels![50 * w + 20]);
        Assert.InRange(half.Levels![50 * w + 20], 125, 130); // left: half
        Assert.Equal(255, half.Levels![50 * w + 180]); // right: as it was
        // Threshold pictures are all or nothing: left alone, with a warning.
        var t = s with { Layers = [layer with { ImageMode = ImageEngraveMode.Dither, Dither = DitherMethod.Threshold }], LayerPower = ImmutableDictionary<Guid, PowerField>.Empty.Add(layer.Id, Halves(0.3, 1)) };
        var tj = JobBuilder.Build(t, new JobOptions());
        Assert.Contains(tj.Warnings, x => x.Contains("own power everywhere"));
        Assert.True(Assert.IsType<DotBlock>(Assert.Single(tj.Job.Blocks)).Bitmap[20, 50]);
    }

    // ---- the grain map ----

    /// <summary>Log brightness of a board with grain stripes every <paramref name="periodMm"/> along X, under uneven light.</summary>
    static float[] Board(int w, int h, double cell, double periodMm, Func<int, int, bool>? unseen = null)
    {
        var v = new float[w * h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                double x = (c + 0.5) * cell;
                bool dark = (x % periodMm) < periodMm / 2;
                double lum = (dark ? 0.45 : 0.6) * (0.7 + 0.3 * c / w); // the light falls off to the left
                v[r * w + c] = unseen?.Invoke(c, r) == true ? float.NaN : (float)Math.Log(lum + 0.02);
            }
        return v;
    }

    [Fact]
    public void TheMapFindsDarkAndLightGrainUnderUnevenLight()
    {
        double cell = 0.5;
        int w = 200, h = 80;
        var map = GrainMap.FromLogBrightness(Board(w, h, cell, 8), w, h, new Vec2(10, 20), cell);
        Assert.Equal(new Rect2(10, 20, 110, 60), map.Area);
        // Middle of a dark stripe (x 0–4 mm, centre 2) vs a light one (centre 6), at both ends of the board.
        foreach (double x0 in new[] { 8.0, 88.0 })
        {
            double dark = map.At(new Vec2(10 + x0 + 2, 40)), light = map.At(new Vec2(10 + x0 + 6, 40));
            Assert.True(dark > 0.7 && light < 0.3, $"dark {dark:0.00}, light {light:0.00} at {x0}");
        }
        // Stripes along Y (constant along it): the grain runs at 90°.
        Assert.InRange(map.AngleDeg, 80, 100);
        Assert.InRange(map.ContrastPct, 15, 40);
        Assert.True(double.IsNaN(map.At(new Vec2(0, 0))));
    }

    [Fact]
    public void TheMapKeepsUnseenPlacesUnknownAndReadsBackExactly()
    {
        double cell = 0.5;
        int w = 120, h = 60;
        var map = GrainMap.FromLogBrightness(Board(w, h, cell, 6, (c, r) => c < 20), w, h, new Vec2(0, 0), cell);
        Assert.Equal(GrainMap.Unknown, map.Levels[30 * w + 5]);
        Assert.True(map.KnownFraction is > 0.8 and < 0.9);
        var back = GrainMap.FromJson(map.ToJson())!;
        Assert.Equal(map.Levels, back.Levels);
        Assert.Equal((map.Origin, map.CellMm, map.Width, map.Height), (back.Origin, back.CellMm, back.Width, back.Height));
        Assert.Equal(map.AngleDeg, back.AngleDeg, 1);
        // Masked to a board: outside it unknown.
        var masked = map.Masked(p => p.X > 30);
        Assert.Equal(GrainMap.Unknown, masked.Levels[30 * w + 50]);
        Assert.NotEqual(GrainMap.Unknown, masked.Levels[30 * w + 100]);
    }

    [Fact]
    public void TheMapIsMadeFromAStraightenedCameraPicture()
    {
        // 100 × 50 mm at 0.5 mm per pixel: dark stripes every 6 mm; the right 20 mm see-through.
        int pw = 200, ph = 100;
        var rgba = new byte[pw * ph * 4];
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                int i = (y * pw + x) * 4;
                byte g = (x * 0.5) % 6 < 3 ? (byte)110 : (byte)160;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = g;
                rgba[i + 3] = x >= 160 ? (byte)0 : (byte)255;
            }
        var photo = new RasterImage(pw, ph, rgba);
        var map = GrainMap.FromPhoto(photo, new Rect2(0, 0, 100, 50), 0.5, new Rect2(10, 10, 95, 40));
        Assert.Equal(new Vec2(10, 10), map.Origin);
        Assert.True(map.At(new Vec2(13.5, 25)) > 0.7); // 12–15 is dark
        Assert.True(map.At(new Vec2(16.5, 25)) < 0.3); // 15–18 is light
        Assert.True(double.IsNaN(map.At(new Vec2(90, 25)))); // see-through
        Assert.Throws<ArgumentException>(() => GrainMap.FromPhoto(photo, new Rect2(0, 0, 100, 50), 0.5, new Rect2(200, 200, 300, 300)));
    }

    // ---- settings, project data and the job hook ----

    [Fact]
    public void EvenLowersPowerOnTheGrainThatBurnsDarker()
    {
        var even = new GrainSettings(GrainMode.Even, 0.3);
        Assert.Equal(1, even.Factor(0), 6);
        Assert.Equal(0.7, even.Factor(1), 6);
        // Light grain burns darker on this wood: the other way round.
        Assert.Equal(0.7, (even with { DarkBurnsDarker = false }).Factor(0), 6);
        // Bring out the grain: the opposite of even.
        Assert.Equal(0.7, (even with { Mode = GrainMode.BringOut }).Factor(0), 6);
        Assert.Equal(1, (even with { Mode = GrainMode.BringOut }).Factor(1), 6);
        Assert.Equal(1, GrainSettings.None.Factor(1));
        // Test strips are never compensated, and the strength is capped.
        Assert.False((even with { TestFactor = 0.7 }).Active);
        Assert.Equal(1 - GrainSettings.MaxStrength, (even with { Strength = 5 }).Factor(1), 6);
    }

    static GrainMap Stripes(Vec2 origin, double wMm, double hMm, double periodMm, double cell = 0.5)
    {
        int w = (int)(wMm / cell), h = (int)(hMm / cell);
        var lv = new byte[w * h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                lv[r * w + c] = ((c + 0.5) * cell % periodMm) < periodMm / 2 ? (byte)254 : (byte)0;
        return new GrainMap(origin, cell, w, h, lv, 20, 90);
    }

    [Fact]
    public void TheHookGivesFollowingLayersAFieldAndTheDataSurvivesTheFile()
    {
        var s = FillSquare(out var layer);
        var other = Layer.Default(1, LayerMode.Fill);
        s = s with { Layers = s.Layers.Add(other) };
        s = GrainProject.WithMap(s, Stripes(new Vec2(-5, -5), 40, 30, 4));
        s = GrainProject.WithLayer(s, layer.Id, new GrainSettings(GrainMode.Even, 0.4));
        var hook = new GrainHook();
        var built = hook.BeforeBuild(s, new JobOptions());
        var f = Assert.Single(built.LayerPower).Value;
        Assert.Equal(0.6f, f.Lowest, 3);
        // Dark grain (x −5…−3, −1…1, …) gets 60 %, light grain full power.
        Assert.Equal(0.6, f.At(new Vec2(0, 5)), 2);
        Assert.Equal(1, f.At(new Vec2(2, 5)), 2);
        // The job follows it.
        var job = JobBuilder.Build(built, new JobOptions());
        Assert.Contains(300L, BurnS(job.Motion));
        Assert.Contains(500L, BurnS(job.Motion));
        // Saved and opened again, everything's still there (the field itself is never saved).
        var back = DocFile.Deserialize(DocFile.Serialize(built));
        Assert.Empty(back.LayerPower);
        Assert.Equal(GrainProject.Map(s)!.Levels, GrainProject.Map(back)!.Levels);
        Assert.Equal(new GrainSettings(GrainMode.Even, 0.4), GrainProject.For(back, layer.Id));
        Assert.Equal(GrainSettings.None, GrainProject.For(back, other.Id));
        // Without the map, nothing changes and the check says why.
        var noMap = GrainProject.WithMap(s, null);
        Assert.Same(noMap, hook.BeforeBuild(noMap, new JobOptions()));
        Assert.Contains(hook.Check(noMap, new JobOptions()), w => w.Contains("hasn't been mapped"));
        // Off the map: said so.
        var small = GrainProject.WithMap(s, Stripes(new Vec2(0, 0), 10, 10, 4));
        Assert.Contains(hook.Check(small, new JobOptions()), w => w.Contains("off the grain map"));
    }

    [Fact]
    public void SettingsOfLayersThatAreGoneAreDropped()
    {
        var s = FillSquare(out var layer);
        s = GrainProject.WithLayer(s, layer.Id, new GrainSettings(GrainMode.Even));
        var gone = Guid.NewGuid();
        s = GrainProject.WithLayer(s, gone, new GrainSettings(GrainMode.Even));
        Assert.Equal(GrainSettings.None, GrainProject.For(s, gone));
        s = GrainProject.WithLayer(s, layer.Id, GrainSettings.None);
        Assert.False(s.AddonData.ContainsKey(GrainProject.LayersKey));
    }

    // ---- the grain test ----

    [Fact]
    public void TheTestGoesOnItsOwnPageAcrossTheGrain()
    {
        var s = FillSquare(out var layer);
        var (t, strips) = GrainTest.AddOnOwnPage(s, layer.Id, new Vec2(100, 100), 0);
        Assert.Equal("Grain test", t.PageName);
        Assert.Equal(2, t.Layers.Count);
        Assert.Equal(2, strips.Count);
        Assert.Equal([50.0, 35.0], t.Layers.Select(l => l.Laser.Power));
        Assert.All(t.Layers, l => Assert.Equal(layer.Id, GrainProject.For(t, l.Id).TestOf));
        // Grain along X (0°): the strips' long side runs along Y, across it.
        var b = t.FindShape(strips[0])!.Bounds;
        Assert.InRange(b.Height, 39.9, 40.1);
        Assert.InRange(b.Width, 14.9, 15.1);
        Assert.Equal(2, GrainTest.Strips(t).Count);
        Assert.Equal(layer.Id, GrainTest.TestedLayer(t));
        // Never compensated.
        Assert.Empty(new GrainHook().BeforeBuild(GrainProject.WithMap(t, Stripes(new Vec2(50, 50), 100, 100, 4)), new JobOptions()).LayerPower);
    }

    [Fact]
    public void ReadingTheTestRecoversHowMuchThePowerTheGrainIsWorth()
    {
        // Grain stripes along X every 4 mm over 0–200 × 0–200; strips across them at 100 % and 70 %.
        var map = Stripes(new Vec2(0, 0), 200, 200, 4);
        var s = FillSquare(out var layer);
        var (t, ids) = GrainTest.Add(s, layer.Id, new Vec2(100, 100), 90);
        var strips = GrainTest.Strips(t);
        // The burned picture: darkness D = 0.2 + 0.15 grain + 0.6 power, so 0.15 / 0.6 = 25 % evens it out.
        int pw = 400, ph = 400;
        var rgba = new byte[pw * ph * 4];
        var polys = strips.Select(x => (Poly: x.Strip.World.Flatten(0.05)[0].Points, x.Factor)).ToList();
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                var p = new Vec2((x + 0.5) * 0.5, (y + 0.5) * 0.5);
                double g = map.At(p);
                double d = 0.1 * g; // unburned wood shows its grain a little
                foreach (var (poly, f) in polys)
                    if (Geom.Inside(poly, p)) d = 0.2 + 0.15 * g + 0.6 * f;
                double lum = Math.Exp(-d) - 0.02;
                int i = (y * pw + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = (byte)Math.Clamp(Math.Round(lum * 255), 0, 255);
                rgba[i + 3] = 255;
            }
        var r = GrainTest.Read(map, strips, new RasterImage(pw, ph, rgba), new Rect2(0, 0, 200, 200));
        var cal = Assert.IsType<GrainCalibration>(r.Calibration);
        Assert.True(cal.DarkBurnsDarker);
        Assert.InRange(cal.Strength, 0.2, 0.3);
        Assert.True(cal.Fit > 0.9, $"fit {cal.Fit}");
        Assert.True(cal.Samples > 500);
        // A picture taken before burning: the strips are no darker at more power.
        var before = new byte[pw * ph * 4];
        Array.Fill(before, (byte)200);
        Assert.Null(GrainTest.Read(map, strips, new RasterImage(pw, ph, before), new Rect2(0, 0, 200, 200)).Calibration);
        // One strip alone can't tell.
        Assert.Null(GrainTest.Read(map, [strips[0]], new RasterImage(pw, ph, rgba), new Rect2(0, 0, 200, 200)).Calibration);
    }

    // ---- placing a picture on the grain ----

    [Fact]
    public void APictureIsPlacedWhereItsDarksFallOnDarkGrain()
    {
        // The board's grain: a dark ring centred at (70, 45) on light wood, 120 × 80 mm.
        double cell = 0.5;
        int w = 240, h = 160;
        var lv = new byte[w * h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                double dist = new Vec2((c + 0.5) * cell, (r + 0.5) * cell).Dist(new Vec2(70, 45));
                lv[r * w + c] = dist is > 8 and < 14 ? (byte)254 : (byte)20;
            }
        var map = new GrainMap(new Vec2(0, 0), cell, w, h, lv, 20, double.NaN);
        // A 40 mm picture of the same ring (centre 20, 20), put at (10, 10): its ring centred at (30, 30).
        var src = ImageSource.FromEncoded(Png(400, 400, c => c.DrawCircle(200, 200, 110, new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 60, IsAntialias = true })));
        var img = new ImageShape { Source = src, SizeMm = new(40, 40), Transform = Affine.Translate(10, 10) };
        var r2 = GrainPlacement.Best(map, img)!;
        Assert.NotNull(r2);
        var centre = r2.Transform.Apply(new Vec2(20, 20));
        Assert.InRange(centre.Dist(new Vec2(70, 45)), 0, 1.0);
        Assert.True(r2.Score > 0.8, $"score {r2.Score}");
        Assert.True(r2.ScoreHere < r2.Score);
        Assert.InRange(r2.Move.Dist(new Vec2(40, 15)), 0, 1.0);
        // Too big for the board: nowhere.
        var huge = new ImageShape { Source = src, SizeMm = new(200, 200) };
        Assert.Null(GrainPlacement.Best(map, huge));
    }
}
