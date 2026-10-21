using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class EngravingLinesTests
{
    static double Area(PathGeometry g, Func<Vec2, bool> where) =>
        g.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3 && where(Rect2.Bounds(f.Points).Center)).Sum(f => Math.Abs(Geom.SignedArea(f.Points)));

    [Fact]
    public void LinesSwellWhereItsDark()
    {
        // 100 × 20 mm, black on the left fading to white on the right; lines across (angle 0), 1 mm apart.
        var area = new Rect2(0, 0, 100, 20);
        var o = new EngravingOptions { Style = EngravingStyle.Straight, AngleDeg = 0, SpacingMm = 1, MaxWidth = 0.8, MinWidthMm = 0.05 };
        var g = EngravingLines.Make(area, p => area.Contains(p) ? 1 - p.X / 100 : 0, o);
        double left = Area(g, c => c.X < 50), right = Area(g, c => c.X >= 50);
        Assert.True(left > right * 2, $"{left} vs {right}");
        // At black the 20 lines are 0.8 mm wide: about 0.8 of the area there is engraved.
        double nearBlack = Area(EngravingLines.Make(new Rect2(0, 0, 10, 20), _ => 1, o), _ => true);
        Assert.InRange(nearBlack / (10 * 20), 0.7, 0.9);
        // White: nothing.
        Assert.True(EngravingLines.Make(area, _ => 0, o).IsEmpty);
    }

    [Fact]
    public void RingsAndSpiralGoRoundTheCentre()
    {
        var area = new Rect2(0, 0, 40, 40);
        foreach (var style in new[] { EngravingStyle.Rings, EngravingStyle.Spiral })
        {
            var g = EngravingLines.Make(area, _ => 0.6, new EngravingOptions { Style = style, SpacingMm = 2, Centre = new Vec2(0.5, 0.5) });
            Assert.False(g.IsEmpty);
            var b = g.Bounds();
            Assert.True(b.Width > 35 && b.Height > 35);
        }
    }
}
