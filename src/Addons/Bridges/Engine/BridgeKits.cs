namespace PhotonCanvas.Addon.Bridges;

/// <summary>Makes the pieces for a bridge.</summary>
public static class BridgeKits
{
    public static BridgeKit Make(BridgeSpec spec)
    {
        var m = new Maker(spec);
        if (spec.LengthMm <= 0 || spec.WidthMm <= 0 || (spec.HeightMm <= 0 && (spec.Kind == BridgeKind.Stone || spec.Ends == EndsOn.Table)))
        {
            m.Bad("Give it a size: length, width and height must be more than 0.");
            return m.Kit();
        }
        switch (spec.Kind)
        {
            case BridgeKind.Rope: PlankBridges.Rope(m); break;
            case BridgeKind.Stone: StoneBridge.Make(m); break;
            default: PlankBridges.Plank(m); break;
        }
        return m.Kit();
    }
}
