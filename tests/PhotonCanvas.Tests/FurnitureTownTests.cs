using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>The town pieces: walls, towers, trees, troughs, stages and stalls in every theme.</summary>
public class FurnitureTownTests
{
    static FurnitureSet Sheet(string theme, double t = 2) =>
        FurnitureThemes.Apply(new FurnitureSet { Items = [], ThicknessMm = t, FitMm = 0.1 }, theme);

    [Fact]
    public void Every_theme_has_the_town_pieces_the_city_builder_looks_for()
    {
        string[] ends = ["wall-1", "wall-2", "wall-corner-out", "wall-corner-in", "wall-gate", "wall-end", "guard-tower",
                         "horse-trough", "stage", "market-stall", "bar", "notice-board"];
        foreach (var key in new[] { "med", "dun", "cav", "sci", "des", "got" })
        {
            foreach (var end in ends)
                Assert.Contains(FurnitureCatalog.All, e => e.Id == $"{key}-{end}" && e.Room == FurnitureCatalog.Town);
            Assert.Contains(FurnitureCatalog.All, e => e.Id.StartsWith($"{key}-tree-") && e.Item.Kind == ItemKind.Tree);
        }
    }

    public static IEnumerable<object[]> Props()
    {
        foreach (var th in FurnitureThemes.Names)
        {
            foreach (var s in Enum.GetValues<TreeStyle>())
            {
                yield return [th, Defaults.For(ItemKind.Tree) with { Tree = s }, 2.0];
                yield return [th, Defaults.For(ItemKind.Tree) with { Tree = s, WidthMm = 30, DepthMm = 30, HeightMm = 50 }, 2.0];
            }
            foreach (var s in Enum.GetValues<StallStyle>())
                yield return [th, s == StallStyle.NoticeBoard
                    ? Defaults.For(ItemKind.Stall) with { Stall = s, WidthMm = 24, DepthMm = 10, HeightMm = 36 }
                    : Defaults.For(ItemKind.Stall) with { Stall = s }, 2.0];
            yield return [th, Defaults.For(ItemKind.Trough), 2.0];
            yield return [th, Defaults.For(ItemKind.Stage), 3.0];
            yield return [th, Defaults.For(ItemKind.Gallows), 2.0];
            yield return [th, Defaults.For(ItemKind.Gallows) with { WidthMm = 69, DepthMm = 57, HeightMm = 90 }, 3.0];
            yield return [th, Defaults.For(ItemKind.Fountain), 2.0];
            yield return [th, Defaults.For(ItemKind.Fountain) with { WidthMm = 76.2, DepthMm = 76.2, HeightMm = 60 }, 3.0];
        }
    }

    [Theory]
    [MemberData(nameof(Props))]
    public void Town_props_cut_whole_and_fit_in_every_theme(string theme, FurnitureItem item, double t)
    {
        var it = item with { Theme = theme };
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet(theme, t)));
    }

    [Fact]
    public void Every_theme_has_a_fountain_and_all_but_sci_fi_a_gallows()
    {
        foreach (var key in new[] { "med", "dun", "cav", "sci", "des", "got" })
        {
            Assert.Contains(FurnitureCatalog.All, e => e.Id == $"{key}-fountain" && e.Item.Kind == ItemKind.Fountain && e.Room == FurnitureCatalog.Town);
            Assert.Equal(key != "sci", FurnitureCatalog.All.Any(e => e.Id == $"{key}-gallows" && e.Item.Kind == ItemKind.Gallows));
        }
    }

    [Theory]
    [InlineData("Medieval", false)]
    [InlineData("Dungeon", true)]
    [InlineData("Gothic", true)]
    public void A_gallows_has_its_frame_railings_and_steps_and_a_cage_in_grim_themes(string theme, bool cage)
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.Gallows) with { Theme = theme }, Sheet(theme));
        Assert.True(k.Ok);
        Assert.Single(k.Parts, p => p.Name == "Gallows frame");
        Assert.True(k.Parts.Count(p => p.Name == "Railing") >= 4);
        Assert.Contains(k.Parts, p => p.Name.StartsWith("Step "));
        var frame = k.Parts.Single(p => p.Name == "Gallows frame");
        // The noose's loop (and the cage's bars) are holes in the frame.
        int holes = Geo.Tidy(frame.Cut).Count(q => Clipper2Lib.Clipper.Area(q) < 0);
        Assert.True(cage ? holes > 4 : holes >= 1, $"{holes} holes");
    }

    [Fact]
    public void A_fountain_has_its_basin_water_and_three_stages_turned_to_each_other()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.Fountain) with { Theme = "Medieval" }, Sheet("Medieval"));
        Assert.True(k.Ok);
        Assert.Equal(8, k.Parts.Where(p => p.Name == "Basin side").Sum(p => p.Copies));
        foreach (var n in new[] { "Plinth", "Coping", "Water", "Lower bowl", "Upper bowl", "Lower column", "Upper column", "Finial", "Lower bowl lip" })
            Assert.Contains(k.Parts, p => p.Name == n);
        // A push fit leaves the glued lips off.
        var push = FurnitureKit.Make(Defaults.For(ItemKind.Fountain) with { Theme = "Medieval" }, Sheet("Medieval") with { PushFit = true });
        Assert.DoesNotContain(push.Parts, p => p.Name.EndsWith("lip"));
    }

    [Fact]
    public void A_gallows_too_shallow_for_the_sheet_says_how_deep_to_make_it()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.Gallows), Sheet("Medieval", 3));
        Assert.False(k.Ok);
        var f = Assert.Single(k.Findings, f => f.Text.Contains("mm deep"));
        double deep = double.Parse(System.Text.RegularExpressions.Regex.Match(f.Text, @"at least (\d+) mm").Groups[1].Value);
        Assert.True(FurnitureKit.Make(Defaults.For(ItemKind.Gallows) with { DepthMm = deep }, Sheet("Medieval", 3)).Ok);
    }

    [Fact]
    public void A_fountain_too_small_for_its_bowls_says_how_big_to_make_it()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.Fountain) with { WidthMm = 30, DepthMm = 30 }, Sheet("Medieval"));
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Text.Contains("mm across"));
    }
}
