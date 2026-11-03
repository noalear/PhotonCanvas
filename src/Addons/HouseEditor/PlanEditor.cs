using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>What dragging on the plan does.</summary>
public enum PlanTool { Select, Wall, Room, Erase, Door, Doorway, Window, Floor }

/// <summary>
/// The floor plan, seen from above on a grid of squares: draw walls along the grid lines (or corner to
/// corner), drag out a whole room at once, rub walls out, drop doors and windows into the walls and drag
/// them along, and lay floor outside the rooms (a balcony). The floor below shows faintly underneath.
/// Scroll to zoom, drag with the middle or right button to move about.
/// </summary>
public sealed class PlanEditor : Control
{
    /// <summary>How many squares the plan reaches across and down.</summary>
    public const int GridSize = 40;

    HouseDesign _design = HouseDesign.Starter();
    int _level;
    PlanShape _plan = PlanShape.Of([]);
    PlanShape? _below;
    List<MadeWall> _made = [];
    double _scale = 30;
    Vector _offset = new(20, 20);
    bool _fitted;

    // Dragging.
    GridPt? _from;
    GridPt _to;
    Point? _pan;
    int _moving = -1;
    HouseDesign? _moveFrom;
    bool _erasing;
    Point _mouse;
    bool _inside;

    public PlanTool Tool { get; set; } = PlanTool.Wall;
    /// <summary>The door or window picked (its index on this floor), or −1.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The design was changed on the plan (what changed, for Undo).</summary>
    public event Action<HouseDesign, string>? Changed;
    /// <summary>A different door or window was picked (or none).</summary>
    public event Action? SelectionChanged;

    public PlanEditor()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public HouseDesign Design => _design;
    public int Level => _level;

    /// <summary>Shows a design, on one of its floors.</summary>
    public void Show(HouseDesign design, int level)
    {
        _design = design;
        _level = Math.Clamp(level, 0, design.Levels.Count - 1);
        _plan = PlanShape.Of(design.Levels[_level].Walls);
        _below = _level > 0 ? PlanShape.Of(design.Levels[_level - 1].Walls) : null;
        if (Selected >= design.Levels[_level].Openings.Count) Select(-1);
        if (!_fitted && Bounds.Width > 0) FitAll();
        InvalidateVisual();
    }

    /// <summary>The walls as last made, so each can be labelled with its letter.</summary>
    public void ShowMade(List<MadeWall> made)
    {
        _made = made;
        InvalidateVisual();
    }

    public void Select(int i)
    {
        if (Selected == i) return;
        Selected = i;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    Level Floor => _design.Levels[_level];

    void Commit(Level level, string what)
    {
        var levels = _design.Levels.ToList();
        levels[_level] = level;
        _design = _design with { Levels = levels };
        _plan = PlanShape.Of(level.Walls);
        Changed?.Invoke(_design, what);
        InvalidateVisual();
    }

    // ---- where things are ----

    Point ToScreen(double x, double y) => new(x * _scale + _offset.X, y * _scale + _offset.Y);
    (double X, double Y) ToPlan(Point p) => ((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
    GridPt Snap(Point p)
    {
        var (x, y) = ToPlan(p);
        return new(Math.Clamp((int)Math.Round(x), 0, GridSize), Math.Clamp((int)Math.Round(y), 0, GridSize));
    }

    /// <summary>The end of a wall dragged from <paramref name="a"/>: the nearest grid point straight along, across or corner to corner.</summary>
    static GridPt Constrain(GridPt a, double x, double y)
    {
        double dx = x - a.X, dy = y - a.Y;
        double angle = Math.Atan2(dy, dx);
        int oct = (int)Math.Round(angle / (Math.PI / 4));
        double ux = Math.Round(Math.Cos(oct * Math.PI / 4)), uy = Math.Round(Math.Sin(oct * Math.PI / 4));
        double along = (dx * ux + dy * uy) / (ux * ux + uy * uy);
        int n = Math.Max(0, (int)Math.Round(along));
        return new(Math.Clamp(a.X + (int)ux * n, 0, GridSize), Math.Clamp(a.Y + (int)uy * n, 0, GridSize));
    }

    static IEnumerable<WallEdge> Edges(GridPt a, GridPt b)
    {
        int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
        if (dx == 0 && dy == 0) yield break;
        for (var p = a; p != b; p += new GridPt(dx, dy)) yield return WallEdge.Of(p, p + new GridPt(dx, dy));
    }

    static IEnumerable<WallEdge> RoomEdges(GridPt a, GridPt b)
    {
        if (a.X == b.X || a.Y == b.Y) return Edges(a, b);
        var c1 = new GridPt(b.X, a.Y);
        var c2 = new GridPt(a.X, b.Y);
        return Edges(a, c1).Concat(Edges(c1, b)).Concat(Edges(b, c2)).Concat(Edges(c2, a));
    }

    /// <summary>The nearest point on a wall to the pointer (squares), within a third of a square, snapped to quarter squares along it; null if there's none near.</summary>
    (double X, double Y)? OnWall(Point p)
    {
        var (x, y) = ToPlan(p);
        (double X, double Y)? best = null;
        double bestD = 0.35;
        foreach (var run in _plan.Runs)
        {
            double ax = run.A.X, ay = run.A.Y, bx = run.B.X, by = run.B.Y;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
            // Quarter squares along it, kept off the columns at its ends.
            double steps = Math.Round(t * run.Steps * 4) / 4;
            steps = Math.Clamp(steps, 0.5, run.Steps - 0.5);
            if (run.Steps < 1) continue;
            double px = ax + dx * steps / run.Steps, py = ay + dy * steps / run.Steps;
            double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
            double off = Math.Abs((x - ax) * dy - (y - ay) * dx) / Math.Sqrt(len2);
            if (off < bestD && d < 1.5) { bestD = off; best = (px, py); }
        }
        return best;
    }

    int OpeningAt(Point p)
    {
        var (x, y) = ToPlan(p);
        for (int i = Floor.Openings.Count - 1; i >= 0; i--)
        {
            var o = Floor.Openings[i];
            double r = Math.Max(0.3, o.WidthMm / _design.SquareMm / 2);
            if ((o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y) <= r * r + 0.04) return i;
        }
        return -1;
    }

    // ---- the mouse ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        _mouse = pt.Position;
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _pan = pt.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        switch (Tool)
        {
            case PlanTool.Select:
            {
                int i = OpeningAt(pt.Position);
                Select(i);
                _moving = i;
                _moveFrom = _design;
                break;
            }
            case PlanTool.Door or PlanTool.Doorway or PlanTool.Window:
            {
                int i = OpeningAt(pt.Position);
                if (i >= 0) { Select(i); _moving = i; _moveFrom = _design; break; }
                if (OnWall(pt.Position) is not { } at) break;
                var s = _design.Style;
                var o = Tool switch
                {
                    PlanTool.Door => Opening.NewDoor(s, at.X, at.Y),
                    PlanTool.Doorway => Opening.NewDoorway(s, at.X, at.Y),
                    _ => Opening.NewWindow(s, at.X, at.Y),
                };
                // A new one like the one picked before, if it's the same kind.
                if (Selected >= 0 && Selected < Floor.Openings.Count && Floor.Openings[Selected] is { } like && like.Kind == o.Kind)
                    o = like with { X = at.X, Y = at.Y };
                Commit(Floor with { Openings = [.. Floor.Openings, o] }, $"Add a {Tool.ToString().ToLowerInvariant()}");
                Select(Floor.Openings.Count - 1);
                _moving = Selected;
                _moveFrom = _design;
                break;
            }
            case PlanTool.Erase:
            {
                int i = OpeningAt(pt.Position);
                if (i >= 0)
                {
                    Select(-1);
                    Commit(Floor with { Openings = [.. Floor.Openings.Where((_, j) => j != i)] }, "Take away a door or window");
                    break;
                }
                _from = Snap(pt.Position);
                _to = _from.Value;
                break;
            }
            case PlanTool.Floor:
            {
                var (x, y) = ToPlan(pt.Position);
                var cell = new GridPt((int)Math.Floor(x), (int)Math.Floor(y));
                _erasing = Floor.Floor.Contains(cell);
                _from = cell;
                _to = cell;
                break;
            }
            default:
                _from = Snap(pt.Position);
                _to = _from.Value;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _inside = true;
        if (_pan is { } from)
        {
            _offset += p - from;
            _pan = p;
            _fitted = true;
            InvalidateVisual();
            return;
        }
        _mouse = p;
        if (_moving >= 0 && _moving < Floor.Openings.Count && OnWall(p) is { } at)
        {
            var o = Floor.Openings[_moving];
            if (Math.Abs(o.X - at.X) > 1e-9 || Math.Abs(o.Y - at.Y) > 1e-9)
            {
                var list = Floor.Openings.ToList();
                list[_moving] = o with { X = at.X, Y = at.Y };
                // Moved without a step for Undo each time: the step is made when it's let go.
                var levels = _design.Levels.ToList();
                levels[_level] = Floor with { Openings = list };
                _design = _design with { Levels = levels };
            }
        }
        else if (_from is { } a)
        {
            var (x, y) = ToPlan(p);
            _to = Tool switch
            {
                PlanTool.Wall or PlanTool.Erase => Constrain(a, x, y),
                PlanTool.Floor => new GridPt((int)Math.Floor(x), (int)Math.Floor(y)),
                _ => Snap(p),
            };
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _inside = false;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_pan is not null) { _pan = null; return; }
        if (_moving >= 0)
        {
            _moving = -1;
            // A step for Undo only if it moved (the design as it was before the drag goes back on).
            if (_moveFrom is { } was && !ReferenceEquals(was, _design)) Commit(Floor, "Move a door or window");
            _moveFrom = null;
            return;
        }
        if (_from is not { } a) return;
        var b = _to;
        _from = null;
        var walls = Floor.Walls.ToHashSet();
        switch (Tool)
        {
            case PlanTool.Wall:
            {
                int before = walls.Count;
                foreach (var w in Edges(a, b)) walls.Add(w);
                // No two diagonals crossing in one square.
                foreach (var w in Edges(a, b).Where(w => w.Diagonal))
                {
                    var cross = WallEdge.Of(new(w.A.X, w.B.Y), new(w.B.X, w.A.Y));
                    walls.Remove(cross);
                }
                if (walls.Count != before || walls.Count != Floor.Walls.Count) Commit(Floor with { Walls = [.. walls] }, "Draw a wall");
                break;
            }
            case PlanTool.Room:
                if (a.X == b.X || a.Y == b.Y) break;
                foreach (var w in RoomEdges(a, b)) walls.Add(w);
                Commit(Floor with { Walls = [.. walls] }, "Draw a room");
                break;
            case PlanTool.Erase:
            {
                var gone = a == b ? NearestEdge(_mouse) is { } one ? [one] : new List<WallEdge>() : Edges(a, b).ToList();
                if (gone.RemoveAll(w => !walls.Contains(w)) >= 0 && gone.Count > 0)
                {
                    foreach (var w in gone) walls.Remove(w);
                    Commit(Floor with { Walls = [.. walls] }, "Take away walls");
                }
                break;
            }
            case PlanTool.Floor:
            {
                var cells = Floor.Floor.ToHashSet();
                for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                    for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    {
                        if (x < 0 || y < 0 || x >= GridSize || y >= GridSize) continue;
                        if (_erasing) cells.Remove(new(x, y)); else cells.Add(new(x, y));
                    }
                Commit(Floor with { Floor = [.. cells.OrderBy(c => c.Y).ThenBy(c => c.X)] }, _erasing ? "Take up floor" : "Lay floor");
                break;
            }
        }
        InvalidateVisual();
    }

    WallEdge? NearestEdge(Point p)
    {
        var (x, y) = ToPlan(p);
        WallEdge? best = null;
        double bestD = 0.3;
        foreach (var w in Floor.Walls)
        {
            double ax = w.A.X, ay = w.A.Y, dx = w.B.X - ax, dy = w.B.Y - ay, len2 = dx * dx + dy * dy;
            double t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
            double px = ax + t * dx - x, py = ay + t * dy - y, d = Math.Sqrt(px * px + py * py);
            if (d < bestD) { bestD = d; best = w; }
        }
        return best;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var at = e.GetPosition(this);
        double k = Math.Pow(1.15, e.Delta.Y);
        double next = Math.Clamp(_scale * k, 6, 120);
        k = next / _scale;
        _offset = at - (at - _offset) * k;
        _scale = next;
        _fitted = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && Selected >= 0 && Selected < Floor.Openings.Count)
        {
            int i = Selected;
            Select(-1);
            Commit(Floor with { Openings = [.. Floor.Openings.Where((_, j) => j != i)] }, "Take away a door or window");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _from = null;
            _moving = -1;
            Select(-1);
            InvalidateVisual();
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None) { FitAll(); e.Handled = true; }
    }

    /// <summary>Zooms to show everything drawn on every floor (or the middle of the grid when there's nothing).</summary>
    public void FitAll()
    {
        var pts = _design.Levels.SelectMany(l => l.Walls.SelectMany(w => new[] { w.A, w.B }).Concat(l.Floor.SelectMany(f => new[] { f, f + new GridPt(1, 1) }))).ToList();
        double x0 = 0, y0 = 0, x1 = 12, y1 = 10;
        if (pts.Count > 0) { x0 = pts.Min(p => p.X); y0 = pts.Min(p => p.Y); x1 = pts.Max(p => p.X); y1 = pts.Max(p => p.Y); }
        x0 -= 2; y0 -= 2; x1 += 2; y1 += 2;
        double w = Math.Max(100, Bounds.Width), h = Math.Max(100, Bounds.Height);
        _scale = Math.Clamp(Math.Min(w / (x1 - x0), h / (y1 - y0)), 6, 80);
        _offset = new Vector((w - (x1 + x0) * _scale) / 2, (h - (y1 + y0) * _scale) / 2);
        _fitted = true;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted && e.NewSize.Width > 0) FitAll();
    }

    // ---- drawing ----

    static readonly Color[] RoomColours =
    [
        Color.FromRgb(0xE8, 0xD9, 0xB8), Color.FromRgb(0xCF, 0xE0, 0xC8), Color.FromRgb(0xD6, 0xD3, 0xEA), Color.FromRgb(0xF0, 0xCF, 0xC4),
        Color.FromRgb(0xC9, 0xDF, 0xE6), Color.FromRgb(0xEC, 0xE2, 0xC2),
    ];

    StreamGeometry Shape(PathsD area)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
        foreach (var path in area.Where(p => p.Count >= 3))
        {
            c.BeginFigure(ToScreen(path[0].x, path[0].y), true);
            foreach (var q in path.Skip(1)) c.LineTo(ToScreen(q.x, q.y));
            c.EndFigure(true);
        }
        return g;
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);

        // Past the edge of the grid, darker; on it, a line every square, darker every fifth.
        var edge = new Rect(ToScreen(0, 0), ToScreen(GridSize, GridSize));
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xDD, 0xD8, 0xCE)), bounds);
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xF6, 0xF3, 0xEC)), edge);
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x60, 0x58, 0x48)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x60, 0x58, 0x48)), 1);
        var (vx0, vy0) = ToPlan(new Point(0, 0));
        var (vx1, vy1) = ToPlan(new Point(bounds.Width, bounds.Height));
        int i0 = Math.Max(0, (int)Math.Floor(Math.Min(vx0, vy0))), i1 = Math.Min(GridSize, (int)Math.Ceiling(Math.Max(vx1, vy1)));
        for (int i = i0; i <= i1; i++)
        {
            var pen = i % 5 == 0 ? major : minor;
            ctx.DrawLine(pen, ToScreen(i, 0), ToScreen(i, GridSize));
            ctx.DrawLine(pen, ToScreen(0, i), ToScreen(GridSize, i));
        }

        // The floor below, faintly.
        if (_below is { } below)
        {
            var faint = new SolidColorBrush(Color.FromArgb(0x26, 0x50, 0x48, 0x40));
            foreach (var r in below.Rooms) ctx.DrawGeometry(faint, null, Shape(r.Area));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x50, 0x48, 0x40)), Math.Max(2, _scale * 0.12), new DashStyle([1.2, 1.2], 0), PenLineCap.Flat);
            foreach (var w in _design.Levels[_level - 1].Walls) ctx.DrawLine(pen, ToScreen(w.A.X, w.A.Y), ToScreen(w.B.X, w.B.Y));
        }

        // Floor laid by hand.
        var laid = new SolidColorBrush(Color.FromArgb(0xA0, 0xC8, 0xA8, 0x78));
        var laidPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x8A, 0x6A, 0x40)), 1);
        foreach (var f in Floor.Floor) ctx.DrawRectangle(laid, laidPen, new Rect(ToScreen(f.X, f.Y), ToScreen(f.X + 1, f.Y + 1)));
        if (_from is { } fa && Tool == PlanTool.Floor)
        {
            var r = new Rect(ToScreen(Math.Min(fa.X, _to.X), Math.Min(fa.Y, _to.Y)), ToScreen(Math.Max(fa.X, _to.X) + 1, Math.Max(fa.Y, _to.Y) + 1));
            ctx.DrawRectangle(new SolidColorBrush(_erasing ? Color.FromArgb(0x50, 0xD0, 0x40, 0x30) : Color.FromArgb(0x70, 0xC8, 0xA8, 0x78)), laidPen, r);
        }

        // The rooms.
        foreach (var r in _plan.Rooms)
        {
            var c = RoomColours[r.Index % RoomColours.Length];
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xC0, c.R, c.G, c.B)), null, Shape(r.Area));
        }

        // The walls, then the columns at every joint.
        double wallPx = Math.Max(4, _design.Style.BoxWalls ? _design.Style.WallMm / _design.SquareMm * _scale : 0.12 * _scale);
        var wallPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x4E, 0x42)), wallPx, lineCap: PenLineCap.Square);
        foreach (var w in Floor.Walls) ctx.DrawLine(wallPen, ToScreen(w.A.X, w.A.Y), ToScreen(w.B.X, w.B.Y));
        double colPx = Math.Max(6, _design.Style.ColumnMm / _design.SquareMm * _scale);
        var colBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x35, 0x2C));
        foreach (var (g, _) in _plan.Joints)
        {
            var c = ToScreen(g.X, g.Y);
            ctx.DrawRectangle(colBrush, null, new Rect(c.X - colPx / 2, c.Y - colPx / 2, colPx, colPx));
        }

        // Doors and windows.
        for (int i = 0; i < Floor.Openings.Count; i++) DrawOpening(ctx, Floor.Openings[i], i == Selected, wallPx);

        // Each wall's letter (as the pieces are named), inside the room beside its middle, when there's room.
        if (_scale >= 14)
        {
            var white = Brushes.White;
            foreach (var m in _made.Where(m => m.Level == _level))
            {
                double dx = m.B.X - m.A.X, dy = m.B.Y - m.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
                // The wall's front faces out on its right (A → B), so its inside is on the left: (dy, −dx).
                double off = 0.32 + wallPx / 2 / _scale;
                var mid = ToScreen((m.A.X + m.B.X) / 2.0 + dy / len * off, (m.A.Y + m.B.Y) / 2.0 - dx / len * off);
                var ft = new FormattedText(m.Letter, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.Bold), Math.Clamp(_scale * 0.32, 9, 14), m.Ok ? white : Brushes.Gold);
                var at = new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2);
                ctx.DrawRectangle(new SolidColorBrush(m.Ok ? Color.FromArgb(0xC0, 0x3E, 0x35, 0x2C) : Color.FromArgb(0xE0, 0xA0, 0x30, 0x20)), null, new Rect(at.X - 3, at.Y - 1, ft.Width + 6, ft.Height + 2), 3, 3);
                ctx.DrawText(ft, at);
            }
        }

        // What's being drawn.
        if (_from is { } a && Tool is PlanTool.Wall or PlanTool.Room or PlanTool.Erase)
        {
            var colour = Tool == PlanTool.Erase ? Color.FromArgb(0xC0, 0xD0, 0x40, 0x30) : Color.FromArgb(0xB0, 0x30, 0x80, 0xD0);
            var pen = new Pen(new SolidColorBrush(colour), wallPx, lineCap: PenLineCap.Square);
            var edges = Tool == PlanTool.Room ? RoomEdges(a, _to) : Edges(a, _to);
            foreach (var w in edges) ctx.DrawLine(pen, ToScreen(w.A.X, w.A.Y), ToScreen(w.B.X, w.B.Y));
            double len = Tool == PlanTool.Room ? 0 : Math.Sqrt(Math.Pow(_to.X - a.X, 2) + Math.Pow(_to.Y - a.Y, 2)) * _design.SquareMm;
            string size = Tool == PlanTool.Room ? $"{Math.Abs(_to.X - a.X)} × {Math.Abs(_to.Y - a.Y)} squares ({Math.Abs(_to.X - a.X) * _design.SquareMm:0} × {Math.Abs(_to.Y - a.Y) * _design.SquareMm:0} mm)" : $"{len:0} mm";
            Label(ctx, ToScreen(_to.X, _to.Y) + new Point(12, 8), size);
        }
        else if (_inside && _from is null && _moving < 0)
        {
            // Where a click would go.
            if (Tool is PlanTool.Wall or PlanTool.Room or PlanTool.Erase)
            {
                var g = Snap(_mouse);
                var c = ToScreen(g.X, g.Y);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 2), c, 5, 5);
            }
            else if (Tool is PlanTool.Door or PlanTool.Doorway or PlanTool.Window && OpeningAt(_mouse) < 0 && OnWall(_mouse) is { } at)
            {
                var s = _design.Style;
                var ghost = Tool == PlanTool.Door ? Opening.NewDoor(s, at.X, at.Y) : Tool == PlanTool.Doorway ? Opening.NewDoorway(s, at.X, at.Y) : Opening.NewWindow(s, at.X, at.Y);
                DrawOpening(ctx, ghost, false, wallPx, 0x80);
            }
        }

        if (Floor.Walls.Count == 0 && _from is null)
        {
            string hint = _level == 0
                ? "Pick Room and drag out a rectangle, or Wall and drag from point to point. Scroll to zoom; drag with the right button to move about."
                : "This floor is empty. The floor below shows faintly: draw this floor's walls on top of it (a room here gets a floor under it).";
            var ft = new FormattedText(hint, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13, UiKit.Brush("TextMuted")) { MaxTextWidth = Math.Max(80, bounds.Width - 60), TextAlignment = TextAlignment.Center };
            ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, 18));
        }
    }

    void Label(DrawingContext ctx, Point at, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, Brushes.White);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0x30, 0x30, 0x30)), null, new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4), 3, 3);
        ctx.DrawText(ft, at);
    }

    /// <summary>A door (a gap with the way it swings), a doorway (a gap) or a window (a pane across the wall), drawn where it sits.</summary>
    void DrawOpening(DrawingContext ctx, Opening o, bool selected, double wallPx, byte alpha = 0xFF)
    {
        // The wall it's on: its direction.
        // The wall it's on, turned (as the kit turns it) so the outside is on its right: the door swings in, away from it.
        var dir = new Vector(1, 0);
        foreach (var run0 in _plan.Runs)
            if (HouseKitMaker.Along(run0, o.X, o.Y) is not null)
            {
                var (left, right) = _plan.Sides(run0.Points[0], run0.Points[1]);
                var run = right != -1 && left == -1 ? run0.Reversed : run0;
                double dx = run.B.X - run.A.X, dy = run.B.Y - run.A.Y, l = Math.Sqrt(dx * dx + dy * dy);
                dir = new Vector(dx / l, dy / l);
                break;
            }
        var n = new Vector(-dir.Y, dir.X);
        double half = o.WidthMm / _design.SquareMm * _scale / 2, thick = wallPx / 2 + 1.5;
        var c = ToScreen(o.X, o.Y);
        Point P(double u, double v) => c + dir * u + n * v;
        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            g.BeginFigure(P(-half, -thick), true);
            g.LineTo(P(half, -thick));
            g.LineTo(P(half, thick));
            g.LineTo(P(-half, thick));
            g.EndFigure(true);
        }
        var fill = o.Kind switch
        {
            OpeningKind.Window => Color.FromArgb(alpha, 0xB8, 0xDC, 0xF0),
            OpeningKind.Door => Color.FromArgb(alpha, 0xC2, 0x8A, 0x52),
            _ => Color.FromArgb(alpha, 0xF6, 0xF3, 0xEC),
        };
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0x2A, 0x24, 0x1E)), 1.2);
        ctx.DrawGeometry(new SolidColorBrush(fill), edge, outline);
        if (o.Kind == OpeningKind.Window) ctx.DrawLine(edge, P(-half, 0), P(half, 0));
        if (o.Kind == OpeningKind.Door)
        {
            // The leaf swung open a quarter, into the building, from its hinge.
            double side = o.Hinge == Walls.HingeSide.Left ? -1 : 1;
            var hinge = P(side * half, -thick);
            var tip = hinge - n * (2 * half);
            var swing = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.7), 0x2A, 0x24, 0x1E)), 1, new DashStyle([3, 2], 0));
            ctx.DrawLine(edge, hinge, tip);
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(tip, false);
                g.ArcTo(P(-side * half, -thick), new Size(2 * half, 2 * half), 0, false, side > 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, swing, arc);
        }
        if (selected)
        {
            var sel = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 2, new DashStyle([3, 2], 0));
            ctx.DrawEllipse(null, sel, c, half + 6, half + 6);
        }
    }
}
