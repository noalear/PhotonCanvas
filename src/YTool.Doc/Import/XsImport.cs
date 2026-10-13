using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using YTool.Core;

namespace YTool.Doc.Import;

/// <summary>
/// xTool Studio projects → millimetre geometry: <c>.xs</c> (version 2, a ZIP of JSON files)
/// and <c>.xcs</c> (version 1, one JSON document). See docs/file-format.md.
/// <para>
/// Reads the active canvas: paths, rectangles, circles/ellipses, lines, text (from the
/// glyph outlines xTool stores with it) and pictures. Each object's colour is its xTool
/// layer colour, and the layer names come along in <see cref="ImportResult.LayerNames"/>.
/// Processing settings (power, speed, …) are not imported.
/// </para>
/// </summary>
/// <remarks>
/// Placement: an object's own coordinates (its <c>dPath</c>, its 0…width box, a picture's
/// pixels) are scaled by <c>scale</c>, rotated by <c>angle</c> degrees (clockwise on screen)
/// and then moved by <c>offsetX/offsetY</c>. The stored <c>x/y</c> is where the object's own
/// top-left corner lands after that, and <c>width/height</c> are its scaled, unrotated size.
/// A <c>transform</c> matrix, when present, gives the same placement directly.
/// </remarks>
public static class XsImport
{
    /// <summary>Read an xTool project file (.xs or .xcs).</summary>
    /// <exception cref="FormatException">The file is not an xTool project or is damaged.</exception>
    public static ImportResult Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Read an xTool project from a stream.</summary>
    /// <exception cref="FormatException">The data is not an xTool project or is damaged.</exception>
    public static ImportResult Load(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>Read an xTool project held in memory (either format; detected from the content).</summary>
    /// <exception cref="FormatException">The data is not an xTool project or is damaged.</exception>
    public static ImportResult Parse(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 'P' && data[1] == 'K') return ParseZip(data);
        return ParseJson(data);
    }

    // ---- v2: ZIP ------------------------------------------------------------

    static ImportResult ParseZip(byte[] data)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read); }
        catch (InvalidDataException ex) { throw new FormatException("This xTool project file is damaged (it isn't a valid ZIP).", ex); }
        using (zip)
        {
            var entries = zip.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'), StringComparer.Ordinal);
            JsonNode? ReadJson(string name)
            {
                if (!entries.TryGetValue(name, out var e)) return null;
                using var s = e.Open();
                try { return JsonNode.Parse(s); }
                catch (JsonException ex) { throw new FormatException($"This xTool project file is damaged ({name} is not valid JSON).", ex); }
            }
            byte[]? ReadBytes(string name)
            {
                if (!entries.TryGetValue(name.TrimStart('/'), out var e)) return null;
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            var project = ReadJson("project.json") as JsonObject;
            var canvasIds = entries.Keys
                .Where(k => k.StartsWith("canvases/", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal)
                            && k.Count(c => c == '/') == 1)
                .Select(k => k["canvases/".Length..^".json".Length])
                .ToList();
            if (project is null && canvasIds.Count == 0)
                throw new FormatException("This ZIP file is not an xTool Studio project (no project.json or canvases).");

            var order = Items(project?["modules"]?["canvases"]).Select(n => Str(n)).OfType<string>().ToList();
            canvasIds = [.. canvasIds.OrderBy(id => order.IndexOf(id) is var i && i >= 0 ? i : int.MaxValue).ThenBy(id => id, StringComparer.Ordinal)];
            if (canvasIds.Count == 0) return new ImportResult([], [], ["The project has no canvas."]);

            var active = Str(project?["activeCanvasId"]);
            var id = active is not null && canvasIds.Contains(active) ? active : canvasIds[0];
            var canvas = ReadJson($"canvases/{id}.json") as JsonObject ?? new JsonObject();

            var chunks = entries.Keys
                .Where(k => k.StartsWith($"canvases/{id}/displays-", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal))
                .Select(k => (Key: k, N: int.TryParse(k[$"canvases/{id}/displays-".Length..^".json".Length], out var n) ? n : int.MaxValue))
                .OrderBy(c => c.N)
                .Select(c => c.Key);
            var displays = new List<JsonNode>();
            foreach (var chunk in chunks) displays.AddRange(Items(ReadJson(chunk)?["displays"]).OfType<JsonNode>());
            if (displays.Count == 0 && canvas["displays"] is { } inline) displays.AddRange(Items(inline).OfType<JsonNode>());

            var conv = new Converter(ReadBytes);
            if (canvasIds.Count > 1)
                conv.Warn($"This project has {canvasIds.Count} canvases; only \"{CanvasTitle(canvas, id)}\" was imported.");
            return conv.Run(canvas, displays);
        }
    }

    // ---- v1: JSON -----------------------------------------------------------

    static ImportResult ParseJson(byte[] data)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException ex)
        {
            throw new FormatException("This is not an xTool Studio project (it is neither a ZIP nor JSON).", ex);
        }
        if (root is not JsonObject obj) throw new FormatException("This is not an xTool Studio project.");
        if (obj["canvas"] is not { } canvasNode)
        {
            if (obj["__v2__"] is not null || obj["schemaMeta"] is not null)
                throw new FormatException("This is the project.json of an unpacked xTool project. Open the .xs file instead.");
            throw new FormatException("This JSON file is not an xTool Studio project (it has no canvas).");
        }

        var canvases = Items(canvasNode).OfType<JsonObject>().ToList();
        if (canvases.Count == 0) return new ImportResult([], [], ["The project has no canvas."]);
        var active = Str(obj["canvasId"]);
        var canvas = canvases.Find(c => Str(c["id"]) == active) ?? canvases[0];
        var displays = Items(canvas["displays"]).OfType<JsonNode>().ToList();

        var conv = new Converter(_ => null);
        if (canvases.Count > 1)
            conv.Warn($"This project has {canvases.Count} canvases; only \"{CanvasTitle(canvas, Str(canvas["id"]) ?? "")}\" was imported.");
        return conv.Run(canvas, displays);
    }

    static string CanvasTitle(JsonObject canvas, string id)
    {
        var t = Str(canvas["title"]) ?? id;
        return t.Replace("{panel}", "Canvas ", StringComparison.Ordinal);
    }

    // ---- displays → geometry ------------------------------------------------

    sealed class Converter(Func<string, byte[]?> resource)
    {
        readonly List<ImportedPath> _paths = [];
        readonly List<ImportedImage> _images = [];
        readonly List<string> _warnings = [];
        readonly SortedSet<string> _unknownTypes = new(StringComparer.Ordinal);
        int _hidden;

        public void Warn(string w)
        {
            if (!_warnings.Contains(w)) _warnings.Add(w);
        }

        public ImportResult Run(JsonObject canvas, List<JsonNode> displays)
        {
            var names = new Dictionary<uint, string>();
            foreach (var (key, value) in Entries(canvas["layerData"]))
            {
                if (SvgImport.ParseColor(key) is not { } c) continue;
                var name = Str(value?["name"]);
                if (!string.IsNullOrWhiteSpace(name)) names[c] = name;
            }

            var ordered = displays.OfType<JsonObject>()
                .Select((d, i) => (d, i))
                .OrderBy(p => Num(p.d["zOrder"], 0)).ThenBy(p => p.i)
                .Select(p => p.d);
            foreach (var d in ordered)
            {
                try { Display(d); }
                catch (FormatException)
                {
                    Warn("Some objects had unreadable shape data and were skipped.");
                }
            }

            foreach (var t in _unknownTypes)
                Warn(t == "MODEL_3D"
                    ? "3-D models were skipped."
                    : $"Objects of type {t} were skipped (not supported yet).");
            if (_hidden > 0) Warn($"{_hidden} hidden object(s) were skipped.");
            if (_paths.Count == 0 && _images.Count == 0 && _warnings.Count == 0) Warn("The project contains no objects.");
            return new ImportResult(_paths, _images, _warnings) { LayerNames = names };
        }

        void Display(JsonObject d)
        {
            var type = (Str(d["type"]) ?? "").ToUpperInvariant();
            if (Bool(d["visible"]) == false || Bool(d["visibleState"]) == false)
            {
                _hidden++;
                return;
            }
            uint? color = Color(d);
            var m = Placement(d);
            switch (type)
            {
                case "PATH":
                {
                    var g = PathOf(d);
                    if (g is not null) Add(g.Transform(m), color, Label(d, "Path"));
                    return;
                }
                case "RECT":
                {
                    var (w, h) = LocalSize(d, m);
                    if (w <= 0 || h <= 0) return;
                    double r = Num(d["radius"], 0);
                    var (sx, sy) = AxisScales(m);
                    var g = r > 0 ? PathGeometry.RoundRect(0, 0, w, h, r / sx, r / sy) : PathGeometry.Rect(0, 0, w, h);
                    Add(g.Transform(m), color, Label(d, "Rectangle"));
                    return;
                }
                case "CIRCLE" or "ELLIPSE":
                {
                    var (w, h) = LocalSize(d, m);
                    if (w <= 0 || h <= 0) return;
                    Add(PathGeometry.Ellipse(w / 2, h / 2, w / 2, h / 2).Transform(m), color, Label(d, type == "CIRCLE" ? "Circle" : "Ellipse"));
                    return;
                }
                case "LINE":
                {
                    var end = new Vec2(Num(d["endPoint"]?["x"], 0), Num(d["endPoint"]?["y"], 0));
                    if (end.Length <= 0) return;
                    Add(PathGeometry.Line(new(0, 0), end).Transform(m), color, Label(d, "Line"));
                    return;
                }
                case "TEXT":
                {
                    var subs = new List<Subpath>();
                    foreach (var glyph in Items(d["charJSONs"]).OfType<JsonObject>())
                        if (PathOf(glyph) is { } g)
                            subs.AddRange(g.Transform(Placement(glyph)).Subpaths); // glyphs carry their own canvas placement
                    if (subs.Count == 0)
                    {
                        Warn("Text without stored letter outlines was skipped. Convert it to a path in xTool Studio first.");
                        return;
                    }
                    var text = Str(d["text"]);
                    var label = Str(d["name"]) is { Length: > 0 } n ? n
                        : text is { Length: > 0 } ? $"Text \"{(text.Length > 30 ? text[..30] + "…" : text)}\"" : "Text";
                    Add(new PathGeometry(subs), color, label);
                    return;
                }
                case "BITMAP": Bitmap(d, m, color); return;
                case "GROUP": return; // groups only tie objects together
                default:
                    _unknownTypes.Add(type.Length > 0 ? type : "(unnamed)");
                    return;
            }
        }

        void Add(PathGeometry g, uint? color, string name)
        {
            if (!g.IsEmpty) _paths.Add(new ImportedPath(g, color, name));
        }

        PathGeometry? PathOf(JsonObject d)
        {
            var dPath = Str(d["dPath"]);
            if (string.IsNullOrWhiteSpace(dPath))
            {
                if (d["vectorRef"] is not null)
                    Warn("Some large paths are stored outside the object list (vectorRef) and couldn't be read yet.");
                return null;
            }
            var g = PathGeometry.FromSvgPathData(dPath);
            return g.IsEmpty ? null : g;
        }

        void Bitmap(JsonObject d, Affine m, uint? color)
        {
            byte[]? bytes = null;
            if (Str(d["resourcePath"]) is { Length: > 0 } rp) bytes = resource(rp);
            if (bytes is null && Str(d["base64"]) is { Length: > 0 } b64)
            {
                int comma = b64.IndexOf(',');
                if (b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0) b64 = b64[(comma + 1)..];
                try { bytes = Convert.FromBase64String(b64); }
                catch (FormatException) { bytes = null; }
            }
            if (bytes is null || bytes.Length == 0)
            {
                Warn("Some pictures were skipped because their image data is missing from the project.");
                return;
            }
            double ow = Num(d["originWidth"], 0), oh = Num(d["originHeight"], 0);
            if (ow <= 0 || oh <= 0)
            {
                try
                {
                    var src = ImageSource.FromEncoded(bytes);
                    (ow, oh) = (src.Width, src.Height);
                }
                catch (FormatException)
                {
                    Warn("Some pictures were skipped because their image data can't be read.");
                    return;
                }
            }
            // Local pixels (0..ow, 0..oh) are placed by m; express that in millimetres instead.
            var (sx, sy) = AxisScales(m);
            var size = new Vec2(ow * sx, oh * sy);
            if (size.X <= 0 || size.Y <= 0) return;
            var t = Affine.Scale(ow / size.X, oh / size.Y).Then(m);
            if (d["mask"] is JsonObject or JsonArray)
                Warn("Picture masks and crops were ignored; the whole picture was imported.");
            _images.Add(new ImportedImage(bytes, size, t, Label(d, "Picture"), color));
        }

        static string Label(JsonObject d, string fallback) => Str(d["name"]) is { Length: > 0 } n ? n : fallback;

        static uint? Color(JsonObject d)
        {
            foreach (var key in (string[])["layerColor", "layerTag"])
                if (Str(d[key]) is { } s && SvgImport.ParseColor(s) is { } c) return c;
            return null;
        }

        /// <summary>The object's own size before its scale (width/height are stored after it).</summary>
        static (double W, double H) LocalSize(JsonObject d, Affine m)
        {
            var (sx, sy) = AxisScales(m);
            double w = Num(d["width"], 0), h = Num(d["height"], 0);
            return (w / sx, h / sy);
        }

        static (double X, double Y) AxisScales(Affine m)
        {
            double sx = Math.Sqrt(m.A * m.A + m.B * m.B), sy = Math.Sqrt(m.C * m.C + m.D * m.D);
            return (sx > 1e-12 ? sx : 1, sy > 1e-12 ? sy : 1);
        }

        /// <summary>Object coordinates → canvas millimetres (see the class remarks).</summary>
        Affine Placement(JsonObject d)
        {
            if (d["transform"] is JsonArray t && t.Count == 6)
            {
                var v = t.Select(n => Num(n, double.NaN)).ToArray();
                if (v.All(double.IsFinite)) return new Affine(v[0], v[1], v[2], v[3], v[4], v[5]);
            }
            double sx = Num(d["scale"]?["x"], 1), sy = Num(d["scale"]?["y"], 1);
            double rot = Num(d["angle"], 0) * Math.PI / 180;
            double kx = Num(d["skew"]?["x"], 0), ky = Num(d["skew"]?["y"], 0);
            if (kx != 0 || ky != 0)
                Warn("Some objects are skewed; xTool's skew convention isn't confirmed, so check their shape.");
            kx *= Math.PI / 180;
            ky *= Math.PI / 180;
            // Same composition as PixiJS (which xTool's canvas is built on): scale, skew, rotate,
            // with the pivot (normally 0, 0) as the object's own origin.
            double a = Math.Cos(rot + ky) * sx, b = Math.Sin(rot + ky) * sx;
            double c = -Math.Sin(rot - kx) * sy, dd = Math.Cos(rot - kx) * sy;
            double px = Num(d["pivot"]?["x"], 0), py = Num(d["pivot"]?["y"], 0);
            double ox = Num(d["offsetX"], Num(d["x"], 0)), oy = Num(d["offsetY"], Num(d["y"], 0));
            return new Affine(a, b, c, dd, ox - (px * a + py * c), oy - (px * b + py * dd));
        }
    }

    // ---- JSON helpers -------------------------------------------------------

    /// <summary>Key/value pairs of an object, or of a JS Map saved as {"dataType":"Map","value":[[k,v],…]}.</summary>
    static IEnumerable<(string Key, JsonNode? Value)> Entries(JsonNode? n)
    {
        if (n is not JsonObject o) yield break;
        if (Str(o["dataType"]) == "Map" && o["value"] is JsonArray pairs)
        {
            foreach (var p in pairs)
                if (p is JsonArray { Count: >= 2 } kv && Str(kv[0]) is { } k) yield return (k, kv[1]);
            yield break;
        }
        foreach (var (k, v) in o) yield return (k, v);
    }

    /// <summary>Elements of an array, or values of an object / saved JS Map.</summary>
    static IEnumerable<JsonNode?> Items(JsonNode? n) => n switch
    {
        JsonArray a => a,
        JsonObject => Entries(n).Select(e => e.Value),
        _ => [],
    };

    static string? Str(JsonNode? n) =>
        n is JsonValue v ? v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number => v.ToJsonString(),
            _ => null,
        } : null;

    static bool? Bool(JsonNode? n) =>
        n is JsonValue v ? v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        } : null;

    static double Num(JsonNode? n, double def)
    {
        if (n is not JsonValue v) return def;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                return v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : def;
            case JsonValueKind.String:
                return double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && double.IsFinite(s) ? s : def;
            default:
                return def;
        }
    }
}
