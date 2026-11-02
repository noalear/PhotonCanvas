using Clipper2Lib;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// A texture laid over an area: its pieces, its bricks or stones as cells (for outlines that follow
/// them), and the face it covers. A wall's runs from its bottom-left corner, as the wall maker's does,
/// so a ruin's courses line up with whole walls beside it; a pile's is turned and moved at random.
/// </summary>
public sealed record TextureGrid(TextureParts Tex, List<Cell> Cells, PathsD Face, double Groove)
{
    /// <summary>The groove a texture is drawn with (the same limits as the wall maker's).</summary>
    public static double GrooveOf(TextureSpec t)
    {
        double a = Math.Max(0.5, t.SizeA), b = Math.Max(0.5, t.SizeB);
        return Math.Clamp(t.GrooveMm, 0.05, (Textures.Preset(t.Texture)?.SizeBLabel is null ? a : Math.Min(a, b)) / 2);
    }

    /// <summary>Over a wall's face: x from 0 to <paramref name="length"/>, y from 0 to <paramref name="height"/>.</summary>
    public static TextureGrid Wall(TextureSpec t, double length, double height, double x0, double x1, double top)
    {
        var tex = Textures.Make(t, new Face(length, height, x0, x1, top));
        double g = GrooveOf(t);
        return new TextureGrid(tex, Rough.Cells(tex, g), [Geo.Rect(-1, -1, length + 1, height + 1)], g);
    }

    /// <summary>Over a box, turned by <paramref name="deg"/> degrees about its middle and shifted a little.</summary>
    public static TextureGrid Turned(TextureSpec t, RectD box, double deg, double shiftX, double shiftY)
    {
        double a = Math.Max(0.5, t.SizeA), b = Math.Max(0.5, t.SizeB);
        double cx = (box.left + box.right) / 2, cy = (box.top + box.bottom) / 2;
        double r = Math.Sqrt(Math.Pow(box.right - box.left, 2) + Math.Pow(box.bottom - box.top, 2)) / 2 + Math.Max(a, b) * 2;
        double side = 2 * r;
        var tex = Textures.Make(t, new Face(side, side, 0, side, side));
        double g = GrooveOf(t);
        double rad = deg * Math.PI / 180, c = Math.Cos(rad), s = Math.Sin(rad);
        PointD M(PointD p)
        {
            double x = p.x - r + shiftX, y = p.y - r + shiftY;
            return new PointD(x * c - y * s + cx, x * s + y * c + cy);
        }
        var moved = new TextureParts(Geo.Map(tex.Raised, M), Geo.Map(tex.Etched, M), Geo.Map(tex.Lines, M));
        return new TextureGrid(moved, Rough.Cells(moved, g), Geo.Map(new PathsD { Geo.Rect(0, 0, side, side) }, M), g);
    }

    /// <summary>The texture inside a region: etched areas (the mortar, as grooves) and scored lines.</summary>
    public (PathsD Etched, List<PathD> Lines) In(PathsD region, EtchStyle style)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (region.Count == 0) return (etched, lines);
        if (style == EtchStyle.Grooves)
        {
            var mortar = Tex.Raised.Count > 0 ? Geo.Subtract(Face, Geo.Union(Tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(mortar, Tex.Etched), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(Tex.Raised.Select(Geo.Loop), region));
            lines.AddRange(Geo.ClipLines(Tex.Etched.Select(Geo.Loop), region));
        }
        lines.AddRange(Geo.ClipLines(Tex.Lines, region));
        return (Geo.Union(etched), lines);
    }
}
