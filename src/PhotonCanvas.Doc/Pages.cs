using System.Collections.Immutable;

namespace PhotonCanvas.Doc;

/// <summary>A page of a project that isn't the one on screen: its own layers, shapes and guides.</summary>
public sealed record DesignPage(string Name, ImmutableList<Layer> Layers, ImmutableList<Shape> Shapes, ImmutableList<Guide> Guides);

/// <summary>
/// Several pages (canvases) in one project. The page on screen is the design itself; the others
/// wait in <see cref="DocState.OtherPages"/>. Each page is its own job. Switching is one undo step.
/// </summary>
public static class Pages
{
    /// <summary>Every page's name, in order.</summary>
    public static List<string> Names(DocState s)
    {
        var names = s.OtherPages.Select(p => p.Name).ToList();
        names.Insert(Math.Clamp(s.PageIndex, 0, names.Count), s.PageName);
        return names;
    }

    static DesignPage Current(DocState s) => new(s.PageName, s.Layers, s.Shapes, s.Guides);

    static List<DesignPage> All(DocState s)
    {
        var all = s.OtherPages.ToList();
        all.Insert(Math.Clamp(s.PageIndex, 0, all.Count), Current(s));
        return all;
    }

    static DocState Show(DocState s, List<DesignPage> all, int index)
    {
        index = Math.Clamp(index, 0, all.Count - 1);
        var p = all[index];
        var others = all.Where((_, i) => i != index).ToImmutableList();
        return s with { Layers = p.Layers, Shapes = p.Shapes, Guides = p.Guides, PageName = p.Name, PageIndex = index, OtherPages = others };
    }

    /// <summary>
    /// Change the layers <paramref name="which"/> picks on pages <paramref name="from"/> to
    /// <paramref name="from"/> + <paramref name="count"/> − 1 (each page has its own copy of its layers).
    /// </summary>
    public static DocState UpdateLayers(DocState s, int from, int count, Func<Layer, bool> which, Func<Layer, Layer> edit)
    {
        var all = All(s);
        for (int i = Math.Max(0, from); i < Math.Min(all.Count, from + count); i++)
            all[i] = all[i] with { Layers = [.. all[i].Layers.Select(l => which(l) ? edit(l) : l)] };
        return Show(s, all, s.PageIndex);
    }

    /// <summary>Show page <paramref name="index"/> (the one on screen is kept as it is).</summary>
    public static DocState SwitchTo(DocState s, int index) => index == s.PageIndex ? s : Show(s, All(s), index);

    /// <summary>A new, empty page after the current one, with the same layers (and their settings), shown.</summary>
    public static DocState Add(DocState s, string? name = null)
    {
        var all = All(s);
        var layers = s.Layers.Select(l => l with { Id = Guid.NewGuid() }).ToList();
        // Sub-layers keep pointing at their parent's new id.
        var map = s.Layers.Zip(layers).ToDictionary(x => x.First.Id, x => x.Second.Id);
        layers = [.. layers.Select(l => l.ParentId is { } p && map.TryGetValue(p, out var np) ? l with { ParentId = np } : l)];
        all.Insert(s.PageIndex + 1, new DesignPage(name ?? FreeName(all), [.. layers], [], []));
        return Show(s, all, s.PageIndex + 1);
    }

    /// <summary>A copy of the current page after it, shown.</summary>
    public static DocState Duplicate(DocState s)
    {
        var all = All(s);
        var layerMap = s.Layers.ToDictionary(l => l.Id, _ => Guid.NewGuid());
        var layers = s.Layers.Select(l => l with { Id = layerMap[l.Id], ParentId = l.ParentId is { } p && layerMap.TryGetValue(p, out var np) ? np : l.ParentId }).ToImmutableList();
        var groupMap = new Dictionary<Guid, Guid>();
        var shapes = s.Shapes.Select(sh =>
        {
            var c = sh.With(layerId: layerMap.TryGetValue(sh.LayerId, out var nl) ? nl : sh.LayerId, id: Guid.NewGuid());
            if (sh.GroupId is { } g)
            {
                if (!groupMap.TryGetValue(g, out var ng)) groupMap[g] = ng = Guid.NewGuid();
                c = c.WithGroup(ng);
            }
            return c;
        }).ToImmutableList();
        all.Insert(s.PageIndex + 1, new DesignPage($"{s.PageName} copy", layers, shapes, s.Guides));
        return Show(s, all, s.PageIndex + 1);
    }

    /// <summary>Remove the current page (never the last one) and show its neighbour.</summary>
    public static DocState Delete(DocState s)
    {
        if (s.OtherPages.Count == 0) return s;
        var all = All(s);
        all.RemoveAt(s.PageIndex);
        return Show(s, all, Math.Min(s.PageIndex, all.Count - 1));
    }

    public static DocState Rename(DocState s, string name) => string.IsNullOrWhiteSpace(name) ? s : s with { PageName = name.Trim() };

    /// <summary>Move the current page one place left (-1) or right (+1).</summary>
    public static DocState Move(DocState s, int by)
    {
        int to = Math.Clamp(s.PageIndex + by, 0, s.OtherPages.Count);
        return to == s.PageIndex ? s : s with { PageIndex = to };
    }

    static string FreeName(List<DesignPage> all)
    {
        for (int n = all.Count + 1; ; n++)
            if (all.All(p => p.Name != $"Page {n}")) return $"Page {n}";
    }
}
