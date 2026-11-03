using System.Globalization;
using System.Text.Json;
using Clipper2Lib;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The handwriting fonts made with the Handwriting to font add-on (Home → Draw → Handwriting), read
/// straight from its data folder: single-line fonts, so numbers written in them are scored, one pass
/// of the beam down each pen stroke. Nothing here needs that add-on to be on.
/// </summary>
public static class HandFonts
{
    /// <summary>Put before a handwriting font's name in the font list and in <see cref="Markings.Font"/>.</summary>
    public const string Prefix = "✎ ";

    /// <summary>One written version of a character: capital height 1, baseline at 0, y down; its width.</summary>
    public sealed record Glyph(double Advance, List<(List<PointD> Points, bool Closed)> Strokes);

    public sealed record Font(string Name, Dictionary<string, List<Glyph>> Glyphs);

    static readonly Lock Gate = new();
    static string? _folder;
    static List<Font> _fonts = [];
    static DateTime _read;

    /// <summary>Where the handwriting fonts are (set by the window: the Handwriting add-on's "fonts" folder).</summary>
    public static string? Folder
    {
        get { lock (Gate) return _folder; }
        set { lock (Gate) { _folder = value; _read = default; } }
    }

    /// <summary>The handwriting add-on's fonts folder, next to this add-on's data folder.</summary>
    public static string? FolderBeside(string? dataFolder) =>
        string.IsNullOrEmpty(dataFolder) || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataFolder)) is not { } root ? null : Path.Combine(root, "handwriting", "fonts");

    public static IReadOnlyList<Font> All()
    {
        lock (Gate)
        {
            if (_folder is null) return _fonts = [];
            DateTime stamp;
            try { stamp = Directory.Exists(_folder) ? Directory.GetLastWriteTimeUtc(_folder) : default; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { stamp = default; }
            if (stamp == _read && _read != default) return _fonts;
            _read = stamp;
            var list = new List<Font>();
            try
            {
                if (Directory.Exists(_folder))
                    foreach (var f in Directory.GetFiles(_folder, "*.json"))
                        if (Parse(File.ReadAllText(f)) is { } font && list.All(x => !string.Equals(x.Name, font.Name, StringComparison.CurrentCultureIgnoreCase)))
                            list.Add(font);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return _fonts = [.. list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
    }

    public static Font? Find(string name) => All().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>A handwriting font file (the Handwriting add-on's own format), or null if it isn't one.</summary>
    public static Font? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kind) || kind.GetString() != "photoncanvas-handwriting-font") return null;
            string name = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : "Handwriting";
            var glyphs = new Dictionary<string, List<Glyph>>(StringComparer.Ordinal);
            if (root.TryGetProperty("glyphs", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var g in arr.EnumerateArray())
                {
                    if (!g.TryGetProperty("char", out var c) || c.GetString() is not { Length: > 0 } ch) continue;
                    var strokes = new List<(List<PointD>, bool)>();
                    if (g.TryGetProperty("strokes", out var ss) && ss.ValueKind == JsonValueKind.Array)
                        foreach (var st in ss.EnumerateArray())
                        {
                            var pts = new List<PointD>();
                            if (st.TryGetProperty("points", out var ps) && ps.ValueKind == JsonValueKind.Array)
                            {
                                var nums = ps.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0).ToArray();
                                for (int i = 0; i + 1 < nums.Length; i += 2) pts.Add(new PointD(nums[i], nums[i + 1]));
                            }
                            bool closed = st.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
                            if (pts.Count >= 2) strokes.Add((pts, closed));
                        }
                    if (strokes.Count == 0) continue;
                    double adv = g.TryGetProperty("advance", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : strokes.SelectMany(x => x.Item1).Max(p => p.x);
                    if (!glyphs.TryGetValue(ch, out var list)) glyphs[ch] = list = [];
                    list.Add(new Glyph(adv, strokes));
                }
            return glyphs.Count == 0 ? null : new Font(name, glyphs);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>
    /// The text written in the font as lines (y up, capital height 1, starting at x = 0 on the baseline),
    /// smoothed; characters it hasn't got are left as a gap.
    /// </summary>
    public static List<PathD> Write(Font font, string text)
    {
        var o = new List<PathD>();
        double x = 0;
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            string ch = e.GetTextElement();
            if (string.IsNullOrWhiteSpace(ch)) { x += 0.45; continue; }
            var versions = font.Glyphs.TryGetValue(ch, out var v) ? v
                : font.Glyphs.TryGetValue(ch.ToUpperInvariant(), out var up) ? up
                : font.Glyphs.TryGetValue(ch.ToLowerInvariant(), out var lo) ? lo
                : ch == "\"" && font.Glyphs.TryGetValue("'", out var q) ? q : null;
            if (versions is null) { x += 0.45; continue; }
            // The same letter twice in a row takes its next version, so it looks written.
            int k = used.TryGetValue(ch, out var c) ? c : 0;
            used[ch] = k + 1;
            var g = versions[k % versions.Count];
            foreach (var (pts, closed) in g.Strokes)
            {
                var line = Smooth(pts, closed);
                o.Add([.. line.Select(p => new PointD(p.x + x, -p.y))]);
            }
            x += g.Advance + 0.12;
        }
        return o;
    }

    /// <summary>A stroke through its points, rounded (Catmull-Rom).</summary>
    static PathD Smooth(List<PointD> p, bool closed)
    {
        var pts = new List<PointD>(p);
        if (closed && pts.Count > 2 && Math.Abs(pts[0].x - pts[^1].x) + Math.Abs(pts[0].y - pts[^1].y) > 1e-9) pts.Add(pts[0]);
        if (pts.Count < 3) return [.. pts];
        var o = new PathD { pts[0] };
        int n = pts.Count;
        PointD At(int i) => closed ? pts[((i % (n - 1)) + (n - 1)) % (n - 1)] : pts[Math.Clamp(i, 0, n - 1)];
        for (int i = 0; i + 1 < n; i++)
        {
            PointD p0 = At(i - 1), p1 = pts[i], p2 = pts[i + 1], p3 = At(i + 2);
            for (int s = 1; s <= 4; s++)
            {
                double t = s / 4.0, t2 = t * t, t3 = t2 * t;
                o.Add(new PointD(
                    0.5 * (2 * p1.x + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
                    0.5 * (2 * p1.y + (-p0.y + p2.y) * t + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2 + (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3)));
            }
        }
        return o;
    }
}

/// <summary>Engraved marks for one piece: text, ornaments and designs, burned the way <see cref="MarkLook"/> says.</summary>
public sealed class Ink(Markings marks, string sheetFont, Maker? maker = null)
{
    public Markings Marks { get; } = marks;
    /// <summary>Numbers, titles and ornaments to engrave (areas), and lines to score.</summary>
    public PathsD Areas { get; } = [];
    public List<PathD> Lines { get; } = [];
    /// <summary>Borders (kept apart: lines aren't cleared round them).</summary>
    public PathsD BorderAreas { get; } = [];
    public List<PathD> BorderLines { get; } = [];

    /// <summary>The font actually used: the marks' own, else the sheet's.</summary>
    public string FontName => string.IsNullOrWhiteSpace(Marks.Font) ? sheetFont : Marks.Font;

    HandFonts.Font? Hand
    {
        get
        {
            if (!FontName.StartsWith(HandFonts.Prefix, StringComparison.Ordinal)) return null;
            string name = FontName[HandFonts.Prefix.Length..];
            var f = HandFonts.Find(name);
            if (f is null && maker is not null && !maker.Findings.Any(x => x.Text.Contains(name, StringComparison.Ordinal)))
                maker.Warn($"The handwriting font \"{name}\" isn't on this computer (make it with Home → Draw → Handwriting): the numbers are in {(string.IsNullOrWhiteSpace(sheetFont) ? "Arial" : sheetFont)} instead.");
            return f;
        }
    }

    string SystemFont => FontName.StartsWith(HandFonts.Prefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(FontName) ? (string.IsNullOrWhiteSpace(sheetFont) || sheetFont.StartsWith(HandFonts.Prefix, StringComparison.Ordinal) ? "Arial" : sheetFont) : FontName;

    /// <summary>
    /// The text laid out flat (y up) as areas and lines, its box's bottom-left at (0, 0) and its height
    /// <paramref name="h"/> (capitals): not yet placed.
    /// </summary>
    public (PathsD Areas, List<PathD> Lines) Shape(string text, double h)
    {
        text = text.Trim();
        if (text.Length == 0 || h < 0.3) return ([], []);
        if (Hand is { } hand)
        {
            var lines = HandFonts.Write(hand, text);
            if (lines.Count == 0) return ([], []);
            var all = new PathsD(lines);
            var b = Geo.Bounds(all);
            double k = h / Math.Max(1e-9, b.bottom - b.top);
            var placed = Geo.Map(lines, q => new PointD((q.x - b.left) * k, (q.y - b.top) * k));
            if (Marks.Look == MarkLook.Engraved)
            {
                // Engraved: the pen strokes drawn thick, a tenth of the letters' height.
                double w = Math.Clamp(h * 0.1, 0.2, 1.2);
                var fat = Geo.Tidy(Clipper.InflatePaths(new PathsD(placed), w / 2, JoinType.Round, EndType.Round, 2, 4));
                return (Geo.Move(fat, w / 2, w / 2), []);
            }
            return ([], placed);
        }
        PathGeometry g;
        try
        {
            g = TextLayout.Build(text, new TextStyle(SystemFont, h, Bold: Marks.Bold, Italic: Marks.Italic, Align: TextAlign.Left, SingleLine: Marks.Look == MarkLook.SingleLine, FitHeightMm: h));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return ([], []); }
        if (g.IsEmpty) return ([], []);
        var (areas, open) = Geo.FromGeometry(g, 0.01);
        var bb = Geo.Bounds(new PathsD([.. areas, .. open]));
        PointD Shift(PointD q) => new(q.x - bb.left, q.y - bb.top);
        if (Marks.Look == MarkLook.SingleLine) return ([], Geo.Map(open.Count > 0 ? open : [.. areas.Select(Geo.Loop)], Shift));
        return (Geo.Tidy(Geo.Map(areas, Shift)), []);
    }

    /// <summary>The text as big as fits in <paramref name="maxW"/> × <paramref name="maxH"/>, centred on (cx, cy), turned <paramref name="deg"/>.</summary>
    public void Text(string text, double cx, double cy, double maxW, double maxH, double deg = 0)
    {
        var (a, l) = Fitted(text, maxW, maxH);
        Put(a, l, cx, cy, deg, Areas, Lines);
    }

    /// <summary>The text's natural width at height <paramref name="h"/>.</summary>
    public double Width(string text, double h)
    {
        var (a, l) = Shape(text, h);
        var all = new PathsD([.. a, .. l]);
        if (all.Count == 0) return 0;
        var b = Geo.Bounds(all);
        return b.right - b.left;
    }

    /// <summary>The text as big as fits, centred on (0, 0).</summary>
    public (PathsD Areas, List<PathD> Lines) Fitted(string text, double maxW, double maxH)
    {
        if (maxW < 0.5 || maxH < 0.5) return ([], []);
        var (a, l) = Shape(text, maxH);
        var all = new PathsD([.. a, .. l]);
        if (all.Count == 0) return ([], []);
        var b = Geo.Bounds(all);
        double w = b.right - b.left, hh = b.bottom - b.top, k = Math.Min(1, maxW / Math.Max(1e-9, w));
        if (k < 1)
        {
            // Too wide: made again smaller, so the stroke width suits the size.
            (a, l) = Shape(text, maxH * k);
            all = new PathsD([.. a, .. l]);
            if (all.Count == 0) return ([], []);
            b = Geo.Bounds(all);
            w = b.right - b.left;
            hh = b.bottom - b.top;
            if (w > maxW + 1e-6)
            {
                double k2 = maxW / w;
                a = Geo.Map(a, q => new PointD(q.x * k2, q.y * k2));
                l = Geo.Map(l, q => new PointD(q.x * k2, q.y * k2));
                b = Geo.Bounds(new PathsD([.. a, .. l]));
            }
        }
        double mx = (b.left + b.right) / 2, my = (b.top + b.bottom) / 2;
        return (Geo.Map(a, q => new PointD(q.x - mx, q.y - my)), Geo.Map(l, q => new PointD(q.x - mx, q.y - my)));
    }

    /// <summary>An ornament (built-in or your design) as big as fits in a <paramref name="w"/> × <paramref name="h"/> box centred on (cx, cy).</summary>
    public void Ornament(double cx, double cy, double w, double h, bool pointLeft = false, double deg = 0)
    {
        var (a, l) = OrnamentShape(w, h, pointLeft);
        Put(a, l, cx, cy, deg, Areas, Lines);
    }

    /// <summary>The ornament, if any, centred on (0, 0).</summary>
    public (PathsD Areas, List<PathD> Lines) OrnamentShape(double w, double h, bool pointLeft = false)
    {
        if (string.IsNullOrEmpty(Marks.Ornament) || w < 0.5 || h < 0.5) return ([], []);
        PathsD unit;
        List<PathD> unitLines = [];
        bool pointing = false;
        if (Marks.Ornament == Ornaments.ArtName)
        {
            if (Marks.Art is not { } art) return ([], []);
            (unit, unitLines) = MarkArts.Unit(art);
        }
        else
        {
            unit = Ornaments.Shape(Marks.Ornament);
            pointing = Ornaments.Find(Marks.Ornament)?.Pointing ?? false;
        }
        if (unit.Count == 0 && unitLines.Count == 0) return ([], []);
        var all = new PathsD([.. unit, .. unitLines]);
        var b = Geo.Bounds(all);
        double uw = b.right - b.left, uh = b.bottom - b.top, k = Math.Min(w / Math.Max(1e-9, uw), h / Math.Max(1e-9, uh));
        double mx = (b.left + b.right) / 2, my = (b.top + b.bottom) / 2;
        bool flip = pointing && pointLeft;
        PointD F(PointD q) => new((flip ? -(q.x - mx) : q.x - mx) * k, (q.y - my) * k);
        var areas = Geo.Map(unit, F);
        if (flip) areas = Geo.Tidy(areas);
        return (areas, Geo.Map(unitLines, F));
    }

    /// <summary>How wide the ornament comes out at height <paramref name="h"/> (its own shape's width over height).</summary>
    public double OrnamentWidth(double h)
    {
        if (string.IsNullOrEmpty(Marks.Ornament)) return 0;
        if (Marks.Ornament == Ornaments.ArtName) return Marks.Art is { } art ? h * Math.Clamp(art.Aspect, 0.3, 3) : 0;
        var s = Ornaments.Shape(Marks.Ornament);
        if (s.Count == 0) return 0;
        var b = Geo.Bounds(s);
        return h * Math.Clamp((b.right - b.left) / Math.Max(1e-9, b.bottom - b.top), 0.3, 3);
    }

    public bool HasOrnament => !string.IsNullOrEmpty(Marks.Ornament) && (Marks.Ornament != Ornaments.ArtName || Marks.Art is not null);

    /// <summary>Puts shapes centred on (0, 0) at (cx, cy), turned.</summary>
    public void Put(PathsD a, List<PathD> l, double cx, double cy, double deg, PathsD intoAreas, List<PathD> intoLines)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        PointD F(PointD q) => new(cx + q.x * c - q.y * s, cy + q.x * s + q.y * c);
        intoAreas.AddRange(Geo.Map(a, F));
        intoLines.AddRange(Geo.Map(l, F));
    }

    /// <summary>The border round a face.</summary>
    public void Border(PathsD face, double inset, double band, int seed)
    {
        if (Marks.Border == BorderStyle.None) return;
        var (a, l) = Borders.Make(face, inset, band, Marks.Border, Marks.BorderArt, seed);
        BorderAreas.AddRange(a);
        BorderLines.AddRange(l);
    }

    /// <summary>
    /// Everything onto the piece: areas engraved (or, outlined, scored round), lines scored. Other lines on the
    /// piece (<paramref name="clearOf"/>: ticks, rings, grid) are stopped short of the numbers and ornaments.
    /// </summary>
    public void Into(Plate p, List<PathD>? clearOf = null, PathsD? keepOut = null)
    {
        var areas = Geo.Union(Areas);
        var border = Geo.Union(BorderAreas);
        if (keepOut is { Count: > 0 })
        {
            areas = Geo.Subtract(areas, keepOut);
            border = Geo.Subtract(border, keepOut);
        }
        if (clearOf is { Count: > 0 } && areas.Count > 0)
        {
            var clear = Geo.Offset(areas, 0.6, true);
            var b = Geo.Bounds(new PathsD([.. clearOf, .. clear]));
            var outside = Geo.Subtract([Geo.Rect(b.left - 1, b.top - 1, b.right + 1, b.bottom + 1)], clear);
            var kept = Geo.ClipLines(clearOf, outside);
            clearOf.Clear();
            clearOf.AddRange(kept);
        }
        if (Marks.Look == MarkLook.Engraved)
        {
            p.Etched.AddRange(Geo.Union(areas, border));
        }
        else
        {
            foreach (var q in Geo.Tidy(Geo.Union(areas, border))) p.Lines.Add(Geo.Loop(q));
        }
        var lines = keepOut is { Count: > 0 } ? Geo.ClipLines([.. Lines, .. BorderLines], Geo.Subtract([Geo.Rect(-1e5, -1e5, 1e5, 1e5)], keepOut)) : [.. Lines, .. BorderLines];
        p.Lines.AddRange(lines);
    }

    /// <summary>A number of dots like a die's face (1 to 9) in an <paramref name="s"/> square centred on (0, 0).</summary>
    public static PathsD Pips(int n, double s)
    {
        double g = s * 0.34, r = s * 0.12;
        int[][] at =
        [
            [], [4], [0, 8], [0, 4, 8], [0, 2, 6, 8], [0, 2, 4, 6, 8], [0, 2, 3, 5, 6, 8], [0, 2, 3, 4, 5, 6, 8], [0, 1, 2, 3, 5, 6, 7, 8], [0, 1, 2, 3, 4, 5, 6, 7, 8],
        ];
        var o = new PathsD();
        foreach (var i in at[Math.Clamp(n, 0, 9)]) o.Add(Geo.Circle((i % 3 - 1) * g, (1 - i / 3) * g, r, 0.01));
        return o;
    }

    /// <summary>Tally marks (groups of five, the fifth across the four) <paramref name="h"/> high, centred on (0, 0): as lines.</summary>
    public static List<PathD> Tally(int n, double h, out double width)
    {
        var o = new List<PathD>();
        double step = h * 0.22, groupGap = h * 0.34, x = 0;
        int left = n;
        while (left > 0)
        {
            int k = Math.Min(4, left);
            double x0 = x;
            for (int i = 0; i < k; i++) { o.Add([new(x, -h / 2), new(x + h * 0.04, h / 2)]); x += step; }
            left -= k;
            if (left > 0 && k == 4) { o.Add([new(x0 - step * 0.4, -h * 0.32), new(x - step * 0.6 + step * 0.4, h * 0.32)]); left--; }
            x += groupGap - step;
        }
        width = Math.Max(0, x - groupGap + step * 0.2);
        double mid = width / 2;
        return Geo.Map(o, q => new PointD(q.x - mid, q.y));
    }
}
