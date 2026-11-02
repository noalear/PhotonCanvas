using System.Text.Json;
using System.Text.Json.Serialization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.LeatherFabric;

/// <summary>Which of the add-on's layers is which (kept in the design so later runs use the same ones).</summary>
public sealed record SewnLayers(Guid? Pattern, Guid? Groove, Guid? Holes, Guid? Cut);

/// <summary>
/// Turns the selected outlines into leather or fabric pieces in the design. Each drawn outline stays,
/// on a "Pattern (not burned)" layer, as the piece's master: its settings are saved on it, and running
/// again on it (or on anything made from it) replaces what was made before.
/// </summary>
public static class SewnBuild
{
    public const string AddonId = "leatherfabric";
    public const string FromKey = AddonId + ".from", RoleKey = AddonId + ".role", OptionsKey = AddonId + ".options", LayersKey = AddonId + ".layers";

    /// <summary>Gap between a piece and its mirror image (mm).</summary>
    public const double MirrorGapMm = 5;

    public const uint PatternColour = 0x9CA3AF, GrooveColour = 0xF59E0B, HolesColour = 0xEF4444, CutColour = 0x2563EB;

    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static string Save(SewnOptions o) => JsonSerializer.Serialize(o, Json);

    public static SewnOptions? Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SewnOptions>(json, Json)?.Clamped(); }
        catch (Exception) { return null; }
    }

    /// <summary>The settings a piece was made with, if it was made here.</summary>
    public static SewnOptions? OptionsOf(Shape s) => s.AddonData.TryGetValue(OptionsKey, out var j) ? Load(j) : null;

    public static SewnLayers LayersOf(DocState s)
    {
        if (s.AddonData.TryGetValue(LayersKey, out var j))
            try { if (JsonSerializer.Deserialize<SewnLayers>(j) is { } l) return l; } catch (Exception) { }
        return new(null, null, null, null);
    }

    static Guid? MadeFrom(Shape s) => s.AddonData.TryGetValue(FromKey, out var f) && Guid.TryParse(f, out var g) ? g : null;

    /// <summary>
    /// The pieces' masters for a selection: each selected outline, or, for something made here, the
    /// outline it was made from. Pictures are left out.
    /// </summary>
    public static List<PathShape> Sources(DocState s, IEnumerable<Guid> selection)
    {
        var byId = s.Shapes.ToDictionary(x => x.Id);
        var o = new List<PathShape>();
        var seen = new HashSet<Guid>();
        foreach (var id in selection)
        {
            if (!byId.TryGetValue(id, out var sh)) continue;
            if (MadeFrom(sh) is { } from) sh = byId.GetValueOrDefault(from)!;
            if (sh is PathShape p && !p.World.IsEmpty && seen.Add(p.Id)) o.Add(p);
        }
        return o;
    }

    public sealed record Result(DocState State, List<Guid> Made, int Pieces, int Holes, List<string> Problems, bool OffBed);

    /// <summary>
    /// Make (or remake) the pieces for the selection. <paramref name="recipe"/>, when given, sets the
    /// cut and hole layers' laser settings from the material library.
    /// </summary>
    public static Result Apply(DocState s, IEnumerable<Guid> selection, SewnOptions o, Rect2 field, (ProcessSettings Settings, MaterialRef Ref)? recipe = null)
    {
        o = o.Clamped();
        var info = SewnMaterials.Info(o.Material);
        var sources = Sources(s, selection);
        if (sources.Count == 0) return new(s, [], 0, 0, ["Select the outlines of the pieces first (pictures can't be used)."], false);
        if (info.Never) return new(s, [], 0, 0, [$"{info.Name} must not be lasered."], false);

        // What was made from these before goes; it is made again below.
        var ids = sources.Select(x => x.Id).ToHashSet();
        s = Ops.Delete(s, s.Shapes.Where(x => MadeFrom(x) is { } f && ids.Contains(f)).Select(x => x.Id).ToHashSet());

        // The layers: found again by id, or made.
        var have = LayersOf(s);
        Guid Ensure(Guid? id, Func<int, Layer> make)
        {
            if (id is { } g && s.FindLayer(g) is not null) return g;
            var l = make(s.Layers.Count);
            s = Ops.AddLayer(s, l);
            return l.Id;
        }
        var pattern = Ensure(have.Pattern, i => Layer.Default(i) with { Name = "Pattern (not burned)", Color = PatternColour, Output = false });
        Guid? groove = o.StitchLine == StitchLineShow.Groove
            ? Ensure(have.Groove, i => Layer.Default(i, LayerMode.Score) with { Name = "Stitching groove", Color = GrooveColour })
            : have.Groove;
        Guid? holes = o.Holes != HoleStyle.None || o.Notches
            ? Ensure(have.Holes, i => Layer.Default(i) with { Name = "Stitch holes", Color = HolesColour })
            : have.Holes;
        var cut = Ensure(have.Cut, i => Layer.Default(i) with { Name = info.Fabric ? "Fabric cut" : "Leather cut", Color = CutColour });
        if (recipe is { } r)
        {
            s = Ops.UpdateLayers(s, new HashSet<Guid> { cut }, l => r.Settings.ApplyTo(l) with { Mode = LayerMode.Cut, Material = r.Ref });
            if (holes is { } h)
                s = Ops.UpdateLayers(s, new HashSet<Guid> { h }, l => l with { Laser = r.Settings.Laser, Material = r.Ref, KerfMm = 0, TabCount = 0, OvercutMm = 0, LeadInMm = 0 });
        }
        // Order: groove, then holes, then the cut, so pieces don't move before their holes are made.
        void Before(Guid? first, Guid then)
        {
            if (first is not { } f) return;
            int a = s.LayerIndex(f), b = s.LayerIndex(then);
            if (a >= 0 && b >= 0 && a > b) s = Ops.MoveLayer(s, f, b);
        }
        Before(holes, cut);
        Before(groove, holes ?? cut);
        s = s with { AddonData = s.AddonData.SetItem(LayersKey, JsonSerializer.Serialize(new SewnLayers(pattern, groove, holes, cut))) };

        // The pieces.
        var made = new List<Guid>();
        var sets = new List<(Guid Source, List<Shape> Shapes)>();
        var problems = new List<string>();
        int holeCount = 0;
        string optionsJson = Save(o);
        foreach (var src in sources)
        {
            var piece = SewnPattern.Make(src.World, o);
            if (piece.Problem is { } p) problems.Add($"{Name(src)}: {p}");
            var group = src.GroupId ?? Guid.NewGuid();
            var master = src.With(layerId: pattern).WithGroup(group).WithAddonData(OptionsKey, optionsJson);
            s = Ops.Replace(s, master);
            var set = new List<Shape> { master };
            void Add(PathGeometry g, Guid layer, string role, string label)
            {
                if (g.IsEmpty) return;
                set.Add(Tag(new PathShape { Name = $"{label} · {Name(src)}", LayerId = layer, Geometry = g }.WithGroup(group), src.Id, role));
            }
            Add(piece.Cut, cut, "cut", "Cut edge");
            Add(piece.Stitch, o.StitchLine == StitchLineShow.Groove && groove is { } gr ? gr : pattern, "stitch", "Stitch line");
            if (holes is { } hl)
            {
                Add(piece.Holes, hl, "holes", "Stitch holes");
                Add(piece.Notches, hl, "notches", "Notches");
            }
            holeCount += piece.HoleCount;
            sets.Add((src.Id, set));
        }
        var newShapes = sets.SelectMany(x => x.Shapes.Skip(1)).ToList();
        s = Ops.AddShapes(s, newShapes);
        made.AddRange(sets.SelectMany(x => x.Shapes.Select(sh => sh.Id)));

        // Mirror images: to the right, or below if they wouldn't fit on the work area there.
        var box = Rect2.Empty;
        foreach (var (_, set) in sets) foreach (var sh in set) box = box.Union(sh.Bounds);
        if (o.Mirror && !box.IsEmpty)
        {
            bool right = box.MaxX + MirrorGapMm + box.Width <= field.MaxX || field.IsEmpty;
            var flip = right
                ? Affine.ScaleAbout(new Vec2(box.MaxX + MirrorGapMm / 2, 0), -1, 1)
                : Affine.ScaleAbout(new Vec2(box.Center.X, 0), -1, 1).Then(Affine.Translate(0, box.Height + MirrorGapMm));
            var mirrored = new List<Shape>();
            foreach (var (from, set) in sets)
            {
                var group = Guid.NewGuid();
                foreach (var sh in set.OfType<PathShape>())
                {
                    string role = sh.AddonData.TryGetValue(RoleKey, out var rl) ? rl : "pattern";
                    mirrored.Add(Tag(new PathShape { Name = $"{sh.Name} (mirrored)", LayerId = sh.LayerId, Geometry = sh.World.Transform(flip) }.WithGroup(group), from, "mirror-" + role));
                }
            }
            s = Ops.AddShapes(s, mirrored);
            made.AddRange(mirrored.Select(m => m.Id));
            foreach (var m in mirrored) box = box.Union(m.Bounds);
            holeCount *= 2;
        }
        bool offBed = !field.IsEmpty && (box.MinX < field.MinX - 0.01 || box.MinY < field.MinY - 0.01 || box.MaxX > field.MaxX + 0.01 || box.MaxY > field.MaxY + 0.01);
        return new(s, made, sources.Count * (o.Mirror ? 2 : 1), holeCount, problems, offBed);
    }

    static string Name(Shape s) => string.IsNullOrWhiteSpace(s.Name) ? "piece" : s.Name;

    static Shape Tag(Shape sh, Guid from, string role) => sh.WithAddonData(FromKey, from.ToString()).WithAddonData(RoleKey, role);

    /// <summary>The job's checks: unsafe materials, and holes set to run after the cut.</summary>
    public static IEnumerable<string> Check(DocState s)
    {
        var outputs = s.Layers.Where(l => l.Output).Select(l => l.Id).ToHashSet();
        var masters = s.Shapes.Where(x => x.AddonData.ContainsKey(OptionsKey)).ToDictionary(x => x.Id);
        var burned = s.Shapes.Where(x => outputs.Contains(x.LayerId) && MadeFrom(x) is { } f && masters.ContainsKey(f)).Select(x => MadeFrom(x)!.Value).ToHashSet();
        var kinds = burned.Select(id => OptionsOf(masters[id])?.Material).OfType<SewnMaterial>().Distinct();
        foreach (var k in kinds)
        {
            var info = SewnMaterials.Info(k);
            if (info.Never) yield return $"Pieces are marked as {info.Name}: never laser PVC or vinyl (chlorine gas).";
            else if (info.Danger) yield return $"Pieces are marked as {info.Name}: only with the extraction vented outdoors.";
        }
        var l = LayersOf(s);
        if (l.Holes is { } h && l.Cut is { } c && outputs.Contains(h) && outputs.Contains(c)
            && s.LayerIndex(h) > s.LayerIndex(c) && s.Shapes.Any(x => x.LayerId == h) && s.Shapes.Any(x => x.LayerId == c))
            yield return "Stitch holes run after the leather/fabric cut: pieces may move before their holes are made. Drag the Stitch holes layer above the cut layer.";
    }
}
