using System.Text.Json;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// Reads the settings the wall maker saves on its walls ("walls.spec"), so a ruin can be made to match
/// them: the same sheet, height, texture and variation, and ends that fit the same columns. Only the
/// JSON is read, so this add-on doesn't need the wall maker to be installed or on.
/// </summary>
public static class WallsLink
{
    public const string SpecKey = "walls.spec";

    /// <summary>The walls among the selection, or failing that the first walls in the design (null if there are none).</summary>
    public static string? Find(DocState s, IReadOnlyCollection<Guid> selection)
    {
        var withSpec = s.Shapes.Where(x => x.AddonData.ContainsKey(SpecKey)).ToList();
        var pick = withSpec.FirstOrDefault(x => selection.Contains(x.Id)) ?? withSpec.FirstOrDefault();
        if (pick is null)
            foreach (var page in s.OtherPages)
                if (page.Shapes.FirstOrDefault(x => x.AddonData.ContainsKey(SpecKey)) is { } other) { pick = other; break; }
        return pick?.AddonData[SpecKey];
    }

    /// <summary>The ruin's settings changed to match the walls' (null if the walls' settings can't be read).</summary>
    public static RuinSpec? Match(RuinSpec s, string wallsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(wallsJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            double Num(string name, double fallback) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
            string Str(string name, string fallback) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            bool Flag(string name, bool fallback) => r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && fallback) : fallback;
            CustomTexture? custom = null;
            if (r.TryGetProperty("Custom", out var c) && c.ValueKind == JsonValueKind.Object)
                custom = c.Deserialize<CustomTexture>(RuinJson.Options);
            string texture = Str("Texture", s.Texture);
            if (Textures.Preset(texture) is null && (custom is null || custom.Name != texture)) texture = s.Texture;
            return s with
            {
                ThicknessMm = Num("ThicknessMm", s.ThicknessMm),
                TwoSided = Flag("TwoSided", s.TwoSided),
                HeightMm = Num("HeightMm", s.HeightMm),
                Texture = texture,
                Theme = RuinThemes.Names.Contains(Str("Theme", "")) ? Str("Theme", s.Theme) : s.Theme,
                Custom = custom is not null && custom.Name == texture ? custom : null,
                SizeA = Num("SizeA", s.SizeA),
                SizeB = Num("SizeB", s.SizeB),
                GrooveMm = Num("GrooveMm", s.GrooveMm),
                Seed = (int)Num("Seed", s.Seed),
                Etch = Str("Etch", "Grooves") == "Lines" ? EtchStyle.Lines : EtchStyle.Grooves,
                PlainEnds = Flag("PlainEnds", s.PlainEnds),
                SlotDepthMm = Num("SlotDepthMm", s.SlotDepthMm),
                FitMm = Num("FitMm", s.FitMm),
            };
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }
}
