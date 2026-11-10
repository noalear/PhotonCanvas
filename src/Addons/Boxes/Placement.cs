using Clipper2Lib;
using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// Puts a box in the design: each piece's outline (with its living hinge slits) on a Cut layer,
/// nested onto the bed, more pages when one bed isn't enough; and the lid's carved border, if asked
/// for, as a depth map on a Picture layer laid exactly over the lid, done before anything is cut.
/// Every shape carries the box's settings, so it can be opened again and changed.
/// </summary>
public static class Placement
{
    public const string CutLayer = "Box · cut";
    /// <summary>Which box a shape belongs to (an id shared by all its pieces).</summary>
    public const string IdKey = "boxes.id";
    /// <summary>The settings it was made from (JSON), on each piece's outline.</summary>
    public const string SpecKey = "boxes.spec";
    /// <summary>On the lid's outline: where the border goes, in the outline's own coordinates (x y w h).</summary>
    const string LidKey = "boxes.lid";

    public sealed record Options
    {
        /// <summary>Sets the cut layer up from the library (its kerf included); null leaves it as it is.</summary>
        public Func<Layer, Layer>? CutSettings { get; init; }
        /// <summary>A box made earlier to replace (its pieces are taken off every page first).</summary>
        public Guid? Replace { get; init; }
        public double GapMm { get; init; } = BoxMaker.BedGap;
    }

    /// <summary>The box a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, BoxSpec Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(IdKey, out var bid) || !Guid.TryParse(bid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(IdKey, out var v) && v == bid && x.AddonData.ContainsKey(SpecKey));
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

    /// <summary>A piece's outline and slits as the program's path, flipped so y runs down.</summary>
    public static PathGeometry Outline(Part p) => Geo.ToGeometry(p.Cut, p.Slits);

    public static (DocState State, int Sheets, Guid Id, string? BorderNote) Place(DocState s, BoxKit kit, BoxSpec spec, MachineProfile machine, Options o, CancellationToken ct = default)
    {
        var id = o.Replace ?? Guid.NewGuid();
        string json = spec.ToJson();
        const string title = "Box";
        if (o.Replace is { } old) s = Remove(s, old);
        else if (s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;

        Layer Cut(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == CutLayer);
            if (cut is not null) return cut;
            cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
            st = Ops.AddLayer(st, cut);
            return cut;
        }
        var cutLayer = Cut(ref s);

        foreach (var p in kit.Parts)
        {
            var b = Geo.Bounds(p.Cut);
            if (!BoxMaker.Fits(b.right - b.left, b.bottom - b.top, machine.FieldW, machine.FieldH))
                throw new InvalidOperationException($"The {p.Name.ToLowerInvariant()} is {b.right - b.left:0} × {b.bottom - b.top:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed.");
        }
        // Each piece (each copy of it) made at the bed's corner; the nesting moves it into place.
        var units = new List<Shape>();
        foreach (var part in kit.Parts)
        {
            ct.ThrowIfCancellationRequested();
            var outline = Outline(part);
            var box = outline.Bounds();
            if (box.IsEmpty) continue;
            var move = Affine.Translate(o.GapMm - box.MinX, o.GapMm - box.MinY);
            outline = outline.Transform(move);
            for (int c = 0; c < part.Copies; c++)
            {
                string name = $"Box · {part.Name.ToLowerInvariant()}" + (part.Copies > 1 ? $" ({c + 1} of {part.Copies})" : "");
                Shape shape = new PathShape { Name = name, LayerId = cutLayer.Id, Geometry = outline }.WithAddonData(SpecKey, json).WithAddonData(IdKey, id.ToString());
                if (part.LidArea is { } a)
                {
                    // The lid area in the outline's own coordinates (flat y up → design y down, then moved).
                    var tl = move.Apply(new Vec2(a.left, -a.bottom));
                    shape = shape.WithAddonData(LidKey, FormattableString.Invariant($"{tl.X} {tl.Y} {a.right - a.left} {a.bottom - a.top}"));
                }
                units.Add(shape);
            }
        }
        if (units.Count == 0) throw new InvalidOperationException("Nothing to cut.");

        // Biggest first, so the panels get the room and the small pieces fill round them.
        units = [.. units.OrderByDescending(u => u.Bounds.Width * u.Bounds.Height)];
        var options = new NestOptions { SpacingMm = o.GapMm, MarginMm = o.GapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var onPage = units.Select(u => u.With(layerId: cutLayer.Id)).ToList();
            s = Ops.AddShapes(s, onPage);
            var ids = onPage.Select(sh => sh.Id).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && s.Shapes.Count == ids.Count)
                throw new InvalidOperationException("A piece doesn't fit on the bed with the spacing round it. Make the box smaller.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => unplaced.Contains(onPage[i].Id))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            cutLayer = Cut(ref s);
        }
        if (o.CutSettings is { } cutSettings)
            s = Pages.UpdateLayers(s, firstPage, sheets, l => l.Name == CutLayer, l => cutSettings(l) with { Id = l.Id, Name = l.Name, Mode = LayerMode.Cut, ParentId = l.ParentId });

        string? note = null;
        if (spec.Border && spec.HasLid)
            (s, note) = AddBorder(s, spec, id, firstPage, sheets, machine, ct);
        return (Pages.SwitchTo(s, firstPage), sheets, id, note);
    }

    /// <summary>The carved border over the lid, on whichever page the lid went to.</summary>
    static (DocState, string?) AddBorder(DocState s, BoxSpec spec, Guid id, int firstPage, int sheets, MachineProfile machine, CancellationToken ct)
    {
        for (int page = firstPage; page < firstPage + sheets; page++)
        {
            var st = Pages.SwitchTo(s, page);
            var lid = st.Shapes.OfType<PathShape>().FirstOrDefault(x => Of(x, id.ToString()) && x.AddonData.ContainsKey(LidKey));
            if (lid is null) continue;
            var parts = lid.AddonData[LidKey].Split(' ').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            // Where the area ended up: the nesting moved (and maybe turned) the outline.
            var corners = new[] { new Vec2(parts[0], parts[1]), new Vec2(parts[0] + parts[2], parts[1]), new Vec2(parts[0], parts[1] + parts[3]), new Vec2(parts[0] + parts[2], parts[1] + parts[3]) }.Select(lid.Transform.Apply).ToList();
            double x0 = corners.Min(c => c.X), y0 = corners.Min(c => c.Y), x1 = corners.Max(c => c.X), y1 = corners.Max(c => c.Y);
            double inset = Math.Max(0, spec.BorderInsetMm);
            var (bs, problem) = BorderFor(spec, x1 - x0 - 2 * inset, y1 - y0 - 2 * inset);
            if (problem is not null) return (s, $"No border on the lid: {problem}");
            var plan = new BorderPlan(bs);
            var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p, ct))).ToList();
            // Its own picture layer, before the cut layer so it's carved while the sheet is whole.
            var layer = Layer.Default(st.Layers.Count, LayerMode.Image) with
            {
                Name = $"Box lid · {bs.Design.Name}", ImageMode = ImageEngraveMode.Slices,
                Dpi = Math.Clamp(Math.Round(Math.Clamp(bs.PixelsPerMm, 4, 20) * 25.4), 100, 508), LinesPerCm = 200, AngleStepDeg = 37,
            };
            int cutAt = st.Layers.FindIndex(l => l.Name == CutLayer);
            st = Ops.AddLayer(st, layer, cutAt < 0 ? st.Layers.Count : cutAt);
            var (withBorder, ids, _) = BorderOutput.Add(st, plan, maps, machine, new Vec2(x0 + inset, y0 + inset), layer);
            var mine = withBorder.Shapes.Select(x => ids.Contains(x.Id) ? x.WithAddonData(IdKey, id.ToString()) : x).ToList();
            withBorder = withBorder with { Shapes = [.. mine] };
            return (Pages.SwitchTo(withBorder, s.PageIndex), $"The border is on \"{layer.Name}\" over the lid, set to carve as depth slices before the cut: try it on scrap first.");
        }
        return (s, null);
    }

    /// <summary>The border maker's settings for a border round an area <paramref name="w"/> × <paramref name="h"/>, or why there can't be one.</summary>
    public static (BorderSpec Spec, string? Problem) BorderFor(BoxSpec spec, double w, double h)
    {
        var design = BorderPresets.All.FirstOrDefault(d => d.Name == spec.BorderDesign) ?? BorderPresets.Default;
        bool round = spec.Style == BoxStyle.Rounded;
        var bs = new BorderSpec
        {
            Design = design, Layout = BorderLayout.Frame, FrameWidthMm = w, FrameHeightMm = h, SizeIsOpening = false,
            Corners = round ? CornerStyle.Rounded : CornerStyle.Mitred,
            CornerRadiusMm = round ? Math.Max(1, spec.CornerRadiusMm + spec.ThicknessMm - spec.BorderInsetMm) : 30,
            WidthMm = spec.BorderWidthMm, DepthMm = spec.BorderDepthMm, Middle = MiddleFill.Untouched,
        };
        return (bs, w < 10 || h < 10 ? "the lid is too small for it." : bs.Problem());
    }
}
