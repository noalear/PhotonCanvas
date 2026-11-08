using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

public sealed record JobOptions
{
    /// <summary>
    /// True: each pass runs over the whole job (with <see cref="PassDelayS"/> between).
    /// False: stock behaviour, each path repeats its passes before the next starts.
    /// </summary>
    public bool WholeJobPasses { get; init; } = true;
    /// <summary>How far the MOPA lands from the blue laser (mm, from Machine → Align lasers): MOPA work is moved back by it.</summary>
    public Vec2 MopaOffset { get; init; }
    /// <summary>Exhaust fan gear (1–4) and purifier speed (%) for the job: see <see cref="Job.FanGear"/>.</summary>
    public int FanGear { get; init; } = 3;
    public int PurifierSpeed { get; init; } = 50;
    /// <summary>Wait this long after each cut or score path, every pass of it, laser off (s): heat leaves between paths (thin metal, masks). 0 = none.</summary>
    public double PathPauseS { get; init; }
    /// <summary>Pause between whole-job passes, in seconds.</summary>
    public double PassDelayS { get; init; }
    /// <summary>Maximum deviation of the cut path from the true curve, in mm.</summary>
    public double Tolerance { get; init; } = 0.01;
    public MachineProfile Machine { get; init; } = MachineProfile.F2Ultra;
    /// <summary>Spread-the-heat planning: how far through it is (0–1), now and then, and a way to stop it when it's no longer wanted.</summary>
    public Action<double>? HeatProgress { get; init; }
    public CancellationToken Cancel { get; init; }
    /// <summary>The lasers' full power (W), for the heat order: the F2 Ultra's 40 W blue diode and 60 W MOPA.</summary>
    public double BlueWatts { get; init; } = 40;
    public double RedWatts { get; init; } = 60;
    /// <summary>Flat bed, rotary or conveyor (for the rotary, the origin is set from the design).</summary>
    public WorkMode Mode { get; init; } = new WorkMode.Flat();
    /// <summary>Mark only this section of a design bigger than the work area, moved into it.</summary>
    public Section? Section { get; init; }
    /// <summary>Mark only this layer (and its sub-layers): one layer at a time, e.g. one screen per colour.</summary>
    public Guid? OnlyLayer { get; init; }
    /// <summary>Spread the heat across layers and objects; null = as the design says (<see cref="DocState.Spread"/>).</summary>
    public SpreadHeat? Spread { get; init; }
    /// <summary>
    /// How far the focus may move either way (mm); null = the work mode's own (<see cref="Job.FocusLimitMm"/>). Set for a
    /// part held up in the rotary and cut on the flat bed (profile cuts), where the cut goes down through the part.
    /// At most <see cref="Job.MaxRotaryFocusOffsetMm"/>.
    /// </summary>
    public double? FocusLimitMm { get; init; }
}

/// <summary>One section of a design bigger than the work area.</summary>
/// <param name="Clip">The part of the design (design mm).</param>
/// <param name="Shift">How far it is moved to land in the work area.</param>
/// <param name="MaterialMove">How far to move the material from where it was for the first section (mm; + = right/down on the work area).</param>
public sealed record Section(int Index, int Count, int Row, int Col, Rect2 Clip, Vec2 Shift, Vec2 MaterialMove)
{
    public string Label => $"section {Index + 1} of {Count}";

    /// <summary>
    /// The sections for a design: a grid of rectangles no bigger than <paramref name="width"/> ×
    /// <paramref name="height"/> mm covering it, row by row, each placed at the same spot in the
    /// work area (so the material is moved between them, never the design).
    /// </summary>
    public static List<Section> Plan(Rect2 design, double width, double height, MachineProfile m)
    {
        var o = new List<Section>();
        if (design.IsEmpty) return o;
        width = Math.Clamp(width, 5, m.FieldW);
        height = Math.Clamp(height, 5, m.FieldH);
        int cols = Math.Max(1, (int)Math.Ceiling(design.Width / width - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(design.Height / height - 1e-9));
        // Where each section lands: the middle of the work area, the same for all.
        var at = new Vec2((m.FieldW - width) / 2, (m.FieldH - height) / 2);
        int n = rows * cols;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var clip = new Rect2(design.MinX + c * width, design.MinY + r * height, design.MinX + (c + 1) * width, design.MinY + (r + 1) * height);
                o.Add(new Section(r * cols + c, n, r, c, clip, at - clip.TopLeft, new Vec2(-c * width, -r * height)));
            }
        return o;
    }
}

/// <summary>A job ready to send, plus everything the UI shows about it.</summary>
public sealed record BuiltJob(
    Job Job,
    GcodeText MotionText,
    string Border,
    byte[] Xf,
    double EstimateS,
    Rect2 Bounds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<BuiltJob.Step> Steps)
{
    /// <summary>The motion G-code as text (small jobs: a huge one is kept only as <see cref="MotionText"/>).</summary>
    public string Motion => MotionText.ToString();

    /// <summary>One processed path, in order, for the preview.</summary>
    public sealed record Step(Guid LayerId, List<Vec2> Points, bool Closed, bool Fill)
    {
        /// <summary>Fills: the direction the lines are scanned in, degrees (0 = along X).</summary>
        public double ScanAngleDeg { get; init; }
    }

    /// <summary>A layer's share of the job, in run order: seconds for one pass, and how many passes.</summary>
    public sealed record LayerTime(Guid LayerId, double PassSeconds, int Passes)
    {
        public double TotalSeconds => PassSeconds * Passes;
    }

    /// <summary>Each output layer's time, in run order.</summary>
    public IReadOnlyList<LayerTime> LayerTimes { get; init; } = [];
    /// <summary>Shapes that are on an output layer but won't be done (on the wrong kind of layer), one line per layer.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];

    /// <summary>Whole-job rounds (passes run over the whole job), with the pause between them; null when each path runs its own passes.</summary>
    public double? RoundDelayS { get; init; }
    /// <summary>How many paths the job cuts, marks or fills. Steps can be more: a heat-ordered layer has a step for every piece of every pass.</summary>
    public int Paths { get; init; }

    /// <summary>
    /// One stretch of the run, in the order it happens: seconds from the start (by the same
    /// estimate as <see cref="EstimateS"/>), the steps it marks, and how long one pass of them
    /// takes (a stretch may repeat its passes). A pause between rounds marks nothing.
    /// </summary>
    public sealed record RunSpan(double Start, double End, int FirstStep, int StepCount, double PassSeconds)
    {
        public bool IsPause => StepCount == 0;
    }

    /// <summary>The whole run, in order (for showing where the laser is while it runs).</summary>
    public IReadOnlyList<RunSpan> Timeline { get; init; } = [];
}

/// <summary>What framing traces.</summary>
public enum FrameShape
{
    /// <summary>The rectangle around everything in the job.</summary>
    Rectangle,
    /// <summary>A tight outline around everything (convex hull): shows angled or round designs better.</summary>
    Outline,
}

public static class JobBuilder
{
    /// <summary>
    /// G-code for framing <paramref name="job"/>: the same border format as inside the job
    /// package (which is what xTool Studio frames with), around the rectangle or the outline.
    /// </summary>
    public static string FrameGcode(BuiltJob job, FrameShape shape, double powerPct = Job.FramePowerDefault, double speedMmS = Job.FrameSpeedDefault)
    {
        var b = job.Bounds;
        Vec2[] box = [b.TopLeft, new(b.MaxX, b.MinY), b.BottomRight, new(b.MinX, b.MaxY), b.TopLeft];
        if (shape == FrameShape.Rectangle) return job.Job.Border(box, powerPct, speedMmS);
        // Kept inside the job's box: one section of a bigger design only frames its own part.
        var hull = ConvexHull(job.Steps.SelectMany(st => st.Points).Select(q => new Vec2(Math.Clamp(q.X, b.MinX, b.MaxX), Math.Clamp(q.Y, b.MinY, b.MaxY))));
        if (hull.Count < 3) return job.Job.Border(box, powerPct, speedMmS);
        return job.Job.Border([.. hull, hull[0]], powerPct, speedMmS);
    }

    /// <summary>Convex hull (monotone chain), counter-clockwise, without repeating the first point.</summary>
    public static List<Vec2> ConvexHull(IEnumerable<Vec2> points)
    {
        var p = points.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        if (p.Count < 3) return p;
        static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new List<Vec2>(p.Count * 2);
        foreach (var v in p)
        {
            while (h.Count >= 2 && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1);
            h.Add(v);
        }
        int lower = h.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--)
        {
            while (h.Count >= lower && Cross(h[^2], h[^1], p[i]) <= 0) h.RemoveAt(h.Count - 1);
            h.Add(p[i]);
        }
        h.RemoveAt(h.Count - 1);
        return h;
    }

    /// <summary>
    /// Pairs of processed shapes that sit exactly on top of each other (same size and place),
    /// e.g. an original and an edited copy: both get processed, usually by mistake.
    /// </summary>
    public static IEnumerable<(Shape A, Shape B)> StackedShapes(DocState s)
    {
        var shapes = OutputLayers(s).SelectMany(l => s.ShapesOn(l.Id)).Where(sh => !sh.Bounds.IsEmpty).ToList();
        static (long, long, long, long) Key(Rect2 b) =>
            ((long)Math.Round(b.MinX * 20), (long)Math.Round(b.MinY * 20), (long)Math.Round(b.MaxX * 20), (long)Math.Round(b.MaxY * 20));
        foreach (var g in shapes.GroupBy(sh => Key(sh.Bounds)).Where(g => g.Count() > 1))
        {
            var list = g.ToList();
            for (int i = 1; i < list.Count; i++) yield return (list[0], list[i]);
        }
    }

    /// <summary>Layers that will be processed, in order.</summary>
    public static IEnumerable<Layer> OutputLayers(DocState s) => s.Layers.Where(l => l.Output && l.Visible && !MaskLayer.IsMask(s, l));

    /// <exception cref="JobException">The job cannot be made; the message says why.</exception>
    /// <summary>What the heat order needs from a layer: its sheet, and the laser as it will cut.</summary>
    public static HeatSettings HeatSettingsFor(Layer layer, JobOptions opt) => new()
    {
        Sheet = SheetMetal.Find(layer.HeatSheet),
        ThicknessMm = layer.HeatThicknessMm,
        LimitC = layer.HeatLimitC,
        PieceMm = layer.HeatPieceMm,
        Fine = layer.HeatFine,
        Watts = (layer.Laser.Light == LightSource.Red ? opt.RedWatts : opt.BlueWatts) * layer.Laser.Power / 100,
        SpeedMmS = layer.Laser.Speed,
        Passes = Math.Max(1, layer.Laser.Passes),
    };

    static string Hours(double s) => s >= 3600 ? $"{(int)(s / 3600)} h {(int)(s % 3600 / 60):00} min" : s >= 60 ? $"{s / 60:0} min" : $"{s:0} s";

    public static BuiltJob Build(DocState s, JobOptions opt, DateTimeOffset? now = null)
    {
        JobException.CheckRange("pause after each path", opt.PathPauseS, 0, Job.MaxPathPauseS, "s");
        var warnings = new List<string>();
        // Add-ons' steps (Extensions.JobHooks): a changed copy of the design, and their own checks.
        if (Extensions.JobHooks.All.Count > 0)
        {
            s = Extensions.JobHooks.Apply(s, opt, warnings);
            warnings.AddRange(Extensions.JobHooks.Checks(s, opt));
        }
        // The job bar's pause after each path: how many it was put after, and the time they add (heat-ordered layers have their own).
        int fixedPauses = 0;
        // Heat-ordered layers add a step per piece per pass: the extra ones beyond one per path.
        int heatExtraSteps = 0;
        double fixedPauseS = 0;
        // Wobble moves so far, every pass counted: checked before any are made, as a dense wobble on long cuts runs out of memory.
        long wobbleMoves = 0;
        var leftOut = new List<string>();
        var blocks = new List<Block>();
        var steps = new List<BuiltJob.Step>();
        // Which steps each block marks (blocks and steps are added together, a group at a time).
        var blockSteps = new List<(int First, int Count)>();
        void Link(int blockMark, int stepMark)
        {
            while (blockSteps.Count < blocks.Count) blockSteps.Add((stepMark, steps.Count - stepMark));
        }
        var pos = new Vec2(0, 0);
        var bounds = Rect2.Empty;
        var layers = OutputLayers(s).Where(l => opt.OnlyLayer is not { } only || l.Id == only || l.ParentId == only).ToList();
        // Only what's sent has to make sense: a broken code or field on a layer that's off doesn't stop the job.
        if (VariableText.Problems(s with { Shapes = [.. layers.SelectMany(s.ShapesFor).Distinct()] }).FirstOrDefault() is { } problem) throw new JobException(problem);
        var times = new List<BuiltJob.LayerTime>();

        foreach (var (a, b) in StackedShapes(s).Take(3))
            warnings.Add($"\"{Label(a)}\" and \"{Label(b)}\" are the same size and in the same place, so both will be processed. If one is an old copy, delete it (ungroup first if they're grouped).");

        foreach (var shiny in layers.Where(l => l.Reflective && s.ShapesFor(l).Any()).Take(1))
            warnings.Add($"\"{shiny.Name}\" is on highly reflective material and may throw reflections. Put it near the exhaust fan, or coat it with anti-reflective black marking spray.");

        foreach (var (mask, on, picture) in MaskLayer.LeftOnOutput(s, layers).Take(3))
            warnings.Add($"\"{Label(mask)}\" on layer \"{on.Name}\" is where the picture \"{Label(picture)}\" is masked, so it will be {(on.Mode == LayerMode.Cut ? "cut" : "marked")}. If it's only there as the mask, right-click it → Put on the mask layer (never burned).");

        foreach (var hidden in s.Layers.Where(l => l.Output && !l.Visible && s.ShapesFor(l).Any()))
            warnings.Add($"Layer \"{hidden.Name}\" is hidden, so it will not be processed.");

        // Cutting before engraving lets parts shift or drop before they are engraved.
        int firstCut = layers.FindIndex(l => l.Mode == LayerMode.Cut && s.ShapesFor(l).Any());
        if (firstCut >= 0)
            foreach (var later in layers.Skip(firstCut + 1).Where(l => l.Mode != LayerMode.Cut && s.ShapesFor(l).Any()))
                warnings.Add($"\"{later.Name}\" runs after the cut layer \"{layers[firstCut].Name}\". Parts may move or drop before it is processed; consider moving it above.");

        // Where each layer's blocks start, for the per-layer times.
        var starts = new List<(Layer Layer, int Start)>();
        foreach (var layer in layers)
        {
            var onLayer = s.ShapesFor(layer).ToList();
            if (onLayer.Count == 0) continue;
            starts.Add((layer, blocks.Count));
            try
            {
                layer.Laser.Validate(opt.Machine, layer.Reflective);
                ValidateExtras(layer, Job.FocusLimitFor(opt.Mode, opt.FocusLimitMm));
                if (layer.Mode == LayerMode.Image && layer.ImageMode == ImageEngraveMode.Slices && layer.Slices.CleanupEvery > 0)
                {
                    try { CleanupLaser(layer).Validate(opt.Machine, layer.Reflective); }
                    catch (JobException e) { throw new JobException($"cleanup pass: {e.Message}"); }
                }
            }
            catch (JobException e) { throw new JobException($"Layer \"{layer.Name}\": {e.Message}"); }

            var images = onLayer.OfType<ImageShape>().ToList();
            var shapes = onLayer.OfType<PathShape>().ToList();
            // Less power in places (set by an add-on's job hook): line fills and grey or dithered pictures only.
            var power = s.LayerPower.GetValueOrDefault(layer.Id);
            bool onOff = layer.Mode == LayerMode.Image && layer.ImageMode == ImageEngraveMode.Dither && layer.Dither is DitherMethod.Threshold or DitherMethod.Sketch;
            if (power is not null && (onOff || (layer.Mode == LayerMode.Image && layer.ImageMode == ImageEngraveMode.Slices) || (layer.Mode == LayerMode.Fill && layer.FillStyle == FillStyle.Offset) || layer.Mode is LayerMode.Cut or LayerMode.Score))
            {
                warnings.Add($"Layer \"{layer.Name}\": power that changes from place to place only works on line fills and on pictures in shades of grey or dithered, so this layer runs at its own power everywhere.");
                power = null;
            }
            if (layer.Mode == LayerMode.Image)
            {
                if (LeftOutOn(s, layer) is { } left) { warnings.Add(left); leftOut.Add(left); }
                if (layer.ImageMode == ImageEngraveMode.Slices)
                {
                    var sl = layer.Slices;
                    if (sl.DropMm > 0 && layer.Laser.Passes > 1)
                        warnings.Add($"Layer \"{layer.Name}\": the focus follows the slices down, but each of its {layer.Laser.Passes} passes starts again at the surface focus.");
                    foreach (var img in images)
                    {
                        var (masks, _) = DepthSliceMasks(img, layer);
                        int n = masks.Length;
                        double turn = layer.AngleStepDeg, start = layer.FillAngleDeg + layer.RunTurnDeg;
                        var clean = CleanupLaser(layer);
                        int run = 0; // slices burned so far (empty ones don't count)
                        for (int kk = 0; kk < n; kk++)
                        {
                            int k = sl.DeepestFirst ? n - 1 - kk : kk;
                            var m = masks[k];
                            var r = m.FilledBounds();
                            if (r.IsEmpty) continue;
                            double angle = start + k * turn;
                            // The focus follows what's been cut so far: slices run, not which slice (deepest first starts on the surface).
                            double focus = layer.FocusOffsetMm + sl.DropMm * (run / Math.Max(1, sl.DropEvery));
                            int bm = blocks.Count, sm = steps.Count;
                            // Repeats carry on turning after the last slice.
                            blocks.Add(new FillBlock([], layer.Laser, layer.LinesPerCm) { Mask = m, AngleDeg = angle, AngleStepDeg = turn * n, OverscanMm = layer.OverscanMm, FocusOffsetMm = focus, DotWidthMm = layer.DotWidthMm });
                            run++;
                            if (sl.CleanupEvery > 0 && run % sl.CleanupEvery == 0)
                                blocks.Add(new FillBlock([], clean, sl.CleanupLinesPerCm) { Mask = sl.CleanupWhole ? masks[0] : m, AngleDeg = angle + 90, FocusOffsetMm = focus });
                            steps.Add(new(layer.Id, [r.TopLeft, new(r.MaxX, r.MinY), r.BottomRight, new(r.MinX, r.MaxY)], true, true) { ScanAngleDeg = angle });
                            Link(bm, sm);
                            bounds = bounds.Union(r);
                        }
                    }
                    continue;
                }
                // Scan angle, crosshatch and turning per pass: flat bed only (the rotary and conveyor
                // step along one axis, a curved surface is banded in rows).
                bool straight = opt.Mode is not WorkMode.Flat || s.Curve.On;
                bool wantsTurn = layer.FillAngleDeg % 360 != 0 || layer.Crosshatch || (layer.AngleStepDeg != 0 && layer.Laser.Passes > 1);
                if (straight && wantsTurn && images.Count > 0)
                    warnings.Add($"Layer \"{layer.Name}\": scan angle, crosshatch and turning per pass only work on the flat bed, so its pictures are burned in straight rows.");
                double firstAngle = straight ? 0 : layer.FillAngleDeg + layer.RunTurnDeg;
                double[] angles = !straight && layer.Crosshatch ? [firstAngle, firstAngle + layer.CrossAngleDeg] : [firstAngle];
                bool perPass = !straight && layer.AngleStepDeg != 0 && layer.Laser.Passes > 1;
                DotBlock Finish(DotBlock d) => d with { FocusOffsetMm = layer.FocusOffsetMm, Spread = layer.SpreadLines ? Job.SpreadStride(25.4 / d.Dpi) : 0, Scatter = layer.SpreadScatter, Unidirectional = !layer.Bidirectional };
                foreach (var img in images)
                {
                    if (layer.PassThrough && OwnDpi(img) > layer.Dpi * 1.02)
                        warnings.Add($"\"{img.Name}\" has {img.Source.Width} pixels across {img.Bounds.Width:0.#} mm ({OwnDpi(img):0} dpi), more than layer \"{layer.Name}\"'s {layer.Dpi:0} dpi. One dot per pixel would put {OwnDpi(img) / layer.Dpi:0.#}× the heat in, so it's engraved at {layer.Dpi:0} dpi instead (pass-through can't keep its own pixels).");
                    foreach (double angle in angles)
                    {
                        var (dots, area) = Rasterize(img, layer, angle, power);
                        if (dots is null) continue;
                        var block = Finish(dots);
                        if (perPass)
                        {
                            // Each pass turned further: the picture laid out again at each pass's angle.
                            var per = new List<DotBlock> { block };
                            for (int p = 2; p <= Math.Min(layer.Laser.Passes, MaxTurnedPasses); p++)
                            {
                                var (more, moreArea) = Rasterize(img, layer, angle + (p - 1) * layer.AngleStepDeg, power);
                                per.Add(more is null ? block with { Bitmap = new Bitmap(1, 1), Levels = null } : Finish(more));
                                area = area.Union(moreArea);
                            }
                            block = block with { PassBlocks = per };
                        }
                        int bm = blocks.Count, sm = steps.Count;
                        blocks.Add(block);
                        steps.Add(new(layer.Id, [area.TopLeft, new(area.MaxX, area.MinY), area.BottomRight, new(area.MinX, area.MaxY)], true, true) { ScanAngleDeg = angle });
                        Link(bm, sm);
                        bounds = bounds.Union(area);
                    }
                }
                continue;
            }
            if (LeftOutOn(s, layer) is { } leftImages) { warnings.Add(leftImages); leftOut.Add(leftImages); }
            if (shapes.Count == 0) continue;

            if (layer.Mode == LayerMode.Fill && layer.FillStyle == FillStyle.Offset)
            {
                int open = 0;
                double spacing = 10 / layer.LinesPerCm;
                foreach (var shape in shapes)
                {
                    var closed = new PathGeometry(shape.World.Subpaths.Where(sp => sp.Closed).ToList());
                    open += shape.World.Subpaths.Count(sp => !sp.Closed);
                    if (closed.Subpaths.IsEmpty) continue;
                    var loops = OffsetLoops(closed, spacing, opt.Tolerance, ref pos);
                    if (loops.Count == 0) continue;
                    int bm = blocks.Count, sm = steps.Count;
                    blocks.Add(new LoopsBlock(loops, layer.Laser, spacing) { FocusOffsetMm = layer.FocusOffsetMm });
                    foreach (var p in closed.Flatten(opt.Tolerance).Where(f => f.Closed && f.Points.Count >= 3))
                    {
                        steps.Add(new(layer.Id, p.Points, true, true));
                        bounds = bounds.Union(Rect2.Bounds(p.Points));
                    }
                    Link(bm, sm);
                }
                if (open > 0)
                    warnings.Add($"Layer \"{layer.Name}\": {open} open path(s) can't be filled and were skipped. Close them, or put them on a Cut/Score layer.");
            }
            else if (layer.Mode == LayerMode.Fill)
            {
                int open = 0;
                // All the shapes in one scan: one combined "shape" of every outline.
                var groups = layer.FillTogether
                    ? [new PathShape { LayerId = layer.Id, Geometry = new PathGeometry(shapes.SelectMany(sh => sh.World.Subpaths)) }]
                    : shapes;
                foreach (var shape in groups)
                {
                    var polys = shape.World.Flatten(opt.Tolerance).Where(f => f.Closed && f.Points.Count >= 3).ToList();
                    open += shape.World.Subpaths.Count(sp => !sp.Closed);
                    if (polys.Count == 0) continue;
                    var block = new FillBlock(polys.Select(p => (IReadOnlyList<Vec2>)p.Points).ToList(), layer.Laser, layer.LinesPerCm)
                    {
                        AngleDeg = layer.FillAngleDeg + layer.RunTurnDeg,
                        AngleStepDeg = layer.AngleStepDeg,
                        OverscanMm = layer.OverscanMm,
                        FocusOffsetMm = layer.FocusOffsetMm,
                        DotWidthMm = layer.DotWidthMm,
                        Unidirectional = !layer.Bidirectional,
                        PowerScale = power,
                    };
                    block = block with
                    {
                        LineDwellMs = layer.LineDwellMs,
                        ReverseOrder = layer.ScanReverse,
                        Spread = layer.SpreadLines ? Job.SpreadStride(10 / layer.LinesPerCm) : 0,
                        Scatter = layer.SpreadScatter,
                    };
                    var cross = layer.Crosshatch ? block with { AngleDeg = block.AngleDeg + layer.CrossAngleDeg } : null;
                    bool first = Job.FillLines(block).Count > 0, second = cross is not null && Job.FillLines(cross).Count > 0;
                    if (!first && !second) continue;
                    int bm = blocks.Count, sm = steps.Count;
                    var main = new List<Block>();
                    if (first) main.Add(block);
                    if (second) main.Add(cross!);
                    if (layer.OutlineAfter)
                        // The outline traced after the fill, at the same settings: clean edges.
                        foreach (var p in polys) main.Add(new VectorBlock(Polyline.Loop(p.Points), layer.Laser) { FocusOffsetMm = layer.FocusOffsetMm });
                    if (layer.StepDownMm > 0 || layer.StepDownFirstMm > 0)
                    {
                        // Carving deeper: each pass is its own copy, the focus lowered a little more each time.
                        int passes = Math.Max(1, layer.Laser.Passes);
                        string deep = $"Layer \"{layer.Name}\" lowers the head by up to {layer.StepDownFirstMm + (passes - 1) * layer.StepDownMm:0.##} mm while engraving (step-down). Make sure the material is at least that thick.";
                        if (!warnings.Contains(deep))
                            warnings.Add(deep);
                        for (int pass = 1; pass <= passes; pass++)
                        {
                            double drop = layer.StepDownFirstMm + (pass - 1) * layer.StepDownMm;
                            foreach (var mb in main)
                                blocks.Add(mb switch
                                {
                                    FillBlock fb => fb.ForPass(pass) with { Laser = fb.Laser with { Passes = 1 }, FocusOffsetMm = layer.FocusOffsetMm + drop },
                                    _ => mb with { Laser = mb.Laser with { Passes = 1 }, FocusOffsetMm = layer.FocusOffsetMm + drop },
                                });
                        }
                    }
                    else blocks.AddRange(main);
                    foreach (var p in polys)
                    {
                        steps.Add(new(layer.Id, p.Points, true, true) { ScanAngleDeg = block.AngleDeg });
                        bounds = bounds.Union(Rect2.Bounds(p.Points));
                    }
                    Link(bm, sm);
                }
                if (open > 0)
                    warnings.Add($"Layer \"{layer.Name}\": {open} open path(s) can't be filled and were skipped. Close them, or put them on a Cut/Score layer.");
            }
            else
            {
                var paths = layer.KerfMm != 0
                    ? ApplyKerf(shapes, layer, opt.Tolerance, warnings)
                    : shapes.SelectMany(sh => sh.World.Flatten(opt.Tolerance)).Where(f => f.Points.Count >= 2).ToList();
                if (layer.RemoveOverlaps && layer.KerfMm != 0)
                    warnings.Add($"Layer \"{layer.Name}\": Cut shared edges once has nothing to do with a kerf set: the kerf moves each part's outline out by half of it, so shapes side by side no longer share an edge.");
                else if (layer.RemoveOverlaps)
                {
                    // Shared edges cut once: the doubled stretches come out as open lines.
                    var geoms = paths.Select(f => PathGeometry.Polyline(f.Points, f.Closed)).ToList();
                    var (cleaned, removedMm) = EditTools.RemoveOverlaps(geoms);
                    if (removedMm > 0.01)
                    {
                        paths = [.. cleaned.SelectMany(g => g.Flatten(opt.Tolerance)).Where(f => f.Points.Count >= 2)];
                        warnings.Add($"Layer \"{layer.Name}\": {removedMm:0.#} mm of lines lying on top of each other will be cut only once.");
                    }
                }
                var extras = new CutExtras(layer) { ManualTabs = [.. shapes.SelectMany(sh => sh.TabsWorld)] };
                List<Vec2> startPoints = [.. shapes.Select(sh => sh.StartWorld).OfType<Vec2>()];
                StepDown? drop = layer.StepDownMm > 0 || layer.StepDownFirstMm > 0 ? new StepDown(layer.StepDownFirstMm, layer.StepDownMm) : null;
                // The beam tracing small circles as it goes: a matter of motion, so either laser. Dots and dashes don't wobble.
                bool wobble = layer.Wobble && layer.DotModeMm <= 0 && layer.PerforateMm <= 0;
                if (wobble)
                {
                    if (layer.WobbleDiameterMm is < Wobble.MinDiameterMm or > Wobble.MaxDiameterMm || layer.WobbleSpacingMm is < Wobble.MinSpacingMm or > Wobble.MaxSpacingMm)
                        throw new JobException($"Layer \"{layer.Name}\": wobble diameter must be {Wobble.MinDiameterMm}–{Wobble.MaxDiameterMm} mm and spacing {Wobble.MinSpacingMm}–{Wobble.MaxSpacingMm} mm.");
                    // Passes count only when written out: per path, with no step-down or pause, the machine's own loop repeats one copy.
                    bool looped = !opt.WholeJobPasses && drop is null && opt.PathPauseS <= 0 && layer.Order != PathOrder.Heat;
                    int passes = looped ? 1 : Math.Max(1, layer.Laser.Passes);
                    double lengthMm = paths.Sum(f => (f.Closed ? Polyline.Loop(f.Points) : Polyline.Open(f.Points)).Length);
                    long moves = Wobble.MoveCount(lengthMm, layer.WobbleSpacingMm) * passes;
                    if (wobbleMoves + moves > Wobble.MaxMovesPerJob)
                    {
                        long room = Wobble.MaxMovesPerJob - wobbleMoves;
                        // The spacing that would fit, rounded up to the box's 0.005 mm steps.
                        double fits = room > 0 ? Math.Ceiling(lengthMm * passes * Wobble.PointsPerLoop / room / 0.005) * 0.005 : double.PositiveInfinity;
                        throw new JobException($"Layer \"{layer.Name}\": wobble would make {moves / 1e6:0.#} million moves ({lengthMm:N0} mm of path{(passes > 1 ? $" × {passes} passes" : "")}, " +
                                               $"{Wobble.PointsPerLoop / layer.WobbleSpacingMm:N0} per mm at {layer.WobbleSpacingMm:0.###} mm spacing)" +
                                               (wobbleMoves > 0 ? $" on top of {wobbleMoves / 1e6:0.#} million from layers before it" : "") + $", more than one job can hold ({Wobble.MaxMovesPerJob / 1e6:0} million). " +
                                               (fits <= Wobble.MaxSpacingMm ? $"Set the wobble spacing to at least {fits:0.###} mm, or wobble fewer paths." : "Wobble fewer paths, or turn wobble off on other layers."));
                    }
                    wobbleMoves += moves;
                }
                if (drop is not null)
                    warnings.Add($"Layer \"{layer.Name}\" lowers the head by up to {drop.DepthFor(Math.Max(1, layer.Laser.Passes)):0.##} mm while cutting (step-down). Make sure the material is at least that thick.");
                bool heat = layer.Order == PathOrder.Heat && layer.DotModeMm <= 0 && layer.PerforateMm <= 0;
                if (layer.Order == PathOrder.Heat && !heat)
                    warnings.Add($"Layer \"{layer.Name}\": dot mode and perforation keep the usual order; spreading the heat is for whole cuts.");
                if (heat)
                {
                    // Every pass of every path (long ones in pieces) placed where the sheet is coolest.
                    var groups = new List<(FlatPath Outline, IReadOnlyList<FlatPath> Pieces)>();
                    var gaps = new List<List<Vec2>>();
                    foreach (var (path, depth) in Order(paths, ref pos, PathOrder.Optimised, layer.StartNearest, startPoints))
                    {
                        groups.Add((path, extras.Pieces(path, depth).ToList()));
                        gaps.AddRange(extras.TakeTabGaps());
                    }
                    var settings = HeatSettingsFor(layer, opt);
                    var plan = HeatOrder.Plan(groups, settings, opt.HeatProgress, opt.Cancel);
                    int heatFirstStep = steps.Count;
                    foreach (var cut in plan.Cuts)
                    {
                        var pl = cut.Closed ? Polyline.Loop(cut.Points) : Polyline.Open(cut.Points);
                        if (!pl.HasExtent) continue;
                        if (wobble) pl = Polyline.Open(Wobble.Expand(pl.EmittedPoints(), layer.WobbleDiameterMm, layer.WobbleSpacingMm));
                        // A wait the model asked for comes after the cut before.
                        if (cut.WaitBeforeS > 0 && blocks.Count > 0) blocks[^1] = blocks[^1] with { PauseAfterS = blocks[^1].PauseAfterS + cut.WaitBeforeS };
                        int bm = blocks.Count, sm = steps.Count;
                        blocks.Add(new VectorBlock(pl, layer.Laser with { Passes = 1 }) { FocusOffsetMm = layer.FocusOffsetMm });
                        steps.Add(new(layer.Id, cut.Points, cut.Closed, false));
                        Link(bm, sm);
                        // A wobble burns half its width either side of the line: the frame shows that too.
                        bounds = bounds.Union(Rect2.Bounds(cut.Points).Inflate(wobble ? layer.WobbleDiameterMm / 2 : 0));
                    }
                    heatExtraSteps += Math.Max(0, steps.Count - heatFirstStep - groups.Count);
                    foreach (var gap in gaps)
                    {
                        int bm = blocks.Count, sm = steps.Count;
                        blocks.Add(new VectorBlock(Polyline.Open(gap), layer.Laser with { Power = Math.Min(layer.Laser.Power, layer.TabPowerPct) }) { FocusOffsetMm = layer.FocusOffsetMm });
                        steps.Add(new(layer.Id, gap, false, false));
                        Link(bm, sm);
                    }
                    int split = plan.Cuts.Count / Math.Max(1, settings.Passes);
                    warnings.Add($"Layer \"{layer.Name}\" spreads the heat: {groups.Count:N0} path{(groups.Count == 1 ? "" : "s")}" +
                                 (split > groups.Count ? $" in {split:N0} pieces" : "") + $", {settings.Passes} pass{(settings.Passes == 1 ? "" : "es")} each, cut where the sheet is coolest. " +
                                 $"The {settings.Sheet.Name.ToLowerInvariant()} stays under about +{plan.PeakC:0} °C (estimated, on a {plan.CellMm:0.##} mm grid)" +
                                 (plan.WaitS > 0.05 ? $", with {Hours(plan.WaitS)} of waits to let it cool." : ", with no waits needed."));
                    if (plan.OneCutC > settings.LimitC)
                        warnings.Add($"Layer \"{layer.Name}\": one cut alone warms the sheet about +{plan.OneCutC:0} °C, more than the +{settings.LimitC:0} °C allowed, so the job waits until each spot is nearly cold. Lower the power, go faster, cut shorter pieces or allow more warming.");
                    if (drop is not null)
                        warnings.Add($"Layer \"{layer.Name}\": step-down isn't used when spreading the heat (each pass is cut on its own).");
                    if (opt.PathPauseS > 0 || opt.WholeJobPasses && opt.PassDelayS > 0 && layer.Laser.Passes > 1)
                        warnings.Add($"Layer \"{layer.Name}\" works out its own waits from the heat, so the job bar's pauses (after each path, between rounds) don't apply to it.");
                }
                else foreach (var (path, depth) in Order(paths, ref pos, layer.Order, layer.StartNearest, startPoints))
                {
                    int pathFirst = blocks.Count;
                    foreach (var p in extras.Pieces(path, depth))
                    {
                        var pl = p.Closed ? Polyline.Loop(p.Points) : Polyline.Open(p.Points);
                        if (!pl.HasExtent) continue;
                        if (layer.DotModeMm > 0)
                        {
                            // Dot mode: a pulse every so often along the path, instead of a line.
                            var dots = DotsAlong(pl.EmittedPoints(), layer.DotModeMm);
                            int dbm = blocks.Count, dsm = steps.Count;
                            blocks.Add(new PointsBlock(dots, layer.Laser, layer.DotUs) { FocusOffsetMm = layer.FocusOffsetMm });
                            steps.Add(new(layer.Id, p.Points, p.Closed, false));
                            Link(dbm, dsm);
                            bounds = bounds.Union(Rect2.Bounds(p.Points));
                            continue;
                        }
                        if (layer.PerforateMm > 0)
                        {
                            // Perforation: dashes with gaps.
                            int pbm = blocks.Count, psm = steps.Count;
                            foreach (var dash in Dashes(pl.EmittedPoints(), layer.PerforateMm, layer.PerforateGapMm))
                                if (dash.Count >= 2) blocks.Add(new VectorBlock(Polyline.Open(dash), layer.Laser) { Drop = drop, FocusOffsetMm = layer.FocusOffsetMm });
                            if (blocks.Count == pbm) continue;
                            steps.Add(new(layer.Id, p.Points, p.Closed, false));
                            Link(pbm, psm);
                            bounds = bounds.Union(Rect2.Bounds(p.Points));
                            continue;
                        }
                        if (wobble) pl = Polyline.Open(Wobble.Expand(pl.EmittedPoints(), layer.WobbleDiameterMm, layer.WobbleSpacingMm));
                        int bm = blocks.Count, sm = steps.Count;
                        blocks.Add(new VectorBlock(pl, layer.Laser) { Drop = drop, FocusOffsetMm = layer.FocusOffsetMm });
                        // Tabs cut at low power, right after their outline's pieces.
                        foreach (var gap in extras.TakeTabGaps())
                            blocks.Add(new VectorBlock(Polyline.Open(gap), layer.Laser with { Power = Math.Min(layer.Laser.Power, layer.TabPowerPct) }) { FocusOffsetMm = layer.FocusOffsetMm });
                        steps.Add(new(layer.Id, p.Points, p.Closed, false));
                        Link(bm, sm);
                        bounds = bounds.Union(Rect2.Bounds(p.Points).Inflate(wobble ? layer.WobbleDiameterMm / 2 : 0));
                    }
                    // The pause comes once the whole path is done (its tabs and pieces too).
                    if (opt.PathPauseS > 0 && blocks.Count > pathFirst)
                    {
                        blocks[^1] = blocks[^1] with { PauseAfterS = opt.PathPauseS };
                        fixedPauses++;
                        fixedPauseS += opt.PathPauseS * Math.Max(1, layer.Laser.Passes);
                    }
                }
                extras.Warn(warnings);
            }
        }

        // Which layer each block came from: the per-layer times follow the blocks through reordering and sections.
        var blockLayer = new List<Layer>(blocks.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1].Start : blocks.Count;
            for (int k = starts[i].Start; k < end; k++) blockLayer.Add(starts[i].Layer);
        }
        if ((opt.Spread ?? s.Spread) is { On: true } spread && blocks.Count > 0)
            SpreadTheHeat(spread, opt, blocks, steps, blockSteps, blockLayer, warnings);

        if (opt.Section is { } sec)
        {
            // One section of a bigger design: every block cut to it and moved into the work area.
            var cutBlocks = new List<Block>();
            var cutSteps = new List<(int First, int Count)>();
            var cutLayers = new List<Layer>();
            for (int i = 0; i < blocks.Count; i++)
                foreach (var piece in Sections.Cut(blocks[i], sec.Clip, sec.Shift))
                {
                    cutBlocks.Add(piece);
                    cutSteps.Add(blockSteps[i]);
                    cutLayers.Add(blockLayer[i]);
                }
            blocks = cutBlocks;
            blockSteps = cutSteps;
            blockLayer = cutLayers;
            for (int i = 0; i < steps.Count; i++)
                steps[i] = steps[i] with { Points = [.. steps[i].Points.Select(p => p + sec.Shift)] };
            var b0 = new Rect2(Math.Max(bounds.MinX, sec.Clip.MinX), Math.Max(bounds.MinY, sec.Clip.MinY), Math.Min(bounds.MaxX, sec.Clip.MaxX), Math.Min(bounds.MaxY, sec.Clip.MaxY));
            bounds = b0.MaxX >= b0.MinX && b0.MaxY >= b0.MinY ? new Rect2(b0.MinX + sec.Shift.X, b0.MinY + sec.Shift.Y, b0.MaxX + sec.Shift.X, b0.MaxY + sec.Shift.Y) : Rect2.Empty;
            if (blocks.Count == 0) throw new JobException($"There's nothing to mark in {sec.Label}.");
        }

        // Each run of one layer's blocks, in run order (a layer comes once, unless the heat was spread across layers).
        for (int start = 0, end; start < blocks.Count; start = end)
        {
            for (end = start + 1; end < blocks.Count && blockLayer[end].Id == blockLayer[start].Id; end++) { }
            // Passes written out one block each (heat order, step-down) count once.
            times.Add(new(blockLayer[start].Id, blocks.Skip(start).Take(end - start).Sum(Job.BlockSeconds), Math.Max(1, blocks.Skip(start).Take(end - start).Max(b => b.Laser.Passes))));
        }

        if (blocks.Count == 0)
            throw new JobException(leftOut.Count > 0
                ? $"Nothing would be done. {string.Join(" ", leftOut)}"
                : s.Shapes.Count == 0
                ? "There is nothing on the canvas to process."
                : "No shapes are on an enabled, visible layer.");

        var mode = opt.Mode switch
        {
            // Round the object from the middle of the design: what faces the laser at the start is the design's middle row.
            // (A rotary start line on the page says what faces it instead.)
            WorkMode.Rotary r => r with { OriginY = s.Guides.FirstOrDefault(g => g.RotaryStart && !g.Vertical)?.At ?? (bounds.MinY + bounds.MaxY) / 2 },
            _ => opt.Mode,
        };
        if (mode is WorkMode.Rotary rot && blocks.Any(b => b is not DotBlock && b.Laser.FValue > Job.RotaryFeedCap(rot)))
            warnings.Add($"On the rotary, moves that turn it are held to half a turn a second ({Job.RotaryFeedCap(rot) / 60.0:0.#} mm/s round this object), so faster layers run at that speed.");
        if (mode is WorkMode.Conveyor)
        {
            // Lines run along the galvo's Y and step along the belt, so the belt doesn't shuttle back and forth.
            if (blocks.OfType<FillBlock>().Any(f => FoldTo180(f.AngleDeg) != 90 || f.AngleStepDeg != 0))
                warnings.Add("On the conveyor, engraving lines always run across the belt (90°), so scan angles and turning per pass are left out.");
            blocks = [.. blocks.Select(b => b is FillBlock f ? f with { AngleDeg = 90, AngleStepDeg = 0 } : b)];
        }
        if (mode is WorkMode.Rotary or WorkMode.Conveyor)
            warnings.Add(mode is WorkMode.Rotary
                ? "Rotary attachment: experimental. The commands come from xTool Studio's own templates but haven't been run on your machine yet. Try a small test on scrap first."
                : "Conveyor: experimental. The commands come from xTool Studio's own templates but haven't been run on your machine yet. Try a small test on scrap first.");
        // A curved piece: the head steps through height bands that follow it.
        Surface? surface = null;
        if (s.Curve.On && s.Curve.Build() is null)
            warnings.Add("Curved surface is on, but it needs at least 3 measured heights (or a typed shape): marked as flat.");
        if (s.Curve.On && s.Curve.Build() is { } built)
        {
            if (mode is not WorkMode.Flat)
                warnings.Add("Curved surface is on, but it only works on the flat bed: it's left out with the rotary or conveyor.");
            else
            {
                surface = built;
                warnings.Add($"Curved surface: the head follows it in {s.Curve.BandMm:0.##} mm steps. Experimental: try it on scrap first.");
                if (!s.Curve.Typed && !s.Curve.Area.IsEmpty && !s.Curve.Area.Inflate(1).Contains(bounds.TopLeft) | !s.Curve.Area.Inflate(1).Contains(bounds.BottomRight))
                    warnings.Add("Parts of the design are outside the measured area: the heights there are guessed. Measure a bigger area to be sure.");
                if (blocks.OfType<VectorBlock>().Any(v => v.Drop is not null))
                    warnings.Add("Step-down on Cut and Score layers is left out on a curved surface (the head's height follows the surface instead).");
                // Each band's fill lines are marked as they are: no pause before a line, no run-ups, the same angle every pass.
                if (blocks.OfType<FillBlock>().Any(f => f.LineDwellMs > 0 || f.OverscanMm > 0 || (f.AngleStepDeg != 0 && f.Laser.Passes > 1)))
                    warnings.Add("On a curved surface, fills leave out the pause before each line, overscan and turning per pass.");
                blocks = [.. blocks.Select(b => b is VectorBlock { Drop: not null } v ? v with { Drop = null } : b)];
            }
        }
        // The MOPA lined up with the blue laser: its work moved back by how far off it lands.
        if (opt.MopaOffset.Length > 1e-6)
        {
            var everywhere = new Rect2(-1e6, -1e6, 1e6, 1e6);
            blocks = [.. blocks.SelectMany(b => b.Laser.Light == LightSource.Red ? Sections.Cut(b, everywhere, -opt.MopaOffset) : [b])];
        }
        if (fixedPauses > 0)
        {
            // Spelled out: a drawing of thousands of pieces turns a short pause into hours.
            var paused = blocks.Where(b => b.PauseAfterS > 0).ToList();
            warnings.Add($"Pausing {opt.PathPauseS:0.##} s after each of {fixedPauses:N0} paths adds about {Hours(fixedPauseS)}." +
                         (paused.Count(b => b is VectorBlock v && v.Path.Length < 0.5) * 2 > paused.Count && fixedPauses > 1000 ? " Most are tiny pieces: if the drawing came in broken up, Outline → Tidy lines joins them into whole shapes first." : ""));
        }
        var job = new Job(opt.Machine, blocks)
        {
            PassMode = opt.WholeJobPasses ? new PassMode.Rounds(opt.PassDelayS) : new PassMode.PerBlock(),
            Created = (now ?? DateTimeOffset.Now).ToString("yyyy_MM_dd_HH_mm_ss"),
            Mode = mode,
            FocusLimitOverrideMm = opt.FocusLimitMm,
            FanGear = opt.FanGear,
            PurifierSpeed = opt.PurifierSpeed,
            SurfaceHeight = surface is null ? null : surface.HeightAt,
            BandMm = s.Curve.BandMm,
        };
        var motion = job.RenderText();
        Vec2[] frame = [bounds.TopLeft, new(bounds.MaxX, bounds.MinY), bounds.BottomRight, new(bounds.MinX, bounds.MaxY), bounds.TopLeft];
        var border = job.Border(frame);

        var lights = blocks.Select(b => b.Laser.Light).Distinct().ToList();
        var light = lights.Count > 1 ? "mixed" : lights[0].Wire();
        var prms = new JsonArray();
        foreach (var l in layers.Where(l => s.ShapesFor(l).Any()))
            prms.Add(new JsonObject
            {
                ["processingType"] = l.Mode switch
                {
                    LayerMode.Fill => "FILL_VECTOR_ENGRAVING",
                    LayerMode.Score => "VECTOR_ENGRAVING",
                    LayerMode.Image => "BITMAP_ENGRAVING",
                    _ => "VECTOR_CUTTING",
                },
                ["processingLightSource"] = l.Laser.Light.Wire(),
                ["power"] = l.Laser.Power,
                ["speed"] = l.Laser.Speed,
                ["repeat"] = l.Laser.Passes,
                ["isFill"] = l.Mode == LayerMode.Fill,
            });
        double estimate = job.EstimateSeconds();
        var xf = XfPackage.Build(new()
        {
            MotionText = motion,
            Border = border,
            PreviewJpeg = XfPackage.PreviewJpeg(steps.Select(st => (IReadOnlyList<Vec2>)(st.Closed ? [.. st.Points, st.Points[0]] : st.Points))),
            Light = light,
            Params = prms,
            Mode = mode switch { WorkMode.Rotary => "LASER_CYLINDER", WorkMode.Conveyor => "CONVEYOR_EXTENSION", _ => surface is not null ? "CURVE_PROCESS" : "LASER_PLANE" },
            Axis = mode is WorkMode.Flat ? "" : "U",
            Created = now ?? DateTimeOffset.Now,
            EstimatedSeconds = estimate,
        });
        return new BuiltJob(job, motion, border, xf, estimate, bounds, warnings, steps)
        {
            LayerTimes = times,
            LeftOut = leftOut,
            RoundDelayS = opt.WholeJobPasses ? opt.PassDelayS : null,
            Timeline = Timeline(job, blockSteps),
            Paths = steps.Count - heatExtraSteps,
        };
    }

    /// <summary>Engrave and picture layers with more separate parts than this are spread as one part.</summary>
    const int MaxSpreadPartsPerLayer = 300;
    /// <summary>A job with more parts than this is spread a layer at a time.</summary>
    const int MaxSpreadParts = 1500;

    /// <summary>
    /// Spread the heat across the job (<see cref="HeatSpread"/>): the blocks made, put in a new order
    /// part by part, with waits where the sheet needs them. A part is a layer's blocks; an engrave or
    /// picture layer is split into one part per separate shape (its blocks and their steps). Cut and
    /// score layers stay whole: their own order (inside first, heat order, travel) is kept. The steps,
    /// and which steps each block marks, follow the blocks. Each part's heat is counted with all its
    /// passes at once, which is on the safe side round by round, where they come a round apart.
    /// </summary>
    static void SpreadTheHeat(SpreadHeat h, JobOptions opt, List<Block> blocks, List<BuiltJob.Step> steps, List<(int First, int Count)> blockSteps, List<Layer> blockLayer, List<string> warnings)
    {
        JobException.CheckRange("cooldown after each part", h.CooldownS, 0, SpreadHeat.MaxCooldownS, "s");
        List<(int First, int Count)> Parts(bool split)
        {
            var parts = new List<(int First, int Count)>();
            for (int i = 0, end; i < blocks.Count; i = end)
            {
                var layer = blockLayer[i];
                for (end = i + 1; end < blocks.Count && blockLayer[end].Id == layer.Id; end++) { }
                var mine = new List<(int First, int Count)>();
                if (split && layer.Mode is LayerMode.Fill or LayerMode.Image)
                    for (int k = i; k < end; k++)
                    {
                        // A block marking the same steps as the one before (a fill and its outline, crosshatch, a slice's cleanup), or none, goes with it.
                        if (mine.Count > 0 && (blockSteps[k] == blockSteps[k - 1] || blockSteps[k].Count == 0)) mine[^1] = (mine[^1].First, mine[^1].Count + 1);
                        else mine.Add((k, 1));
                    }
                if (mine.Count is 0 or > MaxSpreadPartsPerLayer) mine = [(i, end - i)];
                parts.AddRange(mine);
            }
            return parts;
        }
        var units = Parts(true);
        if (units.Count > MaxSpreadParts) units = Parts(false);
        if (units.Count < 2)
        {
            warnings.Add("Spread the heat is on, but the job is one part, so there's nothing to reorder.");
            return;
        }
        if (units.Count > MaxSpreadParts)
        {
            warnings.Add($"Spread the heat: {units.Count:N0} layers is too many to plan, so they run in the usual order.");
            return;
        }

        var heat = units.Select(u =>
        {
            var range = Enumerable.Range(u.First, u.Count);
            var footprint = range.Select(k => blockSteps[k]).Distinct().SelectMany(l => steps.GetRange(l.First, l.Count)).Select(st => Rect2.Bounds(st.Points)).ToList();
            bool cut = blockLayer[u.First].Mode == LayerMode.Cut;
            double joules = range.Sum(k =>
            {
                var b = blocks[k];
                double watts = (b.Laser.Light == LightSource.Red ? opt.RedWatts : opt.BlueWatts) * b.Laser.Power / 100;
                return (cut ? HeatSpread.CutShare : HeatSpread.MarkShare) * watts * Job.AllPassesSeconds(b with { PauseAfterS = 0 });
            });
            return new HeatUnit(footprint, joules, range.Sum(k => Job.AllPassesSeconds(blocks[k])), cut);
        }).ToList();
        var plan = HeatSpread.Plan(heat, h, opt.HeatProgress, opt.Cancel);
        string hottest = blockLayer[units[plan.Hottest].First].Name;

        // The blocks in the new order, each wait after the part before it.
        var newBlocks = new List<Block>(blocks.Count);
        var newLayers = new List<Layer>(blocks.Count);
        var oldLinks = new List<(int First, int Count)>(blocks.Count);
        for (int p = 0; p < plan.Order.Length; p++)
        {
            var (first, count) = units[plan.Order[p]];
            if (plan.WaitBeforeS[p] > 0 && newBlocks.Count > 0)
                newBlocks[^1] = newBlocks[^1] with { PauseAfterS = newBlocks[^1].PauseAfterS + plan.WaitBeforeS[p] };
            for (int k = first; k < first + count; k++)
            {
                newBlocks.Add(blocks[k]);
                newLayers.Add(blockLayer[k]);
                oldLinks.Add(blockSteps[k]);
            }
        }
        // The steps in the order their blocks now run (any no block marks stay at the end).
        var newSteps = new List<BuiltJob.Step>(steps.Count);
        var moved = new Dictionary<int, int>();
        var taken = new bool[steps.Count];
        var newLinks = new List<(int First, int Count)>(blocks.Count);
        foreach (var link in oldLinks)
        {
            if (link.Count == 0) { newLinks.Add((newSteps.Count, 0)); continue; }
            if (!moved.TryGetValue(link.First, out int at))
            {
                moved[link.First] = at = newSteps.Count;
                for (int k = link.First; k < link.First + link.Count; k++)
                {
                    newSteps.Add(steps[k]);
                    taken[k] = true;
                }
            }
            newLinks.Add((at, link.Count));
        }
        for (int k = 0; k < steps.Count; k++)
            if (!taken[k]) newSteps.Add(steps[k]);
        blocks.Clear(); blocks.AddRange(newBlocks);
        blockLayer.Clear(); blockLayer.AddRange(newLayers);
        blockSteps.Clear(); blockSteps.AddRange(newLinks);
        steps.Clear(); steps.AddRange(newSteps);

        int reordered = plan.Order.Where((u, p) => u != p).Count();
        var metal = SheetMetal.Find(h.Sheet).Name.ToLowerInvariant();
        warnings.Add($"Spread the heat: {reordered:N0} of {units.Count:N0} parts reordered (each layer, or each separate shape on an engrave or picture layer), " +
                     $"each starting on {metal} at most about +{plan.PeakC:0} °C warmer than at the start (estimated, on a {plan.CellMm:0.##} mm grid)" +
                     (plan.WaitS > 0.05 ? $", {Hours(plan.WaitS)} of waits added to let it cool." : ", no waits needed."));
        double work = blocks.Sum(Job.AllPassesSeconds) - plan.WaitS;
        if (plan.OneUnitC > h.LimitC)
            warnings.Add($"Spread the heat: the cut \"{hottest}\" warms the {metal} about +{plan.OneUnitC:0} °C on its own, more than the +{h.LimitC:0} °C allowed. " +
                         "No order can fix that, so the job waits until each spot it cuts is nearly cold. A heat sink helps most: tape or clamp the plate to a metal block (and say so under How it's held). Or lower the power, go faster, or allow more warming.");
        if (plan.Capped)
            warnings.Add($"Spread the heat: some waits reached the {HeatSpread.MaxWait / 60:0}-minute cap, so the {metal} is warmer than allowed where some parts start. " +
                         "Ordering alone can't keep it cool enough: a heat sink (tape or clamp the plate to a metal block, and say so under How it's held), fewer or smaller parts, or more warming allowed.");
        else if (plan.WaitS > 0.25 * Math.Max(1, work))
            warnings.Add($"Spread the heat: the waits ({Hours(plan.WaitS)}) are a big share of the job. A heat sink would cut most of them: tape or clamp the plate to a metal block, and say so under How it's held.");
    }

    /// <summary>The run in order, the way <see cref="Job.Render"/> lays it out (rounds or per-path passes).</summary>
    static List<BuiltJob.RunSpan> Timeline(Job job, IReadOnlyList<(int First, int Count)> blockSteps)
    {
        var spans = new List<BuiltJob.RunSpan>();
        // The machine takes a moment to start before anything is marked.
        double t = Job.StartS;
        var sec = job.Blocks.Select(Job.BlockSeconds).ToList();
        if (job.PassMode is PassMode.Rounds r)
        {
            int rounds = job.Rounds;
            for (int round = 1; round <= rounds; round++)
            {
                for (int i = 0; i < job.Blocks.Count; i++)
                {
                    if (job.Blocks[i].Laser.Passes < round) continue;
                    double d = Job.PassSeconds(job.Blocks[i], round);
                    spans.Add(new(t, t + d, blockSteps[i].First, blockSteps[i].Count, d));
                    t += d;
                }
                if (round < rounds && r.DelayS > 0)
                {
                    spans.Add(new(t, t + r.DelayS, 0, 0, r.DelayS));
                    t += r.DelayS;
                }
            }
        }
        else
            for (int i = 0; i < job.Blocks.Count; i++)
            {
                double d = Job.AllPassesSeconds(job.Blocks[i]);
                spans.Add(new(t, t + d, blockSteps[i].First, blockSteps[i].Count, sec[i]));
                t += d;
            }
        return spans;
    }

    static double FoldTo180(double deg) => ((deg % 180) + 180) % 180;

    static string Label(Shape sh) => string.IsNullOrWhiteSpace(sh.Name) ? sh.GetType().Name.Replace("Shape", "") : sh.Name.Length > 30 ? sh.Name[..30] + "…" : sh.Name;

    /// <summary>
    /// What on this layer won't be done because it's the wrong kind of layer for it (outlines on a
    /// picture layer, pictures on a cut, score or fill layer), in plain words; null when all is fine.
    /// </summary>
    public static string? LeftOutOn(DocState s, Layer layer)
    {
        var on = s.ShapesFor(layer).ToList();
        if (layer.Mode == LayerMode.Image)
        {
            int paths = on.OfType<PathShape>().Count();
            return paths == 0 ? null
                : $"Layer \"{layer.Name}\" is a Picture layer, which only engraves pictures, so {(paths == 1 ? "its shape" : $"its {paths} shapes")} won't be done. Change the layer to Cut, Score or Engrave, or move {(paths == 1 ? "it" : "them")} to a layer that is.";
        }
        int pictures = on.OfType<ImageShape>().Count();
        return pictures == 0 ? null
            : $"Layer \"{layer.Name}\" is a {(layer.Mode == LayerMode.Fill ? "Engrave" : layer.Mode)} layer, so {(pictures == 1 ? "its picture" : $"its {pictures} pictures")} won't be engraved. Move {(pictures == 1 ? "it" : "them")} to a Picture layer, or trace {(pictures == 1 ? "it" : "them")} into outlines.";
    }

    /// <summary>The resolution a picture's own pixels have at its size on the bed.</summary>
    public static double OwnDpi(ImageShape img) => 25.4 * img.Source.Width / Math.Max(1e-6, Math.Abs(img.SizeMm.X * img.Transform.ScaleX));

    /// <summary>Most different pass angles a picture is laid out at (after that, they repeat).</summary>
    const int MaxTurnedPasses = 36;

    /// <summary>Most dots a single image may produce (keeps files and memory sane).</summary>
    const long MaxImageCells = 40_000_000;

    /// <summary>
    /// Resample an image onto the machine's dot grid (axis-aligned, so rotated
    /// images work, or turned by <paramref name="angleDeg"/> so the rows run that way),
    /// apply its tone settings and dither it. Returns the dot block and the area it
    /// covers, or null when nothing would burn.
    /// </summary>
    /// <param name="power">Less power in places: each spot's darkness is scaled by the field there before it's dithered (or given its grey power).</param>
    public static (DotBlock? Block, Rect2 Area) Rasterize(ImageShape img, Layer layer, double angleDeg = 0, PowerField? power = null)
    {
        angleDeg = ((angleDeg % 360) + 360) % 360;
        if (Math.Abs(angleDeg - Math.Round(angleDeg)) < 1e-9) angleDeg = Math.Round(angleDeg) % 360;
        JobException.CheckRange("resolution", layer.Dpi, 10, 2540, "dpi");
        // Pass-through: one dot per pixel of the file, but never closer together than the layer's
        // resolution (a big file on a small picture would put far more heat in than was set).
        double dpi = layer.PassThrough ? Math.Clamp(Math.Min(OwnDpi(img), layer.Dpi), 10, 2540) : layer.Dpi;
        double pitch = 25.4 / dpi;
        // Only where the picture shows: a clipped one (one piece of a bigger design) only over its clip.
        var b = img.ShownBounds;
        // The grid's axes: along the rows (u) and across them (v); at 0° just X and Y.
        double a = angleDeg * Math.PI / 180;
        var U = angleDeg == 0 ? new Vec2(1, 0) : new Vec2(Math.Cos(a), Math.Sin(a));
        var V = angleDeg == 0 ? new Vec2(0, 1) : new Vec2(-Math.Sin(a), Math.Cos(a));
        Vec2[] corners = angleDeg == 0 ? [b.TopLeft, b.BottomRight] : img.ShownPoints;
        double u0 = corners.Min(p => p.X * U.X + p.Y * U.Y), u1 = corners.Max(p => p.X * U.X + p.Y * U.Y);
        double v0 = corners.Min(p => p.X * V.X + p.Y * V.Y), v1 = corners.Max(p => p.X * V.X + p.Y * V.Y);
        int cols = Math.Max(1, (int)Math.Floor((u1 - u0) / pitch + 1e-9));
        int rows = Math.Max(1, (int)Math.Floor((v1 - v0) / pitch + 1e-9));
        if ((long)cols * rows > MaxImageCells)
            throw new JobException($"Image \"{img.Name}\" at {layer.Dpi:0} dpi needs {(long)cols * rows / 1e6:0.#} million dots. Lower the resolution or make it smaller.");
        if (!img.Transform.IsInvertible) return (null, b);
        var toLocal = img.Transform.Invert();
        var origin = angleDeg == 0 ? new Vec2(b.MinX + pitch / 2, b.MinY + pitch / 2) : U * (u0 + pitch / 2) + V * (v0 + pitch / 2);
        Vec2 Pos(int c, int r) => angleDeg == 0 ? new Vec2(origin.X + c * pitch, origin.Y + r * pitch) : origin + U * (c * pitch) + V * (r * pitch);
        var tone = img.Tone;
        // The material's measured response, turned round: the grey to send for each tone wanted.
        var material = layer.MaterialTone;
        var grey = new float[cols * rows];
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                var local = toLocal.Apply(Pos(c, r));
                float v;
                if (local.X < 0 || local.Y < 0 || local.X > img.SizeMm.X || local.Y > img.SizeMm.Y || !img.InClip(local)) v = 1f; // outside the picture
                else if (img.LocalToPixel(local) is var px && img.Source.SeeThrough(px.X, px.Y)) v = 1f; // see-through: never burned, even inverted
                else
                {
                    v = (float)tone.Apply(img.Source.SampleLuminance(px.X, px.Y));
                    if (material is not null) v = (float)material.Correct(v);
                    if (power is not null) v = 1 - (1 - v) * (float)power.At(Pos(c, r));
                }
                grey[r * cols + c] = v;
            }
        });
        var area = Rect2.Bounds([Pos(0, 0), Pos(cols - 1, 0), Pos(0, rows - 1), Pos(cols - 1, rows - 1)]);
        if (angleDeg != 0)
        {
            // A turned grid's box reaches well past the picture; only the part it shows is framed.
            var shown = b.Inflate(pitch);
            area = new Rect2(Math.Max(area.MinX, shown.MinX), Math.Max(area.MinY, shown.MinY), Math.Min(area.MaxX, shown.MaxX), Math.Min(area.MaxY, shown.MaxY));
        }
        if (tone.Sharpen > 0) Core.Imaging.Dither.Sharpen(grey, cols, rows, tone.Sharpen, Math.Max(1, (int)Math.Round(tone.SharpenRadiusMm / pitch)));
        var bitmap = new Bitmap(cols, rows);
        if (layer.ImageMode == ImageEngraveMode.Grayscale)
        {
            // Power follows darkness: the lightest burned tone at GrayMinPower,
            // black at the layer's power; pure white is not visited at all.
            var levels = new byte[grey.Length];
            bool any = false;
            for (int i = 0; i < grey.Length; i++)
            {
                int q = (int)Math.Round((1 - Math.Clamp(grey[i], 0f, 1f)) * 255);
                if (q <= 0) continue;
                bitmap.Bits[i] = true;
                levels[i] = (byte)q;
                any = true;
            }
            if (!any) return (null, b);
            return (new DotBlock(bitmap, origin, layer.Laser, dpi, layer.DotUs, layer.GrayMinPower, "grayscale")
            {
                Levels = levels,
                AngleDeg = angleDeg,
            }, area);
        }
        if (layer.Dither == DitherMethod.Threshold && layer.RampMm > 0)
        {
            // Ramp: power rises from the lightest power at an edge to full power RampMm inside it.
            var on = new bool[grey.Length];
            for (int i = 0; i < grey.Length; i++) on[i] = grey[i] < 0.5f;
            var dist = EdgeDistance(on, cols, rows);
            var levels = new byte[grey.Length];
            bool any = false;
            for (int i = 0; i < grey.Length; i++)
            {
                if (!on[i]) continue;
                bitmap.Bits[i] = true;
                levels[i] = (byte)Math.Clamp((int)Math.Round(255 * Math.Min(1, dist[i] * pitch / layer.RampMm)), 0, 255);
                any = true;
            }
            if (!any) return (null, b);
            return (new DotBlock(bitmap, origin, layer.Laser, dpi, layer.DotUs, Math.Min(layer.GrayMinPower, layer.Laser.Power), "grayscale") { Levels = levels, AngleDeg = angleDeg }, area);
        }
        var dots = Core.Imaging.Dither.Apply(grey, cols, rows, layer.Dither, new Screen(dpi / Math.Max(1, layer.HalftoneCpi), layer.HalftoneAngleDeg));
        if (!dots.Any(d => d)) return (null, b);
        Array.Copy(dots, bitmap.Bits, dots.Length);
        var block = new DotBlock(bitmap, origin, layer.Laser, dpi, layer.DotUs,
            Math.Min(1, layer.Laser.Power), Core.Imaging.Dither.WireName(layer.Dither)) { AngleDeg = angleDeg };
        return (block, area);
    }

    /// <summary>Most offset-fill rings per shape (a 100 mm shape at 0.01 mm spacing needs 5000).</summary>
    const int MaxOffsetRings = 20_000;

    /// <summary>
    /// Offset fill: the area's outline stepped inward by <paramref name="spacing"/> (starting half a
    /// step in) until nothing is left. Outermost first; within a ring, nearest loop next, each
    /// starting at its point nearest the laser.
    /// </summary>
    public static List<IReadOnlyList<Vec2>> OffsetLoops(PathGeometry area, double spacing, double tol, ref Vec2 pos)
    {
        var o = new List<IReadOnlyList<Vec2>>();
        if (!(spacing > 0) || !double.IsFinite(spacing)) return o;
        double ftol = Math.Min(tol, spacing / 4);
        for (int ring = 0; ring < MaxOffsetRings; ring++)
        {
            var moved = ShapeGeometry.Offset(area, -(ring + 0.5) * spacing, JoinStyle.Round, EndStyle.Flat, tolerance: ftol);
            var loops = moved.Flatten(ftol).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
            if (loops.Count == 0) break;
            while (loops.Count > 0)
            {
                int best = 0, at = 0;
                double bestD = double.PositiveInfinity;
                for (int k = 0; k < loops.Count; k++)
                    for (int i = 0; i < loops[k].Count; i++)
                    {
                        double d = loops[k][i].Dist(pos);
                        if (d < bestD) { bestD = d; best = k; at = i; }
                    }
                var loop = loops[best];
                loops.RemoveAt(best);
                var start = at == 0 ? loop : [.. loop.Skip(at), .. loop.Take(at)];
                o.Add(start);
                pos = start[0];
            }
        }
        return o;
    }

    /// <summary>The cleanup passes' settings for a depth-slice layer (always the layer's source).</summary>
    static LaserSettings CleanupLaser(Layer l) => l.Slices.CleanupLaser with
    {
        Light = l.Laser.Light,
        Passes = l.Laser.Passes,
        PulseWidthNs = l.Laser.Light == LightSource.Red ? l.Slices.CleanupLaser.PulseWidthNs : l.Laser.PulseWidthNs,
        FrequencyKhz = l.Laser.Light == LightSource.Red ? l.Slices.CleanupLaser.FrequencyKhz : l.Laser.FrequencyKhz,
    };

    /// <summary>
    /// A depth-map picture as slices: cell levels on a square grid of 25.4 / dpi mm, then one mask
    /// per slice (slice k covers everything at least k steps deep). Slice 1 is the largest, the
    /// last the deepest. Cells outside the picture are never engraved.
    /// </summary>
    public static (FillMask[] Slices, Rect2 Area) DepthSliceMasks(ImageShape img, Layer layer)
    {
        JobException.CheckRange("resolution", layer.Dpi, 10, 2540, "dpi");
        int n = Math.Clamp(layer.Slices.Count, 2, DepthSlices.MaxCount);
        double cell = 25.4 / layer.Dpi;
        var b = img.ShownBounds;
        int cols = Math.Max(1, (int)Math.Floor(b.Width / cell + 1e-9));
        int rows = Math.Max(1, (int)Math.Floor(b.Height / cell + 1e-9));
        if ((long)cols * rows > MaxImageCells)
            throw new JobException($"Image \"{img.Name}\" at {layer.Dpi:0} dpi needs {(long)cols * rows / 1e6:0.#} million cells. Lower the resolution or make it smaller.");
        if (!img.Transform.IsInvertible) return ([], b);
        var toLocal = img.Transform.Invert();
        var tone = img.Tone;
        bool negative = layer.Slices.Negative;
        var levels = new byte[cols * rows];
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                var local = toLocal.Apply(new Vec2(b.MinX + (c + 0.5) * cell, b.MinY + (r + 0.5) * cell));
                if (local.X < 0 || local.Y < 0 || local.X > img.SizeMm.X || local.Y > img.SizeMm.Y || !img.InClip(local)) continue; // outside the picture
                var px = img.LocalToPixel(local);
                if (img.Source.SeeThrough(px.X, px.Y)) continue; // see-through: not carved
                double grey = Math.Clamp(tone.Apply(img.Source.SampleLuminance(px.X, px.Y)), 0, 1);
                double depth = negative ? grey : 1 - grey;
                levels[r * cols + c] = (byte)Math.Clamp((int)Math.Round(depth * n), 0, n);
            }
        });
        var origin = b.TopLeft;
        var masks = Enumerable.Range(1, n).Select(k => new FillMask(origin, cell, cols, rows, levels, k)).ToArray();
        return (masks, new Rect2(origin.X, origin.Y, origin.X + cols * cell, origin.Y + rows * cell));
    }

    /// <summary>For each burned cell, how many cells it is from the nearest unburned one (chamfer distance).</summary>
    static double[] EdgeDistance(bool[] on, int w, int h)
    {
        var d = new double[on.Length];
        const double Diag = 1.41421356;
        for (int i = 0; i < d.Length; i++) d[i] = on[i] ? double.PositiveInfinity : 0;
        double At(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : d[y * w + x];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (on[y * w + x])
                    d[y * w + x] = Math.Min(d[y * w + x], Math.Min(Math.Min(At(x - 1, y) + 1, At(x, y - 1) + 1), Math.Min(At(x - 1, y - 1) + Diag, At(x + 1, y - 1) + Diag)));
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
                if (on[y * w + x])
                    d[y * w + x] = Math.Min(d[y * w + x], Math.Min(Math.Min(At(x + 1, y) + 1, At(x, y + 1) + 1), Math.Min(At(x + 1, y + 1) + Diag, At(x - 1, y + 1) + Diag)));
        return d;
    }

    /// <summary>Perforation: the path as dashes of <paramref name="on"/> mm with <paramref name="off"/> mm gaps.</summary>
    public static List<List<Vec2>> Dashes(IReadOnlyList<Vec2> pts, double on, double off)
    {
        var o = new List<List<Vec2>>();
        if (pts.Count < 2 || !(on > 0) || off < 0) return o;
        double phase = 0; // how far into the current dash (0…on) or gap (on…on+off)
        List<Vec2>? dash = null;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            double len = a.Dist(b), t = 0;
            while (len - t > 1e-9)
            {
                bool burning = phase < on;
                double remain = burning ? on - phase : on + off - phase;
                double step = Math.Min(len - t, remain);
                var p1 = Vec2.Lerp(a, b, (t + step) / len);
                if (burning)
                {
                    if (dash is null) { dash = [Vec2.Lerp(a, b, t / len)]; o.Add(dash); }
                    dash.Add(p1);
                }
                t += step;
                phase += step;
                if (phase >= on - 1e-9 && burning && step >= remain - 1e-9) { dash = null; phase = on; }
                if (phase >= on + off - 1e-9) phase = 0;
            }
        }
        return o;
    }

    /// <summary>Dot mode: points every <paramref name="spacing"/> mm along the path, from its start.</summary>
    public static List<Vec2> DotsAlong(IReadOnlyList<Vec2> pts, double spacing)
    {
        var o = new List<Vec2>();
        if (pts.Count == 0 || !(spacing > 0)) return o;
        o.Add(pts[0]);
        double carry = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            double len = a.Dist(b);
            double t = spacing - carry;
            while (t <= len + 1e-12)
            {
                o.Add(Vec2.Lerp(a, b, len > 0 ? t / len : 0));
                t += spacing;
            }
            carry = len - (t - spacing);
        }
        // Round a closed outline the last dot can land back on the first: it would fire twice there.
        if (o.Count > 1 && o[^1].Dist(o[0]) < 1e-6) o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>Checks the layer's cut, fill and image extras; messages are for the person at the laser.</summary>
    static void ValidateExtras(Layer l, double focusLimit)
    {
        JobException.CheckRange("focus offset", l.FocusOffsetMm, -focusLimit, focusLimit, "mm");
        switch (l.Mode)
        {
            case LayerMode.Cut or LayerMode.Score:
                JobException.CheckRange("kerf", l.KerfMm, -MaxKerfMm, MaxKerfMm, "mm");
                // Perforation and dot mode work on scored lines too.
                if (l.PerforateMm > 0) { JobException.CheckRange("perforation dash", l.PerforateMm, 0.05, 100, "mm"); JobException.CheckRange("perforation gap", l.PerforateGapMm, 0.05, 100, "mm"); }
                if (l.DotModeMm > 0) { JobException.CheckRange("dot spacing", l.DotModeMm, 0.01, 50, "mm"); JobException.CheckRange("dot time", l.DotUs, 10, 100_000, "µs"); }
                if (l.Mode == LayerMode.Score) break;
                JobException.CheckRange("number of tabs", l.TabCount, 0, MaxTabs, "");
                if (l.TabCount > 0) JobException.CheckRange("tab width", l.TabWidthMm, 0.1, 10, "mm");
                JobException.CheckRange("tab power", l.TabPowerPct, 0, 100, "%");
                JobException.CheckRange("overcut", l.OvercutMm, 0, 10, "mm");
                JobException.CheckRange("lead-in", l.LeadInMm, 0, 10, "mm");
                JobException.CheckRange("lead-out", l.LeadOutMm, 0, 10, "mm");
                break;
            case LayerMode.Fill:
                JobException.CheckRange("line density", l.LinesPerCm, 1, FillBlock.MaxLinesPerCm, "lines/cm");
                JobException.CheckRange("fill angle", l.FillAngleDeg, -360, 360, "degrees");
                JobException.CheckRange("turn per pass", l.AngleStepDeg, -180, 180, "degrees");
                JobException.CheckRange("overscan", l.OverscanMm, 0, 20, "mm");
                JobException.CheckRange("dot width correction", l.DotWidthMm, 0, 1, "mm");
                JobException.CheckRange("crosshatch angle", l.CrossAngleDeg, -180, 180, "degrees");
                JobException.CheckRange("pause between lines", l.LineDwellMs, 0, 60_000, "ms");
                if (l.StepDownMm > 0 || l.StepDownFirstMm > 0)
                    new StepDown(l.StepDownFirstMm, l.StepDownMm).Validate(Math.Max(1, l.Laser.Passes));
                if (l.DotWidthMm >= 10 / l.LinesPerCm && l.DotWidthMm > 0)
                    throw new JobException($"Dot width correction ({l.DotWidthMm:0.###} mm) must be less than the line spacing ({10 / l.LinesPerCm:0.###} mm).");
                break;
            case LayerMode.Image when l.Dither is DitherMethod.Halftone or DitherMethod.LineScreen && l.ImageMode == ImageEngraveMode.Dither:
                JobException.CheckRange("screen cells per inch", l.HalftoneCpi, 2, l.Dpi / 2, "cells/inch");
                JobException.CheckRange("screen angle", l.HalftoneAngleDeg, -360, 360, "degrees");
                break;
            case LayerMode.Image when l.ImageMode == ImageEngraveMode.Slices:
                var sl = l.Slices;
                JobException.CheckRange("number of slices", sl.Count, 2, DepthSlices.MaxCount, "");
                JobException.CheckRange("line density", l.LinesPerCm, 1, FillBlock.MaxLinesPerCm, "lines/cm");
                JobException.CheckRange("scan angle", l.FillAngleDeg, -360, 360, "degrees");
                JobException.CheckRange("turn per slice", l.AngleStepDeg, -180, 180, "degrees");
                JobException.CheckRange("overscan", l.OverscanMm, 0, 20, "mm");
                JobException.CheckRange("focus drop", sl.DropMm, 0, StepDown.MaxPerPassMm, "mm");
                JobException.CheckRange("slices per focus drop", sl.DropEvery, 1, DepthSlices.MaxCount, "");
                JobException.CheckRange("slices per cleanup", sl.CleanupEvery, 0, DepthSlices.MaxCount, "");
                if (sl.CleanupEvery > 0) JobException.CheckRange("cleanup line density", sl.CleanupLinesPerCm, 1, FillBlock.MaxLinesPerCm, "lines/cm");
                double deepest = l.FocusOffsetMm + sl.DropMm * ((sl.Count - 1) / Math.Max(1, sl.DropEvery));
                if (deepest > Job.MaxFocusOffsetMm + 1e-9)
                    throw new JobException($"Following the slices down would lower the focus {deepest:0.##} mm in all; the limit is {Job.MaxFocusOffsetMm:0} mm. Drop less, or less often.");
                break;
            case LayerMode.Image when l.ImageMode == ImageEngraveMode.Grayscale:
                if (!double.IsFinite(l.GrayMinPower) || l.GrayMinPower < 0 || l.GrayMinPower > l.Laser.Power)
                    throw new JobException($"The lightest grey's power must be between 0% and the layer's power ({JobPct(l.Laser.Power)}), got {JobPct(l.GrayMinPower)}.");
                break;
        }
    }

    static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    static string JobPct(double v) => double.IsFinite(v) ? Num(v) + "%" : "an invalid number";

    /// <summary>Largest kerf compensation accepted, either way (mm).</summary>
    const double MaxKerfMm = 1;

    /// <summary>Most tabs per outline.</summary>
    const int MaxTabs = 100;

    /// <summary>
    /// Kerf compensation: every closed outline moves by half the kerf, away from
    /// the material it bounds. Outlines at even nesting depth (parts) grow;
    /// those at odd depth (holes) shrink, whether a hole is part of a compound
    /// shape or a separate shape on the layer. Open paths are cut unchanged.
    /// </summary>
    static List<FlatPath> ApplyKerf(List<PathShape> shapes, Layer layer, double tol, List<string> warnings)
    {
        var subs = shapes.SelectMany(sh => sh.World.Subpaths).Where(sp => !sp.Segs.IsEmpty).ToList();
        var flat = subs.Select(sp => new FlatPath(sp.Flatten(tol), sp.Closed)).ToList();
        var depth = Depths(flat);
        double half = layer.KerfMm / 2;
        var o = new List<FlatPath>();
        int open = 0, vanished = 0;
        for (int i = 0; i < subs.Count; i++)
        {
            if (!subs[i].Closed || flat[i].Points.Count < 3)
            {
                if (flat[i].Points.Count >= 2) o.Add(flat[i]);
                open++;
                continue;
            }
            var moved = ShapeGeometry.Offset(new PathGeometry([subs[i]]), depth[i] % 2 == 0 ? half : -half,
                JoinStyle.Sharp, EndStyle.Flat, tolerance: tol);
            var loops = moved.Flatten(tol).Where(f => f.Closed && f.Points.Count >= 3).ToList();
            if (loops.Count == 0) vanished++;
            o.AddRange(loops);
        }
        if (open > 0)
            warnings.Add($"Layer \"{layer.Name}\": kerf compensation only works on closed outlines, so {open} open path(s) are cut as drawn.");
        if (vanished > 0)
            warnings.Add($"Layer \"{layer.Name}\": {vanished} outline(s) are narrower than the kerf and were left out.");
        return o;
    }

    /// <summary>How many closed paths each path lies inside (0 = outermost).</summary>
    static int[] Depths(List<FlatPath> paths)
    {
        int n = paths.Count;
        var boxes = paths.Select(p => Rect2.Bounds(p.Points)).ToArray();
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (paths[i].Points.Count == 0) continue;
            var probe = paths[i].Points[0];
            for (int j = 0; j < n; j++)
            {
                if (i == j || !paths[j].Closed || paths[j].Points.Count < 3) continue;
                if (!boxes[j].Contains(boxes[i]) || boxes[i] == boxes[j] && j > i) continue;
                if (Geom.Inside(paths[j].Points, probe)) depth[i]++;
            }
        }
        return depth;
    }

    /// <summary>
    /// Order paths for cutting: anything inside a closed path is cut before
    /// that path (so inner features are done while the part is still held),
    /// then nearest-neighbour within each nesting depth to cut travel.
    /// Open paths may be reversed; closed loops keep their start point.
    /// </summary>
    public static List<FlatPath> OrderForCutting(List<FlatPath> paths, ref Vec2 pos) =>
        Order(paths, ref pos).Select(x => x.Path).ToList();

    /// <summary>A closed loop started at its point nearest <paramref name="p"/>.</summary>
    static FlatPath StartAt(FlatPath loop, Vec2 p)
    {
        if (!loop.Closed || loop.Points.Count < 3) return loop;
        var pts = loop.Points;
        if (pts[0].Dist(pts[^1]) < 1e-9) pts = pts.Take(pts.Count - 1).ToList();
        int best = 0;
        for (int i = 1; i < pts.Count; i++) if (pts[i].Dist(p) < pts[best].Dist(p)) best = i;
        return best == 0 ? loop : loop with { Points = [.. pts.Skip(best), .. pts.Take(best)] };
    }

    /// <summary><see cref="OrderForCutting"/>, with each path's nesting depth (even = part outline, odd = hole).</summary>
    static List<(FlatPath Path, int Depth)> Order(List<FlatPath> paths, ref Vec2 pos, PathOrder order = PathOrder.Optimised, bool startNearest = false, List<Vec2>? starts = null)
    {
        // Loops with a start point chosen on them begin exactly there (at the nearest point, even mid-edge).
        // They're remembered, so Start outlines nearest leaves them be (with kerf they no longer pass through the point itself).
        var chosen = new HashSet<FlatPath>(ReferenceEqualityComparer.Instance);
        if (starts is { Count: > 0 })
            paths = [.. paths.Select(p =>
            {
                if (!p.Closed || p.Points.Count < 3) return p;
                var ring = p.Points[0].Dist(p.Points[^1]) < 1e-9 ? p.Points.Take(p.Points.Count - 1).ToList() : p.Points;
                foreach (var s in starts)
                {
                    var near = PathTools.Nearest([.. ring, ring[0]], s);
                    if (near.Distance > 1) continue;
                    List<Vec2> closedRing = [.. ring, ring[0]];
                    var cum = PathTools.CumulativeLengths(closedRing);
                    var at = PathTools.At(closedRing, cum, near.Along);
                    int i = 0;
                    while (i + 1 < ring.Count && cum[i + 1] <= near.Along) i++;
                    // Start at the point, go round, and come back to it.
                    List<Vec2> pts = [at.Point, .. ring.Skip(i + 1), .. ring.Take(i + 1)];
                    var started = p with { Points = pts.Where((q, k) => k == 0 || q.Dist(pts[k - 1]) > 1e-9).ToList() };
                    chosen.Add(started);
                    return started;
                }
                return p;
            })];
        bool Fixed(FlatPath p) => chosen.Contains(p);
        int n = paths.Count;
        var depth = Depths(paths);
        var o = new List<(FlatPath, int)>(n);
        if (order == PathOrder.AsDrawn)
        {
            for (int i = 0; i < n; i++)
            {
                var p = startNearest && !Fixed(paths[i]) ? StartAt(paths[i], pos) : paths[i];
                o.Add((p, depth[i]));
                if (p.Points.Count > 0) pos = p.Closed ? p.Points[0] : p.Points[^1];
            }
            return o;
        }
        // Back to front: bands 10 mm deep, from the top of the work area down.
        IEnumerable<IGrouping<(int, int), int>> groups = order == PathOrder.BackToFront
            ? Enumerable.Range(0, n).GroupBy(i => (-depth[i], (int)Math.Floor(Rect2.Bounds(paths[i].Points).MinY / 10))).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
            : Enumerable.Range(0, n).GroupBy(i => (-depth[i], 0)).OrderBy(g => g.Key.Item1);
        foreach (var group in groups)
        {
            var left = group.ToList();
            while (left.Count > 0)
            {
                int best = 0;
                bool reverse = false;
                double bestD = double.PositiveInfinity;
                for (int k = 0; k < left.Count; k++)
                {
                    var p = paths[left[k]];
                    double d = p.Points[0].Dist(pos);
                    if (d < bestD) { bestD = d; best = k; reverse = false; }
                    if (!p.Closed)
                    {
                        double e = p.Points[^1].Dist(pos);
                        if (e < bestD) { bestD = e; best = k; reverse = true; }
                    }
                }
                var pick = paths[left[best]];
                int pickDepth = depth[left[best]];
                left.RemoveAt(best);
                if (reverse) pick = pick with { Points = Enumerable.Reverse(pick.Points).ToList() };
                if (startNearest && !Fixed(pick)) pick = StartAt(pick, pos);
                o.Add((pick, pickDepth));
                pos = pick.Closed ? pick.Points[0] : pick.Points[^1];
            }
        }
        return o;
    }

    /// <summary>Tabs, overcut and lead-in for the closed outlines of one Cut layer.</summary>
    sealed class CutExtras(Layer layer)
    {
        /// <summary>A turn sharper than this is a corner a tab should not sit on.</summary>
        const double CornerRad = 30 * Math.PI / 180;

        readonly bool _cut = layer.Mode == LayerMode.Cut;
        int _tooShort, _tabbed;

        /// <summary>Tabs placed by hand (world mm): outlines they sit on get exactly those.</summary>
        public List<Vec2> ManualTabs { get; init; } = [];
        readonly List<List<Vec2>> _tabGaps = [];

        /// <summary>The tab bridges of the outline just split (when tabs are cut at low power), once.</summary>
        public List<List<Vec2>> TakeTabGaps()
        {
            var o = new List<List<Vec2>>(_tabGaps);
            _tabGaps.Clear();
            return o;
        }

        /// <summary>What the laser actually follows for one path: the path itself, or open pieces.</summary>
        public IEnumerable<FlatPath> Pieces(FlatPath path, int depth)
        {
            if (!_cut || !path.Closed) return [path];
            var loop = Distinct(path.Points);
            if (loop.Count < 2) return [path];
            var cum = Cumulative(loop);
            double perimeter = cum[^1];
            // Hand-placed tabs on this outline (within half a millimetre of it).
            var mine = ManualTabs.Select(t => PathTools.Nearest([.. loop, loop[0]], t)).Where(x => x.Distance < 0.5).Select(x => x.Along).OrderBy(a => a).ToList();
            if (mine.Count > 0)
            {
                _tabbed++;
                return TabsAt(loop, cum, mine);
            }
            if (layer.TabCount > 0)
            {
                if (perimeter >= layer.TabCount * layer.TabWidthMm * 3)
                {
                    _tabbed++;
                    return Tabs(loop, cum);
                }
                _tooShort++;
            }
            if (layer.LeadInMm <= 0 && layer.OvercutMm <= 0 && layer.LeadOutMm <= 0) return [path];
            var pts = new List<Vec2>(loop.Count + 8);
            if (layer.LeadInMm > 0) pts.Add(LeadIn(loop, depth, layer.LeadInMm));
            pts.AddRange(loop);
            pts.Add(loop[0]);
            if (layer.OvercutMm > 0) pts.AddRange(Along(loop, cum, 0, Math.Min(layer.OvercutMm, perimeter)).Skip(1));
            // Lead-out: away from the part into the waste, the same side as a lead-in.
            if (layer.LeadOutMm > 0) pts.Add(pts[^1] + (LeadIn(loop, depth, 1) - loop[0]) * layer.LeadOutMm);
            return [new FlatPath(pts, false)];
        }

        public void Warn(List<string> warnings)
        {
            if (_tooShort > 0)
                warnings.Add($"Layer \"{layer.Name}\": {_tooShort} outline(s) are too short for {layer.TabCount} tabs of {Num(layer.TabWidthMm)} mm and were cut without tabs.");
            if (_tabbed > 0 && (layer.LeadInMm > 0 || layer.OvercutMm > 0))
                warnings.Add($"Layer \"{layer.Name}\": outlines with tabs get no lead-in or overcut.");
        }

        /// <summary>The loop cut into pieces with gaps of TabWidth centred on the given distances along it.</summary>
        List<FlatPath> TabsAt(List<Vec2> loop, double[] cum, List<double> centres)
        {
            double perimeter = cum[^1], w = layer.TabWidthMm > 0 ? layer.TabWidthMm : 0.5;
            var o = new List<FlatPath>(centres.Count);
            for (int k = 0; k < centres.Count; k++)
            {
                double from = centres[k] + w / 2;
                double to = (k + 1 < centres.Count ? centres[k + 1] : centres[0] + perimeter) - w / 2;
                if (to - from > 1e-6) o.Add(new FlatPath(Along(loop, cum, from, to), false));
                if (layer.TabPowerPct > 0) _tabGaps.Add(Along(loop, cum, centres[k] - w / 2, centres[k] + w / 2));
            }
            return o;
        }

        /// <summary>
        /// The loop cut into TabCount pieces with gaps of TabWidth between them,
        /// spaced evenly along it; the first gap is centred half a spacing from
        /// the start. Gaps are nudged off sharp corners where there is room.
        /// </summary>
        List<FlatPath> Tabs(List<Vec2> loop, double[] cum)
        {
            int count = layer.TabCount;
            double perimeter = cum[^1], w = layer.TabWidthMm, spacing = perimeter / count;
            var corners = new List<double>();
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i] - loop[(i - 1 + loop.Count) % loop.Count];
                var c = loop[(i + 1) % loop.Count] - loop[i];
                if (Math.Abs(Math.Atan2(a.Cross(c), a.Dot(c))) > CornerRad) corners.Add(cum[i]);
            }
            bool Clear(double centre) => corners.All(s =>
            {
                double d = Math.Abs(s - centre) % perimeter;
                return Math.Min(d, perimeter - d) > w;
            });
            var centres = new double[count];
            for (int k = 0; k < count; k++)
            {
                double c = (k + 0.5) * spacing;
                centres[k] = c;
                if (Clear(c)) continue;
                for (double shift = w / 4; shift <= spacing / 4; shift += w / 4)
                {
                    if (Clear(c + shift)) { centres[k] = c + shift; break; }
                    if (Clear(c - shift)) { centres[k] = c - shift; break; }
                }
            }
            var o = new List<FlatPath>(count);
            for (int k = 0; k < count; k++)
            {
                double from = centres[k] + w / 2;
                double to = (k + 1 < count ? centres[k + 1] : centres[0] + perimeter) - w / 2;
                if (to - from > 1e-6) o.Add(new FlatPath(Along(loop, cum, from, to), false));
                if (layer.TabPowerPct > 0) _tabGaps.Add(Along(loop, cum, centres[k] - w / 2, centres[k] + w / 2));
            }
            return o;
        }

        /// <summary>
        /// Start of the entry line: <paramref name="len"/> mm from the loop's start
        /// along its normal, outside an outer outline and inside a hole (the scrap side).
        /// </summary>
        static Vec2 LeadIn(List<Vec2> loop, int depth, double len)
        {
            var p0 = loop[0];
            var tin = Unit(p0 - loop[^1]);
            var tout = Unit(loop[1] - p0);
            var t = tin + tout;
            t = t.Length < 1e-9 ? tout : Unit(t);
            var left = new Vec2(-t.Y, t.X);
            // Positive signed area: the inside is on the left of the direction of travel.
            var inward = Geom.SignedArea(loop) > 0 ? left : -left;
            return p0 + (depth % 2 == 1 ? inward : -inward) * len;
        }

        static Vec2 Unit(Vec2 v) => v.Length < 1e-12 ? new Vec2(1, 0) : v / v.Length;

        static List<Vec2> Distinct(List<Vec2> pts)
        {
            var o = new List<Vec2>(pts.Count);
            foreach (var p in pts)
                if (o.Count == 0 || o[^1].Dist(p) > Polyline.SamePointMm) o.Add(p);
            while (o.Count > 1 && o[0].Dist(o[^1]) <= Polyline.SamePointMm) o.RemoveAt(o.Count - 1);
            return o;
        }

        /// <summary>Distance along the closed loop to each point; the last entry is the perimeter.</summary>
        static double[] Cumulative(List<Vec2> loop)
        {
            var cum = new double[loop.Count + 1];
            for (int i = 1; i <= loop.Count; i++) cum[i] = cum[i - 1] + loop[i - 1].Dist(loop[i % loop.Count]);
            return cum;
        }

        /// <summary>The part of the closed loop from distance <paramref name="from"/> to <paramref name="to"/> (may wrap past the start once).</summary>
        static List<Vec2> Along(List<Vec2> loop, double[] cum, double from, double to)
        {
            int n = loop.Count;
            double perimeter = cum[^1];
            double len = Math.Clamp(to - from, 0, perimeter);
            from %= perimeter;
            if (from < 0) from += perimeter;
            to = from + len;
            Vec2 At(double s)
            {
                double lap = Math.Floor(s / perimeter);
                double r = s - lap * perimeter;
                int i = Array.BinarySearch(cum, r);
                if (i < 0) i = ~i - 1;
                i = Math.Clamp(i, 0, n - 1);
                double segLen = cum[i + 1] - cum[i];
                double t = segLen > 0 ? (r - cum[i]) / segLen : 0;
                return Vec2.Lerp(loop[i], loop[(i + 1) % n], t);
            }
            var o = new List<Vec2> { At(from) };
            // Vertices strictly between from and to, over at most two laps.
            for (int lap = 0; lap < 2; lap++)
                for (int i = 0; i < n; i++)
                {
                    double s = cum[i] + lap * perimeter;
                    if (s > from + 1e-9 && s < to - 1e-9) o.Add(loop[i]);
                }
            o.Add(At(to));
            return o;
        }
    }
}
