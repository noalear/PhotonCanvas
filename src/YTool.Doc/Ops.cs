using System.Collections.Immutable;
using YTool.Core;

namespace YTool.Doc;

public enum Align { Left, CenterX, Right, Top, CenterY, Bottom }

/// <summary>
/// Pure edits on a <see cref="DocState"/>. Each returns a new state (or the
/// same instance when nothing changed, so no-op edits don't pollute undo).
/// </summary>
public static class Ops
{
    public static DocState AddShapes(DocState s, IEnumerable<Shape> shapes)
    {
        var list = shapes.ToList();
        return list.Count == 0 ? s : s with { Shapes = s.Shapes.AddRange(list) };
    }

    /// <summary>Swap in a new version of a shape (same id).</summary>
    public static DocState Replace(DocState s, Shape shape)
    {
        int i = s.Shapes.FindIndex(x => x.Id == shape.Id);
        return i < 0 || ReferenceEquals(s.Shapes[i], shape) ? s : s with { Shapes = s.Shapes.SetItem(i, shape) };
    }

    public static DocState Delete(DocState s, IReadOnlySet<Guid> ids)
    {
        var next = s.Shapes.RemoveAll(x => ids.Contains(x.Id));
        return next.Count == s.Shapes.Count ? s : s with { Shapes = next };
    }

    /// <summary>Apply a world-space transform to the given shapes.</summary>
    public static DocState Transform(DocState s, IReadOnlySet<Guid> ids, Affine world)
    {
        if (ids.Count == 0 || world.IsIdentity) return s;
        return s with
        {
            Shapes = s.Shapes.Select(x => ids.Contains(x.Id) ? x.With(transform: x.Transform.Then(world)) : x).ToImmutableList(),
        };
    }

    public static DocState Move(DocState s, IReadOnlySet<Guid> ids, double dx, double dy) =>
        dx == 0 && dy == 0 ? s : Transform(s, ids, Affine.Translate(dx, dy));

    /// <summary>Copies of the shapes, offset by (dx, dy). Returns the new ids.</summary>
    public static (DocState State, List<Guid> NewIds) Duplicate(DocState s, IReadOnlySet<Guid> ids, double dx, double dy)
    {
        var copies = s.Shapes.Where(x => ids.Contains(x.Id))
            .Select(x => x.With(id: Guid.NewGuid(), transform: x.Transform.Then(Affine.Translate(dx, dy))))
            .ToList();
        return (AddShapes(s, copies), copies.Select(c => c.Id).ToList());
    }

    public static DocState MoveToLayer(DocState s, IReadOnlySet<Guid> ids, Guid layerId)
    {
        if (s.FindLayer(layerId) is null) return s;
        bool any = false;
        var shapes = s.Shapes.Select(x =>
        {
            if (!ids.Contains(x.Id) || x.LayerId == layerId) return x;
            any = true;
            return x.With(layerId: layerId);
        }).ToImmutableList();
        return any ? s with { Shapes = shapes } : s;
    }

    /// <summary>Move shapes to the top (end of the draw order).</summary>
    public static DocState BringToFront(DocState s, IReadOnlySet<Guid> ids)
    {
        var picked = s.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        return picked.Count == 0 ? s : s with { Shapes = s.Shapes.RemoveAll(x => ids.Contains(x.Id)).AddRange(picked) };
    }

    public static DocState SendToBack(DocState s, IReadOnlySet<Guid> ids)
    {
        var picked = s.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        return picked.Count == 0 ? s : s with { Shapes = s.Shapes.RemoveAll(x => ids.Contains(x.Id)).InsertRange(0, picked) };
    }

    /// <summary>
    /// Align shapes to the edge/centre of <paramref name="target"/> (usually the
    /// selection bounds, or the work area).
    /// </summary>
    public static DocState AlignTo(DocState s, IReadOnlySet<Guid> ids, Align how, Rect2 target)
    {
        bool any = false;
        var shapes = s.Shapes.Select(x =>
        {
            if (!ids.Contains(x.Id)) return x;
            var b = x.Bounds;
            double dx = 0, dy = 0;
            switch (how)
            {
                case Align.Left: dx = target.MinX - b.MinX; break;
                case Align.CenterX: dx = target.Center.X - b.Center.X; break;
                case Align.Right: dx = target.MaxX - b.MaxX; break;
                case Align.Top: dy = target.MinY - b.MinY; break;
                case Align.CenterY: dy = target.Center.Y - b.Center.Y; break;
                case Align.Bottom: dy = target.MaxY - b.MaxY; break;
            }
            if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12) return x;
            any = true;
            return x.With(transform: x.Transform.Then(Affine.Translate(dx, dy)));
        }).ToImmutableList();
        return any ? s with { Shapes = shapes } : s;
    }

    /// <summary>Merge path shapes into one compound shape (holes fill correctly).</summary>
    public static (DocState State, Guid? NewId) Combine(DocState s, IReadOnlySet<Guid> ids)
    {
        var paths = s.Shapes.OfType<PathShape>().Where(x => ids.Contains(x.Id)).ToList();
        if (paths.Count < 2) return (s, null);
        var combined = new PathShape
        {
            Name = "Combined",
            LayerId = paths[0].LayerId,
            Geometry = new PathGeometry(paths.SelectMany(p => p.World.Subpaths)),
        };
        int at = s.Shapes.IndexOf(paths[^1]);
        var shapes = s.Shapes.Insert(at + 1, combined).RemoveAll(x => ids.Contains(x.Id) && x is PathShape);
        return (s with { Shapes = shapes }, combined.Id);
    }

    /// <summary>Split compound shapes into one shape per subpath.</summary>
    public static (DocState State, List<Guid> NewIds) BreakApart(DocState s, IReadOnlySet<Guid> ids)
    {
        var newIds = new List<Guid>();
        var shapes = ImmutableList.CreateBuilder<Shape>();
        foreach (var x in s.Shapes)
        {
            if (ids.Contains(x.Id) && x is PathShape p && p.Geometry.Subpaths.Length > 1)
            {
                int n = 0;
                foreach (var sp in p.World.Subpaths)
                {
                    var part = new PathShape { Name = $"{p.Name} {++n}".Trim(), LayerId = p.LayerId, Geometry = new([sp]) };
                    shapes.Add(part);
                    newIds.Add(part.Id);
                }
            }
            else shapes.Add(x);
        }
        return newIds.Count == 0 ? (s, newIds) : (s with { Shapes = shapes.ToImmutable() }, newIds);
    }

    // ---- layers -------------------------------------------------------------

    public static DocState AddLayer(DocState s, Layer layer, int? index = null) =>
        s with { Layers = s.Layers.Insert(Math.Clamp(index ?? s.Layers.Count, 0, s.Layers.Count), layer) };

    /// <summary>Apply <paramref name="edit"/> to every listed layer (bulk editing).</summary>
    public static DocState UpdateLayers(DocState s, IReadOnlySet<Guid> ids, Func<Layer, Layer> edit)
    {
        bool any = false;
        var layers = s.Layers.Select(l =>
        {
            if (!ids.Contains(l.Id)) return l;
            var n = edit(l);
            any |= n != l;
            return n;
        }).ToImmutableList();
        return any ? s with { Layers = layers } : s;
    }

    /// <summary>
    /// Delete layers. Their shapes move to <paramref name="moveShapesTo"/>, or
    /// are deleted when it is null. At least one layer always remains.
    /// </summary>
    public static DocState DeleteLayers(DocState s, IReadOnlySet<Guid> ids, Guid? moveShapesTo)
    {
        var remaining = s.Layers.RemoveAll(l => ids.Contains(l.Id));
        if (remaining.Count == s.Layers.Count) return s;
        if (remaining.Count == 0) remaining = [Layer.Default(0)];
        var target = moveShapesTo is { } t && remaining.Any(l => l.Id == t) ? t : (Guid?)null;
        var shapes = target is { } dest
            ? s.Shapes.Select(x => ids.Contains(x.LayerId) ? x.With(layerId: dest) : x).ToImmutableList()
            : s.Shapes.RemoveAll(x => ids.Contains(x.LayerId));
        // Shapes on a layer that vanished without a target would be orphans.
        var valid = remaining.Select(l => l.Id).ToHashSet();
        shapes = shapes.Select(x => valid.Contains(x.LayerId) ? x : x.With(layerId: remaining[0].Id)).ToImmutableList();
        return s with { Layers = remaining, Shapes = shapes };
    }

    public static DocState MoveLayer(DocState s, Guid id, int newIndex)
    {
        int i = s.LayerIndex(id);
        newIndex = Math.Clamp(newIndex, 0, s.Layers.Count - 1);
        if (i < 0 || i == newIndex) return s;
        var l = s.Layers[i];
        return s with { Layers = s.Layers.RemoveAt(i).Insert(newIndex, l) };
    }

    /// <summary>Move every shape of the listed layers onto the first of them and delete the rest.</summary>
    public static DocState MergeLayers(DocState s, IReadOnlyList<Guid> ids)
    {
        var ordered = s.Layers.Where(l => ids.Contains(l.Id)).ToList();
        if (ordered.Count < 2) return s;
        var keep = ordered[0].Id;
        var drop = ordered.Skip(1).Select(l => l.Id).ToHashSet();
        return DeleteLayers(s, drop, keep);
    }

    /// <summary>A copy of a layer and all its shapes, placed right after it.</summary>
    public static (DocState State, Guid NewLayer) DuplicateLayer(DocState s, Guid id)
    {
        var l = s.FindLayer(id);
        if (l is null) return (s, Guid.Empty);
        var copy = l with { Id = Guid.NewGuid(), Name = l.Name + " copy" };
        var shapes = s.ShapesOn(id).Select(x => x.With(id: Guid.NewGuid(), layerId: copy.Id)).ToList();
        var next = AddLayer(s, copy, s.LayerIndex(id) + 1);
        return (AddShapes(next, shapes), copy.Id);
    }
}
