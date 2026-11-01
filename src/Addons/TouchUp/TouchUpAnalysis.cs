using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.TouchUp;

/// <summary>How a burned engraving is compared with the design, and how hard the touch-up goes.</summary>
public sealed record TouchUpOptions
{
    /// <summary>Differences smaller than this share of the full darkness are left alone (photo noise, grain, the dots themselves).</summary>
    public double Sensitivity { get; init; } = 0.08;
    /// <summary>Light patches smaller than this (mm²) are left alone.</summary>
    public double MinPatchMm2 { get; init; } = 0.5;
    /// <summary>How much of the missing darkness the touch-up puts back: 1 = all of it. Below 1 is safer: darkness can be added, never taken away.</summary>
    public double Strength { get; init; } = 0.7;
    /// <summary>Aim this much darker than the best-burned parts (0.2 = a fifth darker); 0 = even everything up to the best of this burn.</summary>
    public double AimDarker { get; init; }
    /// <summary>The furthest the photo may sit off the design (mm): the best fit within it is found.</summary>
    public double MaxShiftMm { get; init; } = 1.0;
    /// <summary>The comparison grid's spacing (mm); 0 = chosen from the photo.</summary>
    public double PitchMm { get; init; }
}

/// <summary>The touch-up can't be worked out from this photo; the message says why, in plain words.</summary>
public sealed class TouchUpException(string message) : Exception(message);

/// <summary>A grid of cells over part of the bed: cell (x, y) covers MinX + x·Pitch … +Pitch.</summary>
public readonly record struct CellGrid(Rect2 Area, double Pitch, int W, int H)
{
    public int Count => W * H;
    public Vec2 Centre(int x, int y) => new(Area.MinX + (x + 0.5) * Pitch, Area.MinY + (y + 0.5) * Pitch);

    public static CellGrid Over(Rect2 area, double pitch)
    {
        int w = Math.Max(1, (int)Math.Ceiling(area.Width / pitch - 1e-9)), h = Math.Max(1, (int)Math.Ceiling(area.Height / pitch - 1e-9));
        return new CellGrid(new Rect2(area.MinX, area.MinY, area.MinX + w * pitch, area.MinY + h * pitch), pitch, w, h);
    }
}

/// <summary>What one engraving layer needs going over again.</summary>
/// <param name="Layer">The layer as it is in the design.</param>
/// <param name="Dose">Per cell of the result's grid, 0 … 1: how much more darkness to add (0 = leave it).</param>
/// <param name="Measured">Cells of this layer the photo could judge.</param>
/// <param name="TooLight">Of those, the ones that came out too light (and are big enough to touch up).</param>
/// <param name="FullContrast">How much darker than the bare material the darkest tone should come out (0 … 1 of the bare brightness).</param>
/// <param name="WorstDeficit">The most missing darkness in a cell (share of the full darkness).</param>
/// <param name="MeanDeficit">The average missing darkness over the too-light cells.</param>
/// <param name="Note">Why this layer can't be touched up, or null.</param>
public sealed record TouchUpLayer(Layer Layer, float[] Dose, int Measured, int TooLight, double FullContrast, double WorstDeficit, double MeanDeficit, string? Note)
{
    public double TooLightShare => Measured == 0 ? 0 : (double)TooLight / Measured;
    public bool NeedsTouchUp => Note is null && TooLight > 0;
}

/// <summary>
/// One comparison of a photo with the design: the grid it was made on, the darkness wanted and
/// found in each cell, how far the photo had to move to sit on the design, and per layer what
/// needs going over again.
/// </summary>
public sealed record TouchUpResult(
    CellGrid Grid,
    float[] Target,
    float[] Found,
    float[] Deficit,
    Vec2 ShiftMm,
    double Fit,
    bool MarksLighter,
    double OutsidePhoto,
    IReadOnlyList<TouchUpLayer> Layers)
{
    public bool NeedsTouchUp => Layers.Any(l => l.NeedsTouchUp);

    /// <summary>One or two sentences on what the photo showed.</summary>
    public string Summary
    {
        get
        {
            int measured = Layers.Sum(l => l.Measured), light = Layers.Sum(l => l.TooLight);
            if (measured == 0) return "Nothing in the photo could be compared with the design.";
            var parts = new List<string>();
            if (light == 0) parts.Add("Everything came out as dark as it should: nothing needs touching up.");
            else
            {
                var worst = Layers.Where(l => l.NeedsTouchUp).Max(l => l.WorstDeficit);
                parts.Add($"{100.0 * light / measured:0.#}% of the engraving came out too light (at worst {worst * 100:0}% short of the darkness it should have).");
            }
            if (OutsidePhoto > 0.02) parts.Add($"{OutsidePhoto * 100:0}% of it is outside the photo and wasn't checked.");
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// Burn, look, touch up: a photo of a burned engraving is compared with the design, cell by cell,
/// and the parts that came out too light are found. The photo's brightness is taken relative to
/// the bare material right around each spot, so uneven light doesn't count; what each tone
/// should come out as is learned from the best-burned part of that tone in the same burn (made
/// darker by <see cref="TouchUpOptions.AimDarker"/> to darken the lot), so the same photo serves
/// every material. Never needs the laser or the head: it only reads a photo and adds a page.
/// </summary>
public static class TouchUpAnalysis
{
    /// <summary>The share of each tone's cells, darkest first, that sets what that tone should look like.</summary>
    public const double BestShare = 0.3;
    /// <summary>The least darkness (share of the bare brightness) the engraving must show to be seen at all.</summary>
    public const double VisibleContrast = 0.04;
    const int ToneBins = 10;
    const double MinTone = 0.05;

    /// <summary>Layers that can be touched up: engraved areas and pictures (not relief), sent, with something on them.</summary>
    public static IEnumerable<Layer> Candidates(DocState s) =>
        s.Layers.Where(l => l.Output && l.Visible && IsEngraving(l) && s.ShapesFor(l).Any(sh => sh is ImageShape or PathShape));

    public static bool IsEngraving(Layer l) => l.Mode == LayerMode.Fill || (l.Mode == LayerMode.Image && l.ImageMode != ImageEngraveMode.Slices);

    /// <summary>The bed area the layers' engravings cover.</summary>
    public static Rect2 Extent(DocState s, IEnumerable<Layer> layers)
    {
        var r = Rect2.Empty;
        foreach (var l in layers)
            foreach (var sh in s.ShapesFor(l))
                r = r.Union(sh is ImageShape img ? img.ShownBounds : sh.Bounds);
        return r;
    }

    /// <summary>
    /// The spacing of what a layer burns (mm): its dots or lines. The photo is looked at over a few
    /// of them, so single dots and lines don't count as light gaps.
    /// </summary>
    public static double Texture(Layer l) => l.Mode == LayerMode.Image ? 25.4 / Math.Max(10, l.Dpi) : 10 / Math.Max(1, l.LinesPerCm);

    /// <summary>
    /// Compare a photo (laid straight on the bed: <paramref name="area"/>, <paramref name="mmPerPx"/>)
    /// with the layers' engravings in <paramref name="s"/>.
    /// </summary>
    /// <exception cref="TouchUpException">Nothing to compare, or the engraving can't be seen in the photo.</exception>
    public static TouchUpResult Compare(DocState s, IReadOnlyList<Layer> layers, RasterImage photo, Rect2 area, double mmPerPx, TouchUpOptions o)
    {
        layers = [.. layers.Where(IsEngraving)];
        if (layers.Count == 0) throw new TouchUpException("Choose at least one engraving or picture layer to check.");
        var extent = Extent(s, layers);
        if (extent.IsEmpty) throw new TouchUpException("The chosen layers have nothing on them.");
        var within = Intersect(extent.Inflate(1.5), area);
        if (within.IsEmpty || within.Width < 1 || within.Height < 1)
            throw new TouchUpException("The engraving isn't in this photo. Take the photo with the engraving in view (the close-up camera only sees a small patch under the head).");

        double texture = layers.Max(Texture);
        double pitch = o.PitchMm > 0 ? o.PitchMm : Math.Max(0.08, Math.Max(mmPerPx, texture / 2));
        pitch = Math.Max(pitch, Math.Sqrt(within.Width * within.Height / 2.5e6));
        var grid = CellGrid.Over(within, pitch);
        int n = grid.Count;

        // What each layer should burn, and the strongest of them in each cell.
        var targets = layers.Select(l => LayerTarget(s, l, grid)).ToList();
        var target = new float[n];
        var owner = new int[n];
        Array.Fill(owner, -1);
        for (int k = 0; k < targets.Count; k++)
            for (int i = 0; i < n; i++)
                if (targets[k][i] > target[i]) { target[i] = targets[k][i]; owner[i] = k; }

        // The photo on a bigger grid, so it can be shifted up to MaxShiftMm each way.
        int pad = Math.Max(0, (int)Math.Ceiling(o.MaxShiftMm / pitch));
        var big = new CellGrid(new Rect2(grid.Area.MinX - pad * pitch, grid.Area.MinY - pad * pitch, grid.Area.MaxX + pad * pitch, grid.Area.MaxY + pad * pitch), pitch, grid.W + 2 * pad, grid.H + 2 * pad);
        var raw = SamplePhoto(photo, area, mmPerPx, big);

        // Looked at over a few dots or lines: the burn's texture and the photo's own blur don't count.
        double sigmaMm = Math.Max(0.2, Math.Max(1.2 * texture, 1.5 * mmPerPx));
        double sigma = sigmaMm / pitch;
        var photoSoft = Blur(raw, big.W, big.H, sigma);
        var targetSoft = Blur(target, grid.W, grid.H, sigma);

        // Where the photo sits on the design: the shift that lines the edges up best. Both with the
        // slow changes taken out first, so the light falling off across the photo doesn't pull it.
        double wide = Math.Max(1.5, 4 * sigmaMm) / pitch;
        var (sx, sy, fit) = BestShift(HighPass(targetSoft, grid.W, grid.H, wide), grid.W, grid.H, HighPass(photoSoft, big.W, big.H, wide), big.W, pad);
        if (fit < 0.2)
            throw new TouchUpException("The photo doesn't look like the design here. Make sure the engraving is in view, the piece hasn't moved since it was burned, and the light shows the burn clearly (no glare).");
        var found = new float[n];
        var foundRaw = new float[n];
        for (int y = 0; y < grid.H; y++)
            for (int x = 0; x < grid.W; x++)
            {
                int j = (y + pad + sy) * big.W + x + pad + sx;
                found[y * grid.W + x] = photoSoft[j];
                foundRaw[y * grid.W + x] = raw[j];
            }

        // The bare material around the engraving, as the light falls on it: brightness is judged against it.
        var bare = BareBrightness(found, target, grid, sigmaMm);
        // How dark each cell came out, as a share of the bare material's brightness there.
        var contrast = new float[n];
        for (int i = 0; i < n; i++)
            contrast[i] = float.IsNaN(found[i]) || !(bare[i] > 1e-3) ? float.NaN : 1f - found[i] / bare[i];

        // Marks may come out lighter than the material (some metals, dark anodising): look which way.
        var deep = Enumerable.Range(0, n).Where(i => target[i] > 0.5f && !float.IsNaN(contrast[i])).Select(i => (double)contrast[i]).ToList();
        if (deep.Count < 10) deep = [.. Enumerable.Range(0, n).Where(i => target[i] > MinTone && !float.IsNaN(contrast[i])).Select(i => (double)contrast[i])];
        if (deep.Count < 10) throw new TouchUpException("Too little of the engraving is in the photo to judge it.");
        double median = Percentile(deep, 0.5);
        bool lighter = median < 0;
        if (Math.Abs(median) < VisibleContrast && Math.Abs(Percentile(deep, lighter ? 0.1 : 0.9)) < VisibleContrast)
            throw new TouchUpException("The engraving can't be seen in the photo. Turn the lights so the burn shows clearly against the material, with no glare, and take it again.");
        if (lighter) for (int i = 0; i < n; i++) contrast[i] = -contrast[i];

        // Edges: where the design changes sharply, a hair's misplacement looks like a fault; those aren't judged.
        // So are the outlines of the engraving: a dithered picture's first dots and a fill's first
        // lines come late there by their nature, and going over them again wouldn't change that.
        var edge = Edges(target, grid.W, grid.H, Math.Max(1, (int)Math.Ceiling(Math.Max(0.3, sigmaMm) / pitch)));
        var bareCells = new bool[n];
        for (int i = 0; i < n; i++) bareCells[i] = target[i] <= MinTone;
        var rim = Dilate(bareCells, grid.W, grid.H, Math.Max(1, (int)Math.Ceiling(Math.Max(0.3, Math.Max(sigmaMm, 4 * texture)) / pitch)));
        for (int i = 0; i < n; i++) edge[i] |= rim[i];

        var deficit = new float[n];
        var results = new List<TouchUpLayer>();
        int outside = 0, wanted = 0;
        for (int i = 0; i < n; i++)
            if (target[i] > MinTone) { wanted++; if (float.IsNaN(found[i])) outside++; }
        for (int k = 0; k < layers.Count; k++)
            results.Add(JudgeLayer(layers[k], k, owner, targetSoft, contrast, edge, grid, o, deficit));
        return new TouchUpResult(grid, target, foundRaw, deficit, new Vec2(sx * pitch, sy * pitch), fit, lighter, wanted == 0 ? 0 : (double)outside / wanted, results);
    }

    static TouchUpLayer JudgeLayer(Layer layer, int k, int[] owner, float[] targetSoft, float[] contrast, bool[] edge, CellGrid grid, TouchUpOptions o, float[] deficitOut)
    {
        int n = grid.Count;
        var dose = new float[n];
        var cells = new List<int>();
        for (int i = 0; i < n; i++)
            if (owner[i] == k && targetSoft[i] > MinTone && !edge[i] && !float.IsNaN(contrast[i])) cells.Add(i);
        if (cells.Count < 10) return new TouchUpLayer(layer, dose, cells.Count, 0, 0, 0, 0, "Too little of it is in the photo to judge.");

        // What each tone came out as at best in this burn: the darker share of its cells.
        var bins = new List<double>[ToneBins];
        for (int b = 0; b < ToneBins; b++) bins[b] = [];
        foreach (int i in cells) bins[Bin(targetSoft[i])].Add(contrast[i]);
        var best = new double?[ToneBins];
        int minCount = Math.Max(8, cells.Count / 400);
        for (int b = 0; b < ToneBins; b++)
            if (bins[b].Count >= minCount) best[b] = Percentile(bins[b], 1 - BestShare);
        // Darker tones never come out lighter than paler ones; gaps take their neighbours' values.
        var curve = Monotone(best);
        double full = curve.Max() * (1 + Math.Max(0, o.AimDarker));
        if (full < VisibleContrast) return new TouchUpLayer(layer, dose, cells.Count, 0, full, 0, 0, "It can't be seen in the photo.");

        var light = new bool[n];
        foreach (int i in cells)
        {
            double want = Expected(curve, targetSoft[i]) * (1 + Math.Max(0, o.AimDarker));
            double d = (want - contrast[i]) / full;
            if (d >= o.Sensitivity) { light[i] = true; dose[i] = (float)Math.Min(1, d); }
        }
        // Specks are noise or grain, not a burn that came out light.
        int minCells = Math.Max(1, (int)Math.Ceiling(o.MinPatchMm2 / (grid.Pitch * grid.Pitch)));
        RemoveSmall(light, grid.W, grid.H, minCells);
        int tooLight = 0;
        double worst = 0, sum = 0;
        for (int i = 0; i < n; i++)
        {
            if (!light[i]) { dose[i] = 0; continue; }
            tooLight++;
            worst = Math.Max(worst, dose[i]);
            sum += dose[i];
            deficitOut[i] = dose[i];
            dose[i] = (float)Math.Clamp(dose[i] * o.Strength, 0, 1);
        }
        return new TouchUpLayer(layer, dose, cells.Count, tooLight, full, worst, tooLight == 0 ? 0 : sum / tooLight, null);
    }

    static int Bin(double tone) => Math.Clamp((int)(tone * ToneBins), 0, ToneBins - 1);

    /// <summary>The curve through the bins, made never to fall, with tone 0 at 0 and gaps filled from either side.</summary>
    static double[] Monotone(double?[] best)
    {
        int nb = best.Length;
        var known = new List<(double Tone, double V)> { (0, 0) };
        for (int b = 0; b < nb; b++)
            if (best[b] is { } v) known.Add(((b + 0.5) / nb, v));
        var o = new double[nb];
        for (int b = 0; b < nb; b++)
        {
            double t = (b + 0.5) / nb;
            int hi = known.FindIndex(p => p.Tone >= t);
            if (hi < 0) o[b] = known[^1].V;
            else if (hi == 0 || known[hi].Tone == t) o[b] = known[hi].V;
            else
            {
                var (t0, v0) = known[hi - 1];
                var (t1, v1) = known[hi];
                o[b] = v0 + (v1 - v0) * (t - t0) / (t1 - t0);
            }
        }
        for (int b = 1; b < nb; b++) o[b] = Math.Max(o[b], o[b - 1]);
        for (int b = 0; b < nb; b++) o[b] = Math.Max(0, o[b]);
        return o;
    }

    /// <summary>What a tone should come out as, between the bins' middles (and from 0 at tone 0).</summary>
    static double Expected(double[] curve, double tone)
    {
        int nb = curve.Length;
        double pos = tone * nb - 0.5;
        if (pos <= 0) return curve[0] * Math.Clamp(tone * nb / 0.5, 0, 1);
        int b = Math.Min(nb - 2, (int)Math.Floor(pos));
        double f = Math.Clamp(pos - b, 0, 1);
        return curve[b] + (curve[b + 1] - curve[b]) * f;
    }

    // ---- what the design burns -------------------------------------------------------------

    /// <summary>How dark a layer's engraving should be in each cell: 0 (untouched) … 1 (as dark as it burns).</summary>
    public static float[] LayerTarget(DocState s, Layer layer, CellGrid g)
    {
        var t = new float[g.Count];
        foreach (var sh in s.ShapesFor(layer))
        {
            if (layer.Mode == LayerMode.Image && sh is ImageShape img) PaintPicture(img, g, t);
            else if (layer.Mode == LayerMode.Fill && sh is PathShape ps) PaintFill(ps.World.Flatten(Math.Min(0.02, g.Pitch / 4)), g, t);
        }
        return t;
    }

    static void PaintPicture(ImageShape img, CellGrid g, float[] t)
    {
        if (!img.Transform.IsInvertible) return;
        var inv = img.Transform.Invert();
        var b = img.ShownBounds;
        int x0 = Math.Max(0, (int)Math.Floor((b.MinX - g.Area.MinX) / g.Pitch)), x1 = Math.Min(g.W - 1, (int)Math.Ceiling((b.MaxX - g.Area.MinX) / g.Pitch));
        int y0 = Math.Max(0, (int)Math.Floor((b.MinY - g.Area.MinY) / g.Pitch)), y1 = Math.Min(g.H - 1, (int)Math.Ceiling((b.MaxY - g.Area.MinY) / g.Pitch));
        var tone = img.Tone;
        Parallel.For(y0, y1 + 1, y =>
        {
            for (int x = x0; x <= x1; x++)
            {
                var local = inv.Apply(g.Centre(x, y));
                if (local.X < 0 || local.Y < 0 || local.X > img.SizeMm.X || local.Y > img.SizeMm.Y || !img.InClip(local)) continue;
                var px = img.LocalToPixel(local);
                if (img.Source.SeeThrough(px.X, px.Y)) continue;
                // As the picture should look (the material's tone correction only changes what's sent to get there).
                float d = (float)(1 - tone.Apply(img.Source.SampleLuminance(px.X, px.Y)));
                int i = y * g.W + x;
                if (d > t[i]) t[i] = d;
            }
        });
    }

    /// <summary>Fill a shape's closed outlines (even-odd, holes stay empty) into the cells whose middles they cover.</summary>
    static void PaintFill(List<FlatPath> paths, CellGrid g, float[] t)
    {
        var edges = new List<(Vec2 A, Vec2 B)>();
        foreach (var p in paths.Where(p => p.Closed && p.Points.Count >= 3))
            for (int i = 0; i < p.Points.Count; i++) edges.Add((p.Points[i], p.Points[(i + 1) % p.Points.Count]));
        if (edges.Count == 0) return;
        var xs = new List<double>();
        for (int y = 0; y < g.H; y++)
        {
            double cy = g.Area.MinY + (y + 0.5) * g.Pitch;
            xs.Clear();
            foreach (var (a, b) in edges)
                if ((a.Y <= cy) != (b.Y <= cy)) xs.Add(a.X + (cy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int from = Math.Max(0, (int)Math.Ceiling((xs[k] - g.Area.MinX) / g.Pitch - 0.5));
                int to = Math.Min(g.W - 1, (int)Math.Floor((xs[k + 1] - g.Area.MinX) / g.Pitch - 0.5));
                for (int x = from; x <= to; x++) t[y * g.W + x] = 1f;
            }
        }
    }

    // ---- the photo ---------------------------------------------------------------------------

    /// <summary>The photo's brightness (0 … 1) averaged over each cell; NaN where the photo doesn't reach.</summary>
    public static float[] SamplePhoto(RasterImage photo, Rect2 area, double mmPerPx, CellGrid g)
    {
        int pw = photo.Width, ph = photo.Height;
        var lum = photo.Luminance();
        var sat = new double[(pw + 1) * (ph + 1)];
        for (int y = 0; y < ph; y++)
        {
            double row = 0;
            for (int x = 0; x < pw; x++)
            {
                row += lum[y * pw + x];
                sat[(y + 1) * (pw + 1) + x + 1] = sat[y * (pw + 1) + x + 1] + row;
            }
        }
        var o = new float[g.Count];
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                double mx0 = g.Area.MinX + x * g.Pitch, my0 = g.Area.MinY + y * g.Pitch;
                double fx0 = (mx0 - area.MinX) / mmPerPx, fy0 = (my0 - area.MinY) / mmPerPx;
                double fx1 = fx0 + g.Pitch / mmPerPx, fy1 = fy0 + g.Pitch / mmPerPx;
                if (fx0 < -0.01 || fy0 < -0.01 || fx1 > pw + 0.01 || fy1 > ph + 0.01) { o[y * g.W + x] = float.NaN; continue; }
                int px0 = Math.Clamp((int)Math.Round(fx0), 0, pw - 1), py0 = Math.Clamp((int)Math.Round(fy0), 0, ph - 1);
                int px1 = Math.Clamp((int)Math.Round(fx1), px0 + 1, pw), py1 = Math.Clamp((int)Math.Round(fy1), py0 + 1, ph);
                double sum = sat[py1 * (pw + 1) + px1] - sat[py0 * (pw + 1) + px1] - sat[py1 * (pw + 1) + px0] + sat[py0 * (pw + 1) + px0];
                o[y * g.W + x] = (float)(sum / ((px1 - px0) * (py1 - py0)));
            }
        return o;
    }

    /// <summary>The values less a wide blur of them: edges and detail, without slow changes such as the light's fall-off.</summary>
    static float[] HighPass(float[] v, int w, int h, double sigma)
    {
        var slow = Blur(v, w, h, sigma);
        var o = new float[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = v[i] - slow[i];
        return o;
    }

    /// <summary>
    /// The shift (in cells, within the padding) that puts the photo's detail best over the
    /// design's, and how well it then fits (0 … 1). Marks may come out darker or lighter than
    /// the material, so a fit either way round counts.
    /// </summary>
    static (int Dx, int Dy, double Fit) BestShift(float[] target, int w, int h, float[] photo, int bigW, int pad)
    {
        // Enough cells to judge by (every few on a big grid).
        int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 40000));
        var cells = new List<(int X, int Y, double T)>();
        for (int y = 0; y < h; y += step)
            for (int x = 0; x < w; x += step) cells.Add((x, y, target[y * w + x]));
        double Score(int dx, int dy)
        {
            double st = 0, sp = 0, stt = 0, spp = 0, stp = 0;
            int m = 0;
            foreach (var (x, y, t) in cells)
            {
                float p = photo[(y + pad + dy) * bigW + x + pad + dx];
                if (float.IsNaN(p)) continue;
                double d = p;
                st += t; sp += d; stt += t * t; spp += d * d; stp += t * d; m++;
            }
            if (m < 10) return -1;
            double cov = stp - st * sp / m, vt = stt - st * st / m, vp = spp - sp * sp / m;
            return vt <= 1e-12 || vp <= 1e-12 ? -1 : Math.Abs(cov / Math.Sqrt(vt * vp));
        }
        // Coarse steps first, then every cell round the best.
        int coarse = Math.Max(1, pad / 6);
        (int, int, double) best = (0, 0, Score(0, 0));
        for (int dy = -pad; dy <= pad; dy += coarse)
            for (int dx = -pad; dx <= pad; dx += coarse)
            {
                double sc = Score(dx, dy);
                if (sc > best.Item3 + 1e-9) best = (dx, dy, sc);
            }
        var (bx, by, _) = best;
        for (int dy = Math.Max(-pad, by - coarse); dy <= Math.Min(pad, by + coarse); dy++)
            for (int dx = Math.Max(-pad, bx - coarse); dx <= Math.Min(pad, bx + coarse); dx++)
            {
                double sc = Score(dx, dy);
                if (sc > best.Item3 + 1e-9) best = (dx, dy, sc);
            }
        return best;
    }

    /// <summary>
    /// The bare material's brightness at every cell, as the light falls: measured clear of the
    /// engraving and spread smoothly over it. One value for all when there's too little bare material.
    /// </summary>
    static float[] BareBrightness(float[] found, float[] target, CellGrid g, double sigmaMm)
    {
        int n = g.Count;
        // Clear of the engraving by a margin (the burn's own halo and any misplacement).
        int clear = Math.Max(1, (int)Math.Ceiling(Math.Max(0.8, 2 * sigmaMm) / g.Pitch));
        var burned = new bool[n];
        for (int i = 0; i < n; i++) burned[i] = target[i] > 0.02f;
        var near = Dilate(burned, g.W, g.H, clear);
        var vals = new float[n];
        var wts = new float[n];
        var bareVals = new List<double>();
        for (int i = 0; i < n; i++)
            if (!near[i] && !float.IsNaN(found[i])) { vals[i] = found[i]; wts[i] = 1; bareVals.Add(found[i]); }
        double global;
        if (bareVals.Count >= 30) global = Percentile(bareVals, 0.5);
        else
        {
            // Hardly any bare material in view: the lightest of the faintest parts stands in for it.
            var pale = Enumerable.Range(0, n).Where(i => target[i] < MinTone && !float.IsNaN(found[i])).Select(i => (double)found[i]).ToList();
            if (pale.Count < 30) pale = [.. found.Where(f => !float.IsNaN(f)).Select(f => (double)f)];
            if (pale.Count == 0) throw new TouchUpException("The photo doesn't cover the engraving.");
            global = Percentile(pale, 0.95);
            return Enumerable.Repeat((float)global, n).ToArray();
        }
        // The light's lie over the bed: a wide, smooth blur of the bare parts only.
        double sigma = Math.Max(3, 4 * sigmaMm) / g.Pitch;
        int f = Math.Max(1, (int)Math.Floor(sigma / 3));
        int sw = (g.W + f - 1) / f, sh = (g.H + f - 1) / f;
        var sv = new float[sw * sh];
        var swt = new float[sw * sh];
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                int i = y * g.W + x, j = y / f * sw + x / f;
                sv[j] += vals[i] * wts[i];
                swt[j] += wts[i];
            }
        var bv = BlurRaw(sv, sw, sh, sigma / f);
        var bw = BlurRaw(swt, sw, sh, sigma / f);
        double minW = 0.02 * f * f;
        var o = new float[n];
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                // Between the coarse cells' middles.
                double fx = Math.Clamp((x + 0.5) / f - 0.5, 0, sw - 1), fy = Math.Clamp((y + 0.5) / f - 0.5, 0, sh - 1);
                int ax = (int)fx, ay = (int)fy, bx = Math.Min(sw - 1, ax + 1), by = Math.Min(sh - 1, ay + 1);
                double tx = fx - ax, ty = fy - ay;
                double V(float[] a) => (a[ay * sw + ax] * (1 - tx) + a[ay * sw + bx] * tx) * (1 - ty) + (a[by * sw + ax] * (1 - tx) + a[by * sw + bx] * tx) * ty;
                double wsum = V(bw);
                o[y * g.W + x] = wsum > minW ? (float)(V(bv) / wsum) : (float)global;
            }
        return o;
    }

    // ---- small helpers -----------------------------------------------------------------------

    static Rect2 Intersect(Rect2 a, Rect2 b)
    {
        var r = new Rect2(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY));
        return r.MaxX > r.MinX && r.MaxY > r.MinY ? r : Rect2.Empty;
    }

    /// <summary>The value below which this share of the values lie.</summary>
    public static double Percentile(List<double> v, double share)
    {
        if (v.Count == 0) return double.NaN;
        var a = v.ToArray();
        Array.Sort(a);
        double pos = Math.Clamp(share, 0, 1) * (a.Length - 1);
        int lo = (int)Math.Floor(pos), hi = Math.Min(a.Length - 1, lo + 1);
        return a[lo] + (a[hi] - a[lo]) * (pos - lo);
    }

    /// <summary>Gaussian blur that skips missing (NaN) cells: each cell the weighted average of the cells around it that have a value.</summary>
    public static float[] Blur(float[] v, int w, int h, double sigma)
    {
        if (sigma < 0.3) return (float[])v.Clone();
        var val = new float[v.Length];
        var wt = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
            if (!float.IsNaN(v[i])) { val[i] = v[i]; wt[i] = 1; }
        var bv = BlurRaw(val, w, h, sigma);
        var bw = BlurRaw(wt, w, h, sigma);
        var o = new float[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = float.IsNaN(v[i]) || bw[i] < 1e-4 ? float.NaN : bv[i] / bw[i];
        return o;
    }

    /// <summary>Plain separable Gaussian blur (edges: what's beyond counts as zero).</summary>
    static float[] BlurRaw(float[] v, int w, int h, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new float[2 * r + 1];
        for (int i = -r; i <= r; i++) k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
        float ks = k.Sum();
        for (int i = 0; i < k.Length; i++) k[i] /= ks;
        var tmp = new float[v.Length];
        var o = new float[v.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                int from = Math.Max(-r, -x), to = Math.Min(r, w - 1 - x);
                for (int d = from; d <= to; d++) s += k[d + r] * v[y * w + x + d];
                tmp[y * w + x] = s;
            }
        });
        Parallel.For(0, h, y =>
        {
            int from = Math.Max(-r, -y), to = Math.Min(r, h - 1 - y);
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int d = from; d <= to; d++) s += k[d + r] * tmp[(y + d) * w + x];
                o[y * w + x] = s;
            }
        });
        return o;
    }

    /// <summary>Cells within <paramref name="r"/> cells (a square) of a set cell.</summary>
    static bool[] Dilate(bool[] m, int w, int h, int r)
    {
        var tmp = new bool[m.Length];
        var o = new bool[m.Length];
        for (int y = 0; y < h; y++)
        {
            int last = int.MinValue / 2;
            for (int x = 0; x < w; x++) { if (m[y * w + x]) last = x; tmp[y * w + x] = x - last <= r; }
            last = int.MaxValue / 2;
            for (int x = w - 1; x >= 0; x--) { if (m[y * w + x]) last = x; if (last - x <= r) tmp[y * w + x] = true; }
        }
        for (int x = 0; x < w; x++)
        {
            int last = int.MinValue / 2;
            for (int y = 0; y < h; y++) { if (tmp[y * w + x]) last = y; o[y * w + x] = y - last <= r; }
            last = int.MaxValue / 2;
            for (int y = h - 1; y >= 0; y--) { if (tmp[y * w + x]) last = y; if (last - y <= r) o[y * w + x] = true; }
        }
        return o;
    }

    /// <summary>Cells where the design's darkness changes by more than a third within <paramref name="r"/> cells.</summary>
    static bool[] Edges(float[] t, int w, int h, int r)
    {
        var hiRow = new float[t.Length];
        var loRow = new float[t.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float hi = 0, lo = 1;
                for (int xx = Math.Max(0, x - r); xx <= Math.Min(w - 1, x + r); xx++) { float v = t[y * w + xx]; hi = Math.Max(hi, v); lo = Math.Min(lo, v); }
                hiRow[y * w + x] = hi; loRow[y * w + x] = lo;
            }
        });
        var o = new bool[t.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float hi = 0, lo = 1;
                for (int yy = Math.Max(0, y - r); yy <= Math.Min(h - 1, y + r); yy++) { hi = Math.Max(hi, hiRow[yy * w + x]); lo = Math.Min(lo, loRow[yy * w + x]); }
                o[y * w + x] = hi - lo > 0.35f;
            }
        });
        return o;
    }

    /// <summary>Clear connected patches (four-way) of fewer than <paramref name="minCells"/> cells.</summary>
    static void RemoveSmall(bool[] m, int w, int h, int minCells)
    {
        if (minCells <= 1) return;
        var seen = new bool[m.Length];
        var stack = new Stack<int>();
        var region = new List<int>();
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || seen[start]) continue;
            region.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                region.Add(i);
                if (x > 0 && m[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                if (x < w - 1 && m[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                if (y > 0 && m[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                if (y < h - 1 && m[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
            }
            if (region.Count < minCells) foreach (int i in region) m[i] = false;
        }
    }
}
