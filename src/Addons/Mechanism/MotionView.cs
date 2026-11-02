using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Clipper2Lib;
using PhotonCanvas.App;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// The motion check on screen: the mechanism moving, each view in its own box, its charts underneath and a
/// strip along the bottom showing how the check came out all the way round the cycle (red where something
/// jams). Drag the part that turns it round to turn it by hand, or click the strip to go to that point.
/// </summary>
public sealed class MotionView : Control
{
    Mechanism? _mech;
    double _input;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    DateTime _last = DateTime.UtcNow;
    readonly Dictionary<Body, Geometry> _shapes = new(ReferenceEqualityComparer.Instance);
    readonly List<(MotionPanel Panel, Rect Box, Rect Inner, double K)> _boxes = [];
    Rect _strip;
    // Dragging: a part round its axle, or along the strip.
    (PlanarPanel Panel, Rect Inner, double K, double Angle)? _turning;
    bool _scrubbing;
    double _dragX;

    const double StripH = 14, ChartH = 132, Pad = 8, TitleH = 18;

    public MotionView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => { _last = DateTime.UtcNow; if (Playing) _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    /// <summary>Where the input is (radians it has turned).</summary>
    public double Input
    {
        get => _input;
        set
        {
            _input = value;
            InvalidateVisual();
            Moved?.Invoke();
        }
    }

    bool _playing = true;
    /// <summary>Turning by itself.</summary>
    public bool Playing
    {
        get => _playing;
        set
        {
            if (_playing == value) return;
            _playing = value;
            _last = DateTime.UtcNow;
            if (value && VisualRoot is not null) _timer.Start(); else _timer.Stop();
            PlayingChanged?.Invoke();
        }
    }

    /// <summary>How fast it turns by itself (turns of the input a minute).</summary>
    public double TurnsPerMinute { get; set; } = 8;

    public event Action? Moved;
    public event Action? PlayingChanged;

    public Mechanism? Mechanism => _mech;

    /// <summary>Show another mechanism (or none), keeping where the input is.</summary>
    public void Show(Mechanism? m)
    {
        _mech = m;
        _shapes.Clear();
        _turning = null;
        InvalidateVisual();
        Moved?.Invoke();
    }

    /// <summary>How far round the cycle the input is, 0 to 1.</summary>
    public double Fraction => _mech is { } m && m.Cycle > 0 ? ((_input / m.Cycle) % 1 + 1) % 1 : 0;

    void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Min(0.2, (now - _last).TotalSeconds);
        _last = now;
        if (!Playing || _mech is null) return;
        Input = _input + 2 * Math.PI * TurnsPerMinute / 60 * dt;
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 600 : available.Width, double.IsInfinity(available.Height) ? 480 : available.Height);

    // ---- layout ----

    void Layout(Size size)
    {
        _boxes.Clear();
        if (_mech is not { } m) return;
        var planar = m.Panels.OfType<PlanarPanel>().ToList();
        var charts = m.Panels.OfType<ChartPanel>().ToList();
        double w = size.Width, h = size.Height;
        _strip = new Rect(Pad + 34, h - StripH - 6, Math.Max(10, w - 2 * Pad - 34), StripH);
        double chartH = charts.Count == 0 ? 0 : Math.Min(ChartH, h * 0.3);
        var top = new Rect(0, 0, w, Math.Max(40, _strip.Y - 10 - chartH));
        if (planar.Count > 0)
        {
            if (planar.Count > 1 && planar[0].Weight > 1)
            {
                double share = planar[0].Weight / (planar[0].Weight + 1);
                Place(planar[0], new Rect(top.X, top.Y, top.Width * share, top.Height));
                Grid(planar.Skip(1).ToList(), new Rect(top.X + top.Width * share, top.Y, top.Width * (1 - share), top.Height));
            }
            else Grid(planar, top);
        }
        if (charts.Count > 0)
        {
            double total = charts.Sum(c => c.Weight), x = 0;
            foreach (var c in charts)
            {
                double cw = w * c.Weight / total;
                _boxes.Add((c, new Rect(x, top.Bottom, cw, chartH).Deflate(4), default, 1));
                x += cw;
            }
        }

        void Grid(List<PlanarPanel> panels, Rect area)
        {
            if (panels.Count == 0) return;
            // As many columns as make the smallest picture biggest.
            int best = 1;
            double bestK = -1;
            for (int cols = 1; cols <= panels.Count; cols++)
            {
                int rows = (panels.Count + cols - 1) / cols;
                double cw = area.Width / cols, ch = area.Height / rows;
                double k = panels.Min(p => Math.Min((cw - 2 * Pad) / Math.Max(1, p.Extent.Width), (ch - 2 * Pad - TitleH) / Math.Max(1, p.Extent.Height)));
                if (k > bestK + 1e-9) { bestK = k; best = cols; }
            }
            int r = (panels.Count + best - 1) / best;
            for (int i = 0; i < panels.Count; i++)
            {
                double cw = area.Width / best, ch = area.Height / r;
                Place(panels[i], new Rect(area.X + i % best * cw, area.Y + i / best * ch, cw, ch));
            }
        }

        void Place(PlanarPanel p, Rect box)
        {
            box = box.Deflate(4);
            var room = new Rect(box.X + Pad, box.Y + Pad + TitleH, Math.Max(1, box.Width - 2 * Pad), Math.Max(1, box.Height - 2 * Pad - TitleH));
            double k = Math.Min(room.Width / Math.Max(1, p.Extent.Width), room.Height / Math.Max(1, p.Extent.Height));
            double iw = p.Extent.Width * k, ih = p.Extent.Height * k;
            var inner = new Rect(room.X + (room.Width - iw) / 2, room.Y + (room.Height - ih) / 2, iw, ih);
            _boxes.Add((p, box, inner, k));
        }
    }

    /// <summary>From a panel's millimetres (y up) to the screen.</summary>
    static Matrix View(PlanarPanel p, Rect inner, double k) => new(k, 0, 0, -k, inner.X - p.Extent.MinX * k, inner.Y + p.Extent.MaxY * k);

    static Matrix PoseMatrix(Pose pose)
    {
        double c = Math.Cos(pose.Angle), s = Math.Sin(pose.Angle);
        return new Matrix(c, s, -s, c, pose.At.X, pose.At.Y);
    }

    static Geometry ToGeometry(Paths64 paths)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
            foreach (var path in paths)
            {
                if (path.Count < 2) continue;
                var p0 = Poly.V(path[0]);
                c.BeginFigure(new Point(p0.X, p0.Y), true);
                for (int i = 1; i < path.Count; i++)
                {
                    var p = Poly.V(path[i]);
                    c.LineTo(new Point(p.X, p.Y));
                }
                c.EndFigure(true);
            }
        }
        return g;
    }

    static Color Rgb(uint c, byte a = 255) => Color.FromArgb(a, (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    // ---- drawing ----

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(size));
        if (_mech is not { } m)
        {
            Write(ctx, "Nothing to show yet.", new Point(size.Width / 2, size.Height / 2), UiKit.Brush("TextMuted"), 13, centre: true);
            return;
        }
        Layout(size);
        var clash = m.Clashing?.Invoke(_input) ?? [];
        var line = UiKit.Brush("Line");
        foreach (var (panel, box, inner, k) in _boxes)
        {
            ctx.DrawRectangle(null, new Pen(line, 1), box, 6, 6);
            Write(ctx, panel.Title, new Point(box.X + 8, box.Y + 6), UiKit.Brush("TextMuted"), 11.5, maxWidth: box.Width - 16);
            if (panel is PlanarPanel p) DrawPlanar(ctx, p, inner, k, clash);
            else if (panel is ChartPanel c) DrawChart(ctx, c, box);
        }
        DrawStrip(ctx, m);
    }

    void DrawPlanar(DrawingContext ctx, PlanarPanel p, Rect inner, double k, IReadOnlyCollection<string> clash)
    {
        var view = View(p, inner, k);
        var ink = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        using var clip = ctx.PushClip(inner.Inflate(Pad));
        foreach (var b in p.Bodies.OrderBy(b => b.Faint ? 0 : 1))
        {
            bool bad = clash.Contains(b.Name);
            uint colour = bad ? Tints.Problem : b.Colour;
            var fill = new SolidColorBrush(Rgb(colour, (byte)(b.Faint ? 60 : 200)));
            var edge = new Pen(new SolidColorBrush(Rgb(Darker(colour), (byte)(b.Faint ? 140 : 255))), 1 / k);
            if (b.ShapeAt is { } at)
            {
                using (ctx.PushTransform(view))
                    ctx.DrawGeometry(fill, edge, ToGeometry(at(_input)));
                continue;
            }
            if (!_shapes.TryGetValue(b, out var geo)) _shapes[b] = geo = ToGeometry(b.Shape);
            var pose = b.Pose(_input);
            using (ctx.PushTransform(PoseMatrix(pose) * view))
            {
                ctx.DrawGeometry(fill, edge, geo);
                if (b.Lines.Count > 0)
                {
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, ink.R, ink.G, ink.B)), 1.3 / k, lineCap: PenLineCap.Round);
                    foreach (var l in b.Lines)
                        for (int i = 1; i < l.Count; i++) ctx.DrawLine(pen, new Point(l[i - 1].X, l[i - 1].Y), new Point(l[i].X, l[i].Y));
                }
            }
        }
        if (p.Overlay is { } overlay)
            using (ctx.PushTransform(view))
                foreach (var (l, c) in overlay(_input))
                {
                    var pen = new Pen(new SolidColorBrush(Rgb(c)), 2 / k, dashStyle: new DashStyle([3, 2], 0));
                    for (int i = 1; i < l.Count; i++) ctx.DrawLine(pen, new Point(l[i - 1].X, l[i - 1].Y), new Point(l[i].X, l[i].Y));
                }
    }

    static uint Darker(uint c) => (uint)(((c >> 16 & 0xFF) * 6 / 10) << 16 | ((c >> 8 & 0xFF) * 6 / 10) << 8 | (c & 0xFF) * 6 / 10);

    void DrawChart(DrawingContext ctx, ChartPanel c, Rect box)
    {
        var muted = UiKit.Brush("TextMuted");
        var plot = new Rect(box.X + 40, box.Y + TitleH + 6, Math.Max(10, box.Width - 52), Math.Max(10, box.Height - TitleH - 22));
        double low = c.Series.SelectMany(s => s.Values).DefaultIfEmpty(0).Min(), high = c.Series.SelectMany(s => s.Values).DefaultIfEmpty(1).Max();
        double lo = Math.Min(0, low);
        double hi = Math.Max(c.Limit ?? 0, high);
        if (hi - lo < 1e-6) hi = lo + 1;
        hi += (hi - lo) * 0.08;
        double X(double f) => plot.X + f * plot.Width;
        double Y(double v) => plot.Bottom - (v - lo) / (hi - lo) * plot.Height;
        var axis = new Pen(UiKit.Brush("Line"), 1);
        ctx.DrawLine(axis, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
        ctx.DrawLine(axis, new Point(plot.X, plot.Y), new Point(plot.X, plot.Bottom));
        // The highest value and the bottom, marked on the side.
        static string Num(double v) => Math.Abs(v) < 0.05 ? "0" : $"{v:0.#}";
        Write(ctx, $"{Num(high)} {c.Unit}", new Point(plot.X - 4, Y(high) - 6), muted, 10, right: true);
        ctx.DrawLine(axis, new Point(plot.X - 3, Y(high)), new Point(plot.X, Y(high)));
        Write(ctx, Num(lo), new Point(plot.X - 4, plot.Bottom - 12), muted, 10, right: true);
        Write(ctx, "start", new Point(plot.X, plot.Bottom + 2), muted, 10);
        Write(ctx, "one cycle", new Point(plot.Right, plot.Bottom + 2), muted, 10, right: true);
        if (c.Limit is { } limit)
            ctx.DrawLine(new Pen(UiKit.Brush("Warn"), 1, dashStyle: new DashStyle([4, 3], 0)), new Point(plot.X, Y(limit)), new Point(plot.Right, Y(limit)));
        double f = Fraction, legendX = box.Right - 8;
        foreach (var s in c.Series.AsEnumerable().Reverse())
        {
            var brush = new SolidColorBrush(Rgb(s.Colour));
            var pen = new Pen(brush, 2, dashStyle: s.Dashed ? new DashStyle([4, 3], 0) : null, lineJoin: PenLineJoin.Round);
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                for (int i = 0; i <= s.Values.Length; i++)
                {
                    var pt = new Point(X((double)i / s.Values.Length), Y(s.Values[i % s.Values.Length]));
                    if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
                }
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, g);
            ctx.DrawEllipse(brush, null, new Point(X(f), Y(s.At(f))), 3.5, 3.5);
            var ft = Text(s.Name, brush, 10.5);
            legendX -= ft.Width;
            ctx.DrawText(ft, new Point(legendX, box.Y + 6));
            legendX -= 12;
        }
        ctx.DrawLine(new Pen(UiKit.Brush("Accent"), 1), new Point(X(f), plot.Y), new Point(X(f), plot.Bottom));
    }

    void DrawStrip(DrawingContext ctx, Mechanism m)
    {
        var r = _strip;
        var ok = UiKit.Brush("Ok");
        var colours = new Dictionary<Severity, IBrush>
        {
            [Severity.Good] = ok,
            [Severity.Note] = ok,
            [Severity.Warn] = UiKit.Brush("Warn"),
            [Severity.Bad] = UiKit.Brush("Danger"),
        };
        int n = m.Strip.Length;
        for (int i = 0; i < n;)
        {
            int j = i;
            while (j < n && m.Strip[j] == m.Strip[i]) j++;
            var brush = colours[m.Strip[i]];
            ctx.DrawRectangle(brush, null, new Rect(r.X + r.Width * i / n, r.Y, r.Width * (j - i) / n + 0.5, r.Height), 0, 0);
            i = j;
        }
        ctx.DrawRectangle(null, new Pen(UiKit.Brush("Line"), 1), r, 3, 3);
        double x = r.X + r.Width * Fraction;
        var ink = UiKit.Brush("TextMain");
        ctx.DrawLine(new Pen(ink, 2), new Point(x, r.Y - 4), new Point(x, r.Bottom + 4));
        double deg = Fraction * m.Cycle * 180 / Math.PI;
        Write(ctx, $"{deg % 360:0}°", new Point(r.X - 6, r.Y), UiKit.Brush("TextMuted"), 10.5, right: true);
    }

    static FormattedText Text(string s, IBrush brush, double size) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush);

    static void Write(DrawingContext ctx, string s, Point at, IBrush brush, double size, bool centre = false, bool right = false, double maxWidth = 0)
    {
        var f = Text(s, brush, size);
        if (maxWidth > 0 && f.Width > maxWidth)
        {
            f.MaxTextWidth = maxWidth;
            f.MaxLineCount = 1;
            f.Trimming = TextTrimming.CharacterEllipsis;
        }
        double x = centre ? at.X - f.Width / 2 : right ? at.X - f.Width : at.X;
        ctx.DrawText(f, new Point(x, centre ? at.Y - f.Height / 2 : at.Y));
    }

    // ---- turning it by hand ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_mech is null) return;
        var pt = e.GetPosition(this);
        if (_strip.Inflate(new Thickness(0, 6)).Contains(pt))
        {
            _scrubbing = true;
            Playing = false;
            Scrub(pt);
            e.Pointer.Capture(this);
            return;
        }
        foreach (var (panel, box, inner, k) in _boxes)
            if (panel is PlanarPanel p && box.Contains(pt))
            {
                Playing = false;
                _turning = (p, inner, k, PointerAngle(p, inner, k, pt));
                _dragX = pt.X;
                e.Pointer.Capture(this);
                return;
            }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        if (_scrubbing) { Scrub(pt); return; }
        if (_turning is not { } t || _mech is null) return;
        if (t.Panel.InputPivot is null)
        {
            // No axle in this view to turn round: slide sideways instead.
            Input = _input + (pt.X - _dragX) * 0.012;
            _dragX = pt.X;
            return;
        }
        double now = PointerAngle(t.Panel, t.Inner, t.K, pt);
        double d = now - t.Angle;
        d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI));
        _turning = t with { Angle = now };
        // Turn the input as far as makes the part being dragged follow the pointer.
        var input = t.Panel.Bodies.FirstOrDefault(b => b.IsInput);
        double rate = t.Panel.InputSense;
        if (input is not null)
        {
            double r = (input.Pose(_input + 1e-4).Angle - input.Pose(_input).Angle) / 1e-4;
            if (Math.Abs(r) > 1e-3) rate = r;
        }
        Input = _input + Math.Clamp(d / rate, -0.5, 0.5);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _turning = null;
        _scrubbing = false;
        e.Pointer.Capture(null);
    }

    void Scrub(Point pt)
    {
        if (_mech is not { } m) return;
        double f = Math.Clamp((pt.X - _strip.X) / Math.Max(1, _strip.Width), 0, 0.9999);
        double cycles = Math.Floor(_input / m.Cycle);
        Input = (cycles + f) * m.Cycle;
    }

    static double PointerAngle(PlanarPanel p, Rect inner, double k, Point pt)
    {
        var pivot = p.InputPivot ?? p.Extent.Center;
        double x = p.Extent.MinX + (pt.X - inner.X) / k, y = p.Extent.MaxY - (pt.Y - inner.Y) / k;
        return Math.Atan2(y - pivot.Y, x - pivot.X);
    }
}
