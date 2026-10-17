using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// Where a running job is, worked out from how long it has been running (the machine only
/// reports elapsed time): the steps already marked, the one being marked now, and the laser's
/// position on it, or for fills and pictures the line being scanned.
/// </summary>
/// <param name="Done">Steps marked at least once.</param>
/// <param name="Current">Steps being marked now (empty during a pause between rounds, or when finished).</param>
/// <param name="Head">The laser's position, on a vector path.</param>
/// <param name="Sweep">The line being scanned, on a fill or picture.</param>
/// <param name="Fraction">How far through the whole job (0–1).</param>
public sealed record RunMarker(IReadOnlySet<int> Done, IReadOnlyList<int> Current, Vec2? Head, (Vec2 A, Vec2 B)? Sweep, double Fraction, bool Pausing);

public static class RunTracker
{
    /// <summary>The job's state <paramref name="seconds"/> into the run (by the job's own time estimate).</summary>
    public static RunMarker At(BuiltJob job, double seconds)
    {
        var spans = job.Timeline;
        double total = spans.Count == 0 ? 0 : spans[^1].End;
        double t = Math.Clamp(seconds, 0, total);
        var done = new HashSet<int>();
        foreach (var s in spans)
        {
            if (s.End > t) break;
            for (int k = 0; k < s.StepCount; k++) done.Add(s.FirstStep + k);
        }
        if (seconds >= total || spans.Count == 0)
            return new(done, [], null, null, 1, false);

        var span = spans.First(s => s.End > t);
        if (span.IsPause) return new(done, [], null, null, t / total, true);
        var current = Enumerable.Range(span.FirstStep, span.StepCount).ToList();
        // Within one pass of this stretch (a stretch can repeat its passes).
        double f = span.PassSeconds > 0 ? (t - span.Start) % span.PassSeconds / span.PassSeconds : 0;
        var steps = current.Select(i => job.Steps[i]).ToList();
        Vec2? head = null;
        (Vec2, Vec2)? sweep = null;
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
                sweep = (Back(new Vec2(b.MinX, y)), Back(new Vec2(b.MaxX, y)));
            }
        }
        return new(done, current, head, sweep, t / total, false);
    }

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
}
