using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using YTool.Core;

namespace YTool.Doc;

/// <summary>One drawable element from an SVG, already in millimetres.</summary>
public sealed record ImportedPath(PathGeometry Geometry, uint? Color, string Name);

public sealed record SvgImportResult(List<ImportedPath> Paths, List<string> Warnings)
{
    public Rect2 Bounds => Paths.Aggregate(Rect2.Empty, (r, p) => r.Union(p.Geometry.Bounds()));
}

public sealed record SvgImportOptions
{
    /// <summary>
    /// How many SVG pixels make an inch when the file gives no physical size.
    /// CSS (Inkscape, browsers, Affinity) uses 96; old Illustrator exports use 72.
    /// </summary>
    public double PxPerInch { get; init; } = 96;
}

/// <summary>
/// SVG → millimetre geometry. Supports path, rect, circle, ellipse, line,
/// polyline, polygon, nested groups/svg, use, transforms, viewBox and physical
/// units. Text, images, clips and masks are reported as warnings, not guessed at.
/// </summary>
public static partial class SvgImport
{
    static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public static SvgImportResult Load(string path, SvgImportOptions? options = null)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, options);
    }

    public static SvgImportResult Load(Stream stream, SvgImportOptions? options = null)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        return Parse(XDocument.Load(reader), options ?? new());
    }

    public static SvgImportResult ParseString(string svg, SvgImportOptions? options = null)
    {
        using var s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg));
        return Load(s, options);
    }

    sealed class Ctx(SvgImportOptions opt, Dictionary<string, XElement> ids)
    {
        public SvgImportOptions Opt { get; } = opt;
        public Dictionary<string, XElement> Ids { get; } = ids;
        public List<ImportedPath> Paths { get; } = [];
        public HashSet<string> Warnings { get; } = [];
        public int UseDepth;
    }

    readonly record struct Paint(uint? Stroke, uint? Fill, bool StrokeNone, bool FillNone);

    static SvgImportResult Parse(XDocument doc, SvgImportOptions opt)
    {
        var root = doc.Root ?? throw new FormatException("The file is empty.");
        if (root.Name.LocalName != "svg") throw new FormatException("This is not an SVG file.");
        var ids = new Dictionary<string, XElement>();
        foreach (var e in root.DescendantsAndSelf())
            if (e.Attribute("id")?.Value is { Length: > 0 } id) ids.TryAdd(id, e);
        var ctx = new Ctx(opt, ids);
        var rootT = RootTransform(root, opt, ctx);
        Walk(root, rootT, new Paint(null, 0x000000, false, false), ctx, isRoot: true);
        if (ctx.Paths.Count == 0 && ctx.Warnings.Count == 0) ctx.Warnings.Add("The file contains no shapes.");
        return new(ctx.Paths, [.. ctx.Warnings]);
    }

    /// <summary>User units → mm, from width/height and viewBox.</summary>
    static Affine RootTransform(XElement root, SvgImportOptions opt, Ctx ctx)
    {
        double pxMm = 25.4 / opt.PxPerInch;
        var vb = ParseViewBox(root.Attribute("viewBox")?.Value);
        double? wMm = LengthMm(root.Attribute("width")?.Value, pxMm);
        double? hMm = LengthMm(root.Attribute("height")?.Value, pxMm);
        if (vb is not { } v || v.W <= 0 || v.H <= 0) return Affine.Scale(pxMm, pxMm);
        double sx = wMm.HasValue ? wMm.Value / v.W : hMm.HasValue ? hMm.Value / v.H : pxMm;
        double sy = hMm.HasValue ? hMm.Value / v.H : sx;
        if (Math.Abs(sx - sy) > 1e-9 * Math.Max(sx, sy))
        {
            // preserveAspectRatio default (xMidYMid meet): uniform scale, centred.
            var par = root.Attribute("preserveAspectRatio")?.Value ?? "";
            if (!par.Contains("none"))
            {
                double s = Math.Min(sx, sy);
                double ox = ((wMm ?? v.W * s) - v.W * s) / 2, oy = ((hMm ?? v.H * s) - v.H * s) / 2;
                return Affine.Translate(-v.X, -v.Y).Then(Affine.Scale(s, s)).Then(Affine.Translate(ox, oy));
            }
            ctx.Warnings.Add("The drawing is stretched (preserveAspectRatio=\"none\").");
        }
        return Affine.Translate(-v.X, -v.Y).Then(Affine.Scale(sx, sy));
    }

    readonly record struct ViewBox(double X, double Y, double W, double H);

    static ViewBox? ParseViewBox(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var n = Numbers(s);
        return n.Count == 4 ? new ViewBox(n[0], n[1], n[2], n[3]) : null;
    }

    /// <summary>A root width/height in mm, or null for none/percent.</summary>
    static double? LengthMm(string? s, double pxMm)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = LengthRx().Match(s.Trim());
        if (!m.Success) return null;
        double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "mm" => v,
            "cm" => v * 10,
            "in" => v * 25.4,
            "pt" => v * 25.4 / 72,
            "pc" => v * 25.4 / 6,
            "px" or "" => v * pxMm,
            _ => null, // percentages and unknown units: size comes from the viewBox
        };
    }

    [GeneratedRegex(@"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z%]*)$")]
    private static partial Regex LengthRx();

    static readonly HashSet<string> Skip = ["defs", "clipPath", "mask", "symbol", "title", "desc", "metadata", "style",
        "script", "linearGradient", "radialGradient", "pattern", "filter", "marker", "namedview", "foreignObject"];

    static void Walk(XElement e, Affine parentT, Paint parentPaint, Ctx ctx, bool isRoot = false)
    {
        var name = e.Name.LocalName;
        if (!isRoot && Skip.Contains(name)) return;
        if (e.Name.Namespace != Svg && e.Name.Namespace != XNamespace.None) return; // sodipodi:, inkscape:, ...
        if (IsHidden(e)) return;

        var t = isRoot ? parentT : ParseTransform(e.Attribute("transform")?.Value).Then(parentT);
        var paint = InheritPaint(e, parentPaint);

        switch (name)
        {
            case "svg" when !isRoot:
            {
                double x = Len(e, "x"), y = Len(e, "y");
                var inner = Affine.Translate(x, y).Then(t);
                if (ParseViewBox(e.Attribute("viewBox")?.Value) is { W: > 0, H: > 0 } vb)
                {
                    double w = Len(e, "width", vb.W), h = Len(e, "height", vb.H);
                    double s = Math.Min(w / vb.W, h / vb.H);
                    inner = Affine.Translate(-vb.X, -vb.Y).Then(Affine.Scale(s, s)).Then(inner);
                }
                foreach (var c in e.Elements()) Walk(c, inner, paint, ctx);
                return;
            }
            case "svg":
            case "g":
            case "a":
            case "switch":
                foreach (var c in e.Elements()) Walk(c, t, paint, ctx);
                return;
            case "use":
            {
                var href = (e.Attribute(XLink + "href") ?? e.Attribute("href"))?.Value;
                if (href is null || !href.StartsWith('#') || !ctx.Ids.TryGetValue(href[1..], out var target)) return;
                if (ctx.UseDepth > 16) { ctx.Warnings.Add("Some <use> references loop and were skipped."); return; }
                var ut = Affine.Translate(Len(e, "x"), Len(e, "y")).Then(t);
                ctx.UseDepth++;
                if (target.Name.LocalName == "symbol")
                    foreach (var c in target.Elements()) Walk(c, ut, paint, ctx);
                else
                    Walk(target, ut, paint, ctx);
                ctx.UseDepth--;
                return;
            }
            case "text":
                ctx.Warnings.Add("Text was skipped. Convert text to paths (outlines) in your design app first.");
                return;
            case "image":
                ctx.Warnings.Add("Embedded images were skipped. Import images separately.");
                return;
        }

        var geom = name switch
        {
            "path" => PathGeometry.FromSvgPathData(e.Attribute("d")?.Value ?? ""),
            "rect" => RectGeom(e),
            "circle" => Len(e, "r") is var r && r > 0 ? PathGeometry.Ellipse(Len(e, "cx"), Len(e, "cy"), r, r) : null,
            "ellipse" => EllipseGeom(e),
            "line" => PathGeometry.Line(new(Len(e, "x1"), Len(e, "y1")), new(Len(e, "x2"), Len(e, "y2"))),
            "polyline" => PolyGeom(e, false),
            "polygon" => PolyGeom(e, true),
            _ => null,
        };
        if (geom is null || geom.IsEmpty) return;
        if (e.Attribute("clip-path") is not null || e.Attribute("mask") is not null)
            ctx.Warnings.Add("Clipping paths and masks are ignored; the full shapes were imported.");
        uint? color = !paint.StrokeNone && paint.Stroke is { } st ? st : !paint.FillNone ? paint.Fill : null;
        if (paint.StrokeNone && paint.FillNone)
        {
            ctx.Warnings.Add("Some shapes had neither stroke nor fill (invisible in the design) and were imported anyway.");
        }
        var label = e.Attribute("id")?.Value ?? name;
        ctx.Paths.Add(new ImportedPath(geom.Transform(t), color, label));
    }

    static PathGeometry? RectGeom(XElement e)
    {
        double w = Len(e, "width"), h = Len(e, "height");
        if (w <= 0 || h <= 0) return null;
        var rxA = e.Attribute("rx"); var ryA = e.Attribute("ry");
        double rx = rxA is not null ? Len(e, "rx") : ryA is not null ? Len(e, "ry") : 0;
        double ry = ryA is not null ? Len(e, "ry") : rx;
        return PathGeometry.RoundRect(Len(e, "x"), Len(e, "y"), w, h, rx, ry);
    }

    static PathGeometry? EllipseGeom(XElement e)
    {
        double rx = Len(e, "rx"), ry = Len(e, "ry");
        if (e.Attribute("rx") is null) rx = ry;
        if (e.Attribute("ry") is null) ry = rx;
        return rx > 0 && ry > 0 ? PathGeometry.Ellipse(Len(e, "cx"), Len(e, "cy"), rx, ry) : null;
    }

    static PathGeometry? PolyGeom(XElement e, bool closed)
    {
        var n = Numbers(e.Attribute("points")?.Value ?? "");
        var pts = new List<Vec2>();
        for (int i = 0; i + 1 < n.Count; i += 2) pts.Add(new(n[i], n[i + 1]));
        return pts.Count >= 2 ? PathGeometry.Polyline(pts, closed) : null;
    }

    static double Len(XElement e, string attr, double fallback = 0)
    {
        var s = e.Attribute(attr)?.Value;
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var m = LengthRx().Match(s.Trim());
        if (!m.Success) return fallback;
        double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        // Lengths inside the drawing are in user units; physical units are
        // converted at 96 px/in, which is what browsers do.
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "mm" => v * 96 / 25.4,
            "cm" => v * 96 / 2.54,
            "in" => v * 96,
            "pt" => v * 96 / 72,
            "pc" => v * 16,
            _ => v,
        };
    }

    static List<double> Numbers(string s)
    {
        var o = new List<double>();
        int i = 0;
        while (true)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
            if (i >= s.Length) break;
            try { o.Add(SvgPathParser.Num(s, ref i)); }
            catch (FormatException) { break; }
        }
        return o;
    }

    // ---- style --------------------------------------------------------------

    static string? Prop(XElement e, string name)
    {
        // Inline style wins over presentation attributes.
        if (e.Attribute("style")?.Value is { } style)
            foreach (var decl in style.Split(';'))
            {
                int c = decl.IndexOf(':');
                if (c > 0 && decl[..c].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return decl[(c + 1)..].Replace("!important", "").Trim();
            }
        return e.Attribute(name)?.Value.Trim();
    }

    static bool IsHidden(XElement e) =>
        Prop(e, "display") == "none" || Prop(e, "visibility") is "hidden" or "collapse" ||
        (double.TryParse(Prop(e, "opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var op) && op <= 0);

    static Paint InheritPaint(XElement e, Paint parent)
    {
        // Gradients/patterns (url(...)) and unknown colours are visible but
        // have no single colour; they fall back to black.
        var p = parent;
        if (Prop(e, "stroke") is { } s && s != "inherit")
            p = s == "none" ? p with { Stroke = null, StrokeNone = true }
                : p with { Stroke = ParseColor(s) ?? 0x000000, StrokeNone = false };
        if (Prop(e, "fill") is { } f && f != "inherit")
            p = f == "none" ? p with { Fill = null, FillNone = true }
                : p with { Fill = ParseColor(f) ?? 0x000000, FillNone = false };
        return p;
    }

    static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000, ["white"] = 0xFFFFFF, ["red"] = 0xFF0000, ["lime"] = 0x00FF00, ["green"] = 0x008000,
        ["blue"] = 0x0000FF, ["yellow"] = 0xFFFF00, ["cyan"] = 0x00FFFF, ["aqua"] = 0x00FFFF, ["magenta"] = 0xFF00FF,
        ["fuchsia"] = 0xFF00FF, ["gray"] = 0x808080, ["grey"] = 0x808080, ["silver"] = 0xC0C0C0, ["maroon"] = 0x800000,
        ["olive"] = 0x808000, ["purple"] = 0x800080, ["teal"] = 0x008080, ["navy"] = 0x000080, ["orange"] = 0xFFA500,
        ["pink"] = 0xFFC0CB, ["brown"] = 0xA52A2A, ["gold"] = 0xFFD700, ["violet"] = 0xEE82EE, ["indigo"] = 0x4B0082,
    };

    public static uint? ParseColor(string s)
    {
        s = s.Trim();
        if (s.StartsWith('#'))
        {
            var h = s[1..];
            if (h.Length is 3 or 4) h = string.Concat(h[..3].Select(c => $"{c}{c}"));
            if (h.Length >= 6 && uint.TryParse(h[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }
        if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            int a = s.IndexOf('('), b = s.IndexOf(')');
            if (a < 0 || b < a) return null;
            var parts = s[(a + 1)..b].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;
            uint Ch(string p) => p.EndsWith('%')
                ? (uint)Math.Clamp(Math.Round(double.Parse(p[..^1], CultureInfo.InvariantCulture) * 2.55), 0, 255)
                : (uint)Math.Clamp(Math.Round(double.Parse(p, CultureInfo.InvariantCulture)), 0, 255);
            try { return Ch(parts[0]) << 16 | Ch(parts[1]) << 8 | Ch(parts[2]); }
            catch (FormatException) { return null; }
        }
        return Named.TryGetValue(s, out var n) ? n : null;
    }

    // ---- transforms ---------------------------------------------------------

    [GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)")]
    private static partial Regex TransformRx();

    public static Affine ParseTransform(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Affine.Identity;
        // "a b c" means a then b then c applied right-to-left to the point, i.e.
        // the left-most is the outer transform.
        var result = Affine.Identity;
        foreach (Match m in TransformRx().Matches(s))
        {
            var n = Numbers(m.Groups[2].Value);
            double N(int i, double d = 0) => i < n.Count ? n[i] : d;
            var t = m.Groups[1].Value switch
            {
                "matrix" when n.Count == 6 => new Affine(n[0], n[1], n[2], n[3], n[4], n[5]),
                "translate" => Affine.Translate(N(0), N(1)),
                "scale" => Affine.Scale(N(0, 1), N(1, N(0, 1))),
                "rotate" => n.Count >= 3 ? Affine.RotateAbout(new(n[1], n[2]), n[0]) : Affine.Rotate(N(0)),
                "skewX" => Affine.SkewX(N(0)),
                "skewY" => Affine.SkewY(N(0)),
                _ => Affine.Identity,
            };
            result = t.Then(result);
        }
        return result;
    }
}
