using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>The fit test's settings: the same pocket or sheet and wood as the inlay, and the gaps to try.</summary>
public sealed record FitTestOptions
{
    public InlayInto Into { get; init; } = InlayInto.Pocket;
    /// <summary>The gaps to try, mm (each hole or pocket is this much bigger at every joint than its piece).</summary>
    public IReadOnlyList<double> Fits { get; init; } = DefaultFits;
    /// <summary>The wood the stars are cut from.</summary>
    public WoodCut Piece { get; init; } = new();
    /// <summary>What they go into: the pocket's settings and depth come from here too.</summary>
    public InlayOptions Inlay { get; init; } = new();
    /// <summary>The star's size, point to point (mm).</summary>
    public double SizeMm { get; init; } = 12;
    public Vec2 Origin { get; init; } = new(10, 10);

    public static readonly IReadOnlyList<double> DefaultFits = [-0.04, -0.02, 0, 0.02, 0.04, 0.06, 0.08, 0.1];
}

/// <summary>
/// A row of star-shaped holes (or pockets), each a different gap bigger than the star, labelled
/// with its gap, and on a page of its own the stars to push into them, cut with the kerf drawn in.
/// The one that goes in snug with no light round it is the gap to use. A star has the sharp points
/// and inside corners that are hardest to fit.
/// </summary>
public static class InlayFitTest
{
    public const string Prefix = "Inlay fit test · ";

    /// <summary>A five-pointed star, point up, <paramref name="size"/> mm across its points.</summary>
    public static PathGeometry Star(Vec2 c, double size)
    {
        double r = size / 2, inner = r * 0.45;
        var pts = Enumerable.Range(0, 10).Select(i =>
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : inner;
            return new Vec2(c.X + rr * Math.Cos(a), c.Y + rr * Math.Sin(a));
        }).ToList();
        return PathGeometry.Polyline(pts, true);
    }

    /// <summary>"+0.02", "0", "−0.04".</summary>
    public static string FitLabel(double f) => CutTest.FitLabel(f);

    /// <summary>The two pages (what the pieces go into, then the stars), after the one on screen; the first is shown.</summary>
    public static (DocState State, IReadOnlyList<string> Pages) Make(DocState s, FitTestOptions o)
    {
        var fits = o.Fits.Count > 0 ? o.Fits : FitTestOptions.DefaultFits;
        double size = Math.Clamp(o.SizeMm, 4, 60), pitch = size + 6, label = Math.Clamp(size * 0.25, 2, 4);
        var centres = fits.Select((_, i) => new Vec2(o.Origin.X + 3 + size / 2 + i * pitch, o.Origin.Y + 3 + size / 2)).ToList();
        var inl = o.Inlay;
        var pages = new List<DesignPage>();

        // What they go into: each star the fit bigger (all the fit on this side: the stars are cut as drawn).
        var labels = Layer.Default(0, LayerMode.Score) with { Name = "Fit test labels", Color = 0xD4A017, Laser = LaserSettings.Blue(8, 200) };
        var shapes = new List<Shape>();
        Layer into;
        if (o.Into == InlayInto.Pocket)
        {
            int passes = InlayMaker.Passes(inl);
            into = (inl.PocketSettings ?? Layer.Default(1, LayerMode.Fill)) with { Id = Guid.NewGuid(), Name = InlayMaker.Prefix + "pocket", Mode = LayerMode.Fill, ParentId = null, Output = true, Visible = true };
            into = into with { Laser = into.Laser.WithPasses(passes), AngleStepDeg = into.AngleStepDeg != 0 ? into.AngleStepDeg : 37 };
            for (int i = 0; i < fits.Count; i++)
                shapes.Add(new PathShape { Name = $"Pocket {FitLabel(fits[i])}", LayerId = into.Id, Geometry = Grow(Star(centres[i], size), fits[i]) });
        }
        else
        {
            // Into a sheet, or (pieces only) into the same wood: holes cut with the kerf drawn in.
            var sheet = o.Into == InlayInto.Holes ? inl.Sheet : o.Piece;
            into = (sheet.Settings ?? Layer.Default(1, LayerMode.Cut)) with { Id = Guid.NewGuid(), Name = InlayMaker.Prefix + "holes", Mode = LayerMode.Cut, ParentId = null, KerfMm = 0, Output = true, Visible = true };
            for (int i = 0; i < fits.Count; i++)
                shapes.Add(new PathShape { Name = $"Hole {FitLabel(fits[i])}", LayerId = into.Id, Geometry = Grow(Star(centres[i], size), fits[i] - sheet.KerfMm / 2) });
            if (o.Into == InlayInto.Nothing || inl.CutSheetEdge)
            {
                // A strip round them, so a veneer test comes away in one piece.
                double w = fits.Count * pitch, h = size + label + 9;
                var edge = PathGeometry.Rect(o.Origin.X - sheet.KerfMm / 2, o.Origin.Y - sheet.KerfMm / 2, w + sheet.KerfMm, h + sheet.KerfMm);
                shapes.Add(new PathShape { Name = "Strip", LayerId = into.Id, Geometry = edge });
            }
        }
        if (into.Laser.Light == LightSource.Red) labels = labels with { Laser = new LaserSettings { Light = LightSource.Red, Power = 20, Speed = 1000 } };
        var style = new TextStyle("Arial", label);
        for (int i = 0; i < fits.Count; i++)
        {
            var t = TextShape.Create(FitLabel(fits[i]), style, labels.Id, Affine.Identity);
            var b = t.Bounds;
            var at = new Vec2(centres[i].X, centres[i].Y + size / 2 + 2 + label / 2);
            shapes.Add(b.IsEmpty ? t : t.With(transform: Affine.Translate(at.X - b.Center.X, at.Y - b.Center.Y)));
        }
        pages.Add(new DesignPage(Prefix + (o.Into == InlayInto.Pocket ? "pockets" : "holes"), [labels, into], [.. shapes], []));

        // The stars, one for each hole, at the drawn size.
        var cut = (o.Piece.Settings ?? Layer.Default(2, LayerMode.Cut)) with { Id = Guid.NewGuid(), Name = InlayMaker.Prefix + (string.IsNullOrWhiteSpace(o.Piece.Name) ? "pieces" : o.Piece.Name.Trim() + " pieces"), Mode = LayerMode.Cut, ParentId = null, KerfMm = 0, Output = true, Visible = true };
        var stars = centres.Select((c, i) => (Shape)new PathShape { Name = $"Star {i + 1}", LayerId = cut.Id, Geometry = Grow(Star(c, size), o.Piece.KerfMm / 2) }).ToList();
        pages.Add(new DesignPage(Prefix + "stars", [cut], [.. stars], []));
        return (Pages.Insert(s, pages), [.. pages.Select(p => p.Name)]);
    }

    static PathGeometry Grow(PathGeometry g, double by) =>
        Math.Abs(by) < 1e-9 ? g : ShapeGeometry.Offset(g, by, JoinStyle.Sharp, EndStyle.Flat, miterLimit: 8, tolerance: 0.005);
}
