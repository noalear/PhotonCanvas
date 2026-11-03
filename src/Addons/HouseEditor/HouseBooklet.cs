using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.App.View3D;
using PhotonCanvas.Core;
using SkiaSharp;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// Building instructions for a building from the editor, in the manner of a building-set manual: a cover
/// with the finished building, every numbered piece with how many there are, then a step per page from the
/// ground up, each with the pieces it takes (numbered as they're scored on them) and the building so far,
/// the new pieces picked out in colour with their numbers on them. Saved as a PDF.
/// </summary>
public static class HouseBooklet
{
    const float Margin = 40;
    static readonly SKColor Ink = new(0x22, 0x22, 0x22), Muted = new(0x6B, 0x6B, 0x6B), Rule = new(0xD0, 0xD0, 0xD0);
    static readonly SKColor Wood = new(0xE6, 0xCF, 0xA8), WoodEdge = new(0x9C, 0x7A, 0x52);
    static readonly SKColor NewFill = new(0xFF, 0xC2, 0x5C), NewEdge = new(0xB0, 0x5A, 0x00);
    static readonly SKColor Tag = new(0x1F, 0x5F, 0xA8), Box = new(0xF3, 0xF6, 0xFA), BoxEdge = new(0xC5, 0xD3, 0xE3);

    static readonly Vec3 Light = new(0.35, 0.85, 0.45);
    static SlabStyle LookOf(Color colour, Color edge) => new(SlabStyle.Lit(colour, Light), edge, LineWidth.Px(0.8), Color.FromRgb(0x7E, 0x5A, 0x38), Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.25, 0.8));
    static readonly SlabStyle Built = LookOf(Color.FromRgb(0xDD, 0xC9, 0xAA), Color.FromRgb(0x5A, 0x46, 0x32));
    static readonly SlabStyle Adding = LookOf(Color.FromRgb(0xFF, 0xB2, 0x2E), Color.FromRgb(0x8A, 0x45, 0x00));

    /// <summary>What the booklet is called and the page size (points: US Letter unless set; everything stays inside A4 too).</summary>
    public sealed record Options
    {
        public string Title { get; init; } = "Building";
        public float PageWidth { get; init; } = 612;
        public float PageHeight { get; init; } = 792;
        /// <summary>Whether the numbers are scored on the pieces (the booklet says so).</summary>
        public bool Marked { get; init; } = true;
    }

    /// <summary>Writes the booklet to <paramref name="path"/>; returns how many pages it has.</summary>
    public static int SavePdf(HouseKit kit, HouseDesign design, Options o, string path, CancellationToken ct = default)
    {
        var pages = Pages(kit, design, o, ct);
        using var file = File.Create(path);
        using var doc = SKDocument.CreatePdf(file, new SKDocumentPdfMetadata { Title = $"{o.Title}: building instructions", Creator = AppIdentity.ProductName, Producer = AppIdentity.ProductName });
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            var c = doc.BeginPage(o.PageWidth, o.PageHeight);
            page(c);
            doc.EndPage();
        }
        doc.Close();
        return pages.Count;
    }

    /// <summary>One page as a PNG (for the help's pictures and the tests), <paramref name="scale"/> pixels a point.</summary>
    public static byte[] PagePng(HouseKit kit, HouseDesign design, Options o, int page, float scale = 1.5f)
    {
        var pages = Pages(kit, design, o, default);
        var info = new SKImageInfo((int)(o.PageWidth * scale), (int)(o.PageHeight * scale));
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale(scale);
        pages[Math.Clamp(page, 0, pages.Count - 1)](surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>How many pages the booklet has.</summary>
    public static int PageCount(HouseKit kit, HouseDesign design, Options o) => Pages(kit, design, o, default).Count;

    /// <summary>A numbered piece as the parts list shows it: its outline, how many, what it's called.</summary>
    sealed record Part(int Number, string Name, PathsD Outline, int Count, double W, double H, Sheet Sheet);

    static List<Action<SKCanvas>> Pages(HouseKit kit, HouseDesign design, Options o, CancellationToken ct)
    {
        if (kit.Pieces.Count == 0 || kit.Solids.Count == 0) throw new ArgumentException("There's nothing to put together yet.");
        var parts = kit.Pieces.Where(p => p.Number > 0).GroupBy(p => p.Number).OrderBy(g => g.Key).Select(g =>
        {
            var first = g.First();
            var outline = Clipper.SimplifyPaths(Geo.Tidy(first.Cut), 0.15);
            var b = Geo.Bounds(outline);
            return new Part(g.Key, first.Name, outline, g.Sum(p => p.Copies), b.right - b.left, b.bottom - b.top, first.Sheet);
        }).ToDictionary(p => p.Number);
        var steps = HouseSteps.Steps(kit);
        var spec = design.Style;
        int total = 0;
        var pages = new List<Action<SKCanvas>>();
        void Add(Action<SKCanvas> draw)
        {
            int number = pages.Count + 1;
            pages.Add(c =>
            {
                draw(c);
                using var f = Font(9);
                Text(c, $"{o.Title} · page {number} of {total}", o.PageWidth / 2, o.PageHeight - 20, f, Muted, SKTextAlign.Center);
            });
        }
        var painter = new Model3DPainter();
        float W = o.PageWidth - 2 * Margin;

        // ---- cover ----
        var coverRect = new SKRect(Margin, Margin + 80, o.PageWidth - Margin, o.PageHeight - 200);
        var cover = Render(painter, kit, [.. Enumerable.Range(0, kit.Solids.Count)], [], inside: null, coverRect, ct);
        Add(c =>
        {
            using var big = Font(30, bold: true);
            using var sub = Font(14);
            Text(c, o.Title, Margin, Margin + 30, big, Ink);
            Text(c, "Building instructions", Margin, Margin + 54, sub, Muted);
            cover?.Draw(c, coverRect);
            using var f = Font(11);
            int levels = kit.Solids.Max(s => s.Level) + 1;
            var facts = new List<string>
            {
                $"{kit.PieceCount} pieces ({parts.Count} different), {levels} floor{(levels == 1 ? "" : "s")}, {steps.Count} steps",
                Math.Abs(spec.WallSheetMm - spec.FurnitureSheetMm) < 1e-6 || !kit.Pieces.Any(p => p.Sheet == Sheet.Furniture)
                    ? $"Cut from {spec.WallSheetMm:0.##} mm sheet"
                    : $"Cut from {spec.WallSheetMm:0.##} mm sheet (the furniture and doors that swing from {spec.FurnitureSheetMm:0.##} mm)",
                "Wood glue, and cocktail sticks or 2 mm dowel for the column pins",
            };
            float y = o.PageHeight - 170;
            foreach (var line in facts) { Text(c, "•  " + line, Margin, y, f, Ink); y += 18; }
            using var hint = Font(10);
            Wrap(c, (o.Marked
                    ? "Every piece has its number scored lightly on it, low down where it's clear of the texture. "
                    : "The pieces aren't marked: match them by their shape in the parts list. ") +
                "Build from step 1 on, one floor at a time; in each step the new pieces are picked out in orange, with their numbers.",
                Margin, y + 8, W, hint, Muted);
        });

        // ---- parts list ----
        const int cols = 5;
        float cell = W / cols, cellH = cell * 0.8f + 22;
        int rowsFirst = (int)((o.PageHeight - 2 * Margin - 80) / cellH), rowsMore = (int)((o.PageHeight - 2 * Margin - 50) / cellH);
        var list = parts.Values.ToList();
        for (int start = 0, page = 0; start < list.Count; page++)
        {
            int rows = page == 0 ? rowsFirst : rowsMore, from = start, count = Math.Min(list.Count - start, rows * cols);
            bool first = page == 0;
            Add(c =>
            {
                using var head = Font(20, bold: true);
                Text(c, first ? "Pieces" : "Pieces (continued)", Margin, Margin + 20, head, Ink);
                float top = Margin + 34;
                if (first)
                {
                    using var f = Font(10.5f);
                    Text(c, "Each piece's number, how many to cut, and its size. Pieces with the same number are just alike.", Margin, top + 6, f, Muted);
                    top += 26;
                }
                for (int k = 0; k < count; k++)
                {
                    var p = list[from + k];
                    float x = Margin + k % cols * cell, y = top + k / cols * cellH;
                    PartCell(c, p, new SKRect(x + 3, y + 3, x + cell - 3, y + cellH - 3));
                }
            });
            start += count;
        }

        // ---- the steps ----
        var done = new List<int>();
        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            int n = i + 1;
            var box = PartsBox(step, parts, W);
            float boxTop;
            using (var measure = Font(10.5f)) boxTop = Margin + 34 + LineCount(step.Tip, W, measure) * 10.5f * 1.3f + 4;
            var picRect = new SKRect(Margin, boxTop + box.Height + 12, o.PageWidth - Margin, o.PageHeight - 40);
            // Pieces put together on their own are shown on their own; the step after stands them in the building.
            var picture = Render(painter, kit, step.Alone ? [] : done, step.Solids, step.Inside ? step.Level : null, picRect, ct, labels: !step.PutsIn);
            if (!step.Alone) done.AddRange(step.Solids);
            Add(c =>
            {
                using var num = Font(30, bold: true);
                using var title = Font(15, bold: true);
                Text(c, n.ToString(System.Globalization.CultureInfo.InvariantCulture), Margin, Margin + 26, num, Ink);
                float tx = Margin + num.MeasureText(n.ToString(System.Globalization.CultureInfo.InvariantCulture)) + 12;
                Text(c, Cap(step.Title), tx, Margin + 24, title, Ink);
                using var tip = Font(10.5f);
                Wrap(c, step.Tip, Margin, Margin + 46, W, tip, Muted);
                box.Draw(c, Margin, boxTop);
                picture?.Draw(c, picRect);
            });
        }
        total = pages.Count;
        return pages;
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A piece in the parts list: its outline fitted in the cell, its number, how many, its size and name.</summary>
    static void PartCell(SKCanvas c, Part p, SKRect r)
    {
        using var edge = Stroke(Rule, 0.6f);
        c.DrawRoundRect(r, 4, 4, edge);
        var art = new SKRect(r.Left + 6, r.Top + 20, r.Right - 6, r.Bottom - 26);
        Outline(c, p, art, Wood, WoodEdge);
        using var big = Font(13, bold: true);
        using var small = Font(8.5f);
        Text(c, $"#{p.Number}", r.Left + 6, r.Top + 15, big, Tag);
        Text(c, $"× {p.Count}", r.Right - 6, r.Top + 15, big, Ink, SKTextAlign.Right);
        Text(c, $"{p.W:0} × {p.H:0} mm{(p.Sheet == Sheet.Furniture ? ", furniture sheet" : "")}", r.Left + 6, r.Bottom - 15, small, Muted);
        Text(c, Fit(p.Name, r.Width - 12, small), r.Left + 6, r.Bottom - 5, small, Ink);
    }

    static string Fit(string text, float width, SKPaint font)
    {
        if (font.MeasureText(text) <= width) return text;
        while (text.Length > 1 && font.MeasureText(text + "…") > width) text = text[..^1];
        return text + "…";
    }

    /// <summary>A piece's outline fitted (and kept to scale either way) in a box.</summary>
    static void Outline(SKCanvas c, Part p, SKRect box, SKColor fill, SKColor edge, double? mmScale = null)
    {
        if (p.W <= 0 || p.H <= 0) return;
        // Long pieces lie along the box's long side.
        bool turn = (p.W < p.H) != (box.Width < box.Height) && Math.Abs(p.W - p.H) > 1;
        double w = turn ? p.H : p.W, h = turn ? p.W : p.H;
        double k = mmScale ?? Math.Min(box.Width / w, box.Height / h);
        var b = Geo.Bounds(p.Outline);
        double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var loop in p.Outline)
            for (int i = 0; i < loop.Count; i++)
            {
                double x = loop[i].x - cx, y = loop[i].y - cy;
                if (turn) (x, y) = (-y, x);
                var pt = new SKPoint(box.MidX + (float)(x * k), box.MidY - (float)(y * k));
                if (i == 0) path.MoveTo(pt); else path.LineTo(pt);
                if (i == loop.Count - 1) path.Close();
            }
        using var f = Fill(fill);
        using var s = Stroke(edge, 0.7f);
        c.DrawPath(path, f);
        c.DrawPath(path, s);
    }

    /// <summary>The box at the top of a step with the pieces it takes, each drawn with its number and how many.</summary>
    sealed record PartsBoxLayout(float Height, Action<SKCanvas, float, float> Draw);

    static PartsBoxLayout PartsBox(BuildStep step, Dictionary<int, Part> parts, float width)
    {
        var shown = step.Parts.Where(x => parts.ContainsKey(x.Number)).ToList();
        if (shown.Count == 0) return new(0, (_, _, _) => { });
        const float cellW = 84, cellH = 76, pad = 8;
        int cols = Math.Max(1, (int)((width - 2 * pad) / cellW));
        int rows = (shown.Count + cols - 1) / cols;
        float h = rows * cellH + 2 * pad;
        // One scale for the whole box (so the pieces' sizes compare), unless that makes the smallest too small to see.
        double biggest = shown.Max(x => Math.Max(parts[x.Number].W, parts[x.Number].H));
        double smallest = shown.Min(x => Math.Max(parts[x.Number].W, parts[x.Number].H));
        double k = (cellW - 16) / Math.Max(1e-6, biggest);
        bool oneScale = smallest * k >= 14;
        return new(h, (c, x, y) =>
        {
            var r = new SKRect(x, y, x + width, y + h);
            using var fill = Fill(Box);
            using var edge = Stroke(BoxEdge, 1);
            c.DrawRoundRect(r, 6, 6, fill);
            c.DrawRoundRect(r, 6, 6, edge);
            using var big = Font(10.5f, bold: true);
            using var count = Font(11, bold: true);
            for (int i = 0; i < shown.Count; i++)
            {
                var (number, n) = shown[i];
                var p = parts[number];
                float cx = x + pad + i % cols * cellW, cy = y + pad + i / cols * cellH;
                var art = new SKRect(cx + 6, cy + 4, cx + cellW - 6, cy + cellH - 22);
                Outline(c, p, art, NewFill, NewEdge, oneScale ? k : null);
                Text(c, $"#{number}", cx + 6, cy + cellH - 8, big, Tag);
                Text(c, $"{n}×", cx + cellW - 8, cy + cellH - 8, count, Ink, SKTextAlign.Right);
            }
        });
    }

    /// <summary>A rendered picture of the building so far, and where to label the new pieces' numbers on it.</summary>
    sealed record Picture(SKImage Image, List<(float X, float Y, int Number)> Labels, List<(float X, float Y, string Letter)> Letters)
    {
        public void Draw(SKCanvas c, SKRect r)
        {
            // Kept to its shape, in the middle of the space.
            float k = Math.Min(r.Width / Image.Width, r.Height / Image.Height);
            float w = Image.Width * k, h = Image.Height * k, x = r.MidX - w / 2, y = r.MidY - h / 2;
            c.DrawImage(Image, new SKRect(x, y, x + w, y + h));
            using var font = Font(9, bold: true);
            using var fill = Fill(Tag);
            using var ring = Stroke(SKColors.White, 1.2f);
            using var dark = Fill(Ink);
            using var bigFont = Font(12, bold: true);
            foreach (var (lx, ly, t) in Letters)
            {
                float px = x + lx * k, py = y + ly * k, tw = Math.Max(20, bigFont.MeasureText(t) + 10);
                var tag = new SKRect(px - tw / 2, py - 10, px + tw / 2, py + 10);
                c.DrawRoundRect(tag, 4, 4, dark);
                c.DrawRoundRect(tag, 4, 4, ring);
                Text(c, t, px, py + 4.3f, bigFont, SKColors.White, SKTextAlign.Center);
            }
            foreach (var (lx, ly, n) in Labels)
            {
                float px = x + lx * k, py = y + ly * k;
                string t = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                float tw = Math.Max(14, font.MeasureText(t) + 8);
                var tag = new SKRect(px - tw / 2, py - 7, px + tw / 2, py + 7);
                c.DrawRoundRect(tag, 7, 7, fill);
                c.DrawRoundRect(tag, 7, 7, ring);
                Text(c, t, px, py + 3.2f, font, SKColors.White, SKTextAlign.Center);
            }
        }
    }

    /// <summary>
    /// The pieces in <paramref name="built"/> and, picked out, <paramref name="adding"/>, rendered for a space
    /// <paramref name="r"/> big (points). With <paramref name="inside"/> set, that floor's walls in front are left out to see in.
    /// </summary>
    static Picture? Render(Model3DPainter painter, HouseKit kit, List<int> built, List<int> adding, int? inside, SKRect r, CancellationToken ct, bool labels = true)
    {
        ct.ThrowIfCancellationRequested();
        var camera = Camera3D.YUp(35, 32);
        var toward = new Vec3(camera.Toward.X, 0, camera.Toward.Z).Unit;
        bool InFront(HouseSolid s)
        {
            if (inside is not { } k || s.Level != k || s.Kind != SolidKind.Wall && !(s.Kind == SolidKind.Furniture && s.Facing.Length > 0)) return false;
            var f = new Vec3(s.Facing.X, s.Facing.Y, s.Facing.Z);
            double facing = Math.Abs(f.Dot(toward)), near = new Vec3(s.Middle.X, 0, s.Middle.Z).Dot(toward);
            return facing > 0.38 && near > -2;
        }
        var scene = new Scene3D();
        // Everything above the floor being built is left out (the roofs come last anyway).
        foreach (int i in built)
        {
            var s = kit.Solids[i];
            if (!InFront(s)) Add(scene, s, Built);
        }
        foreach (int i in adding) Add(scene, kit.Solids[i], Adding);
        if (scene.IsEmpty) return null;
        const float pxPerPt = 2.2f;
        int w = Math.Max(64, (int)(r.Width * pxPerPt)), h = Math.Max(64, (int)(r.Height * pxPerPt));
        var pixels = painter.Picture(scene, camera, w, h, Colors.White);
        if (pixels is null) return null;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var ints = new int[pixels.Length];
        Buffer.BlockCopy(pixels, 0, ints, 0, pixels.Length * 4);
        System.Runtime.InteropServices.Marshal.Copy(ints, 0, bitmap.GetPixels(), ints.Length);
        using var raw = SKImage.FromBitmap(bitmap);
        // Saved as a JPEG inside the PDF: a fraction of the size, and it looks the same at print sizes.
        using var jpeg = raw.Encode(SKEncodedImageFormat.Jpeg, 90);
        var image = SKImage.FromEncodedData(jpeg);

        // A number on each new piece (once for each place, the biggest pieces first, not crowding each other).
        var tags = new List<(float X, float Y, int Number)>();
        foreach (int i in labels ? adding.OrderByDescending(i => Geo.Area(kit.Solids[i].Outline)).ToList() : [])
        {
            var s = kit.Solids[i];
            if (s.Number <= 0) continue;
            var b = Geo.Bounds(s.Outline);
            var mid = s.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
            var at = painter.ToView(new Vec3(mid.X, mid.Y, mid.Z));
            float lx = (float)at.X, ly = (float)at.Y;
            if (lx < 0 || ly < 0 || lx > w || ly > h) continue;
            // Not on top of another, nor near another of the same number, and no more than three of each.
            double Far(float d) => d * d * pxPerPt * pxPerPt;
            if (tags.Any(l => (l.X - lx) * (l.X - lx) + (l.Y - ly) * (l.Y - ly) < Far(l.Number == s.Number ? 70 : 17))) continue;
            if (tags.Count(l => l.Number == s.Number) >= 3) continue;
            tags.Add((lx, ly, s.Number));
        }
        // Without numbers (putting in what's been made), each wall's letter, as on the plan.
        var letters = new List<(float X, float Y, string Letter)>();
        if (!labels)
            foreach (var g in adding.Where(i => kit.Solids[i].Kind == SolidKind.Wall).GroupBy(i => (HouseSteps.WallOf(kit.Solids[i].Name), kit.Solids[i].Level)))
            {
                if (g.Key.Item1 is not { Length: > 0 } letter) continue;
                var m = kit.Solids[g.First()].Middle;
                var at = painter.ToView(new Vec3(m.X, m.Y, m.Z));
                if (at.X >= 0 && at.Y >= 0 && at.X <= w && at.Y <= h) letters.Add(((float)at.X, (float)at.Y, letter));
            }
        return new Picture(image, tags, letters);
    }

    static void Add(Scene3D scene, HouseSolid s, SlabStyle style)
    {
        double T = s.Thickness;
        var p = s.Pose;
        var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
        var frame = new Frame3(new Vec3(p.O.X, p.O.Y, p.O.Z), new Vec3(p.U.X, p.U.Y, p.U.Z), new Vec3(p.V.X, p.V.Y, p.V.Z));
        scene.AddSlab(outline, frame, -T / 2, T / 2, style, s.Etched, s.Lines);
    }

    // ---- drawing ----

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };
    static SKPaint Stroke(SKColor c, float w) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeJoin = SKStrokeJoin.Round };

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

    static int LineCount(string text, float width, SKPaint font)
    {
        int n = 1;
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(tryLine) > width && line.Length > 0) { n++; line = word; }
            else line = tryLine;
        }
        return n;
    }

    /// <summary>Text wrapped to a width, from the baseline at <paramref name="y"/> down; returns the baseline after it.</summary>
    static float Wrap(SKCanvas c, string text, float x, float y, float width, SKPaint font, SKColor colour)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(tryLine) > width && line.Length > 0)
            {
                Text(c, line, x, y, font, colour);
                y += font.TextSize * 1.3f;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) Text(c, line, x, y, font, colour);
        return y + font.TextSize * 1.3f;
    }
}
