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

    sealed record Hole(PathsD Shape, PathsD FrontCut, PathsD BackCut, double X0, double X1, double Y0, double Y1, bool Through);

    public static WallKit Make(WallSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        double T = Math.Max(0.3, s.ThicknessMm), W = s.WallMm, G = W - 2 * T, H = s.HeightMm, fit = Math.Max(0, s.FitMm);
        double L = WallKitMaker.PanelLength(s), frame = Math.Max(0, s.FrameMm), pl = Math.Max(0, s.PlinthMm);
        int walls = Math.Max(1, s.Walls);
        WallKit Done() => new(parts, findings, L, W, 0);
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
        bool walkway = Walkway(s);
        if (s.Walkway && s.Top == TopStyle.Battlements && !walkway)
            findings.Add(new(Severity.Warn, "The wall is too low for a walkway behind its battlements: the battlements are filled solid instead."));
        switch (s.Top)
        {
            case TopStyle.Battlements:
            {
                double mh = Math.Clamp(s.MerlonHeightMm, 1, H * 0.6);
                var crenels = Crenels(L, s.MerlonWidthMm, s.CrenelWidthMm);
                if (crenels.Count == 0)
                {
                    findings.Add(new(Severity.Warn, "The wall is too short for battlements: it has a straight top."));
                    front = back = Profile.Level(0, L, H);
                    hb = H;
                    walkway = false;
                    break;
                }
                front = Battlemented(L, H, H - mh, crenels);
                if (walkway)
                {
                    back = Profile.Level(0, L, hb - T);
                    core.Add((W / 2 - 1.5 * T, front));
                }
                else
                {
                    back = front;
                    core.AddRange(zs.Select(z => (z, front)));
                }
                break;
            }
            case TopStyle.Ruined:
            {
                double keep = intoColumns ? 3 : 0;
                front = Ruin(s, L, H, keep, 0, 1);
                back = Ruin(s, L, H, keep, 1, 0.85);
                hb = Math.Min(front.Min, back.Min) - Math.Max(stepY, 2);
                double lowest = 2 * T + 6;
                if (hb < lowest)
                {
                    // Too far down to leave room for the box under the break: the break is made shallower.
                    double up = lowest - hb;
                    front = new Profile(front.Steps.Select(st => (st.X0, st.X1, Math.Min(H, st.Y + up))));
                    back = new Profile(back.Steps.Select(st => (st.X0, st.X1, Math.Min(H, st.Y + up))));
                    hb = lowest;
                    findings.Add(new(Severity.Note, "The top is broken down as far as a box wall this high allows."));
                }
                var rnd = new Random(s.Seed * 104729 + 71);
                foreach (var z in zs)
                {
                    // Each layer between the faces breaks a little lower than the faces here and there, never below the box.
                    var jitter = new Dictionary<double, double>();
                    var p = Profile.Combine(front, back, (x, a, b) =>
                    {
                        double k = Math.Round(x / stepX);
                        if (!jitter.TryGetValue(k, out var j)) jitter[k] = j = rnd.NextDouble() < 0.4 ? stepY : 0;
                        return Math.Max(hb + 1.5, Math.Min(a, b) - j);
                    }).Merged();
                    core.Add((z, p));
                }
                break;
            }
            default:
                front = back = Profile.Level(0, L, H);
                break;
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
        double margin = Math.Max(frame, RevealMm);
        PathsD? door = null;
        double doorX = 0;

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
                    door = shapeD;
                    doorX = cx - w / 2;
                }
            }
        }

        if (s.Windows > 0)
        {
            double w = Math.Max(1, s.WindowWidthMm), h = s.WindowShape == OpeningShape.Round ? w : Math.Max(1, s.WindowHeightMm), sill = s.SillMm;
            if (sill < T + Math.Max(frame, 1) + 1 || sill + h + frame > H - 1.5)
                Bad($"The windows don't fit in the wall's height: keep the sill above {T + Math.Max(frame, 1) + 1:0.#} mm and the top under {H - frame - 1.5:0.#} mm.");
            else
            {
                var xs = WallKitMaker.Spread(s.Windows, w + 2 * margin, inner0, inner1, [.. taken.Where(t => t.Y0 < sill + h + margin && t.Y1 > sill - margin).Select(t => (t.X0, t.X1))], 2);
                int fitted = 0;
                foreach (var x in xs)
                {
                    double top = sill + h + frame;
                    if (top > Math.Min(front.Lowest(x - w / 2 - frame, x + w / 2 + frame), back.Lowest(x - w / 2 - frame, x + w / 2 + frame)) - 1.5) continue;
                    var win = WallKitMaker.Opening(s.WindowShape, x - w / 2, sill, w, h);
                    taken.Add((x - w / 2 - margin, x + w / 2 + margin, sill - margin, sill + h + margin));
                    Frame(win);
                    // Bars are left in the front face; the back face's window is open.
                    holes.Add(new Hole(win, WallKitMaker.Bars(win, s.Bars, Math.Max(0.6, s.BarMm), x, sill, w, h), win, x - w / 2, x + w / 2, sill, sill + h, false));
                    fitted++;
                }
                if (fitted < s.Windows) findings.Add(new(Severity.Warn, fitted == 0 ? "No window fits beside the door and the ends, under the top." : $"Only {fitted} of the {s.Windows} windows fit."));
            }
        }
        var allHoles = Geo.Union(new PathsD(holes.SelectMany(h => h.Shape)));

        // ---- mount points (slots through the front face) ----
        double slotW = T + fit, slotH = Math.Clamp(s.MountSlotMm, 2, 30);
        var mountXs = new List<double>();
        var frontSlots = new PathsD();
        if (s.Mounts > 0)
        {
            double y0 = s.MountHeightMm - slotH / 2, y1 = s.MountHeightMm + slotH / 2;
            if (y0 < T + 2 || y1 > Math.Min(bodyTop, front.Min) - 2)
                Bad($"The mount points are too high or too low: put them between {T + 2 + slotH / 2:0.#} and {Math.Min(bodyTop, front.Min) - 2 - slotH / 2:0.#} mm.");
            else
            {
                mountXs = WallKitMaker.Spread(s.Mounts, slotW + 4, inner0 + 2, inner1 - 2, [.. taken.Where(t => t.Y0 < y1 + 1 && t.Y1 > y0 - 1).Select(t => (t.X0, t.X1))], 4);
                if (mountXs.Count < s.Mounts) findings.Add(new(Severity.Warn, $"Only {mountXs.Count} of the {s.Mounts} mount points fit clear of the doors and windows."));
                foreach (var x in mountXs)
                {
                    frontSlots.Add(Geo.Rect(x - slotW / 2, y0, x + slotW / 2, y1));
                    keepClear.Add(Geo.Rect(x - slotW / 2 - 0.5, y0 - 0.5, x + slotW / 2 + 0.5, y1 + 0.5));
                    taken.Add((x - slotW / 2 - 2, x + slotW / 2 + 2, y0 - 1, y1 + 1));
                }
            }
        }

        // ---- a walkway wider than the wall rests on brackets plugged into the back face ----
        double over = walkway ? Math.Max(0, Math.Clamp(s.WalkwayDepthMm, 4, 80) - (W - 2 * T)) : 0;
        var bracketXs = new List<double>();
        var backSlots = new PathsD();
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
            .Concat(mountXs.Select(x => (x - slotW / 2 - 1.5, x + slotW / 2 + 1.5)))
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
        var (fe, fl) = FaceTexture(s, 0, L, H, Geo.Subtract(Geo.Intersect(frontCut, bodyRect), Geo.Union(keepClear)));
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
            (be, bl) = FaceTexture(s, 1, L, H, Geo.Tidy(region));
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
        var basePlate = Geo.Subtract([Geo.Rect(bx0, -half, bx1, half)], Geo.Union(baseSlots));
        parts.Add(new Part("Base", basePlate, [], [], walls) { Poses = [Pose.Up(0, T / 2, 0)] });

        // ---- the top plate between the faces (a walkway reaches back over the back face), split where an opening runs through it ----
        double tv0 = walkway ? -(W / 2 - T) : -G / 2, tv1 = walkway ? W / 2 + over : G / 2;
        var topPlate = new PathsD { Geo.Rect(0, tv0, L, tv1) };
        var band = new PathsD { Geo.Rect(-1, bodyTop - 0.01, L + 1, hb + 0.01) };
        foreach (var h in holes)
        {
            var through = Geo.Intersect(h.Shape, band);
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
            foreach (var piece in Separate(region))
            {
                string name = walkway ? "Parapet backing" : s.Top == TopStyle.Battlements ? "Merlon filling" : "Broken top layer";
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
            rings = Geo.Intersect(rings, [Geo.Rect(inner0 + 0.3, T, inner1 - 0.3, bodyTop)]);
            var pieces = Separate(rings).Where(p => Geo.Area(p) >= 2).ToList();
            for (int k = 0; k < pieces.Count; k++)
            {
                var b = Geo.Bounds(pieces[k]);
                parts.Add(new Part(pieces.Count == 1 ? "Lining" : $"Lining {k + 1}", Geo.Move(pieces[k], -b.left, -b.top), [], [], zs.Length * walls)
                { Poses = [.. zs.Select(z => Pose.Front(b.left, b.top, z))] });
            }
        }

        // ---- a loose door to fit the doorway ----
        if (s.Door == DoorKind.DoorwayAndDoor && door is not null)
        {
            var leaf = Geo.Offset(door, -WallKitMaker.DoorClearMm);
            var (le, ll) = WallKitMaker.DoorFace(leaf, s.Etch, groove, s.DoorLook);
            var leafCut = s.DoorLook == DoorLook.Bars ? Geo.Subtract(leaf, WallKitMaker.BarGaps(leaf)) : leaf;
            PointD To(PointD p) => new(p.x - doorX, p.y - T);
            parts.Add(new Part(s.DoorLook == DoorLook.Bars ? "Portcullis" : "Door", Geo.Tidy(Geo.Map(leafCut, To)), Geo.Map(le, To), Geo.Map(ll, To), walls)
            { Poses = [Pose.Front(doorX, T, 0)] });
        }

        // ---- accessories on the mounts, brackets under a walkway ----
        if (mountXs.Count > 0)
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
        findings.Add(new(Severity.Note, $"Each wall is a box {W:0.#} mm thick: {each} pieces (front, back, base, top, two ends{(ribXs.Count > 0 ? $", {ribXs.Count} rib{(ribXs.Count == 1 ? "" : "s")}" : "")}{(holes.Count > 0 ? ", linings" : "")}{(core.Count > 0 ? (walkway ? ", the parapet's backing" : s.Top == TopStyle.Battlements ? ", merlon filling" : ", the broken top's layers") : "")})."));
        findings.Add(new(Severity.Note, "Put it together on the base: the faces' tabs and the ribs into its slots, the ends, linings and ribs glued to the inside of the front, the top plate on, then the back. Glue as you go."));
        if (walkway) findings.Add(new(Severity.Note, $"The walkway is {hb:0.#} mm up, {WallKitMaker.WalkwayDropMm:0} mm under the gaps in the battlements, and {Math.Clamp(s.WalkwayDepthMm, 4, 80):0.#} mm deep behind the parapet."));
        if (s.Measure == LengthMeasure.Centres) findings.Add(new(Severity.Note, $"Each wall is {L:0.#} mm long, so the columns' centres are {s.LengthMm:0.#} mm apart."));
        else if (intoColumns && s.Connectors.Values.Any(c => c > 0)) findings.Add(new(Severity.Note, $"Columns' centres end up {L + 2 * WallKitMaker.CentreToEnd(s):0.#} mm apart."));
        return Done();
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

    static Profile Battlemented(double L, double top, double floor, List<(double X0, double X1)> crenels)
    {
        var steps = new List<(double, double, double)>();
        double x = 0;
        foreach (var (c0, c1) in crenels)
        {
            steps.Add((x, c0, top));
            steps.Add((c0, c1, floor));
            x = c1;
        }
        steps.Add((x, L, top));
        return new Profile(steps);
    }

    /// <summary>
    /// A broken top: a dip in stone-sized steps, deepest somewhere near the middle (the same place on both
    /// faces, each broken its own way), the ends whole for <paramref name="keep"/> mm.
    /// </summary>
    static Profile Ruin(WallSpec s, double L, double H, double keep, int face, double depthScale)
    {
        var shared = new Random(s.Seed * 7919 + 13);
        var rnd = new Random(s.Seed * 7919 + 13 + face * 6007);
        double depth = Math.Clamp(s.RuinDepth, 0.05, 0.8) * H * depthScale, u = L - 2 * keep;
        double centre = keep + u * (0.35 + 0.3 * shared.NextDouble()), half = u * (0.3 + 0.15 * shared.NextDouble());
        double stepX = Math.Clamp(s.SizeA / 2, 1.5, 8), stepY = Math.Clamp(s.SizeB, 1, 8);
        var steps = new List<(double, double, double)>();
        if (keep > 0) steps.Add((0, keep, H));
        for (double x = keep; x < L - keep - 1e-6; x += stepX)
        {
            double x1 = Math.Min(L - keep, x + stepX);
            double t = Math.Clamp(Math.Abs((x + x1) / 2 - centre) / half, 0, 1);
            double dip = depth * Math.Pow(Math.Cos(t * Math.PI / 2), 2) + (rnd.NextDouble() - 0.3) * stepY;
            steps.Add((x, x1, H - Math.Max(0, Math.Round(dip / stepY) * stepY)));
        }
        if (keep > 0) steps.Add((L - keep, L, H));
        return new Profile(steps).Merged();
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
    static List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses)> Group(IEnumerable<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Pose Pose)> pieces)
    {
        var groups = new List<(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses, string Key)>();
        foreach (var p in pieces)
        {
            var b = Geo.Bounds(p.Cut);
            // Moved so its corner is at 0, 0: the same shape anywhere along the wall is the same piece.
            var cut = Geo.Move(p.Cut, -b.left, -b.top);
            string key = $"{p.Name}|{Geo.Area(cut):0.00}|{b.right - b.left:0.00}|{b.bottom - b.top:0.00}|{p.Etched.Count}";
            var pose = p.Pose with { O = p.Pose.At(b.left, b.top) };
            int i = groups.FindIndex(g => g.Key == key && g.Etched.Count == 0 && g.Lines.Count == 0 && p.Etched.Count == 0 && p.Lines.Count == 0);
            if (i >= 0) groups[i].Poses.Add(pose);
            else groups.Add((p.Name, cut, Geo.Move(p.Etched, -b.left, -b.top), [.. p.Lines.Select(l => Geo.Map(l, q => new PointD(q.x - b.left, q.y - b.top)))], [pose], key));
        }
        return [.. groups.Select(g => (g.Name, g.Cut, g.Etched, g.Lines, g.Poses))];
    }
}
