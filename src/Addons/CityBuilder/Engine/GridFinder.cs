using PhotonCanvas.Addon.MapTerrain;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Finds the grid on a battle map: thin lines (dark or light) that repeat every so many pixels right
/// across the picture. Each column (and row) is scored by how many thin lines cross it, the score
/// with the picture's own texture taken away; then every spacing from 8 pixels up is tried as a
/// comb over that profile, and the smallest spacing nearly as strong as the strongest wins (twice
/// the spacing lines up just as well, half of it only half as well). The lines found are then fitted
/// to pin the spacing down to a hundredth of a pixel, and the two directions are made to agree.
/// </summary>
public static class GridFinder
{
    /// <summary>How well a spacing fits a direction: the spacing (pixels), where its first line is, how much stronger its lines are than the texture (1 = no better) and the share of the lines expected that show.</summary>
    public readonly record struct Fit(double Period, double Phase, double Strength, double Share);

    /// <summary>The grid in a picture, or null when no lines repeat.</summary>
    public static MapPicture? Find(RasterImage img)
    {
        int w = img.Width, h = img.Height;
        if (w < 32 || h < 32) return null;
        var lum = img.Luminance();
        var across = Profiles(lum, w, h, true);
        var down = Profiles(lum, w, h, false);
        double most = Math.Min(500, Math.Min(w, h) / 3.0);
        // Dark lines and light lines are tried apart: a grid is one or the other.
        var fx = Best(Period(across.Dark, most), Period(across.Light, most));
        var fy = Best(Period(down.Dark, most), Period(down.Light, most));
        if (fx is null && fy is null) return null;
        double p;
        if (fx is { } a && fy is { } b)
        {
            // The same spacing both ways (squares), unless one direction is much surer.
            if (Math.Abs(a.Period - b.Period) / Math.Max(a.Period, b.Period) < 0.04) p = (a.Period * a.Strength + b.Period * b.Strength) / (a.Strength + b.Strength);
            else p = a.Strength >= b.Strength ? a.Period : b.Period;
        }
        else p = (fx ?? fy)!.Value.Period;
        // Each direction's lines at the shared spacing, fitted again so the first line is exact.
        var px = Refit(across.Dark, across.Light, p) ?? Refit(down.Dark, down.Light, p);
        var py = Refit(down.Dark, down.Light, p) ?? px;
        if (px is null || py is null) return null;
        double period = (px.Value.Period * px.Value.Share + py.Value.Period * py.Value.Share) / Math.Max(1e-9, px.Value.Share + py.Value.Share);
        if (Math.Abs(period - p) / p > 0.03) period = p;
        double ox = Phase(across.Dark, across.Light, period, px.Value.Phase), oy = Phase(down.Dark, down.Light, period, py.Value.Phase);
        return new MapPicture { SquarePx = Math.Round(period, 2), OffsetX = Math.Round(ox, 1), OffsetY = Math.Round(oy, 1) };
    }

    static Fit? Best(Fit? a, Fit? b) => a is null ? b : b is null ? a : a.Value.Strength * a.Value.Share >= b.Value.Strength * b.Value.Share ? a : b;

    /// <summary>How much thin dark (and thin light) line crosses each column (or each row): the line's depth below (height above) both its sides, summed; then the local level taken away so only lines stand out.</summary>
    internal static (double[] Dark, double[] Light) Profiles(float[] lum, int w, int h, bool columns)
    {
        int n = columns ? w : h, m = columns ? h : w;
        var dark = new double[n];
        var light = new double[n];
        // Rows (or columns) are all used up to about a thousand: enough for a sure profile.
        int step = Math.Max(1, m / 1000);
        for (int j = 0; j < m; j += step)
        {
            for (int i = 6; i < n - 6; i++)
            {
                float v = columns ? lum[j * w + i] : lum[i * w + j];
                // A line one to about eight pixels wide (a soft one on a blurred or upscaled map too): darker
                // than both its sides some pixels away.
                float side = 0, sideLight = 1;
                foreach (int d in Widths)
                {
                    float l = columns ? lum[j * w + i - d] : lum[(i - d) * w + j], r = columns ? lum[j * w + i + d] : lum[(i + d) * w + j];
                    side = Math.Max(side, Math.Min(l, r));
                    sideLight = Math.Min(sideLight, Math.Max(l, r));
                }
                float dk = side - v;
                if (dk > 0.025f) dark[i] += Math.Min(dk, 0.35f);
                float u = v - sideLight;
                if (u > 0.025f) light[i] += Math.Min(u, 0.35f);
            }
        }
        return (Peaks(dark), Peaks(light));
    }

    /// <summary>A profile with its local level (the texture) taken away: what's left is the lines.</summary>
    static readonly int[] Widths = [2, 3, 4, 6];

    static double[] Peaks(double[] raw)
    {
        int n = raw.Length;
        const int k = 10;
        var res = new double[n];
        double sum = 0;
        int count = 0;
        for (int i = 0; i < Math.Min(n, k); i++) { sum += raw[i]; count++; }
        for (int i = 0; i < n; i++)
        {
            if (i + k < n) { sum += raw[i + k]; count++; }
            if (i - k - 1 >= 0) { sum -= raw[i - k - 1]; count--; }
            res[i] = Math.Max(0, raw[i] - sum / Math.Max(1, count));
        }
        return res;
    }

    /// <summary>
    /// How well lines every <paramref name="p"/> from <paramref name="phase"/> fit the profile: the mean of
    /// the profile at them (each line the best of three pixels), the strongest quarter left out and each
    /// capped, so a few heavy lines (the roofs' outlines) can't make a sparse comb look good; and how many
    /// of those lines show.
    /// </summary>
    static (double Mean, double Share) Comb(double[] f, double p, double phase, double showing, double cap)
    {
        var teeth = new List<double>();
        int shown = 0;
        for (double x = phase; x < f.Length - 1; x += p)
        {
            int i = (int)Math.Round(x);
            // The edges of the picture have no profile: a line there is neither for nor against.
            if (i < 6 || i >= f.Length - 6) continue;
            double best = 0;
            for (int d = -1; d <= 1; d++) if (i + d >= 0 && i + d < f.Length) best = Math.Max(best, f[i + d]);
            teeth.Add(Math.Min(best, cap));
            if (best > showing) shown++;
        }
        if (teeth.Count < 3) return (0, 0);
        teeth.Sort();
        int keep = Math.Max(2, teeth.Count * 3 / 4);
        double sum = 0;
        for (int i = 0; i < keep; i++) sum += teeth[i];
        return (sum / keep, (double)shown / teeth.Count);
    }

    /// <summary>Where to send what the finder tries (for tuning); null for nowhere.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>
    /// How well a whole-pixel spacing fits, without the small error in it adding up along the profile:
    /// the profile is taken a few lines at a time, each stretch finding its own start (lines within two
    /// pixels count), and all the stretches' lines are averaged the way <see cref="Comb"/> does.
    /// </summary>
    static double Coarse(double[] f, int p, double cap)
    {
        int n = f.Length;
        const int teethPerStretch = 4;
        int stretch = teethPerStretch * p;
        var teeth = new List<double>();
        var best = new double[teethPerStretch];
        var vals = new double[teethPerStretch];
        for (int start = 0; start < n - p; start += stretch)
        {
            double bestSum = -1;
            for (int ph = 0; ph < p; ph++)
            {
                double sum = 0;
                int count = 0;
                for (int k = 0; k < teethPerStretch; k++)
                {
                    int x = start + ph + k * p;
                    if (x >= n) { vals[k] = double.NaN; break; }
                    double v = 0;
                    for (int d = -2; d <= 2; d++) if (x + d >= 0 && x + d < n) v = Math.Max(v, f[x + d]);
                    vals[k] = Math.Min(v, cap);
                    sum += vals[k];
                    count++;
                }
                if (count > 0 && sum > bestSum) { bestSum = sum; Array.Copy(vals, best, teethPerStretch); for (int k = count; k < teethPerStretch; k++) best[k] = double.NaN; }
            }
            if (bestSum < 0) continue;
            foreach (var v in best) if (!double.IsNaN(v)) teeth.Add(v);
        }
        if (teeth.Count < 3) return 0;
        teeth.Sort();
        int keep = Math.Max(2, teeth.Count * 3 / 4);
        double total = 0;
        for (int i = 0; i < keep; i++) total += teeth[i];
        return total / keep;
    }

    /// <summary>
    /// The spacing that repeats in a profile: every whole spacing from 8 pixels to <paramref name="most"/>
    /// is tried (<see cref="Coarse"/>), the likeliest few are pinned down to a fiftieth of a pixel, and
    /// the smallest spacing at least four fifths as strong as the strongest wins (twice the spacing lines
    /// up just as well, half of it only half as well). Null when the best comb is hardly stronger than
    /// the texture, or too few of its lines show.
    /// </summary>
    internal static Fit? Period(double[] f, double most)
    {
        int n = f.Length;
        double level = f.Average();
        if (level <= 1e-9) return null;
        int hi = (int)Math.Min(most, n / 3.0);
        if (hi < 8) return null;
        double cap = level * 6;
        var score = new double[hi + 2];
        for (int p = 8; p <= hi; p++) score[p] = Coarse(f, p, cap);
        double top = 0;
        for (int p = 8; p <= hi; p++) top = Math.Max(top, score[p]);
        if (Log is { } log) log($"level {level:0.000} top {top / level:0.0}: " + string.Join(" ", Enumerable.Range(8, hi - 7).Where(p => score[p] >= 0.4 * top).Select(p => $"{p}={score[p] / level:0.0}")));
        if (top < level * 2.5) return null;
        // A grid's spacing stands out from the rest; grain and a few outlines score much the same at every spacing.
        var sorted = score.Skip(8).Take(hi - 7).OrderBy(x => x).ToArray();
        if (top < 1.5 * sorted[sorted.Length / 2]) return null;
        // The likeliest spacings, each a peak: made exact, with the comb over the whole profile.
        var fits = new List<(double P, double Ph, double S)>();
        var peaks = Enumerable.Range(8, hi - 7).Where(p => score[p] >= 0.6 * top && score[p] >= score[p - 1] && score[p] >= score[p + 1]).Take(8);
        foreach (int p in peaks)
        {
            double bestP = p, bestPh = 0, bestS = -1;
            for (double q = Math.Max(6, p - 2); q <= p + 2 + 1e-9; q += 0.05)
                for (double ph = 0; ph < q; ph += 1)
                {
                    var (mean, _) = Comb(f, q, ph, 0, cap);
                    if (mean > bestS) { bestS = mean; bestP = q; bestPh = ph; }
                }
            // Then to a hundredth of a pixel.
            double p0 = bestP, ph0 = bestPh;
            for (double q = p0 - 0.05; q <= p0 + 0.05 + 1e-9; q += 0.01)
                for (double ph = ph0 - 1; ph <= ph0 + 1; ph += 0.25)
                {
                    var (mean, _) = Comb(f, q, ((ph % q) + q) % q, 0, cap);
                    if (mean > bestS) { bestS = mean; bestP = q; bestPh = ((ph % q) + q) % q; }
                }
            fits.Add((bestP, bestPh, bestS));
        }
        if (fits.Count == 0) return null;
        double best2 = fits.Max(t => t.S);
        if (Log is { } log2) log2("exact: " + string.Join(" ", fits.Select(t => $"{t.P:0.00}={t.S / level:0.0}")));
        var pick = fits.Where(t => t.S >= 0.8 * best2).OrderBy(t => t.P).First();
        // Lines show at their own start and not between: grain and a few outlines score nearly as well from any start.
        var others = new List<double>();
        for (int k = 1; k < 8; k++) others.Add(Comb(f, pick.P, ((pick.Ph + pick.P * k / 8) % pick.P + pick.P) % pick.P, 0, cap).Mean);
        others.Sort();
        if (pick.S < 2 * others[others.Count / 2]) return null;
        var fit = Lines(f, pick.P, pick.Ph);
        if (fit is null) return null;
        var (mean2, share) = Comb(f, fit.Value.Period, fit.Value.Phase, level * 1.5, cap);
        // Most of its lines must show, and at least five: a few outlined buildings spaced alike aren't a grid.
        if (share < 0.5 || share * (n - 12) / fit.Value.Period < 5) return null;
        Log?.Invoke($"fit {fit.Value.Period:0.00} from {fit.Value.Phase:0.0}: strength {mean2 / level:0.0} share {share:0.00} others {others[others.Count / 2] / level:0.0}");
        // Lines well above the profile's run (a grid's are several times it; grain and odd outlines reach twice).
        if (mean2 < 3.5 * level) return null;
        return new Fit(fit.Value.Period, fit.Value.Phase, mean2 / level, share);
    }

    /// <summary>
    /// The lines near a comb's teeth fitted with a straight line (position = start + k × spacing): the
    /// spacing to a hundredth of a pixel, and where the first line is. Only the teeth where a line shows
    /// count. Null when fewer than three do.
    /// </summary>
    static (double Period, double Phase)? Lines(double[] f, double p, double phase)
    {
        var ks = new List<double>();
        var xs = new List<double>();
        double level = f.Average();
        int k = 0;
        for (double x = phase; x < f.Length - 1; x += p, k++)
        {
            int i = (int)Math.Round(x), at = -1;
            double best = 0;
            for (int d = -2; d <= 2; d++)
                if (i + d >= 0 && i + d < f.Length && f[i + d] > best) { best = f[i + d]; at = i + d; }
            if (at < 0 || best < level * 1.5) continue;
            // The line's middle to a fraction of a pixel: the weighted mean of it and its two neighbours.
            double sw = 0, sx = 0;
            for (int d = -1; d <= 1; d++)
                if (at + d >= 0 && at + d < f.Length) { sw += f[at + d]; sx += f[at + d] * (at + d); }
            ks.Add(k);
            xs.Add(sw > 0 ? sx / sw : at);
        }
        if (ks.Count < 3) return null;
        double mk = ks.Average(), mx = xs.Average(), sxy = 0, sxx = 0;
        for (int i = 0; i < ks.Count; i++) { sxy += (ks[i] - mk) * (xs[i] - mx); sxx += (ks[i] - mk) * (ks[i] - mk); }
        if (sxx <= 0) return null;
        double slope = sxy / sxx, start = mx - slope * mk;
        if (slope < 4) return null;
        return (slope, ((start % slope) + slope) % slope);
    }

    /// <summary>A direction's lines fitted again at a spacing known from the other direction (or both): whichever of its dark or light lines fits better.</summary>
    static Fit? Refit(double[] dark, double[] light, double p)
    {
        Fit? best = null;
        foreach (var f in new[] { dark, light })
        {
            double level = f.Average();
            if (level <= 1e-9) continue;
            double cap = level * 6, bestMean = 0, bestPh = 0;
            for (double ph = 0; ph < p; ph += 0.25)
            {
                var (mean, _) = Comb(f, p, ph, 0, cap);
                if (mean > bestMean) { bestMean = mean; bestPh = ph; }
            }
            if (Lines(f, p, bestPh) is not { } fit) continue;
            var (mean2, share) = Comb(f, fit.Period, fit.Phase, level * 1.5, level * 6);
            var r = new Fit(fit.Period, fit.Phase, mean2 / level, share);
            if (best is null || r.Strength * r.Share > best.Value.Strength * best.Value.Share) best = r;
        }
        return best;
    }

    /// <summary>Where the first line is at a settled spacing: the start that lines up best, near the one fitted.</summary>
    static double Phase(double[] dark, double[] light, double p, double near)
    {
        // The comb reads whole pixels, so a run of starts fits equally well: the middle of the best run.
        double best = -1, from = near, to = near;
        foreach (var f in new[] { dark, light })
        {
            double cap = f.Average() * 6;
            int last = -2;
            for (int k = -40; k <= 40; k++)
            {
                double ph = near + k * 0.1;
                var (mean, _) = Comb(f, p, ((ph % p) + p) % p, 0, cap);
                if (mean > best + 1e-9) { best = mean; from = to = ph; last = k; }
                else if (mean >= best - 1e-9 && last == k - 1) { to = ph; last = k; }
            }
        }
        double at = (from + to) / 2;
        return ((at % p) + p) % p;
    }
}
