using System.Text.Json.Nodes;
using YTool.Core;

namespace YTool.Doc;

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
            var shapes = s.ShapesOn(layer.Id).OfType<PathShape>().ToList();
            if (shapes.Count == 0) continue;
            try { layer.Laser.Validate(opt.Machine); }
            catch (JobException e) { throw new JobException($"Layer \"{layer.Name}\": {e.Message}"); }

            if (layer.Mode == LayerMode.Fill)
            {
                int open = 0;
                foreach (var shape in shapes)
                {
                    var polys = shape.World.Flatten(opt.Tolerance).Where(f => f.Closed && f.Points.Count >= 3).ToList();
                    open += shape.World.Subpaths.Count(sp => !sp.Closed);
                    if (polys.Count == 0) continue;
                    var block = new FillBlock(polys.Select(p => (IReadOnlyList<Vec2>)p.Points).ToList(), layer.Laser, layer.LinesPerCm);
                    if (Job.FillLines(block).Count == 0) continue;
                    blocks.Add(block);
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
                var paths = shapes.SelectMany(sh => sh.World.Flatten(opt.Tolerance)).Where(f => f.Points.Count >= 2).ToList();
                foreach (var p in OrderForCutting(paths, ref pos))
                {
                    var pl = p.Closed ? Polyline.Loop(p.Points) : Polyline.Open(p.Points);
                    if (!pl.HasExtent) continue;
                    blocks.Add(new VectorBlock(pl, layer.Laser));
                    steps.Add(new(layer.Id, p.Points, p.Closed, false));
                    bounds = bounds.Union(Rect2.Bounds(p.Points));
                }
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
                ["processingType"] = l.Mode == LayerMode.Fill ? "FILL_VECTOR_ENGRAVING" : l.Mode == LayerMode.Score ? "VECTOR_ENGRAVING" : "VECTOR_CUTTING",
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

    /// <summary>
    /// Order paths for cutting: anything inside a closed path is cut before
    /// that path (so inner features are done while the part is still held),
    /// then nearest-neighbour within each nesting depth to cut travel.
    /// Open paths may be reversed; closed loops keep their start point.
    /// </summary>
    public static List<FlatPath> OrderForCutting(List<FlatPath> paths, ref Vec2 pos)
    {
        int n = paths.Count;
        var boxes = paths.Select(p => Rect2.Bounds(p.Points)).ToArray();
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            var probe = paths[i].Points[0];
            for (int j = 0; j < n; j++)
            {
                if (i == j || !paths[j].Closed || paths[j].Points.Count < 3) continue;
                if (!boxes[j].Contains(boxes[i]) || boxes[i] == boxes[j] && j > i) continue;
                if (Geom.Inside(paths[j].Points, probe)) depth[i]++;
            }
        }
        var o = new List<FlatPath>(n);
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
                o.Add(pick);
                pos = pick.Closed ? pick.Points[0] : pick.Points[^1];
            }
        }
        return o;
    }
}
