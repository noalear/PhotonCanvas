using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>
/// The relief preview, fitted to the space. Dragging moves the photo on the plaque, the wheel zooms
/// it, and while <see cref="Picking"/> is on a click marks a place on the background instead.
/// </summary>
public sealed class ReliefView : Control
{
    Bitmap? _bitmap;
    string? _message;
    Point? _dragFrom;
    bool _dragged;

    /// <summary>The picture's size on the work area (mm), to turn a drag into mm.</summary>
    public Size PlaqueMm { get; set; } = new(100, 100);
    /// <summary>A click marks the background (it doesn't move the photo).</summary>
    public bool Picking { get; set; }
    /// <summary>The photo was dragged this far (mm on the plaque).</summary>
    public event Action<double, double>? Dragged;
    /// <summary>The wheel turned: zoom the photo by this factor.</summary>
    public event Action<double>? Zoomed;
    /// <summary>A click while picking, at this point of the plaque (fractions 0–1 across and down).</summary>
    public event Action<double, double>? Picked;

    public ReliefView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Shows a new picture (the view takes it and disposes of the last one), or a message instead.</summary>
    public void Show(Bitmap? bitmap, string? message = null)
    {
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _message = message;
        InvalidateVisual();
    }

    /// <summary>Where the picture is drawn in the control.</summary>
    Rect Placed()
    {
        double pw = PlaqueMm.Width, ph = PlaqueMm.Height;
        if (_bitmap is { } b) { pw = b.PixelSize.Width; ph = b.PixelSize.Height; }
        if (pw <= 0 || ph <= 0 || Bounds.Width < 20 || Bounds.Height < 20) return default;
        double k = Math.Min((Bounds.Width - 24) / pw, (Bounds.Height - 24) / ph);
        double w = pw * k, h = ph * k;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }

    public override void Render(DrawingContext ctx)
    {
        // An exception here would take the program down (Render isn't guarded).
        try
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x23, 0x25, 0x2b)), new Rect(Bounds.Size));
            if (_bitmap is { } b && Placed() is var r && r.Width > 0)
            {
                using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    ctx.DrawImage(b, new Rect(0, 0, b.PixelSize.Width, b.PixelSize.Height), r);
            }
            if (_message is { Length: > 0 } m)
            {
                var t = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gainsboro)
                { MaxTextWidth = Math.Max(50, Bounds.Width - 60), TextAlignment = TextAlignment.Center };
                ctx.DrawText(t, new Point(30, Bounds.Height / 2 - t.Height / 2));
            }
        }
        catch (Exception) { /* leave the frame as it is */ }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this);
        _dragged = false;
        Cursor = new Cursor(Picking ? StandardCursorType.Cross : StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from || Picking) return;
        var p = e.GetPosition(this);
        var r = Placed();
        if (r.Width <= 0) return;
        double k = PlaqueMm.Width / r.Width;
        var d = p - from;
        if (!_dragged && Math.Abs(d.X) + Math.Abs(d.Y) < 3) return;
        _dragged = true;
        _dragFrom = p;
        Dragged?.Invoke(d.X * k, d.Y * k);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is { } from && Picking && !_dragged)
        {
            var r = Placed();
            if (r.Width > 0 && r.Contains(from)) Picked?.Invoke((from.X - r.X) / r.Width, (from.Y - r.Y) / r.Height);
        }
        _dragFrom = null;
        Cursor = Picking ? new Cursor(StandardCursorType.Cross) : null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        Zoomed?.Invoke(Math.Pow(1.12, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
