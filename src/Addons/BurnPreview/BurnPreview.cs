using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.BurnPreview;

/// <summary>A picture of the bed to draw the finished piece on: the camera's (or a plain stand-in), and the bed area it covers.</summary>
/// <param name="Image">RGBA; pixel (x, y) is the bed at <c>Area.MinX + (x + ½)·MmPerPx</c>, <c>Area.MinY + (y + ½)·MmPerPx</c>.</param>
public sealed record BedPicture(RasterImage Image, Rect2 Area, double MmPerPx)
{
    /// <summary>A plain picture of bare material (no camera picture yet).</summary>
    public static BedPicture Plain(Rect2 area, double mmPerPx, uint colour)
    {
        int w = Math.Max(1, (int)Math.Ceiling(area.Width / mmPerPx)), h = Math.Max(1, (int)Math.Ceiling(area.Height / mmPerPx));
        var px = new byte[w * h * 4];
        // A faint, even grain so marks don't sit on a flat colour (fixed seed: the same each time).
        var rnd = new Random(7);
        for (int i = 0; i < w * h; i++)
        {
            int n = rnd.Next(-6, 7);
            px[i * 4] = (byte)Math.Clamp((int)((colour >> 16) & 0xFF) + n, 0, 255);
            px[i * 4 + 1] = (byte)Math.Clamp((int)((colour >> 8) & 0xFF) + n, 0, 255);
            px[i * 4 + 2] = (byte)Math.Clamp((int)(colour & 0xFF) + n, 0, 255);
            px[i * 4 + 3] = 255;
        }
        return new BedPicture(new RasterImage(w, h, px), area, mmPerPx);
    }

    /// <summary>
    /// Just the part over <paramref name="mm"/> (kept inside the picture), whole pixels. Null if
    /// they don't overlap.
    /// </summary>
    public BedPicture? Crop(Rect2 mm)
    {
        int x0 = Math.Max(0, (int)Math.Floor((mm.MinX - Area.MinX) / MmPerPx)), y0 = Math.Max(0, (int)Math.Floor((mm.MinY - Area.MinY) / MmPerPx));
        int x1 = Math.Min(Image.Width, (int)Math.Ceiling((mm.MaxX - Area.MinX) / MmPerPx)), y1 = Math.Min(Image.Height, (int)Math.Ceiling((mm.MaxY - Area.MinY) / MmPerPx));
        if (x1 <= x0 || y1 <= y0) return null;
        int w = x1 - x0, h = y1 - y0;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(Image.Rgba, ((y0 + y) * Image.Width + x0) * 4, px, y * w * 4, w * 4);
        return new BedPicture(new RasterImage(w, h, px),
            new Rect2(Area.MinX + x0 * MmPerPx, Area.MinY + y0 * MmPerPx, Area.MinX + x1 * MmPerPx, Area.MinY + y1 * MmPerPx), MmPerPx);
    }
}

/// <summary>One layer in the preview, and how it was made to look.</summary>
public sealed record PreviewLayer(Guid LayerId, string Name, LayerMode Mode, BurnLook Look);

/// <summary>The finished piece drawn on the bed picture, and where each layer's look came from.</summary>
public sealed record BurnPreviewResult(BedPicture Picture, IReadOnlyList<PreviewLayer> Layers)
{
    /// <summary>Every layer's look came from the user's own tests.</summary>
    public bool AllMeasured => Layers.All(l => l.Look.Source != LookSource.Estimate);
}

/// <summary>
/// "See it before you burn it": the design drawn as it will come out, on the camera's picture
/// of the actual material. Each output layer's marks darken or tint the picture by how its
/// settings marked the material in the user's tests (colour tests, tone tests), or by a guess
/// from the energy when there are none. Nothing is sent to the machine.
/// </summary>
public static class BurnPreview
{
    /// <summary>What a through-cut shows: the dark bed below the material.</summary>
    public const uint BedColour = 0x17181B;
    /// <summary>A wood-like char either side of a cut.</summary>
    const uint Scorch = 0x6B4A2E;
    /// <summary>Sub-rows per pixel row when filling shapes (smooth edges).</summary>
    const int Sub = 4;

    /// <summary>
    /// Draw <paramref name="state"/>'s output layers onto <paramref name="bed"/>.
    /// <paramref name="looks"/> overrides how a layer looks (default: <see cref="BurnLooks.For"/>).
    /// Hidden and non-output layers are left out.
    /// </summary>
    public static BurnPreviewResult Render(BedPicture bed, DocState state, MaterialLibrary? library, Func<Layer, BurnLook>? looks = null, CancellationToken cancel = default)
    {
        looks ??= l => BurnLooks.For(l, library);
        int w = bed.Image.Width, h = bed.Image.Height;
        // Work in linear light: multiplying by mark ÷ bare is how a mark changes the light the material gives back.
        var lin = new float[w * h * 3];
        var src = bed.Image.Rgba;
        for (int i = 0; i < w * h; i++)
        {
            lin[i * 3] = Lin[src[i * 4]];
            lin[i * 3 + 1] = Lin[src[i * 4 + 1]];
            lin[i * 3 + 2] = Lin[src[i * 4 + 2]];
        }

        var used = new List<PreviewLayer>();
        var cov = new float[w * h];
        var grid = new Grid(bed.Area, bed.MmPerPx, w, h);
        // Marks first, in layer order; cuts last, as they go through whatever was marked.
        var layers = state.Layers.Where(l => l.Output && l.Visible).ToList();
        foreach (var layer in layers.Where(l => l.Mode != LayerMode.Cut).Concat(layers.Where(l => l.Mode == LayerMode.Cut)))
        {
            cancel.ThrowIfCancellationRequested();
            var shapes = state.Shapes.Where(s => s.LayerId == layer.Id).ToList();
            if (shapes.Count == 0) continue;
            var look = looks(layer);
            used.Add(new PreviewLayer(layer.Id, layer.Name, layer.Mode, look));
            if (look.Strength <= 0) continue;
            switch (layer.Mode)
            {
                case LayerMode.Fill:
                    Array.Clear(cov);
                    foreach (var s in shapes.OfType<PathShape>())
                        FillPolygons(grid, cov, s.Flat.Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList());
                    Tint(lin, cov, look.Mark, look.Bare, look.Strength * FillCoverage(layer, look));
                    break;
                case LayerMode.Score:
                    Array.Clear(cov);
                    foreach (var s in shapes.OfType<PathShape>()) Stroke(grid, cov, s.Flat, look.LineWidthMm);
                    Tint(lin, cov, look.Mark, look.Bare, look.Strength);
                    break;
                case LayerMode.Cut:
                    if (look.ScorchMm > 0)
                    {
                        Array.Clear(cov);
                        foreach (var s in shapes.OfType<PathShape>()) Stroke(grid, cov, s.Flat, look.LineWidthMm + 2 * look.ScorchMm);
                        Tint(lin, cov, Scorch, look.Bare, 0.45 * look.Strength);
                    }
                    Array.Clear(cov);
                    // A gap through the material shows at least a pixel wide in a photo (its edges cast a shadow).
                    foreach (var s in shapes.OfType<PathShape>()) Stroke(grid, cov, s.Flat, look.ThroughCut ? Math.Max(look.LineWidthMm, bed.MmPerPx) : look.LineWidthMm);
                    if (look.ThroughCut) Paint(lin, cov, BedColour);
                    else Tint(lin, cov, look.Mark, look.Bare, look.Strength);
                    break;
                case LayerMode.Image:
                    Array.Clear(cov);
                    foreach (var im in shapes.OfType<ImageShape>()) Picture(grid, cov, im, look);
                    // Path shapes on a picture layer are engraved solid.
                    foreach (var s in shapes.OfType<PathShape>())
                        FillPolygons(grid, cov, s.Flat.Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList());
                    Tint(lin, cov, look.Mark, look.Bare, look.Strength);
                    break;
            }
        }

        var outPx = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            outPx[i * 4] = Srgb(lin[i * 3]);
            outPx[i * 4 + 1] = Srgb(lin[i * 3 + 1]);
            outPx[i * 4 + 2] = Srgb(lin[i * 3 + 2]);
            outPx[i * 4 + 3] = src[i * 4 + 3];
        }
        return new BurnPreviewResult(bed with { Image = new RasterImage(w, h, outPx) }, used);
    }

    /// <summary>
    /// The picture to draw on over <paramref name="area"/>: that part of the camera's, or (no
    /// picture, or it doesn't reach) a stand-in in the colour of the first layer's material, at
    /// most <paramref name="maxSide"/> pixels across.
    /// </summary>
    public static BedPicture Bed(DocState state, BedPicture? camera, MaterialLibrary? library, Rect2 area, int maxSide = 1600)
    {
        if (camera?.Crop(area) is { } cropped) return cropped;
        var first = state.Layers.FirstOrDefault(l => l.Output && l.Visible && l.Material is not null) ?? state.Layers.FirstOrDefault(l => l.Output && l.Visible);
        uint colour = first is null ? BurnLooks.BareGuess(null) : BurnLooks.For(first, library).Bare;
        double mmPerPx = Math.Max(0.05, Math.Max(area.Width, area.Height) / maxSide);
        return BedPicture.Plain(area, mmPerPx, colour);
    }

    /// <summary>What the job would mark: everything on visible output layers (null = nothing).</summary>
    public static Rect2? DesignBounds(DocState state)
    {
        var on = state.Layers.Where(l => l.Output && l.Visible).Select(l => l.Id).ToHashSet();
        Rect2? b = null;
        foreach (var s in state.Shapes.Where(s => on.Contains(s.LayerId)))
            b = b is { } r ? r.Union(s.Bounds) : s.Bounds;
        return b;
    }

    /// <summary>
    /// Fills: lines further apart than the beam is wide leave bare stripes, so the area looks
    /// lighter overall (the beam is taken as the layer's line width).
    /// </summary>
    static double FillCoverage(Layer layer, BurnLook look)
    {
        if (look.Source is LookSource.ColourTest or LookSource.NearestColourTest) return 1; // measured with its own line spacing
        double spacing = 10 / Math.Max(1, layer.LinesPerCm);
        double beam = Math.Max(0.05, look.LineWidthMm);
        double one = Math.Min(1, beam / spacing);
        return layer.Crosshatch ? 1 - (1 - one) * (1 - one) : one;
    }

    /// <summary>Where the bed picture's pixels are.</summary>
    readonly record struct Grid(Rect2 Area, double MmPerPx, int W, int H)
    {
        public double X(double mm) => (mm - Area.MinX) / MmPerPx;
        public double Y(double mm) => (mm - Area.MinY) / MmPerPx;
        public Vec2 Mm(double x, double y) => new(Area.MinX + x * MmPerPx, Area.MinY + y * MmPerPx);
    }

    /// <summary>Change the picture by mark ÷ bare where covered, as much as <paramref name="strength"/>.</summary>
    static void Tint(float[] lin, float[] cov, uint mark, uint bare, double strength)
    {
        Span<float> ratio = stackalloc float[3];
        for (int c = 0; c < 3; c++)
        {
            int shift = 16 - 8 * c;
            float m = Lin[(mark >> shift) & 0xFF], b = Lin[(bare >> shift) & 0xFF];
            ratio[c] = b < 1e-4f ? (m < 1e-4f ? 1 : 4) : Math.Min(4f, m / b);
        }
        float s = (float)Math.Clamp(strength, 0, 1);
        for (int i = 0; i < cov.Length; i++)
        {
            float a = cov[i] * s;
            if (a <= 0) continue;
            for (int c = 0; c < 3; c++)
                lin[i * 3 + c] = Math.Min(1f, lin[i * 3 + c] * (1 + (ratio[c] - 1) * a));
        }
    }

    /// <summary>Paint a flat colour where covered (what shows through a cut).</summary>
    static void Paint(float[] lin, float[] cov, uint colour)
    {
        for (int c = 0; c < 3; c++)
        {
            float v = Lin[(colour >> (16 - 8 * c)) & 0xFF];
            for (int i = 0; i < cov.Length; i++)
                if (cov[i] > 0) lin[i * 3 + c] += (v - lin[i * 3 + c]) * cov[i];
        }
    }

    /// <summary>
    /// Fill closed outlines (one shape: holes respected, even-odd) into the coverage, keeping the
    /// larger of what's there and this shape's coverage.
    /// </summary>
    static void FillPolygons(Grid g, float[] cov, List<List<Vec2>> polys)
    {
        if (polys.Count == 0) return;
        double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
        var edges = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (var poly in polys)
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                double x0 = g.X(a.X), y0 = g.Y(a.Y), x1 = g.X(b.X), y1 = g.Y(b.Y);
                if (y0 == y1) continue;
                edges.Add((x0, y0, x1, y1));
                minY = Math.Min(minY, Math.Min(y0, y1)); maxY = Math.Max(maxY, Math.Max(y0, y1));
                minX = Math.Min(minX, Math.Min(x0, x1)); maxX = Math.Max(maxX, Math.Max(x0, x1));
            }
        if (edges.Count == 0) return;
        int r0 = Math.Max(0, (int)Math.Floor(minY)), r1 = Math.Min(g.H - 1, (int)Math.Ceiling(maxY));
        int c0 = Math.Max(0, (int)Math.Floor(minX)), c1 = Math.Min(g.W - 1, (int)Math.Ceiling(maxX));
        if (r0 > r1 || c0 > c1) return;
        int bw = c1 - c0 + 1;
        var row = new float[bw];
        var xs = new List<double>();
        for (int y = r0; y <= r1; y++)
        {
            Array.Clear(row);
            for (int s = 0; s < Sub; s++)
            {
                double sy = y + (s + 0.5) / Sub;
                xs.Clear();
                foreach (var (x0, y0, x1, y1) in edges)
                    if ((y0 <= sy) != (y1 <= sy))
                        xs.Add(x0 + (sy - y0) * (x1 - x0) / (y1 - y0));
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2) Span(row, c0, xs[k], xs[k + 1], 1f / Sub);
            }
            int baseI = y * g.W + c0;
            for (int x = 0; x < bw; x++)
                if (row[x] > cov[baseI + x]) cov[baseI + x] = Math.Min(1, row[x]);
        }
    }

    /// <summary>Add a horizontal span from <paramref name="xa"/> to <paramref name="xb"/> (pixels), partial pixels at the ends.</summary>
    static void Span(float[] row, int c0, double xa, double xb, float weight)
    {
        xa = Math.Max(xa, c0);
        xb = Math.Min(xb, c0 + row.Length);
        if (xb <= xa) return;
        int ia = (int)Math.Floor(xa), ib = (int)Math.Floor(xb);
        if (ia == ib) { row[ia - c0] += (float)(xb - xa) * weight; return; }
        row[ia - c0] += (float)(ia + 1 - xa) * weight;
        for (int x = ia + 1; x < ib && x - c0 < row.Length; x++) row[x - c0] += weight;
        if (ib - c0 < row.Length) row[ib - c0] += (float)(xb - ib) * weight;
    }

    /// <summary>Draw lines <paramref name="widthMm"/> wide along the paths, smooth-edged; lines thinner than a pixel come out faint.</summary>
    static void Stroke(Grid g, float[] cov, List<FlatPath> paths, double widthMm)
    {
        double wpx = widthMm / g.MmPerPx;
        double hw = Math.Max(0.5, wpx / 2);
        float thin = (float)Math.Min(1, wpx);
        foreach (var f in paths)
        {
            var pts = f.Points;
            int n = f.Closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                double ax = g.X(a.X), ay = g.Y(a.Y), bx = g.X(b.X), by = g.Y(b.Y);
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - hw - 1)), x1 = Math.Min(g.W - 1, (int)Math.Ceiling(Math.Max(ax, bx) + hw + 1));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - hw - 1)), y1 = Math.Min(g.H - 1, (int)Math.Ceiling(Math.Max(ay, by) + hw + 1));
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        double px = x + 0.5, py = y + 0.5;
                        double t = len2 < 1e-12 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
                        double ex = ax + t * dx - px, ey = ay + t * dy - py;
                        double d = Math.Sqrt(ex * ex + ey * ey);
                        float c = (float)Math.Clamp(hw - d + 0.5, 0, 1) * thin;
                        int k = y * g.W + x;
                        if (c > cov[k]) cov[k] = c;
                    }
            }
        }
    }

    /// <summary>A picture's greys as they'll come out: corrected and through the material's response when measured.</summary>
    static void Picture(Grid g, float[] cov, ImageShape im, BurnLook look)
    {
        if (!im.Transform.IsInvertible) return;
        var inv = im.Transform.Invert();
        var px = im.Source.Pixels;
        if (px.Width == 0 || px.Height == 0 || im.SizeMm.X <= 0 || im.SizeMm.Y <= 0) return;
        // Each grey worked out once.
        var dark = new float[256];
        for (int v = 0; v < 256; v++)
        {
            double tone = im.Tone.Apply(v / 255.0);
            double light = look.Tone is { } curve ? curve.Response(look.Corrected ? curve.Correct(tone) : tone) : tone;
            dark[v] = (float)Math.Clamp(1 - light, 0, 1);
        }
        var b = im.Bounds;
        int x0 = Math.Max(0, (int)Math.Floor(g.X(b.MinX))), x1 = Math.Min(g.W - 1, (int)Math.Ceiling(g.X(b.MaxX)));
        int y0 = Math.Max(0, (int)Math.Floor(g.Y(b.MinY))), y1 = Math.Min(g.H - 1, (int)Math.Ceiling(g.Y(b.MaxY)));
        double sx = px.Width / im.SizeMm.X, sy = px.Height / im.SizeMm.Y;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var local = inv.Apply(g.Mm(x + 0.5, y + 0.5));
                int u = (int)Math.Floor(local.X * sx), v = (int)Math.Floor(local.Y * sy);
                if (u < 0 || v < 0 || u >= px.Width || v >= px.Height) continue;
                if (im.Clip is not null && !im.InClip(local)) continue;
                int k = (v * px.Width + u) * 4;
                int lum = (px.Rgba[k] * 77 + px.Rgba[k + 1] * 150 + px.Rgba[k + 2] * 29) >> 8;
                float c = dark[lum] * px.Rgba[k + 3] / 255f;
                int i = y * g.W + x;
                if (c > cov[i]) cov[i] = c;
            }
    }

    static readonly float[] Lin = [.. Enumerable.Range(0, 256).Select(v => (float)ColourScience.ToLinear(v / 255.0))];

    static byte Srgb(float v) => (byte)Math.Round(Math.Clamp(ColourScience.FromLinear(Math.Clamp(v, 0, 1)), 0, 1) * 255);
}
