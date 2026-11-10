using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>How the preview shows the relief.</summary>
public enum ReliefLook
{
    /// <summary>Carved wood, darker where it's deeper (as a laser leaves it), lit from the top left.</summary>
    Wood,
    /// <summary>Bare metal, as a fiber laser carves it.</summary>
    Metal,
    /// <summary>The depth map itself, white high and black deep.</summary>
    DepthMap,
    /// <summary>The part of the photo it's made from.</summary>
    Photo,
}

/// <summary>Turns the relief's heights into a lit picture of the carving.</summary>
public static class ReliefShading
{
    public static RasterImage Shade(ReliefResult r, ReliefLook look, double depthMm)
    {
        if (look == ReliefLook.DepthMap) return PhotoDepth.DepthMap(r);
        int cols = r.Cols, rows = r.Rows;
        var h = r.Heights;
        double mmpp = r.MmPerPx, depth = Math.Max(0.05, depthMm);
        var px = new byte[cols * rows * 4];
        // Light from the top left and a little in front.
        double lx = -0.55, ly = -0.65, lz = 0.9, ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ln; ly /= ln; lz /= ln;
        // Half way between the light and the eye (straight above), for the shine.
        double hx = lx, hy = ly, hz = lz + 1, hn = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        hx /= hn; hy /= hn; hz /= hn;
        Parallel.For(0, rows, row =>
        {
            for (int c = 0; c < cols; c++)
            {
                int i = row * cols + c, o = i * 4;
                float z = h[i];
                if (float.IsNaN(z)) continue;
                double Z(int cc, int rr)
                {
                    if (cc < 0 || rr < 0 || cc >= cols || rr >= rows) return z * depth;
                    float v = h[rr * cols + cc];
                    return (float.IsNaN(v) ? z : v) * depth;
                }
                double dx = (Z(c + 1, row) - Z(c - 1, row)) / (2 * mmpp), dy = (Z(c, row + 1) - Z(c, row - 1)) / (2 * mmpp);
                double nx = -dx, ny = -dy, nz = 1, nn = Math.Sqrt(nx * nx + ny * ny + 1);
                nx /= nn; ny /= nn; nz /= nn;
                double diffuse = Math.Max(0, nx * lx + ny * ly + nz * lz);
                double spec = Math.Pow(Math.Max(0, nx * hx + ny * hy + nz * hz), 24);
                double deep = Math.Clamp(1 - z, 0, 1);
                double rr, gg, bb;
                if (look == ReliefLook.Metal)
                {
                    double k = 0.3 + 0.7 * diffuse - 0.2 * deep;
                    rr = 176 * k + 255 * 0.45 * spec; gg = 181 * k + 255 * 0.45 * spec; bb = 188 * k + 255 * 0.45 * spec;
                }
                else
                {
                    // Laser-carved wood: pale at the surface, browner the deeper it's burned.
                    double k = 0.42 + 0.68 * diffuse;
                    double w = Math.Pow(deep, 0.7) * 0.8;
                    rr = Lerp(232, 150, w) * k; gg = Lerp(200, 98, w) * k; bb = Lerp(152, 56, w) * k;
                }
                px[o] = (byte)Math.Clamp(rr, 0, 255);
                px[o + 1] = (byte)Math.Clamp(gg, 0, 255);
                px[o + 2] = (byte)Math.Clamp(bb, 0, 255);
                px[o + 3] = 255;
            }
        });
        return new RasterImage(cols, rows, px);
    }

    /// <summary>The photo as it sits on the plaque (see-through outside it).</summary>
    public static RasterImage Photo(ReliefSpec spec, ReliefPhoto photo, ReliefResult r)
    {
        var img = PhotoDepth.Crop(spec, photo, r.Cols, r.Rows);
        var px = img.Rgba;
        for (int i = 0; i < r.Heights.Length; i++) if (float.IsNaN(r.Heights[i])) px[i * 4 + 3] = 0;
        return img;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
