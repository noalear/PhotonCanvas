using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.Doc;
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
    readonly StackPanel _tests = new() { Spacing = 6 };
    readonly TextBlock _testsHeader = new() { Text = "COLOUR AND GRATING TESTS", Classes = { "h" } };
    // Grating sets play their views; one timer moves them all.
    readonly Avalonia.Threading.DispatcherTimer _play = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly List<(Image Image, TextBlock Label, Bitmap[] Frames, string[] Names)> _players = [];
    int _tick;
    readonly Control _details;
    readonly TextBlock _empty = Hint("Pick a material on the left, or make a new one. Recipes come from Save to library on a layer, or from a material test (Materials & Tests → Tests → Record results).");
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
        _play.Tick += (_, _) => Play();
        Closed += (_, _) => { _play.Stop(); foreach (var p in _players) foreach (var f in p.Frames) f.Dispose(); };
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
        var export = Button("Export…", tip: "Save the selected materials (or all, if none is selected) to a .pcmat file to share. xTool's own settings (imported from xTool Studio) are left out: they're xTool's to give out.");
        export.Click += async (_, _) => await Export();
        var xtool = Button("Import from xTool Studio…", tip: "xTool's tested F2 Ultra settings and your own from xTool Studio, read from its folder on this computer (read only; nothing is sent).");
        xtool.Click += async (_, _) => await ImportXTool();
        var author = new TextBox { Text = settings.LibraryAuthor ?? "", Watermark = "Your name (shown to people you share with)" };
        author.LostFocus += (_, _) => { _settings.LibraryAuthor = string.IsNullOrWhiteSpace(author.Text) ? null : author.Text.Trim(); _settings.Save(); };

        var left = new DockPanel { Margin = new(0, 0, 14, 0) };
        var top = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 8), Children = { _search, _filter } };
        var bottom = new StackPanel { Spacing = 6, Margin = new(0, 8, 0, 0), Children = { Row(add, del), Row(import, export), xtool, author, _status } };
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
                _testsHeader,
                _tests,
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
        ShowTests(m);
    }

    // ---- colour palettes and grating sets ---------------------------------------------------

    void ShowTests(Material m)
    {
        foreach (var p in _players) foreach (var f in p.Frames) f.Dispose();
        _players.Clear();
        _tests.Children.Clear();
        _testsHeader.IsVisible = _tests.IsVisible = m.Palettes.Count > 0 || m.Gratings.Count > 0;
        foreach (var p in m.Palettes.OrderByDescending(p => p.Measured)) _tests.Children.Add(PaletteCard(m, p));
        foreach (var g in m.Gratings.OrderByDescending(g => g.Measured)) _tests.Children.Add(GratingCard(m, g));
        if (_players.Count > 0) _play.Start(); else _play.Stop();
    }

    void Play()
    {
        _tick++;
        foreach (var (image, label, frames, names) in _players)
        {
            int i = _tick % frames.Length;
            image.Source = frames[i];
            label.Text = names[i];
        }
    }

    Control DeleteButton(string what, Action remove)
    {
        var b = new Button { Classes = { "icon" }, Content = new PathIcon { Data = Icon("IcTrash"), Width = 13, Height = 13 }, VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(b, $"Delete this {what}");
        b.Click += async (_, _) =>
        {
            if (!await Dialogs.Confirm(this, $"Delete {what}", $"Delete this {what} from the library? This can't be undone.", "Delete", "Keep")) return;
            remove();
            _lib.Save();
            Changed();
            if (_current is { } cur) Fill(cur.Id);
        };
        return b;
    }

    Control PaletteCard(Material m, ColourPalette p)
    {
        var distinct = ColourMeasure.Distinct(p.Swatches);
        var chips = new WrapPanel();
        foreach (var s in distinct)
        {
            var (r, g, b) = PhotonCanvas.Core.Imaging.ColourScience.LabToSrgb(s.Colour);
            var chip = new Border { Width = 18, Height = 18, Margin = new(0, 0, 3, 3), CornerRadius = new(3), Background = new SolidColorBrush(Color.FromRgb(r, g, b)) };
            ToolTip.SetTip(chip, s.Settings.Summary());
            chips.Children.Add(chip);
        }
        var info = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = $"Colour palette · {p.Name}", FontWeight = FontWeight.SemiBold },
                Hint($"{p.Measured:d MMM yyyy} · {p.Swatches.Count} squares, {distinct.Count} clearly different colours" + (p.Method.Length > 0 ? $" · {p.Method}" : "") + (p.FocusMm is { } f ? $" · focus {f:0.0} mm" : "")),
                chips,
            },
        };
        var row = new DockPanel();
        var del = DeleteButton("palette", () => _lib.Upsert(m with { Palettes = m.Palettes.Remove(p) }));
        DockPanel.SetDock(del, Dock.Right);
        row.Children.Add(del);
        row.Children.Add(info);
        return new Border { Classes = { "card" }, Margin = new(0), Child = row };
    }

    Control GratingCard(Material m, GratingSet g)
    {
        // The views as an animation, from the frames saved with the set.
        var frames = new List<Bitmap>();
        var names = new List<string>();
        foreach (var v in g.Views)
            if (_lib.PhotoPath(v.Frame) is { } path)
                try { frames.Add(new Bitmap(path)); names.Add(GratingViews.Short(v.View)); }
                catch (Exception e) when (e is ArgumentException or IOException) { }
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var label = new TextBlock { Classes = { "muted" } };
        if (frames.Count > 0)
        {
            image.Source = frames[0];
            label.Text = names[0];
            _players.Add((image, label, [.. frames], [.. names]));
        }
        var best = GratingMeasure.Score(g.Views).Take(3).Select(sc =>
            $"• R{sc.Row + 1} C{sc.Col + 1} ({sc.Settings.Summary()}): {(sc.LightsUpAt.Count == 0 ? "weak everywhere" : "lights up at " + string.Join(", ", sc.LightsUpAt.Select(GratingViews.Short)))}");
        var gif = new Button { Content = "Save GIF…", IsEnabled = _lib.PhotoPath(g.Gif) is not null, HorizontalAlignment = HorizontalAlignment.Left };
        gif.Click += async (_, _) =>
        {
            if (_lib.PhotoPath(g.Gif) is not { } src) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the views as a GIF",
                DefaultExtension = "gif",
                SuggestedFileName = g.Name + ".gif",
                FileTypeChoices = [new FilePickerFileType("GIF") { Patterns = ["*.gif"] }],
            });
            if (file?.TryGetLocalPath() is { } dst) File.Copy(src, dst, overwrite: true);
        };
        var info = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = $"Gratings · {g.Name}", FontWeight = FontWeight.SemiBold },
                Hint($"{g.Measured:d MMM yyyy} · {g.Views.Count} of 5 views" + (g.FocusMm is { } f ? $" · focus {f:0.0} mm" : "") + (g.Notes.Length > 0 ? $"\n{g.Notes}" : "")),
                image,
                label,
                new TextBlock { Text = string.Join("\n", best), TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                gif,
            },
        };
        var row = new DockPanel();
        var del = DeleteButton("grating set", () =>
        {
            foreach (var file in MaterialLibrary.GratingFiles(g)) _lib.DeletePhoto(file);
            _lib.Upsert(m with { Gratings = m.Gratings.Remove(g) });
        });
        DockPanel.SetDock(del, Dock.Right);
        row.Children.Add(del);
        row.Children.Add(info);
        return new Border { Classes = { "card" }, Margin = new(0), Child = row };
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
        tested.IsCheckedChanged += (_, _) => UpdateRecipe(m, r with { Tested = tested.IsChecked == true, TestedOn = tested.IsChecked == true ? r.TestedOn ?? DateTime.Now : null, TestedBy = tested.IsChecked == true || MaterialLibrary.IsXToolPreset(r) ? r.TestedBy : "" });
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

    /// <summary>xTool Studio's settings for this machine, read from its folder (read only, presets only).</summary>
    async Task ImportXTool()
    {
        if (PhotonCanvas.Doc.Import.XToolPresets.Folder() is not { } folder)
        {
            await Dialogs.Message(this, "Import from xTool Studio", "xTool Studio isn't installed on this computer (or hasn't opened its material library yet).");
            return;
        }
        if (!await Dialogs.Confirm(this, "Import from xTool Studio",
                "This reads xTool Studio's material settings from its folder on this computer: xTool's own tested F2 Ultra settings (as xTool Studio last downloaded them) and the settings you made yourself in xTool Studio.\n\n" +
                "Only materials and settings are read. Your account, devices and projects are left alone, nothing in xTool Studio is changed, and nothing is sent anywhere.\n\n" +
                "Materials already in your library (same name and thickness) get the new settings added; settings with the same name are updated.", "Import", "Cancel")) return;
        try
        {
            var r = await Task.Run(() => PhotonCanvas.Doc.Import.XToolPresets.Read(folder, PhotonCanvas.Core.MachineProfile.F2Ultra.DeviceCode));
            var (mats, added, updated) = PhotonCanvas.Doc.Import.XToolPresets.Into(_lib, r, PhotonCanvas.Core.MachineProfile.F2Ultra.DeviceCode);
            _lib.Save();
            Changed();
            Fill();
            int yours = r.Materials.Where(m => m.Yours).Sum(m => m.Settings.Count), theirs = r.Materials.Where(m => !m.Yours).Sum(m => m.Settings.Count);
            AppLog.Write("library", $"xTool Studio import: {mats} new materials, {added} settings added, {updated} updated ({theirs} xTool's, {yours} yours); {r.LeftOut} for other work modes left out.");
            await Dialogs.Message(this, "Import from xTool Studio",
                $"{theirs} of xTool's tested settings and {yours} of your own, on {r.Materials.Count} materials: {mats} new material{(mats == 1 ? "" : "s")}, {added} setting{(added == 1 ? "" : "s")} added, {updated} updated.\n\n" +
                $"xTool's are marked \"(xTool)\". Left out: {r.LeftOut} settings for xTool's other work modes (batch, conveyor, relief) and tools PhotonCanvas doesn't have; most repeat the flat-bed ones.\n\n" +
                "Try any setting on scrap first: xTool's are for their own materials, and both lasers' settings are in (blue and MOPA).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            AppLog.Error("xTool import", e);
            await Dialogs.Message(this, "Import from xTool Studio", $"Couldn't read xTool Studio's settings: {e.Message}\n\nIf xTool Studio is open, close it and try again.");
        }
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
            var done = _lib.Export(s, materials, _settings.LibraryAuthor);
            _status.Text = $"Saved {done.Materials} material(s) to {Path.GetFileName(path)}." + (done.XToolLeftOut == 0 ? "" :
                $" {done.XToolLeftOut} of xTool's own settings were left out: they came from xTool Studio and are xTool's to share, not ours. Anyone with xTool Studio can import them with Import from xTool Studio.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = e.Message; }
    }
}
