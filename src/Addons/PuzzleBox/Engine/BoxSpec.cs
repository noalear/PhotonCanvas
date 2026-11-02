using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>What closes the top of the box.</summary>
public enum TopKind
{
    /// <summary>Sliding panels side by side in a groove, hooked together underneath: they come out one at a time, each only once it and the next are pushed the right way.</summary>
    SlidingPanels,
    /// <summary>A lid with a lip underneath that sits in the opening and lifts off.</summary>
    LiftLid,
    /// <summary>No way in from the top: a solid block (for a hidden drawer on its own).</summary>
    Closed,
}

/// <summary>How long the sliding panels take to work out (the maker tries layouts and picks one with fewer or more moves).</summary>
public enum Difficulty { Easy, Medium, Hard }

/// <summary>Where the secret message is etched.</summary>
public enum MessagePlace { Drawer, Floor }

/// <summary>
/// One puzzle box: a box glued up from layers of sheet, with a sliding-panel top, a lift-off lid or a
/// solid top, and a drawer hidden in the bottom layers that only opens once a button inside the box
/// is slid. Sizes are in millimetres. Saved with the pieces it makes.
/// </summary>
public sealed record BoxSpec
{
    /// <summary>Outside, left to right.</summary>
    public double WidthMm { get; init; } = 120;
    /// <summary>Outside, front to back.</summary>
    public double DepthMm { get; init; } = 90;
    /// <summary>Outside, bottom to top: rounded to whole layers of the sheet.</summary>
    public double HeightMm { get; init; } = 60;
    /// <summary>How thick the walls are (the width of each layer's ring).</summary>
    public double WallMm { get; init; } = 12;

    public TopKind Top { get; init; } = TopKind.SlidingPanels;
    /// <summary>Sliding panels across the top (1 = a plain sliding lid).</summary>
    public int Panels { get; init; } = 3;
    public Difficulty Difficulty { get; init; } = Difficulty.Medium;

    /// <summary>A drawer in the bottom layers, its front cut flush with the wall so the seams look like the layers'.</summary>
    public bool Drawer { get; init; } = true;
    /// <summary>The drawer's outside height (rounded to whole layers).</summary>
    public double DrawerHeightMm { get; init; } = 18;
    /// <summary>A bolt that keeps the drawer shut until a button on the floor inside the box is slid.</summary>
    public bool Bolt { get; init; } = true;
    /// <summary>A notch in the bottom under the drawer's front, to pull it out with a fingertip.</summary>
    public bool FingerNotch { get; init; } = true;

    /// <summary>Etched inside (empty: none).</summary>
    public string Message { get; init; } = "";
    public MessagePlace MessageAt { get; init; } = MessagePlace.Drawer;
    /// <summary>Letters etched on the faces that get glued over, so the layers go together in the right order.</summary>
    public bool MarkPieces { get; init; } = true;

    public int Count { get; init; } = 1;

    // ---- the sheet ----
    /// <summary>The sheet's thickness: every layer is one sheet.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>The gap round every part that slides (mm each side), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.25;
    /// <summary>Which way each panel's hooks point and where they sit: each number gives a different puzzle.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>For the letters and the message.</summary>
    public string Font { get; init; } = "Arial";

    public string ToJson() => JsonSerializer.Serialize(this, BoxJson.Options);

    public static BoxSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<BoxSpec>(json, BoxJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

public static class Presets
{
    public sealed record Kind(string Name, string About, BoxSpec Spec);

    public static readonly IReadOnlyList<Kind> Kinds =
    [
        new("Sliding-panel puzzle box",
            "The top is panels that slide out one at a time, but each is hooked to the next underneath: push the right panels sideways first, in the right order, or nothing comes out.",
            new BoxSpec { Top = TopKind.SlidingPanels, Panels = 3, Drawer = false, Bolt = false, HeightMm = 45 }),
        new("Secret drawer box",
            "A box with a lift-off lid and a drawer hidden in its bottom layers. The drawer stays shut until you slide the button on the floor inside.",
            new BoxSpec { Top = TopKind.LiftLid, Drawer = true, Bolt = true, HeightMm = 60 }),
        new("Puzzle box with a secret drawer",
            "Both: work the sliding panels out to get in, then find the button inside that lets the hidden drawer open.",
            new BoxSpec { Top = TopKind.SlidingPanels, Panels = 3, Drawer = true, Bolt = true, HeightMm = 66 }),
        new("Hidden drawer block",
            "A solid-looking block with a drawer nobody can see: its seams look like the layers. Pull it from the notch underneath.",
            new BoxSpec { Top = TopKind.Closed, Drawer = true, Bolt = false, HeightMm = 30, DrawerHeightMm = 18 }),
    ];

    /// <summary>The preset a box looks like (by its top, drawer and bolt), or −1.</summary>
    public static int Match(BoxSpec s) =>
        s.Top == TopKind.SlidingPanels && !s.Drawer ? 0
        : s.Top == TopKind.LiftLid && s.Drawer ? 1
        : s.Top == TopKind.SlidingPanels && s.Drawer ? 2
        : s.Top == TopKind.Closed && s.Drawer ? 3 : -1;

    public static readonly IReadOnlyList<string> TopNames = ["Sliding panels", "Lift-off lid", "Closed (solid top)"];
    public static readonly IReadOnlyList<string> DifficultyNames = ["Easy", "Medium", "Hard"];
    public static readonly IReadOnlyList<string> MessagePlaceNames = ["In the drawer", "On the floor of the box"];
}

static class BoxJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
