using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;

namespace PhotonCanvas.Remote.Android;

/// <summary>The app's notifications: the ongoing one while watching, a job's end, the machine's alarms.</summary>
static class Notices
{
    // Channels: users can turn each off (or make it quieter) in the phone's settings.
    public const string Watching = "watching", Jobs = "jobs", Alarms = "alarms";
    public const int OngoingId = 1, JobId = 2, AlarmId = 3, LostId = 4, TestId = 5;

    public static NotificationManager Manager(Context c) => (NotificationManager)c.GetSystemService(Context.NotificationService)!;

    public static void EnsureChannels(Context c)
    {
        var m = Manager(c);
        if (m.GetNotificationChannel(Watching) is null)
            m.CreateNotificationChannel(new NotificationChannel(Watching, "Watching the laser", NotificationImportance.Low)
            {
                Description = "The job on the machine and its progress, while the app keeps watching.",
            });
        if (m.GetNotificationChannel(Jobs) is null)
            m.CreateNotificationChannel(new NotificationChannel(Jobs, "Jobs finished or stopped", NotificationImportance.Default)
            {
                Description = "When a job ends, with a picture of the result; and when the phone loses the computer during a job.",
            });
        if (m.GetNotificationChannel(Alarms) is null)
        {
            var alarms = new NotificationChannel(Alarms, "Machine alarms", NotificationImportance.High)
            {
                Description = "The machine reports a possible flame, being moved, or another problem.",
            };
            alarms.EnableVibration(true);
            alarms.SetVibrationPattern([0, 600, 200, 600, 200, 600]);
            alarms.EnableLights(true);
            alarms.LightColor = Color.Red;
            m.CreateNotificationChannel(alarms);
        }
    }

    /// <summary>Opens the app (where it was).</summary>
    public static PendingIntent OpenApp(Context c)
    {
        var intent = new Intent(c, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(c, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    public static Notification.Builder Builder(Context c, string channel) =>
        new Notification.Builder(c, channel)
            .SetSmallIcon(Resource.Drawable.ic_stat_laser)
            .SetColor(Color.ParseColor("#3B82F6"))
            .SetContentIntent(OpenApp(c))
            .SetShowWhen(true);

    public static void Post(Context c, int id, Notification n)
    {
        // Without permission (newer Android) nothing shows; nothing else to do.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && c.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != global::Android.Content.PM.Permission.Granted) return;
        Manager(c).Notify(id, n);
    }

    public static void Cancel(Context c, int id) => Manager(c).Cancel(id);
}
