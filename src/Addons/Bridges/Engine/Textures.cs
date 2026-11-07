using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>What a preset texture asks for: the names of its two sizes (null = not used), its defaults and its usual detail size (mm).</summary>
public sealed record TexturePreset(string Name, string? SizeALabel, double SizeA, string? SizeBLabel, double SizeB, bool UsesGroove, double Groove, bool Random, string About, double Detail = 0.5);

/// <summary>
/// A texture laid over the face: the raised pieces (bricks, stones, planks: what's between them is
/// etched away), areas etched as they are (nail holes, your own closed shapes) and lines scored as they
/// are (wood grain, rivets, your own open shapes).
/// </summary>
public sealed record TextureParts(PathsD Raised, PathsD Etched, List<PathD> Lines)
{
    public static readonly TextureParts None = new([], [], []);
}

/// <summary>The face a texture covers: the panel's whole size, the part between the plain ends, and the top of the wall's body (under any battlements).</summary>
public readonly record struct Face(double Length, double Height, double X0, double X1, double Top);

/// <summary>
/// The wall maker's preset textures and your own, drawn over a face: the same presets, sizes and
/// layouts as Walls (Mini-Designer → Buildings → Walls), so a bridge can match the walls beside it. Copied from the
/// wall maker so this add-on works without it.
/// </summary>
public static class Textures
{
    public const string Plain = "Plain";

    /// <summary>The shared presets (Shared/Terrain/TextureEngine.cs: brick, stone, wood, metal and the themes' own), then Plain.</summary>
    public static readonly IReadOnlyList<TexturePreset> Presets =
    [
        .. TextureEngine.Presets.Select(t => new TexturePreset(t.Name, t.SizeALabel, t.SizeA, t.SizeBLabel, t.SizeB, t.UsesGroove, t.Groove, true, t.About, t.Detail)),
        new(Plain, null, 0, null, 0, false, 0, false, "No texture: just the outline, openings and mounts."),
    ];

    public static TexturePreset? Preset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>The texture's pieces over the face (with a little over the edges; they're clipped to the panel afterwards), worn <paramref name="wear"/> (0–1) with marks <paramref name="detail"/> mm across (0 = the texture's own).</summary>
    public static TextureParts Make(string texture, double sizeA, double sizeB, double groove, int seed, CustomTexture? custom, Face f, double wear = 0.5, double detail = 0)
    {
        if (custom is not null && texture == custom.Name) return Custom(custom, Math.Max(0.5, sizeA), f);
        var (r, e, l) = TextureEngine.Make(texture, f.Length, f.Height, f.X0, f.X1, f.Top, sizeA, sizeB, groove, seed, wear, detail);
        return new(r, e, l);
    }

    /// <summary>Your own shapes repeated in rows over the face, scaled so a tile is <paramref name="width"/> wide.</summary>
    static TextureParts Custom(CustomTexture c, double width, Face f)
    {
        PathGeometry geo;
        try { geo = PathGeometry.FromSvgPathData(c.Path); }
        catch (Exception) { return TextureParts.None; }
        var (areas, lines) = Geo.FromGeometry(geo);
        double k = width / Math.Max(0.1, c.TileWidthMm), tw = c.TileWidthMm * k, th = Math.Max(0.1, c.TileHeightMm * k);
        // y down in the tile (top-left at 0, 0) → y up on the wall, the tile's bottom on the row's.
        PointD Tile(PointD p) => new(p.x * k, (c.TileHeightMm + p.y) * k);
        var tileAreas = Geo.Tidy(Geo.Map(areas, Tile));
        var tileLines = Geo.Map(lines, Tile);
        if (tw * th < 0.01 || f.Length * f.Height / (tw * th) > 40000) return TextureParts.None;
        var etched = new PathsD();
        var scored = new List<PathD>();
        int rows = (int)Math.Ceiling(f.Height / th) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * th, off = r % 2 == 1 ? Math.Clamp(c.RowShift, 0, 1) * tw : 0;
            for (double x = off - tw; x < f.Length + tw; x += tw)
            {
                etched.AddRange(Geo.Move(tileAreas, x, y));
                scored.AddRange(tileLines.Select(l => Geo.Map(l, p => new PointD(p.x + x, p.y + y))));
            }
        }
        return new([], Geo.Union(etched), scored);
    }
}
