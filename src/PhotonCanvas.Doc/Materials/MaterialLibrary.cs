using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc.Materials;

/// <summary>What a recipe is for.</summary>
public enum RecipePurpose { Cut, Score, Engrave, Picture, Relief, Mark }

/// <summary>The processing part of a layer's settings (everything but its name, colour and visibility).</summary>
public sealed record ProcessSettings
{
    public LayerMode Mode { get; init; } = LayerMode.Cut;
    public LaserSettings Laser { get; init; } = LaserSettings.Blue(70, 40);
    public double LinesPerCm { get; init; } = 100;
    public double Dpi { get; init; } = 180;
    public int DotUs { get; init; } = 200;
    public DitherMethod Dither { get; init; } = DitherMethod.Jarvis;
    public ImageEngraveMode ImageMode { get; init; } = ImageEngraveMode.Dither;
    public double GrayMinPower { get; init; } = 10;
    public double KerfMm { get; init; }
    public int TabCount { get; init; }
    public double TabWidthMm { get; init; } = 0.5;
    public double OvercutMm { get; init; }
    public double LeadInMm { get; init; }
    public double FillAngleDeg { get; init; }
    public bool Crosshatch { get; init; }
    public double OverscanMm { get; init; }
    public double StepDownMm { get; init; }
    public double StepDownFirstMm { get; init; }
    public bool Wobble { get; init; }
    public double WobbleDiameterMm { get; init; } = 0.05;
    public double WobbleSpacingMm { get; init; } = 0.015;

    public static ProcessSettings From(Layer l) => new()
    {
        Mode = l.Mode, Laser = l.Laser, LinesPerCm = l.LinesPerCm, Dpi = l.Dpi, DotUs = l.DotUs, Dither = l.Dither,
        ImageMode = l.ImageMode, GrayMinPower = l.GrayMinPower, KerfMm = l.KerfMm, TabCount = l.TabCount, TabWidthMm = l.TabWidthMm,
        OvercutMm = l.OvercutMm, LeadInMm = l.LeadInMm, FillAngleDeg = l.FillAngleDeg, Crosshatch = l.Crosshatch, OverscanMm = l.OverscanMm,
        StepDownMm = l.StepDownMm, StepDownFirstMm = l.StepDownFirstMm, Wobble = l.Wobble, WobbleDiameterMm = l.WobbleDiameterMm, WobbleSpacingMm = l.WobbleSpacingMm,
    };

    public Layer ApplyTo(Layer l) => l with
    {
        Mode = Mode, Laser = Laser, LinesPerCm = LinesPerCm, Dpi = Dpi, DotUs = DotUs, Dither = Dither,
        ImageMode = ImageMode, GrayMinPower = GrayMinPower, KerfMm = KerfMm, TabCount = TabCount, TabWidthMm = TabWidthMm,
        OvercutMm = OvercutMm, LeadInMm = LeadInMm, FillAngleDeg = FillAngleDeg, Crosshatch = Crosshatch, OverscanMm = OverscanMm,
        StepDownMm = StepDownMm, StepDownFirstMm = StepDownFirstMm, Wobble = Wobble, WobbleDiameterMm = WobbleDiameterMm, WobbleSpacingMm = WobbleSpacingMm,
    };

    /// <summary>Settings brought inside the machine's limits (imported files are not trusted blindly).</summary>
    public ProcessSettings Clamped(MachineProfile m) => this with
    {
        Laser = Laser with
        {
            Power = Math.Clamp(Laser.Power, 0, 100),
            Speed = Math.Clamp(Laser.Speed, m.MinSpeed, m.MaxSpeed),
            Passes = Math.Clamp(Laser.Passes, 1, 1000),
        },
        LinesPerCm = Math.Clamp(LinesPerCm, 1, 1000),
        Dpi = Math.Clamp(Dpi, 25, 1000),
        DotUs = Math.Clamp(DotUs, 10, 10000),
        GrayMinPower = Math.Clamp(GrayMinPower, 0, 100),
        KerfMm = Math.Clamp(KerfMm, -2, 2),
        TabCount = Math.Clamp(TabCount, 0, 100),
        TabWidthMm = Math.Clamp(TabWidthMm, 0, 20),
        OvercutMm = Math.Clamp(OvercutMm, 0, 20),
        LeadInMm = Math.Clamp(LeadInMm, 0, 20),
        OverscanMm = Math.Clamp(OverscanMm, 0, 50),
        StepDownMm = Math.Clamp(StepDownMm, 0, StepDown.MaxPerPassMm),
        StepDownFirstMm = Math.Clamp(StepDownFirstMm, 0, StepDown.MaxPerPassMm),
        WobbleDiameterMm = Math.Clamp(WobbleDiameterMm, PhotonCanvas.Core.Wobble.MinDiameterMm, PhotonCanvas.Core.Wobble.MaxDiameterMm),
        WobbleSpacingMm = Math.Clamp(WobbleSpacingMm, PhotonCanvas.Core.Wobble.MinSpacingMm, PhotonCanvas.Core.Wobble.MaxSpacingMm),
    };

    /// <summary>One line for lists, e.g. "Cut · blue 70% · 40 mm/s ×8".</summary>
    public string Summary()
    {
        string light = Laser.Light == LightSource.Red ? $"fiber {Laser.PulseWidthNs} ns {Laser.FrequencyKhz} kHz" : "diode";
        string mode = Mode switch { LayerMode.Fill => $"engrave {LinesPerCm:0} l/cm", LayerMode.Image => $"picture {Dpi:0} dpi", LayerMode.Score => "score", _ => "cut" };
        return $"{mode} · {light} {Laser.Power:0.#}% · {Laser.Speed:0.#} mm/s" + (Laser.Passes > 1 ? $" ×{Laser.Passes}" : "");
    }
}

/// <summary>Settings that do something on a material, with how well they worked.</summary>
public sealed record Recipe
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RecipePurpose Purpose { get; init; }
    /// <summary>Short description, e.g. "Clean cut, light char".</summary>
    public string Title { get; init; } = "";
    public ProcessSettings Settings { get; init; } = new();
    /// <summary>0 = not rated, 1–5 stars.</summary>
    public int Rating { get; init; }
    /// <summary>Actually burned and looked at (by <see cref="TestedBy"/>), not just a guess.</summary>
    public bool Tested { get; init; }
    public DateTime? TestedOn { get; init; }
    /// <summary>Who tested it: empty for you; a name for recipes imported from someone else.</summary>
    public string TestedBy { get; init; } = "";
    /// <summary>Focus distance used, mm (material top to laser), if it matters.</summary>
    public double? FocusMm { get; init; }
    public string Notes { get; init; } = "";
    /// <summary>File name of a photo of the result, in the library's photo folder.</summary>
    public string? Photo { get; init; }
    /// <summary>Machine it was made on (device code), e.g. GS004-CLASS-4.</summary>
    public string Machine { get; init; } = MachineProfile.F2Ultra.DeviceCode;
    public DateTime Updated { get; init; } = DateTime.Now;
}

public sealed record Material
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Category { get; init; } = "Other";
    public double? ThicknessMm { get; init; }
    public string Notes { get; init; } = "";
    public ImmutableList<Recipe> Recipes { get; init; } = [];
    public DateTime Updated { get; init; } = DateTime.Now;

    /// <summary>e.g. "Basswood 3 mm".</summary>
    public string Label => ThicknessMm is { } t ? $"{Name} {t:0.##} mm" : Name;

    public static readonly string[] Categories =
        ["Wood", "Plywood & MDF", "Acrylic", "Leather", "Paper & card", "Fabric", "Cork & rubber", "Stone & tile", "Glass", "Metal", "Coated metal", "Plastic", "Food", "Other"];
}

/// <summary>A layer's material: which library entry and recipe it came from (kept by name too, so it still reads well elsewhere).</summary>
public sealed record MaterialRef(Guid MaterialId, string Label, Guid? RecipeId = null, string? RecipeTitle = null);

/// <summary>What happened on import.</summary>
public sealed record ImportResult(int NewMaterials, int NewRecipes, int Skipped, string? From);

/// <summary>
/// The user's material and cutting library: materials, each with recipes that record what
/// settings did on it. Kept as JSON plus a photo folder; shared as a .pcmat zip.
/// </summary>
public sealed class MaterialLibrary
{
    public const string FileFormat = "photoncanvas-materials";
    public const string ShareExtension = ".pcmat";
    const long MaxShareBytes = 200L * 1024 * 1024;
    const long MaxPhotoBytes = 8L * 1024 * 1024;

    public List<Material> Materials { get; } = [];
    public string Folder { get; }
    public string PhotoFolder => Path.Combine(Folder, "photos");
    string FilePath => Path.Combine(Folder, "library.json");

    public MaterialLibrary(string folder) => Folder = folder;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record FileBody(string Format, int Version, string? Author, DateTime Saved, List<Material> Materials);

    /// <summary>The library in <paramref name="folder"/>; empty if there is none yet. Never throws for a missing or damaged file (a damaged one is set aside).</summary>
    public static MaterialLibrary Load(string folder)
    {
        var lib = new MaterialLibrary(folder);
        try
        {
            if (File.Exists(lib.FilePath))
                lib.Materials.AddRange(Parse(File.ReadAllText(lib.FilePath)).Materials);
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException or NotSupportedException)
        {
            try { File.Copy(lib.FilePath, lib.FilePath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", true); } catch (IOException) { }
        }
        return lib;
    }

    static FileBody Parse(string json)
    {
        var body = JsonSerializer.Deserialize<FileBody>(json, Json) ?? throw new FormatException("Empty material file.");
        if (body.Format != FileFormat) throw new FormatException("This isn't a PhotonCanvas material file.");
        if (body.Version > 1) throw new FormatException("This material file was made by a newer version of PhotonCanvas.");
        return body;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new FileBody(FileFormat, 1, null, DateTime.Now, Materials), Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public Material? Find(Guid id) => Materials.FirstOrDefault(m => m.Id == id);

    public void Upsert(Material m)
    {
        int i = Materials.FindIndex(x => x.Id == m.Id);
        var stamped = m with { Updated = DateTime.Now };
        if (i >= 0) Materials[i] = stamped; else Materials.Add(stamped);
        Materials.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
    }

    public void Remove(Guid id)
    {
        if (Find(id) is not { } m) return;
        foreach (var r in m.Recipes) DeletePhoto(r.Photo);
        Materials.Remove(m);
    }

    /// <summary>Save a photo for a recipe and return its file name.</summary>
    public string SavePhoto(Guid recipeId, byte[] jpeg)
    {
        Directory.CreateDirectory(PhotoFolder);
        var name = $"{recipeId:N}.jpg";
        File.WriteAllBytes(Path.Combine(PhotoFolder, name), jpeg);
        return name;
    }

    public string? PhotoPath(string? name) =>
        name is not null && SafeName(name) && File.Exists(Path.Combine(PhotoFolder, name)) ? Path.Combine(PhotoFolder, name) : null;

    public void DeletePhoto(string? name)
    {
        if (PhotoPath(name) is { } p) try { File.Delete(p); } catch (IOException) { }
    }

    static bool SafeName(string name) =>
        name.Length is > 0 and < 80 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !name.StartsWith('.') &&
        (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

    // ---- sharing --------------------------------------------------------------------------

    /// <summary>Write <paramref name="materials"/> (and their photos) to a .pcmat file for someone else.</summary>
    public void Export(Stream output, IEnumerable<Material> materials, string? author)
    {
        var list = materials.ToList();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var body = new FileBody(FileFormat, 1, string.IsNullOrWhiteSpace(author) ? null : author.Trim(), DateTime.Now, list);
        using (var w = new StreamWriter(zip.CreateEntry("library.json").Open()))
            w.Write(JsonSerializer.Serialize(body, Json));
        foreach (var name in list.SelectMany(m => m.Recipes).Select(r => r.Photo).OfType<string>().Distinct())
            if (PhotoPath(name) is { } path)
                zip.CreateEntryFromFile(path, "photos/" + name, CompressionLevel.NoCompression);
    }

    /// <summary>
    /// Add someone else's materials. New materials are added; for ones you already have (same id),
    /// only recipes you don't have are added. Recipes keep who tested them; settings are brought
    /// inside the machine's limits; anything unreadable is skipped.
    /// </summary>
    /// <exception cref="FormatException">Not a PhotonCanvas material file.</exception>
    public ImportResult Import(Stream input, string fallbackAuthor)
    {
        try { return ImportZip(input, fallbackAuthor); }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            throw new FormatException("This isn't a readable PhotonCanvas material file.", e);
        }
    }

    ImportResult ImportZip(Stream input, string fallbackAuthor)
    {
        if (input.CanSeek && input.Length > MaxShareBytes) throw new FormatException("That file is too big to be a material library.");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("library.json") ?? throw new FormatException("This isn't a PhotonCanvas material file (no library.json).");
        if (entry.Length > 50L * 1024 * 1024) throw new FormatException("The material list is too big.");
        FileBody body;
        using (var r = new StreamReader(entry.Open())) body = Parse(r.ReadToEnd());
        string from = string.IsNullOrWhiteSpace(body.Author) ? fallbackAuthor : body.Author!;
        var machine = MachineProfile.F2Ultra;
        int newMaterials = 0, newRecipes = 0, skipped = 0;
        foreach (var incoming in body.Materials ?? [])
        {
            if (incoming is null || string.IsNullOrWhiteSpace(incoming.Name)) { skipped++; continue; }
            var mine = Find(incoming.Id);
            var known = mine?.Recipes.Select(r => r.Id).ToHashSet() ?? [];
            var added = new List<Recipe>();
            foreach (var r in incoming.Recipes ?? [])
            {
                if (r is null || known.Contains(r.Id)) { if (r is not null) skipped++; continue; }
                string? photo = null;
                if (r.Photo is { } p && SafeName(p) && zip.GetEntry("photos/" + p) is { Length: > 0 and <= MaxPhotoBytes } pe)
                {
                    Directory.CreateDirectory(PhotoFolder);
                    var local = $"{r.Id:N}{Path.GetExtension(p).ToLowerInvariant()}";
                    pe.ExtractToFile(Path.Combine(PhotoFolder, local), overwrite: true);
                    photo = local;
                }
                added.Add(r with
                {
                    Settings = r.Settings.Clamped(machine),
                    Rating = Math.Clamp(r.Rating, 0, 5),
                    TestedBy = r.Tested && string.IsNullOrEmpty(r.TestedBy) ? from : r.TestedBy,
                    Photo = photo,
                    Title = Trim(r.Title, 120),
                    Notes = Trim(r.Notes, 4000),
                });
            }
            newRecipes += added.Count;
            if (mine is null)
            {
                Upsert(incoming with
                {
                    Name = Trim(incoming.Name, 120),
                    Category = Material.Categories.Contains(incoming.Category) ? incoming.Category : "Other",
                    Notes = Trim(incoming.Notes, 4000),
                    ThicknessMm = incoming.ThicknessMm is { } t && t is > 0 and < 500 ? t : null,
                    Recipes = [.. added],
                });
                newMaterials++;
            }
            else if (added.Count > 0)
                Upsert(mine with { Recipes = mine.Recipes.AddRange(added) });
        }
        return new ImportResult(newMaterials, newRecipes, skipped, from);
    }

    static string Trim(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];

    /// <summary>Bring old named presets into the library (once): one "My presets" material each.</summary>
    public int AdoptPresets(IEnumerable<(string Name, LayerMode Mode, LaserSettings Laser, double LinesPerCm)> presets)
    {
        int n = 0;
        foreach (var (name, mode, laser, lpcm) in presets)
        {
            if (string.IsNullOrWhiteSpace(name) || Materials.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var purpose = mode switch { LayerMode.Fill => RecipePurpose.Engrave, LayerMode.Score => RecipePurpose.Score, LayerMode.Image => RecipePurpose.Picture, _ => RecipePurpose.Cut };
            Upsert(new Material
            {
                Name = name,
                Notes = "From your saved presets.",
                Recipes = [new Recipe { Purpose = purpose, Title = "From preset", Settings = new ProcessSettings { Mode = mode, Laser = laser, LinesPerCm = lpcm } }],
            });
            n++;
        }
        return n;
    }

    /// <summary>A small JPEG of <paramref name="area"/> (mm) cut from a straightened work-area photo, for a recipe's photo.</summary>
    public static byte[]? CropPhoto(RasterImage photo, Rect2 photoArea, double mmPerPx, Rect2 area, int maxSide = 360)
    {
        int x0 = (int)Math.Floor((area.MinX - photoArea.MinX) / mmPerPx), y0 = (int)Math.Floor((area.MinY - photoArea.MinY) / mmPerPx);
        int x1 = (int)Math.Ceiling((area.MaxX - photoArea.MinX) / mmPerPx), y1 = (int)Math.Ceiling((area.MaxY - photoArea.MinY) / mmPerPx);
        x0 = Math.Clamp(x0, 0, photo.Width); x1 = Math.Clamp(x1, 0, photo.Width);
        y0 = Math.Clamp(y0, 0, photo.Height); y1 = Math.Clamp(y1, 0, photo.Height);
        int w = x1 - x0, h = y1 - y0;
        if (w < 4 || h < 4) return null;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(photo.Rgba, ((y0 + y) * photo.Width + x0) * 4, rgba, y * w * 4, w * 4);
        return new RasterImage(w, h, rgba).EncodeJpeg(maxSide);
    }
}
