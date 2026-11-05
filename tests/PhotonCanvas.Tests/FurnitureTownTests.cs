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
        }
    }

    [Theory]
    [MemberData(nameof(Props))]
    public void Town_props_cut_whole_and_fit_in_every_theme(string theme, FurnitureItem item, double t)
    {
        var it = item with { Theme = theme };
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet(theme, t)));
    }
}
