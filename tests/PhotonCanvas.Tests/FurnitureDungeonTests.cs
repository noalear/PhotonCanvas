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

    static readonly FurnitureItem Anvil = Defaults.For(ItemKind.Stand) with { Stand = StandStyle.Anvil, WidthMm = 13, DepthMm = 8, HeightMm = 11 };
    static readonly FurnitureItem WallTorch = Defaults.For(ItemKind.WallHanging) with { Hanging = HangingStyle.Torch, WidthMm = 7, DepthMm = 12, HeightMm = 16, HangMm = 14 };
    static readonly FurnitureItem Treasure = Defaults.For(ItemKind.Chest) with { WidthMm = 18, DepthMm = 11, HeightMm = 10, Lid = LidStyle.Open, Load = CrateLoad.Treasure };

    [Theory]
    [MemberData(nameof(Themes))]
    public void Anvil_wall_torch_and_treasure_chest_cut_whole_and_fit_in_every_theme(string theme)
    {
        foreach (var it in new[] { Anvil, WallTorch, Treasure, Defaults.For(ItemKind.Stand) with { Stand = StandStyle.Dummy } })
        {
            Assert.Equal("", FurnitureCatalogTests.Check(it with { Theme = theme }, Sheet(theme)));
            Assert.Equal("", FurnitureCatalogTests.Check(FurnitureCatalog.Sized(it with { Theme = theme }, 54), Sheet(theme, 3)));
        }
    }

    [Fact]
    public void Wall_torch_is_a_post_an_arm_and_a_torch_up_on_the_wall()
    {
        var k = FurnitureKit.Make(WallTorch with { Theme = "Dungeon" }, Sheet("Dungeon"));
        Assert.True(k.Ok);
        Assert.Equal(["Torch post", "Torch arm", "Torch"], k.Parts.Select(p => p.Name));
        Assert.Equal(FurnitureSpot.OnWall, FurnitureCatalog.SpotFor(WallTorch));
        // The post has the slot for the arm's tab; everything sits from the hang height up.
        Assert.True(k.Parts[0].Cut.Count > 1);
        Assert.All(k.Parts, p => Assert.All(p.Poses, pose => Assert.True(pose.O.Y >= 14 - 1e-6)));
    }

    [Fact]
    public void Treasure_chest_is_thrown_open_and_heaped_with_treasure_but_push_fit_leaves_the_lid_off()
    {
        var k = FurnitureKit.Make(Treasure with { Theme = "Dungeon" }, Sheet("Dungeon"));
        Assert.Contains(k.Parts, p => p.Name == "Lid (thrown open)" && p.Glued);
        Assert.Contains(k.Parts, p => p.Name == "Treasure");
        Assert.DoesNotContain(k.Parts, p => p.Name == "Lid");
        var push = FurnitureKit.Make(Treasure with { Theme = "Dungeon" }, Sheet("Dungeon", push: true));
        Assert.DoesNotContain(push.Parts, p => p.Name == "Lid (thrown open)");
        Assert.Contains(push.Parts, p => p.Name == "Treasure");
    }

    [Fact]
    public void Dungeon_pieces_get_their_own_look()
    {
        // Barred doors: tall slots, not a lattice of diamonds.
        var cup = Defaults.For(ItemKind.Cabinet) with { Front = CabinetFront.Lattice, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 26 };
        static PathsD Front(ItemKit k) => k.Parts.Single(p => p.Name == "Front").Cut;
        static int Tall(PathsD cut) => cut.Select(h => Geo.Bounds([h])).Count(b => b.bottom - b.top > 3 && b.bottom - b.top > 2 * (b.right - b.left) && b.bottom - b.top < 20);
        Assert.True(Tall(Front(FurnitureKit.Make(cup with { Theme = "Dungeon" }, Sheet("Dungeon")))) >= 8);
        Assert.Equal(0, Tall(Front(FurnitureKit.Make(cup with { Theme = "Medieval" }, Sheet("Medieval")))));
        // Two skulls on the altar.
        var altar = Defaults.For(ItemKind.Cabinet) with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 26, DepthMm = 12, HeightMm = 16 };
        Assert.Equal(2, FurnitureKit.Make(altar with { Theme = "Dungeon" }, Sheet("Dungeon")).Parts.Where(p => p.Name == "Skull").Sum(p => p.Copies));
        Assert.DoesNotContain(FurnitureKit.Make(altar with { Theme = "Medieval" }, Sheet("Medieval")).Parts, p => p.Name == "Skull");
        // The chained book has its padlock hanging under it.
        var lectern = Defaults.For(ItemKind.SlantDesk) with { Slant = SlantStyle.Lectern, WidthMm = 10 };
        double Low(string theme) => Geo.Bounds(FurnitureKit.Make(lectern with { Theme = theme }, Sheet(theme)).Parts.Single(p => p.Name == "Book").Cut).top;
        Assert.True(Low("Dungeon") < Low("Medieval") - 1);
        Assert.Equal("", FurnitureCatalogTests.Check(cup with { Theme = "Dungeon" }, Sheet("Dungeon")));
        Assert.Equal("", FurnitureCatalogTests.Check(altar with { Theme = "Dungeon" }, Sheet("Dungeon")));
        Assert.Equal("", FurnitureCatalogTests.Check(lectern with { Theme = "Dungeon" }, Sheet("Dungeon")));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Stump_stool_spiked_throne_chained_stocks_and_weapons_cupboard_fit_in_every_theme(string theme)
    {
        var items = new[]
        {
            Defaults.For(ItemKind.Stool) with { Legs = LegStyle.Slab, WidthMm = 9, DepthMm = 9 },
            Defaults.For(ItemKind.Armchair) with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 30, SeatMm = 10, ArmMm = 14 },
            Defaults.For(ItemKind.Stocks),
            Defaults.For(ItemKind.Cabinet) with { Front = CabinetFront.Lattice, Contents = ShelfContents.Weapons, Shelves = 1, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 30 },
        };
        foreach (var it in items)
        {
            Assert.Equal("", FurnitureCatalogTests.Check(it with { Theme = theme }, Sheet(theme)));
            Assert.Equal("", FurnitureCatalogTests.Check(FurnitureCatalog.Sized(it with { Theme = theme }, 54), Sheet(theme, 3)));
        }
    }

    [Fact]
    public void Dungeon_stocks_hang_a_chain_and_the_throne_wears_spikes()
    {
        var stocks = FurnitureKit.Make(Defaults.For(ItemKind.Stocks) with { Theme = "Dungeon" }, Sheet("Dungeon"));
        Assert.Contains(stocks.Parts, p => p.Name == "Chain" && p.Glued);
        Assert.DoesNotContain(FurnitureKit.Make(Defaults.For(ItemKind.Stocks) with { Theme = "Dungeon" }, Sheet("Dungeon", push: true)).Parts, p => p.Name == "Chain");
        Assert.DoesNotContain(FurnitureKit.Make(Defaults.For(ItemKind.Stocks) with { Theme = "Medieval" }, Sheet("Medieval")).Parts, p => p.Name == "Chain");
        var throne = Defaults.For(ItemKind.Armchair) with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 30, SeatMm = 10, ArmMm = 14 };
        double Top(string theme) => Geo.Bounds(FurnitureKit.Make(throne with { Theme = theme }, Sheet(theme)).Parts.Single(p => p.Name == "Back").Cut).bottom;
        Assert.True(Top("Dungeon") > Top("Medieval") + 2);
        var stool = FurnitureKit.Make(Defaults.For(ItemKind.Stool) with { Legs = LegStyle.Slab, WidthMm = 9, DepthMm = 9 }, Sheet("Cave"));
        Assert.NotEmpty(stool.Parts.Single(p => p.Name == "Seat").Lines);
    }
}
