namespace PhotonCanvas.Addon.GameKit;

/// <summary>How the numbers, title and designs are burned: engraved (filled), outlined (scored round), or the letters as single lines.</summary>
public enum MarkLook { Engraved, Outlined, SingleLine }

/// <summary>How each distance is written.</summary>
public enum Numbering { Numbers, WithUnit, Roman, Pips, Tally, None }

/// <summary>A border round the marked face (the gauge's segments, the ruler's title band, the template's rim).</summary>
public enum BorderStyle { None, Line, Double, Dashed, Beads, Rope, Zigzag, Wave, GreekKey, Vine, Rivets, Hazard, Circuit, Stones, Arches, Art }

/// <summary>Where the ornament goes: at the ends (the gauge's and ruler's ends, round the template), each side of the title, or with every number.</summary>
public enum OrnamentPlace { Ends, Title, EveryNumber }

/// <summary>
/// A design you uploaded (from SVG, DXF, PDF or a picture), kept with the settings so the accessory
/// can be opened anywhere: its filled areas and lines in a box 1000 units on its longest side
/// (y up), as compact path text (<see cref="MarkArt.Areas"/>: "M x y L x y … Z", one loop each).
/// </summary>
public sealed record MarkArt
{
    public string Name { get; init; } = "Design";
    /// <summary>Closed loops (even-odd: a loop inside another is a hole).</summary>
    public string Areas { get; init; } = "";
    /// <summary>Open lines (the strokes of line art).</summary>
    public string Lines { get; init; } = "";
    /// <summary>Width over height.</summary>
    public double Aspect { get; init; } = 1;
}

/// <summary>
/// How a movement gauge, range ruler or area template is marked. Saved with the accessory; picking a
/// <see cref="Design"/> (<see cref="MarkDesigns"/>) sets the border, ornament and numbers together.
/// </summary>
public sealed record Markings
{
    /// <summary>The design last picked (shown as picked while nothing else was changed).</summary>
    public string Design { get; init; } = "Plain";
    /// <summary>The font: empty for the sheet's font, a font installed on the computer, or a handwriting font (<see cref="HandFonts.Prefix"/> + its name).</summary>
    public string Font { get; init; } = "";
    public bool Bold { get; init; } = true;
    public bool Italic { get; init; }
    public MarkLook Look { get; init; } = MarkLook.Engraved;
    /// <summary>The numbers' and title's size, times what fits (0.5 to 1.5).</summary>
    public double TextScale { get; init; } = 1;
    public Numbering Numbers { get; init; } = Numbering.Numbers;
    /// <summary>A number on every mark (1), every second (2)…; the gauge's numbers and the ruler's whole units.</summary>
    public int Every { get; init; } = 1;
    /// <summary>Words to etch on it ("MOVE", "6\" CHARGE", your army's name). Empty: none.</summary>
    public string Title { get; init; } = "";
    /// <summary>Write the unit on the ruler (inches, cm…).</summary>
    public bool ShowUnit { get; init; } = true;
    public BorderStyle Border { get; init; } = BorderStyle.None;
    /// <summary>A built-in ornament (<see cref="Ornaments.Names"/>), <see cref="Ornaments.ArtName"/> for <see cref="Art"/>, or empty for none.</summary>
    public string Ornament { get; init; } = "";
    public OrnamentPlace Place { get; init; } = OrnamentPlace.Ends;
    /// <summary>Your own design, as the ornament.</summary>
    public MarkArt? Art { get; init; }
    /// <summary>Your own design, repeated along the border (<see cref="BorderStyle.Art"/>).</summary>
    public MarkArt? BorderArt { get; init; }
    /// <summary>The ruler's finest ticks, as parts of a unit (1, 2, 4, 8, 16, 5 or 10); 0 for the usual (quarter inches, millimetres, half squares).</summary>
    public int Ticks { get; init; }
}

/// <summary>Ready-made designs: a border, an ornament and a way of numbering that go together, one for each terrain theme and a few more.</summary>
public static class MarkDesigns
{
    public sealed record Info(string Name, string About, BorderStyle Border, string Ornament, Numbering Numbers, OrnamentPlace Place = OrnamentPlace.Ends);

    public static readonly IReadOnlyList<Info> All =
    [
        new("Plain", "Just the numbers.", BorderStyle.None, "", Numbering.Numbers),
        new("Classic", "A double line round it and a diamond at each end.", BorderStyle.Double, "Diamond", Numbering.Numbers),
        new("Medieval", "A twisted rope border and a fleur-de-lis at each end.", BorderStyle.Rope, "Fleur-de-lis", Numbering.Numbers),
        new("Dungeon", "Iron rivets round it and a skull at each end.", BorderStyle.Rivets, "Skull", Numbering.Numbers),
        new("Cave", "A border of rough stones and crystals at the ends.", BorderStyle.Stones, "Crystals", Numbering.Numbers),
        new("Sci-fi", "Circuit traces round it and a targeting reticle at each end.", BorderStyle.Circuit, "Reticle", Numbering.Numbers),
        new("Desert", "A Greek key border and a sun at each end.", BorderStyle.GreekKey, "Sun", Numbering.Numbers),
        new("Gothic", "A row of pointed arches and a cross at each end.", BorderStyle.Arches, "Gothic cross", Numbering.Roman),
        new("Woodland", "A climbing vine and laurel sprigs.", BorderStyle.Vine, "Laurel", Numbering.Numbers),
        new("Adventurer", "Beads round it and a d20 at each end.", BorderStyle.Beads, "D20", Numbering.Numbers),
        new("March", "A dashed line and boot prints: a movement gauge's own look.", BorderStyle.Dashed, "Boot prints", Numbering.Numbers),
        new("Hazard", "Warning stripes round it and lightning bolts.", BorderStyle.Hazard, "Lightning", Numbering.Numbers),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(d => d.Name)];

    public static Info? Find(string? name) => All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The markings with a design's border, ornament and numbers (font, size, title and the rest kept).</summary>
    public static Markings Apply(Markings m, string name)
    {
        if (Find(name) is not { } d) return m;
        return m with { Design = d.Name, Border = d.Border, Ornament = d.Ornament, Numbers = d.Numbers, Place = d.Place };
    }

    /// <summary>The design that goes with a terrain theme.</summary>
    public static string ForTheme(string? theme) => theme switch
    {
        "Dungeon" => "Dungeon",
        "Cave" => "Cave",
        "Sci-fi" => "Sci-fi",
        "Desert" => "Desert",
        "Gothic" => "Gothic",
        _ => "Medieval",
    };

    /// <summary>Is the item marked (a gauge, ruler or template).</summary>
    public static bool Marked(AccessoryKind k) => k is AccessoryKind.MoveGauge or AccessoryKind.Ruler or AccessoryKind.Template;

    /// <summary>Roman numerals (1 to 3999; others as they are).</summary>
    public static string Roman(int n)
    {
        if (n <= 0 || n > 3999) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (int V, string S)[] t = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in t)
            while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }
}
