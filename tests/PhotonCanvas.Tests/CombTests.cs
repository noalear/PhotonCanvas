using Clipper2Lib;
using PhotonCanvas.Addon.Automata;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>The music box comb: tine lengths from the cantilever formula, tines on the strip's lanes, the clamp bars, and placing it.</summary>
public class CombTests
{
    static readonly CombMetal Steel = CombMetal.Find("1095");

    [Fact]
    public void ATineIsAsLongAsACantileverRingingAtItsNote()
    {
        // f = (1.8751² / 2π) · t/L² · √(E / 12ρ): for spring steel, k ≈ 825.5 m/s.
        Assert.Equal(825.5, CombSpec.Stiffness(Steel) / 1000, 0);
        // C5 (523.25 Hz) in 0.508 mm steel: √(825 500 × 0.508 / 523.25) ≈ 28.3 mm.
        Assert.Equal(28.3, CombSpec.LengthFor(CombSpec.Hz(72), 0.508, Steel), 1);
        Assert.Equal(440, CombSpec.Hz(69), 6);
        foreach (double hz in new[] { 130.8, 440, 1318.5 })
            foreach (double t in CombSpec.Stock)
                Assert.Equal(hz, CombSpec.FrequencyOf(CombSpec.LengthFor(hz, t, Steel), t, Steel), 6);
        // Twice as thick, √2 times as long; an octave up, 1/√2 as long.
        Assert.Equal(Math.Sqrt(2), CombSpec.LengthFor(500, 1, Steel) / CombSpec.LengthFor(500, 0.5, Steel), 6);
        var spec = new CombSpec();
        var up = (spec with { Octave = 1 }).Tines();
        foreach (var (a, b) in spec.Tines().Zip(up)) Assert.Equal(a.LengthMm / Math.Sqrt(2), b.LengthMm, 6);
        // Brass is floppier for its weight: the same note wants a shorter tine.
        Assert.True(CombSpec.LengthFor(500, 0.5, CombMetal.Find("brass")) < CombSpec.LengthFor(500, 0.5, Steel));
    }

    [Theory]
    [InlineData("15", false)]
    [InlineData("30", false)]
    [InlineData("15", true)]
    public void TheTinesAreOnTheStripsLanes(string key, bool mirror)
    {
        var movement = Movement.Find(key)!;
        var comb = new CombSpec { Mirror = mirror }.WithMovement(movement);
        var strip = new StripSpec { Mirror = mirror }.WithMovement(movement);
        var tines = comb.Tines();
        Assert.Equal(strip.Lanes(), tines.Select(t => t.Pitch));
        // The strip's lane i is FirstMm + i × pitch from its edge (or from the far edge, mirrored).
        for (int i = 0; i < tines.Count; i++)
            Assert.Equal(mirror ? movement.WidthMm - movement.FirstMm - i * movement.PitchMm : movement.FirstMm + i * movement.PitchMm, tines[i].X, 6);
        // Lower notes, longer tines.
        Assert.All(tines.Zip(tines.Skip(1)), p => Assert.True(p.First.LengthMm > p.Second.LengthMm));
    }

    [Theory]
    [InlineData("15", true)]
    [InlineData("30", true)]
    [InlineData("15", false)]
    public void EachTineIsFreeToItsRootAndClampedThere(string key, bool alignTips)
    {
        var spec = new CombSpec { AlignTips = alignTips }.WithMovement(Movement.Find(key)!);
        spec = spec with { ThicknessMm = spec.Suggest() };
        var m = spec.Build();
        var comb = m.Parts.Single(p => p.Name == "Comb").Shape;
        var clamp = m.Parts.Single(p => p.Name == "Clamp bar");
        Assert.Equal(2, clamp.Copies);
        Assert.NotNull(clamp.Stock);
        // The comb is one piece (with its bolt holes).
        Assert.Single(comb, p => Clipper.IsPositive(p));
        Assert.Single(clamp.Shape, p => Clipper.IsPositive(p));
        var tines = spec.Tines();
        double w = spec.TineWidthMm, lMax = tines.Max(t => t.LengthMm);
        foreach (var t in tines)
        {
            double root = alignTips ? t.LengthMm : lMax, tip = root - t.LengthMm;
            foreach (double f in new[] { 0.05, 0.5, 0.97 })
            {
                double y = tip + f * t.LengthMm;
                // Metal across the tine's width, a gap each side of it, all the way to its root.
                Assert.True(Poly.Inside(comb, new Vec2(t.X, y)), $"{Notes.Name(t.Pitch)} has no metal at {y:0.0}");
                Assert.True(Poly.Inside(comb, new Vec2(t.X - w / 2 + 0.03, y)) && Poly.Inside(comb, new Vec2(t.X + w / 2 - 0.03, y)), $"{Notes.Name(t.Pitch)} is narrower than {w} mm at {y:0.0}");
                Assert.False(Poly.Inside(comb, new Vec2(t.X - w / 2 - 0.05, y)) || Poly.Inside(comb, new Vec2(t.X + w / 2 + 0.05, y)), $"{Notes.Name(t.Pitch)} is joined to something at {y:0.0}");
            }
            // Nothing past its tip.
            Assert.False(Poly.Inside(comb, new Vec2(t.X, tip - 0.1)));
            // The clamp bar's edge is at its root: clear of the tine in front, over it just behind.
            Assert.False(Poly.Inside(clamp.Shape, new Vec2(t.X, root - 0.1)));
            Assert.True(Poly.Inside(clamp.Shape, new Vec2(t.X, root + 0.1)));
        }
        // A name for every tine, by its root.
        Assert.Equal(tines.Select(t => Notes.Name(t.Pitch)), clamp.Labels.Select(l => l.Text));
        Assert.DoesNotContain(m.Findings, f => f.Level == Severity.Bad);
        Assert.Empty(ToyPlacement.TooBig(m, MachineProfile.F2Ultra));
    }

    [Fact]
    public void TheSuggestedThicknessKeepsTheTinesAGoodLength()
    {
        foreach (var movement in Movement.All)
        {
            var spec = new CombSpec().WithMovement(movement);
            double t = spec.Suggest();
            Assert.Contains(t, CombSpec.Stock);
            var tines = (spec with { ThicknessMm = t }).Tines();
            Assert.InRange(tines.Max(x => x.LengthMm), 20, CombSpec.LongestWanted);
            Assert.True(tines.Min(x => x.LengthMm) >= CombSpec.ShortestWanted);
            Assert.True(t <= spec.TineWidthMm / 2);
            // It's the thickest that does: the next size up makes the bass tine too long.
            int i = CombSpec.Stock.IndexOf(t);
            if (i + 1 < CombSpec.Stock.Length && CombSpec.Stock[i + 1] <= spec.TineWidthMm / 2)
                Assert.True((spec with { ThicknessMm = CombSpec.Stock[i + 1] }).Tines().Max(x => x.LengthMm) > CombSpec.LongestWanted);
        }
    }

    [Fact]
    public void TheCheckWarnsOfStubbyLongAndNarrowTines()
    {
        var thick = new CombSpec { ThicknessMm = 1.6, GapMm = 0.4 }.Build();
        Assert.Contains(thick.Findings, f => f.Level == Severity.Warn && f.Text.Contains("wobble sideways"));
        var high = new CombSpec { Octave = 2, ThicknessMm = 0.3 }.Build();
        Assert.Contains(high.Findings, f => f.Level == Severity.Warn && f.Text.Contains("stubby"));
        var low = (new CombSpec { Octave = -1, ThicknessMm = 0.8 }).WithMovement(Movement.Find("30")!).Build();
        Assert.Contains(low.Findings, f => f.Level == Severity.Warn && f.Text.Contains("ring quietly"));
        Assert.Contains(new CombSpec { Metal = "annealed" }.Build().Findings, f => f.Level == Severity.Warn && f.Text.Contains("harden"));
        Assert.Throws<InvalidOperationException>(() => new CombSpec { GapMm = 1.8 }.Build());
    }

    [Fact]
    public void TheClampBarsGoOnAPageOfTheirOwnAndTheCombCanBeOpenedAgain()
    {
        var spec = new CombSpec();
        Assert.Equal(spec.ToJson(), Assert.IsType<CombSpec>(ToySpec.FromJson(spec.ToJson())).ToJson());
        var m = spec.Build();
        var (s, sheets, id) = Placement.Place(DocState.New(), m, spec.ToJson(), MachineProfile.F2Ultra, new PlaceOptions(), ToyPlacement.Target);
        Assert.Equal(2, sheets);
        // The comb on the page shown, on the usual cut layer.
        var cut = s.Layers.Single(l => l.Name == "Toy · cut");
        Assert.Contains(s.Shapes, x => x.Name == "Comb" && x.LayerId == cut.Id);
        Assert.DoesNotContain(s.Shapes, x => x.Name.StartsWith("Clamp bar", StringComparison.Ordinal));
        Assert.IsType<CombSpec>(ToyPlacement.Find(s, Placement.ShapesOf(s, id, ToyPlacement.Target))!.Value.Spec);
        // The clamp bars on the next, on a cut layer for the plate, each with a note name by each tine.
        var next = Pages.SwitchTo(s, s.PageIndex + 1);
        var plate = next.Layers.Single(l => l.Name == "Toy · cut (3 mm steel)");
        Assert.Equal(2, next.Shapes.Count(x => x.LayerId == plate.Id && x.Name.StartsWith("Clamp bar", StringComparison.Ordinal)));
        Assert.Equal(2 * spec.Tines().Count, next.Shapes.OfType<TextShape>().Count());
        // Made again: both pages' parts go, and the clamp bars' page with them.
        var again = Placement.Place(s, (spec with { ThicknessMm = 0.635 }).Build(), spec.ToJson(), MachineProfile.F2Ultra, new PlaceOptions { Replace = id }, ToyPlacement.Target);
        Assert.Equal(id, again.Id);
        Assert.Equal(1 + s.OtherPages.Count, 1 + again.State.OtherPages.Count);
    }
}
