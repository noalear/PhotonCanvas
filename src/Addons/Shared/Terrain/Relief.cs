using Clipper2Lib;
using PhotonCanvas.Doc;
using SkiaSharp;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// A textured piece as a greyscale depth map (dark = deep), to carve as a relief on a depth-slices
/// image layer instead of etching it at one depth. Made from the same shapes the flat etch uses:
/// the etched areas (mortar, pits, cracks) go deepest, with floors that aren't quite flat; every raised
/// piece (each brick, stone, plank or plate) stands at its own height and leans a little; its edges
/// round over into the grooves; worn surfaces get a gentle unevenness; scored lines (grain, scratches,
/// small cracks) are cut shallow. Outside the piece the picture is see-through, so nothing is engraved there.
/// </summary>
internal static class Relief
{
    /// <summary>What the relief is made from: its look (how worn, how fine, metal or not) and how sharp it is.</summary>
    public readonly record struct Look(double Groove, double Wear, double Detail, Stuff Stuff, int Seed, double PxPerMm = 10);

    /// <summary>The depth map (a PNG: grey = depth, transparent = not engraved) and where it goes: the piece's top-left corner (the design's own y-up millimetres) and size.</summary>
    public sealed record Map(byte[] Png, double Left, double Top, double WidthMm, double HeightMm);

    /// <summary>
    /// A new relief layer: a picture layer carving in depth slices at the map's own resolution (10 px/mm),
    /// each slice's lines turned so they don't stack into ridges.
    /// </summary>
    public static Layer Setup(Layer l) => l with
    {
        ImageMode = ImageEngraveMode.Slices, Dpi = 254, LinesPerCm = 200, AngleStepDeg = 37,
        Slices = l.Slices with { Count = 20 },
    };

    /// <summary>Most pixels a map may have (about 300 × 300 mm at 10 px/mm): bigger pieces are drawn coarser.</summary>
    const int MaxPixels = 9_000_000;

    /// <summary>
    /// The depth map of a piece. <paramref name="cut"/> is its outline (holes are left out),
    /// <paramref name="etched"/> what the flat etch would burn, <paramref name="lines"/> what it would score.
    /// Coordinates are y-up millimetres, as the makers draw. Null if there's nothing to carve.
    /// </summary>
    public static Map? Render(PathsD cut, PathsD etched, IReadOnlyList<PathD> lines, Look look)
    {
        if (cut.Count == 0 || (etched.Count == 0 && lines.Count == 0)) return null;
        var b = Clipper.GetBounds(cut);
        double wMm = b.right - b.left, hMm = b.bottom - b.top;
        if (wMm <= 0 || hMm <= 0) return null;
        double ppm = look.PxPerMm;
        if (wMm * hMm * ppm * ppm > MaxPixels) ppm = Math.Sqrt(MaxPixels / (wMm * hMm));
        int W = Math.Max(1, (int)Math.Ceiling(wMm * ppm)), H = Math.Max(1, (int)Math.Ceiling(hMm * ppm));
        // Pixel (i, j) covers x from left + i / ppm, y from top (the highest y) down.
        double left = b.left, top = b.bottom;

        var mask = Raster(W, H, ppm, left, top, c => Fill(c, cut, true));
        var etch = Raster(W, H, ppm, left, top, c => Fill(c, etched, true));
        double lineMm = Math.Max(0.18, Math.Min(0.35, look.Detail * 0.5));
        var scored = Raster(W, H, ppm, left, top, c => Stroke(c, lines, (float)lineMm));

        bool metal = look.Stuff == Stuff.Metal, soft = look.Stuff is Stuff.SoftStone or Stuff.Plaster, wood = look.Stuff == Stuff.Wood;
        double wear = Math.Clamp(look.Wear, 0, 1);
        var noise = new Noise(look.Seed * 31 + 7);
        var rnd = new Random(look.Seed * 131 + 3);

        // ---- each raised piece's own height and lean ----
        int n = W * H;
        var label = new int[n];
        var pieces = new List<(double Base, double Tx, double Ty, double Cx, double Cy)> { default };
        int inside = 0;
        for (int i = 0; i < n; i++) if (mask[i] >= 128) inside++;
        var stack = new Stack<int>();
        var plainIds = new HashSet<int>();
        // How much the pieces' heights differ (share of the full depth): metal plates are made level, stone isn't.
        double vary = metal ? 0.03 + 0.05 * wear : wood ? 0.06 + 0.1 * wear : 0.07 + 0.18 * wear;
        double lean = metal ? 0.01 : 0.03 + 0.07 * wear;
        for (int start = 0; start < n; start++)
        {
            if (label[start] != 0 || mask[start] < 128 || etch[start] >= 128) continue;
            int id = pieces.Count, count = 0;
            long sx = 0, sy = 0;
            int minX = int.MaxValue, maxX = 0, minY = int.MaxValue, maxY = 0;
            label[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % W, y = p / W;
                count++; sx += x; sy += y;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                void Visit(int q) { if (label[q] == 0 && mask[q] >= 128 && etch[q] < 128) { label[q] = id; stack.Push(q); } }
                if (x > 0) Visit(p - 1);
                if (x < W - 1) Visit(p + 1);
                if (y > 0) Visit(p - W);
                if (y < H - 1) Visit(p + W);
            }
            // The plain parts of a piece (smooth ends, frames, the band above the texture) are one big
            // area: they stay at the surface, so the face reads as level and the pieces in it as worn.
            bool plain = count > inside * 0.2;
            if (plain) plainIds.Add(id);
            double size = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
            pieces.Add(plain
                ? (0, 0, 0, 0, 0)
                : (rnd.NextDouble() * vary, (rnd.NextDouble() * 2 - 1) * lean / size, (rnd.NextDouble() * 2 - 1) * lean / size, (double)sx / count, (double)sy / count));
        }

        // ---- what stands in a recess sits part way down ----
        // How open the ground round each piece is: a brick has only its mortar round it, but a pipe, a
        // box or a grille's slats stand in a wide recess, and are set lower than the face, so the layers read.
        {
            int reach = Math.Max(2, (int)Math.Round(1.2 * ppm));
            var open = Blur(Blur(ToFloat(etch), W, H, reach), W, H, reach);
            var sum = new double[pieces.Count];
            var count = new int[pieces.Count];
            for (int i = 0; i < n; i++)
                if (label[i] > 0) { sum[label[i]] += open[i]; count[label[i]]++; }
            for (int id = 1; id < pieces.Count; id++)
            {
                var pc = pieces[id];
                if (count[id] == 0 || plainIds.Contains(id)) continue;
                double sink = Math.Clamp((sum[id] / count[id] - 0.38) / 0.3, 0, 1) * 0.42;
                pieces[id] = pc with { Base = pc.Base + sink };
            }
        }

        // ---- how deep each etched mark goes ----
        // The mortar (or any big joined-up groove) goes deepest; small marks on their own (pits, chips,
        // nail holes, short cracks) only part of the way, each its own depth.
        var markDepth = new double[n];
        var seen = new bool[n];
        double smallPx = Math.Max(4, Math.Pow(Math.Max(1.0, look.Detail * 2.5) * ppm, 2));
        var cells = new List<int>();
        for (int start = 0; start < n; start++)
        {
            if (seen[start] || etch[start] < 128 || mask[start] < 128) continue;
            cells.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % W, y = p / W;
                cells.Add(p);
                void Visit(int q) { if (!seen[q] && etch[q] >= 128 && mask[q] >= 128) { seen[q] = true; stack.Push(q); } }
                if (x > 0) Visit(p - 1);
                if (x < W - 1) Visit(p + 1);
                if (y > 0) Visit(p - W);
                if (y < H - 1) Visit(p + W);
            }
            double depth = cells.Count < smallPx ? 0.35 + 0.35 * rnd.NextDouble() : 1;
            foreach (int c in cells) markDepth[c] = depth;
        }
        // Anti-aliased edge pixels outside a mark take the depth of the mark next to them.
        for (int i = 0; i < n; i++)
            if (markDepth[i] == 0 && etch[i] > 0)
            {
                int x = i % W, y = i / W;
                double best = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < W && yy < H && etch[yy * W + xx] >= 128) best = Math.Max(best, markDepth[yy * W + xx]);
                    }
                markDepth[i] = best > 0 ? best : 1;
            }

        // ---- the edges of the pieces round over into the grooves ----
        // How far in from the groove an edge starts to drop (pixels): wider on soft stone, a hair on metal.
        double roundMm = (metal ? 0.12 : soft ? Math.Clamp(look.Groove * 0.9, 0.25, 0.9) : Math.Clamp(look.Groove * 0.6, 0.18, 0.6)) * (metal ? 1 : 0.7 + 0.8 * wear);
        int r = Math.Max(1, (int)Math.Round(roundMm * ppm / 2));
        // Weighted by each mark's depth, so the edge round a shallow pit drops less than the edge of the mortar.
        var weighted = ToFloat(etch);
        for (int i = 0; i < n; i++) weighted[i] *= (float)markDepth[i];
        var near = Blur(Blur(weighted, W, H, r), W, H, r);

        // ---- depth everywhere (0 = surface, 1 = deepest) ----
        var outPx = new byte[n * 4];
        double grainSize = Math.Max(0.3, look.Detail * 3), patchSize = Math.Max(2, look.Detail * 18);
        double rough = metal ? 0.015 + 0.03 * wear : 0.03 + 0.09 * wear;
        double edgeDrop = metal ? 0.35 : 0.55 + 0.25 * wear;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (mask[i] < 128) continue;
                double mx = left + (x + 0.5) / ppm, my = top - (y + 0.5) / ppm;
                double e = etch[i] / 255.0;
                var pc = pieces[label[i]];
                double d = label[i] == 0 ? 0 : pc.Base + pc.Tx * (x - pc.Cx) + pc.Ty * (y - pc.Cy);
                // Uneven, worn surface: broad hollows and fine grit.
                d += rough * (0.5 + 0.5 * noise.Fbm(mx, my, patchSize, 3)) + rough * 0.35 * (0.5 + 0.5 * noise.Fbm(mx + 91, my - 37, grainSize, 2));
                // The rounded shoulder where the surface meets a groove.
                double k = Math.Clamp(near[i] * 2, 0, 1);
                d += (1 - d) * edgeDrop * k * k;
                // The groove's floor: deepest, but not quite flat.
                double floor = markDepth[i] * (0.88 + 0.12 * (0.5 + 0.5 * noise.Fbm(mx - 53, my + 11, Math.Max(0.5, look.Detail * 4), 2)));
                d = d + (floor - d) * e;
                // Scored lines: a shallow cut.
                d = Math.Max(d, 0.32 * scored[i] / 255.0);
                byte v = (byte)Math.Clamp((int)Math.Round(255 * (1 - Math.Clamp(d, 0, 1))), 0, 255);
                outPx[i * 4] = outPx[i * 4 + 1] = outPx[i * 4 + 2] = v;
                outPx[i * 4 + 3] = 255;
            }
        return new Map(Encode(W, H, outPx), left, top, W / ppm, H / ppm);
    }

    static byte[] Raster(int W, int H, double ppm, double left, double top, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(W, H, SKColorType.Alpha8, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            c.Scale((float)ppm, (float)-ppm);
            c.Translate((float)-left, (float)-top);
            draw(c);
        }
        return bmp.Bytes;
    }

    static void Fill(SKCanvas c, PathsD areas, bool evenOdd)
    {
        using var path = new SKPath { FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        foreach (var p in areas)
        {
            if (p.Count < 3) continue;
            path.MoveTo((float)p[0].x, (float)p[0].y);
            for (int i = 1; i < p.Count; i++) path.LineTo((float)p[i].x, (float)p[i].y);
            path.Close();
        }
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawPath(path, paint);
    }

    static void Stroke(SKCanvas c, IReadOnlyList<PathD> lines, float width)
    {
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var l in lines)
        {
            if (l.Count < 2) continue;
            using var path = new SKPath();
            path.MoveTo((float)l[0].x, (float)l[0].y);
            for (int i = 1; i < l.Count; i++) path.LineTo((float)l[i].x, (float)l[i].y);
            c.DrawPath(path, paint);
        }
    }

    static float[] ToFloat(byte[] a)
    {
        var f = new float[a.Length];
        for (int i = 0; i < a.Length; i++) f[i] = a[i] / 255f;
        return f;
    }

    /// <summary>A box blur of radius <paramref name="r"/> pixels, across then down (running sums).</summary>
    static float[] Blur(float[] src, int W, int H, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += src[row + Math.Clamp(k, 0, W - 1)];
            for (int x = 0; x < W; x++)
            {
                tmp[row + x] = sum * inv;
                sum += src[row + Math.Min(W - 1, x + r + 1)] - src[row + Math.Max(0, x - r)];
            }
        }
        for (int x = 0; x < W; x++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, H - 1) * W + x];
            for (int y = 0; y < H; y++)
            {
                dst[y * W + x] = sum * inv;
                sum += tmp[Math.Min(H - 1, y + r + 1) * W + x] - tmp[Math.Max(0, y - r) * W + x];
            }
        }
        return dst;
    }

    static byte[] Encode(int W, int H, byte[] rgba)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bmp.GetPixels(), rgba.Length);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
