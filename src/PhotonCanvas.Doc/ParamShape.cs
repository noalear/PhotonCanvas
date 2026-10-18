using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

public enum ParamKind { Rectangle, Ellipse, Polygon, Star }

/// <summary>
/// A drawn shape that stays editable: a rectangle keeps its size and corner radius, a polygon
/// its number of sides, a star its points and how deep they go. Its outline is rebuilt from
/// these whenever they change; everything that works on paths works on it.
/// </summary>
public sealed class ParamShape : PathShape
{
    public required ParamKind Kind { get; init; }
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    /// <summary>Rectangle: corner radius, mm.</summary>
    public double CornerMm { get; init; }
    /// <summary>Polygon: sides; star: points.</summary>
    public int Sides { get; init; } = 6;
    /// <summary>Star: inner radius as a share of the outer (0.05–0.95).</summary>
    public double Inner { get; init; } = 0.5;

    public static string KindName(ParamKind k) => k switch { ParamKind.Rectangle => "Rectangle", ParamKind.Ellipse => "Ellipse", ParamKind.Polygon => "Polygon", _ => "Star" };

    /// <summary>The outline with its top-left at (0, 0).</summary>
    public static PathGeometry Build(ParamKind kind, double w, double h, double corner, int sides, double inner)
    {
        w = Math.Max(0.01, w);
        h = Math.Max(0.01, h);
        switch (kind)
        {
            case ParamKind.Rectangle:
                double r = Math.Clamp(corner, 0, Math.Min(w, h) / 2);
                return r > 1e-6 ? PathGeometry.RoundRect(0, 0, w, h, r, r) : PathGeometry.Rect(0, 0, w, h);
            case ParamKind.Ellipse:
                return PathGeometry.Ellipse(w / 2, h / 2, w / 2, h / 2);
            default:
                int corners = Math.Clamp(sides, 3, 64);
                bool star = kind == ParamKind.Star;
                double k = Math.Clamp(inner, 0.05, 0.95);
                var pts = new List<Vec2>();
                int n = star ? corners * 2 : corners;
                for (int i = 0; i < n; i++)
                {
                    double a = -Math.PI / 2 + i * 2 * Math.PI / n;
                    double rr = star && i % 2 == 1 ? k : 1;
                    pts.Add(new Vec2(Math.Cos(a) * rr, Math.Sin(a) * rr));
                }
                // Scaled so the shape's own bounds fill the box.
                var b = Rect2.Bounds(pts);
                return PathGeometry.Polyline([.. pts.Select(p => new Vec2((p.X - b.MinX) / b.Width * w, (p.Y - b.MinY) / b.Height * h))], true);
        }
    }

    public static ParamShape Create(ParamKind kind, double w, double h, double corner, int sides, double inner, Guid layerId, Affine transform, Guid? id = null, string? name = null, Guid? groupId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? KindName(kind),
        LayerId = layerId,
        Transform = transform,
        GroupId = groupId,
        Kind = kind,
        WidthMm = w,
        HeightMm = h,
        CornerMm = corner,
        Sides = sides,
        Inner = inner,
        Geometry = Build(kind, w, h, corner, sides, inner),
    };

    /// <summary>
    /// The same shape with new settings. Sizes are as seen (any stretching from the handles is
    /// folded into them), so the corners stay round and the sides even.
    /// </summary>
    public ParamShape Edit(double w, double h, double corner, int sides, double inner)
    {
        double sx = Math.Max(1e-9, Transform.ScaleX), sy = Math.Max(1e-9, Math.Abs(Transform.ScaleY));
        var unscaled = Affine.Scale(1 / sx, 1 / sy).Then(Transform);
        return Create(Kind, w, h, corner, sides, inner, LayerId, unscaled, Id, Name, GroupId);
    }

    /// <summary>The size as seen on the work area (the drawn size times any stretching).</summary>
    public (double W, double H) SeenSize => (WidthMm * Transform.ScaleX, HeightMm * Math.Abs(Transform.ScaleY));
}
