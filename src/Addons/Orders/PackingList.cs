using System.Globalization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using SkiaSharp;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// The packing list as a PDF: for each sheet, a map of where every piece is (numbered as on the
/// work area) and a table of whose each one is; then one slip per order to pack it by.
/// </summary>
public static class PackingList
{
    static readonly SKColor Ink = new(0x1F, 0x23, 0x29), Grey = new(0x6B, 0x72, 0x80), Rule = new(0xD5, 0xD9, 0xE0), Band = new(0xF3, 0xF4, 0xF6), Piece = new(0x3B, 0x82, 0xF6);
    const float M = 42;

    public static void Save(string path, IReadOnlyList<SheetMaker.FoundSheet> sheets, Func<string, OrderItem?> item, Rect2 bed, bool a4, DateTime now)
    {
        using var file = File.Create(path);
        using var doc = SKDocument.CreatePdf(file, new SKDocumentPdfMetadata { Title = "Packing list", Creator = AppIdentity.ProductName, Producer = AppIdentity.ProductName });
        Draw(doc, sheets, item, bed, a4, now);
        doc.Close();
    }

    /// <summary>The first page as a PNG (for checks and the help picture).</summary>
    public static byte[] FirstPagePng(IReadOnlyList<SheetMaker.FoundSheet> sheets, Func<string, OrderItem?> item, Rect2 bed, bool a4, DateTime now, float scale = 1)
    {
        var pages = new List<SKPicture>();
        var (w, h) = Size(a4);
        Draw(new Recorder(pages, w, h), sheets, item, bed, a4, now);
        using var surface = SKSurface.Create(new SKImageInfo((int)(w * scale), (int)(h * scale)));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(scale);
        if (pages.Count > 0) surface.Canvas.DrawPicture(pages[0]);
        foreach (var p in pages) p.Dispose();
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static (float W, float H) Size(bool a4) => a4 ? (595, 842) : (612, 792);

    /// <summary>Pages to draw on: a PDF, or pictures.</summary>
    interface IPages { SKCanvas Begin(); void End(); }

    sealed class Pdf(SKDocument doc, float w, float h) : IPages
    {
        public SKCanvas Begin() => doc.BeginPage(w, h);
        public void End() => doc.EndPage();
    }

    sealed class Recorder(List<SKPicture> into, float w, float h) : IPages
    {
        SKPictureRecorder? _rec;
        public SKCanvas Begin() { _rec = new SKPictureRecorder(); return _rec.BeginRecording(new SKRect(0, 0, w, h)); }
        public void End() { into.Add(_rec!.EndRecording()); _rec.Dispose(); _rec = null; }
    }

    static void Draw(SKDocument doc, IReadOnlyList<SheetMaker.FoundSheet> sheets, Func<string, OrderItem?> item, Rect2 bed, bool a4, DateTime now)
    {
        var (w, h) = Size(a4);
        Draw(new Pdf(doc, w, h), sheets, item, bed, a4, now);
    }

    static void Draw(IPages pages, IReadOnlyList<SheetMaker.FoundSheet> sheets, Func<string, OrderItem?> item, Rect2 bed, bool a4, DateTime now)
    {
        var (w, h) = Size(a4);
        using var title = Font(17, true);
        using var head = Font(11, true);
        using var body = Font(9.5f);
        using var bold = Font(9.5f, true);
        using var small = Font(8);
        using var rule = new SKPaint { Color = Rule, StrokeWidth = 0.75f, IsAntialias = true };
        using var band = new SKPaint { Color = Band, Style = SKPaintStyle.Fill };
        float right = w - M;

        SKCanvas c = null!;
        float y = 0;
        void NewPage() { c = pages.Begin(); y = M + 12; }
        void EndPage() => pages.End();
        bool Room(float need) => y + need <= h - M;

        // ---- each sheet: its map and whose each piece is
        foreach (var sheet in sheets)
        {
            NewPage();
            Text(c, sheet.Name, M, y + 6, title, Ink);
            Text(c, $"{sheet.Pieces.Count} piece{(sheet.Pieces.Count == 1 ? "" : "s")} · {now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", right, y + 6, small, Grey, SKTextAlign.Right);
            y += 22;
            y = Map(c, sheet, bed, M, y, right - M, Math.Min(300, (h - 2 * M) * 0.42f)) + 16;

            float[] col = [M + 4, M + 30, M + 108, M + 250, M + 420];
            void Header()
            {
                c.DrawRect(SKRect.Create(M, y, right - M, 18), band);
                Text(c, "#", col[0], y + 12.5f, bold, Ink);
                Text(c, "Order", col[1], y + 12.5f, bold, Ink);
                Text(c, "Item", col[2], y + 12.5f, bold, Ink);
                Text(c, "Personalization", col[3], y + 12.5f, bold, Ink);
                Text(c, "Ship to", col[4], y + 12.5f, bold, Ink);
                y += 18;
            }
            Header();
            foreach (var p in sheet.Pieces)
            {
                var it = item(p.Key);
                float rowH = 14 + 11 * Math.Max(1, Math.Min(4, (it?.Personalization ?? "").Split('\n').Length));
                if (!Room(rowH + 4)) { EndPage(); NewPage(); Header(); }
                float top = y + 13;
                Text(c, p.Number.ToString(CultureInfo.InvariantCulture), col[0], top, bold, Ink);
                if (it is null) { Text(c, "(no longer in the order list)", col[1], top, body, Grey); }
                else
                {
                    Text(c, Fit(it.Order, col[2] - col[1] - 6, body), col[1], top, body, Ink);
                    Text(c, Fit(it.Title + (it.Quantity > 1 ? $" ({p.PieceOfItem} of {it.Quantity})" : ""), col[3] - col[2] - 6, body), col[2], top, body, Ink);
                    if (it.OptionsText.Length > 0) Text(c, Fit(it.OptionsText, col[3] - col[2] - 6, small), col[2], top + 11, small, Grey);
                    float py = top;
                    foreach (var line in it.Personalization.Replace("\r", "").Split('\n').Take(4))
                    {
                        Text(c, Fit(line, col[4] - col[3] - 6, body), col[3], py, body, Ink);
                        py += 11;
                    }
                    Text(c, Fit(it.ShipName.Length > 0 ? it.ShipName : it.Buyer, right - col[4], body), col[4], top, body, Ink);
                }
                y += rowH;
                c.DrawLine(M, y, right, y, rule);
            }
            EndPage();
        }

        // ---- one slip per order
        var orders = sheets.SelectMany(s => s.Pieces.Select(p => (Sheet: s.Name, Piece: p, Item: item(p.Key))))
            .Where(x => x.Item is not null)
            .GroupBy(x => (x.Item!.Source, x.Item.Order))
            .OrderBy(g => g.Min(x => x.Item!.Date ?? DateTime.MaxValue)).ThenBy(g => g.Key.Order, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orders.Count == 0) return;
        NewPage();
        Text(c, "Packing slips", M, y + 6, title, Ink);
        y += 26;
        foreach (var o in orders)
        {
            var first = o.First().Item!;
            var lines = o.GroupBy(x => x.Item!.Key).Select(g => (Item: g.First().Item!, Pieces: g.Select(x => x.Piece).ToList(), g.First().Sheet)).ToList();
            var address = (first.ShipName.Length > 0 ? first.ShipName : first.Buyer) + (first.ShipAddress.Length > 0 ? "\n" + first.ShipAddress : "");
            float need = 30 + Math.Max(address.Split('\n').Length * 11, lines.Sum(l => 13 + 11 * (l.Item.Personalization.Length > 0 ? 1 : 0) + (l.Item.OptionsText.Length > 0 ? 11 : 0))) + (first.Note.Length > 0 ? 14 : 0);
            if (!Room(need)) { EndPage(); NewPage(); }
            float top = y;
            using (var box = new SKPaint { Color = Rule, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f, IsAntialias = true })
                c.DrawRect(SKRect.Create(M, top, right - M, need - 6), box);
            Text(c, $"Order {first.Order}", M + 8, top + 16, head, Ink);
            Text(c, $"{first.Source}{(first.Date is { } d ? " · " + d.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "")}", right - 8, top + 16, small, Grey, SKTextAlign.Right);
            float ay = top + 32;
            foreach (var line in address.Split('\n')) { Text(c, Fit(line, 170, body), M + 8, ay, body, Ink); ay += 11; }
            float ly = top + 32;
            float lx = M + 190;
            foreach (var l in lines)
            {
                var nums = string.Join(", ", l.Pieces.Select(p => "#" + p.Number));
                using (var tick = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f, IsAntialias = true })
                    c.DrawRect(SKRect.Create(lx, ly - 7.5f, 8, 8), tick);
                Text(c, Fit($"{l.Item.Quantity} × {l.Item.Title}", right - lx - 110, bold), lx + 14, ly, bold, Ink);
                Text(c, Fit($"{l.Sheet}: {nums}", 100, small), right - 8, ly, small, Grey, SKTextAlign.Right);
                ly += 11;
                if (l.Item.OptionsText.Length > 0) { Text(c, Fit(l.Item.OptionsText, right - lx - 22, small), lx + 14, ly, small, Grey); ly += 11; }
                if (l.Item.Personalization.Length > 0) { Text(c, Fit("“" + l.Item.Personalization.Replace("\r", "").Replace("\n", " / ") + "”", right - lx - 22, body), lx + 14, ly, body, Ink); ly += 11; }
                ly += 2;
            }
            if (first.Note.Length > 0) Text(c, Fit("Note: " + first.Note.Replace("\n", " "), right - M - 16, small), M + 8, Math.Max(ay, ly) + 4, small, Grey);
            y = top + need;
        }
        EndPage();
    }

    /// <summary>The sheet seen from above: the bed, each piece's outline, and its number. Returns the y under it.</summary>
    static float Map(SKCanvas c, SheetMaker.FoundSheet sheet, Rect2 bed, float x, float y, float maxW, float maxH)
    {
        var area = sheet.Pieces.Aggregate(bed, (a, p) => p.Box.IsEmpty ? a : a.Union(p.Box));
        float k = (float)Math.Min(maxW / area.Width, maxH / area.Height);
        float ox = x + (maxW - (float)area.Width * k) / 2;
        using var bedPaint = new SKPaint { Color = Rule, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f, IsAntialias = true };
        using var bedFill = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFB), Style = SKPaintStyle.Fill };
        using var line = new SKPaint { Color = Piece, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, IsAntialias = true };
        using var dot = new SKPaint { Color = Ink, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var num = Font(7.5f, true);
        SKPoint P(Vec2 v) => new(ox + (float)(v.X - area.MinX) * k, y + (float)(v.Y - area.MinY) * k);
        var bedRect = new SKRect(P(bed.TopLeft).X, P(bed.TopLeft).Y, P(bed.BottomRight).X, P(bed.BottomRight).Y);
        c.DrawRect(bedRect, bedFill);
        c.DrawRect(bedRect, bedPaint);
        foreach (var p in sheet.Pieces)
        {
            using var path = new SKPath();
            foreach (var shape in p.Outlines)
                foreach (var f in shape.World.Flatten(0.2))
                {
                    if (f.Points.Count < 2) continue;
                    path.MoveTo(P(f.Points[0]));
                    for (int i = 1; i < f.Points.Count; i++) path.LineTo(P(f.Points[i]));
                    if (f.Closed) path.Close();
                }
            c.DrawPath(path, line);
            if (p.Box.IsEmpty) continue;
            // In the piece's corner, so it doesn't hide the words.
            float r = Math.Max(5.5f, num.MeasureText(p.Number.ToString(CultureInfo.InvariantCulture)) / 2 + 3);
            var corner = P(p.Box.TopLeft);
            var centre = new SKPoint(corner.X + Math.Min(r + 1, (float)p.Box.Width * k / 2), corner.Y + Math.Min(r + 1, (float)p.Box.Height * k / 2));
            c.DrawCircle(centre, r, dot);
            Text(c, p.Number.ToString(CultureInfo.InvariantCulture), centre.X, centre.Y + 2.7f, num, SKColors.White, SKTextAlign.Center);
        }
        return y + (float)area.Height * k;
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

    /// <summary>The text cut short with "…" to fit a width.</summary>
    static string Fit(string text, float width, SKPaint font)
    {
        text = text.Replace("\r", "").Replace('\n', ' ').Trim();
        if (font.MeasureText(text) <= width) return text;
        while (text.Length > 1 && font.MeasureText(text + "…") > width) text = text[..^1];
        return text.TrimEnd() + "…";
    }
}
