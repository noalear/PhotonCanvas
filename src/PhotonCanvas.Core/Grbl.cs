using System.Globalization;
using System.Text;

namespace PhotonCanvas.Core;

/// <summary>What talks to the machine: xTool's own (the F2 Ultra, over Wi-Fi) or GRBL (most hobby lasers' controller, over USB).</summary>
public enum MachineKind { XTool, Grbl }

/// <summary>Where a GRBL machine's (0, 0) is, and which way Y goes from it.</summary>
public enum GrblOrigin
{
    /// <summary>Front left, Y going back (positive): the usual for GRBL.</summary>
    FrontLeft,
    /// <summary>Back left (where many machines home), Y going forward as negative numbers.</summary>
    BackLeftNegative,
    /// <summary>Back left, Y going forward as positive numbers (the same way as the design).</summary>
    BackLeft,
}

/// <summary>
/// A laser the user has: built in (the F2 Ultra) or added by them. GRBL machines are described by
/// their bed, laser and controller settings; PhotonCanvas writes GRBL G-code for them and sends it
/// over USB. Kept in the settings.
/// </summary>
public sealed record MachineSetup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public MachineKind Kind { get; init; } = MachineKind.Grbl;
    /// <summary>The working area (mm): across, and front to back.</summary>
    public double BedW { get; init; } = 400;
    public double BedH { get; init; } = 300;
    /// <summary>"CO2", "Diode" or "Fiber": shown, and CO2 tubes don't do dot mode.</summary>
    public string Laser { get; init; } = "Diode";
    public double PowerW { get; init; } = 10;
    /// <summary>Fastest and slowest speed the machine moves while marking (mm/s).</summary>
    public double MaxSpeed { get; init; } = 300;
    public double MinSpeed { get; init; } = 1;
    /// <summary>GRBL's S for full power ($30, usually 1000).</summary>
    public int SMax { get; init; } = 1000;
    /// <summary>The serial port (COM3, /dev/ttyUSB0) and its speed.</summary>
    public string Port { get; init; } = "";
    public int Baud { get; init; } = 115200;
    public GrblOrigin Origin { get; init; } = GrblOrigin.FrontLeft;
    /// <summary>Turn air assist on while marking (M8, off with M9).</summary>
    public bool AirAssist { get; init; } = true;
    /// <summary>Picture rows: run this far past each end, laser off, so the head is at speed where it burns (mm).</summary>
    public double RasterOverscanMm { get; init; } = 3;
    /// <summary>xTool machines: the model's device code (as it reports itself) and its tag in the G-code (# GS004-4 HEAD…).</summary>
    public string DeviceCode { get; init; } = "";
    public string GcodeTag { get; init; } = "";
    /// <summary>xTool machines: whether the MOPA (fiber) laser's limits are known (the F2 Ultra's), so MOPA settings are allowed.</summary>
    public bool MopaKnown { get; init; }
    /// <summary>Not yet run on a real one: shown with the machine.</summary>
    public bool Untested { get; init; }

    /// <summary>The F2 Ultra (built in, over Wi-Fi with xTool's own protocol).</summary>
    public static readonly MachineSetup F2Ultra = new()
    {
        Id = new Guid("2f2f0000-0000-4000-8000-000000000001"), Name = "xTool F2 Ultra", Kind = MachineKind.XTool,
        BedW = MachineProfile.F2Ultra.FieldW, BedH = MachineProfile.F2Ultra.FieldH, Laser = "Diode + fiber", PowerW = 60,
        MaxSpeed = MachineProfile.F2Ultra.MaxSpeed, MinSpeed = MachineProfile.F2Ultra.MinSpeed,
        DeviceCode = MachineProfile.F2Ultra.DeviceCode, GcodeTag = MachineProfile.F2Ultra.GcodeTag, MopaKnown = true,
    };

    /// <summary>
    /// The rest of xTool's galvo family, which speak the same protocol (V2) and G-code as the F2 Ultra
    /// (from xTool Studio's own model list): work area, lasers and G-code tag. Not yet run on any.
    /// </summary>
    public static readonly IReadOnlyList<MachineSetup> XToolFamily =
    [
        XTool("xTool F2 Ultra (Single)", "GS007-CLASS-4", "GS007-4", 220, "Fiber", 60, mopa: true),
        XTool("xTool F1 Ultra", "GS003", "GS002", 220, "Diode + fiber", 20),
        XTool("xTool F1 Ultra (first)", "GS002", "GS002", 220, "Diode + fiber", 20),
        XTool("xTool F2", "GS006", "GS006", 115, "Diode + infrared", 15),
        XTool("xTool F1 Lite", "GS005", "F1", 115, "Diode", 10),
        XTool("xTool F2 Ultra UV", "GS009-CLASS-4", "GS009-4", 200, "UV", 5),
    ];

    static MachineSetup XTool(string name, string code, string tag, double field, string laser, double watts, bool mopa = false) => new()
    {
        Id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(code))),
        Name = name, Kind = MachineKind.XTool, DeviceCode = code, GcodeTag = tag, BedW = field, BedH = field, Laser = laser, PowerW = watts,
        MaxSpeed = MachineProfile.F2Ultra.MaxSpeed, MinSpeed = MachineProfile.F2Ultra.MinSpeed, MopaKnown = mopa, Untested = true,
    };

    /// <summary>The built-in xTool machine with this device code, if PhotonCanvas knows it.</summary>
    public static MachineSetup? ForDeviceCode(string? code) =>
        code is null ? null : code == F2Ultra.DeviceCode ? F2Ultra : XToolFamily.FirstOrDefault(m => m.DeviceCode == code);

    /// <summary>
    /// A starting point for a Full Spectrum Laser 5th Gen Hobby 20×12 (45 W CO2) running GRBL after a
    /// controller swap: its bed and a CO2's speeds. Check the origin and $30 against the new controller.
    /// </summary>
    public static MachineSetup FullSpectrumHobby => new()
    {
        Name = "Full Spectrum Hobby 20×12 (GRBL)", Laser = "CO2", PowerW = 45, BedW = 508, BedH = 305,
        MaxSpeed = 500, MinSpeed = 1, SMax = 1000, Origin = GrblOrigin.FrontLeft, AirAssist = true, RasterOverscanMm = 4,
    };

    /// <summary>The limits PhotonCanvas checks jobs against.</summary>
    public MachineProfile Profile => Kind == MachineKind.XTool
        ? (DeviceCode.Length == 0 || DeviceCode == MachineProfile.F2Ultra.DeviceCode ? MachineProfile.F2Ultra : MachineProfile.F2Ultra with
        {
            DeviceCode = DeviceCode, GcodeTag = GcodeTag, FieldW = BedW, FieldH = BedH,
            Mopa = MopaKnown ? MachineProfile.F2UltraMopa : [],
        })
        : new MachineProfile
    {
        DeviceCode = "GRBL", GcodeTag = "GRBL", FieldW = BedW, FieldH = BedH, MinSpeed = MinSpeed, MaxSpeed = MaxSpeed,
        ZOffset = 0, ZMax = 0, Mopa = [],
    };

    /// <summary>"Full Spectrum Hobby 20×12 (GRBL) · CO2 45 W · 508 × 305 mm".</summary>
    public string Describe => $"{Name} · {Laser} {PowerW:0.#} W · {BedW:0.#} × {BedH:0.#} mm";
}

/// <summary>
/// GRBL G-code for a job (GRBL 1.1 in laser mode, $32=1): cuts and scores as G1 moves with the
/// laser's S, fills as scan lines, pictures as rows whose power changes along them (M4: power
/// follows speed, so corners don't scorch), dot mode as short dwells. Coordinates in mm from the
/// machine's origin, speeds in mm/min.
/// </summary>
public static class GrblWriter
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write(Job job, MachineSetup m, string title = "")
    {
        if (job.FollowsSurface || job.Mode is not WorkMode.Flat)
            throw new JobException("GRBL machines take flat-bed jobs only (no rotary, conveyor or curved surface).");
        var o = new StringBuilder();
        o.AppendLine($"; {title} — PhotonCanvas for {m.Name} (GRBL)".Replace("  ", " "));
        o.AppendLine($"; {m.Laser} {m.PowerW:0.#} W, bed {m.BedW:0.#} × {m.BedH:0.#} mm, S{m.SMax} = full power");
        o.AppendLine("G21 G90 G17");
        o.AppendLine("M5 S0");
        if (m.AirAssist) o.AppendLine("M8");
        var w = new Writer(o, m);
        void Run(Block b, int pass)
        {
            switch (b)
            {
                case VectorBlock v: w.Path(v.Path.Points, v.Path.Closed, v.Laser); break;
                case LoopsBlock l: foreach (var loop in l.Loops) w.Path(loop, l.Closed, l.Laser); break;
                case FillBlock f: w.Fill(f.ForPass(pass)); break;
                case PointsBlock p: w.Dots(p); break;
                case DotBlock d: w.Raster(d.ForPass(pass)); break;
                default: throw new JobException($"GRBL machines can't run this kind of work ({b.GetType().Name}).");
            }
            if (b.PauseAfterS > 0) o.AppendLine($"M5\nG4 P{b.PauseAfterS.ToString("0.###", Inv)} ; pause after the path");
        }
        switch (job.PassMode)
        {
            case PassMode.Rounds r:
                for (int round = 1; round <= job.Rounds; round++)
                {
                    if (round > 1 && r.DelayS > 0) o.AppendLine($"M5\nG4 P{r.DelayS.ToString("0.###", Inv)} ; pause between passes");
                    foreach (var b in job.Blocks.Where(b => b.Laser.Passes >= round)) Run(b, round);
                }
                break;
            default:
                foreach (var b in job.Blocks)
                    for (int p = 1; p <= Math.Max(1, b.Laser.Passes); p++) Run(b, p);
                break;
        }
        o.AppendLine("M5 S0");
        if (m.AirAssist) o.AppendLine("M9");
        o.AppendLine("G0 X0 Y0");
        return o.ToString();
    }

    /// <summary>The G-code for tracing a box (laser off): framing on a GRBL machine.</summary>
    public static string Frame(Rect2 box, MachineSetup m)
    {
        var w = new Writer(new StringBuilder(), m);
        string P(Vec2 p) => w.XY(p);
        double f = Math.Min(m.MaxSpeed, 100) * 60;
        return $"G21 G90\nM5 S0\nG0 {P(box.TopLeft)}\nG1 {P(new Vec2(box.MaxX, box.MinY))} F{f:0} S0\nG1 {P(box.BottomRight)}\nG1 {P(new Vec2(box.MinX, box.MaxY))}\nG1 {P(box.TopLeft)}\n";
    }

    sealed class Writer(StringBuilder o, MachineSetup m)
    {
        string _mode = "";
        double _feed = -1;

        public string XY(Vec2 p)
        {
            double y = m.Origin switch { GrblOrigin.FrontLeft => m.BedH - p.Y, GrblOrigin.BackLeftNegative => -p.Y, _ => p.Y };
            return $"X{p.X.ToString("0.###", Inv)} Y{y.ToString("0.###", Inv)}";
        }

        int S(double powerPct) => (int)Math.Round(Math.Clamp(powerPct, 0, 100) / 100 * m.SMax);

        void Mode(string mode)
        {
            if (_mode == mode) return;
            o.AppendLine(mode);
            _mode = mode;
        }

        string F(double mmPerS)
        {
            double f = Math.Round(Math.Clamp(mmPerS, m.MinSpeed, m.MaxSpeed) * 60);
            if (f == _feed) return "";
            _feed = f;
            return $" F{f:0}";
        }

        public void Path(IReadOnlyList<Vec2> pts, bool closed, LaserSettings l)
        {
            if (pts.Count < 2) return;
            Mode("M4");
            o.AppendLine($"G0 {XY(pts[0])}");
            int s = S(l.Power);
            bool first = true;
            foreach (var p in pts.Skip(1).Concat(closed && pts[0].Dist(pts[^1]) > 1e-6 ? [pts[0]] : []))
            {
                o.AppendLine($"G1 {XY(p)}{(first ? $"{F(l.Speed)} S{s}" : "")}");
                first = false;
            }
        }

        public void Fill(FillBlock f)
        {
            Mode("M4");
            int s = S(f.Laser.Power);
            string dwell = (f.LineDwellMs / 1000.0).ToString("0.###", Inv);
            foreach (var seg in Job.FillLines(f))
            {
                var dir = (seg.End - seg.Start) * (1 / Math.Max(1e-9, seg.Start.Dist(seg.End)));
                // Run-ups at each end, laser off, as on the galvo (cut short where the bed ends).
                var runUp = seg.Start - dir * Room(seg.Start, dir * -1, f.OverscanMm);
                o.AppendLine($"G0 {XY(runUp)}");
                if (f.LineDwellMs > 0) o.AppendLine($"G4 P{dwell}");
                if (f.OverscanMm > 0) o.AppendLine($"G1 {XY(seg.Start)}{F(f.Laser.Speed)} S0");
                if (f.PowerScale is { Lowest: < 1 } field)
                {
                    bool firstPiece = true;
                    foreach (var (at, k) in field.Along(seg.Start, seg.End))
                    {
                        o.AppendLine($"G1 {XY(at)}{(firstPiece ? F(f.Laser.Speed) : "")} S{(int)Math.Round(s * k)}");
                        firstPiece = false;
                    }
                }
                else o.AppendLine($"G1 {XY(seg.End)}{F(f.Laser.Speed)} S{s}");
                if (f.OverscanMm > 0) o.AppendLine($"G1 {XY(seg.End + dir * Room(seg.End, dir, f.OverscanMm))} S0");
            }
        }

        /// <summary>How far from <paramref name="p"/> a run-up can go along <paramref name="dir"/> (at most <paramref name="want"/>) and stay on the bed: GRBL's soft limits would stop the job.</summary>
        double Room(Vec2 p, Vec2 dir, double want)
        {
            double t = want;
            if (dir.X > 1e-12) t = Math.Min(t, (m.BedW - p.X) / dir.X); else if (dir.X < -1e-12) t = Math.Min(t, -p.X / dir.X);
            if (dir.Y > 1e-12) t = Math.Min(t, (m.BedH - p.Y) / dir.Y); else if (dir.Y < -1e-12) t = Math.Min(t, -p.Y / dir.Y);
            return Math.Max(0, t);
        }

        public void Dots(PointsBlock p)
        {
            // Dwell with the laser held on (M3: constant power), moving with it off.
            Mode("M3");
            int s = S(p.Laser.Power);
            string dwell = (p.DotUs / 1e6).ToString("0.######", Inv);
            foreach (var pt in p.Points)
            {
                o.AppendLine($"G0 {XY(pt)} S0");
                o.AppendLine($"S{s} G4 P{dwell}");
            }
            o.AppendLine("S0");
        }

        /// <summary>A picture: row by row, runs of the same power as single moves, overscan at each end.</summary>
        public void Raster(DotBlock d)
        {
            Mode("M4");
            double pitch = 25.4 / d.Dpi;
            double a = d.AngleDeg * Math.PI / 180;
            var along = new Vec2(Math.Cos(a), Math.Sin(a));
            bool forward = true;
            foreach (int r in Job.SpreadOrder(d.Bitmap.Height, d.Spread, d.Scatter))
            {
                int c0 = -1, c1 = -1;
                for (int c = 0; c < d.Bitmap.Width; c++)
                    if (d.Bitmap[c, r]) { if (c0 < 0) c0 = c; c1 = c; }
                if (c0 < 0) continue;
                int step = forward ? 1 : -1, from = forward ? c0 : c1, to = forward ? c1 : c0;
                var dir = along * step;
                // Each dot covers half a pitch either side of its centre.
                var start = d.At(from, r) - dir * (pitch / 2);
                o.AppendLine($"G0 {XY(start - dir * Room(start, dir * -1, m.RasterOverscanMm))}");
                o.AppendLine($"G1 {XY(start)}{F(d.Laser.Speed)} S0");
                int PowerAt(int c) => !d.Bitmap[c, r] ? 0
                    : d.Levels is { } lv ? S(d.MinPower + (d.Laser.Power - d.MinPower) * lv[r * d.Bitmap.Width + c] / 255.0)
                    : S(d.Laser.Power);
                for (int c = from; c != to + step; c += step)
                {
                    int s = PowerAt(c);
                    // A run of the same power is one move: it ends at this dot's far edge when the next differs (or the row ends).
                    if (c == to || PowerAt(c + step) != s)
                        o.AppendLine($"G1 {XY(d.At(c, r) + dir * (pitch / 2))} S{s}");
                }
                var end = d.At(to, r) + dir * (pitch / 2);
                o.AppendLine($"G1 {XY(end + dir * Room(end, dir, m.RasterOverscanMm))} S0");
                if (!d.Unidirectional) forward = !forward;
            }
        }
    }
}
