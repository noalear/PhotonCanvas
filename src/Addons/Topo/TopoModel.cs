using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Addon.Topo;

public enum TopoFrame { Rectangle, Circle, Hexagon }

/// <summary>Where the bottom layer's height is: the lowest point in the map, sea level (the sea stays flat), or a height chosen.</summary>
public enum TopoBottom { Lowest, SeaLevel, Height }

public sealed record TopoOptions
{
    public TopoFrame Frame { get; init; } = TopoFrame.Rectangle;
    /// <summary>The map's width; a circle's diameter; a hexagon's, corner to corner.</summary>
    public double WidthMm { get; init; } = 150;
    /// <summary>Front to back (rectangles only: a circle is as deep as it's wide, a hexagon √3/2 of its width).</summary>
    public double DepthMm { get; init; } = 150;
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Layers for the land above the bottom.</summary>
    public int Layers { get; init; } = 10;
    /// <summary>Whole layers of the map's shape underneath, the board it all stands on.</summary>
    public int BaseLayers { get; init; } = 1;
    public TopoBottom Bottom { get; init; } = TopoBottom.Lowest;
    /// <summary>The bottom's height when <see cref="Bottom"/> is <see cref="TopoBottom.Height"/>, in the heights' own units.</summary>
    public double BottomHeight { get; init; }
    /// <summary>How much the heights are smoothed first, in points of the model's grid (0 = not at all).</summary>
    public double Smooth { get; init; } = 1;
    /// <summary>Points along the model's longer side.</summary>
    public int Detail { get; init; } = 300;

    public double ModelDepthMm => Frame switch
    {
        TopoFrame.Circle => WidthMm,
        TopoFrame.Hexagon => WidthMm * Math.Sqrt(3) / 2,
        _ => DepthMm,
    };

    /// <summary>Width over depth: the area on the ground has the same shape.</summary>
    public double Aspect => WidthMm / Math.Max(1e-9, ModelDepthMm);
}

/// <summary>The part of a <see cref="DemGrid"/> the map shows: its middle in metres from the grid's north-west corner (east, south), and its size.</summary>
public readonly record struct TopoArea(double X, double Y, double WidthM, double HeightM)
{
    public double Left => X - WidthM / 2;
    public double Top => Y - HeightM / 2;
}

/// <summary>
/// The map's heights sampled over its area, smoothed, with the bottom and the height of a layer
/// worked out: everything needed to see the layers, make the solid to slice, or draw contours.
/// Model coordinates are millimetres with Y running north (as a 3D model's are); the slicer turns
/// them onto the bed.
/// </summary>
public sealed class TopoSurface
{
    public int Nx { get; }
    public int Ny { get; }
    /// <summary>Heights, row by row from the north, smoothed; missing ones filled with the lowest.</summary>
    public float[] Heights { get; }
    /// <summary>Which points are inside the map's shape.</summary>
    public bool[] Inside { get; }
    public TopoOptions Options { get; }
    public TopoArea Area { get; }
    /// <summary>The lowest and highest heights inside the shape.</summary>
    public double Low { get; }
    public double High { get; }
    /// <summary>Where the first land layer starts from, and the height a layer stands for.</summary>
    public double Start { get; }
    public double Step { get; }
    /// <summary>How much taller than true the hills come out (null when the ground size or the units aren't known).</summary>
    public double? Exaggeration { get; }
    public HeightUnits Units { get; }
    /// <summary>The map's outline, millimetres, Y north, anticlockwise.</summary>
    public IReadOnlyList<Vec2> Outline { get; }

    double W => Options.WidthMm;
    double D => Options.ModelDepthMm;
    double T => Options.ThicknessMm;

    TopoSurface(int nx, int ny, float[] h, bool[] inside, TopoOptions o, TopoArea a, double low, double high, double start, double step, double? ex, HeightUnits units, IReadOnlyList<Vec2> outline)
    {
        Nx = nx; Ny = ny; Heights = h; Inside = inside; Options = o; Area = a; Low = low; High = high; Start = start; Step = step; Exaggeration = ex; Units = units; Outline = outline;
    }

    /// <summary>The largest area of the map's shape that fits on the grid, in its middle.</summary>
    public static TopoArea Fit(DemGrid g, double aspect)
    {
        double w = g.WidthM, h = w / aspect;
        if (h > g.HeightM) { h = g.HeightM; w = h * aspect; }
        return new TopoArea(g.WidthM / 2, g.HeightM / 2, w, h);
    }

    /// <exception cref="ArgumentException">Nothing to make a map from there (no heights, or all flat; the message says).</exception>
    public static TopoSurface Make(DemGrid g, TopoArea a, TopoOptions o, CancellationToken ct = default)
    {
        if (!(o.WidthMm > 0) || !(o.ModelDepthMm > 0) || !(o.ThicknessMm > 0)) throw new ArgumentException("The map's size and the material's thickness must be above zero.");
        if (o.Layers < 1) throw new ArgumentException("There must be at least one layer for the land.");
        int detail = Math.Clamp(o.Detail, 20, 1000);
        double w = o.WidthMm, d = o.ModelDepthMm;
        int nx = w >= d ? detail : Math.Max(8, (int)Math.Round(detail * w / d));
        int ny = d >= w ? detail : Math.Max(8, (int)Math.Round(detail * d / w));
        var h = new float[nx * ny];
        double low = double.PositiveInfinity;
        Parallel.For(0, ny, j =>
        {
            double y = a.Top + j * a.HeightM / (ny - 1);
            for (int i = 0; i < nx; i++) h[j * nx + i] = (float)g.Sample(a.Left + i * a.WidthM / (nx - 1), y);
        });
        ct.ThrowIfCancellationRequested();
        foreach (var v in h) if (!float.IsNaN(v) && v < low) low = v;
        if (double.IsInfinity(low)) throw new ArgumentException("There are no heights in this area: move it over the land (or the file's heights).");
        for (int i = 0; i < h.Length; i++) if (float.IsNaN(h[i])) h[i] = (float)low;
        if (o.Smooth > 0.05) h = Blur(h, nx, ny, o.Smooth);
        ct.ThrowIfCancellationRequested();

        var outline = Shape(o);
        var inward = new Inward(outline);
        var inside = new bool[nx * ny];
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var p = new Vec2(i * w / (nx - 1), d - j * d / (ny - 1));
                if (!(inside[j * nx + i] = inward.Depth(p) >= -1e-6)) continue;
                double v = h[j * nx + i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        double start = o.Bottom switch { TopoBottom.SeaLevel => 0, TopoBottom.Height => o.BottomHeight, _ => lo };
        if (!(hi > start + 1e-6))
            throw new ArgumentException(o.Bottom == TopoBottom.Lowest
                ? "The area is flat: there's nothing to stack."
                : $"The whole area is at or below the bottom height ({start:0.#}): choose a lower one, or start from the lowest point.");
        double step = (hi - start) / o.Layers;
        double metres = g.Units switch { HeightUnits.Feet => 0.3048, _ => 1 };
        double? ex = g.Georeferenced && g.Units != HeightUnits.Levels ? (o.ThicknessMm / (step * metres)) / (w / a.WidthM) : null;
        return new TopoSurface(nx, ny, h, inside, o, a, lo, hi, start, step, ex, g.Units, outline);
    }

    /// <summary>The height where land layer <paramref name="layer"/> (1 = the first above the base) begins: the slicer cuts each layer through its middle.</summary>
    public double LevelOf(int layer) => Start + (layer - 0.5) * Step;

    /// <summary>The top layer at a height (0 = the base shows; up to <see cref="TopoOptions.Layers"/>).</summary>
    public int LayerAt(double height) => Math.Clamp((int)Math.Floor((height - Start) / Step + 0.5), 0, Options.Layers);

    /// <summary>A height's place in the model, millimetres above the bottom of the base.</summary>
    public double ZOf(double height) => Options.BaseLayers * T + Math.Max(0, height - Start) * T / Step;

    /// <summary>How tall the stack stands.</summary>
    public double StackMm => (Options.BaseLayers + Options.Layers) * T;

    public string UnitWord => Units switch { HeightUnits.Feet => "ft", HeightUnits.Levels => "", _ => "m" };

    /// <summary>A height as shown: "1,240 m".</summary>
    public string Say(double height) => Units == HeightUnits.Levels ? $"level {height:0}" : $"{height:N0} {UnitWord}";

    /// <summary>The map's shape, millimetres, Y north, anticlockwise.</summary>
    public static List<Vec2> Shape(TopoOptions o)
    {
        double w = o.WidthMm, d = o.ModelDepthMm, cx = w / 2, cy = d / 2;
        switch (o.Frame)
        {
            case TopoFrame.Circle:
                {
                    int n = 192;
                    return [.. Enumerable.Range(0, n).Select(k => new Vec2(cx + w / 2 * Math.Cos(2 * Math.PI * k / n), cy + w / 2 * Math.Sin(2 * Math.PI * k / n)))];
                }
            case TopoFrame.Hexagon:
                // Flat top and bottom, corners at the left and right.
                return [.. Enumerable.Range(0, 6).Select(k => new Vec2(cx + w / 2 * Math.Cos(Math.PI / 3 * k), cy + w / 2 * Math.Sin(Math.PI / 3 * k)))];
            default:
                return [new(0, 0), new(w, 0), new(w, d), new(0, d)];
        }
    }

    /// <summary>A convex outline's edges as lines facing inwards, for asking many points how far inside they are.</summary>
    sealed class Inward
    {
        readonly double[] _nx, _ny, _c;

        public Inward(IReadOnlyList<Vec2> poly)
        {
            int n = poly.Count;
            _nx = new double[n]; _ny = new double[n]; _c = new double[n];
            for (int k = 0; k < n; k++)
            {
                var a = poly[k];
                var e = poly[(k + 1) % n] - a;
                double len = e.Length;
                // Left of an anticlockwise edge is inside.
                _nx[k] = -e.Y / len; _ny[k] = e.X / len;
                _c[k] = -(_nx[k] * a.X + _ny[k] * a.Y);
            }
        }

        public double Depth(Vec2 p)
        {
            double min = double.PositiveInfinity;
            for (int k = 0; k < _c.Length; k++)
            {
                double d = _nx[k] * p.X + _ny[k] * p.Y + _c[k];
                if (d < min) min = d;
            }
            return min;
        }
    }

    static float[] Blur(float[] src, int w, int h, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var k = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int i = -r; i <= r; i++) s += k[i + r] * src[y * w + Math.Clamp(x + i, 0, w - 1)];
                tmp[y * w + x] = (float)s;
            }
        });
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int i = -r; i <= r; i++) s += k[i + r] * tmp[Math.Clamp(y + i, 0, h - 1) * w + x];
                dst[y * w + x] = (float)s;
            }
        });
        return dst;
    }

    readonly record struct P(double X, double Y, double Z);

    /// <summary>
    /// The map as a closed solid: the land on top (cut to the map's shape), straight sides down to
    /// the bottom of the base, and a flat bottom. Faces point outwards, as the slicer needs.
    /// </summary>
    public List<Tri> Mesh(CancellationToken ct = default)
    {
        var tris = new List<Tri>(Nx * Ny * 2 + 4000);
        var walls = new List<(P A, P B)>();
        double sx = W / (Nx - 1), sy = D / (Ny - 1), cell = Math.Max(sx, sy);
        var outline = Outline;
        // Each point: how far inside the shape (to know which cells are well inside and need no cutting).
        var depth = new double[Nx * Ny];
        var inward = new Inward(outline);
        Parallel.For(0, Ny, j =>
        {
            for (int i = 0; i < Nx; i++) depth[j * Nx + i] = inward.Depth(new Vec2(i * sx, D - j * sy));
        });
        P At(int i, int j) => new(i * sx, D - j * sy, ZOf(Heights[j * Nx + i]));
        const double eps = 1e-6;
        void Emit(List<P> poly, bool edgeCheck)
        {
            for (int k = 1; k + 1 < poly.Count; k++) tris.Add(new Tri(V(poly[0]), V(poly[k]), V(poly[k + 1])));
            if (!edgeCheck) return;
            for (int k = 0; k < poly.Count; k++)
            {
                var a = poly[k];
                var b = poly[(k + 1) % poly.Count];
                if (OnSameEdge(outline, a, b, 1e-5 * Math.Max(W, D))) walls.Add((a, b));
            }
        }
        for (int j = 0; j + 1 < Ny; j++)
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i + 1 < Nx; i++)
            {
                double d00 = depth[j * Nx + i], d10 = depth[j * Nx + i + 1], d01 = depth[(j + 1) * Nx + i], d11 = depth[(j + 1) * Nx + i + 1];
                if (Math.Max(Math.Max(d00, d10), Math.Max(d01, d11)) < -2 * cell) continue; // well outside
                P p00 = At(i, j), p10 = At(i + 1, j), p01 = At(i, j + 1), p11 = At(i + 1, j + 1);
                // Anticlockwise seen from above (Y north).
                if (Math.Min(Math.Min(d00, d10), Math.Min(d01, d11)) > cell * 0.01)
                {
                    tris.Add(new Tri(V(p01), V(p11), V(p10)));
                    tris.Add(new Tri(V(p01), V(p10), V(p00)));
                    continue;
                }
                foreach (var t in new[] { new List<P> { p01, p11, p10 }, new List<P> { p01, p10, p00 } })
                {
                    var c = Clip(t, outline, eps);
                    if (c.Count >= 3) Emit(c, edgeCheck: true);
                }
            }
        }
        // The sides: down from each edge of the land along the outline, and the bottom under it all.
        double cxm = outline.Average(v => v.X), cym = outline.Average(v => v.Y);
        var mid = new V3(cxm, cym, 0);
        foreach (var (a, b) in walls)
        {
            V3 at = V(a), bt = V(b), ab = new(a.X, a.Y, 0), bb = new(b.X, b.Y, 0);
            tris.Add(new Tri(ab, bb, bt));
            tris.Add(new Tri(ab, bt, at));
            tris.Add(new Tri(mid, bb, ab));
        }
        return tris;

        static V3 V(P p) => new(p.X, p.Y, p.Z);
    }

    static bool OnSameEdge(IReadOnlyList<Vec2> poly, P a, P b, double tol)
    {
        for (int k = 0; k < poly.Count; k++)
        {
            var s = poly[k];
            var e = poly[(k + 1) % poly.Count] - s;
            double len = e.Length;
            double da = Math.Abs(e.Cross(new Vec2(a.X, a.Y) - s)) / len, db = Math.Abs(e.Cross(new Vec2(b.X, b.Y) - s)) / len;
            if (da > tol || db > tol) continue;
            // And going the outline's way (the land's edge runs anticlockwise too).
            if (e.Dot(new Vec2(b.X - a.X, b.Y - a.Y)) > 0) return true;
        }
        return false;
    }

    /// <summary>A triangle (or any convex polygon) cut to a convex outline; heights follow along the cut edges.</summary>
    static List<P> Clip(List<P> poly, IReadOnlyList<Vec2> outline, double eps)
    {
        var cur = poly;
        for (int k = 0; k < outline.Count && cur.Count > 0; k++)
        {
            var s = outline[k];
            var e = outline[(k + 1) % outline.Count] - s;
            double len = e.Length;
            double Dist(P p) => e.Cross(new Vec2(p.X, p.Y) - s) / len;
            var next = new List<P>(cur.Count + 2);
            for (int i = 0; i < cur.Count; i++)
            {
                P a = cur[i], b = cur[(i + 1) % cur.Count];
                double da = Dist(a), db = Dist(b);
                bool ina = da >= -eps, inb = db >= -eps;
                if (ina) next.Add(a);
                if (ina != inb)
                {
                    double t = da / (da - db);
                    next.Add(new P(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t));
                }
            }
            // Drop points that came out on top of each other.
            cur = [];
            foreach (var p in next)
                if (cur.Count == 0 || Math.Abs(cur[^1].X - p.X) + Math.Abs(cur[^1].Y - p.Y) > 1e-9) cur.Add(p);
            if (cur.Count > 1 && Math.Abs(cur[0].X - cur[^1].X) + Math.Abs(cur[0].Y - cur[^1].Y) <= 1e-9) cur.RemoveAt(cur.Count - 1);
        }
        return cur;
    }

    /// <summary>
    /// Contour lines to engrave flat: where the land crosses each height, as open lines (the parts
    /// that run along the map's own edge left out), millimetres, Y north.
    /// </summary>
    public List<List<Vec2>> Contours(IReadOnlyList<Tri> mesh, IEnumerable<double> heights)
    {
        var lines = new List<List<Vec2>>();
        var inward = new Inward(Outline);
        double tol = 0.02;
        foreach (var hgt in heights)
        {
            var region = StackSlicer.Section(mesh, ZOf(hgt));
            foreach (var sp in region.Subpaths)
            {
                var pts = sp.Flatten(0.05);
                if (pts.Count < 3) continue;
                // Start where the loop is off the edge, then cut it wherever it runs along the edge.
                int n = pts.Count;
                bool OnEdge(int i) => OnSameEdgeAny(Outline, inward, pts[i % n], pts[(i + 1) % n], tol);
                int first = Enumerable.Range(0, n).FirstOrDefault(i => !OnEdge(i), -1);
                if (first < 0) continue;
                var cur = new List<Vec2>();
                for (int s = 0; s <= n; s++)
                {
                    int i = (first + s) % n;
                    if (OnEdge(i) || s == n)
                    {
                        if (cur.Count > 0) { cur.Add(pts[i]); Keep(cur); cur = []; }
                        continue;
                    }
                    cur.Add(pts[i]);
                }
            }
        }
        return lines;

        // Points on top of each other dropped; stubs where a contour just touches the edge left out.
        void Keep(List<Vec2> l)
        {
            var clean = new List<Vec2>(l.Count);
            foreach (var p in l) if (clean.Count == 0 || clean[^1].Dist(p) > 1e-6) clean.Add(p);
            double len = 0;
            for (int i = 1; i < clean.Count; i++) len += clean[i - 1].Dist(clean[i]);
            if (clean.Count >= 2 && len >= 0.5) lines.Add(clean);
        }
    }

    static bool OnSameEdgeAny(IReadOnlyList<Vec2> poly, Inward inward, Vec2 a, Vec2 b, double tol)
    {
        for (int k = 0; k < poly.Count; k++)
        {
            var s = poly[k];
            var e = poly[(k + 1) % poly.Count] - s;
            double len = e.Length;
            if (Math.Abs(e.Cross(a - s)) / len <= tol && Math.Abs(e.Cross(b - s)) / len <= tol) return true;
        }
        // A circle's outline is many short edges: near the outline at both ends, close together, counts too.
        return inward.Depth(a) <= tol && inward.Depth(b) <= tol && a.Dist(b) < 3;
    }

    /// <summary>The model as a binary STL (millimetres), for a 3D printer or anything else.</summary>
    public static void SaveStl(IReadOnlyList<Tri> mesh, string path, string name)
    {
        using var f = new BinaryWriter(File.Create(path));
        var head = new byte[80];
        System.Text.Encoding.ASCII.GetBytes(("PhotonCanvas topo map " + name).PadRight(80)[..80]).CopyTo(head, 0);
        f.Write(head);
        f.Write((uint)mesh.Count);
        foreach (var t in mesh)
        {
            double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l > 0) { nx /= l; ny /= l; nz /= l; }
            foreach (var v in new[] { nx, ny, nz, t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z }) f.Write((float)v);
            f.Write((ushort)0);
        }
    }

    /// <summary>Contours and the outline laid on the bed (Y down, top-left at <paramref name="at"/>).</summary>
    public PathGeometry OnBed(IEnumerable<IReadOnlyList<Vec2>> lines, Vec2 at, bool closed) =>
        new([.. lines.Where(l => l.Count >= 2).Select(l => PathGeometry.Polyline(l.Select(p => new Vec2(at.X + p.X, at.Y + D - p.Y)).ToList(), closed).Subpaths[0])]);
}
