using System.Net;
using System.Text;
using System.Text.Json;
using PhotonCanvas.Remote;
using PhotonCanvas.Server;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The phone's side: reading the QR code, the server's answers and events, errors, and the demo machine.</summary>
public class RemoteClientTests
{
    // ---- where the server is ----

    [Fact]
    public void TheQrCodeFromRemoteAccessGivesAddressPortAndCode()
    {
        var c = ConnectionInfo.TryParse("http://192.168.50.247:8740/#code=4CL5-7RF3-MPZL-TZHJ");
        Assert.Equal(new ConnectionInfo("192.168.50.247", 8740, "4CL5-7RF3-MPZL-TZHJ"), c);
        Assert.Equal(new Uri("http://192.168.50.247:8740/"), c!.BaseUri);
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", 8740)]
    [InlineData("192.168.1.20:9000", "192.168.1.20", 9000)]
    [InlineData(" http://pc.local:8740/ ", "pc.local", 8740)]
    [InlineData("100.101.102.103", "100.101.102.103", 8740)]
    [InlineData("http://192.168.1.20:80", "192.168.1.20", 80)]
    public void ATypedAddressWorksWithOrWithoutHttpAndPort(string typed, string address, int port)
    {
        var c = ConnectionInfo.TryParse(typed, "abcd efgh jklm npqr");
        Assert.Equal(address, c?.Address);
        Assert.Equal(port, c?.Port);
        Assert.Equal("ABCD-EFGH-JKLM-NPQR", c?.Code);
    }

    [Theory]
    [InlineData("", "ABCD")]
    [InlineData("192.168.1.20", "")]
    [InlineData("http://192.168.1.20:8740/", null)]
    [InlineData("ftp://192.168.1.20", "ABCD")]
    [InlineData("WIFI:S:home;T:WPA;P:secret;;", null)]
    public void SomethingElseIsNotAConnection(string text, string? code) => Assert.Null(ConnectionInfo.TryParse(text, code));

    [Fact]
    public void ThePhoneRemembersTheComputer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-phone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionStore(dir);
            Assert.Null(store.Load());
            var c = new ConnectionInfo("192.168.1.20", 8740, "ABCD-EFGH-JKLM-NPQR");
            store.Save(c);
            Assert.Equal(c, new ConnectionStore(dir).Load());
            store.Forget();
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ---- what the server sends ----

    static async Task<List<(string, string)>> Read(string stream)
    {
        var o = new List<(string, string)>();
        await foreach (var e in ServerSentEvents.ReadAsync(new StringReader(stream), TimeSpan.FromSeconds(5))) o.Add(e);
        return o;
    }

    [Fact]
    public async Task EventsAreReadAsTheServerWritesThem()
    {
        var events = await Read("event: status\ndata: {\"a\":1}\n\n: ping\n\nevent: job\ndata: {\"b\":\ndata: 2}\n\ndata: plain\n\n");
        Assert.Equal([("status", "{\"a\":1}"), ("job", "{\"b\":\n2}"), ("message", "plain")], events);
    }

    [Fact]
    public async Task AQuietConnectionCountsAsDropped()
    {
        var never = new BlockingReader();
        var e = await Assert.ThrowsAsync<RemoteError>(async () => { await foreach (var _ in ServerSentEvents.ReadAsync(never, TimeSpan.FromMilliseconds(100))) { } });
        Assert.True(e.Unreachable);
    }

    sealed class BlockingReader : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
    }

    [Fact]
    public void ThePhoneReadsWhatTheServerSends()
    {
        // The server writes events with its own JSON settings; the phone reads them with the shared ones.
        var b = new Broadcaster();
        using var sub = b.Subscribe();
        var job = new JobStatus("t1", "freddie", "phone", "running", DateTimeOffset.Now.AddMinutes(-3), DateTimeOffset.Now.AddMinutes(-2), 120.5, null, 600, 720, true);
        b.Publish("status", new MachineStatus(true, "192.168.50.48", "F2 Ultra", "40.54", "P_WORKING", "Running a job", 21.3, null, job, true, DateTimeOffset.Now));
        b.Publish("job", new JobEvent("finished", "r1", "freddie", "phone", true, 61.5, "\"freddie\" finished in 1 min 01 s."));
        b.Publish("alert", new AlertEvent("flame", "The machine reports a possible flame. Check it now.", DateTimeOffset.Now));
        b.Publish("something-new", new { x = 1 });

        var read = new List<RemoteEvent?>();
        while (sub.Reader.TryRead(out var e)) read.Add(HttpRemote.Parse(e.Kind, e.Json));
        var status = Assert.IsType<StatusChanged>(read[0]).Status;
        Assert.Equal("P_WORKING", status.Mode);
        Assert.Equal(job with { LoadedAt = status.Job!.LoadedAt, StartedAt = status.Job.StartedAt }, status.Job);
        Assert.Equal(61.5, Assert.IsType<JobChanged>(read[1]).Job.TookS);
        Assert.Equal("flame", Assert.IsType<AlarmRaised>(read[2]).Alert.Kind);
        Assert.Null(read[3]); // a newer server's event this app doesn't know
    }

    // ---- errors ----

    sealed class Answer(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("No route to host");
    }

    static readonly ConnectionInfo Pc = new("192.168.1.20", 8740, "ABCD-EFGH-JKLM-NPQR");

    [Fact]
    public async Task EveryRequestCarriesTheCodeAndAnswersAreRead()
    {
        var h = new Answer(HttpStatusCode.OK, """{"product":"PhotonCanvas","version":"0.1.0","machine":"F2","cameraNote":"…","api":1}""");
        using var r = new HttpRemote(Pc, h);
        var info = await r.InfoAsync();
        Assert.Equal("PhotonCanvas", info.Product);
        Assert.Equal("Bearer ABCD-EFGH-JKLM-NPQR", h.Last!.Headers.Authorization!.ToString());
        Assert.Equal("http://192.168.1.20:8740/api/info", h.Last.RequestUri!.ToString());
    }

    [Fact]
    public async Task TheServersOwnWordsComeThroughAsTheError()
    {
        using var r = new HttpRemote(Pc, new Answer(HttpStatusCode.Conflict, """{"error":"That picture is 3 min old. Take a new one, look at it, then start."}"""));
        var e = await Assert.ThrowsAsync<RemoteError>(() => r.StartAsync("p1"));
        Assert.Equal(409, e.Status);
        Assert.StartsWith("That picture is 3 min old", e.Message);
        Assert.False(e.NeedsCode);

        using var locked = new HttpRemote(Pc, new Answer(HttpStatusCode.Unauthorized, """{"error":"Wrong or missing access code."}"""));
        Assert.True((await Assert.ThrowsAsync<RemoteError>(() => locked.StatusAsync())).NeedsCode);

        using var gone = new HttpRemote(Pc, new Unreachable());
        var g = await Assert.ThrowsAsync<RemoteError>(() => gone.StatusAsync());
        Assert.True(g.Unreachable);
        Assert.Contains("same network", g.Message);
    }

    // ---- the demo machine follows the real rules ----

    [Fact]
    public async Task TheDemoOnlyStartsOnAFreshPictureOfTheLoadedJob()
    {
        using var demo = new DemoRemote(speed: 200);
        var before = await demo.TakePictureAsync("overview", 0); // taken before anything was loaded
        var designs = await demo.ProjectsAsync();
        var check = await demo.CheckAsync(designs[0].Id);
        Assert.Null(check.Refusal);
        Assert.NotEmpty(await demo.ImageAsync(check.Preview, 0));
        var load = await demo.LoadAsync(designs[0].Id);
        Assert.Equal("loaded", (await demo.StatusAsync()).Job?.Stage);

        Assert.Contains("before this job was loaded", (await Assert.ThrowsAsync<RemoteError>(() => demo.StartAsync(before.Id!))).Message);
        Assert.Contains("Take a picture", (await Assert.ThrowsAsync<RemoteError>(() => demo.StartAsync("nonsense"))).Message);
        Assert.Equal("P_WORKING", (await demo.StartAsync(load.Picture!.Id)).Mode);
        Assert.True((await demo.TakePictureAsync("overview", 0)).Blanked);

        // It finishes, with pictures before and after in the history.
        for (int i = 0; i < 100 && (await demo.StatusAsync()).Mode == "P_WORKING"; i++) await Task.Delay(20);
        Assert.Equal("P_WORK_DONE", (await demo.StatusAsync()).Mode);
        var last = (await demo.HistoryAsync(10))[0];
        Assert.True(last.Finished);
        Assert.True(last.StartedFromPhone);
        Assert.NotEmpty(await demo.ImageAsync(last.After!, 0));
    }

    [Fact]
    public async Task TheDemoPausesContinuesAndStops()
    {
        using var demo = new DemoRemote(speed: 1);
        var events = new List<RemoteEvent>();
        using var cts = new CancellationTokenSource();
        var listening = Task.Run(async () => { await foreach (var e in demo.EventsAsync(cts.Token)) lock (events) events.Add(e); });
        var load = await demo.LoadAsync("demo-tag");
        await demo.StartAsync(load.Picture!.Id);
        Assert.Equal("P_PAUSE", (await demo.ControlAsync("pause")).Mode);
        Assert.Equal("P_WORKING", (await demo.ControlAsync("resume")).Mode);
        Assert.Equal("P_IDLE", (await demo.ControlAsync("stop")).Mode);
        Assert.Contains("Nothing is running", (await Assert.ThrowsAsync<RemoteError>(() => demo.ControlAsync("pause"))).Message);
        Assert.False((await demo.HistoryAsync(1))[0].Finished);
        await Task.Delay(100);
        cts.Cancel();
        try { await listening; } catch (OperationCanceledException) { }
        lock (events)
        {
            Assert.IsType<StatusChanged>(events[0]);
            Assert.Contains(events, e => e is JobChanged { Job.Kind: "started" });
            Assert.Contains(events, e => e is JobChanged { Job.Kind: "stopped" });
        }
    }

    [Fact]
    public async Task TheDemosPicturesArePngs()
    {
        using var demo = new DemoRemote();
        var png = (await demo.TakePictureAsync("overview", 0)).Jpeg;
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        using var bmp = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal((640, 480), (bmp.Width, bmp.Height));
    }

    [Fact]
    public void TheSharedJsonIsCamelCase()
    {
        var json = JsonSerializer.Serialize(new ModeResult("P_WORKING", "Running a job"), RemoteJson.Default.ModeResult);
        Assert.Equal("""{"mode":"P_WORKING","state":"Running a job"}""", json);
    }
}
