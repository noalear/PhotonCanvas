using Avalonia.Media;
using PhotonCanvas.Addon.HouseEditor;
using SkiaSharp;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>How a map is drawn as a battle map picture.</summary>
public sealed record TownPictureOptions
{
    /// <summary>Pixels to a square (as asked: <see cref="TownPicture.FitPx"/> may make it fewer for a very big map).</summary>
    public int PxPerSquare { get; init; } = 100;
    public bool Grid { get; init; } = true;
    public bool Labels { get; init; } = true;
    /// <summary>Each building's name over its roof.</summary>
    public bool Names { get; init; }
    /// <summary>Only these squares (the far sides just outside), or the whole map.</summary>
    public (int X0, int Y0, int X1, int Y1)? Box { get; init; }
    public int Seed { get; init; } = 1;
}

/// <summary>
/// The map drawn from above as a battle map for a virtual table or for printing: every square of ground
/// with its own texture (grass, cobbles, flagstones, dirt, water, fields, sand, rock), the buildings'
/// roofs with their two (or four) slopes lit from the top left and their tiles, thatch or shingles in
/// rows, shadows, walls, fences and hedges, trees, wells, carts and lamps, the street names and the grid.
/// </summary>
public static class TownPicture
{
    /// <summary>The biggest picture made, each way (pixels), and in all.</summary>
    public const int MaxSide = 16000;
    public const long MaxPixels = 80_000_000;

    /// <summary>The pixels to a square that fit a picture <paramref name="across"/> × <paramref name="down"/> squares, as near as can be to what's asked.</summary>
    public static int FitPx(int across, int down, int want)
    {
        int px = Math.Clamp(want, 8, 400);
        while (px > 8 && ((long)across * px > MaxSide || (long)down * px > MaxSide || (long)across * down * px * px > MaxPixels)) px--;
        return px;
    }

    /// <summary>The picture, encoded as its file's name says (PNG, JPEG or WebP).</summary>
    public static byte[] Encode(CityMap m, TownPictureOptions o, string fileName)
    {
        using var bmp = Render(m, o);
        using var img = SKImage.FromBitmap(bmp);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var (format, quality) = ext switch
        {
            ".jpg" or ".jpeg" => (SKEncodedImageFormat.Jpeg, 90),
            ".webp" => (SKEncodedImageFormat.Webp, 90),
            _ => (SKEncodedImageFormat.Png, 100),
        };
        using var data = img.Encode(format, quality);
        return data.ToArray();
    }

    public static SKBitmap Render(CityMap m, TownPictureOptions o)
    {
        m = m.Tidied();
        // The ground with the roads laid on it, the water left under their bridges.
        var ground = m.Paved(overWater: false);
        var (x0, y0, x1, y1) = o.Box ?? (0, 0, m.Across, m.Down);
        x0 = Math.Clamp(x0, 0, m.Across - 1); y0 = Math.Clamp(y0, 0, m.Down - 1);
        x1 = Math.Clamp(x1, x0 + 1, m.Across); y1 = Math.Clamp(y1, y0 + 1, m.Down);
        int px = FitPx(x1 - x0, y1 - y0, o.PxPerSquare);
        var bmp = new SKBitmap((x1 - x0) * px, (y1 - y0) * px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(0x6B, 0x55, 0x3C));
        // Everything is drawn in squares: one unit is a square.
        c.Scale(px);
        c.Translate(-x0, -y0);
        var view = new SKRect(x0 - 1, y0 - 1, x1 + 1, y1 + 1);
        bool In(double x, double y, double r = 1.5) => x > view.Left - r && x < view.Right + r && y > view.Top - r && y < view.Bottom + r;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

        // The ground: each square's colour first, then its marks (grass reaching a little over its edges).
        for (int y = Math.Max(0, y0 - 1); y < Math.Min(m.Down, y1 + 1); y++)
            for (int x = Math.Max(0, x0 - 1); x < Math.Min(m.Across, x1 + 1); x++)
            {
                var g = ground.GroundAt(x, y);
                double v = Noise(x * 0.35, y * 0.35, o.Seed) * 0.12 - 0.06;
                fill.Color = Shade(Base(g), 1 + v);
                c.DrawRect(x - 0.01f, y - 0.01f, 1.02f, 1.02f, fill);
            }
        for (int y = Math.Max(0, y0 - 1); y < Math.Min(m.Down, y1 + 1); y++)
            for (int x = Math.Max(0, x0 - 1); x < Math.Min(m.Across, x1 + 1); x++)
                GroundMarks(c, ground, x, y, o.Seed, fill, line);
        foreach (var site in TownPieces.Bridges(m)) DrawBridge(c, site, fill, line, view);

        // Shadows: buildings by their height, walls, trees.
        using var shadow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 0x58), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.05f * px, false) };
        foreach (var b in m.Buildings)
        {
            var (w, d) = CityFootprint.Size(b);
            var (bx, by) = CityFootprint.Centre(b);
            if (!In(bx, by, Math.Max(w, d))) continue;
            float off = (float)(0.18 + 0.12 * Math.Max(1, b.Design.Levels.Count));
            if (CityFootprint.OnGrid(b)) foreach (var (x, y) in CityFootprint.Cells(b)) c.DrawRect(x + off * 0.7f, y + off, 1.02f, 1.02f, shadow);
            else
            {
                using var path = new SKPath();
                var corners = CityFootprint.Corners(b);
                path.MoveTo((float)(corners[0].X + off * 0.7f), (float)(corners[0].Y + off));
                for (int i = 1; i < 4; i++) path.LineTo((float)(corners[i].X + off * 0.7f), (float)(corners[i].Y + off));
                path.Close();
                c.DrawPath(path, shadow);
            }
        }
        foreach (var p in m.Props.Where(p => p.Kind is PropKind.Tree or PropKind.Pine or PropKind.Bush or PropKind.Statue or PropKind.Well && In(p.X, p.Y)))
        {
            float r = (float)(Radius(p) * (p.Kind == PropKind.Statue ? 0.5 : 1));
            c.DrawCircle((float)p.X + r * 0.35f, (float)p.Y + r * 0.5f, r, shadow);
        }
        foreach (var p in m.Props.Where(p => Prop.Footprint(p.Kind) is not null && In(p.X, p.Y)))
        {
            // The town's bigger pieces: a guard tower's long shadow, a stage's or a stall's shorter one.
            var (fw, fd) = Prop.Footprint(p.Kind)!.Value;
            float w = (float)(fw * p.Size), d = (float)(fd * p.Size), off = p.Kind switch { PropKind.GuardTower => 0.9f, PropKind.Stage or PropKind.Stall or PropKind.Bar or PropKind.Fountain => 0.3f, PropKind.Gallows => 0.7f, _ => 0.12f };
            c.Save();
            c.Translate((float)p.X + off * 0.7f, (float)p.Y + off);
            c.RotateDegrees((float)p.TurnDeg);
            c.DrawRect(-w / 2, -d / 2, w, d, shadow);
            c.Restore();
        }

        // Fences, hedges and walls, open where the roads go through, with a town wall's turrets and gatehouses.
        DrawWalls(c, m, fill, line);

        // The buildings' roofs.
        foreach (var b in m.Buildings)
        {
            var (w, d) = CityFootprint.Size(b);
            var (bx, by) = CityFootprint.Centre(b);
            if (In(bx, by, Math.Max(w, d))) DrawRoofs(c, b, fill, line);
        }

        // Trees and things.
        foreach (var p in m.Props.Where(p => In(p.X, p.Y)).OrderBy(p => p.Kind is PropKind.Tree or PropKind.Pine or PropKind.GuardTower ? 1 : 0).ThenBy(p => p.Y))
            DrawProp(c, p, fill, line);

        // The grid.
        if (o.Grid)
        {
            line.Color = new SKColor(0, 0, 0, 0x40);
            line.StrokeWidth = Math.Max(1f / px, 0.012f);
            line.StrokeCap = SKStrokeCap.Butt;
            for (int x = x0; x <= x1; x++) c.DrawLine(x, y0, x, y1, line);
            for (int y = y0; y <= y1; y++) c.DrawLine(x0, y, x1, y, line);
            line.StrokeCap = SKStrokeCap.Round;
        }

        // Names: the buildings' and the labels.
        using var text = new SKPaint { IsAntialias = true, Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold), TextAlign = SKTextAlign.Center };
        if (o.Names)
            foreach (var b in m.Buildings)
            {
                var (bx, by) = CityFootprint.Centre(b);
                Text(c, text, b.Name, (float)bx, (float)by, 0.32f);
            }
        if (o.Labels)
        {
            foreach (var (r, at, deg) in CityPlan.RoadNames(m).Where(s => In(s.At.X, s.At.Y, 6)))
            {
                c.Save();
                c.Translate((float)at.X, (float)at.Y);
                c.RotateDegrees((float)deg);
                Text(c, text, r.Name, 0, 0, (float)(CityPlan.NameSize(r) * 0.8));
                c.Restore();
            }
            foreach (var l in m.Labels.Where(l => In(l.X, l.Y, 6)))
            {
                c.Save();
                c.Translate((float)l.X, (float)l.Y);
                c.RotateDegrees((float)l.TurnDeg);
                Text(c, text, l.Text, 0, 0, (float)Math.Max(0.2, l.Size * 0.62));
                c.Restore();
            }
        }
        return bmp;
    }

    // ---- the ground ----

    static SKColor Base(Ground g) => g switch
    {
        Ground.Grass => new SKColor(0x6C, 0x93, 0x45),
        Ground.Dirt => new SKColor(0x96, 0x77, 0x52),
        Ground.Road => new SKColor(0x6A, 0x64, 0x5C),
        Ground.Paving => new SKColor(0x8E, 0x86, 0x7A),
        Ground.Sand => new SKColor(0xD6, 0xC2, 0x8E),
        Ground.Water => new SKColor(0x3C, 0x72, 0xA2),
        Ground.Rock => new SKColor(0x7A, 0x75, 0x6E),
        Ground.Field => new SKColor(0x8E, 0x76, 0x46),
        _ => new SKColor(0xB4, 0x96, 0x6C),
    };

    static void GroundMarks(SKCanvas c, CityMap m, int x, int y, int seed, SKPaint fill, SKPaint line)
    {
        var g = m.GroundAt(x, y);
        var rnd = new Random(Hash(x, y, seed));
        switch (g)
        {
            case Ground.Grass:
            {
                // Blades of grass in tufts, darker and lighter, the odd flower.
                line.StrokeWidth = 0.022f;
                for (int i = 0; i < 26; i++)
                {
                    float bx = x - 0.08f + (float)rnd.NextDouble() * 1.16f, by = y - 0.08f + (float)rnd.NextDouble() * 1.16f;
                    double light = rnd.NextDouble();
                    line.Color = light < 0.5 ? new SKColor(0x4E, 0x74, 0x30, 0xC0) : new SKColor(0x8C, 0xB0, 0x5C, 0xB0);
                    for (int k = 0; k < 3; k++)
                    {
                        double a = -Math.PI / 2 + (rnd.NextDouble() - 0.5) * 1.2, len = 0.05 + rnd.NextDouble() * 0.06;
                        c.DrawLine(bx, by, bx + (float)(Math.Cos(a) * len), by + (float)(Math.Sin(a) * len), line);
                    }
                }
                if (rnd.NextDouble() < 0.12)
                {
                    fill.Color = rnd.NextDouble() < 0.5 ? new SKColor(0xF2, 0xE6, 0x7A) : new SKColor(0xEE, 0xEE, 0xF4);
                    for (int i = 0; i < 4; i++) c.DrawCircle(x + 0.2f + (float)rnd.NextDouble() * 0.6f, y + 0.2f + (float)rnd.NextDouble() * 0.6f, 0.02f, fill);
                }
                break;
            }
            case Ground.Dirt:
            {
                // Pebbles and darker patches, wheel ruts along a track.
                for (int i = 0; i < 4; i++)
                {
                    fill.Color = new SKColor(0x7E, 0x62, 0x42, 0x50);
                    c.DrawOval(x + (float)rnd.NextDouble(), y + (float)rnd.NextDouble(), 0.12f + (float)rnd.NextDouble() * 0.12f, 0.08f + (float)rnd.NextDouble() * 0.08f, fill);
                }
                for (int i = 0; i < 14; i++)
                {
                    fill.Color = rnd.NextDouble() < 0.5 ? new SKColor(0xB4, 0x9C, 0x7A) : new SKColor(0x6E, 0x58, 0x3E);
                    c.DrawCircle(x + (float)rnd.NextDouble(), y + (float)rnd.NextDouble(), 0.012f + (float)rnd.NextDouble() * 0.018f, fill);
                }
                break;
            }
            case Ground.Road:
            {
                // Cobbles in rough rows, each rounded and lit from the top left.
                int n = 5;
                for (int j = 0; j < n; j++)
                {
                    double shift = (j % 2) * 0.5 / n;
                    for (int i = -1; i < n; i++)
                    {
                        double cx = x + (i + 0.5) / n + shift + (rnd.NextDouble() - 0.5) * 0.04, cy = y + (j + 0.5) / n + (rnd.NextDouble() - 0.5) * 0.04;
                        if (cx < x - 0.02 || cx > x + 1.02) continue;
                        float rx = (float)(0.42 / n + rnd.NextDouble() * 0.04 / n), ry = (float)(0.38 / n + rnd.NextDouble() * 0.05 / n);
                        double tone = 0.85 + rnd.NextDouble() * 0.3;
                        fill.Color = Shade(new SKColor(0xA8, 0xA0, 0x94), tone);
                        c.DrawOval((float)cx, (float)cy, rx, ry, fill);
                        fill.Color = new SKColor(0xFF, 0xFF, 0xFF, 0x30);
                        c.DrawOval((float)cx - rx * 0.25f, (float)cy - ry * 0.3f, rx * 0.5f, ry * 0.45f, fill);
                    }
                }
                break;
            }
            case Ground.Paving:
            {
                // Flagstones: the square as one slab, two or four, with dark joints.
                (double X, double Y, double W, double H)[] slabs = rnd.Next(3) switch
                {
                    0 => [(0, 0, 1, 1)],
                    1 when rnd.Next(2) == 0 => [(0, 0, 0.5, 1), (0.5, 0, 0.5, 1)],
                    1 => [(0, 0, 1, 0.5), (0, 0.5, 1, 0.5)],
                    _ => [(0, 0, 0.5, 0.5), (0.5, 0, 0.5, 0.5), (0, 0.5, 0.5, 0.5), (0.5, 0.5, 0.5, 0.5)],
                };
                foreach (var (sx, sy, sw, sh) in slabs)
                {
                    fill.Color = Shade(new SKColor(0xBA, 0xB0, 0x9E), 0.86 + rnd.NextDouble() * 0.22);
                    var r = new SKRect((float)(x + sx + 0.02), (float)(y + sy + 0.02), (float)(x + sx + sw - 0.02), (float)(y + sy + sh - 0.02));
                    c.DrawRoundRect(r, 0.03f, 0.03f, fill);
                    line.Color = new SKColor(0, 0, 0, 0x30);
                    line.StrokeWidth = 0.012f;
                    if (rnd.NextDouble() < 0.25) c.DrawLine(r.Left + r.Width * (float)rnd.NextDouble(), r.Top, r.Left + r.Width * (float)rnd.NextDouble(), r.Bottom, line);
                }
                break;
            }
            case Ground.Sand:
            {
                line.Color = new SKColor(0xB0, 0x98, 0x62, 0x80);
                line.StrokeWidth = 0.018f;
                for (int i = 0; i < 4; i++)
                {
                    double yy = y + (i + 0.5) / 4 + (rnd.NextDouble() - 0.5) * 0.08;
                    using var path = new SKPath();
                    path.MoveTo(x, (float)yy);
                    path.CubicTo(x + 0.33f, (float)yy - 0.06f, x + 0.66f, (float)yy + 0.06f, x + 1, (float)yy);
                    c.DrawPath(path, line);
                }
                break;
            }
            case Ground.Water:
            {
                // Darker near the banks, light ripples on top.
                fill.Color = new SKColor(0x2A, 0x58, 0x80, 0x70);
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = m.GroundAt(x + dx, y + dy);
                    if (n == Ground.Water || x + dx < 0 || y + dy < 0 || x + dx >= m.Across || y + dy >= m.Down) continue;
                    float bw = 0.22f;
                    var r = (dx, dy) switch
                    {
                        (1, 0) => new SKRect(x + 1 - bw, y, x + 1, y + 1),
                        (-1, 0) => new SKRect(x, y, x + bw, y + 1),
                        (0, 1) => new SKRect(x, y + 1 - bw, x + 1, y + 1),
                        _ => new SKRect(x, y, x + 1, y + bw),
                    };
                    c.DrawRect(r, fill);
                }
                line.StrokeWidth = 0.02f;
                for (int i = 0; i < 3; i++)
                {
                    line.Color = new SKColor(0xC8, 0xE0, 0xF4, (byte)(0x50 + rnd.Next(0x40)));
                    float cx = x + 0.15f + (float)rnd.NextDouble() * 0.7f, cy = y + 0.15f + (float)rnd.NextDouble() * 0.7f, len = 0.12f + (float)rnd.NextDouble() * 0.18f;
                    using var path = new SKPath();
                    path.MoveTo(cx - len, cy);
                    path.QuadTo(cx, cy - 0.04f, cx + len, cy);
                    c.DrawPath(path, line);
                }
                break;
            }
            case Ground.Field:
            {
                // Furrows, and rows of green shoots.
                line.StrokeWidth = 0.03f;
                for (int i = 0; i < 6; i++)
                {
                    float yy = y + (i + 0.5f) / 6;
                    line.Color = new SKColor(0x6A, 0x56, 0x30, 0xB0);
                    c.DrawLine(x, yy, x + 1, yy, line);
                }
                fill.Color = new SKColor(0x7E, 0xA2, 0x4A);
                for (int i = 0; i < 6; i++)
                    for (int k = 0; k < 7; k++)
                        c.DrawCircle(x + (k + 0.5f) / 7 + (float)(rnd.NextDouble() - 0.5) * 0.03f, y + (i + 0.5f) / 6 - 0.05f, 0.022f, fill);
                break;
            }
            case Ground.Rock:
            {
                line.Color = new SKColor(0x40, 0x3C, 0x38, 0x90);
                line.StrokeWidth = 0.018f;
                for (int i = 0; i < 3; i++)
                {
                    float ax = x + (float)rnd.NextDouble(), ay = y + (float)rnd.NextDouble();
                    c.DrawLine(ax, ay, ax + (float)(rnd.NextDouble() - 0.5) * 0.4f, ay + (float)(rnd.NextDouble() - 0.5) * 0.4f, line);
                }
                fill.Color = new SKColor(0xA6, 0xA0, 0x98, 0x70);
                for (int i = 0; i < 4; i++) c.DrawOval(x + (float)rnd.NextDouble(), y + (float)rnd.NextDouble(), 0.08f, 0.05f, fill);
                break;
            }
            default:
            {
                // A building's floor: boards.
                line.Color = new SKColor(0x6A, 0x50, 0x36, 0x90);
                line.StrokeWidth = 0.015f;
                for (int i = 1; i < 5; i++) c.DrawLine(x, y + i / 5f, x + 1, y + i / 5f, line);
                break;
            }
        }
    }

    // ---- fences and walls ----

    static void DrawWalls(SKCanvas c, CityMap m, SKPaint fill, SKPaint line)
    {
        var layout = WallLayout.Of(m);
        static IEnumerable<(CityPt A, CityPt B)> Segs(WallPiece p)
        {
            for (int i = 0; i + 1 < p.Points.Count; i++) yield return (p.Points[i], p.Points[i + 1]);
            if (p.Closed && p.Points.Count > 2) yield return (p.Points[^1], p.Points[0]);
        }
        SKPath PathOf(WallPiece p)
        {
            var path = new SKPath();
            path.MoveTo((float)p.Points[0].X, (float)p.Points[0].Y);
            foreach (var q in p.Points.Skip(1)) path.LineTo((float)q.X, (float)q.Y);
            if (p.Closed) path.Close();
            return path;
        }
        void Stroke(WallPiece p, SKColor colour, float width, SKStrokeCap cap = SKStrokeCap.Square)
        {
            using var path = PathOf(p);
            line.Color = colour;
            line.StrokeWidth = width;
            line.StrokeCap = cap;
            line.StrokeJoin = SKStrokeJoin.Miter;
            c.DrawPath(path, line);
            line.StrokeJoin = SKStrokeJoin.Round;
            line.StrokeCap = SKStrokeCap.Round;
        }
        foreach (var p in layout.Pieces)
        {
            bool ruin = p.Fence.Ruined;
            switch (p.Fence.Kind)
            {
                case FenceKind.CityWall:
                    Stroke(p, new SKColor(0x4A, 0x46, 0x40), 0.5f);
                    Stroke(p, ruin ? new SKColor(0x8A, 0x84, 0x7A) : new SKColor(0x9A, 0x94, 0x8A), 0.42f);
                    // The walk along the top, and the battlements along both sides.
                    Stroke(p, new SKColor(0x7E, 0x78, 0x6E), 0.22f);
                    fill.Color = new SKColor(0xB4, 0xAE, 0xA2);
                    foreach (var (a, b) in Segs(p))
                    {
                        double len = a.DistanceTo(b);
                        var u = (b - a).Unit;
                        int i = 0;
                        for (double t = 0.12; t < len; t += 0.33, i++)
                            foreach (int side in new[] { -1, 1 })
                            {
                                // A ruin's battlements are mostly gone.
                                if (ruin && (i * 7 + side * 3) % 5 != 0) continue;
                                double mx = a.X + u.X * t + side * u.Y * 0.16, my = a.Y + u.Y * t - side * u.X * 0.16;
                                c.DrawRect((float)mx - 0.06f, (float)my - 0.06f, 0.12f, 0.12f, fill);
                            }
                    }
                    break;
                case FenceKind.StoneWall:
                    Stroke(p, new SKColor(0x50, 0x4C, 0x46), 0.2f);
                    Stroke(p, new SKColor(0xA0, 0x9A, 0x90), 0.14f);
                    break;
                case FenceKind.Hedge:
                    fill.Color = new SKColor(0x2E, 0x52, 0x24);
                    foreach (var (a, b) in Segs(p)) Along(a.X, a.Y, b.X, b.Y, 0.12, (x, y, i) => c.DrawCircle((float)x, (float)y, 0.17f, fill));
                    fill.Color = new SKColor(0x4A, 0x78, 0x36);
                    foreach (var (a, b) in Segs(p)) Along(a.X, a.Y, b.X, b.Y, 0.12, (x, y, i) => c.DrawCircle((float)x - 0.03f, (float)y - 0.04f, 0.12f, fill));
                    break;
                default:
                    Stroke(p, new SKColor(0x5A, 0x3E, 0x26), 0.05f, SKStrokeCap.Butt);
                    fill.Color = new SKColor(0x46, 0x30, 0x1C);
                    foreach (var (a, b) in Segs(p)) Along(a.X, a.Y, b.X, b.Y, 0.5, (x, y, i) => c.DrawRect((float)x - 0.05f, (float)y - 0.05f, 0.1f, 0.1f, fill));
                    break;
            }
        }
        void Square(CityPt at, CityPt way, float length, float across, SKColor body, SKColor rim)
        {
            c.Save();
            c.Translate((float)at.X, (float)at.Y);
            c.RotateDegrees((float)(Math.Atan2(way.Y, way.X) * 180 / Math.PI));
            fill.Color = rim;
            c.DrawRect(-length / 2 - 0.04f, -across / 2 - 0.04f, length + 0.08f, across + 0.08f, fill);
            fill.Color = body;
            c.DrawRect(-length / 2, -across / 2, length, across, fill);
            c.Restore();
        }
        // The ends: a town wall's round turret, a stone wall's pillar, a fence's post.
        foreach (var e in layout.Ends.Where(e => e.Kind == WallEndKind.Cap))
        {
            switch (e.Fence.Kind)
            {
                case FenceKind.CityWall:
                    fill.Color = new SKColor(0x4A, 0x46, 0x40);
                    c.DrawCircle((float)e.At.X, (float)e.At.Y, 0.46f, fill);
                    fill.Color = new SKColor(0xA4, 0x9E, 0x94);
                    c.DrawCircle((float)e.At.X, (float)e.At.Y, 0.4f, fill);
                    fill.Color = new SKColor(0x7E, 0x78, 0x6E);
                    c.DrawCircle((float)e.At.X, (float)e.At.Y, 0.24f, fill);
                    break;
                case FenceKind.StoneWall:
                    Square(e.At, e.Out, 0.26f, 0.26f, new SKColor(0xA8, 0xA2, 0x98), new SKColor(0x50, 0x4C, 0x46));
                    break;
                case FenceKind.Fence:
                    Square(e.At, e.Out, 0.12f, 0.12f, new SKColor(0x5A, 0x3E, 0x26), new SKColor(0x3A, 0x28, 0x16));
                    break;
            }
        }
        // The gates: a town wall's gatehouse over the road (a tower each side, the roof of the passage between), a fence's gate posts.
        foreach (var g in layout.Gates)
        {
            var u = (g.B - g.A).Unit;
            if (g.Fence.Kind == FenceKind.CityWall)
            {
                Square(g.A - u * 0.38, u, 0.76f, 0.92f, new SKColor(0xA4, 0x9E, 0x94), new SKColor(0x4A, 0x46, 0x40));
                Square(g.B + u * 0.38, u, 0.76f, 0.92f, new SKColor(0xA4, 0x9E, 0x94), new SKColor(0x4A, 0x46, 0x40));
                var mid = g.Middle;
                c.Save();
                c.Translate((float)mid.X, (float)mid.Y);
                c.RotateDegrees((float)(Math.Atan2(u.Y, u.X) * 180 / Math.PI));
                float w = (float)g.Width;
                fill.Color = new SKColor(0x6E, 0x4A, 0x30, 0xE0);
                c.DrawRect(-w / 2, -0.3f, w, 0.6f, fill);
                line.Color = new SKColor(0x3A, 0x28, 0x16, 0xC0);
                line.StrokeWidth = 0.025f;
                for (float t = -w / 2 + 0.15f; t < w / 2; t += 0.15f) c.DrawLine(t, -0.3f, t, 0.3f, line);
                c.Restore();
            }
            else if (g.Fence.Kind != FenceKind.Hedge)
            {
                Square(g.A, u, 0.16f, 0.16f, new SKColor(0x5A, 0x3E, 0x26), new SKColor(0x3A, 0x28, 0x16));
                Square(g.B, u, 0.16f, 0.16f, new SKColor(0x5A, 0x3E, 0x26), new SKColor(0x3A, 0x28, 0x16));
            }
        }
    }

    /// <summary>A bridge over the water: its shadow, a deck of flagstones or planks, a parapet or rail along each side.</summary>
    static void DrawBridge(SKCanvas c, BridgeSite site, SKPaint fill, SKPaint line, SKRect view)
    {
        float x0 = site.Cells.Min(q => q.X), x1 = site.Cells.Max(q => q.X) + 1, y0 = site.Cells.Min(q => q.Y), y1 = site.Cells.Max(q => q.Y) + 1;
        if (x1 < view.Left || x0 > view.Right || y1 < view.Top || y0 > view.Bottom) return;
        bool planks = site.Road == Ground.Dirt;
        // Reaching a little onto each bank.
        if (site.Across) { x0 -= 0.15f; x1 += 0.15f; } else { y0 -= 0.15f; y1 += 0.15f; }
        fill.Color = new SKColor(0, 0, 0, 0x50);
        c.DrawRect(x0 + 0.12f, y0 + 0.18f, x1 - x0, y1 - y0, fill);
        fill.Color = planks ? new SKColor(0x96, 0x6C, 0x44) : new SKColor(0xA8, 0xA0, 0x92);
        c.DrawRect(x0, y0, x1 - x0, y1 - y0, fill);
        line.StrokeCap = SKStrokeCap.Butt;
        line.Color = planks ? new SKColor(0x5A, 0x3E, 0x26, 0xA0) : new SKColor(0x6A, 0x64, 0x5C, 0x70);
        line.StrokeWidth = 0.02f;
        float step = planks ? 0.2f : 0.5f;
        if (site.Across) for (float t = x0 + step; t < x1; t += step) c.DrawLine(t, y0, t, y1, line);
        else for (float t = y0 + step; t < y1; t += step) c.DrawLine(x0, t, x1, t, line);
        line.Color = planks ? new SKColor(0x4A, 0x32, 0x1E) : new SKColor(0x7E, 0x78, 0x6E);
        line.StrokeWidth = planks ? 0.1f : 0.2f;
        if (site.Across) { c.DrawLine(x0, y0 + 0.08f, x1, y0 + 0.08f, line); c.DrawLine(x0, y1 - 0.08f, x1, y1 - 0.08f, line); }
        else { c.DrawLine(x0 + 0.08f, y0, x0 + 0.08f, y1, line); c.DrawLine(x1 - 0.08f, y0, x1 - 0.08f, y1, line); }
        line.StrokeCap = SKStrokeCap.Round;
    }

    /// <summary>Points every <paramref name="step"/> squares along a straight run, both ends included.</summary>
    static void Along(double ax, double ay, double bx, double by, double step, Action<double, double, int> at)
    {
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        int n = Math.Max(1, (int)Math.Round(len / step));
        for (int i = 0; i <= n; i++) at(ax + (bx - ax) * i / n, ay + (by - ay) * i / n, i);
    }

    // ---- roofs ----

    /// <summary>Where the light comes from (the top left), as a way along the map.</summary>
    static readonly (double X, double Y) Light = (-0.6, -0.8);

    static void DrawRoofs(SKCanvas c, CityBuilding b, SKPaint fill, SKPaint line)
    {
        SKPoint P(double x, double y) { var (mx, my) = CityFootprint.ToMap(b, x, y); return new((float)mx, (float)my); }
        if (b.Design.Roofs.Count == 0)
        {
            // No roof: the walls' colour, with a darker rim.
            fill.Color = ToSk(CityScene.WallColour(b.Design.Style.Texture));
            if (CityFootprint.OnGrid(b)) foreach (var (x, y) in CityFootprint.Cells(b)) c.DrawRect(x - 0.005f, y - 0.005f, 1.01f, 1.01f, fill);
            else
            {
                using var path = new SKPath();
                var corners = CityFootprint.Corners(b);
                path.MoveTo((float)corners[0].X, (float)corners[0].Y);
                for (int i = 1; i < 4; i++) path.LineTo((float)corners[i].X, (float)corners[i].Y);
                path.Close();
                c.DrawPath(path, fill);
            }
            return;
        }
        foreach (var r in b.Design.Roofs)
        {
            var colour = ToSk(CityScene.RoofColour(r.Covering));
            double X0 = r.X0, Y0 = r.Y0, X1 = r.X1, Y1 = r.Y1, cx = (X0 + X1) / 2, cy = (Y0 + Y1) / 2;
            var planes = new List<(SKPoint[] Corners, SKPoint EaveA, SKPoint EaveB)>();
            switch (r.Kind)
            {
                case RoofKind.Flat:
                    planes.Add(([P(X0, Y0), P(X1, Y0), P(X1, Y1), P(X0, Y1)], P(X0, Y0), P(X1, Y0)));
                    break;
                case RoofKind.LeanTo:
                    if (r.RidgeAcross) planes.Add(([P(X0, Y0), P(X1, Y0), P(X1, Y1), P(X0, Y1)], P(X0, Y1), P(X1, Y1)));
                    else planes.Add(([P(X0, Y0), P(X1, Y0), P(X1, Y1), P(X0, Y1)], P(X1, Y0), P(X1, Y1)));
                    break;
                default:
                {
                    bool hip = r.Kind == RoofKind.Hip;
                    if (r.RidgeAcross)
                    {
                        double inset = hip ? Math.Min((Y1 - Y0) / 2, (X1 - X0) / 2) : 0;
                        SKPoint ra = P(X0 + inset, cy), rb = P(X1 - inset, cy);
                        planes.Add(([P(X0, Y0), P(X1, Y0), rb, ra], P(X0, Y0), P(X1, Y0)));
                        planes.Add(([P(X0, Y1), P(X1, Y1), rb, ra], P(X0, Y1), P(X1, Y1)));
                        if (hip)
                        {
                            planes.Add(([P(X0, Y0), P(X0, Y1), ra], P(X0, Y0), P(X0, Y1)));
                            planes.Add(([P(X1, Y0), P(X1, Y1), rb], P(X1, Y0), P(X1, Y1)));
                        }
                    }
                    else
                    {
                        double inset = hip ? Math.Min((X1 - X0) / 2, (Y1 - Y0) / 2) : 0;
                        SKPoint ra = P(cx, Y0 + inset), rb = P(cx, Y1 - inset);
                        planes.Add(([P(X0, Y0), P(X0, Y1), rb, ra], P(X0, Y0), P(X0, Y1)));
                        planes.Add(([P(X1, Y0), P(X1, Y1), rb, ra], P(X1, Y0), P(X1, Y1)));
                        if (hip)
                        {
                            planes.Add(([P(X0, Y0), P(X1, Y0), ra], P(X0, Y0), P(X1, Y0)));
                            planes.Add(([P(X0, Y1), P(X1, Y1), rb], P(X0, Y1), P(X1, Y1)));
                        }
                    }
                    break;
                }
            }
            var mid = P(cx, cy);
            foreach (var (corners, ea, eb) in planes)
            {
                using var path = new SKPath();
                path.AddPoly(corners, true);
                // Lit by how much it faces the light: the way from the ridge down to its eave.
                double ex = (ea.X + eb.X) / 2 - mid.X, ey = (ea.Y + eb.Y) / 2 - mid.Y, el = Math.Sqrt(ex * ex + ey * ey);
                double lit = r.Kind == RoofKind.Flat || el < 1e-6 ? 0.95 : 0.92 + 0.22 * (ex * Light.X + ey * Light.Y) / el;
                var plane = Shade(colour, lit);
                fill.Color = plane;
                c.DrawPath(path, fill);
                // Rows of tiles, shingles or thatch along the eave.
                c.Save();
                c.ClipPath(path, SKClipOperation.Intersect, true);
                double ax = eb.X - ea.X, ay = eb.Y - ea.Y, al = Math.Sqrt(ax * ax + ay * ay);
                if (al > 1e-6)
                {
                    double ux = ax / al, uy = ay / al, nx = -uy, ny = ux;
                    // Rows go inwards from the eave.
                    if ((mid.X - ea.X) * nx + (mid.Y - ea.Y) * ny < 0) { nx = -nx; ny = -ny; }
                    double depth = Math.Max(X1 - X0, Y1 - Y0);
                    bool thatch = r.Covering == "Thatch";
                    line.StrokeWidth = thatch ? 0.035f : 0.02f;
                    line.Color = Shade(plane, thatch ? 0.84 : 0.76).WithAlpha(thatch ? (byte)0x90 : (byte)0xC0);
                    double step = thatch ? 0.16 : r.Covering is "Slate" or "Fish scale" ? 0.11 : 0.13;
                    for (double t = step; t < depth; t += step)
                    {
                        float sx = (float)(ea.X - ux * depth + nx * t), sy = (float)(ea.Y - uy * depth + ny * t);
                        c.DrawLine(sx, sy, sx + (float)(ux * (al + 2 * depth)), sy + (float)(uy * (al + 2 * depth)), line);
                    }
                    if (!thatch && r.Kind != RoofKind.Flat)
                    {
                        // The tiles' sides, staggered row by row.
                        line.StrokeWidth = 0.012f;
                        int row = 0;
                        for (double t = 0; t < depth; t += step, row++)
                            for (double s = -depth + (row % 2) * step * 0.6; s < al + depth; s += step * 1.2)
                            {
                                float qx = (float)(ea.X + ux * s + nx * t), qy = (float)(ea.Y + uy * s + ny * t);
                                c.DrawLine(qx, qy, qx + (float)(nx * step), qy + (float)(ny * step), line);
                            }
                    }
                }
                c.Restore();
                line.Color = Shade(colour, 0.5).WithAlpha(0xC0);
                line.StrokeWidth = 0.03f;
                c.DrawPath(path, line);
            }
        }
    }

    // ---- trees and things ----

    static double Radius(Prop p) => p.Kind switch
    {
        PropKind.Tree => 0.55,
        PropKind.Pine => 0.42,
        PropKind.Bush => 0.3,
        PropKind.Boulder => 0.28,
        PropKind.Well => 0.42,
        PropKind.Statue => 0.3,
        _ => 0.35,
    } * Math.Max(0.3, p.Size);

    static void DrawProp(SKCanvas c, Prop p, SKPaint fill, SKPaint line)
    {
        float x = (float)p.X, y = (float)p.Y, r = (float)Radius(p);
        var rnd = new Random(p.Id.GetHashCode());
        c.Save();
        c.Translate(x, y);
        c.RotateDegrees((float)p.TurnDeg);
        switch (p.Kind)
        {
            case PropKind.Tree:
            case PropKind.Bush:
            {
                // A canopy of lumps, darker underneath, lit on top.
                var dark = p.Kind == PropKind.Tree ? new SKColor(0x2C, 0x52, 0x22) : new SKColor(0x3C, 0x66, 0x2C);
                var mid = p.Kind == PropKind.Tree ? new SKColor(0x3E, 0x6E, 0x2E) : new SKColor(0x52, 0x82, 0x3A);
                fill.Color = dark;
                c.DrawCircle(0, 0, r, fill);
                for (int i = 0; i < 7; i++)
                {
                    double a = i * Math.PI * 2 / 7 + rnd.NextDouble() * 0.4;
                    fill.Color = mid;
                    c.DrawCircle((float)(Math.Cos(a) * r * 0.5), (float)(Math.Sin(a) * r * 0.5), r * 0.48f, fill);
                }
                fill.Color = new SKColor(0x7A, 0xA8, 0x4E, 0x90);
                for (int i = 0; i < 4; i++)
                    c.DrawCircle(-r * 0.25f + (float)(rnd.NextDouble() - 0.5) * r * 0.5f, -r * 0.3f + (float)(rnd.NextDouble() - 0.5) * r * 0.5f, r * 0.22f, fill);
                break;
            }
            case PropKind.Pine:
            {
                // A star of needles, in rings getting lighter to the top.
                foreach (var (k, col) in new[] { (1.0, new SKColor(0x1E, 0x40, 0x24)), (0.72, new SKColor(0x2A, 0x56, 0x30)), (0.44, new SKColor(0x3A, 0x6C, 0x3A)) })
                {
                    using var path = new SKPath();
                    int n = 9;
                    for (int i = 0; i < n * 2; i++)
                    {
                        double a = i * Math.PI / n, rr = (i % 2 == 0 ? r : r * 0.55) * k;
                        if (i == 0) path.MoveTo((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
                        else path.LineTo((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
                    }
                    path.Close();
                    fill.Color = col;
                    c.DrawPath(path, fill);
                }
                break;
            }
            case PropKind.Boulder:
            {
                using var path = new SKPath();
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4, rr = r * (0.75 + rnd.NextDouble() * 0.3);
                    if (i == 0) path.MoveTo((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
                    else path.LineTo((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
                }
                path.Close();
                fill.Color = new SKColor(0x86, 0x82, 0x7C);
                c.DrawPath(path, fill);
                fill.Color = new SKColor(0xB4, 0xB0, 0xA8, 0xA0);
                c.DrawCircle(-r * 0.25f, -r * 0.25f, r * 0.35f, fill);
                line.Color = new SKColor(0x40, 0x3C, 0x38);
                line.StrokeWidth = 0.025f;
                c.DrawPath(path, line);
                break;
            }
            case PropKind.Well:
            {
                fill.Color = new SKColor(0x8C, 0x86, 0x7C);
                c.DrawCircle(0, 0, r, fill);
                line.Color = new SKColor(0x4A, 0x46, 0x40);
                line.StrokeWidth = 0.025f;
                for (int i = 0; i < 10; i++)
                {
                    double a = i * Math.PI / 5;
                    c.DrawLine((float)(Math.Cos(a) * r * 0.62), (float)(Math.Sin(a) * r * 0.62), (float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r), line);
                }
                c.DrawCircle(0, 0, r, line);
                fill.Color = new SKColor(0x1E, 0x3C, 0x58);
                c.DrawCircle(0, 0, r * 0.62f, fill);
                // The winch's beam across.
                line.Color = new SKColor(0x5A, 0x3E, 0x26);
                line.StrokeWidth = 0.07f;
                c.DrawLine(-r * 1.1f, 0, r * 1.1f, 0, line);
                break;
            }
            case PropKind.Cart:
            {
                fill.Color = new SKColor(0x2E, 0x22, 0x18);
                c.DrawRect(-0.2f, -0.36f, 0.12f, 0.08f, fill);
                c.DrawRect(-0.2f, 0.28f, 0.12f, 0.08f, fill);
                fill.Color = new SKColor(0x8A, 0x60, 0x3A);
                c.DrawRect(-0.42f, -0.28f, 0.7f, 0.56f, fill);
                line.Color = new SKColor(0x5A, 0x3E, 0x26);
                line.StrokeWidth = 0.02f;
                for (int i = 1; i < 5; i++) c.DrawLine(-0.42f + i * 0.14f, -0.28f, -0.42f + i * 0.14f, 0.28f, line);
                line.StrokeWidth = 0.03f;
                c.DrawLine(0.28f, -0.1f, 0.62f, -0.06f, line);
                c.DrawLine(0.28f, 0.1f, 0.62f, 0.06f, line);
                break;
            }
            case PropKind.Crates:
            {
                foreach (var (cx, cy, s) in new[] { (-0.16f, -0.12f, 0.26f), (0.14f, -0.1f, 0.22f), (-0.02f, 0.18f, 0.22f) })
                {
                    fill.Color = new SKColor(0xA2, 0x7A, 0x4C);
                    c.DrawRect(cx - s / 2, cy - s / 2, s, s, fill);
                    line.Color = new SKColor(0x5A, 0x3E, 0x26);
                    line.StrokeWidth = 0.018f;
                    c.DrawRect(cx - s / 2, cy - s / 2, s, s, line);
                    c.DrawLine(cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2, line);
                }
                fill.Color = new SKColor(0x7A, 0x52, 0x30);
                c.DrawCircle(0.24f, 0.2f, 0.1f, fill);
                line.Color = new SKColor(0x3A, 0x2A, 0x1C);
                c.DrawCircle(0.24f, 0.2f, 0.1f, line);
                break;
            }
            case PropKind.Lamp:
            {
                fill.Color = new SKColor(0xFF, 0xD8, 0x7A, 0x40);
                c.DrawCircle(0, 0, 0.32f, fill);
                fill.Color = new SKColor(0x2A, 0x26, 0x22);
                c.DrawCircle(0, 0, 0.07f, fill);
                fill.Color = new SKColor(0xFF, 0xE0, 0x90);
                c.DrawCircle(0, 0, 0.04f, fill);
                break;
            }
            case PropKind.Trough:
            {
                // A stone trough with water in it.
                float w = 1.26f * (float)p.Size, d = 0.43f * (float)p.Size, t = 0.07f * (float)p.Size;
                fill.Color = new SKColor(0x9C, 0x96, 0x8C);
                c.DrawRect(-w / 2, -d / 2, w, d, fill);
                fill.Color = new SKColor(0x3C, 0x6E, 0x98);
                c.DrawRect(-w / 2 + t, -d / 2 + t, w - 2 * t, d - 2 * t, fill);
                fill.Color = new SKColor(0xFF, 0xFF, 0xFF, 0x50);
                c.DrawRect(-w / 2 + t, -d / 2 + t, w - 2 * t, (d - 2 * t) * 0.3f, fill);
                line.Color = new SKColor(0x4A, 0x46, 0x40);
                line.StrokeWidth = 0.02f;
                c.DrawRect(-w / 2, -d / 2, w, d, line);
                break;
            }
            case PropKind.Stage:
            {
                // A planked deck, steps at the front, the backdrop and curtains along the back.
                float w = 3f * (float)p.Size, d = 2f * (float)p.Size;
                fill.Color = new SKColor(0xA6, 0x7C, 0x50);
                c.DrawRect(-w / 2, -d / 2, w, d, fill);
                line.Color = new SKColor(0x6A, 0x4A, 0x2C);
                line.StrokeWidth = 0.02f;
                for (float yy = -d / 2 + 0.16f; yy < d / 2; yy += 0.16f) c.DrawLine(-w / 2, yy, w / 2, yy, line);
                fill.Color = new SKColor(0x8A, 0x62, 0x3E);
                c.DrawRect(-0.5f * (float)p.Size, d / 2, 1f * (float)p.Size, 0.18f * (float)p.Size, fill);
                c.DrawRect(-0.5f * (float)p.Size, d / 2, 1f * (float)p.Size, 0.18f * (float)p.Size, line);
                fill.Color = new SKColor(0x8E, 0x24, 0x28);
                c.DrawRect(-w / 2, -d / 2, w, 0.2f * (float)p.Size, fill);
                fill.Color = new SKColor(0xB0, 0x34, 0x34);
                c.DrawRect(-w / 2, -d / 2 + 0.2f * (float)p.Size, 0.35f * (float)p.Size, 0.3f * (float)p.Size, fill);
                c.DrawRect(w / 2 - 0.35f * (float)p.Size, -d / 2 + 0.2f * (float)p.Size, 0.35f * (float)p.Size, 0.3f * (float)p.Size, fill);
                line.Color = new SKColor(0x3A, 0x2A, 0x1C);
                line.StrokeWidth = 0.025f;
                c.DrawRect(-w / 2, -d / 2, w, d, line);
                break;
            }
            case PropKind.Stall:
            case PropKind.Bar:
            {
                // A striped awning (a stall), or a counter with barrels behind it (a bar).
                float w = (p.Kind == PropKind.Stall ? 1.42f : 1.57f) * (float)p.Size, d = 0.79f * (float)p.Size;
                if (p.Kind == PropKind.Stall)
                {
                    int n = 6;
                    for (int i = 0; i < n; i++)
                    {
                        fill.Color = i % 2 == 0 ? new SKColor(0xC0, 0x3C, 0x34) : new SKColor(0xF0, 0xE6, 0xD0);
                        c.DrawRect(-w / 2 + w * i / n, -d / 2, w / n, d, fill);
                    }
                    fill.Color = new SKColor(0, 0, 0, 0x30);
                    c.DrawRect(-w / 2, 0, w, d / 2, fill);
                }
                else
                {
                    fill.Color = new SKColor(0x5A, 0x3E, 0x26);
                    c.DrawRect(-w / 2, -d / 2, w, d * 0.42f, fill);
                    fill.Color = new SKColor(0x9A, 0x70, 0x48);
                    c.DrawRect(-w / 2, d / 2 - d * 0.42f, w, d * 0.42f, fill);
                    line.Color = new SKColor(0x3A, 0x2A, 0x1C);
                    line.StrokeWidth = 0.02f;
                    foreach (float bx in new[] { -0.5f, 0f, 0.5f })
                    {
                        fill.Color = new SKColor(0x7A, 0x52, 0x30);
                        c.DrawCircle(bx * (float)p.Size, -d * 0.22f, 0.12f * (float)p.Size, fill);
                        c.DrawCircle(bx * (float)p.Size, -d * 0.22f, 0.12f * (float)p.Size, line);
                    }
                }
                line.Color = new SKColor(0x3A, 0x2A, 0x1C);
                line.StrokeWidth = 0.02f;
                c.DrawRect(-w / 2, -d / 2, w, d, line);
                break;
            }
            case PropKind.NoticeBoard:
            {
                float w = 0.94f * (float)p.Size, d = 0.39f * (float)p.Size;
                fill.Color = new SKColor(0x7A, 0x52, 0x32);
                c.DrawRect(-w / 2, -d / 2, w, d, fill);
                line.Color = new SKColor(0x4A, 0x32, 0x1E);
                line.StrokeWidth = 0.02f;
                c.DrawLine(-w / 2, 0, w / 2, 0, line);
                c.DrawRect(-w / 2, -d / 2, w, d, line);
                fill.Color = new SKColor(0xF0, 0xE6, 0xD0);
                c.DrawRect(-w * 0.3f, d * 0.12f, w * 0.18f, d * 0.3f, fill);
                c.DrawRect(w * 0.08f, d * 0.08f, w * 0.2f, d * 0.34f, fill);
                break;
            }
            case PropKind.GuardTower:
            {
                // The fighting roof from above: flagstones inside the merlons, a hatch and a flag.
                float w = 1.81f * (float)p.Size, t = 0.2f * (float)p.Size;
                fill.Color = new SKColor(0x9A, 0x94, 0x8A);
                c.DrawRect(-w / 2, -w / 2, w, w, fill);
                fill.Color = new SKColor(0x7E, 0x78, 0x70);
                c.DrawRect(-w / 2 + t, -w / 2 + t, w - 2 * t, w - 2 * t, fill);
                line.Color = new SKColor(0x5A, 0x56, 0x50);
                line.StrokeWidth = 0.015f;
                for (float g = -w / 2 + t + 0.25f; g < w / 2 - t; g += 0.25f)
                {
                    c.DrawLine(g, -w / 2 + t, g, w / 2 - t, line);
                    c.DrawLine(-w / 2 + t, g, w / 2 - t, g, line);
                }
                // Merlons round the top, lighter where the light falls.
                fill.Color = new SKColor(0xC4, 0xBE, 0xB4);
                int n = 5;
                for (int i = 0; i < n; i++)
                {
                    float a = -w / 2 + (w - t) * i / (n - 1);
                    c.DrawRect(a, -w / 2, t, t, fill);
                    c.DrawRect(-w / 2, a, t, t, fill);
                    c.DrawRect(a, w / 2 - t, t, t, fill);
                    c.DrawRect(w / 2 - t, a, t, t, fill);
                }
                fill.Color = new SKColor(0x5A, 0x3E, 0x26);
                c.DrawRect(w * 0.12f, w * 0.12f, w * 0.18f, w * 0.18f, fill);
                fill.Color = new SKColor(0xB0, 0x2A, 0x2A);
                using (var flag = new SKPath())
                {
                    flag.MoveTo(-w * 0.15f, -w * 0.15f);
                    flag.LineTo(w * 0.12f, -w * 0.08f);
                    flag.LineTo(-w * 0.15f, 0);
                    flag.Close();
                    c.DrawPath(flag, fill);
                }
                line.Color = new SKColor(0x3A, 0x36, 0x30);
                line.StrokeWidth = 0.03f;
                c.DrawRect(-w / 2, -w / 2, w, w, line);
                break;
            }
            case PropKind.Fountain:
            {
                // An eight-sided basin of water, the two bowls on the column and the finial in the middle.
                float rr = (float)p.Size;
                using (var oct = new SKPath())
                using (var water = new SKPath())
                {
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI / 8 + i * Math.PI / 4;
                        float ro = (float)(0.95 / Math.Cos(Math.PI / 8)) * rr, ri = (float)(0.8 / Math.Cos(Math.PI / 8)) * rr;
                        if (i == 0) { oct.MoveTo((float)Math.Cos(a) * ro, (float)Math.Sin(a) * ro); water.MoveTo((float)Math.Cos(a) * ri, (float)Math.Sin(a) * ri); }
                        else { oct.LineTo((float)Math.Cos(a) * ro, (float)Math.Sin(a) * ro); water.LineTo((float)Math.Cos(a) * ri, (float)Math.Sin(a) * ri); }
                    }
                    oct.Close();
                    water.Close();
                    fill.Color = new SKColor(0xA4, 0x9C, 0x90);
                    c.DrawPath(oct, fill);
                    fill.Color = new SKColor(0x3C, 0x6E, 0x98);
                    c.DrawPath(water, fill);
                    line.Color = new SKColor(0x4A, 0x46, 0x40);
                    line.StrokeWidth = 0.025f;
                    c.DrawPath(oct, line);
                    c.DrawPath(water, line);
                }
                line.Color = new SKColor(0xFF, 0xFF, 0xFF, 0x60);
                line.StrokeWidth = 0.015f;
                c.DrawCircle(0, 0, 0.62f * rr, line);
                fill.Color = new SKColor(0xB4, 0xAC, 0xA0);
                c.DrawCircle(0, 0, 0.48f * rr, fill);
                fill.Color = new SKColor(0x4E, 0x86, 0xB6);
                c.DrawCircle(0, 0, 0.42f * rr, fill);
                fill.Color = new SKColor(0xC4, 0xBC, 0xB0);
                c.DrawCircle(0, 0, 0.27f * rr, fill);
                fill.Color = new SKColor(0x5E, 0x96, 0xC6);
                c.DrawCircle(0, 0, 0.21f * rr, fill);
                fill.Color = new SKColor(0xD4, 0xCC, 0xC0);
                c.DrawCircle(0, 0, 0.08f * rr, fill);
                line.Color = new SKColor(0x4A, 0x46, 0x40);
                line.StrokeWidth = 0.02f;
                c.DrawCircle(0, 0, 0.48f * rr, line);
                c.DrawCircle(0, 0, 0.27f * rr, line);
                break;
            }
            case PropKind.Gallows:
            {
                // The planked platform, steps up the front, railings round it and the beam with its noose.
                float s = (float)p.Size, w = 1.81f * s, d = 1.5f * s, pd = 1.14f * s;
                fill.Color = new SKColor(0xA6, 0x7C, 0x50);
                c.DrawRect(-w / 2, -d / 2, w, pd, fill);
                line.Color = new SKColor(0x6A, 0x4A, 0x2C);
                line.StrokeWidth = 0.015f;
                for (float xx = -w / 2 + 0.13f * s; xx < w / 2; xx += 0.13f * s) c.DrawLine(xx, -d / 2, xx, -d / 2 + pd, line);
                fill.Color = new SKColor(0x8A, 0x62, 0x3E);
                c.DrawRect(-w / 2 + 0.3f * s, -d / 2 + pd, 0.47f * s, d - pd, fill);
                c.DrawRect(-w / 2 + 0.3f * s, -d / 2 + pd, 0.47f * s, d - pd, line);
                line.Color = new SKColor(0x4A, 0x32, 0x1E);
                line.StrokeWidth = 0.035f;
                float m = 0.25f * s;
                c.DrawLine(-w / 2 + m, -d / 2 + m * 0.6f, w / 2 - m, -d / 2 + m * 0.6f, line);
                c.DrawLine(-w / 2 + m, -d / 2 + m * 0.6f, -w / 2 + m, -d / 2 + pd - m * 0.6f, line);
                c.DrawLine(w / 2 - m, -d / 2 + m * 0.6f, w / 2 - m, -d / 2 + pd - m * 0.6f, line);
                fill.Color = new SKColor(0x5A, 0x3E, 0x26);
                float by = -d / 2 + pd * 0.45f;
                c.DrawRect(-w * 0.32f, by - 0.06f * s, w * 0.64f, 0.12f * s, fill);
                c.DrawRect(-w * 0.34f, by - 0.08f * s, 0.14f * s, 0.16f * s, fill);
                c.DrawRect(w * 0.34f - 0.14f * s, by - 0.08f * s, 0.14f * s, 0.16f * s, fill);
                line.Color = new SKColor(0xD8, 0xC0, 0x8A);
                line.StrokeWidth = 0.02f;
                c.DrawCircle(-w * 0.06f, by + 0.1f * s, 0.07f * s, line);
                line.Color = new SKColor(0x3A, 0x2A, 0x1C);
                c.DrawRect(-w / 2, -d / 2, w, pd, line);
                break;
            }
            case PropKind.Statue:
            {
                fill.Color = new SKColor(0x9A, 0x96, 0x8E);
                c.DrawRect(-r, -r, 2 * r, 2 * r, fill);
                line.Color = new SKColor(0x4A, 0x46, 0x40);
                line.StrokeWidth = 0.02f;
                c.DrawRect(-r, -r, 2 * r, 2 * r, line);
                fill.Color = new SKColor(0xC4, 0xC0, 0xB8);
                c.DrawCircle(0, 0, r * 0.5f, fill);
                c.DrawCircle(0, 0, r * 0.5f, line);
                break;
            }
        }
        c.Restore();
    }

    // ---- bits ----

    static void Text(SKCanvas c, SKPaint text, string s, float x, float y, float size)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        text.TextSize = size;
        text.Style = SKPaintStyle.Stroke;
        text.StrokeWidth = size * 0.18f;
        text.Color = new SKColor(0, 0, 0, 0xC8);
        c.DrawText(s, x, y + size * 0.35f, text);
        text.Style = SKPaintStyle.Fill;
        text.Color = SKColors.White;
        c.DrawText(s, x, y + size * 0.35f, text);
    }

    static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    static SKColor Shade(SKColor c, double f) => new((byte)Math.Clamp(c.Red * f, 0, 255), (byte)Math.Clamp(c.Green * f, 0, 255), (byte)Math.Clamp(c.Blue * f, 0, 255), c.Alpha);

    static int Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed * 374761393 + x * 668265263 + y * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    /// <summary>Smooth noise between 0 and 1 (value noise on a grid, eased between its points).</summary>
    static double Noise(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double V(int i, int j) => (Hash(i, j, seed + 17) & 0xFFFF) / 65535.0;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double a = V(ix, iy) + (V(ix + 1, iy) - V(ix, iy)) * sx, b = V(ix, iy + 1) + (V(ix + 1, iy + 1) - V(ix, iy + 1)) * sx;
        return a + (b - a) * sy;
    }
}
