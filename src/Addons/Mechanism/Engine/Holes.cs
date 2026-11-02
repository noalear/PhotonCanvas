using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>The shape of a shaft, and of the hole it goes through.</summary>
public enum HoleShape { Round, D, Square }

/// <summary>
/// How tight a hole is on its shaft. Press and Snug hold the part on the shaft (it turns with it); Slip and
/// Free let it turn on the shaft (Slip with less wobble). A D or square hole always turns with its shaft.
/// </summary>
public enum HoleFit { Press, Snug, Slip, Free }

/// <summary>A shaft or dowel: round, round with a flat (a D, like a motor shaft) or square.</summary>
public sealed record Shaft(HoleShape Shape, double SizeMm, double FlatMm)
{
    /// <summary>Across the flat of a D: the given one, or 0.5 mm off the size (a motor shaft's) when not given.</summary>
    public double Flat => Shape == HoleShape.D ? (FlatMm > 0 && FlatMm < SizeMm ? FlatMm : Math.Max(SizeMm * 0.5, SizeMm - 0.5)) : SizeMm;

    /// <summary>How far its edge reaches from its middle (its corners, for a square).</summary>
    public double OuterR => Shape == HoleShape.Square ? SizeMm * Math.Sqrt(2) / 2 : SizeMm / 2;

    /// <summary>Its outline grown by <paramref name="allowance"/> across (a hole for it), at <paramref name="at"/>, turned to <paramref name="angle"/>. A D's flat faces +x before turning.</summary>
    public Path64 Outline(Vec2 at, double angle = 0, double allowance = 0)
    {
        double s = Math.Max(0.2, SizeMm + allowance);
        var pose = new Pose(at, angle);
        switch (Shape)
        {
            case HoleShape.Square:
            {
                double h = s / 2;
                return Poly.Path(new[] { new Vec2(-h, -h), new Vec2(h, -h), new Vec2(h, h), new Vec2(-h, h) }.Select(pose.Apply));
            }
            case HoleShape.D:
            {
                double r = s / 2;
                double f = Math.Clamp(Flat - SizeMm / 2 + allowance / 2, -r * 0.9, r * 0.98);
                double a0 = Math.Acos(f / r);
                int n = Math.Max(24, Poly.Segments(r, 0.003));
                var pts = new List<Vec2>();
                for (int i = 0; i <= n; i++) pts.Add(Shapes.Polar(r, a0 + (2 * Math.PI - 2 * a0) * i / n));
                return Poly.Path(pts.Select(pose.Apply));
            }
            default:
                return Poly.Circle(at, s / 2, 0.003);
        }
    }

    /// <summary>A round hole it turns freely in (round, whatever its shape, and big enough for its corners).</summary>
    public Path64 TurningHole(Vec2 at, string material = "") => Poly.Circle(at, OuterR + Fits.Allowance(HoleFit.Free, material) / 2, 0.003);

    /// <summary>"6 mm", "5 mm D", "6 mm square": short, for a part's name.</summary>
    public string Short => Shape switch { HoleShape.Square => $"{SizeMm:0.#} mm square", HoleShape.D => $"{SizeMm:0.#} mm D", _ => $"{SizeMm:0.#} mm" };

    public string Words => Shape switch
    {
        HoleShape.Square => $"{SizeMm:0.#} mm square",
        HoleShape.D => $"{SizeMm:0.#} mm D-shaped ({Flat:0.##} mm across the flat)",
        _ => $"{SizeMm:0.#} mm",
    };
}

/// <summary>A hole for a shaft: its shape and size (the shaft's), and how tight it is.</summary>
public sealed record Hole
{
    public HoleShape Shape { get; init; } = HoleShape.Round;
    /// <summary>The shaft's size: a round or D shaft's diameter, a square one's side (mm).</summary>
    public double SizeMm { get; init; } = 6;
    /// <summary>A D shaft's width across its flat (mm); 0 = 0.5 mm less than its size.</summary>
    public double FlatMm { get; init; }
    public HoleFit Fit { get; init; } = HoleFit.Free;
    /// <summary>A nudge added across the hole after the fit (mm): + is looser. For dowels that aren't quite their size.</summary>
    public double AdjustMm { get; init; }

    public Shaft Shaft => new(Shape, SizeMm, FlatMm);

    /// <summary>The part turns on the shaft (a round hole with a slip or free fit); otherwise it turns with it.</summary>
    public bool Turns => Shape == HoleShape.Round && Fit is HoleFit.Slip or HoleFit.Free;

    /// <summary>How much bigger than the shaft the hole is drawn, across (mm; less than 0 is tighter), in this material.</summary>
    public double Allowance(string material) => Fits.Allowance(Fit, material) + AdjustMm;

    /// <summary>The hole, drawn for the material it's cut from (the kerf is allowed for on the cut layer, so it comes out this size).</summary>
    public Path64 Cut(Vec2 at, string material, double angle = 0) => Shaft.Outline(at, angle, Allowance(material));

    /// <summary>How far the hole reaches from its middle.</summary>
    public double OuterR(string material) => Shape == HoleShape.Square ? (SizeMm + Allowance(material)) * Math.Sqrt(2) / 2 : (SizeMm + Allowance(material)) / 2;

    /// <summary>"6 mm round, free (6.3 mm)".</summary>
    public string Words(string material)
    {
        double across = SizeMm + Allowance(material);
        return $"{(Shape == HoleShape.Round ? $"{SizeMm:0.#} mm round" : Shaft.Words)}, {Fits.Name(Fit).ToLowerInvariant()} fit ({(Shape == HoleShape.Square ? $"{across:0.##} mm square" : $"{across:0.##} mm across")})";
    }

    /// <summary>A hole for <paramref name="axle"/>: one it turns in, or one that holds it.</summary>
    public static Hole For(Axle axle, HoleFit fit) => new() { Shape = axle.Square ? HoleShape.Square : HoleShape.Round, SizeMm = axle.SizeMm, Fit = fit };
}

/// <summary>
/// How much bigger (or smaller) than its shaft a hole is drawn for each fit, across, in mm. Wood and other soft
/// sheets squash round a shaft, so they take a tighter press; acrylic and other stiff plastics crack, so
/// they get less. These are for holes cut at their drawn size (the material's kerf set on the cut layer).
/// </summary>
public static class Fits
{
    public static readonly HoleFit[] All = [HoleFit.Press, HoleFit.Snug, HoleFit.Slip, HoleFit.Free];

    /// <summary>A stiff sheet: acrylic, plastic, glass, stone, metal.</summary>
    public static bool Stiff(string material) => material is "Acrylic" or "Plastic" or "Glass" or "Stone & tile" or "Metal" or "Coated metal";

    public static double Allowance(HoleFit fit, string material) => Stiff(material)
        ? fit switch { HoleFit.Press => -0.08, HoleFit.Snug => -0.02, HoleFit.Slip => 0.08, _ => 0.25 }
        : fit switch { HoleFit.Press => -0.2, HoleFit.Snug => -0.06, HoleFit.Slip => 0.1, _ => 0.3 };

    public static string Name(HoleFit fit) => fit switch
    {
        HoleFit.Press => "Press",
        HoleFit.Snug => "Snug",
        HoleFit.Slip => "Slip",
        _ => "Free",
    };

    /// <summary>What each fit does, for the picker.</summary>
    public static string Words(HoleFit fit) => fit switch
    {
        HoleFit.Press => "Press: tap it on; holds without glue",
        HoleFit.Snug => "Snug: push it on; glue it to be sure",
        HoleFit.Slip => "Slip: slides on, turns on it with little wobble",
        _ => "Free: turns easily on it",
    };
}

/// <summary>A handle on a gear or cam: a hole off its middle for a peg (a crank pin) to turn it by.</summary>
public sealed record Crank
{
    public bool On { get; init; } = true;
    /// <summary>How far from the gear's middle the hole sits (mm); 0 = as far out as there's room for.</summary>
    public double RadiusMm { get; init; }
    /// <summary>The peg's hole; null = a snug round one for a dowel the size of the axle (4 mm at least).</summary>
    public Hole? Hole { get; init; }

    /// <summary>The peg's hole, for parts on <paramref name="axle"/>.</summary>
    public Hole PegHole(Axle axle) => Hole ?? new Hole { SizeMm = Math.Max(4, axle.SizeMm), Fit = HoleFit.Snug };

    /// <summary>
    /// Where the peg's hole goes, on +x: at the radius asked for, or as far out as fits. Null, with why, when it
    /// doesn't fit between <paramref name="innerR"/> (the shaft's hole) and <paramref name="outerR"/> (the rim).
    /// </summary>
    public (Vec2? At, string? Why) Place(double innerR, double outerR, double pegR, string what)
    {
        double lo = innerR + pegR + 1.5, hi = outerR - pegR;
        if (hi < lo) return (null, $"{what} is too small for a handle hole that size: turn it by its axle, use a smaller peg, or give it more {(what.StartsWith("The cam", StringComparison.Ordinal) ? "size" : "teeth")}.");
        if (RadiusMm <= 0) return (Shapes.Polar(hi, 0), null);
        if (RadiusMm < lo - 1e-6 || RadiusMm > hi + 1e-6)
            return (null, $"{what}'s handle hole at {RadiusMm:0.#} mm from the middle {(RadiusMm < lo ? "runs into the axle hole" : "cuts into the rim")}: it fits between {Math.Ceiling(lo * 10) / 10:0.#} and {Math.Floor(hi * 10) / 10:0.#} mm.");
        return (Shapes.Polar(RadiusMm, 0), null);
    }
}

/// <summary>
/// A part's hole for its shaft, worked out: the hole chosen for it, or (none chosen) the mechanism's usual one
/// for its axle, which a part turns on or grips as the mechanism needs.
/// </summary>
public sealed class Bore
{
    readonly Hole? _hole;
    readonly Axle _axle;
    readonly bool _turnsByDefault;
    readonly string _material;

    public Bore(Hole? hole, Axle axle, bool turnsByDefault, string material)
    {
        _hole = hole;
        _axle = axle;
        _turnsByDefault = turnsByDefault;
        _material = material;
    }

    /// <summary>A hole was chosen for this part (not just the axle's usual one).</summary>
    public bool Chosen => _hole is not null;
    public Hole? Hole => _hole;
    /// <summary>The shaft through it.</summary>
    public Shaft Shaft => _hole?.Shaft ?? _axle.Shaft;
    /// <summary>The part turns on its shaft (otherwise the shaft turns with it).</summary>
    public bool Turns => _hole?.Turns ?? _turnsByDefault;
    /// <summary>How far the hole reaches from the middle (for room round it).</summary>
    public double R => _hole?.OuterR(_material) ?? (_axle.Square ? _axle.SizeMm * Math.Sqrt(2) / 2 + 0.15 : _axle.SizeMm / 2 + 0.15);

    /// <summary>The hole in the part, at <paramref name="at"/>, turned to <paramref name="angle"/> (a square or D hole sets the part's timing on its shaft).</summary>
    public Path64 Cut(Vec2 at, double angle = 0) => _hole is { } h ? h.Cut(at, _material, angle) : _turnsByDefault ? _axle.TurningHole(at) : _axle.HeldHole(at, angle);

    /// <summary>The hole for the same shaft in a frame (a plate, a backboard, a side): one it turns in, or one that grips it.</summary>
    public Path64 Frame(Vec2 at, bool shaftTurns) => _hole is null
        ? shaftTurns ? _axle.TurningHole(at) : _axle.HeldHole(at)
        : shaftTurns ? Shaft.TurningHole(at, _material) : Shaft.Outline(at, 0, Fits.Allowance(HoleFit.Snug, _material));

    /// <summary>The shaft itself, drawn in the motion view.</summary>
    public Path64 ShaftOutline() => _hole is null ? (_axle.Square ? _axle.HeldHole(default) : Poly.Circle(default, _axle.SizeMm / 2, 0.01)) : Shaft.Outline(default);

    public string ShaftWords => _hole is null ? _axle.Words : _hole.Shaft.Words;

    /// <summary>"6.3 mm round (free fit)", for the list of holes as drawn.</summary>
    public string Words => _hole?.Words(_material) ?? "";
}

/// <summary>Words about the sheet for the findings.</summary>
static class MaterialWords
{
    public static string Of(string material) => string.IsNullOrEmpty(material) ? "wood" : material.ToLowerInvariant();
}
