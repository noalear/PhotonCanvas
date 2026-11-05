using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>What dragging on the plan does.</summary>
public enum PlanTool { Select, Wall, Room, Erase, Door, Doorway, Window, Floor, Roof, Furnish, Stairs }

/// <summary>
/// The floor plan, seen from above on a grid of squares: draw walls along the grid lines (or corner to
/// corner), drag out a whole room at once, rub walls out, drop doors and windows into the walls and drag
/// them along, lay floor outside the rooms (a balcony), and put roofs over them. The floor below shows
/// faintly underneath, and the railings that will go round open floor show along its edges.
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
    /// <summary>The roof picked (its index in the design), or −1.</summary>
    public int SelectedRoof { get; private set; } = -1;
    /// <summary>The room picked: a point in it (squares), or null.</summary>
    public (double X, double Y)? SelectedRoom { get; private set; }
    /// <summary>The wall picked: one square of it (the whole straight wall it's in is picked), or null.</summary>
    public WallEdge? SelectedWall { get; private set; }

    /// <summary>The piece of furniture picked (its index on this floor), or −1.</summary>
    public int SelectedItem { get; private set; } = -1;
    /// <summary>The way up picked (its index on this floor), or −1.</summary>
    public int SelectedWay { get; private set; } = -1;
    /// <summary>What a click with Furniture puts down (null: nothing chosen yet).</summary>
    public FurnitureItem? ToPlace { get; set; }
    /// <summary>A click with Furniture puts down a banana for scale (instead of <see cref="ToPlace"/>).</summary>
    public bool BananaToPlace { get; set; }
    /// <summary>What a click with Stairs puts down.</summary>
    public WayKind WayToPlace { get; set; } = WayKind.Stairs;

    // Furniture or a way up being dragged: which, and where it was grabbed (squares from its middle).
    int _dragItem = -1, _dragWay = -1;
    // How the next piece of furniture is turned when none is picked (quarter turns).
    double _placeTurn;
    (double X, double Y) _grab;

    /// <summary>The column picked: the grid point it stands on, or null.</summary>
    public GridPt? SelectedColumn { get; private set; }

    /// <summary>The railing picked: one square of the floor's open edge it runs along, or null.</summary>
    public WallEdge? SelectedRailing { get; private set; }

    /// <summary>The stretch of open floor edge picked (with its railing, or with it taken away).</summary>
    public RailingRun? PickedRailing => SelectedRailing is { } e ? OpenRuns().FirstOrDefault(r => EdgesOf(r).Contains(e)) : null;

    /// <summary>Every stretch of this floor's edge with no wall on it, whether its railing is there or was taken away.</summary>
    public List<RailingRun> OpenRuns() => _below is { } below
        ? HouseKitMaker.RailingRuns(Floor with { Railings = true, NoRailing = [] }, below, _plan, _design.Style.WallSheetMm, _design.Style.ColumnMm)
        : [];

    /// <summary>The squares of edge a stretch of railing runs along.</summary>
    public static IEnumerable<WallEdge> EdgesOf(RailingRun r) => Enumerable.Range(0, r.Steps).Select(i => WallEdge.Of(r.A + new GridPt(r.Step.X * i, r.Step.Y * i), r.A + new GridPt(r.Step.X * (i + 1), r.Step.Y * (i + 1))));

    /// <summary>The straight wall picked, from column to column.</summary>
    public Run? PickedRun => SelectedWall is { } e ? RunOf(_plan, e) : null;

    /// <summary>The straight wall (column to column) a square of wall is in.</summary>
    public static Run? RunOf(PlanShape plan, WallEdge e)
    {
        foreach (var r in plan.Runs)
            for (int i = 0; i + 1 < r.Points.Count; i++)
                if (WallEdge.Of(r.Points[i], r.Points[i + 1]) == e) return r;
        return null;
    }

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
        _plan = design.Levels[_level].Shape();
        _below = _level > 0 ? design.Levels[_level - 1].Shape() : null;
        if (Selected >= design.Levels[_level].Openings.Count) Select(-1);
        if (SelectedRoof >= 0 && (SelectedRoof >= design.Roofs.Count || design.Roofs[SelectedRoof].Level != _level)) SelectRoof(-1);
        if (SelectedRoom is { } rm && _plan.RoomAt(rm.X, rm.Y) < 0) SelectRoom(null);
        if (SelectedWall is { } sw && !design.Levels[_level].Walls.Contains(sw)) SelectWall(null);
        if (SelectedRailing is not null && PickedRailing is null) SelectRailing(null);
        if (SelectedColumn is { } sc && !_plan.Joints.ContainsKey(sc)) SelectColumn(null);
        if (SelectedItem >= design.Levels[_level].Furniture.Count) SelectItem(-1);
        if (SelectedWay >= design.Levels[_level].WaysUp.Count) SelectWay(-1);
        if (!_fitted && Bounds.Width > 0) FitAll();
        InvalidateVisual();
    }

    /// <summary>The walls as last made, so each can be labelled with its letter.</summary>
    public void ShowMade(List<MadeWall> made)
    {
        _made = made;
        InvalidateVisual();
    }

    public void Select(int i) => Pick(i, -1, null, null, null, null);

    public void SelectRoof(int i) => Pick(-1, i, null, null, null, null);

    public void SelectRoom((double X, double Y)? at) => Pick(-1, -1, at, null, null, null);

    public void SelectWall(WallEdge? e) => Pick(-1, -1, null, e, null, null);

    public void SelectRailing(WallEdge? e) => Pick(-1, -1, null, null, e, null);

    public void SelectColumn(GridPt? at) => Pick(-1, -1, null, null, null, at);

    public void SelectItem(int i) => Pick(-1, -1, null, null, null, null, i);

    public void SelectWay(int i) => Pick(-1, -1, null, null, null, null, -1, i);

    /// <summary>Picks one thing (a door or window, a roof, a room, a wall, a railing, a column, furniture or a way up) and nothing else.</summary>
    void Pick(int opening, int roof, (double X, double Y)? room, WallEdge? wall, WallEdge? railing, GridPt? column, int item = -1, int way = -1)
    {
        if (Selected == opening && SelectedRoof == roof && SelectedRoom == room && SelectedWall == wall && SelectedRailing == railing && SelectedColumn == column
            && SelectedItem == item && SelectedWay == way) return;
        SelectedItem = item;
        SelectedWay = way;
        SelectedColumn = column;
        Selected = opening;
        SelectedRoof = roof;
        SelectedRoom = room;
        SelectedWall = wall;
        SelectedRailing = railing;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    Level Floor => _design.Levels[_level];

    /// <summary>What's picked, as one thing on one floor (null: nothing).</summary>
    public PlanPick? CurrentPick =>
        Selected >= 0 ? new PlanPick(_level, PickKind.Opening, Selected)
        : SelectedRoof >= 0 ? new PlanPick(_level, PickKind.Roof, SelectedRoof)
        : SelectedWall is { } w ? new PlanPick(_level, PickKind.Wall, Edge: w)
        : SelectedRailing is { } r ? new PlanPick(_level, PickKind.Railing, Edge: r)
        : SelectedColumn is { } c ? new PlanPick(_level, PickKind.Column, At: c)
        : SelectedItem >= 0 ? new PlanPick(_level, PickKind.Item, SelectedItem)
        : SelectedWay >= 0 ? new PlanPick(_level, PickKind.Way, SelectedWay)
        : SelectedRoom is { } m ? new PlanPick(_level, PickKind.Room, Point: m)
        : null;

    /// <summary>Picks one thing on the floor shown (null: nothing), as a click on it with Select would (the 3D view's clicks).</summary>
    public void PickThing(PlanPick? p)
    {
        switch (p)
        {
            case null: Pick(-1, -1, null, null, null, null); break;
            case { Kind: PickKind.Opening }: Select(p.Index); break;
            case { Kind: PickKind.Roof }: SelectRoof(p.Index); break;
            case { Kind: PickKind.Wall }: SelectWall(p.Edge); break;
            case { Kind: PickKind.Railing }: SelectRailing(p.Edge); break;
            case { Kind: PickKind.Column }: SelectColumn(p.At); break;
            case { Kind: PickKind.Item }: SelectItem(p.Index); break;
            case { Kind: PickKind.Way }: SelectWay(p.Index); break;
            case { Kind: PickKind.Room }: SelectRoom(p.Point); break;
        }
        InvalidateVisual();
    }

    // ---- copy and paste ----

    /// <summary>What was copied (furniture, a way up, a door or window): kept while the editor's open, for any floor.</summary>
    static object? _copied;

    /// <summary>The furniture, way up, or door or window picked, which can be copied; null if what's picked can't be.</summary>
    object? Copyable =>
        SelectedItem >= 0 && SelectedItem < Floor.Furniture.Count ? Floor.Furniture[SelectedItem]
        : SelectedWay >= 0 && SelectedWay < Floor.WaysUp.Count ? Floor.WaysUp[SelectedWay]
        : Selected >= 0 && Selected < Floor.Openings.Count ? Floor.Openings[Selected]
        : null;

    /// <summary>What's picked can be copied (it's furniture, a way up, or a door or window).</summary>
    public bool CanCopy => Copyable is not null;

    /// <summary>Something has been copied, to paste.</summary>
    public bool HasCopy => _copied is not null;

    /// <summary>Copies what's picked, to paste (Ctrl+C); false if it's nothing that can be copied.</summary>
    public bool CopyPicked()
    {
        if (Copyable is not { } c) return false;
        _copied = c;
        return true;
    }

    /// <summary>A copy of what's picked put down beside it, and picked (the Copy button, Ctrl+D); false if it can't be.</summary>
    public bool Duplicate() => Copyable is { } c && Put(c, atPointer: false);

    /// <summary>
    /// What was copied put down on this floor (Ctrl+V): at the pointer when it's over the plan (on a wall, for a door or
    /// window), otherwise beside the one copied; the copy is picked. False when there's nothing copied or no room for it.
    /// </summary>
    public bool Paste() => _copied is { } c && Put(c, atPointer: true);

    bool Put(object what, bool atPointer)
    {
        double sq = Math.Max(5, _design.SquareMm);
        bool pointer = atPointer && _inside;
        switch (what)
        {
            case PlacedItem it:
            {
                var (x, y) = pointer ? SnapItem(ToPlan(_mouse), false)
                    : Beside(it.X, it.Y, (x, y) => [HouseKitMaker.ItemArea(_design.Style, it with { X = x, Y = y }, sq)]);
                Commit(Floor with { Furniture = [.. Floor.Furniture, it with { X = x, Y = y }] }, "Copy furniture");
                SelectItem(Floor.Furniture.Count - 1);
                return true;
            }
            case PlacedWay w:
            {
                var (x, y) = pointer ? SnapItem(ToPlan(_mouse), false) : Beside(w.X, w.Y, (x, y) =>
                {
                    var (hatch, foot) = HouseKitMaker.WayArea(_design.Style, w with { X = x, Y = y }, sq);
                    var b = Clipper.GetBounds(new PathsD(hatch.Concat(foot)));
                    return [new RectD(b.left / sq, b.top / sq, b.right / sq, b.bottom / sq)];
                });
                Commit(Floor with { WaysUp = [.. Floor.WaysUp, w with { X = x, Y = y }] }, "Copy a way up");
                SelectWay(Floor.WaysUp.Count - 1);
                return true;
            }
            case Opening o:
            {
                var at = pointer ? OnWall(_mouse) : AlongWall(o, sq);
                if (at is not { } spot) return false;
                Commit(Floor with { Openings = [.. Floor.Openings, o with { X = spot.X, Y = spot.Y }] }, $"Copy a {(o.Kind == OpeningKind.Window ? "window" : o.Kind == OpeningKind.Door ? "door" : "doorway")}");
                Select(Floor.Openings.Count - 1);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Where a copy of a piece at (<paramref name="x"/>, <paramref name="y"/>) goes: just clear of it to its right, left,
    /// front or back, the first of those that stays in the same room clear of the furniture there (or just in the room;
    /// or, if there's no room for it, half a square off it). <paramref name="area"/> is the floor it would take there (squares).
    /// </summary>
    (double X, double Y) Beside(double x, double y, Func<double, double, List<RectD>> area)
    {
        var here = area(x, y);
        double l = here.Min(r => Math.Min(r.left, r.right)), t = here.Min(r => Math.Min(r.top, r.bottom));
        double w = here.Max(r => Math.Max(r.left, r.right)) - l, h = here.Max(r => Math.Max(r.top, r.bottom)) - t;
        double sx = Math.Ceiling((w + 0.125) * 8) / 8, sy = Math.Ceiling((h + 0.125) * 8) / 8;
        int room = _plan.RoomAt(x, y);
        var taken = Floor.Furniture.Select(f => HouseKitMaker.ItemArea(_design.Style, f, Math.Max(5, _design.SquareMm))).ToList();
        bool Free((double X, double Y) at, bool clearOfFurniture)
        {
            var rs = area(at.X, at.Y);
            foreach (var r in rs)
            {
                double x0 = Math.Min(r.left, r.right) + 0.02, x1 = Math.Max(r.left, r.right) - 0.02, y0 = Math.Min(r.top, r.bottom) + 0.02, y1 = Math.Max(r.top, r.bottom) - 0.02;
                if (x0 < 0 || y0 < 0 || x1 > GridSize || y1 > GridSize) return false;
                // Inside the room it was in (or out of every room, as it was), corner to corner.
                foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1), ((x0 + x1) / 2, (y0 + y1) / 2) })
                    if (_plan.RoomAt(cx, cy) != room) return false;
                if (clearOfFurniture && taken.Any(o => Math.Min(o.left, o.right) < x1 && Math.Max(o.left, o.right) > x0 && Math.Min(o.top, o.bottom) < y1 && Math.Max(o.top, o.bottom) > y0)) return false;
            }
            return true;
        }
        var tries = new[] { (x + sx, y), (x - sx, y), (x, y + sy), (x, y - sy) }.Select(p => SnapItem(p, false)).ToList();
        // Clear of the other furniture if it can be; else just in the room; else nearly on top of it, to drag away.
        foreach (bool clear in new[] { true, false })
            foreach (var p in tries)
                if (Free(p, clear)) return p;
        return SnapItem((x + 0.5, y + 0.5), false);
    }

    /// <summary>A spot further along the same wall for a copy of a door or window (either way), clear of the others; null if there's no room.</summary>
    (double X, double Y)? AlongWall(Opening o, double sq)
    {
        foreach (var run in _plan.Runs)
        {
            double ax = run.A.X, ay = run.A.Y, dx = run.B.X - ax, dy = run.B.Y - ay, len2 = dx * dx + dy * dy;
            if (run.Steps < 1 || len2 < 1e-9) continue;
            double t = ((o.X - ax) * dx + (o.Y - ay) * dy) / len2;
            double off = Math.Abs((o.X - ax) * dy - (o.Y - ay) * dx) / Math.Sqrt(len2);
            if (off > 0.05 || t < -0.01 || t > 1.01) continue;
            double here = t * run.Steps, need = Math.Max(1, Math.Ceiling((o.WidthMm / sq + 0.25) * 4) / 4);
            for (int n = 1; n <= run.Steps * 2; n++)
                foreach (int side in new[] { 1, -1 })
                {
                    double steps = here + side * need * n;
                    if (steps < 0.5 || steps > run.Steps - 0.5) continue;
                    double px = ax + dx * steps / run.Steps, py = ay + dy * steps / run.Steps;
                    bool clear = Floor.Openings.All(q => Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py)) * sq >= (q.WidthMm + o.WidthMm) / 2 + 1);
                    if (clear) return (px, py);
                }
        }
        return null;
    }

    void CommitDesign(HouseDesign design, string what)
    {
        _design = design;
        Changed?.Invoke(_design, what);
        InvalidateVisual();
    }

    void Commit(Level level, string what)
    {
        var levels = _design.Levels.ToList();
        levels[_level] = level;
        _design = _design with { Levels = levels };
        _plan = level.Shape();
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

    /// <summary>The roof on this floor under the pointer (the last one drawn, if they overlap), or −1.</summary>
    int RoofAt(Point p)
    {
        var (x, y) = ToPlan(p);
        for (int i = _design.Roofs.Count - 1; i >= 0; i--)
        {
            var r = _design.Roofs[i];
            if (r.Level == _level && x >= r.X0 && x <= r.X1 && y >= r.Y0 && y <= r.Y1) return i;
        }
        return -1;
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
                if (i >= 0)
                {
                    Select(i);
                    _moving = i;
                    _moveFrom = _design;
                    break;
                }
                // A wall or a railing when the click is on it; otherwise a roof, or the room under it (click again to swap between them).
                double reach = Math.Max(0.12, (WallPx / 2 + 5) / _scale);
                if (ColumnAt(pt.Position) is { } col) { SelectColumn(col); break; }
                if (ItemAt(pt.Position) is >= 0 and var item) { SelectItem(item); StartDrag(pt.Position, item, -1); break; }
                if (WayAt(pt.Position) is >= 0 and var way) { SelectWay(way); StartDrag(pt.Position, -1, way); break; }
                if (NearestEdge(pt.Position, reach) is { } wall) { SelectWall(wall); break; }
                if (RailingAt(pt.Position, reach) is { } rail) { SelectRailing(rail); break; }
                var (x, y) = ToPlan(pt.Position);
                bool inRoom = _plan.RoomAt(x, y) >= 0;
                int roof = RoofAt(pt.Position);
                if (roof >= 0 && !(SelectedRoof == roof && inRoom)) { SelectRoof(roof); break; }
                SelectRoom(inRoom ? (x, y) : null);
                break;
            }
            case PlanTool.Roof:
            {
                int i = RoofAt(pt.Position);
                if (i >= 0) { SelectRoof(i); break; }
                _from = Snap(pt.Position);
                _to = _from.Value;
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
            case PlanTool.Furnish:
            {
                if (ItemAt(pt.Position) is >= 0 and var item) { SelectItem(item); StartDrag(pt.Position, item, -1); break; }
                if (Placing() is not { } placing) break;
                var (x, y) = SnapItem(ToPlan(pt.Position), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                Commit(Floor with { Furniture = [.. Floor.Furniture, placing with { X = x, Y = y, Turn = NewTurn }] }, placing.Banana ? "Put down a banana for scale" : "Put in furniture");
                SelectItem(Floor.Furniture.Count - 1);
                StartDrag(pt.Position, Floor.Furniture.Count - 1, -1);
                break;
            }
            case PlanTool.Stairs:
            {
                if (WayAt(pt.Position) is >= 0 and var way) { SelectWay(way); StartDrag(pt.Position, -1, way); break; }
                var (x, y) = SnapItem(ToPlan(pt.Position), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                int turn = SelectedWay >= 0 && SelectedWay < Floor.WaysUp.Count ? Floor.WaysUp[SelectedWay].Turn : 0;
                Commit(Floor with { WaysUp = [.. Floor.WaysUp, new PlacedWay { X = x, Y = y, Turn = turn, Kind = WayToPlace }] }, "Put in a way up");
                SelectWay(Floor.WaysUp.Count - 1);
                StartDrag(pt.Position, -1, Floor.WaysUp.Count - 1);
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
        if (_dragItem >= 0 || _dragWay >= 0)
        {
            var (mx, my) = ToPlan(p);
            var (x, y) = SnapItem((mx - _grab.X, my - _grab.Y), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            var level = Floor;
            if (_dragItem >= 0 && _dragItem < level.Furniture.Count && level.Furniture[_dragItem] is var it && (it.X != x || it.Y != y))
            {
                var list = level.Furniture.ToList();
                list[_dragItem] = it with { X = x, Y = y };
                level = level with { Furniture = list };
            }
            else if (_dragWay >= 0 && _dragWay < level.WaysUp.Count && level.WaysUp[_dragWay] is var w && (w.X != x || w.Y != y))
            {
                var list = level.WaysUp.ToList();
                list[_dragWay] = w with { X = x, Y = y };
                level = level with { WaysUp = list };
            }
            if (!ReferenceEquals(level, Floor))
            {
                var levels = _design.Levels.ToList();
                levels[_level] = level;
                _design = _design with { Levels = levels };
            }
        }
        else if (_moving >= 0 && _moving < Floor.Openings.Count && OnWall(p) is { } at)
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
        if (_dragItem >= 0 || _dragWay >= 0)
        {
            bool item = _dragItem >= 0;
            _dragItem = _dragWay = -1;
            if (_moveFrom is { } was && !ReferenceEquals(was, _design)) Commit(Floor, item ? "Move furniture" : "Move a way up");
            _moveFrom = null;
            InvalidateVisual();
            return;
        }
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
            case PlanTool.Roof:
            {
                if (a.X == b.X || a.Y == b.Y) break;
                var roof = RoofArea.Over(_level, a, b, _design.Style);
                // Like the roof picked before: its shape, pitch and covering.
                if (SelectedRoof >= 0 && SelectedRoof < _design.Roofs.Count && _design.Roofs[SelectedRoof] is { } like)
                    roof = roof with { Kind = like.Kind, PitchDeg = like.PitchDeg, Covering = like.Covering, EaveMm = like.EaveMm, Window = like.Window, Turn = like.Kind == RoofKind.LeanTo ? like.Turn : roof.Turn };
                CommitDesign(_design with { Roofs = [.. _design.Roofs, roof] }, "Put on a roof");
                SelectRoof(_design.Roofs.Count - 1);
                break;
            }
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

    // ---- furniture and ways up ----

    void StartDrag(Point at, int item, int way)
    {
        var (x, y) = ToPlan(at);
        var (cx, cy) = item >= 0 ? (Floor.Furniture[item].X, Floor.Furniture[item].Y) : (Floor.WaysUp[way].X, Floor.WaysUp[way].Y);
        _grab = (x - cx, y - cy);
        _dragItem = item;
        _dragWay = way;
        _moveFrom ??= _design;
    }

    /// <summary>Where a piece goes: on eighths of a square (whole millimetres with Shift held).</summary>
    (double X, double Y) SnapItem((double X, double Y) p, bool free)
    {
        double step = free ? 1 / _design.SquareMm : 0.125;
        return (Math.Clamp(Math.Round(p.X / step) * step, 0, GridSize), Math.Clamp(Math.Round(p.Y / step) * step, 0, GridSize));
    }

    /// <summary>How a new piece of furniture is turned: as the one picked is, or as last turned.</summary>
    double NewTurn => SelectedItem >= 0 && SelectedItem < Floor.Furniture.Count ? Floor.Furniture[SelectedItem].Turn : _placeTurn;

    static double Quarters(double turn) => Math.Round((((turn % 4) + 4) % 4) * 2) / 2 % 4;

    /// <summary>
    /// The R key: turns the furniture picked (or being put down) 45° clockwise seen from above, or back
    /// with <paramref name="back"/>. Stairs and ladders only go square to the grid, so they turn a quarter.
    /// </summary>
    public void TurnKey(bool back)
    {
        double by = back ? 0.5 : -0.5;
        bool dragging = _dragItem >= 0 || _dragWay >= 0;
        if (SelectedItem >= 0 && SelectedItem < Floor.Furniture.Count || SelectedWay >= 0 && SelectedWay < Floor.WaysUp.Count) TurnPicked(by, commit: !dragging);
        else if (Tool == PlanTool.Furnish)
        {
            _placeTurn = Quarters(_placeTurn + by);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Turns the furniture or way up picked: by quarter turns, +1 anticlockwise (seen from above), −1
    /// clockwise; furniture also by halves (45°), and a way up a whole quarter whichever way it's asked.
    /// </summary>
    public void TurnPicked(double by, bool commit = true)
    {
        var level = Floor;
        if (SelectedItem >= 0 && SelectedItem < level.Furniture.Count)
        {
            var list = level.Furniture.ToList();
            list[SelectedItem] = list[SelectedItem] with { Turn = Quarters(list[SelectedItem].Turn + by) };
            _placeTurn = list[SelectedItem].Turn;
            level = level with { Furniture = list };
        }
        else if (SelectedWay >= 0 && SelectedWay < level.WaysUp.Count)
        {
            var list = level.WaysUp.ToList();
            int q = by > 0 ? 1 : -1;
            list[SelectedWay] = list[SelectedWay] with { Turn = (((list[SelectedWay].Turn + q) % 4) + 4) % 4 };
            level = level with { WaysUp = list };
        }
        else return;
        if (commit) { Commit(level, "Turn"); return; }
        // While dragging: the step for Undo is made when it's let go.
        var levels = _design.Levels.ToList();
        levels[_level] = level;
        _design = _design with { Levels = levels };
        InvalidateVisual();
    }

    /// <summary>The piece of furniture under a point (the one on top), or −1.</summary>
    int ItemAt(Point p)
    {
        var (x, y) = ToPlan(p);
        for (int i = Floor.Furniture.Count - 1; i >= 0; i--)
        {
            var outline = HouseKitMaker.ItemOutline(_design.Style, Floor.Furniture[i], _design.SquareMm);
            if (Clipper.PointInPolygon(new PointD(x, y), outline) != PointInPolygonResult.IsOutside) return i;
        }
        return -1;
    }

    /// <summary>The way up under a point (its stairs or its opening above), or −1.</summary>
    int WayAt(Point p)
    {
        var (x, y) = ToPlan(p);
        var at = new PointD(x * _design.SquareMm, y * _design.SquareMm);
        for (int i = Floor.WaysUp.Count - 1; i >= 0; i--)
        {
            var (hatch, foot) = HouseKitMaker.WayArea(_design.Style, Floor.WaysUp[i], _design.SquareMm);
            if (hatch.Concat(foot).Any(q => Clipper.PointInPolygon(at, q) != PointInPolygonResult.IsOutside)) return i;
        }
        return -1;
    }

    /// <summary>How wide walls are drawn (px).</summary>
    double WallPx => Math.Max(4, _design.Style.BoxWalls ? _design.Style.WallMm / _design.SquareMm * _scale : 0.12 * _scale);

    /// <summary>The column under a point, or null.</summary>
    GridPt? ColumnAt(Point p)
    {
        var (x, y) = ToPlan(p);
        double r = Math.Max(5, _design.Style.ColumnMm / _design.SquareMm * _scale / 2 + 2) / _scale;
        GridPt? best = null;
        double bestD = r;
        foreach (var g in _plan.Joints.Keys)
        {
            double d = Math.Max(Math.Abs(g.X - x), Math.Abs(g.Y - y));
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    /// <summary>The square of open floor edge (railed or not) under a point, or null.</summary>
    WallEdge? RailingAt(Point p, double reach)
    {
        var (x, y) = ToPlan(p);
        WallEdge? best = null;
        double bestD = reach;
        foreach (var e in OpenRuns().SelectMany(EdgesOf))
        {
            double ax = e.A.X, ay = e.A.Y, dx = e.B.X - ax, dy = e.B.Y - ay, len2 = dx * dx + dy * dy;
            double t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
            double px = ax + t * dx - x, py = ay + t * dy - y, d = Math.Sqrt(px * px + py * py);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    WallEdge? NearestEdge(Point p, double reach = 0.3)
    {
        var (x, y) = ToPlan(p);
        WallEdge? best = null;
        double bestD = reach;
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
        // Turning what's being dragged.
        if ((_dragItem >= 0 || _dragWay >= 0) && Math.Abs(e.Delta.Y) > 0.01)
        {
            TurnPicked(e.Delta.Y > 0 ? 1 : -1, commit: false);
            e.Handled = true;
            return;
        }
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
        if (e.Key is Key.Delete or Key.Back && SelectedRoof >= 0 && SelectedRoof < _design.Roofs.Count)
        {
            int i = SelectedRoof;
            SelectRoof(-1);
            CommitDesign(_design with { Roofs = [.. _design.Roofs.Where((_, j) => j != i)] }, "Take away a roof");
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && Selected >= 0 && Selected < Floor.Openings.Count)
        {
            int i = Selected;
            Select(-1);
            Commit(Floor with { Openings = [.. Floor.Openings.Where((_, j) => j != i)] }, "Take away a door or window");
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && SelectedItem >= 0 && SelectedItem < Floor.Furniture.Count)
        {
            int i = SelectedItem;
            SelectItem(-1);
            Commit(Floor with { Furniture = [.. Floor.Furniture.Where((_, j) => j != i)] }, "Take away furniture");
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && SelectedWay >= 0 && SelectedWay < Floor.WaysUp.Count)
        {
            int i = SelectedWay;
            SelectWay(-1);
            Commit(Floor with { WaysUp = [.. Floor.WaysUp.Where((_, j) => j != i)] }, "Take away a way up");
            e.Handled = true;
        }
        else if (e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemComma or Key.OemPeriod && (SelectedItem >= 0 || SelectedWay >= 0))
        {
            TurnPicked(e.Key is Key.OemOpenBrackets or Key.OemComma ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _from = null;
            _moving = -1;
            _dragItem = _dragWay = -1;
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
        // A floor kept to its yard: where its floor reaches, the edge dashed.
        if (Floor.Yard is not null && HouseKitMaker.FloorArea(Floor, _below, _plan) is { Count: > 0 } yard)
        {
            var yardPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x6A, 0x50, 0x30)), Math.Max(1.5, _scale * 0.06), new DashStyle([4, 3], 0));
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA8, 0x78)), yardPen, Shape(yard));
        }
        if (_from is { } fa && Tool == PlanTool.Floor)
        {
            var r = new Rect(ToScreen(Math.Min(fa.X, _to.X), Math.Min(fa.Y, _to.Y)), ToScreen(Math.Max(fa.X, _to.X) + 1, Math.Max(fa.Y, _to.Y) + 1));
            ctx.DrawRectangle(new SolidColorBrush(_erasing ? Color.FromArgb(0x50, 0xD0, 0x40, 0x30) : Color.FromArgb(0x70, 0xC8, 0xA8, 0x78)), laidPen, r);
        }

        // The rooms, with the floor pattern of any that has its own.
        foreach (var r in _plan.Rooms)
        {
            var c = RoomColours[r.Index % RoomColours.Length];
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xC0, c.R, c.G, c.B)), null, Shape(r.Area));
            if (_scale >= 14 && HouseKitMaker.RoomTexture(Floor, _plan, r) is { } tex)
            {
                var b = Walls.Geo.Bounds(r.Area);
                var ft = new FormattedText(tex, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Italic), Math.Clamp(_scale * 0.3, 9, 13), new SolidColorBrush(Color.FromRgb(0x6A, 0x5A, 0x48)));
                var mid = ToScreen((b.left + b.right) / 2, (b.top + b.bottom) / 2 + 0.8);
                ctx.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
            }
        }

        // The walls, then the columns at every joint.
        double wallPx = WallPx;
        var wallPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x4E, 0x42)), wallPx, lineCap: PenLineCap.Square);
        // Walls with a look of their own a little lighter.
        var ownPen = new Pen(new SolidColorBrush(Color.FromRgb(0x8C, 0x74, 0x52)), wallPx, lineCap: PenLineCap.Square);
        foreach (var w in Floor.Walls) ctx.DrawLine(Floor.LookOf(w) is null ? wallPen : ownPen, ToScreen(w.A.X, w.A.Y), ToScreen(w.B.X, w.B.Y));
        if (PickedRun is { } picked0)
        {
            var sel = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), wallPx + 5, lineCap: PenLineCap.Square);
            ctx.DrawLine(sel, ToScreen(picked0.A.X, picked0.A.Y), ToScreen(picked0.B.X, picked0.B.Y));
            ctx.DrawLine(Floor.LookOf(SelectedWall!.Value) is null ? wallPen : ownPen, ToScreen(picked0.A.X, picked0.A.Y), ToScreen(picked0.B.X, picked0.B.Y));
        }
        double colPx = Math.Max(6, _design.Style.ColumnMm / _design.SquareMm * _scale);
        var colBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x35, 0x2C));
        var colPick = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 3);
        var ownCol = new SolidColorBrush(Color.FromRgb(0x6E, 0x58, 0x3C));
        foreach (var (g, dirs) in _plan.Joints)
        {
            // Drawn its own shape, at least a few pixels across.
            var foot = HouseKitMaker.ColumnFoot(Floor, _design.Style, g, dirs, _design.SquareMm);
            double k = Math.Max(1, colPx / Math.Max(1e-6, _design.Style.ColumnMm / _design.SquareMm * _scale));
            var shape = new PathsD { new PathD(foot.Select(p => new PointD(g.X + (p.x / _design.SquareMm - g.X) * k, g.Y + (p.y / _design.SquareMm - g.Y) * k))) };
            ctx.DrawGeometry(Floor.PickAt(g) is null ? colBrush : ownCol, SelectedColumn == g ? colPick : null, Shape(shape));
        }

        // Doors and windows.
        for (int i = 0; i < Floor.Openings.Count; i++) DrawOpening(ctx, Floor.Openings[i], i == Selected, wallPx);

        // The ways up from here and the openings coming up from the floor below, then the furniture.
        DrawWays(ctx);
        DrawFurniture(ctx);

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

        // The roofs on this floor, and the railings round its open floor.
        DrawRailings(ctx);
        for (int i = 0; i < _design.Roofs.Count; i++)
            if (_design.Roofs[i].Level == _level) DrawRoof(ctx, _design.Roofs[i], i, i == SelectedRoof);
            else if (_design.Roofs[i].Level == _level - 1) DrawRoofBelow(ctx, _design.Roofs[i]);
        if (SelectedRoom is { } picked && _plan.RoomAt(picked.X, picked.Y) is >= 0 and var ri)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 2.5, new DashStyle([3, 2], 0)), Shape(_plan.Rooms[ri].Area));

        // What's being drawn.
        if (_from is { } ra && Tool == PlanTool.Roof)
        {
            var r = new Rect(ToScreen(Math.Min(ra.X, _to.X), Math.Min(ra.Y, _to.Y)), ToScreen(Math.Max(ra.X, _to.X), Math.Max(ra.Y, _to.Y)));
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0xA0, 0x50, 0x38)), new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 2, new DashStyle([3, 2], 0)), r);
            Label(ctx, ToScreen(_to.X, _to.Y) + new Point(12, 8), $"Roof {Math.Abs(_to.X - ra.X)} × {Math.Abs(_to.Y - ra.Y)} squares");
        }
        else if (_from is { } a && Tool is PlanTool.Wall or PlanTool.Room or PlanTool.Erase)
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
            if (Tool is PlanTool.Wall or PlanTool.Room or PlanTool.Erase or PlanTool.Roof)
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

    static readonly Color RoofColour = Color.FromRgb(0x9A, 0x4E, 0x36);

    static readonly IBrush Picked = new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0));

    /// <summary>Which way a quarter-turned thing's front (or its climb, for a way up turned from the right) points on the plan.</summary>
    static (double X, double Y) Facing(int turn, bool climb = false) => (((((turn % 4) + 4) % 4) + (climb ? 1 : 0)) % 4) switch
    {
        0 => (0, 1), 1 => (1, 0), 2 => (0, -1), _ => (-1, 0),
    };

    /// <summary>A piece of furniture's pieces seen from above, ready to draw (mm about its middle, front towards +y), each shaded by how high it stands.</summary>
    sealed record TopDrawing(List<(Geometry Shape, IBrush Fill)> Parts);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemLook, TopDrawing> TopDrawings = [];

    static TopDrawing DrawingOf(ItemLook look)
    {
        if (TopDrawings.TryGetValue(look, out var known)) return known;
        double lowest = look.Parts.Count > 0 ? look.Parts.Min(p => p.Top) : 0, highest = look.Parts.Count > 0 ? look.Parts.Max(p => p.Top) : 1;
        var parts = new List<(Geometry, IBrush)>();
        foreach (var part in look.Parts)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(Avalonia.Media.FillRule.EvenOdd);
                foreach (var path in part.Shape.Where(q => q.Count >= 3))
                {
                    c.BeginFigure(new Point(path[0].x, path[0].y), true);
                    foreach (var q in path.Skip(1)) c.LineTo(new Point(q.x, q.y));
                    c.EndFigure(true);
                }
            }
            // Darker low down, lighter higher up, so the tops stand out from what's under them.
            double t = highest - lowest < 1e-6 ? 1 : (part.Top - lowest) / (highest - lowest);
            byte L(double lo, double hi) => (byte)Math.Round(lo + (hi - lo) * t);
            parts.Add((g, new SolidColorBrush(Color.FromArgb(0xB4, L(0x86, 0xE6), L(0x5E, 0xC8), L(0x36, 0x98)))));
        }
        var made = new TopDrawing(parts);
        TopDrawings.AddOrUpdate(look, made);
        return made;
    }

    /// <summary>
    /// One piece of furniture seen from above, as it's turned: the floor it takes, its pieces (the furniture
    /// maker's, lowest first), its front as a dark edge with a notch pointing out, and its outline.
    /// </summary>
    void DrawItem(DrawingContext ctx, PlacedItem p, bool picked, bool ghost)
    {
        double sq = _design.SquareMm;
        double px = _scale / sq;   // screen pixels a millimetre
        if (p.Banana)
        {
            // A banana for scale: a yellow oval as long as a real one, turned as it lies.
            double ba = HouseKitMaker.TurnDeg(p.Turn) * Math.PI / 180, bc = Math.Cos(ba), bs = Math.Sin(ba);
            var bat = ToScreen(p.X, p.Y);
            using (ctx.PushOpacity(ghost ? 0.55 : 1))
            using (ctx.PushTransform(new Matrix(bc * px, -bs * px, bs * px, bc * px, bat.X, bat.Y)))
                ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xE0, 0xF2, 0xD2, 0x3C)), new Pen(picked || ghost ? Picked : new SolidColorBrush(Color.FromRgb(0x5A, 0x3E, 0x22)), (picked ? 2.5 : 1.2) / px),
                    new EllipseGeometry(new Rect(-HouseKitMaker.BananaLongMm / 2, -HouseKitMaker.BananaDeepMm / 2, HouseKitMaker.BananaLongMm, HouseKitMaker.BananaDeepMm)));
            return;
        }
        var look = HouseKitMaker.Look(_design.Style, p.Item);
        var outline = HouseKitMaker.ItemOutline(_design.Style, p, sq);
        var shape = Shape([outline]);
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(ghost ? (byte)0x50 : (byte)0x70, 0xB8, 0x8A, 0x58)), null, shape);
        // Its pieces, once there's room to make them out.
        if (Math.Min(look.W, look.D) * px >= 6 && look.Parts.Count > 0)
        {
            double a = HouseKitMaker.TurnDeg(p.Turn) * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
            var at = ToScreen(p.X, p.Y);
            // Plan mm about its middle → screen: turned as on the plan (x c + y s, −x s + y c), scaled, moved.
            var m = new Matrix(c * px, -sn * px, sn * px, c * px, at.X, at.Y);
            var line = new Pen(new SolidColorBrush(Color.FromArgb(0xD8, 0x4A, 0x32, 0x1C)), 0.9 / px);
            using (ctx.PushOpacity(ghost ? 0.55 : 1))
            using (ctx.PushTransform(m))
                foreach (var (g, fill) in DrawingOf(look).Parts) ctx.DrawGeometry(fill, line, g);
        }
        var edge = picked ? new Pen(Picked, 2.5) : ghost ? new Pen(Picked, 1.5, new DashStyle([3, 2], 0)) : new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x3E, 0x22)), 1.2);
        ctx.DrawGeometry(null, edge, shape);
        // Its front: the edge from its front left to front right, dark, with a notch pointing out of it.
        var fl = ToScreen(outline[0].x, outline[0].y);
        var fr = ToScreen(outline[1].x, outline[1].y);
        var front = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x12)), Math.Max(2.5, _scale * 0.08), lineCap: PenLineCap.Flat);
        ctx.DrawLine(front, fl, fr);
        double ex = fr.X - fl.X, ey = fr.Y - fl.Y, len = Math.Sqrt(ex * ex + ey * ey);
        if (len < 4) return;
        // Outwards from the front: the front edge's direction turned towards the side away from the middle.
        double ox = ey / len, oy = -ex / len;
        var mid = new Point((fl.X + fr.X) / 2, (fl.Y + fr.Y) / 2);
        var centre = ToScreen(p.X, p.Y);
        if ((mid.X - centre.X) * ox + (mid.Y - centre.Y) * oy < 0) (ox, oy) = (-ox, -oy);
        double h = Math.Clamp(_scale * 0.16, 4, 9), w = Math.Min(h, len / 2 - 1);
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(mid.X + ex / len * w, mid.Y + ey / len * w), true);
            g.LineTo(new Point(mid.X + ox * h, mid.Y + oy * h));
            g.LineTo(new Point(mid.X - ex / len * w, mid.Y - ey / len * w));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(picked ? Picked : new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x12)), null, tri);
    }

    void DrawFurniture(DrawingContext ctx)
    {
        int hover = _inside && _from is null ? ItemAt(_mouse) : -1;
        for (int i = 0; i < Floor.Furniture.Count; i++) DrawItem(ctx, Floor.Furniture[i], i == SelectedItem, false);
        // What a click would put down, faintly.
        if (Tool == PlanTool.Furnish && Placing() is { } what && _inside && _from is null && _dragItem < 0 && hover < 0)
        {
            var (x, y) = SnapItem(ToPlan(_mouse), false);
            DrawItem(ctx, what with { X = x, Y = y, Turn = NewTurn }, false, true);
        }
        // The name of the one under the mouse (or the one picked), on a label clear of its pieces.
        int named = hover >= 0 ? hover : SelectedItem;
        if (named >= 0 && named < Floor.Furniture.Count && _scale >= 12)
        {
            var p = Floor.Furniture[named];
            var r = HouseKitMaker.ItemArea(_design.Style, p, _design.SquareMm);
            var ft = new FormattedText(HouseKitMaker.PlacedName(p), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), Math.Clamp(_scale * 0.3, 10, 13), Brushes.White);
            var top = ToScreen((r.left + r.right) / 2, r.top);
            var box = new Rect(top.X - ft.Width / 2 - 5, top.Y - ft.Height - 8, ft.Width + 10, ft.Height + 4);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE0, 0x3A, 0x26, 0x12)), null, box, 4, 4);
            ctx.DrawText(ft, new Point(box.X + 5, box.Y + 2));
        }
    }

    /// <summary>What a click with Furniture puts down (where and turned still to set), or null when nothing's chosen.</summary>
    PlacedItem? Placing() => BananaToPlace ? new PlacedItem { Banana = true } : ToPlace is { } what ? new PlacedItem { Item = what } : null;

    void DrawWays(DrawingContext ctx)
    {
        double sq = _design.SquareMm;
        StreamGeometry Mm(PathsD mm) => Shape(Walls.Geo.Map(mm, q => new PointD(q.x / sq, q.y / sq)));
        // Openings from the floor below: holes in this floor.
        if (_level > 0)
            foreach (var w in _design.Levels[_level - 1].WaysUp)
            {
                var (hatch, _) = HouseKitMaker.WayArea(_design.Style, w, sq);
                ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x2A, 0x24)), new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x1A, 0x14)), 1.5), Mm(hatch));
            }
        var stair = new SolidColorBrush(Color.FromArgb(0xD0, 0xA8, 0x9C, 0x8C));
        var line = new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x46, 0x3C)), 1.2);
        var dashed = new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x46, 0x3C)), 1.5, new DashStyle([3, 2], 0));
        for (int i = 0; i < Floor.WaysUp.Count; i++)
        {
            var w = Floor.WaysUp[i];
            var (hatch, foot) = HouseKitMaker.WayArea(_design.Style, w, sq);
            ctx.DrawGeometry(stair, i == SelectedWay ? new Pen(Picked, 2.5) : line, Mm(foot));
            ctx.DrawGeometry(null, i == SelectedWay ? new Pen(Picked, 1.5, new DashStyle([3, 2], 0)) : dashed, Mm(hatch));
            // Which way it climbs.
            var (dx, dy) = Facing(w.Turn, climb: true);
            var b = Walls.Geo.Bounds(foot);
            var mid = ToScreen((b.left + b.right) / 2 / sq, (b.top + b.bottom) / 2 / sq);
            double len = Math.Max(8, Math.Min(b.right - b.left, b.bottom - b.top) / sq * _scale * 0.35);
            var tip = new Point(mid.X + dx * len, mid.Y + dy * len);
            var tail = new Point(mid.X - dx * len, mid.Y - dy * len);
            var arrow = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x1A)), 2);
            ctx.DrawLine(arrow, tail, tip);
            ctx.DrawLine(arrow, tip, new Point(tip.X - dx * 6 - dy * 5, tip.Y - dy * 6 + dx * 5));
            ctx.DrawLine(arrow, tip, new Point(tip.X - dx * 6 + dy * 5, tip.Y - dy * 6 - dx * 5));
        }
        if (Tool == PlanTool.Stairs && _inside && _from is null && _dragWay < 0 && WayAt(_mouse) < 0)
        {
            var (x, y) = SnapItem(ToPlan(_mouse), false);
            int turn = SelectedWay >= 0 && SelectedWay < Floor.WaysUp.Count ? Floor.WaysUp[SelectedWay].Turn : 0;
            var (hatch, foot) = HouseKitMaker.WayArea(_design.Style, new PlacedWay { X = x, Y = y, Turn = turn, Kind = WayToPlace }, sq);
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x60, 0xA8, 0x9C, 0x8C)), new Pen(Picked, 1.5, new DashStyle([3, 2], 0)), Mm(Walls.Geo.Union(foot, hatch)));
        }
    }

    /// <summary>A roof seen from above: its outline, its ridge (and hips), or which way a lean-to slopes, and its number.</summary>
    void DrawRoof(DrawingContext ctx, RoofArea r, int index, bool selected)
    {
        var tl = ToScreen(r.X0, r.Y0);
        var br = ToScreen(r.X1, r.Y1);
        var rect = new Rect(tl, br);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, RoofColour.R, RoofColour.G, RoofColour.B)), null, rect);
        var pen = new Pen(new SolidColorBrush(RoofColour), 2);
        var thin = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, RoofColour.R, RoofColour.G, RoofColour.B)), 1.5);
        ctx.DrawRectangle(null, pen, rect);
        var mid = rect.Center;
        bool across = r.RidgeAcross;
        switch (r.Kind)
        {
            case RoofKind.Gable:
                ctx.DrawLine(pen, across ? new Point(rect.Left, mid.Y) : new Point(mid.X, rect.Top), across ? new Point(rect.Right, mid.Y) : new Point(mid.X, rect.Bottom));
                break;
            case RoofKind.Hip:
            {
                // The ridge along the longer side, the hips down to the corners.
                bool wide = rect.Width >= rect.Height;
                double inset = (wide ? rect.Height : rect.Width) / 2;
                var a = wide ? new Point(rect.Left + inset, mid.Y) : new Point(mid.X, rect.Top + inset);
                var b = wide ? new Point(rect.Right - inset, mid.Y) : new Point(mid.X, rect.Bottom - inset);
                ctx.DrawLine(pen, a, b);
                ctx.DrawLine(thin, rect.TopLeft, a); ctx.DrawLine(thin, rect.BottomLeft, wide ? a : b);
                ctx.DrawLine(thin, rect.TopRight, wide ? b : a); ctx.DrawLine(thin, rect.BottomRight, b);
                break;
            }
            case RoofKind.LeanTo:
            {
                // An arrow down the slope.
                var (_, f) = HouseKitMaker.Directions(r.Turn);
                double len = (f.X != 0 ? rect.Width : rect.Height) * 0.3;
                var from = mid - new Vector(f.X, f.Y) * len;
                var to = mid + new Vector(f.X, f.Y) * len;
                ctx.DrawLine(pen, from, to);
                var side = new Vector(-f.Y, f.X) * 6;
                var back = new Vector(f.X, f.Y) * 9;
                ctx.DrawLine(pen, to, to - back + side);
                ctx.DrawLine(pen, to, to - back - side);
                break;
            }
            default:
                ctx.DrawRectangle(null, thin, rect.Deflate(Math.Min(8, Math.Min(rect.Width, rect.Height) / 6)));
                break;
        }
        if (selected) ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0)), 2.5, new DashStyle([3, 2], 0)), rect.Inflate(4));
        if (_scale >= 12)
        {
            string text = $"Roof {index + 1}";
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.Bold), 11, Brushes.White);
            var at = new Point(rect.Left + 5, rect.Top + 4);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD0, RoofColour.R, RoofColour.G, RoofColour.B)), null, new Rect(at.X - 3, at.Y - 1, ft.Width + 6, ft.Height + 2), 3, 3);
            ctx.DrawText(ft, at);
        }
    }

    /// <summary>A roof on the floor below, faintly: this floor mustn't stand on it.</summary>
    void DrawRoofBelow(DrawingContext ctx, RoofArea r) =>
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x90, RoofColour.R, RoofColour.G, RoofColour.B)), 1.5, new DashStyle([4, 3], 0)), new Rect(ToScreen(r.X0, r.Y0), ToScreen(r.X1, r.Y1)));

    /// <summary>The railings that go round this floor's open edges: a rail with balusters.</summary>
    void DrawRailings(DrawingContext ctx)
    {
        if (_below is not { } below) return;
        var runs = HouseKitMaker.RailingRuns(Floor, below, _plan, _design.Style.WallSheetMm, _design.Style.ColumnMm);
        var rail = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x6A, 0x40)), Math.Max(2, _scale * 0.06));
        // Where a railing was taken away, faintly, so it can be picked and put back.
        var gone = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x8A, 0x6A, 0x40)), 1.5, new DashStyle([2, 3], 0));
        foreach (var e in Floor.NoRailing) ctx.DrawLine(gone, ToScreen(e.A.X, e.A.Y), ToScreen(e.B.X, e.B.Y));
        if (PickedRailing is { } picked)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x30, 0x80, 0xD0)), Math.Max(6, _scale * 0.2), lineCap: PenLineCap.Square), ToScreen(picked.A.X, picked.A.Y), ToScreen(picked.B.X, picked.B.Y));
        var post = new SolidColorBrush(Color.FromRgb(0x6A, 0x4E, 0x2E));
        double r = Math.Max(1.5, _scale * 0.05);
        foreach (var run in runs)
        {
            var a = ToScreen(run.A.X, run.A.Y);
            var b = ToScreen(run.B.X, run.B.Y);
            ctx.DrawLine(rail, a, b);
            int dots = Math.Max(1, (int)(run.Steps * (run.Diagonal ? 1.41 : 1) * 4));
            for (int i = 1; i < dots; i++) ctx.DrawEllipse(post, null, a + (b - a) * (i / (double)dots), r, r);
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
        if (o.Kind == OpeningKind.Door && o.Opens)
        {
            // The leaf swung open a quarter, out of the building, from its hinge (left or right seen from outside).
            double side = o.Hinge == Walls.HingeSide.Left ? -1 : 1;
            var hinge = P(side * half, thick);
            var tip = hinge + n * (2 * half);
            var swing = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.7), 0x2A, 0x24, 0x1E)), 1, new DashStyle([3, 2], 0));
            ctx.DrawLine(edge, hinge, tip);
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(tip, false);
                g.ArcTo(P(-side * half, thick), new Size(2 * half, 2 * half), 0, false, side > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
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
