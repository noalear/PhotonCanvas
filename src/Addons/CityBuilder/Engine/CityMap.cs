using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What a square of the map's ground is.</summary>
public enum Ground { Grass, Dirt, Road, Paving, Sand, Water, Rock, Field, Floor }

/// <summary>What a fence or wall along the grid lines is.</summary>
public enum FenceKind { Fence, Hedge, StoneWall, CityWall }

/// <summary>
/// Small things dotted about the map, shown in 3D: trees, rocks, a well, carts and so on, and the town's
/// bigger pieces: horse troughs, a players' stage, market stalls, outdoor bars, notice boards and guard towers.
/// </summary>
public enum PropKind { Tree, Pine, Bush, Boulder, Well, Cart, Crates, Lamp, Statue, Trough, Stage, Stall, Bar, NoticeBoard, GuardTower, Fountain, Gallows }

/// <summary>
/// A building on the map: the building editor's design (its plan's top left corner at 0, 0), where its top
/// left corner is on the map (squares, plus a shift of less than a square), how many quarter turns it's
/// turned (clockwise seen from above) and any further tilt in degrees about its middle (a house read off
/// a map that sits at an angle to the grid). The turn and tilt only place it: the pieces are cut from the
/// design as it is. <see cref="PrefabId"/> is the building editor's saved building it came from, if any.
/// </summary>
public sealed record CityBuilding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "House";
    /// <summary>What it is: house, shop, tavern, temple… (the building editor's types, or anything typed).</summary>
    public string Type { get; init; } = "House";
    /// <summary>The part of town it belongs to: residential, market, docks…</summary>
    public string District { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public Guid? PrefabId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Turn { get; init; }
    /// <summary>How far past X, Y its corner really is (squares; 0 on a map drawn to the grid).</summary>
    public double ShiftX { get; init; }
    public double ShiftY { get; init; }
    /// <summary>Its turn past the quarter turns, clockwise about its middle (degrees; 0 on a map drawn to the grid).</summary>
    public double TiltDeg { get; init; }
    public HouseDesign Design { get; init; } = new();
}

/// <summary>A fence, hedge or wall along one square's edge of the grid, from grid point (X, Y) one square across (or down).</summary>
public readonly record struct FenceEdge(int X, int Y, bool Across);

/// <summary>A point on the map (squares from its top left corner; grid points are whole numbers).</summary>
public readonly record struct CityPt(double X, double Y)
{
    public static CityPt operator +(CityPt a, CityPt b) => new(a.X + b.X, a.Y + b.Y);
    public static CityPt operator -(CityPt a, CityPt b) => new(a.X - b.X, a.Y - b.Y);
    public static CityPt operator *(CityPt a, double k) => new(a.X * k, a.Y * k);
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public CityPt Unit => Length < 1e-12 ? new(1, 0) : this * (1 / Length);
    public double Dot(CityPt o) => X * o.X + Y * o.Y;
    public double Cross(CityPt o) => X * o.Y - Y * o.X;
    public double DistanceTo(CityPt o) => (this - o).Length;
    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

/// <summary>
/// A fence, hedge or wall: a line from point to point (squares; grid points, or halfway between them), going
/// all the way round when <see cref="Closed"/>. Where a road crosses it, it opens for the road (a town wall's
/// gatehouse); where a river does, it stops either side; a guard tower stands astride it. Its open ends are
/// capped (a town wall's turret, a stone wall's pillar, a fence's post) unless it's <see cref="Ruined"/>.
/// Older maps kept fences as one-square <see cref="Edges"/> instead: <see cref="CityMap.Tidied"/> joins those
/// into lines.
/// </summary>
public sealed record Fence
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public FenceKind Kind { get; init; } = FenceKind.Fence;
    public List<CityPt> Points { get; init; } = [];
    public bool Closed { get; init; }
    /// <summary>Broken down: no caps on its ends, gaps left as they are.</summary>
    public bool Ruined { get; init; }
    /// <summary>The old way of keeping a fence: one-square edges of the grid.</summary>
    public List<FenceEdge> Edges { get; init; } = [];

    /// <summary>The straight stretches from point to point (the last back to the first when closed).</summary>
    public IEnumerable<(CityPt A, CityPt B)> Segments()
    {
        for (int i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1]);
        if (Closed && Points.Count > 2) yield return (Points[^1], Points[0]);
    }

    /// <summary>How long it is (squares).</summary>
    [JsonIgnore] public double Length => Segments().Sum(s => s.A.DistanceTo(s.B));

    /// <summary>
    /// The one-square grid edges it runs along, where it runs along the grid lines from grid point to grid point
    /// (a stretch at a slant or off the grid lines has none).
    /// </summary>
    public IEnumerable<FenceEdge> GridEdges()
    {
        foreach (var e in Edges) yield return e;
        foreach (var (a, b) in Segments())
        {
            if (!Whole(a.X) || !Whole(a.Y) || !Whole(b.X) || !Whole(b.Y)) continue;
            int ax = (int)Math.Round(a.X), ay = (int)Math.Round(a.Y), bx = (int)Math.Round(b.X), by = (int)Math.Round(b.Y);
            if (ay == by) for (int x = Math.Min(ax, bx); x < Math.Max(ax, bx); x++) yield return new FenceEdge(x, ay, true);
            else if (ax == bx) for (int y = Math.Min(ay, by); y < Math.Max(ay, by); y++) yield return new FenceEdge(ax, y, false);
        }
        static bool Whole(double v) => Math.Abs(v - Math.Round(v)) < 1e-6;
    }

    /// <summary>Whether a point (squares) is within <paramref name="reach"/> of the line.</summary>
    public bool Near(double x, double y, double reach) => Segments().Any(s => Geometry.DistanceToSegment(new CityPt(x, y), s.A, s.B) <= reach);
}

/// <summary>
/// A road: its middle line from point to point (squares), how many squares wide, what it's laid with (cobbles,
/// a dirt track, paving, sand) and its name, written along it. Roads lie on the ground over whatever's painted
/// there and may cross and overlap each other; where one crosses water it's a bridge, and where one crosses a
/// fence or wall, that opens for it. A road two squares wide has its middle on the grid lines, so it covers
/// whole squares; one an odd number wide has its middle halfway between them.
/// </summary>
public sealed record CityRoad
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public Ground Surface { get; init; } = Ground.Road;
    public double Width { get; init; } = 2;
    public List<CityPt> Points { get; init; } = [];

    /// <summary>The stretches from point to point.</summary>
    public IEnumerable<(CityPt A, CityPt B)> Segments()
    {
        for (int i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1]);
    }

    [JsonIgnore] public double Length => Segments().Sum(s => s.A.DistanceTo(s.B));

    /// <summary>
    /// Whether a point (squares) is on the road: inside one of its stretches, each as wide as the road and
    /// reaching half the road's width past its ends (so corners close and the ends are square).
    /// </summary>
    public bool Covers(double x, double y, double inset = 0)
    {
        var p = new CityPt(x, y);
        foreach (var (a, b) in Segments())
            if (Geometry.InStrip(p, a, b, Width / 2 - inset, Width / 2 - inset)) return true;
        if (Points.Count == 1) return Math.Abs(x - Points[0].X) <= Width / 2 - inset && Math.Abs(y - Points[0].Y) <= Width / 2 - inset;
        return false;
    }

    /// <summary>The squares it covers (their middles on it), on a map this big.</summary>
    public IEnumerable<(int X, int Y)> Cells(int across, int down)
    {
        if (Points.Count == 0) yield break;
        double r = Width / 2 + 0.01;
        int x0 = Math.Max(0, (int)Math.Floor(Points.Min(p => p.X) - r)), x1 = Math.Min(across - 1, (int)Math.Ceiling(Points.Max(p => p.X) + r));
        int y0 = Math.Max(0, (int)Math.Floor(Points.Min(p => p.Y) - r)), y1 = Math.Min(down - 1, (int)Math.Ceiling(Points.Max(p => p.Y) + r));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Covers(x + 0.5, y + 0.5)) yield return (x, y);
    }
}

/// <summary>Sums with points and lines on the map.</summary>
public static class Geometry
{
    /// <summary>How far a point is from a line between two points.</summary>
    public static double DistanceToSegment(CityPt p, CityPt a, CityPt b)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        double t = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Whether a point is in the strip <paramref name="half"/> either side of the line from a to b, reaching <paramref name="past"/> beyond each end.</summary>
    public static bool InStrip(CityPt p, CityPt a, CityPt b, double half, double past)
    {
        var ab = b - a;
        double len = ab.Length;
        if (len < 1e-9) return Math.Abs(p.X - a.X) <= half && Math.Abs(p.Y - a.Y) <= half;
        var u = ab * (1 / len);
        var d = p - a;
        double along = d.Dot(u), across = u.Cross(d);
        return along >= -past - 1e-9 && along <= len + past + 1e-9 && Math.Abs(across) <= half + 1e-9;
    }

    /// <summary>
    /// The part of the line from a to b inside a convex outline (its corners in order), as fractions of the way
    /// along (0 at a, 1 at b), or null if none of it is.
    /// </summary>
    public static (double T0, double T1)? Clip(CityPt a, CityPt b, IReadOnlyList<CityPt> outline)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        // Which way round the outline goes, so "inside" is the same side of every edge.
        double area = 0;
        for (int i = 0; i < outline.Count; i++) area += outline[i].Cross(outline[(i + 1) % outline.Count]);
        double sign = area >= 0 ? 1 : -1;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var e = outline[(i + 1) % outline.Count] - p;
            // Inside where sign × e × (x − p) ≥ 0.
            double num = sign * e.Cross(a - p), den = sign * e.Cross(d);
            if (Math.Abs(den) < 1e-12)
            {
                if (num < 0) return null;
                continue;
            }
            double t = -num / den;
            if (den > 0) t0 = Math.Max(t0, t);
            else t1 = Math.Min(t1, t);
            if (t0 > t1) return null;
        }
        return (t0, t1);
    }

    /// <summary>The four corners of a strip along a to b, <paramref name="half"/> either side and <paramref name="past"/> beyond each end.</summary>
    public static CityPt[] Strip(CityPt a, CityPt b, double half, double past)
    {
        var u = (b - a).Unit;
        var n = new CityPt(-u.Y, u.X);
        var s = a - u * past;
        var e = b + u * past;
        return [s + n * half, e + n * half, e - n * half, s - n * half];
    }

    /// <summary>The angle to write text along a line at, kept the right way up (it reads left to right, or upwards).</summary>
    public static double TextAngle(CityPt a, CityPt b)
    {
        double deg = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        if (deg > 90.001) deg -= 180;
        else if (deg <= -90.001) deg += 180;
        if (Math.Abs(deg - 90) < 0.001) deg = -90;
        return deg;
    }

    /// <summary>A point turned about another, clockwise seen from above (y down), by degrees.</summary>
    public static CityPt Turn(CityPt p, CityPt about, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var d = p - about;
        return new CityPt(about.X + d.X * c - d.Y * s, about.Y + d.X * s + d.Y * c);
    }
}

/// <summary>A tree, rock, well… at a point on the map (squares), its size (1 = about a square) and turn (degrees).</summary>
public sealed record Prop
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PropKind Kind { get; init; } = PropKind.Tree;
    public double X { get; init; }
    public double Y { get; init; }
    public double Size { get; init; } = 1;
    public double TurnDeg { get; init; }

    /// <summary>
    /// How big a rectangular thing is (squares, at size 1, before it's turned: across, then front to back),
    /// the furniture maker's town pieces' sizes; null for the round ones (a tree, a well).
    /// </summary>
    public static (double W, double D)? Footprint(PropKind k) => k switch
    {
        PropKind.Trough => (1.26, 0.43),
        PropKind.Stage => (3, 2),
        PropKind.Stall => (1.42, 0.79),
        PropKind.Bar => (1.57, 0.79),
        PropKind.NoticeBoard => (0.94, 0.39),
        PropKind.GuardTower => (1.81, 1.81),
        PropKind.Fountain => (2, 2),
        PropKind.Gallows => (1.81, 1.5),
        _ => null,
    };

    /// <summary>How far from its middle it reaches (squares), for picking it.</summary>
    [JsonIgnore]
    public double Reach => Footprint(Kind) is { } f ? Math.Max(0.3, Math.Max(f.W, f.D) / 2 * Size) : Math.Max(0.3, 0.4 * Size);

    /// <summary>Whether a point (squares) is on it.</summary>
    public bool Hit(double x, double y)
    {
        if (Footprint(Kind) is not { } f) return (X - x) * (X - x) + (Y - y) * (Y - y) <= Reach * Reach;
        double a = -TurnDeg * Math.PI / 180, dx = x - X, dy = y - Y;
        double u = dx * Math.Cos(a) - dy * Math.Sin(a), v = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(u) <= f.W * Size / 2 + 0.05 && Math.Abs(v) <= f.D * Size / 2 + 0.05;
    }
}

/// <summary>
/// A piece of the furniture maker's put down in the town, on a fine grid: a table and benches outside an inn, the
/// stocks on the green, a chest, a weapon rack by the guardhouse. Its middle (squares), how far it's turned
/// (degrees, clockwise seen from above; 0 with its front towards the bottom of the plan), the floor it takes (mm,
/// as made: across its front, then front to back) and the piece itself, sized for the map's squares; the catalog
/// entry it came from, if it did.
/// </summary>
public sealed record CityItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? CatalogId { get; init; }
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double TurnDeg { get; init; }
    public double WidthMm { get; init; } = 25;
    public double DepthMm { get; init; } = 25;
    public PhotonCanvas.Addon.Furniture.FurnitureItem Item { get; init; } = new();

    /// <summary>The floor it takes (squares, as made: across its front, then front to back).</summary>
    public (double W, double D) Size(double squareMm) => (WidthMm / Math.Max(1, squareMm), DepthMm / Math.Max(1, squareMm));

    /// <summary>A point of the piece (squares from its middle: u along its front to its right, v towards its front) on the map.</summary>
    public CityPt At(double u, double v)
    {
        double a = TurnDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new(X + u * c - v * s, Y + u * s + v * c);
    }

    /// <summary>Its four corners on the map (squares): front left, front right, back right, back left.</summary>
    public CityPt[] Corners(double squareMm)
    {
        var (w, d) = Size(squareMm);
        return [At(-w / 2, d / 2), At(w / 2, d / 2), At(w / 2, -d / 2), At(-w / 2, -d / 2)];
    }

    /// <summary>Whether a point (squares) is on it (or within <paramref name="reach"/> of it).</summary>
    public bool Hit(double x, double y, double squareMm, double reach = 0.02)
    {
        var (w, d) = Size(squareMm);
        double a = -TurnDeg * Math.PI / 180, dx = x - X, dy = y - Y;
        double u = dx * Math.Cos(a) - dy * Math.Sin(a), v = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(u) <= w / 2 + reach && Math.Abs(v) <= d / 2 + reach;
    }
}

/// <summary>
/// Words written on the map (a district's or a river's name, a number for the game master's key): the middle of
/// the text (squares), how tall it is (squares) and how far it's turned (degrees, clockwise seen from above).
/// </summary>
public sealed record CityLabel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Size { get; init; } = 0.8;
    public double TurnDeg { get; init; }

    /// <summary>Roughly how wide the text is (squares).</summary>
    public double Width => Math.Max(0.5, Text.Length * Size * 0.55);

    /// <summary>Whether a point (squares) is on the text, turned as it is.</summary>
    public bool Hit(double x, double y)
    {
        var p = Geometry.Turn(new CityPt(x, y), new CityPt(X, Y), -TurnDeg);
        return Math.Abs(p.X - X) <= Width / 2 + 0.1 && Math.Abs(p.Y - Y) <= Size / 2 + 0.1;
    }

    /// <summary>A turn kept the right way up: from a quarter turn back (reading upwards) to just short of a quarter turn on.</summary>
    public static double Upright(double deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg <= -180) deg += 360;
        if (deg >= 89.999) deg -= 180;
        else if (deg < -90.001) deg += 180;
        return Math.Round(deg, 6);
    }
}

/// <summary>The battle map picture a map was read from, shown under the plan: its file and its grid.</summary>
public sealed record CityPicture
{
    public string? Path { get; init; }
    public double SquarePx { get; init; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}

/// <summary>
/// A town, village or city on a big grid of squares: the ground of every square (grass, roads, water…),
/// the buildings standing on it (each a building editor design, turned and placed), fences and walls along
/// the grid lines, and trees and other small things. Saved as JSON: the ground as a row of letters for each
/// row of squares (<see cref="Code"/>).
/// </summary>
public sealed record CityMap
{
    public const int MaxSide = 300;

    public string Name { get; init; } = "New town";
    public string Theme { get; init; } = "Medieval";
    public double SquareMm { get; init; } = 25.4;
    public int Across { get; init; } = 40;
    public int Down { get; init; } = 30;
    public List<string> Rows { get; init; } = [];
    public List<CityBuilding> Buildings { get; init; } = [];
    public List<CityRoad> Roads { get; init; } = [];
    public List<Fence> Fences { get; init; } = [];
    public List<Prop> Props { get; init; } = [];
    public List<CityLabel> Labels { get; init; } = [];
    /// <summary>The furniture maker's pieces put down in the town (tables, benches, the stocks…).</summary>
    public List<CityItem> Items { get; init; } = [];
    public CityPicture? Picture { get; init; }

    /// <summary>The letter each kind of ground is saved as.</summary>
    public static char Code(Ground g) => g switch
    {
        Ground.Grass => 'g', Ground.Dirt => 'd', Ground.Road => 'r', Ground.Paving => 'p', Ground.Sand => 's',
        Ground.Water => 'w', Ground.Rock => 'k', Ground.Field => 'f', _ => 'o',
    };

    public static Ground FromCode(char c) => c switch
    {
        'd' => Ground.Dirt, 'r' => Ground.Road, 'p' => Ground.Paving, 's' => Ground.Sand, 'w' => Ground.Water,
        'k' => Ground.Rock, 'f' => Ground.Field, 'o' => Ground.Floor, _ => Ground.Grass,
    };

    public static string Label(Ground g) => g switch
    {
        Ground.Road => "Road (cobbles)", Ground.Paving => "Paving (flagstones)", Ground.Field => "Field (crops)", Ground.Floor => "Floor (indoors)",
        _ => g.ToString(),
    };

    public static string Label(PropKind k) => k switch
    {
        PropKind.Pine => "Pine tree", PropKind.Crates => "Crates and barrels", PropKind.Trough => "Horse trough", PropKind.Stage => "Players' stage",
        PropKind.Stall => "Market stall", PropKind.Bar => "Outdoor bar", PropKind.NoticeBoard => "Notice board", PropKind.GuardTower => "Guard tower",
        _ => k.ToString(),
    };

    public static string Label(FenceKind k) => k switch { FenceKind.StoneWall => "Stone wall", FenceKind.CityWall => "Town wall", _ => k.ToString() };

    /// <summary>An all-grass map of this many squares.</summary>
    public static CityMap Empty(int across, int down, string name = "New town")
    {
        across = Math.Clamp(across, 1, MaxSide);
        down = Math.Clamp(down, 1, MaxSide);
        return new CityMap { Name = name, Across = across, Down = down, Rows = [.. Enumerable.Repeat(new string('g', across), down)] };
    }

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < Across && y < Down;

    public Ground GroundAt(int x, int y) => !In(x, y) || y >= Rows.Count || x >= Rows[y].Length ? Ground.Grass : FromCode(Rows[y][x]);

    /// <summary>The ground as a grid to change (x across, y down).</summary>
    public Ground[,] GroundGrid()
    {
        var g = new Ground[Across, Down];
        for (int y = 0; y < Down; y++)
            for (int x = 0; x < Across; x++) g[x, y] = GroundAt(x, y);
        return g;
    }

    public CityMap WithGround(Ground[,] g) => this with
    {
        Rows = [.. Enumerable.Range(0, Down).Select(y => new string([.. Enumerable.Range(0, Across).Select(x => Code(x < g.GetLength(0) && y < g.GetLength(1) ? g[x, y] : Ground.Grass))]))],
    };

    /// <summary>The same map made a new size, kept at the top left (new squares are grass; buildings off the edge go).</summary>
    public CityMap Resized(int across, int down)
    {
        across = Math.Clamp(across, 1, MaxSide);
        down = Math.Clamp(down, 1, MaxSide);
        var old = GroundGrid();
        var g = new Ground[across, down];
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++) g[x, y] = x < Across && y < Down ? old[x, y] : Ground.Grass;
        var m = this with { Across = across, Down = down };
        return m.WithGround(g) with
        {
            Buildings = [.. Buildings.Where(b => { var (w, d) = CityFootprint.Size(b); return b.X + w <= across && b.Y + d <= down; })],
            Props = [.. Props.Where(p => p.X < across && p.Y < down)],
            Items = [.. Items.Where(i => i.X < across && i.Y < down)],
            Labels = [.. Labels.Where(l => l.X < across && l.Y < down)],
            Fences = [.. Tidied().Fences.Where(f => f.Points.Any(p => p.X < across && p.Y < down)).Select(f => f with { Points = [.. f.Points.Select(p => Clamp(p, across, down))] })],
            Roads = [.. Roads.Where(r => r.Points.Any(p => p.X < across && p.Y < down)).Select(r => r with { Points = [.. r.Points.Select(p => Clamp(p, across, down))] })],
        };
        static CityPt Clamp(CityPt p, int across, int down) => new(Math.Clamp(p.X, 0, across), Math.Clamp(p.Y, 0, down));
    }

    /// <summary>
    /// The map with its older fences (kept as one-square edges of the grid) joined into lines from point to point,
    /// one fence for each line (all the way round where they close), and ids where any are missing. The same map
    /// when there's nothing to change.
    /// </summary>
    public CityMap Tidied()
    {
        if (Fences.All(f => f.Edges.Count == 0 && f.Id != Guid.Empty) && Roads.All(r => r.Id != Guid.Empty)) return this;
        var fences = new List<Fence>();
        foreach (var f in Fences)
        {
            var id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id;
            if (f.Edges.Count == 0) { fences.Add(f with { Id = id }); continue; }
            bool first = true;
            if (f.Points.Count > 1) { fences.Add(f with { Id = id, Edges = [] }); first = false; }
            foreach (var (pts, closed) in FenceLines.Join(f.Edges))
            {
                fences.Add(new Fence { Id = first ? id : Guid.NewGuid(), Kind = f.Kind, Ruined = f.Ruined, Points = pts, Closed = closed });
                first = false;
            }
        }
        return this with { Fences = fences, Roads = [.. Roads.Select(r => r.Id == Guid.Empty ? r with { Id = Guid.NewGuid() } : r)] };
    }

    /// <summary>
    /// The ground with the roads laid on it: every square a road covers as the road's surface, over water too
    /// (a bridge's deck) or, with <paramref name="overWater"/> false, leaving the water under its bridges.
    /// The roads are still on it. The same map when there are no roads.
    /// </summary>
    public CityMap Paved(bool overWater = true)
    {
        if (Roads.Count == 0) return this;
        var g = GroundGrid();
        var raw = overWater ? null : (Ground[,])g.Clone();
        foreach (var r in Roads)
            foreach (var (x, y) in r.Cells(Across, Down))
                if (overWater || raw![x, y] != Ground.Water) g[x, y] = r.Surface;
        return WithGround(g);
    }

    /// <summary>Whether a road crosses water on this square (a bridge stands there).</summary>
    public bool Bridged(int x, int y) => GroundAt(x, y) == Ground.Water && Roads.Any(r => r.Covers(x + 0.5, y + 0.5));

    public CityRoad? Road(Guid id) => Roads.FirstOrDefault(r => r.Id == id);
    public Fence? FenceById(Guid id) => Fences.FirstOrDefault(f => f.Id == id);
    public CityItem? ItemById(Guid id) => Items.FirstOrDefault(i => i.Id == id);

    public CityBuilding? Building(Guid id) => Buildings.FirstOrDefault(b => b.Id == id);

    public CityMap WithBuilding(CityBuilding b) => this with
    {
        Buildings = Buildings.Any(x => x.Id == b.Id) ? [.. Buildings.Select(x => x.Id == b.Id ? b : x)] : [.. Buildings, b],
    };

    public CityMap Without(Guid id) => this with { Buildings = [.. Buildings.Where(b => b.Id != id)] };

    public string ToJson() => JsonSerializer.Serialize(this, CityJson.Options);

    public static CityMap? FromJson(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<CityMap>(json, CityJson.Options);
            if (m is null) return null;
            // Older maps had no labels, roads or furniture; their trees had no ids, their fences were one-square edges.
            return (m with
            {
                Across = Math.Clamp(m.Across, 1, MaxSide), Down = Math.Clamp(m.Down, 1, MaxSide), Labels = m.Labels ?? [], Roads = m.Roads ?? [], Items = m.Items ?? [],
                Fences = [.. (m.Fences ?? []).Select(f => f with { Points = f.Points ?? [], Edges = f.Edges ?? [] })],
                Props = [.. (m.Props ?? []).Select(p => p.Id == Guid.Empty ? p with { Id = Guid.NewGuid() } : p)],
            }).Tidied();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>One-square fence edges joined into lines from point to point.</summary>
public static class FenceLines
{
    /// <summary>
    /// The edges joined end to end into lines (a new line wherever three or more meet), each line's points only
    /// where it turns, and whether it goes all the way round.
    /// </summary>
    public static List<(List<CityPt> Points, bool Closed)> Join(IEnumerable<FenceEdge> edges)
    {
        static (int X, int Y) End(FenceEdge e) => e.Across ? (e.X + 1, e.Y) : (e.X, e.Y + 1);
        var set = edges.Distinct().ToList();
        var at = new Dictionary<(int X, int Y), List<FenceEdge>>();
        foreach (var e in set)
            foreach (var p in new[] { (e.X, e.Y), End(e) })
            {
                if (!at.TryGetValue(p, out var l)) at[p] = l = [];
                l.Add(e);
            }
        var used = new HashSet<FenceEdge>();
        var res = new List<(List<CityPt>, bool)>();
        (int X, int Y) Other(FenceEdge e, (int X, int Y) p) => (e.X, e.Y) == p ? End(e) : (e.X, e.Y);
        List<(int X, int Y)> Walk((int X, int Y) start, FenceEdge first)
        {
            var path = new List<(int X, int Y)> { start };
            var cur = start;
            var e = first;
            while (true)
            {
                used.Add(e);
                cur = Other(e, cur);
                path.Add(cur);
                if (at[cur].Count != 2 || cur == start) break;
                var next = at[cur].Where(x => !used.Contains(x)).ToList();
                if (next.Count == 0) break;
                e = next[0];
            }
            return path;
        }
        foreach (var p in at.Keys.Where(p => at[p].Count != 2).OrderBy(p => p.Y).ThenBy(p => p.X))
            foreach (var e in at[p].OrderBy(e => e.Across).ThenBy(e => e.Y).ThenBy(e => e.X))
                if (!used.Contains(e)) res.Add((Simplify(Walk(p, e), false), false));
        // What's left goes round in rings.
        foreach (var e in set.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Across))
        {
            if (used.Contains(e)) continue;
            var ring = Walk((e.X, e.Y), e);
            if (ring.Count > 1 && ring[^1] == ring[0]) ring.RemoveAt(ring.Count - 1);
            res.Add((Simplify(ring, true), true));
        }
        return res;
    }

    /// <summary>Only the points where the line turns (and its ends, unless it's a ring).</summary>
    static List<CityPt> Simplify(List<(int X, int Y)> path, bool ring)
    {
        var keep = new List<(int X, int Y)>();
        int n = path.Count;
        for (int i = 0; i < n; i++)
        {
            bool end = !ring && (i == 0 || i == n - 1);
            if (!end)
            {
                var a = path[(i - 1 + n) % n];
                var b = path[(i + 1) % n];
                var p = path[i];
                if ((p.X - a.X) * (b.Y - p.Y) - (p.Y - a.Y) * (b.X - p.X) == 0) continue;
            }
            keep.Add(path[i]);
        }
        if (keep.Count == 0) keep.Add(path[0]);
        return [.. keep.Select(p => new CityPt(p.X, p.Y))];
    }
}

static class CityJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };
}
