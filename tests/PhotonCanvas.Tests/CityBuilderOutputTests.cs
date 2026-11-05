using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.Scatter;
using SkiaSharp;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Tests;

/// <summary>What the city builder makes of a map besides the buildings: a battle map picture, the ground as tiles, bridges and scenery.</summary>
public class CityBuilderOutputTests
{
    /// <summary>A 12 × 8 map: a road across the middle (rows 3 and 4), a river down columns 6 to 8 that the road crosses, a dirt track crossing it lower down, a house, a well and a fence.</summary>
    static CityMap Crossing()
    {
        var m = CityMap.Empty(12, 8, "Crossing");
        var g = m.GroundGrid();
        for (int y = 0; y < 8; y++) for (int x = 6; x <= 8; x++) g[x, y] = Ground.Water;
        for (int x = 0; x < 12; x++) { g[x, 3] = Ground.Road; g[x, 4] = Ground.Road; }
        for (int x = 0; x < 12; x++) g[x, 6] = Ground.Dirt;
        var (design, hx, hy) = BuildingGuess.Make(new SeenBuilding { Cells = [(1, 0), (2, 0), (3, 0), (1, 1), (2, 1), (3, 1)] }, "Medieval", 25.4)!.Value;
        return m.WithGround(g) with
        {
            Buildings = [new CityBuilding { Name = "House", X = hx, Y = hy, Design = design }],
            Props = [new Prop { Kind = PropKind.Well, X = 10.5, Y = 1.5 }, new Prop { Kind = PropKind.Tree, X = 4.5, Y = 7.5 }],
            Fences = [new Fence { Kind = FenceKind.Fence, Edges = [new(9, 2, true), new(10, 2, true), new(11, 2, true)] }],
        };
    }

    [Fact]
    public void The_picture_is_the_map_drawn_square_by_square()
    {
        var m = Crossing();
        using var bmp = TownPicture.Render(m, new TownPictureOptions { PxPerSquare = 40, Grid = false });
        Assert.Equal((480, 320), (bmp.Width, bmp.Height));
        // Water's blue, grass green, the road grey; the house's roof isn't grass.
        static SKColor At(SKBitmap b, double x, double y) => b.GetPixel((int)(x * 40), (int)(y * 40));
        var water = At(bmp, 7.5, 1.2);
        Assert.True(water.Blue > water.Red + 30 && water.Blue > water.Green, $"water {water}");
        var grass = At(bmp, 10.2, 5.2);
        Assert.True(grass.Green > grass.Red && grass.Green > grass.Blue, $"grass {grass}");
        var roof = At(bmp, 2.5, 1.0);
        Assert.False(roof.Green > roof.Red + 10 && roof.Green > roof.Blue + 10, $"roof {roof}");
        // Only a box of it, and the grid drawn over it.
        using var part = TownPicture.Render(m, new TownPictureOptions { PxPerSquare = 40, Box = (6, 0, 9, 8) });
        Assert.Equal((120, 320), (part.Width, part.Height));
        // A huge map at a lot of pixels is drawn at fewer, within the limits.
        int px = TownPicture.FitPx(300, 300, 200);
        Assert.True(px < 200 && 300L * px <= TownPicture.MaxSide && 300L * 300 * px * px <= TownPicture.MaxPixels, $"{px}");
        Assert.Equal(100, TownPicture.FitPx(40, 30, 100));
        // Saved as the file's name says.
        var png = TownPicture.Encode(m, new TownPictureOptions { PxPerSquare = 20 }, "town.png");
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));
        var jpg = TownPicture.Encode(m, new TownPictureOptions { PxPerSquare = 20 }, "town.jpg");
        Assert.Equal([0xFF, 0xD8], jpg.Take(2));
    }

    [Fact]
    public void The_ground_is_cut_as_tiles_each_square_etched_with_its_ground()
    {
        var m = Crossing();
        var (spec, kit) = GroundBoards.Make(m, new GroundOptions { TileSquares = 4 });
        Assert.True(kit.Ok, string.Join("; ", kit.Findings.Select(f => f.Text)));
        Assert.Equal(12, spec.BoardAcross);
        Assert.Equal(6, kit.Tiles.Count());
        // Every tile has its ground etched or scored on it.
        Assert.All(kit.Board, b => Assert.True(b.Tile.Etched.Count > 0 || b.Tile.Lines.Count > 0, b.Tile.Name));
        // The top left tile (the map's rows 0 to 3, y up from its bottom edge): the road along its bottom row is cobbled
        // (the gaps between the cobbles etched), the squares under the house are left plain.
        var topLeft = kit.Board.Single(b => b.X == 0 && b.Y > 100);
        double g = 25.4;
        double EtchedIn(double x0, double y0, double x1, double y1) => Geo.Area(Geo.Intersect(Geo.Union(topLeft.Tile.Etched), [Geo.Rect(x0, y0, x1, y1)]));
        double road = EtchedIn(0, 0, 4 * g, g);
        Assert.True(road > 0.15 * 4 * g * g, $"{road:0} mm² etched on the road");
        Assert.True(EtchedIn(g + 1, 2 * g + 1, 4 * g - 1, 4 * g - 1) < 1, "etched under the house");
        // A box of it: only those squares.
        var (boxSpec, boxKit) = GroundBoards.Make(m, new GroundOptions { TileSquares = 3, Box = (6, 0, 9, 8) });
        Assert.Equal((3.0, 8.0), (boxSpec.BoardAcross, boxSpec.BoardDown));
        Assert.True(boxKit.Ok);
    }

    [Fact]
    public void A_bridge_goes_wherever_a_road_crosses_water()
    {
        var m = Crossing();
        var sites = TownPieces.Bridges(m).OrderBy(s => s.Middle.Y).ToList();
        Assert.Equal(2, sites.Count);
        var road = sites[0];
        Assert.True(road.Across);
        Assert.Equal((3, 2, Ground.Road), (road.Span, road.Width, road.Road));
        Assert.Equal(6, road.Cells.Count);
        var track = sites[1];
        Assert.Equal((3, 1, Ground.Dirt), (track.Span, track.Width, track.Road));
        // Stone arches for the road, planks for the track, each long enough to reach the banks.
        var stone = TownPieces.Bridge(road, "Medieval", 25.4);
        Assert.NotNull(stone);
        Assert.Equal(PhotonCanvas.Addon.Bridges.BridgeKind.Stone, stone.Value.Spec.Kind);
        Assert.True(stone.Value.Spec.LengthMm >= 3 * 25.4 && stone.Value.Spec.WidthMm > 25.4);
        Assert.True(stone.Value.Kit.Ok);
        var plank = TownPieces.Bridge(track, "Medieval", 25.4);
        Assert.NotNull(plank);
        Assert.Equal(PhotonCanvas.Addon.Bridges.BridgeKind.Plank, plank.Value.Spec.Kind);
        // A road beside the water but not over it isn't a bridge; nor is one outside the box.
        Assert.Empty(TownPieces.Bridges(m, (0, 0, 5, 8)));
        Assert.Empty(TownPieces.Bridges(CityWindow.Sample()));
    }

    [Theory]
    [InlineData(TownLayout.Crossroads)]
    [InlineData(TownLayout.MainStreet)]
    [InlineData(TownLayout.Grid)]
    [InlineData(TownLayout.Winding)]
    [InlineData(TownLayout.Riverside)]
    public void A_made_towns_roads_cross_its_river_and_never_run_down_it(TownLayout layout)
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var (m, _) = TownMaker.Make(new TownOptions { Across = 60, Down = 44, Layout = layout, River = true, Walls = seed % 2 == 0, Seed = seed });
            var sites = TownPieces.Bridges(m);
            // The river runs down the map, and the roads across it get bridges; a riverside town's runs along
            // the bottom, its lanes stopping at the quay. A road never runs along it.
            bool across = layout != TownLayout.Riverside;
            if (across) Assert.NotEmpty(sites);
            Assert.All(sites, s => Assert.True(s.Across == across, $"seed {seed}: a road along the river at {s.Middle}"));
            if (seed == 1 && sites.Count > 0) Assert.NotNull(TownPieces.Bridge(sites[0], "Medieval", m.SquareMm));
        }
    }

    [Fact]
    public void The_scenery_is_the_maps_own_from_the_scenery_maker()
    {
        var m = Crossing();
        var set = TownPieces.Scenery(m, "Medieval");
        Assert.NotNull(set);
        Assert.Contains(set.Items, i => i.Kind == ItemKind.Well && i.Count == 1);
        Assert.Contains(set.Items, i => i.Kind == ItemKind.Fence && i.Count == 1 && Math.Abs(i.WidthMm - (3 * 25.4 - 1)) < 0.01);
        Assert.DoesNotContain(set.Items, i => i.Kind == ItemKind.Cart);
        Assert.True(ScatterKit.Make(set).Ok);
        // The sample village's carts, crates and long fences (in sections of up to four squares).
        var village = TownPieces.Scenery(CityWindow.Sample(), "Medieval")!;
        Assert.Contains(village.Items, i => i.Kind == ItemKind.Cart);
        Assert.All(village.Items.Where(i => i.Kind == ItemKind.Fence), i => Assert.True(i.WidthMm <= 4 * 25.4));
        Assert.True(ScatterKit.Make(village).Ok);
        // Nothing in an empty corner.
        Assert.Null(TownPieces.Scenery(m, "Medieval", (0, 5, 3, 8)));
    }
}
