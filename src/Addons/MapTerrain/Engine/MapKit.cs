using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Finding = PhotonCanvas.Addon.Buildings.Finding;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// Makes the terrain for a floor plan: the wall maker's textured wall panels on every wall of the plan
/// (split into panels no longer than asked, each door in a panel of its own), the slotted columns they
/// slide into at every corner, junction, end and split, the doors (the furniture maker's that swing,
/// or the wall maker's loose ones), and the floor tile maker's tiles for every floor square, reaching
/// under the walls round the edge. Everything comes back as the building kit's pieces and solids, so
/// the building maker's 3D view and piece views can show it.
/// <para>
/// In 3D, X runs to the right along the plan's rows, Z down the plan (towards you) and Y up; a grid
/// corner (x, y) is at X = x·square, Z = y·square.
/// </para>
/// </summary>
public static class MapKitMaker
{
    /// <summary>The pin up each column (mm).</summary>
    public const double PinMm = 2;
    /// <summary>The most floor tiles at once.</summary>
    public const int MaxTiles = 400;

    public static double WallThickness(MapSpec s) => s.SheetMm * (s.TwoSided ? 2 : 1);

    /// <summary>The walls' height: whole layers of the sheet (at least 2), so the columns are as tall as the walls.</summary>
    public static double WallHeight(MapSpec s) => Math.Max(2, Math.Round(s.WallHeightMm / Math.Max(0.1, s.SheetMm))) * s.SheetMm;

    /// <summary>How high the walls stand: on the floor tiles, or on the table without them.</summary>
    public static double Ground(MapSpec s) => s.Floor ? s.SheetMm : 0;

    /// <summary>How deep the walls go into the columns (mm).</summary>
    public static double SlotDepth(MapSpec s) => Math.Round(Math.Clamp(s.ColumnMm * 0.3, 1, s.ColumnMm / 2 - 1), 2);

    /// <summary>The wall maker's settings for a panel <paramref name="squares"/> squares long, column centre to column centre.</summary>
    public static WallSpec Wall(MapSpec s, int squares, double doorOffset = double.NaN)
    {
        var preset = Textures.Preset(s.WallTexture) ?? Textures.Preset("Stone blocks")!;
        bool door = !double.IsNaN(doorOffset);
        return new WallSpec
        {
            LengthMm = squares * s.SquareMm,
            Measure = LengthMeasure.Centres,
            HeightMm = WallHeight(s),
            ThicknessMm = s.SheetMm,
            TwoSided = s.TwoSided,
            Walls = 1,
            Texture = preset.Name,
            SizeA = preset.SizeA, SizeB = preset.SizeB, GrooveMm = preset.Groove,
            // The same for every panel of a length, so they come out alike and are cut as copies.
            Seed = s.Seed + 17 * squares,
            Etch = s.Etch,
            PlainEnds = true,
            Top = s.Top,
            Door = door ? (s.Door == DoorStyle.Loose ? DoorKind.DoorwayAndDoor : DoorKind.Doorway) : DoorKind.None,
            DoorShape = s.DoorShape, DoorWidthMm = s.DoorWidthMm, DoorHeightMm = s.DoorHeightMm, DoorOffsetMm = door ? doorOffset : 0,
            Windows = 0,
            FitMm = s.FitMm,
            Connectors = new(),
            ColumnMm = s.ColumnMm,
            SlotDepthMm = SlotDepth(s),
            PinMm = PinMm,
            Cap = s.Caps,
        };
    }

    /// <summary>How far a door's middle may be from the middle of a panel so many squares long and still fit between its plain ends (mm; less than 0 = not at all).</summary>
    public static double DoorRoom(MapSpec s, int squares)
    {
        var w = Wall(s, squares, 0);
        double L = WallKitMaker.PanelLength(w), ez = w.SlotDepthMm + WallKitMaker.EndClearMm, frame = Math.Max(0, w.FrameMm);
        return Math.Floor((L / 2 - ez - s.DoorWidthMm / 2 - frame - 0.05) * 100) / 100;
    }

    /// <summary>The widest door with no frame round it that fits in the middle of a panel so many squares long (mm).</summary>
    public static double NarrowDoor(MapSpec s, int squares)
    {
        var w = Wall(s, squares, 0);
        double L = WallKitMaker.PanelLength(w), ez = w.SlotDepthMm + WallKitMaker.EndClearMm;
        return Math.Min(s.DoorWidthMm, Math.Floor((L - 2 * ez - 0.2) * 10) / 10);
    }

    public static DoorFit Doors(MapSpec s) => new(n => DoorRoom(s, n), n => NarrowDoor(s, n));

    public static MapLayout Layout(MapSpec s) => MapLayoutMaker.Make(s.Plan.Grid(), s.LongestSquares, s.SquareMm, Doors(s));

    /// <summary>The wall maker's settings for a panel as laid out (its door included).</summary>
    public static WallSpec Wall(MapSpec s, WallPanel p, int sign)
    {
        if (p.Door < 0) return Wall(s, p.Squares);
        var w = Wall(s, p.Squares, p.DoorOffsetMm * sign);
        // A narrow door sits loose in its doorway (there's no wall beside it for a hinge's clips), with no frame round it.
        return p.Narrow ? w with { DoorWidthMm = p.NarrowWidthMm, FrameMm = 0, Door = s.Door == DoorStyle.Open ? DoorKind.Doorway : DoorKind.DoorwayAndDoor } : w;
    }

    /// <summary>Does this panel's door hang on clips (the furniture maker's)?</summary>
    static bool Swings(MapSpec s, WallPanel p) => p.Door >= 0 && !p.Narrow && s.Door == DoorStyle.Swinging;

    /// <summary>Does this panel's door sit loose in its doorway (the wall maker's)?</summary>
    static bool Loose(MapSpec s, WallPanel p) => p.Door >= 0 && s.Door != DoorStyle.Open && (p.Narrow || s.Door == DoorStyle.Loose);

    public static BuildingKit Make(MapSpec s, CancellationToken ct = default)
    {
        var findings = new List<Finding>();
        var pieces = new List<Piece>();
        var solids = new List<Solid>();
        double T = s.SheetMm, Wt = WallThickness(s), H = WallHeight(s), g = s.SquareMm;
        BuildingKit Fail(string why) { findings.Add(new(Severity.Bad, why)); return new([], [], findings, H, Wt, null); }
        if (!(g >= 10) || g > 150) return Fail("Make the grid square between 10 and 150 mm.");
        if (!(T >= 0.5)) return Fail("The sheet is too thin: use at least 0.5 mm.");
        if (!s.Walls && !s.Floor) return Fail("Tick Walls or Floor tiles: there's nothing to make.");
        var grid = s.Plan.Grid();
        if (grid.FloorCount == 0) return Fail("The plan has no floor yet: paint some squares, or read a battle map.");

        MapLayout? layout = null;
        SetKit? doors = null;
        if (s.Walls)
        {
            if (s.ColumnMm < Wt + 3) return Fail($"The columns are too thin for a {Wt:0.#} mm wall: make them at least {Wt + 3:0.#} mm across.");
            if (s.ColumnMm > g * 0.8) return Fail($"The columns are too wide for {g:0.#} mm squares: make them at most {Math.Floor(g * 0.8):0} mm.");
            if (Math.Abs(H - s.WallHeightMm) > 0.05)
                findings.Add(new(Severity.Note, $"The walls are {H:0.#} mm high: a whole number of {T:0.##} mm layers, so the columns come out as tall as the walls."));
            layout = Layout(s);
            MakeWalls(s, grid, layout, pieces, solids, findings);
            if (!findings.Any(f => f.Level == Severity.Bad)) Columns(s, layout, pieces, solids, findings);
            if (!findings.Any(f => f.Level == Severity.Bad)) doors = Doors(s, grid, layout, pieces, solids, findings);
        }
        ct.ThrowIfCancellationRequested();
        if (s.Floor && !findings.Any(f => f.Level == Severity.Bad)) MapTiles.Make(s, grid, layout, pieces, solids, findings, ct);

        if (!findings.Any(f => f.Level == Severity.Bad))
        {
            double w = grid.Across * g, d = grid.Down * g;
            var floor = Enumerable.Range(0, grid.Down).SelectMany(y => Enumerable.Range(0, grid.Across).Where(x => grid[x, y]).Select(x => (x, y))).ToList();
            int x0 = floor.Min(c => c.x), x1 = floor.Max(c => c.x), y0 = floor.Min(c => c.y), y1 = floor.Max(c => c.y);
            findings.Add(new(Severity.Note, $"{floor.Count} floor squares, {x1 - x0 + 1} × {y1 - y0 + 1} squares in all ({(x1 - x0 + 1) * g:0} × {(y1 - y0 + 1) * g:0} mm)."));
        }
        return new(pieces, solids, findings, H, Wt, doors);
    }

    /// <summary>A grid corner in 3D (on the table).</summary>
    static V3 Corner(MapSpec s, int x, int y, double up) => new(x * s.SquareMm, up, y * s.SquareMm);

    /// <summary>Where a panel stands: its first end (u = 0) and its textured face (+N) the way it faces; and which way along the run u goes (+1 or −1).</summary>
    public static (Pose Pose, int Sign) PanelPose(MapSpec s, WallPanel p, double up)
    {
        double c2e = WallKitMaker.CentreToEnd(Wall(s, p.Squares)), g = s.SquareMm;
        var (ex, ey) = p.End;
        if (p.Across)
            return p.FacesB ? (Pose.Front(p.X * g + c2e, up, p.Y * g), 1) : (Pose.Back(ex * g - c2e, up, p.Y * g), -1);
        return p.FacesB ? (Pose.Right(p.X * g, up, ey * g - c2e), -1) : (Pose.Left(p.X * g, up, p.Y * g + c2e), 1);
    }

    static string Lengths(int n) => n == 1 ? "1 square" : $"{n} squares";

    static void MakeWalls(MapSpec s, PlanGrid grid, MapLayout layout, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        double up = Ground(s), Wt = WallThickness(s);
        var groups = new Dictionary<string, (WallSpec Spec, WallKit Kit, int Count, int Squares, bool Door)>();
        var plainTop = new SortedSet<int>();
        int unbroken = 0;
        var order = new List<string>();
        foreach (var p in layout.Panels)
        {
            var (pose, sign) = PanelPose(s, p, up);
            var spec = Wall(s, p, sign);
            string key = spec.ToJson();
            if (!groups.TryGetValue(key, out var gr))
            {
                var kit = WallKitMaker.Make(spec);
                // A broken top that cuts a doorway's wall apart: that one keeps a straight top.
                if (!kit.Ok && spec.Top == TopStyle.Ruined)
                {
                    var straight = WallKitMaker.Make(spec with { Top = TopStyle.Straight });
                    if (straight.Ok) { kit = straight; spec = spec with { Top = TopStyle.Straight }; unbroken++; }
                }
                gr = (spec, kit, 0, p.Squares, p.Door >= 0);
                order.Add(key);
                foreach (var f in kit.Findings.Where(f => f.Level != PhotonCanvas.Addon.Walls.Severity.Note))
                {
                    // Short walls with no room for battlements are said once, as a note.
                    if (f.Text.Contains("too short for battlements", StringComparison.Ordinal)) { plainTop.Add(p.Squares); continue; }
                    findings.Add(new(f.Level == PhotonCanvas.Addon.Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Walls {Lengths(p.Squares)} long{(p.Door >= 0 ? " with a door" : "")}: {f.Text}"));
                }
            }
            groups[key] = gr with { Count = gr.Count + 1 };
            if (!gr.Kit.Ok || gr.Kit.Front is not { } panel) continue;
            solids.Add(new Solid(p.Door >= 0 ? "Wall with a door" : "Wall", panel.Cut, pose, Wt, Nil(panel.Etched), Nil(panel.Lines), 0, Side(p), false));
            if (Loose(s, p) && gr.Kit.Parts.FirstOrDefault(x => x.Name == "Door") is { } leaf)
            {
                double cx = WallKitMaker.PanelLength(spec) / 2 + spec.DoorOffsetMm;
                solids.Add(new Solid("Door", leaf.Cut, pose with { O = pose.At(cx - spec.DoorWidthMm / 2, 0) }, Wt, Nil(leaf.Etched), Nil(leaf.Lines), 0, Side(p), false));
            }
        }
        // Longest first; those with a door after the plain ones of the same length.
        foreach (var key in order.OrderByDescending(k => groups[k].Squares).ThenBy(k => groups[k].Door))
        {
            var (spec, kit, count, n, door) = groups[key];
            if (!kit.Ok || kit.Front is not { } panel) continue;
            string name = $"Wall, {Lengths(n)}{(door ? ", door" : "")}";
            // Panels with the door the other way along are told apart.
            if (door && order.Count(k => groups[k].Squares == n && groups[k].Door) > 1)
                name += spec.DoorOffsetMm switch { < -0.05 => " to the left", > 0.05 => " to the right", _ => " in the middle" };
            pieces.Add(new Piece(name, Sheet.Walls, panel.Cut, panel.Etched, panel.Lines, count) { WallSpecJson = spec.ToJson() });
            if (kit.Parts.FirstOrDefault(x => x.Name == "Wall back") is { } back)
                pieces.Add(new Piece($"{name}: back panel", Sheet.Walls, back.Cut, back.Etched, back.Lines, count));
            if (spec.Door == DoorKind.DoorwayAndDoor && kit.Parts.FirstOrDefault(x => x.Name == "Door") is { } leaf)
                pieces.Add(new Piece(spec.FrameMm == 0 ? "Narrow door" : "Door", Sheet.Walls, leaf.Cut, leaf.Etched, leaf.Lines, count));
        }
        // Loose doors all alike are one piece with copies.
        foreach (var name in new[] { "Door", "Narrow door" })
        {
            var same = pieces.Where(x => x.Name == name).ToList();
            if (same.Count < 2 || same.Any(x => x.Cut.Count != same[0].Cut.Count || Math.Abs(Geo.Area(x.Cut) - Geo.Area(same[0].Cut)) > 1e-3)) continue;
            int at = pieces.IndexOf(same[0]);
            pieces.RemoveAll(x => x.Name == name);
            pieces.Insert(Math.Min(at, pieces.Count), same[0] with { Copies = same.Sum(x => x.Copies) });
        }

        int panels = layout.Panels.Count, doorCount = layout.Panels.Count(p => p.Door >= 0);
        if (panels > 0)
            findings.Add(new(Severity.Note, $"{panels} wall panel{(panels == 1 ? "" : "s")}{(doorCount > 0 ? $", {doorCount} with a door" : "")}, {WallHeight(s):0.#} mm high. Their texture faces into the rooms{(s.TwoSided ? " and out" : "")}."));
        foreach (var e in layout.OpenDoors)
            findings.Add(new(Severity.Warn, $"The door {MapPlan.Name(e, grid.Across, grid.Down)} has no room in its wall (it's only a square long between columns, or the door's too wide): it's left as an open doorway."));
        if (plainTop.Count > 0)
            findings.Add(new(Severity.Note, $"Walls {string.Join(" and ", plainTop.Select(Lengths))} long are too short for battlements: they have a straight top."));
        if (unbroken > 0)
            findings.Add(new(Severity.Note, "Some walls with a door keep a straight top: broken away, the top would cut the wall in two over the doorway."));
        var narrow = layout.Panels.Where(p => p.Narrow).ToList();
        if (narrow.Count > 0)
            findings.Add(new(Severity.Note, $"{narrow.Count} door{(narrow.Count == 1 ? " is" : "s are")} in a wall only a square long between two columns (where a corridor meets a room): {(narrow.Count == 1 ? "it's" : "they're")} {narrow.Min(p => p.NarrowWidthMm):0.#} mm wide to fit between them{(s.Door == DoorStyle.Open ? "" : ", with a door that sits loose in the doorway")}."));
        if (layout.ShiftedDoors.Count > 0)
            findings.Add(new(Severity.Note, $"{layout.ShiftedDoors.Count} door{(layout.ShiftedDoors.Count == 1 ? " sits" : "s sit")} a little off the middle of {(layout.ShiftedDoors.Count == 1 ? "its" : "their")} square, to keep clear of the column beside {(layout.ShiftedDoors.Count == 1 ? "it" : "them")}."));
        if (!s.TwoSided && layout.Panels.Any(p => p.Edges().All(e => { var (a, b) = e.Sides; return grid[a.X, a.Y] && grid[b.X, b.Y]; })))
            findings.Add(new(Severity.Note, "Walls between two rooms are textured on one side only: tick Two panels back to back for both."));
    }

    static WallSide Side(WallPanel p) => p.Across ? (p.FacesB ? WallSide.Front : WallSide.Back) : (p.FacesB ? WallSide.Right : WallSide.Left);

    static IEnumerable<EdgeId> Edges(this WallPanel p) => Enumerable.Range(0, p.Squares).Select(p.Edge);

    static void Columns(MapSpec s, MapLayout layout, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        if (layout.Columns.Count == 0) return;
        double T = s.SheetMm, up = Ground(s);
        var spec = Wall(s, 2);
        double slotW = WallKitMaker.WallThickness(spec) + Math.Max(0, spec.FitMm);
        int layers = WallKitMaker.ColumnLayers(spec);
        double tall = layers * T;
        foreach (var kind in Connectors.Kinds)
        {
            var these = layout.Columns.Where(c => c.Kind == kind.Id).ToList();
            if (these.Count == 0) continue;
            var col = Connectors.Column(spec, Connectors.Directions(kind.Id, 0), slotW, Math.Max(0.5, spec.SlotDepthMm), out var problem);
            if (problem is not null) { findings.Add(new(Severity.Bad, $"{kind.Name} columns: {problem}")); return; }
            string name = char.ToUpperInvariant(kind.Short[0]) + kind.Short[1..] + " column";
            pieces.Add(new Piece($"{name} layer", Sheet.Walls, col.Body, [], [], layers * these.Count));
            if (s.Caps) pieces.Add(new Piece($"{name} cap", Sheet.Walls, col.Top, [], [], these.Count));
            foreach (var c in these)
            {
                var at = Corner(s, c.X, c.Y, 0);
                solids.Add(new Solid(name, col.Body, BuildingKitMaker.Flat(at.X, up + tall / 2, at.Z, c.Turn), tall, null, null, 0, WallSide.None, false));
                if (s.Caps) solids.Add(new Solid($"{name} cap", col.Top, BuildingKitMaker.Flat(at.X, up + tall + T / 2, at.Z, c.Turn), T, null, null, 0, WallSide.None, false));
            }
        }
        string Count(ConnectorKind k, string one, string many)
        {
            int n = layout.Columns.Count(c => c.Kind == k);
            return n == 0 ? "" : $"{n} {(n == 1 ? one : many)}";
        }
        var kinds = new[]
        {
            Count(ConnectorKind.Corner, "corner", "corners"), Count(ConnectorKind.Tee, "T", "T's"), Count(ConnectorKind.Cross, "cross", "crosses"),
            Count(ConnectorKind.Straight, "straight", "straight"), Count(ConnectorKind.EndCap, "end", "ends"),
        }.Where(x => x.Length > 0);
        findings.Add(new(Severity.Note, $"{layout.Columns.Count} columns ({string.Join(", ", kinds)}), each {layers} layers stacked on a {PinMm:0.#} mm pin (a cocktail stick) and glued{(s.Caps ? ", with a cap on top" : "")}."));
    }

    /// <summary>A door that swings for every doorway (the furniture maker's), hung on the room side of its wall.</summary>
    static SetKit? Doors(MapSpec s, PlanGrid grid, MapLayout layout, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        var withDoor = layout.Panels.Where(p => Swings(s, p)).ToList();
        if (withDoor.Count == 0) return null;
        var item = Defaults.For(ItemKind.WallDoor) with
        {
            Count = withDoor.Count,
            WidthMm = s.DoorWidthMm, HeightMm = s.DoorHeightMm, WallMm = WallThickness(s), WallSheetMm = s.SheetMm,
            Shape = s.DoorShape switch { OpeningShape.Rectangle => DoorShape.Rectangle, OpeningShape.Pointed => DoorShape.Pointed, _ => DoorShape.Arched },
            OverlapMm = 1.5,
        };
        var set = new FurnitureSet { Items = [item], ThicknessMm = s.DoorSheetMm, FitMm = s.DoorFitMm, Detail = WoodDetail.Planks, Seed = s.Seed };
        var kit = FurnitureKit.Make(set);
        var ik = kit.Items[0];
        foreach (var f in ik.Findings.Where(f => f.Level != Furniture.Severity.Note))
            findings.Add(new(f.Level == Furniture.Severity.Bad ? Severity.Bad : Severity.Warn, $"Doors: {f.Text}"));
        if (!ik.Ok) return kit;
        foreach (var p in ik.Parts)
            pieces.Add(new Piece("Door " + p.Name.ToLowerInvariant(), Sheet.Furniture, p.Cut, p.Etched, p.Lines, p.Copies * withDoor.Count));
        double up = Ground(s);
        foreach (var panel in withDoor)
        {
            var (pose, sign) = PanelPose(s, panel, up);
            var spec = Wall(s, panel, sign);
            double left = WallKitMaker.PanelLength(spec) / 2 + spec.DoorOffsetMm - s.DoorWidthMm / 2;
            foreach (var part in ik.Parts)
                foreach (var local in part.Poses)
                    solids.Add(new Solid("Door", part.Cut, Place(pose, local, left), s.DoorSheetMm, Nil(part.Etched), Nil(part.Lines), 0, Side(panel), true));
        }
        findings.Add(new(Severity.Note, $"{(withDoor.Count == 1 ? "1 door that swings, on" : $"{withDoor.Count} doors that swing, each on")} two clips slid onto the side of its doorway (the furniture maker's), cut from the {s.DoorSheetMm:0.##} mm sheet."));
        return kit;
    }

    /// <summary>A piece placed in a wall's own frame (x along the wall from <paramref name="dx"/>, y up, z out of its face) moved to where the wall stands.</summary>
    static Pose Place(Pose wall, Pose local, double dx)
    {
        V3 Dir(V3 d) => wall.U * d.X + wall.V * d.Y + wall.N * d.Z;
        return new Pose(wall.At(local.O.X + dx, local.O.Y, local.O.Z), Dir(local.U), Dir(local.V));
    }

    internal static PathsD? Nil(PathsD p) => p.Count > 0 ? p : null;
    internal static List<PathD>? Nil(List<PathD> p) => p.Count > 0 ? p : null;
}
