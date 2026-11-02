using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Stencil;

/// <summary>
/// What the window works on: the selected letters (bridged ones back as they were before, so their
/// bridges can be changed), the stencil's edge if one of the selected shapes is a single outline
/// round all the others, and the bridges already there.
/// </summary>
public sealed class StencilInput
{
    /// <summary>The letters as they are (or were before their bridges), bottom first.</summary>
    public required IReadOnlyList<PathShape> Letters { get; init; }
    /// <summary>One outline round all the letters: the stencil's own edge (left as it is).</summary>
    public PathShape? Edge { get; init; }
    /// <summary>Shapes in the design that already have bridges, by id: they're replaced by <see cref="Letters"/>' originals.</summary>
    public required IReadOnlySet<Guid> Bridged { get; init; }
    /// <summary>The bridges they had (world mm, where they are now).</summary>
    public required IReadOnlyList<Bridge> Existing { get; init; }
    /// <summary>The options they were made with (null: never bridged).</summary>
    public StencilOptions? Options { get; init; }
}

/// <summary>
/// Bridging shapes in the design and taking the bridges out again. Each bridged shape keeps a copy
/// of what it was (its original shapes, so a text comes back as text) and its bridges in its add-on
/// notes, so the bridges can be changed or taken out later even after it was moved.
/// </summary>
public static class StencilEdit
{
    public const string OriginalKey = "stencil.original";
    public const string BridgesKey = "stencil.bridges";
    public const string OptionsKey = "stencil.options";

    public static bool IsBridged(Shape s) => s.AddonData.ContainsKey(OriginalKey);

    static bool HasOutline(PathShape p) => p.Geometry.Subpaths.Any(s => s.Closed && !s.Segs.IsEmpty);

    /// <summary>The selected shapes that can be stencil letters (anything with a closed outline).</summary>
    public static List<PathShape> Selected(DocState s, IReadOnlySet<Guid> selection) =>
        [.. s.Shapes.Where(x => selection.Contains(x.Id)).OfType<PathShape>().Where(HasOutline)];

    public static StencilInput Gather(DocState s, IReadOnlySet<Guid> selection, bool findEdge = true)
    {
        var letters = new List<PathShape>();
        var bridged = new HashSet<Guid>();
        var existing = new List<Bridge>();
        StencilOptions? options = null;
        foreach (var sh in Selected(s, selection))
        {
            if (IsBridged(sh) && Originals(sh) is { Count: > 0 } orig)
            {
                bridged.Add(sh.Id);
                letters.AddRange(orig);
                existing.AddRange(Bridges(sh));
                options ??= ReadOptions(sh);
            }
            else letters.Add(sh);
        }
        PathShape? edge = findEdge ? FindEdge(letters) : null;
        if (edge is not null) letters.Remove(edge);
        return new StencilInput { Letters = letters, Edge = edge, Bridged = bridged, Existing = existing, Options = options };
    }

    /// <summary>
    /// The stencil's edge: a shape that is one closed outline with every other shape inside it.
    /// A single letter O is one shape with two outlines, so it is never taken for an edge; an O
    /// drawn as two circles isn't either (one plain outline inside one other is a letter and its
    /// middle). The window lets the person change it.
    /// </summary>
    public static PathShape? FindEdge(IReadOnlyList<PathShape> shapes)
    {
        if (shapes.Count < 2) return null;
        if (shapes.Count == 2 && shapes.All(x => x is not TextShape && x.Flat.Count(f => f.Closed) == 1)) return null;
        foreach (var e in shapes)
        {
            var loops = e.World.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3).ToList();
            if (loops.Count != 1 || e is TextShape) continue;
            var loop = loops[0].Points;
            var eb = e.Bounds;
            bool all = shapes.All(o => ReferenceEquals(o, e) ||
                (eb.Contains(o.Bounds) && o.Flat.Where(f => f.Points.Count > 0).All(f => Geom.Inside(loop, f.Points[0]))));
            if (all) return e;
        }
        return null;
    }

    /// <summary>
    /// The design with the letters bridged: each letter a bridge crosses becomes an outline with the
    /// bridges kept out of it (on its own layer and group, in its place); letters no bridge crosses
    /// stay as they are (bridged ones go back to what they were). Letters with outlines inside
    /// another's (the middle of an O drawn as its own shape) become one shape, so the cut goes
    /// round the bridge properly.
    /// </summary>
    public static DocState Apply(DocState s, StencilInput input, IReadOnlyList<Bridge> bridges, StencilOptions options)
    {
        // First put back what bridged shapes were, in their place.
        foreach (var id in input.Bridged)
            if (s.FindShape(id) is PathShape b) s = ReplaceWith(s, id, Originals(b) ?? []);
        var order = s.Shapes.Select((x, i) => (x.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var letters = input.Letters.Where(l => order.ContainsKey(l.Id)).OrderBy(l => order[l.Id]).ToList();
        foreach (var group in Nested(letters))
        {
            var gb = group.Select(g => g.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
            var mine = bridges.Where(b => b.Outline.Bounds().Intersects(gb)).ToList();
            var geometry = group.Count == 1 ? group[0].World : new PathGeometry(group.SelectMany(g => g.World.Subpaths));
            var cut = mine.Count == 0 ? geometry : StencilSheet.Cut(geometry, mine);
            bool changed = mine.Count > 0 && Math.Abs(Doc.Geometry.ShapeGeometry.Area(cut) - Doc.Geometry.ShapeGeometry.Area(geometry)) > 1e-4;
            if (!changed) continue;
            var first = group[0];
            var notes = first.AddonData.Remove(OriginalKey).Remove(BridgesKey).Remove(OptionsKey)
                .SetItem(OriginalKey, SaveOriginals(s, group))
                .SetItem(BridgesKey, SaveBridges(mine))
                .SetItem(OptionsKey, SaveOptions(options));
            var shape = (PathShape)new PathShape { Id = first.Id, Name = first.Name, LayerId = first.LayerId, GroupId = first.GroupId, Geometry = cut }
                .WithAddonData(notes);
            if (first.Locked) shape = (PathShape)shape.WithLocked(true);
            s = Ops.Replace(s, shape);
            if (group.Count > 1) s = Ops.Delete(s, group.Skip(1).Select(g => g.Id).ToHashSet());
        }
        return s;
    }

    /// <summary>The design with the bridges taken out of these shapes: each goes back to what it was before (a text is a text again).</summary>
    public static DocState TakeOut(DocState s, IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
            if (s.FindShape(id) is PathShape b && IsBridged(b) && Originals(b) is { Count: > 0 } orig)
                s = ReplaceWith(s, id, orig);
        return s;
    }

    /// <summary>Groups of letters whose outlines lie inside each other's (bottom first in each).</summary>
    static List<List<PathShape>> Nested(List<PathShape> letters)
    {
        var parent = Enumerable.Range(0, letters.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var loops = letters.Select(l => l.Flat.Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList()).ToList();
        for (int i = 0; i < letters.Count; i++)
            for (int j = 0; j < letters.Count; j++)
            {
                if (i == j || Find(i) == Find(j) || !letters[j].Bounds.Contains(letters[i].Bounds)) continue;
                if (loops[i].Any(li => loops[j].Any(lj => Rect2.Bounds(lj).Contains(Rect2.Bounds(li)) && Geom.Inside(lj, li[0]))))
                    parent[Find(i)] = Find(j);
            }
        return [.. Enumerable.Range(0, letters.Count).GroupBy(Find).Select(g => g.Select(i => letters[i]).ToList())];
    }

    static DocState ReplaceWith(DocState s, Guid id, IReadOnlyList<Shape> shapes)
    {
        int i = s.Shapes.FindIndex(x => x.Id == id);
        if (i < 0) return s;
        var list = s.Shapes.RemoveAt(i).InsertRange(i, shapes);
        // A copy of one of them already in the design (made after bridging) keeps its own id: don't duplicate ids.
        var seen = new HashSet<Guid>();
        var b = ImmutableList.CreateBuilder<Shape>();
        foreach (var x in list)
            if (seen.Add(x.Id)) b.Add(x);
            else b.Add(x.With(id: Guid.NewGuid()));
        return s with { Shapes = b.ToImmutable() };
    }

    // ---- notes -----------------------------------------------------------------------------

    /// <summary>What a bridged shape was, moved and turned with it since.</summary>
    public static List<PathShape>? Originals(PathShape bridged)
    {
        if (!bridged.AddonData.TryGetValue(OriginalKey, out var json)) return null;
        try
        {
            var t = bridged.Transform;
            var orig = DocFile.Deserialize(json).Shapes.OfType<PathShape>().ToList();
            // A copy of a bridged shape (its own id) gives its originals ids of their own too.
            bool copy = orig.Count > 0 && orig[0].Id != bridged.Id;
            return [.. orig.Select((o, i) => (PathShape)o.With(transform: o.Transform.Then(t),
                id: i == 0 ? bridged.Id : copy ? Guid.NewGuid() : o.Id))];
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>A bridged shape's bridges, where they are now.</summary>
    public static List<Bridge> Bridges(PathShape bridged)
    {
        var list = new List<Bridge>();
        if (!bridged.AddonData.TryGetValue(BridgesKey, out var json)) return list;
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return list;
            foreach (var n in arr)
                if (n is JsonArray v && v.Count == 5)
                    list.Add(new Bridge(new(N(v[0]), N(v[1])), new(N(v[2]), N(v[3])), N(v[4])).Transformed(bridged.Transform));
        }
        catch (JsonException) { }
        return list;
        static double N(JsonNode? n) => n?.GetValue<double>() ?? 0;
    }

    public static StencilOptions? ReadOptions(Shape s)
    {
        if (!s.AddonData.TryGetValue(OptionsKey, out var json)) return null;
        try { return JsonSerializer.Deserialize<StencilOptions>(json); }
        catch (JsonException) { return null; }
    }

    static string SaveOptions(StencilOptions o) => JsonSerializer.Serialize(o);

    static string SaveBridges(IEnumerable<Bridge> bridges) =>
        new JsonArray([.. bridges.Select(b => (JsonNode)new JsonArray(R(b.A.X), R(b.A.Y), R(b.B.X), R(b.B.Y), R(b.WidthMm)))]).ToJsonString();

    static double R(double v) => Math.Round(v, 5);

    static string SaveOriginals(DocState s, IReadOnlyList<PathShape> group)
    {
        var layers = group.Select(g => g.LayerId).Distinct().Select(s.FindLayer).OfType<Layer>().ToImmutableList();
        var shapes = group.Select(g => g.WithAddonData(g.AddonData.Remove(OriginalKey).Remove(BridgesKey).Remove(OptionsKey))).ToImmutableList();
        return DocFile.Serialize(new DocState(layers, shapes));
    }

    internal static string Mm(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
}
