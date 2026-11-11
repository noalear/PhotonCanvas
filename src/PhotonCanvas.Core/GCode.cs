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

/// <summary>
/// How the design maps onto the machine. On the flat bed the galvo draws X and Y. With an
/// attachment one direction is mechanical (the "U" axis): the galvo draws the other while U turns
/// the object or moves the belt, coordinated by the machine. Worked out from xTool Studio's own
/// templates (head <c>M535U</c>/<c>G100</c>, tail <c>G0 U0</c>/<c>G101</c>): not yet run on a machine.
/// </summary>
public abstract record WorkMode
{
    public sealed record Flat : WorkMode;

    /// <summary>
    /// Rotary attachment: design X is the galvo's X; design Y goes round the object (U, in mm of
    /// its surface, 0 at <paramref name="OriginY"/>). The galvo's Y stays on one line across the object.
    /// </summary>
    /// <param name="UMultiple">Firmware scale (roller 0.59917 on an RA2, ×4 on an RA3; chuck 128 ÷ circumference).</param>
    /// <param name="Direction">+1 or −1: which way the object turns for increasing design Y.</param>
    /// <param name="LineY">The galvo's Y for that line (mm from the back of the field); null = the middle.</param>
    public sealed record Rotary(double UMultiple, int Direction, double CircumferenceMm, double OriginY = 0, double? LineY = null) : WorkMode;

    /// <summary>
    /// Conveyor feeder: design X moves the belt (U, mm; the middle of the field is U 0), design Y is
    /// the galvo's Y; the galvo's X stays at the middle.
    /// </summary>
    /// <param name="UMultiple">Firmware scale: 0.74592 × 100 ÷ the belt calibration (%).</param>
    /// <param name="AngleCompensate">The belt's angle compensation from the machine's own calibration.</param>
    /// <param name="LineX">The galvo's X for the line it draws along (mm from the left of the field); null = the middle.</param>
    public sealed record Conveyor(double UMultiple, double AngleCompensate, double LengthMm = 610, double SpeedMmMin = 9600, double? LineX = null) : WorkMode;
}

public abstract record Block(LaserSettings Laser)
{
    /// <summary>
    /// Move the focus this far into the material for this block (mm): + lowers the head, − raises
    /// it. The head moves only when the offset changes from one block to the next, and goes back
    /// to where it started at the end of the job.
    /// </summary>
    public double FocusOffsetMm { get; init; }

    /// <summary>Curved surfaces: the head's height for the whole block (absolute, machine Z), set before it runs. Null = as the job goes.</summary>
    public double? HeadZ { get; init; }

    /// <summary>Wait this long after each pass of the block, laser off (s): heat leaves between paths (thin metal, masks). 0 = none.</summary>
    public double PauseAfterS { get; init; }
}

/// <summary>Part of a path at one height of the head (curved surfaces).</summary>
public sealed record BandPiece(double HeadZ, IReadOnlyList<Vec2> Points);

/// <summary>
/// Lines on a curved surface: the paths cut into pieces by height band and run one band at a time,
/// highest first, the head moving to each band's height in between (as xTool Studio does).
/// </summary>
public sealed record BandedVectorBlock(IReadOnlyList<BandPiece> Pieces, LaserSettings Laser) : Block(Laser);

/// <summary>A fill on a curved surface: its scan lines cut by height band, one band at a time.</summary>
public sealed record BandedFillBlock(FillBlock Fill, IReadOnlyList<(double HeadZ, IReadOnlyList<Job.FillSegment> Segments)> Bands) : Block(Fill.Laser);

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
/// Wobble: the beam traces small circles while moving along the path, for wider, smoother marks
/// and a wider kerf that lets a cut clear itself (either laser: it's only how the galvo moves). Generated as xTool Studio does (checked point by point against its
/// export): 36 points per loop turning 10° each, starting at −100°, the loop centre advancing
/// <c>spacing / 36</c> along the path per point.
/// </summary>
public static class Wobble
{
    public const int PointsPerLoop = 36;
    public const double MinDiameterMm = 0.01, MaxDiameterMm = 5, MinSpacingMm = 0.005, MaxSpacingMm = 5;

    /// <summary>
    /// The most wobble moves one job may hold, every layer and pass together: about 1.9 GB of
    /// G-code, under the 2.1 GB the app can send as one job (the F2 Ultra loaded a 2 GB test job,
    /// 2026-10-26). Checked before any are made, so a job that can't be sent fails at once instead
    /// of after minutes of building. A 4 m cut wobbled at 0.015 mm, 7 passes, is 69 million.
    /// </summary>
    public const long MaxMovesPerJob = 100_000_000;

    /// <summary>Moves a wobble along <paramref name="lengthMm"/> of path makes.</summary>
    public static long MoveCount(double lengthMm, double spacingMm) => (long)Math.Ceiling(lengthMm / (spacingMm / PointsPerLoop)) + 1;

    public static List<Vec2> Expand(IReadOnlyList<Vec2> path, double diameterMm, double spacingMm)
    {
        if (path.Count < 2) return [.. path];
        var cum = new double[path.Count];
        for (int i = 1; i < path.Count; i++) cum[i] = cum[i - 1] + path[i - 1].Dist(path[i]);
        double total = cum[^1], r = diameterMm / 2, step = spacingMm / PointsPerLoop;
        long n = (long)Math.Ceiling(total / step);
        if (n > MaxMovesPerJob)
            throw new JobException($"A wobble along {total:N0} mm at {spacingMm:0.###} mm spacing would make {n / 1e6:0.#} million moves, more than one job can hold. Widen the spacing.");
        var o = new List<Vec2>((int)n + 1);
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

    /// <summary>The same cells, moved (shares the level data).</summary>
    public FillMask Shifted(Vec2 d) => new(Origin + d, CellMm, Width, Height, _levels, _threshold);
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

    /// <summary>Only the parts of the lines inside this rectangle are burned (marking in sections).</summary>
    public Rect2? ClipRect { get; init; }

    /// <summary>Burn every line in the same direction (the beam travels back unlit): more even on some materials, slower.</summary>
    public bool Unidirectional { get; init; }

    /// <summary>Scan from the far side back (bottom to top in the scan direction).</summary>
    public bool ReverseOrder { get; init; }

    /// <summary>Pause this long before each line after the first (ms), so heat can leave (acrylic, thin stock). 0 = none.</summary>
    public int LineDwellMs { get; init; }

    /// <summary>
    /// Burn the scan lines spread out: every <see cref="Spread"/>th line first, then the ones after
    /// them, and so on, so no line is burned straight after its neighbour and the heat has time to
    /// leave (less scorching and warping, more even colours). 0 or 1 = in order.
    /// </summary>
    public int Spread { get; init; }

    /// <summary>Spread the lines in no set pattern (still never beside the last), so no row pattern repeats across the area.</summary>
    public bool Scatter { get; init; }

    /// <summary>
    /// Turn the scan lines this much more on every repeat of the block (degrees), so the line
    /// pattern doesn't build up into stripes on deep or repeated engraving. 0 = every pass the same.
    /// </summary>
    public double AngleStepDeg { get; init; }

    /// <summary>
    /// Dot width correction: every burned line is this much shorter (half at each end), so the
    /// beam's own width doesn't make fills come out wider than drawn. About the beam width.
    /// </summary>
    public double DotWidthMm { get; init; }

    /// <summary>
    /// Less power in some places (grain-aware engraving): each scan line is burned in pieces, each at
    /// the layer's power times the field's factor there. Null = the layer's power everywhere.
    /// </summary>
    public PowerField? PowerScale { get; init; }

    /// <summary>
    /// The scan lines given directly, in order (marks moved onto an object, <see cref="SurfaceWrap"/>): used
    /// instead of working them out from <see cref="Polygons"/> or <see cref="Mask"/>. Null = worked out.
    /// </summary>
    public IReadOnlyList<Job.FillSegment>? Lines { get; init; }

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
    /// <summary>The loops close back on themselves (false: open pieces, after cutting into sections).</summary>
    public bool Closed { get; init; } = true;

    /// <summary>Total length of the loops, mm.</summary>
    public double Length
    {
        get
        {
            double l = 0;
            foreach (var p in Loops)
                for (int i = 0; i < p.Count - (Closed ? 0 : 1); i++) l += p[i].Dist(p[(i + 1) % p.Count]);
            return l;
        }
    }
}

/// <summary>Single laser pulses of <see cref="DotUs"/> at these points, in this order (dot mode along a path).</summary>
public sealed record PointsBlock(IReadOnlyList<Vec2> Points, LaserSettings Laser, int DotUs) : Block(Laser);

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

    /// <summary>Burn the rows spread out, every <see cref="Spread"/>th row first (see <see cref="FillBlock.Spread"/>). 0 or 1 = in order.</summary>
    public int Spread { get; init; }

    /// <summary>Spread the lines in no set pattern (still never beside the last), so no row pattern repeats across the area.</summary>
    public bool Scatter { get; init; }

    /// <summary>Every row burned left to right, the head going back unlit (slower; rows can't be offset from each other by the direction).</summary>
    public bool Unidirectional { get; init; }

    /// <summary>
    /// The rows' direction, degrees from X (the picture was laid out on a grid turned this much).
    /// Dot (c, r) is at <see cref="At"/>; at 0 that's <see cref="Origin"/> + (c, r) × pitch.
    /// </summary>
    public double AngleDeg { get; init; }

    /// <summary>
    /// Turning per pass: the same picture laid out at each pass's angle; pass p burns
    /// PassBlocks[(p − 1) mod count]. Null: every pass the same.
    /// </summary>
    public IReadOnlyList<DotBlock>? PassBlocks { get; init; }

    /// <summary>The block a pass burns (see <see cref="PassBlocks"/>).</summary>
    public DotBlock ForPass(int pass) => PassBlocks is { Count: > 0 } pb && pass >= 1 ? pb[(pass - 1) % pb.Count] : this;

    /// <summary>
    /// The first and last burned dot of every row. The head only goes to burned dots, so
    /// everything it visits lies between these (a grid turned to an angle has corners far
    /// outside the picture that it never goes near).
    /// </summary>
    public IEnumerable<Vec2> RowEnds()
    {
        int w = Bitmap.Width;
        for (int r = 0; r < Bitmap.Height; r++)
        {
            int first = Array.IndexOf(Bitmap.Bits, true, r * w, w);
            if (first < 0) continue;
            int last = Array.LastIndexOf(Bitmap.Bits, true, r * w + w - 1, w);
            yield return At(first - r * w, r);
            if (last != first) yield return At(last - r * w, r);
        }
    }

    /// <summary>Where dot (column, row) is on the bed.</summary>
    public Vec2 At(int c, int r)
    {
        double pitch = 25.4 / Dpi;
        if (AngleDeg == 0) return new(Origin.X + c * pitch, Origin.Y + r * pitch);
        double a = AngleDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        return new(Origin.X + (c * cos - r * sin) * pitch, Origin.Y + (c * sin + r * cos) * pitch);
    }

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

    /// <summary>Longest pause after each path we accept (s).</summary>
    public const double MaxPathPauseS = 600;

    /// <summary>Longest fill overscan we accept (mm).</summary>
    public const double MaxOverscanMm = 50;

    /// <summary>Furthest the focus may be moved either way for a block (mm).</summary>
    public const double MaxFocusOffsetMm = 10;

    /// <summary>
    /// On the rotary the surface can be much nearer the axis in places than where the focus was set
    /// (a stacked model finished on its dowel), so there the focus may move this far (mm).
    /// </summary>
    public const double MaxRotaryFocusOffsetMm = 60;

    /// <summary>Furthest the focus may move either way in this job's work mode (mm).</summary>
    public static double FocusLimitMm(WorkMode mode) => mode is WorkMode.Rotary ? MaxRotaryFocusOffsetMm : MaxFocusOffsetMm;

    /// <summary>The limit for a job: <paramref name="overrideMm"/> when set (never past the rotary's), else the work mode's.</summary>
    public static double FocusLimitFor(WorkMode mode, double? overrideMm) =>
        overrideMm is { } o && double.IsFinite(o) && o > 0 ? Math.Min(o, MaxRotaryFocusOffsetMm) : FocusLimitMm(mode);

    /// <summary>
    /// A wider focus limit for this job (mm, null = the work mode's): a part held up in the rotary and cut on the
    /// flat, the cut going down through it. Never past <see cref="MaxRotaryFocusOffsetMm"/>.
    /// </summary>
    public double? FocusLimitOverrideMm { get; init; }

    double FocusLimit => FocusLimitFor(Mode, FocusLimitOverrideMm);

    public MachineProfile Machine { get; }
    public IReadOnlyList<Block> Blocks { get; }
    public PassMode PassMode { get; init; } = new PassMode.PerBlock();

    /// <summary>
    /// The exhaust fan's gear for the job (M9064 B…, sent to the fan's accessory port too): 3 is what
    /// xTool Studio sends, its calibration jobs 2. Read back by xTool as the fan's "A" gear.
    /// </summary>
    public int FanGear { get; init; } = 3;
    /// <summary>The purifier's speed for the job (%; M9039 V…, 50 as xTool Studio sends).</summary>
    public int PurifierSpeed { get; init; } = 50;
    public Preamble Preamble { get; init; }
    /// <summary>Written to the <c># date=</c> header line, e.g. <c>2026_10_12_19_02_38</c>.</summary>
    public string Created { get; init; } = "";
    /// <summary>Flat bed, rotary or conveyor.</summary>
    public WorkMode Mode { get; init; } = new WorkMode.Flat();

    /// <summary>
    /// The head follows a curved surface: its height is set absolutely by the blocks (see
    /// <see cref="Block.HeadZ"/>, <see cref="BandedVectorBlock"/>), inside <c>G102</c> … <c>G103</c>.
    /// </summary>
    public bool FollowsSurface => SurfaceHeight is not null;

    /// <summary>Curved surfaces: the material's height (mm above the bed) anywhere on the bed. Null = flat.</summary>
    public Func<Vec2, double>? SurfaceHeight { get; init; }

    /// <summary>Curved surfaces: how far apart the head's heights are (mm). xTool Studio uses 0.2.</summary>
    public double BandMm { get; init; } = 0.2;

    /// <summary>
    /// Curved surfaces: the share of each layer's speed to go at over a bed point (0–1; 1 = as set), so a
    /// surface that slopes away from the beam gets as much light per mm² as its top (<see cref="WrapZone.EvenOut"/>).
    /// Dots are held on longer instead. Null = full speed everywhere.
    /// </summary>
    public Func<Vec2, double>? SpeedShare { get; init; }

    /// <summary>The blocks as they're written: split into height bands on a curved surface.</summary>
    public IReadOnlyList<Block> OutputBlocks() => SurfaceHeight is { } h && Flat ? SurfaceBanding.Apply(Blocks, h, BandMm, SpeedShare) : Blocks;

    /// <summary>The F word for a stretch at <paramref name="at"/>: the layer's speed, slowed where <see cref="SpeedShare"/> says.</summary>
    long FeedAt(LaserSettings l, Vec2 at)
    {
        if (SpeedShare is not { } share) return l.FValue;
        double k = share(at);
        return !double.IsFinite(k) || k >= 1 ? l.FValue : Math.Max(1, (long)Math.Round(l.FValue * Math.Max(WrapZone.SlowestShare, k), MidpointRounding.AwayFromZero));
    }

    /// <summary>The head's travel (machine Z, mm).</summary>
    public const double MinHeadZ = 6, MaxHeadZ = 156;

    bool Flat => Mode is WorkMode.Flat;

    /// <summary>
    /// With an attachment, where the galvo holds its other axis: Y across the rotary, X along the belt.
    /// The galvo field is square, and the work area's other side is the unrolled object or the belt,
    /// so the field's middle is taken from the side that is still the field.
    /// </summary>
    double AttachmentLine => Mode switch
    {
        WorkMode.Rotary r => r.LineY ?? Machine.FieldW / 2,
        WorkMode.Conveyor c => c.LineX ?? Machine.FieldH / 2,
        _ => 0,
    };

    /// <summary>A design position as G-code words: X/Y on the bed, X/U on the rotary, U/Y on the conveyor.</summary>
    string Pos(Vec2 p, Func<double, string> f) => Mode switch
    {
        WorkMode.Rotary r => $"X{f(p.X)}U{f((p.Y - r.OriginY) * r.Direction)}",
        WorkMode.Conveyor => $"U{f(p.X - Machine.FieldW / 2)}Y{f(p.Y)}",
        _ => $"X{f(p.X)}Y{f(p.Y)}",
    };

    /// <summary>A Z move; with an attachment the U mode is left for it and entered again, as xTool Studio does.</summary>
    string[] ZMoveAny(double mm) => Mode switch
    {
        WorkMode.Flat => ZMove(mm),
        WorkMode.Conveyor c => ["G101", .. ZMove(mm), $"G100 F{JsNumber(c.SpeedMmMin)}"],
        // The Z move puts the rapid back to full speed: held down again once U is back on.
        WorkMode.Rotary r => ["G101", .. ZMove(mm), "G100", $"G0F{RotaryFeedCap(r)}"],
        _ => ["G101", .. ZMove(mm), "G100"],
    };

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
            if (!double.IsFinite(block.FocusOffsetMm) || Math.Abs(block.FocusOffsetMm) > FocusLimit + 1e-9)
                throw new JobException($"The focus can move at most {FocusLimit:0} mm either way{(Mode is WorkMode.Rotary ? " on the rotary" : "")} (asked for {block.FocusOffsetMm:0.##} mm).");
            foreach (var z in HeadHeights(block))
                if (!double.IsFinite(z) || z < MinHeadZ - 1e-9 || z > MaxHeadZ + 1e-9)
                    throw new JobException($"The curved surface needs the head at {z:0.#} mm, outside its travel ({MinHeadZ:0}–{MaxHeadZ:0} mm). Check the measured heights.");
            switch (block)
            {
                case BandedVectorBlock b:
                    foreach (var piece in b.Pieces)
                    {
                        foreach (var p in piece.Points) CheckPoint(p, ctx, i);
                        anyMotion |= piece.Points.Count >= 2;
                    }
                    break;
                case BandedFillBlock b:
                    foreach (var (_, segs) in b.Bands)
                        foreach (var seg in segs)
                        {
                            CheckPoint(seg.Start, ctx, i);
                            CheckPoint(seg.End, ctx, i);
                            anyMotion = true;
                        }
                    break;
                case VectorBlock b:
                    b.Path.Validate();
                    if (b.Drop is { } drop)
                    {
                        // A block only runs its own passes, round by round or not.
                        drop.Validate(b.Laser.Passes);
                        double lowest = b.FocusOffsetMm + drop.DepthFor(b.Laser.Passes);
                        if (lowest > StepDown.MaxTotalMm + 1e-9)
                            throw new JobException($"The focus offset ({b.FocusOffsetMm:0.##} mm) and stepping down ({drop.DepthFor(b.Laser.Passes):0.##} mm) together lower the head {lowest:0.##} mm; the limit is {StepDown.MaxTotalMm} mm.");
                    }
                    var pts = b.Path.EmittedPoints();
                    anyMotion |= pts.Count >= 2;
                    foreach (var p in pts) CheckPoint(p, ctx, i);
                    break;
                case FillBlock b:
                    JobException.CheckRange("line density", b.LinesPerCm, 1, FillBlock.MaxLinesPerCm, "lines/cm");
                    if (!double.IsFinite(b.AngleDeg) || !double.IsFinite(b.AngleStepDeg)) throw new JobException("The fill angle is not a valid number.");
                    JobException.CheckRange("overscan", b.OverscanMm, 0, MaxOverscanMm, "mm");
                    JobException.CheckRange("dot width correction", b.DotWidthMm, 0, 1, "mm");
                    JobException.CheckRange("pause between lines", b.LineDwellMs, 0, 60_000, "ms");
                    foreach (var p in b.Polygons.SelectMany(x => x))
                    {
                        if (!p.IsFinite) throw new JobException("A fill coordinate is not a valid number.");
                        // Cut to a section, or lines given directly: only the lines (checked below) need to be in the work area.
                        if (b.ClipRect is null && b.Lines is null) CheckPoint(p, ctx, i);
                    }
                    // Turned per pass, each pass has its own lines and run-ups: check every angle used.
                    var passFills = Enumerable.Range(1, Math.Max(1, b.Laser.Passes)).Select(b.ForPass)
                        .DistinctBy(f => Math.Round(((f.AngleDeg % 360) + 360) % 360, 6)).Take(360).ToList();
                    if (b.ClipRect is not null || b.Lines is not null)
                        foreach (var seg in passFills.SelectMany(FillLines))
                        {
                            CheckPoint(seg.Start, ctx, i);
                            CheckPoint(seg.End, ctx, i);
                        }
                    if (b.Mask is { } mask && b.ClipRect is null && b.Lines is null)
                    {
                        if (!mask.Valid || !(mask.CellMm > 0) || !mask.Origin.IsFinite)
                            throw new JobException("A depth slice's cells don't match its size.");
                        var mb = mask.FilledBounds();
                        if (!mb.IsEmpty)
                        {
                            CheckPoint(mb.TopLeft, ctx, i);
                            CheckPoint(mb.BottomRight, ctx, i);
                        }
                    }
                    var lines = FillLines(b);
                    if (b.OverscanMm > 0)
                        foreach (var seg in passFills.SelectMany(FillLines))
                        {
                            var (runUp, runOut) = Overscan(seg, b.OverscanMm);
                            CheckPoint(runUp, $" (fill overscan, item {i + 1})", i);
                            CheckPoint(runOut, $" (fill overscan, item {i + 1})", i);
                        }
                    anyMotion |= lines.Count > 0;
                    break;
                case PointsBlock b:
                    JobException.CheckRange("dot duration", b.DotUs, 10, 100_000, "µs");
                    if (b.DotUs % 10 != 0)
                        throw new JobException($"Dot duration must be a multiple of 10 µs (got {b.DotUs} µs).");
                    foreach (var p in b.Points)
                    {
                        if (!p.IsFinite) throw new JobException("A dot position is not a valid number.");
                        CheckPoint(p, ctx, i);
                    }
                    anyMotion |= b.Points.Count > 0;
                    break;
                case LoopsBlock b:
                    foreach (var p in b.Loops.SelectMany(x => x))
                    {
                        if (!p.IsFinite) throw new JobException("A fill coordinate is not a valid number.");
                        CheckPoint(p, ctx, i);
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
                    // Each row's first and last burned dot bound every dot the head goes to.
                    foreach (var pb in b.PassBlocks ?? [b])
                        foreach (var end in pb.RowEnds()) CheckPoint(end, ctx, i);
                    anyMotion |= b.Bitmap.Bits.Any(v => v);
                    break;
            }
        }
        if (!anyMotion) throw new EmptyJobException();
    }

    /// <param name="item">Which block the point is in (0 = first), so the job's maker can name the shape.</param>
    void CheckPoint(Vec2 p, string ctx, int? item = null)
    {
        switch (Mode)
        {
            case WorkMode.Rotary r:
                if (p.X < 0 || p.X > Machine.FieldW || Math.Abs(p.Y - r.OriginY) > r.CircumferenceMm / 2 + RotarySeamSlackMm + 1e-6)
                    throw new OutsideWorkAreaException(p.X, p.Y, Machine.FieldW, r.CircumferenceMm, ctx + " (the rotary's area: the field across, once round the object)", r.OriginY - r.CircumferenceMm / 2, item);
                return;
            case WorkMode.Conveyor c:
                if (p.X < 0 || p.X > c.LengthMm || p.Y < 0 || p.Y > Machine.FieldH)
                    throw new OutsideWorkAreaException(p.X, p.Y, c.LengthMm, Machine.FieldH, ctx + " (the conveyor's area)", item: item);
                return;
        }
        if (!Machine.Contains(p.X, p.Y))
            throw new OutsideWorkAreaException(p.X, p.Y, Machine.FieldW, Machine.FieldH, ctx, item: item);
    }

    /// <summary>Every head height a block asks for (curved surfaces).</summary>
    static IEnumerable<double> HeadHeights(Block b) => b switch
    {
        BandedVectorBlock v => v.Pieces.Select(p => p.HeadZ),
        BandedFillBlock f => f.Bands.Select(x => x.HeadZ),
        _ => b.HeadZ is { } z ? [z] : [],
    };

    /// <summary>The complete motion file. Validates first.</summary>
    public string Render() => RenderText().ToString();

    /// <summary>The complete motion file as bytes, for jobs of any size (a dense wobble can be more than one string holds). Validates first.</summary>
    public GcodeText RenderText()
    {
        Validate();
        if (FollowsSurface && !Flat) throw new JobException("Curved surfaces work on the flat bed only (not with the rotary or conveyor).");
        var blocks = OutputBlocks();
        if (FollowsSurface)
            foreach (var z in blocks.SelectMany(HeadHeights))
                if (!double.IsFinite(z) || z < MinHeadZ - 1e-9 || z > MaxHeadZ + 1e-9)
                    throw new JobException($"The curved surface needs the head at {z:0.#} mm, outside its travel ({MinHeadZ:0}–{MaxHeadZ:0} mm). Check the measured heights.");
        var tag = Machine.GcodeTag;
        var lines = new GcodeText(Header());
        double focus = 0; // how far the head is lowered now (mm)
        double? headZ = null; // curved surfaces: where the head is now
        // A step-down cut written one pass at a time: the head goes to that pass's depth here and
        // stays there for the next path at the same depth, instead of going back up after each one.
        void Focus(Block b, int? pass = null)
        {
            if (FollowsSurface)
            {
                // Blocks with one height move the head before they start; banded ones move it themselves.
                if (b.HeadZ is { } z && (headZ is not { } now || Math.Abs(now - z) > 1e-6))
                {
                    lines.AddRange([$"G0Z{Trim3(z)}F600", "# MUST", ""]);
                    headZ = z;
                }
                else if (b is BandedVectorBlock or BandedFillBlock) headZ = HeadHeights(b).LastOrDefault();
                return;
            }
            double want = b.FocusOffsetMm + (b is VectorBlock { Drop: { } d } && pass is { } p ? d.DepthFor(p) : 0);
            if (Math.Abs(want - focus) < 1e-6) return;
            lines.AddRange(ZMoveAny(-(want - focus)));
            lines.AddRange(["# MUST", ""]);
            focus = want;
        }
        switch (PassMode)
        {
            case PassMode.PerBlock:
                foreach (var b in blocks)
                {
                    bool onePass = b is VectorBlock { Drop: not null } && (b.PauseAfterS > 0 || b.Laser.Passes <= 1);
                    Focus(b, onePass ? 1 : null);
                    if (b is FillBlock { AngleStepDeg: not 0 } f && f.Laser.Passes > 1)
                        // Each pass turned a little further: written out, the device loop repeats one pattern.
                        for (int p = 1; p <= f.Laser.Passes; p++)
                        {
                            lines.AddRange(RenderBlock(f.ForPass(p), tag, 1));
                            lines.Add("");
                        }
                    else if (b is DotBlock { PassBlocks.Count: > 1 } d && d.Laser.Passes > 1)
                        for (int p = 1; p <= d.Laser.Passes; p++)
                        {
                            lines.AddRange(RenderBlock(d.ForPass(p), tag, 1));
                            lines.Add("");
                        }
                    else if (b.PauseAfterS > 0)
                        // A pause after each pass: the passes written out (the device's loop can't hold one).
                        for (int p = 1; p <= Math.Max(1, b.Laser.Passes); p++)
                        {
                            if (onePass) Focus(b, p);
                            lines.AddRange(RenderBlock(b, tag, 1));
                            lines.Add("# pause after the path");
                            lines.AddRange(Dwell(b.PauseAfterS));
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
                    foreach (var b in blocks.Where(b => b.Laser.Passes >= round))
                    {
                        Focus(b, round);
                        lines.AddRange(RenderBlock(b switch { FillBlock f => f.ForPass(round), DotBlock d => d.ForPass(round), _ => b }, tag, 1));
                        if (b.PauseAfterS > 0)
                        {
                            lines.Add("# pause after the path");
                            lines.AddRange(Dwell(b.PauseAfterS));
                        }
                        lines.Add("");
                    }
                }
                break;
        }
        if (!FollowsSurface && Math.Abs(focus) > 1e-6)
        {
            // Back to the focus the job started at.
            lines.AddRange(ZMoveAny(focus));
            lines.AddRange(["# MUST", ""]);
        }
        var tail = Tail(tag, Flat);
        // Curved: leave Z mode at the start of the tail, as xTool Studio does.
        if (FollowsSurface) tail.Insert(tail.IndexOf($"# {tag} TAIL") + 1, "G103");
        lines.AddRange(tail);
        return lines;
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
            .. GcLines(),
            "# timeConfig=",
            "G90",
            "G0 F3000",
            "G4P5",
            "G198 P84 \"M9033 V100\"",
            "G198 P85 \"M9401 D2 V500\"",
            $"M9064 B{Math.Clamp(FanGear, 1, 4)}",
            $"G198 P70 \"M9064 B{Math.Clamp(FanGear, 1, 4)}\"",
            $"G198 P78 \"M9064 B{Math.Clamp(FanGear, 1, 4)}\"",
            "M9039 C2",
            "G198 P69 \"M9039 C2\"",
            $"G198 P76 \"M9039 V{Math.Clamp(PurifierSpeed, 10, 100)}\"",
            $"G198 P76 \"M9043 {m9043}\"",
            "G4P5",
            $"# {tag} HEAD",
            "G0 F180000",
            "M4 S0",
            "G1 F180000",
            "G0 X0 Y0",
            .. ModeHead(),
            .. FollowsSurface ? new[] { "G102" } : [],
            "",
        ];
    }

    /// <summary>The <c># gc=</c> header lines: work area size, start, and which axes the design uses.</summary>
    IEnumerable<string> GcLines()
    {
        static string F4(double v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        var (w, h, sx, sy, keys, rm) = Mode switch
        {
            WorkMode.Rotary r => (Machine.FieldW, r.CircumferenceMm, "0", F4(r.OriginY), "\"x\",\"u\"", r.Direction),
            WorkMode.Conveyor c => (c.LengthMm, Machine.FieldH, JsNumber(Machine.FieldW / 2), "0.0000", "\"u\",\"y\"", 1),
            _ => (Machine.FieldW, Machine.FieldH, "0", "0.0000", "\"x\",\"y\"", 1),
        };
        yield return $"# gc={{\"size\":{{\"w\":{JsNumber(w)},\"h\":{JsNumber(h)}}}}}";
        yield return "# gc={\"offset\":{\"x\":0,\"y\":0}}";
        yield return $"# gc={{\"start\":{{\"x\":{sx},\"y\":{sy}}}}}";
        yield return $"# gc={{\"keys\":[{keys}],\"rm\":{rm},\"is3DMode\":false}}";
    }

    /// <summary>Entering U mode with an attachment, and parking the galvo's other axis in the middle.</summary>
    IEnumerable<string> ModeHead() => Mode switch
    {
        // The galvo goes to its line before U mode too: once U is on, it may not take a Y (or X) on its own.
        // The rapid is held down once U is on: every G0 from here may turn the rotary.
        WorkMode.Rotary r => [$"G0Y{Fixed3(AttachmentLine)}", $"M535U{JsRounded(r.UMultiple, 5)}", "G100", $"G0F{RotaryFeedCap(r)}", $"G0Y{Fixed3(AttachmentLine)}"],
        WorkMode.Conveyor c => [$"G0X{Fixed3(AttachmentLine)}", $"G100 F{JsNumber(c.SpeedMmMin)}", $"M535U{JsRounded(c.UMultiple, 5)}", $"M536U{JsRounded(c.AngleCompensate, 5)}", $"G0X{Fixed3(AttachmentLine)}"],
        _ => [],
    };

    /// <summary>
    /// The framing file the machine traces at low power when its frame button
    /// is used. <paramref name="outline"/> is usually the job's bounding box or hull.
    /// </summary>
    /// <summary>Framing power (%) and speed (mm/s) as the stock software uses them: a faint, fast trace.</summary>
    public const double FramePowerDefault = 6.4, FrameSpeedDefault = 16000, FramePowerMax = 15;

    public string Border(IReadOnlyList<Vec2> outline, double powerPct = FramePowerDefault, double speedMmS = FrameSpeedDefault)
    {
        powerPct = Math.Clamp(powerPct, 0.5, FramePowerMax);
        speedMmS = Math.Clamp(speedMmS, 100, 20000);
        long sv = (long)Math.Round(powerPct * 10), fv = (long)Math.Round(speedMmS * 60);
        if (outline.Count < 2) throw new JobException("The frame outline needs at least two points.");
        foreach (var p in outline)
        {
            if (!p.IsFinite) throw new JobException("A frame coordinate is not a valid number.");
            CheckPoint(p, " (frame)");
        }
        if (!Flat)
        {
            // With an attachment the frame only shows the galvo's own direction: a thin box across
            // the attachment's line (the object or belt doesn't move while framing).
            var box = Rect2.Bounds(outline);
            double mid = AttachmentLine;
            outline = Mode is WorkMode.Rotary
                ? [new(box.MinX, mid - 0.5), new(box.MaxX, mid - 0.5), new(box.MaxX, mid + 0.5), new(box.MinX, mid + 0.5), new(box.MinX, mid - 0.5)]
                : [new(mid - 0.5, box.MinY), new(mid + 0.5, box.MinY), new(mid + 0.5, box.MaxY), new(mid - 0.5, box.MaxY), new(mid - 0.5, box.MinY)];
        }
        var tag = Machine.GcodeTag;
        var lines = new List<string>
        {
            $"# {tag} HEAD", "G0 F180000", "M4 S0", "G1 F180000", "M114 S1", "", "",
            $"# {tag} VECTOR HEAD", "# motion_start", "", "G21", "G90", "", "", "", "",
            $"# blockConfig={{\"powerFactor\":{JsRounded(powerPct / 100, 6)},\"isVector\":true,\"crossDot\":false}}",
            $"G0X{Trim3(outline[0].X)}Y{Trim3(outline[0].Y)}",
        };
        for (int i = 1; i < outline.Count; i++)
            lines.Add($"G1X{Trim3(outline[i].X)}Y{Trim3(outline[i].Y)}" + (i == 1 ? $"S{sv}F{fv}" : ""));
        lines.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        lines.AddRange(Tail(tag, true));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The rotary's speed limit, in turns of the object a second. The machine's rapid (<c>G0 F180000</c>, 3 m/s) is
    /// fine for the galvo but would spin a small rod about 30 turns a second (a homemade direct-drive chuck took off,
    /// 2026-11-08), so on the rotary every move that turns it is held to this. (Half a turn a second at first;
    /// Kyle raised it to 10, since half a turn is slow on a thin rod.)
    /// </summary>
    public const double MaxRotaryTurnsPerS = 10;

    /// <summary>The fastest feed (mm/min round the object) that keeps the rotary within <see cref="MaxRotaryTurnsPerS"/>.</summary>
    public static long RotaryFeedCap(WorkMode.Rotary r) => Math.Max(60, (long)Math.Floor(MaxRotaryTurnsPerS * r.CircumferenceMm * 60));

    /// <summary>A layer's feed (mm/min) as written: on the rotary never past <see cref="RotaryFeedCap"/>.</summary>
    long Feed(LaserSettings l) => Mode is WorkMode.Rotary r ? Math.Min(l.FValue, RotaryFeedCap(r)) : l.FValue;

    /// <summary>Furthest one turning job may move the rotary (mm round the object: a few turns of the largest object).</summary>
    public const double MaxTurnMm = 2000;

    /// <summary>How far past once round a rotary path may reach (mm): a line all the way round carries on past its start.</summary>
    public const double RotarySeamSlackMm = 10;

    /// <summary>
    /// A job that only turns the rotary by <paramref name="uMm"/> (U, mm round the object, the same
    /// units and sign as the jobs' own U words) with the laser off: the usual head and U mode, one
    /// <c>G0 U</c>, and the tail without its <c>G0 U0</c> (that would turn it straight back). Used to
    /// bring the object back to where a stopped job started, since the F2 Ultra ignores U moves sent
    /// outside a job.
    /// </summary>
    public string TurnRotary(double uMm)
    {
        if (Mode is not WorkMode.Rotary) throw new JobException("Turning the rotary needs the rotary work mode.");
        if (!double.IsFinite(uMm)) throw new JobException("The turn is not a valid number.");
        if (Math.Abs(uMm) > MaxTurnMm) throw new JobException($"That's a long way to turn ({Math.Abs(uMm):0} mm round the object). Turn it by hand instead.");
        var tag = Machine.GcodeTag;
        var lines = Header();
        lines.AddRange(["# turn the rotary, laser off", $"G0U{Fixed3(uMm)}", "# MUST", ""]);
        lines.AddRange(["# END", $"# {tag} TAIL", "G101", "G90", "G0 S0", "G1 F180000", "M536 U0", "M6 P1", ""]);
        return string.Join("\n", lines);
    }

    // Timings measured on the F2 Ultra against the job history (Oct 2026).
    /// <summary>Moves with the laser off: galvo jumps, and the moves between a fill's or a picture's pieces (mm/s).</summary>
    const double Jump = 3000;
    /// <summary>Starting a job: the smallest jobs took about this much longer than their moves.</summary>
    public const double StartS = 1.5;
    /// <summary>Each piece of a fill line: the laser going on and off at its ends (3.2 ms over two fills of 5000 pieces, 2.9 without the start).</summary>
    const double FillPieceS = 0.0029;
    /// <summary>
    /// Each dot of a picture, on top of its own duration: the machine steps dot to dot in about the same
    /// time however far apart (200 µs dots took 604–614 µs each at 300 to 670 dpi and 80 mm/s, where
    /// moving at the speed would take 474–1058 µs). Only 80 mm/s has been timed.
    /// </summary>
    const double DotStepS = 0.00041;
    /// <summary>
    /// On the rotary, each time the part turns between one line and the next (a fill's or a picture's rows): the
    /// rotary has to start and stop, where the galvo's step is next to nothing. Kyle's DIY rotary, 2026-11-09: a
    /// Carve to model job of 29,561 lines 0.1 mm apart ran 3,271 s against 2,315 s estimated, 32 ms a line more.
    /// </summary>
    public const double RotaryStepS = 0.032;

    /// <summary>
    /// Duration in seconds, from the moves and the times measured on the machine (the start, each
    /// fill piece, each dot). Good for comparing settings and showing progress.
    /// </summary>
    public double EstimateSeconds()
    {
        double work = StartS + Blocks.Sum(b => AllPassesSeconds(b, Mode));
        return PassMode is PassMode.Rounds r ? work + r.DelayS * Math.Max(0, Rounds - 1) : work;
    }

    /// <summary>Every pass of a block (a picture turned per pass has a different layout each pass).</summary>
    /// <param name="mode">The work mode (null: the flat bed): on the rotary, turning between lines takes time of its own.</param>
    public static double AllPassesSeconds(Block b, WorkMode? mode = null)
    {
        if (b is not DotBlock { PassBlocks: { Count: > 0 } pb }) return BlockSeconds(b, mode) * b.Laser.Passes;
        var each = pb.Select(x => BlockSeconds(x, mode)).ToArray();
        return Enumerable.Range(0, Math.Max(1, b.Laser.Passes)).Sum(p => each[p % each.Length]);
    }

    /// <summary>Pass <paramref name="pass"/> (from 1) of a block.</summary>
    public static double PassSeconds(Block b, int pass, WorkMode? mode = null) => BlockSeconds(b is DotBlock d ? d.ForPass(pass) : b, mode);

    /// <summary>
    /// A fill: its line pieces at the speed, jumps between them, and the time each piece costs. On the rotary a move
    /// that goes round (design Y) turns the part, at the layer's speed, starting and stopping (<see cref="RotaryStepS"/>).
    /// </summary>
    static double FillSeconds(FillBlock f, bool rotary)
    {
        var segs = FillLines(f);
        double s = 0;
        for (int i = 0; i < segs.Count; i++)
        {
            s += (segs[i].Start.Dist(segs[i].End) + 2 * f.OverscanMm) / f.Laser.Speed + FillPieceS + f.LineDwellMs / 1000.0;
            if (i == 0) continue;
            var from = segs[i - 1].End;
            var to = segs[i].Start;
            double round = Math.Abs(to.Y - from.Y);
            if (rotary && round > 1e-6)
                s += RotaryStepS + round / Math.Max(1, f.Laser.Speed) + Math.Max(0, Math.Abs(to.X - from.X) - 2 * f.OverscanMm) / Jump;
            else
                s += Math.Max(0, from.Dist(to) - 2 * f.OverscanMm) / Jump;
        }
        return s;
    }

    /// <summary>A picture: each dot's duration and step, and jumps over the blank stretches of each row.</summary>
    static double DotSeconds(DotBlock d, bool rotary)
    {
        double pitch = 25.4 / d.Dpi;
        int w = d.Bitmap.Width;
        var bits = d.Bitmap.Bits;
        long on = 0;
        double gaps = 0;
        for (int r = 0; r < d.Bitmap.Height; r++)
        {
            int a = Array.IndexOf(bits, true, r * w, w);
            if (a < 0) continue;
            int z = Array.LastIndexOf(bits, true, r * w + w - 1, w), n = 0;
            for (int i = a; i <= z; i++) if (bits[i]) n++;
            on += n;
            gaps += (z - a + 1 - n) * pitch;
        }
        return on * (d.DotUs * 1e-6 + DotStepS) + gaps / Jump
            // One way: the head goes back across each row.
            + (d.Unidirectional ? d.Bitmap.Height * w * pitch / Jump : 0)
            // On the rotary the part turns to each row with something on it.
            + (rotary ? RowsWithDots(d) * RotaryStepS : 0);
    }

    static int RowsWithDots(DotBlock d)
    {
        int w = d.Bitmap.Width, n = 0;
        for (int r = 0; r < d.Bitmap.Height; r++)
            if (Array.IndexOf(d.Bitmap.Bits, true, r * w, w) >= 0) n++;
        return n;
    }

    /// <summary>Time for one pass of a block, in seconds (same model as <see cref="EstimateSeconds"/>).</summary>
    public static double BlockSeconds(Block b, WorkMode? mode = null)
    {
        bool rotary = mode is WorkMode.Rotary;
        return b.PauseAfterS + b switch
        {
            VectorBlock v => v.Path.Length / v.Laser.Speed + (v.Path.Points.Count > 0 ? 0.002 : 0) + 20 / Jump,
            // Each band: the head's move at 10 mm/s, then the pieces.
            BandedVectorBlock bv => bv.Pieces.Sum(p => PathLength(p.Points) / bv.Laser.Speed + 10 / Jump)
                + bv.Pieces.Select(p => p.HeadZ).Distinct().Count() * 0.25,
            BandedFillBlock bf => bf.Bands.Sum(x => x.Segments.Sum(s => s.Start.Dist(s.End) / bf.Laser.Speed + 10 / bf.Fill.LinesPerCm / bf.Laser.Speed) + 0.25),
            FillBlock f => FillSeconds(f, rotary),
            LoopsBlock l => l.Length / l.Laser.Speed + l.Loops.Count * (0.0005 + 5 / Jump),
            PointsBlock p => p.Points.Count * p.DotUs * 1e-6 + Enumerable.Range(1, Math.Max(0, p.Points.Count - 1)).Sum(i => p.Points[i - 1].Dist(p.Points[i])) / Math.Max(1, p.Laser.Speed),
            DotBlock d => DotSeconds(d, rotary),
            _ => 0,
        };
    }

    static List<string> Tail(string tag, bool flat) => flat
        ? ["# END", $"# {tag} TAIL", "G90", "G0 S0", "G1 F180000", "M536 U0", "M6 P1", ""]
        : ["# END", $"# {tag} TAIL", "G0 U0", "G101", "G90", "G0 S0", "G1 F180000", "M536 U0", "M6 P1", ""];

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

    static double PathLength(IReadOnlyList<Vec2> pts)
    {
        double s = 0;
        for (int i = 1; i < pts.Count; i++) s += pts[i - 1].Dist(pts[i]);
        return s;
    }

    /// <summary>A curved surface's lines: one vector block, the head moving between bands (as xTool Studio writes it).</summary>
    List<string> RenderBandedVector(BandedVectorBlock b, string tag, int passes)
    {
        var l = b.Laser;
        var body = new List<string> { $"# blockConfig={{\"powerFactor\":{JsRounded(l.Power / 100, 6)},\"isVector\":true,\"crossDot\":false}}", "" };
        double? z = null;
        foreach (var piece in b.Pieces.Where(p => p.Points.Count >= 2))
        {
            if (z is not { } now || Math.Abs(now - piece.HeadZ) > 1e-6)
            {
                body.Add($"G0Z{Trim3(piece.HeadZ)}F600");
                z = piece.HeadZ;
            }
            body.Add($"G0{Pos(piece.Points[0], Trim3)}F180000");
            long feed = FeedAt(l, piece.Points[piece.Points.Count / 2]);
            for (int i = 1; i < piece.Points.Count; i++)
                body.Add($"G1{Pos(piece.Points[i], Trim3)}" + (i == 1 ? $"S{l.SValue}F{feed}" : ""));
        }
        if (z is null) return [];
        var v = new List<string> { $"# {tag} VECTOR HEAD", "# motion_start" };
        v.AddRange(SourceLines(l, true));
        v.AddRange(["M523P40", "", ""]);
        v.AddRange(PassLoop(passes, body));
        v.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        return v;
    }

    /// <summary>A curved surface's fill: one engraving block, the head moving between bands with the laser off.</summary>
    List<string> RenderBandedFill(BandedFillBlock bf, string tag, int passes)
    {
        var b = bf.Fill;
        var l = b.Laser;
        long s = l.SValue;
        var body = new List<string> { BitmapCfg(l.Power / 100, b.LinesPerCm / 10, 0, s, "grayscale", 0), "", "", $"G1F{l.FValue}S0" };
        bool first = true;
        foreach (var (z, segs) in bf.Bands)
        {
            if (segs.Count == 0) continue;
            body.Add($"G1 Z{Trim3(z)} F600 S0");
            // F is modal: back to the burn speed, or the band's lines would run at the head's 10 mm/s.
            long feed = FeedAt(l, Vec2.Lerp(segs[0].Start, segs[0].End, 0.5));
            body.Add($"G1F{feed}S0");
            foreach (var seg in segs)
            {
                // Slower where the surface slopes away (each line's own share).
                long want = FeedAt(l, Vec2.Lerp(seg.Start, seg.End, 0.5));
                if (want != feed) { body.Add($"G1F{want}S0"); feed = want; }
                body.Add(first ? $"G0{Pos(seg.Start, Fixed3)}" : $"G1{Pos(seg.Start, Fixed3)}S0");
                first = false;
                BurnTo(body, seg.Start, seg.End, s, b.PowerScale, p => Pos(p, Fixed3));
            }
        }
        if (first) return [];
        var v = BitmapHead(tag, l, "M523P40");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    List<string> RenderBlock(Block block, string tag, int passes) => block switch
    {
        BandedVectorBlock b => RenderBandedVector(b, tag, passes),
        BandedFillBlock b => RenderBandedFill(b, tag, passes),
        VectorBlock { Drop: not null } b => RenderSteppedVector(b, tag, passes),
        VectorBlock b => RenderVector(b, tag, passes),
        FillBlock b => RenderFill(b, tag, passes),
        LoopsBlock b => RenderLoops(b, tag, passes),
        PointsBlock b => RenderPoints(b, tag, passes),
        DotBlock b when !Flat => RenderDotsU(b, tag, passes),
        DotBlock b => RenderDots(b, tag, passes),
        _ => throw new ArgumentException("unknown block type", nameof(block)),
    };

    List<string> RenderVector(VectorBlock b, string tag, int passes)
    {
        var pts = b.Path.EmittedPoints();
        if (pts.Count < 2) return []; // nothing to burn
        var l = b.Laser;
        var body = new List<string>
        {
            $"# blockConfig={{\"powerFactor\":{JsRounded(l.Power / 100, 6)},\"isVector\":true,\"crossDot\":false}}",
            $"G0{Pos(pts[0], Trim3)}",
        };
        for (int i = 1; i < pts.Count; i++)
            body.Add($"G1{Pos(pts[i], Trim3)}" + (i == 1 ? $"S{l.SValue}F{Feed(l)}" : ""));
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
    /// end, exactly like xTool Studio. With one pass (whole-job rounds, or a pause after each pass)
    /// <see cref="Render"/> has already put the head at this pass's depth and leaves it there for
    /// the next path, so nothing moves Z here.
    /// </summary>
    List<string> RenderSteppedVector(VectorBlock b, string tag, int passes)
    {
        var pts = b.Path.EmittedPoints();
        if (pts.Count < 2) return [];
        var l = b.Laser;
        var drop = b.Drop!;
        var path = new List<string> { $"G0{Pos(pts[0], Trim3)}" };
        for (int i = 1; i < pts.Count; i++)
            path.Add($"G1{Pos(pts[i], Trim3)}" + (i == 1 ? $"S{l.SValue}F{Feed(l)}" : ""));
        var v = new List<string> { $"# {tag} VECTOR HEAD", "# motion_start" };
        v.AddRange(SourceLines(l, true));
        v.AddRange(["M523P40", "", ""]);
        v.Add($"# blockConfig={{\"powerFactor\":{JsRounded(l.Power / 100, 6)},\"isVector\":true,\"crossDot\":false}}");
        double first = passes > 1 ? drop.FirstMm : 0;
        if (first > 0) v.AddRange(ZMoveAny(-first));
        v.AddRange(["# MUST", path[0]]);
        v.AddRange(path);
        for (int p = 2; p <= passes; p++)
        {
            if (drop.PerPassMm > 0) v.AddRange(ZMoveAny(-drop.PerPassMm));
            v.AddRange(path);
        }
        v.AddRange(["", $"# {tag} VECTOR TAIL", "# motion_end"]);
        double back = passes > 1 ? drop.DepthFor(passes) : 0;
        if (back > 0) v.AddRange([.. ZMoveAny(back), "# MUST"]);
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

    /// <summary>
    /// How many lines apart to burn when spreading them out: neighbours about 0.4 mm apart in each
    /// sweep (heat from one line has mostly gone by the time the next one beside it is burned).
    /// </summary>
    public static int SpreadStride(double lineSpacingMm) =>
        lineSpacingMm > 0 && double.IsFinite(lineSpacingMm) ? Math.Clamp((int)Math.Round(0.4 / lineSpacingMm), 2, 32) : 2;

    /// <summary>Rows 0…n−1 in spread order: 0, k, 2k, … then 1, 1+k, … (k ≤ 1: in order).</summary>
    public static IEnumerable<int> SpreadOrder(int n, int stride, bool scatter = false)
    {
        if (scatter && stride > 1 && n > 2) { foreach (int i in Scattered(n, stride)) yield return i; yield break; }
        if (stride <= 1) { for (int i = 0; i < n; i++) yield return i; yield break; }
        for (int start = 0; start < Math.Min(stride, n); start++)
            for (int i = start; i < n; i += stride) yield return i;
    }

    /// <summary>
    /// Rows 0…n−1 in no set order, each next one at least <paramref name="stride"/> rows from the
    /// last whenever any such are left: heat spread like <see cref="SpreadOrder"/>, but with no
    /// repeating pattern of rows burned early (cool wood) and late (beside charred ones) to show.
    /// The same every time for the same rows.
    /// </summary>
    public static List<int> Scattered(int n, int stride)
    {
        var rnd = new Random(unchecked(n * 7919 + stride * 104729));
        var left = Enumerable.Range(0, n).ToList();
        var o = new List<int>(n);
        int prev = -stride * 2;
        while (left.Count > 0)
        {
            int pick = -1;
            for (int t = 0; t < 32 && pick < 0; t++)
            {
                int k = rnd.Next(left.Count);
                if (Math.Abs(left[k] - prev) >= stride) pick = k;
            }
            if (pick < 0) pick = left.FindIndex(r => Math.Abs(r - prev) >= stride);
            if (pick < 0) pick = rnd.Next(left.Count); // only near ones are left
            prev = left[pick];
            o.Add(prev);
            left[pick] = left[^1];
            left.RemoveAt(left.Count - 1);
        }
        return o;
    }

    /// <summary>The scan lines (each one or more segments on the same line) burned in spread order, alternating direction as they go.</summary>
    static List<FillSegment> SpreadOut(List<FillSegment> lines, int stride, bool scatter = false)
    {
        var groups = new List<List<FillSegment>>();
        foreach (var s in lines)
        {
            if (groups.Count == 0 || Math.Abs(groups[^1][0].Y - s.Y) > 1e-9) groups.Add([]);
            groups[^1].Add(s);
        }
        static FillSegment Flip(FillSegment s) => new(s.Y, s.X1, s.X0, s.End, s.Start);
        var o = new List<FillSegment>(lines.Count);
        bool forward = true;
        foreach (int i in SpreadOrder(groups.Count, stride, scatter))
        {
            var run = groups[i].Select(s => s.X1 >= s.X0 ? s : Flip(s)).OrderBy(s => s.X0).ToList();
            if (forward) o.AddRange(run);
            else for (int k = run.Count - 1; k >= 0; k--) o.Add(Flip(run[k]));
            forward = !forward;
        }
        return o;
    }

    static List<FillSegment> MakeFillLines(FillBlock b)
    {
        if (b.Lines is { } given) return [.. given];
        var lines = MakeFullLines(b);
        if (b.Spread > 1) lines = SpreadOut(lines, b.Spread, b.Scatter);
        if (b.ReverseOrder)
        {
            // From the far side: the lines in reverse order, each run the other way so the zigzag still joins up.
            lines = [.. Enumerable.Reverse(lines).Select(s => new FillSegment(s.Y, s.X1, s.X0, s.End, s.Start))];
        }
        if (b.Unidirectional)
            // Every line burned the same way: the ones that ran backwards are turned round.
            lines = [.. lines.Select(s => s.X1 >= s.X0 ? s : new FillSegment(s.Y, s.X1, s.X0, s.End, s.Start))];
        if (b.DotWidthMm > 0)
        {
            // Shorter by the beam width, half at each end; lines no longer than it are left out.
            double half = b.DotWidthMm / 2;
            var o = new List<FillSegment>(lines.Count);
            foreach (var s in lines)
            {
                double len = s.Start.Dist(s.End);
                if (len <= b.DotWidthMm + 1e-9) continue;
                var u = (s.End - s.Start) / len;
                double dir = s.X1 >= s.X0 ? 1 : -1;
                o.Add(new(s.Y, s.X0 + dir * half, s.X1 - dir * half, s.Start + u * half, s.End - u * half));
            }
            lines = o;
        }
        if (b.ClipRect is not { } clip) return lines;
        // Marking in sections: each line cut to the section, in the same order.
        var cut = new List<FillSegment>(lines.Count);
        foreach (var s in lines)
            foreach (var run in Sections.ClipPolyline([s.Start, s.End], clip))
            {
                if (run.Count < 2 || run[0].Dist(run[^1]) < 1e-6) continue;
                double len = s.Start.Dist(s.End);
                double t0 = len > 0 ? s.Start.Dist(run[0]) / len : 0, t1 = len > 0 ? s.Start.Dist(run[^1]) / len : 1;
                cut.Add(new(s.Y, s.X0 + (s.X1 - s.X0) * t0, s.X0 + (s.X1 - s.X0) * t1, run[0], run[^1]));
            }
        return cut;
    }

    static List<FillSegment> MakeFullLines(FillBlock b)
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

    /// <summary>
    /// One burned scan line to <paramref name="end"/> at S <paramref name="s"/>, or, with a power
    /// field, in pieces each at its own lower S (the field never asks for more than <paramref name="s"/>).
    /// </summary>
    static void BurnTo(List<string> body, Vec2 start, Vec2 end, long s, PowerField? field, Func<Vec2, string> pos)
    {
        if (field is null || field.Lowest >= 1) { body.Add($"G1{pos(end)}S{s}"); return; }
        foreach (var (at, f) in field.Along(start, end))
            body.Add($"G1{pos(at)}S{Math.Clamp((long)Math.Round(s * f, MidpointRounding.AwayFromZero), 0, s)}");
    }

    List<string> RenderFill(FillBlock b, string tag, int passes)
    {
        var segs = FillLines(b);
        if (segs.Count == 0) return [];
        var l = b.Laser;
        long s = l.SValue;
        double over = b.OverscanMm;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.LinesPerCm / 10, 0, s, "grayscale", over), "", "", $"G1F{Feed(l)}S0",
        };
        if (b.LineDwellMs > 0)
        {
            // A pause before each line: written out in full, one move at a time.
            string? at0 = null;
            foreach (var seg in segs)
            {
                var (runUp, runOut) = Overscan(seg, over);
                if (at0 is not null) body.Add($"G4P{b.LineDwellMs}");
                at0 = Pos(runUp, Fixed3);
                body.Add(body.Count <= 4 ? $"G0{at0}" : $"G1{at0}S0");
                if (over > 0) body.Add($"G1{Pos(seg.Start, Fixed3)}S0");
                BurnTo(body, seg.Start, seg.End, s, b.PowerScale, p => Pos(p, Fixed3));
                if (over > 0) body.Add($"G1{Pos(runOut, Fixed3)}S0");
            }
        }
        else if (Flat && FoldAngle(b.AngleDeg) == 0)
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
                BurnTo(body, seg.Start, seg.End, s, b.PowerScale, p => $"X{Fixed3(p.X)}");
                if (over > 0)
                {
                    lastX = Fixed3(seg.X1 + dir * over);
                    body.Add($"G1X{lastX}S0");
                }
            }
        }
        else
        {
            string? last = null;
            foreach (var seg in segs)
            {
                var (runUp, runOut) = Overscan(seg, over);
                string at = Pos(runUp, Fixed3);
                if (last is null) body.Add($"G0{at}");
                else if (last != at) body.Add($"G1{at}S0");
                if (over > 0) body.Add($"G1{Pos(seg.Start, Fixed3)}S0");
                last = Pos(seg.End, Fixed3);
                BurnTo(body, seg.Start, seg.End, s, b.PowerScale, p => Pos(p, Fixed3));
                if (over > 0)
                {
                    last = Pos(runOut, Fixed3);
                    body.Add($"G1{last}S0");
                }
            }
        }
        var v = BitmapHead(tag, l, "M523P40");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    /// <summary>Offset-fill loops: one engraving block, each loop traced closed, the laser off between loops.</summary>
    List<string> RenderLoops(LoopsBlock b, string tag, int passes)
    {
        var loops = b.Loops.Where(l => l.Count >= 2).ToList();
        if (loops.Count == 0) return [];
        var l = b.Laser;
        long s = l.SValue;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.SpacingMm > 0 ? 1 / b.SpacingMm : 10, 0, s, "grayscale"), "", "", $"G1F{Feed(l)}S0",
        };
        bool first = true;
        foreach (var loop in loops)
        {
            string at = Pos(loop[0], Fixed3);
            body.Add(first ? $"G0{at}" : $"G1{at}S0");
            first = false;
            for (int i = 1; i < loop.Count + (b.Closed ? 1 : 0); i++)
                body.Add($"G1{Pos(loop[i % loop.Count], Fixed3)}S{s}");
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
        foreach (int r in SpreadOrder(b.Bitmap.Height, b.Spread, b.Scatter))
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
                if (b.AngleDeg != 0)
                {
                    // Rows at an angle: both X and Y change from dot to dot.
                    var p = b.At(cols[k], r);
                    string xy = $"X{Fixed3(p.X)}Y{Fixed3(p.Y)}";
                    if (first)
                    {
                        body.Add($"G0{xy}");
                        body.Add("");
                        body.Add(levels is null ? $"G25{xy}S{l.SValue}" : $"G25{xy}{power}");
                        first = false;
                    }
                    else body.Add($"G25{xy}" + power);
                    continue;
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
            if (!b.Unidirectional) forward = !forward;
        }
        if (first) return []; // blank bitmap
        var v = BitmapHead(tag, l, "M523P10");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    /// <summary>Dot mode along a path: a pulse at each point, the beam moving unlit between them.</summary>
    List<string> RenderPoints(PointsBlock b, string tag, int passes)
    {
        if (b.Points.Count == 0) return [];
        var l = b.Laser;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, 1, 0, l.SValue, "Threshold"), "", "", $"G1F{Feed(l)}S0", $"G25T{b.DotUs / 10}",
        };
        bool first = true;
        foreach (var p in b.Points)
        {
            string at = Pos(p, Fixed3);
            if (Flat)
            {
                if (first) { body.Add($"G0{at}"); body.Add(""); }
                body.Add($"G25{at}" + (first ? $"S{l.SValue}" : ""));
            }
            else
            {
                // The attachment moves first, then the galvo fires on its own axis.
                int u = at.IndexOf('U');
                string galvo = Mode is WorkMode.Rotary ? at[..u] + $"Y{Fixed3(AttachmentLine)}" : $"X{Fixed3(AttachmentLine)}" + at[(at.IndexOf('Y'))..];
                body.Add($"G0{at[u..].Split('Y')[0]}");
                body.Add($"G25{galvo}" + (first ? $"S{l.SValue}" : ""));
            }
            first = false;
        }
        var v = BitmapHead(tag, l, "M523P10");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }

    /// <summary>
    /// Dots with an attachment: the attachment steps (<c>G0U</c>) between rows of dots that the galvo
    /// fires along its own axis, as in xTool Studio's conveyor calibration program. On the rotary the
    /// rows run along X; on the conveyor they run along Y, one column of the picture at a time.
    /// </summary>
    List<string> RenderDotsU(DotBlock b, string tag, int passes)
    {
        var l = b.Laser;
        double pitch = 25.4 / b.Dpi;
        bool rotary = Mode is WorkMode.Rotary;
        var body = new List<string>
        {
            BitmapCfg(l.Power / 100, b.Dpi / 25.4,
                (long)Math.Round(b.MinPower * 10, MidpointRounding.AwayFromZero), l.SValue, b.Mode),
            "", "", $"G1F{l.FValue}S0", $"G25T{b.DotUs / 10}",
        };
        bool first = true, forward = true;
        long lastS = l.SValue;
        var levels = b.Levels;
        int lines = rotary ? b.Bitmap.Height : b.Bitmap.Width, along = rotary ? b.Bitmap.Width : b.Bitmap.Height;
        string park = Fixed3(AttachmentLine);
        var idx = new List<int>();
        // Rows spread out and one way, as on the flat bed.
        foreach (int k in SpreadOrder(lines, b.Spread, b.Scatter))
        {
            idx.Clear();
            for (int j = 0; j < along; j++)
                if (rotary ? b.Bitmap[j, k] : b.Bitmap[k, j]) idx.Add(j);
            if (idx.Count == 0) continue;
            if (!forward) idx.Reverse();
            // Where this row is, on the attachment.
            var rowPos = rotary ? new Vec2(0, b.Origin.Y + k * pitch) : new Vec2(b.Origin.X + k * pitch, 0);
            string u = Pos(rowPos, Fixed3);
            body.Add($"G0{u[u.IndexOf('U')..]}");
            for (int n = 0; n < idx.Count; n++)
            {
                int j = idx[n];
                string g = Fixed3(rotary ? b.Origin.X + j * pitch : b.Origin.Y + j * pitch);
                string power = "";
                if (levels is not null)
                {
                    int cell = rotary ? k * b.Bitmap.Width + j : j * b.Bitmap.Width + k;
                    long sv = b.LevelSValue(levels[cell]);
                    if (first || sv != lastS) power = $"S{sv}";
                    lastS = sv;
                }
                else if (first) power = $"S{l.SValue}";
                string pos = rotary ? $"X{g}" : $"Y{g}";
                body.Add(first ? $"G25{(rotary ? $"X{g}Y{park}" : $"X{park}Y{g}")}{power}" : $"G25{pos}{power}");
                first = false;
            }
            if (!b.Unidirectional) forward = !forward;
        }
        if (first) return [];
        var v = BitmapHead(tag, l, "M523P10");
        v.AddRange(PassLoop(passes, body));
        v.AddRange(BitmapTail(tag));
        return v;
    }
}
