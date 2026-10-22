using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class SeeThroughTests
{
    [Fact]
    public void SeeThroughPartsAreNeverBurnedEvenInverted()
    {
        // 10 × 10 px: the left half grey and solid, the right half see-through.
        var px = new byte[10 * 10 * 4];
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
            {
                int o = (y * 10 + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = 128;
                px[o + 3] = (byte)(x < 5 ? 255 : 0);
            }
        var layer = Layer.Default(0, LayerMode.Image) with { Dpi = 254, Dither = DitherMethod.Threshold };
        var pic = new ImageShape { LayerId = layer.Id, Source = ImageSource.FromEncoded(new RasterImage(10, 10, px).ToPng()), SizeMm = new Vec2(10, 10), Tone = ToneAdjust.None with { Invert = true } };
        var (block, _) = JobBuilder.Rasterize(pic, layer);
        Assert.NotNull(block);
        var bits = block!.Bitmap;
        // Inverted mid-grey is burned on the solid half; nothing on the see-through half.
        int left = 0, right = 0;
        for (int r = 0; r < bits.Height; r++)
            for (int c = 0; c < bits.Width; c++)
                if (bits.Bits[r * bits.Width + c]) { if (c < bits.Width / 2) left++; else right++; }
        Assert.True(left > 0);
        Assert.Equal(0, right);
    }
}
