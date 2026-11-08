using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PhotonCanvas.Core;

/// <summary>
/// A part in the rotary, measured as it turns: the height of its top (above the bed, the way a focus
/// height is given) along the laser line, at each angle it was turned to. From that comes a model of the
/// stock: how far the surface is from the rotary's axis all the way round and along it.
/// <para>
/// Angles are how far the part had been turned from where the scan started, positive the way a design runs
/// down the page (the way a rotary job turns it). The point measured at angle θ is the one that comes to the
/// top after turning θ from the start, so it is also where design Y sits θ/360 of the way round from the
/// start line.
/// </para>
/// </summary>
public sealed record RotaryScan
{
    /// <summary>Angle turned from the start for each row of <see cref="Heights"/> (degrees, increasing).</summary>
    public double[] AnglesDeg { get; init; } = [];

    /// <summary>Places along the rotary's axis (the field's X, mm) for each column of <see cref="Heights"/>.</summary>
    public double[] Xs { get; init; } = [];

    /// <summary>Top height above the bed (mm) by [angle][place]; null where it couldn't be read.</summary>
    public double?[][] Heights { get; init; } = [];

    /// <summary>The laser line it was measured along (mm from the back of the field).</summary>
    public double LineY { get; init; }

    /// <summary>How it was measured ("pointer" or "autofocus"), or "standard" for an ideal standard stock fitted to a scan.</summary>
    public string Method { get; init; } = "";

    /// <summary>For a standard stock (<see cref="Method"/> "standard"): what it is ("2" × 2" square bar"). Null for a measured scan.</summary>
    public string? Stock { get; init; }

    /// <summary>
    /// The rotary's axis height (mm above the bed) this was saved with as a stock model, or null (a plain scan, or not
    /// known). The axis isn't part of a measurement: it's a setting of the rotary.
    /// </summary>
    public double? AxisHeightMm { get; init; }

    public DateTimeOffset When { get; init; }

    /// <summary>The scan covered a whole turn (so the model closes up round the back).</summary>
    public bool WholeTurn => AnglesDeg.Length >= 3 && AnglesDeg[^1] - AnglesDeg[0] + Step >= 359.9;

    /// <summary>Degrees between rows (0 with fewer than two).</summary>
    public double Step => AnglesDeg.Length < 2 ? 0 : (AnglesDeg[^1] - AnglesDeg[0]) / (AnglesDeg.Length - 1);

    public int Read => Heights.Sum(r => r.Count(h => h is not null));
    public int Total => AnglesDeg.Length * Xs.Length;

    /// <summary>The heights read at place <paramref name="xi"/>, with their angles.</summary>
    public List<(double Deg, double H)> Column(int xi)
    {
        var o = new List<(double, double)>();
        for (int a = 0; a < AnglesDeg.Length; a++)
            if (xi < Heights[a].Length && Heights[a][xi] is { } h) o.Add((AnglesDeg[a], h));
        return o;
    }

    /// <summary>The highest point read: its angle, place and height (null if nothing was read).</summary>
    public (double Deg, double X, double H)? Highest()
    {
        (double, double, double)? best = null;
        for (int a = 0; a < AnglesDeg.Length; a++)
            for (int x = 0; x < Xs.Length && x < Heights[a].Length; x++)
                if (Heights[a][x] is { } h && (best is null || h > best.Value.Item3)) best = (AnglesDeg[a], Xs[x], h);
        return best;
    }

    /// <summary>The lowest point read.</summary>
    public (double Deg, double X, double H)? Lowest()
    {
        (double, double, double)? best = null;
        for (int a = 0; a < AnglesDeg.Length; a++)
            for (int x = 0; x < Xs.Length && x < Heights[a].Length; x++)
                if (Heights[a][x] is { } h && (best is null || h < best.Value.Item3)) best = (AnglesDeg[a], Xs[x], h);
        return best;
    }

    /// <summary>
    /// How a place along the part swings as it turns, as if it were round but held off-centre: the top height
    /// h(θ) ≈ <c>Mean</c> + <c>Offset</c>·cos(θ − <c>HighDeg</c>), least squares over a whole turn. Offset is
    /// how far off the axis the part sits there, HighDeg the angle at which that side comes to the top.
    /// Null with fewer than three readings, or readings that don't go round far enough to tell.
    /// </summary>
    public RoundFit? FitRound(int xi)
    {
        var col = Column(xi);
        if (col.Count < 3) return null;
        // Normal equations for h = a + b cos θ + c sin θ.
        var m = new double[3, 3];
        var v = new double[3];
        foreach (var (deg, h) in col)
        {
            double t = deg * Math.PI / 180;
            double[] f = [1, Math.Cos(t), Math.Sin(t)];
            for (int r = 0; r < 3; r++)
            {
                v[r] += f[r] * h;
                for (int k = 0; k < 3; k++) m[r, k] += f[r] * f[k];
            }
        }
        if (Solve3(m, v) is not { } s) return null;
        double offset = Math.Sqrt(s[1] * s[1] + s[2] * s[2]);
        double high = (Math.Atan2(s[2], s[1]) * 180 / Math.PI + 360) % 360;
        double rms = Math.Sqrt(col.Average(p =>
        {
            double t = p.Deg * Math.PI / 180, e = p.H - (s[0] + s[1] * Math.Cos(t) + s[2] * Math.Sin(t));
            return e * e;
        }));
        return new RoundFit(s[0], offset, high, col.Max(p => p.H) - col.Min(p => p.H), rms);
    }

    /// <summary>
    /// The axis height, if this was a round rod <paramref name="diameterMm"/> across: the average top height
    /// is the axis plus the radius (less a hair for a rod held off-centre). Null if too little was read.
    /// </summary>
    public double? AxisFromRod(double diameterMm)
    {
        if (!(diameterMm > 0)) return null;
        double r = diameterMm / 2;
        var fits = Enumerable.Range(0, Xs.Length).Select(FitRound).OfType<RoundFit>().ToList();
        if (fits.Count == 0) return null;
        // Top of a circle radius r whose centre is e off the axis, measured straight down the axis: averages
        // to axis + r − e²/(4r) over a turn.
        return fits.Average(f => f.Mean - r + f.Offset * f.Offset / (4 * r));
    }

    /// <summary>
    /// The stock model: distance from the axis (mm) by [angle][place], for a rotary whose axis is
    /// <paramref name="axisHeightMm"/> above the bed (the same way heights are given). Gaps are filled from the
    /// readings either side (round the part first, then along it); places with no readings at all stay null.
    /// </summary>
    public double?[][] Radii(double axisHeightMm)
    {
        int na = AnglesDeg.Length, nx = Xs.Length;
        var r = new double?[na][];
        for (int a = 0; a < na; a++)
        {
            r[a] = new double?[nx];
            for (int x = 0; x < nx && x < Heights[a].Length; x++)
                if (Heights[a][x] is { } h) r[a][x] = Math.Max(0, h - axisHeightMm);
        }
        // Round the part: between the nearest readings at the same place (wrapping round on a whole turn).
        bool wrap = WholeTurn;
        for (int x = 0; x < nx; x++)
        {
            var known = Enumerable.Range(0, na).Where(a => r[a][x] is not null).ToList();
            if (known.Count == 0 || known.Count == na) continue;
            for (int a = 0; a < na; a++)
            {
                if (r[a][x] is not null) continue;
                int? before = known.LastOrDefault(k => k < a, -1) is var b && b >= 0 ? b : wrap ? known[^1] : null;
                int? after = known.FirstOrDefault(k => k > a, -1) is var f && f >= 0 ? f : wrap ? known[0] : null;
                if (before is { } b0 && after is { } a0)
                {
                    double span = (a0 - b0 + na) % na, at = (a - b0 + na) % na;
                    if (span == 0) { r[a][x] = r[b0][x]; continue; }
                    double t = at / span;
                    r[a][x] = r[b0][x]!.Value + (r[a0][x]!.Value - r[b0][x]!.Value) * t;
                }
                else r[a][x] = r[(before ?? after)!.Value][x];
            }
        }
        // Along it: places still empty take the nearest place either side.
        for (int a = 0; a < na; a++)
            for (int x = 0; x < nx; x++)
            {
                if (r[a][x] is not null) continue;
                int lo = x - 1, hi = x + 1;
                while (lo >= 0 && r[a][lo] is null) lo--;
                while (hi < nx && r[a][hi] is null) hi++;
                if (lo >= 0 && hi < nx)
                {
                    double t = (Xs[x] - Xs[lo]) / Math.Max(1e-9, Xs[hi] - Xs[lo]);
                    r[a][x] = r[a][lo]!.Value + (r[a][hi]!.Value - r[a][lo]!.Value) * t;
                }
                else if (lo >= 0) r[a][x] = r[a][lo];
                else if (hi < nx) r[a][x] = r[a][hi];
            }
        return r;
    }

    /// <summary>The readings as a spreadsheet (CSV): one row per angle, one column per place along the axis.</summary>
    public string ToCsv(double? axisHeightMm = null)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# Rotary scan, ").Append(When.ToString("yyyy-MM-dd HH:mm", ci)).Append(", ").Append(Method)
          .Append(", laser line ").Append(LineY.ToString("0.#", ci)).Append(" mm from the back");
        if (axisHeightMm is { } ax) sb.Append(", axis ").Append(ax.ToString("0.##", ci)).Append(" mm above the bed");
        sb.AppendLine();
        sb.Append(axisHeightMm is null ? "# Top height above the bed (mm). Rows: angle turned (°); columns: X along the axis (mm)."
            : "# Distance from the axis (mm, gaps filled). Rows: angle turned (°); columns: X along the axis (mm).").AppendLine();
        sb.Append("angle");
        foreach (var x in Xs) sb.Append(',').Append(x.ToString("0.##", ci));
        sb.AppendLine();
        var rows = axisHeightMm is { } a2 ? Radii(a2) : Heights;
        for (int a = 0; a < AnglesDeg.Length; a++)
        {
            sb.Append(AnglesDeg[a].ToString("0.##", ci));
            for (int x = 0; x < Xs.Length; x++)
                sb.Append(',').Append(x < rows[a].Length && rows[a][x] is { } h ? h.ToString("0.###", ci) : "");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// The stock model as a closed solid (binary STL, mm): X along the axis, the axis at Y = Z = 0, Z up at the
    /// start. Null without a whole turn, or with fewer than two places along it.
    /// </summary>
    public byte[]? ToStl(double axisHeightMm)
    {
        if (!WholeTurn || Xs.Length < 2) return null;
        var r = Radii(axisHeightMm);
        if (r.Any(row => row.Any(v => v is null))) return null;
        int na = AnglesDeg.Length, nx = Xs.Length;
        (double X, double Y, double Z) P(int a, int x)
        {
            // Turning θ the way a design runs down the page brings this point to the top (Z): at the start it's θ back.
            double t = AnglesDeg[a % na] * Math.PI / 180, rr = r[a % na][x]!.Value;
            return (Xs[x], -rr * Math.Sin(t), rr * Math.Cos(t));
        }
        var tris = new List<((double, double, double) A, (double, double, double) B, (double, double, double) C)>();
        for (int a = 0; a < na; a++)
            for (int x = 0; x + 1 < nx; x++)
            {
                var p00 = P(a, x); var p01 = P(a, x + 1); var p10 = P(a + 1, x); var p11 = P(a + 1, x + 1);
                tris.Add((p00, p10, p11));
                tris.Add((p00, p11, p01));
            }
        // End caps: fans from the axis.
        foreach (int x in new[] { 0, nx - 1 })
        {
            var c = (Xs[x], 0.0, 0.0);
            for (int a = 0; a < na; a++)
                tris.Add(x == 0 ? (c, P(a + 1, x), P(a, x)) : (c, P(a, x), P(a + 1, x)));
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var header = new byte[80];
        Encoding.ASCII.GetBytes("PhotonCanvas rotary scan stock model").CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)tris.Count);
        foreach (var (A, B, C) in tris)
        {
            var u = (B.Item1 - A.Item1, B.Item2 - A.Item2, B.Item3 - A.Item3);
            var v = (C.Item1 - A.Item1, C.Item2 - A.Item2, C.Item3 - A.Item3);
            double nx2 = u.Item2 * v.Item3 - u.Item3 * v.Item2, ny = u.Item3 * v.Item1 - u.Item1 * v.Item3, nz = u.Item1 * v.Item2 - u.Item2 * v.Item1;
            double len = Math.Sqrt(nx2 * nx2 + ny * ny + nz * nz);
            if (len > 0) { nx2 /= len; ny /= len; nz /= len; }
            w.Write((float)nx2); w.Write((float)ny); w.Write((float)nz);
            foreach (var p in new[] { A, B, C }) { w.Write((float)p.Item1); w.Write((float)p.Item2); w.Write((float)p.Item3); }
            w.Write((ushort)0);
        }
        w.Flush();
        return ms.ToArray();
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static RotaryScan? FromJson(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<RotaryScan>(json, Json);
            return s is null || s.Heights.Length != s.AnglesDeg.Length ? null : s;
        }
        catch (JsonException) { return null; }
    }

    static double[]? Solve3(double[,] a, double[] b)
    {
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        const int n = 3;
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-9) return null;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                x[r] -= f * x[col];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
        }
        return x;
    }
}

/// <summary>One place along a part, fitted as a round part held off-centre (see <see cref="RotaryScan.FitRound"/>).</summary>
/// <param name="Mean">Average top height (mm above the bed).</param>
/// <param name="Offset">How far off the axis it sits (mm).</param>
/// <param name="HighDeg">The angle turned at which its high side is on top (0–360).</param>
/// <param name="Swing">Highest minus lowest reading (mm): how much the top rises and falls as it turns.</param>
/// <param name="RmsMm">How far the readings sit from the round fit (mm): big for a part that isn't round.</param>
public readonly record struct RoundFit(double Mean, double Offset, double HighDeg, double Swing, double RmsMm);

/// <summary>What to scan: the angles to stop at and the places along the axis to measure at each.</summary>
/// <param name="FromDeg">First angle (degrees turned from where the part is now).</param>
/// <param name="ToDeg">Last angle; 360 more than <paramref name="FromDeg"/> for a whole turn (360 itself isn't measured again).</param>
/// <param name="Stops">How many angles to measure at.</param>
public sealed record RotaryScanPlan(double FromDeg, double ToDeg, int Stops, IReadOnlyList<double> Xs, double LineY, string Method)
{
    public bool WholeTurn => ToDeg - FromDeg >= 359.9;

    /// <summary>The angles to stop at, in order.</summary>
    public double[] Angles()
    {
        int n = Math.Max(1, Stops);
        double span = ToDeg - FromDeg;
        if (n == 1) return [FromDeg];
        double step = WholeTurn ? span / n : span / (n - 1);
        return [.. Enumerable.Range(0, n).Select(i => Math.Round(FromDeg + i * step, 6))];
    }

    /// <summary>
    /// The angles in words, for under the Angles box: "4 angles, every 90°: 0, 90, 180, 270°" (long lists shortened).
    /// Kyle (2026-11-08) set 5 places along the part and got far more turns than he expected: show what Angles means.
    /// </summary>
    public string AnglesText()
    {
        var a = Angles();
        string N(double d) => d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        string list = a.Length <= 8 ? string.Join(", ", a.Select(N)) : $"{N(a[0])}, {N(a[1])}, {N(a[2])} … {N(a[^1])}";
        string step = a.Length > 1 ? $", every {N(a[1] - a[0])}°" : "";
        return $"{a.Length} {(a.Length == 1 ? "angle" : "angles")}{step}: {list}°";
    }

    /// <summary>Evenly spaced places from <paramref name="from"/> to <paramref name="to"/> (one in the middle for a count of 1).</summary>
    public static double[] Places(double from, double to, int count) =>
        count <= 1 ? [Math.Round((from + to) / 2, 3)] : [.. Enumerable.Range(0, count).Select(i => Math.Round(from + (to - from) * i / (count - 1), 3))];
}

/// <summary>What a scan drives: turning the rotary and measuring along the laser line.</summary>
public interface IRotaryScanRig
{
    /// <summary>Turn the part by <paramref name="degrees"/> (positive the way a design runs down the page) and wait until it has stopped.</summary>
    Task TurnAsync(double degrees, CancellationToken ct);

    /// <summary>
    /// The top heights at <paramref name="xs"/> along the laser line (null where unread). <paramref name="guesses"/> are
    /// what the last angle read at each place (null where it didn't), to start looking near.
    /// </summary>
    Task<double?[]> MeasureAsync(IReadOnlyList<double> xs, IReadOnlyList<double?> guesses, CancellationToken ct);
}

/// <summary>Running a scan: stop at each angle, measure, turn on; then back to where it started.</summary>
public static class RotaryScanner
{
    /// <summary>
    /// Scan with <paramref name="rig"/>. When it finishes the part is turned back to where it started (the shorter way
    /// on a whole turn). If it's stopped or fails part way it is NOT turned back (so nothing moves after Stop):
    /// <paramref name="turned"/> then says how far from the start it was left, and what was read so far comes back.
    /// <para>Everything stays on the caller's thread (no ConfigureAwait(false)): the rig drives the window's machine panel,
    /// which may only be touched from the UI thread ("Call from invalid thread" on Kyle's first scan).</para>
    /// </summary>
    public static async Task<RotaryScan> RunAsync(RotaryScanPlan plan, IRotaryScanRig rig, IProgress<string>? progress, Action<double> turned,
        Action<RotaryScan>? partial = null, CancellationToken ct = default)
    {
        var angles = plan.Angles();
        var xs = plan.Xs.ToArray();
        var rows = new double?[angles.Length][];
        for (int i = 0; i < rows.Length; i++) rows[i] = new double?[xs.Length];
        double at = 0;
        RotaryScan Make(int done) => new()
        {
            AnglesDeg = angles[..done],
            Xs = xs,
            Heights = rows[..done],
            LineY = plan.LineY,
            Method = plan.Method,
            When = DateTimeOffset.Now,
        };
        int done = 0;
        try
        {
            double?[] guesses = new double?[xs.Length];
            for (int i = 0; i < angles.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                double move = angles[i] - at;
                if (Math.Abs(move) > 1e-6)
                {
                    progress?.Report($"Turning to {angles[i]:0.#}° ({i + 1} of {angles.Length})…");
                    await rig.TurnAsync(move, ct);
                    at = angles[i];
                    turned(at);
                }
                progress?.Report($"Measuring at {angles[i]:0.#}° ({i + 1} of {angles.Length})…");
                var h = await rig.MeasureAsync(xs, guesses, ct);
                for (int x = 0; x < xs.Length && x < h.Length; x++)
                {
                    rows[i][x] = h[x];
                    if (h[x] is { } v) guesses[x] = v;
                }
                done = i + 1;
                partial?.Invoke(Make(done));
            }
            // Back to the start: on a whole turn, on round or back, whichever is shorter.
            double back = -at;
            if (plan.WholeTurn && 360 - at < at) back = 360 - at;
            if (Math.Abs(back) > 1e-6)
            {
                progress?.Report("Turning back to where it started…");
                await rig.TurnAsync(back, ct);
            }
            at = 0;
            turned(0);
            return Make(done);
        }
        catch (Exception e) when (e is OperationCanceledException || done > 0)
        {
            throw new RotaryScanStopped(Make(done), at, e);
        }
    }

    /// <summary>
    /// The head heights (the material height it focuses on) to measure from at one angle, lowest first, so the head
    /// only ever moves up during an angle. Each height reads the surface within ±<paramref name="reach"/> of it; the
    /// heights overlap a little and cover what the places are expected to be (<paramref name="guesses"/>, what the last
    /// angle read at each, a little either side). With no guesses (the first angle) it covers from well below
    /// <paramref name="start"/> (where the head was focused, on the top of the part) to a little above it. At most
    /// <paramref name="maxLevels"/> heights, spread out to fit if need be; all within <paramref name="lo"/>–<paramref name="hi"/>.
    /// </summary>
    public static List<double> FocusSweep(IReadOnlyList<double?> guesses, double start, double reach, double lo, double hi, int maxLevels = 6)
    {
        var levels = new List<double>();
        if (reach <= 0 || hi < lo || maxLevels < 1) return levels;
        var g = guesses.OfType<double>().ToList();
        double needLo, needHi;
        if (g.Count > 0)
        {
            needLo = g.Min() - reach * 0.5;
            needHi = g.Max() + reach * 0.5;
        }
        else
        {
            needLo = start - 5 * reach;
            needHi = start + reach;
        }
        needLo = Math.Max(lo - reach, needLo);
        needHi = Math.Min(hi + reach, needHi);
        double span = Math.Max(0, needHi - needLo - 2 * reach), step = 1.6 * reach;
        int n = (int)Math.Ceiling(span / step - 1e-9) + 1;
        if (n > maxLevels) { n = maxLevels; step = Math.Min(2 * reach, span / Math.Max(1, n - 1)); }
        for (int i = 0; i < n; i++)
        {
            double l = Math.Round(Math.Clamp(needLo + reach + i * step, lo, hi), 2);
            if (levels.Count == 0 || l - levels[^1] > reach * 0.25) levels.Add(l);
        }
        return levels;
    }
}

/// <summary>A scan stopped (or failed) part way: what was read, and how far from the start the part was left.</summary>
public sealed class RotaryScanStopped(RotaryScan sofar, double leftDeg, Exception why)
    : Exception(why is OperationCanceledException ? "Stopped." : why.Message, why)
{
    public RotaryScan SoFar { get; } = sofar;
    public double LeftDeg { get; } = leftDeg;
    public bool Cancelled => InnerException is OperationCanceledException;
}
