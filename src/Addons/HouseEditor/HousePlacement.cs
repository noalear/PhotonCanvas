using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// Puts a building drawn in the editor in the design, the way the whole-building kits are put in, one sheet after the other (the walls' and the furniture's, when
/// they're different): each piece's outline on a Cut layer, the texture on an etch (Fill) layer and its
/// lines on a light Score layer (both done before anything is cut, while the sheet is whole), grouped
/// piece by piece and nested onto the bed, a page per bed. Every shape carries the settings, so the
/// building can be opened again and changed; each wall also carries the wall maker's settings, so the
/// furniture maker can match a door or shelf to it.
/// </summary>
public static class HousePlacement
{
    public const string CutLayer = "Building · cut";
    public const string EtchLayer = "Building · etch";
    public const string LineLayer = "Building · lines";
    /// <summary>Which building a shape belongs to (an id shared by all its pieces).</summary>
    public const string IdKey = "houseeditor.id";
    /// <summary>The settings it was made from (JSON), on each piece's outline.</summary>
    public const string SpecKey = "houseeditor.spec";
    /// <summary>The wall maker's own key for its settings (read by the furniture maker).</summary>
    public const string WallSpecKey = Placement.WallSpecKey;

    /// <summary>The building a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, HouseDesign Design)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var bid) || !Guid.TryParse(bid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == bid && x.AddonData.ContainsKey(SpecKey));
            if (withSpec is not null && HouseDesign.FromJson(withSpec.AddonData[SpecKey]) is { } spec) return (g, spec);
        }
        return null;
    }

    static bool Of(Shape x, string set) => x.AddonData.TryGetValue(IdKey, out var v) && v == set;

    public static HashSet<Guid> ShapesOf(DocState s, Guid set)
    {
        string id = set.ToString();
        return [.. s.Shapes.Where(x => Of(x, id)).Select(x => x.Id)];
    }

    /// <summary>How many of the building's shapes there are on every page.</summary>
    public static int CountEverywhere(DocState s, Guid set)
    {
        string id = set.ToString();
        return s.Shapes.Count(x => Of(x, id)) + s.OtherPages.Sum(p => p.Shapes.Count(x => Of(x, id)));
    }

    /// <summary>Takes a building's pieces off every page; other pages that leaves empty go too. The page shown stays.</summary>
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

    static bool Fits(double w, double h, MachineProfile machine, double gap) =>
        Math.Max(w, h) + 2 * gap <= Math.Max(machine.FieldW, machine.FieldH) + 1e-6
        && Math.Min(w, h) + 2 * gap <= Math.Min(machine.FieldW, machine.FieldH) + 1e-6;

    /// <summary>The pieces too big for the bed, as findings to show before anything is put in.</summary>
    public static IEnumerable<Finding> TooBig(HouseKit kit, MachineProfile machine, double gapMm = 2)
    {
        foreach (var part in kit.Pieces)
        {
            var b = Geo.Bounds(part.Cut);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (w <= 0 || Fits(w, h, machine, gapMm)) continue;
            yield return new Finding(Severity.Bad, $"The {Lower(part.Name)} is {w:0} × {h:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. Make the building smaller.");
        }
    }

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    /// <summary>The sheets the building is cut from, in order: the walls', then the furniture's if it's a different thickness.</summary>
    public static List<(string Title, double Thickness, List<Piece> Pieces, Func<Layer, Layer>? Cut)> Sheets(HouseKit kit, HouseDesign design, PlaceOptions o)
    {
        var spec = design.Style;
        const string name = "Building";
        var walls = kit.Pieces.Where(p => p.Sheet == Sheet.Walls).ToList();
        var furniture = kit.Pieces.Where(p => p.Sheet == Sheet.Furniture).ToList();
        if (Math.Abs(spec.WallSheetMm - spec.FurnitureSheetMm) < 1e-6 || furniture.Count == 0)
            return [($"{name} ({spec.WallSheetMm:0.##} mm sheet)", spec.WallSheetMm, [.. walls, .. furniture], o.WallCut)];
        var list = new List<(string, double, List<Piece>, Func<Layer, Layer>?)>();
        if (walls.Count > 0) list.Add(($"{name} walls ({spec.WallSheetMm:0.##} mm sheet)", spec.WallSheetMm, walls, o.WallCut));
        list.Add(($"{name} furniture ({spec.FurnitureSheetMm:0.##} mm sheet)", spec.FurnitureSheetMm, furniture, o.FurnitureCut));
        return list;
    }

    public static (DocState State, int Pages, Guid Id) Place(DocState s, HouseKit kit, HouseDesign spec, MachineProfile machine, PlaceOptions o, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        if (o.Replace is { } old) s = Remove(s, old);
        if (TooBig(kit, machine, o.GapMm).FirstOrDefault() is { } tooBig) throw new InvalidOperationException(tooBig.Text);
        int firstPage = -1, pages = 0;
        bool firstSheet = true;
        foreach (var (title, _, pieces, cutSettings) in Sheets(kit, spec, o))
        {
            // The first sheet goes on the page shown if it's empty (or the building's being replaced on it); each sheet after on pages of its own.
            if (!(firstSheet && (s.Shapes.Count == 0 || o.Replace is not null))) s = Pages.Add(s, title);
            else if (s.Shapes.Count == 0) s = Pages.Rename(s, title);
            firstSheet = false;
            int from = s.PageIndex;
            if (firstPage < 0) firstPage = from;
            int sheets = PlaceSheet(ref s, pieces, title, id, json, machine, o, ct);
            pages += sheets;
            if (cutSettings is { } cut)
                s = Pages.UpdateLayers(s, from, sheets, l => l.Name == CutLayer, l => cut(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
            if (o.EtchSettings is { } etch)
                s = Pages.UpdateLayers(s, from, sheets, l => l.Name == EtchLayer, l => etch(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Fill, ParentId = l.ParentId });
        }
        if (firstPage < 0) throw new InvalidOperationException("Nothing to cut.");
        return (Pages.SwitchTo(s, firstPage), pages, id);
    }

    /// <summary>One sheet's pieces nested onto as many pages (beds) as they need, from the page shown on; returns how many pages.</summary>
    static int PlaceSheet(ref DocState s, List<Piece> pieces, string title, Guid id, string json, MachineProfile machine, PlaceOptions o, CancellationToken ct)
    {
        bool anyEtch = pieces.Any(p => p.Etched.Count > 0), anyLines = pieces.Any(p => p.Lines.Count > 0);
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

        // Each piece (each copy of it) made at the bed's corner; the nesting moves it into place. Kind of each shape: 0 cut, 1 etch, 2 lines.
        var units = new List<List<(Shape Shape, int Kind)>>();
        foreach (var part in pieces)
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
                Shape cutShape = new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline };
                if (part.WallSpecJson is { } wall && c == 0) cutShape = cutShape.WithAddonData(WallSpecKey, wall);
                var shapes = new List<(Shape, int)> { (cutShape, 0) };
                if (etched is not null) shapes.Add((new PathShape { Name = $"{name} · etching", LayerId = etchLayer!.Id, Geometry = etched }, 1));
                if (lines is not null) shapes.Add((new PathShape { Name = $"{name} · lines", LayerId = lineLayer!.Id, Geometry = lines }, 2));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add([.. shapes.Select(x => (x.Item1.WithGroup(g).WithAddonData(IdKey, id.ToString()), x.Item2))]);
            }
        }
        if (units.Count == 0) return 0;

        // Biggest first, so the walls and floors get the room and the small pieces fill round them.
        units = [.. units.OrderByDescending(u => { var b = u[0].Shape.Bounds; return b.Width * b.Height; })];
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        int sheets = 1;
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            // The settings on the biggest piece of each page (it always goes on): enough to find the building
            // again from any of its pieces, where every outline carrying them would make the file big.
            if (!units[0][0].Shape.AddonData.ContainsKey(SpecKey)) units[0][0] = (units[0][0].Shape.WithAddonData(SpecKey, json), 0);
            var onPage = units.Select(u => u.Select(x => x.Shape.With(layerId: x.Kind == 0 ? cutLayer.Id : x.Kind == 1 ? etchLayer!.Id : lineLayer!.Id)).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A piece doesn't fit on the bed with the spacing round it. Make the building smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            (cutLayer, etchLayer, lineLayer) = Layers(ref s);
        }
        return sheets;
    }
}
