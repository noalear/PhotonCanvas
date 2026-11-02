using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.Addon.BurnPreview;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>"See it before you burn it": the design drawn as it will come out on the camera's picture of the material.</summary>
public class BurnPreviewTests
{
    static readonly Rect2 Bed = new(0, 0, 40, 40);
    const double MmPerPx = 0.5;

    static (byte R, byte G, byte B) At(BurnPreviewResult r, double xMm, double yMm)
    {
        var img = r.Picture.Image;
        int x = (int)(xMm / MmPerPx), y = (int)(yMm / MmPerPx);
        int k = (y * img.Width + x) * 4;
        return (img.Rgba[k], img.Rgba[k + 1], img.Rgba[k + 2]);
    }

    static uint Rgb((byte R, byte G, byte B) c) => (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    static (DocState State, Layer Layer) WithSquare(Layer layer, double x = 10, double y = 10, double size = 10)
    {
        var s = Ops.AddLayer(DocState.New(), layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(x, y, size, size) }]);
        return (s, layer);
    }

    [Fact]
    public void FillTakesTheColourOfItsColourTestSquare()
    {
        var bare = new Lab(70, 0, 2);
        var gold = new Lab(62, 8, 40);
        var layer = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Red(40, 300, 200, 60), LinesPerCm = 300 };
        var palette = new ColourPalette
        {
            Name = "SS304", BareL = bare.L, BareA = bare.A, BareB = bare.B,
            Swatches = [new ColourSwatch { Row = 2, Col = 4, Settings = ProcessSettings.From(layer), L = gold.L, A = gold.A, B = gold.B }],
        };
        var material = new Material { Name = "Stainless", Category = "Metal", Palettes = [palette] };
        var lib = new MaterialLibrary(Path.GetTempPath());
        lib.Upsert(material);
        layer = layer with { Material = new MaterialRef(material.Id, material.Label) };
        var (state, _) = WithSquare(layer);
        var (br, bg, bb) = ColourScience.LabToSrgb(bare);
        var bed = BedPicture.Plain(Bed, MmPerPx, (uint)br << 16 | (uint)bg << 8 | bb);
        // The plain picture has a faint grain: compare against what was there.
        var r = BurnPreview.Render(bed, state, lib);
        var look = Assert.Single(r.Layers).Look;
        Assert.Equal(LookSource.ColourTest, look.Source);
        Assert.Contains("row 3 column 5", look.About);
        var inside = ColourScience.SrgbToLab(At(r, 15, 15).R, At(r, 15, 15).G, At(r, 15, 15).B);
        Assert.True(ColourScience.DeltaE2000(inside, gold) < 3, $"ΔE {ColourScience.DeltaE2000(inside, gold):0.0}");
        // Outside the square the material is untouched.
        int k = (int)(5 / MmPerPx) * bed.Image.Width * 4 + (int)(5 / MmPerPx) * 4;
        Assert.Equal(bed.Image.Rgba[k], r.Picture.Image.Rgba[k]);
        Assert.True(r.AllMeasured);
    }

    [Fact]
    public void NearbySettingsUseTheNearestSquareAndFarOnesAGuess()
    {
        var tested = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Red(40, 300, 200, 60) };
        var palette = new ColourPalette { Name = "p", Swatches = [new ColourSwatch { Settings = ProcessSettings.From(tested), L = 40 }] };
        var material = new Material { Name = "Steel", Category = "Metal", Palettes = [palette] };
        var lib = new MaterialLibrary(Path.GetTempPath());
        lib.Upsert(material);
        var mref = new MaterialRef(material.Id, material.Label);
        var near = tested with { Laser = tested.Laser with { Power = 42 }, Material = mref };
        Assert.Equal(LookSource.NearestColourTest, BurnLooks.For(near, lib).Source);
        var far = tested with { Laser = tested.Laser with { Power = 90, Speed = 900 }, Material = mref };
        Assert.Equal(LookSource.Estimate, BurnLooks.For(far, lib).Source);
        var diode = tested with { Laser = LaserSettings.Blue(40, 300), Material = mref };
        Assert.Equal(double.PositiveInfinity, BurnLooks.SettingsDistance(diode, palette.Swatches[0].Settings));
    }

    [Fact]
    public void MarksKeepTheMaterialsOwnGrain()
    {
        // Two stripes of differently coloured "grain" under one fill: both darken by the same share.
        int w = (int)(Bed.Width / MmPerPx), h = (int)(Bed.Height / MmPerPx);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                byte v = (byte)(y % 4 < 2 ? 220 : 170);
                px[k] = v; px[k + 1] = (byte)(v * 0.85); px[k + 2] = (byte)(v * 0.6); px[k + 3] = 255;
            }
        var bed = new BedPicture(new RasterImage(w, h, px), Bed, MmPerPx);
        var layer = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(90, 200), LinesPerCm = 100 };
        var (state, _) = WithSquare(layer);
        var r = BurnPreview.Render(bed, state, null);
        double light = ColourScience.ToLinear(At(r, 15, 16.0).R / 255.0), dark = ColourScience.ToLinear(At(r, 15, 15.0).R / 255.0);
        double ratioBefore = ColourScience.ToLinear(220 / 255.0) / ColourScience.ToLinear(170 / 255.0);
        Assert.Equal(ratioBefore, light / dark, 1);
        Assert.True(At(r, 15, 15).R < 150, "a strong diode fill darkens wood");
    }

    [Fact]
    public void PicturesComeOutThroughTheirToneTest()
    {
        var picLayer = Layer.Default(0, LayerMode.Image) with
        {
            MaterialTone = new ToneCurve([(0, 0), (0.5, 0.2), (1, 1)]) { BareColour = 0xE0C090, BurnColour = 0x302010 },
        };
        // Left half black, right half white.
        int w = 20, h = 10;
        var pp = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte v = (byte)(i % w < 10 ? 0 : 255);
            pp[i * 4] = pp[i * 4 + 1] = pp[i * 4 + 2] = v;
            pp[i * 4 + 3] = 255;
        }
        var src = ImageSource.FromEncoded(new RasterImage(w, h, pp).ToPng());
        var s = Ops.AddLayer(DocState.New(), picLayer);
        s = Ops.AddShapes(s, [new ImageShape { LayerId = picLayer.Id, Source = src, SizeMm = new Vec2(20, 10), Transform = Affine.Translate(10, 10) }]);
        var bed = new BedPicture(Solid(0xE0C090), Bed, MmPerPx);
        var r = BurnPreview.Render(bed, s, null);
        Assert.Equal(LookSource.ToneTest, Assert.Single(r.Layers).Look.Source);
        var black = At(r, 14, 15);
        // Corrected black is the lightest grey already within a whisker of the darkest: nearly the burn colour.
        Assert.InRange(black.R, 0x2C, 0x40);
        Assert.InRange(black.G, 0x1C, 0x30);
        Assert.Equal(0xE0C090u, Rgb(At(r, 26, 15)));
    }

    [Fact]
    public void ThroughCutsShowTheBedAndScorchWood()
    {
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Blue(100, 5) with { Passes = 2 } };
        // Outline through the middle of a pixel column (10.25 mm).
        var (state, _) = WithSquare(cut, x: 10.25);
        var r = BurnPreview.Render(new BedPicture(Solid(0xE3C79B), Bed, MmPerPx), state, null);
        var look = Assert.Single(r.Layers).Look;
        Assert.True(look.ThroughCut);
        Assert.Equal(LookSource.Estimate, look.Source);
        Assert.False(r.AllMeasured);
        // On the cut line: dark. A little beside it: scorched, lighter than the gap, darker than bare.
        var onLine = At(r, 10.25, 15);
        Assert.True(onLine.R < 40, $"cut line {onLine}");
        var beside = At(r, 9.75, 15);
        Assert.InRange(beside.R, onLine.R + 1, 0xE2);
        Assert.Equal(0xE3C79Bu, Rgb(At(r, 15, 15)));
    }

    [Fact]
    public void HiddenAndReferenceLayersAndZeroPowerLeaveNoMark()
    {
        var bedColour = 0xD8B98Au;
        foreach (var layer in new[]
                 {
                     Layer.Default(0, LayerMode.Fill) with { Visible = false },
                     Layer.Default(0, LayerMode.Fill) with { Output = false },
                     Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(0, 100) },
                 })
        {
            var (state, _) = WithSquare(layer);
            var r = BurnPreview.Render(new BedPicture(Solid(bedColour), Bed, MmPerPx), state, null);
            Assert.Equal(bedColour, Rgb(At(r, 15, 15)));
        }
    }

    [Fact]
    public void MoreEnergyMarksMore()
    {
        var weak = Layer.Default(0, LayerMode.Score) with { Laser = LaserSettings.Blue(10, 300) };
        var strong = weak with { Laser = LaserSettings.Blue(80, 50) };
        Assert.True(BurnLooks.For(strong, null).Strength > BurnLooks.For(weak, null).Strength);
        Assert.InRange(BurnLooks.For(strong, null).Strength, 0.9, 1);
    }

    static RasterImage Solid(uint c)
    {
        int w = (int)(Bed.Width / MmPerPx), h = (int)(Bed.Height / MmPerPx);
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)(c >> 16); px[i * 4 + 1] = (byte)(c >> 8); px[i * 4 + 2] = (byte)c; px[i * 4 + 3] = 255;
        }
        return new RasterImage(w, h, px);
    }
}
