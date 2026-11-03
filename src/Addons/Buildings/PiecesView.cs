using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// The pieces of one sheet as they'll be cut, one of each, at one scale, with how many of each there
/// are; the etching dark and the lines scored.
/// </summary>
public sealed class PiecesView : Control
{
    sealed record Placed(Piece Piece, double X, double Y, double LabelX, double LabelY, double W, StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines);

    readonly List<Placed> _placed = [];
    double _w, _h;
    string? _message;

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));

    public PiecesView() => ClipToBounds = true;

    /// <summary>Shows some pieces (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(IReadOnlyList<Piece>? pieces, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        if (pieces is { Count: > 0 })
        {
            // Biggest first, in rows about as wide as the biggest piece or as the pieces' total area suggests.
            var list = pieces.Select(p => (Piece: p, Box: Geo.Bounds(p.Cut))).Where(x => x.Box.right > x.Box.left)
                             .OrderByDescending(x => (x.Box.bottom - x.Box.top) * 1000 + (x.Box.right - x.Box.left)).ToList();
            double area = list.Sum(x => (x.Box.right - x.Box.left + 3) * (x.Box.bottom - x.Box.top + 6));
            double rowW = Math.Max(list.Max(x => x.Box.right - x.Box.left), Math.Sqrt(area) * 1.5), gap = 3, x = 0, y = 0, rowH = 0;
            foreach (var (p, b) in list)
            {
                double w = b.right - b.left, h = b.bottom - b.top;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + gap + 5; rowH = 0; }
                _placed.Add(new Placed(p, x - b.left, y + b.bottom, x, y + h, w, Stream(p.Cut, true),
                    p.Etched.Count > 0 ? Stream(p.Etched, true) : null, p.Lines.Count > 0 ? Stream(p.Lines, false) : null));
                x += w + gap;
                rowH = Math.Max(rowH, h);
                _w = Math.Max(_w, x - gap);
                _h = Math.Max(_h, y + rowH);
            }
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
            if (_message is { } m) Model3DView.DrawMessage(ctx, Bounds, m);
            return;
        }
        double k = Math.Min((Bounds.Width - 24) / _w, (Bounds.Height - 40) / _h);
        if (!(k > 0)) return;
        double ox = (Bounds.Width - _w * k) / 2, oy = (Bounds.Height - _h * k) / 2 + 6;
        var edge = new Pen(EdgeBrush, 1.2 / k);
        var score = new Pen(ScoreBrush, Math.Max(0.05, 0.8 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(Wood, edge, p.Cut);
                if (p.Etched is not null) ctx.DrawGeometry(Burnt, null, p.Etched);
                if (p.Lines is not null) ctx.DrawGeometry(null, score, p.Lines);
            }
            // Its name and how many, under it, where there's room.
            if (p.W * k < 24) continue;
            var label = $"{p.Piece.Name}{(p.Piece.Copies > 1 ? $" ×{p.Piece.Copies}" : "")}";
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, UiKit.Brush("TextMuted"))
            { MaxTextWidth = Math.Max(24, p.W * k + 2), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(text, new Point(ox + p.LabelX * k, oy + p.LabelY * k + 1));
        }
    }
}
