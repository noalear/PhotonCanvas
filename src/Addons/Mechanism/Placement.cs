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
/// Where parts go and how they're tagged: the layers' names and the keys on each shape for which set of parts it
/// is and the settings it was made from. Other add-ons that make parts the same way (the toys and automata maker)
/// use their own, so their parts and the mechanism maker's never mix.
/// </summary>
public sealed record PlaceTarget(string CutLayer, string MarkLayer, string IdKey, string SpecKey)
{
    public static readonly PlaceTarget Mechanism = new(Placement.CutLayer, Placement.MarkLayer, Placement.IdKey, Placement.SpecKey);
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

    static bool Of(Shape x, string mechanism, string key = IdKey) => x.AddonData.TryGetValue(key, out var v) && v == mechanism;

    /// <summary>The shapes of a mechanism on the page shown.</summary>
    public static HashSet<Guid> ShapesOf(DocState s, Guid mechanism, PlaceTarget? to = null)
    {
        string id = mechanism.ToString(), key = (to ?? PlaceTarget.Mechanism).IdKey;
        return [.. s.Shapes.Where(x => Of(x, id, key)).Select(x => x.Id)];
    }

    /// <summary>Is any part of the mechanism on any page.</summary>
    public static bool Exists(DocState s, Guid mechanism, PlaceTarget? to = null)
    {
        string id = mechanism.ToString(), key = (to ?? PlaceTarget.Mechanism).IdKey;
        return s.Shapes.Any(x => Of(x, id, key)) || s.OtherPages.Any(p => p.Shapes.Any(x => Of(x, id, key)));
    }

    /// <summary>
    /// Takes a mechanism's parts off every page. Other pages that leaves empty go too (the ones its parts
    /// spilled onto); the page shown stays, and stays shown.
    /// </summary>
    public static DocState Remove(DocState s, Guid mechanism, PlaceTarget? to = null)
    {
        int shown = s.PageIndex;
        // From the last page back, so the pages before each one keep their places.
        for (int i = s.OtherPages.Count; i >= 0; i--)
        {
            var page = Pages.SwitchTo(s, i);
            var mine = ShapesOf(page, mechanism, to);
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

    public static (DocState State, int Sheets, Guid Id) Place(DocState s, Mechanism m, MechSpec spec, MachineProfile machine, PlaceOptions o, CancellationToken ct = default) =>
        Place(s, m, spec.ToJson(), machine, o, PlaceTarget.Mechanism, ct);

    /// <summary>Puts the parts in as the other <c>Place</c> does, on the layers and with the keys of <paramref name="to"/>, carrying <paramref name="json"/> as their settings.</summary>
    public static (DocState State, int Sheets, Guid Id) Place(DocState s, Mechanism m, string json, MachineProfile machine, PlaceOptions o, PlaceTarget to, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string title = m.Title;
        // Made again: the old parts go (from every page they were on) and the new ones go on the page shown.
        if (o.Replace is { } old) s = Remove(s, old, to);
        // A new one: a page of its own when this one already has something on it.
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;

        // Parts cut from other stock (a comb's thick clamp bars) get a cut layer named after it, on pages of their own.
        string CutName(string? stock) => stock is null ? to.CutLayer : $"{to.CutLayer} ({stock})";
        (Layer Cut, Layer? Marks) Layers(ref DocState st, string? stock = null)
        {
            string cutName = CutName(stock);
            var cut = st.Layers.FirstOrDefault(l => l.Name == cutName);
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = cutName };
                st = Ops.AddLayer(st, cut);
            }
            Layer? mk = null;
            if (o.Marks)
            {
                mk = st.Layers.FirstOrDefault(l => l.Name == to.MarkLayer);
                if (mk is null)
                {
                    var score = Layer.Default(st.Layers.Count, LayerMode.Score);
                    mk = score with { Name = to.MarkLayer, Laser = score.Laser with { Power = Math.Min(score.Laser.Power, 15) } };
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
        var units = new List<(string? Stock, List<Shape> Shapes)>();
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
            var labels = new List<TextShape>();
            if (markLayer is not null)
            {
                var inside = Poly.FromGeometry(outline);
                foreach (var lab in (part.Label is { } first ? [first] : Array.Empty<PartLabel>()).Concat(part.Labels))
                {
                    var text = TextShape.Create(lab.Text, new TextStyle("Arial", lab.HeightMm, SingleLine: true), markLayer.Id, Affine.Identity);
                    var tb = text.Bounds;
                    var at = move.Apply(Flip(lab.At));
                    var placed = text.With(transform: Affine.Translate(-tb.Center.X, -tb.Center.Y).Then(Affine.Rotate(-lab.AngleDeg)).Then(Affine.Translate(at.X, at.Y)));
                    // Only where it's all on the part (never over a hole or off the edge).
                    var pb = placed.Bounds;
                    if (!pb.IsEmpty && new[] { new Vec2(pb.MinX, pb.MinY), new Vec2(pb.MaxX, pb.MinY), new Vec2(pb.MaxX, pb.MaxY), new Vec2(pb.MinX, pb.MaxY), pb.Center }.All(p => Poly.Inside(inside, p))
                        && Poly.Area(Poly.Subtract([Poly.Rect(pb.MinX, pb.MinY, pb.MaxX, pb.MaxY)], inside)) < 0.01)
                        labels.Add((TextShape)placed);
                }
            }
            for (int c = 0; c < part.Copies; c++)
            {
                string name = part.Copies > 1 ? $"{part.Name} ({c + 1} of {part.Copies})" : part.Name;
                var shapes = new List<Shape> { new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(to.SpecKey, json) };
                if (marks is not null) shapes.Add(new PathShape { Name = $"{name} · marks", LayerId = markLayer!.Id, Geometry = marks });
                foreach (var label in labels) shapes.Add(label.With(id: Guid.NewGuid(), name: labels.Count == 1 ? $"{name} · name" : $"{name} · {label.Text}"));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add((part.Stock, [.. shapes.Select(x => x.WithGroup(g).WithAddonData(to.IdKey, id.ToString()))]));
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");

        // The main sheet's parts first, then each other stock's on pages of their own; bed by bed: everything
        // left goes on the page, the nesting places what fits, the rest moves on.
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        var stocks = units.Select(u => u.Stock).Distinct().OrderBy(x => x is null ? 0 : 1).ToList();
        for (int k = 0; k < stocks.Count; k++)
        {
            string? stock = stocks[k];
            var left = units.Where(u => u.Stock == stock).Select(u => u.Shapes).ToList();
            if (k > 0)
            {
                s = Pages.Add(s, $"{title} ({stock})");
                sheets++;
            }
            if (k > 0 || stock is not null) (cutLayer, markLayer) = Layers(ref s, stock);
            while (left.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                // Each page has its own layers: the outline is first in its part, the rest are marks.
                var onPage = left.Select(u => u.Select((sh, i) => sh.With(layerId: i == 0 ? cutLayer.Id : markLayer!.Id)).ToList()).ToList();
                s = Ops.AddShapes(s, onPage.SelectMany(u => u));
                var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
                var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
                if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                    throw new InvalidOperationException("A part doesn't fit on the bed with the spacing round it. Make it smaller.");
                var unplaced = report.Unplaced.ToHashSet();
                s = Ops.Delete(nested, unplaced);
                left = [.. left.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
                if (left.Count == 0) break;
                s = Pages.Add(s, stock is null ? $"{title} {++sheets}" : $"{title} ({stock}) {++sheets}");
                (cutLayer, markLayer) = Layers(ref s, stock);
            }
        }
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == to.CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
        return (Pages.SwitchTo(s, firstPage), sheets, id);
    }
}
