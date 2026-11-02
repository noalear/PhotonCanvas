using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for scatter terrain:
/// each sets the wood detail and turns the pieces already on the list to suit (barrels become drums
/// aboard a ship, fences become iron railings in a graveyard), and has a starter list of its own pieces
/// (braziers in a dungeon, stalagmites and mushrooms in a cave, consoles in a sci-fi base, urns and
/// palms in the desert). Crates, chests and tables come from the furniture maker in the same theme.
/// </summary>
public static class ScatterThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    static string Name(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme));

    public static WoodDetail Detail(string? theme) => Name(theme) switch
    {
        TerrainThemes.SciFi => WoodDetail.None,
        TerrainThemes.Cave or TerrainThemes.Desert or TerrainThemes.Gothic => WoodDetail.Grain,
        _ => WoodDetail.Planks,
    };

    /// <summary>The fence that suits a theme.</summary>
    public static FenceStyle Fence(string? theme) => Name(theme) switch
    {
        TerrainThemes.Dungeon => FenceStyle.Planks,
        TerrainThemes.Cave => FenceStyle.PostAndRail,
        TerrainThemes.SciFi => FenceStyle.Barrier,
        TerrainThemes.Desert => FenceStyle.Wattle,
        TerrainThemes.Gothic => FenceStyle.Railings,
        _ => FenceStyle.Picket,
    };

    /// <summary>A well's stonework for a theme.</summary>
    public static string Stone(string? theme) => Name(theme) switch
    {
        TerrainThemes.Dungeon or TerrainThemes.Gothic => "Stone blocks",
        TerrainThemes.Desert => "Brick",
        _ => "Fieldstone",
    };

    /// <summary>The set's detail changed to the theme's, and the pieces on the list turned to suit it; nothing is added or taken away.</summary>
    public static ScatterSet Apply(ScatterSet s, string? theme)
    {
        string name = Name(theme);
        bool scifi = name == TerrainThemes.SciFi, old = name is TerrainThemes.Gothic or TerrainThemes.Dungeon or TerrainThemes.Cave;
        return s with
        {
            Theme = name,
            Detail = Detail(name),
            Items = [.. s.Items.Select(i => i.Kind switch
            {
                ItemKind.Barrel => i with { Metal = scifi },
                ItemKind.Fence => i with { Style = Fence(name) },
                ItemKind.Well => i with { Texture = Stone(name) },
                ItemKind.Gravestone => i with { Weathered = old },
                _ => i,
            })],
        };
    }

    /// <summary>A starter list of scenery for a theme (sizes for 28 mm figures).</summary>
    public static List<ScatterItem> Starter(string? theme)
    {
        string name = Name(theme);
        ScatterItem I(ItemKind k, int count = 1, Func<ScatterItem, ScatterItem>? edit = null)
        {
            var it = Defaults.For(k) with { Count = count };
            if (k == ItemKind.Fence) it = it with { Style = Fence(name) };
            if (k == ItemKind.Well) it = it with { Texture = Stone(name) };
            return edit is null ? it : edit(it);
        }
        return name switch
        {
            TerrainThemes.Dungeon => [I(ItemKind.Brazier, 2), I(ItemKind.Barrel, 4), I(ItemKind.Boulder, 2, b => b with { WidthMm = 16, DepthMm = 12, HeightMm = 8 }), I(ItemKind.Fence, 2)],
            TerrainThemes.Cave => [I(ItemKind.Stalagmite, 3), I(ItemKind.Mushroom, 2), I(ItemKind.Boulder, 3), I(ItemKind.Stalagmite, 2, s => s with { WidthMm = 8, HeightMm = 18, Cluster = 1 })],
            TerrainThemes.SciFi => [I(ItemKind.Console, 2), I(ItemKind.Barrel, 4, b => b with { Metal = true, WidthMm = 11, HeightMm = 14 }), I(ItemKind.Fence, 3)],
            TerrainThemes.Desert => [I(ItemKind.Urn, 4), I(ItemKind.Urn, 2, u => u with { WidthMm = 8, HeightMm = 12, Open = true }), I(ItemKind.Palm, 2), I(ItemKind.Boulder, 2), I(ItemKind.MarketStall)],
            TerrainThemes.Gothic =>
            [
                I(ItemKind.Gravestone, 2, g => g with { Shape = StoneShape.Pointed, Weathered = true }), I(ItemKind.Gravestone, 1, g => g with { Shape = StoneShape.Celtic, HeightMm = 16, SlabMm = 18, Weathered = true }),
                I(ItemKind.Gravestone, 2, g => g with { Shape = StoneShape.Cross, Weathered = true }), I(ItemKind.Fence, 3), I(ItemKind.Brazier, 2),
            ],
            _ => [I(ItemKind.Barrel, 3), I(ItemKind.Fence, 2), I(ItemKind.Cart), I(ItemKind.MarketStall), I(ItemKind.Well)],
        };
    }
}
