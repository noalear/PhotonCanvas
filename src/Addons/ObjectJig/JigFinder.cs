using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>Where the jig was found: the move taking the objects page onto it, and how sure that is.</summary>
/// <param name="OnEdge">Fraction of the jig's drawn edges lying on edges in the photo (0–1), after the fine fit.</param>
/// <param name="CornerClear">Fraction of the cut-off corner that's clear bed in the photo (1 = plainly the right way round).</param>
/// <param name="Found">The jig's outline as the camera saw it (bed mm), for the preview.</param>
public sealed record JigFound(Affine Move, double OnEdge, double CornerClear, bool Refined, PathGeometry Found, IReadOnlyList<string> Warnings)
{
    /// <summary>How far the jig's middle moved (mm) and how far it turned (degrees), for the message.</summary>
    public (double Mm, double Deg) Change(Vec2 centre) => (Move.Apply(centre).Dist(centre), Wrap(Move.RotationDeg));

    static double Wrap(double d) { d %= 360; return d > 180 ? d - 360 : d <= -180 ? d + 360 : d; }
}

/// <summary>
/// Finds a jig lying anywhere on the bed, at any angle, in a straightened top-down photo: the biggest
/// thing that isn't the bed is the jig; its area's middle and long side give a first guess (both ways
/// round, and the quarter turns too for a squarish jig); the guess whose cut-off corner is clear and
/// whose shape covers the jig best wins; then the drawn edges (outline and pockets) are fitted to the
/// photo's edges to well under a millimetre. Only reads the photo.
/// </summary>
public static class JigFinder
{
    /// <summary>The photo as greyscale (for the edge fit).</summary>
    public static GrayImage Gray(RasterImage img)
    {
        var px = img.Rgba;
        var g = new byte[img.Width * img.Height];
        for (int i = 0; i < g.Length; i++)
            g[i] = (byte)Math.Clamp((int)Math.Round(0.299 * px[i * 4] + 0.587 * px[i * 4 + 1] + 0.114 * px[i * 4 + 2]), 0, 255);
        return new GrayImage(img.Width, img.Height, g);
    }

    /// <summary>A keep mask of what isn't the bed (over 127 = something on it).</summary>
    public static byte[] NotBed(RasterImage photo, double tolerance) =>
        BackgroundRemoval.Mask(photo, new BackgroundOptions { Tolerance = tolerance, FromEdges = true, Shrink = 1, Feather = 0 });

    /// <exception cref="InvalidOperationException">Nothing like the jig is in the photo.</exception>
    public static JigFound Find(RasterImage photo, Rect2 area, double mmPerPx, JigShapes jig, double tolerance = 18, CancellationToken ct = default)
    {
        var keep = NotBed(photo, tolerance);
        return Find(keep, Gray(photo), area, mmPerPx, jig, ct);
    }

    /// <exception cref="InvalidOperationException">Nothing like the jig is in the photo.</exception>
    public static JigFound Find(byte[] keep, GrayImage gray, Rect2 area, double mmPerPx, JigShapes jig, CancellationToken ct = default)
    {
        var outer = JigMaker.Outers(jig.Outline);
        if (outer.Count == 0) throw new InvalidOperationException("The jig's outline is missing from this page.");
        var jigOuter = outer[0];
        double jigArea = ShapeGeometry.Area(jigOuter);
        var blobs = JigMaker.FromMask(keep, gray.Width, gray.Height, area, mmPerPx, Math.Max(50, jigArea * 0.25));
        ct.ThrowIfCancellationRequested();
        if (blobs.Count == 0)
            throw new InvalidOperationException("No jig in the photo. Check it's on the bed, inside the camera's view, and stands out from the bed (try another tolerance).");
        var blob = blobs[0];
        var warnings = new List<string>();
        double ratio = ShapeGeometry.Area(blob) / jigArea;
        if (ratio < 0.75 || ratio > 1.35)
            warnings.Add(ratio < 0.75
                ? "What the camera sees is smaller than the jig: part of it may be off the photo or too like the bed. Check the green outline."
                : "What the camera sees is bigger than the jig: something touches it, or the bed shows up too. Move things away from it, or try another tolerance.");

        var blobPoly = blob.Flatten(0.2)[0].Points;
        var jigPoly = jigOuter.Flatten(0.2)[0].Points;
        var (cj, aj, ej) = JigMaker.Axis(jigOuter);
        var (cb, ab, _) = JigMaker.Axis(blob);

        // First guesses: the long sides lined up, either way round (and the quarter turns for a squarish jig).
        var turns = new List<double> { ab - aj, ab - aj + 180 };
        if (ej < 1.2) { turns.Add(ab - aj + 90); turns.Add(ab - aj - 90); }
        var cornerPts = Samples(jig.Corner, 0.5);
        var jb = jigOuter.Bounds(0.1);
        double step = Math.Max(1, Math.Max(jb.Width, jb.Height) / 70);
        var covered = Grid(jb.Inflate(step * 4), step).Select(p => (P: p, In: Geom.Inside(jigPoly, p))).ToList();
        (Affine Move, double Score, double Corner) best = (Affine.Identity, double.NegativeInfinity, 0);
        foreach (var t in turns)
        {
            var move = Affine.RotateAbout(cj, t).Then(Affine.Translate(cb - cj));
            int both = 0, either = 0;
            foreach (var (p, inJig) in covered)
            {
                bool inBlob = Geom.Inside(blobPoly, move.Apply(p));
                if (inJig && inBlob) both++;
                if (inJig || inBlob) either++;
            }
            double iou = either == 0 ? 0 : both / (double)either;
            double corner = cornerPts.Count == 0 ? 0.5 : cornerPts.Count(p => !Geom.Inside(blobPoly, move.Apply(p))) / (double)cornerPts.Count;
            double score = iou + 0.6 * corner;
            if (score > best.Score) best = (move, score, corner);
        }
        ct.ThrowIfCancellationRequested();

        // Fine fit on the drawn edges (outline and pockets).
        var loops = new List<IReadOnlyList<Vec2>>();
        foreach (var f in new PathGeometry([.. jig.Outline.Subpaths, .. jig.Pockets.Subpaths]).Flatten(0.05))
            if (f.Points.Count > 1) loops.Add(f.Closed ? [.. f.Points.Select(best.Move.Apply), best.Move.Apply(f.Points[0])] : [.. f.Points.Select(best.Move.Apply)]);
        var final = best.Move;
        double onEdge = 0;
        bool refined = false;
        try
        {
            var fit = ShapeFit.Fit(gray, area, mmPerPx, loops, new FitOptions { MaxRotationDeg = 6, MaxScaleChange = 0, SearchMm = 6 }, ct);
            onEdge = fit.OnEdge;
            if (fit.OnEdge >= 0.35)
            {
                final = best.Move.Then(Affine.ScaleAbout(fit.Center, fit.Scale, fit.Scale)).Then(Affine.RotateAbout(fit.Center, fit.AngleDeg)).Then(Affine.Translate(fit.Offset));
                refined = true;
            }
        }
        catch (ArgumentException) { }
        if (!refined)
            warnings.Add("The jig's edges didn't show clearly, so it's placed from its shape alone (to within a millimetre or two). More light, or a bed of another shade under it, helps.");
        if (best.Corner < 0.5 && jig.Corner is { IsEmpty: false })
            warnings.Add("The cut-off corner didn't show clearly: check from the green outline that the jig was found the right way round.");
        return new JigFound(final, onEdge, best.Corner, refined, blob, warnings);
    }

    static List<Vec2> Grid(Rect2 r, double step)
    {
        var o = new List<Vec2>();
        for (double y = r.MinY + step / 2; y < r.MaxY; y += step)
            for (double x = r.MinX + step / 2; x < r.MaxX; x += step) o.Add(new(x, y));
        return o;
    }

    /// <summary>Points inside a shape, about <paramref name="stepMm"/> apart (none for an empty shape).</summary>
    static List<Vec2> Samples(PathGeometry g, double stepMm)
    {
        if (g.IsEmpty) return [];
        var polys = g.Flatten(0.1).Where(f => f.Points.Count > 2).Select(f => f.Points).ToList();
        var b = g.Bounds(0.1);
        // Keep away from the edges: the cut edge itself is neither bed nor jig in a photo.
        return [.. Grid(b, stepMm).Where(p => polys.Any(poly => Geom.Inside(poly, p)) && polys.All(poly => Edge(poly, p) > 1))];
    }

    static double Edge(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        double d = double.PositiveInfinity;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) d = Math.Min(d, Geom.SegmentDistance(p, poly[j], poly[i]));
        return d;
    }
}
