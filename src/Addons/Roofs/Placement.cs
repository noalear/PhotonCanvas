using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>How the pieces go into the design.</summary>
public sealed record PlaceOptions
{
    /// <summary>Sets the cut layer up from the library (its kerf included, so slots come out their drawn width); null leaves it as it is.</summary>
    public Func<Layer, Layer>? CutSettings { get; init; }
    /// <summary>Sets the etch layer up from the library; null leaves it as it is.</summary>
    public Func<Layer, Layer>? EtchSettings { get; init; }
    /// <summary>Space between pieces and round the edge of the bed (mm).</summary>
    public double GapMm { get; init; } = 2;
    /// <summary>A set made earlier to replace (its pieces are taken off every page first and the new ones go on the page shown).</summary>
    public Guid? Replace { get; init; }
}

/// <summary>
/// Puts a roof or tower in the design (as the wall maker does its walls): each piece's outline on a Cut
/// layer, the shingles or stonework on an etch (Fill) layer and its lines on a light Score layer (both
/// done before anything is cut, while the sheet is whole), grouped piece by piece and nested onto the
/// bed; pieces that don't fit go on further pages. Every shape carries the settings, so it can be opened
/// again and changed.
/// </summary>
public static class Placement
{
    public const string CutLayer = "Roofs · cut";
    public const string EtchLayer = "Roofs · etch";
    public const string LineLayer = "Roofs · lines";
    /// <summary>Which set a shape belongs to (an id shared by all its pieces).</summary>
    public const string IdKey = "roofs.id";
    /// <summary>The settings it was made from (JSON), on each piece's outline.</summary>
    public const string SpecKey = "roofs.spec";

    /// <summary>The set a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, RoofsDesign Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var wid) || !Guid.TryParse(wid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == wid && x.AddonData.ContainsKey(SpecKey));
            if (withSpec is not null && RoofsDesign.FromJson(withSpec.AddonData[SpecKey]) is { } spec) return (g, spec);
        }
        return null;
    }

    static bool Of(Shape x, string set) => x.AddonData.TryGetValue(IdKey, out var v) && v == set;

    public static HashSet<Guid> ShapesOf(DocState s, Guid set)
    {
        string id = set.ToString();
        return [.. s.Shapes.Where(x => Of(x, id)).Select(x => x.Id)];
    }

    /// <summary>Takes a set's pieces off every page; other pages that leaves empty go too. The page shown stays.</summary>
    public static DocState Remove(DocState s, Guid set)
    {
        int shown = s.PageIndex;
        for (int i = s.OtherPages.Count; i >= 0; i--)
        {
            var page = Pages.SwitchTo(s, i);
            var mine = ShapesOf(page, set);
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

    static bool Fits(Rect2 box, MachineProfile machine, double gap) =>
        Math.Max(box.Width, box.Height) + 2 * gap <= Math.Max(machine.FieldW, machine.FieldH) + 1e-6
        && Math.Min(box.Width, box.Height) + 2 * gap <= Math.Min(machine.FieldW, machine.FieldH) + 1e-6;

    /// <summary>The pieces too big for the bed, as findings to show before anything is put in.</summary>
    public static IEnumerable<Finding> TooBig(RoofKit kit, MachineProfile machine, double gapMm = 2)
    {
        foreach (var part in kit.Parts)
        {
            var b = Geo.Bounds(part.Cut);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (w <= 0 || Fits(new Rect2(0, 0, w, h), machine, gapMm)) continue;
            yield return new Finding(Severity.Bad, $"The {part.Name.ToLowerInvariant()} is {w:0} × {h:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. Make it smaller.");
        }
    }

    public static (DocState State, int Sheets, Guid Id) Place(DocState s, RoofKit kit, RoofsDesign spec, MachineProfile machine, PlaceOptions o, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        string title = spec.Tower is not null ? "Tower" : "Roof";
        if (o.Replace is { } old) s = Remove(s, old);
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;
        bool anyEtch = kit.Parts.Any(p => p.Etched.Count > 0), anyLines = kit.Parts.Any(p => p.Lines.Count > 0);

        (Layer Cut, Layer? Etch, Layer? Lines) Layers(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == CutLayer);
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
                st = Ops.AddLayer(st, cut);
            }
            Layer? Before(ref DocState st2, bool want, string name, LayerMode mode, Func<Layer, Layer> setup)
            {
                if (!want) return null;
                var l = st2.Layers.FirstOrDefault(x => x.Name == name);
                if (l is not null) return l;
                l = setup(Layer.Default(st2.Layers.Count, mode) with { Name = name });
                // Etched before anything is cut: cut pieces can shift or drop.
                var cutId = cut.Id;
                st2 = Ops.AddLayer(st2, l, st2.Layers.FindIndex(x => x.Id == cutId));
                return l;
            }
            var etch = Before(ref st, anyEtch, EtchLayer, LayerMode.Fill, l => l);
            var lines = Before(ref st, anyLines, LineLayer, LayerMode.Score, l => l with { Laser = l.Laser with { Power = Math.Min(l.Laser.Power, 15) } });
            return (cut, etch, lines);
        }
        var (cutLayer, etchLayer, lineLayer) = Layers(ref s);

        if (TooBig(kit, machine, o.GapMm).FirstOrDefault() is { } tooBig) throw new InvalidOperationException(tooBig.Text);
        // Each piece (each copy of it) made at the bed's corner; the nesting moves it into place. Kind of each shape: 0 cut, 1 etch, 2 lines.
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
            var lines = part.Lines.Count > 0 ? Geo.ToGeometry([], part.Lines).Transform(move) : null;
            for (int c = 0; c < part.Copies; c++)
            {
                string name = part.Copies > 1 ? $"{part.Name} ({c + 1} of {part.Copies})" : part.Name;
                var shapes = new List<(Shape, int)> { (new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(SpecKey, json), 0) };
                if (etched is not null) shapes.Add((new PathShape { Name = $"{name} · texture", LayerId = etchLayer!.Id, Geometry = etched }, 1));
                if (lines is not null) shapes.Add((new PathShape { Name = $"{name} · lines", LayerId = lineLayer!.Id, Geometry = lines }, 2));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add([.. shapes.Select(x => (x.Item1.WithGroup(g).WithAddonData(IdKey, id.ToString()), x.Item2))]);
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");

        // Biggest first, so the panels get the room and the small pieces fill round them.
        units = [.. units.OrderByDescending(u => { var b = u[0].Shape.Bounds; return b.Width * b.Height; })];
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var onPage = units.Select(u => u.Select(x => x.Shape.With(layerId: x.Kind == 0 ? cutLayer.Id : x.Kind == 1 ? etchLayer!.Id : lineLayer!.Id)).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A piece doesn't fit on the bed with the spacing round it. Make it smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            (cutLayer, etchLayer, lineLayer) = Layers(ref s);
        }
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
        if (o.EtchSettings is { } etchSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == EtchLayer, l => etchSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Fill, ParentId = l.ParentId });
        return (Pages.SwitchTo(s, firstPage), sheets, id);
    }
}
