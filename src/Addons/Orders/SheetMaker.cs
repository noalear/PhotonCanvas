using System.Collections.Immutable;
using System.Globalization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Orders;

/// <summary>How sheets are laid out.</summary>
public sealed record SheetOptions
{
    /// <summary>Where pieces may go (the work area, or the box round the material outline), world mm.</summary>
    public Rect2 Area { get; init; } = new(0, 0, 400, 400);
    /// <summary>Space between pieces, mm.</summary>
    public double GapMm { get; init; } = 3;
    /// <summary>Space kept from the area's edge, mm.</summary>
    public double MarginMm { get; init; } = 5;
    /// <summary>Turn a piece a quarter turn when it only fits that way, or when more fit in its row.</summary>
    public bool Turn { get; init; } = true;
    /// <summary>A number and the order on each piece, on a layer that isn't burned.</summary>
    public bool Labels { get; init; } = true;
    /// <summary>The page names start with this.</summary>
    public string Title { get; init; } = "Orders";
}

/// <summary>One piece to burn: piece <paramref name="Number"/> of the item's quantity, made from <paramref name="Design"/>.</summary>
public sealed record Piece(OrderItem Item, int Number, Product Product, DocState Design);

/// <summary>What making the sheets did.</summary>
public sealed record SheetResult(DocState State, int Sheets, int Pieces, List<string> Notes, Dictionary<string, string> SheetOf);

/// <summary>
/// Turns pieces into sheets: each material on its own pages, pieces in rows in order-date order,
/// each a copy of its product's design with the order's fields written in, grouped, and numbered
/// on a layer that isn't burned so the packing list can say which piece is whose.
/// </summary>
public static class SheetMaker
{
    /// <summary>The note on every shape of a piece: "sheet|number|item key".</summary>
    public const string PieceKey = "orders.piece";
    public const string LabelLayerName = "Order labels";

    public static SheetResult Make(DocState current, IReadOnlyList<Piece> pieces, SheetOptions o, DateTime now)
    {
        var notes = new List<string>();
        var sheetOf = new Dictionary<string, string>();
        var pages = new List<DesignPage>();
        int placedCount = 0;
        var area = new Rect2(o.Area.MinX + o.MarginMm, o.Area.MinY + o.MarginMm, o.Area.MaxX - o.MarginMm, o.Area.MaxY - o.MarginMm);
        if (area.IsEmpty || area.Width < 1 || area.Height < 1) return new(current, 0, 0, ["The area is too small for the margin."], sheetOf);

        var groups = pieces.GroupBy(p => p.Product.SheetGroup).ToList();
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(p => p.Item.Date ?? DateTime.MaxValue).ThenBy(p => p.Item.Order, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Item.Key, StringComparer.Ordinal).ThenBy(p => p.Number).ToList();
            var sheets = Layout(ordered, area, o, notes);
            for (int si = 0; si < sheets.Count; si++)
            {
                string name = sheets.Count == 1 && groups.Count == 1 ? o.Title
                    : sheets.Count == 1 ? $"{o.Title}: {g.Key}"
                    : $"{o.Title}: {g.Key} {si + 1} of {sheets.Count}";
                var page = Page(name, sheets[si], o, current.Vars, now, notes);
                pages.Add(page);
                placedCount += sheets[si].Count;
                foreach (var spot in sheets[si]) sheetOf[spot.Piece.Item.Key] = name;
            }
        }
        if (pages.Count == 0) return new(current, 0, 0, notes, sheetOf);

        // An empty project takes the first sheet in place of its empty page; otherwise the sheets go after the page on screen.
        bool empty = current.Shapes.Count == 0 && current.OtherPages.Count == 0;
        DocState next;
        if (empty)
        {
            var p0 = pages[0];
            next = current with { Layers = p0.Layers, Shapes = p0.Shapes, Guides = [], PageName = p0.Name, PageIndex = 0, OtherPages = [.. pages.Skip(1)] };
        }
        else next = Pages.Insert(current, pages);
        return new(next, pages.Count, placedCount, notes, sheetOf);
    }

    /// <summary>A piece's place on its sheet: its top-left corner, and whether it's turned a quarter.</summary>
    public sealed record Spot(Piece Piece, int Number, Vec2 At, bool Turned, Rect2 Box);

    /// <summary>Rows, left to right and top to bottom; a new sheet when the next row doesn't fit.</summary>
    public static List<List<Spot>> Layout(IReadOnlyList<Piece> pieces, Rect2 area, SheetOptions o, List<string> notes)
    {
        var sheets = new List<List<Spot>>();
        var sheet = new List<Spot>();
        double x = area.MinX, y = area.MinY, rowH = 0;
        foreach (var p in pieces)
        {
            var box = ProductDesigns.Box(p.Design);
            if (box.IsEmpty) { notes.Add($"{p.Product.Name}: its design is empty."); continue; }
            double w = box.Width, h = box.Height;
            bool fitsFlat = w <= area.Width && h <= area.Height, fitsTurned = o.Turn && h <= area.Width && w <= area.Height;
            if (!fitsFlat && !fitsTurned) { notes.Add($"{Who(p.Item)}: {p.Product.Name} ({w:0.#} × {h:0.#} mm) is bigger than the sheet."); continue; }
            bool turned = !fitsFlat;
            // Turned also when that lets it into the row it would otherwise leave.
            if (fitsFlat && fitsTurned && x + w > area.MaxX + 1e-6 && x + h <= area.MaxX + 1e-6 && rowH > 0 && w <= rowH + 1e-6 && h > 0) turned = true;
            if (turned) (w, h) = (h, w);
            if (x + w > area.MaxX + 1e-6 && x > area.MinX) { x = area.MinX; y += rowH + o.GapMm; rowH = 0; }
            if (y + h > area.MaxY + 1e-6 && sheet.Count > 0)
            {
                sheets.Add(sheet);
                sheet = [];
                x = area.MinX; y = area.MinY; rowH = 0;
            }
            sheet.Add(new Spot(p, sheet.Count + 1, new Vec2(x, y), turned, box));
            x += w + o.GapMm;
            rowH = Math.Max(rowH, h);
        }
        if (sheet.Count > 0) sheets.Add(sheet);
        return sheets;
    }

    static string Who(OrderItem i) => $"{i.Order} {(i.Buyer.Length > 0 ? i.Buyer : i.ShipName)}".Trim();

    /// <summary>One sheet as a page: the designs' layers (shared where they're the same), the pieces, and their labels.</summary>
    static DesignPage Page(string name, List<Spot> spots, SheetOptions o, VariableData vars, DateTime now, List<string> notes)
    {
        var layers = new List<Layer>();
        var shapes = new List<Shape>();
        var maps = new Dictionary<DocState, Dictionary<Guid, Guid>>(ReferenceEqualityComparer.Instance);
        Layer? labels = null;
        foreach (var spot in spots)
        {
            var design = spot.Piece.Design;
            if (!maps.TryGetValue(design, out var map)) maps[design] = map = MapLayers(design, layers);
            var fields = OrderFields.For(spot.Piece.Item, spot.Piece.Number, spot.Piece.Item.Quantity);
            var box = spot.Box;
            var place = Affine.Translate(-box.MinX, -box.MinY);
            if (spot.Turned) place = place.Then(Affine.Rotate(90)).Then(Affine.Translate(box.Height, 0));
            place = place.Then(Affine.Translate(spot.At));
            var group = Guid.NewGuid();
            string note = $"{spot.Number}|{spot.Piece.Number}|{spot.Piece.Item.Key}";
            var empty = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var original in design.Shapes)
            {
                if (!map.TryGetValue(original.LayerId, out var layerId)) continue;
                var copy = original.With(id: Guid.NewGuid(), layerId: layerId, transform: original.Transform.Then(place));
                copy = Bake(copy, fields, empty);
                shapes.Add(copy.WithGroup(group).WithAddonData(PieceKey, note));
            }
            foreach (var e in empty) notes.Add($"{Who(spot.Piece.Item)}: {{{e}}} is empty.");
            if (o.Labels)
            {
                labels ??= Layer.Default(0, LayerMode.Score) with { Name = LabelLayerName, Output = false, Color = 0x9CA3AF };
                var label = TextShape.Create($"{spot.Number} · {spot.Piece.Item.Order}", new TextStyle("Arial", 3), labels.Id, Affine.Identity, name: $"Label {spot.Number}");
                var lb = label.Bounds;
                var at = lb.IsEmpty ? spot.At : new Vec2(spot.At.X + 0.6 - lb.MinX, spot.At.Y + 0.6 - lb.MinY);
                shapes.Add(label.With(transform: Affine.Translate(at)).WithGroup(group).WithAddonData(PieceKey, note));
            }
        }
        // Engraving before cutting, whichever product brought its layers first: a cut-out piece may move.
        var byId = layers.ToDictionary(l => l.Id);
        bool Cuts(Layer l) => (l.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent : l).Mode == LayerMode.Cut;
        layers = [.. layers.OrderBy(l => Cuts(l) ? 1 : 0)];
        if (labels is not null) layers.Add(labels);
        // Fill in the program's own fields ({serial}, {date}) as it would on screen.
        var filled = VariableText.Refresh(new DocState([.. layers], [.. shapes]) { Vars = vars }, now);
        return new DesignPage(name, filled.Layers, filled.Shapes, []);
    }

    /// <summary>
    /// The design's layers on the sheet: one already there with the same settings and name is
    /// shared; otherwise a copy is added (sub-layers keep pointing at their parent's copy).
    /// </summary>
    static Dictionary<Guid, Guid> MapLayers(DocState design, List<Layer> sheetLayers)
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var l in design.Layers)
        {
            Guid? parent = l.ParentId is { } p ? map.TryGetValue(p, out var np) ? np : null : null;
            if (l.ParentId is not null && parent is null) continue;
            var want = l with { ParentId = parent };
            var same = sheetLayers.FirstOrDefault(x => (x with { Id = Guid.Empty }) == (want with { Id = Guid.Empty }));
            if (same is not null) { map[l.Id] = same.Id; continue; }
            var added = want with { Id = Guid.NewGuid() };
            if (sheetLayers.Any(x => x.Name == added.Name)) added = added with { Name = $"{added.Name} ({sheetLayers.Count + 1})" };
            sheetLayers.Add(added);
            map[l.Id] = added.Id;
        }
        return map;
    }

    /// <summary>The shape with the order's fields written into its words or code.</summary>
    public static Shape Bake(Shape s, IReadOnlyDictionary<string, string> fields, ISet<string>? empty = null)
    {
        switch (s)
        {
            case TextShape t when t.Text.Contains('{'):
                foreach (var e in OrderFields.Empty(t.Text, fields)) empty?.Add(e);
                var text = OrderFields.Bake(t.Text, fields);
                return text == t.Text ? s : Anchored(t, t.Edit(text, t.Style));
            case CodeShape c when c.Spec.Content.Contains('{'):
                foreach (var e in OrderFields.Empty(c.Spec.Content, fields)) empty?.Add(e);
                var content = OrderFields.Bake(c.Spec.Content, fields);
                return content == c.Spec.Content ? s : c.Edit(c.Spec with { Content = content });
            default:
                return s;
        }
    }

    /// <summary>
    /// The new words kept where the placeholder was: a name shorter or taller than "{Personalization}"
    /// stays centred on the same line (left-aligned text keeps its left edge, right-aligned its right).
    /// </summary>
    static Shape Anchored(TextShape was, TextShape now)
    {
        if (was.Guide is not null) return now;
        Rect2 a = was.Bounds, b = now.Bounds;
        if (a.IsEmpty || b.IsEmpty) return now;
        double dx = was.Style.Align switch
        {
            TextAlign.Left => a.MinX - b.MinX,
            TextAlign.Right => a.MaxX - b.MaxX,
            _ => a.Center.X - b.Center.X,
        };
        double dy = a.Center.Y - b.Center.Y;
        return Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9 ? now : now.With(transform: now.Transform.Then(Affine.Translate(dx, dy)));
    }

    /// <summary>One item's piece on its own (for a proof): the design with the fields written in, at 0, 0.</summary>
    public static DocState Proof(DocState design, OrderItem item, DateTime now)
    {
        var fields = OrderFields.For(item, 1, item.Quantity);
        var shapes = design.Shapes.Select(s => Bake(s, fields)).ToImmutableList();
        return VariableText.Refresh(design with { Shapes = shapes, OtherPages = [], AddonData = design.AddonData }, now);
    }

    // ---- reading sheets back (packing list, what finished) ----------------------------------------

    /// <summary>A piece found on a page: its number there, which piece of the item, the item's key, and where it is.</summary>
    public sealed record Found(int Number, int PieceOfItem, string Key, Rect2 Box, List<PathShape> Outlines);

    /// <summary>A page with pieces on it.</summary>
    public sealed record FoundSheet(string Name, List<Found> Pieces);

    /// <summary>Every page of the project with order pieces, in page order (the page on screen included).</summary>
    public static List<FoundSheet> Read(DocState s)
    {
        var names = Pages.Names(s);
        var result = new List<FoundSheet>();
        int other = 0;
        for (int i = 0; i < names.Count; i++)
        {
            (ImmutableList<Layer> layers, ImmutableList<Shape> shapes) = i == s.PageIndex ? (s.Layers, s.Shapes) : (s.OtherPages[other].Layers, s.OtherPages[other++].Shapes);
            var found = OnPage(layers, shapes);
            if (found.Count > 0) result.Add(new FoundSheet(names[i], found));
        }
        return result;
    }

    /// <summary>The pieces on the page on screen whose shapes are burned (on an output layer, not hidden or switched off).</summary>
    public static List<Found> OnScreen(DocState s) => OnPage(s.Layers, s.Shapes, burnedOnly: true);

    static List<Found> OnPage(ImmutableList<Layer> layers, ImmutableList<Shape> shapes, bool burnedOnly = false)
    {
        var output = layers.Where(l => l.Output).Select(l => l.Id).ToHashSet();
        var pieces = new Dictionary<string, (int Number, int Of, string Key, Rect2 Box, List<PathShape> Outlines, bool Burned)>();
        foreach (var sh in shapes)
        {
            if (!sh.AddonData.TryGetValue(PieceKey, out var note)) continue;
            var parts = note.Split('|', 3);
            if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int of);
            string id = note;
            bool burned = output.Contains(sh.LayerId) && sh.Visible && sh.Output;
            if (!pieces.TryGetValue(id, out var p)) p = (n, Math.Max(1, of), parts[2], Rect2.Empty, [], false);
            if (output.Contains(sh.LayerId))
            {
                p.Box = p.Box.Union(sh.Bounds);
                if (sh is PathShape ps) p.Outlines.Add(ps);
            }
            else if (p.Box.IsEmpty && sh is not TextShape) p.Box = sh.Bounds;
            p.Burned |= burned;
            pieces[id] = p;
        }
        return [.. pieces.Values.Where(p => !burnedOnly || p.Burned).OrderBy(p => p.Number).Select(p => new Found(p.Number, p.Of, p.Key, p.Box, p.Outlines))];
    }
}
