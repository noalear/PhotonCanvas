using System.Collections.Immutable;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>What a surface is: a round object lying on its side, or a raised flat piece.</summary>
public enum MarkSurfaceKind
{
    /// <summary>A pen, a dowel, a bottle or a tube lying on the bed: marks are wrapped round it.</summary>
    Round,
    /// <summary>A flat piece at its own height (a box lid, a block in a jig): marks are focused on it.</summary>
    Flat,
}

/// <summary>
/// An object on the bed that marks go onto (Machine → Surfaces). On the canvas it's a rectangle centred on
/// <see cref="Centre"/>: anything drawn inside it is marked on the object, focused on its surface, and on a
/// round object wrapped round it (the rectangle is the object's top unrolled).
/// </summary>
public sealed record MarkSurface
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Surface 1";
    public MarkSurfaceKind Kind { get; init; } = MarkSurfaceKind.Round;
    /// <summary>The middle of its rectangle on the canvas (mm): on a round object, a point on its top line.</summary>
    public Vec2 Centre { get; init; } = new(110, 110);
    /// <summary>Which way it lies: 0 = left to right (along X), 90 = front to back (along Y).</summary>
    public double AxisDeg { get; init; }
    /// <summary>How much of its length takes marks (mm).</summary>
    public double LengthMm { get; init; } = 60;
    /// <summary>Round: its diameter where the marks go (mm).</summary>
    public double DiameterMm { get; init; } = 10;
    /// <summary>Flat: how wide it is across (mm).</summary>
    public double WidthMm { get; init; } = 30;
    /// <summary>Height of its top above the bed (mm), as you'd focus on it.</summary>
    public double TopMm { get; init; } = 10;
    /// <summary>Round: how far round from the top, each way, marks may go (degrees).</summary>
    public double ReachDeg { get; init; } = DefaultReachDeg;
    /// <summary>Round: wrap the design round the surface (true), or drop it straight down onto it.</summary>
    public bool Wrap { get; init; } = true;
    /// <summary>Round: slow down where the surface slopes away, so the mark is as dark there as on the top.</summary>
    public bool EvenOut { get; init; } = true;

    public const double DefaultReachDeg = 60, MaxReachDeg = 85, MinDiameterMm = 1, MaxDiameterMm = 300, MaxTopMm = 150;

    public bool Round => Kind == MarkSurfaceKind.Round;
    public double RadiusMm => DiameterMm / 2;

    /// <summary>How wide its rectangle is on the canvas: on a round object, the arc it reaches round, unrolled.</summary>
    public double AcrossMm => Round
        ? (Wrap ? 2 * RadiusMm * Math.Clamp(ReachDeg, 1, MaxReachDeg) * Math.PI / 180 : 2 * RadiusMm * Math.Sin(Math.Clamp(ReachDeg, 1, MaxReachDeg) * Math.PI / 180))
        : WidthMm;

    /// <summary>The object as the job uses it.</summary>
    public WrapZone Zone() => new(Name, Centre, AxisDeg, Math.Max(0.1, LengthMm), Math.Max(0.1, AcrossMm), Round ? Math.Max(MinDiameterMm, DiameterMm) / 2 : 0, TopMm)
    {
        Wrap = Wrap,
        EvenOut = EvenOut,
    };

    /// <summary>A short description: "Ø 10 mm, 60 mm long, top 10 mm up".</summary>
    public string Summary => Round
        ? $"Ø {DiameterMm:0.##} mm round, {LengthMm:0.#} mm long, top {TopMm:0.##} mm up"
        : $"flat, {LengthMm:0.#} × {WidthMm:0.#} mm, top {TopMm:0.##} mm up";
}

/// <summary>
/// The objects a design's marks go onto (Machine → Surfaces), kept with the design. When anything is drawn on
/// one, the job is marked with the head's height set for each part (as on a curved surface), and everything
/// not on an object is marked at <see cref="OtherMm"/> (or on the curved surface, when that's on).
/// </summary>
public sealed record MarkSurfaces
{
    public ImmutableList<MarkSurface> Items { get; init; } = [];
    /// <summary>The height of everything that isn't on an object (mm above the bed); null = not set.</summary>
    public double? OtherMm { get; init; }
    /// <summary>How far apart the head's heights are (mm), as with a curved surface.</summary>
    public double BandMm { get; init; } = 0.2;

    /// <summary>A name not used yet: "Pen 1", "Pen 2"…</summary>
    public string NextName(string stem)
    {
        for (int i = 1; ; i++)
            if (!Items.Any(s => s.Name == $"{stem} {i}")) return $"{stem} {i}";
    }

    public SurfaceWrap Wrap() => new(Items.Select(s => s.Zone()));
}
