using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Walls;

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
/// Puts a set of walls in the design: each piece's outline on a Cut layer, the texture on an etch (Fill)
/// layer and its lines on a light Score layer (both done before anything is cut, while the sheet is
/// whole), grouped piece by piece and nested onto the bed; pieces that don't fit go on further pages.
/// Every shape carries the settings, so the set can be opened again and changed.
/// </summary>
public static class Placement
{
    public const string CutLayer = "Walls · cut";
    public const string EtchLayer = "Walls · etch";
    public const string LineLayer = "Walls · lines";
    /// <summary>The texture as a greyscale depth map, carved in slices (<see cref="WallSpec.Relief"/>).</summary>
    public const string ReliefLayer = "Walls · relief";
    /// <summary>A single panel's back (<see cref="WallSpec.EngraveBack"/>): engraved after the cut pieces are turned over.</summary>
    public const string BackEtchLayer = "Walls · back etch";
    public const string BackLineLayer = "Walls · back lines";
    public const string BackReliefLayer = "Walls · back relief";
    /// <summary>Each turned-over piece's outline, not burned: what Fit to piece lines the back's engraving up by.</summary>
    public const string BackGuideLayer = "Walls · back outline (guide)";
    /// <summary>The layers a set's backs go on.</summary>
    public static readonly BackLayers WallBacks = new(BackEtchLayer, BackLineLayer, BackReliefLayer, BackGuideLayer);
    /// <summary>The name ending the page each sheet's backs are on.</summary>
    public const string BackPage = "backs (turn the pieces over)";
    /// <summary>Which set a shape belongs to (an id shared by all its pieces).</summary>
    public const string IdKey = "walls.id";
    /// <summary>The settings it was made from (JSON), on each piece's outline.</summary>
    public const string SpecKey = "walls.spec";

    /// <summary>The set a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, WallSpec Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var wid) || !Guid.TryParse(wid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == wid && x.AddonData.ContainsKey(SpecKey));
            if (withSpec is not null && WallSpec.FromJson(withSpec.AddonData[SpecKey]) is { } spec) return (g, spec);
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

    /// <summary>How the relief is carved for a set: its texture's groove, wear, detail and material.</summary>
    static Terrain.Relief.Look LookOf(WallSpec s)
    {
        var t = TextureEngine.Find(s.Texture);
        return new(s.GrooveMm, s.Wear, s.DetailMm > 0 ? s.DetailMm : t?.Detail ?? 0.5, t?.Stuff ?? Stuff.Stone, s.Seed);
    }

    static bool Fits(Rect2 box, MachineProfile machine, double gap) =>
        Math.Max(box.Width, box.Height) + 2 * gap <= Math.Max(machine.FieldW, machine.FieldH) + 1e-6
        && Math.Min(box.Width, box.Height) + 2 * gap <= Math.Min(machine.FieldW, machine.FieldH) + 1e-6;

    /// <summary>The pieces too big for the bed, as findings to show before anything is put in.</summary>
    public static IEnumerable<Finding> TooBig(WallKit kit, MachineProfile machine, double gapMm = 2)
    {
        foreach (var part in kit.Parts)
        {
            var b = Geo.Bounds(part.Cut);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (w <= 0 || Fits(new Rect2(0, 0, w, h), machine, gapMm)) continue;
            yield return new Finding(Severity.Bad, $"The {part.Name.ToLowerInvariant()} is {w:0} × {h:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. Make the wall shorter or lower.");
        }
    }

    public static (DocState State, int Sheets, Guid Id) Place(DocState s, WallKit kit, WallSpec spec, MachineProfile machine, PlaceOptions o, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        const string title = "Walls";
        if (o.Replace is { } old) s = Remove(s, old);
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;
        bool relief = spec.Relief;
        bool anyEtch = !relief && kit.Parts.Any(p => p.Etched.Count > 0), anyLines = !relief && kit.Parts.Any(p => p.Lines.Count > 0);
        bool anyRelief = relief && kit.Parts.Any(p => p.Etched.Count > 0 || p.Lines.Count > 0);

        (Layer Cut, Layer? Etch, Layer? Lines, Layer? Relief) Layers(ref DocState st)
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
            var carve = Before(ref st, anyRelief, ReliefLayer, LayerMode.Image, Terrain.Relief.Setup);
            return (cut, etch, lines, carve);
        }
        var (cutLayer, etchLayer, lineLayer, reliefLayer) = Layers(ref s);
        var look = LookOf(spec);

        if (TooBig(kit, machine, o.GapMm).FirstOrDefault() is { } tooBig) throw new InvalidOperationException(tooBig.Text);
        // Each piece (each copy of it) made at the bed's corner; the nesting moves it into place. Kind of each shape: 0 cut, 1 etch, 2 lines, 3 relief.
        var units = new List<List<(Shape Shape, int Kind)>>();
        // A piece's back, by its outline's id: put on a page of its own once the front is nested (kind 4 etch, 5 lines, 6 relief, 7 guide outline).
        var backs = new Dictionary<Guid, List<(Shape Shape, int Kind)>>();
        foreach (var part in kit.Parts)
        {
            ct.ThrowIfCancellationRequested();
            var outline = Geo.ToGeometry(part.Cut);
            var box = outline.Bounds();
            if (box.IsEmpty) continue;
            var move = Affine.Translate(o.GapMm - box.MinX, o.GapMm - box.MinY);
            outline = outline.Transform(move);
            var etched = !relief && part.Etched.Count > 0 ? Geo.ToGeometry(part.Etched).Transform(move) : null;
            var lines = !relief && part.Lines.Count > 0 ? Geo.ToGeometry([], part.Lines).Transform(move) : null;
            var map = relief ? Terrain.Relief.Render(part.Cut, part.Etched, part.Lines, look with { Seed = look.Seed + units.Count }) : null;
            var source = map is null ? null : ImageSource.FromEncoded(map.Png);
            var back = part.HasBack ? BackShapes(part.Cut, part.BackEtched, part.BackLines, outline, move, relief ? look with { Seed = look.Seed + 500 + units.Count } : null) : null;
            for (int c = 0; c < part.Copies; c++)
            {
                string name = part.Copies > 1 ? $"{part.Name} ({c + 1} of {part.Copies})" : part.Name;
                var shapes = new List<(Shape, int)> { (new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(SpecKey, json), 0) };
                if (etched is not null) shapes.Add((new PathShape { Name = $"{name} · texture", LayerId = etchLayer!.Id, Geometry = etched }, 1));
                if (lines is not null) shapes.Add((new PathShape { Name = $"{name} · lines", LayerId = lineLayer!.Id, Geometry = lines }, 2));
                if (map is not null)
                    shapes.Add((new ImageShape
                    {
                        Name = $"{name} · relief", LayerId = reliefLayer!.Id, Source = source!, SizeMm = new Vec2(map.WidthMm, map.HeightMm),
                        Transform = Affine.Translate(map.Left, -map.Top).Then(move),
                    }, 3));
                var g = shapes.Count > 1 ? Guid.NewGuid() : (Guid?)null;
                units.Add([.. shapes.Select(x => (x.Item1.WithGroup(g).WithAddonData(IdKey, id.ToString()), x.Item2))]);
                if (back is not null)
                {
                    var bg = Guid.NewGuid();
                    backs[units[^1][0].Shape.Id] = [.. back.Select(b => (b.Shape.With(name: $"{name} · back{b.Suffix}", id: Guid.NewGuid()).WithGroup(bg).WithAddonData(IdKey, id.ToString()), b.Kind))];
                }
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");
        var backSheets = new List<(int Sheet, Affine Nested, List<(Shape Shape, int Kind)> Shapes)>();

        // Biggest first, so the panels get the room and the small pieces fill round them.
        units = [.. units.OrderByDescending(u => { var b = u[0].Shape.Bounds; return b.Width * b.Height; })];
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var onPage = units.Select(u => u.Select(x => x.Shape.With(layerId: x.Kind switch { 0 => cutLayer.Id, 1 => etchLayer!.Id, 2 => lineLayer!.Id, _ => reliefLayer!.Id })).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(sh => sh.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A piece doesn't fit on the bed with the spacing round it. Make it smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            foreach (var u in onPage)
                if (!unplaced.Contains(u[0].Id) && backs.TryGetValue(u[0].Id, out var b) && s.Shapes.FirstOrDefault(x => x.Id == u[0].Id) is { } placed)
                    backSheets.Add((sheets, placed.Transform, b));
            units = [.. units.Where((u, i) => onPage[i].Any(sh => unplaced.Contains(sh.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            (cutLayer, etchLayer, lineLayer, reliefLayer) = Layers(ref s);
        }
        int backPages = 0;
        if (backSheets.Count > 0) (s, backPages) = AddBackPages(s, backSheets, firstPage, sheets, machine, title, WallBacks);
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });
        if (o.EtchSettings is { } etchSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets + backPages, l => l.Name is EtchLayer or BackEtchLayer, l => etchSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Fill, ParentId = l.ParentId });
        return (Pages.SwitchTo(s, firstPage), sheets, id);
    }

    /// <summary>
    /// A piece's back, made like its front at the bed's corner (<paramref name="move"/>): its outline as a guide, and
    /// what's engraved, both as they lie seen through the piece from the front. Mirrored across the bed afterwards, it's
    /// where the piece's back is once it's turned over left to right.
    /// </summary>
    /// <remarks>Kinds: 4 etch, 5 lines, 6 relief (with <paramref name="relief"/>), 7 the guide outline; each shape's name is left for the caller, its suffix given.</remarks>
    public static List<(Shape Shape, int Kind, string Suffix)> BackShapes(PathsD cut, PathsD? backEtched, List<PathD>? backLines, PathGeometry outline, Affine move) =>
        BackShapes(cut, backEtched, backLines, outline, move, null);

    internal static List<(Shape Shape, int Kind, string Suffix)> BackShapes(PathsD cut, PathsD? backEtched, List<PathD>? backLines, PathGeometry outline, Affine move, Terrain.Relief.Look? relief)
    {
        var list = new List<(Shape, int, string)> { (new PathShape { Name = "", Geometry = outline }, 7, " outline") };
        var etched = backEtched ?? [];
        var lines = backLines ?? [];
        if (relief is not { } look)
        {
            if (etched.Count > 0) list.Add((new PathShape { Name = "", Geometry = Geo.ToGeometry(etched).Transform(move) }, 4, " texture"));
            if (lines.Count > 0) list.Add((new PathShape { Name = "", Geometry = Geo.ToGeometry([], lines).Transform(move) }, 5, " lines"));
            return list;
        }
        // A relief is a picture: drawn as seen from behind (so it's never shown mirrored), then turned to lie through the front.
        var b = Geo.Bounds(cut);
        double c = b.left + b.right;
        PointD M(PointD p) => new(c - p.x, p.y);
        var map = Terrain.Relief.Render(Geo.Tidy(Geo.Map(cut, M)), Geo.Tidy(Geo.Map(etched, M)), Geo.Map(lines, M), look);
        if (map is null) return list;
        var box = outline.Bounds();
        var through = Affine.ScaleAbout(new Vec2(box.Center.X, 0), -1, 1);
        list.Add((new ImageShape
        {
            Name = "", Source = ImageSource.FromEncoded(map.Png), SizeMm = new Vec2(map.WidthMm, map.HeightMm),
            Transform = Affine.Translate(map.Left, -map.Top).Then(move).Then(through),
        }, 6, " relief"));
        return list;
    }

    /// <summary>
    /// The backs of each sheet's pieces on a page after all the sheets: mirrored across the bed's middle, so they lie
    /// where the pieces are once the sheet is turned over left to right. Only engraved; each piece's outline is a guide
    /// for Fit to piece. <paramref name="first"/> and <paramref name="sheets"/> are the sheets' pages; the backs go after them.
    /// Returns how many pages were added, with the last of them shown.
    /// </summary>
    public static (DocState, int) AddBackPages(DocState s, List<(int Sheet, Affine Nested, List<(Shape Shape, int Kind)> Shapes)> backs, int first, int sheets, MachineProfile machine, string title, BackLayers names)
    {
        var flip = Affine.ScaleAbout(new Vec2(machine.FieldW / 2, 0), -1, 1);
        int added = 0;
        foreach (var sheet in backs.GroupBy(b => b.Sheet).OrderBy(g => g.Key))
        {
            // After the last page of this set (the front sheets, then the backs added so far).
            s = Pages.SwitchTo(s, first + sheets - 1 + added);
            s = Pages.Add(s, sheets == 1 ? $"{title} · {BackPage}" : $"{title} {sheet.Key} · {BackPage}");
            added++;
            // Only the back's own layers on this page.
            var empty = s.Layers.Where(l => !s.Shapes.Any(x => x.LayerId == l.Id)).Select(l => l.Id).ToHashSet();
            var kinds = sheet.SelectMany(b => b.Shapes.Select(x => x.Kind)).ToHashSet();
            var layers = new Dictionary<int, Guid>();
            void Want(int kind, string name, LayerMode mode, Func<Layer, Layer> setup)
            {
                if (!kinds.Contains(kind)) return;
                var l = setup(Layer.Default(s.Layers.Count, mode) with { Name = name });
                s = Ops.AddLayer(s, l);
                layers[kind] = l.Id;
            }
            Want(4, names.Etch, LayerMode.Fill, l => l);
            Want(5, names.Lines, LayerMode.Score, l => l with { Laser = l.Laser with { Power = Math.Min(l.Laser.Power, 15) } });
            Want(6, names.Relief, LayerMode.Image, Terrain.Relief.Setup);
            Want(7, names.Guide, LayerMode.Score, l => l with { Output = false, Color = 0x9CA3AF });
            if (empty.Count > 0) s = Ops.DeleteLayers(s, empty, null);
            var shapes = new List<Shape>();
            foreach (var (_, nested, parts) in sheet)
                foreach (var (shape, kind) in parts)
                {
                    var world = shape.Transform.Then(nested).Then(flip);
                    // Outlines and etching kept as plain geometry (no mirrored transform left on them).
                    shapes.Add(shape is PathShape p
                        ? new PathShape { Name = p.Name, Id = p.Id, LayerId = layers[kind], Geometry = p.Geometry.Transform(world), GroupId = p.GroupId, AddonData = p.AddonData }
                        : shape.With(layerId: layers[kind], transform: world));
                }
            s = Ops.AddShapes(s, shapes);
        }
        return (s, added);
    }
}

/// <summary>The names of the layers turned-over pieces' backs go on: what's etched, scored and carved, and the outline guide.</summary>
public sealed record BackLayers(string Etch, string Lines, string Relief, string Guide);
