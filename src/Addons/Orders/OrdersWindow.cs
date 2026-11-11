using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Export;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// The orders brought in, and what to do with them: bring more in (a CSV export, or straight from
/// Etsy or Shopify), tick the ones to make, make the sheets, save a proof or the packing list.
/// </summary>
public sealed class OrdersWindow : Window
{
    readonly IAddonHost _host;
    OrderBook _book;
    ProductList _products;
    ShopSettings _shops;
    readonly HashSet<string> _ticked = [];
    string? _selected;
    readonly Dictionary<string, DocState?> _designs = [];

    readonly ComboBox _show = new() { ItemsSource = new[] { "To burn", "On a sheet", "Burned", "Skipped", "All" }, SelectedIndex = 0, MinWidth = 130 };
    readonly StackPanel _rows = new() { Spacing = 4 };
    readonly TextBlock _count = Hint();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };

    // The selected line.
    readonly Image _proof = new() { Stretch = Stretch.Uniform, MaxHeight = 220 };
    readonly TextBlock _proofSays = Hint();
    readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _product = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button _saveProof = Button("Save proof…", tip: "A picture of this piece with the customer's text, to send them for approval before you burn it.");
    readonly Button _newProduct = Button("New product from this…", tip: "Make a product for items like this one (its SKU or name), then give it a design.");
    readonly StackPanel _detailPanel = new() { Spacing = 8 };
    bool _loading;

    // The sheets.
    readonly ComboBox _sheet = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _gap = Number(0, 100, 0.5m, "0.##");
    readonly NumericUpDown _margin = Number(0, 100, 0.5m, "0.##");
    readonly CheckBox _turn = new() { Content = "Turn pieces to fit", IsChecked = true };
    readonly CheckBox _labels = new() { Content = "Number each piece", IsChecked = true };
    readonly Button _make = new() { Content = "Make sheets", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    List<MaterialChoice> _sheets = [];

    public static string BookPath(IAddonHost host) => Path.Combine(host.DataFolder, "orders.json");
    string ProductsPath => Path.Combine(_host.DataFolder, "products.json");
    string ShopsPath => Path.Combine(_host.DataFolder, "shops.json");

    public OrdersWindow(IAddonHost host)
    {
        _host = host;
        _book = OrderBook.Load(BookPath(host));
        _products = ProductList.Load(ProductsPath);
        _shops = ShopSettings.Load(ShopsPath);
        Title = "Orders to sheet";
        Width = 1080;
        Height = 800;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _gap.Value = 3;
        _margin.Value = 5;
        Content = Layout();
        TickDefaults();
        FillSheets();
        ShowList();
    }

    // ---- building the window ----

    Control Layout()
    {
        var import = Button("Import CSV…", tip: "Etsy's \"Order Items\" CSV (Shop Manager → Settings → Options → Download Data), Shopify's orders export, or any spreadsheet with a row per item.");
        import.Click += async (_, _) => await ImportCsv();
        var etsy = Button("Get Etsy orders", tip: "Read your Etsy shop's paid orders that haven't shipped (connect the shop first in Shops…).");
        etsy.Click += async (_, _) => await FetchEtsy(etsy);
        var shopify = Button("Get Shopify orders", tip: "Read your Shopify shop's open orders not yet fulfilled (set the shop up first in Shops…).");
        shopify.Click += async (_, _) => await FetchShopify(shopify);
        var shops = Button("Shops…", tip: "Connect your Etsy and Shopify shops, and the paper for the packing list.");
        shops.Click += async (_, _) => { await new ShopsWindow(_host, ShopsPath).ShowDialog(this); _shops = ShopSettings.Load(ShopsPath); };
        var products = Button("Products…", tip: "What you sell: which ordered items each product is, and the design burned for it with {Personalization} and other order fields in its text.");
        products.Click += async (_, _) => await EditProducts(null);
        var samples = Button("Sample orders", tip: "Bring in made-up Etsy and Shopify orders, with two sample products (a name keyring and a slate coaster), to try it all without a shop.");
        samples.Click += (_, _) => SampleOrders();
        _show.SelectionChanged += (_, _) => ShowList();
        ToolTip.SetTip(_show, "Which order lines to list.");

        var top = new DockPanel
        {
            Margin = new Thickness(16, 14, 16, 8),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Muted("Show"), _show } }.With(p => { p.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(p, Dock.Right); }),
                new WrapPanel { Children = { import, etsy, shopify, shops, products, samples } }.With(w => { foreach (var c in w.Children) c.Margin = new Thickness(0, 0, 6, 4); }),
            },
        };

        var all = Button("Tick all");
        all.Click += (_, _) => { foreach (var i in Listed()) _ticked.Add(i.Key); ShowList(); };
        var none = Button("Tick none");
        none.Click += (_, _) => { _ticked.Clear(); ShowList(); };
        var burned = Button("Mark burned", tip: "The ticked lines are made (when their sheet finishes on the machine this happens by itself).");
        burned.Click += (_, _) => SetStatus(ItemStatus.Burned);
        var again = Button("Mark to burn", tip: "Put the ticked lines back to be made: a reprint, or a sheet that went wrong.");
        again.Click += (_, _) => SetStatus(ItemStatus.ToBurn);
        var skip = Button("Skip", tip: "Not to be made here (made another way, a gift card, cancelled).");
        skip.Click += (_, _) => SetStatus(ItemStatus.Skipped);
        var remove = Button("Remove", tip: "Take the ticked lines out of the list. Bringing the orders in again brings them back.");
        remove.Click += async (_, _) => await Remove();
        var listButtons = new WrapPanel { Children = { all, none, burned, again, skip, remove } }.With(w => { foreach (var c in w.Children) c.Margin = new Thickness(0, 6, 6, 0); });

        var list = new DockPanel
        {
            Margin = new Thickness(16, 0, 8, 12),
            Children =
            {
                _count.With(t => DockPanel.SetDock(t, Dock.Top)),
                new StackPanel { Spacing = 6, Children = { listButtons, _status } }.With(p => DockPanel.SetDock(p, Dock.Bottom)),
                new Border
                {
                    BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 6, 0, 0),
                    Child = new ScrollViewer { Content = _rows, Padding = new Thickness(6), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                },
            },
        };

        _product.SelectionChanged += (_, _) => PickProduct();
        ToolTip.SetTip(_product, "The product this line is made as. Automatic: the first product whose SKU or words match it.");
        _saveProof.Click += async (_, _) => await SaveProof();
        _newProduct.Click += async (_, _) => await NewProductFromSelected();
        _detailPanel.Children.Add(Muted("THE SELECTED LINE"));
        _detailPanel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), MinHeight = 80, Child = _proof });
        _detailPanel.Children.Add(_proofSays);
        _detailPanel.Children.Add(Field("Product", _product));
        _detailPanel.Children.Add(new WrapPanel { Children = { _saveProof, _newProduct } }.With(w => { foreach (var c in w.Children) c.Margin = new Thickness(0, 0, 6, 4); }));
        _detailPanel.Children.Add(_details);

        ToolTip.SetTip(_sheet, "Where the pieces go: the whole bed, or the material outlined on the page (outline the board, then right-click it: Use as material outline).");
        ToolTip.SetTip(_turn, "A piece is turned a quarter when it only fits that way, or when that fits it into the row.");
        ToolTip.SetTip(_labels, "A small number and the order on each piece, on the \"Order labels\" layer, which isn't burned. The packing list uses the same numbers.");
        ToolTip.SetTip(_make, "Put a copy of each ticked line's design on sheets (one copy per piece of its quantity), with the order's text written in. Each material gets its own sheets, as new pages.");
        _make.Click += (_, _) => MakeSheets();
        var packing = new Button { Content = "Packing list…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(packing, "A PDF of the order sheets in this project: a map of each sheet with the pieces numbered, whose each one is, and a slip for each order to pack it by.");
        packing.Click += async (_, _) => await SavePackingList();
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        var sheetCard = Explained("Sheets", "The ticked lines, laid out in rows, oldest order first.",
            Field("Sheet", _sheet),
            Row(Labelled("Gap between", _gap.With(g => ToolTip.SetTip(g, "Space between pieces, mm. At least the cut width."))),
                Labelled("Edge margin", _margin.With(m => ToolTip.SetTip(m, "Space kept from the sheet's edge, mm.")))),
            new WrapPanel { Children = { _turn.With(t => t.Margin = new Thickness(0, 0, 14, 0)), _labels } },
            _make, packing);

        var right = new Border
        {
            Background = Brush("Bg2"),
            BorderBrush = Brush("Line"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    new StackPanel { Spacing = 8, Margin = new Thickness(14, 8, 14, 14), Children = { sheetCard, close } }.With(p => DockPanel.SetDock(p, Dock.Bottom)),
                    new ScrollViewer { Content = _detailPanel.With(p => p.Margin = new Thickness(14, 14, 14, 0)) },
                },
            },
        };

        var grid = new Grid { ColumnDefinitions = new("*,360"), RowDefinitions = new("Auto,*") };
        grid.Children.Add(top);
        Grid.SetColumnSpan(top, 2);
        grid.Children.Add(list);
        Grid.SetRow(list, 1);
        grid.Children.Add(right);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        return grid;
    }

    void FillSheets()
    {
        (_sheets, int first) = MaterialChoice.For(_host.Document.State, _host.Machine.FieldW, _host.Machine.FieldH);
        _sheet.ItemsSource = _sheets.Select(c => c.Label).ToList();
        _sheet.SelectedIndex = first;
    }

    // ---- the list ----

    IEnumerable<OrderItem> Listed() => _book.Items
        .Where(i => _show.SelectedIndex switch
        {
            0 => i.Status == ItemStatus.ToBurn,
            1 => i.Status == ItemStatus.OnSheet,
            2 => i.Status == ItemStatus.Burned,
            3 => i.Status == ItemStatus.Skipped,
            _ => true,
        })
        .OrderBy(i => i.Date ?? DateTime.MaxValue).ThenBy(i => i.Order, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Key, StringComparer.Ordinal);

    /// <summary>Lines to burn that have a product are ticked to start with.</summary>
    void TickDefaults()
    {
        foreach (var i in _book.Items.Where(i => i.Status == ItemStatus.ToBurn && _products.For(i) is not null)) _ticked.Add(i.Key);
        ShowList();
    }

    /// <summary>The order list was changed elsewhere (a sheet finished): read it again.</summary>
    public void Reload()
    {
        _book = OrderBook.Load(BookPath(_host));
        ShowList();
    }

    void ShowList()
    {
        _rows.Children.Clear();
        var listed = Listed().ToList();
        _ticked.IntersectWith(_book.Items.Select(i => i.Key));
        foreach (var i in listed) _rows.Children.Add(LineRow(i));
        if (listed.Count == 0)
            _rows.Children.Add(Hint(_book.Items.Count == 0
                ? "No orders yet. Import your shop's CSV export, or get them straight from Etsy or Shopify (set the shop up in Shops… first)."
                : "Nothing to show here. Pick another list under Show.").With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(8); }));
        int pieces = PiecesTicked().Sum(p => p.Item.Quantity);
        int lines = _ticked.Count;
        _count.Text = $"{listed.Count} line{(listed.Count == 1 ? "" : "s")} listed · {lines} ticked" + (lines > 0 ? $" ({pieces} piece{(pieces == 1 ? "" : "s")} with a product)" : "");
        _make.Content = pieces > 0 ? $"Make sheets ({pieces} piece{(pieces == 1 ? "" : "s")})" : "Make sheets";
        _make.IsEnabled = pieces > 0;
        if (_selected is null || _book.Find(_selected) is null) _selected = listed.FirstOrDefault()?.Key;
        ShowSelected();
    }

    /// <summary>The ticked lines that have a product, with it.</summary>
    IEnumerable<(OrderItem Item, Product Product)> PiecesTicked() =>
        _book.Items.Where(i => _ticked.Contains(i.Key)).Select(i => (Item: i, Product: _products.For(i)!)).Where(x => x.Product is not null);

    Control LineRow(OrderItem i)
    {
        var tick = new CheckBox { IsChecked = _ticked.Contains(i.Key), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 4, 0) };
        ToolTip.SetTip(tick, "Tick to put it on the next sheets, or to mark it.");
        tick.IsCheckedChanged += (_, _) => { if (tick.IsChecked == true) _ticked.Add(i.Key); else _ticked.Remove(i.Key); UpdateCount(); };
        var product = _products.For(i);
        var who = i.Buyer.Length > 0 ? i.Buyer : i.ShipName;
        var lines = new StackPanel { Spacing = 1 };
        lines.Children.Add(new TextBlock
        {
            Text = $"{i.Order} · {who}{(i.Date is { } d ? " · " + d.ToString("d MMM", CultureInfo.InvariantCulture) : "")}",
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        lines.Children.Add(new TextBlock { Text = $"{i.Quantity} × {i.Title}" + (i.OptionsText.Length > 0 ? $" · {i.OptionsText}" : ""), TextTrimming = TextTrimming.CharacterEllipsis });
        if (i.Personalization.Length > 0)
            lines.Children.Add(new TextBlock { Text = "“" + i.Personalization.Replace("\r", "").Replace("\n", " / ") + "”", Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis });
        var badge = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new TextBlock { Text = product?.Name ?? "No product", Foreground = product is null ? Brush("Warn") : Brush("TextMain"), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 },
                new TextBlock { Text = OrderItem.StatusText(i.Status) + (i.Sheet.Length > 0 && i.Status != ItemStatus.ToBurn ? $" · {i.Sheet}" : ""), Classes = { "muted" }, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(tick);
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        var row = new Border
        {
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(6),
            Background = i.Key == _selected ? Brush("Bg3") : Brushes.Transparent,
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = i.Key,
        };
        row.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<CheckBox>(true) is not null) return;
            _selected = i.Key;
            foreach (var r in _rows.Children.OfType<Border>()) r.Background = Equals(r.Tag, _selected) ? Brush("Bg3") : Brushes.Transparent;
            ShowSelected();
        };
        return row;
    }

    void UpdateCount()
    {
        int pieces = PiecesTicked().Sum(p => p.Item.Quantity);
        int listed = _rows.Children.OfType<Border>().Count();
        _count.Text = $"{listed} line{(listed == 1 ? "" : "s")} listed · {_ticked.Count} ticked" + (_ticked.Count > 0 ? $" ({pieces} piece{(pieces == 1 ? "" : "s")} with a product)" : "");
        _make.Content = pieces > 0 ? $"Make sheets ({pieces} piece{(pieces == 1 ? "" : "s")})" : "Make sheets";
        _make.IsEnabled = pieces > 0;
    }

    // ---- the selected line ----

    DocState? Design(Product p)
    {
        var path = DesignPath(_host, p);
        string key = path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0);
        if (!_designs.TryGetValue(key, out var d)) _designs[key] = d = ProductDesigns.Load(path);
        return d;
    }

    /// <summary>A product's design file: as given, or in the add-on's designs folder when it's only a name.</summary>
    public static string DesignPath(IAddonHost host, Product p) =>
        p.Design.Length == 0 ? "" : Path.IsPathRooted(p.Design) ? p.Design : Path.Combine(host.DataFolder, "designs", p.Design);

    void ShowSelected()
    {
        var item = _selected is null ? null : _book.Find(_selected);
        _detailPanel.IsVisible = item is not null;
        if (item is null) return;
        _loading = true;
        var auto = _products.For(item with { Product = "" });
        _product.ItemsSource = new[] { auto is null ? "Automatic: none matches" : $"Automatic: {auto.Name}" }.Concat(_products.Products.Select(p => p.Name)).ToList();
        _product.SelectedIndex = item.Product.Length == 0 ? 0 : Math.Max(0, _products.Products.FindIndex(p => p.Name == item.Product) + 1);
        _loading = false;

        var lines = new List<string>();
        if (item.Personalization.Length > 0) lines.Add("Personalization: " + item.Personalization);
        foreach (var o in item.Options) lines.Add($"{o.Key}: {o.Value}");
        if (item.Note.Length > 0) lines.Add("Note: " + item.Note);
        var shipTo = (item.ShipName.Length > 0 ? item.ShipName : item.Buyer) + (item.ShipAddress.Length > 0 ? "\n" + item.ShipAddress : "");
        if (shipTo.Trim().Length > 0) lines.Add("Ship to: " + shipTo);
        lines.Add($"{item.Source} · {item.Quantity} × {item.Title}{(item.Sku.Length > 0 ? $" · SKU {item.Sku}" : "")}");
        _details.Text = string.Join("\n", lines);

        var product = _products.For(item);
        _newProduct.IsVisible = product is null;
        _saveProof.IsEnabled = false;
        _proof.Source = null;
        if (product is null) { _proofSays.Text = "No product matches this line. Make one (New product from this…), or add its SKU or name to a product in Products…."; return; }
        var design = Design(product);
        if (design is null) { _proofSays.Text = $"{product.Name} has no design yet (Products…)."; return; }
        try
        {
            var proof = SheetMaker.Proof(design, item, DateTime.Now);
            var png = DesignExport.Png(proof, 150);
            _proof.Source = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
            _saveProof.IsEnabled = true;
            var empty = proof.Shapes.Count == 0 ? [] : design.Shapes.OfType<TextShape>().SelectMany(t => OrderFields.Empty(t.Text, OrderFields.For(item))).Distinct().ToList();
            _proofSays.Text = empty.Count > 0 ? $"As it will burn. Empty for this order: {ListWords(empty.Select(e => "{" + e + "}"))}." : "As it will burn.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { _proofSays.Text = $"Couldn't draw it: {e.Message}"; }
    }

    void PickProduct()
    {
        if (_loading || _selected is null || _book.Find(_selected) is not { } item) return;
        string name = _product.SelectedIndex <= 0 ? "" : _products.Products[_product.SelectedIndex - 1].Name;
        if (name == item.Product) return;
        _book.Update(item.Key, i => i with { Product = name });
        SaveBook();
        ShowList();
    }

    async Task SaveProof()
    {
        if (_selected is null || _book.Find(_selected) is not { } item || _products.For(item) is not { } product || Design(product) is not { } design) return;
        var safe = string.Concat($"Proof {item.Order} {item.ShipName}".Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the proof",
            DefaultExtension = "png",
            SuggestedFileName = safe + ".png",
            FileTypeChoices = [new FilePickerFileType("PNG picture") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            var png = DesignExport.Png(SheetMaker.Proof(design, item, DateTime.Now), 300);
            await File.WriteAllBytesAsync(path, png);
            _status.Text = $"Proof saved: {Path.GetFileName(path)}. Send it to the customer to check the spelling before you burn.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
    }

    async Task NewProductFromSelected()
    {
        if (_selected is null || _book.Find(_selected) is not { } item) return;
        var p = new Product { Name = item.Title.Length > 40 ? item.Title[..40].Trim() : item.Title, Matches = item.Sku.Length > 0 ? item.Sku : item.Title };
        await EditProducts(p);
    }

    async Task EditProducts(Product? add)
    {
        var w = new ProductsWindow(_host, _products, ProductsPath, add, _book.Items.FirstOrDefault(i => i.Key == _selected));
        await w.ShowDialog(this);
        _products = ProductList.Load(ProductsPath);
        _designs.Clear();
        // Lines that now have a product are ticked, as when the window opened.
        foreach (var i in _book.Items.Where(i => i.Status == ItemStatus.ToBurn && _products.For(i) is not null)) _ticked.Add(i.Key);
        ShowList();
    }

    // ---- bringing orders in ----

    async Task ImportCsv()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Orders (CSV)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("CSV spreadsheet") { Patterns = ["*.csv", "*.txt", "*.tsv"] }],
        });
        var said = new List<string>();
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try
            {
                if (new FileInfo(path).Length > 20_000_000) { said.Add($"{Path.GetFileName(path)}: too big (over 20 MB)."); continue; }
                var result = OrderCsv.Read(await File.ReadAllTextAsync(path), _shops.IncludeShipped);
                if (result.Problem is not null) { said.Add($"{Path.GetFileName(path)}: {result.Problem}"); continue; }
                said.Add($"{Path.GetFileName(path)}: {Bring(result.Items, result.Summary)}");
            }
            catch (IOException e) { said.Add($"{Path.GetFileName(path)}: couldn't read it ({e.Message})."); }
        }
        if (said.Count > 0) _status.Text = string.Join("\n", said);
    }

    /// <summary>Adds the lines to the list, ticks the new ones that have a product, and says what happened.</summary>
    string Bring(List<OrderItem> items, string what)
    {
        var before = _book.Items.Select(i => i.Key).ToHashSet();
        var (added, updated) = _book.Merge(items);
        foreach (var i in items.Where(i => !before.Contains(i.Key)).Select(i => _book.Find(i.Key)!).Where(i => i.Status == ItemStatus.ToBurn && _products.For(i) is not null))
            _ticked.Add(i.Key);
        SaveBook();
        ShowList();
        int noProduct = items.Select(i => _book.Find(i.Key)!).Count(i => i.Status == ItemStatus.ToBurn && _products.For(i) is null);
        return $"{what} {added} new, {updated} changed, {items.Count - added - updated} already here." +
            (noProduct > 0 ? $" {noProduct} match no product yet: pick one and use New product from this…." : "");
    }

    /// <summary>The sample orders, and the sample products when there's no product of that name yet.</summary>
    void SampleOrders()
    {
        var added = new List<string>();
        foreach (var (product, design) in Samples.Products())
        {
            if (_products.Products.Any(p => p.Name == product.Name)) continue;
            try
            {
                var folder = Path.Combine(_host.DataFolder, "designs");
                Directory.CreateDirectory(folder);
                DocFile.Save(design, Path.Combine(folder, product.Design));
                _products.Products.Add(product);
                added.Add(product.Name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save the sample design: {e.Message}"; return; }
        }
        if (added.Count > 0)
        {
            try { _products.Save(ProductsPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save the products: {e.Message}"; return; }
            _designs.Clear();
        }
        var etsy = OrderCsv.Read(Samples.EtsyCsv);
        var shopify = OrderCsv.Read(Samples.ShopifyCsv);
        var said = Bring([.. etsy.Items, .. shopify.Items], $"Sample orders ({etsy.Items.Count} Etsy and {shopify.Items.Count} Shopify lines, made up):");
        _status.Text = said + (added.Count > 0 ? $" Sample products added: {ListWords(added)} (see Products…)." : "") + " Remove them when you're done (tick them, Remove).";
    }

    async Task FetchEtsy(Button b)
    {
        _shops = ShopSettings.Load(ShopsPath);
        if (!_shops.EtsyConnected) { _status.Text = "Connect your Etsy shop first: Shops… → Connect to Etsy."; return; }
        b.IsEnabled = false;
        _status.Text = "Reading your Etsy orders…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var items = await EtsyApi.Fetch(http, _shops, _shops.IncludeShipped, CancellationToken.None);
            _shops.Save(ShopsPath); // a refreshed token
            _status.Text = Bring(items, $"Etsy{(_shops.EtsyShopName.Length > 0 ? " (" + _shops.EtsyShopName + ")" : "")}: {items.Count} line{(items.Count == 1 ? "" : "s")} to make.");
        }
        catch (Exception e) when (e is ShopException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException)
        {
            _status.Text = e is ShopException ? e.Message : $"Couldn't reach Etsy: {e.Message}";
        }
        finally { b.IsEnabled = true; }
    }

    async Task FetchShopify(Button b)
    {
        _shops = ShopSettings.Load(ShopsPath);
        if (!_shops.ShopifyReady) { _status.Text = "Set your Shopify shop up first: Shops…."; return; }
        b.IsEnabled = false;
        _status.Text = "Reading your Shopify orders…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var (items, noCustomers) = await ShopifyApi.Fetch(http, _shops, _shops.IncludeShipped, CancellationToken.None);
            _status.Text = Bring(items, $"Shopify: {items.Count} line{(items.Count == 1 ? "" : "s")} to make.") +
                (noCustomers ? " Names and addresses were left out: the app isn't allowed to see customers' details (give it the read_customers permission and customer data access)." : "");
        }
        catch (Exception e) when (e is ShopException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException)
        {
            _status.Text = e is ShopException ? e.Message : $"Couldn't reach Shopify: {e.Message}";
        }
        finally { b.IsEnabled = true; }
    }

    // ---- what's done with the lines ----

    void SetStatus(ItemStatus status)
    {
        if (_ticked.Count == 0) { _status.Text = "Tick the lines first."; return; }
        foreach (var key in _ticked) _book.Update(key, i => i with { Status = status, Burned = status == ItemStatus.Burned ? i.Burned ?? DateTime.Now : null });
        _status.Text = $"{_ticked.Count} line{(_ticked.Count == 1 ? "" : "s")}: {OrderItem.StatusText(status).ToLowerInvariant()}.";
        _ticked.Clear();
        SaveBook();
        ShowList();
    }

    async Task Remove()
    {
        if (_ticked.Count == 0) { _status.Text = "Tick the lines to remove first."; return; }
        if (!await Dialogs.Confirm(this, "Remove lines", $"Take {_ticked.Count} line{(_ticked.Count == 1 ? "" : "s")} out of the list?", "Remove")) return;
        _book.Items.RemoveAll(i => _ticked.Contains(i.Key));
        _ticked.Clear();
        SaveBook();
        ShowList();
    }

    void SaveBook()
    {
        try { _book.Save(BookPath(_host)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save the order list: {e.Message}"; }
    }

    void MakeSheets()
    {
        var pieces = new List<Piece>();
        var noDesign = new SortedSet<string>();
        foreach (var (item, product) in PiecesTicked())
        {
            if (Design(product) is not { } design) { noDesign.Add(product.Name); continue; }
            for (int n = 1; n <= item.Quantity; n++) pieces.Add(new Piece(item, n, product, design));
        }
        if (pieces.Count == 0)
        {
            _status.Text = noDesign.Count > 0 ? $"{ListWords(noDesign)} {(noDesign.Count == 1 ? "has" : "have")} no design yet: give {(noDesign.Count == 1 ? "it" : "them")} one in Products…." : "Tick lines that have a product first.";
            return;
        }
        var choice = _sheets[Math.Max(0, _sheet.SelectedIndex)];
        var area = choice.Outline?.Bounds() is { IsEmpty: false } b ? b : new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH);
        var o = new SheetOptions
        {
            Area = area,
            GapMm = (double)(_gap.Value ?? 3),
            MarginMm = (double)(_margin.Value ?? 5),
            Turn = _turn.IsChecked == true,
            Labels = _labels.IsChecked == true,
            Title = "Orders " + DateTime.Now.ToString("d MMM", CultureInfo.InvariantCulture),
        };
        var result = SheetMaker.Make(_host.Document.State, pieces, o, DateTime.Now);
        if (result.Sheets == 0) { _status.Text = string.Join("\n", result.Notes.DefaultIfEmpty("Nothing could be placed.")); return; }
        _host.Document.Commit("Orders to sheets", result.State);
        if (result.State.Layers.FirstOrDefault(l => l.Output) is { } first) _host.ActivateLayer(first.Id);
        _host.FitWorkArea();
        foreach (var (key, sheet) in result.SheetOf) _book.Update(key, i => i with { Status = ItemStatus.OnSheet, Sheet = sheet });
        _ticked.ExceptWith(result.SheetOf.Keys);
        SaveBook();
        ShowList();
        var lines = new List<string>
        {
            $"{result.Pieces} piece{(result.Pieces == 1 ? "" : "s")} on {result.Sheets} sheet{(result.Sheets == 1 ? "" : "s")} (new page{(result.Sheets == 1 ? "" : "s")}: switch with the page tabs). Check each sheet's layers, then Ship It. Ctrl+Z undoes it.",
        };
        if (noDesign.Count > 0) lines.Add($"Left out, no design yet: {ListWords(noDesign)}.");
        lines.AddRange(result.Notes.Take(8));
        if (result.Notes.Count > 8) lines.Add($"…and {result.Notes.Count - 8} more.");
        _status.Text = string.Join("\n", lines);
    }

    async Task SavePackingList()
    {
        var sheets = SheetMaker.Read(_host.Document.State);
        if (sheets.Count == 0) { _status.Text = "No order sheets in this project yet: make them first."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the packing list",
            DefaultExtension = "pdf",
            SuggestedFileName = $"Packing list {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            var bed = new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH);
            await Task.Run(() => PackingList.Save(path, sheets, _book.Find, bed, _shops.PaperA4, DateTime.Now));
            _status.Text = $"Packing list saved: {Path.GetFileName(path)} ({sheets.Count} sheet{(sheets.Count == 1 ? "" : "s")}, {sheets.Sum(s => s.Pieces.Count)} pieces).";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't save it (is it open in a PDF viewer?): {e.Message}";
        }
    }
}
