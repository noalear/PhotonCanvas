using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>How the sheet is held while it's marked: what's under it decides how fast its heat leaves.</summary>
public enum SheetBacking
{
    /// <summary>Lying on the honeycomb: only the air takes heat away, from both faces.</summary>
    InAir,
    /// <summary>Taped flat to a metal block: the block draws heat through the tape.</summary>
    TapedToMetal,
    /// <summary>Clamped hard to a thick aluminium block: the block draws heat away fast.</summary>
    ClampedToAluminium,
}

/// <summary>
/// Spread the heat across the whole job: each layer (and each separate shape on an engrave or
/// picture layer) is run where the sheet is coolest, so a hot square doesn't warm its neighbour's
/// starting point. Kept with the design; off by default.
/// </summary>
public sealed record SpreadHeat
{
    public bool On { get; init; }
    /// <summary>The sheet metal's name (one of <see cref="SheetMetal.All"/>).</summary>
    public string Sheet { get; init; } = "Stainless steel";
    public double ThicknessMm { get; init; } = 0.5;
    /// <summary>Keep the sheet's warming under this many degrees above where it started.</summary>
    public double LimitC { get; init; } = 25;
    public SheetBacking Backing { get; init; } = SheetBacking.InAir;
    /// <summary>Wait at least this long after each part, laser off (s). 0 = only the waits the heat needs.</summary>
    public double CooldownS { get; init; }

    public const double MaxCooldownS = 600;

    /// <summary>
    /// Heat leaving the sheet, both faces together (W/mm²·K). Still air takes about 15 W/m²·K from
    /// each face; tape to a metal block passes roughly 300 more and a hard clamp to thick aluminium
    /// roughly 1500. Rough figures from handbook contact conductances, not measured on this machine.
    /// </summary>
    public double LossWmm2K => Backing switch
    {
        SheetBacking.TapedToMetal => 315e-6,
        SheetBacking.ClampedToAluminium => 1515e-6,
        _ => 30e-6,
    };

    public static readonly IReadOnlyList<(SheetBacking Backing, string Name)> Backings =
    [
        (SheetBacking.InAir, "In air on the honeycomb"),
        (SheetBacking.TapedToMetal, "Taped to a metal block"),
        (SheetBacking.ClampedToAluminium, "Clamped to a thick aluminium block"),
    ];
}

/// <summary>One part of the job for <see cref="HeatSpread"/>: where it marks, the heat it leaves (J), how long it runs (s), and whether it's a cut.</summary>
public sealed record HeatUnit(IReadOnlyList<Rect2> Footprint, double Joules, double Seconds, bool Cut);

/// <summary>
/// The order worked out: which part runs at each place, the wait before each (s), the warmest patch
/// any part started on (°C over the start), the waits in all, the most one cut warms the sheet on
/// its own (and which part that is), whether a wait ran into its cap, and the simulation's cell size.
/// </summary>
public sealed record SpreadPlan(int[] Order, double[] WaitBeforeS, double PeakC, double WaitS, double OneUnitC, int Hottest, bool Capped, double CellMm);

/// <summary>
/// Spreads the heat of a whole job over the sheet, a part (a layer, or one shape on an engrave or
/// picture layer) at a time. The sheet's warming is simulated as the job runs, with the same thin
/// plate model as <see cref="HeatOrder"/> (heat leaving faster when the sheet is backed by metal).
/// Next goes the part whose patch of sheet is coolest right now; among parts about as cool, the
/// one farthest from the part just done. The limit is on the patch a part starts on: marking (a
/// colour square) heats its own spot a lot whenever it runs, that's how the colour forms, but it
/// should never start on a spot its neighbours already warmed. When the patch is warmer than the
/// limit, the job waits until it isn't, at most two minutes a time. Cuts count their own warming
/// too, as in the heat order (and when one cut alone is too much, wait until the patch is nearly
/// cold). Parts that overlap keep their order (a fill before its outline, slices in turn), and cuts
/// keep their place before or after everything else, so parts still drop last.
/// </summary>
public static class HeatSpread
{
    /// <summary>
    /// Share of the laser's power left in the sheet as heat while marking (fills, pictures, scores):
    /// metal reflects much of it, the rest goes into the surface. A rough figure, not measured.
    /// </summary>
    public const double MarkShare = 0.7;
    /// <summary>Cutting: as <see cref="HeatSettings.HeatShare"/> (much of it blown away with the melt).</summary>
    public const double CutShare = 0.3;
    /// <summary>No single wait longer than this (s).</summary>
    public const double MaxWait = 120;
    /// <summary>Sheet simulated round the work (mm).</summary>
    const double Margin = 15;

    public static SpreadPlan Plan(IReadOnlyList<HeatUnit> units, SpreadHeat h, Action<double>? progress = null, CancellationToken cancel = default)
    {
        int n = units.Count;
        var box = units.SelectMany(u => u.Footprint).Aggregate(Rect2.Empty, (a, r) => a.Union(r));
        if (n == 0 || box.IsEmpty) return new([.. Enumerable.Range(0, n)], new double[n], 0, 0, 0, 0, false, 0);
        box = box.Inflate(Margin);
        var sheet = SheetMetal.Find(h.Sheet);
        double limit = Math.Max(0.1, h.LimitC), cooldown = Math.Clamp(h.CooldownS, 0, SpreadHeat.MaxCooldownS);
        double alpha = sheet.DiffusivityMm2S;
        // Cells sized so the simulation stays quick, as the heat order sizes them (waits counted roughly).
        double jobS = units.Sum(u => u.Seconds) + n * (cooldown + 20);
        double dx = Math.Pow(4 * alpha * Math.Max(jobS, 1) * box.Width * box.Height / 2e8, 0.25);
        dx = Math.Clamp(dx, Math.Max(0.5, Math.Sqrt(alpha * 0.012)), 8);
        dx = Math.Max(dx, Math.Max(box.Width, box.Height) / 220);
        var sim = new HeatOrder.Sheet(box, dx, new HeatSettings { Sheet = sheet, ThicknessMm = h.ThicknessMm, LimitC = limit }, h.LossWmm2K);

        var deposits = units.Select(u => sim.DepositArea(u.Footprint, u.Joules)).ToArray();
        var centres = units.Select(u => u.Footprint.Aggregate(Rect2.Empty, (a, r) => a.Union(r))).Select(b => b.IsEmpty ? box.Center : b.Center).ToArray();
        double perArea = sheet.HeatPerMm3K * Math.Max(0.01, h.ThicknessMm);
        double tau = perArea / h.LossWmm2K;
        // Marking heats its own spot whatever the order: only a cut's own warming counts against the limit.
        var own = units.Select(u => u.Cut ? OwnRise(u, alpha, perArea, tau) : 0).ToArray();

        // What has to go first: earlier parts it overlaps, and the cuts' place in the run.
        var waitingOn = new int[n];
        var unblocks = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < j; i++)
                if (units[j].Cut || units[i].Cut || Overlap(units[i].Footprint, units[j].Footprint))
                {
                    waitingOn[j]++;
                    unblocks[i].Add(j);
                }

        var order = new int[n];
        var waits = new double[n];
        var done = new bool[n];
        double peak = 0, waited = 0, band = Math.Max(0.5, 0.05 * limit), stepS = Math.Max(0.5, sim.Dt);
        bool capped = false;
        int last = -1;
        for (int k = 0; k < n; k++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke((double)k / n);
            // The coolest patch of the parts free to go, then of those about as cool, the farthest from the last.
            var ready = Enumerable.Range(0, n).Where(i => !done[i] && waitingOn[i] == 0).ToList();
            var local = ready.Select(i => sim.LocalRise(deposits[i])).ToList();
            double coolest = local.Min();
            int pick = -1;
            double far = -1;
            for (int r = 0; r < ready.Count; r++)
            {
                if (local[r] > coolest + band) continue;
                int i = ready[r];
                double d = last < 0 ? 0 : centres[i].Dist(centres[last]);
                if (d > far + 1e-9) { far = d; pick = i; }
            }

            // Too warm there: wait just long enough (nearly cold, when a cut alone is too much).
            double wait = 0, now = sim.LocalRise(deposits[pick]);
            if (k > 0)
            {
                bool TooWarm() => now + own[pick] > limit && now > 0.1 * limit;
                while (TooWarm() && wait < MaxWait)
                {
                    sim.Advance(stepS);
                    wait += stepS;
                    now = sim.LocalRise(deposits[pick]);
                }
                if (TooWarm()) capped = true;
                if (wait < cooldown)
                {
                    sim.Advance(cooldown - wait);
                    wait = cooldown;
                    now = sim.LocalRise(deposits[pick]);
                }
            }
            peak = Math.Max(peak, now + own[pick]);
            waits[k] = Math.Round(wait, 3);
            waited += waits[k];
            order[k] = pick;

            // Its heat goes in as it runs, a second or so at a time, so the sheet spreads and loses it meanwhile.
            double t = Math.Max(0, units[pick].Seconds);
            int chunks = Math.Clamp((int)Math.Ceiling(t), 1, 30);
            var (cells, rise) = deposits[pick];
            var part = (cells, rise.Select(x => x / chunks).ToArray());
            for (int c = 0; c < chunks; c++)
            {
                sim.Add(part);
                sim.Advance(t / chunks);
            }
            done[pick] = true;
            foreach (int j in unblocks[pick]) waitingOn[j]--;
            last = pick;
        }
        int hottest = Enumerable.Range(0, n).MaxBy(i => own[i]);
        return new(order, waits, peak, waited, own[hottest], hottest, capped, dx);
    }

    /// <summary>
    /// How much a part warms its own patch, roughly: its heat over its area grown by how far heat
    /// spreads while it runs, less what leaves the faces meanwhile.
    /// </summary>
    static double OwnRise(HeatUnit u, double alpha, double perArea, double tau)
    {
        var b = u.Footprint.Aggregate(Rect2.Empty, (a, r) => a.Union(r));
        if (b.IsEmpty || u.Joules <= 0) return 0;
        double t = Math.Max(1e-3, u.Seconds);
        double spread = Math.Sqrt(alpha * t);
        double area = Math.Max(1, (b.Width + spread) * (b.Height + spread));
        double kept = tau / t * (1 - Math.Exp(-t / tau));
        return u.Joules / (perArea * area) * kept;
    }

    /// <summary>Whether two parts mark any of the same sheet (by the boxes of their shapes; touching edges don't count).</summary>
    static bool Overlap(IReadOnlyList<Rect2> a, IReadOnlyList<Rect2> b)
    {
        foreach (var p in a)
            foreach (var q in b)
                if (Math.Min(p.MaxX, q.MaxX) - Math.Max(p.MinX, q.MinX) > 0.01 && Math.Min(p.MaxY, q.MaxY) - Math.Max(p.MinY, q.MinY) > 0.01)
                    return true;
        return false;
    }
}
