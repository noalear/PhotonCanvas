using System.Buffers.Binary;
using PhotonCanvas.Addon.Topo;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class TopoTests
{
    /// <summary>The test grids' heights: 100 + 3x + 7y + xy/2 over 37 × 23 points.</summary>
    static double F(int x, int y) => 100 + 3 * x + 7 * y + 0.5 * x * y;

    static string Save(string base64, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-topo-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    [Fact]
    public void ReadsAFloatingPointTiledDeflateGeoTiffWithTheFloatPredictor()
    {
        var g = ElevationFiles.Load(Save(FloatTiledDeflate, "a.tif"));
        Assert.Equal((37, 23), (g.Width, g.Height));
        for (int y = 0; y < 23; y++)
            for (int x = 0; x < 37; x++) Assert.Equal(F(x, y), g.At(x, y), 3);
        // Degrees: 0.001° a pixel, about 111 m north-south and less across at 46.9°N.
        Assert.True(g.Georeferenced);
        Assert.Equal(111.2, g.Dy, 0);
        Assert.Equal(111.2 * Math.Cos(46.8885 * Math.PI / 180), g.Dx, 0);
        Assert.Equal(46.8885, g.Lat!.Value, 3);
        Assert.Equal(-121.7815, g.Lon!.Value, 3);
    }

    [Fact]
    public void ReadsABigEndianLzwGeoTiffInStripsWithTheIntegerPredictorAndNoData()
    {
        var g = ElevationFiles.Load(Save(Int16StripsLzw, "b.tif"));
        Assert.Equal((37, 23), (g.Width, g.Height));
        for (int y = 0; y < 23; y++)
            for (int x = 0; x < 37; x++)
                if (x == 4 && y == 3) Assert.True(float.IsNaN(g.At(x, y)), "the no-data point is missing");
                else Assert.Equal(Math.Round(F(x, y), MidpointRounding.ToEven), g.At(x, y), 3);
        // A projection in metres: 2 m a pixel.
        Assert.Equal(2, g.Dx, 6);
        Assert.Equal(2, g.Dy, 6);
        Assert.Null(g.Lat);
    }

    [Fact]
    public void ReadsAPackBitsTiffWithoutGeoTagsAsAGuessedSize()
    {
        var g = ElevationFiles.Load(Save(UInt16PackBits, "c.tif"));
        for (int y = 0; y < 23; y++)
            for (int x = 0; x < 37; x++) Assert.Equal(Math.Round(F(x, y) * 10, MidpointRounding.ToEven), g.At(x, y), 3);
        Assert.False(g.Georeferenced);
    }

    [Fact]
    public void ReadsSixteenBitGreyAndTerrariumPictures()
    {
        var grey = ElevationFiles.Load(Save(Grey16Png, "grey.png"));
        Assert.Equal(HeightUnits.Levels, grey.Units);
        Assert.Equal(Math.Round(F(30, 20) * 10, MidpointRounding.ToEven), grey.At(30, 20), 3);
        var t = ElevationFiles.Load(Save(TerrariumPng, "tile-terrarium.png"));
        Assert.Equal(HeightUnits.Metres, t.Units);
        for (int y = 0; y < 23; y += 5)
            for (int x = 0; x < 37; x += 6) Assert.InRange(t.At(x, y), F(x, y) - 0.01, F(x, y) + 0.01);
        Assert.Equal(-32768 + 128 * 256 + 5 + 0.5, PngHeights.Terrarium(128, 5, 128), 9);
    }

    [Fact]
    public void ReadsSrtmAndAsciiGridsAndAveragesBigOnesDown()
    {
        // A 1-arc-second SRTM square is 3601 a side; a tiny one of 5 keeps the test quick (same layout).
        int side = 5;
        var hgt = new byte[side * side * 2];
        for (int i = 0; i < side * side; i++) BinaryPrimitives.WriteInt16BigEndian(hgt.AsSpan(2 * i), (short)(i == 7 ? -32768 : 1000 + i));
        var g = ElevationFiles.Hgt(hgt, "N46W122");
        Assert.Equal(1000 + 12, g.At(2, 2));
        Assert.True(float.IsNaN(g.At(2, 1)));
        Assert.Equal(46.5, g.Lat!.Value, 6);
        Assert.Equal(-121.5, g.Lon!.Value, 6);
        Assert.Equal(GeoTiffMetresPerDegree / 4, g.Dy, 3);

        var asc = "ncols 4\nnrows 3\nxllcorner 400000\nyllcorner 5000000\ncellsize 30\nNODATA_value -9999\n1 2 3 4\n5 6 -9999 8\n9 10 11 12\n";
        var a = ElevationFiles.Ascii(asc, "grid");
        Assert.Equal((4, 3), (a.Width, a.Height));
        Assert.Equal(30, a.Dx);
        Assert.True(float.IsNaN(a.At(2, 1)));
        Assert.Equal(12, a.At(3, 2));

        // Averaged down: 4 × 3 to at most 2 a side means 2 × 2 blocks.
        var small = ElevationFiles.Ascii(asc, "grid", maxSide: 2);
        Assert.Equal((2, 2), (small.Width, small.Height));
        Assert.Equal((1 + 2 + 5 + 6) / 4.0, small.At(0, 0), 6);
        Assert.Equal((3 + 4 + 8) / 3.0, small.At(1, 0), 6); // the missing one left out
        Assert.Equal(60, small.Dx);
    }

    static double GeoTiffMetresPerDegree => 6371008.8 * Math.PI / 180;

    [Theory]
    [InlineData("46.8523, -121.7603", 46.8523, -121.7603)]
    [InlineData("(-33.86 151.21)", -33.86, 151.21)]
    [InlineData("46°51′08″N 121°45′37″W", 46.85222, -121.76028)]
    [InlineData("46 51 8 N, 121 45 37 W", 46.85222, -121.76028)]
    [InlineData("https://www.google.com/maps/@46.8523,-121.7603,12z", 46.8523, -121.7603)]
    [InlineData("https://www.google.com/maps/place/Mount+Rainier/@46.8529,-121.7604,11z/data=!3m1", 46.8529, -121.7604)]
    [InlineData("https://www.openstreetmap.org/#map=13/46.8523/-121.7603", 46.8523, -121.7603)]
    [InlineData("https://www.openstreetmap.org/?mlat=46.85&mlon=-121.76#map=12/46.85/-121.76", 46.85, -121.76)]
    [InlineData("https://maps.apple.com/?ll=46.8523,-121.7603&q=Rainier", 46.8523, -121.7603)]
    [InlineData("https://www.bing.com/maps?cp=46.8523%7E-121.7603&lvl=11", 46.8523, -121.7603)]
    public void ReadsCoordinatesAndMapLinks(string text, double lat, double lon)
    {
        var p = Places.Parse(text);
        Assert.NotNull(p);
        Assert.Equal(lat, p.Lat, 4);
        Assert.Equal(lon, p.Lon, 4);
    }

    [Fact]
    public void AGoogleLinkToAPlaceGivesItsName() =>
        Assert.Equal("Mount Rainier", Places.Parse("https://www.google.com/maps/place/Mount+Rainier/@46.8529,-121.7604,11z/data=!3m1")!.Name);

    [Theory]
    [InlineData("Mount Rainier")]
    [InlineData("Lake Tahoe, California")]
    [InlineData("K2")]
    [InlineData("100, 200")]
    public void NamesAreSearchedFor(string text) => Assert.Null(Places.Parse(text));

    [Fact]
    public void ReadsTheSearchAnswerWithEachPlacesSize()
    {
        var json = """[{"place_id":1,"lat":"46.8529","lon":"-121.7604","display_name":"Mount Rainier, Pierce County, Washington, United States","boundingbox":["46.80","46.90","-121.82","-121.70"]}]""";
        var l = Places.ParseSearch(json);
        var p = Assert.Single(l);
        Assert.Equal("Mount Rainier", Places.Short(p.Name));
        Assert.Equal(0.1 * GeoTiffMetresPerDegree, p.HeightM!.Value, 0);
        Assert.Equal(0.12 * GeoTiffMetresPerDegree * Math.Cos(46.8529 * Math.PI / 180), p.WidthM!.Value, 0);
    }

    [Fact]
    public void TileMathMatchesTheWebMapsTiles()
    {
        Assert.Equal((128.0, 128.0), TerrainTiles.Pixel(0, 0, 0));
        Assert.Equal(156543.03, TerrainTiles.MetresPerPixel(0, 0), 1);
        // Mount Rainier at zoom 10 is in tile 163, 357.
        var (x, y) = TerrainTiles.Pixel(46.8523, -121.7603, 10);
        Assert.Equal((165, 360), ((int)(x / 256), (int)(y / 256)));
        // 10 km at 300 points: pixels at least as fine as 33 m, within the tile limit.
        int z = TerrainTiles.ZoomFor(46.85, -121.76, 20_000, 20_000, 10_000 / 299.0);
        Assert.True(TerrainTiles.MetresPerPixel(46.85, z) <= 10_000 / 299.0);
        Assert.True(TerrainTiles.Tiles(46.85, -121.76, 20_000, 20_000, z).Count <= TerrainTiles.MaxTiles);
        // A huge area drops to a coarser zoom to stay within the limit.
        int far = TerrainTiles.ZoomFor(46.85, -121.76, 2_000_000, 2_000_000, 10);
        Assert.True(TerrainTiles.Tiles(46.85, -121.76, 2_000_000, 2_000_000, far).Count <= TerrainTiles.MaxTiles);
        var (la, lo) = TerrainTiles.Offset(0, 179.99, 10_000, 0);
        Assert.True(lo < -179.9, "going east over the 180° line comes back round");
        Assert.Equal(0, la, 9);
    }

    /// <summary>A cone 1000 m high and 10 km across on flat ground at 200 m, 50 m between points.</summary>
    static DemGrid Cone()
    {
        int n = 201;
        var z = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double r = Math.Sqrt((x - 100) * (x - 100) + (y - 100) * (y - 100)) * 50;
                z[y * n + x] = (float)(200 + Math.Max(0, 1000 * (1 - r / 5000)));
            }
        return new DemGrid(n, n, z) { Dx = 50, Dy = 50, Name = "cone" };
    }

    static double Area(PhotonCanvas.Doc.PathGeometry g) =>
        g.Subpaths.Where(s => s.Closed).Sum(s => Geom.SignedArea(s.Flatten(0.01)));

    [Theory]
    [InlineData(TopoFrame.Rectangle)]
    [InlineData(TopoFrame.Circle)]
    [InlineData(TopoFrame.Hexagon)]
    public void TheSolidSlicesIntoTheLayersOfTheGround(TopoFrame frame)
    {
        var g = Cone();
        var o = new TopoOptions { Frame = frame, WidthMm = 100, DepthMm = 100, ThicknessMm = 3, Layers = 5, BaseLayers = 1, Smooth = 0, Detail = 200 };
        var s = TopoSurface.Make(g, TopoSurface.Fit(g, o.Aspect), o);
        // The samples miss the very tip a little.
        Assert.InRange(s.Low, 200, 201);
        Assert.InRange(s.High, 1185, 1200);
        Assert.Equal((s.High - s.Low) / 5, s.Step, 6);
        Assert.Equal(18, s.StackMm, 9);
        // 10 km on 100 mm is 0.01 mm a metre across; 3 mm a 200 m layer is 0.015: 1.5 × taller than true.
        Assert.Equal(0.015 / (s.Step / 200) / 0.01, s.Exaggeration!.Value, 6);
        Assert.InRange(s.Exaggeration!.Value, 1.5, 1.55);
        var mesh = s.Mesh();

        // The base layer is the map's whole shape.
        double full = frame switch
        {
            TopoFrame.Circle => Math.PI * 50 * 50,
            TopoFrame.Hexagon => 3 * Math.Sqrt(3) / 2 * 50 * 50,
            _ => 100 * 100,
        };
        Assert.Equal(full, Area(StackSlicer.Section(mesh, 1.5)), full * 0.002);
        // A land layer cut through its middle: the cone above that height, a circle (all inside the map's shape).
        for (int layer = 1; layer <= 5; layer++)
        {
            double h = s.LevelOf(layer);
            double r = 50 * (1 - (h - 200) / 1000);
            if (frame == TopoFrame.Circle) r = Math.Min(r, 50);
            Assert.Equal(Math.PI * r * r, Area(StackSlicer.Section(mesh, s.ZOf(h))), Math.PI * r * r * 0.03 + 2);
        }

        // Sliced by 3D to slices: a layer per thickness, every land part sitting on the one below.
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 3, HeightMm = s.StackMm, KeepJoined = false });
        Assert.Equal(6, stack.Slices);
        Assert.Equal(6, stack.Parts.Count);
        Assert.DoesNotContain(stack.Parts, p => p.Floating);
        Assert.Equal(100, stack.ModelWidthMm, 1);
    }

    [Fact]
    public void ARectangleIsWatertight()
    {
        var g = Cone();
        var o = new TopoOptions { WidthMm = 120, DepthMm = 80, Layers = 4, Detail = 60, Smooth = 1 };
        var mesh = TopoSurface.Make(g, TopoSurface.Fit(g, o.Aspect), o).Mesh();
        // Every edge is used once each way round.
        var edges = new Dictionary<((double, double, double), (double, double, double)), int>();
        void Add(PhotonCanvas.Doc.Import.StlDepth.Vec3 a, PhotonCanvas.Doc.Import.StlDepth.Vec3 b)
        {
            var k = ((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
            edges[k] = edges.GetValueOrDefault(k) + 1;
        }
        foreach (var t in mesh) { Add(t.A, t.B); Add(t.B, t.C); Add(t.C, t.A); }
        var bad = edges.Where(e => e.Value != 1 || !edges.ContainsKey((e.Key.Item2, e.Key.Item1))).Take(5).ToList();
        Assert.True(bad.Count == 0, "open or doubled edges: " + string.Join(", ", bad.Select(b => b.Key)));
    }

    [Fact]
    public void SeaLevelKeepsTheSeaFlatAndAHeightCanBeChosen()
    {
        // Half sea floor (−50 m), half land rising to 300 m.
        int n = 50;
        var z = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++) z[y * n + x] = x < 25 ? -50 : (x - 25) * 12;
        var g = new DemGrid(n, n, z) { Dx = 20, Dy = 20 };
        var a = TopoSurface.Fit(g, 1);
        var sea = TopoSurface.Make(g, a, new TopoOptions { Bottom = TopoBottom.SeaLevel, Layers = 6, Smooth = 0, Detail = 50 });
        Assert.Equal(0, sea.Start);
        Assert.Equal(0, sea.LayerAt(-50));
        Assert.Equal(1 * 3, sea.ZOf(-50), 9); // on the base
        var mid = TopoSurface.Make(g, a, new TopoOptions { Bottom = TopoBottom.Height, BottomHeight = 100, Layers = 4, Smooth = 0, Detail = 50 });
        Assert.Equal(100, mid.Start);
        Assert.True(mid.Step > 0);
        Assert.Throws<ArgumentException>(() => TopoSurface.Make(g, a, new TopoOptions { Bottom = TopoBottom.Height, BottomHeight = 5000 }));
    }

    [Fact]
    public void ContourLinesLeaveTheMapsEdgeOut()
    {
        var g = Cone();
        var o = new TopoOptions { Frame = TopoFrame.Circle, WidthMm = 100, Layers = 5, Smooth = 0, Detail = 150 };
        // Zoomed in on the cone's side, so the contours run off the edge of the map.
        var s = TopoSurface.Make(g, new TopoArea(5000 + 2000, 5000, 4000, 4000), o);
        var mesh = s.Mesh();
        var lines = s.Contours(mesh, Enumerable.Range(1, 5).Select(s.LevelOf).ToList());
        Assert.NotEmpty(lines);
        // No line runs along the circle: each has points well inside it.
        foreach (var l in lines)
            Assert.True(l.Any(p => new Vec2(p.X - 50, p.Y - 50).Length < 49), "along the edge: " + string.Join(" ", l.Select(p => p.ToString())));
        // Open lines (the contours cross the map's edge).
        Assert.Contains(lines, l => l[0].Dist(l[^1]) > 1);
    }

    [Fact]
    public void SavesAnStlTheSlicerReads()
    {
        var g = Cone();
        var o = new TopoOptions { Layers = 3, Detail = 40 };
        var mesh = TopoSurface.Make(g, TopoSurface.Fit(g, 1), o).Mesh();
        var path = Path.Combine(Path.GetTempPath(), "pc-topo-tests", "cone.stl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TopoSurface.SaveStl(mesh, path, "cone");
        var back = PhotonCanvas.Doc.Import.StlDepth.Load(path);
        Assert.Equal(mesh.Count, back.Count);
    }

    [Fact]
    public void TheAreaFitsTheGridInTheMapsShape()
    {
        var g = new DemGrid(101, 51, new float[101 * 51]) { Dx = 10, Dy = 10 };
        var wide = TopoSurface.Fit(g, 3);
        Assert.Equal((1000, 1000 / 3.0), (wide.WidthM, wide.HeightM));
        var square = TopoSurface.Fit(g, 1);
        Assert.Equal((500, 500), (square.WidthM, square.HeightM));
        Assert.Equal((500, 250), (square.X, square.Y));
    }

    const string FloatTiledDeflate =
        "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";

    const string Int16StripsLzw =
        "TU0AKgAAAAgAFAEAAAQAAAABAAAAJQEBAAQAAAABAAAAFwECAAMAAAABABAAAAEDAAMAAAABAAUAAAEGAAMAAAABAAEAAAEOAAIAAAAUAAAA/gERAAQAAAAFAAABIgEVAAMAAAABAAEA" +
        "AAEWAAQAAAABAAAABQEXAAMAAAAFAAABNgEaAAUAAAABAAABQAEbAAUAAAABAAABSAEoAAMAAAABAAEAAAExAAIAAAAMAAABUAE9AAMAAAABAAIAAAFTAAMAAAABAAIAAIMOAAwAAAAD" +
        "AAABXISCAAwAAAAGAAABdIevAAMAAAAMAAABpKSBAAIAAAAGAAABvAAAAAB7InNoYXBlIjogWzIzLCAzN119AAAAAAAAAAAAAAAAAAAAAAAAAAHQAAACOgAAAqsAAAMSAAADgwBqAHEA" +
        "ZwBxAD4AAAABAAAAAQAAAAEAAAABdGlmZmZpbGUucHkAQAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQR6EgAAAAABBU9YgAAAAAAAAAAAAAAAA" +
        "AAEAAQAAAAIEAAAAAAEAAQwEAAAAASMpLTk5OTkAAAAAAAAAAAAAAAAAAACAAAyAABwSDQWEQeFQmGQuHQ2IQ+JGuDAQARaExaMRWLwqNR6OxmQxyNwWPyKNnKOx+WSuXS2YS+ZTGaTM" +
        "AHkAAWVtg1idPy6dTqP0GgTmi0KO0Sh0al0iLTpAUaiVOpVWqVerVmsVutQEgAAQ4AAoAA0GgkJg8HgsFhcJh0IhsIhkKiUWisRjMUhKOjkPkEfkUhkkjk0lkqVAAHjkslkPl0tlcyl8" +
        "ImMwmc4msHm82ACcmcxoVBolDo1FpFHpVJACjmYIAFQmNQqVPqNEqlYq9TrdWqssrNcqUBCAABVAAEQSDQWEQeFQmGQuHQ2IQ+JLEAAmFRaLQmMReKxyMwaNxqOyKPwWQyAALiOxuWSu" +
        "XS2YS+ZTGaTMAL+OgoATqNzqeTmdy6fUKgz2i0Cfxah0afsag0OoU+pVGqVOrVWsVeAggAAZoABYABUGgkJg8HgsFhcJh0IhsIhkKiUWisRjMUhLUiEfichkEjkUlkknk0TbcJBgAlsT" +
        "lsvlkuj8xms0mE4mcygs2nMycU0m1DoVFolHo1JpFLpQAdIABtFqNRm1TqVQq9UmlWqtYrtaltcrcBCAAB4AAGwSDQWEQeFQmGQuHQ2IQ+JPeDA4ARaExaMRWLwqNR6OxmQxyNwWPyKN" +
        "v6Ox+WSuXS2YS+ZTGaTOAg==";

    const string UInt16PackBits =
        "SUkqAAgAAAAOAAABBAABAAAAJQAAAAEBBAABAAAAFwAAAAIBAwABAAAAEAAAAAMBAwABAAAABYAAAAYBAwABAAAAAQAAAA4BAgAUAAAAtgAAABEBBAABAAAAAAEAABUBAwABAAAAAQAA" +
        "ABYBBAABAAAAFwAAABcBBAABAAAAvQYAABoBBQABAAAA2gAAABsBBQABAAAA4gAAACgBAwABAAAAAQAAADEBAgAMAAAA6gAAAAAAAAB7InNoYXBlIjogWzIzLCAzN119AAAAAAAAAAAA" +
        "AAAAAAAAAAABAAAAAQAAAAEAAAABAAAAdGlmZmZpbGUucHkAAAAAAAAAAAAAAEnoAwYEJARCBGAEfgScBLoE2AT2BBQFMgVQBW4FjAWqBcgF5gUEBiIGQAZeBnwGmga4BtYG9AYSBzAH" +
        "TgdsB4oHqAfGB+QHAgggCEkuBFEEdASXBLoE3QQABSMFRgVpBYwFrwXSBfUFGAY7Bl4GgQakBscG6gYNBzAHUwd2B5kHvAffBwIIJQhICGsIjgixCNQI9wgaCUl0BJwExATsBBQFPAVk" +
        "BYwFtAXcBQQGLAZUBnwGpAbMBvQGHAdEB2wHlAe8B+QHDAg0CFwIhAisCNQI/AgkCUwJdAmcCcQJ7AkUCkm6BOcEFAVBBW4FmwXIBfUFIgZPBnwGqQbWBgMHMAddB4oHtwfkBxEIPghr" +
        "CJgIxQjyCB8JTAl5CaYJ0wkACi0KWgqHCrQK4QoOC0kABTIFZAWWBcgF+gUsBl4GkAbCBvQGJgdYB4oHvAfuByAIUgiECLYI6AgaCUwJfgmwCeIJFApGCngKqgrcCg4LQAtyC6QL1gsI" +
        "DElGBX0FtAXrBSIGWQaQBscG/gY1B2wHowfaBxEISAh/CLYI7QgkCVsJkgnJCQAKNwpuCqUK3AoTC0oLgQu4C+8LJgxdDJQMywwCDUmMBcgFBAZABnwGuAb0BjAHbAeoB+QHIAhcCJgI" +
        "1AgQCUwJiAnECQAKPAp4CrQK8AosC2gLpAvgCxwMWAyUDNAMDA1IDYQNwA38DUnSBRMGVAaVBtYGFwdYB5kH2gcbCFwInQjeCB8JYAmhCeIJIwpkCqUK5gonC2gLqQvqCysMbAytDO4M" +
        "Lw1wDbEN8g0zDnQOtQ72DkkYBl4GpAbqBjAHdge8BwIISAiOCNQIGglgCaYJ7AkyCngKvgoEC0oLkAvWCxwMYgyoDO4MNA16DcANBg5MDpIO2A4eD2QPqg/wD0leBqkG9AY/B4oH1Qcg" +
        "CGsItggBCUwJlwniCS0KeArDCg4LWQukC+8LOgyFDNAMGw1mDbEN/A1HDpIO3Q4oD3MPvg8JEFQQnxDqEEmkBvQGRAeUB+QHNAiECNQIJAl0CcQJFApkCrQKBAtUC6QL9AtEDJQM5Aw0" +
        "DYQN1A0kDnQOxA4UD2QPtA8EEFQQpBD0EEQRlBHkEUnqBj8HlAfpBz4IkwjoCD0JkgnnCTwKkQrmCjsLkAvlCzoMjwzkDDkNjg3jDTgOjQ7iDjcPjA/hDzYQixDgEDURihHfETQSiRLe" +
        "EkkwB4oH5Ac+CJgI8ghMCaYJAApaCrQKDgtoC8ILHAx2DNAMKg2EDd4NOA6SDuwORg+gD/oPVBCuEAgRYhG8ERYScBLKEiQTfhPYE0l2B9UHNAiTCPIIUQmwCQ8KbgrNCiwLiwvqC0kM" +
        "qAwHDWYNxQ0kDoMO4g5BD6AP/w9eEL0QHBF7EdoRORKYEvcSVhO1ExQUcxTSFEm8ByAIhAjoCEwJsAkUCngK3ApAC6QLCAxsDNAMNA2YDfwNYA7EDigPjA/wD1QQuBAcEYAR5BFIEqwS" +
        "EBN0E9gTPBSgFAQVaBXMFUkCCGsI1Ag9CaYJDwp4CuEKSguzCxwMhQzuDFcNwA0pDpIO+w5kD80PNhCfEAgRcRHaEUMSrBIVE34T5xNQFLkUIhWLFfQVXRbGFklICLYIJAmSCQAKbgrc" +
        "CkoLuAsmDJQMAg1wDd4NTA66DigPlg8EEHIQ4BBOEbwRKhKYEgYTdBPiE1AUvhQsFZoVCBZ2FuQWUhfAF0mOCAEJdAnnCVoKzQpAC7MLJgyZDAwNfw3yDWUO2A5LD74PMRCkEBcRihH9" +
        "EXAS4xJWE8kTPBSvFCIVlRUIFnsW7hZhF9QXRxi6GEnUCEwJxAk8CrQKLAukCxwMlAwMDYQN/A10DuwOZA/cD1QQzBBEEbwRNBKsEiQTnBMUFIwUBBV8FfQVbBbkFlwX1BdMGMQYPBm0" +
        "GUkaCZcJFAqRCg4LiwsIDIUMAg1/DfwNeQ72DnMP8A9tEOoQZxHkEWES3hJbE9gTVRTSFE8VzBVJFsYWQxfAFz0Yuhg3GbQZMRquGklgCeIJZArmCmgL6gtsDO4McA3yDXQO9g54D/oP" +
        "fBD+EIARAhKEEgYTiBMKFIwUDhWQFRIWlBYWF5gXGhicGB4ZoBkiGqQaJhuoG0mmCS0KtAo7C8ILSQzQDFcN3g1lDuwOcw/6D4EQCBGPERYSnRIkE6sTMhS5FEAVxxVOFtUWXBfjF2oY" +
        "8Rh4Gf8ZhhoNG5QbGxyiHEnsCXgKBAuQCxwMqAw0DcANTA7YDmQP8A98EAgRlBEgEqwSOBPEE1AU3BRoFfQVgBYMF5gXJBiwGDwZyBlUGuAabBv4G4QcEB2cHQ==";

    const string Grey16Png =
        "iVBORw0KGgoAAAANSUhEUgAAACUAAAAXEAAAAAD51wU6AAADDElEQVR4nI2TIXDqWhRF1+6LqampwfBFK4r4/JnGRDxMTMw1MTExMTGYGAwGg8HUYGowGAwGg8FgMBgMJiYmJvNnMBhM" +
        "DF9wS2nf+zNvrjj73sns2eucE/34V3/x7Xx/+bMv5PzD88fRM1+O/vjGs54dAlq0aKlFi/RGt2jx9uW2sLriwJE9LbVwuOeBR06qHQLaatMmpk3b6v5VtWkzudEr2mpz5ETNWYU1anBS" +
        "7SjAJcTFxZWLS3ZVo6tymeHKpaTiwEYuNWccCrk80uD4kcrDw5OHR3pVA1s93vHkkVOwkMeBIye28ixaKY/mp1VHHTrEdKzq2Tqydc9UHUoqlnTUoebMTp0LmiprXzsE8gnx8eXj07V1" +
        "YOsYXz4FM3z5HDmxlo/DPXv5NGhS2ffaUYAhIFBAQqCAgJ6tIwIF5EwUUHFgoYCaMxsFPPConCZPVPpMhcHIEGNk6GJkMAwwMuwZy1AyxchwYimDwz1bGRrkMrx8s1JIRKiQlFAhPUKF" +
        "7BgppOCdUCFH5go5s1LIA4/a0eRJxWWmCj96ZYiIFJEQKSIjUsSASBFvRIo4MFVEzUIR96wV0WCniBcKRVQcdO1VTKiImFgxXcVs6SsmZ6SYinfFnJgpxmGpmEc2innSnr8pVN5YWcCE" +
        "WAmpEjISJQxIlPCmhCMTJZyZK+GBlRKa2vLCXgkFpZIbKxlSIsWkSumSKqWvlJKRUsakSpkq5Z6FUhpa88RWKa/KKSh1TXWnAHMBVEJKVxk99Rkw1Ig3jfXORFNmmmuhpVastdFWO+3J" +
        "VahUpQNH2W03CskUk5GqS6aMvjIOjJQxVoajKQ/MlbFUxos2vLJTRi6biqNO1HcEMoRExCT6TSreNWGqmeYstGSlNTaVchUqqXTQUSfVd3wBVJdMPfoMNGSkN34BlAVkrxwLaK1kV1Q3" +
        "qejrl1TMtcD2iptUn1aXH2dHRE5CSZcDPfUZashIQ8YaakJTM1604FUrfl6sPlJh237t1XfASyp9meBtr7AT5BaQ3wCqz0D/A6jrMnwAYq3+A4yLnH6PkToTAAAAAElFTkSuQmCC";

    const string TerrariumPng =
        "iVBORw0KGgoAAAANSUhEUgAAACUAAAAXCAIAAAADThHyAAABGklEQVR4nL2V603DMBhFzy2FtHn0kaRJux8TMMGdgAmYoBMwARMwARMwAeIHkRClpIljKp0flu3P0tFnX8v3cHM9ZiSQ" +
        "mAUszBKWJoXUZJCZHHJTQGFWsDJrWJsNbMwWtqaE0lRQmRpqs4OdaaAxLbRmD3tzQH6A+ZXQ/D/91KDGalFr7VHnZ7iNhi5tiOCnEpVWhQb17xHuQlBQVYif1lb4/XyCZCgavPMvhvqp" +
        "sOK8vyMs+lDv6lgu+Cl+vjzD8gw6Nzmd836KnZ9f+aID8gukP1B6OjOFk9NO/RQ7P3/17xWyDmXf4yn0nBP5f1CNLtzPN8ghR3k3CGNgeeenaX4a8f7eoUAFBDG2cEaCQvunkHz5QCsY" +
        "T1jVJwOBmQDUbR02AAAAAElFTkSuQmCC";
}
