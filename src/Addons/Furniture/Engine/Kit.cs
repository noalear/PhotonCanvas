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
        Detail = set.Detail;
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
                    PathsD? etched = null, IEnumerable<PathD>? lines = null, bool planks = false, double plankMm = 3)
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
        var part = new Part(name, cut, Geo.Intersect(Geo.Union(etch), cut), Geo.ClipLines(ln, cut), [.. poses]);
        if (Geo.Pieces(cut) != 1) Bad($"The {name.ToLowerInvariant()} comes out in more than one piece: make it bigger or the sheet thinner.");
        else if (Thin(cut) is > 0.05) Warn($"The {name.ToLowerInvariant()} has bits narrower than {Bridge:0.#} mm that may break: make it bigger, or use a thinner sheet.");
        Parts.Add(part);
        return part;
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
