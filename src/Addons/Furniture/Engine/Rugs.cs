using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Flat pieces: rugs (lying on the floor, the pattern etched and scored on top) and things hung on a
/// wall (standing against it, the back on the wall, glued): tapestries, banners, paintings, shields,
/// maps and screens.
/// </summary>
public static class Rugs
{
    // ---- rugs ----

    public static void Rug(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm;
        if (W < 6 || D < 4) { m.Bad("The rug is too small: make it at least 6 by 4 mm."); return; }
        var etched = new PathsD();
        var lines = new List<PathD>();
        PathsD outline;
        var rnd = m.Rnd;
        switch (it.Rug)
        {
            case RugPattern.Round:
            {
                double cx = W / 2, cy = D / 2, rx = W / 2, ry = D / 2;
                outline = [Ellipse(cx, cy, rx, ry)];
                PointD On(double a, double f) => new(cx + Math.Cos(a) * rx * f, cy + Math.Sin(a) * ry * f);
                // A fringe of short stitches round the edge.
                int ticks = (int)(Math.PI * (rx + ry) / 0.9);
                for (int i = 0; i < ticks; i++) { double a = 2 * Math.PI * i / ticks; lines.Add([On(a, 0.985), On(a, 0.92)]); }
                // A border band between two etched rings, a scrolling vine running round inside it.
                etched.AddRange(Fronts.Outline([Ellipse(cx, cy, rx * 0.9, ry * 0.9)], 0.4));
                etched.AddRange(Fronts.Outline([Ellipse(cx, cy, rx * 0.7, ry * 0.7)], 0.4));
                int scrolls = Math.Max(8, (int)(Math.PI * (rx + ry) * 0.8 / 4.5)) / 2 * 2;
                var vine = new PathD();
                for (int i = 0; i <= 160; i++) { double a = 2 * Math.PI * i / 160; vine.Add(On(a, 0.8 + 0.045 * Math.Sin(a * scrolls))); }
                lines.Add(vine);
                for (int i = 0; i < scrolls; i++)
                {
                    // A curl off the vine at each crest, turning in towards the vine.
                    double a = 2 * Math.PI * (i + 0.25) / scrolls, f = i % 2 == 0 ? 0.845 : 0.755, cr = Math.Min(rx, ry) * 0.045;
                    var c = On(a + Math.PI / scrolls * 0.5, f);
                    if (cr > 0.25) lines.Add([.. Geo.Arc(c.x, c.y, cr, a, a + Math.PI * 1.5, 0.02)]);
                }
                // The field: the theme's symbol in the middle, a lozenge either side and small ones round it.
                double sr = Math.Min(rx, ry) * 0.3;
                if (sr > 0.9) Cabinets.Symbol(Fronts.Look(m), m.Theme, cx, cy, sr, etched, lines);
                foreach (var a in new[] { 0, Math.PI })
                {
                    var p = On(a, 0.5);
                    double lw = Math.Min(rx, ry) * 0.08;
                    if (lw > 0.4) etched.Add(new PathD { new(p.x - lw * 1.6, p.y), new(p.x, p.y + lw), new(p.x + lw * 1.6, p.y), new(p.x, p.y - lw) });
                }
                for (int i = 0; i < 8; i++)
                {
                    if (i % 4 == 0) continue;
                    var p = On(i * Math.PI / 4, 0.55);
                    lines.Add(WoodFace.Dot(p.x, p.y, Math.Min(rx, ry) * 0.03 + 0.15));
                }
                etched.AddRange(Fronts.Outline([Ellipse(cx, cy, rx * 0.62, ry * 0.62)], 0.3));
                break;
            }
            case RugPattern.Hide:
                outline = [Hide(W / 2, D / 2, W / 2, D / 2, rnd)];
                // Fur: short strokes all over, and the spine.
                lines.Add([new(W * 0.12, D / 2), new(W * 0.88, D / 2)]);
                for (int i = 0; i < (int)(W * D / 6); i++)
                {
                    double x = W * (0.1 + 0.8 * rnd.NextDouble()), y = D * (0.15 + 0.7 * rnd.NextDouble()), a = (y < D / 2 ? -1 : 1) * (0.4 + rnd.NextDouble() * 0.5);
                    lines.Add([new(x, y), new(x + Math.Cos(a) * 0.7, y + Math.Sin(a) * 0.7)]);
                }
                lines = Geo.ClipLines(lines, Geo.Offset(outline, -0.3));
                break;
            case RugPattern.Mat:
            {
                outline = [Fronts.Chamfered(0, 0, W, D, Math.Min(1.5, Math.Min(W, D) * 0.15))];
                // Hazard stripes along the ends, a grating in the middle.
                double band = Math.Min(2.5, W * 0.15);
                foreach (var (x0, x1) in new[] { (0.0, band), (W - band, W) })
                {
                    var stripes = new PathsD();
                    for (double s = -D; s < W + D; s += 1.6) stripes.Add(Geo.Strip(new PointD(s, 0), new PointD(s + D, D), 0.8));
                    etched.AddRange(Geo.Intersect(Geo.Union(stripes), [Geo.Rect(x0, 0, x1, D)]));
                }
                double gx0 = band + 0.8, gx1 = W - band - 0.8;
                for (double x = gx0; x <= gx1 + 1e-6; x += 1.2) lines.Add([new(x, 0.8), new(x, D - 0.8)]);
                lines.Add(Geo.Loop(Geo.Rect(gx0, 0.8, gx1, D - 0.8)));
                break;
            }
            case RugPattern.Kilim:
            {
                outline = [Geo.Rect(0, 0, W, D)];
                etched.AddRange(Fronts.Outline(0.8, 0.8, W - 0.8, D - 0.8, 0.5));
                Fringe(W, D, lines);
                // Bands across of stepped diamonds and zigzags.
                int bands = Math.Max(1, (int)Math.Round((W - 2) / 5));
                double bw = (W - 2) / bands;
                for (int b = 0; b < bands; b++)
                {
                    double cx = 1 + bw * (b + 0.5);
                    if (b % 2 == 0)
                        for (double y = 1.8; y < D - 1.8; y += bw * 0.9)
                            etched.Add(Stepped(cx, Math.Min(y + bw * 0.45, D - 1.8 - bw * 0.0), Math.Min(bw * 0.38, (D - 3) / 2)));
                    else
                    {
                        var zig = new PathD();
                        for (double y = 1.5; y <= D - 1.5; y += 1.2) zig.Add(new PointD(cx + ((int)((y - 1.5) / 1.2) % 2 == 0 ? -bw * 0.25 : bw * 0.25), y));
                        if (zig.Count > 1) etched.AddRange(Thick(zig, 0.45));
                    }
                }
                break;
            }
            case RugPattern.Damask:
            {
                outline = [Geo.Rect(0, 0, W, D)];
                etched.AddRange(Fronts.Outline(0.9, 0.9, W - 0.9, D - 0.9, 0.6));
                lines.Add(Geo.Loop(Geo.Rect(1.8, 1.8, W - 1.8, D - 1.8)));
                Fringe(W, D, lines);
                // An ogee lattice with a lily in each cell, inside the border.
                Pictures.Damask(2.2, 2.2, W - 2.2, D - 2.2, Geo.Clamp(Math.Min(W, D) / 4, 3.2, 5.5), etched, lines);
                break;
            }
            default:
            {
                // A bordered rug with a medallion.
                outline = [Geo.Rect(0, 0, W, D)];
                double b = Geo.Clamp(Math.Min(W, D) * 0.12, 0.8, 2.2);
                etched.AddRange(Fronts.Outline(b * 0.5, b * 0.5, W - b * 0.5, D - b * 0.5, 0.45));
                etched.AddRange(Fronts.Outline(b * 1.4, b * 1.4, W - b * 1.4, D - b * 1.4, 0.45));
                // Little diamonds round the border.
                for (double x = b * 1.5; x < W - b; x += b * 1.2) foreach (var y in new[] { b * 0.95, D - b * 0.95 }) lines.Add(Geo.Loop(Diamond(x, y, b * 0.3, b * 0.3)));
                Fringe(W, D, lines);
                double mw = (W - 3 * b) * 0.32, md = (D - 3 * b) * 0.42;
                if (mw > 1 && md > 1)
                {
                    etched.Add(Diamond(W / 2, D / 2, mw, md));
                    etched = Geo.Subtract(Geo.Union(etched), [Diamond(W / 2, D / 2, mw * 0.65, md * 0.65)]);
                    lines.Add(Geo.Loop(Diamond(W / 2, D / 2, mw * 0.35, md * 0.35)));
                    foreach (var (x, y) in new[] { (b * 2.6, b * 2.6), (W - b * 2.6, b * 2.6), (b * 2.6, D - b * 2.6), (W - b * 2.6, D - b * 2.6) })
                        if (W > 12 && D > 8) lines.Add(Geo.Loop(Diamond(x, y, b * 0.7, b * 0.7)));
                }
                break;
            }
        }
        m.Add("Rug", outline, [], [Pose.Up(0, m.T / 2, D)], grainAlongU: true, face: [], etched: etched, lines: lines);
        m.Note(m.T > 1.6 ? "A rug looks best cut from thin card or veneer (set the sheet to 0.5 to 1 mm)." : "Glue the rug to the floor, or leave it loose.");
    }

    static void Fringe(double W, double D, List<PathD> lines)
    {
        foreach (var x in new[] { 0.0, W - 0.6 })
            for (double y = 0.5; y < D - 0.3; y += 0.45) lines.Add([new(x, y), new(x + 0.6, y)]);
    }

    public static PathD Ellipse(double cx, double cy, double rx, double ry)
    {
        var c = Geo.Circle(0, 0, 1, 0.002);
        return [.. c.Select(p => new PointD(cx + p.x * rx, cy + p.y * ry))];
    }

    static PathD Diamond(double cx, double cy, double rx, double ry) => [new(cx, cy - ry), new(cx + rx, cy), new(cx, cy + ry), new(cx - rx, cy)];

    /// <summary>A stepped (kilim) diamond.</summary>
    static PathD Stepped(double cx, double cy, double r)
    {
        var p = new PathD();
        int n = 3;
        double s = r / n;
        // Up the right side in steps, then round.
        for (int q = 0; q < 4; q++)
        {
            for (int i = 0; i < n; i++)
            {
                double ax = (n - i) * s, ay = i * s, bx = (n - i) * s, by = (i + 1) * s;
                (double x, double y) Rot(double x, double y) => q switch { 0 => (x, y), 1 => (-y, x), 2 => (-x, -y), _ => (y, -x) };
                var (x1, y1) = Rot(ax, ay);
                var (x2, y2) = Rot(bx, by);
                var (x3, y3) = Rot(bx - s, by);
                p.Add(new PointD(cx + x1, cy + y1));
                p.Add(new PointD(cx + x2, cy + y2));
                if (i < n - 1) p.Add(new PointD(cx + x3, cy + y3));
            }
        }
        return Geo.Tidy([p]).OrderByDescending(q => Math.Abs(Clipper.Area(q))).FirstOrDefault() ?? Diamond(cx, cy, r, r);
    }

    static PathsD Thick(PathD line, double w)
    {
        var all = new PathsD();
        for (int i = 0; i + 1 < line.Count; i++) all.Add(Geo.Strip(line[i], line[i + 1], w));
        return Geo.Union(all);
    }

    /// <summary>An animal hide: an oval body with four legs and a neck, the edges wobbly.</summary>
    public static PathD Hide(double cx, double cy, double rx, double ry, Random rnd)
    {
        var p = new PathD();
        const int n = 64;
        double ph = rnd.NextDouble() * 6;
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            // Legs at the four diagonals, the neck and tail along the long axis.
            double r = 0.72;
            foreach (var la in new[] { Math.PI / 4, 3 * Math.PI / 4, 5 * Math.PI / 4, 7 * Math.PI / 4 })
                r += 0.3 * Math.Exp(-Math.Pow(Angle(a - la) / 0.13, 2));
            r += 0.2 * Math.Exp(-Math.Pow(Angle(a) / 0.18, 2)) + 0.12 * Math.Exp(-Math.Pow(Angle(a - Math.PI) / 0.12, 2));
            r -= 0.12 * Math.Exp(-Math.Pow(Angle(a - Math.PI / 2) / 0.4, 2)) + 0.12 * Math.Exp(-Math.Pow(Angle(a + Math.PI / 2) / 0.4, 2));
            r *= 1 + 0.04 * Math.Sin(a * 7 + ph);
            p.Add(new PointD(cx + Math.Cos(a) * rx * r, cy + Math.Sin(a) * ry * r));
        }
        return p;
    }

    static double Angle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    // ---- wall hangings ----

    public static void Hanging(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, H = it.HeightMm, y0 = Math.Max(0, it.HangMm);
        if (W < 4 || H < 4) { m.Bad("Make it at least 4 by 4 mm."); return; }
        var look = Fronts.Look(m);
        var rnd = m.Rnd;
        var etched = new PathsD();
        var lines = new List<PathD>();
        PathsD outline;
        bool grain = false;
        switch (it.Hanging)
        {
            case HangingStyle.Banner:
            {
                // A pole across the top, the cloth hanging from it, a swallowtail (or a point) at the bottom.
                double pole = Math.Min(1.2, H * 0.08), cw = W * 0.8, x0 = (W - cw) / 2, tail = Math.Min(cw * 0.5, H * 0.2);
                var cloth = new PathD { new(x0, tail), new(x0, H - pole), new(x0 + cw, H - pole), new(x0 + cw, tail) };
                if (look == FrontLook.Gothic || look == FrontLook.Metal) cloth.Add(new PointD(x0 + cw / 2, 0));
                else { cloth.Add(new PointD(x0 + cw, 0)); cloth.Add(new PointD(x0 + cw / 2, tail)); cloth.Add(new PointD(x0, 0)); }
                outline = Geo.Union([cloth, Geo.Rect(0, H - pole, W, H), Geo.Circle(0.6, H - pole / 2, 0.75), Geo.Circle(W - 0.6, H - pole / 2, 0.75)]);
                etched.AddRange(Fronts.Outline([Geo.Rect(x0 + 0.7, tail + 0.4, x0 + cw - 0.7, H - pole - 0.7)], 0.35));
                double r = Math.Min(cw * 0.3, (H - tail - pole) * 0.22);
                Cabinets.Symbol(look, m.Theme, W / 2, tail + (H - tail - pole) * 0.55, r, etched, lines);
                break;
            }
            case HangingStyle.Painting:
            {
                // A carved frame; a portrait in a tall one, a landscape in a wide one.
                outline = [Geo.Rect(0, 0, W, H)];
                double fr = Geo.Clamp(Math.Min(W, H) * 0.12, 0.8, 2.4);
                Pictures.Frame(0, 0, W, H, fr, etched, lines);
                if (H > W * 1.1) Pictures.Portrait(fr, fr, W - fr, H - fr, etched, lines);
                else Pictures.Landscape(fr, fr, W - fr, H - fr, rnd, etched, lines);
                grain = false;
                break;
            }
            case HangingStyle.Shield:
            {
                // A shield with two swords crossed behind it.
                double sw = Math.Min(W * 0.62, H * 0.75), sh = sw * 1.2, cx = W / 2, cy = H * 0.55;
                var shield = look == FrontLook.Plank && m.Theme is "Dungeon" or "Cave" ? Geo.Circle(cx, cy, sw / 2) : Heater(cx - sw / 2, cy - sh / 2, sw, sh);
                var swords = new PathsD();
                foreach (var s in new[] { -1, 1 })
                {
                    var blade = Sword(H * 0.95, 1.1);
                    swords.AddRange(Geo.Turn(blade, s * 35, cx, cy - H * 0.42));
                }
                outline = Geo.Union(Geo.Union(swords), [shield]);
                etched.AddRange(Fronts.Outline([shield], 0.5));
                double r = sw * 0.24;
                if (look == FrontLook.Plank) { lines.Add(WoodFace.Dot(cx, cy, r * 0.8)); etched.Add(Geo.Circle(cx, cy, r * 0.45, 0.01)); }
                else Cabinets.Symbol(look, m.Theme, cx, cy, r, etched, lines);
                break;
            }
            case HangingStyle.Map:
            {
                // A parchment, rolled at both ends, with a coast, a road and a compass rose.
                double roll = Math.Min(1.4, H * 0.1);
                outline = Geo.Union([Geo.Rect(roll * 0.5, 0, W - roll * 0.5, H), WoodFace.RoundRect(0, 0, roll * 1.6, H, roll * 0.7), WoodFace.RoundRect(W - roll * 1.6, 0, W, H, roll * 0.7)]);
                lines.Add([new(roll * 1.6, 0.3), new(roll * 1.6, H - 0.3)]);
                lines.Add([new(W - roll * 1.6, 0.3), new(W - roll * 1.6, H - 0.3)]);
                double mx0 = roll * 1.6 + 0.4, mx1 = W - roll * 1.6 - 0.4;
                lines.Add(Geo.Loop(Geo.Rect(mx0, 0.5, mx1, H - 0.5)));
                if (look == FrontLook.Metal) Pictures.StarMap(mx0, 0.5, mx1, H - 0.5, rnd, etched, lines);
                else Pictures.Map(mx0, 0.5, mx1, H - 0.5, rnd, etched, lines);
                grain = false;
                break;
            }
            case HangingStyle.Monitor:
            {
                outline = [Fronts.Chamfered(0, 0, W, H, Math.Min(1.2, Math.Min(W, H) * 0.12))];
                double b = Math.Min(1.0, Math.Min(W, H) * 0.1);
                var screen = Fronts.Chamfered(b, b * 1.6, W - b, H - b, Math.Min(0.8, Math.Min(W, H) * 0.08));
                etched.Add(screen);
                // What's on it, left unetched: a graph and lines of text.
                var shown = new PathsD();
                var graph = new PathD();
                for (int i = 0; i <= 10; i++) graph.Add(new PointD(b + 1 + (W * 0.55 - b - 1) * i / 10, b * 1.6 + 1 + (H - b * 2.6 - 2) * (0.3 + 0.5 * rnd.NextDouble())));
                shown.AddRange(Thick(graph, 0.3));
                for (double y = H - b - 1.2; y > b * 1.6 + 1; y -= 1.0) shown.Add(Geo.Rect(W * 0.62, y - 0.18, W * (0.7 + 0.22 * rnd.NextDouble()) - b, y + 0.18));
                etched = Geo.Subtract(etched, shown);
                for (double x = W * 0.4; x < W * 0.6; x += 0.7) lines.Add(WoodFace.Dot(x, b * 0.8, 0.15));
                break;
            }
            default:
            {
                // A tapestry on a rod: a border, a field of diamonds and a figure in the middle; a fringe at the bottom.
                double rod = Math.Min(1.0, H * 0.07), x0 = Math.Min(1.0, W * 0.08);
                // Knobs on the rod's ends.
                double kr = look != FrontLook.Metal && W >= 8 ? Math.Max(0.7, rod * 0.75) : 0;
                // A cloth hung by loops from the rod with a dagged hem, on a wooden one wide enough; else a fringe.
                bool dagged = look != FrontLook.Metal && W - 2 * x0 >= 8 && H >= 14;
                double loops = dagged ? Math.Min(1.4, H * 0.07) : 0, top = H - rod - loops, dd = dagged ? Math.Min(1.8, H * 0.08) : 0;
                var cloth = new PathD { new(x0, top), new(x0, dd) };
                if (dagged)
                {
                    int n = Math.Max(3, (int)Math.Round((W - 2 * x0) / 2.6));
                    for (int i = 0; i < n; i++)
                    {
                        double a0 = x0 + (W - 2 * x0) * i / n, a1 = x0 + (W - 2 * x0) * (i + 1) / n, mid = (a0 + a1) / 2;
                        cloth.AddRange([new(mid - 0.5, 0), new(mid + 0.5, 0), new(a1, dd)]);
                    }
                }
                else cloth.Add(new PointD(W - x0, 0));
                cloth.Add(new PointD(W - x0, top));
                var parts = new PathsD { cloth, kr > 0 ? Geo.Rect(kr, H - rod, W - kr, H) : Geo.Rect(0, H - rod, W, H) };
                if (kr > 0) parts.AddRange([Geo.Circle(kr, H - kr, kr, 0.02), Geo.Circle(W - kr, H - kr, kr, 0.02)]);
                if (dagged)
                {
                    int nl = Math.Max(3, (int)Math.Round((W - 2 * x0) / 3.2));
                    for (int i = 0; i < nl; i++)
                    {
                        double lx = x0 + 0.65 + (W - 2 * x0 - 1.3) * i / (nl - 1);
                        parts.Add(Geo.Rect(lx - 0.65, top - 0.01, lx + 0.65, H - rod + 0.01));
                        lines.Add([new(lx - 0.65, H - rod - 0.3), new(lx + 0.65, H - rod - 0.3)]);
                    }
                }
                outline = Geo.Solid(parts);
                double b = Geo.Clamp(Math.Min(W, H) * 0.1, 0.8, 2);
                etched.AddRange(Fronts.Outline(x0 + b * 0.6, dd + b * 1.2, W - x0 - b * 0.6, top - b * 0.6, 0.45));
                if (dagged) lines.Add([new(x0 + 0.2, dd + 0.5), new(W - x0 - 0.2, dd + 0.5)]);
                else for (double x = x0 + 0.4; x < W - x0 - 0.3; x += 0.45) lines.Add([new(x, 0), new(x, b * 0.7)]);
                double fx0 = x0 + b * 1.3, fx1 = W - x0 - b * 1.3, fy0 = dd + b * 1.9, fy1 = top - b * 1.3;
                double r = Math.Min(fx1 - fx0, fy1 - fy0) * 0.3;
                // A heraldic field: lilies left light on a dark ground, round the shield; diamonds on a metal one.
                bool seme = look != FrontLook.Metal && fx1 - fx0 >= 8 && fy1 - fy0 >= 8;
                if (seme)
                    Pictures.Seme(fx0, fy0, fx1, fy1, Geo.Clamp((fx1 - fx0) / 4, 2.8, 4), r > 1 ? [Heater(W / 2 - r, (fy0 + fy1) / 2 - r * 1.1, r * 2, r * 2.3)] : [], etched);
                else
                    for (double s = -(fy1 - fy0); s < fx1 - fx0; s += 2.4)
                    {
                        lines.AddRange(Geo.ClipLines([[new(fx0 + s, fy0), new(fx0 + s + (fy1 - fy0), fy1)]], [Geo.Rect(fx0, fy0, fx1, fy1)]));
                        lines.AddRange(Geo.ClipLines([[new(fx1 - s, fy0), new(fx1 - s - (fy1 - fy0), fy1)]], [Geo.Rect(fx0, fy0, fx1, fy1)]));
                    }
                if (r > 1)
                {
                    var plate = Heater(W / 2 - r, (fy0 + fy1) / 2 - r * 1.1, r * 2, r * 2.3);
                    etched.Add(plate);
                    var inner = new PathsD();
                    var il = new List<PathD>();
                    Cabinets.Symbol(look, m.Theme, W / 2, (fy0 + fy1) / 2 + r * 0.1, r * 0.55, inner, il);
                    etched = Geo.Subtract(etched, Geo.Offset([plate], -0.5));
                    lines.AddRange(il);
                    etched.AddRange(inner);
                }
                break;
            }
        }
        m.Add(it.Hanging switch { HangingStyle.Banner => "Banner", HangingStyle.Painting => "Painting", HangingStyle.Shield => "Shield", HangingStyle.Map => "Map", HangingStyle.Monitor => "Screen", _ => "Tapestry" },
            outline, [], [Pose.Front(0, y0, m.T / 2)], grainAlongU: true, face: grain ? null : [], etched: etched, lines: lines);
        m.Note($"Glue it to the wall with its bottom {y0:0.#} mm up.");
    }

    /// <summary>A heater shield: straight top, sides curving to a point.</summary>
    public static PathD Heater(double x0, double y0, double w, double h)
    {
        var p = new PathD { new(x0, y0 + h), new(x0 + w, y0 + h) };
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            p.Add(new PointD(x0 + w - w / 2 * Math.Pow(t, 1.6), y0 + h * 0.62 * (1 - t) + 0));
        }
        for (int i = 15; i >= 1; i--)
        {
            double t = i / 16.0;
            p.Add(new PointD(x0 + w / 2 * Math.Pow(t, 1.6), y0 + h * 0.62 * (1 - t)));
        }
        p.Add(new PointD(x0, y0 + h * 0.62));
        return Geo.Tidy([p]).OrderByDescending(q => Math.Abs(Clipper.Area(q))).First();
    }

    /// <summary>A sword standing on its point at the origin, <paramref name="len"/> long: blade, crossguard, grip and pommel.</summary>
    public static PathsD Sword(double len, double blade)
    {
        double grip = len * 0.2, bl = len - grip;
        var p = new PathsD
        {
            new PathD { new(0, 0), new(blade / 2, blade * 0.9), new(blade / 2, bl), new(-blade / 2, bl), new(-blade / 2, blade * 0.9) },
            Geo.Rect(-blade * 1.9, bl - 0.01, blade * 1.9, bl + 1.0),
            Geo.Rect(-0.55, bl + 0.99, 0.55, len - 0.7),
            Geo.Circle(0, len - 0.6, 0.75),
        };
        return Geo.Union(p);
    }
}
