using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The textures the themes brought (dungeon blocks, cave rock, sci-fi panels, sandstone and adobe): their
/// names, sizes and looks. They're drawn by <see cref="TextureEngine"/> with every other preset, the same
/// way in every terrain maker, so a wall, a ruin, a bridge and a dice tower made with the same sizes and
/// variation match stone for stone.
///
/// Compiled into each terrain add-on that draws them (see its project file): nothing is shared at run time.
/// </summary>
internal static class ThemeTextures
{
    public const string DungeonBlocks = "Dungeon blocks";
    public const string CaveRock = "Cave rock";
    public const string SciFiPanels = "Sci-fi panels";
    public const string Sandstone = "Sandstone";
    public const string Adobe = "Adobe";

    /// <summary>A texture's name, the names of its two sizes and their defaults (mm, for 28 mm figures), its groove and what it looks like.</summary>
    public sealed record Info(string Name, string SizeALabel, double SizeA, string SizeBLabel, double SizeB, double Groove, string About);

    public static readonly IReadOnlyList<Info> All =
    [
        .. new[] { DungeonBlocks, CaveRock, SciFiPanels, Sandstone, Adobe }
            .Select(n => TextureEngine.Find(n)!)
            .Select(p => new Info(p.Name, p.SizeALabel!, p.SizeA, p.SizeBLabel!, p.SizeB, p.Groove, p.About)),
    ];

    public static Info? Find(string? name) => All.FirstOrDefault(i => i.Name == name);

    /// <summary>
    /// The texture's raised pieces, etched areas and scored lines over a face <paramref name="length"/> ×
    /// <paramref name="height"/> mm: <paramref name="a"/> and <paramref name="b"/> are its two sizes,
    /// <paramref name="g"/> the groove between pieces, worn the normal amount. Empty for a name that isn't one of these.
    /// </summary>
    public static (PathsD Raised, PathsD Etched, List<PathD> Lines) Make(string name, double length, double height, double a, double b, double g, Random rnd) =>
        Find(name) is null ? ([], [], []) : TextureEngine.Make(name, length, height, 0, length, height, a, b, g, rnd.Next());
}
