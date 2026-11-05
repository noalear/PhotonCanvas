using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>The dungeon pieces from the reference sheet: cook fire on a spit, torch on splayed legs, war banner, shaggy hide.</summary>
public class FurnitureDungeonTests
{
    static FurnitureSet Sheet(string theme, double t = 2, bool push = false) =>
        FurnitureThemes.Apply(new FurnitureSet { Items = [], ThicknessMm = t, FitMm = 0.1, PushFit = push, GripMm = push ? 0.15 : 0 }, theme);

    static readonly FurnitureItem Fire = Defaults.For(ItemKind.Fireplace) with { Hearth = HearthStyle.Spit, WidthMm = 26, DepthMm = 14, HeightMm = 18 };
    static readonly FurnitureItem Torch = Defaults.For(ItemKind.Candlestand) with { Candle = CandleStyle.Torch, WidthMm = 7, DepthMm = 7, HeightMm = 22 };

    public static IEnumerable<object[]> Themes() => FurnitureThemes.Names.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Themes))]
    public void Cook_fire_and_torch_cut_whole_and_fit_in_every_theme(string theme)
    {
        Assert.Equal("", FurnitureCatalogTests.Check(Fire with { Theme = theme }, Sheet(theme)));
        Assert.Equal("", FurnitureCatalogTests.Check(Torch with { Theme = theme }, Sheet(theme)));
        Assert.Equal("", FurnitureCatalogTests.Check(Torch with { Theme = theme, WidthMm = 12, DepthMm = 12, HeightMm = 40 }, Sheet(theme, 3)));
    }

    [Fact]
    public void Cook_fire_has_its_spit_frames_roast_and_cauldron_and_drops_the_glued_ones_under_push_fit()
    {
        var k = FurnitureKit.Make(Fire with { Theme = "Dungeon" }, Sheet("Dungeon"));
        Assert.True(k.Ok, string.Join("; ", k.Findings.Select(f => f.Text)));
        foreach (var name in new[] { "Hearth stone", "Ring of stones", "Fire", "Spit frame", "Spit", "Roast", "Cauldron" })
            Assert.Contains(k.Parts, p => p.Name == name);
        Assert.Equal(2, k.Parts.Single(p => p.Name == "Spit frame").Copies);
        // The fire stands free: the catalog puts it in the middle of the room, not on a wall.
        Assert.Equal(FurnitureSpot.Free, FurnitureCatalog.All.Single(e => e.Id == "dun-cook-fire").Spot);

        var push = FurnitureKit.Make(Fire with { Theme = "Dungeon" }, Sheet("Dungeon", push: true));
        Assert.DoesNotContain(push.Parts, p => p.Name is "Roast" or "Cauldron" or "Ring of stones");
        Assert.Contains(push.Parts, p => p.Name == "Spit");
    }

    [Fact]
    public void Cook_fire_too_small_says_so()
    {
        var k = FurnitureKit.Make(Fire with { WidthMm = 10 }, Sheet("Medieval"));
        Assert.False(k.Ok);
    }

    [Fact]
    public void Torch_is_a_stand_and_a_crossing_pair_of_legs_each_in_one_piece()
    {
        var k = FurnitureKit.Make(Torch with { Theme = "Dungeon" }, Sheet("Dungeon"));
        Assert.True(k.Ok);
        Assert.Contains(k.Parts, p => p.Name == "Torch stand");
        Assert.Contains(k.Parts, p => p.Name == "Legs");
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        Assert.NotEmpty(k.Parts.Single(p => p.Name == "Torch stand").Etched);
    }

    [Fact]
    public void Dungeon_banner_is_a_tattered_war_banner_with_tears_and_a_skull()
    {
        var it = Defaults.For(ItemKind.WallHanging) with { Hanging = HangingStyle.Banner, WidthMm = 12, HeightMm = 26, Theme = "Dungeon" };
        var war = FurnitureKit.Make(it, Sheet("Dungeon")).Parts.Single();
        var plain = FurnitureKit.Make(it with { Theme = "Medieval" }, Sheet("Medieval")).Parts.Single();
        Assert.Equal(1, Geo.Pieces(war.Cut));
        // Tears cut through it: holes inside the outline.
        Assert.True(war.Cut.Count > 1, "no tears cut through");
        Assert.NotEmpty(war.Etched);
        Assert.NotEqual(Clipper.Area(plain.Cut), Clipper.Area(war.Cut), 1);
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet("Dungeon")));
        Assert.Equal("", FurnitureCatalogTests.Check(it with { WidthMm = 20, HeightMm = 46 }, Sheet("Dungeon", 3)));
    }

    [Fact]
    public void Hide_rug_has_a_shaggy_edge_and_a_dark_saddle()
    {
        var it = Defaults.For(ItemKind.Rug) with { Rug = RugPattern.Hide, WidthMm = 30, DepthMm = 20, Theme = "Dungeon" };
        var rug = FurnitureKit.Make(it, Sheet("Dungeon")).Parts.Single();
        Assert.Equal(1, Geo.Pieces(rug.Cut));
        // The shaggy edge is many little bumps: far more points round the outline than a smooth hide would need.
        Assert.True(rug.Cut.Sum(p => p.Count) > 150, $"{rug.Cut.Sum(p => p.Count)} points");
        Assert.True(Math.Abs(Clipper.Area(rug.Etched)) > Math.Abs(Clipper.Area(rug.Cut)) * 0.15);
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet("Dungeon")));
    }
}
