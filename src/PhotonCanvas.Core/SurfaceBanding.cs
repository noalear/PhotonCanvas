namespace PhotonCanvas.Core;

/// <summary>
/// Marking on a curved surface the way xTool Studio does it: the head doesn't follow the surface
/// continuously, it steps through height bands (0.2 mm apart by default), highest first, and in
/// each band marks just the parts of the design whose surface lies in it. The head height for a
/// surface height h is h + 6 (the focus's own rule) less the layer's focus offset.
/// </summary>
public static class SurfaceBanding
{
    /// <summary>Machine Z of the head when focused on material this high (mm above the bed).</summary>
    public static double HeadZFor(double heightMm) => heightMm + Job.MinHeadZ;

    /// <summary>Paths are cut into pieces at most this long, so a piece doesn't span bands.</summary>
    const double SampleMm = 0.5;

    /// <summary>
    /// The blocks split into bands. Lines of one layer (same settings) share one block. With
    /// <paramref name="speedShare"/> (surfaces that slope away), dots are held on longer where it's below 1:
    /// each band's dots are split again by how long they're held.
    /// </summary>
    public static List<Block> Apply(IReadOnlyList<Block> blocks, Func<Vec2, double> height, double bandMm, Func<Vec2, double>? speedShare = null)
    {
        bandMm = Math.Clamp(bandMm, 0.02, 5);
        var o = new List<Block>();
        var pos = new Vec2(0, 0);
        // A pause after a block (cooling between paths) stays after the last of the pieces it's banded into.
        void Add(IEnumerable<Block> made, double pause)
        {
            int from = o.Count;
            o.AddRange(made.Select(x => x with { PauseAfterS = 0 }));
            if (pause > 0 && o.Count > from) o[^1] = o[^1] with { PauseAfterS = pause };
        }
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            double Z(Vec2 p) => HeadZFor(height(p)) - b.FocusOffsetMm;
            switch (b)
            {
                case VectorBlock:
                {
                    // Consecutive line blocks with the same settings: one banded block.
                    var paths = new List<IReadOnlyList<Vec2>>();
                    int j = i;
                    while (j < blocks.Count && blocks[j] is VectorBlock v && v.Laser == b.Laser && Math.Abs(v.FocusOffsetMm - b.FocusOffsetMm) < 1e-9)
                    {
                        paths.Add(v.Path.EmittedPoints());
                        j++;
                        if (v.PauseAfterS > 0) break; // the pause comes after this path, so the run ends here
                    }
                    i = j - 1;
                    Add([new BandedVectorBlock(BandPaths(paths, Z, bandMm, ref pos), b.Laser) { FocusOffsetMm = b.FocusOffsetMm }], blocks[i].PauseAfterS);
                    break;
                }
                case LoopsBlock l:
                    Add([new BandedVectorBlock(BandPaths([.. l.Loops.Where(x => x.Count >= 2).Select(x => l.Closed ? (IReadOnlyList<Vec2>)[.. x, x[0]] : x)], Z, bandMm, ref pos), l.Laser) { FocusOffsetMm = l.FocusOffsetMm }], l.PauseAfterS);
                    break;
                case FillBlock f:
                    Add([BandFill(f, Z, bandMm)], f.PauseAfterS);
                    break;
                case PointsBlock p:
                    Add(Bands(p.Points, Z, bandMm).SelectMany(g => g.Items.GroupBy(x => DotUsAt(p.DotUs, x, speedShare)).Select(h => (Block)(p with { Points = [.. h], HeadZ = g.HeadZ, DotUs = h.Key }))), p.PauseAfterS);
                    break;
                case DotBlock d:
                    Add(BandDots(d, Z, bandMm, speedShare), d.PauseAfterS);
                    break;
                default:
                    o.Add(b);
                    break;
            }
        }
        return o;
    }

    /// <summary>Band levels: the highest head height, then steps of <paramref name="band"/> down from it.</summary>
    static (double Top, Func<double, int> Level) Levels(IEnumerable<double> zs, double band)
    {
        double top = zs.DefaultIfEmpty(Job.MinHeadZ).Max();
        return (top, z => (int)Math.Round((top - z) / band));
    }

    static List<(double HeadZ, List<T> Items)> Bands<T>(IReadOnlyList<T> items, Func<T, double> z, double band) where T : notnull
    {
        var (top, level) = Levels(items.Select(z), band);
        return [.. items.GroupBy(x => level(z(x))).OrderBy(g => g.Key).Select(g => (top - g.Key * band, g.ToList()))];
    }

    static List<(double HeadZ, List<Vec2> Items)> Bands(IReadOnlyList<Vec2> pts, Func<Vec2, double> z, double band) => Bands<Vec2>(pts, z, band);

    /// <summary>Paths resampled, cut where the band changes (the cut point belongs to both pieces), grouped by band, highest first, nearest piece next.</summary>
    static List<BandPiece> BandPaths(List<IReadOnlyList<Vec2>> paths, Func<Vec2, double> z, double band, ref Vec2 pos)
    {
        var sampled = paths.Where(p => p.Count >= 2).Select(Resample).ToList();
        var (top, level) = Levels(sampled.SelectMany(p => p).Select(z), band);
        var byLevel = new SortedDictionary<int, List<List<Vec2>>>();
        foreach (var path in sampled)
        {
            var piece = new List<Vec2> { path[0] };
            int k = level(z(path[0]));
            for (int i = 1; i < path.Count; i++)
            {
                int ki = level(z(path[i]));
                piece.Add(path[i]);
                if (ki == k) continue;
                Add(k, piece);
                piece = [path[i]];
                k = ki;
            }
            Add(k, piece);
        }
        var o = new List<BandPiece>();
        foreach (var (k, pieces) in byLevel)
        {
            double headZ = top - k * band;
            var left = pieces.Where(p => p.Count >= 2).ToList();
            while (left.Count > 0)
            {
                // Nearest next piece, either way round.
                int best = 0;
                bool rev = false;
                double bd = double.PositiveInfinity;
                for (int i = 0; i < left.Count; i++)
                {
                    double a = left[i][0].Dist(pos), e = left[i][^1].Dist(pos);
                    if (a < bd) { bd = a; best = i; rev = false; }
                    if (e < bd) { bd = e; best = i; rev = true; }
                }
                var pick = left[best];
                left.RemoveAt(best);
                if (rev) pick.Reverse();
                o.Add(new BandPiece(headZ, pick));
                pos = pick[^1];
            }
        }
        return o;

        void Add(int k, List<Vec2> piece)
        {
            if (!byLevel.TryGetValue(k, out var list)) byLevel[k] = list = [];
            list.Add(piece);
        }
    }

    /// <summary>A path with extra points so no step is longer than <see cref="SampleMm"/>.</summary>
    static List<Vec2> Resample(IReadOnlyList<Vec2> pts)
    {
        var o = new List<Vec2> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            int n = Math.Max(1, (int)Math.Ceiling(a.Dist(b) / SampleMm));
            for (int k = 1; k <= n; k++) o.Add(Vec2.Lerp(a, b, (double)k / n));
        }
        return o;
    }

    /// <summary>The fill's scan lines cut where the band changes; each band keeps the lines' own order.</summary>
    static BandedFillBlock BandFill(FillBlock f, Func<Vec2, double> z, double band)
    {
        var segs = Job.FillLines(f);
        // The pieces of every line first, with their band.
        var raw = new List<(Job.FillSegment Seg, List<(double T, double Z)> Samples)>();
        foreach (var s in segs)
        {
            int n = Math.Max(1, (int)Math.Ceiling(s.Start.Dist(s.End) / SampleMm));
            raw.Add((s, [.. Enumerable.Range(0, n + 1).Select(k => ((double)k / n, z(Vec2.Lerp(s.Start, s.End, (double)k / n))))]));
        }
        var (top, level) = Levels(raw.SelectMany(r => r.Samples).Select(x => x.Z), band);
        // Each line cut half-way between samples in different bands.
        var pieces = new List<(int Level, Job.FillSegment Seg)>();
        foreach (var (s, samples) in raw)
        {
            double t0 = 0;
            int k = level(samples[0].Z);
            for (int i = 1; i < samples.Count; i++)
            {
                int ki = level(samples[i].Z);
                if (ki == k) continue;
                double cut = (samples[i - 1].T + samples[i].T) / 2;
                pieces.Add((k, Part(s, t0, cut)));
                t0 = cut;
                k = ki;
            }
            pieces.Add((k, Part(s, t0, 1)));
        }
        var bands = pieces.Where(p => p.Seg.Start.Dist(p.Seg.End) > 1e-4).GroupBy(p => p.Level).OrderBy(g => g.Key)
            .Select(g => (top - g.Key * band, (IReadOnlyList<Job.FillSegment>)[.. g.Select(p => p.Seg)])).ToList();
        return new BandedFillBlock(f, bands) { FocusOffsetMm = f.FocusOffsetMm };

        static Job.FillSegment Part(Job.FillSegment s, double t0, double t1) =>
            new(s.Y, s.X0 + (s.X1 - s.X0) * t0, s.X0 + (s.X1 - s.X0) * t1, Vec2.Lerp(s.Start, s.End, t0), Vec2.Lerp(s.Start, s.End, t1));
    }

    /// <summary>
    /// How long a dot is held on at <paramref name="at"/>: longer where the surface slopes away (the share of
    /// speed there, see <see cref="Job.SpeedShare"/>), in whole 10 µs, at most 100 ms.
    /// </summary>
    public static int DotUsAt(int dotUs, Vec2 at, Func<Vec2, double>? speedShare)
    {
        if (speedShare is null) return dotUs;
        double k = speedShare(at);
        if (!double.IsFinite(k) || k >= 1) return dotUs;
        double held = dotUs / Math.Max(WrapZone.SlowestShare, k);
        return (int)Math.Clamp(Math.Round(held / 10, MidpointRounding.AwayFromZero) * 10, 10, 100_000);
    }

    /// <summary>A picture split into one picture per band (each pixel where its surface is), each with its head height.</summary>
    static IEnumerable<DotBlock> BandDots(DotBlock d, Func<Vec2, double> z, double band, Func<Vec2, double>? speedShare)
    {
        int w = d.Bitmap.Width, h = d.Bitmap.Height;
        var zs = new double[w * h];
        var us = new int[w * h];
        var on = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!d.Bitmap.Bits[i]) continue;
                var at = d.At(x, y);
                zs[i] = z(at);
                us[i] = DotUsAt(d.DotUs, at, speedShare);
                on.Add(i);
            }
        var (top, level) = Levels(on.Select(i => zs[i]), band);
        foreach (var g in on.GroupBy(i => (Level: level(zs[i]), Us: us[i])).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Us))
        {
            var bmp = new Bitmap(w, h);
            foreach (var i in g) bmp.Bits[i] = true;
            yield return d with { Bitmap = bmp, HeadZ = top - g.Key.Level * band, DotUs = g.Key.Us, PassBlocks = null };
        }
    }
}
