using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Where a building's design lands on the map: its size in squares, turned; a point of its plan on the
/// map; and the squares it stands on.
/// </summary>
public static class CityFootprint
{
    /// <summary>The design's own extent (squares): every wall, laid floor and roof, from 0, 0.</summary>
    public static (int W, int D) Extent(HouseDesign d)
    {
        int x1 = 0, y1 = 0;
        foreach (var l in d.Levels)
        {
            foreach (var e in l.Walls) { x1 = Math.Max(x1, Math.Max(e.A.X, e.B.X)); y1 = Math.Max(y1, Math.Max(e.A.Y, e.B.Y)); }
            foreach (var f in l.Floor) { x1 = Math.Max(x1, f.X + 1); y1 = Math.Max(y1, f.Y + 1); }
            if (l.Yard is int yard && yard > 0 && l.Walls.Count > 0)
            {
                x1 = Math.Max(x1, l.Walls.Max(e => Math.Max(e.A.X, e.B.X)) + yard);
                y1 = Math.Max(y1, l.Walls.Max(e => Math.Max(e.A.Y, e.B.Y)) + yard);
            }
        }
        foreach (var r in d.Roofs) { x1 = Math.Max(x1, r.X1); y1 = Math.Max(y1, r.Y1); }
        return (Math.Max(1, x1), Math.Max(1, y1));
    }

    /// <summary>The building's size on the map (squares across, down), turned.</summary>
    public static (int W, int D) Size(CityBuilding b)
    {
        var (w, d) = Extent(b.Design);
        return (b.Turn & 1) == 1 ? (d, w) : (w, d);
    }

    /// <summary>A point of the design's plan (squares) where it is on the map: turned, shifted, then tilted about the middle.</summary>
    public static (double X, double Y) ToMap(CityBuilding b, double x, double y)
    {
        var (w, d) = Extent(b.Design);
        var (u, v) = (((b.Turn % 4) + 4) % 4) switch
        {
            1 => (d - y, x),
            2 => (w - x, d - y),
            3 => (y, w - x),
            _ => (x, y),
        };
        double mx = u + b.X + b.ShiftX, my = v + b.Y + b.ShiftY;
        if (b.TiltDeg == 0) return (mx, my);
        var (cx, cy) = Centre(b);
        double a = b.TiltDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), dx = mx - cx, dy = my - cy;
        return (cx + dx * c - dy * s, cy + dx * s + dy * c);
    }

    /// <summary>A point on the map in the design's own plan (the other way to <see cref="ToMap"/>).</summary>
    public static (double X, double Y) FromMap(CityBuilding b, double mx, double my)
    {
        var (w, d) = Extent(b.Design);
        if (b.TiltDeg != 0)
        {
            var (cx, cy) = Centre(b);
            double a = -b.TiltDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), dx = mx - cx, dy = my - cy;
            mx = cx + dx * c - dy * s;
            my = cy + dx * s + dy * c;
        }
        double u = mx - b.X - b.ShiftX, v = my - b.Y - b.ShiftY;
        return (((b.Turn % 4) + 4) % 4) switch
        {
            1 => (v, d - u),
            2 => (w - u, d - v),
            3 => (w - v, u),
            _ => (u, v),
        };
    }

    /// <summary>The middle of the building on the map (squares): the tilt turns about it.</summary>
    public static (double X, double Y) Centre(CityBuilding b)
    {
        var (w, d) = Size(b);
        return (b.X + b.ShiftX + w / 2.0, b.Y + b.ShiftY + d / 2.0);
    }

    /// <summary>The building's four corners on the map (squares), clockwise from the plan's top left.</summary>
    public static (double X, double Y)[] Corners(CityBuilding b)
    {
        var (w, d) = Extent(b.Design);
        return [ToMap(b, 0, 0), ToMap(b, w, 0), ToMap(b, w, d), ToMap(b, 0, d)];
    }

    /// <summary>Whether the building sits square to the grid, on whole squares.</summary>
    public static bool OnGrid(CityBuilding b) => b.TiltDeg == 0 && b.ShiftX == 0 && b.ShiftY == 0;

    /// <summary>The squares of the design's own plan its ground floor covers: inside its rooms, or floor laid by hand.</summary>
    public static HashSet<(int X, int Y)> OwnCells(HouseDesign d)
    {
        var cells = new HashSet<(int, int)>();
        if (d.Levels.Count == 0) return cells;
        var ground = d.Levels[0];
        var plan = ground.Shape();
        var (w, dp) = Extent(d);
        for (int y = 0; y < dp; y++)
            for (int x = 0; x < w; x++)
                if (plan.RoomAt(x + 0.5, y + 0.5) >= 0) cells.Add((x, y));
        foreach (var f in ground.Floor) cells.Add((f.X, f.Y));
        if (cells.Count == 0)
            for (int y = 0; y < dp; y++)
                for (int x = 0; x < w; x++) cells.Add((x, y));
        return cells;
    }

    /// <summary>The map squares a building stands on (every square it covers any real part of, when it's shifted or tilted).</summary>
    public static HashSet<(int X, int Y)> Cells(CityBuilding b)
    {
        var res = new HashSet<(int, int)>();
        bool onGrid = OnGrid(b);
        foreach (var (x, y) in OwnCells(b.Design))
        {
            if (onGrid)
            {
                var (mx, my) = ToMap(b, x + 0.5, y + 0.5);
                res.Add(((int)Math.Floor(mx), (int)Math.Floor(my)));
                continue;
            }
            // Off the grid, a square of the plan can lie over up to four map squares: sample it.
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    var (mx, my) = ToMap(b, x + 0.2 + 0.3 * i, y + 0.2 + 0.3 * j);
                    res.Add(((int)Math.Floor(mx), (int)Math.Floor(my)));
                }
        }
        return res;
    }

    /// <summary>Is the map point (squares) on the building: inside the rectangle it takes up.</summary>
    public static bool Hit(CityBuilding b, double x, double y)
    {
        var (w, d) = Extent(b.Design);
        var (u, v) = FromMap(b, x, y);
        return u >= 0 && v >= 0 && u <= w && v <= d;
    }

    /// <summary>The buildings that stand on some of the same squares, as pairs.</summary>
    public static List<(CityBuilding A, CityBuilding B)> Overlaps(CityMap m)
    {
        var owner = new Dictionary<(int, int), CityBuilding>();
        var res = new List<(CityBuilding, CityBuilding)>();
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var b in m.Buildings)
            foreach (var c in Cells(b))
            {
                if (owner.TryGetValue(c, out var o) && o.Id != b.Id)
                {
                    if (seen.Add((o.Id, b.Id))) res.Add((o, b));
                }
                else owner[c] = b;
            }
        return res;
    }

    /// <summary>The design moved so the top left corner of what's drawn is at 0, 0.</summary>
    public static HouseDesign Normalised(HouseDesign d)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue;
        foreach (var l in d.Levels)
        {
            foreach (var e in l.Walls) { x0 = Math.Min(x0, Math.Min(e.A.X, e.B.X)); y0 = Math.Min(y0, Math.Min(e.A.Y, e.B.Y)); }
            foreach (var f in l.Floor) { x0 = Math.Min(x0, f.X); y0 = Math.Min(y0, f.Y); }
            // A yard reaches past the walls: keep room for it.
            if (l.Yard is int yard && yard > 0 && l.Walls.Count > 0)
            {
                x0 = Math.Min(x0, l.Walls.Min(e => Math.Min(e.A.X, e.B.X)) - yard);
                y0 = Math.Min(y0, l.Walls.Min(e => Math.Min(e.A.Y, e.B.Y)) - yard);
            }
        }
        foreach (var r in d.Roofs) { x0 = Math.Min(x0, r.X0); y0 = Math.Min(y0, r.Y0); }
        if (x0 == int.MaxValue || (x0 == 0 && y0 == 0)) return d;
        return d.Moved(-x0, -y0);
    }
}
