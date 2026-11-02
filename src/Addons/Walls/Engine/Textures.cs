using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Walls;

/// <summary>What a preset texture asks for: the names of its two sizes (null = not used) and its defaults.</summary>
public sealed record TexturePreset(string Name, string? SizeALabel, double SizeA, string? SizeBLabel, double SizeB, bool UsesGroove, double Groove, bool Random, string About);

/// <summary>
/// A texture laid over the face: the raised pieces (bricks, stones, planks: what's between them is
/// etched away), areas etched as they are (nail holes, your own closed shapes) and lines scored as they
/// are (wood grain, rivets, your own open shapes).
/// </summary>
public sealed record TextureParts(PathsD Raised, PathsD Etched, List<PathD> Lines)
{
    public static readonly TextureParts None = new([], [], []);
}

/// <summary>The face a texture covers: the panel's whole size, the part between the plain ends, and the top of the wall's body (under any battlements).</summary>
public readonly record struct Face(double Length, double Height, double X0, double X1, double Top);

/// <summary>The preset textures and your own, drawn over a wall's face.</summary>
public static class Textures
{
    public const string Plain = "Plain";

    public static readonly IReadOnlyList<TexturePreset> Presets =
    [
        new("Brick", "Brick length", 8, "Brick height", 3.5, true, 0.6, false, "Bricks in running bond: each row moved along half a brick."),
        new("Stone blocks", "Block length", 12, "Course height", 6, true, 0.8, true, "Cut stone in rows of different heights, blocks of different lengths."),
        new("Fieldstone", "Stone size", 9, "Course height", 6, true, 0.9, true, "Rough, rounded stones of uneven shape, roughly in rows."),
        new("Tiles", "Tile width", 6, "Tile height", 6, true, 0.6, false, "Square tiles or blocks in straight rows and columns."),
        new("Wood planks (upright)", "Plank width", 5, "Plank length", 35, true, 0.5, true, "Upright boards with butt joints, grain and nail holes."),
        new("Wood planks (across)", "Plank width", 5, "Plank length", 35, true, 0.5, true, "Boards running along the wall, with grain and nail holes."),
        new("Timber frame", "Beam width", 3, "Bay width", 20, false, 0.5, false, "Tudor style: posts, rails and braces stand out, the plaster between them is etched."),
        new("Corrugated metal", "Ridge pitch", 2.5, null, 0, false, 0, false, "Upright ridges, for sheds and industrial or sci-fi scenery."),
        new("Metal plates", "Plate width", 20, "Plate height", 14, true, 0.5, false, "Riveted plates, each row moved along half a plate."),
        new("Hexagons", "Hexagon size", 8, null, 0, true, 0.7, false, "Hexagonal tiles or armour, for sci-fi walls."),
        // The themes' own (Shared/Terrain/ThemeTextures.cs): dungeon blocks, cave rock, sci-fi panels, sandstone, adobe.
        .. ThemeTextures.All.Select(t => new TexturePreset(t.Name, t.SizeALabel, t.SizeA, t.SizeBLabel, t.SizeB, true, t.Groove, true, t.About)),
        new(Plain, null, 0, null, 0, false, 0, false, "No texture: just the outline, openings and mounts."),
    ];

    public static TexturePreset? Preset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>The texture's pieces over the face (with a little over the edges; they're clipped to the panel afterwards).</summary>
    public static TextureParts Make(WallSpec s, Face f)
    {
        double a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        // The groove can't be wider than half a brick (or half a tile, when there's only one size).
        double g = Math.Clamp(s.GrooveMm, 0.05, (Preset(s.Texture)?.SizeBLabel is null ? a : Math.Min(a, b)) / 2);
        var rnd = new Random(s.Seed);
        if (s.Custom is { } custom && s.Texture == custom.Name) return Custom(custom, a, f);
        if (ThemeTextures.Find(s.Texture) is not null)
        {
            var (r, e, l) = ThemeTextures.Make(s.Texture, f.Length, f.Height, a, b, g, rnd);
            return new(r, e, l);
        }
        return s.Texture switch
        {
            "Brick" => Bricks(f, a, b, g, 0.5),
            "Tiles" => Bricks(f, a, b, g, 0),
            "Stone blocks" => Blocks(f, a, b, g, rnd),
            "Fieldstone" => Fieldstone(f, a, b, g, rnd),
            "Wood planks (upright)" => Planks(f, a, b, g, rnd, upright: true),
            "Wood planks (across)" => Planks(f, a, b, g, rnd, upright: false),
            "Timber frame" => Timber(f, a, b),
            "Corrugated metal" => Corrugated(f, a),
            "Metal plates" => Plates(f, a, b, g),
            "Hexagons" => Hexagons(f, a, g),
            _ => TextureParts.None,
        };
    }

    static PathD Box(double x0, double y0, double x1, double y1, double g) => Geo.Rect(x0 + g / 2, y0 + g / 2, x1 - g / 2, y1 - g / 2);

    static TextureParts Bricks(Face f, double a, double b, double g, double shift)
    {
        var raised = new PathsD();
        int rows = (int)Math.Ceiling(f.Height / b) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * b, off = r % 2 == 1 ? shift * a : 0;
            for (double x = -off; x < f.Length + a; x += a) raised.Add(Box(x, y, x + a, y + b, g));
        }
        return new(raised, [], []);
    }

    static TextureParts Blocks(Face f, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        for (double y = 0; y < f.Height + b;)
        {
            double h = b * (0.75 + 0.5 * rnd.NextDouble());
            for (double x = -a * rnd.NextDouble(); x < f.Length + a;)
            {
                double w = a * (0.6 + 0.9 * rnd.NextDouble());
                raised.Add(Box(x, y, x + w, y + h, g));
                x += w;
            }
            y += h;
        }
        return new(raised, [], []);
    }

    /// <summary>Rows whose edges wander up and down, split into stones with leaning sides, shrunk and rounded off.</summary>
    static TextureParts Fieldstone(Face f, double a, double b, double g, Random rnd)
    {
        double step = Math.Max(0.8, a / 3), x0 = -a, x1 = f.Length + a;
        int n = (int)Math.Ceiling((x1 - x0) / step) + 1;
        double[] Edge(double y) => [.. Enumerable.Range(0, n).Select(_ => y + (rnd.NextDouble() - 0.5) * 0.45 * b)];
        double At(double[] e, double x)
        {
            double t = Math.Clamp((x - x0) / step, 0, n - 1.0001);
            int i = (int)t;
            return e[i] + (e[i + 1] - e[i]) * (t - i);
        }
        // The edge's points from xa to xb: both ends and the samples between.
        List<PointD> Along(double[] e, double xa, double xb)
        {
            var pts = new List<PointD> { new(xa, At(e, xa)) };
            for (int i = Math.Max(0, (int)Math.Floor((xa - x0) / step) + 1); i < n && x0 + i * step < xb - 1e-6; i++) pts.Add(new(x0 + i * step, e[i]));
            pts.Add(new(xb, At(e, xb)));
            return pts;
        }
        var raised = new PathsD();
        double y0 = -b * 0.5;
        var lower = Edge(y0);
        double round = Math.Min(a, b) * 0.18;
        while (y0 < f.Height + b)
        {
            double y1 = y0 + b * (0.75 + 0.5 * rnd.NextDouble());
            var upper = Edge(y1);
            // Each side joins a point on the lower edge to one on the upper: neighbours share it.
            double side = x0 - a * rnd.NextDouble();
            double lowX = side + (rnd.NextDouble() - 0.5) * a * 0.3, upX = side + (rnd.NextDouble() - 0.5) * a * 0.3;
            while (Math.Min(lowX, upX) < x1)
            {
                double next = Math.Max(lowX, upX) + a * (0.6 + 0.8 * rnd.NextDouble());
                double nLow = next + (rnd.NextDouble() - 0.5) * a * 0.3, nUp = next + (rnd.NextDouble() - 0.5) * a * 0.3;
                var stone = new PathD(Along(lower, lowX, nLow));
                var top = Along(upper, upX, nUp);
                top.Reverse();
                stone.AddRange(top);
                raised.AddRange(Geo.Offset(Geo.Offset([stone], -(g / 2 + round), round: true), round, round: true));
                lowX = nLow;
                upX = nUp;
            }
            y0 = y1;
            lower = upper;
        }
        return new(raised, [], []);
    }

    static TextureParts Planks(Face f, double a, double b, double g, Random rnd, bool upright)
    {
        // Drawn upright on a face turned if need be, then turned back.
        double across = upright ? f.Length : f.Height, along = upright ? f.Height : f.Length;
        var raised = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        for (double x = 0; x < across + a; x += a)
        {
            for (double y = -b * rnd.NextDouble(); y < along + b;)
            {
                double l = b * (0.6 + 0.8 * rnd.NextDouble());
                raised.Add(Box(x, y, x + a, y + l, g));
                // Two nails at each end of the board.
                if (a >= 2.5 && l >= 6)
                    foreach (double ny in new[] { y + 1.5, y + l - 1.5 })
                        foreach (double nx in new[] { x + a * 0.3, x + a * 0.7 })
                            etched.Add(Geo.Circle(nx, ny, Math.Min(0.3, a * 0.06)));
                // Grain: gently wavy lines along the board.
                if (a >= 2.5 && l >= 6)
                {
                    int count = a >= 5 ? 2 : 1;
                    for (int k = 0; k < count; k++)
                    {
                        double gx = x + a * (k + 1) / (count + 1) + (rnd.NextDouble() - 0.5) * a * 0.15;
                        double wave = 8 + 10 * rnd.NextDouble(), phase = rnd.NextDouble() * 6.28, amp = Math.Min(0.25, a * 0.04);
                        var line = new PathD();
                        for (double t = y + 3; t <= y + l - 3; t += 0.8) line.Add(new PointD(gx + amp * Math.Sin(t / wave * 6.28 + phase), t));
                        if (line.Count >= 2) lines.Add(line);
                    }
                }
                y += l;
            }
        }
        if (upright) return new(raised, etched, lines);
        static PointD Swap(PointD p) => new(p.y, p.x);
        return new(Geo.Map(raised, Swap), Geo.Map(etched, Swap), Geo.Map(lines, Swap));
    }

    static TextureParts Timber(Face f, double beam, double bay)
    {
        double span = Math.Max(beam * 3, f.X1 - f.X0);
        int bays = Math.Max(1, (int)Math.Round(span / bay));
        double w = span / bays, top = f.Top;
        var beams = new PathsD
        {
            Geo.Rect(-1, -1, f.Length + 1, beam),                // sill
            Geo.Rect(-1, top - beam, f.Length + 1, top + 1),     // top plate
            Geo.Rect(-1, top * 0.5 - beam / 2, f.Length + 1, top * 0.5 + beam / 2), // middle rail
        };
        for (int i = 0; i <= bays; i++)
        {
            double x = f.X0 + i * w;
            beams.Add(Geo.Rect(x - beam / 2, -1, x + beam / 2, top + 1));
        }
        // A brace across the lower half of every other bay, leaning alternately.
        for (int i = 0; i < bays; i += 2)
        {
            double xa = f.X0 + i * w, xb = xa + w;
            var (p, q) = (i / 2) % 2 == 0 ? (new PointD(xa, 0), new PointD(xb, top * 0.5)) : (new PointD(xb, 0), new PointD(xa, top * 0.5));
            beams.Add(Strip(p, q, beam));
        }
        return new(Geo.Union(beams), [], []);
    }

    /// <summary>A straight strip of the given width from p to q.</summary>
    public static PathD Strip(PointD p, PointD q, double width)
    {
        double dx = q.x - p.x, dy = q.y - p.y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
        double nx = -dy / len * width / 2, ny = dx / len * width / 2;
        return [new(p.x + nx, p.y + ny), new(p.x - nx, p.y - ny), new(q.x - nx, q.y - ny), new(q.x + nx, q.y + ny)];
    }

    static TextureParts Corrugated(Face f, double pitch)
    {
        var etched = new PathsD();
        for (double x = 0; x < f.Length + pitch; x += pitch) etched.Add(Geo.Rect(x, -1, x + pitch / 2, f.Height + 1));
        return new([], etched, []);
    }

    static TextureParts Plates(Face f, double a, double b, double g)
    {
        var raised = new PathsD();
        var lines = new List<PathD>();
        double rivet = Math.Clamp(Math.Min(a, b) * 0.03, 0.2, 0.45), inset = Math.Max(1, rivet * 3);
        int rows = (int)Math.Ceiling(f.Height / b) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * b, off = r % 2 == 1 ? a / 2 : 0;
            for (double x = -off; x < f.Length + a; x += a)
            {
                raised.Add(Box(x, y, x + a, y + b, g));
                // Rivets round the plate's edge, about 3 mm apart.
                double x0 = x + inset, x1 = x + a - inset, y0 = y + inset, y1 = y + b - inset;
                if (x1 <= x0 || y1 <= y0) continue;
                int nx = Math.Max(1, (int)Math.Round((x1 - x0) / 3)), ny = Math.Max(1, (int)Math.Round((y1 - y0) / 3));
                for (int i = 0; i <= nx; i++)
                {
                    lines.Add(Geo.Loop(Geo.Circle(x0 + (x1 - x0) * i / nx, y0, rivet)));
                    lines.Add(Geo.Loop(Geo.Circle(x0 + (x1 - x0) * i / nx, y1, rivet)));
                }
                for (int j = 1; j < ny; j++)
                {
                    lines.Add(Geo.Loop(Geo.Circle(x0, y0 + (y1 - y0) * j / ny, rivet)));
                    lines.Add(Geo.Loop(Geo.Circle(x1, y0 + (y1 - y0) * j / ny, rivet)));
                }
            }
        }
        return new(raised, [], lines);
    }

    static TextureParts Hexagons(Face f, double size, double g)
    {
        // Flat-topped hexagons "size" across their flats, in columns.
        double r = size / Math.Sqrt(3), dx = 1.5 * r, inner = r - g / Math.Sqrt(3);
        var raised = new PathsD();
        int col = 0;
        for (double x = 0; x < f.Length + size; x += dx, col++)
            for (double y = col % 2 == 0 ? 0 : size / 2; y < f.Height + size; y += size)
            {
                var hex = new PathD(6);
                for (int k = 0; k < 6; k++) hex.Add(new PointD(x + inner * Math.Cos(k * Math.PI / 3), y + inner * Math.Sin(k * Math.PI / 3)));
                raised.Add(hex);
            }
        return new(raised, [], []);
    }

    /// <summary>Your own shapes repeated in rows over the face, scaled so a tile is <paramref name="width"/> wide.</summary>
    static TextureParts Custom(CustomTexture c, double width, Face f)
    {
        PathGeometry geo;
        try { geo = PathGeometry.FromSvgPathData(c.Path); }
        catch (Exception) { return TextureParts.None; }
        var (areas, lines) = Geo.FromGeometry(geo);
        double k = width / Math.Max(0.1, c.TileWidthMm), tw = c.TileWidthMm * k, th = Math.Max(0.1, c.TileHeightMm * k);
        // y down in the tile (top-left at 0, 0) → y up on the wall, the tile's bottom on the row's.
        PointD Tile(PointD p) => new(p.x * k, (c.TileHeightMm + p.y) * k);
        var tileAreas = Geo.Tidy(Geo.Map(areas, Tile));
        var tileLines = Geo.Map(lines, Tile);
        if (tw * th < 0.01 || f.Length * f.Height / (tw * th) > 40000) return TextureParts.None;
        var etched = new PathsD();
        var scored = new List<PathD>();
        int rows = (int)Math.Ceiling(f.Height / th) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * th, off = r % 2 == 1 ? Math.Clamp(c.RowShift, 0, 1) * tw : 0;
            for (double x = off - tw; x < f.Length + tw; x += tw)
            {
                etched.AddRange(Geo.Move(tileAreas, x, y));
                scored.AddRange(tileLines.Select(l => Geo.Map(l, p => new PointD(p.x + x, p.y + y))));
            }
        }
        return new([], Geo.Union(etched), scored);
    }
}
