using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.RecipeTuner;

/// <summary>A try you rated, kept by the tuner (poor ones too: they teach it where not to go).</summary>
/// <param name="RecipeId">The library recipe it was saved as, if it was (then the library's copy counts instead).</param>
/// <param name="Heat">−1 not enough heat (didn't cut through, too light), +1 too much (charred, too dark), 0 right or not said.</param>
public sealed record TriedRecord(
    Guid RoundId, int Number, Guid MaterialId, string MaterialName, string Category, double? ThicknessMm,
    RecipePurpose Purpose, ProcessSettings Settings, int Rating, DateTime When, string Machine, Guid? RecipeId, int Heat = 0);

/// <summary>The tries the tuner keeps, in its own data folder (tries.json).</summary>
public sealed class TriesStore
{
    // Their own resolver, so the program's shared cache never holds the add-on's types (docs/addons.md).
    static readonly JsonSerializerOptions Json = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record Body(string Format, int Version, List<TriedRecord> Tries);

    public string File { get; }
    public List<TriedRecord> Tries { get; } = [];

    public TriesStore(string folder) => File = Path.Combine(folder, "tries.json");

    /// <summary>The kept tries; none if there's no file yet or it can't be read (a damaged one is set aside).</summary>
    public static TriesStore Load(string folder)
    {
        var t = new TriesStore(folder);
        try
        {
            if (System.IO.File.Exists(t.File) && JsonSerializer.Deserialize<Body>(System.IO.File.ReadAllText(t.File), Json) is { Format: "photoncanvas-tuner-tries" } b)
                t.Tries.AddRange(b.Tries.Where(x => x is not null && x.Settings is not null));
        }
        catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
        {
            try { System.IO.File.Copy(t.File, t.File + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", true); } catch (IOException) { }
        }
        return t;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        var tmp = File + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(new Body("photoncanvas-tuner-tries", 1, Tries), Json));
        System.IO.File.Move(tmp, File, overwrite: true);
    }

    /// <summary>Put in a rating, replacing the earlier rating of the same try.</summary>
    public void Put(TriedRecord r)
    {
        Tries.RemoveAll(x => x.RoundId == r.RoundId && x.Number == r.Number);
        Tries.Add(r);
    }

    public TriedRecord? Find(Guid round, int number) => Tries.FirstOrDefault(x => x.RoundId == round && x.Number == number);

    /// <summary>Forget the tries kept for a material (its library recipes stay).</summary>
    public int Forget(Guid materialId) => Tries.RemoveAll(x => x.MaterialId == materialId);
}

/// <summary>A round of tries in the design: what it was for.</summary>
public sealed record TuneRound(Guid Id, Guid MaterialId, string MaterialLabel, RecipePurpose Purpose, LightSource Light, DateTime Made);

/// <summary>One try in the design: its number, its layer (with the settings as they are now) and where it is.</summary>
public sealed record TryInDesign(int Number, Layer Layer, Rect2 Bounds);

/// <summary>
/// Putting a round of tries in the design and finding it again: one small square (or a copy of the
/// picture) per try, each on its own "Test tune N" layer with those settings, numbered on a light
/// score layer. Nothing is burned until you send it with Ship It.
/// </summary>
public static class TuneTries
{
    public const string RoundKey = "recipe-tuner.round";
    public const string TryKey = "recipe-tuner.try";
    public const string LabelKey = "recipe-tuner.label";
    public const string LayerPrefix = "Test tune ";

    static readonly JsonSerializerOptions Json = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new JsonStringEnumConverter() } };

    public static TuneRound? Round(DocState s)
    {
        if (!s.AddonData.TryGetValue(RoundKey, out var json)) return null;
        try { return JsonSerializer.Deserialize<TuneRound>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>The tries in the design, by number.</summary>
    public static List<TryInDesign> Read(DocState s)
    {
        var list = new List<TryInDesign>();
        foreach (var g in s.Shapes.Where(x => x.AddonData.ContainsKey(TryKey)).GroupBy(x => x.AddonData[TryKey]))
        {
            if (!int.TryParse(g.Key, out int n)) continue;
            var shape = g.First();
            if (s.Layers.FirstOrDefault(l => l.Id == shape.LayerId) is not { } layer) continue;
            list.Add(new TryInDesign(n, layer, g.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b))));
        }
        return [.. list.OrderBy(t => t.Number)];
    }

    public static bool InDesign(DocState s) => s.Shapes.Any(x => x.AddonData.ContainsKey(TryKey));

    /// <summary>Take the tries, their layers, their numbers and the round's note out of the design.</summary>
    public static DocState Remove(DocState s)
    {
        var tagged = s.Shapes.Where(x => x.AddonData.ContainsKey(TryKey) || x.AddonData.ContainsKey(LabelKey)).ToList();
        var layers = tagged.Select(x => x.LayerId).ToHashSet();
        // Only layers that hold nothing else.
        layers.RemoveWhere(id => s.Shapes.Any(x => x.LayerId == id && !tagged.Contains(x)));
        s = Ops.Delete(s, tagged.Select(x => x.Id).ToHashSet());
        if (layers.Count > 0 && layers.Count < s.Layers.Count) s = Ops.DeleteLayers(s, layers, null);
        return s with { AddonData = s.AddonData.Remove(RoundKey) };
    }

    /// <summary>
    /// Add a round of tries (taking out an earlier round first). For pictures, <paramref name="picture"/>
    /// is copied for each try, shrunk to <paramref name="sizeMm"/>. Returns the new state and what was added.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Added, TuneRound Round) Add(
        DocState s, Material material, RecipePurpose purpose, LightSource light, IReadOnlyList<ProcessSettings> tries,
        double sizeMm, string font, MachineProfile m, ImageShape? picture = null)
    {
        if (purpose == RecipePurpose.Picture && picture is null) throw new InvalidOperationException("Select a picture first: each try is a small copy of it.");
        s = Remove(s);
        var round = new TuneRound(Guid.NewGuid(), material.Id, material.Label, purpose, light, DateTime.Now);
        double size = Math.Clamp(sizeMm, 3, 60);
        double gap = Math.Max(3, size * 0.3), labelMm = Math.Clamp(size * 0.3, 2.5, 5);
        int cols = Math.Min(tries.Count, tries.Count <= 6 ? tries.Count : (int)Math.Ceiling(Math.Sqrt(tries.Count * 1.5)));
        int rows = (int)Math.Ceiling(tries.Count / (double)Math.Max(1, cols));
        double pitchX = size + gap, pitchY = size + gap + labelMm + 1;
        double w = cols * pitchX - gap, h = rows * pitchY - gap;
        var origin = FreeSpot(s, w, h, m);

        var mode = Knobs.ModeFor(purpose);
        var labelLaser = light == LightSource.Red
            ? Knobs.Fix(new ProcessSettings { Laser = new LaserSettings { Light = LightSource.Red, Power = 30, Speed = 1000 } }, m).Laser
            : LaserSettings.Blue(Math.Clamp((tries.Count > 0 ? tries.Min(t => t.Laser.Power) : 50) * 0.6, 5, 100), Math.Clamp(Math.Max(tries.Count > 0 ? tries.Max(t => t.Laser.Speed) : 200, 200), m.MinSpeed, m.MaxSpeed));
        var mref = new MaterialRef(material.Id, material.Label);
        // Numbers first: cut tries drop out, so nothing may come after them.
        var labels = Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = "Test labels", Laser = labelLaser, Material = mref, Reflective = material.HighlyReflective };
        s = Ops.AddLayer(s, labels);
        var shapes = new List<Shape>();
        var style = new TextStyle(font, labelMm);
        for (int i = 0; i < tries.Count; i++)
        {
            int n = i + 1;
            var settings = Knobs.Fix(tries[i] with { Mode = mode }, m, material.HighlyReflective);
            var layer = settings.ApplyTo(Layer.Default(s.Layers.Count, mode)) with
            {
                Name = $"{LayerPrefix}{n}: {Knobs.Summary(settings, purpose)}",
                Material = mref,
                Reflective = material.HighlyReflective,
            };
            s = Ops.AddLayer(s, layer);
            double x = origin.X + (i % cols) * pitchX, y = origin.Y + (i / cols) * pitchY;
            Shape piece;
            if (purpose == RecipePurpose.Picture)
            {
                var b = picture!.Bounds;
                double k = size / Math.Max(1e-6, Math.Max(b.Width, b.Height));
                var place = picture.Transform.Then(Affine.Translate(-b.MinX, -b.MinY)).Then(Affine.Scale(k, k)).Then(Affine.Translate(x, y));
                piece = picture.With(layerId: layer.Id, transform: place, name: $"Try {n}", id: Guid.NewGuid());
            }
            else piece = new PathShape { Name = $"Try {n}", LayerId = layer.Id, Geometry = PathGeometry.Rect(x, y, size, size) };
            shapes.Add(piece.WithAddonData(TryKey, n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var t = TextShape.Create(n.ToString(System.Globalization.CultureInfo.InvariantCulture), style, labels.Id, Affine.Identity, name: $"Try {n} number");
            var tb = t.Bounds;
            if (!tb.IsEmpty) t = (TextShape)t.With(transform: Affine.Translate(x + size / 2 - (tb.MinX + tb.MaxX) / 2, y + size + 1 + labelMm / 2 - (tb.MinY + tb.MaxY) / 2));
            shapes.Add(t.WithAddonData(LabelKey, "1"));
        }
        s = Ops.AddShapes(s, shapes);
        s = s with { AddonData = s.AddonData.SetItem(RoundKey, JsonSerializer.Serialize(round, Json)) };
        return (s, shapes.Select(x => x.Id).ToList(), round);
    }

    /// <summary>A place on the bed for a w × h block that keeps clear of what's already there (top left when nothing is free).</summary>
    static Vec2 FreeSpot(DocState s, double w, double h, MachineProfile m)
    {
        var taken = s.Shapes.Where(x => s.Layers.FirstOrDefault(l => l.Id == x.LayerId) is { Visible: true, Output: true })
            .Select(x => x.Bounds).Where(b => !b.IsEmpty).Select(b => b.Inflate(3)).ToList();
        for (double y = 10; y + h <= m.FieldH - 5; y += 5)
            for (double x = 10; x + w <= m.FieldW - 5; x += 5)
            {
                var r = new Rect2(x, y, x + w, y + h);
                if (!taken.Any(t => t.Intersects(r))) return new Vec2(x, y);
            }
        return new Vec2(10, 10);
    }
}
