using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// What every toy is made from: the sheet's thickness, how snug its slots are, and the material library's
/// category for it (how tight holes are drawn). Saved as JSON on the parts so a toy can be opened and changed.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FigureBoxSpec), "figures")]
[JsonDerivedType(typeof(StripSpec), "strip")]
public abstract record ToySpec
{
    /// <summary>Sheet thickness (mm).</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Slots for joints are the thickness plus this (mm).</summary>
    public double SlotFitMm { get; init; } = 0.1;
    /// <summary>The material library's category for the sheet ("Wood", "Acrylic"…; empty = wood).</summary>
    public string Material { get; init; } = "";

    public abstract Mech Build(CancellationToken ct = default);

    public string ToJson() => JsonSerializer.Serialize<ToySpec>(this, ToyJson.Options);

    public static ToySpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ToySpec>(json, ToyJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>
/// The JSON settings for everything this add-on saves, with a resolver of their own so they never share a
/// cache with the program's (which would keep this add-on in memory after it's turned off).
/// </summary>
static class ToyJson
{
    public static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
}
