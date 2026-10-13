using System.Collections.Immutable;
using YTool.Core;
using YTool.Core.Imaging;

namespace YTool.Doc;

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

/// <summary>
/// A named group of shapes with one set of laser settings. Layers are real
/// objects, not colours: a shape belongs to exactly one layer, and the
/// layer order is the processing order.
/// </summary>
public sealed record Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Layer";
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
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public Guid LayerId { get; init; }
    /// <summary>Local → world (machine mm).</summary>
    public Affine Transform { get; init; } = Affine.Identity;

    public abstract Rect2 Bounds { get; }

    public abstract Shape With(Guid? layerId = null, Affine? transform = null, string? name = null, Guid? id = null);

    /// <summary>True if <paramref name="p"/> (world mm) is within <paramref name="tol"/> of the shape.</summary>
    public abstract bool HitTest(Vec2 p, double tol, bool insideCounts);

    /// <summary>True if any part of the shape lies inside <paramref name="r"/>.</summary>
    public abstract bool Touches(Rect2 r);
}

/// <summary>A vector shape: any number of line/curve subpaths.</summary>
public sealed class PathShape : Shape
{
    /// <summary>Tolerance used for hit testing and bounds.</summary>
    public const double HitTol = 0.02;

    public required PathGeometry Geometry { get; init; }

    PathGeometry? _world;
    List<FlatPath>? _flat;
    Rect2? _bounds;

    /// <summary>The geometry in machine millimetres.</summary>
    public PathGeometry World => _world ??= Geometry.Transform(Transform);

    /// <summary>World geometry flattened for hit testing.</summary>
    public List<FlatPath> Flat => _flat ??= World.Flatten(HitTol);

    public override Rect2 Bounds => _bounds ??= Rect2.Bounds(Flat.SelectMany(f => f.Points));

    public bool HasOpenSubpaths => Geometry.Subpaths.Any(s => !s.Closed);

    public override Shape With(Guid? layerId = null, Affine? transform = null, string? name = null, Guid? id = null) =>
        new PathShape
        {
            Id = id ?? Id,
            Name = name ?? Name,
            LayerId = layerId ?? LayerId,
            Transform = transform ?? Transform,
            Geometry = Geometry,
        };

    /// <summary>Bake the transform into the geometry (used by flatten-style ops).</summary>
    public PathShape Baked() => new() { Id = Id, Name = Name, LayerId = LayerId, Geometry = World };

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

/// <summary>The whole editable state. Immutable; every edit produces a new one.</summary>
public sealed record DocState(ImmutableList<Layer> Layers, ImmutableList<Shape> Shapes)
{
    public static DocState New()
    {
        var l = Layer.Default(0) with { Name = "Cut" };
        return new([l], []);
    }

    public Layer? FindLayer(Guid id) => Layers.FirstOrDefault(l => l.Id == id);
    public Shape? FindShape(Guid id) => Shapes.FirstOrDefault(s => s.Id == id);
    public int LayerIndex(Guid id) => Layers.FindIndex(l => l.Id == id);

    public IEnumerable<Shape> ShapesOn(Guid layerId) => Shapes.Where(s => s.LayerId == layerId);

    /// <summary>Shapes that can be picked on the canvas (layer visible and unlocked).</summary>
    public IEnumerable<Shape> Editable()
    {
        var ok = Layers.Where(l => l.Visible && !l.Locked).Select(l => l.Id).ToHashSet();
        return Shapes.Where(s => ok.Contains(s.LayerId));
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
