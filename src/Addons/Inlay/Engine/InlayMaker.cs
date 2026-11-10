using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>What the pieces go into.</summary>
public enum InlayInto
{
    /// <summary>A pocket engraved into a board, as deep as the pieces are thick (veneer inlay).</summary>
    Pocket,
    /// <summary>Holes cut right through a sheet: a background veneer (marquetry), or a board the same thickness as the pieces (a through inlay).</summary>
    Holes,
    /// <summary>Nothing: the pieces fit each other, to glue onto a backing.</summary>
    Nothing,
}

/// <summary>How one material is cut.</summary>
public sealed record WoodCut
{
    public string Name { get; init; } = "";
    /// <summary>The cut's width (mm): every cut line moves half of it away from the piece, so pieces come out the drawn size.</summary>
    public double KerfMm { get; init; } = 0.1;
    /// <summary>The laser settings (null: the default cut settings). The kerf is drawn in, so the layer's own kerf is set to 0.</summary>
    public Layer? Settings { get; init; }
}

/// <summary>Everything the maker needs besides the pieces.</summary>
public sealed record InlayOptions
{
    public InlayInto Into { get; init; } = InlayInto.Pocket;
    /// <summary>The gap at every joint (mm): between two pieces, and between a piece and the pocket or hole. Negative = a press fit.</summary>
    public double FitMm { get; init; } = 0.05;
    /// <summary>How thick the pieces are: the pocket goes this deep (mm).</summary>
    public double ThicknessMm { get; init; } = 0.6;
    /// <summary>How deep one engrave pass goes in the board (mm, from its etch-rate test).</summary>
    public double EtchMmPerPass { get; init; } = 0.15;
    /// <summary>The pocket's engrave settings (one pass each; the passes come from the depth).</summary>
    public Layer? PocketSettings { get; init; }
    /// <summary>The sheet the holes are cut in (<see cref="InlayInto.Holes"/>).</summary>
    public WoodCut Sheet { get; init; } = new() { Name = "Background" };
    /// <summary>Cut the sheet's outside edge too, <see cref="MarginMm"/> round the picture (a background veneer); off for holes in a bigger board.</summary>
    public bool CutSheetEdge { get; init; } = true;
    public double MarginMm { get; init; } = 10;
    /// <summary>Cut the pieces mirrored, to be turned over: their underside (where the cut is widest) shows, so joints close up on the face.</summary>
    public bool Flip { get; init; }
    /// <summary>Lay each wood's pieces out close together in rows (keeping their grain direction) instead of where they go in the picture.</summary>
    public bool Pack { get; init; }
    /// <summary>Space between packed pieces (mm).</summary>
    public double SpacingMm { get; init; } = 2;
    /// <summary>Lightly mark each piece's number on it (on the side facing up). Off: the numbers are only on screen.</summary>
    public bool MarkNumbers { get; init; }
    /// <summary>How each wood is cut, in the order of <see cref="InlayDesign.Woods"/> (missing ones get the default).</summary>
    public IReadOnlyList<WoodCut> Woods { get; init; } = [];
}

/// <summary>What making it came to: the pages added, and anything to watch out for.</summary>
public sealed record InlayResult(DocState State, IReadOnlyList<string> Pages, IReadOnlyList<string> Warnings, int Passes);

/// <summary>
/// Pieces that fit: each wood's pieces on a page of their own, cut with their kerf drawn in (every
/// cut line half a kerf outside the piece, or inside its holes) less half the fit gap, so two pieces
/// side by side leave exactly the gap between them. What they go into gets the other half: a pocket
/// engraved half a gap bigger than the picture, or holes cut in a sheet half a gap bigger (their
/// cut lines half the sheet's kerf inside). The design page itself is left as it is, to show where
/// everything goes.
/// </summary>
public static class InlayMaker
{
    public const string Prefix = "Inlay · ";

    /// <summary>Passes for the pocket: the thickness in passes of the etch depth (at least one, never short).</summary>
    public static int Passes(InlayOptions o) => Math.Max(1, (int)Math.Ceiling(o.ThicknessMm / Math.Max(0.001, o.EtchMmPerPass) - 1e-9));

    /// <summary>How far a piece's cut line goes out from its drawn edge (holes in it come in by as much).</summary>
    public static double PieceOffset(double kerfMm, double fitMm) => kerfMm / 2 - fitMm / 2;

    /// <summary>A piece's cut line: its edge moved out by <see cref="PieceOffset"/> (sharp corners stay sharp).</summary>
    public static PathGeometry CutLine(PathGeometry area, double kerfMm, double fitMm)
    {
        double d = PieceOffset(kerfMm, fitMm);
        return Math.Abs(d) < 1e-9 ? ShapeGeometry.Union([area]) : ShapeGeometry.Offset(area, d, JoinStyle.Sharp, EndStyle.Flat, miterLimit: 8, tolerance: 0.005);
    }

    /// <summary>The sheet round the picture: its edge <see cref="InlayOptions.MarginMm"/> out from it.</summary>
    public static Rect2 SheetRect(InlayDesign d, InlayOptions o) => d.Bounds.Inflate(Math.Max(0, o.MarginMm));

    /// <summary>
    /// The sheet (<see cref="InlayInto.Holes"/>) as pieces: the main sheet with the picture's holes in
    /// it (first), then any islands of sheet the picture rings round (a ring's middle), which come out
    /// as loose pieces of the sheet's wood, numbered after the others.
    /// </summary>
    public static (PathGeometry Main, IReadOnlyList<Piece> Islands) Sheet(InlayDesign d, InlayOptions o)
    {
        var r = SheetRect(d, o);
        var region = ShapeGeometry.Boolean(PathGeometry.Rect(r.MinX, r.MinY, r.Width, r.Height), d.All, BoolOp.Subtract);
        var parts = InlayDesign.Components(region);
        // The main sheet is the part reaching the rectangle's corner.
        int main = parts.FindIndex(p => p.Bounds().MinX <= r.MinX + 1e-3 && p.Bounds().MinY <= r.MinY + 1e-3);
        var islands = parts.Where((p, i) => i != main && ShapeGeometry.Area(p) >= InlayDesign.SliverMm2).Select(p => (-1, p)).ToList();
        int next = d.Pieces.Count == 0 ? 1 : d.Pieces.Max(p => p.Number) + 1;
        return (main >= 0 ? parts[main] : PathGeometry.Empty, InlayDesign.Number(islands, next));
    }

    /// <summary>Make the pages (after the one on screen; the first of them is shown).</summary>
    public static InlayResult Make(DocState s, InlayDesign d, InlayOptions o, double bedWidthMm = 400)
    {
        if (d.IsEmpty) throw new InvalidOperationException("Select the closed shapes to inlay first.");
        var pages = new List<DesignPage>();
        var warnings = new List<string>();
        int passes = 0;
        var mirror = Affine.ScaleAbout(d.Bounds.Center, -1, 1);

        if (o.Into == InlayInto.Pocket)
        {
            passes = Passes(o);
            var pocketLayer = (o.PocketSettings ?? Layer.Default(0, LayerMode.Fill)) with
            {
                Id = Guid.NewGuid(), Name = Prefix + "pocket", Mode = LayerMode.Fill, ParentId = null, Visible = true, Locked = false, Output = true,
            };
            pocketLayer = pocketLayer with
            {
                Laser = pocketLayer.Laser.WithPasses(passes),
                // Each pass turned, so the floor comes out flat instead of ridged.
                AngleStepDeg = pocketLayer.AngleStepDeg != 0 ? pocketLayer.AngleStepDeg : 37,
            };
            var pocket = o.FitMm == 0 ? d.All : ShapeGeometry.Offset(d.All, o.FitMm / 2, JoinStyle.Sharp, EndStyle.Flat, miterLimit: 8, tolerance: 0.005);
            pages.Add(new DesignPage(Prefix + "pocket", [pocketLayer], [new PathShape { Name = "Pocket", LayerId = pocketLayer.Id, Geometry = pocket }], []));
        }

        var islands = new List<Piece>();
        if (o.Into == InlayInto.Holes)
        {
            var (main, isl) = Sheet(d, o);
            islands.AddRange(isl);
            var sheet = o.Sheet;
            var cut = CutLayer(sheet, 0x9CA3AF, $"{Name(sheet, "Background")} cut");
            var line = CutLine(main, sheet.KerfMm, o.FitMm);
            if (!o.CutSheetEdge)
            {
                // Only the holes: drop the outline round the outside (the board is bigger).
                var r = SheetRect(d, o);
                line = new PathGeometry(line.Subpaths.Where(sp => !(sp.Closed && Contains(new PathGeometry([sp]).Bounds(), r))));
            }
            var shapes = new List<Shape> { new PathShape { Name = o.CutSheetEdge ? "Sheet" : "Holes", LayerId = cut.Id, Geometry = line } };
            var numbers = NumbersLayer(o, cut);
            // Islands of sheet are cut where they are (from what drops out of the holes), never turned over.
            foreach (var p in isl)
            {
                shapes.Add(new PathShape { Name = $"Piece {p.Number}", LayerId = cut.Id, Geometry = CutLine(p.Area, sheet.KerfMm, o.FitMm) });
                shapes.Add(Number(p, numbers.Id, Affine.Identity));
            }
            pages.Add(new DesignPage(Prefix + Name(sheet, "Background"), isl.Count > 0 ? [numbers, cut] : [cut], [.. shapes], []));
        }

        // Each wood: its pieces, the kerf drawn in.
        for (int w = 0; w < d.Woods.Count; w++)
        {
            var mine = d.Pieces.Where(p => p.Wood == w).ToList();
            if (mine.Count == 0) continue;
            var wood = w < o.Woods.Count ? o.Woods[w] : new WoodCut { Name = d.Woods[w].Name };
            string name = Name(wood, d.Woods[w].Name);
            var cut = CutLayer(wood, d.Woods[w].Color, $"{name} pieces");
            var numbers = NumbersLayer(o, cut);
            var lines = mine.Select(p => CutLine(p.Area, wood.KerfMm, o.FitMm)).ToList();
            var place = mine.Select(_ => o.Flip ? mirror : Affine.Identity).ToList();
            if (o.Pack) place = Pack(lines, place, o.SpacingMm, bedWidthMm);
            else if (Overlapping(lines)) warnings.Add($"Some {name} pieces are too close together to cut where they are in the picture: tick Pack the pieces.");
            var shapes = new List<Shape>();
            for (int i = 0; i < mine.Count; i++)
            {
                if (lines[i].IsEmpty) { warnings.Add($"Piece {mine[i].Number} ({name}) is too thin to cut."); continue; }
                shapes.Add(new PathShape { Name = $"Piece {mine[i].Number}", LayerId = cut.Id, Geometry = lines[i].Transform(place[i]) });
                shapes.Add(Number(mine[i], numbers.Id, place[i]));
            }
            pages.Add(new DesignPage(Prefix + name, [numbers, cut], [.. shapes], []));
        }

        // Packed, they're no longer where they go: a key page shows each piece in place, nothing burned.
        if (o.Pack)
        {
            var layers = new List<Layer>();
            var shapes = new List<Shape>();
            for (int w = 0; w < d.Woods.Count; w++)
            {
                var wood = w < o.Woods.Count ? o.Woods[w] : new WoodCut();
                var l = Layer.Default(w, LayerMode.Fill) with { Name = Name(wood, d.Woods[w].Name), Color = d.Woods[w].Color, Output = false };
                layers.Add(l);
                shapes.AddRange(d.Pieces.Where(p => p.Wood == w).Select(p => new PathShape { Name = $"Piece {p.Number}", LayerId = l.Id, Geometry = p.Area }));
            }
            if (islands.Count > 0)
            {
                var l = Layer.Default(layers.Count, LayerMode.Fill) with { Name = Name(o.Sheet, "Background"), Color = 0x9CA3AF, Output = false };
                layers.Add(l);
                shapes.AddRange(islands.Select(p => new PathShape { Name = $"Piece {p.Number}", LayerId = l.Id, Geometry = p.Area }));
            }
            var nums = Layer.Default(layers.Count, LayerMode.Score) with { Name = "Piece numbers (not burned)", Color = 0xD4A017, Output = false };
            layers.Insert(0, nums);
            shapes.AddRange(d.Pieces.Concat(islands).Select(p => Number(p, nums.Id, Affine.Identity)));
            pages.Add(new DesignPage(Prefix + "key", [.. layers], [.. shapes], []));
        }

        var tiny = d.Pieces.Count(p => p.Label.Room < 0.4);
        if (tiny > 0) warnings.Add($"{tiny} piece{(tiny == 1 ? " is" : "s are")} under 0.8 mm across in places: small pieces can burn away or get lost.");
        if (d.Slivers > 0) warnings.Add($"{d.Slivers} sliver{(d.Slivers == 1 ? "" : "s")} left between shapes {(d.Slivers == 1 ? "was" : "were")} too small to be a piece and left out.");
        return new InlayResult(Pages.Insert(s, pages), [.. pages.Select(p => p.Name)], warnings, passes);
    }

    static string Name(WoodCut w, string otherwise) => string.IsNullOrWhiteSpace(w.Name) ? otherwise : w.Name.Trim();

    static bool Contains(Rect2 outer, Rect2 inner) => outer.Inflate(1e-3).Contains(inner) && outer.Width >= inner.Width - 1e-3;

    /// <summary>A cutting layer for one material, its kerf drawn in (so the layer's own is 0).</summary>
    static Layer CutLayer(WoodCut w, uint color, string name) => (w.Settings ?? Layer.Default(0, LayerMode.Cut)) with
    {
        Id = Guid.NewGuid(), Name = name, Mode = LayerMode.Cut, Color = color, ParentId = null, KerfMm = 0, Visible = true, Locked = false, Output = true,
    };

    /// <summary>The pieces' numbers: a light mark before the cuts (ticked), or only on screen.</summary>
    static Layer NumbersLayer(InlayOptions o, Layer cut)
    {
        var light = cut.Laser.Light;
        var laser = light == LightSource.Red
            ? new LaserSettings { Light = LightSource.Red, Power = 20, Speed = 1000 }
            : LaserSettings.Blue(Math.Clamp(cut.Laser.Power * 0.12, 3, 15), 200);
        return Layer.Default(0, LayerMode.Score) with
        {
            Name = o.MarkNumbers ? "Piece numbers" : "Piece numbers (not burned)",
            Color = 0xD4A017,
            Laser = laser,
            Output = o.MarkNumbers,
        };
    }

    /// <summary>A piece's number at its roomiest point, as big as fits (1–5 mm), moved with the piece.</summary>
    static Shape Number(Piece p, Guid layer, Affine place)
    {
        var (at, room) = p.Label;
        string text = p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // The number's box (about 0.6 of the height wide a digit) inside the circle of room round the point.
        double size = Math.Clamp(0.9 * room / Math.Sqrt(Math.Pow(0.3 * text.Length, 2) + 0.25), 1, 5);
        var t = TextShape.Create(text, new TextStyle("Arial", size), layer, Affine.Identity, name: $"Number {text}");
        var b = t.Bounds;
        var c = place.Apply(at);
        return b.IsEmpty ? t : t.With(transform: Affine.Translate(c.X - b.Center.X, c.Y - b.Center.Y));
    }

    /// <summary>Do any two of these outlines overlap (pieces too close to cut where they are)?</summary>
    static bool Overlapping(List<PathGeometry> lines)
    {
        var boxes = lines.Select(l => l.Bounds()).ToList();
        for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
                if (boxes[i].Intersects(boxes[j]) && ShapeGeometry.Area(ShapeGeometry.Boolean(lines[i], lines[j], BoolOp.Intersect)) > 1e-3)
                    return true;
        return false;
    }

    /// <summary>
    /// Shelf packing: tallest first, left to right in rows <paramref name="spacing"/> apart, a new row
    /// when the bed's width is used up. Pieces are never turned, so the grain runs the same way.
    /// </summary>
    static List<Affine> Pack(List<PathGeometry> lines, List<Affine> place, double spacing, double bedWidth)
    {
        spacing = Math.Max(0.5, spacing);
        var boxes = lines.Select((l, i) => l.Transform(place[i]).Bounds()).ToList();
        var order = Enumerable.Range(0, lines.Count).Where(i => !boxes[i].IsEmpty).OrderByDescending(i => boxes[i].Height).ThenBy(i => i).ToList();
        var result = place.ToList();
        double x = spacing, y = spacing, rowH = 0;
        foreach (int i in order)
        {
            var b = boxes[i];
            if (x > spacing && x + b.Width > bedWidth - spacing)
            {
                x = spacing;
                y += rowH + spacing;
                rowH = 0;
            }
            result[i] = place[i].Then(Affine.Translate(x - b.MinX, y - b.MinY));
            x += b.Width + spacing;
            rowH = Math.Max(rowH, b.Height);
        }
        return result;
    }
}
