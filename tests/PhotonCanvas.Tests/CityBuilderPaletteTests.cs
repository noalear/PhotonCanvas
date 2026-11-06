using PhotonCanvas.Addon.CityBuilder;
using SkiaSharp;

namespace PhotonCanvas.Tests;

/// <summary>The pictures on the city builder's palettes: one for every ground, road surface, wall and thing, each its own.</summary>
public class CityBuilderPaletteTests
{
    static SKBitmap Picture(byte[] png)
    {
        var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);
        Assert.InRange(bmp.Width, PalettePictures.Px - 8, PalettePictures.Px + 16);
        Assert.Equal(bmp.Width, bmp.Height);
        return bmp;
    }

    [Fact]
    public void Every_ground_road_wall_and_thing_has_a_picture_of_its_own()
    {
        var all = new List<byte[]>();
        foreach (var g in Enum.GetValues<Ground>()) all.Add(PalettePictures.Ground(g));
        foreach (var g in new[] { Ground.Road, Ground.Dirt, Ground.Paving, Ground.Sand }) all.Add(PalettePictures.Road(g));
        foreach (var k in Enum.GetValues<FenceKind>()) all.Add(PalettePictures.Wall(k));
        foreach (var k in Enum.GetValues<PropKind>()) all.Add(PalettePictures.Thing(k));
        foreach (var png in all) Picture(png).Dispose();
        // No two alike: each shows what it is.
        Assert.Equal(all.Count, all.Select(Convert.ToBase64String).Distinct().Count());
        // Made once and kept.
        Assert.Same(PalettePictures.Thing(PropKind.Well), PalettePictures.Thing(PropKind.Well));
    }

    [Fact]
    public void A_thing_is_drawn_in_the_middle_of_its_picture()
    {
        // The middle of a well's picture isn't the paving round its edge.
        using var well = Picture(PalettePictures.Thing(PropKind.Well));
        var middle = well.GetPixel(well.Width / 2, well.Height / 2);
        var edge = well.GetPixel(2, 2);
        Assert.True(Math.Abs(middle.Red - edge.Red) + Math.Abs(middle.Green - edge.Green) + Math.Abs(middle.Blue - edge.Blue) > 30, $"{middle} vs {edge}");
    }
}
