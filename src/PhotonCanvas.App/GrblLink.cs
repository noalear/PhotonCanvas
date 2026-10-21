using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace PhotonCanvas.App;

/// <summary>A GRBL machine's state, from its status reports: Idle, Run, Hold, Alarm… and where the head is (mm).</summary>
public sealed record GrblStatus(string State, double X, double Y);

/// <summary>An error or alarm from GRBL, with what it means.</summary>
public sealed class GrblException(string message) : Exception(message);

/// <summary>
/// A GRBL controller over USB (a serial port). Jobs are streamed with GRBL's own flow control:
/// lines are sent while they fit in its 128-byte receive buffer and counted off as each "ok"
/// comes back, so the motion planner never runs dry (a stutter mid-row leaves burn marks).
/// Status is asked for with "?" four times a second. Feed hold ("!"), resume ("~") and reset
/// (Ctrl-X) go straight through, ahead of the job.
/// </summary>
public sealed class GrblLink : IAsyncDisposable
{
    const int RxBuffer = 127;
    SerialPort? _port;
    CancellationTokenSource? _cts;
    readonly object _lock = new();
    readonly Queue<int> _inFlight = new();
    int _bytes;
    TaskCompletionSource<string>? _reply;
    string? _error;

    /// <summary>Every line GRBL sends (status reports included), for a log. Not on the UI thread.</summary>
    public event Action<string>? Received;
    /// <summary>A status report arrived. Not on the UI thread.</summary>
    public event Action<GrblStatus>? StatusChanged;

    public bool IsOpen => _port?.IsOpen == true;
    public GrblStatus? Status { get; private set; }
    public string Version { get; private set; } = "";

    /// <summary>The serial ports on this computer.</summary>
    public static string[] Ports() { try { return SerialPort.GetPortNames().Order().ToArray(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return []; } }

    /// <summary>Open the port, wait for GRBL's greeting, and start asking for status.</summary>
    public async Task OpenAsync(string port, int baud)
    {
        if (IsOpen) return;
        var p = new SerialPort(port, baud) { NewLine = "\n", Encoding = Encoding.ASCII, ReadTimeout = 500, WriteTimeout = 2000, DtrEnable = true };
        p.Open();
        _port = p;
        _cts = new CancellationTokenSource();
        var greeting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Hello(string line) { if (line.StartsWith("Grbl", StringComparison.OrdinalIgnoreCase)) greeting.TrySetResult(line); }
        Received += Hello;
        _ = Task.Run(() => ReadLoop(_cts.Token));
        // Opening the port resets most boards (DTR); a soft reset makes the rest say hello too.
        await Task.Delay(300);
        p.Write("\x18");
        var done = await Task.WhenAny(greeting.Task, Task.Delay(4000));
        Received -= Hello;
        if (done != greeting.Task)
        {
            await DisposeAsync();
            throw new GrblException($"No answer from GRBL on {port} at {baud} baud. Check the port and the speed (usually 115200), and that the controller runs GRBL.");
        }
        Version = greeting.Task.Result;
        _ = Task.Run(() => PollLoop(_cts.Token));
    }

    void ReadLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _port is { IsOpen: true } p)
        {
            string line;
            try { line = p.ReadLine().Trim(); }
            catch (TimeoutException) { continue; }
            catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException) { break; }
            if (line.Length == 0) continue;
            Received?.Invoke(line);
            if (line.StartsWith('<')) { Parse(line); continue; }
            if (line == "ok" || line.StartsWith("error:", StringComparison.Ordinal))
            {
                lock (_lock)
                {
                    if (_inFlight.Count > 0) _bytes -= _inFlight.Dequeue();
                    if (line != "ok") _error ??= line;
                    _reply?.TrySetResult(line);
                }
                continue;
            }
            if (line.StartsWith("ALARM:", StringComparison.Ordinal)) lock (_lock) { _error ??= line; _reply?.TrySetResult(line); }
        }
    }

    async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { _port?.Write("?"); }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException) { }
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
        }
    }

    void Parse(string report)
    {
        // <Idle|MPos:0.000,0.000,0.000|FS:0,0|WCO:…>
        var parts = report.Trim('<', '>').Split('|');
        string state = parts[0].Split(':')[0];
        double x = Status?.X ?? 0, y = Status?.Y ?? 0;
        foreach (var part in parts.Skip(1))
            if (part.StartsWith("MPos:", StringComparison.Ordinal) || part.StartsWith("WPos:", StringComparison.Ordinal))
            {
                var n = part[5..].Split(',');
                if (n.Length >= 2 && double.TryParse(n[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && double.TryParse(n[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py)) (x, y) = (px, py);
            }
        Status = new GrblStatus(state, x, y);
        StatusChanged?.Invoke(Status);
    }

    /// <summary>One command (like $H or $X), waiting for its answer. Throws on an error or alarm.</summary>
    public async Task CommandAsync(string line, TimeSpan timeout)
    {
        if (_port is not { IsOpen: true } p) throw new GrblException("Not connected.");
        TaskCompletionSource<string> reply;
        lock (_lock)
        {
            reply = _reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight.Enqueue(line.Length + 1);
            _bytes += line.Length + 1;
            _error = null;
        }
        p.Write(line + "\n");
        var done = await Task.WhenAny(reply.Task, Task.Delay(timeout));
        if (done != reply.Task) throw new GrblException($"No answer to {line}.");
        if (reply.Task.Result != "ok") throw new GrblException(Explain(reply.Task.Result));
    }

    /// <summary>
    /// Stream a job's lines, keeping GRBL's receive buffer full. Reports lines done; stops at the
    /// first error or alarm (throws), or when cancelled (the caller resets the machine).
    /// </summary>
    public async Task StreamAsync(IReadOnlyList<string> lines, IProgress<int>? progress, CancellationToken ct)
    {
        if (_port is not { IsOpen: true } p) throw new GrblException("Not connected.");
        var send = lines.Select(l => l.Split(';')[0].Trim()).Where(l => l.Length > 0).ToList();
        lock (_lock) { _inFlight.Clear(); _bytes = 0; _error = null; }
        int sent = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? err;
            int inFlight, bytes;
            lock (_lock) { err = _error; inFlight = _inFlight.Count; bytes = _bytes; }
            if (err is not null) throw new GrblException(Explain(err));
            if (sent == send.Count && inFlight == 0) break;
            if (sent < send.Count && bytes + send[sent].Length + 1 <= RxBuffer)
            {
                var line = send[sent];
                lock (_lock) { _inFlight.Enqueue(line.Length + 1); _bytes += line.Length + 1; }
                p.Write(line + "\n");
                sent++;
                progress?.Report(sent);
                continue;
            }
            await Task.Delay(2, ct);
        }
        // Done when the machine has stopped moving.
        while (Status?.State is "Run" or "Hold" or "Jog") { ct.ThrowIfCancellationRequested(); await Task.Delay(100, ct); }
    }

    /// <summary>Feed hold: stops moving (and, in laser mode, firing) at once; Resume carries on.</summary>
    public void Hold() => Realtime("!");
    public void Resume() => Realtime("~");
    /// <summary>Stop everything: GRBL's reset (the laser goes off; what's in its buffer is dropped).</summary>
    public void Reset() { Realtime("\x18"); lock (_lock) { _inFlight.Clear(); _bytes = 0; } }

    void Realtime(string c) { try { _port?.Write(c); } catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException) { } }

    /// <summary>GRBL's numbered errors and alarms, in words.</summary>
    public static string Explain(string code) => code switch
    {
        "error:1" => "error:1 — a G-code word without a number.",
        "error:2" => "error:2 — a number is missing or wrong.",
        "error:9" => "error:9 — GRBL is locked (an alarm, or it hasn't been homed): Unlock or Home first.",
        "error:15" => "error:15 — a move goes past the machine's travel limits: is the bed size right, and was it homed?",
        "error:20" => "error:20 — a G-code this GRBL doesn't know.",
        "error:22" => "error:22 — no speed (F) for a move.",
        "ALARM:1" => "ALARM:1 — a limit switch was hit: the head reached the end of travel. Unlock, home, and check the bed size and origin.",
        "ALARM:2" => "ALARM:2 — a move goes past the soft limits: the design is outside the travel. Check the bed size and origin.",
        "ALARM:3" => "ALARM:3 — reset while moving: the position may be lost. Home again.",
        "ALARM:8" or "ALARM:9" => $"{code} — homing failed: a switch wasn't found. Check the switches.",
        _ => code,
    };

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        await Task.Delay(50);
        try { _port?.Close(); } catch (IOException) { }
        _port?.Dispose();
        _port = null;
    }
}
