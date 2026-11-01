using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// The artistic side of grain-aware engraving: where on the board to put a picture so its dark
/// parts land on dark grain and its light parts on light grain, and the wood's own figure becomes
/// part of the picture.
/// </summary>
public static class GrainPlacement
{
    /// <param name="Transform">The picture's new transform.</param>
    /// <param name="Score">How well the picture's darks follow the grain there (−1 to 1, the correlation).</param>
    /// <param name="ScoreHere">The same where the picture is now (NaN if it isn't all on the mapped board).</param>
    /// <param name="TurnDeg">How far it was turned (0, 90, 180 or 270).</param>
    /// <param name="Move">How far its middle moved, mm.</param>
    public sealed record Result(Affine Transform, double Score, double ScoreHere, double TurnDeg, Vec2 Move);

    /// <summary>Coarse search step, mm (refined afterwards to the map's own cell).</summary>
    public const double CoarseMm = 2;

    /// <summary>
    /// The best place for <paramref name="img"/> on the mapped board, the whole picture on known grain
    /// and inside <paramref name="within"/> (null = anywhere on the map). Turns by quarter turns too
    /// when <paramref name="allowTurn"/>. Null when the picture doesn't fit on the mapped board.
    /// </summary>
    public static Result? Best(GrainMap map, ImageShape img, Rect2? within = null, bool allowTurn = false, CancellationToken cancel = default)
    {
        var region = map.Area;
        if (within is { } w) region = new Rect2(Math.Max(region.MinX, w.MinX), Math.Max(region.MinY, w.MinY), Math.Min(region.MaxX, w.MaxX), Math.Min(region.MaxY, w.MaxY));
        if (!(region.MaxX > region.MinX && region.MaxY > region.MinY) || !img.Transform.IsInvertible) return null;
        var centre = img.Bounds.Center;
        double coarse = Math.Max(CoarseMm, map.CellMm), fine = Math.Max(map.CellMm, 0.5);
        Result? best = null;
        foreach (double turn in allowTurn ? new[] { 0.0, 90, 180, 270 } : [0.0])
        {
            cancel.ThrowIfCancellationRequested();
            var t = img.Transform.Then(Affine.RotateAbout(centre, turn));
            var pic = Darkness(img, t, coarse);
            var grain = Grain(map, region, coarse);
            var hit = Search(pic, grain, 0, grain.W - pic.W, 0, grain.H - pic.H, cancel);
            if (hit is not { } h) continue;
            // Refined around the coarse best, on the map's own cells.
            var target = new Vec2(grain.Origin.X + h.X * coarse, grain.Origin.Y + h.Y * coarse);
            var move = target - pic.Origin;
            var t2 = t.Then(Affine.Translate(move));
            var picF = Darkness(img, t2, fine);
            var near = new Rect2(picF.Origin.X - coarse * 1.5, picF.Origin.Y - coarse * 1.5, picF.Origin.X + picF.W * fine + coarse * 1.5, picF.Origin.Y + picF.H * fine + coarse * 1.5);
            near = new Rect2(Math.Max(near.MinX, region.MinX), Math.Max(near.MinY, region.MinY), Math.Min(near.MaxX, region.MaxX), Math.Min(near.MaxY, region.MaxY));
            var grainF = Grain(map, near, fine);
            if (Search(picF, grainF, 0, grainF.W - picF.W, 0, grainF.H - picF.H, cancel) is { } hf)
            {
                var targetF = new Vec2(grainF.Origin.X + hf.X * fine, grainF.Origin.Y + hf.Y * fine);
                t2 = t2.Then(Affine.Translate(targetF - picF.Origin));
                h = hf;
            }
            if (best is null || h.Score > best.Score)
                best = new Result(t2, h.Score, double.NaN, turn, t2.Apply(img.Transform.Invert().Apply(centre)) - centre);
        }
        if (best is null) return null;
        return best with { ScoreHere = ScoreAt(map, img, img.Transform) };
    }

    /// <summary>How well the picture's darks follow the grain with this transform (NaN if it isn't all on known grain).</summary>
    public static double ScoreAt(GrainMap map, ImageShape img, Affine transform)
    {
        double fine = Math.Max(map.CellMm, 0.5);
        var pic = Darkness(img, transform, fine);
        var grain = Grain(map, new Rect2(pic.Origin.X, pic.Origin.Y, pic.Origin.X + pic.W * fine, pic.Origin.Y + pic.H * fine), fine);
        if (grain.W < pic.W || grain.H < pic.H) return double.NaN;
        return Search(pic, grain, 0, 0, 0, 0, default) is { } h ? h.Score : double.NaN;
    }

    /// <summary>A grid of values (NaN = none) whose cell (0, 0) has its corner at <see cref="Origin"/>.</summary>
    internal sealed record Grid(Vec2 Origin, int W, int H, float[] V);

    /// <summary>The picture's darkness (0 white – 1 black, NaN outside it or see-through) on a grid over where <paramref name="transform"/> puts it.</summary>
    internal static Grid Darkness(ImageShape img, Affine transform, double cell)
    {
        var corners = new[] { new Vec2(0, 0), new Vec2(img.SizeMm.X, 0), img.SizeMm, new Vec2(0, img.SizeMm.Y) }.Select(transform.Apply).ToList();
        var b = Rect2.Bounds(corners);
        // Snapped to the cell grid, so the picture can move in whole cells.
        int w = Math.Max(1, (int)Math.Ceiling(b.Width / cell)), h = Math.Max(1, (int)Math.Ceiling(b.Height / cell));
        var origin = b.TopLeft;
        var toLocal = transform.Invert();
        var v = new float[w * h];
        var tone = img.Tone;
        Parallel.For(0, h, r =>
        {
            for (int c = 0; c < w; c++)
            {
                var local = toLocal.Apply(new Vec2(origin.X + (c + 0.5) * cell, origin.Y + (r + 0.5) * cell));
                float d = float.NaN;
                if (local.X >= 0 && local.Y >= 0 && local.X <= img.SizeMm.X && local.Y <= img.SizeMm.Y && img.InClip(local))
                {
                    var px = img.LocalToPixel(local);
                    if (!img.Source.SeeThrough(px.X, px.Y)) d = 1 - (float)tone.Apply(img.Source.SampleLuminance(px.X, px.Y));
                }
                v[r * w + c] = d;
            }
        });
        return new Grid(origin, w, h, v);
    }

    /// <summary>The grain map averaged onto a grid of <paramref name="cell"/> mm over <paramref name="area"/>.</summary>
    internal static Grid Grain(GrainMap map, Rect2 area, double cell)
    {
        int w = Math.Max(0, (int)Math.Floor(area.Width / cell + 1e-9)), h = Math.Max(0, (int)Math.Floor(area.Height / cell + 1e-9));
        var v = new float[w * h];
        int sub = Math.Clamp((int)Math.Round(cell / map.CellMm), 1, 8);
        Parallel.For(0, h, r =>
        {
            for (int c = 0; c < w; c++)
            {
                double sum = 0;
                int n = 0;
                bool unknown = false;
                for (int j = 0; j < sub && !unknown; j++)
                    for (int i = 0; i < sub; i++)
                    {
                        double t = map.At(new Vec2(area.MinX + (c + (i + 0.5) / sub) * cell, area.MinY + (r + (j + 0.5) / sub) * cell));
                        if (double.IsNaN(t)) { unknown = true; break; }
                        sum += t;
                        n++;
                    }
                v[r * w + c] = unknown || n == 0 ? float.NaN : (float)(sum / n);
            }
        });
        return new Grid(area.TopLeft, w, h, v);
    }

    /// <summary>
    /// The offset (in cells) of <paramref name="pic"/> on <paramref name="grain"/>, within the ranges,
    /// where the picture's darkness follows the grain best: the highest correlation, the whole picture
    /// on known grain. Null if it fits nowhere.
    /// </summary>
    internal static (int X, int Y, double Score)? Search(Grid pic, Grid grain, int x0, int x1, int y0, int y1, CancellationToken cancel)
    {
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(grain.W - pic.W, x1); y1 = Math.Min(grain.H - pic.H, y1);
        if (x1 < x0 || y1 < y0) return null;
        // The picture's cells that count, centred (the mean drops out of the correlation's top half).
        var cells = new List<(int Dx, int Dy, double P)>();
        double mean = 0;
        for (int r = 0; r < pic.H; r++)
            for (int c = 0; c < pic.W; c++)
                if (!float.IsNaN(pic.V[r * pic.W + c])) { cells.Add((c, r, pic.V[r * pic.W + c])); mean += pic.V[r * pic.W + c]; }
        if (cells.Count < 4) return null;
        mean /= cells.Count;
        double spp = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var (dx, dy, p) = cells[i];
            cells[i] = (dx, dy, p - mean);
            spp += (p - mean) * (p - mean);
        }
        if (spp < 1e-9) return null; // a flat picture fits anywhere equally
        int n = cells.Count;
        var bestByRow = new (int X, double Score)[y1 - y0 + 1];
        Parallel.For(y0, y1 + 1, new ParallelOptions { CancellationToken = cancel }, oy =>
        {
            int bx = -1;
            double bs = double.NegativeInfinity;
            for (int ox = x0; ox <= x1; ox++)
            {
                double spg = 0, sg = 0, sgg = 0;
                bool ok = true;
                foreach (var (dx, dy, p) in cells)
                {
                    float g = grain.V[(oy + dy) * grain.W + ox + dx];
                    if (float.IsNaN(g)) { ok = false; break; }
                    spg += p * g;
                    sg += g;
                    sgg += g * g;
                }
                if (!ok) continue;
                double vg = sgg - sg * sg / n;
                double score = vg < 1e-9 ? 0 : spg / Math.Sqrt(spp * vg);
                if (score > bs) { bs = score; bx = ox; }
            }
            bestByRow[oy - y0] = (bx, bs);
        });
        int by = -1;
        for (int i = 0; i < bestByRow.Length; i++)
            if (bestByRow[i].X >= 0 && (by < 0 || bestByRow[i].Score > bestByRow[by].Score)) by = i;
        return by < 0 ? null : (bestByRow[by].X, by + y0, bestByRow[by].Score);
    }
}
