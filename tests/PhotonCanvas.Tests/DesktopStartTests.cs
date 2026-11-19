using System.Text.Json;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Starting a loaded job from the desktop app's job bar: off unless the user turned it on.</summary>
public class DesktopStartTests
{
    [Fact]
    public void ItsOffToBeginWithAndInOldSettingsFiles()
    {
        Assert.False(new AppSettings().AllowDesktopStart);
        // Settings saved before the switch existed don't have it: still off.
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{\"AutoConnect\":true}")!.AllowDesktopStart);
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"AllowDesktopStart\":true}")!.AllowDesktopStart);
    }

    [Fact]
    public void EveryStartSaysTheLaserWillFireAndWhoseJobItIs()
    {
        Assert.Equal("Start \"Coaster\" now?", DesktopStart.ConfirmTitle("Coaster"));
        Assert.Equal("Start the loaded job now?", DesktopStart.ConfirmTitle(null));
        var text = DesktopStart.ConfirmText("Loaded: \"Coaster\", sent from this computer at 14:05.");
        Assert.StartsWith("Loaded: \"Coaster\"", text);
        Assert.Contains("The laser will fire", text);
        Assert.Contains("The laser will fire", DesktopStart.ConfirmText(null));
        Assert.Contains("own risk", DesktopStart.Warning);
        Assert.Contains("flame alarm", DesktopStart.Warning);
    }
}
