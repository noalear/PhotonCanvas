using System.Text.Json;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// Your own textures, kept in the add-on's data folder (textures.json) so they're in the list next time,
/// and made from drawn shapes or an SVG file. Each one is also saved inside the designs that use it.
/// </summary>
public sealed class CustomTextures
{
    readonly string? _file;
    readonly List<CustomTexture> _list = [];

    public CustomTextures(string? folder)
    {
        _file = folder is null ? null : Path.Combine(folder, "textures.json");
        try
        {
            if (_file is not null && File.Exists(_file) && JsonSerializer.Deserialize<List<CustomTexture>>(File.ReadAllText(_file), WallJson.Options) is { } l)
                _list.AddRange(l.Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Path.Length > 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
    }

    public IReadOnlyList<CustomTexture> All => _list;

    public CustomTexture? Get(string name) => _list.FirstOrDefault(t => t.Name == name);

    /// <summary>Adds (or replaces, by name) a texture and saves the list.</summary>
    public void Put(CustomTexture t)
    {
        _list.RemoveAll(x => x.Name == t.Name);
        _list.Add(t);
        Save();
    }

    public void Remove(string name)
    {
        if (_list.RemoveAll(x => x.Name == name) > 0) Save();
    }

    void Save()
    {
        if (_file is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_list, WallJson.Options));
    }

    /// <summary>
    /// A texture from shapes in the program's millimetres (y down): moved so their top-left corner is at
    /// 0, 0, repeating every <paramref name="gapMm"/> beyond their size.
    /// </summary>
    public static CustomTexture? FromGeometry(string name, IEnumerable<PathGeometry> shapes, double gapMm, double rowShift)
    {
        var all = shapes.Where(g => !g.IsEmpty).ToList();
        if (all.Count == 0) return null;
        var box = all.Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));
        if (box.IsEmpty || box.Width < 0.05 && box.Height < 0.05) return null;
        var move = Affine.Translate(-box.MinX, -box.MinY);
        var path = new PathGeometry(all.SelectMany(g => g.Transform(move).Subpaths));
        return new CustomTexture
        {
            Name = name.Trim(),
            Path = path.ToSvgPathData(),
            TileWidthMm = Math.Max(0.1, box.Width + Math.Max(0, gapMm)),
            TileHeightMm = Math.Max(0.1, box.Height + Math.Max(0, gapMm)),
            RowShift = Math.Clamp(rowShift, 0, 1),
        };
    }

    /// <summary>A texture from an SVG file's shapes (at the size the file gives them).</summary>
    public static CustomTexture? FromSvg(string name, string file, double gapMm, double rowShift) =>
        FromGeometry(name, SvgImport.Load(file).Paths.Select(p => p.Geometry), gapMm, rowShift);
}
