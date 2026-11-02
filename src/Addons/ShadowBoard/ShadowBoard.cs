using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addons.ShadowBoard;

/// <summary>One tool to make a pocket for: its outline as it lies on the bed (closed, mm), and the label engraved by it.</summary>
public sealed record ShadowTool(string Name, PathGeometry Outline);

/// <summary>How the board is made.</summary>
public enum ShadowBoardStyle
{
    /// <summary>Sheets stacked and glued: the top sheets have the tools cut right through, the base sheet is solid.</summary>
    StackedSheets,
    /// <summary>One thick board with the pockets engraved down into it.</summary>
    EngravedPockets,
}

/// <summary>Where a tool's finger notches go.</summary>
public enum NotchPlacement
{
    /// <summary>No notches.</summary>
    None,
    /// <summary>One notch, on the front side of the tool (towards the front of the bed).</summary>
    Front,
    /// <summary>A notch on each side, across the middle of the tool.</summary>
    BothSides,
}

/// <summary>Settings for a shadow board (see <see cref="ShadowBoardMaker"/>).</summary>
public sealed record ShadowBoardOptions
{
    public ShadowBoardStyle Style { get; init; } = ShadowBoardStyle.StackedSheets;
    /// <summary>How deep the tools sit (mm). Stacked sheets: rounded up to whole sheets.</summary>
    public double DepthMm { get; init; } = 9;
    /// <summary>Stacked sheets: the thickness of one sheet (mm).</summary>
    public double SheetMm { get; init; } = 3;
    /// <summary>Engraved pockets: how deep one engrave pass goes (mm, from the etch-rate test).</summary>
    public double EtchMmPerPass { get; init; } = 0.15;
    /// <summary>Room round each tool, each side (mm): the camera outline is rarely exact, and tools must drop in.</summary>
    public double GapMm { get; init; } = 1.5;
    public NotchPlacement Notches { get; init; } = NotchPlacement.BothSides;
    /// <summary>Finger notch diameter (mm).</summary>
    public double NotchDiameterMm { get; init; } = 22;
    /// <summary>Solid board round the tools (mm); with a fixed size it's the least there must be.</summary>
    public double MarginMm { get; init; } = 15;
    /// <summary>A fixed board size (mm), e.g. the drawer's inside; 0 = just big enough for the tools.</summary>
    public double BoardWidthMm { get; init; }
    public double BoardHeightMm { get; init; }
    public double CornerRadiusMm { get; init; } = 4;
    /// <summary>Engrave each tool's name by its pocket.</summary>
    public bool Labels { get; init; } = true;
    public double LabelSizeMm { get; init; } = 5;
    public string LabelFont { get; init; } = "Arial";
    /// <summary>Stacked sheets: engrave each tool's silhouette on the base sheet, so a missing tool shows at a glance.</summary>
    public bool Shadow { get; init; } = true;
    /// <summary>Stacked sheets: holes in the corners of every sheet for dowels, to line the stack up when gluing (0 = none).</summary>
    public double PinDiameterMm { get; init; } = 3;
    /// <summary>Cut the board's outline (off when the pockets go into a board that is already the right size).</summary>
    public bool CutOutline { get; init; } = true;

    /// <summary>Cut settings for the sheets / the board's outline.</summary>
    public Layer? CutSettings { get; init; }
    /// <summary>Engrave settings for the labels and the shadows.</summary>
    public Layer? EngraveSettings { get; init; }
    /// <summary>Engraved pockets: the pockets' engrave settings (one pass each; the passes come from the depth).</summary>
    public Layer? PocketSettings { get; init; }
}

/// <summary>A label's place: the words, centred on <see cref="Centre"/>, and the box they take.</summary>
public sealed record LabelSpot(string Text, Vec2 Centre, Rect2 Box, bool Fits);

/// <summary>What a shadow board comes to, before anything is put in the document.</summary>
public sealed record ShadowBoardPlan(
    Rect2 Board,
    PathGeometry BoardOutline,
    PathGeometry PinHoles,
    PathGeometry Pockets,
    PathGeometry Shadows,
    IReadOnlyList<LabelSpot> Labels,
    int Sheets,
    int Passes,
    double DepthMm,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The shadow board maker: tools laid on the bed (outlined by the camera, or drawn) become a drawer
/// insert with a pocket for each, the fit gap bigger all round, finger notches to lift them out and
/// their names engraved alongside. Either a stack of sheets (the top ones cut through, a solid base
/// with each tool's silhouette engraved on it) or one board with the pockets engraved down into it.
/// The tools stay where they were laid: the board is built round them.
/// </summary>
public static class ShadowBoardMaker
{
    public const string CutLayer = "Shadow board · cut", LabelLayer = "Shadow board · labels",
        ShadowLayer = "Shadow board · shadows", PocketLayer = "Shadow board · pockets";
    public const string TopPage = "Shadow board · top", MiddlePage = "Shadow board · middle", BasePage = "Shadow board · base", BoardPage = "Shadow board";

    const double Tol = 0.02;

    /// <summary>Sheets cut through for the pockets (the base sheet comes on top of these).</summary>
    public static int SheetsFor(ShadowBoardOptions o) =>
        Math.Max(1, (int)Math.Ceiling(o.DepthMm / Math.Max(0.1, o.SheetMm) - 1e-9));

    /// <summary>Engrave passes for the pockets (never short: rounded up).</summary>
    public static int PassesFor(ShadowBoardOptions o) =>
        Math.Max(1, (int)Math.Ceiling(o.DepthMm / Math.Max(0.001, o.EtchMmPerPass) - 1e-9));

    // ---- the tools ------------------------------------------------------------------------------

    /// <summary>
    /// Tools from a mask of what isn't the bed (one byte a pixel, over 127 = a tool): each separate
    /// blob's outline, holes filled in (a spanner's ring or scissor handles would be loose bits in a
    /// cut sheet), in reading order (back to front, left to right), named "Tool 1", "Tool 2", ….
    /// </summary>
    /// <param name="area">The bed area the picture covers (mm); pixel (0, 0) is its top-left.</param>
    public static List<ShadowTool> FromMask(byte[] keep, int width, int height, Rect2 area, double mmPerPx, double minAreaMm2 = 100, double smoothness = 3)
    {
        var mask = new bool[keep.Length];
        for (int i = 0; i < keep.Length; i++) mask[i] = keep[i] > 127;
        var traced = Doc.Tracing.Tracer.TraceMask(mask, width, height, new Doc.Tracing.TraceOptions
        {
            Smoothness = smoothness,
            MinAreaPx = (int)Math.Max(4, minAreaMm2 / (mmPerPx * mmPerPx) / 4),
        });
        var toBed = Affine.Scale(mmPerPx, mmPerPx).Then(Affine.Translate(area.MinX, area.MinY));
        var outlines = Outers(traced.Transform(toBed)).Where(g => ShapeGeometry.Area(g) >= minAreaMm2).ToList();
        return [.. ReadingOrder(outlines).Select((g, i) => new ShadowTool($"Tool {i + 1}", g))];
    }

    /// <summary>Tools from shapes on the canvas: each shape's outer outlines (holes filled in), named after the shape.</summary>
    public static List<ShadowTool> FromShapes(IEnumerable<PathShape> shapes)
    {
        var o = new List<(string Name, PathGeometry G)>();
        foreach (var shape in shapes)
        {
            var outers = Outers(shape.World);
            for (int i = 0; i < outers.Count; i++)
                o.Add((outers.Count == 1 ? shape.Name : $"{shape.Name} {i + 1}", outers[i]));
        }
        var order = ReadingOrder(o.Select(x => x.G).ToList());
        return [.. order.Select((g, i) => { var name = o.First(x => ReferenceEquals(x.G, g)).Name; return new ShadowTool(string.IsNullOrWhiteSpace(name) ? $"Tool {i + 1}" : name.Trim(), g); })];
    }

    /// <summary>The outer outlines of a shape, one geometry each: closed subpaths not inside another one.</summary>
    public static List<PathGeometry> Outers(PathGeometry g)
    {
        var merged = ShapeGeometry.Union([g], Tol);
        var subs = merged.Subpaths.Where(sp => sp.Closed && !sp.Segs.IsEmpty).ToList();
        var flat = subs.Select(sp => sp.Flatten(0.1)).ToList();
        var areas = flat.Select(f => Math.Abs(Geom.SignedArea(f))).ToList();
        var o = new List<PathGeometry>();
        for (int i = 0; i < subs.Count; i++)
        {
            bool inside = false;
            for (int j = 0; j < subs.Count && !inside; j++)
                inside = j != i && areas[j] > areas[i] && Geom.Inside(flat[j], flat[i][0]);
            if (!inside && areas[i] > 0.01) o.Add(new PathGeometry([subs[i]]));
        }
        return o;
    }

    static List<PathGeometry> ReadingOrder(List<PathGeometry> outlines)
    {
        if (outlines.Count == 0) return outlines;
        var b = outlines.Select(g => g.Bounds(0.1)).ToList();
        double row = Math.Max(5, b.Average(r => Math.Min(r.Width, r.Height)) / 2);
        return [.. outlines.Select((g, i) => (g, r: b[i])).OrderBy(x => Math.Round(x.r.Center.Y / row)).ThenBy(x => x.r.Center.X).Select(x => x.g)];
    }

    /// <summary>The middle of an outline's area and the direction its long side runs (degrees, from its area, so any shape works).</summary>
    public static (Vec2 Centre, double AngleDeg) Axis(PathGeometry outline)
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
        if (Math.Abs(a) < 1e-9) return (outline.Bounds().Center, 0);
        double cx = sx / (6 * a), cy = sy / (6 * a);
        double mxx = sxx / (12 * a) - cx * cx, myy = syy / (12 * a) - cy * cy, mxy = sxy / (24 * a) - cx * cy;
        return (new Vec2(cx, cy), 0.5 * Math.Atan2(2 * mxy, mxx - myy) * 180 / Math.PI);
    }

    // ---- the plan -------------------------------------------------------------------------------

    /// <summary>Works the board out: pockets, notches, labels, the board round them and anything to warn about.</summary>
    public static ShadowBoardPlan Plan(IReadOnlyList<ShadowTool> tools, ShadowBoardOptions o, Func<string, TextStyle, Rect2>? measure = null)
    {
        if (tools.Count == 0) throw new InvalidOperationException("There are no tools to make pockets for.");
        measure ??= Measure;
        var warnings = new List<string>();
        var pockets = new List<PathGeometry>();
        foreach (var t in tools)
        {
            var pocket = o.GapMm > 0 ? ShapeGeometry.Offset(t.Outline, o.GapMm, JoinStyle.Round, EndStyle.Flat, tolerance: Tol) : t.Outline;
            var notches = NotchesFor(pocket, Axis(t.Outline), o);
            pockets.Add(notches.Count == 0 ? pocket : ShapeGeometry.Union([pocket, .. notches], Tol));
        }
        var allPockets = ShapeGeometry.Union(pockets, Tol);
        if (Outers(allPockets).Count < tools.Count)
            warnings.Add("Some pockets run into each other: move those tools apart if you want a wall between them.");

        // Labels: under each pocket if there's room, else above, right or left.
        var style = new TextStyle(o.LabelFont, o.LabelSizeMm, Align: TextAlign.Center);
        var labels = new List<LabelSpot>();
        if (o.Labels)
        {
            var taken = new List<Rect2>();
            for (int i = 0; i < tools.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(tools[i].Name)) continue;
                var text = tools[i].Name.Trim();
                var size = measure(text, style);
                var pb = pockets[i].Bounds(0.1);
                double gap = Math.Max(1.5, o.LabelSizeMm * 0.4), w = size.Width, h = size.Height;
                Vec2[] spots =
                [
                    new(pb.Center.X, pb.MaxY + gap + h / 2), new(pb.Center.X, pb.MinY - gap - h / 2),
                    new(pb.MaxX + gap + w / 2, pb.Center.Y), new(pb.MinX - gap - w / 2, pb.Center.Y),
                ];
                LabelSpot? chosen = null;
                foreach (var c in spots)
                {
                    var box = new Rect2(c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2);
                    if (Clear(box.Inflate(gap * 0.6), allPockets, taken)) { chosen = new LabelSpot(text, c, box, true); break; }
                }
                if (chosen is null)
                {
                    var c = spots[0];
                    chosen = new LabelSpot(text, c, new Rect2(c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2), false);
                    warnings.Add($"\"{text}\" has no clear spot by its pocket: it may run into another pocket. Move the tools apart or shorten the name.");
                }
                labels.Add(chosen);
                taken.Add(chosen.Box);
            }
        }

        // The board: round everything, or the fixed size centred on it.
        var content = allPockets.Bounds(0.1);
        foreach (var l in labels) content = content.Union(l.Box);
        var auto = content.Inflate(Math.Max(0, o.MarginMm));
        Rect2 board = auto;
        if (o.BoardWidthMm > 0 && o.BoardHeightMm > 0)
        {
            var c = content.Center;
            board = new Rect2(c.X - o.BoardWidthMm / 2, c.Y - o.BoardHeightMm / 2, c.X + o.BoardWidthMm / 2, c.Y + o.BoardHeightMm / 2);
            if (!board.Contains(content.Inflate(Math.Min(3, o.MarginMm))))
                warnings.Add($"The tools need at least {auto.Width:0} × {auto.Height:0} mm with the margin; the board is {o.BoardWidthMm:0} × {o.BoardHeightMm:0} mm. Lay the tools closer together or make the board bigger.");
        }
        double r = Math.Clamp(o.CornerRadiusMm, 0, Math.Min(board.Width, board.Height) / 2);
        var outline = PathGeometry.RoundRect(board.MinX, board.MinY, board.Width, board.Height, r, r);

        // Dowel holes in the corners (stacked sheets only).
        var pins = PathGeometry.Empty;
        if (o.Style == ShadowBoardStyle.StackedSheets && o.PinDiameterMm > 0)
        {
            double pr = o.PinDiameterMm / 2;
            double inset = Math.Max(Math.Min(Math.Max(0, o.MarginMm) / 2, 12), pr + 2) + r * 0.3;
            Vec2[] at = [new(board.MinX + inset, board.MinY + inset), new(board.MaxX - inset, board.MinY + inset), new(board.MaxX - inset, board.MaxY - inset), new(board.MinX + inset, board.MaxY - inset)];
            pins = new PathGeometry(at.SelectMany(p => PathGeometry.Ellipse(p.X, p.Y, pr, pr).Subpaths));
            if (ShapeGeometry.Area(ShapeGeometry.Boolean(ShapeGeometry.Offset(pins, 1.5, JoinStyle.Round, EndStyle.Flat), allPockets, BoolOp.Intersect)) > 0.01)
                warnings.Add("A dowel hole in a corner runs into a pocket: give the board more margin or turn the dowel holes off.");
        }

        int sheets = SheetsFor(o), passes = PassesFor(o);
        double depth = o.Style == ShadowBoardStyle.StackedSheets ? sheets * o.SheetMm : passes * o.EtchMmPerPass;
        var shadows = o.Style == ShadowBoardStyle.StackedSheets && o.Shadow ? ShapeGeometry.Union(tools.Select(t => t.Outline), Tol) : PathGeometry.Empty;
        return new ShadowBoardPlan(board, outline, pins, allPockets, shadows, labels, sheets, passes, depth, warnings);
    }

    /// <summary>Finger notch circles for a pocket: on the line across the tool's middle, centred on the pocket's edge.</summary>
    static List<PathGeometry> NotchesFor(PathGeometry pocket, (Vec2 Centre, double AngleDeg) axis, ShadowBoardOptions o)
    {
        if (o.Notches == NotchPlacement.None || o.NotchDiameterMm <= 0) return [];
        double a = axis.AngleDeg * Math.PI / 180;
        var n = new Vec2(-Math.Sin(a), Math.Cos(a)); // across the tool
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        var c = axis.Centre;
        foreach (var f in pocket.Flatten(0.05))
        {
            var p = f.Points;
            int count = f.Closed ? p.Count : p.Count - 1;
            for (int i = 0; i < count; i++)
                if (Cross(c, n, p[i], p[(i + 1) % p.Count]) is { } s) { lo = Math.Min(lo, s); hi = Math.Max(hi, s); }
        }
        if (!double.IsFinite(lo) || !double.IsFinite(hi)) return [];
        double rr = o.NotchDiameterMm / 2;
        var front = c + n * hi; var back = c + n * lo;
        if (front.Y < back.Y) (front, back) = (back, front); // the front is towards the bed's front (larger Y)
        var o2 = new List<PathGeometry> { PathGeometry.Ellipse(front.X, front.Y, rr, rr) };
        if (o.Notches == NotchPlacement.BothSides) o2.Add(PathGeometry.Ellipse(back.X, back.Y, rr, rr));
        return o2;
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

    static bool Clear(Rect2 box, PathGeometry pockets, List<Rect2> taken)
    {
        if (taken.Any(t => t.Intersects(box))) return false;
        var rect = PathGeometry.Rect(box.MinX, box.MinY, box.Width, box.Height);
        return ShapeGeometry.Area(ShapeGeometry.Boolean(rect, pockets, BoolOp.Intersect)) < 0.01;
    }

    /// <summary>The box a label's words take (their outline's bounds, centred on 0, 0).</summary>
    public static Rect2 Measure(string text, TextStyle style)
    {
        var g = TextLayout.Build(text, style, null);
        var b = g.IsEmpty ? Rect2.Empty : g.Bounds(0.1);
        if (b.IsEmpty) { double w = text.Length * style.SizeMm * 0.6; return new Rect2(-w / 2, -style.SizeMm / 2, w / 2, style.SizeMm / 2); }
        return b;
    }

    // ---- into the document ----------------------------------------------------------------------

    /// <summary>
    /// The board on pages of its own after the current one (stacked: top, middle if there's more
    /// than one cut sheet, base; engraved: one page), all in the same place on the bed, so each sheet
    /// goes where the last one was. The top (or only) page is shown.
    /// </summary>
    public static DocState Make(DocState s, IReadOnlyList<ShadowTool> tools, ShadowBoardOptions o, Func<string, TextStyle, Rect2>? measure = null)
    {
        var plan = Plan(tools, o, measure);
        var style = new TextStyle(o.LabelFont, o.LabelSizeMm, Align: TextAlign.Center);
        var boardCut = new PathGeometry(o.CutOutline ? [.. plan.PinHoles.Subpaths, .. plan.BoardOutline.Subpaths] : plan.PinHoles.Subpaths);
        int first;
        if (o.Style == ShadowBoardStyle.StackedSheets)
        {
            s = Pages.Add(s, TopPage);
            first = s.PageIndex;
            s = Sheet(s, o, boardCut, plan.Pockets);
            s = AddLabels(s, o, plan, style);
            if (plan.Sheets > 1)
            {
                s = Pages.Add(s, $"{MiddlePage} (cut {plan.Sheets - 1})");
                s = Sheet(s, o, boardCut, plan.Pockets);
            }
            s = Pages.Add(s, BasePage);
            s = Sheet(s, o, boardCut, PathGeometry.Empty);
            if (!plan.Shadows.IsEmpty)
            {
                var (s2, layer) = NewLayer(s, o.EngraveSettings, ShadowLayer, LayerMode.Fill, 0x5B6270);
                s = Ops.AddShapes(s2, [new PathShape { Name = "Tool shadows", LayerId = layer.Id, Geometry = plan.Shadows }]);
            }
        }
        else
        {
            s = Pages.Add(s, BoardPage);
            first = s.PageIndex;
            var (s2, pocketLayer) = NewLayer(s, o.PocketSettings, PocketLayer, LayerMode.Fill, 0x3A86FF);
            s = Ops.UpdateLayers(s2, new HashSet<Guid> { pocketLayer.Id }, l => l with
            {
                Laser = l.Laser.WithPasses(plan.Passes),
                AngleStepDeg = l.AngleStepDeg != 0 ? l.AngleStepDeg : 37,
            });
            s = Ops.AddShapes(s, [new PathShape { Name = "Tool pockets", LayerId = pocketLayer.Id, Geometry = plan.Pockets }]);
            s = AddLabels(s, o, plan, style);
            if (!boardCut.IsEmpty)
            {
                var (s3, cut) = NewLayer(s, o.CutSettings, CutLayer, LayerMode.Cut, 0xE63946);
                s = Ops.AddShapes(s3, [new PathShape { Name = "Board", LayerId = cut.Id, Geometry = boardCut }]);
            }
        }
        return Pages.SwitchTo(s, first);
    }

    static DocState Sheet(DocState s, ShadowBoardOptions o, PathGeometry boardCut, PathGeometry pockets)
    {
        var (s2, layer) = NewLayer(s, o.CutSettings, CutLayer, LayerMode.Cut, 0xE63946);
        var shapes = new List<Shape>();
        if (!pockets.IsEmpty) shapes.Add(new PathShape { Name = "Tool pockets", LayerId = layer.Id, Geometry = pockets });
        if (!boardCut.IsEmpty) shapes.Add(new PathShape { Name = "Board", LayerId = layer.Id, Geometry = boardCut });
        return shapes.Count == 0 ? s2 : Ops.AddShapes(s2, shapes);
    }

    static DocState AddLabels(DocState s, ShadowBoardOptions o, ShadowBoardPlan plan, TextStyle style)
    {
        if (plan.Labels.Count == 0) return s;
        var (s2, layer) = NewLayer(s, o.EngraveSettings, LabelLayer, LayerMode.Fill, 0x2A9D8F);
        var texts = new List<Shape>();
        foreach (var l in plan.Labels)
        {
            var at = TextShape.Create(l.Text, style, layer.Id, Affine.Identity);
            var b = at.Geometry.IsEmpty ? new Rect2(0, 0, 0, 0) : at.Geometry.Bounds(0.1);
            texts.Add(TextShape.Create(l.Text, style, layer.Id, Affine.Translate(l.Centre - b.Center), name: $"Label · {l.Text}"));
        }
        return Ops.AddShapes(s2, texts);
    }

    /// <summary>A layer for the board on the page shown: the given settings (or the defaults) under the board's name.</summary>
    static (DocState, Layer) NewLayer(DocState s, Layer? settings, string name, LayerMode mode, uint colour)
    {
        if (s.Layers.FirstOrDefault(l => l.Name == name) is { } have) return (s, have);
        var layer = (settings ?? Layer.Default(s.Layers.Count, mode)) with { Id = Guid.NewGuid(), Name = name, Mode = mode, ParentId = null, Color = colour, Output = true };
        return (Ops.AddLayer(s, layer), layer);
    }
}
