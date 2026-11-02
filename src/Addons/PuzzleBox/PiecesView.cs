using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>
/// The pieces of a box as they'll be cut, one of each, at one scale, with how many of each there
/// are and the etching dark.
/// </summary>
public sealed class PiecesView : Control
{
    sealed record Placed(Part Part, int Count, double X, double Y, double LabelX, double LabelY, double W, StreamGeometry Cut, StreamGeometry? Etched);

    readonly List<Placed> _placed = [];
    double _w, _h;
    string? _message;

    internal static readonly Color WoodColour = Color.FromRgb(0xDD, 0xBE, 0x92);
    internal static readonly IBrush Wood = new SolidColorBrush(WoodColour);
    internal static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    internal static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));

    public PiecesView() => ClipToBounds = true;

    /// <summary>Shows a box's pieces (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BoxKit? kit, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            // Rows about as wide as the biggest piece, or as the pieces' total area suggests.
            var sizes = kit.Parts.Select(p => Geo.Bounds(p.Cut)).ToList();
            double area = sizes.Sum(b => (b.right - b.left + 4) * (b.bottom - b.top + 8));
            double rowW = Math.Max(sizes.Max(b => b.right - b.left), Math.Sqrt(area) * 1.5), gap = 4, x = 0, y = 0, rowH = 0;
            for (int i = 0; i < kit.Parts.Count; i++)
            {
                var p = kit.Parts[i];
                var b = sizes[i];
                double w = b.right - b.left, h = b.bottom - b.top;
                if (w <= 0) continue;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + gap + 6; rowH = 0; }
                _placed.Add(new Placed(p, p.Copies * Math.Max(1, kit.Design.Spec.Count), x - b.left, y + b.bottom, x, y + h, w,
                    Stream(p.Cut), p.Etched.Count > 0 ? Stream(p.Etched) : null));
                x += w + gap;
                rowH = Math.Max(rowH, h);
                _w = Math.Max(_w, x - gap);
                _h = Math.Max(_h, y + rowH);
            }
        }
        InvalidateVisual();
    }

    /// <summary>The areas with y flipped (y up in the kit, down on screen).</summary>
    internal static StreamGeometry Stream(IEnumerable<PathD> paths)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var p in paths)
        {
            if (p.Count < 3) continue;
            c.BeginFigure(new Point(p[0].x, -p[0].y), true);
            for (int i = 1; i < p.Count; i++) c.LineTo(new Point(p[i].x, -p[i].y));
            c.EndFigure(true);
        }
        return g;
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_placed.Count == 0 || _w <= 0)
        {
            if (_message is { } m) Message(ctx, Bounds, m);
            return;
        }
        double k = Math.Min((Bounds.Width - 24) / _w, (Bounds.Height - 40) / (_h + 6));
        if (!(k > 0)) return;
        double ox = (Bounds.Width - _w * k) / 2, oy = (Bounds.Height - _h * k) / 2 + 2;
        var edge = new Pen(EdgeBrush, 1.1 / k);
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                ctx.DrawGeometry(Wood, edge, p.Cut);
                if (p.Etched is not null) ctx.DrawGeometry(Burnt, null, p.Etched);
            }
            // Its name and how many, under it.
            var label = $"{p.Part.Name}{(p.Count > 1 ? $" ×{p.Count}" : "")}";
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10.5, UiKit.Brush("TextMuted"))
            { MaxTextWidth = Math.Max(30, p.W * k + 16), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(text, new Point(ox + p.LabelX * k, oy + p.LabelY * k + 1));
        }
    }

    internal static void Message(DrawingContext ctx, Rect bounds, string m)
    {
        var text = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted"))
        { MaxTextWidth = Math.Max(50, bounds.Width - 40), TextAlignment = TextAlignment.Center };
        ctx.DrawText(text, new Point(20, bounds.Height / 2 - text.Height / 2));
    }
}
