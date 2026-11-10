using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// The shape across a band (its cross-section), from the band's outer side to its inner side: what
/// a moulding plane or router bit would leave. Heights go from the band's <see cref="BorderBand.TopMm"/>
/// down by its <see cref="BorderBand.DropMm"/>.
/// </summary>
public enum BandProfile
{
    /// <summary>Level at the band's top.</summary>
    Flat,
    /// <summary>A straight slope down (a chamfer or bevel).</summary>
    Slope,
    /// <summary>A hollow quarter-round: drops steeply at first, then levels out.</summary>
    Cove,
    /// <summary>A rounded quarter-round (ovolo): level at first, then rounds down.</summary>
    RoundOver,
    /// <summary>An S curve, rounded over at the top and hollow at the bottom (cyma recta, the "Roman ogee").</summary>
    Ogee,
    /// <summary>An S curve, hollow at the top and rounded at the bottom (cyma reversa).</summary>
    ReverseOgee,
    /// <summary>A half-round bead standing up in the middle of the band.</summary>
    Bead,
    /// <summary>A low, wide rounded hump with a flatter top.</summary>
    Cushion,
    /// <summary>Level, then a tight round-over at the very edge (a table-top's thumbnail edge).</summary>
    Thumbnail,
    /// <summary>Several beads side by side (<see cref="BorderBand.Count"/>).</summary>
    Reeds,
    /// <summary>Several hollow grooves side by side (<see cref="BorderBand.Count"/>).</summary>
    Flutes,
    /// <summary>Equal steps down (<see cref="BorderBand.Count"/>), as in Art Deco.</summary>
    Steps,
    /// <summary>A V-shaped groove down the middle.</summary>
    VGroove,
}

/// <summary>A repeating carved pattern along a band. Each is drawn here as original art.</summary>
public enum Ornament
{
    None,
    EggAndDart,
    Dentil,
    BeadAndReel,
    Pearls,
    Rope,
    BarleyTwist,
    GreekKey,
    Fret,
    WaveScroll,
    Vitruvian,
    Guilloche,
    Plait,
    Acanthus,
    LeafAndDart,
    WaterLeaf,
    Palmette,
    Laurel,
    Rosettes,
    Shells,
    Chevron,
    Herringbone,
    Basketweave,
    Billet,
    Dogtooth,
    Nailhead,
    Gadroon,
    TwistedGadroon,
    FlutesAcross,
    Arcade,
    FishScale,
    IvyVine,
    Lozenge,
    Bamboo,
    RibbonAndStick,
    Quatrefoil,
    Hearts,
    Stars,
    Ripple,
    Waves,
    Hammered,
}

/// <summary>
/// One strip of a border's cross-section, running all the way along: its width, its shape across
/// (<see cref="Profile"/>) and the pattern carved into it (<see cref="Ornament"/>). Bands are listed
/// from the border's outer edge to its inner edge.
/// </summary>
public sealed record BorderBand
{
    /// <summary>How wide the band is, across the border (mm).</summary>
    public double WidthMm { get; init; } = 10;
    /// <summary>The band's highest level, above the deepest point of the whole border (mm).</summary>
    public double TopMm { get; init; } = 2;
    public BandProfile Profile { get; init; } = BandProfile.Flat;
    /// <summary>How far the profile goes down from <see cref="TopMm"/> (mm): the cove's or ogee's fall, the bead's height.</summary>
    public double DropMm { get; init; }
    /// <summary>Reeds, flutes or steps across the band (profiles that have several).</summary>
    public int Count { get; init; } = 3;
    public Ornament Ornament { get; init; } = Ornament.None;
    /// <summary>How deep the ornament is carved into the band's surface (mm).</summary>
    public double CarveMm { get; init; } = 1;
    /// <summary>The length of one repeat of the ornament (mm); 0 = its natural length for the band's width.</summary>
    public double RepeatMm { get; init; }
    /// <summary>Turned round across: the profile falls toward the outer side, and the ornament points the other way.</summary>
    public bool Flip { get; init; }

    /// <summary>The lowest this band goes (mm).</summary>
    [JsonIgnore]
    public double LowestMm => TopMm - (Profile == BandProfile.Flat ? 0 : Math.Max(0, DropMm)) - (Ornament == Ornament.None ? 0 : Math.Max(0, CarveMm));
}

/// <summary>A border's design: its bands from the outer edge in, and what it's called.</summary>
public sealed record BorderDesign
{
    public string Name { get; init; } = "Border";
    /// <summary>Picture frames, Cabinet and furniture edges, Ornament bands, or My designs.</summary>
    public string Category { get; init; } = BorderPresets.MyDesigns;
    /// <summary>A line saying what it is and where it's used.</summary>
    public string About { get; init; } = "";
    public IReadOnlyList<BorderBand> Bands { get; init; } = [];

    /// <summary>The border's width as designed (mm).</summary>
    [JsonIgnore]
    public double WidthMm => Bands.Sum(b => Math.Max(0, b.WidthMm));
    /// <summary>The highest level: the surface of the material (white in the depth map).</summary>
    [JsonIgnore]
    public double TopMm => Bands.Count == 0 ? 0 : Bands.Max(b => b.TopMm);
    /// <summary>The deepest level (black in the depth map).</summary>
    [JsonIgnore]
    public double LowestMm => Bands.Count == 0 ? 0 : Math.Min(Bands.Min(b => b.LowestMm), TopMm);
    /// <summary>How deep the deepest part is carved below the surface (mm).</summary>
    [JsonIgnore]
    public double DepthMm => TopMm - LowestMm;

    /// <summary>The same design made <paramref name="widthMm"/> wide and <paramref name="depthMm"/> deep (0 keeps it as it is).</summary>
    public BorderDesign Scaled(double widthMm, double depthMm)
    {
        double kw = widthMm > 0 && WidthMm > 0 ? widthMm / WidthMm : 1;
        double kd = depthMm > 0 && DepthMm > 0 ? depthMm / DepthMm : 1;
        double low = LowestMm;
        return this with
        {
            Bands = [.. Bands.Select(b => b with
            {
                WidthMm = b.WidthMm * kw,
                RepeatMm = b.RepeatMm * kw,
                TopMm = (b.TopMm - low) * kd,
                DropMm = b.DropMm * kd,
                CarveMm = b.CarveMm * kd,
            })],
        };
    }
}

/// <summary>What the border goes round.</summary>
public enum BorderLayout
{
    /// <summary>A straight length: a cabinet edge, a shelf front, trim.</summary>
    Strip,
    /// <summary>A rectangle all the way round: a picture frame, a mirror, a door panel.</summary>
    Frame,
    /// <summary>An oval or a circle all the way round.</summary>
    Oval,
    /// <summary>
    /// Wrapped round a round part turning in the rotary (a pen, a tool handle, a table leg): the picture is the
    /// part unrolled, its length along the axis by once round, and the pattern meets itself where it joins.
    /// </summary>
    Rotary,
}

/// <summary>Which way a border wraps round a rotary part.</summary>
public enum WrapWay
{
    /// <summary>Rings: the border goes round the part, its width along the axis.</summary>
    Round,
    /// <summary>Flutes: copies of the border run along the part, side by side all the way round.</summary>
    Along,
    /// <summary>A spiral (one or more starts) winding along the part, as a barley twist or rope does.</summary>
    Spiral,
}

/// <summary>How a rectangular frame's corners are made.</summary>
public enum CornerStyle
{
    /// <summary>The sides meet on the diagonal, as a mitred frame's sticks do.</summary>
    Mitred,
    /// <summary>A square block with a rosette in each corner, the sides butting into it.</summary>
    Blocks,
    /// <summary>The pattern bends round rounded corners.</summary>
    Rounded,
}

/// <summary>What happens inside a frame (the opening).</summary>
public enum MiddleFill
{
    /// <summary>Left as it is: not carved (the picture is see-through there).</summary>
    Untouched,
    /// <summary>Level with the border's inner edge: a sunk panel in a door or a lid.</summary>
    InnerEdge,
    /// <summary>Carved down to the deepest level.</summary>
    Floor,
}

/// <summary>Everything that makes one border: the design, what it goes round and how big, and the picture's settings.</summary>
public sealed record BorderSpec
{
    public BorderDesign Design { get; init; } = BorderPresets.Default;
    public BorderLayout Layout { get; init; } = BorderLayout.Frame;
    /// <summary>The border's width across (mm); 0 = as designed.</summary>
    public double WidthMm { get; init; }
    /// <summary>How deep the deepest part is carved (mm); 0 = as designed.</summary>
    public double DepthMm { get; init; }

    /// <summary>A strip's length along its outer edge (mm).</summary>
    public double LengthMm { get; init; } = 300;
    /// <summary>A strip's ends cut at 45° (the outer edge longest), to meet another strip at a corner.</summary>
    public bool MitredEnds { get; init; }

    /// <summary>A frame's width (mm): the outside, or the opening with <see cref="SizeIsOpening"/>.</summary>
    public double FrameWidthMm { get; init; } = 180;
    /// <summary>A frame's height (mm): the outside, or the opening with <see cref="SizeIsOpening"/>.</summary>
    public double FrameHeightMm { get; init; } = 220;
    /// <summary>The size given is the opening (the picture), not the outside.</summary>
    public bool SizeIsOpening { get; init; }
    public CornerStyle Corners { get; init; } = CornerStyle.Mitred;
    /// <summary>The outside corners' radius for <see cref="CornerStyle.Rounded"/> (mm).</summary>
    public double CornerRadiusMm { get; init; } = 30;
    /// <summary>A rectangular, mitred frame made as four separate sticks to engrave one by one and glue up.</summary>
    public bool FourPieces { get; init; }
    public MiddleFill Middle { get; init; } = MiddleFill.Untouched;

    /// <summary>A pattern's motif sits on each corner (or a strip's ends), else the gap between two does.</summary>
    public bool MotifOnCorners { get; init; }
    /// <summary>Each side gets a whole motif in its middle (the number of repeats is made odd or even to suit).</summary>
    public bool CentreEachSide { get; init; } = true;

    /// <summary>Pixels per mm in the depth map.</summary>
    public double PixelsPerMm { get; init; } = 10;
    /// <summary>A blur over the heights (mm): rounds off sharp walls.</summary>
    public double SoftenMm { get; init; }
    /// <summary>Also put cut lines round the outside and the opening.</summary>
    public bool CutLines { get; init; }
    /// <summary>A strip longer than this is made in pieces that carry on from each other (mm); 0 = one piece.</summary>
    public double MaxPieceMm { get; init; }

    // ---- round a rotary part ----

    /// <summary>The round part's diameter (mm): the pattern goes once round it at this size (Machine → Work mode's Object Ø).</summary>
    public double PartDiameterMm { get; init; } = 25;
    public WrapWay Wrap { get; init; } = WrapWay.Round;
    /// <summary>How many: rings along the part (<see cref="WrapWay.Round"/>), copies side by side round it (<see cref="WrapWay.Along"/>), or starts of a spiral.</summary>
    public int WrapCopies { get; init; } = 1;
    /// <summary>The gap between rings along the part (mm).</summary>
    public double WrapGapMm { get; init; } = 5;
    /// <summary>How far along the part the flutes or the spiral run (mm).</summary>
    public double WrapLengthMm { get; init; } = 100;
    /// <summary>Each copy is the design and its mirror image side by side, their inner edges meeting in the middle.</summary>
    public bool WrapMirrored { get; init; }
    /// <summary>Turned round across: the design's outer edge on the other side.</summary>
    public bool WrapTurned { get; init; }
    /// <summary>The spiral winds the other way (left-handed, as a left-hand thread).</summary>
    public bool WrapLeftHand { get; init; }

    /// <summary>Once round the part (mm).</summary>
    [JsonIgnore]
    public double PartCircumferenceMm => Math.PI * PartDiameterMm;

    /// <summary>Copies wrapped side by side round the part: each one's width is fitted to go round a whole number of times.</summary>
    [JsonIgnore]
    bool WidthFitted => Layout == BorderLayout.Rotary && Wrap == WrapWay.Along;

    /// <summary>The design at the width and depth asked for.</summary>
    public BorderDesign Sized() => Design.Scaled(WidthFitted ? BorderWidth : WidthMm, DepthMm);

    /// <summary>The border's width across, as it will be made (mm). Flutes round a rotary part are fitted to go round.</summary>
    public double BorderWidth => WidthFitted
        ? PartCircumferenceMm / Math.Max(1, WrapCopies) / (WrapMirrored ? 2 : 1)
        : WidthMm > 0 ? WidthMm : Design.WidthMm;

    /// <summary>A frame's outside size (mm).</summary>
    public (double W, double H) Outside()
    {
        double add = SizeIsOpening ? 2 * BorderWidth : 0;
        return (Math.Max(0, FrameWidthMm) + add, Math.Max(0, FrameHeightMm) + add);
    }

    /// <summary>What's wrong with these settings, in a sentence, or null.</summary>
    public string? Problem()
    {
        var d = Design;
        if (d.Bands.Count == 0) return "The design has no bands. Add one on the Bands tab.";
        if (d.Bands.Any(b => !(b.WidthMm > 0))) return "Every band needs a width more than 0.";
        if (!(d.DepthMm > 0) && !(DepthMm > 0)) return "The design is flat: give a band some drop or carving, or a different top.";
        double w = BorderWidth;
        if (!(w >= 1)) return "Make the border at least 1 mm wide.";
        if (!(PixelsPerMm >= 1 && PixelsPerMm <= 40)) return "Pixels per mm goes from 1 to 40.";
        switch (Layout)
        {
            case BorderLayout.Strip:
                if (!(LengthMm >= 5)) return "Make the strip at least 5 mm long.";
                if (MitredEnds && LengthMm < 2 * w + 1) return $"With mitred ends the strip must be longer than twice its width ({2 * w:0.#} mm).";
                break;
            case BorderLayout.Rotary:
                return RotaryProblem();
            default:
                var (ow, oh) = Outside();
                if (!(ow >= 5 && oh >= 5)) return "Give the frame a width and a height.";
                if (Math.Min(ow, oh) < 2 * w + 1) return $"The border ({w:0.#} mm) is too wide for a frame that size: it needs more than {2 * w:0.#} mm each way.";
                break;
        }
        return null;
    }

    /// <summary>What's wrong with the rotary settings, or null.</summary>
    string? RotaryProblem()
    {
        if (!(PartDiameterMm >= 2)) return "Give the part's diameter: at least 2 mm.";
        double c = PartCircumferenceMm, most = PhotonCanvas.Doc.WorkModes.MaxCircumferenceMm;
        if (c > most + 1e-9) return $"The rotary goes up to {most:0} mm round (Ø {most / Math.PI:0.#} mm).";
        if (WrapCopies < 1 || WrapCopies > 60) return Wrap switch
        {
            WrapWay.Round => "Make 1 to 60 rings.",
            WrapWay.Along => "Fit 1 to 60 copies round the part.",
            _ => "A spiral has 1 to 60 starts.",
        };
        if (Wrap != WrapWay.Round && !(WrapLengthMm >= 5)) return "Make it at least 5 mm long along the part.";
        if (Wrap == WrapWay.Round && !(WrapGapMm >= 0)) return "The gap between rings can't be less than 0.";
        if (Wrap == WrapWay.Spiral)
        {
            double across = WrapCopies * BorderWidth * (WrapMirrored ? 2 : 1);
            if (across > 0.95 * c)
                return $"Too wide to spiral: {WrapCopies} × {BorderWidth * (WrapMirrored ? 2 : 1):0.#} mm across is more than once round the part ({c:0.#} mm). Use fewer starts or a narrower border (or Along the part).";
        }
        return null;
    }

    // ---- saving ----

    // Its own resolver, so the program's shared JSON cache never holds this add-on's types (docs/addons.md).
    static readonly JsonSerializerOptions Json = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A spec read back, or null when the text isn't one.</summary>
    public static BorderSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<BorderSpec>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    internal static T? Read<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}
