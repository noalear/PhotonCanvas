using System.Collections.Immutable;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// The curved piece a design is marked on: measured heights (or a cylinder typed in), how they're
/// fitted, and how finely the head steps. Kept with the design. When it's on, every job is marked
/// in height bands that follow it.
/// </summary>
public sealed record CurvedSurface
{
    public bool On { get; init; }
    public SurfaceShape Shape { get; init; } = SurfaceShape.Freeform;
    /// <summary>Measured heights (mm above the bed). Empty with a typed cylinder.</summary>
    public ImmutableList<SurfacePoint> Points { get; init; } = [];
    /// <summary>Where the heights were measured (the design should stay inside it).</summary>
    public Rect2 Area { get; init; } = Rect2.Empty;
    public double Smoothing { get; init; } = 0.3;
    /// <summary>Slats round the whole object (prism shape).</summary>
    public int Sides { get; init; } = 12;
    public double BandMm { get; init; } = 0.2;

    /// <summary>A cylinder or prism typed in instead of measured: its axis on the bed, top height and diameter.</summary>
    public bool Typed { get; init; }
    public double AxisDeg { get; init; } = 90;
    /// <summary>A point the axis passes through, on the bed (mm).</summary>
    public Vec2 AxisPoint { get; init; } = new(110, 110);
    public double TopMm { get; init; } = 50;
    public double DiameterMm { get; init; } = 80;

    /// <summary>The fitted surface, or null when there isn't enough to go on.</summary>
    public Surface? Build()
    {
        if (Typed)
        {
            double t = AxisDeg * Math.PI / 180;
            double offset = -AxisPoint.X * Math.Sin(t) + AxisPoint.Y * Math.Cos(t);
            return Surface.FromCylinder(AxisDeg, offset, TopMm, DiameterMm, Shape == SurfaceShape.Prism ? Sides : 0);
        }
        if (Points.Count < 3) return null;
        try { return Surface.Fit(Points, Shape, Smoothing, Sides); }
        catch (ArgumentException) { return Surface.Fit(Points, SurfaceShape.Freeform, Smoothing); }
    }
}
