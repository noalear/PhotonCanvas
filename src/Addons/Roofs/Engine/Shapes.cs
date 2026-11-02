using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>Small drawing helpers on top of the wall maker's <see cref="Geo"/> (mm, y up).</summary>
public static class Shapes
{
    /// <summary>The part of the areas where a·x + b·y ≤ c.</summary>
    public static PathsD Below(PathsD area, double a, double b, double c)
    {
        if (area.Count == 0) return area;
        var box = Geo.Bounds(area);
        double big = 10 * (Math.Abs(box.right) + Math.Abs(box.left) + Math.Abs(box.top) + Math.Abs(box.bottom) + 10);
        double len = Math.Sqrt(a * a + b * b);
        if (len < 1e-12) return c >= 0 ? area : [];
        double nx = a / len, ny = b / len, d = c / len;
        // A huge square on the allowed side of the line n·p = d.
        var p0 = new PointD(nx * d, ny * d);
        var t = new PointD(-ny, nx);
        var half = new PathD
        {
            new(p0.x + t.x * big, p0.y + t.y * big),
            new(p0.x - t.x * big, p0.y - t.y * big),
            new(p0.x - t.x * big - nx * big, p0.y - t.y * big - ny * big),
            new(p0.x + t.x * big - nx * big, p0.y + t.y * big - ny * big),
        };
        return Geo.Intersect(area, [half]);
    }

    /// <summary>A ring: outer outline less a circle of <paramref name="inner"/> radius (none when 0).</summary>
    public static PathsD Ring(PathD outer, double inner) => inner > 0 ? Geo.Subtract([outer], [Geo.Circle(0, 0, inner)]) : [outer];

    /// <summary>A polygon of points round the middle: <paramref name="radius"/> gives the distance at each angle (radians).</summary>
    public static PathD Polar(Func<double, double> radius, int steps)
    {
        var p = new PathD(steps);
        for (int i = 0; i < steps; i++)
        {
            double a = 2 * Math.PI * i / steps, r = radius(a);
            p.Add(new PointD(r * Math.Cos(a), r * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>Points round the middle for a circle of radius r drawn to about <paramref name="tol"/> mm.</summary>
    public static int Steps(double r, double tol = 0.02) => Geo.Segments(r, tol);

    /// <summary>A sector (wedge) from the middle out past <paramref name="r"/>, between two angles (degrees).</summary>
    public static PathD Wedge(double fromDeg, double toDeg, double r)
    {
        var p = new PathD { new(0, 0) };
        int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(toDeg - fromDeg) / 3));
        for (int i = 0; i <= n; i++)
        {
            double a = (fromDeg + (toDeg - fromDeg) * i / n) * Math.PI / 180;
            p.Add(new PointD(r * Math.Cos(a), r * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>A strip <paramref name="width"/> wide straight out from the middle at an angle (degrees), from <paramref name="r0"/> to <paramref name="r1"/>.</summary>
    public static PathD Radial(double deg, double r0, double r1, double width) =>
        Geo.Turn([Geo.Rect(r0, -width / 2, r1, width / 2)], deg)[0];

    /// <summary>A number in little straight strokes (a seven-segment style), its bottom-left at x, y, to be scored.</summary>
    public static List<PathD> Number(int n, double x, double y, double h)
    {
        // Segments: a top, b top right, c bottom right, d bottom, e bottom left, f top left, g middle.
        string[] digits = ["abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abfgcd"];
        double w = h * 0.55, gap = h * 0.3;
        var lines = new List<PathD>();
        foreach (char ch in n.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            if (ch is < '0' or > '9') continue;
            foreach (char seg in digits[ch - '0'])
            {
                (double x0, double y0, double x1, double y1) = seg switch
                {
                    'a' => (0, h, w, h),
                    'b' => (w, h, w, h / 2),
                    'c' => (w, h / 2, w, 0),
                    'd' => (0, 0, w, 0),
                    'e' => (0, 0, 0, h / 2),
                    'f' => (0, h / 2, 0, h),
                    _ => (0, h / 2, w, h / 2),
                };
                lines.Add([new(x + x0, y + y0), new(x + x1, y + y1)]);
            }
            x += w + gap;
        }
        return lines;
    }

    public static double NumberWidth(int n, double h) => n.ToString(System.Globalization.CultureInfo.InvariantCulture).Length * h * 0.85 - h * 0.3;

    /// <summary>Bounds of the areas as (x0, y0, x1, y1).</summary>
    public static (double X0, double Y0, double X1, double Y1) Box(PathsD a)
    {
        var b = Geo.Bounds(a);
        return (b.left, b.top, b.right, b.bottom);
    }
}
