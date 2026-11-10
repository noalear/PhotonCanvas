using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>What kind of box.</summary>
public enum BoxStyle
{
    /// <summary>Flat panels with finger joints at every corner.</summary>
    Finger,
    /// <summary>A bottom and one long wall bent round it: the corners rounded on living hinges.</summary>
    Rounded,
    /// <summary>A finger-jointed box whose back goes on over the top as the lid, bending on a living hinge.</summary>
    BendingLid,
}

/// <summary>The lid of a finger-jointed or rounded box.</summary>
public enum LidKind
{
    /// <summary>No lid: an open tray.</summary>
    None,
    /// <summary>A lid that lifts off, with a lip under it that keeps it in place.</summary>
    LiftOff,
    /// <summary>A lid that slides out to the front between runners and a cap.</summary>
    Sliding,
    /// <summary>A lid that swings up on a pin through the sides near the back.</summary>
    Hinged,
}

/// <summary>Everything a box is made from; saved with the box so it can be opened again and changed.</summary>
public sealed record BoxSpec
{
    public BoxStyle Style { get; init; } = BoxStyle.Finger;
    public double WidthMm { get; init; } = 100;
    public double DepthMm { get; init; } = 70;
    public double HeightMm { get; init; } = 45;
    /// <summary>The sizes are the outside of the box (else the space inside).</summary>
    public bool Outside { get; init; }
    public double ThicknessMm { get; init; } = 3;
    /// <summary>How wide the fingers are (0: about three times the sheet).</summary>
    public double FingerMm { get; init; }
    /// <summary>Play in every joint, on top of the kerf (positive looser, negative tighter).</summary>
    public double FitMm { get; init; }
    public LidKind Lid { get; init; } = LidKind.LiftOff;
    /// <summary>The lip under a lift-off lid, or the runners and cap of a sliding lid: how far in from the walls (mm).</summary>
    public double LipMm { get; init; } = 6;
    /// <summary>The hinge pin's diameter (a dowel, a bamboo skewer, a nail or a length of filament).</summary>
    public double PinMm { get; init; } = 3;
    /// <summary>Dividers running front to back (they split the width).</summary>
    public int DividersAcross { get; init; }
    /// <summary>Dividers running side to side (they split the depth).</summary>
    public int DividersAlong { get; init; }
    /// <summary>A rounded box's corners, inside (mm).</summary>
    public double CornerRadiusMm { get; init; } = 15;
    /// <summary>Where a bending lid goes over the back, inside (mm).</summary>
    public double BendRadiusMm { get; init; } = 10;
    /// <summary>The living hinge's rows apart, cuts and gaps between them (0: worked out from the sheet).</summary>
    public double HingeSpacingMm { get; init; }
    public double HingeCutMm { get; init; }
    public double HingeGapMm { get; init; }
    /// <summary>A half-round notch at the top of the front, to get a finger under a hinged or bending lid.</summary>
    public bool FingerNotch { get; init; } = true;
    /// <summary>How pieces too long for the bed are cut and joined back together.</summary>
    public JoinKind SeamJoin { get; init; } = JoinKind.Dovetail;
    /// <summary>A carved border on the lid (made by the border maker).</summary>
    public bool Border { get; init; }
    public string BorderDesign { get; init; } = "";
    public double BorderWidthMm { get; init; } = 10;
    public double BorderDepthMm { get; init; } = 1.5;
    /// <summary>How far in from the lid's edge the border starts (mm).</summary>
    public double BorderInsetMm { get; init; } = 4;

    /// <summary>The finger width used (the one asked for, or three times the sheet).</summary>
    public double Finger => FingerMm > 0 ? FingerMm : Math.Clamp(3 * ThicknessMm, 4, 20);

    /// <summary>The lid this style of box has (a bending-lid box's is part of it; a rounded box's lifts off or there's none).</summary>
    public LidKind LidUsed => Style switch
    {
        BoxStyle.BendingLid => LidKind.None,
        BoxStyle.Rounded => Lid == LidKind.None ? LidKind.None : LidKind.LiftOff,
        _ => Lid,
    };

    /// <summary>Has the box got a lid to carve a border on.</summary>
    public bool HasLid => Style == BoxStyle.BendingLid || LidUsed != LidKind.None;

    public string ToJson() => JsonSerializer.Serialize(this, BoxJson.Options);

    public static BoxSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<BoxSpec>(json, BoxJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class BoxJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
