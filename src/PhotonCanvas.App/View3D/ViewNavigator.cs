using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PhotonCanvas.App.View3D;

/// <summary>What a view of a model does when it's turned, moved or zoomed (by <see cref="ViewNavigator"/>).</summary>
public interface INavigable
{
    /// <summary>Turn the model as if dragged by (<paramref name="dx"/>, <paramref name="dy"/>) screen units.</summary>
    void Turn(double dx, double dy);

    /// <summary>Move what's in the view by <paramref name="by"/> screen units (right, down).</summary>
    void Move(Vector by);

    /// <summary>
    /// Draw it <paramref name="factor"/> times as big, keeping what's under <paramref name="at"/> (a point in the view)
    /// where it is; without a point, what's in the middle.
    /// </summary>
    void Zoom(double factor, Point? at);

    /// <summary>The whole model in the view again, seen the same way.</summary>
    void Fit();

    /// <summary>Back to the way it was first shown: the starting angle, the whole model in the view.</summary>
    void Reset();
}

/// <summary>
/// The same mouse, wheel, keys and buttons for every view of a model (the makers' 3D views, and others):
/// <list type="bullet">
/// <item>drag to turn it; drag with the middle or right button, or with Shift held, to move it;</item>
/// <item>the wheel zooms in and out on what's under the pointer;</item>
/// <item>the arrow keys move it (Ctrl + arrows turn it), + and − zoom, F fits it in the view, Home goes back to
/// the starting view; double-click fits it too;</item>
/// <item>buttons in the bottom-right corner: the whole model, zoom in, zoom out, the starting view.</item>
/// </list>
/// </summary>
public sealed class ViewNavigator
{
    /// <summary>How far an arrow key moves the view (screen units); a tenth of it with Shift held.</summary>
    const double Step = 40;

    /// <summary>How much one notch of the wheel, or a press of + or −, zooms.</summary>
    const double Notch = 1.2;

    /// <summary>What the view says in its corner about how to use it.</summary>
    public const string Hint = "Drag to turn · middle-drag or arrows to move · scroll to zoom";

    readonly Control _host;
    readonly INavigable _view;
    readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(220) };
    Point? _turning, _moving;
    // Where the left button went down, and whether it has moved off it since (a drag, not a click).
    Point _pressedAt;
    bool _dragged;
    KeyModifiers _pressedWith;
    Cursor? _cursor;

    /// <summary>How far the pointer can move with the button down and still be a click, not a drag (screen units).</summary>
    const double ClickSlop = 4;

    /// <summary>
    /// Hooks <paramref name="host"/>'s mouse and keys up to <paramref name="view"/>. The host puts <see cref="Buttons"/> in itself
    /// (in its visual and logical children: they place themselves in its bottom-right corner).
    /// </summary>
    public ViewNavigator(Control host, INavigable view)
    {
        _host = host;
        _view = view;
        host.Focusable = true;
        host.ClipToBounds = true;
        host.Cursor = new Cursor(StandardCursorType.SizeAll);
        host.AddHandler(InputElement.PointerPressedEvent, Pressed);
        host.AddHandler(InputElement.PointerMovedEvent, Moved);
        host.AddHandler(InputElement.PointerReleasedEvent, Released);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Stop());
        host.AddHandler(InputElement.PointerWheelChangedEvent, Wheel);
        host.AddHandler(InputElement.KeyDownEvent, Key);
        _settle.Tick += (_, _) => { _settle.Stop(); _host.InvalidateVisual(); };
        Buttons = MakeButtons(view);
    }

    /// <summary>The buttons, for the bottom-right corner of the view.</summary>
    public Control Buttons { get; }

    /// <summary>Puts a button of the view's own at the start of the row of <see cref="Buttons"/> (26 × 26, not taking the keyboard).</summary>
    public void AddButton(Control button)
    {
        if (Buttons is Border { Child: Panel row }) row.Children.Insert(0, button);
    }

    /// <summary>
    /// Being turned, moved or zoomed just now: draw quickly (roughly), and properly once it's still. The view is drawn
    /// again by itself when it settles.
    /// </summary>
    public bool Busy => _turning is not null || _moving is not null || _settle.IsEnabled;

    /// <summary>Draws <see cref="Hint"/> (or <paramref name="hint"/>) in the bottom-left corner of the view.</summary>
    public static void DrawHint(DrawingContext ctx, Rect bounds, string hint = Hint)
    {
        var text = new FormattedText(hint, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"))
        {
            // Clear of the buttons on the right.
            MaxTextWidth = Math.Max(40, bounds.Width - 150),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        // On a backing like the buttons', so it reads over the model when that's zoomed in under it.
        var at = new Point(12, bounds.Height - 22 - text.Height / 2);
        ctx.DrawRectangle(UiKit.Brush("HudBg"), null, new Rect(at.X - 6, at.Y - 4, text.Width + 12, text.Height + 8), 7, 7);
        ctx.DrawText(text, at);
    }

    void Busied()
    {
        _settle.Stop();
        _settle.Start();
    }

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_host);
        var props = point.Properties;
        _host.Focus();
        if (e.ClickCount == 2 && (props.IsLeftButtonPressed || props.IsMiddleButtonPressed))
        {
            Stop();
            _view.Fit();
            e.Handled = true;
            return;
        }
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || props.IsLeftButtonPressed && (e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _moving = point.Position;
            _cursor = _host.Cursor;
            _host.Cursor = new Cursor(StandardCursorType.Hand);
        }
        else if (props.IsLeftButtonPressed)
        {
            _turning = _pressedAt = point.Position;
            _dragged = false;
            _pressedWith = e.KeyModifiers;
        }
        else return;
        e.Pointer.Capture(_host);
        e.Handled = true;
    }

    void Moved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(_host);
        if (_moving is { } from)
        {
            _moving = at;
            _view.Move(at - from);
        }
        else if (_turning is { } start)
        {
            // A click that wobbles a little doesn't turn it.
            if (!_dragged && Math.Abs(at.X - _pressedAt.X) <= ClickSlop && Math.Abs(at.Y - _pressedAt.Y) <= ClickSlop) return;
            _dragged = true;
            _turning = at;
            _view.Turn(at.X - start.X, at.Y - start.Y);
        }
    }

    void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_turning is null && _moving is null) return;
        bool click = _turning is not null && !_dragged && e.InitialPressMouseButton == MouseButton.Left;
        e.Pointer.Capture(null);
        Stop();
        if (click) Click?.Invoke(_pressedAt, _pressedWith);
    }

    /// <summary>A click with the left button (pressed and let go without dragging): where, and the keys held.</summary>
    public Action<Point, KeyModifiers>? Click { get; set; }

    void Stop()
    {
        if (_turning is null && _moving is null) return;
        if (_moving is not null && _cursor is not null) _host.Cursor = _cursor;
        _turning = _moving = null;
        // Drawn properly now it's still.
        _host.InvalidateVisual();
    }

    /// <summary>Offered the wheel first (a view that uses Ctrl + wheel for something of its own); true when it took it.</summary>
    public Func<PointerWheelEventArgs, bool>? WheelFirst { get; set; }

    void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (WheelFirst?.Invoke(e) == true) { e.Handled = true; return; }
        if (e.Delta.Y == 0) return;
        Busied();
        _view.Zoom(Math.Pow(Notch, Math.Clamp(e.Delta.Y, -5, 5)), e.GetPosition(_host));
        e.Handled = true;
    }

    void Key(object? sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? Step / 4 : Step;
        Vector? arrow = e.Key switch
        {
            Avalonia.Input.Key.Left => new Vector(-1, 0),
            Avalonia.Input.Key.Right => new Vector(1, 0),
            Avalonia.Input.Key.Up => new Vector(0, -1),
            Avalonia.Input.Key.Down => new Vector(0, 1),
            _ => null,
        };
        if (arrow is { } a)
        {
            Busied();
            // Ctrl turns it about 15° a press, as dragging that way would; otherwise the model moves the way the arrow points.
            if (ctrl) _view.Turn(a.X * 30, a.Y * 37.5);
            else _view.Move(a * step);
        }
        else if (e.Key is Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add) { Busied(); _view.Zoom(Notch, null); }
        else if (e.Key is Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract) { Busied(); _view.Zoom(1 / Notch, null); }
        else if (e.Key is Avalonia.Input.Key.F || ctrl && e.Key is Avalonia.Input.Key.D0 or Avalonia.Input.Key.NumPad0) _view.Fit();
        else if (e.Key is Avalonia.Input.Key.Home) _view.Reset();
        else return;
        e.Handled = true;
    }

    /// <summary>The buttons, in order: icon, what it does (with its key).</summary>
    public static readonly (string Icon, string Tip)[] ButtonTips =
    [
        ("IcFitDesign", "Show the whole model (F, or double-click)"),
        ("IcZoomIn", "Zoom in (+, or scroll up)"),
        ("IcZoomOut", "Zoom out (−, or scroll down)"),
        ("IcRotateCcw", "Back to the starting view (Home)"),
    ];

    static Control MakeButtons(INavigable view)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        Action[] actions = [view.Fit, () => view.Zoom(Notch, null), () => view.Zoom(1 / Notch, null), view.Reset];
        for (int i = 0; i < ButtonTips.Length; i++)
        {
            var (icon, tip) = ButtonTips[i];
            var act = actions[i];
            var b = new Button
            {
                Classes = { "icon" },
                Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                // Clicking leaves the keyboard with the view, so its keys keep working.
                Focusable = false,
                Content = new PathIcon { Data = UiKit.Icon(icon), Width = 15, Height = 15 },
                Cursor = new Cursor(StandardCursorType.Arrow),
            };
            Avalonia.Automation.AutomationProperties.SetName(b, tip);
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => act();
            row.Children.Add(b);
        }
        return new Border
        {
            Background = UiKit.Brush("HudBg"), BorderBrush = UiKit.Brush("Line"), BorderThickness = new(1),
            CornerRadius = new(7), Padding = new(2),
            Margin = new(0, 0, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = row,
        };
    }
}
