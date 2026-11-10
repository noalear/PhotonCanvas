using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Borders;

/// <summary>A border's depth maps made and put in the design: one picture per piece, on a depth-slices layer.</summary>
public static class BorderOutput
{
    /// <summary>The spec a border picture was made from (JSON), so it can be opened again to change it.</summary>
    public const string SpecKey = "borders.spec";
    /// <summary>The same id on every picture (and cut line) of one border.</summary>
    public const string SetKey = "borders.set";
    /// <summary>Most pixels a picture gets along a side, so the program never has to shrink it.</summary>
    public const int MaxSide = RasterImage.MaxSide;
    const long MaxPixels = 30_000_000;

    /// <summary>The pixels per mm a piece is made at: as asked, unless that would make it too big.</summary>
    public static double PixelsPerMm(BorderSpec spec, BorderPiece p)
    {
        double ppm = Math.Clamp(spec.PixelsPerMm, 1, 40);
        double longest = Math.Max(p.WidthMm, p.HeightMm);
        if (longest * ppm > MaxSide) ppm = MaxSide / longest;
        if (p.WidthMm * p.HeightMm * ppm * ppm > MaxPixels) ppm = Math.Sqrt(MaxPixels / (p.WidthMm * p.HeightMm));
        return ppm;
    }

    /// <summary>Piece <paramref name="p"/>'s depth map: white the surface, black the deepest, see-through where it isn't carved.</summary>
    public static RasterImage DepthMap(BorderPlan plan, BorderPiece p, CancellationToken ct = default)
    {
        double ppm = PixelsPerMm(plan.Spec, p);
        int cols = Math.Max(1, (int)Math.Round(p.WidthMm * ppm)), rows = Math.Max(1, (int)Math.Round(p.HeightMm * ppm));
        double mmPerPx = Math.Max(p.WidthMm / cols, p.HeightMm / rows);
        var h = plan.Render(p, 0, 0, mmPerPx, cols, rows, ct);
        if (plan.Spec.SoftenMm > 0) BorderPlan.Soften(h, cols, rows, plan.Spec.SoftenMm / mmPerPx);
        ct.ThrowIfCancellationRequested();
        return BorderPlan.DepthMap(h, cols, rows, plan.Top, plan.Lowest);
    }

    /// <summary>
    /// The design with the border's pictures added (and its cut lines, if asked for), its top-left
    /// at <paramref name="at"/>, or in the middle of the work area when it fits there. With
    /// <paramref name="layer"/> the pictures go on that layer; else on a new depth-slices layer.
    /// </summary>
    public static (DocState State, List<Guid> Ids, Guid LayerId) Add(DocState state, BorderPlan plan, IReadOnlyList<(BorderPiece Piece, RasterImage Map)> maps,
        MachineProfile machine, Vec2? at = null, Layer? layer = null)
    {
        var spec = plan.Spec;
        if (layer is null)
        {
            var l = Layer.Default(state.Layers.Count, LayerMode.Image) with
            {
                Name = $"Border · {spec.Design.Name}", ImageMode = ImageEngraveMode.Slices,
                Dpi = Math.Clamp(Math.Round(Math.Clamp(spec.PixelsPerMm, 4, 20) * 25.4), 100, 508), LinesPerCm = 200, AngleStepDeg = 37,
            };
            state = Ops.AddLayer(state, l);
            layer = l;
        }
        // Pieces one under another, 5 mm apart.
        const double Gap = 5;
        double totalW = maps.Max(m => m.Piece.WidthMm), totalH = maps.Sum(m => m.Piece.HeightMm) + Gap * (maps.Count - 1);
        Vec2 origin = at ?? (totalW <= machine.FieldW && totalH <= machine.FieldH
            ? new Vec2((machine.FieldW - totalW) / 2, (machine.FieldH - totalH) / 2)
            : new Vec2(5, 5));
        string set = Guid.NewGuid().ToString("N");
        string json = spec.ToJson();
        var shapes = new List<Shape>();
        Layer? cut = null;
        double y = origin.Y;
        foreach (var (piece, map) in maps)
        {
            var move = Affine.Translate(origin.X, y);
            string name = maps.Count == 1 ? $"Border · {spec.Design.Name}" : $"Border · {spec.Design.Name} · {piece.Name.ToLowerInvariant()}";
            shapes.Add(new ImageShape
            {
                Name = name, LayerId = layer.Id, Source = ImageSource.FromEncoded(map.EncodePng()),
                SizeMm = new Vec2(piece.WidthMm, piece.HeightMm), Transform = move,
            }.WithAddonData(SpecKey, json).WithAddonData(SetKey, set));
            if (spec.CutLines)
            {
                if (cut is null) (state, cut) = CutLayer(state);
                var lines = plan.CutLines(piece);
                var g = new PathGeometry(lines.Select(pts => PathGeometry.Polyline(pts, true)).SelectMany(p => p.Subpaths)).Transform(move);
                shapes.Add(new PathShape { Name = $"{name} · cut", LayerId = cut.Id, Geometry = g }.WithAddonData(SetKey, set));
            }
            y += piece.HeightMm + Gap;
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

    /// <summary>The border picked: its spec, every shape of its set, where its top-left was, and its layer. Null if none of ours is selected.</summary>
    public static (BorderSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? Find(DocState state, IReadOnlySet<Guid> selection)
    {
        var picked = state.Shapes.FirstOrDefault(s => selection.Contains(s.Id) && s.AddonData.ContainsKey(SpecKey) && s is ImageShape);
        if (picked is null || BorderSpec.FromJson(picked.AddonData[SpecKey]) is not { } spec) return null;
        var ids = new HashSet<Guid> { picked.Id };
        if (picked.AddonData.TryGetValue(SetKey, out var set))
            foreach (var s in state.Shapes)
                if (s.AddonData.TryGetValue(SetKey, out var other) && other == set) ids.Add(s.Id);
        var bounds = state.Shapes.Where(s => ids.Contains(s.Id) && s is ImageShape).Select(s => s.Bounds).ToList();
        var tl = new Vec2(bounds.Min(b => b.MinX), bounds.Min(b => b.MinY));
        return (spec, ids, tl, picked.LayerId);
    }
}
