using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class DowelPinsTests
{
    [Fact]
    public void Pins_are_shorter_than_two_slices_with_spares_rounded_up()
    {
        var plan = DowelPins.Plan(new DowelPinOptions { PinsNeeded = 21, ThicknessMm = 3, ShorterShare = 0.15, SpareShare = 0.10, DowelDiameterMm = 3, DowelLengthMm = 300, KerfMm = 0.2 }, 200);
        Assert.Equal(5.1, plan.PinLengthMm, 6);
        Assert.Equal(24, plan.Wanted); // 21 × 1.1 = 23.1, rounded up
        Assert.Equal(24, plan.Made);
        Assert.False(plan.Short);
        // A cut squaring the end, then one after each pin.
        Assert.Equal(25, plan.CutsAtMm.Count);
        Assert.Equal(3.6, plan.CutLineMm, 6);
        // Between neighbouring cuts' edges: exactly a pin.
        for (int i = 1; i < plan.CutsAtMm.Count; i++)
            Assert.Equal(plan.PinLengthMm, plan.CutsAtMm[i] - plan.CutsAtMm[i - 1] - 0.2, 6);
    }

    [Fact]
    public void Exactly_ten_percent_more_does_not_round_up_again()
    {
        var plan = DowelPins.Plan(new DowelPinOptions { PinsNeeded = 10, SpareShare = 0.10 }, 200);
        Assert.Equal(11, plan.Wanted);
    }

    [Fact]
    public void A_short_dowel_makes_what_it_can_and_says_so()
    {
        var plan = DowelPins.Plan(new DowelPinOptions { PinsNeeded = 40, ThicknessMm = 3, ShorterShare = 0.15, DowelLengthMm = 50, KerfMm = 0.2 }, 200);
        Assert.True(plan.Short);
        Assert.Equal(44, plan.Wanted);
        Assert.Equal(9, plan.Made); // (50 − 1 − 0.1) / 5.3 = 9.2
        Assert.True(plan.UsedMm <= 50);
        // The bed limits it too.
        Assert.Equal(DowelPins.Plan(new DowelPinOptions { PinsNeeded = 40, DowelLengthMm = 1000 }, 50).Made, plan.Made);
    }

    [Fact]
    public void The_job_is_cut_lines_across_the_dowel_on_a_page_of_its_own()
    {
        var plan = DowelPins.Plan(new DowelPinOptions { PinsNeeded = 5, DowelDiameterMm = 3, DowelLengthMm = 300 }, 200);
        var start = DocState.New();
        var s = DowelPins.Place(start, plan, 3, 300, MachineProfile.F2Ultra);
        Assert.Equal(start.OtherPages.Count + 1, s.OtherPages.Count);
        var cut = Assert.Single(s.Layers, l => l.Name == DowelPins.CutLayer);
        Assert.Equal(LayerMode.Cut, cut.Mode);
        var lines = s.Shapes.OfType<PathShape>().Where(p => p.LayerId == cut.Id).ToList();
        Assert.Equal(plan.CutsAtMm.Count, lines.Count);
        Assert.All(lines, l => Assert.Equal(3.6, Math.Max(l.Bounds.Width, l.Bounds.Height), 3));
        // The dowel is drawn on the material layer, which is never sent.
        var material = MaterialOutline.Layer(s);
        Assert.NotNull(material);
        Assert.False(material!.Output);
        Assert.Single(MaterialOutline.On(s));
        Assert.All(s.Shapes, sh => Assert.True(MachineProfile.F2Ultra.Contains(sh.Bounds.MinX, sh.Bounds.MinY) && MachineProfile.F2Ultra.Contains(sh.Bounds.MaxX, sh.Bounds.MaxY)));
    }
}
