using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Orders;

/// <summary>What a spreadsheet of orders turned out to be, and what was read from it.</summary>
public sealed record CsvImport(string Kind, List<OrderItem> Items, int Skipped, string? Problem)
{
    /// <summary>Shipped, fulfilled or cancelled lines left out, and why the file couldn't be read.</summary>
    public string Summary => Problem ?? $"{Kind}: {Items.Count} line{(Items.Count == 1 ? "" : "s")}" +
        (Skipped > 0 ? $", {Skipped} already shipped or cancelled left out" : "") + ".";
}

/// <summary>
/// Reads orders from a shop's CSV export: Etsy's "Order Items" download, Shopify's orders export,
/// or any spreadsheet with a row per item (its headings become fields).
/// </summary>
public static partial class OrderCsv
{
    public static CsvImport Read(string text, bool includeShipped = false)
    {
        var t = Csv.Parse(text);
        if (t.Headers.Length == 0 || t.Rows.Length == 0) return new("Spreadsheet", [], 0, "The file has no rows under its headings.");
        if (Has(t, "Transaction ID") && Has(t, "Item Name") && Has(t, "Order ID")) return Etsy(t, includeShipped);
        if (Has(t, "Number of Items") && Has(t, "Order ID") && !Has(t, "Item Name"))
            return new("Etsy orders", [], 0, "This is Etsy's \"Orders\" download, which doesn't list what was ordered. On Etsy, go to Shop Manager → Settings → Options → Download Data, and download \"Order Items\" instead.");
        if (Has(t, "Lineitem name") && Has(t, "Name")) return Shopify(t, includeShipped);
        return Generic(t);
    }

    static bool Has(Csv.Table t, string heading) => t.Column(heading) >= 0;

    static string Cell(Csv.Table t, int row, string heading) => t.Column(heading) is var c and >= 0 ? t.Cell(row, c).Trim() : "";

    // ---- Etsy ------------------------------------------------------------------------------------

    static CsvImport Etsy(Csv.Table t, bool includeShipped)
    {
        var items = new List<OrderItem>();
        int skipped = 0;
        for (int r = 0; r < t.Rows.Length; r++)
        {
            string Get(string h) => Cell(t, r, h);
            if (!includeShipped && Get("Date Shipped").Length > 0) { skipped++; continue; }
            var (options, personal) = EtsyVariations(Get("Variations"));
            string sku = Get("SKU");
            items.Add(new OrderItem
            {
                Key = $"etsy:{Get("Order ID")}:{Get("Transaction ID")}",
                Source = OrderSource.Etsy,
                Order = Get("Order ID"),
                Date = Date(Get("Sale Date")),
                Buyer = Get("Buyer"),
                ShipName = Get("Ship Name"),
                ShipAddress = Lines(Get("Ship Address1"), Get("Ship Address2"), Join(" ", Get("Ship City"), Get("Ship State"), Get("Ship Zipcode")), Get("Ship Country")),
                Title = Html(Get("Item Name")),
                Sku = sku,
                Quantity = Quantity(Get("Quantity")),
                Personalization = personal,
                Options = options,
            });
        }
        return new("Etsy order items", items, skipped, null);
    }

    [GeneratedRegex(@"^\s*[^,:]{1,60}:")]
    private static partial Regex OptionStart();

    /// <summary>
    /// Etsy's "Variations" column: "Personalization:Emma &amp; Jack,Colour:Walnut". A comma inside the
    /// customer's text stays in it: a new option only starts where a comma is followed by "Name:".
    /// </summary>
    public static (Dictionary<string, string> Options, string Personalization) EtsyVariations(string text)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string personal = "";
        if (string.IsNullOrWhiteSpace(text)) return (options, personal);
        var parts = new List<string>();
        foreach (var piece in text.Split(','))
        {
            bool starts = parts.Count == 0 || OptionStart().IsMatch(piece) && (!IsPersonal(parts[^1]) || KnownOption(piece));
            if (starts) parts.Add(piece);
            else parts[^1] += "," + piece;
        }
        foreach (var p in parts)
        {
            int colon = p.IndexOf(':');
            if (colon <= 0) continue;
            var key = Html(p[..colon].Trim());
            var value = Html(p[(colon + 1)..].Trim());
            if (IsPersonalKey(key))
            {
                if (!NotRequested(value)) personal = value;
            }
            else if (key.Length > 0) options[key] = value;
        }
        return (options, personal);

        static bool IsPersonal(string part) => part.IndexOf(':') is var c and > 0 && IsPersonalKey(part[..c].Trim());
        // After the customer's text, only a heading Etsy itself uses for options starts a new one
        // ("Colour:", "Size:"), not "Love:" typed by the customer. A short one-or-two-word heading counts.
        static bool KnownOption(string piece)
        {
            var key = piece[..piece.IndexOf(':')].Trim();
            return key.Length is > 0 and <= 30 && key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 && char.IsUpper(key[0]);
        }
    }

    static bool IsPersonalKey(string key) =>
        key.Contains("personali", StringComparison.OrdinalIgnoreCase) || key.Contains("engrav", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("custom text", StringComparison.OrdinalIgnoreCase) || key.Contains("inscription", StringComparison.OrdinalIgnoreCase);

    static bool NotRequested(string value) =>
        value.Length == 0 || value.StartsWith("Not requested", StringComparison.OrdinalIgnoreCase);

    // ---- Shopify ---------------------------------------------------------------------------------

    static CsvImport Shopify(Csv.Table t, bool includeShipped)
    {
        // An order's own columns (buyer, address, notes) are only on its first row; the rows under it
        // have the same Name and their own line item.
        var items = new List<OrderItem>();
        int skipped = 0;
        var first = new Dictionary<string, int>();
        var seen = new Dictionary<string, int>();
        for (int r = 0; r < t.Rows.Length; r++)
        {
            string name = Cell(t, r, "Name");
            if (name.Length == 0) continue;
            if (!first.ContainsKey(name)) first[name] = r;
            int o = first[name];
            string Order(string h) => Cell(t, r, h) is { Length: > 0 } v ? v : Cell(t, o, h);
            string line = Cell(t, r, "Lineitem name");
            if (line.Length == 0) continue;
            string status = Cell(t, r, "Lineitem fulfillment status");
            if (!includeShipped && (status.Equals("fulfilled", StringComparison.OrdinalIgnoreCase) || Order("Cancelled at").Length > 0)) { skipped++; continue; }
            var attrs = Attributes(Order("Note Attributes"));
            string personal = attrs.FirstOrDefault(a => IsPersonalKey(a.Key)).Value ?? "";
            var options = attrs.Where(a => !IsPersonalKey(a.Key)).ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
            var (title, variant) = SplitVariant(line);
            if (variant.Length > 0 && !options.ContainsKey("Variant")) options["Variant"] = variant;
            int n = seen[name + "\n" + line] = seen.GetValueOrDefault(name + "\n" + line) + 1;
            items.Add(new OrderItem
            {
                Key = ShopifyKey(name, line, n),
                Source = OrderSource.Shopify,
                Order = name,
                Date = Date(Order("Created at")),
                Buyer = Order("Billing Name"),
                ShipName = Order("Shipping Name"),
                ShipAddress = Lines(Order("Shipping Address1"), Order("Shipping Address2"), Join(" ", Order("Shipping City"), Order("Shipping Province"), Order("Shipping Zip")), Order("Shipping Country")),
                Title = title,
                Sku = Cell(t, r, "Lineitem sku"),
                Quantity = Quantity(Cell(t, r, "Lineitem quantity")),
                Personalization = personal,
                Options = options,
                Note = Order("Notes"),
            });
        }
        return new("Shopify orders", items, skipped, null);
    }

    /// <summary>The same key from the export and from the shop itself, so an order brought in both ways is one.</summary>
    public static string ShopifyKey(string order, string lineName, int n) => $"shopify:{order}:{lineName}:{n}";

    /// <summary>"Keyring - Walnut / Large" → ("Keyring", "Walnut / Large").</summary>
    public static (string Title, string Variant) SplitVariant(string line)
    {
        int at = line.LastIndexOf(" - ", StringComparison.Ordinal);
        return at > 0 ? (line[..at].Trim(), line[(at + 3)..].Trim()) : (line.Trim(), "");
    }

    /// <summary>Shopify's "Note Attributes": one "Name: value" a line.</summary>
    static List<KeyValuePair<string, string>> Attributes(string text) =>
        [.. text.Replace("\r", "").Split('\n')
            .Select(l => l.IndexOf(':') is var c and > 0 ? new KeyValuePair<string, string>(l[..c].Trim(), l[(c + 1)..].Trim()) : default)
            .Where(kv => kv.Key is { Length: > 0 })];

    // ---- any spreadsheet -------------------------------------------------------------------------

    static readonly string[] OrderHeads = ["Order", "Order ID", "Order Number", "Order No", "Order #", "Name"];
    static readonly string[] ItemHeads = ["Item", "Item Name", "Product", "Product Name", "Title", "Lineitem name"];
    static readonly string[] QtyHeads = ["Quantity", "Qty", "Lineitem quantity", "Count"];
    static readonly string[] PersonalHeads = ["Personalization", "Personalisation", "Engraving", "Text", "Custom text", "Name to engrave", "Inscription"];
    static readonly string[] BuyerHeads = ["Buyer", "Customer", "Full Name", "Customer Name", "Billing Name"];
    static readonly string[] ShipHeads = ["Ship Name", "Ship To", "Shipping Name", "Recipient"];
    static readonly string[] SkuHeads = ["SKU", "Lineitem sku", "Product Code"];
    static readonly string[] DateHeads = ["Date", "Order Date", "Sale Date", "Created at"];
    static readonly string[] NoteHeads = ["Note", "Notes", "Gift Message", "Message"];

    static CsvImport Generic(Csv.Table t)
    {
        int Col(string[] names) => names.Select(t.Column).FirstOrDefault(c => c >= 0, -1);
        int order = Col(OrderHeads), item = Col(ItemHeads), qty = Col(QtyHeads), personal = Col(PersonalHeads), buyer = Col(BuyerHeads),
            ship = Col(ShipHeads), sku = Col(SkuHeads), date = Col(DateHeads), note = Col(NoteHeads);
        var known = new HashSet<int> { order, item, qty, personal, buyer, ship, sku, date, note };
        var items = new List<OrderItem>();
        var seen = new Dictionary<string, int>();
        for (int r = 0; r < t.Rows.Length; r++)
        {
            string C(int c) => c >= 0 ? t.Cell(r, c).Trim() : "";
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Every other heading is a field of its own ({Font}, {Line 2}…).
            for (int c = 0; c < t.Headers.Length; c++)
                if (!known.Contains(c) && t.Headers[c].Trim().Length > 0) options[t.Headers[c].Trim()] = C(c);
            string rowText = string.Join("\u001f", t.Rows[r]);
            int n = seen[rowText] = seen.GetValueOrDefault(rowText) + 1;
            items.Add(new OrderItem
            {
                Key = $"csv:{Hash(rowText)}:{n}",
                Source = OrderSource.Csv,
                Order = C(order).Length > 0 ? C(order) : $"Row {r + 1}",
                Date = Date(C(date)),
                Buyer = C(buyer),
                ShipName = C(ship),
                Title = C(item),
                Sku = C(sku),
                Quantity = qty >= 0 ? Quantity(C(qty)) : 1,
                Personalization = C(personal),
                Options = options,
                Note = C(note),
            });
        }
        return new("Spreadsheet", items, 0, null);
    }

    // ---- small helpers ---------------------------------------------------------------------------

    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();

    static string Html(string s) => WebUtility.HtmlDecode(s);

    static string Join(string sep, params string[] parts) => string.Join(sep, parts.Where(p => p.Length > 0));

    static string Lines(params string[] parts) => Join("\n", parts);

    static int Quantity(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) && q > 0 ? Math.Min(q, 999) : 1;

    static readonly string[] DateFormats =
    [
        "MM/dd/yy", "M/d/yy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ssK",
    ];

    /// <summary>A date as the shops write it (Etsy 11/09/26, Shopify 2026-11-09 14:03:22 -0500), or null.</summary>
    public static DateTime? Date(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (DateTimeOffset.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var d)) return d.LocalDateTime;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d)) return d.LocalDateTime;
        return null;
    }
}
