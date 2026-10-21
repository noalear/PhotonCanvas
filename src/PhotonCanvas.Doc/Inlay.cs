using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

/// <summary>Settings for an inlay (see <see cref="Inlay"/>).</summary>
public sealed record InlayOptions
{
    /// <summary>The veneer's thickness: the pocket goes this deep, so the piece sits flush (mm).</summary>
    public double VeneerMm { get; init; } = 0.6;
    /// <summary>Room round the piece in the pocket, each side (mm): glue, and wood moving.</summary>
    public double GapMm { get; init; } = 0.05;
    /// <summary>How deep one engrave pass goes in the board (mm, from its etch-rate test).</summary>
    public double EtchMmPerPass { get; init; } = 0.15;
    /// <summary>The engrave settings for the pocket (one pass each; the passes come from the depth).</summary>
    public Layer? PocketSettings { get; init; }
    /// <summary>The cut settings for the veneer, kerf included (so the piece comes out the drawn size).</summary>
    public Layer? PieceSettings { get; init; }
}

/// <summary>
/// An inlay from one design: a pocket engraved into the board (the shapes, a fit gap bigger, as deep
/// as the veneer: passes of the measured etch depth, each turned so the floor comes out flat), and
/// the piece cut from the veneer at the drawn size (the cut test's kerf on its layer), on a page of
/// its own in the same place, so the veneer goes where the board was.
/// </summary>
public static class Inlay
{
    public const string PocketLayer = "Inlay · pocket", PieceLayer = "Inlay · piece";

    /// <summary>Passes for the pocket: the veneer's thickness in passes of the etch depth (at least one).</summary>
    public static int Passes(InlayOptions o) => Math.Max(1, (int)Math.Ceiling(o.VeneerMm / Math.Max(0.001, o.EtchMmPerPass) - 1e-9));

    /// <summary>The pocket and the piece for <paramref name="shapes"/> (closed outlines, bed mm). The design shows the board's page.</summary>
    public static DocState Make(DocState s, IReadOnlyList<PathShape> shapes, InlayOptions o)
    {
        var outlines = shapes.Select(x => x.World).Where(g => !g.IsEmpty).ToList();
        if (outlines.Count == 0) throw new InvalidOperationException("Select the closed shapes to inlay first.");
        var all = ShapeGeometry.Union(outlines);
        int passes = Passes(o);
        // The pocket, on the board: the shape, the gap bigger all round, engraved down in turned passes.
        var pocketLayer = (o.PocketSettings ?? Layer.Default(s.Layers.Count, LayerMode.Fill)) with
        {
            Id = Guid.NewGuid(), Name = PocketLayer, Mode = LayerMode.Fill, ParentId = null,
        };
        pocketLayer = pocketLayer with { Laser = pocketLayer.Laser.WithPasses(passes), AngleStepDeg = pocketLayer.AngleStepDeg != 0 ? pocketLayer.AngleStepDeg : 37 };
        s = Ops.AddLayer(s, pocketLayer);
        var pocket = o.GapMm > 0 ? ShapeGeometry.Offset(all, o.GapMm, JoinStyle.Sharp, EndStyle.Flat) : all;
        s = Ops.AddShapes(s, [new PathShape { Name = "Inlay pocket", LayerId = pocketLayer.Id, Geometry = pocket }]);
        int board = s.PageIndex;
        // The piece, on the veneer: its own page, the same place, cut to the drawn size.
        s = Pages.Add(s, "Inlay piece");
        var pieceLayer = (o.PieceSettings ?? Layer.Default(s.Layers.Count, LayerMode.Cut)) with { Id = Guid.NewGuid(), Name = PieceLayer, Mode = LayerMode.Cut, ParentId = null };
        s = Ops.AddLayer(s, pieceLayer);
        s = Ops.AddShapes(s, [new PathShape { Name = "Inlay piece", LayerId = pieceLayer.Id, Geometry = all }]);
        return Pages.SwitchTo(s, board);
    }
}
