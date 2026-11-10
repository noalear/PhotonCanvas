namespace PhotonCanvas.Addon.Borders;

/// <summary>A profile's name and what it looks like.</summary>
public sealed record ProfileInfo(BandProfile Kind, string Name, string About, bool UsesCount = false);

/// <summary>The cross-section shapes a band can have: how high each point across it is.</summary>
public static class Profiles
{
    public static readonly IReadOnlyList<ProfileInfo> All =
    [
        new(BandProfile.Flat, "Flat", "Level all the way across: a fillet or a flat frieze."),
        new(BandProfile.Slope, "Slope (chamfer)", "A straight slope down: a chamfer or bevel."),
        new(BandProfile.Cove, "Cove", "A hollow quarter-round: steep at first, then levelling out."),
        new(BandProfile.RoundOver, "Round-over (ovolo)", "A rounded quarter-round: level at first, then rounding down."),
        new(BandProfile.Ogee, "Ogee", "An S curve, rounded at the top and hollow at the bottom: the Roman ogee."),
        new(BandProfile.ReverseOgee, "Reverse ogee", "An S curve, hollow at the top and rounded at the bottom."),
        new(BandProfile.Bead, "Bead", "A half-round bead standing up from the band."),
        new(BandProfile.Cushion, "Cushion", "A low, wide hump with a flatter top."),
        new(BandProfile.Thumbnail, "Thumbnail", "Level, then a tight round-over at the edge."),
        new(BandProfile.Reeds, "Reeds", "Beads side by side.", true),
        new(BandProfile.Flutes, "Flutes", "Hollow grooves side by side, with narrow flats between.", true),
        new(BandProfile.Steps, "Steps", "Equal steps down.", true),
        new(BandProfile.VGroove, "V groove", "A V-shaped groove down the middle."),
    ];

    static readonly Dictionary<BandProfile, ProfileInfo> ByKind = All.ToDictionary(p => p.Kind);

    public static ProfileInfo Info(BandProfile p) => ByKind.TryGetValue(p, out var i) ? i : All[0];

    /// <summary>
    /// How high profile <paramref name="p"/> is at <paramref name="t"/> across the band (0 = its outer
    /// side, 1 = its inner side): 1 = the band's top, 0 = its top less its drop.
    /// </summary>
    public static double Height(BandProfile p, double t, int count)
    {
        t = Math.Clamp(t, 0, 1);
        int n = Math.Clamp(count, 1, 12);
        switch (p)
        {
            case BandProfile.Flat: return 1;
            case BandProfile.Slope: return 1 - t;
            case BandProfile.Cove: { double k = 1 - t; return 1 - Math.Sqrt(Math.Max(0, 1 - k * k)); }
            case BandProfile.RoundOver: return Math.Sqrt(Math.Max(0, 1 - t * t));
            case BandProfile.Ogee:
                if (t < 0.5) { double k = 2 * t; return 0.5 + 0.5 * Math.Sqrt(Math.Max(0, 1 - k * k)); }
                else { double k = 2 - 2 * t; return 0.5 * (1 - Math.Sqrt(Math.Max(0, 1 - k * k))); }
            case BandProfile.ReverseOgee:
                if (t < 0.5) { double k = 1 - 2 * t; return 0.5 + 0.5 * (1 - Math.Sqrt(Math.Max(0, 1 - k * k))); }
                else { double k = 2 * t - 1; return 0.5 * Math.Sqrt(Math.Max(0, 1 - k * k)); }
            case BandProfile.Bead: { double k = 2 * t - 1; return Math.Sqrt(Math.Max(0, 1 - k * k)); }
            case BandProfile.Cushion: return 1 - Math.Pow(Math.Abs(2 * t - 1), 2.5);
            case BandProfile.Thumbnail:
                if (t < 0.55) return 1;
                { double k = (t - 0.55) / 0.45; return Math.Sqrt(Math.Max(0, 1 - k * k)); }
            case BandProfile.Reeds:
                { double k = 2 * Carve.Frac(t * n) - 1; if (t >= 1) k = 1; return Math.Sqrt(Math.Max(0, 1 - k * k)); }
            case BandProfile.Flutes:
                {
                    double f = Carve.Frac(t * n);
                    if (t >= 1) f = 1;
                    double k = (f - 0.08) / 0.84;
                    if (k <= 0 || k >= 1) return 1;
                    double c = 2 * k - 1;
                    return 1 - Math.Sqrt(Math.Max(0, 1 - c * c));
                }
            case BandProfile.Steps:
                {
                    if (n < 2) return 1 - t;
                    int step = Math.Min(n - 1, (int)Math.Floor(t * n));
                    return 1 - step / (double)(n - 1);
                }
            case BandProfile.VGroove: return Math.Abs(2 * t - 1);
            default: return 1;
        }
    }
}
