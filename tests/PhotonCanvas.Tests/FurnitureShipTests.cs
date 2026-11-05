using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>The sci-fi ship fittings and the armoury's weapon rack and suit of armour.</summary>
public class FurnitureShipTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static FurnitureSet In(string theme, double t = 2, bool push = false) => FurnitureThemes.Apply(Sheet with { ThicknessMm = t, PushFit = push }, theme);

    public static IEnumerable<object[]> Styles() => Enum.GetValues<ShipStyle>().SelectMany(s => new[] { new object[] { s, false }, [s, true] });

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_ship_fitting_fits_together_at_its_own_size(ShipStyle style, bool push)
    {
        var it = TownWalls.ShipSized(Defaults.For(ItemKind.Ship), style);
        var r = FurnitureCatalogTests.Check(it, In("Sci-fi", push: push));
        // A push fit's grip ribs stand proud into the slots on purpose.
        if (push) r = string.Join("\n", r.Split('\n').Where(l => !l.StartsWith("clash")));
        Assert.Equal("", r);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_ship_fitting_is_made_for_bigger_figures_on_3_mm_sheet(ShipStyle style, bool push)
    {
        var it = FurnitureCatalog.Scale(TownWalls.ShipSized(Defaults.For(ItemKind.Ship), style), 54 / 28.0);
        var k = FurnitureKit.Make(it, In("Sci-fi", 3, push));
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
    }

    [Theory]
    [InlineData(ShipStyle.CryoPod, "cryo pod is too")]
    [InlineData(ShipStyle.MedBed, "med bay bed is too")]
    [InlineData(ShipStyle.MessTable, "mess table is too")]
    [InlineData(ShipStyle.ServerRack, "server rack is too")]
    [InlineData(ShipStyle.Bunks, "bunks are too")]
    [InlineData(ShipStyle.Console, "console is too")]
    [InlineData(ShipStyle.RifleRack, "rifle rack is too")]
    [InlineData(ShipStyle.FuelDrums, "fuel drums are too")]
    [InlineData(ShipStyle.Reactor, "reactor is too")]
    public void A_ship_fitting_too_small_says_so(ShipStyle style, string says)
    {
        var it = TownWalls.ShipSized(Defaults.For(ItemKind.Ship), style) with { WidthMm = 8, DepthMm = 6, HeightMm = 8 };
        var k = FurnitureKit.Make(it, In("Sci-fi"));
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains(says));
    }

    [Fact]
    public void Picking_a_kind_starts_it_at_its_own_size()
    {
        var bunks = TownWalls.ShipSized(Defaults.For(ItemKind.Ship), ShipStyle.Bunks);
        var drums = TownWalls.ShipSized(bunks, ShipStyle.FuelDrums);
        Assert.Equal(ShipStyle.FuelDrums, drums.Ship);
        Assert.NotEqual(bunks.HeightMm, drums.HeightMm);
    }

    [Fact]
    public void The_sci_fi_catalog_uses_the_ship_fittings()
    {
        foreach (var (id, style) in new[] { ("sci-sleeper-pod", ShipStyle.CryoPod), ("sci-medical-bed", ShipStyle.MedBed), ("sci-mess-table", ShipStyle.MessTable), ("sci-server-rack", ShipStyle.ServerRack),
                                            ("sci-crew-bunks", ShipStyle.Bunks), ("sci-helm-console", ShipStyle.Console), ("sci-rifle-rack", ShipStyle.RifleRack), ("sci-fuel-drums", ShipStyle.FuelDrums), ("sci-reactor-core", ShipStyle.Reactor) })
        {
            var e = FurnitureCatalog.Find(id);
            Assert.NotNull(e);
            Assert.Equal(ItemKind.Ship, e!.Item.Kind);
            Assert.Equal(style, e.Item.Ship);
        }
    }

    [Fact]
    public void Glued_drums_are_stacked_rings_and_push_fit_ones_are_halved_profiles()
    {
        var it = TownWalls.ShipSized(Defaults.For(ItemKind.Ship), ShipStyle.FuelDrums);
        var glued = FurnitureKit.Make(it, In("Sci-fi"));
        var push = FurnitureKit.Make(it, In("Sci-fi", push: true));
        Assert.Equal(3, glued.Parts.Count(p => p.Name == "Drum lid"));
        Assert.Equal(6, glued.Parts.Count(p => p.Name == "Drum rib"));
        Assert.DoesNotContain(glued.Parts, p => p.Name.StartsWith("Drum (crossing)"));
        Assert.Equal(3, push.Parts.Count(p => p.Name == "Drum (crossing)"));
    }

    // ---- the armoury ----

    public static IEnumerable<object[]> Themes() => new[] { "Medieval", "Dungeon", "Cave", "Desert", "Gothic" }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_weapon_rack_fits_together_in_every_wooden_theme(string theme)
    {
        var it = Defaults.For(ItemKind.WeaponRack);
        Assert.Equal("", FurnitureCatalogTests.Check(it, In(theme)));
        var k = FurnitureKit.Make(it, In(theme));
        Assert.Equal(5, k.Parts.Count(p => p.Name is "Spear" or "Halberd" or "Battle axe"));
        Assert.True(k.Parts.Count(p => p.Name == "Sword") >= 3);
    }

    [Fact]
    public void A_shallow_weapon_rack_leaves_the_swords_out_and_a_narrow_one_holds_fewer_polearms()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.WeaponRack) with { WidthMm = 18, DepthMm = 8 }, In("Medieval"));
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Sword" || p.Name == "Sword rest");
        Assert.InRange(k.Parts.Count(p => p.Name is "Spear" or "Halberd" or "Battle axe"), 1, 2);
    }

    [Fact]
    public void A_sci_fi_weapon_rack_still_holds_rifles()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.WeaponRack), In("Sci-fi"));
        Assert.True(k.Ok);
        Assert.Contains(k.Parts, p => p.Name == "Rifle");
    }

    public static IEnumerable<object[]> ArmourThemes() =>
        new[] { "Medieval", "Dungeon", "Desert", "Gothic", "Sci-fi" }.SelectMany(t => new[] { new object[] { t, false }, [t, true] });

    [Theory]
    [MemberData(nameof(ArmourThemes))]
    public void The_suit_of_armour_stands_round_in_every_theme(string theme, bool push)
    {
        var it = Defaults.For(ItemKind.Stand) with { Stand = StandStyle.Armour };
        var r = FurnitureCatalogTests.Check(it, In(theme, push: push));
        if (push) r = string.Join("\n", r.Split('\n').Where(l => !l.StartsWith("clash")));
        Assert.Equal("", r);
        var k = FurnitureKit.Make(it, In(theme, push: push));
        Assert.Contains(k.Parts, p => p.Name == "Armour");
        Assert.Contains(k.Parts, p => p.Name == "Armour (side)");
        Assert.Equal(!push, k.Parts.Any(p => p.Name == "Visor"));
    }

    [Fact]
    public void A_tiny_suit_of_armour_says_so()
    {
        var k = FurnitureKit.Make(Defaults.For(ItemKind.Stand) with { Stand = StandStyle.Armour, WidthMm = 6, DepthMm = 4, HeightMm = 14 }, In("Medieval"));
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Text.Contains("suit of armour is too small"));
    }
}
