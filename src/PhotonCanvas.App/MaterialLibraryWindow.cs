using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The material and cutting library: browse and edit materials and their recipes, apply a
/// recipe to the selected layers, and share with others (.pcmat export/import).
/// </summary>
public sealed class MaterialLibraryWindow : Window
{
    readonly MaterialLibrary _lib;
    readonly AppSettings _settings;
    readonly Action<Material, Recipe>? _apply;

    readonly TextBox _search = new() { Watermark = "Search materials" };
    readonly ComboBox _filter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    readonly TextBox _name = new();
    readonly ComboBox _category = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Material.Categories };
    readonly NumericUpDown _thickness = Number(0, 100, 0.5m, "0.##");
    readonly CheckBox _reflective = new() { Content = "Highly reflective" };
    readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
    readonly StackPanel _recipes = new() { Spacing = 6 };
    readonly Control _details;
    readonly TextBlock _empty = Hint("Pick a material on the left, or make a new one. Recipes come from Save to library on a layer, or from a material test (Laser → Record results).");
    readonly TextBlock _status = Hint();
    List<Material> _shown = [];
    Material? _current;
    bool _loading;

    /// <summary>Something in the library changed (so lists elsewhere can refresh).</summary>
    public event Action? LibraryChanged;

    public MaterialLibraryWindow(MaterialLibrary lib, AppSettings settings, Action<Material, Recipe>? applyToLayers)
    {
        _lib = lib;
        _settings = settings;
        _apply = applyToLayers;
        Title = "Material library";
        Width = 1000;
        Height = 720;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _filter.ItemsSource = Material.Categories.Prepend("All categories").ToList();
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Fill();
        _filter.SelectionChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => Show(_list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex] : null);
        _name.LostFocus += (_, _) => SaveMaterialFields();
        _notes.LostFocus += (_, _) => SaveMaterialFields();
        _category.SelectionChanged += (_, _) => SaveMaterialFields();
        _thickness.ValueChanged += (_, _) => SaveMaterialFields();
        _reflective.IsCheckedChanged += (_, _) => SaveMaterialFields();
        ToolTip.SetTip(_reflective, "Bare copper, brass, aluminium, silver, gold, chrome or mirror finishes. Layers using this material keep the MOPA out of the frequencies that bounce the most back into the fiber, and the job reminds you how to cut reflections.");

        var add = Button("New material");
        add.Click += (_, _) => NewMaterial();
        var del = Button("Delete");
        del.Click += async (_, _) => await DeleteMaterials();
        var import = Button("Import…", tip: "Add materials and recipes from a .pcmat file someone shared.");
        import.Click += async (_, _) => await Import();
        var export = Button("Export…", tip: "Save the selected materials (or all, if none is selected) to a .pcmat file to share.");
        export.Click += async (_, _) => await Export();
        var author = new TextBox { Text = settings.LibraryAuthor ?? "", Watermark = "Your name (shown to people you share with)" };
        author.LostFocus += (_, _) => { _settings.LibraryAuthor = string.IsNullOrWhiteSpace(author.Text) ? null : author.Text.Trim(); _settings.Save(); };

        var left = new DockPanel { Margin = new(0, 0, 14, 0) };
        var top = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 8), Children = { _search, _filter } };
        var bottom = new StackPanel { Spacing = 6, Margin = new(0, 8, 0, 0), Children = { Row(add, del), Row(import, export), author, _status } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        left.Children.Add(top);
        left.Children.Add(bottom);
        left.Children.Add(_list);

        _details = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Card("Material", Field("Name", _name), Field("Category", _category), Field("Thickness mm", _thickness, tip: "0 = doesn't apply (coatings, surfaces)."), _reflective, _notes),
                new TextBlock { Text = "RECIPES", Classes = { "h" } },
                _recipes,
            },
        };
        var right = new ScrollViewer { Content = new Panel { Children = { _empty, _details } } };
        var grid = new Grid { ColumnDefinitions = new("300,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = new Border { Padding = new Thickness(16), Child = grid };
        Fill();
        Show(null);
    }

    void Changed()
    {
        _lib.Save();
        LibraryChanged?.Invoke();
    }

    void Fill(Guid? select = null)
    {
        var q = _search.Text?.Trim() ?? "";
        string? cat = _filter.SelectedIndex > 0 ? _filter.SelectedItem as string : null;
        _shown = _lib.Materials
            .Where(m => cat is null || m.Category == cat)
            .Where(m => q.Length == 0 || m.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase) || m.Notes.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || m.Recipes.Any(r => r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        _list.ItemsSource = _shown.Select(m => new StackPanel
        {
            Children =
            {
                new TextBlock { Text = m.Label, FontWeight = FontWeight.SemiBold },
                new TextBlock { Classes = { "hint" }, Text = $"{m.Category} · {m.Recipes.Count} recipe{(m.Recipes.Count == 1 ? "" : "s")} · {m.Recipes.Count(r => r.Tested)} tested" },
            },
        }).ToList();
        var id = select ?? _current?.Id;
        int i = _shown.FindIndex(m => m.Id == id);
        _list.SelectedIndex = i;
        if (i < 0) Show(null);
    }

    void Show(Material? m)
    {
        _current = m;
        _details.IsVisible = m is not null;
        _empty.IsVisible = m is null;
        if (m is null) return;
        _loading = true;
        _name.Text = m.Name;
        _category.SelectedItem = Material.Categories.Contains(m.Category) ? m.Category : "Other";
        _thickness.Value = (decimal)(m.ThicknessMm ?? 0);
        _reflective.IsChecked = m.HighlyReflective;
        _notes.Text = m.Notes;
        _loading = false;
        _recipes.Children.Clear();
        if (m.Recipes.Count == 0) _recipes.Children.Add(Hint("No recipes yet."));
        foreach (var r in m.Recipes.OrderBy(r => r.Purpose).ThenByDescending(r => r.Rating))
            _recipes.Children.Add(RecipeCard(m, r));
    }

    void SaveMaterialFields()
    {
        if (_loading || _current is not { } m) return;
        var name = _name.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        double t = (double)(_thickness.Value ?? 0);
        // A new name that sounds reflective (copper, brass…) ticks the box, until it's set by hand.
        bool reflective = _reflective.IsChecked == true || (name != m.Name && !m.HighlyReflective && Material.LooksReflective(name) && !Material.LooksReflective(m.Name));
        var next = m with { Name = name, Category = _category.SelectedItem as string ?? "Other", ThicknessMm = t > 0 ? t : null, Notes = _notes.Text?.Trim() ?? "", HighlyReflective = reflective };
        if (next == m) return;
        _lib.Upsert(next);
        _current = _lib.Find(m.Id);
        Changed();
        Fill(m.Id);
    }

    Control RecipeCard(Material m, Recipe r)
    {
        string who = !r.Tested ? "Untested" : r.TestedBy.Length == 0 ? $"Tested by you{(r.TestedOn is { } d ? $" · {d:d MMM yyyy}" : "")}" : $"Tested by {r.TestedBy}";
        var info = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = $"{r.Purpose} · {r.Title}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = r.Settings.Summary() + (r.FocusMm is { } f ? $" · focus {f:0.0} mm" : ""), FontSize = 12 },
                new TextBlock { Text = $"{Stars.Text(r.Rating)}  {who}", FontSize = 12, Foreground = r.Tested ? Brush("Ok") : Brush("Warn") },
            },
        };
        if (r.Notes.Length > 0) info.Children.Add(new TextBlock { Text = r.Notes, Classes = { "hint" }, TextWrapping = TextWrapping.Wrap });

        var apply = Button("Apply to layer", tip: "Use these settings on the selected layer(s).");
        apply.IsEnabled = _apply is not null;
        apply.Click += (_, _) => { _apply?.Invoke(m, r); _status.Text = $"Applied \"{r.Title}\"."; };
        var stars = new Stars { Value = r.Rating };
        stars.Changed += () => UpdateRecipe(m, r with { Rating = stars.Value });
        var tested = new CheckBox { Content = "Tested", IsChecked = r.Tested };
        tested.IsCheckedChanged += (_, _) => UpdateRecipe(m, r with { Tested = tested.IsChecked == true, TestedOn = tested.IsChecked == true ? r.TestedOn ?? DateTime.Now : null, TestedBy = tested.IsChecked == true ? r.TestedBy : "" });
        var rename = Button("Rename…");
        rename.Click += async (_, _) =>
        {
            if (await Dialogs.Prompt(this, "Recipe title", "Title:", r.Title) is { Length: > 0 } t) UpdateRecipe(m, r with { Title = t.Trim() });
        };
        var notes = Button("Notes…");
        notes.Click += async (_, _) =>
        {
            if (await Dialogs.Prompt(this, "Recipe notes", "Notes:", r.Notes) is { } n) UpdateRecipe(m, r with { Notes = n.Trim() });
        };
        var delete = Button("Delete");
        delete.Click += async (_, _) =>
        {
            if (!await Dialogs.Confirm(this, "Delete recipe", $"Delete \"{r.Title}\" from {m.Label}?", "Delete", "Keep")) return;
            _lib.DeletePhoto(r.Photo);
            _lib.Upsert(m with { Recipes = m.Recipes.RemoveAll(x => x.Id == r.Id) });
            Changed();
            Show(_lib.Find(m.Id));
            Fill(m.Id);
        };
        var actions = new WrapPanel { Children = { apply, stars, tested, rename, notes, delete } };
        foreach (var c in actions.Children) c.Margin = new(0, 4, 6, 0);
        info.Children.Add(actions);

        var row = new Grid { ColumnDefinitions = new("Auto,*") };
        if (_lib.PhotoPath(r.Photo) is { } path)
        {
            try
            {
                var img = new Image { Source = new Bitmap(path), Width = 110, Height = 110, Stretch = Stretch.UniformToFill, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(new Border { CornerRadius = new(6), ClipToBounds = true, Child = img, VerticalAlignment = VerticalAlignment.Top });
            }
            catch (Exception e) when (e is ArgumentException or IOException) { }
        }
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        return new Border { Classes = { "card" }, Child = row };
    }

    void UpdateRecipe(Material m, Recipe r)
    {
        var cur = _lib.Find(m.Id) ?? m;
        _lib.Upsert(cur with { Recipes = cur.Recipes.Select(x => x.Id == r.Id ? r with { Updated = DateTime.Now } : x).ToImmutableList() });
        Changed();
        Show(_lib.Find(m.Id));
        Fill(m.Id);
    }

    void NewMaterial()
    {
        var m = new Material { Name = "New material", Category = "Wood" };
        _lib.Upsert(m);
        Changed();
        _search.Text = "";
        _filter.SelectedIndex = 0;
        Fill(m.Id);
        _name.Focus();
        _name.SelectAll();
    }

    async Task DeleteMaterials()
    {
        var picked = _list.SelectedItems?.Cast<object>().Select(o => _list.ItemsSource is System.Collections.IList l ? l.IndexOf(o) : -1)
            .Where(i => i >= 0 && i < _shown.Count).Select(i => _shown[i]).ToList() ?? [];
        if (picked.Count == 0) return;
        var what = picked.Count == 1 ? picked[0].Label : $"{picked.Count} materials";
        if (!await Dialogs.Confirm(this, "Delete", $"Delete {what} and all their recipes and photos?", "Delete", "Keep")) return;
        foreach (var m in picked) _lib.Remove(m.Id);
        Changed();
        Fill();
    }

    async Task Import()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import materials",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("PhotonCanvas materials") { Patterns = ["*" + MaterialLibrary.ShareExtension] }],
        });
        var report = new List<string>();
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try
            {
                using var s = File.OpenRead(path);
                var r = _lib.Import(s, Path.GetFileNameWithoutExtension(path));
                report.Add($"{Path.GetFileName(path)}: {r.NewMaterials} new material(s), {r.NewRecipes} new recipe(s)" + (r.Skipped > 0 ? $", {r.Skipped} already had or unreadable" : "") + $" · from {r.From}");
            }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
            {
                report.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
        }
        if (report.Count == 0) return;
        Changed();
        Fill();
        await Dialogs.Message(this, "Import", string.Join("\n", report) +
            "\n\nOther people's recipes are marked with who tested them. Their machine and material may differ from yours: try them on a scrap first.");
    }

    async Task Export()
    {
        var picked = _list.SelectedItems?.Cast<object>().Select(o => _list.ItemsSource is System.Collections.IList l ? l.IndexOf(o) : -1)
            .Where(i => i >= 0 && i < _shown.Count).Select(i => _shown[i]).ToList() ?? [];
        var materials = picked.Count > 0 ? picked : _lib.Materials;
        if (materials.Count == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Share materials",
            SuggestedFileName = materials.Count == 1 ? materials[0].Label : "My materials",
            DefaultExtension = MaterialLibrary.ShareExtension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType("PhotonCanvas materials") { Patterns = ["*" + MaterialLibrary.ShareExtension] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            using var s = File.Create(path);
            _lib.Export(s, materials, _settings.LibraryAuthor);
            _status.Text = $"Saved {materials.Count} material(s) to {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = e.Message; }
    }
}
