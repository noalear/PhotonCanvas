using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// A sign worked out from its spec: the outline, the border's bands following it with their
/// patterns fitted to each run, the words laid out inside, the screw holes, and the height of every
/// point. Heights are mm above the deepest level (<see cref="Lowest"/> is 0, <see cref="Top"/> the
/// surface); NaN = not part of the sign (outside it, or a screw hole).
/// </summary>
public sealed class SignPlan
{
    public SignSpec Spec { get; }
    public SignOutline Outline { get; }
    /// <summary>The border at the size it's made (its deepest level at 0), or null for none.</summary>
    public BorderDesign? Border { get; }
    public SignWords Words { get; }
    public IReadOnlyList<Vec2> Holes { get; }
    public double SizeW => Outline.W;
    public double SizeH => Outline.H;
    /// <summary>The surface: white in the depth map (mm above the deepest level).</summary>
    public double Top { get; }
    public double Lowest => 0;
    /// <summary>How deep the panel inside the border sits below the surface (mm).</summary>
    public double PanelDepth { get; }
    /// <summary>How deep the countersinks go below where they start (mm; 0 without).</summary>
    public double CountersinkDepth { get; }

    readonly BorderBand[] _bands;
    readonly double[] _t0;
    readonly double _borderWidth, _borderTop, _edge;
    // Per run of the outline and band: the repeat's length, how many fit, and the phase.
    readonly double[][] _pitch;
    readonly int[][] _repeats;
    readonly double[] _phase;
    readonly InkField _ink;
    readonly double _vk;          // depth per mm in from a letter's edge for a V (1 / tan of half the angle)

    public SignPlan(SignSpec spec)
    {
        Spec = spec;
        Outline = SignOutline.For(spec);
        Border = spec.SizedBorder();
        _bands = Border is null ? [] : [.. Border.Bands];
        _t0 = new double[_bands.Length + 1];
        for (int i = 0; i < _bands.Length; i++) _t0[i + 1] = _t0[i] + _bands[i].WidthMm;
        _borderWidth = _t0[^1];
        _borderTop = Border?.TopMm ?? 0;
        _edge = Math.Max(0, spec.EdgeMarginMm);

        // Fit each ornament's repeats to each run of the edge: a whole number, a motif in the middle.
        var runs = Outline.Runs;
        _pitch = new double[runs.Count][];
        _repeats = new int[runs.Count][];
        _phase = new double[runs.Count];
        for (int r = 0; r < runs.Count; r++)
        {
            _pitch[r] = new double[_bands.Length];
            _repeats[r] = new int[_bands.Length];
            var run = runs[r];
            _phase[r] = run.Loop ? 0 : 0.5;
            for (int i = 0; i < _bands.Length; i++)
            {
                double want = BorderPlan.NaturalPitch(_bands[i]);
                double len = Math.Max(1e-6, run.Length);
                int n = Math.Max(1, (int)Math.Round(len / want));
                if (_bands[i].Ornament != Ornament.None)
                {
                    if (run.Loop)
                    {
                        // A multiple of four puts a motif at the top, bottom and both sides.
                        int four = Math.Max(4, (int)Math.Round(len / want / 4) * 4);
                        n = Math.Abs(len / four - want) / want < 0.25 ? four : Math.Max(2, (int)Math.Round(len / want / 2) * 2);
                    }
                    else if (n % 2 == 0)
                    {
                        // Odd counts put a whole motif in the middle of the run, with gaps at its corners.
                        int up = n + 1, down = n - 1;
                        n = down >= 1 && Math.Abs(len / down - want) <= Math.Abs(len / up - want) ? down : up;
                    }
                }
                _repeats[r][i] = n;
                _pitch[r][i] = len / n;
            }
        }

        PanelDepth = Border is not null && spec.Panel == PanelLevel.BorderInnerEdge && _bands.Length > 0
            ? Math.Max(0, _borderTop - Level(_bands[^1], 1)) : 0;

        // Screw holes, then the words kept clear of the border and the holes.
        double csk = spec.Countersink ? spec.CountersinkMm : spec.HoleMm;
        CountersinkDepth = spec.Holes != SignHoles.None && spec.Countersink ? (spec.CountersinkMm - spec.HoleMm) / 2 : 0;
        Holes = PlaceHoles(spec, Outline, _edge + _borderWidth + csk / 2 + 2.5);
        Words = SignWords.Make(spec, Outline, _edge + _borderWidth + Math.Max(0, spec.MarginMm), Holes, csk / 2 + Math.Max(1, spec.MarginMm));

        double k = 1 / Math.Tan(Math.Clamp(spec.VAngleDeg, 20, 150) * Math.PI / 360);
        _vk = k;
        double depth = Math.Max(0, spec.LetterDepthMm);
        double cap = spec.Style switch
        {
            LetterStyle.VCarved or LetterStyle.Chiselled => depth / k,
            _ => spec.Edge == LetterEdge.Square && spec.Style is LetterStyle.Sunk or LetterStyle.Raised ? 0 : Math.Max(0.05, spec.EdgeMm),
        };
        _ink = new InkField(Words.Outlines, Math.Max(cap, 0.6) + 0.05);

        double letters = PanelDepth + depth;
        // A countersink is cut from where the screw head sits: the panel, or its floor round raised letters.
        double seat = PanelDepth + (spec.RaisedLetters ? depth : 0);
        Top = Math.Max(Math.Max(Border?.DepthMm ?? 0, letters), CountersinkDepth > 0 ? seat + CountersinkDepth : 0);
        if (!(Top > 0)) Top = 1;
    }

    /// <summary>Where the screw holes go: inside the border, at the sides, the corners or the top.</summary>
    static IReadOnlyList<Vec2> PlaceHoles(SignSpec spec, SignOutline o, double inset)
    {
        double w = o.W, h = o.H;
        Vec2 mid = new(w / 2, h / 2);
        Vec2? Toward(Vec2 from) => o.FirstInset(from, mid, inset);
        var found = spec.Holes switch
        {
            SignHoles.Sides => new[] { Toward(new(0, h / 2)), Toward(new(w, h / 2)) },
            SignHoles.TopCorners => [Toward(new(0, 0)), Toward(new(w, 0))],
            SignHoles.FourCorners => [Toward(new(0, 0)), Toward(new(w, 0)), Toward(new(w, h)), Toward(new(0, h))],
            SignHoles.TopMiddle => [Toward(new(w / 2, 0))],
            _ => [],
        };
        return [.. found.Where(p => p is not null).Select(p => p!.Value)];
    }

    // ---- the border ----

    /// <summary>A band's profile height at <paramref name="tau"/> across it (0 outer … 1 inner), before carving.</summary>
    static double Level(BorderBand b, double tau)
    {
        double t = b.Flip ? 1 - tau : tau;
        return b.TopMm - Math.Max(0, b.DropMm) * (1 - Profiles.Height(b.Profile, t, b.Count));
    }

    /// <summary>The border's height <paramref name="t"/> mm in from its outer side, <paramref name="s"/> mm along run <paramref name="run"/>.</summary>
    double BandHeight(int run, double s, double t)
    {
        int i = Array.BinarySearch(_t0, t);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, _bands.Length - 1);
        var b = _bands[i];
        double w = Math.Max(1e-9, b.WidthMm);
        double tau = Math.Clamp((t - _t0[i]) / w, 0, 1);
        double h = Level(b, tau);
        if (b.Ornament == Ornament.None || b.CarveMm <= 0) return h;
        double u = s / _pitch[run][i] + _phase[run];
        double v = b.Flip ? 1 - tau : tau;
        double m = Ornaments.Height(b.Ornament, u, v, new OrnamentScale(_pitch[run][i], w, b.Count));
        return h - b.CarveMm * (1 - Math.Clamp(m, 0, 1));
    }

    /// <summary>For each run of the edge, how many repeats each ornamented band has (for the summary).</summary>
    public IEnumerable<(string Run, int Band, int Repeats, double PitchMm)> Repeats()
    {
        for (int r = 0; r < Outline.Runs.Count; r++)
            for (int i = 0; i < _bands.Length; i++)
                if (_bands[i].Ornament != Ornament.None) yield return (Outline.Runs[r].Name, i, _repeats[r][i], _pitch[r][i]);
    }

    // ---- the words ----

    /// <summary>How deep the lettering goes below the panel at a point <paramref name="sd"/> mm inside the words (− outside).</summary>
    double Lettering(double sd, double x, double y, double mmPerPx)
    {
        var s = Spec;
        double depth = Math.Max(0, s.LetterDepthMm), e = Math.Max(0.05, s.EdgeMm);
        double Edge(LetterEdge edge) => edge switch
        {
            // 0 at the letter's outline, 1 a bevel's or rounding's width inside it.
            LetterEdge.Chamfer => Math.Clamp(sd / e, 0, 1),
            LetterEdge.Rounded => Math.Clamp(sd / e, 0, 1),
            _ => Math.Clamp(0.5 + sd / Math.Max(1e-6, mmPerPx), 0, 1),
        };
        switch (s.Style)
        {
            case LetterStyle.VCarved:
                return sd <= 0 ? 0 : Math.Min(depth, sd * _vk);
            case LetterStyle.Sunk:
                {
                    double a = Edge(s.Edge);
                    // A rounded lip: level at first, falling steeply into the letter.
                    if (s.Edge == LetterEdge.Rounded) a = 1 - Math.Sqrt(Math.Max(0, 1 - a * a));
                    return depth * a;
                }
            default:
                {
                    double floor = Math.Max(0, depth - Texture(x, y));
                    double a = s.Style switch
                    {
                        LetterStyle.Domed => Round(Math.Clamp(sd / e, 0, 1)),
                        LetterStyle.Chiselled => Math.Clamp(sd * _vk / Math.Max(1e-6, depth), 0, 1),
                        _ => s.Edge == LetterEdge.Rounded ? Round(Edge(s.Edge)) : Edge(s.Edge),
                    };
                    return floor * (1 - a);
                }
        }
        // A quarter round: rises steeply from the edge and levels off.
        static double Round(double x) => Math.Sqrt(Math.Max(0, 1 - (1 - x) * (1 - x)));
    }

    /// <summary>How much the panel's texture stands up from the floor round raised letters (mm).</summary>
    double Texture(double x, double y)
    {
        double a = Math.Max(0, Math.Min(Spec.TextureMm, Spec.LetterDepthMm));
        switch (Spec.Texture)
        {
            case Background.Hammered:
                return a * (0.62 * Noise.At(x / 2.4, y / 2.4, 7) + 0.38 * Noise.At(x / 1.1, y / 1.1, 13));
            case Background.Stippled:
                {
                    // A pit in each 1.1 mm cell, jittered, the rest left standing.
                    const double Cell = 1.1;
                    int cx = (int)Math.Floor(x / Cell), cy = (int)Math.Floor(y / Cell);
                    double best = double.MaxValue;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            double px = (cx + i + 0.2 + 0.6 * Noise.Hash(cx + i, cy + j, 3)) * Cell;
                            double py = (cy + j + 0.2 + 0.6 * Noise.Hash(cx + i, cy + j, 5)) * Cell;
                            best = Math.Min(best, (x - px) * (x - px) + (y - py) * (y - py));
                        }
                    double r = Math.Sqrt(best) / 0.36;
                    return a * (r >= 1 ? 1 : 1 - Math.Sqrt(Math.Max(0, 1 - r * r)));
                }
            case Background.Lines:
                return a * (0.5 + 0.5 * Math.Cos(2 * Math.PI * y / 1.2));
            default:
                return 0;
        }
    }

    // ---- heights ----

    /// <summary>The height at (x, y) given the words' signed distance there: NaN outside the sign or in a hole.</summary>
    double Height(double x, double y, double sd, double mmPerPx)
    {
        double t = Outline.Inset(x, y, out int run, out double along);
        if (t < 0) return double.NaN;
        double sink = 0;   // how far into the countersink's cone, from its rim (90°: as deep as that)
        foreach (var h in Holes)
        {
            double rho = Math.Sqrt((x - h.X) * (x - h.X) + (y - h.Y) * (y - h.Y));
            if (rho <= Spec.HoleMm / 2) return double.NaN;
            if (Spec.Countersink) sink = Math.Max(sink, Spec.CountersinkMm / 2 - rho);
        }
        double depth;
        if (t < _edge) depth = 0;
        else if (t - _edge < _borderWidth) depth = _borderTop - BandHeight(run, along, t - _edge);
        else depth = PanelDepth + Lettering(sd, x, y, mmPerPx);
        return Top - (sink > 0 ? depth + sink : depth);
    }

    /// <summary>The height at one point (mm above the deepest level), or NaN where there's no sign.</summary>
    public double HeightAt(double x, double y) => Height(x, y, _ink.At(x, y), 0.1);

    /// <summary>
    /// The heights of a window <paramref name="cols"/> × <paramref name="rows"/> pixels of
    /// <paramref name="mmPerPx"/>, the first one's corner at (<paramref name="x0"/>, <paramref name="y0"/>).
    /// </summary>
    public float[] Render(double x0, double y0, double mmPerPx, int cols, int rows, CancellationToken ct = default)
    {
        var sd = _ink.Fill(x0, y0, mmPerPx, cols, rows, ct);
        var h = new float[cols * rows];
        var opts = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, rows, opts, r =>
        {
            double y = y0 + (r + 0.5) * mmPerPx;
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                h[i] = (float)Height(x0 + (c + 0.5) * mmPerPx, y, sd[i], mmPerPx);
            }
        });
        return h;
    }

    /// <summary>The cut lines (mm): round the sign if asked for, and every screw hole.</summary>
    public List<List<Vec2>> CutLines()
    {
        var lines = new List<List<Vec2>>();
        if (Spec.CutLines) lines.Add([.. Outline.Polygon]);
        foreach (var h in Holes)
        {
            var c = new List<Vec2>();
            for (int i = 0; i < 48; i++)
            {
                double a = 2 * Math.PI * i / 48;
                c.Add(new Vec2(h.X + Spec.HoleMm / 2 * Math.Cos(a), h.Y + Spec.HoleMm / 2 * Math.Sin(a)));
            }
            lines.Add(c);
        }
        return lines;
    }
}

/// <summary>Cheap smooth noise for the textures.</summary>
static class Noise
{
    public static double At(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        double top = a + (b - a) * sx, bottom = c + (d - c) * sx;
        return top + (bottom - top) * sy;
    }

    public static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
}
