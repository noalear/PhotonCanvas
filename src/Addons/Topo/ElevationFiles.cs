using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Elevation files from survey sites (USGS National Map, Copernicus, OpenTopography, Earthdata…):
/// GeoTIFF, SRTM .hgt, ESRI ASCII grids (.asc), and heightmap pictures (16-bit grey PNG, or
/// Terrarium / terrain-RGB coloured tiles). Big files are averaged down to at most
/// <c>maxSide</c> points along their longer side as they're read.
/// </summary>
public static partial class ElevationFiles
{
    public static readonly string[] Patterns = ["*.tif", "*.tiff", "*.hgt", "*.asc", "*.png"];

    /// <exception cref="FormatException">Not a file of heights this reads (the message says why).</exception>
    public static DemGrid Load(string path, int maxSide = 2400)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var data = File.ReadAllBytes(path);
        if (GeoTiff.IsTiff(data) || data.Length >= 4 && ((data[0] == 'I' && data[2] == 43) || (data[0] == 'M' && data[3] == 43))) return GeoTiff.Read(data, name, maxSide);
        if (PngHeights.IsPng(data)) return Picture(data, name, maxSide);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".hgt" => Hgt(data, name, maxSide),
            ".asc" or ".txt" => Ascii(System.Text.Encoding.ASCII.GetString(data), name, maxSide),
            _ => throw new FormatException("This isn't an elevation file this reads: GeoTIFF (.tif), SRTM (.hgt), ESRI ASCII grid (.asc) or a heightmap picture (.png)."),
        };
    }

    /// <summary>SRTM: a square of big-endian 16-bit metres, 1201 or 3601 a side for one degree; the name says where (N46W122 = its south-west corner).</summary>
    public static DemGrid Hgt(byte[] data, string name, int maxSide = 2400)
    {
        int side = (int)Math.Round(Math.Sqrt(data.Length / 2.0));
        if (side < 2 || side * side * 2 != data.Length) throw new FormatException("This .hgt file isn't a square of heights (SRTM files are 1201 or 3601 points a side).");
        var r = new GridReducer(side, side, maxSide);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                short v = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(2 * (y * side + x)));
                if (v != -32768) r.Add(x, y, v);
            }
        double deg = 1.0 / (side - 1);
        double? lat = null, lon = null;
        if (HgtName().Match(name) is { Success: true } m)
        {
            lat = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * (m.Groups[1].Value.Equals("S", StringComparison.OrdinalIgnoreCase) ? -1 : 1) + 0.5;
            lon = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * (m.Groups[3].Value.Equals("W", StringComparison.OrdinalIgnoreCase) ? -1 : 1) + 0.5;
        }
        double dy = deg * GeoTiff.MetresPerDegree, dx = dy * Math.Cos((lat ?? 45) * Math.PI / 180);
        return r.Grid(dx, dy) with { Lat = lat, Lon = lon, Name = name, Source = "SRTM file" };
    }

    [GeneratedRegex(@"^([NS])(\d{1,2})([EW])(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex HgtName();

    /// <summary>ESRI ASCII grid: a header (ncols, nrows, xllcorner, yllcorner, cellsize, NODATA_value), then the heights row by row from the north.</summary>
    public static DemGrid Ascii(string text, string name, int maxSide = 2400)
    {
        var head = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        int pos = 0;
        // Header lines: a word and a number.
        while (pos < text.Length)
        {
            int end = text.IndexOf('\n', pos);
            if (end < 0) end = text.Length;
            var parts = text[pos..end].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { pos = end + 1; continue; }
            if (parts.Length != 2 || !char.IsLetter(parts[0][0])) break;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException($"The grid's header line \"{parts[0]}\" has no number.");
            head[parts[0]] = v;
            pos = end + 1;
        }
        if (!head.TryGetValue("ncols", out var nc) || !head.TryGetValue("nrows", out var nr)) throw new FormatException("This isn't an ESRI ASCII grid (its header has no ncols and nrows).");
        int w = (int)nc, h = (int)nr;
        double cell = head.TryGetValue("cellsize", out var cs) ? cs : head.GetValueOrDefault("dx", 1);
        double cellY = head.TryGetValue("dy", out var cy) ? cy : cell;
        double nodata = head.TryGetValue("nodata_value", out var nd) ? nd : double.NaN;
        double xll = head.TryGetValue("xllcorner", out var xc) ? xc : head.GetValueOrDefault("xllcenter");
        double yll = head.TryGetValue("yllcorner", out var yc) ? yc : head.GetValueOrDefault("yllcenter");
        var r = new GridReducer(w, h, maxSide);
        int i = 0;
        var span = text.AsSpan(pos);
        while (span.Length > 0 && i < w * h)
        {
            int k = 0;
            while (k < span.Length && char.IsWhiteSpace(span[k])) k++;
            span = span[k..];
            if (span.Length == 0) break;
            k = 0;
            while (k < span.Length && !char.IsWhiteSpace(span[k])) k++;
            if (!double.TryParse(span[..k], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException($"The grid has something that isn't a number in it (\"{span[..Math.Min(k, 20)].ToString()}\").");
            if (v != nodata) r.Add(i % w, i / w, v);
            i++;
            span = span[k..];
        }
        if (i < w * h) throw new FormatException($"The grid is cut short: it should have {w * h:N0} heights and has {i:N0}.");
        // Degrees when it fits the Earth and the cells are small; else the projection's metres.
        bool degrees = cell < 0.5 && Math.Abs(xll) <= 360 && Math.Abs(yll) <= 90;
        double? lat = degrees ? yll + cellY * h / 2 : null, lon = degrees ? xll + cell * w / 2 : null;
        double dy = degrees ? cellY * GeoTiff.MetresPerDegree : cellY, dx = degrees ? cell * GeoTiff.MetresPerDegree * Math.Cos(lat!.Value * Math.PI / 180) : cell;
        return r.Grid(dx, dy) with { Lat = lat, Lon = lon, Name = name, Source = "ESRI ASCII grid" };
    }

    /// <summary>
    /// A heightmap picture: grey (8 or 16 bits, white high), or coloured elevation tiles (Terrarium, or
    /// Mapbox terrain-RGB when the name says so). Its size on the ground isn't in it: 10 m a pixel to
    /// start with, which the person changes.
    /// </summary>
    public static DemGrid Picture(byte[] data, string name, int maxSide = 2400)
    {
        var im = PngHeights.Decode(data);
        bool colour = im.Channels >= 3;
        bool terrainRgb = colour && (name.Contains("terrain-rgb", StringComparison.OrdinalIgnoreCase) || name.Contains("mapbox", StringComparison.OrdinalIgnoreCase));
        bool terrarium = colour && !terrainRgb && name.Contains("terrarium", StringComparison.OrdinalIgnoreCase);
        var r = new GridReducer(im.Width, im.Height, maxSide);
        bool alpha = im.Channels is 2 or 4;
        for (int y = 0; y < im.Height; y++)
            for (int x = 0; x < im.Width; x++)
            {
                if (alpha && im.At(x, y, im.Channels - 1) == 0) continue;
                double v = terrarium ? PngHeights.Terrarium(im.At(x, y, 0), im.At(x, y, 1), im.At(x, y, 2))
                    : terrainRgb ? PngHeights.TerrainRgb(im.At(x, y, 0), im.At(x, y, 1), im.At(x, y, 2))
                    : colour ? 0.299 * im.At(x, y, 0) + 0.587 * im.At(x, y, 1) + 0.114 * im.At(x, y, 2)
                    : im.At(x, y, 0);
                r.Add(x, y, v);
            }
        return r.Grid(10, 10) with
        {
            Georeferenced = false,
            Units = terrarium || terrainRgb ? HeightUnits.Metres : HeightUnits.Levels,
            Name = name,
            Source = "Heightmap picture",
        };
    }
}
