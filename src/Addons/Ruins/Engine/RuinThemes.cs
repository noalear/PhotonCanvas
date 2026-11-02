using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for ruins: each sets
/// the texture (the wall maker's for that theme, so a ruin matches whole walls beside it) and the
/// pillars' shape. Sizes and the damage are left as they are.
/// </summary>
public static class RuinThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    public static RuinSpec Apply(RuinSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        var (texture, a, b, groove) = name switch
        {
            TerrainThemes.Dungeon => (ThemeTextures.DungeonBlocks, 14.0, 8.0, 0.9),
            TerrainThemes.Cave => (ThemeTextures.CaveRock, 16.0, 10.0, 1.0),
            TerrainThemes.SciFi => (ThemeTextures.SciFiPanels, 16.0, 12.0, 0.5),
            TerrainThemes.Desert => (ThemeTextures.Sandstone, 16.0, 7.0, 0.7),
            TerrainThemes.Gothic => ("Stone blocks", 10.0, 5.0, 0.6),
            _ => ("Brick", 8.0, 3.5, 0.6),
        };
        return s with
        {
            Theme = name, Texture = texture, Custom = null, SizeA = a, SizeB = b, GrooveMm = groove,
            PillarShape = name switch { TerrainThemes.Dungeon or TerrainThemes.SciFi => PillarShape.Square, TerrainThemes.Gothic => PillarShape.Octagon, _ => PillarShape.Round },
        };
    }
}
