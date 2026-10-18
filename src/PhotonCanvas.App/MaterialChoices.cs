using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>Where parts can go: the whole bed, the material outlined on the page (all of it, or one piece), or another closed shape.</summary>
public sealed record MaterialChoice(string Label, PathGeometry? Outline, Guid? ShapeId)
{
    /// <summary>The choices for this page, material outlines first; the first material choice is the one to start on.</summary>
    public static (List<MaterialChoice> Choices, int Default) For(DocState s, double bedW, double bedH, IReadOnlySet<Guid>? excluding = null, bool otherShapes = false)
    {
        var list = new List<MaterialChoice> { new($"The whole bed ({bedW:0} × {bedH:0} mm)", null, null) };
        var outlines = MaterialOutline.On(s);
        if (outlines.Count > 1)
            list.Add(new($"All {outlines.Count} material outlines", MaterialOutline.Combined(outlines), null));
        foreach (var p in outlines)
            list.Add(new($"Material · {Name(p)} · {p.Bounds.Width:0} × {p.Bounds.Height:0} mm", p.World, p.Id));
        if (otherShapes)
            foreach (var p in s.Shapes.OfType<PathShape>().Where(p => excluding?.Contains(p.Id) != true && !outlines.Contains(p) && p is not TextShape
                         && p.Geometry.Subpaths.Any(sp => sp.Closed) && p.Bounds.Width * p.Bounds.Height > 100))
                list.Add(new($"{Name(p)} · {p.Bounds.Width:0} × {p.Bounds.Height:0} mm", p.World, p.Id));
        return (list, outlines.Count > 0 ? 1 : 0);
    }

    static string Name(PathShape p) => string.IsNullOrWhiteSpace(p.Name) ? "Shape" : p.Name;
}
