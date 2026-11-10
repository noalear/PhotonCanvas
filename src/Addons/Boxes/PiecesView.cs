using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>The box's pieces as they'll be cut, one of each, at one scale, with how many of each; the living hinge slits in red.</summary>
public sealed class PiecesView : Control
{
    sealed record Placed(string Label, double X, double Y, double LabelX, double LabelY, double W, StreamGeometry Cut, StreamGeometry? Slits);

    readonly List<Placed> _placed = [];
    double _w, _h;
    string? _message;

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush SlitBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    public PiecesView() => ClipToBounds = true;

    public void Show(BoxKit? kit, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            var sizes = kit.Parts.Select(p => Geo.Bounds(p.Cut)).ToList();
            double area = sizes.Sum(b => (b.right - b.left + 4) * (b.bottom - b.top + 4));
            double rowW = Math.Max(sizes.Max(b => b.right - b.left), Math.Sqrt(area) * 1.5), gap = 4, x = 0, y = 0, rowH = 0;
            for (int i = 0; i < kit.Parts.Count; i++)
            {
                var p = kit.Parts[i];
                var b = sizes[i];
                double w = b.right - b.left, h = b.bottom - b.top;
                if (w <= 0) continue;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + gap + 4; rowH = 0; }
                _placed.Add(new Placed(p.Name + (p.Copies > 1 ? $" ×{p.Copies}" : ""), x - b.left, y + b.bottom, x, y + h, w, Stream(p.Cut, true), p.Slits.Count > 0 ? Stream(p.Slits, false) : null));
                x += w + gap;
                rowH = Math.Max(rowH, h);
                _w = Math.Max(_w, x - gap);
                _h = Math.Max(_h, y + rowH);
            }
        }
        InvalidateVisual();
    }

    /// <summary>The paths with y flipped (y up in the pieces, down on screen).</summary>
    static StreamGeometry Stream(IEnumerable<PathD> paths, bool closed)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var p in paths)
        {
            if (p.Count < 2) continue;
            c.BeginFigure(new Point(p[0].x, -p[0].y), closed);
            for (int i = 1; i < p.Count; i++) c.LineTo(new Point(p[i].x, -p[i].y));
            c.EndFigure(closed);
        }
        return g;
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_placed.Count == 0 || _w <= 0)
        {
            if (_message is { } m) Model3DMessage(ctx, m);
            return;
        }
        double k = Math.Min((Bounds.Width - 24) / _w, (Bounds.Height - 40) / _h);
        if (!(k > 0)) return;
        double ox = (Bounds.Width - _w * k) / 2, oy = (Bounds.Height - _h * k) / 2 + 6;
        var edge = new Pen(EdgeBrush, 1.2 / k);
        var slit = new Pen(SlitBrush, Math.Max(0.05, 0.9 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(Wood, edge, p.Cut);
                if (p.Slits is not null) ctx.DrawGeometry(null, slit, p.Slits);
            }
            var text = new FormattedText(p.Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, UiKit.Brush("TextMuted"))
            { MaxTextWidth = Math.Max(30, p.W * k + 20), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(text, new Point(ox + p.LabelX * k, oy + p.LabelY * k + 1));
        }
    }

    void Model3DMessage(DrawingContext ctx, string m) => PhotonCanvas.App.View3D.Model3DView.DrawMessage(ctx, Bounds, m);
}
