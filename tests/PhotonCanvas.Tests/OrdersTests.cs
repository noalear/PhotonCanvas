using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PhotonCanvas.Addon.Orders;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>The orders add-on: reading shops' exports and answers, products, sheets and the packing list.</summary>
public class OrdersTests
{
    // ---- CSV exports ----

    [Fact]
    public void EtsyOrderItemsCsvIsReadWithPersonalizationAndOptions()
    {
        var r = OrderCsv.Read(Samples.EtsyCsv);
        Assert.Null(r.Problem);
        Assert.Equal("Etsy order items", r.Kind);
        Assert.Equal(4, r.Items.Count);
        Assert.Equal(1, r.Skipped); // Ava's was shipped

        var emma = r.Items.Single(i => i.Buyer == "Emma Larsen");
        Assert.Equal("etsy:3300100100:4011001001", emma.Key);
        Assert.Equal(OrderSource.Etsy, emma.Source);
        Assert.Equal("Emma & Jack", emma.Personalization);
        Assert.Equal("Walnut", emma.Options["Wood"]);
        Assert.Equal("Personalized Wooden Keychain \"Name\" Laser Engraved", emma.Title);
        Assert.Equal("KEY-WAL", emma.Sku);
        Assert.Equal(new DateTime(2026, 11, 8), emma.Date!.Value.Date);
        Assert.Equal("12 Birch Lane\nPortland OR 97201\nUnited States", emma.ShipAddress);

        // A comma in the customer's words stays in them; the option after it is still found.
        var noah = r.Items.Single(i => i.Buyer == "Noah Brooks");
        Assert.Equal("Dad, love you", noah.Personalization);
        Assert.Equal("Maple", noah.Options["Wood"]);
        Assert.Equal(2, noah.Quantity);

        // Two lines of personalization; "Not requested" is nothing.
        Assert.Equal("The Chens\nEst. 2019", r.Items.Single(i => i.Buyer == "Olivia Chen").Personalization);
        Assert.Equal("", r.Items.Single(i => i.Buyer == "Liam Novak").Personalization);

        Assert.Equal(5, OrderCsv.Read(Samples.EtsyCsv, includeShipped: true).Items.Count);
    }

    [Fact]
    public void EtsyOrdersCsvWithoutItemsSaysWhichDownloadToUse()
    {
        var r = OrderCsv.Read("Sale Date,Order ID,Buyer User ID,Full Name,Number of Items\n11/08/26,1,2,Emma,1\n");
        Assert.NotNull(r.Problem);
        Assert.Contains("Order Items", r.Problem);
        Assert.Empty(r.Items);
    }

    [Fact]
    public void ShopifyExportCarriesTheOrderDownToItsOtherLines()
    {
        var r = OrderCsv.Read(Samples.ShopifyCsv);
        Assert.Null(r.Problem);
        Assert.Equal("Shopify orders", r.Kind);
        Assert.Equal(3, r.Items.Count);
        Assert.Equal(1, r.Skipped); // #1000 was fulfilled

        var key = r.Items.Single(i => i.Order == "#1001" && i.Sku == "KEY-WAL");
        Assert.Equal("Sofia", key.Personalization);
        Assert.Equal("Script", key.Options["Font"]);
        Assert.Equal("Walnut", key.Options["Variant"]);
        Assert.Equal("Name Keychain", key.Title);
        Assert.Equal("Gift wrap please", key.Note);
        Assert.Equal("shopify:#1001:Name Keychain - Walnut:1", key.Key);

        // The second line of #1001 has only its own columns: the buyer and address come from the first.
        var coaster = r.Items.Single(i => i.Order == "#1001" && i.Sku == "COASTER-RND");
        Assert.Equal("Sofia Rossi", coaster.ShipName);
        Assert.Equal("Sofia Rossi", coaster.Buyer);
        Assert.Contains("Chicago", coaster.ShipAddress);
        Assert.Equal(new DateTime(2026, 11, 9), coaster.Date!.Value.Date);

        Assert.Equal(2, r.Items.Single(i => i.Order == "#1002").Quantity);
        Assert.Equal("Team Silva", r.Items.Single(i => i.Order == "#1002").Personalization);
    }

    [Fact]
    public void AnySpreadsheetGivesEachHeadingAsAField()
    {
        const string csv = "Order,Customer,Product,Qty,Engraving,Font,Line 2\n" +
                           "A-1,Jo,Coaster,2,Smith Family,Script,Est 2020\n" +
                           "A-2,Kim,Keyring,1,Kim,Block,\n";
        var r = OrderCsv.Read(csv);
        Assert.Equal("Spreadsheet", r.Kind);
        Assert.Equal(2, r.Items.Count);
        var a = r.Items[0];
        Assert.Equal("A-1", a.Order);
        Assert.Equal("Jo", a.Buyer);
        Assert.Equal("Coaster", a.Title);
        Assert.Equal(2, a.Quantity);
        Assert.Equal("Smith Family", a.Personalization);
        Assert.Equal("Script", a.Options["Font"]);
        Assert.Equal("Est 2020", OrderFields.For(a)["Line 2"]);
        // The same file again gives the same keys, so nothing is doubled.
        Assert.Equal(r.Items.Select(i => i.Key), OrderCsv.Read(csv).Items.Select(i => i.Key));
    }

    // ---- the order list ----

    [Fact]
    public void BringingOrdersInAgainKeepsTheirProgress()
    {
        var book = new OrderBook();
        var first = OrderCsv.Read(Samples.EtsyCsv).Items;
        Assert.Equal((4, 0), book.Merge(first));
        book.Update(first[0].Key, i => i with { Status = ItemStatus.Burned, Sheet = "Orders 1", Product = "Name keyring" });

        // Etsy changed the personalization of the first line since.
        var again = OrderCsv.Read(Samples.EtsyCsv).Items;
        again[0] = again[0] with { Personalization = "Emma & Jake" };
        Assert.Equal((0, 1), book.Merge(again));
        var had = book.Find(first[0].Key)!;
        Assert.Equal("Emma & Jake", had.Personalization);
        Assert.Equal(ItemStatus.Burned, had.Status);
        Assert.Equal("Name keyring", had.Product);

        var dir = Path.Combine(Path.GetTempPath(), "pc-orders-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "orders.json");
            book.Save(path);
            var back = OrderBook.Load(path);
            Assert.Equal(4, back.Items.Count);
            Assert.Equal(ItemStatus.Burned, back.Find(first[0].Key)!.Status);
            Assert.Equal("Walnut", back.Find(first[0].Key)!.Options["Wood"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FieldsAreWrittenInAndTheProgramsOwnAreLeft()
    {
        var item = new OrderItem
        {
            Order = "#1001", Buyer = "sofia rossi", Personalization = "Sofia {the best}\nForever", Quantity = 3,
            Options = new() { ["Font"] = "Script" },
        };
        var f = OrderFields.For(item, 2, 3);
        Assert.Equal("For Sofia (the best) no. {serial:000} on {date}", OrderFields.Bake("For {Personalization 1} no. {serial:000} on {date}", f));
        Assert.Equal("SOFIA ROSSI / Sofia Rossi / sofia", OrderFields.Bake("{Buyer:upper} / {Buyer:title} / {Buyer:first}", f));
        Assert.Equal("{{literal}} Script 2 of 3 #1001", OrderFields.Bake("{{literal}} {Font} {Piece} {Order}", f));
        Assert.Equal("Forever", OrderFields.Bake("{Personalization 2}", f));
        Assert.Equal("Sofia {the best}\nForever".Replace('{', '(').Replace('}', ')'), OrderFields.Bake("{Personalization}", f));

        var none = OrderFields.For(new OrderItem { Order = "1" });
        Assert.Equal(["Personalization"], OrderFields.Empty("{Personalization} {Personalization 2} {Piece}", none).ToList());
    }

    [Fact]
    public void ProductsMatchBySkuFirstThenWords()
    {
        var list = new ProductList
        {
            Products =
            {
                new Product { Name = "Any keyring", Matches = "keychain" },
                new Product { Name = "Walnut keyring", Matches = "KEY-WAL" },
                new Product { Name = "Coaster", Matches = "Coaster\nRound" },
                new Product { Name = "Everything else", Matches = "*" },
            },
        };
        var items = OrderCsv.Read(Samples.EtsyCsv).Items;
        Assert.Equal("Walnut keyring", list.For(items.Single(i => i.Buyer == "Emma Larsen"))!.Name);
        Assert.Equal("Any keyring", list.For(items.Single(i => i.Buyer == "Noah Brooks"))!.Name);
        Assert.Equal("Coaster", list.For(items.Single(i => i.Buyer == "Olivia Chen"))!.Name);
        Assert.Equal("Everything else", list.For(new OrderItem { Title = "Gift card" })!.Name);
        Assert.Equal("Coaster", list.For(items[0] with { Product = "Coaster" })!.Name);
        Assert.Null(new ProductList().For(items[0]));
    }

    // ---- the shops' own answers ----

    const string EtsyReceipts = """
        {"count":1,"results":[{"receipt_id":3300100100,"name":"Emma Larsen","first_line":"12 Birch Lane","second_line":"","city":"Portland","state":"OR","zip":"97201","country_iso":"US",
          "message_from_buyer":"It&#39;s a gift","create_timestamp":1794240000,
          "transactions":[{"transaction_id":4011001001,"title":"Personalized Wooden Keychain &quot;Name&quot;","quantity":1,"sku":"KEY-WAL",
            "variations":[{"property_id":54,"value_id":1,"formatted_name":"Personalization","formatted_value":"Emma &amp; Jack"},{"property_id":513,"value_id":2,"formatted_name":"Wood","formatted_value":"Walnut"}]}]}]}
        """;

    [Fact]
    public void EtsyReceiptsGiveTheSameLinesAsItsCsv()
    {
        var items = EtsyApi.ParseReceipts(JsonNode.Parse(EtsyReceipts)!);
        var line = Assert.Single(items);
        Assert.Equal("etsy:3300100100:4011001001", line.Key);
        Assert.Equal("Emma & Jack", line.Personalization);
        Assert.Equal("Walnut", line.Options["Wood"]);
        Assert.Equal("It's a gift", line.Note);
        Assert.Equal("Personalized Wooden Keychain \"Name\"", line.Title);
        Assert.Contains("Portland OR 97201", line.ShipAddress);
        // Brought in from the CSV before: the same line, not a second one.
        var book = new OrderBook();
        book.Merge(OrderCsv.Read(Samples.EtsyCsv).Items);
        Assert.Equal(0, book.Merge(items).Added);
    }

    const string ShopifyOrders = """
        {"data":{"orders":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
          {"name":"#1001","createdAt":"2026-11-09T15:12:41Z","note":"Gift wrap please","cancelledAt":null,"customAttributes":[],
           "customer":{"displayName":"Sofia Rossi"},
           "shippingAddress":{"name":"Sofia Rossi","address1":"9 Lake View","address2":null,"city":"Chicago","province":"Illinois","zip":"60601","country":"United States"},
           "lineItems":{"nodes":[
             {"name":"Name Keychain - Walnut","title":"Name Keychain","variantTitle":"Walnut","sku":"KEY-WAL","quantity":1,"unfulfilledQuantity":1,"customAttributes":[{"key":"Engraving","value":"Sofia"},{"key":"_app_id","value":"x"}]},
             {"name":"Slate Coaster - Round","title":"Slate Coaster","variantTitle":"Round","sku":"COASTER-RND","quantity":2,"unfulfilledQuantity":0,"customAttributes":[]}]}},
          {"name":"#0999","createdAt":"2026-11-01T10:00:00Z","note":null,"cancelledAt":"2026-11-02T10:00:00Z","customAttributes":[],"lineItems":{"nodes":[]}}]}}}
        """;

    [Fact]
    public void ShopifyOrdersGiveTheLinesStillToMake()
    {
        var items = ShopifyApi.ParseOrders(JsonNode.Parse(ShopifyOrders)!["data"]!["orders"]);
        var line = Assert.Single(items); // the coaster is fulfilled, #0999 cancelled
        Assert.Equal("shopify:#1001:Name Keychain - Walnut:1", line.Key);
        Assert.Equal("Sofia", line.Personalization);
        Assert.False(line.Options.ContainsKey("_app_id"));
        Assert.Equal("Walnut", line.Options["Variant"]);
        Assert.Equal("Sofia Rossi", line.Buyer);
        // The CSV export's line for the same thing has the same key.
        Assert.Contains(OrderCsv.Read(Samples.ShopifyCsv).Items, i => i.Key == line.Key);
        Assert.Equal(2, ShopifyApi.ParseOrders(JsonNode.Parse(ShopifyOrders)!["data"]!["orders"], includeShipped: true).Count);
    }

    [Theory]
    [InlineData("my-shop", "my-shop.myshopify.com")]
    [InlineData("My-Shop.myshopify.com", "my-shop.myshopify.com")]
    [InlineData("https://my-shop.myshopify.com/admin/orders", "my-shop.myshopify.com")]
    [InlineData("https://admin.shopify.com/store/my-shop/orders", "my-shop.myshopify.com")]
    public void ShopifyShopAddressIsTidied(string typed, string host) => Assert.Equal(host, ShopifyApi.Host(typed));

    [Fact]
    public void EtsyConnectsWithPkceAndReadsTheBrowsersAnswer()
    {
        // SHA-256, base64url without padding (worked out apart from the program).
        Assert.Equal("Qq3VqDi5tBnIsE436H__FNAwDOSYnUKANpD3NmRFg3o", EtsyApi.Challenge("dBjftJeZ4CVP-mJ92K9qPgRwNodfgXDQSaBwHn8jvrXk"));
        var (verifier, challenge) = EtsyApi.Pkce();
        Assert.Equal(43, verifier.Length);
        Assert.Equal(EtsyApi.Challenge(verifier), challenge);
        var url = EtsyApi.AuthorizeUrl("abc123", "chal", "st8");
        Assert.StartsWith("https://www.etsy.com/oauth/connect?response_type=code", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A3003%2Forders%2Fetsy", url);
        Assert.Contains("scope=transactions_r%20shops_r", url);
        Assert.Contains("code_challenge_method=S256", url);

        Assert.Equal(("the-code", "st8", (string?)null), EtsyApi.ReadCallback("GET /orders/etsy?code=the-code&state=st8 HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        Assert.Equal((null, "st8", "access_denied"), EtsyApi.ReadCallback("GET /orders/etsy?error=access_denied&state=st8 HTTP/1.1\r\n"));
        Assert.Equal((null, null, null), EtsyApi.ReadCallback("GET /favicon.ico HTTP/1.1\r\n"));
    }

    [Fact]
    public async Task TheBrowsersReturnIsCaughtOnThisComputer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = EtsyApi.WaitForCode("st8", cts.Token);
        using var http = new HttpClient();
        // Something else first (a browser asks for its icon), then Etsy's answer.
        var other = await http.GetStringAsync($"http://127.0.0.1:{EtsyApi.Port}/favicon.ico", cts.Token);
        Assert.Contains("Waiting", other);
        Assert.False(waiting.IsCompleted);
        var page = await http.GetStringAsync($"http://127.0.0.1:{EtsyApi.Port}/orders/etsy?code=abc%20d&state=st8", cts.Token);
        Assert.Contains("close this tab", page);
        Assert.Equal("abc d", await waiting);
    }

    /// <summary>Answers requests from a list, keeping what was asked.</summary>
    sealed class FakeShop(Func<HttpRequestMessage, string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public readonly List<(string Url, string Body, string? Token)> Asked = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Asked.Add((request.RequestUri!.ToString(), body, request.Headers.TryGetValues("X-Shopify-Access-Token", out var t) ? t.First() : null));
            var (code, text) = answer(request, body);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ShopifyAsksForADaysTokenThenReadsWithoutCustomersWhenNotAllowed()
    {
        var shop = new FakeShop((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/admin/oauth/access_token") return (HttpStatusCode.OK, """{"access_token":"tok-1","scope":"read_orders","expires_in":86399}""");
            if (body.Contains("shippingAddress")) return (HttpStatusCode.OK, """{"errors":[{"message":"This app is not approved to access the Customer object."}]}""");
            return (HttpStatusCode.OK, ShopifyOrders);
        });
        using var http = new HttpClient(shop);
        var s = new ShopSettings { ShopifyStore = "my-shop", ShopifyClientId = "cid", ShopifySecret = "csecret" };
        var (items, noCustomers) = await ShopifyApi.Fetch(http, s, false, CancellationToken.None);
        Assert.True(noCustomers);
        Assert.Single(items);
        Assert.Equal("https://my-shop.myshopify.com/admin/oauth/access_token", shop.Asked[0].Url);
        Assert.Contains("grant_type=client_credentials", shop.Asked[0].Body);
        Assert.Contains("client_secret=csecret", shop.Asked[0].Body);
        Assert.All(shop.Asked.Skip(1), a => Assert.Equal("tok-1", a.Token));
        Assert.All(shop.Asked.Skip(1), a => Assert.Equal($"https://my-shop.myshopify.com/admin/api/{ShopifyApi.Version}/graphql.json", a.Url));
    }

    [Fact]
    public async Task EtsyRefreshesItsTokenAndReadsReceipts()
    {
        var calls = new List<string>();
        var shop = new FakeShop((req, body) =>
        {
            calls.Add(req.Method + " " + req.RequestUri!.PathAndQuery + " key=" + string.Join(",", req.Headers.GetValues("x-api-key")) + " auth=" + req.Headers.Authorization);
            if (req.RequestUri.AbsolutePath == "/v3/public/oauth/token") return (HttpStatusCode.OK, """{"access_token":"12.newaccess","token_type":"Bearer","expires_in":3600,"refresh_token":"12.newrefresh"}""");
            return (HttpStatusCode.OK, EtsyReceipts);
        });
        using var http = new HttpClient(shop);
        var s = new ShopSettings { EtsyKeystring = "ks", EtsySecret = "sec", EtsyRefresh = "12.oldrefresh", EtsyShopId = 77, EtsyExpiresUtc = DateTime.UtcNow.AddHours(-1) };
        var items = await EtsyApi.Fetch(http, s, false, CancellationToken.None);
        Assert.Single(items);
        Assert.StartsWith("POST /v3/public/oauth/token key=ks:sec", calls[0]);
        Assert.Contains("/v3/application/shops/77/receipts?was_paid=true&was_canceled=false&limit=100&offset=0&was_shipped=false", calls[1]);
        Assert.EndsWith("auth=Bearer 12.newaccess", calls[1]);
        Assert.Equal("12.newrefresh", Secret.Open(s.EtsyRefresh));
    }

    // ---- sheets ----

    static List<Piece> SamplePieces(params string[] buyers)
    {
        var products = Samples.Products();
        var list = new ProductList { Products = [.. products.Select(p => p.Product)] };
        var designs = products.ToDictionary(p => p.Product.Name, p => p.Design);
        var items = OrderCsv.Read(Samples.EtsyCsv).Items.Concat(OrderCsv.Read(Samples.ShopifyCsv).Items)
            .Where(i => buyers.Length == 0 || buyers.Contains(i.Buyer)).ToList();
        var pieces = new List<Piece>();
        foreach (var i in items)
        {
            var p = list.For(i)!;
            for (int n = 1; n <= i.Quantity; n++) pieces.Add(new Piece(i, n, p, designs[p.Name]));
        }
        return pieces;
    }

    [Fact]
    public void SheetsHoldOneCopyPerPieceWithTheCustomersWordsAndAPagePerMaterial()
    {
        var pieces = SamplePieces();
        Assert.Equal(9, pieces.Count); // Etsy: 4 keyrings and a coaster; Shopify: 3 keyrings and a coaster
        var o = new SheetOptions { Area = new Rect2(0, 0, 220, 220), Title = "Orders" };
        var r = SheetMaker.Make(DocState.New(), pieces, o, new DateTime(2026, 11, 11));
        Assert.Equal(9, r.Pieces);
        Assert.Equal(2, r.Sheets); // wood and slate don't share
        var names = Pages.Names(r.State);
        Assert.Equal(["Orders: Wood 3 mm", "Orders: Slate"], names);

        var sheets = SheetMaker.Read(r.State);
        Assert.Equal(7, sheets[0].Pieces.Count);
        Assert.Equal(2, sheets[1].Pieces.Count);
        Assert.Equal(Enumerable.Range(1, 7), sheets[0].Pieces.Select(p => p.Number));

        // The keyring page: the customers' words written into the text, nothing left as a field.
        var texts = r.State.Shapes.OfType<TextShape>().Where(t => r.State.FindLayer(t.LayerId)!.Output).Select(t => t.Text).ToList();
        Assert.Contains("Emma & Jack", texts);
        Assert.Contains("Dad, love you", texts);
        Assert.Contains("Team Silva", texts);
        Assert.DoesNotContain(texts, t => t.Contains('{'));
        Assert.Equal(2, texts.Count(t => t == "Dad, love you")); // quantity 2

        // Inside the area (5 mm margin), and no two pieces overlap.
        var boxes = sheets[0].Pieces.Select(p => p.Box).ToList();
        Assert.All(boxes, b => Assert.True(new Rect2(5, 5, 215, 215).Contains(b.Inflate(-0.01)), $"{b} outside"));
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].Inflate(-0.5).Intersects(boxes[j].Inflate(-0.5)), $"pieces {i + 1} and {j + 1} overlap");

        // Labels are on a layer that isn't burned, and each piece is one group.
        var labelLayer = r.State.Layers.Single(l => l.Name == SheetMaker.LabelLayerName);
        Assert.False(labelLayer.Output);
        Assert.Equal(7, r.State.Shapes.Count(s => s.LayerId == labelLayer.Id));
        Assert.Equal(7, r.State.Shapes.Select(s => s.GroupId).Distinct().Count());
        // Layers with the same settings are shared: Cut, Engrave and the labels.
        Assert.Equal(3, r.State.Layers.Count);

        Assert.Contains(r.Notes, n => n.Contains("Liam Novak") && n.Contains("{Personalization}"));
        // It builds as a job: engraving before the cut, nothing left out.
        var job = JobBuilder.Build(r.State, new JobOptions());
        Assert.Empty(job.LeftOut);
        Assert.DoesNotContain(job.Warnings, w => w.Contains("after the cut"));
        Assert.Equal("Orders: Wood 3 mm", r.SheetOf["etsy:3300100100:4011001001"]);
    }

    [Fact]
    public void ManyPiecesGoOnSeveralSheetsAndAPieceIsTurnedToFit()
    {
        var one = SamplePieces("Emma Larsen")[0];
        var pieces = Enumerable.Range(1, 30).Select(n => one with { Number = n }).ToList();
        // 100 × 80 mm: keyrings are 60 × 25, so one a row flat; 3 rows a sheet.
        var r = SheetMaker.Make(DocState.New(), pieces, new SheetOptions { Area = new Rect2(0, 0, 100, 80), MarginMm = 2, GapMm = 2, Labels = false }, DateTime.Now);
        Assert.Equal(30, r.Pieces);
        Assert.True(r.Sheets >= 5, $"{r.Sheets} sheets");
        Assert.All(SheetMaker.Read(r.State), s => Assert.All(s.Pieces, p => Assert.True(new Rect2(2, 2, 98, 78).Contains(p.Box.Inflate(-0.01)))));

        // 30 mm wide: a 60 mm keyring only fits turned.
        var narrow = SheetMaker.Make(DocState.New(), [one], new SheetOptions { Area = new Rect2(0, 0, 30, 100), MarginMm = 1 }, DateTime.Now);
        var box = SheetMaker.Read(narrow.State)[0].Pieces[0].Box;
        Assert.True(box.Width < 26 && box.Height > 59, $"{box}");
        var none = SheetMaker.Make(DocState.New(), [one], new SheetOptions { Area = new Rect2(0, 0, 30, 100), MarginMm = 1, Turn = false }, DateTime.Now);
        Assert.Equal(0, none.Sheets);
        Assert.Contains(none.Notes, n => n.Contains("bigger than the sheet"));
    }

    [Fact]
    public void SheetsGoAfterThePageOnScreenWhenTheProjectHasADesign()
    {
        var s = DocState.New();
        s = Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(0, 0, 10, 10) }]);
        var r = SheetMaker.Make(s, SamplePieces("Emma Larsen"), new SheetOptions { Area = new Rect2(0, 0, 200, 200) }, DateTime.Now);
        Assert.Equal(["Page 1", "Orders"], Pages.Names(r.State));
        Assert.Equal(1, r.State.PageIndex);
        Assert.Single(r.State.OtherPages[0].Shapes); // the design is untouched
        // What's on screen is burned: its pieces are found for marking them done.
        Assert.Single(SheetMaker.OnScreen(r.State));
        Assert.Empty(SheetMaker.OnScreen(Pages.SwitchTo(r.State, 0)));
    }

    [Fact]
    public void AProofIsOnePieceWithTheWordsIn()
    {
        var (product, design) = Samples.Products()[1];
        var olivia = OrderCsv.Read(Samples.EtsyCsv).Items.Single(i => i.Buyer == "Olivia Chen");
        var proof = SheetMaker.Proof(design, olivia, DateTime.Now);
        Assert.Equal(["The Chens", "Est. 2019"], proof.Shapes.OfType<TextShape>().Select(t => t.Text));
        Assert.Equal(design.Shapes.Count, proof.Shapes.Count);
        Assert.True(PhotonCanvas.Doc.Export.DesignExport.Png(proof, 100).Length > 1000);
        Assert.Equal("Slate", product.SheetGroup);

        // The words stay centred where the placeholder was, however long they are.
        var (_, keyring) = Samples.Products()[0];
        var placeholder = keyring.Shapes.OfType<TextShape>().Single().Bounds.Center;
        foreach (var who in new[] { "Emma Larsen", "Noah Brooks" })
        {
            var item = OrderCsv.Read(Samples.EtsyCsv).Items.Single(i => i.Buyer == who);
            var c = SheetMaker.Proof(keyring, item, DateTime.Now).Shapes.OfType<TextShape>().Single().Bounds.Center;
            Assert.True(Math.Abs(c.X - placeholder.X) < 0.05 && Math.Abs(c.Y - placeholder.Y) < 0.05, $"{who}: {c} vs {placeholder}");
        }
    }

    [Fact]
    public void PackingListShowsEverySheetAndAnOrderSlip()
    {
        var pieces = SamplePieces();
        var r = SheetMaker.Make(DocState.New(), pieces, new SheetOptions { Area = new Rect2(0, 0, 220, 220) }, DateTime.Now);
        var sheets = SheetMaker.Read(r.State);
        var items = pieces.Select(p => p.Item).DistinctBy(i => i.Key).ToDictionary(i => i.Key);
        var png = PackingList.FirstPagePng(sheets, k => items.GetValueOrDefault(k), new Rect2(0, 0, 220, 220), false, DateTime.Now);
        Assert.True(png.Length > 5000);
        var dir = Path.Combine(Path.GetTempPath(), "pc-orders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "packing.pdf");
            PackingList.Save(path, sheets, k => items.GetValueOrDefault(k), new Rect2(0, 0, 220, 220), true, DateTime.Now);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            // Two sheets and at least one page of slips.
            Assert.True(Encoding.ASCII.GetString(bytes).Split("/Type /Page").Length - 1 >= 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SecretsComeBackAsTyped()
    {
        Assert.Equal("shpat_abc", Secret.Open(Secret.Lock("shpat_abc")));
        Assert.Equal("", Secret.Open(""));
        if (!OperatingSystem.IsWindows()) Assert.Equal("plain", Secret.Lock("plain"));
    }
}
