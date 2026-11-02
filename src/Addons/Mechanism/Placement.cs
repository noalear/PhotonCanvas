using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>How the parts go into the design.</summary>
public sealed record PlaceOptions
{
    /// <summary>Sets the cut layer up from the library (its kerf included, so the parts come out at their drawn size); null leaves the layer as it is.</summary>
    public Func<Layer, Layer>? CutSettings { get; init; }
    /// <summary>Engrave the parts' names and marks (turning arrows, lines to line them up) on a light Score layer.</summary>
    public bool Marks { get; init; } = true;
    /// <summary>Space between parts and round the edge of the bed (mm).</summary>
    public double GapMm { get; init; } = 3;
    /// <summary>A mechanism made earlier to replace (its parts are taken off every page first and the new ones go on the page shown).</summary>
    public Guid? Replace { get; init; }
}

/// <summary>
/// Puts a mechanism's parts in the design: each outline on a Cut layer, its name and marks on a light Score
/// layer (engraved first, while the sheet is whole), grouped part by part and nested onto the bed by their
/// true shape; parts that don't fit go on further pages. Every shape carries the mechanism's settings, so
/// it can be opened again and changed.
/// </summary>
public static class Placement
{
    public const string CutLayer = "Mechanism · cut";
    public const string MarkLayer = "Mechanism · marks";
    /// <summary>Which mechanism a shape belongs to (an id shared by all its parts).</summary>
    public const string IdKey = "mechanism.id";
    /// <summary>The settings it was made from (JSON), on each part's outline.</summary>
    public const string SpecKey = "mechanism.spec";

    /// <summary>The mechanism a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, MechSpec Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var mid) || !Guid.TryParse(mid, out var g)) continue;
            // The settings are on the part's outline: the shape itself, or another in its group.
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == mid && x.AddonData.ContainsKey(SpecKey));
            if (withSpec is not null && MechSpec.FromJson(withSpec.AddonData[SpecKey]) is { } spec) return (g, spec);
        }
        return null;
    }

    static bool Of(Shape x, string mechanism) => x.AddonData.TryGetValue(IdKey, out var v) && v == mechanism;

    /// <summary>The shapes of a mechanism on the page shown.</summary>
    public static HashSet<Guid> ShapesOf(DocState s, Guid mechanism)
    {
        string id = mechanism.ToString();
        return [.. s.Shapes.Where(x => Of(x, id)).Select(x => x.Id)];
    }

    /// <summary>Is any part of the mechanism on any page.</summary>
    public static bool Exists(DocState s, Guid mechanism)
    {
        string id = mechanism.ToString();
        return s.Shapes.Any(x => Of(x, id)) || s.OtherPages.Any(p => p.Shapes.Any(x => Of(x, id)));
    }

    /// <summary>
    /// Takes a mechanism's parts off every page. Other pages that leaves empty go too (the ones its parts
    /// spilled onto); the page shown stays, and stays shown.
    /// </summary>
    public static DocState Remove(DocState s, Guid mechanism)
    {
        int shown = s.PageIndex;
        // From the last page back, so the pages before each one keep their places.
        for (int i = s.OtherPages.Count; i >= 0; i--)
        {
            var page = Pages.SwitchTo(s, i);
            var mine = ShapesOf(page, mechanism);
            if (mine.Count == 0) continue;
            page = Ops.Delete(page, mine);
            if (i != shown && page.Shapes.Count == 0 && page.OtherPages.Count > 0)
            {
                page = Pages.Delete(page);
                if (i < shown) shown--;
            }
            s = page;
        }
        return Pages.SwitchTo(s, shown);
    }

    static Vec2 Flip(Vec2 v) => new(v.X, -v.Y);

    /// <summary>Does a part this size fit on the bed with the gap round it (turned a quarter if need be).</summary>
    static bool Fits(Rect2 box, MachineProfile machine, double gap) =>
        Math.Max(box.Width, box.Height) + 2 * gap <= Math.Max(machine.FieldW, machine.FieldH) + 1e-6
        && Math.Min(box.Width, box.Height) + 2 * gap <= Math.Min(machine.FieldW, machine.FieldH) + 1e-6;

    /// <summary>The parts too big for the machine's bed, as findings to show before anything is put in.</summary>
    public static IEnumerable<Finding> TooBig(Mechanism m, MachineProfile machine, double gapMm = 3)
    {
        foreach (var part in m.Parts)
        {
            var box = Poly.Bounds(part.Shape);
            if (box.IsEmpty || Fits(box, machine, gapMm)) continue;
            yield return new Finding(Severity.Bad, $"The {Say.Lower(part.Name)} is {box.Width:0} × {box.Height:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. Make it smaller (fewer or smaller teeth, less travel).");
        }
    }

    public static (DocState State, int Sheets, Guid Id) Place(DocState s, Mechanism m, MechSpec spec, MachineProfile machine, PlaceOptions o, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        string title = m.Title;
        // Made again: the old parts go (from every page they were on) and the new ones go on the page shown.
        if (o.Replace is { } old) s = Remove(s, old);
        // A new one: a page of its own when this one already has something on it.
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;

        (Layer Cut, Layer? Marks) Layers(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == CutLayer);
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
                st = Ops.AddLayer(st, cut);
            }
            Layer? mk = null;
            if (o.Marks)
            {
                mk = st.Layers.FirstOrDefault(l => l.Name == MarkLayer);
                if (mk is null)
                {
                    var score = Layer.Default(st.Layers.Count, LayerMode.Score);
                    mk = score with { Name = MarkLayer, Laser = score.Laser with { Power = Math.Min(score.Laser.Power, 15) } };
                    // Marked before anything is cut: cut parts can shift or drop.
                    var cutId = cut.Id;
                    st = Ops.AddLayer(st, mk, st.Layers.FindIndex(l => l.Id == cutId));
                }
            }
            return (cut, mk);
        }
        var (cutLayer, markLayer) = Layers(ref s);

        // Each part (each copy of it) with its marks, made at the bed's corner; the nesting moves it into place.
        if (TooBig(m, machine, o.GapMm).FirstOrDefault() is { } tooBig) throw new InvalidOperationException(tooBig.Text);
        var units = new List<List<Shape>>();
        foreach (var part in m.Parts)
        {
            ct.ThrowIfCancellationRequested();
            if (part.Shape.Count == 0) continue;
            var outline = Poly.ToGeometry(part.Shape).Transform(Affine.Scale(1, -1));
            var box = outline.Bounds();
            if (box.IsEmpty) continue;
            var move = Affine.Translate(o.GapMm - box.MinX, o.GapMm - box.MinY);
            outline = outline.Transform(move);
            PathGeometry? marks = null;
            if (markLayer is not null && part.Marks.Count > 0)
                marks = new PathGeometry(part.Marks.Where(l => l.Count >= 2).SelectMany(l => PathGeometry.Polyline([.. l.Select(Flip)], false).Subpaths)).Transform(move);
            TextShape? label = null;
            if (markLayer is not null && part.Label is { } lab)
            {
                var text = TextShape.Create(lab.Text, new TextStyle("Arial", lab.HeightMm, SingleLine: true), markLayer.Id, Affine.Identity);
                var tb = text.Bounds;
                var at = move.Apply(Flip(lab.At));
                var placed = text.With(transform: Affine.Translate(-tb.Center.X, -tb.Center.Y).Then(Affine.Rotate(-lab.AngleDeg)).Then(Affine.Translate(at.X, at.Y)));
                // Only where it's all on the part (never over a hole or off the edge).
                var pb = placed.Bounds;
                var inside = Poly.FromGeometry(outline);
                if (!pb.IsEmpty && new[] { new Vec2(pb.MinX, pb.MinY), new Vec2(pb.MaxX, pb.MinY), new Vec2(pb.MaxX, pb.MaxY), new Vec2(pb.MinX, pb.MaxY), pb.Center }.All(p => Poly.Inside(inside, p))
                    && Poly.Area(Poly.Subtract([Poly.Rect(pb.MinX, pb.MinY, pb.MaxX, pb.MaxY)], inside)) < 0.01)
                    label = (TextShape)placed;
            }
            for (int c = 0; c < part.Copies; c++)
            {
                string name = part.Copies > 1 ? $"{part.Name} ({c + 1} of {part.Copies})" : part.Name;
                var shapes = new List<Shape> { new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(SpecKey, json) };
                if (marks is not null) shapes.Add(new PathShape { Name = $"{name} · marks", LayerId = markLayer!.Id, Geometry = marks });
                if (label is not null) shapes.Add(label.With(id: Guid.NewGuid(), name: $"{name} · name"));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add([.. shapes.Select(x => x.WithGroup(g).WithAddonData(IdKey, id.ToString()))]);
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");

        // Bed by bed: everything left goes on the page, the nesting places what fits, the rest moves on.
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            // Each page has its own layers: the outline is first in its part, the rest are marks.
            var onPage = units.Select(u => u.Select((sh, k) => sh.With(layerId: k == 0 ? cutLayer.Id : markLayer!.Id)).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A part doesn't fit on the bed with the spacing round it. Make it smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            (cutLayer, markLayer) = Layers(ref s);
        }
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
        return (Pages.SwitchTo(s, firstPage), sheets, id);
    }
}
