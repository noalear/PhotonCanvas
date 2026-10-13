using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

public sealed record JobOptions
{
    /// <summary>
    /// True: each pass runs over the whole job (with <see cref="PassDelayS"/> between).
    /// False: stock behaviour, each path repeats its passes before the next starts.
    /// </summary>
    public bool WholeJobPasses { get; init; } = true;
    /// <summary>Pause between whole-job passes, in seconds.</summary>
    public double PassDelayS { get; init; }
    /// <summary>Maximum deviation of the cut path from the true curve, in mm.</summary>
    public double Tolerance { get; init; } = 0.01;
    public MachineProfile Machine { get; init; } = MachineProfile.F2Ultra;
}

/// <summary>A job ready to send, plus everything the UI shows about it.</summary>
public sealed record BuiltJob(
    Job Job,
    string Motion,
    string Border,
    byte[] Xf,
    double EstimateS,
    Rect2 Bounds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<BuiltJob.Step> Steps)
{
    /// <summary>One processed path, in order, for the preview.</summary>
    public sealed record Step(Guid LayerId, List<Vec2> Points, bool Closed, bool Fill);
}

public static class JobBuilder
{
    /// <summary>Layers that will be processed, in order.</summary>
    public static IEnumerable<Layer> OutputLayers(DocState s) => s.Layers.Where(l => l.Output && l.Visible);

    /// <exception cref="JobException">The job cannot be made; the message says why.</exception>
    public static BuiltJob Build(DocState s, JobOptions opt, DateTimeOffset? now = null)
    {
        var warnings = new List<string>();
        var blocks = new List<Block>();
        var steps = new List<BuiltJob.Step>();
        var pos = new Vec2(0, 0);
        var bounds = Rect2.Empty;
        var layers = OutputLayers(s).ToList();

        foreach (var hidden in s.Layers.Where(l => l.Output && !l.Visible && s.ShapesOn(l.Id).Any()))
            warnings.Add($"Layer \"{hidden.Name}\" is hidden, so it will not be processed.");

        // Cutting before engraving lets parts shift or drop before they are engraved.
        int firstCut = layers.FindIndex(l => l.Mode == LayerMode.Cut && s.ShapesOn(l.Id).Any());
        if (firstCut >= 0)
            foreach (var later in layers.Skip(firstCut + 1).Where(l => l.Mode != LayerMode.Cut && s.ShapesOn(l.Id).Any()))
                warnings.Add($"\"{later.Name}\" runs after the cut layer \"{layers[firstCut].Name}\". Parts may move or drop before it is processed; consider moving it above.");

        foreach (var layer in layers)
        {
            var onLayer = s.ShapesOn(layer.Id).ToList();
            if (onLayer.Count == 0) continue;
            try
            {
                layer.Laser.Validate(opt.Machine);
                ValidateExtras(layer);
            }
            catch (JobException e) { throw new JobException($"Layer \"{layer.Name}\": {e.Message}"); }

            var images = onLayer.OfType<ImageShape>().ToList();
            var shapes = onLayer.OfType<PathShape>().ToList();
            if (layer.Mode == LayerMode.Image)
            {
                if (shapes.Count > 0)
                    warnings.Add($"Layer \"{layer.Name}\" is an Image layer, so its {shapes.Count} vector shape(s) were skipped. Move them to a Cut, Score or Fill layer.");
                foreach (var img in images)
                {
                    var (block, area) = Rasterize(img, layer);
                    if (block is null) continue;
                    blocks.Add(block);
                    steps.Add(new(layer.Id, [area.TopLeft, new(area.MaxX, area.MinY), area.BottomRight, new(area.MinX, area.MaxY)], true, true));
                    bounds = bounds.Union(area);
                }
                continue;
            }
            if (images.Count > 0)
                warnings.Add($"Layer \"{layer.Name}\" is a {layer.Mode} layer, so its {images.Count} image(s) were skipped. Put images on an Image layer to engrave them, or trace them into vectors.");
            if (shapes.Count == 0) continue;

            if (layer.Mode == LayerMode.Fill)
            {
                int open = 0;
                foreach (var shape in shapes)
                {
                    var polys = shape.World.Flatten(opt.Tolerance).Where(f => f.Closed && f.Points.Count >= 3).ToList();
                    open += shape.World.Subpaths.Count(sp => !sp.Closed);
                    if (polys.Count == 0) continue;
                    var block = new FillBlock(polys.Select(p => (IReadOnlyList<Vec2>)p.Points).ToList(), layer.Laser, layer.LinesPerCm)
                    {
                        AngleDeg = layer.FillAngleDeg,
                        OverscanMm = layer.OverscanMm,
                    };
                    var cross = layer.Crosshatch ? block with { AngleDeg = layer.FillAngleDeg + 90 } : null;
                    bool first = Job.FillLines(block).Count > 0, second = cross is not null && Job.FillLines(cross).Count > 0;
                    if (!first && !second) continue;
                    if (first) blocks.Add(block);
                    if (second) blocks.Add(cross!);
                    foreach (var p in polys)
                    {
                        steps.Add(new(layer.Id, p.Points, true, true));
                        bounds = bounds.Union(Rect2.Bounds(p.Points));
                    }
                }
                if (open > 0)
                    warnings.Add($"Layer \"{layer.Name}\": {open} open path(s) can't be filled and were skipped. Close them, or put them on a Cut/Score layer.");
            }
            else
            {
                var paths = layer.KerfMm != 0
                    ? ApplyKerf(shapes, layer, opt.Tolerance, warnings)
                    : shapes.SelectMany(sh => sh.World.Flatten(opt.Tolerance)).Where(f => f.Points.Count >= 2).ToList();
                var extras = new CutExtras(layer);
                foreach (var (path, depth) in Order(paths, ref pos))
                    foreach (var p in extras.Pieces(path, depth))
                    {
                        var pl = p.Closed ? Polyline.Loop(p.Points) : Polyline.Open(p.Points);
                        if (!pl.HasExtent) continue;
                        blocks.Add(new VectorBlock(pl, layer.Laser));
                        steps.Add(new(layer.Id, p.Points, p.Closed, false));
                        bounds = bounds.Union(Rect2.Bounds(p.Points));
                    }
                extras.Warn(warnings);
            }
        }

        if (blocks.Count == 0)
            throw new JobException(s.Shapes.Count == 0
                ? "There is nothing on the canvas to process."
                : "No shapes are on an enabled, visible layer.");

        var job = new Job(opt.Machine, blocks)
        {
            PassMode = opt.WholeJobPasses ? new PassMode.Rounds(opt.PassDelayS) : new PassMode.PerBlock(),
            Created = (now ?? DateTimeOffset.Now).ToString("yyyy_MM_dd_HH_mm_ss"),
        };
        var motion = job.Render();
        Vec2[] frame = [bounds.TopLeft, new(bounds.MaxX, bounds.MinY), bounds.BottomRight, new(bounds.MinX, bounds.MaxY), bounds.TopLeft];
        var border = job.Border(frame);

        var lights = blocks.Select(b => b.Laser.Light).Distinct().ToList();
        var light = lights.Count > 1 ? "mixed" : lights[0].Wire();
        var prms = new JsonArray();
        foreach (var l in layers.Where(l => s.ShapesOn(l.Id).Any()))
            prms.Add(new JsonObject
            {
                ["processingType"] = l.Mode switch
                {
                    LayerMode.Fill => "FILL_VECTOR_ENGRAVING",
                    LayerMode.Score => "VECTOR_ENGRAVING",
                    LayerMode.Image => "BITMAP_ENGRAVING",
                    _ => "VECTOR_CUTTING",
                },
                ["processingLightSource"] = l.Laser.Light.Wire(),
                ["power"] = l.Laser.Power,
                ["speed"] = l.Laser.Speed,
                ["repeat"] = l.Laser.Passes,
                ["isFill"] = l.Mode == LayerMode.Fill,
            });
        var xf = XfPackage.Build(new()
        {
            Motion = motion,
            Border = border,
            PreviewJpeg = XfPackage.PreviewJpeg(steps.Select(st => (IReadOnlyList<Vec2>)(st.Closed ? [.. st.Points, st.Points[0]] : st.Points))),
            Light = light,
            Params = prms,
            Created = now ?? DateTimeOffset.Now,
        });
        return new BuiltJob(job, motion, border, xf, job.EstimateSeconds(), bounds, warnings, steps);
    }

    /// <summary>Most dots a single image may produce (keeps files and memory sane).</summary>
    const long MaxImageCells = 40_000_000;

    /// <summary>
    /// Resample an image onto the machine's dot grid (axis-aligned, so rotated
    /// images work), apply its tone settings and dither it. Returns the dot
    /// block and the area it covers, or null when nothing would burn.
    /// </summary>
    public static (DotBlock? Block, Rect2 Area) Rasterize(ImageShape img, Layer layer)
    {
        JobException.CheckRange("resolution", layer.Dpi, 10, 2540, "dpi");
        double pitch = 25.4 / layer.Dpi;
        var b = img.Bounds;
        int cols = Math.Max(1, (int)Math.Floor(b.Width / pitch + 1e-9));
        int rows = Math.Max(1, (int)Math.Floor(b.Height / pitch + 1e-9));
        if ((long)cols * rows > MaxImageCells)
            throw new JobException($"Image \"{img.Name}\" at {layer.Dpi:0} dpi needs {(long)cols * rows / 1e6:0.#} million dots. Lower the resolution or make it smaller.");
        if (!img.Transform.IsInvertible) return (null, b);
        var toLocal = img.Transform.Invert();
        var origin = new Vec2(b.MinX + pitch / 2, b.MinY + pitch / 2);
        var tone = img.Tone;
        var grey = new float[cols * rows];
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                var local = toLocal.Apply(new Vec2(origin.X + c * pitch, origin.Y + r * pitch));
                float v;
                if (local.X < 0 || local.Y < 0 || local.X > img.SizeMm.X || local.Y > img.SizeMm.Y) v = 1f; // outside the picture
                else
                {
                    var px = img.LocalToPixel(local);
                    v = (float)tone.Apply(img.Source.SampleLuminance(px.X, px.Y));
                }
                grey[r * cols + c] = v;
            }
        });
        var area = new Rect2(origin.X, origin.Y, origin.X + (cols - 1) * pitch, origin.Y + (rows - 1) * pitch);
        var bitmap = new Bitmap(cols, rows);
        if (layer.ImageMode == ImageEngraveMode.Grayscale)
        {
            // Power follows darkness: the lightest burned tone at GrayMinPower,
            // black at the layer's power; pure white is not visited at all.
            var levels = new byte[grey.Length];
            bool any = false;
            for (int i = 0; i < grey.Length; i++)
            {
                int q = (int)Math.Round((1 - Math.Clamp(grey[i], 0f, 1f)) * 255);
                if (q <= 0) continue;
                bitmap.Bits[i] = true;
                levels[i] = (byte)q;
                any = true;
            }
            if (!any) return (null, b);
            return (new DotBlock(bitmap, origin, layer.Laser, layer.Dpi, layer.DotUs, layer.GrayMinPower, "grayscale")
            {
                Levels = levels,
            }, area);
        }
        var dots = Core.Imaging.Dither.Apply(grey, cols, rows, layer.Dither);
        if (!dots.Any(d => d)) return (null, b);
        Array.Copy(dots, bitmap.Bits, dots.Length);
        var block = new DotBlock(bitmap, origin, layer.Laser, layer.Dpi, layer.DotUs,
            Math.Min(1, layer.Laser.Power), Core.Imaging.Dither.WireName(layer.Dither));
        return (block, area);
    }

    /// <summary>Checks the layer's cut, fill and image extras; messages are for the person at the laser.</summary>
    static void ValidateExtras(Layer l)
    {
        switch (l.Mode)
        {
            case LayerMode.Cut or LayerMode.Score:
                JobException.CheckRange("kerf", l.KerfMm, -MaxKerfMm, MaxKerfMm, "mm");
                if (l.Mode == LayerMode.Score) break;
                JobException.CheckRange("number of tabs", l.TabCount, 0, MaxTabs, "");
                if (l.TabCount > 0) JobException.CheckRange("tab width", l.TabWidthMm, 0.1, 10, "mm");
                JobException.CheckRange("overcut", l.OvercutMm, 0, 10, "mm");
                JobException.CheckRange("lead-in", l.LeadInMm, 0, 10, "mm");
                break;
            case LayerMode.Fill:
                JobException.CheckRange("fill angle", l.FillAngleDeg, -360, 360, "degrees");
                JobException.CheckRange("overscan", l.OverscanMm, 0, 20, "mm");
                break;
            case LayerMode.Image when l.ImageMode == ImageEngraveMode.Grayscale:
                if (!double.IsFinite(l.GrayMinPower) || l.GrayMinPower < 0 || l.GrayMinPower > l.Laser.Power)
                    throw new JobException($"The lightest grey's power must be between 0% and the layer's power ({JobPct(l.Laser.Power)}), got {JobPct(l.GrayMinPower)}.");
                break;
        }
    }

    static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    static string JobPct(double v) => double.IsFinite(v) ? Num(v) + "%" : "an invalid number";

    /// <summary>Largest kerf compensation accepted, either way (mm).</summary>
    const double MaxKerfMm = 1;

    /// <summary>Most tabs per outline.</summary>
    const int MaxTabs = 100;

    /// <summary>
    /// Kerf compensation: every closed outline moves by half the kerf, away from
    /// the material it bounds. Outlines at even nesting depth (parts) grow;
    /// those at odd depth (holes) shrink, whether a hole is part of a compound
    /// shape or a separate shape on the layer. Open paths are cut unchanged.
    /// </summary>
    static List<FlatPath> ApplyKerf(List<PathShape> shapes, Layer layer, double tol, List<string> warnings)
    {
        var subs = shapes.SelectMany(sh => sh.World.Subpaths).Where(sp => !sp.Segs.IsEmpty).ToList();
        var flat = subs.Select(sp => new FlatPath(sp.Flatten(tol), sp.Closed)).ToList();
        var depth = Depths(flat);
        double half = layer.KerfMm / 2;
        var o = new List<FlatPath>();
        int open = 0, vanished = 0;
        for (int i = 0; i < subs.Count; i++)
        {
            if (!subs[i].Closed || flat[i].Points.Count < 3)
            {
                if (flat[i].Points.Count >= 2) o.Add(flat[i]);
                open++;
                continue;
            }
            var moved = ShapeGeometry.Offset(new PathGeometry([subs[i]]), depth[i] % 2 == 0 ? half : -half,
                JoinStyle.Sharp, EndStyle.Flat, tolerance: tol);
            var loops = moved.Flatten(tol).Where(f => f.Closed && f.Points.Count >= 3).ToList();
            if (loops.Count == 0) vanished++;
            o.AddRange(loops);
        }
        if (open > 0)
            warnings.Add($"Layer \"{layer.Name}\": kerf compensation only works on closed outlines, so {open} open path(s) are cut as drawn.");
        if (vanished > 0)
            warnings.Add($"Layer \"{layer.Name}\": {vanished} outline(s) are narrower than the kerf and were left out.");
        return o;
    }

    /// <summary>How many closed paths each path lies inside (0 = outermost).</summary>
    static int[] Depths(List<FlatPath> paths)
    {
        int n = paths.Count;
        var boxes = paths.Select(p => Rect2.Bounds(p.Points)).ToArray();
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (paths[i].Points.Count == 0) continue;
            var probe = paths[i].Points[0];
            for (int j = 0; j < n; j++)
            {
                if (i == j || !paths[j].Closed || paths[j].Points.Count < 3) continue;
                if (!boxes[j].Contains(boxes[i]) || boxes[i] == boxes[j] && j > i) continue;
                if (Geom.Inside(paths[j].Points, probe)) depth[i]++;
            }
        }
        return depth;
    }

    /// <summary>
    /// Order paths for cutting: anything inside a closed path is cut before
    /// that path (so inner features are done while the part is still held),
    /// then nearest-neighbour within each nesting depth to cut travel.
    /// Open paths may be reversed; closed loops keep their start point.
    /// </summary>
    public static List<FlatPath> OrderForCutting(List<FlatPath> paths, ref Vec2 pos) =>
        Order(paths, ref pos).Select(x => x.Path).ToList();

    /// <summary><see cref="OrderForCutting"/>, with each path's nesting depth (even = part outline, odd = hole).</summary>
    static List<(FlatPath Path, int Depth)> Order(List<FlatPath> paths, ref Vec2 pos)
    {
        int n = paths.Count;
        var depth = Depths(paths);
        var o = new List<(FlatPath, int)>(n);
        foreach (var group in Enumerable.Range(0, n).GroupBy(i => depth[i]).OrderByDescending(g => g.Key))
        {
            var left = group.ToList();
            while (left.Count > 0)
            {
                int best = 0;
                bool reverse = false;
                double bestD = double.PositiveInfinity;
                for (int k = 0; k < left.Count; k++)
                {
                    var p = paths[left[k]];
                    double d = p.Points[0].Dist(pos);
                    if (d < bestD) { bestD = d; best = k; reverse = false; }
                    if (!p.Closed)
                    {
                        double e = p.Points[^1].Dist(pos);
                        if (e < bestD) { bestD = e; best = k; reverse = true; }
                    }
                }
                var pick = paths[left[best]];
                left.RemoveAt(best);
                if (reverse) pick = pick with { Points = Enumerable.Reverse(pick.Points).ToList() };
                o.Add((pick, group.Key));
                pos = pick.Closed ? pick.Points[0] : pick.Points[^1];
            }
        }
        return o;
    }

    /// <summary>Tabs, overcut and lead-in for the closed outlines of one Cut layer.</summary>
    sealed class CutExtras(Layer layer)
    {
        /// <summary>A turn sharper than this is a corner a tab should not sit on.</summary>
        const double CornerRad = 30 * Math.PI / 180;

        readonly bool _cut = layer.Mode == LayerMode.Cut;
        int _tooShort, _tabbed;

        /// <summary>What the laser actually follows for one path: the path itself, or open pieces.</summary>
        public IEnumerable<FlatPath> Pieces(FlatPath path, int depth)
        {
            if (!_cut || !path.Closed) return [path];
            var loop = Distinct(path.Points);
            if (loop.Count < 2) return [path];
            var cum = Cumulative(loop);
            double perimeter = cum[^1];
            if (layer.TabCount > 0)
            {
                if (perimeter >= layer.TabCount * layer.TabWidthMm * 3)
                {
                    _tabbed++;
                    return Tabs(loop, cum);
                }
                _tooShort++;
            }
            if (layer.LeadInMm <= 0 && layer.OvercutMm <= 0) return [path];
            var pts = new List<Vec2>(loop.Count + 8);
            if (layer.LeadInMm > 0) pts.Add(LeadIn(loop, depth, layer.LeadInMm));
            pts.AddRange(loop);
            pts.Add(loop[0]);
            if (layer.OvercutMm > 0) pts.AddRange(Along(loop, cum, 0, Math.Min(layer.OvercutMm, perimeter)).Skip(1));
            return [new FlatPath(pts, false)];
        }

        public void Warn(List<string> warnings)
        {
            if (_tooShort > 0)
                warnings.Add($"Layer \"{layer.Name}\": {_tooShort} outline(s) are too short for {layer.TabCount} tabs of {Num(layer.TabWidthMm)} mm and were cut without tabs.");
            if (_tabbed > 0 && (layer.LeadInMm > 0 || layer.OvercutMm > 0))
                warnings.Add($"Layer \"{layer.Name}\": outlines with tabs get no lead-in or overcut.");
        }

        /// <summary>
        /// The loop cut into TabCount pieces with gaps of TabWidth between them,
        /// spaced evenly along it; the first gap is centred half a spacing from
        /// the start. Gaps are nudged off sharp corners where there is room.
        /// </summary>
        List<FlatPath> Tabs(List<Vec2> loop, double[] cum)
        {
            int count = layer.TabCount;
            double perimeter = cum[^1], w = layer.TabWidthMm, spacing = perimeter / count;
            var corners = new List<double>();
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i] - loop[(i - 1 + loop.Count) % loop.Count];
                var c = loop[(i + 1) % loop.Count] - loop[i];
                if (Math.Abs(Math.Atan2(a.Cross(c), a.Dot(c))) > CornerRad) corners.Add(cum[i]);
            }
            bool Clear(double centre) => corners.All(s =>
            {
                double d = Math.Abs(s - centre) % perimeter;
                return Math.Min(d, perimeter - d) > w;
            });
            var centres = new double[count];
            for (int k = 0; k < count; k++)
            {
                double c = (k + 0.5) * spacing;
                centres[k] = c;
                if (Clear(c)) continue;
                for (double shift = w / 4; shift <= spacing / 4; shift += w / 4)
                {
                    if (Clear(c + shift)) { centres[k] = c + shift; break; }
                    if (Clear(c - shift)) { centres[k] = c - shift; break; }
                }
            }
            var o = new List<FlatPath>(count);
            for (int k = 0; k < count; k++)
            {
                double from = centres[k] + w / 2;
                double to = (k + 1 < count ? centres[k + 1] : centres[0] + perimeter) - w / 2;
                if (to - from > 1e-6) o.Add(new FlatPath(Along(loop, cum, from, to), false));
            }
            return o;
        }

        /// <summary>
        /// Start of the entry line: <paramref name="len"/> mm from the loop's start
        /// along its normal, outside an outer outline and inside a hole (the scrap side).
        /// </summary>
        static Vec2 LeadIn(List<Vec2> loop, int depth, double len)
        {
            var p0 = loop[0];
            var tin = Unit(p0 - loop[^1]);
            var tout = Unit(loop[1] - p0);
            var t = tin + tout;
            t = t.Length < 1e-9 ? tout : Unit(t);
            var left = new Vec2(-t.Y, t.X);
            // Positive signed area: the inside is on the left of the direction of travel.
            var inward = Geom.SignedArea(loop) > 0 ? left : -left;
            return p0 + (depth % 2 == 1 ? inward : -inward) * len;
        }

        static Vec2 Unit(Vec2 v) => v.Length < 1e-12 ? new Vec2(1, 0) : v / v.Length;

        static List<Vec2> Distinct(List<Vec2> pts)
        {
            var o = new List<Vec2>(pts.Count);
            foreach (var p in pts)
                if (o.Count == 0 || o[^1].Dist(p) > Polyline.SamePointMm) o.Add(p);
            while (o.Count > 1 && o[0].Dist(o[^1]) <= Polyline.SamePointMm) o.RemoveAt(o.Count - 1);
            return o;
        }

        /// <summary>Distance along the closed loop to each point; the last entry is the perimeter.</summary>
        static double[] Cumulative(List<Vec2> loop)
        {
            var cum = new double[loop.Count + 1];
            for (int i = 1; i <= loop.Count; i++) cum[i] = cum[i - 1] + loop[i - 1].Dist(loop[i % loop.Count]);
            return cum;
        }

        /// <summary>The part of the closed loop from distance <paramref name="from"/> to <paramref name="to"/> (may wrap past the start once).</summary>
        static List<Vec2> Along(List<Vec2> loop, double[] cum, double from, double to)
        {
            int n = loop.Count;
            double perimeter = cum[^1];
            double len = Math.Clamp(to - from, 0, perimeter);
            from %= perimeter;
            if (from < 0) from += perimeter;
            to = from + len;
            Vec2 At(double s)
            {
                double lap = Math.Floor(s / perimeter);
                double r = s - lap * perimeter;
                int i = Array.BinarySearch(cum, r);
                if (i < 0) i = ~i - 1;
                i = Math.Clamp(i, 0, n - 1);
                double segLen = cum[i + 1] - cum[i];
                double t = segLen > 0 ? (r - cum[i]) / segLen : 0;
                return Vec2.Lerp(loop[i], loop[(i + 1) % n], t);
            }
            var o = new List<Vec2> { At(from) };
            // Vertices strictly between from and to, over at most two laps.
            for (int lap = 0; lap < 2; lap++)
                for (int i = 0; i < n; i++)
                {
                    double s = cum[i] + lap * perimeter;
                    if (s > from + 1e-9 && s < to - 1e-9) o.Add(loop[i]);
                }
            o.Add(At(to));
            return o;
        }
    }
}
