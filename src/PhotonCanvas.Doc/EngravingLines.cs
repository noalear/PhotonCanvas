using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>How the lines of an engraving-style picture run.</summary>
public enum EngravingStyle
{
    /// <summary>Parallel straight lines at an angle: the classic banknote shading.</summary>
    Straight,
    /// <summary>Parallel lines bent by the picture's light and dark, so they follow its forms (relief).</summary>
    Contour,
    /// <summary>Circles round a centre (guilloché rings).</summary>
    Rings,
    /// <summary>One spiral from the centre out.</summary>
    Spiral,
}

/// <summary>Settings for <see cref="EngravingLines"/>.</summary>
public sealed record EngravingOptions
{
    public EngravingStyle Style { get; init; } = EngravingStyle.Straight;
    /// <summary>Lines this far apart (mm), centre to centre.</summary>
    public double SpacingMm { get; init; } = 0.6;
    /// <summary>At black, a line is this share of the spacing wide (0–1); at white, nothing.</summary>
    public double MaxWidth { get; init; } = 0.85;
    /// <summary>Narrower than this (mm) and a line breaks off (highlights stay clean).</summary>
    public double MinWidthMm { get; init; } = 0.06;
    public double AngleDeg { get; init; } = 30;
    /// <summary>Contour: how far the lines bend at full contrast, as a share of the spacing.</summary>
    public double Bend { get; init; } = 1;
    /// <summary>Rings and spiral: the centre, as a share across and down the picture.</summary>
    public Vec2 Centre { get; init; } = new(0.5, 0.45);
}

/// <summary>
/// A picture redrawn as engraving lines (banknote style): lines that swell where the picture is
/// dark and thin away to nothing where it's light, straight, bent to its forms, in rings or a
/// spiral. The lines come out as filled outlines (ribbons), to engrave on a fill layer.
/// </summary>
public static class EngravingLines
{
    /// <summary>The ribbons for <paramref name="picture"/> as it's placed (bed mm), its tone settings applied.</summary>
    public static PathGeometry Make(ImageShape picture, EngravingOptions o)
    {
        if (!picture.Transform.IsInvertible) return PathGeometry.Empty;
        var toLocal = picture.Transform.Invert();
        double Dark(Vec2 world)
        {
            var local = toLocal.Apply(world);
            if (local.X < 0 || local.Y < 0 || local.X > picture.SizeMm.X || local.Y > picture.SizeMm.Y || !picture.InClip(local)) return 0;
            var px = picture.LocalToPixel(local);
            return 1 - Math.Clamp(picture.Tone.Apply(picture.Source.SampleLuminance(px.X, px.Y)), 0, 1);
        }
        return Make(picture.Bounds, Dark, o);
    }

    /// <summary>The ribbons over <paramref name="area"/> for a darkness function (0 white … 1 black) on the bed.</summary>
    public static PathGeometry Make(Rect2 area, Func<Vec2, double> dark, EngravingOptions o)
    {
        double s = Math.Max(0.05, o.SpacingMm), step = Math.Clamp(s / 4, 0.03, 0.15), maxW = s * Math.Clamp(o.MaxWidth, 0.05, 1);
        var inside = dark;
        dark = p => area.Contains(p) ? inside(p) : 0; // nothing past the picture's edge
        var subpaths = new List<Subpath>();
        foreach (var line in Lines(area, dark, o, s, step))
            subpaths.AddRange(Ribbons(line, dark, maxW, o.MinWidthMm));
        return new PathGeometry(subpaths);
    }

    /// <summary>The centre lines, sampled every <paramref name="step"/> mm.</summary>
    static IEnumerable<List<Vec2>> Lines(Rect2 area, Func<Vec2, double> dark, EngravingOptions o, double s, double step)
    {
        var c = new Vec2(area.MinX + area.Width * o.Centre.X, area.MinY + area.Height * o.Centre.Y);
        double r1 = s * 1.5, r2 = s * 3;
        double Smooth(Vec2 p) => (dark(p) * 2 + dark(p + new Vec2(r1, 0)) + dark(p - new Vec2(r1, 0)) + dark(p + new Vec2(0, r1)) + dark(p - new Vec2(0, r1))
            + dark(p + new Vec2(r2, r2)) + dark(p - new Vec2(r2, r2)) + dark(p + new Vec2(r2, -r2)) + dark(p - new Vec2(r2, -r2))) / 10;
        double reach = Math.Sqrt(area.Width * area.Width + area.Height * area.Height);
        switch (o.Style)
        {
            case EngravingStyle.Rings:
                for (double r = s / 2; r < reach; r += s)
                {
                    int n = Math.Max(12, (int)Math.Ceiling(2 * Math.PI * r / step));
                    var ring = new List<Vec2>(n + 1);
                    for (int i = 0; i <= n; i++) { double a = 2 * Math.PI * i / n; ring.Add(new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a))); }
                    yield return ring;
                }
                yield break;
            case EngravingStyle.Spiral:
                {
                    var spiral = new List<Vec2>();
                    double th = 0;
                    while (true)
                    {
                        double r = s * th / (2 * Math.PI);
                        if (r > reach) break;
                        spiral.Add(new Vec2(c.X + r * Math.Cos(th), c.Y + r * Math.Sin(th)));
                        th += step / Math.Max(r, s);
                    }
                    yield return spiral;
                    yield break;
                }
            default:
                {
                    double a = o.AngleDeg * Math.PI / 180;
                    var u = new Vec2(Math.Cos(a), Math.Sin(a));
                    var v = new Vec2(-u.Y, u.X);
                    var mid = area.Center;
                    for (double off = -reach / 2 + s / 2; off < reach / 2; off += s)
                    {
                        var line = new List<Vec2>();
                        for (double t = -reach / 2; t <= reach / 2; t += step)
                        {
                            var p = mid + u * t + v * off;
                            // Contour: bent towards the dark, so the lines follow the picture's forms.
                            // (By the darkness round about, so they follow the forms rather than every speck.)
                            if (o.Style == EngravingStyle.Contour) p += v * ((Smooth(p) - 0.5) * s * o.Bend);
                            line.Add(p);
                        }
                        yield return line;
                    }
                    yield break;
                }
        }
    }

    /// <summary>A line as ribbons: as wide as it's dark there, broken where it's too light (or off the picture).</summary>
    static IEnumerable<Subpath> Ribbons(List<Vec2> line, Func<Vec2, double> dark, double maxW, double minW)
    {
        var left = new List<Vec2>();
        var right = new List<Vec2>();
        Subpath? Close()
        {
            Subpath? sp = left.Count >= 2 ? PathGeometry.Polyline([.. left, .. Enumerable.Reverse(right)], true).Subpaths[0] : null;
            left.Clear();
            right.Clear();
            return sp;
        }
        for (int i = 0; i < line.Count; i++)
        {
            var p = line[i];
            var d = (i + 1 < line.Count ? line[i + 1] : p) - (i > 0 ? line[i - 1] : p);
            double len = d.Length;
            if (len < 1e-9) continue;
            var n = new Vec2(-d.Y / len, d.X / len);
            double w = dark(p) * maxW;
            if (w < minW)
            {
                if (Close() is { } sp) yield return sp;
                continue;
            }
            left.Add(p + n * (w / 2));
            right.Add(p - n * (w / 2));
        }
        if (Close() is { } last) yield return last;
    }
}
