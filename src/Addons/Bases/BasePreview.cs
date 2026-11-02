using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Bases;

/// <summary>
/// The bases as they'll come out, at one scale: one of each size with its layers stacked (so a stepped
/// edge shows), and the movement tray with bases sitting in its pockets.
/// </summary>
public sealed class BasePreview : Control
{
    sealed record Placed(double X, double Y, StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines, IBrush Fill);

    readonly List<Placed> _placed = [];
    readonly List<(Point At, string Text)> _labels = [];
    double _x0, _y0, _w, _h;
    string? _message;

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush Lower = new SolidColorBrush(Color.FromRgb(0xC4, 0xA2, 0x74));
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));

    public BasePreview() => ClipToBounds = true;

    /// <summary>Shows a kit (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BaseKit? kit, string? message = null)
    {
        _placed.Clear();
        _labels.Clear();
        _message = message;
        _x0 = _y0 = _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            // Placed at x, y (screen, y down) for the part's y = 0; parts are y up.
            void Put(Part p, double x, double y, IBrush fill)
            {
                var b = Geo.Bounds(p.Cut);
                if (b.right <= b.left) return;
                _placed.Add(new Placed(x, y, Stream(p.Cut, true), p.Etched.Count > 0 ? Stream(p.Etched, true) : null, p.Lines.Count > 0 ? Stream(p.Lines, false) : null, fill));
                minX = Math.Min(minX, x + b.left); maxX = Math.Max(maxX, x + b.right);
                minY = Math.Min(minY, y - b.bottom); maxY = Math.Max(maxY, y - b.top);
            }

            // One base of each size, in a row along their bottoms, as wide as about the tray.
            double x = 0, y = 0, rowH = 0;
            double rowW = Math.Max(120, kit.Tray is { } tk && tk.Frames.Count > 0 ? PartWidth(tk.Frames[^1]) : 0);
            foreach (var stack in kit.Stacks)
            {
                var b = Geo.Bounds(stack.Layers[0].Cut);
                double w = b.right - b.left, h = b.bottom - b.top;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + 6; rowH = 0; }
                for (int i = 0; i < stack.Layers.Count; i++) Put(stack.Layers[i], x - b.left, y + b.bottom, i == stack.Layers.Count - 1 ? Wood : Lower);
                if (stack.Count > 1) _labels.Add((new Point(x + w / 2, y + h + 1), $"× {stack.Count}"));
                x += w + 6;
                rowH = Math.Max(rowH, h + 5);
            }
            double below = _placed.Count > 0 ? maxY + 4 : 0;

            // The tray put together, with bases in it.
            if (kit.Tray is { } tray)
            {
                var top = tray.Frames[^1];
                var tb = Geo.Bounds(top.Cut);
                double ty = below + tb.bottom;
                if (tray.Bottom is { } bottom) Put(bottom, -tb.left, ty, Lower);
                Put(top, -tb.left, ty, Wood);
                var sb = Geo.Bounds(tray.Base);
                var stack = kit.Stacks.FirstOrDefault(s => Same(Geo.Bounds(s.Layers[0].Cut), sb));
                foreach (var c in tray.Spots)
                {
                    if (stack is null) Put(new Part("", tray.Base, [], [], 1), c.x - tb.left, ty - c.y, Lower);
                    else for (int i = 0; i < stack.Layers.Count; i++) Put(stack.Layers[i], c.x - tb.left, ty - c.y, i == stack.Layers.Count - 1 ? Wood : Lower);
                }
            }
            if (_placed.Count > 0) { _x0 = minX; _y0 = minY; _w = maxX - minX; _h = maxY - minY + 5; }
        }
        InvalidateVisual();
    }

    static double PartWidth(Part p) { var b = Geo.Bounds(p.Cut); return b.right - b.left; }

    static bool Same(RectD a, RectD b) => Math.Abs(a.right - a.left - (b.right - b.left)) < 1e-6 && Math.Abs(a.bottom - a.top - (b.bottom - b.top)) < 1e-6;

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
        double ox = (Bounds.Width - _w * k) / 2 - _x0 * k, oy = (Bounds.Height - _h * k) / 2 - _y0 * k;
        var edge = new Pen(EdgeBrush, 1.2 / k);
        var score = new Pen(ScoreBrush, Math.Max(0.06, 0.9 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(p.Fill, null, p.Cut);
                if (p.Etched is not null) ctx.DrawGeometry(Burnt, null, p.Etched);
                if (p.Lines is not null) ctx.DrawGeometry(null, score, p.Lines);
                ctx.DrawGeometry(null, edge, p.Cut);
            }
        }
        foreach (var (at, label) in _labels)
        {
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, UiKit.Brush("TextMuted"));
            ctx.DrawText(text, new Point(ox + at.X * k - text.Width / 2, oy + at.Y * k));
        }
    }
}
