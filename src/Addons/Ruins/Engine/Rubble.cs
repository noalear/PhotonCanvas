using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// Rubble: piles stacked from layers of the sheet, each a little smaller than the one under it, with
/// edges that follow whole bricks or stones (turned at random from layer to layer, as fallen stones lie)
/// and the texture etched on the part of each layer that shows; loose bricks or stones; and chunks of
/// broken wall to lay on the ground.
/// </summary>
public static class Rubble
{
    public const int MaxLoose = 60;
    public const int MaxChunks = 20;
    public const int MaxPiles = 20;

    public static void Make(RuinSpec s, List<Part> parts, List<View> views, List<Finding> findings)
    {
        var rnd = new Random(s.Seed * 7919 + 4243);
        int piles = Math.Clamp(s.Piles, 0, MaxPiles);
        for (int i = 0; i < piles; i++)
        {
            string name = piles == 1 ? "Rubble pile" : $"Rubble pile {i + 1}";
            // Each pile a little different in size, so a few of them don't look stamped out.
            double k = piles == 1 ? 1 : 0.85 + 0.3 * rnd.NextDouble();
            var layers = Pile(s, name, s.PileShape, s.PileWidthMm * k, s.PileDepthMm * k, s.PileHeightMm * (0.9 + 0.2 * rnd.NextDouble()), rnd, null, findings);
            if (layers.Count == 0) continue;
            parts.AddRange(layers);
            views.Add(new View($"{name}, from above", layers));
        }
        if (piles > 0)
        {
            int layers = Layers(s, s.PileHeightMm);
            findings.Add(new(Severity.Note, $"A pile is up to {layers} layers of {s.ThicknessMm:0.#} mm sheet: glue layer 1 at the bottom and each one on the marks scored on the one under it."));
        }
        Loose(s, parts, rnd, findings);
        ChunksOf(s, parts, rnd, findings);
    }

    public static int Layers(RuinSpec s, double height) => Math.Clamp((int)Math.Round(height / Math.Max(0.3, s.ThicknessMm)), 1, 40);

    /// <summary>
    /// The layers of one pile, bottom first. A heap is round; one against a wall has a straight back on
    /// y = 0 (it fills y ≥ 0); one in a corner fills x ≥ 0, y ≥ 0. With <paramref name="within"/>, every
    /// layer stays inside it (a base).
    /// </summary>
    public static List<Part> Pile(RuinSpec s, string name, PileShape shape, double width, double depth, double height, Random rnd, PathsD? within, List<Finding> findings)
    {
        double w = Math.Clamp(width, 4, 1000), d = Math.Clamp(depth, 3, 1000);
        double a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        int n = Layers(s, height);
        bool cellular = Rough.Cellular(s.Texture);
        double step = Math.Min(0.8, Math.Max(0.3, Math.Min(a, b) * 0.15));
        var shapeRnd = new Random(rnd.Next());
        double lump = Math.Min(w, d) * 0.12 + 0.4, wave = Math.Max(8, Math.Max(a, b) * 1.6);
        var keep = shape switch
        {
            PileShape.AgainstWall => new PathsD { Geo.Rect(-w - 10, 0, w + 10, d * 2 + 10) },
            PileShape.Corner => new PathsD { Geo.Rect(0, 0, w * 2 + 10, d * 2 + 10) },
            _ => null,
        };

        var areas = new List<(PathsD Area, TextureGrid Grid)>();
        PathsD? prev = null;
        for (int i = 0; i < n; i++)
        {
            // A heap's slope: each layer's reach shrinks like a mound's (the top's narrow).
            double t = (double)i / n, k = Math.Max(0.14, Math.Sqrt(1 - 0.92 * t));
            var (rx, ry, cx, cy) = shape switch
            {
                PileShape.AgainstWall => (w / 2 * k, d * k, 0.0, 0.0),
                PileShape.Corner => (w * k, d * k, 0.0, 0.0),
                _ => (w / 2 * k, d / 2 * k, 0.0, 0.0),
            };
            // The lower layers drift a little off the middle, so the heap leans.
            cx += (shapeRnd.NextDouble() - 0.5) * w * 0.05 * (1 - k);
            cy += shape == PileShape.Heap ? (shapeRnd.NextDouble() - 0.5) * d * 0.05 * (1 - k) : 0;
            var blob = new PathsD { Rough.Lumpy(Rough.Ellipse(cx, cy, rx, ry), Math.Min(lump * k + 0.3, Math.Min(rx, ry) * 0.35), wave, new Random(shapeRnd.Next())) };
            var grid = TextureGrid.Turned(s.TextureOf(s.Seed * 13 + i * 7 + rnd.Next(1000)), Geo.Bounds(blob), rnd.NextDouble() * 180, rnd.NextDouble() * a, rnd.NextDouble() * b);
            // Stones are picked inside the layer below (less half a stone), so few are cut short where they overhang its edge.
            var pick = prev is null ? blob : Geo.Intersect(blob, Geo.Offset(prev, -(step + Math.Min(a, b) * 0.35)));
            if (within is not null) pick = Geo.Intersect(pick, within);
            var area = cellular && grid.Cells.Count > 0 ? Rough.Snap(grid.Cells, pick) : pick;
            if (cellular) area = Rough.Close(area, Math.Max(grid.Groove * 2, Math.Min(a, b) * 0.2));
            if (keep is not null) area = Geo.Intersect(area, keep);
            if (prev is not null) area = Geo.Intersect(area, Geo.Offset(prev, -step));
            if (within is not null) area = Geo.Intersect(area, within);
            area = Rough.Largest(Rough.Open(Rough.FillHoles(area), 1.5));
            var box = Geo.Bounds(area);
            if (area.Count == 0 || Geo.Area(area) < Math.Max(6, a * b * 0.8) || Math.Min(box.right - box.left, box.bottom - box.top) < 2.5) break;
            areas.Add((area, grid));
            prev = area;
        }
        if (areas.Count == 0)
        {
            findings.Add(new(Severity.Warn, $"The {name.ToLowerInvariant()} is too small to make: make it wider or deeper."));
            return [];
        }

        var layers = new List<Part>();
        for (int i = 0; i < areas.Count; i++)
        {
            var (area, grid) = areas[i];
            var above = i + 1 < areas.Count ? areas[i + 1].Area : null;
            var shows = above is null ? area : Geo.Subtract(area, above);
            var (etched, lines) = grid.In(shows, s.Etch);
            if (s.MarkLayers && above is not null) lines.AddRange(Geo.ClipLines(above.Select(Geo.Loop), Geo.Offset(area, 0.01)));
            string label = areas.Count == 1 ? name : $"{name} layer {i + 1}";
            layers.Add(new Part(label, area, etched, lines, 1));
        }
        return layers;
    }

    /// <summary>Loose bricks or stones, the texture's own (pieces the same size are counted together); rough chips for textures without stones.</summary>
    static void Loose(RuinSpec s, List<Part> parts, Random rnd, List<Finding> findings)
    {
        int n = Math.Clamp(s.LooseStones, 0, MaxLoose);
        if (n == 0) return;
        double a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        var stones = new List<PathsD>();
        if (Rough.Cellular(s.Texture))
        {
            var grid = TextureGrid.Wall(s.TextureOf(s.Seed + 17), a * 12, b * 12, 0, a * 12, b * 12);
            foreach (var r in grid.Tex.Raised.Where(r => { var m = Rough.Middle(r); return m.x > a && m.x < a * 11 && m.y > b && m.y < b * 11; }).OrderBy(_ => rnd.Next()).Take(n))
                stones.Add(Geo.Tidy([r]));
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                double rx = Math.Clamp(a * (0.25 + 0.2 * rnd.NextDouble()), 1, 15), ry = Math.Clamp(Math.Min(a, b) * (0.2 + 0.15 * rnd.NextDouble()), 0.8, 10);
                stones.Add(Geo.Tidy([Rough.Lumpy(Rough.Ellipse(0, 0, rx, ry, 24), Math.Min(rx, ry) * 0.3, Math.Max(2, rx), rnd, 0.4)]));
            }
        }
        stones = [.. stones.Where(x => x.Count > 0 && Geo.Area(x) > 0.5)];
        if (stones.Count == 0) { findings.Add(new(Severity.Warn, "The bricks or stones are too small to cut loose.")); return; }
        // The same size and area: the same piece.
        var groups = stones.GroupBy(x => { var bb = Geo.Bounds(x); return (Math.Round(bb.right - bb.left, 1), Math.Round(bb.bottom - bb.top, 1), Math.Round(Geo.Area(x), 0)); }).ToList();
        string word = Rough.Cellular(s.Texture) ? (s.Texture is "Brick" ? "brick" : s.Texture is "Tiles" ? "tile" : s.Texture is "Metal plates" or ThemeTextures.SciFiPanels ? "plate" : s.Texture == ThemeTextures.CaveRock ? "rock" : "stone") : "chip";
        int k = 0;
        foreach (var g in groups)
        {
            var first = g.First();
            var b0 = Geo.Bounds(first);
            string label = groups.Count == 1 ? $"Loose {word}" : $"Loose {word} {++k}";
            parts.Add(new Part(label, Geo.Move(first, -b0.left, -b0.top), [], [], g.Count()));
        }
        if (Math.Min(a, b) < s.ThicknessMm * 0.8)
            findings.Add(new(Severity.Note, $"The loose {word}s are smaller across than the sheet is thick: they'll stand up on edge as well as lie flat."));
    }

    /// <summary>Broken-off pieces of wall to lay on the ground: the courses still bonded, the texture on top.</summary>
    static void ChunksOf(RuinSpec s, List<Part> parts, Random rnd, List<Finding> findings)
    {
        int n = Math.Clamp(s.Chunks, 0, MaxChunks);
        double a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            double w = Math.Clamp(a * 2.8, 10, 45) * (0.8 + 0.45 * rnd.NextDouble()), h = Math.Clamp(b * 3.5, 8, 35) * (0.8 + 0.45 * rnd.NextDouble());
            double ox = rnd.NextDouble() * a, oy = rnd.NextDouble() * b;
            var grid = TextureGrid.Wall(s.TextureOf(s.Seed * 3 + i + 29), w + 2 * a + 4, h + 2 * b + 4, 0, w + 2 * a + 4, h + 2 * b + 4);
            double cx = a + 2 + w / 2 + ox * 0.2, cy = b + 2 + h / 2 + oy * 0.2;
            var blob = new PathsD { Rough.Lumpy(Rough.Ellipse(cx, cy, w / 2, h / 2), Math.Min(w, h) * 0.12, Math.Max(6, Math.Max(a, b) * 1.4), rnd) };
            var area = Rough.Cellular(s.Texture) && grid.Cells.Count > 0 ? Rough.Snap(grid.Cells, blob) : blob;
            area = Rough.Largest(Rough.Open(Rough.FillHoles(Rough.Close(area, Math.Min(a, b) * 0.2)), 1.5));
            if (area.Count == 0 || Geo.Area(area) < 20) continue;
            var (etched, lines) = grid.In(area, s.Etch);
            var bb = Geo.Bounds(area);
            PointD M(PointD p) => new(p.x - bb.left, p.y - bb.top);
            parts.Add(new Part(n == 1 ? "Fallen chunk" : $"Fallen chunk {++made}", Geo.Map(area, M), Geo.Map(etched, M), Geo.Map(lines, M), 1));
            if (n == 1) made = 1;
        }
        if (made < n) findings.Add(new(Severity.Note, $"Only {made} of the {n} chunks came out big enough to cut."));
    }
}
