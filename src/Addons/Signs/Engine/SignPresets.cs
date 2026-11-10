using PhotonCanvas.Addon.Borders;

namespace PhotonCanvas.Addon.Signs;

/// <summary>A ready-made sign: what it is for, and every setting it starts with.</summary>
public sealed record SignPreset(string Name, string About, SignSpec Spec);

/// <summary>The signs the maker comes with, all small enough for a 220 mm bed.</summary>
public static class SignPresets
{
    static BorderDesign? B(string name) => BorderPresets.All.FirstOrDefault(d => d.Name == name);

    static SignPreset P(string name, string about, SignSpec spec) => new(name, about, spec with { Name = name });

    public static readonly IReadOnlyList<SignPreset> All =
    [
        P("House name", "An oval house sign: the name V-carved inside a rope border.",
            new SignSpec
            {
                Shape = SignShape.Oval, WidthMm = 210, HeightMm = 130, Border = B("Rope twist"), BorderWidthMm = 11, BorderDepthMm = 2.2,
                Lines = [new("Rose Cottage")], Font = "Georgia", Italic = true, Style = LetterStyle.VCarved, LetterDepthMm = 2.5, VAngleDeg = 60,
            }),
        P("House number", "A bold number standing up from a hammered panel, with an ogee edge.",
            new SignSpec
            {
                Shape = SignShape.Rounded, WidthMm = 150, HeightMm = 110, CornerMm = 14, Border = B("Roman ogee"), BorderWidthMm = 10, BorderDepthMm = 2.5,
                Lines = [new("27")], Font = "Georgia", Bold = true, Style = LetterStyle.Raised, LetterDepthMm = 2.5, Edge = LetterEdge.Chamfer, EdgeMm = 0.8,
                Texture = Background.Hammered, TextureMm = 0.4, Holes = SignHoles.Sides,
            }),
        P("Arched house sign", "An arch-topped sign: the name curving under the arch and a date below, V-carved.",
            new SignSpec
            {
                Shape = SignShape.Arched, WidthMm = 210, HeightMm = 140, ArchRiseMm = 48, CornerMm = 14, Border = B("Classic gilt"), BorderWidthMm = 14, BorderDepthMm = 2.4,
                Lines = [new("The Old Forge", 100, 32), new("Est. 1892", 55)], Font = "Georgia", Style = LetterStyle.VCarved, LetterDepthMm = 2.2, VAngleDeg = 60,
                Holes = SignHoles.TopCorners,
            }),
        P("Memorial plaque", "A plaque with three lines sunk into it, inside a Louis XVI frame.",
            new SignSpec
            {
                Shape = SignShape.Rectangle, WidthMm = 200, HeightMm = 130, Border = B("Louis XVI"), BorderWidthMm = 15, BorderDepthMm = 2.4,
                Lines = [new("In loving memory of", 55), new("Margaret Hale"), new("1931 – 2019", 55)], Font = "Times New Roman",
                Style = LetterStyle.Sunk, LetterDepthMm = 1.6, Edge = LetterEdge.Chamfer, EdgeMm = 0.5, LineGapPct = 40,
            }),
        P("Welcome", "A long sign with round ends: domed letters and a flower either side.",
            new SignSpec
            {
                Shape = SignShape.RoundEnds, WidthMm = 210, HeightMm = 80, Border = B("Cock bead"), BorderWidthMm = 7, BorderDepthMm = 1.8,
                Lines = [new("Welcome")], Font = "Georgia", Italic = true, Style = LetterStyle.Domed, LetterDepthMm = 2.2, EdgeMm = 2.2,
                Decoration = Decoration.Flower, Texture = Background.Smooth,
            }),
        P("Street name", "Bold capitals standing up from a stippled panel, inside a plain bead.",
            new SignSpec
            {
                Shape = SignShape.Rectangle, WidthMm = 210, HeightMm = 60, Border = B("Cock bead"), BorderWidthMm = 6, BorderDepthMm = 1.6,
                Lines = [new("OAK LANE")], Font = "Georgia", Bold = true, LetterSpacingPct = 12, Style = LetterStyle.Raised, LetterDepthMm = 2,
                Edge = LetterEdge.Square, Texture = Background.Stippled, TextureMm = 0.35, MarginMm = 4, Holes = SignHoles.Sides,
            }),
        P("Door plaque", "A small round-ended plaque with sunk, rounded-in letters.",
            new SignSpec
            {
                Shape = SignShape.RoundEnds, WidthMm = 150, HeightMm = 50, Border = B("Round-over"), BorderWidthMm = 5, BorderDepthMm = 1.5,
                Lines = [new("Office")], Font = "Georgia", Style = LetterStyle.Sunk, LetterDepthMm = 1.5, Edge = LetterEdge.Rounded, EdgeMm = 0.6, MarginMm = 4,
            }),
        P("Pet name", "A round sign with the name arched over a heart and a line curving under it.",
            new SignSpec
            {
                Shape = SignShape.Circle, WidthMm = 160, HeightMm = 160, Border = B("Ivy"), BorderWidthMm = 14, BorderDepthMm = 2,
                Lines = [new("Biscuit", 100, 60), new("good dog", 50, -40)], Font = "Georgia", Bold = true, Style = LetterStyle.Domed, LetterDepthMm = 2, EdgeMm = 1.6,
                Decoration = Decoration.Heart, LineGapPct = 60, Holes = SignHoles.TopMiddle,
            }),
        P("Workshop", "Chiselled capitals on a cut-corner board, a rule under the name.",
            new SignSpec
            {
                Shape = SignShape.CutCorners, WidthMm = 210, HeightMm = 110, CornerMm = 16, Border = B("Rope edge"), BorderWidthMm = 8, BorderDepthMm = 2,
                Lines = [new("WORKSHOP"), new("EST. 2026", 45)], Font = "Georgia", Bold = true, LetterSpacingPct = 8, Style = LetterStyle.Chiselled,
                LetterDepthMm = 2.4, VAngleDeg = 70, Rule = true, Holes = SignHoles.FourCorners, Countersink = true,
            }),
        P("Garden sign", "Raised letters over fine level lines, a leaf either side, under an arch.",
            new SignSpec
            {
                Shape = SignShape.Arched, WidthMm = 190, HeightMm = 110, ArchRiseMm = 36, CornerMm = 10, Border = B("Reeded edge"), BorderWidthMm = 8, BorderDepthMm = 1.8,
                Lines = [new("Herb Garden")], Font = "Georgia", Italic = true, Style = LetterStyle.Raised, LetterDepthMm = 2, Edge = LetterEdge.Rounded, EdgeMm = 0.7,
                Texture = Background.Lines, TextureMm = 0.3, Decoration = Decoration.Leaf,
            }),
        P("Gate sign", "Two lines V-carved on a rounded board, a star either side of the first.",
            new SignSpec
            {
                Shape = SignShape.Rounded, WidthMm = 200, HeightMm = 85, CornerMm = 10, Border = B("Chamfer"), BorderWidthMm = 6, BorderDepthMm = 1.5,
                Lines = [new("Please close"), new("the gate")], Font = "Georgia", Style = LetterStyle.VCarved, LetterDepthMm = 2, VAngleDeg = 60,
                Decoration = Decoration.Star, Holes = SignHoles.Sides,
            }),
        P("Plain plaque", "Just the words V-carved on a plain board: no border.",
            new SignSpec
            {
                Shape = SignShape.Rectangle, WidthMm = 180, HeightMm = 90, Border = null,
                Lines = [new("Your words here")], Font = "Georgia", Style = LetterStyle.VCarved, LetterDepthMm = 2, VAngleDeg = 60, MarginMm = 10,
            }),
    ];

    public static SignSpec Default => All[0].Spec;
}
