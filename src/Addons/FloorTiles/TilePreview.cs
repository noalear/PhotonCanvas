using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>
/// The tiles as they'll come out, at one scale: a board put together (so you see the joints lock) or the
/// loose tiles side by side, and under them a clip and a base layer if there are any.
/// </summary>
public sealed class TilePreview : Control
{
    sealed record Placed(double X, double Y, StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines);

    readonly List<Placed> _placed = [];
    double _x0, _y0, _w, _h;
    string? _message;

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));

    public TilePreview() => ClipToBounds = true;

    /// <summary>Shows a kit (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(TileKit? kit, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _x0 = _y0 = _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            // Placed at x, y with y down on screen; kit parts are y up.
            void Put(Part p, double x, double y)
            {
                var b = Geo.Bounds(p.Cut);
                if (b.right <= b.left) return;
                _placed.Add(new Placed(x, y, Stream(p.Cut, true), p.Etched.Count > 0 ? Stream(p.Etched, true) : null, p.Lines.Count > 0 ? Stream(p.Lines, false) : null));
                minX = Math.Min(minX, x + b.left); maxX = Math.Max(maxX, x + b.right);
                minY = Math.Min(minY, y - b.bottom); maxY = Math.Max(maxY, y - b.top);
            }

            double below;
            if (kit.Board.Count > 0)
            {
                double top = kit.Board.Max(b => b.Y + b.H);
                // The board put together, the way it lies on the canvas (its top row at the top).
                foreach (var spot in kit.Board) Put(spot.Tile, spot.X, top - spot.Y);
                below = maxY;
            }
            else
            {
                // Loose tiles in rows about as wide as they'd make a square.
                var tiles = kit.Tiles.ToList();
                double area = tiles.Sum(t => { var b = Geo.Bounds(t.Cut); return (b.right - b.left + 4) * (b.bottom - b.top + 4); });
                double rowW = Math.Max(60, Math.Sqrt(area) * 1.3), x = 0, y = 0, rowH = 0;
                foreach (var t in tiles)
                {
                    var b = Geo.Bounds(t.Cut);
                    double w = b.right - b.left, h = b.bottom - b.top;
                    if (x > 0 && x + w > rowW) { x = 0; y += rowH + 4; rowH = 0; }
                    Put(t, x - b.left, y + b.bottom);
                    x += w + 4;
                    rowH = Math.Max(rowH, h);
                }
                below = maxY;
            }
            // One of each other piece underneath: a base layer, a clip.
            double px = minX == double.MaxValue ? 0 : minX;
            foreach (var extra in new[] { kit.Parts.FirstOrDefault(p => p.Name.EndsWith(TileKitMaker.BaseSuffix, StringComparison.Ordinal)), kit.Parts.FirstOrDefault(p => p.Name == TileKitMaker.ClipName) })
            {
                if (extra is null) continue;
                var b = Geo.Bounds(extra.Cut);
                Put(extra, px - b.left, below + 6 + b.bottom);
                px += b.right - b.left + 6;
            }
            if (_placed.Count > 0) { _x0 = minX; _y0 = minY; _w = maxX - minX; _h = maxY - minY; }
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
        double ox = (Bounds.Width - _w * k) / 2 - _x0 * k, oy = (Bounds.Height - _h * k) / 2 - _y0 * k;
        var edge = new Pen(EdgeBrush, 1.2 / k);
        var score = new Pen(ScoreBrush, Math.Max(0.06, 0.9 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(Wood, null, p.Cut);
                if (p.Etched is not null) ctx.DrawGeometry(Burnt, null, p.Etched);
                if (p.Lines is not null) ctx.DrawGeometry(null, score, p.Lines);
                ctx.DrawGeometry(null, edge, p.Cut);
            }
        }
    }
}
