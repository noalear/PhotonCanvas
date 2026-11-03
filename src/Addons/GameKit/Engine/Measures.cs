using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// Movement gauges, range rulers and area templates: marked in inches, centimetres, grid squares or a
/// unit of your own, with the numbers, title, border and ornaments their <see cref="Markings"/> say.
/// </summary>
public static class Measures
{
    /// <summary>The movement gauge's joint: the pin's stem (square-ish, turning in a round hole) and the hole.</summary>
    public sealed record PinPlan(double StemW, double Hole, double HeadW, double HeadH, double CapR);

    public static PinPlan Pin(double T, double fit)
    {
        double pw = Math.Max(T, 2.5), hole = Math.Sqrt(pw * pw + T * T) + Math.Max(fit, 0) + 0.25;
        return new(pw, hole, hole + 3, Math.Max(2, T), hole / 2 + 1.8);
    }

    /// <summary>
    /// A movement gauge: segments on two layers, each joined to the next at a unit mark by a pin (a T,
    /// its stem through both segments, a cap glued on its end underneath) so it bends round scenery.
    /// The ends are square: the gauge is exactly its segments long. Each segment shows the distance to
    /// its far end; the first can carry a title instead, and the ends an ornament.
    /// </summary>
    public static void Gauge(Maker m)
    {
        var it = m.Item;
        var mk = it.Marks;
        double T = m.T, wd = it.WidthMm, U = Defaults.UnitMm(it), P = it.StepUnits * U;
        int n = it.Segments;
        if (n < 2 || n > 40) { m.Bad("A movement gauge has 2 to 40 segments."); return; }
        var pin = Pin(T, m.Fit);
        double minW = pin.Hole + 2 * (Maker.Bridge + 0.4);
        if (wd < minW) { m.Bad($"The gauge is too narrow for its pins in a {T:0.##} mm sheet: make it at least {minW:0.#} mm wide."); return; }
        if (P < wd + 1) { m.Bad($"The segments are too short for their width: make each at least {(wd + 1) / U:0.##} {Defaults.UnitWord(it, (wd + 1) / U > 1)} long, or the gauge narrower."); return; }

        var caps = new List<Pose>();
        var pinPoses = new List<Pose>();
        for (int i = 0; i + 1 < n; i++)
        {
            double px = (i + 1) * P;
            pinPoses.Add(Pose.Front(px - pin.HeadW / 2, -T, 0));
            caps.Add(Pose.Up(px, -T / 2, 0));
        }
        bool bordered = mk.Border != BorderStyle.None;
        double inset = Math.Clamp(wd * 0.06, 0.5, 1.0), band = Math.Clamp(wd * 0.13, 0.9, 2.4);
        string title = mk.Title.Trim();
        double small = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            // Even segments are on the lower layer, odd ones on the upper.
            bool lower = i % 2 == 0;
            var seg = m.Plate($"Segment {i + 1}", Pose.Up(i * P, T / 2 + (i % 2) * T, 0));
            var body = new PathsD { Geo.Rect(0, -wd / 2, P, wd / 2) };
            if (i > 0) body.Add(Geo.Circle(0, 0, wd / 2, 0.01));
            if (i + 1 < n) body.Add(Geo.Circle(P, 0, wd / 2, 0.01));
            seg.Body.AddRange(Geo.Union(body));
            var holes = new PathsD();
            if (i > 0) holes.Add(Geo.Circle(0, 0, pin.Hole / 2, 0.01));
            if (i + 1 < n) holes.Add(Geo.Circle(P, 0, pin.Hole / 2, 0.01));
            seg.Holes.AddRange(holes);

            // What shows: a lower segment's ends are under the round ends of the segments over it.
            var face = Geo.Subtract(Geo.Union(body), holes);
            if (lower)
            {
                var over = new PathsD();
                if (i > 0) over.Add(Geo.Circle(0, 0, wd / 2 + 0.15, 0.01));
                if (i + 1 < n) over.Add(Geo.Circle(P, 0, wd / 2 + 0.15, 0.01));
                face = Geo.Subtract(face, over);
            }
            var ink = new Ink(mk, m.Set.Font, m);
            if (bordered) ink.Border(face, inset, band, m.Set.Seed * 31 + i);

            // The marks go between the joints (and in from the free ends), inside the border.
            double edge = bordered ? inset + band + 0.55 : Math.Max(1, wd * 0.2);
            double joint = lower ? wd / 2 + 0.15 + (bordered ? inset + band + 0.5 : 0.6) : pin.Hole / 2 + 1.2;
            double a = i > 0 ? joint : edge, b = i + 1 < n ? P - joint : P - edge;
            double boxH = Math.Min(wd - 2 * edge, wd * 0.55 * Math.Clamp(mk.TextScale, 0.5, 1.5));
            if (b - a < 1 || boxH < 0.8) continue;
            small = Math.Min(small, boxH);

            var cells = new List<Cell>();
            bool first = i == 0, last = i + 1 == n, titled = first && title.Length > 0;
            bool every = mk.Every <= 1 || (i + 1) % mk.Every == 0 || last;
            var number = !titled && every ? NumberCell(ink, it, (i + 1) * it.StepUnits) : null;
            bool ends = ink.HasOrnament && mk.Place == OrnamentPlace.Ends;
            if (ends && first) cells.Add(OrnCell(ink, pointLeft: true));
            if (titled)
            {
                if (ink.HasOrnament && mk.Place == OrnamentPlace.Title) cells.Add(OrnCell(ink, pointLeft: true));
                cells.Add(TextCell(ink, title));
                if (ink.HasOrnament && mk.Place == OrnamentPlace.Title) cells.Add(OrnCell(ink));
            }
            if (ink.HasOrnament && mk.Place == OrnamentPlace.EveryNumber && (number is not null || mk.Numbers == Numbering.None)) cells.Add(OrnCell(ink));
            if (number is not null) cells.Add(number);
            if (ends && last) cells.Add(OrnCell(ink));
            Row(ink, cells, (a + b) / 2, 0, b - a, boxH);
            ink.Into(seg, keepOut: Geo.Offset(holes, 0.6, true));
        }
        if (bordered && small < 2.5 && small < double.MaxValue) m.Warn($"The numbers come out only {small:0.#} mm high inside the border: make the gauge wider, or leave the border off.");

        var cap = m.Plate("Pin cap", [.. caps]);
        cap.Body.Add(Geo.Circle(0, 0, pin.CapR, 0.01));
        var pinPlate = m.Plate("Pin", [.. pinPoses]);
        double s0 = pin.HeadW / 2 - pin.StemW / 2, s1 = pin.HeadW / 2 + pin.StemW / 2;
        pinPlate.Body.Add(Geo.Rect(s0, 0, s1, 3 * T + 0.01));
        pinPlate.Body.Add(Geo.Rect(0, 3 * T, pin.HeadW, 3 * T + pin.HeadH));
        m.Tab(pinPlate, Geo.Rect(s0, 0, s1, T), cap);
        m.Note($"{n} segments of {Defaults.Mark(it, it.StepUnits)} {Defaults.UnitWord(it, it.StepUnits != 1)}, {n * P:0.#} mm in all. Stack each joint (odd segments under, even over), push a pin down through it and glue a cap on the stem underneath: glue only the cap, so the joint still turns.");
        if (title.Length > 0) m.Note($"\"{title}\" is on the first segment, in place of its number.");
    }

    /// <summary>A straight ruler: ticks along its top edge, numbers under the whole units, and a band along the bottom for the unit, a title and ornaments.</summary>
    public static void Ruler(Maker m)
    {
        var it = m.Item;
        var mk = it.Marks;
        double U = Defaults.UnitMm(it), L = it.LengthUnits * U, w = it.WidthMm;
        if (it.LengthUnits <= 0 || L < 20) { m.Bad("Make the ruler at least 20 mm long."); return; }
        if (w < 8) { m.Bad("Make the ruler at least 8 mm wide."); return; }
        var r = m.Plate("Ruler", Pose.Up(0, m.T / 2, w));
        r.Body.Add(Geo.Rect(0, 0, L, w));
        var ink = new Ink(mk, m.Set.Font, m);
        double scale = Math.Clamp(mk.TextScale, 0.5, 1.5);

        // Ticks down from the top edge.
        int sub = TickParts(it);
        double big = w * 0.36, textH = Math.Min(w * 0.24, U * 0.45) * scale;
        int steps = (int)Math.Floor(L / U * sub + 1e-6);
        for (int k = 1; k <= steps; k++)
        {
            double x = k * U / sub;
            if (x > L - 0.3) break;
            bool whole = k % sub == 0, half = sub % 2 == 0 && k % (sub / 2) == 0, quarter = sub % 4 == 0 && k % (sub / 4) == 0;
            double len = whole ? big : half ? w * 0.26 : quarter ? w * 0.19 : w * 0.13;
            r.Lines.Add([new(x, w + 0.5), new(x, w - len)]);
            if (!whole) continue;
            int unit = k / sub;
            if (mk.Every > 1 && unit % mk.Every != 0) continue;
            var cells = new List<Cell>();
            if (ink.HasOrnament && mk.Place == OrnamentPlace.EveryNumber) cells.Add(OrnCell(ink));
            if (NumberCell(ink, it, unit) is { } num) cells.Add(num);
            Row(ink, cells, x, w - big - 0.8 - textH / 2, U * Math.Max(1, mk.Every) * 0.8, textH);
        }

        // The band along the bottom: the unit at the left, the title in the middle, ornaments at the ends.
        double top = w - big - 0.8 - textH - 0.6;
        bool bordered = mk.Border != BorderStyle.None;
        double inset = 0.6, band = Math.Clamp(top * 0.13, 0.6, 1.8);
        if (top >= 2.5)
        {
            var region = new PathsD { Geo.Rect(0, 0, L, top) };
            if (bordered) ink.Border(region, inset, band, m.Set.Seed * 17 + 3);
            double edge = bordered ? inset + band + 0.45 : 0.9;
            double h = top - 2 * edge;
            if (h >= 1)
            {
                double x0 = edge, x1 = L - edge, cy = top / 2, gap = h * 0.35;
                string title = mk.Title.Trim();
                bool ends = ink.HasOrnament && mk.Place == OrnamentPlace.Ends;
                if (ends)
                {
                    double ow = Math.Min(ink.OrnamentWidth(h), L * 0.15);
                    ink.Ornament(x0 + ow / 2, cy, ow, h, pointLeft: true);
                    ink.Ornament(x1 - ow / 2, cy, ow, h);
                    x0 += ow + gap;
                    x1 -= ow + gap;
                }
                if (mk.ShowUnit)
                {
                    string unit = Defaults.UnitWord(it);
                    double uh = h * 0.8, uw = Math.Min(ink.Width(unit, uh), Math.Max(0, (x1 - x0) * (title.Length > 0 ? 0.3 : 1)));
                    if (uw > 0.5)
                    {
                        ink.Text(unit, x0 + uw / 2, cy, uw, uh);
                        x0 += uw + gap;
                    }
                }
                if (title.Length > 0 && x1 - x0 > 2)
                {
                    var cells = new List<Cell>();
                    bool flank = ink.HasOrnament && mk.Place == OrnamentPlace.Title;
                    if (flank) cells.Add(OrnCell(ink, pointLeft: true));
                    cells.Add(TextCell(ink, title));
                    if (flank) cells.Add(OrnCell(ink));
                    double want = RowWidth(cells, h);
                    // In the middle of the ruler if there's room, else in the middle of what's left.
                    double cx = Math.Clamp(L / 2, x0 + Math.Min(want, x1 - x0) / 2, x1 - Math.Min(want, x1 - x0) / 2);
                    Row(ink, cells, cx, cy, x1 - x0, h);
                }
            }
            else if (mk.Title.Trim().Length > 0 || bordered) m.Warn("There's no room for the title and border along the bottom: make the ruler wider.");
        }
        ink.Into(r);
        m.Note($"{Defaults.Mark(it, it.LengthUnits)} {Defaults.UnitWord(it, it.LengthUnits != 1)}: {L:0.#} mm long.");
    }

    /// <summary>How many parts of a unit the ruler's finest ticks mark (none closer than 0.6 mm).</summary>
    public static int TickParts(AccessoryItem it)
    {
        double U = Defaults.UnitMm(it);
        int sub = it.Marks.Ticks > 0 ? Math.Clamp(it.Marks.Ticks, 1, 32) : it.Units switch
        {
            Units.Centimetres => 10,
            Units.Inches => 4,
            Units.GridFeet => 2,
            _ => U >= 15 ? 4 : 2,
        };
        while (sub > 1 && U / sub < 0.6) sub = sub % 2 == 0 ? sub / 2 : 1;
        return sub;
    }

    /// <summary>An area template: a circle with a ring at every unit, a cone (as wide as it is far, at every distance) or a square with a grid.</summary>
    public static void Template(Maker m)
    {
        var it = m.Item;
        var mk = it.Marks;
        double U = Defaults.UnitMm(it), S = it.LengthUnits * U;
        if (it.LengthUnits <= 0 || S < 15) { m.Bad("Make the template at least 15 mm."); return; }
        int whole = (int)Math.Floor(it.LengthUnits + 1e-6);
        double scale = Math.Clamp(mk.TextScale, 0.5, 1.5);
        double textH = Math.Clamp(U * 0.3, 3, 7) * scale;
        var lines = new List<PathD>();
        var ink = new Ink(mk, m.Set.Font, m);
        string title = mk.Title.Trim();
        bool bordered = mk.Border != BorderStyle.None;
        double inset = 0.8;
        PathsD? keepOut = null;
        Plate t;
        bool Shown(int k) => mk.Every <= 1 || k % mk.Every == 0 || k == whole;
        switch (it.Shape)
        {
            case TemplateShape.Circle:
            {
                t = m.Plate("Circle template", Pose.Up(S, m.T / 2, S));
                t.Body.Add(Geo.Circle(S, S, S, 0.02));
                // A hole in the middle to see the model it's centred on.
                double hole = Math.Min(4, S * 0.08);
                t.Holes.Add(Geo.Circle(S, S, hole, 0.01));
                keepOut = [Geo.Circle(S, S, hole + 0.6, 0.01)];
                double band = Math.Clamp(S * 0.045, 0.8, 2.4);
                if (bordered) ink.Border([Geo.Circle(S, S, S, 0.02)], inset, band, m.Set.Seed * 13 + 1);
                double rim = bordered ? inset + band + 0.5 : 1.0;
                double inner = hole + 1;
                for (int k = 1; k <= whole; k++)
                {
                    double rk = k * U;
                    if (rk < S - 0.3) { lines.Add(Geo.Loop(Geo.Circle(S, S, rk, 0.02))); inner = rk; }
                    if (rk - textH > hole + 1 && Shown(k))
                    {
                        // Just inside its ring, on the right.
                        var cells = new List<Cell>();
                        if (ink.HasOrnament && mk.Place == OrnamentPlace.EveryNumber) cells.Add(OrnCell(ink));
                        if (NumberCell(ink, it, k) is { } num) cells.Add(num);
                        // Clear of the ring where it curves in above and below the number.
                        double ring = Math.Min(rk, S - rim), right = Math.Sqrt(Math.Max(0, ring * ring - textH * textH / 4)) - 0.9, rw = Math.Min(RowWidth(cells, textH), U * 0.9);
                        Row(ink, cells, S + right - rw / 2, S, rw, textH);
                    }
                }
                // The outer band (between the last ring and the rim): the title round the top, ornaments round the bottom.
                double rIn = inner + 0.8, rOut = S - rim;
                if (rOut - rIn >= 2)
                {
                    double th = Math.Min((rOut - rIn) * 0.62, 8 * scale), rc = (rIn + rOut) / 2;
                    double used = 0;
                    if (title.Length > 0)
                    {
                        var (ta, tl) = ink.Fitted(title, rc * 150 * Math.PI / 180, th);
                        var all = new PathsD([.. ta, .. tl]);
                        if (all.Count > 0)
                        {
                            var bb = Geo.Bounds(all);
                            used = (bb.right - bb.left) / rc;
                            ink.Areas.AddRange(OnArc(ta, S, S, rc, 90, true));
                            ink.Lines.AddRange(OnArc(tl, S, S, rc, 90, false));
                        }
                    }
                    if (ink.HasOrnament)
                    {
                        double os = Math.Min(rOut - rIn - 0.6, th * 1.2);
                        double ow = ink.OrnamentWidth(os);
                        var angles = new List<double>();
                        if (mk.Place == OrnamentPlace.Title && used > 0)
                        {
                            double off = (used / 2 + (ow * 0.5 + th * 0.4) / rc) * 180 / Math.PI;
                            angles.AddRange([90 + off, 90 - off]);
                        }
                        else if (mk.Place == OrnamentPlace.Ends || (mk.Place == OrnamentPlace.Title && used == 0))
                        {
                            angles.AddRange(title.Length > 0 ? [225, 315, 270] : [45, 135, 225, 315]);
                        }
                        foreach (var deg in angles)
                        {
                            double a = deg * Math.PI / 180;
                            // Turned with the circle, its top outward above the middle and inward below it, so none is upside down.
                            double turn = deg <= 180 ? deg - 90 : deg + 90;
                            ink.Ornament(S + rc * Math.Cos(a), S + rc * Math.Sin(a), ow, os, pointLeft: deg > 90 && deg < 270, deg: turn);
                        }
                    }
                }
                else if (title.Length > 0) m.Warn("There's no room round the edge for the title: make the template bigger or the numbers smaller.");
                break;
            }
            case TemplateShape.Cone:
            {
                // From its point (where the caster stands) out: as wide at each distance as it is far.
                t = m.Plate("Cone template", Pose.Up(0, m.T / 2, 0));
                var tri = new PathD { new(0, 0), new(S, -S / 2), new(S, S / 2) };
                t.Body.Add(tri);
                double band = Math.Clamp(S * 0.035, 0.8, 2.2);
                if (bordered) ink.Border([tri], inset, band, m.Set.Seed * 13 + 2);
                double rim = bordered ? inset + band + 0.5 : 1.0;
                double lastLine = 0;
                for (int k = 1; k <= whole; k++)
                {
                    double x = k * U;
                    if (x < S - 0.3) { lines.Add([new(x, -x / 2 - 1), new(x, x / 2 + 1)]); lastLine = x; }
                    if (x * 0.5 > textH && Shown(k))
                    {
                        var cells = new List<Cell>();
                        if (ink.HasOrnament && mk.Place == OrnamentPlace.EveryNumber) cells.Add(OrnCell(ink));
                        if (NumberCell(ink, it, k) is { } num) cells.Add(num);
                        double right = Math.Min(x, S - rim) - 0.7, rw = Math.Min(RowWidth(cells, textH), x * 0.45);
                        Row(ink, cells, right - rw / 2, 0, rw, textH);
                    }
                }
                lines.Add([new(-1, 0), new(S + 1, 0)]);
                // The last band: the title above the middle line, an ornament below it.
                double xa = lastLine + 1.2, xb = S - rim, y0 = textH / 2 + 0.8, y1 = xa * 0.5 - rim * 1.2;
                if (xb - xa > 3 && y1 - y0 > 1.5)
                {
                    double h = Math.Min(y1 - y0, textH * 1.2), cy = (y0 + y1) / 2;
                    if (title.Length > 0)
                    {
                        var cells = new List<Cell>();
                        bool flank = ink.HasOrnament && mk.Place == OrnamentPlace.Title;
                        if (flank) cells.Add(OrnCell(ink, pointLeft: true));
                        cells.Add(TextCell(ink, title));
                        if (flank) cells.Add(OrnCell(ink));
                        Row(ink, cells, (xa + xb) / 2, cy, xb - xa, h);
                    }
                    if (ink.HasOrnament && mk.Place != OrnamentPlace.EveryNumber && !(mk.Place == OrnamentPlace.Title && title.Length > 0))
                        ink.Ornament((xa + xb) / 2, -cy, xb - xa, h);
                }
                else if (title.Length > 0) m.Warn("There's no room for the title past the last line: make the template longer.");
                break;
            }
            default:
            {
                t = m.Plate("Square template", Pose.Up(0, m.T / 2, S));
                var sq = Geo.Rect(0, 0, S, S);
                t.Body.Add(sq);
                double band = Math.Clamp(S * 0.035, 0.8, 2.2);
                if (bordered) ink.Border([sq], inset, band, m.Set.Seed * 13 + 3);
                for (int k = 1; k <= whole; k++)
                {
                    double x = k * U;
                    if (x >= S - 0.3) break;
                    lines.Add([new(x, -1), new(x, S + 1)]);
                    lines.Add([new(-1, x), new(S + 1, x)]);
                }
                double h = textH * 1.4;
                var cells = new List<Cell>();
                if (ink.HasOrnament && mk.Place == OrnamentPlace.EveryNumber) cells.Add(OrnCell(ink));
                if (NumberCell(ink, it, it.LengthUnits) is { } num) cells.Add(num);
                Row(ink, cells, S / 2, S / 2, Math.Min(U * 0.8, S * 0.5), h);
                if (title.Length > 0)
                {
                    var tc = new List<Cell>();
                    bool flank = ink.HasOrnament && mk.Place == OrnamentPlace.Title;
                    if (flank) tc.Add(OrnCell(ink, pointLeft: true));
                    tc.Add(TextCell(ink, title));
                    if (flank) tc.Add(OrnCell(ink));
                    double th = textH * 0.9;
                    Row(ink, tc, S / 2, S / 2 - h / 2 - 1 - th / 2, S * 0.8, th);
                }
                if (ink.HasOrnament && (mk.Place == OrnamentPlace.Ends || (mk.Place == OrnamentPlace.Title && title.Length == 0)))
                {
                    double cell = Math.Min(U, S / 4), c = cell / 2 + (bordered ? inset + band : 0) * 0.5, os = cell * 0.6;
                    foreach (var (x, y) in new[] { (c, c), (S - c, c), (c, S - c), (S - c, S - c) })
                        ink.Ornament(x, y, os, os, pointLeft: x < S / 2);
                }
                break;
            }
        }
        ink.Into(t, lines, keepOut);
        t.Lines.AddRange(lines);
        m.Note($"A {Defaults.Mark(it, it.LengthUnits)} {(it.Units == Units.GridFeet ? "ft" : Defaults.UnitWord(it, false))} {it.Shape.ToString().ToLowerInvariant()}{(it.Shape == TemplateShape.Circle ? " (radius)" : "")}: {(it.Shape == TemplateShape.Circle ? 2 * S : S):0.#} mm across.");
    }

    /// <summary>
    /// A plaque <paramref name="w"/> × <paramref name="h"/> marked as <paramref name="mk"/> says (border, an ornament
    /// each end and <paramref name="number"/> in the middle): the pictures in the window's lists of designs.
    /// </summary>
    public static (PathsD Cut, PathsD Etched, List<PathD> Lines) Sample(Markings mk, string font, double w, double h, double? number = 3)
    {
        var region = new PathsD { Geo.Rect(0, 0, w, h) };
        var ink = new Ink(mk, font);
        bool bordered = mk.Border != BorderStyle.None;
        double inset = Math.Clamp(h * 0.06, 0.5, 1.0), band = Math.Clamp(h * 0.13, 0.9, 2.4);
        if (bordered) ink.Border(region, inset, band, 7);
        double edge = bordered ? inset + band + 0.55 : Math.Max(1, h * 0.2);
        var cells = new List<Cell>();
        var it = new AccessoryItem { Kind = AccessoryKind.MoveGauge, Marks = mk };
        if (ink.HasOrnament) cells.Add(OrnCell(ink, pointLeft: true));
        if (number is { } v && NumberCell(ink, it, v) is { } num) cells.Add(num);
        if (ink.HasOrnament && number is not null) cells.Add(OrnCell(ink));
        Row(ink, cells, w / 2, h / 2, w - 2 * edge, Math.Min(h - 2 * edge, h * 0.55));
        var p = new Plate { Name = "Sample", Poses = [] };
        ink.Into(p);
        return (region, Geo.Intersect(Geo.Union(p.Etched), region), Geo.ClipLines(p.Lines, region));
    }

    // ---- laying marks out in a row ----

    /// <summary>One thing in a row of marks: how wide it is at a height, and drawing it centred at a point.</summary>
    sealed record Cell(Func<double, double> Width, Action<double, double, double, double> Draw);

    /// <summary>The distance as the markings write it: a number (with its unit), Roman numerals, dice pips or tally marks; null for none.</summary>
    static Cell? NumberCell(Ink ink, AccessoryItem it, double units)
    {
        var mk = it.Marks;
        double shown = it.Units == Units.GridFeet ? units * 5 : it.Units == Units.Custom ? units * (it.UnitCounts > 0 ? it.UnitCounts : 1) : units;
        bool integer = Math.Abs(units - Math.Round(units)) < 1e-6, shownInt = Math.Abs(shown - Math.Round(shown)) < 1e-6;
        switch (mk.Numbers)
        {
            case Numbering.None:
                return null;
            case Numbering.Pips when integer && Math.Round(units) is >= 1 and <= 9:
            {
                int n = (int)Math.Round(units);
                return new Cell(h => h, (cx, cy, w, h) => ink.Areas.AddRange(Geo.Move(Ink.Pips(n, h), cx, cy)));
            }
            case Numbering.Tally when integer && Math.Round(units) is >= 1 and <= 25:
            {
                int n = (int)Math.Round(units);
                return new Cell(h => { Ink.Tally(n, h, out var tw); return tw; }, (cx, cy, w, h) =>
                {
                    var marks = Ink.Tally(n, h, out _);
                    var moved = Geo.Map(marks, q => new PointD(q.x + cx, q.y + cy));
                    if (mk.Look == MarkLook.Engraved) ink.Areas.AddRange(Clipper.InflatePaths(new PathsD(moved), Math.Clamp(h * 0.05, 0.15, 0.6), JoinType.Round, EndType.Round, 2, 4));
                    else ink.Lines.AddRange(moved);
                });
            }
            case Numbering.Roman when shownInt && shown >= 1:
                return TextCell(ink, MarkDesigns.Roman((int)Math.Round(shown)));
            case Numbering.WithUnit:
                return TextCell(ink, Defaults.Mark(it, units) + Defaults.UnitSuffix(it));
            default:
                return TextCell(ink, Defaults.Mark(it, units));
        }
    }

    static Cell TextCell(Ink ink, string text)
    {
        // Text is as wide as it is high times this (measured once, at 10 mm).
        double ratio = -1;
        double R() => ratio >= 0 ? ratio : ratio = ink.Width(text, 10) / 10;
        return new Cell(h => R() * h, (cx, cy, w, h) => ink.Text(text, cx, cy, w * 1.02 + 0.01, h));
    }

    static Cell OrnCell(Ink ink, bool pointLeft = false) =>
        new(h => ink.OrnamentWidth(h), (cx, cy, w, h) => ink.Ornament(cx, cy, w, h, pointLeft));

    const double Gap = 0.25;

    static double RowWidth(List<Cell> cells, double h) => cells.Count == 0 ? 0 : cells.Sum(c => c.Width(h)) + Gap * h * (cells.Count - 1);

    /// <summary>The cells side by side, centred on (cx, cy), as high as <paramref name="maxH"/> and made smaller to fit <paramref name="maxW"/>.</summary>
    static void Row(Ink ink, List<Cell> cells, double cx, double cy, double maxW, double maxH)
    {
        if (cells.Count == 0 || maxW <= 0.5 || maxH <= 0.5) return;
        double h = maxH, w = RowWidth(cells, h);
        if (w > maxW && w > 0) { h *= maxW / w; w = RowWidth(cells, h); }
        if (h < 0.5) return;
        double x = cx - w / 2;
        foreach (var c in cells)
        {
            double cw = c.Width(h);
            if (cw > 0) c.Draw(x + cw / 2, cy, cw, h);
            x += cw + Gap * h;
        }
    }

    /// <summary>
    /// Flat shapes centred on (0, 0) bent round a circle: x along it (left to right, round the top, centred
    /// at <paramref name="atDeg"/>), y outward from radius <paramref name="r"/>.
    /// </summary>
    static IEnumerable<PathD> OnArc(IEnumerable<PathD> shapes, double cx, double cy, double r, double atDeg, bool closed)
    {
        foreach (var p in shapes)
        {
            var dense = new PathD();
            int count = closed ? p.Count : p.Count - 1;
            for (int i = 0; i < p.Count; i++)
            {
                dense.Add(p[i]);
                if (i >= count) break;
                var q = p[(i + 1) % p.Count];
                double d = Math.Sqrt((q.x - p[i].x) * (q.x - p[i].x) + (q.y - p[i].y) * (q.y - p[i].y));
                int steps = (int)Math.Ceiling(d / 0.3);
                for (int k = 1; k < steps; k++) dense.Add(new PointD(p[i].x + (q.x - p[i].x) * k / steps, p[i].y + (q.y - p[i].y) * k / steps));
            }
            yield return [.. dense.Select(v =>
            {
                double a = atDeg * Math.PI / 180 - v.x / r, rr = r + v.y;
                return new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a));
            })];
        }
    }
}
