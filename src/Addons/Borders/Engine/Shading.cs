using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.Borders;

/// <summary>How the preview shows the heights.</summary>
public enum PreviewLook
{
    /// <summary>Carved wood, darker where it's deeper (as a laser leaves it), lit from the top left.</summary>
    Wood,
    /// <summary>Gilded, with shine.</summary>
    Gilt,
    /// <summary>Bare metal, as a fiber laser carves it.</summary>
    Metal,
    /// <summary>The depth map itself, white high and black deep.</summary>
    DepthMap,
}

/// <summary>Turns heights into a lit picture of the carving.</summary>
public static class Shading
{
    public static RasterImage Shade(float[] h, int cols, int rows, double mmPerPx, PreviewLook look, double top, double lowest)
    {
        if (look == PreviewLook.DepthMap) return BorderPlan.DepthMap(h, cols, rows, top, lowest);
        var px = new byte[cols * rows * 4];
        double range = Math.Max(1e-9, top - lowest);
        // Light from the top left and a little in front.
        double lx = -0.55, ly = -0.65, lz = 0.9, ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ln; ly /= ln; lz /= ln;
        // Half way between the light and the eye (straight above), for the shine.
        double hx = lx, hy = ly, hz = lz + 1, hn = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        hx /= hn; hy /= hn; hz /= hn;
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c, o = i * 4;
                float z = h[i];
                if (float.IsNaN(z)) { px[o + 3] = 0; continue; }
                double Z(int cc, int rr)
                {
                    if (cc < 0 || rr < 0 || cc >= cols || rr >= rows) return z;
                    float v = h[rr * cols + cc];
                    return float.IsNaN(v) ? z : v;
                }
                double dx = (Z(c + 1, r) - Z(c - 1, r)) / (2 * mmPerPx), dy = (Z(c, r + 1) - Z(c, r - 1)) / (2 * mmPerPx);
                double nx = -dx, ny = -dy, nz = 1, nn = Math.Sqrt(nx * nx + ny * ny + 1);
                nx /= nn; ny /= nn; nz /= nn;
                double diffuse = Math.Max(0, nx * lx + ny * ly + nz * lz);
                double spec = Math.Pow(Math.Max(0, nx * hx + ny * hy + nz * hz), 24);
                double depth = Math.Clamp((top - z) / range, 0, 1);
                double rr2, gg, bb;
                switch (look)
                {
                    case PreviewLook.Gilt:
                        {
                            double k = 0.28 + 0.78 * diffuse - 0.12 * depth;
                            rr2 = 214 * k + 255 * 0.55 * spec; gg = 170 * k + 235 * 0.55 * spec; bb = 84 * k + 170 * 0.5 * spec;
                            break;
                        }
                    case PreviewLook.Metal:
                        {
                            double k = 0.3 + 0.7 * diffuse - 0.2 * depth;
                            rr2 = 176 * k + 255 * 0.45 * spec; gg = 181 * k + 255 * 0.45 * spec; bb = 188 * k + 255 * 0.45 * spec;
                            break;
                        }
                    default:
                        {
                            // Laser-carved wood: pale at the surface, browner the deeper it's burned.
                            double k = 0.42 + 0.68 * diffuse;
                            double w = Math.Pow(depth, 0.7);
                            rr2 = Carve.Lerp(232, 150, w) * k; gg = Carve.Lerp(200, 98, w) * k; bb = Carve.Lerp(152, 56, w) * k;
                            break;
                        }
                }
                px[o] = (byte)Math.Clamp(rr2, 0, 255);
                px[o + 1] = (byte)Math.Clamp(gg, 0, 255);
                px[o + 2] = (byte)Math.Clamp(bb, 0, 255);
                px[o + 3] = 255;
            }
        });
        return new RasterImage(cols, rows, px);
    }
}
