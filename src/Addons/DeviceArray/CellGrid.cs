using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.DeviceArray;

/// <summary>
/// The matrix as squares, the first device top left as on the bed: on ones filled, off ones crossed
/// out, each with its number in the counting order. Click one to turn it off or on; drag across
/// several to do the same to all of them.
/// </summary>
sealed class CellGrid : Control
{
    ArraySpec _spec = new();
    Dictionary<Cell, int> _numbers = [];
    bool? _painting;
    Cell? _last;

    /// <summary>A device was clicked or dragged over: it and whether it should now be on.</summary>
    public event Action<Cell, bool>? Toggled;

    public ArraySpec Spec
    {
        get => _spec;
        set
        {
            _spec = value;
            _numbers = value.Numbers();
            InvalidateVisual();
        }
    }

    public CellGrid()
    {
        MinHeight = 160;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    (double Size, double X0, double Y0) Layout()
    {
        double w = Bounds.Width, h = Bounds.Height;
        double size = Math.Min(44, Math.Min(w / Math.Max(1, _spec.Columns), h / Math.Max(1, _spec.Rows)));
        size = Math.Max(size, 1);
        double x0 = (w - size * _spec.Columns) / 2, y0 = (h - size * _spec.Rows) / 2;
        return (size, Math.Max(0, x0), Math.Max(0, y0));
    }

    Cell? At(Point p)
    {
        var (size, x0, y0) = Layout();
        int c = (int)Math.Floor((p.X - x0) / size), r = (int)Math.Floor((p.Y - y0) / size);
        var cell = new Cell(c, r);
        return _spec.Contains(cell) ? cell : null;
    }

    public override void Render(DrawingContext ctx)
    {
        // Never throw from drawing: it would take the whole program down.
        try { Draw(ctx); }
        catch (Exception) { }
    }

    void Draw(DrawingContext ctx)
    {
        ctx.FillRectangle(Brush("Bg0"), new Rect(Bounds.Size));
        var (size, x0, y0) = Layout();
        double gap = size >= 8 ? Math.Max(1, size * 0.08) : 0;
        var on = Brush("AccentSoft");
        var onLine = new Pen(Brush("Accent"), 1);
        var offLine = new Pen(Brush("TextMuted"), 1);
        var offFill = Brush("Bg2");
        var text = Brush("TextMain");
        var muted = Brush("TextMuted");
        bool numbers = size >= 18;
        var face = new Typeface(FontFamily.Default);
        for (int r = 0; r < _spec.Rows; r++)
            for (int c = 0; c < _spec.Columns; c++)
            {
                var cell = new Cell(c, r);
                var rect = new Rect(x0 + c * size + gap / 2, y0 + r * size + gap / 2, Math.Max(1, size - gap), Math.Max(1, size - gap));
                bool isOn = _spec.IsOn(cell);
                if (isOn) ctx.DrawRectangle(on, size >= 6 ? onLine : null, rect, 3, 3);
                else
                {
                    ctx.DrawRectangle(offFill, size >= 6 ? offLine : null, rect, 3, 3);
                    if (size >= 6)
                    {
                        var d = rect.Deflate(size * 0.22);
                        ctx.DrawLine(offLine, d.TopLeft, d.BottomRight);
                        ctx.DrawLine(offLine, d.TopRight, d.BottomLeft);
                    }
                }
                if (cell.IsFirst && size >= 6) ctx.DrawRectangle(null, new Pen(Brush("Accent"), 2), rect.Inflate(0.5), 3, 3);
                if (!numbers || (!isOn && !_spec.KeepNumbers)) continue;
                var label = (_numbers.GetValueOrDefault(cell) + 1).ToString(CultureInfo.CurrentCulture);
                var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Math.Min(13, size * 0.4), isOn ? text : muted);
                ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
            }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || At(e.GetPosition(this)) is not { } cell) return;
        _painting = !_spec.IsOn(cell);
        _last = cell;
        e.Pointer.Capture(this);
        Toggled?.Invoke(cell, _painting.Value);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_painting is not { } paint || At(e.GetPosition(this)) is not { } cell || cell == _last) return;
        _last = cell;
        if (_spec.IsOn(cell) != paint) Toggled?.Invoke(cell, paint);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _painting = null;
        _last = null;
        e.Pointer.Capture(null);
    }
}
