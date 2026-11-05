using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotonCanvas.App.View3D;

/// <summary>
/// A view of a model in 3D (a maker's "put together" view), drawn by <see cref="Model3DPainter"/>, with the
/// camera controls every such view has (<see cref="ViewNavigator"/>): drag to turn it, middle-drag or the
/// arrow keys to move it, the wheel to zoom on what's under the pointer, F or the button to fit the whole
/// model in again, Home to go back to the starting view.
/// <para>
/// Until it's zoomed or moved, the whole model is fitted into the view however it's turned (as it changes,
/// too). Once zoomed or moved, it stays that size and turns about whatever is in the middle of the view, and
/// keeps that view while the model changes, so a detail can be watched while it's being changed.
/// </para>
/// <para>
/// A button beside them switches every 3D view between the plain look and the realistic one (<see cref="Look3D"/>).
/// </para>
/// Fill <see cref="Scene"/> and call <see cref="Visual.InvalidateVisual"/> when the model changes.
/// </summary>
public abstract class Model3DView : Control, INavigable
{
    readonly Model3DPainter _painter = new();
    readonly ViewNavigator _nav;
    readonly double _yaw0, _pitch0, _minPitch, _maxPitch;
    double _yaw, _pitch;
    Framing? _framing;

    /// <summary>
    /// A view first seen turned <paramref name="yaw"/>° round and looking down from <paramref name="pitch"/>° above level, which can be
    /// tipped between <paramref name="minPitch"/>° and <paramref name="maxPitch"/>°.
    /// </summary>
    protected Model3DView(double yaw, double pitch, double minPitch = -15, double maxPitch = 85)
    {
        _yaw = _yaw0 = yaw;
        _pitch = _pitch0 = pitch;
        _minPitch = minPitch;
        _maxPitch = maxPitch;
        _nav = new ViewNavigator(this, this) { WheelFirst = Wheel, Click = (at, keys) => Clicked(at, keys) };
        _look = new ToggleButton
        {
            Classes = { "tool" },
            Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new(0), Margin = new(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false,
            Content = new PathIcon { Data = UiKit.Icon("IcBulb"), Width = 15, Height = 15 },
            Cursor = new Cursor(StandardCursorType.Arrow),
            IsChecked = Look3D.Realistic,
        };
        Avalonia.Automation.AutomationProperties.SetName(_look, LookTip);
        ToolTip.SetTip(_look, LookTip);
        _look.IsCheckedChanged += (_, _) => Look3D.Realistic = _look.IsChecked == true;
        _nav.AddButton(_look);
        VisualChildren.Add(_nav.Buttons);
        LogicalChildren.Add(_nav.Buttons);
        AttachedToVisualTree += (_, _) => { Look3D.Changed += LookChanged; LookChanged(); };
        DetachedFromVisualTree += (_, _) => Look3D.Changed -= LookChanged;
    }

    /// <summary>What the look button says.</summary>
    public const string LookTip = "Realistic look: wood grain, charred edges, sunlight and shadows (a little slower to turn). Switches every 3D view.";

    readonly ToggleButton _look;

    void LookChanged()
    {
        _look.IsChecked = Look3D.Realistic;
        InvalidateVisual();
    }

    /// <summary>The wheel, before it zooms: true when the view used it for something of its own (with Ctrl held, say).</summary>
    protected virtual bool Wheel(Avalonia.Input.PointerWheelEventArgs e) => false;

    /// <summary>A click on the view (not a drag): see what's there with <see cref="HitAt"/> to pick it.</summary>
    protected virtual void Clicked(Point at, Avalonia.Input.KeyModifiers keys) { }

    /// <summary>What's under a point of the view, as last drawn (null if nothing, or the scene changed since).</summary>
    protected Hit3D? HitAt(Point at, double reachPx = 4) => _painter.HitAt(Scene, at, reachPx);

    /// <summary>The model.</summary>
    protected Scene3D Scene { get; } = new();

    /// <summary>What to say when there's nothing to draw.</summary>
    protected string? Message { get; set; }

    /// <summary>Groups of the scene left out of the picture.</summary>
    protected virtual ulong HiddenGroups => 0;

    /// <summary>What the view says in its corner about how to use it.</summary>
    protected virtual string Hint => ViewNavigator.Hint;

    /// <summary>The camera at an angle: Y up unless overridden.</summary>
    protected virtual Camera3D CameraAt(double yaw, double pitch) => Camera3D.YUp(yaw, pitch);

    /// <summary>Called before each drawing, to bring the scene up to date.</summary>
    protected virtual void BeforeDraw() { }

    /// <summary>The whole model is fitted into the view (it hasn't been zoomed or moved since).</summary>
    public bool Fitted => _framing is null;

    /// <summary>How big it was last drawn: screen units per mm.</summary>
    public double PxPerMm => _painter.PxPerMm;

    Camera3D Camera => CameraAt(_yaw, _pitch);

    public sealed override void Render(DrawingContext ctx)
    {
        BeforeDraw();
        if (!_painter.Draw(ctx, this, Scene, Camera, UiKit.Brush("Bg2"), HiddenGroups, _nav.Busy, _framing, Look3D.Realistic))
        {
            if (Message is { } m) DrawMessage(ctx, Bounds, m);
            return;
        }
        ViewNavigator.DrawHint(ctx, Bounds, Hint);
    }

    /// <summary>A message in the middle of a view.</summary>
    public static void DrawMessage(DrawingContext ctx, Rect bounds, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
        {
            MaxTextWidth = Math.Max(50, bounds.Width - 40),
            TextAlignment = TextAlignment.Center,
        };
        ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    public void Turn(double dx, double dy)
    {
        _yaw = (_yaw - dx * 0.5) % 360;
        _pitch = Math.Clamp(_pitch + dy * 0.4, _minPitch, _maxPitch);
        InvalidateVisual();
    }

    /// <summary>How it's drawn now; null before it's first drawn.</summary>
    Framing? Now => _framing ?? (_painter.Shown.PxPerMm > 0 ? _painter.Shown : null);

    public void Move(Vector by)
    {
        if (Now is not { } f) return;
        _framing = f.Moved(Camera, by);
        InvalidateVisual();
    }

    public void Zoom(double factor, Point? at)
    {
        if (Now is not { } f || !(factor > 0)) return;
        // From a tenth of the size that just fits, up to 200 times it (close enough to see a tenth of a mm).
        double fit = _painter.Fitted.PxPerMm > 0 ? _painter.Fitted.PxPerMm : f.PxPerMm;
        double k = Math.Clamp(f.PxPerMm * factor, fit / 10, fit * 200);
        var middle = Model3DPainter.Middle(Bounds.Size);
        var offset = at is { } p ? p - middle : default;
        _framing = f.Zoomed(Camera, k, offset);
        InvalidateVisual();
    }

    public void Fit()
    {
        _framing = null;
        InvalidateVisual();
    }

    public void Reset()
    {
        _yaw = _yaw0;
        _pitch = _pitch0;
        Fit();
    }
}
