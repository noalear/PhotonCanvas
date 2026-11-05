using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What dragging on the map does.</summary>
public enum CityTool { Select, Ground, Fill, Building, Place, Fence, Prop, Erase }

/// <summary>
/// The map seen from above, on its grid, over the battle map it was read from (if any): the ground, each
/// building's walls, doors and roof, fences and the trees and other things. Zoom with the wheel; move it with
/// the middle button, or Shift and drag. With the tool chosen: pick and drag buildings (R or the wheel while
/// dragging turns one, Delete takes it away), paint or fill the ground, drag out a new building's footprint,
/// put down a saved building, draw fences along the grid lines, drop trees and the like, or rub things out.
/// The right button rubs out with the painting tools.
/// </summary>
public sealed class CityPlan : Control
{
    CityMap _map = CityMap.Empty(20, 15);
    WriteableBitmap? _groundBitmap;
    Bitmap? _picture;
    int _picW, _picH;
    bool _showPicture = true;
    double _groundOpacity = 1;

    /// <summary>After each change: the map as it now is, and what was done (for undo).</summary>
    public event Action<CityMap, string>? Edited;
    /// <summary>A building was picked (or none).</summary>
    public event Action<Guid?>? Picked;

    public CityTool Tool { get; set; } = CityTool.Select;
    public Ground Paint { get; set; } = Ground.Road;
    /// <summary>The brush: how many squares across.</summary>
    public int Brush { get; set; } = 1;
    public FenceKind FenceKind { get; set; } = FenceKind.Fence;
    public PropKind PropKind { get; set; } = PropKind.Tree;
    /// <summary>What the Place tool puts down, turned by <see cref="PlaceTurn"/>.</summary>
    public PrefabInfo? ToPlace { get; set; }
    public int PlaceTurn { get; set; }
    /// <summary>Makes a new building for a footprint dragged out with the Building tool (squares, top left and size).</summary>
    public Func<int, int, int, int, CityBuilding?>? NewBuilding { get; set; }

    public Guid? Selected { get; private set; }

    public CityPlan()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public CityMap Map => _map;

    public void Show(CityMap map, bool refit = false)
    {
        bool resized = map.Across != _map.Across || map.Down != _map.Down;
        _map = map;
        if (Selected is { } s && map.Building(s) is null) Select(null);
        RedrawGround();
        if (refit || resized) Fit();
        InvalidateVisual();
    }

    public void Select(Guid? id)
    {
        Selected = id;
        Picked?.Invoke(id);
        InvalidateVisual();
    }

    public void SetPicture(Bitmap? picture, int width, int height)
    {
        _picture = picture;
        _picW = width;
        _picH = height;
        InvalidateVisual();
    }

    public bool ShowPicture
    {
        get => _showPicture;
        set { _showPicture = value; InvalidateVisual(); }
    }

    /// <summary>How solid the ground's colours are over the picture (0 to 1).</summary>
    public double GroundOpacity
    {
        get => _groundOpacity;
        set { _groundOpacity = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    void RedrawGround()
    {
        int w = _map.Across, h = _map.Down;
        if (_groundBitmap is null || _groundBitmap.PixelSize.Width != w || _groundBitmap.PixelSize.Height != h)
        {
            _groundBitmap?.Dispose();
            _groundBitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        var g = _map.GroundGrid();
        using var fb = _groundBitmap.Lock();
        var row = new int[w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = CityScene.GroundColour(g[x, y]);
                row[x] = unchecked((int)(0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w);
        }
    }

    // ---- where things are on screen ----

    double _zoom = 1;
    Vector _pan;
    const double Gutter = 4;

    /// <summary>Pixels per square.</summary>
    double K => Math.Max(0.5, Base * _zoom);

    double Base => Math.Max(0.5, Math.Min((Bounds.Width - 2 * Gutter) / _map.Across, (Bounds.Height - 2 * Gutter) / _map.Down));

    Point Origin
    {
        get
        {
            double k = K;
            return new Point((Bounds.Width - k * _map.Across) / 2 + _pan.X, (Bounds.Height - k * _map.Down) / 2 + _pan.Y);
        }
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    Point At(double x, double y) { var o = Origin; double k = K; return new Point(o.X + x * k, o.Y + y * k); }

    (double X, double Y) ToMap(Point p) { var o = Origin; double k = K; return ((p.X - o.X) / k, (p.Y - o.Y) / k); }

    (int X, int Y)? CellAt((double X, double Y) p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        return _map.In(x, y) ? (x, y) : null;
    }

    FenceEdge? EdgeAt((double X, double Y) p)
    {
        double hy = Math.Round(p.Y), dy = Math.Abs(p.Y - hy), vx = Math.Round(p.X), dx = Math.Abs(p.X - vx);
        var e = dy <= dx ? new FenceEdge((int)Math.Floor(p.X), (int)hy, true) : new FenceEdge((int)vx, (int)Math.Floor(p.Y), false);
        bool valid = e.Across ? e.X >= 0 && e.X < _map.Across && e.Y >= 0 && e.Y <= _map.Down : e.X >= 0 && e.X <= _map.Across && e.Y >= 0 && e.Y < _map.Down;
        return Math.Min(dx, dy) <= 0.3 && valid ? e : null;
    }

    CityBuilding? BuildingAt((double X, double Y) p)
    {
        // The last drawn (on top) first; inside its squares before inside its rectangle.
        var cell = ((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        for (int i = _map.Buildings.Count - 1; i >= 0; i--)
            if (CityFootprint.Cells(_map.Buildings[i]).Contains(cell)) return _map.Buildings[i];
        for (int i = _map.Buildings.Count - 1; i >= 0; i--)
            if (CityFootprint.Hit(_map.Buildings[i], p.X, p.Y)) return _map.Buildings[i];
        return null;
    }

    // ---- drawing ----

    static readonly IBrush WallBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x1C));
    static readonly IBrush DoorBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x7A, 0x1E));
    static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0xB6, 0xE0));
    static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x3B, 0x82, 0xF6));
    static readonly IBrush RubBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE0, 0x40, 0x40));
    static readonly IBrush PickBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x9A, 0x2A));
    static readonly IBrush OverlapBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xE0, 0x40, 0x40));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        double k = K;
        var o = Origin;
        var all = new Rect(o, new Size(k * _map.Across, k * _map.Down));
        using var clip = ctx.PushClip(new Rect(Bounds.Size));
        bool picture = _picture is not null && _showPicture && _map.Picture is { SquarePx: > 0 };
        if (picture)
        {
            var pg = _map.Picture!;
            double p = pg.SquarePx, x0 = pg.OffsetX - Math.Floor(pg.OffsetX / p) * p, y0 = pg.OffsetY - Math.Floor(pg.OffsetY / p) * p;
            if (x0 > p * 0.5) x0 -= p;
            if (y0 > p * 0.5) y0 -= p;
            var dest = new Rect(o.X - x0 / p * k, o.Y - y0 / p * k, _picW / p * k, _picH / p * k);
            using (ctx.PushClip(all))
                ctx.DrawImage(_picture!, new Rect(0, 0, _picture!.PixelSize.Width, _picture.PixelSize.Height), dest);
        }
        if (_groundBitmap is not null && (!picture || _groundOpacity > 0.01))
            using (ctx.PushOpacity(picture ? _groundOpacity : 1))
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                ctx.DrawImage(_groundBitmap, new Rect(0, 0, _map.Across, _map.Down), all);

        if (k >= 7)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1);
            int x0 = Math.Max(0, (int)Math.Floor(-o.X / k)), x1 = Math.Min(_map.Across, (int)Math.Ceiling((Bounds.Width - o.X) / k));
            int y0 = Math.Max(0, (int)Math.Floor(-o.Y / k)), y1 = Math.Min(_map.Down, (int)Math.Ceiling((Bounds.Height - o.Y) / k));
            for (int x = x0; x <= x1; x++) ctx.DrawLine(gridPen, At(x, y0), At(x, y1));
            for (int y = y0; y <= y1; y++) ctx.DrawLine(gridPen, At(x0, y), At(x1, y));
        }

        // Fences.
        foreach (var f in _map.Fences)
        {
            var colour = f.Kind switch
            {
                FenceKind.Hedge => Color.FromRgb(0x3F, 0x6A, 0x2E),
                FenceKind.Fence => Color.FromRgb(0x6E, 0x4D, 0x30),
                _ => Color.FromRgb(0x7A, 0x74, 0x6C),
            };
            double width = k * (f.Kind switch { FenceKind.CityWall => 0.4, FenceKind.Hedge => 0.25, FenceKind.StoneWall => 0.14, _ => 0.08 });
            var pen = new Pen(new SolidColorBrush(colour), Math.Max(1.5, width), lineCap: PenLineCap.Square);
            foreach (var (ax, ay, bx, by) in CityScene.Runs(f.Edges)) ctx.DrawLine(pen, At(ax, ay), At(bx, by));
        }

        // Buildings: their squares, roofs, walls with the doors and windows in them.
        var overlaps = CityFootprint.Overlaps(_map).SelectMany(p => new[] { p.A.Id, p.B.Id }).ToHashSet();
        foreach (var b in _map.Buildings) DrawBuilding(ctx, b, k, overlaps.Contains(b.Id));

        // Trees and the like.
        foreach (var p in _map.Props) DrawProp(ctx, p, k);

        // What the pointer is over, and what's being dragged.
        DrawHover(ctx, k);
        if (_map.Buildings.Count == 0 && _map.Props.Count == 0)
            Text(ctx, "Read a battle map, or drag out buildings with the Building tool", new Point(Bounds.Width / 2, Bounds.Height - 22), UiKit.Brush("TextMuted"), 11, true);
    }

    static Color Scene3DShade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    void DrawBuilding(DrawingContext ctx, CityBuilding b, double k, bool overlapping)
    {
        bool picked = b.Id == Selected;
        var cells = CityFootprint.Cells(b);
        var roofCol = b.Design.Roofs.Count > 0 ? CityScene.RoofColour(b.Design.Roofs[0].Covering) : CityScene.WallColour(b.Design.Style.Texture);
        var fill = new SolidColorBrush(roofCol, 0.82);
        foreach (var (x, y) in cells) ctx.FillRectangle(overlapping ? OverlapBrush : fill, new Rect(At(x, y), new Size(k + 0.5, k + 0.5)));
        // The ridges, so the roofs read from above.
        var ridge = new Pen(new SolidColorBrush(Scene3DShade(roofCol, 0.6)), Math.Max(1, k * 0.06));
        foreach (var r in b.Design.Roofs)
        {
            if (r.Kind == HouseEditor.RoofKind.Flat) continue;
            Point P(double x, double y) { var (mx, my) = CityFootprint.ToMap(b, x, y); return At(mx, my); }
            double cx = (r.X0 + r.X1) / 2.0, cy = (r.Y0 + r.Y1) / 2.0;
            if (r.Kind == HouseEditor.RoofKind.LeanTo) continue;
            if (r.RidgeAcross)
            {
                double inset = r.Kind == HouseEditor.RoofKind.Hip ? Math.Min((r.Y1 - r.Y0) / 2.0, (r.X1 - r.X0) / 2.0) : 0;
                ctx.DrawLine(ridge, P(r.X0 + inset, cy), P(r.X1 - inset, cy));
            }
            else
            {
                double inset = r.Kind == HouseEditor.RoofKind.Hip ? Math.Min((r.X1 - r.X0) / 2.0, (r.Y1 - r.Y0) / 2.0) : 0;
                ctx.DrawLine(ridge, P(cx, r.Y0 + inset), P(cx, r.Y1 - inset));
            }
        }
        if (b.Design.Levels.Count > 0)
        {
            var g = b.Design.Levels[0];
            var wallPen = new Pen(picked ? PickBrush : WallBrush, Math.Clamp(k * 0.12, 1, 6), lineCap: PenLineCap.Square);
            foreach (var e in g.Walls)
            {
                var (ax, ay) = CityFootprint.ToMap(b, e.A.X, e.A.Y);
                var (bx, by) = CityFootprint.ToMap(b, e.B.X, e.B.Y);
                ctx.DrawLine(wallPen, At(ax, ay), At(bx, by));
            }
            if (k >= 6)
            {
                double r = Math.Clamp(k * 0.14, 1.5, 5);
                foreach (var op in g.Openings)
                {
                    var (mx, my) = CityFootprint.ToMap(b, op.X, op.Y);
                    ctx.DrawEllipse(op.Kind == HouseEditor.OpeningKind.Window ? WindowBrush : DoorBrush, null, At(mx, my), r, r);
                }
            }
        }
        if (picked)
        {
            var (w, d) = CityFootprint.Size(b);
            ctx.DrawRectangle(null, new Pen(PickBrush, 2) { DashStyle = new DashStyle([3, 2], 0) }, new Rect(At(b.X, b.Y), At(b.X + w, b.Y + d)));
        }
        if (k >= 10)
        {
            var (w, d) = CityFootprint.Size(b);
            var mid = At(b.X + w / 2.0, b.Y + d / 2.0);
            Text(ctx, b.Name, mid + new Point(0, -6), Brushes.White, 10, true, shadow: true);
        }
    }

    void DrawProp(DrawingContext ctx, Prop p, double k)
    {
        var at = At(p.X, p.Y);
        double r = Math.Max(1.5, k * 0.36 * p.Size);
        IBrush fill = p.Kind switch
        {
            PropKind.Tree => new SolidColorBrush(Color.FromRgb(0x3F, 0x72, 0x34)),
            PropKind.Pine => new SolidColorBrush(Color.FromRgb(0x2E, 0x5A, 0x34)),
            PropKind.Bush => new SolidColorBrush(Color.FromRgb(0x5A, 0x86, 0x44)),
            PropKind.Boulder => new SolidColorBrush(Color.FromRgb(0x8C, 0x88, 0x82)),
            PropKind.Well => new SolidColorBrush(Color.FromRgb(0x4E, 0x86, 0xB6)),
            PropKind.Lamp => new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x6A)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0x70, 0x48)),
        };
        if (p.Kind is PropKind.Lamp or PropKind.Statue) r *= 0.5;
        if (p.Kind is PropKind.Cart or PropKind.Crates) ctx.FillRectangle(fill, new Rect(at.X - r * 0.8, at.Y - r * 0.5, r * 1.6, r));
        else ctx.DrawEllipse(fill, k >= 8 ? new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 1) : null, at, r, r);
    }

    void DrawHover(DrawingContext ctx, double k)
    {
        if (_drag is { } dg && Tool == CityTool.Building)
        {
            int x0 = Math.Min(dg.A.X, dg.B.X), x1 = Math.Max(dg.A.X, dg.B.X), y0 = Math.Min(dg.A.Y, dg.B.Y), y1 = Math.Max(dg.A.Y, dg.B.Y);
            ctx.FillRectangle(HoverBrush, new Rect(At(x0, y0), At(x1 + 1, y1 + 1)));
            Text(ctx, $"{x1 - x0 + 1} × {y1 - y0 + 1}", At(x1 + 1, y1 + 1) + new Point(4, 2), UiKit.Brush("Text"), 11, false);
            return;
        }
        if (_hover is not { } hp) return;
        switch (Tool)
        {
            case CityTool.Ground or CityTool.Fill or CityTool.Erase or CityTool.Building:
            {
                if (CellAt(hp) is not { } c) break;
                int n = Tool == CityTool.Ground ? Math.Max(1, Brush) : 1, h0 = (n - 1) / 2;
                ctx.FillRectangle(Tool == CityTool.Erase || _right ? RubBrush : HoverBrush, new Rect(At(c.X - h0, c.Y - h0), new Size(k * n, k * n)));
                break;
            }
            case CityTool.Fence:
                if (EdgeAt(hp) is { } e)
                    ctx.DrawLine(new Pen(HoverBrush, Math.Max(3, k * 0.25)), At(e.X, e.Y), e.Across ? At(e.X + 1, e.Y) : At(e.X, e.Y + 1));
                break;
            case CityTool.Place when ToPlace is { } p:
            {
                var b = p.Place((int)Math.Floor(hp.X), (int)Math.Floor(hp.Y), PlaceTurn);
                var (w, d) = CityFootprint.Size(b);
                ctx.FillRectangle(HoverBrush, new Rect(At(b.X, b.Y), At(b.X + w, b.Y + d)));
                foreach (var w0 in b.Design.Levels.FirstOrDefault()?.Walls ?? [])
                {
                    var (ax, ay) = CityFootprint.ToMap(b, w0.A.X, w0.A.Y);
                    var (bx, by) = CityFootprint.ToMap(b, w0.B.X, w0.B.Y);
                    ctx.DrawLine(new Pen(WallBrush, Math.Clamp(k * 0.1, 1, 4)), At(ax, ay), At(bx, by));
                }
                break;
            }
            case CityTool.Prop:
                ctx.DrawEllipse(_right ? RubBrush : HoverBrush, null, At(hp.X, hp.Y), Math.Max(3, k * 0.4), Math.Max(3, k * 0.4));
                break;
        }
    }

    static void Text(DrawingContext ctx, string s, Point at, IBrush brush, double size, bool centred, bool shadow = false)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush);
        var p = centred ? new Point(at.X - ft.Width / 2, at.Y) : at;
        if (shadow)
        {
            var back = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, Brushes.Black);
            ctx.DrawText(back, p + new Point(1, 1));
        }
        ctx.DrawText(ft, p);
    }

    // ---- editing ----

    (double X, double Y)? _hover;
    bool _down, _right, _changed, _panning;
    Point _panFrom;
    ((int X, int Y) A, (int X, int Y) B)? _drag;
    Ground[,]? _ground;
    CityBuilding? _moving;
    (double X, double Y) _grab;
    bool? _fenceAdd;
    readonly HashSet<FenceEdge> _stroke = [];

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_moving is { } m && _down)
        {
            Turn(m, e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }
        if (Tool == CityTool.Place && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            PlaceTurn = (PlaceTurn + (e.Delta.Y > 0 ? 1 : 3)) % 4;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        var at = e.GetPosition(this);
        var before = ToMap(at);
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 1, 60);
        var after = At(before.X, before.Y);
        _pan += at - after;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        var props = pt.Properties;
        Focus();
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _panning = true;
            _panFrom = pt.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
        _down = true;
        _right = props.IsRightButtonPressed;
        _changed = false;
        _stroke.Clear();
        _fenceAdd = null;
        e.Pointer.Capture(this);
        var p = ToMap(pt.Position);
        switch (Tool)
        {
            case CityTool.Select:
            {
                var b = BuildingAt(p);
                Select(b?.Id);
                _moving = _right ? null : b;
                _grab = b is null ? default : (p.X - b.X, p.Y - b.Y);
                break;
            }
            case CityTool.Ground:
                _ground = _map.GroundGrid();
                PaintAt(p);
                break;
            case CityTool.Fill:
                Fill(p);
                break;
            case CityTool.Building:
                if (!_right && CellAt(p) is { } c) _drag = (c, c);
                else if (_right) EraseAt(p);
                break;
            case CityTool.Place:
                if (_right) { PlaceTurn = (PlaceTurn + 1) % 4; _down = false; }
                else if (ToPlace is { } prefab)
                {
                    var b = prefab.Place((int)Math.Floor(p.X), (int)Math.Floor(p.Y), PlaceTurn);
                    _map = _map.WithBuilding(b);
                    Select(b.Id);
                    _changed = true;
                }
                break;
            case CityTool.Fence:
                FenceAt(p);
                break;
            case CityTool.Prop:
                if (_right) RemovePropAt(p);
                else
                {
                    _map = _map with { Props = [.. _map.Props, new Prop { Kind = PropKind, X = Math.Round(p.X * 4) / 4, Y = Math.Round(p.Y * 4) / 4, TurnDeg = Random.Shared.Next(360), Size = PropKind is PropKind.Tree or PropKind.Pine ? 1.2 : 1 }] };
                    _changed = true;
                }
                break;
            case CityTool.Erase:
                EraseAt(p);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_panning)
        {
            _pan += pos - _panFrom;
            _panFrom = pos;
            InvalidateVisual();
            return;
        }
        var p = ToMap(pos);
        _hover = p;
        if (_down)
        {
            switch (Tool)
            {
                case CityTool.Select when _moving is { } m:
                {
                    int nx = (int)Math.Round(p.X - _grab.X), ny = (int)Math.Round(p.Y - _grab.Y);
                    var (w, d) = CityFootprint.Size(m);
                    nx = Math.Clamp(nx, 0, Math.Max(0, _map.Across - w));
                    ny = Math.Clamp(ny, 0, Math.Max(0, _map.Down - d));
                    if (nx != m.X || ny != m.Y)
                    {
                        _moving = m with { X = nx, Y = ny };
                        _map = _map.WithBuilding(_moving);
                        _changed = true;
                    }
                    break;
                }
                case CityTool.Ground:
                    PaintAt(p);
                    break;
                case CityTool.Building when _drag is { } dg:
                    _drag = (dg.A, ((int)Math.Clamp(Math.Floor(p.X), 0, _map.Across - 1), (int)Math.Clamp(Math.Floor(p.Y), 0, _map.Down - 1)));
                    break;
                case CityTool.Building when _right:
                case CityTool.Erase:
                    EraseAt(p);
                    break;
                case CityTool.Fence:
                    FenceAt(p);
                    break;
                case CityTool.Prop when _right:
                    RemovePropAt(p);
                    break;
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            return;
        }
        if (!_down) return;
        _down = false;
        e.Pointer.Capture(null);
        if (Tool == CityTool.Building && _drag is { } dg)
        {
            int x0 = Math.Min(dg.A.X, dg.B.X), x1 = Math.Max(dg.A.X, dg.B.X), y0 = Math.Min(dg.A.Y, dg.B.Y), y1 = Math.Max(dg.A.Y, dg.B.Y);
            _drag = null;
            if (NewBuilding?.Invoke(x0, y0, x1 - x0 + 1, y1 - y0 + 1) is { } b)
            {
                _map = _map.WithBuilding(b);
                Select(b.Id);
                _changed = true;
            }
        }
        if (Tool == CityTool.Ground && _ground is not null)
        {
            if (_changed) _map = _map.WithGround(_ground);
            _ground = null;
        }
        _moving = null;
        if (_changed)
        {
            RedrawGround();
            Edited?.Invoke(_map, Tool switch
            {
                CityTool.Select => "Move a building",
                CityTool.Ground => "Paint the ground",
                CityTool.Fill => "Fill the ground",
                CityTool.Building => "Add a building",
                CityTool.Place => "Put down a building",
                CityTool.Fence => "Change fences",
                CityTool.Prop => _right ? "Take away trees" : "Add a tree",
                _ => "Rub out",
            });
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && Selected is { } id && _map.Building(id) is not null)
        {
            _map = _map.Without(id);
            Select(null);
            Edited?.Invoke(_map, "Take a building away");
            e.Handled = true;
        }
        else if (e.Key is Key.R or Key.OemCloseBrackets or Key.OemOpenBrackets)
        {
            int by = e.Key == Key.OemOpenBrackets || e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            if (Tool == CityTool.Place) { PlaceTurn = ((PlaceTurn + by) % 4 + 4) % 4; InvalidateVisual(); }
            else if (Selected is { } sid && _map.Building(sid) is { } b)
            {
                Turn(b, by);
                if (!_down) Edited?.Invoke(_map, "Turn a building");
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F) { Fit(); e.Handled = true; }
    }

    /// <summary>Turns a building a quarter turn about its middle (kept on the map).</summary>
    void Turn(CityBuilding b, int by)
    {
        var (w, d) = CityFootprint.Size(b);
        double cx = b.X + w / 2.0, cy = b.Y + d / 2.0;
        var t = b with { Turn = ((b.Turn + by) % 4 + 4) % 4 };
        var (tw, td) = CityFootprint.Size(t);
        t = t with
        {
            X = Math.Clamp((int)Math.Round(cx - tw / 2.0), 0, Math.Max(0, _map.Across - tw)),
            Y = Math.Clamp((int)Math.Round(cy - td / 2.0), 0, Math.Max(0, _map.Down - td)),
        };
        if (_moving?.Id == b.Id) _moving = t;
        _map = _map.WithBuilding(t);
        _changed = true;
        InvalidateVisual();
    }

    void PaintAt((double X, double Y) p)
    {
        if (_ground is null || CellAt(p) is not { } c) return;
        int n = Math.Max(1, Brush), h0 = (n - 1) / 2;
        var to = _right ? Ground.Grass : Paint;
        for (int y = c.Y - h0; y < c.Y - h0 + n; y++)
            for (int x = c.X - h0; x < c.X - h0 + n; x++)
                if (_map.In(x, y) && _ground[x, y] != to) { _ground[x, y] = to; _changed = true; }
        if (_changed)
        {
            // Shown as it's painted.
            _map = _map.WithGround(_ground);
            RedrawGround();
        }
    }

    void Fill((double X, double Y) p)
    {
        if (CellAt(p) is not { } c) return;
        var g = _map.GroundGrid();
        var from = g[c.X, c.Y];
        var to = _right ? Ground.Grass : Paint;
        if (from == to) return;
        var todo = new Stack<(int X, int Y)>();
        todo.Push(c);
        while (todo.Count > 0)
        {
            var (x, y) = todo.Pop();
            if (!_map.In(x, y) || g[x, y] != from) continue;
            g[x, y] = to;
            todo.Push((x + 1, y)); todo.Push((x - 1, y)); todo.Push((x, y + 1)); todo.Push((x, y - 1));
        }
        _map = _map.WithGround(g);
        _changed = true;
    }

    void FenceAt((double X, double Y) p)
    {
        if (EdgeAt(p) is not { } e || !_stroke.Add(e)) return;
        bool has = _map.Fences.Any(f => f.Edges.Contains(e));
        _fenceAdd ??= !_right && !has;
        var fences = _map.Fences.Select(f => f with { Edges = [.. f.Edges.Where(x => x != e)] }).Where(f => f.Edges.Count > 0).ToList();
        if (_fenceAdd == true)
        {
            int i = fences.FindIndex(f => f.Kind == FenceKind);
            if (i < 0) fences.Add(new Fence { Kind = FenceKind, Edges = [e] });
            else fences[i] = fences[i] with { Edges = [.. fences[i].Edges, e] };
        }
        else if (!has) return;
        _map = _map with { Fences = fences };
        _changed = true;
    }

    void RemovePropAt((double X, double Y) p)
    {
        double r = 0.5;
        int before = _map.Props.Count;
        var left = _map.Props.Where(q => (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y) > r * r).ToList();
        if (left.Count == before) return;
        _map = _map with { Props = left };
        _changed = true;
    }

    void EraseAt((double X, double Y) p)
    {
        int props = _map.Props.Count;
        RemovePropAt(p);
        if (_map.Props.Count != props) return;
        if (EdgeAt(p) is { } e && _map.Fences.Any(f => f.Edges.Contains(e)))
        {
            _map = _map with { Fences = [.. _map.Fences.Select(f => f with { Edges = [.. f.Edges.Where(x => x != e)] }).Where(f => f.Edges.Count > 0)] };
            _changed = true;
            return;
        }
        if (BuildingAt(p) is { } b)
        {
            _map = _map.Without(b.Id);
            if (Selected == b.Id) Select(null);
            _changed = true;
        }
    }
}
