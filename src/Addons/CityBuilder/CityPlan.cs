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
    /// <summary>What the Label tool writes, and how tall (squares).</summary>
    public string LabelText { get; set; } = "Market Street";
    public double LabelSize { get; set; } = 0.8;
    /// <summary>Colour the buildings by their district.</summary>
    public bool DistrictColours { get => _districts; set { _districts = value; InvalidateVisual(); } }
    bool _districts;

    /// <summary>What's picked: buildings, trees and things, labels and stretches of fence.</summary>
    public CityPick Pick { get; private set; } = CityPick.None;

    /// <summary>The one building picked (when that's all that's picked).</summary>
    public Guid? Selected => Pick.Buildings.Count == 1 && Pick.Props.Count == 0 && Pick.Labels.Count == 0 && Pick.Fences.Count == 0 ? Pick.Buildings.First() : null;

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
        {
            var l = _map.Labels[i];
            if (Math.Abs(p.X - l.X) <= l.Width / 2 + 0.1 && Math.Abs(p.Y - l.Y) <= l.Size / 2 + 0.1) return l;
        }
        return null;
    }

    /// <summary>The one thing under the pointer, as a pick: a label, a tree or thing, a building, or a fence's whole run.</summary>
    CityPick? HitAt((double X, double Y) p)
    {
        if (LabelAt(p) is { } l) return CityPick.Of(l);
        if (PropAt(p) is { } q) return CityPick.Of(q);
        if (BuildingAt(p) is { } b) return CityPick.Of(b);
        if (EdgeAt(p) is { } e && _map.Fences.Any(f => f.Edges.Contains(e))) return new CityPick { Fences = CityEdit.Run(_map, e) };
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
        if (Pick.Fences.Count > 0)
        {
            var pen = new Pen(PickBrush, Math.Max(2, k * 0.12), lineCap: PenLineCap.Square);
            foreach (var (ax, ay, bx, by) in CityScene.Runs(Pick.Fences)) ctx.DrawLine(pen, At(ax, ay), At(bx, by));
        }

        // Buildings: their squares, roofs, walls with the doors and windows in them.
        var overlaps = CityFootprint.Overlaps(_map).SelectMany(p => new[] { p.A.Id, p.B.Id }).ToHashSet();
        foreach (var b in _map.Buildings) DrawBuilding(ctx, b, k, overlaps.Contains(b.Id));

        // Trees and the like, and the labels.
        foreach (var p in _map.Props) DrawProp(ctx, p, k);
        foreach (var l in _map.Labels) DrawLabel(ctx, l, k, Pick.Labels.Contains(l.Id));

        // The last box dragged, to fill with buildings.
        if (LastBox is { } lb && Tool == CityTool.Select && _box is null)
            ctx.DrawRectangle(null, new Pen(PickBrush, 1) { DashStyle = new DashStyle([4, 3], 0) }, new Rect(At(lb.X0, lb.Y0), At(lb.X1, lb.Y1)));

        // What the pointer is over, and what's being dragged.
        DrawHover(ctx, k);
        if (_map.Buildings.Count == 0 && _map.Props.Count == 0 && _map.Labels.Count == 0)
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
        if (CityFootprint.OnGrid(b))
            foreach (var (x, y) in cells) ctx.FillRectangle(overlapping ? OverlapBrush : fill, new Rect(At(x, y), new Size(k + 0.5, k + 0.5)));
        else
        {
            // Off the grid (shifted or tilted): its real outline, not the squares under it.
            var corners = CityFootprint.Corners(b).Select(c => At(c.X, c.Y)).ToList();
            ctx.DrawGeometry(overlapping ? OverlapBrush : fill, null, new PolylineGeometry(corners, true));
        }
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
            var corners = CityFootprint.Corners(b).Select(c => At(c.X, c.Y)).ToList();
            ctx.DrawGeometry(null, new Pen(PickBrush, 2) { DashStyle = new DashStyle([3, 2], 0) }, new PolylineGeometry(corners, true));
        }
        if (k >= 10)
        {
            var (cx, cy) = CityFootprint.Centre(b);
            var mid = At(cx, cy);
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
        double size = Math.Max(4, l.Size * k);
        var ft = new FormattedText(l.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), size, Brushes.White);
        var mid = At(l.X, l.Y);
        var p = new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2);
        var back = new FormattedText(l.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), size, new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)));
        foreach (var (dx, dy) in new[] { (1.0, 1.0), (-1.0, 1.0), (1.0, -1.0), (-1.0, -1.0) }) ctx.DrawText(back, p + new Point(dx, dy));
        ctx.DrawText(ft, p);
        if (picked) ctx.DrawRectangle(null, new Pen(PickBrush, 1.5) { DashStyle = new DashStyle([3, 2], 0) }, new Rect(p.X - 3, p.Y - 2, ft.Width + 6, ft.Height + 4));
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
            var brush = new SolidColorBrush(CityScene.GroundColour(RoadGround), 0.75);
            foreach (var (x, y) in RoadCells(rd)) if (_map.In(x, y)) ctx.FillRectangle(brush, new Rect(At(x, y), new Size(k + 0.5, k + 0.5)));
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
            case CityTool.Road when CellAt(hp) is { } c:
            {
                int n = Math.Max(1, RoadWidth), h0 = (n - 1) / 2;
                ctx.FillRectangle(HoverBrush, new Rect(At(c.X - h0, c.Y - h0), new Size(k * n, k * n)));
                break;
            }
            case CityTool.Label when !string.IsNullOrWhiteSpace(LabelText):
                using (ctx.PushOpacity(0.6)) DrawLabel(ctx, new CityLabel { Text = LabelText.Trim(), X = hp.X, Y = hp.Y, Size = LabelSize }, k, false);
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
    ((double X, double Y) A, (double X, double Y) B)? _box;
    bool _boxAdd;
    Ground[,]? _ground;
    CityMap? _moveFrom;
    CityPick _movePick = CityPick.None;
    (double X, double Y) _grabAt;
    bool? _fenceAdd;
    readonly HashSet<FenceEdge> _stroke = [];

    HashSet<(int X, int Y)> RoadCells(((int X, int Y) A, (int X, int Y) B) d) => CityEdit.Road(d.A.X, d.A.Y, d.B.X, d.B.Y, RoadWidth, _ctrl);

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
        _stroke.Clear();
        _fenceAdd = null;
        _moveFrom = null;
        e.Pointer.Capture(this);
        var p = ToMap(pt.Position);
        switch (Tool)
        {
            case CityTool.Select:
            {
                var hit = HitAt(p);
                LastBox = null;
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
                if (CellAt(p) is { } rc) _drag = (rc, rc);
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
                FenceAt(p);
                break;
            case CityTool.Prop:
                if (_right) RemovePropAt(p);
                else
                {
                    _map = _map with { Props = [.. _map.Props, new Prop { Kind = PropKind, X = Math.Round(p.X * 4) / 4, Y = Math.Round(p.Y * 4) / 4, TurnDeg = Prop.Footprint(PropKind) is null ? Random.Shared.Next(360) : 0, Size = PropKind is PropKind.Tree or PropKind.Pine ? 1.2 : 1 }] };
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
                    var l = new CityLabel { Text = LabelText.Trim(), X = Math.Round(p.X * 4) / 4, Y = Math.Round(p.Y * 4) / 4, Size = LabelSize };
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
            _ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            switch (Tool)
            {
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
        e.Pointer.Capture(null);
        if (_panning)
        {
            _panning = false;
            return;
        }
        if (!_down) return;
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
            var to = _right ? Ground.Grass : RoadGround;
            foreach (var (x, y) in RoadCells(rd))
                if (_map.In(x, y) && g[x, y] != to) { g[x, y] = to; _changed = true; }
            if (_changed) _map = _map.WithGround(g);
        }
        if (Tool == CityTool.Ground && _ground is not null)
        {
            if (_changed) _map = _map.WithGround(_ground);
            _ground = null;
        }
        if (_moveFrom is not null)
        {
            _moveFrom = null;
            PickChanged?.Invoke();
        }
        if (_changed)
        {
            RedrawGround();
            Edited?.Invoke(_map, Tool switch
            {
                CityTool.Select => "Move",
                CityTool.Ground => "Paint the ground",
                CityTool.Fill => "Fill the ground",
                CityTool.Building => "Add a building",
                CityTool.Place => "Put down a building",
                CityTool.Fence => "Change fences",
                CityTool.Prop => _right ? "Take away trees" : "Add a tree",
                CityTool.Road => _right ? "Take a road up" : "Lay a road",
                CityTool.Label => _right ? "Take a label away" : "Add a label",
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
                else TurnPicked(by);
                break;
            }
            case Key.Left or Key.Right or Key.Up or Key.Down when !Pick.IsEmpty && !_down:
                Nudge(e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0);
                break;
            case Key.Escape:
                _box = null;
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
        Apply(m, pick, Pick.Buildings.Count == 1 && pick.Props.Count + pick.Labels.Count + pick.Fences.Count == 0 ? "Turn a building" : "Turn");
        (_turned, _pivot) = (_map, pivot);
    }

    public void Nudge(int dx, int dy)
    {
        var (m, pick) = CityEdit.Move(_map, Pick, dx, dy);
        Apply(m, pick, "Move");
    }

    public void DeletePicked()
    {
        if (Pick.IsEmpty) return;
        Apply(CityEdit.Delete(_map, Pick), CityPick.None, Pick.Buildings.Count == 1 && Pick.Props.Count + Pick.Labels.Count + Pick.Fences.Count == 0 ? "Take a building away" : "Take away");
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
        if (EdgeAt(p) is { } e && _map.Fences.Any(f => f.Edges.Contains(e)))
        {
            _map = _map with { Fences = [.. _map.Fences.Select(f => f with { Edges = [.. f.Edges.Where(x => x != e)] }).Where(f => f.Edges.Count > 0)] };
            _changed = true;
            return;
        }
        if (BuildingAt(p) is { } b)
        {
            _map = _map.Without(b.Id);
            if (Pick.Buildings.Contains(b.Id)) SetPick(Pick.On(_map));
            _changed = true;
        }
    }
}
