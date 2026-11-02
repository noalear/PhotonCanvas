using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Buildings;

public enum BuildingKind { Cottage, Tavern, Watchtower }

/// <summary>The front door: hung on clips so it swings (the furniture maker's), loose in the doorway (the wall maker's), or just a doorway.</summary>
public enum DoorStyle { Swinging, Loose, Open }

/// <summary>The top of the building: walls cut straight (for a roof), or a floor on top with a low wall of battlements round it.</summary>
public enum BuildingTop { Straight, Battlements }

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
    /// <summary>Walls of two panels glued back to back, textured inside and out.</summary>
    public bool TwoSided { get; init; }
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

    /// <summary>The furniture, room by room.</summary>
    public List<RoomItem> Items { get; init; } = [];
    /// <summary>The sheet the furniture (and a swinging door) is cut from (mm).</summary>
    public double FurnitureSheetMm { get; init; } = 2;
    public double FurnitureFitMm { get; init; } = 0.1;
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
        new(BuildingKind.Cottage, "Cottage", "One storey of fieldstone with an arched door and windows, furnished with a table, chairs, a bed, a chest and a bookshelf."),
        new(BuildingKind.Tavern, "Tavern", "Two storeys: stone below and timber frame above. Tables, benches, stools and crates in the taproom, beds and chests in the rooms upstairs."),
        new(BuildingKind.Watchtower, "Watchtower", "A narrow stone tower with arrow slits, two storeys and a floor on top behind battlements. Crates and a chest below, bunks above."),
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

    /// <summary>A kind of building as it comes, for 28 mm figures.</summary>
    public static BuildingSpec Preset(BuildingKind k) => k switch
    {
        BuildingKind.Tavern => new BuildingSpec
        {
            Kind = k, WidthMm = 150, DepthMm = 100, Storeys = 2, StoreyHeightMm = 48,
            Texture = "Stone blocks", UpperTexture = "Timber frame",
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
            Kind = k, WidthMm = 72, DepthMm = 72, Storeys = 2, StoreyHeightMm = 45, Top = BuildingTop.Battlements, ParapetHeightMm = 21,
            Texture = "Stone blocks", DoorShape = OpeningShape.Arched, DoorWidthMm = 15, DoorHeightMm = 27,
            WindowsFront = 1, WindowsSide = 1, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2.5, WindowHeightMm = 15, SillMm = 18, Bars = WindowBars.None,
            Items = [I(ItemKind.Crate, 0, 3), I(ItemKind.Chest, 0, 1), I(ItemKind.Bed, 1, 2), I(ItemKind.Table, 1, 1, t => t with { WidthMm = 20, DepthMm = 12 }), I(ItemKind.Stool, 1, 2)],
        },
        _ => new BuildingSpec
        {
            Kind = BuildingKind.Cottage, WidthMm = 110, DepthMm = 80, Storeys = 1, StoreyHeightMm = 48,
            Texture = "Fieldstone",
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
            WidthMm = R(WidthMm * k), DepthMm = R(DepthMm * k), StoreyHeightMm = R(StoreyHeightMm * k), ParapetHeightMm = R(ParapetHeightMm * k),
            ColumnMm = Math.Max(WallSheetMm * (TwoSided ? 2 : 1) + 6, R(ColumnMm * k)),
            DoorWidthMm = R(DoorWidthMm * k), DoorHeightMm = R(DoorHeightMm * k),
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
