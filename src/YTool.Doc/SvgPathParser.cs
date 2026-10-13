using System.Collections.Immutable;
using System.Globalization;
using YTool.Core;

namespace YTool.Doc;

/// <summary>
/// SVG path-data parser. Quadratics become exact cubics, elliptical arcs
/// become cubics of at most 90° each. Malformed data stops parsing at the
/// error and keeps what was read so far, as browsers do.
/// </summary>
static class SvgPathParser
{
    public static PathGeometry Parse(string d)
    {
        var subs = new List<Subpath>();
        var segs = ImmutableArray.CreateBuilder<Seg>();
        Vec2 start = default, cur = default, lastCtrl = default;
        char prevCmd = ' ';
        bool open = false;
        int i = 0;

        void Flush(bool closed)
        {
            if (open) subs.Add(new Subpath(start, segs.ToImmutable(), closed));
            segs.Clear();
            open = false;
        }

        void EnsureOpen()
        {
            if (!open)
            {
                start = cur;
                open = true;
            }
        }

        try
        {
            char cmd = ' ';
            while (true)
            {
                SkipSep(d, ref i);
                if (i >= d.Length) break;
                char c = d[i];
                if (char.IsAsciiLetter(c))
                {
                    cmd = c;
                    i++;
                }
                else if (cmd == ' ') break; // data before any command
                bool rel = char.IsAsciiLetterLower(cmd);
                Vec2 Pt(ref int k) { var x = Num(d, ref k); var y = Num(d, ref k); return rel ? cur + new Vec2(x, y) : new Vec2(x, y); }

                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        Flush(false);
                        cur = Pt(ref i);
                        start = cur;
                        open = true;
                        // Further pairs after M are implicit L.
                        cmd = rel ? 'l' : 'L';
                        prevCmd = 'M';
                        continue;
                    case 'L':
                        EnsureOpen();
                        cur = Pt(ref i);
                        segs.Add(Seg.Line(cur));
                        break;
                    case 'H':
                    {
                        EnsureOpen();
                        double x = Num(d, ref i);
                        cur = new(rel ? cur.X + x : x, cur.Y);
                        segs.Add(Seg.Line(cur));
                        break;
                    }
                    case 'V':
                    {
                        EnsureOpen();
                        double y = Num(d, ref i);
                        cur = new(cur.X, rel ? cur.Y + y : y);
                        segs.Add(Seg.Line(cur));
                        break;
                    }
                    case 'C':
                    {
                        EnsureOpen();
                        var c1 = Pt(ref i); var c2 = Pt(ref i); var to = Pt(ref i);
                        segs.Add(Seg.Cubic(c1, c2, to));
                        lastCtrl = c2;
                        cur = to;
                        break;
                    }
                    case 'S':
                    {
                        EnsureOpen();
                        var c1 = "CcSs".Contains(prevCmd) ? cur * 2 - lastCtrl : cur;
                        var c2 = Pt(ref i); var to = Pt(ref i);
                        segs.Add(Seg.Cubic(c1, c2, to));
                        lastCtrl = c2;
                        cur = to;
                        break;
                    }
                    case 'Q':
                    {
                        EnsureOpen();
                        var q = Pt(ref i); var to = Pt(ref i);
                        segs.Add(QuadToCubic(cur, q, to));
                        lastCtrl = q;
                        cur = to;
                        break;
                    }
                    case 'T':
                    {
                        EnsureOpen();
                        var q = "QqTt".Contains(prevCmd) ? cur * 2 - lastCtrl : cur;
                        var to = Pt(ref i);
                        segs.Add(QuadToCubic(cur, q, to));
                        lastCtrl = q;
                        cur = to;
                        break;
                    }
                    case 'A':
                    {
                        EnsureOpen();
                        double rx = Num(d, ref i), ry = Num(d, ref i), rot = Num(d, ref i);
                        bool large = Flag(d, ref i), sweep = Flag(d, ref i);
                        var to = Pt(ref i);
                        ArcToCubics(cur, rx, ry, rot, large, sweep, to, segs);
                        cur = to;
                        break;
                    }
                    case 'Z':
                        if (open)
                        {
                            if (cur.Dist(start) > Polyline.SamePointMm) segs.Add(Seg.Line(start));
                            Flush(true);
                        }
                        cur = start;
                        prevCmd = 'Z';
                        cmd = ' '; // Z takes no arguments; numbers after it are an error
                        continue;
                    default:
                        throw new FormatException($"unknown path command '{cmd}'");
                }
                prevCmd = cmd;
            }
        }
        catch (FormatException)
        {
            // Keep everything parsed before the error.
        }
        Flush(false);
        return new PathGeometry(subs.Where(s => !s.Segs.IsEmpty));
    }

    static Seg QuadToCubic(Vec2 p0, Vec2 q, Vec2 p3) =>
        Seg.Cubic(p0 + (q - p0) * (2.0 / 3), p3 + (q - p3) * (2.0 / 3), p3);

    static void SkipSep(string d, ref int i)
    {
        while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
    }

    static bool Flag(string d, ref int i)
    {
        SkipSep(d, ref i);
        if (i < d.Length && d[i] is '0' or '1') return d[i++] == '1';
        throw new FormatException("arc flag expected");
    }

    internal static double Num(string d, ref int i)
    {
        SkipSep(d, ref i);
        int s = i;
        if (i < d.Length && d[i] is '+' or '-') i++;
        bool digits = false, dot = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsAsciiDigit(c)) digits = true;
            else if (c == '.' && !dot) dot = true;
            else break;
            i++;
        }
        if (digits && i < d.Length && d[i] is 'e' or 'E')
        {
            int save = i++;
            if (i < d.Length && d[i] is '+' or '-') i++;
            if (i < d.Length && char.IsAsciiDigit(d[i]))
                while (i < d.Length && char.IsAsciiDigit(d[i])) i++;
            else i = save;
        }
        if (!digits) throw new FormatException("number expected");
        return double.Parse(d.AsSpan(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>SVG endpoint arc → cubic Béziers (SVG spec F.6).</summary>
    internal static void ArcToCubics(Vec2 p0, double rx, double ry, double rotDeg, bool large, bool sweep, Vec2 p1,
        ImmutableArray<Seg>.Builder o)
    {
        if (p0.Dist(p1) < 1e-12) return;
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-12 || ry < 1e-12)
        {
            o.Add(Seg.Line(p1));
            return;
        }
        double phi = rotDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (p0.X - p1.X) / 2, dy = (p0.Y - p1.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }
        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
        double cx = cos * cxp - sin * cyp + (p0.X + p1.X) / 2;
        double cy = sin * cxp + cos * cyp + (p0.Y + p1.Y) / 2;

        static double Angle(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }
        double th1 = Angle(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
        double dth = Angle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
        if (!sweep && dth > 0) dth -= 2 * Math.PI;
        else if (sweep && dth < 0) dth += 2 * Math.PI;

        int n = (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2) - 1e-9);
        n = Math.Max(1, n);
        double step = dth / n;
        double k = 4.0 / 3 * Math.Tan(step / 4);
        Vec2 Map(double ex, double ey) => new(cx + rx * ex * cos - ry * ey * sin, cy + rx * ex * sin + ry * ey * cos);
        double t = th1;
        for (int s = 0; s < n; s++)
        {
            double c1 = Math.Cos(t), s1 = Math.Sin(t), c2 = Math.Cos(t + step), s2 = Math.Sin(t + step);
            var a = Map(c1 - k * s1, s1 + k * c1);
            var b = Map(c2 + k * s2, s2 - k * c2);
            var e = s == n - 1 ? p1 : Map(c2, s2);
            o.Add(Seg.Cubic(a, b, e));
            t += step;
        }
    }
}
