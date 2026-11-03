using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Finding = PhotonCanvas.Addon.Buildings.Finding;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>What a piece in the 3D view is, so the view can hide walls or show one floor.</summary>
public enum SolidKind { Wall, Column, Floor, Ground, Roof, Furniture, Railing }

/// <summary>
/// One piece where it sits once the building is put together: its outline at <see cref="Pose"/>,
/// <see cref="Thickness"/> thick (centred on the pose's plane), what's etched on its +N face, the floor
/// it belongs to, and for a wall's pieces which way the wall faces and where its middle is (so the view
/// can cut away the walls between you and the rooms).
/// </summary>
public sealed record HouseSolid(string Name, PathsD Outline, Pose Pose, double Thickness, PathsD? Etched, List<PathD>? Lines, int Level, SolidKind Kind)
{
    public V3 Facing { get; init; }
    public V3 Middle { get; init; }
}

/// <summary>A straight wall as made: which floor, its letter, its two ends on the plan (squares) and what it was made from.</summary>
public sealed record MadeWall(int Level, string Letter, GridPt A, GridPt B, WallSpec Spec, bool Ok);

/// <summary>Everything the building is cut from, where it all goes, and what was found while making it.</summary>
public sealed record HouseKit(List<Piece> Pieces, List<HouseSolid> Solids, List<Finding> Findings, List<MadeWall> Walls)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Pieces.Sum(p => p.Copies);
    public int Count(Sheet s) => Pieces.Where(p => p.Sheet == s).Sum(p => p.Copies);
    /// <summary>The plan point (squares) that's the middle of the building in the 3D view.</summary>
    public (double X, double Y) Origin { get; init; }
}

/// <summary>
/// Makes a building drawn in the editor from the wall maker's pieces: every straight run of wall
/// between two columns is one wall (box or flat) with its doors and windows where they were dropped, a
/// column at every corner, end and junction with a slot for each wall that meets there, a floor over
/// each storey shaped to the rooms below and above it (and any floor laid outside them), and the floor
/// tiles under it all.
/// </summary>
public static partial class HouseKitMaker
{
    /// <summary>The most levels a building can have.</summary>
    public const int MaxLevels = 6;

    static double K(BuildingSpec s) => Math.Max(0.2, s.ScaleMm / 28);

    /// <summary>The whole-building settings the walls are made with: as many storeys as levels, straight tops.</summary>
    public static BuildingSpec StyleFor(HouseDesign d) => d.Style with
    {
        Storeys = Math.Clamp(d.Levels.Count, 1, MaxLevels), Top = BuildingTop.Straight, Roof = RoofShape.None, Items = [],
    };

    /// <summary>A run's length from column centre to column centre (mm).</summary>
    public static double Length(Run r, double sq) => r.Steps * sq * (r.Diagonal ? Math.Sqrt(2) : 1);

    /// <summary>The letter a wall goes by: A, B … Z, AA …</summary>
    public static string Letter(int i) => TileKitMaker.Column(i);

    /// <summary>
    /// The building's pieces. <paramref name="longestMm"/> is the longest piece the bed takes: walls
    /// longer are cut at a grid point with a straight column between, and floors bigger are cut in pieces
    /// along the walls under them.
    /// </summary>
    public static HouseKit Make(HouseDesign d, double longestMm = 216)
    {
        var findings = new List<Finding>();
        var pieces = new List<Piece>();
        var solids = new List<HouseSolid>();
        var made = new List<MadeWall>();
        var s = StyleFor(d);
        double sq = Math.Max(5, d.SquareMm), T = s.WallSheetMm, Wt = BuildingKitMaker.WallThickness(s), H = BuildingKitMaker.StoreyHeight(s), C = s.ColumnMm;
        int levels = Math.Min(d.Levels.Count, MaxLevels);
        if (levels < d.Levels.Count) findings.Add(new(Severity.Warn, $"Only the first {MaxLevels} floors are made."));
        if (T < 0.5) { findings.Add(new(Severity.Bad, "The wall sheet is too thin: use at least 0.5 mm.")); return new([], [], findings, made); }
        if (s.BoxWalls && C < s.NarrowestBoxColumn) { findings.Add(new(Severity.Bad, $"The columns are too thin for {Wt:0.#} mm box walls: make them at least {Math.Ceiling(s.NarrowestBoxColumn * 10) / 10:0.#} mm across.")); return new([], [], findings, made); }
        if (C < Wt + 3) { findings.Add(new(Severity.Bad, $"The columns are too thin for a {Wt:0.#} mm wall: make them at least {Wt + 3:0.#} mm across.")); return new([], [], findings, made); }
        if (d.Levels.Take(levels).All(l => l.Walls.Count == 0)) { findings.Add(new(Severity.Bad, "There are no walls yet: draw some on the plan.")); return new([], [], findings, made); }
        if (Math.Abs(H - s.StoreyHeightMm) > 0.05)
            findings.Add(new(Severity.Note, $"Each storey is {H:0.#} mm high: a whole number of {T:0.##} mm layers, so the columns come out as tall as the walls."));

        var plans = d.Levels.Take(levels).Select(l => PlanShape.Of(l.Walls)).ToList();
        // The plan's middle (squares): the middle of everything drawn, so the building sits on the origin.
        var allPts = d.Levels.Take(levels).SelectMany(l => l.Walls.SelectMany(e => new[] { e.A, e.B }).Concat(l.Floor.SelectMany(f => new[] { f, f + new GridPt(1, 1) }))).ToList();
        double ox = (allPts.Min(p => p.X) + allPts.Max(p => p.X)) / 2.0, oy = (allPts.Min(p => p.Y) + allPts.Max(p => p.Y)) / 2.0;
        V3 World(double px, double py, double y) => new((px - ox) * sq, y, (py - oy) * sq);

        // Each floor's plate (plan mm, y towards you), worked out first: the columns under it are flush with it.
        var plates = new List<PathsD> { new() };
        for (int k = 1; k < levels; k++) plates.Add(PlateArea(d, plans, k, sq, C));

        for (int k = 0; k < levels; k++)
        {
            var level = d.Levels[k];
            var plan = plans[k];
            double y0 = BuildingKitMaker.StoreyBase(s, k);
            string floorName = levels == 1 ? "" : $"floor {k + 1} ";
            var (runs, extraJoints) = Split(level, plan, s, sq, k, longestMm, findings);
            // Walls that come out the same are cut together: their pieces once, with copies for each.
            var groups = new Dictionary<string, (List<Walls.Part> Parts, WallSpec Spec, List<string> Letters)>();
            int letter = 0;
            foreach (var (run, openings) in runs)
            {
                string name = Letter(letter++);
                double L = Length(run, sq);
                var spec = WallFor(s, k, L, openings);
                var kit = WallKitMaker.Make(spec);
                string what = $"{(levels == 1 ? "Wall" : $"Floor {k + 1} wall")} {name}";
                foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
                    findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"{what}: {f.Text}"));
                made.Add(new MadeWall(k, name, run.A, run.B, spec, kit.Ok));
                if (!kit.Ok || kit.Front is not { } panel) continue;

                // The wall's frame: along it from A to B, up, its front facing out to the right of A → B.
                var a = World(run.A.X, run.A.Y, y0);
                var b = World(run.B.X, run.B.Y, y0);
                var u = Unit(b - a);
                var normal = new V3(-u.Z, 0, u.X);
                var pose = new Pose(a + u * WallKitMaker.CentreToEnd(spec), u, new V3(0, 1, 0));
                var middle = (a + b) * 0.5 + new V3(0, H / 2, 0);
                if (spec.Boxed)
                {
                    foreach (var part in kit.Parts)
                        foreach (var at in part.Poses)
                            solids.Add(new HouseSolid(part.Name == "Front" ? $"Wall {name}" : $"Wall {name} {part.Name.ToLowerInvariant()}", part.Cut, InWall(pose, at), T, Nil(part.Etched), Nil(part.Lines), k,
                                part.Name == "Door" ? SolidKind.Furniture : SolidKind.Wall) { Facing = normal, Middle = middle });
                }
                else
                {
                    solids.Add(new HouseSolid($"Wall {name}", panel.Cut, pose, Wt, Nil(panel.Etched), Nil(panel.Lines), k, SolidKind.Wall) { Facing = normal, Middle = middle });
                    if (kit.Parts.FirstOrDefault(p => p.Name is "Door" or "Portcullis") is { } leaf && leaf.Poses.Count > 0)
                        solids.Add(new HouseSolid($"Wall {name} door", leaf.Cut, pose with { O = pose.At(leaf.Poses[0].O.X, 0) }, Wt, Nil(leaf.Etched), Nil(leaf.Lines), k, SolidKind.Furniture) { Facing = normal, Middle = middle });
                }

                string key = spec.ToJson();
                if (groups.TryGetValue(key, out var g)) g.Letters.Add(name);
                else groups[key] = (kit.Parts, spec, [name]);
            }
            foreach (var (key, (parts, spec, letters)) in groups)
            {
                string label = letters.Count == 1 ? $"wall {letters[0]}" : $"walls {string.Join(", ", letters)}";
                foreach (var part in parts)
                {
                    if (!spec.Boxed && part.Name.Contains("column", StringComparison.Ordinal)) continue;
                    bool face = part.Name is "Front" or "Wall";
                    string pn = face ? "" : $": {part.Name.ToLowerInvariant()}";
                    pieces.Add(new Piece($"{Cap(floorName + label)}{pn}", Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies * letters.Count) { WallSpecJson = face ? key : null });
                }
            }

            // The columns: one at every corner, end and junction, and where a long wall was cut.
            var joints = plan.Joints.ToDictionary(j => j.Key, j => j.Value);
            foreach (var (g2, dirs) in extraJoints) joints[g2] = dirs;
            var byShape = new Dictionary<string, (double[] Dirs, bool Flush, List<(Walls.Model.V3, double)> At)>();
            foreach (var (pt, dirs) in joints)
            {
                var angles = dirs.Select(PlanShape.Angle).Order().ToArray();
                // Turned so the first slot is the one after the widest gap: the same junction turned round comes out the same.
                int start = 0;
                double widest = -1;
                for (int i = 0; i < angles.Length; i++)
                {
                    double gap = ((angles[i] - angles[(i + angles.Length - 1) % angles.Length]) % 360 + 360) % 360;
                    if (angles.Length == 1) gap = 360;
                    if (gap > widest + 1e-6) { widest = gap; start = i; }
                }
                double yaw = angles[start];
                var rel = angles.Select(x => Math.Round(((x - yaw) % 360 + 360) % 360, 3)).Order().ToArray();
                bool flush = k + 1 < levels && Inside(plates[k + 1], (pt.X) * sq, (pt.Y) * sq);
                string key = $"{string.Join(",", rel)}|{flush}";
                var w = World(pt.X, pt.Y, y0);
                if (!byShape.TryGetValue(key, out var e)) byShape[key] = e = (rel, flush, []);
                e.At.Add((new Walls.Model.V3(w.X, w.Y, w.Z), yaw));
            }
            foreach (var (_, (rel, flush, at)) in byShape.OrderBy(x => x.Value.Dirs.Length).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                var spec = Topped(BuildingKitMaker.StoreyWall(s, k, 100), !flush);
                string kind = ColumnName(rel);
                string label = Cap($"{floorName}{kind}{(flush || levels == 1 ? "" : ", top")}");
                if (s.BoxWalls)
                {
                    var parts = BoxColumns.Columns(spec, rel, label, at.Count, at, out var problem);
                    if (problem is not null) { findings.Add(new(Severity.Bad, $"{Cap(kind)}s: {problem}")); continue; }
                    foreach (var p in parts)
                    {
                        pieces.Add(new Piece(p.Name, Sheet.Walls, p.Cut, p.Etched, p.Lines, p.Copies));
                        foreach (var pose in p.Poses)
                            solids.Add(new HouseSolid("Column", p.Cut, Building(pose), T, Nil(p.Etched), Nil(p.Lines), k, SolidKind.Column));
                    }
                }
                else
                {
                    double slotW = WallKitMaker.WallThickness(spec) + Math.Max(0, spec.FitMm);
                    var col = Connectors.Column(spec, rel, slotW, Math.Max(0.5, spec.SlotDepthMm), out var problem);
                    if (problem is not null) { findings.Add(new(Severity.Bad, $"{Cap(kind)}s: {problem}")); continue; }
                    int layers = WallKitMaker.ColumnLayers(spec);
                    pieces.Add(new Piece($"{label} layer", Sheet.Walls, col.Body, [], [], layers * at.Count));
                    if (!flush) pieces.Add(new Piece($"{label} cap", Sheet.Walls, col.Top, [], [], at.Count));
                    double tall = layers * T;
                    foreach (var (c, yaw) in at)
                    {
                        solids.Add(new HouseSolid("Column", col.Body, BuildingKitMaker.Flat(c.X, c.Y + tall / 2, c.Z, yaw), tall, null, null, k, SolidKind.Column));
                        if (!flush) solids.Add(new HouseSolid("Column cap", col.Top, BuildingKitMaker.Flat(c.X, c.Y + tall + T / 2, c.Z, yaw), T, null, null, k, SolidKind.Column));
                    }
                }
            }
        }
        if (s.BoxWalls) findings.Add(new(Severity.Note, $"Every column is {C:0.#} mm across: a box (or, where walls meet at an angle, a stack of layers on a pin) with a slot for each wall's tabs."));
        else findings.Add(new(Severity.Note, $"Stack each column's layers on a {BuildingKitMaker.PinMm:0.#} mm pin (a cocktail stick) and glue them."));

        for (int k = 1; k < levels; k++)
        {
            var slots = Railings(d, s, plans, k, sq, World, pieces, solids, findings);
            Floor(d, s, plans, plates[k], k, sq, longestMm, World, slots, pieces, solids, findings);
        }
        RoomFloors(d, s, plans[0], sq, longestMm, World, pieces, solids, findings);
        Roofs(d, s, plans, plates, sq, World, pieces, solids, findings);
        Tiles(d, s, sq, ox, oy, pieces, solids, findings);

        double tallest = solids.Count == 0 ? 0 : solids.Max(x => x.Pose.O.Y + x.Thickness);
        var outline = new PathsD(solids.Where(x => x.Kind is SolidKind.Wall or SolidKind.Column).Select(x => Geo.Rect(x.Pose.O.X - 1, x.Pose.O.Z - 1, x.Pose.O.X + 1, x.Pose.O.Z + 1)));
        var box = Geo.Bounds(outline);
        if (outline.Count > 0)
            findings.Add(new(Severity.Note, $"The building is about {box.right - box.left + C:0} × {box.bottom - box.top + C:0} mm on the ground, {levels} floor{(levels == 1 ? "" : "s")} high."));
        return new(pieces, solids, findings, made) { Origin = (ox, oy) };
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A column by the walls that meet at it.</summary>
    static string ColumnName(double[] rel) => rel.Length switch
    {
        1 => "end column",
        2 when Math.Abs(rel[1] - 180) < 1e-3 => "straight column",
        2 when Math.Abs(rel[1] - 90) < 1e-3 || Math.Abs(rel[1] - 270) < 1e-3 => "corner column",
        2 => $"{Math.Round(Math.Min(rel[1], 360 - rel[1])):0}° column",
        3 when rel.All(r => Math.Abs(Math.IEEERemainder(r, 90)) < 1e-3) => "T column",
        4 when rel.All(r => Math.Abs(Math.IEEERemainder(r, 90)) < 1e-3) => "cross column",
        _ => $"{rel.Length}-way column",
    };

    static WallSpec Topped(WallSpec w, bool cap) => w with { Cap = cap, ColumnTop = cap ? ColumnTop.Cap : ColumnTop.Flush };

    /// <summary>The wall maker's settings for one run: the storey's walls with the doors and windows dropped in it.</summary>
    public static WallSpec WallFor(BuildingSpec s, int k, double length, List<(Opening O, double Along)> openings)
    {
        var spec = BuildingKitMaker.StoreyWall(s, k, length);
        if (openings.FirstOrDefault(o => o.O.Kind != OpeningKind.Window) is { O: { } door } dw)
        {
            spec = spec with
            {
                Door = door.Kind == OpeningKind.Doorway ? DoorKind.Doorway : DoorKind.DoorwayAndDoor,
                DoorOpens = door.Kind == OpeningKind.Door && s.BoxWalls,
                DoorShape = door.Shape, DoorLook = door.Look, DoorHinge = door.Hinge,
                DoorWidthMm = door.WidthMm, DoorHeightMm = door.HeightMm,
                DoorOffsetMm = Math.Round(dw.Along - length / 2, 2),
            };
        }
        var windows = openings.Where(o => o.O.Kind == OpeningKind.Window).ToList();
        if (windows.Count > 0)
        {
            var w = windows[0].O;
            spec = spec with
            {
                Windows = windows.Count, WindowAtMm = [.. windows.Select(x => Math.Round(x.Along - length / 2, 2))],
                WindowShape = w.Shape, WindowWidthMm = w.WidthMm, WindowHeightMm = w.HeightMm, SillMm = w.SillMm, Bars = w.Bars,
                WindowFill = s.BoxWalls ? w.Fill : spec.WindowFill,
                FrameMm = w.Shape == OpeningShape.ArrowSlit ? 0 : spec.FrameMm,
            };
        }
        return spec;
    }

    /// <summary>
    /// A floor's runs with the doors and windows on each (and how far along it each is, mm), cut at grid
    /// points with a straight column where two doors (or two kinds of window) share a run, or where a
    /// run is too long for the bed.
    /// </summary>
    public static (List<(Run Run, List<(Opening O, double Along)> Openings)> Runs, Dictionary<GridPt, List<GridPt>> Joints) Split(Level level, PlanShape plan, BuildingSpec s, double sq, int k, double longestMm, List<Finding> findings)
    {
        var result = new List<(Run, List<(Opening, double)>)>();
        var joints = new Dictionary<GridPt, List<GridPt>>();
        var placed = new HashSet<Opening>();
        string where = $"on floor {k + 1}";
        foreach (var run0 in plan.Runs)
        {
            // Its front faces out of the building: the outside on its right (A → B as drawn), if one side is outside.
            var (left, right) = plan.Sides(run0.Points[0], run0.Points[1]);
            var run = right != -1 && left == -1 ? run0.Reversed : run0;
            double stepMm = sq * (run.Diagonal ? Math.Sqrt(2) : 1);
            var on = new List<(Opening O, double Along)>();
            foreach (var o in level.Openings)
            {
                if (placed.Contains(o) || Along(run, o.X, o.Y) is not { } t) continue;
                placed.Add(o);
                on.Add((o, t * stepMm));
            }
            on.Sort((a, b) => a.Along.CompareTo(b.Along));
            double margin = s.ColumnMm / 2 + 2;
            // Where it's cut: between openings that can't share a wall, and to keep each wall short enough.
            var cuts = new List<int>();
            int Nearest(double from, double to, double want)
            {
                int best = -1;
                for (int i = 1; i < run.Steps; i++)
                {
                    double x = i * stepMm;
                    if (x <= from || x >= to) continue;
                    if (on.Any(o => Math.Abs(o.Along - x) < o.O.WidthMm / 2 + margin + 1.5)) continue;
                    if (best < 0 || Math.Abs(x - want) < Math.Abs(best * stepMm - want)) best = i;
                }
                return best;
            }
            var kept = new List<(Opening O, double Along)>();
            var group = new List<(Opening O, double Along)>();
            double groupStart = 0;
            foreach (var o in on)
            {
                bool clash = o.O.Kind == OpeningKind.Window
                    ? group.Any(g => g.O.Kind == OpeningKind.Window && g.O.WindowStyle != o.O.WindowStyle)
                    : group.Any(g => g.O.Kind != OpeningKind.Window);
                if (clash)
                {
                    var prev = group[^1];
                    double from = prev.Along + prev.O.WidthMm / 2 + margin, to = o.Along - o.O.WidthMm / 2 - margin;
                    // The door, or the other kind of window, must have a wall to itself: cut after the last one that clashes.
                    var last = group.Last(g => o.O.Kind == OpeningKind.Window ? g.O.Kind == OpeningKind.Window && g.O.WindowStyle != o.O.WindowStyle : g.O.Kind != OpeningKind.Window);
                    from = Math.Max(from, last.Along + last.O.WidthMm / 2 + margin);
                    int at = Nearest(Math.Max(from, groupStart), to, (from + to) / 2);
                    if (at < 0)
                    {
                        findings.Add(new(Severity.Warn, $"A {Name(o.O)} {where} is too near the {Name(last.O)} beside it to have a stretch of wall of its own: it's left out (move it a square along)."));
                        continue;
                    }
                    cuts.Add(at);
                    groupStart = at * stepMm;
                    group = [.. group.Where(g => g.Along > groupStart)];
                }
                group.Add(o);
                kept.Add(o);
            }
            on = kept;
            // Too long for the bed: cut where the walls would be the longest.
            var bounds = new List<int> { 0 };
            bounds.AddRange(cuts.Order());
            bounds.Add(run.Steps);
            var more = new List<int>();
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                int a = bounds[i], b = bounds[i + 1];
                double panel = (b - a) * stepMm - s.ColumnMm;
                int pieces = (int)Math.Ceiling(panel / Math.Max(20, longestMm));
                for (int j = 1; j < pieces; j++)
                {
                    double want = (a + (b - a) * j / (double)pieces) * stepMm;
                    int at = Nearest(a * stepMm + s.ColumnMm, b * stepMm - s.ColumnMm, want);
                    if (at > a && at < b) more.Add(at);
                }
            }
            var all = cuts.Concat(more).Distinct().Order().ToList();
            if (more.Count > 0) findings.Add(new(Severity.Note, $"A wall {where} is longer than the bed: it's cut in {all.Count + 1} with a straight column between each."));
            int from0 = 0;
            foreach (var c in all.Append(run.Steps))
            {
                var part = new Run(run.Points.GetRange(from0, c - from0 + 1));
                double off = from0 * stepMm;
                result.Add((part, [.. on.Where(o => o.Along > off && o.Along < c * stepMm).Select(o => (o.O, o.Along - off))]));
                if (c < run.Steps) joints[run.Points[c]] = [run.Step, new GridPt(-run.Step.X, -run.Step.Y)];
                from0 = c;
            }
        }
        int lost = level.Openings.Count(o => !placed.Contains(o));
        if (lost > 0) findings.Add(new(Severity.Warn, $"{lost} door{(lost == 1 ? "" : "s")} or window{(lost == 1 ? "" : "s")} {where} {(lost == 1 ? "isn't" : "aren't")} on a wall (or {(lost == 1 ? "is" : "are")} at a corner): {(lost == 1 ? "it's" : "they're")} left out."));
        return (result, joints);
    }

    static string Name(Opening o) => o.Kind switch { OpeningKind.Door => "door", OpeningKind.Doorway => "doorway", _ => "window" };

    /// <summary>How far along a run (in steps) a point on it is, or null when it isn't on the run (or sits on a column).</summary>
    public static double? Along(Run r, double x, double y)
    {
        double dx = r.B.X - r.A.X, dy = r.B.Y - r.A.Y, len2 = dx * dx + dy * dy;
        double t = ((x - r.A.X) * dx + (y - r.A.Y) * dy) / len2;
        double px = r.A.X + t * dx - x, py = r.A.Y + t * dy - y;
        if (px * px + py * py > 0.05 * 0.05) return null;
        double steps = t * r.Steps;
        if (steps < 0.2 || steps > r.Steps - 0.2) return null;
        return steps;
    }

    /// <summary>
    /// The area a floor's plate covers (plan mm): the rooms under it and on it, and the floor laid by hand,
    /// grown to cover the columns and walls round them.
    /// </summary>
    public static PathsD PlateArea(HouseDesign d, List<PlanShape> plans, int k, double sq, double columnMm)
    {
        var area = new PathsD();
        foreach (var r in plans[k - 1].Rooms) area.AddRange(r.Area);
        foreach (var r in plans[k].Rooms) area.AddRange(r.Area);
        foreach (var f in d.Levels[k].Floor) area.Add(Geo.Rect(f.X, f.Y, f.X + 1, f.Y + 1));
        if (area.Count == 0) return [];
        var mm = Geo.Map(Geo.Union(area), p => new PointD(p.x * sq, p.y * sq));
        return Geo.Union(Clipper.InflatePaths(mm, columnMm / 2, JoinType.Miter, EndType.Polygon, 4, 3));
    }

    static bool Inside(PathsD area, double x, double y) =>
        area.Count > 0 && Clipper.PointInPolygon(new PointD(x, y), area[0]) != PointInPolygonResult.IsOutside
        || area.Skip(1).Any(p => Clipper.PointInPolygon(new PointD(x, y), p) != PointInPolygonResult.IsOutside && Clipper.IsPositive(p) == Clipper.IsPositive(area[0]));

    /// <summary>
    /// A floor between storeys: one plate (cut along the walls under it when it's bigger than the bed),
    /// lying on the walls and columns below; the texture etched where it shows, and lines scored where the
    /// walls above stand, so they go back in the right place.
    /// </summary>
    static void Floor(HouseDesign d, BuildingSpec s, List<PlanShape> plans, PathsD area, int k, double sq, double longestMm, Func<double, double, double, V3> world,
        PathsD slots, List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        if (area.Count == 0)
        {
            if (d.Levels[k].Walls.Count > 0) findings.Add(new(Severity.Bad, $"Floor {k + 1} has walls but no floor under them: close in a room under it or lay some floor."));
            return;
        }
        double T = s.WallSheetMm, Wt = BuildingKitMaker.WallThickness(s), C = s.ColumnMm;
        // Where the walls and columns above stand on it.
        var foot = new PathsD();
        foreach (var e in d.Levels[k].Walls)
        {
            var a = new PointD(e.A.X * sq, e.A.Y * sq);
            var b = new PointD(e.B.X * sq, e.B.Y * sq);
            foot.Add(Textures.Strip(a, b, Wt));
        }
        foreach (var (g, _) in plans[k].Joints) foot.Add(Geo.Rect(g.X * sq - C / 2, g.Y * sq - C / 2, g.X * sq + C / 2, g.Y * sq + C / 2));
        foot = Geo.Union(foot);
        var shows = Geo.Subtract(area, foot);
        // Rooms with a floor of their own, then the building's floor everywhere else.
        var etched = new PathsD();
        var lines = new List<PathD>();
        var own = new PathsD();
        foreach (var room in plans[k].Rooms)
        {
            if (RoomTexture(d.Levels[k], plans[k], room) is not { } tex) continue;
            var roomMm = Geo.Map(room.Area, p => new PointD(p.x * sq, p.y * sq));
            var region = Geo.Intersect(roomMm, shows);
            var (e1, l1) = FloorTexture(s, tex, region, Geo.Bounds(roomMm), sq, s.Seed + 31 * k + room.Index);
            etched.AddRange(e1);
            lines.AddRange(l1);
            own.AddRange(region);
        }
        var (e0, l0) = Texture(s, own.Count > 0 ? Geo.Subtract(shows, Geo.Union(own)) : shows, k);
        etched.AddRange(e0);
        lines.AddRange(l0);
        lines.AddRange(Geo.ClipLines(foot.Select(Geo.Loop), area));
        if (Geo.Area(Geo.Subtract(foot, area)) > 1)
            findings.Add(new(Severity.Warn, $"Some of floor {k + 1}'s walls stand past the edge of its floor: lay floor under them."));

        // Cut along the walls under it, where it's too big for the bed.
        var cuts = SplitPlate(area, plans[k - 1], sq, longestMm, out bool unsupported);
        if (cuts.Count > 1)
            findings.Add(new(Severity.Note, $"Floor {k + 1} is bigger than the bed: it's cut in {cuts.Count} pieces whose edges meet on top of the walls{(unsupported ? " (one join has no wall under it: glue a strip of sheet under that join)" : "")}."));
        int n = 0;
        foreach (var whole in cuts)
        {
            // Slots for the railings' tabs.
            var cut = slots.Count > 0 ? Geo.Subtract(whole, slots) : whole;
            string name = cuts.Count == 1 ? $"Floor {k + 1}" : $"Floor {k + 1}, piece {++n}";
            var e2 = Geo.Intersect(etched, cut);
            var l2 = Geo.ClipLines(lines, cut);
            // Turned over onto the plate's own (x, −y) so its etched face is up (Pose.Up: u along X, v away from you).
            PointD Flip(PointD p) => new(p.x, -p.y);
            var cutF = Geo.Map(cut, Flip);
            var etchF = Geo.Map(e2, Flip);
            var linesF = Geo.Map(l2, Flip);
            pieces.Add(new Piece(name, Sheet.Walls, Geo.Tidy(cutF), etchF, linesF, 1));
            var o = world(0, 0, BuildingKitMaker.StoreyBase(s, k) - T / 2);
            solids.Add(new HouseSolid(name, Geo.Tidy(cutF), Pose.Up(o.X, o.Y, o.Z), T, Nil(etchF), Nil(linesF), k, SolidKind.Floor));
        }
    }

    /// <summary>The plate in pieces that each fit the bed, cut along grid lines that have walls under them when it can.</summary>
    static List<PathsD> SplitPlate(PathsD area, PlanShape below, double sq, double longest, out bool unsupported)
    {
        unsupported = false;
        var done = new List<PathsD>();
        var todo = new Queue<PathsD>();
        todo.Enqueue(area);
        bool any = false;
        int guard = 0;
        while (todo.Count > 0 && guard++ < 64)
        {
            var piece = todo.Dequeue();
            var b = Geo.Bounds(piece);
            double w = b.right - b.left, h = b.bottom - b.top;
            bool fits = Math.Max(w, h) <= longest && Math.Min(w, h) <= longest;
            if (fits) { done.Add(piece); continue; }
            bool across = w >= h;   // cut with an upright line (x = …) when it's wider than deep
            double lo = across ? b.left : b.top, hi = across ? b.right : b.bottom, mid = (lo + hi) / 2;
            // Grid lines with a wall under them, nearest the middle first.
            var lines = new List<(double At, bool Wall)>();
            for (int g = (int)Math.Ceiling(lo / sq); g * sq < hi; g++)
            {
                double at = g * sq;
                if (at - lo < 20 || hi - at < 20) continue;
                bool wall = below.Runs.Any(r => !r.Diagonal && (across ? r.A.X == g && r.B.X == g : r.A.Y == g && r.B.Y == g));
                lines.Add((at, wall));
            }
            if (lines.Count == 0) { done.Add(piece); continue; }
            var pick = lines.Where(l => l.Wall).OrderBy(l => Math.Abs(l.At - mid)).Select(l => (double?)l.At).FirstOrDefault();
            if (pick is null || Math.Abs(pick.Value - mid) > (hi - lo) / 2 - Math.Max(1, (hi - lo) - longest) / 2)
            {
                if (pick is null || Math.Max(pick.Value - lo, hi - pick.Value) > longest)
                {
                    pick = lines.OrderBy(l => Math.Abs(l.At - mid)).First().At;
                    unsupported |= !lines.First(l => l.At == pick).Wall;
                }
            }
            double c = pick!.Value;
            var big = 1e5;
            var first = Geo.Intersect(piece, [across ? Geo.Rect(-big, -big, c, big) : Geo.Rect(-big, -big, big, c)]);
            var second = Geo.Intersect(piece, [across ? Geo.Rect(c, -big, big, big) : Geo.Rect(-big, c, big, big)]);
            foreach (var part in new[] { first, second })
                foreach (var island in Islands(part)) todo.Enqueue(island);
            any = true;
        }
        while (todo.Count > 0) done.Add(todo.Dequeue());
        _ = any;
        return done;
    }

    /// <summary>A shape's separate pieces, each with its own holes.</summary>
    static List<PathsD> Islands(PathsD a)
    {
        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, a, null, tree, FillRule.EvenOdd);
        var list = new List<PathsD>();
        void Walk(PolyPathD node)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var child = node[i];
                if (!child.IsHole)
                {
                    var p = new PathsD { child.Polygon! };
                    for (int j = 0; j < child.Count; j++)
                    {
                        p.Add(child[j].Polygon!);
                        Walk(child[j]);
                    }
                    if (Math.Abs(Clipper.Area(p)) > 1) list.Add(p);
                }
            }
        }
        Walk(tree);
        return list;
    }

    /// <summary>The floor's texture over an area: planks, deck plates for metal furniture, or stone.</summary>
    static (PathsD Etched, List<PathD> Lines) Texture(BuildingSpec s, PathsD region, int k)
    {
        if (region.Count == 0) return ([], []);
        double k28 = K(s);
        var (name, a, b) = s.Detail switch
        {
            WoodDetail.None => (Textures.Plain, 0.0, 0.0),
            WoodDetail.Metal => (Terrain.ThemeTextures.SciFiPanels, 16.0, 12.0),
            WoodDetail.Stone => ("Stone blocks", 12.0, 6.0),
            _ => ("Wood planks (across)", 5.0, 35.0),
        };
        if (name == Textures.Plain || Textures.Preset(name) is null) return ([], []);
        var bounds = Geo.Bounds(region);
        double w = bounds.right - bounds.left, h = bounds.bottom - bounds.top;
        if (w <= 0 || h <= 0) return ([], []);
        var ws = new WallSpec { Texture = name, SizeA = Math.Round(a * k28, 2), SizeB = Math.Round(b * k28, 1), GrooveMm = 0.5, Seed = s.Seed + 101 + k, Etch = s.Etch, Connectors = [] };
        var tex = Textures.Make(ws, new Face(w, h, 0, w, h));
        PointD Map(PointD p) => new(bounds.left + p.x, bounds.top + p.y);
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-1, -1, w + 1, h + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Tidy(Geo.Map(Geo.Union(mortar, tex.Etched), Map)), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(Geo.Map(tex.Raised.Select(Geo.Loop), Map), region));
            lines.AddRange(Geo.ClipLines(Geo.Map(tex.Etched.Select(Geo.Loop), Map), region));
        }
        lines.AddRange(Geo.ClipLines(Geo.Map(tex.Lines, Map), region));
        return (etched, lines);
    }

    /// <summary>The floor tile maker's tiles under the ground floor, as big as its walls' outside.</summary>
    static void Tiles(HouseDesign d, BuildingSpec s, double sq, double ox, double oy, List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        if (!s.FloorTiles || d.Levels.Count == 0 || d.Levels[0].Walls.Count == 0) return;
        var pts = d.Levels[0].Walls.SelectMany(e => new[] { e.A, e.B }).ToList();
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        var spec = BuildingKitMaker.TileSpecFor(s with { WidthMm = (x1 - x0) * sq, DepthMm = (y1 - y0) * sq });
        var kit = TileKitMaker.Make(spec);
        foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
            findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Floor tiles: {f.Text}"));
        if (!kit.Ok) return;
        foreach (var p in kit.Parts)
            pieces.Add(new Piece(p.Name == TileKitMaker.ClipName ? "Floor tile clip" : $"Floor tile {p.Name}", Sheet.Walls, p.Cut, p.Etched, p.Lines, p.Copies));
        // Centred under the ground floor's walls.
        double cx = ((x0 + x1) / 2 - ox) * sq, cz = ((y0 + y1) / 2 - oy) * sq;
        double bx = cx - spec.BoardAcross * spec.SquareMm / 2, bz = cz + spec.BoardDown * spec.SquareMm / 2;
        foreach (var spot in kit.Board)
            solids.Add(new HouseSolid("Floor tile", spot.Tile.Cut, Pose.Up(bx + spot.X, s.WallSheetMm / 2, bz - spot.Y), s.WallSheetMm, Nil(spot.Tile.Etched), Nil(spot.Tile.Lines), 0, SolidKind.Ground));
        findings.Add(new(Severity.Note, $"The building stands on {kit.Board.Count} floor tiles ({spec.BoardAcross:0.#} × {spec.BoardDown:0.#} squares of {spec.SquareMm:0.#} mm) that lock together with jigsaw edges."));
    }

    static V3 Unit(V3 v) => v * (1 / Math.Max(1e-9, v.Length));

    /// <summary>A piece of a box wall (placed in the wall's own frame: x along it, y up, z out of its front) where the wall stands.</summary>
    static Pose InWall(Pose wall, Walls.Model.Pose local)
    {
        V3 Dir(Walls.Model.V3 d) => wall.U * d.X + wall.V * d.Y + wall.N * d.Z;
        return new Pose(wall.O + Dir(local.O), Dir(local.U), Dir(local.V));
    }

    static Pose Building(Walls.Model.Pose p) => new(new V3(p.O.X, p.O.Y, p.O.Z), new V3(p.U.X, p.U.Y, p.U.Z), new V3(p.V.X, p.V.Y, p.V.Z));

    static PathsD? Nil(PathsD p) => p.Count > 0 ? p : null;
    static List<PathD>? Nil(List<PathD> p) => p.Count > 0 ? p : null;
}
