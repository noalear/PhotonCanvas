using PhotonCanvas.Core.Imaging;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Doc.Import;

/// <summary>
/// A shaded picture of a 3D model, seen from above at an angle (for thumbnails): every triangle
/// drawn with a depth buffer and lit from the upper left. Transparent where the model isn't.
/// </summary>
public static class MeshPicture
{
    public static RasterImage Render(IReadOnlyList<Tri> tris, int size, double yawDeg = -35, double pitchDeg = 30, uint colour = 0xD9C29A)
    {
        size = Math.Clamp(size, 16, 2048);
        var px = new byte[size * size * 4];
        if (tris.Count == 0) return new RasterImage(size, size, px);
        var depth = new float[size * size];
        Array.Fill(depth, float.NegativeInfinity);
        double yaw = yawDeg * Math.PI / 180, pitch = pitchDeg * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        // Turned into the view: x right, y down the picture, d towards you (Z up in the model).
        (double X, double Y, double D) View(StlDepth.Vec3 v)
        {
            double rx = v.X * cy - v.Y * sy, ry = v.X * sy + v.Y * cy;
            return (rx, ry * sp - v.Z * cp, ry * cp + v.Z * sp);
        }
        var view = new (double X, double Y, double D)[tris.Count * 3];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < tris.Count; i++)
        {
            view[i * 3] = View(tris[i].A);
            view[i * 3 + 1] = View(tris[i].B);
            view[i * 3 + 2] = View(tris[i].C);
            for (int k = 0; k < 3; k++)
            {
                var p = view[i * 3 + k];
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
        }
        double span = Math.Max(maxX - minX, maxY - minY);
        if (!(span > 0)) return new RasterImage(size, size, px);
        double scale = size * 0.9 / span, ox = size / 2.0 - (minX + maxX) / 2 * scale, oy = size / 2.0 - (minY + maxY) / 2 * scale;
        // Light from the upper left, in front.
        double lx = -0.45, ly = -0.7, ld = 0.55, ll = Math.Sqrt(lx * lx + ly * ly + ld * ld);
        lx /= ll; ly /= ll; ld /= ll;
        byte r0 = (byte)(colour >> 16), g0 = (byte)(colour >> 8), b0 = (byte)colour;
        for (int i = 0; i < tris.Count; i++)
        {
            var a = view[i * 3];
            var b = view[i * 3 + 1];
            var c = view[i * 3 + 2];
            // The face's own normal (either side: models aren't always wound consistently).
            double ux = b.X - a.X, uy = b.Y - a.Y, ud = b.D - a.D, vx = c.X - a.X, vy = c.Y - a.Y, vd = c.D - a.D;
            double nx = uy * vd - ud * vy, ny = ud * vx - ux * vd, nd = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nd * nd);
            if (nl < 1e-12) continue;
            if (nd < 0) { nx = -nx; ny = -ny; nd = -nd; }
            double lit = 0.35 + 0.65 * Math.Max(0, (nx * lx + ny * ly + nd * ld) / nl);
            byte r = (byte)Math.Min(255, r0 * lit), g = (byte)Math.Min(255, g0 * lit), bl = (byte)Math.Min(255, b0 * lit);
            double ax = a.X * scale + ox, ay = a.Y * scale + oy, bx = b.X * scale + ox, by = b.Y * scale + oy, qx = c.X * scale + ox, qy = c.Y * scale + oy;
            double area = (bx - ax) * (qy - ay) - (qx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-12) continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, qx)))), x1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, qy)))), y1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, qy))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double sx = x + 0.5, syy = y + 0.5;
                    double w0 = ((bx - sx) * (qy - syy) - (qx - sx) * (by - syy)) / area;
                    double w1 = ((qx - sx) * (ay - syy) - (ax - sx) * (qy - syy)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float d = (float)(w0 * a.D + w1 * b.D + w2 * c.D);
                    int k = y * size + x;
                    if (d <= depth[k]) continue;
                    depth[k] = d;
                    px[k * 4] = r; px[k * 4 + 1] = g; px[k * 4 + 2] = bl; px[k * 4 + 3] = 255;
                }
        }
        return new RasterImage(size, size, px);
    }
}
