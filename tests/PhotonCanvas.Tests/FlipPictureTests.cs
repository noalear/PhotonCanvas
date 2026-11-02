using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.Addon.Flip;
using Xunit;

namespace PhotonCanvas.Tests;

public class FlipPictureTests
{
    /// <summary>A white picture with one solid grey block (x0, y0, x1, y1 in pixels).</summary>
    static RasterImage Block(int w, int h, int x0, int y0, int x1, int y1, byte grey = 0)
    {
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte v = x >= x0 && x < x1 && y >= y0 && y < y1 ? grey : (byte)255;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        return new RasterImage(w, h, rgba);
    }

    [Theory]
    [InlineData(FlipWeave.Tiles)]
    [InlineData(FlipWeave.Columns)]
    [InlineData(FlipWeave.Rows)]
    [InlineData(FlipWeave.Diagonal)]
    public void EveryWeaveSharesThePlateEvenly(FlipWeave weave)
    {
        for (int n = 2; n <= FlipOptions.MaxViews; n++)
        {
            var counts = new int[n];
            for (int r = 0; r < 12; r++)
                for (int c = 0; c < 12; c++)
                    counts[FlipPicture.Owner(c, r, n, weave)]++;
            Assert.All(counts, k => Assert.Equal(144 / n, k));
        }
    }

    [Fact]
    public void TilesNeverPutTwoPatchesOfOnePictureSideBySide()
    {
        for (int n = 2; n <= 4; n++)
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 7; c++)
                        Assert.NotEqual(FlipPicture.Owner(c, r, n, FlipWeave.Tiles), FlipPicture.Owner(c + 1, r, n, FlipWeave.Tiles));
    }

    [Fact]
    public void SpreadAnglesAreEvenAndFarApart()
    {
        Assert.Equal([0, 90], FlipOptions.SpreadAngles(2));
        Assert.Equal([0, 60, 120], FlipOptions.SpreadAngles(3));
        Assert.Equal([0, 45, 90, 135], FlipOptions.SpreadAngles(4));
    }

    [Fact]
    public void EachPictureMarksOnlyItsOwnPatchesWhereItIsDark()
    {
        // 20 × 20 mm plate, 1 mm patches. A is dark on the left half, B on the top half.
        var a = Block(40, 40, 0, 0, 20, 40);
        var b = Block(40, 40, 0, 0, 40, 20);
        var o = new FlipOptions { PatchMm = 1, Weave = FlipWeave.Tiles };
        var map = FlipPicture.Map([a, b], new Vec2(20, 20), o);
        Assert.Equal((20, 20), (map.Cols, map.Rows));
        Assert.Equal(2, map.Buckets.Count);
        for (int r = 0; r < 20; r++)
            for (int c = 0; c < 20; c++)
            {
                int k = map.Cells[r * 20 + c];
                if (k < 0) continue;
                Assert.Equal(FlipPicture.Owner(c, r, 2, FlipWeave.Tiles), k);
                if (k == 0) Assert.True(c < 10, "A marked outside its dark half");
                else Assert.True(r < 10, "B marked outside its dark half");
            }
        Assert.Equal(100, map.Buckets[0].Pixels); // half of the 200 left-half patches
        Assert.Equal(100, map.Buckets[1].Pixels);
        Assert.Equal(0, map.Buckets[0].AngleDeg);
        Assert.Equal(90, map.Buckets[1].AngleDeg);
        Assert.All(map.Buckets, x => Assert.Equal(1.0, x.PitchUm, 2));
    }

    [Fact]
    public void InvertMarksTheLightParts()
    {
        var a = Block(20, 20, 0, 0, 10, 20);
        var o = new FlipOptions { PatchMm = 1, Views = [new() { Invert = true }, new() { AngleDeg = 90 }] };
        var map = FlipPicture.Map([a, a], new Vec2(20, 20), o);
        for (int r = 0; r < 20; r++)
            for (int c = 0; c < 20; c++)
                if (map.Cells[r * 20 + c] == 0) Assert.True(c >= 10);
        Assert.Equal(100, map.Buckets[0].Pixels);
        Assert.Equal(100, map.Buckets[1].Pixels); // B isn't inverted: its dark left half
    }

    [Fact]
    public void StrengthAndTonesThinAPictureOut()
    {
        var black = Block(40, 40, 0, 0, 40, 40);
        var grey = Block(40, 40, 0, 0, 40, 40, 119); // L ≈ 50
        var full = FlipPicture.Map([black, black], new Vec2(40, 40), new FlipOptions { PatchMm = 1 });
        Assert.Equal(800, full.Buckets[0].Pixels);
        var faint = FlipPicture.Map([black, black], new Vec2(40, 40), new FlipOptions { PatchMm = 1, Views = [new() { StrengthPct = 30 }, new() { AngleDeg = 90 }] });
        Assert.InRange(faint.Buckets[0].Pixels, 180, 300);
        Assert.Equal(800, faint.Buckets[1].Pixels);
        var toned = FlipPicture.Map([grey, grey], new Vec2(40, 40), new FlipOptions { PatchMm = 1, Views = [new() { Tones = true }, new() { AngleDeg = 90, Tones = true }] });
        Assert.All(toned.Buckets, x => Assert.InRange(x.Pixels, 300, 500));
        // Without tones a mid-grey is all or nothing.
        var hard = FlipPicture.Map([grey, grey], new Vec2(40, 40), new FlipOptions { PatchMm = 1, Views = [new() { Threshold = 60 }, new() { AngleDeg = 90, Threshold = 40 }] });
        Assert.Equal(800, hard.Buckets[0].Pixels);
        Assert.Equal(0, hard.Buckets[1].Pixels);
    }

    [Fact]
    public void PitchesBecomeFrequenciesWithinTheSourcesLimits()
    {
        var black = Block(10, 10, 0, 0, 10, 10);
        var o = new FlipOptions { PatchMm = 1, Views = [new() { PitchUm = 0.8 }, new() { PitchUm = 1.3 }] };
        var map = FlipPicture.Map([black, black], new Vec2(10, 10), o);
        var lim = MachineProfile.F2Ultra.MopaLimitFor(60)!.Value;
        Assert.All(map.Buckets, x => Assert.Equal(lim.Safe(x.FrequencyKhz), x.FrequencyKhz));
        Assert.True(map.Buckets[0].PitchUm < map.Buckets[1].PitchUm);
    }

    [Fact]
    public void AddsOneLayerPerPictureOverTheFirstPictureAndBuilds()
    {
        var src = ImageSource.FromEncoded(Block(40, 20, 0, 0, 20, 20).ToPng());
        var src2 = ImageSource.FromEncoded(Block(30, 30, 0, 0, 30, 15).ToPng());
        var s = DocState.New();
        var picLayer = Layer.Default(1, LayerMode.Image);
        s = Ops.AddLayer(s, picLayer);
        var a = new ImageShape { Name = "a", LayerId = picLayer.Id, Source = src, SizeMm = new Vec2(20, 10), Transform = Affine.Translate(50, 60) };
        var b = new ImageShape { Name = "b", LayerId = picLayer.Id, Source = src2, SizeMm = new Vec2(30, 30), Transform = Affine.Translate(0, 0) };
        s = Ops.AddShapes(s, [a, b]);
        var (after, added) = FlipPicture.Add(s, [a, b], new FlipOptions { PatchMm = 0.5, Views = [new() { Power = 25 }, new() { AngleDeg = 90 }] });
        Assert.Equal(2, added.Count);
        var layers = after.Layers.Where(l => l.Name.StartsWith(FlipPicture.LayerPrefix)).ToList();
        Assert.Equal(2, layers.Count);
        Assert.Equal("Flip A · 0° · 1.00 µm", layers[0].Name);
        Assert.Equal(0, layers[0].FillAngleDeg);
        Assert.Equal(90, layers[1].FillAngleDeg);
        Assert.Equal(25, layers[0].Laser.Power);
        Assert.Equal(32, layers[1].Laser.Power);
        Assert.All(layers, l => Assert.Equal(LightSource.Red, l.Laser.Light));
        // Everything lands on picture A's place, whatever B's size and place.
        foreach (var id in added)
        {
            var bb = after.FindShape(id)!.Bounds;
            Assert.True(bb.MinX >= 50 - 1e-9 && bb.MaxX <= 70 + 1e-9 && bb.MinY >= 60 - 1e-9 && bb.MaxY <= 70 + 1e-9);
        }
        var job = JobBuilder.Build(Ops.UpdateLayers(after, new HashSet<Guid> { picLayer.Id }, l => l with { Output = false }), new JobOptions());
        Assert.Equal(2, job.LayerTimes.Count);
    }

    [Fact]
    public void TheFillAngleTurnsWithThePlate()
    {
        var src = ImageSource.FromEncoded(Block(10, 10, 0, 0, 10, 10).ToPng());
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Image);
        s = Ops.AddLayer(s, layer);
        var a = new ImageShape { Name = "a", LayerId = layer.Id, Source = src, SizeMm = new Vec2(10, 10), Transform = Affine.Rotate(30) };
        s = Ops.AddShapes(s, [a]);
        var (after, _) = FlipPicture.Add(s, [a, a], new FlipOptions { PatchMm = 1 });
        var angles = after.Layers.Where(l => l.Name.StartsWith(FlipPicture.LayerPrefix)).Select(l => l.FillAngleDeg).ToList();
        Assert.Equal(30, angles[0], 6);
        Assert.Equal(120, angles[1], 6);
    }

    [Fact]
    public void TiltHintsFollowTheDirection()
    {
        Assert.Equal("tilt left or right", FlipPicture.TiltHint(0));
        Assert.Equal("tilt left or right", FlipPicture.TiltHint(180));
        Assert.Equal("tilt towards or away from you", FlipPicture.TiltHint(90));
        Assert.Equal("tilt towards or away from you", FlipPicture.TiltHint(-90));
        Assert.Contains("diagonal", FlipPicture.TiltHint(45));
    }

    [Fact]
    public void SimulationLightsTheChosenPictureAndGhostsTheOthers()
    {
        var map = new GratingMap(3, 1, [0, 1, -1], [], 1);
        Assert.Equal([235, 70, 40], FlipPicture.Simulate(map, 0));
        Assert.Equal([70, 235, 40], FlipPicture.Simulate(map, 1));
        Assert.Equal([235, 235, 40], FlipPicture.Simulate(map, -1));
    }

    static ColourSwatch Square(int col, int khz, double chroma) => new()
    {
        Row = 0, Col = col, L = 60, A = chroma, B = 0,
        Settings = new ProcessSettings { Mode = LayerMode.Fill, Laser = LaserSettings.Red(30 + col, 300, 60, khz) },
    };

    static GratingSet Test() => new()
    {
        Name = "t",
        Views =
        [
            // Column 0 (1.2 µm) shows from the left, column 1 (0.8 µm) from the right, column 2 everywhere a little.
            new() { View = GratingView.Left, Swatches = [Square(0, 250, 40), Square(1, 375, 4), Square(2, 300, 25)] },
            new() { View = GratingView.Right, Swatches = [Square(0, 250, 5), Square(1, 375, 38), Square(2, 300, 24)] },
            new() { View = GratingView.Straight, Swatches = [Square(0, 250, 2), Square(1, 375, 2), Square(2, 300, 2)] },
        ],
    };

    [Fact]
    public void SuggestionsPickTheSquareThatStandsOutFromEachView()
    {
        var s = FlipSuggest.FromGratings(Test(), [GratingView.Left, GratingView.Right])!;
        Assert.Equal(1.2, s[0].PitchUm, 2);
        Assert.Equal(0.8, s[1].PitchUm, 2);
        Assert.Equal(30, s[0].Power);
        Assert.Equal(31, s[1].Power);
        Assert.All(s, x => Assert.Equal(0, x.AngleDeg));
        Assert.All(s, x => Assert.True(x.Separation > 30));
        Assert.All(s, x => Assert.Equal((300, 60), (x.Speed, x.PulseWidthNs)));
    }

    [Fact]
    public void ATopTargetTurnsTheGrating()
    {
        var s = FlipSuggest.FromGratings(Test(), [GratingView.Left, GratingView.Top])!;
        Assert.Equal(0, s[0].AngleDeg);
        Assert.Equal(90, s[1].AngleDeg);
        // Different directions keep them apart, so each just takes its most vivid square.
        Assert.Equal(1.2, s[0].PitchUm, 2);
        Assert.Equal(1.2, s[1].PitchUm, 2);
    }

    [Fact]
    public void AnEmptyTestSuggestsNothing() =>
        Assert.Null(FlipSuggest.FromGratings(new GratingSet(), [GratingView.Left, GratingView.Right]));
}
