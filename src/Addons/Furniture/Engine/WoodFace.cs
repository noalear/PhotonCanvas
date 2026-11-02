using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Wood detail for a face: grain as wavy scored lines (with the odd knot), and planks as etched
/// grooves between boards with a butt joint here and there. Also iron bands, rivets and the like.
/// </summary>
public static class WoodFace
{
    /// <summary>Space between grain lines (mm).</summary>
    public const double GrainMm = 0.9;
    /// <summary>Width of the etched groove between planks (mm).</summary>
    public const double GrooveMm = 0.35;

    public static (PathsD Etched, List<PathD> Lines) Make(PathsD region, bool alongU, WoodDetail detail, bool planks, double plankMm, Random rnd)
    {
        if (detail == WoodDetail.None || region.Count == 0) return ([], []);
        var etched = new PathsD();
        var lines = new List<PathD>();
        var b = Geo.Bounds(region);
        double along = alongU ? b.right - b.left : b.bottom - b.top, across = alongU ? b.bottom - b.top : b.right - b.left;
        if (along < 1 || across < 0.8) return ([], []);
        // Planks only where the face is wide enough for two of them.
        int boards = detail == WoodDetail.Planks && planks ? Math.Max(1, (int)Math.Round(across / Math.Max(1.2, plankMm))) : 1;
        if (boards > 1)
        {
            double pw = across / boards;
            var seams = new PathsD();
            for (int i = 1; i < boards; i++) seams.Add(Strip(b, alongU, i * pw, GrooveMm));
            // A butt joint in some boards, away from the ends.
            for (int i = 0; i < boards; i++)
            {
                if (along < 12 || rnd.NextDouble() < 0.4) continue;
                double at = along * (0.25 + 0.5 * rnd.NextDouble());
                seams.Add(Cross(b, alongU, at, i * pw, (i + 1) * pw, GrooveMm));
            }
            etched.AddRange(Geo.Intersect(Geo.Union(seams), region));
            for (int i = 0; i < boards; i++) lines.AddRange(Grain(region, b, alongU, i * pw + GrooveMm, (i + 1) * pw - GrooveMm, rnd));
        }
        else lines.AddRange(Grain(region, b, alongU, 0, across, rnd));
        return (etched, lines);
    }

    /// <summary>A strip along the grain, <paramref name="at"/> mm across the bounds.</summary>
    static PathD Strip(RectD b, bool alongU, double at, double w) => alongU
        ? Geo.Rect(b.left - 1, b.top + at - w / 2, b.right + 1, b.top + at + w / 2)
        : Geo.Rect(b.left + at - w / 2, b.top - 1, b.left + at + w / 2, b.bottom + 1);

    /// <summary>A short strip across the grain at <paramref name="at"/> along it, between two distances across.</summary>
    static PathD Cross(RectD b, bool alongU, double at, double from, double to, double w) => alongU
        ? Geo.Rect(b.left + at - w / 2, b.top + from, b.left + at + w / 2, b.top + to)
        : Geo.Rect(b.left + from, b.top + at - w / 2, b.left + to, b.top + at + w / 2);

    /// <summary>Wavy grain lines between two distances across the bounds, clipped to the region, with a knot now and then.</summary>
    static List<PathD> Grain(PathsD region, RectD b, bool alongU, double from, double to, Random rnd)
    {
        var lines = new List<PathD>();
        double width = to - from;
        if (width < 0.5) return lines;
        double along = alongU ? b.right - b.left : b.bottom - b.top;
        int n = Math.Max(1, (int)Math.Floor(width / GrainMm));
        double phase = rnd.NextDouble() * 10, wave = 6 + rnd.NextDouble() * 6;
        // A knot: the lines bulge round it.
        bool knot = along > 10 && width > 2.5 && rnd.NextDouble() < 0.35;
        double kx = along * (0.2 + 0.6 * rnd.NextDouble()), ky = from + width * (0.3 + 0.4 * rnd.NextDouble()), kr = Math.Min(width * 0.18, 0.8);
        for (int i = 0; i < n; i++)
        {
            double c = from + width * (i + 0.5) / n + (rnd.NextDouble() - 0.5) * GrainMm * 0.3;
            // Grain lines stop short of the ends now and then, like real grain running out.
            double start = rnd.NextDouble() < 0.3 ? along * rnd.NextDouble() * 0.3 : -1;
            double end = rnd.NextDouble() < 0.3 ? along * (1 - rnd.NextDouble() * 0.3) : along + 1;
            var p = new PathD();
            for (double s = start; s <= end + 1e-9; s += 0.5)
            {
                double off = 0.12 * Math.Sin((s + phase) / wave * 2 * Math.PI) + 0.06 * Math.Sin((s + phase * 3) / (wave * 0.37) * 2 * Math.PI);
                if (knot)
                {
                    double d = Math.Sqrt((s - kx) * (s - kx) + (c - ky) * (c - ky));
                    if (d < kr * 4) off += (c >= ky ? 1 : -1) * kr * 1.2 * Math.Exp(-(s - kx) * (s - kx) / (kr * kr * 3)) * Math.Max(0, 1 - Math.Abs(c - ky) / (kr * 5));
                }
                p.Add(alongU ? new PointD(b.left + s, b.top + c + off) : new PointD(b.left + c + off, b.top + s));
            }
            if (p.Count >= 2) lines.Add(p);
        }
        if (knot)
        {
            var k = new PathD();
            for (int i = 0; i <= 16; i++)
            {
                double a = i / 16.0 * Math.PI * 2;
                double x = kx + Math.Cos(a) * kr * 1.4, y = ky + Math.Sin(a) * kr * 0.7;
                k.Add(alongU ? new PointD(b.left + x, b.top + y) : new PointD(b.left + y, b.top + x));
            }
            lines.Add(k);
        }
        return Geo.ClipLines(lines, region);
    }

    /// <summary>A small circle as a scored line: a rivet, a nail head, a ring.</summary>
    public static PathD Dot(double x, double y, double r) => Geo.Loop(Geo.Circle(x, y, r, 0.01));

    /// <summary>A rounded rectangle.</summary>
    public static PathD RoundRect(double x0, double y0, double x1, double y1, double r)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(x1 - x0, y1 - y0) / 2 - 1e-6));
        if (r <= 1e-6) return Geo.Rect(x0, y0, x1, y1);
        var p = new PathD();
        p.AddRange(Geo.Arc(x1 - r, y0 + r, r, -Math.PI / 2, 0));
        p.AddRange(Geo.Arc(x1 - r, y1 - r, r, 0, Math.PI / 2));
        p.AddRange(Geo.Arc(x0 + r, y1 - r, r, Math.PI / 2, Math.PI));
        p.AddRange(Geo.Arc(x0 + r, y0 + r, r, Math.PI, Math.PI * 1.5));
        return p;
    }
}
