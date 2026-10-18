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
/// <param name="PathShiftMm">Text on a path: slide the words this far along the path (mm).</param>
/// <param name="Weld">Merge letters that touch or overlap into one outline (script fonts, tight spacing), so no line is cut twice.</param>
/// <param name="SingleLine">One line down the middle of each stroke instead of an outline: fast, clean engraving (put it on a Score layer).</param>
public sealed record TextStyle(
    string FontFamily = "Arial",
    double SizeMm = 10,
    bool Bold = false,
    bool Italic = false,
    double LetterSpacingMm = 0,
    double LineSpacing = 1.2,
    TextAlign Align = TextAlign.Left,
    double BendDeg = 0,
    double PathShiftMm = 0,
    bool Weld = false,
    bool SingleLine = false);

/// <summary>
/// Editable text. Its outline (<see cref="PathShape.Geometry"/>) is rebuilt
/// from the font whenever the words or style change, so everything that
/// works on paths (cutting, filling, offsets) works on text too.
/// </summary>
public sealed class TextShape : PathShape
{
    /// <summary>The words as typed, fields such as <c>{serial}</c> included.</summary>
    public required string Text { get; init; }
    public required TextStyle Style { get; init; }
    /// <summary>The words with their fields filled in (what is drawn and marked); null when there are no fields.</summary>
    public string? Shown { get; init; }
    /// <summary>A path the words follow (in the text's own coordinates), or null for straight lines.</summary>
    public PathGeometry? Guide { get; init; }

    public static TextShape Create(string text, TextStyle style, Guid layerId, Affine transform, Guid? id = null, string? name = null, Guid? groupId = null, string? shown = null, PathGeometry? guide = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? FirstLine(text),
        LayerId = layerId,
        Transform = transform,
        GroupId = groupId,
        Text = text,
        Style = style,
        Shown = shown,
        Guide = guide,
        Geometry = TextLayout.Build(shown ?? text, style, guide),
    };

    /// <summary>The same text object with new words and/or style (fields are filled in again when the edit is committed).</summary>
    public TextShape Edit(string text, TextStyle style) =>
        Create(text, style, LayerId, Transform, Id, Name == FirstLine(Text) ? null : Name, GroupId, text == Text ? Shown : null, Guide);

    /// <summary>The same text showing other values for its fields.</summary>
    public TextShape WithShown(string? shown) => Create(Text, Style, LayerId, Transform, Id, Name, GroupId, shown, Guide);

    /// <summary>The same text following a path given in world mm (null: back to straight lines).</summary>
    public TextShape OnPath(PathGeometry? worldPath) =>
        Create(Text, Style, LayerId, Transform, Id, Name, GroupId, Shown,
            worldPath is null || !Transform.IsInvertible ? null : worldPath.Transform(Transform.Invert()));

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
    public static PathGeometry Build(string text, TextStyle style, PathGeometry? guide = null)
    {
        var g = BuildLetters(text, style, guide);
        if (style.SingleLine && !g.IsEmpty) return SingleLines(g);
        if (!style.Weld || g.IsEmpty) return g;
        // Each letter on its own (keeping its holes), then all merged.
        var letters = new List<PathGeometry>();
        var current = new List<Subpath>();
        foreach (var sp in g.Subpaths)
        {
            // A new letter starts with an outline that isn't inside the previous one's box.
            if (current.Count > 0 && !LetterBox(current).Contains(Rect2.Bounds(sp.Flatten(0.05))))
            {
                letters.Add(new PathGeometry(current));
                current = [];
            }
            current.Add(sp);
        }
        if (current.Count > 0) letters.Add(new PathGeometry(current));
        return Geometry.ShapeGeometry.Union(letters, 0.005);
    }

    /// <summary>The letters' strokes as single centre lines: the outlines are drawn filled, then thinned.</summary>
    static PathGeometry SingleLines(PathGeometry outlines)
    {
        var box = Rect2.Bounds(outlines.Flatten(0.02).SelectMany(f => f.Points).ToList());
        if (box.IsEmpty) return outlines;
        // About 30 pixels per mm, fewer for big text so it stays quick.
        double k = Math.Min(30, 2400 / Math.Max(box.Width, box.Height));
        int w = Math.Max(1, (int)Math.Ceiling(box.Width * k) + 4), h = Math.Max(1, (int)Math.Ceiling(box.Height * k) + 4);
        using var bmp = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Black);
            c.Translate(2, 2);
            c.Scale((float)k);
            c.Translate((float)-box.MinX, (float)-box.MinY);
            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var sp in outlines.Subpaths.Where(s => !s.Segs.IsEmpty))
            {
                path.MoveTo((float)sp.Start.X, (float)sp.Start.Y);
                foreach (var seg in sp.Segs)
                {
                    if (seg.Kind == SegKind.Line) path.LineTo((float)seg.To.X, (float)seg.To.Y);
                    else path.CubicTo((float)seg.C1.X, (float)seg.C1.Y, (float)seg.C2.X, (float)seg.C2.Y, (float)seg.To.X, (float)seg.To.Y);
                }
                path.Close();
            }
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
            c.DrawPath(path, paint);
        }
        var ink = new bool[w * h];
        var px = bmp.GetPixelSpan();
        for (int i = 0; i < ink.Length; i++) ink[i] = px[i] > 127;
        var lines = Tracing.Centreline.Trace(ink, w, h, minLengthPx: k * 0.3, smoothPx: Math.Max(0.5, k * 0.02));
        var toMm = Affine.Translate(-2, -2).Then(Affine.Scale(1 / k, 1 / k)).Then(Affine.Translate(box.MinX, box.MinY));
        return Tracing.Centreline.ToGeometry(lines, toMm);
    }

    static Rect2 LetterBox(List<Subpath> subs) => subs.Select(s => Rect2.Bounds(s.Flatten(0.05))).Aggregate(Rect2.Empty, (a, b) => a.Union(b));

    static PathGeometry BuildLetters(string text, TextStyle style, PathGeometry? guide)
    {
        if (string.IsNullOrEmpty(text) || !(style.SizeMm > 0)) return PathGeometry.Empty;
        if (guide?.Subpaths.FirstOrDefault(s => !s.Segs.IsEmpty) is { } along) return BuildOnPath(text, style, along);
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

    /// <summary>
    /// Text following a path: each letter's middle sits on the path, turned to its direction.
    /// Left alignment starts at the path's start, centre centres it, right ends at its end;
    /// further lines go below the first. On a closed path the words wrap round.
    /// </summary>
    static PathGeometry BuildOnPath(string text, TextStyle style, Subpath path)
    {
        using var typeface = SKTypeface.FromFamilyName(style.FontFamily,
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Units);
        double scale = style.SizeMm / Units;
        var pts = path.Flatten(0.01);
        if (path.Closed && pts.Count > 1 && pts[0].Dist(pts[^1]) > 1e-9) pts.Add(pts[0]);
        if (pts.Count < 2) return PathGeometry.Empty;
        var cum = Geometry.PathTools.CumulativeLengths(pts);
        double length = cum[^1];
        if (length < 1e-9) return PathGeometry.Empty;
        var subpaths = new List<Subpath>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var (glyphs, widths) = Glyphs(font, lines[li]);
            double x = 0;
            var xs = new double[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                xs[i] = x;
                x += widths[i] * scale + (i < glyphs.Length - 1 ? style.LetterSpacingMm : 0);
            }
            double width = x;
            double start = style.Align switch
            {
                TextAlign.Center => (length - width) / 2,
                TextAlign.Right => length - width,
                _ => 0,
            } + style.PathShiftMm;
            double below = li * style.SizeMm * style.LineSpacing;
            for (int g = 0; g < glyphs.Length; g++)
            {
                using var gp = font.GetGlyphPath(glyphs[g]);
                if (gp is null || gp.IsEmpty) continue;
                double w = widths[g] * scale;
                double s = start + xs[g] + w / 2;
                if (path.Closed) s = ((s % length) + length) % length;
                var (p, angle) = Geometry.PathTools.At(pts, cum, s);
                double rad = angle * Math.PI / 180;
                var normal = new Vec2(-Math.Sin(rad), Math.Cos(rad));
                var place = Affine.Scale(scale, scale)
                    .Then(Affine.Translate(-w / 2, 0))
                    .Then(Affine.Rotate(angle))
                    .Then(Affine.Translate(p + normal * below));
                foreach (var sp in FromSkia(gp)) subpaths.Add(sp.Transform(place));
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
