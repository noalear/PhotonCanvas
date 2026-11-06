using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Looks at a battle map picture the way the eye does before anything is named: finds the drawn lines
/// (thin dark strokes, the roofs' outlines), takes the texture out of the colours, and splits the
/// picture into patches of one colour that don't cross a line. The patches are what the roof finder,
/// the tree finder and the ground reader work from.
/// </summary>
public sealed class MapVision
{
    /// <summary>A patch of one colour: its cells (at the working size), colour, and shape.</summary>
    public sealed class Patch
    {
        public int Id;
        public List<int> Cells = [];
        public double R, G, B;
        /// <summary>How uneven the colour is inside it (the standard deviation of brightness).</summary>
        public double Busy;
        /// <summary>How much of it is drawn strokes (0 none, 1 all).</summary>
        public double Tex;
        public int X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1;
        public double Area => Cells.Count;
        public (double H, double S, double V) Hsv => CityReader.Hsv(R, G, B);
        /// <summary>Of its boundary cells, the share next to a drawn line.</summary>
        public double Outlined;
        /// <summary>Its boundary cells (cells with a neighbour outside the patch).</summary>
        public List<int> Edge = [];
    }

    /// <summary>The smallest rectangle round a shape, turned: its middle (working pixels), its turn (degrees, 0 to 90) and its sides along and across that turn.</summary>
    public readonly record struct Box(double Cx, double Cy, double AngleDeg, double W, double H)
    {
        public double Area => W * H;
        /// <summary>The four corners, clockwise from the one at (−W/2, −H/2) in the box's own frame.</summary>
        public (double X, double Y)[] Corners()
        {
            double a = AngleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            var res = new (double, double)[4];
            int i = 0;
            foreach (var (u, v) in new[] { (-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2) })
                res[i++] = (Cx + u * c - v * s, Cy + u * s + v * c);
            return res;
        }
        /// <summary>A point in the box's own frame (u along, v across, from its middle).</summary>
        public (double U, double V) Local(double x, double y)
        {
            double a = -AngleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), dx = x - Cx, dy = y - Cy;
            return (dx * c - dy * s, dx * s + dy * c);
        }
        public bool Contains(double x, double y, double slack = 0)
        {
            var (u, v) = Local(x, y);
            return Math.Abs(u) <= W / 2 + slack && Math.Abs(v) <= H / 2 + slack;
        }
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Picture pixels per working cell.</summary>
    public double Scale { get; }
    /// <summary>The picture's size in pixels (the line maps are this wide and high).</summary>
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    /// <summary>One grid square at the working size.</summary>
    public double Square { get; }
    /// <summary>The grid's first lines at the working size.</summary>
    public double OffsetX { get; }
    public double OffsetY { get; }
    /// <summary>Each cell's colour with the drawn lines left out and the texture smoothed (0–1).</summary>
    public float[] R { get; }
    public float[] G { get; }
    public float[] B { get; }
    /// <summary>Each cell's brightness before smoothing.</summary>
    public float[] Lum { get; }
    /// <summary>How much of each cell (and the cells round it) is drawn strokes: cobbles and leaves are busy with them, a roof or a lawn isn't.</summary>
    public float[] Tex { get; }
    /// <summary>Cells that are mostly drawn line.</summary>
    public bool[] Line { get; }
    /// <summary>Line cells on a straight run of line at least a third of a square long (a ridge, a hip, an outline).</summary>
    public bool[] LongLine { get; private set; } = [];
    /// <summary>Which picture pixels lie on (within two pixels of) one of the grid's lines.</summary>
    public bool[] OnGrid { get; }
    /// <summary>How strongly the picture's thin dark lines show at each picture pixel (brightness below both sides).</summary>
    public float[] Lineness { get; }
    /// <summary>As <see cref="Lineness"/>, but dark lines only: the strokes drawn on roofs and cobbles, without the highlights.</summary>
    public float[] DarkLines { get; }
    /// <summary>The line strength that counts as a drawn outline (above the grid's own lines and the texture).</summary>
    public double LineLevel { get; }
    /// <summary>How strongly the grid's own lines show.</summary>
    public double GridLine { get; }
    /// <summary>The patch each cell is in (−1 for a line cell).</summary>
    public int[] PatchOf { get; }
    public List<Patch> Patches { get; } = [];

    public int Index(int x, int y) => y * Width + x;

    MapVision(RasterImage img, double squarePx, double ox, double oy, CancellationToken ct)
    {
        int w = img.Width, h = img.Height;
        double p = Math.Max(4, squarePx);
        // Work at about 24 cells to a square: fine enough for a roof's edge to a tenth of a square, coarse enough to be quick.
        Scale = Math.Max(1, p / 24);
        ImageWidth = w;
        ImageHeight = h;
        Width = Math.Max(1, (int)Math.Ceiling(w / Scale));
        Height = Math.Max(1, (int)Math.Ceiling(h / Scale));
        Square = p / Scale;
        OffsetX = ox / Scale;
        OffsetY = oy / Scale;
        var lum = img.Luminance();
        Lineness = ThinLines(lum, w, h, true);
        DarkLines = ThinLines(lum, w, h, false);
        ct.ThrowIfCancellationRequested();
        var (level, gridLine) = OutlineLevel(Lineness, w, h, p, ox, oy);
        LineLevel = level;
        GridLine = gridLine;
        // The grid's own lines aren't outlines: along them, a stroke is only what's stronger than the grid is.
        var onGrid = new bool[w * h];
        for (double x = ox; x < w; x += p)
            for (int d = -2; d <= 2; d++)
            {
                int i = (int)Math.Round(x) + d;
                if (i < 0 || i >= w) continue;
                for (int y = 0; y < h; y++) onGrid[y * w + i] = true;
            }
        for (double y = oy; y < h; y += p)
            for (int d = -2; d <= 2; d++)
            {
                int j = (int)Math.Round(y) + d;
                if (j < 0 || j >= h) continue;
                for (int x = 0; x < w; x++) onGrid[j * w + x] = true;
            }
        for (int i = 0; i < onGrid.Length; i++) if (onGrid[i]) Lineness[i] = Math.Max(0, Lineness[i] - (float)gridLine * 2f);
        OnGrid = onGrid;
        int n = Width * Height;
        R = new float[n];
        G = new float[n];
        B = new float[n];
        Lum = new float[n];
        Tex = new float[n];
        Line = new bool[n];
        var px = img.Rgba;
        // Each cell: the mean colour of its pixels that aren't on a line; a cell that's mostly line is a line.
        for (int cy = 0; cy < Height; cy++)
        {
            int y0 = (int)(cy * Scale), y1 = Math.Min(h, Math.Max(y0 + 1, (int)((cy + 1) * Scale)));
            for (int cx = 0; cx < Width; cx++)
            {
                int x0 = (int)(cx * Scale), x1 = Math.Min(w, Math.Max(x0 + 1, (int)((cx + 1) * Scale)));
                double r = 0, g = 0, b = 0, l = 0;
                int count = 0, lines = 0, all = 0, strokes = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * w + x;
                        all++;
                        if (Lineness[i] >= LineLevel * 0.5) strokes++;
                        if (Lineness[i] >= LineLevel) { lines++; continue; }
                        int k = i * 4;
                        r += px[k]; g += px[k + 1]; b += px[k + 2]; l += lum[i];
                        count++;
                    }
                int c = Index(cx, cy);
                Tex[c] = (float)strokes / all;
                if (count == 0 || lines * 2 >= all)
                {
                    Line[c] = true;
                    // Its colour is still wanted (for smoothing's sake): the mean of all of it.
                    r = g = b = l = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) { int k = (y * w + x) * 4; r += px[k]; g += px[k + 1]; b += px[k + 2]; l += lum[y * w + x]; }
                    count = all;
                }
                R[c] = (float)(r / (255.0 * count));
                G[c] = (float)(g / (255.0 * count));
                B[c] = (float)(b / (255.0 * count));
                Lum[c] = (float)(l / count);
            }
        }
        ct.ThrowIfCancellationRequested();
        // The texture smoothed away: each cell the mean of the cells round it that aren't lines (about a sixth of a square across).
        int radius = Math.Max(1, (int)Math.Round(Square / 12));
        Smooth(R, radius);
        Smooth(G, radius);
        Smooth(B, radius);
        // The strokes are counted over a wider patch (about a third of a square): one cobble is no texture.
        Smooth(Tex, radius * 2, true);
        ct.ThrowIfCancellationRequested();
        PatchOf = new int[n];
        Grow(ct);
    }

    /// <summary>Looks at a picture on a grid of <paramref name="squarePx"/> pixel squares.</summary>
    public static MapVision Of(RasterImage img, double squarePx, double offsetX, double offsetY, CancellationToken ct = default) => new(img, squarePx, offsetX, offsetY, ct);

    /// <summary>
    /// How much each pixel is a thin dark line: how far its brightness is below both its sides, two to
    /// six pixels away, across or down (a stroke one to eight pixels wide, blurred or not).
    /// </summary>
    static float[] ThinLines(float[] lum, int w, int h, bool lightToo)
    {
        var res = new float[w * h];
        int[] widths = [2, 3, 4, 6];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = lum[i], best = 0;
                foreach (int d in widths)
                {
                    // Darker than both sides (a drawn line), or lighter than both (a chalk line, a glow round a night map's roofs).
                    if (x - d >= 0 && x + d < w) best = Math.Max(best, Math.Min(lum[i - d], lum[i + d]) - v);
                    if (y - d >= 0 && y + d < h) best = Math.Max(best, Math.Min(lum[i - d * w], lum[i + d * w]) - v);
                    if (!lightToo) continue;
                    if (x - d >= 0 && x + d < w) best = Math.Max(best, v - Math.Max(lum[i - d], lum[i + d]));
                    if (y - d >= 0 && y + d < h) best = Math.Max(best, v - Math.Max(lum[i - d * w], lum[i + d * w]));
                }
                res[i] = best;
            }
        return res;
    }

    /// <summary>
    /// The line strength an outline must have: well above the grid's own lines (their typical strength,
    /// measured along them), and never faint.
    /// </summary>
    static (double Level, double GridLine) OutlineLevel(float[] lines, int w, int h, double p, double ox, double oy)
    {
        // The grid's typical strength along its lines: a thin line can sit a pixel or two off where the grid was
        // found, so each nearby column (row) is measured on its own and the strongest of them is the line.
        double gridLine = 0;
        for (int d = -2; d <= 2; d++)
        {
            var grid = new List<float>();
            for (double x = ox; x < w; x += p)
            {
                int i = (int)Math.Round(x) + d;
                if (i < 0 || i >= w) continue;
                for (int y = 0; y < h; y += 3) grid.Add(lines[y * w + i]);
            }
            for (double y = oy; y < h; y += p)
            {
                int j = (int)Math.Round(y) + d;
                if (j < 0 || j >= h) continue;
                for (int x = 0; x < w; x += 3) grid.Add(lines[j * w + x]);
            }
            if (grid.Count == 0) continue;
            grid.Sort();
            // The grid shows on most of its length: its typical strength, which strokes crossing it don't shift.
            gridLine = Math.Max(gridLine, grid[grid.Count / 2]);
        }
        // The strongest strokes (the outlines: the strength only one pixel in a hundred reaches) set the scale: a
        // faint, blurred map has faint outlines; a crisp one has cobbles and bricks nearly as dark as its outlines.
        var sample = new List<float>();
        for (int i = 0; i < lines.Length; i += 5) sample.Add(lines[i]);
        sample.Sort();
        double strongest = sample[(int)(sample.Count * 0.99)];
        return (Math.Clamp(Math.Max(strongest * 0.4, gridLine * 1.1), 0.05, 0.3), gridLine);
    }

    /// <summary>A box blur of a channel at the working size, line cells left out (and left as they are).</summary>
    void Smooth(float[] ch, int radius, bool linesToo = false)
    {
        int w = Width, h = Height;
        var sum = new double[(w + 1) * (h + 1)];
        var cnt = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = Index(x, y);
                bool ok = linesToo || !Line[i];
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + sum[(y + 1) * (w + 1) + x] - sum[y * (w + 1) + x] + (ok ? ch[i] : 0);
                cnt[(y + 1) * (w + 1) + x + 1] = cnt[y * (w + 1) + x + 1] + cnt[(y + 1) * (w + 1) + x] - cnt[y * (w + 1) + x] + (ok ? 1 : 0);
            }
        var res = new float[ch.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = Index(x, y);
                if (Line[i] && !linesToo) { res[i] = ch[i]; continue; }
                int x0 = Math.Max(0, x - radius), y0 = Math.Max(0, y - radius), x1 = Math.Min(w, x + radius + 1), y1 = Math.Min(h, y + radius + 1);
                double s = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
                int c = cnt[y1 * (w + 1) + x1] - cnt[y0 * (w + 1) + x1] - cnt[y1 * (w + 1) + x0] + cnt[y0 * (w + 1) + x0];
                res[i] = c == 0 ? ch[i] : (float)(s / c);
            }
        Array.Copy(res, ch, ch.Length);
    }

    /// <summary>How far apart two cells' colours are (0 the same, about 1.7 black to white).</summary>
    /// <summary>
    /// Whether two cells are of different stuff, not just the same stuff lit differently: their colours
    /// differ once brightness is taken out, or one is far brighter, or one is far busier with strokes.
    /// </summary>
    public bool Steps(int a, int b)
    {
        double ma = Math.Max(0.08, (R[a] + G[a] + B[a]) / 3), mb = Math.Max(0.08, (R[b] + G[b] + B[b]) / 3);
        double dr = R[a] / ma - R[b] / mb, dg = G[a] / ma - G[b] / mb, db = B[a] / ma - B[b] / mb;
        return Math.Sqrt(dr * dr + dg * dg + db * db) > 0.12 || Math.Abs(ma - mb) > 0.22 || Math.Abs(Tex[a] - Tex[b]) > 0.2;
    }

    public double Apart(int a, int b)
    {
        double dr = R[a] - R[b], dg = G[a] - G[b], db = B[a] - B[b], dt = (Tex[a] - Tex[b]) * 0.6;
        return Math.Sqrt(dr * dr + dg * dg + db * db + dt * dt);
    }

    /// <summary>
    /// The patches: grown from cell to cell where the colour changes little, never through a line cell,
    /// and never far from the patch's mean colour (so a slow drift across a lawn doesn't join it to the
    /// road).
    /// </summary>
    void Grow(CancellationToken ct)
    {
        int w = Width, h = Height, n = w * h;
        const double step = 0.055, drift = 0.16;
        Array.Fill(PatchOf, -1);
        var todo = new Stack<int>();
        // Start from the cells least like their neighbours' edges: in order, so the same picture always patches the same way.
        for (int start = 0; start < n; start++)
        {
            if (PatchOf[start] >= 0 || Line[start]) continue;
            if ((start & 4095) == 0) ct.ThrowIfCancellationRequested();
            var patch = new Patch { Id = Patches.Count };
            double sr = 0, sg = 0, sb = 0, st = 0;
            PatchOf[start] = patch.Id;
            todo.Push(start);
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                patch.Cells.Add(c);
                sr += R[c]; sg += G[c]; sb += B[c]; st += Tex[c];
                int k = patch.Cells.Count;
                double mr = sr / k, mg = sg / k, mb = sb / k, mt = st / k;
                int x = c % w, y = c / w;
                foreach (int q in Neighbours(x, y))
                {
                    if (PatchOf[q] >= 0 || Line[q]) continue;
                    if (Apart(c, q) > step) continue;
                    double dr = R[q] - mr, dg = G[q] - mg, db = B[q] - mb, dt = (Tex[q] - mt) * 0.6;
                    if (Math.Sqrt(dr * dr + dg * dg + db * db + dt * dt) > drift) continue;
                    PatchOf[q] = patch.Id;
                    todo.Push(q);
                }
            }
            Patches.Add(patch);
        }
        foreach (var patch in Patches) Measure(patch);
        LongLine = LongLines();
    }

    /// <summary>
    /// Line cells that are part of a straight run of line cells (along a row, a column or a diagonal, with
    /// single-cell gaps allowed) at least a third of a square long: the ridge, a hip, an outline; not a
    /// shingle's stroke or a speck.
    /// </summary>
    bool[] LongLines()
    {
        int w = Width, h = Height, n = w * h;
        var res = new bool[n];
        int least = Math.Max(4, (int)Math.Round(Square / 3));
        (int dx, int dy)[] dirs = [(1, 0), (0, 1), (1, 1), (1, -1)];
        var run = new List<int>();
        foreach (var (dx, dy) in dirs)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // Only from the start of a run (the cell before, and the one before that, not line).
                    int bx = x - dx, by = y - dy, bx2 = x - 2 * dx, by2 = y - 2 * dy;
                    if (!Line[Index(x, y)]) continue;
                    if (bx >= 0 && by >= 0 && bx < w && by < h && Line[Index(bx, by)]) continue;
                    if (bx2 >= 0 && by2 >= 0 && bx2 < w && by2 < h && Line[Index(bx2, by2)]) continue;
                    run.Clear();
                    int cx = x, cy = y, gap = 0;
                    while (cx >= 0 && cy >= 0 && cx < w && cy < h)
                    {
                        if (Line[Index(cx, cy)]) { run.Add(Index(cx, cy)); gap = 0; }
                        else if (++gap > 1) break;
                        cx += dx; cy += dy;
                    }
                    if (run.Count >= least) foreach (int c in run) res[c] = true;
                }
        return res;
    }

    IEnumerable<int> Neighbours(int x, int y)
    {
        if (x > 0) yield return Index(x - 1, y);
        if (x < Width - 1) yield return Index(x + 1, y);
        if (y > 0) yield return Index(x, y - 1);
        if (y < Height - 1) yield return Index(x, y + 1);
    }

    void Measure(Patch patch)
    {
        double r = 0, g = 0, b = 0, l = 0, l2 = 0, t = 0;
        int w = Width;
        foreach (int c in patch.Cells)
        {
            r += R[c]; g += G[c]; b += B[c]; t += Tex[c];
            l += Lum[c]; l2 += Lum[c] * Lum[c];
            int x = c % w, y = c / w;
            patch.X0 = Math.Min(patch.X0, x); patch.X1 = Math.Max(patch.X1, x);
            patch.Y0 = Math.Min(patch.Y0, y); patch.Y1 = Math.Max(patch.Y1, y);
        }
        int n = patch.Cells.Count;
        patch.R = r / n; patch.G = g / n; patch.B = b / n; patch.Tex = t / n;
        double mean = l / n;
        patch.Busy = Math.Sqrt(Math.Max(0, l2 / n - mean * mean));
        int lined = 0;
        foreach (int c in patch.Cells)
        {
            int x = c % w, y = c / w;
            bool edge = false, line = false;
            foreach (int q in Neighbours(x, y))
            {
                if (PatchOf[q] == patch.Id) continue;
                edge = true;
                if (Line[q]) line = true;
            }
            if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1) edge = true;
            if (!edge) continue;
            patch.Edge.Add(c);
            if (line) lined++;
        }
        patch.Outlined = patch.Edge.Count == 0 ? 0 : (double)lined / patch.Edge.Count;
    }

    // ---- shapes ----

    /// <summary>The smallest turned rectangle round a set of cells (their centres, with half a cell round them).</summary>
    public Box MinBox(IEnumerable<int> cells)
    {
        var pts = new List<(double X, double Y)>();
        foreach (int c in cells)
        {
            double x = c % Width, y = c / Width;
            pts.Add((x, y)); pts.Add((x + 1, y)); pts.Add((x, y + 1)); pts.Add((x + 1, y + 1));
        }
        return MinBox(pts);
    }

    /// <summary>The smallest turned rectangle round some points (rotating calipers on their hull).</summary>
    public static Box MinBox(List<(double X, double Y)> pts)
    {
        var hull = Hull(pts);
        if (hull.Count == 0) return new Box(0, 0, 0, 0, 0);
        if (hull.Count == 1) return new Box(hull[0].X, hull[0].Y, 0, 0, 0);
        Box best = default;
        double bestArea = double.MaxValue;
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double c = Math.Cos(-ang), s = Math.Sin(-ang);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var q in hull)
            {
                double u = q.X * c - q.Y * s, v = q.X * s + q.Y * c;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            double area = (u1 - u0) * (v1 - v0);
            if (area < bestArea - 1e-9)
            {
                bestArea = area;
                double cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
                // Back to the picture's frame.
                double cx = cu * Math.Cos(ang) - cv * Math.Sin(ang), cy = cu * Math.Sin(ang) + cv * Math.Cos(ang);
                best = new Box(cx, cy, ang * 180 / Math.PI, u1 - u0, v1 - v0);
            }
        }
        return Upright(best);
    }

    /// <summary>The same box with its turn between 0 and 90 degrees (the sides swapped as needed).</summary>
    public static Box Upright(Box b)
    {
        double a = ((b.AngleDeg % 180) + 180) % 180;
        double w = b.W, h = b.H;
        if (a >= 90) { a -= 90; (w, h) = (h, w); }
        return new Box(b.Cx, b.Cy, a, w, h);
    }

    /// <summary>The convex hull of some points (Andrew's monotone chain), anticlockwise in picture coordinates.</summary>
    public static List<(double X, double Y)> Hull(List<(double X, double Y)> pts)
    {
        var p = pts.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        var lower = new List<(double X, double Y)>();
        foreach (var q in p)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], q) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(q);
        }
        var upper = new List<(double X, double Y)>();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var q = p[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], q) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(q);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
