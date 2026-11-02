using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Papercraft;

/// <summary>Puts an unfolded model on the work area: cut outlines, fold lines and edge numbers, nested a bed at a time.</summary>
public static class PapercraftLayout
{
    public const string CutLayer = "Papercraft · cut";
    public const string MountainLayer = "Papercraft · mountain folds";
    public const string ValleyLayer = "Papercraft · valley folds";
    public const string NumbersLayer = "Papercraft · numbers";

    /// <summary>
    /// One outline a piece (faces and tabs together) on a Cut layer; mountain folds (and the folds
    /// along the tabs) on a Score layer, valley folds on a dashed Score layer, and the edge numbers
    /// lightly marked, all run before the cut. Each piece is a group, nested on the bed (or the
    /// material outline) a page a bed. Returns the design showing the first page used, and how many beds.
    /// </summary>
    public static (DocState State, int Sheets) Place(DocState s, PaperNet net, MachineProfile machine, bool numbers, double gapMm = 3, string title = "Papercraft", CancellationToken ct = default, PathGeometry? material = null)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Papercraft" : title.Trim();
        bool fresh = material is null || s.Shapes.Count == 0;
        if (material is null && s.Shapes.Count > 0) s = Pages.Add(s, title);
        int firstPage = s.PageIndex;
        int sheets = 1;
        var (cut, mountain, valley, marks) = Layers(ref s);

        var units = new List<List<Shape>>();
        foreach (var piece in net.Pieces)
        {
            ct.ThrowIfCancellationRequested();
            var shapes = Piece(piece, net.Pieces.Count, numbers, cut, mountain, valley, marks, gapMm);
            if (shapes.Count == 0) continue;
            var box = shapes[0].Bounds;
            if (Math.Min(box.Width, box.Height) + 2 * gapMm > Math.Min(machine.FieldW, machine.FieldH) || Math.Max(box.Width, box.Height) + 2 * gapMm > Math.Max(machine.FieldW, machine.FieldH))
                throw new InvalidOperationException($"Piece {piece.Index + 1} is {box.Width:0} × {box.Height:0} mm: bigger than the bed. Make the model smaller.");
            if (shapes.Count > 1)
            {
                var g = Guid.NewGuid();
                shapes = [.. shapes.Select(x => x.WithGroup(g))];
            }
            units.Add(shapes);
        }

        var options = new NestOptions { Sheet = material, SpacingMm = gapMm, MarginMm = gapMm, RotationStepDeg = 90, ResolutionMm = 0.5 };
        while (units.Count > 0)
        {
            // Each page has its own layers: put every shape on this page's layer of the same name.
            var byName = new Dictionary<string, Guid> { [CutLayer] = cut.Id, [MountainLayer] = mountain.Id, [ValleyLayer] = valley.Id, [NumbersLayer] = marks.Id };
            var onPage = units.Select(u => u.Select(x => x.With(layerId: byName[LayerOf(x)])).ToList()).ToList();
            s = Ops.AddShapes(s, onPage.SelectMany(u => u));
            var ids = onPage.SelectMany(u => u.Select(x => x.Id)).ToHashSet();
            var (nested, report) = Nesting.Nest(s, ids, options, machine, ct);
            if (report.Placed == 0 && fresh)
                throw new InvalidOperationException(material is null
                    ? "A piece doesn't fit on the bed with the spacing around it. Make the model smaller."
                    : "A piece doesn't fit on the material with the spacing around it. Make the model smaller or outline a bigger sheet.");
            var unplaced = report.Unplaced.ToHashSet();
            s = Ops.Delete(nested, unplaced);
            units = [.. units.Where((u, i) => onPage[i].Any(x => unplaced.Contains(x.Id)))];
            if (units.Count == 0) break;
            s = Pages.Add(s, $"{title} {++sheets}");
            fresh = true;
            if (material is not null)
            {
                var (withLayer, ml) = MaterialOutline.Ensure(s);
                s = Ops.AddShapes(withLayer, [new PathShape { Name = "Material", LayerId = ml.Id, Geometry = material }]);
            }
            (cut, mountain, valley, marks) = Layers(ref s);
        }
        return (Pages.SwitchTo(s, firstPage), sheets);
    }

    // Shapes remember which of the four they are by name prefix (set in Piece).
    static string LayerOf(Shape x) => x.Name.EndsWith(" · cut") ? CutLayer : x.Name.EndsWith(" · mountain folds") ? MountainLayer : x.Name.EndsWith(" · valley folds") ? ValleyLayer : NumbersLayer;

    /// <summary>This page's papercraft layers, made when missing: marks and folds before the cut.</summary>
    static (Layer Cut, Layer Mountain, Layer Valley, Layer Marks) Layers(ref DocState st)
    {
        var cut = st.Layers.FirstOrDefault(l => l.Name == CutLayer);
        if (cut is null)
        {
            cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
            st = Ops.AddLayer(st, cut);
        }
        int Before(DocState d) => d.Layers.FindIndex(l => l.Id == cut.Id);
        var valley = st.Layers.FirstOrDefault(l => l.Name == ValleyLayer);
        if (valley is null)
        {
            // Dashed, so it folds either way: towards the scored side too.
            valley = Layer.Default(st.Layers.Count, LayerMode.Score) with { Name = ValleyLayer, PerforateMm = 3, PerforateGapMm = 1.5 };
            st = Ops.AddLayer(st, valley, Before(st));
        }
        var mountain = st.Layers.FirstOrDefault(l => l.Name == MountainLayer);
        if (mountain is null)
        {
            mountain = Layer.Default(st.Layers.Count, LayerMode.Score) with { Name = MountainLayer };
            st = Ops.AddLayer(st, mountain, st.Layers.FindIndex(l => l.Id == valley.Id));
        }
        var marks = st.Layers.FirstOrDefault(l => l.Name == NumbersLayer);
        if (marks is null)
        {
            var score = Layer.Default(st.Layers.Count, LayerMode.Score);
            marks = score with { Name = NumbersLayer, Laser = score.Laser with { Power = Math.Min(score.Laser.Power, 15) } };
            st = Ops.AddLayer(st, marks, st.Layers.FindIndex(l => l.Id == mountain.Id));
        }
        return (cut, mountain, valley, marks);
    }

    /// <summary>A piece's shapes, its outline first, moved so its corner is at (gap, gap).</summary>
    static List<Shape> Piece(PaperPiece piece, int count, bool numbers, Layer cut, Layer mountain, Layer valley, Layer marks, double gapMm)
    {
        var name = $"Piece {piece.Index + 1}";
        var outline = Outline(piece);
        if (outline.IsEmpty) return [];
        var box = outline.Bounds();
        var move = Affine.Translate(gapMm - box.MinX, gapMm - box.MinY);
        var shapes = new List<Shape> { new PathShape { Name = $"{name} · cut", LayerId = cut.Id, Geometry = outline.Transform(move) } };
        // Tabs fold back behind the face they're glued to: the same way as a ridge.
        var ridges = piece.Folds.Where(f => f.Kind == FoldKind.Mountain).Select(f => (f.A, f.B))
            .Concat(piece.Tabs.Select(t => (t.Outline[0], t.Outline[^1]))).ToList();
        if (ridges.Count > 0)
            shapes.Add(new PathShape { Name = $"{name} · mountain folds", LayerId = mountain.Id, Geometry = Lines(ridges).Transform(move) });
        var valleys = piece.Folds.Where(f => f.Kind == FoldKind.Valley).Select(f => (f.A, f.B)).ToList();
        if (valleys.Count > 0)
            shapes.Add(new PathShape { Name = $"{name} · valley folds", LayerId = valley.Id, Geometry = Lines(valleys).Transform(move) });
        if (numbers)
        {
            foreach (var e in piece.Edges.Where(e => e.Number > 0))
                if (Label($"{e.Number}", e.A, e.B, e.Inward, e.RoomMm, marks.Id, move) is { } t) shapes.Add(t);
            // The piece's own number, in its biggest face, when there's more than one piece.
            if (count > 1 && piece.Faces.Count > 0)
            {
                var (a, b, c) = piece.Faces.MaxBy(f => Math.Abs((f.B - f.A).Cross(f.C - f.A)));
                var centre = (a + b + c) / 3;
                double inr = Math.Abs((b - a).Cross(c - a)) / (a.Dist(b) + b.Dist(c) + c.Dist(a));
                double size = Math.Clamp(inr * 0.8, 0, 5);
                if (size >= 1.5)
                {
                    var text = TextShape.Create($"P{piece.Index + 1}", new TextStyle("Arial", size, Bold: true), marks.Id, Affine.Identity, name: $"{name} · name");
                    var tb = text.Bounds;
                    var at = move.Apply(centre);
                    shapes.Add(text.With(transform: Affine.Translate(at.X - tb.Center.X, at.Y - tb.Center.Y)));
                }
            }
        }
        return shapes;
    }

    /// <summary>An edge's number, just inside it, reading along it with its top towards the face.</summary>
    static Shape? Label(string number, Vec2 a, Vec2 b, Vec2 inward, double room, Guid layer, Affine move)
    {
        double len = a.Dist(b);
        double size = Math.Min(Math.Min(len / (0.7 * number.Length + 0.6), room * 0.35), 3.5);
        if (size < 1.2) return null;
        var text = TextShape.Create(number, new TextStyle("Arial", size), layer, Affine.Identity, name: $"Edge {number}");
        var tb = text.Bounds;
        // Rotate so the text's "up" (−Y) points into the face.
        double deg = Math.Atan2(inward.X, -inward.Y) * 180 / Math.PI;
        var at = move.Apply((a + b) / 2 + inward * (size * 0.8));
        var t = Affine.Translate(-tb.Center.X, -tb.Center.Y).Then(Affine.Rotate(deg)).Then(Affine.Translate(at.X, at.Y));
        return text.With(transform: t);
    }

    static PathGeometry Lines(IEnumerable<(Vec2 A, Vec2 B)> lines) =>
        new(lines.Select(l => new Subpath(l.A, [Seg.Line(l.B)], false)));

    /// <summary>The piece's outline: its faces and tabs joined into one shape.</summary>
    public static PathGeometry Outline(PaperPiece piece)
    {
        var parts = piece.Faces.Select(f => PathGeometry.Polyline([f.A, f.B, f.C], true))
            .Concat(piece.Tabs.Select(t => PathGeometry.Polyline(t.Outline, true)));
        return ShapeGeometry.Union(parts, 0.001);
    }
}
