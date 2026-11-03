using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Walls;

/// <summary>A look to start from: its name and what it brings.</summary>
public sealed record ThemeInfo(string Name, string About);

/// <summary>
/// The themes (Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) as the wall maker draws them: each
/// sets the texture and its sizes, the shape and look of doors and windows, the bars, the frame round
/// them, the accessories and the columns' shape. Sizes, the top, how many of anything and the fit are
/// left as they are. The floor tile, roof, stair and building makers use the same names.
/// </summary>
public static class WallThemes
{
    public static IReadOnlyList<ThemeInfo> All { get; } = [.. TerrainThemes.All.Select(t => new ThemeInfo(t.Name, t.About))];

    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    /// <summary>The wall's texture for a theme: its name, sizes (for 28 mm figures) and groove.</summary>
    public static (string Texture, double A, double B, double Groove) Texture(string? theme) => theme switch
    {
        TerrainThemes.Dungeon => (ThemeTextures.DungeonBlocks, 14, 8, 0.9),
        TerrainThemes.Cave => (ThemeTextures.CaveRock, 16, 10, 1.0),
        TerrainThemes.SciFi => (ThemeTextures.SciFiPanels, 16, 12, 0.5),
        TerrainThemes.Desert => (ThemeTextures.Sandstone, 16, 7, 0.7),
        TerrainThemes.Gothic => ("Stone blocks", 10, 5, 0.6),
        _ => ("Brick", 8, 3.5, 0.6),
    };

    /// <summary>The door's shape and look for a theme.</summary>
    public static (OpeningShape Shape, DoorLook Look) Door(string? theme) => theme switch
    {
        TerrainThemes.Dungeon => (OpeningShape.Arched, DoorLook.Studded),
        TerrainThemes.Cave => (OpeningShape.Rough, DoorLook.Planks),
        TerrainThemes.SciFi => (OpeningShape.Chamfered, DoorLook.Metal),
        TerrainThemes.Desert => (OpeningShape.Rectangle, DoorLook.Planks),
        TerrainThemes.Gothic => (OpeningShape.Pointed, DoorLook.Studded),
        _ => (OpeningShape.Arched, DoorLook.Planks),
    };

    /// <summary>The windows' shape, width and height (mm, for 28 mm figures) and bars for a theme.</summary>
    public static (OpeningShape Shape, double W, double H, WindowBars Bars) Window(string? theme) => theme switch
    {
        TerrainThemes.Dungeon => (OpeningShape.Rectangle, 8, 7, WindowBars.Bars),
        TerrainThemes.Cave => (OpeningShape.Rough, 10, 9, WindowBars.None),
        TerrainThemes.SciFi => (OpeningShape.Chamfered, 12, 6, WindowBars.None),
        TerrainThemes.Desert => (OpeningShape.Rectangle, 6, 8, WindowBars.None),
        TerrainThemes.Gothic => (OpeningShape.Pointed, 7, 14, WindowBars.Cross),
        _ => (OpeningShape.Arched, 8, 12, WindowBars.None),
    };

    public static WallSpec Apply(WallSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        var (texture, a, b, groove) = Texture(name);
        var (doorShape, look) = Door(name);
        var (winShape, ww, wh, bars) = Window(name);
        double k = Math.Clamp(s.HeightMm / 50, 0.5, 3);
        return s with
        {
            Theme = name,
            Texture = texture, Custom = null, SizeA = a, SizeB = b, GrooveMm = groove, DetailMm = 0,
            DoorShape = doorShape, DoorLook = look,
            WindowShape = winShape, WindowWidthMm = Math.Round(ww * k, 1), WindowHeightMm = Math.Round(wh * k, 1), Bars = bars,
            FrameMm = name switch { TerrainThemes.Cave => 0, TerrainThemes.Desert => 2, TerrainThemes.SciFi => 1, _ => 1.5 },
            Accessories = s.Accessories == Accessory.None ? Accessory.None : name switch
            {
                TerrainThemes.SciFi => Accessory.Lamps,
                TerrainThemes.Dungeon or TerrainThemes.Cave or TerrainThemes.Gothic => Accessory.Sconces,
                TerrainThemes.Desert => Accessory.Brackets,
                _ => s.Accessories == Accessory.Lamps ? Accessory.Pegs : s.Accessories,
            },
            MountPoints = [.. s.MountPoints.Select(m => m.Item == MountItem.None ? m : m with
            {
                Item = name switch
                {
                    TerrainThemes.SciFi => MountItem.Lamp,
                    TerrainThemes.Dungeon or TerrainThemes.Cave or TerrainThemes.Gothic => MountItem.Sconce,
                    TerrainThemes.Desert => MountItem.Shelf,
                    _ => m.Item == MountItem.Lamp ? MountItem.Peg : m.Item,
                },
            })],
            Column = name switch { TerrainThemes.Dungeon or TerrainThemes.Desert => ColumnShape.Square, TerrainThemes.Gothic => ColumnShape.Octagon, TerrainThemes.Cave => ColumnShape.Round, _ => ColumnShape.Auto },
        };
    }
}
