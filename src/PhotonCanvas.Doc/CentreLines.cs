namespace PhotonCanvas.Doc;

/// <summary>
/// The centrelines layer: where the Centreline tool puts centrelines and centre marks unless
/// they're to be marked. It's an ordinary Score layer with its output off, so they're there to
/// line things up with and to snap to, but aren't sent until you turn its output on.
/// </summary>
public static class CentreLines
{
    public const string LayerName = "Centrelines";
    /// <summary>Slate grey, drawn faded and dash-dot like a drawing's centrelines.</summary>
    public const uint Colour = 0x94A3B8;

    /// <summary>The page's centrelines layer, if it has one.</summary>
    public static Layer? Layer(DocState s) => s.Layers.FirstOrDefault(l => l.Centrelines && l.ParentId is null);

    /// <summary>The page's centrelines layer, made (last in the list, not marked) if it isn't there yet.</summary>
    public static (DocState State, Layer Layer) Ensure(DocState s)
    {
        if (Layer(s) is { } have) return (s, have);
        var layer = Doc.Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = LayerName, Centrelines = true, Output = false, Color = Colour };
        return (Ops.AddLayer(s, layer), layer);
    }

    /// <summary>Whether a layer is the centrelines layer, or a sub-layer of it (their lines are drawn dash-dot).</summary>
    public static bool IsCentrelines(DocState s, Layer l) => s.FindLayer(s.Owner(l.Id)) is { Centrelines: true };
}
