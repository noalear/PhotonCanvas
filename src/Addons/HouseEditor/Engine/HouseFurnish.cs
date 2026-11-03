using System.Text.Json;
using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Stairs;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Finding = PhotonCanvas.Addon.Buildings.Finding;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Piece = PhotonCanvas.Addon.Buildings.Piece;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>A way up as it stands in the building: the opening it needs in the floor above, and the floor it takes up below (plan mm, y towards you).</summary>
public sealed record MadeWay(int Level, int Index, PathsD Hatch, PathsD Footprint);

/// <summary>The furniture on each floor and the stairs and ladders between them.</summary>
public static partial class HouseKitMaker
{
    /// <summary>The furniture maker's settings for the building's furniture.</summary>
    public static FurnitureSet SetFor(BuildingSpec s, List<FurnitureItem> items) => new()
    {
        Items = items, ThicknessMm = s.FurnitureSheetMm, FitMm = s.FurnitureFitMm, PushFit = s.FurniturePushFit, GripMm = s.FurnitureGripMm,
        Detail = s.Detail, Seed = s.Seed, ScaleMm = s.ScaleMm,
    };

    /// <summary>A new piece of furniture of a kind, sized for the building's figures.</summary>
    public static FurnitureItem NewItem(BuildingSpec s, ItemKind kind) => BuildingSpec.ScaledItem(Defaults.For(kind), Math.Max(0.2, s.ScaleMm / 28)) with { Count = 1 };

    /// <summary>What a piece of furniture is called.</summary>
    public static string ItemName(FurnitureItem item) => item.Name is { Length: > 0 } name ? name : Defaults.Info(item.Kind).Name;

    /// <summary>A ready-made piece from the furniture maker's catalog, sized for the building's figures.</summary>
    public static FurnitureItem FromCatalog(BuildingSpec s, CatalogEntry e) => FurnitureCatalog.Sized(e.Item, s.ScaleMm) with { Count = 1 };

    static readonly Dictionary<string, (double W, double D)> Sizes = [];

    /// <summary>
    /// How much floor a piece of furniture takes (mm): across its front and front to back, as made (not turned).
    /// Remembered, as the plan asks often.
    /// </summary>
    public static (double W, double D) Footprint(BuildingSpec s, FurnitureItem item)
    {
        string key = $"{s.FurnitureSheetMm}|{s.ScaleMm}|{JsonSerializer.Serialize(item)}";
        lock (Sizes)
            if (Sizes.TryGetValue(key, out var known)) return known;
        var kit = FurnitureKit.Make(SetFor(s, [item with { Count = 1 }]));
        (double, double) size = (item.WidthMm, item.DepthMm);
        if (kit.Items.Count > 0 && kit.Items[0].Ok)
        {
            var (lo, hi) = Interior.Box(kit.Items[0], kit.ThicknessMm, 0);
            size = (hi.X - lo.X, hi.Z - lo.Z);
        }
        lock (Sizes)
        {
            if (Sizes.Count > 500) Sizes.Clear();
            Sizes[key] = size;
        }
        return size;
    }

    /// <summary>The floor a placed piece takes on the plan (squares): a rectangle turned as it's turned.</summary>
    public static RectD ItemArea(BuildingSpec s, PlacedItem p, double sq)
    {
        var (w, d) = Footprint(s, p.Item);
        if (((p.Turn % 2) + 2) % 2 == 1) (w, d) = (d, w);
        return new RectD(p.X - w / 2 / sq, p.Y - d / 2 / sq, p.X + w / 2 / sq, p.Y + d / 2 / sq);
    }

    /// <summary>Where the floor's top is on floor <paramref name="k"/> at a plan point: a loose room floor on the ground floor lifts it a sheet.</summary>
    static double FloorTop(HouseDesign d, BuildingSpec s, PlanShape ground, int k, double x, double y)
    {
        double top = BuildingKitMaker.StoreyBase(s, k);
        if (k == 0 && !OnPlate(d, s) && ground.RoomAt(x, y) is >= 0 and var r && RoomTexture(d.Levels[0], ground, ground.Rooms[r]) is not null) top += s.WallSheetMm;
        return top;
    }

    /// <summary>
    /// The furniture placed on each floor, made by the furniture maker and stood where it was put, turned
    /// as asked. Pieces that come out the same are cut together. Warns of furniture standing in a wall,
    /// a column, a way up or another piece, or off the edge of its floor.
    /// </summary>
    static void Furnish(HouseDesign d, BuildingSpec s, List<PlanShape> plans, List<PathsD> plates, double sq, Func<double, double, double, V3> world,
        List<MadeWay> ways, List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        var all = new List<(int Level, PlacedItem Placed)>();
        for (int k = 0; k < plans.Count; k++)
            foreach (var p in d.Levels[k].Furniture) all.Add((k, p));
        if (all.Count == 0) return;
        // Each different piece made once.
        var keys = all.Select(a => JsonSerializer.Serialize(a.Placed.Item with { Count = 1 })).ToList();
        var distinct = keys.Distinct().ToList();
        var items = distinct.Select(k => JsonSerializer.Deserialize<FurnitureItem>(k)!).ToList();
        var kit = FurnitureKit.Make(SetFor(s, items));
        double T = kit.ThicknessMm;
        // Names: the kind's name, numbered where different pieces of the same kind are made.
        var names = new string[items.Count];
        foreach (var g in items.Select((it, i) => (it, i)).GroupBy(x => ItemName(x.it)))
        {
            int n = 0;
            foreach (var (_, i) in g) names[i] = g.Count() == 1 ? g.Key : $"{g.Key} {++n}";
        }
        var counts = new int[items.Count];
        foreach (var key in keys) counts[distinct.IndexOf(key)]++;
        for (int i = 0; i < items.Count && i < kit.Items.Count; i++)
        {
            var ik = kit.Items[i];
            foreach (var f in ik.Findings.Where(f => f.Level != Furniture.Severity.Note))
                findings.Add(new(Severity.Warn, f.Level == Furniture.Severity.Bad ? $"{names[i]} left out: {f.Text}" : $"{names[i]}: {f.Text}"));
            if (!ik.Ok) continue;
            foreach (var p in ik.Parts)
                pieces.Add(new Piece($"{names[i]}: {p.Name.ToLowerInvariant()}", Sheet.Furniture, p.Cut, p.Etched, p.Lines, p.Copies * counts[i]));
        }

        // Stood in place, and checked for room.
        var taken = new Dictionary<int, List<(string Name, PathsD Area)>>();
        var trouble = new List<string>();
        void Trouble(string text) { if (!trouble.Contains(text)) trouble.Add(text); }
        for (int a = 0; a < all.Count; a++)
        {
            var (k, placed) = all[a];
            int i = distinct.IndexOf(keys[a]);
            if (i >= kit.Items.Count || !kit.Items[i].Ok) continue;
            var ik = kit.Items[i];
            double turn = (((placed.Turn % 4) + 4) % 4) * 90;
            var (lo, hi) = Interior.Box(ik, T, turn);
            var mid = world(placed.X, placed.Y, FloorTop(d, s, plans[0], k, placed.X, placed.Y));
            // A hanging keeps its height up the wall; everything else stands on the floor.
            var spot = FurnitureCatalog.SpotFor(placed.Item);
            double dx = mid.X - (lo.X + hi.X) / 2, dy = mid.Y - (spot == FurnitureSpot.OnWall ? 0 : lo.Y), dz = mid.Z - (lo.Z + hi.Z) / 2;
            string where = plans.Count == 1 ? "" : $" on floor {k + 1}";
            string name = Lower(names[i]);
            foreach (var part in ik.Parts)
                foreach (var pose in part.Poses)
                    solids.Add(new HouseSolid(names[i], part.Cut, Interior.Move(pose, dx, dy, dz, turn), T, Nil(part.Etched), Nil(part.Lines), k, SolidKind.Furniture));

            // Its footprint on the plan (mm), shrunk a little so pieces may stand touching.
            var r = ItemArea(s, placed, sq);
            var foot = new PathsD { Geo.Rect(r.left * sq + 0.3, r.top * sq + 0.3, r.right * sq - 0.3, r.bottom * sq - 0.3) };
            var walls = WallFootprint(d.Levels[k], plans[k], s, sq);
            if (Geo.Area(Geo.Intersect(foot, walls)) > 0.5) Trouble($"The {name}{where} stands in a wall or a column: move it into the room.");
            var floor = k == 0 ? GroundArea(d, plans[0], s, sq) : plates[k];
            if (Geo.Area(Geo.Subtract(foot, floor)) > Geo.Area(foot) * 0.05) Trouble($"The {name}{where} stands off the edge of the floor.");
            foreach (var w in ways.Where(w => w.Level == k && Geo.Area(Geo.Intersect(foot, w.Footprint)) > 0.5)) Trouble($"The {name}{where} is in the way of the stairs.");
            foreach (var w in ways.Where(w => w.Level + 1 == k && Geo.Area(Geo.Intersect(foot, w.Hatch)) > 0.5)) Trouble($"The {name}{where} stands over the opening for the stairs.");
            // Rugs lie under other furniture, and hangings are up on the wall over it.
            if (spot is FurnitureSpot.Floor or FurnitureSpot.OnWall) continue;
            if (!taken.TryGetValue(k, out var here)) taken[k] = here = [];
            foreach (var (other, area) in here)
                if (Geo.Area(Geo.Intersect(foot, area)) > 0.5) Trouble($"The {name}{where} runs into the {Lower(other)}.");
            here.Add((names[i], foot));
        }
        foreach (var t in trouble) findings.Add(new(Severity.Warn, t));
        int total = keys.Count(key => distinct.IndexOf(key) is var j && j < kit.Items.Count && kit.Items[j].Ok);
        if (total > 0) findings.Add(new(Severity.Note, $"{total} piece{(total == 1 ? "" : "s")} of furniture, cut from the furniture's {T:0.##} mm sheet: glue each together and stand it where it shows."));
    }

    /// <summary>Where a floor's walls and columns stand (plan mm).</summary>
    static PathsD WallFootprint(Level level, PlanShape plan, BuildingSpec s, double sq)
    {
        double Wt = BuildingKitMaker.WallThickness(s);
        var foot = new PathsD();
        foreach (var e in level.Walls) foot.Add(Textures.Strip(new PointD(e.A.X * sq, e.A.Y * sq), new PointD(e.B.X * sq, e.B.Y * sq), Wt));
        foreach (var (g, dirs) in plan.Joints) foot.Add(ColumnFoot(level, s, g, dirs, sq));
        return Geo.Union(foot);
    }

    /// <summary>The ground floor's floor (plan mm): its rooms and the floor laid by hand.</summary>
    static PathsD GroundArea(HouseDesign d, PlanShape ground, BuildingSpec s, double sq)
    {
        var area = FloorArea(d.Levels[0], null, ground);
        if (area.Count == 0) return [];
        var mm = Geo.Map(area, p => new PointD(p.x * sq, p.y * sq));
        // Standing on tiles or a plate, it may reach under the walls' edge a little.
        return Geo.Union(Clipper.InflatePaths(mm, s.ColumnMm / 2, JoinType.Miter, EndType.Polygon, 4, 3));
    }

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    // ---- stairs and ladders ----

    /// <summary>The stairs maker's settings for a way up one storey of the building, in a big empty room (it's only the way up that's used).</summary>
    public static FloorSpec WaySpec(BuildingSpec s, WayKind kind)
    {
        double k28 = K(s);
        return new FloorSpec
        {
            WallHeightMm = BuildingKitMaker.StoreyHeight(s), WallSheetMm = s.WallSheetMm, TwoSidedWalls = false, WallThickMm = BuildingKitMaker.WallThickness(s),
            WallLengthMm = 400, WallsAlong = 1, WallWidthMm = 300, WallsAcross = 1, Columns = false,
            SheetMm = s.WallSheetMm, FitMm = s.FitMm, Floor = true, Edge = FloorEdge.FlushWithWalls, Floors = 1,
            Texture = Textures.Plain, Seed = s.Seed + 211, Etch = s.Etch,
            Hatch = true, HatchAuto = true,
            Way = kind switch { WayKind.SolidStairs => WayUp.SolidStairs, WayKind.Spiral => WayUp.Spiral, WayKind.Ladder => WayUp.Ladder, _ => WayUp.Stairs },
            StairWidthMm = Math.Round(25 * k28, 1), RiseMm = Math.Round(6 * k28, 1), GoingMm = Math.Round(7 * k28, 1),
            SpiralRadiusMm = Math.Round(26 * k28, 1), SpiralHubMm = Math.Round(9 * k28, 1),
            LadderWidthMm = Math.Round(12 * k28, 1), RungStepMm = Math.Round(6 * k28, 1),
            Rail = kind == WayKind.Stairs ? Railing.Left : Railing.None,
            WaysUp = 1,
        };
    }

    /// <summary>What a way up is called.</summary>
    public static string WayName(WayKind k) => k switch
    {
        WayKind.SolidStairs => "Solid stairs",
        WayKind.Spiral => "Spiral stairs",
        WayKind.Ladder => "Ladder",
        _ => "Stairs",
    };

    static readonly Dictionary<string, FloorKit> WayKits = [];

    /// <summary>The stairs maker's kit for a way up (remembered: the plan asks for it to draw it).</summary>
    public static FloorKit WayKit(BuildingSpec s, WayKind kind)
    {
        var spec = WaySpec(s, kind);
        string key = spec.ToJson();
        lock (WayKits)
            if (WayKits.TryGetValue(key, out var known)) return known;
        var kit = FloorKitMaker.Make(spec);
        lock (WayKits)
        {
            if (WayKits.Count > 64) WayKits.Clear();
            WayKits[key] = kit;
        }
        return kit;
    }

    static bool WayPart(Stairs.Piece p) => p.Name is not ("Floor" or "Trapdoor");

    /// <summary>
    /// Where a way up stands in its kit's own room (the stairs maker's plan, mm: x along, y across): the box
    /// round it and its opening, whose middle goes where it's placed.
    /// </summary>
    static (double Cx, double Cy, PathsD Hatch, PathsD Footprint) WayInKit(FloorKit kit)
    {
        var foot = new PathsD();
        foreach (var part in kit.Parts.Where(WayPart))
        {
            var b = Geo.Bounds(part.Cut);
            foreach (var pose in part.Poses)
            {
                var pts = new[] { pose.At(b.left, b.top, 0), pose.At(b.right, b.top, 0), pose.At(b.left, b.bottom, 0), pose.At(b.right, b.bottom, 0),
                    pose.At(b.left, b.top, 1), pose.At(b.right, b.bottom, 1) };
                // Only what stands below the floor above takes floor space (stairs' tops tuck under the opening).
                foot.Add(Geo.Rect(pts.Min(q => q.X), pts.Min(q => -q.Z), pts.Max(q => q.X), pts.Max(q => -q.Z)));
            }
        }
        foot = Geo.Union(foot);
        var hatch = kit.Layout.Hatch;
        var all = Geo.Bounds(Geo.Union(foot, hatch));
        return ((all.left + all.right) / 2, (all.top + all.bottom) / 2, hatch, foot);
    }

    /// <summary>A point of a way up's kit (its plan, mm) on the building's plan (mm), placed and turned as asked.</summary>
    static PointD WayToPlan(PlacedWay w, double cx, double cy, double sq, double x, double y)
    {
        // The kit's plan has y away from you; the building's towards you. Turned anticlockwise seen from above.
        double lx = x - cx, lz = cy - y;
        var (rx, rz) = TurnXZ(lx, lz, w.Turn);
        return new PointD(w.X * sq + rx, w.Y * sq + rz);
    }

    static (double X, double Z) TurnXZ(double x, double z, int turn)
    {
        double a = (((turn % 4) + 4) % 4) * Math.PI / 2, c = Math.Round(Math.Cos(a)), sn = Math.Round(Math.Sin(a));
        return (x * c + z * sn, -x * sn + z * c);
    }

    /// <summary>A way up's opening and footprint on the building's plan (mm).</summary>
    public static (PathsD Hatch, PathsD Footprint) WayArea(BuildingSpec s, PlacedWay w, double sq)
    {
        var kit = WayKit(s, w.Kind);
        if (!kit.Ok) return ([], []);
        var (cx, cy, hatch, foot) = WayInKit(kit);
        return (Geo.Map(hatch, p => WayToPlan(w, cx, cy, sq, p.x, p.y)), Geo.Map(foot, p => WayToPlan(w, cx, cy, sq, p.x, p.y)));
    }

    /// <summary>
    /// The stairs and ladders from each floor to the one above, made by the stairs maker and stood where
    /// they were put. Returns each one's opening (cut in the floor above) and footprint.
    /// </summary>
    static List<MadeWay> WaysUp(HouseDesign d, BuildingSpec s, List<PlanShape> plans, List<PathsD> plates, double sq, double ox, double oy,
        List<Piece> pieces, List<HouseSolid> solids, List<Finding> findings)
    {
        var made = new List<MadeWay>();
        int levels = plans.Count;
        var counts = new Dictionary<WayKind, int>();
        var order = new List<WayKind>();
        for (int k = 0; k < levels; k++)
        {
            var level = d.Levels[k];
            for (int i = 0; i < level.WaysUp.Count; i++)
            {
                var w = level.WaysUp[i];
                string what = $"The {WayName(w.Kind).ToLowerInvariant()} on floor {k + 1}";
                if (k + 1 >= levels) { findings.Add(new(Severity.Warn, $"{what} lead nowhere: there's no floor above. Add one, or take them away.")); continue; }
                var kit = WayKit(s, w.Kind);
                foreach (var f in kit.Findings.Where(f => f.Level == Walls.Severity.Bad))
                    findings.Add(new(Severity.Warn, $"{what}: {f.Text}"));
                if (!kit.Ok) continue;
                var (cx, cy, hatchK, footK) = WayInKit(kit);
                var hatch = Geo.Map(hatchK, p => WayToPlan(w, cx, cy, sq, p.x, p.y));
                var foot = Geo.Map(footK, p => WayToPlan(w, cx, cy, sq, p.x, p.y));
                made.Add(new MadeWay(k, i, hatch, foot));
                if (!counts.ContainsKey(w.Kind)) { counts[w.Kind] = 0; order.Add(w.Kind); }
                counts[w.Kind]++;
                // Stood in place: the kit's (X, up, Z) → the building's, turned.
                double baseY = BuildingKitMaker.StoreyBase(s, k);
                V3 Point(Stairs.V3 p)
                {
                    var (rx, rz) = TurnXZ(p.X - cx, p.Z + cy, w.Turn);
                    return new V3(w.X * sq + rx - ox * sq, baseY + p.Y, w.Y * sq + rz - oy * sq);
                }
                V3 Dir(Stairs.V3 p)
                {
                    var (rx, rz) = TurnXZ(p.X, p.Z, w.Turn);
                    return new V3(rx, p.Y, rz);
                }
                foreach (var part in kit.Parts.Where(WayPart))
                    foreach (var pose in part.Poses)
                        solids.Add(new HouseSolid(WayName(w.Kind), part.Cut, new Pose(Point(pose.O), Dir(pose.U), Dir(pose.V)), s.WallSheetMm, Nil(part.Etched), Nil(part.Lines), k, SolidKind.Furniture));

                // Room for it: clear of the walls below, its opening clear of the walls above and inside that floor.
                if (Geo.Area(Geo.Intersect(Geo.Offset(foot, -0.3), WallFootprint(level, plans[k], s, sq))) > 0.5)
                    findings.Add(new(Severity.Warn, $"{what} run into a wall or a column: move or turn them."));
                if (Geo.Area(Geo.Intersect(Geo.Offset(hatch, 0.5), WallFootprint(d.Levels[k + 1], plans[k + 1], s, sq))) > 0.5)
                    findings.Add(new(Severity.Warn, $"{what} come up into a wall or column of floor {k + 2}: move them so the opening is clear of the walls above."));
                if (Geo.Area(Geo.Subtract(hatch, plates[k + 1])) > 1)
                    findings.Add(new(Severity.Warn, $"{what} come up past the edge of floor {k + 2}: move them under its floor."));
                foreach (var other in made.Where(m => m.Level == k && m.Index != i))
                    if (Geo.Area(Geo.Intersect(Geo.Union(other.Footprint, other.Hatch), Geo.Union(foot, hatch))) > 0.5)
                        findings.Add(new(Severity.Warn, $"Two ways up on floor {k + 1} run into each other."));
            }
        }
        foreach (var kind in order)
        {
            var kit = WayKit(s, kind);
            string name = WayName(kind);
            foreach (var part in kit.Parts.Where(WayPart))
                pieces.Add(new Piece($"{name}: {part.Name.ToLowerInvariant()}", Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies * counts[kind]));
            findings.Add(new(Severity.Note, $"{(counts[kind] == 1 ? name : $"{counts[kind]} × {name.ToLowerInvariant()}")}: {kit.Layout.Steps switch { > 0 => $"{kit.Layout.Steps} steps of {kit.Layout.RiseMm:0.#} mm, ", _ => "" }}through an opening cut in the floor above."));
        }
        return made;
    }
}
