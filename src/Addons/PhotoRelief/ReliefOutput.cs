using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>A relief's depth map made at full detail and put in the design, on a depth-slices layer.</summary>
public static class ReliefOutput
{
    /// <summary>The spec a relief was made from (JSON), so it can be opened again to change it.</summary>
    public const string SpecKey = "photorelief.spec";
    /// <summary>The photo it was made from (base64 of a JPEG or PNG), kept for changing it later.</summary>
    public const string PhotoKey = "photorelief.photo";
    /// <summary>The photo's name.</summary>
    public const string NameKey = "photorelief.name";
    /// <summary>The same id on the relief and its cut line.</summary>
    public const string SetKey = "photorelief.set";
    /// <summary>Most pixels a side, so the program never has to shrink it.</summary>
    public const int MaxSide = RasterImage.MaxSide;
    const long MaxPixels = 30_000_000;

    /// <summary>The pixels per mm it's made at: as asked, unless that would make it too big.</summary>
    public static double PixelsPerMm(ReliefSpec spec, double wMm, double hMm)
    {
        double ppm = Math.Clamp(spec.PixelsPerMm, 1, 40);
        double longest = Math.Max(wMm, hMm);
        if (longest * ppm > MaxSide) ppm = MaxSide / longest;
        if (wMm * hMm * ppm * ppm > MaxPixels) ppm = Math.Sqrt(MaxPixels / (wMm * hMm));
        return ppm;
    }

    /// <summary>The pixels across and down the full depth map has.</summary>
    public static (int Cols, int Rows) Pixels(ReliefSpec spec, ReliefPhoto photo)
    {
        var (w, h) = spec.Size(photo.Width, photo.Height);
        double ppm = PixelsPerMm(spec, w, h);
        return (Math.Max(2, (int)Math.Round(w * ppm)), Math.Max(2, (int)Math.Round(h * ppm)));
    }

    /// <summary>The relief at full detail.</summary>
    public static ReliefResult Full(ReliefSpec spec, ReliefPhoto photo, CancellationToken ct = default)
    {
        var (cols, rows) = Pixels(spec, photo);
        return PhotoDepth.Make(spec, photo, cols, rows, ct);
    }

    /// <summary>The plaque's outline (mm, its top-left at 0, 0), or null when there's no plaque.</summary>
    public static PathGeometry? Outline(ReliefSpec spec, double w, double h) => spec.Shape switch
    {
        PlaqueShape.Rectangle => PathGeometry.Rect(0, 0, w, h),
        PlaqueShape.Rounded => PathGeometry.RoundRect(0, 0, w, h, spec.CornerMm, spec.CornerMm),
        PlaqueShape.Oval or PlaqueShape.Circle => PathGeometry.Ellipse(w / 2, h / 2, w / 2, h / 2),
        _ => null,
    };

    /// <summary>
    /// The design with the relief added (and its cut line, if asked for), centred on <paramref name="centre"/>,
    /// or in the middle of the work area. With <paramref name="layer"/> it goes on that layer; else on a new depth-slices layer.
    /// </summary>
    public static (DocState State, List<Guid> Ids, Guid LayerId) Add(DocState state, ReliefSpec spec, ReliefPhoto photo, RasterImage map,
        double wMm, double hMm, MachineProfile machine, Vec2? centre = null, Layer? layer = null)
    {
        if (layer is null)
        {
            var l = Layer.Default(state.Layers.Count, LayerMode.Image) with
            {
                Name = $"Relief · {photo.Name}", ImageMode = ImageEngraveMode.Slices,
                Dpi = Math.Clamp(Math.Round(Math.Clamp(spec.PixelsPerMm, 4, 20) * 25.4), 100, 508), LinesPerCm = 200, AngleStepDeg = 37,
            };
            state = Ops.AddLayer(state, l);
            layer = l;
        }
        var mid = centre ?? new Vec2(machine.FieldW / 2, machine.FieldH / 2);
        var move = Affine.Translate(mid.X - wMm / 2, mid.Y - hMm / 2);
        string set = Guid.NewGuid().ToString("N");
        string name = $"Relief · {photo.Name}";
        var shapes = new List<Shape>
        {
            new ImageShape
            {
                Name = name, LayerId = layer.Id, Source = ImageSource.FromEncoded(map.EncodePng()),
                SizeMm = new Vec2(wMm, hMm), Transform = move,
            }.WithAddonData(SpecKey, spec.ToJson()).WithAddonData(PhotoKey, Convert.ToBase64String(photo.Encoded()))
             .WithAddonData(NameKey, photo.Name).WithAddonData(SetKey, set),
        };
        if (spec.CutLine && Outline(spec, wMm, hMm) is { } outline)
        {
            Layer cut;
            (state, cut) = CutLayer(state);
            shapes.Add(new PathShape { Name = $"{name} · cut", LayerId = cut.Id, Geometry = outline.Transform(move) }.WithAddonData(SetKey, set));
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

    /// <summary>Is this one of our reliefs?</summary>
    public static bool IsRelief(Shape s) => s is ImageShape && s.AddonData.ContainsKey(SpecKey) && s.AddonData.ContainsKey(PhotoKey);

    /// <summary>The relief picked: its spec, its photo, every shape of its set, its centre and its layer. Null if none of ours is selected.</summary>
    public static (ReliefSpec Spec, ReliefPhoto Photo, HashSet<Guid> Ids, Vec2 Centre, Guid LayerId)? Find(DocState state, IReadOnlySet<Guid> selection)
    {
        var picked = state.Shapes.FirstOrDefault(s => selection.Contains(s.Id) && IsRelief(s));
        if (picked is null || ReliefSpec.FromJson(picked.AddonData[SpecKey]) is not { } spec) return null;
        RasterImage img;
        try { img = RasterImage.Decode(Convert.FromBase64String(picked.AddonData[PhotoKey])); }
        catch (FormatException) { return null; }
        var photo = new ReliefPhoto(img, picked.AddonData.GetValueOrDefault(NameKey) ?? "Photo");
        var ids = new HashSet<Guid> { picked.Id };
        if (picked.AddonData.TryGetValue(SetKey, out var set))
            foreach (var s in state.Shapes)
                if (s.AddonData.TryGetValue(SetKey, out var other) && other == set) ids.Add(s.Id);
        var b = picked.Bounds;
        return (spec, photo, ids, new Vec2((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2), picked.LayerId);
    }
}
