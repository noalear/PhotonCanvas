using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>Which part of a battle map is the floor: worked out from the picture, or the lighter or darker part.</summary>
public enum FloorTone { Auto, Lighter, Darker }

public sealed record ReadOptions
{
    public FloorTone Floor { get; init; } = FloorTone.Auto;
    /// <summary>The brightness (0 black … 1 white) between floor and not floor; NaN = worked out from the picture.</summary>
    public double Threshold { get; init; } = double.NaN;
    /// <summary>Look for walls drawn as dark lines along the grid (between rooms, or all of them on a line drawing).</summary>
    public bool WallLines { get; init; } = true;
}

/// <summary>What reading a battle map found: the plan, the brightness it split floor from the rest at, and notes on how it went.</summary>
public sealed record ReadResult(PlanGrid Plan, double Threshold, List<string> Notes);

/// <summary>
/// Reads a floor plan from a battle map picture: finds the grid (the period of the lines across and
/// down), sorts the squares into floor and not floor by how bright their middles are (split where the
/// picture's own tones split best), and finds walls drawn as thick dark lines along the grid. Doors
/// aren't read: they're marked by hand afterwards. Also makes a plan from closed shapes in the design.
/// </summary>
public static class MapReader
{
    /// <summary>The grid in a picture: one square's size (pixels) and where the first full square's top left corner is. Null if no grid shows.</summary>
    public static MapPicture? FindGrid(RasterImage img)
    {
        var l = img.Luminance();
        int w = img.Width, h = img.Height;
        if (w < 32 || h < 32) return null;
        var across = Profile(l, w, h, true);
        var down = Profile(l, w, h, false);
        var px = Period(across, Math.Min(400, w / 2.0));
        var py = Period(down, Math.Min(400, h / 2.0));
        if (px is null && py is null) return null;
        double p;
        if (px is { } a && py is { } b)
            p = Math.Abs(a.Period - b.Period) / a.Period < 0.06 ? (a.Period + b.Period) / 2 : a.Strength >= b.Strength ? a.Period : b.Period;
        else p = (px ?? py)!.Value.Period;
        double ox = Phase(across, p), oy = Phase(down, p);
        return new MapPicture { SquarePx = Math.Round(p, 2), OffsetX = Math.Round(ox, 1), OffsetY = Math.Round(oy, 1) };
    }

    /// <summary>The grid for a picture with no lines to find: so many squares across it, from its top left corner.</summary>
    public static MapPicture GridFor(RasterImage img, int squaresAcross) => new() { SquarePx = Math.Round(img.Width / (double)Math.Max(1, squaresAcross), 3) };

    /// <summary>How sharply the brightness changes at each column (or row), with the picture's own texture taken away.</summary>
    static double[] Profile(float[] l, int w, int h, bool columns)
    {
        int n = columns ? w : h, m = columns ? h : w, step = Math.Max(1, m / 600);
        var raw = new double[n];
        for (int i = 1; i < n; i++)
        {
            double sum = 0;
            int count = 0;
            for (int j = 0; j < m; j += step)
            {
                float a = columns ? l[j * w + i] : l[i * w + j], b = columns ? l[j * w + i - 1] : l[(i - 1) * w + j];
                sum += Math.Abs(a - b);
                count++;
            }
            raw[i] = sum / Math.Max(1, count);
        }
        // Peaks over the local level: grid lines stand out, the texture between them doesn't.
        var res = new double[n];
        const int k = 6;
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            int c = 0;
            for (int j = Math.Max(0, i - k); j <= Math.Min(n - 1, i + k); j++) { s += raw[j]; c++; }
            res[i] = Math.Max(0, raw[i] - s / c);
        }
        return res;
    }

    /// <summary>The comb's mean over lines every <paramref name="p"/> pixels from <paramref name="phase"/> (each line ±1 pixel).</summary>
    static double Comb(double[] f, double p, double phase)
    {
        double sum = 0;
        int count = 0;
        for (double x = phase; x < f.Length - 1; x += p)
        {
            int i = (int)Math.Round(x);
            double best = 0;
            for (int d = -1; d <= 1; d++) if (i + d >= 0 && i + d < f.Length) best = Math.Max(best, f[i + d]);
            sum += best;
            count++;
        }
        return count < 2 ? 0 : sum / count;
    }

    /// <summary>
    /// The grid's period in a profile and how strongly it shows, or null when nothing repeats: the
    /// profile's autocorrelation (its strongest peaks clipped, so a few big edges between rooms and
    /// rock don't outweigh the many grid lines), at its first peak nearly as high as the highest
    /// (twice the period lines up as well as the period itself), refined to a twentieth of a pixel.
    /// </summary>
    static (double Period, double Strength)? Period(double[] raw, double most)
    {
        int n = raw.Length;
        var sorted = raw.Where(v => v > 0).OrderBy(v => v).ToArray();
        if (sorted.Length < 8) return null;
        double cap = sorted[(int)(sorted.Length * 0.97)];
        var f = raw.Select(v => Math.Min(v, cap)).ToArray();
        double mean = f.Average();
        for (int i = 0; i < n; i++) f[i] -= mean;
        double R(double p)
        {
            int ip = (int)Math.Floor(p);
            double t = p - ip, sum = 0;
            int count = 0;
            for (int x = 0; x + ip + 1 < n; x++)
            {
                sum += f[x] * (f[x + ip] * (1 - t) + f[x + ip + 1] * t);
                count++;
            }
            return count == 0 ? 0 : sum / count;
        }
        double r0 = R(0);
        if (r0 <= 1e-12) return null;
        int hi = (int)Math.Min(most, n / 3.0);
        if (hi < 8) return null;
        var r = new double[hi + 2];
        for (int p = 6; p <= hi + 1; p++) r[p] = R(p) / r0;
        double top = 0;
        for (int p = 8; p <= hi; p++) top = Math.Max(top, r[p]);
        if (top < 0.12) return null;
        int pick = -1;
        for (int p = 8; p <= hi; p++)
            if (r[p] >= 0.7 * top && r[p] >= r[p - 1] && r[p] >= r[p + 1]) { pick = p; break; }
        if (pick < 0) return null;
        double bestP = pick, bestS = r[pick];
        for (double p = pick - 1; p <= pick + 1 + 1e-9; p += 0.05)
        {
            double v = R(p) / r0;
            if (v > bestS) { bestS = v; bestP = p; }
        }
        // Over many squares, a small error adds up: the peak a few squares along pins the period down finer.
        int k = Math.Max(1, (int)Math.Floor(n / 2.0 / bestP));
        double at = bestP, peak = double.MinValue;
        for (double q = k * bestP - 2; q <= k * bestP + 2 + 1e-9; q += 0.05)
        {
            double v = R(q);
            if (v > peak) { peak = v; at = q / k; }
        }
        return (Math.Round(at, 2), bestS);
    }

    /// <summary>
    /// Where the grid's lines are for a period: the first one, from 0 up to one square. A dark (or light)
    /// line shows as two steps in brightness, into it and out of it: the line is between them.
    /// </summary>
    static double Phase(double[] f, double p)
    {
        double Exact(double ph)
        {
            double sum = 0;
            int count = 0;
            for (double x = ph; Math.Round(x) < f.Length; x += p) { sum += f[(int)Math.Round(x)]; count++; }
            return count == 0 ? 0 : sum / count;
        }
        int steps = (int)Math.Ceiling(p);
        var score = new double[steps];
        int at = 0;
        for (int ph = 0; ph < steps; ph++) { score[ph] = Exact(ph); if (score[ph] > score[at]) at = ph; }
        double best = score[at];
        if (best <= 0) return 0;
        double next = score[(at + 1) % steps], before = score[(at - 1 + steps) % steps];
        double res = next >= 0.6 * best && next >= before ? at + 0.5 : before >= 0.6 * best ? at - 0.5 : at;
        return (res % p + p) % p;
    }

    /// <summary>Reads the floor (and drawn walls) from a picture on the grid given.</summary>
    public static ReadResult Read(RasterImage img, MapPicture grid, ReadOptions o)
    {
        var notes = new List<string>();
        var l = img.Luminance();
        int w = img.Width, h = img.Height;
        double p = Math.Max(4, grid.SquarePx), ox = grid.OffsetX, oy = grid.OffsetY;
        // Squares whose middle is on the picture; a part square left of or above the first line counts too.
        double x0 = ox - Math.Floor(ox / p) * p, y0 = oy - Math.Floor(oy / p) * p;
        if (x0 > p * 0.5) x0 -= p;
        if (y0 > p * 0.5) y0 -= p;
        int across = Math.Clamp((int)Math.Floor((w - x0) / p + 0.5), 1, MapPlan.MaxSide);
        int down = Math.Clamp((int)Math.Floor((h - y0) / p + 0.5), 1, MapPlan.MaxSide);
        if ((w - x0) / p > MapPlan.MaxSide + 0.5 || (h - y0) / p > MapPlan.MaxSide + 0.5)
            notes.Add($"The map is more than {MapPlan.MaxSide} squares across: only its top left {across} × {down} squares are read.");

        double Mean(double ax, double ay, double bx, double by, out double darkShare, double darkBelow = -1)
        {
            int i0 = Math.Clamp((int)Math.Floor(ax), 0, w - 1), i1 = Math.Clamp((int)Math.Ceiling(bx), i0 + 1, w);
            int j0 = Math.Clamp((int)Math.Floor(ay), 0, h - 1), j1 = Math.Clamp((int)Math.Ceiling(by), j0 + 1, h);
            int sx = Math.Max(1, (i1 - i0) / 24), sy = Math.Max(1, (j1 - j0) / 24);
            double sum = 0;
            int n = 0, dark = 0;
            for (int j = j0; j < j1; j += sy)
                for (int i = i0; i < i1; i += sx)
                {
                    float v = l[j * w + i];
                    sum += v;
                    n++;
                    if (v < darkBelow) dark++;
                }
            darkShare = n == 0 ? 0 : (double)dark / n;
            return n == 0 ? 1 : sum / n;
        }

        // Each square's middle (its inner half), how bright.
        var m = new double[across, down];
        var all = new List<double>();
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                double cx = x0 + x * p, cy = y0 + y * p;
                m[x, y] = Mean(cx + p * 0.25, cy + p * 0.25, cx + p * 0.75, cy + p * 0.75, out _);
                all.Add(m[x, y]);
            }
        var (otsu, spread) = Otsu(all);
        double t = double.IsNaN(o.Threshold) ? otsu : Math.Clamp(o.Threshold, 0, 1);
        var plan = new PlanGrid(across, down);
        bool even = double.IsNaN(o.Threshold) && spread < 0.08;
        if (even)
        {
            // One tone all over (a line drawing): every square is floor until the walls say otherwise.
            for (int y = 0; y < down; y++) for (int x = 0; x < across; x++) plan[x, y] = true;
            notes.Add("The squares are all much the same brightness, so the map is read as walls drawn in lines.");
        }
        else
        {
            bool lighter = o.Floor switch
            {
                FloorTone.Lighter => true,
                FloorTone.Darker => false,
                // The outside is whatever the edge of the map is mostly made of.
                _ => Ring(across, down).Count(c => m[c.X, c.Y] > t) <= Ring(across, down).Count(c => m[c.X, c.Y] <= t),
            };
            for (int y = 0; y < down; y++)
                for (int x = 0; x < across; x++) plan[x, y] = lighter ? m[x, y] > t : m[x, y] <= t;
            if (o.Floor == FloorTone.Auto) notes.Add($"The floor is the {(lighter ? "lighter" : "darker")} part of the map (the edge of the map is mostly the other).");
        }

        int lines = 0;
        if (o.WallLines)
        {
            // A wall drawn along an edge: a strip along the grid line much darker than the squares either side, most of the way along.
            double half = Math.Max(1.5, p * 0.08);
            foreach (var e in plan.Edges())
            {
                var (a, b) = e.Sides;
                if (!plan.In(a.X, a.Y) || !plan.In(b.X, b.Y)) continue;
                double side = Math.Min(m[a.X, a.Y], m[b.X, b.Y]);
                double lx = x0 + e.X * p, ly = y0 + e.Y * p, ms, share;
                if (e.Across) ms = Mean(lx + p * 0.15, ly - half, lx + p * 0.85, ly + half, out share, side - 0.2);
                else ms = Mean(lx - half, ly + p * 0.15, lx + half, ly + p * 0.85, out share, side - 0.2);
                if (side - ms < 0.2 || share < 0.5) continue;
                if (plan[a.X, a.Y] && plan[b.X, b.Y]) plan.SetMark(e, EdgeKind.Wall);
                lines++;
            }
            if (even)
            {
                // The outside: squares reached from the map's edge without crossing a drawn wall.
                var outside = new bool[across, down];
                var todo = new Stack<(int X, int Y)>();
                foreach (var c in Ring(across, down)) if (!outside[c.X, c.Y]) { outside[c.X, c.Y] = true; todo.Push(c); }
                while (todo.Count > 0)
                {
                    var (x, y) = todo.Pop();
                    foreach (var (nx, ny, e) in new[] { (x + 1, y, new EdgeId(x + 1, y, false)), (x - 1, y, new EdgeId(x, y, false)), (x, y + 1, new EdgeId(x, y + 1, true)), (x, y - 1, new EdgeId(x, y, true)) })
                        if (plan.In(nx, ny) && !outside[nx, ny] && plan.Mark(e) != EdgeKind.Wall) { outside[nx, ny] = true; todo.Push((nx, ny)); }
                }
                int outCount = 0;
                foreach (var b in outside) if (b) outCount++;
                if (outCount < across * down * 0.9 && outCount > 0)
                {
                    for (int y = 0; y < down; y++) for (int x = 0; x < across; x++) if (outside[x, y]) plan[x, y] = false;
                    notes.Add("Squares outside the drawn walls are left out.");
                }
                else notes.Add("The walls don't close the rooms off from the edge of the map: rub out the squares outside the rooms.");
            }
            if (lines > 0) notes.Add($"{lines} wall{(lines == 1 ? "" : "s")} drawn along the grid found.");
        }
        notes.Add("Doors aren't read from the picture: mark them with the Door tool.");
        return new ReadResult(plan, Math.Round(t, 3), notes);
    }

    static IEnumerable<(int X, int Y)> Ring(int across, int down)
    {
        for (int x = 0; x < across; x++) { yield return (x, 0); if (down > 1) yield return (x, down - 1); }
        for (int y = 1; y < down - 1; y++) { yield return (0, y); if (across > 1) yield return (across - 1, y); }
    }

    /// <summary>The brightness that splits the values into two groups best (Otsu), and how far apart the groups' means are.</summary>
    public static (double Threshold, double Spread) Otsu(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return (0.5, 0);
        var sorted = v.OrderBy(x => x).ToArray();
        double total = sorted.Sum(), best = -1, at = 0.5, spread = 0, left = 0;
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            left += sorted[i];
            int n0 = i + 1, n1 = sorted.Length - n0;
            double m0 = left / n0, m1 = (total - left) / n1, between = (double)n0 * n1 * (m1 - m0) * (m1 - m0);
            if (between > best) { best = between; at = (sorted[i] + sorted[i + 1]) / 2; spread = m1 - m0; }
        }
        return (at, spread);
    }

    /// <summary>
    /// A plan from closed outlines in the design (mm, y down): a square is floor where its middle is
    /// inside them. The grid starts at the outlines' top left corner.
    /// </summary>
    public static PlanGrid FromOutlines(IReadOnlyList<IReadOnlyList<(double X, double Y)>> loops, double squareMm)
    {
        var pts = loops.SelectMany(q => q).ToList();
        if (pts.Count == 0) return new PlanGrid(1, 1);
        double g = Math.Max(1, squareMm), minX = pts.Min(q => q.X), minY = pts.Min(q => q.Y), maxX = pts.Max(q => q.X), maxY = pts.Max(q => q.Y);
        int across = Math.Clamp((int)Math.Ceiling((maxX - minX) / g - 0.2), 1, MapPlan.MaxSide);
        int down = Math.Clamp((int)Math.Ceiling((maxY - minY) / g - 0.2), 1, MapPlan.MaxSide);
        var plan = new PlanGrid(across, down);
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                double px = minX + (x + 0.5) * g, py = minY + (y + 0.5) * g;
                int inside = 0;
                foreach (var loop in loops) if (Inside(loop, px, py)) inside++;
                plan[x, y] = inside % 2 == 1;
            }
        return plan;
    }

    static bool Inside(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
        }
        return c;
    }
}
