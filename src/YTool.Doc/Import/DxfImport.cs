using System.Globalization;
using System.Text;
using YTool.Core;

namespace YTool.Doc.Import;

/// <summary>
/// AutoCAD DXF (ASCII, R12 to R2018) → millimetre geometry.
/// <para>
/// Reads LINE, LWPOLYLINE and POLYLINE (with arc bulges), CIRCLE, ARC, ELLIPSE, SPLINE,
/// SOLID/TRACE and block references (INSERT, including nested blocks and arrays). Text,
/// hatches, dimensions and 3-D objects are reported as warnings rather than guessed at.
/// </para>
/// <para>
/// The drawing is scaled from its <c>$INSUNITS</c> to millimetres, turned right side up
/// (DXF's Y axis points up, ours points down) and moved so it starts at (0, 0).
/// Each path is named after its DXF layer and coloured with its DXF colour.
/// </para>
/// </summary>
public static class DxfImport
{
    /// <summary>Read a DXF file.</summary>
    /// <exception cref="FormatException">The file is binary DXF, or damaged.</exception>
    public static ImportResult Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Read a DXF file from a stream.</summary>
    /// <exception cref="FormatException">The data is binary DXF, or damaged.</exception>
    public static ImportResult Load(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>Read DXF file contents.</summary>
    /// <exception cref="FormatException">The data is binary DXF, or damaged.</exception>
    public static ImportResult Parse(byte[] data)
    {
        if (StartsWith(data, "AutoCAD Binary DXF"u8))
            throw new FormatException(
                "This is a binary DXF file, which can't be read. Save it again as an ASCII DXF from your CAD program.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(data); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(data); } // pre-2007 files use the ANSI code page
        return ParseString(text);
    }

    /// <summary>Read DXF text (as it appears in the file).</summary>
    /// <exception cref="FormatException">The text is not valid DXF.</exception>
    public static ImportResult ParseString(string dxf)
    {
        var tags = ReadTags(dxf);
        var doc = new DxfDoc();
        doc.Read(tags);
        return new Converter(doc).Run();
    }

    static bool StartsWith(byte[] data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    // ---- tag reading --------------------------------------------------------

    readonly record struct Tag(int Code, string Value);

    static List<Tag> ReadTags(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var lines = text.Split('\n');
        int count = lines.Length;
        while (count > 0 && lines[count - 1].Trim().Length == 0) count--;
        var tags = new List<Tag>(count / 2);
        for (int i = 0; i + 1 < count; i += 2)
        {
            var codeText = lines[i].Trim();
            if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                if (i == 0) throw new FormatException("This is not a DXF file (it doesn't start with a DXF group code).");
                throw new FormatException(
                    $"This DXF file is damaged: line {i + 1} should hold a group code number but reads \"{Shorten(codeText)}\".");
            }
            tags.Add(new Tag(code, lines[i + 1].Trim()));
        }
        if (tags.Count == 0) throw new FormatException("The DXF file is empty.");
        return tags;
    }

    static string Shorten(string s) => s.Length > 40 ? s[..40] + "…" : s;

    static double Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : 0;

    // ---- document model -----------------------------------------------------

    sealed class Ent(string type)
    {
        public string Type { get; } = type;
        public List<Tag> Tags { get; } = [];
        public List<Ent> Children { get; } = [];

        public bool Has(int code) => Tags.Exists(t => t.Code == code);
        public string? S(int code) => Tags.Find(t => t.Code == code).Value;

        public double D(int code, double def = 0)
        {
            foreach (var t in Tags) if (t.Code == code) return Num(t.Value);
            return def;
        }

        public int I(int code, int def = 0)
        {
            foreach (var t in Tags)
                if (t.Code == code)
                    return int.TryParse(t.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int)Num(t.Value);
            return def;
        }

        public List<double> All(int code) => [.. Tags.Where(t => t.Code == code).Select(t => Num(t.Value))];

        /// <summary>Points given as repeated (xCode, xCode+10) pairs, in file order.</summary>
        public List<Vec2> Points(int xCode)
        {
            var o = new List<Vec2>();
            foreach (var t in Tags)
            {
                if (t.Code == xCode) o.Add(new(Num(t.Value), 0));
                else if (t.Code == xCode + 10 && o.Count > 0) o[^1] = o[^1] with { Y = Num(t.Value) };
            }
            return o;
        }

        public Vec2 P(int xCode) => new(D(xCode), D(xCode + 10));

        /// <summary>Extrusion direction (210/220/230), default +Z.</summary>
        public (double X, double Y, double Z) Normal => (D(210), D(220), D(230, 1));
    }

    sealed record Layer(string Name, int Aci, uint? TrueColor, bool Hidden);

    sealed record Block(string Name, Vec2 Base, List<Ent> Entities, bool IsXref);

    sealed class DxfDoc
    {
        public Dictionary<string, string> Header { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Layer> Layers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Block> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Ent> Entities { get; } = [];
        public bool SawSection { get; private set; }

        public void Read(List<Tag> tags)
        {
            int i = 0;
            while (i < tags.Count)
            {
                if (tags[i] is { Code: 0, Value: "SECTION" } && i + 1 < tags.Count && tags[i + 1].Code == 2)
                {
                    SawSection = true;
                    string name = tags[i + 1].Value.ToUpperInvariant();
                    int start = i + 2, end = start;
                    while (end < tags.Count && tags[end] is not { Code: 0, Value: "ENDSEC" }) end++;
                    var body = tags.GetRange(start, end - start);
                    switch (name)
                    {
                        case "HEADER": ReadHeader(body); break;
                        case "TABLES": ReadTables(body); break;
                        case "BLOCKS": ReadBlocks(body); break;
                        case "ENTITIES": Entities.AddRange(Group(Split(body))); break;
                    }
                    i = end + 1;
                }
                else if (tags[i] is { Code: 0, Value: "EOF" }) break;
                else i++;
            }
            if (!SawSection) throw new FormatException("This is not a DXF file (no SECTION found).");
        }

        void ReadHeader(List<Tag> body)
        {
            for (int i = 0; i < body.Count; i++)
                if (body[i].Code == 9 && i + 1 < body.Count && body[i + 1].Code != 9)
                    Header[body[i].Value] = body[i + 1].Value;
        }

        void ReadTables(List<Tag> body)
        {
            foreach (var e in Split(body))
            {
                if (e.Type != "LAYER") continue;
                var name = e.S(2) ?? "0";
                int aci = e.I(62, 7);
                uint? tc = e.Has(420) ? (uint)e.I(420) & 0xFFFFFF : null;
                bool hidden = aci < 0 || (e.I(70) & 1) != 0;
                Layers[name] = new Layer(name, Math.Abs(aci), tc, hidden);
            }
        }

        void ReadBlocks(List<Tag> body)
        {
            var ents = Split(body);
            for (int i = 0; i < ents.Count; i++)
            {
                if (ents[i].Type != "BLOCK") continue;
                var head = ents[i];
                var content = new List<Ent>();
                int j = i + 1;
                for (; j < ents.Count && ents[j].Type != "ENDBLK"; j++) content.Add(ents[j]);
                var name = head.S(2) ?? head.S(3) ?? "";
                bool xref = (head.I(70) & (4 | 8)) != 0;
                Blocks[name] = new Block(name, head.P(10), Group(content), xref);
                i = j;
            }
        }

        /// <summary>Split a tag run into entities at each code-0 tag.</summary>
        static List<Ent> Split(List<Tag> body)
        {
            var o = new List<Ent>();
            Ent? cur = null;
            foreach (var t in body)
            {
                if (t.Code == 0) o.Add(cur = new Ent(t.Value.ToUpperInvariant()));
                else cur?.Tags.Add(t);
            }
            return o;
        }

        /// <summary>Attach VERTEX/ATTRIB runs (ended by SEQEND) to their POLYLINE/INSERT.</summary>
        static List<Ent> Group(List<Ent> flat)
        {
            var o = new List<Ent>();
            for (int i = 0; i < flat.Count; i++)
            {
                var e = flat[i];
                o.Add(e);
                bool takesChildren = e.Type == "POLYLINE" || (e.Type == "INSERT" && e.I(66) == 1);
                if (!takesChildren) continue;
                int j = i + 1;
                for (; j < flat.Count && flat[j].Type is "VERTEX" or "ATTRIB"; j++) e.Children.Add(flat[j]);
                if (j < flat.Count && flat[j].Type == "SEQEND") i = j;
                else i = j - 1;
            }
            return o;
        }
    }

    // ---- conversion ---------------------------------------------------------

    readonly record struct Inherit(Affine T, string? Layer, uint? ByBlockColor, int Depth);

    sealed record Raw(PathGeometry Geometry, uint Color, string Layer);

    sealed class Converter(DxfDoc doc)
    {
        readonly List<Raw> _out = [];
        readonly List<string> _warnings = [];
        readonly Dictionary<string, int> _skipped = [];
        readonly HashSet<string> _blockStack = new(StringComparer.OrdinalIgnoreCase);
        int _paperSkipped, _hiddenSkipped;
        double _unitMm = 1, _tol = 0.005;

        void Warn(string w)
        {
            if (!_warnings.Contains(w)) _warnings.Add(w);
        }

        void Skip(string what) => _skipped[what] = _skipped.GetValueOrDefault(what) + 1;

        public ImportResult Run()
        {
            int units = doc.Header.TryGetValue("$INSUNITS", out var u) ? (int)Num(u) : 0;
            bool unitless = units == 0 || UnitMm(units) is null;
            _unitMm = UnitMm(units) ?? 1;
            _tol = 0.005 / _unitMm; // spline sampling tolerance in drawing units (5 µm)

            var top = new Inherit(Affine.Identity, null, null, 0);
            foreach (var e in doc.Entities)
            {
                if (e.I(67) == 1) { _paperSkipped++; continue; }
                Entity(e, top);
            }

            ReportSkipped();
            if (_hiddenSkipped > 0)
                Warn($"{_hiddenSkipped} object(s) on hidden or frozen layers were skipped.");
            if (_out.Count == 0 && _paperSkipped > 0)
                Warn("This drawing only has objects in a paper-space layout, which were skipped. Move them to model space.");

            // DXF units → mm, Y up → Y down, then move the drawing to start at (0, 0).
            var flip = Affine.Scale(_unitMm, -_unitMm);
            var bounds = _out.Aggregate(Rect2.Empty, (r, p) => r.Union(p.Geometry.Transform(flip).Bounds()));
            var final = bounds.IsEmpty ? flip : flip.Then(Affine.Translate(-bounds.MinX, -bounds.MinY));

            if (!bounds.IsEmpty)
            {
                double size = Math.Max(bounds.Width, bounds.Height);
                if (unitless && (size < 1 || size > 2000))
                    Warn($"The drawing doesn't say which units it uses, so it was read as millimetres and is " +
                         $"{bounds.Width:0.###} × {bounds.Height:0.###} mm. If it was drawn in inches, scale it by 25.4.");
                else if (!unitless && _unitMm > 1 && size > 2000)
                    // Templates often carry the wrong units; say so rather than silently guessing.
                    Warn($"The file says it is drawn in {UnitName(units)}, which makes it {bounds.Width:0.#} × {bounds.Height:0.#} mm, " +
                         $"far larger than a laser bed. If it was really drawn in millimetres, scale it down by {_unitMm:0.####}×.");
            }

            var paths = _out.Select(r => new ImportedPath(r.Geometry.Transform(final), r.Color, r.Layer)).ToList();
            if (paths.Count == 0 && _warnings.Count == 0) Warn("The file contains no shapes.");

            // Layer names keyed by colour, where a colour belongs to exactly one layer.
            var names = new Dictionary<uint, string>();
            foreach (var g in _out.GroupBy(r => r.Color))
            {
                var layers = g.Select(r => r.Layer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (layers.Count == 1) names[g.Key] = layers[0];
            }
            return new ImportResult(paths, [], _warnings) { LayerNames = names };
        }

        static string UnitName(int insunits) => insunits switch
        {
            1 => "inches", 2 => "feet", 3 => "miles", 5 => "centimetres", 6 => "metres", 7 => "kilometres",
            10 => "yards", 14 => "decimetres", _ => $"unit code {insunits}",
        };

        static double? UnitMm(int insunits) => insunits switch
        {
            1 => 25.4,         // inches
            2 => 304.8,        // feet
            3 => 1_609_344,    // miles
            4 => 1,            // millimetres
            5 => 10,           // centimetres
            6 => 1000,         // metres
            7 => 1_000_000,    // kilometres
            8 => 25.4e-6,      // microinches
            9 => 0.0254,       // mils
            10 => 914.4,       // yards
            11 => 1e-7,        // ångströms
            12 => 1e-6,        // nanometres
            13 => 1e-3,        // microns
            14 => 100,         // decimetres
            _ => null,
        };

        void ReportSkipped()
        {
            foreach (var (what, n) in _skipped)
            {
                Warn(what switch
                {
                    "text" => $"Text was skipped ({n} object(s)). Convert text to curves (e.g. TXTEXP / Explode Text) in your CAD program first.",
                    "hatch" => $"Hatch fills were skipped ({n} object(s)). Use a fill operation on the outline instead.",
                    "dimension" => $"Dimensions were skipped ({n} object(s)).",
                    "image" => $"Linked pictures were skipped ({n} object(s)). Import the picture file separately.",
                    "3d" => $"3-D solids, surfaces and meshes were skipped ({n} object(s)).",
                    "infinite" => $"Construction lines (XLINE/RAY) are infinitely long and were skipped ({n} object(s)).",
                    "leader" => $"Leaders were skipped ({n} object(s)).",
                    "mesh polyline" => $"Polygon/polyface mesh polylines were skipped ({n} object(s)).",
                    _ => $"{n} object(s) of type {what} were skipped (not supported).",
                });
            }
        }

        // ---- entities -------------------------------------------------------

        void Entity(Ent e, Inherit inh)
        {
            if (e.I(60) == 1) return; // invisible
            switch (e.Type)
            {
                case "LINE":
                    Emit(e, inh, PathGeometry.Line(e.P(10), e.P(11)), ocs: false);
                    return;
                case "CIRCLE":
                {
                    double r = e.D(40);
                    if (r > 0) Emit(e, inh, EllipseArc(e.P(10), new(r, 0), new(0, r), 0, 2 * Math.PI, closed: true), ocs: true);
                    return;
                }
                case "ARC":
                {
                    double r = e.D(40);
                    double a0 = e.D(50) * Math.PI / 180, a1 = e.D(51) * Math.PI / 180;
                    double sweep = a1 - a0;
                    while (sweep <= 0) sweep += 2 * Math.PI;
                    if (r > 0) Emit(e, inh, EllipseArc(e.P(10), new(r, 0), new(0, r), a0, sweep, closed: false), ocs: true);
                    return;
                }
                case "ELLIPSE": Ellipse(e, inh); return;
                case "LWPOLYLINE": LwPolyline(e, inh); return;
                case "POLYLINE": Polyline(e, inh); return;
                case "SPLINE": Spline(e, inh); return;
                case "SOLID" or "TRACE":
                {
                    Vec2 a = e.P(10), b = e.P(11), c = e.P(12), d = e.Has(13) ? e.P(13) : e.P(12);
                    var pts = new List<Vec2> { a, b, d, c };
                    if (d.Dist(c) < 1e-12) pts.RemoveAt(3);
                    Emit(e, inh, PathGeometry.Polyline(pts, true), ocs: true);
                    return;
                }
                case "INSERT": Insert(e, inh); return;
                case "POINT" or "VIEWPORT" or "WIPEOUT" or "ATTDEF" or "SEQEND" or "XRECORD": return;
                case "TEXT" or "MTEXT" or "ATTRIB" or "RTEXT" or "ARCALIGNEDTEXT": Skip("text"); return;
                case "HATCH" or "MPOLYGON": Skip("hatch"); return;
                case "DIMENSION" or "ARC_DIMENSION" or "TOLERANCE": Skip("dimension"); return;
                case "IMAGE" or "OLE2FRAME" or "OLEFRAME" or "PDFUNDERLAY" or "DWFUNDERLAY" or "DGNUNDERLAY": Skip("image"); return;
                case "3DFACE" or "3DSOLID" or "REGION" or "BODY" or "MESH" or "SURFACE" or "PLANESURFACE"
                    or "EXTRUDEDSURFACE" or "LOFTEDSURFACE" or "REVOLVEDSURFACE" or "SWEPTSURFACE" or "NURBSURFACE"
                    or "POLYFACE" or "SHAPE":
                    Skip("3d"); return;
                case "XLINE" or "RAY": Skip("infinite"); return;
                case "LEADER" or "MLEADER" or "MULTILEADER": Skip("leader"); return;
                default: Skip(e.Type); return;
            }
        }

        /// <summary>The layer an entity really lives on (layer 0 inside a block takes the block reference's layer).</summary>
        string EffectiveLayer(Ent e, Inherit inh)
        {
            var layer = e.S(8);
            if (string.IsNullOrEmpty(layer)) layer = "0";
            return layer == "0" && inh.Layer is not null ? inh.Layer : layer;
        }

        bool LayerHidden(string layer) => doc.Layers.TryGetValue(layer, out var l) && l.Hidden;

        uint ResolveColor(Ent e, string layer, Inherit inh)
        {
            if (e.Has(420)) return (uint)e.I(420) & 0xFFFFFF;
            int aci = e.I(62, 256);
            if (aci == 0 && inh.ByBlockColor is { } bb) return bb;
            if (aci is >= 1 and <= 255) return Aci.ToRgb(aci);
            return LayerColor(layer);
        }

        uint LayerColor(string layer)
        {
            if (!doc.Layers.TryGetValue(layer, out var l)) return 0x000000;
            if (l.TrueColor is { } tc) return tc;
            return l.Aci is >= 1 and <= 255 ? Aci.ToRgb(l.Aci) : 0x000000;
        }

        /// <summary>OCS → WCS for the common cases: +Z (identity) and −Z (mirror X).</summary>
        Affine Ocs(Ent e)
        {
            var (nx, ny, nz) = e.Normal;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-12) return Affine.Identity;
            if (Math.Abs(nx / len) > 1e-6 || Math.Abs(ny / len) > 1e-6)
                Warn("Some objects are tilted in 3-D; they were flattened onto the drawing plane and may look distorted.");
            return nz < 0 ? Affine.Scale(-1, 1) : Affine.Identity;
        }

        void Emit(Ent e, Inherit inh, PathGeometry? geom, bool ocs)
        {
            if (geom is null || geom.IsEmpty) return;
            var layer = EffectiveLayer(e, inh);
            if (LayerHidden(layer)) { _hiddenSkipped++; return; }
            var t = ocs ? Ocs(e).Then(inh.T) : inh.T;
            _out.Add(new Raw(geom.Transform(t), ResolveColor(e, layer, inh), layer));
        }

        void Ellipse(Ent e, Inherit inh)
        {
            var c = e.P(10);
            var major = e.P(11);
            double ratio = e.D(40, 1);
            double t0 = e.D(41, 0), t1 = e.D(42, 2 * Math.PI);
            var (nx, ny, nz) = e.Normal;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-12) nz = len = 1;
            if (Math.Abs(nx / len) > 1e-6 || Math.Abs(ny / len) > 1e-6)
                Warn("Some objects are tilted in 3-D; they were flattened onto the drawing plane and may look distorted.");
            // Minor axis = ratio · (N × major); in the XY plane that is ratio · nz · (−My, Mx).
            double s = nz / len;
            var minor = new Vec2(-major.Y * s, major.X * s) * ratio;
            double sweep = t1 - t0;
            bool full = Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-9 || Math.Abs(sweep) < 1e-12;
            if (full) sweep = 2 * Math.PI;
            while (sweep < 0) sweep += 2 * Math.PI;
            if (major.Length <= 0) return;
            Emit(e, inh, EllipseArc(c, major, minor, t0, sweep, closed: full), ocs: false);
        }

        void LwPolyline(Ent e, Inherit inh)
        {
            var verts = new List<(Vec2 P, double Bulge)>();
            foreach (var t in e.Tags)
            {
                switch (t.Code)
                {
                    case 10: verts.Add((new(Num(t.Value), 0), 0)); break;
                    case 20 when verts.Count > 0: verts[^1] = (verts[^1].P with { Y = Num(t.Value) }, verts[^1].Bulge); break;
                    case 42 when verts.Count > 0: verts[^1] = (verts[^1].P, Num(t.Value)); break;
                }
            }
            Emit(e, inh, BulgePath(verts, (e.I(70) & 1) != 0), ocs: true);
        }

        void Polyline(Ent e, Inherit inh)
        {
            int flags = e.I(70);
            if ((flags & (16 | 64)) != 0) { Skip("mesh polyline"); return; }
            var verts = new List<(Vec2 P, double Bulge)>();
            foreach (var v in e.Children)
            {
                if (v.Type != "VERTEX") continue;
                int vf = v.I(70);
                if ((vf & 16) != 0) continue; // spline frame control point, not on the curve
                verts.Add((v.P(10), v.D(42)));
            }
            bool is3d = (flags & 8) != 0;
            Emit(e, inh, BulgePath(verts, (flags & 1) != 0), ocs: !is3d);
        }

        void Spline(Ent e, Inherit inh)
        {
            int flags = e.I(70);
            int degree = e.I(71, 3);
            var knots = e.All(40);
            var weights = e.All(41);
            var ctrl = e.Points(10);
            var fit = e.Points(11);
            bool closedFlag = (flags & 1) != 0;

            PathGeometry? g = null;
            if (ctrl.Count >= 2 && degree >= 1)
            {
                if (degree >= ctrl.Count) degree = ctrl.Count - 1;
                // A broken knot list: prefer the fit points if there are any, else assume clamped uniform knots.
                if (knots.Count != ctrl.Count + degree + 1 && fit.Count < 2)
                {
                    Warn("A spline had an inconsistent knot list; a standard (clamped, uniform) one was used instead.");
                    knots = ClampedUniformKnots(ctrl.Count, degree);
                }
                if (knots.Count == ctrl.Count + degree + 1)
                    g = Nurbs.ToGeometry(degree, [.. knots], ctrl, weights.Count == ctrl.Count ? weights : null, closedFlag, _tol);
            }
            if (g is null && fit.Count >= 2)
            {
                Warn("Some splines only had fit points; they were interpolated through those points and may differ slightly from your CAD program.");
                g = CatmullRom(fit, closedFlag);
            }
            Emit(e, inh, g, ocs: false);
        }

        static List<double> ClampedUniformKnots(int n, int p)
        {
            var k = new List<double>();
            int inner = n - p - 1;
            for (int i = 0; i <= p; i++) k.Add(0);
            for (int i = 1; i <= inner; i++) k.Add((double)i / (inner + 1));
            for (int i = 0; i <= p; i++) k.Add(1);
            return k;
        }

        void Insert(Ent e, Inherit inh)
        {
            var name = e.S(2) ?? "";
            if (!doc.Blocks.TryGetValue(name, out var block))
            {
                Warn($"A reference to the missing block \"{name}\" was skipped.");
                return;
            }
            if (block.IsXref)
            {
                Warn($"The external reference \"{name}\" can't be loaded. Bind it in your CAD program first.");
                return;
            }
            if (inh.Depth >= 32 || _blockStack.Contains(name))
            {
                Warn($"The block \"{name}\" contains itself; the repeated copy was skipped.");
                return;
            }
            var layer = EffectiveLayer(e, inh);
            if (LayerHidden(layer)) { _hiddenSkipped++; return; }
            uint color = ResolveColor(e, layer, inh);

            double sx = e.D(41, 1), sy = e.D(42, 1), rot = e.D(50);
            if (sx == 0) sx = 1;
            if (sy == 0) sy = 1;
            int cols = Math.Max(1, e.I(70, 1)), rows = Math.Max(1, e.I(71, 1));
            double colSp = e.D(44), rowSp = e.D(45);
            var ins = e.P(10);
            var ocs = Ocs(e);

            _blockStack.Add(name);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var t = Affine.Translate(-block.Base.X, -block.Base.Y)
                        .Then(Affine.Scale(sx, sy))
                        .Then(Affine.Translate(c * colSp, r * rowSp))
                        .Then(Affine.Rotate(rot)) // counter-clockwise, since DXF's Y points up
                        .Then(Affine.Translate(ins))
                        .Then(ocs)
                        .Then(inh.T);
                    var child = new Inherit(t, layer, color, inh.Depth + 1);
                    foreach (var be in block.Entities) Entity(be, child);
                }
            _blockStack.Remove(name);
            if (e.Children.Any(ch => ch.Type == "ATTRIB")) Skip("text");
        }
    }

    // ---- curve building (in drawing space; Y up, angles counter-clockwise) ---

    /// <summary>
    /// Elliptical arc P(t) = c + u·cos t + v·sin t from <paramref name="t0"/> over
    /// <paramref name="sweep"/> radians, as cubic Béziers (≤ 90° each).
    /// </summary>
    static PathGeometry EllipseArc(Vec2 c, Vec2 u, Vec2 v, double t0, double sweep, bool closed)
    {
        var segs = new List<Seg>();
        AppendArc(segs, c, u, v, t0, sweep);
        var start = c + u * Math.Cos(t0) + v * Math.Sin(t0);
        if (closed && segs.Count > 0) segs[^1] = segs[^1] with { To = start };
        return new([new Subpath(start, [.. segs], closed)]);
    }

    static void AppendArc(List<Seg> segs, Vec2 c, Vec2 u, Vec2 v, double t0, double sweep)
    {
        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
        double dt = sweep / n, k = 4.0 / 3 * Math.Tan(dt / 4);
        Vec2 P(double t) => c + u * Math.Cos(t) + v * Math.Sin(t);
        Vec2 D(double t) => v * Math.Cos(t) - u * Math.Sin(t);
        for (int i = 0; i < n; i++)
        {
            double a = t0 + i * dt, b = a + dt;
            segs.Add(Seg.Cubic(P(a) + D(a) * k, P(b) - D(b) * k, P(b)));
        }
    }

    /// <summary>
    /// A polyline whose vertices carry DXF bulges: bulge = tan(θ/4) of the arc to the next
    /// vertex, positive = counter-clockwise.
    /// </summary>
    static PathGeometry? BulgePath(List<(Vec2 P, double Bulge)> verts, bool closed)
    {
        if (verts.Count == 0) return null;
        if (verts.Count == 1) return null;
        var segs = new List<Seg>();
        int n = closed ? verts.Count : verts.Count - 1;
        for (int i = 0; i < n; i++)
        {
            var (p1, b) = verts[i];
            var p2 = verts[(i + 1) % verts.Count].P;
            if (closed && i == verts.Count - 1 && p1.Dist(p2) < 1e-12) break; // closing edge of zero length
            AppendBulge(segs, p1, p2, b);
        }
        return segs.Count == 0 ? null : new([new Subpath(verts[0].P, [.. segs], closed)]);
    }

    static void AppendBulge(List<Seg> segs, Vec2 p1, Vec2 p2, double b)
    {
        var d = p2 - p1;
        double chord = d.Length;
        if (Math.Abs(b) < 1e-12 || chord < 1e-12)
        {
            segs.Add(Seg.Line(p2));
            return;
        }
        double theta = 4 * Math.Atan(b);
        var left = new Vec2(-d.Y, d.X) / chord;
        var center = (p1 + p2) / 2 + left * (chord / 2 * (1 - b * b) / (2 * b));
        double r = p1.Dist(center);
        double a0 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
        int first = segs.Count;
        AppendArc(segs, center, new(r, 0), new(0, r), a0, theta);
        if (segs.Count > first) segs[^1] = segs[^1] with { To = p2 };
    }

    /// <summary>Smooth curve through points (uniform Catmull-Rom as cubic Béziers).</summary>
    static PathGeometry CatmullRom(List<Vec2> pts, bool closed)
    {
        if (closed && pts.Count > 2 && pts[^1].Dist(pts[0]) < 1e-9) pts = pts[..^1];
        int n = pts.Count;
        Vec2 At(int i) => closed ? pts[((i % n) + n) % n] : pts[Math.Clamp(i, 0, n - 1)];
        var segs = new List<Seg>();
        int count = closed ? n : n - 1;
        for (int i = 0; i < count; i++)
        {
            Vec2 p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
            segs.Add(Seg.Cubic(p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2));
        }
        return new([new Subpath(pts[0], [.. segs], closed)]);
    }

    /// <summary>B-spline / NURBS evaluation (de Boor) and conversion to path segments.</summary>
    static class Nurbs
    {
        readonly record struct H(double X, double Y, double W)
        {
            public static H operator +(H a, H b) => new(a.X + b.X, a.Y + b.Y, a.W + b.W);
            public static H operator *(H a, double s) => new(a.X * s, a.Y * s, a.W * s);
            public Vec2 Point => W != 0 ? new(X / W, Y / W) : new(X, Y);
        }

        /// <summary>
        /// The curve as path segments: exact cubic Béziers for non-rational splines of degree ≤ 3,
        /// otherwise lines through points sampled within <paramref name="tol"/>.
        /// </summary>
        public static PathGeometry? ToGeometry(int p, double[] u, List<Vec2> ctrl, List<double>? weights, bool closedFlag, double tol)
        {
            int n = ctrl.Count;
            bool rational = weights is not null && weights.Exists(w => Math.Abs(w - weights[0]) > 1e-12);
            var pts = new H[n];
            for (int i = 0; i < n; i++)
            {
                double w = rational ? weights![i] : 1;
                if (w <= 0) w = 1e-9;
                pts[i] = new H(ctrl[i].X * w, ctrl[i].Y * w, w);
            }
            var spans = new List<int>();
            for (int k = p; k < n; k++) if (u[k + 1] > u[k]) spans.Add(k);
            if (spans.Count == 0) return null;

            var segs = new List<Seg>();
            Vec2 start = Eval(p, u, pts, spans[0], u[spans[0]]).Point;
            if (!rational && p <= 3)
            {
                foreach (int k in spans)
                {
                    double a = u[k], b = u[k + 1];
                    var bz = new Vec2[p + 1];
                    var args = new double[p];
                    for (int j = 0; j <= p; j++)
                    {
                        for (int m = 0; m < p; m++) args[m] = m < p - j ? a : b;
                        bz[j] = Blossom(p, u, pts, k, args).Point;
                    }
                    segs.Add(p switch
                    {
                        1 => Seg.Line(bz[1]),
                        2 => Seg.Cubic(bz[0] + (bz[1] - bz[0]) * (2.0 / 3), bz[2] + (bz[1] - bz[2]) * (2.0 / 3), bz[2]),
                        _ => Seg.Cubic(bz[1], bz[2], bz[3]),
                    });
                }
            }
            else
            {
                foreach (int k in spans)
                {
                    double a = u[k], b = u[k + 1];
                    const int initial = 8;
                    var prev = Eval(p, u, pts, k, a).Point;
                    for (int i = 1; i <= initial; i++)
                    {
                        double t0 = a + (b - a) * (i - 1) / initial, t1 = a + (b - a) * i / initial;
                        var next = Eval(p, u, pts, k, t1).Point;
                        Refine(p, u, pts, k, t0, prev, t1, next, tol, segs, 0);
                        prev = next;
                    }
                }
            }
            var end = segs.Count > 0 ? segs[^1].To : start;
            bool closed = closedFlag && end.Dist(start) < Math.Max(tol, 1e-9) * 10;
            if (closed && segs.Count > 0) segs[^1] = segs[^1] with { To = start };
            return new([new Subpath(start, [.. segs], closed)]);
        }

        static void Refine(int p, double[] u, H[] pts, int k, double t0, Vec2 p0, double t1, Vec2 p1, double tol, List<Seg> segs, int depth)
        {
            double tm = (t0 + t1) / 2;
            var pm = Eval(p, u, pts, k, tm).Point;
            if (depth >= 14 || Geom.SegmentDistance(pm, p0, p1) <= tol)
            {
                segs.Add(Seg.Line(p1));
                return;
            }
            Refine(p, u, pts, k, t0, p0, tm, pm, tol, segs, depth + 1);
            Refine(p, u, pts, k, tm, pm, t1, p1, tol, segs, depth + 1);
        }

        static H Eval(int p, double[] u, H[] pts, int k, double t)
        {
            Span<double> args = stackalloc double[p];
            args.Fill(t);
            return Blossom(p, u, pts, k, args);
        }

        /// <summary>
        /// de Boor's algorithm with a separate parameter per level (the blossom). With every
        /// argument equal to t it evaluates the curve at t in knot span k (u[k] ≤ t ≤ u[k+1]).
        /// </summary>
        static H Blossom(int p, double[] u, H[] pts, int k, ReadOnlySpan<double> args)
        {
            Span<H> d = stackalloc H[p + 1];
            for (int j = 0; j <= p; j++) d[j] = pts[k - p + j];
            for (int r = 1; r <= p; r++)
                for (int j = p; j >= r; j--)
                {
                    int i = j + k - p;
                    double denom = u[i + p + 1 - r] - u[i];
                    double alpha = denom == 0 ? 0 : (args[r - 1] - u[i]) / denom;
                    d[j] = d[j - 1] * (1 - alpha) + d[j] * alpha;
                }
            return d[p];
        }
    }

    /// <summary>
    /// The RGB colour (0xRRGGBB) of an AutoCAD Color Index (ACI) number 1–255. Index 7, which
    /// CAD programs show as white or black depending on the background, gives black.
    /// </summary>
    public static uint AciToRgb(int index) => Aci.ToRgb(index);

    /// <summary>AutoCAD Color Index → RGB.</summary>
    static class Aci
    {
        static readonly uint[] Basic =
        [
            0x000000, // 0: BYBLOCK (not a real colour)
            0xFF0000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF,
            0x000000, // 7: white on a dark CAD screen, black on paper; black is what you'd expect here
            0x808080, 0xC0C0C0,
        ];

        static readonly uint[] Greys = [0x333333, 0x5B5B5B, 0x848484, 0xADADAD, 0xD6D6D6, 0xFFFFFF];
        static readonly double[] Values = [1.0, 0.8, 0.6, 0.5, 0.3];

        /// <summary>RGB for ACI 1–255 (others give black).</summary>
        public static uint ToRgb(int aci)
        {
            if (aci is >= 0 and <= 9) return Basic[aci];
            if (aci is >= 250 and <= 255) return Greys[aci - 250];
            if (aci is < 10 or > 249) return 0x000000;
            // 10–249: 24 hues 15° apart; within each group of ten, five brightness levels,
            // alternating full and half saturation.
            int g = (aci - 10) / 10, sub = (aci - 10) % 10;
            double h = g * 15, s = sub % 2 == 0 ? 1.0 : 0.5, v = Values[sub / 2];
            return Hsv(h, s, v);
        }

        static uint Hsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = (h / 60) switch
            {
                < 1 => (c, x, 0.0),
                < 2 => (x, c, 0.0),
                < 3 => (0.0, c, x),
                < 4 => (0.0, x, c),
                < 5 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            uint B(double f) => (uint)Math.Clamp(Math.Floor((f + m) * 255 + 1e-9), 0, 255); // AutoCAD truncates: ACI 16 = (127, 0, 0)
            return B(r) << 16 | B(g) << 8 | B(b);
        }
    }
}
