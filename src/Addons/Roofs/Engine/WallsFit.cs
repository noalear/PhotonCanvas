using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>Towers: see <see cref="Rings.Tower"/>.</summary>
public static class TowerMaker
{
    public static RoofKit Make(TowerSpec s) => Rings.Tower(s);
}

/// <summary>
/// Fits roofs and towers to walls made with the wall maker, from the settings it saves on its pieces:
/// a room is four walls joined by corner columns, so its outside is the columns' centres apart plus a
/// column (and the caps' lip, if they have caps).
/// </summary>
public static class WallsFit
{
    /// <summary>What the wall maker saves on each of its pieces.</summary>
    public const string SpecKey = "walls.spec", IdKey = "walls.id";

    /// <summary>The different sets of walls in the design (every page), the selected ones first.</summary>
    public static List<WallSpec> Find(DocState s, IReadOnlyCollection<Guid> selection)
    {
        var found = new List<(bool Selected, string Id, WallSpec Spec)>();
        void Look(IEnumerable<Shape> shapes)
        {
            foreach (var x in shapes)
            {
                if (!x.AddonData.TryGetValue(SpecKey, out var json)) continue;
                string id = x.AddonData.TryGetValue(IdKey, out var v) ? v : json;
                bool sel = selection.Contains(x.Id);
                int at = found.FindIndex(f => f.Id == id);
                if (at >= 0) { if (sel && !found[at].Selected) found[at] = found[at] with { Selected = true }; continue; }
                if (WallSpec.FromJson(json) is { } spec) found.Add((sel, id, spec));
            }
        }
        Look(s.Shapes);
        foreach (var page in s.OtherPages) Look(page.Shapes);
        return [.. found.OrderByDescending(f => f.Selected).Select(f => f.Spec)];
    }

    /// <summary>The distance between the middles of the columns at a wall's two ends (mm).</summary>
    public static double CentreToCentre(WallSpec w) => WallKitMaker.PanelLength(w) + 2 * WallKitMaker.CentreToEnd(w);

    static double Lip(WallSpec w) => w.Cap ? WallKitMaker.LipMm : 0;

    /// <summary>The outside of a room whose side is this wall: the columns' centres apart plus a column and its cap's lip (mm).</summary>
    public static double Outside(WallSpec w) => CentreToCentre(w) + w.ColumnMm + 2 * Lip(w);

    /// <summary>
    /// The roof made to sit on a room of these walls (the longer ones along the ridge): its size, the
    /// locator inside the walls and clear of the columns, the sheet, the fit and the gables' texture.
    /// </summary>
    public static RoofSpec Fit(RoofSpec r, WallSpec a, WallSpec? b = null)
    {
        b ??= a;
        if (CentreToCentre(b) > CentreToCentre(a)) (a, b) = (b, a);
        double wallT = WallKitMaker.WallThickness(a);
        double colTop = WallKitMaker.ColumnLayers(a) * a.ThicknessMm + (a.Cap ? a.ThicknessMm : 0);
        var fitted = r with
        {
            LengthMm = Math.Round(Outside(a), 2),
            WidthMm = Math.Round(Outside(b), 2),
            ThicknessMm = a.ThicknessMm,
            FitMm = a.FitMm,
            WallMm = wallT,
            InsetMm = Math.Round(a.ColumnMm / 2 + Lip(a) + wallT / 2, 2),
            CornerMm = Math.Round(a.ColumnMm + 2 * Lip(a), 2),
            RestAboveWallsMm = Math.Round(Math.Max(0, colTop - Math.Min(a.HeightMm, b.HeightMm)), 2),
        };
        if (Textures.Preset(a.Texture) is not null || a.Custom is { } c && c.Name == a.Texture)
            fitted = fitted with { GableTexture = a.Texture, GableCustom = a.Custom, GableA = a.SizeA, GableB = a.SizeB, GableGrooveMm = a.GrooveMm, GableSeed = a.Seed };
        return fitted;
    }

    /// <summary>A tower made to take these walls in its slots, as the wall maker's columns do.</summary>
    public static TowerSpec Fit(TowerSpec t, WallSpec w)
    {
        var fitted = t with
        {
            ThicknessMm = w.ThicknessMm,
            WallMm = WallKitMaker.WallThickness(w),
            WallHeightMm = w.HeightMm,
            SlotDepthMm = w.SlotDepthMm,
            FitMm = w.FitMm,
            Join = t.Join ?? ConnectorKind.Corner,
            HeightMm = Math.Max(t.HeightMm, w.HeightMm + 25),
            RingMm = Math.Max(t.RingMm, w.SlotDepthMm + 2.5),
        };
        if (Rings.Textures.Contains(w.Texture)) fitted = fitted with { Texture = w.Texture, SizeA = w.SizeA, SizeB = w.SizeB, GrooveMm = w.GrooveMm, Seed = w.Seed };
        return fitted;
    }
}
