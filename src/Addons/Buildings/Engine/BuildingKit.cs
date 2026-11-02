using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;
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

public enum WallSide { None, Front, Back, Left, Right, Floor }

/// <summary>Everything a building is cut from, where it all goes, and what was found while making it.</summary>
public sealed record BuildingKit(List<Piece> Pieces, List<Solid> Solids, List<Finding> Findings, double StoreyMm, double WallMm, SetKit? Furniture)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Pieces.Sum(p => p.Copies);
    public int Count(Sheet s) => Pieces.Where(p => p.Sheet == s).Sum(p => p.Copies);
}

/// <summary>
/// Makes a whole building from the wall maker and the furniture maker: for each storey, four walls
/// (front, back and the two sides) that slide into four corner columns; a floor between storeys that
/// lies on the walls and columns, pinned to them at the corners; a low wall of battlements round the
/// top floor if asked for; the front door; and the furniture for every room.
/// </summary>
public static class BuildingKitMaker
{
    /// <summary>How deep the walls go into the columns (mm), at 28 mm figures.</summary>
    public const double SlotDepthMm = 4;
    /// <summary>The pin up each column (and through the floors), mm.</summary>
    public const double PinMm = 2;
    /// <summary>Height of the battlements' merlons (mm), at 28 mm figures.</summary>
    public const double MerlonMm = 6;
    /// <summary>Room the furniture keeps from the walls and each other in the 3D view (mm).</summary>
    public const double Clearance = 1.5;

    public static double WallThickness(BuildingSpec s) => s.WallSheetMm * (s.TwoSided ? 2 : 1);

    /// <summary>A height rounded to whole layers of the wall sheet (at least 2).</summary>
    public static double Layers(double h, double t) => Math.Max(2, Math.Round(h / Math.Max(0.1, t))) * t;

    /// <summary>The storey's walls' height: whole layers of the sheet, so the columns are as tall as the walls.</summary>
    public static double StoreyHeight(BuildingSpec s) => Layers(s.StoreyHeightMm, s.WallSheetMm);

    /// <summary>The parapet's height, in whole layers, to the top of the battlements.</summary>
    public static double ParapetHeight(BuildingSpec s) => Layers(s.ParapetHeightMm, s.WallSheetMm);

    /// <summary>Where storey <paramref name="k"/>'s walls stand (mm up from the ground): each storey's walls, then a floor.</summary>
    public static double StoreyBase(BuildingSpec s, int k) => k * (StoreyHeight(s) + s.WallSheetMm);

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
        };
    }

    public static BuildingKit Make(BuildingSpec s)
    {
        var findings = new List<Finding>();
        var pieces = new List<Piece>();
        var solids = new List<Solid>();
        double T = s.WallSheetMm, Wt = WallThickness(s), W = s.WidthMm, D = s.DepthMm, H = StoreyHeight(s);
        int storeys = Math.Clamp(s.Storeys, 1, 6);
        s = s with { Storeys = storeys };
        if (T < 0.5) { findings.Add(new(Severity.Bad, "The wall sheet is too thin: use at least 0.5 mm.")); return new([], [], findings, H, Wt, null); }
        if (W < 30 || D < 30) { findings.Add(new(Severity.Bad, "The building is too small: make it at least 30 × 30 mm.")); return new([], [], findings, H, Wt, null); }
        if (s.ColumnMm < Wt + 3) { findings.Add(new(Severity.Bad, $"The corner columns are too thin for a {Wt:0.#} mm wall: make them at least {Wt + 3:0.#} mm across.")); return new([], [], findings, H, Wt, null); }
        if (Math.Abs(H - s.StoreyHeightMm) > 0.05)
            findings.Add(new(Severity.Note, $"Each storey is {H:0.#} mm high: a whole number of {T:0.##} mm layers, so the columns come out as tall as the walls."));

        bool parapet = s.Top == BuildingTop.Battlements;
        int wallStoreys = storeys + (parapet ? 1 : 0);
        // Walls that come out the same are cut together: one piece with copies.
        var walls = new Dictionary<string, WallGroup>();
        var extras = new List<Piece>();
        var named = new List<(int Index, WallGroup Group)>();
        WallSpec? frontGround = null;

        for (int k = 0; k < wallStoreys; k++)
        {
            bool top = k == storeys;
            double y0 = StoreyBase(s, k);
            foreach (var side in new[] { WallSide.Front, WallSide.Back, WallSide.Left, WallSide.Right })
            {
                bool frontBack = side is WallSide.Front or WallSide.Back;
                var spec = StoreyWall(s, k, frontBack ? W : D);
                if (!top) spec = spec with { Windows = Math.Max(0, frontBack ? s.WindowsFront : s.WindowsSide) };
                if (k == 0 && side == WallSide.Front)
                {
                    spec = spec with
                    {
                        Door = s.Door == DoorStyle.Loose ? DoorKind.DoorwayAndDoor : DoorKind.Doorway,
                        DoorShape = s.DoorShape, DoorWidthMm = s.DoorWidthMm, DoorHeightMm = s.DoorHeightMm,
                    };
                    frontGround = spec;
                }
                string key = spec.ToJson();
                double c2e = WallKitMaker.CentreToEnd(spec);
                var pose = side switch
                {
                    WallSide.Front => Pose.Front(-W / 2 + c2e, y0, D / 2),
                    WallSide.Back => Pose.Back(W / 2 - c2e, y0, -D / 2),
                    WallSide.Left => Pose.Left(-W / 2, y0, -D / 2 + c2e),
                    _ => Pose.Right(W / 2, y0, D / 2 - c2e),
                };
                if (!walls.TryGetValue(key, out var group))
                {
                    var kit = WallKitMaker.Make(spec);
                    group = new WallGroup(k, kit, spec);
                    walls[key] = group;
                    string what = (side == WallSide.Front ? "Front wall" : side == WallSide.Back ? "Back wall" : "Side wall") + (k == 0 ? "" : k >= s.Storeys ? ", battlements" : $", storey {k + 1}");
                    foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
                        findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"{what}: {f.Text}"));
                    // A loose door (the wall maker's) and anything else that isn't a wall panel.
                    foreach (var extra in kit.Parts.Skip(1).Where(p => p.Name != "Wall back"))
                        extras.Add(new Piece(extra.Name == "Door" ? "Front door" : extra.Name, Sheet.Walls, extra.Cut, extra.Etched, extra.Lines, extra.Copies));
                }
                group.Sides.Add(side);
                if (!group.Kit.Ok || group.Kit.Front is not { } panel) continue;
                named.Add((solids.Count, group));
                solids.Add(new Solid("Wall", panel.Cut, pose, Wt, Nil(panel.Etched), Nil(panel.Lines), k, side, false));
                if (side == WallSide.Front && k == 0 && group.Kit.Parts.FirstOrDefault(p => p.Name == "Door") is { } leaf)
                    solids.Add(new Solid("Front door", leaf.Cut, pose with { O = pose.At(WallKitMaker.PanelLength(spec) / 2 - spec.DoorWidthMm / 2, 0) }, Wt, Nil(leaf.Etched), Nil(leaf.Lines), k, side, false));
            }
        }
        // The walls first (biggest), storey by storey; a back panel for each when they're two-sided; the loose door after.
        foreach (var g in walls.Values.Where(g => g.Kit.Ok && g.Kit.Front is not null))
        {
            var panel = g.Kit.Front!;
            string name = g.Name(wallStoreys, storeys);
            pieces.Add(new Piece(name, Sheet.Walls, panel.Cut, panel.Etched, panel.Lines, g.Sides.Count) { WallSpecJson = g.Spec.ToJson() });
            if (g.Kit.Parts.FirstOrDefault(p => p.Name == "Wall back") is { } back)
                pieces.Add(new Piece($"{name}: back panel", Sheet.Walls, back.Cut, back.Etched, back.Lines, g.Sides.Count));
        }
        // Named once every side is known.
        foreach (var (i, g) in named) solids[i] = solids[i] with { Name = g.Name(wallStoreys, storeys) };
        pieces.AddRange(extras);

        Columns(s, storeys, wallStoreys, pieces, solids, findings);
        Floors(s, storeys, wallStoreys, pieces, solids, findings);
        var furniture = Furnish(s, frontGround, pieces, solids, findings);

        if (frontGround is not null)
            findings.Add(new(Severity.Note, $"The building is {W + s.ColumnMm:0} × {D + s.ColumnMm:0} mm on the ground and {StoreyBase(s, wallStoreys - 1) + (wallStoreys > storeys ? ParapetHeight(s) : H):0} mm tall."));
        return new(pieces, solids, findings, H, Wt, furniture);
    }

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

    /// <summary>Four corner columns a storey: a slot for each of the two walls, a pin hole up the middle; a cap on the top ones if nothing lies on them.</summary>
    static void Columns(BuildingSpec s, int storeys, int wallStoreys, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        double W = s.WidthMm, D = s.DepthMm, T = s.WallSheetMm;
        var corners = new (double X, double Z, double Turn)[] { (-W / 2, D / 2, 0), (W / 2, D / 2, 90), (W / 2, -D / 2, 180), (-W / 2, -D / 2, 270) };
        for (int k = 0; k < wallStoreys; k++)
        {
            bool top = k == wallStoreys - 1;
            var spec = StoreyWall(s, k, W) with { Connectors = new() { [ConnectorKind.Corner] = 4 }, Cap = top };
            double slotW = WallKitMaker.WallThickness(spec) + Math.Max(0, spec.FitMm);
            var col = Connectors.Column(spec, [0, 90], slotW, Math.Max(0.5, spec.SlotDepthMm), out var problem);
            if (problem is not null) { findings.Add(new(Severity.Bad, $"Corner columns: {problem}")); return; }
            int layers = WallKitMaker.ColumnLayers(spec);
            string where = wallStoreys == 1 ? "" : k == storeys ? ", battlements" : $", storey {k + 1}";
            pieces.Add(new Piece($"Column layer{where}", Sheet.Walls, col.Body, [], [], layers * 4));
            if (top) pieces.Add(new Piece("Column cap", Sheet.Walls, col.Top, [], [], 4));
            double y0 = StoreyBase(s, k), tall = layers * T;
            foreach (var (x, z, turn) in corners)
            {
                solids.Add(new Solid("Corner column", col.Body, Flat(x, y0 + tall / 2, z, turn), tall, null, null, k, WallSide.None, false));
                if (top) solids.Add(new Solid("Column cap", col.Top, Flat(x, y0 + tall + T / 2, z, turn), T, null, null, k, WallSide.None, false));
            }
        }
        findings.Add(new(Severity.Note, $"Stack each corner column's layers on a {PinMm:0.#} mm pin (a cocktail stick or wire) and glue them; the pin carries on up through the floors into the column above."));
    }

    /// <summary>Lying flat at <paramref name="y"/>, turned <paramref name="turn"/> degrees round the upright (u along +X when 0, v away from you).</summary>
    public static Pose Flat(double x, double y, double z, double turn)
    {
        double a = turn * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
        return new Pose(new V3(x, y, z), new V3(c, 0, -sn), new V3(-sn, 0, -c));
    }

    /// <summary>
    /// A floor between storeys (and under the parapet): a plate as big as the columns' outside, lying on
    /// the walls and columns, with a pin hole over each column so the pins run on into the columns above.
    /// </summary>
    static void Floors(BuildingSpec s, int storeys, int wallStoreys, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        int count = wallStoreys - 1;
        if (count <= 0) return;
        double W = s.WidthMm, D = s.DepthMm, c = s.ColumnMm, T = s.WallSheetMm;
        double x0 = -W / 2 - c / 2, x1 = W / 2 + c / 2, z0 = -D / 2 - c / 2, z1 = D / 2 + c / 2;
        // Flat: u along x from the left, v from the front to the back (lying face up).
        double w = x1 - x0, d = z1 - z0;
        var holes = new PathsD();
        foreach (var (cx, cz) in new[] { (-W / 2, D / 2), (W / 2, D / 2), (W / 2, -D / 2), (-W / 2, -D / 2) })
            holes.Add(Geo.Circle(cx - x0, z1 - cz, PinMm / 2));
        var cut = Geo.Subtract([Geo.Rect(0, 0, w, d)], holes);
        // Floorboards on the face that shows, inside the walls.
        double Wt = WallThickness(s);
        var inside = Geo.Subtract([Geo.Rect(c / 2 + Wt / 2, c / 2 + Wt / 2, w - c / 2 - Wt / 2, d - c / 2 - Wt / 2)], Geo.Offset(holes, 1));
        var (etched, lines) = WoodFace.Make(inside, true, s.Detail == WoodDetail.None ? WoodDetail.None : WoodDetail.Planks, true, Math.Round(5 * K(s), 1), new Random(s.Seed * 7919 + 101));
        pieces.Add(new Piece("Floor", Sheet.Walls, cut, etched, lines, count));
        for (int k = 1; k <= count; k++)
        {
            double y = StoreyBase(s, k) - T / 2;
            solids.Add(new Solid(k == storeys ? "Top floor" : "Floor", cut, Pose.Up(x0, y, z1), T, etched.Count > 0 ? etched : null, lines.Count > 0 ? lines : null, k, WallSide.Floor, false));
        }
    }

    /// <summary>The furniture maker's pieces for every room (and a swinging door), laid out in the rooms for the 3D view.</summary>
    static SetKit? Furnish(BuildingSpec s, WallSpec? front, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        var chosen = s.Items.Select((r, i) => (Room: r, Label: s.Label(i))).Where(x => x.Room.Item.Count > 0 && x.Room.Item.Kind is not (ItemKind.WallDoor or ItemKind.WallShelf)).ToList();
        var items = chosen.Select(x => x.Room).ToList();
        bool door = s.Door == DoorStyle.Swinging && front is not null;
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
            double x = -s.WidthMm / 2 + WallKitMaker.CentreToEnd(front) + WallKitMaker.PanelLength(front) / 2 - s.DoorWidthMm / 2;
            foreach (var p in dk.Parts)
                foreach (var pose in p.Poses)
                    solids.Add(new Solid("Front door", p.Cut, Interior.Move(pose, x, 0, s.DepthMm / 2, 0), s.FurnitureSheetMm, Nil(p.Etched), Nil(p.Lines), 0, WallSide.Front, true));
        }
        Interior.Lay(s, items, [.. chosen.Select(x => x.Label)], kit, solids, findings);
        if (s.FurnitureSheetMm > s.WallSheetMm + 1e-9 && door)
            findings.Add(new(Severity.Note, "The door is cut from the furniture's sheet, which is thicker than the walls' here."));
        return kit;
    }

    static PathsD? Nil(PathsD p) => p.Count > 0 ? p : null;
    static List<PathD>? Nil(List<PathD> p) => p.Count > 0 ? p : null;
}
