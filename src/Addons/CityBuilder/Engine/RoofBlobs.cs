namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Finds the blocks of a map that aren't the ground's colour: a roof drawn with heavy texture (rows of
/// shingles, bricks, a wash of rain) doesn't come out as one colour patch, because the strokes part it
/// into hundreds of specks, but seen with the texture smoothed away it's still one block of a colour that
/// isn't the ground's. The ground is the map's biggest patch and every big patch of the same hue (water
/// and cobbles on a blue night map, grass and more grass by day); each cell is as far from the ground as
/// its smoothed colour is, brightness taken out (grey against blue, tan against green, but not a shadow
/// on the grass); and the cells far from it, joined up, are the blobs. The roof finder then shapes each
/// like a patch: the rectangles inside it, slid to its drawn lines, checked round their sides.
/// </summary>
public static class RoofBlobs
{
    /// <summary>How far from the ground's colour a cell must be to be a blob's (brightness taken out).</summary>
    public const double Far = 0.14;
    /// <summary>How much further than that a cell must be to start a piece of its own (a peak, not a bump on a slope).</summary>
    public const double Peak = 0.06;
    /// <summary>How deep the dip between two peaks must be for them to be two pieces (shallower is one roof's texture).</summary>
    public const double Depth = 0.1;
    /// <summary>How near a cell's colour must be to a piece's (brightness taken out) to join it.</summary>
    public const double Alike = 0.15;

    /// <summary>Where to send the pieces and the blobs they're joined into (for tuning); null for nowhere.</summary>
    public static Action<List<List<int>>, List<List<int>>>? Debug { get; set; }
    /// <summary>Where to send each cell's distance from the ground's colour, whether it's far, and whether it's cut (for tuning); null for nowhere.</summary>
    public static Action<float[], bool[], bool[]>? Trace { get; set; }

    /// <summary>The blobs as patches of their own (ids from <paramref name="firstId"/>), each at least <paramref name="least"/> cells.</summary>
    public static List<MapVision.Patch> Find(MapVision v, double least, double most, int firstId, CancellationToken ct = default)
    {
        int w = v.Width, h = v.Height, n = w * h;
        double sq = v.Square;
        var ground = GroundColours(v);
        if (ground.Count == 0) return [];
        // The colour with the texture smoothed away: about an eighth of a square round each cell, lines left out.
        var r = (float[])v.R.Clone();
        var g = (float[])v.G.Clone();
        var b = (float[])v.B.Clone();
        int radius = Math.Max(1, (int)Math.Round(sq / 12));
        Smooth(v, r, radius);
        Smooth(v, g, radius);
        Smooth(v, b, radius);
        ct.ThrowIfCancellationRequested();
        // A long drawn line is an outline (two buildings side by side, a building against a quay) unless it's one of
        // many: rows of shingles drawn heavy are lines too, but with the next row close on both sides.
        var cut = new bool[n];
        for (int i = 0; i < n; i++) cut[i] = v.LongLine[i] && Isolated(v, i);
        var dist = new float[n];
        for (int i = 0; i < n; i++)
        {
            var (r0, g0, b0) = Normal(r[i], g[i], b[i]);
            double best = 9;
            foreach (var (r1, g1, b1) in ground)
                best = Math.Min(best, Math.Sqrt((r0 - r1) * (r0 - r1) + (g0 - g1) * (g0 - g1) + (b0 - b1) * (b0 - b1)));
            dist[i] = (float)best;
        }
        // Smoothed over about an eighth of a square, so a roof's texture is one hump, not a hundred; the ground
        // showing between two buildings is still a dip between two humps.
        Smooth(v, dist, Math.Max(1, (int)Math.Round(sq / 8)), true);
        var far = new bool[n];
        for (int i = 0; i < n; i++) far[i] = dist[i] > Far && !cut[i];
        Trace?.Invoke(dist, far, cut);
        ct.ThrowIfCancellationRequested();
        // The pieces: flooded from the cells furthest from the ground's colour, the furthest first, so two
        // buildings side by side with a dip between them (the ground showing through the outline's glow)
        // come out as two, each from its own peak. A new peak only well above the floor; and where two
        // floods meet with hardly a dip between them (a ripple in one roof's texture) they're one piece.
        var pieceOf = new int[n];
        Array.Fill(pieceOf, -1);
        var pieces = new List<List<int>>();
        var peak = new List<float>();
        var parent = new List<int>();
        var sums = new List<(double R, double G, double B, int N)>();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        bool AlikePiece(int rt, int c)
        {
            var (sr, sg, sb, k) = sums[rt];
            if (k == 0) return true;
            var (r0, g0, b0) = Normal(sr / k, sg / k, sb / k);
            var (r1, g1, b1) = Normal(r[c], g[c], b[c]);
            return Math.Sqrt((r0 - r1) * (r0 - r1) + (g0 - g1) * (g0 - g1) + (b0 - b1) * (b0 - b1)) < Alike;
        }
        var order = Enumerable.Range(0, n).Where(i => far[i]).OrderByDescending(i => dist[i]).ToArray();
        var waiting = new List<int>();
        var roots = new List<int>(4);
        foreach (int c in order)
        {
            roots.Clear();
            // A cell joins a piece it's next to of its own colour (a red roof against a tan one is two); a line cell joins any.
            foreach (int q in Next(v, c))
                if (q >= 0 && pieceOf[q] >= 0)
                {
                    int rq = Root(pieceOf[q]);
                    if (!roots.Contains(rq) && (v.Line[c] || AlikePiece(rq, c))) roots.Add(rq);
                }
            int best;
            if (roots.Count == 0)
            {
                if (dist[c] < Far + Peak || v.Line[c]) { waiting.Add(c); continue; }
                best = pieces.Count;
                pieces.Add([]);
                peak.Add(dist[c]);
                parent.Add(best);
                sums.Add((0, 0, 0, 0));
            }
            else
            {
                best = roots[0];
                foreach (int rt in roots) if (peak[rt] > peak[best]) best = rt;
                // The lower of two peaks is no peak of its own when the dip between them is shallow.
                foreach (int rt in roots)
                    if (rt != best && peak[rt] - dist[c] < Depth)
                    {
                        parent[rt] = best;
                        var (ar, ag, ab, an) = sums[best];
                        var (br, bg, bb, bn) = sums[rt];
                        sums[best] = (ar + br, ag + bg, ab + bb, an + bn);
                    }
            }
            pieceOf[c] = best;
            pieces[best].Add(c);
            if (!v.Line[c]) { var (sr, sg, sb, k) = sums[best]; sums[best] = (sr + r[c], sg + g[c], sb + b[c], k + 1); }
        }
        for (int i = 0; i < pieces.Count; i++)
        {
            int rt = Root(i);
            if (rt == i) continue;
            pieces[rt].AddRange(pieces[i]);
            pieces[i] = [];
        }
        for (int i = 0; i < n; i++) if (pieceOf[i] >= 0) pieceOf[i] = Root(pieceOf[i]);
        // The cells no peak's flood reached in order: joined to whatever piece they touch, out from it.
        var todo = new Queue<int>();
        foreach (int c in waiting)
            if (pieceOf[c] < 0 && Next(v, c).Any(q => q >= 0 && pieceOf[q] >= 0)) todo.Enqueue(c);
        while (todo.Count > 0)
        {
            int c = todo.Dequeue();
            if (pieceOf[c] >= 0) continue;
            int best = -1;
            float bestDist = -1;
            foreach (int q in Next(v, c))
                if (q >= 0 && pieceOf[q] >= 0 && dist[q] > bestDist) { bestDist = dist[q]; best = pieceOf[q]; }
            if (best < 0) continue;
            pieceOf[c] = best;
            pieces[best].Add(c);
            foreach (int q in Next(v, c)) if (q >= 0 && far[q] && pieceOf[q] < 0) todo.Enqueue(q);
        }
        ct.ThrowIfCancellationRequested();
        var blobs = Rejoin(v, pieces, pieceOf, far, ct);
        Debug?.Invoke(pieces, blobs);
        var res = new List<MapVision.Patch>();
        foreach (var cells in blobs)
        {
            if (cells.Count < least || cells.Count > most) continue;
            var p = new MapVision.Patch { Id = firstId + res.Count, Cells = cells };
            double sr = 0, sg = 0, sb = 0, st = 0;
            foreach (int c in cells)
            {
                sr += v.R[c]; sg += v.G[c]; sb += v.B[c]; st += v.Tex[c];
                int x = c % w, y = c / w;
                p.X0 = Math.Min(p.X0, x); p.X1 = Math.Max(p.X1, x);
                p.Y0 = Math.Min(p.Y0, y); p.Y1 = Math.Max(p.Y1, y);
            }
            p.R = sr / cells.Count; p.G = sg / cells.Count; p.B = sb / cells.Count; p.Tex = st / cells.Count;
            // A pool of a deeper blue than the ground isn't a roof either.
            if (RoofFinder.Ground(p)) continue;
            res.Add(p);
        }
        return res;
    }

    /// <summary>
    /// Whether a line cell is a line on its own: across it (one way or the other), the run of line cells
    /// it's in is no more than four thick, and the six cells beyond that run on one side at least hold
    /// no more than one line cell. The rows of a shingled roof have the next row close on both sides.
    /// </summary>
    static bool Isolated(MapVision v, int c)
    {
        int w = v.Width, h = v.Height, x = c % w, y = c / w;
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
        {
            int thick = 1;
            bool clear = false;
            foreach (int sign in new[] { -1, 1 })
            {
                int k = 1;
                while (k <= 4)
                {
                    int px = x + dx * k * sign, py = y + dy * k * sign;
                    if (px < 0 || py < 0 || px >= w || py >= h || !v.Line[v.Index(px, py)]) break;
                    k++;
                }
                thick += k - 1;
                int lines = 0;
                for (int j = k; j < k + 6; j++)
                {
                    int px = x + dx * j * sign, py = y + dy * j * sign;
                    if (px < 0 || py < 0 || px >= w || py >= h) break;
                    if (v.Line[v.Index(px, py)]) lines++;
                }
                if (lines <= 1) clear = true;
            }
            if (thick <= 4 && clear) return true;
        }
        return false;
    }

    static int[] Next(MapVision v, int c)
    {
        int w = v.Width, x = c % w, y = c / w;
        return [x > 0 ? c - 1 : -1, x < w - 1 ? c + 1 : -1, y > 0 ? c - w : -1, y < v.Height - 1 ? c + w : -1];
    }

    /// <summary>
    /// The pieces a long drawn line parted put back together where the line was a ridge or a hip, not an
    /// outline: two pieces that meet across a line along a good length, alike in colour, whose union is
    /// at least as rectangular as either, are one blob; two buildings side by side (the union an oblong
    /// no fuller than its parts, or the colours apart) stay two.
    /// </summary>
    static List<List<int>> Rejoin(MapVision v, List<List<int>> pieces, int[] pieceOf, bool[] far, CancellationToken ct)
    {
        int w = v.Width, h = v.Height;
        double tiny = 0.05 * v.Square * v.Square;
        // How long each pair's shared boundary is: from a piece's cell, across up to three cells of line, to another piece.
        var contact = new Dictionary<(int, int), int>();
        for (int a = 0; a < pieces.Count; a++)
        {
            if (pieces[a].Count < tiny) continue;
            if ((a & 255) == 0) ct.ThrowIfCancellationRequested();
            foreach (int c in pieces[a])
            {
                int x = c % w, y = c / w;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                    for (int step = 1; step <= 4; step++)
                    {
                        int fx = x + dx * step, fy = y + dy * step;
                        if (fx >= w || fy >= h) break;
                        int q = pieceOf[v.Index(fx, fy)];
                        if (q == a) break;
                        // Across drawn line only (a ridge, a hip): the ground showing between two pieces parts them for good.
                        if (q < 0) { if (!v.Line[v.Index(fx, fy)]) break; continue; }
                        if (pieces[q].Count >= tiny) { var key = (Math.Min(a, q), Math.Max(a, q)); contact[key] = contact.GetValueOrDefault(key) + 1; }
                        break;
                    }
            }
        }
        // Each piece's box and how full it is; then the pairs, longest contact first, joined where the union is as rectangular.
        var group = new int[pieces.Count];
        for (int i = 0; i < group.Length; i++) group[i] = i;
        int Root(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        var cellsOf = pieces.Select(p => new List<int>(p)).ToList();
        var fill = new double[pieces.Count];
        var side = new double[pieces.Count];
        var colour = new (double R, double G, double B)[pieces.Count];
        void Measure(int i)
        {
            var box = v.MinBox(cellsOf[i]);
            fill[i] = cellsOf[i].Count / Math.Max(1, box.Area);
            side[i] = Math.Min(box.W, box.H);
            double r = 0, g = 0, b = 0;
            foreach (int c in cellsOf[i]) { r += v.R[c]; g += v.G[c]; b += v.B[c]; }
            colour[i] = Normal(r / cellsOf[i].Count, g / cellsOf[i].Count, b / cellsOf[i].Count);
        }
        for (int i = 0; i < pieces.Count; i++) if (pieces[i].Count >= tiny) Measure(i);
        foreach (var ((a0, b0), length) in contact.OrderByDescending(k => k.Value))
        {
            int a = Root(a0), b = Root(b0);
            if (a == b) continue;
            // Along at least a third of the shorter side of the smaller one.
            if (length < 0.33 * Math.Min(side[a], side[b])) continue;
            var (ra, ga, ba) = colour[a];
            var (rb, gb, bb) = colour[b];
            if (Math.Sqrt((ra - rb) * (ra - rb) + (ga - gb) * (ga - gb) + (ba - bb) * (ba - bb)) > 0.11) continue;
            var union = new List<int>(cellsOf[a]);
            union.AddRange(cellsOf[b]);
            var box = v.MinBox(union);
            double unionFill = union.Count / Math.Max(1, box.Area);
            if (unionFill < Math.Min(fill[a], fill[b]) - 0.03) continue;
            group[b] = a;
            cellsOf[a] = union;
            cellsOf[b] = [];
            Measure(a);
        }
        var res = new List<List<int>>();
        for (int i = 0; i < pieces.Count; i++) if (Root(i) == i && cellsOf[i].Count > 0) res.Add(cellsOf[i]);
        return res;
    }

    /// <summary>
    /// The ground's colours, brightness taken out: the biggest patch's (when it's at least three squares),
    /// and every other patch of two squares or more that's near it in colour.
    /// </summary>
    public static List<(double R, double G, double B)> GroundColours(MapVision v)
    {
        double sqArea = v.Square * v.Square;
        var big = v.Patches.OrderByDescending(p => p.Area).ToList();
        var res = new List<(double, double, double)>();
        if (big.Count == 0 || big[0].Area < 3 * sqArea) return res;
        var (r0, g0, b0) = Normal(big[0].R, big[0].G, big[0].B);
        foreach (var p in big)
        {
            if (p.Area < 2 * sqArea) break;
            var (r1, g1, b1) = Normal(p.R, p.G, p.B);
            if (Math.Sqrt((r0 - r1) * (r0 - r1) + (g0 - g1) * (g0 - g1) + (b0 - b1) * (b0 - b1)) < 0.15) res.Add((r1, g1, b1));
        }
        return res;
    }

    static (double R, double G, double B) Normal(double r, double g, double b)
    {
        double m = Math.Max(0.08, (r + g + b) / 3);
        return (r / m, g / m, b / m);
    }

    /// <summary>A box blur of a channel at the working size, line cells left out (or counted, with <paramref name="linesToo"/>).</summary>
    static void Smooth(MapVision v, float[] ch, int radius, bool linesToo = false)
    {
        int w = v.Width, h = v.Height;
        var sum = new double[(w + 1) * (h + 1)];
        var cnt = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = v.Index(x, y);
                bool ok = linesToo || !v.Line[i];
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + sum[(y + 1) * (w + 1) + x] - sum[y * (w + 1) + x] + (ok ? ch[i] : 0);
                cnt[(y + 1) * (w + 1) + x + 1] = cnt[y * (w + 1) + x + 1] + cnt[(y + 1) * (w + 1) + x] - cnt[y * (w + 1) + x] + (ok ? 1 : 0);
            }
        var res = new float[ch.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - radius), x1 = Math.Min(w, x + radius + 1), y0 = Math.Max(0, y - radius), y1 = Math.Min(h, y + radius + 1);
                double s = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
                int c = cnt[y1 * (w + 1) + x1] - cnt[y0 * (w + 1) + x1] - cnt[y1 * (w + 1) + x0] + cnt[y0 * (w + 1) + x0];
                int i = v.Index(x, y);
                res[i] = c == 0 ? ch[i] : (float)(s / c);
            }
        Array.Copy(res, ch, ch.Length);
    }
}
