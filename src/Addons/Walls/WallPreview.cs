using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// The pieces as they'll come out, at one scale: the wall's face on top with its texture, and under it
/// one of each other piece (the door, the walkway, the accessories, a layer of each column). The wall's
/// mount points are marked on the face and can be dragged about.
/// </summary>
public sealed class WallPreview : Control
{
    sealed record Placed(Part Part, double X, double Y, StreamGeometry Cut, StreamGeometry? Etched, StreamGeometry? Lines);

    /// <summary>A mount point as marked on the face: its slots, its middle and whether it's on the back.</summary>
    public sealed record Mark(List<(double X0, double Y0, double X1, double Y1)> Slots, double X, double Y, bool Back);

    readonly List<Placed> _placed = [];
    WallKit? _kit;
    bool _faceOnly;

    /// <summary>Just the wall's face, bigger (for placing mount points), or every piece.</summary>
    public bool FaceOnly
    {
        get => _faceOnly;
        set { if (_faceOnly == value) return; _faceOnly = value; Show(_kit, _message); }
    }
    double _w, _h;
    string? _message;
    List<Mark> _marks = [];
    int _selected = -1, _dragging = -1;
    Point _dragFrom;
    double _dx, _dy;
    // How the face was last drawn: wall mm to the screen.
    double _k, _ox, _oy, _fx, _fy;
    bool _drawn;

    /// <summary>A mount point was clicked (its number in the list).</summary>
    public event Action<int>? MountPicked;
    /// <summary>A mount point was dragged to a new middle (mm, on the wall).</summary>
    public event Action<int, double, double>? MountMoved;

    /// <summary>The mount points to mark on the face, and the one picked.</summary>
    public void ShowMounts(List<Mark> marks, int selected)
    {
        _marks = marks;
        _selected = selected;
        if (_dragging >= marks.Count) _dragging = -1;
        InvalidateVisual();
    }

    (double X, double Y) ToWall(Point p) => ((p.X - _ox) / _k - _fx, _fy - (p.Y - _oy) / _k);

    int Hit(Point p)
    {
        if (!_drawn) return -1;
        var (x, y) = ToWall(p);
        double pad = Math.Max(1.5, 6 / _k);
        for (int i = _marks.Count - 1; i >= 0; i--)
            if (_marks[i].Slots.Count > 0 && x >= _marks[i].Slots.Min(r => r.X0) - pad && x <= _marks[i].Slots.Max(r => r.X1) + pad
                && y >= _marks[i].Slots.Min(r => r.Y0) - pad && y <= _marks[i].Slots.Max(r => r.Y1) + pad) return i;
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        int i = Hit(p);
        if (i < 0) return;
        _dragging = _selected = i;
        _dragFrom = p;
        _dx = _dy = 0;
        e.Pointer.Capture(this);
        MountPicked?.Invoke(i);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragging < 0)
        {
            Cursor = Hit(p) >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            return;
        }
        // Moved in steps of half a millimetre.
        _dx = Math.Round((p.X - _dragFrom.X) / _k * 2) / 2;
        _dy = -Math.Round((p.Y - _dragFrom.Y) / _k * 2) / 2;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        int i = _dragging;
        _dragging = -1;
        e.Pointer.Capture(null);
        if (_dx != 0 || _dy != 0)
        {
            var m = _marks[i];
            double x = m.X + _dx, y = m.Y + _dy;
            // Shown where it was dropped until the wall is made again.
            _marks[i] = new Mark([.. m.Slots.Select(r => (r.X0 + _dx, r.Y0 + _dy, r.X1 + _dx, r.Y1 + _dy))], x, y, m.Back);
            _dx = _dy = 0;
            MountMoved?.Invoke(i, x, y);
        }
        InvalidateVisual();
    }

    static readonly IBrush Wood = new SolidColorBrush(Color.FromRgb(0xDD, 0xBE, 0x92));
    static readonly IBrush Burnt = new SolidColorBrush(Color.FromRgb(0x8C, 0x61, 0x37));
    static readonly Pen Edge = new(new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E)), 1);
    static readonly Pen Score = new(new SolidColorBrush(Color.FromRgb(0x6B, 0x47, 0x28)), 0.8);

    public WallPreview() => ClipToBounds = true;

    /// <summary>Shows a kit (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(WallKit? kit, string? message = null)
    {
        _kit = kit;
        _placed.Clear();
        _message = message;
        _w = _h = 0;
        _drawn = false;
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
            if (_faceOnly) { InvalidateVisual(); return; }
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
        (_k, _ox, _oy, _fx, _fy, _drawn) = (k, ox, oy, _placed[0].X, _placed[0].Y, true);
        DrawMarks(ctx);
    }

    void DrawMarks(DrawingContext ctx)
    {
        var accent = UiKit.Brush("Accent");
        Point S(double x, double y) => new((x + _fx) * _k + _ox, (_fy - y) * _k + _oy);
        for (int i = 0; i < _marks.Count; i++)
        {
            var m = _marks[i];
            if (m.Slots.Count == 0) continue;
            bool sel = i == _selected, drag = i == _dragging;
            double dx = drag ? _dx : 0, dy = drag ? _dy : 0;
            var pen = new Pen(accent, sel ? 2 : 1.3) { DashStyle = m.Back ? new DashStyle([3, 2], 0) : null };
            double pad = 1.2;
            var a = S(m.Slots.Min(r => r.X0) - pad + dx, m.Slots.Max(r => r.Y1) + pad + dy);
            var b = S(m.Slots.Max(r => r.X1) + pad + dx, m.Slots.Min(r => r.Y0) - pad + dy);
            var box = new Rect(a, b);
            ctx.DrawRectangle(sel ? new SolidColorBrush(Color.FromArgb(0x40, 0x3B, 0x82, 0xF6)) : null, pen, box, 2, 2);
            foreach (var r in m.Slots)
                ctx.DrawRectangle(accent, null, new Rect(S(r.X0 + dx, r.Y1 + dy), S(r.X1 + dx, r.Y0 + dy)));
            string label = drag ? $"{i + 1}: {m.X + dx:0.#}, {m.Y + dy:0.#} mm" : $"{i + 1}";
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.SemiBold), 11, accent);
            ctx.DrawText(text, new Point(box.Right + 3, box.Top - text.Height / 2));
        }
    }
}
