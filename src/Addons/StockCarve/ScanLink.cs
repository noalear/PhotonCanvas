using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.StockCarve;

/// <summary>The stock from the last "Scan the part" (Machine → Work area), as a stock map.</summary>
static class ScanLink
{
    /// <summary>A scan (or a stock model saved as one) as a stock map, for a rotary whose axis is <paramref name="axisHeightMm"/> above the bed.</summary>
    public static StockMap FromScan(RotaryScan scan, double axisHeightMm) =>
        StockMap.FromRadii(scan.AnglesDeg, scan.Xs, [.. scan.Radii(axisHeightMm).Select(r => (IReadOnlyList<double?>)r)], scan.WholeTurn);

    /// <summary>
    /// The stock from the last scan: the shape it was fitted to (a standard bar) or as measured, whichever the
    /// scan window last made the stock model from; or why there isn't one.
    /// </summary>
    public static (StockMap? Stock, string Note) LastScan(AppSettings settings)
    {
        RotaryScan? scan;
        try { scan = RotaryScanWindow.LoadStock(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            return (null, $"Couldn't read the last scan: {e.Message}");
        }
        if (scan is null) return (null, "There's no scan yet: scan the part first (Machine → Scan the part, in rotary mode).");
        if ((scan.AxisHeightMm ?? settings.RotaryAxisHeightMm) is not { } axis)
            return (null, "The scan can't be turned into a shape until the rotary's axis height is known: measure it in Scan the part first.");
        var stock = FromScan(scan, axis);
        string what = scan.Stock is { Length: > 0 } s ? $"the last scan, as {s}" : "the last scan";
        return (stock, $"From {what} ({scan.When.ToLocalTime():d MMM, HH:mm})");
    }
}
