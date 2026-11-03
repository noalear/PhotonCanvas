using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>Outlines shared by several kinds: a panel whose top is shaped (a seat's back, a bed's head board, a screen).</summary>
public static class Shapes
{
    /// <summary>
    /// The rectangle x0..x1, y0..y1 with its top shaped as <paramref name="top"/>: the shape takes up to
    /// <paramref name="rise"/> mm at the top (the sides are straight below that).
    /// </summary>
    public static PathD Topped(BackTop top, double x0, double y0, double x1, double y1, double rise)
    {
        double w = x1 - x0;
        rise = Geo.Clamp(rise, 0, Math.Max(0, y1 - y0 - 0.5));
        if (top == BackTop.Square || rise < 0.3 || w < 1) return Geo.Rect(x0, y0, x1, y1);
        double s = y1 - rise;
        var p = new PathD { new(x0, y0), new(x1, y0), new(x1, s) };
        switch (top)
        {
            case BackTop.Rounded:
            {
                double r = Math.Min(rise, w / 2 - 0.01);
                p.Add(new(x1, y1 - r));
                p.AddRange(Geo.Arc(x1 - r, y1 - r, r, 0, Math.PI / 2, 0.02).Skip(1));
                p.AddRange(Geo.Arc(x0 + r, y1 - r, r, Math.PI / 2, Math.PI, 0.02));
                break;
            }
            case BackTop.Arched:
            {
                // A segment of a circle through both top corners and the middle, rise above them.
                double half = w / 2, r = (half * half + rise * rise) / (2 * rise), cy = y1 - r, a0 = Math.Asin(Geo.Clamp(half / r, -1, 1));
                p.AddRange(Geo.Arc(x0 + half, cy, r, Math.PI / 2 - a0, Math.PI / 2 + a0, 0.02));
                break;
            }
            case BackTop.Pointed:
            {
                // Two arcs meeting in a point, like a gothic arch squashed to the rise.
                const int n = 14;
                for (int i = 1; i <= n; i++)
                {
                    double a = Math.PI / 3 * i / n;
                    p.Add(new(x0 + w * Math.Cos(a), s + rise * Math.Sin(a) / Math.Sin(Math.PI / 3)));
                }
                for (int i = 1; i < n; i++)
                {
                    double a = Math.PI * 2 / 3 + Math.PI / 3 * i / n;
                    p.Add(new(x1 + w * Math.Cos(a), s + rise * Math.Sin(a) / Math.Sin(Math.PI / 3)));
                }
                break;
            }
            case BackTop.Winged:
            {
                // Posts at the corners with ball tops, a dip and a crest in the middle.
                double post = Geo.Clamp(w * 0.12, 1.0, 3), r = post / 2, dip = rise * 0.55;
                p.Add(new(x1, y1 - r));
                p.AddRange(Geo.Arc(x1 - r, y1 - r, r, 0, Math.PI, 0.02).Skip(1));
                p.Add(new(x1 - post, y1 - dip));
                // The crest: a curve up to a point in the middle.
                double cx = x0 + w / 2;
                for (int i = 1; i < 10; i++) { double t = i / 10.0; p.Add(new(x1 - post - (x1 - post - cx) * t, y1 - dip + (dip * 0.9) * t * t)); }
                p.Add(new(cx, y1));
                for (int i = 9; i >= 1; i--) { double t = i / 10.0; p.Add(new(x0 + post + (cx - x0 - post) * t, y1 - dip + (dip * 0.9) * t * t)); }
                p.Add(new(x0 + post, y1 - dip));
                p.AddRange(Geo.Arc(x0 + r, y1 - r, r, 0, Math.PI, 0.02));
                break;
            }
        }
        p.Add(new(x0, s));
        return Geo.Tidy([p]).OrderByDescending(q => Math.Abs(Clipper.Area(q))).FirstOrDefault() ?? Geo.Rect(x0, y0, x1, y1);
    }

    /// <summary>Upholstery: a cushion's edge scored round the area, buttons in a diamond grid (tufted).</summary>
    public static void Tufted(double x0, double y0, double x1, double y1, bool buttons, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 2 || h < 2) return;
        double i = Math.Min(0.5, Math.Min(w, h) * 0.1);
        lines.Add(Geo.Loop(WoodFace.RoundRect(x0 + i, y0 + i, x1 - i, y1 - i, Math.Min(1.2, Math.Min(w, h) * 0.2))));
        if (!buttons || w < 4 || h < 4) return;
        double step = Geo.Clamp(Math.Min(w, h) / 3, 1.8, 3);
        int row = 0;
        for (double y = y0 + step * 0.8; y < y1 - step * 0.5; y += step * 0.75, row++)
            for (double x = x0 + step * (row % 2 == 0 ? 0.8 : 1.3); x < x1 - step * 0.5; x += step)
                etched.Add(Geo.Circle(x, y, 0.16, 0.01));
    }
}
