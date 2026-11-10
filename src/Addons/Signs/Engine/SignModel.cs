using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Borders;

namespace PhotonCanvas.Addon.Signs;

/// <summary>The sign's outline.</summary>
public enum SignShape
{
    /// <summary>Square corners.</summary>
    Rectangle,
    /// <summary>Rounded corners (<see cref="SignSpec.CornerMm"/> is the radius).</summary>
    Rounded,
    /// <summary>The corners cut off at 45° (<see cref="SignSpec.CornerMm"/> along each side).</summary>
    CutCorners,
    /// <summary>An oval (four arcs, as a sign maker draws one).</summary>
    Oval,
    /// <summary>A circle, <see cref="SignSpec.WidthMm"/> across.</summary>
    Circle,
    /// <summary>Straight sides and bottom, an arch over the top with rounded shoulders.</summary>
    Arched,
    /// <summary>Straight top and bottom with half-round ends.</summary>
    RoundEnds,
}

/// <summary>How the words are carved.</summary>
public enum LetterStyle
{
    /// <summary>A V groove: deepest down the middle of each stroke, as a V bit leaves it.</summary>
    VCarved,
    /// <summary>Carved down into the board with a flat floor.</summary>
    Sunk,
    /// <summary>The letters stand up: the panel round them is carved away.</summary>
    Raised,
    /// <summary>Raised letters rounded over like cushions.</summary>
    Domed,
    /// <summary>Raised letters with a ridge down each stroke, like a V carving turned inside out.</summary>
    Chiselled,
}

/// <summary>The edge of sunk or raised letters.</summary>
public enum LetterEdge
{
    /// <summary>Straight down.</summary>
    Square,
    /// <summary>A straight bevel.</summary>
    Chamfer,
    /// <summary>A rounded-over edge.</summary>
    Rounded,
}

/// <summary>The carved-away panel round raised letters.</summary>
public enum Background
{
    Smooth,
    /// <summary>Soft dents, like beaten metal.</summary>
    Hammered,
    /// <summary>Little pits all over, as a carver's punch leaves them.</summary>
    Stippled,
    /// <summary>Fine level grooves.</summary>
    Lines,
}

/// <summary>Where the panel inside the border sits.</summary>
public enum PanelLevel
{
    /// <summary>At the surface of the board.</summary>
    Surface,
    /// <summary>Level with the border's inner edge (sunk, when the border falls toward the middle).</summary>
    BorderInnerEdge,
}

/// <summary>A small carved motif either side of the first line.</summary>
public enum Decoration { None, Star, Heart, Diamond, Leaf, Flower, Dot }

/// <summary>Screw holes to put the sign up with.</summary>
public enum SignHoles
{
    None,
    /// <summary>One each side, half way up.</summary>
    Sides,
    /// <summary>One in each top corner.</summary>
    TopCorners,
    /// <summary>One in each corner.</summary>
    FourCorners,
    /// <summary>One at the top in the middle, to hang it from a nail.</summary>
    TopMiddle,
}

/// <summary>One line of words, its size against the others, and how much it curves.</summary>
public sealed record SignLine
{
    public string Text { get; init; } = "";
    /// <summary>Its letters' size against a line at 100%.</summary>
    public double SizePct { get; init; } = 100;
    /// <summary>How many degrees of a circle the line spans: + arches up, − curves down (a smile), 0 straight.</summary>
    public double BendDeg { get; init; }

    public SignLine() { }
    public SignLine(string text, double sizePct = 100, double bendDeg = 0) { Text = text; SizePct = sizePct; BendDeg = bendDeg; }
}

/// <summary>Everything that makes one sign.</summary>
public sealed record SignSpec
{
    /// <summary>The ready-made sign it started from (for the gallery and the layer's name).</summary>
    public string Name { get; init; } = "Sign";

    // ---- the shape ----
    public SignShape Shape { get; init; } = SignShape.Rounded;
    /// <summary>Outside width (mm); a circle's diameter.</summary>
    public double WidthMm { get; init; } = 200;
    /// <summary>Outside height (mm); ignored for a circle.</summary>
    public double HeightMm { get; init; } = 110;
    /// <summary>The corner radius (rounded, the arch's shoulders) or how much is cut off each corner (mm).</summary>
    public double CornerMm { get; init; } = 14;
    /// <summary>An arched sign: how far the top of the arch is above where the straight sides start (mm).</summary>
    public double ArchRiseMm { get; init; } = 30;

    // ---- the border ----
    /// <summary>The carved border round the edge (from the border maker), or null for none.</summary>
    public BorderDesign? Border { get; init; } = BorderPresets.All.FirstOrDefault(d => d.Name == "Rope twist");
    /// <summary>The border's width across (mm); 0 = as designed.</summary>
    public double BorderWidthMm { get; init; } = 12;
    /// <summary>How deep the border's deepest part is (mm); 0 = as designed.</summary>
    public double BorderDepthMm { get; init; } = 2;
    /// <summary>A plain flat strip between the sign's edge and the border (mm).</summary>
    public double EdgeMarginMm { get; init; }
    public PanelLevel Panel { get; init; } = PanelLevel.Surface;

    // ---- the words ----
    public IReadOnlyList<SignLine> Lines { get; init; } = [new("Rose Cottage")];
    public string Font { get; init; } = "Georgia";
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    /// <summary>Extra space between letters, as % of the capitals' height.</summary>
    public double LetterSpacingPct { get; init; }
    /// <summary>Space between lines, as % of the capitals' height.</summary>
    public double LineGapPct { get; init; } = 35;
    /// <summary>Make the words as big as will fit inside the border (else <see cref="LetterHeightMm"/>).</summary>
    public bool FitWords { get; init; } = true;
    /// <summary>The capitals' height on a 100% line, when the words aren't fitted (mm).</summary>
    public double LetterHeightMm { get; init; } = 20;
    /// <summary>Space kept clear between the words and the border (mm).</summary>
    public double MarginMm { get; init; } = 5;
    /// <summary>Moves the words up (−) or down (+) from the middle (mm).</summary>
    public double OffsetYMm { get; init; }

    // ---- the lettering ----
    public LetterStyle Style { get; init; } = LetterStyle.VCarved;
    /// <summary>How deep the letters are carved (sunk, V) or how far they stand up (raised) (mm).</summary>
    public double LetterDepthMm { get; init; } = 2;
    /// <summary>The V's angle, for V-carved and chiselled letters (degrees).</summary>
    public double VAngleDeg { get; init; } = 60;
    public LetterEdge Edge { get; init; } = LetterEdge.Square;
    /// <summary>How wide the bevel or rounding on sunk and raised letters is, or a domed letter's rounding (mm).</summary>
    public double EdgeMm { get; init; } = 0.8;
    public Background Texture { get; init; } = Background.Smooth;
    /// <summary>How much the texture stands up from the carved-away panel (mm).</summary>
    public double TextureMm { get; init; } = 0.3;

    // ---- extras ----
    public Decoration Decoration { get; init; } = Decoration.None;
    /// <summary>A carved rule (a line with a diamond) under the first line.</summary>
    public bool Rule { get; init; }
    public SignHoles Holes { get; init; } = SignHoles.None;
    /// <summary>The screw holes' diameter (mm).</summary>
    public double HoleMm { get; init; } = 4.5;
    /// <summary>Countersink the screw holes (a 90° cone carved round each).</summary>
    public bool Countersink { get; init; } = true;

    // ---- output ----
    public double PixelsPerMm { get; init; } = 10;
    /// <summary>A blur over the heights (mm): rounds off sharp walls.</summary>
    public double SoftenMm { get; init; }
    /// <summary>Also put a cut line round the sign (screw holes are always cut).</summary>
    public bool CutLines { get; init; } = true;

    /// <summary>The words that will be carved: lines with something on them.</summary>
    [JsonIgnore]
    public IReadOnlyList<SignLine> Words => [.. Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))];

    /// <summary>The outside size (mm): a circle is as tall as it is wide.</summary>
    public (double W, double H) Size() => Shape == SignShape.Circle ? (WidthMm, WidthMm) : (WidthMm, HeightMm);

    /// <summary>The border at the width and depth asked for, or null for none.</summary>
    public BorderDesign? SizedBorder() => Border is { Bands.Count: > 0 } b ? b.Scaled(BorderWidthMm, BorderDepthMm) : null;

    /// <summary>The border's width as made (mm); 0 with none.</summary>
    [JsonIgnore]
    public double BorderWidth => Border is { Bands.Count: > 0 } b ? (BorderWidthMm > 0 ? BorderWidthMm : b.WidthMm) : 0;

    /// <summary>The countersink's diameter (mm): twice the hole, at least 4 mm more.</summary>
    [JsonIgnore]
    public double CountersinkMm => Math.Max(2 * HoleMm, HoleMm + 4);

    /// <summary>Do the letters stand up from a carved-away panel?</summary>
    [JsonIgnore]
    public bool RaisedLetters => Style is LetterStyle.Raised or LetterStyle.Domed or LetterStyle.Chiselled;

    /// <summary>What's wrong with these settings, in a sentence, or null.</summary>
    public string? Problem()
    {
        var (w, h) = Size();
        if (!(w >= 20 && h >= 15)) return "Make the sign at least 20 mm wide and 15 mm high.";
        if (!(w <= 3000 && h <= 3000)) return "Make the sign at most 3000 mm each way.";
        double inner = Math.Min(w, h) - 2 * (BorderWidth + Math.Max(0, EdgeMarginMm));
        if (inner < 8) return $"The border ({BorderWidth:0.#} mm) and edge leave no room for words: make the sign bigger or the border narrower.";
        if (Border is { } b && (b.Bands.Count == 0 || b.Bands.Any(x => !(x.WidthMm > 0)))) return "The border design has no bands.";
        if (!(LetterDepthMm > 0)) return "Give the letters a depth more than 0.";
        if (Style is LetterStyle.VCarved or LetterStyle.Chiselled && !(VAngleDeg >= 20 && VAngleDeg <= 150)) return "The V angle goes from 20° to 150°.";
        if (!(PixelsPerMm >= 1 && PixelsPerMm <= 40)) return "Pixels per mm goes from 1 to 40.";
        if (!FitWords && !(LetterHeightMm >= 1)) return "Make the letters at least 1 mm high.";
        if (Holes != SignHoles.None && !(HoleMm >= 1 && HoleMm <= 20)) return "Screw holes go from 1 to 20 mm across.";
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
    public static SignSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SignSpec>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}
