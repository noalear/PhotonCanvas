using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class BevelTests
{
    [Fact]
    public void TheSlopeGrowsWithDistanceFromTheMiddle()
    {
        Assert.Equal(0, Bevel.AngleAt(0, 300), 6);
        Assert.Equal(45, Bevel.AngleAt(300, 300), 6);
        Assert.Equal(Bevel.AngleAt(-80, 300), Bevel.AngleAt(80, 300)); // either side
        // DistanceFor is AngleAt turned round.
        Assert.Equal(12, Bevel.AngleAt(Bevel.DistanceFor(12, 350), 350), 6);
        // The F2 Ultra's field: an edge 105 mm out with the pivot 300 mm up slopes about 19°.
        Assert.InRange(Bevel.MaxAngle(MachineProfile.F2Ultra, 300), 19, 20);
    }

    [Fact]
    public void ThePivotComesBackFromSimulatedCalipers()
    {
        // A cut as the slanted beam makes it: the bottom face is the top scaled about the pivot by (H + t) ÷ H.
        // The kerf (0.15 at the top, 0.25 at the bottom) takes off the same from every bar and adds to every hole.
        const double H = 320, t = 6, kTop = 0.15, kBottom = 0.25;
        (double Bar, double Hole) Cut(double l) => (l * (H + t) / H - kBottom, l + kTop);
        var (lb, lh) = Cut(150);
        var (sb, sh) = Cut(40);
        Assert.Equal(H, Bevel.PivotFromBars(t, 150, lb, lh, 40, sb, sh)!.Value, 6);
        // Bars no longer than their holes: nothing to read.
        Assert.Null(Bevel.PivotFromBars(t, 150, 150, 150, 40, 40, 40));
    }

    [Fact]
    public void TheBarsFitInTheFieldWithoutCrossing()
    {
        var s = Bevel.AddTest(DocState.New(), MachineProfile.F2Ultra, Layer.Default(0, LayerMode.Cut) with { Name = "Cut 6 mm" });
        Assert.True(Bevel.InDesign(s));
        var layer = s.Layers.Single(l => l.Name == Bevel.TestLayer);
        Assert.Equal(LayerMode.Cut, layer.Mode);
        var bars = s.Shapes.Where(x => x.LayerId == layer.Id).Select(x => x.Bounds).ToList();
        Assert.Equal(4, bars.Count);
        var field = new Rect2(0, 0, MachineProfile.F2Ultra.FieldW, MachineProfile.F2Ultra.FieldH);
        Assert.All(bars, b => Assert.True(field.Contains(b)));
        for (int i = 0; i < bars.Count; i++)
            for (int j = i + 1; j < bars.Count; j++)
                Assert.False(bars[i].Intersects(bars[j]), $"bars {i} and {j} cross");
        var (longMm, shortMm) = Bevel.BarLengths(MachineProfile.F2Ultra);
        Assert.Equal([longMm, shortMm, 12, 12], bars.Select(b => Math.Round(b.Width, 6)));
    }
}
