using PhotonCanvas.Core;
using PhotonCanvas.Doc.Import;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// The outline is the same all through a cut slice, but the model's isn't: which one to cut.
/// </summary>
public enum SliceOutline
{
    /// <summary>The model's shape half-way through the slice: steps stick out as much as they cut in.</summary>
    Middle,
    /// <summary>Everything the model covers anywhere in the slice: sand the steps back to the shape.</summary>
    Outer,
    /// <summary>Only what the model covers all through the slice: nothing sticks out, fill the steps.</summary>
    Inner,
}

/// <summary>How the model is built from flat sheets.</summary>
public enum SliceMethod
{
    /// <summary>Flat layers glued one on another: solid, stepped.</summary>
    Stacked,
    /// <summary>Upright sheets across X and across Y, slotted together where they cross (an egg crate).</summary>
    EggCrate,
    /// <summary>Upright fins round a central axis, slotted onto flat rings at a few heights.</summary>
    Radial,
}

/// <summary>Where a flat part sits in the model: its outline's (u, v) map to Origin + u·U + v·V, its thickness along N.</summary>
public readonly record struct Frame3(V3 Origin, V3 U, V3 V, V3 N)
{
    public V3 At(double u, double v, double s) => new(Origin.X + u * U.X + v * V.X + s * N.X, Origin.Y + u * U.Y + v * V.Y + s * N.Y, Origin.Z + u * U.Z + v * V.Z + s * N.Z);
}

/// <summary>Which way up the model stands while it's sliced.</summary>
public enum SliceUp { Z, MinusZ, Y, MinusY, X, MinusX }

public sealed record SliceOptions
{
    /// <summary>The material's thickness: one slice each.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>The model's height (along "up") in mm; the other sizes follow.</summary>
    public double HeightMm { get; init; } = 60;
    /// <summary>The model's width (X) and depth (Y) standing this way up, mm; 0 = in proportion to the height.</summary>
    public double WidthMm { get; init; }
    public double DepthMm { get; init; }
    public SliceUp Up { get; init; } = SliceUp.Z;
    /// <summary>Turn round the up direction (degrees), to lay the slices out differently.</summary>
    public double TurnDeg { get; init; }
    public SliceOutline Outline { get; init; } = SliceOutline.Middle;
    /// <summary>Cross-sections taken through each slice for Outer and Inner.</summary>
    public int Samples { get; init; } = 5;
    /// <summary>Dowel holes through every slice for lining them up (0 = none).</summary>
    public double DowelMm { get; init; }
    /// <summary>
    /// Pin holes between each pair of neighbouring slices (0 = none): wherever a part sits on the
    /// part below it, both get matching holes, so a short pin through both lines them up.
    /// </summary>
    public double PinMm { get; init; }
    /// <summary>
    /// Where the model is joined but two slices barely touch (steep slopes: a trunk, an ear), add
    /// the shape where they meet to both, so they overlap enough to glue and pin.
    /// </summary>
    public bool KeepJoined { get; init; } = true;
    /// <summary>The least overlap (mm²) two joined parts should have to glue them.</summary>
    public double MinJoinMm2 { get; init; } = 6;
    public SliceMethod Method { get; init; } = SliceMethod.Stacked;
    /// <summary>Egg crate: sheets across X (cut at even steps along X) and across Y.</summary>
    public int SheetsX { get; init; } = 6;
    public int SheetsY { get; init; } = 4;
    /// <summary>Radial: fins round the axis, and rings at even heights.</summary>
    public int Fins { get; init; } = 12;
    public int Rings { get; init; } = 3;
    /// <summary>Radial: the hole up the middle (for a dowel); fins start this far out.</summary>
    public double CoreMm { get; init; } = 6;
    /// <summary>Slots are the material's thickness plus this (mm), so the sheets slide together.</summary>
    public double SlotFitMm { get; init; } = 0.1;
    /// <summary>Parts smaller than this (mm²), or thinner than <see cref="MinPartWidthMm"/>, are left out: slivers too small to cut or handle.</summary>
    public double MinPartMm2 { get; init; } = 1;
    public double MinPartWidthMm { get; init; } = 1;
    /// <summary>
    /// Edge shaping (stacked): engrave each slice's top down in steps of this depth (mm, the depth one
    /// engrave pass takes off, from the etch test) wherever the model slopes away within the slice,
    /// so the stack's steps become the model's slopes. 0 = off.
    /// </summary>
    public double ShapeStepMm { get; init; }
    /// <summary>Edge shaping never engraves deeper than the slice less this (mm), so it can't go through.</summary>
    public double ShapeSkinMm { get; init; } = 0.4;
    /// <summary>
    /// Add-ons that change the stacked parts (hollowing them, say), run in order once every slice's
    /// parts are chosen and before the pins are placed. None by default.
    /// </summary>
    public IReadOnlyList<ISliceModifier> Modifiers { get; init; } = [];
}

/// <summary>
/// A step an add-on adds to stacked slicing: it sees every slice's parts once they're chosen (before
/// pins), and may change their outlines, split or drop them, and say what it did.
/// </summary>
public interface ISliceModifier
{
    void Modify(SliceWork work, CancellationToken ct);
}

/// <summary>
/// What the 3D to slices window passes to add-on panels in its "stackslice.stacked" slot: the
/// options it slices with now, and a place to put the add-on's slicing step.
/// </summary>
public interface IStackSliceSlot
{
    /// <summary>The options as the window has them now (without the add-ons' steps).</summary>
    SliceOptions Options { get; }
    /// <summary>Use this step when slicing (null takes it away); one per <paramref name="owner"/>. Slices again.</summary>
    void SetModifier(object owner, ISliceModifier? modifier);
}

/// <summary>Stacked slicing part-way through, for an <see cref="ISliceModifier"/>.</summary>
public sealed class SliceWork
{
    /// <summary>The model, stood up, turned and sized, starting at 0, 0, 0 (mm).</summary>
    public required IReadOnlyList<Tri> Mesh { get; init; }
    public required SliceOptions Options { get; init; }
    /// <summary>Each slice's height in the model (mm): slice i runs from i·SlabMm to (i+1)·SlabMm.</summary>
    public required double SlabMm { get; init; }
    /// <summary>Each slice's whole outline, bottom first (as sliced, before any modifier).</summary>
    public required IReadOnlyList<PathGeometry> Layers { get; init; }
    /// <summary>The dowel holes through every slice (already chosen; they're cut after the modifiers).</summary>
    public required IReadOnlyList<Vec2> Dowels { get; init; }
    /// <summary>Each slice's parts, bottom slice first: change, add or remove them.</summary>
    public required List<List<PathGeometry>> Parts { get; init; }
    /// <summary>Sentences for the slicing summary.</summary>
    public List<string> Notes { get; } = [];
    /// <summary>Anything else a modifier wants to hand on (by its own key), kept on the <see cref="SliceStack"/>.</summary>
    public Dictionary<string, object> Extras { get; } = [];
}

/// <summary>One part to cut: its outline (with holes), where its number goes, and the next slice's outline to mark on it.</summary>
public sealed record SlicePart(int Slice, PathGeometry Outline, Vec2 LabelAt, double LabelRoomMm, IReadOnlyList<IReadOnlyList<Vec2>> NextMarks)
{
    /// <summary>Where it sits in the model (for the 3D view).</summary>
    public Frame3 Frame { get; init; }
    /// <summary>
    /// Its name, marked on it: unique in the stack ("3", or "3a", "3b" where slice 3 is in several
    /// pieces; "X3", "Fin 2" for sheets); null = its slice number.
    /// </summary>
    public string? Label { get; init; }
    /// <summary>Small notes marked on it: beside each slot, the name of the sheet that goes in it.</summary>
    public IReadOnlyList<(string Text, Vec2 At)> Notes { get; init; } = [];
    /// <summary>Not joined, through the slices, to anything resting on the bottom slice: it starts in mid-air with nothing under it (glue it on by eye).</summary>
    public bool Floating { get; init; }
    /// <summary>
    /// Edge shaping: the areas to engrave on its top, one per step down, shallowest (biggest) first;
    /// each is engraved once, so a spot is taken down one step for every area it's in.
    /// </summary>
    public IReadOnlyList<PathGeometry> Shaping { get; init; } = [];
    /// <summary>How much of its underside slopes away (mm²): steps there can't be engraved from above, so they're sanded.</summary>
    public double UndersideMm2 { get; init; }
}

/// <summary>The result: every part, bottom slice first, in model millimetres (laid flat, top view).</summary>
public sealed record SliceStack(IReadOnlyList<SlicePart> Parts, int Slices, double StackHeightMm, double ModelWidthMm, double ModelDepthMm,
    IReadOnlyList<PathGeometry> Layers, IReadOnlyList<Vec2> Dowels)
{
    /// <summary>Slivers left out for being too small.</summary>
    public int LeftOut { get; init; }
    /// <summary>Joints where the shape where the slices meet was added so they overlap enough to glue.</summary>
    public int Joined { get; init; }
    /// <summary>Pins joining neighbouring slices: where each goes, and the slice it rises from (it sits in that slice and the one above).</summary>
    public IReadOnlyList<(int Slice, Vec2 At)> Pins { get; init; } = [];
    /// <summary>How it's built: stacked slices, or slotted sheets.</summary>
    public SliceMethod Method { get; init; } = SliceMethod.Stacked;
    /// <summary>The material's thickness (one slice or sheet each), mm.</summary>
    public double ThicknessMm { get; init; }
    /// <summary>What the add-ons that ran (<see cref="SliceOptions.Modifiers"/>) said they did, for the summary.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>What the add-ons handed on, by their own keys.</summary>
    public IReadOnlyDictionary<string, object> Extras { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Stacked slices: a 3D model cut into flat layers of the material's thickness. Each layer is cut
/// out (several parts where the model branches, like an animal's legs), numbered from the bottom
/// up, and marked with where the next layer sits, so they glue up in order into the model.
/// </summary>
public static class StackSlicer
{
    public static SliceStack Slice(IReadOnlyList<Tri> mesh, SliceOptions o, CancellationToken ct = default)
    {
        if (mesh.Count == 0) throw new ArgumentException("The model has no triangles.", nameof(mesh));
        if (!(o.ThicknessMm > 0) || !(o.HeightMm > 0)) throw new ArgumentException("Thickness and height must be above zero.");
        var tris = Orient(mesh, o);
        if (o.Method != SliceMethod.Stacked) return Sheets(tris, o, ct);
        double h = tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        int n = Math.Max(1, (int)Math.Round(h / o.ThicknessMm));
        double slab = h / n;
        var layers = new List<PathGeometry>();
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            double z0 = i * slab, z1 = (i + 1) * slab;
            PathGeometry layer;
            if (o.Outline == SliceOutline.Middle) layer = Section(tris, (z0 + z1) / 2);
            else
            {
                int k = Math.Max(2, o.Samples);
                var cuts = Enumerable.Range(0, k).Select(j => Section(tris, z0 + slab * (0.02 + 0.96 * j / (k - 1)))).Where(g => !g.IsEmpty).ToList();
                layer = cuts.Count == 0 ? PathGeometry.Empty
                    : o.Outline == SliceOutline.Outer ? ShapeGeometry.Union(cuts)
                    : cuts.Skip(1).Aggregate(cuts[0], (a, b) => ShapeGeometry.Boolean(a, b, BoolOp.Intersect));
            }
            layers.Add(PathTools.Simplify(layer, 0.01).Geometry);
        }
        // Steep slopes: where the model is joined between two slices but they barely overlap, both
        // get the model's shape at the plane where they meet (always shared, so they overlap).
        int joined = 0;
        if (o.KeepJoined)
            for (int i = 0; i + 1 < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                var meet = Section(tris, (i + 1) * slab);
                if (meet.IsEmpty) continue;
                var shared = ShapeGeometry.Boolean(layers[i], layers[i + 1], BoolOp.Intersect);
                var add = new List<PathGeometry>();
                foreach (var piece in Islands(meet))
                {
                    if (Area(piece) < 0.5) continue; // a point of contact: nothing to glue
                    // How much of this meeting place the two slices already share.
                    var near = ShapeGeometry.Offset(piece, 1, JoinStyle.Round, EndStyle.Round);
                    if (Area(ShapeGeometry.Boolean(shared, near, BoolOp.Intersect)) >= Math.Min(o.MinJoinMm2, Area(piece) * 0.8)) continue;
                    add.Add(piece);
                }
                if (add.Count == 0) continue;
                joined += add.Count;
                layers[i] = ShapeGeometry.Union([layers[i], .. add]);
                layers[i + 1] = ShapeGeometry.Union([layers[i + 1], .. add]);
            }
        // Dowels: two holes in the part every slice has in common (when there is one).
        var dowels = new List<Vec2>();
        if (o.DowelMm > 0 && layers.All(l => !l.IsEmpty))
        {
            var common = layers.Skip(1).Aggregate(layers[0], (a, b) => ShapeGeometry.Boolean(a, b, BoolOp.Intersect));
            var room = ShapeGeometry.Offset(common, -(o.DowelMm / 2 + 1.5), JoinStyle.Round, EndStyle.Round);
            dowels = TwoPointsInside(room);
        }
        // Every slice's parts first (slivers left out), so the pins can join them up.
        int leftOut = 0;
        var islands = new List<List<PathGeometry>>();
        for (int i = 0; i < n; i++)
        {
            var keep = new List<PathGeometry>();
            foreach (var island in Islands(layers[i]))
            {
                var loops = Loops(island);
                if (Math.Abs(loops.Sum(Geom.SignedArea)) < o.MinPartMm2 || Pole(loops).Room * 2 < o.MinPartWidthMm) { leftOut++; continue; }
                keep.Add(island);
            }
            // Left to right (then front to back), so a slice's pieces are lettered in the order they're seen.
            islands.Add([.. keep.OrderBy(g => Math.Round(g.Bounds().Center.X, 1)).ThenBy(g => g.Bounds().Center.Y)]);
        }
        // Add-ons (hollowing, say) change the parts before the pins go in, so the pins land where there's material.
        SliceWork? work = null;
        if (o.Modifiers.Count > 0)
        {
            work = new SliceWork { Mesh = tris, Options = o, SlabMm = slab, Layers = layers, Dowels = dowels, Parts = islands };
            foreach (var m in o.Modifiers)
            {
                ct.ThrowIfCancellationRequested();
                m.Modify(work, ct);
            }
            if (islands.Count != n) throw new InvalidOperationException("A slicing add-on changed how many slices there are.");
            for (int i = 0; i < n; i++)
                islands[i] = [.. islands[i].Where(g => !g.IsEmpty).OrderBy(g => Math.Round(g.Bounds().Center.X, 1)).ThenBy(g => g.Bounds().Center.Y)];
        }
        // Pins: wherever a part overlaps the part above it, matching holes in both.
        var holes = islands.Select(l => l.Select(_ => new List<Vec2>()).ToList()).ToList();
        var pins = new List<(int, Vec2)>();
        if (o.PinMm > 0)
            for (int i = 0; i + 1 < n; i++)
                for (int a = 0; a < islands[i].Count; a++)
                    for (int b = 0; b < islands[i + 1].Count; b++)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var p in PinSpots(islands[i][a], islands[i + 1][b], i > 0 ? layers[i - 1] : null, i + 2 < n ? layers[i + 2] : null, o.PinMm, holes[i][a], dowels, o.DowelMm))
                        {
                            holes[i][a].Add(p);
                            holes[i + 1][b].Add(p);
                            pins.Add((i + 1, p));
                        }
                    }
        // Groups of parts joined to each other through the slices.
        var ids = new Dictionary<(int, int), int>();
        foreach (var (i, list) in islands.Select((l, i) => (i, l)))
            for (int k = 0; k < list.Count; k++) ids[(i, k)] = ids.Count;
        var parent = Enumerable.Range(0, ids.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        for (int i = 0; i + 1 < n; i++)
            for (int a = 0; a < islands[i].Count; a++)
                for (int b = 0; b < islands[i + 1].Count; b++)
                    if (Area(ShapeGeometry.Boolean(islands[i][a], islands[i + 1][b], BoolOp.Intersect)) > 0.5)
                        parent[Find(ids[(i, a)])] = Find(ids[(i + 1, b)]);
        // A group that reaches the bottom slice stands on the base and can be built up from it (a
        // fish's separate segments, say); only a group starting in mid-air has nothing under it.
        var grounded = ids.Where(kv => kv.Key.Item1 == 0).Select(kv => Find(kv.Value)).ToHashSet();
        bool Touches(int i, int k) => grounded.Contains(Find(ids[(i, k)]));
        var parts = new List<SlicePart>();
        for (int i = 0; i < n; i++)
        {
            var next = i + 1 < n ? layers[i + 1] : null;
            for (int k = 0; k < islands[i].Count; k++)
            {
                var part = islands[i][k];
                var cut = dowels.Select(p => (p, o.DowelMm)).Concat(holes[i][k].Select(p => (p, o.PinMm))).ToList();
                if (cut.Count > 0)
                    part = ShapeGeometry.Boolean(part, new PathGeometry(cut.SelectMany(c => PathGeometry.Ellipse(c.p.X, c.p.Y, c.Item2 / 2, c.Item2 / 2).Subpaths)), BoolOp.Subtract);
                // The number goes clear of the holes and edges.
                var loops = Loops(part);
                var (at, room) = Pole(loops);
                var marks = next is null ? [] : MarksInside(next, loops, 0.6);
                var (shaping, under) = o.ShapeStepMm > 0 ? Shape(tris, part, i * slab, slab, o) : ([], 0);
                parts.Add(new SlicePart(i + 1, part, at, room, marks)
                {
                    Label = PartLabel(i + 1, k, islands[i].Count),
                    Shaping = shaping,
                    UndersideMm2 = under,
                    Floating = n > 1 && !Touches(i, k),
                    Frame = new Frame3(new V3(0, 0, (i + 0.5) * o.ThicknessMm), new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1)),
                });
            }
        }
        double w = tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), d = tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        return new SliceStack(parts, n, n * o.ThicknessMm, w, d, layers, dowels)
        {
            LeftOut = leftOut, Pins = pins, Joined = joined, ThicknessMm = o.ThicknessMm,
            Notes = work?.Notes ?? [], Extras = work?.Extras ?? new Dictionary<string, object>(),
        };
    }

    /// <summary>
    /// A stacked part's name: its slice number, with a letter when the slice is in several pieces
    /// (3a, 3b… then 3aa after 3z), so no two parts share one.
    /// </summary>
    public static string PartLabel(int slice, int piece, int pieces) => pieces > 1 ? $"{slice}{Letters(piece)}" : NotUpsideDown($"{slice}");

    static string Letters(int k) => k < 26 ? $"{(char)('a' + k)}" : Letters(k / 26 - 1) + (char)('a' + k % 26);

    /// <summary>
    /// Nesting turns parts round, so a number that reads as another one upside down (6 and 9, 16 and
    /// 91, 68 and 89) gets a dot after it: "6." is never mistaken for 9.
    /// </summary>
    public static string NotUpsideDown(string label)
    {
        static char? Turned(char c) => c switch { '0' => '0', '1' => '1', '6' => '9', '8' => '8', '9' => '6', _ => null };
        var turned = new char[label.Length];
        for (int i = 0; i < label.Length; i++)
        {
            if (Turned(label[^(i + 1)]) is not { } t) return label;
            turned[i] = t;
        }
        var other = new string(turned);
        return other != label && other[0] != '0' ? label + "." : label;
    }

    /// <summary>
    /// Edge shaping for one part of the slice from <paramref name="z0"/>, <paramref name="slab"/> thick.
    /// Step k (depth k·step) engraves where the model has no material at that depth or anywhere
    /// above it in the slice: the part less every cross-section from the top down to it. Areas
    /// shrink with depth, so each spot is engraved once per step it should go down. Also how much
    /// of the underside slopes away (only reachable from below).
    /// </summary>
    static (IReadOnlyList<PathGeometry> Steps, double UndersideMm2) Shape(IReadOnlyList<Tri> tris, PathGeometry part, double z0, double slab, SliceOptions o)
    {
        double step = o.ShapeStepMm;
        int count = (int)Math.Floor((slab - o.ShapeSkinMm) / step + 1e-9);
        var steps = new List<PathGeometry>();
        PathGeometry above = PathGeometry.Empty;
        double top = z0 + slab;
        for (int k = 1; k <= count; k++)
        {
            // The middle of the layer this step takes off.
            var cut = Section(tris, top - (k - 0.5) * step);
            above = above.IsEmpty ? cut : cut.IsEmpty ? above : ShapeGeometry.Union([above, cut]);
            var away = above.IsEmpty ? part : ShapeGeometry.Boolean(part, above, BoolOp.Subtract);
            // Hairline slivers (where the section and the cut outline all but agree) aren't worth a pass.
            away = ShapeGeometry.Offset(ShapeGeometry.Offset(away, -0.05, JoinStyle.Round, EndStyle.Round), 0.05, JoinStyle.Round, EndStyle.Round);
            if (Area(away) < 0.5) break; // everything deeper is narrower still
            steps.Add(PathTools.Simplify(away, 0.01).Geometry);
        }
        // The underside: where the model is higher up in the slice but not at its bottom (an overhang).
        var bottom = Section(tris, z0 + Math.Min(step, slab / 4));
        var higher = ShapeGeometry.Union([Section(tris, z0 + slab / 2), Section(tris, top - Math.Min(step, slab / 4))]);
        var over = higher.IsEmpty ? PathGeometry.Empty : bottom.IsEmpty ? higher : ShapeGeometry.Boolean(higher, bottom, BoolOp.Subtract);
        double under = over.IsEmpty ? 0 : Area(ShapeGeometry.Boolean(over, part, BoolOp.Intersect));
        return (steps, under);
    }

    // ---- egg crate and radial ------------------------------------------------------------------

    /// <summary>The model cut by a plane: its outline in the plane's own (u, v), thickness direction N.</summary>
    static PathGeometry PlaneSection(IReadOnlyList<Tri> tris, Frame3 f)
    {
        V3 Map(V3 p)
        {
            double dx = p.X - f.Origin.X, dy = p.Y - f.Origin.Y, dz = p.Z - f.Origin.Z;
            return new(dx * f.U.X + dy * f.U.Y + dz * f.U.Z, dx * f.V.X + dy * f.V.Y + dz * f.V.Z, dx * f.N.X + dy * f.N.Y + dz * f.N.Z);
        }
        return Section([.. tris.Select(t => new Tri(Map(t.A), Map(t.B), Map(t.C)))], 0);
    }

    /// <summary>Where a line across an outline (u = <paramref name="at"/>, or v = at when <paramref name="alongU"/>) is inside it: the spans, in order.</summary>
    static List<(double From, double To)> Spans(PathGeometry outline, double at, bool alongU)
    {
        var hits = new List<double>();
        foreach (var l in Loops(outline))
            for (int i = 0; i < l.Count; i++)
            {
                var a = l[i];
                var b = l[(i + 1) % l.Count];
                double pa = alongU ? a.Y : a.X, pb = alongU ? b.Y : b.X;
                if ((pa - at) * (pb - at) >= 0 && !(pa == at && pb != at)) continue;
                double t = (at - pa) / (pb - pa);
                hits.Add(alongU ? a.X + (b.X - a.X) * t : a.Y + (b.Y - a.Y) * t);
            }
        hits.Sort();
        var o = new List<(double, double)>();
        for (int i = 0; i + 1 < hits.Count; i += 2) if (hits[i + 1] - hits[i] > 1e-6) o.Add((hits[i], hits[i + 1]));
        return o;
    }

    static PathGeometry Box(double u0, double v0, double u1, double v1) => PathGeometry.Rect(Math.Min(u0, u1), Math.Min(v0, v1), Math.Abs(u1 - u0), Math.Abs(v1 - v0));

    /// <summary>A slot along a direction in a flat part: from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> wide.</summary>
    static PathGeometry Slot(Vec2 a, Vec2 b, double width)
    {
        var d = b - a;
        double len = Math.Max(1e-9, d.Length);
        var n = new Vec2(-d.Y / len, d.X / len) * (width / 2);
        return PathGeometry.Polyline([a + n, b + n, b - n, a - n], true);
    }

    sealed class Sheet(string label, Frame3 frame, PathGeometry outline)
    {
        public string Label = label;
        public Frame3 Frame = frame;
        public PathGeometry Outline = outline;
        public readonly List<PathGeometry> Cuts = [];
        public readonly List<(string, Vec2)> Notes = [];
    }

    /// <summary>Egg crate or radial: upright sheets slotted together.</summary>
    static SliceStack Sheets(List<Tri> tris, SliceOptions o, CancellationToken ct)
    {
        double maxX = tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), maxY = tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        double maxZ = tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        // Below 0 makes the slots tighter than the material (for a cut layer's kerf to open up), but never under half of it.
        double w = o.ThicknessMm + Math.Max(-o.ThicknessMm / 2, o.SlotFitMm);
        var up = new V3(0, 0, -1); // v runs down the part, so the model's top is at the top of the part as laid on the bed
        var sheets = new List<Sheet>();
        if (o.Method == SliceMethod.EggCrate)
        {
            int nx = Math.Clamp(o.SheetsX, 1, 60), ny = Math.Clamp(o.SheetsY, 1, 60);
            // Evenly inside the model, not on its very ends.
            var xs = Enumerable.Range(0, nx).Select(i => maxX * (i + 0.5) / nx).ToList();
            var ys = Enumerable.Range(0, ny).Select(j => maxY * (j + 0.5) / ny).ToList();
            var xSheets = xs.Select((x, i) => new Sheet($"X{i + 1}", new Frame3(new V3(x, 0, 0), new V3(0, 1, 0), up, new V3(1, 0, 0)), PathGeometry.Empty)).ToList();
            var ySheets = ys.Select((y, j) => new Sheet($"Y{j + 1}", new Frame3(new V3(0, y, 0), new V3(1, 0, 0), up, new V3(0, 1, 0)), PathGeometry.Empty)).ToList();
            foreach (var s in xSheets.Concat(ySheets)) { ct.ThrowIfCancellationRequested(); s.Outline = PlaneSection(tris, s.Frame); }
            // Each crossing: the model's spans up the line where the two meet (the same for both sheets).
            // The X sheet is slotted from the top down to half-way, the Y sheet from the bottom up.
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var xs_ = xSheets[i];
                    var ys_ = ySheets[j];
                    foreach (var (v0, v1) in Spans(xs_.Outline, ys[j], alongU: false))
                    {
                        if (v1 - v0 < 2 * w + 2) continue; // too short to slot
                        double mid = (v0 + v1) / 2; // v is downwards: v0 is the top
                        xs_.Cuts.Add(Box(ys[j] - w / 2, v0 - 3 * w, ys[j] + w / 2, mid));
                        ys_.Cuts.Add(Box(xs[i] - w / 2, mid, xs[i] + w / 2, v1 + 3 * w));
                        xs_.Notes.Add((ys_.Label, new Vec2(ys[j], mid + 2.5)));
                        ys_.Notes.Add((xs_.Label, new Vec2(xs[i], mid - 2.5)));
                    }
                }
            sheets.AddRange(xSheets);
            sheets.AddRange(ySheets);
        }
        else
        {
            int fins = Math.Clamp(o.Fins, 3, 72), rings = Math.Clamp(o.Rings, 1, 20);
            double core = Math.Max(0, o.CoreMm) / 2;
            var c = new V3(maxX / 2, maxY / 2, 0);
            var finSheets = new List<(Sheet S, double Angle)>();
            for (int k = 0; k < fins; k++)
            {
                double a = 2 * Math.PI * k / fins, ca = Math.Cos(a), sa = Math.Sin(a);
                var f = new Sheet($"Fin {k + 1}", new Frame3(c, new V3(ca, sa, 0), up, new V3(-sa, ca, 0)), PathGeometry.Empty);
                ct.ThrowIfCancellationRequested();
                // Only the half on this side of the axis, from the core out.
                var full = PlaneSection(tris, f.Frame);
                f.Outline = full.IsEmpty ? full : ShapeGeometry.Boolean(full, Box(core, -maxZ - 10, Math.Max(maxX, maxY) * 2, maxZ + 10), BoolOp.Intersect);
                finSheets.Add((f, a));
            }
            var ringSheets = new List<(Sheet S, double Z)>();
            for (int r = 0; r < rings; r++)
            {
                double z = maxZ * (r + 1) / (rings + 1);
                var ring = new Sheet($"Ring {r + 1}", new Frame3(new V3(0, 0, z), new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1)), PathGeometry.Empty);
                ring.Outline = Section(tris, z);
                if (core > 0 && !ring.Outline.IsEmpty) ring.Cuts.Add(PathGeometry.Ellipse(c.X, c.Y, core, core));
                ringSheets.Add((ring, z));
            }
            // Each fin meets each ring along a line out from the axis: the fin is slotted from its inner
            // edge out to half-way, the ring from its outer edge in.
            foreach (var (fin, a) in finSheets)
                foreach (var (ring, z) in ringSheets)
                    foreach (var (r0, r1) in Spans(fin.Outline, -z, alongU: true))
                    {
                        if (r1 - r0 < 2 * w + 2) continue;
                        double mid = (r0 + r1) / 2;
                        fin.Cuts.Add(Box(r0 - 3 * w, -z - w / 2, mid, -z + w / 2));
                        var dir = new Vec2(Math.Cos(a), Math.Sin(a));
                        var centre = new Vec2(c.X, c.Y);
                        ring.Cuts.Add(Slot(centre + dir * mid, centre + dir * (r1 + 3 * w), w));
                        fin.Notes.Add((ring.Label, new Vec2(mid + 3, -z - w / 2 - 2)));
                        ring.Notes.Add((fin.Label, centre + dir * (mid - 3)));
                    }
            sheets.AddRange(finSheets.Select(f => f.S));
            sheets.AddRange(ringSheets.Select(r => r.S));
        }

        var parts = new List<SlicePart>();
        int index = 0, leftOut = 0;
        foreach (var s in sheets)
        {
            index++;
            if (s.Outline.IsEmpty) continue;
            var cut = s.Cuts.Count == 0 ? s.Outline : ShapeGeometry.Boolean(s.Outline, ShapeGeometry.Union(s.Cuts), BoolOp.Subtract);
            var islands = Islands(cut);
            for (int k = 0; k < islands.Count; k++)
            {
                var island = islands[k];
                var loops = Loops(island);
                if (Math.Abs(loops.Sum(Geom.SignedArea)) < Math.Max(o.MinPartMm2, 4)) { leftOut++; continue; }
                var (at, room) = Pole(loops);
                var notes = s.Notes.Where(n => InsideEvenOdd(loops, n.Item2)).Select(n => (n.Item1, n.Item2)).ToList();
                parts.Add(new SlicePart(index, island, at, room, [])
                {
                    Frame = s.Frame,
                    Label = islands.Count > 1 ? $"{s.Label}{Letters(k)}" : s.Label,
                    Notes = notes,
                });
            }
        }
        return new SliceStack(parts, sheets.Count, maxZ, maxX, maxY, [], []) { LeftOut = leftOut, Method = o.Method, ThicknessMm = o.ThicknessMm };
    }

    /// <summary>Area covered (outlines minus holes), mm².</summary>
    public static double Area(PathGeometry g) => g.IsEmpty ? 0 : Math.Abs(Loops(g).Sum(Geom.SignedArea));

    public static List<List<Vec2>> Loops(PathGeometry g) => [.. g.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points)];

    /// <summary>
    /// Where pins go between a part and the part above it: inside both, clear of their edges, and
    /// where the slices below and above cover the holes too (so they don't show) if there's room.
    /// Two pins, far apart, when there's room for both (that stops the parts turning); else one.
    /// They keep clear of the lower part's other pins and of the dowel holes through every slice.
    /// </summary>
    static List<Vec2> PinSpots(PathGeometry lower, PathGeometry upper, PathGeometry? below, PathGeometry? above, double pinMm, IReadOnlyList<Vec2> taken, IReadOnlyList<Vec2> dowels, double dowelMm)
    {
        var overlap = ShapeGeometry.Boolean(lower, upper, BoolOp.Intersect);
        if (overlap.IsEmpty) return [];
        double clear = pinMm / 2 + 1.2;
        var hidden = overlap;
        if (below is not null) hidden = ShapeGeometry.Boolean(hidden, below, BoolOp.Intersect);
        if (above is not null) hidden = ShapeGeometry.Boolean(hidden, above, BoolOp.Intersect);
        foreach (var area in new[] { hidden, overlap })
        {
            if (area.IsEmpty) continue;
            var room = ShapeGeometry.Offset(area, -clear, JoinStyle.Round, EndStyle.Round);
            // Clear of the holes already in the lower part (its pins from the joint below), and of the
            // dowel holes, with a wall as thick as the one kept to the edge between them.
            var keepOff = taken.Select(t => (t, pinMm * 1.5 + 1)).Concat(dowels.Select(d => (d, dowelMm / 2 + clear))).ToList();
            if (keepOff.Count > 0 && !room.IsEmpty)
                room = ShapeGeometry.Boolean(room, new PathGeometry(keepOff.SelectMany(k => PathGeometry.Ellipse(k.Item1.X, k.Item1.Y, k.Item2, k.Item2).Subpaths)), BoolOp.Subtract);
            if (room.IsEmpty) continue;
            var two = TwoPointsInside(room);
            // Two pins only where they can be well apart (small parts: one, plus the outline mark).
            if (two.Count == 2 && two[0].Dist(two[1]) >= Math.Max(pinMm * 4, 12)) return two;
            var loops = Loops(room);
            if (loops.Count > 0 && Pole(loops) is { Room: > 0 } pole) return [pole.At];
        }
        return [];
    }

    /// <summary>The model stood the chosen way up, turned, sized to the height, and moved to start at 0, 0, 0.</summary>
    /// <summary>The model's size standing the chosen way up and turned, before any scaling (its own units).</summary>
    public static (double X, double Y, double Z) NaturalSize(IReadOnlyList<Tri> mesh, SliceUp up, double turnDeg)
    {
        var o = Orient(mesh, new SliceOptions { Up = up, TurnDeg = turnDeg, HeightMm = 1 }, scale: false);
        var all = o.SelectMany(x => new[] { x.A, x.B, x.C }).ToList();
        return (all.Max(v => v.X) - all.Min(v => v.X), all.Max(v => v.Y) - all.Min(v => v.Y), all.Max(v => v.Z) - all.Min(v => v.Z));
    }

    static List<Tri> Orient(IReadOnlyList<Tri> mesh, SliceOptions o, bool scale = true)
    {
        V3 Up(V3 v) => o.Up switch
        {
            SliceUp.MinusZ => new(v.X, -v.Y, -v.Z),
            SliceUp.Y => new(v.X, -v.Z, v.Y),
            SliceUp.MinusY => new(v.X, v.Z, -v.Y),
            SliceUp.X => new(v.Y, v.Z, v.X),
            SliceUp.MinusX => new(-v.Y, v.Z, -v.X),
            _ => v,
        };
        double t = o.TurnDeg * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t);
        V3 Map(V3 v) { var u = Up(v); return new(u.X * c - u.Y * s, u.X * s + u.Y * c, u.Z); }
        var tris = mesh.Select(x => new Tri(Map(x.A), Map(x.B), Map(x.C))).ToList();
        var all = tris.SelectMany(x => new[] { x.A, x.B, x.C }).ToList();
        double minX = all.Min(v => v.X), minY = all.Min(v => v.Y), minZ = all.Min(v => v.Z), maxZ = all.Max(v => v.Z);
        double maxX = all.Max(v => v.X), maxY = all.Max(v => v.Y);
        // Height sets the scale; width and depth stretch or squash on their own when given.
        double k = scale ? o.HeightMm / Math.Max(1e-9, maxZ - minZ) : 1;
        double kx = scale && o.WidthMm > 0 ? o.WidthMm / Math.Max(1e-9, maxX - minX) : k;
        double ky = scale && o.DepthMm > 0 ? o.DepthMm / Math.Max(1e-9, maxY - minY) : k;
        // Y runs the other way: the parts are laid on the bed seen from above, where Y runs towards the
        // front of the machine, so without this the glued-up stack came out a mirror image of the
        // model. (A mirror turns the triangles inside out: two corners swap to keep them facing out.)
        V3 Fit(V3 v) => new((v.X - minX) * kx, (maxY - v.Y) * ky, (v.Z - minZ) * k);
        return [.. tris.Select(x => new Tri(Fit(x.A), Fit(x.C), Fit(x.B)))];
    }

    /// <summary>The model's cross-section at height <paramref name="z"/>: triangle edges crossing it, joined into closed outlines.</summary>
    public static PathGeometry Section(IReadOnlyList<Tri> tris, double z)
    {
        z += 1e-7; // never exactly on a vertex
        var segs = new List<(Vec2 A, Vec2 B)>();
        foreach (var t in tris)
        {
            var pts = new List<Vec2>(2);
            void Edge(V3 p, V3 q)
            {
                if ((p.Z - z) * (q.Z - z) >= 0) return;
                double f = (z - p.Z) / (q.Z - p.Z);
                pts.Add(new Vec2(p.X + (q.X - p.X) * f, p.Y + (q.Y - p.Y) * f));
            }
            Edge(t.A, t.B); Edge(t.B, t.C); Edge(t.C, t.A);
            if (pts.Count != 2 || pts[0].Dist(pts[1]) <= 1e-9) continue;
            // Run each piece with the solid on its left (outward normal on the right), so outlines
            // come out anticlockwise and holes clockwise, whatever order the triangle's edges cut.
            double nx = (t.B.Y - t.A.Y) * (t.C.Z - t.A.Z) - (t.B.Z - t.A.Z) * (t.C.Y - t.A.Y);
            double ny = (t.B.Z - t.A.Z) * (t.C.X - t.A.X) - (t.B.X - t.A.X) * (t.C.Z - t.A.Z);
            var d = pts[1] - pts[0];
            segs.Add(d.Y * nx - d.X * ny >= 0 ? (pts[0], pts[1]) : (pts[1], pts[0]));
        }
        // Join the pieces end to end (ends matched to a micron).
        static (long, long) Key(Vec2 p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));
        // Pieces by where they start: each one carries on from the end of the last.
        var at = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            if (!at.TryGetValue(Key(segs[i].A), out var l)) at[Key(segs[i].A)] = l = [];
            l.Add(i);
        }
        var used = new bool[segs.Count];
        var loops = new List<Subpath>();
        for (int s0 = 0; s0 < segs.Count; s0++)
        {
            if (used[s0]) continue;
            used[s0] = true;
            var loop = new List<Vec2> { segs[s0].A, segs[s0].B };
            while (true)
            {
                var end = loop[^1];
                int nxt = at.TryGetValue(Key(end), out var cand) ? cand.FirstOrDefault(j => !used[j], -1) : -1;
                if (nxt < 0) break;
                used[nxt] = true;
                loop.Add(segs[nxt].B);
                if (Key(loop[^1]) == Key(loop[0])) break;
            }
            if (Key(loop[^1]) == Key(loop[0])) loop.RemoveAt(loop.Count - 1);
            if (loop.Count >= 3 && Math.Abs(Geom.SignedArea(loop)) > 1e-4)
                loops.Add(PathGeometry.Polyline(loop, true).Subpaths[0]);
        }
        // Non-zero: overlapping solids in the model merge, holes (running the other way) stay holes.
        return loops.Count == 0 ? PathGeometry.Empty : ShapeGeometry.FillNonZero(new PathGeometry(loops));
    }

    /// <summary>Each separate piece of a layer (an outline with the holes inside it).</summary>
    public static List<PathGeometry> Islands(PathGeometry layer)
    {
        var loops = layer.Subpaths.Where(sp => sp.Closed).Select(sp => (Sp: sp, Pts: sp.Flatten(0.05))).Where(x => x.Pts.Count >= 3).ToList();
        int Depth(int i) => loops.Where((_, j) => j != i).Count(o => Geom.Inside(o.Pts, loops[i].Pts[0]));
        var depth = loops.Select((_, i) => Depth(i)).ToList();
        var outers = Enumerable.Range(0, loops.Count).Where(i => depth[i] % 2 == 0).ToList();
        var o = outers.ToDictionary(i => i, i => new List<Subpath> { loops[i].Sp });
        for (int i = 0; i < loops.Count; i++)
        {
            if (depth[i] % 2 == 0) continue;
            // A hole belongs to the smallest outline round it.
            var owner = outers.Where(j => Geom.Inside(loops[j].Pts, loops[i].Pts[0])).OrderBy(j => Math.Abs(Geom.SignedArea(loops[j].Pts))).FirstOrDefault(-1);
            if (owner >= 0) o[owner].Add(loops[i].Sp);
        }
        return [.. o.Values.Select(l => new PathGeometry(l))];
    }

    static bool InsideEvenOdd(IReadOnlyList<List<Vec2>> loops, Vec2 p) => loops.Count(l => Geom.Inside(l, p)) % 2 == 1;

    static double EdgeDistance(IReadOnlyList<List<Vec2>> loops, Vec2 p)
    {
        double best = double.PositiveInfinity;
        foreach (var l in loops)
            for (int i = 0; i < l.Count; i++)
            {
                var a = l[i];
                var b = l[(i + 1) % l.Count];
                var ab = b - a;
                double len2 = ab.Dot(ab), t = len2 < 1e-18 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
                best = Math.Min(best, p.Dist(a + ab * t));
            }
        return best;
    }

    /// <summary>The point inside furthest from any edge (where the number fits best), and how far that is.</summary>
    public static (Vec2 At, double Room) Pole(List<List<Vec2>> loops)
    {
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        (Vec2, double) best = (box.Center, 0);
        double cell = Math.Max(box.Width, box.Height) / 24;
        var centre = box.Center;
        for (int pass = 0; pass < 3 && cell > 1e-3; pass++)
        {
            var start = best.Item2 > 0 ? best.Item1 : centre;
            double span = pass == 0 ? Math.Max(box.Width, box.Height) : cell * 4;
            for (double x = start.X - span / 2; x <= start.X + span / 2; x += cell)
                for (double y = start.Y - span / 2; y <= start.Y + span / 2; y += cell)
                {
                    var p = new Vec2(x, y);
                    if (!InsideEvenOdd(loops, p)) continue;
                    double d = EdgeDistance(loops, p);
                    if (d > best.Item2) best = (p, d);
                }
            cell /= 4;
        }
        return best;
    }

    /// <summary>The next layer's outline, only where it lies inside this part (clear of its edge by <paramref name="margin"/>).</summary>
    static List<IReadOnlyList<Vec2>> MarksInside(PathGeometry next, List<List<Vec2>> part, double margin)
    {
        var o = new List<IReadOnlyList<Vec2>>();
        foreach (var f in next.Flatten(0.05).Where(f => f.Closed))
        {
            var pts = f.Points;
            var run = new List<Vec2>();
            int count = pts.Count;
            for (int i = 0; i <= count; i++)
            {
                var a = pts[i % count];
                var b = pts[(i + 1) % count];
                int steps = Math.Max(1, (int)Math.Ceiling(a.Dist(b) / 0.25));
                for (int k = 0; k < steps && !(i == count && k > 0); k++)
                {
                    var p = Vec2.Lerp(a, b, (double)k / steps);
                    if (InsideEvenOdd(part, p) && EdgeDistance(part, p) > margin) run.Add(p);
                    else { if (run.Count >= 3) o.Add(run); run = []; }
                }
            }
            if (run.Count >= 3) o.Add(run);
        }
        return o;
    }

    /// <summary>Two points inside an area, far apart (for dowels), or none.</summary>
    public static List<Vec2> TwoPointsInside(PathGeometry area)
    {
        var loops = area.Flatten(0.1).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        if (loops.Count == 0) return [];
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        var inside = new List<Vec2>();
        double cell = Math.Max(0.5, Math.Max(box.Width, box.Height) / 40);
        for (double x = box.MinX; x <= box.MaxX; x += cell)
            for (double y = box.MinY; y <= box.MaxY; y += cell)
                if (InsideEvenOdd(loops, new Vec2(x, y))) inside.Add(new Vec2(x, y));
        if (inside.Count < 2) return [];
        var a = inside.MaxBy(p => p.Dist(box.Center));
        var b = inside.MaxBy(p => p.Dist(a));
        return a.Dist(b) < 3 ? [] : [a, b];
    }
}

/// <summary>Stacked slices laid out on the bed, ready to cut: a page per bed-full.</summary>
public static class StackLayout
{
    /// <summary>
    /// Every part on a Cut layer, its number (and the next slice's outline, when asked) lightly
    /// marked on a Score layer, nested by true shape (biggest first, quarter turns, small parts in
    /// the gaps) to fill each bed before starting the next. Parts that don't fit go on new pages
    /// ("Slices 2", …). Returns the design (showing the first page used) and how many beds it takes.
    /// With <paramref name="material"/> (an outline of the board in the machine), the parts fill that
    /// on the current page, around anything already on it; each further bed is a new page with
    /// the same outline (another board the same size, in the same place).
    /// </summary>
    /// <param name="shaping">The engrave settings for edge shaping (one pass = one step); the parts' shaping is left out when null.</param>
    public static (DocState State, int Sheets) Place(DocState s, SliceStack stack, MachineProfile machine, bool numbers, bool marks, double gapMm = 3, string title = "Slices", CancellationToken ct = default, PathGeometry? material = null, Layer? shaping = null)
    {
        bool shape = shaping is not null && stack.Parts.Any(p => p.Shaping.Count > 0);
        title = string.IsNullOrWhiteSpace(title) ? "Slices" : title.Trim();
        // Never on top of what's already there: a page of their own if this one has anything on it.
        // (On the material outlined on this page, the nesting keeps clear of what's there instead.)
        bool fresh = material is null || s.Shapes.Count == 0;
        if (material is null && s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;
        (Layer Cut, Layer Marks, Layer? Shaping) Layers(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == "Slices · cut");
            var mk = st.Layers.FirstOrDefault(l => l.Name == "Slices · marks");
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = "Slices · cut" };
                st = Ops.AddLayer(st, cut);
            }
            Layer? sh = null;
            if (shape)
            {
                sh = st.Layers.FirstOrDefault(l => l.Name == ShapingLayer);
                if (sh is null)
                {
                    // Engraved before the cut (cut parts can shift), one pass a step.
                    sh = shaping! with { Id = Guid.NewGuid(), Name = ShapingLayer, Mode = LayerMode.Fill, Laser = shaping!.Laser.WithPasses(1), ParentId = null };
                    st = Ops.AddLayer(st, sh, st.Layers.FindIndex(l => l.Id == cut.Id));
                }
                else
                {
                    // Made again: the steps are engraved with the settings chosen this time (each step's depth depends on them).
                    var had = sh;
                    sh = shaping! with { Id = had.Id, Name = ShapingLayer, Mode = LayerMode.Fill, Laser = shaping!.Laser.WithPasses(1), ParentId = null, Visible = had.Visible, Output = had.Output, Locked = had.Locked };
                    var now = sh;
                    st = st with { Layers = [.. st.Layers.Select(l => l.Id == had.Id ? now : l)] };
                }
            }
            if (mk is null)
            {
                var score = Layer.Default(st.Layers.Count, LayerMode.Score);
                mk = score with { Name = "Slices · marks", Laser = score.Laser with { Power = Math.Min(score.Laser.Power, 15) } };
                // Marked before anything is cut: cut parts can shift or drop.
                st = Ops.AddLayer(st, mk, st.Layers.FindIndex(l => l.Id == (sh ?? cut).Id));
            }
            return (cut, mk, sh);
        }
        var (cutLayer, markLayer, shapeLayer) = Layers(ref s);
        // The shaping steps of every part (ids are kept through grouping and the nesting).
        var shapingIds = new HashSet<Guid>();
        // Each part with its marks, made at the bed's corner; the nesting moves it into place.
        var units = new List<List<Shape>>();
        foreach (var part in stack.Parts)
        {
            var box = part.Outline.Bounds();
            if (box.IsEmpty) continue;
            string name = stack.Method == SliceMethod.Stacked ? $"Slice {(part.Label ?? $"{part.Slice}").TrimEnd('.')}" : part.Label ?? $"Slice {part.Slice}";
            if (Math.Min(box.Width, box.Height) + 2 * gapMm > Math.Min(machine.FieldW, machine.FieldH) || Math.Max(box.Width, box.Height) + 2 * gapMm > Math.Max(machine.FieldW, machine.FieldH))
                throw new InvalidOperationException($"{name} is {box.Width:0} × {box.Height:0} mm: bigger than the bed. Make the model smaller.");
            var move = Affine.Translate(gapMm - box.MinX, gapMm - box.MinY);
            var shapes = new List<Shape> { new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = part.Outline.Transform(move) } };
            if (shape)
                foreach (var (area, k) in part.Shaping.Select((a, k) => (a, k)))
                {
                    var step = new PathShape { Name = $"{name} · step {k + 1}", LayerId = shapeLayer!.Id, Geometry = area.Transform(move) };
                    shapingIds.Add(step.Id);
                    shapes.Add(step);
                }
            if (marks && part.NextMarks.Count > 0)
                shapes.Add(new PathShape { Name = $"Where slice {part.Slice + 1} goes", LayerId = markLayer.Id, Geometry = new PathGeometry(part.NextMarks.SelectMany(m => PathGeometry.Polyline(m, false).Subpaths)).Transform(move) });
            if (numbers && part.LabelRoomMm > 0.6)
            {
                double size = Math.Clamp(part.LabelRoomMm * 0.9, 1.2, 6);
                var text = TextShape.Create(part.Label ?? $"{part.Slice}", new TextStyle("Arial", size), markLayer.Id, Affine.Identity);
                var tb = text.Bounds;
                var at = move.Apply(part.LabelAt);
                shapes.Add(text.With(transform: Affine.Translate(at.X - tb.Center.X, at.Y - tb.Center.Y)));
            }
            if (numbers)
                foreach (var (note, where) in part.Notes)
                {
                    // Beside each slot: the sheet that goes in it.
                    var t = TextShape.Create(note, new TextStyle("Arial", 2), markLayer.Id, Affine.Identity);
                    var tb = t.Bounds;
                    var at = move.Apply(where);
                    shapes.Add(t.With(transform: Affine.Translate(at.X - tb.Center.X, at.Y - tb.Center.Y)));
                }
            if (shapes.Count > 1)
            {
                var g = Guid.NewGuid();
                shapes = [.. shapes.Select(sh => sh.WithGroup(g))];
            }
            units.Add(shapes);
        }

        // Bed by bed: everything left goes on the page, the nesting places what fits, the rest moves on.
        var options = new NestOptions { Sheet = material, SpacingMm = gapMm, MarginMm = gapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            // Each page has its own layers: the cut outline is first in its part, the rest are marks.
            var onPage = units.Select(u => u.Select((sh, k) => sh.With(layerId: k == 0 ? cutLayer.Id : shapingIds.Contains(sh.Id) ? shapeLayer!.Id : markLayer.Id)).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && fresh)
                throw new InvalidOperationException(material is null
                    ? "A part doesn't fit on the bed with the spacing around it. Make the model smaller."
                    : "A part doesn't fit on the material with the spacing around it. Make the model smaller or outline a bigger piece.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            // A new bed-full on its own page (on the same size of board, when there's an outline).
            s = Pages.Add(s, $"{title} {++sheets}");
            fresh = true;
            if (material is not null)
            {
                var (withLayer, ml) = MaterialOutline.Ensure(s);
                s = Ops.AddShapes(withLayer, [new PathShape { Name = "Material", LayerId = ml.Id, Geometry = material }]);
            }
            (cutLayer, markLayer, shapeLayer) = Layers(ref s);
        }
        return (Pages.SwitchTo(s, firstPage), sheets);
    }

    /// <summary>The engrave layer edge shaping goes on.</summary>
    public const string ShapingLayer = "Slices · shaping";
}
