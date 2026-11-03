using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The finish on a face: the furniture maker's wood grain and planks, or the wall maker's stone,
/// brick, tile, plate and hexagon textures (Shared/Terrain/TextureEngine.cs), drawn the same way so a dice
/// tower can match the scenery. Sizes are the wall maker's defaults times a scale.
/// </summary>
public static class Surface
{
    /// <summary>The etched areas and scored lines of the finish over <paramref name="region"/> (flat mm, y up).</summary>
    public static (PathsD Etched, List<PathD> Lines) Make(PathsD region, Finish finish, double scale, bool alongU, Random rnd)
    {
        if (finish == Finish.None || region.Count == 0) return ([], []);
        double k = Math.Clamp(scale, 0.25, 10);
        var b = Geo.Bounds(region);
        double len = b.right - b.left, h = b.bottom - b.top;
        if (len < 1 || h < 1) return ([], []);
        if (finish is Finish.WoodGrain or Finish.Planks)
        {
            // The furniture's wood is drawn for 28 mm furniture: drawn small, then scaled up.
            var small = Geo.Map(region, p => new PointD(p.x / k, p.y / k));
            var (e, l) = WoodFace.Make(small, alongU, finish == Finish.Planks, true, 3, rnd);
            return (Geo.Map(e, p => new PointD(p.x * k, p.y * k)), Geo.Map(l, p => new PointD(p.x * k, p.y * k)));
        }
        if (EngineTexture(finish) is { } name)
        {
            // Drawn by the shared texture engine (Shared/Terrain/TextureEngine.cs), as the wall maker draws them, at its default sizes times the scale.
            var p = TextureEngine.Find(name)!;
            var (tr, te, tl) = TextureEngine.Make(name, len, h, 0, len, h, p.SizeA * k, (p.SizeBLabel is null ? p.SizeA : p.SizeB) * k, p.Groove * k, rnd.Next(), 0.5, p.Detail * Math.Sqrt(k));
            var r2 = Geo.Move(tr, b.left, b.top);
            var e2 = Geo.Move(te, b.left, b.top);
            var l2 = Geo.Map(tl, q => new PointD(q.x + b.left, q.y + b.top));
            var etched = r2.Count > 0 ? Geo.Subtract(region, Geo.Union(r2)) : [];
            return (Geo.Union(etched, Geo.Intersect(e2, region)), Geo.ClipLines(l2, region));
        }
        return ([], []);
    }

    /// <summary>The finishes drawn by the shared texture engine: the wall maker's textures and the themes' own.</summary>
    static string? EngineTexture(Finish f) => f switch
    {
        Finish.Brick => "Brick",
        Finish.Tiles => "Tiles",
        Finish.StoneBlocks => "Stone blocks",
        Finish.Fieldstone => "Fieldstone",
        Finish.MetalPlates => "Metal plates",
        Finish.Hexagons => "Hexagons",
        _ => ThemeTexture(f),
    };

    /// <summary>The themes' textures (Shared/Terrain/ThemeTextures.cs), drawn as the wall maker draws them.</summary>
    static string? ThemeTexture(Finish f) => f switch
    {
        Finish.DungeonBlocks => ThemeTextures.DungeonBlocks,
        Finish.CaveRock => ThemeTextures.CaveRock,
        Finish.SciFiPanels => ThemeTextures.SciFiPanels,
        Finish.Sandstone => ThemeTextures.Sandstone,
        Finish.Adobe => ThemeTextures.Adobe,
        _ => null,
    };
}
