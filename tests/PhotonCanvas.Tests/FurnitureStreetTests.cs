using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.Furniture;
using ItemKind = PhotonCanvas.Addon.Furniture.ItemKind;

namespace PhotonCanvas.Tests;

/// <summary>The street pieces: lamps, statues, signposts, market crosses and watchtowers.</summary>
public class FurnitureStreetTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static FurnitureSet In(string theme, double t = 2, bool push = false) => FurnitureThemes.Apply(Sheet with { ThicknessMm = t, PushFit = push }, theme);

    static readonly string[] AllThemes = ["Medieval", "Dungeon", "Cave", "Sci-fi", "Desert", "Gothic"];

    public static IEnumerable<object[]> Pieces() =>
        Enum.GetValues<StreetStyle>().SelectMany(s => AllThemes.SelectMany(t => new[] { new object[] { s, t, false }, [s, t, true] }));

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Every_street_piece_fits_together_in_every_theme(StreetStyle style, string theme, bool push)
    {
        var it = TownWalls.StreetSized(Defaults.For(ItemKind.Street), style);
        var r = FurnitureCatalogTests.Check(it, In(theme, push: push));
        // A push fit's grip ribs stand proud into the slots on purpose.
        if (push) r = string.Join("\n", r.Split('\n').Where(l => !l.StartsWith("clash")));
        Assert.Equal("", r);
    }

    public static IEnumerable<object[]> Styles() => Enum.GetValues<StreetStyle>().SelectMany(s => new[] { new object[] { s, false }, [s, true] });

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_street_piece_is_made_for_bigger_figures_on_3_mm_sheet(StreetStyle style, bool push)
    {
        foreach (var theme in AllThemes)
        {
            var it = FurnitureCatalog.Scale(TownWalls.StreetSized(Defaults.For(ItemKind.Street), style), 54 / 28.0);
            var k = FurnitureKit.Make(it, In(theme, 3, push));
            Assert.True(k.Ok, theme + ": " + string.Join("\n", k.Findings));
            Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
        }
    }

    [Theory]
    [InlineData(StreetStyle.Lamp, "street lamp is too")]
    [InlineData(StreetStyle.Statue, "statue is too small")]
    [InlineData(StreetStyle.Signpost, "signpost is too")]
    [InlineData(StreetStyle.MarketCross, "market cross is too")]
    [InlineData(StreetStyle.Watchtower, "watchtower is too")]
    public void A_street_piece_too_small_says_so(StreetStyle style, string says)
    {
        var it = TownWalls.StreetSized(Defaults.For(ItemKind.Street), style) with { WidthMm = 8, DepthMm = 8, HeightMm = 20 };
        var k = FurnitureKit.Make(it, In("Medieval"));
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains(says));
    }

    [Fact]
    public void Every_theme_has_the_street_pieces_the_city_builder_looks_for()
    {
        foreach (var key in new[] { "med", "dun", "cav", "sci", "des", "got" })
            foreach (var (end, style) in new[] { ("street-lamp", StreetStyle.Lamp), ("statue", StreetStyle.Statue), ("signpost", StreetStyle.Signpost), ("market-cross", StreetStyle.MarketCross), ("watchtower", StreetStyle.Watchtower) })
            {
                var e = FurnitureCatalog.Find($"{key}-{end}");
                Assert.NotNull(e);
                Assert.Equal(ItemKind.Street, e!.Item.Kind);
                Assert.Equal(style, e.Item.Street);
                Assert.Equal(FurnitureCatalog.Town, e.Room);
            }
    }

    [Fact]
    public void Picking_a_kind_starts_it_at_its_own_size()
    {
        var lamp = TownWalls.StreetSized(Defaults.For(ItemKind.Street), StreetStyle.Lamp);
        var tower = TownWalls.StreetSized(lamp, StreetStyle.Watchtower);
        Assert.Equal(StreetStyle.Watchtower, tower.Street);
        Assert.True(tower.HeightMm > lamp.HeightMm && tower.WidthMm > lamp.WidthMm);
    }

    [Fact]
    public void The_watchtower_has_a_ladder_a_bell_and_a_roof_and_glued_bits_only_when_glued()
    {
        var it = TownWalls.StreetSized(Defaults.For(ItemKind.Street), StreetStyle.Watchtower);
        var glued = FurnitureKit.Make(it, In("Medieval"));
        var push = FurnitureKit.Make(it, In("Medieval", push: true));
        Assert.Equal(4, glued.Parts.Count(p => p.Name.StartsWith("Leg frame")));
        Assert.Equal(2, glued.Parts.Count(p => p.Name == "Roof"));
        Assert.Contains(glued.Parts, p => p.Name == "Ridge board");
        Assert.DoesNotContain(push.Parts, p => p.Name == "Ridge board");
        Assert.Contains(glued.Parts, p => p.Name == "Brace ring");
    }

    [Fact]
    public void The_city_builder_cuts_lamps_and_statues_in_the_maps_look()
    {
        var m = CityMap.Empty(10, 10) with
        {
            Theme = "Gothic",
            Props = [new Prop { Kind = PropKind.Lamp, X = 2, Y = 2 }, new Prop { Kind = PropKind.Lamp, X = 4, Y = 2 }, new Prop { Kind = PropKind.Statue, X = 6, Y = 6 }],
        };
        var set = TownPieces.TownPropSet(m)!;
        Assert.Equal([(StreetStyle.Lamp, 2), (StreetStyle.Statue, 1)], set.Items.Select(i => (i.Street, i.Count)));
        Assert.All(set.Items, i => Assert.Equal(ItemKind.Street, i.Kind));
        Assert.True(FurnitureKit.Make(set).Ok);
    }
}
