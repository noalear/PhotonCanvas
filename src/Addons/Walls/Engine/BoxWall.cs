using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// Walls built as boxes: a textured front and back, a base plate they stand on (a plinth when it's wider),
/// a top plate between them, an end at each end, and ribs inside that hold the faces apart. Doors and
/// windows are lined with layers of sheet between the faces, so they look solid from any side. A
/// battlemented top has its merlons filled with layers (or a parapet and a walkway behind it); a broken top
/// has layers between the faces that break unevenly too. The faces' ends have tabs that go into slots in
/// the columns, or the ends are closed and finished.
/// </summary>
/// <remarks>
/// The wall runs along x from 0 to its length, y is up from the table and z is through it (the front face
/// is at +z). Every piece is drawn flat in its own coordinates and carries where it sits (<see cref="Part.Poses"/>).
/// </remarks>
public static class BoxWall
{
    /// <summary>How far the lining round a door or window reaches past its edge, between the faces (mm).</summary>
    public const double RevealMm = 3;
    /// <summary>The narrowest strip of sheet left beside a slot (mm).</summary>
    public const double BridgeMm = 1.2;
    /// <summary>How far in from each end a box wall's ends sit when the wall goes into columns (so their tabs have the base all round).</summary>
    public const double EndInsetMm = 2;

    /// <summary>The room between the faces (mm).</summary>
    public static double Gap(WallSpec s) => s.WallMm - 2 * s.ThicknessMm;

    /// <summary>How many layers of sheet fit between the faces (for linings, merlons and broken tops).</summary>
    public static int CoreLayers(WallSpec s) => Math.Max(1, (int)Math.Floor((Gap(s) + 0.05) / Math.Max(0.1, s.ThicknessMm)));

    /// <summary>The middle of each layer between the faces (z, mm), back to front, any room left over shared out between them.</summary>
    public static double[] CoreZ(WallSpec s)
    {
        int n = CoreLayers(s);
        double g = Gap(s), t = s.ThicknessMm, spare = Math.Max(0, g - n * t);
        return [.. Enumerable.Range(0, n).Select(i => -g / 2 + spare / (n + 1) * (i + 1) + t * (i + 0.5))];
    }

    /// <summary>How far the faces' end tabs go into a column (mm): through its side and a little past.</summary>
    public static double TabLength(WallSpec s) => s.ThicknessMm + 0.5;

    /// <summary>The top of the wall's box (the top plate's top, y mm): under any merlons, parapet or broken top.</summary>
    public static double BoxTop(WallSpec s)
    {
        double H = s.HeightMm, T = s.ThicknessMm;
        return s.Top switch
        {
            TopStyle.Battlements when Walkway(s) => H - Math.Clamp(s.MerlonHeightMm, 1, H * 0.6) - WallKitMaker.WalkwayDropMm,
            TopStyle.Battlements => H - Math.Clamp(s.MerlonHeightMm, 1, H * 0.6),
            _ => H,
        };
    }

    /// <summary>A walkway behind battlements: wanted, and the wall is tall enough for one.</summary>
    public static bool Walkway(WallSpec s) =>
        s.Walkway && s.Top == TopStyle.Battlements && s.HeightMm - Math.Clamp(s.MerlonHeightMm, 1, s.HeightMm * 0.6) - WallKitMaker.WalkwayDropMm - 2 * s.ThicknessMm >= 6;

    /// <summary>How many plates a column stands on (boxed columns; the bottom of a stacked column is its own layers).</summary>
    public static int ColumnBasePlates(WallSpec s) => s.ColumnBase == ColumnBase.SteppedPlinth ? 2 : 1;

    /// <summary>
    /// The tabs on the ends of the faces that go into a column's side (y ranges, mm): above what the column
    /// stands on and its locating plug, below the plate inside its top, and below the top of either face.
    /// </summary>
    public static List<(double Y0, double Y1)> EndTabs(WallSpec s)
    {
        double T = s.ThicknessMm, H = s.HeightMm;
        double bottom = s.ColumnBuild == ColumnBuild.Boxed ? (ColumnBasePlates(s) + 1) * T + 1 : T + 1;
        double faceTop = Walkway(s) ? BoxTop(s) - T : H;
        double top = Math.Min(s.ColumnBuild == ColumnBuild.Boxed ? H - T : H, faceTop) - 1;
        // A wall with a fallen end keeps its tabs low, so the end can fall away above them.
        if (s.Breaks == RuinStyle.FallenEnd && !Walkway(s)) top = Math.Min(top, bottom + Math.Max(10, (top - bottom) * 0.4));
        double span = top - bottom;
        if (span < 3) return [];
        if (span < 20)
        {
            double h = Math.Min(span, 8), m = (bottom + top) / 2;
            return [(m - h / 2, m + h / 2)];
        }
        double th = Math.Clamp(span * 0.25, 4, 12);
        return [(bottom + span * 0.25 - th / 2, bottom + span * 0.25 + th / 2), (bottom + span * 0.75 - th / 2, bottom + span * 0.75 + th / 2)];
    }

    /// <summary>A door or window through the faces; <paramref name="Groove"/> is how far a piece set into it reaches past its edge.</summary>
    sealed record Hole(PathsD Shape, PathsD FrontCut, PathsD BackCut, double X0, double X1, double Y0, double Y1, bool Through, double Groove = 0);

    public static WallKit Make(WallSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        double T = Math.Max(0.3, s.ThicknessMm), W = s.WallMm, G = W - 2 * T, H = s.HeightMm, fit = Math.Max(0, s.FitMm);
        double L = WallKitMaker.PanelLength(s), frame = Math.Max(0, s.FrameMm), pl = Math.Max(0, s.PlinthMm);
        int walls = Math.Max(1, s.Walls);
        var used = new List<MountPoint>();
        WallKit Done() => new(parts, findings, L, W, 0) { MountsUsed = used };
        void Bad(string t) => findings.Add(new(Severity.Bad, t));

        if (G < T - 1e-6) { Bad($"A boxed wall needs room for a layer of sheet between its faces: make it at least {3 * T:0.#} mm thick (three {T:0.#} mm panels)."); return Done(); }
        if (L < 20) { Bad($"The wall is too short ({L:0.#} mm): make it longer."); return Done(); }
        if (H < 4 * T + 6) { Bad($"The wall is too low for a box: make it at least {4 * T + 6:0.#} mm high."); return Done(); }

        int n = CoreLayers(s);
        var zs = CoreZ(s);
        double spare = G - n * T;
        if (spare > 0.4)
            findings.Add(new(Severity.Warn, $"{G:0.#} mm between the faces takes {n} layer{(n == 1 ? "" : "s")} of {T:0.#} mm sheet with {spare:0.#} mm over, so linings, merlons and broken tops leave a gap. A wall {(2 + n) * T:0.#} or {(3 + n) * T:0.#} mm thick fits exactly."));
        double groove = Textures.Preset(s.Texture) is { UsesGroove: false } || s.GrooveMm < 0.2 ? 0.6 : Math.Min(s.GrooveMm, 3);
        bool intoColumns = s.EndsIntoColumns;
        double endX = intoColumns ? EndInsetMm : 0;          // where the ends sit (from each end)
        double inner0 = endX + T, inner1 = L - endX - T;      // the box's inside, between its ends

        // ---- the tops: front, back and the layers between ----
        double stepX = Math.Clamp(s.SizeA / 2, 1.5, 8), stepY = Math.Clamp(s.SizeB, 1, 8);
        Profile front, back;
        List<(double Z, Profile P)> core = [];
        double hb = BoxTop(s);
        bool walkway = Walkway(s), battlements = s.Top == TopStyle.Battlements;
        var breaks = s.Breaks;
        if (s.Walkway && battlements && !walkway)
            findings.Add(new(Severity.Warn, "The wall is too low for a walkway behind its battlements: the battlements are filled solid instead."));
        var plain = Profile.Level(0, L, H);
        if (battlements)
        {
            double mh = Math.Clamp(s.MerlonHeightMm, 1, H * 0.6);
            var crenels = Crenels(L, s.MerlonWidthMm, s.CrenelWidthMm);
            if (crenels.Count == 0)
            {
                findings.Add(new(Severity.Warn, "The wall is too short for battlements: it has a straight top."));
                battlements = walkway = false;
                hb = H;
            }
            else
            {
                plain = Tops.Battlements(s, L, H, H - mh, crenels);
                if (s.Merlons != MerlonPattern.Even && s.MerlonStepMm > mh - 1)
                    findings.Add(new(Severity.Warn, $"The shorter merlons can only be {Math.Max(0, mh - 1):0.#} mm lower than the tall ones: make the merlons taller."));
            }
        }
        if (breaks == RuinStyle.None)
        {
            front = plain;
            if (walkway)
            {
                back = Profile.Level(0, L, hb - T);
                core.Add((W / 2 - 1.5 * T, front));
            }
            else
            {
                back = front;
                if (battlements) core.AddRange(zs.Select(z => (z, front)));
            }
        }
        else
        {
            // The ends stay up to the top of their tabs into the columns; below the break there has to be room
            // for the box (or, behind a parapet, the walkway stays whole).
            double keep = intoColumns ? 3 : 0;
            var tabsUp = intoColumns ? EndTabs(s) : [];
            double endFloor = tabsUp.Count > 0 ? tabsUp.Max(t => t.Y1) + 1.5 : H;
            double under = Math.Max(stepY, 2), lowest = 2 * T + 6;
            double floor = walkway ? hb + 1.5 : lowest + under;
            bool byStones = breaks is not (RuinStyle.Stepped or RuinStyle.Shattered);
            front = Tops.Break(s, plain, L, keep, endFloor, floor, 0, 1, byStones ? Stones(s, 0, L, H) : null, out bool shallow0);
            if (walkway)
            {
                back = Profile.Level(0, L, hb - T);
                core.Add((W / 2 - 1.5 * T, front));
                if (shallow0) findings.Add(new(Severity.Note, "The parapet is broken down as far as the walkway behind it."));
            }
            else
            {
                back = Tops.Break(s, plain, L, keep, endFloor, floor, 1, 0.85, byStones ? Stones(s, 1, L, H) : null, out bool shallow1);
                double broken = Math.Min(front.Min, back.Min);
                if (broken < hb - 1e-6) hb = broken - under;
                if (shallow0 || shallow1) findings.Add(new(Severity.Note, "The top is broken down as far as a box wall this high allows."));
                var rnd = new Random(s.Seed * 104729 + 71);
                double boxTop = hb;
                foreach (var z in zs)
                {
                    // Each layer between the faces breaks a little lower than the faces here and there, never below the box.
                    var jitter = new Dictionary<double, double>();
                    var p = Profile.Combine(front, back, (x, a, b) =>
                    {
                        double k = Math.Round(x / stepX);
                        if (!jitter.TryGetValue(k, out var j)) jitter[k] = j = rnd.NextDouble() < 0.4 ? stepY : 0;
                        double m = Math.Min(a, b);
                        return Math.Min(m, Math.Max(boxTop + 1.5, m - j));
                    }).Merged();
                    core.Add((z, p));
                }
            }
        }
        double bodyTop = hb - T;   // the underside of the top plate: ribs, ends and linings stop here
        if (bodyTop - T < 4) { Bad("There's no room for the box under the top: make the wall taller or the battlements shallower."); return Done(); }

        // ---- doors and windows ----
        var holes = new List<Hole>();
        var keepClear = new PathsD();     // where the texture stops on the faces (frames, a closed door, round slots)
        var frameRings = new PathsD();
        var frameLines = new List<PathD>();
        var doorEtched = new PathsD();    // a closed door, etched on both faces
        var doorLines = new List<PathD>();
        var taken = new List<(double X0, double X1, double Y0, double Y1)>();
        double margin = Math.Max(frame, Math.Max(RevealMm, T));

        // What fills the doors and windows. Glued pieces sit in one layer between the faces, its lining cut back
        // to make a groove they fit in; a hinged door or shutters turn on square pins, cut on the leaf, in round
        // holes in the base and in plates across the opening (between the faces, in the middle of the wall).
        double gIns = Math.Clamp(s.InsertGrooveMm, 0.8, RevealMm - 1);
        double zi = zs[zs.Length / 2];                       // the layer glued pieces sit in
        double cH = Math.Max(0.6, 0.22 * T + 0.25);          // room beside a hinged leaf for its corners to swing past
        double pinD = T * Math.Sqrt(2) + fit + 0.3;          // the round holes the square pins turn in
        bool pinRoom = G >= pinD + 1;
        var bands = new PathsD();          // taken out of every lining layer: plates across an opening
        var grooves = new PathsD();        // taken out of the glued pieces' layer
        var stops = new List<(double Z, PathsD Area)>();     // a strip a door shuts against, added to a layer
        var pinHoles = new PathsD();       // in the base plate
        var slides = new List<(double X0, double X1, double Bottom, double Top)>();   // a portcullis's channel up inside the wall
        var fillPieces = new List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Pose Pose)>();
        var hingeAt = new Dictionary<string, double>();   // the leaves that swing, and where their pins are (u)
        double lifts = 0;
        var said = new HashSet<string>();
        void Say(string t) { if (said.Add(t)) findings.Add(new(Severity.Note, t)); }

        // A piece standing upright between the faces at z (drawn in the wall's x, y), moved so its corner is at 0, 0.
        void Stand(string name, PathsD cut, PathsD etched, List<PathD> lines, double z)
        {
            var b = Geo.Bounds(cut);
            PointD To(PointD p) => new(p.x - b.left, p.y - b.top);
            fillPieces.Add((name, Geo.Tidy(Geo.Map(cut, To)), Geo.Map(etched, To), Geo.Map(lines, To), Pose.Front(b.left, b.top, z)));
        }

        // A plate across an opening between the faces (from y0 up one sheet), with a hole for each pin.
        void Plate(string name, double x0, double x1, double y0, IEnumerable<double> pins)
        {
            var cut = Geo.Subtract([Geo.Rect(0, 0, x1 - x0, G)], new PathsD(pins.Select(px => Geo.Circle(px - x0, G / 2, pinD / 2))));
            fillPieces.Add((name, cut, [], [], Pose.Up(x0, y0 + T / 2, G / 2)));
            bands.Add(Geo.Rect(x0 - 0.01, y0, x1 + 0.01, y0 + T));
            taken.Add((x0 - 2, x1 + 2, y0 - 1, y0 + T + 1));
        }

        // A leaf from x0 to x1 and y0 to y1, a square pin on its hinge side at the top and bottom (from pinLow to pinHigh).
        double Leaf(string name, double x0, double x1, double y0, double y1, bool hingeLeft, double pinLow, double pinHigh, DoorLook look)
        {
            double px = hingeLeft ? x0 + T / 2 : x1 - T / 2;
            var cut = Geo.Union(new PathsD { Geo.Rect(x0, y0, x1, y1), Geo.Rect(px - T / 2, pinLow, px + T / 2, y0 + 0.01), Geo.Rect(px - T / 2, y1 - 0.01, px + T / 2, pinHigh) });
            var (e, l) = WallKitMaker.DoorFace([Geo.Rect(x0, y0, x1, y1)], s.Etch, groove, look);
            Stand(name, cut, e, l, 0);
            hingeAt[name] = px - x0;
            return px;
        }

        // A piece glued into a groove round an opening (a door, a window's frame or shutter), in the middle layer.
        void Glued(string name, PathsD shape, PathsD cut, PathsD etched, List<PathD> lines)
        {
            Stand(name, cut, etched, lines, zi);
            grooves.AddRange(Geo.Offset(shape, gIns));
            holes[^1] = holes[^1] with { Groove = gIns };
        }
        // An opening's outline grown into its groove, standing on y ≥ floorY.
        PathsD InGroove(PathsD shape, double floorY)
        {
            var b = Geo.Bounds(shape);
            return Geo.Intersect(Geo.Offset(shape, gIns - fit / 2), [Geo.Rect(b.left - 5, floorY, b.right + 5, b.bottom + 5)]);
        }

        // The arched or pointed head of an opening above a hinged leaf's head plate: a panel glued in a groove.
        void ArchPanel(PathsD shape, double from, DoorLook look)
        {
            var b = Geo.Bounds(shape);
            if (b.bottom - from < 1.5) return;
            var above = new PathsD { Geo.Rect(b.left - 5, from, b.right + 5, b.bottom + 5) };
            var piece = Geo.Intersect(Geo.Offset(shape, gIns - fit / 2), above);
            var seen = Geo.Intersect(shape, above);
            if (Geo.Area(seen) < 2) return;
            var (e, l) = WallKitMaker.DoorFace(seen, s.Etch, groove, look);
            Stand("Arch panel", piece, e, l, zi);
            grooves.AddRange(Geo.Intersect(Geo.Offset(shape, gIns), above));
            holes[^1] = holes[^1] with { Groove = gIns };
        }

        // What hangs in the doorway: a door on hinges, glued in, or a portcullis that slides up inside the wall.
        void DoorFill(OpeningShape kind, PathsD shape, double x, double w, double h)
        {
            var b = Geo.Bounds(shape);
            if (s.DoorLook == DoorLook.Bars)
            {
                Glued("Portcullis", shape, Geo.Subtract(InGroove(shape, T), WallKitMaker.BarGaps(shape)), [], []);
                if (s.DoorOpens) slides.Add((b.left - gIns, b.right + gIns, T, b.bottom));
                return;
            }
            var (ax0, ax1, spring) = Springing(kind, shape, x, T, w, h);
            bool left = s.DoorHinge == HingeSide.Left;
            double lx0 = left ? ax0 + cH : ax0 + 0.5, lx1 = left ? ax1 - 0.5 : ax1 - cH, ly0 = T + 0.4, ly1 = spring - 0.4;
            string? why = !s.DoorOpens ? ""
                : !pinRoom ? $"The door is glued in place: a wall {2 * T + pinD + 1:0.#} mm thick or more has room for its hinge pins to turn."
                : spring + T > bodyTop - 0.2 ? "The door is glued in place: there's no room over it, inside the wall, for the plate its top pin turns in. Make the door lower or the wall taller."
                : lx1 - lx0 < T + 3 || ly1 - ly0 < 4 ? "The door is glued in place: it's too small to hang on hinges."
                : null;
            if (why is not null)
            {
                if (why.Length > 0) Say(why);
                var (e, l) = WallKitMaker.DoorFace(shape, s.Etch, groove, s.DoorLook);
                Glued("Door", shape, InGroove(shape, T), e, l);
                return;
            }
            double px = Leaf("Door", lx0, lx1, ly0, ly1, left, 0.2, spring + T - 0.2, s.DoorLook);
            pinHoles.Add(Geo.Circle(px, 0, pinD / 2));
            Plate("Door head", b.left - RevealMm, b.right + RevealMm, spring, [px]);
            // A strip the door shuts against, on its latch side behind it: in the nearest layer wholly behind it, or the back face.
            var strip = new PathsD { left ? Geo.Rect(lx1 - 1, T, b.right + 0.01, spring) : Geo.Rect(b.left - 0.01, T, lx0 + 1, spring) };
            var behind = zs.Where(z => z <= -T + 1e-6).ToList();
            if (behind.Count > 0) stops.Add((behind.Max(), strip));
            else holes[^1] = holes[^1] with { BackCut = Geo.Subtract(holes[^1].BackCut, strip) };
            ArchPanel(shape, spring + T, s.DoorLook);
        }

        // What fills a window: a frame with its bars or a shutter glued in, or shutters that open.
        void WindowFillIn(PathsD win, double x, double sill, double w, double h)
        {
            var fill = s.WindowFill;
            var look = s.DoorLook is DoorLook.Metal or DoorLook.Studded ? s.DoorLook : DoorLook.Planks;
            if (fill == WindowFill.Shutters)
            {
                var kind = s.WindowShape;
                var b = Geo.Bounds(win);
                var (ax0, ax1, spring) = Springing(kind, win, x - w / 2, sill, w, h);
                double y0 = sill + 0.4, y1 = spring - 0.4, half = (ax1 - ax0) / 2, mid = (ax0 + ax1) / 2;
                // Room between a pair for their corners to pass as they swing open together.
                double wl = half - cH, gap = Math.Sqrt(wl * wl + T * T / 4) - wl + 0.15;
                bool pair = wl - gap >= T + 1.5, single = ax1 - ax0 - cH - 0.5 >= T + 2;
                string? why = kind is OpeningShape.Round or OpeningShape.ArrowSlit ? "Round windows and arrow slits have a shutter glued in: they can't have shutters that open."
                    : !pinRoom ? $"The shutters are glued in place: a wall {2 * T + pinD + 1:0.#} mm thick or more has room for their hinge pins to turn."
                    : sill - T < T + 0.5 ? "The shutters are glued in place: the sill is too low for the plate under them. Raise the windows."
                    : spring + T > bodyTop - 0.2 ? "The shutters are glued in place: there's no room over the windows, inside the wall, for the plate their top pins turn in. Make the windows lower or the wall taller."
                    : !pair && !single || y1 - y0 < 4 ? "The shutters are glued in place: the windows are too small to hang shutters on hinges."
                    : null;
                if (why is null)
                {
                    var pins = new List<double>();
                    double lo = sill - T + 0.2, hi = spring + T - 0.2;
                    if (pair)
                    {
                        pins.Add(Leaf("Left shutter", ax0 + cH, mid - gap, y0, y1, true, lo, hi, look));
                        pins.Add(Leaf("Right shutter", mid + gap, ax1 - cH, y0, y1, false, lo, hi, look));
                    }
                    else
                    {
                        bool left = s.DoorHinge == HingeSide.Left;
                        pins.Add(Leaf("Shutter", left ? ax0 + cH : ax0 + 0.5, left ? ax1 - 0.5 : ax1 - cH, y0, y1, left, lo, hi, look));
                    }
                    Plate("Window sill", b.left - RevealMm, b.right + RevealMm, sill - T, pins);
                    Plate("Window head", b.left - RevealMm, b.right + RevealMm, spring, pins);
                    ArchPanel(win, spring + T, look);
                    return;
                }
                Say(why);
                fill = WindowFill.Shutter;
            }
            if (fill == WindowFill.Shutter)
            {
                var (e, l) = WallKitMaker.DoorFace(win, s.Etch, groove, look);
                Glued("Shutter", win, InGroove(win, -1e6), e, l);
            }
            else if (fill == WindowFill.Frame)
            {
                double fw = Math.Clamp(s.BarMm, 0.8, 2.5);
                var inside = Geo.Offset(win, -fw);
                if (Geo.Area(inside) < 0.3 * Geo.Area(win)) return;   // an arrow slit or a tiny window: a frame would all but fill it
                var panes = Geo.Intersect(WallKitMaker.Bars(win, s.Bars, Math.Max(0.6, s.BarMm), x, sill, w, h), inside);
                Glued("Window frame", win, Geo.Subtract(InGroove(win, -1e6), panes), [], []);
            }
        }

        void Frame(PathsD shape)
        {
            if (frame <= 0) return;
            var outer = Geo.Offset(shape, frame, round: true);
            keepClear.AddRange(outer);
            if (s.Etch == EtchStyle.Grooves) frameRings.AddRange(Geo.Subtract(outer, Geo.Offset(shape, Math.Max(0.05, frame - groove), round: true)));
            else frameLines.AddRange(outer.Select(Geo.Loop));
        }

        if (s.Door != DoorKind.None)
        {
            double w = Math.Max(3, s.DoorWidthMm), h = Math.Max(3, s.DoorHeightMm), cx = L / 2 + s.DoorOffsetMm;
            var shape = s.DoorShape is OpeningShape.Round or OpeningShape.ArrowSlit ? OpeningShape.Arched : s.DoorShape;
            double room = Math.Min(front.Lowest(cx - w / 2 - frame, cx + w / 2 + frame), back.Lowest(cx - w / 2 - frame, cx + w / 2 + frame));
            if (cx - w / 2 - margin < inner0 || cx + w / 2 + margin > inner1)
                Bad("The door doesn't fit between the ends: make it narrower, move it nearer the middle or the wall longer.");
            else if (T + h + frame > room - 1.5)
                Bad($"The door is too tall for the wall: make it lower than {room - 1.5 - frame - T:0.#} mm.");
            else
            {
                var shapeD = WallKitMaker.Opening(shape, cx - w / 2, T, w, h);
                taken.Add((cx - w / 2 - margin, cx + w / 2 + margin, 0, T + h + margin));
                Frame(shapeD);
                if (s.Door == DoorKind.ClosedDoor)
                {
                    keepClear.AddRange(shapeD);
                    var (de, dl) = WallKitMaker.DoorFace(shapeD, s.Etch, groove, s.DoorLook);
                    doorEtched.AddRange(de);
                    doorLines.AddRange(dl);
                }
                else
                {
                    holes.Add(new Hole(shapeD, shapeD, shapeD, cx - w / 2, cx + w / 2, T, T + h, true));
                    if (s.Door == DoorKind.DoorwayAndDoor) DoorFill(shape, shapeD, cx - w / 2, w, h);
                }
            }
        }

        if (WallKitMaker.WindowCount(s) > 0)
        {
            double w = Math.Max(1, s.WindowWidthMm), h = s.WindowShape == OpeningShape.Round ? w : Math.Max(1, s.WindowHeightMm), sill = s.SillMm;
            if (sill < T + Math.Max(frame, 1) + 1 || sill + h + frame > H - 1.5)
                Bad($"The windows don't fit in the wall's height: keep the sill above {T + Math.Max(frame, 1) + 1:0.#} mm and the top under {H - frame - 1.5:0.#} mm.");
            else
            {
                List<(double X0, double X1)> clear = [.. taken.Where(t => t.Y0 < sill + h + margin && t.Y1 > sill - margin).Select(t => (t.X0, t.X1))];
                var xs = s.WindowAtMm.Count > 0 ? WallKitMaker.Placed(s.WindowAtMm, L, w + 2 * margin, inner0, inner1, clear, 2) : WallKitMaker.Spread(s.Windows, w + 2 * margin, inner0, inner1, clear, 2);
                int fitted = 0;
                foreach (var x in xs)
                {
                    double top = sill + h + frame;
                    if (top > Math.Min(front.Lowest(x - w / 2 - frame, x + w / 2 + frame), back.Lowest(x - w / 2 - frame, x + w / 2 + frame)) - 1.5) continue;
                    var win = WallKitMaker.Opening(s.WindowShape, x - w / 2, sill, w, h);
                    taken.Add((x - w / 2 - margin, x + w / 2 + margin, sill - margin, sill + h + margin));
                    Frame(win);
                    // Left open, the bars are left in the front face and the back face's window is open; filled, both are clear.
                    var front0 = s.WindowFill == WindowFill.Open ? WallKitMaker.Bars(win, s.Bars, Math.Max(0.6, s.BarMm), x, sill, w, h) : win;
                    holes.Add(new Hole(win, front0, win, x - w / 2, x + w / 2, sill, sill + h, false));
                    WindowFillIn(win, x, sill, w, h);
                    fitted++;
                }
                if (fitted < WallKitMaker.WindowCount(s)) findings.Add(new(Severity.Warn, fitted == 0 ? "No window fits beside the door and the ends, under the top." : $"Only {fitted} of the {WallKitMaker.WindowCount(s)} windows fit."));
            }
        }
        var allHoles = Geo.Union(new PathsD(holes.SelectMany(h => h.Shape)));

        // ---- mount points (slots through the front face, or the back's) ----
        double slotW = T + fit, slotH = Math.Clamp(s.MountSlotMm, 2, 30);
        var mountXs = new List<double>();
        var mountBlocks = new List<(double X0, double X1)>();   // where the ribs keep clear of the accessories' tabs
        var frontSlots = new PathsD();
        var backSlots = new PathsD();
        var frontClear = new PathsD();    // where the front's texture stops (the back keeps clear of its own slots)
        List<(MountPoint Mount, List<PathD> Slots)> placed = [];
        if (s.MountPoints.Count > 0)
        {
            placed = Mounts.Place(s, inner0 + 2, inner1 - 2, T + 2, (a, b, onBack) => Math.Min(bodyTop, (onBack ? back : front).Lowest(a, b)), taken, findings);
            foreach (var (m, slots) in placed)
            {
                (m.OnBack ? backSlots : frontSlots).AddRange(slots);
                var b = m.Bounds(s);
                if (!m.OnBack) frontClear.AddRange(Geo.Offset(new PathsD(slots), 0.5));
                taken.Add((b.X0 - 2, b.X1 + 2, b.Y0 - 1, b.Y1 + 1));
                foreach (var sl in slots) { var r = Geo.Bounds([sl]); mountBlocks.Add((r.left - 1.5, r.right + 1.5)); }
                used.Add(m);
            }
        }
        else if (s.Mounts > 0)
        {
            double y0 = s.MountHeightMm - slotH / 2, y1 = s.MountHeightMm + slotH / 2;
            if (y0 < T + 2 || y1 > Math.Min(bodyTop, front.Min) - 2)
                Bad($"The mount points are too high or too low: put them between {T + 2 + slotH / 2:0.#} and {Math.Min(bodyTop, front.Min) - 2 - slotH / 2:0.#} mm.");
            else
            {
                mountXs = WallKitMaker.Spread(s.Mounts, slotW + 4, inner0 + 2, inner1 - 2, [.. taken.Where(t => t.Y0 < y1 + 1 && t.Y1 > y0 - 1).Select(t => (t.X0, t.X1))], 4);
                if (mountXs.Count < s.Mounts) findings.Add(new(Severity.Warn, $"Only {mountXs.Count} of the {s.Mounts} mount points fit clear of the doors and windows."));
                for (int i = 0; i < mountXs.Count; i++)
                {
                    double x = mountXs[i];
                    used.Add(new MountPoint { XMm = Math.Round(x, 2), YMm = s.MountHeightMm, SlotHeightMm = slotH, Item = Mounts.Legacy(s.Accessories, i) });
                    mountBlocks.Add((x - slotW / 2 - 1.5, x + slotW / 2 + 1.5));
                    frontSlots.Add(Geo.Rect(x - slotW / 2, y0, x + slotW / 2, y1));
                    keepClear.Add(Geo.Rect(x - slotW / 2 - 0.5, y0 - 0.5, x + slotW / 2 + 0.5, y1 + 0.5));
                    taken.Add((x - slotW / 2 - 2, x + slotW / 2 + 2, y0 - 1, y1 + 1));
                }
            }
        }

        // ---- a walkway wider than the wall rests on brackets plugged into the back face ----
        double over = walkway ? Math.Max(0, Math.Clamp(s.WalkwayDepthMm, 4, 80) - (W - 2 * T)) : 0;
        var bracketXs = new List<double>();
        double bracketY0 = bodyTop - 1.5 - slotH;
        if (over > T)
        {
            int nb = Math.Max(2, (int)Math.Round((inner1 - inner0) / 40) + 1);
            if (bracketY0 < T + 2) findings.Add(new(Severity.Warn, "There's no room under the walkway for its brackets."));
            else
            {
                bracketXs = WallKitMaker.Spread(nb, slotW + 4, inner0 + 2, inner1 - 2, [.. taken.Where(t => t.Y0 < bodyTop && t.Y1 > bracketY0 - 1).Select(t => (t.X0, t.X1))], 4);
                foreach (var x in bracketXs)
                {
                    backSlots.Add(Geo.Rect(x - slotW / 2, bracketY0, x + slotW / 2, bracketY0 + slotH));
                    taken.Add((x - slotW / 2 - 2, x + slotW / 2 + 2, bracketY0 - 1, bodyTop));
                }
                if (bracketXs.Count < 2) findings.Add(new(Severity.Warn, "There's no room for the walkway's brackets clear of the windows."));
            }
        }

        // ---- ribs between the faces, clear of the openings, slots and brackets ----
        int ribs = Math.Max(0, (int)Math.Ceiling((inner1 - inner0) / Math.Max(15, s.RibSpacingMm)) - 1);
        var ribBlock = holes.Select(h => (h.X0 - RevealMm - 1, h.X1 + RevealMm + 1))
            .Concat(mountBlocks)
            .Concat(bracketXs.Select(x => (x - slotW / 2 - 1.5, x + slotW / 2 + 1.5))).ToList();
        var ribXs = WallKitMaker.Spread(ribs, T, inner0 + 6, inner1 - 6, ribBlock, 6);

        // ---- tabs on the faces' bottoms into the base, clear of the doorway, the ribs and the ends ----
        double tw = Math.Clamp(L / 12, 4, 10);
        int faceTabs = Math.Max(2, (int)Math.Round(L / 35));
        var tabBlock = holes.Where(h => h.Y0 <= T + 0.01).Select(h => (h.X0 - 2, h.X1 + 2))
            .Concat(ribXs.Select(x => (x - T / 2 - 2, x + T / 2 + 2))).ToList();
        var tabXs = WallKitMaker.Spread(faceTabs, tw, inner0 + 2, inner1 - 2, tabBlock, 4);
        var endTabs = intoColumns ? EndTabs(s) : [];
        if (intoColumns && endTabs.Count == 0) Bad("The wall is too low for tabs into the columns: make it taller.");
        double tl = TabLength(s);

        PathsD FaceOutline(Profile p)
        {
            var o = new PathsD(p.Under(T));
            foreach (var x in tabXs) o.Add(Geo.Rect(x - tw / 2, 0, x + tw / 2, T + 0.01));
            foreach (var (y0, y1) in endTabs)
            {
                o.Add(Geo.Rect(-tl, y0, 0.01, y1));
                o.Add(Geo.Rect(L - 0.01, y0, L + tl, y1));
            }
            return Geo.Union(o);
        }

        // ---- the faces ----
        var frontCut = Geo.Subtract(FaceOutline(front), Geo.Union(new PathsD(holes.SelectMany(h => h.FrontCut).Concat(frontSlots))));
        var backCutW = Geo.Subtract(FaceOutline(back), Geo.Union(new PathsD(holes.SelectMany(h => h.BackCut).Concat(backSlots))));
        if (Geo.Pieces(frontCut) != 1 || Geo.Pieces(backCutW) != 1)
        {
            Bad("The doors, windows or broken top cut a face into separate pieces: move or shrink them.");
            return Done();
        }
        var bodyRect = new PathsD { Geo.Rect(0, T, L, H + 1) };
        var (fe, fl) = FaceTexture(s, 0, L, H, Geo.Subtract(Geo.Intersect(frontCut, bodyRect), Geo.Union(keepClear, frontClear)));
        fe.AddRange(Geo.Intersect(Geo.Union(frameRings), frontCut));
        fe.AddRange(doorEtched);
        fl.AddRange(Geo.ClipLines(frameLines, frontCut));
        fl.AddRange(doorLines);
        parts.Add(new Part("Front", frontCut, Geo.Union(fe), fl, walls) { Poses = [Pose.Front(0, 0, W / 2 - T / 2)] });

        // The back, drawn as seen from behind (mirrored).
        PointD M(PointD p) => new(L - p.x, p.y);
        var backCut = Geo.Tidy(Geo.Map(backCutW, M));
        PathsD be = [];
        List<PathD> bl = [];
        if (s.TextureBack)
        {
            var clear = new PathsD(keepClear);
            clear.AddRange(Geo.Offset(backSlots, 0.5));
            var region = Geo.Subtract(Geo.Intersect(backCut, bodyRect), Geo.Map(Geo.Union(clear), M));
            (be, bl) = FaceTexture(s.BackSpec, 1, L, H, Geo.Tidy(region));
            be.AddRange(Geo.Map(Geo.Intersect(Geo.Union(frameRings), backCutW), M));
            bl.AddRange(Geo.Map(Geo.ClipLines(frameLines, backCutW), M));
        }
        if (s.Door == DoorKind.ClosedDoor)
        {
            be.AddRange(Geo.Map(doorEtched, M));
            bl.AddRange(Geo.Map(doorLines, M));
        }
        parts.Add(new Part("Back", backCut, Geo.Union(Geo.Tidy(be)), bl, walls) { Poses = [Pose.Back(L, 0, -W / 2 + T / 2)] });

        // ---- the base plate (drawn with v = −z; the faces stand on it, their tabs through its slots) ----
        double bx0 = intoColumns ? 0 : -pl, bx1 = intoColumns ? L : L + pl, half = W / 2 + pl;
        var baseSlots = new PathsD();
        double outer = pl - fit / 2 < BridgeMm ? half + 1 : W / 2 + fit / 2;   // a slot too near the edge is opened into a notch
        foreach (var x in tabXs)
        {
            baseSlots.Add(Geo.Rect(x - tw / 2 - fit / 2, -outer, x + tw / 2 + fit / 2, -(W / 2 - T - fit / 2)));
            baseSlots.Add(Geo.Rect(x - tw / 2 - fit / 2, W / 2 - T - fit / 2, x + tw / 2 + fit / 2, outer));
        }
        // A rib or an end: tabs on its bottom (in its own u across the gap, z = −G/2 + u, v = −z).
        var ribPiece = Rib(G, T, bodyTop, out var ribTabs, true);
        void InnerSlots(double x) { foreach (var (u0, u1) in ribTabs) baseSlots.Add(Geo.Rect(x - T / 2 - fit / 2, G / 2 - u1 - fit / 2, x + T / 2 + fit / 2, G / 2 - u0 + fit / 2)); }
        foreach (var x in ribXs) InnerSlots(x);
        bool endTabsIn = intoColumns || pl - fit / 2 >= BridgeMm;
        if (endTabsIn) { InnerSlots(endX + T / 2); InnerSlots(L - endX - T / 2); }
        baseSlots.AddRange(pinHoles);
        var basePlate = Geo.Subtract([Geo.Rect(bx0, -half, bx1, half)], Geo.Union(baseSlots));
        parts.Add(new Part("Base", basePlate, [], [], walls) { Poses = [Pose.Up(0, T / 2, 0)] });

        // ---- the top plate between the faces (a walkway reaches back over the back face), split where an opening runs through it ----
        double tv0 = walkway ? -(W / 2 - T) : -G / 2, tv1 = walkway ? W / 2 + over : G / 2;
        var topPlate = new PathsD { Geo.Rect(0, tv0, L, tv1) };
        var band = new PathsD { Geo.Rect(-1, bodyTop - 0.01, L + 1, hb + 0.01) };
        foreach (var h in holes)
        {
            var through = Geo.Intersect(h.Groove > 0 ? Geo.Offset(h.Shape, h.Groove + 0.1) : h.Shape, band);
            if (through.Count == 0) continue;
            var b = Geo.Bounds(through);
            topPlate = Geo.Subtract(topPlate, [Geo.Rect(b.left, tv0 - 1, b.right, tv1 + 1)]);
        }
        foreach (var piece in Separate(topPlate))
        {
            var (te, tlines) = s.TextureSides ? FaceTexture(s, 2, L, tv1 - tv0, Geo.Move(piece, 0, -tv0)) : ([], []);
            parts.Add(new Part(walkway ? "Walkway" : "Top", piece, Geo.Move(te, 0, tv0), [.. tlines.Select(l => Geo.Map(l, q => new PointD(q.x, q.y + tv0)))], walls) { Poses = [Pose.Up(0, hb - T / 2, 0)] });
        }

        // ---- the ends ----
        var endPiece = endTabsIn ? ribPiece : Rib(G, T, bodyTop, out _, false);
        PathsD endEtched = [];
        List<PathD> endLines = [];
        if (s.TextureSides && !intoColumns) (endEtched, endLines) = FaceTexture(s, 3, G, H, Geo.Intersect(endPiece, [Geo.Rect(0, T, G, H)]));
        parts.Add(new Part("End", endPiece, endEtched, endLines, 2 * walls) { Poses = [Pose.Left(endX + T / 2, 0, -G / 2), Pose.Right(L - endX - T / 2, 0, G / 2)] });
        if (ribXs.Count > 0)
            parts.Add(new Part("Rib", ribPiece, [], [], ribXs.Count * walls) { Poses = [.. ribXs.Select(x => Pose.Left(x, 0, -G / 2))] });

        // ---- layers between the faces above the box: merlons, a parapet's backing or the broken top ----
        var coreParts = new List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Pose Pose)>();
        foreach (var (z, p) in core)
        {
            var region = Geo.Subtract(p.Under(hb), allHoles);
            if (Math.Abs(z - zi) < 1e-6 && grooves.Count > 0) region = Geo.Subtract(region, Geo.Union(grooves));
            foreach (var piece in Separate(region))
            {
                string name = walkway ? "Parapet backing" : breaks == RuinStyle.None ? "Merlon filling" : "Broken top layer";
                if (walkway && s.TextureSides)
                {
                    // The parapet's back faces the walkway: drawn and textured as seen from behind, like the back face.
                    var mirrored = Geo.Tidy(Geo.Map(piece, M));
                    var (e2, l2) = FaceTexture(s, 4, L, H, mirrored);
                    coreParts.Add((name, mirrored, e2, l2, Pose.Back(L, 0, z)));
                }
                else coreParts.Add((name, piece, [], [], Pose.Front(0, 0, z)));
            }
        }
        foreach (var g in Group(coreParts))
            parts.Add(new Part(g.Name, g.Cut, g.Etched, g.Lines, g.Poses.Count * walls) { Poses = g.Poses });

        // ---- linings round doors and windows: rings of sheet between the faces ----
        if (holes.Count > 0)
        {
            var rings = Geo.Subtract(Geo.Union(new PathsD(holes.SelectMany(h => Geo.Offset(h.Shape, RevealMm)))), allHoles);
            var inside = new PathsD { Geo.Rect(inner0 + 0.3, T, inner1 - 0.3, bodyTop) };
            rings = Geo.Intersect(rings, inside);
            if (bands.Count > 0) rings = Geo.Subtract(rings, Geo.Union(bands));
            // A portcullis slides up a channel in its layer, as far as whatever's above it allows.
            var cut = new PathsD(grooves);
            foreach (var (sx0, sx1, bottom, top) in slides)
            {
                double ceiling = bodyTop - 0.2;
                bool Over(double a, double b) => a < sx1 + 0.5 && b > sx0 - 0.5;
                foreach (var h in holes) if (h.Y0 > top && Over(h.X0 - RevealMm, h.X1 + RevealMm)) ceiling = Math.Min(ceiling, h.Y0 - Math.Max(RevealMm, T) - 0.5);
                foreach (var m in used) { var mb = m.Bounds(s); if (mb.Y0 > top && Over(mb.X0, mb.X1)) ceiling = Math.Min(ceiling, mb.Y0 - 0.5); }
                foreach (var bx in bracketXs) if (Over(bx - slotW / 2, bx + slotW / 2)) ceiling = Math.Min(ceiling, bracketY0 - 0.5);
                double lift = ceiling - top - gIns;   // its top is in the groove over the doorway
                if (lift >= 1)
                {
                    cut.Add(Geo.Rect(sx0, bottom, sx1, ceiling));
                    lifts = Math.Floor(lift * 10) / 10;
                    Say($"The portcullis slides up {Math.Floor(lift * 10) / 10:0.#} mm inside the wall; it drops back down on its own.");
                }
                else Say("The portcullis sits in its grooves: there's no room above it inside the wall for it to slide up.");
            }
            var groovesCut = cut.Count > 0 ? Geo.Union(cut) : cut;
            var lining = new List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Pose Pose)>();
            foreach (var z in zs)
            {
                var r = Math.Abs(z - zi) < 1e-6 && groovesCut.Count > 0 ? Geo.Subtract(rings, groovesCut) : rings;
                foreach (var (sz, area) in stops) if (Math.Abs(sz - z) < 1e-6) r = Geo.Union(r, Geo.Intersect(area, inside));
                foreach (var piece in Separate(r).Where(p => Geo.Area(p) >= 2)) lining.Add(("Lining", piece, [], [], Pose.Front(0, 0, z)));
            }
            var groups = Group(lining);
            for (int k = 0; k < groups.Count; k++)
                parts.Add(new Part(groups.Count == 1 ? "Lining" : $"Lining {k + 1}", groups[k].Cut, [], [], groups[k].Poses.Count * walls) { Poses = groups[k].Poses });
        }

        // ---- doors, shutters, frames and the plates they hang from ----
        foreach (var g in Group(fillPieces, sameEtching: true))
            parts.Add(new Part(g.Name, g.Cut, g.Etched, g.Lines, g.Poses.Count * walls)
            {
                Poses = g.Poses,
                HingeU = hingeAt.TryGetValue(g.Name, out var hu) ? hu : null,
                SlideMm = g.Name == "Portcullis" ? lifts : 0,
            });

        // ---- accessories on the mounts, brackets under a walkway ----
        if (placed.Count > 0)
            parts.AddRange(Mounts.Accessories(s, placed.Select(p => p.Mount), tl + 0.7, W / 2, walls));
        else if (mountXs.Count > 0)
        {
            var acc = WallKitMaker.Accessories(s, tl + 0.7, slotH);
            for (int j = 0; j < acc.Count; j++)
            {
                double baseY = acc[j].Name == "Shelf bracket" ? s.MountHeightMm - slotH / 2 : s.MountHeightMm;
                var poses = mountXs.Where((_, i) => i % acc.Count == j).Select(x => Pose.Left(x, baseY, W / 2)).ToList();
                parts.Add(acc[j] with { Copies = mountXs.Count * walls, Poses = poses });
            }
        }
        if (bracketXs.Count >= 2)
            parts.Add(new Part("Walkway bracket", WallKitMaker.Bracket(tl + 0.7, slotH, over - 0.5, 1.5), [], [], bracketXs.Count * walls)
            { Poses = [.. bracketXs.Select(x => Pose.Right(x, bracketY0, -W / 2))] });

        // ---- the columns ----
        parts.AddRange(BoxColumns.Make(s, findings));

        // ---- notes ----
        int each = parts.Where(p => !p.Name.Contains("column", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Copies) / walls;
        findings.Add(new(Severity.Note, $"Each wall is a box {W:0.#} mm thick: {each} pieces (front, back, base, top, two ends{(ribXs.Count > 0 ? $", {ribXs.Count} rib{(ribXs.Count == 1 ? "" : "s")}" : "")}{(holes.Count > 0 ? ", linings" : "")}{(fillPieces.Count > 0 ? ", what fills the door and windows" : "")}{(core.Count > 0 ? (walkway ? ", the parapet's backing" : breaks == RuinStyle.None ? ", merlon filling" : ", the broken top's layers") : "")})."));
        findings.Add(new(Severity.Note, "Put it together on the base: the faces' tabs and the ribs into its slots, the ends, linings and ribs glued to the inside of the front, the top plate on, then the back. Glue as you go."));
        if (fillPieces.Any(f => f.Name is "Door head" or "Window head"))
            findings.Add(new(Severity.Note, "Hang the door and shutters before the back goes on: each one's bottom pin in its hole (in the base, or the window's sill plate), the head plate over its top pin and glued to the linings. Then they swing open."));
        if (grooves.Count > 0)
            findings.Add(new(Severity.Note, "The glued-in pieces (frames, shutters, a door or arch panels) go in the grooves in the middle layer of the linings as you build it up."));
        if (walkway) findings.Add(new(Severity.Note, $"The walkway is {hb:0.#} mm up, {WallKitMaker.WalkwayDropMm:0} mm under the gaps in the battlements, and {Math.Clamp(s.WalkwayDepthMm, 4, 80):0.#} mm deep behind the parapet."));
        if (s.Measure == LengthMeasure.Centres) findings.Add(new(Severity.Note, $"Each wall is {L:0.#} mm long, so the columns' centres are {s.LengthMm:0.#} mm apart."));
        else if (intoColumns && s.Connectors.Values.Any(c => c > 0)) findings.Add(new(Severity.Note, $"Columns' centres end up {L + 2 * WallKitMaker.CentreToEnd(s):0.#} mm apart."));
        return Done();
    }

    /// <summary>
    /// Where a piece sits with the doors open: a hinged leaf turned <paramref name="deg"/> on its pins, its far
    /// edge out to the front; a portcullis slid all the way up. Anything else stays put.
    /// </summary>
    public static Pose Opened(Part part, Pose pose, double deg = 75)
    {
        if (part.SlideMm > 0) return pose with { O = pose.O + new V3(0, part.SlideMm, 0) };
        if (part.HingeU is not double hu) return pose;
        var b = Geo.Bounds(part.Cut);
        var p = pose.At(hu, 0);
        double a = deg * Math.PI / 180 * (hu < (b.left + b.right) / 2 ? 1 : -1), c = Math.Cos(a), sn = Math.Sin(a);
        // Turned about the upright line through the pins (the piece stands upright, facing the front).
        V3 R(V3 v) => new(v.X * c - v.Z * sn, v.Y, v.X * sn + v.Z * c);
        return new Pose(p + R(pose.O - p), R(pose.U), pose.V);
    }

    /// <summary>
    /// The upright part of an opening (its bottom-left corner at x, y), up to where its head starts to narrow
    /// (its springing): the x range a flat leaf fits in, and the top. Round windows and arrow slits have none.
    /// </summary>
    internal static (double X0, double X1, double Top) Springing(OpeningShape kind, PathsD outline, double x, double y, double w, double h)
    {
        double top = kind switch
        {
            OpeningShape.Arched or OpeningShape.Rough => Math.Max(y, y + h - w / 2),
            OpeningShape.Pointed => Math.Max(y, y + h - w * Math.Sqrt(3) / 2),
            OpeningShape.Chamfered => y + h - Math.Min(w * 0.3, h * 0.3),
            OpeningShape.Rectangle => y + h,
            _ => y,
        };
        if (kind != OpeningShape.Rough) return (x, x + w, top);
        // A rough mouth's sides wander in and out: a leaf goes between the narrowest of them, below where its top wanders down.
        double x0 = x - 1, x1 = x + w + 1;
        top -= w * 0.1;
        for (double yy = y + 0.1; yy <= top; yy += 0.25)
        {
            var cuts = Geo.ClipLines([[new PointD(x - 2, yy), new PointD(x + w + 2, yy)]], outline);
            if (cuts.Count == 0) return (x, x, y);
            var c = cuts.OrderByDescending(p => Math.Abs(p[^1].x - p[0].x)).First();
            x0 = Math.Max(x0, Math.Min(c[0].x, c[^1].x));
            x1 = Math.Min(x1, Math.Max(c[0].x, c[^1].x));
        }
        return (x0, x1, top);
    }

    /// <summary>A rib (or an end): across the gap between the faces (u, 0 to G), from the base up to the top plate, with tabs into the base.</summary>
    static PathsD Rib(double g, double t, double top, out List<(double U0, double U1)> tabs, bool withTabs)
    {
        var o = new PathsD { Geo.Rect(0, t, g, top) };
        tabs = [];
        if (withTabs)
        {
            if (g >= 10) { double w = Math.Min(g / 4, 6); tabs.Add((g / 4 - w / 2, g / 4 + w / 2)); tabs.Add((g * 3 / 4 - w / 2, g * 3 / 4 + w / 2)); }
            else tabs.Add((g / 4, g * 3 / 4));
            foreach (var (u0, u1) in tabs) o.Add(Geo.Rect(u0, 0, u1, t + 0.01));
        }
        return Geo.Union(o);
    }

    /// <summary>The gaps in battlements (x ranges): merlons all the same width, a whole one at each end.</summary>
    public static List<(double X0, double X1)> Crenels(double L, double merlonW, double crenelW)
    {
        double mw = Math.Max(1, merlonW), cw = Math.Max(1, crenelW);
        int gaps = (int)Math.Floor((L - mw) / (mw + cw));
        if (gaps < 1) return [];
        double m = (L - gaps * cw) / (gaps + 1);
        return [.. Enumerable.Range(0, gaps).Select(i => ((i + 1) * m + i * cw, (i + 1) * (m + cw)))];
    }

    /// <summary>The texture's stones on a face, in the front's x (the back's are drawn as seen from behind).</summary>
    static PathsD Stones(WallSpec s, int face, double L, double H)
    {
        var raised = Textures.Make(face == 0 ? s : s with { Seed = s.Seed + face * 101 }, new Face(L, H, 0, L, H)).Raised;
        return face == 1 ? Geo.Map(raised, p => new PointD(L - p.x, p.y)) : raised;
    }

    /// <summary>A texture over a face (its own flat coordinates, from 0, 0), kept inside <paramref name="region"/>.</summary>
    internal static (PathsD Etched, List<PathD> Lines) FaceTexture(WallSpec s, int face, double length, double height, PathsD region)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (region.Count == 0) return (etched, lines);
        var tex = Textures.Make(face == 0 ? s : s with { Seed = s.Seed + face * 101 }, new Face(length, height, 0, length, height));
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-1, -1, length + 1, height + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(mortar, tex.Etched), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), region));
            lines.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), region));
        }
        lines.AddRange(Geo.ClipLines(tex.Lines, region));
        return (etched, lines);
    }

    /// <summary>Areas split into their separate pieces (each with its holes).</summary>
    internal static List<PathsD> Separate(PathsD areas)
    {
        var tree = new PolyTreeD();
        var c = new ClipperD(4);
        c.AddSubject(areas);
        c.Execute(ClipType.Union, FillRule.EvenOdd, tree);
        var pieces = new List<PathsD>();
        void Walk(PolyPathD node)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var outer = node[i];
                if (Math.Abs(Clipper.Area(outer.Polygon!)) < 1e-6) continue;
                var piece = new PathsD { outer.Polygon! };
                for (int j = 0; j < outer.Count; j++)
                {
                    piece.Add(outer[j].Polygon!);
                    Walk(outer[j]);   // islands inside holes are pieces of their own
                }
                pieces.Add(piece);
            }
        }
        Walk(tree);
        return pieces;
    }

    /// <summary>Pieces of the same name and shape gathered into one part, with every place one sits.</summary>
    /// <remarks>Etched pieces are only gathered when <paramref name="sameEtching"/> (each was etched the same way from its own outline).</remarks>
    static List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses)> Group(IEnumerable<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Pose Pose)> pieces, bool sameEtching = false)
    {
        var groups = new List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses, string Key)>();
        foreach (var p in pieces)
        {
            var b = Geo.Bounds(p.Cut);
            // Moved so its corner is at 0, 0: the same shape anywhere along the wall is the same piece.
            var cut = Geo.Move(p.Cut, -b.left, -b.top);
            string key = $"{p.Name}|{Geo.Area(cut):0.00}|{b.right - b.left:0.00}|{b.bottom - b.top:0.00}|{p.Etched.Count}|{p.Lines.Count}|{Geo.Area(p.Etched):0.0}";
            var pose = p.Pose with { O = p.Pose.At(b.left, b.top) };
            int i = groups.FindIndex(g => g.Key == key && (sameEtching || g.Etched.Count == 0 && g.Lines.Count == 0 && p.Etched.Count == 0 && p.Lines.Count == 0));
            if (i >= 0) groups[i].Poses.Add(pose);
            else groups.Add((p.Name, cut, Geo.Move(p.Etched, -b.left, -b.top), [.. p.Lines.Select(l => Geo.Map(l, q => new PointD(q.x - b.left, q.y - b.top)))], [pose], key));
        }
        return [.. groups.Select(g => (g.Name, g.Cut, g.Etched, g.Lines, g.Poses))];
    }
}
