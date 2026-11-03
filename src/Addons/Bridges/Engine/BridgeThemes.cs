using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for bridges: each
/// sets a stone bridge's texture, arches, parapet and road, and whether a plank bridge is wooden or a
/// metal gantry (grating on the deck, rivets on the beams). The kind and the sizes are left as they are.
/// </summary>
public static class BridgeThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    public static BridgeSpec Apply(BridgeSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        double k = Math.Clamp(s.ScaleMm / 28, 0.3, 4);
        double R(double v) => Math.Round(v * k, 1);
        var (texture, a, b, groove) = name switch
        {
            TerrainThemes.Dungeon => (ThemeTextures.DungeonBlocks, 10.0, 6.0, 0.8),
            TerrainThemes.Cave => (ThemeTextures.CaveRock, 12.0, 8.0, 0.9),
            TerrainThemes.SciFi => (ThemeTextures.SciFiPanels, 12.0, 8.0, 0.5),
            TerrainThemes.Desert => (ThemeTextures.Sandstone, 12.0, 5.0, 0.6),
            TerrainThemes.Gothic => ("Stone blocks", 7.0, 4.0, 0.5),
            _ => ("Stone blocks", 8.0, 4.0, 0.6),
        };
        bool metal = name == TerrainThemes.SciFi;
        var arch = name switch { TerrainThemes.Gothic => ArchShape.Pointed, TerrainThemes.Cave or TerrainThemes.SciFi => ArchShape.Segmental, _ => ArchShape.Round };
        // A pointed arch stands taller than a round one as wide: the road goes up to clear it.
        static double Rise(ArchShape a, double span) => a switch { ArchShape.Segmental => span / 4, ArchShape.Pointed => Math.Sqrt(0.5) * span, _ => span / 2 };
        double higher = Math.Max(0, Rise(arch, s.ArchSpanMm) - Rise(s.Arch, s.ArchSpanMm));
        return s with
        {
            HeightMm = Math.Round(s.HeightMm + higher, 1),
            Arch = arch,
            Theme = name,
            Texture = texture, Custom = null, SizeA = R(a), SizeB = R(b), GrooveMm = groove, DetailMm = 0,
            Parapet = name switch { TerrainThemes.Dungeon or TerrainThemes.Gothic => ParapetTop.Battlements, TerrainThemes.Cave => ParapetTop.None, _ => ParapetTop.Straight },
            Surface = name switch { TerrainThemes.Medieval or TerrainThemes.Gothic => RoadSurface.Cobbles, TerrainThemes.SciFi => RoadSurface.Plain, _ => RoadSurface.Flagstones },
            Metal = metal,
            Grain = !metal,
            // Cave planks are rough, a gantry's never; otherwise as they were.
            Rough = name == TerrainThemes.Cave || (!metal && s.Rough),
        };
    }
}
