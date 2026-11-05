using Avalonia.Controls;
using Avalonia.Layout;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>When the phone watches the machine with the app closed, and how it reaches the computer.</summary>
static class SettingsPage
{
    public static Control Build(MainView host, IPhone phone, Session? session)
    {
        var panel = new StackPanel { Spacing = 12 };
        if (phone.CanWatch)
        {
            var options = new StackPanel { Spacing = 10 };
            void Option(WatchMode mode, string title, string about)
            {
                var r = new RadioButton { GroupName = "watch", IsChecked = host.Settings.Watch == mode, Content = new StackPanel { Spacing = 2, Children = { Text(title).With(t => t.FontWeight = Avalonia.Media.FontWeight.SemiBold), Text(about, "muted") } } };
                r.IsCheckedChanged += async (_, _) =>
                {
                    if (r.IsChecked != true) return;
                    host.Settings = host.Settings with { Watch = mode };
                    if (mode != WatchMode.Off && !await phone.AllowNotificationsAsync())
                        host.Toast("Notifications are turned off for the app in Android's settings.");
                };
                options.Children.Add(r);
            }
            Option(WatchMode.DuringJobs, "While a job is on the machine", "Its progress (with a Pause button), a notification when it ends with a picture of the result, the machine's alarms, and a warning if the phone loses the computer. Stops itself after the job.");
            Option(WatchMode.Always, "Always", "Also catches jobs started at the machine or in the desktop app, and alarms between jobs. Keeps a connection open, so it uses a little more battery.");
            Option(WatchMode.Off, "Only while the app is open", "No notifications.");
            panel.Children.Add(Card("Notifications", options,
                Btn("Send a test notification", null, async () =>
                {
                    if (await phone.AllowNotificationsAsync()) phone.TestNotification();
                    else host.Toast("Notifications are turned off for the app in Android's settings.");
                }),
                Text("If notifications stop while the phone sleeps, set the app's battery use to Unrestricted (Android settings → Apps → PhotonCanvas Remote → Battery).", "muted")));
        }
        else
            panel.Children.Add(Card("Notifications", Text("The Android app tells you when a job ends or the machine raises an alarm, even when it's closed. Here, keep the app open.", "muted")));

        if (session?.Connection is { } c)
        {
            string Kind(string a) => ConnectionInfo.IsTailscale(a) ? "Tailscale" : "home network";
            var lines = new StackPanel { Spacing = 6 };
            lines.Children.Add(Text($"Connected to {c.Display} ({Kind(c.Address)}){(c.Secure ? ", encrypted to this computer's own certificate" : ", not encrypted (an older PhotonCanvas)")}."));
            if (c.Others is { Length: > 0 } others)
                lines.Children.Add(Text($"Also knows {string.Join(", ", others.Select(a => $"{a} ({Kind(a)})"))}: it uses whichever answers.", "muted"));
            lines.Children.Add(Text(c.AllAddresses.Any(ConnectionInfo.IsTailscale)
                ? "Away from home it reaches the computer through Tailscale (keep Tailscale on, on the phone)."
                : "To use it away from home: install Tailscale (free, tailscale.com) on the computer and on this phone and sign in to the same account on both. The app learns the computer's Tailscale address the next time it connects at home, then switches by itself.", "muted"));
            panel.Children.Add(Card("Connection", lines));
        }
        else if (session?.Remote.IsDemo == true)
            panel.Children.Add(Card("Connection", Text("The demo machine: nothing real moves or fires. ⋯ → Change computer connects to yours.", "muted")));
        return panel;
    }
}
