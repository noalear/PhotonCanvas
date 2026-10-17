using static PhotonCanvas.Core.NumFmt;

namespace PhotonCanvas.Core;

// G-code for the xTool F2 Ultra galvo, in the dialect documented in
// docs/gcode-f2-ultra.md and verified against four real exports
// (tests/PhotonCanvas.Tests/SampleTests.cs).
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

public abstract record Block(LaserSettings Laser)
{
    /// <summary>
    /// Move the focus this far into the material for this block (mm): + lowers the head, − raises
    /// it. The head moves only when the offset changes from one block to the next, and goes back
    /// to where it started at the end of the job.
    /// </summary>
    public double FocusOffsetMm { get; init; }
}

public sealed record VectorBlock(Polyline Path, LaserSettings Laser) : Block(Laser)
{
    /// <summary>Lower the head between passes (for cutting thick material); null = keep the focus.</summary>
    public StepDown? Drop { get; init; }
}

/// <summary>
/// Lower the head by <see cref="FirstMm"/> before the first pass and by <see cref="PerPassMm"/>
/// before each later pass, so the focus follows the cut down into thick material. The head goes
/// back up by the total afterwards. Encoded as xTool Studio does (relative Z between
/// <c>G102</c>/<c>G103</c>, 10 mm/s), checked against its exports.
/// </summary>
public sealed record StepDown(double FirstMm, double PerPassMm)
{
    public const double MaxPerPassMm = 2, MaxTotalMm = 10;

    /// <summary>How far the head is lowered for pass <paramref name="pass"/> (1-based).</summary>
    public double DepthFor(int pass) => FirstMm + (pass - 1) * PerPassMm;

    public void Validate(int passes)
    {
        JobException.CheckRange("first step down", FirstMm, 0, MaxPerPassMm, "mm");
        JobException.CheckRange("step down per pass", PerPassMm, 0, MaxPerPassMm, "mm");
        if (DepthFor(passes) > MaxTotalMm + 1e-9)
            throw new JobException($"Stepping down {PerPassMm:0.##} mm for each of {passes} passes lowers the head {DepthFor(passes):0.##} mm in total; the limit is {MaxTotalMm} mm.");
    }
}

/// <summary>
/// Fiber "wobble": the beam traces small circles while moving along the path, for wider,
/// smoother marks and cuts. Generated as xTool Studio does (checked point by point against its
/// export): 36 points per loop turning 10° each, starting at −100°, the loop centre advancing
/// <c>spacing / 36</c> along the path per point.
/// </summary>
public static class Wobble
{
    public const int PointsPerLoop = 36;
    public const double MinDiameterMm = 0.01, MaxDiameterMm = 5, MinSpacingMm = 0.005, MaxSpacingMm = 5;

    public static List<Vec2> Expand(IReadOnlyList<Vec2> path, double diameterMm, double spacingMm)
    {
        if (path.Count < 2) return [.. path];
        var cum = new double[path.Count];
        for (int i = 1; i < path.Count; i++) cum[i] = cum[i - 1] + path[i - 1].Dist(path[i]);
        double total = cum[^1], r = diameterMm / 2, step = spacingMm / PointsPerLoop;
        long n = (long)Math.Ceiling(total / step);
        var o = new List<Vec2>((int)Math.Min(n + 1, int.MaxValue / 2));
        int seg = 1;
        for (long i = 0; i <= n; i++)
        {
            double s = Math.Min(i * step, total);
            while (seg < path.Count - 1 && cum[seg] < s) seg++;
            double len = cum[seg] - cum[seg - 1];
            var c = len < 1e-12 ? path[seg] : path[seg - 1] + (path[seg] - path[seg - 1]) * ((s - cum[seg - 1]) / len);
            double a = (-100 - 10.0 * i) * Math.PI / 180;
            o.Add(new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
        }
        return o;
    }
}

/// <summary>
/// A grid of cells to fill, square, in line with the machine's axes: cell (0, 0) has its
/// top-left corner at <see cref="Origin"/>, rows run down. Used for depth slices, where a
/// picture's area is far too fiddly to trace into outlines.
/// </summary>
public sealed class FillMask
{
    readonly byte[] _levels;
    readonly int _threshold;
    Rect2? _filled;

    /// <summary>Cells that are on: <c>true</c> = fill.</summary>
    public FillMask(Vec2 origin, double cellMm, int width, int height, bool[] cells)
        : this(origin, cellMm, width, height, cells.Select(c => c ? (byte)1 : (byte)0).ToArray(), 1) { }

    /// <summary>
    /// Cells whose level is at least <paramref name="threshold"/> are on. Several masks can share
    /// one level array (every slice of a depth map uses the same one).
    /// </summary>
    public FillMask(Vec2 origin, double cellMm, int width, int height, byte[] levels, int threshold)
    {
        Origin = origin;
        CellMm = cellMm;
        Width = width;
        Height = height;
        _levels = levels;
        _threshold = Math.Max(1, threshold);
    }

    public Vec2 Origin { get; }
    public double CellMm { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>True when the level data matches the size.</summary>
    public bool Valid => _levels.Length == Width * Height && Width > 0 && Height > 0;

    /// <summary>Is cell <paramref name="i"/> (row-major) on?</summary>
    public bool this[int i] => _levels[i] >= _threshold;

    public Rect2 Bounds => new(Origin.X, Origin.Y, Origin.X + Width * CellMm, Origin.Y + Height * CellMm);

    public bool Any() => !FilledBounds().IsEmpty;

    /// <summary>How many cells are on.</summary>
    public int Count()
    {
        int n = 0;
        for (int i = 0; i < Width * Height; i++) if (_levels[i] >= _threshold) n++;
        return n;
    }

    /// <summary>The smallest rectangle around the filled cells (empty if none).</summary>
    public Rect2 FilledBounds() => _filled ??= FindFilled();

    Rect2 FindFilled()
    {
        if (!Valid) return Rect2.Empty;
        int c0 = Width, c1 = -1, r0 = Height, r1 = -1;
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
                if (_levels[r * Width + c] >= _threshold)
                {
                    if (c < c0) c0 = c;
                    if (c > c1) c1 = c;
                    if (r < r0) r0 = r;
                    r1 = r;
                }
        return c1 < 0 ? Rect2.Empty : new(Origin.X + c0 * CellMm, Origin.Y + r0 * CellMm, Origin.X + (c1 + 1) * CellMm, Origin.Y + (r1 + 1) * CellMm);
    }
}

/// <summary>Zigzag line fill of closed polygons, even-odd rule (holes work), or of a <see cref="FillMask"/>.</summary>
public sealed record FillBlock(IReadOnlyList<IReadOnlyList<Vec2>> Polygons, LaserSettings Laser, double LinesPerCm)
    : Block(Laser)
{
    /// <summary>Fill these cells instead of <see cref="Polygons"/> (depth slices).</summary>
    public FillMask? Mask { get; init; }

    /// <summary>
    /// Turn the scan lines this much more on every repeat of the block (degrees), so the line
    /// pattern doesn't build up into stripes on deep or repeated engraving. 0 = every pass the same.
    /// </summary>
    public double AngleStepDeg { get; init; }

    /// <summary>The block as run on pass <paramref name="pass"/> (1-based): turned by <see cref="AngleStepDeg"/> per earlier pass.</summary>
    public FillBlock ForPass(int pass) => AngleStepDeg == 0 || pass <= 1 ? this : this with { AngleDeg = AngleDeg + (pass - 1) * AngleStepDeg, AngleStepDeg = 0 };

    /// <summary>
    /// Densest fill: 0.001 mm between lines, the finest the G-code's coordinates can express. The
    /// stock software stops at 0.002 mm; finer works the same way, it just puts in more heat and time.
    /// </summary>
    public const double MaxLinesPerCm = 10000;

    /// <summary>
    /// Direction of the scan lines in degrees, turning the same way as the
    /// rotate tool (0 = left–right, 90 = top–bottom). Only the line direction
    /// matters, so 180° is the same as 0°.
    /// </summary>
    public double AngleDeg { get; init; }

    /// <summary>
    /// Laser-off run-up before and run-out after every scan line (mm), so the
    /// beam is already moving at speed where the burn starts and ends. The
    /// burned part of each line is unchanged.
    /// </summary>
    public double OverscanMm { get; init; }
}

/// <summary>
/// Offset fill: closed loops (outlines stepped inward), burned in this order in one block with
/// the laser off between them.
/// </summary>
public sealed record LoopsBlock(IReadOnlyList<IReadOnlyList<Vec2>> Loops, LaserSettings Laser, double SpacingMm) : Block(Laser)
{
    /// <summary>Total length of the loops, mm.</summary>
    public double Length
    {
        get
        {
            double l = 0;
            foreach (var p in Loops)
                for (int i = 0; i < p.Count; i++) l += p[i].Dist(p[(i + 1) % p.Count]);
            return l;
        }
    }
}

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
    : Block(Laser)
{
    /// <summary>
    /// Grayscale engraving: a strength of 0–255 for every pixel, laid out like
    /// <see cref="Bitmap"/>. A burned pixel fires at
    /// <see cref="MinPower"/> + (power − <see cref="MinPower"/>) × level / 255,
    /// so 0 is the lightest tone that still burns and 255 is the layer's full power.
    /// Null: every dot fires at the layer's power (dithered engraving).
    /// </summary>
    public byte[]? Levels { get; init; }

    /// <summary>The <c>S</c> value (per-mille) of a dot with strength <paramref name="level"/>.</summary>
    public long LevelSValue(byte level) =>
        (long)Math.Round((MinPower + (Laser.Power - MinPower) * level / 255.0) * 10, MidpointRounding.AwayFromZero);
}

public sealed class Job
{
    /// <summary>
    /// Longest single dwell we emit; longer pauses are split into several
    /// <c>G4</c>s so no firmware integer limit is ever approached.
    /// </summary>
    const long MaxDwellMs = 60_000;

    /// <summary>Longest pause between passes we accept.</summary>
    public const double MaxPassDelayS = 24 * 3600;

    /// <summary>Longest fill overscan we accept (mm).</summary>
    public const double MaxOverscanMm = 50;

    /// <summary>Furthest the focus may be moved either way for a block (mm).</summary>
    public const double MaxFocusOffsetMm = 10;

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

    /// <summary>How many whole-job rounds run (the most passes of any block).</summary>
    public int Rounds => Blocks.Count == 0 ? 1 : Blocks.Max(b => b.Laser.Passes);

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
            if (!double.IsFinite(block.FocusOffsetMm) || Math.Abs(block.FocusOffsetMm) > MaxFocusOffsetMm + 1e-9)
                throw new JobException($"The focus can move at most {MaxFocusOffsetMm:0} mm either way (asked for {block.FocusOffsetMm:0.##} mm).");
            switch (block)
            {
                case VectorBlock b:
                    b.Path.Validate();
                    b.Drop?.Validate(PassMode is PassMode.Rounds ? Rounds : b.Laser.Passes);
                    var pts = b.Path.EmittedPoints();
                    anyMotion |= pts.Count >= 2;
                    foreach (var p in pts) CheckPoint(p, ctx);
                    break;
                case FillBlock b:
                    JobException.CheckRange("line density", b.LinesPerCm, 1, FillBlock.MaxLinesPerCm, "lines/cm");
                    if (!double.IsFinite(b.AngleDeg) || !double.IsFinite(b.AngleStepDeg)) throw new JobException("The fill angle is not a valid number.");
                    JobException.CheckRange("overscan", b.OverscanMm, 0, MaxOverscanMm, "mm");
                    foreach (var p in b.Polygons.SelectMany(x => x))
                    {
                        if (!p.IsFinite) throw new JobException("A fill coordinate is not a valid number.");
                        CheckPoint(p, ctx);
                    }
                    if (b.Mask is { } mask)
                    {
                        if (!mask.Valid || !(mask.CellMm > 0) || !mask.Origin.IsFinite)
                            throw new JobException("A depth slice's cells don't match its size.");
                        var mb = mask.FilledBounds();
                        if (!mb.IsEmpty)
                        {
                            CheckPoint(mb.TopLeft, ctx);
                            CheckPoint(mb.BottomRight, ctx);
                        }
                    }
                    var lines = FillLines(b);
                    if (b.OverscanMm > 0)
                        foreach (var seg in lines)
                        {
                            var (runUp, runOut) = Overscan(seg, b.OverscanMm);
                            CheckPoint(runUp, $" (fill overscan, item {i + 1})");
                            CheckPoint(runOut, $" (fill overscan, item {i + 1})");
                        }
                    anyMotion |= lines.Count > 0;
                    break;
                case LoopsBlock b:
                    foreach (var p in b.Loops.SelectMany(x => x))
                    {
                        if (!p.IsFinite) throw new JobException("A fill coordinate is not a valid number.");
                        CheckPoint(p, ctx);
                    }
                    anyMotion |= b.Loops.Any(l => l.Count >= 2);
                    break;
                case DotBlock b:
                    JobException.CheckRange("resolution", b.Dpi, 10, 2540, "dpi");
                    JobException.CheckRange("dot duration", b.DotUs, 10, 100_000, "µs");
                    if (b.DotUs % 10 != 0)
                        throw new JobException($"Dot duration must be a multiple of 10 µs (got {b.DotUs} µs).");
                    JobException.CheckRange("minimum power", b.MinPower, 0, b.Laser.Power, "%");
                    if (b.Bitmap.Bits.Length != b.Bitmap.Width * b.Bitmap.Height)
                        throw new JobException("Bitmap size does not match its data.");
                    if (b.Levels is { } levels && levels.Length != b.Bitmap.Bits.Length)
                        throw new JobException("The grey levels do not match the bitmap size.");
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
        double focus = 0; // how far the head is lowered now (mm)
        void Focus(Block b)
        {
            if (Math.Abs(b.FocusOffsetMm - focus) < 1e-6) return;
            lines.AddRange(ZMove(-(b.FocusOffsetMm - focus)));
            lines.AddRange(["# MUST", ""]);
            focus = b.FocusOffsetMm;
        }
        switch (PassMode)
        {
            case PassMode.PerBlock:
                foreach (var b in Blocks)
                {
                    Focus(b);
                    if (b is FillBlock { AngleStepDeg: not 0 } f && f.Laser.Passes > 1)
                        // Each pass turned a little further: written out, the device loop repeats one pattern.
                        for (int p = 1; p <= f.Laser.Passes; p++)
                        {
                            lines.AddRange(RenderBlock(f.ForPass(p), tag, 1));
                            lines.Add("");
                        }
                    else
                    {
                        lines.AddRange(RenderBlock(b, tag, b.Laser.Passes));
                        lines.Add("");
                    }
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
                        Focus(b);
                        lines.AddRange(RenderBlock(b is FillBlock f ? f.ForPass(round) : b, tag, 1, round));
                        lines.Add("");
                    }
                }
                break;
        }
        if (Math.Abs(focus) > 1e-6)
        {
            // Back to the focus the job started at.
            lines.AddRange(ZMove(focus));
            lines.AddRange(["# MUST", ""]);
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
        double work = Blocks.Sum(b => BlockSeconds(b) * b.Laser.Passes);
        return PassMode is PassMode.Rounds r ? work + r.DelayS * Math.Max(0, Rounds - 1) : work;
    }

    /// <summary>Rough time for one pass of a block, in seconds (same model as <see cref="EstimateSeconds"/>).</summary>
    public static double BlockSeconds(Block b)
    {
        const double Jump = 3000;
        return b switch
        {
            VectorBlock v => v.Path.Length / v.Laser.Speed + (v.Path.Points.Count > 0 ? 0.002 : 0) + 20 / Jump,
            FillBlock f => FillLines(f).Sum(s =>
                (s.Start.Dist(s.End) + 2 * f.OverscanMm) / f.Laser.Speed + 10 / f.LinesPerCm / f.Laser.Speed),
            LoopsBlock l => l.Length / l.Laser.Speed + l.Loops.Count * (0.0005 + 5 / Jump),
            DotBlock d => d.Bitmap.Bits.Count(v => v) *
                (d.DotUs * 1e-6 + 25.4 / d.Dpi / Math.Max(1, d.Laser.Speed)),
            _ => 0,
        };
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

    static List<string> RenderBlock(Block block, string tag, int passes, int round = 1) => block switch
    {
        VectorBlock { Drop: not null } b => RenderSteppedVector(b, tag, passes, round),
        VectorBlock b => RenderVector(b, tag, passes),
        FillBlock b => RenderFill(b, tag, passes),
        LoopsBlock b => RenderLoops(b, tag, passes),
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

    /// <summary>Relative Z move of the head (negative = down), as xTool Studio writes it.</summary>
    static string[] ZMove(double mm) => ["G102", "G91", $"G0Z{Trim3(mm)}F600", "G90", "G103", "G0F180000"];

    /// <summary>
    /// A cut with step-down. With <paramref name="passes"/> &gt; 1 (per-path passes) the passes are
    /// written out one after another, the head lowered before each and raised by the total at the
    /// end, exactly like xTool Studio. With one pass (whole-job rounds) the head is lowered to this
    /// round's depth and raised again straight after, so other layers keep their focus.
    /// </summary>
    static List<string> RenderSteppedVector(VectorBlock b, string tag, int passes, int round)
    {
        var pts = b.Path.EmittedPoints();
        if (pts.Count < 2) return [];
        var l = b.Laser;
        var drop = b.Drop!;
        var path = new List<string> { $"G0X{Trim3(pts[0].X)}Y{Trim3(pts[0].Y)}" };
        for (int i = 1; i < pts.Count; i++)
            path.Add($"G1X{Trim3(pts[i].X)}Y{Trim3(pts[i].Y)}" + (i == 1 ? $"S{l.SValue}F{l.FValue}" : ""));
        var v = new List<string> { $"# {tag} VECTOR HEAD", "# motion_start" };
        v.AddRange(SourceLines(l, true));
        v.AddRange(["M523P40", "", ""]);
        v.Add($"# blockConfig={{\"powerFactor\":{JsRounded(l.Power / 100, 6)},\"isVector\":true,\"crossDot\":false}}");
        double first = passes > 1 ? drop.FirstMm : drop.DepthFor(round);
        if (first > 0) v.AddRange(ZMove(-first));
        v.AddRange(["# MUST", path[0]]);
        v.AddRange(path);
        for (int p = 2; p <= passes; p++)
        {
            if (drop.PerPassMm > 0) v.AddRange(ZMove(-drop.PerPassMm));
            v.AddRange(path);
        }
        v.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        double back = passes > 1 ? drop.DepthFor(passes) : first;
        if (back > 0) v.AddRange([.. ZMove(back), "# MUST"]);
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
        {
            if (xs[i + 1] - xs[i] <= 1e-6) continue;
            // Shapes that touch (pixels side by side) make one continuous line, not a stop-start at each edge.
            if (spans.Count > 0 && xs[i] - spans[^1].Item2 <= 1e-6) spans[^1] = (spans[^1].Item1, xs[i + 1]);
            else spans.Add((xs[i], xs[i + 1]));
        }
        return spans;
    }

    /// <summary>
    /// One burn segment of a fill. <see cref="Y"/>, <see cref="X0"/> and
    /// <see cref="X1"/> are in the scan frame (the shape turned back by the
    /// fill angle, so lines run along X); <see cref="Start"/> and
    /// <see cref="End"/> are machine positions, in travel direction.
    /// </summary>
    public readonly record struct FillSegment(double Y, double X0, double X1, Vec2 Start, Vec2 End);

    /// <summary>Fill angle folded into [0, 180) degrees; anything invalid counts as 0.</summary>
    static double FoldAngle(double deg)
    {
        if (!double.IsFinite(deg)) return 0;
        double a = deg % 180;
        if (a < 0) a += 180;
        return a >= 180 ? 0 : a;
    }

    /// <summary>Lines already worked out, per block instance (blocks are immutable; <c>with</c> makes a new one).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FillBlock, List<FillSegment>> LineCache = new();

    /// <summary>Every burn segment of a fill, in emission order. The list is shared: don't change it.</summary>
    public static List<FillSegment> FillLines(FillBlock b) => LineCache.GetValue(b, MakeFillLines);

    static List<FillSegment> MakeFillLines(FillBlock b)
    {
        double step = 10 / b.LinesPerCm;
        var o = new List<FillSegment>();
        if (!(step > 0) || !double.IsFinite(step)) return o;
        if (b.Mask is { } mask) return MaskLines(mask, step, FoldAngle(b.AngleDeg));
        double angle = FoldAngle(b.AngleDeg);
        if (angle == 0)
        {
            foreach (var (y, a, c) in Scan(b.Polygons, step))
                o.Add(new(y, a, c, new(a, y), new(c, y)));
            return o;
        }
        // Turn the shapes back by the angle, scan along X, turn the lines forward again.
        double rad = angle * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        var turned = b.Polygons
            .Select(p => (IReadOnlyList<Vec2>)p.Select(v => new Vec2(v.X * cos + v.Y * sin, -v.X * sin + v.Y * cos)).ToList())
            .ToList();
        Vec2 Back(double x, double y) => new(x * cos - y * sin, x * sin + y * cos);
        foreach (var (y, a, c) in Scan(turned, step))
            o.Add(new(y, a, c, Back(a, y), Back(c, y)));
        return o;
    }

    /// <summary>
    /// Zigzag lines across a cell mask at <paramref name="angle"/> degrees, <paramref name="step"/> mm
    /// apart: each line walks the cells it crosses and burns exactly over the filled ones.
    /// </summary>
    static List<FillSegment> MaskLines(FillMask m, double step, double angle)
    {
        var o = new List<FillSegment>();
        var box = m.FilledBounds();
        if (box.IsEmpty) return o;
        double rad = angle * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        // The scan frame: lines run along x' and step along y'.
        Vec2[] corners = [box.TopLeft, new(box.MaxX, box.MinY), box.BottomRight, new(box.MinX, box.MaxY)];
        double y0 = corners.Min(v => -v.X * sin + v.Y * cos), y1 = corners.Max(v => -v.X * sin + v.Y * cos);
        double x0 = corners.Min(v => v.X * cos + v.Y * sin) - 1, x1 = corners.Max(v => v.X * cos + v.Y * sin) + 1;
        Vec2 Back(double x, double y) => new(x * cos - y * sin, x * sin + y * cos);
        var dir = new Vec2(cos, sin);
        long n = (long)Math.Floor((y1 - y0) / step + 1e-9);
        var spans = new List<(double, double)>();
        bool forward = true;
        for (long i = 0; i <= n; i++)
        {
            // Half a line in, so no line grazes a cell edge exactly.
            double y = y0 + (i + 0.5) * step;
            if (y > y1) break;
            spans.Clear();
            MaskSpans(m, Back(x0, y), dir, 0, x1 - x0, spans);
            if (spans.Count == 0) continue;
            if (forward)
                foreach (var (a, c) in spans) o.Add(new(y, x0 + a, x0 + c, Back(x0 + a, y), Back(x0 + c, y)));
            else
                for (int k = spans.Count - 1; k >= 0; k--)
                {
                    var (a, c) = spans[k];
                    o.Add(new(y, x0 + c, x0 + a, Back(x0 + c, y), Back(x0 + a, y)));
                }
            forward = !forward;
        }
        return o;
    }

    /// <summary>
    /// Stretches (t from, t to) of the line <paramref name="p0"/> + t·<paramref name="d"/>, t in
    /// [<paramref name="t0"/>, <paramref name="t1"/>], that lie over filled cells: the cells are
    /// walked in the order the line crosses them (Amanatides–Woo).
    /// </summary>
    static void MaskSpans(FillMask m, Vec2 p0, Vec2 d, double t0, double t1, List<(double, double)> spans)
    {
        var b = m.Bounds;
        double lo = t0, hi = t1;
        bool Slab(double p, double dv, double min, double max)
        {
            if (Math.Abs(dv) < 1e-12) return p >= min && p < max;
            double ta = (min - p) / dv, tb = (max - p) / dv;
            lo = Math.Max(lo, Math.Min(ta, tb));
            hi = Math.Min(hi, Math.Max(ta, tb));
            return true;
        }
        if (!Slab(p0.X, d.X, b.MinX, b.MaxX) || !Slab(p0.Y, d.Y, b.MinY, b.MaxY) || hi - lo <= 1e-9) return;
        double c = m.CellMm;
        double mid = lo + 1e-9;
        var s = p0 + d * mid;
        int i = Math.Clamp((int)Math.Floor((s.X - m.Origin.X) / c), 0, m.Width - 1);
        int j = Math.Clamp((int)Math.Floor((s.Y - m.Origin.Y) / c), 0, m.Height - 1);
        int stepX = d.X > 0 ? 1 : -1, stepY = d.Y > 0 ? 1 : -1;
        double tDeltaX = Math.Abs(d.X) < 1e-12 ? double.PositiveInfinity : c / Math.Abs(d.X);
        double tDeltaY = Math.Abs(d.Y) < 1e-12 ? double.PositiveInfinity : c / Math.Abs(d.Y);
        double tMaxX = Math.Abs(d.X) < 1e-12 ? double.PositiveInfinity : (m.Origin.X + (i + (stepX > 0 ? 1 : 0)) * c - p0.X) / d.X;
        double tMaxY = Math.Abs(d.Y) < 1e-12 ? double.PositiveInfinity : (m.Origin.Y + (j + (stepY > 0 ? 1 : 0)) * c - p0.Y) / d.Y;
        double t = lo, start = 0;
        bool inside = false;
        while (t < hi)
        {
            bool on = m[j * m.Width + i];
            if (on && !inside) { inside = true; start = t; }
            else if (!on && inside)
            {
                inside = false;
                if (t - start > 1e-6) spans.Add((start, t));
            }
            double next = Math.Min(Math.Min(tMaxX, tMaxY), hi);
            t = next;
            if (t >= hi) break;
            if (tMaxX < tMaxY) { i += stepX; tMaxX += tDeltaX; }
            else { j += stepY; tMaxY += tDeltaY; }
            if (i < 0 || i >= m.Width || j < 0 || j >= m.Height) break;
        }
        if (inside && Math.Min(t, hi) - start > 1e-6) spans.Add((start, Math.Min(t, hi)));
    }

    /// <summary>Zigzag scan lines (height, from, to) along X, <paramref name="step"/> mm apart.</summary>
    static List<(double Y, double X0, double X1)> Scan(IReadOnlyList<IReadOnlyList<Vec2>> polygons, double step)
    {
        var o = new List<(double, double, double)>();
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (var p in polygons.SelectMany(x => x))
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
            var spans = ScanSpans(polygons, y + 1e-9);
            if (spans.Count == 0) continue;
            if (forward)
                foreach (var (a, c) in spans) o.Add((y, a, c));
            else
                for (int k = spans.Count - 1; k >= 0; k--) o.Add((y, spans[k].B, spans[k].A));
            forward = !forward;
        }
        return o;
    }

    /// <summary>Where the laser-off run-up of a fill line starts and where its run-out ends.</summary>
    static (Vec2 RunUp, Vec2 RunOut) Overscan(FillSegment seg, double mm)
    {
        var d = seg.End - seg.Start;
        double len = d.Length;
        var u = len > 0 ? d / len : new Vec2(1, 0);
        return (seg.Start - u * mm, seg.End + u * mm);
    }

    static string BitmapCfg(double powerFactor, double density, long pmin, long pmax, string mode, double overDist = 0) =>
        $"# blockConfig={{\"powerFactor\":{JsRounded(powerFactor, 6)},\"overDist\":{JsRounded(overDist, 3)},\"accel\":5000,\"density\":{JsRounded(density, 4)},\"power\":[{pmin},{pmax}],\"bitmapMode\":\"{mode}\",\"crossDot\":false,\"startX\":0,\"isVector\":false}}";

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
        double over = b.OverscanMm;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.LinesPerCm / 10, 0, s, "grayscale", over), "", "", $"G1F{l.FValue}S0",
        };
        if (FoldAngle(b.AngleDeg) == 0)
        {
            // Horizontal lines: only the coordinates that change are written, as xTool does.
            string? lastX = null;
            foreach (var seg in segs)
            {
                double dir = seg.X1 >= seg.X0 ? 1 : -1;
                string ya = Fixed3(seg.Y), xa = Fixed3(seg.X0 - dir * over);
                if (lastX is null) body.Add($"G0X{xa}Y{ya}");
                else if (lastX == xa) body.Add($"G1Y{ya}S0");
                else body.Add($"G1X{xa}Y{ya}S0");
                if (over > 0) body.Add($"G1X{Fixed3(seg.X0)}S0");
                lastX = Fixed3(seg.X1);
                body.Add($"G1X{lastX}S{s}");
                if (over > 0)
                {
                    lastX = Fixed3(seg.X1 + dir * over);
                    body.Add($"G1X{lastX}S0");
                }
            }
        }
        else
        {
            string? lastX = null, lastY = null;
            foreach (var seg in segs)
            {
                var (runUp, runOut) = Overscan(seg, over);
                string xa = Fixed3(runUp.X), ya = Fixed3(runUp.Y);
                if (lastX is null) body.Add($"G0X{xa}Y{ya}");
                else if (lastX != xa || lastY != ya) body.Add($"G1X{xa}Y{ya}S0");
                if (over > 0) body.Add($"G1X{Fixed3(seg.Start.X)}Y{Fixed3(seg.Start.Y)}S0");
                (lastX, lastY) = (Fixed3(seg.End.X), Fixed3(seg.End.Y));
                body.Add($"G1X{lastX}Y{lastY}S{s}");
                if (over > 0)
                {
                    (lastX, lastY) = (Fixed3(runOut.X), Fixed3(runOut.Y));
                    body.Add($"G1X{lastX}Y{lastY}S0");
                }
            }
        }
        var v = BitmapHead(tag, l, "M523P40");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    /// <summary>Offset-fill loops: one engraving block, each loop traced closed, the laser off between loops.</summary>
    static List<string> RenderLoops(LoopsBlock b, string tag, int passes)
    {
        var loops = b.Loops.Where(l => l.Count >= 2).ToList();
        if (loops.Count == 0) return [];
        var l = b.Laser;
        long s = l.SValue;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.SpacingMm > 0 ? 1 / b.SpacingMm : 10, 0, s, "grayscale"), "", "", $"G1F{l.FValue}S0",
        };
        bool first = true;
        foreach (var loop in loops)
        {
            string xa = Fixed3(loop[0].X), ya = Fixed3(loop[0].Y);
            body.Add(first ? $"G0X{xa}Y{ya}" : $"G1X{xa}Y{ya}S0");
            first = false;
            for (int i = 1; i <= loop.Count; i++)
            {
                var p = loop[i % loop.Count];
                body.Add($"G1X{Fixed3(p.X)}Y{Fixed3(p.Y)}S{s}");
            }
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
        long lastS = l.SValue;
        var levels = b.Levels;
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
                // S is modal: grayscale writes it only when the power changes.
                string power = "";
                if (levels is not null)
                {
                    long sv = b.LevelSValue(levels[r * b.Bitmap.Width + cols[k]]);
                    if (first || sv != lastS) power = $"S{sv}";
                    lastS = sv;
                }
                if (first)
                {
                    body.Add($"G0X{x}Y{y}");
                    body.Add("");
                    body.Add(levels is null ? $"G25X{x}Y{y}S{l.SValue}" : $"G25X{x}Y{y}{power}");
                    first = false;
                }
                else if (k == 0) body.Add((x == lastX ? $"G25Y{y}" : $"G25X{x}Y{y}") + power);
                else body.Add($"G25X{x}" + power);
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
