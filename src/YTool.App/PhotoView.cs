using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using YTool.Core;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace YTool.App;

/// <summary>A marker drawn over the photo, at a normalised (0–1) position.</summary>
public sealed record PhotoMarker(Vec2 At, string Label, Color Color, bool Selected = false, bool Hollow = false);

/// <summary>
/// Zoomable photo with clickable markers. Wheel zooms at the cursor, middle or
/// right drag pans, left click reports the normalised position.
/// </summary>
public sealed class PhotoView : Control
{
    Bitmap? _image;
    IReadOnlyList<PhotoMarker> _markers = [];
    double _zoom = 1;          // screen px per image px
    Point _offset;             // screen position of image (0, 0)
    bool _fitted;
    Point? _panFrom;
    Point _press;

    public Bitmap? Image
    {
        get => _image;
        set
        {
            bool sizeChanged = value?.PixelSize != _image?.PixelSize;
            _image = value;
            if (sizeChanged) _fitted = false;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<PhotoMarker> Markers
    {
        get => _markers;
        set { _markers = value; InvalidateVisual(); }
    }

    /// <summary>Left click (without dragging) at a normalised position.</summary>
    public event Action<Vec2>? Clicked;
    /// <summary>Right click (without dragging) at a normalised position.</summary>
    public event Action<Vec2>? RightClicked;

    public PhotoView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void Fit()
    {
        if (_image is null || Bounds.Width < 10) return;
        var s = _image.PixelSize;
        _zoom = Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height);
        _offset = new((Bounds.Width - s.Width * _zoom) / 2, (Bounds.Height - s.Height * _zoom) / 2);
        _fitted = true;
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = base.ArrangeOverride(finalSize);
        if (!_fitted) Avalonia.Threading.Dispatcher.UIThread.Post(Fit);
        return r;
    }

    Point ToScreen(Vec2 n) => _image is null ? default
        : new(_offset.X + n.X * _image.PixelSize.Width * _zoom, _offset.Y + n.Y * _image.PixelSize.Height * _zoom);

    Vec2 ToNorm(Point p) => _image is null ? default
        : new((p.X - _offset.X) / _zoom / _image.PixelSize.Width, (p.Y - _offset.Y) / _zoom / _image.PixelSize.Height);

    /// <summary>Distance in screen pixels between a normalised point and a screen point.</summary>
    public double ScreenDistance(Vec2 n, Vec2 other) => Dist(ToScreen(n), ToScreen(other));

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0E1014")), new Rect(Bounds.Size));
        if (_image is null)
        {
            var t = new FormattedText("No photo yet", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gray);
            ctx.DrawText(t, new Point((Bounds.Width - t.Width) / 2, (Bounds.Height - t.Height) / 2));
            return;
        }
        var s = _image.PixelSize;
        ctx.DrawImage(_image, new Rect(0, 0, s.Width, s.Height), new Rect(_offset.X, _offset.Y, s.Width * _zoom, s.Height * _zoom));
        foreach (var m in _markers)
        {
            var p = ToScreen(m.At);
            var pen = new Pen(new SolidColorBrush(m.Color), m.Selected ? 2.5 : 1.5);
            double r = m.Selected ? 11 : 8;
            ctx.DrawEllipse(m.Hollow ? null : new SolidColorBrush(Color.FromArgb(50, m.Color.R, m.Color.G, m.Color.B)), pen, p, r, r);
            ctx.DrawLine(pen, new(p.X - r - 4, p.Y), new(p.X - 3, p.Y));
            ctx.DrawLine(pen, new(p.X + 3, p.Y), new(p.X + r + 4, p.Y));
            ctx.DrawLine(pen, new(p.X, p.Y - r - 4), new(p.X, p.Y - 3));
            ctx.DrawLine(pen, new(p.X, p.Y + 3), new(p.X, p.Y + r + 4));
            if (m.Label.Length > 0)
            {
                var t = new FormattedText(m.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.White);
                var box = new Rect(p.X + r + 4, p.Y - r - t.Height - 2, t.Width + 8, t.Height + 2);
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(200, m.Color.R, m.Color.G, m.Color.B)), box, 3);
                ctx.DrawText(t, new Point(box.X + 4, box.Y + 1));
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_image is null) return;
        var p = e.GetPosition(this);
        var n = ToNorm(p);
        double fit = Math.Min(Bounds.Width / _image.PixelSize.Width, Bounds.Height / _image.PixelSize.Height);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), fit * 0.5, 8);
        _offset = new(p.X - n.X * _image.PixelSize.Width * _zoom, p.Y - n.Y * _image.PixelSize.Height * _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _press = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _panFrom = _press;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panFrom is not { } from) return;
        var p = e.GetPosition(this);
        _offset += p - from;
        _panFrom = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        bool click = Dist(p, _press) < 4;
        _panFrom = null;
        e.Pointer.Capture(null);
        if (!click || _image is null) return;
        if (e.InitialPressMouseButton == MouseButton.Left) Clicked?.Invoke(ToNorm(p));
        else if (e.InitialPressMouseButton == MouseButton.Right) RightClicked?.Invoke(ToNorm(p));
    }
}
