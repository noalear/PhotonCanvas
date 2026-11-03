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
        var pieces = s.ColumnBuild == ColumnBuild.Boxed && square ? Boxed(s, dirs, out problem) : Stacked(s, dirs, out problem);
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

    static List<Piece> Stacked(WallSpec s, double[] dirs, out string? problem)
    {
        problem = null;
        double C = Math.Max(3, s.ColumnMm), T = s.ThicknessMm, W = s.WallMm, H = s.HeightMm, fit = Math.Max(0, s.FitMm), pl = Math.Max(0, s.PlinthMm);
        var shape = Connectors.ShapeFor(s, dirs);
        var outline = Connectors.Outline(shape, C);
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
            var bigger = Connectors.Outline(shape, bases[i]);
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
            pieces.Add(new(caps.Count == 1 ? "cap" : $"cap {i + 1}", Geo.Subtract([Connectors.Outline(shape, caps[i])], i == 0 ? pin : []), [], [], (c, yaw) => Plate(c, y, yaw)));
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
