using System.Net.WebSockets;
using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using PhotonCanvas.Remote;

namespace PhotonCanvas.Server;

/// <summary>A request the phone made that can't be done, with the HTTP status to answer and a plain reason.</summary>
public sealed class RemoteException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// The server's own connection to the machine (beside the desktop app's: the machine takes several).
/// Keeps it up, follows the machine's mode and the job on it, takes pictures, and loads, starts,
/// pauses and stops jobs for the phone.
/// </summary>
public sealed class MachineLink(RemoteConfig config, Broadcaster events, JobLog log) : BackgroundService
{
    public PictureStore Pictures { get; } = new();

    readonly Lock _gate = new();
    readonly SemaphoreSlim _camera = new(1, 1);
    readonly SemaphoreSlim _jobOps = new(1, 1);
    volatile DeviceClient? _client;
    string? _ip, _name, _firmware, _problem, _discoveredIp;
    string _mode = "";
    string? _taskId, _phoneStartFor;
    double? _focus;
    DateTime _focusReadAt;
    Tracked? _current;

    static readonly TimeSpan PicturePause = TimeSpan.FromSeconds(3);

    /// <summary>The job on the machine, followed from when it's loaded (or first seen) until it ends.</summary>
    sealed class Tracked
    {
        public required string TaskId;
        public required string Design;
        public required string Source;
        public SentJobInfo? Sent;
        public DateTime? LoadedAt;
        public DateTime? StartedAt;
        public DateTime? PausedAt;
        public double PausedFor;
        public double? MachineElapsed;
        public DateTime MachineElapsedAt;
        public double? Fraction;
        public bool StartedFromPhone;
        public byte[]? Before;
        public string? RecordId;
    }

    // ---- status ------------------------------------------------------------------------------

    public MachineStatus Status()
    {
        var now = DateTime.Now;
        lock (_gate)
        {
            JobStatus? job = null;
            if (_current is { } t && _mode is "P_WORK" or "P_WORKING" or "P_PAUSE")
            {
                double elapsed = Elapsed(t, now);
                double? remaining = null;
                if (t.Fraction is { } f && f > 0.02) remaining = elapsed / f * (1 - f);
                else if (t.Sent?.EstimateS is { } est && t.StartedAt is not null)
                    remaining = Math.Max(0, est * Math.Clamp(DesktopSettings.Get().JobTimeScale, 0.3, 5) - elapsed);
                string stage = _mode switch { "P_WORK" => "loaded", "P_PAUSE" => "paused", _ => "running" };
                job = new JobStatus(t.TaskId, t.Design, t.Source, stage, t.LoadedAt, t.StartedAt, elapsed, t.Fraction, remaining, t.Sent?.EstimateS, t.StartedFromPhone);
            }
            bool connected = _client is { IsConnected: true };
            return new MachineStatus(connected, _ip, _name, _firmware, connected ? _mode : "", connected ? DeviceClient.DescribeMode(_mode) : "Not connected",
                connected ? _focus : null, _problem, job, connected && _mode == "P_WORKING", now);
        }
    }

    static double Elapsed(Tracked t, DateTime now)
    {
        if (t.StartedAt is not { } start) return 0;
        if (t.MachineElapsed is { } m) return m + (t.PausedAt is null ? (now - t.MachineElapsedAt).TotalSeconds : 0);
        return Math.Max(0, (now - start).TotalSeconds - t.PausedFor - (t.PausedAt is { } p ? (now - p).TotalSeconds : 0));
    }

    void PublishStatus() => events.Publish("status", Status());

    void Alert(string kind, string text)
    {
        ServerLog.Write("alert", text);
        events.Publish("alert", new AlertEvent(kind, text, DateTimeOffset.Now));
    }

    DeviceClient Client() => _client is { IsConnected: true } c ? c
        : throw new RemoteException(503, _problem ?? "The server isn't connected to the machine right now.");

    // ---- the connection ----------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var wait = TimeSpan.FromSeconds(2);
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            var keys = MachineKeys.Load();
            string? ip = config.MachineIp ?? _discoveredIp ?? DesktopSettings.Get().LastMachineIp;
            if (!keys.PairingValid)
                SetProblem("The connection keys aren't set up. Open the desktop app once: Settings → Connection → Connection keys.");
            else if (ip is null && failures == 0)
            {
                SetProblem("No machine address yet: connect to the machine once from the desktop app.");
                failures++;
            }
            else
            {
                bool connected = false;
                try
                {
                    if (ip is null || failures >= 3) ip = await DiscoverAsync(keys, stop) ?? ip;
                    connected = ip is not null && await RunConnectionAsync(ip, keys, stop);
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    // Whatever went wrong, the server keeps trying (it must never stop by itself).
                    SetProblem($"Something went wrong talking to the machine: {e.Message}");
                }
                if (connected)
                {
                    wait = TimeSpan.FromSeconds(2);
                    failures = 0;
                    continue;
                }
                failures++;
            }
            try { await Task.Delay(wait, stop); } catch (OperationCanceledException) { break; }
            wait = TimeSpan.FromSeconds(Math.Min(30, wait.TotalSeconds * 2));
        }
    }

    void SetProblem(string? problem)
    {
        bool changed;
        lock (_gate)
        {
            changed = _problem != problem;
            _problem = problem;
        }
        if (changed)
        {
            if (problem is not null) ServerLog.Write("machine", problem);
            PublishStatus();
        }
    }

    /// <summary>Look for the machine on the network (its address may have changed).</summary>
    async Task<string?> DiscoverAsync(MachineKeys keys, CancellationToken stop)
    {
        if (!keys.RequestValid || !keys.ReplyValid) return null;
        try
        {
            var found = await Discovery.DiscoverAsync(keys, TimeSpan.FromSeconds(4), cancellationToken: stop);
            var m = found.FirstOrDefault(d => d.DeviceCode == "GS004-CLASS-4") ?? found.FirstOrDefault(d => d.IsV2);
            if (m is null) return null;
            ServerLog.Write("machine", $"Found {m.Name ?? "a machine"} at {m.Ip}.");
            return _discoveredIp = m.Ip;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ServerLog.Write("machine", $"Looking for the machine failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Connect and follow the machine until the link drops. True if it connected at all.</summary>
    async Task<bool> RunConnectionAsync(string ip, MachineKeys keys, CancellationToken stop)
    {
        var client = new DeviceClient(ip, new DeviceClientOptions
        {
            PairingKey = keys.Pairing,
            StatePath = Path.Combine(RemoteConfig.Dir, "state.json"),
            Log = m => ServerLog.Write("device", m),
        });
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += OnEvent;
        client.Disconnected += e =>
        {
            ServerLog.Write("machine", $"Connection lost: {e.Message}");
            lost.TrySetResult();
        };
        bool connected = false;
        try
        {
            await client.ConnectAsync(pair: true, onButtonPressRequired: () => SetProblem("Press the machine's button to let the server connect."), cancellationToken: stop);
            connected = true;
            var info = await client.GetMachineInfoAsync(stop);
            lock (_gate)
            {
                _ip = ip;
                _name = info.Raw["deviceName"]?.ToString();
                _firmware = info.Firmware;
                _focusReadAt = DateTime.MinValue;
            }
            _client = client;
            ServerLog.Write("machine", $"Connected to {_name ?? "the machine"} at {ip} (firmware {info.Firmware}).");
            SetProblem(null);
            while (!lost.Task.IsCompleted && !stop.IsCancellationRequested)
            {
                await PollAsync(client, stop);
                bool busy;
                lock (_gate) busy = _mode is "P_WORKING" or "P_PAUSE";
                await Task.WhenAny(lost.Task, Task.Delay(busy ? 2000 : 3000, stop));
            }
        }
        catch (Exception e) when (e is DeviceException or IOException or WebSocketException or TimeoutException or System.Net.Sockets.SocketException)
        {
            SetProblem(connected ? $"The connection to the machine broke: {e.Message}" : $"Can't reach the machine at {ip}: {e.Message}");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            _client = null;
            client.EventReceived -= OnEvent;
            await client.DisposeAsync();
            PublishStatus();
        }
        return connected;
    }

    async Task PollAsync(DeviceClient c, CancellationToken ct)
    {
        var rs = await c.GetRuntimeStateAsync(ct);
        OnMode(rs.Mode, rs.TaskId);
        if (rs.Mode is "P_WORKING" or "P_PAUSE")
        {
            var p = await c.GetJobProgressAsync(ct);
            lock (_gate)
            {
                if (_current is { } t)
                {
                    t.Fraction = p.Fraction;
                    if (p.ElapsedS is { } e && e > 0)
                    {
                        t.MachineElapsed = e;
                        t.MachineElapsedAt = DateTime.Now;
                    }
                }
            }
            PublishStatus();
        }
        else if (DateTime.Now - _focusReadAt > TimeSpan.FromSeconds(30) && rs.Mode is "P_IDLE" or "P_WORK" or "P_WORK_DONE")
        {
            double focus = Math.Round(await c.GetHeadZAsync(ct) - MachineProfile.F2Ultra.ZOffset, 2);
            bool changed;
            lock (_gate)
            {
                _focusReadAt = DateTime.Now;
                changed = _focus != focus;
                _focus = focus;
            }
            if (changed) PublishStatus();
        }
        // A job the desktop app sent: its name reaches app.json a moment after the machine reports it.
        lock (_gate)
            if (_current is { Source: "elsewhere" } stray && Identify(stray.TaskId) is { Source: not "elsewhere" } better)
            {
                stray.Design = better.Design;
                stray.Source = better.Source;
                stray.Sent = better.Sent;
            }
    }

    // ---- what the machine reports --------------------------------------------------------------

    void OnEvent(DeviceEvent ev)
    {
        if (ev.Type == "MODE_CHANGE")
        {
            var o = ev.Info as JsonObject;
            var mode = o?["mode"]?.ToString() ?? ev.InfoText;
            if (!string.IsNullOrEmpty(mode)) OnMode(mode, o?["taskId"]?.ToString());
            return;
        }
        if (ev.Module is "MACHINE_INFO" or "DEVICE_CONFIG") return; // repeated every few seconds
        ServerLog.Write("event", $"{ev.Module}/{ev.Type} ({ev.Level}): {ev.InfoText}");
        if (ev.Module == "FIRE_RECOGNITION")
            Alert("flame", "The machine reports a possible flame. Check it now.");
        else if (ev.Module == "GYRO_SENSOR")
            Alert("tilt", "The machine reports it was moved or tilted.");
        else if (ev.Level is "error" or "alarm" or "fatal" or "warning" or "warn")
            Alert("machine", $"The machine reports a problem: {ev.Module} {ev.Type}{(string.IsNullOrEmpty(ev.InfoText) ? "" : $" ({ev.InfoText})")}.");
    }

    /// <summary>Who sent the job with this id, and its name.</summary>
    (string Design, string Source, SentJobInfo? Sent) Identify(string? taskId)
    {
        if (log.Sent(taskId) is { } s) return (s.Design, "phone", s);
        if (!string.IsNullOrEmpty(taskId) && DesktopSettings.Get().SentJobs.FirstOrDefault(j => j.TaskId == taskId) is { } d) return (d.Name, "computer", null);
        return ("A job from elsewhere", "elsewhere", null);
    }

    Tracked Track(string? taskId, DateTime? loadedAt)
    {
        var (design, source, sent) = Identify(taskId);
        return new Tracked { TaskId = taskId ?? "", Design = design, Source = source, Sent = sent, LoadedAt = loadedAt };
    }

    /// <summary>The machine's mode (from an event or a status check): follow the job through it.</summary>
    void OnMode(string mode, string? taskId)
    {
        Tracked? started = null, ended = null, loaded = null;
        bool finished = false;
        var now = DateTime.Now;
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(taskId)) _taskId = taskId;
            bool otherJob = _current is not null && _taskId is not null && _current.TaskId != _taskId && mode is "P_WORK" or "P_WORKING" or "P_PAUSE";
            if (mode == _mode && !otherJob) return;
            ServerLog.Write("machine", $"{DeviceClient.DescribeMode(mode)} ({mode}{(_taskId is null ? "" : $", job {_taskId}")}).");
            _mode = mode;
            if (otherJob)
            {
                ended = _current!.StartedAt is not null ? _current : null;
                _current = null;
            }
            switch (mode)
            {
                case "P_WORK":
                    if (_current is null)
                        loaded = _current = Track(_taskId, now);
                    break;
                case "P_WORKING":
                    _current ??= Track(_taskId, null);
                    if (_current.StartedAt is null)
                    {
                        _current.StartedAt = now;
                        _current.StartedFromPhone = _phoneStartFor is not null && _phoneStartFor == _current.TaskId;
                        started = _current;
                    }
                    if (_current.PausedAt is { } p)
                    {
                        _current.PausedFor += (now - p).TotalSeconds;
                        _current.PausedAt = null;
                    }
                    break;
                case "P_PAUSE":
                    if (_current is { } tp) tp.PausedAt ??= now;
                    break;
                default:
                    // Finished, stopped, or a loaded job cancelled: the job is over.
                    if (_current is { StartedAt: not null } te)
                    {
                        ended = te;
                        finished = mode is "P_WORK_DONE" or "P_FINISH";
                    }
                    _current = null;
                    break;
            }
        }
        if (ended is not null) _ = EndedAsync(ended, finished, now);
        if (loaded is not null) _ = BeforePictureAsync(loaded);
        if (started is not null) Started(started);
        PublishStatus();
    }

    void Started(Tracked t)
    {
        t.RecordId = $"{t.StartedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        if (t.Before is { } b) log.SavePicture(t.RecordId, "before.jpg", b);
        log.Save(new RemoteJobRecord(t.RecordId, t.TaskId, t.Design, t.Source, t.LoadedAt, t.StartedAt!.Value, null, false, t.StartedFromPhone,
            t.Sent?.EstimateS, null, t.Before is not null, false));
        events.Publish("job", new JobEvent("started", t.RecordId, t.Design, t.Source, t.StartedFromPhone));
    }

    async Task EndedAsync(Tracked t, bool finished, DateTime at)
    {
        double took = Elapsed(t, at);
        if (t.RecordId is { } id && log.Find(id) is { } rec)
            log.Save(rec with { EndedAt = at, Finished = finished, TookS = took });
        if (t.Sent is { } s)
            JobHistory.Add(new JobRecord(at, s.Design, s.EstimateS, took, finished, s.Layers, s.WidthMm, s.HeightMm));
        events.Publish("job", new JobEvent(finished ? "finished" : "stopped", t.RecordId, t.Design, t.Source, t.StartedFromPhone, took,
            finished ? $"\"{t.Design}\" finished in {TimeText.Duration(took)}." : $"\"{t.Design}\" stopped after {TimeText.Duration(took)}."));
        // The camera works again once the job is over: a picture of the result.
        if (t.RecordId is not { } rid) return;
        try
        {
            await Task.Delay(PicturePause);
            var pic = await TakePictureAsync(0, CancellationToken.None);
            log.SavePicture(rid, "after.jpg", pic.Jpeg);
            if (log.Find(rid) is { } r) log.Save(r with { HasAfter = true });
            events.Publish("job", new JobEvent("picture", rid, t.Design));
        }
        catch (Exception e) when (e is RemoteException or DeviceException or IOException or WebSocketException or OperationCanceledException)
        {
            ServerLog.Write("jobs", $"No picture after \"{t.Design}\": {e.Message}");
        }
    }

    /// <summary>A picture of the bed once a job is loaded (whoever loaded it): the "before" of its history.</summary>
    async Task BeforePictureAsync(Tracked t)
    {
        try
        {
            await Task.Delay(1500);
            lock (_gate) if (t.Before is not null || _current != t || _mode != "P_WORK") return;
            var pic = await TakePictureAsync(0, CancellationToken.None);
            lock (_gate) t.Before ??= pic.Jpeg;
        }
        catch (Exception e) when (e is RemoteException or DeviceException or IOException or WebSocketException or OperationCanceledException)
        {
            ServerLog.Write("jobs", $"No picture of the loaded job: {e.Message}");
        }
    }

    // ---- what the phone asks for ---------------------------------------------------------------

    /// <summary>A camera picture now (0 = overview, 1 = close-up), kept for a while under its id.</summary>
    public async Task<Picture> TakePictureAsync(int stream, CancellationToken ct)
    {
        var c = Client();
        await _camera.WaitAsync(ct);
        try
        {
            string modeBefore;
            string? taskBefore;
            lock (_gate) (modeBefore, taskBefore) = (_mode, _taskId);
            byte[] jpg;
            try { jpg = await c.SnapshotAsync(stream, ct); }
            catch (DeviceException)
            {
                // Now and then the camera answers "get camera snap failed" once.
                await Task.Delay(400, ct);
                jpg = await c.SnapshotAsync(stream, ct);
            }
            string mode;
            string? task;
            lock (_gate) (mode, task) = (_mode, _taskId);
            // The mode changed while the picture was taken: it counts for neither.
            if (mode != modeBefore || task != taskBefore) mode = "changing";
            return Pictures.Add(stream, jpg, DateTime.Now, mode, mode is "P_WORK" or "P_WORKING" or "P_PAUSE" ? task : null);
        }
        finally { _camera.Release(); }
    }

    /// <summary>The machine's focus distance now (mm), or null when it can't say.</summary>
    public async Task<double?> FocusDistanceAsync(CancellationToken ct)
    {
        if (_client is not { IsConnected: true } c) return null;
        try
        {
            double focus = Math.Round(await c.GetHeadZAsync(ct) - MachineProfile.F2Ultra.ZOffset, 2);
            lock (_gate) (_focus, _focusReadAt) = (focus, DateTime.Now);
            return focus;
        }
        catch (DeviceException) { return null; }
    }

    /// <summary>What the machine has loaded, for the check before loading another (which replaces it).</summary>
    public string? LoadedJobNote()
    {
        lock (_gate)
            return _mode == "P_WORK" && _current is { } t
                ? $"A job is already loaded on the machine and not started: \"{t.Design}\" ({SourceText(t.Source)}). It will be replaced by this one."
                : null;
    }

    public static string SourceText(string source) => source switch
    {
        "phone" => "loaded from the phone",
        "computer" => "sent from the desktop app",
        _ => "from xTool Studio, the machine's screen or another computer",
    };

    /// <summary>Upload a built job; the machine then waits (for its button or the phone's Start). Returns its id and a picture of the bed.</summary>
    public async Task<(string TaskId, Picture? Picture)> LoadAsync(ProjectBuild b, string who, CancellationToken ct)
    {
        var c = Client();
        await _jobOps.WaitAsync(ct);
        try
        {
            string taskId = Guid.NewGuid().ToString();
            // Remembered first: the machine announces the loaded job (with this id) before the upload call returns.
            log.AddSent(new SentJobInfo(taskId, b.Design, b.Path, b.Job.EstimateS, b.Job.LayerTimes.Count, b.Job.Bounds.Width, b.Job.Bounds.Height, DateTime.Now));
            ServerLog.Write("job", $"Loading \"{b.Design}\" for {who}.");
            try { await c.UploadJobAsync(b.Job.Xf, null, ct, replaceLoadedJob: true, taskId: taskId); }
            catch (DeviceException e) { throw new RemoteException(409, e.Message); }
            var rs = await c.GetRuntimeStateAsync(ct);
            OnMode(rs.Mode, rs.TaskId);
            Picture? pic = null;
            try
            {
                pic = await TakePictureAsync(0, ct);
                lock (_gate) if (_current is { } t && t.TaskId == taskId) t.Before ??= pic.Jpeg;
            }
            catch (DeviceException e) { ServerLog.Write("job", $"Loaded, but no picture: {e.Message}"); }
            return (taskId, pic);
        }
        finally { _jobOps.Release(); }
    }

    /// <summary>
    /// Start the loaded job from the phone. Only on a picture the server took in the last two minutes
    /// with this job loaded (see <see cref="StartGuard"/>). The laser fires.
    /// </summary>
    public async Task<string> StartAsync(string? pictureId, string who, CancellationToken ct)
    {
        var c = Client();
        await _jobOps.WaitAsync(ct);
        try
        {
            var rs = await c.GetRuntimeStateAsync(ct);
            OnMode(rs.Mode, rs.TaskId);
            string mode;
            string? task;
            lock (_gate) (mode, task) = (_mode, _taskId);
            if (StartGuard.Problem(Pictures.Get(pictureId), DateTime.Now, mode, task) is { } why)
                throw new RemoteException(409, why);
            lock (_gate) _phoneStartFor = task;
            ServerLog.Write("job", $"Start from {who} (job {task}, on picture {pictureId}).");
            string after;
            try { after = await c.StartLoadedJobAsync(ct); }
            catch (DeviceException e) { throw new RemoteException(409, e.Message); }
            rs = await c.GetRuntimeStateAsync(ct);
            OnMode(rs.Mode, rs.TaskId);
            if (after != "P_WORKING")
            {
                ServerLog.Write("job", $"The machine didn't start: it reports {after}.");
                throw new RemoteException(502, $"The machine didn't start: it reports \"{DeviceClient.DescribeMode(after)}\". Press its button to start, or try again.");
            }
            return after;
        }
        finally { _jobOps.Release(); }
    }

    /// <summary>Pause, resume or stop the job on the machine.</summary>
    public async Task<string> ControlAsync(string action, string who, CancellationToken ct)
    {
        var c = Client();
        var rs = await c.GetRuntimeStateAsync(ct);
        try
        {
            switch (action)
            {
                case "pause":
                    if (rs.Mode != "P_WORKING") throw new RemoteException(409, "Nothing is running.");
                    await c.PauseJobAsync(ct);
                    break;
                case "resume":
                    if (rs.Mode != "P_PAUSE") throw new RemoteException(409, "Nothing is paused.");
                    await c.ResumeJobAsync(ct);
                    break;
                case "stop":
                    if (rs.Mode is not ("P_WORKING" or "P_PAUSE" or "P_WORK")) throw new RemoteException(409, "There's no job to stop.");
                    await c.StopJobAsync(ct);
                    break;
                default:
                    throw new RemoteException(404, $"Unknown action \"{action}\".");
            }
        }
        catch (DeviceException e) { throw new RemoteException(502, e.Message); }
        ServerLog.Write("job", $"{action} from {who} (the machine was {rs.Mode}).");
        await Task.Delay(800, ct);
        rs = await c.GetRuntimeStateAsync(ct);
        OnMode(rs.Mode, rs.TaskId);
        return rs.Mode;
    }
}

/// <summary>The desktop app's settings (app.json), read again only when the file changes. Read-only.</summary>
public static class DesktopSettings
{
    static readonly Lock Gate = new();
    static AppSettings? _cached;
    static DateTime _stamp;

    public static string FilePath => Path.Combine(AppIdentity.DataDir, "app.json");

    public static AppSettings Get()
    {
        lock (Gate)
        {
            DateTime t;
            try { t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch (IOException) { t = DateTime.MinValue; }
            if (_cached is null || t != _stamp)
            {
                _cached = AppSettings.Load();
                _stamp = t;
            }
            return _cached;
        }
    }
}
