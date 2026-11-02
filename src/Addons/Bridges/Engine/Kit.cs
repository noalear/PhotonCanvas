using Clipper2Lib;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// One piece to cut (flat, mm, y up): its outline and holes, what's etched on it as areas, and lines
/// scored on it; and where each copy of it sits in the bridge (<see cref="Poses"/>).
/// </summary>
public sealed record Part(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, List<Pose> Poses)
{
    /// <summary>How many to cut for one bridge.</summary>
    public int Copies => Math.Max(1, Poses.Count);
}

/// <summary>The pieces of one bridge, and what was found while making them.</summary>
public sealed record BridgeKit(BridgeSpec Spec, List<Part> Parts, List<Finding> Findings)
{
    public bool Ok => Parts.Count > 0 && !Findings.Any(f => f.Level == Severity.Bad);
    /// <summary>Every piece to cut, for every bridge asked for.</summary>
    public int PieceCount => Parts.Sum(p => p.Copies) * Math.Max(1, Spec.Count);
}

/// <summary>Builds one bridge: the sheet and fit it's made with, a dice for the random bits, and a place for findings.</summary>
public sealed class Maker
{
    public BridgeSpec Spec { get; }
    public double T { get; }
    public double Fit { get; }
    public Random Rnd { get; }
    public List<Finding> Findings { get; } = [];
    public List<Part> Parts { get; } = [];

    /// <summary>The narrowest strip of sheet left beside a slot or between cut-outs (mm).</summary>
    public const double Bridge = 1.0;

    public Maker(BridgeSpec spec)
    {
        Spec = spec;
        T = Math.Clamp(spec.ThicknessMm, 0.3, 20);
        Fit = Math.Clamp(spec.FitMm, -0.5, 2);
        Rnd = new Random(spec.Seed * 7919 + (int)spec.Kind * 31 + 17);
    }

    /// <summary>A slot's width: the sheet plus the fit.</summary>
    public double SlotW => T + Fit;

    public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
    public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
    public void Note(string text) => Findings.Add(new(Severity.Note, text));

    /// <summary>Adds a piece: its outline less the holes, with what's etched and scored on it (both kept inside the piece).</summary>
    public Part Add(string name, PathsD outline, PathsD holes, IEnumerable<Pose> poses, PathsD? etched = null, IEnumerable<PathD>? lines = null, bool check = true)
    {
        var cut = Geo.Subtract(Geo.Union(outline), Geo.Union(holes));
        var et = etched is { Count: > 0 } ? Geo.Intersect(Geo.Union(etched), cut) : [];
        var ln = lines is null ? [] : Geo.ClipLines(lines, cut);
        var part = new Part(name, cut, et, ln, [.. poses]);
        if (check)
        {
            if (Geo.Pieces(cut) != 1) Bad($"The {Lower(name)} comes out in more than one piece: make it bigger or the sheet thinner.");
            else if (Thin(cut) is > 0.05) Warn($"The {Lower(name)} has bits narrower than {Bridge:0.#} mm that may break: make it bigger, or use a thinner sheet.");
        }
        Parts.Add(part);
        return part;
    }

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    /// <summary>How much of the piece (mm²) is in strips narrower than <see cref="Bridge"/>.</summary>
    public static double Thin(PathsD cut)
    {
        double w = Bridge * 0.9;
        var opened = Geo.Offset(Geo.Offset(cut, -w / 2), w / 2);
        return Geo.Area(Geo.Subtract(cut, opened));
    }

    public BridgeKit Kit() => new(Spec, Parts, Findings);
}
