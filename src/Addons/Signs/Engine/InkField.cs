using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// How far each point is inside the words (positive) or outside them (negative), in mm, up to a cap:
/// carving only ever needs the distance near the letters' edges (a V bit's depth, a bevel's width).
/// Inside or out comes from the outlines by the non-zero rule along each row; the distance from the
/// edge pieces near the point, found through a grid of cells at least as big as the cap.
/// </summary>
public sealed class InkField
{
    readonly double[] _ax, _ay, _bx, _by;
    readonly double _cap;
    // Rows: edge pieces by bands of y, for the inside test.
    readonly double _bandY0, _bandH;
    readonly int[][] _bands;
    // Cells: edge pieces by square cells, for the distance.
    readonly double _cellX0, _cellY0, _cell;
    readonly int _cellsW, _cellsH;
    readonly int[] _cellStart, _cellItems;

    public double Cap => _cap;
    public bool IsEmpty => _ax.Length == 0;

    public InkField(IReadOnlyList<Vec2[]> outlines, double cap)
    {
        _cap = Math.Max(1e-3, cap);
        var ax = new List<double>(); var ay = new List<double>(); var bx = new List<double>(); var by = new List<double>();
        var box = Rect2.Empty;
        foreach (var o in outlines)
        {
            if (o.Length < 3) continue;
            for (int i = 0; i < o.Length; i++)
            {
                var a = o[i];
                var b = o[(i + 1) % o.Length];
                if (a == b) continue;
                ax.Add(a.X); ay.Add(a.Y); bx.Add(b.X); by.Add(b.Y);
                box = box.Include(a);
            }
        }
        _ax = [.. ax]; _ay = [.. ay]; _bx = [.. bx]; _by = [.. by];
        int n = _ax.Length;
        if (n == 0)
        {
            _bands = [];
            _cellStart = [0];
            _cellItems = [];
            return;
        }

        // Bands about 0.5 mm high (fewer for very tall words).
        _bandY0 = box.MinY;
        int bandCount = Math.Clamp((int)Math.Ceiling(box.Height / 0.5), 1, 20000);
        _bandH = Math.Max(1e-6, box.Height / bandCount);
        var bandLists = new List<int>[bandCount];
        for (int i = 0; i < bandCount; i++) bandLists[i] = [];
        for (int e = 0; e < n; e++)
        {
            int b0 = Math.Clamp((int)Math.Floor((Math.Min(_ay[e], _by[e]) - _bandY0) / _bandH), 0, bandCount - 1);
            int b1 = Math.Clamp((int)Math.Floor((Math.Max(_ay[e], _by[e]) - _bandY0) / _bandH), 0, bandCount - 1);
            for (int b = b0; b <= b1; b++) bandLists[b].Add(e);
        }
        _bands = [.. bandLists.Select(l => l.ToArray())];

        // Cells no smaller than the cap, so everything within it is in the 3 × 3 cells round a point.
        var grown = box.Inflate(_cap);
        _cell = Math.Max(_cap, Math.Max(grown.Width, grown.Height) / 1500);
        _cellX0 = grown.MinX;
        _cellY0 = grown.MinY;
        _cellsW = Math.Max(1, (int)Math.Ceiling(grown.Width / _cell));
        _cellsH = Math.Max(1, (int)Math.Ceiling(grown.Height / _cell));
        var counts = new int[_cellsW * _cellsH + 1];
        void Each(int e, Action<int> act)
        {
            int c0 = Math.Clamp((int)Math.Floor((Math.Min(_ax[e], _bx[e]) - _cellX0) / _cell), 0, _cellsW - 1);
            int c1 = Math.Clamp((int)Math.Floor((Math.Max(_ax[e], _bx[e]) - _cellX0) / _cell), 0, _cellsW - 1);
            int r0 = Math.Clamp((int)Math.Floor((Math.Min(_ay[e], _by[e]) - _cellY0) / _cell), 0, _cellsH - 1);
            int r1 = Math.Clamp((int)Math.Floor((Math.Max(_ay[e], _by[e]) - _cellY0) / _cell), 0, _cellsH - 1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++) act(r * _cellsW + c);
        }
        for (int e = 0; e < n; e++) Each(e, c => counts[c + 1]++);
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        _cellStart = counts;
        _cellItems = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (int e = 0; e < n; e++) { int ee = e; Each(e, c => _cellItems[fill[c]++] = ee); }
    }

    /// <summary>The non-zero winding of the outlines round (x, y).</summary>
    int Winding(double x, double y)
    {
        if (_bands.Length == 0) return 0;
        int b = (int)Math.Floor((y - _bandY0) / _bandH);
        if (b < 0 || b >= _bands.Length) return 0;
        int w = 0;
        foreach (int e in _bands[b])
        {
            double ay = _ay[e], by = _by[e];
            if ((ay <= y) == (by <= y)) continue;
            double cx = _ax[e] + (y - ay) * (_bx[e] - _ax[e]) / (by - ay);
            if (cx > x) w += by > ay ? 1 : -1;
        }
        return w;
    }

    /// <summary>The distance to the nearest edge piece, up to the cap.</summary>
    double Distance(double x, double y)
    {
        int c = (int)Math.Floor((x - _cellX0) / _cell), r = (int)Math.Floor((y - _cellY0) / _cell);
        if (c < -1 || r < -1 || c > _cellsW || r > _cellsH) return _cap;
        double best = _cap * _cap;
        for (int rr = Math.Max(0, r - 1); rr <= Math.Min(_cellsH - 1, r + 1); rr++)
            for (int cc = Math.Max(0, c - 1); cc <= Math.Min(_cellsW - 1, c + 1); cc++)
            {
                int cell = rr * _cellsW + cc;
                for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    int e = _cellItems[k];
                    double ax = _ax[e], ay = _ay[e], dx = _bx[e] - ax, dy = _by[e] - ay;
                    double px = x - ax, py = y - ay, l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-18 ? 0 : (px * dx + py * dy) / l2;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double ex = px - dx * t, ey = py - dy * t, d2 = ex * ex + ey * ey;
                    if (d2 < best) best = d2;
                }
            }
        return Math.Sqrt(best);
    }

    /// <summary>The signed distance at one point (mm): + inside the words, − outside, within ± the cap.</summary>
    public double At(double x, double y)
    {
        if (IsEmpty) return -_cap;
        double d = Distance(x, y);
        return Winding(x, y) != 0 ? d : -d;
    }

    /// <summary>
    /// The signed distance at the middle of every pixel of a window <paramref name="cols"/> × <paramref name="rows"/>
    /// of <paramref name="mmPerPx"/>, its first pixel's corner at (<paramref name="x0"/>, <paramref name="y0"/>).
    /// </summary>
    public float[] Fill(double x0, double y0, double mmPerPx, int cols, int rows, CancellationToken ct = default)
    {
        var o = new float[cols * rows];
        if (IsEmpty) { Array.Fill(o, (float)-_cap); return o; }
        var opts = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, rows, opts, r =>
        {
            double y = y0 + (r + 0.5) * mmPerPx;
            // Where the row crosses the edges, in order, with their winding.
            var cross = new List<(double X, int W)>();
            int b = (int)Math.Floor((y - _bandY0) / _bandH);
            if (b >= 0 && b < _bands.Length)
                foreach (int e in _bands[b])
                {
                    double ay = _ay[e], by = _by[e];
                    if ((ay <= y) == (by <= y)) continue;
                    cross.Add((_ax[e] + (y - ay) * (_bx[e] - _ax[e]) / (by - ay), by > ay ? 1 : -1));
                }
            cross.Sort((p, q) => p.X.CompareTo(q.X));
            int next = 0, wind = 0;
            for (int c = 0; c < cols; c++)
            {
                double x = x0 + (c + 0.5) * mmPerPx;
                while (next < cross.Count && cross[next].X < x) wind += cross[next++].W;
                double d = Distance(x, y);
                o[r * cols + c] = (float)(wind != 0 ? d : -d);
            }
        });
        return o;
    }
}
