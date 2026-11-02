using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// The pieces as they'll come out, at one scale: the wall's face on top with its texture, and under it
/// one of each other piece (the door, the walkway, the accessories, a layer of each column).
/// </summary>
public sealed class WallPreview : Control
{
    sealed record Placed(Part Part, double X, double Y, StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines);

    readonly List<Placed> _placed = [];
    double _w, _h;
    string? _message;

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly Pen Edge = new(new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E)), 1);
    static readonly Pen Score = new(new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28)), 0.8);

    public WallPreview() => ClipToBounds = true;

    /// <summary>Shows a kit (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(WallKit? kit, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            // The front on its own row, then one of each other piece in rows as wide as the front.
            var front = kit.Parts[0];
            var fb = Geo.Bounds(front.Cut);
            double rowW = Math.Max(60, fb.right - fb.left), gap = 4, x = 0, y = 0, rowH = 0;
            void Add(Part p)
            {
                var b = Geo.Bounds(p.Cut);
                double w = b.right - b.left, h = b.bottom - b.top;
                if (w <= 0) return;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + gap; rowH = 0; }
                _placed.Add(new Placed(p, x - b.left, y + b.bottom, Stream(p.Cut, true), p.Etched.Count > 0 ? Stream(p.Etched, true) : null, p.Lines.Count > 0 ? Stream(p.Lines, false) : null));
                x += w + gap;
                rowH = Math.Max(rowH, h);
                _w = Math.Max(_w, x - gap);
                _h = Math.Max(_h, y + rowH);
            }
            Add(front);
            x = 0; y = _h + gap * 2; rowH = 0;
            foreach (var p in kit.Parts.Skip(1).Where(p => p.Name != "Wall back")) Add(p);
        }
        InvalidateVisual();
    }

    /// <summary>The paths with y flipped (y up in the kit, down on screen).</summary>
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
            if (_message is { } m)
            {
                var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
                { MaxTextWidth = Math.Max(50, Bounds.Width - 40), TextAlignment = TextAlignment.Center };
                ctx.DrawText(text, new Point(20, Bounds.Height / 2 - text.Height / 2));
            }
            return;
        }
        double k = Math.Min((Bounds.Width - 24) / _w, (Bounds.Height - 24) / _h);
        if (!(k > 0)) return;
        double ox = (Bounds.Width - _w * k) / 2, oy = (Bounds.Height - _h * k) / 2;
        var edge = new Pen(Edge.Brush, 1.2 / k);
        var score = new Pen(Score.Brush, Math.Max(0.06, 0.9 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(Wood, edge, p.Cut);
                if (p.Etched is not null) ctx.DrawGeometry(Burnt, null, p.Etched);
                if (p.Lines is not null) ctx.DrawGeometry(null, score, p.Lines);
            }
        }
    }
}
