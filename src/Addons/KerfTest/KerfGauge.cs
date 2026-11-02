using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.KerfTest;

/// <summary>What a kerf gauge is cut with and how big it is.</summary>
public sealed record KerfGaugeOptions
{
    public LaserSettings Laser { get; init; } = LaserSettings.Blue(100, 10);
    /// <summary>The material's thickness (mm): sets the fit slot's widths. Below <see cref="KerfGauge.MinEdgeThicknessMm"/> there's no fit slot.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>How long each tapered slot is (mm). Longer reads finer; shorter fits a smaller close-up view.</summary>
    public double TaperMm { get; init; } = 12;
    public double LabelMm { get; init; } = 1.4;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the gauge on the bed.</summary>
    public Vec2 Origin { get; init; } = new(10, 10);
    /// <summary>The library entry the cut settings came from, kept on the cut layer so the result can go back to it.</summary>
    public MaterialRef? Material { get; init; }
}

/// <summary>One tapered slot of the gauge, in bed millimetres: from its open mouth to its closed end.</summary>
public sealed record GaugeSlot(Vec2 Mouth, Vec2 End, double MouthWidthMm, double EndWidthMm)
{
    public double Length => Mouth.Dist(End);
    /// <summary>Unit vector from the mouth into the slot.</summary>
    public Vec2 Along => Length > 1e-9 ? (End - Mouth) / Length : new Vec2(1, 0);
    /// <summary>Unit vector across the slot (a quarter turn from <see cref="Along"/>).</summary>
    public Vec2 Across => new(-Along.Y, Along.X);
    /// <summary>The slot's drawn width this far in from the mouth.</summary>
    public double WidthAt(double depth) => MouthWidthMm + (EndWidthMm - MouthWidthMm) * Math.Clamp(depth / Math.Max(1e-9, Length), 0, 1);
    /// <summary>The point on the slot's middle line this far in.</summary>
    public Vec2 At(double depth) => Mouth + Along * depth;
    /// <summary>How far in a point is, along the slot (not clamped).</summary>
    public double DepthOf(Vec2 p) => (p - Mouth).Dot(Along);
    /// <summary>How far in the slot is this wide as drawn (not clamped: outside 0…Length means off the scale).</summary>
    public double DepthForWidth(double w) => Math.Abs(EndWidthMm - MouthWidthMm) < 1e-12 ? 0 : (w - MouthWidthMm) / (EndWidthMm - MouthWidthMm) * Length;
}

/// <summary>
/// A tapered-slot kerf gauge ("fits first time"). A window is cut in the stock with two slots
/// opening off its side, each narrowing steadily from its mouth to its closed end, and the keys to
/// try in them are cut inside the window (they drop out with it). Push a key into the top slot
/// lying flat until it stops: it stops where the slot, cut a kerf wider than drawn, is as wide as
/// the key, cut a kerf narrower. The slot narrows only 1.1 mm over its whole length, so each
/// 0.01 mm of kerf moves the key's tip about a tenth of a millimetre: easy to see on the marked
/// scale, and easy to find in a close-up photo. Push the other key standing on its edge into the
/// bottom slot and where it stops gives the slot width that grips the material's real thickness,
/// for slotted builds. Nothing here depends on a calibrated camera scale: the stock never moves
/// and the reading is a position along a slot whose ends are known.
/// </summary>
public sealed record KerfGauge(GaugeSlot Flat, GaugeSlot? Edge)
{
    public const string CutLayerName = "Kerf gauge";
    public const string MarksLayerName = "Kerf gauge marks";
    public const string WindowName = "Kerf gauge";
    public const string KeyName = "Kerf gauge key";

    /// <summary>The kerf the flat slot reads at its mouth and at its closed end (mm).</summary>
    public const double KerfAtMouth = -0.05, KerfAtEnd = 0.5;
    /// <summary>The fit slot is the thickness plus this at its mouth, and plus this at its closed end (mm).</summary>
    public const double FitAtMouth = 0.4, FitAtEnd = -1.0;
    /// <summary>Thinner than this, a key can't stand on its edge: no fit slot.</summary>
    public const double MinEdgeThicknessMm = 1.5;
    /// <summary>Taper lengths offered (mm).</summary>
    public const double MinTaperMm = 8, MaxTaperMm = 20;

    /// <summary>The key's drawn width: the flat slot is this wide where it reads a kerf of 0.</summary>
    public double KeyWidthMm => (Flat.MouthWidthMm + Flat.EndWidthMm) / 2 + (KerfAtMouth + KerfAtEnd);

    /// <summary>The thickness the gauge was drawn for (from the fit slot's widths); null without a fit slot.</summary>
    public double? ThicknessMm => Edge is { } e ? (e.MouthWidthMm + e.EndWidthMm) / 2 - (FitAtMouth + FitAtEnd) / 2 : null;

    /// <summary>
    /// The kerf when the flat key stops this far in: the slot there (cut a kerf wider) is as wide as
    /// the key (cut a kerf narrower), so key − 2·kerf = slot as drawn.
    /// </summary>
    public double KerfAt(double depth) => (KeyWidthMm - Flat.WidthAt(depth)) / 2;

    /// <summary>Where the flat key stops for a kerf (not clamped).</summary>
    public double DepthForKerf(double kerf) => Flat.DepthForWidth(KeyWidthMm - 2 * kerf);

    /// <summary>The fit slot's drawn width less the thickness, where the standing key stops (mm): the slot fit with no kerf on the cut layer.</summary>
    public double FitAt(double depth) => Edge is { } e && ThicknessMm is { } t ? e.WidthAt(depth) - t : double.NaN;

    /// <summary>Where the standing key stops for a fit (not clamped).</summary>
    public double DepthForFit(double fit) => Edge is { } e && ThicknessMm is { } t ? e.DepthForWidth(t + fit) : double.NaN;

    /// <summary>The cut layer, if the design has a gauge.</summary>
    public static Layer? CutLayer(DocState s) => s.Layers.FirstOrDefault(l => l.Name == CutLayerName);

    /// <summary>Is this one of the gauge's layers?</summary>
    public static bool IsGaugeLayer(Layer l) => l.Name is CutLayerName or MarksLayerName;

    /// <summary>The gauge in the design, read back from its window outline (wherever it has been moved or turned); null if there's none.</summary>
    public static KerfGauge? Find(DocState s)
    {
        if (CutLayer(s) is not { } cut) return null;
        var window = s.ShapesOn(cut.Id).OfType<PathShape>().FirstOrDefault(x => x.Name == WindowName);
        return window is null ? null : FromOutline(Points(window));
    }

    static List<Vec2> Points(PathShape shape)
    {
        var sub = shape.World.Subpaths.FirstOrDefault();
        if (sub is null) return [];
        var pts = new List<Vec2> { sub.Start };
        pts.AddRange(sub.Segs.Select(x => x.To));
        if (pts.Count > 1 && pts[^1].Dist(pts[0]) < 1e-6) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>The slots from the window outline's corners, in the order <see cref="Outline"/> draws them.</summary>
    public static KerfGauge? FromOutline(IReadOnlyList<Vec2> p)
    {
        if (p.Count is not (8 or 12)) return null;
        static GaugeSlot Slot(Vec2 a, Vec2 b, Vec2 c, Vec2 d) => new(Vec2.Lerp(a, d, 0.5), Vec2.Lerp(b, c, 0.5), a.Dist(d), b.Dist(c));
        var flat = Slot(p[2], p[3], p[4], p[5]);
        var edge = p.Count == 12 ? Slot(p[6], p[7], p[8], p[9]) : null;
        if (flat.Length < 1 || flat.EndWidthMm >= flat.MouthWidthMm) return null;
        if (edge is not null && (edge.Length < 1 || edge.EndWidthMm >= edge.MouthWidthMm)) return null;
        return new KerfGauge(flat, edge);
    }

    /// <summary>The key's width for a thickness: at least 4 mm, and taller than the material when it stands on its edge.</summary>
    public static double KeyWidth(double thicknessMm) => Math.Max(4, thicknessMm + 1);

    public static bool HasEdgeSlot(double thicknessMm) => thicknessMm >= MinEdgeThicknessMm;

    const double Margin = 1.5, KeyGap = 2, SlotGap = 3, SlotEdge = 1.2, TickGap = 0.5, MajorTick = 1.2, MinorTick = 0.6;

    sealed record Layout(double KeyW, double KeyL, double WindowW, double WindowH, double FlatY, double EdgeY, bool Edge, double L, double Thickness)
    {
        public double FlatMouth => KeyW - 2 * KerfAtMouth;
        public double FlatEnd => KeyW - 2 * KerfAtEnd;
        public double EdgeMouth => Thickness + FitAtMouth;
        public double EdgeEnd => Thickness + FitAtEnd;
        public int Keys => Edge ? 2 : 1;
        /// <summary>Room the scale above the flat slot and below the fit slot need, outside the window's height.</summary>
        public double Above(double label) => Math.Max(0, TickGap + MajorTick + 0.4 + label - (FlatY - FlatMouth / 2));
        public double Below(double label) => Edge ? Math.Max(0, EdgeY + EdgeMouth / 2 + TickGap + MajorTick + 0.4 + label - WindowH) : 0;
    }

    static Layout Plan(KerfGaugeOptions o)
    {
        double t = o.ThicknessMm, L = Math.Clamp(o.TaperMm, MinTaperMm, MaxTaperMm);
        bool edge = HasEdgeSlot(t);
        double kw = KeyWidth(t), kl = L + 5;
        double ww = kl + 2 * Margin;
        double flatMouth = kw - 2 * KerfAtMouth, edgeMouth = t + FitAtMouth;
        double flatY = SlotEdge + flatMouth / 2;
        double edgeY = flatY + flatMouth / 2 + SlotGap + edgeMouth / 2;
        double keysH = 2 * Margin + kw * (edge ? 2 : 1) + (edge ? KeyGap : 0);
        double slotsH = (edge ? edgeY + edgeMouth / 2 : flatY + flatMouth / 2) + SlotEdge;
        return new Layout(kw, kl, ww, Math.Max(keysH, slotsH), flatY, edgeY, edge, L, t);
    }

    /// <summary>The gauge's overall size (window, slots and scales), mm.</summary>
    public static Vec2 Size(KerfGaugeOptions o)
    {
        var p = Plan(o);
        return new Vec2(p.WindowW + p.L + 0.5, p.Above(o.LabelMm) + p.WindowH + p.Below(o.LabelMm));
    }

    /// <summary>The longest taper (whole mm, at least <see cref="MinTaperMm"/>) that fits in this much room; null if even the shortest doesn't.</summary>
    public static double? TaperToFit(KerfGaugeOptions o, Vec2 room)
    {
        for (double L = MaxTaperMm; L >= MinTaperMm; L -= 1)
        {
            var size = Size(o with { TaperMm = L });
            if (size.X <= room.X && size.Y <= room.Y) return L;
        }
        return null;
    }

    /// <summary>The window and its two slots, one closed outline (points in the order <see cref="FromOutline"/> reads them).</summary>
    static List<Vec2> Outline(Layout p, Vec2 at)
    {
        double x1 = p.WindowW, x2 = p.WindowW + p.L;
        var pts = new List<Vec2>
        {
            new(0, 0), new(x1, 0),
            new(x1, p.FlatY - p.FlatMouth / 2), new(x2, p.FlatY - p.FlatEnd / 2), new(x2, p.FlatY + p.FlatEnd / 2), new(x1, p.FlatY + p.FlatMouth / 2),
        };
        if (p.Edge)
            pts.AddRange([new(x1, p.EdgeY - p.EdgeMouth / 2), new(x2, p.EdgeY - p.EdgeEnd / 2), new(x2, p.EdgeY + p.EdgeEnd / 2), new(x1, p.EdgeY + p.EdgeMouth / 2)]);
        pts.AddRange([new(x1, p.WindowH), new(0, p.WindowH)]);
        return [.. pts.Select(v => v + at)];
    }

    /// <summary>The light marking layer: nowhere near cutting.</summary>
    static Layer Marks(int index, LaserSettings cut) => Layer.Default(index, LayerMode.Score) with
    {
        Name = MarksLayerName,
        Laser = cut.Light == LightSource.Red ? new LaserSettings { Light = LightSource.Red, Power = 20, Speed = 1000 } : LaserSettings.Blue(Math.Clamp(cut.Power * 0.12, 3, 15), 200),
    };

    static TextShape Text(string text, TextStyle style, Guid layer, Vec2 centre)
    {
        var t = TextShape.Create(text, style, layer, Affine.Identity);
        var b = t.Bounds;
        return b.IsEmpty ? t : (TextShape)t.With(transform: Affine.Translate(centre.X - b.Center.X, centre.Y - b.Center.Y));
    }

    /// <summary>".1", "0", "+.4", "−.2", "−1": short enough to sit between the ticks.</summary>
    public static string Short(double v, bool signed)
    {
        double r = Math.Round(v, 2);
        if (Math.Abs(r) < 1e-9) return "0";
        string digits = Math.Abs(r).ToString(Math.Abs(r) >= 1 ? "0.##" : ".##", System.Globalization.CultureInfo.InvariantCulture);
        return r < 0 ? "−" + digits : signed ? "+" + digits : digits;
    }

    /// <summary>
    /// Add the gauge: the marks layer first (before any cut layer already in the design: cut pieces
    /// drop), then the cut layer with no kerf allowance. Any gauge already in the design is replaced.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, KerfGaugeOptions o)
    {
        s = Remove(s);
        var p = Plan(o);
        // The origin is the top-left of everything: the window starts below the flat slot's scale.
        var at = o.Origin + new Vec2(0, p.Above(o.LabelMm));
        var cut = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = CutLayerName, Laser = o.Laser, KerfMm = 0, Material = o.Material };
        var marks = Marks(s.Layers.Count + 1, o.Laser);
        s = Ops.AddLayer(Ops.AddLayer(s, marks, BeforeCuts(s)), cut);
        var shapes = new List<Shape>();
        // Keys inside the window (cut first: inner outlines go before the outline around them).
        for (int i = 0; i < p.Keys; i++)
            shapes.Add(new PathShape { Name = KeyName, LayerId = cut.Id, Geometry = PathGeometry.Rect(at.X + Margin, at.Y + Margin + i * (p.KeyW + KeyGap), p.KeyL, p.KeyW) });
        shapes.Add(new PathShape { Name = WindowName, LayerId = cut.Id, Geometry = PathGeometry.Polyline(Outline(p, at), true) });

        // The scales: kerf above the flat slot, fit below the standing-key slot.
        var g = FromOutline(Outline(p, at))!;
        var style = new TextStyle(o.Font, o.LabelMm);
        var ticks = new List<Subpath>();
        void Scale(GaugeSlot slot, double side, IEnumerable<(double Depth, bool Major, string? Label)> marksAt)
        {
            foreach (var (depth, major, label) in marksAt)
            {
                if (depth < -1e-6 || depth > slot.Length + 1e-6) continue;
                var c = slot.At(depth);
                var from = c + slot.Across * side * (slot.WidthAt(depth) / 2 + TickGap);
                var to = from + slot.Across * side * (major ? MajorTick : MinorTick);
                ticks.Add(new Subpath(from, [Seg.Line(to)], false));
                if (label is not null) shapes.Add(Text(label, style, marks.Id, to + slot.Across * side * (0.4 + o.LabelMm / 2)));
            }
        }
        // Kerf every 0.05 mm, labelled every 0.1.
        Scale(g.Flat, -1, Enumerable.Range(0, 11).Select(i => i * 0.05).Select(k => (g.DepthForKerf(k), Math.Round(k * 100) % 10 == 0, Math.Round(k * 100) % 10 == 0 ? Short(k, false) : null)));
        if (g.Edge is not null)
        {
            // Fit every 0.1 mm, labelled every 0.4.
            var fits = Enumerable.Range(0, 15).Select(i => Math.Round(FitAtMouth - i * 0.1, 2));
            Scale(g.Edge, 1, fits.Select(f => (g.DepthForFit(f), Math.Round(f * 10) % 2 == 0, Math.Round(f * 10) % 4 == 0 ? Short(f, true) : null)));
        }
        shapes.Add(new PathShape { Name = "Kerf gauge scales", LayerId = marks.Id, Geometry = new PathGeometry(ticks) });
        return (Ops.AddShapes(s, shapes), shapes.Select(x => x.Id).ToList());
    }

    /// <summary>Where light marks go: before every cut layer already in the design.</summary>
    static int BeforeCuts(DocState s)
    {
        int first = s.Layers.FindIndex(l => l.Mode == LayerMode.Cut && l.Output);
        return first >= 0 ? first : s.Layers.Count;
    }

    /// <summary>The design without the gauge (its layers and shapes).</summary>
    public static DocState Remove(DocState s)
    {
        var gone = s.Layers.Where(IsGaugeLayer).Select(l => l.Id).ToHashSet();
        return gone.Count == 0 ? s : Ops.DeleteLayers(s, gone, null);
    }

    /// <summary>The bed area the gauge covers (window, slots and scales).</summary>
    public static Rect2 Area(DocState s)
    {
        var ids = s.Shapes.Where(x => s.Layers.FirstOrDefault(l => l.Id == x.LayerId) is { } l && IsGaugeLayer(l)).Select(x => x.Id).ToHashSet();
        return ids.Count == 0 ? Rect2.Empty : s.Bounds(ids);
    }
}

/// <summary>What was read from the gauge: where each key stopped, and what that means.</summary>
public sealed record KerfGaugeReading(double? FlatDepthMm, double? EdgeDepthMm)
{
    /// <summary>The kerf: what to set on a cut layer so parts drawn the same size press together snugly.</summary>
    public double? Kerf(KerfGauge g) => FlatDepthMm is { } d ? g.KerfAt(d) : null;

    /// <summary>The fit slot's reading as drawn (slot = thickness + this, with no kerf on the layer).</summary>
    public double? RawFit(KerfGauge g) => EdgeDepthMm is { } d && g.Edge is not null ? g.FitAt(d) : null;

    /// <summary>
    /// The slot fit for slotted builds with the measured kerf on the cut layer (slots then come out
    /// as drawn): the slot that gripped was drawn thickness + raw fit and came out a kerf wider.
    /// </summary>
    public double? SlotFit(KerfGauge g) => RawFit(g) is { } f ? f + (Kerf(g) ?? 0) : null;

    /// <summary>The material's real thickness, as the standing key felt it: the slot it stopped in, as cut.</summary>
    public double? Thickness(KerfGauge g) => EdgeDepthMm is { } d && g.Edge is { } e && Kerf(g) is { } k ? e.WidthAt(d) + k : null;
}

/// <summary>Where a key's tip was found along a slot in a photo, and how sure that is.</summary>
public sealed record TipFind(double DepthMm, double Contrast, double Confidence)
{
    /// <summary>Clear enough to trust without a look.</summary>
    public bool Sure => Confidence >= KerfGaugePhoto.SureConfidence;
}

/// <summary>
/// Finding where the keys stopped in a photo of the gauge. Along each slot's middle line, a band of
/// the photo is averaged across the slot (well inside its walls); the key fills the slot from the
/// mouth to its tip and the slot is open (showing whatever is underneath) beyond. The tip is where
/// one even stretch changes to another: the split that leaves the least colour variation on each
/// side. Works with any photo whose mapping from bed millimetres to pixels is known: a close-up
/// camera photo laid on the bed, or any photo once two points on it have been clicked.
/// </summary>
public static class KerfGaugePhoto
{
    /// <summary>Contrast over noise above which a find is trusted.</summary>
    public const double SureConfidence = 2.5;
    /// <summary>The tip isn't looked for this close to either end of the slot (mm).</summary>
    public const double EndMarginMm = 0.3;

    /// <summary>Find a key's tip along a slot. <paramref name="mmToPx"/> maps bed mm to photo pixels. Null when the slot isn't in the photo.</summary>
    public static TipFind? FindTip(RasterImage img, Func<Vec2, Vec2> mmToPx, GaugeSlot slot)
    {
        // Sample about every half pixel along, every pixel across.
        double pxPerMm = mmToPx(slot.Mouth).Dist(mmToPx(slot.Mouth + slot.Along));
        if (!(pxPerMm > 1)) return null;
        double step = Math.Clamp(0.5 / pxPerMm, 0.01, 0.1);
        double start = EndMarginMm, stop = slot.Length - EndMarginMm;
        int n = (int)Math.Floor((stop - start) / step) + 1;
        if (n < 8) return null;
        (double R, double G, double B)? Band(double depth, double half)
        {
            int across = Math.Max(3, (int)Math.Ceiling(2 * half * pxPerMm));
            double sr = 0, sg = 0, sb = 0;
            for (int j = 0; j < across; j++)
            {
                double u = -half + 2 * half * (j + 0.5) / across;
                if (Sample(img, mmToPx(slot.At(depth) + slot.Across * u)) is not { } c) return null;
                sr += c.R; sg += c.G; sb += c.B;
            }
            return (sr / across, sg / across, sb / across);
        }
        var bands = new (double R, double G, double B)[n];
        for (int i = 0; i < n; i++)
        {
            if (Band(start + i * step, slot.WidthAt(start + i * step) * 0.3) is not { } c) return null;
            bands[i] = c;
        }
        // The key sticks out of the mouth into the window: what it looks like there is what to look for in the slot.
        // Each stretch of the slot is then near that (the key) or far from it (the open slot, whatever is under it).
        double sumR = 0, sumG = 0, sumB = 0; int seen = 0;
        for (double d = -2; d <= -0.4; d += step)
            if (Band(d, slot.MouthWidthMm * 0.3) is { } c) { sumR += c.R; sumG += c.G; sumB += c.B; seen++; }
        if (seen >= 3)
        {
            var key = (R: sumR / seen, G: sumG / seen, B: sumB / seen);
            var far = bands.Select(c => Math.Sqrt((c.R - key.R) * (c.R - key.R) + (c.G - key.G) * (c.G - key.G) + (c.B - key.B) * (c.B - key.B))).ToArray();
            var (at, near, away, spread) = Rise(far, Math.Max(2, (int)Math.Ceiling(0.3 / step)));
            var byKey = at < 0 ? null : new TipFind(start + (at - 0.5) * step, away - near, (away - near) / (spread + 2));
            if (byKey is { Sure: true }) return byKey;
            var plain = Plain();
            return plain is not null && (byKey is null || plain.Confidence > byKey.Confidence) ? plain : byKey;
        }
        // No key showing at the mouth (off the photo): the plain split into two even stretches.
        return Plain();

        TipFind? Plain()
        {
            var grey = bands.Select(c => (c.R + c.G + c.B) / 3).ToArray();
            var (k, contrast, noise) = Step(grey, Math.Max(2, (int)Math.Ceiling(0.3 / step)));
            return k < 0 ? null : new TipFind(start + (k - 0.5) * step, contrast, contrast / (noise + 2));
        }
    }

    /// <summary>The best split of a run of values into a low stretch then a high one: where, the two levels, and the spread left.</summary>
    static (int At, double Low, double High, double Noise) Rise(double[] v, int minRun)
    {
        int n = v.Length;
        var s1 = new double[n + 1];
        var s2 = new double[n + 1];
        for (int i = 0; i < n; i++) { s1[i + 1] = s1[i] + v[i]; s2[i + 1] = s2[i] + v[i] * v[i]; }
        int best = -1; double bestSse = double.PositiveInfinity, low = 0, high = 0;
        for (int k = minRun; k <= n - minRun; k++)
        {
            double ma = s1[k] / k, mb = (s1[n] - s1[k]) / (n - k);
            if (mb <= ma) continue;
            double e = s2[k] - k * ma * ma + (s2[n] - s2[k]) - (n - k) * mb * mb;
            if (e < bestSse) { bestSse = e; best = k; low = ma; high = mb; }
        }
        return (best, low, high, Math.Sqrt(Math.Max(0, bestSse) / Math.Max(1, n)));
    }

    /// <summary>
    /// How far along the flat slot the window's edge really is from where the map puts the mouth
    /// (mm; + = further in). The stock beside the mouth is light or dark against the open window
    /// next to it; that edge is found on lines across it, between the slots (or below the only one).
    /// Corrects a camera position that's off a little, or a mouth clicked a little wide, before the
    /// tips are read from it. Null when the edge isn't clear.
    /// </summary>
    public static double? MouthShift(RasterImage img, Func<Vec2, Vec2> mmToPx, KerfGauge g)
    {
        var slot = g.Flat;
        double pxPerMm = mmToPx(slot.Mouth).Dist(mmToPx(slot.Mouth + slot.Along));
        if (!(pxPerMm > 1)) return null;
        // The stretch of window edge beside the mouth: from just past the flat slot's side to just before the fit slot's (or 2 mm on).
        double from = slot.MouthWidthMm / 2 + 0.4;
        double to = g.Edge is { } e ? (e.Mouth - slot.Mouth).Dot(slot.Across) - e.MouthWidthMm / 2 - 0.4 : from + 2;
        if (to - from < 0.4) return null;
        double step = Math.Clamp(0.5 / pxPerMm, 0.01, 0.1), reach = 1.5;
        int n = (int)Math.Floor(2 * reach / step) + 1;
        int lines = Math.Max(3, (int)Math.Ceiling((to - from) * pxPerMm));
        var v = new double[n];
        for (int i = 0; i < n; i++)
        {
            double along = -reach + i * step, sum = 0;
            for (int j = 0; j < lines; j++)
            {
                double u = from + (to - from) * (j + 0.5) / lines;
                if (Sample(img, mmToPx(slot.Mouth + slot.Along * along + slot.Across * u)) is not { } c) return null;
                sum += (c.R + c.G + c.B) / 3;
            }
            v[i] = sum / lines;
        }
        var (at, contrast, noise) = Step(v, Math.Max(2, (int)Math.Ceiling(0.3 / step)));
        if (at < 0 || contrast < 12 || contrast / (noise + 2) < SureConfidence) return null;
        return -reach + (at - 0.5) * step;
    }

    /// <summary>The best split of a run of values into two even stretches: where, how different, and the spread left.</summary>
    static (int At, double Contrast, double Noise) Step(double[] v, int minRun)
    {
        int n = v.Length;
        var s1 = new double[n + 1];
        var s2 = new double[n + 1];
        for (int i = 0; i < n; i++) { s1[i + 1] = s1[i] + v[i]; s2[i + 1] = s2[i] + v[i] * v[i]; }
        double Sse(int a, int b, out double m) { m = (s1[b] - s1[a]) / (b - a); return s2[b] - s2[a] - (b - a) * m * m; }
        int best = -1; double bestSse = double.PositiveInfinity, contrast = 0;
        for (int k = minRun; k <= n - minRun; k++)
        {
            double e = Sse(0, k, out var ma) + Sse(k, n, out var mb);
            if (e < bestSse) { bestSse = e; best = k; contrast = Math.Abs(ma - mb); }
        }
        return (best, contrast, Math.Sqrt(Math.Max(0, bestSse) / Math.Max(1, n)));
    }

    /// <summary>
    /// The gauge's part of the photo, laid straight: <paramref name="area"/> of the bed (mm) at
    /// <paramref name="pxPerMm"/>, sampled from the photo through the map (grey where the photo doesn't reach).
    /// </summary>
    public static RasterImage Resample(RasterImage img, Func<Vec2, Vec2> mmToPx, Rect2 area, double pxPerMm)
    {
        int w = Math.Clamp((int)Math.Ceiling(area.Width * pxPerMm), 1, 4000), h = Math.Clamp((int)Math.Ceiling(area.Height * pxPerMm), 1, 4000);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Sample(img, mmToPx(new Vec2(area.MinX + (x + 0.5) / pxPerMm, area.MinY + (y + 0.5) / pxPerMm))) ?? (128, 128, 128);
                int i = (y * w + x) * 4;
                px[i] = (byte)Math.Round(c.R); px[i + 1] = (byte)Math.Round(c.G); px[i + 2] = (byte)Math.Round(c.B); px[i + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    /// <summary>Bilinear colour at a pixel position; null outside the picture.</summary>
    static (double R, double G, double B)? Sample(RasterImage img, Vec2 p)
    {
        double x = p.X - 0.5, y = p.Y - 0.5;
        if (x < 0 || y < 0 || x > img.Width - 1 || y > img.Height - 1) return null;
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, img.Width - 1), y1 = Math.Min(y0 + 1, img.Height - 1);
        double fx = x - x0, fy = y - y0;
        var px = img.Rgba;
        double Ch(int c)
        {
            double a = px[(y0 * img.Width + x0) * 4 + c], bb = px[(y0 * img.Width + x1) * 4 + c];
            double cc = px[(y1 * img.Width + x0) * 4 + c], d = px[(y1 * img.Width + x1) * 4 + c];
            return (a * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy;
        }
        return (Ch(0), Ch(1), Ch(2));
    }

    /// <summary>The map from bed mm to a close-up photo laid on the bed (its area and scale).</summary>
    public static Func<Vec2, Vec2> OnBed(Rect2 area, double mmPerPx) => mm => new Vec2((mm.X - area.MinX) / mmPerPx, (mm.Y - area.MinY) / mmPerPx);

    /// <summary>
    /// The map from bed mm to any photo, from two points clicked on it: the flat slot's mouth and
    /// its closed end (a turn, a size and a shift; the photo should be taken square on).
    /// The photo may be mirrored by nothing; null when the clicks coincide.
    /// </summary>
    public static Func<Vec2, Vec2>? FromTwoPoints(Vec2 mmA, Vec2 mmB, Vec2 pxA, Vec2 pxB)
    {
        var dm = mmB - mmA; var dp = pxB - pxA;
        double lm = dm.Length, lp = dp.Length;
        if (lm < 1e-9 || lp < 1e-6) return null;
        double scale = lp / lm;
        double angle = Math.Atan2(dp.Y, dp.X) - Math.Atan2(dm.Y, dm.X);
        double c = Math.Cos(angle) * scale, sn = Math.Sin(angle) * scale;
        return mm =>
        {
            var v = mm - mmA;
            return pxA + new Vec2(c * v.X - sn * v.Y, sn * v.X + c * v.Y);
        };
    }
}

/// <summary>Putting a gauge's reading into the material library.</summary>
public static class KerfGaugeLibrary
{
    /// <summary>
    /// The material with the reading saved: the recipe the gauge was cut from gets the kerf (and the
    /// slot fit, when the fit slot was read); with no such recipe on this material, a new tested cut
    /// recipe is added with the gauge's cut settings. Returns the material and the recipe written.
    /// </summary>
    public static (Material Material, Recipe Recipe) Save(Material m, Layer cut, double kerfMm, double? slotFitMm, double? thicknessMm, double? nominalMm, DateTime when)
    {
        var notes = new List<string> { $"Kerf gauge {when:yyyy-MM-dd}: kerf {kerfMm:0.000} mm" };
        if (slotFitMm is { } f) notes.Add($"slot fit {f:+0.00;−0.00;0.00} mm with that kerf on the cut layer");
        if (thicknessMm is { } t) notes.Add($"the sheet is really {t:0.00} mm" + (nominalMm is { } n ? $" (sold as {n:0.##})" : ""));
        string note = string.Join(", ", notes) + ".";
        var source = cut.Material is { RecipeId: { } rid } r && r.MaterialId == m.Id ? m.Recipes.FirstOrDefault(x => x.Id == rid) : null;
        Recipe written;
        if (source is not null)
        {
            written = source with
            {
                Settings = source.Settings with { KerfMm = kerfMm },
                SlotFitMm = slotFitMm ?? source.SlotFitMm,
                Tested = true,
                TestedOn = when,
                Notes = string.IsNullOrWhiteSpace(source.Notes) ? note : source.Notes.TrimEnd() + " " + note,
                Updated = when,
            };
            m = m with { Recipes = m.Recipes.Replace(source, written) };
        }
        else
        {
            var l = cut.Laser;
            written = new Recipe
            {
                Purpose = RecipePurpose.Cut,
                Title = ((nominalMm ?? m.ThicknessMm) is { } th ? $"Cuts {th:0.##} mm: " : "Cuts: ") + $"{l.Speed:0.#} mm/s × {l.Passes}, kerf {kerfMm:0.00}",
                Settings = ProcessSettings.From(cut with { Mode = LayerMode.Cut, KerfMm = kerfMm, Material = null }),
                Tested = true,
                TestedOn = when,
                SlotFitMm = slotFitMm,
                Notes = note,
                Updated = when,
            };
            m = m with { Recipes = m.Recipes.Add(written) };
        }
        if (m.ThicknessMm is null && (nominalMm ?? thicknessMm) is { } keep) m = m with { ThicknessMm = keep };
        return (m, written);
    }
}
