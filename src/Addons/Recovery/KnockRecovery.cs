using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Recovery;

/// <summary>How far a shape had got before the piece was knocked, as the photo shows it.</summary>
public enum MarkState
{
    /// <summary>Its marks show all round: it was finished.</summary>
    Done,
    /// <summary>Some of its marks show: it was being marked when the piece moved (or is faint).</summary>
    Partly,
    /// <summary>Nothing of it shows yet.</summary>
    NotYet,
}

/// <summary>A shape that leaves marks: the lines the laser draws for it (world mm, where the design has it now).</summary>
/// <param name="Lines">Paths and outlines; a closed one ends on its first point.</param>
public sealed record MarkedShape(Guid Id, string Label, IReadOnlyList<IReadOnlyList<Vec2>> Lines);

/// <summary>What the photo shows of one shape, once the design is moved onto the piece.</summary>
/// <param name="Seen">Fraction of its lines lying on an edge in the photo (0–1).</param>
/// <param name="Background">The same where nothing was marked: plain material and its grain, a little off the shape (0–1).</param>
public sealed record ShapeSeen(Guid Id, string Label, double Seen, double Background, MarkState State)
{
    /// <summary>How clearly it shows above the material's own texture (0 = not at all, 1 = every bit of it).</summary>
    public double Clarity => Background >= 1 ? 0 : Math.Clamp((Seen - Background) / (1 - Background), 0, 1);
}

/// <summary>Where the piece went and what's already on it.</summary>
/// <param name="Move">Takes the design from where it was onto the piece where it is now (world mm).</param>
/// <param name="Offset">How far the middle of the marks moved (mm; + = right / towards the front).</param>
/// <param name="AngleDeg">How far it turned (degrees, + = clockwise seen from above).</param>
/// <param name="Matched">Fraction of the finished marks found on edges in the photo (0–1).</param>
/// <param name="RmsMm">Typical distance from the design's lines to those edges, mm (NaN when nothing matched).</param>
/// <param name="FarErrorMm">How far off the furthest part of the job may land, mm: the fit's spread plus the turn's uncertainty over that distance.</param>
/// <param name="OtherTurnDeg">Another turn that fits the marks nearly as well (round or symmetrical marks), or null.</param>
public sealed record Recovery(
    Affine Move, Vec2 Center, Vec2 Offset, double AngleDeg, double Matched, double RmsMm, double FarErrorMm,
    IReadOnlyList<ShapeSeen> Shapes, double? OtherTurnDeg)
{
    /// <summary>The finished marks are found well enough to trust the move.</summary>
    public bool Trusted => Matched >= KnockRecovery.TrustedMatch && OtherTurnDeg is null;
}

/// <summary>Limits for <see cref="KnockRecovery.Find"/>.</summary>
public sealed record RecoverOptions
{
    /// <summary>Largest turn either way, degrees (180 = any).</summary>
    public double MaxTurnDeg { get; init; } = 30;
    /// <summary>How far the piece may have moved, mm.</summary>
    public double SearchMm { get; init; } = 30;
}

/// <summary>
/// Knocked-piece recovery: a piece bumped halfway through a job is found again from a top-down
/// photo by the marks already burnt on it, so the rest of the job can be moved to line up.
/// <para>The design's lines are fitted to the edges in the photo (<see cref="ShapeFit"/>, the
/// fit Line up pieces uses), turning and moving but never resizing. A first fit uses every shape
/// that leaves marks; each shape is then checked for marks under it, and the fit is made again
/// with only the shapes that show, so shapes not marked yet can't pull it off.</para>
/// </summary>
public static class KnockRecovery
{
    /// <summary>A line point within this of an edge counts as seen (mm).</summary>
    const double OnEdgeMm = 0.5;
    /// <summary>Clarity at or above which a shape counts as finished, and below which it counts as not marked yet.</summary>
    public const double DoneClarity = 0.55, PartlyClarity = 0.2;
    /// <summary>Fraction of the finished marks that must be found to trust the fit.</summary>
    public const double TrustedMatch = 0.5;

    /// <summary>The shapes on the page that leave marks (on a layer that's sent, or under a sub-layer that is), as lines.</summary>
    public static List<MarkedShape> Marked(DocState s)
    {
        var owners = JobBuilder.OutputLayers(s).Select(l => s.Owner(l.Id)).ToHashSet();
        var o = new List<MarkedShape>();
        foreach (var shape in s.Shapes)
        {
            if (!owners.Contains(shape.LayerId)) continue;
            var lines = LinesOf(shape);
            if (lines.Count > 0) o.Add(new MarkedShape(shape.Id, Label(shape), lines));
        }
        return o;
    }

    /// <summary>A shape's lines (world mm): a path's subpaths; a picture's outline (or its clip's).</summary>
    public static List<IReadOnlyList<Vec2>> LinesOf(Shape shape)
    {
        var o = new List<IReadOnlyList<Vec2>>();
        switch (shape)
        {
            case PathShape p:
                foreach (var f in p.World.Flatten(0.05))
                {
                    if (f.Points.Count < 2) continue;
                    var pts = new List<Vec2>(f.Points);
                    if (f.Closed && pts[0] != pts[^1]) pts.Add(pts[0]);
                    o.Add(pts);
                }
                break;
            case ImageShape img:
                var loop = img.ShownPoints.ToList();
                if (loop.Count >= 3)
                {
                    loop.Add(loop[0]);
                    o.Add(loop);
                }
                break;
        }
        return o;
    }

    static string Label(Shape s) => s.Name.Length > 0 ? s.Name : s switch
    {
        TextShape t => $"Text \"{(t.Text.Length > 24 ? t.Text[..24] + "…" : t.Text)}\"",
        ImageShape => "Picture",
        CodeShape => "Code",
        _ => "Shape",
    };

    /// <summary>
    /// Find the piece in <paramref name="image"/> by its marks.
    /// </summary>
    /// <param name="image">Straightened top-down photo covering <paramref name="area"/> (pixel (0, 0) at its top-left).</param>
    /// <param name="shapes">The shapes that leave marks, where the design has them now.</param>
    /// <param name="knownDone">Shapes the person says were finished (null = find out from the photo).</param>
    /// <exception cref="ArgumentException">No shapes, or none inside the photo.</exception>
    public static Recovery Find(GrayImage image, Rect2 area, double mmPerPx, IReadOnlyList<MarkedShape> shapes,
        RecoverOptions? options = null, IReadOnlySet<Guid>? knownDone = null, CancellationToken cancellationToken = default)
    {
        var opt = options ?? new RecoverOptions();
        var view = area.Inflate(-1);
        var inView = shapes.Select(s => s with { Lines = Clip(s.Lines, view) }).Where(s => s.Lines.Count > 0).ToList();
        if (inView.Count == 0) throw new ArgumentException("None of the design is inside the camera's picture.", nameof(shapes));
        if (knownDone is not null && !inView.Any(s => knownDone.Contains(s.Id)))
            throw new ArgumentException("None of the finished shapes is inside the camera's picture.", nameof(knownDone));

        var edges = EdgeMap.Build(image);
        cancellationToken.ThrowIfCancellationRequested();
        var photo = new Photo(edges, DistanceField.Build(edges.IsEdge, image.Width, image.Height), area, mmPerPx);
        var samples = inView.ToDictionary(s => s.Id, s => SampleWithNormals(s.Lines, 0.25));

        // 1. Where the piece went. Shapes the person named as finished are fitted together. Otherwise
        //    each of the bigger shapes is fitted on its own, and the place that the most other shapes
        //    show up at wins: finished shapes agree on it, while shapes not marked yet (and the grain
        //    they may land on) each go their own way.
        Affine move;
        if (knownDone is not null)
            move = FitTogether(image, area, mmPerPx, [.. inView.Where(s => knownDone.Contains(s.Id))], Affine.Identity, opt.SearchMm, opt.MaxTurnDeg, cancellationToken);
        else
        {
            var seeds = inView.OrderByDescending(s => samples[s.Id].Count).Take(MaxSeeds).ToList();
            var tried = new List<Affine>();
            foreach (var seed in seeds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (samples[seed.Id].Count < 16) continue; // under 4 mm of line: too little to place on its own
                tried.Add(FitOne(image, area, mmPerPx, seed, opt, cancellationToken));
            }
            if (tried.Count == 0) tried.Add(FitTogether(image, area, mmPerPx, inView, Affine.Identity, opt.SearchMm, opt.MaxTurnDeg, cancellationToken));
            move = tried.OrderByDescending(m => Support(inView, samples, m, photo)).First();
        }

        // 2. What shows there, then a last fit with only the finished shapes, close to that place.
        var seen = Check(inView, samples, move, photo, knownDone);
        var guide = inView.Where(s => seen.First(x => x.Id == s.Id).State == MarkState.Done).ToList();
        if (guide.Count == 0) guide = [.. inView.Where(s => seen.First(x => x.Id == s.Id).State == MarkState.Partly)];
        if (guide.Count > 0)
        {
            var refined = FitTogether(image, area, mmPerPx, guide, move, 4, 4, cancellationToken);
            var seenRefined = Check(inView, samples, refined, photo, knownDone);
            if (Score(guide, samples, refined, photo).Seen >= Score(guide, samples, move, photo).Seen - 0.02) { move = refined; seen = seenRefined; }
        }

        // 3. How sure: how much of the finished marks is found, how closely, and whether another turn fits as well.
        var marks = seen.Where(x => x.State == MarkState.Done).Select(x => x.Id).ToHashSet();
        var scored = inView.Where(s => marks.Contains(s.Id)).ToList();
        if (scored.Count == 0) scored = guide.Count > 0 ? guide : inView;
        var all = scored.SelectMany(s => samples[s.Id]).ToList();
        int on = 0;
        double sq = 0;
        foreach (var (p, n) in all)
        {
            var (d, ok) = photo.At(move.Apply(p), move.ApplyVector(n));
            if (ok) { on++; sq += d * d; }
        }
        double matched = all.Count == 0 ? 0 : (double)on / all.Count;
        double rms = on > 0 ? Math.Sqrt(sq / on) : double.NaN;

        var lines = scored.SelectMany(s => s.Lines).ToList();
        var marksBox = Rect2.Bounds(lines.SelectMany(l => l));
        var center = marksBox.Center;
        var offset = move.Apply(center) - center;
        double angle = Math.Atan2(move.B, move.A) * 180 / Math.PI;
        // How far off the furthest part of the job may be: the spread, plus the turn's uncertainty
        // (about the spread over the size of the marks) carried out to the far end of the design.
        double spread = double.IsFinite(rms) ? Math.Max(rms, mmPerPx / 2) : 1;
        double arm = Math.Max(5, Math.Max(marksBox.Width, marksBox.Height) / 2);
        double far = shapes.SelectMany(s => s.Lines).SelectMany(l => l).Select(p => p.Dist(center)).DefaultIfEmpty(0).Max();
        double farError = spread + spread / arm * far;

        double? other = OtherTurn(all, move, center, opt, photo);
        return new Recovery(move, center, offset, angle, matched, rms, farError, seen, other);
    }

    /// <summary>How many of the biggest shapes are each tried on their own.</summary>
    const int MaxSeeds = 10;

    /// <summary>The photo's edges, for checking where lines land.</summary>
    sealed record Photo(EdgeMap Edges, DistanceField Field, Rect2 Area, double MmPerPx)
    {
        /// <summary>Distance to the nearest edge (mm), and whether the line point counts as on it: near enough, and running the same way.</summary>
        public (double Dist, bool On) At(Vec2 p, Vec2 normal)
        {
            double d = Field.DistanceMm(p.X, p.Y, Area, MmPerPx);
            if (d > OnEdgeMm) return (d, false);
            var g = Edges.DirectionAt(Field.NearestEdge(p.X, p.Y, Area, MmPerPx));
            return (d, Math.Abs(g.X * normal.X + g.Y * normal.Y) >= 0.75);
        }
    }

    /// <summary>One shape fitted on its own, in the part of the photo it can reach.</summary>
    static Affine FitOne(GrayImage image, Rect2 area, double mmPerPx, MarkedShape shape, RecoverOptions opt, CancellationToken ct)
    {
        var reach = Rect2.Bounds(shape.Lines.SelectMany(l => l)).Inflate(opt.SearchMm + 6);
        var (crop, cropArea) = Crop(image, area, mmPerPx, reach);
        return FitTogether(crop, cropArea, mmPerPx, [shape], Affine.Identity, opt.SearchMm, opt.MaxTurnDeg, ct);
    }

    /// <summary>The shapes (first moved by <paramref name="from"/>) fitted to the photo's edges; the result includes <paramref name="from"/>.</summary>
    static Affine FitTogether(GrayImage image, Rect2 area, double mmPerPx, IReadOnlyList<MarkedShape> shapes, Affine from, double searchMm, double maxTurnDeg, CancellationToken ct)
    {
        var lines = shapes.SelectMany(s => s.Lines).Select(l => (IReadOnlyList<Vec2>)l.Select(from.Apply).ToList()).ToList();
        var fit = ShapeFit.Fit(image, area, mmPerPx, lines, new FitOptions { MaxRotationDeg = maxTurnDeg, MaxScaleChange = 0, SearchMm = searchMm }, ct);
        return from.Then(Affine.RotateAbout(fit.Center, fit.AngleDeg)).Then(Affine.Translate(fit.Offset));
    }

    /// <summary>The part of the photo inside <paramref name="want"/> (on whole pixels), and the area it covers.</summary>
    static (GrayImage Image, Rect2 Area) Crop(GrayImage image, Rect2 area, double mmPerPx, Rect2 want)
    {
        int x0 = Math.Clamp((int)Math.Floor((want.MinX - area.MinX) / mmPerPx), 0, image.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor((want.MinY - area.MinY) / mmPerPx), 0, image.Height - 1);
        int x1 = Math.Clamp((int)Math.Ceiling((want.MaxX - area.MinX) / mmPerPx), x0 + 1, image.Width);
        int y1 = Math.Clamp((int)Math.Ceiling((want.MaxY - area.MinY) / mmPerPx), y0 + 1, image.Height);
        int w = x1 - x0, h = y1 - y0;
        var px = new byte[w * h];
        for (int y = 0; y < h; y++) Array.Copy(image.Pixels, (y0 + y) * image.Width + x0, px, y * w, w);
        return (new GrayImage(w, h, px), new Rect2(area.MinX + x0 * mmPerPx, area.MinY + y0 * mmPerPx, area.MinX + x1 * mmPerPx, area.MinY + y1 * mmPerPx));
    }

    /// <summary>How many shapes show up clearly with the design moved by <paramref name="move"/> (each counted by how clearly).</summary>
    static double Support(List<MarkedShape> shapes, Dictionary<Guid, List<(Vec2 P, Vec2 N)>> samples, Affine move, Photo photo)
    {
        double sum = 0;
        foreach (var s in shapes)
        {
            var x = Score([s], samples, move, photo);
            sum += Math.Max(0, Clarity(x.Seen, x.Background) - PartlyClarity);
        }
        return sum;
    }

    static double Clarity(double seen, double background) => background >= 1 ? 0 : Math.Clamp((seen - background) / (1 - background), 0, 1);

    /// <summary>The fraction of the shapes' lines on a matching edge, and the same a little off to each side (the material's own texture).</summary>
    static (double Seen, double Background) Score(IEnumerable<MarkedShape> shapes, Dictionary<Guid, List<(Vec2 P, Vec2 N)>> samples, Affine move, Photo photo)
    {
        var pts = shapes.SelectMany(s => samples[s.Id]).Select(x => (P: move.Apply(x.P), N: move.ApplyVector(x.N))).ToList();
        double seen = OnEdge(pts, photo, new Vec2(0, 0));
        var bg = new List<double>();
        foreach (var (dx, dy) in new[] { (2.5, 0.0), (-2.5, 0.0), (0.0, 2.5), (0.0, -2.5), (1.8, 1.8), (-1.8, -1.8), (1.8, -1.8), (-1.8, 1.8) })
            bg.Add(OnEdge(pts, photo, new Vec2(dx, dy)));
        bg.Sort();
        return (seen, bg[bg.Count / 2]);
    }

    static double OnEdge(List<(Vec2 P, Vec2 N)> pts, Photo photo, Vec2 shift)
    {
        if (pts.Count == 0) return 0;
        int on = 0;
        foreach (var (p, n) in pts) if (photo.At(p + shift, n).On) on++;
        return (double)on / pts.Count;
    }

    /// <summary>What each shape shows, the design moved by <paramref name="move"/>.</summary>
    static List<ShapeSeen> Check(List<MarkedShape> shapes, Dictionary<Guid, List<(Vec2 P, Vec2 N)>> samples, Affine move, Photo photo, IReadOnlySet<Guid>? knownDone)
    {
        var o = new List<ShapeSeen>();
        foreach (var s in shapes)
        {
            var (seen, background) = Score([s], samples, move, photo);
            var x = new ShapeSeen(s.Id, s.Label, seen, background, MarkState.NotYet);
            var state = knownDone is not null
                ? (knownDone.Contains(s.Id) ? MarkState.Done : MarkState.NotYet)
                : x.Clarity >= DoneClarity ? MarkState.Done : x.Clarity >= PartlyClarity ? MarkState.Partly : MarkState.NotYet;
            o.Add(x with { State = state });
        }
        return o;
    }

    /// <summary>
    /// Another turn of the marks (well away from the one found) that fits them nearly as well, as a
    /// round or symmetrical set of marks does: then the turn can't be told from the photo.
    /// </summary>
    static double? OtherTurn(List<(Vec2 P, Vec2 N)> samples, Affine move, Vec2 center, RecoverOptions opt, Photo photo)
    {
        if (samples.Count == 0) return null;
        // Every 4th point is plenty to compare turns.
        var few = samples.Where((_, i) => i % 4 == 0).ToList();
        (double Seen, double Bg) Try(Affine m)
        {
            var pts = few.Select(x => (m.Apply(x.P), m.ApplyVector(x.N))).ToList();
            return (OnEdge(pts, photo, new Vec2(0, 0)), Math.Min(OnEdge(pts, photo, new Vec2(2.5, 0)), OnEdge(pts, photo, new Vec2(0, 2.5))));
        }
        var (hereSeen, hereBg) = Try(move);
        double here = Clarity(hereSeen, hereBg);
        if (here <= 0) return null;
        var c = move.Apply(center);
        double found = Math.Atan2(move.B, move.A) * 180 / Math.PI;
        double lo = opt.MaxTurnDeg >= 179 ? -180 : -opt.MaxTurnDeg, hi = opt.MaxTurnDeg >= 179 ? 180 : opt.MaxTurnDeg;
        double best = 0;
        double? at = null;
        for (double a = lo; a <= hi; a += 2)
        {
            double turn = Wrap(a - found);
            if (Math.Abs(turn) < 8) continue;
            // Small shifts too: the marks' middle needn't be the centre of turn.
            for (double dy = -2; dy <= 2; dy += 0.5)
                for (double dx = -2; dx <= 2; dx += 0.5)
                {
                    var m = move.Then(Affine.RotateAbout(c, turn)).Then(Affine.Translate(dx, dy));
                    var (seen, _) = Try(m);
                    double clarity = Clarity(seen, hereBg);
                    if (clarity > best) { best = clarity; at = a; }
                }
        }
        return best >= 0.85 * here && at is { } t ? Wrap(t) : null;
    }

    static double Wrap(double a) { a %= 360; if (a > 180) a -= 360; if (a <= -180) a += 360; return a; }

    /// <summary>Points every <paramref name="spacing"/> mm along the lines, with the unit normal there.</summary>
    internal static List<(Vec2 P, Vec2 N)> SampleWithNormals(IEnumerable<IReadOnlyList<Vec2>> lines, double spacing)
    {
        var o = new List<(Vec2, Vec2)>();
        foreach (var path in lines)
        {
            double carry = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                double len = a.Dist(b);
                if (len < 1e-9) continue;
                var t = (b - a) / len;
                var n = new Vec2(-t.Y, t.X);
                double d = carry;
                for (; d < len; d += spacing) o.Add((a + t * d, n));
                carry = d - len;
            }
        }
        return o;
    }

    /// <summary>The parts of the lines inside <paramref name="view"/>.</summary>
    internal static List<IReadOnlyList<Vec2>> Clip(IEnumerable<IReadOnlyList<Vec2>> lines, Rect2 view)
    {
        var o = new List<IReadOnlyList<Vec2>>();
        foreach (var pts in lines)
        {
            List<Vec2>? run = null;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (ClipSegment(pts[i], pts[i + 1], view) is not { } seg)
                {
                    if (run is { Count: >= 2 }) o.Add(run);
                    run = null;
                    continue;
                }
                if (run is null || run[^1] != seg.A)
                {
                    if (run is { Count: >= 2 }) o.Add(run);
                    run = [seg.A];
                }
                run.Add(seg.B);
            }
            if (run is { Count: >= 2 }) o.Add(run);
        }
        return o;
    }

    /// <summary>The part of a segment inside a rectangle (Liang–Barsky), or null.</summary>
    static (Vec2 A, Vec2 B)? ClipSegment(Vec2 a, Vec2 b, Rect2 r)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (p, q) in new[] { (-dx, a.X - r.MinX), (dx, r.MaxX - a.X), (-dy, a.Y - r.MinY), (dy, r.MaxY - a.Y) })
        {
            if (Math.Abs(p) < 1e-12) { if (q < 0) return null; continue; }
            double t = q / p;
            if (p < 0) { if (t > t1) return null; if (t > t0) t0 = t; }
            else { if (t < t0) return null; if (t < t1) t1 = t; }
        }
        if (t1 - t0 < 1e-12) return null;
        return (t0 == 0 ? a : new Vec2(a.X + t0 * dx, a.Y + t0 * dy), t1 == 1 ? b : new Vec2(a.X + t1 * dx, a.Y + t1 * dy));
    }

    /// <summary>Name of the layer the finished shapes are put on, on the page with the rest of the job.</summary>
    public const string DoneLayerName = "Already marked";

    /// <summary>
    /// The rest of the job, on a new page after this one (this page is left as it was): a copy of
    /// the page with <paramref name="moved"/> moved onto the piece by <paramref name="move"/>, and
    /// <paramref name="leaveOut"/> put on a grey "Already marked" layer that isn't sent, so they
    /// still show over the camera picture but aren't marked again. The new page is shown.
    /// </summary>
    public static DocState RestPage(DocState s, IReadOnlySet<Guid> moved, IReadOnlySet<Guid> leaveOut, Affine move)
    {
        string name = s.PageName;
        var before = s.Shapes;
        var t = Pages.Duplicate(s);
        // The copy keeps the shapes in the same order, with new ids.
        var map = new Dictionary<Guid, Guid>();
        for (int i = 0; i < before.Count && i < t.Shapes.Count; i++) map[before[i].Id] = t.Shapes[i].Id;
        HashSet<Guid> Mapped(IEnumerable<Guid> ids) => [.. ids.Where(map.ContainsKey).Select(id => map[id])];

        t = Ops.Transform(t, Mapped(moved), move);
        var done = Mapped(leaveOut);
        if (done.Count > 0)
        {
            var layer = Layer.Default(t.Layers.Count, LayerMode.Score) with { Name = DoneLayerName, Output = false, Locked = true, Color = 0x8A8F98 };
            t = Ops.AddLayer(t, layer);
            t = Ops.MoveToLayer(t, done, layer.Id);
        }
        return Pages.Rename(t, $"{name} – rest after the knock");
    }
}
