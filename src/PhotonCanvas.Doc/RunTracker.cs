using System.Runtime.CompilerServices;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// Where a running job is, worked out from how long it has been running (the machine only
/// reports elapsed time): the steps already marked, the one being marked now, and the laser's
/// position on it, or for fills and pictures the line being scanned.
/// </summary>
/// <param name="Done">Steps marked at least once.</param>
/// <param name="Current">Steps being marked now (empty during a pause between rounds, or when finished).</param>
/// <param name="Head">The laser's position.</param>
/// <param name="Sweep">The line being scanned, on a fill or picture.</param>
/// <param name="Fraction">How far through the whole job (0–1).</param>
/// <param name="Pausing">Waiting with the laser off: between rounds, the head moving up or down, or after a path (a pause after each path, or a cool-down when spreading the heat).</param>
public sealed record RunMarker(IReadOnlySet<int> Done, IReadOnlyList<int> Current, Vec2? Head, (Vec2 A, Vec2 B)? Sweep, double Fraction, bool Pausing)
{
    /// <summary>Whether the laser is on where <see cref="Head"/> is (off on a jump, a run-up, or a wait).</summary>
    public bool LaserOn { get; init; }

    /// <summary>What the laser has just marked, oldest first: the last moment of its moves, as it ran them.</summary>
    public IReadOnlyList<(Vec2 A, Vec2 B)> Trail { get; init; } = [];

    /// <summary>
    /// True when the position comes from the block's own G-code moves; false while those are still being read
    /// (a big block, read in the background) and the position is a rough one along the shapes.
    /// </summary>
    public bool Exact { get; init; }
}

public static class RunTracker
{
    /// <summary>How long a trail the laser leaves on the screen (s of the run).</summary>
    public const double TrailS = 0.8;

    /// <summary>
    /// The job's state <paramref name="seconds"/> into the run (by the job's own time estimate).
    /// The laser's position follows the G-code of the block being run, read the first time it's asked for:
    /// with <paramref name="wait"/> false a big block is read in the background and a rough position is given
    /// meanwhile (<see cref="RunMarker.Exact"/> false); true waits for it.
    /// </summary>
    public static RunMarker At(BuiltJob job, double seconds, bool wait = false)
    {
        var spans = job.Timeline;
        double total = spans.Count == 0 ? 0 : spans[^1].End;
        double t = Math.Clamp(seconds, 0, total);
        var done = new HashSet<int>();
        int si = 0;
        for (; si < spans.Count && spans[si].End <= t; si++)
            for (int k = 0; k < spans[si].StepCount; k++) done.Add(spans[si].FirstStep + k);
        if (seconds >= total || spans.Count == 0 || si >= spans.Count)
            return new(done, [], null, null, 1, false) { Exact = true };
        // Still starting: nothing marked yet.
        if (t < spans[0].Start) return new(done, [], null, null, t / total, false) { Exact = true };

        var span = spans[si];
        if (span.IsPause)
        {
            // Between rounds, or the head moving up or down: the laser off where the last path ended.
            Vec2? rest = null;
            for (int k = si - 1; k >= 0 && rest is null; k--)
                if (!spans[k].IsPause)
                    rest = spans[k].Block >= 0 && Traces.Get(job, spans[k].Block, spans[k].Pass + spans[k].Passes - 1, false) is { Count: > 0 } prev
                        ? prev.End
                        : EndOf(Enumerable.Range(spans[k].FirstStep, spans[k].StepCount).Select(i => job.Steps[i]).ToList());
            if (si + 1 < spans.Count) Traces.Prefetch(job, spans, si);
            return new(done, [], rest, null, t / total, true) { Exact = true };
        }
        var current = Enumerable.Range(span.FirstStep, span.StepCount).ToList();

        // Which pass of the stretch, and how far into it; a wait at the end of each pass marks nothing.
        double local = t - span.Start;
        int k0 = span.PassSeconds > 0 ? Math.Clamp((int)Math.Floor(local / span.PassSeconds), 0, Math.Max(0, span.Passes - 1)) : 0;
        double within = local - k0 * span.PassSeconds;
        double work = Math.Max(0, span.PassSeconds - span.WaitS);
        bool waiting = span.WaitS > 0 && within >= work;
        double f = work > 0 ? Math.Clamp(within / work, 0, 1) : 1;
        int pass = span.Pass + k0;

        if (waiting)
        {
            // Done with the path, cooling before the next: it shows marked, the laser off where it stopped.
            foreach (int i in current) done.Add(i);
            var rest = span.Block >= 0 ? Traces.Get(job, span.Block, pass, wait) : null;
            Vec2? end = rest is { Count: > 0 } ? rest.End : EndOf(current.Select(i => job.Steps[i]).ToList());
            return new(done, [], end, null, t / total, true) { Exact = rest is not null };
        }

        if (span.Block >= 0 && Traces.Get(job, span.Block, pass, wait) is { Count: > 0 } trace)
        {
            Traces.Prefetch(job, spans, si);
            double tt = f * trace.Seconds;
            var (at, lit, move) = trace.At(tt);
            // Real seconds to the moves' own: the trail is the last moment of the run.
            double per = work > 0 ? trace.Seconds / work : 0;
            var trail = trace.Trail(move, tt, tt - TrailS * per);
            bool scans = job.Job.Blocks[span.Block] is FillBlock or BandedFillBlock or DotBlock;
            var sweep = scans && lit ? trace.LineAround(move) : null;
            return new(done, current, at, sweep, t / total, false) { LaserOn = lit, Trail = trail, Exact = true };
        }

        // Rough, until the block's moves are read: along the paths by length, or across a fill.
        var steps = current.Select(i => job.Steps[i]).ToList();
        Vec2? head = null;
        (Vec2, Vec2)? sw = null;
        if (steps.All(s => !s.Fill))
            head = Along(steps, f);
        else
        {
            // Lines run along the scan direction and step across it: turn the shapes so the scan
            // runs along X, find the line f of the way across, turn it back.
            double rad = steps[0].ScanAngleDeg * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            Vec2 Turn(Vec2 v) => new(v.X * cos + v.Y * sin, -v.X * sin + v.Y * cos);
            Vec2 Back(Vec2 v) => new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
            var b = steps.Aggregate(Rect2.Empty, (a, s) => a.Union(Rect2.Bounds(s.Points.Select(Turn).ToList())));
            if (!b.IsEmpty)
            {
                double y = b.MinY + f * b.Height;
                sw = (Back(new Vec2(b.MinX, y)), Back(new Vec2(b.MaxX, y)));
            }
        }
        return new(done, current, head, sw, t / total, false) { LaserOn = true };
    }

    static Vec2? EndOf(IReadOnlyList<BuiltJob.Step> steps) =>
        steps.LastOrDefault(s => s.Points.Count > 0) is { } last ? last.Closed ? last.Points[0] : last.Points[^1] : null;

    /// <summary>The point <paramref name="f"/> (0–1) of the way along the steps' paths, by length.</summary>
    static Vec2? Along(IReadOnlyList<BuiltJob.Step> steps, double f)
    {
        var pts = new List<List<Vec2>>();
        foreach (var s in steps)
        {
            if (s.Points.Count == 0) continue;
            var p = new List<Vec2>(s.Points);
            if (s.Closed) p.Add(s.Points[0]);
            pts.Add(p);
        }
        double total = pts.Sum(p => Length(p));
        if (pts.Count == 0) return null;
        if (total <= 0) return pts[0][0];
        double want = f * total;
        foreach (var p in pts)
            for (int i = 1; i < p.Count; i++)
            {
                double d = p[i - 1].Dist(p[i]);
                if (want <= d) return d <= 0 ? p[i] : p[i - 1] + (p[i] - p[i - 1]) * (want / d);
                want -= d;
            }
        return pts[^1][^1];
    }

    static double Length(List<Vec2> p)
    {
        double l = 0;
        for (int i = 1; i < p.Count; i++) l += p[i - 1].Dist(p[i]);
        return l;
    }

    /// <summary>
    /// Each job's blocks read into timed moves, a few at a time (the one running and the next): a big
    /// block (a picture, a dense fill, a wobbled cut) is read in the background so the screen doesn't stall.
    /// </summary>
    static class Traces
    {
        /// <summary>Blocks with fewer moves than about this are read on the spot.</summary>
        const long ReadNowMoves = 20_000;
        const int Keep = 4;

        sealed class Cache
        {
            public readonly Dictionary<(int Block, int Pass), Task<MotionTrace?>> Reads = [];
            public readonly Queue<(int, int)> Order = new();
        }

        static readonly ConditionalWeakTable<BuiltJob, Cache> All = [];

        public static MotionTrace? Get(BuiltJob job, int block, int pass, bool wait)
        {
            if (block < 0 || block >= job.Job.Blocks.Count) return null;
            var task = Start(job, block, pass, wait || Size(job.Job.Blocks[block]) < ReadNowMoves);
            if (task.IsCompleted) return task.IsCompletedSuccessfully ? task.Result : null;
            if (!wait) return null;
            try { return task.GetAwaiter().GetResult(); }
            catch (Exception) { return null; }
        }

        /// <summary>Starts reading the next block that marks something, so it's ready when the laser gets there.</summary>
        public static void Prefetch(BuiltJob job, IReadOnlyList<BuiltJob.RunSpan> spans, int at)
        {
            for (int i = at + 1; i < spans.Count && i <= at + 3; i++)
                if (spans[i].Block >= 0)
                {
                    Start(job, spans[i].Block, spans[i].Pass, false);
                    return;
                }
        }

        static Task<MotionTrace?> Start(BuiltJob job, int block, int pass, bool now)
        {
            var cache = All.GetValue(job, _ => new Cache());
            lock (cache)
            {
                // A block that doesn't change from pass to pass reads the same each time.
                var b = job.Job.Blocks[block];
                if (!(b is FillBlock { AngleStepDeg: not 0 } || b is DotBlock { PassBlocks.Count: > 1 })) pass = 1;
                if (cache.Reads.TryGetValue((block, pass), out var have)) return have;
                var shift = b.Laser.Light == LightSource.Red ? job.MopaShift : default;
                var start = StartPoint(job, block);
                MotionTrace? Read()
                {
                    try { return job.Job.BlockTrace(block, pass, start, shift); }
                    catch (Exception) { return null; } // a block that can't be written: the rough position instead
                }
                var task = now ? Task.FromResult(Read()) : Task.Run(Read);
                cache.Reads[(block, pass)] = task;
                cache.Order.Enqueue((block, pass));
                while (cache.Order.Count > Keep) cache.Reads.Remove(cache.Order.Dequeue());
                return task;
            }
        }

        /// <summary>Where the laser starts a block: where the step before it ended (a first jump is quick either way).</summary>
        static Vec2 StartPoint(BuiltJob job, int block)
        {
            var spans = job.Timeline;
            for (int i = 0; i < spans.Count; i++)
                if (spans[i].Block == block && i > 0)
                {
                    for (int k = i - 1; k >= 0; k--)
                        if (spans[k].StepCount > 0 && EndOf(job.Steps.Skip(spans[k].FirstStep).Take(spans[k].StepCount).ToList()) is { } e)
                            return e;
                    break;
                }
            return default;
        }

        /// <summary>About how many moves a block makes.</summary>
        static long Size(Block b) => b switch
        {
            VectorBlock v => v.Path.Points.Count,
            PointsBlock p => p.Points.Count,
            LoopsBlock l => l.Loops.Sum(x => (long)x.Count),
            FillBlock f => Job.FillLines(f).Count * 3L,
            DotBlock d => d.Bitmap.Bits.LongLength,
            BandedVectorBlock bv => bv.Pieces.Sum(p => (long)p.Points.Count),
            _ => long.MaxValue,
        };
    }
}
