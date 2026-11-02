using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>One piece to cut (y up, mm): its outline and holes, what's etched on it as areas, and lines scored on it.</summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies);

/// <summary>Everything a set of walls is cut from, with what was found while making it.</summary>
public sealed record WallKit(List<Part> Parts, List<Finding> Findings, double PanelLengthMm, double WallThicknessMm, int ColumnLayers)
{
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

    public static double WallThickness(WallSpec s) => s.ThicknessMm * (s.TwoSided ? 2 : 1);

    /// <summary>Distance from a column's centre to the end of a wall in its slot (mm).</summary>
    public static double CentreToEnd(WallSpec s) => s.ColumnMm / 2 - s.SlotDepthMm + EndClearMm;

    public static double PanelLength(WallSpec s) => s.Measure == LengthMeasure.Centres ? s.LengthMm - 2 * CentreToEnd(s) : s.LengthMm;

    /// <summary>Layers in each column (the foot included, the cap not): enough to reach the top of the wall.</summary>
    public static int ColumnLayers(WallSpec s) => Math.Max(1, (int)Math.Ceiling(s.HeightMm / Math.Max(0.1, s.ThicknessMm) - 0.02));

    public static WallKit Make(WallSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        double L = PanelLength(s), H = s.HeightMm, T = s.ThicknessMm, wallT = WallThickness(s);
        double ez = s.PlainEnds ? s.SlotDepthMm + EndClearMm : 0;
        int layers = ColumnLayers(s);
        if (L < 2 * ez + 5 || L < 10) { findings.Add(new(Severity.Bad, $"The wall is too short ({L:0.#} mm): make it longer.")); return new(parts, findings, L, wallT, layers); }
        if (H < 5) { findings.Add(new(Severity.Bad, "The wall is too low: make it at least 5 mm high.")); return new(parts, findings, L, wallT, layers); }

        var wall = Panel(s, L, H, ez, findings);
        if (wall is null) return new(parts, findings, L, wallT, layers);
        parts.Add(new Part("Wall", wall.Cut, wall.Etched, wall.Lines, Math.Max(1, s.Walls)));
        if (s.TwoSided)
        {
            // Cut as a mirror image: turned over, it lies exactly on the back of the front panel.
            PointD Mirror(PointD p) => new(L - p.x, p.y);
            parts.Add(new Part("Wall back", Geo.Tidy(Geo.Map(wall.Cut, Mirror)), Geo.Tidy(Geo.Map(wall.Etched, Mirror)), Geo.Map(wall.Lines, Mirror), Math.Max(1, s.Walls)));
        }
        foreach (var extra in wall.Extras) parts.Add(extra with { Copies = extra.Copies * Math.Max(1, s.Walls) });
        parts.AddRange(Connectors.Make(s, findings));

        if (s.Measure == LengthMeasure.Centres) findings.Add(new(Severity.Note, $"Each panel is {L:0.#} mm long, so the columns' centres are {s.LengthMm:0.#} mm apart."));
        else if (parts.Any(p => p.Name.Contains("column"))) findings.Add(new(Severity.Note, $"Columns' centres end up {L + 2 * CentreToEnd(s):0.#} mm apart."));
        if (s.TwoSided) findings.Add(new(Severity.Note, $"Two panels glued back to back make a {wallT:0.#} mm wall; the columns' slots fit that."));
        return new(parts, findings, L, wallT, layers);
    }

    sealed record PanelResult(PathsD Cut, PathsD Etched, List<PathD> Lines, List<Part> Extras);

    static PanelResult? Panel(WallSpec s, double L, double H, double ez, List<Finding> findings)
    {
        double T = s.ThicknessMm, wallT = WallThickness(s), frame = Math.Max(0, s.FrameMm), fit = Math.Max(0, s.FitMm);
        // Frames and doors use the texture's groove, or 0.6 mm when the texture has none of its own.
        double groove = Textures.Preset(s.Texture) is { UsesGroove: false } || s.GrooveMm < 0.2 ? 0.6 : Math.Min(s.GrooveMm, 3);
        var extras = new List<Part>();

        // The outline, with its top.
        var panel = new PathsD { Geo.Rect(0, 0, L, H) };
        double body = H;
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
                var crenels = new PathsD();
                for (int i = 0; i < gaps; i++)
                {
                    double cx = ez + pitch * (i + 0.5);
                    crenels.Add(Geo.Rect(cx - cw / 2, body, cx + cw / 2, H + 1));
                }
                panel = Geo.Subtract(panel, crenels);
            }
        }
        else if (s.Top == TopStyle.Ruined) panel = Geo.Subtract(panel, [Ruin(s, L, H, ez)]);

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
                    var (de, dl) = DoorFace(door, s.Etch, groove);
                    etched.AddRange(de);
                    lines.AddRange(dl);
                }
                else holes.AddRange(door);
                if (s.Door == DoorKind.DoorwayAndDoor)
                {
                    var leaf = Geo.Offset(door, -DoorClearMm);
                    var (le, ll) = DoorFace(leaf, s.Etch, groove);
                    extras.Add(new Part("Door", Geo.Tidy(Geo.Move(leaf, -(cx - w / 2), 0)), Geo.Move(le, -(cx - w / 2), 0), Geo.Map(ll, p => new PointD(p.x - (cx - w / 2), p.y)), 1));
                }
            }
        }

        if (s.Windows > 0)
        {
            double w = Math.Max(1, s.WindowWidthMm), h = s.WindowShape == OpeningShape.Round ? w : Math.Max(1, s.WindowHeightMm), sill = s.SillMm;
            if (sill + h + frame > body - 1 || sill < frame + 1)
                findings.Add(new(Severity.Bad, $"The windows don't fit in the wall's height: lower the sill or make them shorter (the top must be under {body - frame - 1:0.#} mm)."));
            else
            {
                var xs = Spread(s.Windows, w + 2 * frame, ez, L - ez, taken.Where(t => t.Y0 < sill + h + frame && t.Y1 > sill - frame).Select(t => (t.X0, t.X1)).ToList(), 2);
                if (xs.Count < s.Windows) findings.Add(new(Severity.Warn, xs.Count == 0 ? "No window fits beside the door and the ends." : $"Only {xs.Count} of the {s.Windows} windows fit."));
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
        if (s.Mounts > 0)
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
            }
        }
        foreach (var a in Accessories(s, wallT, slotH)) extras.Add(a with { Copies = a.Copies * mounts });

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
                    extras.Add(new Part("Walkway", [Geo.Rect(0, 0, length, depth)], [], [], 1));
                    extras.Add(new Part("Walkway bracket", Bracket(wallT, slotH, depth - 1, 0), [], [], xs.Count));
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
        var tex = Textures.Make(s, new Face(L, H, ez, L - ez, body));
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-1, -1, L + 1, H + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(mortar, tex.Etched), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), region));
            lines.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), region));
        }
        lines.AddRange(Geo.ClipLines(tex.Lines, region));
        etched.AddRange(Geo.Intersect(Geo.Union(frameRings), cut));
        lines.AddRange(Geo.ClipLines(frameLines, cut));
        return new(cut, Geo.Union(etched), lines, extras);
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
            default:
                return [Geo.Rect(x, y, x + w, y + h)];
        }
    }

    /// <summary>A window with bars left across it: the panes that are cut out.</summary>
    static PathsD Bars(PathsD window, WindowBars bars, double bar, double cx, double y, double w, double h)
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

    /// <summary>Planks, two braces and a ring handle, inside a door's outline.</summary>
    static (PathsD Etched, List<PathD> Lines) DoorFace(PathsD door, EtchStyle style, double groove)
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
        return list;
    }

    /// <summary>The tab that goes through a mount slot: a touch shorter than the wall is thick and than the slot is tall.</summary>
    static PathD Tab(double wallT, double slotH, double top) => Geo.Rect(-(wallT - 0.2), top - slotH + 0.1, 0.01, top - 0.1);

    /// <summary>A peg for pictures, shields and banners: a tab with a shoulder that stops it going through; glue the picture to its end.</summary>
    static PathsD Peg(double wallT, double slotH) => Geo.Union([Tab(wallT, slotH, slotH / 2), Geo.Rect(0, -slotH / 2 - 1, 3, slotH / 2 + 1)]);

    /// <summary>A shelf bracket (or a walkway's): a triangle whose top is <paramref name="above"/> mm over the slot.</summary>
    static PathsD Bracket(double wallT, double slotH, double size, double above)
    {
        double top = slotH + above;
        return Geo.Union([Tab(wallT, slotH, slotH), [new(0, top), new(0, top - size), new(size, top)]]);
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
