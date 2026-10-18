using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Import;

/// <summary>
/// HPGL / PLT plotter files (vinyl cutters, CorelDRAW and Inkscape exports): pen up / pen down
/// moves in plotter units of 0.025 mm. Each pen becomes its own layer colour.
/// </summary>
public static class HpglImport
{
    static readonly uint[] PenColours = [0x000000, 0x000000, 0xE53935, 0x43A047, 0x1E88E5, 0xFDD835, 0x8E24AA, 0x00ACC1, 0xFB8C00];

    public static ImportResult Load(string path) => Parse(File.ReadAllText(path));

    public static ImportResult Parse(string text)
    {
        const double Unit = 0.025; // mm per plotter unit
        var paths = new List<ImportedPath>();
        var warnings = new List<string>();
        var current = new List<Vec2>();
        int pen = 1;
        bool down = false, relative = false;
        var pos = new Vec2(0, 0);
        void Flush()
        {
            if (current.Count >= 2)
            {
                bool closed = current[0].Dist(current[^1]) < 1e-6 && current.Count > 3;
                var pts = closed ? current.Take(current.Count - 1).ToList() : current;
                paths.Add(new ImportedPath(PathGeometry.Polyline(pts, closed), PenColours[Math.Clamp(pen, 0, PenColours.Length - 1)], $"Pen {pen}"));
            }
            current = [];
        }
        foreach (var raw in text.Split(';', '\n'))
        {
            var cmd = raw.Trim();
            if (cmd.Length < 2) continue;
            string op = cmd[..2].ToUpperInvariant();
            var nums = cmd[2..].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).Where(double.IsFinite).ToList();
            switch (op)
            {
                case "IN": Flush(); pos = new(0, 0); down = false; relative = false; break;
                case "SP": Flush(); pen = nums.Count > 0 ? (int)nums[0] : 1; break;
                case "PA": relative = false; Move(nums); break;
                case "PR": relative = true; Move(nums); break;
                case "PU": Flush(); down = false; Move(nums); break;
                case "PD":
                    if (!down) { Flush(); current.Add(Mm(pos)); }
                    down = true;
                    Move(nums);
                    break;
                case "CI":
                    // A circle round the current point.
                    if (nums.Count > 0)
                    {
                        Flush();
                        var c = Mm(pos);
                        double r = nums[0] * Unit;
                        paths.Add(new ImportedPath(PathGeometry.Ellipse(c.X, c.Y, r, r), PenColours[Math.Clamp(pen, 0, PenColours.Length - 1)], $"Pen {pen}"));
                    }
                    break;
            }
        }
        Flush();
        if (paths.Count == 0) warnings.Add("No lines found in the plotter file.");
        return new ImportResult(paths, [], warnings);

        // Plotter Y points up: flipped so the drawing reads the right way round.
        Vec2 Mm(Vec2 p) => new(p.X * Unit, -p.Y * Unit);
        void Move(List<double> nums)
        {
            for (int i = 0; i + 1 < nums.Count; i += 2)
            {
                var p = new Vec2(nums[i], nums[i + 1]);
                pos = relative ? pos + p : p;
                if (down) current.Add(Mm(pos));
            }
        }
    }
}

/// <summary>
/// LightBurn projects (.lbrn2, .lbrn): rectangles, ellipses, paths, groups, pictures and text
/// (from the outline LightBurn keeps with it, or rebuilt from the font). Each of its layers
/// (cut settings) becomes a layer here, with its name; the settings themselves aren't converted.
/// </summary>
public static class LbrnImport
{
    // LightBurn's layer colours, by index.
    static readonly uint[] Palette =
    [
        0x000000, 0x0000FF, 0xFF0000, 0x00E000, 0xD0D000, 0xFF8000, 0x00E0E0, 0xFF00FF, 0xB4B4B4, 0x0000A0,
        0xA00000, 0x00A000, 0xA0A000, 0xC08000, 0x00A0FF, 0xA000A0, 0x808080, 0x7D87B9, 0xBB7784, 0x4A6FE3,
        0xD33F6A, 0x8CD78C, 0xF0B98D, 0xF6C4E1, 0xFA9ED4, 0x500A78, 0xB45A00, 0x004754, 0x86FA88, 0xFFDB66,
    ];

    public static ImportResult Load(string path) => Parse(File.ReadAllText(path));

    public static ImportResult Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("This isn't a LightBurn project.");
        if (!root.Name.LocalName.StartsWith("LightBurnProject", StringComparison.OrdinalIgnoreCase)) throw new FormatException("This isn't a LightBurn project.");
        var paths = new List<ImportedPath>();
        var images = new List<ImportedImage>();
        var warnings = new List<string>();
        var names = new Dictionary<uint, string>();
        foreach (var cs in root.Elements("CutSetting").Concat(root.Elements("CutSetting_Img")))
        {
            int index = Int(cs.Element("index")?.Attribute("Value")?.Value, 0);
            var name = cs.Element("name")?.Attribute("Value")?.Value ?? $"C{index:00}";
            names[Colour(index)] = name;
        }
        foreach (var shape in root.Elements("Shape")) Read(shape, Affine.Identity);
        return new ImportResult(paths, images, warnings) { LayerNames = names };

        void Read(XElement s, Affine parent)
        {
            var t = XForm(s).Then(parent);
            var type = s.Attribute("Type")?.Value ?? "";
            int cut = Int(s.Attribute("CutIndex")?.Value, 0);
            uint colour = Colour(cut);
            // Y up in LightBurn: flipped at the end.
            var flip = t.Then(Affine.Scale(1, -1));
            switch (type)
            {
                case "Group":
                    foreach (var c in s.Element("Children")?.Elements("Shape") ?? []) Read(c, t);
                    break;
                case "Rect":
                {
                    double w = D(s, "W"), h = D(s, "H"), r = D(s, "Cr");
                    var g = r > 0 ? PathGeometry.RoundRect(-w / 2, -h / 2, w, h, r, r) : PathGeometry.Rect(-w / 2, -h / 2, w, h);
                    paths.Add(new(g.Transform(flip), colour, "Rectangle"));
                    break;
                }
                case "Ellipse":
                    paths.Add(new(PathGeometry.Ellipse(0, 0, D(s, "Rx"), D(s, "Ry")).Transform(flip), colour, "Ellipse"));
                    break;
                case "Path":
                    if (PathOf(s) is { } pg) paths.Add(new(pg.Transform(flip), colour, "Path"));
                    break;
                case "Text":
                {
                    // LightBurn keeps the outline it drew with the text: use it if it's there.
                    var backup = s.Element("BackupPath");
                    if (backup is not null && PathOf(backup) is { } bp)
                    {
                        paths.Add(new(bp.Transform(XForm(backup).Then(flip)), colour, $"Text “{s.Attribute("Str")?.Value}”"));
                        break;
                    }
                    var str = s.Attribute("Str")?.Value ?? "";
                    var font = (s.Attribute("Font")?.Value ?? "Arial").Split(',')[0];
                    double h = D(s, "H") > 0 ? D(s, "H") : 10;
                    var g = TextLayout.Build(str, new TextStyle(font, h));
                    // Text sits on its baseline at the origin in LightBurn; ours has its top at 0.
                    var tb = Rect2.Bounds(g.Flatten(0.05).SelectMany(f => f.Points).ToList());
                    if (!tb.IsEmpty) paths.Add(new(g.Transform(Affine.Translate(0, -tb.MaxY).Then(Affine.Scale(1, -1)).Then(flip)), colour, $"Text “{str}”"));
                    break;
                }
                case "Bitmap":
                {
                    var data = s.Attribute("Data")?.Value;
                    if (string.IsNullOrEmpty(data)) break;
                    double w = D(s, "W"), h = D(s, "H");
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(data); } catch (FormatException) { warnings.Add("A picture couldn't be read."); break; }
                    // Pictures are centred on their origin with Y up; ours start at the top-left with Y down.
                    images.Add(new ImportedImage(bytes, new Vec2(w, h), Affine.Scale(1, -1).Then(Affine.Translate(-w / 2, h / 2)).Then(flip), "Picture", colour));
                    break;
                }
                default:
                    if (type.Length > 0) warnings.Add($"A LightBurn “{type}” shape was skipped.");
                    break;
            }
        }
    }

    static uint Colour(int cutIndex) => Palette[Math.Clamp(cutIndex, 0, Palette.Length - 1)];

    static double D(XElement s, string name) =>
        double.TryParse(s.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static int Int(string? v, int fallback) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    /// <summary>The shape's transform, "a b c d e f" (x' = a x + c y + e, y' = b x + d y + f).</summary>
    static Affine XForm(XElement s)
    {
        var parts = (s.Element("XForm")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        return parts.Length == 6 && parts.All(double.IsFinite) ? new Affine(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]) : Affine.Identity;
    }

    /// <summary>
    /// A path from LightBurn's vertex list ("V x y c0x … c0y … c1x … c1y …", control points
    /// optional) and its primitive list ("L a b" lines, "B a b" curves from a's c0 to b's c1).
    /// </summary>
    static PathGeometry? PathOf(XElement s)
    {
        var vl = s.Element("VertList")?.Value;
        if (string.IsNullOrWhiteSpace(vl)) return null;
        var verts = new List<(Vec2 P, Vec2? C0, Vec2? C1)>();
        foreach (var chunk in vl.Split('V', StringSplitOptions.RemoveEmptyEntries))
        {
            var main = chunk.Split('c')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (main.Length < 2) continue;
            var p = new Vec2(Num(main[0]), Num(main[1]));
            Vec2? c0 = Ctl(chunk, "c0x", "c0y"), c1 = Ctl(chunk, "c1x", "c1y");
            verts.Add((p, c0, c1));
        }
        if (verts.Count < 2) return null;
        var prim = (s.Element("PrimList")?.Value ?? "").Trim();
        var subs = new List<Subpath>();
        if (prim.Length == 0 || prim.StartsWith("LineClosed", StringComparison.Ordinal) || prim.StartsWith("Line", StringComparison.Ordinal) && !prim.Contains(' '))
        {
            bool closed = prim.StartsWith("LineClosed", StringComparison.Ordinal);
            return PathGeometry.Polyline([.. verts.Select(v => v.P)], closed);
        }
        // Primitives joined into subpaths where each starts at the previous one's end.
        var b = ImmutableArray.CreateBuilder<Seg>();
        int? first = null, last = null;
        void Close(bool closed)
        {
            if (first is { } f && b.Count > 0) subs.Add(new Subpath(verts[f].P, b.ToImmutable(), closed));
            b.Clear();
            first = last = null;
        }
        foreach (var tok in prim.Split(['L', 'B'], StringSplitOptions.RemoveEmptyEntries).Zip(PrimKinds(prim)))
        {
            var ij = tok.First.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ij.Length < 2) continue;
            int i = Int(ij[0], -1), j = Int(ij[1], -1);
            if (i < 0 || j < 0 || i >= verts.Count || j >= verts.Count) continue;
            if (last != i) Close(false);
            first ??= i;
            var vj = verts[j];
            b.Add(tok.Second == 'B' && verts[i].C0 is { } c0 && vj.C1 is { } c1 ? Seg.Cubic(c0, c1, vj.P) : Seg.Line(vj.P));
            last = j;
            if (j == first) Close(true);
        }
        Close(false);
        return new PathGeometry(subs);

        static double Num(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        static Vec2? Ctl(string chunk, string kx, string ky)
        {
            int ix = chunk.IndexOf(kx, StringComparison.Ordinal), iy = chunk.IndexOf(ky, StringComparison.Ordinal);
            if (ix < 0 || iy < 0) return null;
            string Read(int at) => new([.. chunk[at..].TakeWhile(ch => char.IsDigit(ch) || ch is '.' or '-' or 'e' or 'E' or '+')]);
            return new Vec2(Num(Read(ix + kx.Length)), Num(Read(iy + ky.Length)));
        }
    }

    static IEnumerable<char> PrimKinds(string prim) => prim.Where(c => c is 'L' or 'B');
}

/// <summary>
/// EPS and PostScript files, through Ghostscript when it's installed on this computer (it isn't
/// shipped with us): turned into a PDF in a temporary folder, then read like any PDF. Ghostscript
/// runs in its safe mode, so the file can't read or write anything else.
/// </summary>
public static class EpsImport
{
    /// <summary>Ghostscript's command-line program, or null when it isn't installed.</summary>
    public static string? FindGhostscript()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                var gs = Path.Combine(root, "gs");
                if (!Directory.Exists(gs)) continue;
                // The newest version first.
                foreach (var dir in Directory.GetDirectories(gs).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                    foreach (var exe in new[] { "gswin64c.exe", "gswin32c.exe" })
                        if (File.Exists(Path.Combine(dir, "bin", exe))) return Path.Combine(dir, "bin", exe);
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                     .Concat(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"]))
            foreach (var name in OperatingSystem.IsWindows() ? new[] { "gswin64c.exe", "gswin32c.exe" } : ["gs"])
                if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        return null;
    }

    public static ImportResult Load(string path)
    {
        var gs = FindGhostscript() ?? throw new FormatException("EPS files need Ghostscript (free, from ghostscript.com). Install it, then import again — or save the file as PDF or SVG.");
        var dir = Directory.CreateTempSubdirectory("pc-eps-");
        try
        {
            var pdf = Path.Combine(dir.FullName, "drawing.pdf");
            var psi = new System.Diagnostics.ProcessStartInfo(gs) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            // Safe mode; the drawing's own bounding box as the page; vector output.
            foreach (var a in new[] { "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dEPSCrop", "-sDEVICE=pdfwrite", $"-sOutputFile={pdf}", path })
                psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new FormatException("Ghostscript didn't start.");
            var err = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new FormatException("Ghostscript took too long with this file."); }
            if (p.ExitCode != 0 || !File.Exists(pdf)) throw new FormatException($"Ghostscript couldn't read this file. {err.Result.Trim()}".Trim());
            return PdfImport.Load(pdf);
        }
        finally
        {
            try { dir.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
