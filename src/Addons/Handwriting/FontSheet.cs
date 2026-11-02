using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Tracing;
using SkiaSharp;

namespace PhotonCanvas.Addon.Handwriting;

public enum Paper { A4, Letter }

/// <summary>
/// The sheet people write their letters on: a box per character with guide lines, and a black
/// square in each corner (the top-left one hollow) so a photo of it, taken at any angle or turned
/// any way round, can be straightened and each box read. One layout fits A4 and US Letter.
/// All sizes in mm from the top-left corner of the top-left square.
/// </summary>
public static class FontSheet
{
    public const double Width = 186, Height = 255;
    public const double Marker = 8, MarkerHole = 3.6;
    public const int Columns = 11;
    public const double CellW = 16, LabelH = 4, BoxH = 22, RowGap = 1, GridLeft = 5, GridTop = 17;
    /// <summary>Guide lines inside a box, from its top.</summary>
    public const double CapLine = 4, XLine = 8.8, Baseline = 16, DescLine = 20.5;
    /// <summary>Capital height on the sheet: one font unit.</summary>
    public const double CapHeight = Baseline - CapLine;

    /// <summary>The characters, in the order of the boxes.</summary>
    public static readonly string[] Characters =
    [
        .. "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()),
        .. "abcdefghijklmnopqrstuvwxyz".Select(c => c.ToString()),
        .. "0123456789".Select(c => c.ToString()),
        ".", ",", "!", "?", "'", "\"", "-", ":", ";", "(", ")", "&", "/", "@", "#", "+", "=", "$", "%", "*",
        "°", "€", "£", "<", ">", "_",
    ];

    /// <summary>Centres of the corner squares: top-left, top-right, bottom-right, bottom-left.</summary>
    public static readonly Vec2[] MarkerCentres =
    [
        new(Marker / 2, Marker / 2), new(Width - Marker / 2, Marker / 2),
        new(Width - Marker / 2, Height - Marker / 2), new(Marker / 2, Height - Marker / 2),
    ];

    public static int Rows => (Characters.Length + Columns - 1) / Columns;

    /// <summary>The writing box of character <paramref name="i"/>.</summary>
    public static Rect2 Box(int i)
    {
        int col = i % Columns, row = i / Columns;
        double x = GridLeft + col * CellW, y = GridTop + row * (LabelH + BoxH + RowGap) + LabelH;
        return new Rect2(x, y, x + CellW, y + BoxH);
    }

    static readonly SKColor Guide = new(0x9C, 0xC2, 0xEE), GuideDark = new(0x74, 0xA6, 0xE2), LabelInk = new(0x50, 0x50, 0x50);

    /// <summary>Draws the sheet in mm (scale the canvas first).</summary>
    public static void Draw(SKCanvas c, string? fontName = null)
    {
        using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var m in MarkerCentres)
            c.DrawRect((float)(m.X - Marker / 2), (float)(m.Y - Marker / 2), (float)Marker, (float)Marker, black);
        var tl = MarkerCentres[0];
        c.DrawRect((float)(tl.X - MarkerHole / 2), (float)(tl.Y - MarkerHole / 2), (float)MarkerHole, (float)MarkerHole, white);

        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        // Sizes are mm: no hinting or whole-pixel spacing, which would squash letters this small.
        static SKFont Font(SKTypeface t, float size) => new(t, size) { Subpixel = true, LinearMetrics = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
        using var bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? typeface;
        using var title = Font(bold, 4.6f);
        using var small = Font(typeface, 2.6f);
        using var label = Font(typeface, 3.0f);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var grey = new SKPaint { Color = LabelInk, IsAntialias = true };
        c.DrawText("Handwriting font sheet" + (string.IsNullOrWhiteSpace(fontName) ? "" : $": {fontName}"), 12, 5.6f, title, ink);
        c.DrawText("Write each character once in its box, standing on the solid line. Capitals and tall letters reach the top line,", 12, 10.6f, small, grey);
        c.DrawText("small letters the dashed line; tails (g, j, p, q, y) hang below. Print-style letters, not joined up.", 12, 14.0f, small, grey);

        using var line = new SKPaint { Color = Guide, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.22f };
        using var baseLine = new SKPaint { Color = GuideDark, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.3f };
        using var dashed = new SKPaint { Color = Guide, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.2f, PathEffect = SKPathEffect.CreateDash([1.2f, 1.0f], 0) };
        using var dotted = new SKPaint { Color = Guide, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.18f, PathEffect = SKPathEffect.CreateDash([0.4f, 1.0f], 0) };
        for (int i = 0; i < Characters.Length; i++)
        {
            var b = Box(i);
            float x0 = (float)b.MinX, x1 = (float)b.MaxX, y0 = (float)b.MinY;
            c.DrawText(Characters[i], x0 + 0.8f, y0 - 1.0f, label, grey);
            c.DrawRect(x0, y0, (float)CellW, (float)BoxH, line);
            c.DrawLine(x0, y0 + (float)CapLine, x1, y0 + (float)CapLine, line);
            c.DrawLine(x0, y0 + (float)XLine, x1, y0 + (float)XLine, dashed);
            c.DrawLine(x0, y0 + (float)Baseline, x1, y0 + (float)Baseline, baseLine);
            c.DrawLine(x0, y0 + (float)DescLine, x1, y0 + (float)DescLine, dotted);
        }
        float fy = (float)(GridTop + Rows * (LabelH + BoxH + RowGap) + 3);
        c.DrawText("Use a dark pen (a black felt-tip is best). Print at actual size (100%, not \"fit to page\").", 12, fy, small, grey);
        c.DrawText("Photograph the whole sheet flat and evenly lit, all four black squares in the picture. Fill in the same sheet", 12, fy + 3.4f, small, grey);
        c.DrawText("again and read it into the same font to give each letter a second version: words then look less regular.", 12, fy + 6.8f, small, grey);
    }

    public static (double W, double H) PaperSize(Paper p) => p == Paper.Letter ? (215.9, 279.4) : (210, 297);

    /// <summary>The sheet as a one-page PDF, centred on the paper.</summary>
    public static byte[] Pdf(Paper paper, string? fontName = null)
    {
        const float PtPerMm = 72f / 25.4f;
        var (pw, ph) = PaperSize(paper);
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata { Title = "Handwriting font sheet", Creator = "PhotonCanvas", RasterDpi = 300 }))
        {
            var c = doc.BeginPage((float)(pw * PtPerMm), (float)(ph * PtPerMm));
            c.Scale(PtPerMm);
            c.Translate((float)((pw - Width) / 2), (float)((ph - Height) / 2));
            Draw(c, fontName);
            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }

    /// <summary>The sheet as a picture (for previews, tests and printing without PDF).</summary>
    public static SKBitmap Picture(double pxPerMm, double marginMm = 6, string? fontName = null)
    {
        int w = (int)Math.Ceiling((Width + 2 * marginMm) * pxPerMm), h = (int)Math.Ceiling((Height + 2 * marginMm) * pxPerMm);
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        c.Scale((float)pxPerMm);
        c.Translate((float)marginMm, (float)marginMm);
        Draw(c, fontName);
        return bmp;
    }
}

/// <summary>How a filled-in sheet is read.</summary>
/// <param name="InkLevel">A pixel is ink when darker than this share of the paper's brightness around it (0.3–0.85).</param>
/// <param name="SmoothMm">How much the traced lines are straightened out (mm on the sheet).</param>
public sealed record SheetOptions(double InkLevel = 0.62, double SmoothMm = 0.18);

/// <summary>One box of a read sheet.</summary>
/// <param name="Glyph">The letter as single lines, null when the box is empty.</param>
/// <param name="Gray">What the box looks like in the photo (8-bit grey, <paramref name="W"/> × <paramref name="H"/>), for checking.</param>
/// <param name="Left">Where the ink started across the box (font units from the box's left edge): the glyph itself starts at 0.</param>
public sealed record SheetBox(string Char, HandGlyph? Glyph, byte[] Gray, int W, int H, double Left = 0);

/// <summary>A filled-in sheet, read from a photo.</summary>
/// <param name="Problem">Why it couldn't be read (null when it was).</param>
public sealed record SheetReading(IReadOnlyList<SheetBox> Boxes, string? Problem, Vec2[]? Corners = null)
{
    public int Written => Boxes.Count(b => b.Glyph is not null);
}

/// <summary>Finds the sheet in a photo and turns each written box into single-line strokes.</summary>
public static class SheetReader
{
    /// <summary>Pixels per mm the boxes are read at.</summary>
    public const double Ppm = 10;
    /// <summary>Kept clear of each box's edge (its printed line).</summary>
    public const double Inset = 0.6;

    public static SheetReading Read(RasterImage photo, SheetOptions? options = null)
    {
        var o = options ?? new SheetOptions();
        var lum = photo.Luminance();
        var corners = FindCorners(lum, photo.Width, photo.Height);
        if (corners is null)
            return new SheetReading([], "The sheet's four black corner squares weren't found. Photograph the whole sheet, flat and evenly lit, with all four squares in the picture and nothing dark touching them.");
        var map = Homography.Fit([.. FontSheet.MarkerCentres.Select((m, i) => (m, corners[i]))]);
        if (map is null) return new SheetReading([], "The sheet's corners were found but don't make sense together. Try another photo, straight on.", corners);
        var boxes = new List<SheetBox>();
        for (int i = 0; i < FontSheet.Characters.Length; i++)
            boxes.Add(ReadBox(lum, photo.Width, photo.Height, map.Value, i, o));
        return new SheetReading(boxes, null, corners);
    }

    static double Sample(float[] lum, int w, int h, Vec2 p)
    {
        double x = p.X - 0.5, y = p.Y - 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double At(int xx, int yy) => xx < 0 || yy < 0 || xx >= w || yy >= h ? 1 : lum[yy * w + xx];
        return At(x0, y0) * (1 - fx) * (1 - fy) + At(x0 + 1, y0) * fx * (1 - fy) + At(x0, y0 + 1) * (1 - fx) * fy + At(x0 + 1, y0 + 1) * fx * fy;
    }

    static SheetBox ReadBox(float[] lum, int iw, int ih, Homography map, int index, SheetOptions o)
    {
        var box = FontSheet.Box(index);
        int w = (int)Math.Round((box.Width - 2 * Inset) * Ppm), h = (int)Math.Round((box.Height - 2 * Inset) * Ppm);
        var v = new float[w * h];
        // A few samples per pixel when the photo is sharper than the reading grid.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        var mm = new Vec2(box.MinX + Inset + (x + 0.25 + sx * 0.5) / Ppm, box.MinY + Inset + (y + 0.25 + sy * 0.5) / Ppm);
                        s += Sample(lum, iw, ih, map.Apply(mm));
                    }
                v[y * w + x] = (float)(s / 4);
            }
        // The paper's brightness here: most of a box is bare paper.
        var sorted = (float[])v.Clone();
        Array.Sort(sorted);
        double paper = Math.Max(0.05, sorted[(int)(sorted.Length * 0.88)]);
        double cut = paper * Math.Clamp(o.InkLevel, 0.2, 0.95);
        var gray = new byte[w * h];
        var ink = new bool[w * h];
        for (int i = 0; i < v.Length; i++)
        {
            gray[i] = (byte)Math.Clamp(v[i] / paper * 235, 0, 255);
            ink[i] = v[i] < cut;
        }
        var (glyph, left) = Strokes(ink, w, h, o);
        return new SheetBox(FontSheet.Characters[index], glyph, gray, w, h, left);
    }

    /// <summary>The ink of one box (pixels at <see cref="Ppm"/>, starting <see cref="Inset"/> inside the box) as a letter.</summary>
    internal static (HandGlyph? Glyph, double Left) Strokes(bool[] ink, int w, int h, SheetOptions o)
    {
        var comps = Components(ink, w, h);
        double speck = 0.15 * Ppm * Ppm; // 0.15 mm²
        var keep = new bool[w * h];
        var dots = new List<(Vec2 Centre, double Size)>();
        var traced = new List<Component>();
        foreach (var c in comps)
        {
            if (c.Count < speck) continue;
            bool edge = c.MinX == 0 || c.MinY == 0 || c.MaxX == w - 1 || c.MaxY == h - 1;
            int bw = c.MaxX - c.MinX + 1, bh = c.MaxY - c.MinY + 1;
            // The printed box line showing through along an edge: long and very thin.
            if (edge && Math.Min(bw, bh) <= 0.7 * Ppm && Math.Max(bw, bh) >= 3 * Ppm) continue;
            if (Math.Max(bw, bh) <= 1.8 * Ppm && c.Count <= 3.0 * Ppm * Ppm)
            {
                dots.Add((new Vec2(c.SumX / c.Count + 0.5, c.SumY / c.Count + 0.5), Math.Max(bw, bh)));
                continue;
            }
            foreach (int i in c.Pixels) keep[i] = true;
            traced.Add(c);
        }
        var lines = traced.Count == 0 ? [] : Centreline.Trace(keep, w, h, minLengthPx: 0.8 * Ppm, smoothPx: Math.Max(0.3, o.SmoothMm * Ppm));
        lines = Join(lines, 0.3 * Ppm, 70);
        // Bits that thinned away to nothing (a heavy full stop, say) are dots too.
        foreach (var c in traced)
        {
            var bb = new Rect2(c.MinX - 1, c.MinY - 1, c.MaxX + 2, c.MaxY + 2);
            if (!lines.Any(l => l.Any(bb.Contains)))
                dots.Add((new Vec2(c.SumX / c.Count + 0.5, c.SumY / c.Count + 0.5), Math.Max(c.MaxX - c.MinX + 1, c.MaxY - c.MinY + 1)));
        }
        if (lines.Count == 0 && dots.Count == 0) return (null, 0);
        // Pixels → font units: capital height = 1, baseline at 0.
        Vec2 U(Vec2 p) => new((p.X / Ppm + Inset) / FontSheet.CapHeight, (p.Y / Ppm + Inset - FontSheet.Baseline) / FontSheet.CapHeight);
        var strokes = new List<HandStroke>();
        foreach (var l in lines)
        {
            bool closed = l.Count > 3 && l[0].Dist(l[^1]) < 1.5;
            var pts = l.Select(U).ToList();
            if (closed) pts.RemoveAt(pts.Count - 1);
            strokes.Add(new HandStroke(pts, closed));
        }
        foreach (var (centre, size) in dots)
        {
            // A small ring the size of the dot, at least 0.25 mm across on the sheet.
            double r = Math.Clamp(size * 0.3, 0.25 * Ppm / 2, 0.7 * Ppm);
            var pts = Enumerable.Range(0, 10).Select(k => U(centre + new Vec2(Math.Cos(k * Math.PI / 5), Math.Sin(k * Math.PI / 5)) * r)).ToList();
            strokes.Add(new HandStroke(pts, true));
        }
        var b = Rect2.Bounds(strokes.SelectMany(s => s.Points));
        var shifted = strokes.Select(s => new HandStroke([.. s.Points.Select(p => new Vec2(p.X - b.MinX, p.Y))], s.Closed)).ToList();
        return (new HandGlyph(b.Width, Order(shifted)), b.MinX);
    }

    /// <summary>Strokes in writing order, roughly: left to right, each starting near where the last ended.</summary>
    static List<HandStroke> Order(List<HandStroke> strokes)
    {
        var left = strokes.ToList();
        var o = new List<HandStroke>();
        Vec2 at = new(-1, -1);
        while (left.Count > 0)
        {
            int best = 0; bool flip = false; double bestD = double.MaxValue;
            for (int i = 0; i < left.Count; i++)
            {
                var s = left[i];
                double d0 = s.Points[0].Dist(at), d1 = s.Closed ? double.MaxValue : s.Points[^1].Dist(at);
                if (d0 < bestD) { bestD = d0; best = i; flip = false; }
                if (d1 < bestD) { bestD = d1; best = i; flip = true; }
            }
            var pick = left[best];
            left.RemoveAt(best);
            if (flip) pick = new HandStroke([.. pick.Points.Reverse()], pick.Closed);
            o.Add(pick);
            at = pick.Closed ? pick.Points[0] : pick.Points[^1];
        }
        return o;
    }

    /// <summary>
    /// Lines that meet end to end and carry on in about the same direction become one line, as the
    /// pen drew them (the trace breaks every stroke where another crosses or meets it).
    /// </summary>
    internal static List<List<Vec2>> Join(List<List<Vec2>> lines, double tolPx, double maxTurnDeg)
    {
        var ls = lines.Where(l => l.Count >= 2).Select(l => l.ToList()).ToList();
        static bool IsLoop(List<Vec2> l) => l.Count > 3 && l[0].Dist(l[^1]) < 1e-6;
        static Vec2 Dir(List<Vec2> l, bool atEnd)
        {
            // Direction coming out of the line at that end, over a few pixels.
            double want = 4, got = 0;
            int n = l.Count;
            Vec2 tip = atEnd ? l[^1] : l[0], back = tip;
            for (int k = 1; k < n && got < want; k++)
            {
                back = atEnd ? l[n - 1 - k] : l[k];
                got = tip.Dist(back);
            }
            var d = tip - back;
            return d.Length > 1e-9 ? d / d.Length : default;
        }
        double cosMax = Math.Cos(maxTurnDeg * Math.PI / 180);
        while (true)
        {
            int bi = -1, bj = -1; bool biEnd = false, bjEnd = false; double bestCos = cosMax;
            for (int i = 0; i < ls.Count; i++)
            {
                if (IsLoop(ls[i])) continue;
                for (int j = i + 1; j < ls.Count; j++)
                {
                    if (IsLoop(ls[j])) continue;
                    foreach (bool ie in new[] { true, false })
                        foreach (bool je in new[] { true, false })
                        {
                            Vec2 a = ie ? ls[i][^1] : ls[i][0], b = je ? ls[j][^1] : ls[j][0];
                            if (a.Dist(b) > tolPx) continue;
                            // Leaving line i at that end, then entering line j at its end: straight on = the directions are opposite.
                            double cos = -Dir(ls[i], ie).Dot(Dir(ls[j], je));
                            if (cos > bestCos) { bestCos = cos; bi = i; bj = j; biEnd = ie; bjEnd = je; }
                        }
                }
            }
            if (bi < 0) break;
            var first = biEnd ? ls[bi] : Enumerable.Reverse(ls[bi]).ToList();
            var second = bjEnd ? Enumerable.Reverse(ls[bj]).ToList() : ls[bj];
            var joined = first.Concat(second.Skip(first[^1].Dist(second[0]) < 1e-6 ? 1 : 0)).ToList();
            ls.RemoveAt(bj);
            ls[bi] = joined;
        }
        // A line that now ends where it started is a loop (an o drawn in pieces).
        foreach (var l in ls)
            if (l.Count > 4 && !IsLoop(l) && l[0].Dist(l[^1]) <= tolPx) l.Add(l[0]);
        return ls;
    }

    internal sealed class Component
    {
        public int Count, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1;
        public double SumX, SumY;
        public List<int> Pixels = [];
    }

    internal static List<Component> Components(bool[] mask, int w, int h, int maxPixels = int.MaxValue)
    {
        var seen = new bool[w * h];
        var o = new List<Component>();
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            var c = new Component();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                c.Count++;
                c.SumX += x; c.SumY += y;
                if (c.Pixels.Count < maxPixels) c.Pixels.Add(i);
                if (x < c.MinX) c.MinX = x;
                if (x > c.MaxX) c.MaxX = x;
                if (y < c.MinY) c.MinY = y;
                if (y > c.MaxY) c.MaxY = y;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (mask[ni] && !seen[ni]) { seen[ni] = true; stack.Push(ni); }
                    }
            }
            o.Add(c);
        }
        return o;
    }

    // ---- Finding the sheet ----

    sealed record Square(Vec2 Centre, double Area, double Hole);

    /// <summary>
    /// The centres of the four corner squares in the photo (pixels), in the sheet's order:
    /// top-left (the hollow one), top-right, bottom-right, bottom-left. Null when not found.
    /// </summary>
    public static Vec2[]? FindCorners(float[] lum, int w, int h)
    {
        // Work on a smaller copy: the squares are big.
        int f = Math.Max(1, (int)Math.Ceiling(Math.Max(w, h) / 1600.0));
        int sw = w / f, sh = h / f;
        if (sw < 40 || sh < 40) return null;
        var small = new float[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                double s = 0;
                for (int yy = 0; yy < f; yy++)
                    for (int xx = 0; xx < f; xx++) s += lum[(y * f + yy) * w + x * f + xx];
                small[y * sw + x] = (float)(s / (f * f));
            }
        // Dark against the light around it (a wide neighbourhood, so uneven light doesn't matter).
        int r = Math.Max(8, Math.Min(sw, sh) / 10);
        var integral = new double[(sw + 1) * (sh + 1)];
        for (int y = 0; y < sh; y++)
        {
            double row = 0;
            for (int x = 0; x < sw; x++)
            {
                row += small[y * sw + x];
                integral[(y + 1) * (sw + 1) + x + 1] = integral[y * (sw + 1) + x + 1] + row;
            }
        }
        var dark = new bool[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(sw, x + r + 1), y0 = Math.Max(0, y - r), y1 = Math.Min(sh, y + r + 1);
                double sum = integral[y1 * (sw + 1) + x1] - integral[y0 * (sw + 1) + x1] - integral[y1 * (sw + 1) + x0] + integral[y0 * (sw + 1) + x0];
                double mean = sum / ((x1 - x0) * (y1 - y0));
                dark[y * sw + x] = small[y * sw + x] < mean * 0.7 && small[y * sw + x] < 0.6;
            }
        var squares = new List<Square>();
        int minSide = Math.Min(sw, sh);
        foreach (var c in Components(dark, sw, sh))
        {
            int bw = c.MaxX - c.MinX + 1, bh = c.MaxY - c.MinY + 1;
            if (c.Count < 40 || Math.Max(bw, bh) > minSide / 4 || Math.Min(bw, bh) < 6) continue;
            if (Math.Max(bw, bh) > 2.2 * Math.Min(bw, bh)) continue;
            if (Classify(c, sw, sh) is { } sq) squares.Add(sq with { Centre = new Vec2((sq.Centre.X + 0.5) * f, (sq.Centre.Y + 0.5) * f), Area = sq.Area * f * f });
        }
        return Pick(squares);
    }

    /// <summary>A solid or hollow square (or something else: null).</summary>
    static Square? Classify(Component c, int w, int h)
    {
        int bw = c.MaxX - c.MinX + 1, bh = c.MaxY - c.MinY + 1;
        var inside = new bool[bw * bh];
        foreach (int i in c.Pixels) inside[(i / w - c.MinY) * bw + i % w - c.MinX] = true;
        // Holes: background not reachable from the box's edge.
        var outside = new bool[bw * bh];
        var stack = new Stack<int>();
        for (int x = 0; x < bw; x++) { Seed(x, 0); Seed(x, bh - 1); }
        for (int y = 0; y < bh; y++) { Seed(0, y); Seed(bw - 1, y); }
        void Seed(int x, int y) { int i = y * bw + x; if (!inside[i] && !outside[i]) { outside[i] = true; stack.Push(i); } }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % bw, y = i / bw;
            if (x > 0) Seed(x - 1, y);
            if (x < bw - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < bh - 1) Seed(x, y + 1);
        }
        int hole = 0;
        double hx = 0, hy = 0;
        for (int i = 0; i < inside.Length; i++)
            if (!inside[i] && !outside[i]) { hole++; hx += i % bw; hy += i / bw; }
        double full = c.Count + hole;
        // Solid enough: the filled shape covers most of its convex outline (a square seen at an angle is still a quadrilateral).
        var rows = new List<Vec2>();
        for (int y = 0; y < bh; y++)
        {
            int a = -1, b = -1;
            for (int x = 0; x < bw; x++) if (inside[y * bw + x]) { if (a < 0) a = x; b = x; }
            if (a >= 0) { rows.Add(new Vec2(a, y)); rows.Add(new Vec2(b + 1, y)); rows.Add(new Vec2(a, y + 1)); rows.Add(new Vec2(b + 1, y + 1)); }
        }
        var hull = Hull(rows);
        double hullArea = Area(hull), perim = 0;
        for (int i = 0; i < hull.Count; i++) perim += hull[i].Dist(hull[(i + 1) % hull.Count]);
        if (hullArea < 1 || full / hullArea < 0.86) return null;
        // Square-ish, not a circle or a long bar: perimeter² / (16 × area) is 1 for a square, 0.79 for a circle.
        double shape = perim * perim / (16 * hullArea);
        if (shape < 0.88 || shape > 1.25) return null;
        double holeShare = hole / full;
        // The hole of the hollow square sits in its middle.
        if (holeShare > 0.04)
        {
            var hc = new Vec2(hx / hole, hy / hole);
            var cc = new Vec2(c.SumX / c.Count - c.MinX, c.SumY / c.Count - c.MinY);
            if (hc.Dist(cc) > 0.15 * Math.Max(bw, bh) || holeShare > 0.45) return null;
        }
        // Centre of the whole square, hole included.
        double sx = c.SumX + hx + hole * c.MinX, sy = c.SumY + hy + hole * c.MinY;
        return new Square(new Vec2(sx / full, sy / full), full, holeShare);
    }

    /// <summary>The hollow square and three solid ones that make the biggest sheet-shaped quadrilateral.</summary>
    static Vec2[]? Pick(List<Square> squares)
    {
        var hollow = squares.Where(s => s.Hole is > 0.06 and < 0.45).ToList();
        var solid = squares.Where(s => s.Hole <= 0.03).OrderByDescending(s => s.Area).Take(16).ToList();
        Vec2[]? best = null;
        double bestArea = 0;
        double expectRatio = (FontSheet.Width - FontSheet.Marker) / (FontSheet.Height - FontSheet.Marker);
        foreach (var t in hollow)
            for (int a = 0; a < solid.Count; a++)
                for (int b = a + 1; b < solid.Count; b++)
                    for (int c = b + 1; c < solid.Count; c++)
                    {
                        var four = new[] { t, solid[a], solid[b], solid[c] };
                        double amin = four.Min(s => s.Area), amax = four.Max(s => s.Area);
                        if (amax > 5 * amin) continue;
                        var centre = new Vec2(four.Average(s => s.Centre.X), four.Average(s => s.Centre.Y));
                        // Round the middle (clockwise on screen), starting at the hollow one.
                        var ring = four.OrderBy(s => Math.Atan2(s.Centre.Y - centre.Y, s.Centre.X - centre.X)).ToList();
                        int k = ring.IndexOf(t);
                        var q = Enumerable.Range(0, 4).Select(i => ring[(k + i) % 4].Centre).ToArray();
                        if (!Convex(q)) continue;
                        // Top edge to side edge about as on the sheet (a photo at an angle changes it some).
                        double top = (q[0].Dist(q[1]) + q[3].Dist(q[2])) / 2, side = (q[1].Dist(q[2]) + q[0].Dist(q[3])) / 2;
                        if (side < 1e-9) continue;
                        double ratio = top / side;
                        if (ratio < expectRatio * 0.45 || ratio > expectRatio * 1.7) continue;
                        // The squares should be small next to the sheet.
                        double area = Area([.. q]);
                        if (amax > area / 150) continue;
                        if (area > bestArea) { bestArea = area; best = q; }
                    }
        return best;
    }

    static bool Convex(Vec2[] q)
    {
        int sign = 0;
        for (int i = 0; i < q.Length; i++)
        {
            var a = q[(i + 1) % q.Length] - q[i];
            var b = q[(i + 2) % q.Length] - q[(i + 1) % q.Length];
            int s = Math.Sign(a.Cross(b));
            if (s == 0) return false;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        // Clockwise on screen (y down) means positive cross products.
        return sign > 0;
    }

    static double Area(IReadOnlyList<Vec2> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) a += p[i].Cross(p[(i + 1) % p.Count]);
        return Math.Abs(a) / 2;
    }

    static List<Vec2> Hull(List<Vec2> pts)
    {
        var p = pts.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        if (p.Count < 3) return p;
        var lower = new List<Vec2>();
        foreach (var v in p)
        {
            while (lower.Count >= 2 && (lower[^1] - lower[^2]).Cross(v - lower[^2]) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(v);
        }
        var upper = new List<Vec2>();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var v = p[i];
            while (upper.Count >= 2 && (upper[^1] - upper[^2]).Cross(v - upper[^2]) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(v);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return [.. lower, .. upper];
    }
}
