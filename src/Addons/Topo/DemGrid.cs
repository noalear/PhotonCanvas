namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Heights on a regular grid of points, north at the top: <see cref="Dx"/> metres apart going east
/// and <see cref="Dy"/> going south. A missing height (no data, a void in the survey) is NaN.
/// </summary>
public sealed record DemGrid
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Heights, row by row from the north edge.</summary>
    public float[] Z { get; }
    /// <summary>Metres between points going east, and going south.</summary>
    public double Dx { get; init; } = 1;
    public double Dy { get; init; } = 1;
    /// <summary>The grid's spacing is real (the file or the download said so); false for a plain picture, where the size is a guess the person sets.</summary>
    public bool Georeferenced { get; init; } = true;
    /// <summary>Heights are in metres (false: feet, or a picture's grey levels).</summary>
    public HeightUnits Units { get; init; } = HeightUnits.Metres;
    /// <summary>Where the grid's middle is on Earth, when known.</summary>
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    /// <summary>What it is: a place or a file name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Where the heights came from, for the credit line.</summary>
    public string Source { get; init; } = "";

    public DemGrid(int width, int height, float[] z)
    {
        if (width < 2 || height < 2) throw new FormatException("There are too few heights in it to make a map from (at least 2 × 2 are needed).");
        if (z.Length != width * height) throw new ArgumentException("The heights don't match the grid's size.");
        Width = width;
        Height = height;
        Z = z;
    }

    /// <summary>Metres from the first point to the last, across and down.</summary>
    public double WidthM => (Width - 1) * Dx;
    public double HeightM => (Height - 1) * Dy;

    public float At(int x, int y) => Z[y * Width + x];

    /// <summary>The height at a point given in metres from the north-west corner (east, south), blended from the four around it; NaN off the grid or where all four are missing.</summary>
    public double Sample(double xM, double yM)
    {
        double fx = xM / Dx, fy = yM / Dy;
        if (!(fx >= 0 && fy >= 0 && fx <= Width - 1 && fy <= Height - 1)) return double.NaN;
        int x0 = Math.Min((int)fx, Width - 2), y0 = Math.Min((int)fy, Height - 2);
        double tx = fx - x0, ty = fy - y0;
        double sum = 0, wsum = 0;
        void Add(int x, int y, double w)
        {
            float v = Z[y * Width + x];
            if (w <= 0 || float.IsNaN(v)) return;
            sum += v * w;
            wsum += w;
        }
        Add(x0, y0, (1 - tx) * (1 - ty));
        Add(x0 + 1, y0, tx * (1 - ty));
        Add(x0, y0 + 1, (1 - tx) * ty);
        Add(x0 + 1, y0 + 1, tx * ty);
        return wsum > 1e-9 ? sum / wsum : double.NaN;
    }

    /// <summary>The lowest and highest heights there are (NaN, NaN when there are none).</summary>
    public (double Low, double High) Range()
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var v in Z)
            if (!float.IsNaN(v)) { if (v < lo) lo = v; if (v > hi) hi = v; }
        return double.IsInfinity(lo) ? (double.NaN, double.NaN) : (lo, hi);
    }
}

public enum HeightUnits { Metres, Feet, Levels }

/// <summary>
/// Averages a big grid down while it is read, a block of points into one, so a 10,000-point-wide
/// survey file never has to be held whole. Missing heights are left out of the averages.
/// </summary>
sealed class GridReducer
{
    readonly int _w, _h, _f, _ow, _oh;
    readonly double[] _sum;
    readonly int[] _count;

    public GridReducer(int width, int height, int maxSide)
    {
        _w = width;
        _h = height;
        _f = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)Math.Max(2, maxSide)));
        _ow = (width + _f - 1) / _f;
        _oh = (height + _f - 1) / _f;
        _sum = new double[_ow * _oh];
        _count = new int[_ow * _oh];
    }

    /// <summary>How many of the file's points go into one of the grid's, along each side.</summary>
    public int Factor => _f;

    public void Add(int x, int y, double v)
    {
        if (double.IsNaN(v) || (uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        int o = y / _f * _ow + x / _f;
        _sum[o] += v;
        _count[o]++;
    }

    public DemGrid Grid(double dx, double dy)
    {
        var z = new float[_ow * _oh];
        int have = 0;
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = _count[i] > 0 ? (float)(_sum[i] / _count[i]) : float.NaN;
            if (_count[i] > 0) have++;
        }
        if (have == 0) throw new FormatException("There are no heights in it: every point is marked as missing.");
        return new DemGrid(_ow, _oh, z) { Dx = dx * _f, Dy = dy * _f };
    }
}
