using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Tracing;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Addon.RotaryFinish;

/// <summary>How a stacked model is finished on the rotary.</summary>
public sealed record RotaryFinishOptions
{
    /// <summary>The dowel up the middle that the rotary's chuck holds (mm across): a hole this size through every slice.</summary>
    public double DowelMm { get; init; } = 6;
    /// <summary>How deep one engrave pass goes on a flat piece (mm), from the etch rate test.</summary>
    public double PassDepthMm { get; init; } = 0.1;
    /// <summary>
    /// How far the surface may be from the focus (mm): the surface is split into bands this deep by
    /// how far it is from the dowel, and each band is engraved with the focus at its own height.
    /// </summary>
    public double FocusBandMm { get; init; } = 1;
    /// <summary>Never engraves deeper than this (mm); anything more is left to sand.</summary>
    public double MaxDepthMm { get; init; } = 3;
    /// <summary>Keeps at least this much (mm) over the dowel hole, the pin holes and a hollow.</summary>
    public double WallMm { get; init; } = 2;
    /// <summary>The size of the map of the surface (mm, round and along): smaller follows the shape closer, slower to make.</summary>
    public double DetailMm { get; init; } = 0.25;
    /// <summary>The stack goes on the rotary with its bottom slice on the right (the model's top to the left).</summary>
    public bool BottomOnRight { get; init; }
}

/// <summary>What the slicing step hands on to the page made afterwards (in <see cref="SliceStack.Extras"/>).</summary>
public sealed record RotaryPrep(Vec2 Axis, IReadOnlyList<Tri> Mesh, double SlabMm, double ThicknessMm, int Slices, RotaryFinishOptions Options);

/// <summary>One focus height: the parts of the surface that are within the band then, a shape a pass.</summary>
/// <param name="RadiusMm">The band's middle: how far that surface is from the dowel's centre.</param>
/// <param name="FocusOffsetMm">How far the focus moves for it from where it was set (+ lowers the head).</param>
/// <param name="Passes">What each pass in the band engraves (design mm), in order.</param>
public sealed record RotaryBand(int Index, double RadiusMm, double FocusOffsetMm, IReadOnlyList<PathGeometry> Passes);

/// <summary>
/// A map of a turning surface for <see cref="RotaryFinish.Carve"/>: <see cref="NT"/> rows round the axis by
/// <see cref="NZ"/> columns along it, indexed [row × NZ + column]. Row j is design Y (j + ½) of the way round
/// in NT (the middle row, NT/2, faces the laser at the start); column k runs along design X.
/// </summary>
/// <param name="Stock">How far out the material is there now (mm from the axis; 0 = none).</param>
/// <param name="Floor">How far out it should end (never taken below this).</param>
public sealed record RotaryGrid(int NT, int NZ, float[] Stock, float[] Floor);

/// <summary>How the passes are made: the depth a flat pass goes, how deep a focus band is, and the most taken off anywhere (mm).</summary>
public sealed record RotaryCarve(double PassDepthMm, double FocusBandMm, double MaxDepthMm);

/// <summary>The engraving that takes a glued stack down to the model's shape, turning on its dowel.</summary>
public sealed record RotaryPlan
{
    /// <summary>The dowel's centre in the slices (slice mm).</summary>
    public Vec2 Axis { get; init; }
    /// <summary>The object Ø to set in Work mode (mm): the design goes once round at this size, so angles come out right.</summary>
    public double DiameterMm { get; init; }
    public double CircumferenceMm => Math.PI * DiameterMm;
    /// <summary>The widest the stack swings (mm across, about the dowel).</summary>
    public double WidestMm { get; init; }
    /// <summary>The stack's length along the dowel, and where its bottom slice's end is on the design (X, mm).</summary>
    public double LengthMm { get; init; }
    public double StartX { get; init; }
    public bool BottomOnRight { get; init; }
    /// <summary>Focus here before starting: on this slice (1 = bottom), on the start line, this far from the dowel's centre.</summary>
    public int FocusSlice { get; init; }
    public double FocusRadiusMm { get; init; }
    /// <summary>Where along the design (X, mm) to focus, when it's set by place rather than by slice (carving scanned stock).</summary>
    public double FocusX { get; init; }
    /// <summary>The focus heights, outermost first (empty: nothing to take off).</summary>
    public IReadOnlyList<RotaryBand> Bands { get; init; } = [];
    /// <summary>The most taken off anywhere (mm), and the most passes anywhere.</summary>
    public double DeepestMm { get; init; }
    public int MostPasses { get; init; }
    /// <summary>The surface engraved at all (mm² on the stack), and where more than the deepest allowed is needed (left to sand).</summary>
    public double EngravedMm2 { get; init; }
    public double TooDeepMm2 { get; init; }
    public int PassCount => Bands.Sum(b => b.Passes.Count);
}

/// <summary>
/// Finishing a stacked model on the rotary. The glued stack turns on a dowel up its middle (held in
/// the chuck) and the laser, firing straight down at the top of it, engraves the steps away down to
/// the model's real surface. The surface is mapped by angle round the dowel (so it doesn't matter that
/// the stack isn't round) and by height along it: at each spot, how far out the stack is and how far
/// out the model is. The difference comes off a pass at a time, and as the stack isn't round, each
/// pass happens at a different distance from the dowel in different places. So the passes are sorted
/// into focus bands by how far out the surface is at that moment: each band is one layer with the
/// focus moved to it, holding only the parts of each pass that are that far out, for every angle.
/// </summary>
public static class RotaryFinish
{
    /// <summary>The key the <see cref="RotaryPrep"/> is kept under in <see cref="SliceStack.Extras"/>.</summary>
    public const string Key = "rotary-finish";

    /// <summary>Which way faces the laser at the start: the model's front (−Y as modelled; down on the slices).</summary>
    public const double StartAngle = -Math.PI / 2;

    /// <summary>The biggest map made (cells); a finer detail on a big model is coarsened to this.</summary>
    const int MaxCells = 1_500_000;

    /// <summary>
    /// Where the dowel goes: inside every slice with room round it (the dowel, the wall and the
    /// notch), at the spot the stack swings least about, so the surface stays near the focus.
    /// Null when no straight line goes through every slice with that room.
    /// </summary>
    public static Vec2? ChooseAxis(IReadOnlyList<PathGeometry> layers, RotaryFinishOptions o)
    {
        if (layers.Count == 0 || layers.Any(l => l.IsEmpty)) return null;
        var common = layers.Skip(1).Aggregate(layers[0], (a, b) => ShapeGeometry.Boolean(a, b, BoolOp.Intersect));
        if (common.IsEmpty) return null;
        var room = ShapeGeometry.Offset(common, -(o.DowelMm / 2 + o.WallMm + 1.5), JoinStyle.Round, EndStyle.Round);
        var loops = room.IsEmpty ? [] : StackSlicer.Loops(room);
        if (loops.Count == 0) return null;
        var hull = JobBuilder.ConvexHull(layers.SelectMany(StackSlicer.Loops).SelectMany(l => l));
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        double cell = Math.Max(0.1, Math.Max(box.Width, box.Height) / 60);
        Vec2? best = null;
        double bestR = double.PositiveInfinity;
        for (double x = box.MinX + cell / 2; x <= box.MaxX; x += cell)
            for (double y = box.MinY + cell / 2; y <= box.MaxY; y += cell)
            {
                var p = new Vec2(x, y);
                if (loops.Count(l => Geom.Inside(l, p)) % 2 == 0) continue;
                double r = hull.Max(h => h.Dist(p));
                if (r < bestR - 1e-9) { bestR = r; best = p; }
            }
        // A sliver of room smaller than the grid: its widest spot.
        return best ?? (StackSlicer.Pole(loops) is { Room: > 0 } pole ? pole.At : null);
    }

    /// <summary>The notch in the dowel hole's edge that points to the start (the side that faces the laser first).</summary>
    public static PathGeometry Notch(Vec2 axis, double dowelMm) =>
        PathGeometry.Rect(axis.X - 0.6, axis.Y + dowelMm / 2 - 0.3, 1.2, 1.6);

    /// <summary>
    /// The engraving for a glued stack: <paramref name="parts"/> are each slice's parts as cut (bottom
    /// first, with their holes), in the slices' own mm. <paramref name="fieldW"/> is the laser's field
    /// across (the stack lies along it). <paramref name="cellMm"/> overrides the options' detail (a quick look).
    /// </summary>
    public static RotaryPlan Plan(RotaryPrep prep, IReadOnlyList<IReadOnlyList<PathGeometry>> parts, double fieldW, double? cellMm = null, CancellationToken ct = default)
    {
        var o = prep.Options;
        if (!(o.PassDepthMm > 0)) throw new ArgumentException("The depth a pass must be more than zero.");
        if (!(o.FocusBandMm > 0)) throw new ArgumentException("The focus band must be more than zero.");
        var a = prep.Axis;
        int n = prep.Slices;
        var sliceLoops = Enumerable.Range(0, n).Select(i => i < parts.Count ? parts[i].SelectMany(StackSlicer.Loops).ToList() : []).ToList();
        double rMax = sliceLoops.SelectMany(l => l).SelectMany(l => l).Select(p => p.Dist(a)).DefaultIfEmpty(0).Max();
        if (rMax <= 0) throw new ArgumentException("There's nothing round the dowel to engrave.");
        // Once round at the stack's widest (in whole mm, so it's easy to type), or as much as the rotary takes.
        // Any size would do, as the design goes by angle: this one keeps the lines nearly as far apart as drawn.
        double dia = Math.Min(Math.Max(1, Math.Ceiling(2 * rMax - 0.05)), Math.Floor(WorkModes.MaxCircumferenceMm / Math.PI));
        double rRef = dia / 2, circ = Math.PI * dia;
        double length = n * prep.ThicknessMm;
        double cell = Math.Max(0.05, cellMm ?? o.DetailMm);
        cell = Math.Max(cell, Math.Sqrt(circ * length / MaxCells));
        int nT = Math.Max(16, (int)Math.Ceiling(circ / cell)), nZ = Math.Max(1, (int)Math.Ceiling(length / cell));
        double dT = 2 * Math.PI / nT, dz = length / nZ;
        // Bin j's angle round the dowel (anticlockwise looking down the model from its top): the start
        // is the middle row (design Y = half way round); the stack the other way round turns it the other way.
        int sgn = o.BottomOnRight ? -1 : 1;
        double aBase = StartAngle - sgn * Math.PI;
        double Angle(int j) => aBase + sgn * (j + 0.5) * dT;
        var dirs = Enumerable.Range(0, nT).Select(j => { double t = Angle(j); return new Vec2(Math.Cos(t), -Math.Sin(t)); }).ToArray();

        // Each slice: the outermost edge along each angle, and the next one in (where the material there ends).
        var outer = new float[n][];
        var inner = new float[n][];
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            outer[i] = new float[nT];
            inner[i] = new float[nT];
            Crossings(sliceLoops[i], a, dirs, aBase, sgn, dT, outer[i], inner[i]);
        }
        double keep = o.DowelMm / 2 + o.WallMm;
        // The focus: set on the stack's highest point at the start line.
        int j0 = nT / 2, focusSlice = 0;
        double focusR = 0;
        for (int i = 0; i < n; i++)
            for (int j = Math.Max(0, j0 - 1); j <= Math.Min(nT - 1, j0); j++)
                if (outer[i][j] > focusR) { focusR = outer[i][j]; focusSlice = i; }
        if (focusR <= 0) focusR = rMax;

        var stock = new float[nZ * nT];
        var floor = new float[nZ * nT];
        var model = new float[nT];
        for (int k = 0; k < nZ; k++)
        {
            ct.ThrowIfCancellationRequested();
            double zModel = (k + 0.5) * dz * prep.SlabMm / prep.ThicknessMm;
            int i = Math.Clamp((int)(zModel / prep.SlabMm), 0, n - 1);
            Array.Clear(model);
            var section = StackSlicer.Section(prep.Mesh, zModel);
            if (!section.IsEmpty) Crossings(StackSlicer.Loops(section), a, dirs, aBase, sgn, dT, model, null);
            int col = o.BottomOnRight ? nZ - 1 - k : k;
            for (int j = 0; j < nT; j++)
            {
                stock[j * nZ + col] = outer[i][j];
                // Down to the model, never through the material there into a hole or the hollow.
                floor[j * nZ + col] = (float)Math.Max(model[j], Math.Max(inner[i][j] + o.WallMm, keep));
            }
        }

        double startX = (fieldW - length) / 2;
        var carved = Carve(new RotaryGrid(nT, nZ, stock, floor), new RotaryCarve(o.PassDepthMm, o.FocusBandMm, o.MaxDepthMm), rMax, focusR, dia, length, startX, ct);
        return carved with { Axis = a, BottomOnRight = o.BottomOnRight, FocusSlice = focusSlice + 1 };
    }

    /// <summary>
    /// The passes that take a turning surface down from <see cref="RotaryGrid.Stock"/> to <see cref="RotaryGrid.Floor"/>
    /// (both radii from the axis, mm, on the same map), sorted into focus bands by how far out each pass is.
    /// <paramref name="rMax"/>: the furthest out anything swings (the first band starts there); <paramref name="focusR"/>:
    /// how far out the surface is where the focus is set; <paramref name="dia"/>: the object Ø the design goes once round at;
    /// <paramref name="length"/> and <paramref name="startX"/>: how long the map is along the axis and where its first column starts on the design.
    /// </summary>
    public static RotaryPlan Carve(RotaryGrid g, RotaryCarve c, double rMax, double focusR, double dia, double length, double startX, CancellationToken ct = default)
    {
        if (!(c.PassDepthMm > 0)) throw new ArgumentException("The depth a pass must be more than zero.");
        if (!(c.FocusBandMm > 0)) throw new ArgumentException("The focus band must be more than zero.");
        int nT = g.NT, nZ = g.NZ;
        double rRef = dia / 2, circ = Math.PI * dia;
        double dT = 2 * Math.PI / nT, dz = length / nZ;
        double band = c.FocusBandMm, step = c.PassDepthMm;
        var masks = new Dictionary<(int Band, int Pass), bool[]>();
        double deepest = 0, engraved = 0, tooDeep = 0;
        int most = 0;
        for (int j = 0; j < nT; j++)
        {
            ct.ThrowIfCancellationRequested();
            for (int col = 0; col < nZ; col++)
            {
                double r0 = g.Stock[j * nZ + col];
                if (!(r0 > 0)) continue;
                double target = g.Floor[j * nZ + col];
                double excess = r0 - target;
                if (excess < step / 2) continue;
                double depth = Math.Min(excess, c.MaxDepthMm);
                double area = dz * r0 * dT;
                if (excess > c.MaxDepthMm + step / 2) tooDeep += area;
                // A pass at radius r goes deeper than on the flat by rRef / r: the lines are drawn
                // rRef apart a radian, so they're closer together on the surface nearer the axis.
                double r = r0;
                int pass = 0;
                while (r0 - r + Step(r) / 2 < depth && pass < 10000)
                {
                    int b = Math.Max(0, (int)Math.Floor((rMax - r) / band));
                    if (!masks.TryGetValue((b, pass), out var m)) masks[(b, pass)] = m = new bool[nZ * nT];
                    m[j * nZ + col] = true;
                    r -= Step(r);
                    pass++;
                }
                if (pass == 0) continue;
                engraved += area;
                most = Math.Max(most, pass);
                deepest = Math.Max(deepest, r0 - r);
            }
        }
        double Step(double r) => step * rRef / Math.Max(1, r);

        // Each band a layer: its passes in order, traced into shapes on the unrolled surface.
        var toDesign = Affine.Scale(dz, circ / nT).Then(Affine.Translate(startX, 0));
        var within = PathGeometry.Rect(startX - 1, 1e-3, length + 2, circ - 2e-3);
        var trace = new TraceOptions { Smoothness = 1, CornerAngle = 60, MinAreaPx = 3 };
        var bands = new List<RotaryBand>();
        foreach (var grp in masks.GroupBy(kv => kv.Key.Band).OrderBy(grp => grp.Key))
        {
            ct.ThrowIfCancellationRequested();
            var passes = new List<PathGeometry>();
            foreach (var kv in grp.OrderBy(kv => kv.Key.Pass))
            {
                var shape = Tracer.TraceMask(kv.Value, nZ, nT, trace);
                // (Kept to once round: the fitted curves can bulge a hair past the seam.)
                if (!shape.IsEmpty) shape = ShapeGeometry.Boolean(shape.Transform(toDesign), within, BoolOp.Intersect);
                if (!shape.IsEmpty) passes.Add(shape);
            }
            if (passes.Count == 0) continue;
            double mid = rMax - (grp.Key + 0.5) * band;
            bands.Add(new RotaryBand(bands.Count, mid, Math.Round(focusR - mid, 3), passes));
        }
        return new RotaryPlan
        {
            DiameterMm = dia, WidestMm = 2 * rMax, LengthMm = length, StartX = startX,
            FocusRadiusMm = focusR, Bands = bands,
            DeepestMm = deepest, MostPasses = most, EngravedMm2 = engraved, TooDeepMm2 = tooDeep,
        };
    }

    /// <summary>
    /// Along every bin's ray from <paramref name="a"/>: the furthest edge crossed into
    /// <paramref name="far"/>, and the next one in into <paramref name="next"/> (bigger values win).
    /// </summary>
    static void Crossings(List<List<Vec2>> loops, Vec2 a, Vec2[] dirs, double aBase, int sgn, double dT, float[] far, float[]? next)
    {
        int nT = dirs.Length;
        foreach (var loop in loops)
            for (int e = 0; e < loop.Count; e++)
            {
                var p = loop[e];
                var q = loop[(e + 1) % loop.Count];
                var wp = p - a;
                var edge = q - p;
                if (wp.Length < 1e-9 && (q - a).Length < 1e-9) continue;
                double ap = Math.Atan2(-wp.Y, wp.X), aq = Math.Atan2(-(q - a).Y, (q - a).X);
                double up = sgn * (ap - aBase) / dT;
                double du = sgn * Wrap(aq - ap) / dT;
                double lo = Math.Min(up, up + du), hi = Math.Max(up, up + du);
                for (int jj = (int)Math.Ceiling(lo - 0.5); jj <= (int)Math.Floor(hi - 0.5); jj++)
                {
                    int j = ((jj % nT) + nT) % nT;
                    var d = dirs[j];
                    double den = d.X * edge.Y - d.Y * edge.X;
                    if (Math.Abs(den) < 1e-12) continue;
                    double s = (wp.X * edge.Y - wp.Y * edge.X) / den;
                    double t = (wp.X * d.Y - wp.Y * d.X) / den;
                    if (s <= 0 || t < -1e-9 || t > 1 + 1e-9) continue;
                    float r = (float)s;
                    if (r > far[j])
                    {
                        if (next is not null) next[j] = far[j];
                        far[j] = r;
                    }
                    else if (next is not null && r > next[j]) next[j] = r;
                }
            }
    }

    static double Wrap(double x)
    {
        while (x > Math.PI) x -= 2 * Math.PI;
        while (x <= -Math.PI) x += 2 * Math.PI;
        return x;
    }

    /// <summary>What the plan means, for the slicing summary.</summary>
    public static string Describe(RotaryPlan p, RotaryFinishOptions o)
    {
        var s = $"Rotary finish: a {o.DowelMm:0.#} mm dowel goes up the middle (the notch in its hole points to the start). " +
                $"The stack swings {p.WidestMm:0.#} mm across: on the rotary (chuck on the dowel, slice 1 on the {(p.BottomOnRight ? "right" : "left")}) set Object Ø to {p.DiameterMm:0} mm.";
        if (p.Bands.Count == 0) return s + " Nothing needs taking off: the stack is already the model's shape (choose Outer for the outline so there's something to engrave down).";
        s += $" Up to {p.DeepestMm:0.##} mm comes off, in up to {p.MostPasses} pass{(p.MostPasses == 1 ? "" : "es")}, at {p.Bands.Count} focus height{(p.Bands.Count == 1 ? "" : "s")} {o.FocusBandMm:0.##} mm apart.";
        if (p.TooDeepMm2 > 1) s += $" {p.TooDeepMm2 / 100:0.#} cm² needs more than {o.MaxDepthMm:0.#} mm off: sand those spots.";
        return s;
    }
}

/// <summary>
/// The slicing step: picks the dowel's place, puts its hole (with the start notch) through every
/// slice, and after "Make the slices" adds a page with the rotary engraving.
/// </summary>
public sealed class RotaryFinishSlices(RotaryFinishOptions options, Layer? engrave = null) : ISliceModifier
{
    public RotaryFinishOptions Options { get; } = options;
    /// <summary>The engrave settings each focus band's layer starts from (a Fill layer; its depth a pass is <see cref="RotaryFinishOptions.PassDepthMm"/>).</summary>
    public Layer Engrave { get; } = engrave ?? Layer.Default(0, LayerMode.Fill);

    /// <summary>Before hollowing, so the hollow keeps material round the dowel.</summary>
    public int Order => -10;

    public void Modify(SliceWork work, CancellationToken ct)
    {
        var o = Options;
        if (!(o.DowelMm > 0)) throw new ArgumentException("The dowel needs a size.");
        if (ChooseAxisOf(work) is not { } axis)
        {
            work.Notes.Add($"Rotary finish: no straight line goes through every slice with room for a {o.DowelMm:0.#} mm dowel. Stand the model another way up, or use a thinner dowel.");
            return;
        }
        work.Holes.Add(new SliceHole(axis, o.DowelMm));
        var notch = RotaryFinish.Notch(axis, o.DowelMm);
        foreach (var slice in work.Parts)
            for (int k = 0; k < slice.Count; k++)
                if (slice[k].Bounds().Inflate(o.DowelMm).Contains(axis))
                    slice[k] = ShapeGeometry.Boolean(slice[k], notch, BoolOp.Subtract);
        var prep = new RotaryPrep(axis, work.Mesh, work.SlabMm, work.Options.ThicknessMm, work.Layers.Count, o);
        work.Extras[RotaryFinish.Key] = prep;
        // A quick look for the summary (the page gets the full detail).
        var holes = PathGeometry.Ellipse(axis.X, axis.Y, o.DowelMm / 2, o.DowelMm / 2);
        var parts = work.Parts.Select(l => (IReadOnlyList<PathGeometry>)[.. l.Select(g => ShapeGeometry.Boolean(g, holes, BoolOp.Subtract))]).ToList();
        try
        {
            var plan = RotaryFinish.Plan(prep, parts, MachineProfile.F2Ultra.FieldW, cellMm: Math.Max(1, o.DetailMm), ct: ct);
            work.Notes.Add(RotaryFinish.Describe(plan, o) + Fits(plan) +
                (work.Options.Outline != SliceOutline.Outer ? " Choose Outer for the outline: the other outlines cut into the model at the steps, and the laser can't put that back." : ""));
        }
        catch (ArgumentException e) { work.Notes.Add($"Rotary finish: {e.Message}"); }
    }

    Vec2? ChooseAxisOf(SliceWork work) => RotaryFinish.ChooseAxis(work.Layers, Options);

    static string Fits(RotaryPlan p) => p.LengthMm > MachineProfile.F2Ultra.FieldW - 4
        ? $" ⚠ The stack is {p.LengthMm:0} mm long, more than the laser's field ({MachineProfile.F2Ultra.FieldW:0} mm): make the model shorter."
        : "";

    public DocState AfterMake(DocState state, SliceStack stack, MachineProfile machine, string title)
    {
        if (!stack.Extras.TryGetValue(RotaryFinish.Key, out var x) || x is not RotaryPrep prep) return state;
        var parts = Enumerable.Range(1, prep.Slices).Select(i => (IReadOnlyList<PathGeometry>)[.. stack.Parts.Where(p => p.Slice == i).Select(p => p.Outline)]).ToList();
        var plan = RotaryFinish.Plan(prep, parts, MachineProfile.F2Ultra.FieldW);
        if (plan.Bands.Count == 0) return state;
        return Pages.Insert(state, [RotaryPage.Make(plan, Engrave, $"{title} on the rotary")]);
    }
}

/// <summary>The rotary engraving as a page of its own: a layer per focus height, guides at the stack's ends and the start line.</summary>
public static class RotaryPage
{
    /// <summary>The note each shape carries (so the job's check can find the page, and right-click help finds the add-on).</summary>
    public const string NoteKey = JobBuilder.RepeatsOnPurposeKey;

    public static DesignPage Make(RotaryPlan plan, Layer engrave, string name)
    {
        var layers = new List<Layer>();
        var shapes = new List<Shape>();
        string note = FormattableString.Invariant($"d={plan.DiameterMm:0.###}");
        foreach (var b in plan.Bands)
        {
            var layer = engrave with
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                Name = FormattableString.Invariant($"Rotary · focus {b.FocusOffsetMm:+0.0#;-0.0#;0.0} mm"),
                Color = Layer.Palette[b.Index % Layer.Palette.Count],
                Mode = LayerMode.Fill,
                Laser = engrave.Laser with { Passes = 1 },
                FocusOffsetMm = b.FocusOffsetMm,
                // Lines along the dowel, the rotary stepping round between them; each pass's shape filled on its own.
                FillAngleDeg = 0, Crosshatch = false, AngleStepDeg = 0, TurnEachRun = false, FillTogether = false, FillStyle = FillStyle.Lines,
                Material = engrave.Material,
            };
            layers.Add(layer);
            for (int k = 0; k < b.Passes.Count; k++)
                shapes.Add(new PathShape { Name = $"Pass {k + 1} at {b.RadiusMm:0.#} mm out", LayerId = layer.Id, Geometry = b.Passes[k] }.WithAddonData(NoteKey, note));
        }
        var guides = new List<Guide>
        {
            new(false, plan.CircumferenceMm / 2) { RotaryStart = true },
            new(true, plan.StartX),
            new(true, plan.StartX + plan.LengthMm),
        };
        return new DesignPage(name, [.. layers], [.. shapes], [.. guides]);
    }

    /// <summary>The object Ø a rotary page was made for (null: not one).</summary>
    public static double? MadeFor(DocState s)
    {
        foreach (var sh in s.Shapes)
            if (sh.AddonData.TryGetValue(NoteKey, out var v) && v.StartsWith("d=")
                && double.TryParse(v[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
        return null;
    }
}
