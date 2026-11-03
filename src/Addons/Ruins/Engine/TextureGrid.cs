using Clipper2Lib;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// A texture laid over an area: its pieces, its bricks or stones as cells (for outlines that follow
/// them), and the face it covers. A wall's runs from its bottom-left corner, as the wall maker's does,
/// so a ruin's courses line up with whole walls beside it; a pile's is turned and moved at random.
/// The texture is drawn worn (as the spec's wear says), but its cells, and <see cref="Whole"/>, are the
/// bricks and stones as laid, before any wear: a wall breaks along the middle of its mortar, and loose
/// bricks come out the size they were laid.
/// </summary>
public sealed record TextureGrid(TextureParts Tex, List<Cell> Cells, PathsD Face, double Groove)
{
    /// <summary>The texture with no wear (the same layout): its raised pieces are the bricks and stones as laid.</summary>
    public TextureParts Whole { get; init; } = Tex;

    /// <summary>The texture as the spec says, and as laid (no wear), over a face.</summary>
    static (TextureParts Worn, TextureParts Whole) Both(TextureSpec t, Face face)
    {
        var worn = Textures.Make(t, face);
        return (worn, t.Wear > 0 ? Textures.Make(t with { Wear = 0 }, face) : worn);
    }

    /// <summary>The groove a texture is drawn with (the same limits as the wall maker's).</summary>
    public static double GrooveOf(TextureSpec t)
    {
        double a = Math.Max(0.5, t.SizeA), b = Math.Max(0.5, t.SizeB);
        return Math.Clamp(t.GrooveMm, 0.05, (Textures.Preset(t.Texture)?.SizeBLabel is null ? a : Math.Min(a, b)) / 2);
    }

    /// <summary>Over a wall's face: x from 0 to <paramref name="length"/>, y from 0 to <paramref name="height"/>.</summary>
    public static TextureGrid Wall(TextureSpec t, double length, double height, double x0, double x1, double top)
    {
        var (tex, whole) = Both(t, new Face(length, height, x0, x1, top));
        double g = GrooveOf(t);
        return new TextureGrid(tex, Rough.Cells(whole, g), [Geo.Rect(-1, -1, length + 1, height + 1)], g) { Whole = whole };
    }

    /// <summary>Over a box, turned by <paramref name="deg"/> degrees about its middle and shifted a little.</summary>
    public static TextureGrid Turned(TextureSpec t, RectD box, double deg, double shiftX, double shiftY)
    {
        double a = Math.Max(0.5, t.SizeA), b = Math.Max(0.5, t.SizeB);
        double cx = (box.left + box.right) / 2, cy = (box.top + box.bottom) / 2;
        double r = Math.Sqrt(Math.Pow(box.right - box.left, 2) + Math.Pow(box.bottom - box.top, 2)) / 2 + Math.Max(a, b) * 2;
        double side = 2 * r;
        var (tex, whole) = Both(t, new Face(side, side, 0, side, side));
        double g = GrooveOf(t);
        double rad = deg * Math.PI / 180, c = Math.Cos(rad), s = Math.Sin(rad);
        PointD M(PointD p)
        {
            double x = p.x - r + shiftX, y = p.y - r + shiftY;
            return new PointD(x * c - y * s + cx, x * s + y * c + cy);
        }
        var moved = new TextureParts(Geo.Map(tex.Raised, M), Geo.Map(tex.Etched, M), Geo.Map(tex.Lines, M));
        var movedWhole = new TextureParts(Geo.Map(whole.Raised, M), Geo.Map(whole.Etched, M), Geo.Map(whole.Lines, M));
        return new TextureGrid(moved, Rough.Cells(movedWhole, g), Geo.Map(new PathsD { Geo.Rect(0, 0, side, side) }, M), g) { Whole = movedWhole };
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
        // The texture's own lines (grain, cracks, scratches) stop a hair inside the edge: Clipper can leave the end of
        // a line that runs almost along a broken edge a few hundredths of a millimetre outside it.
        var inside = Geo.Offset(region, -0.05);
        lines.AddRange(Geo.ClipLines(Tex.Lines, inside.Count > 0 ? inside : region));
        return (Geo.Union(etched), lines);
    }
}
