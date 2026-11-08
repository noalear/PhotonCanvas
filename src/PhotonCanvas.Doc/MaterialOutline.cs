namespace PhotonCanvas.Doc;

/// <summary>
/// The material actually in the machine (a board, an offcut, a scrap with a knot in it), drawn
/// with the shape tools or picked from the camera: closed shapes on the "Material" layer, which
/// is shown but never sent to the laser. Nesting and the model slicer fill it instead of the
/// whole bed; a hole in an outline (a knot, a cut-out) is kept clear.
/// </summary>
public static class MaterialOutline
{
    public const string LayerName = "Material";

    /// <summary>The page's material layer, if it has one.</summary>
    public static Layer? Layer(DocState s) => s.Layers.FirstOrDefault(l => l.Name == LayerName && !l.Output);

    /// <summary>
    /// A sub-layer of the material layer: it would go over the material outlines again with its own
    /// settings, so it's never sent either, whatever its switch says.
    /// </summary>
    public static bool IsUnder(DocState s, Doc.Layer l) => l.ParentId is not null && Layer(s) is { } m && s.Owner(l.Id) == m.Id;

    /// <summary>The page's material layer, made if it isn't there yet (grey, never sent).</summary>
    public static (DocState State, Layer Layer) Ensure(DocState s)
    {
        if (Layer(s) is { } have) return (s, have);
        var layer = Doc.Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = LayerName, Output = false, Color = 0x8A8F98 };
        return (Ops.AddLayer(s, layer), layer);
    }

    /// <summary>The shapes become material outlines (moved to the material layer).</summary>
    public static DocState Mark(DocState s, IReadOnlySet<Guid> ids)
    {
        (s, var layer) = Ensure(s);
        return Ops.MoveToLayer(s, ids, layer.Id);
    }

    /// <summary>The material outlines on the page: closed shapes on the material layer, biggest first.</summary>
    public static IReadOnlyList<PathShape> On(DocState s) =>
        Layer(s) is not { } layer ? [] :
        [.. s.Shapes.OfType<PathShape>()
            .Where(p => p.LayerId == layer.Id && p is not TextShape && p.Geometry.Subpaths.Any(sp => sp.Closed) && p.Bounds.Width * p.Bounds.Height > 25)
            .OrderByDescending(p => p.Bounds.Width * p.Bounds.Height)];

    /// <summary>Several outlines as one region to fill (their closed outlines; holes stay holes).</summary>
    public static PathGeometry Combined(IEnumerable<PathShape> outlines) =>
        new(outlines.SelectMany(p => p.World.Subpaths.Where(sp => sp.Closed)));
}
