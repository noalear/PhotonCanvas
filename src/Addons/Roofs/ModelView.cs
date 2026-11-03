using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// The roof or tower put together, drawn in 3D from above and to one side: every piece at its
/// thickness where it sits, the etched face showing its shingles or stonework. Drag to turn it round;
/// the wheel lifts the roof off (pulls the pieces apart upwards) to show inside.
/// </summary>
public sealed class ModelView : Control
{
    /// <summary>One copy of a piece in the model, and how far up it goes when the pieces are lifted apart (0–1).</summary>
    sealed record Slab(PathsD Outline, Pose Pose, double T, PathsD? Etched, List<PathD>? Lines, double Lift);

    readonly List<Slab> _slabs = [];
    readonly Scene3D _scene = new();
    readonly Model3DPainter _painter = new();
    double _yaw = 35, _pitch = 28, _apart, _built = -1;
    Point? _drag;
    string? _message;

    static readonly Color Wood = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly Vec3 Light = new Vec3(-0.35, -0.45, 0.85).Unit;

    static readonly SlabStyle Look = new(
        (n, side) =>
        {
            double lit = Math.Max(0, n.Dot(Light));
            return Scene3D.Shade(Wood, 0.45 + 0.55 * (side ? (0.25 + 0.75 * lit) * 0.85 : 0.45 + 0.55 * lit));
        },
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.6),
        Color.FromRgb(0x8C, 0x61, 0x37),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.3, 0.9));

    public ModelView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(RoofKit? kit, string? message = null)
    {
        _slabs.Clear();
        _message = message;
        if (kit is not null)
        {
            double T = kit.ThicknessMm;
            double lo = double.MaxValue, hi = double.MinValue;
            var centres = new List<double>();
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.12));
                foreach (var pose in part.Poses)
                {
                    var all = outline.SelectMany(l => l).SelectMany(p => new[] { pose.At(p.x, p.y, 0), pose.At(p.x, p.y, T) }).ToList();
                    if (all.Count == 0) continue;
                    // The middle of its box (not the average of its points: a ring with a gap would lean away from the gap).
                    double z0 = all.Min(q => q.Z), z1 = all.Max(q => q.Z);
                    lo = Math.Min(lo, z0); hi = Math.Max(hi, z1);
                    centres.Add((z0 + z1) / 2);
                    _slabs.Add(new Slab(outline, pose, T, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, 0));
                }
            }
            // Pieces are pulled apart upwards in order of height: the roof's slopes and caps come off first.
            double zSpan = Math.Max(1, hi - lo);
            for (int i = 0; i < _slabs.Count; i++) _slabs[i] = _slabs[i] with { Lift = (centres[i] - lo) / zSpan };
        }
        _built = -1;
        InvalidateVisual();
    }

    /// <summary>The model, with the pieces lifted apart as far as the wheel says.</summary>
    void Build()
    {
        if (_built == _apart) return;
        _built = _apart;
        _scene.Clear();
        double spread = _apart * 40;
        foreach (var s in _slabs)
        {
            var p = s.Pose;
            var o = p.O + new V3(0, 0, s.Lift * spread);
            var frame = new Frame3(new(o.X, o.Y, o.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));
            _scene.AddSlab(s.Outline, frame, 0, s.T, Look, s.Etched, s.Lines);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        Build();
        if (!_painter.Draw(ctx, this, _scene, Camera3D.ZUp(_yaw, _pitch), UiKit.Brush("Bg2"), draft: _drag is not null))
        {
            if (_message is { } m)
            {
                var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
                { MaxTextWidth = Math.Max(50, Bounds.Width - 40), TextAlignment = TextAlignment.Center };
                ctx.DrawText(text, new Point(20, Bounds.Height / 2 - text.Height / 2));
            }
            return;
        }
        var hint = new FormattedText("Drag to turn it round · scroll to lift the pieces apart", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"));
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
        _pitch = Math.Clamp(_pitch + (at.Y - from.Y) * 0.4, 5, 85);
        _drag = at;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _apart = Math.Clamp(_apart + e.Delta.Y * 0.15, 0, 3);
        InvalidateVisual();
        e.Handled = true;
    }
}
