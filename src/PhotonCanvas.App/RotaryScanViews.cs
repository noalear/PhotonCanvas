using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.App.View3D;
using PhotonCanvas.Core;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The stock model from a rotary scan in 3D: the surface all the way round (or the part scanned), the axis, and an
/// orange line along what faced the laser when the scan started.
/// </summary>
public sealed class StockView : Model3DView
{
    static readonly Color Stock = Color.FromRgb(0xD8, 0xB4, 0x86);
    static readonly Color Mark = Color.FromRgb(0xF5, 0x9E, 0x0B);
    static readonly Vec3 Light = new Vec3(0.35, 0.85, 0.45).Unit;

    public StockView() : base(28, 22) { }

    /// <summary>
    /// Show <paramref name="scan"/> with the axis <paramref name="axisHeight"/> above the bed; with no axis height (or
    /// nothing read yet) a message says what's missing. <paramref name="sliceX"/> is ringed.
    /// </summary>
    public void Show(RotaryScan? scan, double? axisHeight, int sliceX)
    {
        Scene.Clear();
        Message = scan is null || scan.Read == 0 ? "Scan the part to see its shape here."
            : axisHeight is null ? "Type the rotary's axis height (or set it from a round rod) to see the stock model."
            : scan.Xs.Length < 2 && scan.AnglesDeg.Length < 3 ? "Too few readings for a model."
            : null;
        if (Message is null && scan is not null && axisHeight is { } ax)
            Build(scan, scan.Radii(ax), sliceX);
        InvalidateVisual();
    }

    // Model space: X along the axis, Y up, Z towards the front. At the start the top (angle 0) is +Y.
    static Vec3 At(double x, double r, double deg)
    {
        double t = deg * Math.PI / 180;
        return new Vec3(x, r * Math.Cos(t), r * Math.Sin(t));
    }

    void Build(RotaryScan scan, double?[][] r, int sliceX)
    {
        int na = scan.AnglesDeg.Length, nx = scan.Xs.Length;
        bool wrap = scan.WholeTurn;
        // A part measured at one place only is drawn as a short ring, so it can be seen.
        double[] xs = nx >= 2 ? scan.Xs : [scan.Xs[0] - 2, scan.Xs[0] + 2];
        double R(int a, int x) => r[a % na][nx >= 2 ? x : 0] ?? 0;
        int cols = xs.Length;
        for (int a = 0; a < (wrap ? na : na - 1); a++)
            for (int x = 0; x + 1 < cols; x++)
            {
                double d0 = scan.AnglesDeg[a], d1 = a + 1 < na ? scan.AnglesDeg[a + 1] : scan.AnglesDeg[0] + 360;
                var p00 = At(xs[x], R(a, x), d0);
                var p01 = At(xs[x + 1], R(a, x + 1), d0);
                var p10 = At(xs[x], R(a + 1, x), d1);
                var p11 = At(xs[x + 1], R(a + 1, x + 1), d1);
                Tri(p00, p01, p11);
                Tri(p00, p11, p10);
            }
        // The axis, and what faced the laser at the start.
        double rMax = Math.Max(1, r.SelectMany(row => row).OfType<double>().DefaultIfEmpty(1).Max());
        Scene.AddEdge(new Vec3(xs[0] - rMax * 0.3, 0, 0), new Vec3(xs[^1] + rMax * 0.3, 0, 0), Color.FromRgb(0x60, 0x70, 0x80), LineWidth.Px(1));
        var start = Enumerable.Range(0, cols).Select(x => At(xs[x], R(0, x) + 0.05, scan.AnglesDeg[0])).ToArray();
        for (int i = 0; i + 1 < start.Length; i++) Scene.AddEdge(start[i], start[i + 1], Mark, LineWidth.Px(2.5));
        // The slice chosen below.
        if (nx >= 2 && sliceX >= 0 && sliceX < nx)
        {
            var ring = Enumerable.Range(0, na).Select(a => At(scan.Xs[sliceX], R(a, sliceX) + 0.05, scan.AnglesDeg[a])).ToList();
            if (wrap) ring.Add(ring[0]);
            for (int i = 0; i + 1 < ring.Count; i++) Scene.AddEdge(ring[i], ring[i + 1], Color.FromRgb(0x38, 0x8B, 0xFD), LineWidth.Px(2));
        }
    }

    void Tri(Vec3 a, Vec3 b, Vec3 c)
    {
        var n = (b - a).Cross(c - a);
        if (n.Length < 1e-9) return;
        n = n.Unit;
        // Outwards: away from the axis.
        var mid = (a + b + c) * (1.0 / 3);
        var outward = new Vec3(0, mid.Y, mid.Z);
        if (n.Dot(outward) < 0) { (b, c) = (c, b); n = -n; }
        Scene.AddFace([[a, b, c]], n, Scene3D.Shade(Stock, 0.6 + 0.4 * Math.Max(0, n.Dot(Light))));
    }
}

/// <summary>
/// One place along the part, round the turn: with the axis height known, its outline seen end-on (the axis in the
/// middle, angle 0 at the top); without, its top height against the angle turned.
/// </summary>
public sealed class ScanChart : Control
{
    RotaryScan? _scan;
    double? _axis;
    int _x;

    public void Show(RotaryScan? scan, double? axisHeight, int xi)
    {
        _scan = scan;
        _axis = axisHeight;
        _x = xi;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Brush("Bg2"), b);
        var text = Brush("TextMuted");
        if (_scan is not { } s || _x < 0 || _x >= s.Xs.Length || s.Column(_x).Count == 0)
        {
            Write(ctx, "Nothing read here yet.", new Point(10, 8), text);
            return;
        }
        var line = new Pen(Brush("Accent"), 2);
        var grid = new Pen(Brush("Line"), 1);
        if (_axis is { } ax)
        {
            var r = s.Radii(ax);
            var pts = Enumerable.Range(0, s.AnglesDeg.Length).Where(a => r[a][_x] is not null).Select(a => (s.AnglesDeg[a], r[a][_x]!.Value)).ToList();
            double rMax = Math.Max(0.5, pts.Max(p => p.Item2));
            var c = b.Center;
            double k = (Math.Min(b.Width, b.Height) / 2 - 18) / rMax;
            ctx.DrawEllipse(null, grid, c, rMax * k, rMax * k);
            ctx.DrawLine(grid, new Point(c.X - 6, c.Y), new Point(c.X + 6, c.Y));
            ctx.DrawLine(grid, new Point(c.X, c.Y - 6), new Point(c.X, c.Y + 6));
            // Seen from the left end: angle 0 at the top, turning brings later angles up from the front.
            Point P(double deg, double rr) => new(c.X + rr * k * Math.Sin(deg * Math.PI / 180), c.Y - rr * k * Math.Cos(deg * Math.PI / 180));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(pts[0].Item1, pts[0].Item2), false);
                foreach (var p in pts.Skip(1)) g.LineTo(P(p.Item1, p.Item2));
                g.EndFigure(s.WholeTurn);
            }
            ctx.DrawGeometry(null, line, geo);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 2), c, P(s.AnglesDeg[0], rMax));
            Write(ctx, $"At X {s.Xs[_x]:0.#} mm, seen from the left end. Ring: {rMax:0.#} mm from the axis. Orange: what faced the laser at the start.", new Point(8, 4), text);
        }
        else
        {
            var col = s.Column(_x);
            double lo = col.Min(p => p.H), hi = col.Max(p => p.H);
            if (hi - lo < 0.5) { lo -= 0.25; hi += 0.25; }
            double left = 44, right = b.Width - 10, top = 24, bottom = b.Height - 22;
            double X(double deg) => left + (right - left) * deg / 360;
            double Y(double h) => bottom - (bottom - top) * (h - lo) / (hi - lo);
            ctx.DrawLine(grid, new Point(left, bottom), new Point(right, bottom));
            ctx.DrawLine(grid, new Point(left, top), new Point(left, bottom));
            for (int d = 0; d <= 360; d += 90) Write(ctx, $"{d}°", new Point(X(d) - 8, bottom + 3), text);
            Write(ctx, $"{hi:0.#}", new Point(4, top - 6), text);
            Write(ctx, $"{lo:0.#}", new Point(4, bottom - 8), text);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(col[0].Deg), Y(col[0].H)), false);
                foreach (var p in col.Skip(1)) g.LineTo(new Point(X(p.Deg), Y(p.H)));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, line, geo);
            foreach (var p in col) ctx.DrawEllipse(Brush("Accent"), null, new Point(X(p.Deg), Y(p.H)), 2.5, 2.5);
            Write(ctx, $"Top height (mm above the bed) at X {s.Xs[_x]:0.#} mm, against the angle turned.", new Point(8, 4), text);
        }
    }

    static void Write(DrawingContext ctx, string s, Point at, IBrush brush) =>
        ctx.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush), at);
}
