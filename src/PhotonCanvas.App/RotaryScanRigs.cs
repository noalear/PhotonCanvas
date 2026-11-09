using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// Turning the rotary for a scan: straight away (the conveyor feed request, the motor held first), then waiting long
/// enough for the turn to finish, since the machine doesn't say when it has.
/// </summary>
abstract class RotaryScanRigBase(AppSettings settings, MachinePanel machine, double settleS) : IRotaryScanRig
{
    /// <summary>
    /// The speed turns are asked for (the feed's units a minute): a third of Work mode's, so a heavy bar on a
    /// direct-drive chuck starts and stops gently instead of slipping (a quarter turn takes about 1.6 s).
    /// </summary>
    const double Speed = 400;

    protected MachinePanel Machine => machine;

    public async Task TurnAsync(double degrees, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        double u = await RotaryTurn.FeedUnitsAsync(settings, machine, degrees)
            ?? throw new InvalidOperationException("Choose the rotary in Work mode first.");
        if (!double.IsFinite(u) || Math.Abs(u) > Job.MaxTurnMm)
            throw new InvalidOperationException("That turn is far too long: check the rotary's scale in Work mode.");
        // Measuring (moving the focus, auto-focusing) leaves the machine in focus mode, where it holds the rotary
        // but doesn't turn it (Kyle's scan, 2026-11-08: it held and buzzed). Always back to idle first, as the
        // stock app does when it leaves focus mode, whatever the machine panel last heard the mode was.
        AppLog.Write("scan", $"Turn {degrees:0.###}° = {u:0.###} feed units (panel mode {machine.Mode}).");
        if (machine.Client is { } c)
        {
            await c.ReturnToIdleAsync();
            await Task.Delay(400, ct);
        }
        await machine.TurnRotaryNowAsync(u, Speed);
        // How long the turn takes at that speed (with room to spare), plus time for the part to stop swinging.
        double s = Math.Abs(u) / (Speed / 60) * 1.5 + 0.3 + Math.Max(0, settleS);
        await Task.Delay(TimeSpan.FromSeconds(s), ct);
    }

    public abstract Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct);
}

/// <summary>
/// Measuring along the laser line with the blue pointer and the close-up camera (calibrated once on a flat board).
/// Each head height reads the surface within about 8 mm of it. At each angle the machine's own auto-focus first
/// finds the part's top in the middle of the field, then the head steps up through the heights around that and
/// what the last angle read, lowest first, and stops once every place is read. (Kyle's scan, 2026-11-09: started
/// from the board's focus, the head never got near the bar's top, so nothing was read.) If the auto-focus fails,
/// or nothing reads around it, it searches the whole range.
/// </summary>
sealed class PointerScanRig(AppSettings settings, MachinePanel machine, double settleS, SurfaceProbe probe, PointerCalibration cal,
    double lineY, double startFocus, IProgress<string> progress) : RotaryScanRigBase(settings, machine, settleS)
{
    /// <summary>How far from the head's focus the calibration reads (mm either way).</summary>
    readonly double _reach = cal.Depths.Count == 0 ? 8 : Math.Min(Math.Abs(cal.Depths.Min()), Math.Abs(cal.Depths.Max()));
    bool _first = true;

    public override async Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct)
    {
        var got = new double?[xs.Count];
        double hi = DeviceClient.MaxHeadZ - MachineProfile.F2Ultra.ZOffset;
        // Plan from the part's top where the machine focuses and the places the last angle read.
        List<double?> known = _first ? [] : [.. guesses.Where(g => g is not null)];
        _first = false;
        if (await MiddleTopAsync(ct) is { } top) known.Add(top);
        var levels = RotaryScanner.FocusSweep(known, startFocus, _reach, 0, hi);
        // How far from the head's focus each place was read: readings near the edge of the reach are the least sure, so
        // such a place is tried again at the next height up and the reading nearest the focus is kept.
        var off = new double[xs.Count];
        await ReadAsync(levels);
        if (known.Count > 0 && got.All(g => g is null))
        {
            // Nothing where it was expected (the auto-focus caught the chuck or the base, say): look everywhere.
            AppLog.Write("scan", "Nothing read around the expected height: searching the whole range.");
            await ReadAsync([.. RotaryScanner.FocusSweep(new double?[] { 0, hi }, startFocus, _reach, 0, hi, 12).Except(levels)]);
        }
        return got;

        async Task ReadAsync(IEnumerable<double> heights)
        {
        foreach (var f in heights)
        {
            // Places outside what the pointer was calibrated over can't be read: not even photographed.
            var pending = Enumerable.Range(0, xs.Count)
                .Where(i => cal.Covers(new Vec2(xs[i], lineY)) && (got[i] is null || off[i] > _reach * 0.6)).ToList();
            if (pending.Count == 0) break;
            var aims = pending.Select(i => new Vec2(xs[i], lineY)).ToList();
            var h = await probe.ReadAtAsync(aims, f, cal, progress, ct);
            for (int k = 0; k < pending.Count; k++)
            {
                int i = pending[k];
                if (h[k] is not { } v || got[i] is not null && Math.Abs(v - f) >= off[i]) continue;
                got[i] = v;
                off[i] = Math.Abs(v - f);
            }
        }
        }
    }

    /// <summary>The height of the part's top in the middle of the field by the machine's own auto-focus; null if it didn't work.</summary>
    async Task<double?> MiddleTopAsync(CancellationToken ct)
    {
        if (Machine.Client is not { IsConnected: true } c) return null;
        progress.Report("Auto-focusing on the part's top to find its height…");
        try
        {
            double top = await c.AutoFocusAsync(cancellationToken: ct) - MachineProfile.F2Ultra.ZOffset;
            AppLog.Write("scan", $"Auto-focus in the middle: the part's top is at {top:0.0} mm.");
            return double.IsFinite(top) ? top : null;
        }
        catch (Exception e) when (e is DeviceException or TimeoutException or InvalidOperationException)
        {
            AppLog.Write("scan", $"Auto-focus in the middle didn't work ({e.Message}): searching by heights.");
            return null;
        }
    }
}

/// <summary>
/// Measuring with the machine's own auto-focus: one reading an angle, wherever the machine measures (the middle of
/// the field). The head moves to the part's height each time.
/// </summary>
sealed class AutoFocusScanRig(AppSettings settings, MachinePanel machine, double settleS) : RotaryScanRigBase(settings, machine, settleS)
{
    public override async Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct)
    {
        if (Machine.Client is not { IsConnected: true } c) throw new DeviceException(DeviceException.NotConnectedCode, "Not connected to a machine.");
        double z = await c.AutoFocusAsync(cancellationToken: ct);
        var o = new double?[xs.Count];
        o[0] = z - MachineProfile.F2Ultra.ZOffset;
        return o;
    }
}
