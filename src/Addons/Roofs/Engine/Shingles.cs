using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// The textures etched on a roof's slopes, drawn on a face whose bottom edge (y = 0) is the eave and
/// whose y runs up the slope: courses of shingles, slates or tiles laid from the eave up, each course
/// overlapping the one below it so only its lower part shows. Planks and corrugated metal are the wall
/// maker's own textures, turned to run down the slope.
/// </summary>
public static class Shingles
{
    public const string Plain = "Plain";

    public static readonly IReadOnlyList<TexturePreset> Presets =
    [
        new("Slate", "Slate width", 6, "Showing", 4.5, true, 0.5, false, "Even rectangular slates, each course moved along half a slate."),
        new("Wooden shingles", "Shingle width", 5, "Showing", 5, true, 0.6, true, "Split wooden shakes of uneven width with ragged ends and a little grain."),
        new("Fish scale", "Scale width", 6, "Showing", 4.5, true, 0.5, false, "Round-ended shingles, for towers, turrets and fairy-tale cottages."),
        new("Clay tiles", "Tile width", 6, "Showing", 6, true, 0.6, false, "Half-round clay tiles in straight columns, Mediterranean style."),
        new("Thatch", "Course height", 8, null, 0, true, 0.8, true, "Straw laid in ragged courses, scored with straw lines."),
        new("Wood planks", "Plank width", 5, "Plank length", 35, true, 0.5, true, "Boards running down the slope, with grain and nail holes."),
        new("Corrugated metal", "Ridge pitch", 2.5, null, 0, false, 0, false, "Ridges running down the slope, for sheds and lean-tos."),
        new(Plain, null, 0, null, 0, false, 0, false, "No texture."),
    ];

    public static TexturePreset? Preset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    enum Bottom { Flat, Round }

    /// <summary>The texture over a face <paramref name="w"/> wide and <paramref name="h"/> up the slope (with a little over the edges; it's clipped to the piece afterwards).</summary>
    public static TextureParts Make(string name, double a, double b, double groove, int seed, double w, double h)
    {
        a = Math.Max(0.5, a);
        b = Math.Max(0.5, b);
        var p = Preset(name);
        double g = Math.Clamp(groove, 0.05, (p?.SizeBLabel is null ? a : Math.Min(a, b)) / 2);
        var rnd = new Random(seed);
        return name switch
        {
            "Slate" => Courses(w, h, a, b, g, 0.5, Bottom.Flat, false, rnd, grain: false, rolls: false),
            "Wooden shingles" => Courses(w, h, a, b, g, 0.5, Bottom.Flat, true, rnd, grain: true, rolls: false),
            "Fish scale" => Courses(w, h, a, b, g, 0.5, Bottom.Round, false, rnd, grain: false, rolls: false),
            "Clay tiles" => Courses(w, h, a, b, g, 0, Bottom.Round, false, rnd, grain: false, rolls: true),
            "Thatch" => Thatch(w, h, a, g, rnd),
            // The wall maker's: upright planks run up the face, which here is down the slope.
            "Wood planks" => Textures.Make(new WallSpec { Texture = "Wood planks (upright)", SizeA = a, SizeB = b, GrooveMm = g, Seed = seed }, new Face(w, h, 0, w, h)),
            "Corrugated metal" => Textures.Make(new WallSpec { Texture = "Corrugated metal", SizeA = a }, new Face(w, h, 0, w, h)),
            _ => TextureParts.None,
        };
    }

    /// <summary>
    /// Courses <paramref name="b"/> apart from the eave up; each shingle is drawn reaching well up under
    /// the course above, and what shows is what the course above doesn't cover.
    /// </summary>
    static TextureParts Courses(double w, double h, double a, double b, double g, double shift, Bottom bottom, bool random, Random rnd, bool grain, bool rolls)
    {
        var courses = new List<PathsD>();
        int rows = (int)Math.Ceiling(h / b) + 2;
        for (int j = 0; j < rows; j++)
        {
            double y = j * b - b * 0.5, off = j % 2 == 1 ? shift * a : 0;
            var course = new PathsD();
            double x = -a - off - (random ? a * rnd.NextDouble() : 0);
            while (x < w + a)
            {
                double sw = random ? a * (0.6 + 0.8 * rnd.NextDouble()) : a;
                double sy = random ? y + (rnd.NextDouble() - 0.5) * 0.25 * b : y;
                course.Add(Shingle(x, x + sw, sy, sy + 2.2 * b, bottom, b));
                x += sw;
            }
            courses.Add(course);
        }
        var raised = new PathsD();
        var lines = new List<PathD>();
        for (int j = 0; j < courses.Count; j++)
        {
            var above = j + 1 < courses.Count ? Geo.Union(courses[j + 1]) : new PathsD();
            foreach (var s in courses[j])
            {
                var seen = Geo.Subtract([s], above);
                var shrunk = Geo.Offset(seen, -g / 2);
                raised.AddRange(shrunk);
                if (!grain && !rolls) continue;
                var box = Geo.Bounds(shrunk);
                double bw = box.right - box.left, bh = box.bottom - box.top;
                if (bw < 1.5 || bh < 1.5) continue;
                if (rolls)
                {
                    // The highlight down the middle of each half-round tile.
                    double cx = (box.left + box.right) / 2;
                    lines.AddRange(Geo.ClipLines([[new(cx, box.top - 1), new(cx, box.bottom + 1)]], Geo.Offset(shrunk, -0.4)));
                }
                else
                {
                    // A grain line or two, a little wavy, up the shingle.
                    int n = bw > 4 ? 2 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        double gx = box.left + bw * (k + 1) / (n + 1) + (rnd.NextDouble() - 0.5) * bw * 0.2, phase = rnd.NextDouble() * 6.28;
                        var line = new PathD();
                        for (double t = box.top + 0.6; t <= box.bottom - 0.4; t += 0.5) line.Add(new PointD(gx + 0.15 * Math.Sin(t * 1.3 + phase), t));
                        if (line.Count >= 2) lines.AddRange(Geo.ClipLines([line], Geo.Offset(shrunk, -0.3)));
                    }
                }
            }
        }
        return new(raised, [], lines);
    }

    /// <summary>A shingle from x0 to x1, its lower end at y0 (flat or rounded), up to y1.</summary>
    static PathD Shingle(double x0, double x1, double y0, double y1, Bottom bottom, double b)
    {
        if (bottom == Bottom.Flat) return Geo.Rect(x0, y0, x1, y1);
        double rx = (x1 - x0) / 2, ry = Math.Min(rx, b * 0.8), cx = (x0 + x1) / 2;
        var p = new PathD();
        // The rounded lower end: half an ellipse from the left side round to the right.
        for (int i = 0; i <= 16; i++)
        {
            double t = Math.PI + Math.PI * i / 16;
            p.Add(new PointD(cx + rx * Math.Cos(t), y0 + ry + ry * Math.Sin(t)));
        }
        p.Add(new PointD(x1, y1));
        p.Add(new PointD(x0, y1));
        return p;
    }

    /// <summary>Straw in courses <paramref name="course"/> high with ragged lower edges, and straw lines up each course.</summary>
    static TextureParts Thatch(double w, double h, double course, double g, Random rnd)
    {
        int rows = (int)Math.Ceiling(h / course) + 2;
        double step = Math.Max(1, course / 3);
        int n = (int)Math.Ceiling((w + 2) / step) + 2;
        double[] Edge(double y) => [.. Enumerable.Range(0, n).Select(_ => y + (rnd.NextDouble() - 0.5) * 0.3 * course)];
        var edges = Enumerable.Range(0, rows + 1).Select(j => Edge(j * course - course * 0.4)).ToList();
        var raised = new PathsD();
        var lines = new List<PathD>();
        for (int j = 0; j < rows; j++)
        {
            var band = new PathD();
            for (int i = 0; i < n; i++) band.Add(new PointD(-1 + i * step, edges[j][i]));
            for (int i = n - 1; i >= 0; i--) band.Add(new PointD(-1 + i * step, edges[j + 1][i]));
            var shrunk = Geo.Offset([band], -g / 2);
            raised.AddRange(shrunk);
            // Straw: short strokes up from the course's lower edge, leaning a little this way and that.
            for (double x = -1 + rnd.NextDouble(); x < w + 1; x += 0.9 + 0.6 * rnd.NextDouble())
            {
                double t = Math.Clamp((x + 1) / step, 0, n - 1.001);
                int i = (int)t;
                double y0 = edges[j][i] + (edges[j][i + 1] - edges[j][i]) * (t - i) + g;
                double len = course * (0.45 + 0.4 * rnd.NextDouble()), lean = (rnd.NextDouble() - 0.5) * 0.6;
                lines.AddRange(Geo.ClipLines([[new(x, y0), new(x + lean, y0 + len)]], shrunk));
            }
        }
        return new(raised, [], lines);
    }

    /// <summary>
    /// The texture on a piece: what's etched (the gaps between the raised parts, as areas, or round
    /// them as lines) and what's scored, kept inside <paramref name="region"/>.
    /// </summary>
    public static (PathsD Etched, List<PathD> Lines) Apply(TextureParts tex, PathsD region, EtchStyle style)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (region.Count == 0) return (etched, lines);
        if (style == EtchStyle.Grooves)
        {
            var box = Geo.Bounds(region);
            var gaps = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(box.left - 1, box.top - 1, box.right + 1, box.bottom + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(gaps, tex.Etched), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), region));
            lines.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), region));
        }
        lines.AddRange(Geo.ClipLines(tex.Lines, region));
        return (Geo.Union(etched), lines);
    }
}
