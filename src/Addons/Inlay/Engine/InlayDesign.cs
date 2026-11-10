using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>One material in the picture: the shapes on one layer.</summary>
/// <param name="LayerId">The layer its shapes were drawn on.</param>
/// <param name="Name">The layer's name, to start the wood's name from.</param>
/// <param name="Color">The layer's colour (0xRRGGBB), for the preview and its page.</param>
public sealed record Wood(Guid LayerId, string Name, uint Color);

/// <summary>A piece to cut: one joined-up area of one wood, as drawn (no kerf or fit yet).</summary>
/// <param name="Number">1, 2, 3… across the whole picture, top to bottom, left to right.</param>
/// <param name="Wood">Its wood, as an index into <see cref="InlayDesign.Woods"/>; −1 = the sheet the pieces go in.</param>
/// <param name="Area">Its outline and any holes (where other pieces sit), world mm, even-odd.</param>
public sealed record Piece(int Number, int Wood, PathGeometry Area)
{
    /// <summary>Where its number goes: the point inside it farthest from its edges, and how far that is.</summary>
    public (Vec2 At, double Room) Label { get; } = InsidePoint(Area);

    public Rect2 Bounds => Area.Bounds();

    /// <summary>
    /// The point inside the area farthest from its edges (sampled on a grid, then refined round the
    /// best), and that distance. The middle of a C is outside it, so its number goes in the C's thick part.
    /// </summary>
    public static (Vec2 At, double Room) InsidePoint(PathGeometry area)
    {
        var loops = area.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        var b = area.Bounds();
        if (loops.Count == 0 || b.IsEmpty) return (b.IsEmpty ? new Vec2(0, 0) : b.Center, 0);
        bool Inside(Vec2 p) => loops.Count(l => Geom.Inside(l, p)) % 2 == 1;
        double Room(Vec2 p)
        {
            double best = double.MaxValue;
            foreach (var l in loops)
                for (int i = 0; i < l.Count; i++)
                    best = Math.Min(best, DistToSegment(p, l[i], l[(i + 1) % l.Count]));
            return best;
        }
        Vec2 at = b.Center;
        double room = Inside(at) ? Room(at) : -1;
        double step = Math.Max(b.Width, b.Height) / 16;
        for (int round = 0; round < 4 && step > 1e-4; round++)
        {
            var centre = room >= 0 ? at : b.Center;
            int n = round == 0 ? 16 : 6;
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                {
                    var p = round == 0
                        ? new Vec2(b.MinX + (i + n + 0.5) * b.Width / (2 * n + 1), b.MinY + (j + n + 0.5) * b.Height / (2 * n + 1))
                        : new Vec2(centre.X + i * step / n, centre.Y + j * step / n);
                    if (!b.Contains(p) || !Inside(p)) continue;
                    double r = Room(p);
                    if (r > room) { room = r; at = p; }
                }
            step /= 3;
        }
        return (at, Math.Max(0, room));
    }

    static double DistToSegment(Vec2 p, Vec2 a, Vec2 c)
    {
        double dx = c.X - a.X, dy = c.Y - a.Y, len = dx * dx + dy * dy;
        double t = len < 1e-18 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }
}

/// <summary>
/// The picture the selected shapes make, as pieces: each shape covers what's under it (later shapes
/// are on top, as on the canvas), the shapes on one layer are one wood, and touching parts of one
/// wood join into one piece. So a sky drawn as a rectangle with a sun on top comes out as a sun and a
/// sky with a sun-shaped hole.
/// </summary>
public sealed class InlayDesign
{
    /// <summary>Areas smaller than this (mm²) are slivers left between shapes, not pieces.</summary>
    public const double SliverMm2 = 0.05;

    public required IReadOnlyList<Wood> Woods { get; init; }
    public required IReadOnlyList<Piece> Pieces { get; init; }
    /// <summary>Everything the pieces cover together: the pocket or hole they go in.</summary>
    public required PathGeometry All { get; init; }
    /// <summary>How many slivers were left out.</summary>
    public int Slivers { get; init; }
    /// <summary>How many of the selected shapes had no closed outline (lines), so aren't pieces.</summary>
    public int Open { get; init; }

    public Rect2 Bounds => All.Bounds();

    public bool IsEmpty => Pieces.Count == 0;

    public static readonly InlayDesign Empty = new() { Woods = [], Pieces = [], All = PathGeometry.Empty };

    /// <summary>The selected shapes as pieces (see the class).</summary>
    public static InlayDesign From(DocState s, IReadOnlySet<Guid> selection)
    {
        var shapes = s.Shapes.Where(x => selection.Contains(x.Id)).OfType<PathShape>().ToList();
        int open = 0;
        // Each shape's own area, minus everything above it.
        var covered = PathGeometry.Empty;
        var areas = new List<(Guid Layer, PathGeometry Area)>();
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            var fill = ShapeGeometry.Union([shapes[i].World]);
            if (fill.IsEmpty) { open++; continue; }
            var area = covered.IsEmpty ? fill : ShapeGeometry.Boolean(fill, covered, BoolOp.Subtract);
            covered = covered.IsEmpty ? fill : ShapeGeometry.Union([covered, fill]);
            if (!area.IsEmpty) areas.Add((shapes[i].LayerId, area));
        }
        // A wood per layer, in the Layers panel's order.
        var layerIds = areas.Select(a => a.Layer).Distinct()
            .OrderBy(id => s.Layers.FindIndex(l => l.Id == id) is var k && k >= 0 ? k : int.MaxValue).ToList();
        var woods = new List<Wood>();
        var raw = new List<(int Wood, PathGeometry Area)>();
        int slivers = 0;
        foreach (var id in layerIds)
        {
            var layer = s.FindLayer(id);
            int w = woods.Count;
            woods.Add(new Wood(id, layer?.Name ?? $"Wood {w + 1}", layer?.Color ?? 0x8B5A2B));
            var joined = ShapeGeometry.Union(areas.Where(a => a.Layer == id).Select(a => a.Area));
            foreach (var part in Components(joined))
            {
                if (ShapeGeometry.Area(part) < SliverMm2) { slivers++; continue; }
                raw.Add((w, part));
            }
        }
        var pieces = Number(raw);
        return new InlayDesign
        {
            Woods = woods,
            Pieces = pieces,
            All = covered.IsEmpty ? PathGeometry.Empty : ShapeGeometry.Union(pieces.Select(p => p.Area)),
            Slivers = slivers,
            Open = open,
        };
    }

    /// <summary>Pieces numbered in reading order: rows (by where they start, a row ≈ the typical piece's height), then left to right.</summary>
    public static List<Piece> Number(IReadOnlyList<(int Wood, PathGeometry Area)> raw, int first = 1)
    {
        if (raw.Count == 0) return [];
        var boxes = raw.Select(r => r.Area.Bounds()).ToList();
        var heights = boxes.Select(b => b.Height).Order().ToList();
        double row = Math.Max(0.5, heights[heights.Count / 2] * 0.6);
        var order = Enumerable.Range(0, raw.Count)
            .OrderBy(i => Math.Floor(boxes[i].Center.Y / row)).ThenBy(i => boxes[i].Center.X).ToList();
        return [.. order.Select((i, n) => new Piece(first + n, raw[i].Wood, raw[i].Area))];
    }

    /// <summary>
    /// The separate parts of an area: each outer outline with the holes straight inside it (an island
    /// in a hole is a part of its own).
    /// </summary>
    public static List<PathGeometry> Components(PathGeometry g)
    {
        var subs = g.Subpaths.Where(sp => sp.Closed && !sp.Segs.IsEmpty).ToList();
        var flat = subs.Select(sp => sp.Flatten(0.01)).ToList();
        int n = subs.Count;
        var boxes = flat.Select(Rect2.Bounds).ToArray();
        var area = flat.Select(f => f.Count < 3 ? 0 : Math.Abs(Geom.SignedArea(f))).ToArray();
        var depth = new int[n];
        var parent = Enumerable.Repeat(-1, n).ToArray();
        for (int i = 0; i < n; i++)
        {
            if (flat[i].Count < 3) continue;
            var probe = Probe(flat[i]);
            for (int j = 0; j < n; j++)
            {
                if (i == j || flat[j].Count < 3 || !boxes[j].Contains(boxes[i]) || area[j] <= area[i]) continue;
                if (!Geom.Inside(flat[j], probe)) continue;
                depth[i]++;
                if (parent[i] < 0 || area[j] < area[parent[i]]) parent[i] = j;
            }
        }
        var parts = new List<PathGeometry>();
        for (int i = 0; i < n; i++)
        {
            if (flat[i].Count < 3 || depth[i] % 2 != 0) continue;
            var loops = new List<Subpath> { subs[i] };
            for (int j = 0; j < n; j++)
                if (depth[j] % 2 == 1 && parent[j] == i) loops.Add(subs[j]);
            parts.Add(new PathGeometry(loops));
        }
        return parts;
    }

    /// <summary>A point on the loop that isn't one of its corners (corners of outlines from one union can touch).</summary>
    static Vec2 Probe(List<Vec2> loop)
    {
        var a = loop[0];
        var b = loop[1 % loop.Count];
        return new Vec2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }
}
