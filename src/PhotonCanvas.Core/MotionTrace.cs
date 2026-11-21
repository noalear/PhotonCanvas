using System.Globalization;

namespace PhotonCanvas.Core;

/// <summary>
/// The laser's way through some motion G-code, timed: every move in order, where it ends (design mm),
/// when it starts, and whether the laser is on. It is read from the very lines the machine gets, so it
/// follows what the machine does: a fill's lines in the order they're written (spread, reversed, both
/// ways or one way, crosshatched, turned per pass), their run-ups, a picture's rows, offset loops,
/// dots along a path, wobble circles and pauses. Used to show where the laser is while a job runs.
/// </summary>
/// <remarks>
/// Times use the same speeds and costs as <see cref="Job.EstimateSeconds"/> (jumps at the galvo's jump
/// speed, lit moves at the feed, a few ms each time the laser goes on, each dot its duration plus the
/// step). They're good for sharing a block's time out between its moves; the run as a whole keeps to
/// the job's own estimate.
/// </remarks>
public sealed class MotionTrace
{
    // Each move ends at (X[i], Y[i]); it starts where the move before it ended (or at the start).
    readonly float[] _x, _y;
    readonly double[] _t; // when each move starts; _t[n] is the end of the last
    readonly bool[] _on;
    readonly Vec2 _start;

    MotionTrace(Vec2 start, List<float> x, List<float> y, List<double> t, List<bool> on)
    {
        _start = start;
        _x = [.. x];
        _y = [.. y];
        _t = [.. t];
        _on = [.. on];
    }

    /// <summary>How many moves.</summary>
    public int Count => _x.Length;

    /// <summary>Seconds for all the moves.</summary>
    public double Seconds => _t.Length == 0 ? 0 : _t[^1];

    /// <summary>Where the first move starts.</summary>
    public Vec2 Start => _start;

    /// <summary>Where the last move ends.</summary>
    public Vec2 End => Count == 0 ? _start : To(Count - 1);

    /// <summary>Where move <paramref name="i"/> ends.</summary>
    public Vec2 To(int i) => new(_x[i], _y[i]);

    /// <summary>Where move <paramref name="i"/> starts.</summary>
    public Vec2 From(int i) => i == 0 ? _start : To(i - 1);

    /// <summary>Whether the laser is on during move <paramref name="i"/>.</summary>
    public bool Burning(int i) => _on[i];

    /// <summary>When move <paramref name="i"/> starts (s).</summary>
    public double StartOf(int i) => _t[i];

    /// <summary>The laser <paramref name="seconds"/> into the moves: where it is, whether it's on, and which move it's on.</summary>
    public (Vec2 At, bool Burning, int Move) At(double seconds)
    {
        if (Count == 0) return (_start, false, -1);
        if (seconds <= 0) return (_start, false, 0);
        if (seconds >= Seconds) return (End, false, Count - 1);
        // The last move starting at or before the time.
        int lo = 0, hi = Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_t[mid] <= seconds) lo = mid;
            else hi = mid - 1;
        }
        double d = _t[lo + 1] - _t[lo];
        double f = d > 0 ? (seconds - _t[lo]) / d : 1;
        return (Vec2.Lerp(From(lo), To(lo), Math.Clamp(f, 0, 1)), _on[lo], lo);
    }

    /// <summary>
    /// The scan line move <paramref name="i"/> is part of: the lit moves either side of it running the
    /// same way (a fill's line, or a row of a picture's dots), from its first point to its last.
    /// </summary>
    public (Vec2 A, Vec2 B)? LineAround(int i, int maxMoves = 20000)
    {
        if (i < 0 || i >= Count || !_on[i]) return null;
        var dir = Direction(i);
        if (dir is not { } d) return null;
        bool Same(int k) => _on[k] && Direction(k) is { } e && Math.Abs(e.Dot(d)) > 0.985;
        int a = i, b = i;
        while (a > 0 && i - a < maxMoves && Same(a - 1)) a--;
        while (b + 1 < Count && b - i < maxMoves && Same(b + 1)) b++;
        return (From(a), To(b));
    }

    Vec2? Direction(int i)
    {
        var v = To(i) - From(i);
        double l = v.Length;
        return l > 1e-6 ? v / l : null;
    }

    /// <summary>
    /// The lit moves between <paramref name="fromSeconds"/> and move <paramref name="i"/> (at <paramref name="atSeconds"/>,
    /// where the laser is now), newest last, at most <paramref name="max"/> of them: the trail it has just left.
    /// </summary>
    public List<(Vec2 A, Vec2 B)> Trail(int i, double atSeconds, double fromSeconds, int max = 1500)
    {
        var o = new List<(Vec2, Vec2)>();
        if (i < 0 || i >= Count) return o;
        var now = At(atSeconds).At;
        if (_on[i]) o.Add((From(i), now));
        for (int k = i - 1; k >= 0 && o.Count < max && _t[k + 1] > fromSeconds; k--)
            if (_on[k]) o.Add((From(k), To(k)));
        o.Reverse();
        return o;
    }

    /// <summary>
    /// Reads motion G-code: <c>G0</c>/<c>G1</c> moves (X, Y, and U on an attachment), <c>G25</c> dots,
    /// <c>G4</c> pauses and Z moves. Comments, set-up and other codes take no time here.
    /// </summary>
    /// <param name="mode">How the job maps onto the machine: U back to design Y on the rotary, design X on the conveyor.</param>
    /// <param name="fieldW">The galvo field's width (the conveyor's U is 0 in its middle).</param>
    /// <param name="start">Where the laser is before the first move (design mm).</param>
    /// <param name="shift">Added to every position (e.g. the MOPA's offset, to put its work back where it was drawn).</param>
    public static MotionTrace Read(IEnumerable<string> lines, WorkMode mode, double fieldW, Vec2 start, Vec2 shift = default)
    {
        var xs = new List<float>();
        var ys = new List<float>();
        var ts = new List<double> { 0 };
        var on = new List<bool>();
        var pos = start; // design mm, without the shift
        double t = 0;
        double g1Feed = 0, g0Feed = Job.JumpMmS * 60, s = 0, dotUs = 0;
        double? zNow = null; // the head's height, once an absolute one is known
        bool relative = false, lastLit = false;
        var at = start + shift;

        void Add(Vec2 to, double seconds, bool lit)
        {
            var p = to + shift;
            xs.Add((float)p.X);
            ys.Add((float)p.Y);
            t += Math.Max(0, seconds);
            ts.Add(t);
            on.Add(lit);
            at = p;
        }

        foreach (var raw in lines)
        {
            if (string.IsNullOrEmpty(raw) || raw[0] == '#' || raw[0] == ';') continue;
            int g = -1;
            double? x = null, y = null, z = null, u = null, f = null, sw = null, p = null, tw = null;
            var line = raw.AsSpan();
            int i = 0;
            bool skip = false;
            while (i < line.Length)
            {
                char c = char.ToUpperInvariant(line[i]);
                if (c == '"' || c == ';' || c == '(') break; // quoted commands (G198 "...") and comments
                if (!char.IsLetter(c)) { i++; continue; }
                int j = ++i;
                while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-' or '+')) j++;
                if (!double.TryParse(line[i..j], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { i = j; continue; }
                i = j;
                switch (c)
                {
                    case 'G':
                        if (g >= 0) { skip = true; break; } // two G words: not a move we know
                        g = (int)v;
                        break;
                    case 'M': skip = true; break;
                    case 'X': x = v; break;
                    case 'Y': y = v; break;
                    case 'Z': z = v; break;
                    case 'U': u = v; break;
                    case 'F': f = v; break;
                    case 'S': sw = v; break;
                    case 'P': p = v; break;
                    case 'T': tw = v; break;
                }
                if (skip) break;
            }
            if (skip) continue;
            switch (g)
            {
                case 90: relative = false; continue;
                case 91: relative = true; continue;
                case 4:
                    if (p is { } ms && ms > 0) { Add(pos, ms / 1000, false); lastLit = false; }
                    continue;
                case 0 or 1 or 25:
                    break;
                default:
                    continue;
            }
            if (g == 0 && f is { } f0 && f0 > 0) g0Feed = f0;
            if (g == 1 && f is { } f1 && f1 > 0) g1Feed = f1;
            if (sw is { } sv && g != 25) s = sv;
            if (g == 25 && tw is { } dur) dotUs = dur * 10; // T: the dot's duration, in 10 µs

            var to = Position(pos, x, y, u, mode, fieldW, relative);
            double dist = to.Dist(pos);
            if (z is { } dz && dist < 1e-9)
            {
                // The head moving up or down (focus, step-down, a curved surface's bands).
                double feed = Math.Max(1, (g == 0 ? g0Feed : g1Feed) / 60);
                double moveS = relative ? Math.Abs(dz) / feed : zNow is { } zn ? Math.Abs(dz - zn) / feed : Job.BandMoveSeconds;
                if (!relative) zNow = dz;
                Add(pos, moveS, false);
                lastLit = false;
                continue;
            }
            if (g == 25)
            {
                if (x is null && y is null && u is null) continue; // the dot length on its own
                // A dot: the step to it, then the pulse.
                Add(to, dist / JumpOr(g0Feed) + dotUs * 1e-6 + Job.DotStepSeconds, true);
                lastLit = false;
                pos = to;
                continue;
            }
            if (dist < 1e-9) continue;
            bool lit = g == 1 && s > 0;
            double speed = g == 0 ? JumpOr(g0Feed) : g1Feed > 0 ? g1Feed / 60 : Job.JumpMmS;
            Add(to, dist / speed + (lit && !lastLit ? Job.LaserOnSeconds : 0), lit);
            lastLit = lit;
            pos = to;
        }
        return new MotionTrace(start + shift, xs, ys, ts, on);
    }

    static double JumpOr(double g0Feed) => Math.Min(Job.JumpMmS, Math.Max(1, g0Feed / 60));

    /// <summary>A move's G-code words back to a design position.</summary>
    static Vec2 Position(Vec2 pos, double? x, double? y, double? u, WorkMode mode, double fieldW, bool relative)
    {
        double R(double? w, double now) => w is { } v ? relative ? now + v : v : now;
        switch (mode)
        {
            case WorkMode.Rotary r:
                // X is the galvo across the object; U goes round it. The galvo's Y stays on its line.
                double uNow = (pos.Y - r.OriginY) * r.Direction;
                return new(R(x, pos.X), r.OriginY + R(u, uNow) * r.Direction);
            case WorkMode.Conveyor:
                // U moves the belt (0 in the middle of the field); the galvo's X stays on its line.
                double belt = pos.X - fieldW / 2;
                return new(fieldW / 2 + R(u, belt), R(y, pos.Y));
            default:
                return new(R(x, pos.X), R(y, pos.Y));
        }
    }
}
