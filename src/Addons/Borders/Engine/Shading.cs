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
                var (rr2, gg, bb) = Colour(look, nx, ny, nz, (top - z) / range);
                px[o] = (byte)Math.Clamp(rr2, 0, 255);
                px[o + 1] = (byte)Math.Clamp(gg, 0, 255);
                px[o + 2] = (byte)Math.Clamp(bb, 0, 255);
                px[o + 3] = 255;
            }
        });
        return new RasterImage(cols, rows, px);
    }

    // Light from the top left and a little in front, and half way between it and the eye (straight above) for the shine.
    static readonly (double X, double Y, double Z) Light = Unit(-0.55, -0.65, 0.9), Half = Unit(-0.55 / L0, -0.65 / L0, 0.9 / L0 + 1);
    static double L0 => Math.Sqrt(0.55 * 0.55 + 0.65 * 0.65 + 0.9 * 0.9);

    static (double, double, double) Unit(double x, double y, double z)
    {
        double n = Math.Sqrt(x * x + y * y + z * z);
        return (x / n, y / n, z / n);
    }

    /// <summary>The colour of a spot facing (<paramref name="nx"/>, <paramref name="ny"/>, <paramref name="nz"/>), <paramref name="depth"/> of the way down (0 the surface, 1 the deepest).</summary>
    static (double R, double G, double B) Colour(PreviewLook look, double nx, double ny, double nz, double depth)
    {
        double diffuse = Math.Max(0, nx * Light.X + ny * Light.Y + nz * Light.Z);
        double spec = Math.Pow(Math.Max(0, nx * Half.X + ny * Half.Y + nz * Half.Z), 24);
        depth = Math.Clamp(depth, 0, 1);
        switch (look)
        {
            case PreviewLook.Gilt:
                {
                    double k = 0.28 + 0.78 * diffuse - 0.12 * depth;
                    return (214 * k + 255 * 0.55 * spec, 170 * k + 235 * 0.55 * spec, 84 * k + 170 * 0.5 * spec);
                }
            case PreviewLook.Metal:
                {
                    double k = 0.3 + 0.7 * diffuse - 0.2 * depth;
                    return (176 * k + 255 * 0.45 * spec, 181 * k + 255 * 0.45 * spec, 188 * k + 255 * 0.45 * spec);
                }
            case PreviewLook.DepthMap:
                {
                    double g = 255 * (1 - depth);
                    return (g, g, g);
                }
            default:
                {
                    // Laser-carved wood: pale at the surface, browner the deeper it's burned.
                    double k = 0.42 + 0.68 * diffuse;
                    double w = Math.Pow(depth, 0.7);
                    return (Carve.Lerp(232, 150, w) * k, Carve.Lerp(200, 98, w) * k, Carve.Lerp(152, 56, w) * k);
                }
        }
    }

    /// <summary>
    /// A border wrapped round a rotary part, seen from above as the laser sees it: <paramref name="cols"/> × <paramref name="rows"/>
    /// pixels of <paramref name="mmPerPx"/>, the first one's corner at <paramref name="x0"/> along the part and <paramref name="v0"/>
    /// across it (0 = its far edge, the part's Ø the near one). The row that faces the laser at the start is in the middle,
    /// unless the part is turned <paramref name="turnMm"/> round (mm on its surface). Plain where the border isn't carved.
    /// </summary>
    public static RasterImage ShadeOnPart(BorderPlan plan, double x0, double v0, double mmPerPx, int cols, int rows, PreviewLook look, double turnMm, CancellationToken ct = default)
    {
        var px = new byte[cols * rows * 4];
        double r0 = plan.Spec.PartDiameterMm / 2, c = plan.SizeH, top = plan.Top;
        double range = Math.Max(1e-9, top - plan.Lowest), e = Math.Max(0.02, mmPerPx * 0.7);
        double H(double x, double y)
        {
            double h = plan.HeightAt(Math.Clamp(x, 0, plan.SizeW), y - Math.Floor(y / c) * c);
            return double.IsNaN(h) ? top : h;
        }
        Parallel.For(0, rows, new ParallelOptions { CancellationToken = ct }, r =>
        {
            double v = v0 + (r + 0.5) * mmPerPx - r0;
            if (Math.Abs(v) >= r0) return;
            double phi = Math.Asin(v / r0), sin = v / r0, cos = Math.Cos(phi);
            double y = c / 2 + turnMm + r0 * phi;
            for (int col = 0; col < cols; col++)
            {
                double x = x0 + (col + 0.5) * mmPerPx;
                if (x < 0 || x > plan.SizeW) continue;
                double h = H(x, y);
                double hx = (H(x + e, y) - h) / e, hs = (H(x, y + e) - h) / e;
                // The round surface's own slope, tipped by the carving's slopes along and round it.
                double nx = -hx, ny = sin - hs * cos, nz = cos + hs * sin, n = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                var (rr, gg, bb) = Colour(look, nx / n, ny / n, nz / n, (top - h) / range);
                int o = (r * cols + col) * 4;
                px[o] = (byte)Math.Clamp(rr, 0, 255);
                px[o + 1] = (byte)Math.Clamp(gg, 0, 255);
                px[o + 2] = (byte)Math.Clamp(bb, 0, 255);
                px[o + 3] = 255;
            }
        });
        return new RasterImage(cols, rows, px);
    }
}
