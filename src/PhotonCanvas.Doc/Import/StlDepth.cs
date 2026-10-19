using System.Globalization;
using System.Text;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc.Import;

/// <summary>Which way a 3D model is looked at to make a depth map.</summary>
public enum StlView { Top, Front, Side }

/// <summary>
/// 3D models (STL, binary or text) to depth maps for relief engraving: the model seen straight
/// on, the highest point white (the surface, not carved) down to the lowest black (deepest).
/// </summary>
public static class StlDepth
{
    public readonly record struct Triangle(Vec3 A, Vec3 B, Vec3 C);
    public readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>A 3D model file by its extension: STL (binary or text), OBJ, or STEP (turned into triangles within 0.05 mm of its exact shapes).</summary>
    /// <exception cref="FormatException">Not a readable model.</exception>
    public static List<Triangle> Load(string path, double stepToleranceMm = 0.05) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" => ParseObj(File.ReadAllText(path)),
        ".stp" or ".step" => FromStep(Step.StepMesh.Load(path, stepToleranceMm)),
        _ => Parse(File.ReadAllBytes(path)),
    };

    /// <summary>File types <see cref="Load"/> reads.</summary>
    public static readonly string[] Extensions = [".stl", ".obj", ".stp", ".step"];

    /// <summary>A STEP file's triangles as a model.</summary>
    public static List<Triangle> FromStep(Step.StepResult r)
    {
        if (r.Triangles.Count == 0)
            throw new FormatException(r.Problems.Count > 0 ? $"Nothing in this STEP file could be turned into a model: {r.Problems[0]}." : "There are no solid shapes in this STEP file.");
        return [.. r.Triangles.Select(t => new Triangle(new(t.A.X, t.A.Y, t.A.Z), new(t.B.X, t.B.Y, t.B.Z), new(t.C.X, t.C.Y, t.C.Z)))];
    }

    /// <summary>Wavefront OBJ: "v x y z" corners and "f a b c …" faces (faces with more corners are fanned into triangles).</summary>
    public static List<Triangle> ParseObj(string text)
    {
        var v = new List<Vec3>();
        var tris = new List<Triangle>();
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 && p[0] == "v")
                v.Add(new(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture)));
            else if (p.Length >= 4 && p[0] == "f")
            {
                // "7", "7/2", "7//3" or negative (counted from the end).
                var idx = p.Skip(1).Select(x => int.Parse(x.Split('/')[0], CultureInfo.InvariantCulture)).Select(i => i < 0 ? v.Count + i : i - 1).ToList();
                if (idx.Any(i => i < 0 || i >= v.Count)) throw new FormatException("The OBJ file has a face with a corner that doesn't exist.");
                for (int k = 1; k + 1 < idx.Count; k++) tris.Add(new(v[idx[0]], v[idx[k]], v[idx[k + 1]]));
            }
        }
        if (tris.Count == 0) throw new FormatException("The OBJ file has no faces.");
        return tris;
    }

    /// <exception cref="FormatException">Not a readable STL file.</exception>
    public static List<Triangle> Parse(byte[] data)
    {
        if (data.Length >= 84)
        {
            uint count = BitConverter.ToUInt32(data, 80);
            if (84 + (long)count * 50 == data.Length)
            {
                var tris = new List<Triangle>((int)count);
                for (int i = 0; i < count; i++)
                {
                    int o = 84 + i * 50 + 12;
                    Vec3 V(int k) => new(BitConverter.ToSingle(data, o + k * 12), BitConverter.ToSingle(data, o + k * 12 + 4), BitConverter.ToSingle(data, o + k * 12 + 8));
                    tris.Add(new(V(0), V(1), V(2)));
                }
                return tris;
            }
        }
        // Text STL: "vertex x y z" lines, three per facet.
        var text = Encoding.ASCII.GetString(data);
        if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase)) throw new FormatException("This isn't an STL file.");
        var verts = new List<Vec3>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 4) continue;
            verts.Add(new(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture)));
        }
        if (verts.Count < 3) throw new FormatException("The STL file has no triangles.");
        return [.. Enumerable.Range(0, verts.Count / 3).Select(i => new Triangle(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]))];
    }

    /// <summary>
    /// The model as a depth map, <paramref name="longSidePx"/> pixels along its longer side.
    /// Where the model isn't, the picture is white (left as the surface) or, with
    /// <paramref name="carveAround"/>, black (carved to the bottom, so the model stands proud).
    /// Also returns the model's size across the view (mm, in the model's own units).
    /// </summary>
    public static (RasterImage Depth, double WidthMm, double HeightMm, double DepthRange) Render(IReadOnlyList<Triangle> tris, StlView view, int longSidePx, bool carveAround = false)
    {
        // Looking along −Z for the top view, along +Y for the front, along +X for the side.
        Vec3 Map(Vec3 v) => view switch
        {
            StlView.Front => new(v.X, -v.Z, v.Y * -1),
            StlView.Side => new(v.Y, -v.Z, v.X),
            _ => new(v.X, -v.Y, v.Z),
        };
        var t = tris.Select(x => new Triangle(Map(x.A), Map(x.B), Map(x.C))).ToList();
        double x0 = t.Min(q => Math.Min(q.A.X, Math.Min(q.B.X, q.C.X))), x1 = t.Max(q => Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)));
        double y0 = t.Min(q => Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y))), y1 = t.Max(q => Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)));
        double z0 = t.Min(q => Math.Min(q.A.Z, Math.Min(q.B.Z, q.C.Z))), z1 = t.Max(q => Math.Max(q.A.Z, Math.Max(q.B.Z, q.C.Z)));
        double sw = Math.Max(1e-9, x1 - x0), sh = Math.Max(1e-9, y1 - y0);
        double k = longSidePx / Math.Max(sw, sh);
        int w = Math.Max(1, (int)Math.Round(sw * k)), h = Math.Max(1, (int)Math.Round(sh * k));
        var zbuf = new double[w * h];
        Array.Fill(zbuf, double.NegativeInfinity);
        foreach (var tri in t)
        {
            // Pixel coordinates.
            (double X, double Y, double Z) P(Vec3 v) => ((v.X - x0) * k, (v.Y - y0) * k, v.Z);
            var a = P(tri.A);
            var b = P(tri.B);
            var c = P(tri.C);
            double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(area) < 1e-12) continue;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) / area;
                    double w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -1e-9 || w1 < -1e-9 || w2 < -1e-9) continue;
                    double z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                    int i = y * w + x;
                    if (z > zbuf[i]) zbuf[i] = z;
                }
        }
        var rgba = new byte[w * h * 4];
        double range = Math.Max(1e-9, z1 - z0);
        for (int i = 0; i < w * h; i++)
        {
            double v = double.IsNegativeInfinity(zbuf[i]) ? (carveAround ? 0 : 1) : (zbuf[i] - z0) / range;
            byte g = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = g;
            rgba[i * 4 + 3] = 255;
        }
        return (new RasterImage(w, h, rgba), sw, sh, range);
    }
}
