using Avalonia.Controls;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// Turning the rotary from the app. First choice: straight away, outside a job (the port's motor set
/// to hold, then turned, the way xTool Studio's conveyor feed buttons work). If the machine refuses
/// that, a tiny laser-off job that starts as soon as it arrives (after the user confirms).
/// </summary>
public static class RotaryTurn
{
    /// <summary>
    /// Turn the object <paramref name="degrees"/> straight away (+ the way a design runs down the page), with the rotary
    /// as set in Work mode, as Work mode's Turn by does. False if it isn't turning (the person has been told why).
    /// </summary>
    public static async Task<bool> TurnDegreesAsync(Window owner, AppSettings settings, MachinePanel machine, double degrees, string what)
    {
        if (WorkModes.From(settings) is not WorkMode.Rotary mode)
        {
            await Dialogs.Message(owner, "Not on the rotary", "Choose the rotary in Machine → Work mode first.");
            return false;
        }
        if (Math.Abs(degrees) < 0.05) return true;
        double circ = Math.PI * Math.Max(1, settings.RotaryDiameterMm);
        return await TurnAsync(owner, settings, machine, RotaryReturn.UnitsFor(degrees, mode, circ), mode, RotaryReturn.UFor(degrees, mode, circ), what);
    }

    /// <summary>
    /// Turn the rotary by <paramref name="motorUnits"/> (a job's U × its scale). <paramref name="mode"/> and
    /// <paramref name="jobU"/> are the same turn as a job, for when turning straight away is refused.
    /// Returns true once it's turning (or the job is on its way).
    /// </summary>
    public static async Task<bool> TurnAsync(Window owner, AppSettings settings, MachinePanel machine, double motorUnits, WorkMode.Rotary mode, double jobU, string what)
    {
        if (!machine.IsConnected)
        {
            await Dialogs.Message(owner, "Not connected", "Connect to the machine first (Machine, at the right end of the ribbon).");
            return false;
        }
        if (machine.Mode is "P_WORKING" or "P_PAUSE")
        {
            await Dialogs.Message(owner, "A job is running", "Wait for the job on the machine to finish (or stop it) first.");
            return false;
        }
        double u = motorUnits * await NowScaleAsync(settings, machine);
        if (!double.IsFinite(u) || Math.Abs(u) > Job.MaxTurnMm)
        {
            await Dialogs.Message(owner, "Can't turn it", "That's a very long way to turn. Turn it by hand instead.");
            return false;
        }
        try
        {
            await machine.TurnRotaryNowAsync(u);
            return true;
        }
        catch (DeviceException e) when (!e.IsConnectionProblem)
        {
            // Older firmware may not take the feed request: fall back to a job.
            return await SendJobAsync(owner, machine, mode, jobU, what + $"\n\n(Turning straight away didn't work: {e.Message} So it goes as a short job instead.)");
        }
        catch (DeviceException e)
        {
            await Dialogs.Message(owner, "Couldn't turn it", e.Message);
            return false;
        }
    }

    // The machine's own belt factor, read once a session (the feed is scaled by it).
    static double? _machineRate;

    /// <summary>
    /// The feed's units per motor unit: the one-turn check's when there is one, else 1 ÷ the belt factor
    /// stored on the machine (ConveyorURate), else 1 ÷ the conveyor setting here.
    /// </summary>
    internal static async Task<double> NowScaleAsync(AppSettings settings, MachinePanel machine)
    {
        if (settings.RotaryNowScale > 0) return settings.RotaryNowScale;
        if (_machineRate is null)
        {
            var configs = await machine.GetConfigsAsync();
            if (configs?["ConveyorURate"] is { } n && double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) && r is > 0.1 and < 10)
                _machineRate = r;
        }
        return _machineRate is { } rate ? 1 / rate : RotaryReturn.NowScale(settings);
    }

    /// <summary>
    /// The conveyor-feed units that turn the object <paramref name="degrees"/> straight away (positive the way a design
    /// runs down the page), with the saved rotary settings. Null when the work mode isn't the rotary.
    /// </summary>
    public static async Task<double?> FeedUnitsAsync(AppSettings settings, MachinePanel machine, double degrees)
    {
        if (WorkModes.From(settings) is not WorkMode.Rotary mode) return null;
        double circ = Math.PI * Math.Max(1, settings.RotaryDiameterMm);
        return RotaryReturn.UnitsFor(degrees, mode, circ) * await NowScaleAsync(settings, machine);
    }

    /// <summary>The scale turns use now, as far as is known without asking the machine.</summary>
    public static double KnownNowScale(AppSettings s) =>
        s.RotaryNowScale > 0 ? s.RotaryNowScale : _machineRate is { } r ? 1 / r : RotaryReturn.NowScale(s);

    /// <summary>The turn as a laser-off job that starts as soon as it arrives, after a confirm.</summary>
    static async Task<bool> SendJobAsync(Window owner, MachinePanel machine, WorkMode.Rotary mode, double uMm, string what)
    {
        byte[] xf;
        try { xf = RotaryReturn.TurnJob(mode, uMm); }
        catch (JobException e)
        {
            await Dialogs.Message(owner, "Can't turn it", e.Message);
            return false;
        }
        if (!await Dialogs.Confirm(owner, "Turn the rotary",
                what + "\n\nThe laser stays off. This one starts as soon as it reaches the machine, without its button: keep hands clear of the chuck." +
                (machine.Mode == "P_WORK" ? "\n\nThe job that's loaded on the machine but not started will be replaced." : ""),
                "Turn it", "Cancel"))
            return false;
        try
        {
            await machine.UploadJobAsync(xf, null, CancellationToken.None, replaceLoadedJob: true, name: "Turn the rotary", autoStart: true);
            return true;
        }
        catch (DeviceException e)
        {
            await Dialogs.Message(owner, "Couldn't send it", e.Message);
            return false;
        }
    }

    /// <summary>Turn back what a stopped job left, and forget it once it's turning.</summary>
    public static async Task<bool> ReturnAsync(Window owner, AppSettings settings, MachinePanel machine)
    {
        if (settings.RotaryLeftU is not { } left) return false;
        double deg = Math.Abs(RotaryReturn.Degrees(left, settings.RotaryLeftCircumferenceMm));
        bool sent = await TurnAsync(owner, settings, machine, RotaryReturn.UnitsBack(settings), RotaryReturn.ReturnMode(settings), -left,
            $"Turn the object back about {deg:0}° to where the stopped job started.");
        if (sent)
        {
            settings.RotaryLeftU = null;
            settings.Save();
        }
        return sent;
    }

    /// <summary>"85°": a left-over turn for the job bar.</summary>
    public static string Describe(AppSettings s) =>
        s.RotaryLeftU is { } u ? $"{Math.Abs(RotaryReturn.Degrees(u, s.RotaryLeftCircumferenceMm)):0}°" : "";
}
