using System.Globalization;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Tracing;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// Designs you upload to engrave as an ornament or along a border: read from a drawing (SVG, DXF,
/// PDF, Illustrator, EPS and the program's other formats) or traced from a picture (PNG, JPEG…),
/// then kept small (about 1000 units on the longest side, y up) inside the accessory's settings,
/// and in a folder of their own so they can be picked again.
/// </summary>
public static class MarkArts
{
    /// <summary>Units on the longest side.</summary>
    public const double Box = 1000;
    /// <summary>Most points kept: more detail than an engraved ornament a few mm across can show.</summary>
    public const int MaxPoints = 6000;

    public static readonly IReadOnlyList<string> PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>Every file type a design can come from.</summary>
    public static IReadOnlyList<string> Extensions => [.. Importer.Extensions, .. PictureExtensions];

    /// <summary>
    /// A design from a file: a drawing's shapes (closed ones filled, open ones as lines), or a
    /// picture's dark parts traced. <paramref name="threshold"/> (0 to 1) is how dark counts, for pictures.
    /// </summary>
    /// <exception cref="FormatException">Nothing usable in it.</exception>
    public static MarkArt FromFile(string path, double threshold = 0.5)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        PathGeometry geometry;
        if (PictureExtensions.Contains(ext))
            geometry = Trace(File.ReadAllBytes(path), threshold);
        else
        {
            ImportResult r;
            try { r = Importer.Load(path); }
            catch (IOException e) { throw new FormatException($"Couldn't read \"{Path.GetFileName(path)}\": {e.Message}", e); }
            geometry = new PathGeometry(r.Paths.SelectMany(p => p.Geometry.Subpaths));
            // A drawing that's only a picture: trace the picture.
            if (geometry.Subpaths.Length == 0 && r.Images.Count > 0) geometry = Trace(r.Images[0].Encoded, threshold);
        }
        return FromGeometry(name, geometry);
    }

    static PathGeometry Trace(byte[] encoded, double threshold)
    {
        RasterImage img;
        try { img = RasterImage.Decode(encoded); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { throw new FormatException("This picture can't be read.", e); }
        var work = ImageTrace.Prepare(img);
        var o = new TraceOptions { Threshold = Math.Clamp(threshold, 0.05, 0.95), Smoothness = 2.5, MinAreaPx = Math.Max(12, work.Image.Width * work.Image.Height / 40000) };
        return ImageTrace.BlackAndWhite(work, o).Parts.FirstOrDefault()?.Geometry ?? PathGeometry.Empty;
    }

    /// <summary>A design from the program's paths (y down, any size).</summary>
    public static MarkArt FromGeometry(string name, PathGeometry g)
    {
        var (areas, lines) = Geo.FromGeometry(g, 0.002 * Math.Max(1, g.Bounds().Width + g.Bounds().Height));
        areas = Geo.Tidy(areas);
        if (areas.Count == 0 && lines.Count == 0) throw new FormatException("There's nothing in it to engrave: no shapes, lines or dark parts.");
        var all = new PathsD([.. areas, .. lines]);
        var b = Clipper.GetBounds(all);
        double w = b.right - b.left, h = b.bottom - b.top, k = Box / Math.Max(1e-9, Math.Max(w, h));
        if (!(k > 0) || !double.IsFinite(k)) throw new FormatException("The design has no size.");
        PathD Norm(PathD p) => [.. p.Select(q => new PointD((q.x - b.left) * k, (q.y - b.top) * k))];
        var na = new PathsD(areas.Select(Norm));
        var nl = new PathsD(lines.Select(Norm));
        // Simplified until it's small enough to keep with the settings.
        for (double eps = 0.5; ; eps *= 1.6)
        {
            var sa = Geo.Tidy(Clipper.SimplifyPaths(na, eps, true)).Where(p => Math.Abs(Clipper.Area(p)) > eps * eps).ToList();
            var sl = Clipper.SimplifyPaths(nl, eps, false).Where(p => p.Count >= 2).ToList();
            if (sa.Sum(p => p.Count) + sl.Sum(p => p.Count) <= MaxPoints || eps > 20)
                return new MarkArt { Name = Clean(name), Areas = Write(sa, true), Lines = Write(sl, false), Aspect = Math.Clamp(w / Math.Max(1e-9, h), 0.02, 50) };
        }
    }

    static string Clean(string name)
    {
        name = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        return name.Length == 0 ? "Design" : name.Length > 60 ? name[..60] : name;
    }

    static string Write(IEnumerable<PathD> paths, bool closed)
    {
        var sb = new StringBuilder();
        foreach (var p in paths)
        {
            if (p.Count < 2) continue;
            if (sb.Length > 0) sb.Append(' ');
            for (int i = 0; i < p.Count; i++)
            {
                sb.Append(i == 0 ? 'M' : i == 1 ? " L" : " ");
                sb.Append(Math.Round(p[i].x).ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Math.Round(p[i].y).ToString(CultureInfo.InvariantCulture));
            }
            if (closed) sb.Append(" Z");
        }
        return sb.ToString();
    }

    /// <summary>Paths back from the text written by <see cref="Write"/> (M, L, Z and numbers).</summary>
    public static PathsD Read(string? data)
    {
        var o = new PathsD();
        if (string.IsNullOrWhiteSpace(data)) return o;
        PathD? cur = null;
        double? x = null;
        foreach (var tok in data.Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok;
            while (t.Length > 0 && char.IsLetter(t[0]))
            {
                char c = char.ToUpperInvariant(t[0]);
                t = t[1..];
                if (c == 'M') { if (cur is { Count: >= 2 }) o.Add(cur); cur = []; x = null; }
                else if (c == 'Z') { if (cur is { Count: >= 2 }) o.Add(cur); cur = null; x = null; }
            }
            if (t.Length == 0) continue;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            if (x is null) x = v;
            else { (cur ??= []).Add(new PointD(x.Value, v)); x = null; }
        }
        if (cur is { Count: >= 2 }) o.Add(cur);
        return o;
    }

    /// <summary>The design's areas fitted to a −1…1 box (like a built-in ornament).</summary>
    public static PathsD Shape(MarkArt art) => Ornaments.Fit(Geo.Tidy(new PathsD(Read(art.Areas).Where(p => p.Count >= 3))));

    /// <summary>The design's areas and lines fitted to the same −1…1 box.</summary>
    public static (PathsD Areas, List<PathD> Lines) Unit(MarkArt art)
    {
        var a = Geo.Tidy(new PathsD(Read(art.Areas).Where(p => p.Count >= 3)));
        var l = Read(art.Lines);
        var all = new PathsD([.. a, .. l]);
        if (all.Count == 0) return ([], []);
        var b = Clipper.GetBounds(all);
        double k = 2 / Math.Max(1e-9, Math.Max(b.right - b.left, b.bottom - b.top)), cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
        PointD F(PointD q) => new((q.x - cx) * k, (q.y - cy) * k);
        return (Geo.Map(a, F), Geo.Map(l, F));
    }
}

/// <summary>The designs you've uploaded, one small JSON file each in the add-on's data folder.</summary>
public sealed class ArtStore(string folder)
{
    public string Folder { get; } = folder;

    static JsonSerializerOptions Options => GameKitJson.Options;

    public List<MarkArt> All()
    {
        var o = new List<MarkArt>();
        try
        {
            if (!Directory.Exists(Folder)) return o;
            foreach (var f in Directory.GetFiles(Folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try { if (JsonSerializer.Deserialize<MarkArt>(File.ReadAllText(f), Options) is { } a && (a.Areas.Length > 0 || a.Lines.Length > 0)) o.Add(a); }
                catch (Exception e) when (e is JsonException or IOException or NotSupportedException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return [.. o.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Saves it, under a name not already taken; returns it as saved.</summary>
    public MarkArt Save(MarkArt art)
    {
        Directory.CreateDirectory(Folder);
        var taken = All().Select(a => a.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        string name = art.Name;
        for (int i = 2; taken.Contains(name); i++) name = $"{art.Name} {i}";
        art = art with { Name = name };
        var path = PathFor(name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(art, Options));
        File.Move(path + ".tmp", path, overwrite: true);
        return art;
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    string PathFor(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string([.. name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim('.', ' ');
        return Path.Combine(Folder, (s.Length == 0 ? "design" : s) + ".json");
    }
}
