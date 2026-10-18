using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>
/// Depth against passes, drawn like a cut through the material: the surface at the top, depth
/// going down, the bottom of the material as a line. Typed depths are dots with the curve fitted
/// through them; the burn-through gives a straight line from the surface to where it went through
/// (the average rate), so readings above or below it show it speeding up or slowing down.
/// </summary>
public sealed class EtchChart : Control
{
    double _thickness;
    int _maxPasses = 20;
    int? _through, _notThrough;
    IReadOnlyList<(int Passes, double Depth)> _readings = [];
    EtchCurve? _curve;

    public EtchChart() => Height = 220;

    public void Show(double thicknessMm, int maxPasses, int? through, int? notThrough, IReadOnlyList<(int Passes, double Depth)> readings, EtchCurve? curve)
    {
        _thickness = thicknessMm;
        _maxPasses = Math.Max(1, maxPasses);
        _through = through;
        _notThrough = notThrough;
        _readings = readings;
        _curve = curve;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(b.Size), 6);
        const double left = 46, right = 14, top = 26, bottom = 40;
        double w = Math.Max(10, b.Width - left - right), h = Math.Max(10, b.Height - top - bottom);
        double maxDepth = Math.Max(_thickness * 1.08, _readings.Select(r => r.Depth).DefaultIfEmpty(0).Max() * 1.1);
        if (!(maxDepth > 0)) maxDepth = 1;
        Point P(double passes, double depth) => new(left + passes / _maxPasses * w, top + depth / maxDepth * h);
        var muted = UiKit.Brush("TextMuted");
        var grid = new Pen(UiKit.Brush("Line"), 1);
        var face = new Typeface(FontFamily.Default);
        void Text(string s, Point at, bool right = false, bool centre = false)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, muted);
            ctx.DrawText(ft, new Point(at.X - (right ? ft.Width : centre ? ft.Width / 2 : 0), at.Y - ft.Height / 2));
        }

        // Axes and grid: passes across, depth down.
        double depthStep = NiceStep(maxDepth / 5), passStep = Math.Max(1, NiceStep(_maxPasses / 8.0));
        for (double d = 0; d <= maxDepth + 1e-9; d += depthStep)
        {
            ctx.DrawLine(grid, P(0, d), P(_maxPasses, d));
            Text($"{d:0.##}", new Point(left - 6, P(0, d).Y), right: true);
        }
        for (double n = 0; n <= _maxPasses + 1e-9; n += passStep)
        {
            ctx.DrawLine(grid, P(n, 0), P(n, maxDepth));
            Text($"{n:0}", new Point(P(n, 0).X, top + h + 10), centre: true);
        }
        Text("passes →", new Point(left + w, top + h + 28), right: true);
        Text("mm deep ↓", new Point(6, 10));

        // The material: its bottom.
        if (_thickness > 0)
        {
            var bottomPen = new Pen(new SolidColorBrush(Color.Parse("#C9A27A")), 2);
            ctx.DrawLine(bottomPen, P(0, _thickness), P(_maxPasses, _thickness));
            Text($"bottom of the material ({_thickness:0.##} mm)", new Point(left + 6, P(0, _thickness).Y - 9));
        }

        // The burn-through average: a straight line to where it went through.
        if (_through is { } th && _thickness > 0)
        {
            double at = _notThrough is { } nt ? (th + nt) / 2.0 : th;
            var avg = new Pen(UiKit.Brush("Accent"), 1.5, new DashStyle([5, 4], 0));
            ctx.DrawLine(avg, P(0, 0), P(at, _thickness));
            // Where it went through: the span between the last that didn't and the first that did.
            double from = _notThrough ?? th;
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 76, 123, 217)), null, new Rect(P(from, _thickness - maxDepth * 0.03), P(th, _thickness + maxDepth * 0.03)));
            Text($"through at {th}", new Point(P(th, _thickness).X + 4, P(th, _thickness).Y + 10));
        }

        // The fitted curve and the readings.
        if (_curve is { } c)
        {
            var curvePen = new Pen(UiKit.Brush("Ok"), 2);
            int last = Math.Min(_maxPasses, _readings.Select(r => r.Passes).DefaultIfEmpty(_maxPasses).Max());
            Point? prev = null;
            for (double n = 0; n <= last + 1e-9; n += Math.Max(0.1, last / 60.0))
            {
                var p = P(n, Math.Max(0, Depth(c, n)));
                if (prev is { } q) ctx.DrawLine(curvePen, q, p);
                prev = p;
            }
        }
        foreach (var (n, d) in _readings)
            ctx.DrawEllipse(Brushes.White, new Pen(UiKit.Brush("Ok"), 2), P(n, d), 4, 4);
        if (_readings.Count == 0 && _through is null)
            Text("Pick the first circle that went through, or type some depths, to see the chart.", new Point(left + w / 2, top + h / 2), centre: true);
    }

    /// <summary>The fitted depth after a fractional number of passes (the curve is smooth in n).</summary>
    static double Depth(EtchCurve c, double n)
    {
        // DepthAfter is a·n − b·n²: recover a and b from two whole-pass values.
        double d1 = c.DepthAfter(1), d2 = c.DepthAfter(2);
        double bq = (2 * d1 - d2) / 2, a = d1 + bq;
        return a * n - bq * n * n;
    }

    static double NiceStep(double raw)
    {
        if (!(raw > 0)) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw))), m = raw / p;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p;
    }
}
