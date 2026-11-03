using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// What stands on a shelf, drawn on the panel behind it as seen from the front: each thing's outline
/// scored, the space round them etched dark (so they stand out, like the books on a bookshelf's back).
/// </summary>
public static class Contents
{
    /// <summary>Draws <paramref name="what"/> in the box between x0..x1 and y0..y1 (the shelf at y0).</summary>
    public static void Fill(ShelfContents what, double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        if (y1 - y0 < 2 || x1 - x0 < 2) return;
        switch (what)
        {
            case ShelfContents.Books: Books(x0, x1, y0, y1, rnd, etched, lines); break;
            case ShelfContents.Bottles: Bottles(x0, x1, y0, y1, rnd, etched, lines, jars: false); break;
            case ShelfContents.Jars: Bottles(x0, x1, y0, y1, rnd, etched, lines, jars: true); break;
            case ShelfContents.Plates: Plates(x0, x1, y0, y1, rnd, etched, lines); break;
            case ShelfContents.Scrolls: Scrolls(x0, x1, y0, y1, rnd, etched, lines); break;
            case ShelfContents.Tech: Tech(x0, x1, y0, y1, rnd, etched, lines); break;
        }
    }

    /// <summary>Book spines standing on a shelf: scored outlines and bands, the space over them etched dark.</summary>
    public static void Books(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0;
        if (h < 2 || x1 - x0 < 2) return;
        var space = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        var books = new PathsD();
        double x = x0 + 0.3;
        while (x < x1 - 0.5)
        {
            if (rnd.NextDouble() < 0.08) { x += 0.6 + rnd.NextDouble() * 1.5; continue; } // a gap on the shelf
            double w = 0.7 + rnd.NextDouble() * 0.9, bh = h * (0.55 + rnd.NextDouble() * 0.33);
            if (x + w > x1 - 0.2) break;
            // Now and then a book leaning on the one before it.
            if (rnd.NextDouble() < 0.07 && x + bh * 0.45 + w < x1 - 0.2 && bh > 2)
            {
                double lean = 0.4;
                var leaning = Geo.Turn([Geo.Rect(0, 0, w, bh)], -lean * 180 / Math.PI, x + 0.05, y0);
                var p = Geo.Move(leaning, Math.Sin(lean) * bh, 0);
                var shelf = new PathsD { Geo.Rect(x0, y0, x1, y1) };
                var cut = Geo.Intersect(p, shelf);
                books.AddRange(cut);
                lines.AddRange(cut.Select(Geo.Loop));
                x += Math.Sin(lean) * bh + w / Math.Cos(lean) + 0.2;
                continue;
            }
            var b = Geo.Rect(x, y0, x + w, y0 + bh);
            books.Add(b);
            lines.Add(Geo.Loop(b));
            if (bh > 2.5 && w > 0.9)
            {
                lines.Add([new(x, y0 + bh * 0.82), new(x + w, y0 + bh * 0.82)]);
                lines.Add([new(x, y0 + bh * 0.18), new(x + w, y0 + bh * 0.18)]);
            }
            x += w;
        }
        etched.AddRange(Geo.Subtract(space, Geo.Offset(Geo.Union(books), 0.1)));
    }

    /// <summary>Bottles (tall, a neck and a cork) or jars (squat, a lid), in a row, now and then a gap.</summary>
    static void Bottles(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines, bool jars)
    {
        double h = y1 - y0;
        var space = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        var all = new PathsD();
        double x = x0 + 0.4;
        while (x < x1 - 0.8)
        {
            if (rnd.NextDouble() < 0.1) { x += 0.8 + rnd.NextDouble(); continue; }
            double w = jars ? 1.4 + rnd.NextDouble() * 1.0 : 0.9 + rnd.NextDouble() * 0.6;
            double bh = Math.Min(h - 0.4, jars ? h * (0.4 + rnd.NextDouble() * 0.2) : h * (0.6 + rnd.NextDouble() * 0.3));
            if (x + w > x1 - 0.3 || bh < 1.2) break;
            PathD shape;
            if (jars)
            {
                // A body, a narrower lid on top.
                double lid = Math.Min(0.5, bh * 0.2);
                var body = WoodFace.RoundRect(x, y0, x + w, y0 + bh - lid, Math.Min(0.4, w * 0.25));
                var top = Geo.Rect(x + w * 0.12, y0 + bh - lid - 0.05, x + w * 0.88, y0 + bh);
                shape = Geo.Union([body, top]).OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();
                lines.Add([new(x + w * 0.12, y0 + bh - lid), new(x + w * 0.88, y0 + bh - lid)]);
                if (bh > 2.5) lines.Add(Geo.Loop(Geo.Rect(x + w * 0.2, y0 + bh * 0.3, x + w * 0.8, y0 + bh * 0.55)));
            }
            else
            {
                // A body with shoulders, a neck and a cork.
                double neckW = Math.Max(0.35, w * 0.36), body = bh * 0.62, shoulder = bh * 0.12;
                double cx = x + w / 2;
                shape =
                [
                    new(x, y0), new(x + w, y0), new(x + w, y0 + body),
                    new(cx + neckW / 2, y0 + body + shoulder), new(cx + neckW / 2, y0 + bh),
                    new(cx - neckW / 2, y0 + bh), new(cx - neckW / 2, y0 + body + shoulder), new(x, y0 + body),
                ];
                lines.Add([new(cx - neckW / 2, y0 + bh - 0.35), new(cx + neckW / 2, y0 + bh - 0.35)]);
                if (body > 1.6) lines.Add(Geo.Loop(Geo.Rect(x + 0.15, y0 + body * 0.25, x + w - 0.15, y0 + body * 0.65)));
            }
            all.Add(shape);
            lines.Add(Geo.Loop(shape));
            x += w + 0.15 + rnd.NextDouble() * 0.3;
        }
        etched.AddRange(Geo.Subtract(space, Geo.Offset(Geo.Union(all), 0.1)));
    }

    /// <summary>Plates stood on edge on the shelf against a rail, overlapping, with a jug or bowl here and there.</summary>
    static void Plates(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0;
        double r = Math.Min(h * 0.42, 3.2);
        var space = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        var all = new PathsD();
        double x = x0 + r + 0.3;
        // The rail that keeps them from sliding off, across the front.
        double rail = y0 + Math.Min(0.9, h * 0.15);
        while (x + r < x1 - 0.2)
        {
            double rr = r * (0.85 + rnd.NextDouble() * 0.15);
            var plate = Geo.Circle(x, y0 + rr + 0.1, rr, 0.01);
            all.Add(plate);
            lines.Add(Geo.Loop(plate));
            if (rr > 1.2) lines.Add(Geo.Loop(Geo.Circle(x, y0 + rr + 0.1, rr * 0.62, 0.01)));
            x += rr * 1.55;
        }
        var u = Geo.Union(all);
        etched.AddRange(Geo.Subtract(space, Geo.Offset(u, 0.1)));
        lines.Add([new(x0, rail), new(x1, rail)]);
    }

    /// <summary>Pigeonholes, most with a rolled scroll end-on.</summary>
    static void Scrolls(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0, w = x1 - x0;
        double cell = Geo.Clamp(Math.Min(h / 2, 2.6), 1.4, 2.6);
        int nx = Math.Max(1, (int)Math.Floor(w / cell)), ny = Math.Max(1, (int)Math.Floor(h / cell));
        double cw = w / nx, ch = h / ny, wall = Math.Min(0.35, cw * 0.15);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double cx0 = x0 + i * cw + wall / 2, cy0 = y0 + j * ch + wall / 2, cx1 = cx0 + cw - wall, cy1 = cy0 + ch - wall;
                var hole = new PathsD { Geo.Rect(cx0, cy0, cx1, cy1) };
                if (rnd.NextDouble() < 0.8)
                {
                    double r = Math.Min(cx1 - cx0, cy1 - cy0) * (0.3 + rnd.NextDouble() * 0.12);
                    double sx = (cx0 + cx1) / 2 + (rnd.NextDouble() - 0.5) * 0.3, sy = cy0 + r + 0.05;
                    var end = Geo.Circle(sx, sy, r, 0.01);
                    hole = Geo.Subtract(hole, [end]);
                    lines.Add(Geo.Loop(end));
                    if (r > 0.45) lines.Add(Spiral(sx, sy, r * 0.8));
                }
                etched.AddRange(hole);
            }
    }

    static PathD Spiral(double cx, double cy, double r)
    {
        var p = new PathD();
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0, a = t * Math.PI * 4, rr = r * t;
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>Machines on a rack: blinking lights, vents and small screens.</summary>
    static void Tech(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0;
        double unit = Geo.Clamp(h / Math.Max(1, Math.Round(h / 2.2)), 1.6, 3);
        for (double y = y0; y + unit <= y1 + 1e-6; y += unit)
        {
            var box = Geo.Rect(x0 + 0.3, y + 0.15, x1 - 0.3, y + unit - 0.15);
            lines.Add(Geo.Loop(box));
            Unit(x0 + 0.3, x1 - 0.3, y + 0.15, y + unit - 0.15, rnd, etched, lines);
        }
    }

    /// <summary>The face of one machine: lights, a vent, a screen, or a pair of tape reels.</summary>
    public static void Unit(double x0, double x1, double y0, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0, w = x1 - x0, cy = (y0 + y1) / 2;
        if (h < 0.8 || w < 2) return;
        int kind = rnd.Next(4);
        if (kind == 3 && h >= 2.6 && w >= 6)
        {
            // Two tape reels and a little row of lights.
            double r = Math.Min(h * 0.38, w * 0.16);
            foreach (var cx in new[] { x0 + w * 0.25, x0 + w * 0.6 })
            {
                lines.Add(Geo.Loop(Geo.Circle(cx, cy, r, 0.01)));
                lines.Add(Geo.Loop(Geo.Circle(cx, cy, r * 0.3, 0.01)));
                etched.AddRange(Geo.Subtract([Geo.Circle(cx, cy, r * 0.75, 0.01)], [Geo.Circle(cx, cy, r * 0.42, 0.01)]));
            }
            for (double x = x0 + w * 0.82; x < x1 - 0.3; x += 0.7) etched.Add(Geo.Circle(x, cy, 0.18, 0.01));
            return;
        }
        if (kind == 2 && w >= 4)
        {
            // A small screen with lines of text.
            double sx1 = x0 + Math.Min(w * 0.45, 6);
            var screen = Geo.Rect(x0 + 0.3, y0 + 0.25, sx1, y1 - 0.25);
            etched.Add(screen);
            for (double x = sx1 + 0.6; x < x1 - 0.3; x += 0.75) lines.Add(WoodFace.Dot(x, cy, 0.17));
            return;
        }
        if (kind == 1)
        {
            // Vent slots.
            for (double x = x0 + 0.5; x < x1 - 0.6; x += 0.7) etched.Add(Geo.Rect(x, y0 + h * 0.25, x + 0.3, y1 - h * 0.25));
            return;
        }
        // Lights, some lit (etched), some not (rings).
        for (double x = x0 + 0.5; x < x1 - 0.3; x += 0.65)
            if (rnd.NextDouble() < 0.45) etched.Add(Geo.Circle(x, cy, 0.18, 0.01));
            else lines.Add(WoodFace.Dot(x, cy, 0.17));
    }
}
