using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>
/// The picture as pieces: each piece in its wood's colour with its number, on the board (its pocket
/// shaded) or the sheet the holes are cut in. Hover a piece to see what it is; scroll to zoom, drag
/// with the right (or middle) button to move, double-click to see it all.
/// </summary>
public sealed class InlayPreview : Control
{
    InlayDesign _design = InlayDesign.Empty;
    InlayOptions _options = new();
    IReadOnlyList<string> _woodNames = [];
    List<(Piece Piece, StreamGeometry Geo, IBrush Fill)> _shown = [];
    StreamGeometry? _sheet;
    List<StreamGeometry> _islands = [];
    Rect2 _area = Rect2.Empty;
    Piece? _hover;
    double _zoom = 1;
    Vec2 _centre;
    bool _fitted;
    Point? _panFrom;

    public InlayPreview()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Show these pieces, made with these settings (the woods' names as typed).</summary>
    public void Show(InlayDesign design, InlayOptions options, IReadOnlyList<string> woodNames)
    {
        bool newDesign = !ReferenceEquals(design, _design);
        _design = design;
        _options = options;
        _woodNames = woodNames;
        _shown = [.. design.Pieces.Select(p => (p, ToStream(p.Area), (IBrush)new SolidColorBrush(WoodColour(design.Woods[p.Wood].Color))))];
        _sheet = null;
        _islands = [];
        if (!design.IsEmpty)
        {
            var b = design.Bounds;
            if (options.Into == InlayInto.Holes)
            {
                var (main, islands) = InlayMaker.Sheet(design, options);
                _sheet = ToStream(main);
                _islands = [.. islands.Select(i => ToStream(i.Area))];
                b = InlayMaker.SheetRect(design, options);
            }
            _area = b.Inflate(Math.Max(b.Width, b.Height) * 0.04 + 2);
        }
        else _area = Rect2.Empty;
        if (newDesign) _fitted = false;
        if (_hover is not null && !design.Pieces.Contains(_hover)) _hover = null;
        InvalidateVisual();
    }

    /// <summary>A layer colour made into something like a wood: kept, but softened so the numbers read on it.</summary>
    static Color WoodColour(uint rgb)
    {
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        return Color.FromRgb((byte)(r * 0.75 + 255 * 0.25), (byte)(g * 0.75 + 255 * 0.25), (byte)(b * 0.75 + 255 * 0.25));
    }

    double Scale => Fit() * _zoom;

    double Fit()
    {
        if (_area.IsEmpty || Bounds.Width < 10 || Bounds.Height < 10) return 1;
        return Math.Min((Bounds.Width - 16) / Math.Max(_area.Width, 1e-3), (Bounds.Height - 16) / Math.Max(_area.Height, 1e-3));
    }

    Matrix WorldToScreen()
    {
        double k = Scale;
        return Matrix.CreateTranslation(-_centre.X, -_centre.Y) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
    }

    Vec2 ToWorld(Point p)
    {
        double k = Scale;
        return new Vec2((p.X - Bounds.Width / 2) / k + _centre.X, (p.Y - Bounds.Height / 2) / k + _centre.Y);
    }

    public void SeeAll()
    {
        if (_area.IsEmpty) return;
        _centre = _area.Center;
        _zoom = 1;
        _fitted = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        try { Paint(ctx); }
        catch (Exception) { /* a drawing problem must never take the program down */ }
    }

    void Paint(DrawingContext ctx)
    {
        var text = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_design.IsEmpty)
        {
            var f = new FormattedText("Select closed shapes on the canvas to see them as pieces here. Each layer is one wood.", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 12, new SolidColorBrush(text)) { MaxTextWidth = Math.Max(50, Bounds.Width - 32) };
            ctx.DrawText(f, new Point(16, 16));
            return;
        }
        if (!_fitted) { _centre = _area.Center; _zoom = 1; _fitted = true; }
        double k = Scale;
        var board = new SolidColorBrush(Color.FromRgb(0xE9, 0xDF, 0xC9));
        var seam = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x2A, 0x22)), 1 / k);
        using (ctx.PushTransform(WorldToScreen()))
        {
            if (_options.Into == InlayInto.Holes && _sheet is not null)
            {
                ctx.DrawGeometry(board, _options.CutSheetEdge ? seam : null, _sheet);
                foreach (var i in _islands) ctx.DrawGeometry(board, seam, i);
            }
            else if (_options.Into == InlayInto.Pocket)
            {
                ctx.DrawRectangle(board, null, new Rect(_area.MinX, _area.MinY, _area.Width, _area.Height));
                ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0x5A, 0x3A, 0x1A)), null, ToStream(_design.All));
            }
            foreach (var (p, geo, fill) in _shown)
                ctx.DrawGeometry(fill, ReferenceEquals(p, _hover) ? new Pen(UiKit.Brush("Accent"), 2.5 / k) : seam, geo);
        }
        // Numbers, where there's room for them on screen.
        var ink = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
        var m = WorldToScreen();
        foreach (var (p, _, _) in _shown)
        {
            double room = p.Label.Room * k;
            if (room < 6) continue;
            var f = new FormattedText(p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), Math.Clamp(room * 0.8, 8, 16), ink);
            var c = new Point(p.Label.At.X, p.Label.At.Y).Transform(m);
            ctx.DrawText(f, new Point(c.X - f.Width / 2, c.Y - f.Height / 2));
        }
        if (_hover is { } h)
        {
            string wood = h.Wood < _woodNames.Count && !string.IsNullOrWhiteSpace(_woodNames[h.Wood]) ? _woodNames[h.Wood] : _design.Woods[h.Wood].Name;
            var b = h.Bounds;
            var f = new FormattedText($"Piece {h.Number} · {wood} · {b.Width:0.#} × {b.Height:0.#} mm", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 11, new SolidColorBrush(text));
            ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(4, Bounds.Height - f.Height - 10, f.Width + 8, f.Height + 6));
            ctx.DrawText(f, new Point(8, Bounds.Height - f.Height - 7));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            double k = Scale;
            _centre -= new Vec2((p.X - from.X) / k, (p.Y - from.Y) / k);
            _panFrom = p;
            InvalidateVisual();
            return;
        }
        var w = ToWorld(p);
        var hit = _shown.LastOrDefault(s => s.Piece.Bounds.Contains(w) && Inside(s.Piece.Area, w)).Piece;
        if (!ReferenceEquals(hit, _hover)) { _hover = hit; InvalidateVisual(); }
    }

    static bool Inside(PathGeometry g, Vec2 p) =>
        g.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Count(f => Geom.Inside(f.Points, p)) % 2 == 1;

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null) { _hover = null; InvalidateVisual(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed || pt.Properties.IsMiddleButtonPressed)
        {
            _panFrom = pt.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (pt.Properties.IsLeftButtonPressed && e.ClickCount == 2) SeeAll();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panFrom is not null) { _panFrom = null; e.Pointer.Capture(null); }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var before = ToWorld(p);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 0.5, 200);
        var after = ToWorld(p);
        _centre += before - after;
        InvalidateVisual();
        e.Handled = true;
    }

    static StreamGeometry ToStream(PathGeometry pg)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(FillRule.EvenOdd);
        foreach (var sp in pg.Subpaths)
        {
            if (sp.Segs.IsEmpty) continue;
            c.BeginFigure(new Point(sp.Start.X, sp.Start.Y), sp.Closed);
            foreach (var seg in sp.Segs)
            {
                if (seg.Kind == SegKind.Line) c.LineTo(new Point(seg.To.X, seg.To.Y));
                else c.CubicBezierTo(new Point(seg.C1.X, seg.C1.Y), new Point(seg.C2.X, seg.C2.Y), new Point(seg.To.X, seg.To.Y));
            }
            c.EndFigure(sp.Closed);
        }
        return g;
    }
}
