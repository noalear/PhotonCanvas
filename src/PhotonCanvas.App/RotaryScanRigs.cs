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
    /// <summary>The speed turns are asked for (the feed's units a minute), as Work mode's turns.</summary>
    const double Speed = 1200;

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
/// Measuring along the laser line with the red pointer and the close-up camera (calibrated once on a flat board).
/// Each head height reads the surface within about 8 mm of it, so at each angle the head steps up through the
/// heights the places are expected at (what the last angle read there), lowest first, and stops once every place
/// is read. Places that couldn't be read at the first angle (out of the camera's sight) don't widen later angles.
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
        // After the first angle, plan from the places that have been read (the rest are tried at the same heights).
        var known = guesses.Where(g => g is not null).ToList();
        var levels = RotaryScanner.FocusSweep(_first || known.Count == 0 ? [] : known, startFocus, _reach, 0, hi);
        _first = false;
        foreach (var f in levels)
        {
            var pending = Enumerable.Range(0, xs.Count).Where(i => got[i] is null).ToList();
            if (pending.Count == 0) break;
            var aims = pending.Select(i => new Vec2(xs[i], lineY)).ToList();
            var h = await probe.ReadAtAsync(aims, f, cal, progress, ct);
            for (int k = 0; k < pending.Count; k++) got[pending[k]] = h[k];
        }
        return got;
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
