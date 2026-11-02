using System.Text.Json;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Reads the settings the wall maker saves on its walls ("walls.spec"), so a door or a shelf can be
/// made to fit them. Only the few numbers needed are read, from the JSON, so this add-on doesn't need
/// the wall maker to be installed or on.
/// </summary>
public static class WallsLink
{
    public const string SpecKey = "walls.spec";

    /// <summary>What a door and a shelf need to know about some walls.</summary>
    public sealed record WallInfo(double SheetMm, double WallMm, double FitMm, string Door, DoorShape Shape, double DoorWidthMm, double DoorHeightMm, int Mounts, double MountSlotMm)
    {
        public bool HasDoorway => Door is "Doorway" or "DoorwayAndDoor";
    }

    /// <summary>The walls among the selection, or failing that the first walls in the design (null if there are none).</summary>
    public static WallInfo? Find(DocState s, IReadOnlyCollection<Guid> selection)
    {
        var withSpec = s.Shapes.Where(x => x.AddonData.ContainsKey(SpecKey)).ToList();
        var pick = withSpec.FirstOrDefault(x => selection.Contains(x.Id)) ?? withSpec.FirstOrDefault();
        if (pick is null)
            foreach (var page in s.OtherPages)
                if (page.Shapes.FirstOrDefault(x => x.AddonData.ContainsKey(SpecKey)) is { } other) { pick = other; break; }
        return pick is null ? null : Read(pick.AddonData[SpecKey]);
    }

    public static WallInfo? Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            double Num(string name, double fallback) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
            string Str(string name, string fallback) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            bool twoSided = r.TryGetProperty("TwoSided", out var ts) && ts.ValueKind == JsonValueKind.True;
            double sheet = Num("ThicknessMm", 3);
            var shape = Str("DoorShape", "Arched") switch
            {
                "Rectangle" => DoorShape.Rectangle,
                "Pointed" => DoorShape.Pointed,
                _ => DoorShape.Arched,
            };
            return new WallInfo(sheet, sheet * (twoSided ? 2 : 1), Num("FitMm", 0.15), Str("Door", "None"), shape,
                Num("DoorWidthMm", 16), Num("DoorHeightMm", 28), (int)Num("Mounts", 0), Num("MountSlotMm", 6));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A door or shelf made to fit the walls.</summary>
    public static FurnitureItem Fit(FurnitureItem it, WallInfo w) => it.Kind switch
    {
        ItemKind.WallDoor => it with { WidthMm = w.DoorWidthMm, HeightMm = w.DoorHeightMm, Shape = w.Shape, WallMm = w.WallMm, WallSheetMm = w.SheetMm },
        ItemKind.WallShelf => it with { WallMm = w.WallMm, WallSheetMm = w.SheetMm, MountSlotMm = w.MountSlotMm },
        _ => it,
    };
}
