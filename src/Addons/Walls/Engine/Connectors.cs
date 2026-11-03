using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// The columns walls slide into. A column is a stack of layers cut from the same sheet, each with a
/// slot for every wall (as wide as the wall plus the fit) and a hole up the middle for a pin that keeps
/// the stack lined up; a wider cap can go on top and a wider foot at the bottom.
/// </summary>
public static class Connectors
{
    public sealed record Kind(ConnectorKind Id, string Name, string Short);

    public static readonly IReadOnlyList<Kind> Kinds =
    [
        new(ConnectorKind.Straight, "Straight (180°)", "straight"),
        new(ConnectorKind.Corner, "Corner (90°)", "corner"),
        new(ConnectorKind.Angle135, "Wide (135°)", "135°"),
        new(ConnectorKind.Sharp45, "Sharp (45°)", "45°"),
        new(ConnectorKind.Tee, "T junction", "T"),
        new(ConnectorKind.Cross, "Cross (+)", "cross"),
        new(ConnectorKind.EndCap, "End cap", "end"),
        new(ConnectorKind.Custom, "Your angle", "angled"),
    ];

    /// <summary>The directions its slots point (degrees, anticlockwise from the first wall).</summary>
    public static double[] Directions(ConnectorKind k, double custom) => k switch
    {
        ConnectorKind.Straight => [0, 180],
        ConnectorKind.Corner => [0, 90],
        ConnectorKind.Angle135 => [0, 135],
        ConnectorKind.Sharp45 => [0, 45],
        ConnectorKind.Tee => [0, 90, 180],
        ConnectorKind.Cross => [0, 90, 180, 270],
        ConnectorKind.EndCap => [0],
        _ => [0, Math.Clamp(custom, 20, 340)],
    };

    /// <summary>Square when every wall meets a flat side of a square, an octagon for 45° steps, otherwise round.</summary>
    public static ColumnShape ShapeFor(WallSpec s, double[] dirs)
    {
        if (s.Column != ColumnShape.Auto) return s.Column;
        static bool Steps(double[] d, double step) => d.All(a => Math.Abs(Math.IEEERemainder(a, step)) < 1e-6);
        return Steps(dirs, 90) ? ColumnShape.Square : Steps(dirs, 45) ? ColumnShape.Octagon : ColumnShape.Round;
    }

    /// <summary>The column's outline, <paramref name="across"/> mm across its flats.</summary>
    public static PathD Outline(ColumnShape shape, double across)
    {
        double r = across / 2;
        switch (shape)
        {
            case ColumnShape.Square: return Geo.Rect(-r, -r, r, r);
            case ColumnShape.Octagon:
            {
                double c = r / Math.Cos(Math.PI / 8);
                return [.. Enumerable.Range(0, 8).Select(i => new PointD(c * Math.Cos(Math.PI / 8 + i * Math.PI / 4), c * Math.Sin(Math.PI / 8 + i * Math.PI / 4)))];
            }
            default: return Geo.Circle(0, 0, r);
        }
    }

    /// <summary>How far from the middle the outline's edge is, looking along a direction (degrees).</summary>
    public static double Reach(PathD outline, double deg)
    {
        double a = deg * Math.PI / 180, dx = Math.Cos(a), dy = Math.Sin(a), best = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            // Ray (t·d) meets edge p + u·(q − p).
            double ex = q.x - p.x, ey = q.y - p.y, den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-12) continue;
            double t = (p.x * ey - p.y * ex) / den, u = (p.x * dy - p.y * dx) / den;
            if (t > 0 && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Max(best, t);
        }
        return best;
    }

    public static List<Part> Make(WallSpec s, List<Finding> findings)
    {
        var parts = new List<Part>();
        double wallT = WallKitMaker.WallThickness(s), slotW = wallT + Math.Max(0, s.FitMm), depth = Math.Max(0.5, s.SlotDepthMm);
        int layers = WallKitMaker.ColumnLayers(s);
        bool any = false;
        // The 3D view shows one wall in the first kind's slot at its left end and the second kind's (or the first's) at its right.
        var kinds = Kinds.Where(k => s.ConnectorCount(k.Id) > 0).ToList();
        double T = s.ThicknessMm, c2e = WallKitMaker.CentreToEnd(s), L = WallKitMaker.PanelLength(s);
        var shown = new List<(ConnectorKind Kind, double X, double Yaw)>();
        if (kinds.Count > 0)
        {
            var left = kinds[0].Id;
            var right = kinds[kinds.Count > 1 ? 1 : 0].Id;
            shown.Add((left, -c2e, ShowYaw(Directions(left, s.CustomAngleDeg), 0)));
            shown.Add((right, L + c2e, ShowYaw(Directions(right, s.CustomAngleDeg), 180)));
        }
        foreach (var kind in Kinds)
        {
            int count = s.ConnectorCount(kind.Id);
            if (count <= 0) continue;
            any = true;
            var dirs = Directions(kind.Id, s.CustomAngleDeg);
            var c = Column(s, dirs, slotW, depth, out var problem);
            if (problem is not null) { findings.Add(new(Severity.Bad, $"{kind.Name}: {problem}")); continue; }
            string name = $"{Cap(kind.Short)} column";
            int body = s.Foot ? layers - 1 : layers;
            var at = shown.Where(x => x.Kind == kind.Id).ToList();
            List<Model.Pose> Layers(IEnumerable<int> ks) => [.. at.SelectMany(p => ks.Select(k => Plate(p.X, k * T + T / 2, p.Yaw)))];
            if (body > 0) parts.Add(new Part($"{name} layer", c.Body, [], [], body * count) { Poses = Layers(Enumerable.Range(s.Foot ? 1 : 0, body)) });
            if (s.Foot) parts.Add(new Part($"{name} foot", c.Foot, [], [], count) { Poses = Layers([0]) });
            if (s.Cap) parts.Add(new Part($"{name} cap", c.Top, [], [], count) { Poses = Layers([layers]) });
        }
        if (any)
        {
            double tall = layers * s.ThicknessMm;
            findings.Add(new(Severity.Note, $"Each column is {layers} layers ({tall:0.#} mm) tall{(s.Cap ? ", plus its cap" : "")}, for a {s.HeightMm:0.#} mm wall."));
            if (s.PinMm > 0) findings.Add(new(Severity.Note, $"Line each column's layers up on a {s.PinMm:0.#} mm pin (a cocktail stick or wire) through the middle, and glue them."));
        }
        return parts;
    }

    /// <summary>
    /// How to turn a column in the 3D view so one of its slots points at the wall (<paramref name="toward"/>, degrees)
    /// and its other slots point away from you as much as they can.
    /// </summary>
    public static double ShowYaw(double[] dirs, double toward)
    {
        double best = toward - dirs[0], score = double.MinValue;
        foreach (var d in dirs)
        {
            double yaw = toward - d;
            // Away from you is −z, which is 90° (anticlockwise from x, seen from above).
            double sc = dirs.Where(o => o != d).Sum(o => Math.Sin((o + yaw) * Math.PI / 180));
            if (sc > score + 1e-9) { score = sc; best = yaw; }
        }
        return best;
    }

    /// <summary>A layer lying flat, its middle at (x, y, 0), its first slot pointing along <paramref name="yaw"/> (degrees, anticlockwise seen from above).</summary>
    static Model.Pose Plate(double x, double y, double yaw)
    {
        double a = yaw * Math.PI / 180, b = a + Math.PI / 2;
        return new Model.Pose(new Model.V3(x, y, 0), new Model.V3(Math.Cos(a), 0, -Math.Sin(a)), new Model.V3(Math.Cos(b), 0, -Math.Sin(b)));
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public sealed record ColumnLayers(PathsD Body, PathsD Foot, PathsD Top);

    /// <summary>A column's layers, or why it can't be made.</summary>
    public static ColumnLayers Column(WallSpec s, double[] dirs, double slotW, double depth, out string? problem)
    {
        problem = null;
        var shape = ShapeFor(s, dirs);
        double across = Math.Max(3, s.ColumnMm);
        var outline = Outline(shape, across);
        var bigger = Outline(shape, across + 2 * WallKitMaker.LipMm);
        PathsD Slots(double past)
        {
            var slots = new PathsD();
            foreach (var d in dirs)
            {
                double reach = Reach(outline, d);
                slots.AddRange(Geo.Turn([Geo.Rect(reach - depth, -slotW / 2, reach + past, slotW / 2)], d));
            }
            return slots;
        }
        var body = Slots(1);
        var pin = s.PinMm > 0 ? new PathsD { Geo.Circle(0, 0, s.PinMm / 2) } : [];
        // Checks: material each side of the slots, slots clear of each other and of the pin, one piece left.
        double side = dirs.Min(d => Reach(outline, d + 90));
        if (side - slotW / 2 < 1) problem = $"a {slotW:0.##} mm slot leaves too little column either side: make the column wider than {slotW + 2:0.#} mm.";
        else if (dirs.Length > 1 && Geo.Area(Geo.Union(body)) < body.Sum(p => Math.Abs(Clipper.Area(p))) - 1e-3)
            problem = "the slots run into each other in the middle: make the column wider or the slots shallower.";
        else if (pin.Count > 0 && Geo.Area(Geo.Intersect(Geo.Offset(pin, 0.8), Geo.Union(body))) > 1e-4)
            problem = "the pin hole runs into a slot: make the pin thinner, the column wider or the slots shallower.";
        var layer = Geo.Subtract(Geo.Subtract([outline], Geo.Union(body)), pin);
        if (problem is null && Geo.Pieces(layer) != 1) problem = "the slots cut the column apart: make it wider or the slots shallower.";
        var foot = Geo.Subtract(Geo.Subtract([bigger], Geo.Union(Slots(WallKitMaker.LipMm + 1))), pin);
        var top = Geo.Subtract([bigger], pin);
        return new(layer, foot, top);
    }
}
