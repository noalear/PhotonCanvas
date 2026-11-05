using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// What stands on a shelf, drawn on the panel behind it as seen from the front: each thing's outline
/// scored, the space round them etched dark (so they stand out, like the books on a bookshelf's back).
/// </summary>
public static class Contents
{
    /// <summary>One row of things cut out to stand on a shelf: its outline (tabs and all), and what's etched and scored on its face.</summary>
    /// <param name="Tabs">Where its tabs are (u of each tab's middle) and how wide they are: the shelf has notches for them at its back edge.</param>
    public sealed record ShelfRow(PathsD Outline, PathsD Etched, List<PathD> Lines, double[] Tabs, double TabW);

    /// <summary>Whether <paramref name="what"/> can be cut out as a row to stand on a shelf (machines stay drawn on the back).</summary>
    public static bool Cuttable(ShelfContents what) => what is ShelfContents.Books or ShelfContents.Bottles or ShelfContents.Jars or ShelfContents.Plates or ShelfContents.Scrolls or ShelfContents.Stores;

    /// <summary>
    /// A row of books, bottles, jars, plates or scrolls cut out of the sheet, <paramref name="len"/> long and no
    /// taller than <paramref name="h"/>, standing (v = 0) on a shelf with its back to the cabinet's back: the
    /// things joined at their feet by a low strip, and two tabs (<paramref name="t"/> deep) down into notches in
    /// the shelf. Every bit is at least <see cref="Maker.Bridge"/> wide. Null when they won't fit (too small a
    /// shelf): the caller draws them on the back instead.
    /// </summary>
    public static ShelfRow? Row(ShelfContents what, double len, double h, double t, Random rnd)
    {
        if (!Cuttable(what) || len < 5 || h < 2.6) return null;
        var shapes = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        switch (what)
        {
            case ShelfContents.Books: BookRow(len, h, rnd, shapes, etched, lines); break;
            case ShelfContents.Bottles: BottleRow(len, h, rnd, shapes, etched, lines); break;
            case ShelfContents.Jars: JarRow(len, h, rnd, shapes, etched, lines); break;
            case ShelfContents.Plates: PlateRow(len, h, rnd, shapes, etched, lines); break;
            case ShelfContents.Scrolls: ScrollRow(len, h, rnd, shapes, etched, lines); break;
            case ShelfContents.Stores: StoreRow(len, h, rnd, shapes, etched, lines); break;
        }
        if (shapes.Count == 0) return null;
        var things = Geo.Solid(shapes);
        var b = Geo.Bounds(things);
        double x0 = Math.Max(0, b.left), x1 = Math.Min(len, b.right);
        if (x1 - x0 < 3) return null;
        // The strip along their feet that holds them together (hidden behind them, mostly).
        const double foot = 1.0;
        var outline = Geo.Solid([.. things, Geo.Rect(x0, 0, x1, foot)]);
        double tw = Geo.Clamp((x1 - x0) * 0.12, 1.5, 3);
        double[] tabs = x1 - x0 < 9 ? [(x0 + x1) / 2] : [x0 + (x1 - x0) * 0.25, x0 + (x1 - x0) * 0.75];
        outline = Geo.Solid([.. outline, .. tabs.Select(c => Geo.Rect(c - tw / 2, -t, c + tw / 2, 0.01))]);
        outline = Geo.Intersect(outline, [Geo.Rect(0, -t - 1, len, h)]);
        if (Geo.Pieces(outline) != 1 || Maker.Thin(outline) > 0.05) return null;
        return new ShelfRow(outline, etched, lines, tabs, tw);
    }

    /// <summary>
    /// Books standing on end, of different heights and thicknesses, their spines banded and some darker; now
    /// and then one leaning on the next, or a gap with a few books lying in a pile.
    /// </summary>
    static void BookRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double x = 0.2, prevH = 0;
        while (x < len - 1.2)
        {
            // A gap with a pile of books lying flat in it.
            if (prevH > 0 && rnd.NextDouble() < 0.1 && x + 4 < len - 0.5)
            {
                double pw = Math.Min(len - x - 0.4, 2.8 + rnd.NextDouble() * 1.4), py = 0;
                int n = 1 + rnd.Next(3);
                for (int k = 0; k < n && py + 1.0 <= h * 0.6; k++)
                {
                    double bw = pw - rnd.NextDouble() * 0.8, bx = x + (pw - bw) * rnd.NextDouble(), bt = 1.0 + rnd.NextDouble() * 0.3;
                    var lying = Geo.Rect(bx, py, bx + bw, py + bt);
                    shapes.Add(lying);
                    lines.Add(Geo.Loop(lying));
                    if (rnd.NextDouble() < 0.4) etched.Add(Geo.Rect(bx + 0.15, py + 0.15, bx + bw - 0.15, py + bt - 0.15));
                    py += bt;
                }
                x += pw + 0.2;
                prevH = 0;
                continue;
            }
            double w = 1.0 + rnd.NextDouble() * 0.8, bh = Math.Min(h, h * (0.62 + rnd.NextDouble() * 0.3));
            if (x + w > len - 0.2) break;
            // Now and then the last of a run leans on the one before it.
            if (prevH > 2 && rnd.NextDouble() < 0.09)
            {
                double a = 0.32 + rnd.NextDouble() * 0.12, L = Math.Min(bh, prevH / Math.Cos(a) - 0.1);
                double s = Math.Sin(a), c = Math.Cos(a);
                if (x - 0.1 + L * s + w * c < len - 0.2 && L * c > 1.5)
                {
                    var bl = new PointD(x - 0.1 + L * s, 0);
                    PointD At(double across, double along) => new(bl.x + across * c - along * s, bl.y + across * s + along * c);
                    var leaning = new PathD { At(0, 0), At(w, 0), At(w, L), At(0, L) };
                    shapes.Add(leaning);
                    lines.Add(Geo.Loop(leaning));
                    if (L > 2.5) lines.Add([At(0, L * 0.82), At(w, L * 0.82)]);
                    x = bl.x + w * c + 1.2 + rnd.NextDouble();
                    prevH = 0;
                    continue;
                }
            }
            var book = Geo.Rect(x, 0, x + w, bh);
            shapes.Add(book);
            lines.Add(Geo.Loop(book));
            // Bands across the spine near its ends, and on some a dark spine or a label.
            if (bh > 2.4)
            {
                lines.Add([new(x, bh * 0.85), new(x + w, bh * 0.85)]);
                lines.Add([new(x, bh * 0.15), new(x + w, bh * 0.15)]);
            }
            double pick = rnd.NextDouble();
            if (pick < 0.3) etched.Add(Geo.Rect(x + 0.12, bh * 0.16 + 0.1, x + w - 0.12, bh * 0.84 - 0.1));
            else if (pick < 0.55 && bh > 3) lines.Add(Geo.Loop(Geo.Rect(x + w * 0.22, bh * 0.45, x + w * 0.78, bh * 0.62)));
            x += w;
            prevH = bh;
        }
    }

    /// <summary>A storeroom's shelf: sacks tied at the neck, little iron-cornered boxes, pots with lids and small barrels.</summary>
    static void StoreRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double x = 0.3;
        int last = -1;
        while (x < len - 2.4)
        {
            int kind = rnd.Next(4);
            if (kind == last) kind = (kind + 1) % 4;
            last = kind;
            double top = Math.Min(h, h * (0.7 + rnd.NextDouble() * 0.25)), bw;
            switch (kind)
            {
                case 0:
                {
                    // A sack: a fat bag narrowing to a tied neck with a tuft over it.
                    bw = Geo.Clamp(top * 0.85, 2.6, 4.2);
                    if (x + bw > len - 0.3) return;
                    double cx = x + bw / 2, neck = top * 0.72, nw = Math.Max(1.1, bw * 0.3);
                    var shape = Geo.Solid([[new(x + 0.1, 0), new(x + bw - 0.1, 0), new(x + bw, neck * 0.45), new(cx + nw / 2, neck), new(cx + nw / 2 + 0.35, top), new(cx - nw / 2 - 0.35, top), new(cx - nw / 2, neck), new(x, neck * 0.45)]]);
                    shapes.AddRange(shape);
                    lines.AddRange(shape.Select(Geo.Loop));
                    lines.Add([new(cx - nw / 2 - 0.05, neck + 0.25), new(cx + nw / 2 + 0.05, neck + 0.25)]);
                    lines.Add([new(cx - bw * 0.2, neck * 0.2), new(cx - bw * 0.05, neck * 0.7)]);
                    break;
                }
                case 1:
                {
                    // A little box: planks, iron corners, a lid line.
                    bw = Geo.Clamp(top * 0.95, 2.6, 4.4);
                    double bh = Math.Min(top, bw * 0.8);
                    if (x + bw > len - 0.3) return;
                    var box = Geo.Rect(x, 0, x + bw, bh);
                    shapes.Add(box);
                    lines.Add(Geo.Loop(box));
                    lines.Add([new(x, bh * 0.7), new(x + bw, bh * 0.7)]);
                    lines.Add([new(x, bh * 0.35), new(x + bw, bh * 0.35)]);
                    foreach (var (cx, cy) in new[] { (x, 0.0), (x + bw, 0.0), (x, bh), (x + bw, bh) })
                        etched.Add(Geo.Intersect([Geo.Rect(cx - 0.6, cy - 0.6, cx + 0.6, cy + 0.6)], [box]).FirstOrDefault() ?? []);
                    etched.Add(Geo.Rect(x + bw / 2 - 0.25, bh * 0.45, x + bw / 2 + 0.25, bh * 0.68));
                    break;
                }
                case 2:
                {
                    // A pot: a round belly, a short neck and a knobbed lid.
                    bw = Geo.Clamp(top * 0.8, 2.4, 3.8);
                    if (x + bw > len - 0.3) return;
                    double cx = x + bw / 2, r = bw / 2, cy = r * 0.85, mw = bw * 0.55, ph = Math.Min(top, cy + r + 1.2);
                    var belly = Geo.Intersect([Geo.Circle(cx, cy, r, 0.01)], [Geo.Rect(x - 1, 0, x + bw + 1, cy + r + 1)]);
                    var shape = Geo.Solid([.. belly, Geo.Rect(cx - mw / 2, cy, cx + mw / 2, ph - 0.6), Geo.Rect(cx - mw / 2 - 0.25, ph - 0.7, cx + mw / 2 + 0.25, ph - 0.3), Geo.Circle(cx, ph - 0.3, 0.5, 0.01)]);
                    shapes.AddRange(shape);
                    lines.AddRange(shape.Select(Geo.Loop));
                    etched.AddRange(Fronts.Outline([Rugs.Ellipse(cx, cy, r * 0.75, r * 0.3)], 0.3));
                    break;
                }
                default:
                {
                    // A small barrel: bulging staves and hoops.
                    bw = Geo.Clamp(top * 0.75, 2.4, 3.6);
                    double bh = Math.Min(top, bw * 1.25);
                    if (x + bw > len - 0.3) return;
                    var barrel = new PathD();
                    for (int i = 0; i <= 10; i++) { double t = i / 10.0; barrel.Add(new(x + bw * 0.08 * (1 - Math.Sin(Math.PI * t)), bh * t)); }
                    for (int i = 10; i >= 0; i--) { double t = i / 10.0; barrel.Add(new(x + bw - bw * 0.08 * (1 - Math.Sin(Math.PI * t)), bh * t)); }
                    shapes.Add(barrel);
                    lines.Add(Geo.Loop(barrel));
                    foreach (var f in new[] { 0.2, 0.8 }) etched.Add(Geo.Rect(x + 0.1, bh * f - 0.25, x + bw - 0.1, bh * f + 0.25));
                    for (double s = x + bw * 0.33; s < x + bw - 0.4; s += bw * 0.33) lines.Add([new(s, 0.1), new(s, bh - 0.1)]);
                    break;
                }
            }
            x += bw + 0.3 + rnd.NextDouble() * 0.4;
        }
    }

    /// <summary>Bottles with shoulders and corks, round flasks with long necks and squat jugs, in clusters.</summary>
    static void BottleRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double x = 0.3;
        while (x < len - 1.8)
        {
            int kind = rnd.Next(5);
            double bw, top = Math.Min(h, h * (0.62 + rnd.NextDouble() * 0.3));
            if (kind <= 2)
            {
                // A bottle: body, rounded shoulders, a neck and a cork.
                bw = 1.7 + rnd.NextDouble() * 0.5;
                if (x + bw > len - 0.3) break;
                double cx = x + bw / 2, nw = 1.0, body = top * 0.58, sh = top * 0.14;
                var shape = new PathD { new(x, 0), new(x + bw, 0), new(x + bw, body) };
                for (int i = 1; i < 6; i++) { double a = Math.PI / 2 * i / 6; shape.Add(new(cx + nw / 2 + (bw - nw) / 2 * Math.Cos(a), body + sh * Math.Sin(a))); }
                shape.AddRange([new(cx + nw / 2, top), new(cx - nw / 2, top)]);
                for (int i = 5; i >= 1; i--) { double a = Math.PI / 2 * i / 6; shape.Add(new(cx - nw / 2 - (bw - nw) / 2 * Math.Cos(a), body + sh * Math.Sin(a))); }
                shape.Add(new(x, body));
                shapes.Add(shape);
                lines.Add(Geo.Loop(shape));
                // The cork and a label.
                lines.Add([new(cx - nw / 2, top - 0.5), new(cx + nw / 2, top - 0.5)]);
                if (body > 1.8) etched.Add(Geo.Rect(x + 0.25, body * 0.28, x + bw - 0.25, body * 0.7));
            }
            else if (kind == 3)
            {
                // A round flask: a ball and a long neck.
                double r = Geo.Clamp(top * 0.3, 0.9, 1.5);
                bw = 2 * r;
                if (x + bw > len - 0.3) break;
                double cx = x + r, neckTop = Math.Min(h, 2 * r + Math.Max(1.2, r * 1.3));
                // The ball sits on a flat cut across its bottom.
                double cy = r * 0.8;
                neckTop = Math.Min(h, cy + r + Math.Max(1.2, r * 1.3));
                var ball = Geo.Intersect([Geo.Circle(cx, cy, r, 0.01)], [Geo.Rect(cx - r - 1, 0, cx + r + 1, cy + r + 1)]);
                var neck = Geo.Rect(cx - 0.5, cy, cx + 0.5, neckTop);
                var shape = Geo.Solid([.. ball, neck]);
                shapes.AddRange(shape);
                lines.AddRange(shape.Select(Geo.Loop));
                lines.Add([new(cx - 0.5, neckTop - 0.45), new(cx + 0.5, neckTop - 0.45)]);
                etched.Add(Geo.Circle(cx, cy, r * 0.55, 0.01));
            }
            else
            {
                // A jug: a wide belly, a narrower mouth with a lip.
                double jh = Math.Min(h, Geo.Clamp(top * 0.75, 2.2, 4));
                bw = 2.4 + rnd.NextDouble() * 0.6;
                if (x + bw > len - 0.3) break;
                double mw = bw * 0.62, cx = x + bw / 2;
                var shape = new PathD
                {
                    new(x + bw * 0.15, 0), new(x + bw * 0.85, 0), new(x + bw, jh * 0.45), new(cx + mw / 2, jh * 0.8),
                    new(cx + mw / 2, jh), new(cx - mw / 2, jh), new(cx - mw / 2, jh * 0.8), new(x, jh * 0.45),
                };
                shapes.Add(shape);
                lines.Add(Geo.Loop(shape));
                lines.Add([new(x + bw * 0.08, jh * 0.3), new(x + bw * 0.92, jh * 0.3)]);
            }
            // Touching the next one, or a gap wide enough to cut.
            x += bw + (rnd.NextDouble() < 0.5 ? -0.05 : 1.0 + rnd.NextDouble() * 0.8);
        }
    }

    /// <summary>Jars and pots with lids and knobs, some stacked two high, and squat crocks.</summary>
    static void JarRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double x = 0.3;
        while (x < len - 2.2)
        {
            double jw = 2.0 + rnd.NextDouble() * 1.2;
            if (x + jw > len - 0.3) break;
            double y = 0;
            int stack = h > 6.5 && rnd.NextDouble() < 0.3 ? 2 : 1;
            for (int k = 0; k < stack; k++)
            {
                double w = k == 0 ? jw : jw * (0.75 + rnd.NextDouble() * 0.15), x0 = x + (jw - w) / 2;
                double jh = Geo.Clamp((stack == 1 ? h * (0.45 + rnd.NextDouble() * 0.25) : h * 0.4) - (k == 0 ? 0 : 0.2), 1.8, h - y);
                if (y + jh > h + 1e-6 || jh < 1.8) break;
                // A body with chamfered foot and shoulders up to its lid, and a knob on a big enough jar.
                bool knobbed = jh >= 3 && w >= 2.4 && k == stack - 1;
                double knob = knobbed ? 1.0 : 0, lid = Math.Min(0.6, jh * 0.18), body = jh - lid - knob, c = Math.Min(0.3, w * 0.1), sh = Math.Min(0.45, body * 0.2);
                var shapeList = new PathsD
                {
                    new PathD
                    {
                        new(x0 + c, y), new(x0 + w - c, y), new(x0 + w, y + c), new(x0 + w, y + body - sh), new(x0 + w * 0.9, y + body),
                        new(x0 + w * 0.9, y + body + lid), new(x0 + w * 0.1, y + body + lid), new(x0 + w * 0.1, y + body), new(x0, y + body - sh), new(x0, y + c),
                    },
                };
                if (knobbed) shapeList.Add(Geo.Rect(x0 + w / 2 - 0.6, y + body + lid - 0.05, x0 + w / 2 + 0.6, y + jh));
                var shape = Geo.Solid(shapeList);
                shapes.AddRange(shape);
                lines.AddRange(shape.Select(Geo.Loop));
                lines.Add([new(x0 + w * 0.1, y + body), new(x0 + w * 0.9, y + body)]);
                if (body > 1.6) etched.Add(Geo.Rect(x0 + w * 0.2, y + body * 0.3, x0 + w * 0.8, y + body * 0.65));
                y += jh;
            }
            x += jw + (rnd.NextDouble() < 0.45 ? -0.05 : 1.0 + rnd.NextDouble() * 0.6);
        }
    }

    /// <summary>Plates stood on edge, overlapping, their rims scored, with a jug or a tankard here and there.</summary>
    static void PlateRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double r = Math.Min(h * 0.45, 3.2);
        if (r < 1.2) { JarRow(len, h, rnd, shapes, etched, lines); return; }
        double x = 0.3 + r;
        while (x + r < len - 0.3)
        {
            if (rnd.NextDouble() < 0.18 && x + 2.2 < len - 0.3)
            {
                // A tankard between the plates: a body and a handle.
                double tw = 1.8, th = Math.Min(h, Geo.Clamp(r * 1.2, 2, 3.4)), x0 = x - r * 0.4;
                var mug = Geo.Solid([Geo.Rect(x0, 0, x0 + tw, th), WoodFace.RoundRect(x0 + tw - 0.05, th * 0.25, x0 + tw + 1.1, th * 0.8, 0.4)]);
                shapes.AddRange(mug);
                lines.Add(Geo.Loop(Geo.Rect(x0, 0, x0 + tw, th)));
                lines.Add([new(x0, th * 0.82), new(x0 + tw, th * 0.82)]);
                x = x0 + tw + 1.1 + r * 0.7;
                continue;
            }
            double rr = r * (0.85 + rnd.NextDouble() * 0.15);
            var plate = Geo.Circle(x, rr, rr, 0.01);
            shapes.Add(plate);
            lines.Add(Geo.Loop(plate));
            if (rr > 1.4) lines.Add(Geo.Loop(Geo.Circle(x, rr, rr * 0.62, 0.01)));
            if (rr > 2.2 && rnd.NextDouble() < 0.5) etched.AddRange(Geo.Subtract([Geo.Circle(x, rr, rr * 0.9, 0.01)], [Geo.Circle(x, rr, rr * 0.72, 0.01)]));
            x += rr * 1.45;
        }
    }

    /// <summary>Rolled scrolls piled end-on in little pyramids, their ends spiralled.</summary>
    static void ScrollRow(double len, double h, Random rnd, PathsD shapes, PathsD etched, List<PathD> lines)
    {
        double r = Geo.Clamp(h * 0.16, 0.9, 1.3), d = r * 1.65, rowH = r * 1.45;
        double x = 0.3;
        while (x < len - 2 * r - 0.3)
        {
            int rows = Geo.Clamp((int)Math.Floor((h - 2 * r) / rowH) + 1, 1, 3);
            int across = Math.Min(rows + rnd.Next(2), (int)Math.Floor((len - 0.3 - x - 2 * r) / d) + 1);
            if (across < 1) break;
            rows = Math.Min(rows, across);
            for (int row = 0; row < rows; row++)
            {
                int n = across - row;
                for (int k = 0; k < n; k++)
                {
                    double cx = x + r + row * d / 2 + k * d, cy = r + row * rowH;
                    var end = Geo.Circle(cx, cy, r, 0.01);
                    shapes.Add(end);
                    lines.Add(Geo.Loop(end));
                    if (r > 0.8) lines.Add(Spiral(cx, cy, r * 0.75));
                    if (rnd.NextDouble() < 0.3) etched.Add(Geo.Circle(cx, cy, r * 0.25, 0.01));
                }
            }
            x += (across - 1) * d + 2 * r + 1.2 + rnd.NextDouble();
        }
    }

    /// <summary>Tools hung on pegs along a rack: hammers, a saw, pliers, chisels, a mallet, a square, dark against the board.</summary>
    public static void Tools(double x0, double y0, double x1, double y1, Random rnd, PathsD etched, List<PathD> lines)
    {
        double h = y1 - y0, slot = Math.Clamp(h * 0.42, 2.6, 4.2);
        int n = (int)Math.Floor((x1 - x0) / slot);
        if (n < 1 || h < 3) return;
        double gap = (x1 - x0 - n * slot) / n;
        for (int i = 0; i < n; i++)
        {
            double a = x0 + i * (slot + gap) + gap / 2, cx = a + slot / 2, top = y1 - 0.2;
            lines.Add(WoodFace.Dot(cx, y1 + 0.5, 0.25));
            switch (i % 6)
            {
                case 0: // a hammer: head across the top, handle down
                    etched.Add(Geo.Rect(cx - slot * 0.38, top - 1.0, cx + slot * 0.38, top));
                    etched.Add(Geo.Rect(cx - 0.3, y0 + h * 0.15, cx + 0.3, top - 0.9));
                    break;
                case 1: // a saw: a tapering blade under a handle with a hole in it
                    etched.Add(new PathD { new(cx - slot * 0.4, top - 1.6), new(cx + slot * 0.4, top - 1.6), new(cx + slot * 0.4, y0 + h * 0.1), new(cx - slot * 0.1, y0 + h * 0.1) });
                    lines.Add(Geo.Loop(WoodFace.RoundRect(cx - slot * 0.3, top - 1.5, cx + slot * 0.3, top, 0.4)));
                    for (double y = y0 + h * 0.14; y < top - 1.8; y += 0.5) lines.Add([new(cx - slot * 0.1 - (y - y0 - h * 0.1) / (top - 1.6 - y0 - h * 0.1) * slot * 0.3 - 0.1, y), new(cx - slot * 0.1 - (y - y0 - h * 0.1) / (top - 1.6 - y0 - h * 0.1) * slot * 0.3 + 0.25, y)]);
                    break;
                case 2: // pliers: two handles crossing at a pivot, jaws up top
                    lines.Add([new(cx - 0.25, top), new(cx + 0.15, top - h * 0.3), new(cx - slot * 0.3, y0 + h * 0.12)]);
                    lines.Add([new(cx + 0.25, top), new(cx - 0.15, top - h * 0.3), new(cx + slot * 0.3, y0 + h * 0.12)]);
                    lines.Add(WoodFace.Dot(cx, top - h * 0.3, 0.3));
                    break;
                case 3: // two chisels
                    foreach (var dx in new[] { -slot * 0.2, slot * 0.2 })
                    {
                        etched.Add(Geo.Rect(cx + dx - 0.35, top - h * 0.45, cx + dx + 0.35, top));
                        lines.Add(Geo.Loop(new PathD { new(cx + dx - 0.3, top - h * 0.45), new(cx + dx + 0.3, top - h * 0.45), new(cx + dx + 0.2, y0 + h * 0.15), new(cx + dx - 0.2, y0 + h * 0.15) }));
                    }
                    break;
                case 4: // a mallet: a fat head and a handle
                    etched.Add(WoodFace.RoundRect(cx - slot * 0.32, top - 1.8, cx + slot * 0.32, top, 0.4));
                    lines.Add(Geo.Loop(Geo.Rect(cx - 0.3, y0 + h * 0.15, cx + 0.3, top - 1.8)));
                    break;
                default: // a square: an L
                    etched.Add(new PathD { new(cx - slot * 0.35, top), new(cx - slot * 0.35 + 0.7, top), new(cx - slot * 0.35 + 0.7, y0 + h * 0.2 + 0.6), new(cx + slot * 0.35, y0 + h * 0.2 + 0.6), new(cx + slot * 0.35, y0 + h * 0.2), new(cx - slot * 0.35, y0 + h * 0.2) });
                    break;
            }
        }
    }

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
            case ShelfContents.Stores: Bottles(x0, x1, y0, y1, rnd, etched, lines, jars: true); break;
            case ShelfContents.Tools:
                // Tools hung on pegs on the back.
                Tools(x0 + 0.3, y0 + 0.4, x1 - 0.3, y1 - 1.0, rnd, etched, lines);
                break;
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
