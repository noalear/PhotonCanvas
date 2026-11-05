using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace PhotonCanvas.Remote;

/// <summary>
/// A pretend machine inside the app: try everything without a laser (and what the store's
/// reviewers see). It follows the real rules: a job starts only on a fresh picture of the bed
/// taken with that job loaded, and the camera is blanked while the job runs.
/// </summary>
public sealed class DemoRemote : IRemote
{
    sealed record Design(ProjectItem Item, double EstimateS, double W, double H, uint Colour, Action<Raster, double, double, double, uint> Draw);

    readonly Lock _gate = new();
    readonly List<Design> _designs;
    readonly List<HistoryItem> _history = [];
    readonly Dictionary<string, byte[]> _images = [];
    readonly List<Channel<RemoteEvent>> _listeners = [];
    readonly Dictionary<string, (string Task, DateTimeOffset At, string Mode)> _pictures = [];
    readonly double _speed;
    string _mode = "P_IDLE";
    Design? _loaded;
    string? _taskId;
    DateTimeOffset? _loadedAt, _startedAt;
    double _elapsed;
    bool _fromPhone;
    CancellationTokenSource? _run;

    /// <param name="speed">How much faster than real time the pretend job runs (tests use a large number).</param>
    public DemoRemote(double speed = 1)
    {
        _speed = speed;
        var now = DateTimeOffset.Now;
        _designs =
        [
            new(new("demo-coaster", "Coaster", "Demo designs", now.AddHours(-2), true), 42, 90, 90, 0x3B82F6, (r, x, y, s, c) => { r.Ring(x, y, s * 0.45, s * 0.06, c); r.Ring(x, y, s * 0.3, s * 0.04, c); }),
            new(new("demo-tag", "Name tag", "Demo designs", now.AddDays(-1), true), 35, 80, 30, 0xF59E0B, (r, x, y, s, c) => { r.Box(x - s * 0.45, y - s * 0.17, s * 0.9, s * 0.34, s * 0.03, c); r.Fill(x - s * 0.3, y - s * 0.04, s * 0.6, s * 0.08, c); }),
            new(new("demo-photo", "Photo on basswood", "Demo designs", now.AddDays(-3), false), 48, 100, 70, 0x22C55E, (r, x, y, s, c) => r.Shade(x - s * 0.45, y - s * 0.32, s * 0.9, s * 0.64)),
        ];
        var past = _designs[0];
        var before = Bed(null, 0);
        var after = Bed(past, 1);
        _images["demo:history/1/before"] = before;
        _images["demo:history/1/after"] = after;
        _history.Add(new HistoryItem("1", past.Item.Name, "computer", "sent from the desktop app", now.AddHours(-1).AddMinutes(-1), now.AddHours(-1), now.AddHours(-1).AddSeconds(past.EstimateS * 1.6),
            true, false, past.EstimateS, past.EstimateS * 1.6, "demo:history/1/before", "demo:history/1/after", past.Item.Id));
    }

    public string Name => "Demo machine";
    public bool IsDemo => true;

    public Task<ServerInfo> InfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new ServerInfo("PhotonCanvas (demo)", "demo", "Demo F2 Ultra", "The machine blanks its cameras while a job runs: pictures come out black until it ends."));

    public Task<MachineStatus> StatusAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Status());
    }

    MachineStatus Status()
    {
        JobStatus? job = null;
        if (_loaded is { } d && _mode is "P_WORK" or "P_WORKING" or "P_PAUSE")
        {
            double total = d.EstimateS;
            job = new JobStatus(_taskId!, d.Item.Name, "phone", _mode switch { "P_WORK" => "loaded", "P_PAUSE" => "paused", _ => "running" },
                _loadedAt, _startedAt, _elapsed, _startedAt is null ? null : Math.Min(1, _elapsed / total), _startedAt is null ? null : Math.Max(0, total - _elapsed), total, _fromPhone);
        }
        return new MachineStatus(true, "demo", "Demo F2 Ultra", "demo", _mode, Describe(_mode), 21.0, null, job, _mode == "P_WORKING", DateTimeOffset.Now);
    }

    static string Describe(string mode) => mode switch
    {
        "P_IDLE" => "Ready",
        "P_WORK" => "Job loaded – waiting for the start button",
        "P_WORKING" => "Running a job",
        "P_PAUSE" => "Paused",
        "P_WORK_DONE" => "Job finished",
        _ => mode,
    };

    void Publish(RemoteEvent e)
    {
        foreach (var l in _listeners) l.Writer.TryWrite(e);
    }

    void PublishStatus() => Publish(new StatusChanged(Status()));

    public async IAsyncEnumerable<RemoteEvent> EventsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var ch = Channel.CreateUnbounded<RemoteEvent>();
        lock (_gate)
        {
            _listeners.Add(ch);
            ch.Writer.TryWrite(new StatusChanged(Status()));
        }
        try
        {
            await foreach (var e in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false)) yield return e;
        }
        finally
        {
            lock (_gate) _listeners.Remove(ch);
        }
    }

    public Task<TakenPicture> TakePictureAsync(string camera, int width, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var id = Guid.NewGuid().ToString("N");
            bool blanked = _mode == "P_WORKING";
            // After a job, the result on the board; otherwise a blank board.
            var result = _mode == "P_WORK_DONE" && _history.Count > 0 ? Find(_history[^1].Design) : null;
            var jpeg = blanked ? Blank() : Bed(result, result is null ? 0 : 1, camera == "closeup");
            _pictures[id] = (_taskId ?? "", DateTimeOffset.Now, _mode);
            _images[$"demo:picture/{id}"] = jpeg;
            return Task.FromResult(new TakenPicture(jpeg, id, DateTimeOffset.Now, blanked));
        }
    }

    Design? Find(string name) => _designs.FirstOrDefault(d => d.Item.Name == name);

    public Task<byte[]> ImageAsync(string url, int width, CancellationToken ct = default)
    {
        lock (_gate)
            return _images.TryGetValue(url, out var b) ? Task.FromResult(b) : Task.FromException<byte[]>(new RemoteError(404, "That picture is gone."));
    }

    public Task<IReadOnlyList<ProjectItem>> ProjectsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProjectItem>>([.. _designs.Select(d => d.Item)]);

    Design Get(string id) => _designs.FirstOrDefault(d => d.Item.Id == id) ?? throw new RemoteError(404, "That design isn't in the list any more.");

    public async Task<CheckResult> CheckAsync(string projectId, CancellationToken ct = default)
    {
        var d = Get(projectId);
        await Task.Delay(TimeSpan.FromSeconds(0.8 / Math.Max(1, _speed)), ct).ConfigureAwait(false);
        string preview = $"demo:preview/{d.Item.Id}";
        lock (_gate)
        {
            _images[preview] = Preview(d);
            string? loaded = _mode == "P_WORK" && _loaded is { } l ? $"A job is already loaded on the machine and not started: \"{l.Item.Name}\" (loaded from the phone). It will be replaced by this one." : null;
            string[] lines =
            [
                $"• {d.Item.Name}: {(d.Item.Id == "demo-photo" ? "Image, 30% at 80 mm/s × 1" : "Score, 40% at 200 mm/s × 1")} (diode)",
                "Passes run over the whole job, 1 s pause between.",
                $"Size {d.W:0} × {d.H:0} mm · about {d.EstimateS:0} s.",
                "Machine focus distance: 21.0 mm. Make sure this matches your material.",
            ];
            string text = string.Join('\n', lines.Take(1).Append("").Concat(lines.Skip(1))) + "\n" + (loaded is null ? "" : $"ⓘ {loaded}\n")
                + "\nThis is the demo machine: nothing real moves or fires.\n\nAfter loading, the machine waits. Nothing moves or fires until you start it: from the phone (after a look at the bed) or with its button.";
            return new CheckResult(d.Item.Id, d.Item.Name, d.EstimateS, $"{d.EstimateS:0} s", d.W, d.H, null, [], lines, [], loaded, null, text, preview);
        }
    }

    public async Task<LoadResult> LoadAsync(string projectId, CancellationToken ct = default)
    {
        var d = Get(projectId);
        await Task.Delay(TimeSpan.FromSeconds(0.6 / Math.Max(1, _speed)), ct).ConfigureAwait(false);
        lock (_gate)
        {
            if (_mode is "P_WORKING" or "P_PAUSE") throw new RemoteError(409, "The machine is running a job. Wait for it to finish, or stop it, then send again.");
            _run?.Cancel();
            _loaded = d;
            _taskId = Guid.NewGuid().ToString();
            _loadedAt = DateTimeOffset.Now;
            _startedAt = null;
            _elapsed = 0;
            _fromPhone = false;
            _mode = "P_WORK";
            PublishStatus();
        }
        var pic = await TakePictureAsync("overview", 0, ct).ConfigureAwait(false);
        return new LoadResult(_taskId!, d.Item.Name, d.EstimateS, new PictureInfo(pic.Id!, pic.TakenAt, false, $"demo:picture/{pic.Id}"),
            $"\"{d.Item.Name}\" is loaded. Look at the picture: is the material in place and the bed clear? Then start.");
    }

    public Task<ModeResult> StartAsync(string pictureId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_mode != "P_WORK") throw new RemoteError(409, _mode is "P_WORKING" or "P_PAUSE" ? "A job is already running on the machine." : "No job is loaded on the machine. Load one first.");
            if (!_pictures.TryGetValue(pictureId, out var p)) throw new RemoteError(409, "Take a picture of the bed first: a job only starts from the phone after you've looked.");
            if (DateTimeOffset.Now - p.At > TimeSpan.FromMinutes(2)) throw new RemoteError(409, "That picture is too old. Take a new one, look at it, then start.");
            if (p.Mode != "P_WORK" || p.Task != _taskId) throw new RemoteError(409, "That picture was taken before this job was loaded. Take a new one, look at it, then start.");
            _mode = "P_WORKING";
            _startedAt = DateTimeOffset.Now;
            _fromPhone = true;
            _run = new CancellationTokenSource();
            _ = RunAsync(_loaded!, _run.Token);
            Publish(new JobChanged(new JobEvent("started", _taskId, _loaded!.Item.Name, "phone", true)));
            PublishStatus();
            return Task.FromResult(new ModeResult(_mode, Describe(_mode)));
        }
    }

    async Task RunAsync(Design d, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.5 / _speed), ct).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_mode == "P_WORKING") _elapsed += 0.5;
                    if (_elapsed >= d.EstimateS) { End(true); return; }
                    PublishStatus();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The job is over: a history entry with pictures (inside the lock).</summary>
    void End(bool finished)
    {
        var d = _loaded!;
        string id = (_history.Count + 1).ToString();
        _images[$"demo:history/{id}/before"] = Bed(null, 0);
        _images[$"demo:history/{id}/after"] = Bed(d, finished ? 1 : _elapsed / d.EstimateS);
        _history.Add(new HistoryItem(id, d.Item.Name, "phone", "loaded from the phone", _loadedAt, _startedAt ?? DateTimeOffset.Now, DateTimeOffset.Now,
            finished, _fromPhone, d.EstimateS, _elapsed, $"demo:history/{id}/before", $"demo:history/{id}/after", d.Item.Id));
        _mode = finished ? "P_WORK_DONE" : "P_IDLE";
        Publish(new JobChanged(new JobEvent(finished ? "finished" : "stopped", id, d.Item.Name, "phone", _fromPhone, _elapsed,
            finished ? $"\"{d.Item.Name}\" finished in {_elapsed:0} s." : $"\"{d.Item.Name}\" stopped after {_elapsed:0} s.")));
        Publish(new JobChanged(new JobEvent("picture", id, d.Item.Name)));
        _loaded = null;
        PublishStatus();
    }

    public Task<ModeResult> ControlAsync(string action, CancellationToken ct = default)
    {
        lock (_gate)
        {
            switch (action)
            {
                case "pause" when _mode == "P_WORKING": _mode = "P_PAUSE"; break;
                case "resume" when _mode == "P_PAUSE": _mode = "P_WORKING"; break;
                case "stop" when _mode is "P_WORKING" or "P_PAUSE":
                    _run?.Cancel();
                    End(false);
                    break;
                case "stop" when _mode == "P_WORK":
                    _loaded = null;
                    _mode = "P_IDLE";
                    break;
                case "pause": throw new RemoteError(409, "Nothing is running.");
                case "resume": throw new RemoteError(409, "Nothing is paused.");
                default: throw new RemoteError(409, "There's no job to stop.");
            }
            PublishStatus();
            return Task.FromResult(new ModeResult(_mode, Describe(_mode)));
        }
    }

    public Task<IReadOnlyList<HistoryItem>> HistoryAsync(int max, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<HistoryItem>>([.. _history.AsEnumerable().Reverse().Take(max)]);
    }

    public void Dispose()
    {
        _run?.Cancel();
        lock (_gate)
            foreach (var l in _listeners) l.Writer.TryComplete();
    }

    // ---- pictures, drawn here ----

    /// <summary>The bed from above: honeycomb, a basswood board, and the design burned in as far as <paramref name="done"/> (0–1).</summary>
    static byte[] Bed(Design? d, double done, bool closeUp = false)
    {
        var r = new Raster(640, 480, 0x2A2D33);
        for (int y = 0; y < r.H; y += 12) r.Fill(0, y, r.W, 1, 0x34383F);
        for (int x = 0; x < r.W; x += 12) r.Fill(x, 0, 1, r.H, 0x34383F);
        double s = closeUp ? 2.2 : 1;
        r.Fill(320 - 190 * s, 240 - 140 * s, 380 * s, 280 * s, 0xE8D3A8);
        if (d is not null && done > 0) d.Draw(r, 320, 240, 260 * s, 0x5A3B1C);
        return r.Png();
    }

    static byte[] Blank()
    {
        var r = new Raster(640, 480, 0x000000);
        r.Disc(250, 180, 3, 0xFFD0F0);
        return r.Png();
    }

    static byte[] Preview(Design d)
    {
        var r = new Raster(480, 360, 0xFFFFFF);
        d.Draw(r, 240, 180, 330, d.Colour);
        return r.Png();
    }

    /// <summary>A small RGB picture with a few shapes, written as PNG (no graphics library in the phone's core).</summary>
    sealed class Raster
    {
        public readonly int W, H;
        readonly uint[] _px;

        public Raster(int w, int h, uint bg)
        {
            W = w;
            H = h;
            _px = new uint[w * h];
            Array.Fill(_px, bg);
        }

        void Set(int x, int y, uint c) { if ((uint)x < W && (uint)y < H) _px[y * W + x] = c; }

        public void Fill(double x, double y, double w, double h, uint c)
        {
            for (int yy = (int)y; yy < (int)(y + h); yy++)
                for (int xx = (int)x; xx < (int)(x + w); xx++) Set(xx, yy, c);
        }

        public void Disc(double cx, double cy, double rad, uint c) => Ring(cx, cy, rad / 2, rad / 2, c);

        public void Ring(double cx, double cy, double rad, double half, uint c)
        {
            for (int y = (int)(cy - rad - half); y <= cy + rad + half; y++)
                for (int x = (int)(cx - rad - half); x <= cx + rad + half; x++)
                    if (Math.Abs(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - rad) <= half) Set(x, y, c);
        }

        public void Box(double x, double y, double w, double h, double t, uint c)
        {
            Fill(x, y, w, t, c);
            Fill(x, y + h - t, w, t, c);
            Fill(x, y, t, h, c);
            Fill(x + w - t, y, t, h, c);
        }

        /// <summary>A soft shaded oval, like an engraved photo.</summary>
        public void Shade(double x, double y, double w, double h)
        {
            for (int yy = (int)y; yy < y + h; yy++)
                for (int xx = (int)x; xx < x + w; xx++)
                {
                    double u = (xx - x) / w - 0.5, v = (yy - y) / h - 0.5;
                    double t = Math.Clamp(1 - Math.Sqrt(u * u + v * v) * 2, 0, 1) * (0.6 + 0.4 * Math.Sin(xx * 0.08) * Math.Cos(yy * 0.08));
                    uint g = (uint)(232 - t * 170);
                    Set(xx, yy, (g << 16) | ((uint)(g * 0.85) << 8) | (uint)(g * 0.6));
                }
        }

        public byte[] Png()
        {
            var raw = new byte[H * (W * 3 + 1)];
            for (int y = 0; y < H; y++)
            {
                int o = y * (W * 3 + 1);
                for (int x = 0; x < W; x++)
                {
                    uint c = _px[y * W + x];
                    raw[o + 1 + x * 3] = (byte)(c >> 16);
                    raw[o + 2 + x * 3] = (byte)(c >> 8);
                    raw[o + 3 + x * 3] = (byte)c;
                }
            }
            using var ms = new MemoryStream();
            ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, (uint)W);
            BigEndian(ihdr, 4, (uint)H);
            ihdr[8] = 8; // bits per channel
            ihdr[9] = 2; // RGB
            Chunk(ms, "IHDR", ihdr);
            using (var z = new MemoryStream())
            {
                using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zs.Write(raw);
                Chunk(ms, "IDAT", z.ToArray());
            }
            Chunk(ms, "IEND", []);
            return ms.ToArray();
        }

        static void BigEndian(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            BigEndian(head, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head);
            s.Write(data);
            var crcBytes = new byte[4];
            BigEndian(crcBytes, 0, Crc(head.AsSpan(4), data));
            s.Write(crcBytes);
        }

        static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            uint c = 0xFFFFFFFF;
            foreach (var b in type) c = Step(c, b);
            foreach (var b in data) c = Step(c, b);
            return c ^ 0xFFFFFFFF;

            static uint Step(uint c, byte b)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                return c;
            }
        }
    }
}
