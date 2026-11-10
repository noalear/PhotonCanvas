using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Signs;

/// <summary>A sign's depth map made and put in the design, on a depth-slices layer, with its cut lines.</summary>
public static class SignOutput
{
    /// <summary>The spec a sign picture was made from (JSON), so it can be opened again to change it.</summary>
    public const string SpecKey = "signs.spec";
    /// <summary>The same id on the picture and the cut lines of one sign.</summary>
    public const string SetKey = "signs.set";
    public const int MaxSide = RasterImage.MaxSide;
    const long MaxPixels = 30_000_000;

    /// <summary>The pixels per mm the sign is made at: as asked, unless that would make it too big.</summary>
    public static double PixelsPerMm(SignPlan plan)
    {
        double ppm = Math.Clamp(plan.Spec.PixelsPerMm, 1, 40);
        double w = plan.SizeW, h = plan.SizeH;
        if (Math.Max(w, h) * ppm > MaxSide) ppm = MaxSide / Math.Max(w, h);
        if (w * h * ppm * ppm > MaxPixels) ppm = Math.Sqrt(MaxPixels / (w * h));
        return ppm;
    }

    /// <summary>The depth map: white the surface, black the deepest, see-through outside the sign and in its holes.</summary>
    public static RasterImage DepthMap(SignPlan plan, CancellationToken ct = default)
    {
        double ppm = PixelsPerMm(plan);
        int cols = Math.Max(1, (int)Math.Round(plan.SizeW * ppm)), rows = Math.Max(1, (int)Math.Round(plan.SizeH * ppm));
        double mmPerPx = Math.Max(plan.SizeW / cols, plan.SizeH / rows);
        var h = plan.Render(0, 0, mmPerPx, cols, rows, ct);
        if (plan.Spec.SoftenMm > 0) BorderPlan.Soften(h, cols, rows, plan.Spec.SoftenMm / mmPerPx);
        ct.ThrowIfCancellationRequested();
        return BorderPlan.DepthMap(h, cols, rows, plan.Top, plan.Lowest);
    }

    /// <summary>The layer's and picture's name.</summary>
    public static string Title(SignSpec spec)
    {
        string words = string.Join(" ", spec.Words.Select(l => l.Text.Trim()));
        if (words.Length > 28) words = words[..28].TrimEnd() + "…";
        return words.Length > 0 ? $"Sign · {words}" : $"Sign · {spec.Name}";
    }

    /// <summary>
    /// The design with the sign's picture added (and its cut lines), its top-left at <paramref name="at"/>
    /// or in the middle of the work area. With <paramref name="layer"/> the picture goes on that layer;
    /// else on a new depth-slices layer.
    /// </summary>
    public static (DocState State, List<Guid> Ids, Guid LayerId) Add(DocState state, SignPlan plan, RasterImage map,
        MachineProfile machine, Vec2? at = null, Layer? layer = null)
    {
        var spec = plan.Spec;
        string title = Title(spec);
        if (layer is null)
        {
            var l = Layer.Default(state.Layers.Count, LayerMode.Image) with
            {
                Name = title, ImageMode = ImageEngraveMode.Slices,
                Dpi = Math.Clamp(Math.Round(Math.Clamp(spec.PixelsPerMm, 4, 20) * 25.4), 100, 508), LinesPerCm = 200, AngleStepDeg = 37,
            };
            state = Ops.AddLayer(state, l);
            layer = l;
        }
        Vec2 origin = at ?? (plan.SizeW <= machine.FieldW && plan.SizeH <= machine.FieldH
            ? new Vec2((machine.FieldW - plan.SizeW) / 2, (machine.FieldH - plan.SizeH) / 2)
            : new Vec2(5, 5));
        string set = Guid.NewGuid().ToString("N");
        var move = Affine.Translate(origin.X, origin.Y);
        var shapes = new List<Shape>
        {
            new ImageShape
            {
                Name = title, LayerId = layer.Id, Source = ImageSource.FromEncoded(map.EncodePng()),
                SizeMm = new Vec2(plan.SizeW, plan.SizeH), Transform = move,
            }.WithAddonData(SpecKey, spec.ToJson()).WithAddonData(SetKey, set),
        };
        var lines = plan.CutLines();
        if (lines.Count > 0)
        {
            Layer cut;
            (state, cut) = CutLayer(state);
            var g = new PathGeometry(lines.Select(pts => PathGeometry.Polyline(pts, true)).SelectMany(p => p.Subpaths)).Transform(move);
            shapes.Add(new PathShape { Name = $"{title} · cut", LayerId = cut.Id, Geometry = g }.WithAddonData(SetKey, set));
        }
        state = Ops.AddShapes(state, shapes);
        var ids = shapes.Select(s => s.Id).ToList();
        if (ids.Count > 1) state = Ops.Group(state, ids.ToHashSet()).State;
        return (state, ids, layer.Id);
    }

    /// <summary>The design's Cut layer (the first one that's shown and not locked), or a new one.</summary>
    static (DocState, Layer) CutLayer(DocState s)
    {
        var existing = s.Layers.FirstOrDefault(l => l.Mode == LayerMode.Cut && l.Visible && !l.Locked && l.ParentId is null);
        if (existing is not null) return (s, existing);
        var layer = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = "Cut" };
        return (Ops.AddLayer(s, layer), layer);
    }

    /// <summary>The sign picked: its spec, its picture and cut lines, where its top-left was, and its layer. Null if none of ours is selected.</summary>
    public static (SignSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? Find(DocState state, IReadOnlySet<Guid> selection)
    {
        var picked = state.Shapes.FirstOrDefault(s => selection.Contains(s.Id) && s.AddonData.ContainsKey(SpecKey) && s is ImageShape);
        if (picked is null || SignSpec.FromJson(picked.AddonData[SpecKey]) is not { } spec) return null;
        var ids = new HashSet<Guid> { picked.Id };
        if (picked.AddonData.TryGetValue(SetKey, out var set))
            foreach (var s in state.Shapes)
                if (s.AddonData.TryGetValue(SetKey, out var other) && other == set) ids.Add(s.Id);
        var b = picked.Bounds;
        return (spec, ids, new Vec2(b.MinX, b.MinY), picked.LayerId);
    }
}
