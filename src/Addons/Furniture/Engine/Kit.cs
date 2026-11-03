using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// One piece to cut (flat, mm, y up): its outline and holes, what's etched on it as areas, and lines
/// scored on it; and where each copy of it sits in one piece of furniture (<see cref="Poses"/>).
/// </summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses)
{
    /// <summary>How many to cut for one piece of furniture.</summary>
    public int Copies => Math.Max(1, Poses.Count);

    /// <summary>The grip ribs added to <see cref="Cut"/> for a push fit (they squash as the joint goes together).</summary>
    public PathsD Ribs { get; init; } = [];
}

/// <summary>The pieces of one piece of furniture, and what was found while making them.</summary>
public sealed record ItemKit(FurnitureItem Item, List<Part> Parts, List<Finding> Findings)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies) * Math.Max(1, Item.Count);
}

/// <summary>Everything in a set.</summary>
public sealed record SetKit(List<ItemKit> Items, double ThicknessMm)
{
    public bool Ok => Items.Count > 0 && Items.All(i => i.Ok);
    public int PieceCount => Items.Sum(i => i.PieceCount);
}

/// <summary>Builds one piece of furniture: the sheet, the fit and the detail it's made with, and a place for findings.</summary>
public sealed class Maker
{
    public FurnitureItem Item { get; }
    public double T { get; }
    public double Fit { get; }
    public WoodDetail Detail { get; }
    /// <summary>The look it's made in (a <see cref="FurnitureThemes"/> name): the item's own, or the set's.</summary>
    public string Theme { get; }
    /// <summary>Metal faces: handles are bars, panels have chamfered corners.</summary>
    public bool IsMetal => Detail == WoodDetail.Metal;
    /// <summary>Grip ribs in the joints (see <see cref="FurnitureSet.PushFit"/>).</summary>
    public bool PushFit { get; }
    /// <summary>How much tighter than the sheet the ribs make a joint (mm, both sides together).</summary>
    public double Grip { get; }
    /// <summary>Gaps besides <see cref="SlotW"/> that get ribs (a clip's fork round a wall).</summary>
    public List<double> RibGaps { get; } = [];
    public Random Rnd { get; }
    public List<Finding> Findings { get; } = [];
    public List<Part> Parts { get; } = [];

    /// <summary>The narrowest strip of sheet left beside a slot or between cut-outs (mm).</summary>
    public const double Bridge = 1.0;

    public Maker(FurnitureItem item, FurnitureSet set, int index)
    {
        Item = item;
        T = Math.Clamp(set.ThicknessMm, 0.3, 20);
        Fit = Math.Clamp(set.FitMm, -0.5, 2);
        Theme = FurnitureThemes.Names.Contains(item.Theme ?? "") ? item.Theme! : FurnitureThemes.Names.Contains(set.Theme) ? set.Theme : FurnitureThemes.Names[0];
        // A piece of another theme than the set's keeps its own theme's faces unless it says otherwise.
        Detail = item.Detail ?? (Theme != set.Theme ? FurnitureThemes.Detail(Theme) : set.Detail);
        PushFit = set.PushFit;
        Grip = Math.Clamp(set.GripMm, 0.02, 0.5);
        Rnd = new Random(set.Seed * 7919 + index * 104729 + (int)item.Kind * 31 + 17);
    }

    /// <summary>A slot's width: the sheet plus the fit.</summary>
    public double SlotW => T + Fit;

    public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
    public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
    public void Note(string text) => Findings.Add(new(Severity.Note, text));

    /// <summary>
    /// Adds a piece: its outline less the holes, the wood detail on <paramref name="face"/> (the outline
    /// itself if null) running along u or v, and any extra etching and lines.
    /// </summary>
    public Part Add(string name, PathsD outline, PathsD holes, IEnumerable<Pose> poses, bool grainAlongU, PathsD? face = null,
                    PathsD? etched = null, IEnumerable<PathD>? lines = null, bool planks = false, double plankMm = 3, PathsD? ribs = null)
    {
        var cut = Geo.Subtract(Geo.Union(outline), Geo.Union(holes));
        var etch = new PathsD();
        var ln = new List<PathD>();
        var region = Geo.Intersect(face ?? cut, cut);
        var (de, dl) = WoodFace.Make(region, grainAlongU, Detail, planks, plankMm, Rnd);
        etch.AddRange(de);
        ln.AddRange(dl);
        if (etched is not null) etch.AddRange(etched);
        if (lines is not null) ln.AddRange(lines);
        etch = Geo.Intersect(Geo.Union(etch), cut);
        var scored = Geo.ClipLines(ln, cut);
        if (Geo.Pieces(cut) != 1) Bad($"The {name.ToLowerInvariant()} comes out in more than one piece: make it bigger or the sheet thinner.");
        else if (Thin(cut) is > 0.05) Warn($"The {name.ToLowerInvariant()} has bits narrower than {Bridge:0.#} mm that may break: make it bigger, or use a thinner sheet.");
        // The grip ribs go on last: they're meant to be tiny, so they're left out of the checks above.
        var grip = new PathsD();
        if (PushFit)
        {
            var more = Ribs(cut, [SlotW, .. RibGaps], (Fit + Grip) / 2);
            if (ribs is not null) more.AddRange(ribs);
            // Only teeth standing on the piece (a slot's edges matched across a sloping cut can put one adrift).
            more = [.. more.Where(q => Geo.Area(Geo.Intersect([q], cut)) > 1e-5)];
            if (more.Count > 0)
            {
                grip = Geo.Subtract(Geo.Union(more), cut);
                // Joined with their feet (which overlap the wall), so a sloping edge leaves no hairline gap.
                cut = Geo.Union(cut, Geo.Union(more));
            }
        }
        var part = new Part(name, cut, etch, scored, [.. poses]) { Ribs = grip };
        Parts.Add(part);
        return part;
    }

    /// <summary>
    /// Grip ribs for every slot or notch in <paramref name="cut"/>: wherever two of its straight edges face
    /// each other across an empty gap as wide as one of <paramref name="gaps"/>, a small tooth
    /// <paramref name="height"/> high on each, pointing into the gap (one, two or three along it).
    /// </summary>
    public static PathsD Ribs(PathsD cut, IReadOnlyList<double> gaps, double height)
    {
        var ribs = new PathsD();
        if (height <= 0) return ribs;
        var edges = new List<(PointD A, PointD Dir, double Len)>();
        foreach (var path in cut)
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Count];
                double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len >= 0.5) edges.Add((a, new PointD(dx / len, dy / len), len));
            }
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var (a, d, la) = edges[i];
                var (b, e, lb) = edges[j];
                if (d.x * e.x + d.y * e.y > -0.9999) continue;
                // b across from a's line, and how far.
                double bx = b.x - a.x, by = b.y - a.y;
                double across = d.x * by - d.y * bx;
                double gap = Math.Abs(across);
                if (!gaps.Any(g => Math.Abs(gap - g) < 0.015)) continue;
                // Where they overlap along a.
                double t0 = d.x * bx + d.y * by, t1 = t0 - lb;
                double lo = Math.Max(0, Math.Min(t0, t1)), hi = Math.Min(la, Math.Max(t0, t1));
                if (hi - lo < 0.5) continue;
                var n = new PointD(-d.y * Math.Sign(across), d.x * Math.Sign(across));
                double mid = (lo + hi) / 2;
                var centre = new PointD(a.x + d.x * mid + n.x * gap / 2, a.y + d.y * mid + n.y * gap / 2);
                if (Inside(cut, centre)) continue;
                double run = hi - lo;
                int count = run < 2.5 ? 1 : run < 8 ? 2 : 3;
                double baseW = Math.Min(0.8, run / (count * 1.5));
                for (int k = 0; k < count; k++)
                {
                    double at = lo + run * (k + 0.5) / count;
                    foreach (var (o, nn) in new[] { (new PointD(a.x + d.x * at, a.y + d.y * at), n),
                                                    (new PointD(a.x + d.x * at + n.x * gap, a.y + d.y * at + n.y * gap), new PointD(-n.x, -n.y)) })
                        ribs.Add(Tooth(o, new PointD(nn.y, -nn.x), nn, baseW, height));
                }
            }
        return ribs;
    }

    /// <summary>A small tooth standing on a wall at <paramref name="at"/>: <paramref name="along"/> the wall, pointing <paramref name="out"/>.</summary>
    public static PathD Tooth(PointD at, PointD along, PointD @out, double baseW, double height)
    {
        PointD P(double s, double h) => new(at.x + along.x * s + @out.x * h, at.y + along.y * s + @out.y * h);
        // Its foot goes a little into the wall so it joins the piece.
        return [P(-baseW / 2, -0.02), P(baseW / 2, -0.02), P(baseW / 2, 0), P(baseW * 0.12, height), P(-baseW * 0.12, height), P(-baseW / 2, 0)];
    }

    static bool Inside(PathsD region, PointD p)
    {
        int inside = 0;
        foreach (var path in region)
            if (Clipper.PointInPolygon(p, path) != PointInPolygonResult.IsOutside) inside++;
        return inside % 2 == 1;
    }

    /// <summary>How much of the piece (mm²) is in strips narrower than <see cref="Bridge"/>.</summary>
    public static double Thin(PathsD cut)
    {
        double w = Bridge * 0.9;
        var opened = Geo.Offset(Geo.Offset(cut, -w / 2), w / 2);
        return Geo.Area(Geo.Subtract(cut, opened));
    }

    public ItemKit Kit() => new(Item, Parts, Findings);
}
