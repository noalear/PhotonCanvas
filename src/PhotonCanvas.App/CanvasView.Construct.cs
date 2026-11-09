using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.App;

/// <summary>
/// CAD-style ways to draw (see <see cref="CadDraw"/>): each drawing tool has a "Draw by" choice,
/// and every way but the usual drag is drawn click by click. Points snap like the other tools,
/// a number typed between clicks gives an exact radius, length or width, and steps that want
/// another shape (a line or circle to touch, a circle to mark) pick it instead of a point.
/// </summary>
public sealed partial class CanvasView
{
    readonly Dictionary<Tool, DrawMethod> _methods = [];
    /// <summary>The points (and shapes) clicked so far.</summary>
    readonly List<CadPick> _cad = [];
    /// <summary>A number being typed for the next step.</summary>
    string _cadTyped = "";
    /// <summary>The line or circle under the pointer, when the step wants one.</summary>
    CadTarget? _cadHover;
    /// <summary>The last press added a point (letting go elsewhere adds the next: drag to draw).</summary>
    bool _cadPressAdded;

    /// <summary>Circle by tangent, tangent, radius: the radius (mm).</summary>
    public double TangentRadiusMm { get; set; } = 10;
    /// <summary>How far centrelines and centre marks run past what they're drawn on (mm).</summary>
    public double CentrelineOvershootMm { get; set; } = 3;
    /// <summary>Centrelines go on the layer being drawn on (and are marked) instead of the Centrelines layer.</summary>
    public bool MarkCentrelines { get; set; }

    /// <summary>The ways a tool can draw (empty for tools that only draw one way).</summary>
    public static DrawMethod[] MethodsFor(Tool t) => t switch
    {
        Tool.Rect => CadDraw.RectMethods,
        Tool.Ellipse => CadDraw.EllipseMethods,
        Tool.Polygon => CadDraw.PolygonMethods,
        Tool.Star => CadDraw.StarMethods,
        Tool.Line => CadDraw.LineMethods,
        Tool.Arc => CadDraw.ArcMethods,
        Tool.Centreline => CadDraw.CentrelineMethods,
        _ => [],
    };

    /// <summary>How the tool draws now.</summary>
    public DrawMethod MethodFor(Tool t) =>
        _methods.TryGetValue(t, out var m) && MethodsFor(t).Contains(m) ? m : MethodsFor(t).FirstOrDefault();

    public void SetMethod(Tool t, DrawMethod m)
    {
        if (!MethodsFor(t).Contains(m)) return;
        _methods[t] = m;
        if (t == _tool)
        {
            ResetConstruct();
            _lineFrom = null;
            _typed = "";
            UpdateCursor(null);
        }
        InvalidateVisual();
    }

    /// <summary>The method being drawn click by click now, or null (the tool's usual drag, or not a drawing tool).</summary>
    DrawMethod? ConstructMethod => MethodsFor(_tool).Length > 0 && MethodFor(_tool) is var m && !CadDraw.IsBox(m) ? m : null;

    /// <summary>Points are down for a shape drawn click by click (its keys are its own until it's finished).</summary>
    public bool ConstructInProgress => ConstructMethod is not null && (_cad.Count > 0 || _cadTyped.Length > 0);

    CadOptions CadOptionsNow => new()
    {
        Sides = PolygonSides,
        StarPoints = StarPoints,
        StarInner = StarInner,
        TangentRadiusMm = TangentRadiusMm,
        OvershootMm = CentrelineOvershootMm,
        Square = _lastMods.HasFlag(KeyModifiers.Shift),
        Clockwise = _lastMods.HasFlag(KeyModifiers.Alt),
    };

    void ResetConstruct()
    {
        _cad.Clear();
        _cadTyped = "";
        _cadHover = null;
        _cadPressAdded = false;
    }

    CadStep? CurrentStep(DrawMethod m)
    {
        var steps = CadDraw.Steps(m);
        return _cad.Count < steps.Length ? steps[_cad.Count] : null;
    }

    /// <summary>The typed number as the next point, or null.</summary>
    Vec2? TypedCadPoint(DrawMethod m)
    {
        if (_cadTyped.Length == 0 || _cad.Count == 0) return null;
        if (!double.TryParse(_cadTyped, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        // Angles are degrees; everything else is a length in the units shown.
        bool angle = CurrentStep(m)?.Typed == TypedMeaning.Angle;
        return CadDraw.TypedPoint(m, _cad, _pick ?? _cursor ?? _cad[^1].At, angle ? v : Units.ToMm(v), CadOptionsNow);
    }

    /// <summary>What the next click would add: the snapped pointer (or the typed number), or the line or circle under it.</summary>
    CadPick? PendingPick(DrawMethod m)
    {
        if (CurrentStep(m) is not { } step) return null;
        if (step.PicksShape) return _cadHover is { } t && _cursor is { } c ? new CadPick(c, t) : null;
        if (TypedCadPoint(m) is { } typed) return new CadPick(typed);
        return (_pick ?? _cursor) is { } at ? new CadPick(at) : null;
    }

    // ---- input ------------------------------------------------------------------------------

    void ConstructPressed(DrawMethod m)
    {
        if (CurrentStep(m) is not { } step) { ResetConstruct(); return; }
        CadPick pick;
        if (step.PicksShape)
        {
            if (TargetAt(_pressWorld, step) is not { } t)
            {
                Hint?.Invoke(step.WantsShape ? "Click on a circle, an arc or a shape." : step.WantsCircle && !step.WantsLine ? "Click on a circle or arc." : step.WantsLine && !step.WantsCircle ? "Click on a straight line or a shape's side." : "Click on a line or a circle.");
                return;
            }
            pick = new CadPick(_pressWorld, t);
        }
        else pick = new CadPick(TypedCadPoint(m) ?? SnapPick(_pressWorld, null, _cad.Count > 0 ? _cad[^1].At : null));
        _guides.Clear();
        _cadTyped = "";
        _cadPressAdded = true;
        AddCadPick(m, pick);
    }

    /// <summary>Letting go away from where the button went down is the next point (drag to draw).</summary>
    void ConstructReleased(DrawMethod m, Point p)
    {
        if (!_cadPressAdded) return;
        _cadPressAdded = false;
        if (_cad.Count == 0 || Dist(p, _pressScreen) < DragThreshold * 2) return;
        if (CurrentStep(m) is not { PicksShape: false }) return;
        AddCadPick(m, new CadPick(SnapPick(ToWorld(p), null, _cad[^1].At)));
    }

    void AddCadPick(DrawMethod m, CadPick pick)
    {
        var steps = CadDraw.Steps(m);
        if (_cad.Count + 1 < steps.Length)
        {
            _cad.Add(pick);
            InvalidateVisual();
            return;
        }
        var shape = CadDraw.Build(m, _cad, pick, CadOptionsNow);
        ResetConstruct();
        InvalidateVisual();
        if (shape is null)
        {
            Hint?.Invoke(m is DrawMethod.CircleTanTanRadius or DrawMethod.CircleTanTanTan or DrawMethod.LineTangentTwoCircles
                ? "No circle or line touches those like that: try other ones, or another radius."
                : "Those points don't make one (three in a row, or two the same): try again.");
            return;
        }
        AddConstructed(shape);
    }

    void AddConstructed(CadShape shape)
    {
        if (_doc is null) return;
        var state = _doc.State;
        Layer? layer;
        if (shape.Centreline && !MarkCentrelines)
        {
            (state, layer) = CentreLines.Ensure(state);
            // Hidden, it'd swallow them out of sight: show it.
            if (!layer.Visible)
            {
                var id = layer.Id;
                state = Ops.UpdateLayers(state, new HashSet<Guid> { id }, l => l with { Visible = true });
            }
        }
        else layer = DrawLayer();
        if (layer is null) return;
        var ps = shape.ToShape(layer.Id);
        // Closed shapes are selected and the tool goes back to Select, as after dragging one;
        // lines, arcs and centrelines aren't, and the tool stays on for the next.
        bool closed = shape.Closed && !shape.Centreline;
        _doc.Commit($"Draw {shape.Name.ToLowerInvariant()}", Ops.AddShapes(state, [ps]), closed ? [ps.Id] : []);
        if (shape.Centreline && !MarkCentrelines && CentreLines.Layer(_doc.State) is { } cl)
            Hint?.Invoke(cl.Output
                ? $"{shape.Name} added to the \"{cl.Name}\" layer."
                : $"{shape.Name} added to the \"{cl.Name}\" layer, which isn't marked (turn its output on, or tick Mark centrelines, to mark them).");
        if (closed) Tool = Tool.Select;
    }

    void ConstructMoved(DrawMethod m, Vec2 w)
    {
        if (CurrentStep(m) is { PicksShape: true } step)
        {
            // Picking a shape, not a point: no snap marker, the shape under the pointer lights up.
            _snapHit = null;
            _guides.Clear();
            _cadHover = TargetAt(w, step);
        }
        else _cadHover = null;
    }

    bool ConstructKey(KeyEventArgs e)
    {
        if (ConstructMethod is not { } m) return false;
        switch (e.Key)
        {
            case Key.Enter when _cadTyped.Length > 0:
                if (TypedCadPoint(m) is { } at)
                {
                    _cadTyped = "";
                    AddCadPick(m, new CadPick(at));
                }
                InvalidateVisual();
                return true;
            case Key.Escape when _cadTyped.Length > 0:
                _cadTyped = "";
                InvalidateVisual();
                return true;
            case Key.Escape when _cad.Count > 0:
                ResetConstruct();
                InvalidateVisual();
                return true;
            case Key.Back or Key.Delete when _cadTyped.Length > 0:
                _cadTyped = _cadTyped[..^1];
                InvalidateVisual();
                return true;
            case Key.Back or Key.Delete when _cad.Count > 0:
                _cad.RemoveAt(_cad.Count - 1);
                InvalidateVisual();
                return true;
        }
        return false;
    }

    /// <summary>Digits typed between clicks: an exact radius, length, width or angle. True if used.</summary>
    bool ConstructText(string text)
    {
        if (ConstructMethod is not { } m || _cad.Count == 0 || CurrentStep(m) is not { Typed: not TypedMeaning.None }) return false;
        var add = "";
        foreach (char ch in text)
        {
            char c = ch == ',' ? '.' : ch;
            if (char.IsDigit(c) || c == '.' || (c == '-' && _cadTyped.Length + add.Length == 0)) add += c;
            else return false;
        }
        _cadTyped += add;
        InvalidateVisual();
        return true;
    }

    /// <summary>The line or circle (or, for centre marks, shape) under <paramref name="w"/>, as the step wants it.</summary>
    CadTarget? TargetAt(Vec2 w, CadStep step)
    {
        if (_doc is null) return null;
        double tol = SnapDistancePx / _scale;
        var visible = _doc.State.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet();
        CadTarget? circle = null, line = null, box = null;
        double circleD = tol, lineD = tol, boxArea = double.PositiveInfinity;
        foreach (var s in _doc.State.Shapes)
        {
            if (!visible.Contains(s.LayerId) || !s.Bounds.Inflate(tol).Contains(w) || FeaturesOf(s) is not { } f) continue;
            double onLines = double.PositiveInfinity;
            foreach (var l in f.Lines)
                for (int i = 0; i + 1 < l.Count; i++)
                {
                    double d = Geom.SegmentDistance(w, l[i], l[i + 1]);
                    onLines = Math.Min(onLines, d);
                    if (step.WantsLine && d < lineD && l[i].Dist(l[i + 1]) > 1e-6)
                    {
                        lineD = d;
                        line = new LineTarget(l[i], l[i + 1]);
                    }
                }
            if (step.WantsCircle && onLines <= tol)
                foreach (var c in f.Circles)
                {
                    if (c.Radius <= 0) continue;
                    double d = Math.Abs(w.Dist(c.Center) - c.Radius);
                    if (d < circleD) { circleD = d; circle = new CircleTarget(c.Center, c.Radius); }
                }
            if (step.WantsShape && (onLines <= tol || s.HitTest(w, tol, true)))
            {
                double area = Math.Max(1e-9, s.Bounds.Width * s.Bounds.Height);
                if (area < boxArea) { boxArea = area; box = new BoxTarget(s.Bounds); }
            }
        }
        // A circle's outline is many short straight pieces: on one, it's the circle that's meant.
        if (circle is not null && (line is null || circleD <= lineD + tol * 0.5)) return circle;
        return line ?? box;
    }

    // ---- drawing ----------------------------------------------------------------------------

    static readonly DashStyle CentreDash = new([12, 3, 2, 3], 0);

    /// <summary>The shape so far (following the pointer), the points clicked and the shapes picked (on the work area's own transform).</summary>
    void DrawConstruct(DrawingContext ctx)
    {
        if (ConstructMethod is not { } m) return;
        double px = 1 / _scale;
        var accent = new SolidColorBrush(AccentColor);
        var faint = new SolidColorBrush(Color.FromArgb(150, AccentColor.R, AccentColor.G, AccentColor.B));
        // The lines and circles already picked, and the one under the pointer.
        foreach (var t in _cad.Select(c => c.Target).Append(_cadHover))
            if (t is not null) DrawTarget(ctx, t, new Pen(faint, 4 * px));
        if (PendingPick(m) is { } next)
        {
            if (CadDraw.Build(m, _cad, next, CadOptionsNow) is { } shape)
            {
                bool centre = shape.Centreline;
                ctx.DrawGeometry(null, new Pen(accent, 1.5 * px, centre ? CentreDash : null), ToStream(shape.Geometry));
            }
            // A rubber band from the last point clicked to where the next would go.
            if (_cad.Count > 0 && CurrentStep(m) is { PicksShape: false })
                ctx.DrawLine(new Pen(faint, px, new DashStyle([4, 3], 0)), P(_cad[^1].At), P(next.At));
        }
        foreach (var c in _cad)
        {
            double r = 3.5 * px;
            ctx.DrawRectangle(Brushes.White, new Pen(accent, 1.2 * px), new Rect(c.At.X - r, c.At.Y - r, 2 * r, 2 * r));
        }
    }

    void DrawTarget(DrawingContext ctx, CadTarget t, IPen pen)
    {
        switch (t)
        {
            case LineTarget l: ctx.DrawLine(pen, P(l.A), P(l.B)); break;
            case CircleTarget c: ctx.DrawEllipse(null, pen, P(c.Centre), c.Radius, c.Radius); break;
            case BoxTarget b: ctx.DrawRectangle(null, pen, new Rect(b.Box.MinX, b.Box.MinY, b.Box.Width, b.Box.Height)); break;
        }
    }

    /// <summary>By the pointer: what the next click is for, and the size it gives (or the number being typed).</summary>
    void DrawConstructLabel(DrawingContext ctx)
    {
        if (ConstructMethod is not { } m || _cursor is not { } cursor || CurrentStep(m) is not { } step) return;
        string text = step.Prompt;
        if (_cadTyped.Length > 0)
            text += $": {_cadTyped}▏  (Enter)";
        else if (_cad.Count > 0 && PendingPick(m) is { } next && Measure(m, step, next.At) is { } value)
            text += $": {value}" + (step.Typed != TypedMeaning.None ? "   type a number, Enter" : "");
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11.5, Brushes.White);
        var p = ToScreen(PendingPick(m)?.At ?? cursor);
        var box = new Rect(p.X + 14, p.Y + 14, label.Width + 10, label.Height + 6);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(225, 22, 26, 33)), box, 4);
        ctx.DrawText(label, new Point(box.X + 5, box.Y + 3));
    }

    /// <summary>The size the next point gives, as typing it would: a radius, a length, a width or an angle.</summary>
    string? Measure(DrawMethod m, CadStep step, Vec2 at)
    {
        var last = _cad[^1].At;
        switch (step.Typed)
        {
            case TypedMeaning.Distance:
                return (m == DrawMethod.CircleCentreDiameter ? "Ø " : m == DrawMethod.CircleCentreRadius ? "R " : "") + Units.Len(last.Dist(at));
            case TypedMeaning.DistanceHalf:
                return Units.Len(2 * last.Dist(at));
            case TypedMeaning.Offset or TypedMeaning.OffsetHalf when _cad.Count >= 2:
            {
                var u = _cad[1].At - _cad[0].At;
                if (u.Length < 1e-9) return null;
                u /= u.Length;
                double d = Math.Abs((at - _cad[0].At).Cross(u));
                return Units.Len(step.Typed == TypedMeaning.OffsetHalf ? 2 * d : d);
            }
            case TypedMeaning.Angle when _cad.Count >= 2:
            {
                var c = _cad[0].At;
                double a0 = Math.Atan2(_cad[1].At.Y - c.Y, _cad[1].At.X - c.X), a1 = Math.Atan2(at.Y - c.Y, at.X - c.X);
                double sweep = (CadOptionsNow.Clockwise ? a1 - a0 : a0 - a1) * 180 / Math.PI;
                sweep = ((sweep % 360) + 360) % 360;
                return $"{sweep:0.#}°";
            }
        }
        return null;
    }
}
