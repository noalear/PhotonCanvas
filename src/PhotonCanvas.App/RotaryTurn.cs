using Avalonia.Controls;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// Turning the rotary from the app: the F2 Ultra ignores U moves sent on their own, so each turn is a
/// tiny laser-off job that starts as soon as it arrives (after the user confirms).
/// </summary>
static class RotaryTurn
{
    /// <summary>Turn the rotary by <paramref name="uMm"/> (the job's own U units). Returns true once the job is on its way.</summary>
    public static async Task<bool> SendAsync(Window owner, MachinePanel machine, WorkMode.Rotary mode, double uMm, string what)
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

    /// <summary>Turn back what a stopped job left, and forget it once it's sent.</summary>
    public static async Task<bool> ReturnAsync(Window owner, AppSettings settings, MachinePanel machine)
    {
        if (settings.RotaryLeftU is not { } left) return false;
        double deg = Math.Abs(RotaryReturn.Degrees(left, settings.RotaryLeftCircumferenceMm));
        bool sent = await SendAsync(owner, machine, RotaryReturn.ReturnMode(settings), -left,
            $"Turn the object back about {deg:0}° to where the stopped job started. That's worked out from how far the job had got, so check your mark afterwards and fine-tune with Turn by in Laser → Work mode.");
        if (sent)
        {
            settings.RotaryLeftU = null;
            settings.Save();
        }
        return sent;
    }

    /// <summary>"85° round": a left-over turn for the job bar.</summary>
    public static string Describe(AppSettings s) =>
        s.RotaryLeftU is { } u ? $"{Math.Abs(RotaryReturn.Degrees(u, s.RotaryLeftCircumferenceMm)):0}°" : "";
}
