using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Finding = PhotonCanvas.Addon.Buildings.Finding;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using RoofsNs = PhotonCanvas.Addon.Roofs;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>A straight stretch of railing on the plan (squares), and how far it's cut back (+) or carried on (−) at each end (mm).</summary>
public sealed record RailingRun(GridPt A, GridPt B, double TrimA, double TrimB)
{
    public GridPt Step => new(Math.Sign(B.X - A.X), Math.Sign(B.Y - A.Y));
    public int Steps => Math.Max(Math.Abs(B.X - A.X), Math.Abs(B.Y - A.Y));
    public bool Diagonal => Step.X != 0 && Step.Y != 0;
}

/// <summary>The roofs, the railings round open floor and the rooms' own floors.</summary>
public static partial class HouseKitMaker
{
    /// <summary>A railing's height (mm) for 28 mm figures: about a metre.</summary>
    public const double RailingMm = 15;

    // ---- each floor's floor, and its yard ----

    /// <summary>
    /// A floor's floor on the plan (squares): over the rooms below it (none for the ground) and its own, and
    /// the floor laid by hand; kept within <see cref="Level.Yard"/> squares of its own rooms and walls when
    /// it has a yard set. The ground floor with a yard set reaches that far all round, laid or not.
    /// </summary>
    public static PathsD FloorArea(Level level, PlanShape? below, PlanShape plan)
    {
        var area = new PathsD();
        if (below is not null) foreach (var r in below.Rooms) area.AddRange(r.Area);
        foreach (var r in plan.Rooms) area.AddRange(r.Area);
        foreach (var f in level.Floor) area.Add(Geo.Rect(f.X, f.Y, f.X + 1, f.Y + 1));
        if (level.Yard is not int yard) return area.Count == 0 ? [] : Geo.Union(area);
        var own = OwnArea(level, plan);
        if (own.Count == 0) return area.Count == 0 ? [] : Geo.Union(area);
        var reach = Reach(own, Math.Max(0, yard));
        if (below is null) return reach;
        return area.Count == 0 ? [] : Geo.Intersect(Geo.Union(area), reach);
    }

    /// <summary>What a floor stands on itself (squares): its rooms, and a hairline along each of its walls.</summary>
    static PathsD OwnArea(Level level, PlanShape plan)
    {
        var own = new PathsD();
        foreach (var r in plan.Rooms) own.AddRange(r.Area);
        foreach (var e in level.Walls) own.Add(Textures.Strip(new PointD(e.A.X, e.A.Y), new PointD(e.B.X, e.B.Y), 0.002));
        return own.Count == 0 ? [] : Geo.Union(own);
    }

    /// <summary><paramref name="own"/> and every whole square within <paramref name="yard"/> squares of it (across or corner to corner).</summary>
    static PathsD Reach(PathsD own, int yard)
    {
        if (yard <= 0) return own;
        var grown = Clipper.InflatePaths(own, yard - 0.25, JoinType.Miter, EndType.Polygon, 4, 3);
        static bool In(PathsD a, PointD p) => a.Count(path => Clipper.PointInPolygon(p, path) == PointInPolygonResult.IsInside) % 2 == 1;
        var b = Geo.Bounds(grown);
        var cells = new PathsD(own);
        for (int x = (int)Math.Floor(b.left); x < Math.Ceiling(b.right); x++)
            for (int y = (int)Math.Floor(b.top); y < Math.Ceiling(b.bottom); y++)
                if (In(grown, new PointD(x + 0.5, y + 0.5))) cells.Add(Geo.Rect(x, y, x + 1, y + 1));
        return Geo.Union(cells);
    }

    /// <summary>The ground floor's yard (mm) for floor tiles: the building's own, or the floor's when it has one set.</summary>
    static double TileYardMm(HouseDesign d, BuildingSpec s, double sq) => d.Levels.Count > 0 && d.Levels[0].Yard is int y ? Math.Max(0, y) * sq : s.YardMm;

    // ---- railings ----

    /// <summary>
    /// The railings on floor <paramref name="k"/>: every straight stretch of its floor's edge (the rooms
    /// below and on it, and floor laid by hand) that has no wall of this floor standing on it — round a
    /// balcony or a terrace on the roof of the rooms below. Each stops at a column and meets the next at a
    /// corner (the one running left to right carries on past the corner, the other stops against it).
    /// </summary>
    public static List<RailingRun> RailingRuns(Level level, PlanShape below, PlanShape plan, double sheetMm, double columnMm)
    {
        if (!level.Railings) return [];
        var area = FloorArea(level, below, plan);
        if (area.Count == 0) return [];
        var walls = level.Walls.ToHashSet();
        // Stretches whose railing was taken away count as walled: the railing stops there.
        walls.UnionWith(level.NoRailing);
        var joints = plan.Joints;
        var runs = new List<(GridPt A, GridPt B)>();
        foreach (var path in Geo.Union(area))
        {
            var mine = new List<(GridPt A, GridPt B)>();
            (GridPt A, GridPt B)? cur = null;
            for (int i = 0; i < path.Count; i++)
            {
                var a = new GridPt((int)Math.Round(path[i].x), (int)Math.Round(path[i].y));
                var b = new GridPt((int)Math.Round(path[(i + 1) % path.Count].x), (int)Math.Round(path[(i + 1) % path.Count].y));
                int ax = Math.Abs(b.X - a.X), ay = Math.Abs(b.Y - a.Y), n = Math.Max(ax, ay);
                if (n == 0 || ax != 0 && ay != 0 && ax != ay) continue;
                var step = new GridPt(Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y));
                for (int j = 0; j < n; j++)
                {
                    var p = a + new GridPt(step.X * j, step.Y * j);
                    var q = p + step;
                    if (walls.Contains(WallEdge.Of(p, q)))
                    {
                        if (cur is { } c0) mine.Add(c0);
                        cur = null;
                        continue;
                    }
                    if (cur is { } c && c.B == p && new GridPt(Math.Sign(c.B.X - c.A.X), Math.Sign(c.B.Y - c.A.Y)) == step && !joints.ContainsKey(p)) cur = (c.A, q);
                    else
                    {
                        if (cur is { } c1) mine.Add(c1);
                        cur = (p, q);
                    }
                }
            }
            if (cur is { } last) mine.Add(last);
            // The outline is a loop: the last stretch may carry straight on into the first.
            if (mine.Count > 1 && mine[^1].B == mine[0].A && !joints.ContainsKey(mine[0].A)
                && Dir(mine[^1]) == Dir(mine[0]))
            {
                mine[0] = (mine[^1].A, mine[0].B);
                mine.RemoveAt(mine.Count - 1);
            }
            runs.AddRange(mine);
        }

        static GridPt Dir((GridPt A, GridPt B) r) => new(Math.Sign(r.B.X - r.A.X), Math.Sign(r.B.Y - r.A.Y));
        double Trim((GridPt A, GridPt B) run, GridPt end)
        {
            if (joints.ContainsKey(end)) return columnMm / 2 + 0.2;
            // A free end (the railing beside it was taken away): it stops on the grid point.
            if (!runs.Any(o => o != run && (o.A == end || o.B == end) && Dir(o) != Dir(run) && Dir(o) != new GridPt(-Dir(run).X, -Dir(run).Y))) return 0;
            // Meeting another railing at a corner.
            bool across = run.A.Y == run.B.Y;
            return across ? -sheetMm / 2 : sheetMm / 2;
        }
        return [.. runs.Select(r => new RailingRun(r.A, r.B, Trim(r, r.A), Trim(r, r.B)))];
    }

    /// <summary>
    /// A railing panel <paramref name="length"/> long (x along it, y up from the floor): a top and bottom
    /// rail with balusters between, posts at its ends, and tabs under it into slots in the floor.
    /// </summary>
    public static (PathsD Cut, List<double> Tabs, double TabWidth) RailingPanel(double length, BuildingSpec s)
    {
        double k28 = K(s), T = s.WallSheetMm;
        double h = Math.Round(RailingMm * k28, 1), top = Math.Max(2, 2.5 * k28), bottom = Math.Max(1.6, 2 * k28);
        double bar = Math.Max(1.5, 1.5 * k28), gap = Math.Max(2, 3 * k28), post = Math.Max(bar, 2.5 * k28);
        var cut = new PathsD { Geo.Rect(0, 0, length, h) };
        double tw = Math.Clamp(length * 0.15, 4, 8);
        var tabs = length < 3.2 * tw ? new List<double> { length / 2 } : [Math.Max(post + tw / 2, length * 0.2), Math.Min(length - post - tw / 2, length * 0.8)];
        foreach (var t in tabs) cut.Add(Geo.Rect(t - tw / 2, -T, t + tw / 2, 0.01));
        cut = Geo.Union(cut);
        double span = length - 2 * post, inner = h - top - bottom;
        int n = (int)Math.Floor((span + bar) / (gap + bar));
        if (n >= 1 && inner >= 2)
        {
            double g = (span - (n - 1) * bar) / n;
            var holes = new PathsD();
            for (int i = 0; i < n; i++)
            {
                double x = post + i * (g + bar);
                holes.Add(Geo.Rect(x, bottom, x + g, h - top));
            }
            cut = Geo.Subtract(cut, holes);
        }
        return (Geo.Tidy(cut), tabs, tw);
    }

    /// <summary>The railings on floor <paramref name="k"/>, standing on its floor with tabs in slots: returns the slots (plan mm) to cut in the floor.</summary>
    static PathsD Railings(HouseDesign d, BuildingSpec s, List<PlanShape> plans, int k, double sq, Func<double, double, double, V3> world,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        var slots = new PathsD();
        double T = s.WallSheetMm, fit = Math.Max(0, s.FitMm);
        var runs = RailingRuns(d.Levels[k], plans[k - 1], plans[k], T, s.ColumnMm);
        if (runs.Count == 0) return slots;
        var counts = new SortedDictionary<double, (PathsD Cut, int Count)>();
        double y0 = BuildingKitMaker.StoreyBase(s, k);
        foreach (var r in runs)
        {
            double unit = sq * (r.Diagonal ? Math.Sqrt(2) : 1);
            double L = Math.Round(r.Steps * unit - r.TrimA - r.TrimB, 1);
            if (L < 8) { findings.Add(new(Severity.Warn, $"Floor {k + 1}: a railing between columns is too short to make ({L:0.#} mm), so it's left out.")); continue; }
            var (cut, tabs, tw) = RailingPanel(L, s);
            if (counts.TryGetValue(L, out var c)) counts[L] = (c.Cut, c.Count + 1);
            else counts[L] = (cut, 1);
            // Along the plan (mm) from its start, cut back (or carried on) at the end.
            double ux = r.Step.X / (r.Diagonal ? Math.Sqrt(2) : 1), uy = r.Step.Y / (r.Diagonal ? Math.Sqrt(2) : 1);
            double sx = r.A.X * sq + ux * r.TrimA, sy = r.A.Y * sq + uy * r.TrimA;
            foreach (var t in tabs)
            {
                var c0 = new PointD(sx + ux * (t - (tw + fit) / 2), sy + uy * (t - (tw + fit) / 2));
                var c1 = new PointD(sx + ux * (t + (tw + fit) / 2), sy + uy * (t + (tw + fit) / 2));
                slots.Add(Textures.Strip(c0, c1, T + fit));
            }
            var o = world(sx / sq, sy / sq, y0);
            solids.Add(new HouseSolid("Railing", cut, new Pose(o, new V3(ux, 0, uy), new V3(0, 1, 0)), T, null, null, k, SolidKind.Railing));
        }
        foreach (var (L, (cut, count)) in counts)
            pieces.Add(new Piece($"Floor {k + 1} railing, {L:0.#} mm", Sheet.Walls, cut, [], [], count));
        if (counts.Count > 0)
            findings.Add(new(Severity.Note, $"Floor {k + 1} has railings round its open edges: their tabs go in slots in the floor."));
        return Geo.Union(slots);
    }

    // ---- the rooms' own floors ----

    /// <summary>The floor pattern picked for a room, if it has one of its own.</summary>
    public static string? RoomTexture(Level level, PlanShape plan, Room room) =>
        level.RoomFloors.FirstOrDefault(f => plan.RoomAt(f.X, f.Y) == room.Index)?.Texture;

    /// <summary>One of the floor tile maker's textures over an area, laid from the corner of <paramref name="from"/> (so it follows the grid).</summary>
    static (PathsD Etched, List<PathD> Lines) FloorTexture(HouseDesign d, BuildingSpec s, string name, PathsD region, RectD from, double sq, int seed)
    {
        if (region.Count == 0 || FloorTextures.Preset(name) is not { } preset || name == FloorTextures.Plain) return ([], []);
        double w = from.right - from.left, h = from.bottom - from.top;
        if (w <= 0 || h <= 0) return ([], []);
        var (a, b) = FloorTextures.SizesFor(preset, sq);
        var spec = new TileSpec { Texture = name, SquareMm = sq, SizeA = a, SizeB = b, GrooveMm = preset.UsesGroove ? preset.Groove : 0.5, Seed = seed, Etch = s.Etch, CracksPct = 0, Wear = Math.Clamp(d.Wear, 0, 1), DetailMm = Math.Max(0, d.DetailMm) };
        var tex = FloorTextures.Make(spec, w, h, seed);
        PointD Map(PointD p) => new(from.left + p.x, from.top + p.y);
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-sq, -sq, w + sq, h + sq)], Geo.Union(tex.Raised)) : [];
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

    /// <summary>A rectangle's top-left corner moved back onto the grid, so a pattern laid from it follows the squares.</summary>
    static RectD OnGrid(RectD b, double sq) => new(Math.Floor(b.left / sq) * sq, Math.Floor(b.top / sq) * sq, b.right, b.bottom);

    /// <summary>Whether the ground floor stands on one plate (rather than jigsaw tiles or nothing).</summary>
    static bool OnPlate(HouseDesign d, BuildingSpec s) => s.FloorTiles && d.Base == BaseKind.Plate;

    /// <summary>
    /// One plate the building stands on, shaped to the ground floor's rooms (and floor laid by hand) out
    /// to the columns' outside: the ground floor's pattern etched on it (each room's own where it has
    /// one), and lines scored where the walls and columns stand. Cut along walls if it's bigger than the bed.
    /// </summary>
    static void BasePlate(HouseDesign d, BuildingSpec s, PlanShape plan, double sq, double longestMm, Func<double, double, double, V3> world,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        if (!OnPlate(d, s) || d.Levels.Count == 0 || d.Levels[0].Walls.Count == 0) return;
        double T = s.WallSheetMm, Wt = BuildingKitMaker.WallThickness(s), C = s.ColumnMm;
        var level = d.Levels[0];
        var area = FloorArea(level, null, plan);
        area.AddRange(OwnArea(level, plan));
        var mm = Geo.Map(Geo.Union(area), p => new PointD(p.x * sq, p.y * sq));
        var plate = Geo.Tidy(Geo.Union(Clipper.InflatePaths(mm, C / 2 + 2, JoinType.Miter, EndType.Polygon, 4, 3)));
        var foot = new PathsD();
        foreach (var e in level.Walls) foot.Add(Textures.Strip(new PointD(e.A.X * sq, e.A.Y * sq), new PointD(e.B.X * sq, e.B.Y * sq), Wt));
        foreach (var (g, dirs) in plan.Joints) foot.Add(ColumnFoot(level, s, g, dirs, sq));
        foot = Geo.Union(foot);
        var shows = Geo.Subtract(plate, foot);
        var etched = new PathsD();
        var lines = new List<PathD>();
        var own = new PathsD();
        foreach (var room in plan.Rooms)
        {
            if (RoomTexture(level, plan, room) is not { } tex) continue;
            var roomMm = Geo.Map(room.Area, p => new PointD(p.x * sq, p.y * sq));
            var region = Geo.Intersect(roomMm, shows);
            var (e1, l1) = FloorTexture(d, s, tex, region, Geo.Bounds(roomMm), sq, s.Seed + 17 + room.Index);
            etched.AddRange(e1);
            lines.AddRange(l1);
            own.AddRange(region);
        }
        var (e0, l0) = FloorTexture(d, s, s.FloorTexture, own.Count > 0 ? Geo.Subtract(shows, Geo.Union(own)) : shows, OnGrid(Geo.Bounds(plate), sq), sq, s.Seed + 7);
        etched.AddRange(e0);
        lines.AddRange(l0);
        lines.AddRange(Geo.ClipLines(foot.Select(Geo.Loop), plate));
        var cuts = SplitPlate(plate, plan, sq, longestMm, out bool unsupported);
        findings.Add(new(Severity.Note, cuts.Count == 1
            ? "The building stands on one base plate, with lines scored where the walls and columns go."
            : $"The base is bigger than the bed: it's cut in {cuts.Count} pieces whose edges meet under walls{(unsupported ? " (one join has no wall over it: glue a strip of sheet under that join)" : "")}."));
        int n = 0;
        foreach (var cut in cuts)
        {
            string name = cuts.Count == 1 ? "Base" : $"Base, piece {++n}";
            PointD Flip(PointD p) => new(p.x, -p.y);
            var cutF = Geo.Tidy(Geo.Map(cut, Flip));
            var etchF = Geo.Map(Geo.Intersect(etched, cut), Flip);
            var linesF = Geo.Map(Geo.ClipLines(lines, cut), Flip);
            pieces.Add(new Piece(name, Sheet.Walls, cutF, etchF, linesF, 1));
            var o = world(0, 0, T / 2);
            solids.Add(new HouseSolid(name, cutF, Pose.Up(o.X, o.Y, o.Z), T, Nil(etchF), Nil(linesF), 0, SolidKind.Ground));
        }
    }

    /// <summary>A loose floor for each ground-floor room with a pattern of its own: it drops in between the walls and columns (on a base plate, it's etched on that instead).</summary>
    static void RoomFloors(HouseDesign d, BuildingSpec s, PlanShape plan, double sq, double longestMm, Func<double, double, double, V3> world,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        if (d.Levels.Count == 0 || OnPlate(d, s)) return;
        double T = s.WallSheetMm, Wt = BuildingKitMaker.WallThickness(s), C = s.ColumnMm, clear = 0.3;
        var columns = Geo.Offset(new PathsD(plan.Joints.Select(j => ColumnFoot(d.Levels[0], s, j.Key, j.Value, sq))), clear);
        foreach (var room in plan.Rooms)
        {
            if (RoomTexture(d.Levels[0], plan, room) is not { } tex) continue;
            var roomMm = Geo.Map(room.Area, p => new PointD(p.x * sq, p.y * sq));
            var plate = Geo.Tidy(Geo.Subtract(Clipper.InflatePaths(roomMm, -(Wt / 2 + clear), JoinType.Miter, EndType.Polygon, 4, 3), columns));
            if (Geo.Area(plate) < 50) continue;
            var (etched, lines) = FloorTexture(d, s, tex, plate, Geo.Bounds(roomMm), sq, s.Seed + 17 + room.Index);
            var b = Geo.Bounds(plate);
            if (Math.Max(b.right - b.left, b.bottom - b.top) > longestMm || Math.Min(b.right - b.left, b.bottom - b.top) > longestMm)
                findings.Add(new(Severity.Warn, $"Room {room.Index + 1}'s floor is bigger than the bed: cut it in two by hand, or leave it plain."));
            string name = $"Floor 1, room {room.Index + 1} ({tex.ToLowerInvariant()})";
            PointD Flip(PointD p) => new(p.x, -p.y);
            var cutF = Geo.Tidy(Geo.Map(plate, Flip));
            var etchF = Geo.Map(etched, Flip);
            var linesF = Geo.Map(lines, Flip);
            pieces.Add(new Piece(name, Sheet.Walls, cutF, etchF, linesF, 1));
            var o = world(0, 0, BuildingKitMaker.StoreyBase(s, 0) + T / 2);
            solids.Add(new HouseSolid(name, cutF, Pose.Up(o.X, o.Y, o.Z), T, Nil(etchF), Nil(linesF), 0, SolidKind.Floor));
        }
    }

    // ---- roofs ----

    /// <summary>Which way a roof runs on the plan: along its ridge, and down its slope towards the front (a lean-to's low side).</summary>
    public static ((int X, int Y) Along, (int X, int Y) Front) Directions(int turn) => (((turn % 4) + 4) % 4) switch
    {
        0 => ((1, 0), (0, 1)),
        1 => ((0, -1), (1, 0)),
        2 => ((-1, 0), (0, -1)),
        _ => ((0, 1), (-1, 0)),
    };

    /// <summary>Whether a roof's rectangle is exactly one room on its floor (its locator then fits inside the walls).</summary>
    public static bool OneRoom(RoofArea r, PlanShape plan)
    {
        int w = r.X1 - r.X0, h = r.Y1 - r.Y0;
        return plan.Rooms.Any(room =>
        {
            if (Math.Abs(room.AreaSquares - w * h) > 1e-6) return false;
            var b = Geo.Bounds(room.Area);
            return Math.Abs(b.left - r.X0) < 1e-6 && Math.Abs(b.top - r.Y0) < 1e-6 && Math.Abs(b.right - r.X1) < 1e-6 && Math.Abs(b.bottom - r.Y1) < 1e-6;
        });
    }

    /// <summary>The roof maker's settings for one of the roofs (null for a flat one).</summary>
    public static RoofsNs.RoofSpec? RoofSpecFor(HouseDesign d, RoofArea r, PlanShape plan, int index)
    {
        if (r.Kind == RoofKind.Flat) return null;
        var s = StyleFor(d);
        double k28 = K(s), sq = Math.Max(5, d.SquareMm);
        var wall = Topped(BuildingKitMaker.StoreyWall(s, Math.Clamp(r.Level, 0, MaxLevels - 1), 100), true);
        double lip = WallKitMaker.ColumnLip(wall);
        int w = r.X1 - r.X0, h = r.Y1 - r.Y0;
        double along = (r.RidgeAcross ? w : h) * sq + s.ColumnMm + 2 * lip, across = (r.RidgeAcross ? h : w) * sq + s.ColumnMm + 2 * lip;
        var shingle = RoofsNs.Shingles.Preset(r.Covering) ?? RoofsNs.Shingles.Preset("Slate")!;
        var spec = new RoofsNs.RoofSpec
        {
            Style = r.Kind switch { RoofKind.Hip => RoofsNs.RoofStyle.Hip, RoofKind.LeanTo => RoofsNs.RoofStyle.LeanTo, _ => RoofsNs.RoofStyle.Gable },
            PitchDeg = r.PitchDeg, EaveMm = r.EaveMm, GableOverhangMm = Math.Round(3 * k28, 1),
            Shingle = shingle.Name, ShingleA = Math.Round(shingle.SizeA * k28, 2), ShingleB = Math.Round(shingle.SizeB * k28, 2),
            ShingleGrooveMm = shingle.UsesGroove ? Math.Round(Math.Max(0.4, shingle.Groove * Math.Sqrt(k28)), 2) : shingle.Groove,
            Seed = s.Seed + 211 + index, Etch = s.Etch, TrussSpacingMm = Math.Round(60 * k28, 1),
            Window = r.Window, WindowMm = Math.Round(10 * k28, 1), PinMm = BuildingKitMaker.PinMm,
        };
        spec = RoofsNs.RoofMaker.FitToWalls(spec, wall) with { LengthMm = Math.Round(along, 2), WidthMm = Math.Round(across, 2) };
        // Over more than one room the locator would hit the walls inside: it has none.
        if (!OneRoom(r, plan)) spec = spec with { InsetMm = 0 };
        return spec;
    }

    /// <summary>
    /// Every roof: the roof maker's, sized to the outside of the column caps round its rectangle and resting
    /// on them, turned the way it runs; a flat roof is one plate with the same frame under it.
    /// </summary>
    static void Roofs(HouseDesign d, BuildingSpec s, List<PlanShape> plans, List<PathsD> plates, double sq, Func<double, double, double, V3> world,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        int levels = plans.Count, n = d.Roofs.Count;
        double T = s.WallSheetMm, C = s.ColumnMm;
        var made = new Dictionary<string, (List<RoofsNs.RoofPart> Parts, List<int> Numbers)>();
        for (int i = 0; i < n; i++)
        {
            var r = d.Roofs[i];
            string what = n == 1 ? "The roof" : $"Roof {i + 1}";
            if (r.Level < 0 || r.Level >= levels) { findings.Add(new(Severity.Warn, $"{what} is on a floor that isn't there, so it's left out.")); continue; }
            if (r.X1 - r.X0 < 1 || r.Y1 - r.Y0 < 1) continue;
            var wall = Topped(BuildingKitMaker.StoreyWall(s, r.Level, 100), true);
            double lip = WallKitMaker.ColumnLip(wall);
            double baseY = BuildingKitMaker.StoreyBase(s, r.Level) + WallKitMaker.ColumnTopMm(wall);
            var rectMm = new PathsD { Geo.Rect(r.X0 * sq, r.Y0 * sq, r.X1 * sq, r.Y1 * sq) };
            // (The floor above reaches half a column past its edge onto the walls: that much is no matter.)
            if (r.Level + 1 < levels && plates[r.Level + 1].Count > 0 && Geo.Area(Geo.Intersect(rectMm, Geo.Offset(plates[r.Level + 1], -(s.ColumnMm / 2 + 0.5)))) > sq * sq * 0.25)
                findings.Add(new(Severity.Warn, $"{what} is under floor {r.Level + 2}'s floor: put it on the floor above, or over rooms with nothing above them."));
            for (int j = 0; j < i; j++)
            {
                var o = d.Roofs[j];
                if (o.Level == r.Level && Math.Min(o.X1, r.X1) > Math.Max(o.X0, r.X0) && Math.Min(o.Y1, r.Y1) > Math.Max(o.Y0, r.Y0))
                    findings.Add(new(Severity.Warn, $"Roofs {j + 1} and {i + 1} overlap."));
            }
            bool oneRoom = OneRoom(r, plans[r.Level]);
            var (al, fr) = Directions(r.Turn);
            var A3 = new V3(al.X, 0, al.Y);
            var F3 = new V3(fr.X, 0, fr.Y);
            var centre = world((r.X0 + r.X1) / 2.0, (r.Y0 + r.Y1) / 2.0, baseY);

            if (r.Kind == RoofKind.Flat)
            {
                FlatRoof(d, s, r, i, n, sq, lip, oneRoom, centre, pieces, solids, findings);
                continue;
            }
            var spec = RoofSpecFor(d, r, plans[r.Level], i)!;
            var kit = RoofsNs.RoofMaker.Make(spec);
            foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
                findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"{what}: {f.Text}"));
            if (!kit.Ok) continue;
            if (!oneRoom) findings.Add(new(Severity.Note, $"{what} covers more than one room (or part of one), so it has no frame under it to keep it in place: glue it on, or let it rest on the column caps."));
            double L = spec.LengthMm, Tk = kit.ThicknessMm;
            // The roof's model (X along the ridge from 0, Y across from the middle, Z up) → the building.
            V3 Dir(RoofsNs.V3 v) => A3 * v.X - F3 * v.Y + new V3(0, v.Z, 0);
            V3 Point(RoofsNs.V3 p) => centre + A3 * (p.X - L / 2) - F3 * p.Y + new V3(0, p.Z, 0);
            foreach (var part in kit.Parts)
                foreach (var pose in part.Poses)
                {
                    var mid = pose.O + pose.N * (Tk / 2);
                    solids.Add(new HouseSolid("Roof", part.Cut, new Pose(Point(mid), Dir(pose.U), Dir(pose.V)), Tk, Nil(part.Etched), Nil(part.Lines), r.Level, SolidKind.Roof));
                }
            string key = spec.ToJson();
            if (made.TryGetValue(key, out var m)) m.Numbers.Add(i + 1);
            else made[key] = (kit.Parts, [i + 1]);
        }
        foreach (var (_, (parts, numbers)) in made)
        {
            string label = n == 1 ? "Roof" : numbers.Count == 1 ? $"Roof {numbers[0]}" : $"Roofs {string.Join(", ", numbers)}";
            foreach (var part in parts)
                pieces.Add(new Piece($"{label}: {part.Name.ToLowerInvariant()}", Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies * numbers.Count));
        }
        if (made.Count > 0) findings.Add(new(Severity.Note, "Each roof is glued together on its base and lifts off to show the rooms."));
    }

    /// <summary>A flat roof: a plate over the column caps with its covering etched on, and a frame under it inside the walls when it's over one room.</summary>
    static void FlatRoof(HouseDesign d, BuildingSpec s, RoofArea r, int i, int n, double sq, double lip, bool oneRoom, V3 centre,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        double T = s.WallSheetMm, C = s.ColumnMm, Wt = BuildingKitMaker.WallThickness(s);
        double w = (r.X1 - r.X0) * sq + C + 2 * lip, h = (r.Y1 - r.Y0) * sq + C + 2 * lip;
        string label = n == 1 ? "Roof" : $"Roof {i + 1}";
        var plate = new PathsD { Geo.Rect(-w / 2, -h / 2, w / 2, h / 2) };
        string tex = FloorTextures.Preset(r.Covering) is not null ? r.Covering : "Stone blocks";
        var (etched, lines) = FloorTexture(d, s, tex, plate, new RectD(-w / 2, -h / 2, w / 2, h / 2), sq, s.Seed + 211 + i);
        PointD Flip(PointD p) => new(p.x, -p.y);
        var etchF = Geo.Map(etched, Flip);
        var linesF = Geo.Map(lines, Flip);
        pieces.Add(new Piece($"{label} (flat)", Sheet.Walls, plate, etchF, linesF, 1));
        solids.Add(new HouseSolid("Roof", plate, Pose.Up(centre.X, centre.Y + T / 2, centre.Z), T, Nil(etchF), Nil(linesF), r.Level, SolidKind.Roof));
        if (!oneRoom)
        {
            findings.Add(new(Severity.Note, $"{label} is flat over more than one room: glue it on, or let it rest on the column caps."));
            return;
        }
        // The frame under it: inside the walls, clear of the columns, reaching below the walls' tops.
        double inset = C / 2 + lip + Wt / 2 + RoofsNs.RoofMaker.ClearMm, corner = C + 2 * lip + RoofsNs.RoofMaker.ClearMm;
        double fw = w / 2 - inset, fh = h / 2 - inset;
        if (fw < 6 || fh < 6) return;
        var frame = Geo.Subtract([Geo.Rect(-fw, -fh, fw, fh)],
            [Geo.Rect(-w, -h, -w / 2 + corner, -h / 2 + corner), Geo.Rect(w / 2 - corner, -h, w, -h / 2 + corner), Geo.Rect(w / 2 - corner, h / 2 - corner, w, h), Geo.Rect(-w, h / 2 - corner, -w / 2 + corner, h)]);
        double band = Math.Max(8, corner - inset + 5);
        if (fw - band >= 8 && fh - band >= 8) frame = Geo.Subtract(frame, [Geo.Rect(-fw + band, -fh + band, fw - band, fh - band)]);
        if (Geo.Pieces(frame) != 1) return;
        frame = Geo.Tidy(frame);
        var wall = Topped(BuildingKitMaker.StoreyWall(s, r.Level, 100), true);
        double rest = Math.Max(0, WallKitMaker.ColumnTopMm(wall) - wall.HeightMm);
        int layers = Math.Max(1, (int)Math.Ceiling((rest + RoofsNs.RoofMaker.GripMm) / T - 1e-6));
        pieces.Add(new Piece($"{label} (flat): frame underneath", Sheet.Walls, frame, [], [], layers));
        for (int j = 0; j < layers; j++)
            solids.Add(new HouseSolid("Roof", frame, Pose.Up(centre.X, centre.Y - T / 2 - j * T, centre.Z), T, null, null, r.Level, SolidKind.Roof));
    }
}
