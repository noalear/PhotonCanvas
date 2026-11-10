using Clipper2Lib;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// Makes a box's pieces from its <see cref="BoxSpec"/>. The room's X runs to the right, Y up and Z
/// towards you; the box stands with its back-left-bottom corner at the origin. Flat pieces are
/// laid out and joined by <see cref="Joinery"/>; the bent wall of a rounded box and the bending lid
/// are flat strips with living hinge slits, drawn in 3D bent along their path.
/// </summary>
public static class BoxMaker
{
    /// <summary>The bed when it isn't known (mm): the F2 Ultra's.</summary>
    public const double DefaultBed = 220;
    /// <summary>Kept clear round each piece on the bed (mm).</summary>
    public const double BedGap = 2;

    sealed class Ctx(BoxSpec spec)
    {
        public BoxSpec S { get; } = spec;
        public double T { get; } = Math.Clamp(spec.ThicknessMm, 0.5, 12);
        public List<Finding> Findings { get; } = [];
        public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
        public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
        public void Note(string text) => Findings.Add(new(Severity.Note, text));
        /// <summary>Play that only ever loosens (where a tight fit would jam: a turning pin, a sliding lid).</summary>
        public double Loose => Math.Max(0, S.FitMm);
    }

    public static BoxKit Make(BoxSpec spec, double bedW = DefaultBed, double bedH = DefaultBed)
    {
        var c = new Ctx(spec);
        var (parts, outside, lid) = spec.Style switch
        {
            BoxStyle.Rounded => Rounded(c, bedW, bedH),
            BoxStyle.BendingLid => BendingLid(c),
            _ => Fingered(c),
        };
        if (c.Findings.Any(f => f.Level == Severity.Bad)) return new BoxKit([], c.Findings, c.T, outside, lid);
        parts = Merge(parts);
        parts = FitBed(c, parts, bedW, bedH);
        return new BoxKit(parts, c.Findings, c.T, outside, lid);
    }

    /// <summary>The inside sizes, worked out from the outside ones when that's what was given.</summary>
    static (double W, double D, double H) Inside(Ctx c, double extraHeight)
    {
        var s = c.S;
        double t = c.T;
        double w = s.Outside ? s.WidthMm - 2 * t : s.WidthMm;
        double d = s.Outside ? s.DepthMm - 2 * t : s.DepthMm;
        double h = s.Outside ? s.HeightMm - t - extraHeight : s.HeightMm;
        if (w < 10 || d < 10) c.Bad($"The box is too small inside ({Math.Max(0, w):0.#} × {Math.Max(0, d):0.#} mm): make it at least 10 mm each way inside.");
        if (h < 5) c.Bad($"The box is too low inside ({Math.Max(0, h):0.#} mm): make it at least 5 mm.");
        return (w, d, h);
    }

    /// <summary>A rectangle of the room's floor plan (x, z) as a piece lying flat (<see cref="Pose.Up"/> at z = <paramref name="depth"/>).</summary>
    static PathD Plan(double x0, double z0, double x1, double z1, double depth) => Geo.Rect(x0, depth - z1, x1, depth - z0);

    static Slab Panel(Joinery j, string name, Pose pose, int rank, double w, double h, int group = 0)
    {
        var s = j.Add(new Slab { Name = name, Pose = pose, Rank = rank, Group = group });
        s.Body.Add(Geo.Rect(0, 0, w, h));
        return s;
    }

    // ───────────────────────────── Finger-jointed box ─────────────────────────────

    static (List<Part>, V3, LidMove) Fingered(Ctx c)
    {
        var s = c.S;
        double t = c.T;
        var lid = s.LidUsed;
        double gapAbove = 0.5 + c.Loose;
        double extra = lid switch { LidKind.LiftOff => t, LidKind.Sliding => 2 * t + gapAbove, LidKind.Hinged => t, _ => 0 };
        var (W, D, H) = Inside(c, extra);
        if (c.Findings.Any(f => f.Level == Severity.Bad)) return ([], default, LidMove.None);
        double Wo = W + 2 * t, Do = D + 2 * t, top = t + H;
        double hFront = top, hSide = lid switch { LidKind.Sliding => top + t + gapAbove, LidKind.Hinged => top + t, _ => top };
        var j = new Joinery(t, s.FitMm);
        var bottom = j.Add(new Slab { Name = "Bottom", Pose = Pose.Up(0, t / 2, Do), Rank = 1 });
        bottom.Body.Add(Geo.Rect(0, 0, Wo, Do));
        var front = Panel(j, "Front", Pose.Front(0, 0, Do - t / 2), 3, Wo, hFront);
        var back = Panel(j, "Back", Pose.Back(Wo, 0, t / 2), 3, Wo, hSide);
        var left = Panel(j, "Left side", Pose.Left(t / 2, 0, 0), 2, Do, hSide);
        var right = Panel(j, "Right side", Pose.Right(Wo - t / 2, 0, Do), 2, Do, hSide);
        double f = s.Finger;
        foreach (var wall in new[] { front, back })
        {
            j.Finger(wall, left, f);
            j.Finger(wall, right, f);
        }
        foreach (var wall in new[] { front, back, left, right }) j.Finger(wall, bottom, f);

        double ho = hSide;
        var move = LidMove.None;
        double dividerRoom = H - 1;
        switch (lid)
        {
            case LidKind.LiftOff:
            {
                var l = j.Add(new Slab { Name = "Lid", Pose = Pose.Up(0, top + t / 2, Do), Rank = 1, Group = 1, LidArea = new RectD(0, 0, Wo, Do) });
                l.Body.Add(Geo.Rect(0, 0, Wo, Do));
                Lip(j, c, top - t / 2, Do, Plan(t, t, Wo - t, Do - t, Do), null);
                ho = top + t;
                move = new LidMove(default, V3.UnitX, 0, new V3(0, Math.Max(20, H * 0.7), -Do * 0.1));
                dividerRoom = H - t - 1;
                c.Note("Glue the lip under the lid, in the middle: it fits inside the walls and keeps the lid in place.");
                break;
            }
            case LidKind.Sliding:
                Sliding(j, c, left, right, back, Wo, Do, W, D, top, hSide, gapAbove);
                ho = hSide + t;
                move = new LidMove(default, V3.UnitX, 0, new V3(0, 0, Do * 0.6));
                dividerRoom = H - t - 1;
                break;
            case LidKind.Hinged:
                dividerRoom = Hinged(j, c, front, left, right, Wo, Do, D, top, out move) - 1;
                ho = top + t;
                break;
        }
        if (s.FingerNotch && lid == LidKind.Hinged) Notch(front, Wo, hFront, W, H);
        Dividers(j, c, front, back, left, right, bottom, Wo, Do, W, D, dividerRoom);
        var parts = j.Build();
        c.Findings.AddRange(j.Findings);
        return (parts, new V3(Wo, ho, Do), move);
    }

    /// <summary>A half-round notch at the middle of a wall's top, to get a finger under the lid.</summary>
    static void Notch(Slab wall, double w, double h, double insideW, double insideH)
    {
        double r = Math.Clamp(Math.Min(insideW / 6, insideH / 3), 4, 10);
        wall.Holes.Add(Geo.Circle(w / 2, h, r, 0.02));
    }

    /// <summary>
    /// The lip under a lift-off lid: the inside of the walls (<paramref name="inside"/>, flat), less the
    /// play, as a ring the lip width wide (or solid when that would leave almost nothing in the middle).
    /// </summary>
    static void Lip(Joinery j, Ctx c, double y, double depth, PathD inside, double? radius)
    {
        double play = 0.4 + c.Loose / 2;
        var outer = Geo.Offset([inside], -play, radius is not null);
        var ring = Geo.Offset(outer, -Math.Max(2, c.S.LipMm), radius is not null);
        var lip = j.Add(new Slab { Name = "Lid lip", Pose = Pose.Up(0, y, depth), Rank = 0, Group = 1 });
        var b = Geo.Bounds(ring);
        lip.Body.AddRange(ring.Count > 0 && b.right - b.left > 8 && b.bottom - b.top > 8 ? Geo.Subtract(outer, ring) : outer);
    }

    /// <summary>
    /// A sliding lid: runners inside the sides and back (tabbed through them) hold it up, a cap glued on
    /// the sides and back holds it down; the front is lower, so it slides out over it.
    /// </summary>
    static void Sliding(Joinery j, Ctx c, Slab left, Slab right, Slab back, double Wo, double Do, double W, double D, double top, double hSide, double gapAbove)
    {
        double t = c.T, lw = Math.Clamp(c.S.LipMm, 3, Math.Min(W, D) / 4), play = 0.4 + c.Loose / 2;
        var runners = j.Add(new Slab { Name = "Lid runners", Pose = Pose.Up(0, top - t / 2, Do), Rank = 0 });
        runners.Body.AddRange(Geo.Subtract([Plan(0, 0, Wo, Do - t, Do)], [Plan(t + lw, t + lw, Wo - t - lw, Do + 1, Do)]));
        double tab = Math.Min(c.S.Finger, 10);
        j.Tabs(runners, left, 0, tab);
        j.Tabs(runners, right, 0, tab);
        j.Tabs(runners, back, 0, tab);
        var cap = j.Add(new Slab { Name = "Lid cap", Pose = Pose.Up(0, hSide + t / 2, Do), Rank = 0 });
        cap.Body.AddRange(Geo.Subtract([Plan(0, 0, Wo, Do, Do)], [Plan(t + lw, t + lw, Wo - t - lw, Do + 1, Do)]));
        var lid = j.Add(new Slab
        {
            Name = "Lid", Pose = Pose.Up(0, top + t / 2, Do), Rank = 0, Group = 1,
            // What shows between the cap's arms.
            LidArea = new RectD(t + lw, 0, Wo - t - lw, Do - t - lw),
        });
        lid.Body.Add(Plan(t + play, t + play, Wo - t - play, Do, Do));
        // A finger hole near the front, behind a border if there's one.
        double hw = Math.Min(26, (W - 2 * lw) * 0.5), hh = 9;
        double v = c.S.Border ? c.S.BorderInsetMm + c.S.BorderWidthMm + hh / 2 + 3 : hh / 2 + 6;
        if (hw > hh && v + hh / 2 < D / 2)
            lid.Holes.AddRange(Geo.Union([Geo.Rect(Wo / 2 - hw / 2 + hh / 2, v - hh / 2, Wo / 2 + hw / 2 - hh / 2, v + hh / 2), Geo.Circle(Wo / 2 - hw / 2 + hh / 2, v, hh / 2, 0.02), Geo.Circle(Wo / 2 + hw / 2 - hh / 2, v, hh / 2, 0.02)]));
        c.Note("The runners' tabs go through the sides and back; glue the cap on top of the sides and back. The lid slides in from the front.");
    }

    /// <summary>
    /// A lid that swings up on a pin. It sits between the sides, on the front, and turns on a pin through
    /// the sides and a cheek under each of its back corners. The pin is placed far enough from the back
    /// that the lid's back edge clears the back as it swings up; the gap that leaves at the back is kept
    /// small. Returns the height the inside keeps clear of the cheeks.
    /// </summary>
    static double Hinged(Joinery j, Ctx c, Slab front, Slab left, Slab right, double Wo, double Do, double D, double top, out LidMove move)
    {
        double t = c.T, side = 0.5 + c.Loose / 2;
        var g = HingeGeometry(t, c.S.PinMm, D, top - t);
        double zp = t + g.D, yp = top - g.Drop;
        if (yp - g.D < t + 3) c.Bad("The box is too low for the hinge: the lid's back would swing into the bottom. Make it taller, or use a thinner pin or sheet.");
        if (g.Gap > 2.5) c.Warn($"The lid leaves a {g.Gap:0.#} mm gap at the back so it can swing up: a thinner pin or sheet makes it smaller.");
        var lid = j.Add(new Slab
        {
            Name = "Lid", Pose = Pose.Up(0, top + t / 2, Do), Rank = 0, Group = 1,
            LidArea = new RectD(t + side, 0, Wo - t - side, Do - t - g.Gap),
        });
        lid.Body.Add(Plan(t + side, t + g.Gap, Wo - t - side, Do, Do));
        double tab = Math.Min(c.S.Finger, 8), reach = g.R + Math.Max(10, 2.5 * tab);
        if (zp + reach > Do - t - 2) c.Bad("The box is too shallow front to back for the hinge: make it deeper, or use a thinner pin.");
        // The cheeks a little in from the lid's edges, so their tabs go through slots, not notches in the edge.
        double inset = Math.Max(2, 0.8 * t);
        foreach (var x in new[] { t + side + inset + t / 2, Wo - t - side - inset - t / 2 })
        {
            var cheek = j.Add(new Slab { Name = "Hinge cheek", Pose = Pose.Left(x, 0, 0), Rank = 0, Group = 1 });
            cheek.Body.Add(Geo.Circle(zp, yp, g.R, 0.02));
            cheek.Body.Add(Geo.Rect(zp - g.R, yp, zp + reach, top + t));
            // The pin's glued in the cheeks and turns in the sides.
            cheek.Holes.Add(Geo.Circle(zp, yp, c.S.PinMm / 2, 0.01));
            j.Tabs(cheek, lid, 2, tab);
        }
        double turn = c.S.PinMm / 2 + 0.15 + c.Loose / 2;
        left.Holes.Add(Geo.Circle(zp, yp, turn, 0.01));
        right.Holes.Add(Geo.Circle(Do - zp, yp, turn, 0.01));
        move = new LidMove(new V3(0, yp, zp), V3.UnitX, -80, default);
        c.Note($"Push a {c.S.PinMm:0.#} mm pin (a dowel, skewer or rod) through each side into the hinge cheeks, and glue it in the cheeks only, so it turns in the sides. The lid stops a little past upright.");
        // The lid's back, behind the pin, swings down into the box as it opens (no lower than the pin
        // less its distance from the back): the dividers stay under that.
        return yp - g.D - t;
    }

    /// <summary>
    /// Where a hinged lid's pin goes: <see cref="D"/> in front of the back's inside, <see cref="Drop"/>
    /// under the lid; the cheek's radius round it; and the gap the lid leaves at the back so its back
    /// edge (sheet <paramref name="t"/> above the pin plus the drop) clears the back as it turns. The
    /// further the pin is from the back the smaller the gap, but the lid's back swings down that far
    /// into the box: it's kept to 0.6 of the inside height under the pin (<paramref name="insideHeight"/> is the whole inside's).
    /// </summary>
    public readonly record struct Hinge(double D, double Drop, double R, double Gap);

    /// <summary>The play the lid's back edge keeps from the back as it swings.</summary>
    public const double HingePlay = 0.6;

    public static Hinge HingeGeometry(double t, double pin, double insideDepth, double insideHeight = double.PositiveInfinity)
    {
        double r = Math.Max(pin / 2 + 2.5, t), drop = r + 1, b = t + drop;
        double d = Math.Max(b + HingePlay + 0.25, r + HingePlay + 1), gap = 0;
        double most = Math.Max(d, Math.Min(insideDepth * 0.45, (insideHeight - drop) * 0.6));
        for (; d + 0.25 <= most; d += 0.25)
        {
            gap = Math.Max(HingePlay, d - Math.Sqrt((d - HingePlay) * (d - HingePlay) - b * b)) + 0.1;
            if (gap <= 1.2 + HingePlay) break;
        }
        gap = Math.Max(HingePlay, d - Math.Sqrt(Math.Max(0, (d - HingePlay) * (d - HingePlay) - b * b))) + 0.1;
        return new Hinge(d, drop, r, gap);
    }

    /// <summary>Dividers: slotted into each other half way, tabbed through the walls and bottom.</summary>
    static void Dividers(Joinery j, Ctx c, Slab front, Slab back, Slab left, Slab right, Slab bottom, double Wo, double Do, double W, double D, double room)
    {
        int na = Math.Clamp(c.S.DividersAcross, 0, 20), nz = Math.Clamp(c.S.DividersAlong, 0, 20);
        if (na + nz == 0) return;
        double t = c.T;
        if (room < 5)
        {
            c.Warn("There's no room for dividers under the lid: make the box taller.");
            return;
        }
        if (W / (na + 1) < t + 4 || D / (nz + 1) < t + 4)
        {
            c.Warn("The dividers would be too close together: use fewer, or make the box bigger.");
            return;
        }
        int tabs = room >= 25 ? 2 : 1;
        double tab = Math.Min(c.S.Finger, room / 3);
        var across = new List<Slab>();
        for (int i = 1; i <= na; i++)
        {
            var d = Panel(j, "Divider, front to back", Pose.Left(t + W * i / (na + 1), 0, 0), 0, Do, t + room);
            j.Tabs(d, front, tabs, tab);
            j.Tabs(d, back, tabs, tab);
            j.Tabs(d, bottom, 0, c.S.Finger);
            across.Add(d);
        }
        for (int i = 1; i <= nz; i++)
        {
            var d = Panel(j, "Divider, side to side", Pose.Front(0, 0, t + D * i / (nz + 1)), 0, Wo, t + room);
            j.Tabs(d, left, tabs, tab);
            j.Tabs(d, right, tabs, tab);
            j.Tabs(d, bottom, 0, c.S.Finger);
            foreach (var a in across) j.Halving(a, d);
        }
        c.Note("The dividers' tabs go through slots in the walls and bottom, so their ends show on the outside.");
    }

    // ───────────────────────────── Box with a bending lid ─────────────────────────────

    static (List<Part>, V3, LidMove) BendingLid(Ctx c)
    {
        var s = c.S;
        double t = c.T;
        var (W, D, H) = Inside(c, t);
        double ri = s.BendRadiusMm;
        if (c.Findings.Any(f => f.Level == Severity.Bad)) return ([], default, LidMove.None);
        double Wo = W + 2 * t, Do = D + 2 * t, Ho = t + H + t, yb = Ho - t - ri;
        if (ri < 2) c.Bad("The bend over the back needs a radius of at least 2 mm.");
        if (yb < t + 6) c.Bad($"The box is too low for a {ri:0.#} mm bend: make it taller or the bend tighter.");
        if (ri > D - 10) c.Bad($"The box is too shallow front to back for a {ri:0.#} mm bend.");
        if (c.Findings.Any(f => f.Level == Severity.Bad)) return ([], default, LidMove.None);
        if (ri < 2.5 * t) c.Warn($"A bend this tight ({ri:0.#} mm in {t:0.#} mm sheet) may crack: try a bigger radius, or closer rows of slits.");

        var j = new Joinery(t, s.FitMm);
        var bottom = j.Add(new Slab { Name = "Bottom", Pose = Pose.Up(0, t / 2, Do), Rank = 1 });
        bottom.Body.Add(Geo.Rect(0, 0, Wo, Do));
        var front = Panel(j, "Front", Pose.Front(0, 0, Do - t / 2), 3, Wo, Ho - t);
        // The sides: their back top corner rounded where the lid bends over it.
        var sideShape = Geo.Union(Geo.Subtract([Geo.Rect(0, 0, Do, Ho - t)], [Geo.Rect(-1, yb, t + ri, Ho)]),
            Geo.Intersect([Geo.Circle(t + ri, yb, ri, 0.02)], [Geo.Rect(t, yb, t + ri, Ho - t)]));
        var left = j.Add(new Slab { Name = "Left side", Pose = Pose.Left(t / 2, 0, 0), Rank = 2 });
        left.Body.AddRange(sideShape);
        var right = j.Add(new Slab { Name = "Right side", Pose = Pose.Right(Wo - t / 2, 0, Do), Rank = 2 });
        right.Body.AddRange(Geo.Map(sideShape, q => new PointD(Do - q.x, q.y)));

        // The back and lid: one strip, the back jointed, the bend slit, the lid resting on the sides and front.
        double rn = ri + t / 2, arc = Math.PI / 2 * rn, lidLen = Do - t - ri, vEnd = yb + arc;
        var back = j.Add(new Slab { Name = "Back and lid", Pose = Pose.Back(Wo, 0, t / 2), Rank = 3, LidArea = new RectD(0, vEnd, Wo, vEnd + lidLen) });
        back.Body.Add(Geo.Rect(0, 0, Wo, yb));
        back.Extra.Add(Geo.Rect(0, yb - 0.01, Wo, vEnd + lidLen));
        var hinge = HingeStyle.For(t, Wo, s.HingeSpacingMm, s.HingeCutMm, s.HingeGapMm);
        back.Slits.AddRange(Hinges.Slits(yb + 0.5, vEnd + 1, 0, Wo, hinge, alongU: false));
        back.NoSeam.Add((yb - 1, vEnd + 2));
        var facets = new List<(PathsD, Pose)> { ([Geo.Rect(-1, -1, Wo + 1, yb)], back.Pose) };
        V3 At(double a) => new(Wo, yb + rn * Math.Sin(a), t + ri - rn * Math.Cos(a));
        const int steps = 8;
        for (int i = 0; i < steps; i++)
        {
            double a0 = Math.PI / 2 * i / steps, a1 = Math.PI / 2 * (i + 1) / steps, v0 = yb + rn * a0, v1 = yb + rn * a1;
            var dir = (At(a1) - At(a0)).Norm();
            facets.Add(([Geo.Rect(-1, v0, Wo + 1, v1)], new Pose(At(a0) - dir * v0, -V3.UnitX, dir)));
        }
        facets.Add(([Geo.Rect(-1, vEnd, Wo + 1, vEnd + lidLen + 1)], new Pose(new V3(Wo, Ho - t / 2, t + ri) - V3.UnitZ * vEnd, -V3.UnitX, V3.UnitZ)));
        back.Facets = facets;

        double f = s.Finger;
        foreach (var wall in new[] { front, back })
        {
            j.Finger(wall, left, f);
            j.Finger(wall, right, f);
        }
        foreach (var wall in new[] { front, back, left, right }) j.Finger(wall, bottom, f);
        if (s.FingerNotch) Notch(front, Wo, Ho - t, W, H);
        Dividers(j, c, front, back, left, right, bottom, Wo, Do, W, D, H - ri - 1);
        c.Note("Bend the lid over gently, a little at a time; a little warmth or damp helps wood bend. It rests on the sides and front.");
        var parts = j.Build();
        c.Findings.AddRange(j.Findings);
        return (parts, new V3(Wo, Ho, Do), LidMove.None);
    }

    // ───────────────────────────── Rounded box ─────────────────────────────

    /// <summary>A stretch of a rounded box's wall: straight, or a quarter turn round a corner.</summary>
    sealed record Run(double Length, bool Bend, Func<double, (double X, double Z)> At);

    static (List<Part>, V3, LidMove) Rounded(Ctx c, double bedW, double bedH)
    {
        var s = c.S;
        double t = c.T;
        bool lid = s.LidUsed != LidKind.None;
        var (W, D, H) = Inside(c, lid ? t : 0);
        if (c.Findings.Any(f => f.Level == Severity.Bad)) return ([], default, LidMove.None);
        double r = s.CornerRadiusMm;
        if (r > Math.Min(W, D) / 2)
        {
            r = Math.Min(W, D) / 2;
            c.Note($"The corners are as round as the box allows: {r:0.#} mm.");
        }
        if (r < 2) { c.Bad("The corners need a radius of at least 2 mm."); return ([], default, LidMove.None); }
        if (r < 2.5 * t) c.Warn($"Corners this tight ({r:0.#} mm in {t:0.#} mm sheet) may crack: try a bigger radius, or closer rows of slits.");
        double Wo = W + 2 * t, Do = D + 2 * t, Hs = t + H, rn = r + t / 2;
        double fw = W - 2 * r, fd = D - 2 * r;
        // The wall's middle line, walked so the outside faces out: from the back's middle to the left,
        // round to the front, to the right and back again.
        double cxL = t + r, cxR = Wo - t - r, czB = t + r, czF = Do - t - r;
        (double, double) Arc(double cx, double cz, double from, double u) { double a = from - u / rn; return (cx + rn * Math.Cos(a), cz + rn * Math.Sin(a)); }
        var loop = new List<Run>
        {
            new(fw / 2, false, u => (Wo / 2 - u, t / 2)),
            new(Math.PI / 2 * rn, true, u => Arc(cxL, czB, -Math.PI / 2, u)),
            new(fd, false, u => (t / 2, czB + u)),
            new(Math.PI / 2 * rn, true, u => Arc(cxL, czF, Math.PI, u)),
            new(fw, false, u => (cxL + u, Do - t / 2)),
            new(Math.PI / 2 * rn, true, u => Arc(cxR, czF, Math.PI / 2, u)),
            new(fd, false, u => (Wo - t / 2, czF - u)),
            new(Math.PI / 2 * rn, true, u => Arc(cxR, czB, 0, u)),
            new(fw / 2, false, u => (cxR - u, t / 2)),
        };
        // Start at the middle of the longest straight if the back's too short to hold the closing seam.
        var seamStyle = SeamStyle(c);
        double needs = 2 * (seamStyle.SizeMm + 3);
        if (fw < needs)
        {
            var longest = loop.Where(q => !q.Bend).MaxBy(q => q.Length)!;
            if (longest.Length < needs) { c.Bad("The wall has no straight long enough to join its ends: make the box bigger or the corners smaller."); return ([], default, LidMove.None); }
            int k = loop.IndexOf(longest);
            var rest = loop.Skip(1).Take(loop.Count - 2).ToList();
            // Rejoin the back's two halves, then open the loop at the longest straight's middle.
            rest.Add(new Run(fw, false, u => (cxR - u, t / 2)));
            int at = rest.IndexOf(longest);
            var half = longest.Length / 2;
            loop = [new Run(half, false, u => longest.At(half + u)), .. rest.Skip(at + 1), .. rest.Take(at), new Run(half, false, longest.At)];
        }
        double L = loop.Sum(q => q.Length);
        var starts = new List<double>();
        { double u = 0; foreach (var q in loop) { starts.Add(u); u += q.Length; } }
        (double X, double Z) Where(double u)
        {
            for (int i = loop.Count - 1; i >= 0; i--)
                if (u >= starts[i] - 1e-9 || i == 0) return loop[i].At(Math.Min(u - starts[i], loop[i].Length));
            return loop[0].At(0);
        }
        (double X, double Z) Tangent(double u)
        {
            var (x0, z0) = Where(Math.Max(0, u - 0.01));
            var (x1, z1) = Where(Math.Min(L, u + 0.01));
            double d = Math.Max(1e-9, Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)));
            return ((x1 - x0) / d, (z1 - z0) / d);
        }

        // Seams: the loop's own ends, and more on straights if it's longer than the bed.
        double maxLen = Math.Max(bedW, bedH) - 2 * BedGap;
        if (Hs > Math.Min(bedW, bedH) - 2 * BedGap) { c.Bad($"The wall is {Hs:0} mm high: too high for the bed. Make the box lower."); return ([], default, LidMove.None); }
        var seams = new List<double> { 0 };
        double room = maxLen - seamStyle.SizeMm - 1;
        if (L > room)
        {
            int k = (int)Math.Ceiling(L / room);
            var straights = loop.Select((q, i) => (q, i)).Where(x => !x.q.Bend && x.q.Length >= needs).Select(x => (Lo: starts[x.i] + needs / 2, Hi: starts[x.i] + x.q.Length - needs / 2)).ToList();
            for (int i = 1; i < k; i++)
            {
                double want = L * i / k;
                double best = double.NaN;
                foreach (var (lo, hi) in straights)
                {
                    double at = Math.Clamp(want, lo, hi);
                    if (double.IsNaN(best) || Math.Abs(at - want) < Math.Abs(best - want)) best = at;
                }
                if (!double.IsNaN(best) && best - seams[^1] > needs) seams.Add(best);
            }
            seams.Add(L);
            for (int i = 1; i < seams.Count; i++)
                if (seams[i] - seams[i - 1] + seamStyle.SizeMm > maxLen + 1e-6)
                {
                    c.Bad("The wall is too long for the bed, and the straights between its corners are too short to join it there: make the box smaller.");
                    return ([], default, LidMove.None);
                }
            seams.RemoveAt(seams.Count - 1);
        }

        // The bottom's tabs go into notches along the wall's bottom edge, on the straights, clear of the seams.
        double f = s.Finger, clear = seamStyle.SizeMm + 2;
        var notches = new PathsD();
        var tabs = new PathsD();
        for (int i = 0; i < loop.Count; i++)
        {
            if (loop[i].Bend) continue;
            var free = new List<(double Lo, double Hi)> { (starts[i] + 1, starts[i] + loop[i].Length - 1) };
            foreach (var sm in seams.Append(L)) free = Without(free, sm - clear, sm + clear);
            free.RemoveAll(q => q.Hi - q.Lo < f + 2);
            foreach (var (lo, hi) in free)
            {
                int n = Math.Max(1, (int)Math.Floor((hi - lo) / (3 * f)));
                for (int k = 0; k < n; k++)
                {
                    double cu = lo + (hi - lo) * (k + 0.5) / n;
                    notches.Add(Geo.Rect(cu - f / 2 - s.FitMm / 2, -1, cu + f / 2 + s.FitMm / 2, t + Math.Max(0, s.FitMm) / 2));
                    var (px, pz) = Where(cu);
                    var (tx, tz) = Tangent(cu);
                    // Outward: the tangent turned a quarter (the wall's walked with the outside on its left).
                    double nx = tz, nz = -tx;
                    if ((px - Wo / 2) * nx + (pz - Do / 2) * nz < 0) { nx = -nx; nz = -nz; }
                    PointD P(double along, double outward) => new(px + tx * along + nx * outward, Do - (pz + tz * along + nz * outward));
                    tabs.Add([P(-f / 2, -t / 2 - 1), P(f / 2, -t / 2 - 1), P(f / 2, t / 2), P(-f / 2, t / 2)]);
                }
            }
        }
        var bottomPlate = Geo.Union(Geo.Union([RoundRect(t, t, Wo - t, Do - t, r)]), Geo.Union(tabs));
        var hinge = HingeStyle.For(t, Hs, s.HingeSpacingMm, s.HingeCutMm, s.HingeGapMm);
        var slits = new List<PathD>();
        for (int i = 0; i < loop.Count; i++)
            if (loop[i].Bend) slits.AddRange(Hinges.Slits(starts[i] - 0.5, starts[i] + loop[i].Length + 0.5, 0, Hs, hinge, alongU: true));
        double E = seamStyle.SizeMm + 2;
        var wall = Geo.Subtract([Geo.Rect(-E, 0, L + E, Hs)], notches);
        var pieces = Joins.Split(wall, true, [.. seams, L], seamStyle, t + 1.5, keepEnds: false, stretches: [(0, Hs)]);

        // In 3D: each straight one flat stretch, each corner eight.
        var facets = new List<(PathsD, Pose)>();
        for (int i = 0; i < loop.Count; i++)
        {
            int steps = loop[i].Bend ? 8 : 1;
            for (int k = 0; k < steps; k++)
            {
                double u0 = starts[i] + loop[i].Length * k / steps, u1 = starts[i] + loop[i].Length * (k + 1) / steps;
                var (x0, z0) = Where(u0);
                var (x1, z1) = Where(u1);
                var dir = new V3(x1 - x0, 0, z1 - z0).Norm();
                if (dir.Length < 0.5) continue;
                double lo = i == 0 && k == 0 ? -E - 1 : u0 - 0.02, hi = i == loop.Count - 1 && k == steps - 1 ? L + E + 1 : u1 + 0.02;
                facets.Add(([Geo.Rect(lo, -1, hi, Hs + 1)], new Pose(new V3(x0, 0, z0) - dir * u0, dir, V3.UnitY)));
            }
        }
        var parts = new List<Part> { new("Bottom", Geo.Tidy(bottomPlate), [], [Pose.Up(0, t / 2, Do)]) };
        for (int i = 0; i < pieces.Count; i++)
        {
            string name = pieces.Count > 1 ? $"Wall {i + 1} of {pieces.Count}" : "Wall";
            parts.Add(new Part(name, pieces[i], Geo.ClipLines(slits, pieces[i]), []) { Facets = facets });
        }
        if (pieces.Count > 1) c.Note($"The wall is cut in {pieces.Count} pieces; glue their {Name(seamStyle.Kind)} together into a loop round the bottom.");
        else c.Note($"Bend the wall round the bottom, its tabs in the notches, and glue its ends' {Name(seamStyle.Kind)} together at the back.");

        double ho = Hs;
        var move = LidMove.None;
        if (lid)
        {
            double R = r + t;
            parts.Add(new Part("Lid", Geo.Tidy([RoundRect(0, 0, Wo, Do, R)]), [], [Pose.Up(0, Hs + t / 2, Do)]) { Group = 1, LidArea = new RectD(0, 0, Wo, Do) });
            double play = 0.4 + c.Loose / 2;
            var outer = Geo.Tidy([RoundRect(t + play, t + play, Wo - t - play, Do - t - play, Math.Max(0.5, r - play))]);
            var inner = Geo.Offset(outer, -Math.Max(2, s.LipMm), true);
            var ib = Geo.Bounds(inner);
            var lip = inner.Count > 0 && ib.right - ib.left > 8 && ib.bottom - ib.top > 8 ? Geo.Subtract(outer, inner) : outer;
            parts.Add(new Part("Lid lip", lip, [], [Pose.Up(0, Hs - t / 2, Do)]) { Group = 1 });
            ho = Hs + t;
            move = new LidMove(default, V3.UnitX, 0, new V3(0, Math.Max(20, H * 0.7), -Do * 0.1));
            c.Note("Glue the lip under the lid, in the middle: it fits inside the wall and keeps the lid in place.");
        }
        return (parts, new V3(Wo, ho, Do), move);
    }

    /// <summary>The stretches with [a, b] taken out of them.</summary>
    static List<(double Lo, double Hi)> Without(List<(double Lo, double Hi)> stretches, double a, double b)
    {
        var o = new List<(double, double)>();
        foreach (var (lo, hi) in stretches)
        {
            if (b <= lo || a >= hi) { o.Add((lo, hi)); continue; }
            if (a > lo) o.Add((lo, a));
            if (b < hi) o.Add((b, hi));
        }
        return o;
    }

    static string Name(JoinKind k) => k switch
    {
        JoinKind.Dovetail => "dovetails",
        JoinKind.Jigsaw => "jigsaw knobs",
        JoinKind.Fingers => "fingers",
        _ => "straight ends",
    };

    /// <summary>A rectangle with round corners (flat, y up).</summary>
    public static PathD RoundRect(double x0, double y0, double x1, double y1, double r)
    {
        r = Math.Min(r, Math.Min(x1 - x0, y1 - y0) / 2);
        if (r <= 1e-6) return Geo.Rect(x0, y0, x1, y1);
        var p = new PathD();
        p.AddRange(Geo.Arc(x1 - r, y0 + r, r, -Math.PI / 2, 0, 0.01));
        p.AddRange(Geo.Arc(x1 - r, y1 - r, r, 0, Math.PI / 2, 0.01));
        p.AddRange(Geo.Arc(x0 + r, y1 - r, r, Math.PI / 2, Math.PI, 0.01));
        p.AddRange(Geo.Arc(x0 + r, y0 + r, r, Math.PI, 1.5 * Math.PI, 0.01));
        return p;
    }

    /// <summary>The joins used where a piece is cut to fit the bed.</summary>
    public static JoinStyle SeamStyle(BoxSpec s) => new(s.SeamJoin, Math.Clamp(2.5 * s.ThicknessMm, 5, 12), s.FitMm);
    static JoinStyle SeamStyle(Ctx c) => SeamStyle(c.S);

    // ───────────────────────────── Finishing ─────────────────────────────

    /// <summary>Pieces made twice the same (dividers, cheeks) become one piece cut twice.</summary>
    static List<Part> Merge(List<Part> parts)
    {
        var o = new List<Part>();
        foreach (var p in parts)
        {
            var same = p.Facets is null ? o.FirstOrDefault(q => q.Facets is null && q.Name == p.Name && Same(q.Cut, p.Cut)) : null;
            if (same is null) o.Add(p with { Poses = [.. p.Poses] });
            else same.Poses.AddRange(p.Poses);
        }
        return o;
    }

    static bool Same(PathsD a, PathsD b)
    {
        var ba = Geo.Bounds(a);
        var bb = Geo.Bounds(b);
        return Math.Abs(Geo.Area(a) - Geo.Area(b)) < 1e-3 && Math.Abs(ba.Width - bb.Width) < 1e-4 && Math.Abs(ba.Height - bb.Height) < 1e-4
            && Geo.Area(Geo.Subtract(Geo.Move(a, -ba.left, -ba.top), Geo.Move(b, -bb.left, -bb.top))) < 1e-3;
    }

    /// <summary>Does a w × h piece fit the bed (turned if need be).</summary>
    public static bool Fits(double w, double h, double bedW, double bedH) =>
        Math.Max(w, h) + 2 * BedGap <= Math.Max(bedW, bedH) + 1e-6 && Math.Min(w, h) + 2 * BedGap <= Math.Min(bedW, bedH) + 1e-6;

    /// <summary>Pieces longer than the bed are cut in pieces with joins along their longer side.</summary>
    static List<Part> FitBed(Ctx c, List<Part> parts, double bedW, double bedH)
    {
        var o = new List<Part>();
        var style = SeamStyle(c);
        double maxLen = Math.Max(bedW, bedH) - 2 * BedGap;
        foreach (var p in parts)
        {
            var b = Geo.Bounds(p.Cut);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (Fits(w, h, bedW, bedH)) { o.Add(p); continue; }
            if (Math.Min(w, h) + 2 * BedGap > Math.Min(bedW, bedH))
            {
                c.Bad($"The {p.Name.ToLowerInvariant()} is {w:0} × {h:0} mm: too big for the {bedW:0} × {bedH:0} mm bed even cut in pieces. Make the box smaller.");
                continue;
            }
            bool alongX = w >= h;
            var seams = Joins.Seams(alongX ? b.left : b.top, alongX ? b.right : b.bottom, maxLen, style, alongX ? null : p.NoSeam);
            var bits = Joins.Split(p.Cut, alongX, seams, style, c.T + 2);
            for (int i = 0; i < bits.Count; i++)
            {
                var bb = Geo.Bounds(bits[i]);
                if (!Fits(bb.right - bb.left, bb.bottom - bb.top, bedW, bedH))
                    c.Bad($"The {p.Name.ToLowerInvariant()} can't be cut in pieces that fit the bed: make the box smaller.");
                o.Add(p with
                {
                    Name = $"{p.Name} ({i + 1} of {bits.Count})", Cut = bits[i], Slits = Geo.ClipLines(p.Slits, bits[i]), Poses = [.. p.Poses],
                    // A border goes on a lid in one piece only.
                    LidArea = null,
                });
            }
            c.Note($"The {p.Name.ToLowerInvariant()} is bigger than the bed, so it's cut in {bits.Count} pieces joined with {Name(style.Kind)}: glue them together first.");
            if (p.LidArea is not null && c.S.Border) c.Warn("The lid is cut in pieces to fit the bed, so it gets no carved border.");
        }
        return o;
    }
}
