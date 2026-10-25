using PhotonCanvas.Core;
using SkiaSharp;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>How to pack parts onto material.</summary>
public sealed record NestOptions
{
    /// <summary>Region to fill (world mm): a closed outline (holes count as unusable). Null = the whole work area.</summary>
    public PathGeometry? Sheet { get; init; }
    /// <summary>Gap kept between parts, mm.</summary>
    public double SpacingMm { get; init; } = 2;
    /// <summary>Gap kept from the sheet's edge, mm.</summary>
    public double MarginMm { get; init; } = 2;
    /// <summary>Rotation step in degrees (0 = never rotate, 90 = quarter turns, 15 …).</summary>
    public double RotationStepDeg { get; init; } = 90;
    /// <summary>Grid size, mm: smaller packs tighter but takes longer.</summary>
    public double ResolutionMm { get; init; } = 0.5;
    /// <summary>Total copies of each part to place (1 = just the originals).</summary>
    public int Copies { get; init; } = 1;
    /// <summary>Keep adding copies of the parts (in turn) until nothing more fits.</summary>
    public bool FillSheet { get; init; }
    /// <summary>Upper limit on copies when filling, so a tiny part doesn't produce thousands.</summary>
    public int MaxCopies { get; init; } = 500;
    /// <summary>The shape used as the sheet (it isn't an obstacle).</summary>
    public Guid? SheetShapeId { get; init; }
    /// <summary>Start in the middle of the sheet and work outward (true), or pack from the top-left corner, keeping the leftover in one piece (false).</summary>
    public bool FromCenter { get; init; } = true;
}

/// <summary>What nesting did.</summary>
public sealed record NestReport(int Placed, int DidNotFit, IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Unplaced, double SheetUsedPercent);

/// <summary>
/// True-shape nesting on a grid: each part's silhouette (holes filled, grown by the spacing)
/// is slid over the sheet at each allowed rotation and placed at the free spot nearest the
/// sheet's middle (or the first free spot from the top-left, when packing from the corner).
/// Shapes already on the sheet are avoided. Parts are never put
/// inside another part's holes (the cut order would make that unsafe).
/// </summary>
public static class Nesting
{
    sealed class Mask(int w, int h, bool[] cells)
    {
        public int W = w, H = h;
        public bool[] Cells = cells;
        public int[]? Set; // indices of set cells, outline first: overlaps show up there soonest
        public int[] SetList()
        {
            if (Set is not null) return Set;
            bool Edge(int i)
            {
                int x = i % W, y = i / W;
                return x == 0 || y == 0 || x == W - 1 || y == H - 1 || !Cells[i - 1] || !Cells[i + 1] || !Cells[i - W] || !Cells[i + W];
            }
            var all = Enumerable.Range(0, Cells.Length).Where(i => Cells[i]).ToList();
            return Set = [.. all.Where(Edge), .. all.Where(i => !Edge(i))];
        }
        public (int Row, int Lo, int Hi)[]? Runs; // the set cells, row by row, as runs
        public (int Row, int Lo, int Hi)[] RunList()
        {
            if (Runs is not null) return Runs;
            var runs = new List<(int, int, int)>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!Cells[y * W + x]) continue;
                    int lo = x;
                    while (x + 1 < W && Cells[y * W + x + 1]) x++;
                    runs.Add((y, lo, x));
                }
            // The widest runs first: they hit whatever is in the way soonest.
            return Runs = [.. runs.OrderByDescending(r => r.Item3 - r.Item2)];
        }
    }

    sealed record Variant(double AngleDeg, Mask Grown, Mask Body, Vec2 Origin);

    /// <exception cref="ArgumentException">Nothing to nest.</exception>
    public static (DocState State, NestReport Report) Nest(DocState s, IReadOnlySet<Guid> ids, NestOptions o, MachineProfile machine, CancellationToken ct = default)
    {
        double r = Math.Clamp(o.ResolutionMm, 0.1, 5);
        var sheet = o.Sheet ?? PathGeometry.Rect(0, 0, machine.FieldW, machine.FieldH);
        var sheetBounds = sheet.Bounds(0.05);
        if (sheetBounds.IsEmpty) throw new ArgumentException("The sheet is empty.");
        // Never outside the work area. The grid reaches a little past the sheet so a part's
        // spacing halo may hang over its edge (only the part itself must stay inside).
        var within = new Rect2(Math.Max(0, sheetBounds.MinX), Math.Max(0, sheetBounds.MinY), Math.Min(machine.FieldW, sheetBounds.MaxX), Math.Min(machine.FieldH, sheetBounds.MaxY));
        int pad = (int)Math.Ceiling(Math.Max(0, o.SpacingMm) / r) + 2;
        var area = within.Inflate(pad * r);
        int W = Math.Max(1, (int)Math.Ceiling(area.Width / r)), H = Math.Max(1, (int)Math.Ceiling(area.Height / r));

        // Units: groups move together.
        var shapes = s.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        if (shapes.Count == 0) throw new ArgumentException("Select the parts to nest.");
        var units = shapes.GroupBy(x => x.GroupId ?? x.Id).Select(g => g.ToList()).ToList();

        // Where a part may not be: outside the sheet (or the work area), or within the margin of its edge.
        var edge = Rasterize(W, H, area, r, c =>
        {
            c.Clear(SKColors.White);
            using var inside = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
            using var p = ToSk(sheet, SKPathFillType.EvenOdd);
            c.Save();
            c.ClipRect(new SKRect((float)within.MinX, (float)within.MinY, (float)within.MaxX, (float)within.MaxY));
            c.DrawPath(p, inside);
            c.Restore();
            if (o.MarginMm > 0)
                using (var band = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = (float)(2 * o.MarginMm) })
                {
                    c.DrawPath(p, band);
                    c.DrawRect(new SKRect((float)within.MinX, (float)within.MinY, (float)within.MaxX, (float)within.MaxY), band);
                }
        });
        // What's already there: other visible shapes (each part is added as it's placed).
        var obstacles = Rasterize(W, H, area, r, c =>
        {
            c.Clear(SKColors.Black);
            using var obstacle = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
            using var line = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(r, 0.2) };
            // Only things that will be lasered take up material; guide layers (output off) and the sheet itself don't.
            var solid = s.Layers.Where(l => l.Visible && l.Output).Select(l => l.Id).ToHashSet();
            foreach (var other in s.Shapes.Where(x => !ids.Contains(x.Id) && x.Id != o.SheetShapeId && solid.Contains(x.LayerId) && x.Bounds.Intersects(area)))
                DrawSolid(c, other, obstacle, line);
        });

        double step = o.RotationStepDeg <= 0 ? 360 : Math.Clamp(o.RotationStepDeg, 1, 360);
        var angles = new List<double>();
        for (double a = 0; a < 360 - 1e-9; a += step) angles.Add(a);

        // Each unit's silhouettes at each rotation.
        var variants = units.Select(u => angles.Select(a => MakeVariant(u, a, r, o.SpacingMm)).ToList()).ToList();
        // Biggest first packs best.
        var order = Enumerable.Range(0, units.Count).OrderByDescending(i => variants[i][0].Body.SetList().Length).ToList();

        // For each cell, the last blocked cell at or left of it in its row (-1 = none): a run of a
        // part's cells is clear exactly when the last blocked cell before its right end is left of it.
        void Last(bool[] g, int[] last, int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int run = -1;
                for (int x = 0; x < W; x++)
                {
                    if (g[y * W + x]) run = x;
                    last[y * W + x] = run;
                }
            }
        }
        // The same from the other side: the first blocked cell at or right of it (W = none).
        void First(bool[] g, int[] first, int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int run = W;
                for (int x = W - 1; x >= 0; x--)
                {
                    if (g[y * W + x]) run = x;
                    first[y * W + x] = run;
                }
            }
        }
        var edgeLast = new int[W * H];
        var edgeFirst = new int[W * H];
        var obstacleLast = new int[W * H];
        var obstacleFirst = new int[W * H];
        Last(edge, edgeLast, 0, H);
        First(edge, edgeFirst, 0, H);
        Last(obstacles, obstacleLast, 0, H);
        First(obstacles, obstacleFirst, 0, H);
        // Obstacles plus the parts placed so far (reset for each packing tried).
        bool[] occupied = [];
        int[] occLast = [], occFirst = [];

        // -1 when the part fits at (x, y); otherwise the next x worth trying in this row (every spot
        // before it overlaps the same blocked cell).
        int Blocked(Variant v, int x, int y, int[] occ)
        {
            foreach (var (row, lo, hi) in v.Body.RunList())
            {
                int b = edgeLast[(y + row) * W + x + hi];
                if (b >= x + lo) return b - lo + 1;
            }
            foreach (var (row, lo, hi) in v.Grown.RunList())
            {
                int b = occ[(y + row) * W + x + hi];
                if (b >= x + lo) return b - lo + 1;
            }
            return -1;
        }
        // Scanning leftward: int.MaxValue when the part fits at (x, y); otherwise the next x worth trying to the left.
        int BlockedLeft(Variant v, int x, int y)
        {
            foreach (var (row, lo, hi) in v.Body.RunList())
            {
                int b = edgeFirst[(y + row) * W + x + lo];
                if (b <= x + hi) return b - hi - 1;
            }
            foreach (var (row, lo, hi) in v.Grown.RunList())
            {
                int b = occFirst[(y + row) * W + x + lo];
                if (b <= x + hi) return b - hi - 1;
            }
            return int.MaxValue;
        }

        // The middle of the sheet, in grid cells.
        double midX = (within.Center.X - area.MinX) / r, midY = (within.Center.Y - area.MinY) / r;

        // The free spot whose centre is nearest the middle: rows outward from the middle, and in
        // each row the first free spot to the right and to the left of it.
        (int X, int Y, Variant V)? FindNearMiddle(List<Variant> vs)
        {
            (int X, int Y, Variant V)? best = null;
            double bestD = double.MaxValue;
            foreach (var v in vs)
            {
                var g = v.Grown;
                if (g.W > W || g.H > H) continue;
                int maxX = W - g.W, maxY = H - g.H;
                double hx = g.W / 2.0, hy = g.H / 2.0;
                int x0 = Math.Clamp((int)Math.Round(midX - hx), 0, maxX), y0 = Math.Clamp((int)Math.Round(midY - hy), 0, maxY);
                for (int d = 0; ; d++)
                {
                    bool any = false;
                    foreach (var y in d == 0 ? new[] { y0 } : new[] { y0 + d, y0 - d })
                    {
                        if (y < 0 || y > maxY) continue;
                        double dy2 = (y + hy - midY) * (y + hy - midY);
                        if (dy2 >= bestD) continue;
                        any = true;
                        for (int x = x0; x <= maxX;)
                        {
                            double dx = x + hx - midX;
                            if (dx * dx + dy2 >= bestD) break;
                            int next = Blocked(v, x, y, occLast);
                            if (next < 0) { bestD = dx * dx + dy2; best = (x, y, v); break; }
                            x = Math.Max(next, x + 1);
                        }
                        for (int x = x0 - 1; x >= 0;)
                        {
                            double dx = x + hx - midX;
                            if (dx * dx + dy2 >= bestD) break;
                            int next = BlockedLeft(v, x, y);
                            if (next == int.MaxValue) { bestD = dx * dx + dy2; best = (x, y, v); break; }
                            x = Math.Min(next, x - 1);
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    // Rows only get farther from the middle from here on.
                    if (!any) break;
                }
            }
            return best;
        }

        (int X, int Y, Variant V)? FindSpot(List<Variant> vs)
        {
            (int X, int Y, Variant V)? best = null;
            foreach (var v in vs)
            {
                var g = v.Grown;
                for (int y = 0; y + g.H <= H; y++)
                {
                    if (best is { } b && y > b.Y) break;
                    for (int x = 0; x + g.W <= W;)
                    {
                        if (best is { } bb && y == bb.Y && x >= bb.X) break;
                        int next = Blocked(v, x, y, occLast);
                        if (next >= 0) { x = Math.Max(next, x + 1); continue; }
                        best = (x, y, v);
                        break;
                    }
                    ct.ThrowIfCancellationRequested();
                }
            }
            return best;
        }

        void Occupy(int x, int y, Variant v)
        {
            // Mark the part itself; the next part's grown outline keeps the spacing from it.
            var body = v.Body;
            foreach (var i in body.SetList()) occupied[(y + i / body.W) * W + x + i % body.W] = true;
            Last(occupied, occLast, y, y + body.H);
            First(occupied, occFirst, y, y + body.H);
        }

        // Every part (originals first, then copies in turn) given a spot on a fresh sheet.
        (List<(int Ui, int X, int Y, Variant V, bool Copy)> Spots, int Failed, List<int> Unplaced) Pack(bool fromMiddle)
        {
            occupied = (bool[])obstacles.Clone();
            occLast = (int[])obstacleLast.Clone();
            occFirst = (int[])obstacleFirst.Clone();
            (int X, int Y, Variant V)? Find(List<Variant> vs) => fromMiddle ? FindNearMiddle(vs) : FindSpot(vs);
            var spots = new List<(int Ui, int X, int Y, Variant V, bool Copy)>();
            var left = new List<int>();
            int failed = 0, copies = 0;
            foreach (var ui in order)
            {
                ct.ThrowIfCancellationRequested();
                if (Find(variants[ui]) is { } spot)
                {
                    spots.Add((ui, spot.X, spot.Y, spot.V, false));
                    Occupy(spot.X, spot.Y, spot.V);
                }
                else
                {
                    failed++;
                    left.Add(ui);
                }
            }
            int extraEach = o.FillSheet ? int.MaxValue : Math.Max(0, o.Copies - 1);
            var full = new HashSet<int>();
            for (int round = 0; round < extraEach && full.Count < order.Count && copies < o.MaxCopies; round++)
            {
                foreach (var ui in order)
                {
                    if (full.Contains(ui) || copies >= o.MaxCopies) continue;
                    ct.ThrowIfCancellationRequested();
                    if (Find(variants[ui]) is not { } spot)
                    {
                        full.Add(ui);
                        if (!o.FillSheet) failed++;
                        continue;
                    }
                    spots.Add((ui, spot.X, spot.Y, spot.V, true));
                    copies++;
                    Occupy(spot.X, spot.Y, spot.V);
                }
            }
            return (spots, failed, left);
        }

        // A corner packing moved as a block (all the way, or as far toward it as it can) so it sits around the middle.
        List<(int Ui, int X, int Y, Variant V, bool Copy)> Centred(List<(int Ui, int X, int Y, Variant V, bool Copy)> spots)
        {
            if (spots.Count == 0) return spots;
            int minX = spots.Min(p => p.X), minY = spots.Min(p => p.Y);
            int maxX = spots.Max(p => p.X + p.V.Grown.W), maxY = spots.Max(p => p.Y + p.V.Grown.H);
            int dx = (int)Math.Round(midX - (minX + maxX) / 2.0), dy = (int)Math.Round(midY - (minY + maxY) / 2.0);
            bool Fits(Variant v, int x, int y) => x >= 0 && y >= 0 && x + v.Grown.W <= W && y + v.Grown.H <= H && Blocked(v, x, y, obstacleLast) < 0;
            foreach (var f in new[] { 1, 0.75, 0.5, 0.25 })
            {
                int sx = (int)Math.Round(dx * f), sy = (int)Math.Round(dy * f);
                if (spots.All(p => Fits(p.V, p.X + sx, p.Y + sy)))
                    return [.. spots.Select(p => p with { X = p.X + sx, Y = p.Y + sy })];
            }
            return spots;
        }

        Affine Placement(int ui, int x, int y, Variant v)
        {
            var c = UnitBounds(units[ui]).Center;
            var target = new Vec2(area.MinX + x * r, area.MinY + y * r);
            return Affine.RotateAbout(c, v.AngleDeg).Then(Affine.Translate(target - v.Origin));
        }

        var packed = Pack(o.FromCenter);
        if (o.FromCenter && (packed.Failed > 0 || o.FillSheet))
        {
            // Starting in the middle can leave odd gaps at the edges; when packing from the corner
            // fits more, use that, moved toward the middle as far as the sheet allows.
            var corner = Pack(false);
            if (corner.Spots.Count > packed.Spots.Count) packed = (Centred(corner.Spots), corner.Failed, corner.Unplaced);
        }

        var state = s;
        var added = new List<Guid>();
        foreach (var (ui, x, y, v, copy) in packed.Spots)
        {
            if (!copy)
            {
                state = Ops.Transform(state, units[ui].Select(sh => sh.Id).ToHashSet(), Placement(ui, x, y, v));
                continue;
            }
            // Copy of the original part (as it was before nesting), then placed like a fresh one.
            var copies = Ops.CopyAsUnit(units[ui], Placement(ui, x, y, v));
            state = Ops.AddShapes(state, copies);
            added.AddRange(copies.Select(c => c.Id));
        }
        var unplaced = packed.Unplaced.SelectMany(ui => units[ui].Select(sh => sh.Id)).ToList();
        int placed = packed.Spots.Count, failed = packed.Failed;
        double usable = 0, used = 0;
        for (int i = 0; i < edge.Length; i++) if (!edge[i]) usable++;
        foreach (var x in state.Shapes.Where(x => ids.Contains(x.Id) || added.Contains(x.Id)))
            used += SolidArea(x);
        return (state, new NestReport(placed, failed, added, unplaced, usable > 0 ? Math.Min(100, 100 * used / (usable * r * r)) : 0));
    }

    static Rect2 UnitBounds(List<Shape> unit) => unit.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));

    static Variant MakeVariant(List<Shape> unit, double angle, double r, double spacing)
    {
        var c = UnitBounds(unit).Center;
        var rot = Affine.RotateAbout(c, angle);
        var b = unit.Select(x => RotatedBounds(x, rot)).Aggregate(Rect2.Empty, (a, bb) => a.Union(bb));
        int pad = (int)Math.Ceiling(spacing / r) + 1;
        var origin = new Vec2(b.MinX - pad * r, b.MinY - pad * r);
        int w = (int)Math.Ceiling(b.Width / r) + 2 * pad, h = (int)Math.Ceiling(b.Height / r) + 2 * pad;
        var region = new Rect2(origin.X, origin.Y, origin.X + w * r, origin.Y + h * r);
        bool[] Draw(double grow) => Rasterize(w, h, region, r, cv =>
        {
            cv.Clear(SKColors.Black);
            cv.Save();
            var m = SkMatrix(rot);
            cv.Concat(ref m);
            using var fill = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
            using var line = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(2 * grow, r), StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            foreach (var x in unit) DrawSolid(cv, x, fill, line, grow > 0);
            cv.Restore();
        });
        var grown = new Mask(w, h, Draw(spacing));
        // The body mask uses the same frame (so placement offsets agree); only the outline growth differs.
        var body = new Mask(w, h, Draw(0));
        return new Variant(angle, grown, body, origin);
    }

    static Rect2 RotatedBounds(Shape x, Affine rot)
    {
        if (x is ImageShape im) return Rect2.Bounds(im.Corners.Select(rot.Apply));
        if (x is PathShape ps) return Rect2.Bounds(ps.World.Flatten(0.1).SelectMany(f => f.Points).Select(rot.Apply));
        return x.Bounds;
    }

    /// <summary>A shape as a solid silhouette: closed outlines filled (holes too), open lines stroked; optionally its outline stroked as well (growth).</summary>
    static void DrawSolid(SKCanvas c, Shape x, SKPaint fill, SKPaint line, bool strokeOutline = false)
    {
        if (x is ImageShape im)
        {
            using var p = new SKPath();
            p.AddPoly(im.Corners.Select(v => new SKPoint((float)v.X, (float)v.Y)).ToArray(), true);
            c.DrawPath(p, fill);
            if (strokeOutline) c.DrawPath(p, line);
            return;
        }
        if (x is not PathShape ps) return;
        foreach (var f in ps.World.Flatten(0.05))
        {
            if (f.Points.Count < 2) continue;
            using var p = new SKPath();
            p.AddPoly(f.Points.Select(v => new SKPoint((float)v.X, (float)v.Y)).ToArray(), f.Closed);
            if (f.Closed) c.DrawPath(p, fill);
            if (!f.Closed || strokeOutline) c.DrawPath(p, line);
        }
    }

    static double SolidArea(Shape x)
    {
        if (x is ImageShape im) return Math.Abs(Geom.SignedArea([.. im.Corners]));
        if (x is not PathShape ps) return 0;
        // Outer outlines only (holes count as used material, which they are for cutting).
        var loops = ps.World.Flatten(0.1).Where(f => f.Closed && f.Points.Count > 2).Select(f => f.Points).ToList();
        return loops.Where(l => !loops.Any(o => !ReferenceEquals(o, l) && Contains(o, l[0]))).Sum(l => Math.Abs(Geom.SignedArea(l)));
    }

    static bool Contains(List<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    static SKMatrix SkMatrix(Affine a) => new((float)a.A, (float)a.C, (float)a.E, (float)a.B, (float)a.D, (float)a.F, 0, 0, 1);

    static SKPath ToSk(PathGeometry g, SKPathFillType fill)
    {
        var p = new SKPath { FillType = fill };
        foreach (var f in g.Flatten(0.05))
            if (f.Points.Count > 1) p.AddPoly(f.Points.Select(v => new SKPoint((float)v.X, (float)v.Y)).ToArray(), f.Closed);
        return p;
    }

    /// <summary>Draw in mm over <paramref name="area"/> into a w × h grid; returns true where the result is white (blocked / set).</summary>
    static bool[] Rasterize(int w, int h, Rect2 area, double r, Action<SKCanvas> draw)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Scale((float)(1 / r));
            c.Translate((float)-area.MinX, (float)-area.MinY);
            draw(c);
        }
        var px = bmp.Bytes;
        var o = new bool[w * h];
        for (int i = 0; i < o.Length; i++) o[i] = px[i * 4] > 127;
        return o;
    }
}
