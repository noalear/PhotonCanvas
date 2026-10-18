using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// An imported picture: the original file bytes plus the decoded pixels.
/// Shared (immutably) by every copy of the image on the canvas.
/// </summary>
public sealed class ImageSource
{
    readonly Lazy<RasterImage> _pixels;
    readonly Lazy<float[]> _luma;

    /// <summary>The file as imported (kept so saving loses nothing).</summary>
    public byte[] Encoded { get; }
    public int Width => Pixels.Width;
    public int Height => Pixels.Height;
    public RasterImage Pixels => _pixels.Value;
    /// <summary>Brightness per pixel, 0 black … 1 white (transparent = white).</summary>
    public float[] Luminance => _luma.Value;

    ImageSource(byte[] encoded, RasterImage? decoded)
    {
        Encoded = encoded;
        _pixels = decoded is null ? new(() => RasterImage.Decode(encoded)) : new(decoded);
        _luma = new(() => Pixels.Luminance());
    }

    /// <exception cref="FormatException">The bytes are not a readable image.</exception>
    public static ImageSource FromEncoded(byte[] encoded) => new(encoded, RasterImage.Decode(encoded));

    /// <summary>Bilinear brightness at pixel coordinates (pixel centres at +0.5). Outside = white.</summary>
    public float SampleLuminance(double px, double py)
    {
        int w = Width, h = Height;
        double x = px - 0.5, y = py - 0.5;
        if (px < 0 || py < 0 || px > w || py > h) return 1f;
        int x0 = Math.Clamp((int)Math.Floor(x), 0, w - 1), y0 = Math.Clamp((int)Math.Floor(y), 0, h - 1);
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double tx = Math.Clamp(x - x0, 0, 1), ty = Math.Clamp(y - y0, 0, 1);
        var l = Luminance;
        double a = l[y0 * w + x0] * (1 - tx) + l[y0 * w + x1] * tx;
        double b = l[y1 * w + x0] * (1 - tx) + l[y1 * w + x1] * tx;
        return (float)(a * (1 - ty) + b * ty);
    }
}

/// <summary>
/// A picture on the canvas. In local space it covers (0, 0)–<see cref="SizeMm"/>;
/// <see cref="Shape.Transform"/> places, scales and rotates it.
/// </summary>
public sealed class ImageShape : Shape
{
    public required ImageSource Source { get; init; }
    public required Vec2 SizeMm { get; init; }
    /// <summary>Brightness/contrast/gamma/invert used when engraving.</summary>
    public ToneAdjust Tone { get; init; } = ToneAdjust.None;
    /// <summary>Only the part inside this outline (the picture's own mm) is shown and engraved; null = all of it.</summary>
    public PathGeometry? Clip { get; init; }

    /// <summary>Is this local point (mm) inside the clip outline (or is there none)?</summary>
    public bool InClip(Vec2 local)
    {
        if (Clip is null) return true;
        _clipFlat ??= [.. Clip.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points)];
        int n = 0;
        foreach (var poly in _clipFlat) if (Geom.Inside(poly, local)) n++;
        return n % 2 == 1;
    }

    List<List<Vec2>>? _clipFlat;

    /// <summary>The same picture clipped to an outline given in world mm (null removes the clip).</summary>
    public ImageShape WithClip(PathGeometry? world) => new()
    {
        Id = Id, Name = Name, LayerId = LayerId, GroupId = GroupId, Transform = Transform, Source = Source, SizeMm = SizeMm, Tone = Tone,
        Clip = world is null || !Transform.IsInvertible ? null : world.Transform(Transform.Invert()),
    };

    Vec2[]? _corners;
    Rect2? _bounds;

    /// <summary>World corners: top-left, top-right, bottom-right, bottom-left.</summary>
    public Vec2[] Corners => _corners ??=
    [
        Transform.Apply(new(0, 0)), Transform.Apply(new(SizeMm.X, 0)),
        Transform.Apply(SizeMm), Transform.Apply(new(0, SizeMm.Y)),
    ];

    public override Rect2 Bounds => _bounds ??= Rect2.Bounds(Corners);

    /// <summary>Default physical size: the file's pixels at 254 dpi (0.1 mm each), capped to fit the work area.</summary>
    public static Vec2 DefaultSize(ImageSource src, double maxMm)
    {
        double w = src.Width * 0.1, h = src.Height * 0.1;
        double s = Math.Min(1, maxMm / Math.Max(w, h));
        return new(w * s, h * s);
    }

    protected override void ResetCaches()
    {
        _corners = null;
        _bounds = null;
    }

    public ImageShape WithTone(ToneAdjust tone) => new()
    {
        Id = Id, Name = Name, LayerId = LayerId, GroupId = GroupId, Transform = Transform, Source = Source, SizeMm = SizeMm, Tone = tone, Clip = Clip,
    };

    /// <summary>Local mm → pixel coordinates.</summary>
    public Vec2 LocalToPixel(Vec2 local) => new(local.X / SizeMm.X * Source.Width, local.Y / SizeMm.Y * Source.Height);

    /// <summary>Pixel → world mm (for placing traced paths exactly over the image).</summary>
    public Affine PixelToWorld => Affine.Scale(SizeMm.X / Source.Width, SizeMm.Y / Source.Height).Then(Transform);

    public override bool HitTest(Vec2 p, double tol, bool insideCounts) =>
        Bounds.Inflate(tol).Contains(p) && (Geom.Inside(Corners, p) || EdgeDistance(p) <= tol);

    double EdgeDistance(Vec2 p)
    {
        var c = Corners;
        double d = double.PositiveInfinity;
        for (int i = 0; i < 4; i++) d = Math.Min(d, Geom.SegmentDistance(p, c[i], c[(i + 1) % 4]));
        return d;
    }

    public override bool Touches(Rect2 r)
    {
        if (!Bounds.Intersects(r)) return false;
        var c = Corners;
        if (c.Any(r.Contains) || Geom.Inside(c, r.Center)) return true;
        // Any rectangle corner inside the image, or any image edge crossing the rectangle.
        Vec2[] rc = [r.TopLeft, new(r.MaxX, r.MinY), r.BottomRight, new(r.MinX, r.MaxY)];
        if (rc.Any(p => Geom.Inside(c, p))) return true;
        var outline = PathGeometry.Polyline(c, true);
        return new PathShape { Geometry = outline }.Touches(r);
    }
}
