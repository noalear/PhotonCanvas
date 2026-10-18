using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PhotonCanvas.App;

/// <summary>
/// What a cut test will look like: the grid of squares with every speed written over its column and
/// every pass count (or power) beside its row, the squares shaded by how hard they're cut (darker =
/// slower or more passes, so more likely to go through).
/// </summary>
public sealed class CutTestPreview : Control
{
    IReadOnlyList<double> _speeds = [];
    IReadOnlyList<double> _down = [];
    bool _power;
    double _fixedPower = 100;
    int _fixedPasses = 1;

    public void Show(IReadOnlyList<double> speeds, IReadOnlyList<double> down, bool power, double fixedPower, int fixedPasses)
    {
        (_speeds, _down, _power, _fixedPower, _fixedPasses) = (speeds, down, power, fixedPower, fixedPasses);
        InvalidateMeasure();
        InvalidateVisual();
    }

    const double Cell = 34, Gap = 8, Left = 86, Top = 50;

    protected override Size MeasureOverride(Size available) =>
        new(Math.Min(available.Width, Left + Math.Max(1, _speeds.Count) * (Cell + Gap) + 10), Top + Math.Max(1, _down.Count) * (Cell + Gap) + 26);

    public override void Render(DrawingContext ctx)
    {
        if (_speeds.Count == 0 || _down.Count == 0) return;
        var text = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        var muted = new SolidColorBrush(Color.FromArgb(170, text.R, text.G, text.B));
        var ink = new SolidColorBrush(text);
        var accent = UiKit.Brush("Accent");
        void Write(string s, double x, double y, IBrush brush, double size = 12, bool centre = true, bool right = false, FontWeight weight = FontWeight.Normal)
        {
            var f = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, weight), size, brush);
            double dx = right ? -f.Width : centre ? -f.Width / 2 : 0;
            ctx.DrawText(f, new Point(x + dx, y - f.Height / 2));
        }
        double gridW = _speeds.Count * (Cell + Gap) - Gap, gridH = _down.Count * (Cell + Gap) - Gap;
        Write("Speed, mm/s   (faster →)", Left + gridW / 2, 12, ink, 12, weight: FontWeight.SemiBold);
        for (int c = 0; c < _speeds.Count; c++)
            Write($"{_speeds[c]:0.#}", Left + c * (Cell + Gap) + Cell / 2, Top - 14, muted);
        // How hard each square is cut, for the shading: energy per mm.
        double Energy(int r, int c) => (_power ? _down[r] * _fixedPasses : _fixedPower * _down[r]) / _speeds[c];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int r = 0; r < _down.Count; r++)
            for (int c = 0; c < _speeds.Count; c++) { lo = Math.Min(lo, Energy(r, c)); hi = Math.Max(hi, Energy(r, c)); }
        var line = new Pen(accent, 1.5);
        for (int r = 0; r < _down.Count; r++)
        {
            double y = Top + r * (Cell + Gap);
            Write(_power ? $"{_down[r]:0.#}%" : $"{_down[r]:0} pass{(_down[r] == 1 ? "" : "es")}", Left - 10, y + Cell / 2, muted, right: true);
            for (int c = 0; c < _speeds.Count; c++)
            {
                double k = hi > lo ? (Energy(r, c) - lo) / (hi - lo) : 0.5;
                // Pale wood (cut gently) to char (cut hard).
                var fill = new SolidColorBrush(Color.FromRgb((byte)(0xD8 + (0x3A - 0xD8) * k), (byte)(0xBC + (0x24 - 0xBC) * k), (byte)(0x8E + (0x16 - 0x8E) * k)));
                ctx.DrawRectangle(fill, line, new Rect(Left + c * (Cell + Gap), y, Cell, Cell), 2, 2);
            }
        }
        string fixedText = _power ? $"Every square: {_fixedPasses} pass{(_fixedPasses == 1 ? "" : "es")}" : $"Every square: {_fixedPower:0.#}% power";
        Write(fixedText + "   ·   darker = cut harder, more likely to go through", Left, Top + gridH + 16, muted, 11, centre: false);
    }
}
