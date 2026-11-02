using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// Where the wood's grain is dark and where it's light, read from a photo of the board on the bed:
/// for every cell, 0 = the lightest grain (earlywood) to 1 = the darkest (latewood). The board's
/// overall colour and the lighting across it are taken out first, so only the grain is left.
/// Immutable.
/// </summary>
/// <param name="Origin">Work-area position of cell (0, 0)'s corner, mm.</param>
/// <param name="CellMm">Cell size, mm.</param>
/// <param name="Levels">Row-major, 0–254 = lightest to darkest grain, <see cref="Unknown"/> = not seen (off the photo or the board).</param>
/// <param name="ContrastPct">How strongly the grain shows in the photo: the brightness difference between light and dark grain, %.</param>
/// <param name="AngleDeg">Which way the grain runs (0 = left–right, 90 = top–bottom); NaN when there's no clear direction.</param>
public sealed record GrainMap(Vec2 Origin, double CellMm, int Width, int Height, byte[] Levels, double ContrastPct, double AngleDeg)
{
    public const byte Unknown = 255;

    /// <summary>Most cells in a map (a 400 × 400 mm board at 0.25 mm).</summary>
    public const int MaxCells = 2_600_000;

    /// <summary>The finest cell a map is made at, mm (the overview camera sees about 0.3–0.5 mm per pixel).</summary>
    public const double FinestCellMm = 0.25;

    public Rect2 Area => new(Origin.X, Origin.Y, Origin.X + Width * CellMm, Origin.Y + Height * CellMm);

    /// <summary>Grain darkness 0–1 at cell (c, r), NaN where unknown.</summary>
    public float Cell(int c, int r) => Levels[r * Width + c] is var v && v == Unknown ? float.NaN : v / 254f;

    /// <summary>Grain darkness 0–1 at <paramref name="p"/> (mm), blended between cells; NaN off the map or where unknown.</summary>
    public double At(Vec2 p)
    {
        double fx = (p.X - Origin.X) / CellMm - 0.5, fy = (p.Y - Origin.Y) / CellMm - 0.5;
        if (!(fx > -1 && fy > -1 && fx < Width && fy < Height)) return double.NaN;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = fx - x0, ty = fy - y0, sum = 0, wsum = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = y0 + (k >> 1);
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            byte v = Levels[y * Width + x];
            if (v == Unknown) continue;
            double w = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            sum += w * v / 254.0;
            wsum += w;
        }
        return wsum < 0.25 ? double.NaN : sum / wsum;
    }

    /// <summary>The same map with every cell whose middle isn't <paramref name="keep"/> made unknown (off the board).</summary>
    public GrainMap Masked(Func<Vec2, bool> keep)
    {
        var levels = (byte[])Levels.Clone();
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
                if (!keep(new Vec2(Origin.X + (c + 0.5) * CellMm, Origin.Y + (r + 0.5) * CellMm))) levels[r * Width + c] = Unknown;
        return this with { Levels = levels };
    }

    /// <summary>How much of the map was seen (0–1).</summary>
    public double KnownFraction => Levels.Length == 0 ? 0 : Levels.Count(v => v != Unknown) / (double)Levels.Length;

    /// <summary>
    /// The grain of the board under <paramref name="region"/> (mm), from a straightened photo of the
    /// work area covering <paramref name="photoArea"/> at <paramref name="photoMmPerPx"/>. Parts of the
    /// photo that are see-through (the camera can't see them) stay unknown.
    /// </summary>
    /// <param name="lightingMm">Brightness changes wider than about this are lighting or the board's own colour, not grain, and are taken out.</param>
    /// <param name="detailMm">Smaller than this is camera noise or wood pores, and is smoothed away.</param>
    public static GrainMap FromPhoto(RasterImage photo, Rect2 photoArea, double photoMmPerPx, Rect2 region, double lightingMm = 10, double detailMm = 0.4)
    {
        var area = Intersect(region, photoArea);
        if (area.IsEmpty || area.Width < 1 || area.Height < 1) throw new ArgumentException("The camera picture doesn't cover that area.");
        double cell = Math.Max(FinestCellMm, photoMmPerPx);
        int w = Math.Max(1, (int)Math.Floor(area.Width / cell)), h = Math.Max(1, (int)Math.Floor(area.Height / cell));
        while ((long)w * h > MaxCells)
        {
            cell *= 1.25;
            w = Math.Max(1, (int)Math.Floor(area.Width / cell));
            h = Math.Max(1, (int)Math.Floor(area.Height / cell));
        }
        var origin = area.TopLeft;
        // The photo's brightness at each cell's centre (log, so lighting divides out).
        var lum = photo.Luminance();
        double pxW = photo.Width / Math.Max(1e-9, photoArea.Width), pxH = photo.Height / Math.Max(1e-9, photoArea.Height);
        var g = new float[w * h];
        Parallel.For(0, h, r =>
        {
            for (int c = 0; c < w; c++)
            {
                double x = (origin.X + (c + 0.5) * cell - photoArea.MinX) * pxW - 0.5;
                double y = (origin.Y + (r + 0.5) * cell - photoArea.MinY) * pxH - 0.5;
                g[r * w + c] = SampleLog(photo, lum, x, y);
            }
        });
        return FromLogBrightness(g, w, h, origin, cell, lightingMm, detailMm);
    }

    /// <summary>The map from log-brightness cells (NaN = unseen): the shared part of <see cref="FromPhoto"/>, for tests.</summary>
    public static GrainMap FromLogBrightness(float[] logL, int w, int h, Vec2 origin, double cell, double lightingMm = 10, double detailMm = 0.4)
    {
        // Lighting and the board's own colour: the slow changes, taken out.
        var slow = Blur(logL, w, h, lightingMm / cell);
        var fine = Blur(logL, w, h, detailMm / cell);
        var d = new float[w * h];
        var known = new List<float>(w * h);
        for (int i = 0; i < d.Length; i++)
        {
            // Darker than its surroundings = higher.
            d[i] = float.IsNaN(logL[i]) ? float.NaN : slow[i] - fine[i];
            if (!float.IsNaN(d[i])) known.Add(d[i]);
        }
        var levels = new byte[w * h];
        if (known.Count < 16)
        {
            Array.Fill(levels, Unknown);
            return new GrainMap(origin, cell, w, h, levels, 0, double.NaN);
        }
        known.Sort();
        float lo = known[(int)(known.Count * 0.05)], hi = known[(int)(known.Count * 0.95)];
        double spread = hi - lo;
        for (int i = 0; i < d.Length; i++)
        {
            if (float.IsNaN(d[i])) { levels[i] = Unknown; continue; }
            levels[i] = spread < 1e-4 ? (byte)127 : (byte)Math.Clamp((int)Math.Round((d[i] - lo) / spread * 254), 0, 254);
        }
        // log brightness: e^spread − 1 is the light-to-dark difference.
        double contrast = (Math.Exp(spread) - 1) * 100;
        return new GrainMap(origin, cell, w, h, levels, contrast, Direction(d, w, h));
    }

    /// <summary>Which way the grain lines run (degrees, 0–180), from the brightness gradients; NaN when there's no clear direction.</summary>
    static double Direction(float[] d, int w, int h)
    {
        double xx = 0, yy = 0, xy = 0;
        for (int r = 1; r < h - 1; r++)
            for (int c = 1; c < w - 1; c++)
            {
                int i = r * w + c;
                float gx = (d[i + 1] - d[i - 1]) / 2, gy = (d[i + w] - d[i - w]) / 2;
                if (float.IsNaN(gx) || float.IsNaN(gy)) continue;
                xx += gx * gx;
                yy += gy * gy;
                xy += gx * gy;
            }
        double tr = xx + yy;
        if (tr <= 1e-12) return double.NaN;
        // How one-sided the gradients are: 0 = every way, 1 = all one way.
        double coherence = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy) / tr;
        if (coherence < 0.2) return double.NaN;
        // The gradients point across the grain; the lines run at right angles to them.
        double across = 0.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI;
        double along = across + 90;
        along %= 180;
        if (along < 0) along += 180;
        return Math.Round(along, 1);
    }

    /// <summary>The photo's log brightness at work-area point <paramref name="p"/> (NaN off it or where it's see-through).</summary>
    internal static float SampleLogAt(RasterImage photo, float[] lum, Rect2 photoArea, Vec2 p) =>
        SampleLog(photo, lum, (p.X - photoArea.MinX) * photo.Width / Math.Max(1e-9, photoArea.Width) - 0.5,
            (p.Y - photoArea.MinY) * photo.Height / Math.Max(1e-9, photoArea.Height) - 0.5);

    static float SampleLog(RasterImage photo, float[] lum, double x, double y)
    {
        int W = photo.Width, H = photo.Height;
        if (x < -0.5 || y < -0.5 || x > W - 0.5 || y > H - 0.5) return float.NaN;
        x = Math.Clamp(x, 0, W - 1);
        y = Math.Clamp(y, 0, H - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(W - 1, x0 + 1), y1 = Math.Min(H - 1, y0 + 1);
        double tx = x - x0, ty = y - y0;
        double sum = 0, wsum = 0;
        foreach (var (px, py, wt) in new[] { (x0, y0, (1 - tx) * (1 - ty)), (x1, y0, tx * (1 - ty)), (x0, y1, (1 - tx) * ty), (x1, y1, tx * ty) })
        {
            int i = py * W + px;
            if (photo.Rgba[i * 4 + 3] < 128) continue; // the camera can't see there
            sum += wt * lum[i];
            wsum += wt;
        }
        if (wsum < 0.5) return float.NaN;
        return (float)Math.Log(sum / wsum + 0.02);
    }

    static Rect2 Intersect(Rect2 a, Rect2 b)
    {
        var r = new Rect2(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY));
        return r.MaxX > r.MinX && r.MaxY > r.MinY ? r : Rect2.Empty;
    }

    /// <summary>A Gaussian-like blur (three box passes) that skips NaN cells and leaves them NaN.</summary>
    public static float[] Blur(float[] src, int w, int h, double sigmaCells)
    {
        var val = new float[src.Length];
        var wt = new float[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            bool ok = !float.IsNaN(src[i]);
            val[i] = ok ? src[i] : 0;
            wt[i] = ok ? 1 : 0;
        }
        if (sigmaCells >= 0.3)
        {
            // Three box passes of this half-width come close to a Gaussian of that sigma.
            int rad = (int)Math.Max(1, Math.Round((Math.Sqrt(4 * sigmaCells * sigmaCells + 1) - 1) / 2));
            for (int pass = 0; pass < 3; pass++)
            {
                BoxRows(val, w, h, rad);
                BoxRows(wt, w, h, rad);
                BoxCols(val, w, h, rad);
                BoxCols(wt, w, h, rad);
            }
        }
        var o = new float[src.Length];
        for (int i = 0; i < o.Length; i++)
            o[i] = float.IsNaN(src[i]) || wt[i] < 1e-6f ? float.NaN : val[i] / wt[i];
        return o;
    }

    static void BoxRows(float[] a, int w, int h, int rad)
    {
        Parallel.For(0, h, r =>
        {
            var line = new float[w];
            Array.Copy(a, r * w, line, 0, w);
            double sum = 0;
            for (int c = -rad; c <= rad; c++) sum += line[Math.Clamp(c, 0, w - 1)];
            for (int c = 0; c < w; c++)
            {
                a[r * w + c] = (float)(sum / (2 * rad + 1));
                sum += line[Math.Clamp(c + rad + 1, 0, w - 1)] - line[Math.Clamp(c - rad, 0, w - 1)];
            }
        });
    }

    static void BoxCols(float[] a, int w, int h, int rad)
    {
        Parallel.For(0, w, c =>
        {
            var line = new float[h];
            for (int r = 0; r < h; r++) line[r] = a[r * w + c];
            double sum = 0;
            for (int r = -rad; r <= rad; r++) sum += line[Math.Clamp(r, 0, h - 1)];
            for (int r = 0; r < h; r++)
            {
                a[r * w + c] = (float)(sum / (2 * rad + 1));
                sum += line[Math.Clamp(r + rad + 1, 0, h - 1)] - line[Math.Clamp(r - rad, 0, h - 1)];
            }
        });
    }

    /// <summary>The map as a picture: light grain light, dark grain dark, unknown see-through (for the canvas overlay and the file).</summary>
    public RasterImage ToImage()
    {
        var rgba = new byte[Width * Height * 4];
        for (int i = 0; i < Levels.Length; i++)
        {
            byte v = Levels[i];
            // 254 − level, so the picture reads back exactly.
            byte g = v == Unknown ? (byte)0 : (byte)(254 - v);
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = g;
            rgba[i * 4 + 3] = v == Unknown ? (byte)0 : (byte)255;
        }
        return new RasterImage(Width, Height, rgba);
    }

    public JsonObject ToJson() => new()
    {
        ["origin"] = new JsonArray(Origin.X, Origin.Y),
        ["cellMm"] = CellMm,
        ["width"] = Width,
        ["height"] = Height,
        ["contrastPct"] = Math.Round(ContrastPct, 2),
        ["angleDeg"] = double.IsNaN(AngleDeg) ? null : AngleDeg,
        // Lossless, small: the levels as a grey picture, unknown cells see-through.
        ["png"] = Convert.ToBase64String(ToImage().EncodePng()),
    };

    /// <summary>A map read back (null if it isn't one).</summary>
    public static GrainMap? FromJson(JsonNode? n)
    {
        try
        {
            if (n is not JsonObject o || o["png"]?.GetValue<string>() is not { } png) return null;
            var img = RasterImage.Decode(Convert.FromBase64String(png));
            int w = o["width"]?.GetValue<int>() ?? img.Width, h = o["height"]?.GetValue<int>() ?? img.Height;
            if (w != img.Width || h != img.Height) return null;
            var levels = new byte[w * h];
            for (int i = 0; i < levels.Length; i++)
                levels[i] = img.Rgba[i * 4 + 3] < 128 ? Unknown : (byte)Math.Clamp(254 - img.Rgba[i * 4], 0, 254);
            var or = o["origin"]!.AsArray();
            double cell = o["cellMm"]!.GetValue<double>();
            if (!(cell > 0)) return null;
            return new GrainMap(new Vec2(or[0]!.GetValue<double>(), or[1]!.GetValue<double>()), cell, w, h, levels,
                o["contrastPct"]?.GetValue<double>() ?? 0, o["angleDeg"]?.GetValue<double>() ?? double.NaN);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
