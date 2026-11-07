namespace PhotonCanvas.Doc;

/// <summary>
/// The mask layer: where the shapes that mask pictures (Image mask) and other shapes (Mask with
/// shape) go once they've done their job, so they stay on the work area to line things up with
/// and to mask again, but are never cut or marked. Its output can't be turned on
/// (<see cref="Doc.Layer.Mask"/>), and neither can a sub-layer of it.
/// </summary>
public static class MaskLayer
{
    public const string LayerName = "Mask (never burned)";
    /// <summary>Purple, drawn faded like every layer that isn't sent.</summary>
    public const uint Colour = 0xA855F7;

    /// <summary>The page's mask layer, if it has one.</summary>
    public static Layer? Layer(DocState s) => s.Layers.FirstOrDefault(l => l.Mask && l.ParentId is null);

    /// <summary>The page's mask layer, made (last in the list) if it isn't there yet.</summary>
    public static (DocState State, Layer Layer) Ensure(DocState s)
    {
        if (Layer(s) is { } have) return (s, have);
        var layer = Doc.Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = LayerName, Mask = true, Output = false, Color = Colour };
        return (Ops.AddLayer(s, layer), layer);
    }

    /// <summary>The shapes move to the mask layer (made if need be). Pictures stay where they are.</summary>
    public static DocState Mark(DocState s, IReadOnlySet<Guid> ids)
    {
        var paths = s.Shapes.Where(x => ids.Contains(x.Id) && x is PathShape).Select(x => x.Id).ToHashSet();
        if (paths.Count == 0) return s;
        (s, var layer) = Ensure(s);
        return Ops.MoveToLayer(s, paths, layer.Id);
    }

    /// <summary>Whether a layer is the mask layer, or a sub-layer of it: never sent.</summary>
    public static bool IsMask(DocState s, Layer l) => s.FindLayer(s.Owner(l.Id)) is { Mask: true };

    /// <summary>
    /// Shapes on layers that are sent which look like a picture's mask (their closed outlines are
    /// where its mask is): left on a cut layer, they'd be cut round the picture. For the job's checks.
    /// </summary>
    public static IEnumerable<(PathShape Shape, Layer Layer, ImageShape Picture)> LeftOnOutput(DocState s, IEnumerable<Layer> sent)
    {
        var clips = s.Shapes.OfType<ImageShape>().Where(im => im.Clip is not null)
            .Select(im => (Picture: im, Outline: im.Clip!.Transform(im.Transform)))
            .Select(x => (x.Picture, Count: x.Outline.Subpaths.Count(sp => sp.Closed), Box: x.Outline.Bounds()))
            .Where(x => x.Count > 0 && !x.Box.IsEmpty)
            .ToList();
        if (clips.Count == 0) yield break;
        var seen = new HashSet<Guid>();
        foreach (var layer in sent)
            foreach (var p in s.ShapesFor(layer).OfType<PathShape>())
            {
                if (p is TextShape or CodeShape || seen.Contains(p.Id)) continue;
                var region = Geometry.Mask.Region(p.World);
                int n = region.Subpaths.Length;
                if (n == 0) continue;
                var box = region.Bounds();
                foreach (var c in clips)
                    if (c.Count == n && Near(c.Box, box))
                    {
                        seen.Add(p.Id);
                        yield return (p, layer, c.Picture);
                        break;
                    }
            }
    }

    static bool Near(Core.Rect2 a, Core.Rect2 b, double tol = 0.05) =>
        Math.Abs(a.MinX - b.MinX) <= tol && Math.Abs(a.MinY - b.MinY) <= tol && Math.Abs(a.MaxX - b.MaxX) <= tol && Math.Abs(a.MaxY - b.MaxY) <= tol;
}
