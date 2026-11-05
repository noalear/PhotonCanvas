using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotonCanvas.Doc;
using PhotonCanvas.Remote;
using PhotonCanvas.Server;
using Xunit;
using ConnectionInfo = PhotonCanvas.Remote.ConnectionInfo;

namespace PhotonCanvas.Tests;

/// <summary>The encrypted connection to the computer's own certificate, and reaching it at home or away.</summary>
public class RemoteSecureTests
{
    [Fact]
    public void TheQrCodeCarriesTheCertificateAndTheOtherAddresses()
    {
        var c = ConnectionInfo.TryParse("http://192.168.1.50:8740/#code=ABCD-EFGH-JKLM-NPQR&tls=8741&fp=" + new string('A', 43) + "&alt=192.168.1.50,100.101.102.103");
        Assert.NotNull(c);
        Assert.True(c!.Secure);
        Assert.Equal(new Uri("https://192.168.1.50:8741/"), c.BaseUri);
        Assert.Equal(new Uri("http://192.168.1.50:8740/"), c.PlainUri);
        Assert.Equal(["100.101.102.103"], c.Others!);
        Assert.Equal(["192.168.1.50", "100.101.102.103"], c.AllAddresses);
        var away = c.Using("100.101.102.103");
        Assert.Equal("100.101.102.103", away.Address);
        Assert.Equal(["192.168.1.50"], away.Others!);
        // Half a certificate is no certificate.
        Assert.False(ConnectionInfo.TryParse("http://192.168.1.2:8740/#code=ABCD&fp=" + new string('A', 43))!.Secure);
    }

    [Fact]
    public void TheDesktopsQrTextIsWhatThePhoneReads()
    {
        var config = new RemoteConfig { AccessCode = "ABCD-EFGH-JKLM-NPQR" };
        var tls = new TlsState(8741, new string('B', 43), "pw");
        var text = config.QrText("192.168.1.20", tls, ["192.168.1.20", "100.64.0.9"]);
        var c = ConnectionInfo.TryParse(text)!;
        Assert.Equal(new ConnectionInfo("192.168.1.20", 8740, "ABCD-EFGH-JKLM-NPQR", 8741, new string('B', 43), ["100.64.0.9"]) with { Others = c.Others }, c);
        Assert.Equal(["100.64.0.9"], c.Others!);
        Assert.DoesNotContain("pw", text);
        // The phone page only takes the code from it.
        Assert.StartsWith("http://192.168.1.20:8740/#code=ABCD-EFGH-JKLM-NPQR&", text);
    }

    [Fact]
    public void TheFinishTimeIsShownForARunningJob()
    {
        var now = new DateTimeOffset(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);
        JobStatus Job(string stage, double? left) => new("t", "d", "phone", stage, null, now, 60, null, left, 600, false);
        Assert.Equal($"done about {now.AddMinutes(75).ToLocalTime():t}", JobTimes.FinishText(Job("running", 75 * 60), now));
        Assert.Null(JobTimes.FinishText(Job("loaded", 600), now));
        Assert.Null(JobTimes.FinishText(Job("running", null), now));
        Assert.Contains(now.AddHours(30).ToLocalTime().ToString("ddd"), JobTimes.FinishText(Job("running", 30 * 3600), now));
    }

    [Fact]
    public void ThePhonesSettingsAreKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-phone-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(WatchMode.DuringJobs, PhoneSettings.Load(dir).Watch);
            new PhoneSettings(WatchMode.Always, true).Save(dir);
            Assert.Equal(new PhoneSettings(WatchMode.Always, true), PhoneSettings.Load(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TailscaleAddressesAreRecognised()
    {
        Assert.True(ConnectionInfo.IsTailscale("100.101.102.103"));
        Assert.True(ConnectionInfo.IsTailscale("100.64.0.1"));
        Assert.False(ConnectionInfo.IsTailscale("100.128.0.1"));
        Assert.False(ConnectionInfo.IsTailscale("192.168.1.20"));
        Assert.False(ConnectionInfo.IsTailscale("pc.local"));
    }

    /// <summary>A small stand-in for the server on this computer: plain and HTTPS ports, /tls and /api/info.</summary>
    sealed class TestServer : IAsyncDisposable
    {
        readonly WebApplication _app;
        public int Plain, Secure;
        public TlsState Tls = null!;
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "pc-tls-" + Guid.NewGuid().ToString("N"));

        TestServer(WebApplication app) => _app = app;

        public static async Task<TestServer> StartAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pc-tls-" + Guid.NewGuid().ToString("N"));
            var (cert, tls) = TlsCertificate.LoadOrCreate(0, dir);
            var b = WebApplication.CreateSlimBuilder();
            b.Logging.ClearProviders();
            b.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(cert));
            });
            var app = b.Build();
            var s = new TestServer(app);
            app.MapGet(Routes.Tls, () => Results.Json(new TlsInfo(s.Secure, s.Tls.Fingerprint)));
            app.MapGet(Routes.Info, () => Results.Json(new ServerInfo("PhotonCanvas", "test", "F2", "")));
            await app.StartAsync();
            var ports = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(a => new Uri(a)).ToList();
            s.Plain = ports.First(u => u.Scheme == "http").Port;
            s.Secure = ports.First(u => u.Scheme == "https").Port;
            s.Tls = tls;
            return s;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            try { Directory.Delete(Dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task TheCertificateIsMadeOnceAndKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-tls-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (a, sa) = TlsCertificate.LoadOrCreate(8741, dir);
            var (b, sb) = TlsCertificate.LoadOrCreate(8751, dir);
            Assert.Equal(sa.Fingerprint, sb.Fingerprint);
            Assert.Equal(8751, TlsState.Read(dir)!.Port);
            Assert.True(b.HasPrivateKey);
            Assert.Equal(43, sa.Fingerprint.Length);
            a.Dispose();
            b.Dispose();
        }
        finally
        {
            await Task.Yield();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ATypedAddressLearnsTheCertificateThenTalksHttpsToItAlone()
    {
        await using var server = await TestServer.StartAsync();
        var typed = new ConnectionInfo("127.0.0.1", server.Plain, "ABCD-EFGH-JKLM-NPQR");
        var secure = await HttpRemote.SecureAsync(typed);
        Assert.True(secure.Secure);
        Assert.Equal(server.Secure, secure.TlsPort);
        Assert.Equal(server.Tls.Fingerprint, secure.Fingerprint);

        using (var r = new HttpRemote(secure))
            Assert.Equal("PhotonCanvas", (await r.InfoAsync()).Product);

        // Someone else's certificate (or a new one after a reset): refused, and the phone asks for a new scan.
        using var wrong = new HttpRemote(secure with { Fingerprint = new string('X', 43) });
        var e = await Assert.ThrowsAsync<RemoteError>(() => wrong.InfoAsync());
        Assert.Equal(495, e.Status);
        Assert.True(e.NeedsCode);
        Assert.Contains("scan the QR code", e.Message);
    }

    [Fact]
    public async Task TheComputerIsReachedAtWhicheverAddressAnswers()
    {
        await using var server = await TestServer.StartAsync();
        // 127.0.0.2 is this computer too, but nothing listens there: like the home address when away.
        var c = new ConnectionInfo("127.0.0.2", server.Plain, "ABCD", Others: ["127.0.0.1"]);
        var reached = await HttpRemote.ReachAsync(c);
        Assert.Equal("127.0.0.1", reached?.Address);
        Assert.Equal(["127.0.0.2"], reached!.Others!);
        // The usual address answering: it stays.
        Assert.Same(reached, await HttpRemote.ReachAsync(reached));
        // None answering.
        Assert.Null(await HttpRemote.ReachAsync(new ConnectionInfo("127.0.0.2", server.Plain, "ABCD", Others: ["127.0.0.3"])));
    }
}
