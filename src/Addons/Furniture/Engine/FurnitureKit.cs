namespace PhotonCanvas.Addon.Furniture;

/// <summary>Makes the pieces for every piece of furniture in a set.</summary>
public static class FurnitureKit
{
    public static ItemKit Make(FurnitureItem item, FurnitureSet set, int index = 0)
    {
        var m = new Maker(item, set, index);
        if (item.WidthMm <= 0 || (item.HeightMm <= 0 && item.Kind is not ItemKind.Rug) || (item.DepthMm <= 0 && item.Kind is not (ItemKind.WallDoor or ItemKind.WallHanging)))
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
            case ItemKind.Cabinet: Cabinets.Make(m); break;
            case ItemKind.Armchair: Armchairs.Make(m); break;
            case ItemKind.Desk: Slants.Desk(m); break;
            case ItemKind.Fireplace: Hearths.Make(m); break;
            case ItemKind.SlantDesk: Slants.SlantDesk(m); break;
            case ItemKind.WeaponRack: Profiles.WeaponRack(m); break;
            case ItemKind.Stand: Profiles.Stand(m); break;
            case ItemKind.Coffin: Coffins.Make(m); break;
            case ItemKind.Rug: Rugs.Rug(m); break;
            case ItemKind.WallHanging: Rugs.Hanging(m); break;
            case ItemKind.Candlestand: Profiles.Candlestand(m); break;
            case ItemKind.Screen: Profiles.Screen(m); break;
            case ItemKind.Stocks: Profiles.Stocks(m); break;
            case ItemKind.TownWall: TownWalls.Make(m); break;
            case ItemKind.Tower: TownWalls.Tower(m); break;
            case ItemKind.Tree: TownWalls.Tree(m); break;
            case ItemKind.Trough: TownWalls.Trough(m); break;
            case ItemKind.Stage: TownWalls.Stage(m); break;
            case ItemKind.Stall: TownWalls.Stall(m); break;
            default: m.Bad($"{Defaults.Info(item.Kind).Name} isn't made yet."); break;
        }
        return m.Kit();
    }

    public static SetKit Make(FurnitureSet set) =>
        new([.. set.Items.Select((it, i) => Make(it, set, i))], set.ThicknessMm);

    /// <summary>"Table", or "Table 2" when there's more than one table in the set.</summary>
    public static string Label(FurnitureSet set, int index)
    {
        var it = set.Items[index];
        string name = string.IsNullOrWhiteSpace(it.Name) ? Defaults.Info(it.Kind).Name : it.Name!;
        string Of(FurnitureItem i) => string.IsNullOrWhiteSpace(i.Name) ? Defaults.Info(i.Kind).Name : i.Name!;
        int same = set.Items.Count(i => Of(i) == name);
        return same < 2 ? name : $"{name} {set.Items.Take(index + 1).Count(i => Of(i) == name)}";
    }
}
