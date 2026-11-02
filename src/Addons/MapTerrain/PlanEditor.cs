using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>What dragging on the plan does.</summary>
public enum PlanTool { Room, Floor, Erase, Wall, Door }

/// <summary>
/// The floor plan, drawn on its grid over the battle map it was read from (if any): the floor squares,
/// the walls as they'll be made (with a column at every join), the doors and the gaps. Drag with the
/// tool chosen: a room (a rectangle of floor; the right button rubs one out), floor and rubbing out a
/// square at a time, walls along the grid lines (adding, or taking away where there is one; the right
/// button puts an edge back as it was), and doors (click a wall).
/// </summary>
public sealed class PlanEditor : Control
{
    PlanGrid _grid = new(10, 8);
    MapLayout? _layout;
    Bitmap? _picture;
    int _picW, _picH;
    MapPicture? _picGrid;
    bool _showPicture = true;

    /// <summary>Works out the walls for a plan as it's drawn (the window's settings decide the columns and doors).</summary>
    public Func<PlanGrid, MapLayout?>? LayoutFor { get; set; }

    /// <summary>After each stroke: the plan as it now is, and what was done (for undo).</summary>
    public event Action<PlanGrid, string>? Edited;

    public PlanTool Tool { get; set; } = PlanTool.Room;

    static readonly IBrush FloorFill = new SolidColorBrush(Color.FromRgb(0xE9, 0xD9, 0xBB));
    static readonly IBrush FloorOver = new SolidColorBrush(Color.FromArgb(0x8C, 0xF2, 0xE2, 0xC0));
    static readonly IBrush WallBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x33, 0x1E));
    static readonly IBrush DoorBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x7A, 0x1E));
    static readonly IBrush ColumnBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x22, 0x16));
    static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x3B, 0x82, 0xF6));
    static readonly IBrush RubBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE0, 0x40, 0x40));

    public PlanEditor()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public PlanGrid Grid => _grid;

    public void Show(PlanGrid grid)
    {
        _grid = grid.Copy();
        _layout = LayoutFor?.Invoke(_grid);
        InvalidateVisual();
    }

    /// <summary>Re-works the walls (the settings changed).</summary>
    public void Relayout()
    {
        _layout = LayoutFor?.Invoke(_grid);
        InvalidateVisual();
    }

    public void SetPicture(Bitmap? picture, int width, int height, MapPicture? grid)
    {
        _picture = picture;
        _picW = width;
        _picH = height;
        _picGrid = grid;
        InvalidateVisual();
    }

    public bool ShowPicture
    {
        get => _showPicture;
        set { _showPicture = value; InvalidateVisual(); }
    }

    public bool HasPicture => _picture is not null;

    // ---- where things are on screen ----

    const double Gutter = 26;

    double K => Math.Max(1, Math.Min((Bounds.Width - Gutter - 10) / _grid.Across, (Bounds.Height - Gutter - 10) / _grid.Down));

    Point Origin
    {
        get
        {
            double k = K;
            return new Point(Gutter + (Bounds.Width - Gutter - 10 - k * _grid.Across) / 2, Gutter + (Bounds.Height - Gutter - 10 - k * _grid.Down) / 2);
        }
    }

    Point At(double x, double y) { var o = Origin; double k = K; return new Point(o.X + x * k, o.Y + y * k); }

    (double X, double Y) ToPlan(Point p) { var o = Origin; double k = K; return ((p.X - o.X) / k, (p.Y - o.Y) / k); }

    /// <summary>The grid edge nearest a point on the plan, if it's near enough.</summary>
    EdgeId? EdgeAt((double X, double Y) p)
    {
        double hy = Math.Round(p.Y), dy = Math.Abs(p.Y - hy), vx = Math.Round(p.X), dx = Math.Abs(p.X - vx);
        EdgeId e = dy <= dx ? new EdgeId((int)Math.Floor(p.X), (int)hy, true) : new EdgeId((int)vx, (int)Math.Floor(p.Y), false);
        return Math.Min(dx, dy) <= 0.3 && _grid.Valid(e) ? e : null;
    }

    (int X, int Y)? CellAt((double X, double Y) p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        return _grid.In(x, y) ? (x, y) : null;
    }

    // ---- drawing ----

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(Bounds.Size));
        double k = K;
        var o = Origin;
        var all = new Rect(o, new Size(k * _grid.Across, k * _grid.Down));
        bool picture = _picture is not null && _picGrid is { SquarePx: > 0 } && _showPicture;
        if (picture)
        {
            var pg = _picGrid!;
            double p = pg.SquarePx, x0 = pg.OffsetX - Math.Floor(pg.OffsetX / p) * p, y0 = pg.OffsetY - Math.Floor(pg.OffsetY / p) * p;
            if (x0 > p * 0.5) x0 -= p;
            if (y0 > p * 0.5) y0 -= p;
            var dest = new Rect(o.X - x0 / p * k, o.Y - y0 / p * k, _picW / p * k, _picH / p * k);
            using (ctx.PushClip(all))
                ctx.DrawImage(_picture!, new Rect(0, 0, _picture!.PixelSize.Width, _picture.PixelSize.Height), dest);
        }
        else ctx.FillRectangle(UiKit.Brush("Bg1"), all);

        for (int y = 0; y < _grid.Down; y++)
            for (int x = 0; x < _grid.Across; x++)
                if (_grid[x, y]) ctx.FillRectangle(picture ? FloorOver : FloorFill, new Rect(At(x, y), new Size(k, k)));

        var gridPen = new Pen(UiKit.Brush("Line"), 1);
        for (int x = 0; x <= _grid.Across; x++) ctx.DrawLine(gridPen, At(x, 0), At(x, _grid.Down));
        for (int y = 0; y <= _grid.Down; y++) ctx.DrawLine(gridPen, At(0, y), At(_grid.Across, y));

        // Labels: letters across, numbers down (as the notes name the squares).
        int every = k >= 16 ? 1 : k >= 8 ? 2 : 5;
        var muted = UiKit.Brush("TextMuted");
        for (int x = 0; x < _grid.Across; x += every) Text(ctx, MapPlan.Column(x), At(x + 0.5, 0) + new Point(0, -13), muted, true);
        for (int y = 0; y < _grid.Down; y += every) Text(ctx, (y + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), At(0, y + 0.5) + new Point(-14, -6), muted, true);

        // Gaps left by hand where a wall would be, dashed.
        var gapPen = new Pen(muted, Math.Max(1, k * 0.05)) { DashStyle = new DashStyle([2, 2], 0) };
        foreach (var e in _grid.Edges())
            if (_grid.Mark(e) == EdgeKind.Open && _grid.AutoWall(e)) Line(ctx, gapPen, e, 0.1);

        // The walls as they'll be made, with the doors in them and a column at every join.
        double wallW = Math.Clamp(k * 0.14, 2, 7);
        var wallPen = new Pen(WallBrush, wallW, lineCap: PenLineCap.Flat);
        var doorPen = new Pen(DoorBrush, wallW * 0.8, lineCap: PenLineCap.Flat);
        if (_layout is { } lay)
        {
            foreach (var p in lay.Panels)
            {
                var (ex, ey) = p.End;
                if (p.Door < 0) { ctx.DrawLine(wallPen, At(p.X, p.Y), At(ex, ey)); continue; }
                double t0 = p.Door + 0.2, t1 = p.Door + 0.8;
                (double, double) Along(double t) => p.Across ? (p.X + t, p.Y) : (p.X, p.Y + t);
                var a0 = Along(t0); var a1 = Along(t1);
                ctx.DrawLine(wallPen, At(p.X, p.Y), At(a0.Item1, a0.Item2));
                ctx.DrawLine(wallPen, At(a1.Item1, a1.Item2), At(ex, ey));
                ctx.DrawLine(doorPen, At(a0.Item1, a0.Item2), At(a1.Item1, a1.Item2));
            }
            var openPen = new Pen(DoorBrush, Math.Max(1, wallW * 0.5)) { DashStyle = new DashStyle([2, 2], 0) };
            foreach (var e in lay.OpenDoors) Line(ctx, openPen, e, 0.2);
            double c = Math.Clamp(k * 0.3, 4, 12);
            foreach (var col in lay.Columns)
            {
                var at = At(col.X, col.Y);
                ctx.FillRectangle(ColumnBrush, new Rect(at.X - c / 2, at.Y - c / 2, c, c));
            }
        }

        // What the pointer is over, and a room being dragged out.
        if (_room is { } r)
        {
            int x0 = Math.Min(r.A.X, r.B.X), x1 = Math.Max(r.A.X, r.B.X), y0 = Math.Min(r.A.Y, r.B.Y), y1 = Math.Max(r.A.Y, r.B.Y);
            ctx.FillRectangle(_roomErase ? RubBrush : HoverBrush, new Rect(At(x0, y0), At(x1 + 1, y1 + 1)));
        }
        else if (_hover is { } hp)
        {
            if (Tool is PlanTool.Wall or PlanTool.Door)
            {
                if (EdgeAt(hp) is { } he) Line(ctx, new Pen(HoverBrush, wallW * 1.6), he, 0);
            }
            else if (CellAt(hp) is { } hc) ctx.FillRectangle(Tool == PlanTool.Erase ? RubBrush : HoverBrush, new Rect(At(hc.X, hc.Y), new Size(k, k)));
        }
        if (_grid.FloorCount == 0)
            Text(ctx, "Drag out a room with the Room tool, or read a battle map", new Point(Bounds.Width / 2, Bounds.Height - 22), muted, true);
    }

    void Line(DrawingContext ctx, Pen pen, EdgeId e, double inset)
    {
        var (a, b) = e.Ends;
        double dx = (b.X - a.X) * inset, dy = (b.Y - a.Y) * inset;
        ctx.DrawLine(pen, At(a.X + dx, a.Y + dy), At(b.X - dx, b.Y - dy));
    }

    static void Text(DrawingContext ctx, string s, Point at, IBrush brush, bool centred)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, brush);
        ctx.DrawText(ft, centred ? new Point(at.X - ft.Width / 2, at.Y) : at);
    }

    // ---- editing ----

    (double X, double Y)? _hover;
    bool _down, _right, _changed, _addWalls;
    ((int X, int Y) A, (int X, int Y) B)? _room;
    bool _roomErase;
    readonly HashSet<EdgeId> _stroke = [];

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
        Focus();
        _down = true;
        _right = props.IsRightButtonPressed;
        _changed = false;
        _stroke.Clear();
        e.Pointer.Capture(this);
        var p = ToPlan(e.GetPosition(this));
        switch (Tool)
        {
            case PlanTool.Room:
                if (CellAt(p) is { } c) { _room = (c, c); _roomErase = _right; }
                break;
            case PlanTool.Wall:
                if (EdgeAt(p) is { } we) { _addWalls = !_right && _grid.State(we) == EdgeState.None; WallStroke(we); }
                break;
            case PlanTool.Door:
                if (EdgeAt(p) is { } de) Door(de);
                break;
            default:
                Paint(p);
                break;
        }
        Changed();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = ToPlan(e.GetPosition(this));
        _hover = p;
        if (_down)
        {
            switch (Tool)
            {
                case PlanTool.Room:
                    if (_room is { } r && CellAt((Math.Clamp(p.X, 0, _grid.Across - 0.01), Math.Clamp(p.Y, 0, _grid.Down - 0.01))) is { } c) _room = (r.A, c);
                    break;
                case PlanTool.Wall:
                    if (EdgeAt(p) is { } we) WallStroke(we);
                    break;
                case PlanTool.Floor or PlanTool.Erase:
                    Paint(p);
                    break;
            }
            Changed();
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_down) return;
        _down = false;
        e.Pointer.Capture(null);
        if (Tool == PlanTool.Room && _room is { } r)
        {
            int x0 = Math.Min(r.A.X, r.B.X), x1 = Math.Max(r.A.X, r.B.X), y0 = Math.Min(r.A.Y, r.B.Y), y1 = Math.Max(r.A.Y, r.B.Y);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) _grid[x, y] = !_roomErase;
            _room = null;
            _changed = true;
        }
        if (_changed)
        {
            _layout = LayoutFor?.Invoke(_grid);
            Edited?.Invoke(_grid.Copy(), Tool switch
            {
                PlanTool.Room => _roomErase ? "Rub out a room" : "Add a room",
                PlanTool.Floor => _right ? "Rub out floor" : "Paint floor",
                PlanTool.Erase => "Rub out floor",
                PlanTool.Wall => "Change walls",
                _ => "Change a door",
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

    void Changed()
    {
        if (_changed) _layout = LayoutFor?.Invoke(_grid);
        InvalidateVisual();
    }

    void Paint((double X, double Y) p)
    {
        if (CellAt(p) is not { } c) return;
        bool floor = Tool == PlanTool.Floor && !_right;
        if (_grid[c.X, c.Y] == floor) return;
        _grid[c.X, c.Y] = floor;
        _changed = true;
    }

    /// <summary>Adds or takes away the wall on an edge, the way the stroke started (the right button puts it back as it was).</summary>
    void WallStroke(EdgeId e)
    {
        if (!_stroke.Add(e) || !_grid.Touches(e)) return;
        var before = (_grid.Mark(e), _grid.State(e));
        if (_right) _grid.SetMark(e, EdgeKind.Auto);
        else if (_addWalls) { if (_grid.State(e) == EdgeState.None) _grid.SetMark(e, _grid.AutoWall(e) ? EdgeKind.Auto : EdgeKind.Wall); }
        else if (_grid.State(e) != EdgeState.None) _grid.SetMark(e, _grid.AutoWall(e) ? EdgeKind.Open : EdgeKind.Auto);
        if ((_grid.Mark(e), _grid.State(e)) != before) _changed = true;
    }

    /// <summary>A door where there's none (a wall goes there if there wasn't one), or the door taken out again (the wall stays).</summary>
    void Door(EdgeId e)
    {
        if (!_grid.Touches(e)) return;
        if (_grid.State(e) == EdgeState.Door) _grid.SetMark(e, _grid.AutoWall(e) ? EdgeKind.Auto : EdgeKind.Wall);
        else if (!_right) _grid.SetMark(e, EdgeKind.Door);
        else return;
        _changed = true;
    }
}
