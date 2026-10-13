using static YTool.Core.NumFmt;

namespace YTool.Core;

// G-code for the xTool F2 Ultra galvo, in the dialect documented in
// docs/gcode-f2-ultra.md and verified against four real exports
// (tests/YTool.Tests/SampleTests.cs).
//
// A Job is a list of Blocks. Each block is one vector path, one filled
// region, or one dot-mode bitmap, with its own LaserSettings.

/// <summary>
/// The last preamble line differs between cut and engrave jobs in the stock
/// software (<c>M9043 H11…</c> vs <c>H1…</c>). Its meaning is unknown (not air
/// assist); we reproduce it so the machine sees exactly what it is used to.
/// </summary>
public enum Preamble { Cut, Engrave }

/// <summary>How multiple passes are executed.</summary>
public abstract record PassMode
{
    /// <summary>
    /// Stock behaviour: each block repeats its own passes (<c>G104 P&lt;n&gt;</c> …
    /// <c>G105</c>) before the next block starts.
    /// </summary>
    public sealed record PerBlock : PassMode;

    /// <summary>
    /// Pass by pass over the whole job: round 1 processes every block once,
    /// then waits <see cref="DelayS"/>, round 2 processes every block that has
    /// ≥ 2 passes, and so on. Blocks with fewer passes drop out of later
    /// rounds, so mixed layers (engrave ×1, cut ×8) behave naturally.
    /// </summary>
    public sealed record Rounds(double DelayS) : PassMode;
}

public abstract record Block(LaserSettings Laser);

public sealed record VectorBlock(Polyline Path, LaserSettings Laser) : Block(Laser);

/// <summary>Zigzag line fill of closed polygons, even-odd rule (holes work).</summary>
public sealed record FillBlock(IReadOnlyList<IReadOnlyList<Vec2>> Polygons, LaserSettings Laser, double LinesPerCm)
    : Block(Laser);

/// <summary>A 1-bit image, row-major, <c>true</c> = burn. Row 0 is at the smallest Y.</summary>
public sealed class Bitmap(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public bool[] Bits { get; } = new bool[width * height];

    public bool this[int x, int y]
    {
        get => Bits[y * Width + x];
        set => Bits[y * Width + x] = value;
    }
}

/// <summary>Dot-mode bitmap engraving: one laser pulse of <see cref="DotUs"/> per black pixel.</summary>
public sealed record DotBlock(
    Bitmap Bitmap,
    Vec2 Origin, // machine position of pixel (0, 0)
    LaserSettings Laser,
    double Dpi,
    int DotUs,
    double MinPower, // lower end of the power range written to blockConfig (percent)
    string Mode) // dither name written to blockConfig (informational)
    : Block(Laser);

public sealed class Job
{
    /// <summary>
    /// Longest single dwell we emit; longer pauses are split into several
    /// <c>G4</c>s so no firmware integer limit is ever approached.
    /// </summary>
    const long MaxDwellMs = 60_000;

    /// <summary>Longest pause between passes we accept.</summary>
    public const double MaxPassDelayS = 24 * 3600;

    public MachineProfile Machine { get; }
    public IReadOnlyList<Block> Blocks { get; }
    public PassMode PassMode { get; init; } = new PassMode.PerBlock();
    public Preamble Preamble { get; init; }
    /// <summary>Written to the <c># date=</c> header line, e.g. <c>2026_10_12_19_02_38</c>.</summary>
    public string Created { get; init; } = "";

    public Job(MachineProfile machine, IReadOnlyList<Block> blocks)
    {
        Machine = machine;
        Blocks = blocks;
        Preamble = blocks.Any(b => b is VectorBlock) ? Preamble.Cut : Preamble.Engrave;
    }

    int Rounds => Blocks.Count == 0 ? 1 : Blocks.Max(b => b.Laser.Passes);

    /// <summary>Checks every setting and every coordinate the laser will visit.</summary>
    public void Validate()
    {
        if (Blocks.Count == 0) throw new EmptyJobException();
        if (PassMode is PassMode.Rounds r)
            JobException.CheckRange("pause between passes", r.DelayS, 0, MaxPassDelayS, "s");
        bool anyMotion = false;
        for (int i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i];
            block.Laser.Validate(Machine);
            var ctx = $" (item {i + 1})";
            switch (block)
            {
                case VectorBlock b:
                    b.Path.Validate();
                    var pts = b.Path.EmittedPoints();
                    anyMotion |= pts.Count >= 2;
                    foreach (var p in pts) CheckPoint(p, ctx);
                    break;
                case FillBlock b:
                    JobException.CheckRange("line density", b.LinesPerCm, 1, 1000, "lines/cm");
                    foreach (var p in b.Polygons.SelectMany(x => x))
                    {
                        if (!p.IsFinite) throw new JobException("A fill coordinate is not a valid number.");
                        CheckPoint(p, ctx);
                    }
                    anyMotion |= FillLines(b).Count > 0;
                    break;
                case DotBlock b:
                    JobException.CheckRange("resolution", b.Dpi, 10, 2540, "dpi");
                    JobException.CheckRange("dot duration", b.DotUs, 10, 100_000, "µs");
                    if (b.DotUs % 10 != 0)
                        throw new JobException($"Dot duration must be a multiple of 10 µs (got {b.DotUs} µs).");
                    JobException.CheckRange("minimum power", b.MinPower, 0, b.Laser.Power, "%");
                    if (b.Bitmap.Bits.Length != b.Bitmap.Width * b.Bitmap.Height)
                        throw new JobException("Bitmap size does not match its data.");
                    if (!b.Origin.IsFinite) throw new JobException("Bitmap position is not a valid number.");
                    double pitch = 25.4 / b.Dpi;
                    // The corners of the pixel grid bound every dot.
                    if (b.Bitmap.Width > 0 && b.Bitmap.Height > 0)
                    {
                        CheckPoint(b.Origin, ctx);
                        CheckPoint(new(b.Origin.X + (b.Bitmap.Width - 1) * pitch,
                            b.Origin.Y + (b.Bitmap.Height - 1) * pitch), ctx);
                    }
                    anyMotion |= b.Bitmap.Bits.Any(v => v);
                    break;
            }
        }
        if (!anyMotion) throw new EmptyJobException();
    }

    void CheckPoint(Vec2 p, string ctx)
    {
        if (!Machine.Contains(p.X, p.Y))
            throw new OutsideWorkAreaException(p.X, p.Y, Machine.FieldW, Machine.FieldH, ctx);
    }

    /// <summary>The complete motion file. Validates first.</summary>
    public string Render()
    {
        Validate();
        var tag = Machine.GcodeTag;
        var lines = Header();
        switch (PassMode)
        {
            case PassMode.PerBlock:
                foreach (var b in Blocks)
                {
                    lines.AddRange(RenderBlock(b, tag, b.Laser.Passes));
                    lines.Add("");
                }
                break;
            case PassMode.Rounds r:
                int rounds = Rounds;
                for (int round = 1; round <= rounds; round++)
                {
                    if (round > 1 && r.DelayS > 0)
                    {
                        lines.Add($"# pass {round} of {rounds}");
                        lines.AddRange(Dwell(r.DelayS));
                        lines.Add("");
                    }
                    foreach (var b in Blocks.Where(b => b.Laser.Passes >= round))
                    {
                        lines.AddRange(RenderBlock(b, tag, 1));
                        lines.Add("");
                    }
                }
                break;
        }
        lines.AddRange(Tail(tag));
        return string.Join("\n", lines);
    }

    List<string> Header()
    {
        int level = Preamble == Preamble.Cut ? 11 : 1;
        var m9043 = string.Join(" ", "HIJKLM".Select(c => $"{c}{level}"));
        var tag = Machine.GcodeTag;
        return
        [
            $"# date={Created}",
            "# version=1.9.11",
            "# algorithmVersion=",
            $"# gc={{\"size\":{{\"w\":{JsNumber(Machine.FieldW)},\"h\":{JsNumber(Machine.FieldH)}}}}}",
            "# gc={\"offset\":{\"x\":0,\"y\":0}}",
            "# gc={\"start\":{\"x\":0,\"y\":0.0000}}",
            "# gc={\"keys\":[\"x\",\"y\"],\"rm\":1,\"is3DMode\":false}",
            "# timeConfig=",
            "G90",
            "G0 F3000",
            "G4P5",
            "G198 P84 \"M9033 V100\"",
            "G198 P85 \"M9401 D2 V500\"",
            "M9064 B3",
            "G198 P70 \"M9064 B3\"",
            "G198 P78 \"M9064 B3\"",
            "M9039 C2",
            "G198 P69 \"M9039 C2\"",
            "G198 P76 \"M9039 V50\"",
            $"G198 P76 \"M9043 {m9043}\"",
            "G4P5",
            $"# {tag} HEAD",
            "G0 F180000",
            "M4 S0",
            "G1 F180000",
            "G0 X0 Y0",
            "",
        ];
    }

    /// <summary>
    /// The framing file the machine traces at low power when its frame button
    /// is used. <paramref name="outline"/> is usually the job's bounding box or hull.
    /// </summary>
    public string Border(IReadOnlyList<Vec2> outline)
    {
        if (outline.Count < 2) throw new JobException("The frame outline needs at least two points.");
        foreach (var p in outline)
        {
            if (!p.IsFinite) throw new JobException("A frame coordinate is not a valid number.");
            CheckPoint(p, " (frame)");
        }
        var tag = Machine.GcodeTag;
        var lines = new List<string>
        {
            $"# {tag} HEAD", "G0 F180000", "M4 S0", "G1 F180000", "M114 S1", "", "",
            $"# {tag} VECTOR HEAD", "# motion_start", "", "G21", "G90", "", "", "", "",
            "# blockConfig={\"powerFactor\":0.064,\"isVector\":true,\"crossDot\":false}",
            $"G0X{Trim3(outline[0].X)}Y{Trim3(outline[0].Y)}",
        };
        for (int i = 1; i < outline.Count; i++)
            lines.Add($"G1X{Trim3(outline[i].X)}Y{Trim3(outline[i].Y)}" + (i == 1 ? "S64F960000" : ""));
        lines.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        lines.AddRange(Tail(tag));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Rough duration in seconds (ignores acceleration; galvo jumps at
    /// 3000 mm/s). Good for comparing settings and showing progress.
    /// </summary>
    public double EstimateSeconds()
    {
        const double Jump = 3000;
        double BlockTime(Block b) => b switch
        {
            VectorBlock v => v.Path.Length / v.Laser.Speed + (v.Path.Points.Count > 0 ? 0.002 : 0) + 20 / Jump,
            FillBlock f => FillLines(f).Sum(s =>
                Math.Abs(s.X1 - s.X0) / f.Laser.Speed + 10 / f.LinesPerCm / f.Laser.Speed),
            DotBlock d => d.Bitmap.Bits.Count(v => v) *
                (d.DotUs * 1e-6 + 25.4 / d.Dpi / Math.Max(1, d.Laser.Speed)),
            _ => 0,
        };
        double work = Blocks.Sum(b => BlockTime(b) * b.Laser.Passes);
        return PassMode is PassMode.Rounds r ? work + r.DelayS * Math.Max(0, Rounds - 1) : work;
    }

    static string[] Tail(string tag) =>
        ["# END", $"# {tag} TAIL", "G90", "G0 S0", "G1 F180000", "M536 U0", "M6 P1", ""];

    internal static List<string> Dwell(double delayS)
    {
        long ms = (long)Math.Round(delayS * 1000, MidpointRounding.AwayFromZero);
        var o = new List<string>();
        while (ms > 0)
        {
            long chunk = Math.Min(ms, MaxDwellMs);
            o.Add($"G4P{chunk}");
            ms -= chunk;
        }
        return o;
    }

    static List<string> SourceLines(LaserSettings l, bool vector)
    {
        var v = new List<string> { "G4P1", l.Light == LightSource.Red ? "G22" : "G21" };
        if (vector) v.Add("G90");
        if (l.Light == LightSource.Red)
        {
            v.Add($"G0H{l.PulseWidthNs}");
            v.Add($"G0Q{l.FrequencyKhz}");
        }
        v.AddRange(["", "G4P1"]);
        return v;
    }

    static List<string> PassLoop(int passes, List<string> body)
    {
        if (passes <= 1) return body;
        var v = new List<string> { "# MUST", $"G104 P{passes}", "" };
        v.AddRange(body);
        v.AddRange(["", "# MUST", "G105"]);
        return v;
    }

    static List<string> RenderBlock(Block block, string tag, int passes) => block switch
    {
        VectorBlock b => RenderVector(b, tag, passes),
        FillBlock b => RenderFill(b, tag, passes),
        DotBlock b => RenderDots(b, tag, passes),
        _ => throw new ArgumentException("unknown block type", nameof(block)),
    };

    static List<string> RenderVector(VectorBlock b, string tag, int passes)
    {
        var pts = b.Path.EmittedPoints();
        if (pts.Count < 2) return []; // nothing to burn
        var l = b.Laser;
        var body = new List<string>
        {
            $"# blockConfig={{\"powerFactor\":{JsRounded(l.Power / 100, 6)},\"isVector\":true,\"crossDot\":false}}",
            $"G0X{Trim3(pts[0].X)}Y{Trim3(pts[0].Y)}",
        };
        for (int i = 1; i < pts.Count; i++)
            body.Add($"G1X{Trim3(pts[i].X)}Y{Trim3(pts[i].Y)}" + (i == 1 ? $"S{l.SValue}F{l.FValue}" : ""));
        var v = new List<string> { $"# {tag} VECTOR HEAD", "# motion_start" };
        v.AddRange(SourceLines(l, true));
        v.AddRange(["M523P40", "", ""]);
        v.AddRange(PassLoop(passes, body));
        v.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        return v;
    }

    /// <summary>Scanline spans inside the polygons (even-odd) at height <paramref name="y"/>.</summary>
    static List<(double A, double B)> ScanSpans(IReadOnlyList<IReadOnlyList<Vec2>> polygons, double y)
    {
        var xs = new List<double>();
        foreach (var poly in polygons)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var (a, b) = (poly[i], poly[(i + 1) % n]);
                if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                    xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
        }
        xs.Sort();
        var spans = new List<(double, double)>(xs.Count / 2);
        for (int i = 0; i + 1 < xs.Count; i += 2)
            if (xs[i + 1] - xs[i] > 1e-6) spans.Add((xs[i], xs[i + 1]));
        return spans;
    }

    /// <summary>One burn segment of a fill: at height Y from X0 to X1 (in travel direction).</summary>
    public readonly record struct FillSegment(double Y, double X0, double X1);

    /// <summary>Every burn segment of a fill, in emission order.</summary>
    public static List<FillSegment> FillLines(FillBlock b)
    {
        double step = 10 / b.LinesPerCm;
        var o = new List<FillSegment>();
        if (!(step > 0) || !double.IsFinite(step)) return o;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (var p in b.Polygons.SelectMany(x => x))
        {
            y0 = Math.Min(y0, p.Y);
            y1 = Math.Max(y1, p.Y);
        }
        if (!(y1 >= y0)) return o;
        long nLines = (long)Math.Floor((y1 - y0) / step + 1e-9);
        bool forward = true;
        for (long i = 0; i < nLines; i++)
        {
            double y = y0 + i * step;
            var spans = ScanSpans(b.Polygons, y + 1e-9);
            if (spans.Count == 0) continue;
            if (forward)
                foreach (var (a, c) in spans) o.Add(new(y, a, c));
            else
                for (int k = spans.Count - 1; k >= 0; k--) o.Add(new(y, spans[k].B, spans[k].A));
            forward = !forward;
        }
        return o;
    }

    static string BitmapCfg(double powerFactor, double density, long pmin, long pmax, string mode) =>
        $"# blockConfig={{\"powerFactor\":{JsRounded(powerFactor, 6)},\"overDist\":0,\"accel\":5000,\"density\":{JsRounded(density, 4)},\"power\":[{pmin},{pmax}],\"bitmapMode\":\"{mode}\",\"crossDot\":false,\"startX\":0,\"isVector\":false}}";

    static List<string> BitmapHead(string tag, LaserSettings l, string modeCode)
    {
        var v = new List<string> { $"# {tag} BITMAP HEAD", "# motion_start", "", "", "" };
        v.AddRange(SourceLines(l, false));
        v.AddRange([modeCode, "", ""]);
        return v;
    }

    static string[] BitmapTail(string tag) => ["", $"# {tag} BITMAP TAIL", "# motion_end", ""];

    static List<string> RenderFill(FillBlock b, string tag, int passes)
    {
        var segs = FillLines(b);
        if (segs.Count == 0) return [];
        var l = b.Laser;
        long s = l.SValue;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.LinesPerCm / 10, 0, s, "grayscale"), "", "", $"G1F{l.FValue}S0",
        };
        string? lastX = null;
        foreach (var seg in segs)
        {
            string ya = Fixed3(seg.Y), xa = Fixed3(seg.X0);
            if (lastX is null) body.Add($"G0X{xa}Y{ya}");
            else if (lastX == xa) body.Add($"G1Y{ya}S0");
            else body.Add($"G1X{xa}Y{ya}S0");
            var xb = Fixed3(seg.X1);
            body.Add($"G1X{xb}S{s}");
            lastX = xb;
        }
        var v = BitmapHead(tag, l, "M523P40");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    static List<string> RenderDots(DotBlock b, string tag, int passes)
    {
        var l = b.Laser;
        double pitch = 25.4 / b.Dpi;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.Dpi / 25.4,
                (long)Math.Round(b.MinPower * 10, MidpointRounding.AwayFromZero), l.SValue, b.Mode),
            "", "", $"G1F{l.FValue}S0", $"G25T{b.DotUs / 10}",
        };
        bool first = true, forward = true;
        string lastX = "";
        var cols = new List<int>();
        for (int r = 0; r < b.Bitmap.Height; r++)
        {
            cols.Clear();
            for (int c = 0; c < b.Bitmap.Width; c++)
                if (b.Bitmap[c, r]) cols.Add(c);
            if (cols.Count == 0) continue;
            if (!forward) cols.Reverse();
            var y = Fixed3(b.Origin.Y + r * pitch);
            for (int k = 0; k < cols.Count; k++)
            {
                var x = Fixed3(b.Origin.X + cols[k] * pitch);
                if (first)
                {
                    body.Add($"G0X{x}Y{y}");
                    body.Add("");
                    body.Add($"G25X{x}Y{y}S{l.SValue}");
                    first = false;
                }
                else if (k == 0) body.Add(x == lastX ? $"G25Y{y}" : $"G25X{x}Y{y}");
                else body.Add($"G25X{x}");
                lastX = x;
            }
            forward = !forward;
        }
        if (first) return []; // blank bitmap
        var v = BitmapHead(tag, l, "M523P10");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }
}
