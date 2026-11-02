namespace PhotonCanvas.Addon.Scatter;

/// <summary>Makes the pieces for every piece of scenery in a set.</summary>
public static class ScatterKit
{
    public static ItemKit Make(ScatterItem item, ScatterSet set, int index = 0)
    {
        var m = new Maker(item, set, index);
        bool needsDepth = item.Kind is ItemKind.Cart or ItemKind.MarketStall or ItemKind.Well;
        if (item.WidthMm <= 0 || item.HeightMm <= 0 || (needsDepth && item.DepthMm <= 0))
        {
            m.Bad("Give it a size: " + (needsDepth ? "width, depth and height" : "width and height") + " must be more than 0.");
            return m.Kit();
        }
        switch (item.Kind)
        {
            case ItemKind.Barrel: Barrels.Make(m); break;
            case ItemKind.Fence: Fences.Make(m); break;
            case ItemKind.Cart: Carts.Make(m); break;
            case ItemKind.MarketStall: Stalls.Make(m); break;
            case ItemKind.Well: Wells.Make(m); break;
            case ItemKind.Gravestone: Graves.Make(m); break;
        }
        return m.Kit();
    }

    public static SetKit Make(ScatterSet set) =>
        new([.. set.Items.Select((it, i) => Make(it, set, i))], set.ThicknessMm);

    /// <summary>"Barrel", or "Barrel 2" when there's more than one barrel in the set.</summary>
    public static string Label(ScatterSet set, int index)
    {
        var kind = set.Items[index].Kind;
        string name = Defaults.Info(kind).Name;
        int same = set.Items.Count(i => i.Kind == kind);
        return same < 2 ? name : $"{name} {set.Items.Take(index + 1).Count(i => i.Kind == kind)}";
    }
}
