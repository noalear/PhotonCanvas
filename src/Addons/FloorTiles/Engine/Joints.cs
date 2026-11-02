using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>The sides of a tile (bottom, right, top, left: anticlockwise, y up).</summary>
[Flags]
public enum Sides { None = 0, Bottom = 1, Right = 2, Top = 4, Left = 8, All = 15 }

/// <summary>
/// What goes on a tile's edges so tiles lock together on the grid. Every whole grid square of every
/// edge gets the same: going round the tile anticlockwise, a tab a quarter of the way along the square
/// and a socket three quarters of the way (for clips, one socket in the middle; for magnets, a hole each
/// side of the middle). The tile beside it runs along that square the other way, so its tab meets
/// this socket and its socket this tab: any tile fits any other on the grid, turned any way round.
/// Part squares (at the far edge of a board that isn't a whole number of squares) are left plain, on
/// both tiles that meet there.
/// </summary>
public static class Joints
{
    /// <summary>An edge: where it starts, the way it runs (anticlockwise round the tile), its outward normal and its length.</summary>
    public readonly record struct Edge(Sides Side, PointD Start, PointD Along, PointD Out, double Length)
    {
        public PointD At(double t, double outward) => new(Start.x + Along.x * t + Out.x * outward, Start.y + Along.y * t + Out.y * outward);
    }

    public static IEnumerable<Edge> Edges(double w, double h)
    {
        yield return new(Sides.Bottom, new(0, 0), new(1, 0), new(0, -1), w);
        yield return new(Sides.Right, new(w, 0), new(0, 1), new(1, 0), h);
        yield return new(Sides.Top, new(w, h), new(-1, 0), new(0, 1), w);
        yield return new(Sides.Left, new(0, h), new(0, -1), new(-1, 0), h);
    }

    /// <summary>
    /// The places along an edge (distance from its start) of the whole grid squares' features at
    /// <paramref name="share"/> of the way along each square, counted the way the edge runs.
    /// </summary>
    public static IEnumerable<double> Along(Edge e, double square, double share)
    {
        int whole = (int)Math.Floor(e.Length / square + 1e-6);
        // Squares are counted from the tile's corner at 0, 0: from the edge's start on the bottom and
        // right edges, from its end on the top and left ones (so a part square is always at the far end).
        bool fromStart = e.Side is Sides.Bottom or Sides.Right;
        for (int j = 0; j < whole; j++)
        {
            double u0 = j * square;   // the square's low end, from the corner at 0, 0
            double u = fromStart ? u0 + share * square : u0 + (1 - share) * square;
            yield return fromStart ? u : e.Length - u;
        }
    }

    // ---- jigsaw ----

    /// <summary>How far a jigsaw tab sticks out past the edge, for a head <paramref name="d"/> across.</summary>
    public static double TabReach(double d) => 1.25 * d;

    /// <summary>A jigsaw tab at <paramref name="t"/> along the edge, at its full size (a neck and a round head), reaching 1 mm back over the edge.</summary>
    static PathsD TabShape(Edge e, double t, double d, int dir)
    {
        double r = d / 2, neck = 0.55 * d, c = 0.25 * d + r;
        var pts = new PathsD
        {
            new PathD { e.At(t - neck / 2, -dir), e.At(t + neck / 2, -dir), e.At(t + neck / 2, dir * (c)), e.At(t - neck / 2, dir * (c)) },
        };
        var head = new PathD();
        var centre = e.At(t, dir * c);
        int n = Geo.Segments(r, 0.01);
        for (int k = 0; k < n; k++)
        {
            double a = 2 * Math.PI * k / n;
            head.Add(new PointD(centre.x + r * Math.Cos(a), centre.y + r * Math.Sin(a)));
        }
        pts.Add(head);
        // Rounded where the neck meets the head.
        double fillet = Math.Min(0.6, d * 0.1);
        // Each outline tidied first: turned inwards, the neck runs the other way round from the head.
        return Geo.Offset(Geo.Offset(Geo.Union(new PathsD(pts.SelectMany(p => Geo.Tidy([p])))), fillet, round: true), -fillet, round: true);
    }

    // The fit is the play across a tab or clip: half of it each side, a quarter off the tab and a quarter on the socket.

    /// <summary>A jigsaw tab (shrunk by a quarter of the fit all round) sticking out at <paramref name="t"/>.</summary>
    public static PathsD Tab(Edge e, double t, double d, double fit) => Geo.Offset(TabShape(e, t, d, 1), -fit / 4, round: true);

    /// <summary>A jigsaw socket (grown by a quarter of the fit all round) cut in at <paramref name="t"/>: the tab's shape turned inwards.</summary>
    public static PathsD Socket(Edge e, double t, double d, double fit) => Geo.Offset(TabShape(e, t, d, -1), fit / 4, round: true);

    // ---- clips ----

    /// <summary>A clip's sizes for a width of <paramref name="tab"/>: the neck where two tiles meet, the wide end, and how far each half reaches.</summary>
    public static (double Neck, double Wide, double Reach) ClipSize(double tab) => (0.7 * tab, 1.3 * tab, tab);

    /// <summary>Half a bowtie, cut in at <paramref name="t"/> (grown by a quarter of the fit), reaching 1 mm out past the edge.</summary>
    public static PathD ClipSocket(Edge e, double t, double tab, double fit)
    {
        var (neck, wide, reach) = ClipSize(tab);
        // The sides slope out from the neck at the edge to the wide end inside; the bit past the edge carries the slope on.
        double slope = (wide - neck) / 2 / reach, outBy = 1;
        double f = fit / 4, n0 = neck / 2 + f - slope * outBy, n1 = wide / 2 + f + slope * f;
        return [e.At(t - n0, outBy), e.At(t + n0, outBy), e.At(t + n1, -(reach + f)), e.At(t - n1, -(reach + f))];
    }

    /// <summary>A bowtie clip (shrunk by a quarter of the fit), its middle at 0, 0, lying along x.</summary>
    public static PathD Clip(double tab, double fit)
    {
        var (neck, wide, reach) = ClipSize(tab);
        double f = fit / 4, l = reach - f, n = neck / 2 - f, wd = wide / 2 - f;
        return [new(-l, -wd), new(0, -n), new(l, -wd), new(l, wd), new(0, n), new(-l, wd)];
    }

    // ---- magnets ----

    /// <summary>
    /// Where the two magnets of a grid square sit: their share of the way along the square, and how far in
    /// from the edge their middles are. Far enough from the corner that the two edges' magnets there don't
    /// meet, and from each other. Null if they can't fit.
    /// </summary>
    public static (double Share, double Inset)? MagnetPlaces(double square, double magnet, double fit)
    {
        double r = magnet / 2 + fit / 4, inset = r + 0.8, wall = 1.2;
        // Magnets at p on two edges meeting at a corner are (p, inset) and (inset, p): √2 (p − inset) apart.
        double low = Math.Max(square / 4, inset + (2 * r + wall) / Math.Sqrt(2));
        double high = (square - 2 * r - wall) / 2;
        return low > high + 1e-9 ? null : (low / square, inset);
    }
}
