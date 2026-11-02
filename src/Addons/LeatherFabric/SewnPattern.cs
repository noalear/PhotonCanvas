using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.LeatherFabric;

/// <summary>Which line of the piece the person drew.</summary>
public enum DrawnLine
{
    /// <summary>The outline is where the material is cut; the stitching goes inside it.</summary>
    CutEdge,
    /// <summary>The outline is where it is sewn; the cut edge goes outside it by the seam allowance.</summary>
    SewingLine,
}

/// <summary>The holes punched along the stitch line for sewing by hand.</summary>
public enum HoleStyle
{
    None,
    /// <summary>Round holes, like a rotary punch or an awl.</summary>
    Round,
    /// <summary>Short slots slanted 45° to the stitch line, like a pricking iron (French or Japanese style).</summary>
    Slots,
    /// <summary>Diamonds slanted 45° to the stitch line, like a diamond stitching chisel.</summary>
    Diamonds,
}

/// <summary>How the stitch line itself is shown.</summary>
public enum StitchLineShow
{
    /// <summary>On the pattern layer: seen on the screen, never burned.</summary>
    Guide,
    /// <summary>Burned lightly on a Score layer: the groove a stitching groover makes in leather, so the thread sits below the surface.</summary>
    Groove,
}

/// <summary>Everything that decides how the pieces are made. Saved on each piece so it can be changed later.</summary>
public sealed record SewnOptions
{
    public SewnMaterial Material { get; init; } = SewnMaterial.VegTan;
    public DrawnLine Drawn { get; init; } = DrawnLine.CutEdge;
    /// <summary>From the cut edge to the stitch line (mm).</summary>
    public double AllowanceMm { get; init; } = 3.5;
    public HoleStyle Holes { get; init; } = HoleStyle.Slots;
    /// <summary>Stitch length: from one hole to the next along the stitch line (mm). Each straight run is spaced evenly, as close to this as fits.</summary>
    public double PitchMm { get; init; } = 4;
    /// <summary>Round: diameter. Slots and diamonds: their length (mm).</summary>
    public double HoleSizeMm { get; init; } = 1.6;
    /// <summary>Also stitch round cut-outs inside a piece (a window, a slot); off = outer edges only.</summary>
    public bool StitchInside { get; init; }
    /// <summary>Small cuts from the edge into the seam allowance, for lining pieces up before sewing.</summary>
    public bool Notches { get; init; }
    public StitchLineShow StitchLine { get; init; } = StitchLineShow.Guide;
    /// <summary>Also make each piece's mirror image (a left and a right).</summary>
    public bool Mirror { get; init; }
    /// <summary>The library recipe to cut with (null = leave the layers' settings).</summary>
    public Guid? RecipeId { get; init; }

    /// <summary>The usual settings for a material (picked when the material is changed).</summary>
    public static SewnOptions For(SewnMaterial m) => SewnMaterials.Info(m).Fabric
        ? new() { Material = m, Drawn = DrawnLine.SewingLine, AllowanceMm = 10, Holes = HoleStyle.None, PitchMm = 4, HoleSizeMm = 1.6, Notches = true }
        : new() { Material = m, Drawn = DrawnLine.CutEdge, AllowanceMm = 3.5, Holes = HoleStyle.Slots, PitchMm = 4, HoleSizeMm = 1.6, Notches = false };

    /// <summary>The same options brought into sensible ranges (they come from saved files too).</summary>
    public SewnOptions Clamped() => this with
    {
        AllowanceMm = double.IsFinite(AllowanceMm) ? Math.Clamp(AllowanceMm, 0.5, 50) : 3.5,
        PitchMm = double.IsFinite(PitchMm) ? Math.Clamp(PitchMm, 1, 30) : 4,
        HoleSizeMm = double.IsFinite(HoleSizeMm) ? Math.Clamp(HoleSizeMm, 0.3, 10) : 1.6,
    };
}

/// <summary>What one drawn piece becomes, all in machine millimetres.</summary>
public sealed record SewnPiece(PathGeometry Cut, PathGeometry Stitch, PathGeometry Holes, PathGeometry Notches, int HoleCount, string? Problem);

/// <summary>
/// The geometry of leather and fabric pieces: the cut edge and the stitch line a seam allowance apart,
/// stitch holes spaced evenly along the stitch line with one in every corner, and notches.
/// </summary>
public static class SewnPattern
{
    /// <summary>Curves are followed this closely when placing holes (mm).</summary>
    const double FlatTol = 0.02;

    /// <summary>A bend sharper than this (degrees) is a corner: it gets a hole of its own and the spacing starts again from it.</summary>
    public const double CornerDeg = 35;

    /// <summary>
    /// Make one piece from the outline it was drawn as. Closed outlines become a cut edge and a stitch
    /// line; open lines are stitch lines on their own (holes along them, nothing cut).
    /// </summary>
    public static SewnPiece Make(PathGeometry world, SewnOptions o)
    {
        o = o.Clamped();
        var closed = new PathGeometry(world.Subpaths.Where(s => s.Closed && !s.Segs.IsEmpty));
        var open = world.Subpaths.Where(s => !s.Closed && !s.Segs.IsEmpty).ToList();
        PathGeometry cut = PathGeometry.Empty, stitch = PathGeometry.Empty;
        string? problem = null;
        if (!closed.IsEmpty)
        {
            if (o.Drawn == DrawnLine.CutEdge)
            {
                cut = closed;
                stitch = ShapeGeometry.Offset(closed, -o.AllowanceMm, JoinStyle.Sharp, EndStyle.Flat);
                if (stitch.IsEmpty) problem = $"too narrow for a {o.AllowanceMm:0.#} mm seam allowance on both sides";
            }
            else
            {
                stitch = closed;
                cut = ShapeGeometry.Offset(closed, o.AllowanceMm, JoinStyle.Sharp, EndStyle.Flat);
            }
        }

        // Stitch holes: outer edges (and cut-outs, if asked), and open lines.
        var holes = new List<Subpath>();
        int count = 0;
        if (o.Holes != HoleStyle.None)
        {
            var loops = stitch.Flatten(FlatTol).Where(f => f.Closed && f.Points.Count > 2).ToList();
            foreach (var (loop, outer) in Classify(loops))
                if (outer || o.StitchInside)
                    foreach (var (p, dir) in HolePlaces(loop.Points, true, o.PitchMm)) { holes.AddRange(Hole(p, dir, o.Holes, o.HoleSizeMm).Subpaths); count++; }
            foreach (var line in new PathGeometry(open).Flatten(FlatTol))
                foreach (var (p, dir) in HolePlaces(line.Points, false, o.PitchMm)) { holes.AddRange(Hole(p, dir, o.Holes, o.HoleSizeMm).Subpaths); count++; }
            if (problem is null && !cut.IsEmpty && o.HoleSizeMm / 2 >= o.AllowanceMm)
                problem = "the holes reach the cut edge: make them smaller or the seam allowance bigger";
        }
        if (open.Count > 0) stitch = new PathGeometry([.. stitch.Subpaths, .. open]);

        // Notches: from the cut edge into the seam allowance, at the middle of each side.
        var notches = new List<Subpath>();
        if (o.Notches && !cut.IsEmpty)
        {
            double len = Math.Min(3, o.AllowanceMm * 0.5);
            var loops = cut.Flatten(FlatTol).Where(f => f.Closed && f.Points.Count > 2).ToList();
            if (len >= 0.5)
                foreach (var (loop, outer) in Classify(loops))
                    if (outer)
                        foreach (var (p, inward) in NotchPlaces(loop.Points, len))
                            notches.Add(new Subpath(p, [Seg.Line(p + inward * len)], false));
        }
        return new SewnPiece(cut, stitch, new PathGeometry(holes), new PathGeometry(notches), count, problem);
    }

    /// <summary>Each loop with whether it is an outer edge (inside an even number of the others) or a cut-out.</summary>
    static IEnumerable<(FlatPath Loop, bool Outer)> Classify(List<FlatPath> loops)
    {
        foreach (var l in loops)
        {
            int inside = loops.Count(o => !ReferenceEquals(o, l) && Geom.Inside(o.Points, l.Points[0]));
            yield return (l, inside % 2 == 0);
        }
    }

    /// <summary>The points without repeats (and without the closing repeat of the first point on a loop).</summary>
    static List<Vec2> Clean(IReadOnlyList<Vec2> raw, bool closed)
    {
        var pts = new List<Vec2>(raw.Count);
        foreach (var p in raw)
            if (pts.Count == 0 || pts[^1].Dist(p) > 1e-6) pts.Add(p);
        if (closed && pts.Count > 1 && pts[0].Dist(pts[^1]) <= 1e-6) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>
    /// The corners of a line: points where it bends by more than <see cref="CornerDeg"/>, at least
    /// <paramref name="minGap"/> apart along it (of two closer ones, the sharper stays). Indexes into the cleaned points.
    /// </summary>
    internal static List<int> Corners(List<Vec2> pts, bool closed, double minGap)
    {
        int n = pts.Count;
        var cand = new List<(int I, double Turn)>();
        for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
        {
            var a = pts[(i - 1 + n) % n];
            var b = pts[i];
            var c = pts[(i + 1) % n];
            var u = b - a;
            var v = c - b;
            if (u.Length < 1e-9 || v.Length < 1e-9) continue;
            double turn = Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v))) * 180 / Math.PI;
            if (turn > CornerDeg) cand.Add((i, turn));
        }
        if (cand.Count == 0) return [];
        var s = Lengths(pts, closed, out double total);
        var kept = new List<(int I, double Turn)>();
        foreach (var c in cand)
        {
            if (kept.Count > 0 && s[c.I] - s[kept[^1].I] < minGap)
            {
                if (c.Turn > kept[^1].Turn) kept[^1] = c;
                continue;
            }
            kept.Add(c);
        }
        // On a loop the last and the first are neighbours too.
        if (closed && kept.Count > 1 && s[kept[0].I] + total - s[kept[^1].I] < minGap)
        {
            if (kept[^1].Turn > kept[0].Turn) kept[0] = kept[^1];
            kept.RemoveAt(kept.Count - 1);
        }
        return [.. kept.Select(k => k.I).Order()];
    }

    /// <summary>The distance along the line to each point, and the whole length (a loop's includes its closing side).</summary>
    static double[] Lengths(List<Vec2> pts, bool closed, out double total)
    {
        var s = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) s[i] = s[i - 1] + pts[i - 1].Dist(pts[i]);
        total = s[^1] + (closed && pts.Count > 1 ? pts[^1].Dist(pts[0]) : 0);
        return s;
    }

    /// <summary>The point and direction of travel at a distance along the line (wrapping round a loop).</summary>
    static (Vec2 P, Vec2 Dir) At(List<Vec2> pts, double[] s, double total, bool closed, double d)
    {
        int n = pts.Count;
        if (closed) { d %= total; if (d < 0) d += total; }
        else d = Math.Clamp(d, 0, total);
        int segs = closed ? n : n - 1;
        int lo = 0, hi = segs - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (s[mid] <= d) lo = mid; else hi = mid - 1;
        }
        var a = pts[lo];
        var b = pts[(lo + 1) % n];
        double len = a.Dist(b);
        double t = len > 0 ? Math.Clamp((d - s[lo]) / len, 0, 1) : 0;
        var dir = len > 0 ? (b - a) / len : new Vec2(1, 0);
        return (Vec2.Lerp(a, b, t), dir);
    }

    /// <summary>The direction halfway between coming in to and going out of point i (for a hole in a corner).</summary>
    static Vec2 Bisector(List<Vec2> pts, int i, bool closed)
    {
        int n = pts.Count;
        Vec2 Unit(Vec2 v) => v.Length > 0 ? v / v.Length : new Vec2(1, 0);
        var into = i > 0 || closed ? Unit(pts[i] - pts[(i - 1 + n) % n]) : Unit(pts[1] - pts[0]);
        var outOf = i < n - 1 || closed ? Unit(pts[(i + 1) % n] - pts[i]) : into;
        var sum = into + outOf;
        return sum.Length > 1e-6 ? sum / sum.Length : outOf;
    }

    /// <summary>
    /// Where the holes go on a stitch line: one in every corner (and at both ends of an open line), the
    /// rest spaced evenly along each run between them, as close to <paramref name="pitch"/> as a whole
    /// number of stitches allows. A smooth loop with no corners gets at least three.
    /// </summary>
    internal static List<(Vec2 P, Vec2 Dir)> HolePlaces(IReadOnlyList<Vec2> raw, bool closed, double pitch)
    {
        var pts = Clean(raw, closed);
        var o = new List<(Vec2, Vec2)>();
        if (pts.Count < 2 || !(pitch > 0)) return o;
        var s = Lengths(pts, closed, out double total);
        if (total < 1e-6) return o;
        var corners = Corners(pts, closed, pitch * 0.6);
        var anchors = new List<int>(corners);
        if (!closed)
        {
            if (anchors.Count == 0 || anchors[0] != 0) anchors.Insert(0, 0);
            if (anchors[^1] != pts.Count - 1) anchors.Add(pts.Count - 1);
        }
        if (anchors.Count == 0)
        {
            int m = Math.Max(3, (int)Math.Round(total / pitch));
            for (int j = 0; j < m; j++) o.Add(At(pts, s, total, true, j * total / m));
            return o;
        }
        int runs = closed ? anchors.Count : anchors.Count - 1;
        for (int k = 0; k < runs; k++)
        {
            double from = s[anchors[k]];
            double to = k + 1 < anchors.Count ? s[anchors[k + 1]] : s[anchors[0]] + total;
            double len = to - from;
            int m = Math.Max(1, (int)Math.Round(len / pitch));
            o.Add((pts[anchors[k]], Bisector(pts, anchors[k], closed)));
            for (int j = 1; j < m; j++) o.Add(At(pts, s, total, closed, from + j * len / m));
        }
        if (!closed) o.Add((pts[anchors[^1]], Bisector(pts, anchors[^1], false)));
        return o;
    }

    /// <summary>One hole centred on <paramref name="p"/>; slots and diamonds lean 45° from the direction of the stitch line.</summary>
    internal static PathGeometry Hole(Vec2 p, Vec2 dir, HoleStyle style, double size)
    {
        if (style == HoleStyle.Round) return PathGeometry.Ellipse(p.X, p.Y, size / 2, size / 2);
        const double c = 0.70710678118654752;
        var along = new Vec2(dir.X * c - dir.Y * c, dir.X * c + dir.Y * c);
        var across = new Vec2(-along.Y, along.X);
        double half = size / 2;
        if (style == HoleStyle.Slots)
        {
            double w = Math.Max(0.3, size * 0.38) / 2;
            return PathGeometry.Polyline([p - along * half - across * w, p + along * half - across * w, p + along * half + across * w, p - along * half + across * w], true);
        }
        double h = size * 0.45 / 2;
        return PathGeometry.Polyline([p - along * half, p - across * h, p + along * half, p + across * h], true);
    }

    /// <summary>A notch at the middle of each side of an outer edge (four, evenly round, on a smooth one), pointing into the piece.</summary>
    internal static List<(Vec2 P, Vec2 Inward)> NotchPlaces(IReadOnlyList<Vec2> raw, double len)
    {
        var pts = Clean(raw, true);
        var o = new List<(Vec2, Vec2)>();
        if (pts.Count < 3) return o;
        var s = Lengths(pts, true, out double total);
        // Positive signed area is clockwise on screen (+Y down): the inside is then on the right of the direction of travel.
        bool clockwise = Geom.SignedArea(pts) > 0;
        Vec2 In(Vec2 d) => clockwise ? new Vec2(-d.Y, d.X) : new Vec2(d.Y, -d.X);
        var corners = Corners(pts, true, len * 2);
        var at = new List<double>();
        if (corners.Count == 0) for (int j = 0; j < 4; j++) at.Add(j * total / 4);
        else
            for (int k = 0; k < corners.Count; k++)
            {
                double from = s[corners[k]];
                double to = k + 1 < corners.Count ? s[corners[k + 1]] : s[corners[0]] + total;
                if (to - from >= len * 4) at.Add((from + to) / 2);
            }
        foreach (var d in at)
        {
            var (p, dir) = At(pts, s, total, true, d);
            o.Add((p, In(dir)));
        }
        return o;
    }
}
