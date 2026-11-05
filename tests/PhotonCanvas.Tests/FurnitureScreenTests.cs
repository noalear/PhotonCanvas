using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>Folding screens: separate leaves on working hinges, standing in a zig-zag.</summary>
public class FurnitureScreenTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static ItemKit Make(FurnitureItem it, FurnitureSet? set = null) => FurnitureKit.Make(it, set ?? Sheet);

    [Fact]
    public void A_screen_of_three_panels_is_three_leaves_on_two_hinges_standing_in_a_zig_zag()
    {
        var k = Make(Defaults.For(ItemKind.Screen));
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Foot");
        Assert.Equal(3, k.Parts.Where(p => p.Name.Contains("leaf", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Copies));
        // Each hinge: three round-holed knuckles, one square-holed one at the top, and a pin.
        Assert.Equal(6, k.Parts.Single(p => p.Name == "Knuckle").Copies);
        Assert.Equal(2, k.Parts.Single(p => p.Name == "Top knuckle").Copies);
        Assert.Equal(2, k.Parts.Single(p => p.Name == "Hinge pin").Copies);
        // The leaves turn alternately back and forward: a zig-zag, not a flat wall.
        var leaves = k.Parts.Where(p => p.Name.Contains("leaf", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Poses).OrderBy(p => p.O.X).ToList();
        Assert.True(leaves[0].U.Z < -0.2 && leaves[1].U.Z > 0.2 && leaves[2].U.Z < -0.2, "not a zig-zag");
        Assert.All(leaves, p => Assert.Equal(0, p.U.Y, 6));
        Assert.All(leaves, p => Assert.True(p.N.Z > 0, "a leaf's etched face turned away"));
        // About as wide and deep as asked, standing.
        var pts = k.Parts.SelectMany(p => p.Poses.SelectMany(pose => p.Cut.SelectMany(path => path.SelectMany(q => new[] { pose.At(q.x, q.y, -1), pose.At(q.x, q.y, 1) })))).ToList();
        Assert.InRange(pts.Max(q => q.X) - pts.Min(q => q.X), 29, 32.5);
        Assert.InRange(pts.Max(q => q.Z) - pts.Min(q => q.Z), 11, 13.5);
        Assert.Equal(0, pts.Min(q => q.Y), 3);
        Assert.Equal(30, pts.Max(q => q.Y), 1);
    }

    [Fact]
    public void Each_hinge_turns_its_two_leaves_round_one_pin()
    {
        var k = Make(Defaults.For(ItemKind.Screen) with { Columns = 4 });
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var knuckles = k.Parts.Where(p => p.Name.Contains("Knuckle", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Poses).ToList();
        var pins = k.Parts.Single(p => p.Name == "Hinge pin").Poses;
        Assert.Equal(3, pins.Count);
        foreach (var pin in pins)
        {
            // Four knuckles on the pin's axis, lying flat, one above another and not touching.
            var on = knuckles.Where(q => Math.Abs(q.O.X - pin.O.X) < 1e-6 && Math.Abs(q.O.Z - pin.O.Z) < 1e-6).OrderBy(q => q.O.Y).ToList();
            Assert.Equal(4, on.Count);
            Assert.All(on, q => Assert.Equal(1, q.N.Y, 6));
            for (int i = 1; i < 4; i++) Assert.True(on[i].O.Y - on[i - 1].O.Y > 2 + 0.5);
            // They take turns: first one leaf's, then the other's, the other's, and the first's again at the top.
            Assert.True(on[0].U.Dot(on[3].U) > 0.999 && on[1].U.Dot(on[2].U) > 0.999 && on[0].U.Dot(on[1].U) < 0.9);
        }
        // The round holes are big enough for the pin to turn in; the top one holds it square.
        var hinge = new Profiles.ScreenHinge(2, 0.1);
        Assert.True(hinge.HoleR > hinge.PinReach + 0.05);
        Assert.True(hinge.Gap > hinge.PinReach + 0.2);
    }

    public static IEnumerable<object[]> Screens()
    {
        foreach (var style in Enum.GetValues<ScreenStyle>())
            foreach (var n in new[] { 2, 3, 5 })
                yield return [Defaults.For(ItemKind.Screen) with { ScreenStyle = style, Columns = n, WidthMm = 10 * n + 2 }, 2.0];
        // Folded as little and as much as it goes, on thin and thick sheet, with a tight and a loose fit.
        yield return [Defaults.For(ItemKind.Screen) with { DepthMm = 2 }, 2.0];
        yield return [Defaults.For(ItemKind.Screen) with { DepthMm = 40 }, 2.0];
        yield return [Defaults.For(ItemKind.Screen) with { Columns = 6, WidthMm = 60, HeightMm = 40, DepthMm = 14 }, 1.5];
        yield return [Defaults.For(ItemKind.Screen) with { WidthMm = 45, HeightMm = 45, DepthMm = 12 }, 3.0];
        yield return [Defaults.For(ItemKind.Screen) with { ScreenStyle = ScreenStyle.Panels, Columns = 2, WidthMm = 20, HeightMm = 24 }, 1.0];
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Hinged_screens_fit_together_without_clashing(FurnitureItem it, double t)
    {
        foreach (var set in new[] { Sheet with { ThicknessMm = t }, Sheet with { ThicknessMm = t, PushFit = true }, Sheet with { ThicknessMm = t, FitMm = 0.25 } })
        {
            var k = Make(it, set);
            Assert.True(k.Ok, string.Join("\n", k.Findings));
            Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
            Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
            if (set.PushFit) Assert.True(k.Parts.Sum(p => Geo.Area(p.Ribs)) > 0.01, "no grip ribs");
            // Without the grip ribs (they squash as it goes together), nothing is in another's way.
            var all = FurnitureTests.Assemble(k with { Parts = [.. k.Parts.Select(p => p with { Cut = Geo.Subtract(p.Cut, Geo.Offset(p.Ribs, 0.005)) })] }, t);
            Assert.Empty(FurnitureTests.Clashes(all));
            Assert.Empty(FurnitureCatalogTests.EmptySlots(all, t, set.FitMm));
            Assert.True(FurnitureTests.AllJoined(all, t), "a piece isn't joined to the others");
        }
    }

    [Fact]
    public void A_one_panel_screen_still_stands_on_two_feet()
    {
        var k = Make(Defaults.For(ItemKind.Screen) with { Columns = 1, ScreenStyle = ScreenStyle.Barrier, WidthMm = 24, DepthMm = 7, HeightMm = 14 });
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.Equal(2, k.Parts.Single(p => p.Name == "Foot").Copies);
        Assert.Single(k.Parts.Single(p => p.Name == "Screen").Poses);
    }

    [Fact]
    public void Too_narrow_or_too_low_a_hinged_screen_is_refused_with_a_reason()
    {
        var narrow = Make(Defaults.For(ItemKind.Screen) with { Columns = 6, WidthMm = 20 });
        Assert.Contains(narrow.Findings, f => f.Level == Severity.Bad && f.Text.Contains("wide"));
        var low = Make(Defaults.For(ItemKind.Screen) with { HeightMm = 11 }, Sheet with { ThicknessMm = 3 });
        Assert.Contains(low.Findings, f => f.Level == Severity.Bad && f.Text.Contains("high"));
    }
}
