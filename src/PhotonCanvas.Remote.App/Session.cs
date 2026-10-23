using Avalonia.Threading;

namespace PhotonCanvas.Remote.App;

/// <summary>A picture the phone has shown, and whether a job could be started on it.</summary>
/// <param name="TaskId">The job that was loaded when it was taken (null: none).</param>
public sealed record ShownPicture(byte[] Image, string? Id, DateTimeOffset TakenAt, bool Blanked, string? TaskId)
{
    /// <summary>Good to start this job on: taken with it loaded, less than two minutes ago (the server checks this too).</summary>
    public bool StartsJob(JobStatus? job, DateTimeOffset now) =>
        Id is not null && job is { Stage: "loaded" } j && j.TaskId == TaskId && now - TakenAt < TimeSpan.FromSeconds(115);
}

/// <summary>
/// One connection to a server (or the demo): the machine's status as it changes, the job and alarm
/// events, and the last picture shown. Everything it raises arrives on the UI thread.
/// </summary>
public sealed class Session : IDisposable
{
    readonly CancellationTokenSource _stop = new();

    public Session(IRemote remote)
    {
        Remote = remote;
        _ = ListenAsync(_stop.Token);
    }

    public IRemote Remote { get; }
    public MachineStatus? Status { get; private set; }
    /// <summary>Listening to the server right now (false while it's reconnecting).</summary>
    public bool Live { get; private set; }
    /// <summary>Why it isn't live, in plain words.</summary>
    public string? Problem { get; private set; }
    public ShownPicture? LastPicture { get; set; }

    public event Action? Changed;
    public event Action<JobEvent>? JobEvent;
    public event Action<AlertEvent>? Alarm;
    /// <summary>The access code was refused: back to the connect screen.</summary>
    public event Action<string>? CodeRefused;

    async Task ListenAsync(CancellationToken ct)
    {
        var wait = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var e in Remote.EventsAsync(ct).ConfigureAwait(false))
                {
                    wait = TimeSpan.FromSeconds(1);
                    Dispatcher.UIThread.Post(() => Handle(e));
                }
                Post(false, "The connection to the computer ended. Trying again…");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (RemoteError e) when (e.NeedsCode)
            {
                Dispatcher.UIThread.Post(() => CodeRefused?.Invoke(e.Message));
                return;
            }
            catch (RemoteError e)
            {
                Post(false, e.Message);
            }
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            wait = TimeSpan.FromSeconds(Math.Min(15, wait.TotalSeconds * 2));
        }
    }

    void Post(bool live, string? problem) => Dispatcher.UIThread.Post(() =>
    {
        Live = live;
        Problem = problem;
        Changed?.Invoke();
    });

    void Handle(RemoteEvent e)
    {
        switch (e)
        {
            case StatusChanged s:
                Status = s.Status;
                Live = true;
                Problem = null;
                Changed?.Invoke();
                break;
            case JobChanged j:
                JobEvent?.Invoke(j.Job);
                break;
            case AlarmRaised a:
                Alarm?.Invoke(a.Alert);
                break;
        }
    }

    /// <summary>Take a picture and remember it (with the job loaded now) for starting.</summary>
    public async Task<ShownPicture> TakePictureAsync(string camera, int width)
    {
        string? loaded = Status?.Job is { Stage: "loaded" } j ? j.TaskId : null;
        var p = await Remote.TakePictureAsync(camera, width);
        var shown = new ShownPicture(p.Jpeg, p.Id, p.TakenAt, p.Blanked, loaded);
        LastPicture = shown;
        return shown;
    }

    public void Dispose()
    {
        _stop.Cancel();
        Remote.Dispose();
    }
}
