using Clipper2Lib;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Stairs;
using PhotonCanvas.Addon.Terrain;
using Roofs = PhotonCanvas.Addon.Roofs;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>Which sheet a piece is cut from.</summary>
public enum Sheet { Walls, Furniture }

/// <summary>One piece to cut (flat, mm, y up): its outline and holes, what's etched on it as areas and lines scored on it, and how many.</summary>
public sealed record Piece(string Name, Sheet Sheet, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies)
{
    /// <summary>The wall maker's settings it was made from (walls only), so the furniture maker can match a door or shelf to it.</summary>
    public string? WallSpecJson { get; init; }
}

/// <summary>
/// One piece where it sits once the building is put together, for the 3D view: its outline at
/// <see cref="Pose"/>, <see cref="Thickness"/> thick (centred on the pose's plane), and what's
/// etched on its +N face. <see cref="Storey"/> is the storey it belongs to; <see cref="Side"/> which
/// wall it's in (so the view can cut the front away).
/// </summary>
public sealed record Solid(string Name, PathsD Outline, Pose Pose, double Thickness, PathsD? Etched, List<PathD>? Lines, int Storey, WallSide Side, bool IsFurniture);

public enum WallSide { None, Front, Back, Left, Right, Floor, Ground, Roof }

/// <summary>Everything a building is cut from, where it all goes, and what was found while making it.</summary>
public sealed record BuildingKit(List<Piece> Pieces, List<Solid> Solids, List<Finding> Findings, double StoreyMm, double WallMm, SetKit? Furniture)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Pieces.Sum(p => p.Copies);
    public int Count(Sheet s) => Pieces.Where(p => p.Sheet == s).Sum(p => p.Copies);
}

/// <summary>
/// Makes a whole building from the wall maker and the furniture maker: for each storey, a wall along
/// each side of its <see cref="Footprint"/> (four for a rectangle, eight for an octagon, six for an L)
/// that slides into a corner column at each corner; a floor between storeys that lies on the walls and
/// columns; a low wall of battlements round the top floor if asked for; the front door; and the
/// furniture for every room.
/// </summary>
public static partial class BuildingKitMaker
{
    /// <summary>Room a flat wall's door that swings needs each side of its doorway for its clips and hinge, clear of the corner columns (mm).</summary>
    const double ClipRoomMm = 7;

    /// <summary>How deep the walls go into the columns (mm), at 28 mm figures.</summary>
    public const double SlotDepthMm = 4;
    /// <summary>The pin up each column (and through the floors), mm.</summary>
    public const double PinMm = 2;
    /// <summary>Height of the battlements' merlons (mm), at 28 mm figures.</summary>
    public const double MerlonMm = 6;
    /// <summary>Room the furniture keeps from the walls and each other in the 3D view (mm).</summary>
    public const double Clearance = 1.5;

    public static double WallThickness(BuildingSpec s) => s.BoxWalls ? s.WallMm : s.WallSheetMm * (s.TwoSided ? 2 : 1);

    /// <summary>A storey's walls with their columns' top: a cap (for a roof to sit on, or the top of the building) or flush (for a floor), battlements round a box parapet.</summary>
    static WallSpec Topped(WallSpec w, bool cap, bool parapet = false) => w with
    {
        Cap = cap,
        ColumnTop = !cap ? ColumnTop.Flush : parapet ? ColumnTop.Battlements : ColumnTop.Cap,
    };

    /// <summary>A height rounded to whole layers of the wall sheet (at least 2).</summary>
    public static double Layers(double h, double t) => Math.Max(2, Math.Round(h / Math.Max(0.1, t))) * t;

    /// <summary>The storey's walls' height: whole layers of the sheet, so the columns are as tall as the walls.</summary>
    public static double StoreyHeight(BuildingSpec s) => Layers(s.StoreyHeightMm, s.WallSheetMm);

    /// <summary>The parapet's height, in whole layers, to the top of the battlements.</summary>
    public static double ParapetHeight(BuildingSpec s) => Layers(s.ParapetHeightMm, s.WallSheetMm);

    /// <summary>Where storey <paramref name="k"/>'s walls stand (mm up from the table): on the floor tiles, then each storey's walls and a floor.</summary>
    public static double StoreyBase(BuildingSpec s, int k) => Ground(s) + k * (StoreyHeight(s) + s.WallSheetMm);

    /// <summary>How high the floor tiles under the building are (0 without them).</summary>
    public static double Ground(BuildingSpec s) => s.FloorTiles ? s.WallSheetMm : 0;

    static double K(BuildingSpec s) => Math.Max(0.2, s.ScaleMm / 28);

    /// <summary>The wall maker's settings shared by every wall of a storey (<paramref name="k"/> = the parapet when it's past the last storey).</summary>
    public static WallSpec StoreyWall(BuildingSpec s, int k, double length)
    {
        bool parapet = k >= s.Storeys;
        double k28 = K(s);
        string texture = k > 0 && !parapet && s.UpperTexture is { Length: > 0 } up ? up : s.Texture;
        var preset = Textures.Preset(texture) ?? Textures.Preset("Brick")!;
        return new WallSpec
        {
            LengthMm = length,
            Measure = LengthMeasure.Centres,
            HeightMm = parapet ? ParapetHeight(s) : StoreyHeight(s),
            ThicknessMm = s.WallSheetMm,
            TwoSided = s.TwoSided,
            Walls = 1,
            Texture = preset.Name,
            SizeA = Math.Round(preset.SizeA * k28, 2),
            SizeB = Math.Round(preset.SizeB * k28, 2),
            GrooveMm = preset.UsesGroove ? Math.Round(Math.Max(0.4, preset.Groove * Math.Sqrt(k28)), 2) : preset.Groove,
            Seed = s.Seed + 31 * k,
            Etch = s.Etch,
            PlainEnds = true,
            Top = parapet ? TopStyle.Battlements : TopStyle.Straight,
            MerlonHeightMm = Math.Round(MerlonMm * k28, 1),
            MerlonWidthMm = Math.Round(7 * k28, 1),
            CrenelWidthMm = Math.Round(5 * k28, 1),
            Door = DoorKind.None,
            Windows = 0,
            WindowShape = s.WindowShape,
            WindowWidthMm = s.WindowWidthMm,
            WindowHeightMm = s.WindowHeightMm,
            SillMm = s.SillMm,
            Bars = s.Bars,
            BarMm = Math.Round(1.5 * k28, 1),
            FrameMm = s.WindowShape == OpeningShape.ArrowSlit ? 0 : Math.Round(1.5 * k28, 1),
            FitMm = s.FitMm,
            Connectors = new(),
            ColumnMm = s.ColumnMm,
            SlotDepthMm = Math.Round(Math.Min(SlotDepthMm * k28, s.ColumnMm / 2 - 1), 2),
            PinMm = PinMm,
            Cap = false,
        } is var w && s.BoxWalls ? w with
        {
            Build = WallBuild.Box, WallMm = s.WallMm, TwoSided = false, PlinthMm = 0, EndsIntoColumns = true,
            ColumnBuild = ColumnBuild.Boxed, ColumnTop = ColumnTop.Flush, ColumnBase = ColumnBase.Flat,
            WindowFill = s.WindowFill, MerlonHeightMm = Math.Round(MerlonMm * k28, 1),
        } : w;
    }

    public static BuildingKit Make(BuildingSpec s) => Make(s, null);

    /// <summary>
    /// A building on a footprint drawn by hand (<see cref="Footprint.FromPoints"/>) instead of its
    /// <see cref="BuildingSpec.Shape"/>: the spec's other settings (storeys, walls, door, windows, roof,
    /// furniture) still apply. A rectangle gets every roof; other drawn shapes get none.
    /// </summary>
    public static BuildingKit Make(BuildingSpec s, Footprint? footprint)
    {
        var findings = new List<Finding>();
        var pieces = new List<Piece>();
        var solids = new List<Solid>();
        if (footprint is { IsRectangle: true } drawn)
            s = s with { Shape = BuildingShape.Rectangle, WidthMm = drawn.Bounds.right - drawn.Bounds.left, DepthMm = drawn.Bounds.bottom - drawn.Bounds.top };
        else if (footprint is not null) s = s with { Shape = BuildingShape.Custom };
        if (s.Shape == BuildingShape.Round) s = s with { Sides = Footprint.RoundSides(s.Sides) };
        var fp = footprint is { IsRectangle: false } ? footprint : Footprint.Of(s);
        double T = s.WallSheetMm, Wt = WallThickness(s), H = StoreyHeight(s);
        int storeys = Math.Clamp(s.Storeys, 1, 6);
        s = s with { Storeys = storeys };
        if (!s.BoxWalls && s.Door == DoorStyle.Swinging && s.DoorShape is OpeningShape.Chamfered or OpeningShape.Rough)
        {
            findings.Add(new(Severity.Note, "A door that swings is only made for square, arched and pointed doorways: this one has a loose door instead."));
            s = s with { Door = DoorStyle.Loose };
        }
        var bounds = fp.Bounds;
        double W = bounds.right - bounds.left, D = bounds.bottom - bounds.top;
        if (T < 0.5) { findings.Add(new(Severity.Bad, "The wall sheet is too thin: use at least 0.5 mm.")); return new([], [], findings, H, Wt, null); }
        if (fp.Points.Count < 3 || W < 30 || D < 30) { findings.Add(new(Severity.Bad, "The building is too small: make it at least 30 × 30 mm.")); return new([], [], findings, H, Wt, null); }
        bool anyBoxed = s.BoxWalls && fp.Corners.Any(c => Boxed(s, c));
        if (anyBoxed && s.ColumnMm < s.NarrowestBoxColumn) { findings.Add(new(Severity.Bad, $"The corner columns are too thin for {Wt:0.#} mm box walls: make them at least {Math.Ceiling(s.NarrowestBoxColumn * 10) / 10:0.#} mm across.")); return new([], [], findings, H, Wt, null); }
        if (s.ColumnMm < Wt + 3) { findings.Add(new(Severity.Bad, $"The corner columns are too thin for a {Wt:0.#} mm wall: make them at least {Wt + 3:0.#} mm across.")); return new([], [], findings, H, Wt, null); }
        if ((footprint is null ? Footprint.Problem(s) : null) is { } shapeProblem) { findings.Add(new(Severity.Bad, shapeProblem)); return new([], [], findings, H, Wt, null); }
        if (fp.Walls.FirstOrDefault(w => w.Length < s.ColumnMm + 10) is { } shortWall)
        { findings.Add(new(Severity.Bad, $"A wall is only {shortWall.Length:0.#} mm between its corner columns' centres: make it at least {s.ColumnMm + 10:0.#} mm, or the columns thinner.")); return new([], [], findings, H, Wt, null); }
        if (Math.Abs(H - s.StoreyHeightMm) > 0.05)
            findings.Add(new(Severity.Note, $"Each storey is {H:0.#} mm high: a whole number of {T:0.##} mm layers, so the columns come out as tall as the walls."));

        bool parapet = s.Top == BuildingTop.Battlements;
        int wallStoreys = storeys + (parapet ? 1 : 0);
        // Walls that come out the same are cut together: one piece with copies.
        var walls = new Dictionary<string, WallGroup>();
        var extras = new List<Piece>();
        var named = new List<(int Index, WallGroup Group)>();
        WallSpec? frontGround = null;

        // Walls are made once for each different set of settings.
        var made = new Dictionary<string, WallKit>();
        WallKit MakeWall(WallSpec w) => made.TryGetValue(w.ToJson(), out var m) ? m : made[w.ToJson()] = WallKitMaker.Make(w);
        // Box walls hang their own door on pins (or glue it in when it's loose); flat walls have the furniture maker's.
        WallSpec WithDoor(WallSpec w) => w with
        {
            Door = s.BoxWalls ? (s.Door == DoorStyle.Open ? DoorKind.Doorway : DoorKind.DoorwayAndDoor) : s.Door == DoorStyle.Loose ? DoorKind.DoorwayAndDoor : DoorKind.Doorway,
            DoorOpens = s.Door == DoorStyle.Swinging,
            DoorShape = s.DoorShape, DoorLook = s.DoorLook, DoorWidthMm = s.DoorWidthMm, DoorHeightMm = s.DoorHeightMm,
        };
        // The walls of a many-sided building can be narrow: the door narrows to fit its wall, down to two thirds of its width.
        if (!fp.IsRectangle)
        {
            // A flat wall's door that swings needs room for its clips beside the doorway, clear of the columns.
            if (!s.BoxWalls && s.Door == DoorStyle.Swinging && (fp.Walls[fp.DoorWall].Length - s.ColumnMm - s.DoorWidthMm) / 2 < ClipRoomMm)
            {
                findings.Add(new(Severity.Note, "The front wall is too narrow for a door that swings on clips beside it: it has a loose door instead."));
                s = s with { Door = DoorStyle.Loose };
            }
            static bool DoorMissing(WallKit kit) => kit.Findings.Any(f => f.Level == Walls.Severity.Bad && f.Text.Contains("door", StringComparison.OrdinalIgnoreCase));
            var doorWall = StoreyWall(s, 0, fp.Walls[fp.DoorWall].Length) with { Windows = 0 };
            double want = s.DoorWidthMm, least = Math.Max(4, want * 2 / 3);
            if (DoorMissing(MakeWall(WithDoor(doorWall))))
            {
                double w = want;
                while (w - 0.5 >= least && DoorMissing(MakeWall(WithDoor(doorWall) with { DoorWidthMm = w - 0.5 }))) w -= 0.5;
                if (w - 0.5 >= least)
                {
                    s = s with { DoorWidthMm = w - 0.5 };
                    findings.Add(new(Severity.Note, $"The door is {s.DoorWidthMm:0.#} mm wide (not {want:0.#}), to fit its wall."));
                }
                else findings.Add(new(Severity.Note, $"The front wall is too short for a door {want:0.#} mm wide: make the building wider{(fp.IsRegular && s.Shape == BuildingShape.Round ? ", give it fewer sides" : "")} or the door narrower."));
            }
        }
        // The walls in the order they've always been listed for a rectangle: front, back, left, right.
        var order = fp.IsRectangle ? new[] { 0, 2, 3, 1 } : [.. Enumerable.Range(0, fp.Walls.Count)];
        for (int k = 0; k < wallStoreys; k++)
        {
            bool top = k == storeys;
            double y0 = StoreyBase(s, k);
            foreach (var wi in order)
            {
                var wall = fp.Walls[wi];
                var side = wall.Facing;
                bool frontBack = side is WallSide.Front or WallSide.Back;
                var spec = StoreyWall(s, k, wall.Length);
                if (!top)
                {
                    int want = Math.Max(0, frontBack ? s.WindowsFront : s.WindowsSide);
                    spec = spec with { Windows = want };
                }
                if (k == 0 && wi == fp.DoorWall)
                {
                    spec = WithDoor(spec);
                    frontGround = spec;
                }
                // Round any other shape, as many windows as there's room for in its shorter walls.
                while (!fp.IsRectangle && spec.Windows > 0 && MakeWall(spec).Findings.Any(f => f.Level == Walls.Severity.Warn && f.Text.Contains("window", StringComparison.OrdinalIgnoreCase)))
                    spec = spec with { Windows = spec.Windows - 1 };
                string key = spec.ToJson();
                var pose = WallPose(wall, WallKitMaker.CentreToEnd(spec), y0);
                if (!walls.TryGetValue(key, out var group))
                {
                    var kit = MakeWall(spec);
                    group = new WallGroup(k, kit, spec);
                    walls[key] = group;
                    string what = (k == 0 && wi == fp.DoorWall ? "Front wall" : side == WallSide.Front ? "Front wall" : side == WallSide.Back ? "Back wall" : "Side wall") + (k == 0 ? "" : k >= s.Storeys ? ", battlements" : $", storey {k + 1}");
                    foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
                        findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"{what}: {f.Text}"));
                    // A loose door (the wall maker's) and anything else that isn't a wall panel (a box wall's pieces are all listed with the wall).
                    if (!spec.Boxed)
                        foreach (var extra in kit.Parts.Skip(1).Where(p => p.Name != "Wall back"))
                            extras.Add(new Piece(extra.Name == "Door" ? "Front door" : extra.Name, Sheet.Walls, extra.Cut, extra.Etched, extra.Lines, extra.Copies));
                }
                group.Sides.Add(k == 0 && wi == fp.DoorWall ? WallSide.Front : side);
                if (!group.Kit.Ok || group.Kit.Front is not { } panel) continue;
                if (spec.Boxed)
                {
                    // Every piece of the box where it sits in the wall, the wall where it stands.
                    foreach (var part in group.Kit.Parts)
                        foreach (var at in part.Poses)
                        {
                            if (part.Name == "Front") named.Add((solids.Count, group));
                            string what = part.Name switch { "Front" => "Wall", "Door" => "Front door", _ => "Wall " + part.Name.ToLowerInvariant() };
                            solids.Add(new Solid(what, part.Cut, InWall(pose, at), T, Nil(part.Etched), Nil(part.Lines), k, side, false));
                        }
                    continue;
                }
                named.Add((solids.Count, group));
                solids.Add(new Solid("Wall", panel.Cut, pose, Wt, Nil(panel.Etched), Nil(panel.Lines), k, side, false));
                if (wi == fp.DoorWall && k == 0 && group.Kit.Parts.FirstOrDefault(p => p.Name == "Door") is { } leaf)
                    solids.Add(new Solid("Front door", leaf.Cut, pose with { O = pose.At(WallKitMaker.PanelLength(spec) / 2 - spec.DoorWidthMm / 2, 0) }, Wt, Nil(leaf.Etched), Nil(leaf.Lines), k, side, false));
            }
        }
        // The walls first (biggest), storey by storey; a back panel for each when they're two-sided; the loose door after.
        foreach (var g in walls.Values.Where(g => g.Kit.Ok && g.Kit.Front is not null))
        {
            var panel = g.Kit.Front!;
            string name = g.Name(wallStoreys, storeys);
            if (g.Spec.Boxed)
            {
                // The box's pieces: its front named for the wall, the rest after it ("Walls: back", "Front wall: door").
                foreach (var part in g.Kit.Parts)
                {
                    string what = part.Name == "Front" ? name : part.Name == "Door" ? "Front door" : $"{name}: {part.Name.ToLowerInvariant()}";
                    pieces.Add(new Piece(what, Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies * g.Sides.Count) { WallSpecJson = part.Name == "Front" ? g.Spec.ToJson() : null });
                }
                continue;
            }
            pieces.Add(new Piece(name, Sheet.Walls, panel.Cut, panel.Etched, panel.Lines, g.Sides.Count) { WallSpecJson = g.Spec.ToJson() });
            if (g.Kit.Parts.FirstOrDefault(p => p.Name == "Wall back") is { } back)
                pieces.Add(new Piece($"{name}: back panel", Sheet.Walls, back.Cut, back.Etched, back.Lines, g.Sides.Count));
        }
        // Named once every side is known.
        foreach (var (i, g) in named) solids[i] = solids[i] with { Name = g.Name(wallStoreys, storeys) };
        pieces.AddRange(extras);

        Tiles(s, fp, pieces, solids, findings);
        Columns(s, fp, storeys, wallStoreys, pieces, solids, findings);
        if (!parapet)
        {
            if (fp.IsRectangle) Roof(s, pieces, solids, findings);
            else ShapedRoof(s, fp, pieces, solids, findings);
        }
        var blocked = new List<(int Storey, double X0, double Z0, double X1, double Z1)>();
        if (fp.IsRectangle) Floors(s, storeys, wallStoreys, pieces, solids, findings, blocked);
        else ShapedFloors(s, fp, storeys, wallStoreys, pieces, solids, findings, blocked);
        var furniture = Furnish(s, fp, frontGround, pieces, solids, findings, blocked);

        if (frontGround is not null)
        {
            double tall = StoreyBase(s, wallStoreys - 1) + (wallStoreys > storeys ? ParapetHeight(s) : H);
            foreach (var r in solids.Where(x => x.Side == WallSide.Roof))
            {
                var b = Geo.Bounds(r.Outline);
                foreach (var u in new[] { b.left, b.right })
                    foreach (var v in new[] { b.top, b.bottom })
                        tall = Math.Max(tall, r.Pose.At(u, v, r.Thickness / 2).Y);
            }
            findings.Add(new(Severity.Note, $"The building is {W + s.ColumnMm:0} × {D + s.ColumnMm:0} mm on the ground and {tall:0} mm tall{(solids.Any(x => x.Side == WallSide.Roof) ? " with its roof" : "")}."));
        }
        return new(pieces, solids, findings, H, Wt, furniture);
    }

    /// <summary>Where a wall stands: upright along the footprint's wall, its outside facing out, its panel starting <paramref name="c2e"/> past the first column's centre.</summary>
    public static Pose WallPose(FootprintWall wall, double c2e, double y0)
    {
        var start = new Clipper2Lib.PointD(wall.From.x + wall.Dir.x * c2e, wall.From.y + wall.Dir.y * c2e);
        return new Pose(Footprint.To3D(start, y0), Footprint.Dir3D(wall.Dir), V3.UnitY);
    }

    /// <summary>Whether a corner's column is a box (square, of box walls) rather than a stack of layers.</summary>
    static bool Boxed(BuildingSpec s, FootprintCorner c) => s.BoxWalls && ColumnShapeAt(s, c) == ColumnShape.Square;

    /// <summary>The shape of the column at a corner: the one asked for if it suits the corner's angle, else one that does.</summary>
    public static ColumnShape ColumnShapeAt(BuildingSpec s, FootprintCorner c) => Connectors.Matching(s.Column, c.Slots);

    /// <summary>Walls that come out the same, and the sides of the building they go on.</summary>
    sealed class WallGroup(int storey, WallKit kit, WallSpec spec)
    {
        public int Storey { get; } = storey;
        public WallKit Kit { get; } = kit;
        public WallSpec Spec { get; } = spec;
        public List<WallSide> Sides { get; } = [];

        public string Name(int wallStoreys, int storeys)
        {
            bool f = Sides.Contains(WallSide.Front), b = Sides.Contains(WallSide.Back), sides = Sides.Contains(WallSide.Left) || Sides.Contains(WallSide.Right);
            bool many = Sides.Count > 1;
            string what = (f, b, sides) switch
            {
                (true, true, true) => "Walls",
                (true, true, false) => "Front and back walls",
                (false, true, true) => "Back and side walls",
                (true, false, true) => "Front and side walls",
                (true, false, false) => "Front wall",
                (false, true, false) => "Back wall",
                _ => many ? "Side walls" : "Side wall",
            };
            string where = wallStoreys == 1 ? "" : Storey == storeys ? ", battlements" : $", storey {Storey + 1}";
            return what + where;
        }
    }

    /// <summary>The roof maker's settings for a roof on the top storey's walls (null for none).</summary>
    public static Roofs.RoofSpec? RoofSpecFor(BuildingSpec s)
    {
        var shape = s.RoofFor();
        if (shape == RoofShape.None || s.Top != BuildingTop.Straight) return null;
        int top = Math.Clamp(s.Storeys, 1, 6) - 1;
        var along = Topped(StoreyWall(s, top, s.WidthMm), true);
        var across = Topped(StoreyWall(s, top, s.DepthMm), true);
        var roof = RoofLook(s) with { Style = shape switch { RoofShape.Hip => Roofs.RoofStyle.Hip, RoofShape.LeanTo => Roofs.RoofStyle.LeanTo, _ => Roofs.RoofStyle.Gable } };
        return Roofs.RoofMaker.FitToWalls(roof, along, across);
    }

    /// <summary>The roof maker's settings for the building's roof before it's fitted to the walls: its covering, slope, eaves and trusses, sized for the figures.</summary>
    static Roofs.RoofSpec RoofLook(BuildingSpec s)
    {
        double k28 = K(s);
        var shingle = Roofs.Shingles.Preset(s.RoofTexture) ?? Roofs.Shingles.Preset("Slate")!;
        return new Roofs.RoofSpec
        {
            PitchDeg = s.RoofPitchDeg, EaveMm = s.RoofEaveMm, GableOverhangMm = Math.Round(3 * k28, 1),
            Shingle = shingle.Name, ShingleA = Math.Round(shingle.SizeA * k28, 2), ShingleB = Math.Round(shingle.SizeB * k28, 2),
            ShingleGrooveMm = shingle.UsesGroove ? Math.Round(Math.Max(0.4, shingle.Groove * Math.Sqrt(k28)), 2) : shingle.Groove,
            Seed = s.Seed + 211, Etch = s.Etch, TrussSpacingMm = Math.Round(60 * k28, 1),
            Window = Roofs.GableWindow.None, WindowMm = Math.Round(10 * k28, 1), PinMm = PinMm,
        };
    }

    /// <summary>
    /// The roof maker's roof, sitting on the top storey's column caps with its locator inside the walls:
    /// it lifts off to show the rooms. Its ridge runs along the longer side.
    /// </summary>
    static void Roof(BuildingSpec s, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        if (RoofSpecFor(s) is not { } spec) return;
        var kit = Roofs.RoofMaker.Make(spec);
        foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
            findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Roof: {f.Text}"));
        if (!kit.Ok) return;
        int top = Math.Clamp(s.Storeys, 1, 6) - 1;
        var wall = Topped(StoreyWall(s, top, s.WidthMm), true);
        // The roof's Z = 0 is the top of the column caps it rests on.
        double baseY = StoreyBase(s, top) + WallKitMaker.ColumnTopMm(wall);
        double L = spec.LengthMm, T = kit.ThicknessMm;
        bool alongX = s.WidthMm >= s.DepthMm;
        // The roof's model (X along the ridge from 0, Y across from the middle, Z up) → the building (X right, Y up, Z toward you).
        V3 Dir(Roofs.V3 d) => alongX ? new V3(d.X, d.Z, -d.Y) : new V3(d.Y, d.Z, d.X);
        V3 Point(Roofs.V3 p) => alongX ? new V3(p.X - L / 2, baseY + p.Z, -p.Y) : new V3(p.Y, baseY + p.Z, p.X - L / 2);
        foreach (var part in kit.Parts)
        {
            string name = part.Name.StartsWith("Roof", StringComparison.Ordinal) ? part.Name : "Roof " + char.ToLowerInvariant(part.Name[0]) + part.Name[1..];
            pieces.Add(new Piece(name, Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies));
            foreach (var pose in part.Poses)
            {
                // The roof maker's pieces run from w = 0 to T; a solid here is centred on its pose.
                var mid = pose.O + pose.N * (T / 2);
                solids.Add(new Solid(name, part.Cut, new Pose(Point(mid), Dir(pose.U), Dir(pose.V)), T, Nil(part.Etched), Nil(part.Lines), top + 1, WallSide.Roof, false));
            }
        }
        findings.Add(new(Severity.Note, "The roof is glued together on its base and lifts off: the frame under the base drops inside the top walls so it can't slide."));
    }

    /// <summary>
    /// A corner column at every corner of every storey: slots for its two walls, flush on top under a floor,
    /// a cap on the top ones (battlements round a parapet). Box walls' square columns are boxes; every other
    /// column (any other shape or angle, or flat walls') is a stack of layers on a pin.
    /// </summary>
    static void Columns(BuildingSpec s, Footprint fp, int storeys, int wallStoreys, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        double W = s.WidthMm, T = s.WallSheetMm;
        // Corners whose columns come out the same are cut together.
        var groups = fp.Corners.GroupBy(c => (Shape: ColumnShapeAt(s, c), Slot: Math.Round(c.SlotDeg, 6))).ToList();
        bool one = groups.Count == 1;
        var changed = fp.Corners.Where(c => s.Column != ColumnShape.Auto && ColumnShapeAt(s, c) != s.Column).ToList();
        if (changed.Count > 0)
        {
            var c = changed[0];
            findings.Add(new(Severity.Note, $"{ShapeName(s.Column)} columns don't suit walls meeting at {c.InsideDeg:0.#}°: {(changed.Count == fp.Corners.Count ? "every" : "those")} corner{(changed.Count == fp.Corners.Count ? "'s column is" : "s' columns are")} {ShapeName(ColumnShapeAt(s, c)).ToLowerInvariant()} instead, so each wall meets a flat side."));
        }
        bool stacked = false;
        for (int k = 0; k < wallStoreys; k++)
        {
            bool top = k == wallStoreys - 1;
            double y0 = StoreyBase(s, k);
            string where = wallStoreys == 1 ? "" : k == storeys ? ", battlements" : $", storey {k + 1}";
            foreach (var g in groups)
            {
                var spec = Topped(StoreyWall(s, k, W), top, k == storeys) with { Column = g.Key.Shape, CustomAngleDeg = g.Key.Slot };
                var kind = g.First().Kind;
                string label = one ? "" : (g.Key.Slot is > 89.999 and < 90.001 ? "Corner" : $"{g.Key.Slot:0.#}°") + (g.Key.Shape is ColumnShape.Square ? "" : " " + ShapeName(g.Key.Shape).ToLowerInvariant());
                if (s.BoxWalls)
                {
                    // The wall maker's columns for box walls: boxed when square, stacked otherwise.
                    spec = spec with { ColumnBuild = g.Key.Shape == ColumnShape.Square ? ColumnBuild.Boxed : ColumnBuild.Stacked };
                    stacked |= spec.ColumnBuild == ColumnBuild.Stacked;
                    var at = g.Select(c => (new Walls.Model.V3(c.At.x, y0, -c.At.y), c.YawDeg)).ToList();
                    var parts = BoxColumns.Columns(spec, kind, g.Count(), at, out var problem);
                    if (problem is not null) { findings.Add(new(Severity.Bad, $"Corner columns: {problem}")); return; }
                    foreach (var p in parts)
                    {
                        // "Corner column side", or for the other kinds "135° octagonal column layer".
                        string name = one || kind == ConnectorKind.Corner && g.Key.Shape == ColumnShape.Square ? p.Name : $"{label.Trim()} column{p.Name[(p.Name.IndexOf(" column", StringComparison.Ordinal) + 7)..]}";
                        pieces.Add(new Piece(Capital(name) + where, Sheet.Walls, p.Cut, p.Etched, p.Lines, p.Copies));
                        foreach (var pose in p.Poses)
                            solids.Add(new Solid(p.Name.Contains("cap") || p.Name.Contains("merlons") ? "Column cap" : "Corner column", p.Cut, Building(pose), T, Nil(p.Etched), Nil(p.Lines), k, WallSide.None, false));
                    }
                    continue;
                }
                // Flat walls: a stack of layers with a slot for each wall and a pin hole, a cap on the top storey.
                var flat = spec with { Connectors = new() { [kind] = g.Count() } };
                double slotW = WallKitMaker.WallThickness(flat) + Math.Max(0, flat.FitMm);
                var col = Connectors.Column(flat, g.First().Slots, slotW, Math.Max(0.5, flat.SlotDepthMm), out var trouble);
                if (trouble is not null) { findings.Add(new(Severity.Bad, $"Corner columns: {trouble}")); return; }
                int layers = WallKitMaker.ColumnLayers(flat);
                string prefix = one ? "Column" : $"{Capital(label.Trim())} column";
                pieces.Add(new Piece($"{prefix} layer{where}", Sheet.Walls, col.Body, [], [], layers * g.Count()));
                if (top) pieces.Add(new Piece(one ? "Column cap" : $"{prefix} cap", Sheet.Walls, col.Top, [], [], g.Count()));
                double tall = layers * T;
                foreach (var c in g)
                {
                    solids.Add(new Solid("Corner column", col.Body, Flat(c.At.x, y0 + tall / 2, -c.At.y, c.YawDeg), tall, null, null, k, WallSide.None, false));
                    if (top) solids.Add(new Solid("Column cap", col.Top, Flat(c.At.x, y0 + tall + T / 2, -c.At.y, c.YawDeg), T, null, null, k, WallSide.None, false));
                }
            }
        }
        if (!s.BoxWalls)
            findings.Add(new(Severity.Note, $"Stack each corner column's layers on a {PinMm:0.#} mm pin (a cocktail stick or wire) and glue them; the pin carries on up through the floors into the column above."));
        else
        {
            if (fp.Corners.Any(c => Boxed(s, c)))
                findings.Add(new(Severity.Note, $"Each {(stacked ? "square " : "")}corner column is a box {s.ColumnMm:0.#} mm square: its sides go round the plug on its base, the walls' tabs into their slots, then its top."));
            if (stacked)
                findings.Add(new(Severity.Note, $"The {(fp.Corners.All(c => !Boxed(s, c)) ? "" : "other ")}corner columns are stacks of layers {s.ColumnMm:0.#} mm across: line each up on a {PinMm:0.#} mm pin through the middle, the walls' tabs in its slots, and glue them."));
        }
    }

    static string Capital(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];

    /// <summary>What a column shape is called in the window and the notes.</summary>
    public static string ShapeName(ColumnShape c) => c switch
    {
        ColumnShape.Square => "Square",
        ColumnShape.Octagon => "Octagonal",
        ColumnShape.Hexagon => "Hexagonal",
        ColumnShape.Round => "Round",
        ColumnShape.Chamfered => "Chamfered",
        ColumnShape.Fluted => "Fluted",
        _ => "Matching",
    };

    /// <summary>The floor tile maker's settings for the tiles under the building: a board as big as its outside (and the yard round it).</summary>
    public static TileSpec TileSpecFor(BuildingSpec s) => TileSpecFor(s, Footprint.Of(s));

    /// <summary>The tiles for a building of this footprint: a board as big as its outside's bounds (and the yard round it).</summary>
    public static TileSpec TileSpecFor(BuildingSpec s, Footprint fp)
    {
        var fb = fp.Bounds;
        double fw = fb.right - fb.left, fd = fb.bottom - fb.top;
        double sq = Math.Max(5, s.SquareMm), yard = Math.Max(0, s.YardMm);
        var preset = FloorTextures.Preset(s.FloorTexture) ?? FloorTextures.Preset("Flagstones")!;
        var (a, b) = FloorTextures.SizesFor(preset, sq);
        // A strip less than half a square wide at the edge is too thin for the jigsaw joints: the board reaches a whole square instead.
        static double Squares(double mm, double sq)
        {
            double n = mm / sq, frac = n - Math.Floor(n);
            return Math.Round(frac > 0.02 && frac < 0.5 ? Math.Ceiling(n) : n, 4);
        }
        return new TileSpec
        {
            Mode = TileMode.Board, SquareMm = sq,
            BoardAcross = Squares(fw + s.ColumnMm + 2 * yard, sq),
            BoardDown = Squares(fd + s.ColumnMm + 2 * yard, sq),
            TileAcross = 2, TileDown = 2, PlainBorder = true,
            ThicknessMm = s.WallSheetMm,
            Texture = preset.Name, SizeA = a, SizeB = b, GrooveMm = preset.Groove, Seed = s.Seed + 7, Etch = s.Etch,
            Join = TileJoin.Jigsaw, FitMm = s.FitMm, TabMm = Math.Round(5 * Math.Clamp(sq / 25.4, 0.6, 2), 1),
        };
    }

    /// <summary>The floor tiles the building stands on, from the floor tile maker, lying where they go.</summary>
    static void Tiles(BuildingSpec s, Footprint fp, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        if (!s.FloorTiles) return;
        var spec = TileSpecFor(s, fp);
        var kit = TileKitMaker.Make(spec);
        foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
            findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Floor tiles: {f.Text}"));
        if (!kit.Ok) return;
        foreach (var p in kit.Parts)
            pieces.Add(new Piece(p.Name == TileKitMaker.ClipName ? "Floor tile clip" : $"Floor tile {p.Name}", Sheet.Walls, p.Cut, p.Etched, p.Lines, p.Copies));
        // The board centred under the building (it can reach a little past the yard to a whole square), its rows running back.
        var fb = fp.Bounds;
        double x0 = (fb.left + fb.right) / 2 - spec.BoardAcross * spec.SquareMm / 2, z1 = -(fb.top + fb.bottom) / 2 + spec.BoardDown * spec.SquareMm / 2;   // centred under the building
        foreach (var spot in kit.Board)
            solids.Add(new Solid("Floor tile", spot.Tile.Cut, Pose.Up(x0 + spot.X, s.WallSheetMm / 2, z1 - spot.Y), s.WallSheetMm, Nil(spot.Tile.Etched), Nil(spot.Tile.Lines), 0, WallSide.Ground, false));
        findings.Add(new(Severity.Note, $"The building stands on {kit.Board.Count} floor tiles ({spec.BoardAcross:0.#} × {spec.BoardDown:0.#} squares of {spec.SquareMm:0.#} mm) that lock together with jigsaw edges."));
    }

    /// <summary>Lying flat at <paramref name="y"/>, turned <paramref name="turn"/> degrees round the upright (u along +X when 0, v away from you).</summary>
    public static Pose Flat(double x, double y, double z, double turn)
    {
        double a = turn * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
        return new Pose(new V3(x, y, z), new V3(c, 0, -sn), new V3(-sn, 0, -c));
    }

    /// <summary>The stairs maker's settings for the floors between storeys: a plate over the columns' outside, lying on the walls and columns, with an opening and a way up at the back.</summary>
    public static FloorSpec FloorSpecFor(BuildingSpec s, int floors)
    {
        double k28 = K(s), Wt = WallThickness(s);
        return new FloorSpec
        {
            WallHeightMm = StoreyHeight(s), WallSheetMm = s.WallSheetMm, TwoSidedWalls = s.TwoSided && !s.BoxWalls, WallThickMm = s.BoxWalls ? s.WallMm : 0,
            WallLengthMm = s.WidthMm, WallsAlong = 1, WallWidthMm = s.DepthMm, WallsAcross = 1,
            // Told about the columns so the opening and the way up keep clear of them. The building's columns
            // are only as tall as the walls, though, so the floor lies on them: Floors() fills their holes back in.
            Columns = true, ColumnMm = s.ColumnMm, ColumnCaps = false,
            SheetMm = s.WallSheetMm, FitMm = s.FitMm,
            Floor = true, Edge = FloorEdge.Overhang, OverhangMm = Math.Max(0, s.ColumnMm / 2 - Wt / 2), Floors = floors,
            Texture = FloorTexture(s.Detail), SizeA = Math.Round(FloorSizes(s.Detail).A * k28, 2), SizeB = Math.Round(FloorSizes(s.Detail).B * k28, 1), GrooveMm = 0.5,
            Seed = s.Seed + 101, Etch = s.Etch,
            Hatch = s.WayUp != WayUpStyle.None, HatchAuto = true,
            Way = s.WayUp switch { WayUpStyle.Stairs => WayUp.Stairs, WayUpStyle.Spiral => WayUp.Spiral, WayUpStyle.Ladder => WayUp.Ladder, _ => WayUp.None },
            StairWidthMm = Math.Round(25 * k28, 1), RiseMm = Math.Round(6 * k28, 1), GoingMm = Math.Round(7 * k28, 1),
            SpiralRadiusMm = Math.Round(26 * k28, 1), SpiralHubMm = Math.Round(9 * k28, 1),
            LadderWidthMm = Math.Round(12 * k28, 1), RungStepMm = Math.Round(6 * k28, 1),
            WaysUp = s.WayUp == WayUpStyle.None ? 1 : floors,
        };
    }

    /// <summary>
    /// The floors between storeys (and under the battlements) and the way up to each, from the stairs
    /// maker: the plate lies on the walls and the columns, with a pin hole over each column so the
    /// pins run on into the columns above. Its plan is turned round so the opening and the way up are
    /// at the back right, away from the front door.
    /// </summary>
    static void Floors(BuildingSpec s, int storeys, int wallStoreys, List<Piece> pieces, List<Solid> solids, List<Finding> findings, List<(int Storey, double X0, double Z0, double X1, double Z1)> blocked)
    {
        int count = wallStoreys - 1;
        if (count <= 0) return;
        double W = s.WidthMm, D = s.DepthMm;
        var spec = FloorSpecFor(s, count);
        var kit = FloorKitMaker.Make(spec);
        foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
            findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Floors: {f.Text}"));
        if (!kit.Ok) return;
        // Plan (x along, y across, from the first corner column) → the building, turned half round: the first corner is the back right.
        V3 Point(Stairs.V3 p, double up) => new(W / 2 - p.X, p.Y + up, -D / 2 - p.Z);
        V3 Dir(Stairs.V3 p) => new(-p.X, p.Y, -p.Z);
        // Stacked columns carry a pin up through the floors; boxed ones have none.
        var pins = s.BoxWalls ? [] : new PathsD { Geo.Circle(0, 0, PinMm / 2), Geo.Circle(W, 0, PinMm / 2), Geo.Circle(0, D, PinMm / 2), Geo.Circle(W, D, PinMm / 2) };
        var columnHoles = Plate.ColumnHoles(spec);
        foreach (var part in kit.Parts)
        {
            bool plate = part.Name == "Floor";
            // The plate lies with its plan coordinates as they are (Pose.Flat at the first corner), so the pin holes go at the column centres.
            var cut = part.Cut;
            if (plate)
            {
                // The plate lies on the columns rather than round them: fill their holes in (inside the plate's edge) and drill the pin holes instead.
                var edge = Geo.Bounds(cut);
                cut = Geo.Subtract(Geo.Intersect(Geo.Union(cut, columnHoles), [Geo.Rect(edge.left, edge.top, edge.right, edge.bottom)]), pins);
            }
            var etched = plate ? Geo.Subtract(part.Etched, Geo.Offset(pins, 0.8)) : part.Etched;
            var lines = plate ? Geo.ClipLines(part.Lines, Geo.Subtract(cut, Geo.Offset(pins, 0.8))) : part.Lines;
            string name = part.Name switch { "Floor" => "Floor", _ => part.Name };
            pieces.Add(new Piece(name, Sheet.Walls, cut, etched, lines, part.Copies));
            if (part.Poses.Count == 0) continue;
            for (int k = 1; k <= count; k++)
            {
                double up = StoreyBase(s, k - 1);
                foreach (var pose in part.Poses)
                {
                    var solid = new Solid(plate ? (k == storeys ? "Top floor" : "Floor") : part.Name, cut, new Pose(Point(pose.O, up), Dir(pose.U), Dir(pose.V)), s.WallSheetMm,
                        Nil(etched), Nil(lines), plate ? k : k - 1, plate ? WallSide.Floor : WallSide.None, false);
                    solids.Add(solid);
                    if (plate) continue;
                    // The way up takes room on the storey below.
                    var b = Geo.Bounds(cut);
                    var pts = new[] { solid.Pose.At(b.left, b.top), solid.Pose.At(b.right, b.top), solid.Pose.At(b.left, b.bottom), solid.Pose.At(b.right, b.bottom) };
                    blocked.Add((k - 1, pts.Min(q => q.X), pts.Min(q => q.Z), pts.Max(q => q.X), pts.Max(q => q.Z)));
                }
                // Nothing stands under the opening on the storey above, either.
                if (kit.Layout.Hatch.Count > 0)
                {
                    var hb = Geo.Bounds(kit.Layout.Hatch);
                    double x0 = W / 2 - hb.right, x1 = W / 2 - hb.left, z0 = -D / 2 + hb.top, z1 = -D / 2 + hb.bottom;
                    blocked.Add((k, x0, z0, x1, z1));
                }
            }
        }
        if (s.WayUp != WayUpStyle.None)
            findings.Add(new(Severity.Note, $"Each floor has an opening at the back right with {(s.WayUp == WayUpStyle.Ladder ? "a ladder" : s.WayUp == WayUpStyle.Spiral ? "spiral stairs" : "stairs")} up to it from the storey below."));
    }

    /// <summary>The furniture maker's pieces for every room (and a swinging door), laid out in the rooms for the 3D view.</summary>
    static SetKit? Furnish(BuildingSpec s, Footprint fp, WallSpec? front, List<Piece> pieces, List<Solid> solids, List<Finding> findings, List<(int Storey, double X0, double Z0, double X1, double Z1)> blocked)
    {
        var chosen = s.Items.Select((r, i) => (Room: r, Label: s.Label(i))).Where(x => x.Room.Item.Count > 0 && x.Room.Item.Kind is not (ItemKind.WallDoor or ItemKind.WallShelf)).ToList();
        var items = chosen.Select(x => x.Room).ToList();
        bool door = s.Door == DoorStyle.Swinging && front is not null && !s.BoxWalls;
        var all = items.Select(r => r.Item).ToList();
        if (door)
        {
            all.Add(Defaults.For(ItemKind.WallDoor) with
            {
                WidthMm = s.DoorWidthMm, HeightMm = s.DoorHeightMm, WallMm = WallThickness(s), WallSheetMm = s.WallSheetMm,
                Shape = s.DoorShape switch { OpeningShape.Rectangle => DoorShape.Rectangle, OpeningShape.Pointed => DoorShape.Pointed, _ => DoorShape.Arched },
                OverlapMm = Math.Round(1.5 * K(s), 1),
            });
        }
        if (all.Count == 0) return null;
        var set = new FurnitureSet { Items = all, ThicknessMm = s.FurnitureSheetMm, FitMm = s.FurnitureFitMm, PushFit = s.FurniturePushFit, GripMm = s.FurnitureGripMm, Detail = s.Detail, Seed = s.Seed, ScaleMm = s.ScaleMm };
        var kit = FurnitureKit.Make(set);
        for (int i = 0; i < kit.Items.Count; i++)
        {
            var ik = kit.Items[i];
            bool isDoor = door && i == kit.Items.Count - 1;
            string label = isDoor ? "Front door" : chosen[i].Label;
            // Furniture that can't be made is left out (the building can still be cut), except the door it needs.
            foreach (var f in ik.Findings.Where(f => f.Level != Furniture.Severity.Note))
                findings.Add(f.Level == Furniture.Severity.Bad
                    ? new(isDoor ? Severity.Bad : Severity.Warn, isDoor ? $"{label}: {f.Text}" : $"{label} left out: {f.Text}")
                    : new(Severity.Warn, $"{label}: {f.Text}"));
            if (!ik.Ok) continue;
            int n = Math.Max(1, ik.Item.Count);
            foreach (var p in ik.Parts)
                pieces.Add(new Piece(isDoor ? "Front " + p.Name.ToLowerInvariant() : $"{label}: {p.Name.ToLowerInvariant()}", Sheet.Furniture, p.Cut, p.Etched, p.Lines, p.Copies * n));
        }
        if (door && kit.Items[^1] is { Ok: true } dk && front is not null)
        {
            // The door hangs in the doorway, its pins in clips on the outside of the front wall.
            var wall = fp.Walls[fp.DoorWall];
            double along = WallKitMaker.CentreToEnd(front) + WallKitMaker.PanelLength(front) / 2 - s.DoorWidthMm / 2;
            var at = new Clipper2Lib.PointD(wall.From.x + wall.Dir.x * along, wall.From.y + wall.Dir.y * along);
            double turn = Math.Atan2(wall.Dir.y, wall.Dir.x) * 180 / Math.PI;
            foreach (var p in dk.Parts)
                foreach (var pose in p.Poses)
                    solids.Add(new Solid("Front door", p.Cut, Interior.Move(pose, at.x, Ground(s), -at.y, turn), s.FurnitureSheetMm, Nil(p.Etched), Nil(p.Lines), 0, WallSide.Front, true));
        }
        Interior.Lay(s, fp, items, [.. chosen.Select(x => x.Label)], kit, solids, findings, blocked);
        if (s.FurnitureSheetMm > s.WallSheetMm + 1e-9 && door)
            findings.Add(new(Severity.Note, "The door is cut from the furniture's sheet, which is thicker than the walls' here."));
        return kit;
    }

    /// <summary>The floors between storeys: planks, or deck plates for metal furniture, or stone for stone.</summary>
    static string FloorTexture(WoodDetail d) => d switch
    {
        WoodDetail.None => Textures.Plain,
        WoodDetail.Metal => ThemeTextures.SciFiPanels,
        WoodDetail.Stone => "Stone blocks",
        _ => "Wood planks (across)",
    };

    static (double A, double B) FloorSizes(WoodDetail d) => d switch
    {
        WoodDetail.Metal => (16, 12),
        WoodDetail.Stone => (12, 6),
        _ => (5, 35),
    };

    /// <summary>A piece of a box wall (placed in the wall's own frame: x along it, y up, z out of its front) where the wall stands.</summary>
    static Pose InWall(Pose wall, Walls.Model.Pose local)
    {
        V3 Dir(Walls.Model.V3 d) => wall.U * d.X + wall.V * d.Y + wall.N * d.Z;
        return new Pose(wall.O + Dir(local.O), Dir(local.U), Dir(local.V));
    }

    /// <summary>The wall maker's pose as the building's (the same room: X right, Y up, Z towards you).</summary>
    static Pose Building(Walls.Model.Pose p) => new(new V3(p.O.X, p.O.Y, p.O.Z), new V3(p.U.X, p.U.Y, p.U.Z), new V3(p.V.X, p.V.Y, p.V.Z));

    static PathsD? Nil(PathsD p) => p.Count > 0 ? p : null;
    static List<PathD>? Nil(List<PathD> p) => p.Count > 0 ? p : null;
}
