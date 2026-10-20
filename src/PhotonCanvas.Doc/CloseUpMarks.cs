using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// Where the close-up camera looks: a few dots burned where it can see them, found in a close-up
/// photo, tell exactly how the bed (mm) maps onto the camera's picture at this focus height. Ten
/// dots: a 3 × 3 grid and one more off one side, so the pattern can only be read one way round
/// (turned or mirrored, the camera still can't mistake one dot for another).
/// </summary>
public static class CloseUpMarks
{
    public const string LayerName = "Close-up marks";
    const string ShapePrefix = "Close-up mark ";

    /// <summary>The dots, in grid steps from the top-left one: the 3 × 3 grid, then the one that tells which way round it is.</summary>
    public static readonly Vec2[] Pattern =
    [
        new(0, 0), new(1, 0), new(2, 0),
        new(0, 1), new(1, 1), new(2, 1),
        new(0, 2), new(1, 2), new(2, 2),
        new(0.5, -0.6),
    ];

    public static bool IsMarksLayer(Layer l) => l.Name == LayerName;

    /// <summary>The dots' centres on the bed, centred at <paramref name="centre"/>, <paramref name="spacing"/> mm apart.</summary>
    public static IReadOnlyList<Vec2> Places(Vec2 centre, double spacing) =>
        [.. Pattern.Select(p => new Vec2(centre.X + (p.X - 1) * spacing, centre.Y + (p.Y - 1) * spacing))];

    /// <summary>
    /// Add the dots (filled circles <paramref name="dotMm"/> across) on their own engrave layer,
    /// replacing any earlier ones. Returns the new state and the dots' ids.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Dots) Add(DocState s, Vec2 centre, double spacing, double dotMm)
    {
        s = Remove(s);
        var layer = Layer.Default(s.Layers.Count, LayerMode.Fill) with { Name = LayerName };
        s = Ops.AddLayer(s, layer, 0);
        var places = Places(centre, spacing);
        var dots = places.Select((p, i) => (Shape)new PathShape
        {
            Name = $"{ShapePrefix}{i + 1}",
            LayerId = layer.Id,
            Geometry = PathGeometry.Ellipse(p.X, p.Y, dotMm / 2, dotMm / 2),
        }).ToList();
        return (Ops.AddShapes(s, dots), [.. dots.Select(d => d.Id)]);
    }

    /// <summary>The dots' centres on the bed as they are in the design now (they may have been moved), in pattern order; null when there are none.</summary>
    public static IReadOnlyList<Vec2>? Marks(DocState s)
    {
        if (s.Layers.FirstOrDefault(IsMarksLayer) is not { } layer) return null;
        var found = new Vec2?[Pattern.Length];
        foreach (var sh in s.ShapesFor(layer))
            if (sh.Name.StartsWith(ShapePrefix, StringComparison.Ordinal) && int.TryParse(sh.Name[ShapePrefix.Length..], out int n) && n >= 1 && n <= Pattern.Length)
                found[n - 1] = sh.Bounds.Center;
        return found.All(f => f is not null) ? [.. found.Select(f => f!.Value)] : null;
    }

    public static DocState Remove(DocState s)
    {
        var ids = s.Layers.Where(IsMarksLayer).Select(l => l.Id).ToHashSet();
        return ids.Count == 0 ? s : Ops.DeleteLayers(s, ids, null);
    }

    /// <summary>A dark spot found in a photo: its middle (photo pixels) and size (pixels).</summary>
    public sealed record Spot(Vec2 At, double Area);

    /// <summary>
    /// What a close-up photo told: the map from the bed (mm) to the photo (pixels), which dot was
    /// found where (null for one that wasn't), how far off the found dots are from the map (mm),
    /// and the part of the bed the camera sees.
    /// </summary>
    public sealed record Located(Homography MmToCamera, IReadOnlyList<Vec2?> Found, double ErrorMm, Rect2 Sees);

    /// <summary>
    /// Dark, roundish spots in a photo (burned dots on lighter material), darker than what's
    /// around them, so uneven light doesn't matter.
    /// </summary>
    public static List<Spot> FindSpots(RasterImage photo)
    {
        int f = Math.Max(1, (int)Math.Ceiling(photo.Width / 1000.0));
        int w = photo.Width / f, h = photo.Height / f;
        var lum = photo.Luminance();
        var g = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int yy = 0; yy < f; yy++)
                    for (int xx = 0; xx < f; xx++) sum += lum[(y * f + yy) * photo.Width + x * f + xx];
                g[y * w + x] = (float)(sum / (f * f));
            }
        // What's around each pixel: the average over a wide box (summed-area table).
        var sat = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double row = 0;
            for (int x = 0; x < w; x++)
            {
                row += g[y * w + x];
                sat[(y + 1) * (w + 1) + x + 1] = sat[y * (w + 1) + x + 1] + row;
            }
        }
        int rad = Math.Max(8, w / 12);
        var dark = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - rad), x1 = Math.Min(w, x + rad + 1), y0 = Math.Max(0, y - rad), y1 = Math.Min(h, y + rad + 1);
                double mean = (sat[y1 * (w + 1) + x1] - sat[y0 * (w + 1) + x1] - sat[y1 * (w + 1) + x0] + sat[y0 * (w + 1) + x0]) / ((x1 - x0) * (y1 - y0));
                dark[y * w + x] = g[y * w + x] < mean * 0.8;
            }
        // Join touching dark pixels into spots, keep the roundish ones.
        var spots = new List<Spot>();
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (!dark[start] || seen[start]) continue;
            seen[start] = true;
            stack.Push(start);
            int n = 0, minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            double sx = 0, sy = 0;
            bool edge = false;
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                n++; sx += x; sy += y;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge = true;
                if (x > 0 && dark[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                if (x < w - 1 && dark[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                if (y > 0 && dark[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                if (y < h - 1 && dark[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
            }
            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            if (edge || n < 12 || n > w * h / 50) continue;
            double aspect = (double)bw / bh, fill = (double)n / (bw * bh);
            if (aspect < 0.6 || aspect > 1.67 || fill < 0.55) continue;
            spots.Add(new Spot(new Vec2((sx / n + 0.5) * f, (sy / n + 0.5) * f), n * f * f));
        }
        return spots;
    }

    /// <summary>
    /// Find the dots (<paramref name="marksMm"/>, bed positions in pattern order) in a close-up
    /// photo. Null when they can't be told apart with certainty (fewer than seven found, or not the
    /// one that tells which way round).
    /// </summary>
    public static Located? Locate(RasterImage photo, IReadOnlyList<Vec2> marksMm)
    {
        var spots = FindSpots(photo);
        // The likeliest ones: of all the spots, those about the size of most of the others.
        if (spots.Count > 60) spots = [.. spots.OrderByDescending(x => x.Area).Take(60)];
        var d = spots.Select(x => x.At).ToList();
        if (d.Count < 7) return null;
        double step = marksMm[0].Dist(marksMm[1]);
        (int Count, double Err, Vec2?[] Match)? best = null;
        (int A, int B)[] pairs = [(0, 1), (4, 5), (3, 4), (4, 7), (0, 4), (4, 8), (1, 4)];
        foreach (var (a, b) in pairs)
            for (int i = 0; i < d.Count; i++)
                for (int j = 0; j < d.Count; j++)
                {
                    if (i == j) continue;
                    foreach (bool mirror in new[] { false, true })
                    {
                        // The turn, scale and shift (perhaps mirrored) that puts dot a on spot i and dot b on spot j.
                        var map = Similarity(marksMm[a], marksMm[b], d[i], d[j], mirror);
                        double tol = 0.3 * step * map.Scale;
                        var match = new Vec2?[marksMm.Count];
                        int count = 0;
                        double err = 0;
                        for (int k = 0; k < marksMm.Count; k++)
                        {
                            var p = map.Apply(marksMm[k]);
                            int nearest = -1;
                            double nd = tol;
                            for (int q = 0; q < d.Count; q++)
                            {
                                double dd = p.Dist(d[q]);
                                if (dd < nd) { nd = dd; nearest = q; }
                            }
                            if (nearest < 0) continue;
                            match[k] = d[nearest];
                            count++;
                            err += nd;
                        }
                        if (best is null || count > best.Value.Count || (count == best.Value.Count && err < best.Value.Err))
                            best = (count, err, match);
                    }
                }
        if (best is not { } b2 || b2.Count < 7 || b2.Match[^1] is null) return null;
        var found = b2.Match;
        var pairsMm = Enumerable.Range(0, marksMm.Count).Where(k => found[k] is not null).Select(k => (marksMm[k], found[k]!.Value)).ToList();
        if (Homography.Fit(pairsMm) is not { } toCamera || Homography.Fit([.. pairsMm.Select(p => (p.Item2, p.Item1))]) is not { } toBed) return null;
        double errMm = pairsMm.Average(p => toBed.Apply(p.Item2).Dist(p.Item1));
        var sees = Inside([toBed.Apply(new Vec2(0, 0)), toBed.Apply(new Vec2(photo.Width, 0)), toBed.Apply(new Vec2(photo.Width, photo.Height)), toBed.Apply(new Vec2(0, photo.Height))]);
        return new Located(toCamera, found, errMm, sees);
    }

    /// <summary>
    /// The biggest straight (unturned) rectangle round the middle of a camera picture's outline on the
    /// bed (a turned quadrilateral), less a small margin: the part of the bed that's surely in the picture.
    /// </summary>
    public static Rect2 Inside(IReadOnlyList<Vec2> quad)
    {
        var box = Rect2.Bounds(quad);
        var c = new Vec2(quad.Average(p => p.X), quad.Average(p => p.Y));
        bool In(Vec2 p) => Geom.Inside([.. quad], p);
        // Shrink the outline's box about the middle until its corners are all inside, keeping its shape.
        double hw = box.Width / 2, hh = box.Height / 2;
        for (double k = 1; k > 0.05; k -= 0.01)
        {
            var r = new Rect2(c.X - hw * k, c.Y - hh * k, c.X + hw * k, c.Y + hh * k);
            if (In(r.TopLeft) && In(new Vec2(r.MaxX, r.MinY)) && In(r.BottomRight) && In(new Vec2(r.MinX, r.MaxY)))
            {
                // Then widen and heighten it each as far as it goes.
                double grow = Math.Min(hw, hh) * 0.01;
                while (In(new Vec2(r.MinX - grow, r.MinY)) && In(new Vec2(r.MinX - grow, r.MaxY)) && In(new Vec2(r.MaxX + grow, r.MinY)) && In(new Vec2(r.MaxX + grow, r.MaxY)))
                    r = new Rect2(r.MinX - grow, r.MinY, r.MaxX + grow, r.MaxY);
                while (In(new Vec2(r.MinX, r.MinY - grow)) && In(new Vec2(r.MaxX, r.MinY - grow)) && In(new Vec2(r.MinX, r.MaxY + grow)) && In(new Vec2(r.MaxX, r.MaxY + grow)))
                    r = new Rect2(r.MinX, r.MinY - grow, r.MaxX, r.MaxY + grow);
                return r.Inflate(-Math.Min(r.Width, r.Height) * 0.05);
            }
        }
        return new Rect2(c.X, c.Y, c.X, c.Y);
    }

    /// <summary>A turn, scale and shift (mirrored or not) of the plane, as complex multiplication.</summary>
    readonly record struct Sim(double Re, double Im, double Tx, double Ty, bool Mirror)
    {
        public double Scale => Math.Sqrt(Re * Re + Im * Im);
        public Vec2 Apply(Vec2 p)
        {
            double y = Mirror ? -p.Y : p.Y;
            return new Vec2(Re * p.X - Im * y + Tx, Im * p.X + Re * y + Ty);
        }
    }

    static Sim Similarity(Vec2 m1, Vec2 m2, Vec2 d1, Vec2 d2, bool mirror)
    {
        double ay = mirror ? -m1.Y : m1.Y, by = mirror ? -m2.Y : m2.Y;
        double mx = m2.X - m1.X, my = by - ay, dx = d2.X - d1.X, dy = d2.Y - d1.Y;
        double den = mx * mx + my * my;
        if (den < 1e-12) return new Sim(1, 0, 0, 0, mirror);
        // (dx + i dy) / (mx + i my)
        double re = (dx * mx + dy * my) / den, im = (dy * mx - dx * my) / den;
        return new Sim(re, im, d1.X - (re * m1.X - im * ay), d1.Y - (im * m1.X + re * ay), mirror);
    }
}
