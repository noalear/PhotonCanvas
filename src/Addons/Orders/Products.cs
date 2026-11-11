using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// Something you sell: which ordered items it is (by SKU, or words in the item's name or options),
/// the design burned for each one (a PhotonCanvas file whose text uses order fields such as
/// <c>{Personalization}</c>), and the material it's made on (sheets never mix materials).
/// </summary>
public sealed class Product
{
    public string Name { get; set; } = "";
    /// <summary>One per line: a SKU, or words found in the item's name or options. "*" matches anything.</summary>
    public string Matches { get; set; } = "";
    /// <summary>What it's made on; empty = the product's own sheets.</summary>
    public string Material { get; set; } = "";
    /// <summary>The design file (.pcanvas).</summary>
    public string Design { get; set; } = "";

    public IEnumerable<string> Entries => Matches.Replace("\r", "").Split('\n', ';').Select(e => e.Trim()).Where(e => e.Length > 0);

    public string SheetGroup => Material.Trim().Length > 0 ? Material.Trim() : Name.Trim();
}

public sealed class ProductList
{
    public List<Product> Products { get; set; } = [];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static ProductList Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ProductList>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public ProductList Copy() => JsonSerializer.Deserialize<ProductList>(JsonSerializer.Serialize(this, Json), Json)!;

    /// <summary>
    /// The product an item is: first one listing its SKU exactly; else the first with words found in
    /// its name; else in its options; else one that matches anything ("*"). Null = no product yet.
    /// </summary>
    public Product? For(OrderItem item)
    {
        if (item.Product.Length > 0 && Products.FirstOrDefault(p => p.Name == item.Product) is { } picked) return picked;
        if (item.Sku.Length > 0 && Products.FirstOrDefault(p => p.Entries.Any(e => e.Equals(item.Sku, StringComparison.OrdinalIgnoreCase))) is { } bySku) return bySku;
        if (Products.FirstOrDefault(p => p.Entries.Any(e => e != "*" && item.Title.Contains(e, StringComparison.OrdinalIgnoreCase))) is { } byName) return byName;
        var options = item.OptionsText;
        if (Products.FirstOrDefault(p => p.Entries.Any(e => e != "*" && options.Contains(e, StringComparison.OrdinalIgnoreCase))) is { } byOption) return byOption;
        return Products.FirstOrDefault(p => p.Entries.Contains("*"));
    }
}

/// <summary>Product designs: reading and saving the files, and what fields they use.</summary>
public static class ProductDesigns
{
    /// <summary>The design's page on screen: its layers and shapes, or null when the file can't be read.</summary>
    public static DocState? Load(string path)
    {
        try { return File.Exists(path) ? DocFile.Load(path) : null; }
        catch (Exception) { return null; } // a damaged or foreign file: shown as "can't be read"
    }

    /// <summary>The shapes and the layers they're on (with their sub-layers), the box's corner moved to 0, 0; as a design file's state.</summary>
    public static DocState FromSelection(DocState s, IReadOnlySet<Guid> ids)
    {
        var shapes = s.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        var box = shapes.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        var move = box.IsEmpty ? Affine.Identity : Affine.Translate(-box.MinX, -box.MinY);
        var used = shapes.Select(x => x.LayerId).ToHashSet();
        var layers = s.Layers.Where(l => used.Contains(l.Id) || l.ParentId is { } p && used.Contains(p)).ToList();
        var moved = shapes.Select(x => x.With(transform: x.Transform.Then(move))).ToList();
        return new DocState([.. layers], [.. moved]) { Vars = s.Vars };
    }

    /// <summary>The order fields (and other fields) the design's text and codes use, e.g. "Personalization".</summary>
    public static IReadOnlyCollection<string> FieldsUsed(DocState design) => VariableText.Used(design);

    /// <summary>The shapes that go to the laser (on a visible output layer) and their box.</summary>
    public static Rect2 Box(DocState design) => design.Shapes.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
}
