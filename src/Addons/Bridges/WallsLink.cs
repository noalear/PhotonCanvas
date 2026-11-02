using System.Text.Json;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// Reads the settings the wall maker saves on its walls ("walls.spec"), so a stone bridge can wear the
/// same stone as the walls beside it. Only the few values needed are read, from the JSON, so this add-on
/// doesn't need the wall maker to be installed or on.
/// </summary>
public static class WallsLink
{
    public const string SpecKey = "walls.spec";

    /// <summary>The look of some walls: their texture and its sizes, and the sheet they're cut from.</summary>
    public sealed record WallLook(string Texture, double SizeA, double SizeB, double GrooveMm, int Seed, CustomTexture? Custom, double SheetMm, double FitMm);

    /// <summary>The walls among the selection, or failing that the first walls in the design (null if there are none).</summary>
    public static WallLook? Find(DocState s, IReadOnlyCollection<Guid> selection)
    {
        var withSpec = s.Shapes.Where(x => x.AddonData.ContainsKey(SpecKey)).ToList();
        var pick = withSpec.FirstOrDefault(x => selection.Contains(x.Id)) ?? withSpec.FirstOrDefault();
        if (pick is null)
            foreach (var page in s.OtherPages)
                if (page.Shapes.FirstOrDefault(x => x.AddonData.ContainsKey(SpecKey)) is { } other) { pick = other; break; }
        return pick is null ? null : Read(pick.AddonData[SpecKey]);
    }

    public static WallLook? Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            double Num(JsonElement e, string name, double fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
            string Str(JsonElement e, string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            CustomTexture? custom = null;
            if (r.TryGetProperty("Custom", out var c) && c.ValueKind == JsonValueKind.Object)
                custom = new CustomTexture
                {
                    Name = Str(c, "Name", ""), Path = Str(c, "Path", ""),
                    TileWidthMm = Num(c, "TileWidthMm", 10), TileHeightMm = Num(c, "TileHeightMm", 10), RowShift = Num(c, "RowShift", 0),
                };
            if (custom is { } x && (x.Name.Length == 0 || x.Path.Length == 0)) custom = null;
            return new WallLook(Str(r, "Texture", "Brick"), Num(r, "SizeA", 8), Num(r, "SizeB", 3.5), Num(r, "GrooveMm", 0.6), (int)Num(r, "Seed", 1),
                custom, Num(r, "ThicknessMm", 3), Num(r, "FitMm", 0.15));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A stone bridge in the walls' stone, cut from the same sheet.</summary>
    public static BridgeSpec Fit(BridgeSpec s, WallLook w) => s with
    {
        Texture = w.Texture, SizeA = w.SizeA, SizeB = w.SizeB, GrooveMm = w.GrooveMm, Custom = w.Custom,
        ThicknessMm = w.SheetMm, FitMm = w.FitMm,
    };
}
