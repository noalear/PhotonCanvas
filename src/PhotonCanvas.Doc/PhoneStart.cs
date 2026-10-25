using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhotonCanvas.Doc;

/// <summary>
/// Whether the phone may start a job that is loaded on the machine (remote\phone-start.json). Off until
/// the user turns it on in the desktop app's Remote access window, after the full warning. The server
/// reads it at every start, so turning it off takes effect at once.
/// </summary>
public static class PhoneStart
{
    public static string FilePath(string? dir = null) => Path.Combine(dir ?? RemoteConfig.Dir, "phone-start.json");

    /// <summary>True only when the user has turned it on; anything unreadable counts as off.</summary>
    public static bool IsAllowed(string? dir = null)
    {
        try
        {
            return File.Exists(FilePath(dir)) && JsonNode.Parse(File.ReadAllText(FilePath(dir)))?["allowed"]?.GetValue<bool>() == true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public static void Set(bool allowed, string? dir = null)
    {
        var path = FilePath(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, new JsonObject { ["allowed"] = allowed }.ToJsonString());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>What the phone is told when it tries to start a job while this is off.</summary>
    public const string OffMessage =
        "Starting jobs from the phone is turned off. Press the machine's button to start, or turn on " +
        "\"Allow starting jobs from the phone\" in PhotonCanvas on the computer (Laser → Remote access).";

    /// <summary>Shown before it is turned on; the user must confirm.</summary>
    public const string Warning =
        "With this on, a job loaded on the machine can be started from the phone, which fires the laser, possibly with nobody at the machine.\n\n" +
        "• The phone only starts a job after you've looked at a picture of the bed taken less than 2 minutes ago with that job loaded. Look at it properly: material in place and flat, nothing in the way, the lid as you want it.\n" +
        "• The machine blanks its cameras while a job runs, so you can't watch the job itself. A fire can start and grow unseen. Its flame alarm must stay on, and someone should be close enough to act.\n" +
        "• Anyone with this computer's access code can start a job. Keep the code private and make a new one if a phone you don't trust has seen it.\n" +
        "• Direct or reflected laser light can permanently blind; smoke and fumes are harmful. Keep children and pets away from the machine.\n\n" +
        "You can turn it off again here at any time. You use it entirely at your own risk; this software comes with no warranty.";
}
