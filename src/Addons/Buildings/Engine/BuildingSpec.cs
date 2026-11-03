using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Buildings;

public enum BuildingKind { Cottage, Tavern, Watchtower, Outpost, Chapel, Crypt, AdobeHouse }

/// <summary>The front door: hung on clips so it swings (the furniture maker's), loose in the doorway (the wall maker's), or just a doorway.</summary>
public enum DoorStyle { Swinging, Loose, Open }

/// <summary>How you get up to the floors above: the stairs maker's stairs, spiral stairs or a ladder, through an opening at the back.</summary>
public enum WayUpStyle { Stairs, Spiral, Ladder, None }

/// <summary>The top of the building: walls cut straight (for a roof), or a floor on top with a low wall of battlements round it.</summary>
public enum BuildingTop { Straight, Battlements }

/// <summary>The roof on straight walls (the roof maker's): none, pitched with gable ends, hipped, or a lean-to.</summary>
public enum RoofShape { None, Gable, Hip, LeanTo }

/// <summary>One piece of furniture in the building, the storey it stands on (0 = the ground) and how many.</summary>
public sealed record RoomItem
{
    public FurnitureItem Item { get; init; } = Defaults.For(ItemKind.Table);
    public int Storey { get; init; }
    /// <summary>What to call it (null = the kind of furniture: "Table", "Table 2").</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Everything a building is made from. Sizes are in millimetres for the figures chosen (<see cref="ScaleMm"/>);
/// <see cref="Presets"/> gives each kind of building at 28 mm, and <see cref="Scaled"/> resizes them. Saved
/// on the shapes it makes, so it can be opened and changed.
/// </summary>
public sealed record BuildingSpec
{
    public BuildingKind Kind { get; init; } = BuildingKind.Cottage;
    /// <summary>The look last picked (one of the terrain themes: see <see cref="BuildingThemes"/>).</summary>
    public string Theme { get; init; } = "Medieval";
    /// <summary>The figures it's for (mm): only used to rescale the sizes when it's changed.</summary>
    public double ScaleMm { get; init; } = 28;

    /// <summary>Front to back corner columns' centres, left to right (mm).</summary>
    public double WidthMm { get; init; } = 110;
    /// <summary>Front to back, between the corner columns' centres (mm).</summary>
    public double DepthMm { get; init; } = 80;
    /// <summary>Storeys with rooms (the ground floor is the first).</summary>
    public int Storeys { get; init; } = 1;
    /// <summary>Height of each storey's walls (mm), rounded to whole layers of the wall sheet so the columns come out flush.</summary>
    public double StoreyHeightMm { get; init; } = 50;
    public BuildingTop Top { get; init; } = BuildingTop.Straight;
    /// <summary>The low wall round the top floor (mm), to the top of the battlements.</summary>
    public double ParapetHeightMm { get; init; } = 21;

    /// <summary>The sheet the walls, columns and floors are cut from (mm).</summary>
    public double WallSheetMm { get; init; } = 3;
    /// <summary>Walls of two panels glued back to back, textured inside and out (flat walls).</summary>
    public bool TwoSided { get; init; }
    /// <summary>
    /// Walls built as boxes (the wall maker's): a textured front and back, a base, a top, ends and ribs, so
    /// they're as thick as <see cref="WallMm"/>; their corner columns are boxes too. Flat walls are a sheet thick.
    /// </summary>
    public bool BoxWalls { get; init; }
    /// <summary>Box walls' thickness, outside to inside (mm).</summary>
    public double WallMm { get; init; } = 12;
    /// <summary>Gap added to the columns' slots (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;
    /// <summary>The ground floor's texture (a wall maker preset).</summary>
    public string Texture { get; init; } = "Fieldstone";
    /// <summary>The texture of the storeys above (null = the same as the ground floor).</summary>
    public string? UpperTexture { get; init; }
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Corner columns' size across (mm).</summary>
    public double ColumnMm { get; init; } = 16;

    public DoorStyle Door { get; init; } = DoorStyle.Swinging;
    public OpeningShape DoorShape { get; init; } = OpeningShape.Arched;
    /// <summary>What a loose door looks like (the wall maker's): planks, studded, a metal blast door, bars or a stone slab.</summary>
    public DoorLook DoorLook { get; init; } = DoorLook.Planks;
    public double DoorWidthMm { get; init; } = 16;
    public double DoorHeightMm { get; init; } = 28;

    /// <summary>Windows in the front and back walls of each storey (the front of the ground floor fits them round the door).</summary>
    public int WindowsFront { get; init; } = 2;
    /// <summary>Windows in each side wall of each storey.</summary>
    public int WindowsSide { get; init; } = 1;
    public OpeningShape WindowShape { get; init; } = OpeningShape.Arched;
    public double WindowWidthMm { get; init; } = 8;
    public double WindowHeightMm { get; init; } = 12;
    /// <summary>Bottom of the windows above the floor of their storey (mm).</summary>
    public double SillMm { get; init; } = 22;
    public WindowBars Bars { get; init; } = WindowBars.Cross;
    /// <summary>What fills box walls' windows: a frame with the bars, a shutter glued in, shutters that open, or nothing.</summary>
    public WindowFill WindowFill { get; init; } = WindowFill.Frame;

    /// <summary>The roof on top of straight walls (none with battlements).</summary>
    public RoofShape Roof { get; init; } = RoofShape.Gable;
    /// <summary>What's etched on the roof (a roof maker shingle preset).</summary>
    public string RoofTexture { get; init; } = "Slate";
    /// <summary>The roof's slope (degrees from flat).</summary>
    public double RoofPitchDeg { get; init; } = 45;
    /// <summary>How far the roof reaches past the walls at the bottom of the slope (mm).</summary>
    public double RoofEaveMm { get; init; } = 4;

    /// <summary>The way up to each floor above.</summary>
    public WayUpStyle WayUp { get; init; } = WayUpStyle.Stairs;

    /// <summary>Floor tiles (the floor tile maker's) under the whole building, cut from the wall sheet.</summary>
    public bool FloorTiles { get; init; } = true;
    /// <summary>The tiles' texture (a floor tile maker preset).</summary>
    public string FloorTexture { get; init; } = "Flagstones";
    /// <summary>The tiles' grid square (mm).</summary>
    public double SquareMm { get; init; } = 25.4;
    /// <summary>How far the tiles reach past the building's outside, all round (mm).</summary>
    public double YardMm { get; init; }

    /// <summary>The furniture, room by room.</summary>
    public List<RoomItem> Items { get; init; } = [];
    /// <summary>The sheet the furniture (and a swinging door) is cut from (mm).</summary>
    public double FurnitureSheetMm { get; init; } = 2;
    public double FurnitureFitMm { get; init; } = 0.1;
    /// <summary>The furniture's joints with the furniture maker's grip ribs, to hold without glue.</summary>
    public bool FurniturePushFit { get; init; }
    public double FurnitureGripMm { get; init; } = 0.15;
    public WoodDetail Detail { get; init; } = WoodDetail.Planks;

    /// <summary>Stones, planks and grain are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;

    public string ToJson() => JsonSerializer.Serialize(this, BuildingJson.Options);

    public static BuildingSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<BuildingSpec>(json, BuildingJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    public sealed record KindInfo(BuildingKind Kind, string Name, string About);

    public static readonly IReadOnlyList<KindInfo> Kinds =
    [
        new(BuildingKind.Cottage, "Cottage", "One storey of fieldstone under a thatched roof, with an arched door and windows, furnished with a table, chairs, a bed, a chest and a bookshelf."),
        new(BuildingKind.Tavern, "Tavern", "Two storeys: stone below and timber frame above, under a tiled roof. Tables, benches, stools and crates in the taproom, beds and chests in the rooms upstairs."),
        new(BuildingKind.Watchtower, "Watchtower", "A narrow stone tower with arrow slits, two storeys and a floor on top behind battlements. Crates and a chest below, bunks above."),
        new(BuildingKind.Outpost, "Sci-fi outpost", "A panelled metal bunkhouse under a corrugated lean-to roof, with a blast door and angled windows. Bunks, lockers, a mess table and crates inside."),
        new(BuildingKind.Chapel, "Gothic chapel", "A tall, narrow chapel of fine-cut stone under a steep slate roof, with a pointed door, tall pointed windows down the sides, pews and an altar on a chequered floor."),
        new(BuildingKind.Crypt, "Dungeon crypt", "A squat crypt of big worn blocks with a studded door and barred windows high up: a tomb, chests and crates on dungeon flagstones."),
        new(BuildingKind.AdobeHouse, "Adobe house", "Two storeys of sandstone and mud plaster with a flat roof behind a low wall and a ladder up: stools, a low table and chests below, beds above."),
    ];

    public static KindInfo Info(BuildingKind k) => Kinds.First(x => x.Kind == k);

    static RoomItem I(ItemKind kind, int storey = 0, int count = 1, Func<FurnitureItem, FurnitureItem>? edit = null, string? name = null)
    {
        var it = Defaults.For(kind) with { Count = count };
        return new RoomItem { Item = edit is null ? it : edit(it), Storey = storey, Name = name };
    }

    /// <summary>What the <paramref name="i"/>th piece of furniture is called: its own name, or its kind (numbered when there's more than one of a kind).</summary>
    public string Label(int i)
    {
        if (i < 0 || i >= Items.Count) return "";
        if (Items[i].Name is { Length: > 0 } n) return n;
        var kind = Items[i].Item.Kind;
        string name = Defaults.Info(kind).Name;
        bool Same(RoomItem r) => r.Name is not { Length: > 0 } && r.Item.Kind == kind;
        return Items.Count(Same) < 2 ? name : $"{name} {Items.Take(i + 1).Count(Same)}";
    }

    /// <summary>
    /// A kind of building as it comes, for 28 mm figures: box walls 12 mm thick, on boxed corner columns. It's as
    /// much bigger than the flat-walled one as its columns are, so the walls between them are as long.
    /// </summary>
    public static BuildingSpec Preset(BuildingKind k)
    {
        var flat = FlatPreset(k);
        double grow = BoxColumnMm - flat.ColumnMm;
        return flat with { BoxWalls = true, WallMm = 12, ColumnMm = BoxColumnMm, WidthMm = flat.WidthMm + grow, DepthMm = flat.DepthMm + grow };
    }

    /// <summary>Corner columns for 12 mm box walls of 3 mm sheet, at 28 mm (mm across).</summary>
    public const double BoxColumnMm = 24;

    /// <summary>The narrowest corner columns box walls this thick fit (mm across).</summary>
    public double NarrowestBoxColumn => WallMm + Math.Max(0, FitMm) + 2 * WallSheetMm + 2;

    /// <summary>A kind of building as it was first made, with flat walls.</summary>
    public static BuildingSpec FlatPreset(BuildingKind k) => k switch
    {
        BuildingKind.Tavern => new BuildingSpec
        {
            Kind = k, WidthMm = 150, DepthMm = 100, Storeys = 2, StoreyHeightMm = 48,
            Texture = "Stone blocks", UpperTexture = "Timber frame", FloorTexture = "Floorboards", RoofTexture = "Clay tiles", RoofPitchDeg = 40,
            DoorShape = OpeningShape.Rectangle, DoorWidthMm = 18, DoorHeightMm = 30,
            WindowsFront = 3, WindowsSide = 1, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 10, WindowHeightMm = 12, SillMm = 20, Bars = WindowBars.Cross,
            Items =
            [
                I(ItemKind.Table, 0, 2), I(ItemKind.Bench, 0, 4), I(ItemKind.Stool, 0, 3), I(ItemKind.Crate, 0, 2),
                I(ItemKind.Table, 0, 1, t => t with { WidthMm = 40, DepthMm = 10, HeightMm = 14, Legs = LegStyle.Slab }, "Bar counter"),
                I(ItemKind.Bed, 1, 3), I(ItemKind.Chest, 1, 2), I(ItemKind.Chair, 1, 1),
            ],
        },
        BuildingKind.Watchtower => new BuildingSpec
        {
            Kind = k, WidthMm = 72, DepthMm = 72, Storeys = 2, StoreyHeightMm = 45, Top = BuildingTop.Battlements, ParapetHeightMm = 21, Roof = RoofShape.None,
            Texture = "Stone blocks", FloorTexture = "Dungeon flagstones", WayUp = WayUpStyle.Ladder, DoorShape = OpeningShape.Arched, DoorWidthMm = 15, DoorHeightMm = 27,
            WindowsFront = 1, WindowsSide = 1, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2.5, WindowHeightMm = 15, SillMm = 18, Bars = WindowBars.None,
            Items = [I(ItemKind.Crate, 0, 3), I(ItemKind.Chest, 0, 1), I(ItemKind.Bed, 1, 2), I(ItemKind.Table, 1, 1, t => t with { WidthMm = 20, DepthMm = 12 }), I(ItemKind.Stool, 1, 2)],
        },
        BuildingKind.Outpost => new BuildingSpec
        {
            Kind = k, Theme = "Sci-fi", WidthMm = 110, DepthMm = 85, Storeys = 1, StoreyHeightMm = 48,
            Texture = "Sci-fi panels", RoofTexture = "Corrugated metal", Roof = RoofShape.LeanTo, RoofPitchDeg = 15, RoofEaveMm = 5,
            Door = DoorStyle.Loose, DoorShape = OpeningShape.Chamfered, DoorLook = DoorLook.Metal, DoorWidthMm = 18, DoorHeightMm = 30,
            WindowsFront = 2, WindowsSide = 1, WindowShape = OpeningShape.Chamfered, WindowWidthMm = 12, WindowHeightMm = 6, SillMm = 24, Bars = WindowBars.None,
            FloorTexture = "Sci-fi deck", Detail = WoodDetail.Metal, WayUp = WayUpStyle.Ladder,
            Items =
            [
                I(ItemKind.Table, 0, 1, t => t with { Legs = LegStyle.Slab, Top = TopShape.Square }), I(ItemKind.Chair, 0, 2, c => c with { Back = BackStyle.Panel }),
                I(ItemKind.Bed, 0, 2, b => b with { FootMm = 13 }), I(ItemKind.Bookshelf, 0, 1, b => b with { Books = false }, "Lockers"), I(ItemKind.Crate, 0, 3),
            ],
        },
        BuildingKind.Chapel => new BuildingSpec
        {
            Kind = k, Theme = "Gothic", WidthMm = 80, DepthMm = 136, Storeys = 1, StoreyHeightMm = 66,
            Texture = "Stone blocks", RoofTexture = "Slate", RoofPitchDeg = 60, RoofEaveMm = 4,
            Door = DoorStyle.Swinging, DoorShape = OpeningShape.Pointed, DoorWidthMm = 18, DoorHeightMm = 34,
            WindowsFront = 0, WindowsSide = 3, WindowShape = OpeningShape.Pointed, WindowWidthMm = 8, WindowHeightMm = 26, SillMm = 22, Bars = WindowBars.Cross,
            FloorTexture = "Chequered tiles", Detail = WoodDetail.Grain,
            Items =
            [
                I(ItemKind.Bench, 0, 6, b => b with { WidthMm = 26 }, "Pews"),
                I(ItemKind.Table, 0, 1, t => t with { WidthMm = 30, DepthMm = 12, HeightMm = 16, Legs = LegStyle.Slab }, "Altar"),
                I(ItemKind.Chair, 0, 2, c => c with { Back = BackStyle.Ladder, HeightMm = 22 }), I(ItemKind.Bookshelf, 0, 1),
            ],
        },
        BuildingKind.Crypt => new BuildingSpec
        {
            Kind = k, Theme = "Dungeon", WidthMm = 100, DepthMm = 100, Storeys = 1, StoreyHeightMm = 45,
            Texture = "Dungeon blocks", RoofTexture = "Slate", RoofPitchDeg = 35, RoofEaveMm = 4,
            Door = DoorStyle.Loose, DoorShape = OpeningShape.Arched, DoorLook = DoorLook.Studded, DoorWidthMm = 16, DoorHeightMm = 28,
            WindowsFront = 0, WindowsSide = 1, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 8, WindowHeightMm = 7, SillMm = 30, Bars = WindowBars.Bars,
            FloorTexture = "Dungeon flagstones", Detail = WoodDetail.Planks,
            Items =
            [
                I(ItemKind.Table, 0, 1, t => t with { WidthMm = 30, DepthMm = 14, HeightMm = 10, Legs = LegStyle.Slab }, "Tomb"),
                I(ItemKind.Chest, 0, 2), I(ItemKind.Crate, 0, 2), I(ItemKind.Bench, 0, 1),
            ],
        },
        BuildingKind.AdobeHouse => new BuildingSpec
        {
            Kind = k, Theme = "Desert", WidthMm = 100, DepthMm = 80, Storeys = 2, StoreyHeightMm = 44, Top = BuildingTop.Battlements, ParapetHeightMm = 18, Roof = RoofShape.None,
            Texture = "Sandstone", UpperTexture = "Adobe", DoorShape = OpeningShape.Rectangle, DoorWidthMm = 15, DoorHeightMm = 27,
            WindowsFront = 2, WindowsSide = 1, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 6, WindowHeightMm = 8, SillMm = 22, Bars = WindowBars.None,
            FloorTexture = "Sandstone slabs", Detail = WoodDetail.Grain, WayUp = WayUpStyle.Ladder,
            Items =
            [
                I(ItemKind.Table, 0, 1, t => t with { Legs = LegStyle.Round, HeightMm = 9 }), I(ItemKind.Stool, 0, 3), I(ItemKind.Chest, 0, 1), I(ItemKind.Crate, 0, 2),
                I(ItemKind.Bed, 1, 2), I(ItemKind.Chest, 1, 1),
            ],
        },
        _ => new BuildingSpec
        {
            Kind = BuildingKind.Cottage, WidthMm = 110, DepthMm = 80, Storeys = 1, StoreyHeightMm = 48,
            Texture = "Fieldstone", RoofTexture = "Thatch", RoofPitchDeg = 50, RoofEaveMm = 6,
            Items = [I(ItemKind.Table), I(ItemKind.Chair, 0, 2), I(ItemKind.Bed), I(ItemKind.Chest), I(ItemKind.Bookshelf)],
        },
    };

    /// <summary>Every size made <paramref name="k"/> times bigger (the sheets and fits stay as they are).</summary>
    public BuildingSpec Scaled(double k)
    {
        static double R(double v) => Math.Round(v, 1);
        return this with
        {
            ScaleMm = R(ScaleMm * k),
            WidthMm = R(WidthMm * k), DepthMm = R(DepthMm * k), SquareMm = R(SquareMm * k), YardMm = R(YardMm * k), StoreyHeightMm = R(StoreyHeightMm * k), ParapetHeightMm = R(ParapetHeightMm * k),
            WallMm = Math.Max(3, Math.Round(WallMm * k / Math.Max(0.1, WallSheetMm))) * WallSheetMm,
            ColumnMm = Math.Max(BoxWalls ? Math.Ceiling(NarrowestBoxColumn + 1) : WallSheetMm * (TwoSided ? 2 : 1) + 6, R(ColumnMm * k)),
            DoorWidthMm = R(DoorWidthMm * k), DoorHeightMm = R(DoorHeightMm * k), RoofEaveMm = R(RoofEaveMm * k),
            WindowWidthMm = R(WindowWidthMm * k), WindowHeightMm = R(WindowHeightMm * k), SillMm = R(SillMm * k),
            Items = [.. Items.Select(r => r with { Item = ScaledItem(r.Item, k) })],
        };
    }

    public static FurnitureItem ScaledItem(FurnitureItem it, double k) => it with
    {
        WidthMm = Math.Round(it.WidthMm * k, 1), DepthMm = Math.Round(it.DepthMm * k, 1), HeightMm = Math.Round(it.HeightMm * k, 1),
        SeatMm = Math.Round(it.SeatMm * k, 1), FootMm = Math.Round(it.FootMm * k, 1), MattressMm = Math.Round(it.MattressMm * k, 1),
        LegMm = Math.Round(it.LegMm * k, 1),
    };
}

static class BuildingJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
