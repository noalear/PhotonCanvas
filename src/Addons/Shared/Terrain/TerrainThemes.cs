namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The looks every terrain maker offers (walls, floor tiles, roofs, stairs, ruins, bridges, scatter,
/// furniture, the game kit and whole buildings): picking one sets that maker's textures, openings and
/// details to match, so pieces from different makers go together on the table.
///
/// This file is compiled into each terrain add-on (see its project file), so each keeps working on
/// its own: nothing here is shared at run time. Keep it free of anything but plain data.
/// </summary>
internal static class TerrainThemes
{
    public const string Medieval = "Medieval";
    public const string Dungeon = "Dungeon";
    public const string Cave = "Cave";
    public const string SciFi = "Sci-fi";
    public const string Desert = "Desert";
    public const string Gothic = "Gothic";

    public sealed record Info(string Name, string About);

    public static readonly IReadOnlyList<Info> All =
    [
        new(Medieval, "Village and castle: brick, fieldstone and timber, plank doors under round arches."),
        new(Dungeon, "Big worn stone blocks, flagstone floors, studded doors, barred windows and torch sconces."),
        new(Cave, "Rough rock walls and floors, ragged openings, stalagmites, giant mushrooms and boulders."),
        new(SciFi, "Panelled metal walls, deck plates and grating, angled blast doors, wall lamps, consoles, crates and drums."),
        new(Desert, "Sandstone and mud plaster, square openings, low clay roofs, urns, palms and rocks."),
        new(Gothic, "Fine-cut stone, pointed arches, steep slate roofs, chequered floors, iron railings and gravestones."),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(t => t.Name)];

    /// <summary>The theme's place in <see cref="All"/>, or 0 (Medieval) for a name that isn't one.</summary>
    public static int IndexOf(string? name)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Name == name) return i;
        return 0;
    }

    /// <summary>The theme at <paramref name="index"/> in <see cref="All"/>, or Medieval.</summary>
    public static string At(int index) => index >= 0 && index < All.Count ? All[index].Name : Medieval;

    public static string About(string? name) => All[IndexOf(name)].About;
}
