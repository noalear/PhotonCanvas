using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.Stencil;

/// <summary>
/// The stencil as it will come out: the sheet light, the letters (what falls out) dark, the bridges
/// in the sheet's colour edged in the accent colour, middles that would still fall out in red.
/// Click a letter to add a bridge there, a bridge to take it away; scroll to zoom, drag with the
/// right (or middle) button to move, double-click to see it all.
/// </summary>
public sealed class StencilPreview : Control
{
    StencilSheet? _sheet;
    StreamGeometry? _letters, _edge;
    IReadOnlyList<Bridge> _bridges = [];
    IReadOnlyList<Middle> _loose = [];
    HashSet<Bridge> _long = [];
    Bridge? _hover;
    double _zoom = 1;
    Vec2 _centre;
    bool _fitted;
    Point? _panFrom;

    /// <summary>Clicked at this point (world mm): the window adds or removes a bridge there.</summary>
    public event Action<Vec2>? Clicked;

    public StencilPreview()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void Show(StencilSheet? sheet)
    {
        if (!ReferenceEquals(sheet, _sheet))
        {
            _sheet = sheet;
            _letters = sheet is null ? null : ToStream(sheet.Letters);
            _edge = sheet?.Edge is { } e ? ToStream(e) : null;
            _fitted = false;
        }
        InvalidateVisual();
    }

    public void Show(IReadOnlyList<Bridge> bridges, BridgeCheck? check)
    {
        _bridges = bridges;
        _loose = check?.Loose ?? [];
        _long = [.. check?.Long ?? []];
        if (_hover is not null && !_bridges.Contains(_hover)) _hover = null;
        InvalidateVisual();
    }

    /// <summary>The bridge under the pointer, if any.</summary>
    public Bridge? Hovered => _hover;

    double Scale => Fit() * _zoom;

    double Fit()
    {
        if (_sheet is null || Bounds.Width < 10 || Bounds.Height < 10) return 1;
        var b = Shown(_sheet);
        return Math.Min((Bounds.Width - 24) / Math.Max(b.Width, 1e-3), (Bounds.Height - 24) / Math.Max(b.Height, 1e-3));
    }

    /// <summary>The part of the sheet drawn: the edge, or a margin round the letters.</summary>
    static Rect2 Shown(StencilSheet sheet) =>
        sheet.Edge is not null ? sheet.Bounds : sheet.Bounds.Inflate(Math.Max(sheet.Bounds.Width, sheet.Bounds.Height) * 0.04 + 1);

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

    void SeeAll(bool redraw = true)
    {
        if (_sheet is null) return;
        _centre = _sheet.Bounds.Center;
        _zoom = 1;
        _fitted = true;
        if (redraw) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var text = ((ISolidColorBrush)UiKit.Brush("TextMain")).Color;
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        if (_sheet is null || _letters is null) return;
        if (!_fitted) SeeAll(redraw: false);
        double k = Scale;
        var sheetBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0xDF, 0xC9));
        var letterBrush = new SolidColorBrush(Color.FromArgb(235, 0x2B, 0x2B, 0x30));
        var accent = UiKit.Brush("Accent");
        var danger = (ISolidColorBrush)UiKit.Brush("Danger");
        var warn = UiKit.Brush("Warn");
        using (ctx.PushTransform(WorldToScreen()))
        {
            if (_edge is not null) ctx.DrawGeometry(sheetBrush, new Pen(new SolidColorBrush(text), 1.2 / k), _edge);
            else
            {
                var b = Shown(_sheet);
                ctx.DrawRectangle(sheetBrush, null, new Rect(b.MinX, b.MinY, b.Width, b.Height));
            }
            ctx.DrawGeometry(letterBrush, null, _letters);
            var looseBrush = new SolidColorBrush(Color.FromArgb(150, danger.Color.R, danger.Color.G, danger.Color.B));
            foreach (var m in _loose)
                ctx.DrawGeometry(looseBrush, new Pen(danger, 1.5 / k), ToStream(PathGeometry.Polyline(m.Outline, true)));
            foreach (var b in _bridges)
            {
                bool hot = ReferenceEquals(b, _hover) || b.Equals(_hover);
                var pen = new Pen(hot ? danger : _long.Contains(b) ? warn : accent, (hot ? 2 : 1.2) / k);
                ctx.DrawGeometry(sheetBrush, pen, ToStream(b.Outline));
            }
        }
        if (_hover is not null)
        {
            var f = new FormattedText($"{StencilEdit.Mm(_hover.Length)} mm long · click to take it away", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 11, new SolidColorBrush(text));
            ctx.DrawText(f, new Point(8, Bounds.Height - f.Height - 6));
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
        double tol = 4 / Scale;
        var hit = _bridges.Where(b => b.DistanceTo(w) <= b.WidthMm / 2 + tol).MinBy(b => b.DistanceTo(w));
        if (!Equals(hit, _hover)) { _hover = hit; InvalidateVisual(); }
        Cursor = new Cursor(hit is null ? StandardCursorType.Cross : StandardCursorType.Hand);
    }

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
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { SeeAll(); return; }
        Clicked?.Invoke(ToWorld(pt.Position));
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
