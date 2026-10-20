using System.Text.Json;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>Where an exact-depth job is up to.</summary>
public enum DepthStep { MeasureSurface, Burn, MeasureDepth, Finished }

/// <summary>One burn of an exact-depth job: how many passes, and the heights the pointer read after it (null = not read yet).</summary>
public sealed record DepthRound(int Passes, IReadOnlyList<double?>? Heights);

/// <summary>
/// Engraving to an exact depth: the surface is measured with the red pointer before burning, the
/// shapes are burned a round of passes at a time, and after each round the pointer measures how
/// deep it has got. The depth each pass really takes off (it slows as the hole deepens) sets the
/// next round, which closes in on the target from above instead of guessing all the passes up front.
/// Points inside the shapes are measured, and a few just outside them: if those move (the board
/// shifted, the head drifted), that is taken off.
/// </summary>
public sealed record DepthRun
{
    public required Guid LayerId { get; init; }
    public required double TargetMm { get; init; }
    public double ToleranceMm { get; init; } = 0.05;
    /// <summary>The guess at how deep one pass goes, before anything is measured (from an etch test).</summary>
    public required double GuessMmPerPass { get; init; }
    public required IReadOnlyList<Vec2> Inside { get; init; }
    public required IReadOnlyList<Vec2> Outside { get; init; }
    /// <summary>Heights before burning: the inside points, then the outside ones.</summary>
    public IReadOnlyList<double?>? Before { get; init; }
    public IReadOnlyList<DepthRound> Rounds { get; init; } = [];

    /// <summary>What to do next.</summary>
    public DepthStep Next =>
        Before is null ? DepthStep.MeasureSurface :
        Rounds.Count > 0 && Rounds[^1].Heights is null ? DepthStep.MeasureDepth :
        Done ? DepthStep.Finished : DepthStep.Burn;

    public int PassesSoFar => Rounds.Where(r => r.Heights is not null).Sum(r => r.Passes);

    /// <summary>The depth at each inside point after a round's heights (the outside points' shift taken off).</summary>
    public IReadOnlyList<double> Depths(IReadOnlyList<double?> after)
    {
        if (Before is null) return [];
        int n = Inside.Count;
        var shifts = Enumerable.Range(n, Outside.Count).Where(i => Before[i] is not null && after[i] is not null).Select(i => Before[i]!.Value - after[i]!.Value).ToList();
        double shift = shifts.Count > 0 ? shifts.Average() : 0;
        return [.. Enumerable.Range(0, n).Where(i => Before[i] is not null && after[i] is not null).Select(i => Before[i]!.Value - after[i]!.Value - shift)];
    }

    /// <summary>The average depth after each measured round, with the passes up to then.</summary>
    public IReadOnlyList<(int Passes, double DepthMm, double Spread)> History
    {
        get
        {
            var o = new List<(int, double, double)>();
            int passes = 0;
            foreach (var r in Rounds)
            {
                if (r.Heights is null) break;
                passes += r.Passes;
                var d = Depths(r.Heights);
                if (d.Count == 0) continue;
                o.Add((passes, d.Average(), d.Max() - d.Min()));
            }
            return o;
        }
    }

    public double DepthMm => History.Count > 0 ? History[^1].DepthMm : 0;

    public bool Done => History.Count > 0 && DepthMm >= TargetMm - ToleranceMm;

    /// <summary>How deep a pass goes now: over the last round (passes slow down as the hole deepens), else the guess.</summary>
    public double RateMmPerPass
    {
        get
        {
            var h = History;
            if (h.Count == 0) return GuessMmPerPass;
            double prevDepth = h.Count > 1 ? h[^2].DepthMm : 0;
            int prevPasses = h.Count > 1 ? h[^2].Passes : 0;
            double rate = (h[^1].DepthMm - prevDepth) / Math.Max(1, h[^1].Passes - prevPasses);
            // A round that took nothing off (char, a bad reading) mustn't make the next one huge.
            return rate > GuessMmPerPass * 0.1 ? rate : GuessMmPerPass * 0.1;
        }
    }

    /// <summary>
    /// Passes for the next round: the first aims at 30% of the target (the guess may be well off,
    /// even half the real rate stays short); later ones aim at 85% of what's left while that's more
    /// than a few passes, then exactly.
    /// </summary>
    public int NextPasses()
    {
        double left = TargetMm - DepthMm;
        if (left <= ToleranceMm) return 0;
        double rate = RateMmPerPass;
        // The first round only finds out how deep a pass really goes: the guess may be far off.
        double n = History.Count == 0 ? TargetMm * 0.3 / rate : left / rate;
        if (History.Count > 0 && n > 4) n *= 0.85;
        return Math.Clamp((int)Math.Floor(History.Count == 0 || n > 4 ? n : Math.Round(n)), 1, 500);
    }

    public DepthRun Measured(IReadOnlyList<double?> heights) =>
        Before is null ? this with { Before = heights } :
        this with { Rounds = [.. Rounds.Take(Rounds.Count - 1), Rounds[^1] with { Heights = heights }] };

    public DepthRun Burning(int passes) => this with { Rounds = [.. Rounds, new DepthRound(passes, null)] };

    /// <summary>All the points to measure: inside, then outside.</summary>
    public IReadOnlyList<Vec2> Points => [.. Inside, .. Outside];

    // ---- where to measure ---------------------------------------------------------------------

    /// <summary>
    /// Up to <paramref name="count"/> points well inside the area (at least <paramref name="clearMm"/>
    /// from any edge, spread as far apart as they can be), and points just outside its box.
    /// </summary>
    public static (List<Vec2> Inside, List<Vec2> Outside) PickPoints(PathGeometry area, int count = 3, double clearMm = 2.5)
    {
        var loops = area.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        var box = area.Bounds();
        var inside = new List<Vec2>();
        if (!box.IsEmpty && loops.Count > 0)
        {
            // Candidates on a fine grid, inside (even-odd) and clear of the edges.
            double step = Math.Max(0.25, Math.Min(box.Width, box.Height) / 40);
            var cands = new List<(Vec2 P, double Clear)>();
            for (double y = box.MinY + step / 2; y < box.MaxY; y += step)
                for (double x = box.MinX + step / 2; x < box.MaxX; x += step)
                {
                    var p = new Vec2(x, y);
                    if (loops.Count(l => Geom.Inside(l, p)) % 2 == 0) continue;
                    double clear = loops.Min(l => DistanceTo(l, p));
                    if (clear >= clearMm) cands.Add((p, clear));
                }
            // The roomiest first, then each next as far as possible from those chosen.
            if (cands.Count > 0)
            {
                inside.Add(cands.MaxBy(c => c.Clear).P);
                while (inside.Count < count)
                {
                    var next = cands.MaxBy(c => inside.Min(q => q.Dist(c.P)));
                    if (inside.Min(q => q.Dist(next.P)) < clearMm) break;
                    inside.Add(next.P);
                }
            }
        }
        var outside = box.IsEmpty ? [] : new List<Vec2>
        {
            new(box.MinX - 4, (box.MinY + box.MaxY) / 2),
            new(box.MaxX + 4, (box.MinY + box.MaxY) / 2),
            new((box.MinX + box.MaxX) / 2, box.MinY - 4),
        };
        return (inside, outside);
    }

    static double DistanceTo(IReadOnlyList<Vec2> loop, Vec2 p)
    {
        double best = double.MaxValue;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            var d = b - a;
            double l2 = d.X * d.X + d.Y * d.Y, t = l2 > 0 ? Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / l2, 0, 1) : 0;
            best = Math.Min(best, p.Dist(a + d * t));
        }
        return best;
    }

    // ---- kept between rounds ------------------------------------------------------------------

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    sealed record Stored(Guid LayerId, double TargetMm, double ToleranceMm, double GuessMmPerPass, double[][] Inside, double[][] Outside, double?[]? Before, StoredRound[] Rounds);
    sealed record StoredRound(int Passes, double?[]? Heights);

    public static string FileFor(string folder, Guid layerId) => Path.Combine(folder, $"depth-{layerId:N}.json");

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var s = new Stored(LayerId, TargetMm, ToleranceMm, GuessMmPerPass, [.. Inside.Select(p => new[] { p.X, p.Y })], [.. Outside.Select(p => new[] { p.X, p.Y })],
            Before?.ToArray(), [.. Rounds.Select(r => new StoredRound(r.Passes, r.Heights?.ToArray()))]);
        File.WriteAllText(FileFor(folder, LayerId), JsonSerializer.Serialize(s, Json));
    }

    /// <summary>The run kept for this layer, or null.</summary>
    public static DepthRun? Load(string folder, Guid layerId)
    {
        try
        {
            var path = FileFor(folder, layerId);
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json);
            if (s is null) return null;
            return new DepthRun
            {
                LayerId = s.LayerId, TargetMm = s.TargetMm, ToleranceMm = s.ToleranceMm, GuessMmPerPass = s.GuessMmPerPass,
                Inside = [.. s.Inside.Select(p => new Vec2(p[0], p[1]))], Outside = [.. s.Outside.Select(p => new Vec2(p[0], p[1]))],
                Before = s.Before, Rounds = [.. s.Rounds.Select(r => new DepthRound(r.Passes, r.Heights))],
            };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or IndexOutOfRangeException) { return null; }
    }

    public const string LayerPrefix = "Exact depth · ";
}
