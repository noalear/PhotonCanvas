namespace PhotonCanvas.Doc;

/// <summary>
/// Starting a loaded job from the desktop app's job bar (<see cref="AppSettings.AllowDesktopStart"/>).
/// Off until the user turns it on in Settings → Machine, after <see cref="Warning"/>; every start is
/// confirmed again with <see cref="ConfirmText"/>. Otherwise jobs start only from the machine's button
/// (or the phone, with its own switch).
/// </summary>
public static class DesktopStart
{
    /// <summary>Shown before it is turned on; the user must confirm.</summary>
    public const string Warning =
        "With this on, the job bar gets a Start button once a job is loaded on the machine. It fires the laser without anyone pressing the machine's own button, so nobody has to be standing at the machine.\n\n" +
        "• Before every start, look at the machine yourself: material in place and flat, nothing in the way, the lid as you want it. Each start asks you to confirm first.\n" +
        "• The machine blanks its cameras while a job runs. A fire can start and grow unseen. Its flame alarm must stay on, and someone should be close enough to act.\n" +
        "• A job loaded by another program, the phone or the machine's own screen can be started too: the confirmation says whose it is.\n" +
        "• Direct or reflected laser light can permanently blind; smoke and fumes are harmful. Keep children and pets away from the machine.\n\n" +
        "You can turn it off again in Settings → Machine at any time. You use it entirely at your own risk; this software comes with no warranty.";

    /// <summary>The question asked before each start from the job bar.</summary>
    public static string ConfirmTitle(string? design) => design is { Length: > 0 } d ? $"Start \"{d}\" now?" : "Start the loaded job now?";

    /// <summary>What each start says before the laser fires; <paramref name="whose"/> says whose job is loaded, when known.</summary>
    public static string ConfirmText(string? whose) =>
        (whose is { Length: > 0 } w ? w + "\n\n" : "") +
        "The laser will fire. Is the material in place, the lid as you want it, and nothing in the way? Stay where you can reach the machine until it's done.";
}
