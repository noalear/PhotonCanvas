using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The floor and the way up put together in the room, drawn in 3D from above and to one side: every
/// piece at its thickness where it sits, the etched face showing its detail, and the walls and their
/// columns faint behind. Drag to turn it round; the floor can be lifted off to see under it.
/// </summary>
public sealed class AssembledView : Control
{
    readonly Scene3D _scene = new();
    readonly Model3DPainter _painter = new();
    double _yaw = 215, _pitch = 34;
    Point? _drag;
    string? _message;
    bool _liftFloor;

    /// <summary>The floor's group in the scene, left out when it's lifted off.</summary>
    const int FloorGroup = 1;

    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        ((ISolidColorBrush)PiecesView.EdgeBrush).Color, LineWidth.Px(0.7),
        ((ISolidColorBrush)PiecesView.Burnt).Color,
        ((ISolidColorBrush)PiecesView.ScoreBrush).Color, new LineWidth(0.05, 0.3, 0.9));

    /// <summary>The walls and their columns: faint, see-through, so the stairs show behind them.</summary>
    static readonly SlabStyle GhostLook = Look with
    {
        Paint = Faint(SlabStyle.Lit(Color.FromRgb(0xB8, 0xB8, 0xC0), new Vec3(0.35, 0.85, 0.45))),
        Edge = Color.FromArgb(0x70, 0x60, 0x60, 0x70),
        EdgeWidth = LineWidth.Px(0.6),
    };

    static Func<Vec3, bool, Color> Faint(Func<Vec3, bool, Color> paint) => (n, side) => { var c = paint(n, side); return Color.FromArgb(0x80, c.R, c.G, c.B); };

    public AssembledView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Leave the floor out of the picture, to see what's under it.</summary>
    public bool LiftFloor
    {
        get => _liftFloor;
        set { _liftFloor = value; InvalidateVisual(); }
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(FloorKit? kit, double thickness, string? message = null)
    {
        _scene.Clear();
        _message = message;
        if (kit is not null)
        {
            foreach (var g in kit.Ghosts)
                _scene.AddSlab(Geo.Tidy(Clipper.SimplifyPaths(g.Outline, 0.02)), Frame(g.Pose), -g.Thick / 2, g.Thick / 2, GhostLook);
            double T = thickness;
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                    _scene.AddSlab(outline, Frame(pose), -T / 2, T / 2, Look, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null,
                        group: part.Name.StartsWith("Floor", StringComparison.Ordinal) ? FloorGroup : 0);
            }
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));

    public override void Render(DrawingContext ctx)
    {
        if (!_painter.Draw(ctx, this, _scene, Camera3D.YUp(_yaw, _pitch), UiKit.Brush("Bg2"), _liftFloor ? 1UL << FloorGroup : 0, _drag is not null))
        {
            if (_message is { } m) PiecesView.Message(ctx, Bounds, m);
            return;
        }
        var hint = new FormattedText("Drag to turn it round", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
        ctx.DrawText(hint, new Point(10, Bounds.Height - hint.Height - 6));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } from) return;
        var at = e.GetPosition(this);
        _yaw = (_yaw - (at.X - from.X) * 0.5) % 360;
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, -15, 85);
        _drag = at;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
        // Drawn again in full, after the quicker drawing while turning.
        InvalidateVisual();
    }
}
