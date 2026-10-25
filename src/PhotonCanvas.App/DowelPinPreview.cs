using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.App;

/// <summary>
/// The dowel as it lies in the machine, from its end: the cut lines across it (the first squaring
/// the end), with the pin length, the dowel's width, how long each cut line is and how much of the
/// dowel the pins use written on it.
/// </summary>
public sealed class DowelPinPreview : Control
{
    DowelPinPlan? _plan;
    double _dowelMm, _lengthMm;

    public void Show(DowelPinPlan? plan, double dowelMm, double lengthMm)
    {
        (_plan, _dowelMm, _lengthMm) = (plan, dowelMm, lengthMm);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) => new(Math.Min(available.Width, 340), 116);

    public override void Render(DrawingContext ctx)
    {
        if (_plan is not { } plan || _dowelMm <= 0) return;
        var text = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        var muted = new SolidColorBrush(Color.FromArgb(170, text.R, text.G, text.B));
        var ink = new SolidColorBrush(text);
        var accent = UiKit.Brush("Accent");
        void Write(string s, double x, double y, IBrush brush, double size = 11, bool centre = true)
        {
            var f = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush);
            ctx.DrawText(f, new Point(x - (centre ? f.Width / 2 : 0), y - f.Height / 2));
        }
        // The first few pins, big enough to read; the rest of the dowel trails off.
        int shown = Math.Min(plan.CutsAtMm.Count, 4);
        double spanMm = shown > 0 ? plan.CutsAtMm[shown - 1] + plan.PinLengthMm * 0.8 : plan.PinLengthMm * 3;
        double left = 34, width = Bounds.Width - left - 12;
        double k = Math.Min(width / spanMm, 40 / Math.Max(plan.CutLineMm, 0.1));
        double mid = 44, r = _dowelMm / 2 * k;
        var wood = new SolidColorBrush(Color.FromRgb(0xD8, 0xBC, 0x8E));
        ctx.DrawRectangle(wood, new Pen(muted, 1), new Rect(left, mid - r, Math.Min(width, spanMm * k), 2 * r));
        Write($"Ø {_dowelMm:0.##}", left - 17, mid, muted);
        var cutPen = new Pen(accent, 2);
        for (int i = 0; i < shown; i++)
        {
            double x = left + plan.CutsAtMm[i] * k, h = plan.CutLineMm / 2 * k;
            ctx.DrawLine(cutPen, new Point(x, mid - h), new Point(x, mid + h));
        }
        double half = plan.CutLineMm / 2 * k;
        if (shown >= 2)
        {
            double x0 = left + plan.CutsAtMm[0] * k, x1 = left + plan.CutsAtMm[1] * k;
            Write($"pin {plan.PinLengthMm:0.##} mm", (x0 + x1) / 2, mid - half - 10, ink);
        }
        if (shown >= 1)
            Write($"each cut {plan.CutLineMm:0.##} mm long", left + plan.CutsAtMm[0] * k, mid + half + 10, muted, centre: false);
        string count = plan.Wanted == 0 ? "No pin holes, so no pins."
            : $"{plan.Made} pin{(plan.Made == 1 ? "" : "s")} from {plan.UsedMm:0} mm of the {_lengthMm:0} mm dowel";
        Write(count, left, 104, plan.Short ? UiKit.Brush("Accent") : ink, 11, centre: false);
    }
}
