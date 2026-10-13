using System.Collections.Immutable;
using SkiaSharp;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

public enum TextAlign { Left, Center, Right }

/// <summary>How text looks. Sizes are millimetres.</summary>
/// <param name="SizeMm">Font size (em height) in mm.</param>
/// <param name="LetterSpacingMm">Extra space between letters (may be negative).</param>
/// <param name="LineSpacing">Line height as a multiple of the size.</param>
/// <param name="BendDeg">Text on an arc: how many degrees of a circle each line spans (+ arches up, − curves down, 0 straight).</param>
public sealed record TextStyle(
    string FontFamily = "Arial",
    double SizeMm = 10,
    bool Bold = false,
    bool Italic = false,
    double LetterSpacingMm = 0,
    double LineSpacing = 1.2,
    TextAlign Align = TextAlign.Left,
    double BendDeg = 0);

/// <summary>
/// Editable text. Its outline (<see cref="PathShape.Geometry"/>) is rebuilt
/// from the font whenever the words or style change, so everything that
/// works on paths (cutting, filling, offsets) works on text too.
/// </summary>
public sealed class TextShape : PathShape
{
    public required string Text { get; init; }
    public required TextStyle Style { get; init; }

    public static TextShape Create(string text, TextStyle style, Guid layerId, Affine transform, Guid? id = null, string? name = null, Guid? groupId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? FirstLine(text),
        LayerId = layerId,
        Transform = transform,
        GroupId = groupId,
        Text = text,
        Style = style,
        Geometry = TextLayout.Build(text, style),
    };

    /// <summary>The same text object with new words and/or style.</summary>
    public TextShape Edit(string text, TextStyle style) =>
        Create(text, style, LayerId, Transform, Id, Name == FirstLine(Text) ? null : Name, GroupId);

    static string FirstLine(string t)
    {
        var line = t.Split('\n')[0].Trim();
        return line.Length > 24 ? line[..24] + "…" : line.Length == 0 ? "Text" : line;
    }
}

/// <summary>Turns text into outlines with Skia's font engine.</summary>
public static class TextLayout
{
    const float Units = 100; // build at 100 units per em, then scale to mm

    /// <summary>Installed font families, alphabetical.</summary>
    public static IReadOnlyList<string> FontFamilies() =>
        [.. SKFontManager.Default.FontFamilies.Distinct().Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Outline of the text in mm. The block's top-left is at (0, 0); for
    /// centred/right alignment lines align within the widest line.
    /// </summary>
    public static PathGeometry Build(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text) || !(style.SizeMm > 0)) return PathGeometry.Empty;
        using var typeface = SKTypeface.FromFamilyName(style.FontFamily,
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Units);
        double scale = style.SizeMm / Units;
        double spacing = style.LetterSpacingMm / scale; // in font units
        float ascent = -font.Metrics.Ascent;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var laid = new List<(ushort[] Glyphs, double[] X, double Width)>();
        foreach (var line in lines)
        {
            var (glyphs, widths) = Glyphs(font, line);
            var xs = new double[glyphs.Length];
            double x = 0;
            for (int i = 0; i < glyphs.Length; i++)
            {
                xs[i] = x;
                x += widths[i] + (i < glyphs.Length - 1 ? spacing : 0);
            }
            laid.Add((glyphs, xs, x));
        }
        double blockWidth = laid.Count == 0 ? 0 : laid.Max(l => l.Width);
        var subpaths = new List<Subpath>();
        for (int li = 0; li < laid.Count; li++)
        {
            var (glyphs, xs, width) = laid[li];
            double baseline = ascent + li * Units * style.LineSpacing;
            double shift = style.Align switch
            {
                TextAlign.Center => (blockWidth - width) / 2,
                TextAlign.Right => blockWidth - width,
                _ => 0,
            };
            double bend = style.BendDeg * Math.PI / 180;
            double radius = Math.Abs(bend) > 1e-6 && width > 0 ? width / Math.Abs(bend) : 0;
            double lineCentre = shift + width / 2;
            var (_, widths) = Glyphs(font, lines[li]);
            for (int g = 0; g < glyphs.Length; g++)
            {
                using var path = font.GetGlyphPath(glyphs[g]);
                if (path is null || path.IsEmpty) continue;
                Affine place;
                if (radius > 0)
                {
                    // Glyph centre sits on the arc and is turned to follow it.
                    double centreX = shift + xs[g] + widths[g] / 2;
                    double theta = (centreX - lineCentre) / radius;
                    double sign = bend > 0 ? 1 : -1;
                    var onArc = new Vec2(lineCentre + radius * Math.Sin(theta), baseline + sign * radius * (1 - Math.Cos(theta)));
                    place = Affine.Translate(-widths[g] / 2, 0)
                        .Then(Affine.Rotate(sign * theta * 180 / Math.PI))
                        .Then(Affine.Translate(onArc));
                }
                else place = Affine.Translate(shift + xs[g], baseline);
                foreach (var sp in FromSkia(path)) subpaths.Add(sp.Transform(place.Then(Affine.Scale(scale, scale))));
            }
        }
        return new PathGeometry(subpaths);
    }

    static (ushort[] Glyphs, float[] Widths) Glyphs(SKFont font, string line)
    {
        var glyphs = new ushort[font.CountGlyphs(line)];
        font.GetGlyphs(line, glyphs);
        var widths = new float[glyphs.Length];
        font.GetGlyphWidths(glyphs, widths, Span<SKRect>.Empty, null);
        return (glyphs, widths);
    }

    /// <summary>Skia path → our subpaths (quadratics and conics become cubics).</summary>
    public static List<Subpath> FromSkia(SKPath path)
    {
        var o = new List<Subpath>();
        var segs = ImmutableArray.CreateBuilder<Seg>();
        Vec2 start = default, cur = default;
        bool open = false;
        void Flush(bool closed)
        {
            if (open && segs.Count > 0) o.Add(new Subpath(start, segs.ToImmutable(), closed));
            segs.Clear();
            open = false;
        }
        static Vec2 V(SKPoint p) => new(p.X, p.Y);
        using var it = path.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = it.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    Flush(false);
                    start = cur = V(pts[0]);
                    open = true;
                    break;
                case SKPathVerb.Line:
                    cur = V(pts[1]);
                    segs.Add(Seg.Line(cur));
                    break;
                case SKPathVerb.Quad:
                    AddQuad(segs, cur, V(pts[1]), V(pts[2]));
                    cur = V(pts[2]);
                    break;
                case SKPathVerb.Conic:
                {
                    var quads = SKPath.ConvertConicToQuads(pts[0], pts[1], pts[2], it.ConicWeight(), 2);
                    for (int i = 0; i + 2 < quads.Length; i += 2) AddQuad(segs, V(quads[i]), V(quads[i + 1]), V(quads[i + 2]));
                    cur = V(pts[2]);
                    break;
                }
                case SKPathVerb.Cubic:
                    segs.Add(Seg.Cubic(V(pts[1]), V(pts[2]), V(pts[3])));
                    cur = V(pts[3]);
                    break;
                case SKPathVerb.Close:
                    if (cur.Dist(start) > 1e-9) segs.Add(Seg.Line(start));
                    Flush(true);
                    cur = start;
                    break;
            }
        }
        Flush(false);
        return o;
    }

    static void AddQuad(ImmutableArray<Seg>.Builder segs, Vec2 p0, Vec2 q, Vec2 p3) =>
        segs.Add(Seg.Cubic(p0 + (q - p0) * (2.0 / 3), p3 + (q - p3) * (2.0 / 3), p3));
}
