using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>How the pieces go into the design.</summary>
public sealed record PlaceOptions
{
    /// <summary>Sets the cut layer up from the library (its kerf included, so sliding parts come out their drawn size); null leaves it as it is.</summary>
    public Func<Layer, Layer>? CutSettings { get; init; }
    /// <summary>Sets the etch layer up from the library; null leaves it as it is.</summary>
    public Func<Layer, Layer>? EtchSettings { get; init; }
    /// <summary>Space between pieces and round the edge of the bed (mm).</summary>
    public double GapMm { get; init; } = 2;
    /// <summary>A box made earlier to replace (its pieces are taken off every page first and the new ones go on the page shown).</summary>
    public Guid? Replace { get; init; }
}

/// <summary>
/// Puts a box in the design: each piece's outline on a Cut layer and its letters, thumb grips and
/// message on an etch (Fill) layer done first, while the sheet is whole; grouped piece by piece and
/// nested onto the bed, more pages if they need more than one. Every outline carries the settings,
/// so the box can be opened again and changed.
/// </summary>
public static class Placement
{
    public const string CutLayer = "Puzzle box · cut";
    public const string EtchLayer = "Puzzle box · etch";
    /// <summary>Which box a shape belongs to (an id shared by all its pieces).</summary>
    public const string IdKey = "puzzlebox.id";
    /// <summary>The settings it was made from (JSON), on each piece's outline.</summary>
    public const string SpecKey = "puzzlebox.spec";

    /// <summary>The box a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, BoxSpec Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var wid) || !Guid.TryParse(wid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == wid && x.AddonData.ContainsKey(SpecKey));
            if (withSpec is not null && BoxSpec.FromJson(withSpec.AddonData[SpecKey]) is { } spec) return (g, spec);
        }
        return null;
    }

    static bool Of(Shape x, string box) => x.AddonData.TryGetValue(IdKey, out var v) && v == box;

    public static HashSet<Guid> ShapesOf(DocState s, Guid box)
    {
        string id = box.ToString();
        return [.. s.Shapes.Where(x => Of(x, id)).Select(x => x.Id)];
    }

    /// <summary>How many of a box's shapes there are on every page.</summary>
    public static int CountEverywhere(DocState s, Guid box)
    {
        string id = box.ToString();
        return s.Shapes.Count(x => Of(x, id)) + s.OtherPages.Sum(p => p.Shapes.Count(x => Of(x, id)));
    }

    /// <summary>Takes a box's pieces off every page; other pages that leaves empty go too. The page shown stays.</summary>
    public static DocState Remove(DocState s, Guid box)
    {
        int shown = s.PageIndex;
        for (int i = s.OtherPages.Count; i >= 0; i--)
        {
            var page = Pages.SwitchTo(s, i);
            var mine = ShapesOf(page, box);
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

    static bool Fits(double w, double h, MachineProfile machine, double gap) =>
        Math.Max(w, h) + 2 * gap <= Math.Max(machine.FieldW, machine.FieldH) + 1e-6
        && Math.Min(w, h) + 2 * gap <= Math.Min(machine.FieldW, machine.FieldH) + 1e-6;

    /// <summary>The pieces too big for the bed, as findings to show before anything is put in.</summary>
    public static IEnumerable<Finding> TooBig(BoxKit kit, MachineProfile machine, double gapMm = 2)
    {
        foreach (var part in kit.Parts)
        {
            var b = Geo.Bounds(part.Cut);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (w <= 0 || Fits(w, h, machine, gapMm)) continue;
            yield return new Finding(Severity.Bad, $"The {part.Name.ToLowerInvariant()} is {w:0} × {h:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. Make the box smaller.");
            yield break;
        }
    }

    public static (DocState State, int Sheets, Guid Id) Place(DocState s, BoxKit kit, MachineProfile machine, PlaceOptions o, CancellationToken ct = default)
    {
        var spec = kit.Design.Spec;
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        const string title = "Puzzle box";
        if (o.Replace is { } old) s = Remove(s, old);
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;
        int boxes = Math.Max(1, spec.Count);
        bool anyEtch = kit.Parts.Any(p => p.Etched.Count > 0);

        (Layer Cut, Layer? Etch) Layers(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == CutLayer);
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
                st = Ops.AddLayer(st, cut);
            }
            Layer? etch = null;
            if (anyEtch)
            {
                etch = st.Layers.FirstOrDefault(x => x.Name == EtchLayer);
                if (etch is null)
                {
                    etch = Layer.Default(st.Layers.Count, LayerMode.Fill) with { Name = EtchLayer };
                    // Etched before anything is cut: cut pieces can shift or drop.
                    var cutId = cut.Id;
                    st = Ops.AddLayer(st, etch, st.Layers.FindIndex(x => x.Id == cutId));
                }
            }
            return (cut, etch);
        }
        var (cutLayer, etchLayer) = Layers(ref s);

        if (TooBig(kit, machine, o.GapMm).FirstOrDefault() is { } tooBig) throw new InvalidOperationException(tooBig.Text);
        // Each piece (each copy of it) made at the bed's corner; the nesting moves it into place. Kind of each shape: 0 cut, 1 etch.
        var units = new List<List<(Shape Shape, int Kind)>>();
        foreach (var part in kit.Parts)
        {
            ct.ThrowIfCancellationRequested();
            var outline = Geo.ToGeometry(part.Cut);
            var box = outline.Bounds();
            if (box.IsEmpty) continue;
            var move = Affine.Translate(o.GapMm - box.MinX, o.GapMm - box.MinY);
            outline = outline.Transform(move);
            var etched = part.Etched.Count > 0 ? Geo.ToGeometry(part.Etched).Transform(move) : null;
            int count = part.Copies * boxes;
            for (int c = 0; c < count; c++)
            {
                string name = part.Name + (count > 1 ? $" ({c + 1} of {count})" : "");
                var shapes = new List<(Shape, int)> { (new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(SpecKey, json), 0) };
                if (etched is not null) shapes.Add((new PathShape { Name = $"{name} · etching", LayerId = etchLayer!.Id, Geometry = etched }, 1));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add([.. shapes.Select(x => (x.Item1.WithGroup(g).WithAddonData(IdKey, id.ToString()), x.Item2))]);
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");

        // Biggest first, so the layers get the room and the small pieces fill round them.
        units = [.. units.OrderByDescending(u => { var b = u[0].Shape.Bounds; return b.Width * b.Height; })];
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var onPage = units.Select(u => u.Select(x => x.Shape.With(layerId: x.Kind == 0 ? cutLayer.Id : etchLayer!.Id)).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A piece doesn't fit on the bed with the spacing round it. Make the box smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            (cutLayer, etchLayer) = Layers(ref s);
        }
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
        if (o.EtchSettings is { } etchSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == EtchLayer, l => etchSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Fill, ParentId = l.ParentId });
        return (Pages.SwitchTo(s, firstPage), sheets, id);
    }
}
