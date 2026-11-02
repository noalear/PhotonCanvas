using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The floor plate: a sheet over the room that rests on the walls' tops, with a hole for every
/// column (the columns poke up through them and keep the plate from sliding about), the opening for
/// the way up and the floor's texture etched on top. A plate bigger than the bed is cut in pieces
/// joined with dovetails.
/// </summary>
public static class Plate
{
    /// <summary>A dovetail's width where it leaves its piece, at its end, and how far it reaches (mm).</summary>
    public const double TailRootMm = 6, TailTipMm = 8, TailDepthMm = 5;
    /// <summary>How much bigger a loose trapdoor is than its opening, all round (mm).</summary>
    public const double TrapdoorLipMm = 2.5;

    /// <summary>How far the plate's edge is from the walls' centre lines (mm).</summary>
    public static double EdgeBeyond(FloorSpec s, double wallT) => s.Edge switch
    {
        FloorEdge.OverColumns => (s.Columns ? FloorKitMaker.ColumnReach(s) : wallT / 2) + FloorKitMaker.RimMm,
        FloorEdge.FlushWithWalls => wallT / 2,
        _ => wallT / 2 + Math.Clamp(s.OverhangMm, 0, 100),
    };

    /// <summary>The holes the columns poke up through, on the plan.</summary>
    public static PathsD ColumnHoles(FloorSpec s)
    {
        if (!s.Columns) return [];
        var hole = Geo.Offset([FloorKitMaker.ColumnTop(s)], Math.Max(0, s.FitMm) + FloorKitMaker.ColumnClearMm);
        var holes = new PathsD();
        foreach (var c in FloorKitMaker.ColumnCentres(s)) holes.AddRange(Geo.Move(hole, c.x, c.y));
        return Geo.Union(holes);
    }

    internal static void Make(KitBuilder k, PathsD hatch, double X, double Y, double wallT)
    {
        var s = k.Spec;
        double e = EdgeBeyond(s, wallT), T = k.T;
        var outline = Geo.Rect(-e, -e, X + e, Y + e);
        var holes = Geo.Union(ColumnHoles(s), hatch);
        var full = Geo.Subtract([outline], holes);
        var (etched, lines) = k.Texture(full, s.TextureAcross);
        var pose = Pose.Flat(0, 0, s.WallHeightMm + T / 2);
        int copies = Math.Max(1, s.Floors);
        int nx = Math.Clamp(s.PiecesAlong, 1, 6), ny = Math.Clamp(s.PiecesAcross, 1, 6);

        if (nx * ny == 1) k.Add("Floor", [outline], holes, copies, [pose], etched, lines);
        else
        {
            var cells = Split(k, full, -e, -e, X + e, Y + e, nx, ny);
            int n = 0;
            foreach (var cell in cells)
                k.Add($"Floor piece {++n}", cell, [], copies, [pose], Geo.Intersect(etched, cell), Geo.ClipLines(lines, cell));
            k.Note($"The floor is cut in {cells.Count} pieces that join with dovetails: push them together on a flat table, then lift the floor onto the walls (a drop of glue in each joint keeps it in one).");
        }

        if (s.Trapdoor && hatch.Count > 0)
        {
            var lid = Geo.Offset(hatch, TrapdoorLipMm, round: Geo.Pieces(hatch) == 1 && hatch[0].Count > 8);
            var b = Geo.Bounds(lid);
            lid = Geo.Move(lid, -b.left, -b.top);
            var (le, ll) = k.Texture(lid, !s.TextureAcross, 7);
            double w = b.right - b.left, h = b.bottom - b.top, r = Math.Clamp(Math.Min(w, h) * 0.06, 0.8, 2);
            ll.Add(Geo.Loop(Geo.Circle(w * 0.5, h * 0.82, r)));
            k.Add("Trapdoor", lid, [], copies, [], le, ll);
            k.Note("The trapdoor is loose: it lies over the opening, resting on the floor round it.");
        }
        double area = Geo.Area(full);
        if (area > 0 && s.Hatch && hatch.Count > 0 && Geo.Area(hatch) > area * 0.6)
            k.Warn("The opening takes up most of the floor: make it smaller or the room bigger.");
    }

    /// <summary>The plate cut on a grid of nx × ny pieces; each piece's dovetails reach into the next one along and the one above.</summary>
    static List<PathsD> Split(KitBuilder k, PathsD full, double x0, double y0, double x1, double y1, int nx, int ny)
    {
        double socketGrow = Math.Max(0, k.Fit) / 2;
        double[] xs = [.. Enumerable.Range(0, nx + 1).Select(i => x0 + (x1 - x0) * i / nx)];
        double[] ys = [.. Enumerable.Range(0, ny + 1).Select(j => y0 + (y1 - y0) * j / ny)];
        var tabs = new PathsD[nx, ny];
        var sockets = new PathsD[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++) { tabs[i, j] = []; sockets[i, j] = []; }
        var holes = Geo.Subtract([Geo.Rect(x0, y0, x1, y1)], full);
        int missing = 0;

        // One seam: from a to b along it, at c across; tails point towards +across.
        void Seam(bool upright, double c, double a, double b, PathsD into, PathsD socketsOf)
        {
            // Stretches along the seam where a hole comes near it.
            var taken = new List<(double, double)>();
            foreach (var h in holes)
            {
                var hb = Clipper.GetBounds(h);
                double lo = upright ? hb.left : hb.top, hi = upright ? hb.right : hb.bottom;
                if (hi < c - 2 || lo > c + TailDepthMm + 2) continue;
                taken.Add(upright ? (hb.top, hb.bottom) : (hb.left, hb.right));
            }
            double len = b - a;
            int want = Math.Max(1, (int)Math.Round(len / 45));
            var at = WallKitMaker.Spread(want, TailTipMm + 2, a + 5, b - 5, taken, 2);
            if (at.Count == 0) { missing++; return; }
            foreach (var m in at)
            {
                PathD tail = upright
                    ? [new(c - 0.01, m - TailRootMm / 2), new(c + TailDepthMm, m - TailTipMm / 2), new(c + TailDepthMm, m + TailTipMm / 2), new(c - 0.01, m + TailRootMm / 2)]
                    : [new(m + TailRootMm / 2, c - 0.01), new(m + TailTipMm / 2, c + TailDepthMm), new(m - TailTipMm / 2, c + TailDepthMm), new(m - TailRootMm / 2, c - 0.01)];
                into.Add(tail);
                socketsOf.AddRange(Geo.Offset([tail], socketGrow));
            }
        }

        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j < ny; j++) Seam(true, xs[i + 1], ys[j], ys[j + 1], tabs[i, j], sockets[i + 1, j]);
        for (int j = 0; j + 1 < ny; j++)
            for (int i = 0; i < nx; i++) Seam(false, ys[j + 1], xs[i], xs[i + 1], tabs[i, j], sockets[i, j + 1]);
        if (missing > 0) k.Warn($"{missing} of the joins between the floor's pieces have no room for a dovetail (the opening or the columns are in the way): glue those edges.");

        var cells = new List<PathsD>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var rect = Geo.Rect(i == 0 ? x0 - 1 : xs[i], j == 0 ? y0 - 1 : ys[j], i == nx - 1 ? x1 + 1 : xs[i + 1], j == ny - 1 ? y1 + 1 : ys[j + 1]);
                var cell = Geo.Intersect(full, [rect]);
                cell = Geo.Union(cell, Geo.Intersect(Geo.Union(tabs[i, j]), full));
                cell = Geo.Subtract(cell, Geo.Union(sockets[i, j]));
                cells.Add(cell);
            }
        return cells;
    }
}
