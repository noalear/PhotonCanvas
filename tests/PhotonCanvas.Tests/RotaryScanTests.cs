using PhotonCanvas.Core;

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
