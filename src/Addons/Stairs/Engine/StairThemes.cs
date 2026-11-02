using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for floors and the
/// way up: each sets the floor's texture and the kind of way up (open stairs, solid stone steps, a
/// spiral or a ladder). The walls it sits on, the opening's place and the sizes are left as they are.
/// </summary>
public static class StairThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    /// <summary>The floor's texture, its sizes and groove for a theme.</summary>
    public static (string Texture, double A, double B, double Groove) Texture(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme)) switch
    {
        TerrainThemes.Dungeon => (ThemeTextures.DungeonBlocks, 14, 8, 0.9),
        TerrainThemes.Cave => (ThemeTextures.CaveRock, 16, 10, 1.0),
        TerrainThemes.SciFi => (ThemeTextures.SciFiPanels, 16, 12, 0.5),
        TerrainThemes.Desert => (ThemeTextures.Sandstone, 16, 7, 0.7),
        TerrainThemes.Gothic => ("Tiles", 6, 6, 0.6),
        _ => ("Wood planks (across)", 5, 35, 0.5),
    };

    public static WayUp Way(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme)) switch
    {
        TerrainThemes.Dungeon or TerrainThemes.Cave or TerrainThemes.Desert => WayUp.SolidStairs,
        TerrainThemes.SciFi => WayUp.Ladder,
        TerrainThemes.Gothic => WayUp.Spiral,
        _ => WayUp.Stairs,
    };

    public static FloorSpec Apply(FloorSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        var (texture, a, b, groove) = Texture(name);
        return s with
        {
            Theme = name, Texture = texture, Custom = null, SizeA = a, SizeB = b, GrooveMm = groove,
            // Nothing stays nothing: a theme doesn't add a way up that wasn't wanted.
            Way = s.Way == WayUp.None ? WayUp.None : Way(name),
            Rail = name == TerrainThemes.SciFi ? Railing.Both : s.Rail,
        };
    }
}
