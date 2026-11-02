using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) as floors: each
/// picks a texture, its sizes for the grid square, its groove and how many stones are cracked. The
/// grid, the joints and the sheet are left as they are.
/// </summary>
public static class FloorThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    /// <summary>The floor texture for a theme.</summary>
    public static string Texture(string? theme) => theme switch
    {
        TerrainThemes.Medieval => "Flagstones",
        TerrainThemes.Cave => "Cave floor",
        TerrainThemes.SciFi => "Sci-fi deck",
        TerrainThemes.Desert => "Sandstone slabs",
        TerrainThemes.Gothic => "Chequered tiles",
        _ => "Dungeon flagstones",
    };

    public static TileSpec Apply(TileSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        var p = FloorTextures.Preset(Texture(name))!;
        var (a, b) = FloorTextures.SizesFor(p, s.SquareMm);
        return s with
        {
            Theme = name, Texture = p.Name, Custom = null, SizeA = a, SizeB = p.SizeBLabel is null ? s.SizeB : b,
            GrooveMm = p.UsesGroove ? p.Groove : s.GrooveMm,
            CracksPct = name switch { TerrainThemes.Cave => 30, TerrainThemes.Dungeon => 20, TerrainThemes.Gothic => 5, _ => 12 },
        };
    }
}
