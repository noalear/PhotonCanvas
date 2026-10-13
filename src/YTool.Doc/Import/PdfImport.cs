using System.Collections.Immutable;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using YTool.Core;

namespace YTool.Doc.Import;

/// <summary>
/// PDF (and PDF-compatible Adobe Illustrator .ai) → millimetre geometry, using the vector
/// paths drawn on one page. Points are converted to millimetres (1 pt = 25.4/72 mm) and the
/// page is turned right side up (PDF's Y axis points up, ours points down), so the page's
/// top-left corner becomes (0, 0).
/// <para>
/// Each path takes its stroke colour, or its fill colour when it is only filled.
/// Text is not converted (PDFs hold letters, not outlines) and is reported as a warning.
/// Pictures are imported when they are stored as JPEG or can be converted to PNG.
/// </para>
/// </summary>
public static class PdfImport
{
    const double PtMm = 25.4 / 72;

    /// <summary>Read the vector paths of one page of a PDF file.</summary>
    /// <param name="path">The PDF file.</param>
    /// <param name="page">Which page, counting from 1.</param>
    /// <exception cref="FormatException">The file isn't a readable PDF, or the page doesn't exist.</exception>
    public static ImportResult Load(string path, int page = 1) => Load(File.ReadAllBytes(path), page);

    /// <summary>Read the vector paths of one page of a PDF held in memory.</summary>
    /// <exception cref="FormatException">The data isn't a readable PDF, or the page doesn't exist.</exception>
    public static ImportResult Load(byte[] data, int page = 1)
    {
        PdfDocument doc;
        try { doc = PdfDocument.Open(data, new ParsingOptions { ClipPaths = false }); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException($"This PDF can't be read ({ex.Message}).", ex);
        }
        using (doc)
        {
            return ReadPage(doc, page);
        }
    }

    /// <summary>Number of pages in a PDF file (for offering a page choice).</summary>
    /// <exception cref="FormatException">The file isn't a readable PDF.</exception>
    public static int PageCount(string path)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            return doc.NumberOfPages;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not IOException)
        {
            throw new FormatException($"This PDF can't be read ({ex.Message}).", ex);
        }
    }

    /// <summary>
    /// Read an Adobe Illustrator file. Illustrator 9 and later save a PDF copy of the
    /// artwork inside the .ai file ("Create PDF Compatible File"), which is what is read.
    /// </summary>
    /// <exception cref="FormatException">The file was saved without PDF compatibility, or is damaged.</exception>
    public static ImportResult LoadIllustrator(string path, int page = 1)
    {
        var data = File.ReadAllBytes(path);
        var notPdf = new FormatException(
            "This Illustrator file wasn't saved with PDF compatibility, so it can't be read. In Illustrator, " +
            "use Save As → Illustrator with \"Create PDF Compatible File\" ticked, or export it as SVG or PDF.");
        if (data.AsSpan(0, Math.Min(data.Length, 1024)).IndexOf("%PDF"u8) < 0) throw notPdf;
        try { return Load(data, page); }
        catch (FormatException ex) { throw new FormatException(notPdf.Message, ex); }
    }

    static ImportResult ReadPage(PdfDocument doc, int pageNumber)
    {
        int count = doc.NumberOfPages;
        if (count == 0) throw new FormatException("This PDF has no pages.");
        if (pageNumber < 1 || pageNumber > count)
            throw new FormatException($"This PDF has {count} page(s); there is no page {pageNumber}.");

        Page page;
        try { page = doc.GetPage(pageNumber); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException($"Page {pageNumber} of this PDF can't be read ({ex.Message}).", ex);
        }

        var warnings = new List<string>();
        // Content coordinates are in page space with the visible page's bottom-left at (0, 0);
        // PdfPig has already applied the page rotation, and Height is the visible height.
        double pageH = page.Height;
        var toMm = Affine.Scale(PtMm, -PtMm).Then(Affine.Translate(0, pageH * PtMm));
        Vec2 Mm(PdfPoint p) => toMm.Apply(new(p.X, p.Y));

        var paths = new List<ImportedPath>();
        foreach (var pdfPath in page.Paths)
        {
            if (pdfPath.IsClipping) continue;
            if (!pdfPath.IsStroked && !pdfPath.IsFilled) continue; // invisible construction path
            var subs = new List<Subpath>();
            foreach (var sp in pdfPath)
                if (Convert(sp, Mm) is { } s) subs.Add(s);
            if (subs.Count == 0) continue;
            uint? color = pdfPath.IsStroked ? Rgb(pdfPath.StrokeColor) : Rgb(pdfPath.FillColor);
            paths.Add(new ImportedPath(new PathGeometry(subs), color ?? 0x000000, pdfPath.IsStroked ? "stroke" : "fill"));
        }

        if (page.Letters.Count > 0)
            warnings.Add("Text was skipped; convert text to outlines in your design app first.");

        var images = new List<ImportedImage>();
        int skippedImages = 0;
        foreach (var img in page.GetImages())
        {
            if (ImageBytes(img) is not { } bytes) { skippedImages++; continue; }
            var box = img.BoundingBox;
            Vec2 tl = Mm(box.TopLeft), tr = Mm(box.TopRight), bl = Mm(box.BottomLeft);
            double w = tl.Dist(tr), h = tl.Dist(bl);
            if (w <= 0 || h <= 0) continue;
            // Local (0,0)-(w,h) → the three placed corners.
            var ux = (tr - tl) / w;
            var uy = (bl - tl) / h;
            var t = new Affine(ux.X, ux.Y, uy.X, uy.Y, tl.X, tl.Y);
            images.Add(new ImportedImage(bytes, new Vec2(w, h), t, "image", null));
        }
        if (skippedImages > 0)
            warnings.Add($"{skippedImages} picture(s) in the PDF use a format that can't be extracted and were skipped.");

        if (paths.Count == 0 && images.Count == 0 && warnings.Count == 0)
            warnings.Add($"Page {pageNumber} contains no vector shapes.");
        return new ImportResult(paths, images, warnings);
    }

    static Subpath? Convert(PdfSubpath sp, Func<PdfPoint, Vec2> mm)
    {
        Vec2? start = null;
        var segs = ImmutableArray.CreateBuilder<Seg>();
        bool closed = false;
        Vec2 cur = default;
        foreach (var cmd in sp.Commands)
        {
            switch (cmd)
            {
                case PdfSubpath.Move m:
                    if (start is null) { start = mm(m.Location); cur = start.Value; }
                    break;
                case PdfSubpath.Line l:
                    if (start is null) { start = mm(l.From); cur = start.Value; }
                    cur = mm(l.To);
                    segs.Add(Seg.Line(cur));
                    break;
                case PdfSubpath.CubicBezierCurve c:
                    if (start is null) { start = mm(c.StartPoint); cur = start.Value; }
                    cur = mm(c.EndPoint);
                    segs.Add(Seg.Cubic(mm(c.FirstControlPoint), mm(c.SecondControlPoint), cur));
                    break;
                case PdfSubpath.QuadraticBezierCurve q:
                {
                    if (start is null) { start = mm(q.StartPoint); cur = start.Value; }
                    Vec2 p0 = cur, ctl = mm(q.ControlPoint), p2 = mm(q.EndPoint);
                    segs.Add(Seg.Cubic(p0 + (ctl - p0) * (2.0 / 3), p2 + (ctl - p2) * (2.0 / 3), p2));
                    cur = p2;
                    break;
                }
                case PdfSubpath.Close:
                    closed = true;
                    if (start is { } s0 && cur.Dist(s0) > Polyline.SamePointMm) segs.Add(Seg.Line(s0));
                    cur = start ?? cur;
                    break;
            }
        }
        if (start is null || segs.Count == 0) return null;
        return new Subpath(start.Value, segs.ToImmutable(), closed);
    }

    static uint? Rgb(IColor? c)
    {
        if (c is null) return null;
        try
        {
            var (r, g, b) = c.ToRGBValues();
            uint B(double v) => (uint)Math.Clamp(Math.Round(v * 255), 0, 255);
            return B(r) << 16 | B(g) << 8 | B(b);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // patterns and shadings have no single colour
        }
    }

    static byte[]? ImageBytes(IPdfImage img)
    {
        try
        {
            var raw = img.RawMemory.Span;
            if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF) return raw.ToArray(); // JPEG as stored
            return img.TryGetPng(out var png) ? png : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
