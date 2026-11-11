using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// How to reach the shops: Etsy (an app of your own on Etsy's developer site, then Connect) and
/// Shopify (a Dev Dashboard app's client ID and secret, or an older custom app's access token).
/// Kept in the add-on's data folder; the secret parts are locked to this Windows account.
/// </summary>
public sealed class ShopSettings
{
    public string EtsyKeystring { get; set; } = "";
    public string EtsySecret { get; set; } = "";
    public string EtsyAccess { get; set; } = "";
    public string EtsyRefresh { get; set; } = "";
    public DateTime EtsyExpiresUtc { get; set; }
    public long EtsyShopId { get; set; }
    public string EtsyShopName { get; set; } = "";

    /// <summary>The shop's address: my-shop or my-shop.myshopify.com.</summary>
    public string ShopifyStore { get; set; } = "";
    public string ShopifyClientId { get; set; } = "";
    public string ShopifySecret { get; set; } = "";
    /// <summary>An older custom app's Admin API access token (shpat_…), used as it is.</summary>
    public string ShopifyToken { get; set; } = "";

    /// <summary>Also bring in what's been shipped already (for a reprint).</summary>
    public bool IncludeShipped { get; set; }

    /// <summary>The packing list on A4 paper (else US Letter).</summary>
    public bool PaperA4 { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public bool EtsyConnected => EtsyKeystring.Length > 0 && Secret.Open(EtsyRefresh).Length > 0;
    public bool ShopifyReady => ShopifyStore.Trim().Length > 0 && (Secret.Open(ShopifyToken).Length > 0 || (ShopifyClientId.Trim().Length > 0 && Secret.Open(ShopifySecret).Length > 0));

    public static ShopSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ShopSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Secrets kept on disk: on Windows locked with the account's data protection (only this Windows
/// user on this computer can read them back); elsewhere kept as typed, like the GitHub access key.
/// </summary>
public static class Secret
{
    const string Prefix = "dpapi:";

    public static string Lock(string plain)
    {
        if (string.IsNullOrEmpty(plain) || !OperatingSystem.IsWindows()) return plain ?? "";
        try { return Prefix + Convert.ToBase64String(Dpapi.Protect(Encoding.UTF8.GetBytes(plain))); }
        catch (Exception e) when (e is CryptographicException or DllNotFoundException or EntryPointNotFoundException) { return plain; }
    }

    public static string Open(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        if (!OperatingSystem.IsWindows()) return "";
        try { return Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]))); }
        catch (Exception e) when (e is CryptographicException or FormatException or DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Blob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref Blob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        const int NoUi = 1;

        public static byte[] Protect(byte[] data) => Run(data, true);
        public static byte[] Unprotect(byte[] data) => Run(data, false);

        static byte[] Run(byte[] data, bool protect)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
                bool ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out var output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out output);
                if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
                try
                {
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally { LocalFree(output.Data); }
            }
            finally { handle.Free(); }
        }
    }
}

/// <summary>A shop said no, or couldn't be reached: the message is for the person.</summary>
public sealed class ShopException(string message) : Exception(message);

/// <summary>
/// Etsy's Open API v3: connecting (OAuth with PKCE, through the browser, answered on this computer)
/// and reading the paid orders not yet shipped. Only reads: nothing in the shop is changed.
/// </summary>
public static class EtsyApi
{
    public const int Port = 3003;
    /// <summary>The callback URL to enter in the Etsy app's settings, exactly.</summary>
    public const string RedirectUri = "http://localhost:3003/orders/etsy";
    const string Api = "https://api.etsy.com/v3";

    /// <summary>A PKCE verifier and its challenge.</summary>
    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Challenge(verifier));
    }

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The page on Etsy where the shop owner allows this app to read their orders.</summary>
    public static string AuthorizeUrl(string keystring, string challenge, string state) =>
        "https://www.etsy.com/oauth/connect?response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope={Uri.EscapeDataString("transactions_r shops_r")}" +
        $"&client_id={Uri.EscapeDataString(keystring.Trim())}&state={Uri.EscapeDataString(state)}" +
        $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";

    /// <summary>
    /// Waits for the browser to come back to <see cref="RedirectUri"/> and gives the code in it.
    /// Answers the browser with a short page saying it can be closed.
    /// </summary>
    public static async Task<string> WaitForCode(string state, CancellationToken ct)
    {
        var listeners = new List<TcpListener>();
        foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try { var l = new TcpListener(ip, Port); l.Start(); listeners.Add(l); }
            catch (SocketException) { }
        }
        if (listeners.Count == 0) throw new ShopException($"Port {Port} on this computer is in use by another program, so Etsy can't send the answer back. Close it and try again.");
        try
        {
            // One waiting accept per listener; only the one that answered is started again.
            var accepts = listeners.Select(l => l.AcceptTcpClientAsync(ct).AsTask()).ToList();
            while (true)
            {
                var done = await Task.WhenAny(accepts);
                int which = accepts.IndexOf(done);
                accepts[which] = listeners[which].AcceptTcpClientAsync(ct).AsTask();
                using var client = await done;
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                int n = await stream.ReadAsync(buffer, ct);
                var request = Encoding.ASCII.GetString(buffer, 0, n);
                var (code, gotState, error) = ReadCallback(request);
                bool ours = gotState == state && (code is not null || error is not null);
                var page = ours
                    ? error is null ? "Connected. You can close this tab and go back to PhotonCanvas." : $"Etsy said: {WebUtility.HtmlEncode(error)}. Go back to PhotonCanvas and try again."
                    : "Waiting for Etsy…";
                var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>PhotonCanvas</title><body style=\"font-family:sans-serif;padding:3em\"><p>{page}</p>");
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, ct);
                await stream.WriteAsync(body, ct);
                if (!ours) continue;
                if (error is not null) throw new ShopException($"Etsy didn't connect: {error}.");
                return code!;
            }
        }
        finally
        {
            foreach (var l in listeners) l.Stop();
        }
    }

    /// <summary>The code, state and error from the browser's "GET /orders/etsy?code=…&amp;state=…" request.</summary>
    public static (string? Code, string? State, string? Error) ReadCallback(string request)
    {
        var first = request.Split('\n')[0];
        var parts = first.Split(' ');
        if (parts.Length < 2 || !parts[1].StartsWith("/orders/etsy", StringComparison.Ordinal)) return (null, null, null);
        int q = parts[1].IndexOf('?');
        if (q < 0) return (null, null, null);
        var query = parts[1][(q + 1)..].Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");
        return (query.GetValueOrDefault("code"), query.GetValueOrDefault("state"),
            query.TryGetValue("error", out var e) ? query.GetValueOrDefault("error_description") is { Length: > 0 } d ? d : e : null);
    }

    static string ApiKey(ShopSettings s) =>
        Secret.Open(s.EtsySecret) is { Length: > 0 } secret ? $"{s.EtsyKeystring.Trim()}:{secret.Trim()}" : s.EtsyKeystring.Trim();

    /// <summary>Turns the code from the browser into access and refresh tokens, and finds the shop.</summary>
    public static async Task Connect(HttpClient http, ShopSettings s, string code, string verifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = s.EtsyKeystring.Trim(),
            ["redirect_uri"] = RedirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        };
        await Tokens(http, s, form, ct);
        var me = await Get(http, s, $"{Api}/application/users/me", ct);
        s.EtsyShopId = me["shop_id"]?.GetValue<long>() ?? 0;
        if (s.EtsyShopId == 0) throw new ShopException("That Etsy account has no shop.");
        try
        {
            var shop = await Get(http, s, $"{Api}/application/shops/{s.EtsyShopId}", ct);
            s.EtsyShopName = shop["shop_name"]?.GetValue<string>() ?? "";
        }
        catch (ShopException) { s.EtsyShopName = ""; }
    }

    static async Task Tokens(HttpClient http, ShopSettings s, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/public/oauth/token") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Add("x-api-key", ApiKey(s));
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new ShopException($"Etsy refused the connection ({(int)resp.StatusCode}): {Brief(text)}");
        var json = JsonNode.Parse(text)!;
        s.EtsyAccess = Secret.Lock(json["access_token"]!.GetValue<string>());
        if (json["refresh_token"]?.GetValue<string>() is { Length: > 0 } refresh) s.EtsyRefresh = Secret.Lock(refresh);
        s.EtsyExpiresUtc = DateTime.UtcNow.AddSeconds((json["expires_in"]?.GetValue<int>() ?? 3600) - 60);
    }

    static async Task EnsureToken(HttpClient http, ShopSettings s, CancellationToken ct)
    {
        if (Secret.Open(s.EtsyAccess).Length > 0 && DateTime.UtcNow < s.EtsyExpiresUtc) return;
        if (Secret.Open(s.EtsyRefresh) is not { Length: > 0 } refresh) throw new ShopException("Connect to Etsy first (Shops…).");
        await Tokens(http, s, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = s.EtsyKeystring.Trim(),
            ["refresh_token"] = refresh,
        }, ct);
    }

    static async Task<JsonNode> Get(HttpClient http, ShopSettings s, string url, CancellationToken ct)
    {
        await EnsureToken(http, s, ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey(s));
        req.Headers.Authorization = new("Bearer", Secret.Open(s.EtsyAccess));
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new ShopException("Etsy no longer accepts the connection. Connect again (Shops…).");
        if (!resp.IsSuccessStatusCode) throw new ShopException($"Etsy answered {(int)resp.StatusCode}: {Brief(text)}");
        return JsonNode.Parse(text)!;
    }

    /// <summary>The shop's paid orders that haven't been shipped (or all recent ones, with <paramref name="includeShipped"/>).</summary>
    public static async Task<List<OrderItem>> Fetch(HttpClient http, ShopSettings s, bool includeShipped, CancellationToken ct)
    {
        if (s.EtsyShopId == 0) throw new ShopException("Connect to Etsy first (Shops…).");
        var items = new List<OrderItem>();
        for (int offset = 0, page = 0; page < 20; page++, offset += 100)
        {
            var url = $"{Api}/application/shops/{s.EtsyShopId}/receipts?was_paid=true&was_canceled=false&limit=100&offset={offset}" + (includeShipped ? "" : "&was_shipped=false");
            var json = await Get(http, s, url, ct);
            var got = ParseReceipts(json);
            items.AddRange(got);
            int count = json["count"]?.GetValue<int>() ?? 0;
            if ((json["results"] as JsonArray)?.Count is not > 0 || offset + 100 >= count) break;
        }
        return items;
    }

    /// <summary>The lines of a receipts answer (the same keys as the "Order Items" CSV, so both ways meet).</summary>
    public static List<OrderItem> ParseReceipts(JsonNode json)
    {
        var items = new List<OrderItem>();
        foreach (var r in json["results"] as JsonArray ?? [])
        {
            if (r is null) continue;
            string receipt = Num(r["receipt_id"]);
            string name = Str(r["name"]);
            var address = string.Join("\n", new[]
            {
                Str(r["first_line"]), Str(r["second_line"]),
                string.Join(" ", new[] { Str(r["city"]), Str(r["state"]), Str(r["zip"]) }.Where(x => x.Length > 0)),
                Str(r["country_iso"]),
            }.Where(x => x.Length > 0));
            DateTime? date = r["create_timestamp"] is JsonValue ts && ts.TryGetValue<long>(out long secs) ? DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime : null;
            foreach (var t in r["transactions"] as JsonArray ?? [])
            {
                if (t is null) continue;
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string personal = "";
                foreach (var v in t["variations"] as JsonArray ?? [])
                {
                    var key = WebUtility.HtmlDecode(Str(v?["formatted_name"]));
                    var value = WebUtility.HtmlDecode(Str(v?["formatted_value"]));
                    if (key.Length == 0) continue;
                    if (IsPersonal(key)) { if (!value.StartsWith("Not requested", StringComparison.OrdinalIgnoreCase)) personal = value; }
                    else options[key] = value;
                }
                if (personal.Length == 0 && Str(t["personalization"]) is { Length: > 0 } p) personal = WebUtility.HtmlDecode(p);
                items.Add(new OrderItem
                {
                    Key = $"etsy:{receipt}:{Num(t["transaction_id"])}",
                    Source = OrderSource.Etsy,
                    Order = receipt,
                    Date = date,
                    Buyer = name,
                    ShipName = name,
                    ShipAddress = address,
                    Title = WebUtility.HtmlDecode(Str(t["title"])),
                    Sku = Str(t["sku"]),
                    Quantity = t["quantity"] is JsonValue q && q.TryGetValue<int>(out int qty) && qty > 0 ? qty : 1,
                    Personalization = personal,
                    Options = options,
                    Note = WebUtility.HtmlDecode(Str(r["message_from_buyer"])),
                });
            }
        }
        return items;
    }

    static bool IsPersonal(string key) =>
        key.Contains("personali", StringComparison.OrdinalIgnoreCase) || key.Contains("engrav", StringComparison.OrdinalIgnoreCase);

    internal static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    internal static string Num(JsonNode? n) => n is JsonValue v ? v.TryGetValue<long>(out long l) ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : Str(n) : "";

    internal static string Brief(string text)
    {
        try
        {
            var j = JsonNode.Parse(text);
            var msg = Str(j?["error_description"]) is { Length: > 0 } d ? d : Str(j?["error"]) is { Length: > 0 } e ? e : Str(j?["errors"]);
            if (msg.Length > 0) return msg;
        }
        catch (JsonException) { }
        text = text.Trim();
        return text.Length > 200 ? text[..200] + "…" : text.Length == 0 ? "no details" : text;
    }
}

/// <summary>
/// Shopify's Admin API (GraphQL): the shop's open orders, the lines not yet fulfilled. Only reads.
/// Needs an app with the read_orders permission installed on the shop: a Dev Dashboard app's
/// client ID and secret (a token for a day is asked for each time), or an older custom app's token.
/// </summary>
public static class ShopifyApi
{
    public const string Version = "2026-04";

    /// <summary>"my-shop", "my-shop.myshopify.com" or its admin URL → "my-shop.myshopify.com".</summary>
    public static string Host(string store)
    {
        var s = store.Trim().ToLowerInvariant();
        if (s.StartsWith("https://")) s = s[8..];
        if (s.StartsWith("http://")) s = s[7..];
        s = s.Split('/')[0];
        if (s.StartsWith("admin.shopify.com")) s = store.Trim().ToLowerInvariant().Split("/store/") is { Length: > 1 } p ? p[1].Split('/')[0] : "";
        if (s.Length > 0 && !s.Contains('.')) s += ".myshopify.com";
        return s;
    }

    static async Task<string> Token(HttpClient http, ShopSettings s, string host, CancellationToken ct)
    {
        if (Secret.Open(s.ShopifyToken) is { Length: > 0 } token) return token.Trim();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/admin/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = s.ShopifyClientId.Trim(),
                ["client_secret"] = Secret.Open(s.ShopifySecret).Trim(),
            }),
        };
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new ShopException($"Shopify refused the app's client ID and secret ({(int)resp.StatusCode}): {EtsyApi.Brief(text)}. Is the app installed on {host}, in the same organization as the shop?");
        return JsonNode.Parse(text)!["access_token"]!.GetValue<string>();
    }

    const string Full = """
        query Orders($after: String) {
          orders(first: 50, after: $after, query: "status:open", sortKey: CREATED_AT) {
            pageInfo { hasNextPage endCursor }
            nodes {
              name createdAt note cancelledAt
              customAttributes { key value }
              customer { displayName }
              shippingAddress { name address1 address2 city province zip country }
              lineItems(first: 100) { nodes { name title variantTitle sku quantity unfulfilledQuantity customAttributes { key value } } }
            }
          }
        }
        """;

    // Without the buyer's name and address, for an app not allowed to see customers' details.
    const string Plain = """
        query Orders($after: String) {
          orders(first: 50, after: $after, query: "status:open", sortKey: CREATED_AT) {
            pageInfo { hasNextPage endCursor }
            nodes {
              name createdAt note cancelledAt
              customAttributes { key value }
              lineItems(first: 100) { nodes { name title variantTitle sku quantity unfulfilledQuantity customAttributes { key value } } }
            }
          }
        }
        """;

    /// <summary>The open orders' lines still to be fulfilled (all lines with <paramref name="includeShipped"/>). Also says when names and addresses were left out.</summary>
    public static async Task<(List<OrderItem> Items, bool NoCustomers)> Fetch(HttpClient http, ShopSettings s, bool includeShipped, CancellationToken ct)
    {
        var host = Host(s.ShopifyStore);
        if (host.Length == 0) throw new ShopException("Enter the shop's address first (Shops…).");
        var token = await Token(http, s, host, ct);
        var items = new List<OrderItem>();
        string query = Full;
        bool plain = false;
        string? after = null;
        for (int page = 0; page < 40; page++)
        {
            var body = new JsonObject { ["query"] = query, ["variables"] = new JsonObject { ["after"] = after } };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/admin/api/{Version}/graphql.json")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Shopify-Access-Token", token);
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ShopException($"Shopify didn't accept the app ({(int)resp.StatusCode}). Check it's installed on the shop and has the read_orders permission.");
            if (!resp.IsSuccessStatusCode) throw new ShopException($"Shopify answered {(int)resp.StatusCode}: {EtsyApi.Brief(text)}");
            var json = JsonNode.Parse(text)!;
            if (json["errors"] is JsonArray { Count: > 0 } errors)
            {
                var msg = string.Join(" ", errors.Select(e => EtsyApi.Str(e?["message"])));
                // An app not approved for customers' details can still read the orders without them.
                if (!plain && (msg.Contains("access", StringComparison.OrdinalIgnoreCase) || msg.Contains("customer", StringComparison.OrdinalIgnoreCase)))
                {
                    query = Plain;
                    plain = true;
                    page--;
                    continue;
                }
                throw new ShopException($"Shopify said: {msg}");
            }
            var orders = json["data"]?["orders"];
            items.AddRange(ParseOrders(orders, includeShipped));
            if (orders?["pageInfo"]?["hasNextPage"]?.GetValue<bool>() != true) break;
            after = orders["pageInfo"]!["endCursor"]?.GetValue<string>();
        }
        return (items, plain);
    }

    /// <summary>The lines of an "orders" answer (the same keys as the CSV export, so both ways meet).</summary>
    public static List<OrderItem> ParseOrders(JsonNode? orders, bool includeShipped = false)
    {
        var items = new List<OrderItem>();
        foreach (var o in orders?["nodes"] as JsonArray ?? [])
        {
            if (o is null || (!includeShipped && EtsyApi.Str(o["cancelledAt"]).Length > 0)) continue;
            string name = EtsyApi.Str(o["name"]);
            var orderAttrs = Attrs(o["customAttributes"]);
            var ship = o["shippingAddress"];
            string shipName = EtsyApi.Str(ship?["name"]);
            var address = string.Join("\n", new[]
            {
                EtsyApi.Str(ship?["address1"]), EtsyApi.Str(ship?["address2"]),
                string.Join(" ", new[] { EtsyApi.Str(ship?["city"]), EtsyApi.Str(ship?["province"]), EtsyApi.Str(ship?["zip"]) }.Where(x => x.Length > 0)),
                EtsyApi.Str(ship?["country"]),
            }.Where(x => x.Length > 0));
            DateTime? date = OrderCsv.Date(EtsyApi.Str(o["createdAt"]));
            var seen = new Dictionary<string, int>();
            foreach (var l in o["lineItems"]?["nodes"] as JsonArray ?? [])
            {
                if (l is null) continue;
                string lineName = EtsyApi.Str(l["name"]);
                if (lineName.Length == 0) lineName = EtsyApi.Str(l["title"]);
                int n = seen[lineName] = seen.GetValueOrDefault(lineName) + 1;
                int qty = l["quantity"]?.GetValue<int>() ?? 1;
                int open = l["unfulfilledQuantity"]?.GetValue<int>() ?? qty;
                if (!includeShipped && open <= 0) continue;
                var attrs = orderAttrs.Concat(Attrs(l["customAttributes"])).ToList();
                var personal = attrs.LastOrDefault(a => IsPersonal(a.Key)).Value ?? "";
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in attrs.Where(a => !IsPersonal(a.Key))) options[a.Key] = a.Value;
                var variant = EtsyApi.Str(l["variantTitle"]);
                if (variant.Length > 0 && variant != "Default Title") options.TryAdd("Variant", variant);
                items.Add(new OrderItem
                {
                    Key = OrderCsv.ShopifyKey(name, lineName, n),
                    Source = OrderSource.Shopify,
                    Order = name,
                    Date = date,
                    Buyer = EtsyApi.Str(o["customer"]?["displayName"]),
                    ShipName = shipName,
                    ShipAddress = address,
                    Title = EtsyApi.Str(l["title"]),
                    Sku = EtsyApi.Str(l["sku"]),
                    Quantity = Math.Max(1, includeShipped ? qty : open),
                    Personalization = personal,
                    Options = options,
                    Note = EtsyApi.Str(o["note"]),
                });
            }
        }
        return items;
    }

    // Properties starting with "_" are hidden ones apps keep for themselves.
    static List<KeyValuePair<string, string>> Attrs(JsonNode? list) =>
        [.. (list as JsonArray ?? []).Select(a => new KeyValuePair<string, string>(EtsyApi.Str(a?["key"]), EtsyApi.Str(a?["value"])))
            .Where(a => a.Key.Length > 0 && !a.Key.StartsWith('_'))];

    static bool IsPersonal(string key) =>
        key.Contains("personali", StringComparison.OrdinalIgnoreCase) || key.Contains("engrav", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("custom text", StringComparison.OrdinalIgnoreCase) || key.Contains("inscription", StringComparison.OrdinalIgnoreCase);
}
