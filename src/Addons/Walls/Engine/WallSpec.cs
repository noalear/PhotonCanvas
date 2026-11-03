using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Walls;

public enum EtchStyle { Grooves, Lines }
public enum TopStyle { Straight, Battlements, Ruined }
public enum DoorKind { None, Doorway, ClosedDoor, DoorwayAndDoor }
public enum OpeningShape { Rectangle, Arched, Pointed, Round, ArrowSlit, Chamfered, Rough }
/// <summary>What's etched on a closed door, or on a loose door (bars are cut right through a loose one).</summary>
public enum DoorLook { Planks, Studded, Metal, Bars, Stone }
public enum WindowBars { None, Cross, Bars }
/// <summary>What fills a box wall's windows: nothing (bars left in the front face), a frame with its bars, a shutter glued in, or a pair of shutters that open.</summary>
public enum WindowFill { Open, Frame, Shutter, Shutters }
public enum HingeSide { Left, Right }
public enum Accessory { None, Pegs, Sconces, Brackets, OneOfEach, Lamps }
public enum ColumnShape { Auto, Square, Octagon, Round }
public enum LengthMeasure { Panel, Centres }
/// <summary>How a wall is built: one flat panel (or two back to back), or a box with a front, back, top, bottom and ends.</summary>
public enum WallBuild { Flat, Box }
/// <summary>How a box wall's columns are built: a box of four sides with a top and bottom plate, or a stack of layers (any angle).</summary>
public enum ColumnBuild { Boxed, Stacked }
/// <summary>The top of a boxed column.</summary>
public enum ColumnTop { Flush, Cap, SteppedCap, SteppedPoint, Battlements }
/// <summary>The bottom of a boxed column.</summary>
public enum ColumnBase { Flat, Foot, SteppedPlinth }

/// <summary>A connector: a column that walls slide into, with a slot for each wall.</summary>
public enum ConnectorKind { Straight, Corner, Angle135, Sharp45, Tee, Cross, EndCap, Custom }

/// <summary>Everything a set of walls is made from. Saved on the shapes it makes, so it can be opened and changed.</summary>
public sealed record WallSpec
{
    /// <summary>Length of the wall (mm): the panel as cut, or from column centre to column centre (<see cref="Measure"/>).</summary>
    public double LengthMm { get; init; } = 100;
    public LengthMeasure Measure { get; init; } = LengthMeasure.Panel;
    /// <summary>Height of the wall (mm), to the top of the battlements.</summary>
    public double HeightMm { get; init; } = 50;
    /// <summary>The sheet's thickness (mm): drives the slots in the columns and the mount points.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Two panels glued back to back (mirror images), so both faces are textured and the wall is twice as thick.</summary>
    public bool TwoSided { get; init; }
    public int Walls { get; init; } = 1;

    /// <summary>A flat panel (the first walls made), or a box: front, back, top, bottom and ends, which stands up on its own.</summary>
    public WallBuild Build { get; init; } = WallBuild.Flat;
    /// <summary>A box wall's whole thickness, front face to back face (mm). <see cref="ThicknessMm"/> is the panels'.</summary>
    public double WallMm { get; init; } = 12;
    /// <summary>A box wall's ends fit into columns (tabs on the faces go into slots in the columns); otherwise they're closed and finished.</summary>
    public bool EndsIntoColumns { get; init; } = true;
    /// <summary>How far a box wall's bottom plate sticks out past the faces, as a plinth (mm). 0 = flush.</summary>
    public double PlinthMm { get; init; } = 1.5;
    /// <summary>Most room between the ribs that hold a box wall's faces apart (mm).</summary>
    public double RibSpacingMm { get; init; } = 45;
    /// <summary>The texture on a box wall's back face too.</summary>
    public bool TextureBack { get; init; } = true;
    /// <summary>The texture on a box wall's ends and top too (and on the back of a parapet).</summary>
    public bool TextureSides { get; init; }

    /// <summary>The look picked to start from (<see cref="WallThemes"/>): it set the texture, openings and accessories, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";

    /// <summary>A preset (<see cref="Textures.Presets"/>) or the name of one of your own.</summary>
    public string Texture { get; init; } = "Brick";
    /// <summary>Your own texture, kept with the design so it can be made again on another computer.</summary>
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 8;
    public double SizeB { get; init; } = 3.5;
    public double GrooveMm { get; init; } = 0.6;
    public int Seed { get; init; } = 1;
    /// <summary>How worn the texture is, 0 (new and crisp) to 1 (ancient and battered); 0.5 is each texture's usual look.</summary>
    public double Wear { get; init; } = 0.5;
    /// <summary>Size of the texture's smallest marks (pits, nicks, grain, bolts, vent slats) in mm; 0 = the texture's own.</summary>
    public double DetailMm { get; init; }
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Carve the texture as a relief instead of etching it at one depth: a greyscale depth map on a depth-slices picture layer (the grooves' shapes, with depth).</summary>
    public bool Relief { get; init; }
    /// <summary>Leave the ends (the part inside the columns' slots) smooth, so they slide in at full thickness.</summary>
    public bool PlainEnds { get; init; } = true;

    public TopStyle Top { get; init; } = TopStyle.Straight;
    public double MerlonWidthMm { get; init; } = 7;
    public double CrenelWidthMm { get; init; } = 5;
    public double MerlonHeightMm { get; init; } = 6;
    /// <summary>A walkway behind the battlements: a strip on brackets that plug into mount slots.</summary>
    public bool Walkway { get; init; }
    public double WalkwayDepthMm { get; init; } = 10;
    /// <summary>How far down a ruined top is broken, as a share of the height.</summary>
    public double RuinDepth { get; init; } = 0.4;
    /// <summary>How the top is broken, with or without battlements (an old wall with a Ruined top is broken in steps).</summary>
    public RuinStyle Broken { get; init; } = RuinStyle.None;
    /// <summary>Where the break is deepest (or, for a fallen end, which end has fallen).</summary>
    public RuinPlace RuinAt { get; init; } = RuinPlace.Anywhere;
    /// <summary>The merlons' heights: all the same, every other one taller, or taller at the ends.</summary>
    public MerlonPattern Merlons { get; init; } = MerlonPattern.Even;
    /// <summary>How much lower the shorter merlons are than the tall ones (mm).</summary>
    public double MerlonStepMm { get; init; } = 3;

    /// <summary>How the top is broken, taking an old Ruined top as broken in steps.</summary>
    [JsonIgnore] public RuinStyle Breaks => Broken != RuinStyle.None ? Broken : Top == TopStyle.Ruined ? RuinStyle.Stepped : RuinStyle.None;

    public DoorKind Door { get; init; } = DoorKind.None;
    public OpeningShape DoorShape { get; init; } = OpeningShape.Arched;
    public double DoorWidthMm { get; init; } = 16;
    public double DoorHeightMm { get; init; } = 28;
    /// <summary>The door's centre from the middle of the wall (mm; less than 0 = to the left).</summary>
    public double DoorOffsetMm { get; init; }
    /// <summary>What the door looks like: planks, studded planks, a metal blast door, bars (a portcullis) or a stone slab.</summary>
    public DoorLook DoorLook { get; init; } = DoorLook.Planks;

    public int Windows { get; init; }
    /// <summary>
    /// Where each window's centre goes, from the middle of the wall (mm; less than 0 = to the left). When
    /// there are any, the wall has these windows instead of <see cref="Windows"/> spread evenly.
    /// </summary>
    public List<double> WindowAtMm { get; init; } = [];
    public OpeningShape WindowShape { get; init; } = OpeningShape.Arched;
    public double WindowWidthMm { get; init; } = 8;
    public double WindowHeightMm { get; init; } = 12;
    /// <summary>Bottom of the windows above the bottom of the wall (mm).</summary>
    public double SillMm { get; init; } = 25;
    public WindowBars Bars { get; init; } = WindowBars.None;
    public double BarMm { get; init; } = 1.5;
    /// <summary>A box wall's door swings on pins at its top and bottom (else it's glued into a groove round the doorway).</summary>
    public bool DoorOpens { get; init; } = true;
    /// <summary>Which side of the doorway (seen from the front) the door swings from.</summary>
    public HingeSide DoorHinge { get; init; } = HingeSide.Left;
    /// <summary>What fills a box wall's windows.</summary>
    public WindowFill WindowFill { get; init; } = WindowFill.Frame;
    /// <summary>How deep the groove round a door or window is that its glued-in piece sits in (mm).</summary>
    public double InsertGrooveMm { get; init; } = 1.2;
    /// <summary>A smooth band round doors and windows (mm), edged with a groove, where the texture stops.</summary>
    public double FrameMm { get; init; } = 1.5;

    /// <summary>Mount points: slots right through the wall that accessories plug into.</summary>
    public int Mounts { get; init; }
    public double MountHeightMm { get; init; } = 30;
    /// <summary>How tall each mount slot is (mm); it's as wide as the sheet is thick.</summary>
    public double MountSlotMm { get; init; } = 6;
    public Accessory Accessories { get; init; } = Accessory.None;
    /// <summary>
    /// The wall's own mount points, each placed where you like with its own slots and accessory. When there are
    /// any, <see cref="Mounts"/>, <see cref="MountHeightMm"/>, <see cref="MountSlotMm"/> and <see cref="Accessories"/>
    /// aren't used (walls saved before keep them).
    /// </summary>
    public List<MountPoint> MountPoints { get; init; } = [];
    /// <summary>How far a shelf (and its brackets) sticks out from the wall (mm).</summary>
    public double ShelfDepthMm { get; init; } = 10;
    /// <summary>A texture etched on the accessories and shelves (a preset's name; Plain for none).</summary>
    public string AccessoryTexture { get; init; } = Textures.Plain;

    /// <summary>Gap added to slots so parts slide together (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;

    public Dictionary<ConnectorKind, int> Connectors { get; init; } = new() { [ConnectorKind.EndCap] = 2 };
    /// <summary>Angle between the two walls for <see cref="ConnectorKind.Custom"/> (degrees).</summary>
    public double CustomAngleDeg { get; init; } = 120;
    public ColumnShape Column { get; init; } = ColumnShape.Auto;
    /// <summary>Column size across its flats (mm).</summary>
    public double ColumnMm { get; init; } = 16;
    /// <summary>How deep the walls go into the columns (mm).</summary>
    public double SlotDepthMm { get; init; } = 4;
    /// <summary>A hole up the middle of every layer of a column for a pin (a cocktail stick) that lines the stack up. 0 = none.</summary>
    public double PinMm { get; init; } = 2;
    /// <summary>A wider layer on top of each column, over the walls.</summary>
    public bool Cap { get; init; } = true;
    /// <summary>A wider bottom layer on each column.</summary>
    public bool Foot { get; init; }
    /// <summary>A box wall's columns: boxes (straight runs, corners, T junctions, crosses and ends) or stacks of layers (any angle).</summary>
    public ColumnBuild ColumnBuild { get; init; } = ColumnBuild.Boxed;
    /// <summary>What's on top of a box wall's columns.</summary>
    public ColumnTop ColumnTop { get; init; } = ColumnTop.Cap;
    /// <summary>What a box wall's columns stand on.</summary>
    public ColumnBase ColumnBase { get; init; } = ColumnBase.Foot;
    /// <summary>How far a boxed column's cap and foot stick out past its sides, each step (mm).</summary>
    public double ColumnLipMm { get; init; } = 2;

    /// <summary>A new boxed wall with sizes that suit it (12 mm thick from 3 mm sheet, 22 mm columns).</summary>
    public static WallSpec NewBoxed => new() { Build = WallBuild.Box, WallMm = 12, ColumnMm = 22 };

    [JsonIgnore] public bool Boxed => Build == WallBuild.Box;

    public int ConnectorCount(ConnectorKind k) => Connectors.TryGetValue(k, out var n) ? Math.Max(0, n) : 0;

    public string ToJson() => JsonSerializer.Serialize(this, WallJson.Options);

    public static WallSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<WallSpec>(json, WallJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>Your own texture: shapes repeated across the face. Closed shapes are etched as areas, open ones as lines.</summary>
public sealed record CustomTexture
{
    public string Name { get; init; } = "";
    /// <summary>The shapes, as SVG path data in the program's millimetres (y down), with the tile's top-left corner at 0, 0.</summary>
    public string Path { get; init; } = "";
    public double TileWidthMm { get; init; } = 10;
    public double TileHeightMm { get; init; } = 10;
    /// <summary>Every other row shifted along by this share of a tile (0 to 1): 0.5 for a brick-like offset.</summary>
    public double RowShift { get; init; }
}

static class WallJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
