using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class RotaryScanTests
{
    /// <summary>
    /// A pretend rotary holding a round rod (radius <c>R</c>) whose centre is <c>E</c> off the axis, on the side that
    /// comes to the top after turning <c>HighDeg</c>; the axis is <c>Axis</c> above the bed.
    /// </summary>
    sealed class FakeRod(double axis, double r, double e, double highDeg, int cancelAfter = -1) : IRotaryScanRig
    {
        public double At;
        public List<double> Turns = [];
        public int Measured;

        public Task TurnAsync(double degrees, CancellationToken ct)
        {
            Turns.Add(degrees);
            At += degrees;
            return Task.CompletedTask;
        }

        public double Top(double turned)
        {
            double t = (highDeg - turned) * Math.PI / 180;
            return axis + e * Math.Cos(t) + Math.Sqrt(r * r - e * e * Math.Sin(t) * Math.Sin(t));
        }

        public Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct)
        {
            if (Measured == cancelAfter) throw new OperationCanceledException();
            Measured++;
            // The end of the rod at X > 120 is out of reach (unread).
            return Task.FromResult(xs.Select(x => x > 120 ? (double?)null : Top(At)).ToArray());
        }
    }

    [Fact]
    public void AnglesCoverAWholeTurnWithoutRepeatingTheStart()
    {
        var whole = new RotaryScanPlan(0, 360, 24, [110], 110, "pointer").Angles();
        Assert.Equal(24, whole.Length);
        Assert.Equal(15, whole[1], 6);
        Assert.Equal(345, whole[^1], 6);
        var part = new RotaryScanPlan(0, 90, 4, [110], 110, "pointer").Angles();
        Assert.Equal([0, 30, 60, 90], part);
        Assert.Equal([100, 105, 110], RotaryScanPlan.Places(100, 110, 3));
        Assert.Equal([105], RotaryScanPlan.Places(100, 110, 1));
    }

    [Fact]
    public void TheAnglesAreSaidInWords()
    {
        Assert.Equal("4 angles, every 90°: 0, 90, 180, 270°", new RotaryScanPlan(0, 360, 4, [110], 110, "pointer").AnglesText());
        Assert.Equal("5 angles, every 72°: 0, 72, 144, 216, 288°", new RotaryScanPlan(0, 360, 5, [110], 110, "pointer").AnglesText());
        Assert.Equal("24 angles, every 15°: 0, 15, 30 … 345°", new RotaryScanPlan(0, 360, 24, [110], 110, "pointer").AnglesText());
        Assert.Equal("3 angles, every 22.5°: -22.5, 0, 22.5°", new RotaryScanPlan(-22.5, 22.5, 3, [110], 110, "pointer").AnglesText());
    }

    /// <summary>A round rod (radius 10, axis at 50) whose centre is off the axis by <paramref name="off"/>(x), towards the side on top at <paramref name="highDeg"/>.</summary>
    static RotaryScan Rod(Func<double, double> off, double highDeg, int stops = 8)
    {
        double[] xs = [90, 110, 130, 150, 170];
        var angles = Enumerable.Range(0, stops).Select(i => 360.0 * i / stops).ToArray();
        return new RotaryScan
        {
            AnglesDeg = angles,
            Xs = xs,
            Heights = [.. angles.Select(a => xs.Select(x =>
            {
                double e = off(x), t = (a - highDeg) * Math.PI / 180;
                // Top of the circle: its centre is e·cos t above the axis and e·sin t to the side.
                double side = e * Math.Sin(t);
                return (double?)(50 + e * Math.Cos(t) + Math.Sqrt(100 - side * side));
            }).ToArray())],
        };
    }

    [Fact]
    public void ACrookedPartIsToldFromAnOffCentreOne()
    {
        // Kyle, 2026-11-09: the chuck sags, so the part wobbles more the further out it is.
        var crooked = Rod(x => 0.2 + (x - 90) * 0.01, 60).Runout()!.Value;
        Assert.True(crooked.Tilted);
        Assert.Equal(1, crooked.PerHundredMm, 1);
        Assert.Equal(0.2, crooked.FromOffset, 1);
        Assert.Equal(1.0, crooked.ToOffset, 1);
        Assert.Equal(60, crooked.TiltHighDeg, 0);
        var offCentre = Rod(_ => 0.5, 200).Runout()!.Value;
        Assert.False(offCentre.Tilted);
        Assert.Equal(0.5, offCentre.FromOffset, 1);
        Assert.Equal(200, offCentre.FromHighDeg, 0);
    }

    [Fact]
    public void AWholeTurnStopsAtEverySideOfTheStock()
    {
        // Kyle, 2026-11-09: 4 stops measure each side; a square bar wants 8 (faces and corners).
        Assert.Equal(4, RotaryScanPlan.StopsFor(StockKind.Scanned));
        Assert.Equal(4, RotaryScanPlan.StopsFor(StockKind.Round));
        Assert.Equal(8, RotaryScanPlan.StopsFor(StockKind.Square));
        Assert.Equal(8, RotaryScanPlan.StopsFor(StockKind.Rectangle));
        Assert.Equal(12, RotaryScanPlan.StopsFor(StockKind.Hexagon));
        Assert.Equal(4, new AppSettings().RotaryScanStops);
        Assert.Equal([0, 45, 90, 135, 180, 225, 270, 315], new RotaryScanPlan(0, 360, RotaryScanPlan.StopsFor(StockKind.Square), [110], 110, "pointer").Angles());
    }

    [Fact]
    public void OnTheRotaryThePageIsPutOnTheBedWithItsStartRowOnTheLaserLine()
    {
        // Kyle, 2026-11-09: on a 32 mm rod the page's middle row (Y 50.27) is on the laser line (Y 110), so a design at
        // page Y 41–59 is over the rod at bed Y 101–119; moving the design doesn't move that (the photo stays put).
        var s = new AppSettings { OwnsCustomRotary = true, WorkMode = "Rotary", RotaryAttachment = WorkModes.CustomRotary, RotaryDiameterMm = 32, RotaryLineMm = 110 };
        double mid = Math.Round(Math.PI * 32, 2) / 2;
        var d = DocState.New();
        d = Ops.AddShapes(d, [new PathShape { LayerId = d.Layers[0].Id, Geometry = PathGeometry.Rect(60, 41, 60, 18) }]);
        Assert.Equal(110 - mid, WorkModes.BedShift(s, d)!.Value, 6);
        var moved = Ops.AddShapes(DocState.New(), [new PathShape { LayerId = d.Layers[0].Id, Geometry = PathGeometry.Rect(60, 5, 60, 18) }]);
        Assert.Equal(110 - mid, WorkModes.BedShift(s, moved)!.Value, 6);
        // A rotary start line faces the laser instead.
        var withStart = d with { Guides = [new Guide(false, 30) { RotaryStart = true }] };
        Assert.Equal(80, WorkModes.BedShift(s, withStart)!.Value, 6);
        // The flat bed is the bed.
        Assert.Null(WorkModes.BedShift(new AppSettings(), d));
    }

    [Fact]
    public void ARotaryJobStartsWithThePageMiddleUnderTheLaser()
    {
        // A design moved down the page is turned further round, as the canvas shows it.
        double circ = 100;
        var s = DocState.New();
        s = Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(60, 70, 10, 10) }]);
        var job = JobBuilder.Build(s, new JobOptions { Machine = MachineProfile.F2Ultra with { FieldH = circ }, Mode = new WorkMode.Rotary(1, 1, circ) });
        Assert.Equal(50, Assert.IsType<WorkMode.Rotary>(job.Job.Mode).OriginY, 6);
    }

    [Fact]
    public async Task AScanMeasuresEveryAngleAndTurnsBackToTheStart()
    {
        var rod = new FakeRod(30, 10, 2, 80);
        var plan = new RotaryScanPlan(0, 360, 24, [100, 110, 130], 110, "pointer");
        var left = new List<double>();
        var scan = await RotaryScanner.RunAsync(plan, rod, null, left.Add);
        Assert.Equal(24, rod.Measured);
        Assert.Equal(0, rod.At % 360, 6);
        // The shorter way back on a whole turn: on round by one step.
        Assert.Equal(15, rod.Turns[^1], 6);
        Assert.Equal(0, left[^1]);
        Assert.Equal(24 * 2, scan.Read);
        Assert.True(scan.WholeTurn);

        var fit = scan.FitRound(0)!.Value;
        Assert.Equal(80, fit.HighDeg, 0);
        Assert.Equal(2, fit.Offset, 1);
        Assert.Equal(4, fit.Swing, 1);
        Assert.Null(scan.FitRound(2)); // the unread place
        Assert.Equal(75, scan.Highest()!.Value.Deg, 6); // the stop nearest 80°

        // The axis back from a rod of known diameter.
        Assert.Equal(30, scan.AxisFromRod(20)!.Value, 1);
    }

    [Fact]
    public async Task APartialSpanTurnsBackTheWayItCame()
    {
        var rod = new FakeRod(30, 10, 0, 0);
        var scan = await RotaryScanner.RunAsync(new RotaryScanPlan(-30, 60, 4, [110], 110, "autofocus"), rod, null, _ => { });
        Assert.Equal([-30, 30, 30, 30, -60], rod.Turns);
        Assert.Equal(0, rod.At, 6);
        Assert.False(scan.WholeTurn);
    }

    [Fact]
    public async Task StoppingLeavesThePartWhereItIsAndKeepsWhatWasRead()
    {
        var rod = new FakeRod(30, 10, 1, 0, cancelAfter: 3);
        var plan = new RotaryScanPlan(0, 360, 12, [110], 110, "pointer");
        var e = await Assert.ThrowsAsync<RotaryScanStopped>(() => RotaryScanner.RunAsync(plan, rod, null, _ => { }));
        Assert.True(e.Cancelled);
        Assert.Equal(3, e.SoFar.AnglesDeg.Length);
        Assert.Equal(90, e.LeftDeg, 6);
        Assert.Equal(90, rod.At, 6); // not turned back
    }

    [Fact]
    public void TheHeadStepsUpThroughOneSweep()
    {
        // First angle, nothing known: from well below the focused top to just above it, lowest first.
        var first = RotaryScanner.FocusSweep([null, null], 100, 8, 0, 150);
        Assert.Equal(first.Order(), first);
        Assert.True(first[0] - 8 <= 100 - 40 + 0.01, $"starts at {first[0]}");
        Assert.True(first[^1] + 8 >= 108 - 0.01, $"ends at {first[^1]}");
        for (int i = 1; i < first.Count; i++) Assert.InRange(first[i] - first[i - 1], 8, 16); // overlapping reaches
        // Later angles: only round what was read, in one height if it fits.
        Assert.Equal([94], RotaryScanner.FocusSweep([90, 92, null], 100, 8, 0, 150));
        var wide = RotaryScanner.FocusSweep([80, 120], 100, 8, 0, 150);
        Assert.Equal(wide.Order(), wide);
        Assert.True(wide[0] - 8 <= 76 && wide[^1] + 8 >= 124);
        // Never more than asked, and within the head's travel.
        var capped = RotaryScanner.FocusSweep([0, 150], 75, 8, 0, 150, maxLevels: 4);
        Assert.Equal(4, capped.Count);
        Assert.All(capped, l => Assert.InRange(l, 0, 150));
        Assert.Equal([5], RotaryScanner.FocusSweep([1], 1, 8, 0, 150)); // near the bottom of the travel
    }

    /// <summary>Runs posted work on the thread pool, as itself: stands in for the UI thread's dispatcher.</summary>
    sealed class PumpContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => { SetSynchronizationContext(this); d(state); });
    }

    sealed class ContextCheckingRig(SynchronizationContext ui) : IRotaryScanRig
    {
        public int Wrong;
        public async Task TurnAsync(double degrees, CancellationToken ct)
        {
            if (SynchronizationContext.Current != ui) Wrong++;
            await Task.Delay(1, ct);
        }

        public async Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct)
        {
            if (SynchronizationContext.Current != ui) Wrong++;
            await Task.Delay(1, ct);
            return [.. xs.Select(_ => (double?)10)];
        }
    }

    [Fact]
    public async Task AScanStaysOnTheCallersThread()
    {
        var ui = new PumpContext();
        var rig = new ContextCheckingRig(ui);
        var done = new TaskCompletionSource();
        ui.Post(async _ =>
        {
            try
            {
                await RotaryScanner.RunAsync(new RotaryScanPlan(0, 360, 6, [110], 110, "pointer"), rig, null, _ => { });
                done.SetResult();
            }
            catch (Exception e) { done.SetException(e); }
        }, null);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, rig.Wrong);
    }

    [Fact]
    public void GapsAreFilledRoundThePartThenAlongIt()
    {
        var scan = new RotaryScan
        {
            AnglesDeg = [0, 90, 180, 270],
            Xs = [0, 10, 20],
            Heights =
            [
                [12, null, 16],
                [null, null, 18],
                [14, null, 20],
                [null, null, 22],
            ],
        };
        var r = scan.Radii(10);
        Assert.Equal(2, r[0][0]);
        Assert.Equal(3, r[1][0]!.Value, 6); // between 0° and 180°
        Assert.Equal(3, r[3][0]!.Value, 6); // between 180° and 0° round the back
        Assert.Equal(4, r[0][1]!.Value, 6); // along it: between 2 and 6
        Assert.All(r, row => Assert.All(row, v => Assert.NotNull(v)));
        var csv = scan.ToCsv(10);
        Assert.Contains("angle,0,10,20", csv);
        Assert.Contains("90,3,", csv);
    }

    [Fact]
    public void TheStockModelIsAClosedSolid()
    {
        var scan = new RotaryScan
        {
            AnglesDeg = [0, 90, 180, 270],
            Xs = [0, 10, 20],
            Heights = [.. Enumerable.Range(0, 4).Select(_ => new double?[] { 15, 15, 15 })],
        };
        var stl = scan.ToStl(10)!;
        int tris = 4 * 2 * 2 + 2 * 4;
        Assert.Equal(84 + 50 * tris, stl.Length);
        Assert.Equal((uint)tris, BitConverter.ToUInt32(stl, 80));
        // Not a whole turn: no solid.
        Assert.Null((scan with { AnglesDeg = [0, 10, 20, 30] }).ToStl(10));
    }

    [Fact]
    public void AScanRoundTripsThroughJson()
    {
        var scan = new RotaryScan
        {
            AnglesDeg = [0, 120, 240],
            Xs = [100, 110],
            Heights = [[1, null], [2, 3], [null, 4]],
            LineY = 105,
            Method = "pointer",
            When = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero),
        };
        var back = RotaryScan.FromJson(scan.ToJson())!;
        Assert.Equal(scan.AnglesDeg, back.AnglesDeg);
        Assert.Equal(scan.Heights[1], back.Heights[1]);
        Assert.Null(back.Heights[0][1]);
        Assert.Equal(105, back.LineY);
        Assert.Null(RotaryScan.FromJson("not json"));
    }
}
