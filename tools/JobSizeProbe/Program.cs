// Temporary: how big a job will the machine load? Uploads test jobs of growing size, one at a
// time, waits for each to show as loaded, and stops at the first one that doesn't load.
//
//   dotnet run -c Release --project tools/JobSizeProbe -- [machine ip] [sizes in MB, e.g. 300 600 1200 2000]
//
// Safe by construction: the test jobs move the galvo between two points 2 mm apart with the laser
// off (G0 only, no G1, no power anywhere), are uploaded without auto-start, and are never started.
// Each one replaces only the previous test job; at the end the last one is cancelled, so the
// machine is left idle. It won't touch a job of yours that is loaded or running.
//
// The machine IP defaults to the one the app last connected to; keys come from the app's keys.json.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;

string? ip = args.FirstOrDefault(a => a.Contains('.')) ?? AppSettings.Load().LastMachineIp;
var sizesMb = args.Where(a => !a.Contains('.') && !a.StartsWith('-')).Select(long.Parse).ToList();
if (sizesMb.Count == 0) sizesMb = [300, 600, 1200, 2000];
if (ip is null) { Console.WriteLine("No machine IP: give it as the first argument."); return 2; }
var keys = MachineKeys.Load();
if (!keys.PairingValid) { Console.WriteLine($"No valid pairing key in {MachineKeys.DefaultPath}."); return 1; }

var clock = Stopwatch.StartNew();
var logPath = Path.Combine(Path.GetTempPath(), $"JobSizeProbe-{DateTime.Now:yyyyMMdd-HHmmss}.log");
var logFile = new StreamWriter(logPath) { AutoFlush = true };
void Log(string m) { string line = $"{DateTime.Now:HH:mm:ss} [{clock.Elapsed.TotalSeconds,6:0}s] {m}"; Console.WriteLine(line); logFile.WriteLine(line); }

// The pieces of a real job (a 2 mm square), with every firing line turned into a laser-off move.
var doc = DocState.New();
doc = Ops.AddShapes(doc, [new PathShape { LayerId = doc.Layers[0].Id, Geometry = PathGeometry.Rect(100, 100, 2, 2) }]);
var small = JobBuilder.Build(doc, new JobOptions());
static string Dark(string g) => Regex.Replace(g, @"^G1X([^\n]*?)S\d+F\d+", "G0X$1", RegexOptions.Multiline).Replace("\nG1X", "\nG0X");
string motion = Dark(small.Motion), border = Dark(small.Border);
int cut = motion.IndexOf("# blockConfig", StringComparison.Ordinal);
cut = motion.IndexOf('\n', cut) + 1;
int tailAt = motion.IndexOf("\n# GS004-4 VECTOR TAIL", StringComparison.Ordinal);
byte[] head = Encoding.UTF8.GetBytes(motion[..cut]), tail = Encoding.UTF8.GetBytes(motion[tailAt..]);
foreach (var g in new[] { motion, border })
    if (Regex.IsMatch(g, @"^G1[^\n]*S[1-9]", RegexOptions.Multiline) || Regex.IsMatch(g, @"^G1X", RegexOptions.Multiline))
        throw new InvalidOperationException("A test job would fire the laser: stopping.");
var others = XfPackage.Read(XfPackage.Build(new()
{
    Motion = "", Border = border, PreviewJpeg = XfPackage.PreviewJpeg([[new Vec2(100, 100), new Vec2(102, 102)]]),
    Light = "blue", Params = new System.Text.Json.Nodes.JsonArray(), EstimatedSeconds = 60,
})).Where(m => m.Name != "motion.gcode").ToList();
byte[] pair = Encoding.ASCII.GetBytes("G0X100Y100\nG0X102Y102\n");

byte[] Package(long motionBytes)
{
    // The tar written straight into one array: a 2 GB job can't go through a string.
    long body = Math.Max(pair.Length, (motionBytes - head.Length - tail.Length) / pair.Length * pair.Length);
    long motionLen = head.Length + body + tail.Length;
    static long Padded(long n) => 512 + (n + 511) / 512 * 512;
    long total = others.Sum(m => Padded(m.Data.Length)) + Padded(motionLen) + 1024;
    if (total > Array.MaxLength) throw new InvalidOperationException($"{motionBytes / 1e6:0} MB is more than one upload can hold here.");
    var o = new byte[total];
    long at = 0;
    void Member(string name, long len, Action<long> write)
    {
        Ustar(name, len, DateTimeOffset.Now.ToUnixTimeSeconds()).CopyTo(o, at);
        at += 512;
        write(at);
        at += (len + 511) / 512 * 512;
    }
    foreach (var m in others.Take(2)) Member(m.Name, m.Data.Length, p => m.Data.CopyTo(o, p));
    Member("motion.gcode", motionLen, p =>
    {
        head.CopyTo(o, p);
        long q = p + head.Length;
        for (long k = 0; k < body; k += pair.Length) pair.CopyTo(o, q + k);
        tail.CopyTo(o, q + body);
    });
    foreach (var m in others.Skip(2)) Member(m.Name, m.Data.Length, p => m.Data.CopyTo(o, p));
    return o;
}

static byte[] Ustar(string name, long size, long mtime)
{
    var h = new byte[512];
    void Put(int off, string s) => Encoding.ASCII.GetBytes(s).CopyTo(h, off);
    string Octal(long v, int width) => Convert.ToString(v, 8).PadLeft(width - 1, '0');
    Put(0, name);
    Put(100, Octal(Convert.ToInt32("666", 8), 8));
    Put(108, Octal(0, 8));
    Put(116, Octal(0, 8));
    Put(124, Octal(size, 12));
    Put(136, Octal(mtime, 12));
    for (int i = 148; i < 156; i++) h[i] = (byte)' ';
    h[156] = (byte)'0';
    Put(257, "ustar\0");
    Put(263, "00");
    Put(148, Convert.ToString(h.Sum(b => (int)b), 8).PadLeft(6, '0') + "\0 ");
    return h;
}

if (args.Contains("--dry"))
{
    // Builds the packages and checks them, without the machine.
    Console.WriteLine("---- motion around the moves\n" + Encoding.UTF8.GetString(head)[^220..] + "G0X100Y100\nG0X102Y102\n…" + Encoding.UTF8.GetString(tail) + "\n---- border\n" + border);
    foreach (var mb in sizesMb)
    {
        var xf = Package(mb * 1_000_000);
        var back = XfPackage.Read(xf);
        var mo = back.Single(m => m.Name == "motion.gcode").Data;
        Log($"{mb} MB: package {xf.Length / 1e6:0.0} MB, members {string.Join(", ", back.Select(m => m.Name))}, motion ends \"{Encoding.UTF8.GetString(mo, mo.Length - 30, 30).Replace("\n", "⏎")}\"");
    }
    return 0;
}

Log($"Connecting to {ip}… (if the machine asks, press its button to allow this program)");
await using var client = new DeviceClient(ip, new DeviceClientOptions { PairingKey = keys.Pairing, StatePath = Path.Combine(Path.GetTempPath(), "JobSizeProbe-state.json"), TransferTimeout = TimeSpan.FromSeconds(120) });
await client.ConnectAsync(pair: true, onButtonPressRequired: () => Log("Press the button on the machine to allow this program."));
var start = await client.GetRuntimeStateAsync();
if (start.Mode is not ("P_IDLE" or "P_WORK_DONE" or "P_FINISH"))
{
    Log($"The machine is {DeviceClient.DescribeMode(start.Mode)} ({start.Mode}). This test only runs when nothing is loaded or running, so your job is left alone. Run it again when the machine is idle.");
    return 1;
}

string? ours = null;
long? biggestOk = null;
foreach (var mb in sizesMb)
{
    Log($"Making a {mb} MB test job…");
    byte[] xf = Package(mb * 1_000_000);
    string taskId = Guid.NewGuid().ToString();
    if (ours is not null && (await client.GetRuntimeStateAsync()).TaskId is { } loaded && loaded != ours)
    {
        Log("Something else is loaded on the machine now: stopping the test to leave it alone.");
        ours = null;
        break;
    }
    int lastPct = -1;
    var progress = new Progress<double>(f => { int p = (int)(f * 100); if (p / 10 != lastPct / 10) { lastPct = p; Log($"  uploading {p}%"); } });
    var t0 = clock.Elapsed;
    try
    {
        await client.UploadJobAsync(xf, progress, replaceLoadedJob: ours is not null, taskId: taskId, autoStart: false);
        ours = taskId;
    }
    catch (Exception e)
    {
        Log($"  {mb} MB: the upload failed after {(clock.Elapsed - t0).TotalSeconds:0} s: {e.Message}");
        break;
    }
    xf = [];
    GC.Collect();
    // Loaded = the machine shows our job waiting for its button.
    string mode = "";
    var wait = Stopwatch.StartNew();
    while (wait.Elapsed < TimeSpan.FromSeconds(60 + mb / 10))
    {
        var st = await client.GetRuntimeStateAsync();
        mode = st.Mode;
        if (mode == "P_WORK" && st.TaskId == taskId) break;
        if (mode is "P_WORKING") { Log("  The job was started on the machine: stopping it."); await client.StopJobAsync(); return 1; }
        await Task.Delay(1000);
    }
    if (mode == "P_WORK")
    {
        biggestOk = mb;
        Log($"  {mb} MB loaded: upload and load took {(clock.Elapsed - t0).TotalSeconds:0} s.");
    }
    else
    {
        Log($"  {mb} MB uploaded but didn't show as loaded (the machine says {mode}).");
        break;
    }
}

// Leave the machine idle: cancel our test job if it's still the one loaded (nothing moves).
try
{
    var end = await client.GetRuntimeStateAsync();
    if (ours is not null && end.Mode == "P_WORK" && end.TaskId == ours) { await client.StopJobAsync(); Log("Cancelled the test job: the machine is idle."); }
}
catch (Exception e) { Log($"Couldn't cancel the test job ({e.Message}): press Stop in the app or long-press the machine's button."); }
Log(biggestOk is { } b ? $"RESULT: the biggest test job that loaded is {b} MB." : "RESULT: none of the test jobs loaded.");
Log($"Log saved to {logPath}");
return 0;
