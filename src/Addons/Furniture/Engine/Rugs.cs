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
            case RugPattern.Runner:
            {
                // A long runner: a border with a vine waving along it, lilies (or the theme's symbol) in a row down
                // the dark field, a fringe at each end.
                outline = [Geo.Rect(0, 0, W, D)];
                double b = Geo.Clamp(D * 0.12, 0.8, 2.2), c = b * 1.25, amp = b * 0.32;
                etched.AddRange(Fronts.Outline(b * 0.45, b * 0.45, W - b * 0.45, D - b * 0.45, 0.4));
                etched.AddRange(Fronts.Outline(b * 2.05, b * 2.05, W - b * 2.05, D - b * 2.05, 0.4));
                Fringe(W, D, lines);
                foreach (var (a, e, n) in new[] { (new PointD(c, c), new PointD(W - c, c), new PointD(0, 1)), (new PointD(W - c, c), new PointD(W - c, D - c), new PointD(-1, 0)),
                                                   (new PointD(W - c, D - c), new PointD(c, D - c), new PointD(0, -1)), (new PointD(c, D - c), new PointD(c, c), new PointD(1, 0)) })
                {
                    double len = Math.Sqrt((e.x - a.x) * (e.x - a.x) + (e.y - a.y) * (e.y - a.y));
                    int waves = Math.Max(1, (int)Math.Round(len / (b * 2.6)));
                    var vine = new PathD();
                    for (int i = 0; i <= waves * 16; i++)
                    {
                        double t = (double)i / (waves * 16), w = amp * Math.Sin(t * waves * 2 * Math.PI);
                        vine.Add(new PointD(a.x + (e.x - a.x) * t + n.x * w, a.y + (e.y - a.y) * t + n.y * w));
                    }
                    lines.Add(vine);
                    // A leaf at each crest.
                    for (int k = 0; k < waves; k++)
                        foreach (var (tt, sg) in new[] { ((k + 0.25) / waves, 1.0), ((k + 0.75) / waves, -1.0) })
                            if (amp > 0.25) lines.Add(WoodFace.Dot(a.x + (e.x - a.x) * tt + n.x * amp * sg * 0.4, a.y + (e.y - a.y) * tt + n.y * amp * sg * 0.4, amp * 0.45));
                }
                double fx0 = b * 2.4, fx1 = W - b * 2.4, fy0 = b * 2.4, fy1 = D - b * 2.4, fd = fy1 - fy0;
                if (fd > 2 && fx1 - fx0 > 4)
                {
                    var look = Fronts.Look(m);
                    var marks = new PathsD();
                    var ml = new List<PathD>();
                    int n = Math.Max(1, (int)Math.Round((fx1 - fx0) / (fd * 1.3)));
                    double step = (fx1 - fx0) / n;
                    for (int i = 0; i < n; i++)
                    {
                        double cx = fx0 + step * (i + 0.5), cy = (fy0 + fy1) / 2;
                        if (m.Theme is PhotonCanvas.Addon.Terrain.TerrainThemes.Medieval or PhotonCanvas.Addon.Terrain.TerrainThemes.Gothic)
                        {
                            var lily = new PathsD();
                            var ll = new List<PathD>();
                            Pictures.Lily(0, 0, fd * 0.36, lily, ll);
                            // Pointing along the runner, towards its far end.
                            marks.AddRange(Geo.Turn(Geo.Union(lily), -90, cx, cy));
                        }
                        else Cabinets.Symbol(look, m.Theme, cx, cy, fd * 0.3, marks, ml);
                    }
                    // The field burnt dark round them, the lilies left light; a symbol is scored on a light field.
                    if (ml.Count == 0) etched.AddRange(Geo.Subtract([Geo.Rect(fx0, fy0, fx1, fy1)], Geo.Offset(marks, 0.15)));
                    else { etched.AddRange(marks); lines.AddRange(ml); }
                }
                break;
            }
            case RugPattern.Hide:
            {
                // A shaggy edge: tufts of fur all round the hide (round, so none is too thin to cut).
                var body = Hide(W / 2, D / 2, W / 2 - 0.6, D / 2 - 0.6, rnd);
                var tufts = new PathsD { body };
                double run = 0;
                for (int i = 0; i < body.Count; i++)
                {
                    var p0 = body[i];
                    var p1 = body[(i + 1) % body.Count];
                    double len = Math.Sqrt((p1.x - p0.x) * (p1.x - p0.x) + (p1.y - p0.y) * (p1.y - p0.y));
                    for (double t = run; t < len; t += 1.15)
                    {
                        double k = t / len;
                        tufts.Add(Geo.Circle(p0.x + (p1.x - p0.x) * k, p0.y + (p1.y - p0.y) * k, 0.5 + 0.22 * rnd.NextDouble(), 0.01));
                    }
                    run = Math.Max(0, (run - len) % 1.15);
                    if (run == 0) run = 1.15 - len % 1.15;
                }
                outline = Geo.Solid(tufts);
                // A darker saddle of fur down the back, its edge ragged.
                var saddle = Geo.Offset([body], -Math.Min(W, D) * 0.13);
                var nicks = new PathsD();
                foreach (var q in saddle.SelectMany(p => p).Where((_, i) => i % 3 == 0))
                    nicks.Add(Geo.Circle(q.x, q.y, 0.35 + 0.5 * rnd.NextDouble(), 0.01));
                etched.AddRange(Geo.Subtract(saddle, nicks));

                // Fur: short strokes all over, and the spine.
                lines.Add([new(W * 0.12, D / 2), new(W * 0.88, D / 2)]);
                for (int i = 0; i < (int)(W * D / 6); i++)
                {
                    double x = W * (0.1 + 0.8 * rnd.NextDouble()), y = D * (0.15 + 0.7 * rnd.NextDouble()), a = (y < D / 2 ? -1 : 1) * (0.4 + rnd.NextDouble() * 0.5);
                    lines.Add([new(x, y), new(x + Math.Cos(a) * 0.7, y + Math.Sin(a) * 0.7)]);
                }
                lines = Geo.ClipLines(lines, Geo.Offset(outline, -0.3));
                break;
            }
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
        if (it.Hanging == HangingStyle.Torch) { Profiles.WallTorch(m); return; }
        var look = Fronts.Look(m);
        var rnd = m.Rnd;
        var etched = new PathsD();
        var lines = new List<PathD>();
        PathsD outline;
        // Holes right through it (a tattered banner's tears).
        var tears = new PathsD();
        bool grain = false;
        switch (it.Hanging)
        {
            case HangingStyle.Banner when m.Theme == PhotonCanvas.Addon.Terrain.TerrainThemes.Dungeon && W >= 7 && H >= 14:
                outline = WarBanner(W, H, look, m.Theme, rnd, etched, lines, tears);
                break;
            case HangingStyle.Banner:
            {
                // A pole across the top, the cloth hanging from it, a swallowtail (or a point) at the bottom.
                double pole = Math.Min(1.2, H * 0.08), cw = W * 0.8, x0 = (W - cw) / 2, tail = Math.Min(cw * 0.5, H * 0.2);
                var cloth = new PathD { new(x0, tail), new(x0, H - pole), new(x0 + cw, H - pole), new(x0 + cw, tail) };
                if (look == FrontLook.Gothic || look == FrontLook.Metal) cloth.Add(new PointD(x0 + cw / 2, 0));
                else { cloth.Add(new PointD(x0 + cw, 0)); cloth.Add(new PointD(x0 + cw / 2, tail)); cloth.Add(new PointD(x0, 0)); }
                outline = Geo.Union([cloth, Geo.Rect(0, H - pole, W, H), Geo.Circle(0.6, H - pole / 2, 0.75), Geo.Circle(W - 0.6, H - pole / 2, 0.75)]);
                etched.AddRange(Fronts.Outline([Geo.Rect(x0 + 0.7, tail + 0.4, x0 + cw - 0.7, H - pole - 0.7)], 0.35));
                double sw = cw * 0.72, sh = sw * 1.15, scy = tail + (H - tail - pole) * 0.55;
                if (look != FrontLook.Metal && sw >= 7 && tail + sh * 0.5 < scy && scy + sh / 2 < H - pole - 1.2)
                {
                    // A quartered shield: two quarters dark with the symbol left light in them, two light with it carved.
                    var shield = Heater(W / 2 - sw / 2, scy - sh / 2, sw, sh);
                    etched.AddRange(Fronts.Outline([shield], 0.45));
                    var inside = Geo.Offset([shield], -0.5);
                    double qr = sw * 0.17;
                    foreach (var (qx, qy, dark) in new[] { (-1, 1, true), (1, 1, false), (-1, -1, false), (1, -1, true) })
                    {
                        var quarter = Geo.Intersect(inside, [Geo.Rect(W / 2 + Math.Min(0, qx) * sw + (qx > 0 ? 0.2 : 0), scy + Math.Min(0, qy) * sh + (qy > 0 ? 0.2 : 0), W / 2 + Math.Max(0, qx) * sw - (qx < 0 ? 0.2 : 0), scy + Math.Max(0, qy) * sh - (qy < 0 ? 0.2 : 0))]);
                        var se = new PathsD();
                        var sl = new List<PathD>();
                        Cabinets.Symbol(look, m.Theme, W / 2 + qx * sw * 0.24, scy + qy * sh * 0.2 + (qy < 0 ? sh * 0.05 : 0), qr, se, sl);
                        if (dark) etched.AddRange(Geo.Subtract(quarter, Geo.Offset(Geo.Union(se), 0.25)));
                        else { etched.AddRange(Geo.Intersect(Geo.Union(se), quarter)); lines.AddRange(Geo.ClipLines(sl, quarter)); }
                    }
                }
                else
                {
                    double r = Math.Min(cw * 0.3, (H - tail - pole) * 0.22);
                    Cabinets.Symbol(look, m.Theme, W / 2, scy, r, etched, lines);
                }
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
                if (m.Theme is PhotonCanvas.Addon.Terrain.TerrainThemes.Medieval or PhotonCanvas.Addon.Terrain.TerrainThemes.Gothic && sw >= 8)
                {
                    // Heraldic: parted down the middle, the theme's symbol on the light half and lilies left light on
                    // the dark one, a ring of rivets inside the rim.
                    var inner = Geo.Offset([shield], -0.9);
                    var dark = Geo.Intersect(inner, [Geo.Rect(cx, cy - sh, cx + sw, cy + sh)]);
                    var lilies = new PathsD();
                    var ll = new List<PathD>();
                    double lr = sw * 0.13;
                    Pictures.Lily(cx + sw * 0.22, cy + sh * 0.2, lr, lilies, ll);
                    Pictures.Lily(cx + sw * 0.2, cy - sh * 0.14, lr * 0.85, lilies, ll);
                    etched.AddRange(Geo.Subtract(dark, Geo.Offset(Geo.Union(lilies), 0.12)));
                    lines.Add([new(cx, cy + sh / 2 - 0.9), new(cx, cy - sh / 2 + 1.2)]);
                    Cabinets.Symbol(look == FrontLook.Plank ? FrontLook.Gothic : look, m.Theme, cx - sw * 0.2, cy + sh * 0.08, sw * 0.14, etched, lines);
                    var rim = Geo.Offset([shield], -0.55).FirstOrDefault();
                    if (rim is not null)
                    {
                        double per = 0;
                        for (int i = 0; i < rim.Count; i++) { var a = rim[i]; var b2 = rim[(i + 1) % rim.Count]; per += Math.Sqrt((b2.x - a.x) * (b2.x - a.x) + (b2.y - a.y) * (b2.y - a.y)); }
                        int nr = Math.Max(8, (int)(per / 2.2));
                        double step = per / nr, acc = 0, next = step / 2;
                        for (int i = 0; i < rim.Count && nr > 0; i++)
                        {
                            var a = rim[i]; var b2 = rim[(i + 1) % rim.Count];
                            double len = Math.Sqrt((b2.x - a.x) * (b2.x - a.x) + (b2.y - a.y) * (b2.y - a.y));
                            while (next <= acc + len) { double t = (next - acc) / len; lines.Add(WoodFace.Dot(a.x + (b2.x - a.x) * t, a.y + (b2.y - a.y) * t, 0.2)); next += step; }
                            acc += len;
                        }
                    }
                }
                else if (look == FrontLook.Plank && m.Theme is "Dungeon" or "Cave" && sw >= 8)
                {
                    // A round shield of boards in an iron rim studded with rivets, an iron boss in the middle, and a
                    // chevron painted dark down the boards.
                    double R = sw / 2, rim = Geo.Clamp(R * 0.12, 0.7, 1.4), ri = R - 0.5 - rim;
                    var face = new PathsD { Geo.Circle(cx, cy, ri, 0.01) };
                    double pw = Geo.Clamp(sw / 5, 1.6, 3);
                    for (double x = cx - ri + pw; x < cx + ri - 0.3; x += pw)
                    {
                        double hh = Math.Sqrt(Math.Max(0, ri * ri - (x - cx) * (x - cx)));
                        lines.Add([new(x, cy - hh), new(x, cy + hh)]);
                    }
                    lines.Add(Geo.Loop(Geo.Circle(cx, cy, ri, 0.01)));
                    double rr = R - 0.5 - rim / 2;
                    int nr = Math.Max(8, (int)(2 * Math.PI * rr / 2.4));
                    for (int i = 0; i < nr; i++) lines.Add(WoodFace.Dot(cx + rr * Math.Cos(2 * Math.PI * i / nr), cy + rr * Math.Sin(2 * Math.PI * i / nr), 0.22));
                    // The chevron: two dark bands meeting in a point above the boss.
                    double bw = Geo.Clamp(ri * 0.22, 1.0, 2.4), boss = Geo.Clamp(ri * 0.3, 1.4, 3.2);
                    var chev = new PathsD();
                    foreach (var sgn in new[] { -1, 1 })
                        chev.Add(Geo.Strip(new PointD(cx, cy + ri * 0.6), new PointD(cx + sgn * ri * 0.95, cy - ri * 0.7), bw));
                    etched.AddRange(Geo.Subtract(Geo.Intersect(Geo.Union(chev), Geo.Offset(face, -0.4)), [Geo.Circle(cx, cy, boss + 0.4, 0.01)]));
                    lines.Add(Geo.Loop(Geo.Circle(cx, cy, boss, 0.01)));
                    lines.Add(WoodFace.Dot(cx, cy, boss * 0.55));
                    lines.Add(WoodFace.Dot(cx - boss * 0.25, cy + boss * 0.25, 0.25));
                    lines = Geo.ClipLines(lines, Geo.Solid(Geo.Union(outline, [shield])));
                }
                else if (look == FrontLook.Plank) { lines.Add(WoodFace.Dot(cx, cy, r * 0.8)); etched.Add(Geo.Circle(cx, cy, r * 0.45, 0.01)); }
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
            case HangingStyle.FramedMap:
            {
                // A map in a heavy frame with blocks proud at its corners, hung by two chains from a ring.
                double rr = 1.6, ring = H - rr, Hf = Geo.Clamp(H - 2 * rr - Math.Min(W * 0.22, 6), H * 0.6, H - 2 * rr - 2);
                double fr = Geo.Clamp(Math.Min(W, Hf) * 0.11, 1.0, 2.4), cb = fr * 1.25;
                var parts = new PathsD { Geo.Rect(0, 0, W, Hf) };
                // The corner blocks stand a little past the frame.
                foreach (var (cx, cy) in new[] { (cb / 2 - 0.3, cb / 2 - 0.3), (W - cb / 2 + 0.3, cb / 2 - 0.3), (cb / 2 - 0.3, Hf - cb / 2 + 0.3), (W - cb / 2 + 0.3, Hf - cb / 2 + 0.3) })
                    parts.Add(Geo.Rect(cx - cb / 2, cy - cb / 2, cx + cb / 2, cy + cb / 2));
                // The chains, from just in from the top corners up to the ring.
                PointD top = new(W / 2, ring);
                var chainFrom = new[] { new PointD(cb * 0.9, Hf - 0.4), new PointD(W - cb * 0.9, Hf - 0.4) };
                foreach (var a in chainFrom) parts.Add(Geo.Strip(a, top, 1.3));
                parts.Add(Geo.Circle(W / 2, ring, rr, 0.02));
                outline = Geo.Subtract(Geo.Union(parts), [Geo.Circle(W / 2, ring, 0.6, 0.02)]);
                Pictures.Frame(0, 0, W, Hf, fr, etched, lines);
                foreach (var (cx, cy) in new[] { (cb / 2 - 0.3, cb / 2 - 0.3), (W - cb / 2 + 0.3, cb / 2 - 0.3), (cb / 2 - 0.3, Hf - cb / 2 + 0.3), (W - cb / 2 + 0.3, Hf - cb / 2 + 0.3) })
                {
                    lines.Add(Geo.Loop(Geo.Rect(cx - cb / 2 + 0.25, cy - cb / 2 + 0.25, cx + cb / 2 - 0.25, cy + cb / 2 - 0.25)));
                    // A four-pointed star on each block.
                    double r = cb * 0.32;
                    lines.Add([new(cx - r, cy), new(cx + r, cy)]);
                    lines.Add([new(cx, cy - r), new(cx, cy + r)]);
                }
                // The links: an oval scored for every other one, a bar across for the ones between.
                foreach (var a in chainFrom)
                {
                    double dx = top.x - a.x, dy = top.y - a.y, len = Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len;
                    for (double t = 1.2, k = 0; t < len - rr - 0.6; t += 1.1, k++)
                    {
                        double cx = a.x + ux * t, cy = a.y + uy * t;
                        if (k % 2 == 0)
                        {
                            var o = Ellipse(0, 0, 0.75, 0.32);
                            lines.Add(Geo.Loop([.. o.Select(q => new PointD(cx + q.x * ux - q.y * uy, cy + q.x * uy + q.y * ux))]));
                        }
                        else lines.Add([new(cx - uy * 0.45, cy + ux * 0.45), new(cx + uy * 0.45, cy - ux * 0.45)]);
                    }
                }
                if (look == FrontLook.Metal) Pictures.StarMap(fr, fr, W - fr, Hf - fr, rnd, etched, lines);
                else Pictures.Map(fr, fr, W - fr, Hf - fr, rnd, etched, lines);
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
        m.Add(it.Hanging switch { HangingStyle.Banner => "Banner", HangingStyle.Painting => "Painting", HangingStyle.Shield => "Shield", HangingStyle.Map or HangingStyle.FramedMap => "Map", HangingStyle.Monitor => "Screen", _ => "Tapestry" },
            outline, tears, [Pose.Front(0, y0, m.T / 2)], grainAlongU: true, face: grain ? null : [], etched: etched, lines: lines);
        m.Note($"Glue it to the wall with its bottom {y0:0.#} mm up.");
    }

    /// <summary>
    /// A war banner: a spear across the top with a head at each end, the cloth hanging from it on two chains,
    /// its bottom torn into tatters and a tear or two through it; burnt dark but for a border and a great
    /// skull left light.
    /// </summary>
    static PathsD WarBanner(double W, double H, FrontLook look, string theme, Random rnd, PathsD etched, List<PathD> lines, PathsD tears)
    {
        double ph = 1.2, yc = H - ph / 2, head = Math.Min(2.2, W * 0.18), cw = W * 0.72, x0 = (W - cw) / 2, x1 = x0 + cw;
        double chain = Geo.Clamp(H * 0.1, 1.8, 3.5), top = H - ph - chain, tail = Math.Min(cw * 0.6, H * 0.22);
        // The spear: a shaft, a head each end (blunted to what the laser leaves standing), a collar behind each head.
        var spear = new PathsD
        {
            Geo.Rect(head - 0.2, H - ph, W - head + 0.2, H),
            new PathD { new(0, yc), new(head, yc - 1.0), new(head, yc + 1.0) },
            new PathD { new(W, yc), new(W - head, yc + 1.0), new(W - head, yc - 1.0) },
            Geo.Rect(head - 0.1, yc - 0.85, head + 0.7, yc + 0.85), Geo.Rect(W - head - 0.7, yc - 0.85, W - head + 0.1, yc + 0.85),
        };
        var pole = Geo.Offset(Geo.Offset(Geo.Solid(spear), -0.4), 0.4, round: true);
        lines.Add([new(head + 0.7, yc), new(W - head - 0.7, yc)]);
        // The chains: a strip of links each side.
        var chains = new PathsD();
        foreach (var cx in new[] { x0 + 1.0, x1 - 1.0 })
        {
            chains.Add(Geo.Rect(cx - 0.6, top - 0.5, cx + 0.6, H - ph + 0.1));
            for (double y = top + 0.2; y < H - ph - 0.3; y += 0.9) lines.Add([new(cx - 0.6, y), new(cx + 0.6, y)]);
            lines.Add([new(cx, top), new(cx, H - ph)]);
        }
        // The cloth: straight down the sides, the bottom torn into tatters of different lengths.
        var cloth = new PathD { new(x0, top), new(x1, top) };
        int n = Math.Max(4, (int)Math.Round(cw / 1.3));
        for (int i = 0; i <= n; i++)
        {
            double x = x1 - cw * i / n;
            double y = i % 2 == 0 ? tail * (0.05 + 0.3 * rnd.NextDouble()) : tail * (0.7 + 0.3 * rnd.NextDouble());
            cloth.Add(new PointD(x, y));
        }
        var cl = Geo.Offset(Geo.Offset(Geo.Solid([cloth]), -0.5), 0.5, round: true);
        var outline = Geo.Union(Geo.Union(pole, chains), cl);
        // A tear or two through it, clear of the edges and the skull.
        double r = Math.Min(cw * 0.33, (top - tail) * 0.28), scx = W / 2, scy = tail + (top - tail) * 0.58;
        foreach (var (tx, ty) in new[] { (x0 + cw * 0.26, tail + (top - tail) * 0.14), (x1 - cw * 0.3, scy - r * 1.7) })
        {
            if (ty - 1.4 < tail || Math.Sqrt((tx - scx) * (tx - scx) + (ty - scy) * (ty - scy)) < r * 1.2 + 1) continue;
            // A ragged slash, leaning a little: jagged both sides, widest in the middle.
            var t = new PathD();
            double lean = (rnd.NextDouble() - 0.5) * 0.5, len = 1.4;
            for (int k = 0; k <= 6; k++)
            {
                double f = k / 6.0 * 2 - 1, half = 0.5 * (1 - f * f) + (k % 2 == 0 ? 0.08 : -0.08);
                t.Add(new PointD(tx + f * len * lean + Math.Max(0.05, half), ty + f * len));
            }
            for (int k = 6; k >= 0; k--)
            {
                double f = k / 6.0 * 2 - 1, half = 0.45 * (1 - f * f) + (k % 2 == 1 ? 0.08 : -0.08);
                t.Add(new PointD(tx + f * len * lean - Math.Max(0.05, half), ty + f * len));
            }
            tears.Add(t);
        }
        // The field burnt dark inside a light border; the skull left light, its eyes and nose dark.
        var field = Geo.Offset(cl, -0.7);
        var skullE = new PathsD();
        var skullL = new List<PathD>();
        Cabinets.Symbol(look, theme, scx, scy, r, skullE, skullL);
        var headArea = Geo.Solid([Geo.Circle(scx, scy + r * 0.18, r * 0.66, 0.01), WoodFace.RoundRect(scx - r * 0.4, scy - r * 0.8, scx + r * 0.4, scy - r * 0.05, r * 0.15)]);
        etched.AddRange(Geo.Subtract(Geo.Subtract(field, Geo.Offset(headArea, 0.15)), Geo.Offset(tears, 0.2)));
        etched.AddRange(skullE);
        lines.AddRange(Geo.ClipLines(skullL, headArea));
        return outline;
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
