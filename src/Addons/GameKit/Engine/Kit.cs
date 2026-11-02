using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// One piece to cut (flat, mm, y up): its outline and holes, what's etched on it as areas, and lines
/// scored on it; and where each copy of it sits in one accessory (<see cref="Poses"/>).
/// </summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses)
{
    /// <summary>How many to cut for one accessory.</summary>
    public int Copies => Math.Max(1, Poses.Count);
}

/// <summary>The pieces of one accessory, and what was found while making them.</summary>
public sealed record ItemKit(AccessoryItem Item, List<Part> Parts, List<Finding> Findings)
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

/// <summary>
/// A flat piece being designed: its outline (tabs included), the holes in it, and where it sits.
/// A tab registered with <see cref="Maker.Tab"/> cuts its own slot in the piece it goes into, so a
/// tab and its slot can't disagree.
/// </summary>
public sealed class Plate
{
    public required string Name { get; init; }
    public required List<Pose> Poses { get; init; }
    public PathsD Body { get; } = [];
    public PathsD Holes { get; } = [];
    /// <summary>The area the finish covers (null: none); clipped to the piece.</summary>
    public PathsD? Face { get; set; }
    public bool GrainAlongU { get; set; } = true;
    public PathsD Etched { get; } = [];
    public List<PathD> Lines { get; } = [];
    /// <summary>Copies cut apart into pieces of their own (the split base of a long tracker).</summary>
    public bool MayBreak { get; set; }
}

/// <summary>Builds one accessory: the sheet, the fit and the finish it's made with, and a place for findings.</summary>
public sealed class Maker
{
    public AccessoryItem Item { get; }
    public AccessorySet Set { get; }
    public double T { get; }
    public double Fit { get; }
    public Random Rnd { get; }
    /// <summary>The longest piece the bed takes (mm): longer bases are cut in pieces.</summary>
    public double MaxLength { get; }
    public List<Finding> Findings { get; } = [];
    readonly List<Plate> _plates = [];
    readonly List<(Plate From, PathD Tab, Plate[] Into)> _tabs = [];

    /// <summary>The narrowest strip of sheet left beside a slot or between cut-outs (mm).</summary>
    public const double Bridge = 1.2;

    public Maker(AccessoryItem item, AccessorySet set, int index, double maxLength)
    {
        Item = item;
        Set = set;
        T = Math.Clamp(set.ThicknessMm, 0.5, 12);
        Fit = Math.Clamp(set.FitMm, -0.5, 2);
        MaxLength = Math.Max(30, maxLength);
        Rnd = new Random(set.Seed * 7919 + index * 104729 + (int)item.Kind * 31 + 17);
    }

    /// <summary>A slot's width: the sheet plus the fit.</summary>
    public double SlotW => T + Fit;

    public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
    public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
    public void Note(string text) => Findings.Add(new(Severity.Note, text));

    public Plate Plate(string name, params Pose[] poses)
    {
        var p = new Plate { Name = name, Poses = [.. poses] };
        _plates.Add(p);
        return p;
    }

    /// <summary>
    /// A tab on <paramref name="from"/> (an area in its flat coordinates, added to its outline) that goes
    /// through one of <paramref name="into"/>: the slot is cut in whichever it passes through once put
    /// together (for each copy), a fit wider all round.
    /// </summary>
    public void Tab(Plate from, PathD tab, params Plate[] into)
    {
        from.Body.Add(tab);
        _tabs.Add((from, tab, into));
    }

    /// <summary>The slot a tab cuts in the plate it goes through (its flat coordinates), or null if it misses it.</summary>
    PathD? SlotOf(Pose from, PathD tab, Pose into)
    {
        var pts = new List<PointD>();
        double wLo = double.MaxValue, wHi = double.MinValue;
        foreach (var q in tab)
            foreach (var w in new[] { -T / 2, T / 2 })
            {
                var (u, v, ww) = into.Local(from.At(q.x, q.y, w));
                pts.Add(new PointD(u, v));
                wLo = Math.Min(wLo, ww);
                wHi = Math.Max(wHi, ww);
            }
        // It has to pass right through the plate.
        if (wLo > -T / 2 + 1e-6 || wHi < T / 2 - 1e-6) return null;
        var hull = Hull(pts);
        if (hull.Count < 3) return null;
        var grown = Fit > 0 ? Geo.Offset([hull], Fit / 2) : Fit < 0 ? Geo.Offset([hull], Fit / 2) : [hull];
        return grown.Count == 0 ? null : grown[0];
    }

    /// <summary>The convex hull, anticlockwise.</summary>
    static PathD Hull(List<PointD> pts)
    {
        var p = pts.Distinct().OrderBy(a => a.x).ThenBy(a => a.y).ToList();
        if (p.Count < 3) return [.. p];
        static double Cross(PointD o, PointD a, PointD b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var h = new PointD[2 * p.Count];
        int k = 0;
        foreach (var q in p) { while (k >= 2 && Cross(h[k - 2], h[k - 1], q) <= 1e-12) k--; h[k++] = q; }
        for (int i = p.Count - 2, t = k + 1; i >= 0; i--) { while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 1e-12) k--; h[k++] = p[i]; }
        return [.. h.Take(k - 1)];
    }

    /// <summary>Cuts every tab's slot, lays the finish on the faces and checks the pieces.</summary>
    public ItemKit Kit()
    {
        var parts = new List<Part>();
        if (Findings.Any(f => f.Level == Severity.Bad)) return new(Item, parts, Findings);
        foreach (var (from, tab, into) in _tabs)
        {
            bool any = false;
            foreach (var target in into)
                foreach (var pf in from.Poses)
                    foreach (var pi in target.Poses)
                        if (SlotOf(pf, tab, pi) is { } slot)
                        {
                            target.Holes.Add(slot);
                            any = true;
                        }
            if (!any) Bad($"The {from.Name.ToLowerInvariant()}'s tab misses the {string.Join(" and ", into.Select(t => t.Name.ToLowerInvariant()))}: make it a little bigger.");
        }
        foreach (var p in _plates)
        {
            var cut = Geo.Subtract(Geo.Union(p.Body), Geo.Union(p.Holes));
            var etch = new PathsD(p.Etched);
            var lines = new List<PathD>(p.Lines);
            if (p.Face is { } face && Set.Finish != Finish.None)
            {
                var (de, dl) = Surface.Make(Geo.Intersect(face, cut), Set.Finish, Set.TextureScale, p.GrainAlongU, Rnd);
                etch.AddRange(de);
                lines.AddRange(dl);
            }
            var etched = Geo.Intersect(Geo.Union(etch), cut);
            var scored = Geo.ClipLines(lines, cut);
            if (Geo.Pieces(cut) != 1)
            {
                if (!p.MayBreak) { Bad($"The {p.Name.ToLowerInvariant()} comes out in more than one piece: make it bigger or the sheet thinner."); continue; }
                // Each separate bit a piece of its own.
                var outers = Geo.Tidy(cut).Where(q => Clipper.Area(q) > 1e-6).ToList();
                for (int i = 0; i < outers.Count; i++)
                {
                    var bit = Geo.Intersect(cut, [outers[i]]);
                    parts.Add(new Part($"{p.Name}{(outers.Count > 1 ? $" ({(char)('a' + i)})" : "")}", bit, Geo.Intersect(etched, bit), Geo.ClipLines(scored, bit), p.Poses));
                }
                continue;
            }
            if (Thin(cut) is > 0.05) Warn($"The {p.Name.ToLowerInvariant()} has bits narrower than {Bridge:0.#} mm that may break: make it bigger, or use a thinner sheet.");
            parts.Add(new Part(p.Name, cut, etched, scored, p.Poses));
        }
        return new(Item, parts, Findings);
    }

    /// <summary>How much of the piece (mm²) is in strips narrower than about <see cref="Bridge"/>.</summary>
    public static double Thin(PathsD cut)
    {
        double w = Bridge * 0.8;
        var opened = Geo.Offset(Geo.Offset(cut, -w / 2), w / 2);
        return Geo.Area(Geo.Subtract(cut, opened));
    }
}

/// <summary>Makes the pieces for every accessory in a set.</summary>
public static class GameKitMaker
{
    /// <summary>Longest piece made when the bed isn't known (mm).</summary>
    public const double DefaultMaxLength = 210;

    public static ItemKit Make(AccessoryItem item, AccessorySet set, int index = 0, double maxLength = DefaultMaxLength)
    {
        var m = new Maker(item, set, index, maxLength);
        switch (item.Kind)
        {
            case AccessoryKind.DiceTower: DiceTower.Make(m); break;
            case AccessoryKind.CardStand: Cards.Stand(m); break;
            case AccessoryKind.DeckTray: Cards.Tray(m); break;
            case AccessoryKind.Initiative: Initiative.Make(m); break;
            case AccessoryKind.MoveGauge: Measures.Gauge(m); break;
            case AccessoryKind.Ruler: Measures.Ruler(m); break;
            case AccessoryKind.Template: Measures.Template(m); break;
        }
        return m.Kit();
    }

    public static SetKit Make(AccessorySet set, double maxLength = DefaultMaxLength) =>
        new([.. set.Items.Select((it, i) => Make(it, set, i, maxLength))], set.ThicknessMm);

    /// <summary>"Dice tower", or "Dice tower 2" when there's more than one in the set.</summary>
    public static string Label(AccessorySet set, int index)
    {
        var kind = set.Items[index].Kind;
        string name = Defaults.Info(kind).Name;
        int same = set.Items.Count(i => i.Kind == kind);
        return same < 2 ? name : $"{name} {set.Items.Take(index + 1).Count(i => i.Kind == kind)}";
    }

    /// <summary>Even spots for <paramref name="n"/> tabs along an edge from <paramref name="a"/> to <paramref name="b"/>: each tab's start and end.</summary>
    public static List<(double A, double B)> Spots(double a, double b, int n, double width)
    {
        var list = new List<(double, double)>();
        double len = b - a;
        if (len <= 0 || n <= 0) return list;
        width = Math.Min(width, len / (2 * n));
        for (int i = 0; i < n; i++)
        {
            double c = a + len * (i + 0.5) / n;
            list.Add((c - width / 2, c + width / 2));
        }
        return list;
    }
}
