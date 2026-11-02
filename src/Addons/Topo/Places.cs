using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotonCanvas.Addon.Topo;

/// <summary>A place found: its name, its middle, and how big it is (when the search knows).</summary>
public sealed record Place(string Name, double Lat, double Lon, double? WidthM = null, double? HeightM = null);

/// <summary>
/// Finding a place from what the person types: coordinates in any common form, a link copied from
/// a web map (Google, OpenStreetMap, Apple, Bing), or a name looked up with OpenStreetMap's
/// Nominatim (only when the Find button is pressed: one search a press, as its rules ask).
/// </summary>
public static partial class Places
{
    public const string SearchCredit = "Place search: Nominatim, © OpenStreetMap contributors.";

    /// <summary>Coordinates or a map link, read without asking anyone; null when it's a name to search for.</summary>
    public static Place? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        string decoded = Uri.UnescapeDataString(text.Replace('+', ' '));
        // Map links: Google "@lat,lon,zoom" / "q=lat,lon" / "ll=", OpenStreetMap "#map=z/lat/lon" or "mlat=&mlon=", Bing "cp=lat~lon".
        foreach (var re in new[] { GoogleAt(), QueryLatLon(), OsmMap(), BingCp() })
            if (re.Match(decoded) is { Success: true } m && Pair(m.Groups["lat"].Value, m.Groups["lon"].Value) is { } p)
                return p with
                {
                    // Google's links to a place carry its name: …/maps/place/Mount Rainier/@46.85,…
                    Name = GooglePlace().Match(decoded) is { Success: true } named ? named.Groups[1].Value.Trim() : p.Name,
                    WidthM = ZoomWidth(m.Groups["z"].Value, p.Lat),
                };
        if (OsmMlat().Match(decoded) is { Success: true } o && OsmMlon().Match(decoded) is { Success: true } o2 && Pair(o.Groups[1].Value, o2.Groups[1].Value) is { } q)
            return q;
        // Degrees, minutes, seconds: 46°51′10″N 121°45′36″W (any of ° ' " ′ ″ or spaces).
        if (Dms().Match(decoded) is { Success: true } d)
        {
            double Deg(string deg, string min, string sec, string hemi)
            {
                double v = double.Parse(deg, CultureInfo.InvariantCulture) + (min.Length > 0 ? double.Parse(min, CultureInfo.InvariantCulture) / 60 : 0) + (sec.Length > 0 ? double.Parse(sec, CultureInfo.InvariantCulture) / 3600 : 0);
                return hemi.ToUpperInvariant() is "S" or "W" ? -v : v;
            }
            double a = Deg(d.Groups[1].Value, d.Groups[2].Value, d.Groups[3].Value, d.Groups[4].Value);
            double b = Deg(d.Groups[5].Value, d.Groups[6].Value, d.Groups[7].Value, d.Groups[8].Value);
            bool latFirst = d.Groups[4].Value.ToUpperInvariant() is "N" or "S";
            var (lat, lon) = latFirst ? (a, b) : (b, a);
            if (Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180) return new Place(Format(lat, lon), lat, lon);
        }
        // Plain decimal degrees: "46.8523, -121.7603" (latitude first, as maps give them).
        if (Decimal().Match(decoded) is { Success: true } dd && Pair(dd.Groups[1].Value, dd.Groups[2].Value) is { } r) return r;
        return null;
    }

    static Place? Pair(string la, string lo)
    {
        if (!double.TryParse(la, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(lo, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
        return new Place(Format(lat, lon), lat, lon);
    }

    /// <summary>About how much ground a web map shows across at a zoom (a 1000-pixel-wide window).</summary>
    static double? ZoomWidth(string z, double lat) =>
        double.TryParse(z, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && zoom is >= 1 and <= 22
            ? Math.Clamp(1000 * 156543.03 * Math.Cos(lat * Math.PI / 180) / Math.Pow(2, zoom), 200, 2_000_000) : null;

    public static string Format(double lat, double lon) =>
        $"{Math.Abs(lat).ToString("0.####", CultureInfo.InvariantCulture)}°{(lat >= 0 ? "N" : "S")} {Math.Abs(lon).ToString("0.####", CultureInfo.InvariantCulture)}°{(lon >= 0 ? "E" : "W")}";

    [GeneratedRegex(@"@(?<lat>-?\d+(?:\.\d+)?),(?<lon>-?\d+(?:\.\d+)?)(?:,(?<z>\d+(?:\.\d+)?)z)?")]
    private static partial Regex GoogleAt();
    [GeneratedRegex(@"[?&](?:q|ll|query|center|sll)=(?<lat>-?\d+(?:\.\d+)?)\s*,\s*(?<lon>-?\d+(?:\.\d+)?)(?:.*?[?&]z=(?<z>\d+(?:\.\d+)?))?")]
    private static partial Regex QueryLatLon();
    [GeneratedRegex(@"map=(?<z>\d+(?:\.\d+)?)/(?<lat>-?\d+(?:\.\d+)?)/(?<lon>-?\d+(?:\.\d+)?)")]
    private static partial Regex OsmMap();
    [GeneratedRegex(@"cp=(?<lat>-?\d+(?:\.\d+)?)~(?<lon>-?\d+(?:\.\d+)?)(?:.*?lvl=(?<z>\d+(?:\.\d+)?))?")]
    private static partial Regex BingCp();
    [GeneratedRegex(@"/place/([^/@?]+)/")]
    private static partial Regex GooglePlace();
    [GeneratedRegex(@"mlat=(-?\d+(?:\.\d+)?)")]
    private static partial Regex OsmMlat();
    [GeneratedRegex(@"mlon=(-?\d+(?:\.\d+)?)")]
    private static partial Regex OsmMlon();
    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*[°º\s]\s*(?:(\d+(?:\.\d+)?)\s*['′’\s]\s*)?(?:(\d+(?:\.\d+)?)\s*(?:""|″|”|''|′′)?\s*)?([NSEW])[\s,;]+(\d+(?:\.\d+)?)\s*[°º\s]\s*(?:(\d+(?:\.\d+)?)\s*['′’\s]\s*)?(?:(\d+(?:\.\d+)?)\s*(?:""|″|”|''|′′)?\s*)?([NSEW])\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Dms();
    [GeneratedRegex(@"^\s*\(?\s*(-?\d{1,2}(?:\.\d+)?)\s*[,;\s]\s*(-?\d{1,3}(?:\.\d+)?)\s*\)?\s*$")]
    private static partial Regex Decimal();

    /// <summary>Places called that, best first (OpenStreetMap's Nominatim).</summary>
    public static async Task<List<Place>> SearchAsync(HttpClient http, string text, CancellationToken ct = default)
    {
        var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&q=" + Uri.EscapeDataString(text.Trim());
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept-Language", "en");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return ParseSearch(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Nominatim's answer: a list of places with "lat", "lon", "display_name" and "boundingbox" (south, north, west, east).</summary>
    public static List<Place> ParseSearch(string json)
    {
        var l = new List<Place>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return l;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            static double Num(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.Parse(v.GetString() ?? "nan", CultureInfo.InvariantCulture);
            if (!e.TryGetProperty("lat", out var la) || !e.TryGetProperty("lon", out var lo)) continue;
            double lat = Num(la), lon = Num(lo);
            string name = e.TryGetProperty("display_name", out var n) ? n.GetString() ?? "" : Format(lat, lon);
            double? w = null, h = null;
            if (e.TryGetProperty("boundingbox", out var bb) && bb.ValueKind == JsonValueKind.Array && bb.GetArrayLength() == 4)
            {
                double s = Num(bb[0]), no = Num(bb[1]), we = Num(bb[2]), ea = Num(bb[3]);
                h = (no - s) * GeoTiff.MetresPerDegree;
                w = (ea - we) * GeoTiff.MetresPerDegree * Math.Cos(lat * Math.PI / 180);
            }
            l.Add(new Place(name, lat, lon, w, h));
        }
        return l;
    }

    /// <summary>The first part of a long place name ("Mount Rainier, Pierce County, Washington, …" → "Mount Rainier").</summary>
    public static string Short(string name) => name.Split(',')[0].Trim() is { Length: > 0 } s ? s : name;
}
