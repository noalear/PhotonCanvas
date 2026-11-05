using Clipper2Lib;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>How a crate's faces are boarded (and painted): what's etched and scored on each side and the lid.</summary>
public enum CrateStyle { Braced, CrossBraced, Slatted, Battened, Ironbound, Cargo, Military }

/// <summary>
/// The faces of crates, board by board. Each board is its own shape with its grain running along it (a
/// frame's uprights up and down, a brace corner to corner), with a groove round it where it meets the
/// next. On top of the boards: nail heads, iron corner brackets with rivets, rope handles and stencilled
/// words. The face detail picks how much wood shows: none (just the boards' grooves), wood grain (the
/// boards' grain, the infill one wide board), or planks and grain (the infill split into planks, with
/// a butt joint here and there).
/// </summary>
public static class Crates
{
    /// <summary>Names for the window, "Plain boards" (no style) first.</summary>
    public static readonly string[] StyleNames =
        ["Plain boards", "Braced (Z)", "Cross-braced (X)", "Slatted", "Battened planks", "Iron-bound", "Cargo (stencilled)", "Military (rope handles)"];

    /// <summary>Words stencilled on cargo crates when none are given.</summary>
    static readonly string[] CargoWords = ["CARGO", "FRAGILE", "SUPPLIES", "TOOLS", "SALT", "FLOUR", "LAMP OIL", "SPICES", "ROPE", "No. 7"];
    /// <summary>Words stencilled on military crates when none are given.</summary>
    static readonly string[] MilitaryWords = ["AMMO", "RATIONS", "MEDICAL", "MUNITIONS", "SIGNALS", "FUEL"];

    /// <summary>Nail heads (scored rings, mm).</summary>
    const double NailR = 0.16;

    /// <summary>One board: its shape, which way its grain runs (degrees) and whether it's nailed on top of the infill.</summary>
    sealed record Board(PathsD Shape, double Angle);

    /// <summary>What goes on one face.</summary>
    public enum Side { Long, End, Lid }

    /// <summary>
    /// A face <paramref name="Lu"/> × <paramref name="Lv"/> (its edge band <paramref name="T"/> wide is the
    /// finger joints) as etched areas and scored lines. <paramref name="label"/>: words to stencil (cargo
    /// and military crates); null or empty picks some.
    /// </summary>
    public static (PathsD Etched, List<PathD> Lines) Face(CrateStyle style, WoodDetail detail, Side side, double Lu, double Lv, double T, string? label, Random rnd)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (Lu < 3 || Lv < 3) return (etched, lines);
        var face = new PathsD { Geo.Rect(0, 0, Lu, Lv) };
        bool metal = detail == WoodDetail.Metal, stone = detail == WoodDetail.Stone;
        double fb = Math.Clamp(Math.Min(Lu, Lv) * 0.16, 1.2, 3), e = T * 0.5, g = WoodFace.GrooveMm;
        double x0 = e + fb, y0 = e + fb, x1 = Lu - e - fb, y1 = Lv - e - fb;
        bool framed = x1 - x0 >= 2 && y1 - y0 >= 2;
        // Things on top of the wood: grain and seams keep out from under them.
        var over = new PathsD();
        var overLines = new List<PathD>();

        if (metal || stone)
        {
            // Sci-fi and stone crates: the face's own detail, then the brackets and words that still make sense.
            var (me, ml) = metal ? WoodFace.Metal(face, rnd) : WoodFace.Stone(face, rnd);
            // (Not a metal vent under the words.)
            if (style is not (CrateStyle.Cargo or CrateStyle.Military)) etched.AddRange(me);
            lines.AddRange(ml);
            if (style == CrateStyle.Ironbound) Brackets(Lu, Lv, T, fb, over, overLines);
            if (style is CrateStyle.Cargo or CrateStyle.Military) Marks(style, side, Lu, Lv, e, e + Math.Clamp(Math.Min(Lu, Lv) * 0.12, 0.4, 1.2) + 0.3, label, rnd, over);
            return Finish(face, etched, lines, over, overLines);
        }

        var top = new List<Board>();
        bool infillAlongU = true;
        double plankMm = Math.Clamp(Math.Min(Lu, Lv) * 0.22, 1.6, 3.2);
        var nails = new List<PointD>();
        // How far in from each end the words and handles keep (clear of the frame or the cleats).
        double clear = framed ? x0 + 0.3 : e + 0.5;
        switch (style)
        {
            case CrateStyle.Braced:
            case CrateStyle.CrossBraced:
            case CrateStyle.Cargo:
                if (!framed) break;
                Frame(Lu, Lv, e, fb, top, nails);
                if (style == CrateStyle.Cargo) break;
                Brace(top, nails, new PointD(x0, y0), new PointD(x1, y1), fb, x0, y0, x1, y1);
                if (style == CrateStyle.CrossBraced)
                {
                    // The second brace butts against the first: two halves.
                    var inner = new PathsD { Geo.Rect(x0, y0, x1, y1) };
                    double ang = Math.Atan2(y0 - y1, x1 - x0);
                    var half = Geo.Subtract(Geo.Intersect([Geo.Strip(new PointD(x0, y1), new PointD(x1, y0), fb)], inner), top[^1].Shape);
                    foreach (var piece in Pieces(half)) top.Add(new Board(piece, ang * 180 / Math.PI));
                    nails.Add(new(x0 + fb * 0.45, y1 - fb * 0.35));
                    nails.Add(new(x1 - fb * 0.45, y0 + fb * 0.35));
                }
                break;
            case CrateStyle.Slatted:
            {
                // Uprights at the two ends, slats across with dark gaps between them.
                if (Lu - 2 * (e + fb) < 2) break;
                top.Add(new Board([Geo.Rect(-1, -1, e + fb, Lv + 1)], 90));
                top.Add(new Board([Geo.Rect(Lu - e - fb, -1, Lu + 1, Lv + 1)], 90));
                double span = Lv - 2 * e;
                int n = Math.Max(2, (int)Math.Round(span / (plankMm * 1.45)));
                double pitch = span / n, gap = Math.Clamp(pitch * 0.28, 0.5, 1.1);
                for (int i = 0; i < n; i++)
                {
                    double s0 = e + i * pitch + (i == 0 ? -e - 1 : gap / 2), s1 = e + (i + 1) * pitch - (i == n - 1 ? -e - 1 : gap / 2);
                    top.Add(new Board([Geo.Rect(e + fb - 0.01, s0, Lu - e - fb + 0.01, s1)], 0));
                    double ny = (Math.Max(s0, 0) + Math.Min(s1, Lv)) / 2;
                    foreach (double x in new[] { e + fb * 0.5, Lu - e - fb * 0.5 }) nails.Add(new(x, ny));
                    // The gap between this slat and the next, etched right through to look open.
                    if (i < n - 1) etched.Add(Geo.Rect(e + fb, e + (i + 1) * pitch - gap / 2, Lu - e - fb, e + (i + 1) * pitch + gap / 2));
                }
                break;
            }
            case CrateStyle.Battened:
            {
                // Planks up and down, held by a batten across the top and the bottom.
                infillAlongU = false;
                double bw = Math.Clamp(Lv * 0.15, 1.1, 2.6), b0 = e + 0.3, b1 = Lv - e - 0.3 - bw;
                if (b1 - b0 < bw + 1.5) break;
                top.Add(new Board([Geo.Rect(e * 0.5, b0, Lu - e * 0.5, b0 + bw)], 0));
                top.Add(new Board([Geo.Rect(e * 0.5, b1, Lu - e * 0.5, b1 + bw)], 0));
                int planks = Math.Max(2, (int)Math.Round((Lu - 2 * e) / plankMm));
                double pw = (Lu - 2 * e) / planks;
                for (int i = 0; i < planks; i++)
                    foreach (double y in new[] { b0 + bw / 2, b1 + bw / 2 })
                        nails.Add(new(e + (i + 0.5) * pw, y));
                break;
            }
            case CrateStyle.Ironbound:
                Brackets(Lu, Lv, T, fb, over, overLines);
                if (Lu > 14 && side != Side.Lid)
                {
                    // A strap down the middle of a wide face.
                    double sw = Math.Clamp(fb * 0.55, 0.8, 1.4), cx = Lu / 2;
                    var strap = new PathsD { Geo.Rect(cx - sw / 2, -1, cx + sw / 2, Lv + 1) };
                    for (double y = e + 1.2; y < Lv - e - 0.8; y += 2.4) strap = Geo.Subtract(strap, [Geo.Circle(cx, y, 0.2, 0.01)]);
                    over.AddRange(strap);
                }
                break;
            case CrateStyle.Military:
            {
                // Planks across, a cleat up each end, rope handles on the ends.
                if (Lu - 2 * (e + fb) < 3) break;
                double cw = Math.Clamp(fb * 0.85, 1.1, 2.4), ci = e + Math.Min(fb * 0.4, 1.2);
                top.Add(new Board([Geo.Rect(ci, e * 0.5, ci + cw, Lv - e * 0.5)], 90));
                top.Add(new Board([Geo.Rect(Lu - ci - cw, e * 0.5, Lu - ci, Lv - e * 0.5)], 90));
                clear = ci + cw + 0.4;
                int planks = Math.Max(2, (int)Math.Round((Lv - 2 * e) / plankMm));
                double ph = (Lv - 2 * e) / planks;
                for (int i = 0; i < planks; i++)
                    foreach (double x in new[] { ci + cw / 2, Lu - ci - cw / 2 })
                        nails.Add(new(x, e + (i + 0.5) * ph));
                break;
            }
        }
        if (style is CrateStyle.Cargo or CrateStyle.Military) Marks(style, side, Lu, Lv, e, clear, label, rnd, over);
        if (style == CrateStyle.Military && side == Side.End) RopeHandle(Lu, Lv, e, clear, over);

        // The infill: what the boards on top leave, as planks (or one wide board) along u or v.
        var topArea = Geo.Union(new PathsD(top.SelectMany(b => b.Shape)));
        var boards = new List<Board>(top);
        var rest = Geo.Subtract(face, topArea);
        if (style == CrateStyle.Slatted) rest = []; // all slats and uprights
        if (rest.Count > 0)
        {
            var rb = Geo.Bounds(rest);
            double across = infillAlongU ? rb.bottom - rb.top : rb.right - rb.left;
            int n = detail == WoodDetail.Planks ? Math.Max(1, (int)Math.Round(across / plankMm)) : 1;
            if (style == CrateStyle.Battened || style == CrateStyle.Military)
            {
                // Planks run the full face under the battens, so they're laid out over the whole face.
                rb = new RectD(0, 0, Lu, Lv);
                across = infillAlongU ? Lv - 2 * e : Lu - 2 * e;
                n = detail == WoodDetail.Planks ? Math.Max(2, (int)Math.Round(across / plankMm)) : 1;
            }
            double start = style is CrateStyle.Battened or CrateStyle.Military ? e : infillAlongU ? rb.top : rb.left;
            double pw = across / n;
            for (int i = 0; i < n; i++)
            {
                double a0 = i == 0 ? -1 : start + i * pw, a1 = i == n - 1 ? Math.Max(Lu, Lv) + 1 : start + (i + 1) * pw;
                var strip = infillAlongU ? Geo.Rect(-1, a0, Lu + 1, a1) : Geo.Rect(a0, -1, a1, Lv + 1);
                var pieces = Geo.Intersect([strip], rest);
                foreach (var piece in Pieces(pieces))
                {
                    // A long plank now and then joins another end to end.
                    var pb = Geo.Bounds(piece);
                    double len = infillAlongU ? pb.right - pb.left : pb.bottom - pb.top;
                    if (detail == WoodDetail.Planks && len > 12 && rnd.NextDouble() < 0.45)
                    {
                        double at = (infillAlongU ? pb.left : pb.top) + len * (0.25 + 0.5 * rnd.NextDouble());
                        var cut = infillAlongU ? Geo.Rect(at, pb.top - 1, pb.right + 1, pb.bottom + 1) : Geo.Rect(pb.left - 1, at, pb.right + 1, pb.bottom + 1);
                        foreach (var q in Pieces(Geo.Subtract(piece, [cut]))) boards.Add(new Board(q, infillAlongU ? 0 : 90));
                        foreach (var q in Pieces(Geo.Intersect(piece, [cut]))) boards.Add(new Board(q, infillAlongU ? 0 : 90));
                        continue;
                    }
                    boards.Add(new Board(piece, infillAlongU ? 0 : 90));
                }
            }
        }

        // A groove round every board (not along the face's own edges), and grain along each.
        var inside = Geo.Offset(face, -g * 0.8);
        var seams = new PathsD();
        foreach (var b in boards) seams.AddRange(Geo.Subtract(Geo.Offset(b.Shape, g / 2), Geo.Offset(b.Shape, -g / 2)));
        etched.AddRange(Geo.Intersect(Geo.Union(seams), inside));
        if (detail != WoodDetail.None)
            foreach (var b in boards)
            {
                var keep = Geo.Subtract(Geo.Intersect(Geo.Offset(b.Shape, -g / 2 - 0.1), face), Geo.Offset(over, 0.15));
                if (style == CrateStyle.Slatted) keep = Geo.Subtract(keep, Geo.Offset(etched, 0.1));
                lines.AddRange(BoardGrain(keep, b.Angle, rnd));
            }
        foreach (var p in nails)
            if (p.x > NailR + 0.2 && p.x < Lu - NailR - 0.2 && p.y > NailR + 0.2 && p.y < Lv - NailR - 0.2)
                lines.Add(WoodFace.Dot(p.x, p.y, NailR));
        return Finish(face, etched, lines, over, overLines);
    }

    static (PathsD, List<PathD>) Finish(PathsD face, PathsD etched, List<PathD> lines, PathsD over, List<PathD> overLines)
    {
        var all = Geo.Union(Geo.Union(etched), Geo.Union(over));
        // Lines don't run across what's painted or bolted on (its own lines are added after).
        var clear = Geo.Subtract(face, Geo.Offset(over, 0.1));
        var scored = Geo.ClipLines(lines, clear);
        scored.AddRange(Geo.ClipLines(overLines, face));
        return (Geo.Intersect(all, face), scored);
    }

    /// <summary>A frame of four boards round the face: top and bottom right across, the uprights between them.</summary>
    static void Frame(double Lu, double Lv, double e, double fb, List<Board> top, List<PointD> nails)
    {
        double w = e + fb;
        top.Add(new Board([Geo.Rect(-1, -1, Lu + 1, w)], 0));
        top.Add(new Board([Geo.Rect(-1, Lv - w, Lu + 1, Lv + 1)], 0));
        top.Add(new Board([Geo.Rect(-1, w, w, Lv - w)], 90));
        top.Add(new Board([Geo.Rect(Lu - w, w, Lu + 1, Lv - w)], 90));
        // Two nails where each upright meets the top and the bottom board.
        foreach (double x in new[] { e + fb * 0.5, Lu - e - fb * 0.5 })
            foreach (double y in new[] { e + fb * 0.5, Lv - e - fb * 0.5 })
            {
                nails.Add(new(x - fb * 0.22, y));
                nails.Add(new(x + fb * 0.22, y));
            }
    }

    /// <summary>A brace from one corner of the frame's inside to the other, a board wide, nailed at each end.</summary>
    static void Brace(List<Board> top, List<PointD> nails, PointD a, PointD b, double fb, double x0, double y0, double x1, double y1)
    {
        var inner = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        var strip = Geo.Intersect([Geo.Strip(a, b, fb)], inner);
        double ang = Math.Atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI;
        foreach (var piece in Pieces(strip)) top.Add(new Board(piece, ang));
        nails.Add(new(a.x + fb * 0.45, a.y + fb * 0.35));
        nails.Add(new(b.x - fb * 0.45, b.y - fb * 0.35));
    }

    /// <summary>Iron L brackets on the four corners, with raised rivets (left unetched) along their arms.</summary>
    static void Brackets(double Lu, double Lv, double T, double fb, PathsD over, List<PathD> overLines)
    {
        double arm = Math.Clamp(Math.Min(Lu, Lv) * 0.3, 2.2, 7), w = Math.Clamp(fb * 0.75, 0.9, 2);
        foreach (var (cx, sx) in new[] { (0.0, 1.0), (Lu, -1.0) })
            foreach (var (cy, sy) in new[] { (0.0, 1.0), (Lv, -1.0) })
            {
                double X(double d) => cx + sx * d;
                double Y(double d) => cy + sy * d;
                var l = new PathsD
                {
                    Geo.Rect(Math.Min(X(-1), X(arm)), Math.Min(Y(-1), Y(w)), Math.Max(X(-1), X(arm)), Math.Max(Y(-1), Y(w))),
                    Geo.Rect(Math.Min(X(-1), X(w)), Math.Min(Y(-1), Y(arm)), Math.Max(X(-1), X(w)), Math.Max(Y(-1), Y(arm))),
                };
                var plate = Geo.Union(l);
                // A rivet in the corner and one near each arm's end.
                double r = Math.Clamp(w * 0.18, 0.15, 0.3), m = Math.Max(T * 0.5 + r + 0.15, w / 2);
                var rivets = new PathsD
                {
                    Geo.Circle(X(m), Y(Math.Max(m, w / 2)), r, 0.01),
                    Geo.Circle(X(arm - w * 0.55), Y(w / 2), r, 0.01),
                    Geo.Circle(X(w / 2), Y(arm - w * 0.55), r, 0.01),
                };
                over.AddRange(Geo.Subtract(plate, rivets));
            }
    }

    /// <summary>Stencilled words on the long faces (or "this way up" arrows on a cargo crate's ends, a star on a military lid).</summary>
    static void Marks(CrateStyle style, Side side, double Lu, double Lv, double e, double clear, string? label, Random rnd, PathsD over)
    {
        string[] words = style == CrateStyle.Military ? MilitaryWords : CargoWords;
        string text = string.IsNullOrWhiteSpace(label) ? words[rnd.Next(words.Length)] : label.Trim().ToUpperInvariant();
        double cx = Lu / 2, cy = Lv / 2;
        switch (side)
        {
            case Side.Long:
            {
                double mw = Math.Min(Lu - 2 * clear, (Lu - 2 * e) * 0.78), mh = Math.Min((Lv - 2 * e) * 0.3, mw / Math.Max(2, text.Length) * 1.6);
                over.AddRange(Stencil(text, cx, cy, mw, mh, rnd));
                if (style == CrateStyle.Military && mh * 0.42 >= 0.7)
                {
                    // A smaller line under it: a lot number.
                    string lot = $"LOT {rnd.Next(10, 99)}-{rnd.Next(100, 999)}";
                    over.AddRange(Stencil(lot, cx, cy - mh * 0.95, mw * 0.6, mh * 0.42, rnd));
                }
                break;
            }
            case Side.End when style == CrateStyle.Cargo:
            {
                // Two arrows over a bar: this way up.
                double h = Math.Min((Lv - 2 * e) * 0.42, (Lu - 2 * clear) * 0.8), aw = h * 0.42;
                if (h < 1.6) break;
                double bar = Math.Max(0.25, h * 0.08);
                foreach (double dx in new[] { -aw * 0.62, aw * 0.62 }) over.Add(Arrow(cx + dx, cy - h / 2 + bar * 2.2, h - bar * 2.2, aw));
                over.Add(Geo.Rect(cx - aw * 1.3, cy - h / 2, cx + aw * 1.3, cy - h / 2 + bar));
                break;
            }
            case Side.Lid when style == CrateStyle.Military:
            {
                double r = Math.Min(Lu, Lv) * 0.2;
                if (r >= 1) over.Add(Star(cx, cy, r));
                break;
            }
        }
    }

    /// <summary>
    /// Words as stencilled paint: bold letters with the bridges a stencil needs across every letter's hole
    /// (an O in two halves), and a few flecks where the paint has worn away.
    /// </summary>
    static PathsD Stencil(string text, double cx, double cy, double maxW, double maxH, Random rnd)
    {
        if (maxW < 1 || maxH < 0.6) return [];
        PathGeometry g;
        try { g = TextLayout.Build(text, new TextStyle("Arial Black", maxH, Bold: true, Align: TextAlign.Center, FitWidthMm: maxW, FitHeightMm: maxH)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return []; }
        if (g.IsEmpty) return [];
        var (areas, _) = Geo.FromGeometry(g, 0.01);
        if (areas.Count == 0) return [];
        var b = Geo.Bounds(areas);
        areas = Geo.Move(areas, cx - (b.left + b.right) / 2, cy - (b.top + b.bottom) / 2);
        double h = b.bottom - b.top, bridge = Math.Max(0.12, h * 0.09);
        var c = new ClipperD(4);
        c.AddSubject(Geo.Tidy(areas));
        var tree = new PolyTreeD();
        c.Execute(ClipType.Union, FillRule.NonZero, tree);
        var result = new PathsD();
        for (int i = 0; i < tree.Count; i++)
        {
            var outer = tree[i];
            var shape = new PathsD { outer.Polygon! };
            var cuts = new PathsD();
            for (int j = 0; j < outer.Count; j++)
            {
                var hole = outer[j];
                shape.Add(hole.Polygon!);
                var hb = Clipper.GetBounds(hole.Polygon!);
                var ob = Clipper.GetBounds(outer.Polygon!);
                double hx = (hb.left + hb.right) / 2;
                cuts.Add(Geo.Rect(hx - bridge / 2, ob.top - 1, hx + bridge / 2, ob.bottom + 1));
                // Anything inside the hole (a letter's island) is a letter of its own.
                for (int k = 0; k < hole.Count; k++) result.Add(hole[k].Polygon!);
            }
            result.AddRange(Geo.Subtract(Geo.Tidy(shape), cuts));
        }
        // Worn paint: a few flecks missing.
        var bb = Geo.Bounds(result);
        var flecks = new PathsD();
        int nf = h < 1.5 ? 0 : 2 + rnd.Next(4);
        for (int k = 0; k < nf; k++)
            flecks.Add(Geo.Circle(bb.left + (bb.right - bb.left) * rnd.NextDouble(), bb.top + (bb.bottom - bb.top) * rnd.NextDouble(), h * (0.05 + 0.06 * rnd.NextDouble()), 0.01));
        // (Without the crumbs a fleck can leave at a letter's edge.)
        if (nf == 0) return Geo.Tidy(result);
        return [.. Geo.Tidy(Geo.Subtract(Geo.Tidy(result), flecks)).Where(p => Clipper.Area(p) < 0 || Clipper.Area(p) >= h * h * 0.02)];
    }

    /// <summary>An arrow pointing up, its foot at (cx, y0).</summary>
    static PathD Arrow(double cx, double y0, double h, double w)
    {
        double head = h * 0.42, shaft = w * 0.36;
        return
        [
            new(cx - shaft / 2, y0), new(cx + shaft / 2, y0), new(cx + shaft / 2, y0 + h - head), new(cx + w / 2, y0 + h - head),
            new(cx, y0 + h), new(cx - w / 2, y0 + h - head), new(cx - shaft / 2, y0 + h - head),
        ];
    }

    /// <summary>A five-pointed star.</summary>
    static PathD Star(double cx, double cy, double r)
    {
        var p = new PathD();
        for (int i = 0; i < 10; i++)
        {
            double a = Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * 0.4;
            p.Add(new(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr));
        }
        return p;
    }

    /// <summary>A rope handle: two cleats with a loop of rope hanging between them.</summary>
    static void RopeHandle(double Lu, double Lv, double e, double clear, PathsD over)
    {
        double span = Math.Min((Lu - 2 * clear) / 1.2, 8), cx = Lu / 2, cy = Lv * 0.7, cw = Math.Max(0.7, span * 0.16), ch = Math.Max(0.9, span * 0.2);
        if (span < 3 || cy + ch / 2 > Lv - e) return;
        foreach (double x in new[] { cx - span / 2, cx + span / 2 }) over.Add(Geo.Rect(x - cw / 2, cy - ch / 2, x + cw / 2, cy + ch / 2));
        double sag = span * 0.45, th = Math.Max(0.35, span * 0.08);
        var curve = new PathD();
        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32.0;
            curve.Add(new(cx - span / 2 + span * t, cy - sag * Math.Sin(Math.PI * t)));
        }
        var rope = Clipper.InflatePaths([curve], th / 2, JoinType.Round, EndType.Round, 2, 4);
        // The rope's twist: thin strips left unburnt across it.
        var twist = new PathsD();
        for (int i = 1; i < 16; i++)
        {
            double t = i / 16.0, x = cx - span / 2 + span * t, y = cy - sag * Math.Sin(Math.PI * t);
            twist.Add(Geo.Strip(new PointD(x - th * 0.5, y - th), new PointD(x + th * 0.5, y + th), Math.Max(0.08, th * 0.18)));
        }
        over.AddRange(Geo.Subtract(Geo.Union(rope), twist));
    }

    /// <summary>Grain along a board (degrees from u), clipped to the area.</summary>
    static List<PathD> BoardGrain(PathsD area, double angle, Random rnd)
    {
        if (area.Count == 0) return [];
        var flat = Geo.Turn(area, -angle);
        var b = Geo.Bounds(flat);
        if (b.right - b.left < 0.8 || b.bottom - b.top < 0.4) return [];
        var grain = WoodFace.Grain(flat, b, true, 0, b.bottom - b.top, rnd);
        double r = angle * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return Geo.Map(grain, q => new PointD(q.x * c - q.y * s, q.x * s + q.y * c));
    }

    /// <summary>Each separate piece of some areas, with its holes.</summary>
    static IEnumerable<PathsD> Pieces(PathsD a)
    {
        var tidy = Geo.Tidy(a);
        foreach (var p in tidy.Where(p => Clipper.Area(p) > 1e-4))
        {
            var holes = tidy.Where(h => Clipper.Area(h) < 0 && Clipper.PointInPolygon(h[0], p) == PointInPolygonResult.IsInside);
            yield return [p, .. holes];
        }
    }
}
