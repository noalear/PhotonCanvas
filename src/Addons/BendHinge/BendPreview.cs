using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.BendHinge;

/// <summary>
/// The curve from above, coloured by what each part of the panel gets (grey solid, blue slits,
/// red too tight), and under it the flat panel with its slits.
/// </summary>
public sealed class BendPreview : Control
{
    BendHingeResult? _r;
    string? _message;
    IReadOnlyList<double> _splits = [];

    static readonly IBrush TooTight = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    public void Show(BendHingeResult? result, string? message, IReadOnlyList<double>? splits = null)
    {
        (_r, _message, _splits) = (result, message, splits ?? []);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var text = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        var ink = new SolidColorBrush(text);
        var muted = new SolidColorBrush(Color.FromArgb(120, text.R, text.G, text.B));
        var accent = UiKit.Brush("Accent");
        double W = Bounds.Width, H = Bounds.Height;
        void Write(string s, double x, double y, IBrush brush, double size = 11)
        {
            var f = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush) { MaxTextWidth = Math.Max(10, W - 2 * x) };
            ctx.DrawText(f, new Point(x, y));
        }
        if (_r is not { } r)
        {
            if (_message is not null) Write(_message, 8, 8, muted, 12);
            return;
        }
        IBrush BrushAt(double s)
        {
            foreach (var b in r.Bands)
                if (s >= b.From && s <= b.To)
                    return b.Kind switch { BandKind.Solid => muted, BandKind.Hinge => accent, _ => TooTight };
            return muted;
        }

        // The curve, from above, in the top part.
        double topH = H * 0.55;
        var pts = r.Profile.Points;
        var box = Rect2.Bounds(pts);
        double pad = 14;
        double k = Math.Min((W - 2 * pad) / Math.Max(1e-6, box.Width), (topH - 2 * pad - 14) / Math.Max(1e-6, box.Height));
        double ox = (W - box.Width * k) / 2 - box.MinX * k, oy = pad + 14 + (topH - 2 * pad - 14 - box.Height * k) / 2 - box.MinY * k;
        Point P(Vec2 v) => new(ox + v.X * k, oy + v.Y * k);
        Write("From above", 0, 0, muted);
        int n = pts.Count, last = r.Profile.Closed ? n : n - 1;
        // Draw in short runs of the same colour.
        int stride = Math.Max(1, n / 1500);
        for (int i = 0; i < last; i += stride)
        {
            int j = Math.Min(i + stride, r.Profile.Closed ? n : n - 1);
            var a = pts[i % n];
            var b = pts[j % n];
            var brush = BrushAt((i + stride / 2.0) * r.Profile.Step);
            ctx.DrawLine(new Pen(brush, brush == muted ? 2 : 4, lineCap: PenLineCap.Round), P(a), P(b));
        }
        // Where the panel starts (and, on a closed curve, where its ends meet).
        var seam = P(pts[0]);
        ctx.DrawEllipse(ink, null, seam, 3.5, 3.5);

        // The flat panel underneath.
        double flatTop = topH + 18;
        double flatH = H - flatTop - 4;
        double kf = Math.Min((W - 2) / Math.Max(1e-6, r.Length), flatH / Math.Max(1e-6, r.Settings.Height));
        double fx = (W - r.Length * kf) / 2, fy = flatTop;
        Write($"Flat: {r.Length:0.#} × {r.Settings.Height:0.#} mm", 0, topH, muted);
        foreach (var b in r.Bands)
        {
            if (b.Kind == BandKind.Solid) continue;
            var fill = b.Kind == BandKind.Hinge ? accent : TooTight;
            ctx.DrawRectangle(new SolidColorBrush(((ISolidColorBrush)fill).Color, 0.18), null, new Rect(fx + b.From * kf, fy, (b.To - b.From) * kf, r.Settings.Height * kf));
        }
        var slit = new Pen(ink, Math.Clamp(kf * 0.2, 0.5, 1));
        foreach (var sp in r.Slits.Subpaths)
        {
            var a = sp.Start; var b = sp.End;
            ctx.DrawLine(slit, new Point(fx + a.X * kf, fy + a.Y * kf), new Point(fx + b.X * kf, fy + b.Y * kf));
        }
        ctx.DrawRectangle(null, new Pen(ink, 1), new Rect(fx, fy, r.Length * kf, r.Settings.Height * kf));
        ctx.DrawEllipse(ink, null, new Point(fx, fy), 3.5, 3.5);
        // Where a panel too long for the work area is cut into pieces.
        var join = new Pen(UiKit.Brush("Warn"), 2, new DashStyle([3, 2], 0));
        foreach (var s in _splits)
        {
            ctx.DrawLine(join, new Point(fx + s * kf, fy - 4), new Point(fx + s * kf, fy + r.Settings.Height * kf + 4));
            var at = r.Profile.PointAt(s);
            var d = r.Profile.DirectionAt(s);
            var across = new Vec2(-d.Y, d.X) * (7 / k);
            ctx.DrawLine(join, P(at - across), P(at + across));
        }
    }
}
