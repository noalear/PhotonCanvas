using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// How a drawing tool makes its shape from the points clicked, as in a CAD program: a circle by
/// its centre and radius, through three points or touching two or three other shapes; a
/// rectangle by three points (at any angle) or from its centre; arcs; centrelines.
/// The <c>…Box</c> methods are the usual drag from corner to corner (the canvas handles those).
/// </summary>
public enum DrawMethod
{
    // Rectangle
    RectCorners, RectCentreCorner, Rect3Points, RectCentreMidCorner, RectMidEndSide, Parallelogram, Slot,
    // Ellipse / circle
    EllipseBox, CircleCentreRadius, CircleCentreDiameter, Circle2Points, Circle3Points, CircleTanTanRadius, CircleTanTanTan,
    EllipseCentreAxes, EllipseAxisEnds,
    // Polygon and star
    PolygonBox, PolygonCentreCorner, PolygonCentreSide, PolygonEdge,
    StarBox, StarCentrePoint,
    // Line
    LineTwoPoints, LineFromMiddle, LineTangentTwoCircles,
    // Arc
    Arc3Points, ArcStartEndPoint, ArcCentreStartEnd,
    // Centreline
    CentrelineTwoPoints, CentrelineBetweenLines, CentreMark,
}

/// <summary>What typing a number at a step means (the point it gives is used as the click).</summary>
public enum TypedMeaning
{
    /// <summary>Typing does nothing here (a point on the arc, a shape to touch).</summary>
    None,
    /// <summary>The distance from the last point, towards the pointer (a radius, a side's length).</summary>
    Distance,
    /// <summary>The whole length, of which the last point is the middle (a side picked at its middle).</summary>
    DistanceHalf,
    /// <summary>How far from the line through the first two points, on the pointer's side (a width).</summary>
    Offset,
    /// <summary>The whole width, centred on the line through the first two points.</summary>
    OffsetHalf,
    /// <summary>The angle swept from the start, in degrees (counter-clockwise as on paper).</summary>
    Angle,
}

/// <summary>Something a tangent circle or a centreline is drawn against: a straight line, a circle (or arc), or a shape's box.</summary>
public abstract record CadTarget;
/// <summary>A straight line through <see cref="A"/> and <see cref="B"/> (taken as endless for tangents).</summary>
public sealed record LineTarget(Vec2 A, Vec2 B) : CadTarget;
public sealed record CircleTarget(Vec2 Centre, double Radius) : CadTarget;
/// <summary>A shape that isn't a circle: its box (centre marks go through its middle).</summary>
public sealed record BoxTarget(Rect2 Box) : CadTarget;

/// <summary>A point clicked, and the line or circle clicked on when the step wants one.</summary>
public readonly record struct CadPick(Vec2 At, CadTarget? Target = null);

/// <summary>Settings for the methods: sides and points, the tangent radius, how far centrelines run past.</summary>
public sealed record CadOptions
{
    public int Sides { get; init; } = 6;
    public int StarPoints { get; init; } = 5;
    public double StarInner { get; init; } = 0.5;
    /// <summary>Tangent, tangent, radius: the circle's radius (mm).</summary>
    public double TangentRadiusMm { get; init; } = 10;
    /// <summary>Centrelines and centre marks run this far past what they're drawn on (mm).</summary>
    public double OvershootMm { get; init; } = 3;
    /// <summary>Shift: squares, circles, even boxes.</summary>
    public bool Square { get; init; }
    /// <summary>Arcs by centre: go clockwise (as on paper) instead of counter-clockwise.</summary>
    public bool Clockwise { get; init; }
}

/// <summary>
/// The shape a method makes: either an editable shape (<see cref="Kind"/> with its size and
/// placement, so a rotated rectangle stays a rectangle you can type a size for) or plain lines.
/// </summary>
public sealed record CadShape
{
    public required string Name { get; init; }
    /// <summary>On the work area (mm).</summary>
    public required PathGeometry Geometry { get; init; }
    public ParamKind? Kind { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double Corner { get; init; }
    public int Sides { get; init; } = 4;
    public double Inner { get; init; } = 0.5;
    public Affine Transform { get; init; } = Affine.Identity;
    /// <summary>A centreline or centre mark (goes on the centrelines layer unless it's to be marked).</summary>
    public bool Centreline { get; init; }
    /// <summary>A closed shape (selected after drawing, and the tool goes back to Select) rather than lines.</summary>
    public bool Closed => Kind is not null || Geometry.Subpaths.Any(s => s.Closed);

    public PathShape ToShape(Guid layerId) => Kind is { } k
        ? ParamShape.Create(k, W, H, Corner, Sides, Inner, layerId, Transform, name: Name)
        : new PathShape { Name = Name, LayerId = layerId, Geometry = Geometry };

    internal static CadShape Param(string name, ParamKind kind, double w, double h, Affine t, double corner = 0, int sides = 4, double inner = 0.5) => new()
    {
        Name = name, Kind = kind, W = w, H = h, Corner = corner, Sides = sides, Inner = inner, Transform = t,
        Geometry = ParamShape.Build(kind, w, h, corner, sides, inner).Transform(t),
    };

    internal static CadShape Lines(string name, PathGeometry g, bool centreline = false) => new() { Name = name, Geometry = g, Centreline = centreline };
}

/// <summary>What one step of a method asks for.</summary>
public sealed record CadStep(string Prompt, TypedMeaning Typed = TypedMeaning.None, bool WantsLine = false, bool WantsCircle = false, bool WantsShape = false)
{
    /// <summary>The step is a click on another shape (a line or circle to touch), not a point.</summary>
    public bool PicksShape => WantsLine || WantsCircle || WantsShape;
}

public static class CadDraw
{
    /// <summary>The methods each tool offers, in menu order (the first is the tool's usual way).</summary>
    public static readonly DrawMethod[] RectMethods = [DrawMethod.RectCorners, DrawMethod.RectCentreCorner, DrawMethod.Rect3Points, DrawMethod.RectCentreMidCorner, DrawMethod.RectMidEndSide, DrawMethod.Slot, DrawMethod.Parallelogram];
    public static readonly DrawMethod[] EllipseMethods = [DrawMethod.EllipseBox, DrawMethod.CircleCentreRadius, DrawMethod.CircleCentreDiameter, DrawMethod.Circle2Points, DrawMethod.Circle3Points, DrawMethod.CircleTanTanRadius, DrawMethod.CircleTanTanTan, DrawMethod.EllipseCentreAxes, DrawMethod.EllipseAxisEnds];
    public static readonly DrawMethod[] PolygonMethods = [DrawMethod.PolygonBox, DrawMethod.PolygonCentreCorner, DrawMethod.PolygonCentreSide, DrawMethod.PolygonEdge];
    public static readonly DrawMethod[] StarMethods = [DrawMethod.StarBox, DrawMethod.StarCentrePoint];
    public static readonly DrawMethod[] LineMethods = [DrawMethod.LineTwoPoints, DrawMethod.LineFromMiddle, DrawMethod.LineTangentTwoCircles];
    public static readonly DrawMethod[] ArcMethods = [DrawMethod.Arc3Points, DrawMethod.ArcStartEndPoint, DrawMethod.ArcCentreStartEnd];
    public static readonly DrawMethod[] CentrelineMethods = [DrawMethod.CentrelineTwoPoints, DrawMethod.CentrelineBetweenLines, DrawMethod.CentreMark];

    /// <summary>The usual drag-a-box (or, for lines, click-by-click) way: the canvas draws these itself.</summary>
    public static bool IsBox(DrawMethod m) => m is DrawMethod.RectCorners or DrawMethod.EllipseBox or DrawMethod.PolygonBox or DrawMethod.StarBox or DrawMethod.LineTwoPoints;

    /// <summary>The method's name in the "Draw by" list.</summary>
    public static string Label(DrawMethod m) => m switch
    {
        DrawMethod.RectCorners => "2 corners (drag)",
        DrawMethod.RectCentreCorner => "Centre and corner",
        DrawMethod.Rect3Points => "3 points (any angle)",
        DrawMethod.RectCentreMidCorner => "Centre, side middle, corner",
        DrawMethod.RectMidEndSide => "Side middle, end, opposite side",
        DrawMethod.Slot => "Slot: centre to centre, width",
        DrawMethod.Parallelogram => "Parallelogram (3 corners)",
        DrawMethod.EllipseBox => "Box (drag)",
        DrawMethod.CircleCentreRadius => "Circle: centre, radius",
        DrawMethod.CircleCentreDiameter => "Circle: centre, diameter",
        DrawMethod.Circle2Points => "Circle: 2 points",
        DrawMethod.Circle3Points => "Circle: 3 points",
        DrawMethod.CircleTanTanRadius => "Circle: tangent, tangent, radius",
        DrawMethod.CircleTanTanTan => "Circle: tangent to 3",
        DrawMethod.EllipseCentreAxes => "Ellipse: centre, axis, other axis",
        DrawMethod.EllipseAxisEnds => "Ellipse: axis ends, other axis",
        DrawMethod.PolygonBox => "Box (drag)",
        DrawMethod.PolygonCentreCorner => "Centre and corner (inscribed)",
        DrawMethod.PolygonCentreSide => "Centre and side middle (circumscribed)",
        DrawMethod.PolygonEdge => "One side (2 corners)",
        DrawMethod.StarBox => "Box (drag)",
        DrawMethod.StarCentrePoint => "Centre and point",
        DrawMethod.LineTwoPoints => "2 points",
        DrawMethod.LineFromMiddle => "From the middle",
        DrawMethod.LineTangentTwoCircles => "Tangent to 2 circles",
        DrawMethod.Arc3Points => "3 points (start, on it, end)",
        DrawMethod.ArcStartEndPoint => "Start, end, point on it",
        DrawMethod.ArcCentreStartEnd => "Centre, start, end",
        DrawMethod.CentrelineTwoPoints => "2 points",
        DrawMethod.CentrelineBetweenLines => "Between 2 lines",
        DrawMethod.CentreMark => "Centre mark (circle or shape)",
        _ => m.ToString(),
    };

    /// <summary>One line on how to draw with the method (for the bar above the work area).</summary>
    public static string Hint(DrawMethod m) => m switch
    {
        DrawMethod.RectCentreCorner => "Click the centre, then a corner. Shift = square.",
        DrawMethod.Rect3Points => "Click two corners (they set one side and the angle), then how wide.",
        DrawMethod.RectCentreMidCorner => "Click the centre, the middle of one side (sets the angle), then a corner.",
        DrawMethod.RectMidEndSide => "Click the middle of one side, one end of that side, then where the opposite side goes.",
        DrawMethod.Slot => "Click the centres of the two rounded ends, then how wide the slot is.",
        DrawMethod.Parallelogram => "Click three corners; the fourth is worked out.",
        DrawMethod.CircleCentreRadius => "Click the centre, then a point on the circle.",
        DrawMethod.CircleCentreDiameter => "Click the centre, then how wide across.",
        DrawMethod.Circle2Points => "Click the two ends of a diameter.",
        DrawMethod.Circle3Points => "Click three points the circle goes through.",
        DrawMethod.CircleTanTanRadius => "Click two lines or circles to touch, near where it should touch them.",
        DrawMethod.CircleTanTanTan => "Click three lines or circles to touch, near where it should touch them.",
        DrawMethod.EllipseCentreAxes => "Click the centre, the end of one axis (sets the angle), then how wide the other way.",
        DrawMethod.EllipseAxisEnds => "Click both ends of one axis, then how wide the other way.",
        DrawMethod.PolygonCentreCorner => "Click the centre, then a corner (the corners lie on that circle).",
        DrawMethod.PolygonCentreSide => "Click the centre, then the middle of a side (the sides touch that circle).",
        DrawMethod.PolygonEdge => "Click both ends of one side; it's built on the left, looking from the first to the second.",
        DrawMethod.StarCentrePoint => "Click the centre, then the tip of one point.",
        DrawMethod.LineFromMiddle => "Click the middle of the line, then one end; the other end mirrors it.",
        DrawMethod.LineTangentTwoCircles => "Click two circles near where the line should touch them.",
        DrawMethod.Arc3Points => "Click the start, a point the arc goes through, then the end.",
        DrawMethod.ArcStartEndPoint => "Click the start, the end, then a point the arc goes through (sets how far it bulges).",
        DrawMethod.ArcCentreStartEnd => "Click the centre, the start (sets the radius), then the end. Alt = clockwise.",
        DrawMethod.CentrelineTwoPoints => "Click both ends of the centreline.",
        DrawMethod.CentrelineBetweenLines => "Click two lines: it runs midway between them (or halves their angle).",
        DrawMethod.CentreMark => "Click a circle, arc or shape to cross its centre.",
        _ => "",
    };

    /// <summary>The steps of a method: what each click is for, and what typing a number means there.</summary>
    public static CadStep[] Steps(DrawMethod m) => m switch
    {
        DrawMethod.RectCentreCorner => [new("Centre"), new("A corner", TypedMeaning.Distance)],
        DrawMethod.Rect3Points => [new("First corner"), new("Second corner", TypedMeaning.Distance), new("Width", TypedMeaning.Offset)],
        DrawMethod.RectCentreMidCorner => [new("Centre"), new("Middle of a side", TypedMeaning.Distance), new("A corner (side length)", TypedMeaning.OffsetHalf)],
        DrawMethod.RectMidEndSide => [new("Middle of a side"), new("End of that side", TypedMeaning.DistanceHalf), new("Opposite side", TypedMeaning.Offset)],
        DrawMethod.Slot => [new("Centre of one end"), new("Centre of the other end", TypedMeaning.Distance), new("Width", TypedMeaning.OffsetHalf)],
        DrawMethod.Parallelogram => [new("First corner"), new("Second corner", TypedMeaning.Distance), new("Third corner")],
        DrawMethod.CircleCentreRadius => [new("Centre"), new("Radius", TypedMeaning.Distance)],
        DrawMethod.CircleCentreDiameter => [new("Centre"), new("Diameter", TypedMeaning.Distance)],
        DrawMethod.Circle2Points => [new("One end of a diameter"), new("Other end", TypedMeaning.Distance)],
        DrawMethod.Circle3Points => [new("First point"), new("Second point"), new("Third point")],
        DrawMethod.CircleTanTanRadius => [new("First line or circle to touch", WantsLine: true, WantsCircle: true), new("Second line or circle to touch", WantsLine: true, WantsCircle: true)],
        DrawMethod.CircleTanTanTan => [new("First line or circle to touch", WantsLine: true, WantsCircle: true), new("Second line or circle to touch", WantsLine: true, WantsCircle: true), new("Third line or circle to touch", WantsLine: true, WantsCircle: true)],
        DrawMethod.EllipseCentreAxes => [new("Centre"), new("End of one axis", TypedMeaning.Distance), new("Other axis", TypedMeaning.Offset)],
        DrawMethod.EllipseAxisEnds => [new("One end of an axis"), new("Other end", TypedMeaning.Distance), new("Other axis", TypedMeaning.OffsetHalf)],
        DrawMethod.PolygonCentreCorner => [new("Centre"), new("A corner", TypedMeaning.Distance)],
        DrawMethod.PolygonCentreSide => [new("Centre"), new("Middle of a side", TypedMeaning.Distance)],
        DrawMethod.PolygonEdge => [new("One end of a side"), new("Other end", TypedMeaning.Distance)],
        DrawMethod.StarCentrePoint => [new("Centre"), new("Tip of a point", TypedMeaning.Distance)],
        DrawMethod.LineFromMiddle => [new("Middle"), new("One end", TypedMeaning.DistanceHalf)],
        DrawMethod.LineTangentTwoCircles => [new("First circle", WantsCircle: true), new("Second circle", WantsCircle: true)],
        DrawMethod.Arc3Points => [new("Start"), new("A point on the arc"), new("End")],
        DrawMethod.ArcStartEndPoint => [new("Start"), new("End", TypedMeaning.Distance), new("A point on the arc")],
        DrawMethod.ArcCentreStartEnd => [new("Centre"), new("Start", TypedMeaning.Distance), new("End", TypedMeaning.Angle)],
        DrawMethod.CentrelineTwoPoints => [new("Start"), new("End", TypedMeaning.Distance)],
        DrawMethod.CentrelineBetweenLines => [new("First line", WantsLine: true), new("Second line", WantsLine: true)],
        DrawMethod.CentreMark => [new("Circle, arc or shape", WantsCircle: true, WantsShape: true)],
        _ => [],
    };

    /// <summary>
    /// The point a typed number gives at step <c>picks.Count</c>: the number read as the step's
    /// <see cref="TypedMeaning"/>, in the direction of (or on the side of) <paramref name="cursor"/>.
    /// Null when typing means nothing there or there's no direction yet.
    /// </summary>
    public static Vec2? TypedPoint(DrawMethod m, IReadOnlyList<CadPick> picks, Vec2 cursor, double typed, CadOptions? options = null)
    {
        var steps = Steps(m);
        if (picks.Count == 0 || picks.Count >= steps.Length || !double.IsFinite(typed)) return null;
        var last = picks[^1].At;
        switch (steps[picks.Count].Typed)
        {
            case TypedMeaning.Distance or TypedMeaning.DistanceHalf:
            {
                var d = cursor - last;
                var dir = d.Length < 1e-9 ? new Vec2(1, 0) : d / d.Length;
                double len = steps[picks.Count].Typed == TypedMeaning.DistanceHalf ? typed / 2 : typed;
                return last + dir * len;
            }
            case TypedMeaning.Offset or TypedMeaning.OffsetHalf when picks.Count >= 2:
            {
                var a = picks[0].At;
                var u = picks[1].At - a;
                if (u.Length < 1e-9) return null;
                u /= u.Length;
                var n = new Vec2(-u.Y, u.X);
                double side = (cursor - a).Dot(n) < 0 ? -1 : 1;
                double off = steps[picks.Count].Typed == TypedMeaning.OffsetHalf ? typed / 2 : typed;
                // Along the line, where the pointer is; across it, the typed distance.
                var along = a + u * (cursor - a).Dot(u);
                return along + n * (side * off);
            }
            case TypedMeaning.Angle when picks.Count >= 2:
            {
                var c = picks[0].At;
                double r = picks[1].At.Dist(c);
                double a0 = Math.Atan2(picks[1].At.Y - c.Y, picks[1].At.X - c.X);
                // Counter-clockwise as on paper is a falling angle on the work area (Y runs down).
                double a1 = a0 - (options?.Clockwise == true ? -1 : 1) * typed * Math.PI / 180;
                return c + new Vec2(Math.Cos(a1), Math.Sin(a1)) * r;
            }
        }
        return null;
    }

    /// <summary>
    /// The shape from the points clicked so far plus <paramref name="next"/> (the pointer, or the
    /// last click): a preview while the method still wants points, the shape itself once
    /// <c>picks.Count + 1</c> equals its number of steps. Null when the points don't make one
    /// (three points in a row, circles a line can't touch…).
    /// </summary>
    public static CadShape? Build(DrawMethod m, IReadOnlyList<CadPick> picks, CadPick next, CadOptions? options = null)
    {
        var o = options ?? new CadOptions();
        var p = picks.Append(next).ToList();
        Vec2 P(int i) => p[i].At;
        try
        {
            switch (m)
            {
                case DrawMethod.RectCentreCorner when p.Count >= 2:
                {
                    var d = P(1) - P(0);
                    double hw = Math.Abs(d.X), hh = Math.Abs(d.Y);
                    if (o.Square) hw = hh = Math.Max(hw, hh);
                    return Rect("Rectangle", P(0), new Vec2(1, 0), 2 * hw, 2 * hh);
                }
                case DrawMethod.Rect3Points when p.Count >= 2:
                {
                    if (p.Count == 2) return Edge(P(0), P(1));
                    return RectOnSide(P(0), P(1), P(2), "Rectangle");
                }
                case DrawMethod.RectCentreMidCorner when p.Count >= 2:
                {
                    var c = P(0);
                    var toMid = P(1) - c;
                    if (toMid.Length < 1e-9) return null;
                    var n = toMid / toMid.Length;       // across the side picked
                    var u = new Vec2(n.Y, -n.X);        // along it
                    double h = 2 * toMid.Length;
                    double w = p.Count == 2 ? h : 2 * Math.Abs((P(2) - c).Dot(u));
                    if (p.Count == 2) return Edge(c, P(1));
                    return Rect("Rectangle", c, u, w, h);
                }
                case DrawMethod.RectMidEndSide when p.Count >= 2:
                {
                    var mid = P(0);
                    var end = P(1);
                    var start = mid * 2 - end;
                    if (p.Count == 2) return Edge(start, end);
                    return RectOnSide(start, end, P(2), "Rectangle");
                }
                case DrawMethod.Slot when p.Count >= 2:
                {
                    var a = P(0);
                    var b = P(1);
                    double len = a.Dist(b);
                    if (len < 1e-9) return null;
                    var u = (b - a) / len;
                    if (p.Count == 2) return Edge(a, b);
                    var n = new Vec2(-u.Y, u.X);
                    double width = 2 * Math.Abs((P(2) - a).Dot(n));
                    if (width < 1e-6) return null;
                    // A rectangle whose corners are rounded by half its width: still editable.
                    return Rect("Slot", (a + b) * 0.5, u, len + width, width, width / 2);
                }
                case DrawMethod.Parallelogram when p.Count >= 2:
                {
                    if (p.Count == 2) return Edge(P(0), P(1));
                    var d = P(0) + (P(2) - P(1));
                    if (Math.Abs((P(1) - P(0)).Cross(P(2) - P(1))) < 1e-9) return null;
                    return CadShape.Lines("Parallelogram", PathGeometry.Polyline([P(0), P(1), P(2), d], true));
                }
                case DrawMethod.CircleCentreRadius when p.Count >= 2:
                    return Circle(P(0), P(0).Dist(P(1)));
                case DrawMethod.CircleCentreDiameter when p.Count >= 2:
                    return Circle(P(0), P(0).Dist(P(1)) / 2);
                case DrawMethod.Circle2Points when p.Count >= 2:
                    return Circle((P(0) + P(1)) * 0.5, P(0).Dist(P(1)) / 2);
                case DrawMethod.Circle3Points when p.Count >= 2:
                {
                    if (p.Count == 2) return Edge(P(0), P(1));
                    return Through(P(0), P(1), P(2)) is { } c ? Circle(c.Centre, c.Radius) : null;
                }
                case DrawMethod.CircleTanTanRadius when p.Count >= 2:
                {
                    if (p[0].Target is not { } t0 || p[1].Target is not { } t1) return null;
                    var best = Best(TangentCircles(t0, t1, o.TangentRadiusMm), [p[0], p[1]]);
                    return best is { } c ? Circle(c.Centre, c.Radius) : null;
                }
                case DrawMethod.CircleTanTanTan when p.Count >= 3:
                {
                    if (p[0].Target is not { } t0 || p[1].Target is not { } t1 || p[2].Target is not { } t2) return null;
                    var best = Best(TangentCircles(t0, t1, t2), [p[0], p[1], p[2]]);
                    return best is { } c ? Circle(c.Centre, c.Radius) : null;
                }
                case DrawMethod.EllipseCentreAxes when p.Count >= 2:
                {
                    var c = P(0);
                    var ax = P(1) - c;
                    if (ax.Length < 1e-9) return null;
                    var u = ax / ax.Length;
                    double a = ax.Length;
                    double b = p.Count == 2 ? a : Math.Abs((P(2) - c).Dot(new Vec2(-u.Y, u.X)));
                    if (b < 1e-6) return null;
                    return Ellipse(c, u, a, b);
                }
                case DrawMethod.EllipseAxisEnds when p.Count >= 2:
                {
                    var c = (P(0) + P(1)) * 0.5;
                    var ax = P(1) - c;
                    if (ax.Length < 1e-9) return null;
                    var u = ax / ax.Length;
                    double a = ax.Length;
                    double b = p.Count == 2 ? a : Math.Abs((P(2) - c).Dot(new Vec2(-u.Y, u.X)));
                    if (b < 1e-6) return null;
                    return Ellipse(c, u, a, b);
                }
                case DrawMethod.PolygonCentreCorner when p.Count >= 2:
                    return Regular("Polygon", ParamKind.Polygon, Math.Clamp(o.Sides, 3, 64), 1, P(0), P(1) - P(0));
                case DrawMethod.PolygonCentreSide when p.Count >= 2:
                {
                    int n = Math.Clamp(o.Sides, 3, 64);
                    var d = P(1) - P(0);
                    if (d.Length < 1e-9) return null;
                    // The corner is half a step round from the side's middle, further out.
                    double ang = Math.Atan2(d.Y, d.X) + Math.PI / n, r = d.Length / Math.Cos(Math.PI / n);
                    return Regular("Polygon", ParamKind.Polygon, n, 1, P(0), new Vec2(Math.Cos(ang), Math.Sin(ang)) * r);
                }
                case DrawMethod.PolygonEdge when p.Count >= 2:
                {
                    int n = Math.Clamp(o.Sides, 3, 64);
                    var a = P(0);
                    var b = P(1);
                    double s = a.Dist(b);
                    if (s < 1e-9) return null;
                    var u = (b - a) / s;
                    // Above the side as you look from the first click to the second (on the left).
                    var up = new Vec2(u.Y, -u.X);
                    double r = s / (2 * Math.Sin(Math.PI / n)), apothem = r * Math.Cos(Math.PI / n);
                    var c = (a + b) * 0.5 + up * apothem;
                    return Regular("Polygon", ParamKind.Polygon, n, 1, c, a - c);
                }
                case DrawMethod.StarCentrePoint when p.Count >= 2:
                    return Regular("Star", ParamKind.Star, Math.Clamp(o.StarPoints, 3, 64), Math.Clamp(o.StarInner, 0.05, 0.95), P(0), P(1) - P(0));
                case DrawMethod.LineFromMiddle when p.Count >= 2:
                {
                    var a = P(0) * 2 - P(1);
                    return a.Dist(P(1)) < 1e-9 ? null : CadShape.Lines("Line", PathGeometry.Line(a, P(1)));
                }
                case DrawMethod.LineTangentTwoCircles when p.Count >= 2:
                {
                    if (p[0].Target is not CircleTarget c0 || p[1].Target is not CircleTarget c1) return null;
                    (Vec2 A, Vec2 B)? best = null;
                    double score = double.PositiveInfinity;
                    foreach (var (a, b) in TangentLines(c0, c1))
                    {
                        double s = a.Dist(p[0].At) + b.Dist(p[1].At);
                        if (s < score) { score = s; best = (a, b); }
                    }
                    return best is { } l && l.A.Dist(l.B) > 1e-6 ? CadShape.Lines("Line", PathGeometry.Line(l.A, l.B)) : null;
                }
                case DrawMethod.Arc3Points when p.Count >= 2:
                    if (p.Count == 2) return Edge(P(0), P(1));
                    return ArcThrough(P(0), P(1), P(2));
                case DrawMethod.ArcStartEndPoint when p.Count >= 2:
                    if (p.Count == 2) return Edge(P(0), P(1));
                    return ArcThrough(P(0), P(2), P(1));
                case DrawMethod.ArcCentreStartEnd when p.Count >= 2:
                {
                    var c = P(0);
                    double r = P(1).Dist(c);
                    if (r < 1e-6) return null;
                    if (p.Count == 2) return Edge(c, P(1));
                    double a0 = Math.Atan2(P(1).Y - c.Y, P(1).X - c.X), a1 = Math.Atan2(P(2).Y - c.Y, P(2).X - c.X);
                    // Counter-clockwise on paper = the angle falling on the work area.
                    double sweep = o.Clockwise ? Wrap(a1 - a0) : -Wrap(a0 - a1);
                    if (Math.Abs(sweep) < 1e-6) return null;
                    return CadShape.Lines("Arc", Arc(c, r, a0, sweep));
                }
                case DrawMethod.CentrelineTwoPoints when p.Count >= 2:
                    return P(0).Dist(P(1)) < 1e-9 ? null : CadShape.Lines("Centreline", PathGeometry.Line(P(0), P(1)), true);
                case DrawMethod.CentrelineBetweenLines when p.Count >= 2:
                {
                    if (p[0].Target is not LineTarget l0 || p[1].Target is not LineTarget l1) return null;
                    return Between(l0, l1, o.OvershootMm) is { } g ? CadShape.Lines("Centreline", g, true) : null;
                }
                case DrawMethod.CentreMark when p.Count >= 1:
                    return p[0].Target is { } t && CentreMark(t, o.OvershootMm) is { } mark ? CadShape.Lines("Centre mark", mark, true) : null;
            }
        }
        catch (ArithmeticException) { }
        return null;
    }

    // ---- shapes ---------------------------------------------------------------------------

    /// <summary>A guide line while the method waits for more points (drawn as the shape so far).</summary>
    static CadShape? Edge(Vec2 a, Vec2 b) => a.Dist(b) < 1e-9 ? null : CadShape.Lines("", PathGeometry.Line(a, b));

    static double Deg(Vec2 u) => Math.Atan2(u.Y, u.X) * 180 / Math.PI;

    /// <summary>An editable rectangle <paramref name="w"/> along <paramref name="u"/> by <paramref name="h"/> across it, centred on <paramref name="c"/>.</summary>
    static CadShape? Rect(string name, Vec2 c, Vec2 u, double w, double h, double corner = 0)
    {
        if (w < 1e-6 || h < 1e-6) return null;
        var t = Affine.Translate(-w / 2, -h / 2).Then(Affine.Rotate(Tidy(Deg(u), 180))).Then(Affine.Translate(c));
        return CadShape.Param(name, ParamKind.Rectangle, w, h, t, corner);
    }

    /// <summary>A rectangle with one side from <paramref name="a"/> to <paramref name="b"/> and the opposite side through <paramref name="k"/>'s line.</summary>
    static CadShape? RectOnSide(Vec2 a, Vec2 b, Vec2 k, string name)
    {
        double len = a.Dist(b);
        if (len < 1e-9) return null;
        var u = (b - a) / len;
        var n = new Vec2(-u.Y, u.X);
        double d = (k - a).Dot(n);
        return Rect(name, (a + b) * 0.5 + n * (d / 2), u, len, Math.Abs(d));
    }

    static CadShape? Circle(Vec2 c, double r) =>
        r < 1e-6 || !c.IsFinite ? null : CadShape.Param("Circle", ParamKind.Ellipse, 2 * r, 2 * r, Affine.Translate(c.X - r, c.Y - r));

    static CadShape? Ellipse(Vec2 c, Vec2 u, double a, double b)
    {
        if (a < 1e-6 || b < 1e-6) return null;
        var t = Affine.Translate(-a, -b).Then(Affine.Rotate(Tidy(Deg(u), 180))).Then(Affine.Translate(c));
        return CadShape.Param(Math.Abs(a - b) < 1e-9 ? "Circle" : "Ellipse", ParamKind.Ellipse, 2 * a, 2 * b, t);
    }

    /// <summary>
    /// A regular polygon or star centred on <paramref name="c"/> with a corner (point) at
    /// <c>c + toCorner</c>, as an editable shape.
    /// </summary>
    static CadShape? Regular(string name, ParamKind kind, int corners, double inner, Vec2 c, Vec2 toCorner)
    {
        double r = toCorner.Length;
        if (r < 1e-6) return null;
        // The shape as ParamShape builds it: pointing up, its own box from (0, 0). Where its centre lands in that box:
        bool star = kind == ParamKind.Star;
        int n = star ? corners * 2 : corners;
        var unit = Enumerable.Range(0, n).Select(i =>
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n, k = star && i % 2 == 1 ? inner : 1;
            return new Vec2(Math.Cos(a) * k, Math.Sin(a) * k);
        }).ToList();
        var box = Rect2.Bounds(unit);
        double w = box.Width * r, h = box.Height * r;
        var centre = new Vec2(-box.MinX * r, -box.MinY * r);
        // Its first corner points straight up (−90°): turn it to point at the corner clicked (or
        // the same shape turned less: one corner on from it).
        double turn = Tidy(Deg(toCorner) + 90, 360.0 / corners);
        var t = Affine.Translate(-centre.X, -centre.Y).Then(Affine.Rotate(turn)).Then(Affine.Translate(c));
        return CadShape.Param(name, kind, w, h, t, 0, corners, inner);
    }

    /// <summary>
    /// An angle in degrees with rounding noise taken off (89.9999999 → 90), and brought within half
    /// of <paramref name="period"/> of 0: a rectangle turned 180° is the same rectangle (so it reads
    /// 0° in the Selection panel), a hexagon turned 60° the same hexagon.
    /// </summary>
    static double Tidy(double deg, double period)
    {
        double r = Math.Round(deg, 6);
        r -= period * Math.Round(r / period);
        if (r <= -period / 2 + 1e-9) r += period;
        return Math.Round(r, 6);
    }

    /// <summary>The circle through three points, or null when they lie in a line.</summary>
    public static (Vec2 Centre, double Radius)? Through(Vec2 a, Vec2 b, Vec2 c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        double scale = Math.Max(a.Dist(b), Math.Max(b.Dist(c), a.Dist(c)));
        if (Math.Abs(d) < 1e-9 * Math.Max(1, scale * scale)) return null;
        double a2 = a.Dot(a), b2 = b.Dot(b), c2 = c.Dot(c);
        var centre = new Vec2((a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d, (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        return (centre, centre.Dist(a));
    }

    /// <summary>The angle wrapped into [0, 2π).</summary>
    static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + 2 * Math.PI : a;
    }

    /// <summary>The arc from <paramref name="s"/> through <paramref name="m"/> to <paramref name="e"/>.</summary>
    static CadShape? ArcThrough(Vec2 s, Vec2 m, Vec2 e)
    {
        if (Through(s, m, e) is not { } c) return s.Dist(e) < 1e-9 ? null : CadShape.Lines("Line", PathGeometry.Line(s, e));
        double a0 = Math.Atan2(s.Y - c.Centre.Y, s.X - c.Centre.X);
        double toMid = Wrap(Math.Atan2(m.Y - c.Centre.Y, m.X - c.Centre.X) - a0), toEnd = Wrap(Math.Atan2(e.Y - c.Centre.Y, e.X - c.Centre.X) - a0);
        // Rising angles reach the middle point before the end: go that way; otherwise the other way round.
        double sweep = toMid < toEnd ? toEnd : toEnd - 2 * Math.PI;
        return CadShape.Lines("Arc", Arc(c.Centre, c.Radius, a0, sweep));
    }

    /// <summary>An arc as curves of a quarter turn or less, from angle <paramref name="a0"/> through <paramref name="sweep"/> (radians, on the work area).</summary>
    public static PathGeometry Arc(Vec2 c, double r, double a0, double sweep)
    {
        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
        double step = sweep / n, k = 4.0 / 3 * Math.Tan(step / 4);
        Vec2 At(double a) => c + new Vec2(Math.Cos(a), Math.Sin(a)) * r;
        Vec2 Tan(double a) => new Vec2(-Math.Sin(a), Math.Cos(a)) * r;
        var segs = new List<Seg>();
        for (int i = 0; i < n; i++)
        {
            double t0 = a0 + i * step, t1 = t0 + step;
            segs.Add(Seg.Cubic(At(t0) + Tan(t0) * k, At(t1) - Tan(t1) * k, At(t1)));
        }
        bool whole = Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-9;
        return new PathGeometry([new Subpath(At(a0), [.. segs], whole)]);
    }

    // ---- tangent circles ------------------------------------------------------------------

    /// <summary>
    /// One condition on a circle (centre x, y, radius r) touching a line or circle:
    /// K·(x² + y² − r²) + A·x + B·y + C·r + E = 0.
    /// </summary>
    readonly record struct Touch(double K, double A, double B, double C, double E);

    /// <summary>The ways a circle can touch the target: either side of a line, outside or inside a circle.</summary>
    static IEnumerable<Touch> Touches(CadTarget t)
    {
        switch (t)
        {
            case LineTarget l:
            {
                var d = l.B - l.A;
                if (d.Length < 1e-12) yield break;
                var n = new Vec2(-d.Y, d.X) / d.Length;
                // n·(P − A) = ±r
                foreach (int s in new[] { 1, -1 }) yield return new(0, n.X, n.Y, -s, -n.Dot(l.A));
                break;
            }
            case CircleTarget c:
                // |P − C|² = (r ± R)²
                foreach (int s in new[] { 1, -1 })
                    yield return new(1, -2 * c.Centre.X, -2 * c.Centre.Y, -2 * s * c.Radius, c.Centre.Dot(c.Centre) - c.Radius * c.Radius);
                break;
        }
    }

    /// <summary>Every circle of radius <paramref name="r"/> touching both targets.</summary>
    public static List<(Vec2 Centre, double Radius)> TangentCircles(CadTarget a, CadTarget b, double r)
    {
        var o = new List<(Vec2, double)>();
        if (!(r > 0)) return o;
        foreach (var ta in Touches(a))
            foreach (var tb in Touches(b))
            {
                // With r known: K·(x² + y²) + A·x + B·y + (C·r − K·r² + E) = 0.
                (double K, double A, double B, double E) Fix(Touch t) => (t.K, t.A, t.B, t.C * r - t.K * r * r + t.E);
                var e1 = Fix(ta);
                var e2 = Fix(tb);
                foreach (var p in Solve2(e1, e2)) Add(o, p, r);
            }
        return o;
    }

    /// <summary>Every circle touching all three targets (lines or circles).</summary>
    public static List<(Vec2 Centre, double Radius)> TangentCircles(CadTarget a, CadTarget b, CadTarget c)
    {
        var o = new List<(Vec2, double)>();
        foreach (var ta in Touches(a))
            foreach (var tb in Touches(b))
                foreach (var tc in Touches(c))
                    foreach (var (p, r) in Solve3([ta, tb, tc]))
                        Add(o, p, r);
        return o;
    }

    static void Add(List<(Vec2, double)> o, Vec2 p, double r)
    {
        if (!p.IsFinite || !(r > 1e-6) || !double.IsFinite(r)) return;
        if (!o.Any(x => x.Item1.Dist(p) < 1e-6 && Math.Abs(x.Item2 - r) < 1e-6)) o.Add((p, r));
    }

    /// <summary>Points (x, y) meeting two conditions K·(x² + y²) + A·x + B·y + E = 0.</summary>
    static IEnumerable<Vec2> Solve2((double K, double A, double B, double E) e1, (double K, double A, double B, double E) e2)
    {
        // Two round ones: their difference is straight.
        if (e1.K != 0 && e2.K != 0) e2 = (0, e2.A - e1.A, e2.B - e1.B, e2.E - e1.E);
        if (e1.K == 0 && e2.K == 0)
        {
            double det = e1.A * e2.B - e1.B * e2.A;
            if (Math.Abs(det) < 1e-12) yield break;
            yield return new Vec2((-e1.E * e2.B + e2.E * e1.B) / det, (-e1.A * e2.E + e2.A * e1.E) / det);
            yield break;
        }
        var (line, round) = e1.K == 0 ? (e1, e2) : (e2, e1);
        // The line A·x + B·y + E = 0 as p0 + t·d, into x² + y² + A'·x + B'·y + E' = 0.
        double nn = line.A * line.A + line.B * line.B;
        if (nn < 1e-18) yield break;
        var p0 = new Vec2(-line.A * line.E / nn, -line.B * line.E / nn);
        var d = new Vec2(-line.B, line.A) / Math.Sqrt(nn);
        double qa = 1, qb = 2 * p0.Dot(d) + round.A * d.X + round.B * d.Y, qc = p0.Dot(p0) + round.A * p0.X + round.B * p0.Y + round.E;
        foreach (var t in Quadratic(qa, qb, qc)) yield return p0 + d * t;
    }

    /// <summary>Circles (x, y, r) meeting three conditions.</summary>
    static IEnumerable<(Vec2, double)> Solve3(Touch[] eqs)
    {
        // Every round condition has the same x² + y² − r²: take one away from the others and they're straight.
        var round = eqs.Where(e => e.K != 0).ToList();
        var lin = eqs.Where(e => e.K == 0).ToList();
        Touch? quad = round.Count > 0 ? round[0] : null;
        foreach (var e in round.Skip(1)) lin.Add(new(0, e.A - round[0].A, e.B - round[0].B, e.C - round[0].C, e.E - round[0].E));
        if (lin.Count == 3)
        {
            // Three straight conditions (three lines).
            double[,] m = { { lin[0].A, lin[0].B, lin[0].C }, { lin[1].A, lin[1].B, lin[1].C }, { lin[2].A, lin[2].B, lin[2].C } };
            double[] rhs = [-lin[0].E, -lin[1].E, -lin[2].E];
            if (Solve3x3(m, rhs) is { } s) yield return (new Vec2(s[0], s[1]), s[2]);
            yield break;
        }
        if (lin.Count != 2 || quad is not { } q) yield break;
        // Two straight conditions in (x, y, r): one unknown is free, the other two follow from it.
        // Pick the free one whose partners' 2×2 determinant is largest.
        double[] e0 = [lin[0].A, lin[0].B, lin[0].C], e1 = [lin[1].A, lin[1].B, lin[1].C];
        int free = -1;
        double bestDet = 0;
        for (int f = 0; f < 3; f++)
        {
            int i = (f + 1) % 3, j = (f + 2) % 3;
            double det = e0[i] * e1[j] - e0[j] * e1[i];
            if (Math.Abs(det) > Math.Abs(bestDet)) { bestDet = det; free = f; }
        }
        if (free < 0 || Math.Abs(bestDet) < 1e-12) yield break;
        {
            int i = (free + 1) % 3, j = (free + 2) % 3;
            // e0[i]·vi + e0[j]·vj = −E0 − e0[f]·t, likewise e1.
            double c0 = -lin[0].E, cf0 = -e0[free], c1 = -lin[1].E, cf1 = -e1[free];
            double vi0 = (c0 * e1[j] - c1 * e0[j]) / bestDet, vit = (cf0 * e1[j] - cf1 * e0[j]) / bestDet;
            double vj0 = (e0[i] * c1 - e1[i] * c0) / bestDet, vjt = (e0[i] * cf1 - e1[i] * cf0) / bestDet;
            // Each unknown as v0 + vt·t.
            var v0 = new double[3];
            var vt = new double[3];
            v0[free] = 0; vt[free] = 1;
            v0[i] = vi0; vt[i] = vit;
            v0[j] = vj0; vt[j] = vjt;
            // Into K·(x² + y² − r²) + A·x + B·y + C·r + E = 0.
            double qa = q.K * (vt[0] * vt[0] + vt[1] * vt[1] - vt[2] * vt[2]);
            double qb = q.K * 2 * (v0[0] * vt[0] + v0[1] * vt[1] - v0[2] * vt[2]) + q.A * vt[0] + q.B * vt[1] + q.C * vt[2];
            double qc = q.K * (v0[0] * v0[0] + v0[1] * v0[1] - v0[2] * v0[2]) + q.A * v0[0] + q.B * v0[1] + q.C * v0[2] + q.E;
            foreach (var t in Quadratic(qa, qb, qc))
                yield return (new Vec2(v0[0] + vt[0] * t, v0[1] + vt[1] * t), v0[2] + vt[2] * t);
        }
    }

    static IEnumerable<double> Quadratic(double a, double b, double c)
    {
        double scale = Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c)));
        if (scale < 1e-300) yield break;
        a /= scale; b /= scale; c /= scale;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12) yield return -c / b;
            yield break;
        }
        double disc = b * b - 4 * a * c;
        if (disc < 0)
        {
            // Just touching, give or take rounding.
            if (disc > -1e-9 * Math.Max(1, b * b)) yield return -b / (2 * a);
            yield break;
        }
        double sq = Math.Sqrt(disc);
        // The numerically careful form.
        double qq = -0.5 * (b + Math.Sign(b == 0 ? 1 : b) * sq);
        yield return qq / a;
        if (Math.Abs(qq) > 1e-300) yield return c / qq;
    }

    static double[]? Solve3x3(double[,] m, double[] r)
    {
        double Det(double[,] a) =>
            a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        double d = Det(m);
        if (Math.Abs(d) < 1e-12) return null;
        var o = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var mc = (double[,])m.Clone();
            for (int row = 0; row < 3; row++) mc[row, c] = r[row];
            o[c] = Det(mc) / d;
        }
        return o;
    }

    /// <summary>Where a circle touches a target.</summary>
    public static Vec2 TouchPoint(CadTarget t, Vec2 centre, double r)
    {
        switch (t)
        {
            case LineTarget l:
            {
                var d = l.B - l.A;
                double len2 = d.Dot(d);
                return len2 < 1e-18 ? l.A : l.A + d * ((centre - l.A).Dot(d) / len2);
            }
            case CircleTarget c:
            {
                var to = centre - c.Centre;
                if (to.Length < 1e-12) return c.Centre + new Vec2(c.Radius, 0);
                var u = to / to.Length;
                var near = c.Centre + u * c.Radius;
                var far = c.Centre - u * c.Radius;
                // Outside (or it inside us): the near side; holding it inside us: the far side.
                return Math.Abs(near.Dist(centre) - r) <= Math.Abs(far.Dist(centre) - r) ? near : far;
            }
            case BoxTarget b:
                return b.Box.Center;
        }
        return centre;
    }

    /// <summary>The circle whose touch points are nearest where the targets were clicked.</summary>
    static (Vec2 Centre, double Radius)? Best(List<(Vec2 Centre, double Radius)> circles, CadPick[] picks)
    {
        (Vec2, double)? best = null;
        double score = double.PositiveInfinity;
        foreach (var (c, r) in circles)
        {
            double s = picks.Sum(p => p.Target is { } t ? TouchPoint(t, c, r).Dist(p.At) : 0);
            if (s < score) { score = s; best = (c, r); }
        }
        return best;
    }

    /// <summary>The (up to four) lines touching both circles, as their two touch points.</summary>
    public static IEnumerable<(Vec2 A, Vec2 B)> TangentLines(CircleTarget a, CircleTarget b)
    {
        var d = b.Centre - a.Centre;
        double len = d.Length;
        if (len < 1e-9) yield break;
        var u = d / len;
        foreach (int s in new[] { 1, -1 })
        {
            // A line n·p = k with a's centre r1 on one side and b's s·r2: n·(b − a) = s·r2 − r1.
            double cos = (s * b.Radius - a.Radius) / len;
            if (Math.Abs(cos) > 1 + 1e-12) continue;
            cos = Math.Clamp(cos, -1, 1);
            double sin = Math.Sqrt(1 - cos * cos);
            foreach (double sg in new[] { 1.0, -1.0 })
            {
                var n = new Vec2(u.X * cos - u.Y * sin * sg, u.Y * cos + u.X * sin * sg);
                yield return (a.Centre - n * a.Radius, b.Centre - n * (s * b.Radius));
                if (sin < 1e-12) break;
            }
        }
    }

    // ---- centrelines ----------------------------------------------------------------------

    /// <summary>
    /// The centreline between two lines: midway between them when they're parallel, along the
    /// line halving the angle between them when they're not. It covers both lines (seen from the
    /// centreline) and runs <paramref name="overshoot"/> past each end.
    /// </summary>
    public static PathGeometry? Between(LineTarget a, LineTarget b, double overshoot)
    {
        var da = a.B - a.A;
        var db = b.B - b.A;
        if (da.Length < 1e-9 || db.Length < 1e-9) return null;
        var ua = da / da.Length;
        var ub = db / db.Length;
        Vec2 origin, dir;
        double cross = ua.Cross(ub);
        if (Math.Abs(cross) < 1e-6)
        {
            // Parallel: halfway between, the way the first line runs.
            if (ua.Dot(ub) < 0) ub = -ub;
            dir = ua;
            var foot = b.A + ub * (a.A - b.A).Dot(ub);
            origin = (a.A + foot) * 0.5;
        }
        else
        {
            // Where they meet; each heads off towards its far end; the centreline halves the angle between.
            double t = (b.A - a.A).Cross(ub) / cross;
            origin = a.A + ua * t;
            var fa = a.A.Dist(origin) > a.B.Dist(origin) ? a.A : a.B;
            var fb = b.A.Dist(origin) > b.B.Dist(origin) ? b.A : b.B;
            var va = fa - origin;
            var vb = fb - origin;
            if (va.Length < 1e-9 || vb.Length < 1e-9) return null;
            var sum = va / va.Length + vb / vb.Length;
            if (sum.Length < 1e-9) return null;
            dir = sum / sum.Length;
        }
        var along = new[] { a.A, a.B, b.A, b.B }.Select(p => (p - origin).Dot(dir)).ToList();
        double lo = along.Min() - overshoot, hi = along.Max() + overshoot;
        if (hi - lo < 1e-9) return null;
        return PathGeometry.Line(origin + dir * lo, origin + dir * hi);
    }

    /// <summary>A cross through a circle's centre (or a shape's middle), running <paramref name="overshoot"/> past its edge.</summary>
    public static PathGeometry? CentreMark(CadTarget t, double overshoot)
    {
        Vec2 c;
        double hx, hy;
        switch (t)
        {
            case CircleTarget ct: c = ct.Centre; hx = hy = ct.Radius + overshoot; break;
            case BoxTarget bt when !bt.Box.IsEmpty: c = bt.Box.Center; hx = bt.Box.Width / 2 + overshoot; hy = bt.Box.Height / 2 + overshoot; break;
            default: return null;
        }
        if (hx < 1e-9 || hy < 1e-9) return null;
        return new PathGeometry(
        [
            new Subpath(new Vec2(c.X - hx, c.Y), [Seg.Line(new Vec2(c.X + hx, c.Y))], false),
            new Subpath(new Vec2(c.X, c.Y - hy), [Seg.Line(new Vec2(c.X, c.Y + hy))], false),
        ]);
    }
}
