using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>How the jig is built.</summary>
public enum JigBuild
{
    /// <summary>One sheet with the pockets cut right through: the objects stand on the bed inside it.</summary>
    OneSheet,
    /// <summary>Sheets with the pockets cut through, glued onto a solid base sheet.</summary>
    StackedWithBase,
    /// <summary>Sheets with the pockets cut through, stacked with no base: the objects stand on the bed.</summary>
    StackedNoBase,
}

/// <summary>Where a pocket's finger notches go.</summary>
public enum JigNotches
{
    /// <summary>A notch each side, across the middle of the object.</summary>
    BothSides,
    /// <summary>One notch, on the side towards the front of the bed.</summary>
    OneSide,
    None,
}

/// <summary>How the design is put on the object.</summary>
public enum DesignPlacement
{
    /// <summary>Where it is on the canvas, over the object (its photo or outline).</summary>
    AsPlaced,
    /// <summary>Its middle on the object's middle (turned along it, sized to fit when asked).</summary>
    Centred,
}

/// <summary>Settings for a jig (see <see cref="JigMaker"/>).</summary>
public sealed record JigOptions
{
    public JigBuild Build { get; init; } = JigBuild.OneSheet;
    /// <summary>How tall the object is (mm): for the depth suggestion and the warnings.</summary>
    public double ObjectHeightMm { get; init; } = 10;
    /// <summary>Thickness of one sheet (mm).</summary>
    public double SheetMm { get; init; } = 3;
    /// <summary>Stacked: how deep the object sits (rounded up to whole sheets). One sheet: the sheet.</summary>
    public double DepthMm { get; init; } = 6;
    /// <summary>Room round the object, each side (mm), so it drops in without rattling.</summary>
    public double GapMm { get; init; } = 0.6;
    /// <summary>Inside corners and slots narrower than this are rounded off / filled (mm): a laser can't cut a slot thinner than its beam, and objects drop in more easily.</summary>
    public double RoundInsideMm { get; init; } = 1;
    public JigNotches Notches { get; init; } = JigNotches.BothSides;
    /// <summary>Finger notch diameter (mm).</summary>
    public double NotchDiameterMm { get; init; } = 18;
    /// <summary>Turn the object so its long side runs left to right (packs best, and the design runs along the bed).</summary>
    public bool LayStraight { get; init; } = true;
    /// <summary>Pockets across and down: a jig for a batch.</summary>
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;
    /// <summary>Wood left between two pockets (mm).</summary>
    public double WallMm { get; init; } = 6;
    /// <summary>Wood round the pockets (mm).</summary>
    public double MarginMm { get; init; } = 10;
    public double CornerRadiusMm { get; init; } = 3;
    /// <summary>The back-left corner is cut off this far (mm), so the jig only goes one way round and the camera can tell which way it lies. 0 = square.</summary>
    public double MarkerMm { get; init; } = 10;
    /// <summary>Stacked sheets: dowel holes in three corners to line the sheets up when gluing (mm, 0 = none).</summary>
    public double PinDiameterMm { get; init; } = 3;
    /// <summary>The jig's name: used for its pages, and engraved on it when <see cref="EngraveName"/>.</summary>
    public string Name { get; init; } = "";
    public bool EngraveName { get; init; } = true;
    public double LabelSizeMm { get; init; } = 5;
    public string LabelFont { get; init; } = "Arial";
    /// <summary>Where the jig's middle goes on the bed; null = where the object lay (kept inside the work area).</summary>
    public Vec2? Centre { get; init; }

    public Layer? CutSettings { get; init; }
    public Layer? EngraveSettings { get; init; }

    public DesignPlacement Placement { get; init; } = DesignPlacement.AsPlaced;
    /// <summary>Centred: turn the design so its left-to-right runs along the object's long side.</summary>
    public bool TurnDesign { get; init; } = true;
    /// <summary>Centred: make the design as big as fits inside the object, <see cref="FitMarginMm"/> in from its edge.</summary>
    public bool FitDesign { get; init; }
    public double FitMarginMm { get; init; } = 2;

    /// <summary>Sheets with the pockets cut through.</summary>
    public int CutSheets => Build == JigBuild.OneSheet ? 1 : Math.Max(1, (int)Math.Ceiling(DepthMm / Math.Max(0.1, SheetMm) - 1e-9));
    public bool HasBase => Build == JigBuild.StackedWithBase;
    /// <summary>How deep the object sits in the jig (mm).</summary>
    public double PocketDepthMm => CutSheets * SheetMm;
    /// <summary>How tall the whole jig is (mm).</summary>
    public double ThicknessMm => (CutSheets + (HasBase ? 1 : 0)) * SheetMm;
    /// <summary>The name, or "Jig".</summary>
    public string Title => string.IsNullOrWhiteSpace(Name) ? "Jig" : $"{Name.Trim()} jig";
}

/// <summary>The engraved name: its words, where their middle goes and the box they take (all in bed mm).</summary>
public sealed record JigLabel(string Text, Vec2 Centre, Rect2 Box);

/// <summary>What a jig comes to, in bed mm, before anything goes in the document.</summary>
/// <param name="Outline">The jig's outside edge (with its cut-off corner).</param>
/// <param name="Corner">The piece the cut-off corner takes away (empty when it's square): the camera looks for it.</param>
/// <param name="Pockets">Every pocket with its notches.</param>
/// <param name="ObjectMoves">For each pocket, in reading order (back to front, left to right): the move taking the object (as found) into it.</param>
/// <param name="ObjectOutlines">The object's outline in each pocket.</param>
public sealed record JigPlan(
    Rect2 Board,
    PathGeometry Outline,
    PathGeometry Corner,
    PathGeometry Pockets,
    PathGeometry PinHoles,
    IReadOnlyList<Affine> ObjectMoves,
    PathGeometry ObjectOutlines,
    JigLabel? Label,
    double ObjectAngleDeg,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The object jig maker: an odd-shaped object (a pen, a phone case, a knife handle), outlined by the
/// camera or drawn, becomes a sheet with a pocket that holds it (or a grid of pockets for a batch), and
/// the design goes on a page of its own lined up with each pocket, so every object is marked in the
/// same place. Nothing here touches the machine.
/// </summary>
public static class JigMaker
{
    public const string PocketLayer = "Jig · pockets", OutlineLayer = "Jig · outline", LabelLayer = "Jig · name",
        MarksLayer = "Jig · object marks", GuideLayer = "Jig (guide, not marked)";
    /// <summary>Shape notes (<see cref="Shape.AddonData"/>) on the guide shapes of the objects page.</summary>
    public const string PartKey = "objectjig.part", IdKey = "objectjig.id", ThicknessKey = "objectjig.thickness";
    public const string PartOutline = "outline", PartPockets = "pockets", PartCorner = "corner", PartObjects = "objects";

    const double Tol = 0.02;

    // ---- the object -----------------------------------------------------------------------------

    /// <summary>The outer outlines of a shape, one geometry each (holes filled in), biggest first.</summary>
    public static List<PathGeometry> Outers(PathGeometry g)
    {
        var merged = ShapeGeometry.Union([g], Tol);
        var subs = merged.Subpaths.Where(sp => sp.Closed && !sp.Segs.IsEmpty).ToList();
        var flat = subs.Select(sp => sp.Flatten(0.1)).ToList();
        var areas = flat.Select(f => Math.Abs(Geom.SignedArea(f))).ToList();
        var o = new List<(PathGeometry G, double A)>();
        for (int i = 0; i < subs.Count; i++)
        {
            bool inside = false;
            for (int j = 0; j < subs.Count && !inside; j++)
                inside = j != i && areas[j] > areas[i] && Geom.Inside(flat[j], flat[i][0]);
            if (!inside && areas[i] > 0.01) o.Add((new PathGeometry([subs[i]]), areas[i]));
        }
        return [.. o.OrderByDescending(x => x.A).Select(x => x.G)];
    }

    /// <summary>
    /// What's on the bed, from a mask of what isn't the bed (one byte a pixel, over 127 = something):
    /// each separate thing's outline, holes filled in, biggest first.
    /// </summary>
    /// <param name="area">The bed area the picture covers (mm); pixel (0, 0) is its top-left.</param>
    public static List<PathGeometry> FromMask(byte[] keep, int width, int height, Rect2 area, double mmPerPx, double minAreaMm2 = 50, double smoothness = 2)
    {
        var mask = new bool[keep.Length];
        for (int i = 0; i < keep.Length; i++) mask[i] = keep[i] > 127;
        var traced = Doc.Tracing.Tracer.TraceMask(mask, width, height, new Doc.Tracing.TraceOptions
        {
            Smoothness = smoothness,
            MinAreaPx = (int)Math.Max(4, minAreaMm2 / (mmPerPx * mmPerPx) / 4),
        });
        var toBed = Affine.Scale(mmPerPx, mmPerPx).Then(Affine.Translate(area.MinX, area.MinY));
        return [.. Outers(traced.Transform(toBed)).Where(g => ShapeGeometry.Area(g) >= minAreaMm2)];
    }

    /// <summary>The middle of an outline's area, the way its long side runs (degrees) and how long it is for its width (≥ 1).</summary>
    public static (Vec2 Centre, double AngleDeg, double Elongation) Axis(PathGeometry outline)
    {
        double a = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        foreach (var f in outline.Flatten(0.05).Where(f => f.Closed || f.Points.Count > 2))
        {
            var p = f.Points;
            for (int i = 0; i < p.Count; i++)
            {
                var (x0, y0) = (p[i].X, p[i].Y);
                var (x1, y1) = (p[(i + 1) % p.Count].X, p[(i + 1) % p.Count].Y);
                double c = x0 * y1 - x1 * y0;
                a += c;
                sx += (x0 + x1) * c; sy += (y0 + y1) * c;
                sxx += (x0 * x0 + x0 * x1 + x1 * x1) * c;
                syy += (y0 * y0 + y0 * y1 + y1 * y1) * c;
                sxy += (x0 * y1 + 2 * x0 * y0 + 2 * x1 * y1 + x1 * y0) * c;
            }
        }
        a /= 2;
        if (Math.Abs(a) < 1e-9) return (outline.Bounds().Center, 0, 1);
        double cx = sx / (6 * a), cy = sy / (6 * a);
        double mxx = sxx / (12 * a) - cx * cx, myy = syy / (12 * a) - cy * cy, mxy = sxy / (24 * a) - cx * cy;
        double half = (mxx + myy) / 2, d = Math.Sqrt(Math.Max(0, (mxx - myy) * (mxx - myy) / 4 + mxy * mxy));
        double l1 = half + d, l2 = Math.Max(1e-12, half - d);
        return (new Vec2(cx, cy), 0.5 * Math.Atan2(2 * mxy, mxx - myy) * 180 / Math.PI, Math.Sqrt(Math.Max(1, l1 / l2)));
    }

    // ---- the plan -------------------------------------------------------------------------------

    /// <summary>Works the jig out: pockets, notches, the sheet round them, its name and anything to warn about.</summary>
    /// <param name="objectOutline">The object as it lay on the bed (or as drawn), in bed mm.</param>
    /// <param name="workArea">The machine's work area (to keep the jig on it).</param>
    public static JigPlan Plan(PathGeometry objectOutline, JigOptions o, Rect2 workArea, Func<string, TextStyle, Rect2>? measure = null)
    {
        var outers = Outers(objectOutline);
        if (outers.Count == 0) throw new InvalidOperationException("The object has no closed outline to make a pocket for.");
        var obj = outers[0];
        measure ??= Measure;
        var warnings = new List<string>();
        var (c, angle, _) = Axis(obj);

        // The object in the jig's own frame: its middle at 0, 0, turned to lie straight when asked.
        double turn = o.LayStraight ? -angle : 0;
        var toLocal = Affine.Translate(-c.X, -c.Y).Then(Affine.Rotate(turn));
        var local = obj.Transform(toLocal);

        // The pocket: the outline grown by the gap, narrow inside corners and slots filled.
        double gap = Math.Max(0, o.GapMm), round = Math.Max(0, o.RoundInsideMm);
        var pocket = gap + round > 0 ? ShapeGeometry.Offset(local, gap + round, JoinStyle.Round, EndStyle.Flat, tolerance: Tol) : local;
        if (round > 0) pocket = ShapeGeometry.Offset(pocket, -round, JoinStyle.Round, EndStyle.Flat, tolerance: Tol);
        pocket = Outers(pocket) is { Count: > 0 } po ? po[0] : throw new InvalidOperationException("The object is too small to make a pocket for.");
        var notches = Notches(pocket, o.LayStraight ? 0 : angle, o);
        if (notches.Count > 0) pocket = ShapeGeometry.Union([pocket, .. notches], Tol);

        // The grid of pockets, centred on 0, 0.
        int cols = Math.Clamp(o.Columns, 1, 50), rows = Math.Clamp(o.Rows, 1, 50);
        var pb = pocket.Bounds(0.05);
        double wall = Math.Max(0, o.WallMm), px = pb.Width + wall, py = pb.Height + wall;
        if (wall < 3 && cols * rows > 1) warnings.Add($"A wall of {wall:0.#} mm between pockets is thin: it may burn through or break. 4 mm or more is safer.");
        var moves = new List<Affine>();
        var pockets = new List<PathGeometry>();
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                var cell = new Vec2((i - (cols - 1) / 2.0) * px, (j - (rows - 1) / 2.0) * py);
                var d = cell - pb.Center;
                moves.Add(toLocal.Then(Affine.Translate(d)));
                pockets.Add(pocket.Transform(Affine.Translate(d)));
            }
        double gw = cols * pb.Width + (cols - 1) * wall, gh = rows * pb.Height + (rows - 1) * wall;
        var content = new Rect2(-gw / 2, -gh / 2, gw / 2, gh / 2);

        // The sheet round them, with room for the name under them.
        double margin = Math.Max(2, o.MarginMm);
        var board = content.Inflate(margin);
        JigLabel? label = null;
        var style = LabelStyle(o);
        if (o.EngraveName && !string.IsNullOrWhiteSpace(o.Name))
        {
            var size = measure(o.Name.Trim(), style);
            double top = content.MaxY + Math.Max(3, margin * 0.6);
            var centre = new Vec2(0, top + size.Height / 2);
            var box = new Rect2(centre.X - size.Width / 2, top, centre.X + size.Width / 2, top + size.Height);
            board = board.Union(box.Inflate(Math.Max(3, margin * 0.6)));
            label = new JigLabel(o.Name.Trim(), centre, box);
        }
        double r = Math.Clamp(o.CornerRadiusMm, 0, Math.Min(board.Width, board.Height) / 4);
        var rect = PathGeometry.RoundRect(board.MinX, board.MinY, board.Width, board.Height, r, r);

        // The cut-off back-left corner: the jig only fits one way round, and the camera can tell which.
        var corner = PathGeometry.Empty;
        var outline = rect;
        double m = o.MarkerMm <= 0 ? 0 : Math.Min(Math.Max(o.MarkerMm, r * 1.5 + 2), Math.Min(board.Width, board.Height) / 3);
        if (m > 0)
        {
            const double e = 1;
            var tri = PathGeometry.Polyline([new(board.MinX - e, board.MinY - e), new(board.MinX + m, board.MinY - e), new(board.MinX - e, board.MinY + m)], true);
            outline = ShapeGeometry.Boolean(rect, tri, BoolOp.Subtract, Tol);
            corner = ShapeGeometry.Boolean(rect, tri, BoolOp.Intersect, Tol);
            var all = ShapeGeometry.Union(pockets, Tol);
            if (ShapeGeometry.Area(ShapeGeometry.Boolean(ShapeGeometry.Offset(tri, 2, JoinStyle.Round, EndStyle.Flat), all, BoolOp.Intersect)) > 0.01)
                warnings.Add("The cut-off corner runs into a pocket: give the jig more margin or a smaller corner.");
        }

        // Dowel holes in three corners (stacked sheets).
        var pins = PathGeometry.Empty;
        if (o.Build != JigBuild.OneSheet && o.PinDiameterMm > 0)
        {
            double pr = o.PinDiameterMm / 2, inset = Math.Max(margin / 2, pr + 1.5) + r * 0.3;
            if (margin < o.PinDiameterMm + 3)
                warnings.Add($"No dowel holes: the margin ({margin:0.#} mm) has no room for them. Make it at least {o.PinDiameterMm + 3:0.#} mm.");
            else
            {
                Vec2[] at = [new(board.MaxX - inset, board.MinY + inset), new(board.MaxX - inset, board.MaxY - inset), new(board.MinX + inset, board.MaxY - inset)];
                pins = new PathGeometry(at.SelectMany(p => PathGeometry.Ellipse(p.X, p.Y, pr, pr).Subpaths));
                var all = ShapeGeometry.Union(pockets, Tol);
                if (ShapeGeometry.Area(ShapeGeometry.Boolean(ShapeGeometry.Offset(pins, 1.5, JoinStyle.Round, EndStyle.Flat), all, BoolOp.Intersect)) > 0.01 ||
                    (label is { } lb && at.Any(p => lb.Box.Inflate(pr + 1).Contains(p))))
                {
                    pins = PathGeometry.Empty;
                    warnings.Add("No dowel holes: they'd run into a pocket or the name. Give the jig more margin.");
                }
            }
        }

        // Onto the bed: where it's asked for, else where the object lay, kept on the work area.
        var want = o.Centre ?? c;
        if (o.Centre is null && !workArea.IsEmpty)
        {
            double hx = board.Width / 2, hy = board.Height / 2;
            want = new Vec2(
                workArea.Width >= board.Width ? Math.Clamp(want.X, workArea.MinX + hx, workArea.MaxX - hx) : workArea.Center.X,
                workArea.Height >= board.Height ? Math.Clamp(want.Y, workArea.MinY + hy, workArea.MaxY - hy) : workArea.Center.Y);
        }
        var place = Affine.Translate(want - board.Center);
        var placedBoard = new Rect2(board.MinX + place.E, board.MinY + place.F, board.MaxX + place.E, board.MaxY + place.F);
        if (!workArea.IsEmpty)
        {
            if (board.Width > workArea.Width || board.Height > workArea.Height)
                warnings.Add($"The jig ({board.Width:0} × {board.Height:0} mm) is bigger than the work area ({workArea.Width:0} × {workArea.Height:0} mm): use fewer pockets.");
            else if (!workArea.Contains(placedBoard))
                warnings.Add("The jig runs off the work area: move its middle towards the middle of the bed.");
        }

        // Depth and height.
        double h = o.ObjectHeightMm, depth = o.PocketDepthMm;
        if (h > 0)
        {
            if (depth > h + 0.01)
                warnings.Add($"The pockets ({depth:0.#} mm) are deeper than the object is tall ({h:0.#} mm): it sits below the jig's top. That's fine for marking, but harder to lift out; use fewer or thinner sheets.");
            else if (depth < h * 0.3)
                warnings.Add($"The pockets are shallow ({depth:0.#} mm for an object {h:0.#} mm tall): it may tip or slide out. About half its height holds it well.");
        }
        if (gap < 0.2) warnings.Add("With almost no gap the object may not go in: a camera outline is rarely closer than half a millimetre.");

        var placedMoves = moves.Select(mv => mv.Then(place)).ToList();
        return new JigPlan(
            placedBoard,
            outline.Transform(place),
            corner.Transform(place),
            ShapeGeometry.Union(pockets, Tol).Transform(place),
            pins.Transform(place),
            placedMoves,
            new PathGeometry(placedMoves.SelectMany(mv => obj.Transform(mv).Subpaths)),
            label is null ? null : new JigLabel(label.Text, place.Apply(label.Centre), new Rect2(label.Box.MinX + place.E, label.Box.MinY + place.F, label.Box.MaxX + place.E, label.Box.MaxY + place.F)),
            angle,
            warnings);
    }

    static TextStyle LabelStyle(JigOptions o) => new(o.LabelFont, Math.Max(1, o.LabelSizeMm), Align: TextAlign.Center);

    /// <summary>Finger notch circles: on the line across the object's middle, centred on the pocket's edge.</summary>
    static List<PathGeometry> Notches(PathGeometry pocket, double axisDeg, JigOptions o)
    {
        if (o.Notches == JigNotches.None || o.NotchDiameterMm <= 0) return [];
        double a = axisDeg * Math.PI / 180;
        var n = new Vec2(-Math.Sin(a), Math.Cos(a));
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        // Across the area's middle, not the box's (they differ for a lopsided object).
        var c = Axis(pocket).Centre;
        foreach (var f in pocket.Flatten(0.05))
        {
            var p = f.Points;
            int count = f.Closed ? p.Count : p.Count - 1;
            for (int i = 0; i < count; i++)
                if (Cross(c, n, p[i], p[(i + 1) % p.Count]) is { } s) { lo = Math.Min(lo, s); hi = Math.Max(hi, s); }
        }
        if (!double.IsFinite(lo) || !double.IsFinite(hi)) return [];
        double rr = o.NotchDiameterMm / 2;
        var front = c + n * hi;
        var back = c + n * lo;
        if (front.Y < back.Y) (front, back) = (back, front);
        var list = new List<PathGeometry> { PathGeometry.Ellipse(front.X, front.Y, rr, rr) };
        if (o.Notches == JigNotches.BothSides) list.Add(PathGeometry.Ellipse(back.X, back.Y, rr, rr));
        return list;
    }

    /// <summary>Where the line c + s·n crosses segment a–b: s, or null.</summary>
    static double? Cross(Vec2 c, Vec2 n, Vec2 a, Vec2 b)
    {
        var d = b - a;
        double den = n.Cross(d);
        if (Math.Abs(den) < 1e-12) return null;
        var ca = a - c;
        double s = ca.Cross(d) / den, t = ca.Cross(n) / den;
        return t is >= 0 and <= 1 ? s : null;
    }

    /// <summary>The box a label's words take (their outline's bounds, centred on 0, 0).</summary>
    public static Rect2 Measure(string text, TextStyle style)
    {
        var g = TextLayout.Build(text, style, null);
        var b = g.IsEmpty ? Rect2.Empty : g.Bounds(0.1);
        if (b.IsEmpty) { double w = text.Length * style.SizeMm * 0.6; return new Rect2(-w / 2, -style.SizeMm / 2, w / 2, style.SizeMm / 2); }
        return new Rect2(-b.Width / 2, -b.Height / 2, b.Width / 2, b.Height / 2);
    }

    // ---- the design -----------------------------------------------------------------------------

    /// <summary>Points round the design (for turning and fitting it): its outlines, or the corners of pictures.</summary>
    public static List<Vec2> DesignPoints(IEnumerable<Shape> design)
    {
        var o = new List<Vec2>();
        foreach (var s in design)
        {
            if (s is PathShape p && s is not ImageShape)
                foreach (var f in p.World.Flatten(0.1)) o.AddRange(f.Points);
            else
            {
                var b = s.Bounds;
                if (!b.IsEmpty) o.AddRange([b.TopLeft, new(b.MaxX, b.MinY), b.BottomRight, new(b.MinX, b.MaxY)]);
            }
        }
        return o;
    }

    /// <summary>
    /// The move putting the design on the object as it lay (before it goes into a pocket): none when it
    /// was placed on the canvas; else its middle onto the object's, turned along it and sized to fit when asked.
    /// </summary>
    public static (Affine Move, string? Warning) DesignOnObject(PathGeometry objectOutline, IReadOnlyList<Vec2> designPoints, JigOptions o)
    {
        if (o.Placement == DesignPlacement.AsPlaced || designPoints.Count == 0) return (Affine.Identity, null);
        var outers = Outers(objectOutline);
        if (outers.Count == 0) return (Affine.Identity, null);
        var obj = outers[0];
        var (c, angle, elong) = Axis(obj);
        var db = Rect2.Bounds(designPoints);
        var dc = db.Center;
        // The design's left-to-right runs along the object's long side (the smaller turn of the two ways).
        double turn = o.TurnDesign && elong > 1.05 ? PieceFinder.TurnOnto(0, angle) : 0;
        var move = Affine.RotateAbout(dc, turn).Then(Affine.Translate(c - dc));
        if (!o.FitDesign) return (move, null);

        // As big as fits inside the object, the margin in from its edge.
        var inset = Outers(ShapeGeometry.Offset(obj, -Math.Max(0, o.FitMarginMm), JoinStyle.Round, EndStyle.Flat, tolerance: Tol));
        if (inset.Count == 0) return (move, "The object is too small for that margin: the design wasn't sized to fit.");
        var polys = inset.Select(g => g.Flatten(0.05)[0].Points).ToList();
        var pts = designPoints.Select(move.Apply).ToList();
        // Fewer points for the search: every outline point would be slow on big designs.
        int stride = Math.Max(1, pts.Count / 1500);
        var test = pts.Where((_, i) => i % stride == 0).ToList();
        bool Fits(double s) => test.All(p => polys.Any(poly => Geom.Inside(poly, c + (p - c) * s)));
        if (!Fits(1e-3)) return (move, "The object's middle isn't inside it (a ring or a U shape?): the design wasn't sized to fit.");
        double lo = 1e-3, hi = lo;
        for (double s = 1e-3; s <= 100; s *= 1.15) { if (Fits(s)) lo = s; else { hi = s; break; } }
        if (hi <= lo) return (move.Then(Affine.ScaleAbout(c, lo, lo)), null);
        for (int k = 0; k < 30; k++) { double mid = (lo + hi) / 2; if (Fits(mid)) lo = mid; else hi = mid; }
        return (move.Then(Affine.ScaleAbout(c, lo, lo)), null);
    }

    // ---- into the document ----------------------------------------------------------------------

    /// <summary>
    /// The jig on pages of its own after the current one, all in the same place on the bed: the sheets to
    /// cut (when <paramref name="cutPages"/>), then the objects page with the design on every object (each
    /// copy one step on in serial numbers / spreadsheet rows) and the jig drawn as a guide that's never
    /// marked. The first new page is shown.
    /// </summary>
    /// <param name="design">The shapes to mark on each object (from the page on screen; may be empty).</param>
    public static DocState Make(DocState s, PathGeometry objectOutline, JigOptions o, IReadOnlyList<Shape> design, Rect2 workArea,
        bool cutPages = true, Func<string, TextStyle, Rect2>? measure = null)
    {
        var plan = Plan(objectOutline, o, workArea, measure);
        string title = o.Title;
        var pages = new List<DesignPage>();
        int cut = o.CutSheets;
        var outlineCut = new PathGeometry([.. plan.PinHoles.Subpaths, .. plan.Outline.Subpaths]);
        if (cutPages)
        {
            if (cut == 1)
                pages.Add(SheetPage($"{title} · {(o.Build == JigBuild.OneSheet ? "cut" : "sheet")}", o, plan, outlineCut, withLabel: true));
            else
            {
                pages.Add(SheetPage($"{title} · top sheet", o, plan, outlineCut, withLabel: true));
                pages.Add(SheetPage($"{title} · sheets (cut {cut - 1})", o, plan, outlineCut, withLabel: false));
            }
            if (o.HasBase)
            {
                var layers = new List<Layer>();
                var shapes = new List<Shape>();
                var marks = NewLayer(o.EngraveSettings, MarksLayer, LayerMode.Score, 0x5B6270, layers.Count);
                layers.Add(marks);
                shapes.Add(new PathShape { Name = "Where the objects go", LayerId = marks.Id, Geometry = plan.ObjectOutlines });
                var edge = NewLayer(o.CutSettings, OutlineLayer, LayerMode.Cut, 0xE63946, layers.Count);
                layers.Add(edge);
                shapes.Add(new PathShape { Name = "Jig", LayerId = edge.Id, Geometry = outlineCut });
                pages.Add(new DesignPage($"{title} · base", [.. layers], [.. shapes], []));
            }
        }
        pages.Add(ObjectsPage(s, $"{title} · objects", o, plan, objectOutline, design));
        return Pages.Insert(s, pages);
    }

    /// <summary>A sheet: its name engraved first (so the sheet hasn't moved), then the pockets, then the outline.</summary>
    static DesignPage SheetPage(string name, JigOptions o, JigPlan plan, PathGeometry outlineCut, bool withLabel)
    {
        var layers = new List<Layer>();
        var shapes = new List<Shape>();
        if (withLabel && plan.Label is { } l)
        {
            var layer = NewLayer(o.EngraveSettings, LabelLayer, LayerMode.Fill, 0x2A9D8F, layers.Count);
            layers.Add(layer);
            var style = LabelStyle(o);
            var t0 = TextShape.Create(l.Text, style, layer.Id, Affine.Identity);
            var b = t0.Geometry.IsEmpty ? new Rect2(0, 0, 0, 0) : t0.Geometry.Bounds(0.1);
            shapes.Add(TextShape.Create(l.Text, style, layer.Id, Affine.Translate(l.Centre - b.Center), name: $"Name · {l.Text}"));
        }
        var pockets = NewLayer(o.CutSettings, PocketLayer, LayerMode.Cut, 0xF4A261, layers.Count);
        layers.Add(pockets);
        shapes.Add(new PathShape { Name = "Pockets", LayerId = pockets.Id, Geometry = plan.Pockets });
        var edge = NewLayer(o.CutSettings, OutlineLayer, LayerMode.Cut, 0xE63946, layers.Count);
        layers.Add(edge);
        shapes.Add(new PathShape { Name = "Jig", LayerId = edge.Id, Geometry = outlineCut });
        return new DesignPage(name, [.. layers], [.. shapes], []);
    }

    /// <summary>The page the objects are marked from: the design on every object, and the jig as a guide.</summary>
    static DesignPage ObjectsPage(DocState s, string name, JigOptions o, JigPlan plan, PathGeometry objectOutline, IReadOnlyList<Shape> design)
    {
        // The page's own copies of the layers (as a new page gets them), sub-layers kept on their parents.
        var map = s.Layers.ToDictionary(l => l.Id, _ => Guid.NewGuid());
        var layers = s.Layers.Select(l => l with { Id = map[l.Id], ParentId = l.ParentId is { } p && map.TryGetValue(p, out var np) ? np : l.ParentId }).ToList();
        var guide = Layer.Default(layers.Count, LayerMode.Score) with { Name = GuideLayer, Output = false, Locked = true, Color = 0x8A8F98 };
        layers.Add(guide);

        var shapes = new List<Shape>();
        var placedDesign = design.Select(d => d.With(layerId: map.TryGetValue(d.LayerId, out var nl) ? nl : d.LayerId)).ToList();
        if (placedDesign.Count > 0)
        {
            var (onObject, _) = DesignOnObject(objectOutline, DesignPoints(design), o);
            for (int k = 0; k < plan.ObjectMoves.Count; k++)
            {
                var copy = Ops.CopyAsUnit(placedDesign, onObject.Then(plan.ObjectMoves[k])).Select(c => c.WithVarOffset(k)).ToList();
                if (copy.Count > 1)
                {
                    var g = Guid.NewGuid();
                    copy = [.. copy.Select(c => c.GroupId is null ? c.WithGroup(g) : c)];
                }
                shapes.AddRange(copy);
            }
        }
        var id = Guid.NewGuid().ToString("N");
        Shape Guide(string part, string label, PathGeometry g) => new PathShape { Name = label, LayerId = guide.Id, Geometry = g }
            .WithAddonData(PartKey, part).WithAddonData(IdKey, id).WithAddonData(ThicknessKey, o.ThicknessMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        shapes.Add(Guide(PartOutline, "Jig outline", plan.Outline));
        shapes.Add(Guide(PartPockets, "Jig pockets", plan.Pockets));
        if (!plan.Corner.IsEmpty) shapes.Add(Guide(PartCorner, "Jig's cut-off corner", plan.Corner));
        shapes.Add(Guide(PartObjects, "Objects", plan.ObjectOutlines));
        return new DesignPage(name, [.. layers], [.. shapes], s.Guides);
    }

    /// <summary>A layer for the jig: the given settings (or the defaults) under the jig's name.</summary>
    static Layer NewLayer(Layer? settings, string name, LayerMode mode, uint colour, int index) =>
        (settings ?? Layer.Default(index, mode)) with { Id = Guid.NewGuid(), Name = name, Mode = mode, ParentId = null, Color = colour, Output = true, Visible = true, Locked = false };

    // ---- the jig's guide on a page --------------------------------------------------------------

    /// <summary>The jig's guide shapes on the page shown, as they are now (moved or not); null when the page has none.</summary>
    public static JigShapes? GuideOn(DocState s)
    {
        var parts = s.Shapes.OfType<PathShape>().Where(x => x.AddonData.ContainsKey(PartKey)).ToList();
        if (parts.Count == 0) return null;
        // Several jigs on one page: the one whose outline was added last.
        var outline = parts.LastOrDefault(x => x.AddonData[PartKey] == PartOutline);
        if (outline is null) return null;
        var id = outline.AddonData.GetValueOrDefault(IdKey);
        var mine = parts.Where(x => x.AddonData.GetValueOrDefault(IdKey) == id).ToList();
        PathGeometry Part(string p) => mine.FirstOrDefault(x => x.AddonData[PartKey] == p)?.World ?? PathGeometry.Empty;
        double.TryParse(outline.AddonData.GetValueOrDefault(ThicknessKey), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t);
        return new JigShapes(outline.World, Part(PartPockets), Part(PartCorner), t);
    }
}

/// <summary>A jig as drawn on its objects page: what the camera looks for to find it again.</summary>
/// <param name="ThicknessMm">How tall the jig is (mm).</param>
public sealed record JigShapes(PathGeometry Outline, PathGeometry Pockets, PathGeometry Corner, double ThicknessMm);
