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
/// events, and the last picture shown. When the computer stops answering at one address it tries its
/// others (home network, Tailscale), so the app keeps working at home and away. Everything it raises
/// arrives on the UI thread.
/// </summary>
public sealed class Session : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly Action<ConnectionInfo>? _remember;

    public Session(IRemote remote, ConnectionInfo? connection = null, Action<ConnectionInfo>? remember = null)
    {
        Remote = remote;
        Connection = connection;
        _remember = remember;
        _ = ListenAsync(_stop.Token);
    }

    /// <summary>What to ask things of (it changes when the computer is reached at another address).</summary>
    public IRemote Remote { get; private set; }
    /// <summary>Where the computer is and its addresses (null for the demo).</summary>
    public ConnectionInfo? Connection { get; private set; }
    public MachineStatus? Status { get; private set; }
    /// <summary>Listening to the server right now (false while it's reconnecting).</summary>
    public bool Live { get; private set; }
    /// <summary>Why it isn't live, in plain words.</summary>
    public string? Problem { get; private set; }
    public ShownPicture? LastPicture { get; set; }

    public event Action? Changed;
    public event Action<JobEvent>? JobEvent;
    public event Action<AlertEvent>? Alarm;
    /// <summary>The access code (or the computer's certificate) was refused: back to the connect screen.</summary>
    public event Action<string>? CodeRefused;

    async Task ListenAsync(CancellationToken ct)
    {
        var wait = TimeSpan.FromSeconds(1);
        bool learned = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Saved before the computer had HTTPS (or typed in): encrypted from now on, if it can.
                if (Connection is { Secure: false } plain && await HttpRemote.SecureAsync(plain, ct: ct).ConfigureAwait(false) is { Secure: true } secure)
                    Use(secure, newRemote: true);
                if (!learned && Connection is not null) learned = await LearnAddressesAsync(ct).ConfigureAwait(false);
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
                // At home the Tailscale address may be the one that answers now, or away the home one doesn't.
                if (e.Unreachable && await SwitchAddressAsync(ct).ConfigureAwait(false))
                {
                    wait = TimeSpan.FromSeconds(1);
                    continue;
                }
            }
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            wait = TimeSpan.FromSeconds(Math.Min(15, wait.TotalSeconds * 2));
        }
    }

    /// <summary>The computer's addresses, from the computer (it may have gained one, e.g. Tailscale). True once asked.</summary>
    async Task<bool> LearnAddressesAsync(CancellationToken ct)
    {
        try
        {
            var info = await Remote.InfoAsync(ct).ConfigureAwait(false);
            if (Connection is { } c && info.Addresses is { Length: > 0 } list)
            {
                var updated = c.Using(c.Address, list.Append(c.Address));
                if (!updated.SameAs(c)) Use(updated, newRemote: false);
            }
            return true;
        }
        catch (RemoteError) { return false; }
    }

    async Task<bool> SwitchAddressAsync(CancellationToken ct)
    {
        if (Connection is not { Others.Length: > 0 } c) return false;
        var reached = await HttpRemote.ReachAsync(c, ct).ConfigureAwait(false);
        if (reached is null || reached.Address == c.Address) return false;
        Use(reached, newRemote: true);
        return true;
    }

    void Use(ConnectionInfo c, bool newRemote)
    {
        Connection = c;
        if (newRemote)
        {
            var old = Remote;
            Remote = new HttpRemote(c);
            // Calls still under way finish on the old one first.
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        }
        Dispatcher.UIThread.Post(() =>
        {
            _remember?.Invoke(c);
            Changed?.Invoke();
        });
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
