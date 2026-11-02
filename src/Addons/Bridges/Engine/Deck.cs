using Clipper2Lib;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// The line a deck follows, seen from the side (x along the bridge, y up): level, arched or sagging.
/// Gives points by distance along it, and bands that follow it (beams, ropes, rails).
/// </summary>
public sealed class DeckLine
{
    readonly Func<double, double> _f;
    readonly double[] _x, _s;

    public double X0 { get; }
    public double X1 { get; }
    /// <summary>Its length along the curve (mm).</summary>
    public double Length { get; }

    public DeckLine(Func<double, double> f, double x0, double x1)
    {
        _f = f;
        X0 = x0;
        X1 = x1;
        int n = Math.Clamp((int)Math.Ceiling((x1 - x0) / 0.05), 2, 40000);
        _x = new double[n + 1];
        _s = new double[n + 1];
        double px = x0, py = f(x0), s = 0;
        for (int i = 0; i <= n; i++)
        {
            double x = x0 + (x1 - x0) * i / n, y = f(x);
            s += Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            _x[i] = x;
            _s[i] = s;
            px = x;
            py = y;
        }
        Length = s;
    }

    /// <summary>A level line.</summary>
    public static DeckLine Level(double y, double x0, double x1) => new(_ => y, x0, x1);

    /// <summary>A circular arc through both ends at <paramref name="y"/>, <paramref name="rise"/> higher in the middle (lower if negative).</summary>
    public static DeckLine Arc(double y, double rise, double x0, double x1)
    {
        if (Math.Abs(rise) < 1e-6) return Level(y, x0, x1);
        double c = x1 - x0, xm = (x0 + x1) / 2, r = Math.Abs(rise), R = (c * c / 4 + r * r) / (2 * r), sign = Math.Sign(rise);
        return new(x =>
        {
            double d = Math.Min(Math.Abs(x - xm), R);
            return y + sign * (Math.Sqrt(R * R - d * d) - (R - r));
        }, x0, x1);
    }

    /// <summary>A parabola through both ends at <paramref name="y"/>, hanging <paramref name="sag"/> lower in the middle: a rope's curve.</summary>
    public static DeckLine Sag(double y, double sag, double x0, double x1)
    {
        double c = Math.Max(1e-6, x1 - x0);
        return new(x => y - sag * 4 * (x - x0) * (x1 - x) / (c * c), x0, x1);
    }

    public double Y(double x) => _f(x);

    /// <summary>The slope's angle at x (radians, anticlockwise from the x axis).</summary>
    public double Angle(double x)
    {
        const double h = 0.01;
        return Math.Atan2(_f(x + h) - _f(x - h), 2 * h);
    }

    /// <summary>Where along x a distance along the curve is.</summary>
    public double XAt(double s)
    {
        if (s <= 0) return X0 + s;
        if (s >= Length) return X1 + (s - Length);
        int i = Array.BinarySearch(_s, s);
        if (i >= 0) return _x[i];
        i = ~i;
        double t = (s - _s[i - 1]) / Math.Max(1e-12, _s[i] - _s[i - 1]);
        return _x[i - 1] + (_x[i] - _x[i - 1]) * t;
    }

    public Station At(double s)
    {
        double x = XAt(s);
        return new Station(s, new PointD(x, _f(x)), Angle(x));
    }

    /// <summary>The area between the curve moved up by <paramref name="lo"/> and by <paramref name="hi"/>, from x = xa to xb.</summary>
    public PathD Band(double lo, double hi, double xa, double xb)
    {
        int n = Math.Max(2, (int)Math.Ceiling((xb - xa) / 0.25));
        var p = new PathD(2 * n + 2);
        for (int i = 0; i <= n; i++) { double x = xa + (xb - xa) * i / n; p.Add(new PointD(x, _f(x) + lo)); }
        for (int i = n; i >= 0; i--) { double x = xa + (xb - xa) * i / n; p.Add(new PointD(x, _f(x) + hi)); }
        return p;
    }

    /// <summary>An upright strip from x = xa to xb, from the curve moved up by <paramref name="lo"/> to it moved up by <paramref name="hi"/> (a post, a leg, a rope).</summary>
    public PathD Upright(double xa, double xb, double lo, double hi) =>
        [new(xa, _f(xa) + lo), new(xb, _f(xb) + lo), new(xb, _f(xb) + hi), new(xa, _f(xa) + hi)];

    /// <summary>An upright strip from the table (y = <paramref name="floor"/>) up to the curve moved up by <paramref name="hi"/>.</summary>
    public PathD FromFloor(double xa, double xb, double floor, double hi) =>
        [new(xa, floor), new(xb, floor), new(xb, _f(xb) + hi), new(xa, _f(xa) + hi)];

    /// <summary>A line following the curve, <paramref name="off"/> above it, wavering a little (wood grain).</summary>
    public PathD Parallel(double off, double xa, double xb, Random rnd, double waver = 0.1)
    {
        double phase = rnd.NextDouble() * 10, wave = 6 + rnd.NextDouble() * 6;
        var p = new PathD();
        for (double x = xa; x <= xb + 1e-9; x += 0.5)
            p.Add(new PointD(x, _f(x) + off + waver * Math.Sin((x + phase) / wave * 2 * Math.PI)));
        return p;
    }
}

/// <summary>A point on the deck line, the distance along it and the slope there.</summary>
public readonly record struct Station(double S, PointD P, double Angle)
{
    public PointD Tangent => new(Math.Cos(Angle), Math.Sin(Angle));
    public PointD Normal => new(-Math.Sin(Angle), Math.Cos(Angle));
    /// <summary>The point <paramref name="along"/> the tangent and <paramref name="up"/> the normal from here.</summary>
    public PointD Off(double along, double up) => new(P.x + Tangent.x * along + Normal.x * up, P.y + Tangent.y * along + Normal.y * up);
    /// <summary>The same, in the space the bridge stands in, at depth z.</summary>
    public V3 Off3(double along, double up, double z) { var q = Off(along, up); return new(q.x, q.y, z); }
}

/// <summary>
/// Loose planks across notched sides (plank and rope bridges). Each plank sits in a notch in the top of
/// each side, as deep as the sheet so it's flush; where it crosses a side it's narrower (a neck), so the
/// wider plank either side stops it sliding across, and the planks hold the sides the right distance apart.
/// </summary>
public static class Planks
{
    /// <summary>How much narrower a plank is at its necks, each side (mm).</summary>
    public static double Keeper(double plank) => Math.Clamp(plank * 0.15, 0.4, 0.8);

    /// <summary>Plank middles evenly along the line between distances sa and sb, as many as fit.</summary>
    public static List<Station> Lay(DeckLine line, double sa, double sb, double plank, double gap)
    {
        double pitch = plank + gap, room = sb - sa;
        int n = Math.Max(0, (int)Math.Floor((room + gap) / pitch + 1e-9));
        double start = sa + (room - (n * pitch - gap)) / 2 + plank / 2;
        return [.. Enumerable.Range(0, n).Select(i => line.At(start + i * pitch))];
    }

    /// <summary>The notch a plank sits in, cut into the top of a side: <paramref name="width"/> along the deck, <paramref name="depth"/> deep, open above.</summary>
    public static PathD Notch(Station st, double width, double depth) =>
        [st.Off(-width / 2, -depth), st.Off(width / 2, -depth), st.Off(width / 2, 0.3), st.Off(-width / 2, 0.3)];

    /// <summary>The stations between neighbouring planks, where a post or rope can rise between the notches.</summary>
    public static List<Station> Teeth(DeckLine line, List<Station> planks) =>
        [.. planks.Zip(planks.Skip(1), (a, b) => line.At((a.S + b.S) / 2))];

    /// <summary>
    /// Which teeth get a post (or a rope) so there's one after every <paramref name="every"/> planks or so,
    /// the same from both ends, and at least two planks between posts so a plank can be passed through.
    /// </summary>
    public static List<int> PostTeeth(int planks, int every)
    {
        every = Math.Max(2, every);
        int bays = Math.Max(1, (int)Math.Round(planks / (double)every));
        while (bays > 1 && planks / bays < 2) bays--;
        var at = new SortedSet<int>();
        for (int j = 1; 2 * j <= bays; j++)
        {
            // An odd number of planks has no gap in the middle: the two middle bays become one.
            if (2 * j == bays && planks % 2 == 1) continue;
            int b = (int)Math.Round(j * planks / (double)bays);
            at.Add(b - 1);
            at.Add(planks - b - 1);
        }
        return [.. at.Where(t => t >= 0 && t < planks - 1)];
    }

    /// <summary>
    /// One plank flat (u across it, v along it from its front end): <paramref name="width"/> wide,
    /// with a neck at each side's place (<paramref name="necks"/>, along v) <paramref name="neckLen"/> long. Each end can be cut
    /// crooked (<paramref name="skew0"/>, <paramref name="skew1"/>: how much further one corner goes than the other).
    /// </summary>
    public static (PathsD Outline, PathsD Holes) Shape(double width, double length, double keeper, double[] necks, double neckLen, double skew0 = 0, double skew1 = 0)
    {
        double h = width / 2;
        var outline = new PathsD
        {
            new PathD
            {
                new(-h, Math.Max(0, skew0)), new(h, Math.Max(0, -skew0)),
                new(h, length - Math.Max(0, -skew1)), new(-h, length - Math.Max(0, skew1)),
            },
        };
        var holes = new PathsD();
        foreach (var v in necks)
        {
            holes.Add(Geo.Rect(-h - 1, v - neckLen / 2, -h + keeper, v + neckLen / 2));
            holes.Add(Geo.Rect(h - keeper, v - neckLen / 2, h + 1, v + neckLen / 2));
        }
        return (outline, holes);
    }

    /// <summary>
    /// Where a plank lies: on the tangent at the station, its top flush with the deck line, its front end at
    /// z = <paramref name="front"/> (u across the plank along the deck, v towards the back).
    /// </summary>
    public static Pose Lying(Station st, double t, double front)
    {
        var u = new V3(Math.Cos(st.Angle), Math.Sin(st.Angle), 0);
        var v = new V3(0, 0, -1);
        var n = u.Cross(v);
        return new Pose(new V3(st.P.x, st.P.y, front) - n * (t / 2), u, v);
    }

    /// <summary>Grain along a plank (v), a line every 0.9 mm across it, wavering a little.</summary>
    public static List<PathD> Grain(double width, double length, Random rnd)
    {
        var lines = new List<PathD>();
        int n = Math.Max(1, (int)Math.Floor(width / 0.9));
        double phase = rnd.NextDouble() * 10, wave = 5 + rnd.NextDouble() * 5;
        for (int i = 0; i < n; i++)
        {
            double u = -width / 2 + width * (i + 0.5) / n + (rnd.NextDouble() - 0.5) * 0.25;
            double v0 = rnd.NextDouble() < 0.3 ? length * rnd.NextDouble() * 0.3 : -1, v1 = rnd.NextDouble() < 0.3 ? length * (1 - rnd.NextDouble() * 0.3) : length + 1;
            var p = new PathD();
            for (double v = v0; v <= v1 + 1e-9; v += 0.5) p.Add(new PointD(u + 0.1 * Math.Sin((v + phase + i * 1.7) / wave * 2 * Math.PI), v));
            if (p.Count >= 2) lines.Add(p);
        }
        return lines;
    }

    /// <summary>Upright grain lines in a strip from x = xa to xb, y = y0 to y1 (posts and legs).</summary>
    public static List<PathD> UprightGrain(double xa, double xb, double y0, double y1, Random rnd)
    {
        var lines = new List<PathD>();
        double w = xb - xa;
        int n = Math.Max(1, (int)Math.Floor(w / 0.9));
        for (int i = 0; i < n; i++)
        {
            double x = xa + w * (i + 0.5) / n, phase = rnd.NextDouble() * 10;
            var p = new PathD();
            for (double y = y0; y <= y1 + 1e-9; y += 0.5) p.Add(new PointD(x + 0.08 * Math.Sin((y + phase) / 7 * 2 * Math.PI), y));
            if (p.Count >= 2) lines.Add(p);
        }
        return lines;
    }
}
