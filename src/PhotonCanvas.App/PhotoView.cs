using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Core;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.App;

/// <summary>A marker drawn over the photo, at a normalised (0–1) position.</summary>
public sealed record PhotoMarker(Vec2 At, string Label, Color Color, bool Selected = false, bool Hollow = false);

/// <summary>
/// Zoomable photo with clickable markers. Wheel zooms at the cursor, middle or
/// right drag pans, left click reports the normalised position.
/// </summary>
public sealed class PhotoView : Control
{
    Bitmap? _image;
    PixelSize? _imageSize;
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
            var size = value?.PixelSize;
            bool sizeChanged = size != _imageSize;
            _image = value;
            _imageSize = size;
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

    /// <summary>Where the next click is expected (normalised), drawn as a dashed ring with a label; null = none.</summary>
    public (Vec2 At, string Label)? Expect { get => _expect; set { _expect = value; InvalidateVisual(); } }
    (Vec2 At, string Label)? _expect;

    /// <summary>Zoom in on <paramref name="n"/> (normalised) so about <paramref name="widthFraction"/> of the photo's width fills the view.</summary>
    public void ZoomTo(Vec2 n, double widthFraction)
    {
        if (_imageSize is not { } s || Bounds.Width < 10) return;
        double fit = Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height);
        _zoom = Math.Clamp(Bounds.Width / (s.Width * Math.Clamp(widthFraction, 0.02, 1)), fit, 8);
        _offset = new(Bounds.Width / 2 - n.X * s.Width * _zoom, Bounds.Height / 2 - n.Y * s.Height * _zoom);
        _fitted = true;
        InvalidateVisual();
    }

    public void Fit()
    {
        if (_imageSize is not { } s || Bounds.Width < 10) return;
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

    Point ToScreen(Vec2 n) => _imageSize is not { } s ? default
        : new(_offset.X + n.X * s.Width * _zoom, _offset.Y + n.Y * s.Height * _zoom);

    Vec2 ToNorm(Point p) => _imageSize is not { } s ? default
        : new((p.X - _offset.X) / _zoom / s.Width, (p.Y - _offset.Y) / _zoom / s.Height);

    /// <summary>Distance in screen pixels between a normalised point and a screen point.</summary>
    public double ScreenDistance(Vec2 n, Vec2 other) => Dist(ToScreen(n), ToScreen(other));

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0E1014")), new Rect(Bounds.Size));
        if (_image is null || _imageSize is not { } s)
        {
            var t = new FormattedText("No photo yet", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gray);
            ctx.DrawText(t, new Point((Bounds.Width - t.Width) / 2, (Bounds.Height - t.Height) / 2));
            return;
        }
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
        if (_expect is { } ex)
        {
            // The expected spot: a big dashed ring, so the mark itself stays visible inside it.
            var p = ToScreen(ex.At);
            var pen = new Pen(new SolidColorBrush(Color.Parse("#3DD6F5")), 2, new DashStyle([5, 4], 0));
            ctx.DrawEllipse(null, pen, p, 34, 34);
            var t = new FormattedText(ex.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 13, Brushes.Black);
            var box = new Rect(p.X - t.Width / 2 - 6, p.Y - 34 - t.Height - 8, t.Width + 12, t.Height + 4);
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#3DD6F5")), box, 4);
            ctx.DrawText(t, new Point(box.X + 6, box.Y + 2));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_imageSize is not { } s) return;
        var p = e.GetPosition(this);
        var n = ToNorm(p);
        double fit = Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), fit * 0.5, 8);
        _offset = new(p.X - n.X * s.Width * _zoom, p.Y - n.Y * s.Height * _zoom);
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
