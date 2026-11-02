namespace PhotonCanvas.Addon.Furniture;

/// <summary>Makes the pieces for every piece of furniture in a set.</summary>
public static class FurnitureKit
{
    public static ItemKit Make(FurnitureItem item, FurnitureSet set, int index = 0)
    {
        var m = new Maker(item, set, index);
        if (item.WidthMm <= 0 || item.HeightMm <= 0 || (item.DepthMm <= 0 && item.Kind is not ItemKind.WallDoor))
        {
            m.Bad("Give it a size: width, depth and height must be more than 0.");
            return m.Kit();
        }
        switch (item.Kind)
        {
            case ItemKind.Table:
            case ItemKind.Bench:
            case ItemKind.Stool: Tables.Make(m); break;
            case ItemKind.Chair: Seating.Chair(m); break;
            case ItemKind.Bookshelf: Storage.Bookshelf(m); break;
            case ItemKind.Chest: Storage.Chest(m); break;
            case ItemKind.Crate: Storage.Crate(m); break;
            case ItemKind.Bed: Beds.Bed(m); break;
            case ItemKind.WallDoor: WallPieces.Door(m); break;
            case ItemKind.WallShelf: WallPieces.Shelf(m); break;
        }
        return m.Kit();
    }

    public static SetKit Make(FurnitureSet set) =>
        new([.. set.Items.Select((it, i) => Make(it, set, i))], set.ThicknessMm);

    /// <summary>"Table", or "Table 2" when there's more than one table in the set.</summary>
    public static string Label(FurnitureSet set, int index)
    {
        var kind = set.Items[index].Kind;
        string name = Defaults.Info(kind).Name;
        int same = set.Items.Count(i => i.Kind == kind);
        return same < 2 ? name : $"{name} {set.Items.Take(index + 1).Count(i => i.Kind == kind)}";
    }
}
