// Checks what a remote-control server would need from the machine:
//   1. More than one connection at once (this tool beside the desktop app, then a third).
//   2. Camera pictures while a job runs, at time-lapse pace and as fast as it goes.
// It only reads status and takes pictures: nothing here moves the head or fires the laser.
//
//   dotnet run --project tools/RemoteProbe -- <machine ip> <output folder> [time-lapse pictures] [seconds between]
//   dotnet run --project tools/RemoteProbe -- <machine ip> <output folder> exposure
//
// "exposure" compares picture brightness before and after POST /v1/camera {exp: 0}, the call the
// stock app makes before every normal picture (it uses exp 10 while measuring focus, which shows
// little but the laser dot). That changes only the camera.
//
// Keys come from the user's saved keys.json (read only). The pairing token goes to the output
// folder, so the desktop app's own state file is never touched.
using System.Diagnostics;
using System.Text.Json.Nodes;
using PhotonCanvas.Device;
using SkiaSharp;

if (args.Length < 2)
{
    Console.WriteLine("usage: RemoteProbe <machine ip> <output folder> [time-lapse pictures=12] [seconds between=5]");
    Console.WriteLine("       RemoteProbe <machine ip> <output folder> exposure");
    return 2;
}
string ip = args[0];
string outDir = Path.GetFullPath(args[1]);
string? command = args.Length > 2 && args[2] is "exposure" or "get" or "exptime" ? args[2] : null;
bool exposureTest = command == "exposure";
int frames = args.Length > 2 && command is null ? int.Parse(args[2]) : 12;
double every = args.Length > 3 && command is null ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 5;
Directory.CreateDirectory(outDir);
var logFile = new StreamWriter(Path.Combine(outDir, "probe.log"), append: false) { AutoFlush = true };
var clock = Stopwatch.StartNew();
var logLock = new Lock();

void Log(string message)
{
    string line = $"{DateTime.Now:HH:mm:ss.fff} [{clock.Elapsed.TotalSeconds,7:0.0}s] {message}";
    lock (logLock)
    {
        Console.WriteLine(line);
        logFile.WriteLine(line);
    }
}

var keys = MachineKeys.Load();
if (!keys.PairingValid)
{
    Log($"No valid pairing key in {MachineKeys.DefaultPath}.");
    return 1;
}

var events = new Dictionary<string, int>();
DeviceClient NewClient(string name)
{
    var c = new DeviceClient(ip, new DeviceClientOptions { PairingKey = keys.Pairing, StatePath = Path.Combine(outDir, "state.json") });
    var seen = new HashSet<string>();
    lock (events) events[name] = 0;
    c.EventReceived += ev =>
    {
        lock (events) events[name]++;
        // The machine repeats its status pushes every few seconds: show each kind once, work events always.
        string kind = $"{ev.Module}/{ev.Type}";
        bool first;
        lock (seen) first = seen.Add(kind);
        if (first || ev.Type?.StartsWith("WORK_") == true)
        {
            string info = ev.InfoText ?? "";
            Log($"{name} event {kind}: {(info.Length > 160 ? info[..160] + "â€¦" : info)}");
        }
    };
    c.Disconnected += ex => Log($"{name} DISCONNECTED: {ex.Message}");
    return c;
}

async Task<string> Status(string name, DeviceClient c)
{
    try
    {
        string mode = await c.GetRuntimeModeAsync();
        var p = await c.GetJobProgressAsync();
        string s = $"mode {mode}, progress {p.Raw?.ToJsonString() ?? "-"}";
        Log($"{name}: {s}");
        return mode;
    }
    catch (Exception e)
    {
        Log($"{name}: status FAILED: {e.Message}");
        return "?";
    }
}

async Task<(bool Ok, double Seconds, int Bytes)> Picture(DeviceClient c, int stream, string file)
{
    var t = Stopwatch.StartNew();
    try
    {
        byte[] jpg = await c.SnapshotAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(outDir, file), jpg);
        return (true, t.Elapsed.TotalSeconds, jpg.Length);
    }
    catch (Exception e)
    {
        Log($"picture {file} FAILED after {t.Elapsed.TotalSeconds:0.00} s: {e.Message}");
        return (false, t.Elapsed.TotalSeconds, 0);
    }
}

// Average brightness 0-255, from every 8th pixel.
static double Brightness(string file)
{
    using var bmp = SKBitmap.Decode(file);
    double sum = 0;
    int n = 0;
    for (int y = 0; y < bmp.Height; y += 8)
        for (int x = 0; x < bmp.Width; x += 8)
        {
            var c = bmp.GetPixel(x, y);
            sum += 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
            n++;
        }
    return sum / n;
}

await using var a = NewClient("A");
await a.ConnectAsync(pair: true, onButtonPressRequired: () => Log("A: the machine wants its button pressed (not expected)"));
Log($"A connected (id {a.ConnectionId})");
string startMode = await Status("A", a);

// get <url> [key=value ...]: one read-only request, reply printed as is.
if (args.Length > 3 && args[2] == "get")
{
    var query = new JsonObject();
    foreach (var kv in args.Skip(4).Select(s => s.Split('=', 2)))
        query[kv[0]] = kv[1];
    try
    {
        var reply = await a.GetAsync(args[3], parameters: query);
        Log($"GET {args[3]} {query.ToJsonString()}: {reply?.ToJsonString() ?? "(no data)"}");
    }
    catch (Exception e)
    {
        Log($"GET {args[3]} FAILED: {e.Message}");
    }
    return 0;
}

// exptime <exp> <time> [time ...]: overview pictures at manual exposure settings, then back to exp 0.
if (command == "exptime")
{
    Log("== Manual exposure (overview)");
    try
    {
        foreach (string t in args.Skip(4))
        {
            var p = new JsonObject { ["exp"] = int.Parse(args[3]), ["stream"] = "0", ["exp_time"] = int.Parse(t) };
            try
            {
                await a.RequestAsync("POST", "/v1/camera", parameters: p);
            }
            catch (Exception e)
            {
                Log($"{p.ToJsonString()}: FAILED: {e.Message}");
                continue;
            }
            for (int i = 0; i < 2; i++)
            {
                await Task.Delay(700);
                string file = $"exptime-{args[3]}-{t}-{i}.jpg";
                var r = await Picture(a, 0, file);
                if (r.Ok) Log($"{p.ToJsonString()} picture {i + 1}: brightness {Brightness(Path.Combine(outDir, file)):0.0}, {r.Seconds:0.00} s");
            }
        }
    }
    finally
    {
        await a.RequestAsync("POST", "/v1/camera", parameters: new JsonObject { ["exp"] = 0, ["stream"] = "0" });
        Log("overview exposure back to 0 (normal)");
    }
    await Task.Delay(700);
    if ((await Picture(a, 0, "exptime-restored.jpg")).Ok)
        Log($"restored: brightness {Brightness(Path.Combine(outDir, "exptime-restored.jpg")):0.0}");
    await Status("A", a);
    return 0;
}

if (exposureTest)
{
    Log("== Exposure");
    foreach (int stream in new[] { 0, 1 })
    {
        string name = stream == 0 ? "overview" : "close-up";
        if ((await Picture(a, stream, $"exp-{stream}-before.jpg")).Ok)
            Log($"{name} before: brightness {Brightness(Path.Combine(outDir, $"exp-{stream}-before.jpg")):0.0}");
        try
        {
            await a.RequestAsync("POST", "/v1/camera", parameters: new JsonObject { ["exp"] = 0, ["stream"] = stream.ToString() });
            Log($"{name}: exposure set to 0 (normal)");
        }
        catch (Exception e)
        {
            Log($"{name}: setting exposure FAILED: {e.Message}");
        }
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(500);
            string file = $"exp-{stream}-after-{i}.jpg";
            if ((await Picture(a, stream, file)).Ok)
                Log($"{name} after {i + 1}: brightness {Brightness(Path.Combine(outDir, file)):0.0}");
        }
    }
    await Status("A", a);
    return 0;
}

// ---- 1. connections ------------------------------------------------------------------------
Log($"== Test 1: connections (machine {ip}; the desktop app may already be connected)");

var b = NewClient("B");
try
{
    await b.ConnectAsync(pair: true, onButtonPressRequired: () => Log("B: the machine wants its button pressed (not expected)"));
    Log($"B connected (id {b.ConnectionId})");
    await Status("B", b);
    await Status("A", a);
    Log("Holding both for 15 s to see whether either is dropped and whether both get pushes...");
    await Task.Delay(TimeSpan.FromSeconds(15));
    lock (events) Log($"After 15 s: A connected={a.IsConnected} events={events["A"]}; B connected={b.IsConnected} events={events["B"]}");
    await Status("A", a);
    await Status("B", b);
}
catch (Exception e)
{
    Log($"B FAILED: {e.Message}");
}
finally
{
    await b.DisposeAsync();
    Log("B closed");
}
await Status("A", a);

// ---- 2. camera -----------------------------------------------------------------------------
Log($"== Test 2: camera (machine was {startMode} at the start)");
var o = await Picture(a, 0, "overview.jpg");
Log($"overview: {(o.Ok ? $"{o.Bytes / 1024} KB in {o.Seconds:0.00} s" : "failed")}");
var d = await Picture(a, 1, "closeup.jpg");
Log($"close-up: {(d.Ok ? $"{d.Bytes / 1024} KB in {d.Seconds:0.00} s" : "failed")}");

Log($"Time-lapse: {frames} pictures, one every {every} s");
int ok = 0;
for (int i = 0; i < frames; i++)
{
    var next = clock.Elapsed + TimeSpan.FromSeconds(every);
    var r = await Picture(a, 0, $"lapse-{i:000}.jpg");
    if (r.Ok) ok++;
    string mode = await Status($"lapse {i + 1}/{frames} ({(r.Ok ? $"{r.Bytes / 1024} KB, {r.Seconds:0.00} s" : "no picture")})", a);
    var wait = next - clock.Elapsed;
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
}
Log($"Time-lapse: {ok}/{frames} pictures");

Log("Burst: 8 overview pictures back to back");
var burst = Stopwatch.StartNew();
int burstOk = 0;
for (int i = 0; i < 8; i++)
    if ((await Picture(a, 0, $"burst-{i}.jpg")).Ok) burstOk++;
Log($"Burst: {burstOk}/8 in {burst.Elapsed.TotalSeconds:0.00} s = {burstOk / burst.Elapsed.TotalSeconds:0.00} pictures/s");
await Status("A", a);
lock (events) Log($"Done. A connected={a.IsConnected}, events={events["A"]}");
return 0;
