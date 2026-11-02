using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// The ruins at one scale: as built (the wall's face, then each pile, base and pillar from above with its
/// layers stacked), or every piece to cut, one of each.
/// </summary>
public sealed class RuinPreview : Control
{
    sealed record Drawn(StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines, double Shade);
    sealed record Placed(double X, double Y, List<Drawn> Layers, string? Caption);

    readonly List<Placed> _placed = [];
    double _w, _h;
    string? _message;

    static readonly Color WoodColour = Color.FromRgb(0xDD, 0xBE, 0x92);
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28));

    public RuinPreview() => ClipToBounds = true;

    /// <summary>Show the ruins as built (true) or the pieces to cut (false).</summary>
    public bool Built { get; set; } = true;

    /// <summary>Shows a kit (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(RuinKit? kit, string? message = null)
    {
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        if (kit is not null && kit.Parts.Count > 0)
        {
            var groups = Built && kit.Views.Count > 0
                ? kit.Views.Select(v => (v.Layers, (string?)v.Name)).ToList()
                : [.. kit.Parts.Where(p => p.Name != "Broken wall back").Select(p => (new List<Part> { p }, (string?)null))];
            // Rows about as wide as the widest thing (and never narrower than 60 mm).
            double rowW = Math.Max(60, groups.Max(g => WidthOf(g.Item1)));
            if (groups.Count > 3) rowW = Math.Max(rowW, Math.Sqrt(groups.Sum(g => WidthOf(g.Item1) * HeightOf(g.Item1))) * 1.3);
            double gap = Math.Max(4, rowW * 0.04), x = 0, y = 0, rowH = 0;
            foreach (var (layers, caption) in groups)
            {
                double w = WidthOf(layers), h = HeightOf(layers);
                if (w <= 0) continue;
                if (x > 0 && x + w > rowW) { x = 0; y += rowH + gap; rowH = 0; }
                var box = BoxOf(layers);
                var drawn = layers.Select((p, i) => new Drawn(Stream(p.Cut, true), p.Etched.Count > 0 ? Stream(p.Etched, true) : null, p.Lines.Count > 0 ? Stream(p.Lines, false) : null,
                    layers.Count <= 1 ? 0 : (double)i / (layers.Count - 1))).ToList();
                _placed.Add(new Placed(x - box.left, y + box.bottom, drawn, caption));
                x += w + gap;
                rowH = Math.Max(rowH, h);
                _w = Math.Max(_w, x - gap);
                _h = Math.Max(_h, y + rowH);
            }
        }
        InvalidateVisual();
    }

    static RectD BoxOf(List<Part> layers) => Geo.Bounds(new PathsD(layers.SelectMany(p => p.Cut)));
    static double WidthOf(List<Part> layers) { var b = BoxOf(layers); return b.right - b.left; }
    static double HeightOf(List<Part> layers) { var b = BoxOf(layers); return b.bottom - b.top; }

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

    /// <summary>Higher layers of a stack a little lighter, so the steps show.</summary>
    static IBrush Wood(double shade)
    {
        byte L(byte v) => (byte)Math.Min(255, v + (255 - v) * 0.45 * shade);
        return new SolidColorBrush(Color.FromRgb(L(WoodColour.R), L(WoodColour.G), L(WoodColour.B)));
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
        var edge = new Pen(EdgeBrush, 1.2 / k);
        var score = new Pen(ScoreBrush, Math.Max(0.06, 0.9 / k));
        foreach (var p in _placed)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
            {
                foreach (var l in p.Layers)
                {
                    ctx.DrawGeometry(Wood(l.Shade), edge, l.Cut);
                    if (l.Etched is not null) ctx.DrawGeometry(Burnt, null, l.Etched);
                    if (l.Lines is not null) ctx.DrawGeometry(null, score, l.Lines);
                }
            }
        }
    }
}
