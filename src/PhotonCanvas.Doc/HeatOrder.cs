using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>A sheet metal's heat properties (room temperature, handbook values).</summary>
public sealed record SheetMetal(string Name, double ConductivityWmK, double DensityKgM3, double SpecificHeatJkgK)
{
    /// <summary>How fast heat spreads through it (mm²/s): aluminium about 84, stainless about 4.</summary>
    public double DiffusivityMm2S => ConductivityWmK / (DensityKgM3 * SpecificHeatJkgK) * 1e6;

    /// <summary>Heat to warm a cubic millimetre by one degree (J).</summary>
    public double HeatPerMm3K => DensityKgM3 * SpecificHeatJkgK * 1e-9;

    public static readonly IReadOnlyList<SheetMetal> All =
    [
        new("Aluminium", 205, 2700, 900),
        new("Stainless steel", 16, 8000, 500),
        new("Steel", 50, 7850, 490),
        new("Brass", 110, 8500, 380),
        new("Copper", 390, 8960, 385),
        new("Titanium", 22, 4500, 520),
    ];

    public static SheetMetal Find(string? name) => All.FirstOrDefault(m => m.Name == name) ?? All[0];
}

/// <summary>What the heat order needs to know about the sheet and the laser.</summary>
public sealed record HeatSettings
{
    public SheetMetal Sheet { get; init; } = SheetMetal.All[0];
    public double ThicknessMm { get; init; } = 0.2;
    /// <summary>Keep the sheet's warming (averaged over a few mm) under this many degrees above where it started.</summary>
    public double LimitC { get; init; } = 30;
    /// <summary>Paths longer than this are cut in pieces of about this length, at different times (0 = never).</summary>
    public double PieceMm { get; init; } = 15;
    /// <summary>The laser's power as it cuts (W) and its speed (mm/s).</summary>
    public double Watts { get; init; } = 10;
    public double SpeedMmS { get; init; } = 100;
    public int Passes { get; init; } = 1;
    /// <summary>
    /// Share of the laser's power left in the sheet as heat. Cutting blows much of it away with the
    /// melt and metal reflects some: a rough figure, not measured on this machine.
    /// </summary>
    public double HeatShare { get; init; } = 0.3;
    /// <summary>Galvo jump and laser switching between paths (s).</summary>
    public double JumpS { get; init; } = 0.002;
}

/// <summary>One cut of the heat order: a piece of a path, one of its passes, after a wait (s).</summary>
public sealed record HeatCut(List<Vec2> Points, bool Closed, int Path, int Pass, double WaitBeforeS);

/// <summary>The order worked out: the cuts, the warmest the sheet got (°C over the start), and the waits added.</summary>
public sealed record HeatPlan(IReadOnlyList<HeatCut> Cuts, double PeakC, double WaitS, double OneCutC, double CellMm);

/// <summary>
/// A cut order that keeps thin sheet cool. The sheet's temperature is simulated as the job runs (a
/// thin plate: heat from each cut spreads at the metal's diffusivity and leaves slowly into the air
/// from both faces; holes already cut out no longer carry or hold heat). The work is cut from the
/// middle outwards, while the metal round it is still whole to carry the heat away: of the cuts in
/// the innermost band, the innermost one cool enough goes next, and when none is, the job waits
/// just long enough. Every pass of every path is scheduled on its own, so repeated passes wait for
/// their own heat too, and long paths are cut in short pieces at different times. Holes and insides
/// are finished before the last pass of the outline around them, so parts stay held.
/// </summary>
public static class HeatOrder
{
    /// <summary>Pieces of a path overlap this much (mm) so the cut is continuous where they meet.</summary>
    const double Overlap = 0.05;
    /// <summary>Heat lost to still air from each face (W/mm²·K): about 15 W/m²·K.</summary>
    const double AirLoss = 15e-6;
    /// <summary>No single wait longer than this (s).</summary>
    const double MaxWait = 30;
    /// <summary>How far out from the innermost cut still to be done the next one may be taken (mm, at least a few cells).</summary>
    const double Band = 10;

    /// <summary>
    /// Plan the cuts. <paramref name="paths"/>: each path's outline (for which lies inside which) and
    /// the pieces it's cut as (tabs and lead-ins already applied).
    /// </summary>
    public static HeatPlan Plan(IReadOnlyList<(FlatPath Outline, IReadOnlyList<FlatPath> Pieces)> paths, HeatSettings h)
    {
        int passes = Math.Max(1, h.Passes);
        // The pieces, long ones split.
        var pieces = new List<(List<Vec2> Points, bool Closed, int Path, double Length)>();
        for (int i = 0; i < paths.Count; i++)
            foreach (var p in paths[i].Pieces)
                foreach (var (pts, closed) in Split(p, h.PieceMm))
                    if (pts.Count >= 2) pieces.Add((pts, closed, i, Length(pts)));
        if (pieces.Count == 0) return new([], 0, 0, 0, 0);

        // Which paths lie inside which: an outline's last pass waits for everything inside it.
        var containers = Containers(paths.Select(p => p.Outline).ToList());
        var piecesOf = new int[paths.Count];
        foreach (var p in pieces) piecesOf[p.Path]++;
        var blockers = new int[paths.Count];
        for (int j = 0; j < paths.Count; j++)
            foreach (int c in containers[j]) blockers[c] += piecesOf[j] * passes;

        // The sheet: the work plus a margin, cells sized so the simulation stays quick.
        var box = Rect2.Empty;
        foreach (var p in pieces) box = box.Union(Rect2.Bounds(p.Points));
        box = box.Inflate(15);
        double alpha = h.Sheet.DiffusivityMm2S;
        double cutS = pieces.Sum(p => p.Length) / h.SpeedMmS * passes;
        double jobS = cutS + pieces.Count * passes * h.JumpS;
        double dx = Math.Pow(4 * alpha * Math.Max(jobS, 1) * box.Width * box.Height / 2e8, 0.25);
        // Not finer than about a millimetre in fast-spreading metals: finer cells need much shorter steps
        // (a time step goes with the cell squared), and warping is read over a few mm anyway.
        dx = Math.Clamp(dx, Math.Max(0.5, Math.Sqrt(alpha * 0.012)), 6);
        dx = Math.Max(dx, Math.Max(box.Width, box.Height) / 220);
        var sim = new Sheet(box, dx, h);

        // Each piece's heat as it's cut, spread over the cells it crosses.
        var deposits = pieces.Select(p => sim.Deposit(p.Points, h.HeatShare * h.Watts * p.Length / h.SpeedMmS)).ToList();
        var own = deposits.Select(sim.OwnRise).ToArray();
        // Middle outwards: the pieces by distance from the middle of the work.
        var middle = box.Center;
        var byRadius = Enumerable.Range(0, pieces.Count).OrderBy(i => Rect2.Bounds(pieces[i].Points).Center.Dist(middle)).ToArray();
        var radius = byRadius.Select(i => Rect2.Bounds(pieces[i].Points).Center.Dist(middle)).ToArray();
        double band = Math.Max(Band, 4 * dx);
        var nextPass = new int[pieces.Count];
        Array.Fill(nextPass, 1);
        var unitsLeft = new int[paths.Count];
        for (int j = 0; j < paths.Count; j++) unitsLeft[j] = piecesOf[j] * passes;

        bool Ready(int i) => nextPass[i] <= passes && (nextPass[i] < passes || blockers[pieces[i].Path] == 0);

        var cuts = new List<HeatCut>(pieces.Count * passes);
        double peak = 0, waited = 0;
        bool released = false;
        int left = pieces.Count * passes, first = 0;
        while (left > 0)
        {
            while (first < byRadius.Length && nextPass[byRadius[first]] > passes) first++;
            // The innermost cut ready to go, and the band beyond it.
            int k0 = first;
            while (k0 < byRadius.Length && !(released ? nextPass[byRadius[k0]] <= passes : Ready(byRadius[k0]))) k0++;
            if (k0 == byRadius.Length)
            {
                // Nothing ready (can't happen with nesting, but never hang): drop the inside-first rule.
                released = true;
                continue;
            }
            // The innermost one in the band that's cool enough; if none is, the coolest of them (and wait).
            int best = -1, coolest = -1;
            double coolestPred = double.PositiveInfinity;
            for (int k = k0; k < byRadius.Length && radius[k] <= radius[k0] + band; k++)
            {
                int i = byRadius[k];
                if (released ? nextPass[i] > passes : !Ready(i)) continue;
                double pred = sim.LocalRise(deposits[i]) + sim.OwnRise(deposits[i]);
                if (pred <= h.LimitC) { best = i; break; }
                if (pred < coolestPred) { coolestPred = pred; coolest = i; }
            }
            if (best < 0) best = coolest;

            // Too warm here: wait for this spot to cool enough.
            double wait = 0, ownBest = sim.OwnRise(deposits[best]);
            double pred0 = sim.LocalRise(deposits[best]) + ownBest;
            if (pred0 > h.LimitC)
            {
                double step = Math.Max(0.05, sim.Dt);
                while (wait < MaxWait)
                {
                    double local = sim.LocalRise(deposits[best]);
                    if (local + ownBest <= h.LimitC || local <= 0.1 * h.LimitC) break;
                    sim.Advance(step);
                    wait += step;
                }
                waited += wait;
            }
            peak = Math.Max(peak, sim.LocalRise(deposits[best]) + ownBest);

            var piece = pieces[best];
            cuts.Add(new(piece.Points, piece.Closed, piece.Path, nextPass[best], Math.Round(wait, 3)));
            sim.Add(deposits[best]);
            sim.Advance(piece.Length / h.SpeedMmS + h.JumpS);
            nextPass[best]++;
            foreach (int c in containers[piece.Path]) blockers[c]--;
            // Cut right round: what's inside is gone from the sheet (a hole, or a part now loose).
            if (--unitsLeft[piece.Path] == 0 && paths[piece.Path].Outline is { Closed: true, Points.Count: >= 3 } outline)
                sim.RemoveInside(outline.Points);
            left--;
        }
        return new(cuts, peak, waited, own.Max(), dx);
    }

    /// <summary>For each path, the closed paths it lies inside.</summary>
    static List<int>[] Containers(List<FlatPath> outlines)
    {
        int n = outlines.Count;
        var boxes = outlines.Select(p => p.Points.Count > 0 ? Rect2.Bounds(p.Points) : Rect2.Empty).ToArray();
        var o = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        // Big closed outlines first: only they can hold others.
        var holders = Enumerable.Range(0, n).Where(i => outlines[i].Closed && outlines[i].Points.Count >= 3).ToList();
        for (int j = 0; j < n; j++)
        {
            if (outlines[j].Points.Count == 0) continue;
            var probe = outlines[j].Points[0];
            foreach (int i in holders)
            {
                if (i == j || !boxes[i].Contains(boxes[j])) continue;
                if (boxes[i] == boxes[j] && j < i) continue; // the same outline twice: one holds the other, not both
                if (Geom.Inside(outlines[i].Points, probe)) o[j].Add(i);
            }
        }
        return o;
    }

    /// <summary>A path as pieces of about <paramref name="pieceMm"/> (all of it when short), each overlapping the next a little.</summary>
    static IEnumerable<(List<Vec2> Points, bool Closed)> Split(FlatPath p, double pieceMm)
    {
        var pts = p.Closed && p.Points.Count > 2 && p.Points[0].Dist(p.Points[^1]) > 1e-9 ? [.. p.Points, p.Points[0]] : p.Points;
        double len = Length(pts);
        if (pieceMm <= 0 || len <= pieceMm * 1.5)
        {
            yield return (p.Points, p.Closed);
            yield break;
        }
        int n = (int)Math.Ceiling(len / pieceMm);
        double step = len / n;
        for (int k = 0; k < n; k++)
            yield return (Sub(pts, k * step, Math.Min(len, (k + 1) * step + Overlap)), false);
    }

    static double Length(IReadOnlyList<Vec2> pts)
    {
        double l = 0;
        for (int i = 1; i < pts.Count; i++) l += pts[i - 1].Dist(pts[i]);
        return l;
    }

    /// <summary>The stretch of a polyline from <paramref name="a"/> to <paramref name="b"/> mm along it.</summary>
    static List<Vec2> Sub(List<Vec2> pts, double a, double b)
    {
        var o = new List<Vec2>();
        double at = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i - 1].Dist(pts[i]);
            double s0 = at, s1 = at + seg;
            at = s1;
            if (s1 < a || seg <= 0) continue;
            if (o.Count == 0) o.Add(Vec2.Lerp(pts[i - 1], pts[i], Math.Clamp((a - s0) / seg, 0, 1)));
            if (s1 >= b)
            {
                o.Add(Vec2.Lerp(pts[i - 1], pts[i], Math.Clamp((b - s0) / seg, 0, 1)));
                return o;
            }
            o.Add(pts[i]);
        }
        return o;
    }

    /// <summary>
    /// The sheet's warming on a grid (°C over the start): explicit diffusion with insulated edges,
    /// losses to the air from both faces. Each cell keeps how much of it is still metal: holes cut
    /// out take their share of the heat it holds and of what it passes on to its neighbours, so heat
    /// near cut-out holes leaves more slowly. Readings are averaged over 3 × 3 cells: warping comes
    /// from a patch expanding, not from the kerf's own spot.
    /// </summary>
    sealed class Sheet
    {
        readonly int _nx, _ny;
        readonly double _x0, _y0, _dx, _r, _decay, _cellHeat;
        double[] _t, _next;
        readonly double[] _metal;
        double _carry;
        public double Dt { get; }

        public Sheet(Rect2 box, double dx, HeatSettings h)
        {
            _dx = dx;
            _x0 = box.MinX;
            _y0 = box.MinY;
            _nx = Math.Max(3, (int)Math.Ceiling(box.Width / dx) + 1);
            _ny = Math.Max(3, (int)Math.Ceiling(box.Height / dx) + 1);
            _t = new double[_nx * _ny];
            _next = new double[_nx * _ny];
            _metal = new double[_nx * _ny];
            Array.Fill(_metal, 1.0);
            double alpha = h.Sheet.DiffusivityMm2S;
            Dt = 0.24 * dx * dx / alpha;
            _r = alpha * Dt / (dx * dx);
            double perArea = h.Sheet.HeatPerMm3K * Math.Max(0.01, h.ThicknessMm);
            _decay = Math.Exp(-2 * AirLoss / perArea * Dt);
            _cellHeat = perArea * dx * dx;
        }

        int Cell(Vec2 p) =>
            Math.Clamp((int)Math.Round((p.Y - _y0) / _dx), 0, _ny - 1) * _nx + Math.Clamp((int)Math.Round((p.X - _x0) / _dx), 0, _nx - 1);

        /// <summary>A cut's heat (J) spread along it, as the warming of each cell it crosses.</summary>
        public (int[] Cells, double[] Rise) Deposit(List<Vec2> pts, double joules)
        {
            var samples = new List<int>();
            for (int i = 1; i < pts.Count; i++)
            {
                double seg = pts[i - 1].Dist(pts[i]);
                int n = Math.Max(1, (int)Math.Ceiling(seg / (_dx / 2)));
                for (int k = 0; k < n; k++) samples.Add(Cell(Vec2.Lerp(pts[i - 1], pts[i], (k + 0.5) / n)));
            }
            if (samples.Count == 0) samples.Add(Cell(pts[0]));
            var d = samples.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count() * joules / samples.Count / _cellHeat);
            return (d.Keys.ToArray(), d.Values.ToArray());
        }

        double Mean3(Func<int, double> at, int c)
        {
            int x = c % _nx, y = c / _nx;
            double s = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    s += at(Math.Clamp(y + j, 0, _ny - 1) * _nx + Math.Clamp(x + i, 0, _nx - 1));
            return s / 9;
        }

        /// <summary>How warm the sheet is where a cut goes (the warmest patch along it).</summary>
        public double LocalRise((int[] Cells, double[] Rise) d)
        {
            double m = 0;
            foreach (int c in d.Cells) m = Math.Max(m, Mean3(i => _t[i], c));
            return m;
        }

        /// <summary>How much a cut warms the sheet by itself (more where holes have taken metal away).</summary>
        public double OwnRise((int[] Cells, double[] Rise) d)
        {
            var map = new Dictionary<int, double>();
            for (int k = 0; k < d.Cells.Length; k++) map[d.Cells[k]] = d.Rise[k] / Math.Max(0.25, _metal[d.Cells[k]]);
            double m = 0;
            foreach (int c in d.Cells) m = Math.Max(m, Mean3(i => map.GetValueOrDefault(i), c));
            return m;
        }

        /// <summary>A cut's heat into the sheet: less metal in a cell, the more it warms.</summary>
        public void Add((int[] Cells, double[] Rise) d)
        {
            for (int k = 0; k < d.Cells.Length; k++) _t[d.Cells[k]] += d.Rise[k] / Math.Max(0.25, _metal[d.Cells[k]]);
        }

        /// <summary>Everything inside a cut-through outline leaves the sheet (sampled 4 × 4 per cell).</summary>
        public void RemoveInside(List<Vec2> outline)
        {
            var b = Rect2.Bounds(outline);
            int x0 = Math.Max(0, (int)Math.Floor((b.MinX - _x0) / _dx)), x1 = Math.Min(_nx - 1, (int)Math.Ceiling((b.MaxX - _x0) / _dx));
            int y0 = Math.Max(0, (int)Math.Floor((b.MinY - _y0) / _dx)), y1 = Math.Min(_ny - 1, (int)Math.Ceiling((b.MaxY - _y0) / _dx));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int inside = 0;
                    for (int j = 0; j < 4; j++)
                        for (int i = 0; i < 4; i++)
                            if (Geom.Inside(outline, new Vec2(_x0 + (x - 0.5 + (i + 0.5) / 4) * _dx, _y0 + (y - 0.5 + (j + 0.5) / 4) * _dx))) inside++;
                    int c = y * _nx + x;
                    _metal[c] = Math.Max(0, _metal[c] - inside / 16.0);
                }
        }

        /// <summary>Let the heat spread and leave for <paramref name="seconds"/>.</summary>
        public void Advance(double seconds)
        {
            _carry += seconds;
            int steps = (int)(_carry / Dt);
            _carry -= steps * Dt;
            // Big sheets: the rows shared out between the processor's cores, a band each.
            int bands = _nx * _ny >= 4096 ? Math.Min(Environment.ProcessorCount, _ny / 8) : 1;
            for (int s = 0; s < steps; s++)
            {
                if (bands > 1) Parallel.For(0, bands, b => Rows(b * _ny / bands, (b + 1) * _ny / bands));
                else Rows(0, _ny);
                (_t, _next) = (_next, _t);
            }
        }

        void Rows(int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int up = (y > 0 ? y - 1 : 1) * _nx, down = (y < _ny - 1 ? y + 1 : _ny - 2) * _nx, row = y * _nx;
                for (int x = 0; x < _nx; x++)
                {
                    int left = x > 0 ? x - 1 : 1, right = x < _nx - 1 ? x + 1 : _nx - 2;
                    int i = row + x;
                    double m = _metal[i], c = _t[i];
                    if (m < 0.02) { _next[i] = 0; continue; }
                    // Heat passes between neighbours through the metal both still have.
                    double flow = Math.Min(m, _metal[row + left]) * (_t[row + left] - c) + Math.Min(m, _metal[row + right]) * (_t[row + right] - c)
                                + Math.Min(m, _metal[up + x]) * (_t[up + x] - c) + Math.Min(m, _metal[down + x]) * (_t[down + x] - c);
                    _next[i] = (c + _r * flow / m) * _decay;
                }
            }
        }
    }
}
