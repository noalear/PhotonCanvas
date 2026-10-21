using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PrintCutTests
{
    [Fact]
    public void ACutGoesRoundEachStickerWithAMargin()
    {
        // A photo of paper 100 × 60 mm at 0.2 mm a pixel: a red disc, a blue square with a white
        // window in it, and a speck; the paper a little uneven.
        const double mm = 0.2;
        int w = 500, h = 300;
        var px = new byte[w * h * 4];
        var rnd = new Random(1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vec2((x + 0.5) * mm, (y + 0.5) * mm);
                (int r, int g, int b) c = (238, 236, 232);
                c = (c.r - x / 40, c.g - x / 40, c.b - x / 40); // light falling off
                if (p.Dist(new Vec2(25, 30)) < 8) c = (200, 40, 40);
                if (p.X > 60 && p.X < 80 && p.Y > 20 && p.Y < 40 && !(p.X > 68 && p.X < 72 && p.Y > 28 && p.Y < 32)) c = (40, 60, 190);
                if (p.X > 90 && p.X < 91 && p.Y > 50 && p.Y < 51) c = (20, 20, 20);
                int n = rnd.Next(-4, 5);
                int k = (y * w + x) * 4;
                px[k] = (byte)Math.Clamp(c.r + n, 0, 255); px[k + 1] = (byte)Math.Clamp(c.g + n, 0, 255); px[k + 2] = (byte)Math.Clamp(c.b + n, 0, 255); px[k + 3] = 255;
            }
        var area = new Rect2(0, 0, 100, 60);
        var cuts = PrintCut.Find(new RasterImage(w, h, px), area, mm, area, new PrintCutOptions { MarginMm = 1 });
        var loops = cuts.Flatten(0.02).Where(f => f.Closed).Select(f => f.Points).ToList();
        // Two stickers: no cut round the speck, none round the square's window.
        Assert.Equal(2, loops.Count);
        var disc = loops.Single(l => Geom.Inside(l, new Vec2(25, 30)));
        var square = loops.Single(l => Geom.Inside(l, new Vec2(62, 22)));
        // A millimetre out: the disc's cut about 9 mm round, the square's about 22 mm across.
        Assert.InRange(Rect2.Bounds(disc).Width, 17.4, 18.8);
        Assert.InRange(Rect2.Bounds(square).Width, 21.4, 22.8);
        Assert.True(Geom.Inside(square, new Vec2(70, 30))); // the window is inside the one cut
    }
}
