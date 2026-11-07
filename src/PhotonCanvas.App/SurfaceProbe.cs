using System.Globalization;
using System.Text.Json;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Device;

namespace PhotonCanvas.App;

/// <summary>
/// Measuring a curved piece with the red pointer and the close-up camera in the head. Only runs
/// when asked (a button): the head moves up and down and the pointer is aimed with the mirrors,
/// but the cutting laser is never fired. Fill lights are turned off while photographing and put back.
/// </summary>
public sealed class SurfaceProbe(DeviceClient client)
{
    const int CloseUp = 1;
    static readonly double ZOffset = MachineProfile.F2Ultra.ZOffset;

    static string CalibrationFile => Path.Combine(AppIdentity.DataDir, "pointer-calibration.json");

    sealed record Saved(double[] Depths, double[][] Aims, double[]?[][] Seen);

    public static PointerCalibration? LoadCalibration()
    {
        try
        {
            if (!File.Exists(CalibrationFile)) return null;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(CalibrationFile));
            if (s is null) return null;
            return new PointerCalibration(s.Depths, [.. s.Aims.Select(a => new Vec2(a[0], a[1]))],
                [.. s.Seen.Select(row => (IReadOnlyList<Vec2?>)[.. row.Select(p => p is { Length: 2 } ? new Vec2(p[0], p[1]) : (Vec2?)null)])]);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    static void SaveCalibration(PointerCalibration c)
    {
        Directory.CreateDirectory(AppIdentity.DataDir);
        var s = new Saved([.. c.Depths], [.. c.Aims.Select(a => new[] { a.X, a.Y })],
            [.. c.Seen.Select(row => row.Select(p => p is { } v ? new[] { v.X, v.Y } : null).ToArray())]);
        File.WriteAllText(CalibrationFile, JsonSerializer.Serialize(s));
    }

    /// <summary>Aim points for a grid over an area (row by row).</summary>
    public static List<Vec2> Grid(Rect2 area, int rows, int cols)
    {
        var o = new List<Vec2>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                o.Add(new Vec2(area.MinX + area.Width * (cols == 1 ? 0.5 : (double)c / (cols - 1)), area.MinY + area.Height * (rows == 1 ? 0.5 : (double)r / (rows - 1))));
        return o;
    }

    /// <summary>
    /// Calibrate on a flat board <paramref name="boardMm"/> thick: the head steps through
    /// <see cref="PointerCalibration.DefaultDepths"/> around it and the dot is photographed at
    /// every aim at each step. Saved for next time.
    /// </summary>
    public async Task<PointerCalibration> CalibrateAsync(double boardMm, IReadOnlyList<Vec2> aims, IProgress<string> progress, CancellationToken ct)
    {
        var depths = PointerCalibration.DefaultDepths;
        var seen = aims.Select(_ => new Vec2?[depths.Length]).ToArray();
        await Session(async () =>
        {
            for (int d = 0; d < depths.Length; d++)
            {
                progress.Report($"Head to {depths[d]:+0;-0;0} mm from the board ({d + 1} of {depths.Length})…");
                await client.MoveFocusAsync(boardMm + depths[d], ZOffset, cancellationToken: ct);
                var off = await PhotoAsync(null, ct);
                for (int i = 0; i < aims.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report($"Height step {d + 1} of {depths.Length}: point {i + 1} of {aims.Count}");
                    var on = await PhotoAsync(aims[i], ct);
                    seen[i][d] = DotFinder.Find(on, off)?.Centre;
                }
            }
            await client.MoveFocusAsync(boardMm, ZOffset, cancellationToken: ct);
        }, ct);
        int found = seen.Sum(r => r.Count(p => p is not null));
        if (found < aims.Count * depths.Length / 3)
            throw new InvalidOperationException($"The dot was only found in {found} of {aims.Count * depths.Length} photos. Check the close-up camera sees the area, and the board isn't shiny.");
        var cal = new PointerCalibration(depths, aims, [.. seen.Select(r => (IReadOnlyList<Vec2?>)r)]);
        SaveCalibration(cal);
        return cal;
    }

    /// <summary>
    /// Measure the heights at <paramref name="aims"/> with the head focused at
    /// <paramref name="focusMm"/> (the material height the focus is set to). Points where the dot
    /// isn't found, or is outside what the calibration covers, come back null.
    /// </summary>
    public async Task<List<double?>> MeasureAsync(IReadOnlyList<Vec2> aims, double focusMm, PointerCalibration cal, IProgress<string> progress, CancellationToken ct)
    {
        var heights = new List<double?>();
        await Session(async () => heights = await ReadAtAsync(aims, focusMm, cal, progress, ct), ct);
        return heights;
    }

    /// <summary>
    /// The heights at <paramref name="aims"/> with the head moved to focus at <paramref name="focusMm"/>, inside a
    /// session (<see cref="RunAsync"/>) that has already darkened the lights. Null where the dot isn't read.
    /// </summary>
    public async Task<List<double?>> ReadAtAsync(IReadOnlyList<Vec2> aims, double focusMm, PointerCalibration cal, IProgress<string>? progress, CancellationToken ct)
    {
        var heights = new List<double?>();
        progress?.Report($"Head to focus at {focusMm:0.#} mm…");
        await client.MoveFocusAsync(focusMm, ZOffset, cancellationToken: ct);
        var off = await PhotoAsync(null, ct);
        for (int i = 0; i < aims.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Point {i + 1} of {aims.Count} (head at {focusMm:0.#} mm)");
            var on = await PhotoAsync(aims[i], ct);
            heights.Add(DotFinder.Find(on, off) is { } dot && cal.Depth(aims[i], dot.Centre) is { } depth ? focusMm - depth : null);
        }
        return heights;
    }

    /// <summary>
    /// Run <paramref name="run"/> as one measuring session: checks the machine is idle, darkens the fill lights, and
    /// puts the lights and pointer back afterwards (even if it fails or is stopped).
    /// </summary>
    public Task RunAsync(Func<Task> run, CancellationToken ct) => Session(run, ct);

    /// <summary>A close-up photo with the pointer aimed at <paramref name="aim"/> (or off, for null).</summary>
    async Task<RasterImage> PhotoAsync(Vec2? aim, CancellationToken ct)
    {
        if (aim is { } a)
        {
            await client.SendCommandAsync(string.Create(CultureInfo.InvariantCulture, $"G0X{a.X:0.###}Y{a.Y:0.###}F60000"), ct);
            await client.SetPointerPowerAsync(100, ct);
            await Task.Delay(120, ct); // let the mirrors and the camera settle
        }
        else
        {
            await client.SetPointerPowerAsync(0, ct);
            await Task.Delay(80, ct);
        }
        var jpg = await client.SnapshotAsync(CloseUp, ct);
        return RasterImage.Decode(jpg);
    }

    /// <summary>Checks the machine is idle, darkens the fill lights, runs, then puts the lights and pointer back.</summary>
    async Task Session(Func<Task> run, CancellationToken ct)
    {
        var mode = await client.GetRuntimeModeAsync(ct);
        if (mode is not ("P_IDLE" or "P_WORK_DONE" or "P_FINISH" or ""))
            throw new InvalidOperationException($"The machine is busy ({mode}). Try again when it's idle.");
        var lights = await client.GetFillLightsAsync(ct);
        var pointer = await client.GetFocusPowerAsync(ct);
        try
        {
            await client.SetFillLightsAsync(0, 0, ct);
            await run();
        }
        finally
        {
            try
            {
                await client.SetFillLightsAsync(lights.Front, lights.Back, CancellationToken.None);
                if (pointer is { } p) await client.SetPointerPowerAsync(p, CancellationToken.None);
            }
            catch (DeviceException) { }
        }
    }
}
