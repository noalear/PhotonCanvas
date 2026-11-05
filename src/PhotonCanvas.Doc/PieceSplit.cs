using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

/// <summary>
/// A design bigger than one piece of material, marked across several (four boards that go together
/// into one picture). Each piece gets a page of its own: its outline as the page's material, its
/// share of the design on the design's own layers, all moved so the piece lies in the work area.
/// The other pieces and the rest of the design show on a layer that's never sent, so the pieces
/// already done can be laid beside this one and everything lined up to them.
/// </summary>
public static class PieceSplit
{
    /// <summary>The layer the other pieces and their share of the design go on (shown, never sent).</summary>
    public const string OthersLayer = "Other pieces";

    /// <summary>What a piece's outline is called on the pages: "Piece 2".</summary>
    public static string PieceName(int number) => $"Piece {number}";

    /// <param name="State">The design with a page per piece added after the one split, the first of them shown.</param>
    /// <param name="Notes">Anything to tell the user (a piece wider than the work area, a piece with nothing on it).</param>
    public sealed record Result(DocState State, int Pieces, IReadOnlyList<string> Notes);

    /// <summary>How a shape lies on a piece.</summary>
    enum Lies { Inside, Across, Outside }

    /// <summary>
    /// The pieces in reading order: rows from the top (pieces whose heights overlap by more than half
    /// are a row), each row from the left.
    /// </summary>
    public static List<PathShape> InReadingOrder(IEnumerable<PathShape> pieces)
    {
        var rows = new List<List<PathShape>>();
        foreach (var p in pieces.OrderBy(p => p.Bounds.MinY))
        {
            var b = p.Bounds;
            var row = rows.FirstOrDefault(r =>
            {
                var a = r[0].Bounds;
                double overlap = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
                return overlap > 0.5 * Math.Min(a.Height, b.Height);
            });
            if (row is null) rows.Add([p]);
            else row.Add(p);
        }
        return [.. rows.SelectMany(r => r.OrderBy(p => p.Bounds.MinX))];
    }

    /// <summary>
    /// Split the shapes <paramref name="art"/> across the <paramref name="pieces"/> (closed outlines,
    /// world mm), a page each. Pictures are clipped to the piece; outlines on an Engrave layer keep
    /// the area on the piece; other lines (cut, score) keep the stretch that lies on it. A shape wholly
    /// on one piece is copied as it is (text stays text).
    /// </summary>
    /// <param name="marginMm">How far inside the work area's edge a piece that has to move is put (room for the camera to see its far edge when it lies a little off).</param>
    public static Result Split(DocState s, IReadOnlyList<PathShape> pieces, IReadOnlySet<Guid> art, double fieldW, double fieldH, double marginMm = 5)
    {
        var ordered = InReadingOrder(pieces);
        var pieceIds = ordered.Select(p => p.Id).ToHashSet();
        var material = MaterialOutline.Layer(s)?.Id;
        var shapes = s.Shapes.Where(x => art.Contains(x.Id) && !pieceIds.Contains(x.Id) && x.LayerId != material).ToList();
        var notes = new List<string>();
        var pages = new List<DesignPage>();
        // Engrave sub-layers of a Cut or Score layer: its lines are cut to a piece as lines, which a
        // fill can't fill, so on each page such a sub-layer becomes a layer of its own with the area share
        // (by the area, which can be on a piece the lines never touch: a big outline round the piece).
        var fillSubs = s.Layers.Where(l => l.Mode == LayerMode.Fill && l.ParentId is { } p && s.FindLayer(p) is { Mode: LayerMode.Cut or LayerMode.Score })
            .GroupBy(l => l.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(l => l.Id).ToList());
        for (int i = 0; i < ordered.Count; i++)
        {
            var piece = ordered[i];
            // Areas are cut to the piece exactly; lines to a hair more, so one drawn on the piece's own
            // edge (a border) counts as on it.
            var region = new PathGeometry(piece.World.Subpaths.Where(sp => sp.Closed));
            var lineRegion = ShapeGeometry.Offset(region, 0.01, JoinStyle.Sharp, EndStyle.Flat);
            var regionFlat = lineRegion.Flatten(0.005).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();

            // The page's layers: the design's, then the material and the other pieces (neither sent).
            var map = s.Layers.ToDictionary(l => l.Id, _ => Guid.NewGuid());
            var layers = s.Layers.Select(l => l with { Id = map[l.Id], ParentId = l.ParentId is { } p && map.TryGetValue(p, out var np) ? np : l.ParentId }).ToList();
            var mat = layers.FirstOrDefault(l => l.Name == MaterialOutline.LayerName && !l.Output);
            if (mat is null)
            {
                mat = Layer.Default(layers.Count, LayerMode.Score) with { Name = MaterialOutline.LayerName, Output = false, Color = 0x8A8F98 };
                layers.Add(mat);
            }
            var others = layers.FirstOrDefault(l => l.Name == OthersLayer && !l.Output);
            if (others is null)
            {
                others = Layer.Default(layers.Count, LayerMode.Score) with { Name = OthersLayer, Output = false, Color = 0x5B6170 };
                layers.Add(others);
            }

            var groups = new Dictionary<Guid, Guid>();
            T Grouped<T>(T made, Shape from) where T : Shape
            {
                if (from.GroupId is not { } g) return made;
                if (!groups.TryGetValue(g, out var ng)) groups[g] = ng = Guid.NewGuid();
                return (T)made.WithGroup(ng);
            }

            // Drawn in this order: the greyed rest underneath, this piece's share, its outline on top.
            var greyed = new List<Shape>();
            var share = new List<Shape>();
            for (int j = 0; j < ordered.Count; j++)
                if (j != i) greyed.Add(new PathShape { Name = PieceName(j + 1), LayerId = others.Id, Geometry = ordered[j].World });
            int live = 0;
            var ownLayer = new HashSet<Guid>(); // sub-layers that hold their own shapes on this page
            foreach (var x in shapes)
            {
                var mode = s.FindLayer(x.LayerId)?.Mode ?? LayerMode.Cut;
                var lies = Where(x, region, lineRegion, regionFlat, mode);
                var layer = map.TryGetValue(x.LayerId, out var ml) ? ml : x.LayerId;
                bool filled = false;
                if (x is PathShape && fillSubs.TryGetValue(x.LayerId, out var subs))
                {
                    var areaLies = Where(x, region, lineRegion, regionFlat, LayerMode.Fill);
                    var area = areaLies switch { Lies.Inside => x, Lies.Across => Clip(x, region, lineRegion, LayerMode.Fill), _ => null };
                    if (area is not null)
                        foreach (var sub in subs)
                        {
                            share.Add(Grouped(area.With(layerId: map[sub], id: Guid.NewGuid()), x));
                            ownLayer.Add(map[sub]);
                            filled = true;
                        }
                }
                if (lies == Lies.Inside) share.Add(Grouped(x.With(layerId: layer, id: Guid.NewGuid()), x));
                else
                {
                    if (lies == Lies.Across && Clip(x, region, lineRegion, mode) is { } part)
                        share.Add(Grouped(part.With(layerId: layer, id: Guid.NewGuid()), x));
                    // The whole of it, greyed, so the design can be seen across the pieces.
                    greyed.Add(x.With(layerId: others.Id, id: Guid.NewGuid()).WithGroup(null));
                }
                if (lies != Lies.Outside || filled) live++;
            }
            if (live == 0) notes.Add($"Nothing of the design is on {PieceName(i + 1).ToLowerInvariant()}.");
            if (ownLayer.Count > 0) layers = [.. layers.Select(l => ownLayer.Contains(l.Id) ? l with { ParentId = null } : l)];
            List<Shape> onPage = [.. greyed, .. share, new PathShape { Name = PieceName(i + 1), LayerId = mat.Id, Geometry = piece.World }];

            // Moved (as little as it takes) so the piece is in the work area.
            var b = piece.Bounds;
            double dx = Into(b.MinX, b.MaxX, fieldW, marginMm), dy = Into(b.MinY, b.MaxY, fieldH, marginMm);
            if (b.Width > fieldW || b.Height > fieldH)
                notes.Add($"{PieceName(i + 1)} ({b.Width:0.#} × {b.Height:0.#} mm) is bigger than the work area ({fieldW:0} × {fieldH:0} mm): only its middle can be marked.");
            if (dx != 0 || dy != 0)
            {
                var move = Affine.Translate(dx, dy);
                onPage = [.. onPage.Select(x => x.With(transform: x.Transform.Then(move)))];
            }
            pages.Add(new DesignPage($"{PieceName(i + 1)} of {ordered.Count}", [.. layers], [.. onPage], []));
        }
        return new Result(Pages.Insert(s, pages), ordered.Count, notes);
    }

    /// <summary>How far to move a span so it lies inside 0…size (at least the margin in, if it fits; centred if it doesn't).</summary>
    static double Into(double min, double max, double size, double margin)
    {
        double span = max - min;
        if (span + 2 * margin > size) return (size - span) / 2 - min;
        if (min < margin) return margin - min;
        if (max > size - margin) return size - margin - max;
        return 0;
    }

    /// <summary>
    /// Wholly on the piece (every point of it inside), not at all (nothing of its area on Engrave
    /// layers and for pictures, nothing of its lines elsewhere), or partly.
    /// </summary>
    static Lies Where(Shape x, PathGeometry region, PathGeometry lineRegion, List<List<Vec2>> regionFlat, LayerMode mode)
    {
        PathGeometry outline;
        bool byArea;
        if (x is ImageShape im) { outline = PictureArea(im); byArea = true; }
        else if (x is PathShape p) { outline = p.World; byArea = mode == LayerMode.Fill && ShapeGeometry.Area(outline) > 1e-6; }
        else return Lies.Outside;
        bool none = byArea
            ? ShapeGeometry.Area(ShapeGeometry.Boolean(outline, region, BoolOp.Intersect)) <= 1e-6
            : ShapeGeometry.ClipLines(outline, lineRegion).IsEmpty;
        if (none) return Lies.Outside;
        bool all = outline.Flatten(0.01).All(f => f.Points.All(pt => regionFlat.Count(poly => Geom.Inside(poly, pt)) % 2 == 1));
        return all ? Lies.Inside : Lies.Across;
    }

    /// <summary>The share of a shape on the piece, or null when nothing of it is.</summary>
    static Shape? Clip(Shape x, PathGeometry region, PathGeometry lineRegion, LayerMode mode)
    {
        if (x is ImageShape im)
        {
            var clip = ShapeGeometry.Boolean(PictureArea(im), region, BoolOp.Intersect);
            return clip.IsEmpty ? null : im.WithClip(clip);
        }
        if (x is not PathShape p) return null;
        var g = mode == LayerMode.Fill ? ShapeGeometry.Boolean(p.World, region, BoolOp.Intersect) : ShapeGeometry.ClipLines(p.World, lineRegion);
        return g.IsEmpty ? null : new PathShape { Name = p.Name, LayerId = p.LayerId, Geometry = g };
    }

    /// <summary>Where a picture shows, world mm: its clip if it has one, else its rectangle.</summary>
    static PathGeometry PictureArea(ImageShape im) =>
        (im.Clip ?? PathGeometry.Rect(0, 0, im.SizeMm.X, im.SizeMm.Y)).Transform(im.Transform);

}
