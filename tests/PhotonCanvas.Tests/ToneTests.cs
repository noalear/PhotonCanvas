using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Tone tests: a burned grey wedge read off a photo, and pictures corrected through it.</summary>
public class ToneTests
{
    /// <summary>A material that goes dark early: lightness = grey^2.2 (half grey comes out only 22% light).</summary>
    static double Wood(double grey) => Math.Pow(grey, 2.2);

    [Fact]
    public void CurveTurnsRoundToTheGreyToSend()
    {
        var pts = Enumerable.Range(0, 11).Select(i => i / 10.0).Select(g => (g, Wood(g))).ToList();
        var c = new ToneCurve(pts);
        // Sending the corrected grey gives the tone wanted.
        foreach (var want in new[] { 0.1, 0.3, 0.5, 0.75, 0.9 })
            Assert.Equal(want, c.Response(c.Correct(want)), 2);
        // Darkening early means the corrected greys are lighter than asked for.
        Assert.True(c.Correct(0.5) > 0.5);
        // Black: the lightest grey already within a whisker of the darkest (2^-2.2 ≈ 0.22 light at 0.5, 0.02 at about 0.17).
        Assert.InRange(c.Correct(0), 0.1, 0.2);
        Assert.Equal(1, c.Correct(1), 6);
    }

    [Fact]
    public void NoisyAndSaturatedReadingsStillGiveAUsableCurve()
    {
        // Black already reached by 30% grey, and one reading out of order.
        var c = new ToneCurve([(0, 0), (0.15, 0.01), (0.3, 0), (0.5, 0.4), (0.6, 0.35), (0.8, 0.8), (1, 1)]);
        var m = c.Monotone;
        for (int i = 1; i < m.Count; i++) Assert.True(m[i].Lightness >= m[i - 1].Lightness);
        // Black: the lightest grey that's already as dark as it goes (no more heat than needed).
        Assert.InRange(c.Correct(0), 0.25, 0.31);
        Assert.InRange(c.Range().DarkFrom, 0.25, 0.31);
        // Corrected greys only go up with the tone wanted.
        double last = -1;
        for (int k = 0; k <= 100; k++) { double g = c.Correct(k / 100.0); Assert.True(g >= last - 1e-12); last = g; }
    }

    [Fact]
    public void StripIsFoundWhereverItIsMovedAndReadOffAPhoto()
    {
        var s = DocState.New();
        var picLayer = Layer.Default(1, LayerMode.Image) with { Name = "Photo", Dpi = 254 };
        s = Ops.AddLayer(s, picLayer);
        var (state, id) = ToneTest.Add(s, picLayer, 11, 6, new Vec2(20, 30));
        var layer = state.Layers.Single(ToneTest.IsTestLayer);
        Assert.Equal("Tone test · Photo", layer.Name);
        Assert.Null(layer.MaterialTone);
        // Moved after adding: the patches follow.
        state = Ops.Transform(state, new HashSet<Guid> { id }, Affine.Translate(15, 40));
        var strip = ToneTest.Strip(state)!;
        var patches = ToneTest.Patches(strip);
        Assert.Equal(11, patches.Count);
        Assert.Equal(35, patches[0].Bed.MinX, 6);
        Assert.Equal(70, patches[0].Bed.MinY, 6);
        Assert.Equal(6, patches[0].Bed.Width, 6);
        Assert.Equal(0, patches[0].Level);
        Assert.Equal(1, patches[^1].Level);

        // A photo of it burned on "wood": light tan bare, dark brown burn, darkening early.
        var area = new Rect2(0, 0, 200, 150);
        const double mmPerPx = 0.25;
        int w = (int)(area.Width / mmPerPx), h = (int)(area.Height / mmPerPx);
        var px = new byte[w * h * 4];
        (byte, byte, byte) bare = (210, 180, 140), burn = (60, 35, 20);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vec2((x + 0.5) * mmPerPx, (y + 0.5) * mmPerPx);
                double light = 1;
                foreach (var (level, bed) in patches)
                    if (p.X >= bed.MinX && p.X <= bed.MaxX && p.Y >= bed.MinY && p.Y <= bed.MaxY) light = Wood(level);
                int k = (y * w + x) * 4;
                px[k] = (byte)Math.Round(burn.Item1 + (bare.Item1 - burn.Item1) * light);
                px[k + 1] = (byte)Math.Round(burn.Item2 + (bare.Item2 - burn.Item2) * light);
                px[k + 2] = (byte)Math.Round(burn.Item3 + (bare.Item3 - burn.Item3) * light);
                px[k + 3] = 255;
            }
        var reading = ToneTest.Read(strip, new RasterImage(w, h, px), area, mmPerPx)!;
        Assert.NotNull(reading);
        foreach (var g in new[] { 0.2, 0.5, 0.8 }) Assert.InRange(reading.Curve.Response(g), Wood(g) - 0.02, Wood(g) + 0.02);
        Assert.Equal(0xD2B48Cu, reading.BareColour);
        // The colours travel with the curve.
        Assert.Equal(0xD2B48Cu, reading.Curve.BareColour);
        Assert.Equal(0x3C2314u, reading.Curve.BurnColour);
        // Mid-grey, corrected, comes out half-way between burned and bare: (60 + 210) / 2 = 135 red.
        Assert.InRange((int)(reading.Curve.Burned(0.5, corrected: true) >> 16), 130, 140);
        Assert.Equal(0x3C2314u, reading.BurnColour);

        // A simulated picture: corrected, a mid-grey comes out mid-way between burned and bare.
        var mid = new RasterImage(1, 1, [128, 128, 128, 255]);
        var straight = ToneTest.Simulate(mid, reading.Curve, reading.BareColour, reading.BurnColour, corrected: false);
        var fixedUp = ToneTest.Simulate(mid, reading.Curve, reading.BareColour, reading.BurnColour, corrected: true);
        Assert.InRange(fixedUp.Rgba[0], 130, 140);           // (60 + 210) / 2 = 135
        Assert.True(straight.Rgba[0] < fixedUp.Rgba[0] - 20); // uncorrected comes out much darker
    }

    [Fact]
    public void PicturesAreCorrectedAndTheCurveIsSaved()
    {
        var curve = new ToneCurve([.. Enumerable.Range(0, 11).Select(i => i / 10.0).Select(g => (g, Wood(g)))]);
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Image) with { ImageMode = ImageEngraveMode.Grayscale, Dpi = 127, GrayMinPower = 0 };
        s = Ops.AddLayer(s, layer);
        // A flat mid-grey picture.
        var png = new RasterImage(4, 4, [.. Enumerable.Repeat(new byte[] { 128, 128, 128, 255 }, 16).SelectMany(b => b)]).EncodePng();
        var pic = new ImageShape { LayerId = layer.Id, Source = ImageSource.FromEncoded(png), SizeMm = new Vec2(4, 4), Transform = Affine.Translate(50, 50) };
        s = Ops.AddShapes(s, [pic]);
        var (plain, _) = JobBuilder.Rasterize(pic, layer);
        var (matched, _) = JobBuilder.Rasterize(pic, layer with { MaterialTone = curve });
        // Corrected: a lighter grey is sent, so less burn.
        Assert.True(matched!.Levels![0] < plain!.Levels![0] - 40);

        var withCurve = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { MaterialTone = curve });
        var back = DocFile.Deserialize(DocFile.Serialize(withCurve)).Layers.Single(l => l.Id == layer.Id).MaterialTone!;
        Assert.Equal(curve.Points.Count, back.Points.Count);
        Assert.Equal(curve.Correct(0.5), back.Correct(0.5), 9);
        // Colours kept through saving, and an older file with only the points still reads.
        var coloured = curve with { BareColour = 0xD2B48C, BurnColour = 0x3C2314 };
        var back2 = DocFile.Deserialize(DocFile.Serialize(Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { MaterialTone = coloured })))
            .Layers.Single(l => l.Id == layer.Id).MaterialTone!;
        Assert.Equal(0xD2B48Cu, back2.BareColour);
        Assert.Equal(0x3C2314u, back2.BurnColour);
        var old = DocFile.ReadTone(System.Text.Json.Nodes.JsonNode.Parse("[[0,0],[0.5,0.2],[1,1]]"))!;
        Assert.Equal(3, old.Points.Count);
        Assert.Null(old.BareColour);
        // And in the material library's own format.
        var settings = PhotonCanvas.Doc.Materials.ProcessSettings.From(layer with { MaterialTone = curve });
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var again = System.Text.Json.JsonSerializer.Deserialize<PhotonCanvas.Doc.Materials.ProcessSettings>(json)!;
        Assert.Equal(curve.Correct(0.5), again.MaterialTone!.Correct(0.5), 9);
        var againColoured = System.Text.Json.JsonSerializer.Deserialize<PhotonCanvas.Doc.Materials.ProcessSettings>(
            System.Text.Json.JsonSerializer.Serialize(PhotonCanvas.Doc.Materials.ProcessSettings.From(layer with { MaterialTone = coloured })))!;
        Assert.Equal(0xD2B48Cu, againColoured.MaterialTone!.BareColour);
    }
}
