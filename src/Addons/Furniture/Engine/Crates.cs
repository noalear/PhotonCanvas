using Clipper2Lib;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>How a crate's faces are boarded (and painted): what's etched and scored on each side and the lid.</summary>
public enum CrateStyle { Braced, CrossBraced, Slatted, Battened, Ironbound, Cargo, Military, Carved, Arched, Lashed }

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
        ["Plain boards", "Braced (Z)", "Cross-braced (X)", "Slatted", "Battened planks", "Iron-bound", "Cargo (stencilled)", "Military (rope handles)",
         "Carved star lattice", "Gothic arches, iron corners", "Lashed poles"];

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
    /// What stands proud of a face, glued on over it: the boards of a frame or a brace, iron corners and straps,
    /// rope handles. Each separate bit is a piece of its own; <see cref="Etched"/> and <see cref="Lines"/> are
    /// the whole face's (each piece keeps what falls on it).
    /// </summary>
    public sealed record Raised(List<PathsD> Pieces, PathsD Etched, List<PathD> Lines, string What);

    /// <summary>
    /// A face <paramref name="Lu"/> × <paramref name="Lv"/> (its edge band <paramref name="T"/> wide is the
    /// finger joints) as etched areas and scored lines. <paramref name="label"/>: words to stencil (cargo
    /// and military crates); null or empty picks some. With <paramref name="raise"/>, the frame, braces,
    /// iron and handles come back as pieces to glue on over the face (and the face leaves out what they cover).
    /// </summary>
    public static (PathsD Etched, List<PathD> Lines, Raised? Up) Face(CrateStyle style, WoodDetail detail, Side side, double Lu, double Lv, double T, string? label, Random rnd,
        string? theme = null, bool raise = false, bool handles = false)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (Lu < 3 || Lv < 3) return (etched, lines, null);
        var face = new PathsD { Geo.Rect(0, 0, Lu, Lv) };
        bool metal = detail == WoodDetail.Metal, stone = detail == WoodDetail.Stone;
        double fb = Math.Clamp(Math.Min(Lu, Lv) * 0.16, 1.2, 3), e = T * 0.5, g = WoodFace.GrooveMm;
        double x0 = e + fb, y0 = e + fb, x1 = Lu - e - fb, y1 = Lv - e - fb;
        bool framed = x1 - x0 >= 2 && y1 - y0 >= 2;
        // Things on top of the wood: grain and seams keep out from under them.
        var over = new PathsD();
        var overLines = new List<PathD>();
        // What's glued on proud of the face, and bits the grain keeps off (they have outlines of their own).
        var up = new PathsD();
        var plain = new PathsD();
        bool gothic = theme == PhotonCanvas.Addon.Terrain.TerrainThemes.Gothic;

        if (metal || stone)
        {
            // Sci-fi and stone crates: the face's own detail, then the brackets and words that still make sense.
            var (me, ml) = metal ? WoodFace.Metal(face, rnd) : WoodFace.Stone(face, rnd);
            // (Not a metal vent under the words.)
            if (style is not (CrateStyle.Cargo or CrateStyle.Military)) etched.AddRange(me);
            lines.AddRange(ml);
            if (style is (CrateStyle.Ironbound or CrateStyle.Arched) && !(metal && raise)) Brackets(Lu, Lv, T, fb, over, overLines, null);
            if (style is CrateStyle.Cargo or CrateStyle.Military) Marks(style, side, Lu, Lv, e, metal && raise ? Math.Clamp(fb * 0.8, 1.2, 2) + 0.6 : e + Math.Clamp(Math.Min(Lu, Lv) * 0.12, 0.4, 1.2) + 0.3, label, rnd, over);
            if (metal && raise) return MetalTrim(style, side, Lu, Lv, e, fb, face, etched, lines, over, overLines);
            var (se, sl) = Finish(face, etched, lines, over, overLines, plain);
            return (se, sl, null);
        }

        var top = new List<Board>();
        bool infillAlongU = true;
        double plankMm = Math.Clamp(Math.Min(Lu, Lv) * 0.22, 1.6, 3.2);
        var nails = new List<PointD>();
        // How far in from each end the words and handles keep (clear of the frame or the cleats).
        double clear = framed ? x0 + 0.3 : e + 0.5;
        // Rope handles on the ends: the end keeps its frame but not its brace.
        bool ropes = side == Side.End && (style == CrateStyle.Military || handles);
        // The face inside the frame is carved, not boarded.
        bool carvedIn = false;
        switch (style)
        {
            case CrateStyle.Braced:
            case CrateStyle.CrossBraced:
            case CrateStyle.Cargo:
                if (!framed) break;
                Frame(Lu, Lv, e, fb, top, nails);
                if (style == CrateStyle.Cargo || ropes) break;
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
                    if (gothic)
                    {
                        // A carved quatrefoil boss where the braces cross.
                        double r = Math.Min(fb * 0.62, Math.Min(x1 - x0, y1 - y0) * 0.16);
                        if (r >= 0.7) Boss(Candles.Quatrefoil(Lu / 2, Lv / 2, r), Lu / 2, Lv / 2, r * 0.4, up, plain, overLines);
                    }
                }
                break;
            case CrateStyle.Carved:
            case CrateStyle.Arched:
                if (!framed) break;
                Frame(Lu, Lv, e, fb, top, nails);
                if (style == CrateStyle.Arched) Plates(Lu, Lv, e, fb, over, overLines, raise ? up : null);
                if (side == Side.Lid) break;
                carvedIn = true;
                if (style == CrateStyle.Carved) StarLattice(x0, y0, x1, y1, etched, lines);
                else Arcade(x0, y0, x1, y1, gothic, theme, etched, lines);
                break;
            case CrateStyle.Lashed:
            {
                // Poles round the edge and one across, lashed where they cross; split logs between them.
                if (!framed) break;
                double pw = Math.Clamp(fb * 1.15, 1.3, 3.2);
                double px0 = e + pw, py0 = e + pw, px1 = Lu - e - pw, py1 = Lv - e - pw;
                if (px1 - px0 < 2 || py1 - py0 < 2) break;
                Frame(Lu, Lv, e, pw, top, []);
                if (side != Side.Lid && !ropes) Brace(top, [], new PointD(px0, py1), new PointD(px1, py0), pw, px0, py0, px1, py1);
                infillAlongU = false;
                foreach (double cx in new[] { (e + pw) / 2, Lu - (e + pw) / 2 })
                    foreach (double cy in new[] { (e + pw) / 2, Lv - (e + pw) / 2 })
                        Lashing(cx, cy, e + pw, plain, overLines);
                clear = px0 + 0.3;
                break;
            }
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
            {
                // (Flat: iron a whole sheet thick reads as a block of wood.)
                Brackets(Lu, Lv, T, fb, over, overLines, null);
                // A dungeon's or a chapel's crate has its mark on the long sides; otherwise a strap down the middle with a ring.
                bool mark = side == Side.Long && theme is PhotonCanvas.Addon.Terrain.TerrainThemes.Dungeon or PhotonCanvas.Addon.Terrain.TerrainThemes.Gothic;
                double r = Math.Min(Lu * 0.2, (Lv - 2 * e) * 0.32);
                if (mark && r >= 1)
                {
                    var me = new PathsD();
                    var ml = new List<PathD>();
                    if (gothic)
                    {
                        // A raised iron boss with a lily on it.
                        Boss(Geo.Union([Geo.Circle(Lu / 2, Lv / 2, r, 0.01)]), Lu / 2, Lv / 2, 0, null, plain, overLines);
                        Pictures.Lily(Lu / 2, Lv / 2 + r * 0.05, r * 0.7, me, ml);
                    }
                    else Cabinets.Symbol(FrontLook.Plank, theme, Lu / 2, Lv / 2, r, me, ml);
                    plain.Add(Geo.Circle(Lu / 2, Lv / 2, r * 0.9, 0.01));
                    over.AddRange(me);
                    overLines.AddRange(ml);
                }
                else if (Lu > 14 && side != Side.Lid)
                {
                    // A strap down the middle of a wide face, with a ring to lift it by.
                    double sw = Math.Clamp(fb * 0.55, raise ? 1.2 : 0.8, 1.4), cx = Lu / 2;
                    var strap = new PathsD { Geo.Rect(cx - sw / 2, -1, cx + sw / 2, Lv + 1) };
                    for (double y = e + 1.2; y < Lv - e - 0.8; y += 2.4) strap = Geo.Subtract(strap, [Geo.Circle(cx, y, 0.2, 0.01)]);
                    over.AddRange(strap);
                    if (side == Side.Long && Lv > 7) overLines.Add(WoodFace.Dot(cx, Lv * 0.45, Math.Min(1.3, Lv * 0.12)));
                }
                break;
            }
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
        if (ropes)
        {
            var rope = RopeHandle(Lu, Lv, e, clear, over, raise);
            if (raise) up.AddRange(rope);
        }

        // The infill: what the boards on top leave, as planks (or one wide board) along u or v.
        var topArea = Geo.Union(new PathsD(top.SelectMany(b => b.Shape)));
        var boards = new List<Board>(top);
        var rest = Geo.Subtract(face, topArea);
        if (style == CrateStyle.Slatted || carvedIn) rest = []; // all slats and uprights, or carved
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
        etched.AddRange(Geo.Subtract(Geo.Intersect(Geo.Union(seams), inside), plain));
        var offGrain = Geo.Offset(Geo.Union(over, plain), 0.15);
        if (detail != WoodDetail.None)
            foreach (var b in boards)
            {
                var keep = Geo.Subtract(Geo.Intersect(Geo.Offset(b.Shape, -g / 2 - 0.1), face), offGrain);
                if (style == CrateStyle.Slatted) keep = Geo.Subtract(keep, Geo.Offset(etched, 0.1));
                lines.AddRange(BoardGrain(keep, b.Angle, rnd));
            }
        foreach (var p in nails)
            if (p.x > NailR + 0.2 && p.x < Lu - NailR - 0.2 && p.y > NailR + 0.2 && p.y < Lv - NailR - 0.2)
                lines.Add(WoodFace.Dot(p.x, p.y, NailR));
        var (fe, fl) = Finish(face, etched, lines, over, overLines, plain);
        if (!raise) return (fe, fl, null);
        up.AddRange(topArea);
        // Poles with their edges rounded off.
        if (style == CrateStyle.Lashed) up = Geo.Offset(Geo.Offset(Geo.Union(up), -0.45), 0.45, round: true);
        return Lift(face, fe, fl, up, style == CrateStyle.Ironbound ? "iron" : "boards");
    }

    /// <summary>
    /// The face with what's raised taken off it, and the raised bits as pieces (the ones too small or too
    /// thin to cut stay drawn on the face).
    /// </summary>
    static (PathsD, List<PathD>, Raised?) Lift(PathsD face, PathsD etched, List<PathD> lines, PathsD up, string what, PathsD? upEtched = null, List<PathD>? upLines = null)
    {
        var pieces = Pieces(Geo.Intersect(Geo.Union(up), face)).Where(p => Geo.Area(p) >= 1.2 && Maker.Thin(p) <= 0.05).ToList();
        if (pieces.Count == 0) return (etched, lines, null);
        var area = Geo.Union(new PathsD(pieces.SelectMany(p => p)));
        var rest = Geo.Subtract(face, area);
        return (Geo.Subtract(etched, area), Geo.ClipLines(lines, rest), new Raised(pieces, upEtched ?? etched, upLines ?? lines, what));
    }

    static (PathsD, List<PathD>) Finish(PathsD face, PathsD etched, List<PathD> lines, PathsD over, List<PathD> overLines, PathsD plain)
    {
        var all = Geo.Union(Geo.Union(etched), Geo.Union(over));
        // Lines don't run across what's painted or bolted on (its own lines are added after).
        var clear = Geo.Subtract(face, Geo.Offset(Geo.Union(over, plain), 0.1));
        var scored = Geo.ClipLines(lines, clear);
        scored.AddRange(Geo.ClipLines(overLines, face));
        return (Geo.Intersect(all, face), scored);
    }

    /// <summary>
    /// A sci-fi crate's trim, bolted on proud of the face: a rim round it, latches on an ammo
    /// crate's long sides and a bar handle on its ends; hazard stripes along a cargo crate's long sides.
    /// </summary>
    static (PathsD, List<PathD>, Raised?) MetalTrim(CrateStyle style, Side side, double Lu, double Lv, double e, double fb, PathsD face,
        PathsD etched, List<PathD> lines, PathsD over, List<PathD> overLines)
    {
        var up = new PathsD();
        var upLines = new List<PathD>();
        // A rim round the face, its inside corners chamfered, bolted at the corners.
        double w = Math.Clamp(fb * 0.8, 1.2, 2), c = w * 0.9, arm = w;
        if (Lu - 2 * w < 2 || Lv - 2 * w < 2)
        {
            var (fe0, fl0) = Finish(face, etched, lines, over, overLines, []);
            return (fe0, fl0, null);
        }
        var inner = new PathD
        {
            new(w + c, w), new(Lu - w - c, w), new(Lu - w, w + c), new(Lu - w, Lv - w - c),
            new(Lu - w - c, Lv - w), new(w + c, Lv - w), new(w, Lv - w - c), new(w, w + c),
        };
        up.AddRange(Geo.Subtract(face, [inner]));
        upLines.Add(Geo.Loop(Geo.Offset([inner], 0.3)[0]));
        foreach (double x in new[] { w / 2, Lu - w / 2 })
            foreach (double y in new[] { w / 2, Lv - w / 2 })
                upLines.Add(WoodFace.Dot(x, y, 0.22));
        // The panel inside the rim: a groove round it.
        lines.Add(Geo.Loop(Geo.Offset([inner], -0.5)[0]));
        double inner0 = arm + 0.5, inner1 = Lu - arm - 0.5;
        if (style == CrateStyle.Cargo && side == Side.Long && inner1 - inner0 > 3)
        {
            // Hazard stripes in a band along the bottom.
            double hb = Math.Clamp(Lv * 0.13, 1, 2), b0 = w + 0.5, b1 = b0 + hb, sw = hb * 0.7;
            var band = new PathsD { Geo.Rect(inner0, b0, inner1, b1) };
            var stripes = new PathsD();
            for (double x = inner0 - hb; x < inner1 + hb; x += sw * 2)
                stripes.Add([new(x, b0), new(x + sw, b0), new(x + sw + hb, b1), new(x + hb, b1)]);
            etched.AddRange(Geo.Intersect(stripes, band));
            lines.Add(Geo.Loop(band[0]));
        }
        if (style == CrateStyle.Military && side == Side.Long)
        {
            // Two latches hanging from the top edge.
            double lw = Math.Clamp(Lu * 0.1, 1.4, 2.4), lh = Math.Clamp(Lv * 0.3, 1.8, 3);
            foreach (double cx in new[] { Lu * 0.3, Lu * 0.7 })
            {
                if (cx - lw / 2 < arm + 0.4 || cx + lw / 2 > Lu - arm - 0.4) continue;
                var latch = Geo.Rect(cx - lw / 2, Lv - lh, cx + lw / 2, Lv);
                up.Add(latch);
                upLines.Add(Geo.Loop(Geo.Rect(cx - lw / 2 + 0.3, Lv - lh + 0.3, cx + lw / 2 - 0.3, Lv - 0.3)));
                upLines.Add([new(cx - lw / 2 + 0.3, Lv - lh * 0.55), new(cx + lw / 2 - 0.3, Lv - lh * 0.55)]);
                upLines.Add(WoodFace.Dot(cx, Lv - lh * 0.3, 0.2));
            }
        }
        if (style == CrateStyle.Military && side == Side.End)
        {
            // A bar handle: a loop standing off the end.
            double hw = Math.Min(Lu - 2 * arm - 1, 6), hh = Math.Clamp(Lv * 0.32, 2.8, 3.4), cx = Lu / 2, cy = Lv * 0.55;
            if (hw >= 3.2 && cy + hh / 2 < Lv - e - 0.3)
            {
                var ring = Geo.Subtract([WoodFace.RoundRect(cx - hw / 2, cy - hh / 2, cx + hw / 2, cy + hh / 2, 0.8)], [WoodFace.RoundRect(cx - hw / 2 + 1.2, cy - hh / 2 + 1.2, cx + hw / 2 - 1.2, cy + hh / 2 - 1.2, 0.3)]);
                up.AddRange(ring);
                upLines.AddRange(Geo.Offset(ring, -0.3).Select(Geo.Loop));
            }
        }
        var (fe, fl) = Finish(face, etched, lines, over, overLines, []);
        return Lift(face, fe, fl, up, "trim", [], upLines);
    }

    /// <summary>A carved boss (raised when <paramref name="up"/> is given): outlined, grain kept off it, a dot in the middle.</summary>
    static void Boss(PathsD shape, double cx, double cy, double dot, PathsD? up, PathsD plain, List<PathD> overLines)
    {
        up?.AddRange(shape);
        plain.AddRange(shape);
        overLines.AddRange(Geo.Offset(shape, -0.25).Select(Geo.Loop));
        if (dot > 0.2) overLines.Add(WoodFace.Dot(cx, cy, dot));
    }

    /// <summary>Square iron plates on the four corners, a quatrefoil pierced in each (left unetched).</summary>
    static void Plates(double Lu, double Lv, double e, double fb, PathsD over, List<PathD> overLines, PathsD? up)
    {
        double s = Math.Clamp(e + fb * 1.25, 2.4, 4.5);
        if (2 * s > Math.Min(Lu, Lv) - 1) return;
        foreach (double x in new[] { 0.0, Lu - s })
            foreach (double y in new[] { 0.0, Lv - s })
            {
                var plate = Geo.Rect(x, y, x + s, y + s);
                up?.Add(plate);
                double cx = x + s / 2, cy = y + s / 2, r = s * 0.17;
                var q = Candles.Quatrefoil(cx, cy, r);
                over.AddRange(Geo.Subtract([plate], q));
                overLines.AddRange(q.Select(Geo.Loop));
                foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    overLines.Add(WoodFace.Dot(cx + dx * s * 0.36, cy + dy * s * 0.36, 0.16));
            }
    }

    /// <summary>A rope lashing round a joint of poles: turns of rope across it, both ways.</summary>
    static void Lashing(double cx, double cy, double s, PathsD plain, List<PathD> overLines)
    {
        double h = s / 2;
        plain.Add(Geo.Rect(cx - h, cy - h, cx + h, cy + h));
        int n = 4;
        for (int i = 0; i < n; i++)
        {
            double t = -h + s * (i + 0.5) / n;
            overLines.Add([new(cx - h, cy + t - h * 0.35), new(cx + h, cy + t + h * 0.35)]);
        }
        overLines.Add([new(cx - h * 0.3, cy - h), new(cx + h * 0.3, cy + h)]);
    }

    /// <summary>
    /// A carved lattice of eight-pointed stars inside the frame: the stars left standing, the ground between
    /// them etched away, a ring scored in each.
    /// </summary>
    static void StarLattice(double x0, double y0, double x1, double y1, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 2.5 || h < 2.5) return;
        double inset = 0.35;
        x0 += inset; y0 += inset; x1 -= inset; y1 -= inset; w -= 2 * inset; h -= 2 * inset;
        int ny = Math.Max(1, (int)Math.Round(h / 3.4)), nx = Math.Max(1, (int)Math.Round(w / (h / ny)));
        double cw = w / nx, ch = h / ny, r = Math.Min(cw, ch) * 0.5;
        var stars = new PathsD();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double cx = x0 + (i + 0.5) * cw, cy = y0 + (j + 0.5) * ch;
                stars.Add(Fronts.Star(cx, cy, r * 0.98, r * 0.66, 8));
                if (r >= 1) lines.Add(Geo.Loop(Fronts.Star(cx, cy, r * 0.55, r * 0.37, 8)));
                if (r >= 1.4) lines.Add(WoodFace.Dot(cx, cy, r * 0.16));
            }
        // Diamonds where four cells meet.
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                double cx = x0 + i * cw, cy = y0 + j * ch, d = r * 0.42;
                stars.Add([new(cx, cy - d), new(cx + d, cy), new(cx, cy + d), new(cx - d, cy)]);
            }
        var box = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        etched.AddRange(Geo.Subtract(box, Geo.Offset(Geo.Union(stars), 0.12)));
        lines.Add(Geo.Loop(box[0]));
    }

    /// <summary>
    /// An arcade of pointed arches inside the frame, their insides etched dark; in a gothic crate the middle
    /// arch holds a lily (left standing), otherwise the theme's mark.
    /// </summary>
    static void Arcade(double x0, double y0, double x1, double y1, bool gothic, string? theme, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0, gap = 0.7;
        if (w < 3 || h < 3) return;
        // An odd number, the middle one wider.
        int n = Geo.Clamp((int)Math.Round(w / Math.Max(h * 0.55, 2.4)), 1, 5) | 1;
        double unit = (w - (n + 1) * gap) / (n + (n > 1 ? 0.6 : 0));
        if (unit < 1.2) { n = 1; unit = w - 2 * gap; }
        double next = x0 + gap;
        for (int i = 0; i < n; i++)
        {
            double aw = i == n / 2 && n > 1 ? unit * 1.6 : unit, ax = next;
            next += aw + gap;
            var arch = new PathsD { Fronts.PointedRect(ax, y0 + gap, ax + aw, y1 - gap) };
            lines.AddRange(Geo.Offset(arch, gap * 0.45).Select(Geo.Loop));
            if (i == n / 2 && n % 2 == 1 && aw >= 2.4)
            {
                double cx = ax + aw / 2, cy = y0 + gap + (y1 - y0 - 2 * gap) * 0.42, r = Math.Min(aw * 0.42, h * 0.3);
                var mark = new PathsD();
                var ml = new List<PathD>();
                if (gothic) Pictures.Lily(cx, cy, r, mark, ml);
                else Cabinets.Symbol(FrontLook.Plank, theme, cx, cy, r * 0.8, mark, ml);
                if (gothic)
                {
                    etched.AddRange(Geo.Subtract(arch, Geo.Offset(Geo.Union(mark), 0.15)));
                    continue;
                }
                etched.AddRange(Geo.Subtract(arch, [Geo.Circle(cx, cy, r, 0.01)]));
                lines.Add(WoodFace.Dot(cx, cy, r));
                etched.AddRange(mark);
                lines.AddRange(ml);
                continue;
            }
            etched.AddRange(arch);
        }
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
    static void Brackets(double Lu, double Lv, double T, double fb, PathsD over, List<PathD> overLines, PathsD? up)
    {
        double arm = Math.Clamp(Math.Min(Lu, Lv) * 0.3, 2.2, 7), w = Math.Clamp(fb * 0.75, up is null ? 0.9 : 1.2, 2);
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
                up?.AddRange(plate);
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
    /// <returns>The handle's whole shape (to raise it), or nothing if there's no room.</returns>
    static PathsD RopeHandle(double Lu, double Lv, double e, double clear, PathsD over, bool thick)
    {
        double span = Math.Min((Lu - 2 * clear) / 1.2, 8), cx = Lu / 2, cy = Lv * 0.7, cw = Math.Max(thick ? 1.2 : 0.7, span * 0.16), ch = Math.Max(thick ? 1.4 : 0.9, span * 0.2);
        if (span < 3 || cy + ch / 2 > Lv - e) return [];
        var cleats = new PathsD();
        foreach (double x in new[] { cx - span / 2, cx + span / 2 }) cleats.Add(Geo.Rect(x - cw / 2, cy - ch / 2, x + cw / 2, cy + ch / 2));
        over.AddRange(cleats);
        double sag = Math.Min(span * 0.45, cy - e - 1), th = Math.Max(thick ? 1.15 : 0.35, span * 0.08);
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
        return Geo.Union(rope, cleats);
    }

    /// <summary>
    /// What's heaped in an open crate, seen from the front: apples, tied sacks or rocks between
    /// <paramref name="x0"/> and <paramref name="x1"/>, standing up over the rim at <paramref name="rim"/>.
    /// The shape reaches down a little below the rim (to join the board it stands on); the ones behind
    /// have only the outline that shows past the ones in front.
    /// </summary>
    public static (PathsD Shape, PathsD Etched, List<PathD> Lines) Heap(CrateLoad load, double x0, double x1, double rim, Random rnd)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        double w = x1 - x0;
        if (w < 3 || load == CrateLoad.None) return ([], etched, lines);
        // Front (the row along the rim) and back (the row over it), each one's outline.
        var front = new List<PathsD>();
        var back = new List<PathsD>();
        var backLines = new List<PathD>();
        switch (load)
        {
            case CrateLoad.Apples:
            {
                double r = Geo.Clamp(w / 7, 1.1, 1.9);
                int gaps = Math.Max(1, (int)Math.Ceiling((w - 2 * r) / (1.7 * r)));
                double pitch = (w - 2 * r) / gaps;
                for (int i = 0; i <= gaps; i++) front.Add([Geo.Circle(x0 + r + i * pitch, rim - r * 0.1, r, 0.01)]);
                for (int i = 0; i < gaps; i++) back.Add([Geo.Circle(x0 + r + (i + 0.5) * pitch, rim + r * 0.95, r * 0.96, 0.01)]);
                foreach (var (a, inFront) in front.Select(f => (f, true)).Concat(back.Select(k => (k, false))))
                {
                    var b = Geo.Bounds(a);
                    var into = inFront ? lines : backLines;
                    double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2, ar = (b.right - b.left) / 2;
                    // A stalk in the dimple at the top, a leaf beside it, a shine on the side.
                    into.Add([new(cx, cy + ar * 0.6), new(cx + ar * 0.15, cy + ar * 0.92)]);
                    etched.Add(Rugs.Ellipse(cx + ar * 0.42, cy + ar * 0.72, ar * 0.26, ar * 0.12));
                    into.Add([.. Geo.Arc(cx, cy, ar * 0.62, Math.PI * 0.6, Math.PI * 0.95)]);
                }
                break;
            }
            case CrateLoad.Sacks:
            {
                double bw = Geo.Clamp(w / 2.4, 2.6, 4.6);
                int n = Math.Max(1, (int)Math.Ceiling((w - bw) / (bw * 0.8)) + 1);
                double pitch = n > 1 ? (w - bw) / (n - 1) : 0;
                for (int i = 0; i < n; i++)
                {
                    double x = x0 + i * pitch, cx = x + bw / 2, b0 = rim - 1, top = rim + bw * (0.85 + 0.25 * rnd.NextDouble()), hgt = top - b0;
                    double neck = b0 + hgt * 0.74, nw = Math.Max(1.15, bw * 0.28);
                    var sack = Geo.Solid([[new(x + 0.1, b0), new(x + bw - 0.1, b0), new(x + bw, b0 + hgt * 0.4), new(cx + nw / 2 + 0.25, neck - 0.3), new(cx + nw / 2, neck),
                        new(cx + nw / 2 + 0.45, top), new(cx - nw / 2 - 0.45, top), new(cx - nw / 2, neck), new(cx - nw / 2 - 0.25, neck - 0.3), new(x, b0 + hgt * 0.4)]]);
                    (i % 2 == 0 ? front : back).Add(sack);
                    var into = i % 2 == 0 ? lines : backLines;
                    // The tie round the neck and a fold or two in the cloth.
                    into.Add([new(cx - nw / 2 - 0.1, neck + 0.25), new(cx + nw / 2 + 0.1, neck + 0.25)]);
                    into.Add([new(cx - bw * 0.22, b0 + hgt * 0.2), new(cx - bw * 0.08, neck - 0.4)]);
                    into.Add([new(cx + bw * 0.25, b0 + hgt * 0.3), new(cx + bw * 0.12, neck - 0.5)]);
                }
                break;
            }
            case CrateLoad.Treasure:
            {
                // A heap of gold coins mounding up over the rim, a crown sitting on it and a goblet standing at
                // one side, gems glinting in the heap.
                double xc = (x0 + x1) / 2, ry = Geo.Clamp(w * 0.2, 1.6, 3.6), cr = Geo.Clamp(w * 0.045, 0.55, 0.9);
                var mound = new PathsD { Geo.Intersect([Rugs.Ellipse(xc, rim - 0.6, w / 2 - cr, ry + 0.6)], [Geo.Rect(x0, rim - 1.2, x1, rim + ry + 1)])[0] };
                // The heap's edge lumpy with coins.
                for (double t = 0.04; t < 0.97; t += 0.07)
                {
                    double a = Math.PI * t, cx = xc + (w / 2 - cr) * Math.Cos(a), cy = rim - 0.6 + (ry + 0.6) * Math.Sin(a);
                    if (cy < rim - 0.2) continue;
                    mound.Add(Geo.Circle(cx, cy, cr, 0.01));
                }
                var heap = Geo.Solid(mound);
                front.Add(heap);
                for (int i = 0; i < (int)(w * 1.4); i++)
                {
                    double px = x0 + cr + rnd.NextDouble() * (w - 2 * cr), py = rim - 0.8 + rnd.NextDouble() * ry;
                    if (!OutlineHas(heap, px, py, cr)) continue;
                    lines.Add(WoodFace.Dot(px, py, cr * 0.55));
                }
                foreach (var (gx, gy) in new[] { (xc - w * 0.18, rim + ry * 0.3), (xc + w * 0.22, rim + ry * 0.15) })
                    if (OutlineHas(heap, gx, gy, 0.6)) etched.Add(new PathD { new(gx, gy - 0.5), new(gx + 0.4, gy), new(gx, gy + 0.5), new(gx - 0.4, gy) });
                // The crown, behind on top of the heap: a band with three points, each with a pearl.
                double kw = Geo.Clamp(w * 0.32, 2.8, 5), kx = xc - w * 0.05, ky = rim + ry * 0.75, kb = Math.Max(1.0, kw * 0.26);
                var crown = new PathsD { Geo.Rect(kx - kw / 2, ky, kx + kw / 2, ky + kb) };
                var pearls = new List<PathD>();
                foreach (var f in new[] { -0.5, 0.0, 0.5 })
                {
                    double px = kx + f * (kw - 1.6), ph = Math.Max(1.4, kb * (f == 0 ? 1.6 : 1.2));
                    crown.Add(new PathD { new(px - 0.9, ky + kb - 0.01), new(px + 0.9, ky + kb - 0.01), new(px, ky + kb + ph) });
                    pearls.Add(WoodFace.Dot(px, ky + kb + ph - 0.75, 0.25));
                }
                var crownS = Geo.Offset(Geo.Offset(Geo.Solid(crown), -0.5), 0.5, round: true);
                backLines.AddRange(pearls);
                back.Add(crownS);
                backLines.Add([new(kx - kw / 2, ky + kb * 0.5), new(kx + kw / 2, ky + kb * 0.5)]);
                foreach (var f in new[] { -0.3, 0.0, 0.3 }) etched.Add(Geo.Circle(kx + f * kw, ky + kb * 0.5, 0.3, 0.01));
                // A goblet standing at the side.
                double gx0 = x1 - Math.Max(1.6, w * 0.12), gt = rim + ry * 0.6 + 1.6;
                if (w >= 9)
                {
                    var gob = Geo.Solid([
                        new PathD { new(gx0 - 1.3, gt), new(gx0 + 1.3, gt), new(gx0 + 0.9, gt - 1.6), new(gx0 - 0.9, gt - 1.6) },
                        Geo.Rect(gx0 - 0.55, rim - 0.5, gx0 + 0.55, gt - 1.4),
                        WoodFace.RoundRect(gx0 - 1.0, rim - 0.6, gx0 + 1.0, rim + 0.1, 0.3) ]);
                    back.Add(gob);
                    backLines.Add([new(gx0 - 1.15, gt - 0.5), new(gx0 + 1.15, gt - 0.5)]);
                }
                break;
            }
            case CrateLoad.Rocks:
            {
                double rr = Geo.Clamp(w / 6, 1.2, 2.2);
                int gaps = Math.Max(1, (int)Math.Ceiling((w - 2 * rr) / (1.6 * rr)));
                double pitch = (w - 2 * rr) / gaps;
                for (int i = 0; i <= gaps; i++) front.Add(Rock(x0 + rr + i * pitch, rim - rr * 0.1, rr, rnd, lines));
                for (int i = 0; i < gaps; i++) back.Add(Rock(x0 + rr + (i + 0.5) * pitch, rim + rr * 0.9, rr * 0.9, rnd, backLines));
                break;
            }
        }
        var frontAll = Geo.Union(new PathsD(front.SelectMany(p => p)));
        var shape = Geo.Union(Geo.Union(frontAll, Geo.Union(new PathsD(back.SelectMany(p => p)))), [Geo.Rect(x0, rim - 1.2, x1, rim)]);
        // Inside each one only what's its own: the back ones' detail stops at the front ones.
        var outlines = new List<PathD>();
        foreach (var f in front) outlines.AddRange(f.Select(Geo.Loop));
        var showing = Geo.Subtract(shape, frontAll);
        outlines.AddRange(Geo.ClipLines([.. back.SelectMany(b => b.Select(Geo.Loop)), .. backLines], showing));
        outlines.AddRange(Geo.ClipLines(lines, shape));
        return (shape, Geo.Intersect(Geo.Union(etched), shape), outlines);
    }

    /// <summary>Whether a circle of radius r at (x, y) lies inside the area.</summary>
    static bool OutlineHas(PathsD area, double x, double y, double r) => Geo.Area(Geo.Subtract([Geo.Circle(x, y, r, 0.05)], area)) < 1e-3;

    /// <summary>A rock: a lumpy polygon with a facet line or two across it.</summary>
    static PathsD Rock(double cx, double cy, double r, Random rnd, List<PathD> lines)
    {
        var p = new PathD();
        int n = 7;
        double a0 = rnd.NextDouble() * Math.PI;
        for (int i = 0; i < n; i++)
        {
            double a = a0 + i * 2 * Math.PI / n + (rnd.NextDouble() - 0.5) * 0.4, rr = r * (0.8 + 0.2 * rnd.NextDouble());
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        var c = new PointD(cx + r * 0.15, cy + r * 0.2);
        lines.Add([p[1], c, p[4]]);
        lines.Add([c, new PointD((p[6].x + c.x) / 2 + r * 0.1, (p[6].y + c.y) / 2)]);
        return Geo.Solid([p]);
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

    /// <summary>Each separate piece of some areas, with its holes (an island in a hole is a piece of its own).</summary>
    static IEnumerable<PathsD> Pieces(PathsD a)
    {
        if (a.Count == 0) yield break;
        var c = new ClipperD(4);
        c.AddSubject(a);
        var tree = new PolyTreeD();
        c.Execute(ClipType.Union, FillRule.EvenOdd, tree);
        var todo = new Stack<PolyPathD>();
        for (int i = 0; i < tree.Count; i++) todo.Push(tree[i]);
        while (todo.Count > 0)
        {
            var outer = todo.Pop();
            if (Math.Abs(Clipper.Area(outer.Polygon!)) <= 1e-4) continue;
            var piece = new PathsD { outer.Polygon! };
            for (int j = 0; j < outer.Count; j++)
            {
                var hole = outer[j];
                piece.Add(hole.Polygon!);
                for (int k = 0; k < hole.Count; k++) todo.Push(hole[k]);
            }
            yield return piece;
        }
    }
}
