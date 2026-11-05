using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;

namespace PhotonCanvas.Remote.Android;

/// <summary>
/// Keeps an eye on the machine with the app closed: an ongoing notification with the job's progress
/// (and a Pause button), a notification when it ends (with a picture of the result), the machine's
/// alarms, and a warning if the phone loses the computer during a job. It has its own connection,
/// from the saved computer, and stops itself a little after its job ends (unless set to watch always).
/// A "connected device" foreground service: Android keeps it running, and shows that it does.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class WatchService : Service
{
    public const string ActionWatch = "org.photoncanvas.remote.WATCH";
    public const string ActionStop = "org.photoncanvas.remote.STOP_WATCHING";
    public const string ActionPause = "org.photoncanvas.remote.PAUSE";
    public const string ActionResume = "org.photoncanvas.remote.RESUME";

    // After the job ends: long enough for the picture of the result to arrive.
    static readonly TimeSpan LingerAfterJob = TimeSpan.FromSeconds(90);
    static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(30);

    CancellationTokenSource? _run;
    HttpRemote? _remote;
    MachineStatus? _status;
    DateTimeOffset? _idleSince;
    DateTimeOffset _lastContact = DateTimeOffset.Now;
    bool _lostShown;
    string? _lastJobName;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Notices.EnsureChannels(this);
        // Android wants the notification at once, whatever the request.
        var n = Ongoing();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(Notices.OngoingId, n, ForegroundService.TypeConnectedDevice);
        else StartForeground(Notices.OngoingId, n);
        switch (intent?.Action)
        {
            case ActionStop:
                Finish();
                return StartCommandResult.NotSticky;
            case ActionPause:
                _ = Control("pause");
                break;
            case ActionResume:
                _ = Control("resume");
                break;
        }
        if (_run is null)
        {
            _run = new CancellationTokenSource();
            _ = WatchAsync(_run.Token);
        }
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        _run?.Cancel();
        _remote?.Dispose();
        base.OnDestroy();
    }

    void Finish()
    {
        _run?.Cancel();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.N) StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    async Task WatchAsync(CancellationToken ct)
    {
        var folder = FilesDir!.AbsolutePath;
        while (!ct.IsCancellationRequested)
        {
            var connection = new ConnectionStore(folder).Load();
            var settings = PhoneSettings.Load(folder);
            if (connection is null || settings.Watch == WatchMode.Off)
            {
                Finish();
                return;
            }
            using var remote = _remote = new HttpRemote(connection);
            try
            {
                await foreach (var e in remote.EventsAsync(ct))
                {
                    _lastContact = DateTimeOffset.Now;
                    OnRemoteEvent(e, remote);
                    if (settings.Watch == WatchMode.DuringJobs && _idleSince is { } idle && DateTimeOffset.Now - idle > LingerAfterJob)
                    {
                        Finish();
                        return;
                    }
                }
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (RemoteError e) when (e.NeedsCode)
            {
                Notices.Post(this, Notices.LostId, Notices.Builder(this, Notices.Jobs).SetContentTitle("PhotonCanvas Remote can't connect").SetContentText(e.Message).SetStyle(new Notification.BigTextStyle().BigText(e.Message)).SetAutoCancel(true).Build());
                Finish();
                return;
            }
            catch (RemoteError) { }
            // Lost the computer: say so if a job was on the machine, and keep trying.
            if (_status?.Job is { } job && DateTimeOffset.Now - _lastContact > LostAfter && !_lostShown)
            {
                _lostShown = true;
                Notices.Post(this, Notices.LostId, Notices.Builder(this, Notices.Jobs)
                    .SetContentTitle("Lost contact with the computer")
                    .SetContentText($"\"{job.Design}\" was {(job.Stage == "running" ? "running" : job.Stage)}. The phone can't reach PhotonCanvas: the machine carries on by itself.")
                    .SetStyle(new Notification.BigTextStyle().BigText($"\"{job.Design}\" was {(job.Stage == "running" ? "running" : job.Stage)}. The phone can't reach PhotonCanvas on the computer; the machine carries on by itself. Still trying."))
                    .SetAutoCancel(true).Build());
            }
            Update(connecting: true);
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (System.OperationCanceledException) { return; }
        }
    }

    void OnRemoteEvent(RemoteEvent e, HttpRemote remote)
    {
        switch (e)
        {
            case StatusChanged s:
                _status = s.Status;
                if (_lostShown)
                {
                    _lostShown = false;
                    Notices.Cancel(this, Notices.LostId);
                }
                if (s.Status.Job is { } current)
                {
                    _idleSince = null;
                    _lastJobName = current.Design;
                }
                else _idleSince ??= DateTimeOffset.Now;
                Update(connecting: false);
                break;
            case JobChanged { Job.Kind: "finished" or "stopped" } j:
                Notices.Post(this, Notices.JobId, Notices.Builder(this, Notices.Jobs)
                    .SetContentTitle(j.Job.Kind == "finished" ? $"{j.Job.Design} finished" : $"{j.Job.Design} stopped")
                    .SetContentText(j.Job.Text ?? "")
                    .SetAutoCancel(true).Build());
                break;
            case JobChanged { Job.Kind: "picture" } p:
                _ = ResultPictureAsync(remote, p.Job);
                break;
            case AlarmRaised a:
                Notices.Post(this, Notices.AlarmId, Notices.Builder(this, Notices.Alarms)
                    .SetContentTitle(a.Alert.Kind == "flame" ? "Possible flame at the laser!" : "The laser reports a problem")
                    .SetContentText(a.Alert.Text)
                    .SetStyle(new Notification.BigTextStyle().BigText(a.Alert.Text))
                    .SetCategory(Notification.CategoryAlarm)
                    .SetColor(Color.ParseColor("#EF4444"))
                    .SetAutoCancel(true).Build());
                break;
        }
    }

    /// <summary>The finished job's notification again, now with the picture of the result.</summary>
    async Task ResultPictureAsync(HttpRemote remote, JobEvent e)
    {
        try
        {
            var item = (await remote.HistoryAsync(5)).FirstOrDefault(h => h.Id == e.Id);
            if (item?.After is not { } url) return;
            var bytes = await remote.ImageAsync(url, 1024);
            using var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bitmap is null) return;
            Notices.Post(this, Notices.JobId, Notices.Builder(this, Notices.Jobs)
                .SetContentTitle(item.Finished ? $"{item.Design} finished" : $"{item.Design} stopped")
                .SetContentText(item.TookS is { } took ? $"Took {TimeSpan.FromSeconds(Math.Round(took)):g}. Here's the result." : "Here's the result.")
                .SetLargeIcon(bitmap)
                .SetStyle(new Notification.BigPictureStyle().BigPicture(bitmap))
                .SetOnlyAlertOnce(true)
                .SetAutoCancel(true).Build());
        }
        catch (RemoteError) { }
    }

    async Task Control(string action)
    {
        try { if (_remote is { } r) await r.ControlAsync(action); }
        catch (RemoteError e)
        {
            Notices.Post(this, Notices.LostId, Notices.Builder(this, Notices.Jobs).SetContentTitle($"Couldn't {action}").SetContentText(e.Message).SetAutoCancel(true).Build());
        }
    }

    void Update(bool connecting) => Notices.Manager(this).Notify(Notices.OngoingId, Ongoing(connecting));

    /// <summary>The ongoing notification: the job and its progress, or what the machine is doing.</summary>
    Notification Ongoing(bool connecting = true)
    {
        var b = Notices.Builder(this, Notices.Watching).SetOngoing(true).SetOnlyAlertOnce(true).SetShowWhen(false);
        var st = _status;
        if (connecting && st is null)
            return b.SetContentTitle("Watching the laser").SetContentText("Connecting to the computer…").Build();
        if (connecting)
            b.SetSubText("reconnecting");
        if (st is not { Connected: true })
            return b.SetContentTitle("Watching the laser").SetContentText(st?.Problem ?? "The computer isn't connected to the machine.").Build();
        if (st.Job is not { } job)
            return b.SetContentTitle(st.Name ?? "Watching the laser").SetContentText(_lastJobName is null ? st.State : $"{st.State} · last job: {_lastJobName}").Build();

        var now = DateTimeOffset.Now;
        string text = job.Stage switch
        {
            "loaded" => job.EstimateS is { } est ? $"Loaded, about {Clock(est)} once started" : "Loaded, waiting to start",
            "paused" => $"Paused at {Clock(job.ElapsedS)}",
            _ => $"{Clock(job.ElapsedS)}" + (job.RemainingS is { } left ? $" · about {Clock(left)} left" : "") + (JobTimes.FinishText(job, now) is { } f ? $" · {f}" : ""),
        };
        b.SetContentTitle(job.Design).SetContentText(text);
        double? fraction = job.Fraction ?? (job.RemainingS is { } r && job.ElapsedS + r > 0 ? job.ElapsedS / (job.ElapsedS + r) : null);
        if (job.Stage != "loaded") b.SetProgress(1000, (int)Math.Round((fraction ?? 0) * 1000), fraction is null);
        // Pause is the safe button here: it can be undone. Stopping is in the app, with a question first.
        if (job.Stage == "running") b.AddAction(Action("Pause", ActionPause));
        if (job.Stage == "paused") b.AddAction(Action("Continue", ActionResume));
        return b.Build();
    }

    Notification.Action Action(string title, string action)
    {
        var intent = new Intent(this, typeof(WatchService)).SetAction(action);
        var pending = PendingIntent.GetForegroundService(this, action.GetHashCode(), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        return new Notification.Action.Builder(global::Android.Graphics.Drawables.Icon.CreateWithResource(this, Resource.Drawable.ic_stat_laser), new Java.Lang.String(title), pending).Build();
    }

    static string Clock(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
