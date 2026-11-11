using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Export;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// What you sell: for each product, which ordered items it is (SKUs or words), what it's made on,
/// and its design: a PhotonCanvas design whose text uses order fields such as {Personalization}.
/// </summary>
public sealed class ProductsWindow : Window
{
    readonly IAddonHost _host;
    readonly ProductList _list;
    readonly string _path;
    readonly OrderItem? _sample;
    readonly ListBox _names = new() { MinHeight = 200 };
    readonly TextBox _name = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _matches = new() { AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "One per line: a SKU, or words in the item's name or options" };
    readonly TextBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "e.g. Walnut 3 mm (empty: its own sheets)" };
    readonly TextBlock _design = new() { TextWrapping = TextWrapping.Wrap };
    readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 200 };
    readonly TextBlock _fields = Hint();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly StackPanel _editor = new() { Spacing = 10 };
    bool _loading;

    Product? Current => _names.SelectedIndex >= 0 && _names.SelectedIndex < _list.Products.Count ? _list.Products[_names.SelectedIndex] : null;

    public ProductsWindow(IAddonHost host, ProductList list, string path, Product? add, OrderItem? sample)
    {
        _host = host;
        _list = list.Copy();
        _path = path;
        _sample = sample;
        Title = "Products";
        Width = 900;
        Height = 700;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        if (add is not null) _list.Products.Add(add);
        Content = Layout();
        Names();
        _names.SelectedIndex = add is not null ? _list.Products.Count - 1 : _list.Products.Count > 0 ? 0 : -1;
        Load();
    }

    Control Layout()
    {
        var add = Button("Add");
        add.Click += (_, _) => { _list.Products.Add(new Product { Name = $"Product {_list.Products.Count + 1}" }); Names(); _names.SelectedIndex = _list.Products.Count - 1; };
        var remove = Button("Remove");
        remove.Click += (_, _) =>
        {
            if (Current is not { } p) return;
            int at = _names.SelectedIndex;
            _list.Products.Remove(p);
            Names();
            _names.SelectedIndex = Math.Min(at, _list.Products.Count - 1);
        };
        var up = Button("Up", tip: "Products are tried in this order: the first that matches an item is used (a SKU match always wins).");
        up.Click += (_, _) => Move(-1);
        var down = Button("Down", tip: "Products are tried in this order: the first that matches an item is used (a SKU match always wins).");
        down.Click += (_, _) => Move(1);
        _names.SelectionChanged += (_, _) => Load();

        var left = new DockPanel
        {
            Margin = new Thickness(16, 16, 8, 16),
            Children =
            {
                Muted("PRODUCTS").With(t => DockPanel.SetDock(t, Dock.Top)),
                new WrapPanel { Children = { add, remove, up, down } }.With(w => { DockPanel.SetDock(w, Dock.Bottom); foreach (var c in w.Children) c.Margin = new Thickness(0, 6, 6, 0); }),
                _names.With(l => l.Margin = new Thickness(0, 6, 0, 0)),
            },
        };

        _name.TextChanged += (_, _) => { if (!_loading && Current is { } p) { p.Name = _name.Text ?? ""; RenameInList(); } };
        _matches.TextChanged += (_, _) => { if (!_loading && Current is { } p) p.Matches = _matches.Text ?? ""; };
        _material.TextChanged += (_, _) => { if (!_loading && Current is { } p) p.Material = _material.Text ?? ""; };
        ToolTip.SetTip(_name, "What you call it: shown beside each order line it matches.");
        ToolTip.SetTip(_matches, "Which ordered items are this product: their SKU (exactly), or words in their name or options, one per line. * matches anything not matched by another product.");
        ToolTip.SetTip(_material, "What it's made on. Products with the same material share sheets; different materials get sheets of their own.");

        var useSelection = Button("Use the selection", tip: "Save the shapes selected on the work area as this product's design (text with {Personalization} and other fields in it). The layers they're on, with their settings, go with them.");
        useSelection.Click += (_, _) => UseSelection();
        var choose = Button("Choose file…", tip: "Use a PhotonCanvas design file (.pcanvas) as this product's design.");
        choose.Click += async (_, _) => await ChooseFile();
        var put = Button("Put on the work area", tip: "Put a copy of the design on the page on screen, to change it. Then select it and Use the selection again.");
        put.Click += (_, _) => PutOnWorkArea();

        _editor.Children.Add(Field("Name", _name));
        _editor.Children.Add(Field("Matches", _matches));
        _editor.Children.Add(Field("Material", _material));
        _editor.Children.Add(Explained("Design", "What's burned for each piece. Type order fields into its text or codes: they're filled in for each order.",
            _design,
            new WrapPanel { Children = { useSelection, choose, put } }.With(w => { foreach (var c in w.Children) c.Margin = new Thickness(0, 0, 6, 4); }),
            new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), MinHeight = 60, Child = _preview },
            _fields));
        var fieldList = new StackPanel { Spacing = 2 };
        foreach (var (f, meaning) in OrderFields.Standard)
            fieldList.Children.Add(new Grid { ColumnDefinitions = new("170,*"), Children = { new SelectableTextBlock { Text = f, FontFamily = new FontFamily("Consolas, Menlo, monospace") }, new TextBlock { Text = meaning, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }.With(t => Grid.SetColumn(t, 1)) } });
        if (_sample is not null && _sample.Options.Count > 0)
            fieldList.Children.Add(Hint($"This shop's items also have: {ListWords(_sample.Options.Keys.Select(k => "{" + k + "}"))}.").With(t => t.TextWrapping = TextWrapping.Wrap));
        fieldList.Children.Add(Hint("The program's own {serial}, {date} and {time} work too.").With(t => t.TextWrapping = TextWrapping.Wrap));
        _editor.Children.Add(Explained("Order fields", "Type these into the design's text, e.g. a text reading {Personalization}, or a QR code holding {Order}.", fieldList));

        var save = new Button { Content = "Save", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            try { _list.Save(_path); Close(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save the products: {e.Message}"; }
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();

        var right = new DockPanel
        {
            Margin = new Thickness(8, 16, 16, 16),
            Children =
            {
                new StackPanel { Spacing = 8, Children = { _status, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } } } }.With(p => DockPanel.SetDock(p, Dock.Bottom)),
                new ScrollViewer { Content = _editor, Margin = new Thickness(0, 0, 0, 8) },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("240,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    void Names() => _names.ItemsSource = _list.Products.Select(p => p.Name.Length > 0 ? p.Name : "(no name)").ToList();

    void RenameInList()
    {
        int at = _names.SelectedIndex;
        _loading = true;
        Names();
        _names.SelectedIndex = at;
        _loading = false;
    }

    void Move(int by)
    {
        int at = _names.SelectedIndex, to = at + by;
        if (at < 0 || to < 0 || to >= _list.Products.Count) return;
        (_list.Products[at], _list.Products[to]) = (_list.Products[to], _list.Products[at]);
        Names();
        _names.SelectedIndex = to;
    }

    void Load()
    {
        if (_loading) return;
        _loading = true;
        var p = Current;
        _editor.IsEnabled = p is not null;
        _name.Text = p?.Name ?? "";
        _matches.Text = p?.Matches ?? "";
        _material.Text = p?.Material ?? "";
        _loading = false;
        ShowDesign();
    }

    void ShowDesign()
    {
        _preview.Source = null;
        if (Current is not { } p) { _design.Text = "Add a product."; _fields.Text = ""; return; }
        var path = OrdersWindow.DesignPath(_host, p);
        if (path.Length == 0) { _design.Text = "No design yet. Draw it on the work area (text such as {Personalization} where the customer's words go), select it, then Use the selection."; _fields.Text = ""; return; }
        var d = ProductDesigns.Load(path);
        if (d is null) { _design.Text = $"Can't read {path}."; _fields.Text = ""; return; }
        var box = ProductDesigns.Box(d);
        _design.Text = $"{Path.GetFileName(path)} · {box.Width:0.#} × {box.Height:0.#} mm";
        try
        {
            var shown = _sample is not null ? SheetMaker.Proof(d, _sample, DateTime.Now) : d;
            _preview.Source = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(DesignExport.Png(shown, 150)));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { _design.Text += $" (can't draw it: {e.Message})"; }
        var used = ProductDesigns.FieldsUsed(d);
        _fields.Text = (used.Count == 0
            ? "Its text uses no fields: every piece comes out the same."
            : $"Fields it uses: {ListWords(used.Select(u => "{" + u + "}"))}.") +
            (_sample is not null ? $" Shown with order {_sample.Order}." : "");
    }

    void UseSelection()
    {
        if (Current is not { } p) return;
        var doc = _host.Document;
        var ids = Ops.WithGroups(doc.State, doc.Selection);
        if (ids.Count == 0) { _status.Text = "Select the design on the work area first (behind this window), then click Use the selection."; return; }
        var design = ProductDesigns.FromSelection(doc.State, ids);
        var folder = Path.Combine(_host.DataFolder, "designs");
        string file = string.Concat((p.Name.Trim().Length > 0 ? p.Name.Trim() : "Product").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + ".pcanvas";
        try
        {
            Directory.CreateDirectory(folder);
            DocFile.Save(design, Path.Combine(folder, file));
            p.Design = file;
            _status.Text = $"Saved {design.Shapes.Count} shape{(design.Shapes.Count == 1 ? "" : "s")} as {p.Name}'s design.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save the design: {e.Message}"; }
        ShowDesign();
    }

    async Task ChooseFile()
    {
        if (Current is not { } p) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Product design",
            FileTypeFilter = [new FilePickerFileType("PhotonCanvas design") { Patterns = ["*.pcanvas"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (ProductDesigns.Load(path) is null) { _status.Text = "That file can't be read as a design."; return; }
        p.Design = path;
        ShowDesign();
    }

    void PutOnWorkArea()
    {
        if (Current is not { } p || ProductDesigns.Load(OrdersWindow.DesignPath(_host, p)) is not { } d) { _status.Text = "This product has no design yet."; return; }
        var doc = _host.Document;
        var s = doc.State;
        var map = new Dictionary<Guid, Guid>();
        foreach (var l in d.Layers)
        {
            // A layer already on the page with the same name and settings is used; otherwise the design's is added.
            var same = s.Layers.FirstOrDefault(x => (x with { Id = Guid.Empty, ParentId = null }) == (l with { Id = Guid.Empty, ParentId = null }));
            if (same is not null) { map[l.Id] = same.Id; continue; }
            var copy = l with { Id = Guid.NewGuid(), ParentId = l.ParentId is { } pp && map.TryGetValue(pp, out var np) ? np : null };
            s = Ops.AddLayer(s, copy);
            map[l.Id] = copy.Id;
        }
        var shapes = Ops.CopyAsUnit(d.Shapes, Affine.Translate(10, 10))
            .Where(x => map.ContainsKey(x.LayerId))
            .Select(x => x.With(layerId: map[x.LayerId])).ToImmutableList();
        doc.Commit($"{p.Name} design", VariableText.Refresh(Ops.AddShapes(s, shapes), DateTime.Now), shapes.Select(x => x.Id));
        _status.Text = "A copy is on the work area, selected. Change it, then Use the selection to save it back.";
    }
}
