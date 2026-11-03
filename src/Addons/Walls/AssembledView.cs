using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// One wall put together, with a column at each end, drawn in 3D from in front, above and to one side:
/// every piece at its thickness where it sits, the etched faces showing their texture. Drag to turn it round.
/// </summary>
public sealed class AssembledView : Control
{
    readonly Scene3D _scene = new();
    readonly Model3DPainter _painter = new();
    double _yaw = 28, _pitch = 22;
    Point? _drag;
    string? _message;

    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.6),
        Color.FromRgb(0x8C, 0x61, 0x37),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.3, 0.9));

    public AssembledView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(WallKit? kit, double thickness, string? message = null)
    {
        _scene.Clear();
        _message = message;
        if (kit is not null)
        {
            double T = thickness;
            foreach (var part in kit.Parts)
            {
                if (part.Poses.Count == 0) continue;
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                    _scene.AddSlab(outline, Frame(pose), -T / 2, T / 2, Look, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null);
            }
            if (_scene.IsEmpty) _message ??= "Nothing to show.";
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));

    public override void Render(DrawingContext ctx)
    {
        if (!_painter.Draw(ctx, this, _scene, Camera3D.YUp(_yaw, _pitch), UiKit.Brush("Bg2"), draft: _drag is not null))
        {
            if (_message is { } m)
            {
                var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
                { MaxTextWidth = Math.Max(50, Bounds.Width - 40), TextAlignment = TextAlignment.Center };
                ctx.DrawText(text, new Point(20, Bounds.Height / 2 - text.Height / 2));
            }
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
