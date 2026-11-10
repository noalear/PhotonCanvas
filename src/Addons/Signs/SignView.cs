using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotonCanvas.Addon.Borders;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// The sign as it will carve, lit (or as its depth map): the whole of it to start with. The wheel
/// zooms in on what's under the pointer, dragging moves it, a double-click fits it again; whatever
/// is in view is drawn again at the screen's own detail.
/// </summary>
public sealed class SignView : Control
{
    SignPlan? _plan;
    PreviewLook _look = PreviewLook.Wood;
    string? _message;
    // The view: the middle of the screen shows world point _centre (mm), at _scale pixels per mm.
    Point _centre;
    double _scale;
    bool _fitted = true;
    Point? _dragFrom;
    // The last picture drawn, and the part of the world (mm) it covers.
    Bitmap? _bitmap;
    Rect _bitmapWorld;
    readonly DispatcherTimer _redraw = new() { Interval = TimeSpan.FromMilliseconds(90) };
    CancellationTokenSource? _cancel;
    int _generation;

    public SignView()
    {
        ClipToBounds = true;
        Focusable = true;
        _redraw.Tick += (_, _) => { _redraw.Stop(); _ = Draw(); };
    }

    /// <summary>Shows a sign (or, with none, why there's nothing to show). Keeps the view where it was unless it was fitted.</summary>
    public void Show(SignPlan? plan, string? message = null)
    {
        bool sizeChanged = plan is null || _plan is null || Math.Abs(plan.SizeW - _plan.SizeW) > 1e-6 || Math.Abs(plan.SizeH - _plan.SizeH) > 1e-6;
        _plan = plan;
        _message = message;
        if (plan is null) { Drop(); InvalidateVisual(); return; }
        if (_fitted || sizeChanged) Fit();
        Schedule();
    }

    public PreviewLook Look
    {
        get => _look;
        set { if (_look == value) return; _look = value; Schedule(); }
    }

    /// <summary>Back to seeing the whole sign.</summary>
    public void Fit()
    {
        _fitted = true;
        if (_plan is null || Bounds.Width < 10 || Bounds.Height < 10) return;
        _scale = Math.Min((Bounds.Width - 24) / _plan.SizeW, (Bounds.Height - 24) / _plan.SizeH);
        _centre = new Point(_plan.SizeW / 2, _plan.SizeH / 2);
        Schedule();
        InvalidateVisual();
    }

    void Drop()
    {
        _cancel?.Cancel();
        _bitmap?.Dispose();
        _bitmap = null;
    }

    void Schedule()
    {
        _redraw.Stop();
        _redraw.Start();
    }

    Point ToScreen(double x, double y) => new((x - _centre.X) * _scale + Bounds.Width / 2, (y - _centre.Y) * _scale + Bounds.Height / 2);

    Point ToWorld(Point p) => new((p.X - Bounds.Width / 2) / _scale + _centre.X, (p.Y - Bounds.Height / 2) / _scale + _centre.Y);

    async Task Draw()
    {
        if (_plan is not { } plan || !(_scale > 0) || Bounds.Width < 2 || Bounds.Height < 2) return;
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        int gen = ++_generation;
        double dpr = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        // What's in view, cut to the sign's own box.
        var tl = ToWorld(new Point(0, 0));
        var br = ToWorld(new Point(Bounds.Width, Bounds.Height));
        double x0 = Math.Max(0, tl.X), y0 = Math.Max(0, tl.Y), x1 = Math.Min(plan.SizeW, br.X), y1 = Math.Min(plan.SizeH, br.Y);
        if (x1 <= x0 || y1 <= y0) return;
        double mmPerPx = 1 / (_scale * dpr);
        int cols = Math.Clamp((int)Math.Ceiling((x1 - x0) / mmPerPx), 1, 4000), rows = Math.Clamp((int)Math.Ceiling((y1 - y0) / mmPerPx), 1, 4000);
        mmPerPx = Math.Max((x1 - x0) / cols, (y1 - y0) / rows);
        var look = _look;
        try
        {
            var bmp = await Task.Run(() =>
            {
                var h = plan.Render(x0, y0, mmPerPx, cols, rows, cancel.Token);
                if (plan.Spec.SoftenMm > 0) BorderPlan.Soften(h, cols, rows, plan.Spec.SoftenMm / mmPerPx);
                cancel.Token.ThrowIfCancellationRequested();
                var img = Shading.Shade(h, cols, rows, mmPerPx, look, plan.Top, plan.Lowest);
                return CameraImaging.ToAvaloniaRgba(img);
            }, cancel.Token);
            if (gen != _generation) { bmp.Dispose(); return; }
            _bitmap?.Dispose();
            _bitmap = bmp;
            _bitmapWorld = new Rect(x0, y0, cols * mmPerPx, rows * mmPerPx);
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitted) Fit(); else Schedule();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (e.ClickCount == 2) { Fit(); return; }
        if (pt.Properties.IsLeftButtonPressed || pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _dragFrom = pt.Position;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from || !(_scale > 0)) return;
        var p = e.GetPosition(this);
        _centre -= (p - from) / _scale;
        _dragFrom = p;
        _fitted = false;
        InvalidateVisual();
        Schedule();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_plan is null || !(_scale > 0)) return;
        var p = e.GetPosition(this);
        var before = ToWorld(p);
        double fit = Math.Min((Bounds.Width - 24) / _plan.SizeW, (Bounds.Height - 24) / _plan.SizeH);
        _scale = Math.Clamp(_scale * Math.Pow(1.25, e.Delta.Y), Math.Max(0.05, fit * 0.5), 200);
        var after = ToWorld(p);
        _centre += before - after;
        _fitted = false;
        InvalidateVisual();
        Schedule();
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_plan is null)
        {
            if (_message is { } m)
            {
                var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
                { MaxTextWidth = Math.Max(50, Bounds.Width - 40), TextAlignment = TextAlignment.Center };
                ctx.DrawText(text, new Point(20, Bounds.Height / 2 - text.Height / 2));
            }
            return;
        }
        if (_bitmap is { } bmp)
        {
            var a = ToScreen(_bitmapWorld.X, _bitmapWorld.Y);
            var b = ToScreen(_bitmapWorld.Right, _bitmapWorld.Bottom);
            ctx.DrawImage(bmp, new Rect(bmp.Size), new Rect(a, b));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _redraw.Stop();
        Drop();
    }
}
