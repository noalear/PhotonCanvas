using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Server;
using Xunit;

namespace PhotonCanvas.Tests;

public class RemoteServerTests
{
    static readonly DateTime Now = new(2026, 10, 23, 2, 0, 0);

    static Picture Pic(string mode, string? task, TimeSpan age) => new("p1", 0, [1, 2, 3], Now - age, mode, task);

    // ---- starting from the phone ----

    [Fact]
    public void AJobStartsOnlyOnAFreshPictureOfTheBedWithThatJobLoaded()
    {
        Assert.Null(StartGuard.Problem(Pic("P_WORK", "job-1", TimeSpan.FromSeconds(30)), Now, "P_WORK", "job-1"));
        Assert.Null(StartGuard.Problem(Pic("P_WORK", "job-1", StartGuard.MaxAge), Now, "P_WORK", "job-1"));
    }

    [Fact]
    public void WithoutAPictureNothingStarts()
    {
        Assert.StartsWith("Take a picture of the bed first", StartGuard.Problem(null, Now, "P_WORK", "job-1"));
    }

    [Fact]
    public void AnOldPictureIsNotEnough()
    {
        Assert.Contains("3 min old", StartGuard.Problem(Pic("P_WORK", "job-1", TimeSpan.FromMinutes(3)), Now, "P_WORK", "job-1"));
    }

    [Fact]
    public void APictureFromBeforeTheJobWasLoadedIsNotEnough()
    {
        // Taken while the machine was idle, or while another job was loaded, or while the mode changed.
        Assert.Contains("before this job was loaded", StartGuard.Problem(Pic("P_IDLE", null, TimeSpan.FromSeconds(10)), Now, "P_WORK", "job-1"));
        Assert.Contains("before this job was loaded", StartGuard.Problem(Pic("P_WORK", "job-0", TimeSpan.FromSeconds(10)), Now, "P_WORK", "job-1"));
        Assert.Contains("before this job was loaded", StartGuard.Problem(Pic("changing", null, TimeSpan.FromSeconds(10)), Now, "P_WORK", "job-1"));
    }

    [Fact]
    public void OnlyALoadedJobCanBeStarted()
    {
        var pic = Pic("P_WORK", "job-1", TimeSpan.FromSeconds(10));
        Assert.Contains("No job is loaded", StartGuard.Problem(pic, Now, "P_IDLE", "job-1"));
        Assert.Contains("already running", StartGuard.Problem(pic, Now, "P_WORKING", "job-1"));
        Assert.Contains("already running", StartGuard.Problem(pic, Now, "P_PAUSE", "job-1"));
    }

    [Fact]
    public void PicturesTakenWhileAJobRunsAreMarkedBlanked()
    {
        Assert.True(Pic("P_WORKING", "j", TimeSpan.Zero).Blanked);
        Assert.False(Pic("P_WORK", "j", TimeSpan.Zero).Blanked);
        Assert.False(Pic("P_IDLE", null, TimeSpan.Zero).Blanked);
    }

    [Fact]
    public void ThePictureStoreKeepsTheLastFewByIdAndScalesDown()
    {
        var store = new PictureStore();
        var first = store.Add(0, Jpeg(400, 300), Now, "P_IDLE", null);
        Assert.Same(first, store.Get(first.Id));
        for (int i = 0; i < 20; i++) store.Add(0, [1], Now, "P_IDLE", null);
        Assert.Null(store.Get(first.Id));
        Assert.Null(store.Get(null));
        var small = SkiaSharp.SKBitmap.Decode(PictureStore.Scaled(Jpeg(400, 300), 200));
        Assert.Equal((200, 150), (small.Width, small.Height));
        // Never made bigger.
        Assert.Equal(400, SkiaSharp.SKBitmap.Decode(PictureStore.Scaled(Jpeg(400, 300), 1000)).Width);
    }

    static byte[] Jpeg(int w, int h)
    {
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        bmp.Erase(SkiaSharp.SKColors.Gray);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        return img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    // ---- the access code ----

    static RemoteConfig Config() => new() { AccessCode = "ABCD-EFGH-JKLM-NPQR" };

    [Fact]
    public void TheAccessCodeIsCheckedHoweverItIsTyped()
    {
        var c = Config();
        Assert.True(c.CodeMatches("ABCD-EFGH-JKLM-NPQR"));
        Assert.True(c.CodeMatches("abcd efgh jklm npqr"));
        Assert.True(c.CodeMatches("ABCDEFGHJKLMNPQR"));
        Assert.False(c.CodeMatches("ABCD-EFGH-JKLM-NPQS"));
        Assert.False(c.CodeMatches(""));
        Assert.False(c.CodeMatches(null));
        Assert.False(new RemoteConfig { AccessCode = "" }.CodeMatches(""));
    }

    [Fact]
    public void NewAccessCodesAreLongRandomAndEasyToType()
    {
        var a = RemoteConfig.NewAccessCode();
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$", a);
        Assert.NotEqual(a, RemoteConfig.NewAccessCode());
    }

    [Fact]
    public void RequestsNeedTheCodeInTheHeaderOrTheQuery()
    {
        var g = new AccessGuard(Config());
        Assert.Null(g.Check("10.0.0.5", "Bearer ABCD-EFGH-JKLM-NPQR", null, Now));
        Assert.Null(g.Check("10.0.0.5", null, "abcdefghjklmnpqr", Now));
        Assert.Equal(401, g.Check("10.0.0.5", null, null, Now)?.Status);
        Assert.Equal(401, g.Check("10.0.0.5", "Basic ABCD-EFGH-JKLM-NPQR", null, Now)?.Status);
    }

    [Fact]
    public void TenWrongCodesLockADeviceOutForFiveMinutes()
    {
        var g = new AccessGuard(Config());
        for (int i = 0; i < AccessGuard.MaxFailures; i++) Assert.Equal(401, g.Check("10.0.0.9", "Bearer WRONG", null, Now)?.Status);
        // Locked out, even with the right code; other devices aren't.
        Assert.Equal(429, g.Check("10.0.0.9", "Bearer ABCD-EFGH-JKLM-NPQR", null, Now.AddMinutes(1))?.Status);
        Assert.Null(g.Check("10.0.0.10", "Bearer ABCD-EFGH-JKLM-NPQR", null, Now.AddMinutes(1)));
        Assert.Null(g.Check("10.0.0.9", "Bearer ABCD-EFGH-JKLM-NPQR", null, Now + AccessGuard.Lockout));
    }

    // ---- the server's history ----

    [Fact]
    public void TheJobLogKeepsJobsAndTheirPicturesAcrossRestarts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-remote-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new JobLog(dir);
            var r = new RemoteJobRecord("20261023-020000-abc123", "t1", "freddie", "phone", Now, Now, null, false, true, 60, null, true, false);
            log.Save(r);
            log.Save(r with { EndedAt = Now.AddMinutes(1), Finished = true, TookS = 61 });
            log.SavePicture(r.Id, "before.jpg", [9, 8, 7]);
            log.AddSent(new SentJobInfo("t1", "freddie", @"C:\d\freddie.pcanvas", 60, 1, 150, 73, Now));

            var again = new JobLog(dir);
            var saved = Assert.Single(again.Newest(10));
            Assert.True(saved.Finished);
            Assert.Equal(61, saved.TookS);
            Assert.Equal([9, 8, 7], again.ReadPicture(r.Id, "before.jpg"));
            Assert.Equal("freddie", again.Sent("t1")?.Design);
            Assert.Null(again.Sent("other"));
            // Only the two pictures, never another path.
            Assert.Null(again.ReadPicture(r.Id, "../remote.json"));
            Assert.Null(again.ReadPicture("..", "before.jpg"));
            Assert.Null(again.ReadPicture(r.Id, "after.jpg"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ListenersGetEveryEventUntilTheyStop()
    {
        var b = new Broadcaster();
        var sub = b.Subscribe();
        b.Publish("job", new { Kind = "finished", TookS = 12.5 });
        Assert.True(sub.Reader.TryRead(out var e));
        Assert.Equal("job", e.Kind);
        Assert.Equal("""{"kind":"finished","tookS":12.5}""", e.Json);
        sub.Dispose();
        Assert.Equal(0, b.Listeners);
        b.Publish("job", new { });
    }

    // ---- the job the phone sends is the desktop's job ----

    [Fact]
    public void TheSavedSettingsGiveTheJobBarsOptions()
    {
        var s = new AppSettings { WholeJobPasses = false, PassDelayS = 5000, PathPauseS = 2, FanGear = 4, PurifierSpeed = 80, MopaOffsetX = 0.1, MopaOffsetY = -0.2 };
        var o = JobSettings.From(s);
        Assert.False(o.WholeJobPasses);
        Assert.Equal(3600, o.PassDelayS); // the job bar's limit
        Assert.Equal(2, o.PathPauseS);
        Assert.Equal(4, o.FanGear);
        Assert.Equal(80, o.PurifierSpeed);
        Assert.Equal(new Vec2(0.1, -0.2), o.MopaOffset);
        Assert.Equal((40.0, 60.0), (o.BlueWatts, o.RedWatts));
        Assert.IsType<WorkMode.Flat>(o.Mode);
        Assert.Equal(MachineProfile.F2Ultra, o.Machine);
        Assert.Null(o.Section);
        Assert.Null(o.OnlyLayer);
    }

    static DocState Squares(Layer layer)
    {
        var s = DocState.New();
        s = s with { Layers = [layer with { Id = s.Layers[0].Id }] };
        return Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
    }

    [Fact]
    public void TheChecksRefuseAJobThatWouldLowerTheHeadOntoTheMaterial()
    {
        var s = Squares(DocState.New().Layers[0] with { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(70, 10).WithPasses(5), StepDownMm = 2 });
        var opt = JobSettings.From(new AppSettings());
        var job = JobBuilder.Build(s, opt);
        // Four step-downs of 2 mm: 8 mm, with the material 10 mm away.
        Assert.Contains("lower the head 8 mm", JobChecks.Review(s, job, opt, new AppSettings(), 10).Refusal);
        Assert.Null(JobChecks.Review(s, job, opt, new AppSettings(), 30).Refusal);
        Assert.Null(JobChecks.Review(s, job, opt, new AppSettings(), null).Refusal);
    }

    [Fact]
    public void TheChecksSayWhatWillHappen()
    {
        var s = Squares(DocState.New().Layers[0] with { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(70, 10).WithPasses(2) });
        var opt = JobSettings.From(new AppSettings { PassDelayS = 1 });
        var job = JobBuilder.Build(s, opt);
        var text = JobChecks.Text(JobChecks.Review(s, job, opt, new AppSettings(), 21));
        Assert.Contains(": Cut, 70% at 10 mm/s × 2 (diode)", text);
        Assert.Contains("Passes run over the whole job, 1 s pause between.", text);
        Assert.Contains("Size 20 × 20 mm · about", text);
        Assert.Contains("Machine focus distance: 21.0 mm.", text);
        Assert.DoesNotContain("NOT IN THIS JOB", text);
    }

    [Fact]
    public void APreviewShowsWhatWillBeMarked()
    {
        var s = Squares(DocState.New().Layers[0] with { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(70, 10) });
        var opt = JobSettings.From(new AppSettings());
        var job = JobBuilder.Build(s, opt);
        var info = new ProjectInfo("id", "squares", "squares.pcanvas", "", Now, false);
        var png = Projects.Preview(new ProjectBuild(info, "squares", "squares.pcanvas", s, job, JobChecks.Review(s, job, opt, new AppSettings(), null), Now, "k"), 400);
        using var bmp = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal(400, bmp.Width);
        Assert.Equal(400, bmp.Height);
    }

    [Fact]
    public void DesignIdsDontChangeAndDontShowThePath()
    {
        string a = Projects.IdOf(@"C:\Users\me\Designs\freddie.pcanvas");
        Assert.Equal(a, Projects.IdOf(@"c:\users\me\designs\FREDDIE.pcanvas"));
        Assert.Equal(16, a.Length);
        Assert.DoesNotContain("freddie", a);
    }
}
