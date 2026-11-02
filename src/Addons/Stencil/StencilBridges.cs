using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Stencil;

/// <summary>Which way the bridges run.</summary>
public enum BridgeDirection
{
    /// <summary>Each bridge takes the shortest way across the letter (straight across the stroke).</summary>
    Shortest,
    /// <summary>Straight up and down, like a classic stencil lettering.</summary>
    UpDown,
    /// <summary>Straight across (left–right).</summary>
    Across,
    /// <summary>At a set angle.</summary>
    Angle,
}

/// <summary>How the bridges are made.</summary>
public sealed record StencilOptions
{
    /// <summary>How wide each bridge is, as drawn (mm).</summary>
    public double WidthMm { get; init; } = 1;
    /// <summary>How many bridges each middle gets (1 holds it; 2 holds it from both sides so it can't twist).</summary>
    public int PerMiddle { get; init; } = 2;
    public BridgeDirection Direction { get; init; } = BridgeDirection.Shortest;
    /// <summary>For <see cref="BridgeDirection.Angle"/>: degrees from left–right, clockwise on screen.</summary>
    public double AngleDeg { get; init; }
}

/// <summary>One bridge: a strip of the sheet <see cref="WidthMm"/> wide, kept from <see cref="A"/> to <see cref="B"/> (world mm).</summary>
public sealed record Bridge(Vec2 A, Vec2 B, double WidthMm)
{
    public double Length => A.Dist(B);
    public Vec2 Middle => Vec2.Lerp(A, B, 0.5);

    /// <summary>The strip as a closed outline.</summary>
    public PathGeometry Outline
    {
        get
        {
            var d = B - A;
            double len = d.Length;
            var n = len > 1e-9 ? new Vec2(-d.Y / len, d.X / len) * (WidthMm / 2) : new Vec2(0, WidthMm / 2);
            return PathGeometry.Polyline([A + n, B + n, B - n, A - n], true);
        }
    }

    /// <summary>How far <paramref name="p"/> is from the bridge's centre line.</summary>
    public double DistanceTo(Vec2 p) => Geom.SegmentDistance(p, A, B);

    public Bridge Transformed(Affine t) => new(t.Apply(A), t.Apply(B), WidthMm * t.MeanScale);
}

/// <summary>A piece of the sheet that would fall out: the middle of an O, A, R, B, a letter inside a letter…</summary>
public sealed class Middle
{
    public required int Index { get; init; }
    /// <summary>Its outline (world mm).</summary>
    public required IReadOnlyList<Vec2> Outline { get; init; }
    public required double AreaMm2 { get; init; }
    public Rect2 Bounds => Rect2.Bounds(Outline);
}

/// <summary>What the bridges hold: which middles are joined to the sheet and which would still fall out.</summary>
public sealed record BridgeCheck(int Middles, IReadOnlyList<Middle> Loose, IReadOnlyList<Bridge> Long);

/// <summary>
/// Stencil bridges. Cut out of a sheet, every piece the cuts go all the way round falls out: the
/// letters (wanted) but also the middles of O, A, R, B, D… (not wanted). This finds those middles
/// and keeps short strips of the sheet (bridges) across the letter, from each middle to the rest of
/// the stencil (or to a middle that is itself held), straight across the stroke where it is
/// thinnest, two per middle by default and spread apart so the middle can't twist.
/// </summary>
/// <remarks>
/// The letters are everything inside an odd number of the closed outlines (even-odd), like the
/// program fills shapes. With an edge (one outline round the whole stencil) the stencil is what is
/// inside it; without, the sheet goes on all round.
/// </remarks>
public sealed class StencilSheet
{
    const double Scale = 1e4; // Clipper grid: 0.1 µm
    const double Tol = 0.01;  // curves followed to 0.01 mm
    /// <summary>Pieces smaller than this (mm²) are slivers from overlapping outlines, not middles.</summary>
    public const double TinyMm2 = 0.05;
    /// <summary>A bridge longer than this many times its width is floppy.</summary>
    public const double LongRatio = 8;

    readonly List<(Vec2 A, Vec2 B, int Piece)> _segs = [];
    readonly List<int>[] _grid;
    readonly int _gw, _gh;
    readonly double _cell;
    readonly Rect2 _gridBox;
    readonly List<Vec2> _region; // the stencil's outer edge (the edge, or a box round everything)
    readonly double _diag;
    readonly int _mainPiece;
    readonly Dictionary<int, Middle> _middleOf = [];
    readonly List<int> _tinyPieces = [];
    int[] _stamp = [];
    int _stampNo;

    /// <summary>The letters: the area cut out (even-odd).</summary>
    public PathGeometry Letters { get; }
    /// <summary>The middles that would fall out.</summary>
    public IReadOnlyList<Middle> Middles { get; }
    /// <summary>Everything, letters and edge (world mm).</summary>
    public Rect2 Bounds { get; }
    /// <summary>The edge round the stencil, when there is one.</summary>
    public PathGeometry? Edge { get; }

    public StencilSheet(IEnumerable<PathGeometry> letters, PathGeometry? edge = null)
    {
        var letterPaths = new Paths64();
        foreach (var g in letters)
            foreach (var f in g.Flatten(Tol))
                if (f.Closed && f.Points.Count >= 3) letterPaths.Add(ToPath(f.Points));
        var lettersArea = Clipper.Union(letterPaths, FillRule.EvenOdd);
        Letters = ToGeometry(lettersArea);
        Edge = edge;

        Paths64 region;
        var lb = Letters.Bounds(Tol);
        var edgeLoop = edge?.Flatten(Tol).Where(f => f.Closed && f.Points.Count >= 3).MaxBy(f => Math.Abs(Geom.SignedArea(f.Points)));
        if (edgeLoop is not null)
        {
            region = Clipper.Union(new Paths64 { ToPath(edgeLoop.Points) }, FillRule.EvenOdd);
            _region = edgeLoop.Points;
            Bounds = lb.Union(Rect2.Bounds(edgeLoop.Points));
        }
        else
        {
            var box = lb.IsEmpty ? new Rect2(0, 0, 1, 1) : lb.Inflate(Math.Max(5, Math.Max(lb.Width, lb.Height) * 0.1));
            _region = [new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY)];
            region = new Paths64 { ToPath(_region) };
            Bounds = lb.IsEmpty ? box : lb;
        }
        var all = Bounds.Union(Rect2.Bounds(_region));
        _diag = Math.Max(1, Math.Sqrt(all.Width * all.Width + all.Height * all.Height));

        // The sheet left after cutting, as separate pieces: each outer outline with its holes is one piece.
        var tree = new PolyTree64();
        var c = new Clipper64();
        c.AddSubject(region);
        c.AddClip(lettersArea);
        c.Execute(ClipType.Difference, FillRule.NonZero, tree);
        var pieces = new List<(List<Vec2> Outer, double Area)>();
        void Walk(PolyPath64 node)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var outer = node[i];
                if (outer.Polygon is null) continue;
                int piece = pieces.Count;
                pieces.Add(default); // its place, before any pieces inside its holes
                var pts = ToPoints(outer.Polygon);
                double area = Math.Abs(Geom.SignedArea(pts));
                AddLoop(pts, piece);
                for (int j = 0; j < outer.Count; j++)
                {
                    var hole = outer[j];
                    if (hole.Polygon is null) continue;
                    var hp = ToPoints(hole.Polygon);
                    area -= Math.Abs(Geom.SignedArea(hp));
                    AddLoop(hp, piece);
                    Walk(hole);
                }
                pieces[piece] = (pts, area);
            }
        }
        Walk(tree);
        _mainPiece = pieces.Count == 0 ? -1 : Enumerable.Range(0, pieces.Count).MaxBy(i => pieces[i].Area);
        var middles = new List<Middle>();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i == _mainPiece) continue;
            if (pieces[i].Area < TinyMm2) { _tinyPieces.Add(i); continue; }
            var m = new Middle { Index = i, Outline = pieces[i].Outer, AreaMm2 = pieces[i].Area };
            middles.Add(m);
            _middleOf[i] = m;
        }
        Middles = middles;

        // A grid over the outlines, so rays only look at the outlines near them.
        _gridBox = all.Inflate(1);
        _cell = Math.Clamp(Math.Max(_gridBox.Width, _gridBox.Height) / 160, 0.25, 10);
        _gw = Math.Max(1, (int)Math.Ceiling(_gridBox.Width / _cell));
        _gh = Math.Max(1, (int)Math.Ceiling(_gridBox.Height / _cell));
        _grid = new List<int>[_gw * _gh];
        for (int s = 0; s < _segs.Count; s++)
        {
            var (a, b, _) = _segs[s];
            int x0 = CellX(Math.Min(a.X, b.X)), x1 = CellX(Math.Max(a.X, b.X)), y0 = CellY(Math.Min(a.Y, b.Y)), y1 = CellY(Math.Max(a.Y, b.Y));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    (_grid[y * _gw + x] ??= []).Add(s);
        }
        _stamp = new int[_segs.Count];
    }

    /// <summary>Slivers (from overlapping outlines) too small to be worth keeping; they fall out.</summary>
    public int TinyPieces => _tinyPieces.Count;

    void AddLoop(List<Vec2> pts, int piece)
    {
        for (int i = 0; i < pts.Count; i++) _segs.Add((pts[i], pts[(i + 1) % pts.Count], piece));
    }

    int CellX(double x) => Math.Clamp((int)((x - _gridBox.MinX) / _cell), 0, _gw - 1);
    int CellY(double y) => Math.Clamp((int)((y - _gridBox.MinY) / _cell), 0, _gh - 1);

    // ---- rays ---------------------------------------------------------------------------

    /// <summary>The first outline a ray from <paramref name="s"/> along unit <paramref name="d"/> meets past <paramref name="minT"/>: how far, and whose.</summary>
    (double T, int Piece) Cast(Vec2 s, Vec2 d, double minT = 1e-6, double maxT = double.NaN)
    {
        if (double.IsNaN(maxT)) maxT = _diag * 2;
        double best = double.PositiveInfinity;
        int bestPiece = -1;
        _stampNo++;
        if (_stampNo == int.MaxValue) { Array.Clear(_stamp); _stampNo = 1; }
        // Walk the grid cells the ray passes through (Amanatides & Woo).
        int cx = (int)Math.Floor((s.X - _gridBox.MinX) / _cell), cy = (int)Math.Floor((s.Y - _gridBox.MinY) / _cell);
        int stepX = d.X > 0 ? 1 : d.X < 0 ? -1 : 0, stepY = d.Y > 0 ? 1 : d.Y < 0 ? -1 : 0;
        double tMaxX = stepX == 0 ? double.PositiveInfinity : ((_gridBox.MinX + (cx + (stepX > 0 ? 1 : 0)) * _cell) - s.X) / d.X;
        double tMaxY = stepY == 0 ? double.PositiveInfinity : ((_gridBox.MinY + (cy + (stepY > 0 ? 1 : 0)) * _cell) - s.Y) / d.Y;
        double tDX = stepX == 0 ? double.PositiveInfinity : _cell / Math.Abs(d.X), tDY = stepY == 0 ? double.PositiveInfinity : _cell / Math.Abs(d.Y);
        double tCell = 0;
        while (tCell <= maxT)
        {
            if (cx >= 0 && cy >= 0 && cx < _gw && cy < _gh && _grid[cy * _gw + cx] is { } list)
                foreach (int si in list)
                {
                    if (_stamp[si] == _stampNo) continue;
                    _stamp[si] = _stampNo;
                    var (a, b, piece) = _segs[si];
                    var e = b - a;
                    double den = d.Cross(e);
                    if (Math.Abs(den) < 1e-15) continue;
                    var w = a - s;
                    double t = w.Cross(e) / den, u = w.Cross(d) / den;
                    if (u < 0 || u > 1 || t <= minT || t > maxT || t >= best) continue;
                    best = t;
                    bestPiece = piece;
                }
            double next = Math.Min(tMaxX, tMaxY);
            if (best <= next) break;
            if (tMaxX < tMaxY) { cx += stepX; tCell = tMaxX; tMaxX += tDX; }
            else { cy += stepY; tCell = tMaxY; tMaxY += tDY; }
            if ((stepX > 0 && cx >= _gw) || (stepX < 0 && cx < 0) || (stepY > 0 && cy >= _gh) || (stepY < 0 && cy < 0)) break;
        }
        return (best, bestPiece);
    }

    /// <summary>Is <paramref name="p"/> in a letter (in the stencil and not on the sheet)?</summary>
    public bool InLetter(Vec2 p)
    {
        if (!Geom.Inside(_region, p)) return false;
        // Count crossings of a ray to the right: odd = on the sheet.
        int n = 0;
        double t = 0;
        var d = new Vec2(1, 0.0001234).Normalised();
        for (int guard = 0; guard < 100000; guard++)
        {
            var (hit, _) = Cast(p, d, t + 1e-9);
            if (double.IsInfinity(hit)) break;
            n++;
            t = hit;
        }
        return n % 2 == 0;
    }

    /// <summary>
    /// A bridge through <paramref name="c"/> (in a letter) along <paramref name="d"/>, if the strip
    /// crosses the letter cleanly: every line along it, across its width, leaves the letter into the
    /// same two pieces of sheet. Its length is from where the first line leaves the sheet to where
    /// the last one meets it again.
    /// </summary>
    Candidate? Through(Vec2 c, Vec2 d, double width, int from = -1)
    {
        var perp = new Vec2(-d.Y, d.X);
        double h = width / 2;
        int back = int.MinValue, fwd = int.MinValue;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity, shortest = double.PositiveInfinity;
        double gapBack = double.PositiveInfinity, gapFwd = double.PositiveInfinity;
        foreach (double k in (ReadOnlySpan<double>)[0, -1, 1, -0.5, 0.5])
        {
            var s = c + perp * (h * k);
            var (tf, pf) = Cast(s, d);
            var (tb, pb) = Cast(s, -d);
            if (pf < 0 || pb < 0) return null;
            if (back == int.MinValue) { back = pb; fwd = pf; }
            else if (pb != back || pf != fwd) return null;
            lo = Math.Min(lo, -tb);
            hi = Math.Max(hi, tf);
            shortest = Math.Min(shortest, tf + tb);
            if (k == 0)
            {
                gapFwd = Cast(s, d, tf + 1e-6).T - tf;
                gapBack = Cast(s, -d, tb + 1e-6).T - tb;
            }
        }
        if (from >= 0 && back != from)
        {
            if (fwd != from) return null;
            (back, fwd) = (fwd, back);
            (lo, hi) = (-hi, -lo);
            (gapBack, gapFwd) = (gapFwd, gapBack);
            d = -d;
        }
        // Just into the sheet at both ends, so nothing thin is left at the joins (never as far as the next letter).
        double extB = Math.Min(Math.Max(0.05, width * 0.1), gapBack / 2), extF = Math.Min(Math.Max(0.05, width * 0.1), gapFwd / 2);
        var a = c + d * (lo - extB);
        var b = c + d * (hi + extF);
        double len = hi - lo;
        // Shorter is better; a strip that meets the letter's edges at a slant is longer than it needs to be.
        return new Candidate(new Bridge(a, b, width), back, fwd, len, len + (len - shortest));
    }

    sealed record Candidate(Bridge Bridge, int From, int To, double Crossing, double Score);

    IEnumerable<Vec2> Directions(Vec2 normal, BridgeDirection dir, double angleDeg)
    {
        switch (dir)
        {
            case BridgeDirection.Shortest:
                foreach (double deg in ShortestTurns)
                    yield return Rotate(normal, deg);
                break;
            default:
                var u = Axis(dir, angleDeg);
                if (u.Dot(normal) > 0.1) yield return u;
                if (-u.Dot(normal) > 0.1) yield return -u;
                break;
        }
    }

    static readonly double[] ShortestTurns = [0, -12, 12, -25, 25, -40, 40];

    static Vec2 Axis(StencilOptions o) => Axis(o.Direction, o.AngleDeg);

    static Vec2 Axis(BridgeDirection dir, double angleDeg)
    {
        double r = (dir == BridgeDirection.UpDown ? 90 : dir == BridgeDirection.Across ? 0 : angleDeg) * Math.PI / 180;
        return new Vec2(Math.Cos(r), Math.Sin(r));
    }

    static Vec2 Rotate(Vec2 v, double deg)
    {
        double r = deg * Math.PI / 180, cs = Math.Cos(r), sn = Math.Sin(r);
        return new Vec2(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs);
    }

    /// <summary>Every way out of a middle: bridges from points all round its outline to whatever sheet is across the letter.</summary>
    List<Candidate> CandidatesFor(Middle m, StencilOptions o)
    {
        var loop = m.Outline;
        double perimeter = 0;
        for (int i = 0; i < loop.Count; i++) perimeter += loop[i].Dist(loop[(i + 1) % loop.Count]);
        double step = Math.Clamp(perimeter / 160, 0.1, 1.5);
        var list = new List<Candidate>();
        double along = 0, nextAt = step / 2;
        for (int i = 0; i < loop.Count; i++)
        {
            Vec2 a = loop[i], b = loop[(i + 1) % loop.Count];
            double len = a.Dist(b);
            if (len < 1e-9) continue;
            var t = (b - a) / len;
            var n = new Vec2(-t.Y, t.X);
            // Point the normal out of the middle (into the letter).
            var mid = Vec2.Lerp(a, b, 0.5);
            if (Geom.Inside(loop, mid + n * 1e-3)) n = -n;
            while (nextAt <= along + len)
            {
                var p = a + t * (nextAt - along);
                nextAt += step;
                foreach (var d in Directions(n, o.Direction, o.AngleDeg))
                {
                    var (t1, piece) = Cast(p + d * 1e-4, d);
                    if (piece < 0 || piece == m.Index || double.IsInfinity(t1)) continue;
                    if (Through(p + d * (1e-4 + t1 / 2), d, o.WidthMm, m.Index) is { } cand && cand.From == m.Index && cand.To != m.Index)
                        list.Add(cand);
                }
            }
            along += len;
        }
        return list;
    }

    /// <summary>
    /// The bridges that hold every middle: each middle in turn (shortest first) is joined to sheet
    /// that is already held, then gets its other bridges, spread round it.
    /// </summary>
    public List<Bridge> AutoBridges(StencilOptions o, CancellationToken cancel = default)
    {
        int per = Math.Clamp(o.PerMiddle, 1, 8);
        var cands = new Dictionary<int, List<Candidate>>();
        foreach (var m in Middles)
        {
            cancel.ThrowIfCancellationRequested();
            cands[m.Index] = [.. CandidatesFor(m, o).OrderBy(c => c.Score)];
        }
        var held = new HashSet<int> { _mainPiece };
        var chosen = new Dictionary<int, List<Candidate>>();
        while (true)
        {
            Candidate? best = null;
            foreach (var m in Middles)
            {
                if (held.Contains(m.Index)) continue;
                foreach (var c in cands[m.Index])
                    if (held.Contains(c.To)) { if (best is null || c.Score < best.Score) best = c; break; }
            }
            if (best is null) break;
            held.Add(best.From);
            chosen[best.From] = [best];
        }
        foreach (var (mid, list) in chosen)
        {
            var m = _middleOf[mid];
            var mb = m.Bounds;
            double size = Math.Sqrt(mb.Width * mb.Width + mb.Height * mb.Height);
            double bestScore = list[0].Score;
            while (list.Count < per)
            {
                Candidate? pick = null;
                foreach (double factor in (ReadOnlySpan<double>)[1.6, 3])
                {
                    double limit = bestScore * factor + o.WidthMm * 0.5, far = -1;
                    foreach (var c in cands[mid])
                    {
                        if (c.Score > limit) break;
                        if (!held.Contains(c.To)) continue; // not to a sliver or a middle nothing holds
                        double dmin = list.Min(x => Math.Min(x.Bridge.A.Dist(c.Bridge.A), x.Bridge.Middle.Dist(c.Bridge.Middle)));
                        if (dmin > far) { far = dmin; pick = c; }
                    }
                    if (pick is not null && far >= Math.Max(o.WidthMm * 2.5, size * 0.3)) break;
                    pick = null;
                }
                if (pick is null) break;
                list.Add(pick);
            }
        }
        return [.. chosen.Values.SelectMany(l => l).Select(c => c.Bridge)];
    }

    /// <summary>
    /// A bridge clicked by hand at <paramref name="p"/> (in a letter): across the letter there, the
    /// way <paramref name="o"/> says (the shortest way when it says Shortest). Null when it can't
    /// cross cleanly there.
    /// </summary>
    public Bridge? BridgeAt(Vec2 p, StencilOptions o)
    {
        if (!InLetter(p)) return null;
        IEnumerable<Vec2> dirs = o.Direction == BridgeDirection.Shortest
            ? Enumerable.Range(0, 36).Select(i => new Vec2(Math.Cos(i * Math.PI / 36), Math.Sin(i * Math.PI / 36)))
            : [Axis(o)];
        Candidate? best = null;
        foreach (var d in dirs)
            if (Through(p, d, o.WidthMm) is { } c)
            {
                // Joining two different pieces beats running along a letter back to the same one.
                double score = c.Score + (c.From == c.To ? 1e6 : 0);
                if (best is null || score < best.Score + (best.From == best.To ? 1e6 : 0)) best = c;
            }
        return best?.Bridge;
    }

    /// <summary>Which pieces of sheet a bridge joins (looked along its middle both ways).</summary>
    (int, int) Joins(Bridge b)
    {
        var d = b.B - b.A;
        if (d.Length < 1e-9) return (-1, -1);
        d /= d.Length;
        var m = b.Middle;
        if (!InLetter(m)) return (-1, -1);
        return (Cast(m, -d).Piece, Cast(m, d).Piece);
    }

    /// <summary>Which middles these bridges leave loose (they'd still fall out), and which bridges are long and floppy.</summary>
    public BridgeCheck Check(IReadOnlyList<Bridge> bridges)
    {
        // Union-find over the pieces of sheet.
        var parent = new Dictionary<int, int>();
        int Find(int x) { while (parent.TryGetValue(x, out int p) && p != x) x = p; return x; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        foreach (var b in bridges)
        {
            var (x, y) = Joins(b);
            if (x >= 0 && y >= 0) Join(x, y);
        }
        int main = Find(_mainPiece);
        var loose = Middles.Where(m => Find(m.Index) != main).ToList();
        var floppy = bridges.Where(b => b.Length > LongRatio * b.WidthMm).ToList();
        return new BridgeCheck(Middles.Count, loose, floppy);
    }

    /// <summary>The letters <paramref name="letters"/> (closed outlines, even-odd) with the bridges kept out of them.</summary>
    public static PathGeometry Cut(PathGeometry letters, IEnumerable<Bridge> bridges)
    {
        var strips = bridges.Select(b => b.Outline).ToList();
        if (strips.Count == 0) return letters;
        var cut = ShapeGeometry.Boolean(letters, ShapeGeometry.Union(strips), BoolOp.Subtract);
        // Lines that aren't outlines (engraved strokes) aren't touched.
        var open = letters.Subpaths.Where(s => !s.Closed).ToList();
        return open.Count == 0 ? cut : new PathGeometry([.. cut.Subpaths, .. open]);
    }

    // ---- Clipper -------------------------------------------------------------------------

    static Path64 ToPath(IReadOnlyList<Vec2> pts)
    {
        var p = new Path64(pts.Count);
        foreach (var v in pts) p.Add(new Point64(Math.Round(v.X * Scale), Math.Round(v.Y * Scale)));
        return p;
    }

    static List<Vec2> ToPoints(Path64 p) => [.. p.Select(q => new Vec2(q.X / Scale, q.Y / Scale))];

    static PathGeometry ToGeometry(Paths64 paths) =>
        new(paths.Where(p => p.Count >= 3).Select(p => PathGeometry.Polyline(ToPoints(p), true).Subpaths[0]));
}

static class VecExt
{
    public static Vec2 Normalised(this Vec2 v) => v.Length > 1e-15 ? v / v.Length : new Vec2(1, 0);
}
