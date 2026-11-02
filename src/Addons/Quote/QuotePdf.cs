using System.Globalization;
using PhotonCanvas.Core;
using SkiaSharp;

namespace PhotonCanvas.Addon.Quote;

/// <summary>What goes on the printed quote besides the prices.</summary>
public sealed record QuotePaper(string Title, int Number, DateTime Date, byte[]? Picture);

/// <summary>The quote as a one-page PDF (US Letter or A4).</summary>
public static class QuotePdf
{
    static readonly SKColor Ink = new(0x1F, 0x23, 0x29), Grey = new(0x6B, 0x72, 0x80), Rule = new(0xD5, 0xD9, 0xE0), Band = new(0xF3, 0xF4, 0xF6);

    public static (float W, float H) PageSize(QuoteRates r) => r.Letter ? (612, 792) : (595, 842);

    public static void Save(string path, QuoteResult q, QuoteJob job, QuoteRates r, QuotePaper paper)
    {
        var (w, h) = PageSize(r);
        using var file = File.Create(path);
        using var doc = SKDocument.CreatePdf(file, new SKDocumentPdfMetadata
        {
            Title = $"Quote {QuoteMath.NumberText(paper.Number)}",
            Creator = AppIdentity.ProductName,
            Producer = AppIdentity.ProductName,
        });
        Draw(doc.BeginPage(w, h), w, h, q, job, r, paper);
        doc.EndPage();
        doc.Close();
    }

    /// <summary>The page as a PNG, <paramref name="scale"/> pixels a point (for checks and the help picture).</summary>
    public static byte[] RenderPng(QuoteResult q, QuoteJob job, QuoteRates r, QuotePaper paper, float scale = 1)
    {
        var (w, h) = PageSize(r);
        using var surface = SKSurface.Create(new SKImageInfo((int)(w * scale), (int)(h * scale)));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(scale);
        Draw(surface.Canvas, w, h, q, job, r, paper);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static void Draw(SKCanvas c, float w, float h, QuoteResult q, QuoteJob job, QuoteRates r, QuotePaper paper)
    {
        const float m = 56;
        float right = w - m, y = m + 14;
        using var big = Font(22, true);
        using var head = Font(14, true);
        using var body = Font(10.5f);
        using var bold = Font(10.5f, true);
        using var small = Font(9);

        // Who it's from (left) and the quote's number and date (right).
        float left = y;
        if (r.Business.Trim().Length > 0) { Text(c, r.Business.Trim(), m, left, head, Ink); left += 18; }
        foreach (var line in r.Contact.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0))
        {
            left = Wrap(c, line.Trim(), m, left, (right - m) * 0.55f, body, Grey);
        }
        Text(c, "QUOTE", right, y + 4, big, Ink, SKTextAlign.Right);
        Text(c, QuoteMath.NumberText(paper.Number), right, y + 22, body, Grey, SKTextAlign.Right);
        Text(c, paper.Date.ToString("D", CultureInfo.CurrentCulture), right, y + 36, body, Grey, SKTextAlign.Right);
        y = Math.Max(left, y + 36) + 26;

        if (job.Customer.Trim().Length > 0)
        {
            Text(c, "FOR", m, y, small, Grey);
            y = Wrap(c, job.Customer.Trim(), m, y + 14, right - m, bold, Ink) + 10;
        }

        // A picture of the design.
        if (paper.Picture is { } png && SKBitmap.Decode(png) is { } bmp)
            using (bmp)
            {
                const float pad = 8;
                float maxW = right - m - 2 * pad, maxH = 180;
                float k = Math.Min(1, Math.Min(maxW / bmp.Width, maxH / bmp.Height));
                var box = SKRect.Create(m + pad, y + pad, bmp.Width * k, bmp.Height * k);
                using var frame = new SKPaint { Color = Rule, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f, IsAntialias = true };
                using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                c.DrawBitmap(bmp, box, paint);
                c.DrawRect(SKRect.Inflate(box, pad, pad), frame);
                y += box.Height + 2 * pad + 22;
            }

        // The lines.
        using var band = new SKPaint { Color = Band, Style = SKPaintStyle.Fill };
        using var rule = new SKPaint { Color = Rule, StrokeWidth = 0.75f, IsAntialias = true };
        c.DrawRect(SKRect.Create(m, y, right - m, 20), band);
        Text(c, "Item", m + 8, y + 14, bold, Ink);
        Text(c, "Amount", right - 8, y + 14, bold, Ink, SKTextAlign.Right);
        y += 20;
        foreach (var l in q.Lines)
        {
            float top = y;
            y = Wrap(c, l.Name, m + 8, y + 16, (right - m) * 0.62f, body, Ink);
            if (l.Detail.Length > 0) y = Wrap(c, l.Detail, m + 8, y + 1, (right - m) * 0.62f, small, Grey);
            Text(c, QuoteMath.Format(l.Amount, r.Currency), right - 8, top + 16, body, Ink, SKTextAlign.Right);
            y += 6;
            c.DrawLine(m, y, right, y, rule);
        }

        // Totals.
        y += 18;
        float labelX = right - 190;
        if (q.Tax > 0)
        {
            Text(c, "Subtotal", labelX, y, body, Grey);
            Text(c, QuoteMath.Format(q.Subtotal, r.Currency), right - 8, y, body, Ink, SKTextAlign.Right);
            y += 16;
            Text(c, QuoteMath.TaxLabel(r), labelX, y, body, Grey);
            Text(c, QuoteMath.Format(q.Tax, r.Currency), right - 8, y, body, Ink, SKTextAlign.Right);
            y += 10;
            c.DrawLine(labelX, y, right, y, rule);
            y += 18;
        }
        Text(c, "Total", labelX, y, head, Ink);
        Text(c, QuoteMath.Format(q.Total, r.Currency), right - 8, y, head, Ink, SKTextAlign.Right);
        y += 18;
        if (q.Pieces > 1) Text(c, $"{QuoteMath.Format(q.PerPiece, r.Currency)} a piece for {q.Pieces} pieces", right - 8, y, small, Grey, SKTextAlign.Right);

        // The note at the foot of the page.
        if (r.Note.Trim().Length > 0)
        {
            float fy = h - m - 14 * Math.Max(1, r.Note.Split('\n').Length);
            c.DrawLine(m, fy - 16, right, fy - 16, rule);
            foreach (var line in r.Note.Replace("\r", "").Split('\n'))
                fy = Wrap(c, line.Trim(), m, fy, right - m, small, Grey);
        }
    }

    static SKPaint Font(float size, bool bold = false) => new()
    {
        IsAntialias = true,
        TextSize = size,
        Typeface = SKTypeface.FromFamilyName("Arial", bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
    };

    static void Text(SKCanvas c, string text, float x, float y, SKPaint font, SKColor colour, SKTextAlign align = SKTextAlign.Left)
    {
        font.Color = colour;
        font.TextAlign = align;
        c.DrawText(text, x, y, font);
    }

    /// <summary>Text wrapped to a width, from the baseline at <paramref name="y"/> down; returns the baseline after the last line.</summary>
    static float Wrap(SKCanvas c, string text, float x, float y, float width, SKPaint font, SKColor colour)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(tryLine) > width && line.Length > 0)
            {
                Text(c, line, x, y, font, colour);
                y += font.TextSize * 1.35f;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) { Text(c, line, x, y, font, colour); y += font.TextSize * 1.35f; }
        return y;
    }
}
