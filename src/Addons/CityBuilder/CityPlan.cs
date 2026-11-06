using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.App;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What dragging on the map does.</summary>
public enum CityTool { Select, Ground, Fill, Building, Place, Fence, Prop, Erase, Road, Label }

/// <summary>
/// The map seen from above, on its grid, over the battle map it was read from (if any): the ground, each
/// building's walls, doors and roof, fences and the trees and other things. Zoom with the wheel; move it with
/// the middle button, or Shift and drag. With the tool chosen: pick and drag buildings (R or the wheel while
/// dragging turns one, Delete takes it away), paint or fill the ground, drag out a new building's footprint,
/// put down a saved building, draw fences along the grid lines, drop trees and the like, lay roads, write
/// labels, or rub things out. The right button rubs out with the painting tools. With Select, drag a box to
/// pick everything in it, Ctrl and click to add to what's picked, and copy, paste, turn and move it all at once.
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
    /// <summary>What's picked changed.</summary>
    public event Action? PickChanged;

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
    /// <summary>What the Road tool lays and how many squares wide.</summary>
    public Ground RoadGround { get; set; } = Ground.Road;
    public int RoadWidth { get; set; } = 2;
    /// <summary>What the Label tool writes, how tall (squares) and how far turned (degrees).</summary>
    public string LabelText { get; set; } = "Market Street";
    public double LabelSize { get; set; } = 0.8;
    public double LabelTurn { get; set; }
    /// <summary>Raised when R turns the label the Label tool writes.</summary>
    public event Action<double>? LabelTurnChanged;
    /// <summary>How the points of roads, fences and walls (and things put down) snap: to the grid, half squares, or not at all.</summary>
    public CitySnap Snap { get; set; } = CitySnap.Squares;
    /// <summary>What the Fence tool draws: a line from point to point, or (true) a rectangle all the way round; and whether it's a ruin.</summary>
    public bool FenceRectangle { get; set; }
    public bool FenceRuined { get; set; }
    /// <summary>A name for a new road.</summary>
    public Func<CityMap, string>? NameRoad { get; set; }
    /// <summary>A building double-clicked, to open in the building editor.</summary>
    public event Action<Guid>? OpenBuilding;
    /// <summary>Something to tell the user (a road that couldn't go where it was dragged).</summary>
    public event Action<string>? Said;
    /// <summary>Colour the buildings by their district.</summary>
    public bool DistrictColours { get => _districts; set { _districts = value; InvalidateVisual(); } }
    bool _districts;

    /// <summary>What's picked: buildings, trees and things, labels and stretches of fence.</summary>
    public CityPick Pick { get; private set; } = CityPick.None;

    /// <summary>The one building picked (when that's all that's picked).</summary>
    public Guid? Selected => Pick.Buildings.Count == 1 && Pick.Count == 1 ? Pick.Buildings.First() : null;

    /// <summary>The last box dragged with Select (squares, its far sides just outside it), for filling with buildings.</summary>
    public (int X0, int Y0, int X1, int Y1)? LastBox { get; private set; }

    /// <summary>Things copied, to paste.</summary>
    public CityClip? Copied { get; private set; }

    public CityPlan()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public CityMap Map => _map;

    public void Show(CityMap map, bool refit = false)
    {
        map = map.Tidied();
        bool resized = map.Across != _map.Across || map.Down != _map.Down;
        _map = map;
        var on = Pick.On(map);
        if (!on.Holds(Pick)) SetPick(on);
        RedrawGround();
        if (refit || resized) Fit();
        InvalidateVisual();
    }

    public void Select(Guid? id) => SetPick(id is { } i ? new CityPick { Buildings = [i] } : CityPick.None);

    public void SetPick(CityPick pick)
    {
        Pick = pick;
        PickChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Moves the view so a point (squares) is in the middle, zoomed in enough to see it.</summary>
    public void CentreOn(double x, double y)
    {
        _zoom = Math.Max(_zoom, Math.Clamp(Math.Max(_map.Across, _map.Down) / 24.0, 1, 60));
        _pan = default;
        var at = At(x, y);
        _pan = new Vector(Bounds.Width / 2 - at.X, Bounds.Height / 2 - at.Y);
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

    /// <summary>Zooms in (more than 1) or out about the middle of the plan.</summary>
    public void ZoomBy(double factor)
    {
        var mid = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var before = ToMap(mid);
        _zoom = Math.Clamp(_zoom * factor, 1, 60);
        _pan += mid - At(before.X, before.Y);
        InvalidateVisual();
    }

    /// <summary>Where the pointer is on the map (squares), or null when it's off the plan.</summary>
    public event Action<(double X, double Y)?>? Hovered;

    Point At(double x, double y) { var o = Origin; double k = K; return new Point(o.X + x * k, o.Y + y * k); }

    (double X, double Y) ToMap(Point p) { var o = Origin; double k = K; return ((p.X - o.X) / k, (p.Y - o.Y) / k); }

    (int X, int Y)? CellAt((double X, double Y) p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        return _map.In(x, y) ? (x, y) : null;
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

    Prop? PropAt((double X, double Y) p)
    {
        for (int i = _map.Props.Count - 1; i >= 0; i--)
        {
            if (_map.Props[i].Hit(p.X, p.Y)) return _map.Props[i];
        }
        return null;
    }

    CityLabel? LabelAt((double X, double Y) p)
    {
        for (int i = _map.Labels.Count - 1; i >= 0; i--)
            if (_map.Labels[i].Hit(p.X, p.Y)) return _map.Labels[i];
        return null;
    }

    /// <summary>The fence or wall under the pointer (within a few pixels of its line, or its thickness).</summary>
    Fence? FenceAt((double X, double Y) p)
    {
        double reach = Math.Max(0.25, 6 / K);
        for (int i = _map.Fences.Count - 1; i >= 0; i--)
        {
            var f = _map.Fences[i];
            if (f.Near(p.X, p.Y, Math.Max(reach, f.Kind == FenceKind.CityWall ? 0.3 : 0))) return f;
        }
        return null;
    }

    /// <summary>The road under the pointer (the last laid, on top, first).</summary>
    CityRoad? RoadAt((double X, double Y) p)
    {
        for (int i = _map.Roads.Count - 1; i >= 0; i--)
            if (_map.Roads[i].Covers(p.X, p.Y)) return _map.Roads[i];
        return null;
    }

    /// <summary>The one thing under the pointer, as a pick: a label, a tree or thing, a building, a fence or a road.</summary>
    CityPick? HitAt((double X, double Y) p)
    {
        if (LabelAt(p) is { } l) return CityPick.Of(l);
        if (PropAt(p) is { } q) return CityPick.Of(q);
        if (BuildingAt(p) is { } b) return CityPick.Of(b);
        if (FenceAt(p) is { } f) return CityPick.Of(f);
        if (RoadAt(p) is { } r) return CityPick.Of(r);
        return null;
    }

    static readonly Color[] Palette =
    [
        Color.FromRgb(0xE6, 0x7E, 0x22), Color.FromRgb(0x34, 0x98, 0xDB), Color.FromRgb(0x9B, 0x59, 0xB6), Color.FromRgb(0x2E, 0xCC, 0x71),
        Color.FromRgb(0xE7, 0x4C, 0x3C), Color.FromRgb(0xF1, 0xC4, 0x0F), Color.FromRgb(0x1A, 0xBC, 0x9C), Color.FromRgb(0xD3, 0x54, 0x00),
        Color.FromRgb(0x8E, 0x44, 0xAD), Color.FromRgb(0x16, 0xA0, 0x85), Color.FromRgb(0xC0, 0x39, 0x2B), Color.FromRgb(0x27, 0x60, 0xA0),
    ];

    /// <summary>The colour a district is shown in (grey for none), the same every time for the same name.</summary>
    public static Color DistrictColour(string? district)
    {
        var d = (district ?? "").Trim().ToLowerInvariant();
        if (d.Length == 0) return Color.FromRgb(0x90, 0x90, 0x90);
        int h = 17;
        foreach (char c in d) h = unchecked(h * 31 + c);
        return Palette[(h & 0x7FFFFFFF) % Palette.Length];
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

        // Roads over the ground, their bridges, then fences and walls with their gates and ends.
        Cache();
        DrawRoads(ctx, k);
        DrawWalls(ctx, k);

        // Buildings: their squares, roofs, walls with the doors and windows in them.
        var overlaps = CityFootprint.Overlaps(_map).SelectMany(p => new[] { p.A.Id, p.B.Id }).ToHashSet();
        foreach (var b in _map.Buildings) DrawBuilding(ctx, b, k, overlaps.Contains(b.Id));

        // Trees and the like, and the labels and road names.
        foreach (var p in _map.Props) DrawProp(ctx, p, k);
        DrawRoadNames(ctx, k);
        foreach (var l in _map.Labels) DrawLabel(ctx, l, k, Pick.Labels.Contains(l.Id));
        DrawHandles(ctx, k);

        // The last box dragged, to fill with buildings.
        if (LastBox is { } lb && Tool == CityTool.Select && _box is null)
            ctx.DrawRectangle(null, new Pen(PickBrush, 1) { DashStyle = new DashStyle([4, 3], 0) }, new Rect(At(lb.X0, lb.Y0), At(lb.X1, lb.Y1)));

        // What the pointer is over, and what's being dragged.
        DrawHover(ctx, k);
        if (_map.Buildings.Count == 0 && _map.Props.Count == 0 && _map.Labels.Count == 0 && _map.Roads.Count == 0)
            Text(ctx, "Read a battle map, or drag out buildings with the Building tool", new Point(Bounds.Width / 2, Bounds.Height - 22), UiKit.Brush("TextMuted"), 11, true);
    }

    static Color Scene3DShade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    void DrawBuilding(DrawingContext ctx, CityBuilding b, double k, bool overlapping)
    {
        bool picked = Pick.Buildings.Contains(b.Id);
        var cells = CityFootprint.Cells(b);
        var roofCol = _districts ? DistrictColour(b.District)
            : b.Design.Roofs.Count > 0 ? CityScene.RoofColour(b.Design.Roofs[0].Covering) : CityScene.WallColour(b.Design.Style.Texture);
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
        if (Prop.Footprint(p.Kind) is { } f)
        {
            // The town's bigger pieces: their footprint, turned, with a mark of what each is.
            double w = Math.Max(3, f.W * p.Size * k), d = Math.Max(2, f.D * p.Size * k);
            var (body, mark) = p.Kind switch
            {
                PropKind.Trough => (Color.FromRgb(0x8C, 0x88, 0x82), Color.FromRgb(0x4E, 0x86, 0xB6)),
                PropKind.Stage => (Color.FromRgb(0x9A, 0x70, 0x48), Color.FromRgb(0x9E, 0x2E, 0x2E)),
                PropKind.Stall => (Color.FromRgb(0xC8, 0x4C, 0x3C), Color.FromRgb(0xF0, 0xE6, 0xD0)),
                PropKind.Bar => (Color.FromRgb(0x7A, 0x52, 0x32), Color.FromRgb(0xB0, 0x86, 0x58)),
                PropKind.NoticeBoard => (Color.FromRgb(0x6E, 0x4D, 0x30), Color.FromRgb(0xF0, 0xE6, 0xD0)),
                PropKind.Fountain => (Color.FromRgb(0xA4, 0x9C, 0x90), Color.FromRgb(0x4E, 0x86, 0xB6)),
                PropKind.Gallows => (Color.FromRgb(0x9A, 0x70, 0x48), Color.FromRgb(0x4A, 0x32, 0x1E)),
                _ => (Color.FromRgb(0x7C, 0x7A, 0x76), Color.FromRgb(0x5A, 0x58, 0x56)),
            };
            using (ctx.PushTransform(Matrix.CreateRotation(p.TurnDeg * Math.PI / 180) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                var box = new Rect(-w / 2, -d / 2, w, d);
                ctx.FillRectangle(new SolidColorBrush(body), box);
                var markBrush = new SolidColorBrush(mark);
                switch (p.Kind)
                {
                    case PropKind.Trough: ctx.FillRectangle(markBrush, box.Deflate(Math.Min(w, d) * 0.22)); break;
                    case PropKind.Stage: ctx.FillRectangle(markBrush, new Rect(box.X, box.Y, w, d * 0.18)); break;
                    case PropKind.Stall:
                        for (int i = 1; i < 6; i += 2) ctx.FillRectangle(markBrush, new Rect(box.X + w * i / 6, box.Y, w / 6, d));
                        break;
                    case PropKind.Bar: ctx.FillRectangle(markBrush, new Rect(box.X, box.Y, w, d * 0.35)); break;
                    case PropKind.NoticeBoard: ctx.FillRectangle(markBrush, box.Deflate(new Thickness(w * 0.15, d * 0.25))); break;
                    case PropKind.Fountain:
                        ctx.DrawEllipse(markBrush, null, new Point(0, 0), w * 0.42, d * 0.42);
                        ctx.DrawEllipse(new SolidColorBrush(body), null, new Point(0, 0), w * 0.12, d * 0.12);
                        break;
                    case PropKind.Gallows:
                        // The beam across the platform, and the steps at the front.
                        ctx.FillRectangle(markBrush, new Rect(box.X + w * 0.18, box.Y + d * 0.32, w * 0.64, d * 0.1));
                        ctx.FillRectangle(markBrush, new Rect(box.X + w * 0.18, box.Bottom - d * 0.28, w * 0.26, d * 0.28));
                        break;
                    case PropKind.GuardTower:
                        ctx.FillRectangle(markBrush, box.Deflate(Math.Min(w, d) * 0.18));
                        for (int i = 0; i < 4; i++)
                        {
                            double c = w * (0.1 + 0.27 * i);
                            ctx.FillRectangle(new SolidColorBrush(body), new Rect(box.X + c, box.Y + d * 0.02, w * 0.08, d * 0.12));
                            ctx.FillRectangle(new SolidColorBrush(body), new Rect(box.X + c, box.Bottom - d * 0.14, w * 0.08, d * 0.12));
                        }
                        break;
                }
                if (k >= 8) ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 1), box);
                if (Pick.Props.Contains(p.Id)) ctx.DrawRectangle(new Pen(PickBrush, 2), box.Inflate(2));
            }
            return;
        }
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
        if (Pick.Props.Contains(p.Id)) ctx.DrawEllipse(null, new Pen(PickBrush, 2), at, r + 2, r + 2);
    }

    void DrawLabel(DrawingContext ctx, CityLabel l, double k, bool picked)
    {
        var mid = At(l.X, l.Y);
        using (ctx.PushTransform(Matrix.CreateRotation(l.TurnDeg * Math.PI / 180) * Matrix.CreateTranslation(mid.X, mid.Y)))
        {
            var r = Words(ctx, l.Text, Math.Max(4, l.Size * k), Brushes.White);
            if (picked) ctx.DrawRectangle(null, new Pen(PickBrush, 1.5) { DashStyle = new DashStyle([3, 2], 0) }, r.Inflate(new Thickness(3, 2)));
        }
    }

    /// <summary>Words centred on (0, 0) with a dark edge round them, so they read over anything; the box they take.</summary>
    static Rect Words(DrawingContext ctx, string text, double size, IBrush brush, byte edge = 0xC0)
    {
        var face = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush);
        var p = new Point(-ft.Width / 2, -ft.Height / 2);
        var back = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(Color.FromArgb(edge, 0, 0, 0)));
        foreach (var (dx, dy) in new[] { (1.0, 1.0), (-1.0, 1.0), (1.0, -1.0), (-1.0, -1.0) }) ctx.DrawText(back, p + new Point(dx, dy));
        ctx.DrawText(ft, p);
        return new Rect(p, new Size(ft.Width, ft.Height));
    }

    // ---- roads, bridges, fences and walls ----

    WallLayout _walls = new([], [], []);
    List<BridgeSite> _bridges = [];
    List<(CityRoad Road, CityPt At, double Deg)> _names = [];
    CityMap? _cachedFor;

    /// <summary>Works out again where the walls stand and the bridges are, when the map has changed.</summary>
    void Cache()
    {
        if (ReferenceEquals(_cachedFor, _map)) return;
        _cachedFor = _map;
        _walls = WallLayout.Of(_map);
        _bridges = _map.Roads.Count > 0 ? TownPieces.Bridges(_map) : [];
        _names = RoadNames(_map);
    }

    /// <summary>A line through points (squares) on screen.</summary>
    StreamGeometry Line(IReadOnlyList<CityPt> pts, bool closed = false)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(At(pts[0].X, pts[0].Y), false);
        for (int i = 1; i < pts.Count; i++) c.LineTo(At(pts[i].X, pts[i].Y));
        c.EndFigure(closed);
        return g;
    }

    static Color Shade(Color c, double f) => Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    /// <summary>The colour of a road's surface.</summary>
    public static Color RoadColour(Ground g) => CityScene.GroundColour(g);

    void DrawRoad(DrawingContext ctx, CityRoad r, double k, IBrush brush, double extra)
    {
        if (r.Points.Count == 0) return;
        double w = Math.Max(1, r.Width * k + extra);
        if (r.Points.Count == 1)
        {
            var c = At(r.Points[0].X, r.Points[0].Y);
            ctx.FillRectangle(brush, new Rect(c.X - w / 2, c.Y - w / 2, w, w));
            return;
        }
        ctx.DrawGeometry(null, new Pen(brush, w, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter, miterLimit: 3), Line(r.Points));
    }

    void DrawRoads(DrawingContext ctx, double k)
    {
        if (_map.Roads.Count == 0) return;
        // Every road's edge first, then every road over them, so where they meet they run into each other.
        foreach (var r in _map.Roads) DrawRoad(ctx, r, k, new SolidColorBrush(Shade(RoadColour(r.Surface), 0.72)), Math.Max(1.5, k * 0.1));
        foreach (var r in _map.Roads) DrawRoad(ctx, r, k, new SolidColorBrush(RoadColour(r.Surface)), 0);
        foreach (var r in _map.Roads.Where(r => Pick.Roads.Contains(r.Id)))
        {
            DrawRoad(ctx, r, k, PickBrush, 4);
            DrawRoad(ctx, r, k, new SolidColorBrush(RoadColour(r.Surface)), 0);
        }
        // Bridges where they cross water: a deck of stone (or planks for a track) with a parapet each side.
        foreach (var b in _bridges)
        {
            bool planks = b.Road == Ground.Dirt;
            var deck = new SolidColorBrush(planks ? Color.FromRgb(0x9A, 0x70, 0x48) : Color.FromRgb(0xB8, 0xB0, 0xA2));
            foreach (var (x, y) in b.Cells) ctx.FillRectangle(deck, new Rect(At(x, y), new Size(k + 0.5, k + 0.5)));
            int x0 = b.Cells.Min(c => c.X), x1 = b.Cells.Max(c => c.X) + 1, y0 = b.Cells.Min(c => c.Y), y1 = b.Cells.Max(c => c.Y) + 1;
            var rail = new Pen(new SolidColorBrush(planks ? Color.FromRgb(0x4A, 0x32, 0x1E) : Color.FromRgb(0x6A, 0x64, 0x5C)), Math.Max(1.5, k * 0.14), lineCap: PenLineCap.Square);
            if (planks && k >= 8)
            {
                var gap = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x3A, 0x26, 0x14)), 1);
                for (double t = 0.25; t < (b.Across ? x1 - x0 : y1 - y0); t += 0.25)
                    if (b.Across) ctx.DrawLine(gap, At(x0 + t, y0), At(x0 + t, y1)); else ctx.DrawLine(gap, At(x0, y0 + t), At(x1, y0 + t));
            }
            if (b.Across) { ctx.DrawLine(rail, At(x0, y0), At(x1, y0)); ctx.DrawLine(rail, At(x0, y1), At(x1, y1)); }
            else { ctx.DrawLine(rail, At(x0, y0), At(x0, y1)); ctx.DrawLine(rail, At(x1, y0), At(x1, y1)); }
        }
    }

    static Color FenceColour(FenceKind k) => k switch
    {
        FenceKind.Hedge => Color.FromRgb(0x3F, 0x6A, 0x2E),
        FenceKind.Fence => Color.FromRgb(0x6E, 0x4D, 0x30),
        _ => Color.FromRgb(0x7A, 0x74, 0x6C),
    };

    static double FenceWidth(FenceKind k) => k switch { FenceKind.CityWall => 0.4, FenceKind.Hedge => 0.25, FenceKind.StoneWall => 0.14, _ => 0.08 };

    void DrawWalls(DrawingContext ctx, double k)
    {
        foreach (var piece in _walls.Pieces)
        {
            var f = piece.Fence;
            var colour = FenceColour(f.Kind);
            double width = Math.Max(1.5, k * FenceWidth(f.Kind));
            var line = Line(piece.Points, piece.Closed);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(f.Ruined ? Shade(colour, 1.15) : colour), width, f.Ruined ? new DashStyle([2.2, 0.6], 0) : null, PenLineCap.Square, PenLineJoin.Miter), line);
            if (f.Kind == FenceKind.CityWall && k >= 6)
                // The walk along the top.
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Shade(colour, 1.28)), width * 0.45, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), line);
        }
        // The ends: a town wall's turrets, a stone wall's pillars, a fence's posts.
        foreach (var e in _walls.Ends.Where(e => e.Kind == WallEndKind.Cap))
        {
            double size = e.Fence.Kind switch { FenceKind.CityWall => 0.8, FenceKind.StoneWall => 0.28, FenceKind.Fence => 0.16, _ => 0 };
            if (size <= 0) continue;
            Block(ctx, e.At, e.Out, size, size, Shade(FenceColour(e.Fence.Kind), e.Fence.Kind == FenceKind.CityWall ? 0.85 : 0.8), k);
        }
        // The gates: a town wall's gatehouse over the road, towers either side; posts for a fence's gate.
        foreach (var g in _walls.Gates)
        {
            var u = (g.B - g.A).Unit;
            if (g.Fence.Kind == FenceKind.CityWall)
            {
                var n = new CityPt(-u.Y, u.X);
                var colour = FenceColour(FenceKind.CityWall);
                var arch = new[] { g.A + n * 0.3, g.B + n * 0.3, g.B - n * 0.3, g.A - n * 0.3 };
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(At(arch[0].X, arch[0].Y), true);
                    foreach (var q in arch.Skip(1)) c.LineTo(At(q.X, q.Y));
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(colour, 0.45), new Pen(new SolidColorBrush(Shade(colour, 0.7)), 1) { DashStyle = new DashStyle([3, 2], 0) }, geo);
                Block(ctx, g.A - u * 0.35, u * -1, 0.75, 0.9, Shade(colour, 0.85), k);
                Block(ctx, g.B + u * 0.35, u, 0.75, 0.9, Shade(colour, 0.85), k);
            }
            else if (g.Fence.Kind != FenceKind.Hedge)
            {
                Block(ctx, g.A, u * -1, 0.2, 0.2, Shade(FenceColour(g.Fence.Kind), 0.7), k);
                Block(ctx, g.B, u, 0.2, 0.2, Shade(FenceColour(g.Fence.Kind), 0.7), k);
            }
        }
        // The fences picked.
        foreach (var f in _map.Fences.Where(f => Pick.Fences.Contains(f.Id) && f.Points.Count > 1))
            ctx.DrawGeometry(null, new Pen(PickBrush, Math.Max(2, k * 0.12), lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), Line(f.Points, f.Closed));
    }

    /// <summary>A block (squares long the way out, and across) centred on a point, facing the way out.</summary>
    void Block(DrawingContext ctx, CityPt at, CityPt way, double length, double across, Color colour, double k)
    {
        var c = At(at.X, at.Y);
        double a = Math.Atan2(way.Y, way.X);
        using (ctx.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            var r = new Rect(-length * k / 2, -across * k / 2, Math.Max(2, length * k), Math.Max(2, across * k));
            ctx.FillRectangle(new SolidColorBrush(colour), r);
            if (k >= 8) ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 1), r);
        }
    }

    /// <summary>
    /// Where along a road its name is written: the middle of its longest stretch, and every two dozen squares
    /// along any long one, each slid along its stretch to where the words don't run over another of
    /// <paramref name="others"/> (a road crossing it) if there's such a place; somewhere, whatever, if not.
    /// </summary>
    public static List<(CityPt At, double Deg)> NameSpots(CityRoad r, double size, IEnumerable<CityRoad>? others = null)
    {
        var res = new List<(CityPt, double)>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Points.Count < 2) return res;
        double text = r.Name.Length * size * 0.55 + 1;
        // The other roads near enough to matter, each with the box round it.
        var near = new List<(CityRoad Road, double X0, double Y0, double X1, double Y1)>();
        double rx0 = r.Points.Min(p => p.X) - text, rx1 = r.Points.Max(p => p.X) + text, ry0 = r.Points.Min(p => p.Y) - text, ry1 = r.Points.Max(p => p.Y) + text;
        foreach (var o in others ?? [])
        {
            if (o.Id == r.Id || o.Points.Count == 0) continue;
            double h = o.Width / 2;
            double x0 = o.Points.Min(p => p.X) - h, y0 = o.Points.Min(p => p.Y) - h, x1 = o.Points.Max(p => p.X) + h, y1 = o.Points.Max(p => p.Y) + h;
            if (x0 > rx1 || y0 > ry1 || x1 < rx0 || y1 < ry0) continue;
            near.Add((o, x0, y0, x1, y1));
        }
        bool Clear(CityPt a, CityPt u, double c)
        {
            if (near.Count == 0) return true;
            for (double t = c - text / 2; t <= c + text / 2 + 1e-9; t += 0.25)
            {
                var p = a + u * t;
                foreach (var (o, x0, y0, x1, y1) in near)
                    if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 && o.Covers(p.X, p.Y)) return false;
            }
            return true;
        }
        var segs = r.Segments().Select(s => (s.A, s.B, L: s.A.DistanceTo(s.B))).Where(s => s.L >= text).OrderByDescending(s => s.L).ToList();
        for (int i = 0; i < segs.Count; i++)
        {
            var (a, b, len) = segs[i];
            if (i > 0 && len < 20) continue;
            var u = (b - a).Unit;
            double deg = Geometry.TextAngle(a, b);
            int n = Math.Max(1, (int)Math.Floor(len / 24));
            for (int j = 0; j < n; j++)
            {
                // From the middle of its share of the stretch outwards, half a square at a time.
                double want = (j + 0.5) / n * len, lo = Math.Max(text / 2, j * len / n), hi = Math.Min(len - text / 2, (j + 1) * len / n);
                double? at = null;
                for (double off = 0; at is null && off <= len; off += 0.5)
                    foreach (double c in new[] { want - off, want + off })
                        if (c >= lo - 1e-9 && c <= hi + 1e-9 && Clear(a, u, c)) { at = c; break; }
                if (at is { } t) res.Add((a + u * t, deg));
            }
        }
        // Nowhere clear: the middle of its longest stretch all the same, so every road has its name.
        if (res.Count == 0 && segs.Count > 0) res.Add((segs[0].A + (segs[0].B - segs[0].A) * 0.5, Geometry.TextAngle(segs[0].A, segs[0].B)));
        return res;
    }

    /// <summary>Every road's name and where it's written, on a map.</summary>
    public static List<(CityRoad Road, CityPt At, double Deg)> RoadNames(CityMap m) =>
        [.. m.Roads.SelectMany(r => NameSpots(r, NameSize(r), m.Roads).Select(s => (r, s.At, s.Deg)))];

    /// <summary>How tall a road's name is (squares): bigger on a wider road.</summary>
    public static double NameSize(CityRoad r) => Math.Clamp(r.Width * 0.36, 0.45, 1.3);

    void DrawRoadNames(DrawingContext ctx, double k)
    {
        foreach (var (r, at, deg) in _names)
        {
            double size = NameSize(r) * k;
            if (size < 6) continue;
            var c = At(at.X, at.Y);
            using (ctx.PushTransform(Matrix.CreateRotation(deg * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y)))
                Words(ctx, r.Name, size, Brushes.White, 0xA0);
        }
    }

    /// <summary>The one road or fence picked, whose points can be dragged.</summary>
    (IReadOnlyList<CityPt> Points, bool Closed, Guid Id, bool Road)? Handled()
    {
        if (Pick.Count != 1) return null;
        if (Pick.Roads.Count == 1 && _map.Road(Pick.Roads.First()) is { Points.Count: > 0 } r) return (r.Points, false, r.Id, true);
        if (Pick.Fences.Count == 1 && _map.FenceById(Pick.Fences.First()) is { Points.Count: > 0 } f) return (f.Points, f.Closed, f.Id, false);
        return null;
    }

    void DrawHandles(DrawingContext ctx, double k)
    {
        if (Tool != CityTool.Select || Handled() is not { } h) return;
        var ring = new Pen(PickBrush, 2);
        int n = h.Points.Count, sides = h.Closed && n > 2 ? n : n - 1;
        for (int i = 0; i < sides; i++)
        {
            var a = h.Points[i];
            var b = h.Points[(i + 1) % n];
            var m = At((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(m.X, m.Y - 5), true);
                c.LineTo(new Point(m.X + 5, m.Y));
                c.LineTo(new Point(m.X, m.Y + 5));
                c.LineTo(new Point(m.X - 5, m.Y));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(PickBrush, new Pen(Brushes.White, 1), geo);
        }
        foreach (var p in h.Points) ctx.DrawEllipse(Brushes.White, ring, At(p.X, p.Y), 5, 5);
    }

    void DrawHover(DrawingContext ctx, double k)
    {
        if (_box is { } box)
        {
            var r = new Rect(At(Math.Min(box.A.X, box.B.X), Math.Min(box.A.Y, box.B.Y)), At(Math.Max(box.A.X, box.B.X), Math.Max(box.A.Y, box.B.Y)));
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xF0, 0x9A, 0x2A)), r);
            ctx.DrawRectangle(null, new Pen(PickBrush, 1) { DashStyle = new DashStyle([4, 3], 0) }, r);
            return;
        }
        if (_drag is { } rd && Tool == CityTool.Road)
        {
            // Taking a painted road up: grass along the way.
            var brush = new SolidColorBrush(CityScene.GroundColour(Ground.Grass), 0.75);
            foreach (var (x, y) in RoadCells(rd)) if (_map.In(x, y)) ctx.FillRectangle(brush, new Rect(At(x, y), new Size(k + 0.5, k + 0.5)));
            return;
        }
        if (_line is { } ln && Tool == CityTool.Road)
        {
            var road = new CityRoad { Width = RoadWidth, Surface = RoadGround, Points = CityEdit.Bent(ln.A, ln.B, _ctrl) };
            bool blocked = CityEdit.Hits(_map, road, CityEdit.BuildingCells(_map));
            using (ctx.PushOpacity(0.75)) DrawRoad(ctx, road, k, new SolidColorBrush(blocked ? Color.FromRgb(0xE0, 0x40, 0x40) : RoadColour(RoadGround)), 0);
            Text(ctx, $"{road.Length:0.#} squares" + (blocked ? " (stops at the building)" : ""), At(ln.B.X, ln.B.Y) + new Point(8, 4), UiKit.Brush("Text"), 11, false);
            return;
        }
        if (_line is { } fl && Tool == CityTool.Fence)
        {
            var pen = new Pen(new SolidColorBrush(FenceColour(FenceKind), 0.8), Math.Max(2, k * FenceWidth(FenceKind)), lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter);
            if (FenceRectangle)
            {
                ctx.DrawRectangle(null, pen, new Rect(At(fl.A.X, fl.A.Y), At(fl.B.X, fl.B.Y)));
                Text(ctx, $"{Math.Abs(fl.B.X - fl.A.X):0.#} × {Math.Abs(fl.B.Y - fl.A.Y):0.#}", At(fl.B.X, fl.B.Y) + new Point(8, 4), UiKit.Brush("Text"), 11, false);
            }
            else
            {
                ctx.DrawLine(pen, At(fl.A.X, fl.A.Y), At(fl.B.X, fl.B.Y));
                Text(ctx, $"{fl.A.DistanceTo(fl.B):0.#} squares", At(fl.B.X, fl.B.Y) + new Point(8, 4), UiKit.Brush("Text"), 11, false);
            }
            return;
        }
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
            {
                var sp = CityEdit.Snapped(new CityPt(hp.X, hp.Y), Snap);
                ctx.DrawEllipse(_right ? RubBrush : HoverBrush, null, At(sp.X, sp.Y), Math.Max(4, k * 0.2), Math.Max(4, k * 0.2));
                break;
            }
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
            case CityTool.Road:
            {
                var sp = CityEdit.Snapped(new CityPt(hp.X, hp.Y), Snap, CityEdit.RoadOffset(RoadWidth));
                double n = Math.Max(1, RoadWidth);
                ctx.FillRectangle(_right ? RubBrush : HoverBrush, new Rect(At(sp.X - n / 2, sp.Y - n / 2), new Size(k * n, k * n)));
                break;
            }
            case CityTool.Label when !string.IsNullOrWhiteSpace(LabelText):
                using (ctx.PushOpacity(0.6)) DrawLabel(ctx, new CityLabel { Text = LabelText.Trim(), X = hp.X, Y = hp.Y, Size = LabelSize, TurnDeg = LabelTurn }, k, false);
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
    bool _down, _right, _changed, _panning, _ctrl;
    Point _panFrom;
    ((int X, int Y) A, (int X, int Y) B)? _drag;
    (CityPt A, CityPt B)? _line;
    ((double X, double Y) A, (double X, double Y) B)? _box;
    bool _boxAdd;
    Ground[,]? _ground;
    CityMap? _moveFrom;
    CityPick _movePick = CityPick.None;
    (double X, double Y) _grabAt;
    /// <summary>A road's or fence's point (or side) being dragged, and the map before.</summary>
    (Guid Id, bool Road, int Index, bool Side)? _handle;
    CityMap? _handleFrom;
    CityPt _handleGrab;
    string? _label;

    HashSet<(int X, int Y)> RoadCells(((int X, int Y) A, (int X, int Y) B) d) => CityEdit.Road(d.A.X, d.A.Y, d.B.X, d.B.Y, RoadWidth, _ctrl);

    /// <summary>Where a road's point snaps (its middle line on grid lines, or square middles for an odd width).</summary>
    CityPt RoadSnap(CityPt p, double width) => CityEdit.Snapped(p, Snap, CityEdit.RoadOffset(width));

    /// <summary>The point (or side, between it and the next) of the picked road or fence within a few pixels of the pointer.</summary>
    (int Index, bool Side)? HandleAt((double X, double Y) p)
    {
        if (Tool != CityTool.Select || Handled() is not { } h) return null;
        var at = At(p.X, p.Y);
        double Near(CityPt q) { var s = At(q.X, q.Y); return Math.Sqrt((s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y)); }
        for (int i = 0; i < h.Points.Count; i++) if (Near(h.Points[i]) <= 7) return (i, false);
        int n = h.Points.Count, sides = h.Closed && n > 2 ? n : n - 1;
        for (int i = 0; i < sides; i++) if (Near((h.Points[i] + h.Points[(i + 1) % n]) * 0.5) <= 7) return (i, true);
        return null;
    }

    /// <summary>The map with the picked road's or fence's points changed.</summary>
    CityMap WithPoints(CityMap m, Guid id, bool road, List<CityPt> pts, bool? closed = null) => road
        ? m with { Roads = [.. m.Roads.Select(r => r.Id == id ? r with { Points = pts } : r)] }
        : m with { Fences = [.. m.Fences.Select(f => f.Id == id ? f with { Points = pts, Closed = closed ?? f.Closed } : f)] };

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_moveFrom is not null && _down)
        {
            // Turning what's being dragged.
            (_map, var pick, _) = CityEdit.Turn(_map, Pick, e.Delta.Y > 0 ? 1 : -1);
            Pick = pick;
            _moveFrom = _map;
            _movePick = pick;
            _grabAt = _hover ?? _grabAt;
            _changed = true;
            InvalidateVisual();
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
        _ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _changed = false;
        _label = null;
        _moveFrom = null;
        _handle = null;
        e.Pointer.Capture(this);
        var p = ToMap(pt.Position);
        var cp = new CityPt(p.X, p.Y);
        switch (Tool)
        {
            case CityTool.Select:
            {
                if (HandleAt(p) is { } hd && Handled() is { } owner)
                {
                    if (_right)
                    {
                        // The right button on a point takes it out (a line keeps two, a ring three).
                        if (!hd.Side && owner.Points.Count > (owner.Closed ? 3 : 2))
                        {
                            var pts = owner.Points.ToList();
                            pts.RemoveAt(hd.Index);
                            _map = WithPoints(_map, owner.Id, owner.Road, pts);
                            _changed = true;
                            _label = owner.Road ? "Change a road" : "Change a fence";
                        }
                        _down = false;
                        break;
                    }
                    _handle = (owner.Id, owner.Road, hd.Index, hd.Side);
                    _handleFrom = _map;
                    _handleGrab = cp;
                    break;
                }
                var hit = HitAt(p);
                LastBox = null;
                if (!_right && e.ClickCount == 2 && hit is not null)
                {
                    _down = false;
                    if (hit.Buildings.Count == 1) { OpenBuilding?.Invoke(hit.Buildings.First()); break; }
                    // A double click on the picked road or fence puts a point in it there.
                    if (Handled() is { } line && (hit.Roads.Contains(line.Id) || hit.Fences.Contains(line.Id)))
                    {
                        var (_, _, seg) = CityEdit.Nearest(line.Points, line.Closed, cp);
                        var at = line.Road ? RoadSnap(cp, _map.Road(line.Id)!.Width) : CityEdit.Snapped(cp, Snap);
                        var pts = line.Points.ToList();
                        pts.Insert(seg + 1, at);
                        _map = WithPoints(_map, line.Id, line.Road, CityEdit.Distinct(pts));
                        _changed = true;
                        _label = line.Road ? "Change a road" : "Change a fence";
                    }
                    break;
                }
                if (_right)
                {
                    if (hit is not null && !Pick.Holds(hit)) SetPick(hit);
                    _down = false;
                }
                else if (hit is null)
                {
                    // A box from here: everything in it is picked.
                    _box = (p, p);
                    _boxAdd = _ctrl;
                    if (!_ctrl) SetPick(CityPick.None);
                }
                else if (_ctrl)
                {
                    SetPick(Pick.Toggle(hit));
                    _down = false;
                }
                else
                {
                    if (!Pick.Holds(hit)) SetPick(hit);
                    _moveFrom = _map;
                    _movePick = Pick;
                    _grabAt = p;
                }
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
            case CityTool.Road:
                if (_right)
                {
                    // The right button takes a road away, or takes a painted one up (grass along the way).
                    if (RoadAt(p) is { } r)
                    {
                        _map = _map with { Roads = [.. _map.Roads.Where(x => x.Id != r.Id)] };
                        _changed = true;
                        _label = "Take a road away";
                        _down = false;
                    }
                    else if (CellAt(p) is { } rc) _drag = (rc, rc);
                }
                else
                {
                    var a = RoadSnap(cp, RoadWidth);
                    _line = (a, a);
                }
                break;
            case CityTool.Place:
                if (_right) { PlaceTurn = (PlaceTurn + 1) % 4; _down = false; }
                else if (ToPlace is { } prefab)
                {
                    var b = prefab.Place((int)Math.Floor(p.X), (int)Math.Floor(p.Y), PlaceTurn);
                    _map = _map.WithBuilding(b);
                    SetPick(CityPick.Of(b));
                    _changed = true;
                }
                break;
            case CityTool.Fence:
                if (_right) CutFenceAt(p);
                else
                {
                    var a = CityEdit.Snapped(cp, Snap);
                    _line = (a, a);
                }
                break;
            case CityTool.Prop:
                if (_right) RemovePropAt(p);
                else
                {
                    // The town's bigger pieces on half squares, trees and the like on quarters (or where they're dropped).
                    var at = Snap == CitySnap.Free ? CityEdit.Snapped(cp, CitySnap.Free)
                        : Prop.Footprint(PropKind) is not null ? CityEdit.Snapped(cp, CitySnap.HalfSquares)
                        : new CityPt(Math.Round(p.X * 4) / 4, Math.Round(p.Y * 4) / 4);
                    _map = _map with { Props = [.. _map.Props, new Prop { Kind = PropKind, X = at.X, Y = at.Y, TurnDeg = Prop.Footprint(PropKind) is null ? Random.Shared.Next(360) : 0, Size = PropKind is PropKind.Tree or PropKind.Pine ? 1.2 : 1 }] };
                    _changed = true;
                }
                break;
            case CityTool.Label:
                if (_right)
                {
                    if (LabelAt(p) is { } l) { _map = _map with { Labels = [.. _map.Labels.Where(x => x.Id != l.Id)] }; _changed = true; }
                }
                else if (!string.IsNullOrWhiteSpace(LabelText) && _map.In((int)Math.Floor(p.X), (int)Math.Floor(p.Y)))
                {
                    var l = new CityLabel { Text = LabelText.Trim(), X = Math.Round(p.X * 4) / 4, Y = Math.Round(p.Y * 4) / 4, Size = LabelSize, TurnDeg = CityLabel.Upright(LabelTurn) };
                    _map = _map with { Labels = [.. _map.Labels, l] };
                    SetPick(CityPick.Of(l));
                    _changed = true;
                }
                break;
            case CityTool.Erase:
                EraseAt(p);
                break;
        }
        InvalidateVisual();
    }

    /// <summary>Where the far end of a fence being drawn goes: snapped, straight across, down or at 45° unless Ctrl is held.</summary>
    CityPt FenceEnd(CityPt a, CityPt p)
    {
        var b = CityEdit.Snapped(p, Snap);
        if (_ctrl || FenceRectangle) return b;
        var d = b - a;
        if (Math.Abs(d.X) > 2 * Math.Abs(d.Y)) return new CityPt(b.X, a.Y);
        if (Math.Abs(d.Y) > 2 * Math.Abs(d.X)) return new CityPt(a.X, b.Y);
        double step = Snap == CitySnap.Squares ? 1 : Snap == CitySnap.HalfSquares ? 0.5 : 0.001;
        double m = Math.Round((Math.Abs(d.X) + Math.Abs(d.Y)) / 2 / step) * step;
        return new CityPt(a.X + Math.Sign(d.X) * m, a.Y + Math.Sign(d.Y) * m);
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
        var cp = new CityPt(p.X, p.Y);
        _hover = p;
        Hovered?.Invoke(p);
        if (_down)
        {
            _ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            switch (Tool)
            {
                case CityTool.Select when _handle is { } h && _handleFrom is { } from:
                    DragHandle(from, h, cp);
                    break;
                case CityTool.Select when _box is { } bx:
                    _box = (bx.A, p);
                    break;
                case CityTool.Select when _moveFrom is { } from:
                {
                    int dx = (int)Math.Round(p.X - _grabAt.X), dy = (int)Math.Round(p.Y - _grabAt.Y);
                    var (moved, pick) = CityEdit.Move(from, _movePick, dx, dy);
                    if (!ReferenceEquals(moved, _map))
                    {
                        _map = moved;
                        Pick = pick;
                        _changed = !ReferenceEquals(moved, from) || _changed;
                    }
                    break;
                }
                case CityTool.Ground:
                    PaintAt(p);
                    break;
                case CityTool.Building or CityTool.Road when _drag is { } dg:
                    _drag = (dg.A, ((int)Math.Clamp(Math.Floor(p.X), 0, _map.Across - 1), (int)Math.Clamp(Math.Floor(p.Y), 0, _map.Down - 1)));
                    break;
                case CityTool.Road when _line is { } ln:
                    _line = (ln.A, RoadSnap(cp, RoadWidth));
                    break;
                case CityTool.Fence when _line is { } fl:
                    _line = (fl.A, FenceEnd(fl.A, cp));
                    break;
                case CityTool.Fence when _right:
                    CutFenceAt(p);
                    break;
                case CityTool.Building when _right:
                case CityTool.Erase:
                    EraseAt(p);
                    break;
                case CityTool.Prop when _right:
                    RemovePropAt(p);
                    break;
            }
        }
        InvalidateVisual();
    }

    /// <summary>Drags the picked road's or fence's point (or side, square to it) to follow the pointer, snapped.</summary>
    void DragHandle(CityMap from, (Guid Id, bool Road, int Index, bool Side) h, CityPt p)
    {
        IReadOnlyList<CityPt>? old = h.Road ? from.Road(h.Id)?.Points : from.FenceById(h.Id)?.Points;
        if (old is null) return;
        double width = h.Road ? from.Road(h.Id)!.Width : 0;
        CityPt S(CityPt q) => h.Road ? RoadSnap(q, width) : CityEdit.Snapped(q, Snap);
        var pts = old.ToList();
        bool? closed = null;
        if (!h.Side)
        {
            pts[h.Index] = S(p);
            // An end dropped on the other end of a fence closes it.
            if (!h.Road && pts.Count > 2 && (h.Index == 0 || h.Index == pts.Count - 1) && pts[0].DistanceTo(pts[^1]) < 1e-6)
            {
                pts.RemoveAt(h.Index);
                closed = true;
            }
        }
        else
        {
            int j = (h.Index + 1) % pts.Count;
            var u = (pts[j] - pts[h.Index]).Unit;
            var n = new CityPt(-u.Y, u.X);
            double by = (p - _handleGrab).Dot(n);
            var shift = n * by;
            var a = S(pts[h.Index] + shift);
            // Both ends moved by the same snapped amount, so the side stays as it was.
            var moved = a - pts[h.Index];
            pts[h.Index] = a;
            pts[j] = pts[j] + moved;
        }
        var next = WithPoints(from, h.Id, h.Road, CityEdit.Distinct(pts), closed);
        if (!ReferenceEquals(next, _map))
        {
            _map = next;
            _changed = true;
            _label = h.Road ? "Change a road" : "Change a fence";
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_panning)
        {
            _panning = false;
            return;
        }
        if (!_down) { if (_changed) Finish(); return; }
        _down = false;
        if (_box is { } bx)
        {
            _box = null;
            int x0 = (int)Math.Floor(Math.Min(bx.A.X, bx.B.X)), x1 = (int)Math.Ceiling(Math.Max(bx.A.X, bx.B.X));
            int y0 = (int)Math.Floor(Math.Min(bx.A.Y, bx.B.Y)), y1 = (int)Math.Ceiling(Math.Max(bx.A.Y, bx.B.Y));
            LastBox = x1 - x0 >= 2 && y1 - y0 >= 2 ? (Math.Max(0, x0), Math.Max(0, y0), Math.Min(_map.Across, x1), Math.Min(_map.Down, y1)) : null;
            var inside = CityEdit.Within(_map, bx.A.X, bx.A.Y, bx.B.X, bx.B.Y);
            SetPick(_boxAdd ? Pick.With(inside) : inside);
        }
        if (Tool == CityTool.Building && _drag is { } dg)
        {
            int x0 = Math.Min(dg.A.X, dg.B.X), x1 = Math.Max(dg.A.X, dg.B.X), y0 = Math.Min(dg.A.Y, dg.B.Y), y1 = Math.Max(dg.A.Y, dg.B.Y);
            _drag = null;
            if (NewBuilding?.Invoke(x0, y0, x1 - x0 + 1, y1 - y0 + 1) is { } b)
            {
                _map = _map.WithBuilding(b);
                SetPick(CityPick.Of(b));
                _changed = true;
            }
        }
        if (Tool == CityTool.Road && _drag is { } rd)
        {
            _drag = null;
            var g = _map.GroundGrid();
            foreach (var (x, y) in RoadCells(rd))
                if (_map.In(x, y) && g[x, y] is Ground.Road or Ground.Dirt or Ground.Paving && g[x, y] != Ground.Grass) { g[x, y] = Ground.Grass; _changed = true; }
            if (_changed) { _map = _map.WithGround(g); _label = "Take a road up"; }
        }
        if (Tool == CityTool.Road && _line is { } ln)
        {
            _line = null;
            var pts = CityEdit.Bent(ln.A, ln.B, _ctrl);
            if (pts.Count > 1)
            {
                var name = NameRoad?.Invoke(_map) ?? "";
                var (m, road) = CityEdit.AddRoad(_map, pts, RoadWidth, RoadGround, name);
                var stopped = CityEdit.StopAtBuildings(m, road);
                if (stopped is null) Said?.Invoke("A road can't start inside a building.");
                else
                {
                    if (!ReferenceEquals(stopped, road))
                    {
                        m = m with { Roads = [.. m.Roads.Select(r => r.Id == road.Id ? stopped : r)] };
                        Said?.Invoke("The road stops at the building in its way: roads can't run through buildings.");
                    }
                    _map = m;
                    SetPick(CityPick.Of(stopped));
                    _changed = true;
                    _label = "Lay a road";
                }
            }
        }
        if (Tool == CityTool.Fence && _line is { } fl)
        {
            _line = null;
            if (fl.A.DistanceTo(fl.B) > 1e-6 && (!FenceRectangle || (Math.Abs(fl.A.X - fl.B.X) > 1e-6 && Math.Abs(fl.A.Y - fl.B.Y) > 1e-6)))
            {
                var (m, fence) = FenceRectangle ? CityEdit.AddFenceRectangle(_map, FenceKind, fl.A, fl.B, FenceRuined) : CityEdit.AddFence(_map, FenceKind, fl.A, fl.B, FenceRuined);
                _map = m;
                _changed = true;
                _label = FenceKind == FenceKind.CityWall ? "Build a town wall" : "Put up a fence";
            }
        }
        if (Tool == CityTool.Ground && _ground is not null)
        {
            if (_changed) _map = _map.WithGround(_ground);
            _ground = null;
        }
        if (_handle is { } h && _handleFrom is { } before)
        {
            _handle = null;
            // A road dragged into a building goes back where it was.
            if (h.Road && _map.Road(h.Id) is { } r && CityEdit.Hits(_map, r, CityEdit.BuildingCells(_map)))
            {
                _map = before;
                _changed = false;
                Said?.Invoke("A road can't run through a building: it's back where it was.");
            }
            _handleFrom = null;
        }
        if (_moveFrom is not null)
        {
            var from = _moveFrom;
            _moveFrom = null;
            if (_changed && _movePick.Roads.Count > 0)
            {
                var cells = CityEdit.BuildingCells(_map);
                if (_map.Roads.Where(r => Pick.Roads.Contains(r.Id)).Any(r => CityEdit.Hits(_map, r, cells)))
                {
                    _map = from;
                    _changed = false;
                    Said?.Invoke("A road can't run through a building: it's back where it was.");
                }
            }
            PickChanged?.Invoke();
        }
        Finish();
    }

    /// <summary>Tells the window what was done (for undo), if anything was.</summary>
    void Finish()
    {
        if (_changed)
        {
            RedrawGround();
            Edited?.Invoke(_map, _label ?? Tool switch
            {
                CityTool.Select => "Move",
                CityTool.Ground => "Paint the ground",
                CityTool.Fill => "Fill the ground",
                CityTool.Building => "Add a building",
                CityTool.Place => "Put down a building",
                CityTool.Fence => "Change fences",
                CityTool.Prop => _right ? "Take away trees" : "Add a tree",
                CityTool.Road => "Lay a road",
                CityTool.Label => _right ? "Take a label away" : "Add a label",
                _ => "Rub out",
            });
            _changed = false;
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        Hovered?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when !Pick.IsEmpty:
                DeletePicked();
                break;
            case Key.R or Key.OemCloseBrackets or Key.OemOpenBrackets:
            {
                int by = e.Key == Key.OemOpenBrackets || e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                if (Tool == CityTool.Place) { PlaceTurn = ((PlaceTurn + by) % 4 + 4) % 4; InvalidateVisual(); }
                else if (Tool == CityTool.Label && Pick.IsEmpty) { LabelTurn = CityLabel.Upright(LabelTurn + 15 * by); LabelTurnChanged?.Invoke(LabelTurn); InvalidateVisual(); }
                else if (_down && _moveFrom is not null)
                {
                    (_map, var pick, _) = CityEdit.Turn(_map, Pick, by);
                    Pick = pick;
                    _moveFrom = _map;
                    _movePick = pick;
                    _grabAt = _hover ?? _grabAt;
                    _changed = true;
                    InvalidateVisual();
                }
                else if (Pick.Count == 1 && Pick.Labels.Count == 1) TurnLabel(15 * by);
                else TurnPicked(by);
                break;
            }
            case Key.Left or Key.Right or Key.Up or Key.Down when !Pick.IsEmpty && !_down:
                Nudge(e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0);
                break;
            case Key.Escape:
                _box = null;
                _line = null;
                SetPick(CityPick.None);
                break;
            case Key.C when ctrl:
                CopyPicked();
                break;
            case Key.X when ctrl:
                CutPicked();
                break;
            case Key.V when ctrl:
                PasteClip();
                break;
            case Key.D when ctrl:
                DuplicatePicked();
                break;
            case Key.A when ctrl:
                SetPick(CityEdit.Within(_map, 0, 0, _map.Across, _map.Down));
                break;
            case Key.F when !ctrl:
                Fit();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---- what's picked, all at once ----

    void Apply(CityMap m, CityPick pick, string label)
    {
        if (ReferenceEquals(m, _map)) return;
        _map = m;
        SetPick(pick);
        Edited?.Invoke(_map, label);
    }

    CityMap? _turned;
    (double X, double Y) _pivot;

    /// <summary>Turns what's picked a quarter turn (clockwise; -1 the other way) about its middle: turned again straight away, about the same point.</summary>
    public void TurnPicked(int by)
    {
        if (Pick.IsEmpty) return;
        var (m, pick, pivot) = CityEdit.Turn(_map, Pick, by, ReferenceEquals(_map, _turned) ? _pivot : null);
        if (pick.Roads.Count > 0)
        {
            var cells = CityEdit.BuildingCells(m);
            if (m.Roads.Where(r => pick.Roads.Contains(r.Id)).Any(r => CityEdit.Hits(m, r, cells)))
            {
                Said?.Invoke("Turned, a road would run through a building.");
                return;
            }
        }
        Apply(m, pick, Pick.Buildings.Count == 1 && pick.Count == 1 ? "Turn a building" : "Turn");
        (_turned, _pivot) = (_map, pivot);
    }

    /// <summary>Turns the one label picked by some degrees, kept the right way up.</summary>
    public void TurnLabel(double deg)
    {
        if (Pick.Labels.Count != 1) return;
        var id = Pick.Labels.First();
        Apply(_map with { Labels = [.. _map.Labels.Select(l => l.Id == id ? l with { TurnDeg = CityLabel.Upright(l.TurnDeg + deg) } : l)] }, Pick, "Turn a label");
    }

    public void Nudge(int dx, int dy)
    {
        var (m, pick) = CityEdit.Move(_map, Pick, dx, dy);
        if (pick.Roads.Count > 0)
        {
            var cells = CityEdit.BuildingCells(m);
            if (m.Roads.Where(r => pick.Roads.Contains(r.Id)).Any(r => CityEdit.Hits(m, r, cells))) { Said?.Invoke("A road can't run through a building."); return; }
        }
        Apply(m, pick, "Move");
    }

    public void DeletePicked()
    {
        if (Pick.IsEmpty) return;
        Apply(CityEdit.Delete(_map, Pick), CityPick.None, Pick.Buildings.Count == 1 && Pick.Count == 1 ? "Take a building away" : "Take away");
    }

    public void CopyPicked()
    {
        if (!Pick.IsEmpty) Copied = CityEdit.Copy(_map, Pick);
    }

    public void CutPicked()
    {
        CopyPicked();
        if (!Pick.IsEmpty) Apply(CityEdit.Delete(_map, Pick), CityPick.None, "Cut");
    }

    /// <summary>Puts down what was copied: its top left corner on the square under the pointer (or where it came from).</summary>
    public void PasteClip()
    {
        if (Copied is not { IsEmpty: false } clip) return;
        int x, y;
        if (_hover is { } h && CellAt(h) is { } c) (x, y) = c;
        else (x, y) = (Math.Max(0, (_map.Across - clip.Width) / 2), Math.Max(0, (_map.Down - clip.Depth) / 2));
        var (m, pick) = CityEdit.Paste(_map, clip, x, y);
        Apply(m, pick, "Paste");
    }

    /// <summary>A copy of what's picked beside it (to the right, or below when there's no room).</summary>
    public void DuplicatePicked()
    {
        if (Pick.IsEmpty || CityEdit.Bounds(_map, Pick) is not { } bb) return;
        var clip = CityEdit.Copy(_map, Pick);
        int x0 = (int)Math.Floor(bb.X0), y0 = (int)Math.Floor(bb.Y0);
        bool right = x0 + clip.Width + 1 + clip.Width <= _map.Across;
        var (m, pick) = CityEdit.Paste(_map, clip, right ? x0 + clip.Width + 1 : x0, right ? y0 : y0 + clip.Depth + 1);
        Apply(m, pick, "Copy");
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

    /// <summary>Takes a square's length of fence away round the pointer (a gap, splitting it in two).</summary>
    void CutFenceAt((double X, double Y) p)
    {
        if (FenceAt(p) is not { } f) return;
        var cut = CityEdit.CutOut(_map, f.Id, new CityPt(p.X, p.Y), Snap == CitySnap.Squares ? 1 : 0.5);
        if (ReferenceEquals(cut, _map)) return;
        _map = cut;
        _changed = true;
        _label = "Take away some fence";
    }

    void RemovePropAt((double X, double Y) p)
    {
        int before = _map.Props.Count;
        var left = _map.Props.Where(q => Prop.Footprint(q.Kind) is null ? (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y) > 0.25 : !q.Hit(p.X, p.Y)).ToList();
        if (left.Count == before) return;
        _map = _map with { Props = left };
        _changed = true;
    }

    void EraseAt((double X, double Y) p)
    {
        if (LabelAt(p) is { } l)
        {
            _map = _map with { Labels = [.. _map.Labels.Where(x => x.Id != l.Id)] };
            _changed = true;
            return;
        }
        int props = _map.Props.Count;
        RemovePropAt(p);
        if (_map.Props.Count != props) return;
        if (FenceAt(p) is not null)
        {
            CutFenceAt(p);
            return;
        }
        if (BuildingAt(p) is { } b)
        {
            _map = _map.Without(b.Id);
            if (Pick.Buildings.Contains(b.Id)) SetPick(Pick.On(_map));
            _changed = true;
            return;
        }
        if (RoadAt(p) is { } r)
        {
            _map = _map with { Roads = [.. _map.Roads.Where(x => x.Id != r.Id)] };
            if (Pick.Roads.Contains(r.Id)) SetPick(Pick.On(_map));
            _changed = true;
        }
    }
}
