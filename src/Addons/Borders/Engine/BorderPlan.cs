using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.Borders;

/// <summary>One picture the border is made as: the whole of it, a length of a long strip, or one side of a frame.</summary>
/// <param name="Name">"Frame", "Top side", "Part 2 of 3"…</param>
/// <param name="Side">For a side of a frame made in four pieces: 0 top, 1 right, 2 bottom, 3 left; else -1.</param>
/// <param name="From">For a part of a strip: where along the strip it starts (mm).</param>
/// <param name="WidthMm">The picture's width (mm).</param>
/// <param name="HeightMm">The picture's height (mm).</param>
public sealed record BorderPiece(string Name, int Side, double From, double WidthMm, double HeightMm)
{
    public bool IsSide => Side >= 0;
}

/// <summary>
/// A border worked out from its spec: the bands across it, the repeats fitted to each side (a whole
/// number of them, so the corners and ends come out the same), and the height of every point. All
/// lengths are mm; a point's height is mm above the deepest level (NaN = not part of the border).
/// </summary>
public sealed class BorderPlan
{
    public BorderSpec Spec { get; }
    /// <summary>The design at the size it's made (the deepest level at 0).</summary>
    public BorderDesign Design { get; }
    /// <summary>The border's width across (mm).</summary>
    public double Width { get; }
    /// <summary>The surface: white in the depth map (mm).</summary>
    public double Top { get; }
    /// <summary>The deepest level: black (mm).</summary>
    public double Lowest { get; }
    /// <summary>The size of the whole thing (mm): a strip's length × width, a frame's outside.</summary>
    public double SizeW { get; }
    public double SizeH { get; }

    readonly BorderBand[] _bands;
    readonly double[] _t0;
    readonly Segment[] _segs;
    readonly double _radius;            // rounded corners
    readonly double _middle;            // NaN = left alone
    readonly double _blockCarve;
    readonly double[]? _ovalTable;      // arc length along the oval at each step of its angle
    readonly double _copyW;             // round a rotary part: one copy across (the design, and its mirror image if asked)
    readonly double _period;            // rings: a ring and its gap along the axis; flutes: a copy round the part
    readonly double _hyp;               // spiral: once round the part along the spiral (√(lead² + C²))

    /// <summary>A spiral round a rotary part: how far along the part it goes in one turn (mm); else 0.</summary>
    public double SpiralLeadMm { get; }

    /// <summary>One run of the border: a side, or the whole way round a loop.</summary>
    sealed record Segment(string Name, double Length, double[] Pitch, int[] Repeats, double Phase, double Anchor);

    public BorderPlan(BorderSpec spec)
    {
        Spec = spec;
        Design = spec.Sized();
        _bands = [.. Design.Bands];
        Width = Design.WidthMm;
        Top = Design.TopMm;
        Lowest = Math.Min(Design.LowestMm, Top - 1e-6);
        _t0 = new double[_bands.Length + 1];
        for (int i = 0; i < _bands.Length; i++) _t0[i + 1] = _t0[i] + _bands[i].WidthMm;

        double phase = spec.MotifOnCorners ? 0 : 0.5;
        double B = Width;
        switch (spec.Layout)
        {
            case BorderLayout.Strip:
                SizeW = spec.LengthMm;
                SizeH = B;
                _segs = [Straight("Strip", SizeW, phase)];
                break;
            case BorderLayout.Oval:
                (SizeW, SizeH) = spec.Outside();
                _ovalTable = OvalTable(SizeW / 2, SizeH / 2);
                _segs = [Loop("Oval", _ovalTable[^1], 0)];
                break;
            case BorderLayout.Rotary:
                {
                    // The part unrolled: X along its axis, Y once round. Every run fits a whole number of repeats,
                    // so where the picture's top and bottom meet round the back the pattern carries straight on.
                    double c = spec.PartCircumferenceMm;
                    int k = Math.Max(1, spec.WrapCopies);
                    _copyW = B * (spec.WrapMirrored ? 2 : 1);
                    SizeH = c;
                    switch (spec.Wrap)
                    {
                        case WrapWay.Along:
                            SizeW = spec.WrapLengthMm;
                            _period = c / k;
                            _segs = [Straight("Along", SizeW, phase)];
                            break;
                        case WrapWay.Spiral:
                            {
                                // Unrolled, a spiral is a straight band at a slant. Once round the part (0, C) has to land
                                // on the same pattern: across the band that's all the starts' widths, which sets the slant,
                                // and along it the run's length, which every repeat is fitted to.
                                SizeW = spec.WrapLengthMm;
                                double across = k * _copyW;
                                SpiralLeadMm = across * c / Math.Sqrt(Math.Max(1e-9, c * c - across * across));
                                _hyp = Math.Sqrt(SpiralLeadMm * SpiralLeadMm + c * c);
                                _segs = [Straight("Spiral", c * c / _hyp, phase)];
                                break;
                            }
                        default:
                            _period = _copyW + Math.Max(0, spec.WrapGapMm);
                            SizeW = k * _copyW + (k - 1) * Math.Max(0, spec.WrapGapMm);
                            _segs = [Straight("Round", c, phase)];
                            break;
                    }
                    break;
                }
            default:
                (SizeW, SizeH) = spec.Outside();
                switch (spec.Corners)
                {
                    case CornerStyle.Blocks:
                        _segs = [Straight("Top", SizeW - 2 * B, 0.5), Straight("Right", SizeH - 2 * B, 0.5), Straight("Bottom", SizeW - 2 * B, 0.5), Straight("Left", SizeH - 2 * B, 0.5)];
                        break;
                    case CornerStyle.Rounded:
                        _radius = Math.Clamp(spec.CornerRadiusMm, 0.5, Math.Min(SizeW, SizeH) / 2);
                        double perimeter = 2 * (SizeW - 2 * _radius) + 2 * (SizeH - 2 * _radius) + 2 * Math.PI * _radius;
                        _segs = [Loop("All round", perimeter, (SizeW - 2 * _radius) / 2)];
                        break;
                    default:
                        _segs = [Straight("Top", SizeW, phase), Straight("Right", SizeH, phase), Straight("Bottom", SizeW, phase), Straight("Left", SizeH, phase)];
                        break;
                }
                break;
        }

        _middle = spec.Middle switch
        {
            MiddleFill.Floor => Lowest,
            MiddleFill.InnerEdge when _bands.Length > 0 => ProfileLevel(_bands[^1], 1),
            _ => double.NaN,
        };
        double carve = _bands.Where(b => b.Ornament != Ornament.None).Select(b => b.CarveMm).DefaultIfEmpty(0).Max();
        _blockCarve = carve > 0 ? carve : 0.4 * (Top - Lowest);
    }

    /// <summary>The length of one repeat of a band's ornament before it's fitted (mm).</summary>
    public static double NaturalPitch(BorderBand b) =>
        b.RepeatMm > 0 ? b.RepeatMm : Math.Max(0.5, Ornaments.Info(b.Ornament).Aspect * b.WidthMm);

    Segment Straight(string name, double length, double phase)
    {
        var pitch = new double[_bands.Length];
        var reps = new int[_bands.Length];
        for (int i = 0; i < _bands.Length; i++)
        {
            double want = NaturalPitch(_bands[i]);
            int n = Math.Max(1, (int)Math.Round(length / want));
            // A whole motif in the middle of each side: odd or even counts, whichever lands the motif there.
            if (Spec.CentreEachSide && _bands[i].Ornament != Ornament.None)
            {
                bool wantEven = phase == 0;
                if ((n % 2 == 0) != wantEven)
                {
                    int up = n + 1, down = n - 1;
                    n = down >= 1 && Math.Abs(length / down - want) <= Math.Abs(length / up - want) ? down : up;
                }
            }
            reps[i] = n;
            pitch[i] = Math.Max(1e-6, length) / n;
        }
        return new Segment(name, length, pitch, reps, phase, 0);
    }

    Segment Loop(string name, double perimeter, double anchor)
    {
        var pitch = new double[_bands.Length];
        var reps = new int[_bands.Length];
        for (int i = 0; i < _bands.Length; i++)
        {
            double want = NaturalPitch(_bands[i]);
            int n = Math.Max(1, (int)Math.Round(perimeter / want));
            if (Spec.CentreEachSide && _bands[i].Ornament != Ornament.None)
            {
                // A multiple of four puts a motif in the middle of every side, so it's symmetric both ways.
                int four = Math.Max(4, (int)Math.Round(perimeter / want / 4) * 4);
                n = Math.Abs(perimeter / four - want) / want < 0.25 ? four : Math.Max(2, (int)Math.Round(perimeter / want / 2) * 2);
            }
            reps[i] = n;
            pitch[i] = perimeter / n;
        }
        return new Segment(name, perimeter, pitch, reps, Spec.CentreEachSide ? 0 : 0.5, anchor);
    }

    /// <summary>For each run of the border, how many repeats each ornamented band has (for the summary).</summary>
    public IEnumerable<(string Run, int Band, int Repeats, double PitchMm)> Repeats()
    {
        foreach (var s in _segs)
            for (int i = 0; i < _bands.Length; i++)
                if (_bands[i].Ornament != Ornament.None) yield return (s.Name, i, s.Repeats[i], s.Pitch[i]);
    }

    // ---- heights ----

    /// <summary>A band's profile height at <paramref name="tau"/> across it (0 outer … 1 inner), before carving.</summary>
    static double ProfileLevel(BorderBand b, double tau)
    {
        double t = b.Flip ? 1 - tau : tau;
        return b.TopMm - Math.Max(0, b.DropMm) * (1 - Profiles.Height(b.Profile, t, b.Count));
    }

    /// <summary>The height <paramref name="t"/> mm in from the outer edge, <paramref name="s"/> mm along run <paramref name="seg"/>.</summary>
    double BandHeight(int seg, double s, double t)
    {
        int i = Array.BinarySearch(_t0, t);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, _bands.Length - 1);
        var b = _bands[i];
        double w = Math.Max(1e-9, b.WidthMm);
        double tau = Math.Clamp((t - _t0[i]) / w, 0, 1);
        double h = ProfileLevel(b, tau);
        if (b.Ornament == Ornament.None || b.CarveMm <= 0) return h;
        var g = _segs[seg];
        double u = (s - g.Anchor) / g.Pitch[i] + g.Phase;
        double v = b.Flip ? 1 - tau : tau;
        double m = Ornaments.Height(b.Ornament, u, v, new OrnamentScale(g.Pitch[i], w, b.Count));
        return h - b.CarveMm * (1 - Math.Clamp(m, 0, 1));
    }

    /// <summary>A corner block: a raised square with a rosette carved in it (<paramref name="dx"/>, <paramref name="dy"/> from its middle).</summary>
    double BlockHeight(double dx, double dy)
    {
        double half = Width / 2;
        double rim = Math.Max(Math.Abs(dx), Math.Abs(dy));
        // A small chamfer round the block's edge, a groove inside it, and the rosette.
        double h = Top;
        double groove = Carve.Ridge(rim - 0.8 * half, 0.04 * half);
        h -= 0.35 * _blockCarve * groove;
        double rose = Ornaments.Rosette(dx, dy, 0.66 * half, 8);
        double inner = Math.Sqrt(dx * dx + dy * dy) < 0.7 * half ? _blockCarve * (1 - rose) : 0;
        return h - inner;
    }

    /// <summary>The height at (x, y) mm in the whole border's box, or NaN where there's no border (outside it, or a left-alone middle).</summary>
    public double HeightAt(double x, double y)
    {
        if (!(x >= 0 && y >= 0 && x <= SizeW && y <= SizeH)) return double.NaN;
        double B = Width;
        switch (Spec.Layout)
        {
            case BorderLayout.Strip:
                if (Spec.MitredEnds && (y > x || y > SizeW - x)) return double.NaN;
                return BandHeight(0, x, Math.Clamp(y, 0, B));
            case BorderLayout.Oval:
                return OvalHeight(x, y);
            case BorderLayout.Rotary:
                return WrapHeight(x, y);
        }
        double W = SizeW, H = SizeH;
        switch (Spec.Corners)
        {
            case CornerStyle.Blocks:
                {
                    bool sideX = x < B || x > W - B, sideY = y < B || y > H - B;
                    if (sideX && sideY) return BlockHeight(x < B ? x - B / 2 : x - (W - B / 2), y < B ? y - B / 2 : y - (H - B / 2));
                    if (y < B) return BandHeight(0, x - B, y);
                    if (x > W - B) return BandHeight(1, y - B, W - x);
                    if (y > H - B) return BandHeight(2, W - B - x, H - y);
                    if (x < B) return BandHeight(3, H - B - y, x);
                    return _middle;
                }
            case CornerStyle.Rounded:
                return RoundedHeight(x, y);
            default:
                {
                    double t = Math.Min(Math.Min(x, y), Math.Min(W - x, H - y));
                    if (t > B) return _middle;
                    if (t == y) return BandHeight(0, x, t);
                    if (t == W - x) return BandHeight(1, y, t);
                    if (t == H - y) return BandHeight(2, W - x, t);
                    return BandHeight(3, H - y, t);
                }
        }
    }

    double RoundedHeight(double x, double y)
    {
        double W = SizeW, H = SizeH, R = _radius, B = Width;
        double lw = W - 2 * R, lh = H - 2 * R, q = Math.PI * R / 2;
        double cx = x < R ? R : x > W - R ? W - R : double.NaN;
        double cy = y < R ? R : y > H - R ? H - R : double.NaN;
        if (!double.IsNaN(cx) && !double.IsNaN(cy))
        {
            double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
            double t = R - d;
            if (t < 0) return double.NaN;
            if (t > B) return _middle;
            double s;
            if (dy < 0 && dx > 0) s = lw + R * Math.Atan2(dx, -dy);                          // top right
            else if (dy >= 0 && dx > 0) s = lw + q + lh + R * Math.Atan2(dy, dx);             // bottom right
            else if (dy > 0 && dx <= 0) s = 2 * lw + 2 * q + lh + R * Math.Atan2(-dx, dy);    // bottom left
            else s = 2 * lw + 3 * q + 2 * lh + R * Math.Atan2(-dy, -dx);                    // top left
            return BandHeight(0, s, t);
        }
        double tt = Math.Min(Math.Min(x, y), Math.Min(W - x, H - y));
        if (tt > B) return _middle;
        if (tt == y) return BandHeight(0, x - R, tt);
        if (tt == W - x) return BandHeight(0, lw + q + (y - R), tt);
        if (tt == H - y) return BandHeight(0, lw + 2 * q + lh + (W - R - x), tt);
        return BandHeight(0, 2 * lw + 3 * q + lh + (H - R - y), tt);
    }

    // ---- round a rotary part ----

    static double Mod(double a, double m) => a - Math.Floor(a / m) * m;

    /// <summary>The height at <paramref name="x"/> along the part and <paramref name="y"/> round it (mm on the unrolled part).</summary>
    double WrapHeight(double x, double y)
    {
        double c = SizeH, s, t;
        switch (Spec.Wrap)
        {
            case WrapWay.Along:
                s = x;
                t = Mod(y, _period);
                break;
            case WrapWay.Spiral:
                {
                    double yy = Spec.WrapLeftHand ? -y : y, lead = SpiralLeadMm;
                    s = Mod((x * lead + yy * c) / _hyp, _segs[0].Length);
                    t = Mod((x * c - yy * lead) / _hyp, _copyW);
                    break;
                }
            default:
                {
                    double i = Math.Floor(x / _period);
                    t = x - i * _period;
                    if (t > _copyW + 1e-9) return double.NaN;
                    s = y;
                    break;
                }
        }
        double w = Width;
        if (Spec.WrapMirrored && t > w) t = 2 * w - t;
        if (Spec.WrapTurned) t = w - t;
        return BandHeight(0, s, Math.Clamp(t, 0, w));
    }

    // ---- the oval ----

    const int OvalSteps = 4096;

    /// <summary>Arc length from the top of the oval, clockwise, at each of <see cref="OvalSteps"/> steps of the angle.</summary>
    static double[] OvalTable(double a, double b)
    {
        var t = new double[OvalSteps + 1];
        double px = 0, py = -b;
        for (int i = 1; i <= OvalSteps; i++)
        {
            double phi = 2 * Math.PI * i / OvalSteps;
            double x = a * Math.Sin(phi), y = -b * Math.Cos(phi);
            t[i] = t[i - 1] + Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            px = x; py = y;
        }
        return t;
    }

    double OvalHeight(double x, double y)
    {
        double a = SizeW / 2, b = SizeH / 2;
        double px = x - a, py = y - b;
        if (px * px / (a * a) + py * py / (b * b) > 1) return double.NaN;
        var (ex, ey) = NearestOnEllipse(px, py, a, b);
        double t = Math.Sqrt((px - ex) * (px - ex) + (py - ey) * (py - ey));
        if (t > Width) return _middle;
        double phi = Math.Atan2(ex / a, -ey / b);
        if (phi < 0) phi += 2 * Math.PI;
        double k = phi / (2 * Math.PI) * OvalSteps;
        int i = Math.Clamp((int)k, 0, OvalSteps - 1);
        double s = Carve.Lerp(_ovalTable![i], _ovalTable[i + 1], k - i);
        return BandHeight(0, s, t);
    }

    /// <summary>The nearest point of the ellipse (semi-axes a, b, centred) to (px, py): an iteration that settles in a few steps.</summary>
    static (double X, double Y) NearestOnEllipse(double px, double py, double a, double b)
    {
        double ax = Math.Abs(px), ay = Math.Abs(py);
        if (Math.Abs(a - b) < 1e-9)
        {
            double r = Math.Sqrt(ax * ax + ay * ay);
            if (r < 1e-12) return (0, -b);
            return (px / r * a, py / r * a);
        }
        double tx = 0.70710678, ty = 0.70710678;
        for (int i = 0; i < 4; i++)
        {
            double x = a * tx, y = b * ty;
            double ex = (a * a - b * b) * tx * tx * tx / a, ey = (b * b - a * a) * ty * ty * ty / b;
            double rx = x - ex, ry = y - ey, qx = ax - ex, qy = ay - ey;
            double r = Math.Sqrt(rx * rx + ry * ry), q = Math.Max(1e-12, Math.Sqrt(qx * qx + qy * qy));
            tx = Math.Clamp((qx * r / q + ex) / a, 0, 1);
            ty = Math.Clamp((qy * r / q + ey) / b, 0, 1);
            double t = Math.Max(1e-12, Math.Sqrt(tx * tx + ty * ty));
            tx /= t; ty /= t;
        }
        return (Math.CopySign(a * tx, px), Math.CopySign(b * ty, py));
    }

    // ---- pieces ----

    /// <summary>The pictures the border is made as.</summary>
    public IReadOnlyList<BorderPiece> Pieces()
    {
        if (Spec.Layout == BorderLayout.Frame && Spec.Corners == CornerStyle.Mitred && Spec.FourPieces)
            return [new("Top side", 0, 0, SizeW, Width), new("Right side", 1, 0, SizeH, Width), new("Bottom side", 2, 0, SizeW, Width), new("Left side", 3, 0, SizeH, Width)];
        if (Spec.Layout == BorderLayout.Rotary)
            return [new("Wrap", -1, 0, SizeW, SizeH)];
        if (Spec.Layout == BorderLayout.Strip && Spec.MaxPieceMm > 0 && SizeW > Spec.MaxPieceMm + 1e-6)
        {
            int n = (int)Math.Ceiling(SizeW / Spec.MaxPieceMm - 1e-9);
            double each = SizeW / n;
            return [.. Enumerable.Range(0, n).Select(i => new BorderPiece($"Part {i + 1} of {n}", -1, i * each, each, Width))];
        }
        string name = Spec.Layout switch { BorderLayout.Strip => "Strip", BorderLayout.Oval => "Oval frame", _ => "Frame" };
        return [new(name, -1, 0, SizeW, SizeH)];
    }

    /// <summary>The height at (x, y) mm in piece <paramref name="p"/>'s own picture, or NaN where it isn't carved.</summary>
    public double HeightAt(BorderPiece p, double x, double y)
    {
        if (p.IsSide)
        {
            // One stick of a mitred frame: its outer edge along the top, 45° ends.
            if (!(y >= 0 && y <= Width && x >= 0 && x <= p.WidthMm) || y > x || y > p.WidthMm - x) return double.NaN;
            return BandHeight(p.Side, x, y);
        }
        return HeightAt(x + p.From, y);
    }

    /// <summary>
    /// The heights of a window onto piece <paramref name="p"/>: <paramref name="cols"/> × <paramref name="rows"/>
    /// pixels of <paramref name="mmPerPx"/>, the first one's corner at (<paramref name="x0"/>, <paramref name="y0"/>).
    /// </summary>
    public float[] Render(BorderPiece p, double x0, double y0, double mmPerPx, int cols, int rows, CancellationToken ct = default) =>
        Render(p, x0, y0, mmPerPx, mmPerPx, cols, rows, ct);

    /// <summary>The same with pixels <paramref name="mmPerPxX"/> wide and <paramref name="mmPerPxY"/> high (a picture that must fill its size exactly).</summary>
    public float[] Render(BorderPiece p, double x0, double y0, double mmPerPxX, double mmPerPxY, int cols, int rows, CancellationToken ct = default)
    {
        var h = new float[cols * rows];
        var opts = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, rows, opts, r =>
        {
            double y = y0 + (r + 0.5) * mmPerPxY;
            for (int c = 0; c < cols; c++)
                h[r * cols + c] = (float)HeightAt(p, x0 + (c + 0.5) * mmPerPxX, y);
        });
        return h;
    }

    /// <summary>The cut lines round piece <paramref name="p"/>, in its own mm: the outside, and a frame's opening.</summary>
    public List<List<Vec2>> CutLines(BorderPiece p)
    {
        double B = Width;
        if (Spec.Layout == BorderLayout.Rotary) return [];
        if (p.IsSide)
            return [[new(0, 0), new(p.WidthMm, 0), new(p.WidthMm - B, B), new(B, B)]];
        if (Spec.Layout == BorderLayout.Strip)
        {
            bool first = p.From <= 1e-9, last = p.From + p.WidthMm >= SizeW - 1e-6;
            double l = p.WidthMm;
            var o = new List<Vec2> { new(0, 0), new(l, 0) };
            if (Spec.MitredEnds && last) o.Add(new(l - B, B)); else o.Add(new(l, B));
            if (Spec.MitredEnds && first) o.Add(new(B, B)); else o.Add(new(0, B));
            return [o];
        }
        double W = SizeW, H = SizeH;
        if (Spec.Layout == BorderLayout.Oval)
        {
            double a = W / 2, b = H / 2;
            var outer = new List<Vec2>();
            var inner = new List<Vec2>();
            for (int i = 0; i < 256; i++)
            {
                double phi = 2 * Math.PI * i / 256;
                double x = a * Math.Sin(phi), y = -b * Math.Cos(phi);
                outer.Add(new(a + x, b + y));
                // The opening is the outline moved in along its normal (not an ellipse).
                double nx = x / (a * a), ny = y / (b * b), nl = Math.Sqrt(nx * nx + ny * ny);
                inner.Add(new(a + x - B * nx / nl, b + y - B * ny / nl));
            }
            return Spec.Middle == MiddleFill.Untouched ? [outer, inner] : [outer];
        }
        var lines = new List<List<Vec2>>();
        if (Spec.Corners == CornerStyle.Rounded)
        {
            lines.Add(RoundRect(0, 0, W, H, _radius));
            if (Spec.Middle == MiddleFill.Untouched) lines.Add(RoundRect(B, B, W - B, H - B, Math.Max(0, _radius - B)));
        }
        else
        {
            lines.Add([new(0, 0), new(W, 0), new(W, H), new(0, H)]);
            if (Spec.Middle == MiddleFill.Untouched) lines.Add([new(B, B), new(W - B, B), new(W - B, H - B), new(B, H - B)]);
        }
        return lines;
    }

    static List<Vec2> RoundRect(double x0, double y0, double x1, double y1, double r)
    {
        var o = new List<Vec2>();
        if (r <= 1e-6) return [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
        void Arc(double cx, double cy, double a0)
        {
            for (int i = 0; i <= 16; i++)
            {
                double a = a0 + Math.PI / 2 * i / 16;
                o.Add(new(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
        }
        Arc(x1 - r, y0 + r, -Math.PI / 2);
        Arc(x1 - r, y1 - r, 0);
        Arc(x0 + r, y1 - r, Math.PI / 2);
        Arc(x0 + r, y0 + r, Math.PI);
        return o;
    }

    // ---- pictures ----

    /// <summary>Blurs heights by <paramref name="sigmaPx"/> pixels, leaving the see-through (NaN) ones alone.</summary>
    public static void Soften(float[] h, int cols, int rows, double sigmaPx, bool wrapRows = false)
    {
        if (!(sigmaPx >= 0.3)) return;
        int rad = Math.Max(1, (int)Math.Ceiling(sigmaPx * 2.5));
        var k = new double[2 * rad + 1];
        for (int i = -rad; i <= rad; i++) k[i + rad] = Math.Exp(-i * i / (2 * sigmaPx * sigmaPx));
        var tmp = new float[h.Length];
        void Pass(float[] src, float[] dst, bool horizontal)
        {
            int lines = horizontal ? rows : cols, len = horizontal ? cols : rows;
            Parallel.For(0, lines, l =>
            {
                for (int i = 0; i < len; i++)
                {
                    int idx = horizontal ? l * cols + i : i * cols + l;
                    float centre = src[idx];
                    if (float.IsNaN(centre)) { dst[idx] = float.NaN; continue; }
                    double sum = 0, wsum = 0;
                    for (int j = -rad; j <= rad; j++)
                    {
                        int q = i + j;
                        // Round a rotary part the top row carries on from the bottom one.
                        if (wrapRows && !horizontal) q = ((q % len) + len) % len;
                        if (q < 0 || q >= len) continue;
                        float val = src[horizontal ? l * cols + q : q * cols + l];
                        if (float.IsNaN(val)) continue;
                        sum += k[j + rad] * val;
                        wsum += k[j + rad];
                    }
                    dst[idx] = (float)(sum / wsum);
                }
            });
        }
        Pass(h, tmp, true);
        Pass(tmp, h, false);
    }

    /// <summary>The heights as a depth map: white = the surface, black = the deepest, see-through where NaN.</summary>
    public static RasterImage DepthMap(float[] h, int cols, int rows, double top, double lowest)
    {
        var px = new byte[cols * rows * 4];
        double range = Math.Max(1e-9, top - lowest);
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c, o = i * 4;
                float v = h[i];
                if (float.IsNaN(v)) { px[o] = px[o + 1] = px[o + 2] = 255; px[o + 3] = 0; continue; }
                byte g = (byte)Math.Clamp((int)Math.Round((v - lowest) / range * 255), 0, 255);
                px[o] = px[o + 1] = px[o + 2] = g;
                px[o + 3] = 255;
            }
        });
        return new RasterImage(cols, rows, px);
    }
}
