using Avalonia.Controls;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>What the app is, its privacy (it collects nothing), safety and licence.</summary>
static class AboutPage
{
    public static Control Build(IPhone phone) => new StackPanel
    {
        Spacing = 12,
        Children =
        {
            Text($"PhotonCanvas Remote {phone.Version}").With(t => t.FontWeight = Avalonia.Media.FontWeight.SemiBold),
            Text("Watch and run your laser through PhotonCanvas on your own computer: its state, its camera, pause and stop, your designs, and starting a job."),
            Card("Privacy",
                Text("The app talks only to PhotonCanvas on your own computer, at the address you give it. It has no accounts, no ads and no analytics, and sends nothing to us or to anyone else. The computer's address and access code stay on this phone, in the app's own storage."),
                Text("Scanning the QR code uses Google Play services' code scanner, a Google component that works under Google's terms and may communicate with Google. The app itself only receives the text of the code.", "muted")),
            Card("Safety",
                Text("Starting a job fires the laser, maybe with nobody at the machine. It's off unless you've turned on \"Allow starting jobs from the phone\" in PhotonCanvas on the computer, and even then the app only starts a job after you've looked at a fresh picture of the bed. Look at it properly. Keep the machine's own safety features (flame alarm, lid, extraction) working, and never leave a laser job unwatched for long."),
                Text("The machine blanks its cameras while a job runs: you can't watch the job itself.", "muted")),
            Card("Licence",
                Text("Free to use; all rights reserved. It may not be sold, redistributed or published in a modified form without written permission. Third-party components keep their own licences. Not made by, affiliated with or endorsed by xTool.", "muted")),
        },
    };
}
