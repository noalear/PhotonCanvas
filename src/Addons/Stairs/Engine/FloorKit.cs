using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// One piece to cut (flat, mm, y up): its outline and holes, what's etched on it as areas, lines
/// scored on it, how many to cut, and where each one shown in the put-together view sits
/// (<see cref="Poses"/>, which may be fewer than the copies: extra storeys and spare ways up aren't shown).
/// </summary>
public sealed record Piece(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies, List<Pose> Poses);

/// <summary>Something that isn't cut here but is shown for where things go (the walls and their columns): an outline stood <see cref="Thick"/> mm thick.</summary>
public sealed record Ghost(string Name, PathsD Outline, Pose Pose, double Thick);

/// <summary>Where things ended up on the room's plan (mm from the first corner column's centre).</summary>
public sealed record FloorLayout(double RoomX, double RoomY, double WallThickMm, double FloorTopMm, double PlateBottomMm, PathsD Hatch, int Steps, double RiseMm);

/// <summary>Everything an upper floor and its way up are cut from, with what was found while making it.</summary>
public sealed record FloorKit(List<Piece> Parts, List<Finding> Findings, List<Ghost> Ghosts, FloorLayout Layout)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
}

/// <summary>Collects the pieces and findings while a kit is made.</summary>
sealed class KitBuilder(FloorSpec spec)
{
    /// <summary>The narrowest strip of sheet left beside a slot or between cut-outs (mm).</summary>
    public const double Bridge = 1.0;

    public FloorSpec Spec { get; } = spec;
    public double T { get; } = Math.Clamp(spec.SheetMm, 0.5, 20);
    public double Fit { get; } = Math.Clamp(spec.FitMm, -0.5, 2);
    public List<Piece> Parts { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<Ghost> Ghosts { get; } = [];

    public void Bad(string text) => Findings.Add(new(Severity.Bad, text));
    public void Warn(string text) => Findings.Add(new(Severity.Warn, text));
    public void Note(string text) => Findings.Add(new(Severity.Note, text));

    /// <summary>Adds a piece (its outline less its holes), checking it comes out whole and without thin bits.</summary>
    public Piece Add(string name, PathsD outline, PathsD holes, int copies, IEnumerable<Pose> poses, PathsD? etched = null, IEnumerable<PathD>? lines = null)
    {
        var cut = Geo.Subtract(Geo.Union(outline), Geo.Union(holes));
        var part = new Piece(name, cut, etched is null ? [] : Geo.Intersect(Geo.Union(etched), cut), lines is null ? [] : Geo.ClipLines(lines, cut), Math.Max(1, copies), [.. poses]);
        string lower = name.ToLowerInvariant();
        if (Geo.Pieces(cut) != 1) Bad($"The {lower} comes out in more than one piece: change its size or the opening.");
        else if (Thin(cut) is > 0.05) Warn($"The {lower} has bits narrower than {Bridge:0.#} mm that may break: make it bigger, or use a thinner sheet.");
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

    /// <summary>
    /// The chosen texture over an area (a floor or a step), using the wall maker's textures: etched as
    /// areas (grooves) or scored as lines. <paramref name="across"/> lays it a quarter turn round.
    /// </summary>
    public (PathsD Etched, List<PathD> Lines) Texture(PathsD region, bool across, int seedOffset = 0)
    {
        var s = Spec;
        if (region.Count == 0 || s.Texture == Textures.Plain) return ([], []);
        if (Textures.Preset(s.Texture) is null && (s.Custom is null || s.Custom.Name != s.Texture)) return ([], []);
        var b = Geo.Bounds(region);
        double w = b.right - b.left, h = b.bottom - b.top;
        if (w <= 0 || h <= 0) return ([], []);
        var ws = new WallSpec
        {
            Texture = s.Texture, Custom = s.Custom, SizeA = s.SizeA, SizeB = s.SizeB, GrooveMm = s.GrooveMm,
            Seed = s.Seed + seedOffset, Etch = s.Etch, Connectors = [],
        };
        double fl = across ? h : w, fh = across ? w : h;
        var tex = Textures.Make(ws, new Face(fl, fh, 0, fl, fh));
        // Texture (tx, ty) → the region: straight, or with x and y swapped (a quarter turn and a flip, which a texture doesn't mind).
        PointD Map(PointD p) => across ? new PointD(b.left + p.y, b.top + p.x) : new PointD(b.left + p.x, b.top + p.y);
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-1, -1, fl + 1, fh + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Tidy(Geo.Map(Geo.Union(mortar, tex.Etched), Map)), region));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(Geo.Map(tex.Raised.Select(Geo.Loop), Map), region));
            lines.AddRange(Geo.ClipLines(Geo.Map(tex.Etched.Select(Geo.Loop), Map), region));
        }
        lines.AddRange(Geo.ClipLines(Geo.Map(tex.Lines, Map), region));
        return (etched, lines);
    }
}
