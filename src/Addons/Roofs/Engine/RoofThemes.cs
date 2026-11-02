using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// The themes (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for roofs and
/// towers: each sets the shingles and the pitch, the gable ends' texture and window, and a tower's
/// stonework, door and windows. Sizes, the roof's shape and the fit are left as they are.
/// </summary>
public static class RoofThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    static string Name(string? theme) => TerrainThemes.At(TerrainThemes.IndexOf(theme));

    /// <summary>The shingles and pitch (degrees) for a theme.</summary>
    public static (string Shingle, double Pitch) Shingle(string? theme) => Name(theme) switch
    {
        TerrainThemes.Dungeon => ("Slate", 50),
        TerrainThemes.Cave => ("Wooden shingles", 40),
        TerrainThemes.SciFi => ("Corrugated metal", 20),
        TerrainThemes.Desert => ("Clay tiles", 25),
        TerrainThemes.Gothic => ("Slate", 60),
        _ => ("Slate", 45),
    };

    public static GableWindow Window(string? theme) => Name(theme) switch
    {
        TerrainThemes.Dungeon => GableWindow.Arched,
        TerrainThemes.Cave => GableWindow.None,
        TerrainThemes.SciFi => GableWindow.Angled,
        TerrainThemes.Desert => GableWindow.Square,
        TerrainThemes.Gothic => GableWindow.Pointed,
        _ => GableWindow.Round,
    };

    public static RoofSpec Apply(RoofSpec s, string? theme)
    {
        string name = Name(theme);
        var (shingle, pitch) = Shingle(name);
        var p = Shingles.Preset(shingle)!;
        var wall = WallThemes.Apply(new WallSpec(), name);
        // A sandy plaster suits a desert roof's gables better than the walls' sandstone.
        var (gt, ga, gb, gg) = name == TerrainThemes.Desert ? ("Adobe", 14.0, 3.0, 0.5) : (wall.Texture, wall.SizeA, wall.SizeB, wall.GrooveMm);
        return s with
        {
            Theme = name,
            Shingle = p.Name, ShingleA = p.SizeA, ShingleB = p.SizeBLabel is null ? s.ShingleB : p.SizeB, ShingleGrooveMm = p.UsesGroove ? p.Groove : s.ShingleGrooveMm,
            PitchDeg = s.Style == RoofStyle.Cone ? s.PitchDeg : pitch,
            GableTexture = gt, GableCustom = null, GableA = ga, GableB = gb, GableGrooveMm = gg,
            Window = Window(name),
            ConeEdge = name switch { TerrainThemes.SciFi or TerrainThemes.Desert => ConeEdge.Smooth, TerrainThemes.Gothic or TerrainThemes.Dungeon => ConeEdge.Slates, _ => ConeEdge.Scallops },
        };
    }

    public static TowerSpec Apply(TowerSpec s, string? theme)
    {
        string name = Name(theme);
        var (texture, a, b, groove) = name switch
        {
            TerrainThemes.Dungeon => ("Stone blocks", 14.0, 6.0, 0.9),
            TerrainThemes.Cave => ("Fieldstone", 10.0, 6.0, 1.0),
            TerrainThemes.SciFi => ("Corrugated metal", 2.5, 6.0, 0.6),
            TerrainThemes.Desert => ("Stone blocks", 16.0, 6.0, 0.7),
            TerrainThemes.Gothic => ("Stone blocks", 10.0, 6.0, 0.6),
            _ => ("Stone blocks", 12.0, 6.0, 0.8),
        };
        var wall = WallThemes.Apply(new WallSpec(), name);
        var (win, ww, wh) = name switch
        {
            TerrainThemes.Cave => (OpeningShape.Rough, 6.0, 8.0),
            TerrainThemes.SciFi => (OpeningShape.Chamfered, 8.0, 5.0),
            TerrainThemes.Desert => (OpeningShape.Rectangle, 4.0, 7.0),
            TerrainThemes.Gothic => (OpeningShape.Pointed, 4.0, 12.0),
            _ => (OpeningShape.ArrowSlit, 3.0, 12.0),
        };
        return s with
        {
            Theme = name,
            Texture = texture, SizeA = a, SizeB = b, GrooveMm = groove,
            DoorShape = wall.DoorShape, WindowShape = win, WindowWidthMm = ww, WindowHeightMm = wh,
            ConeEdge = name switch { TerrainThemes.SciFi or TerrainThemes.Desert => ConeEdge.Smooth, TerrainThemes.Gothic or TerrainThemes.Dungeon => ConeEdge.Slates, _ => ConeEdge.Scallops },
            ConePitchDeg = name == TerrainThemes.Gothic ? 70 : name == TerrainThemes.Desert ? 45 : s.ConePitchDeg,
        };
    }
}
