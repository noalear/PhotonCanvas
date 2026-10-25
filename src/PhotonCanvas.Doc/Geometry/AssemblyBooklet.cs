using PhotonCanvas.Core;
using SkiaSharp;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>What the booklet says besides the slices themselves.</summary>
public sealed record BookletOptions
{
    /// <summary>The model's name, on the cover.</summary>
    public string Title { get; init; } = "Model";
    /// <summary>Pin holes' diameter (mm, 0 = none) and how long each pin is cut (null = up to two slices).</summary>
    public double PinMm { get; init; }
    public double? PinLengthMm { get; init; }
    /// <summary>Dowel holes through every slice (mm, 0 = none).</summary>
    public double DowelMm { get; init; }
    /// <summary>How the slices were cut: says what to sand or fill at the end.</summary>
    public SliceOutline Outline { get; init; } = SliceOutline.Middle;
    /// <summary>Page size in points (1/72 in): US Letter unless set. Everything stays inside A4 too.</summary>
    public float PageWidth { get; init; } = 612;
    public float PageHeight { get; init; } = 792;
}

/// <summary>
/// Assembly instructions for stacked slices, in the manner of a building-set manual: a cover with
/// the finished model, a list of every part, then one step per slice from the bottom up. Each step
/// shows the parts to add (with their labels), the stack so far with the new parts picked out in
/// colour, the pins that go in, and where the new parts sit seen from above. Saved as a PDF.
/// </summary>
public static class AssemblyBooklet
{
    const float Margin = 40;
    static readonly SKColor Ink = new(0x22, 0x22, 0x22), Muted = new(0x6B, 0x6B, 0x6B), Rule = new(0xD0, 0xD0, 0xD0);
    static readonly SKColor Wood = new(0xE6, 0xCF, 0xA8), WoodEdge = new(0x9C, 0x7A, 0x52);
    static readonly SKColor New = new(0xFF, 0xB2, 0x2E), NewEdge = new(0xB0, 0x5A, 0x00);
    static readonly SKColor Pin = new(0xD2, 0x2B, 0x3C), Callout = new(0xE3, 0xEE, 0xF8), CalloutEdge = new(0x9D, 0xB9, 0xD6);
    static readonly SKColor Air = new(0xE5, 0x48, 0x4D);

    /// <summary>Writes the booklet to <paramref name="path"/>; returns how many pages it has.</summary>
    public static int SavePdf(SliceStack stack, BookletOptions o, string path)
    {
        var pages = Pages(stack, o);
        using var file = File.Create(path);
        using var doc = SKDocument.CreatePdf(file, new SKDocumentPdfMetadata { Title = $"{o.Title}: assembly", Creator = AppIdentity.ProductName, Producer = AppIdentity.ProductName });
        foreach (var page in pages)
        {
            var c = doc.BeginPage(o.PageWidth, o.PageHeight);
            page(c);
            doc.EndPage();
        }
        doc.Close();
        return pages.Count;
    }

    public static int PageCount(SliceStack stack, BookletOptions o) => Pages(stack, o).Count;

    /// <summary>One page as a PNG, <paramref name="scale"/> pixels a point (for previews and checks).</summary>
    public static byte[] RenderPng(SliceStack stack, BookletOptions o, int page, float scale = 1.5f)
    {
        var pages = Pages(stack, o);
        using var surface = SKSurface.Create(new SKImageInfo((int)(o.PageWidth * scale), (int)(o.PageHeight * scale)));
        var c = surface.Canvas;
        c.Clear(SKColors.White);
        c.Scale(scale);
        pages[Math.Clamp(page, 0, pages.Count - 1)](c);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>A part flattened once, its loops turned so outlines run anticlockwise and holes clockwise.</summary>
    sealed record Flat(SlicePart Part, string Label, List<List<Vec2>> Loops, Rect2 Box);

    static Flat Prepare(SlicePart p)
    {
        var loops = p.Outline.Flatten(0.2).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points.ToList()).ToList();
        for (int i = 0; i < loops.Count; i++)
        {
            int depth = loops.Where((_, j) => j != i).Count(l => Geom.Inside(l, loops[i][0]));
            bool hole = depth % 2 == 1, ccw = Geom.SignedArea(loops[i]) > 0;
            if (hole == ccw) loops[i].Reverse();
        }
        return new Flat(p, p.Label ?? $"{p.Slice}", loops, Rect2.Bounds(loops.SelectMany(l => l)));
    }

    static List<Action<SKCanvas>> Pages(SliceStack stack, BookletOptions o)
    {
        if (stack.Method != SliceMethod.Stacked) throw new ArgumentException("The assembly booklet is for stacked slices.");
        if (stack.Parts.Count == 0) throw new ArgumentException("There are no parts to put together.");
        var parts = stack.Parts.Select(Prepare).ToList();
        double t = stack.ThicknessMm > 0 ? stack.ThicknessMm : stack.StackHeightMm / Math.Max(1, stack.Slices);
        var iso = new Iso(parts, t, stack.Slices);
        int total = 0;
        var pages = new List<Action<SKCanvas>>();
        // Footer with the page number on every page (the count is known once they're all listed).
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

        // ---- cover ----
        Add(c =>
        {
            using var big = Font(30, bold: true);
            using var sub = Font(14);
            Text(c, o.Title, Margin, Margin + 30, big, Ink);
            Text(c, "Assembly instructions", Margin, Margin + 54, sub, Muted);
            iso.Draw(c, new SKRect(Margin, Margin + 80, o.PageWidth - Margin, o.PageHeight - 190), stack.Slices, highlight: 0, pins: []);
            using var f = Font(11);
            var facts = new List<string>
            {
                $"{stack.Slices} slices of {t:0.##} mm material, {parts.Count} parts",
                $"Finished size about {stack.ModelWidthMm:0} × {stack.ModelDepthMm:0} × {stack.StackHeightMm:0} mm",
            };
            if (o.PinMm > 0 && stack.Pins.Count > 0) facts.Add($"{stack.Pins.Count} pins, {o.PinMm:0.#} mm across, {PinLength(o, t)}");
            if (o.DowelMm > 0 && stack.Dowels.Count > 0) facts.Add($"2 dowels, {o.DowelMm:0.#} mm across, {stack.StackHeightMm:0} mm long, through every slice");
            facts.Add("Wood glue, and a flat board to build on");
            float y = o.PageHeight - 160;
            foreach (var line in facts) { Text(c, "•  " + line, Margin, y, f, Ink); y += 18; }
            using var hint = Font(10);
            Wrap(c, "Every part has its label lightly marked on its top face, and the outline of the part that goes on it. Build from step 1 upwards, marks facing up, one slice at a time.",
                Margin, y + 8, o.PageWidth - 2 * Margin, hint, Muted);
        });

        // ---- parts list ----
        const int cols = 6;
        float cell = (o.PageWidth - 2 * Margin) / cols, cellH = cell + 16;
        int rowsFirst = (int)((o.PageHeight - 2 * Margin - 110) / cellH), rowsMore = (int)((o.PageHeight - 2 * Margin - 50) / cellH);
        double biggest = parts.Max(p => Math.Max(p.Box.Width, p.Box.Height));
        double listScale = (cell - 14) / Math.Max(1e-6, biggest);
        for (int start = 0, page = 0; start < parts.Count; page++)
        {
            int rows = page == 0 ? rowsFirst : rowsMore, from = start, count = Math.Min(parts.Count - start, rows * cols);
            bool first = page == 0;
            Add(c =>
            {
                using var head = Font(20, bold: true);
                Text(c, first ? "Parts" : "Parts (continued)", Margin, Margin + 20, head, Ink);
                float top = Margin + 40;
                if (first)
                {
                    using var f = Font(10.5f);
                    var also = new List<string> { $"{parts.Count} parts from {stack.Slices} slices, all drawn to the same scale." };
                    if (o.PinMm > 0 && stack.Pins.Count > 0) also.Add($"Also: {stack.Pins.Count} pins ({o.PinMm:0.#} mm, {PinLength(o, t)}).");
                    if (o.DowelMm > 0 && stack.Dowels.Count > 0) also.Add($"Also: 2 dowels ({o.DowelMm:0.#} mm × {stack.StackHeightMm:0} mm).");
                    if (parts.Any(p => p.Part.Floating)) also.Add("Parts in red start in mid-air: glue them on by eye.");
                    foreach (var line in also) { Text(c, line, Margin, top + 6, f, Muted); top += 15; }
                    top += 10;
                }
                using var lf = Font(11, bold: true);
                for (int k = 0; k < count; k++)
                {
                    var p = parts[from + k];
                    float x = Margin + k % cols * cell, y = top + k / cols * cellH;
                    using var frame = Stroke(Rule, 0.6f);
                    c.DrawRoundRect(new SKRect(x + 3, y + 3, x + cell - 3, y + cellH - 3), 6, 6, frame);
                    Flatly(c, p, new SKPoint(x + cell / 2, y + (cell - 4) / 2 + 3), listScale, p.Part.Floating ? Air : Wood, WoodEdge, labels: false);
                    Text(c, p.Label, x + cell / 2, y + cellH - 9, lf, Ink, SKTextAlign.Center);
                }
            });
            start += count;
        }

        // ---- steps: two to a page ----
        float half = (o.PageHeight - 2 * Margin - 10) / 2;
        for (int s = 1; s <= stack.Slices; s += 2)
        {
            int a = s, b = s + 1;
            Add(c =>
            {
                Step(c, new SKRect(Margin, Margin, o.PageWidth - Margin, Margin + half), a);
                if (b <= stack.Slices)
                {
                    using var rule = Stroke(Rule, 1);
                    c.DrawLine(Margin, Margin + half + 5, o.PageWidth - Margin, Margin + half + 5, rule);
                    Step(c, new SKRect(Margin, Margin + half + 10, o.PageWidth - Margin, o.PageHeight - Margin), b);
                }
            });
        }

        // ---- finished ----
        Add(c =>
        {
            using var head = Font(24, bold: true);
            Text(c, "Finished", Margin, Margin + 26, head, Ink);
            iso.Draw(c, new SKRect(Margin, Margin + 50, o.PageWidth - Margin, o.PageHeight - 200), stack.Slices, highlight: 0, pins: []);
            using var f = Font(11);
            string finish = o.Outline switch
            {
                SliceOutline.Outer => "The slices were cut to the outside of the shape: sand the steps back to the model's surface.",
                SliceOutline.Inner => "The slices were cut to the inside of the shape: fill the steps with wood filler and sand smooth.",
                _ => "Each slice is the model's shape half-way through it: sand the steps smooth, or leave them for the stepped look.",
            };
            Wrap(c, "Let the glue dry with a light weight on top. " + finish, Margin, o.PageHeight - 170, o.PageWidth - 2 * Margin, f, Ink);
        });
        total = pages.Count;
        return pages;

        void Step(SKCanvas c, SKRect r, int s)
        {
            var mine = parts.Where(p => p.Part.Slice == s).ToList();
            var pins = stack.Pins.Where(p => p.Slice == s).Select(p => p.At).ToList();
            using var num = Font(40, bold: true);
            Text(c, $"{s}", r.Left, r.Top + 38, num, Ink);
            // What to take for this step, in a box.
            float boxW = 190, boxTop = r.Top + 50;
            // Each part over its "1× 3a", in rows across the box: as big as fits, never wider than its label needs.
            using var lf = Font(11, bold: true);
            double inner = boxW - 20, fit = Math.Min(listScale * 1.6, 90 / Math.Max(1e-6, mine.Select(p => p.Box.Height).DefaultIfEmpty(1).Max()));
            fit = Math.Min(fit, inner / Math.Max(1e-6, mine.Select(p => p.Box.Width).DefaultIfEmpty(1).Max()));
            var slots = mine.Select(p => (Part: p, W: Math.Max(p.Box.Width * fit, lf.MeasureText($"1× {p.Label}")) + 8)).ToList();
            var rows = new List<List<(Flat Part, double W)>>();
            foreach (var slot in slots)
            {
                if (rows.Count == 0 || rows[^1].Sum(x => x.W) + slot.W > inner) rows.Add([]);
                rows[^1].Add(slot);
            }
            var rowH = rows.Select(row => (float)(row.Max(x => x.Part.Box.Height) * fit) + 24).ToList();
            float boxH = 16 + rowH.Sum() + (pins.Count > 0 ? 24 : 0);
            using var fill = Fill(Callout);
            using var edge = Stroke(CalloutEdge, 1);
            var box = new SKRect(r.Left, boxTop, r.Left + boxW, boxTop + boxH);
            c.DrawRoundRect(box, 8, 8, fill);
            c.DrawRoundRect(box, 8, 8, edge);
            float y = box.Top + 10;
            for (int i = 0; i < rows.Count; i++)
            {
                double x = box.Left + 10 + (inner - rows[i].Sum(q => q.W)) / 2;
                float tall = rowH[i] - 24;
                foreach (var (p, w) in rows[i])
                {
                    float cx = (float)(x + w / 2);
                    Flatly(c, p, new SKPoint(cx, y + tall / 2), fit, New, NewEdge, labels: false);
                    Text(c, $"1× {p.Label}", cx, y + tall + 16, lf, Ink, SKTextAlign.Center);
                    x += w;
                }
                y += rowH[i];
            }
            if (pins.Count > 0)
            {
                float py = box.Bottom - 16;
                using var pinPaint = Stroke(Pin, 5);
                pinPaint.StrokeCap = SKStrokeCap.Round;
                c.DrawLine(box.Left + 18, py, box.Left + 38, py, pinPaint);
                using var pf = Font(11, bold: true);
                Text(c, $"{pins.Count}× pin", box.Left + 46, py + 4, pf, Ink);
            }
            // Seen from above: the slice below in grey, the new parts in colour.
            float insetTop = box.Bottom + 10, insetH = r.Bottom - insetTop - 34;
            if (insetH > 50)
            {
                var inset = new SKRect(r.Left, insetTop, r.Left + boxW, insetTop + insetH);
                using var cap = Font(8.5f);
                Text(c, s == 1 ? "From above" : $"From above, on slice {s - 1}", inset.Left, inset.Top + 8, cap, Muted);
                var below = parts.Where(p => p.Part.Slice == s - 1).ToList();
                var all = below.Concat(mine).ToList();
                var bb = all.Select(p => p.Box).Aggregate(Rect2.Empty, (u, q) => u.Union(q));
                double k = Math.Min((inset.Width - 10) / Math.Max(1e-6, bb.Width), (inset.Height - 20) / Math.Max(1e-6, bb.Height));
                var centre = new SKPoint(inset.MidX, inset.Top + 14 + (inset.Height - 14) / 2);
                foreach (var p in below) Flatly(c, p, centre, k, new SKColor(0xEC, 0xEC, 0xEC), new SKColor(0xB0, 0xB0, 0xB0), labels: false, around: bb.Center);
                foreach (var p in mine) Flatly(c, p, centre, k, New, NewEdge, labels: true, around: bb.Center);
                using var dot = Fill(Pin);
                foreach (var at in pins)
                {
                    var q = new SKPoint(centre.X + (float)((at.X - bb.Center.X) * k), centre.Y + (float)((at.Y - bb.Center.Y) * k));
                    c.DrawCircle(q, Math.Max(1.6f, (float)(o.PinMm / 2 * k)), dot);
                }
            }
            // The stack so far.
            iso.Draw(c, new SKRect(r.Left + boxW + 16, r.Top + 6, r.Right, r.Bottom - 34), s, highlight: s, pins: pins, pinMm: o.PinMm);
            // What to do, in words.
            // (In words, 6 needs no dot: it's only there for reading the part upside down.)
            string names = Join(mine.Select(p => p.Label.TrimEnd('.')).ToList());
            string what = s == 1
                ? $"Lay {names} flat on the board, marks facing up."
                : $"Glue {names} onto slice {s - 1}{(o.PinMm > 0 && stack.Pins.Any(p => p.Slice == s - 1) ? ", over its pins," : "")} inside the marked line{(mine.Count > 1 ? "s" : "")}.";
            if (pins.Count > 0) what += $" Then push {pins.Count} pin{(pins.Count == 1 ? "" : "s")} into the holes, with a drop of glue.";
            var air = mine.Where(p => p.Part.Floating).Select(p => p.Label.TrimEnd('.')).ToList();
            if (air.Count > 0) what += $" {Join(air)} start{(air.Count == 1 ? "s" : "")} in mid-air: glue {(air.Count == 1 ? "it" : "them")} on by eye.";
            using var wf = Font(10.5f);
            Wrap(c, what, r.Left, r.Bottom - 22, r.Width, wf, Ink);
        }
    }

    static string PinLength(BookletOptions o, double t) => o.PinLengthMm is { } l ? $"{l:0.#} mm long" : $"up to {2 * t:0.#} mm long";

    static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "nothing",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>A part drawn flat (top view), centred on <paramref name="at"/> (or placed relative to <paramref name="around"/>).</summary>
    static void Flatly(SKCanvas c, Flat p, SKPoint at, double scale, SKColor fill, SKColor edge, bool labels, Vec2? around = null)
    {
        var centre = around ?? p.Box.Center;
        SKPoint P(Vec2 v) => new(at.X + (float)((v.X - centre.X) * scale), at.Y + (float)((v.Y - centre.Y) * scale));
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var l in p.Loops) path.AddPoly([.. l.Select(P)], true);
        using var f = Fill(fill);
        using var e = Stroke(edge, 0.8f);
        c.DrawPath(path, f);
        c.DrawPath(path, e);
        if (labels)
        {
            float size = (float)Math.Clamp(p.Part.LabelRoomMm * scale * 1.1, 6, 12);
            using var lf = Font(size, bold: true);
            var q = P(p.Part.LabelAt);
            Text(c, p.Label, q.X, q.Y + size * 0.35f, lf, Ink, SKTextAlign.Center);
        }
    }

    /// <summary>The stack seen from above and to one side, always from the same angle and at the same scale, so the steps line up.</summary>
    sealed class Iso
    {
        readonly List<Flat> _parts;
        readonly double _t, _cy = Math.Cos(-30 * Math.PI / 180), _sy = Math.Sin(-30 * Math.PI / 180), _cp = Math.Cos(32 * Math.PI / 180), _sp = Math.Sin(32 * Math.PI / 180);
        readonly Rect2 _box;

        public Iso(List<Flat> parts, double t, int slices)
        {
            _parts = parts;
            _t = t;
            var box = Rect2.Empty;
            foreach (var p in parts)
                foreach (var l in p.Loops)
                    foreach (var v in l)
                    {
                        box = box.Include(Screen(v, (p.Part.Slice - 1) * t));
                        box = box.Include(Screen(v, p.Part.Slice * t + t));
                    }
            _box = box;
        }

        (double X, double Y, double D) Project(double x, double y, double z)
        {
            double rx = x * _cy - y * _sy, ry = x * _sy + y * _cy;
            return (rx, ry * _sp - z * _cp, ry * _cp + z * _sp);
        }

        Vec2 Screen(Vec2 v, double z) { var (x, y, _) = Project(v.X, v.Y, z); return new(x, y); }

        public void Draw(SKCanvas c, SKRect r, int upTo, int highlight, IReadOnlyList<Vec2> pins, double pinMm = 0)
        {
            double k = Math.Min(r.Width / Math.Max(1e-6, _box.Width), r.Height / Math.Max(1e-6, _box.Height)) * 0.94;
            float ox = (float)(r.MidX - _box.Center.X * k), oy = (float)(r.MidY - _box.Center.Y * k);
            SKPoint P(Vec2 v, double z) { var s = Screen(v, z); return new(ox + (float)(s.X * k), oy + (float)(s.Y * k)); }
            // Coarser outlines where they're drawn small: half a point is as fine as print shows.
            double tol = 0.5 / k;
            for (int s = 1; s <= upTo; s++)
            {
                bool hot = s == highlight;
                double z0 = (s - 1) * _t, z1 = s * _t;
                var slice = _parts.Where(p => p.Part.Slice == s).ToList();
                // The walls facing this way, farthest first; then the tops, which nothing in the slice covers.
                var walls = new List<(double D, SKPoint[] Quad, float Light, bool Hot, bool Air)>();
                foreach (var p in slice)
                    foreach (var l in p.Loops)
                    {
                        var pts = Thin(l, tol);
                        for (int i = 0; i < pts.Count; i++)
                        {
                            var a = pts[i];
                            var b = pts[(i + 1) % pts.Count];
                            var d = b - a;
                            double len = d.Length;
                            if (len < 1e-9) continue;
                            double nx = d.Y / len, ny = -d.X / len;
                            double rnx = nx * _cy - ny * _sy, rny = nx * _sy + ny * _cy;
                            if (rny * _cp <= 0) continue;
                            var mid = Vec2.Lerp(a, b, 0.5);
                            float light = (float)Math.Clamp(0.62 - 0.28 * rnx, 0.35, 0.9);
                            walls.Add((Project(mid.X, mid.Y, z0).D, [P(a, z0), P(b, z0), P(b, z1), P(a, z1)], light, hot, p.Part.Floating));
                        }
                    }
                foreach (var w in walls.OrderBy(w => w.D))
                {
                    var baseColour = w.Air ? Air : w.Hot ? New : Wood;
                    using var f = Fill(Shade(baseColour, w.Light));
                    using var path = new SKPath();
                    path.AddPoly(w.Quad, true);
                    c.DrawPath(path, f);
                    using var e = Stroke(Shade(baseColour, w.Light * 0.92f), 0.4f);
                    c.DrawPath(path, e);
                }
                foreach (var p in slice)
                {
                    using var top = new SKPath { FillType = SKPathFillType.EvenOdd };
                    foreach (var l in p.Loops) top.AddPoly([.. Thin(l, tol).Select(v => P(v, z1))], true);
                    using var f = Fill(p.Part.Floating ? Air : hot ? New : Wood);
                    c.DrawPath(top, f);
                    using var e = Stroke(hot ? NewEdge : WoodEdge, hot ? 1.1f : 0.5f);
                    c.DrawPath(top, e);
                }
                if (!hot) continue;
                foreach (var p in slice)
                {
                    float size = (float)Math.Clamp(p.Part.LabelRoomMm * k * 1.1, 7, 16);
                    using var lf = Font(size, bold: true);
                    var q = P(p.Part.LabelAt, z1);
                    Text(c, p.Label, q.X, q.Y + size * 0.35f, lf, Ink, SKTextAlign.Center);
                }
                // The pins standing in the new slice's holes, ready for the next one.
                float rad = Math.Max(1.5f, (float)(pinMm / 2 * k));
                foreach (var at in pins.OrderBy(v => Project(v.X, v.Y, z1).D))
                {
                    var foot = P(at, z1);
                    var head = P(at, z1 + _t * 0.85);
                    using var body = Stroke(Shade(Pin, 0.8f), rad * 2);
                    c.DrawLine(foot, head, body);
                    using var cap = Fill(Pin);
                    c.DrawOval(head, new SKSize(rad, rad * (float)_sp), cap);
                }
            }
        }

        /// <summary>Points closer together than the tolerance merged, so small drawings stay light.</summary>
        static List<Vec2> Thin(List<Vec2> l, double tol)
        {
            var o = new List<Vec2>(l.Count) { l[0] };
            foreach (var v in l.Skip(1)) if (v.Dist(o[^1]) >= tol) o.Add(v);
            return o.Count >= 3 ? o : l;
        }
    }

    static SKColor Shade(SKColor c, float k) => new((byte)Math.Clamp(c.Red * k, 0, 255), (byte)Math.Clamp(c.Green * k, 0, 255), (byte)Math.Clamp(c.Blue * k, 0, 255));

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

    /// <summary>Text wrapped to a width, from the baseline at <paramref name="y"/> down.</summary>
    static void Wrap(SKCanvas c, string text, float x, float y, float width, SKPaint font, SKColor colour)
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
    }
}
