using PhotonCanvas.Core;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.RecipeTuner;

/// <summary>A rated result the tuner learns from: a library recipe, or a try it kept.</summary>
/// <param name="Trust">1 for the material itself; less for similar materials (0.15–0.7).</param>
/// <param name="From">Where it came from, e.g. "Plywood 3 mm".</param>
/// <param name="Heat">What you said about it: −1 not enough (didn't cut through, too light), +1 too much (charred, too dark), 0 right or not said.</param>
public sealed record Rated(ProcessSettings Settings, int Rating, double Trust, string From, bool Own, DateTime When, int Heat = 0);

/// <summary>What to tune: which material, for what, with which laser, changing which settings.</summary>
public sealed record TuneAsk(Material Material, RecipePurpose Purpose, LightSource Light, IReadOnlyList<Knob> Change, int Count = 6);

public enum SuggestionKind
{
    /// <summary>What the tuner thinks is most likely to come out well.</summary>
    BestGuess,
    /// <summary>Close to what worked, to find out whether a small change is better.</summary>
    Refine,
    /// <summary>Somewhere it knows little about, that could turn out better.</summary>
    Explore,
    /// <summary>Nothing rated yet: a spread around the starting point.</summary>
    Spread,
}

/// <summary>One try: the settings, how good the tuner expects it to be (stars, NaN when it can't say) and why it's suggested.</summary>
public sealed record Suggestion(ProcessSettings Settings, SuggestionKind Kind, double Expected, double Uncertainty, string Why);

/// <summary>The tuner's answer.</summary>
/// <param name="Start">The settings it started from (the best so far, or the material's own recipe, or the program's defaults).</param>
public sealed record TuneResult(IReadOnlyList<Suggestion> Suggestions, IReadOnlyList<Rated> Evidence, ProcessSettings Start, string StartFrom);

/// <summary>
/// The recipe tuner: learns how each setting changes the stars you give, from every rated recipe on
/// the material (and, counting for less, on similar materials), and picks the next few settings
/// worth trying: its best guess first, then ones that could beat your best so far.
/// <para>How: a Gaussian-process model of rating against the settings (in "steps", see <see cref="Knobs"/>)
/// plus the heat they put in; each try is picked for its expected improvement on your best, and the
/// model then assumes it came out as predicted before picking the next, so the round spreads out.</para>
/// </summary>
public static class Tuner
{
    /// <summary>
    /// Every rated recipe that says something about <paramref name="target"/>: its own (trust 1) and
    /// those on materials of the same category (less, the more the thickness differs), for the same
    /// kind of work, laser and machine. <paramref name="kept"/> are the tuner's own tries, poor ones
    /// included, that aren't in the library.
    /// </summary>
    public static List<Rated> Gather(MaterialLibrary lib, IEnumerable<TriedRecord> kept, Material target, RecipePurpose purpose, LightSource light, string machine)
    {
        var list = new List<Rated>();
        var inLibrary = new HashSet<Guid>();
        var keptList = kept.ToList();
        // What you said about a try's heat stays with the tuner, also for tries saved to the library.
        var heat = keptList.Where(k => k.RecipeId is not null && k.Heat != 0).GroupBy(k => k.RecipeId!.Value).ToDictionary(g => g.Key, g => g.Last().Heat);
        foreach (var m in lib.Materials)
        {
            double trust = m.Id == target.Id ? 1 : Similar(target.Name, target.Category, target.ThicknessMm, m.Name, m.Category, m.ThicknessMm);
            foreach (var r in m.Recipes)
            {
                inLibrary.Add(r.Id);
                if (r.Rating <= 0 || Knobs.Family(r.Purpose) != Knobs.Family(purpose) || r.Settings.Laser.Light != light) continue;
                if (!string.IsNullOrEmpty(r.Machine) && r.Machine != machine) continue;
                double t = trust * (r.Purpose == purpose ? 1 : 0.8);
                if (t <= 0) continue;
                list.Add(new Rated(r.Settings, Math.Clamp(r.Rating, 1, 5), t, m.Label, m.Id == target.Id, r.TestedOn ?? r.Updated, heat.GetValueOrDefault(r.Id)));
            }
        }
        foreach (var k in keptList)
        {
            if (k.RecipeId is { } id && inLibrary.Contains(id)) continue;
            if (k.Rating <= 0 || Knobs.Family(k.Purpose) != Knobs.Family(purpose) || k.Settings.Laser.Light != light) continue;
            if (!string.IsNullOrEmpty(k.Machine) && k.Machine != machine) continue;
            bool own = k.MaterialId == target.Id;
            double t = (own ? 1 : Similar(target.Name, target.Category, target.ThicknessMm, k.MaterialName, k.Category, k.ThicknessMm)) * (k.Purpose == purpose ? 1 : 0.8);
            if (t <= 0) continue;
            var label = own ? target.Label : k.ThicknessMm is { } th ? $"{k.MaterialName} {th:0.##} mm" : k.MaterialName;
            list.Add(new Rated(k.Settings, Math.Clamp(k.Rating, 1, 5), t, label, own, k.When, Math.Sign(k.Heat)));
        }
        return list;
    }

    /// <summary>How much another material's results count for this one: 0 for another category; up to 0.7 for the same stuff at about the same thickness.</summary>
    public static double Similar(string name, string category, double? thickness, string otherName, string otherCategory, double? otherThickness)
    {
        if (!string.Equals(category, otherCategory, StringComparison.OrdinalIgnoreCase)) return 0;
        double t;
        if (thickness is { } a && otherThickness is { } b && a > 0 && b > 0)
        {
            double ratio = Math.Max(a, b) / Math.Min(a, b);
            t = ratio <= 1.25 ? 0.5 : ratio <= 2 ? 0.3 : 0.15;
        }
        else t = 0.3;
        if (string.Equals(name.Trim(), otherName.Trim(), StringComparison.OrdinalIgnoreCase)) t += 0.2;
        return Math.Min(0.7, t);
    }

    /// <summary>The settings for a purpose and laser when there's nothing to go on: the program's own layer defaults.</summary>
    public static ProcessSettings Defaults(RecipePurpose p, LightSource light, MachineProfile m)
    {
        var mode = Knobs.ModeFor(p);
        var s = ProcessSettings.From(PhotonCanvas.Doc.Layer.Default(0, mode));
        if (light == LightSource.Red)
        {
            double speed = mode is PhotonCanvas.Doc.LayerMode.Fill or PhotonCanvas.Doc.LayerMode.Image ? 1000 : 200;
            int khz = m.MopaLimitFor(200)?.DefaultKhz ?? 65;
            s = s with { Laser = LaserSettings.Red(mode == PhotonCanvas.Doc.LayerMode.Cut ? 80 : 50, speed, 200, khz) };
        }
        return Knobs.Fix(s, m);
    }

    /// <summary>How many steps further to look the way the heat feedback points, and how far past the last try with too little (or too much) heat.</summary>
    const double LeanSteps = 2.5, Window = 3;

    /// <summary>Suggest the next round of tries.</summary>
    public static TuneResult Suggest(TuneAsk ask, IReadOnlyList<Rated> evidence, MachineProfile m)
    {
        var all = Knobs.For(ask.Purpose, ask.Light);
        var change = ask.Change.Where(all.Contains).Distinct().ToList();
        if (change.Count == 0) change = [.. Knobs.Default(ask.Purpose, ask.Light)];
        bool reflective = ask.Material.HighlyReflective;
        // At most 150 results: all the material's own, then the most trusted others.
        var ev = evidence.OrderByDescending(e => e.Own).ThenByDescending(e => e.Trust).ThenByDescending(e => e.When).Take(150).ToList();
        var (start, startFrom) = Start(ask, ev, m);
        start = Knobs.Fix(start with { Mode = Knobs.ModeFor(ask.Purpose), Laser = start.Laser with { Light = ask.Light } }, m, reflective);

        double[] Features(ProcessSettings s)
        {
            var f = new double[all.Count + 1];
            for (int i = 0; i < all.Count; i++) f[i] = Knobs.ToSteps(all[i], Knobs.Get(s, all[i]), m);
            f[^1] = Knobs.Energy(s, ask.Purpose, m);
            return f;
        }
        string Key(ProcessSettings s) => string.Join("|", all.Select(k => Knobs.Get(s, k).ToString("R")));

        var own = ev.Where(e => e.Own).ToList();
        var tried = own.Select(e => Key(e.Settings)).ToHashSet();
        var rng = new Random(Seed(ask));

        // What the heat feedback says: the sweet spot has more heat than any try that had too little,
        // and less than any that had too much (heat as power × passes ÷ speed, see Knobs.Energy).
        double? lowE = own.Where(e => e.Heat < 0).Select(e => Features(e.Settings)[^1]).DefaultIfEmpty(double.NaN).Max() is var l && double.IsFinite(l) ? l : null;
        double? highE = own.Where(e => e.Heat > 0).Select(e => Features(e.Settings)[^1]).DefaultIfEmpty(double.NaN).Min() is var h && double.IsFinite(h) ? h : null;
        if (lowE is { } le && highE is { } he && le >= he) lowE = highE = null; // they disagree: no bracket
        double? midE = (lowE, highE) switch
        {
            ({ } x, { } y) => (x + y) / 2,
            ({ } x, null) => x + 1,
            (null, { } y) => y - 1,
            _ => null,
        };

        // Where to look: around the best result so far (when several are as good, the one nearest
        // what the heat feedback points at, else the newest), a step and a half each way, further in
        // the direction the heat feedback points.
        if (own.Count > 0)
        {
            int top = own.Max(e => e.Rating);
            var centre = own.Where(e => e.Rating == top)
                .OrderBy(e => midE is { } mm ? Math.Abs(Features(e.Settings)[^1] - mm) : 0).ThenByDescending(e => e.When).First();
            start = Knobs.Fix(centre.Settings with { Mode = Knobs.ModeFor(ask.Purpose) }, m, reflective);
        }
        var told = own.Where(e => e.Heat != 0).OrderByDescending(e => e.Rating).ThenByDescending(e => e.When).Take(3).ToList();
        double lean = told.Count == 0 ? 0 : -told.Average(e => e.Heat);
        lean = Math.Abs(lean) < 0.3 ? 0 : Math.Sign(lean);
        var lo = new double[change.Count];
        var hi = new double[change.Count];
        // Good results (3 stars and up) widen the box to take them all in.
        var good = (own.Count > 0 ? own : ev.Where(e => e.Trust >= 0.25)).Where(e => e.Rating >= 3).ToList();
        for (int i = 0; i < change.Count; i++)
        {
            var k = change[i];
            var (minV, maxV) = Knobs.Limits(k, m, start);
            double limLo = Knobs.ToSteps(k, minV, m), limHi = Knobs.ToSteps(k, maxV, m);
            var vals = good.Select(e => Knobs.ToSteps(k, Knobs.Get(e.Settings, k), m)).Append(Knobs.ToSteps(k, Knobs.Get(start, k), m)).ToList();
            double a = vals.Min(), b = vals.Max();
            double pad = b - a < 0.5 ? 1.5 : 1;
            double more = lean * HeatSign(k) * (lowE is not null && highE is not null ? 1 : LeanSteps);
            lo[i] = Math.Clamp(a - pad + Math.Min(0, more), limLo, limHi);
            hi[i] = Math.Clamp(b + pad + Math.Max(0, more), limLo, limHi);
        }
        bool InBracket(double e) =>
            (lowE is not { } x || (e > x + 0.25 && (highE is not null || e < x + Window))) &&
            (highE is not { } y || (e < y - 0.25 && (lowE is not null || e > y - Window)));

        ProcessSettings At(double[] u)
        {
            var s = start;
            for (int i = 0; i < change.Count; i++) s = Knobs.Set(s, change[i], Knobs.FromSteps(change[i], u[i], m), m, reflective);
            return s;
        }
        double[] StepsOf(ProcessSettings s) => [.. change.Select(k => Knobs.ToSteps(k, Knobs.Get(s, k), m))];

        // Candidates: spread over the box, plus closer looks around the best results.
        var cands = new Dictionary<string, ProcessSettings>();
        void Try(double[] u)
        {
            var s = At(u);
            var key = Key(s);
            if (!tried.Contains(key)) cands.TryAdd(key, s);
        }
        for (int n = 0; n < 2500; n++)
            Try([.. Enumerable.Range(0, change.Count).Select(i => lo[i] + rng.NextDouble() * (hi[i] - lo[i]))]);
        foreach (var e in ev.Where(e => e.Own || e.Trust >= 0.3).OrderByDescending(e => e.Rating * e.Trust).Take(4))
        {
            var c = StepsOf(e.Settings);
            for (int n = 0; n < 250; n++)
                Try([.. c.Select((v, i) => Math.Clamp(v + Gauss(rng) * 0.6, lo[i], hi[i]))]);
        }
        Try(StepsOf(start));
        var pool = cands.Values.ToList();
        // Keep to the heat the feedback points at (unless that leaves nothing).
        if (lowE is not null || highE is not null)
        {
            var inside = pool.Where(c => InBracket(Features(c)[^1])).ToList();
            if (inside.Count >= Math.Max(3, ask.Count)) pool = inside;
        }
        if (pool.Count == 0) return new TuneResult([], ev, start, startFrom);
        var poolF = pool.Select(Features).ToList();
        var poolU = pool.Select(StepsOf).ToList();
        int count = Math.Clamp(ask.Count, 1, 24);
        var picks = new List<Suggestion>();
        var pickedU = new List<double[]>();
        double MinGap(double[] u) => pickedU.Count == 0 ? double.PositiveInfinity : pickedU.Min(p => Dist(p, u));

        if (ev.Count == 0)
        {
            // Nothing rated: the start, then the tries furthest from those already picked (a spread).
            int first = pool.FindIndex(s => Key(s) == Key(start));
            if (first < 0) first = Nearest(poolU, StepsOf(start));
            picks.Add(new Suggestion(pool[first], SuggestionKind.Spread, double.NaN, double.NaN, $"The starting point: {startFrom}."));
            pickedU.Add(poolU[first]);
            var startU = StepsOf(start);
            while (picks.Count < count)
            {
                int best = -1;
                double bestGap = 0.25;
                for (int i = 0; i < pool.Count; i++)
                {
                    // Within about a step of the start, as far as possible from the others.
                    if (Dist(poolU[i], startU) > 1.25 * Math.Sqrt(change.Count)) continue;
                    double g = MinGap(poolU[i]);
                    if (g > bestGap) { bestGap = g; best = i; }
                }
                if (best < 0) break;
                picks.Add(new Suggestion(pool[best], SuggestionKind.Spread, double.NaN, double.NaN, "A step from the start: " + Describe(start, pool[best], change) + "."));
                pickedU.Add(poolU[best]);
            }
            return new TuneResult(picks, ev, start, startFrom);
        }

        // A try with too little heat says less heat is worse still (and too much: more is worse): a
        // pretend result a step that way, so the model looks the other way.
        var xs = ev.Select(e => Features(e.Settings)).ToList();
        var ys = ev.Select(e => (double)e.Rating).ToList();
        var ns = ev.Select(e => 0.25 / e.Trust).ToList();
        foreach (var e in ev.Where(e => e.Heat != 0 && e.Rating < 5))
        {
            var s = e.Settings;
            foreach (var k in all.Where(k => HeatSign(k) != 0))
                s = Knobs.Set(s, k, Knobs.FromSteps(k, Knobs.ToSteps(k, Knobs.Get(s, k), m) + e.Heat * HeatSign(k), m), m, reflective);
            if (Key(s) == Key(e.Settings)) continue;
            xs.Add(Features(s));
            ys.Add(Math.Max(1, e.Rating - 2));
            ns.Add(0.5 / e.Trust);
        }
        var gp = Gp.Fit([.. xs], [.. ys], [.. ns]);
        double target = own.Count > 0 ? own.Max(e => e.Rating) : ev.Max(e => e.Rating * Math.Min(1, e.Trust + 0.3));
        target = Math.Min(target, 4.8);
        const double MinStep = 0.6;
        // A try must differ from what you've already tried by a visible amount (a third of a step: 5% power, 15% speed).
        var ownU = own.Select(e => StepsOf(e.Settings)).ToList();
        var fresh = poolU.Select(u => ownU.Count == 0 || ownU.Min(o => Dist(o, u)) >= 0.33).ToList();
        for (int round = 0; round < count; round++)
        {
            int best = -1;
            double score = double.NegativeInfinity, mean = 0, sd = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (!fresh[i] || MinGap(poolU[i]) < MinStep) continue;
                var (mu, v) = gp.Predict(poolF[i]);
                double s = Math.Sqrt(Math.Max(v, 1e-9));
                // First the safest good one; then the most expected gain over the best so far.
                double a = round == 0 ? mu - 0.5 * s : Ei(mu, s, target + 0.1);
                if (a > score) { score = a; best = i; mean = mu; sd = s; }
            }
            if (best < 0) break;
            var kind = round == 0 ? SuggestionKind.BestGuess : sd > 0.75 ? SuggestionKind.Explore : SuggestionKind.Refine;
            picks.Add(new Suggestion(pool[best], kind, Math.Clamp(mean, 0, 5), sd, Why(pool[best], ev, all, ask.Purpose, Features)));
            pickedU.Add(poolU[best]);
            // Assume it comes out as predicted, so the next pick goes somewhere else.
            gp = gp.With(poolF[best], mean, 0.05);
        }
        return new TuneResult(picks, ev, start, startFrom);
    }

    /// <summary>Which way a setting adds heat: +1 more power, passes, lines or dots; −1 speed (faster is less); 0 for the rest.</summary>
    static int HeatSign(Knob k) => k switch
    {
        Knob.Power or Knob.Passes or Knob.LinesPerCm or Knob.Dpi => 1,
        Knob.Speed => -1,
        _ => 0,
    };

    /// <summary>Where to start: the best result so far (your own first), else the material's own recipe for this, else the defaults.</summary>
    static (ProcessSettings, string) Start(TuneAsk ask, List<Rated> ev, MachineProfile m)
    {
        if (ev.Where(e => e.Own).OrderByDescending(e => e.Rating).ThenByDescending(e => e.When).FirstOrDefault() is { } o)
            return (o.Settings, $"your best so far ({Stars(o.Rating)})");
        if (ev.OrderByDescending(e => e.Rating * e.Trust).ThenByDescending(e => e.When).FirstOrDefault() is { } b)
            return (b.Settings, $"the best on {b.From} ({Stars(b.Rating)})");
        if (ask.Material.Recipes.Where(r => Knobs.Family(r.Purpose) == Knobs.Family(ask.Purpose) && r.Settings.Laser.Light == ask.Light)
                .OrderByDescending(r => r.Purpose == ask.Purpose).ThenByDescending(r => r.Tested).ThenByDescending(r => r.Updated).FirstOrDefault() is { } r)
            return (r.Settings, $"the material's recipe “{r.Title}”");
        return (Defaults(ask.Purpose, ask.Light, m), "the program's default settings");
    }

    static string Stars(int n) => new string('★', Math.Clamp(n, 0, 5)) + new string('☆', 5 - Math.Clamp(n, 0, 5));

    /// <summary>Why a try is suggested: the result it is closest to, and how it differs.</summary>
    static string Why(ProcessSettings s, List<Rated> ev, IReadOnlyList<Knob> all, RecipePurpose purpose, Func<ProcessSettings, double[]> features)
    {
        var f = features(s);
        var pool = ev.Any(e => e.Own) ? ev.Where(e => e.Own) : ev;
        var nearest = pool.OrderBy(e => Dist(features(e.Settings), f) / Math.Sqrt(e.Trust)).First();
        string whose = nearest.Own
            ? $"Your {Stars(nearest.Rating)} ({Knobs.Summary(nearest.Settings, purpose)})"
            : $"The {Stars(nearest.Rating)} on {nearest.From} ({Knobs.Summary(nearest.Settings, purpose)})";
        var diff = Describe(nearest.Settings, s, all);
        return diff.Length == 0 ? $"{whose}, tried here." : $"{whose}, but {diff}.";
    }

    /// <summary>"5 mm/s slower, 1 more pass".</summary>
    static string Describe(ProcessSettings from, ProcessSettings to, IReadOnlyList<Knob> knobs) =>
        string.Join(", ", knobs.Select(k => Knobs.Difference(k, Knobs.Get(from, k), Knobs.Get(to, k))).Where(t => t.Length > 0));

    static double Ei(double mu, double sd, double best)
    {
        double z = (mu - best) / sd;
        return (mu - best) * Phi(z) + sd * Pdf(z);
    }

    static double Pdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);

    /// <summary>The normal distribution's cumulative probability (Abramowitz and Stegun 7.1.26, good to 1e-7).</summary>
    static double Phi(double z)
    {
        double t = 1 / (1 + 0.3275911 * Math.Abs(z) / Math.Sqrt(2));
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z / 2);
        return z >= 0 ? 0.5 * (1 + y) : 0.5 * (1 - y);
    }

    static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    static double Dist(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.Sqrt(s);
    }

    static int Nearest(List<double[]> pts, double[] u)
    {
        int best = 0;
        for (int i = 1; i < pts.Count; i++) if (Dist(pts[i], u) < Dist(pts[best], u)) best = i;
        return best;
    }

    /// <summary>The same question gives the same answer.</summary>
    static int Seed(TuneAsk a)
    {
        unchecked
        {
            int h = 17;
            foreach (var b in a.Material.Id.ToByteArray()) h = h * 31 + b;
            h = h * 31 + (int)a.Purpose;
            h = h * 31 + (int)a.Light;
            foreach (var k in a.Change) h = h * 31 + (int)k;
            return h;
        }
    }
}

/// <summary>
/// A small Gaussian-process regression: ratings against settings, with a squared-exponential kernel
/// whose length (in steps) is picked from a few by how well it explains the ratings.
/// </summary>
sealed class Gp
{
    const double SignalVar = 1.44; // a rating can be about ±1.2 stars from the prior
    const double PriorBelow = 0.5;
    /// <summary>What a setting nobody has tried is expected to get: a bit below average (untried settings are more often worse than better).</summary>
    readonly double _prior;
    readonly List<double[]> _x;
    readonly List<double> _y, _noise;
    readonly double _len;
    double[,] _l = new double[0, 0];
    double[] _alpha = [];

    Gp(List<double[]> x, List<double> y, List<double> noise, double len, double prior)
    {
        _x = x; _y = y; _noise = noise; _len = len; _prior = prior;
        Factor();
    }

    public static Gp Fit(double[][] x, double[] y, double[] noise)
    {
        double[] lengths = x.Length >= 4 ? [0.7, 1.0, 1.4, 2.0] : [1.2];
        double prior = Math.Clamp(y.Average() - PriorBelow, 1.5, 2.5);
        Gp? best = null;
        double bestLl = double.NegativeInfinity;
        foreach (var len in lengths)
        {
            var g = new Gp([.. x], [.. y], [.. noise], len, prior);
            double ll = g.LogLikelihood();
            if (ll > bestLl) { bestLl = ll; best = g; }
        }
        return best!;
    }

    /// <summary>The same model with one more (pretend) result.</summary>
    public Gp With(double[] x, double y, double noise) => new([.. _x, x], [.. _y, y], [.. _noise, noise], _len, _prior);

    double K(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return SignalVar * Math.Exp(-s / (2 * _len * _len));
    }

    void Factor()
    {
        int n = _x.Count;
        var a = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
                a[i, j] = a[j, i] = K(_x[i], _x[j]) + (i == j ? _noise[i] + 1e-6 : 0);
        _l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double s = a[i, j];
                for (int k = 0; k < j; k++) s -= _l[i, k] * _l[j, k];
                _l[i, j] = i == j ? Math.Sqrt(Math.Max(s, 1e-9)) : s / _l[j, j];
            }
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = _y[i] - _prior;
        _alpha = SolveT(Solve(r));
    }

    double[] Solve(double[] b)
    {
        int n = b.Length;
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= _l[i, k] * z[k];
            z[i] = s / _l[i, i];
        }
        return z;
    }

    double[] SolveT(double[] z)
    {
        int n = z.Length;
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = z[i];
            for (int k = i + 1; k < n; k++) s -= _l[k, i] * x[k];
            x[i] = s / _l[i, i];
        }
        return x;
    }

    double LogLikelihood()
    {
        double fit = 0, det = 0;
        for (int i = 0; i < _y.Count; i++) { fit += (_y[i] - _prior) * _alpha[i]; det += Math.Log(_l[i, i]); }
        return -0.5 * fit - det;
    }

    /// <summary>The expected rating and its variance.</summary>
    public (double Mean, double Var) Predict(double[] x)
    {
        int n = _x.Count;
        var k = new double[n];
        double mu = _prior;
        for (int i = 0; i < n; i++) { k[i] = K(_x[i], x); mu += k[i] * _alpha[i]; }
        var v = Solve(k);
        double var = SignalVar;
        for (int i = 0; i < n; i++) var -= v[i] * v[i];
        return (mu, Math.Max(var, 1e-9));
    }
}
