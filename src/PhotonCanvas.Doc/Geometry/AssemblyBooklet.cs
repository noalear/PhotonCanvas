using PhotonCanvas.Core;
using SkiaSharp;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>Which side of the model the booklet's pictures look at it from.</summary>
public enum BookletView
{
    /// <summary>The model's front: the side facing −Y in the file (as almost every 3D program saves it).</summary>
    Front,
    Right,
    Back,
    Left,
}

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
    /// <summary>
    /// The side the pictures look from. <see cref="TurnDeg"/> is how far the model was turned for
    /// slicing (<see cref="SliceOptions.TurnDeg"/>), so "front" stays the model's own front.
    /// </summary>
    public BookletView View { get; init; } = BookletView.Front;
    /// <summary>How far the model was turned for slicing (degrees).</summary>
    public double TurnDeg { get; init; }
    /// <summary>Page size in points (1/72 in): US Letter unless set. Everything stays inside A4 too.</summary>
    public float PageWidth { get; init; } = 612;
    public float PageHeight { get; init; } = 792;
}

/// <summary>
/// Assembly instructions for stacked slices, in the manner of a building-set manual: a cover with
/// the finished model, a list of every part, then one step per slice from the bottom up. Each step
/// shows the parts to add (with their labels), the stack so far with the new parts picked out in
/// colour, the pins that go in, and where the new parts sit seen from above. Parts with nothing
/// under them yet (a tail curling up off the ground, a chin) are built up off to the side, in
/// their own steps, and join the model when it reaches them. Saved as a PDF.
/// </summary>
public static class AssemblyBooklet
{
    const float Margin = 40;
    static readonly SKColor Ink = new(0x22, 0x22, 0x22), Muted = new(0x6B, 0x6B, 0x6B), Rule = new(0xD0, 0xD0, 0xD0);
    static readonly SKColor Wood = new(0xE6, 0xCF, 0xA8), WoodEdge = new(0x9C, 0x7A, 0x52);
    static readonly SKColor New = new(0xFF, 0xB2, 0x2E), NewEdge = new(0xB0, 0x5A, 0x00);
    static readonly SKColor Pin = new(0xD2, 0x2B, 0x3C), Callout = new(0xE3, 0xEE, 0xF8), CalloutEdge = new(0x9D, 0xB9, 0xD6);
    static readonly SKColor SideBg = new(0xF4, 0xF0, 0xFB), SideEdge = new(0xA8, 0x96, 0xD0);
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
    sealed record Flat(SlicePart Part, string Label, List<List<Vec2>> Loops, Rect2 Box)
    {
        /// <summary>Its label in words: the dot after 6 or 9 is only for reading the part upside down.</summary>
        public string Name => Label.TrimEnd('.');
        public int Slice => Part.Slice;
    }

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

    /// <summary>How much two parts of neighbouring slices overlap (mm²).</summary>
    static double Overlap(Flat a, Flat b)
    {
        if (!a.Box.Intersects(b.Box)) return 0;
        var both = ShapeGeometry.Boolean(a.Part.Outline, b.Part.Outline, BoolOp.Intersect);
        return both.IsEmpty ? 0 : Math.Abs(both.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Sum(f => Geom.SignedArea(f.Points)));
    }

    /// <summary>
    /// The order things go together, slice by slice from the bottom. A part goes on the model in its
    /// own slice's step when it rests on something already on the model. One that doesn't (the tip of
    /// a tail curling up off the ground, a chin) starts a side build, built up off to the side: the
    /// parts resting on it join it, and side builds that meet become one. A side build goes on the
    /// model in the first step where a part on the model sits on it: that part is glued onto it, and
    /// they go on together; from then on its parts above go on the model in their own steps. One the
    /// model never touches stays at its own steps, glued on by eye.
    /// </summary>
    sealed class Plan
    {
        public readonly Dictionary<Flat, int> StepOf = [];
        /// <summary>Each pin, and the part it rises from (null when no part holds it).</summary>
        public readonly List<(Vec2 At, int Slice, Flat? From)> Pins = [];
        /// <summary>The side builds, lettered A, B… in the order they start.</summary>
        public readonly List<Side> Sides = [];
        readonly Dictionary<Flat, Side> _sideOf = [];

        public Plan(List<Flat> parts, SliceStack stack)
        {
            var bySlice = parts.GroupBy(p => p.Slice).ToDictionary(g => g.Key, g => g.ToList());
            List<Flat> At(int s) => bySlice.TryGetValue(s, out var l) ? l : [];
            var overlaps = new Dictionary<(Flat, Flat), bool>();
            bool Sits(Flat upper, Flat lower) => overlaps.TryGetValue((upper, lower), out var v) ? v : overlaps[(upper, lower)] = Overlap(upper, lower) > 0.5;
            var on = new HashSet<Flat>();
            // Parts off the model so far, in groups that sit on each other (one group, one side build).
            var parent = new Dictionary<Flat, Flat>();
            Flat Find(Flat x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
            var joined = new List<(List<Flat> Members, List<Flat> Joiners, int Join)>();
            int top = parts.Max(p => p.Slice);
            for (int s = 1; s <= top; s++)
            {
                var left = At(s).ToList();
                for (bool changed = true; changed;)
                {
                    changed = false;
                    foreach (var p in left.ToList())
                        if (s == 1 || At(s - 1).Any(q => on.Contains(q) && Sits(p, q)))
                        {
                            on.Add(p);
                            StepOf[p] = s;
                            left.Remove(p);
                            changed = true;
                        }
                    // A side build that a part now on the model sits on goes on with it.
                    foreach (var group in parent.Keys.Where(q => !on.Contains(q)).GroupBy(Find).ToList())
                    {
                        var members = group.OrderBy(q => q.Slice).ThenBy(q => q.Label).ToList();
                        var joiners = At(s).Where(p => on.Contains(p) && members.Any(m => m.Slice == s - 1 && Sits(p, m))).ToList();
                        if (joiners.Count == 0) continue;
                        foreach (var m in members) { on.Add(m); StepOf[m] = s; }
                        joined.Add((members, joiners, s));
                        changed = true;
                    }
                }
                // What's left starts a side build, or joins the ones it sits on (joining them up).
                foreach (var p in left)
                {
                    parent[p] = p;
                    foreach (var q in At(s - 1).Where(q => parent.ContainsKey(q) && !on.Contains(q) && Sits(p, q))) parent[Find(q)] = Find(p);
                }
            }
            // Never touched by the model: glued on by eye, each at its own step.
            foreach (var p in parts.Where(p => !StepOf.ContainsKey(p))) StepOf[p] = p.Slice;
            foreach (var (j, i) in joined.OrderBy(j => j.Members[0].Slice).ThenBy(j => j.Join).Select((j, i) => (j, i)))
            {
                var side = new Side(Letters(i), j.Members, j.Joiners, j.Join);
                Sides.Add(side);
                foreach (var m in j.Members) _sideOf[m] = side;
            }
            foreach (var (s, at) in stack.Pins)
                Pins.Add((at, s, At(s).FirstOrDefault(p => p.Box.Contains(at) && p.Loops.Any(l => Geom.Inside(l, at)))));
        }

        /// <summary>Built in a side build (it doesn't rest on the model when its slice's step comes).</summary>
        public bool Hangs(Flat p) => _sideOf.ContainsKey(p);

        public Side? SideOf(Flat p) => _sideOf.GetValueOrDefault(p);
    }

    /// <summary>
    /// A side build: its parts (bottom first), built up off to the side in their own slices' steps,
    /// and the parts on the model glued onto it in step <see cref="Join"/>, where it goes on the model.
    /// </summary>
    sealed record Side(string Name, List<Flat> Members, List<Flat> Joiners, int Join);

    static List<Action<SKCanvas>> Pages(SliceStack stack, BookletOptions o)
    {
        if (stack.Method != SliceMethod.Stacked) throw new ArgumentException("The assembly booklet is for stacked slices.");
        if (stack.Parts.Count == 0) throw new ArgumentException("There are no parts to put together.");
        var parts = stack.Parts.Select(Prepare).ToList();
        double t = stack.ThicknessMm > 0 ? stack.ThicknessMm : stack.StackHeightMm / Math.Max(1, stack.Slices);
        // Seen from the chosen side, whichever way the model was turned for slicing.
        double turn = (int)o.View * 90 + o.TurnDeg;
        var plan = new Plan(parts, stack);
        var iso = new Iso(parts, t, turn);
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
            iso.Draw(c, new SKRect(Margin, Margin + 80, o.PageWidth - Margin, o.PageHeight - 190), parts, _ => false, []);
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
            Wrap(c, "Every part has its label lightly marked on its top face, and the outline of the part that goes on it. Build from step 1 upwards, marks facing up, one slice at a time." +
                    (plan.Sides.Count > 0 ? " Parts with nothing under them yet (a tail curling up, a chin) are built up off to the side, a \"side build\", and join the model when it reaches them." : ""),
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
                    if (parts.Any(p => p.Part.Floating && !plan.Hangs(p))) also.Add("Parts in red start in mid-air: glue them on by eye.");
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
                    Flatly(c, p, new SKPoint(x + cell / 2, y + (cell - 4) / 2 + 3), listScale, Floats(p) ? Air : Wood, WoodEdge, labels: false);
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
                if (b > stack.Slices) return;
                using var rule = Stroke(Rule, 1);
                c.DrawLine(Margin, Margin + half + 5, o.PageWidth - Margin, Margin + half + 5, rule);
                Step(c, new SKRect(Margin, Margin + half + 10, o.PageWidth - Margin, o.PageHeight - Margin), b);
            });
        }

        // ---- finished ----
        Add(c =>
        {
            using var head = Font(24, bold: true);
            Text(c, "Finished", Margin, Margin + 26, head, Ink);
            iso.Draw(c, new SKRect(Margin, Margin + 50, o.PageWidth - Margin, o.PageHeight - 200), parts, _ => false, []);
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

        // Red only for a part joined to nothing, below or above: it really is glued on by eye.
        bool Floats(Flat p) => p.Part.Floating && !plan.Hangs(p) && !plan.Sides.Any(g => g.Joiners.Contains(p));

        // The parts to take for a step, in a box: each over its "1× 3a", in rows, as big as fits.
        float PartsBox(SKCanvas c, float left, float top, float width, List<Flat> show, Func<Flat, string> caption, int pinCount, SKColor fill, SKColor edge)
        {
            using var lf = Font(11, bold: true);
            double inner = width - 20, fit = Math.Min(listScale * 1.6, 90 / Math.Max(1e-6, show.Select(p => p.Box.Height).DefaultIfEmpty(1).Max()));
            fit = Math.Min(fit, inner / Math.Max(1e-6, show.Select(p => p.Box.Width).DefaultIfEmpty(1).Max()));
            var rows = new List<List<(Flat Part, double W)>>();
            foreach (var p in show)
            {
                double w = Math.Max(p.Box.Width * fit, lf.MeasureText(caption(p))) + 8;
                if (rows.Count == 0 || rows[^1].Sum(x => x.W) + w > inner) rows.Add([]);
                rows[^1].Add((p, w));
            }
            var rowH = rows.Select(row => (float)(row.Max(x => x.Part.Box.Height) * fit) + 24).ToList();
            float boxH = 16 + rowH.Sum() + (pinCount > 0 ? 24 : 0);
            using var f = Fill(fill);
            using var e = Stroke(edge, 1);
            var box = new SKRect(left, top, left + width, top + boxH);
            c.DrawRoundRect(box, 8, 8, f);
            c.DrawRoundRect(box, 8, 8, e);
            float y = box.Top + 10;
            for (int i = 0; i < rows.Count; i++)
            {
                double x = box.Left + 10 + (inner - rows[i].Sum(q => q.W)) / 2;
                float tall = rowH[i] - 24;
                foreach (var (p, w) in rows[i])
                {
                    float cx = (float)(x + w / 2);
                    Flatly(c, p, new SKPoint(cx, y + tall / 2), fit, New, NewEdge, labels: false);
                    Text(c, caption(p), cx, y + tall + 16, lf, Ink, SKTextAlign.Center);
                    x += w;
                }
                y += rowH[i];
            }
            if (pinCount > 0)
            {
                float py = box.Bottom - 16;
                using var pinPaint = Stroke(Pin, 5);
                pinPaint.StrokeCap = SKStrokeCap.Round;
                c.DrawLine(box.Left + 18, py, box.Left + 38, py, pinPaint);
                using var pf = Font(11, bold: true);
                Text(c, $"{pinCount}× pin", box.Left + 46, py + 4, pf, Ink);
            }
            return box.Bottom;
        }

        void Step(SKCanvas c, SKRect r, int s)
        {
            // On the model now: this slice's parts that stand on it, and any side build joining it under one of them.
            var hot = parts.Where(p => plan.StepOf[p] == s).ToList();
            var main = parts.Where(p => p.Slice == s && !plan.Hangs(p)).ToList();
            var side = parts.Where(p => p.Slice == s && plan.Hangs(p)).ToList();
            var joining = plan.Sides.Where(g => g.Join == s).ToList();
            var active = plan.Sides.Where(g => g.Members[0].Slice <= s && s < g.Join).ToList();
            var pins = plan.Pins.Where(p => p.Slice == s && (p.From is null || !plan.Hangs(p.From))).ToList();
            var sidePins = plan.Pins.Where(p => p.Slice == s && p.From is { } f && plan.Hangs(f)).ToList();
            using var num = Font(40, bold: true);
            Text(c, $"{s}", r.Left, r.Top + 38, num, Ink);
            float boxW = 190;
            string Caption(Flat p) => plan.SideOf(p) is { } g ? $"1× {p.Label} (side {g.Name})"
                : joining.FirstOrDefault(g => g.Joiners.Contains(p)) is { Name: { } j } ? $"1× {p.Label} + side {j}" : $"1× {p.Label}";
            var take = main.Concat(side).ToList();
            float under = r.Top + 50;
            if (take.Count > 0) under = PartsBox(c, r.Left, r.Top + 50, boxW, take, Caption, pins.Count + sidePins.Count, Callout, CalloutEdge);

            // What to do, in words (first, so the pictures leave room for it).
            var what = new List<string>();
            foreach (var g in joining)
                what.Add($"Glue {Join([.. g.Joiners.Select(m => m.Name)])} onto side build {g.Name}, inside the marked line{(g.Joiners.Count > 1 ? "s" : "")} on {Join([.. g.Members.Where(m => m.Slice == s - 1).Select(m => m.Name)])}.");
            if (main.Count > 0)
            {
                var names = Join([.. main.Select(p => joining.FirstOrDefault(g => g.Joiners.Contains(p)) is { Name: { } j } ? $"{p.Name} (with side build {j} under it)" : p.Name)]);
                what.Add(s == 1
                    ? $"Lay {names} flat on the board, marks facing up."
                    : $"Glue {names} onto slice {s - 1}{(o.PinMm > 0 && plan.Pins.Any(p => p.Slice == s - 1 && (p.From is null || !plan.Hangs(p.From))) ? ", over its pins," : "")} inside the marked line{(main.Count > 1 ? "s" : "")}.");
                if (pins.Count > 0) what.Add($"Push {pins.Count} pin{(pins.Count == 1 ? "" : "s")} into the holes, with a drop of glue.");
                var air = main.Where(Floats).Select(p => p.Name).ToList();
                if (air.Count > 0) what.Add($"{Join(air)} start{(air.Count == 1 ? "s" : "")} in mid-air: glue {(air.Count == 1 ? "it" : "them")} on by eye.");
            }
            foreach (var g in active)
            {
                var now = side.Where(p => plan.SideOf(p) == g).Select(p => p.Name).ToList();
                if (now.Count == 0) continue;
                int gp = sidePins.Count(p => plan.SideOf(p.From!) == g);
                what.Add(g.Members[0].Slice == s
                    ? $"Side build {g.Name}: {Join(now)} {(now.Count == 1 ? "has" : "have")} nothing under {(now.Count == 1 ? "it" : "them")} yet, so start {(now.Count == 1 ? "it" : "them")} off to the side: lay {Join(now)} flat on the table, marks up. It joins the model in step {g.Join}."
                    : $"Side build {g.Name}: glue {Join(now)} onto it, inside the marked line{(now.Count > 1 ? "s" : "")}.");
                if (gp > 0) what.Add($"Push {gp} pin{(gp == 1 ? "" : "s")} into side build {g.Name}.");
            }
            using var wf = Font(10.5f);
            string words = string.Join(" ", what);
            float textH = Lines(words, r.Width, wf) * wf.TextSize * 1.3f + 8;
            float bottom = r.Bottom - textH;
            Wrap(c, words, r.Left, bottom + 14, r.Width, wf, Ink);

            // Seen from above: what's already there one level down in grey, what goes on the model now in colour.
            float insetTop = under + 10, insetH = bottom - insetTop - 6;
            var onModel = hot.Where(p => p.Slice >= s - 1).ToList();
            if (insetH > 50 && onModel.Count > 0)
            {
                var inset = new SKRect(r.Left, insetTop, r.Left + boxW, insetTop + insetH);
                using var cap = Font(8.5f);
                Text(c, s == 1 ? "From above" : $"From above, on slice {s - 1}", inset.Left, inset.Top + 8, cap, Muted);
                var below = parts.Where(p => p.Slice == s - 1 && plan.StepOf[p] < s).ToList();
                var bb = below.Concat(onModel).Select(p => p.Box).Aggregate(Rect2.Empty, (u, q) => u.Union(q));
                var spin = Turned(bb, turn);
                double k = Math.Min((inset.Width - 10) / Math.Max(1e-6, spin.Width), (inset.Height - 20) / Math.Max(1e-6, spin.Height));
                var centre = new SKPoint(inset.MidX, inset.Top + 14 + (inset.Height - 14) / 2);
                foreach (var p in below) Flatly(c, p, centre, k, new SKColor(0xEC, 0xEC, 0xEC), new SKColor(0xB0, 0xB0, 0xB0), labels: false, around: bb.Center, turnDeg: turn);
                foreach (var p in onModel.OrderBy(p => p.Slice)) Flatly(c, p, centre, k, New, NewEdge, labels: true, around: bb.Center, turnDeg: turn);
                using var dot = Fill(Pin);
                foreach (var pin in pins)
                {
                    var v = Spin(pin.At - bb.Center, turn);
                    c.DrawCircle(new SKPoint(centre.X + (float)(v.X * k), centre.Y + (float)(v.Y * k)), Math.Max(1.6f, (float)(o.PinMm / 2 * k)), dot);
                }
            }

            // The model so far, and beside it each side build so far (its new part in colour).
            var right = new SKRect(take.Count > 0 ? r.Left + boxW + 16 : r.Left, r.Top + 6, r.Right, bottom - 4);
            var shownSides = active.Take(3).ToList();
            if (shownSides.Count > 0)
            {
                float panelW = right.Width * 0.36f, panelH = right.Height / shownSides.Count;
                for (int i = 0; i < shownSides.Count; i++)
                {
                    var g = shownSides[i];
                    var panel = new SKRect(right.Right - panelW, right.Top + i * panelH, right.Right, right.Top + (i + 1) * panelH - 6);
                    using var bg = Fill(SideBg);
                    using var edge = Stroke(SideEdge, 1);
                    c.DrawRoundRect(panel, 8, 8, bg);
                    c.DrawRoundRect(panel, 8, 8, edge);
                    using var head = Font(10, bold: true);
                    Text(c, $"Side build {g.Name}", panel.Left + 8, panel.Top + 14, head, Ink);
                    using var note = Font(8);
                    Text(c, $"joins the model in step {g.Join}", panel.Left + 8, panel.Top + 25, note, Muted);
                    var view = new SKRect(panel.Left + 4, panel.Top + 28, panel.Right - 4, panel.Bottom - 4);
                    if (view.Height > 20)
                        new Iso(g.Members, t, turn).Draw(c, view, g.Members.Where(m => m.Slice <= s), m => m.Slice == s,
                            [.. sidePins.Where(p => plan.SideOf(p.From!) == g).Select(p => (p.At, p.Slice))], o.PinMm);
                }
                right = new SKRect(right.Left, right.Top, right.Right - panelW - 10, right.Bottom);
            }
            iso.Draw(c, right, parts.Where(p => plan.StepOf[p] <= s), p => plan.StepOf[p] == s, [.. pins.Select(p => (p.At, p.Slice))], o.PinMm);
        }
    }

    /// <summary>How many lines <see cref="Wrap"/> takes for this text.</summary>
    static int Lines(string text, float width, SKPaint font)
    {
        int lines = 0;
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(tryLine) > width && line.Length > 0) { lines++; line = word; }
            else line = tryLine;
        }
        return lines + (line.Length > 0 ? 1 : 0);
    }

    static string Letters(int k) => k < 26 ? $"{(char)('A' + k)}" : Letters(k / 26 - 1) + (char)('A' + k % 26);

    static string PinLength(BookletOptions o, double t) => o.PinLengthMm is { } l ? $"{l:0.#} mm long" : $"up to {2 * t:0.#} mm long";

    static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "nothing",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>A direction turned so the side the pictures look from is at the bottom of a view from above.</summary>
    static Vec2 Spin(Vec2 v, double turnDeg)
    {
        double a = turnDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    static Rect2 Turned(Rect2 box, double turnDeg) =>
        Rect2.Bounds(new[] { box.TopLeft, box.BottomRight, new Vec2(box.MinX, box.MaxY), new Vec2(box.MaxX, box.MinY) }.Select(v => Spin(v - box.Center, turnDeg)));

    /// <summary>A part drawn flat (top view), centred on <paramref name="at"/> (or placed relative to <paramref name="around"/>, turned to match the 3D pictures).</summary>
    static void Flatly(SKCanvas c, Flat p, SKPoint at, double scale, SKColor fill, SKColor edge, bool labels, Vec2? around = null, double turnDeg = 0)
    {
        var centre = around ?? p.Box.Center;
        SKPoint P(Vec2 v) { var q = Spin(v - centre, turnDeg); return new(at.X + (float)(q.X * scale), at.Y + (float)(q.Y * scale)); }
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var l in p.Loops) path.AddPoly([.. Thin(l, 0.4 / scale).Select(P)], true);
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

    /// <summary>
    /// A loop with the points that make no visible difference at this size dropped (within
    /// <paramref name="tol"/> of the line through their neighbours), so pages stay small.
    /// </summary>
    static List<Vec2> Thin(List<Vec2> l, double tol)
    {
        if (l.Count < 8) return l;
        var keep = new bool[l.Count];
        keep[0] = true;
        // The point farthest from the first splits the loop into two runs, each simplified on its own.
        int far = Enumerable.Range(1, l.Count - 1).MaxBy(i => l[i].Dist(l[0]));
        keep[far] = true;
        var stack = new Stack<(int A, int B)>([(0, far), (far, l.Count)]);
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var pa = l[a];
            var pb = l[b % l.Count];
            var d = pb - pa;
            double len = d.Length, best = -1;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double dist = len < 1e-12 ? l[i].Dist(pa) : Math.Abs((l[i] - pa).X * d.Y - (l[i] - pa).Y * d.X) / len;
                if (dist > best) { best = dist; at = i; }
            }
            if (at < 0 || best <= tol) continue;
            keep[at] = true;
            stack.Push((a, at));
            stack.Push((at, b));
        }
        var o = new List<Vec2>();
        for (int i = 0; i < l.Count; i++) if (keep[i]) o.Add(l[i]);
        return o.Count >= 3 ? o : l;
    }

    /// <summary>The stack seen from above and to one side, always from the same angle and at the same scale, so the steps line up.</summary>
    sealed class Iso
    {
        readonly double _t, _cy, _sy, _cp = Math.Cos(32 * Math.PI / 180), _sp = Math.Sin(32 * Math.PI / 180);
        readonly Rect2 _box;

        public Iso(IReadOnlyList<Flat> parts, double t, double turnDeg)
        {
            _t = t;
            // A three-quarter view: the chosen side towards you, turned a little to show the side beside it.
            double yaw = (turnDeg - 30) * Math.PI / 180;
            _cy = Math.Cos(yaw);
            _sy = Math.Sin(yaw);
            var box = Rect2.Empty;
            foreach (var p in parts)
                foreach (var l in p.Loops)
                    foreach (var v in l)
                    {
                        box = box.Include(Screen(v, (p.Slice - 1) * t));
                        box = box.Include(Screen(v, p.Slice * t + t));
                    }
            _box = box;
        }

        (double X, double Y, double D) Project(double x, double y, double z)
        {
            double rx = x * _cy - y * _sy, ry = x * _sy + y * _cy;
            return (rx, ry * _sp - z * _cp, ry * _cp + z * _sp);
        }

        Vec2 Screen(Vec2 v, double z) { var (x, y, _) = Project(v.X, v.Y, z); return new(x, y); }

        /// <summary>
        /// Draws <paramref name="shown"/>, bottom slice first, the <paramref name="hot"/> ones in colour
        /// with their labels, and pins standing in the holes of the slice each rises from.
        /// </summary>
        public void Draw(SKCanvas c, SKRect r, IEnumerable<Flat> shown, Func<Flat, bool> hot, IReadOnlyList<(Vec2 At, int Slice)> pins, double pinMm = 0, Func<Flat, bool>? ghost = null)
        {
            double k = Math.Min(r.Width / Math.Max(1e-6, _box.Width), r.Height / Math.Max(1e-6, _box.Height)) * 0.94;
            float ox = (float)(r.MidX - _box.Center.X * k), oy = (float)(r.MidY - _box.Center.Y * k);
            SKPoint P(Vec2 v, double z) { var s = Screen(v, z); return new(ox + (float)(s.X * k), oy + (float)(s.Y * k)); }
            // Coarser outlines where they're drawn small: half a point is as fine as print shows.
            double tol = 0.5 / k;
            foreach (var slice in shown.GroupBy(p => p.Slice).OrderBy(g => g.Key))
            {
                int s = slice.Key;
                double z0 = (s - 1) * _t, z1 = s * _t;
                var loops = slice.Select(p => (Part: p, Hot: hot(p), Loops: p.Loops.Select(l => Thin(l, tol)).ToList())).ToList();
                // See-through parts: faint faces, firm outlines.
                byte Alpha(Flat p) => ghost?.Invoke(p) == true ? (byte)70 : (byte)255;
                // The walls facing this way, farthest first; then the tops, which nothing in the slice covers.
                var walls = new List<(double D, SKPoint[] Quad, float Light, SKColor Colour)>();
                foreach (var (p, isHot, ls) in loops)
                {
                    var colour = (isHot ? New : Wood).WithAlpha(Alpha(p));
                    foreach (var pts in ls)
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
                            walls.Add((Project(mid.X, mid.Y, z0).D, [P(a, z0), P(b, z0), P(b, z1), P(a, z1)], light, colour));
                        }
                }
                foreach (var w in walls.OrderBy(w => w.D))
                {
                    using var f = Fill(Shade(w.Colour, w.Light).WithAlpha(w.Colour.Alpha));
                    using var path = new SKPath();
                    path.AddPoly(w.Quad, true);
                    c.DrawPath(path, f);
                }
                foreach (var (p, isHot, ls) in loops)
                {
                    using var top = new SKPath { FillType = SKPathFillType.EvenOdd };
                    foreach (var l in ls) top.AddPoly([.. l.Select(v => P(v, z1))], true);
                    using var f = Fill((isHot ? New : Wood).WithAlpha(Alpha(p)));
                    c.DrawPath(top, f);
                    using var e = Stroke(isHot ? NewEdge : WoodEdge, isHot ? 1.1f : 0.5f);
                    c.DrawPath(top, e);
                }
                foreach (var (p, _, _) in loops.Where(x => x.Hot))
                {
                    float size = (float)Math.Clamp(p.Part.LabelRoomMm * k * 1.1, 7, 16);
                    using var lf = Font(size, bold: true);
                    var q = P(p.Part.LabelAt, z1);
                    Text(c, p.Label, q.X, q.Y + size * 0.35f, lf, Ink, SKTextAlign.Center);
                }
                // The pins standing in this slice's holes, ready for the next one.
                float rad = Math.Max(1.5f, (float)(pinMm / 2 * k));
                foreach (var (at, _) in pins.Where(p => p.Slice == s).OrderBy(p => Project(p.At.X, p.At.Y, z1).D))
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
