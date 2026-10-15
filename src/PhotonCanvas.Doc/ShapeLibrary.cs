using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

/// <summary>
/// A ready-made shape: a name, a category and its outline (fits a 100 × 100 box at the origin,
/// unless <paramref name="RealSize"/>: then it is in true millimetres and is added at that size).
/// </summary>
public sealed record LibraryShape(string Category, string Name, PathGeometry Geometry, string? File = null, bool RealSize = false)
{
    /// <summary>Shapes saved by the user (a design file each).</summary>
    public bool IsUser => File is not null;
}

/// <summary>
/// The shape library: shapes drawn for this program (all original, free to use), plus the
/// user's own saved shapes (each a small design file in a folder, so they can be shared).
/// </summary>
public static class ShapeLibrary
{
    public const string UserCategory = "My shapes";

    /// <summary>Scale and move a geometry to fit a <paramref name="size"/> box at the origin, keeping proportions.</summary>
    public static PathGeometry Fit(PathGeometry g, double size = 100)
    {
        var b = g.Bounds(0.01);
        if (b.IsEmpty) return g;
        double s = size / Math.Max(b.Width, b.Height);
        return g.Transform(Affine.Translate(-b.MinX, -b.MinY).Then(Affine.Scale(s, s)));
    }

    static LibraryShape S(string cat, string name, PathGeometry g) => new(cat, name, Fit(g));
    static LibraryShape Svg(string cat, string name, string d) => S(cat, name, PathGeometry.FromSvgPathData(d));
    static PathGeometry Poly(IEnumerable<Vec2> pts) => PathGeometry.Polyline(pts.ToList(), true);
    static PathGeometry Join(params PathGeometry[] parts) => new(parts.SelectMany(p => p.Subpaths));
    static PathGeometry Union(params PathGeometry[] parts) => ShapeGeometry.Union(parts);

    static IEnumerable<Vec2> Regular(int n, double r, double rot = -90, double cx = 50, double cy = 50) =>
        Enumerable.Range(0, n).Select(i => { double a = (rot + 360.0 * i / n) * Math.PI / 180; return new Vec2(cx + r * Math.Cos(a), cy + r * Math.Sin(a)); });

    static PathGeometry Star(int points, double inner) =>
        Poly(Enumerable.Range(0, points * 2).Select(i => { double a = (-90 + 180.0 * i / points) * Math.PI / 180, r = i % 2 == 0 ? 50 : 50 * inner; return new Vec2(50 + r * Math.Cos(a), 50 + r * Math.Sin(a)); }));

    static PathGeometry Gear(int teeth, double hole)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i < teeth; i++)
        {
            double a0 = 2 * Math.PI * i / teeth, w = 2 * Math.PI / teeth;
            foreach (var (f, r) in new[] { (0.0, 40.0), (0.18, 40.0), (0.3, 50.0), (0.7, 50.0), (0.82, 40.0) })
                pts.Add(new Vec2(50 + r * Math.Cos(a0 + f * w), 50 + r * Math.Sin(a0 + f * w)));
        }
        return Join(Poly(pts), PathGeometry.Ellipse(50, 50, hole, hole));
    }

    static PathGeometry Scalloped(int bumps)
    {
        var parts = new List<PathGeometry> { PathGeometry.Ellipse(50, 50, 42, 42) };
        foreach (var p in Regular(bumps, 42)) parts.Add(PathGeometry.Ellipse(p.X, p.Y, 8, 8));
        return Union([.. parts]);
    }

    static PathGeometry Sun(int rays)
    {
        var parts = new List<PathGeometry> { PathGeometry.Ellipse(50, 50, 24, 24) };
        for (int i = 0; i < rays; i++)
        {
            double a = 2 * Math.PI * i / rays, w = Math.PI / rays * 0.55;
            parts.Add(Poly([new(50 + 28 * Math.Cos(a - w), 50 + 28 * Math.Sin(a - w)), new(50 + 50 * Math.Cos(a), 50 + 50 * Math.Sin(a)), new(50 + 28 * Math.Cos(a + w), 50 + 28 * Math.Sin(a + w))]));
        }
        return Union([.. parts]);
    }

    static PathGeometry Flower(int petals)
    {
        var parts = new List<PathGeometry>();
        for (int i = 0; i < petals; i++)
        {
            double a = 360.0 * i / petals;
            parts.Add(PathGeometry.Ellipse(50, 26, 13, 24).Transform(Affine.RotateAbout(new Vec2(50, 50), a)));
        }
        return Join(Union([.. parts]), PathGeometry.Ellipse(50, 50, 9, 9));
    }

    static PathGeometry Snowflake()
    {
        var parts = new List<PathGeometry>();
        for (int i = 0; i < 6; i++)
        {
            var arm = Union(PathGeometry.Rect(47, 2, 6, 48), Poly([new(50, 20), new(38, 10), new(41, 6), new(50, 14), new(59, 6), new(62, 10)]));
            parts.Add(arm.Transform(Affine.RotateAbout(new Vec2(50, 50), 60 * i)));
        }
        return Union([.. parts]);
    }

    static PathGeometry LivingHinge(double w, double h, double cut, double gap, double pitch)
    {
        // Rows of slits, alternately offset, that let sheet material bend. Open lines (cut, not filled).
        var subs = new List<Subpath>();
        int col = 0;
        for (double x = 0; x <= w + 1e-9; x += pitch, col++)
        {
            bool shifted = col % 2 == 1;
            for (double y = shifted ? -cut / 2 - gap / 2 : 0; y < h; y += cut + gap)
            {
                double y0 = Math.Max(0, y), y1 = Math.Min(h, y + cut);
                if (y1 - y0 > 1) subs.Add(new Subpath(new Vec2(x, y0), [Seg.Line(new Vec2(x, y1))], false));
            }
        }
        return new PathGeometry(subs);
    }

    static IReadOnlyList<LibraryShape>? _builtIn;

    /// <summary>The shapes that come with the program.</summary>
    public static IReadOnlyList<LibraryShape> BuiltIn => _builtIn ??=
    [
        // Basic
        S("Basic", "Circle", PathGeometry.Ellipse(50, 50, 50, 50)),
        S("Basic", "Square", PathGeometry.Rect(0, 0, 100, 100)),
        S("Basic", "Rounded square", PathGeometry.RoundRect(0, 0, 100, 100, 18, 18)),
        S("Basic", "Oval", PathGeometry.Ellipse(50, 30, 50, 30)),
        S("Basic", "Triangle", Poly(Regular(3, 55, -90, 50, 60))),
        S("Basic", "Right triangle", Poly([new(0, 0), new(0, 100), new(100, 100)])),
        S("Basic", "Diamond", Poly([new(50, 0), new(100, 50), new(50, 100), new(0, 50)])),
        S("Basic", "Pentagon", Poly(Regular(5, 50))),
        S("Basic", "Hexagon", Poly(Regular(6, 50, 0))),
        S("Basic", "Octagon", Poly(Regular(8, 50, 22.5))),
        S("Basic", "Semicircle", PathGeometry.FromSvgPathData("M0 50 A50 50 0 0 1 100 50 Z")),
        S("Basic", "Quarter circle", PathGeometry.FromSvgPathData("M0 0 L0 100 A100 100 0 0 0 100 0 Z").Transform(Affine.Scale(1, -1))),
        S("Basic", "Ring", Join(PathGeometry.Ellipse(50, 50, 50, 50), PathGeometry.Ellipse(50, 50, 30, 30))),
        S("Basic", "Parallelogram", Poly([new(25, 0), new(100, 0), new(75, 60), new(0, 60)])),
        S("Basic", "Trapezoid", Poly([new(25, 0), new(75, 0), new(100, 60), new(0, 60)])),

        // Stars
        S("Stars", "Star 5", Star(5, 0.42)),
        S("Stars", "Star 4", Star(4, 0.38)),
        S("Stars", "Star 6", Star(6, 0.5)),
        S("Stars", "Star 8", Star(8, 0.55)),
        S("Stars", "Starburst", Star(16, 0.75)),
        Svg("Stars", "Sparkle", "M50 0 C55 35 65 45 100 50 C65 55 55 65 50 100 C45 65 35 55 0 50 C35 45 45 35 50 0 Z"),

        // Symbols
        Svg("Symbols", "Heart", "M50 92 C20 70 0 52 0 30 C0 12 13 0 29 0 C39 0 46 6 50 14 C54 6 61 0 71 0 C87 0 100 12 100 30 C100 52 80 70 50 92 Z"),
        Svg("Symbols", "Arrow", "M0 35 L60 35 L60 10 L100 50 L60 90 L60 65 L0 65 Z"),
        Svg("Symbols", "Double arrow", "M0 50 L30 15 L30 35 L70 35 L70 15 L100 50 L70 85 L70 65 L30 65 L30 85 Z"),
        Svg("Symbols", "Chevron", "M0 0 L40 0 L80 50 L40 100 L0 100 L40 50 Z"),
        S("Symbols", "Plus", Union(PathGeometry.Rect(35, 0, 30, 100), PathGeometry.Rect(0, 35, 100, 30))),
        S("Symbols", "Cross", Union(PathGeometry.Rect(35, -10, 30, 120), PathGeometry.Rect(-10, 35, 120, 30)).Transform(Affine.RotateAbout(new Vec2(50, 50), 45))),
        Svg("Symbols", "Check mark", "M0 55 L15 40 L38 62 L85 10 L100 25 L38 92 Z"),
        Svg("Symbols", "Lightning bolt", "M60 0 L15 58 L45 58 L35 100 L85 38 L55 38 Z"),
        Svg("Symbols", "Teardrop", "M50 0 C50 0 90 45 90 68 C90 90 72 100 50 100 C28 100 10 90 10 68 C10 45 50 0 50 0 Z"),
        Svg("Symbols", "Crescent moon", "M60 0 A50 50 0 1 0 100 70 A40 40 0 1 1 60 0 Z"),
        Svg("Symbols", "Cloud", "M25 80 C8 80 0 68 0 57 C0 45 9 36 21 35 C23 20 35 10 50 10 C64 10 75 19 78 32 C91 33 100 43 100 56 C100 69 90 80 77 80 Z"),
        Svg("Symbols", "Speech bubble", "M10 0 L90 0 C96 0 100 4 100 10 L100 60 C100 66 96 70 90 70 L40 70 L18 92 L22 70 L10 70 C4 70 0 66 0 60 L0 10 C0 4 4 0 10 0 Z"),
        S("Symbols", "Location pin", Join(PathGeometry.FromSvgPathData("M50 100 C50 100 15 58 15 35 C15 15 31 0 50 0 C69 0 85 15 85 35 C85 58 50 100 50 100 Z"), PathGeometry.Ellipse(50, 35, 14, 14))),
        Svg("Symbols", "Shield", "M50 0 L95 15 L95 45 C95 72 75 90 50 100 C25 90 5 72 5 45 L5 15 Z"),
        Svg("Symbols", "Keyhole", "M50 0 A22 22 0 0 1 62 40 L72 100 L28 100 L38 40 A22 22 0 0 1 50 0 Z"),
        S("Symbols", "Paw print", Join(PathGeometry.Ellipse(50, 70, 24, 20), PathGeometry.Ellipse(18, 40, 11, 14), PathGeometry.Ellipse(38, 18, 11, 15), PathGeometry.Ellipse(62, 18, 11, 15), PathGeometry.Ellipse(82, 40, 11, 14))),
        S("Symbols", "Sun", Sun(12)),
        S("Symbols", "Flower", Flower(6)),
        Svg("Symbols", "Leaf", "M10 90 C10 40 40 5 95 5 C95 60 60 90 10 90 Z M10 90 L60 40"),
        S("Symbols", "Snowflake", Snowflake()),
        S("Symbols", "House", Poly([new(50, 0), new(100, 45), new(85, 45), new(85, 100), new(15, 100), new(15, 45), new(0, 45)])),

        // Badges & frames
        S("Badges & frames", "Scalloped badge", Scalloped(16)),
        S("Badges & frames", "Gear", Gear(12, 14)),
        S("Badges & frames", "Rounded frame", Join(PathGeometry.RoundRect(0, 0, 100, 70, 12, 12), PathGeometry.RoundRect(8, 8, 84, 54, 6, 6))),
        S("Badges & frames", "Circle frame", Join(PathGeometry.Ellipse(50, 50, 50, 50), PathGeometry.Ellipse(50, 50, 43, 43))),
        S("Badges & frames", "Hexagon frame", Join(Poly(Regular(6, 50, 0)), Poly(Regular(6, 42, 0)))),
        Svg("Badges & frames", "Ticket", "M0 0 L100 0 L100 22 A10 10 0 0 0 100 42 L100 64 L0 64 L0 42 A10 10 0 0 0 0 22 Z"),
        Svg("Badges & frames", "Banner", "M0 15 L15 15 L15 0 L85 0 L85 15 L100 15 L90 32 L100 50 L72 50 L72 35 L28 35 L28 50 L0 50 L10 32 Z"),
        Svg("Badges & frames", "Label", "M15 0 L85 0 L100 25 L85 50 L15 50 L0 25 Z"),
        S("Badges & frames", "Gift tag", Join(PathGeometry.FromSvgPathData("M25 0 L100 0 L100 60 L25 60 L0 30 Z"), PathGeometry.Ellipse(20, 30, 6, 6))),

        // Laser-made
        S("Laser-made", "Keychain tag", Join(PathGeometry.RoundRect(0, 0, 100, 40, 20, 20), PathGeometry.Ellipse(18, 20, 6, 6))),
        S("Laser-made", "Round keychain", Join(PathGeometry.Ellipse(50, 55, 45, 45), PathGeometry.Ellipse(50, 18, 5, 5))),
        S("Laser-made", "Coaster", PathGeometry.Ellipse(50, 50, 50, 50)),
        S("Laser-made", "Ornament", Join(Union(PathGeometry.Ellipse(50, 58, 42, 42), PathGeometry.RoundRect(40, 6, 20, 14, 3, 3)), PathGeometry.Ellipse(50, 13, 3.5, 3.5))),
        new("Laser-made", "Living hinge 100 × 40 (open lines)", LivingHinge(100, 40, 20, 3, 2.5), RealSize: true),
        new("Laser-made", "Living hinge 60 × 60 (open lines)", LivingHinge(60, 60, 25, 3, 2), RealSize: true),
    ];

    /// <summary>Categories in display order.</summary>
    public static IReadOnlyList<string> Categories => [.. BuiltIn.Select(s => s.Category).Distinct(), UserCategory];

    // ---- the user's shapes -------------------------------------------------------------

    /// <summary>The user's saved shapes in <paramref name="folder"/> (one design file each; unreadable files are skipped).</summary>
    public static List<LibraryShape> LoadUser(string folder)
    {
        var list = new List<LibraryShape>();
        if (!Directory.Exists(folder)) return list;
        foreach (var f in Directory.EnumerateFiles(folder, "*" + DocFile.Extension).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var s = DocFile.Load(f);
                var g = new PathGeometry(s.Shapes.OfType<PathShape>().SelectMany(x => x.World.Subpaths));
                if (!g.IsEmpty) list.Add(new LibraryShape(UserCategory, Path.GetFileNameWithoutExtension(f), Fit(g), f));
            }
            catch (Exception e) when (e is FormatException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Save shapes (world geometry, as one combined outline) to the user's library. Returns the file written.</summary>
    public static string SaveUser(string folder, string name, IEnumerable<PathShape> shapes)
    {
        Directory.CreateDirectory(folder);
        var g = Fit(new PathGeometry(shapes.SelectMany(x => x.World.Subpaths)));
        var s = DocState.New();
        s = Ops.AddShapes(s, [new PathShape { Name = name, LayerId = s.Layers[0].Id, Geometry = g }]);
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "Shape";
        var path = Path.Combine(folder, safe + DocFile.Extension);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{safe} {i}{DocFile.Extension}");
        DocFile.Save(s, path);
        return path;
    }
}
