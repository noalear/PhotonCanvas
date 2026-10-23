using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>What a layer does with its shapes.</summary>
public enum LayerMode
{
    /// <summary>Follow the outline at cutting settings.</summary>
    Cut,
    /// <summary>Follow the outline lightly (marking, scoring). Same motion as Cut, run earlier by default.</summary>
    Score,
    /// <summary>Zigzag-fill the inside of closed shapes (holes respected).</summary>
    Fill,
    /// <summary>Engrave pictures as dots (dithered), one laser pulse per dot.</summary>
    Image,
}

public enum ImageEngraveMode
{
    /// <summary>Every dot at the same power; tone comes from dot density.</summary>
    Dither,
    /// <summary>Each dot's power follows the picture's brightness (experimental on this machine).</summary>
    Grayscale,
    /// <summary>
    /// The picture is a depth map (dark = deep): engraved as thin slices one after another, each
    /// covering everything at least that deep, so the material is carved down in steps (relief).
    /// </summary>
    Slices,
}

/// <summary>The order a Cut or Score layer's paths run in.</summary>
public enum PathOrder
{
    /// <summary>Insides before outsides, then the nearest next: least travel, parts held while their holes are cut.</summary>
    Optimised,
    /// <summary>In the order they were drawn or imported.</summary>
    AsDrawn,
    /// <summary>From the back of the work area to the front, so smoke drifts over what's done, not what's still to cut (insides still first).</summary>
    BackToFront,
    /// <summary>Thin sheet metal: the sheet's heat simulated, each pass of each path cut where it's coolest, long paths in pieces, waits only when needed (see <see cref="HeatOrder"/>).</summary>
    Heat,
}

/// <summary>How a Fill layer covers the inside of its shapes.</summary>
public enum FillStyle
{
    /// <summary>Straight lines back and forth.</summary>
    Lines,
    /// <summary>Outlines stepped inward, like contour lines: follows the shape, cleaner edges.</summary>
    Offset,
}

/// <summary>Depth-slice (relief) settings of an Image layer: see <see cref="ImageEngraveMode.Slices"/>.</summary>
public sealed record DepthSlices
{
    public const int MaxCount = 255;

    /// <summary>How many slices from the surface to the deepest point (a slice per grey step at most).</summary>
    public int Count { get; init; } = 32;
    /// <summary>Light = deep instead of dark = deep.</summary>
    public bool Negative { get; init; }
    /// <summary>Lower the focus this much (mm) every <see cref="DropEvery"/> slices, to follow the floor down. 0 = off.</summary>
    public double DropMm { get; init; }
    public int DropEvery { get; init; } = 8;
    /// <summary>A cleanup pass after every this many slices, to clear debris and smooth the floor. 0 = off.</summary>
    public int CleanupEvery { get; init; }
    /// <summary>Cleanup power, speed and (MOPA) pulse settings; the source is always the layer's.</summary>
    public LaserSettings CleanupLaser { get; init; } = new() { Light = LightSource.Red, Power = 20, Speed = 2000, PulseWidthNs = 200, FrequencyKhz = 300 };
    public double CleanupLinesPerCm { get; init; } = 300;
    /// <summary>Cleanup covers the whole picture (else only the slice just done).</summary>
    public bool CleanupWhole { get; init; } = true;
    /// <summary>Deepest slice first (smaller areas first): a smoother result on some materials ("noise reduction").</summary>
    public bool DeepestFirst { get; init; }
}

/// <summary>
/// A named group of shapes with one set of laser settings. Layers are real
/// objects, not colours: a shape belongs to exactly one layer, and the
/// layer order is the processing order.
/// </summary>
public sealed record Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Layer";
    /// <summary>
    /// A sub-layer: another operation on its parent layer's shapes (an outline after a fill, a
    /// second fill at another angle…). It has no shapes of its own. Null for a normal layer.
    /// </summary>
    public Guid? ParentId { get; init; }
    /// <summary>Display colour, 0xRRGGBB.</summary>
    public uint Color { get; init; } = 0x3B82F6;
    /// <summary>Shown on the canvas.</summary>
    public bool Visible { get; init; } = true;
    /// <summary>Shapes cannot be selected or edited on the canvas.</summary>
    public bool Locked { get; init; }
    /// <summary>Included when the job is sent. Off = reference/guide layer.</summary>
    public bool Output { get; init; } = true;
    public LayerMode Mode { get; init; } = LayerMode.Cut;
    public LaserSettings Laser { get; init; } = LaserSettings.Blue(70, 40);
    /// <summary>Fill only: scan lines per centimetre.</summary>
    public double LinesPerCm { get; init; } = 100;
    /// <summary>Image only: dots per inch (pitch = 25.4 / dpi mm).</summary>
    public double Dpi { get; init; } = 180;
    /// <summary>Image only: how long each dot fires, µs (multiple of 10).</summary>
    public int DotUs { get; init; } = 200;
    /// <summary>Image only: how grey tones become dots.</summary>
    public DitherMethod Dither { get; init; } = DitherMethod.Jarvis;
    /// <summary>Image only: dithered dots, or grey levels as varying power (experimental).</summary>
    public ImageEngraveMode ImageMode { get; init; } = ImageEngraveMode.Dither;
    /// <summary>Image grayscale only: power (%) used for the lightest burned tone.</summary>
    public double GrayMinPower { get; init; } = 10;
    /// <summary>Halftone and line screen: screen cells per inch (coarser = fewer, bigger dots).</summary>
    public double HalftoneCpi { get; init; } = 30;
    /// <summary>Halftone and line screen: screen angle, degrees.</summary>
    public double HalftoneAngleDeg { get; init; } = 45;
    /// <summary>Fill lines and depth slices: dot width correction, mm (every line shorter by about the beam width; 0 = off).</summary>
    public double DotWidthMm { get; init; }

    /// <summary>Cut/Score: beam width (mm). Outlines move out by half of it so parts keep their size; holes shrink to keep theirs.</summary>
    public double KerfMm { get; init; }
    /// <summary>Cut: uncut bridges left in each closed outline so parts stay in the sheet (0 = none).</summary>
    public int TabCount { get; init; }
    /// <summary>Cut: width of each bridge (mm).</summary>
    public double TabWidthMm { get; init; } = 0.5;
    /// <summary>Cut: keep cutting this far past the start of a closed outline so it fully closes (mm).</summary>
    public double OvercutMm { get; init; }
    /// <summary>Cut: start each closed outline with a short entry line of this length, outside the part (mm).</summary>
    public double LeadInMm { get; init; }
    /// <summary>Fill: scan-line direction in degrees (0 = left–right).</summary>
    public double FillAngleDeg { get; init; }
    /// <summary>Fill: add a second pass at <see cref="CrossAngleDeg"/> to the first.</summary>
    public bool Crosshatch { get; init; }
    /// <summary>Fill crosshatch: the second pass's angle from the first (degrees).</summary>
    public double CrossAngleDeg { get; init; } = 90;
    /// <summary>Fill lines: pause before each line (ms), against heat build-up. 0 = none.</summary>
    public int LineDwellMs { get; init; }
    /// <summary>Fills and pictures: burn the lines spread apart, never one straight after its neighbour, against heat build-up.</summary>
    public bool SpreadLines { get; init; }
    /// <summary>Spread out in no set pattern (see FillBlock.Scatter): no repeating rows.</summary>
    public bool SpreadScatter { get; init; }
    /// <summary>
    /// Pictures: how this material really takes these settings, measured with a tone test; each
    /// picture's greys are corrected through it so they come out as light as they look. Null = as sent.
    /// </summary>
    public Core.Imaging.ToneCurve? MaterialTone { get; init; }
    /// <summary>Fill: trace each shape's outline after filling it, for clean edges.</summary>
    public bool OutlineAfter { get; init; }
    /// <summary>Fill lines: scan from the other side (bottom to top).</summary>
    public bool ScanReverse { get; init; }
    /// <summary>Cut: tabs are cut at this power (%) instead of left uncut: thin bridges that snap out easily. 0 = uncut tabs.</summary>
    public double TabPowerPct { get; init; }
    /// <summary>Cut/Score: the order the paths run in.</summary>
    public PathOrder Order { get; init; }
    /// <summary>Spread-the-heat order: the sheet (a <see cref="SheetMetal"/> name), its thickness (mm), the warming to stay under (°C) and the longest piece a path is cut in (mm, 0 = whole).</summary>
    public string HeatSheet { get; init; } = "Aluminium";
    public double HeatThicknessMm { get; init; } = 0.2;
    public double HeatLimitC { get; init; } = 30;
    public double HeatPieceMm { get; init; } = 15;
    /// <summary>Spread-the-heat order: simulate on cells half the size (closer to small holes and the metal between them, much slower to plan).</summary>
    public bool HeatFine { get; init; }
    /// <summary>Cut/Score: each closed outline starts at its point nearest where the laser already is (less travel).</summary>
    public bool StartNearest { get; init; }
    /// <summary>Cut/Score: lines lying on top of each other are cut once (shared edges of shapes side by side).</summary>
    public bool RemoveOverlaps { get; init; }
    /// <summary>Cut: end each closed outline with a short exit line of this length, outside the part (mm).</summary>
    public double LeadOutMm { get; init; }
    /// <summary>Fill: lines or stepped-in outlines.</summary>
    public FillStyle FillStyle { get; init; }
    /// <summary>Fill lines: burn in both directions (off: always the same way, back unlit).</summary>
    public bool Bidirectional { get; init; } = true;
    /// <summary>Fill lines: all the layer's shapes in one scan instead of shape by shape (fewer jumps; overlaps cancel out).</summary>
    public bool FillTogether { get; init; }
    /// <summary>Cut/Score: perforate: burn this many mm, then skip <see cref="PerforateGapMm"/> (0 = a continuous line).</summary>
    public double PerforateMm { get; init; }
    public double PerforateGapMm { get; init; } = 1;
    /// <summary>Cut/Score: dot mode: a single pulse (of <see cref="DotUs"/>) every this many mm instead of a line (0 = off).</summary>
    public double DotModeMm { get; init; }
    /// <summary>Picture, threshold: power ramps up over this distance from each edge, for sloped sides (rubber stamps). 0 = off.</summary>
    public double RampMm { get; init; }
    /// <summary>Picture: engrave the file's own pixels one for one (its size sets the resolution).</summary>
    public bool PassThrough { get; init; }
    /// <summary>Fill lines and depth slices: turn the scan lines this much more on every pass or slice (degrees, 0 = off).</summary>
    public double AngleStepDeg { get; init; }
    /// <summary>Also carry on turning from one run of the job to the next (by <see cref="AngleStepDeg"/>).</summary>
    public bool TurnEachRun { get; init; }
    /// <summary>How many times the layer has been run (counted for <see cref="TurnEachRun"/>).</summary>
    public int RunCount { get; init; }
    /// <summary>Move the focus this far into the material for this layer (mm): + lowers the head, − raises it.</summary>
    public double FocusOffsetMm { get; init; }
    /// <summary>Image layers in <see cref="ImageEngraveMode.Slices"/> mode.</summary>
    public DepthSlices Slices { get; init; } = new();

    /// <summary>Extra turn of the scan lines for this run (<see cref="TurnEachRun"/>), degrees.</summary>
    public double RunTurnDeg => TurnEachRun ? RunCount * AngleStepDeg * Math.Max(1, Laser.Passes) * (Mode == LayerMode.Image && ImageMode == ImageEngraveMode.Slices ? Math.Max(1, Slices.Count) : 1) : 0;
    /// <summary>Fill: extend each scan line this far beyond the shape with the laser off, so the beam is at speed when it burns (mm).</summary>
    public double OverscanMm { get; init; }

    /// <summary>Cut/Score: lower the head by this much before each pass after the first (mm, 0 = off). For thick material.</summary>
    public double StepDownMm { get; init; }
    /// <summary>Cut/Score with step-down: lower the head by this much before the first pass too (mm).</summary>
    public double StepDownFirstMm { get; init; }
    /// <summary>Cut/Score on the fiber laser: trace small circles along the path (wider, smoother marks).</summary>
    public bool Wobble { get; init; }
    /// <summary>Wobble circle diameter (mm).</summary>
    public double WobbleDiameterMm { get; init; } = 0.05;
    /// <summary>How far the circles advance per loop (mm).</summary>
    public double WobbleSpacingMm { get; init; } = 0.015;

    /// <summary>The material this layer is for, and the library recipe its settings came from (null = none chosen).</summary>
    public Materials.MaterialRef? Material { get; init; }

    /// <summary>
    /// The material is highly reflective (bare copper, brass, aluminium, silver, gold, chrome,
    /// mirror finishes): the MOPA keeps out of its protected frequency range, and the job reminds
    /// you how to keep reflections down. Set from the material's tag, or by hand.
    /// </summary>
    public bool Reflective { get; init; }

    /// <summary>A palette that stays distinguishable on dark and light canvases.</summary>
    public static readonly IReadOnlyList<uint> Palette =
    [
        0xEF4444, 0x3B82F6, 0x22C55E, 0xF59E0B, 0xA855F7, 0x06B6D4, 0xEC4899, 0x84CC16,
        0xF97316, 0x6366F1, 0x14B8A6, 0xE11D48,
    ];

    public static Layer Default(int index, LayerMode mode = LayerMode.Cut) => new()
    {
        Name = $"Layer {index + 1}",
        Color = Palette[index % Palette.Count],
        Mode = mode,
        Laser = mode == LayerMode.Image ? LaserSettings.Blue(90, 80) :
            mode == LayerMode.Fill ? LaserSettings.Blue(30, 400) :
            mode == LayerMode.Score ? LaserSettings.Blue(20, 200) : LaserSettings.Blue(70, 40),
    };
}

/// <summary>
/// Something on the canvas. Immutable: edits make a new instance, which keeps
/// undo cheap and lets world-space caches be computed once.
/// </summary>
public abstract class Shape
{
    Guid _id = Guid.NewGuid();
    string _name = "";
    Guid _layerId;
    Affine _transform = Affine.Identity;
    Guid? _groupId;
    bool _locked;

    public Guid Id { get => _id; init => _id = value; }
    /// <summary>Can't be picked or changed on the canvas (until unlocked); still marked.</summary>
    public bool Locked { get => _locked; init => _locked = value; }

    /// <summary>
    /// For copies that each mark a different item: this copy's text and codes are this many steps
    /// ahead (serial + n × step, spreadsheet row + n). 0 = the design's own values.
    /// </summary>
    public int VarOffset { get; private set; }
    public string Name { get => _name; init => _name = value; }
    public Guid LayerId { get => _layerId; init => _layerId = value; }
    /// <summary>Local → world (machine mm).</summary>
    public Affine Transform { get => _transform; init => _transform = value; }
    /// <summary>Shapes with the same group id are selected and moved together.</summary>
    public Guid? GroupId { get => _groupId; init => _groupId = value; }

    public abstract Rect2 Bounds { get; }

    /// <summary>A copy with some common properties changed (everything else is kept).</summary>
    public Shape With(Guid? layerId = null, Affine? transform = null, string? name = null, Guid? id = null)
    {
        var c = (Shape)MemberwiseClone();
        c._id = id ?? _id;
        c._name = name ?? _name;
        c._layerId = layerId ?? _layerId;
        c._transform = transform ?? _transform;
        if (transform is not null) c.ResetCaches();
        return c;
    }

    /// <summary>A copy that is locked or unlocked.</summary>
    public Shape WithLocked(bool locked)
    {
        var c = (Shape)MemberwiseClone();
        c._locked = locked;
        return c;
    }

    /// <summary>
    /// A shape rebuilt from this one (new words, filled-in fields, new settings) keeps what was set
    /// on it by hand: its lock, its copy number, and its tabs and start point (kept where they were
    /// on the work area, even if the rebuild changed its own coordinates).
    /// </summary>
    protected internal T Keep<T>(T rebuilt) where T : Shape
    {
        rebuilt._locked = _locked;
        rebuilt.VarOffset = VarOffset;
        if (this is PathShape from && rebuilt is PathShape to && (from.ManualTabs.Count > 0 || from.StartPoint is not null))
        {
            bool same = Transform == rebuilt.Transform;
            Vec2 Move(Vec2 p) => same || !rebuilt.Transform.IsInvertible ? p : rebuilt.Transform.Invert().Apply(Transform.Apply(p));
            to.ManualTabs = [.. from.ManualTabs.Select(Move)];
            to.StartPoint = from.StartPoint is { } sp ? Move(sp) : null;
        }
        return rebuilt;
    }

    /// <summary>A copy whose fields are <paramref name="n"/> steps ahead (see <see cref="VarOffset"/>).</summary>
    public Shape WithVarOffset(int n)
    {
        var c = (Shape)MemberwiseClone();
        c.VarOffset = Math.Max(0, n);
        return c;
    }

    /// <summary>A copy in group <paramref name="groupId"/> (null = ungrouped).</summary>
    public Shape WithGroup(Guid? groupId)
    {
        var c = (Shape)MemberwiseClone();
        c._groupId = groupId;
        return c;
    }

    /// <summary>Forget anything computed from the transform (called on copies).</summary>
    protected abstract void ResetCaches();

    /// <summary>True if <paramref name="p"/> (world mm) is within <paramref name="tol"/> of the shape.</summary>
    public abstract bool HitTest(Vec2 p, double tol, bool insideCounts);

    /// <summary>True if any part of the shape lies inside <paramref name="r"/>.</summary>
    public abstract bool Touches(Rect2 r);
}

/// <summary>A vector shape: any number of line/curve subpaths.</summary>
public class PathShape : Shape
{
    /// <summary>Tolerance used for hit testing and bounds.</summary>
    public const double HitTol = 0.02;

    public required PathGeometry Geometry { get; init; }

    /// <summary>Tabs placed by hand on this shape's outlines (the shape's own mm, so they move with it): used instead of evenly spaced ones on a Cut layer with tabs.</summary>
    public ImmutableList<Vec2> ManualTabs { get; internal set; } = [];

    /// <summary>Where cutting starts on this shape's outline (the shape's own mm); null = wherever the cut order picks.</summary>
    public Vec2? StartPoint { get; internal set; }

    /// <summary>The hand-placed tabs on the work area.</summary>
    public IEnumerable<Vec2> TabsWorld => ManualTabs.Select(Transform.Apply);

    /// <summary>The chosen start point on the work area.</summary>
    public Vec2? StartWorld => StartPoint is { } p ? Transform.Apply(p) : null;

    PathGeometry? _world;
    List<FlatPath>? _flat;
    Rect2? _bounds;

    /// <summary>The geometry in machine millimetres.</summary>
    public PathGeometry World => _world ??= Geometry.Transform(Transform);

    /// <summary>World geometry flattened for hit testing.</summary>
    public List<FlatPath> Flat => _flat ??= World.Flatten(HitTol);

    public override Rect2 Bounds => _bounds ??= Rect2.Bounds(Flat.SelectMany(f => f.Points));

    public bool HasOpenSubpaths => Geometry.Subpaths.Any(s => !s.Closed);

    protected override void ResetCaches()
    {
        _world = null;
        _flat = null;
        _bounds = null;
    }

    /// <summary>The same shape with these hand-placed tabs (its own mm).</summary>
    public PathShape WithTabs(ImmutableList<Vec2> tabs)
    {
        var c = (PathShape)MemberwiseClone();
        c.ManualTabs = tabs;
        return c;
    }

    /// <summary>The same shape starting its cut at this point (its own mm), or anywhere (null).</summary>
    public PathShape WithStart(Vec2? start)
    {
        var c = (PathShape)MemberwiseClone();
        c.StartPoint = start;
        return c;
    }

    /// <summary>Bake the transform into the geometry (used by flatten-style ops). Text becomes plain paths.</summary>
    public PathShape Baked() => new() { Id = Id, Name = Name, LayerId = LayerId, GroupId = GroupId, Geometry = World };

    public override bool HitTest(Vec2 p, double tol, bool insideCounts)
    {
        if (!Bounds.Inflate(tol).Contains(p)) return false;
        foreach (var f in Flat)
            for (int i = 1; i < f.Points.Count; i++)
                if (Geom.SegmentDistance(p, f.Points[i - 1], f.Points[i]) <= tol) return true;
        if (!insideCounts) return false;
        int crossings = 0;
        foreach (var f in Flat.Where(f => f.Closed))
            if (Geom.Inside(f.Points, p)) crossings++;
        return crossings % 2 == 1;
    }

    public override bool Touches(Rect2 r)
    {
        if (!Bounds.Intersects(r)) return false;
        foreach (var f in Flat)
        {
            for (int i = 1; i < f.Points.Count; i++)
                if (SegmentTouches(f.Points[i - 1], f.Points[i], r)) return true;
            if (f.Points.Count == 1 && r.Contains(f.Points[0])) return true;
        }
        return false;
    }

    /// <summary>Liang–Barsky: does segment ab cross rectangle r?</summary>
    static bool SegmentTouches(Vec2 a, Vec2 b, Rect2 r)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        return Clip(-dx, a.X - r.MinX) && Clip(dx, r.MaxX - a.X) && Clip(-dy, a.Y - r.MinY) && Clip(dy, r.MaxY - a.Y);
    }
}

/// <summary>A guideline: vertical at X = <see cref="At"/>, or horizontal at Y = <see cref="At"/> (mm).</summary>
public sealed record Guide(bool Vertical, double At);

/// <summary>The whole editable state. Immutable; every edit produces a new one.</summary>
public sealed record DocState(ImmutableList<Layer> Layers, ImmutableList<Shape> Shapes)
{
    /// <summary>What fills in the fields of text and codes (serial number, spreadsheet).</summary>
    public VariableData Vars { get; init; } = new();
    /// <summary>Guidelines dragged out from the rulers (shapes snap to them; never marked).</summary>
    public ImmutableList<Guide> Guides { get; init; } = [];
    /// <summary>The curved piece the design is marked on (off by default).</summary>
    public CurvedSurface Curve { get; init; } = new();
    /// <summary>The name of the page on screen.</summary>
    public string PageName { get; init; } = "Page 1";
    /// <summary>Where the page on screen sits among all the pages.</summary>
    public int PageIndex { get; init; }
    /// <summary>The project's other pages, in order (without the one on screen). See <see cref="Pages"/>.</summary>
    public ImmutableList<DesignPage> OtherPages { get; init; } = [];

    public static DocState New()
    {
        var l = Layer.Default(0) with { Name = "Cut" };
        return new([l], []);
    }

    public Layer? FindLayer(Guid id) => Layers.FirstOrDefault(l => l.Id == id);
    public Shape? FindShape(Guid id) => Shapes.FirstOrDefault(s => s.Id == id);
    public int LayerIndex(Guid id) => Layers.FindIndex(l => l.Id == id);

    public IEnumerable<Shape> ShapesOn(Guid layerId) => Shapes.Where(s => s.LayerId == layerId);

    /// <summary>The shapes a layer processes: its own, or for a sub-layer its parent's.</summary>
    public IEnumerable<Shape> ShapesFor(Layer l) => ShapesOn(Owner(l.Id));

    /// <summary>The layer that holds the shapes for <paramref name="layerId"/> (its parent, for a sub-layer).</summary>
    public Guid Owner(Guid layerId) => FindLayer(layerId) is { ParentId: { } p } && FindLayer(p) is not null ? p : layerId;

    /// <summary>The sub-layers of a layer, in order.</summary>
    public IEnumerable<Layer> SubLayers(Guid layerId) => Layers.Where(l => l.ParentId == layerId);

    /// <summary>Shapes that can be picked on the canvas (layer visible and unlocked).</summary>
    public IEnumerable<Shape> Editable()
    {
        var ok = Layers.Where(l => l.Visible && !l.Locked).Select(l => l.Id).ToHashSet();
        return Shapes.Where(s => ok.Contains(s.LayerId) && !s.Locked);
    }

    public Rect2 Bounds(IEnumerable<Guid> ids)
    {
        var set = ids as ISet<Guid> ?? ids.ToHashSet();
        var r = Rect2.Empty;
        foreach (var s in Shapes)
            if (set.Contains(s.Id)) r = r.Union(s.Bounds);
        return r;
    }
}
