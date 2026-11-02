using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>How a check came out.</summary>
public enum Severity { Good, Note, Warn, Bad }

/// <summary>One line of the motion check: what was checked and how it came out, in plain words.</summary>
public sealed record Finding(Severity Level, string Text);

/// <summary>A label engraved on a part: where (part mm, maths axes), how tall, turned how far (degrees).</summary>
public sealed record PartLabel(string Text, Vec2 At, double HeightMm, double AngleDeg = 0);

/// <summary>
/// A piece to cut: its outline with any holes (maths axes, mm), how many of it, lines marked on it
/// (turning arrows, alignment ticks) and its name label.
/// </summary>
public sealed record Part
{
    public required string Name { get; init; }
    public required Paths64 Shape { get; init; }
    public int Copies { get; init; } = 1;
    /// <summary>Lines marked on it (not cut through), in the same axes as the outline.</summary>
    public IReadOnlyList<List<Vec2>> Marks { get; init; } = [];
    public PartLabel? Label { get; init; }
}

/// <summary>Something that moves (or not) in a view of the mechanism.</summary>
public sealed record Body(string Name, Paths64 Shape, uint Colour, Func<double, Pose> Pose)
{
    /// <summary>Drawn pale, behind (a frame or plate the moving parts sit on).</summary>
    public bool Faint { get; init; }
    /// <summary>Lines drawn on it (a mark showing how far it has turned).</summary>
    public IReadOnlyList<List<Vec2>> Lines { get; init; } = [];
    /// <summary>The input: dragging round it turns the mechanism.</summary>
    public bool IsInput { get; init; }
    /// <summary>For something seen edge-on whose outline changes as it turns (a cam from the front): its outline, already placed, for a turn of the input. <see cref="Shape"/> and <see cref="Pose"/> are then only used for its extent.</summary>
    public Func<double, Paths64>? ShapeAt { get; init; }

    /// <summary>Its outline, placed, for a turn of the input.</summary>
    public Paths64 PlacedAt(double input) => ShapeAt is { } f ? f(input) : Poly.Move(Shape, Pose(input));
}

/// <summary>A picture of the mechanism, drawn for any turn of the input.</summary>
public abstract class MotionPanel
{
    public required string Title { get; init; }
    /// <summary>How much room it gets next to the others.</summary>
    public double Weight { get; init; } = 1;
}

/// <summary>Parts seen flat (looking along their axles), each placed by its own function of the input's turn.</summary>
public sealed class PlanarPanel : MotionPanel
{
    public required List<Body> Bodies { get; init; }
    /// <summary>Everything it ever covers (for fitting it on screen), maths axes.</summary>
    public required Rect2 Extent { get; init; }
    /// <summary>Where the input turns about (dragging round it turns the mechanism), and which way: +1 = anticlockwise as drawn.</summary>
    public Vec2? InputPivot { get; init; }
    public double InputSense { get; init; } = 1;
    /// <summary>Extra lines drawn over the bodies for a turn of the input (a follower's path, a lift scale).</summary>
    public Func<double, IEnumerable<(List<Vec2> Line, uint Colour)>>? Overlay { get; init; }
}

/// <summary>Values over one cycle of the input, as lines on a chart.</summary>
public sealed class ChartPanel : MotionPanel
{
    public required List<Series> Series { get; init; }
    /// <summary>The unit of the values (mm, °).</summary>
    public required string Unit { get; init; }
    /// <summary>A line across the chart (a limit), if any.</summary>
    public double? Limit { get; init; }
}

/// <summary>Values sampled evenly over one cycle (index 0 = start).</summary>
public sealed record Series(string Name, uint Colour, double[] Values)
{
    public bool Dashed { get; init; }
    public double At(double cycleFraction)
    {
        int n = Values.Length;
        if (n == 0) return 0;
        double x = (cycleFraction - Math.Floor(cycleFraction)) * n;
        int i = (int)x % n;
        double t = x - Math.Floor(x);
        return Values[i] * (1 - t) + Values[(i + 1) % n] * t;
    }
}

/// <summary>What the maker made: the parts to cut, how it moves, and what the motion check found.</summary>
public sealed class Mechanism
{
    public required string Title { get; init; }
    public List<Part> Parts { get; } = [];
    public List<MotionPanel> Panels { get; } = [];
    public List<Finding> Findings { get; } = [];
    /// <summary>How far the input turns for one whole cycle of the animation (radians).</summary>
    public double Cycle { get; init; } = 2 * Math.PI;
    /// <summary>The worst finding at each of <see cref="StripSamples"/> points of the cycle (for the bar under the picture).</summary>
    public Severity[] Strip { get; init; } = new Severity[StripSamples];
    public const int StripSamples = 360;
    /// <summary>Names of bodies that clash at a turn of the input (shown red).</summary>
    public Func<double, IReadOnlyCollection<string>>? Clashing { get; set; }
    /// <summary>One line on what it does (the gear ratio, the box's size).</summary>
    public string Summary { get; set; } = "";
    /// <summary>Things to get besides the cut parts (dowels for axles, a handle).</summary>
    public List<string> Shopping { get; } = [];

    public Severity Worst => Findings.Count == 0 ? Severity.Good : Findings.Max(f => f.Level);

    public void Mark(int from, int to, Severity s)
    {
        for (int i = from; i < to; i++)
        {
            int k = ((i % StripSamples) + StripSamples) % StripSamples;
            if (Strip[k] < s) Strip[k] = s;
        }
    }

    /// <summary>Mark the angles (radians of input) where <paramref name="bad"/> says so.</summary>
    public void MarkWhere(Func<double, bool> bad, Severity s)
    {
        for (int i = 0; i < StripSamples; i++)
            if (bad(Cycle * i / StripSamples) && Strip[i] < s) Strip[i] = s;
    }
}

/// <summary>An axle or shaft: a round dowel or rod, or a square one.</summary>
public sealed record Axle
{
    public double SizeMm { get; init; } = 6;
    public bool Square { get; init; }

    /// <summary>A hole the part turns freely in.</summary>
    public Path64 TurningHole(Vec2 at) => Poly.Circle(at, (Square ? SizeMm * Math.Sqrt(2) : SizeMm) / 2 + 0.15, 0.003);

    /// <summary>A hole that grips the axle (glue it too): a square one for a square axle, turned to <paramref name="angle"/>.</summary>
    public Path64 HeldHole(Vec2 at, double angle = 0)
    {
        if (!Square) return Poly.Circle(at, SizeMm / 2 - 0.03, 0.003);
        double h = SizeMm / 2 + 0.03;
        var corners = new[] { new Vec2(-h, -h), new Vec2(h, -h), new Vec2(h, h), new Vec2(-h, h) };
        var p = new Pose(at, angle);
        return Poly.Path(corners.Select(p.Apply));
    }

    public string Words => Square ? $"{SizeMm:0.#} mm square" : $"{SizeMm:0.#} mm";

    public Shaft Shaft => new(Square ? HoleShape.Square : HoleShape.Round, SizeMm, 0);
}

/// <summary>
/// What every mechanism is made from: the sheet's thickness and how snug its slots are (both from the
/// material library when the cut settings there have them: the kerf gauge measures them).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GearTrainSpec), "gears")]
[JsonDerivedType(typeof(RackSpec), "rack")]
[JsonDerivedType(typeof(PlanetarySpec), "planetary")]
[JsonDerivedType(typeof(CamSpec), "cam")]
[JsonDerivedType(typeof(AutomatonSpec), "automaton")]
public abstract record MechSpec
{
    /// <summary>Sheet thickness (mm).</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Slots for joints are the thickness plus this (mm).</summary>
    public double SlotFitMm { get; init; } = 0.1;
    /// <summary>The material library's category for the sheet ("Wood", "Acrylic"…; empty = wood): how tight each hole fit is drawn.</summary>
    public string Material { get; init; } = "";

    public abstract Mechanism Build(CancellationToken ct = default);

    public string ToJson() => JsonSerializer.Serialize<MechSpec>(this, AddonJson.Options);

    public static MechSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<MechSpec>(json, AddonJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>
/// The JSON settings for everything this add-on saves. They have a resolver of their own, so they never
/// share a cache with the program's JSON settings (or the shared default ones): that cache would keep
/// this add-on's types, and so the add-on, in memory after it's turned off.
/// </summary>
static class AddonJson
{
    public static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
}

/// <summary>Colours for the pictures (0xRRGGBB).</summary>
public static class Tints
{
    public static readonly uint[] Parts = [0x4F8FD8, 0xE58A3A, 0x4DB37E, 0xB36AD8, 0xD8B23F, 0x3FB8C2, 0xD86A8C, 0x8A9A4F];
    public const uint Frame = 0xB8A07E;
    public const uint Steel = 0x9AA3AD;
    public const uint Problem = 0xE5484D;
    public static uint Of(int i) => Parts[((i % Parts.Length) + Parts.Length) % Parts.Length];
}

/// <summary>Names and lists in sentences.</summary>
public static class Say
{
    /// <summary>
    /// "Gear A (12 teeth)" → "gear A (12 teeth)", "A shaft…" → "a shaft…": the first letter only, and not
    /// in a name like "UV".
    /// </summary>
    public static string Lower(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && (char.IsLower(s[1]) || s.StartsWith("A ", StringComparison.Ordinal)) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string And(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}
