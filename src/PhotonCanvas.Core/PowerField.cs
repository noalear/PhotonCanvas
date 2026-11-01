namespace PhotonCanvas.Core;

/// <summary>
/// How much of a layer's power to use at each place on the work area: a grid of factors from 0 to 1
/// (1 = the layer's power, 0.8 = a fifth less). Never more than the layer's power, so a field can
/// only make a job gentler than the settings that were checked. Places outside the grid, or cells
/// marked unknown, use the full power. Immutable.
/// </summary>
/// <remarks>
/// Fills burn each scan line in pieces, one per change of power along it; pictures have their greys
/// scaled before they are dithered. Grain-aware engraving makes these from a photo of the wood.
/// </remarks>
public sealed class PowerField
{
    readonly float[] _factor;

    /// <summary>
    /// A field whose cell (c, r) covers <paramref name="origin"/> + (c, r) × <paramref name="cellMm"/>
    /// to one cell further. Factors are clamped to 0–1; NaN = unknown (full power).
    /// </summary>
    public PowerField(Vec2 origin, double cellMm, int width, int height, float[] factors)
    {
        if (!(cellMm > 0) || !double.IsFinite(cellMm)) throw new ArgumentOutOfRangeException(nameof(cellMm));
        if (width <= 0 || height <= 0 || factors.Length != width * height) throw new ArgumentException("The factors don't match the size.", nameof(factors));
        Origin = origin;
        CellMm = cellMm;
        Width = width;
        Height = height;
        _factor = new float[factors.Length];
        float lo = 1;
        for (int i = 0; i < factors.Length; i++)
        {
            float f = factors[i];
            _factor[i] = float.IsNaN(f) ? float.NaN : Math.Clamp(f, 0f, 1f);
            if (!float.IsNaN(f)) lo = Math.Min(lo, _factor[i]);
        }
        Lowest = lo;
    }

    PowerField(Vec2 origin, double cellMm, int width, int height, float[] shared, float lowest)
    {
        Origin = origin;
        CellMm = cellMm;
        Width = width;
        Height = height;
        _factor = shared;
        Lowest = lowest;
    }

    public Vec2 Origin { get; }
    public double CellMm { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>The smallest factor anywhere (1 = the field changes nothing).</summary>
    public float Lowest { get; }
    public Rect2 Bounds => new(Origin.X, Origin.Y, Origin.X + Width * CellMm, Origin.Y + Height * CellMm);

    /// <summary>The factor of cell (c, r), NaN where unknown.</summary>
    public float Cell(int c, int r) => _factor[r * Width + c];

    /// <summary>The same field moved by <paramref name="d"/> (sections, the MOPA's offset). Shares the data.</summary>
    public PowerField Shifted(Vec2 d) => new(Origin + d, CellMm, Width, Height, _factor, Lowest);

    /// <summary>The factor at <paramref name="p"/> (mm), blended between cells; 1 outside or where unknown.</summary>
    public double At(Vec2 p)
    {
        double fx = (p.X - Origin.X) / CellMm - 0.5, fy = (p.Y - Origin.Y) / CellMm - 0.5;
        if (!(fx > -1 && fy > -1 && fx < Width && fy < Height)) return 1;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = fx - x0, ty = fy - y0;
        double sum = 0, wsum = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = y0 + (k >> 1);
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            float f = _factor[y * Width + x];
            if (float.IsNaN(f)) continue;
            double w = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            sum += w * f;
            wsum += w;
        }
        // Only partly over known cells: the rest counts as full power.
        return wsum <= 1e-12 ? 1 : sum + (1 - wsum);
    }

    /// <summary>
    /// A straight burn from <paramref name="a"/> to <paramref name="b"/> in pieces of even power:
    /// each piece's end and its factor, rounded to <paramref name="step"/> (pieces at the same rounded
    /// factor are joined). Sampled every half cell; pieces are never shorter than
    /// <paramref name="minPieceMm"/> except the last.
    /// </summary>
    public List<(Vec2 End, double Factor)> Along(Vec2 a, Vec2 b, double step = 0.02, double minPieceMm = 0.2)
    {
        step = Math.Clamp(step, 0.001, 1);
        double len = a.Dist(b);
        var o = new List<(Vec2, double)>();
        double Round(double f) => Math.Clamp(Math.Round(f / step) * step, 0, 1);
        if (len < 1e-9) { o.Add((b, Round(At(a)))); return o; }
        int n = Math.Max(1, (int)Math.Ceiling(len / (CellMm / 2)));
        double pieceStart = 0;
        double current = Round(At(a + (b - a) * (0.5 / n)));
        for (int i = 1; i < n; i++)
        {
            double t = (i + 0.5) / n;
            double f = Round(At(a + (b - a) * t));
            double tEdge = (double)i / n;
            if (Math.Abs(f - current) < step / 2 || (tEdge - pieceStart) * len < minPieceMm) continue;
            o.Add((a + (b - a) * tEdge, current));
            pieceStart = tEdge;
            current = f;
        }
        o.Add((b, current));
        return o;
    }

    /// <summary>Two fields at once: the factors multiplied (null when both are null).</summary>
    public static PowerField? Combine(PowerField? a, PowerField? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        // On the finer grid, over both.
        double cell = Math.Min(a.CellMm, b.CellMm);
        var box = a.Bounds.Union(b.Bounds);
        int w = Math.Max(1, (int)Math.Ceiling(box.Width / cell)), h = Math.Max(1, (int)Math.Ceiling(box.Height / cell));
        var f = new float[w * h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                var p = new Vec2(box.MinX + (c + 0.5) * cell, box.MinY + (r + 0.5) * cell);
                f[r * w + c] = (float)(a.At(p) * b.At(p));
            }
        return new PowerField(box.TopLeft, cell, w, h, f);
    }
}
