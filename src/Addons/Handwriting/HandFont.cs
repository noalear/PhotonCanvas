using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Handwriting;

/// <summary>One pen stroke of a letter, in font units (see <see cref="HandGlyph"/>).</summary>
public sealed record HandStroke(IReadOnlyList<Vec2> Points, bool Closed);

/// <summary>
/// One written version of a character. Font units: capital height = 1, the baseline at y = 0 and
/// y down (so capitals reach y = −1), the ink's left edge at x = 0. <paramref name="Advance"/> is
/// the ink's width; letter spacing is added when writing.
/// </summary>
public sealed record HandGlyph(double Advance, IReadOnlyList<HandStroke> Strokes)
{
    public Rect2 Bounds => Rect2.Bounds(Strokes.SelectMany(s => s.Points));
}

/// <summary>
/// A single-line font made from someone's handwriting: for each character one or more versions
/// (more versions make words look less mechanical: the same letter twice in a row differs).
/// Saved as a small JSON file.
/// </summary>
public sealed class HandFont
{
    public const int FormatVersion = 1;

    public required string Name { get; init; }
    /// <summary>Character (as a string, so any letter fits) → its written versions.</summary>
    public Dictionary<string, List<HandGlyph>> Glyphs { get; init; } = new(StringComparer.Ordinal);

    public int Count => Glyphs.Values.Sum(v => v.Count);

    public IReadOnlyList<HandGlyph>? Versions(string ch) => Glyphs.TryGetValue(ch, out var v) && v.Count > 0 ? v : null;

    /// <summary>This font with <paramref name="more"/> added: as extra versions, or replacing the letters it has.</summary>
    public HandFont With(IEnumerable<(string Char, HandGlyph Glyph)> more, bool asExtraVersions)
    {
        var g = Glyphs.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (ch, glyph) in more)
        {
            if (!asExtraVersions && replaced.Add(ch)) g[ch] = [];
            if (!g.TryGetValue(ch, out var list)) g[ch] = list = [];
            list.Add(glyph);
        }
        return new HandFont { Name = Name, Glyphs = g };
    }

    public HandFont Renamed(string name) => new() { Name = name, Glyphs = Glyphs };

    // ---- File ----

    public string ToJson()
    {
        var glyphs = new JsonArray();
        foreach (var (ch, versions) in Glyphs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            foreach (var v in versions)
                glyphs.Add(new JsonObject
                {
                    ["char"] = ch,
                    ["advance"] = Math.Round(v.Advance, 4),
                    ["strokes"] = new JsonArray([.. v.Strokes.Select(s => (JsonNode)new JsonObject
                    {
                        ["closed"] = s.Closed,
                        ["points"] = new JsonArray([.. s.Points.SelectMany(p => new[] { p.X, p.Y }).Select(d => (JsonNode)JsonValue.Create(Math.Round(d, 4)))]),
                    })]),
                });
        var root = new JsonObject
        {
            ["format"] = FormatVersion,
            ["kind"] = "photoncanvas-handwriting-font",
            ["name"] = Name,
            ["glyphs"] = glyphs,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <exception cref="FormatException">Not a handwriting font file.</exception>
    public static HandFont FromJson(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException e) { throw new FormatException("This isn't a handwriting font file.", e); }
        if (root?["kind"]?.GetValue<string>() != "photoncanvas-handwriting-font") throw new FormatException("This isn't a handwriting font file.");
        if ((root["format"]?.GetValue<int>() ?? 0) > FormatVersion) throw new FormatException("This handwriting font was made by a newer version of the add-on.");
        var glyphs = new Dictionary<string, List<HandGlyph>>(StringComparer.Ordinal);
        foreach (var g in root["glyphs"]?.AsArray() ?? [])
        {
            if (g?["char"]?.GetValue<string>() is not { Length: > 0 } ch) continue;
            var strokes = new List<HandStroke>();
            foreach (var s in g["strokes"]?.AsArray() ?? [])
            {
                var nums = s?["points"]?.AsArray().Select(n => n?.GetValue<double>() ?? 0).ToArray() ?? [];
                var pts = new List<Vec2>();
                for (int i = 0; i + 1 < nums.Length; i += 2) pts.Add(new Vec2(nums[i], nums[i + 1]));
                if (pts.Count >= 2) strokes.Add(new HandStroke(pts, s!["closed"]?.GetValue<bool>() ?? false));
            }
            if (strokes.Count == 0) continue;
            if (!glyphs.TryGetValue(ch, out var list)) glyphs[ch] = list = [];
            list.Add(new HandGlyph(g["advance"]?.GetValue<double>() ?? Rect2.Bounds(strokes.SelectMany(x => x.Points)).Width, strokes));
        }
        return new HandFont { Name = root["name"]?.GetValue<string>() is { Length: > 0 } n ? n : "Handwriting", Glyphs = glyphs };
    }

    /// <summary>
    /// The font as an SVG font: a stroke font other programs read (Inkscape's Hershey Text extension
    /// takes it as a custom font). The first version of each character; 1000 units per em, capitals 700 tall.
    /// </summary>
    public string ToSvgFont(double letterSpacing = 0.1, double wordSpacing = 0.45)
    {
        const double U = 700; // units per capital height
        XNamespace svg = "http://www.w3.org/2000/svg";
        var all = Glyphs.Values.SelectMany(v => v).ToList();
        double top = all.Count == 0 ? -1 : Math.Min(-1, all.Min(g => g.Bounds.MinY));
        double bottom = all.Count == 0 ? 0.3 : Math.Max(0.3, all.Max(g => g.Bounds.MaxY));
        string id = new string([.. Name.Where(char.IsLetterOrDigit)]) is { Length: > 0 } s ? s : "Handwriting";
        var font = new XElement(svg + "font",
            new XAttribute("id", id),
            new XAttribute("horiz-adv-x", F(wordSpacing * U)),
            new XElement(svg + "font-face",
                new XAttribute("font-family", Name),
                new XAttribute("units-per-em", "1000"),
                new XAttribute("ascent", F(-top * U)),
                new XAttribute("descent", F(-bottom * U)),
                new XAttribute("cap-height", F(U)),
                new XAttribute("x-height", F(0.6 * U))),
            new XElement(svg + "missing-glyph", new XAttribute("horiz-adv-x", F(wordSpacing * U))),
            new XElement(svg + "glyph", new XAttribute("unicode", " "), new XAttribute("glyph-name", "space"), new XAttribute("horiz-adv-x", F(wordSpacing * U))));
        foreach (var (ch, versions) in Glyphs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var g = versions[0];
            var flip = Affine.Scale(U, -U);
            var d = PathData(new PathGeometry(g.Strokes.Select(HandLayout.Smooth)).Transform(flip));
            font.Add(new XElement(svg + "glyph",
                new XAttribute("unicode", ch),
                new XAttribute("glyph-name", GlyphName(ch)),
                new XAttribute("horiz-adv-x", F((g.Advance + letterSpacing) * U)),
                new XAttribute("d", d)));
        }
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(svg + "svg", new XAttribute("version", "1.1"),
                new XElement(svg + "metadata", $"{Name}: a single-line handwriting font made with PhotonCanvas."),
                new XElement(svg + "defs", font)));
        var sb = new StringBuilder();
        using (var w = new Utf8StringWriter(sb)) doc.Save(w);
        return sb.ToString();
    }

    static string GlyphName(string ch) => ch.Length == 1 && char.IsAsciiLetterOrDigit(ch[0]) ? ch : "uni" + string.Concat(ch.Select(c => ((int)c).ToString("X4")));

    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>SVG path data ("M … L … C … Z").</summary>
    public static string PathData(PathGeometry g)
    {
        var sb = new StringBuilder();
        string P(Vec2 p) => $"{F(p.X)} {F(p.Y)}";
        foreach (var sp in g.Subpaths)
        {
            if (sp.Segs.IsEmpty) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("M ").Append(P(sp.Start));
            foreach (var s in sp.Segs)
                sb.Append(s.Kind == SegKind.Line ? $" L {P(s.To)}" : $" C {P(s.C1)} {P(s.C2)} {P(s.To)}");
            if (sp.Closed) sb.Append(" Z");
        }
        return sb.ToString();
    }

    sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}

/// <summary>The handwriting fonts kept in the add-on's data folder, one JSON file each.</summary>
public sealed class FontStore(string folder)
{
    public string Folder { get; } = folder;

    public List<HandFont> All()
    {
        var o = new List<HandFont>();
        if (!Directory.Exists(Folder)) return o;
        foreach (var f in Directory.GetFiles(Folder, "*.json"))
        {
            try { o.Add(HandFont.FromJson(File.ReadAllText(f))); }
            catch (Exception e) when (e is FormatException or IOException or JsonException or InvalidOperationException) { }
        }
        return [.. o.GroupBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public HandFont? Find(string name) => All().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public string PathFor(string name) => Path.Combine(Folder, SafeFileName(name) + ".json");

    public void Save(HandFont font)
    {
        Directory.CreateDirectory(Folder);
        var path = PathFor(font.Name);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, font.ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string([.. name.Trim().Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim('.', ' ');
        return s.Length == 0 ? "Handwriting" : s.Length > 80 ? s[..80] : s;
    }
}

/// <summary>How words are written with a handwriting font. Sizes in mm; spacings as a share of the capital height.</summary>
public sealed record HandStyle
{
    public string Font { get; init; } = "";
    /// <summary>How tall capital letters are (mm).</summary>
    public double SizeMm { get; init; } = 10;
    public double LetterSpacing { get; init; } = 0.12;
    public double WordSpacing { get; init; } = 0.5;
    /// <summary>Baseline to baseline, in capital heights.</summary>
    public double LineSpacing { get; init; } = 1.9;
    /// <summary>Lean (degrees): + leans right, − left.</summary>
    public double SlantDeg { get; init; }
    public TextAlign Align { get; init; } = TextAlign.Left;
    /// <summary>0 = every letter exactly as written on the sheet; 1 = lots of wobble in size, angle and height.</summary>
    public double Variation { get; init; } = 0.3;
    /// <summary>Which of the possible variations (a new number gives a different but repeatable result).</summary>
    public int Seed { get; init; } = 1;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static HandStyle? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<HandStyle>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>What writing some words came to.</summary>
/// <param name="Missing">Characters the font doesn't have (left as a gap).</param>
/// <param name="StandIns">Characters written with another one (é as e, “ as ").</param>
public sealed record HandWriting(PathGeometry Geometry, IReadOnlyList<string> Missing, IReadOnlyList<string> StandIns, int Letters);

/// <summary>Writes words with a <see cref="HandFont"/>: single lines in mm, top-left at (0, 0).</summary>
public static class HandLayout
{
    public static HandWriting Write(HandFont font, string text, HandStyle style)
    {
        var missing = new List<string>();
        var standIns = new List<string>();
        var subpaths = new List<Subpath>();
        double k = Math.Max(0.1, style.SizeMm);
        double v = Math.Clamp(style.Variation, 0, 1);
        var lines = Normalise(text).Split('\n');
        var lastVersion = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var placed = new List<(List<Subpath> Paths, double Width)>();
        int letters = 0, position = 0;
        var skew = Affine.SkewX(-style.SlantDeg);
        foreach (var line in lines)
        {
            var paths = new List<Subpath>();
            double x = 0, end = 0;
            var e = StringInfo.GetTextElementEnumerator(line);
            while (e.MoveNext())
            {
                string ch = e.GetTextElement();
                position++;
                if (ch == " " || ch == "\t" || ch == " ")
                {
                    x += style.WordSpacing * (ch == "\t" ? 4 : 1);
                    continue;
                }
                var (glyphs, used) = Find(font, ch);
                if (glyphs is null)
                {
                    if (!missing.Contains(ch)) missing.Add(ch);
                    x += style.WordSpacing;
                    continue;
                }
                if (used != ch && !standIns.Contains(ch)) standIns.Add(ch);
                int n = seen.TryGetValue(used, out var c) ? c : 0;
                seen[used] = n + 1;
                int pick = glyphs.Count == 1 ? 0 : (int)(Hash(style.Seed, used.Aggregate(17, (h, c) => h * 31 + c), n) % (uint)glyphs.Count);
                if (glyphs.Count > 1 && lastVersion.TryGetValue(used, out var last) && last == pick) pick = (pick + 1) % glyphs.Count;
                lastVersion[used] = pick;
                var g = glyphs[pick];
                // A little wobble, the same every time for the same seed and text.
                double scale = 1 + 0.06 * v * Rand(style.Seed, position, 1);
                double turn = 3.0 * v * Rand(style.Seed, position, 2);
                double lift = 0.035 * v * Rand(style.Seed, position, 3);
                var place = Affine.Rotate(turn)
                    .Then(Affine.Scale(scale, scale))
                    .Then(Affine.Translate(0, lift))
                    .Then(skew)
                    .Then(Affine.Translate(x, 0));
                foreach (var s in g.Strokes) paths.Add(Smooth(s).Transform(place));
                end = x + g.Advance * scale;
                x = end + style.LetterSpacing + 0.04 * v * Rand(style.Seed, position, 4);
                letters++;
            }
            placed.Add((paths, end));
        }
        double block = placed.Count == 0 ? 0 : placed.Max(p => p.Width);
        for (int li = 0; li < placed.Count; li++)
        {
            var (paths, width) = placed[li];
            double shift = style.Align switch
            {
                TextAlign.Center => (block - width) / 2,
                TextAlign.Right => block - width,
                _ => 0,
            };
            var toMm = Affine.Translate(shift, 1 + li * style.LineSpacing).Then(Affine.Scale(k, k));
            subpaths.AddRange(paths.Select(p => p.Transform(toMm)));
        }
        var geo = new PathGeometry(subpaths);
        // Top-left of the ink at (0, 0), so the shape's position is where the writing starts.
        if (!geo.IsEmpty)
        {
            var b = geo.Bounds();
            if (!b.IsEmpty) geo = geo.Transform(Affine.Translate(-Math.Min(0, b.MinX), -b.MinY));
        }
        return new HandWriting(geo, missing, standIns, letters);
    }

    static string Normalise(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("…", "...");

    static readonly Dictionary<string, string> Similar = new(StringComparer.Ordinal)
    {
        ["‘"] = "'", ["’"] = "'", ["‚"] = ",", ["“"] = "\"", ["”"] = "\"", ["„"] = "\"", ["–"] = "-", ["—"] = "-", ["−"] = "-",
        ["×"] = "x", ["·"] = ".", ["«"] = "\"", ["»"] = "\"",
    };

    /// <summary>Accented letters → the letter without its accent (the program runs without culture data, so a table).</summary>
    static readonly Dictionary<char, char> Bare = BuildBare();

    static Dictionary<char, char> BuildBare()
    {
        var d = new Dictionary<char, char>();
        void Add(string from, char to) { foreach (var c in from) d[c] = to; }
        Add("àáâãäåāăą", 'a'); Add("ÀÁÂÃÄÅĀĂĄ", 'A'); Add("çćĉċč", 'c'); Add("ÇĆĈĊČ", 'C'); Add("ďđ", 'd'); Add("ĎĐ", 'D');
        Add("èéêëēĕėęě", 'e'); Add("ÈÉÊËĒĔĖĘĚ", 'E'); Add("ĝğġģ", 'g'); Add("ĜĞĠĢ", 'G'); Add("ĥħ", 'h'); Add("ĤĦ", 'H');
        Add("ìíîïĩīĭįı", 'i'); Add("ÌÍÎÏĨĪĬĮİ", 'I'); Add("ĵ", 'j'); Add("Ĵ", 'J'); Add("ķ", 'k'); Add("Ķ", 'K');
        Add("ĺļľŀł", 'l'); Add("ĹĻĽĿŁ", 'L'); Add("ñńņňŉ", 'n'); Add("ÑŃŅŇ", 'N'); Add("òóôõöøōŏő", 'o'); Add("ÒÓÔÕÖØŌŎŐ", 'O');
        Add("ŕŗř", 'r'); Add("ŔŖŘ", 'R'); Add("śŝşšș", 's'); Add("ŚŜŞŠȘ", 'S'); Add("ţťŧț", 't'); Add("ŢŤŦȚ", 'T');
        Add("ùúûüũūŭůűų", 'u'); Add("ÙÚÛÜŨŪŬŮŰŲ", 'U'); Add("ŵ", 'w'); Add("Ŵ", 'W'); Add("ýÿŷ", 'y'); Add("ÝŸŶ", 'Y');
        Add("źżž", 'z'); Add("ŹŻŽ", 'Z');
        return d;
    }

    /// <summary>The versions to write <paramref name="ch"/> with, and the character they are of.</summary>
    static (IReadOnlyList<HandGlyph>? Glyphs, string Used) Find(HandFont font, string ch)
    {
        if (font.Versions(ch) is { } g) return (g, ch);
        if (Similar.TryGetValue(ch, out var s) && font.Versions(s) is { } gs) return (gs, s);
        // An accented letter without its accent.
        string bare = ch.Length == 1 && Bare.TryGetValue(ch[0], out var b0) ? b0.ToString() : ch;
        if (bare.Length > 0 && bare != ch && font.Versions(bare) is { } gb) return (gb, bare);
        // The other case.
        foreach (var other in new[] { bare.ToUpperInvariant(), bare.ToLowerInvariant() })
            if (other != ch && font.Versions(other) is { } go) return (go, other);
        return (null, ch);
    }

    static uint Hash(int seed, int a, int b)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>A repeatable number from −1 to 1.</summary>
    static double Rand(int seed, int position, int what) => Hash(seed, position, what) / (double)uint.MaxValue * 2 - 1;

    /// <summary>A turn sharper than this stays a corner instead of being rounded.</summary>
    const double CornerRad = 60 * Math.PI / 180;

    /// <summary>
    /// A stroke's points as smooth curves through them (sharp turns kept as corners), so the
    /// writing stays smooth however big it's made.
    /// </summary>
    public static Subpath Smooth(HandStroke stroke)
    {
        var p = stroke.Points.ToList();
        bool closed = stroke.Closed;
        if (closed && p.Count > 2 && p[0].Dist(p[^1]) < 1e-9) p.RemoveAt(p.Count - 1);
        int n = p.Count;
        if (n < 2) return new Subpath(n == 1 ? p[0] : default, [], false);
        if (n == 2 && !closed) return new Subpath(p[0], [Seg.Line(p[1])], false);
        Vec2 At(int i) => p[((i % n) + n) % n];
        static Vec2 Unit(Vec2 d) { double l = d.Length; return l > 1e-12 ? d / l : default; }
        var tIn = new Vec2[n];
        var tOut = new Vec2[n];
        for (int i = 0; i < n; i++)
        {
            bool end = !closed && (i == 0 || i == n - 1);
            if (end)
            {
                var t = i == 0 ? Unit(p[1] - p[0]) : Unit(p[n - 1] - p[n - 2]);
                tIn[i] = tOut[i] = t;
                continue;
            }
            Vec2 a = Unit(At(i) - At(i - 1)), b = Unit(At(i + 1) - At(i));
            double turn = Math.Abs(Math.Atan2(a.Cross(b), a.Dot(b)));
            if (turn > CornerRad) { tIn[i] = a; tOut[i] = b; }
            else tIn[i] = tOut[i] = Unit(At(i + 1) - At(i - 1));
        }
        var segs = ImmutableArray.CreateBuilder<Seg>();
        int count = closed ? n : n - 1;
        for (int i = 0; i < count; i++)
        {
            Vec2 a = At(i), b = At(i + 1);
            double d = a.Dist(b) / 3;
            segs.Add(Seg.Cubic(a + tOut[i] * d, b - tIn[(i + 1) % n] * d, b));
        }
        return new Subpath(p[0], segs.ToImmutable(), closed);
    }
}
