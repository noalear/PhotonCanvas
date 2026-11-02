using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for the game kit:
/// each sets the finish on every face, whether dice towers have battlements and the shape of the
/// initiative tiles' tops, so the kit matches the table's scenery. Sizes are left as they are.
/// </summary>
public static class GameKitThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    public static Finish FinishFor(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme)) switch
    {
        TerrainThemes.Dungeon => Finish.DungeonBlocks,
        TerrainThemes.Cave => Finish.CaveRock,
        TerrainThemes.SciFi => Finish.SciFiPanels,
        TerrainThemes.Desert => Finish.Sandstone,
        TerrainThemes.Gothic => Finish.Brick,
        _ => Finish.StoneBlocks,
    };

    public static AccessorySet Apply(AccessorySet s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        bool battlements = name is not (TerrainThemes.Cave or TerrainThemes.SciFi);
        var top = name switch { TerrainThemes.SciFi => TileTop.Square, TerrainThemes.Gothic or TerrainThemes.Desert => TileTop.Arched, _ => TileTop.Rounded };
        return s with
        {
            Theme = name,
            Finish = FinishFor(name),
            // The themes' textures are drawn at the wall maker's sizes for 28 mm figures: a little bigger suits a tower.
            TextureScale = name == TerrainThemes.Medieval ? s.TextureScale : Math.Max(1, Math.Min(s.TextureScale, 1.5)),
            Items = [.. s.Items.Select(i => i with { Battlements = i.Kind == AccessoryKind.DiceTower ? battlements : i.Battlements, Top = top })],
        };
    }
}
