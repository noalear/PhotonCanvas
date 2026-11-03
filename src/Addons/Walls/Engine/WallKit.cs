using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>One piece to cut (y up, mm): its outline and holes, what's etched on it as areas, and lines scored on it.</summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies)
{
    /// <summary>
    /// Where copies of it sit in the 3D view: one wall put together, with a column at each end. Pieces
    /// not shown there have none. The face towards the pose's normal is the etched one.
    /// </summary>
    public List<Model.Pose> Poses { get; init; } = [];

    /// <summary>A door or shutter that swings: it turns on pins up the line u = this (its own coordinates), its far edge out to the front.</summary>
    public double? HingeU { get; init; }

    /// <summary>A portcullis: how far it slides up (mm).</summary>
    public double SlideMm { get; init; }

    /// <summary>
    /// Etched on the piece's back, in a second job after it's turned over (left to right). Drawn in the piece's own
    /// coordinates as seen through it from the front, so it lies exactly behind the front; null = nothing.
    /// </summary>
    public PathsD? BackEtched { get; init; }
    /// <summary>Lines scored on the piece's back, as <see cref="BackEtched"/>.</summary>
    public List<PathD>? BackLines { get; init; }
    /// <summary>The piece has something to engrave on its back after turning it over.</summary>
    public bool HasBack => BackEtched is { Count: > 0 } || BackLines is { Count: > 0 };
}

/// <summary>Everything a set of walls is cut from, with what was found while making it.</summary>
public sealed record WallKit(List<Part> Parts, List<Finding> Findings, double PanelLengthMm, double WallThicknessMm, int ColumnLayers)
{
    /// <summary>The mount points the wall ended up with (for walls that only say how many, where they were spread to).</summary>
    public List<MountPoint> MountsUsed { get; init; } = [];
    public Part? Front => Parts.FirstOrDefault();
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
}

/// <summary>
/// Makes the wall panels (outline, top, door, windows, mount slots, texture) and the columns that join
/// them. Doors, windows and mounts keep clear of each other and of the plain ends; anything that
/// doesn't fit is reported rather than squeezed in.
/// </summary>
public static class WallKitMaker
{
    /// <summary>Gap left between the end of a wall and the bottom of a column's slot (mm).</summary>
    public const double EndClearMm = 0.25;
    /// <summary>How much smaller a loose door is than its doorway, all round (mm).</summary>
    public const double DoorClearMm = 0.3;
    /// <summary>How much wider a column's cap and foot are, all round (mm).</summary>
    public const double LipMm = 2;
    /// <summary>The walkway's top below the bottom of the gaps in the battlements (mm): a figure stands chest-high behind them.</summary>
    public const double WalkwayDropMm = 8;

    /// <summary>How thick the wall is (mm): its sheet (twice that for two panels back to back), or a box wall's whole thickness.</summary>
    public static double WallThickness(WallSpec s) => s.Boxed ? s.WallMm : s.ThicknessMm * (s.TwoSided ? 2 : 1);

    /// <summary>Distance from a column's centre to the end of a wall (mm): in its slot, or for a box wall against its side.</summary>
    public static double CentreToEnd(WallSpec s) => s.Boxed ? s.ColumnMm / 2 : s.ColumnMm / 2 - s.SlotDepthMm + EndClearMm;

    /// <summary>How high the top of the columns is (mm), cap and all.</summary>
    public static double ColumnTopMm(WallSpec s) => s.Boxed ? BoxColumns.TopMm(s) : ColumnLayers(s) * s.ThicknessMm + (s.Cap ? s.ThicknessMm : 0);

    /// <summary>How far a column's cap or foot sticks out past its sides (mm).</summary>
    public static double ColumnLip(WallSpec s) => s.Boxed
        ? (s.ColumnTop is ColumnTop.Cap or ColumnTop.SteppedPoint ? s.ColumnLipMm : s.ColumnTop == ColumnTop.SteppedCap ? 2 * s.ColumnLipMm : 0)
        : s.Cap ? LipMm : 0;

    public static double PanelLength(WallSpec s) => s.Measure == LengthMeasure.Centres ? s.LengthMm - 2 * CentreToEnd(s) : s.LengthMm;

    /// <summary>Layers in each column (the foot included, the cap not): enough to reach the top of the wall.</summary>
    public static int ColumnLayers(WallSpec s) => Math.Max(1, (int)Math.Ceiling(s.HeightMm / Math.Max(0.1, s.ThicknessMm) - 0.02));

    public static WallKit Make(WallSpec s)
    {
        if (s.Boxed) return BoxWall.Make(s);
        var findings = new List<Finding>();
        var parts = new List<Part>();
        double L = PanelLength(s), H = s.HeightMm, T = s.ThicknessMm, wallT = WallThickness(s);
        double ez = s.PlainEnds ? s.SlotDepthMm + EndClearMm : 0;
        int layers = ColumnLayers(s);
        if (L < 2 * ez + 5 || L < 10) { findings.Add(new(Severity.Bad, $"The wall is too short ({L:0.#} mm): make it longer.")); return new(parts, findings, L, wallT, layers); }
        if (H < 5) { findings.Add(new(Severity.Bad, "The wall is too low: make it at least 5 mm high.")); return new(parts, findings, L, wallT, layers); }

        var used = new List<MountPoint>();
        var wall = Panel(s, L, H, ez, findings, used);
        if (wall is null) return new(parts, findings, L, wallT, layers);
        parts.Add(new Part("Wall", wall.Cut, wall.Etched, wall.Lines, Math.Max(1, s.Walls))
        {
            Poses = [Model.Pose.Front(0, 0, s.TwoSided ? T / 2 : 0)],
            BackEtched = s.FlipsForBack ? wall.Back?.Etched : null,
            BackLines = s.FlipsForBack ? wall.Back?.Lines : null,
        });
        if (s.TwoSided)
        {
            // Cut as a mirror image: turned over, it lies exactly on the back of the front panel.
            PointD Mirror(PointD p) => new(L - p.x, p.y);
            var (be, bl) = wall.Back ?? (wall.Etched, wall.Lines);
            parts.Add(new Part("Wall back", Geo.Tidy(Geo.Map(wall.Cut, Mirror)), Geo.Tidy(Geo.Map(be, Mirror)), Geo.Map(bl, Mirror), Math.Max(1, s.Walls)) { Poses = [Model.Pose.Back(L, 0, -T / 2)] });
        }
        foreach (var extra in wall.Extras) parts.Add(extra with { Copies = extra.Copies * Math.Max(1, s.Walls) });
        parts.AddRange(Connectors.Make(s, findings));

        if (s.Measure == LengthMeasure.Centres) findings.Add(new(Severity.Note, $"Each panel is {L:0.#} mm long, so the columns' centres are {s.LengthMm:0.#} mm apart."));
        else if (parts.Any(p => p.Name.Contains("column"))) findings.Add(new(Severity.Note, $"Columns' centres end up {L + 2 * CentreToEnd(s):0.#} mm apart."));
        if (s.TwoSided) findings.Add(new(Severity.Note, $"Two panels glued back to back make a {wallT:0.#} mm wall; the columns' slots fit that."));
        if (s.FlipsForBack)
            findings.Add(new(Severity.Note, "The backs are engraved on a page of their own: once the pieces are cut, turn them over left to right, then run that page, lining each piece up with Fit to piece."));
        return new(parts, findings, L, wallT, layers) { MountsUsed = used };
    }

    sealed record PanelResult(PathsD Cut, PathsD Etched, List<PathD> Lines, List<Part> Extras)
    {
        /// <summary>The other side's etching, in the front's x (as seen through the panel); null = none of its own.</summary>
        public (PathsD Etched, List<PathD> Lines)? Back { get; init; }
    }

    static PanelResult? Panel(WallSpec s, double L, double H, double ez, List<Finding> findings, List<MountPoint> used)
    {
        double T = s.ThicknessMm, wallT = WallThickness(s), frame = Math.Max(0, s.FrameMm), fit = Math.Max(0, s.FitMm);
        // Frames and doors use the texture's groove, or 0.6 mm when the texture has none of its own.
        double groove = Textures.Preset(s.Texture) is { UsesGroove: false } || s.GrooveMm < 0.2 ? 0.6 : Math.Min(s.GrooveMm, 3);
        var extras = new List<Part>();

        // The outline, with its top.
        var panel = new PathsD { Geo.Rect(0, 0, L, H) };
        double body = H;
        var outline = Profile.Level(0, L, H);
        if (s.Top == TopStyle.Battlements)
        {
            double mh = Math.Clamp(s.MerlonHeightMm, 1, H * 0.6), cw = Math.Max(1, s.CrenelWidthMm), mw = Math.Max(1, s.MerlonWidthMm);
            body = H - mh;
            double u = L - 2 * ez;
            int gaps = (int)Math.Floor((u - mw * 0.5) / (cw + mw));
            if (gaps < 1) findings.Add(new(Severity.Warn, "The wall is too short for battlements: it has a straight top."));
            else
            {
                double pitch = u / gaps;
                var crenels = Enumerable.Range(0, gaps).Select(i => ez + pitch * (i + 0.5)).Select(cx => (cx - cw / 2, cx + cw / 2)).ToList();
                outline = Tops.Battlements(s, L, H, body, crenels);
                if (s.Merlons != MerlonPattern.Even && s.MerlonStepMm > mh - 1)
                    findings.Add(new(Severity.Warn, $"The shorter merlons can only be {Math.Max(0, mh - 1):0.#} mm lower than the tall ones: make the merlons taller."));
            }
        }
        var breaks = s.Breaks;
        if (breaks is not (RuinStyle.None or RuinStyle.Stepped))
        {
            var stones = breaks == RuinStyle.Shattered ? null : Textures.Make(s, new Face(L, H, ez, L - ez, body)).Raised;
            outline = Tops.Break(s, outline, L, ez + 1, H, Math.Max(4, 0.15 * H), 0, 1, stones, out _);
        }
        panel = Geo.Intersect(panel, outline.Under(-1));
        if (breaks == RuinStyle.Stepped) panel = Geo.Subtract(panel, [Ruin(s, L, H, ez)]);

        // Doors and windows: where they are, what's cut, and the shape their frame follows.
        var holes = new PathsD();
        var keepClear = new PathsD();   // where the texture stops (frames, a closed door, mounts)
        var frameRings = new PathsD();
        var frameLines = new List<PathD>();
        var etched = new PathsD();
        var lines = new List<PathD>();
        var taken = new List<(double X0, double X1, double Y0, double Y1)>();

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
            if (cx - w / 2 - frame < ez || cx + w / 2 + frame > L - ez)
                findings.Add(new(Severity.Bad, $"The door doesn't fit between the ends: make it narrower, move it nearer the middle or the wall longer."));
            else if (h + frame > body - 1)
                findings.Add(new(Severity.Bad, $"The door is too tall for the wall: make it lower than {body - frame - 1:0.#} mm."));
            else
            {
                var door = Opening(shape, cx - w / 2, 0, w, h);
                taken.Add((cx - w / 2 - frame, cx + w / 2 + frame, 0, h + frame));
                Frame(door);
                if (s.Door == DoorKind.ClosedDoor)
                {
                    keepClear.AddRange(door);
                    var (de, dl) = DoorFace(door, s.Etch, groove, s.DoorLook);
                    etched.AddRange(de);
                    lines.AddRange(dl);
                }
                else holes.AddRange(door);
                if (s.Door == DoorKind.DoorwayAndDoor)
                {
                    var leaf = Geo.Offset(door, -DoorClearMm);
                    var (le, ll) = DoorFace(leaf, s.Etch, groove, s.DoorLook);
                    // A loose portcullis has its gaps cut right through.
                    var leafCut = s.DoorLook == DoorLook.Bars ? Geo.Subtract(leaf, BarGaps(leaf)) : leaf;
                    extras.Add(new Part(s.DoorLook == DoorLook.Bars ? "Portcullis" : "Door", Geo.Tidy(Geo.Move(leafCut, -(cx - w / 2), 0)), Geo.Move(le, -(cx - w / 2), 0), Geo.Map(ll, p => new PointD(p.x - (cx - w / 2), p.y)), 1)
                    { Poses = [Model.Pose.Front(cx - w / 2, 0, 0)] });
                }
            }
        }

        if (WindowCount(s) > 0)
        {
            double w = Math.Max(1, s.WindowWidthMm), h = s.WindowShape == OpeningShape.Round ? w : Math.Max(1, s.WindowHeightMm), sill = s.SillMm;
            if (sill + h + frame > body - 1 || sill < frame + 1)
                findings.Add(new(Severity.Bad, $"The windows don't fit in the wall's height: lower the sill or make them shorter (the top must be under {body - frame - 1:0.#} mm)."));
            else
            {
                var clear = taken.Where(t => t.Y0 < sill + h + frame && t.Y1 > sill - frame).Select(t => (t.X0, t.X1)).ToList();
                var xs = s.WindowAtMm.Count > 0 ? Placed(s.WindowAtMm, L, w + 2 * frame, ez, L - ez, clear, 2) : Spread(s.Windows, w + 2 * frame, ez, L - ez, clear, 2);
                if (xs.Count < WindowCount(s)) findings.Add(new(Severity.Warn, xs.Count == 0 ? "No window fits beside the door and the ends." : $"Only {xs.Count} of the {WindowCount(s)} windows fit."));
                foreach (var x in xs)
                {
                    var win = Opening(s.WindowShape, x - w / 2, sill, w, h);
                    taken.Add((x - w / 2 - frame, x + w / 2 + frame, sill - frame, sill + h + frame));
                    Frame(win);
                    holes.AddRange(Bars(win, s.Bars, Math.Max(0.6, s.BarMm), x, sill, w, h));
                }
            }
        }

        // Mount slots: through the wall, as wide as the sheet (an accessory's tab) plus the fit.
        double slotW = T + fit, slotH = Math.Clamp(s.MountSlotMm, 2, 30);
        int mounts = 0;
        var mountXs = new List<double>();
        if (s.MountPoints.Count > 0)
        {
            var placed = Mounts.Place(s, ez + 1, L - ez - 1, 2, (_, _, _) => body, taken, findings);
            foreach (var (m, slots) in placed)
            {
                holes.AddRange(slots);
                var b = m.Bounds(s);
                keepClear.AddRange(Geo.Offset(new PathsD(slots), 0.5));
                taken.Add((b.X0 - 1, b.X1 + 1, b.Y0 - 1, b.Y1 + 1));
                used.Add(m);
            }
            extras.AddRange(Mounts.Accessories(s, used, wallT, wallT / 2, 1));
        }
        else if (s.Mounts > 0)
        {
            double y0 = s.MountHeightMm - slotH / 2, y1 = s.MountHeightMm + slotH / 2;
            if (y0 < 2 || y1 > body - 2) findings.Add(new(Severity.Bad, $"The mount points are too high or too low: put them between {2 + slotH / 2:0.#} and {body - 2 - slotH / 2:0.#} mm."));
            else
            {
                var xs = Spread(s.Mounts, slotW + 2, ez + 1, L - ez - 1, taken.Where(t => t.Y0 < y1 + 1 && t.Y1 > y0 - 1).Select(t => (t.X0, t.X1)).ToList(), 3);
                if (xs.Count < s.Mounts) findings.Add(new(Severity.Warn, $"Only {xs.Count} of the {s.Mounts} mount points fit clear of the doors and windows."));
                foreach (var x in xs)
                {
                    holes.Add(Geo.Rect(x - slotW / 2, y0, x + slotW / 2, y1));
                    keepClear.Add(Geo.Rect(x - slotW / 2 - 0.5, y0 - 0.5, x + slotW / 2 + 0.5, y1 + 0.5));
                    taken.Add((x - slotW / 2 - 1, x + slotW / 2 + 1, y0 - 1, y1 + 1));
                }
                mounts = xs.Count;
                mountXs = xs;
                used.AddRange(xs.Select((x, i) => new MountPoint { XMm = Math.Round(x, 2), YMm = s.MountHeightMm, SlotHeightMm = slotH, Item = Mounts.Legacy(s.Accessories, i) }));
            }
        }
        var acc = Accessories(s, wallT, slotH);
        for (int j = 0; j < acc.Count; j++)
        {
            double baseY = acc[j].Name == "Shelf bracket" ? s.MountHeightMm - slotH / 2 : s.MountHeightMm;
            extras.Add(acc[j] with { Copies = acc[j].Copies * mounts, Poses = [.. mountXs.Where((_, i) => i % acc.Count == j).Select(x => Model.Pose.Left(x, baseY, wallT / 2))] });
        }

        // A walkway behind battlements: a strip on brackets whose tabs plug into slots under it.
        if (s.Walkway && s.Top == TopStyle.Battlements)
        {
            double top = body - WalkwayDropMm, depth = Math.Clamp(s.WalkwayDepthMm, 4, 60), y1 = top - T, y0 = y1 - slotH;
            double from = ez + 4, to = L - ez - 4;
            if (y0 < 3 || to - from < 10) findings.Add(new(Severity.Warn, "The wall is too small for a walkway behind the battlements."));
            else
            {
                int n = Math.Max(2, (int)Math.Round((to - from) / 40) + 1);
                var clear = taken.Where(t => t.Y0 < y1 + 1 && t.Y1 > y0 - 1).Select(t => (t.X0, t.X1)).ToList();
                var xs = Spread(n, slotW + 2, from, to, clear, 0);
                foreach (var x in xs)
                {
                    holes.Add(Geo.Rect(x - slotW / 2, y0, x + slotW / 2, y1));
                    keepClear.Add(Geo.Rect(x - slotW / 2 - 0.5, y0 - 0.5, x + slotW / 2 + 0.5, y1 + 0.5));
                }
                if (xs.Count < 2) findings.Add(new(Severity.Warn, "There's no room for the walkway's brackets clear of the windows."));
                else
                {
                    double length = L - 2 * Math.Max(ez, s.SlotDepthMm) - 1;
                    extras.Add(new Part("Walkway", [Geo.Rect(0, 0, length, depth)], [], [], 1) { Poses = [Model.Pose.Up((L - length) / 2, top - T / 2, -wallT / 2)] });
                    extras.Add(new Part("Walkway bracket", Bracket(wallT, slotH, depth - 1, 0), [], [], xs.Count) { Poses = [.. xs.Select(x => Model.Pose.Right(x, y0, -wallT / 2))] });
                    findings.Add(new(Severity.Note, $"The walkway's top is {top:0.#} mm up, {WalkwayDropMm:0} mm under the gaps in the battlements."));
                }
            }
        }

        var cut = Geo.Subtract(panel, Geo.Union(holes));
        if (Geo.Pieces(cut) != 1)
        {
            findings.Add(new(Severity.Bad, "The doors, windows or broken top cut the wall into separate pieces: move or shrink them."));
            return null;
        }

        // The texture, kept off the plain ends, the frames, a closed door and round the slots.
        var region = Geo.Subtract(cut, Geo.Union(keepClear));
        if (ez > 0) region = Geo.Subtract(region, [Geo.Rect(-1, -1, ez, H + 1), Geo.Rect(L - ez, -1, L + 1, H + 1)]);
        (PathsD Etched, List<PathD> Lines) Dress(WallSpec look)
        {
            var e = new PathsD(etched);
            var l = new List<PathD>(lines);
            var tex = Textures.Make(look, new Face(L, H, ez, L - ez, body));
            if (s.Etch == EtchStyle.Grooves)
            {
                var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-1, -1, L + 1, H + 1)], Geo.Union(tex.Raised)) : [];
                e.AddRange(Geo.Intersect(Geo.Union(mortar, tex.Etched), region));
            }
            else
            {
                l.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), region));
                l.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), region));
            }
            l.AddRange(Geo.ClipLines(tex.Lines, region));
            e.AddRange(Geo.Intersect(Geo.Union(frameRings), cut));
            l.AddRange(Geo.ClipLines(frameLines, cut));
            return (Geo.Union(e), l);
        }
        var (frontEtched, frontLines) = Dress(s);
        // The other side, when it has a look of its own or is engraved after turning the panel over: drawn as
        // seen through the panel from the front (the same x as the front), its own seed so it isn't the front's twin.
        (PathsD Etched, List<PathD> Lines)? back = s.Back is not null || s.FlipsForBack ? Dress(s.BackSpec with { Seed = s.Seed + 101 }) : null;
        return new(cut, frontEtched, frontLines, extras) { Back = back };
    }

    /// <summary>The broken-away part of a ruined top: a dip in stone-sized steps, deepest somewhere near the middle, the ends whole.</summary>
    static PathD Ruin(WallSpec s, double L, double H, double ez)
    {
        var rnd = new Random(s.Seed * 7919 + 13);
        double depth = Math.Clamp(s.RuinDepth, 0.05, 0.8) * H, u = L - 2 * ez;
        double centre = ez + u * (0.35 + 0.3 * rnd.NextDouble()), half = u * (0.3 + 0.15 * rnd.NextDouble());
        double stepX = Math.Clamp(s.SizeA / 2, 1.5, 8), stepY = Math.Clamp(s.SizeB, 1, 8);
        var p = new PathD { new(ez + 1, H + 1) };
        for (double x = ez + 1; x < L - ez - 1; x += stepX)
        {
            double t = Math.Clamp(Math.Abs(x + stepX / 2 - centre) / half, 0, 1);
            double dip = depth * Math.Pow(Math.Cos(t * Math.PI / 2), 2) + (rnd.NextDouble() - 0.3) * stepY;
            double y = H - Math.Max(0, Math.Round(dip / stepY) * stepY);
            double x1 = Math.Min(L - ez - 1, x + stepX);
            p.Add(new(x, y));
            p.Add(new(x1, y));
        }
        p.Add(new(L - ez - 1, H + 1));
        return p;
    }

    /// <summary>A door or window outline: its bottom-left corner at x, y.</summary>
    public static PathsD Opening(OpeningShape shape, double x, double y, double w, double h)
    {
        switch (shape)
        {
            case OpeningShape.Round:
                return [Geo.Circle(x + w / 2, y + w / 2, w / 2)];
            case OpeningShape.Arched:
            {
                double spring = Math.Max(y, y + h - w / 2);
                var p = new PathD { new(x, y), new(x + w, y), new(x + w, spring) };
                p.AddRange(Geo.Arc(x + w / 2, spring, w / 2, 0, Math.PI).Skip(1));
                return [p];
            }
            case OpeningShape.Pointed:
            {
                // Two arcs as wide as the opening, meeting at a point (an equilateral Gothic arch).
                double rise = w * Math.Sqrt(3) / 2, spring = Math.Max(y, y + h - rise);
                var p = new PathD { new(x, y), new(x + w, y), new(x + w, spring) };
                p.AddRange(Geo.Arc(x, spring, w, 0, Math.PI / 3).Skip(1));
                p.AddRange(Geo.Arc(x + w, spring, w, Math.PI * 2 / 3, Math.PI).Skip(1));
                return [Geo.Tidy([p]).FirstOrDefault() ?? p];
            }
            case OpeningShape.ArrowSlit:
            {
                // A narrow slit with a short cross slit a little above the middle, round holes at the ends.
                double cx = x + w / 2, cy = y + h * 0.55, arm = Math.Min(h * 0.3, w * 4);
                return Geo.Union(new PathsD
                {
                    Geo.Rect(cx - w / 2, y, cx + w / 2, y + h),
                    Geo.Rect(cx - arm, cy - w / 2, cx + arm, cy + w / 2),
                    Geo.Circle(cx, y + w * 0.6, w * 0.6), Geo.Circle(cx, y + h - w * 0.6, w * 0.6),
                });
            }
            case OpeningShape.Chamfered:
            {
                // Square below, the top corners cut off at 45°: sci-fi doors and hatches.
                double c = Math.Min(w * 0.3, h * 0.3);
                return [[new(x, y), new(x + w, y), new(x + w, y + h - c), new(x + w - c, y + h), new(x + c, y + h), new(x, y + h - c)]];
            }
            case OpeningShape.Rough:
                return [RoughArch(x, y, w, h)];
            default:
                return [Geo.Rect(x, y, x + w, y + h)];
        }
    }

    /// <summary>
    /// A ragged cave mouth: a round arch whose sides and top wander in and out by up to a tenth of its
    /// width, the bottom straight. The same size and place always give the same outline.
    /// </summary>
    static PathD RoughArch(double x, double y, double w, double h)
    {
        var rnd = new Random((int)(Math.Round(x * 10) * 7919 + Math.Round(y * 10) * 104729 + Math.Round(w * 10) * 31 + Math.Round(h * 10)) & 0x7fffffff);
        double spring = Math.Max(y, y + h - w / 2);
        // The arch's outline from the bottom right, up and over to the bottom left, a point every 1 mm or so.
        var smooth = new List<PointD>();
        for (double t = y; t < spring; t += 1) smooth.Add(new(x + w, t));
        smooth.AddRange(Geo.Arc(x + w / 2, spring, w / 2, 0, Math.PI, 0.05));
        for (double t = spring - 1; t > y; t -= 1) smooth.Add(new(x, t));
        // Each point pushed in or out along the outline's normal, the push smoothed along it.
        int n = smooth.Count;
        var push = new double[n];
        double amp = w * 0.1;
        for (int i = 0; i < n; i++) push[i] = (rnd.NextDouble() - 0.5) * 2 * amp;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < n - 1; i++) push[i] = (push[i - 1] + push[i] * 2 + push[i + 1]) / 4;
        // Anticlockwise: from the bottom right, round the top, to the bottom left.
        var p = new PathD { new(x + w, y) };
        double cx = x + w / 2;
        for (int i = 0; i < n; i++)
        {
            var q = smooth[i];
            // Out from the arch's middle (the sides straight out, the top round from the springing).
            double dx = q.x - cx, dy = q.y > spring ? q.y - spring : 0, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
            double k = i == 0 || i == n - 1 ? 0.5 : 1;
            p.Add(new(q.x + dx / len * push[i] * k, q.y + dy / len * push[i] * k));
        }
        p.Add(new(x, y));
        return Geo.Tidy([p]).OrderByDescending(q => Math.Abs(Clipper.Area(q))).FirstOrDefault() ?? Geo.Rect(x, y, x + w, y + h);
    }

    /// <summary>A window with bars left across it: the panes that are cut out.</summary>
    internal static PathsD Bars(PathsD window, WindowBars bars, double bar, double cx, double y, double w, double h)
    {
        if (bars == WindowBars.None || w < bar * 3) return window;
        var strips = new PathsD();
        if (bars == WindowBars.Cross)
        {
            strips.Add(Geo.Rect(cx - bar / 2, y - 1, cx + bar / 2, y + h + 1));
            strips.Add(Geo.Rect(cx - w, y + h * 0.55 - bar / 2, cx + w, y + h * 0.55 + bar / 2));
        }
        else
        {
            int n = Math.Max(1, (int)Math.Round(w / (bar * 3.5)) - 1);
            for (int i = 1; i <= n; i++)
            {
                double bx = cx - w / 2 + w * i / (n + 1);
                strips.Add(Geo.Rect(bx - bar / 2, y - 1, bx + bar / 2, y + h + 1));
            }
        }
        return Geo.Subtract(window, Geo.Union(strips));
    }

    /// <summary>What's etched and scored on a door of the given look, inside its outline.</summary>
    public static (PathsD Etched, List<PathD> Lines) DoorFace(PathsD door, EtchStyle style, double groove, DoorLook look) => look switch
    {
        DoorLook.Studded => StuddedDoor(door, style, groove),
        DoorLook.Metal => MetalDoor(door, style, groove),
        DoorLook.Bars => BarsDoor(door, style, groove),
        DoorLook.Stone => StoneDoor(door, style, groove),
        _ => PlankDoor(door, style, groove),
    };

    /// <summary>The edge of a door: a groove just inside it, or a line round it.</summary>
    static void Edge(PathsD door, EtchStyle style, double g, PathsD etched, List<PathD> lines)
    {
        if (style == EtchStyle.Grooves) etched.AddRange(Geo.Subtract(door, Geo.Offset(door, -g)));
        else lines.AddRange(Geo.Offset(door, -g / 2).Select(Geo.Loop));
    }

    /// <summary>A ring groove (or line) round a shape.</summary>
    static void Ring(PathsD shape, EtchStyle style, double g, PathsD etched, List<PathD> lines)
    {
        if (style == EtchStyle.Grooves) etched.AddRange(Geo.Subtract(Geo.Offset(shape, g / 2), Geo.Offset(shape, -g / 2)));
        else lines.AddRange(shape.Select(Geo.Loop));
    }

    /// <summary>Planks with iron straps across them, studded with nail heads, and a ring handle.</summary>
    static (PathsD Etched, List<PathD> Lines) StuddedDoor(PathsD door, EtchStyle style, double groove)
    {
        var (etched, lines) = PlankDoor(door, style, groove);
        var b = Geo.Bounds(door);
        double w = b.right - b.left, h = b.bottom - b.top;
        if (w < 4 || h < 4) return (etched, lines);
        double strap = Math.Max(1.2, h * 0.06), r = Math.Clamp(w * 0.03, 0.25, 0.45), inside = Math.Min(1, w / 8) + r * 2;
        var within = Geo.Offset(door, -inside);
        foreach (double at in new[] { 0.2, 0.62 })
        {
            double y = b.top + h * at + strap / 2;
            int n = Math.Max(2, (int)Math.Round(w / 2.5));
            for (int i = 0; i <= n; i++)
            {
                double x = b.left + inside + (w - 2 * inside) * i / n;
                if (Geo.Intersect([Geo.Circle(x, y, r)], within).Count > 0) lines.Add(Geo.Loop(Geo.Circle(x, y, r)));
            }
        }
        return (etched, lines);
    }

    /// <summary>
    /// A sci-fi blast door: two leaves meeting up the middle, a band of hazard stripes across the foot
    /// (making chevrons where the leaves meet) and a panel scored on each leaf above.
    /// </summary>
    static (PathsD Etched, List<PathD> Lines) MetalDoor(PathsD door, EtchStyle style, double groove)
    {
        var b = Geo.Bounds(door);
        double w = b.right - b.left, h = b.bottom - b.top;
        if (w < 3 || h < 3) return ([], []);
        double g = Math.Min(groove, w / 12), cx = (b.left + b.right) / 2;
        var etched = new PathsD();
        var lines = new List<PathD>();
        Edge(door, style, g, etched, lines);
        var inside = Geo.Offset(door, -g);
        // The seam between the leaves.
        if (style == EtchStyle.Grooves) etched.AddRange(Geo.Intersect([Geo.Rect(cx - g / 2, b.top - 1, cx + g / 2, b.bottom + 1)], inside));
        else lines.AddRange(Geo.ClipLines([[new(cx, b.top - 1), new(cx, b.bottom + 1)]], inside));
        // Hazard chevrons along the foot.
        double band = Math.Min(h * 0.22, w * 0.35), sw = Math.Max(0.7, band * 0.35), y0 = b.top + g, y1 = y0 + band;
        var stripes = new PathsD();
        for (double sx = 0; sx < w / 2 + band; sx += sw * 2)
        {
            stripes.Add([new(cx + sx, y0), new(cx + sx + sw, y0), new(cx + sx + sw + band, y1), new(cx + sx + band, y1)]);
            stripes.Add([new(cx - sx, y0), new(cx - sx - band, y1), new(cx - sx - sw - band, y1), new(cx - sx - sw, y0)]);
        }
        etched.AddRange(Geo.Intersect(Geo.Union(stripes), Geo.Intersect([Geo.Rect(b.left, y0, b.right, y1)], inside)));
        lines.AddRange(Geo.ClipLines([[new(b.left - 1, y1 + 0.3), new(b.right + 1, y1 + 0.3)]], inside));
        // A panel on each leaf, its corners cut.
        double m = Math.Max(1, w * 0.1), py0 = y1 + m, py1 = b.top + h * 0.8;
        foreach (var (x0, x1) in new[] { (b.left + m, cx - m * 0.7), (cx + m * 0.7, b.right - m) })
        {
            if (x1 - x0 < 1.5 || py1 - py0 < 2) continue;
            double c = Math.Min(x1 - x0, py1 - py0) * 0.2;
            PathD panel = [new(x0 + c, py0), new(x1 - c, py0), new(x1, py0 + c), new(x1, py1 - c), new(x1 - c, py1), new(x0 + c, py1), new(x0, py1 - c), new(x0, py0 + c)];
            lines.AddRange(Geo.ClipLines([Geo.Loop(panel)], inside));
        }
        return (Geo.Intersect(Geo.Union(etched), door), lines);
    }

    /// <summary>The gaps of a portcullis's grid of bars over the door (the bars are what's left).</summary>
    public static PathsD BarGaps(PathsD door)
    {
        var b = Geo.Bounds(door);
        double w = b.right - b.left, h = b.bottom - b.top;
        double bar = Math.Clamp(w * 0.1, 1.2, 2.5);
        var inner = Geo.Offset(door, -bar);
        if (inner.Count == 0 || w < bar * 4 || h < bar * 4) return [];
        var ib = Geo.Bounds(inner);
        double iw = ib.right - ib.left, ih = ib.bottom - ib.top;
        int cols = Math.Max(2, (int)Math.Round((iw + bar) / (bar * 3.2)));
        double gw = (iw - (cols - 1) * bar) / cols;
        int rows = Math.Max(2, (int)Math.Round((ih + bar) / (gw * 1.6 + bar)));
        double gh = (ih - (rows - 1) * bar) / rows;
        if (gw < 0.8 || gh < 0.8) return [];
        var gaps = new PathsD();
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                double x = ib.left + i * (gw + bar), y = ib.top + j * (gh + bar);
                gaps.Add(Geo.Rect(x, y, x + gw, y + gh));
            }
        // Clipped to the door's shape; slivers too thin to cut are left as bar.
        var clipped = Geo.Intersect(gaps, inner);
        return Geo.Offset(Geo.Offset(clipped, -0.35), 0.35);
    }

    /// <summary>A portcullis etched on: its gaps etched (or scored round), the bars standing.</summary>
    static (PathsD Etched, List<PathD> Lines) BarsDoor(PathsD door, EtchStyle style, double groove)
    {
        var gaps = BarGaps(door);
        return style == EtchStyle.Grooves ? (gaps, []) : ([], [.. gaps.Select(Geo.Loop)]);
    }

    /// <summary>A stone slab: a border groove inside the edge and a round seal in the middle.</summary>
    static (PathsD Etched, List<PathD> Lines) StoneDoor(PathsD door, EtchStyle style, double groove)
    {
        var b = Geo.Bounds(door);
        double w = b.right - b.left, h = b.bottom - b.top;
        if (w < 3 || h < 3) return ([], []);
        double g = Math.Min(groove, w / 10);
        var etched = new PathsD();
        var lines = new List<PathD>();
        Edge(door, style, g, etched, lines);
        var border = Geo.Offset(door, -Math.Max(1.2, w * 0.14));
        if (border.Count > 0) Ring(border, style, g, etched, lines);
        double r = Math.Min(w * 0.2, h * 0.15), cx = (b.left + b.right) / 2, cy = b.top + h * 0.5;
        if (r > 1.2)
        {
            Ring([Geo.Circle(cx, cy, r)], style, g, etched, lines);
            Ring([Geo.Circle(cx, cy, r * 0.45)], style, g, etched, lines);
        }
        return (Geo.Intersect(Geo.Union(etched), door), lines);
    }

    /// <summary>Planks, two braces and a ring handle, inside a door's outline.</summary>
    static (PathsD Etched, List<PathD> Lines) PlankDoor(PathsD door, EtchStyle style, double groove)
    {
        var b = Geo.Bounds(door);
        double w = b.right - b.left, h = b.bottom - b.top;
        if (w < 2 || h < 2) return ([], []);
        int planks = Math.Max(2, (int)Math.Round(w / 3.5));
        double pw = w / planks, g = Math.Min(groove, pw / 3);
        var inside = Geo.Offset(door, -Math.Min(1, w / 8));
        var braces = new PathsD { Geo.Rect(b.left - 1, b.top + h * 0.2, b.right + 1, b.top + h * 0.2 + Math.Max(1.2, h * 0.06)), Geo.Rect(b.left - 1, b.top + h * 0.62, b.right + 1, b.top + h * 0.62 + Math.Max(1.2, h * 0.06)) };
        var seams = new PathsD();
        for (int i = 1; i < planks; i++) seams.Add(Geo.Rect(b.left + pw * i - g / 2, b.top - 1, b.left + pw * i + g / 2, b.bottom + 1));
        var lines = new List<PathD>();
        var etched = new PathsD();
        // The edge, the seams between the boards (not across the braces) and round the braces.
        var edge = Geo.Subtract(door, Geo.Offset(door, -g));
        var braceIn = Geo.Intersect(braces, inside);
        if (style == EtchStyle.Grooves)
        {
            etched.AddRange(edge);
            etched.AddRange(Geo.Subtract(Geo.Intersect(seams, inside), Geo.Union(braceIn)));
            etched.AddRange(Geo.Subtract(Geo.Offset(braceIn, g), braceIn));
        }
        else
        {
            lines.AddRange(Geo.Offset(door, -g / 2).Select(Geo.Loop));
            for (int i = 1; i < planks; i++) lines.AddRange(Geo.ClipLines([[new(b.left + pw * i, b.top - 1), new(b.left + pw * i, b.bottom + 1)]], Geo.Subtract(inside, braceIn)));
            lines.AddRange(braceIn.Select(Geo.Loop));
        }
        double r = Math.Clamp(w * 0.07, 0.6, 2);
        lines.Add(Geo.Loop(Geo.Circle(b.left + w * 0.78, b.top + h * 0.45, r)));
        return (Geo.Intersect(Geo.Union(etched), door), lines);
    }

    /// <summary>How many windows the wall asks for: one at each of <see cref="WallSpec.WindowAtMm"/>, or <see cref="WallSpec.Windows"/> spread out.</summary>
    public static int WindowCount(WallSpec s) => s.WindowAtMm.Count > 0 ? s.WindowAtMm.Count : Math.Max(0, s.Windows);

    /// <summary>
    /// Centres for things <paramref name="width"/> wide at the places asked for (mm from the middle of a
    /// panel <paramref name="length"/> long), keeping those that fit between from and to, clear of the
    /// taken stretches and of each other.
    /// </summary>
    public static List<double> Placed(IEnumerable<double> fromMiddle, double length, double width, double from, double to, List<(double X0, double X1)> taken, double gap)
    {
        var xs = new List<double>();
        foreach (var x in fromMiddle.Select(m => length / 2 + m).Order())
        {
            double a = x - width / 2, b = x + width / 2;
            if (a < from - 1e-6 || b > to + 1e-6) continue;
            if (taken.Any(t => a < t.X1 + gap && b > t.X0 - gap)) continue;
            if (xs.Count > 0 && a < xs[^1] + width / 2 + gap) continue;
            xs.Add(x);
        }
        return xs;
    }

    /// <summary>
    /// Centres for up to <paramref name="count"/> things <paramref name="width"/> wide between from and to,
    /// clear of the taken stretches, spread evenly over the free stretches by their room.
    /// </summary>
    public static List<double> Spread(int count, double width, double from, double to, List<(double X0, double X1)> taken, double gap)
    {
        var free = new List<(double A, double B)> { (from, to) };
        static (double A, double B) F(double a, double b) => (a, b);
        foreach (var (t0, t1) in taken.OrderBy(t => t.X0))
            free = [.. free.SelectMany(f => t1 <= f.A || t0 >= f.B ? new[] { f } : new[] { F(f.A, t0 - gap), F(t1 + gap, f.B) }).Where(f => f.B - f.A >= width)];
        int Room((double A, double B) f) => (int)Math.Floor((f.B - f.A + gap) / (width + gap));
        int total = free.Sum(Room);
        if (total == 0 || count <= 0) return [];
        count = Math.Min(count, total);
        // Hand them out one at a time to the stretch with the most room per thing.
        var given = new int[free.Count];
        for (int k = 0; k < count; k++)
        {
            int best = -1;
            double bestRoom = -1;
            for (int i = 0; i < free.Count; i++)
            {
                if (given[i] >= Room(free[i])) continue;
                double room = (free[i].B - free[i].A) / (given[i] + 1);
                if (room > bestRoom) { bestRoom = room; best = i; }
            }
            if (best < 0) break;
            given[best]++;
        }
        var xs = new List<double>();
        for (int i = 0; i < free.Count; i++)
            for (int j = 0; j < given[i]; j++)
                xs.Add(free[i].A + (free[i].B - free[i].A) * (j + 0.5) / given[i]);
        return [.. xs.Order()];
    }

    /// <summary>
    /// The accessories for each mount point, drawn side-on (they stand out from the wall): a tab that
    /// plugs through the slot (x less than 0, as deep as the wall) and the part that shows.
    /// </summary>
    public static List<Part> Accessories(WallSpec s, double wallT, double slotH)
    {
        var list = new List<Part>();
        var a = s.Accessories;
        if (a is Accessory.Pegs or Accessory.OneOfEach) list.Add(new Part("Wall peg", Peg(wallT, slotH), [], [], 1));
        if (a is Accessory.Sconces or Accessory.OneOfEach) list.Add(new Part("Torch sconce", Sconce(wallT, slotH), [], [], 1));
        if (a is Accessory.Brackets or Accessory.OneOfEach) list.Add(new Part("Shelf bracket", Bracket(wallT, slotH, 10, 1), [], [], 1));
        if (a is Accessory.Lamps) list.Add(new Part("Wall lamp", Lamp(wallT, slotH), [], [], 1));
        return list;
    }

    /// <summary>The piece for an accessory, side-on, its tab into the wall (u &lt; 0) round v = 0.</summary>
    internal static PathsD AccessoryPiece(MountItem item, double wallT, double slotH) => item switch
    {
        MountItem.Peg => Peg(wallT, slotH),
        MountItem.Sconce => Sconce(wallT, slotH),
        MountItem.Lamp => Lamp(wallT, slotH),
        _ => [],
    };

    /// <summary>A tab into a mount slot whose top is at <paramref name="top"/>.</summary>
    internal static PathD MountTab(double wallT, double slotH, double top) => Tab(wallT, slotH, top);

    /// <summary>The tab that goes through a mount slot: a touch shorter than the wall is thick and than the slot is tall.</summary>
    static PathD Tab(double wallT, double slotH, double top) => Geo.Rect(-(wallT - 0.2), top - slotH + 0.1, 0.01, top - 0.1);

    /// <summary>A peg for pictures, shields and banners: a tab with a shoulder that stops it going through; glue the picture to its end.</summary>
    static PathsD Peg(double wallT, double slotH) => Geo.Union([Tab(wallT, slotH, slotH / 2), Geo.Rect(0, -slotH / 2 - 1, 3, slotH / 2 + 1)]);

    /// <summary>A shelf bracket (or a walkway's): a triangle whose top is <paramref name="above"/> mm over the slot.</summary>
    internal static PathsD Bracket(double wallT, double slotH, double size, double above)
    {
        double top = slotH + above;
        return Geo.Union([Tab(wallT, slotH, slotH), [new(0, top), new(0, top - size), new(size, top)]]);
    }

    /// <summary>A sci-fi wall lamp, side-on: a back plate, an arm out and up, and a lamp housing angled down at the floor.</summary>
    static PathsD Lamp(double wallT, double slotH)
    {
        double half = slotH / 2;
        return Geo.Union(new PathsD
        {
            Tab(wallT, slotH, half),
            Geo.Rect(0, -half - 1, 1.2, half + 1),                                      // back plate
            Textures.Strip(new(0.6, 0), new(3.5, 2.5), 1.4),                         // arm
            new PathD { new(2.6, 1.2), new(7.5, 2.6), new(7.0, 4.6), new(2.4, 4.0) }, // housing, angled down
        });
    }

    /// <summary>A torch in a bracket, side-on: an arm out from the wall, a cup, the torch leaning out and its flame.</summary>
    static PathsD Sconce(double wallT, double slotH)
    {
        double half = slotH / 2;
        var parts = new PathsD
        {
            Tab(wallT, slotH, half),
            Geo.Rect(0, -half, 1.5, half),                              // plate on the wall
            Textures.Strip(new(0.5, -half * 0.4), new(6, -half * 0.4 + 1.5), 1.4), // arm
            new PathD { new(4.6, 0.4), new(8.2, 1.4), new(7.4, 3.6), new(5.0, 2.8) }, // cup
            Textures.Strip(new(5.6, -1.5), new(8.6, 10), 1.4),         // torch
        };
        // The flame: a teardrop above the torch, leaning with it.
        var flame = new PathD();
        for (int i = 0; i < 24; i++)
        {
            double t = i / 24.0 * Math.PI * 2, r = 1.6 * (1 - 0.5 * Math.Sin(t));
            flame.Add(new(8.9 + r * Math.Cos(t) * 0.8, 12 + r * Math.Sin(t) * 1.4));
        }
        parts.Add(flame);
        return Geo.Union(parts);
    }
}
