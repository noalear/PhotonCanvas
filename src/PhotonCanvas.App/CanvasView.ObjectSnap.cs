using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.App;

/// <summary>
/// Object snaps, as in AutoCAD: points picked on the work area (drawing, editing points, measuring,
/// moving a shape by one of its points) land on the ends, middles, centres, quadrants and crossings
/// of other shapes, and on the perpendicular or tangent from where a line starts. A marker shows
/// which. Pausing on one acquires it for tracking: move straight across or down from it and the
/// pointer lines up with it. The Line tool also draws click by click, with typed lengths.
/// </summary>
public sealed partial class CanvasView
{
    /// <summary>Object snaps on (F3 turns them off and on).</summary>
    public bool ObjectSnapOn { get; set; } = true;
    public SnapKinds ObjectSnaps { get; set; } = SnapKinds.Default;
    /// <summary>Pause on a snap point to line up with it across or down.</summary>
    public bool Tracking { get; set; } = true;

    const SnapKinds GrabKinds = SnapKinds.Endpoint | SnapKinds.Midpoint | SnapKinds.Center | SnapKinds.Quadrant | SnapKinds.Intersection;
    static readonly IReadOnlySet<Guid> NoShapes = new HashSet<Guid>();

    readonly ConditionalWeakTable<Shape, SnapFeatures> _snapCache = new();
    /// <summary>The snap the pointer is on now (its marker is drawn), or null.</summary>
    SnapHit? _snapHit;
    /// <summary>Points acquired for tracking (pause on a snap point).</summary>
    readonly List<Vec2> _acquired = [];
    SnapHit? _acquiring;
    DispatcherTimer? _acquireTimer;
    /// <summary>A shape being moved is held by this snap point of it (it snaps onto other shapes' points).</summary>
    Vec2? _grab;

    // The Line tool, click by click: where the next line starts, and a length being typed.
    Vec2? _lineFrom;
    string _typed = "";

    /// <summary>A line is being drawn click by click (keys go to it: typed lengths, Enter, Esc, Backspace).</summary>
    public bool LineInProgress => _tool == Tool.Line && _lineFrom is not null;

    SnapFeatures? FeaturesOf(Shape s) => s switch
    {
        TextShape or CodeShape => null, // letters would snap everywhere
        PathShape p => _snapCache.GetValue(s, _ => ObjectSnap.Features(p.World)),
        ImageShape im => _snapCache.GetValue(s, _ => ObjectSnap.Features(im.Corners)),
        _ => null,
    };

    /// <summary>The object snap near <paramref name="w"/> on the shapes <paramref name="which"/> allows (visible ones), or null.</summary>
    SnapHit? FindObjectSnap(Vec2 w, Func<Shape, bool>? which, Vec2? from, SnapKinds? kinds = null)
    {
        if (_doc is null || !ObjectSnapOn || SnapSuspended) return null;
        double tol = SnapDistancePx / _scale;
        var visible = _doc.State.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet();
        var near = new List<SnapFeatures>();
        foreach (var s in _doc.State.Shapes)
        {
            if (!visible.Contains(s.LayerId) || which?.Invoke(s) == false || !s.Bounds.Inflate(tol).Contains(w)) continue;
            if (FeaturesOf(s) is { } f) near.Add(f);
        }
        return ObjectSnap.Find(near, w, tol, kinds ?? ObjectSnaps, from);
    }

    /// <summary>
    /// Where a point picked at <paramref name="w"/> lands: on an object snap when one is near (its marker
    /// shows), else lined up with other shapes, tracked points or the grid. <paramref name="from"/> is
    /// where the line being drawn starts (perpendicular, tangent, and lining up with it).
    /// </summary>
    Vec2 SnapPick(Vec2 w, IReadOnlySet<Guid>? exclude = null, Vec2? from = null)
    {
        _snapHit = null;
        if (_doc is null || SnapSuspended) { _guides.Clear(); NoteHover(null); return w; }
        var skip = exclude ?? NoShapes;
        if (FindObjectSnap(w, s => !skip.Contains(s.Id), from) is { } hit)
        {
            _snapHit = hit;
            _guides.Clear();
            NoteHover(hit);
            return hit.At;
        }
        NoteHover(null);
        PrepareSnapFor(skip);
        AddTracking(from);
        return SnapPoint(w);
    }

    IReadOnlySet<Guid>? _preparedSkip;
    DocState? _preparedState;
    int _baseX, _baseY;

    /// <summary>The lines to line up with (other shapes, guides, the work area), worked out once per design and set of shapes left out.</summary>
    void PrepareSnapFor(IReadOnlySet<Guid> skip)
    {
        if (_doc is null) return;
        if (!ReferenceEquals(_preparedState, _doc.State) || !ReferenceEquals(_preparedSkip, skip))
        {
            PrepareSnap(skip);
            _preparedState = _doc.State;
            _preparedSkip = skip;
            _baseX = _snapX.Count;
            _baseY = _snapY.Count;
        }
        if (_snapX.Count > _baseX) _snapX.RemoveRange(_baseX, _snapX.Count - _baseX);
        if (_snapY.Count > _baseY) _snapY.RemoveRange(_baseY, _snapY.Count - _baseY);
    }

    /// <summary>Lines across and down through the acquired points and where the line starts.</summary>
    void AddTracking(Vec2? from)
    {
        if (!Tracking || !SnapToObjects) return;
        foreach (var p in from is { } f ? _acquired.Append(f) : _acquired)
        {
            _snapX.Add(new(p.X, p.Y, p.Y));
            _snapY.Add(new(p.Y, p.X, p.X));
        }
    }

    /// <summary>Pausing on a snap point acquires it for tracking (pausing on an acquired one lets it go).</summary>
    void NoteHover(SnapHit? hit)
    {
        if (!Tracking) return;
        if (hit is { } h && _acquiring is { } a && a.At.Dist(h.At) < 1e-6) return; // still on it
        _acquiring = hit;
        _acquireTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Normal, (_, _) => Acquire());
        _acquireTimer.Stop();
        if (hit is not null) _acquireTimer.Start();
    }

    void Acquire()
    {
        _acquireTimer?.Stop();
        if (_acquiring is not { } h || _snapHit is not { } now || now.At.Dist(h.At) > 1e-6) return;
        int at = _acquired.FindIndex(p => p.Dist(h.At) < 1e-6);
        if (at >= 0) _acquired.RemoveAt(at);
        else
        {
            _acquired.Add(h.At);
            if (_acquired.Count > 4) _acquired.RemoveAt(0);
        }
        _preparedState = null; // the tracking lines change
        InvalidateVisual();
    }

    void ResetSnaps()
    {
        _snapHit = null;
        _acquired.Clear();
        _acquiring = null;
        _acquireTimer?.Stop();
        _grab = null;
        _lineFrom = null;
        _typed = "";
    }

    /// <summary>A tool that picks points: the pointer shows where a click would land.</summary>
    bool PicksPoints => _tool is Tool.Line or Tool.Rect or Tool.Ellipse or Tool.Polygon or Tool.Star or Tool.Pen or Tool.Measure or Tool.Align2;

    /// <summary>Where the current point-picking tool's line starts (for perpendicular and tangent), or null.</summary>
    Vec2? PickFrom => _tool switch
    {
        Tool.Line => _lineFrom ?? (_drag == Drag.Create ? _pressWorld : null),
        Tool.Pen => _pen.Count > 0 ? _pen[^1].At : null,
        Tool.Measure => _measuring ? _measureA : null,
        _ => null,
    };

    // ---- the Line tool, click by click --------------------------------------------------

    /// <summary>The end of a line from <paramref name="a"/> to <paramref name="b"/>, kept to 45° steps while Shift is held.</summary>
    Vec2 LineEnd(Vec2 a, Vec2 b)
    {
        if (!_lastMods.HasFlag(KeyModifiers.Shift)) return b;
        var d = b - a;
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4), len = d.Length;
        return a + new Vec2(Math.Cos(ang) * len, Math.Sin(ang) * len);
    }

    /// <summary>Draws the line and carries on from its end.</summary>
    void AddLine(Vec2 a, Vec2 b)
    {
        if (_doc is null || a.Dist(b) < 0.01 || DrawLayer() is not { } lay) return;
        var shape = new PathShape { Name = "Line", LayerId = lay.Id, Geometry = PathGeometry.Line(a, b) };
        // Not selected: handles round each new line would get in the way of the next one.
        _doc.Commit("Draw line", Ops.AddShapes(_doc.State, [shape]), []);
        _lineFrom = b;
        _typed = "";
    }

    /// <summary>The typed length (and angle, "25&lt;30"), in the units shown, as an end point; null if it doesn't read.</summary>
    Vec2? TypedEnd()
    {
        if (_lineFrom is not { } a || _typed.Length == 0) return null;
        var parts = _typed.Split('<');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double len) || !(Math.Abs(len) > 0)) return null;
        len = Units.ToMm(len);
        double ang;
        if (parts.Length > 1)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)) return null;
            ang = -deg * Math.PI / 180; // counter-clockwise from the right, as on paper (Y runs down on the work area)
        }
        else
        {
            var to = LineEnd(a, _createEnd) - a;
            ang = to.Length < 1e-9 ? 0 : Math.Atan2(to.Y, to.X);
        }
        return a + new Vec2(Math.Cos(ang), Math.Sin(ang)) * len;
    }

    /// <summary>Keys for a line being drawn click by click. True if used.</summary>
    bool LineKey(KeyEventArgs e)
    {
        if (!LineInProgress) return false;
        switch (e.Key)
        {
            case Key.Enter:
                if (TypedEnd() is { } end) AddLine(_lineFrom!.Value, end);
                else if (_typed.Length == 0) _lineFrom = null; // Enter on its own: done
                InvalidateVisual();
                return true;
            case Key.Escape:
                if (_typed.Length > 0) _typed = "";
                else _lineFrom = null;
                InvalidateVisual();
                return true;
            case Key.Back or Key.Delete:
                if (_typed.Length > 0) _typed = _typed[..^1];
                InvalidateVisual();
                return true;
        }
        return false;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!LineInProgress || string.IsNullOrEmpty(e.Text)) return;
        foreach (char ch in e.Text)
        {
            char c = ch == ',' ? '.' : ch;
            if (char.IsDigit(c) || c == '.' || c == '-' || (c == '<' && !_typed.Contains('<'))) _typed += c;
            else return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    // ---- drawing --------------------------------------------------------------------------

    static readonly IBrush MarkerInk = new SolidColorBrush(Color.Parse("#F5D90A"));
    static readonly IPen MarkerPen = new Pen(MarkerInk, 2);
    static readonly IPen MarkerHalo = new Pen(new SolidColorBrush(Color.FromArgb(200, 10, 12, 16)), 4.5);
    static readonly IPen TrackPen = new Pen(new SolidColorBrush(Color.Parse("#4ADE80")), 1.5);

    static string SnapName(SnapKind k) => k switch
    {
        SnapKind.Endpoint => "Endpoint",
        SnapKind.Midpoint => "Midpoint",
        SnapKind.Center => "Centre",
        SnapKind.Quadrant => "Quadrant",
        SnapKind.Intersection => "Intersection",
        SnapKind.Perpendicular => "Perpendicular",
        SnapKind.Tangent => "Tangent",
        _ => "Nearest",
    };

    /// <summary>The snap marker (its shape says what kind of point it is, as in AutoCAD) and its name; the acquired points.</summary>
    void DrawSnaps(DrawingContext ctx)
    {
        if (Tracking && (PicksPoints || _drag is Drag.Move or Drag.NodeAnchor))
            foreach (var a in _acquired)
            {
                var q = ToScreen(a);
                ctx.DrawLine(TrackPen, new Point(q.X - 5, q.Y), new Point(q.X + 5, q.Y));
                ctx.DrawLine(TrackPen, new Point(q.X, q.Y - 5), new Point(q.X, q.Y + 5));
            }
        if (_snapHit is not { } hit) return;
        var p = ToScreen(hit.At);
        const double S = 6;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            void Poly(bool closed, params Point[] pts)
            {
                c.BeginFigure(pts[0], false);
                foreach (var q in pts.Skip(1)) c.LineTo(q);
                c.EndFigure(closed);
            }
            Point At(double x, double y) => new(p.X + x, p.Y + y);
            switch (hit.Kind)
            {
                case SnapKind.Endpoint: Poly(true, At(-S, -S), At(S, -S), At(S, S), At(-S, S)); break;
                case SnapKind.Midpoint: Poly(true, At(0, -S), At(S, S * 0.75), At(-S, S * 0.75)); break;
                case SnapKind.Quadrant: Poly(true, At(0, -S), At(S, 0), At(0, S), At(-S, 0)); break;
                case SnapKind.Intersection: Poly(false, At(-S, -S), At(S, S)); Poly(false, At(S, -S), At(-S, S)); break;
                case SnapKind.Perpendicular: Poly(false, At(-S, -S), At(-S, S), At(S, S)); Poly(false, At(-S, 0), At(0, 0), At(0, S)); break;
                case SnapKind.Nearest: Poly(true, At(-S, -S), At(S, -S), At(-S, S), At(S, S)); break;
                case SnapKind.Tangent: Poly(false, At(-S, -S), At(S, -S)); break;
            }
        }
        if (hit.Kind is SnapKind.Center or SnapKind.Tangent)
        {
            ctx.DrawEllipse(null, MarkerHalo, p, S, S);
            ctx.DrawEllipse(null, MarkerPen, p, S, S);
        }
        ctx.DrawGeometry(null, MarkerHalo, g);
        ctx.DrawGeometry(null, MarkerPen, g);
        var label = new FormattedText(SnapName(hit.Kind), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, MarkerInk);
        var box = new Rect(p.X + 11, p.Y + 9, label.Width + 8, label.Height + 4);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(215, 14, 16, 22)), box, 3);
        ctx.DrawText(label, new Point(box.X + 4, box.Y + 2));
    }

    /// <summary>The line being drawn click by click, and its length and angle (or the length being typed) by the pointer.</summary>
    void DrawLineInProgress(DrawingContext ctx)
    {
        Vec2 a, b;
        if (_lineFrom is { } from) { a = from; b = TypedEnd() ?? LineEnd(from, _createEnd); }
        else if (_drag == Drag.Create && _tool == Tool.Line) { a = _pressWorld; b = LineEnd(a, _createEnd); }
        else return;
        if (_lineFrom is not null)
            ctx.DrawLine(new Pen(new SolidColorBrush(AccentColor), 1.5), ToScreen(a), ToScreen(b));
        var d = b - a;
        double deg = (Math.Atan2(-d.Y, d.X) * 180 / Math.PI + 360) % 360;
        string text = _typed.Length > 0
            ? $"Length: {_typed}▏  (Enter places it; 25<30 = 25 at 30°)"
            : $"{Units.Len(d.Length)}  ∠ {deg:0.#}°" + (_lineFrom is not null ? "   type a length, Enter" : "");
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11.5, Brushes.White);
        var p = ToScreen(b);
        var box = new Rect(p.X + 14, p.Y - label.Height - 14, label.Width + 10, label.Height + 6);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(225, 22, 26, 33)), box, 4);
        ctx.DrawText(label, new Point(box.X + 5, box.Y + 3));
    }
}
