using Clipper2Lib;

namespace PhotonCanvas.Addon.Boxes;

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);

/// <summary>
/// One piece to cut (flat, mm, y up): its outline and holes, open cut lines (a living hinge's
/// slits), what's carved on it, and where it sits in the box. A flat piece has one or more
/// <see cref="Poses"/> (copies); a piece that bends has <see cref="Facets"/> instead: strips of its
/// flat outline, each laid where that stretch goes once it's bent.
/// </summary>
public sealed record Part(string Name, PathsD Cut, List<PathD> Slits, List<Pose> Poses)
{
    /// <summary>For a piece that bends: each stretch of it (an area in its flat coordinates) and where it goes.</summary>
    public List<(PathsD Clip, Pose Pose)>? Facets { get; init; }
    /// <summary>0 the box; 1 the lid and what's fixed to it (moves when the lid is opened in the 3D view).</summary>
    public int Group { get; init; }
    /// <summary>On the lid's top: the area (flat coordinates) a carved border goes round.</summary>
    public RectD? LidArea { get; init; }
    /// <summary>Stretches of v (up the flat piece) a seam across it mustn't fall in (a living hinge), if it has to be cut in pieces.</summary>
    public List<(double Lo, double Hi)> NoSeam { get; init; } = [];
    public int Copies => Facets is not null ? 1 : Math.Max(1, Poses.Count);
}

/// <summary>How a box's lid opens in the 3D view: turned about a line, or moved.</summary>
public sealed record LidMove(V3 Through, V3 Axis, double Degrees, V3 Shift)
{
    public static readonly LidMove None = new(default, V3.UnitX, 0, default);
    public Pose Apply(Pose p, double amount) => (Degrees != 0 ? p.Turned(Through, Axis, Degrees * amount) : p).Moved(Shift * amount);
}

/// <summary>Every piece of a box, its size outside, what was found while making it, and how its lid opens.</summary>
public sealed record BoxKit(List<Part> Parts, List<Finding> Findings, double ThicknessMm, V3 Outside, LidMove Lid)
{
    public bool Ok => Parts.Count > 0 && !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
}
