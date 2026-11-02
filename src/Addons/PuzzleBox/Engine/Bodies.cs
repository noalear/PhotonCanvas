using Clipper2Lib;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>
/// A rectangle on the plan of the box (mm): x left to right, y from the front face (0) to the back.
/// Everything that moves or stops something moving is made of these, so the box can be played exactly.
/// </summary>
public readonly record struct Box2(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public bool IsEmpty => X1 - X0 <= 1e-9 || Y1 - Y0 <= 1e-9;
    public Box2 Move(double dx, double dy) => new(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
    public PathD Path => Geo.Rect(X0, Y0, X1, Y1);

    /// <summary>How far the two overlap along x and along y (negative: apart).</summary>
    public (double X, double Y) Overlap(Box2 o) => (Math.Min(X1, o.X1) - Math.Max(X0, o.X0), Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0));

    /// <summary>The parts of this rectangle outside <paramref name="hole"/> (up to four).</summary>
    public IEnumerable<Box2> Minus(Box2 hole)
    {
        var (ox, oy) = Overlap(hole);
        if (ox <= 1e-9 || oy <= 1e-9) { yield return this; yield break; }
        double hx0 = Math.Max(X0, hole.X0), hx1 = Math.Min(X1, hole.X1), hy0 = Math.Max(Y0, hole.Y0), hy1 = Math.Min(Y1, hole.Y1);
        if (hy0 > Y0) yield return new Box2(X0, Y0, X1, hy0);
        if (hy1 < Y1) yield return new Box2(X0, hy1, X1, Y1);
        if (hx0 > X0) yield return new Box2(X0, hy0, hx0, hy1);
        if (hx1 < X1) yield return new Box2(hx1, hy0, X1, hy1);
    }

    public static List<Box2> Minus(IEnumerable<Box2> solid, IEnumerable<Box2> holes)
    {
        var list = solid.ToList();
        foreach (var h in holes) list = [.. list.SelectMany(r => r.Minus(h)).Where(r => !r.IsEmpty)];
        return list;
    }
}

/// <summary>What a body is, for its name and what the person does with it.</summary>
public enum BodyRole { Frame, Lid, Panel, Drawer, Button }

/// <summary>
/// Pieces glued together that move as one: the box itself (every layer that doesn't move), the lid, a
/// sliding panel, the drawer, the button and its bolt. Each layer it has a piece in holds the
/// rectangles that make it up (those are what bump into things), and cut-outs and etching that don't
/// (a finger notch, the letters).
/// </summary>
public sealed class Body
{
    public required string Name { get; init; }
    public required BodyRole Role { get; init; }
    /// <summary>Panels count from the front, from 1.</summary>
    public int Number { get; init; }
    public SortedDictionary<int, List<Box2>> Solid { get; } = [];
    /// <summary>Cut out of a layer's piece without changing how it moves (a finger notch).</summary>
    public Dictionary<int, PathsD> Cutouts { get; } = [];
    /// <summary>Etched on a layer's piece (on its top face).</summary>
    public Dictionary<int, PathsD> Etched { get; } = [];
    /// <summary>What each layer's piece is called (default: the body's name).</summary>
    public Dictionary<int, string> PieceNames { get; } = [];
    /// <summary>Bodies that have to be out before this one can be got at (the button sits inside the box).</summary>
    public List<int> ReachAfter { get; } = [];

    public void Add(int layer, params Box2[] r)
    {
        if (!Solid.TryGetValue(layer, out var list)) Solid[layer] = list = [];
        list.AddRange(r.Where(b => !b.IsEmpty));
    }

    public void Set(int layer, List<Box2> r) => Solid[layer] = [.. r.Where(b => !b.IsEmpty)];

    public void Cut(int layer, PathD p)
    {
        if (!Cutouts.TryGetValue(layer, out var list)) Cutouts[layer] = list = [];
        list.Add(p);
    }

    public void Etch(int layer, PathsD p)
    {
        if (p.Count == 0) return;
        if (!Etched.TryGetValue(layer, out var list)) Etched[layer] = list = [];
        list.AddRange(p);
    }

    /// <summary>The piece's outline in one layer (holes included), as it's cut.</summary>
    public PathsD Outline(int layer)
    {
        if (!Solid.TryGetValue(layer, out var rects) || rects.Count == 0) return [];
        var area = Geo.Union(new PathsD(rects.Select(r => r.Path)));
        if (Cutouts.TryGetValue(layer, out var cuts) && cuts.Count > 0) area = Geo.Subtract(area, Geo.Union(cuts));
        return Geo.Tidy(Clipper.SimplifyPaths(area, 0.001));
    }

    public string PieceName(int layer) => PieceNames.TryGetValue(layer, out var n) ? n : Name;
}
