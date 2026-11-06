using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Roofs;
using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// The themes (Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) for a whole building: each takes its
/// pieces' looks from the maker that cuts them, so a themed building matches walls, roofs, floor tiles
/// and furniture made on their own in the same theme. It sets the walls' texture, the door's and the
/// windows' shape (the windows sized for the figures), the bars, the roof's covering and slope, the floor
/// tiles, the furniture's detail and the way up. The size, storeys and furniture list stay as they are.
/// </summary>
public static class BuildingThemes
{
    public static IReadOnlyList<string> Names => TerrainThemes.Names;

    public static string About(string? theme) => TerrainThemes.About(theme);

    /// <summary>The cyberpunk city's outside walls (the sci-fi look's), from the texture engine.</summary>
    public const string HabConcrete = "Hab concrete", SteelCladding = "Steel cladding", Shopfront = "Shopfront", GlassFacade = "Glass facade";

    public static BuildingSpec Apply(BuildingSpec s, string? theme)
    {
        string name = TerrainThemes.At(TerrainThemes.IndexOf(theme));
        double k = Math.Max(0.2, s.ScaleMm / 28);
        var wall = WallThemes.Apply(new WallSpec(), name);
        var (winShape, ww, wh, bars) = WallThemes.Window(name);
        // A sci-fi building's outside is the cyberpunk city's (hab concrete, plain rectangular windows): the wall
        // maker's sci-fi panels are a ship's or a base's inside, kept for the walls inside.
        bool city = name == TerrainThemes.SciFi;
        if (city) (winShape, ww, wh, bars) = (OpeningShape.Rectangle, 13, 12, WindowBars.None);
        // A watchtower's arrow slits stay in the old-world looks.
        bool slits = s.WindowShape == OpeningShape.ArrowSlit && name is TerrainThemes.Medieval or TerrainThemes.Dungeon or TerrainThemes.Gothic;
        var (shingle, pitch) = RoofThemes.Shingle(name);
        var door = wall.DoorShape;
        return s with
        {
            Theme = name,
            Texture = name == TerrainThemes.Medieval ? (s.Storeys > 1 ? "Stone blocks" : "Fieldstone") : city ? HabConcrete : wall.Texture,
            UpperTexture = name switch { TerrainThemes.Medieval when s.Storeys > 1 => "Timber frame", TerrainThemes.Desert => "Adobe", _ => null },
            DoorShape = door,
            DoorLook = wall.DoorLook,
            // A door that swings is only made for square, arched and pointed doorways.
            Door = s.Door == DoorStyle.Swinging && door is OpeningShape.Chamfered or OpeningShape.Rough ? DoorStyle.Loose : s.Door,
            WindowShape = slits ? s.WindowShape : winShape,
            WindowWidthMm = slits ? s.WindowWidthMm : Math.Round(ww * k, 1),
            WindowHeightMm = slits ? s.WindowHeightMm : Math.Round(wh * k, 1),
            Bars = slits ? s.Bars : bars,
            // Taller windows (a chapel's) brought down so they still fit under the top of the wall.
            SillMm = slits ? s.SillMm : Math.Round(Math.Max(Math.Min(4 * k, s.SillMm), Math.Min(s.SillMm, s.StoreyHeightMm - wh * k - 4 * k)), 1),
            RoofTexture = name == TerrainThemes.Medieval ? s.RoofTexture : shingle,
            RoofPitchDeg = name == TerrainThemes.Medieval ? s.RoofPitchDeg : pitch,
            FloorTexture = FloorThemes.Texture(name),
            Detail = FurnitureThemes.Detail(name),
            WayUp = s.WayUp == WayUpStyle.None ? s.WayUp : name switch
            {
                TerrainThemes.SciFi or TerrainThemes.Desert => WayUpStyle.Ladder,
                TerrainThemes.Gothic => WayUpStyle.Spiral,
                _ => s.WayUp,
            },
            Items = [.. s.Items.Select(r => r.Item.Kind == Furniture.ItemKind.WallDoor ? r with { Item = r.Item with { Shape = FurnitureThemes.Door(name) } } : r)],
        };
    }
}
