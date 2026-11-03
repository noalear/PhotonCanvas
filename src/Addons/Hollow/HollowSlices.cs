using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Hollow;

/// <summary>How a stacked model is hollowed.</summary>
public sealed record HollowOptions
{
    /// <summary>
    /// How thick the shell is (mm), sideways and at the top and bottom: the hollow keeps at least
    /// this far from the model's surface. The top and bottom are a whole number of slices this thick or more.
    /// </summary>
    public double WallMm { get; init; } = 5;
    /// <summary>Leave the bottom open (a lamp shade, a cover over a light): the hollow runs out through the base.</summary>
    public bool OpenBottom { get; init; }
    /// <summary>Leave the top open (a pot, a bowl).</summary>
    public bool OpenTop { get; init; }
    /// <summary>A hole up through the solid base into the hollow (mm across), for a wire or to push an LED in. 0 = none.</summary>
    public double ChannelMm { get; init; }
    /// <summary>Pockets in the base for round magnets (mm across), so the model sticks to a fridge or a base. 0 = none.</summary>
    public double MagnetMm { get; init; }
    /// <summary>How thick the magnets are (mm): the pockets go up through as many slices as that takes.</summary>
    public double MagnetThickMm { get; init; } = 3;
    /// <summary>How many magnets, spread out across the base.</summary>
    public int Magnets { get; init; } = 2;
    /// <summary>Hollows smaller than this (mm²) aren't worth cutting out; 0 = the wall squared.</summary>
    public double MinHollowMm2 { get; init; }
}

/// <summary>What hollowing did, kept on the slice stack under <see cref="HollowSlices.Key"/>.</summary>
public sealed record HollowReport
{
    /// <summary>Material in the parts before and after (mm², summed over every slice).</summary>
    public double SolidMm2 { get; init; }
    public double HollowedMm2 { get; init; }
    /// <summary>Slices with a hollow in them.</summary>
    public int HollowSliceCount { get; init; }
    /// <summary>Strips left across a hollow to keep a part in one piece (round a dowel, or where a slice is a ring).</summary>
    public int Bridges { get; init; }
    /// <summary>Where the channel goes up through the base, and how many slices it goes through; null = none.</summary>
    public Vec2? ChannelAt { get; init; }
    public int ChannelSlices { get; init; }
    /// <summary>The magnet pockets, and how many slices deep they go.</summary>
    public IReadOnlyList<Vec2> MagnetsAt { get; init; } = [];
    public int MagnetSlices { get; init; }
    /// <summary>The share of the material saved (0 to 1).</summary>
    public double Saved => SolidMm2 <= 0 ? 0 : 1 - HollowedMm2 / SolidMm2;
}

/// <summary>
/// Hollow stacked models: each slice keeps a shell round the outside and the middle is cut out, so
/// the model takes less material and less glue, and is lighter. The hollow keeps the wall's
/// thickness from the surface above and below too, so the top and bottom are closed (unless asked
/// open). Parts stay in one piece (bridges where the hollow would cut one in two), the dowels keep
/// solid material round them, and there can be a channel up through the base for a wire or LED and
/// pockets in the base for magnets. Runs before the pins go in, so the pins land in the walls.
/// </summary>
public sealed class HollowSlices(HollowOptions options) : ISliceModifier
{
    /// <summary>The key the <see cref="HollowReport"/> is kept under in <see cref="SliceStack.Extras"/>.</summary>
    public const string Key = "hollow";

    public HollowOptions Options { get; } = options;

    public void Modify(SliceWork work, CancellationToken ct)
    {
        var o = Options;
        if (!(o.WallMm > 0)) throw new ArgumentException("The wall must be thicker than zero.");
        var layers = work.Layers;
        int n = layers.Count;
        double slab = work.SlabMm, wall = o.WallMm;
        double before = work.Parts.Sum(l => l.Sum(StackSlicer.Area));
        // How many slices above and below the hollow must keep clear of the surface.
        int k = Math.Max(1, (int)Math.Ceiling(wall / slab - 1e-6));
        var inset = layers.Select(l => l.IsEmpty ? PathGeometry.Empty : ShapeGeometry.Offset(l, -wall, JoinStyle.Round, EndStyle.Round)).ToList();
        double minHollow = o.MinHollowMm2 > 0 ? o.MinHollowMm2 : wall * wall;
        var hollow = new PathGeometry[n];
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var c = inset[i];
            for (int j = i - k; j <= i + k && !c.IsEmpty; j++)
            {
                if (j == i) continue;
                if (j < 0) { if (o.OpenBottom) continue; c = PathGeometry.Empty; break; }
                if (j >= n) { if (o.OpenTop) continue; c = PathGeometry.Empty; break; }
                c = ShapeGeometry.Boolean(c, inset[j], BoolOp.Intersect);
            }
            // Slivers and specks aren't worth a cut.
            if (!c.IsEmpty) c = ShapeGeometry.Offset(ShapeGeometry.Offset(c, -0.3, JoinStyle.Round, EndStyle.Round), 0.3, JoinStyle.Round, EndStyle.Round);
            var keep = c.IsEmpty ? [] : StackSlicer.Islands(c).Where(g => StackSlicer.Area(g) >= minHollow).ToList();
            hollow[i] = keep.Count == 0 ? PathGeometry.Empty : new PathGeometry(keep.SelectMany(g => g.Subpaths));
        }
        // Solid round the dowels, so the holes have a wall.
        var dowelMm = work.Options.DowelMm;
        var solid = work.Dowels.Select(d => (d, r: dowelMm / 2 + wall / 2)).Concat(work.Holes.Select(h => (d: h.At, r: h.DiameterMm / 2 + wall / 2))).ToList();
        // The channel: up through the solid base into the lowest hollow, at its roomiest spot.
        Vec2? channel = null;
        int channelSlices = 0;
        int lowest = Array.FindIndex(hollow, h => !h.IsEmpty);
        if (o.ChannelMm > 0 && lowest > 0)
        {
            var room = ShapeGeometry.Offset(hollow[lowest], -(o.ChannelMm / 2), JoinStyle.Round, EndStyle.Round);
            // Straight down through solid all the way (legs and feet don't line up with the body above them).
            for (int j = 0; j < lowest && !room.IsEmpty; j++)
                room = layers[j].IsEmpty ? PathGeometry.Empty : ShapeGeometry.Boolean(room, ShapeGeometry.Offset(layers[j], -(o.ChannelMm / 2 + 1.5), JoinStyle.Round, EndStyle.Round), BoolOp.Intersect);
            foreach (var (d, r) in solid) room = room.IsEmpty ? room : ShapeGeometry.Boolean(room, PathGeometry.Ellipse(d.X, d.Y, r + o.ChannelMm / 2, r + o.ChannelMm / 2), BoolOp.Subtract);
            var loops = room.IsEmpty ? [] : StackSlicer.Loops(room);
            if (loops.Count > 0 && StackSlicer.Pole(loops) is { Room: > 0 } pole)
            {
                channel = pole.At;
                channelSlices = lowest;
            }
        }
        // The magnets: pockets up through the bottom slices, under solid material, spread out.
        var magnets = new List<Vec2>();
        int magnetSlices = o.MagnetMm > 0 ? Math.Max(1, (int)Math.Ceiling(o.MagnetThickMm / slab - 1e-6)) : 0;
        if (o.MagnetMm > 0 && o.Magnets > 0 && magnetSlices < n && !layers[0].IsEmpty)
        {
            // In every slice it goes through, and with solid above it.
            var area = layers[0];
            for (int j = 1; j < magnetSlices && !area.IsEmpty; j++) area = ShapeGeometry.Boolean(area, layers[j], BoolOp.Intersect);
            for (int j = 0; j <= magnetSlices && !area.IsEmpty; j++)
                if (!hollow[j].IsEmpty) area = ShapeGeometry.Boolean(area, ShapeGeometry.Offset(hollow[j], 1.2, JoinStyle.Round, EndStyle.Round), BoolOp.Subtract);
            if (!area.IsEmpty) area = ShapeGeometry.Offset(area, -(o.MagnetMm / 2 + 1.5), JoinStyle.Round, EndStyle.Round);
            var avoid = solid.Select(s => (s.d, s.Item2 + o.MagnetMm / 2)).ToList();
            if (channel is { } ch) avoid.Add((ch, o.ChannelMm / 2 + o.MagnetMm / 2 + 1.5));
            if (!area.IsEmpty && avoid.Count > 0)
                area = ShapeGeometry.Boolean(area, new PathGeometry(avoid.SelectMany(a => PathGeometry.Ellipse(a.Item1.X, a.Item1.Y, a.Item2, a.Item2).Subpaths)), BoolOp.Subtract);
            magnets = Spread(area, o.Magnets, o.MagnetMm + 3);
        }

        int bridges = 0, hollowSlices = 0;
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cut = new List<PathGeometry>();
            var h = hollow[i];
            if (!h.IsEmpty && solid.Count > 0)
                h = ShapeGeometry.Boolean(h, new PathGeometry(solid.SelectMany(s => PathGeometry.Ellipse(s.d.X, s.d.Y, s.Item2, s.Item2).Subpaths)), BoolOp.Subtract);
            if (!h.IsEmpty) cut.Add(h);
            if (channel is { } c && i < channelSlices) cut.Add(PathGeometry.Ellipse(c.X, c.Y, o.ChannelMm / 2, o.ChannelMm / 2));
            if (i < magnetSlices) cut.AddRange(magnets.Select(m => PathGeometry.Ellipse(m.X, m.Y, o.MagnetMm / 2, o.MagnetMm / 2)));
            if (cut.Count == 0) continue;
            var remove = cut.Count == 1 ? cut[0] : ShapeGeometry.Union(cut);
            bool any = false;
            var parts = new List<PathGeometry>();
            foreach (var part in work.Parts[i])
            {
                var left = ShapeGeometry.Boolean(part, remove, BoolOp.Subtract);
                if (StackSlicer.Area(left) >= StackSlicer.Area(part) - 0.01) { parts.Add(part); continue; }
                any |= !h.IsEmpty && StackSlicer.Area(ShapeGeometry.Boolean(part, h, BoolOp.Intersect)) > 0.01;
                var (whole, added) = KeepInOnePiece(part, left, wall);
                bridges += added;
                parts.Add(whole);
            }
            if (any) hollowSlices++;
            work.Parts[i] = parts;
        }
        double after = work.Parts.Sum(l => l.Sum(StackSlicer.Area));
        var report = new HollowReport
        {
            SolidMm2 = before, HollowedMm2 = after, HollowSliceCount = hollowSlices, Bridges = bridges,
            ChannelAt = channel, ChannelSlices = channel is null ? 0 : channelSlices, MagnetsAt = magnets, MagnetSlices = magnets.Count > 0 ? magnetSlices : 0,
        };
        work.Extras[Key] = report;
        work.Notes.Add(Describe(report, o));
    }

    /// <summary>What hollowing did, in a sentence or two.</summary>
    public static string Describe(HollowReport r, HollowOptions o)
    {
        if (r.HollowSliceCount == 0)
            return $"Too small to hollow with a {o.WallMm:0.#} mm wall: every slice stays solid.";
        var s = $"Hollowed with a {o.WallMm:0.#} mm wall: {r.HollowSliceCount} slice{(r.HollowSliceCount == 1 ? " has" : "s have")} the middle cut out, saving {r.Saved:P0} of the material (keep the cut-out middles for small parts).";
        if (o.OpenBottom) s += " The bottom is open.";
        if (o.OpenTop) s += " The top is open.";
        if (r.Bridges > 0) s += $" {r.Bridges} bridge{(r.Bridges == 1 ? "" : "s")} across the hollow keep{(r.Bridges == 1 ? "s" : "")} parts in one piece.";
        if (o.ChannelMm > 0)
            s += r.ChannelAt is null
                ? (o.OpenBottom ? " No channel needed: the bottom is open." : " No room for the channel: no straight way up through the base into the hollow.")
                : $" A {o.ChannelMm:0.#} mm channel goes up through the bottom {r.ChannelSlices} slice{(r.ChannelSlices == 1 ? "" : "s")} into the hollow, for a wire or LED.";
        if (o.MagnetMm > 0 && o.Magnets > 0)
            s += r.MagnetsAt.Count == 0
                ? $" No room for {o.MagnetMm:0.#} mm magnet pockets in the base."
                : $" {r.MagnetsAt.Count} magnet pocket{(r.MagnetsAt.Count == 1 ? "" : "s")} of {o.MagnetMm:0.#} mm in the bottom {r.MagnetSlices} slice{(r.MagnetSlices == 1 ? "" : "s")}: glue the magnets in before the slice above goes on.";
        return s;
    }

    /// <summary>
    /// A part with its middle cut out can fall into pieces (a ring round a dowel's solid, a slice
    /// that was already a ring): join each loose piece to the rest with a strip as wide as the wall,
    /// across the hollow at its narrowest, inside the original part.
    /// </summary>
    static (PathGeometry Part, int Bridges) KeepInOnePiece(PathGeometry original, PathGeometry left, double wall)
    {
        int added = 0;
        for (int guard = 0; guard < 64; guard++)
        {
            var pieces = StackSlicer.Islands(left);
            if (pieces.Count <= 1) return (left, added);
            var main = pieces.MaxBy(StackSlicer.Area)!;
            var mainPts = Outline(main);
            // The loose piece nearest the main one, and where they're closest.
            (Vec2 A, Vec2 B, double D) best = (default, default, double.PositiveInfinity);
            foreach (var p in pieces.Where(p => !ReferenceEquals(p, main)))
            {
                var pts = Outline(p);
                foreach (var a in pts)
                    foreach (var b in mainPts)
                        if (a.Dist(b) is var d && d < best.D) best = (a, b, d);
            }
            if (double.IsInfinity(best.D)) return (left, added);
            // A little past each end so the strip overlaps both pieces.
            var dir = best.D > 1e-9 ? (best.B - best.A) / best.D : new Vec2(1, 0);
            var strip = ShapeGeometry.Offset(PathGeometry.Line(best.A - dir * (wall / 2), best.B + dir * (wall / 2)), wall / 2, JoinStyle.Round, EndStyle.Round);
            strip = ShapeGeometry.Boolean(strip, original, BoolOp.Intersect);
            var joined = ShapeGeometry.Union([left, strip]);
            if (StackSlicer.Islands(joined).Count >= pieces.Count) return (joined, added + 1); // couldn't join: leave it as is
            left = joined;
            added++;
        }
        return (left, added);
    }

    /// <summary>Points round a part's edges (every loop), spaced about a millimetre apart.</summary>
    static List<Vec2> Outline(PathGeometry g)
    {
        var o = new List<Vec2>();
        foreach (var loop in StackSlicer.Loops(g))
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                int steps = Math.Max(1, (int)Math.Ceiling(a.Dist(b)));
                for (int s = 0; s < steps; s++) o.Add(Vec2.Lerp(a, b, (double)s / steps));
            }
        return o;
    }

    /// <summary>Up to <paramref name="count"/> points inside an area, as far apart as can be (at least <paramref name="apart"/>).</summary>
    static List<Vec2> Spread(PathGeometry area, int count, double apart)
    {
        if (area.IsEmpty) return [];
        var loops = area.Flatten(0.1).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        if (loops.Count == 0) return [];
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        double cell = Math.Max(0.25, Math.Max(box.Width, box.Height) / 60);
        var inside = new List<Vec2>();
        for (double x = box.MinX; x <= box.MaxX; x += cell)
            for (double y = box.MinY; y <= box.MaxY; y += cell)
                if (loops.Count(l => Geom.Inside(l, new Vec2(x, y))) % 2 == 1) inside.Add(new Vec2(x, y));
        if (inside.Count == 0) return [];
        // The first as far from the middle as it gets, then each as far from those chosen as it gets.
        var centre = new Vec2(inside.Average(p => p.X), inside.Average(p => p.Y));
        var chosen = new List<Vec2>();
        if (count == 1) { chosen.Add(inside.MinBy(p => p.Dist(centre))); return chosen; }
        chosen.Add(inside.MaxBy(p => p.Dist(centre)));
        while (chosen.Count < count)
        {
            var next = inside.MaxBy(p => chosen.Min(c => c.Dist(p)));
            if (chosen.Min(c => c.Dist(next)) < apart) break;
            chosen.Add(next);
        }
        return chosen;
    }
}
