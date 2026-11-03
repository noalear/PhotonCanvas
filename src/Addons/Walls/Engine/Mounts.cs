using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;

namespace PhotonCanvas.Addon.Walls;

/// <summary>What plugs into a mount point.</summary>
public enum MountItem { None, Peg, Sconce, Shelf, Lamp }

/// <summary>
/// One mount point: one or more slots through the wall's face that an accessory plugs into. Each has its own
/// place, slots and accessory.
/// </summary>
public sealed record MountPoint
{
    /// <summary>The middle of its slots from the wall's left end (mm, along the panel).</summary>
    public double XMm { get; init; } = 50;
    /// <summary>The middle of its slots above the bottom of the wall (mm).</summary>
    public double YMm { get; init; } = 30;
    /// <summary>How many slots.</summary>
    public int Holes { get; init; } = 1;
    /// <summary>Slots side by side (else one above another). A shelf's are always side by side, a bracket in each.</summary>
    public bool Across { get; init; }
    /// <summary>From the middle of one slot to the next (mm).</summary>
    public double SpacingMm { get; init; } = 12;
    /// <summary>How wide each slot is (mm); 0 = the sheet's thickness and the fit, for the accessories cut with the wall.</summary>
    public double SlotWidthMm { get; init; }
    /// <summary>How tall each slot is (mm).</summary>
    public double SlotHeightMm { get; init; } = 6;
    /// <summary>On the wall's back (inside a room) rather than its front.</summary>
    public bool OnBack { get; init; }
    public MountItem Item { get; init; }

    /// <summary>Whether its slots are side by side.</summary>
    public bool SideBySide => Across || Item == MountItem.Shelf;
    public int Count => Math.Clamp(Holes, 1, 8);
    public double Spacing => Math.Max(1, SpacingMm);
    public double SlotH => Math.Clamp(SlotHeightMm, 2, 30);
    public double SlotW(WallSpec s) => SlotWidthMm > 0 ? Math.Clamp(SlotWidthMm, 0.5, 30) : s.ThicknessMm + Math.Max(0, s.FitMm);

    /// <summary>The middle of each slot.</summary>
    public IEnumerable<(double X, double Y)> Centres()
    {
        int n = Count;
        for (int k = 0; k < n; k++)
        {
            double o = (k - (n - 1) / 2.0) * Spacing;
            yield return SideBySide ? (XMm + o, YMm) : (XMm, YMm + o);
        }
    }

    /// <summary>The slots (wall coordinates, mm).</summary>
    public List<(double X0, double Y0, double X1, double Y1)> Slots(WallSpec s)
    {
        double w = SlotW(s), h = SlotH;
        return [.. Centres().Select(c => (c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2))];
    }

    /// <summary>Round all its slots.</summary>
    public (double X0, double Y0, double X1, double Y1) Bounds(WallSpec s)
    {
        var r = Slots(s);
        return (r.Min(q => q.X0), r.Min(q => q.Y0), r.Max(q => q.X1), r.Max(q => q.Y1));
    }
}

/// <summary>
/// Placing a wall's own list of mount points (<see cref="WallSpec.MountPoints"/>): their slots, checked against the
/// wall, its doors and windows and each other, and the accessories that plug into them, a shelf for each set of
/// shelf brackets.
/// </summary>
public static class Mounts
{
    /// <summary>A wall saved with only a number of mount points: the accessory the i-th one had.</summary>
    public static MountItem Legacy(Accessory a, int i) => a switch
    {
        Accessory.Pegs => MountItem.Peg,
        Accessory.Sconces => MountItem.Sconce,
        Accessory.Brackets => MountItem.Shelf,
        Accessory.Lamps => MountItem.Lamp,
        Accessory.OneOfEach => new[] { MountItem.Peg, MountItem.Sconce, MountItem.Shelf }[i % 3],
        _ => MountItem.None,
    };

    public static readonly IReadOnlyList<(MountItem Item, string Name)> Items =
    [
        (MountItem.None, "Just the slots"),
        (MountItem.Peg, "A peg (pictures, shields, banners)"),
        (MountItem.Sconce, "A torch sconce"),
        (MountItem.Shelf, "A shelf on brackets"),
        (MountItem.Lamp, "A wall lamp"),
    ];

    /// <summary>The mount points that fit, with every slot of each: the ones that don't are reported and left out.</summary>
    public static List<(MountPoint Mount, List<PathD> Slots)> Place(WallSpec s, double x0, double x1, double y0, Func<double, double, bool, double> topOver,
        List<(double X0, double X1, double Y0, double Y1)> taken, List<Finding> findings)
    {
        var placed = new List<(MountPoint, List<PathD>)>();
        var mine = new List<(int N, double X0, double X1, double Y0, double Y1)>();
        for (int i = 0; i < s.MountPoints.Count; i++)
        {
            var m = s.MountPoints[i];
            var b = m.Bounds(s);
            string who = $"Mount point {i + 1}";
            double top = topOver(b.X0, b.X1, m.OnBack) - 2;
            if (b.X0 < x0 || b.X1 > x1)
            {
                findings.Add(new(Severity.Warn, $"{who} is past the end of the wall: move it between {x0 + (m.XMm - b.X0):0.#} and {x1 - (b.X1 - m.XMm):0.#} mm along."));
                continue;
            }
            if (b.Y0 < y0 || b.Y1 > top)
            {
                findings.Add(new(Severity.Warn, $"{who} is too {(b.Y0 < y0 ? "low" : "high")}: move it between {y0 + (m.YMm - b.Y0):0.#} and {top - (b.Y1 - m.YMm):0.#} mm up."));
                continue;
            }
            if (taken.Any(t => t.X0 < b.X1 + 1 && t.X1 > b.X0 - 1 && t.Y0 < b.Y1 + 1 && t.Y1 > b.Y0 - 1))
            {
                findings.Add(new(Severity.Warn, $"{who} runs into a door, a window or the walkway's brackets: move it clear."));
                continue;
            }
            if (mine.FirstOrDefault(o => o.X0 < b.X1 + 1 && o.X1 > b.X0 - 1 && o.Y0 < b.Y1 + 1 && o.Y1 > b.Y0 - 1) is { N: > 0 } other)
            {
                findings.Add(new(Severity.Warn, $"{who} is on top of mount point {other.N}: move one of them."));
                continue;
            }
            if (m.Count > 1 && m.Spacing < (m.SideBySide ? m.SlotW(s) : m.SlotH) + 1.2)
            {
                findings.Add(new(Severity.Warn, $"{who}'s slots run into each other: space them further apart."));
                continue;
            }
            mine.Add((i + 1, b.X0, b.X1, b.Y0, b.Y1));
            placed.Add((m, [.. m.Slots(s).Select(r => Geo.Rect(r.X0, r.Y0, r.X1, r.Y1))]));
        }
        return placed;
    }

    /// <summary>
    /// The accessories for the placed mount points: one piece with a tab for each slot (or, for slots side by side,
    /// one each), shelf brackets and the shelf they hold. <paramref name="tabMm"/> is how far a tab goes into the
    /// wall; <paramref name="faceZ"/> where the front face's outside is (the back's is at −<paramref name="faceZ"/>).
    /// </summary>
    public static List<Part> Accessories(WallSpec s, IEnumerable<MountPoint> mounts, double tabMm, double faceZ, int walls)
    {
        double T = s.ThicknessMm, fit = Math.Max(0, s.FitMm), depth = Math.Clamp(s.ShelfDepthMm, 5, 60);
        var pieces = new List<(string Name, PathsD Cut, Pose Pose)>();
        foreach (var m in mounts)
        {
            if (m.Item == MountItem.None) continue;
            double h = m.SlotH, z = m.OnBack ? -faceZ : faceZ;
            Pose At(double x, double y) => m.OnBack ? Pose.Right(x, y, z) : Pose.Left(x, y, z);
            var centres = m.Centres().ToList();
            if (m.Item == MountItem.Shelf)
            {
                // A bracket in each slot, its top tab through a slot in the shelf lying on them.
                double tab0 = depth * 0.35, tab1 = tab0 + Math.Max(2.5, depth * 0.3);
                var bracket = Geo.Union(WallKitMaker.Bracket(tabMm, h, depth, 1), [Geo.Rect(tab0, h + 1 - 0.01, tab1, h + 1 + T - 0.1)]);
                foreach (var c in centres) pieces.Add(("Shelf bracket", bracket, At(c.X, c.Y - h / 2)));
                double margin = Math.Max(6, depth * 0.6), xs0 = centres.Min(c => c.X) - margin, xs1 = centres.Max(c => c.X) + margin, len = xs1 - xs0;
                // Drawn lying face up: u along the wall, v from its front edge back to the wall.
                var slots = new PathsD(centres.Select(c => Geo.Rect(c.X - xs0 - T / 2 - fit / 2, depth - tab1 - fit / 2, c.X - xs0 + T / 2 + fit / 2, depth - tab0 + fit / 2)));
                var shelf = Geo.Subtract([Geo.Rect(0, 0, len, depth)], slots);
                double y = centres[0].Y - h / 2 + h + 1 + T / 2;
                var pose = m.OnBack
                    ? new Pose(new V3(xs1, y, z - depth), -V3.UnitX, V3.UnitZ)
                    : Pose.Up(xs0, y, z + depth);
                pieces.Add(("Shelf", shelf, pose));
                continue;
            }
            string name = m.Item switch { MountItem.Peg => "Wall peg", MountItem.Sconce => "Torch sconce", _ => "Wall lamp" };
            PathsD One() => WallKitMaker.AccessoryPiece(m.Item, tabMm, h);
            if (m.SideBySide || centres.Count == 1)
            {
                foreach (var c in centres) pieces.Add((name, One(), At(c.X, c.Y)));
                continue;
            }
            // Slots one above another: one piece, its lowest slot's tab where a single one's would be, a tab for
            // each of the others and a plate on the wall joining them.
            double lowest = centres.Min(c => c.Y);
            var cut = new PathsD(One());
            foreach (var c in centres.Where(c => c.Y > lowest + 1e-6))
                cut.Add(WallKitMaker.MountTab(tabMm, h, c.Y - lowest + h / 2));
            cut.Add(Geo.Rect(0, -h / 2, 1.5, centres.Max(c => c.Y) - lowest + h / 2));
            pieces.Add((name, Geo.Union(cut), At(centres[0].X, lowest)));
        }

        // The same piece in several places is one part with every place it sits.
        var parts = new List<Part>();
        foreach (var g in pieces.GroupBy(p => (p.Name, Key(p.Cut))))
        {
            var first = g.First();
            var (etched, lines) = Etch(s, first.Name, first.Cut);
            parts.Add(new Part(first.Name, first.Cut, etched, lines, g.Count() * walls) { Poses = [.. g.Select(p => p.Pose)] });
        }
        return parts;
    }

    static string Key(PathsD p) => string.Join(";", Geo.Tidy(p).Select(path => string.Join(" ", path.Select(q => $"{q.x:0.###},{q.y:0.###}"))));

    /// <summary>The accessories' own texture (<see cref="WallSpec.AccessoryTexture"/>), at a smaller scale, kept off their tabs and slots.</summary>
    public static (PathsD Etched, List<PathD> Lines) Etch(WallSpec s, string name, PathsD cut)
    {
        if (Textures.Preset(s.AccessoryTexture) is not { } preset || preset.Name == Textures.Plain) return ([], []);
        var b = Geo.Bounds(cut);
        double k = name == "Shelf" ? 0.8 : 0.5;
        var t = s with
        {
            Texture = preset.Name, Custom = null, SizeA = Math.Max(1, preset.SizeA * k), SizeB = Math.Max(1, preset.SizeB * k),
            GrooveMm = Math.Max(0.3, preset.Groove * 0.7), Seed = s.Seed + name.Length * 17,
        };
        // On the wall side of an accessory (u < 0) is its tab, inside the wall.
        var region = Geo.Offset(name == "Shelf" ? cut : Geo.Intersect(cut, [Geo.Rect(0.4, b.top - 1, b.right + 1, b.bottom + 1)]), -0.4);
        if (region.Count == 0) return ([], []);
        double w = b.right - b.left, h = b.bottom - b.top;
        var moved = Geo.Move(region, -b.left, -b.top);
        var (e, l) = BoxWall.FaceTexture(t, 20, w, h, moved);
        return (Geo.Move(e, b.left, b.top), [.. l.Select(p => Geo.Map(p, q => new PointD(q.x + b.left, q.y + b.top)))]);
    }
}
