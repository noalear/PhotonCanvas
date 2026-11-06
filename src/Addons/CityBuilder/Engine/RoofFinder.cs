namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>A roof seen on a battle map: the rectangles it's made of (working pixels, all at one turn), its colour, and how surely its edges are drawn.</summary>
public sealed record SeenRoof
{
    /// <summary>Its rectangles, each in the picture's frame, all turned the same way; the first is the biggest.</summary>
    public List<MapVision.Box> Rects { get; init; } = [];
    /// <summary>The turn they share (degrees, 0 to 90).</summary>
    public double AngleDeg { get; init; }
    public double R { get; init; }
    public double G { get; init; }
    public double B { get; init; }
    /// <summary>How much of its outline is drawn (0 to 1).</summary>
    public double Support { get; init; }
    /// <summary>How much of its rectangles the colour patch fills.</summary>
    public double Fill { get; init; }
    /// <summary>How much the strokes drawn on it run one way (rows of tiles: high; cobbles: low), 0 to 1; −1 when it's plain.</summary>
    public double Aligned { get; init; }
    /// <summary>The cells of the patch it came from (working cells).</summary>
    public List<int> Cells { get; init; } = [];
    public double Area => Rects.Sum(r => r.Area);
}

/// <summary>
/// Finds roofs among a picture's colour patches: a roof is a rectangle (or a few, an L or a T) that a
/// patch fills, with a drawn line or a change of colour most of the way round it. Patches that are the
/// two slopes of one roof (the same hue, lit differently, meeting along the ridge) are joined first; a
/// patch that leaks out past the roof into ground of the same colour gives up the biggest rectangle
/// inside it instead. Each edge is then slid a little to where the drawn line really is.
/// </summary>
public static class RoofFinder
{
    public sealed record Options
    {
        /// <summary>The biggest roof to look for (squares).</summary>
        public double LargestSquares { get; init; } = 120;
        /// <summary>The share of an outline that must be drawn (0.5 usual; less finds more and makes more mistakes).</summary>
        public double Sensitivity { get; init; } = 0.5;
    }

    /// <summary>Where to send what the finder tries (for tuning); null for nowhere.</summary>
    public static Action<string>? Log { get; set; }

    public static List<SeenRoof> Find(MapVision v, Options o, CancellationToken ct = default)
    {
        double sq = v.Square, sqArea = sq * sq;
        double least = 0.6 * sqArea, most = Math.Max(1, o.LargestSquares) * sqArea * 1.6;
        // The patches that could be roofs, or parts of them: big enough, not too big, not the lawn or the river.
        var cands = v.Patches.Where(p => p.Area >= least && p.Area <= most && !Ground(p)).ToList();
        ct.ThrowIfCancellationRequested();
        var groups = Join(v, cands, ct);
        var roofs = new List<SeenRoof>();
        foreach (var g in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (Shape(v, g, o) is { } roof) roofs.Add(roof);
        }
        roofs = JoinHalves(v, roofs, o);
        // Then the blocks that aren't the ground's colour, texture and all (see <see cref="RoofBlobs"/>): the roofs the patches missed.
        foreach (var blob in RoofBlobs.Find(v, least, most, v.Patches.Count, ct))
        {
            ct.ThrowIfCancellationRequested();
            roofs.AddRange(ShapeBlob(v, blob, o));
        }
        // Where two overlap, the surer one stays.
        roofs = roofs.OrderByDescending(r => r.Support * Math.Sqrt(r.Area)).ToList();
        var kept = new List<SeenRoof>();
        foreach (var r in roofs)
        {
            bool clash = false;
            foreach (var k in kept)
            {
                double over = Overlap(r, k, sq);
                if (over > 0.4 * Math.Min(r.Area, k.Area)) { clash = true; break; }
            }
            if (!clash) kept.Add(r);
        }
        return kept;
    }

    /// <summary>
    /// Roofs found as two slopes apart (one lit, one in shade, the ridge drawn between them) joined into
    /// one: two single rectangles at the same turn, alike in colour, side by side along most of a side
    /// with next to no gap, whose joint rectangle is hardly bigger than the two together.
    /// </summary>
    static List<SeenRoof> JoinHalves(MapVision v, List<SeenRoof> roofs, Options o)
    {
        double sq = v.Square;
        var list = new List<SeenRoof>(roofs);
        bool joined = true;
        int rounds = 0;
        while (joined && rounds++ < 4)
        {
            joined = false;
            for (int i = 0; i < list.Count && !joined; i++)
                for (int j = i + 1; j < list.Count && !joined; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    if (a.Rects.Count != 1 || b.Rects.Count != 1) continue;
                    double da = Math.Abs(a.AngleDeg - b.AngleDeg);
                    da = Math.Min(da, 90 - da);
                    if (da > 6) continue;
                    if (!AlikeColours(a.R, a.G, a.B, b.R, b.G, b.B)) continue;
                    var ra = a.Rects[0];
                    var rb = b.Rects[0];
                    // Both boxes' corners in a's frame: the joint box, and how it compares with the two.
                    var pts = ra.Corners().Concat(rb.Corners()).Select(c => ra.Local(c.X, c.Y)).ToList();
                    double u0 = pts.Min(p => p.U), u1 = pts.Max(p => p.U), v0 = pts.Min(p => p.V), v1 = pts.Max(p => p.V);
                    double jointArea = (u1 - u0) * (v1 - v0);
                    if (jointArea > (ra.Area + rb.Area) * 1.12) continue;
                    // Side by side with next to no gap, and no overlap worth the name.
                    var lb = rb.Corners().Select(c => ra.Local(c.X, c.Y)).ToList();
                    double bu0 = lb.Min(p => p.U), bu1 = lb.Max(p => p.U), bv0 = lb.Min(p => p.V), bv1 = lb.Max(p => p.V);
                    double gapU = Math.Max(bu0 - ra.W / 2, -ra.W / 2 - bu1), gapV = Math.Max(bv0 - ra.H / 2, -ra.H / 2 - bv1);
                    double gap = Math.Max(gapU, gapV);
                    if (gap > 0.3 * sq || gap < -0.3 * sq) continue;
                    double along = gapU >= gapV ? Math.Min(ra.H / 2, bv1) - Math.Max(-ra.H / 2, bv0) : Math.Min(ra.W / 2, bu1) - Math.Max(-ra.W / 2, bu0);
                    double shorter = gapU >= gapV ? Math.Min(ra.H, bv1 - bv0) : Math.Min(ra.W, bu1 - bu0);
                    if (along < 0.6 * shorter) continue;
                    double cu = (u0 + u1) / 2, cv = (v0 + v1) / 2, ang = ra.AngleDeg * Math.PI / 180;
                    var joint = new MapVision.Box(ra.Cx + cu * Math.Cos(ang) - cv * Math.Sin(ang), ra.Cy + cu * Math.Sin(ang) + cv * Math.Cos(ang), ra.AngleDeg, u1 - u0, v1 - v0);
                    if (joint.Area > o.LargestSquares * sq * sq) continue;
                    double wa = ra.Area, wb = rb.Area;
                    var cells = new List<int>(a.Cells);
                    cells.AddRange(b.Cells);
                    var merged = new SeenRoof
                    {
                        Rects = [joint], AngleDeg = ra.AngleDeg, Cells = cells,
                        R = (a.R * wa + b.R * wb) / (wa + wb), G = (a.G * wa + b.G * wb) / (wa + wb), B = (a.B * wa + b.B * wb) / (wa + wb),
                        Support = Math.Min(a.Support, b.Support), Fill = Math.Min(a.Fill, b.Fill),
                    };
                    Log?.Invoke($"halves joined at {joint.Cx / sq:0.0},{joint.Cy / sq:0.0}: {joint.W / sq:0.0}x{joint.H / sq:0.0}");
                    list.RemoveAt(j);
                    list.RemoveAt(i);
                    list.Add(merged);
                    joined = true;
                }
        }
        return list;
    }

    static bool AlikeColours(double r1, double g1, double b1, double r2, double g2, double b2)
    {
        double m1 = Math.Max(0.08, (r1 + g1 + b1) / 3), m2 = Math.Max(0.08, (r2 + g2 + b2) / 3);
        double dr = r1 / m1 - r2 / m2, dg = g1 / m1 - g2 / m2, db = b1 / m1 - b2 / m2;
        return Math.Sqrt(dr * dr + dg * dg + db * db) < 0.2 && Math.Abs(m1 - m2) < 0.4;
    }

    /// <summary>Grass, trees and water by colour: never a roof.</summary>
    internal static bool Ground(MapVision.Patch p)
    {
        var (h, s, _) = p.Hsv;
        if (s > 0.16 && h >= 62 && h < 170) return true;
        if (s > 0.28 && h >= 170 && h <= 260 && p.B > p.R + 0.08) return true;
        // Yellow-green and busy with strokes: a bush, not a thatched roof.
        if (s > 0.3 && h >= 40 && h < 62 && p.Tex > 0.25) return true;
        return false;
    }

    /// <summary>
    /// Two patches alike enough in colour to be two slopes of one roof: the same colour once brightness
    /// is taken out (one slope lit, the other in shade), so grey matches grey and tan matches tan, but
    /// not grey.
    /// </summary>
    static bool Alike(MapVision.Patch a, MapVision.Patch b)
    {
        var (_, _, va) = a.Hsv;
        var (_, _, vb) = b.Hsv;
        return ColourDistance(a, b) < 0.11 && Math.Abs(va - vb) < 0.45;
    }

    /// <summary>How far apart two patches' colours are with their brightness taken out.</summary>
    static double ColourDistance(MapVision.Patch a, MapVision.Patch b)
    {
        var (ra, ga, ba) = Normal(a);
        var (rb, gb, bb) = Normal(b);
        return Math.Sqrt((ra - rb) * (ra - rb) + (ga - gb) * (ga - gb) + (ba - bb) * (ba - bb));
    }

    /// <summary>A patch's colour with its brightness taken out (each channel over the mean of the three).</summary>
    static (double R, double G, double B) Normal(MapVision.Patch p)
    {
        double m = Math.Max(0.08, (p.R + p.G + p.B) / 3);
        return (p.R / m, p.G / m, p.B / m);
    }

    /// <summary>
    /// Patches joined into roofs: two that touch along a good length, alike in colour, whose union is at
    /// least as rectangular as either alone (the two slopes of a gable, the four of a hip), one group.
    /// </summary>
    static List<List<MapVision.Patch>> Join(MapVision v, List<MapVision.Patch> cands, CancellationToken ct)
    {
        var index = cands.ToDictionary(p => p.Id, p => p);
        double sliver = 0.6 * v.Square * v.Square;
        bool Sliver(int id, MapVision.Patch from) => !index.ContainsKey(id) && v.Patches[id].Area < sliver && !Ground(v.Patches[id]) && Alike(from, v.Patches[id]);
        // How long each pair's shared boundary is (working cells).
        var shared = new Dictionary<(int, int), int>();
        foreach (var p in cands)
            foreach (int c in p.Edge)
            {
                int x = c % v.Width, y = c / v.Width;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= v.Width || ny >= v.Height) continue;
                    int q = v.PatchOf[v.Index(nx, ny)];
                    // A line between them (the ridge), or a sliver of a patch (a ridge too faint to be a line, left as
                    // a dark strip of its own), counts: look up to three cells further.
                    for (int step = 2; (q < 0 || Sliver(q, p)) && step <= 4; step++)
                    {
                        int fx = x + dx * step, fy = y + dy * step;
                        if (fx < 0 || fy < 0 || fx >= v.Width || fy >= v.Height) break;
                        q = v.PatchOf[v.Index(fx, fy)];
                        if (q >= 0 ? !Sliver(q, p) : !v.Line[v.Index(fx, fy)]) break;
                    }
                    if (q >= 0 && Sliver(q, p)) continue;
                    if (q < 0 || q == p.Id || !index.ContainsKey(q)) continue;
                    var key = (Math.Min(p.Id, q), Math.Max(p.Id, q));
                    shared[key] = shared.GetValueOrDefault(key) + 1;
                }
            }
        var parent = cands.ToDictionary(p => p.Id, p => p.Id);
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var cells = cands.ToDictionary(p => p.Id, p => new List<int>(p.Cells));
        // Each group's hull (from its patches' edge cells): the hull of a union is the hull of the two hulls, far fewer points than the cells.
        var hull = cands.ToDictionary(p => p.Id, p => MapVision.Hull(Corners(v, p.Edge)));
        var fill = cands.ToDictionary(p => p.Id, p => Filled(v, p.Cells, MapVision.MinBox(hull[p.Id])).Count / Math.Max(1, MapVision.MinBox(hull[p.Id]).Area));
        var short_ = cands.ToDictionary(p => p.Id, p => { var b = MapVision.MinBox(hull[p.Id]); return Math.Min(b.W, b.H); });
        // Closest pairs first, again and again until nothing more joins.
        var pairs = shared.Where(kv => kv.Value >= 3).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        bool merged = true;
        int rounds = 0;
        while (merged && rounds++ < 6)
        {
            merged = false;
            ct.ThrowIfCancellationRequested();
            foreach (var (a0, b0) in pairs)
            {
                int a = Root(a0), b = Root(b0);
                if (a == b) continue;
                bool big = index[a0].Area >= 2 * v.Square * v.Square && index[b0].Area >= 2 * v.Square * v.Square;
                if (!Alike(index[a0], index[b0])) { if (big) Log?.Invoke($"  no join {a0}+{b0} ({index[a0].Area / v.Square / v.Square:0.0}+{index[b0].Area / v.Square / v.Square:0.0} sq): colours {index[a0].Hsv} vs {index[b0].Hsv}"); continue; }
                // Along at least a third of the shorter side of the smaller one (half of it for the two halves of a roof).
                int touch = shared[(a0, b0)];
                if (touch < 0.35 * Math.Min(short_[a], short_[b])) { if (big) Log?.Invoke($"  no join {a0}+{b0}: touch {touch} < {0.35 * Math.Min(short_[a], short_[b]):0}"); continue; }
                var both = new List<int>(cells[a]);
                both.AddRange(cells[b]);
                var bothHull = MapVision.Hull([.. hull[a], .. hull[b]]);
                var bothBox = MapVision.MinBox(bothHull);
                double f = bothBox.Area <= 0 ? 0 : Filled(v, both, bothBox).Count / bothBox.Area;
                // Together they must make a cleaner rectangle than either alone: two slopes do, a roof and a strip of road beside it don't;
                // a small piece added to a big one must finish the rectangle off nearly perfectly.
                double sizes = Math.Min(cells[a].Count, cells[b].Count) / (double)Math.Max(cells[a].Count, cells[b].Count);
                double needF = sizes < 0.3 ? Math.Max(0.85, Math.Max(fill[a], fill[b]) - 0.03) : Math.Max(0.7, Math.Max(fill[a], fill[b]) - 0.08);
                if (f < needF) { if (big) Log?.Invoke($"  no join {a0}+{b0}: fill {f:0.00} vs {fill[a]:0.00},{fill[b]:0.00}"); continue; }
                if (big) Log?.Invoke($"  join {a0}+{b0} ({index[a0].Area / v.Square / v.Square:0.0}+{index[b0].Area / v.Square / v.Square:0.0} sq at {index[a0].X0 / v.Square:0.0},{index[a0].Y0 / v.Square:0.0} / {index[b0].X0 / v.Square:0.0},{index[b0].Y0 / v.Square:0.0}): fill {f:0.00}");
                parent[b] = a;
                cells[a] = both;
                hull[a] = bothHull;
                fill[a] = f;
                short_[a] = Math.Min(bothBox.W, bothBox.H);
                merged = true;
            }
        }
        var groups = new Dictionary<int, List<MapVision.Patch>>();
        foreach (var p in cands)
        {
            int r = Root(p.Id);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = [];
            list.Add(p);
        }
        return [.. groups.Values];
    }

    /// <summary>The four corners of each of some cells, as points.</summary>
    static List<(double X, double Y)> Corners(MapVision v, IEnumerable<int> cells)
    {
        var pts = new List<(double X, double Y)>();
        foreach (int c in cells)
        {
            double x = c % v.Width, y = c / v.Width;
            pts.Add((x, y)); pts.Add((x + 1, y)); pts.Add((x, y + 1)); pts.Add((x + 1, y + 1));
        }
        return pts;
    }

    /// <summary>The cells inside a box that count as the roof's: the patch's own, lines, and small patches wholly inside the box.</summary>
    static HashSet<int> Filled(MapVision v, List<int> cells, MapVision.Box box)
    {
        var set = new HashSet<int>(cells);
        var corners = box.Corners();
        int x0 = Math.Max(0, (int)corners.Min(c => c.X)), x1 = Math.Min(v.Width - 1, (int)Math.Ceiling(corners.Max(c => c.X)));
        int y0 = Math.Max(0, (int)corners.Min(c => c.Y)), y1 = Math.Min(v.Height - 1, (int)Math.Ceiling(corners.Max(c => c.Y)));
        var inside = new Dictionary<int, bool>();
        double small = v.Square * v.Square * 1.5;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!box.Contains(x + 0.5, y + 0.5)) continue;
                int i = v.Index(x, y);
                if (set.Contains(i)) continue;
                if (v.Line[i]) { set.Add(i); continue; }
                int q = v.PatchOf[i];
                if (q < 0) continue;
                if (!inside.TryGetValue(q, out bool ok))
                {
                    var other = v.Patches[q];
                    ok = other.Area <= small && other.Cells.All(c => box.Contains(c % v.Width + 0.5, c / v.Width + 0.5, 1));
                    inside[q] = ok;
                }
                if (ok) set.Add(i);
            }
        return set;
    }

    /// <summary>
    /// The roof a group of patches makes: its rectangle (or the biggest rectangles inside it, when it
    /// leaks or is an L), each edge slid to the drawn line, kept when the outline shows most of the way round.
    /// </summary>
    static SeenRoof? Shape(MapVision v, List<MapVision.Patch> group, Options o)
    {
        double sq = v.Square, sqArea = sq * sq;
        var cells = group.SelectMany(p => p.Cells).ToList();
        if (cells.Count < 0.6 * sqArea) return null;
        var box = v.MinBox(cells);
        var filled = Filled(v, cells, box);
        double fill = filled.Count / Math.Max(1, box.Area);
        var rects = new List<MapVision.Box>();
        if (fill >= 0.8 && box.W >= 0.7 * sq && box.H >= 0.7 * sq) rects.Add(box);
        else
        {
            // The biggest rectangles inside it, at its turn: the roof without the leak, or the arms of an L.
            foreach (var r in Inner(v, [.. filled], box.AngleDeg, sq))
            {
                if (rects.Count > 0 && !Touches(rects, r, sq)) continue;
                rects.Add(r);
                if (rects.Count == 3) break;
            }
        }
        return Check(v, group, cells, filled, box, fill, rects, o, "group");
    }

    /// <summary>
    /// The roofs in a blob (see <see cref="RoofBlobs"/>): a blob is as often a row of buildings as one,
    /// so every rectangle inside it big enough to be a roof is tried, the ones that touch as one roof
    /// (an L, a T) and the rest each on its own.
    /// </summary>
    static List<SeenRoof> ShapeBlob(MapVision v, MapVision.Patch blob, Options o)
    {
        double sq = v.Square, sqArea = sq * sq;
        var res = new List<SeenRoof>();
        var cells = blob.Cells;
        if (cells.Count < 0.6 * sqArea) return res;
        var box = v.MinBox(cells);
        var filled = Filled(v, cells, box);
        double fill = filled.Count / Math.Max(1, box.Area);
        var clusters = new List<List<MapVision.Box>>();
        if (fill >= 0.8 && box.W >= 0.7 * sq && box.H >= 0.7 * sq) clusters.Add([box]);
        else
            foreach (var r in Inner(v, [.. filled], box.AngleDeg, sq))
            {
                if (r.Area < 1.2 * sqArea) continue;
                var home = clusters.FirstOrDefault(c => c.Count < 3 && Touches(c, r, sq));
                if (home is null) clusters.Add([r]);
                else home.Add(r);
            }
        foreach (var rects in clusters)
            if (Check(v, [blob], cells, filled, box, fill, rects, o, "blob") is { } roof) res.Add(roof);
        return res;
    }

    /// <summary>
    /// Some rectangles chosen for a patch (or a blob) checked as a roof: slid to their drawn lines, each
    /// big enough, drawn round most of their outline, the strokes inside running one way.
    /// </summary>
    static SeenRoof? Check(MapVision v, List<MapVision.Patch> group, List<int> cells, HashSet<int> filled, MapVision.Box box, double fill, List<MapVision.Box> rects, Options o, string kind)
    {
        double sq = v.Square, sqArea = sq * sq;
        if (rects.Count == 0) return null;
        if (rects.Sum(r => r.Area) > o.LargestSquares * sqArea) return null;
        // A strip running off the picture at both ends is a road (or a river), not a roof.
        if (rects.Any(r => OffBothEnds(v, r))) return null;
        // A rectangle found inside a patch that's mostly something else (a road with a straight bit) isn't a roof;
        // a blob is a row of buildings as often as one, so each of its rectangles stands on its own.
        if (kind == "group" && rects.Sum(r => r.Area) < 0.45 * cells.Count) return null;
        // Each edge to where the line is drawn, then how much of the outline is drawn.
        var set = new HashSet<int>(cells);
        for (int i = 0; i < rects.Count; i++) rects[i] = Slide(v, rects[i], set);
        rects = rects.Where(r => r.W >= 0.8 * sq && r.H >= 0.8 * sq && r.Area >= 1.2 * sqArea).ToList();
        if (rects.Count == 0) return null;
        double need = 0.3 + 0.4 * Math.Clamp(o.Sensitivity, 0, 1);
        // Each rectangle after the first stands on its own: an arm of the L that isn't outlined is dropped, not the whole roof.
        for (int i = rects.Count - 1; i >= 1; i--)
        {
            Support(v, rects, i, filled, out double w, out _);
            if (w < 0.2) rects.RemoveAt(i);
        }
        double support = Support(v, rects, -1, filled, out double weakest, out double drawn);
        double aligned = Aligned(v, rects, filled);
        double r0 = 0, g0 = 0, b0 = 0;
        foreach (var p in group) { r0 += p.R * p.Area; g0 += p.G * p.Area; b0 += p.B * p.Area; }
        double area = cells.Count;
        var roof = new SeenRoof { Rects = rects, AngleDeg = rects[0].AngleDeg, R = r0 / area, G = g0 / area, B = b0 / area, Support = support, Fill = fill, Aligned = aligned, Cells = cells };
        bool ok = support >= need && weakest >= 0.2 && (aligned < 0 || aligned >= 0.66);
        Log?.Invoke($"{kind} of {group.Count} at {box.Cx / sq:0.0},{box.Cy / sq:0.0}: {box.W / sq:0.0}x{box.H / sq:0.0} at {box.AngleDeg:0}° fill {fill:0.00} -> {rects.Count} rects [{string.Join("; ", rects.Select(r => $"{r.W / sq:0.0}x{r.H / sq:0.0} at {r.Cx / sq:0.0},{r.Cy / sq:0.0}"))}], support {support:0.00} (weakest side {weakest:0.00}) hsv {CityReader.Hsv(roof.R, roof.G, roof.B).H:0},{CityReader.Hsv(roof.R, roof.G, roof.B).S:0.00},{CityReader.Hsv(roof.R, roof.G, roof.B).V:0.00} tex {group.Sum(p => p.Tex * p.Area) / area:0.00} drawn {drawn:0.00} aligned {aligned:0.00} {(ok ? "KEPT" : "no")}");
        // Strokes running every way inside it: cobbles or gravel, however well its sides show.
        return ok ? roof : null;
    }

    /// <summary>
    /// How much the strokes inside some rectangles (away from their edges) run one way: each stroke pixel's
    /// direction is the one of eight it carries on in furthest, and the answer is the share of strokes in the
    /// two commonest directions (see <see cref="Agreement"/>; 0.5 when they run every way, as cobbles do;
    /// near 1 for rows of tiles). −1 when there are too few strokes to say (a plain roof).
    /// </summary>
    static double Aligned(MapVision v, List<MapVision.Box> rects, HashSet<int> own)
    {
        double sc = v.Scale;
        int imgW = v.ImageWidth, imgH = v.ImageHeight;
        float level = (float)v.LineLevel * 0.5f;
        var bins = new double[8];
        double total = 0;
        int pixels = 0;
        double inset = v.Square * 0.2;
        (int dx, int dy)[] dirs = [(1, 0), (2, 1), (1, 1), (1, 2), (0, 1), (-1, 2), (-1, 1), (-2, 1)];
        foreach (var r in rects)
        {
            var corners = r.Corners();
            int x0 = Math.Max(1, (int)(corners.Min(c => c.X) * sc)), x1 = Math.Min(imgW - 2, (int)(corners.Max(c => c.X) * sc));
            int y0 = Math.Max(1, (int)(corners.Min(c => c.Y) * sc)), y1 = Math.Min(imgH - 2, (int)(corners.Max(c => c.Y) * sc));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!r.Contains(x / sc, y / sc, -inset)) continue;
                    // Only the patch's own cells, a cell in from its edge: not the hedge the box overshot into.
                    int cx = (int)(x / sc), cy = (int)(y / sc);
                    if (!own.Contains(v.Index(cx, cy)) || !own.Contains(v.Index(Math.Max(0, cx - 1), cy)) || !own.Contains(v.Index(Math.Min(v.Width - 1, cx + 1), cy))
                        || !own.Contains(v.Index(cx, Math.Max(0, cy - 1))) || !own.Contains(v.Index(cx, Math.Min(v.Height - 1, cy + 1)))) continue;
                    pixels++;
                    float here = v.DarkLines[y * imgW + x];
                    // Along the grid's lines what's left of them would pass for rows of tiles: not counted.
                    if (here < level || v.OnGrid[y * imgW + x]) continue;
                    int best = -1;
                    double bestRun = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        var (dx, dy) = dirs[d];
                        double run = 0;
                        for (int k = 1; k <= 2; k++)
                        {
                            int px = x + dx * k, py = y + dy * k, qx = x - dx * k, qy = y - dy * k;
                            if (px >= 0 && py >= 0 && px < imgW && py < imgH) run += Math.Min(here, v.DarkLines[py * imgW + px]);
                            if (qx >= 0 && qy >= 0 && qx < imgW && qy < imgH) run += Math.Min(here, v.DarkLines[qy * imgW + qx]);
                        }
                        if (run > bestRun) { bestRun = run; best = d; }
                    }
                    if (best < 0) continue;
                    // A long straight stroke (a hip, the ridge, a row of tiles) says nothing about the texture either
                    // way; it's the short ones that tell ticks across tiles from the arcs round cobbles.
                    var (shortest, length) = Run(v, x, y, dirs[best], imgW, imgH, level);
                    if (shortest >= 6) continue;
                    // A speck on its own is grain, not a stroke: a stroke runs three pixels or more.
                    if (length < 3) continue;
                    // Each stroke pixel counts once, however dark.
                    bins[best]++;
                    total++;
                }
        }
        if (pixels < 0.3 * v.Square * v.Square * sc * sc || total < 0.015 * pixels) return -1;
        double square = 0;
        for (int i = 0; i < 4; i++)
        {
            double pair = bins[i] + bins[i + 4];
            for (int k = 0; k < 8; k++) if (Math.Abs(k - i) % 4 == 1 || Math.Abs(k - i) % 4 == 3) pair += bins[k] / 2;
            square = Math.Max(square, pair);
        }
        // Cobbles' arcs run between the two square directions as much as along them; rows of tiles with a slant of
        // shading over them have most of their strokes in two directions that aren't square, but still most in
        // the square pair and its neighbours. Only when the square pair holds a fair share do the two commonest count.
        return square / total < 0.6 ? square / total : Agreement(bins) / total;
    }

    /// <summary>
    /// Of strokes binned into eight directions (22.5° apart), how many run in the two commonest directions
    /// that are at least 45° apart (the rows of tiles and the ticks across them, or the shading drawn
    /// slantwise over the rows), counting those a step either side of each at half. Strokes running
    /// every way (cobbles) give about half the total; rows of tiles nearly all of it.
    /// </summary>
    internal static double Agreement(double[] bins)
    {
        double most = 0;
        for (int i = 0; i < 8; i++)
            for (int j = i + 2; j < 8; j++)
            {
                if (Math.Min(j - i, 8 - (j - i)) < 2) continue;
                double pair = bins[i] + bins[j];
                for (int k = 0; k < 8; k++)
                {
                    int di = Math.Min(Math.Abs(k - i), 8 - Math.Abs(k - i)), dj = Math.Min(Math.Abs(k - j), 8 - Math.Abs(k - j));
                    if (di == 1 || dj == 1) pair += bins[k] / 2;
                }
                most = Math.Max(most, pair);
            }
        return most;
    }

    /// <summary>How far a stroke at a pixel runs on along a direction: the shorter of its two ways (up to 6), and both together.</summary>
    static (int Shortest, int Length) Run(MapVision v, int x, int y, (int dx, int dy) dir, int imgW, int imgH, float level)
    {
        int shortest = 6, length = 0;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int k = 0;
            while (k < 6)
            {
                int px = x + dir.dx * (k + 1) * sign, py = y + dir.dy * (k + 1) * sign;
                if (px < 0 || py < 0 || px >= imgW || py >= imgH || v.DarkLines[py * imgW + px] < level) break;
                k++;
            }
            shortest = Math.Min(shortest, k);
            length += k;
        }
        return (shortest, length);
    }

    static bool Touches(List<MapVision.Box> rects, MapVision.Box r, double sq)
    {
        foreach (var a in rects)
        {
            // Close corners or an edge within a quarter square.
            foreach (var c in r.Corners())
                if (a.Contains(c.X, c.Y, sq * 0.3)) return true;
            foreach (var c in a.Corners())
                if (r.Contains(c.X, c.Y, sq * 0.3)) return true;
        }
        return false;
    }

    /// <summary>
    /// The rectangles a set of cells at a turn is made of: the drawn lines through it and the places it
    /// ends part it into tiles (in its own frame), and the biggest rectangle of filled tiles is taken out
    /// again and again, biggest first; each at least most of a square each way. A ridge or a hip parts the
    /// tiles but not the rectangles, so the two slopes of a gable come out as one roof, and a wing that
    /// ends at a drawn line comes out with its edge on that line.
    /// </summary>
    static List<MapVision.Box> Inner(MapVision v, List<int> cells, double angleDeg, double sq)
    {
        double a = -angleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var pts = cells.Select(i => { double x = i % v.Width + 0.5, y = i / v.Width + 0.5; return (U: x * c - y * s, V: x * s + y * c); }).ToList();
        double u0 = pts.Min(p => p.U), v0 = pts.Min(p => p.V), u1 = pts.Max(p => p.U), v1 = pts.Max(p => p.V);
        int w = (int)Math.Ceiling(u1 - u0) + 1, h = (int)Math.Ceiling(v1 - v0) + 1;
        if (w <= 0 || h <= 0) return [];
        var mask = new bool[w, h];
        var cut = new bool[w, h];
        for (int k = 0; k < pts.Count; k++)
        {
            int mx = (int)(pts[k].U - u0), my = (int)(pts[k].V - v0);
            mask[mx, my] = true;
            if (v.LongLine[cells[k]]) cut[mx, my] = true;
        }
        // Turning the cells leaves pinholes: a cell with filled cells on both sides is filled.
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                if (!mask[x, y] && ((mask[x - 1, y] && mask[x + 1, y]) || (mask[x, y - 1] && mask[x, y + 1]))) mask[x, y] = true;
        // Where the lines run and where the patch ends, across each axis: the tiles' edges.
        var alongU = new double[w + 1];
        var alongV = new double[h + 1];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (cut[x, y]) { alongU[x] += 1; alongV[y] += 1; continue; }
                if (!mask[x, y]) continue;
                if (x == 0 || !mask[x - 1, y]) alongU[x] += 1;
                if (x == w - 1 || !mask[x + 1, y]) alongU[x + 1] += 1;
                if (y == 0 || !mask[x, y - 1]) alongV[y] += 1;
                if (y == h - 1 || !mask[x, y + 1]) alongV[y + 1] += 1;
            }
        var us = Edges(alongU, w, sq);
        var vs = Edges(alongV, h, sq);
        // Where a side at each cell edge would be backed by a line or the patch's end (a cell either way).
        var sideU = new bool[w + 1, h];
        var sideV = new bool[w, h + 1];
        for (int y = 0; y < h; y++)
            for (int x = 0; x <= w; x++)
            {
                bool left = x > 0 && (mask[x - 1, y] || cut[x - 1, y]), right = x < w && (mask[x, y] || cut[x, y]);
                sideU[x, y] = (x > 0 && cut[x - 1, y]) || (x < w && cut[x, y]) || left != right;
            }
        for (int x = 0; x < w; x++)
            for (int y = 0; y <= h; y++)
            {
                bool above = y > 0 && (mask[x, y - 1] || cut[x, y - 1]), below = y < h && (mask[x, y] || cut[x, y]);
                sideV[x, y] = (y > 0 && cut[x, y - 1]) || (y < h && cut[x, y]) || above != below;
            }
        // Running totals of backed cells along each cell edge, so a side's share is a subtraction.
        var alongUSum = new int[w + 1, h + 1];
        for (int x = 0; x <= w; x++)
            for (int y = 0; y < h; y++)
                alongUSum[x, y + 1] = alongUSum[x, y] + (sideU[x, y] || (x > 0 && sideU[x - 1, y]) || (x < w && sideU[x + 1, y]) ? 1 : 0);
        var alongVSum = new int[h + 1, w + 1];
        for (int y = 0; y <= h; y++)
            for (int x = 0; x < w; x++)
                alongVSum[y, x + 1] = alongVSum[y, x] + (sideV[x, y] || (y > 0 && sideV[x, y - 1]) || (y < h && sideV[x, y + 1]) ? 1 : 0);
        double Backed(int x, int ya, int yb) => (double)(alongUSum[x, yb] - alongUSum[x, ya]) / Math.Max(1, yb - ya);
        double BackedV(int y, int xa, int xb) => (double)(alongVSum[y, xb] - alongVSum[y, xa]) / Math.Max(1, xb - xa);
        // Each tile filled or not: most of its cells in the patch (lines included).
        int nu = us.Count - 1, nv = vs.Count - 1;
        var tile = new bool[nu, nv];
        var fullIn = new int[nu, nv];
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                int full = 0, all = 0;
                for (int y = vs[j]; y < vs[j + 1]; y++)
                    for (int x = us[i]; x < us[i + 1]; x++) { all++; if (mask[x, y] || cut[x, y]) full++; }
                tile[i, j] = all > 0 && full >= 0.6 * all;
                fullIn[i, j] = full;
            }
        // Running totals, so any rectangle of tiles' filled count is four lookups.
        var sums = new int[nu + 1, nv + 1];
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
                sums[i + 1, j + 1] = fullIn[i, j] + sums[i, j + 1] + sums[i + 1, j] - sums[i, j];
        var res = new List<MapVision.Box>();
        for (int k = 0; k < 4; k++)
        {
            // The biggest rectangle of filled tiles, by area.
            double bestArea = 0;
            (int I0, int J0, int I1, int J1) best = default;
            for (int i0 = 0; i0 < nu; i0++)
                for (int i1 = i0 + 1; i1 <= nu; i1++)
                {
                    if (us[i1] - us[i0] < 0.7 * sq) continue;
                    int j0 = 0;
                    while (j0 < nv)
                    {
                        int j1 = j0;
                        while (j1 < nv && RowFilled(tile, i0, i1, j1)) j1++;
                        if (j1 > j0)
                        {
                            // The biggest, but a run of rows may be fuller without its last (or first) row: those are tried too.
                            for (int ja = j0; ja < j1; ja++)
                                for (int jb = j1; jb > ja; jb--)
                                {
                                    double area = (us[i1] - us[i0]) * (double)(vs[jb] - vs[ja]);
                                    if (vs[jb] - vs[ja] < 0.7 * sq || area <= bestArea) continue;
                                    int full = sums[i1, jb] - sums[i0, jb] - sums[i1, ja] + sums[i0, ja];
                                    // Most of it the patch: not a run of half-filled tiles along a hip.
                                    if (full < 0.85 * area) continue;
                                    // Big, but with its sides on drawn lines or the patch's ends: a wing to the valley, not a slice across it.
                                    double backed = (Backed(us[i0], vs[ja], vs[jb]) + Backed(us[i1], vs[ja], vs[jb]) + BackedV(vs[ja], us[i0], us[i1]) + BackedV(vs[jb], us[i0], us[i1])) / 4;
                                    double score = area * (0.3 + 0.7 * backed);
                                    if (score > bestArea) { bestArea = score; best = (i0, ja, i1, jb); }
                                }
                            j0 = j1;
                        }
                        j0++;
                    }
                }
            if (bestArea <= 0) break;
            for (int i = best.I0; i < best.I1; i++) for (int j = best.J0; j < best.J1; j++) tile[i, j] = false;
            double rw = us[best.I1] - us[best.I0], rh = vs[best.J1] - vs[best.J0];
            double cu = u0 + (us[best.I0] + us[best.I1]) / 2.0, cv = v0 + (vs[best.J0] + vs[best.J1]) / 2.0;
            double ang = angleDeg * Math.PI / 180;
            double cx = cu * Math.Cos(ang) - cv * Math.Sin(ang), cy = cu * Math.Sin(ang) + cv * Math.Cos(ang);
            res.Add(new MapVision.Box(cx, cy, angleDeg, rw, rh));
        }
        return res;
    }

    static bool RowFilled(bool[,] tile, int i0, int i1, int j)
    {
        for (int i = i0; i < i1; i++) if (!tile[i, j]) return false;
        return true;
    }

    /// <summary>
    /// The tile edges along one axis: where the count of line and patch-end cells peaks at a run of at
    /// least a third of a square, peaks closer than a quarter square taken together, plus both ends.
    /// </summary>
    static List<int> Edges(double[] count, int n, double sq)
    {
        var peaks = new List<(double At, double Weight)>();
        double least = sq / 3;
        for (int i = 0; i <= n; i++)
        {
            if (count[i] < least) continue;
            if (peaks.Count > 0 && i - peaks[^1].At < sq / 4)
            {
                var (at, wgt) = peaks[^1];
                peaks[^1] = ((at * wgt + i * count[i]) / (wgt + count[i]), wgt + count[i]);
            }
            else peaks.Add((i, count[i]));
        }
        var res = new List<int> { 0 };
        foreach (var (at, _) in peaks)
        {
            int i = (int)Math.Round(at);
            if (i - res[^1] >= 2 && n - i >= 2) res.Add(i);
        }
        res.Add(n);
        return res;
    }

    /// <summary>The biggest rectangle of filled cells in a grid (x0, y0 inclusive to x1, y1 exclusive), or null when there's none.</summary>
    internal static (int X0, int Y0, int X1, int Y1)? LargestRect(bool[,] mask, int w, int h)
    {
        var height = new int[w];
        int best = 0;
        (int, int, int, int)? at = null;
        var stack = new Stack<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) height[x] = mask[x, y] ? height[x] + 1 : 0;
            stack.Clear();
            for (int x = 0; x <= w; x++)
            {
                int hx = x == w ? 0 : height[x];
                while (stack.Count > 0 && height[stack.Peek()] >= hx)
                {
                    int top = stack.Pop();
                    int left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                    int area = height[top] * (x - left);
                    if (area > best) { best = area; at = (left, y - height[top] + 1, x, y + 1); }
                }
                stack.Push(x);
            }
        }
        return best == 0 ? null : at;
    }

    /// <summary>
    /// Each side of a rectangle slid in or out (up to a third of a square) to where the drawn line is
    /// strongest along it; a side that finds no line stays where the patch put it.
    /// </summary>
    static MapVision.Box Slide(MapVision v, MapVision.Box box, HashSet<int> cells)
    {
        double sq = v.Square, reach = sq * 0.35;
        double a = box.AngleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        // Half sizes along u (W) and v (H), each side moved on its own.
        double[] half = [box.W / 2, box.H / 2, box.W / 2, box.H / 2]; // +u, +v, -u, -v
        for (int side = 0; side < 4; side++)
        {
            bool alongU = side % 2 == 0;
            double sign = side < 2 ? 1 : -1;
            double best = -1, bestAt = half[side];
            for (double d = -reach; d <= reach; d += 1)
            {
                double at = half[side] + d;
                if (at < 0.3 * sq) continue;
                double score = LineAlong(v, box, c, s, alongU, sign * at, alongU ? half[1] + half[3] : half[0] + half[2], alongU ? (half[1] - half[3]) / 2 : (half[0] - half[2]) / 2, out double step, cells, out double ends, null, out _);
                // A line the patch runs on past (a row of shingles) isn't its edge; nearer the patch's own edge is better when it's a toss-up.
                score = Math.Max(score, step * 0.9) * (ends >= 0.5 ? 1 : 0.3) - Math.Abs(d) / reach * 0.08;
                if (score > best) { best = score; bestAt = at; }
            }
            half[side] = bestAt;
        }
        double w = half[0] + half[2], h = half[1] + half[3];
        double du = (half[0] - half[2]) / 2, dv = (half[1] - half[3]) / 2;
        return new MapVision.Box(box.Cx + du * c - dv * s, box.Cy + du * s + dv * c, box.AngleDeg, w, h);
    }

    /// <summary>
    /// How much of a line along one side shows: samples every pixel along the side (at distance
    /// <paramref name="at"/> from the middle across it, over its length), each the strongest line
    /// within two picture pixels either side; the share of samples at the outline's strength.
    /// </summary>
    static double LineAlong(MapVision v, MapVision.Box box, double c, double s, bool alongU, double at, double length, double offset, out double colourStep) => LineAlong(v, box, c, s, alongU, at, length, offset, out colourStep, null, out _, null, out _);

    /// <summary>
    /// As above, and (given the patch's own cells) the share of the side where the patch ends there: its own
    /// cells inside, not outside. Where the side runs along another of the roof's rectangles it isn't an
    /// outside edge: those samples are left out, and <paramref name="outside"/> says what share was kept.
    /// </summary>
    static double LineAlong(MapVision v, MapVision.Box box, double c, double s, bool alongU, double at, double length, double offset, out double colourStep, HashSet<int>? own, out double ends, List<MapVision.Box>? others, out double outside)
    {
        int w = v.Width;
        double sc = v.Scale;
        int imgW = v.ImageWidth;
        int n = Math.Max(2, (int)(length * sc));
        int shown = 0, stepped = 0, count = 0, ended = 0;
        double level = v.LineLevel;
        for (int i = 0; i < n; i++)
        {
            double t = -length / 2 + length * (i + 0.5) / n + offset;
            // The point in the box's frame, then the picture's.
            double u = alongU ? at : t, vv = alongU ? t : at;
            double x = box.Cx + u * c - vv * s, y = box.Cy + u * s + vv * c;
            // Inwards (towards the middle) and outwards, across the side.
            double nx = alongU ? c : -s, ny = alongU ? s : c;
            if (at < 0) { nx = -nx; ny = -ny; }
            if (others is not null && others.Any(o => o != box && o.Contains(x + nx * v.Square * 0.3, y + ny * v.Square * 0.3, 0))) continue;
            count++;
            float best = 0;
            bool onGrid = false;
            for (double d = -2; d <= 2; d += 1)
            {
                int px = (int)Math.Round((x + nx * d / sc) * sc), py = (int)Math.Round((y + ny * d / sc) * sc);
                if (px < 0 || py < 0 || px >= imgW || py * imgW + px >= v.Lineness.Length) continue;
                if (v.Lineness[py * imgW + px] > best) { best = v.Lineness[py * imgW + px]; onGrid = v.OnGrid[py * imgW + px]; }
            }
            // A change of colour across the side, three cells in and three out.
            int ix = (int)Math.Round(x - nx * 3), iy = (int)Math.Round(y - ny * 3), ox = (int)Math.Round(x + nx * 3), oy = (int)Math.Round(y + ny * 3);
            bool step = ix >= 0 && iy >= 0 && ix < w && iy < v.Height && ox >= 0 && oy >= 0 && ox < w && oy < v.Height && v.Steps(v.Index(ix, iy), v.Index(ox, oy));
            if (step) stepped++;
            // A line along one of the grid's own lines is only an edge where the colour changes across it too: the grid runs on over the road.
            if (best >= level * 0.8 && (!onGrid || step)) shown++;
            if (own is not null)
            {
                int ox2 = (int)Math.Round(x + nx * 2.5), oy2 = (int)Math.Round(y + ny * 2.5);
                if (ox2 < 0 || oy2 < 0 || ox2 >= w || oy2 >= v.Height || !own.Contains(v.Index(ox2, oy2))) ended++;
            }
        }
        ends = count == 0 ? 0 : (double)ended / count;
        colourStep = count == 0 ? 0 : (double)stepped / count;
        outside = (double)count / n;
        return count == 0 ? 0 : (double)shown / count;
    }

    /// <summary>How much of a roof's outline is drawn (a line, or a change of colour): the mean over its sides, and the weakest side.</summary>
    static double Support(MapVision v, List<MapVision.Box> rects, int only, HashSet<int> own, out double weakest, out double drawn)
    {
        double sum = 0, lines = 0;
        int n = 0;
        weakest = 1;
        double margin = v.Square * 0.2;
        for (int r = 0; r < rects.Count; r++)
        {
            if (only >= 0 && r != only) continue;
            var box = rects[r];
            double a = box.AngleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            for (int side = 0; side < 4; side++)
            {
                bool alongU = side % 2 == 0;
                double at = (side < 2 ? 1 : -1) * (alongU ? box.W / 2 : box.H / 2);
                double length = alongU ? box.H : box.W;
                // The part of the side along another of the roof's rectangles isn't an outside edge; a side wholly inside is no edge at all.
                double line = LineAlong(v, box, c, s, alongU, at, length, 0, out double step, own, out double ends, rects, out double outside);
                if (outside < 0.15) continue;
                // A side the patch runs on past is only the roof's edge when a line is drawn there (the wing of an L meeting the rest).
                double score = Math.Max(line, step * 0.9) * (ends >= 0.5 ? 1 : 0.3 + 0.5 * line);
                // A side along the picture's edge: the building goes on off the map.
                double ex = box.Cx + (alongU ? at * c : -at * s), ey = box.Cy + (alongU ? at * s : at * c);
                if (ex < margin || ey < margin || ex > v.Width - margin || ey > v.Height - margin) { score = Math.Max(score, 0.9); line = Math.Max(line, 0.9); }
                lines += line;
                if (box.Area >= 2 * v.Square * v.Square) Log?.Invoke($"   side {side} of {box.W / v.Square:0.0}x{box.H / v.Square:0.0}: line {line:0.00} step {step:0.00} ends {ends:0.00}");
                sum += score;
                n++;
                weakest = Math.Min(weakest, score);
            }
        }
        drawn = n == 0 ? 0 : lines / n;
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>
    /// Whether a rectangle runs off the picture at both ends (of its length or its width): then every corner
    /// is at the edge, which one in a corner of the picture (two adjacent sides off) never has.
    /// </summary>
    static bool OffBothEnds(MapVision v, MapVision.Box r)
    {
        double margin = v.Square * 0.2;
        return r.Corners().All(p => p.X < margin || p.Y < margin || p.X > v.Width - margin || p.Y > v.Height - margin);
    }

    /// <summary>How much two roofs' rectangles overlap (working pixels squared), roughly: by sampling.</summary>
    static double Overlap(SeenRoof a, SeenRoof b, double sq)
    {
        double step = Math.Max(1, sq / 6), area = 0;
        foreach (var r in a.Rects)
        {
            var corners = r.Corners();
            double x0 = corners.Min(c => c.X), x1 = corners.Max(c => c.X), y0 = corners.Min(c => c.Y), y1 = corners.Max(c => c.Y);
            for (double y = y0; y <= y1; y += step)
                for (double x = x0; x <= x1; x += step)
                    if (r.Contains(x, y) && b.Rects.Any(q => q.Contains(x, y))) area += step * step;
        }
        return area;
    }
}
