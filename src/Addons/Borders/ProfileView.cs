using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// The border cut across, from its outer edge (left) to its inner edge (right): each band's profile,
/// and how deep its ornament is carved below it. The chosen band is marked; clicking picks a band.
/// </summary>
public sealed class ProfileView : Control
{
    BorderDesign? _design;
    int _selected = -1;

    /// <summary>A band was clicked (its index).</summary>
    public event Action<int>? Picked;

    public ProfileView()
    {
        Height = 96;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void Show(BorderDesign? design, int selected)
    {
        _design = design;
        _selected = selected;
        InvalidateVisual();
    }

    (double K, double Ky, double X0, double Y0)? Scale()
    {
        if (_design is not { } d || d.Bands.Count == 0) return null;
        double w = d.WidthMm, top = d.TopMm, low = Math.Min(0, d.LowestMm);
        if (!(w > 0) || !(top - low > 0)) return null;
        double k = (Bounds.Width - 16) / w;
        // Heights drawn larger than lengths, so a few mm of depth across 40 mm still shows.
        double ky = Math.Min((Bounds.Height - 30) / (top - low), k * 4);
        return (k, ky, 8, Bounds.Height - 18 + low * ky);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Scale() is not { } s || _design is null) return;
        double t = (e.GetPosition(this).X - s.X0) / s.K, at = 0;
        for (int i = 0; i < _design.Bands.Count; i++)
        {
            at += _design.Bands[i].WidthMm;
            if (t <= at || i == _design.Bands.Count - 1) { Picked?.Invoke(i); return; }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (Scale() is not { } s || _design is null) return;
        var (k, ky, x0, y0) = s;
        double at = 0;
        var wood = new SolidColorBrush(Color.FromRgb(0xD9, 0xB9, 0x8C));
        var carved = new SolidColorBrush(Color.FromRgb(0x9C, 0x6B, 0x3E));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x3D, 0x22)), 1.2);
        for (int i = 0; i < _design.Bands.Count; i++)
        {
            var b = _design.Bands[i];
            double w = Math.Max(0, b.WidthMm);
            if (i == _selected)
                ctx.FillRectangle(UiKit.Brush("AccentSoft"), new Rect(x0 + at * k, 0, w * k, Bounds.Height));
            // The profile as a filled shape down to the bottom, and the carving below its surface.
            var top = new StreamGeometry();
            var floor = new StreamGeometry();
            const int Steps = 40;
            using (var c = top.Open())
            {
                c.BeginFigure(new Point(x0 + at * k, y0), true);
                for (int j = 0; j <= Steps; j++)
                {
                    double tau = j / (double)Steps, t = b.Flip ? 1 - tau : tau;
                    double h = b.TopMm - Math.Max(0, b.DropMm) * (1 - Profiles.Height(b.Profile, t, b.Count));
                    c.LineTo(new Point(x0 + (at + tau * w) * k, y0 - h * ky));
                }
                c.LineTo(new Point(x0 + (at + w) * k, y0));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(wood, null, top);
            if (b.Ornament != Ornament.None && b.CarveMm > 0)
            {
                using (var c = floor.Open())
                {
                    c.BeginFigure(new Point(x0 + at * k, y0), true);
                    for (int j = 0; j <= Steps; j++)
                    {
                        double tau = j / (double)Steps, t = b.Flip ? 1 - tau : tau;
                        double h = b.TopMm - Math.Max(0, b.DropMm) * (1 - Profiles.Height(b.Profile, t, b.Count)) - b.CarveMm;
                        c.LineTo(new Point(x0 + (at + tau * w) * k, y0 - h * ky));
                    }
                    c.LineTo(new Point(x0 + (at + w) * k, y0));
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(carved, null, floor);
            }
            ctx.DrawGeometry(null, edge, top);
            at += w;
        }
        var muted = UiKit.Brush("TextMuted");
        var outer = new FormattedText("outer edge", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, muted);
        var inner = new FormattedText("inner edge", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, muted);
        ctx.DrawText(outer, new Point(6, Bounds.Height - 15));
        ctx.DrawText(inner, new Point(Bounds.Width - inner.Width - 6, Bounds.Height - 15));
    }
}
