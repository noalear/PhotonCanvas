using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.BurnPreview;

/// <summary>Where a layer's look in the burn preview came from, best first.</summary>
public enum LookSource
{
    /// <summary>A square of a burned colour test with these settings, measured by the camera.</summary>
    ColourTest,
    /// <summary>The layer's tone test (its measured bare and darkest colours).</summary>
    ToneTest,
    /// <summary>The nearest square of a colour test, not these exact settings.</summary>
    NearestColourTest,
    /// <summary>Worked out from the energy the settings put in and the kind of material: a guess.</summary>
    Estimate,
}

/// <summary>
/// How one layer's marks will look: the colour of the mark next to the colour of the bare
/// material it was measured beside, and how strongly the settings reach it (0 = no mark at all,
/// 1 = the measured mark). The preview darkens or tints the camera's own picture of the material
/// by mark ÷ bare in linear light, so the grain and lighting of the real piece stay in.
/// </summary>
public sealed record BurnLook
{
    /// <summary>The mark's colour, 0xRRGGBB, as the camera saw it.</summary>
    public uint Mark { get; init; }
    /// <summary>The bare material beside it, 0xRRGGBB, under the same light.</summary>
    public uint Bare { get; init; }
    /// <summary>How fully the mark forms, 0 … 1.</summary>
    public double Strength { get; init; } = 1;
    /// <summary>Lines (cut, score): how wide the mark is, mm.</summary>
    public double LineWidthMm { get; init; } = 0.15;
    /// <summary>Cuts through wood-like material: a faint scorch either side of the cut, mm wide each side (0 = none).</summary>
    public double ScorchMm { get; init; }
    /// <summary>Cut layers: the part comes out, so the gap shows the bed beneath.</summary>
    public bool ThroughCut { get; init; }
    /// <summary>Pictures: the material's measured response (null = the greys come out as sent).</summary>
    public ToneCurve? Tone { get; init; }
    /// <summary>Pictures: their greys are corrected through <see cref="Tone"/> before sending.</summary>
    public bool Corrected { get; init; }
    public LookSource Source { get; init; } = LookSource.Estimate;
    /// <summary>One line for the user, e.g. "colour test 'SS304 Oct', row 3 column 5".</summary>
    public string About { get; init; } = "";

    /// <summary>A layer that makes no mark.</summary>
    public static readonly BurnLook None = new() { Mark = 0x808080, Bare = 0x808080, Strength = 0, About = "makes no mark" };
}

/// <summary>Works out each layer's <see cref="BurnLook"/> from the user's own tests, or a guess when there are none.</summary>
public static class BurnLooks
{
    /// <summary>Plain stand-ins for the bare material, by library category, when nothing was measured (0xRRGGBB).</summary>
    public static uint BareGuess(string? category) => category switch
    {
        "Wood" => 0xE3C79B,
        "Plywood & MDF" => 0xD8B98A,
        "Acrylic" => 0x2B2B30,
        "Leather" => 0xA8714A,
        "Paper & card" => 0xEDE6D6,
        "Fabric" => 0xC9C2B4,
        "Cork & rubber" => 0xB48A5E,
        "Stone & tile" => 0x3E4246,
        "Glass" => 0x55636B,
        "Metal" => 0xB4B6B8,
        "Coated metal" => 0x2E3B55,
        "Plastic" => 0xDADADA,
        "Food" => 0xC99A5B,
        _ => 0xD8B98A,
    };

    /// <summary>
    /// The look of <paramref name="layer"/>'s marks. Its library material (if any) gives the
    /// colour tests and the kind of material; its tone curve gives pictures and fills their colours.
    /// </summary>
    public static BurnLook For(Layer layer, MaterialLibrary? library)
    {
        var material = layer.Material is { } r ? library?.Find(r.MaterialId) : null;
        string category = material?.Category ?? "Wood";
        double width = LineWidth(layer);
        bool woody = Woody(category);
        bool through = layer.Mode == LayerMode.Cut;

        // Lines and fills: a colour test square with these settings (MOPA colours on metal).
        if (layer.Mode is LayerMode.Fill or LayerMode.Score && material is { Palettes.Count: > 0 } && Nearest(layer, material) is { } hit)
        {
            var (palette, swatch, distance) = hit;
            var (r8, g8, b8) = ColourScience.LabToSrgb(swatch.Colour);
            var (br, bg, bb) = ColourScience.LabToSrgb(palette.Bare);
            bool exact = distance < 0.01;
            if (exact || distance < NearEnough)
                return new BurnLook
                {
                    Mark = Rgb(r8, g8, b8), Bare = Rgb(br, bg, bb), LineWidthMm = width,
                    Source = exact ? LookSource.ColourTest : LookSource.NearestColourTest,
                    About = $"{(exact ? "colour test" : "nearest square of colour test")} \"{palette.Name}\", row {swatch.Row + 1} column {swatch.Col + 1}",
                };
        }

        // Pictures, and fills on a layer that has a tone test: its measured colours.
        if (layer.MaterialTone is { BareColour: { } bare, BurnColour: { } burn } curve && layer.Mode is LayerMode.Image or LayerMode.Fill)
            return new BurnLook
            {
                Mark = burn, Bare = bare, Tone = curve, Corrected = layer.Mode == LayerMode.Image, LineWidthMm = width,
                Source = LookSource.ToneTest, About = "the layer's tone test",
            };

        return Estimate(layer, category, width, woody, through);
    }

    /// <summary>A colour-test square is used for settings this close (see <see cref="SettingsDistance"/>).</summary>
    public const double NearEnough = 0.15;

    /// <summary>The colour-test square whose settings are nearest the layer's, across the material's palettes.</summary>
    public static (ColourPalette Palette, ColourSwatch Swatch, double Distance)? Nearest(Layer layer, Material material)
    {
        (ColourPalette, ColourSwatch, double)? best = null;
        foreach (var p in material.Palettes)
            foreach (var s in p.Swatches.Where(s => s.Use))
            {
                double d = SettingsDistance(layer, s.Settings);
                if (best is null || d < best.Value.Item3) best = (p, s, d);
            }
        return best;
    }

    /// <summary>
    /// How different two sets of marking settings are: 0 = the same; about 0.1 = one setting 10% off.
    /// A different laser or kind of layer is far away.
    /// </summary>
    public static double SettingsDistance(Layer layer, ProcessSettings s)
    {
        var a = layer.Laser;
        var b = s.Laser;
        if (a.Light != b.Light) return double.PositiveInfinity;
        if ((layer.Mode == LayerMode.Fill) != (s.Mode == LayerMode.Fill)) return double.PositiveInfinity;
        static double Rel(double x, double y) => Math.Abs(x - y) / Math.Max(1e-9, Math.Max(Math.Abs(x), Math.Abs(y)));
        double d = Rel(a.Power, b.Power) + Rel(a.Speed, b.Speed) + Rel(Math.Max(1, a.Passes), Math.Max(1, b.Passes));
        if (a.Light == LightSource.Red) d += Rel(a.FrequencyKhz, b.FrequencyKhz) + Rel(a.PulseWidthNs, b.PulseWidthNs);
        if (layer.Mode == LayerMode.Fill) d += Rel(layer.LinesPerCm, s.LinesPerCm);
        return d;
    }

    static bool Woody(string category) => category is "Wood" or "Plywood & MDF" or "Leather" or "Paper & card" or "Cork & rubber" or "Fabric" or "Food";

    /// <summary>The laser's full power, W, on the F2 Ultra (40 W diode, 60 W MOPA fiber).</summary>
    public static double Watts(LightSource light) => light == LightSource.Red ? 60 : 40;

    /// <summary>Lines: kerf if set, else the beam's usual width.</summary>
    static double LineWidth(Layer layer) =>
        layer.KerfMm > 0.01 ? layer.KerfMm
        : layer.Laser.Light == LightSource.Red ? 0.05 : layer.Mode == LayerMode.Cut ? 0.15 : 0.1;

    /// <summary>
    /// Energy the settings put into the material: J/mm² for fills and pictures (full black),
    /// J/mm along a line for cuts and scores. All passes counted.
    /// </summary>
    public static double Energy(Layer layer)
    {
        var l = layer.Laser;
        double watts = Watts(l.Light) * Math.Clamp(l.Power, 0, 100) / 100;
        int passes = Math.Max(1, l.Passes);
        switch (layer.Mode)
        {
            case LayerMode.Fill:
                double spacing = 10 / Math.Max(1, layer.LinesPerCm);
                return watts * passes / (Math.Max(0.1, l.Speed) * spacing) * (layer.Crosshatch ? 2 : 1);
            case LayerMode.Image when layer.ImageMode == ImageEngraveMode.Slices:
                double sp = 10 / Math.Max(1, layer.LinesPerCm);
                return watts * passes * Math.Max(1, layer.Slices.Count) / (Math.Max(0.1, l.Speed) * sp);
            case LayerMode.Image:
                double dotsPerMm = Math.Max(1, layer.Dpi) / 25.4;
                return watts * layer.DotUs * 1e-6 * dotsPerMm * dotsPerMm * passes;
            default:
                return watts * passes / Math.Max(0.1, l.Speed);
        }
    }

    /// <summary>
    /// A guess with no test to go on, from the energy and the kind of material. Wood-like
    /// materials brown and then blacken; acrylic, glass and stone frost lighter; coatings come
    /// off bright; bare metal hardly marks with the diode and greys or blackens with the fiber.
    /// </summary>
    static BurnLook Estimate(Layer layer, string category, double width, bool woody, bool through)
    {
        uint bare = BareGuess(category);
        double e = Energy(layer);
        bool line = layer.Mode is LayerMode.Cut or LayerMode.Score;
        bool fiber = layer.Laser.Light == LightSource.Red;
        // Energy at which the mark is about two-thirds formed (J/mm² for areas, J/mm for lines).
        double e0 = line ? 0.08 : 0.25;
        uint mark;
        switch (category)
        {
            case "Acrylic": mark = 0xD9D9D9; e0 *= 1.5; break;
            case "Glass": mark = 0xC9D2D6; e0 *= fiber ? 2 : 8; break;
            case "Stone & tile": mark = 0xB8B8B2; break;
            case "Coated metal": mark = 0xE4E4E4; e0 *= 0.5; break;
            case "Metal": mark = 0x3A3B3E; e0 *= fiber ? 3 : 40; break;
            case "Plastic": mark = 0x4A4A48; break;
            default: mark = 0x3A2416; break;
        }
        double strength = layer.Laser.Power <= 0 ? 0 : 1 - Math.Exp(-e / e0);
        string what = layer.Material?.Label ?? "wood (no material chosen)";
        var look = new BurnLook
        {
            Mark = mark, Bare = bare, Strength = strength, LineWidthMm = width, Source = LookSource.Estimate,
            About = $"a guess for {what} from the energy ({e:0.###} {(line ? "J/mm" : "J/mm²")}); run a test grid for a true preview",
        };
        if (layer.Mode == LayerMode.Cut)
            look = look with
            {
                // A cut that goes through shows a dark gap; a weak one is only a line.
                ThroughCut = through && strength > 0.85,
                ScorchMm = woody ? Math.Clamp(0.4 * strength, 0.1, 0.5) : 0,
            };
        return look;
    }

    static uint Rgb(byte r, byte g, byte b) => (uint)r << 16 | (uint)g << 8 | b;
}
