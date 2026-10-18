using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using Xunit;

namespace PhotonCanvas.Tests;

public class TestGridReaderTests
{
    [Fact]
    public void TheBestSquareIsFoundOnAPhoto()
    {
        // A 60 × 20 mm photo at 0.25 mm: bare wood (200), three 10 mm squares: faint (175), dark and even (90), darker but blotchy (60 ± 50).
        int w = 240, h = 80;
        var px = new byte[w * h * 4];
        var squares = new[] { new Rect2(5, 5, 15, 15), new Rect2(25, 5, 35, 15), new Rect2(45, 5, 55, 15) };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double mx = (x + 0.5) * 0.25, my = (y + 0.5) * 0.25;
                int v = 200;
                if (squares[0].Contains(new Vec2(mx, my))) v = 175;
                if (squares[1].Contains(new Vec2(mx, my))) v = 90;
                if (squares[2].Contains(new Vec2(mx, my))) v = (x / 4 + y / 4) % 2 == 0 ? 10 : 110;
                int k = (y * w + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = (byte)v;
                px[k + 3] = 255;
            }
        var photo = new RasterImage(w, h, px);
        var area = new Rect2(0, 0, 60, 20);
        var readings = squares.Select(s => TestGridReader.Read(photo, area, 0.25, s)).ToList();
        Assert.Equal(25, readings[0]!.Contrast, 0);
        Assert.Equal(110, readings[1]!.Contrast, 0);
        Assert.True(readings[2]!.Spread > 40);
        Assert.Equal(1, TestGridReader.Best(readings, TestGoal.DarkestEven));
        Assert.Equal(0, TestGridReader.Best(readings, TestGoal.LightestVisible));
    }
}
