using Clipper2Lib;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>Letters and messages as etched areas, from the program's own text (any installed font).</summary>
public static class Labels
{
    /// <summary>
    /// The text as areas (flat mm, y up), as big as fits in <paramref name="maxW"/> × <paramref name="maxH"/>
    /// and centred on (<paramref name="cx"/>, <paramref name="cy"/>). Empty if the font has no letters for it.
    /// </summary>
    public static PathsD Text(string text, string font, double cx, double cy, double maxW, double maxH, bool bold = true)
    {
        text = text.Trim();
        if (text.Length == 0 || maxW < 0.5 || maxH < 0.5) return [];
        PathGeometry g;
        try { g = TextLayout.Build(text, new TextStyle(string.IsNullOrWhiteSpace(font) ? "Arial" : font, maxH, Bold: bold, Align: TextAlign.Center, FitWidthMm: maxW, FitHeightMm: maxH)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return []; }
        if (g.IsEmpty) return [];
        var (areas, _) = Geo.FromGeometry(g, 0.01);
        if (areas.Count == 0) return [];
        var b = Geo.Bounds(areas);
        double dx = cx - (b.left + b.right) / 2, dy = cy - (b.top + b.bottom) / 2;
        return Geo.Tidy(Geo.Move(areas, dx, dy));
    }
}
