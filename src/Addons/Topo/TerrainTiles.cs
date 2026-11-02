using System.Net;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Heights for anywhere on Earth from the free Terrain Tiles on AWS (Mapzen / Tilezen "Terrarium"
/// tiles: 256-pixel PNGs, zoom 0 to 15, metres coded in the colour). No account or key. Tiles are
/// kept in a folder so moving the area about doesn't fetch them again.
/// </summary>
public sealed class TerrainTiles
{
    public const string Credit = "Heights: Terrain Tiles on AWS (Mapzen/Tilezen), from SRTM, USGS 3DEP, GMTED2010, ETOPO1, Copernicus EU-DEM and other national surveys.";
    public const string CreditUrl = "https://github.com/tilezen/joerd/blob/master/docs/attribution.md";
    public const int MaxZoom = 15;
    /// <summary>The most tiles fetched for one area (about 100 kB each).</summary>
    public const int MaxTiles = 64;
    const double EarthRadius = 6378137;

    readonly HttpClient _http;
    readonly string? _cache;

    public TerrainTiles(HttpClient http, string? cacheFolder)
    {
        _http = http;
        _cache = cacheFolder;
    }

    public static string Url(int z, int x, int y) => $"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";

    /// <summary>Where a latitude and longitude are on the world's tiles at zoom <paramref name="z"/>, in tile pixels.</summary>
    public static (double X, double Y) Pixel(double lat, double lon, int z)
    {
        double n = 256.0 * (1 << z);
        lat = Math.Clamp(lat, -85.05112878, 85.05112878);
        double phi = lat * Math.PI / 180;
        return ((lon + 180) / 360 * n, (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) / 2 * n);
    }

    /// <summary>Metres on the ground a tile pixel covers at that latitude and zoom.</summary>
    public static double MetresPerPixel(double lat, int z) => 2 * Math.PI * EarthRadius * Math.Cos(lat * Math.PI / 180) / (256.0 * (1 << z));

    /// <summary>A point some metres east and north of another (on a sphere, near enough over a few hundred km).</summary>
    public static (double Lat, double Lon) Offset(double lat, double lon, double eastM, double northM)
    {
        const double R = 6371008.8;
        double la = lat + northM / R * 180 / Math.PI;
        double lo = lon + eastM / (R * Math.Cos(lat * Math.PI / 180)) * 180 / Math.PI;
        return (Math.Clamp(la, -85, 85), (lo + 540) % 360 - 180);
    }

    /// <summary>The zoom whose pixels are at least as fine as <paramref name="spacingM"/>, coarser when that many tiles would be too many.</summary>
    public static int ZoomFor(double lat, double lon, double widthM, double heightM, double spacingM)
    {
        int z = 0;
        while (z < MaxZoom && MetresPerPixel(lat, z) > spacingM) z++;
        while (z > 0 && Tiles(lat, lon, widthM, heightM, z).Count > MaxTiles) z--;
        return z;
    }

    /// <summary>The tiles covering an area (its middle, size in metres) at a zoom.</summary>
    public static List<(int X, int Y)> Tiles(double lat, double lon, double widthM, double heightM, int z)
    {
        var (x0, y0, x1, y1) = TileRange(lat, lon, widthM, heightM, z);
        var l = new List<(int, int)>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) l.Add((x, y));
        return l;
    }

    static (int X0, int Y0, int X1, int Y1) TileRange(double lat, double lon, double widthM, double heightM, int z)
    {
        var nw = Offset(lat, lon, -widthM / 2, heightM / 2);
        var se = Offset(lat, lon, widthM / 2, -heightM / 2);
        var a = Pixel(nw.Lat, lon, z);
        var b = Pixel(se.Lat, lon, z);
        // Across: from the middle's pixel, so an area over the 180° line keeps going (tile numbers wrap when fetched).
        var mid = Pixel(lat, lon, z);
        double halfPx = widthM / 2 / MetresPerPixel(lat, z);
        int max = (1 << z) - 1;
        return ((int)Math.Floor((mid.X - halfPx - 1) / 256), Math.Clamp((int)Math.Floor((a.Y - 1) / 256), 0, max),
                (int)Math.Floor((mid.X + halfPx + 1) / 256), Math.Clamp((int)Math.Floor((b.Y + 1) / 256), 0, max));
    }

    /// <summary>
    /// A grid of heights <paramref name="widthM"/> × <paramref name="heightM"/> around a point,
    /// <paramref name="spacingM"/> apart, read from the tiles (fetched or from the folder).
    /// </summary>
    public async Task<DemGrid> GridAsync(double lat, double lon, double widthM, double heightM, double spacingM, string name, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int z = ZoomFor(lat, lon, widthM, heightM, spacingM);
        var (x0, y0, x1, y1) = TileRange(lat, lon, widthM, heightM, z);
        int tx = x1 - x0 + 1, ty = y1 - y0 + 1;
        var mosaic = new float[tx * 256 * ty * 256];
        Array.Fill(mosaic, float.NaN);
        int done = 0, failed = 0, total = tx * ty;
        using var gate = new SemaphoreSlim(6);
        string? problem = null;
        var jobs = new List<Task>();
        for (int j = 0; j < ty; j++)
            for (int i = 0; i < tx; i++)
            {
                int ci = i, cj = j;
                jobs.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        int n = 1 << z;
                        var png = await TileAsync(z, ((x0 + ci) % n + n) % n, y0 + cj, ct);
                        var im = PngHeights.Decode(png);
                        if (im.Width != 256 || im.Height != 256 || im.Channels < 3) throw new FormatException("a tile isn't a 256-pixel colour picture");
                        for (int y = 0; y < 256; y++)
                        {
                            int row = (cj * 256 + y) * tx * 256 + ci * 256;
                            for (int x = 0; x < 256; x++)
                                mosaic[row + x] = (float)PngHeights.Terrarium(im.At(x, y, 0), im.At(x, y, 1), im.At(x, y, 2));
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException or FormatException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failed);
                        problem ??= e.Message;
                    }
                    finally
                    {
                        gate.Release();
                        progress?.Report($"Getting the heights… {Interlocked.Increment(ref done)} of {total} tiles");
                    }
                }, ct));
            }
        await Task.WhenAll(jobs);
        ct.ThrowIfCancellationRequested();
        if (failed == total) throw new HttpRequestException($"Couldn't get the heights (is the internet connected?): {problem}");

        // Points east and north of the middle, read from the tiles where they fall.
        int w = Math.Max(2, (int)Math.Round(widthM / spacingM) + 1), h = Math.Max(2, (int)Math.Round(heightM / spacingM) + 1);
        double dx = widthM / (w - 1), dy = heightM / (h - 1);
        var zs = new float[w * h];
        int mw = tx * 256, mh = ty * 256;
        double ox = x0 * 256.0, oy = y0 * 256.0;
        await Task.Run(() => Parallel.For(0, h, j =>
        {
            double north = heightM / 2 - j * dy;
            for (int i = 0; i < w; i++)
            {
                var (la, lo) = Offset(lat, lon, -widthM / 2 + i * dx, north);
                var (px, py) = Pixel(la, lo, z);
                // Keep going across the 180° line rather than jumping back to the other side.
                double mx = px - ox, my = py - oy;
                double world = 256.0 * (1 << z);
                if (mx < -world / 2) mx += world;
                if (mx > world / 2) mx -= world;
                zs[j * w + i] = Bilinear(mosaic, mw, mh, mx - 0.5, my - 0.5);
            }
        }), ct);
        return new DemGrid(w, h, zs)
        {
            Dx = dx,
            Dy = dy,
            Lat = lat,
            Lon = lon,
            Name = name,
            Source = failed > 0 ? $"Terrain Tiles on AWS ({failed} of {total} tiles couldn't be fetched: gaps are filled in)" : "Terrain Tiles on AWS",
        };
    }

    static float Bilinear(float[] m, int w, int h, double x, double y)
    {
        x = Math.Clamp(x, 0, w - 1.001);
        y = Math.Clamp(y, 0, h - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double tx = x - x0, ty = y - y0;
        float a = m[y0 * w + x0], b = m[y0 * w + x0 + 1], c = m[(y0 + 1) * w + x0], d = m[(y0 + 1) * w + x0 + 1];
        // A missing tile next door: use what there is.
        double sum = 0, ws = 0;
        void Add(float v, double wt) { if (!float.IsNaN(v) && wt > 0) { sum += v * wt; ws += wt; } }
        Add(a, (1 - tx) * (1 - ty)); Add(b, tx * (1 - ty)); Add(c, (1 - tx) * ty); Add(d, tx * ty);
        return ws > 1e-9 ? (float)(sum / ws) : float.NaN;
    }

    async Task<byte[]> TileAsync(int z, int x, int y, CancellationToken ct)
    {
        string? file = _cache is null ? null : Path.Combine(_cache, z.ToString(), x.ToString(), y + ".png");
        if (file is not null && File.Exists(file))
        {
            try { return await File.ReadAllBytesAsync(file, ct); }
            catch (IOException) { }
        }
        byte[] png = [];
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var resp = await _http.GetAsync(Url(z, x, y), ct);
                if (resp.StatusCode == HttpStatusCode.NotFound) throw new HttpRequestException("a tile isn't there");
                resp.EnsureSuccessStatusCode();
                png = await resp.Content.ReadAsByteArrayAsync(ct);
                break;
            }
            catch (HttpRequestException) when (attempt < 1) { await Task.Delay(400, ct); }
        }
        if (file is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, png, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return png;
    }

    /// <summary>Keeps the tile folder under a size: the tiles looked at longest ago go first.</summary>
    public static void Trim(string folder, long maxBytes = 300L << 20)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var files = new DirectoryInfo(folder).GetFiles("*.png", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            foreach (var f in files)
            {
                total += f.Length;
                if (total > maxBytes) f.Delete();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
