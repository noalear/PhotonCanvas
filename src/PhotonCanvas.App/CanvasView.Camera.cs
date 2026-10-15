using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Tracing;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>The straightened camera picture of the work area, as last laid on the canvas.</summary>
/// <param name="Image">RGBA; see-through where the camera can't see.</param>
/// <param name="Area">What it covers on the bed, mm.</param>
public sealed record CameraPhoto(RasterImage Image, Rect2 Area, double MmPerPx, double? DistanceMm);

/// <summary>Tools that draw from the camera picture: the magic wand (pick a piece) and the magnetic pen.</summary>
public sealed partial class CanvasView
{
    CameraPhoto? _photo;
    ImageTrace.Working? _pickWorking;
    LiveWire? _wire;
    Task? _preparing;

    /// <summary>The latest camera picture of the work area (null = none yet).</summary>
    public CameraPhoto? Photo
    {
        get => _photo;
        set
        {
            _photo = value;
            _pickWorking = null;
            _wire = null;
            _preparing = null;
            _wandPreview = null;
            if (_tool is Tool.Wand or Tool.Magnetic) PreparePhoto();
            InvalidateVisual();
        }
    }

    /// <summary>A short message for the status bar (e.g. why a tool can't work yet).</summary>
    public event Action<string>? Hint;

    PickOptions _wandOptions = new();

    /// <summary>Magic wand settings. Changing them refreshes the preview, and refines the piece just picked.</summary>
    public PickOptions WandOptions
    {
        get => _wandOptions;
        set
        {
            _wandOptions = value;
            if (_tool == Tool.Wand && _cursor is { } c) ScheduleWandPreview(c);
            _ = RefineLastPick();
        }
    }

    /// <summary>Build what the tools need from the photo, once, off the UI thread.</summary>
    Task PreparePhoto()
    {
        if (_photo is not { } photo) return Task.CompletedTask;
        return _preparing ??= Task.Run(() =>
        {
            var working = CameraPick.Prepare(photo.Image);
            var gray = new GrayImage(photo.Image.Width, photo.Image.Height, Luma(photo.Image));
            var wire = LiveWire.Build(gray, photo.Area, photo.MmPerPx);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_photo, photo)) return;
                _pickWorking = working;
                _wire = wire;
                InvalidateVisual();
            });
        });
    }

    static byte[] Luma(RasterImage img)
    {
        var g = new byte[img.Width * img.Height];
        for (int i = 0; i < g.Length; i++)
            g[i] = (byte)((img.Rgba[i * 4] * 77 + img.Rgba[i * 4 + 1] * 150 + img.Rgba[i * 4 + 2] * 29) >> 8);
        return g;
    }

    bool NeedsPhoto()
    {
        if (_photo is not null) return false;
        Hint?.Invoke("Show the camera picture first (Camera tab, or Machine at the right end of the ribbon): these tools draw from it.");
        return true;
    }

    // ---- magic wand ---------------------------------------------------------------------

    PathGeometry? _wandPreview;
    int _wandTicket;
    /// <summary>The piece picked last, so the sliders can refine it until something else happens.</summary>
    (Guid Id, Vec2 Click, PathGeometry Geometry)? _lastPick;

    async Task<PathGeometry> PickAt(Vec2 mm)
    {
        await PreparePhoto();
        var photo = _photo;
        var working = _pickWorking;
        var opt = _wandOptions;
        if (photo is null || working is null) return new PathGeometry([]);
        return await Task.Run(() => CameraPick.Outline(working, photo.Area, photo.MmPerPx, mm, opt));
    }

    void ScheduleWandPreview(Vec2 mm)
    {
        int ticket = ++_wandTicket;
        _ = Later();

        async Task Later()
        {
            await Task.Delay(120);
            if (ticket != _wandTicket || _photo is null) return;
            var g = await PickAt(mm);
            if (ticket != _wandTicket) return;
            _wandPreview = g.IsEmpty ? null : g;
            InvalidateVisual();
        }
    }

    async void WandPressed()
    {
        if (_doc is null || NeedsPhoto()) return;
        var at = _pressWorld;
        _wandTicket++;
        var g = await PickAt(at);
        if (g.IsEmpty)
        {
            Hint?.Invoke("Nothing to pick there: click inside a piece the camera can see.");
            return;
        }
        if (DrawLayer() is not { } layer) return;
        var shape = new PathShape { Name = "Picked piece", LayerId = layer.Id, Geometry = g };
        _doc.Commit("Pick piece from camera", Ops.AddShapes(_doc.State, [shape]), [shape.Id]);
        _lastPick = (shape.Id, at, g);
        _wandPreview = null;
        Hint?.Invoke("Picked. Adjust the sliders to refine it; Offset (Ctrl+Shift+O) or an engrave layer can build on it.");
        InvalidateVisual();
    }

    /// <summary>The sliders moved: re-pick the last piece with the new settings, if it's untouched.</summary>
    int _refineTicket;

    async Task RefineLastPick()
    {
        int ticket = ++_refineTicket;
        if (_doc is null || _lastPick is not { } last) return;
        if (_doc.State.FindShape(last.Id) is not PathShape s || !ReferenceEquals(s.Geometry, last.Geometry) || !s.Transform.IsIdentity)
        {
            _lastPick = null; // moved, edited or deleted since: leave it alone
            return;
        }
        var g = await PickAt(last.Click);
        if (ticket != _refineTicket) return; // a newer slider value is on its way
        if (g.IsEmpty || _lastPick is not { } still || still.Id != last.Id) return;
        if (_doc.State.FindShape(last.Id) is not PathShape cur || !ReferenceEquals(cur.Geometry, last.Geometry)) return;
        var next = new PathShape { Id = cur.Id, Name = cur.Name, LayerId = cur.LayerId, GroupId = cur.GroupId, Geometry = g };
        _doc.Commit("Refine picked piece", Ops.Replace(_doc.State, next), [cur.Id], coalesceKey: $"refine-{cur.Id}");
        _lastPick = (cur.Id, last.Click, g);
    }

    void DrawWand(DrawingContext ctx)
    {
        if (_wandPreview is not { } g) return;
        double px = 1 / _scale;
        var fill = new SolidColorBrush(Color.FromArgb(90, AccentColor.R, AccentColor.G, AccentColor.B));
        var stroke = new Pen(new SolidColorBrush(AccentColor), 2.2 * px, new DashStyle([4, 3], 0));
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            c.SetFillRule(FillRule.EvenOdd); // holes stay holes
            foreach (var sp in g.Subpaths)
            {
                c.BeginFigure(P(sp.Start), true);
                foreach (var seg in sp.Segs)
                {
                    if (seg.Kind == SegKind.Line) c.LineTo(P(seg.To));
                    else c.CubicBezierTo(P(seg.C1), P(seg.C2), P(seg.To));
                }
                c.EndFigure(true);
            }
        }
        ctx.DrawGeometry(fill, stroke, sg);
    }

    // ---- magnetic pen -------------------------------------------------------------------

    readonly List<Vec2> _magAnchors = [];
    readonly List<List<Vec2>> _magSegments = [];
    LiveWire.Tree? _magTree;
    int _magTicket;
    const double MagReachMm = 60;

    Vec2 MagSnap(Vec2 mm) => _wire is { } w ? w.Snap(mm, 7 / _scale) : mm;

    void GrowFrom(Vec2 anchor)
    {
        _magTree = null;
        int ticket = ++_magTicket;
        _ = Grow();

        async Task Grow()
        {
            await PreparePhoto();
            if (_wire is not { } wire || ticket != _magTicket) return;
            var tree = await Task.Run(() => wire.Grow(anchor, MagReachMm));
            if (ticket != _magTicket) return;
            _magTree = tree;
            InvalidateVisual();
        }
    }

    /// <summary>The path from the last point to <paramref name="mm"/>: along edges when ready, straight otherwise.</summary>
    List<Vec2> MagPathTo(Vec2 mm) =>
        _magTree?.PathTo(mm) is { Count: > 1 } p ? p : [_magAnchors[^1], mm];

    async void MagneticPressed(PointerPressedEventArgs e)
    {
        if (_doc is null || NeedsPhoto()) return;
        await PreparePhoto();
        var at = MagSnap(_pressWorld);
        if (_magAnchors.Count == 0)
        {
            _magAnchors.Add(at);
            GrowFrom(at);
            InvalidateVisual();
            return;
        }
        // Click the first point to close the shape.
        if (_magAnchors.Count >= 2 && Dist(ToScreen(_magAnchors[0]), _pressScreen) <= PenSnapPx)
        {
            _magSegments.Add(MagPathTo(_magAnchors[0]));
            FinishMagnetic(closed: true);
            return;
        }
        // Double-click finishes an open line (its first click already added the point).
        if (e.ClickCount >= 2 && _magAnchors.Count >= 2)
        {
            FinishMagnetic(closed: false);
            return;
        }
        _magSegments.Add(MagPathTo(at));
        _magAnchors.Add(at);
        GrowFrom(at);
        InvalidateVisual();
    }

    bool MagneticKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _magAnchors.Count >= 2:
                FinishMagnetic(closed: false);
                return true;
            case Key.Escape when _magAnchors.Count > 0:
                ClearMagnetic();
                return true;
            case Key.Back or Key.Delete when _magAnchors.Count > 0:
                _magAnchors.RemoveAt(_magAnchors.Count - 1);
                if (_magSegments.Count > 0) _magSegments.RemoveAt(_magSegments.Count - 1);
                if (_magAnchors.Count > 0) GrowFrom(_magAnchors[^1]); else _magTree = null;
                InvalidateVisual();
                return true;
        }
        return false;
    }

    void ClearMagnetic()
    {
        _magAnchors.Clear();
        _magSegments.Clear();
        _magTree = null;
        _magTicket++;
        InvalidateVisual();
    }

    void FinishMagnetic(bool closed)
    {
        var pts = new List<Vec2>();
        foreach (var seg in _magSegments)
            foreach (var p in seg)
                if (pts.Count == 0 || pts[^1].Dist(p) > 1e-9) pts.Add(p);
        var photo = _photo;
        if (_doc is null || photo is null || pts.Count < 2 || DrawLayer() is not { } layer) { ClearMagnetic(); return; }
        var g = closed && pts.Count >= 4
            ? CameraPick.FitLoop(pts, photo.Area, photo.MmPerPx, _wandOptions.Smoothness, _wandOptions.CornerAngle)
            : PathGeometry.Polyline(LiveWire.Simplify(pts, photo.MmPerPx * (0.6 + _wandOptions.Smoothness * 0.25)), false);
        var shape = new PathShape { Name = closed ? "Traced shape" : "Traced line", LayerId = layer.Id, Geometry = g };
        _doc.Commit("Draw with magnetic pen", Ops.AddShapes(_doc.State, [shape]), [shape.Id]);
        ClearMagnetic();
    }

    void DrawMagnetic(DrawingContext ctx)
    {
        if (_magAnchors.Count == 0) return;
        double px = 1 / _scale;
        var pen = new Pen(new SolidColorBrush(AccentColor), 1.6 * px);
        foreach (var seg in _magSegments) DrawPolyline(ctx, pen, seg);
        if (_cursor is { } c && _drag == Drag.None)
        {
            var rubber = new Pen(new SolidColorBrush(Color.FromArgb(170, AccentColor.R, AccentColor.G, AccentColor.B)), 1.2 * px, new DashStyle([4, 3], 0));
            DrawPolyline(ctx, rubber, MagPathTo(c));
        }
        foreach (var a in _magAnchors)
        {
            double r = (a == _magAnchors[0] && _magAnchors.Count >= 2 ? 5 : 3.5) * px;
            ctx.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(AccentColor), 1.2 * px), new Rect(a.X - r, a.Y - r, 2 * r, 2 * r));
        }
    }

    static void DrawPolyline(DrawingContext ctx, Pen pen, IReadOnlyList<Vec2> pts)
    {
        if (pts.Count < 2) return;
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            c.BeginFigure(P(pts[0]), false);
            for (int i = 1; i < pts.Count; i++) c.LineTo(P(pts[i]));
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, sg);
    }
}
