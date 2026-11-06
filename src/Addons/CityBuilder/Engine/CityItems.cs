using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;
using FGeo = PhotonCanvas.Addon.Furniture.Geo;
using V3 = PhotonCanvas.Addon.Furniture.V3;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// The furniture maker's pieces to put down in the town: the catalog's, by where they belong (the town's street
/// pieces, the gaol's stocks, a tavern hall's tables and benches, a workshop's anvil…), sized for the map's
/// squares, how each looks from above, and the set of them to cut.
/// </summary>
public static class CityItems
{
    /// <summary>Where the catalog's pieces belong, in the order the Furniture tool shows them: the town's first.</summary>
    public static IReadOnlyList<string> Rooms { get; } =
        [FurnitureCatalog.Town, .. FurnitureCatalog.Rooms.Where(r => r != FurnitureCatalog.Town)];

    /// <summary>The catalog's ends that other tools put down (walls, guard towers, trees, and the town's bigger pieces under Things).</summary>
    static readonly string[] Elsewhere =
    [
        "wall-1", "wall-2", "wall-corner-out", "wall-corner-in", "wall-gate", "wall-end", "guard-tower",
        "horse-trough", "stage", "market-stall", "bar", "notice-board", "fountain", "gallows", "street-lamp", "statue",
    ];

    /// <summary>The kinds of piece from the rooms that are also at home out of doors, shown with the town's own: the stocks, tables and benches outside an inn, crates and chests, an anvil, a weapon rack.</summary>
    static readonly ItemKind[] Outdoors = [ItemKind.Stocks, ItemKind.Table, ItemKind.Bench, ItemKind.Stool, ItemKind.Crate, ItemKind.Chest, ItemKind.Stand, ItemKind.WeaponRack];

    /// <summary>
    /// The catalog's pieces in a theme that stand on the ground in a room (or the town): not those hung on walls,
    /// nor walls, trees and the things the Things tool puts down. The town's are the pieces from every room that
    /// are at home out of doors, then the town's own (signposts, market crosses…). Falls back to the medieval ones
    /// for a room a theme has none in.
    /// </summary>
    public static IReadOnlyList<CatalogEntry> Catalog(string theme, string room)
    {
        bool Stands(CatalogEntry e) => FurnitureCatalog.SpotFor(e.Item) != FurnitureSpot.OnWall && e.Item.Kind != ItemKind.Tree
            && !Elsewhere.Any(end => e.Id.EndsWith("-" + end, StringComparison.Ordinal));
        List<CatalogEntry> In(string t)
        {
            if (room != FurnitureCatalog.Town) return [.. FurnitureCatalog.For(t, room).Where(Stands)];
            var outside = FurnitureCatalog.Rooms.Where(r => r != room).SelectMany(r => FurnitureCatalog.For(t, r))
                .Where(e => Outdoors.Contains(e.Item.Kind)).OrderBy(e => Array.IndexOf(Outdoors, e.Item.Kind));
            return [.. outside.Concat(FurnitureCatalog.For(t, room)).Where(Stands).DistinctBy(e => e.Id)];
        }
        var list = In(theme);
        return list.Count > 0 ? list : In("Medieval");
    }

    /// <summary>The building editor's settings the furniture is made with, for a map's squares (figures in proportion).</summary>
    public static BuildingSpec Spec(double squareMm) => new() { ScaleMm = 28 * squareMm / 25.4, SquareMm = squareMm };

    /// <summary>A catalog piece sized for the map's squares (the catalog's are for 25.4 mm squares).</summary>
    public static FurnitureItem Sized(CatalogEntry e, double squareMm)
    {
        double k = squareMm / 25.4;
        var it = Math.Abs(k - 1) < 1e-9 ? e.Item : FurnitureCatalog.Scale(e.Item, k);
        return it with { Count = 1 };
    }

    /// <summary>A piece from the catalog, ready to put down (its middle at x, y, turned).</summary>
    public static CityItem Make(CatalogEntry e, double squareMm, double x = 0, double y = 0, double turnDeg = 0)
    {
        var item = Sized(e, squareMm);
        var (w, d) = HouseKitMaker.Footprint(Spec(squareMm), item);
        return new CityItem { CatalogId = e.Id, Name = e.Name, Item = item, X = x, Y = y, TurnDeg = turnDeg, WidthMm = Math.Round(w, 2), DepthMm = Math.Round(d, 2) };
    }

    /// <summary>How a piece looks from above: the floor it takes (mm) and its pieces, lowest first (remembered).</summary>
    public static ItemLook Look(CityItem i, double squareMm) => HouseKitMaker.Look(Spec(squareMm), i.Item);

    /// <summary>How tall a piece stands (mm): its highest part, or what it's made as.</summary>
    public static double Height(CityItem i, double squareMm)
    {
        var look = Look(i, squareMm);
        return look.Parts.Count > 0 ? look.Parts.Max(p => p.Top) : i.Item.HeightMm;
    }

    /// <summary>
    /// A piece put together, for the 3D view: each of its flat pieces' outline and where each copy of it sits (mm,
    /// about the middle of the floor it takes: x along its front, y up from the ground, z towards its front), and
    /// how thick the sheet is. Remembered, as the 3D view asks again each time the map changes.
    /// </summary>
    public sealed record ItemSolid(List<(PathsD Outline, List<Pose> Poses)> Parts, double ThicknessMm);

    static readonly Dictionary<string, ItemSolid> Solids = [];

    public static ItemSolid SolidOf(CityItem i, double squareMm)
    {
        var spec = Spec(squareMm);
        string key = $"{spec.FurnitureSheetMm}|{squareMm}|{System.Text.Json.JsonSerializer.Serialize(i.Item)}";
        lock (Solids)
            if (Solids.TryGetValue(key, out var known)) return known;
        var made = new ItemSolid([], spec.FurnitureSheetMm);
        try
        {
            var kit = FurnitureKit.Make(HouseKitMaker.SetFor(spec, [i.Item with { Count = 1 }]));
            if (kit.Items.Count > 0 && kit.Items[0].Ok)
            {
                var ik = kit.Items[0];
                double t = kit.ThicknessMm;
                var (lo, hi) = Interior.Box(ik, t, 0);
                var mid = new V3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2);
                made = new ItemSolid([.. ik.Parts.Select(p => (FGeo.Tidy(Clipper.SimplifyPaths(p.Cut, 0.02)), p.Poses.Select(q => q with { O = q.O - mid }).ToList()))], t);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException) { }
        lock (Solids)
        {
            if (Solids.Count > 300) Solids.Clear();
            Solids[key] = made;
        }
        return made;
    }

    /// <summary>The map's furniture (inside the box, if one is given) as one set to cut, the same pieces counted together; null if there's none.</summary>
    public static FurnitureSet? Set(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var items = m.Items.Where(i => box is not { } b || i.X >= b.X0 && i.Y >= b.Y0 && i.X < b.X1 && i.Y < b.Y1)
            .GroupBy(i => System.Text.Json.JsonSerializer.Serialize(i.Item with { Count = 1 }))
            .Select(g => g.First().Item with { Count = g.Count(), Name = g.First().Name.Length > 0 ? g.First().Name : g.First().Item.Name })
            .ToList();
        if (items.Count == 0) return null;
        return FurnitureThemes.Apply(HouseKitMaker.SetFor(Spec(m.SquareMm), items), m.Theme);
    }
}
