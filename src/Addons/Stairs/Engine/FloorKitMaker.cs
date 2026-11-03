using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// Makes an upper floor for a room of the wall maker's walls and the way up to it. The plate sits
/// on the walls' tops, with a hole for each column (they stick up through it and keep it in place)
/// and an opening where the way up comes through. Open and solid stairs run along the first wall
/// and finish at the far end of the opening, tucked under the plate; spiral stairs turn on a pin in
/// a round opening; a leaning ladder rests on the opening's far edge, a hooked one hangs from it.
/// Anything that doesn't fit is reported rather than squeezed in.
/// </summary>
public static class FloorKitMaker
{
    /// <summary>Room left round a column in the plate's hole for it, on top of the fit (mm).</summary>
    public const double ColumnClearMm = 0.15;
    /// <summary>Gap left between the top of stairs tucked under the plate and the plate (mm).</summary>
    public const double TuckClearMm = 0.2;
    /// <summary>How far open stairs and solid stairs reach under the plate past the opening (mm).</summary>
    public const double TuckMm = 4;
    /// <summary>The plate's rim round the columns with <see cref="FloorEdge.OverColumns"/> (mm).</summary>
    public const double RimMm = 2;

    public static double WallThickness(FloorSpec s) => s.WallThickMm > 0 ? s.WallThickMm : Math.Max(0.3, s.WallSheetMm) * (s.TwoSidedWalls ? 2 : 1);

    /// <summary>The room's size on the plan, column centre to column centre (mm).</summary>
    public static (double X, double Y) Room(FloorSpec s) => (Math.Max(1, s.WallLengthMm) * Math.Max(1, s.WallsAlong), Math.Max(1, s.WallWidthMm) * Math.Max(1, s.WallsAcross));

    /// <summary>Where the way up climbs to: the floor's top with a floor, otherwise the height asked for.</summary>
    public static double Climb(FloorSpec s) => s.Floor ? s.WallHeightMm + Math.Max(0.5, s.SheetMm) : s.ClimbMm;

    public static ColumnShape ColumnShapeOf(FloorSpec s) => Connectors.ShapeFor(new WallSpec { Column = s.Column }, [0, 90]);

    /// <summary>A column's outline as it stands above the walls (its cap's, when it has one), centred on 0, 0.</summary>
    public static PathD ColumnTop(FloorSpec s) => Connectors.Outline(ColumnShapeOf(s), Math.Max(3, s.ColumnMm) + (s.ColumnCaps ? 2 * WallKitMaker.LipMm : 0));

    /// <summary>How far a column's hole in the plate reaches from the column's centre, along the walls (mm).</summary>
    public static double ColumnReach(FloorSpec s) => s.Columns ? Math.Max(3, s.ColumnMm) / 2 + (s.ColumnCaps ? WallKitMaker.LipMm : 0) + Math.Max(0, s.FitMm) + ColumnClearMm : 0;

    /// <summary>The columns' centres on the plan: every corner, and every joint between walls along the sides.</summary>
    public static List<PointD> ColumnCentres(FloorSpec s)
    {
        var (X, Y) = Room(s);
        int nx = Math.Max(1, s.WallsAlong), ny = Math.Max(1, s.WallsAcross);
        var list = new List<PointD>();
        for (int i = 0; i <= nx; i++) { list.Add(new(X * i / nx, 0)); list.Add(new(X * i / nx, Y)); }
        for (int j = 1; j < ny; j++) { list.Add(new(0, Y * j / ny)); list.Add(new(X, Y * j / ny)); }
        return list;
    }

    /// <summary>How many layers the wall maker stacks in a column for walls this high.</summary>
    public static int ColumnLayers(FloorSpec s) => Math.Max(1, (int)Math.Ceiling(s.WallHeightMm / Math.Max(0.1, s.WallSheetMm) - 0.02));

    public static FloorKit Make(FloorSpec s)
    {
        var k = new KitBuilder(s);
        var (X, Y) = Room(s);
        double wallT = WallThickness(s), climb = Climb(s), H = s.WallHeightMm;
        var empty = new FloorLayout(X, Y, wallT, climb, H, [], 0, 0);
        if (s.Floor)
        {
            if (H < 5) { k.Bad("The walls are too low: they must be at least 5 mm high."); return Kit(k, empty); }
            if (Math.Min(X, Y) < 20) { k.Bad("The room is too small: make the walls longer."); return Kit(k, empty); }
            if (s.Columns && Math.Min(s.WallLengthMm, s.WallWidthMm) < 2 * ColumnReach(s) + 4) { k.Bad("The walls are too short for their columns: check the walls' length and the column width."); return Kit(k, empty); }
        }
        if (s.Way != WayUp.None && climb < 3 * k.T) { k.Bad($"There's too little to climb ({climb:0.#} mm): make it higher."); return Kit(k, empty); }

        // Where the way up goes and the opening for it.
        var way = s.Way == WayUp.None ? null : WayUps.Plan(k, climb);
        var hatch = new PathsD();
        double far = 0, near = 0, centreX = 0, centreY = 0;
        if (s.Floor && s.Hatch)
        {
            var (hx, hy, hl, hw, round) = HatchBox(s, way, wallT);
            if (round)
            {
                double r = hl / 2;
                hatch.Add(Geo.Circle(hx + r, hy + r, r));
                centreX = hx + r; centreY = hy + r;
            }
            else hatch.Add(Geo.Rect(hx, hy, hx + hl, hy + hw));
            far = hx + hl;
            near = hy;
            CheckHatch(k, s, hatch, X, Y, wallT);
        }
        else if (s.Floor && way is not null)
        {
            // No opening: the way up stands outside, against the first wall's outside face.
            k.Note("There's no opening in the floor: the way up is made to reach the floor's top from outside the walls.");
            far = -Plate.EdgeBeyond(s, wallT) - 0.3;
            near = Math.Max(0, Y / 2 - way.Width / 2);
        }

        if (s.Floor) Plate.Make(k, hatch, X, Y, wallT);
        int steps = 0;
        double rise = 0;
        if (way is not null)
        {
            (steps, rise) = WayUps.Make(k, way, climb, s.Floor && s.Hatch, far, near, centreX, centreY);
            if (s.Floor && s.Hatch) CheckWayInRoom(k, way, far, near, centreX, centreY, X, Y, wallT);
        }
        if (s.Floor) Ghosts(k, X, Y, wallT, climb);
        return Kit(k, new FloorLayout(X, Y, wallT, climb, H, hatch, steps, rise));
    }

    static FloorKit Kit(KitBuilder k, FloorLayout layout) => new(k.Parts, k.Findings, k.Ghosts, layout);

    /// <summary>
    /// The opening: the size asked for, or sized to the way up and put in the first corner along the
    /// first wall, clear of the corner column, and back far enough for a leaning ladder's foot.
    /// </summary>
    static (double X, double Y, double Length, double Width, bool Round) HatchBox(FloorSpec s, WayPlan? way, double wallT)
    {
        bool round = way?.Kind == WayUp.Spiral;
        if (!s.HatchAuto || way is null)
        {
            double l = Math.Max(4, s.HatchLengthMm), w = round ? l : Math.Max(4, s.HatchWidthMm);
            return (s.HatchXMm, s.HatchYMm, l, w, round);
        }
        double clearX = (s.Columns ? ColumnReach(s) : wallT / 2) + 2;
        double y = wallT / 2 + 0.6;
        double length = way.HatchLength, width = way.HatchWidth;
        // The way up mustn't reach back past the corner column either.
        double clearBack = s.Columns ? Math.Max(3, s.ColumnMm) / 2 + 0.6 : wallT / 2 + 1;
        double x = Math.Max(clearX, clearBack + way.Behind - length);
        return (x, y, length, round ? length : width, round);
    }

    static void CheckHatch(KitBuilder k, FloorSpec s, PathsD hatch, double X, double Y, double wallT)
    {
        var b = Geo.Bounds(hatch);
        double inset = wallT / 2;
        if (b.left < inset - 1e-6 || b.top < inset - 1e-6 || b.right > X - inset + 1e-6 || b.bottom > Y - inset + 1e-6)
            k.Warn("The opening reaches over a wall, so the floor doesn't rest on that wall there: keep it inside the room.");
        if (s.Columns)
        {
            var cols = new PathsD(ColumnCentres(s).Select(c => Geo.Map(ColumnTop(s), p => new PointD(p.x + c.x, p.y + c.y))));
            if (Geo.Area(Geo.Intersect(Geo.Offset(hatch, KitBuilder.Bridge), cols)) > 1e-3)
                k.Bad("The opening runs into a column's hole: move it away from the corner or make it smaller.");
        }
    }

    /// <summary>The way up must stand inside the room, clear of the walls and the columns.</summary>
    static void CheckWayInRoom(KitBuilder k, WayPlan way, double far, double near, double cx, double cy, double X, double Y, double wallT)
    {
        var plan = way.Footprint(far, near, cx, cy);
        if (plan.Count == 0) return;
        var room = Geo.Rect(wallT / 2 + 0.2, wallT / 2 + 0.2, X - wallT / 2 - 0.2, Y - wallT / 2 - 0.2);
        var outside = Geo.Subtract(plan, [room]);
        var s = k.Spec;
        if (s.Columns)
        {
            var cols = new PathsD(ColumnCentres(s).Select(c => Geo.Map(Connectors.Outline(ColumnShapeOf(s), Math.Max(3, s.ColumnMm) + 0.4), p => new PointD(p.x + c.x, p.y + c.y))));
            outside = Geo.Union(outside, Geo.Intersect(plan, cols));
        }
        if (Geo.Area(outside) > 0.05)
            k.Bad($"The {way.Name} run{(way.Name.EndsWith('s') ? "" : "s")} into a wall or a column: make {(way.Name.EndsWith('s') ? "them" : "it")} smaller, the room bigger, or move the opening.");
    }

    /// <summary>The walls and their columns, shown faintly in the put-together view.</summary>
    static void Ghosts(KitBuilder k, double X, double Y, double wallT, double floorTop)
    {
        var s = k.Spec;
        double H = s.WallHeightMm, Tw = Math.Max(0.3, s.WallSheetMm), col = s.Columns ? Math.Max(3, s.ColumnMm) / 2 : 0;
        int nx = Math.Max(1, s.WallsAlong), ny = Math.Max(1, s.WallsAcross);
        // Walls along x (at y = 0 and y = Y), then across (at x = 0 and x = X): upright slabs between the columns' faces.
        for (int i = 0; i < nx; i++)
            foreach (var y in new[] { 0, Y })
            {
                double x0 = X * i / nx + col, x1 = X * (i + 1) / nx - col;
                k.Ghosts.Add(new Ghost("Wall", [Geo.Rect(0, 0, x1 - x0, H)], Pose.Upright(x0, y, 0), wallT));
            }
        for (int j = 0; j < ny; j++)
            foreach (var x in new[] { 0, X })
            {
                double y0 = Y * j / ny + col, y1 = Y * (j + 1) / ny - col;
                k.Ghosts.Add(new Ghost("Wall", [Geo.Rect(0, 0, y1 - y0, H)], new Pose(V3.Room(x, y0, 0), -V3.UnitZ, V3.UnitY), wallT));
            }
        if (!s.Columns) return;
        int layers = ColumnLayers(s);
        double body = layers * Tw;
        var outline = Connectors.Outline(ColumnShapeOf(s), Math.Max(3, s.ColumnMm));
        foreach (var c in ColumnCentres(s))
        {
            k.Ghosts.Add(new Ghost("Column", [outline], Pose.Flat(c.x, c.y, body / 2), body));
            if (s.ColumnCaps) k.Ghosts.Add(new Ghost("Column cap", [ColumnTop(s)], Pose.Flat(c.x, c.y, body + Tw / 2), Tw));
        }
        double top = body + (s.ColumnCaps ? Tw : 0), diff = top - floorTop;
        if (Math.Abs(diff) < 0.05)
            k.Note($"The columns{(s.ColumnCaps ? "' caps" : "")} finish level with the floor's top: stand the next storey's columns on them, with a pin through both.");
        else if (diff > 0)
            k.Note(s.ColumnCaps && Math.Abs(Tw - k.T) < 0.01
                ? $"The columns' caps stand {diff:0.#} mm above the floor. For them to finish level (so the next storey's columns can stand on them), make the walls {layers * Tw:0.#} mm high: a whole number of sheets."
                : $"The columns stand {diff:0.#} mm above the floor.");
        else
            k.Note($"The columns finish {-diff:0.#} mm below the floor's top: the next storey's columns drop into the holes and stand on them.");
    }
}
