using SkiaSharp;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// The little pictures on the city builder's palettes: each ground, road surface, wall or fence and tree or thing
/// drawn on a scrap of map the way the battle map picture draws it, as PNG files' bytes. Made once and kept.
/// </summary>
public static class PalettePictures
{
    /// <summary>How big each picture is (pixels each way).</summary>
    public const int Px = 64;

    static readonly Dictionary<string, byte[]> Made = [];
    static readonly object Gate = new();

    /// <summary>A patch of the ground.</summary>
    public static byte[] Ground(Ground g) => Get($"ground {g}", () => Draw(Patch(2, 2, g)));

    /// <summary>A road laid with this across a patch of grass.</summary>
    public static byte[] Road(Ground surface) => Get($"road {surface}", () =>
        Draw(Patch(3, 3, CityBuilder.Ground.Grass) with { Roads = [new CityRoad { Surface = surface, Width = 2, Points = [new(-1, 1.5), new(4, 1.5)] }] }));

    /// <summary>A stretch of fence, hedge or wall with its ends capped.</summary>
    public static byte[] Wall(FenceKind k) => Get($"wall {k}", () =>
        Draw(Patch(3, 3, CityBuilder.Ground.Grass) with { Fences = [new Fence { Kind = k, Points = [new(0.5, 1.5), new(2.5, 1.5)] }] }));

    /// <summary>A tree or thing in the middle of a patch of grass (paving for the market's and the street's things).</summary>
    public static byte[] Thing(PropKind k) => Get($"thing {k}", () =>
    {
        // Seen close enough to make out: a little more than the thing itself (a tree's crown, a stall's awning).
        double big = Prop.Footprint(k) is { } f ? Math.Max(f.W, f.D) : k is PropKind.Tree or PropKind.Pine ? 1.1 : 0.8;
        double view = Math.Max(1.2, big + 0.5);
        int side = (int)Math.Ceiling(view) + 1;
        var under = k is PropKind.Tree or PropKind.Pine or PropKind.Bush or PropKind.Boulder or PropKind.GuardTower ? CityBuilder.Ground.Grass : CityBuilder.Ground.Paving;
        return Draw(Patch(side, side, under) with { Props = [new Prop { Kind = k, X = side / 2.0, Y = side / 2.0 }] }, view);
    });

    static CityMap Patch(int across, int down, Ground g)
    {
        var m = CityMap.Empty(across, down, "");
        var grid = m.GroundGrid();
        for (int y = 0; y < down; y++) for (int x = 0; x < across; x++) grid[x, y] = g;
        return m.WithGround(grid);
    }

    /// <summary>The map drawn, and only the middle <paramref name="view"/> squares of it shown (all of it when 0).</summary>
    static byte[] Draw(CityMap m, double view = 0)
    {
        double shown = view > 0 ? view : Math.Max(m.Across, m.Down);
        int px = Math.Max(8, (int)Math.Ceiling(Px / shown));
        using var bmp = TownPicture.Render(m, new TownPictureOptions { PxPerSquare = px, Grid = false, Labels = false, Names = false, Seed = 3 });
        int w = Math.Min(bmp.Width, (int)Math.Round(shown * px)), h = Math.Min(bmp.Height, (int)Math.Round(shown * px));
        var middle = SKRectI.Create((bmp.Width - w) / 2, (bmp.Height - h) / 2, w, h);
        using var img = SKImage.FromBitmap(bmp);
        using var part = img.Subset(middle);
        using var data = part.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static byte[] Get(string key, Func<byte[]> make)
    {
        lock (Gate) if (Made.TryGetValue(key, out var have)) return have;
        var made = make();
        lock (Gate) Made[key] = made;
        return made;
    }
}
