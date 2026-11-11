using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Orders;

/// <summary>Where an order came from.</summary>
public enum OrderSource { Etsy, Shopify, Csv }

/// <summary>How far an ordered item has got.</summary>
public enum ItemStatus
{
    /// <summary>Not on a sheet yet.</summary>
    ToBurn,
    /// <summary>Put on a sheet, not burned yet.</summary>
    OnSheet,
    /// <summary>The sheet it was on finished on the machine (or marked by hand).</summary>
    Burned,
    /// <summary>Not to be made here (made another way, cancelled, a gift card…).</summary>
    Skipped,
}

/// <summary>
/// One line of an order: one product (with its options and the customer's text), in some quantity.
/// Each piece of the quantity becomes one copy of the product's design on a sheet.
/// </summary>
public sealed record OrderItem
{
    /// <summary>What makes the line unique across imports: "etsy:receipt:transaction", "shopify:#1001:line"…</summary>
    public string Key { get; init; } = "";
    public OrderSource Source { get; init; } = OrderSource.Csv;
    /// <summary>The order's number as the shop shows it (Etsy's order number, Shopify's #1001).</summary>
    public string Order { get; init; } = "";
    public DateTime? Date { get; init; }
    /// <summary>The buyer (or, when the shop doesn't say, who it's shipped to).</summary>
    public string Buyer { get; init; } = "";
    public string ShipName { get; init; } = "";
    /// <summary>The address it goes to, one line per line of the address (no name).</summary>
    public string ShipAddress { get; init; } = "";
    /// <summary>The product's name as listed.</summary>
    public string Title { get; init; } = "";
    public string Sku { get; init; } = "";
    public int Quantity { get; init; } = 1;
    /// <summary>What the customer asked to have engraved (may be several lines).</summary>
    public string Personalization { get; init; } = "";
    /// <summary>The options picked (colour, size, font…) and, for a plain spreadsheet, every other column.</summary>
    public Dictionary<string, string> Options { get; init; } = [];
    /// <summary>The buyer's note or gift message.</summary>
    public string Note { get; init; } = "";
    public ItemStatus Status { get; init; } = ItemStatus.ToBurn;
    /// <summary>The product picked by hand for this line; empty = found from the products' matches.</summary>
    public string Product { get; init; } = "";
    /// <summary>The sheet it was last put on (page name), and when it was burned.</summary>
    public string Sheet { get; init; } = "";
    public DateTime? Burned { get; init; }
    /// <summary>When it was first brought in.</summary>
    public DateTime Imported { get; init; } = DateTime.Now;

    /// <summary>The options as one line: "Colour: Walnut, Size: Large".</summary>
    [JsonIgnore]
    public string OptionsText => string.Join(", ", Options.Select(o => $"{o.Key}: {o.Value}"));

    public static string StatusText(ItemStatus s) => s switch
    {
        ItemStatus.ToBurn => "To burn",
        ItemStatus.OnSheet => "On a sheet",
        ItemStatus.Burned => "Burned",
        _ => "Skipped",
    };
}

/// <summary>
/// Every order line brought in, with how far each has got. Kept in the add-on's data folder
/// (orders.json); bringing the same orders in again updates them without losing their progress.
/// </summary>
public sealed class OrderBook
{
    public List<OrderItem> Items { get; set; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static OrderBook Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<OrderBook>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public OrderItem? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    /// <summary>
    /// Brings lines in: new ones are added; ones already here get the shop's latest details (a
    /// changed personalization, a new address) but keep their progress. Returns how many were new and changed.
    /// </summary>
    public (int Added, int Updated) Merge(IEnumerable<OrderItem> incoming)
    {
        int added = 0, updated = 0;
        foreach (var item in incoming)
        {
            int at = Items.FindIndex(i => i.Key == item.Key);
            if (at < 0) { Items.Add(item); added++; continue; }
            var had = Items[at];
            var next = item with { Status = had.Status, Sheet = had.Sheet, Burned = had.Burned, Imported = had.Imported, Product = had.Product };
            if (!Same(had, next)) { Items[at] = next; updated++; }
        }
        return (added, updated);
    }

    static readonly Dictionary<string, string> NoOptions = [];

    static bool Same(OrderItem a, OrderItem b) =>
        a with { Options = NoOptions } == b with { Options = NoOptions } && a.OptionsText == b.OptionsText;

    /// <summary>Changes one line (by key).</summary>
    public void Update(string key, Func<OrderItem, OrderItem> edit)
    {
        int at = Items.FindIndex(i => i.Key == key);
        if (at >= 0) Items[at] = edit(Items[at]);
    }
}

/// <summary>
/// The fields an order fills in on a product's design: <c>{Personalization}</c>, <c>{Buyer}</c>,
/// <c>{Order}</c>, each option by its name (<c>{Colour}</c>)… Written into the design's text and codes
/// exactly like the program's own fields, so <c>{serial}</c> and <c>{date}</c> still work beside them.
/// </summary>
public static class OrderFields
{
    /// <summary>The fields every order has, with what they hold (for the products window's list).</summary>
    public static readonly IReadOnlyList<(string Field, string Meaning)> Standard =
    [
        ("{Personalization}", "What the customer asked to have engraved"),
        ("{Personalization 1}", "Its first line ({Personalization 2} the second…)"),
        ("{Buyer}", "The buyer's name"),
        ("{Ship name}", "Who it's shipped to"),
        ("{Order}", "The order number"),
        ("{Order date}", "The day it was ordered"),
        ("{Item}", "The product's name as listed"),
        ("{SKU}", "The product's SKU"),
        ("{Options}", "Every option picked, on one line"),
        ("{Note}", "The buyer's note or gift message"),
        ("{Piece}", "Which piece of the quantity (2 of 3)"),
        ("{Name:upper}", "Any field in capitals (also :lower, :title, :first)"),
    ];

    /// <summary>The values for one piece (<paramref name="piece"/> of <paramref name="of"/>), by field name, ignoring case.</summary>
    public static Dictionary<string, string> For(OrderItem item, int piece = 1, int of = 1)
    {
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Options first, so a standard field of the same name wins.
        foreach (var o in item.Options) if (o.Key.Trim().Length > 0) f[o.Key.Trim()] = o.Value;
        f["Personalization"] = item.Personalization;
        f["Personalisation"] = item.Personalization;
        var lines = item.Personalization.Replace("\r", "").Split('\n');
        for (int i = 0; i < 20; i++) f[$"Personalization {i + 1}"] = f[$"Personalisation {i + 1}"] = i < lines.Length ? lines[i].Trim() : "";
        f["Buyer"] = item.Buyer.Length > 0 ? item.Buyer : item.ShipName;
        f["Ship name"] = item.ShipName.Length > 0 ? item.ShipName : item.Buyer;
        f["Ship address"] = item.ShipAddress;
        f["Order"] = item.Order;
        f["Order date"] = item.Date?.ToString("d", CultureInfo.CurrentCulture) ?? "";
        f["Item"] = item.Title;
        f["SKU"] = item.Sku;
        f["Quantity"] = item.Quantity.ToString(CultureInfo.InvariantCulture);
        f["Options"] = item.OptionsText;
        f["Note"] = item.Note;
        f["Piece"] = of > 1 ? $"{piece} of {of}" : "";
        return f;
    }

    /// <summary>
    /// The template with the order's fields written in. Fields that aren't the order's (the program's
    /// <c>{serial}</c>, <c>{date}</c>…) and doubled braces are left as they are. Braces a customer typed become brackets.
    /// </summary>
    public static string Bake(string template, IReadOnlyDictionary<string, string> fields)
    {
        if (!template.Contains('{')) return template;
        var sb = new StringBuilder(template.Length + 32);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append("{{"); i++; continue; }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append("}}"); i++; continue; }
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i + 1)
                {
                    var inner = template[(i + 1)..end];
                    if (!inner.Contains('{') && !inner.Contains('\n') && Value(inner, fields) is { } v)
                    {
                        sb.Append(v.Replace('{', '(').Replace('}', ')'));
                        i = end;
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>One field's value (with its :upper/:lower/:title/:first format), or null when it isn't an order field.</summary>
    public static string? Value(string field, IReadOnlyDictionary<string, string> fields)
    {
        string key = field.Trim(), format = "";
        if (!fields.ContainsKey(key) && field.LastIndexOf(':') is var colon and > 0)
        {
            key = field[..colon].Trim();
            format = field[(colon + 1)..].Trim().ToLowerInvariant();
        }
        if (!fields.TryGetValue(key, out var v)) return null;
        return format switch
        {
            "upper" => v.ToUpper(CultureInfo.CurrentCulture),
            "lower" => v.ToLower(CultureInfo.CurrentCulture),
            "title" => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(v.ToLower(CultureInfo.CurrentCulture)),
            "first" => v.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
            _ => v,
        };
    }

    /// <summary>The order fields a template uses that are empty for this item ("Personalization").</summary>
    public static IEnumerable<string> Empty(string template, IReadOnlyDictionary<string, string> fields)
    {
        foreach (var f in PhotonCanvas.Doc.VariableText.Fields(template))
        {
            var key = fields.ContainsKey(f.Trim()) ? f.Trim() : f.LastIndexOf(':') is var c and > 0 ? f[..c].Trim() : f.Trim();
            // A second or third line of the personalization is often left empty on purpose; so is {Piece}.
            bool optional = key.Equals("Piece", StringComparison.OrdinalIgnoreCase) ||
                            (key.StartsWith("Personali", StringComparison.OrdinalIgnoreCase) && key.Length > "Personalization".Length && !key.EndsWith(" 1"));
            if (!optional && fields.TryGetValue(key, out var v) && string.IsNullOrWhiteSpace(v)) yield return key;
        }
    }
}
