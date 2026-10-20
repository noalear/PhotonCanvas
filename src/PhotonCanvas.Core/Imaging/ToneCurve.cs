using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotonCanvas.Core.Imaging;

/// <summary>
/// How a material really takes a picture, measured: for each grey sent to the laser (0 black …
/// 1 white), how light it came out (0 = as dark as these settings go, 1 = the bare material).
/// Wood, leather and slate don't darken in step with the grey asked for (most go dark long before
/// black, and the palest greys barely show), so pictures come out too dark or flat. Turned round,
/// the curve says which grey to send to get the tone wanted: <see cref="Correct"/>.
/// </summary>
[JsonConverter(typeof(ToneCurveJson))]
public sealed record ToneCurve(IReadOnlyList<(double Input, double Lightness)> Points)
{
    double[]? _lut;

    /// <summary>
    /// From patch brightnesses read off a photo: <paramref name="brightness"/>[i] for grey
    /// <paramref name="inputs"/>[i], against the bare material's <paramref name="bare"/>. The
    /// darkest the settings go is the patch sent black (input 0); on metals that turn lighter it
    /// works the same way round.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than three readings, or no black patch, or no difference between black and bare.</exception>
    public static ToneCurve FromReadings(IReadOnlyList<double> inputs, IReadOnlyList<double> brightness, double bare)
    {
        if (inputs.Count != brightness.Count || inputs.Count < 3) throw new ArgumentException("A tone curve needs at least three readings.");
        int black = Enumerable.Range(0, inputs.Count).MinBy(i => inputs[i]);
        double full = brightness[black];
        if (Math.Abs(bare - full) < 1e-9) throw new ArgumentException("The darkest patch is no different from the bare material: nothing burned.");
        var pts = inputs.Select((g, i) => (Input: Math.Clamp(g, 0, 1), Lightness: Math.Clamp((brightness[i] - full) / (bare - full), 0, 1))).ToList();
        return new ToneCurve(pts);
    }

    /// <summary>The points sorted by input, made never to get darker as the input gets lighter (measurement noise smoothed out), with 0 → 0 and 1 → 1 at the ends.</summary>
    public IReadOnlyList<(double Input, double Lightness)> Monotone
    {
        get
        {
            var pts = Points.OrderBy(p => p.Input).ToList();
            if (pts.Count == 0 || pts[0].Input > 0) pts.Insert(0, (0, 0));
            if (pts[^1].Input < 1) pts.Add((1, 1));
            // Pool adjacent violators: a reading darker than the one before is averaged with it.
            var blocks = new List<(double Sum, int N, double In0, double In1)>();
            foreach (var (x, y) in pts)
            {
                blocks.Add((y, 1, x, x));
                while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].N > blocks[^1].Sum / blocks[^1].N)
                {
                    var b = blocks[^1];
                    var a = blocks[^2];
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[^1] = (a.Sum + b.Sum, a.N + b.N, a.In0, b.In1);
                }
            }
            var o = new List<(double, double)>();
            foreach (var (sum, n, in0, in1) in blocks)
            {
                double y = sum / n;
                o.Add((in0, y));
                if (in1 > in0) o.Add((in1, y));
            }
            return o;
        }
    }

    /// <summary>How light grey <paramref name="input"/> comes out (0 darkest … 1 bare).</summary>
    public double Response(double input)
    {
        var m = Monotone;
        input = Math.Clamp(input, 0, 1);
        for (int i = 1; i < m.Count; i++)
            if (input <= m[i].Input)
            {
                var (x0, y0) = m[i - 1];
                var (x1, y1) = m[i];
                return x1 > x0 ? y0 + (y1 - y0) * (input - x0) / (x1 - x0) : y1;
            }
        return m[^1].Lightness;
    }

    /// <summary>
    /// The grey to send for a picture tone of <paramref name="wanted"/> (0 black … 1 white) to come
    /// out that light. Where the material is already as dark as it goes, the lightest grey that
    /// gets there (no more heat than needed).
    /// </summary>
    public double Correct(double wanted)
    {
        var lut = _lut ??= Table();
        double x = Math.Clamp(wanted, 0, 1) * (lut.Length - 1);
        int i = (int)Math.Floor(x);
        if (i >= lut.Length - 1) return lut[^1];
        return lut[i] + (lut[i + 1] - lut[i]) * (x - i);
    }

    double[] Table()
    {
        var m = Monotone;
        var lut = new double[1025];
        // Within a whisker of the darkest is as dark as it goes (readings are never exactly 0).
        double floor = m.Min(p => p.Lightness) + DarkTolerance;
        for (int k = 0; k < lut.Length; k++)
        {
            double w = Math.Max((double)k / (lut.Length - 1), floor);
            // From the light end down: the first piece that reaches down to w.
            double input = 0;
            for (int i = m.Count - 1; i > 0; i--)
            {
                var (x0, y0) = m[i - 1];
                var (x1, y1) = m[i];
                if (w > y1 + 1e-12) { input = x1; break; }
                if (w >= y0 - 1e-12)
                {
                    input = y1 > y0 ? x0 + (x1 - x0) * (w - y0) / (y1 - y0) : x1;
                    break;
                }
            }
            lut[k] = Math.Clamp(input, 0, 1);
        }
        lut[^1] = 1;
        return lut;
    }

    /// <summary>How close to the darkest reading counts as "as dark as it goes".</summary>
    public const double DarkTolerance = 0.02;

    /// <summary>The darkest the material goes and the palest mark it shows, as a share of the grey range used (for a summary).</summary>
    public (double DarkFrom, double FirstShows) Range()
    {
        var m = Monotone;
        double floor = m.Min(p => p.Lightness) + DarkTolerance;
        double darkFrom = m.LastOrDefault(p => p.Lightness <= floor).Input;
        double firstShows = m.FirstOrDefault(p => p.Lightness >= 0.98).Input;
        return (darkFrom, firstShows == 0 ? 1 : firstShows);
    }
}

/// <summary>A tone curve in JSON: [[input, lightness], …].</summary>
public sealed class ToneCurveJson : JsonConverter<ToneCurve>
{
    public override ToneCurve? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var pts = JsonSerializer.Deserialize<double[][]>(ref reader, options) ?? [];
        var list = pts.Where(p => p.Length == 2).Select(p => (Math.Clamp(p[0], 0, 1), Math.Clamp(p[1], 0, 1))).ToList();
        return list.Count >= 2 ? new ToneCurve(list) : null;
    }

    public override void Write(Utf8JsonWriter writer, ToneCurve value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Points.Select(p => new[] { p.Input, p.Lightness }).ToArray(), options);
}
