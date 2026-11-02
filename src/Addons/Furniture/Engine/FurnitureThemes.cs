using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for furniture: each
/// sets the detail on the faces (planks, rough grain, metal panels with rivets, stone) and the doors'
/// shape, and has a starter set of furniture that suits it (bunks and lockers aboard a ship, a rack of
/// crates and a chest in a dungeon).
/// </summary>
public static class FurnitureThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    static string Name(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme));

    public static WoodDetail Detail(string? theme) => Name(theme) switch
    {
        TerrainThemes.SciFi => WoodDetail.Metal,
        TerrainThemes.Cave or TerrainThemes.Desert => WoodDetail.Grain,
        _ => WoodDetail.Planks,
    };

    public static DoorShape Door(string? theme) => Name(theme) switch
    {
        TerrainThemes.SciFi or TerrainThemes.Desert => DoorShape.Rectangle,
        TerrainThemes.Gothic => DoorShape.Pointed,
        _ => DoorShape.Arched,
    };

    /// <summary>The set's detail and every door's shape changed to the theme's; the list itself is left alone.</summary>
    public static FurnitureSet Apply(FurnitureSet s, string? theme)
    {
        string name = Name(theme);
        var door = Door(name);
        return s with
        {
            Theme = name,
            Detail = Detail(name),
            Items = [.. s.Items.Select(i => i.Kind == ItemKind.WallDoor ? i with { Shape = door } : i)],
        };
    }

    /// <summary>A starter list of furniture for a theme (sizes for 28 mm figures).</summary>
    public static List<FurnitureItem> Starter(string? theme)
    {
        FurnitureItem I(ItemKind k, int count = 1, Func<FurnitureItem, FurnitureItem>? edit = null)
        {
            var it = Defaults.For(k) with { Count = count };
            return edit is null ? it : edit(it);
        }
        var door = Door(theme);
        return Name(theme) switch
        {
            TerrainThemes.Dungeon =>
            [
                I(ItemKind.Table, 1, t => t with { Legs = LegStyle.Trestle, WidthMm = 34 }), I(ItemKind.Bench, 2), I(ItemKind.Chest, 2),
                I(ItemKind.Crate, 3), I(ItemKind.Bed, 1, b => b with { Bedding = false }), I(ItemKind.WallDoor, 1, d => d with { Shape = door }),
            ],
            TerrainThemes.Cave => [I(ItemKind.Stool, 3), I(ItemKind.Table, 1, t => t with { Legs = LegStyle.Slab }), I(ItemKind.Crate, 2), I(ItemKind.Chest, 1), I(ItemKind.Bed, 1)],
            TerrainThemes.SciFi =>
            [
                I(ItemKind.Table, 1, t => t with { Legs = LegStyle.Slab, Top = TopShape.Square }), I(ItemKind.Chair, 2, c => c with { Back = BackStyle.Panel }),
                I(ItemKind.Bed, 2, b => b with { FootMm = 13 }), I(ItemKind.Bookshelf, 1, b => b with { Books = false }),
                I(ItemKind.Crate, 3), I(ItemKind.WallDoor, 1, d => d with { Shape = door }),
            ],
            TerrainThemes.Desert => [I(ItemKind.Table, 1, t => t with { Legs = LegStyle.Round, HeightMm = 9 }), I(ItemKind.Stool, 3), I(ItemKind.Chest, 1), I(ItemKind.Crate, 2), I(ItemKind.WallShelf, 2)],
            TerrainThemes.Gothic =>
            [
                I(ItemKind.Table, 1, t => t with { WidthMm = 40, Legs = LegStyle.Slab }), I(ItemKind.Chair, 4, c => c with { Back = BackStyle.Ladder, HeightMm = 22 }),
                I(ItemKind.Bookshelf, 2), I(ItemKind.Chest, 1), I(ItemKind.WallDoor, 1, d => d with { Shape = door }),
            ],
            _ => [I(ItemKind.Table), I(ItemKind.Chair, 2), I(ItemKind.Bed), I(ItemKind.Chest), I(ItemKind.Bookshelf)],
        };
    }
}
