using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// The columns box walls go into. A boxed column is a square box: four sides with slots for the faces' tabs,
/// standing on one or two base plates (a wider foot or a stepped plinth), with a plug inside at the bottom
/// and the top that lines the sides up, and a top: flush, a wider cap, a stepped cap, a stepped point or
/// battlements. Columns at angles other than right angles are stacks of layers, each with the slots.
/// Wherever a wall comes in, the plates round the foot are notched so the wall reaches the column's side.
/// </summary>
public static class BoxColumns
{
    static V3 Dir(double deg)
    {
        double a = deg * Math.PI / 180;
        return new V3(Math.Cos(a), 0, -Math.Sin(a));
    }

    /// <summary>A plate lying flat, its (u, v) the column's own plan (u along its first slot), its middle at <paramref name="c"/>.</summary>
    static Pose Plate(V3 c, double y, double yaw) => new(new V3(c.X, c.Y + y, c.Z), Dir(yaw), Dir(yaw + 90));

    /// <summary>A side standing <paramref name="dist"/> out from the middle, facing <paramref name="facing"/>, <paramref name="width"/> wide (u across it, v up).</summary>
    static Pose Side(V3 c, double facing, double dist, double width)
    {
        var n = Dir(facing);
        var u = Dir(facing + 90);
        return new Pose(c + n * dist - u * (width / 2), u, V3.UnitY);
    }

    /// <summary>One piece of a column, and where it sits on a column whose middle is at c, turned by yaw.</summary>
    sealed record Piece(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, Func<V3, double, Pose> At);

    /// <summary>
    /// Every column to cut for a box wall. The 3D view shows one wall between two of them: the first kind
    /// asked for at its left end and the second (or the first again) at its right.
    /// </summary>
    public static List<Part> Make(WallSpec s, List<Finding> findings)
    {
        var parts = new List<Part>();
        var kinds = Connectors.Kinds.Where(k => s.ConnectorCount(k.Id) > 0).ToList();
        if (kinds.Count == 0) return parts;
        if (!s.EndsIntoColumns)
        {
            findings.Add(new(Severity.Warn, "The walls' ends are closed, so they don't fit the columns: tick \"Ends go into columns\" to join them."));
        }
        double C = s.ColumnMm, L = WallKitMaker.PanelLength(s);
        var shown = new Dictionary<ConnectorKind, List<(V3 Centre, double Yaw)>>();
        if (s.EndsIntoColumns)
        {
            var left = kinds[0].Id;
            shown.TryAdd(left, []);
            shown[left].Add((new V3(-C / 2, 0, 0), Connectors.ShowYaw(Connectors.Directions(left, s.CustomAngleDeg), 0)));
            var right = kinds[kinds.Count > 1 ? 1 : 0].Id;
            shown.TryAdd(right, []);
            shown[right].Add((new V3(L + C / 2, 0, 0), Connectors.ShowYaw(Connectors.Directions(right, s.CustomAngleDeg), 180)));
        }
        bool anyStacked = false;
        foreach (var kind in kinds)
        {
            var dirs = Connectors.Directions(kind.Id, s.CustomAngleDeg);
            if (s.ColumnBuild == ColumnBuild.Boxed && !dirs.All(d => Math.Abs(Math.IEEERemainder(d, 90)) < 1e-6)) anyStacked = true;
            var these = Columns(s, kind.Id, s.ConnectorCount(kind.Id), shown.TryGetValue(kind.Id, out var places) ? places : [], out var problem);
            if (problem is not null) { findings.Add(new(Severity.Bad, $"{kind.Name}: {problem}")); continue; }
            parts.AddRange(these);
        }
        if (anyStacked) findings.Add(new(Severity.Note, "Columns at angles other than a right angle are stacks of layers: line them up on a pin through the middle and glue them."));
        if (kinds.Any(k => s.ColumnBuild == ColumnBuild.Boxed && Connectors.Directions(k.Id, s.CustomAngleDeg).All(d => Math.Abs(Math.IEEERemainder(d, 90)) < 1e-6)))
            findings.Add(new(Severity.Note, $"Each boxed column is {C:0.#} mm square, {TopMm(s):0.#} mm tall: its sides go round the plug on its base, the walls' tabs into their slots, then the top."));
        return parts;
    }

    /// <summary>
    /// The pieces of <paramref name="count"/> columns of one kind, shown standing at each of <paramref name="at"/>
    /// (its middle on the table, its first slot turned <paramref name="yaw"/> degrees anticlockwise seen from above).
    /// </summary>
    public static List<Part> Columns(WallSpec s, ConnectorKind kind, int count, IReadOnlyList<(V3 Centre, double Yaw)> at, out string? problem)
    {
        var parts = new List<Part>();
        var dirs = Connectors.Directions(kind, s.CustomAngleDeg);
        bool square = dirs.All(d => Math.Abs(Math.IEEERemainder(d, 90)) < 1e-6);
        var shape = Connectors.ShapeFor(s, dirs);
        var pieces = s.ColumnBuild != ColumnBuild.Boxed ? Stacked(s, dirs, out problem)
            : s.BoxAnyShape && !(s.Boxed && square && shape == ColumnShape.Square) && BoxOutline(shape, s.ColumnMm, dirs) is { } poly ? Polygon(s, dirs, shape, poly, out problem)
            : square && s.Boxed ? Boxed(s, dirs, out problem) : Stacked(s, dirs, out problem);
        if (problem is not null) return parts;
        var k = Connectors.Kinds.First(x => x.Id == kind);
        string name = $"{char.ToUpperInvariant(k.Short[0])}{k.Short[1..]} column";
        foreach (var g in pieces.GroupBy(p => p.Name))
        {
            var first = g.First();
            parts.Add(new Part($"{name} {first.Name}", first.Cut, first.Etched, first.Lines, g.Count() * count)
            {
                Poses = [.. at.SelectMany(p => g.Select(x => x.At(p.Centre, p.Yaw)))],
            });
        }
        return parts;
    }

    /// <summary>
    /// The pieces of <paramref name="count"/> columns whose slots point the <paramref name="dirs"/> given
    /// (degrees anticlockwise from the first, which is 0), called <paramref name="name"/>, shown standing
    /// at each of <paramref name="at"/>. Boxed when every slot is at a right angle to the first (and boxed
    /// columns are asked for), otherwise a stack of layers.
    /// </summary>
    public static List<Part> Columns(WallSpec s, double[] dirs, string name, int count, IReadOnlyList<(V3 Centre, double Yaw)> at, out string? problem)
    {
        var parts = new List<Part>();
        bool square = dirs.All(d => Math.Abs(Math.IEEERemainder(d, 90)) < 1e-6);
        var shape = Connectors.ShapeFor(s, dirs);
        var pieces = s.ColumnBuild != ColumnBuild.Boxed ? Stacked(s, dirs, out problem)
            : s.BoxAnyShape && !(s.Boxed && square && shape == ColumnShape.Square) && BoxOutline(shape, s.ColumnMm, dirs) is { } poly ? Polygon(s, dirs, shape, poly, out problem)
            : square && s.Boxed ? Boxed(s, dirs, out problem) : Stacked(s, dirs, out problem);
        if (problem is not null) return parts;
        foreach (var g in pieces.GroupBy(p => p.Name))
        {
            var first = g.First();
            parts.Add(new Part($"{name} {first.Name}", first.Cut, first.Etched, first.Lines, g.Count() * count)
            {
                Poses = [.. at.SelectMany(p => g.Select(x => x.At(p.Centre, p.Yaw)))],
            });
        }
        return parts;
    }

    /// <summary>How high the top of a column is (mm), cap and all.</summary>
    public static double TopMm(WallSpec s)
    {
        double H = s.HeightMm, T = s.ThicknessMm;
        return s.ColumnTop switch
        {
            Walls.ColumnTop.Cap => H + T,
            Walls.ColumnTop.SteppedCap => H + 2 * T,
            Walls.ColumnTop.SteppedPoint => H + PointSizes(s).Count * T,
            Walls.ColumnTop.Battlements => H + MerlonHeight(s),
            _ => H,
        };
    }

    /// <summary>The narrowest a boxed column can be for the walls' tabs to clear its corners (mm).</summary>
    public static double NarrowestBoxed(WallSpec s) => s.WallMm + Math.Max(0, s.FitMm) + 2 * s.ThicknessMm + 2;

    static double MerlonHeight(WallSpec s) => Math.Clamp(s.MerlonHeightMm, 2, 12);

    /// <summary>The plates of a stepped point, wider to narrower (mm across).</summary>
    static List<double> PointSizes(WallSpec s)
    {
        double C = s.ColumnMm, lip = Math.Max(0, s.ColumnLipMm);
        var sizes = new List<double> { C + 2 * lip };
        foreach (var k in new[] { 0.75, 0.5, 0.25 }) if (C * k >= 3) sizes.Add(C * k);
        return sizes;
    }

    /// <summary>The plates a column stands on, bottom first (mm across).</summary>
    static List<double> BaseSizes(WallSpec s, double c, bool boxed)
    {
        double lip = Math.Max(0, s.ColumnLipMm);
        return s.ColumnBase switch
        {
            Walls.ColumnBase.SteppedPlinth => [c + 4 * lip, c + 2 * lip],
            Walls.ColumnBase.Foot => [c + 2 * lip],
            _ => boxed ? [c] : [],
        };
    }

    /// <summary>Notches in a plate round the foot so each wall (its base plate, or its faces above it) reaches the column's side.</summary>
    static PathsD Notches(PathsD plate, double[] dirs, double reach, double width)
    {
        var cut = new PathsD();
        foreach (var d in dirs) cut.AddRange(Geo.Turn([Geo.Rect(reach, -width / 2, reach + 100, width / 2)], d));
        return Geo.Subtract(plate, Geo.Union(cut));
    }

    static List<Piece> Boxed(WallSpec s, double[] dirs, out string? problem)
    {
        problem = null;
        double C = s.ColumnMm, T = s.ThicknessMm, W = s.WallMm, H = s.HeightMm, fit = Math.Max(0, s.FitMm), pl = Math.Max(0, s.PlinthMm);
        if (C < NarrowestBoxed(s)) { problem = $"a boxed column must be at least {NarrowestBoxed(s):0.#} mm wide for {W:0.#} mm walls of {T:0.#} mm sheet."; return []; }
        var pieces = new List<Piece>();
        var bases = BaseSizes(s, C, true);
        int k = bases.Count;
        double plug = C - 2 * T - fit;
        // Base plates, with notches where the walls come in.
        for (int i = 0; i < k; i++)
        {
            double y = i * T + T / 2;
            var plate = new PathsD { Geo.Rect(-bases[i] / 2, -bases[i] / 2, bases[i] / 2, bases[i] / 2) };
            if (bases[i] > C + 0.01) plate = Notches(plate, dirs, C / 2, (i == 0 ? W + 2 * pl : W) + fit);
            string name = k == 1 ? "base" : i == 0 ? "plinth" : "base";
            pieces.Add(new(name, plate, [], [], (c, yaw) => Plate(c, y, yaw)));
        }
        double floor = k * T;
        pieces.Add(new("plug", [Geo.Rect(-plug / 2, -plug / 2, plug / 2, plug / 2)], [], [], (c, yaw) => Plate(c, floor + T / 2, yaw)));

        // The sides: those facing 0° and 180° are the full width, the other two fit between them.
        double sideTop = s.ColumnTop == Walls.ColumnTop.Battlements ? H + MerlonHeight(s) : H;
        var tabs = BoxWall.EndTabs(s);
        foreach (var facing in new double[] { 0, 90, 180, 270 })
        {
            bool full = facing is 0 or 180;
            double w = full ? C : C - 2 * T;
            var side = new PathsD { Geo.Rect(0, floor, w, sideTop) };
            bool wall = dirs.Any(d => Math.Abs(Math.IEEERemainder(d - facing, 360)) < 1e-6);
            var slots = new PathsD();
            if (wall)
                foreach (var (y0, y1) in tabs)
                    foreach (var off in new[] { -(W / 2 - T / 2), W / 2 - T / 2 })
                        slots.Add(Geo.Rect(w / 2 + off - (T + fit) / 2, y0 - fit / 2, w / 2 + off + (T + fit) / 2, y1 + fit / 2));
            if (s.ColumnTop == Walls.ColumnTop.Battlements)
            {
                double cw = Math.Max(2, C / 3);
                slots.Add(Geo.Rect(w / 2 - cw / 2, H, w / 2 + cw / 2, sideTop + 1));
            }
            var cut = Geo.Subtract(side, slots);
            // The column's texture on each side, where no wall covers it.
            var region = Geo.Subtract(cut, Geo.Offset(slots, 0.5));
            if (wall) region = Geo.Subtract(region, [Geo.Rect(w / 2 - W / 2 - 0.5, 0, w / 2 + W / 2 + 0.5, H)]);
            var (e, l) = BoxWall.FaceTexture(s, 10 + (int)facing / 90, w, sideTop, region);
            double f = facing;
            // A side no wall comes into has no slots: it's a piece of its own.
            pieces.Add(new((wall ? "" : "blank ") + (full ? "side" : "inner side"), cut, Geo.Union(e), l, (c, yaw) => Side(c, yaw + f, C / 2 - T / 2, w)));
        }

        // The top.
        switch (s.ColumnTop)
        {
            case Walls.ColumnTop.Flush:
            case Walls.ColumnTop.Battlements:
                pieces.Add(new("top", [Geo.Rect(-plug / 2, -plug / 2, plug / 2, plug / 2)], [], [], (c, yaw) => Plate(c, H - T / 2, yaw)));
                break;
            default:
            {
                pieces.Add(new("plug", [Geo.Rect(-plug / 2, -plug / 2, plug / 2, plug / 2)], [], [], (c, yaw) => Plate(c, H - T / 2, yaw)));
                var sizes = s.ColumnTop switch
                {
                    Walls.ColumnTop.SteppedCap => [C + 2 * s.ColumnLipMm, C + 4 * s.ColumnLipMm],
                    Walls.ColumnTop.SteppedPoint => PointSizes(s),
                    _ => new List<double> { C + 2 * s.ColumnLipMm },
                };
                for (int i = 0; i < sizes.Count; i++)
                {
                    double y = H + i * T + T / 2, a = sizes[i];
                    pieces.Add(new(sizes.Count == 1 ? "cap" : $"cap {i + 1}", [Geo.Rect(-a / 2, -a / 2, a / 2, a / 2)], [], [], (c, yaw) => Plate(c, y, yaw)));
                }
                break;
            }
        }
        return pieces;
    }

    /// <summary>
    /// The outline of a column of this shape that can be boxed for walls coming in along <paramref name="dirs"/>
    /// (straight sides, one facing each wall), <paramref name="across"/> mm across its flats; null if it can't
    /// (fluted, or round with walls at angles no number of flats suits).
    /// </summary>
    public static PathD? BoxOutline(ColumnShape shape, double across, double[] dirs)
    {
        switch (shape)
        {
            case ColumnShape.Square: case ColumnShape.Octagon: case ColumnShape.Hexagon: case ColumnShape.Chamfered:
                return Connectors.Outline(shape, across);
            case ColumnShape.Round:
                foreach (var n in new[] { 12, 16, 24 })
                    if (dirs.All(d => Math.Abs(Math.IEEERemainder(d, 360.0 / n)) < 1e-6)) return Connectors.Regular(n, across / 2, 0);
                return null;
            default: return null;
        }
    }

    /// <summary>One flat of a boxed column: which way it faces (degrees, on the column's plan), how far out it is, and the side that stands on it.</summary>
    readonly record struct Flat(double Facing, double Apothem, double Along, double Width, double Inner);

    /// <summary>The flats of a convex outline, and how wide a side standing on each can be, <paramref name="T"/> thick, clear of its neighbours.</summary>
    static List<Flat> Flats(PathD outline, double T, double fit)
    {
        int n = outline.Count;
        var lines = new List<(PointD N, double A, PointD D)>();
        for (int i = 0; i < n; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % n];
            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            var nn = new PointD(dy / len, -dx / len);
            var mid = new PointD((a.x + b.x) / 2, (a.y + b.y) / 2);
            if (mid.x * nn.x + mid.y * nn.y < 0) nn = new PointD(-nn.x, -nn.y);
            lines.Add((nn, mid.x * nn.x + mid.y * nn.y, new PointD(-nn.y, nn.x)));
        }
        // Where the insides of two neighbouring sides meet.
        PointD Meet(int i, int j, double off)
        {
            var (n1, a1, _) = lines[i];
            var (n2, a2, _) = lines[j];
            double det = n1.x * n2.y - n1.y * n2.x;
            return new PointD(((a1 - off) * n2.y - (a2 - off) * n1.y) / det, (n1.x * (a2 - off) - n2.x * (a1 - off)) / det);
        }
        // Every right-angled corner lets one side run on over the other's end (alternately), as a square box does.
        int m = lines.Count;
        bool square = m == 4 && lines.All(l => Math.Abs(Math.IEEERemainder(Math.Atan2(l.N.y, l.N.x) * 180 / Math.PI, 90)) < 1e-6);
        var flats = new List<Flat>();
        for (int i = 0; i < m; i++)
        {
            var (nn, a, d) = lines[i];
            bool full = square && i % 2 == 0;
            double off = full ? 0 : T;
            var p0 = Meet((i + m - 1) % m, i, off);
            var p1 = Meet(i, (i + 1) % m, off);
            double t0 = p0.x * d.x + p0.y * d.y, t1 = p1.x * d.x + p1.y * d.y;
            double lo = Math.Min(t0, t1) + (full ? 0 : fit / 2), hi = Math.Max(t0, t1) - (full ? 0 : fit / 2);
            var i0 = Meet((i + m - 1) % m, i, T);
            var i1 = Meet(i, (i + 1) % m, T);
            double inner = Math.Abs((i1.x - i0.x) * d.x + (i1.y - i0.y) * d.y);
            flats.Add(new Flat(Math.Atan2(nn.y, nn.x) * 180 / Math.PI, a, (lo + hi) / 2, hi - lo, inner));
        }
        return flats;
    }

    /// <summary>
    /// A boxed column of any straight-sided shape: a side standing on every flat, its tabs top and bottom in
    /// slots in the plate it stands on and in the plate across its top, so the box locks together. Box walls'
    /// tabs go into slots in the sides they meet; a flat wall's end goes between the two halves of the side it
    /// meets, through notches in the plates, into the column. Each side has the column's texture where no wall covers it.
    /// </summary>
    static List<Piece> Polygon(WallSpec s, double[] dirs, ColumnShape shape, PathD outline, out string? problem)
    {
        problem = null;
        double C = s.ColumnMm, T = s.ThicknessMm, H = s.HeightMm, fit = Math.Max(0, s.FitMm), pl = Math.Max(0, s.PlinthMm), lip = Math.Max(0, s.ColumnLipMm);
        bool flatWalls = !s.Boxed;
        double W = flatWalls ? WallKitMaker.WallThickness(s) : s.WallMm;
        double rim = Math.Max(1.5, T);
        var flats = Flats(outline, T, fit);
        string name = shape switch { ColumnShape.Octagon => "octagonal", ColumnShape.Hexagon => "hexagonal", ColumnShape.Chamfered => "chamfered", ColumnShape.Round => "round", _ => "square" };
        static bool Same(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360)) < 1e-3;
        if (dirs.Any(d => !flats.Any(f => Same(f.Facing, d)))) { problem = $"a wall would meet a corner of {(name[0] is 'o' ? "an" : "a")} {name} boxed column here: pick another shape, or stacked layers."; return []; }
        // Wide enough for each wall to go into its side, with sheet to spare either side.
        double need = flatWalls ? W + fit + 2 * 2.5 : W + T + fit + 1.6;
        foreach (var f in flats.Where(f => dirs.Any(d => Same(f.Facing, d))))
            if (f.Width < need)
            {
                double k = f.Inner / Math.Max(0.1, f.Apothem - T);
                double wide = Math.Ceiling(2 * (need / Math.Max(0.05, k) + T) + 1);
                problem = $"{(name[0] is 'o' ? "an" : "a")} {name} boxed column must be at least {wide:0} mm across for {W:0.#} mm walls (its sides are too narrow for them): make the columns wider, or build these as stacked layers.";
                return [];
            }
        if (flats.Any(f => f.Width < 3)) { problem = $"{(name[0] is 'o' ? "an" : "a")} {name} boxed column's sides come out too narrow: make the columns wider, or build them as stacked layers."; return []; }
        if (H < 4 * T) { problem = "the walls are too low for boxed columns: build them as stacked layers."; return []; }

        // The flat's plan frame: along it (anticlockwise) and out of it.
        static PointD P(Flat f, double along, double outward)
        {
            double a = f.Facing * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
            return new PointD(c * outward - sn * along, sn * outward + c * along);
        }
        static PathD Box(Flat f, double a0, double a1, double o0, double o1) => [P(f, a0, o0), P(f, a1, o0), P(f, a1, o1), P(f, a0, o1)];
        PathsD Grown(double size) => [Geo.Map([outline], q => new PointD(q.x * size / C, q.y * size / C))[0]];

        var pieces = new List<Piece>();
        var bases = s.ColumnBase switch
        {
            Walls.ColumnBase.SteppedPlinth => new List<double> { C + 2 * Math.Max(2 * lip, rim + lip), C + 2 * Math.Max(lip, rim) },
            Walls.ColumnBase.Foot => [C + 2 * Math.Max(lip, rim)],
            _ => [C + 2 * rim],
        };
        int nb = bases.Count;
        double floor = nb * T, top = H - T;
        // The sides, and their tabs into the plates (one at each end of each piece of side).
        var slots = new PathsD();
        // A flat wall's end comes into the column between the halves of its side: the plates are notched for it.
        double into = flatWalls ? Math.Max(T, s.SlotDepthMm) + WallKitMaker.EndClearMm : 0;
        var tabs = flatWalls ? [] : BoxWall.EndTabs(s);
        int seed = 10;
        string Width(Flat f) => flats.Select(x => Math.Round(x.Width, 1)).Distinct().Count() > 1 ? (f.Width >= flats.Max(x => x.Width) - 0.05 ? "wide " : "narrow ") : "";
        foreach (var f in flats)
        {
            bool wall = dirs.Any(d => Same(f.Facing, d));
            double w = f.Width, x0 = f.Along - w / 2;
            // Its pieces across (panel x), and a tab on each.
            var spans = wall && flatWalls ? new List<(double A, double B)> { (0, (w - W - fit) / 2), ((w + W + fit) / 2, w) } : [(0, w)];
            var cut = new PathsD();
            foreach (var (a, b) in spans)
            {
                double tw = Math.Clamp((b - a) * 0.4, 1.5, 8), m = (a + b) / 2;
                cut.Add(Geo.Rect(a, floor, b, top));
                cut.Add(Geo.Rect(m - tw / 2, floor - T, m + tw / 2, top + T));
                slots.Add(Box(f, x0 + m - (tw + fit) / 2, x0 + m + (tw + fit) / 2, f.Apothem - T - fit / 2, f.Apothem + fit / 2));
            }
            cut = Geo.Union(cut);
            var holes = new PathsD();
            if (wall && !flatWalls)
                foreach (var (y0, y1) in tabs)
                    foreach (var off in new[] { -(W / 2 - T / 2), W / 2 - T / 2 })
                        holes.Add(Geo.Rect(w / 2 + off - (T + fit) / 2, y0 - fit / 2, w / 2 + off + (T + fit) / 2, y1 + fit / 2));
            cut = Geo.Subtract(cut, holes);
            // The column's texture on its face, where no wall covers it.
            var region = Geo.Subtract(Geo.Intersect(cut, [Geo.Rect(0, floor, w, top)]), Geo.Offset(holes, 0.5));
            if (wall) region = Geo.Subtract(region, [Geo.Rect(w / 2 - W / 2 - 0.5, 0, w / 2 + W / 2 + 0.5, H)]);
            var (e, l) = BoxWall.FaceTexture(s, seed++, w, top, region);
            double facing = f.Facing, dist = f.Apothem - T / 2, along = f.Along;
            string piece = wall ? (flatWalls ? $"{Width(f)}half side" : $"{Width(f)}side") : $"{Width(f)}blank side";
            if (wall && flatWalls)
            {
                // Two halves, alike: each its own piece, the second where the first's mirror would be.
                var half = Geo.Intersect(cut, [Geo.Rect(-1, -T - 1, (w - W - fit) / 2 + 0.01, H + 1)]);
                var he = Geo.Intersect(Geo.Union(e), half);
                var hl = Geo.ClipLines(l, half);
                double hw = (w - W - fit) / 2, second = (w + W + fit) / 2;
                pieces.Add(new(piece, half, he, hl, (c, yaw) => SideAt(c, yaw + facing, dist, along - w / 2)));
                pieces.Add(new(piece, half, he, hl, (c, yaw) => SideAt(c, yaw + facing, dist, along - w / 2 + second)));
                _ = hw;
            }
            else pieces.Add(new(piece, cut, Geo.Union(e), l, (c, yaw) => SideAt(c, yaw + facing, dist, along - w / 2)));
        }

        // A plate notched where the walls come in (outside the column, and for flat walls on into it).
        PathsD Notched(PathsD plate, double width)
        {
            if (flatWalls) return Notches(plate, dirs, C / 2 - into, W + fit);
            return Notches(plate, dirs, C / 2, width);
        }
        for (int i = 0; i < nb; i++)
        {
            double y = i * T + T / 2;
            var plate = Notched(Grown(bases[i]), (i == 0 ? W + 2 * pl : W) + fit);
            if (i == nb - 1) plate = Geo.Subtract(plate, slots);
            string pn = nb == 1 ? "base" : i == 0 ? "plinth" : "base";
            pieces.Add(new(pn, plate, [], [], (c, yaw) => Plate(c, y, yaw)));
        }
        // The plate across the top, holding the sides' top tabs; caps or merlons go on it.
        var topPlate = Geo.Subtract(Notched(Grown(C + 2 * rim), W + fit), slots);
        pieces.Add(new("top", topPlate, [], [], (c, yaw) => Plate(c, H - T / 2, yaw)));
        switch (s.ColumnTop)
        {
            case Walls.ColumnTop.Cap:
            case Walls.ColumnTop.SteppedCap:
            case Walls.ColumnTop.SteppedPoint:
            {
                var sizes = s.ColumnTop switch
                {
                    Walls.ColumnTop.SteppedCap => [C + 2 * Math.Max(lip, rim) + 2 * lip, C + 2 * Math.Max(lip, rim) + 4 * lip],
                    Walls.ColumnTop.SteppedPoint => PointSizes(s),
                    _ => new List<double> { C + 2 * Math.Max(lip, rim) + 2 * lip },
                };
                for (int i = 0; i < sizes.Count; i++)
                {
                    double y = H + i * T + T / 2;
                    pieces.Add(new(sizes.Count == 1 ? "cap" : $"cap {i + 1}", Grown(sizes[i]), [], [], (c, yaw) => Plate(c, y, yaw)));
                }
                break;
            }
            case Walls.ColumnTop.Battlements:
            {
                double cw = Math.Max(2, C / 3);
                var crenel = Geo.Subtract(Grown(C), Geo.Union(new PathsD { Geo.Rect(-C, -cw / 2, C, cw / 2), Geo.Rect(-cw / 2, -C, cw / 2, C) }));
                int n = Math.Max(1, (int)Math.Round(MerlonHeight(s) / T));
                for (int i = 0; i < n; i++)
                {
                    double y = H + i * T + T / 2;
                    pieces.Add(new("merlons", crenel, [], [], (c, yaw) => Plate(c, y, yaw)));
                }
                break;
            }
        }
        return pieces;
    }

    /// <summary>A side standing <paramref name="dist"/> out from the middle, facing <paramref name="facing"/>, its left edge (seen from outside) <paramref name="from"/> along the flat.</summary>
    static Pose SideAt(V3 c, double facing, double dist, double from)
    {
        var n = Dir(facing);
        var u = Dir(facing + 90);
        return new Pose(c + n * dist + u * from, u, V3.UnitY);
    }

    static List<Piece> Stacked(WallSpec s, double[] dirs, out string? problem)
    {
        problem = null;
        double C = Math.Max(3, s.ColumnMm), T = s.ThicknessMm, W = s.WallMm, H = s.HeightMm, fit = Math.Max(0, s.FitMm), pl = Math.Max(0, s.PlinthMm);
        var shape = Connectors.ShapeFor(s, dirs);
        var outline = Connectors.Outline(shape, C, dirs, W);
        double depth = BoxWall.TabLength(s) + WallKitMaker.EndClearMm;
        PathsD Slots()
        {
            var slots = new PathsD();
            foreach (var d in dirs)
            {
                double reach = Connectors.Reach(outline, d);
                foreach (var off in new[] { -(W / 2 - T / 2), W / 2 - T / 2 })
                    slots.AddRange(Geo.Turn([Geo.Rect(reach - depth, off - (T + fit) / 2, reach + 1, off + (T + fit) / 2)], d));
            }
            return slots;
        }
        var slots = Slots();
        var pin = s.PinMm > 0 ? new PathsD { Geo.Circle(0, 0, s.PinMm / 2) } : [];
        double side = dirs.Min(d => Connectors.Reach(outline, d + 90));
        if (side < W / 2 + fit / 2 + 1) { problem = $"a column must be wider than {W + fit + 2:0.#} mm to take {W:0.#} mm walls."; return []; }
        if (Geo.Area(Geo.Union(slots)) < slots.Sum(p => Math.Abs(Clipper.Area(p))) - 1e-3) { problem = "the walls' slots run into each other in the middle: make the column wider."; return []; }
        if (pin.Count > 0 && Geo.Area(Geo.Intersect(Geo.Offset(pin, 0.8), Geo.Union(slots))) > 1e-4) { problem = "the pin hole runs into a slot: make the pin thinner or the column wider."; return []; }
        var body = Geo.Subtract(Geo.Subtract([outline], Geo.Union(slots)), pin);
        if (Geo.Pieces(body) != 1) { problem = "the slots cut the column apart: make it wider."; return []; }

        var pieces = new List<Piece>();
        var bases = BaseSizes(s, C, false);
        for (int i = 0; i < bases.Count; i++)
        {
            var bigger = Connectors.Outline(Connectors.PlateShape(shape), bases[i]);
            var foot = Geo.Subtract(Notches([bigger], dirs, C / 2, (i == 0 ? W + 2 * pl : W) + fit), Geo.Union(slots));
            double y = i * T + T / 2;
            pieces.Add(new(bases.Count == 1 ? "foot" : i == 0 ? "plinth" : "foot", Geo.Subtract(foot, pin), [], [], (c, yaw) => Plate(c, y, yaw)));
        }
        int layers = Math.Max(1, (int)Math.Ceiling(H / T - 0.02)) - bases.Count;
        for (int i = 0; i < layers; i++)
        {
            double y = (bases.Count + i) * T + T / 2;
            pieces.Add(new("layer", body, [], [], (c, yaw) => Plate(c, y, yaw)));
        }
        double top = (bases.Count + layers) * T;
        var caps = s.ColumnTop switch
        {
            Walls.ColumnTop.Cap => [C + 2 * s.ColumnLipMm],
            Walls.ColumnTop.SteppedCap => [C + 2 * s.ColumnLipMm, C + 4 * s.ColumnLipMm],
            Walls.ColumnTop.SteppedPoint => PointSizes(s),
            _ => new List<double>(),
        };
        for (int i = 0; i < caps.Count; i++)
        {
            double y = top + i * T + T / 2;
            pieces.Add(new(caps.Count == 1 ? "cap" : $"cap {i + 1}", Geo.Subtract([Connectors.Outline(Connectors.PlateShape(shape), caps[i])], i == 0 ? pin : []), [], [], (c, yaw) => Plate(c, y, yaw)));
        }
        if (s.ColumnTop == Walls.ColumnTop.Battlements)
        {
            // Layers with a gap across the middle each way: four merlons round the top.
            double cw = Math.Max(2, C / 3);
            var crenel = Geo.Subtract([outline], Geo.Union(new PathsD { Geo.Rect(-C, -cw / 2, C, cw / 2), Geo.Rect(-cw / 2, -C, cw / 2, C) }));
            int n = Math.Max(1, (int)Math.Round(MerlonHeight(s) / T));
            for (int i = 0; i < n; i++)
            {
                double y = top + i * T + T / 2;
                pieces.Add(new("merlons", crenel, [], [], (c, yaw) => Plate(c, y, yaw)));
            }
        }
        return pieces;
    }
}
